using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class BlockInspectionTests
    {
        private const string Fingerprint = "cf85e9e9-6928-4f15-bd8c-dc86c493b46b";

        [Fact]
        public void Minimal_explicit_handle_has_bounded_read_defaults_and_no_identity_guard()
        {
            Assert.True(BlockInspectionInput.TryParse(ValidRequest(), out var input, out var error), error);
            Assert.Equal("1D4E3", input.Handle);
            Assert.Null(input.ExpectedDocument);
            Assert.Null(input.ExpectedFingerprint);
            Assert.True(input.IncludeDefinition);
            Assert.Equal(500, input.MaxEntities);
            Assert.Equal(200, input.MaxMetadataObjects);
        }

        [Fact]
        public void Null_optional_fields_use_the_same_defaults_as_omitted_fields()
        {
            var request = ValidRequest();
            foreach (var field in new[] { "expected_document", "expected_fingerprint", "include_definition", "max_entities", "max_metadata_objects" })
                request[field] = JValue.CreateNull();
            Assert.True(BlockInspectionInput.TryParse(request, out var input, out var error), error);
            Assert.Null(input.ExpectedDocument);
            Assert.Null(input.ExpectedFingerprint);
            Assert.True(input.IncludeDefinition);
            Assert.Equal(500, input.MaxEntities);
            Assert.Equal(200, input.MaxMetadataObjects);
        }

        [Fact]
        public void Explicit_options_allow_attribute_only_reads_and_budget_boundaries()
        {
            var request = ValidRequest();
            request["include_definition"] = false;
            request["max_entities"] = 1;
            request["max_metadata_objects"] = 0;
            Assert.True(BlockInspectionInput.TryParse(request, out var input, out var error), error);
            Assert.False(input.IncludeDefinition);
            Assert.Equal(1, input.MaxEntities);
            Assert.Equal(0, input.MaxMetadataObjects);
            request["max_entities"] = 5000;
            request["max_metadata_objects"] = 1000;
            Assert.True(BlockInspectionInput.TryParse(request, out input, out error), error);
            Assert.Equal(5000, input.MaxEntities);
            Assert.Equal(1000, input.MaxMetadataObjects);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"1D4E3\"")]
        [InlineData("1")]
        [InlineData("true")]
        public void Inspection_requires_an_object_root(string json)
        {
            Assert.False(BlockInspectionInput.TryParse(JToken.Parse(json), out _, out var error));
            Assert.Contains("object", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Missing_root_or_handle_is_rejected()
        {
            Assert.False(BlockInspectionInput.TryParse(null, out _, out _));
            Assert.False(BlockInspectionInput.TryParse(new JObject(), out _, out var error));
            Assert.Contains("handle", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("0000000000000000")]
        [InlineData(" 1D4E3")]
        [InlineData("1D4E3 ")]
        [InlineData("1D4E3\n")]
        [InlineData("0x1D4E3")]
        [InlineData("-1")]
        [InlineData("+1")]
        [InlineData("G1")]
        [InlineData("10000000000000000")]
        [InlineData("8000000000000000")]
        [InlineData("FFFFFFFFFFFFFFFF")]
        public void Handle_validation_is_explicit_hex_without_whitespace_or_overflow(string handle)
        {
            var request = ValidRequest();
            request["handle"] = handle;
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("handle", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("1", 1L)]
        [InlineData("01d4e3", 0x1D4E3L)]
        [InlineData("7FFFFFFFFFFFFFFF", long.MaxValue)]
        public void Valid_handle_aliases_preserve_the_native_numeric_handle(string handle, long expected)
        {
            var request = ValidRequest();
            request["handle"] = handle;
            Assert.True(BlockInspectionInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(expected, long.Parse(input.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            Assert.Equal(input.Handle.ToUpperInvariant(), input.Handle);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("119267")]
        [InlineData("true")]
        [InlineData("[]")]
        [InlineData("{}")]
        public void A_handle_is_never_coerced_from_nonstring_JSON(string json)
        {
            var request = ValidRequest();
            request["handle"] = JToken.Parse(json);
            Assert.False(BlockInspectionInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("max_entities", "0")]
        [InlineData("max_entities", "5001")]
        [InlineData("max_entities", "1.0")]
        [InlineData("max_entities", "\"500\"")]
        [InlineData("max_entities", "true")]
        [InlineData("max_entities", "999999999999999999999999999999999999999")]
        [InlineData("max_metadata_objects", "-1")]
        [InlineData("max_metadata_objects", "1001")]
        [InlineData("max_metadata_objects", "0.0")]
        [InlineData("max_metadata_objects", "\"200\"")]
        [InlineData("max_metadata_objects", "[]")]
        public void Budgets_reject_nonintegers_and_values_outside_the_read_limits(string field, string json)
        {
            var request = ValidRequest();
            request[field] = JToken.Parse(json);
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains(field, error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("\"true\"")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("[]")]
        [InlineData("{}")]
        public void Definition_flag_accepts_only_a_JSON_boolean_or_null(string json)
        {
            var request = ValidRequest();
            request["include_definition"] = JToken.Parse(json);
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("include_definition", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("expected_document")]
        [InlineData("expected_fingerprint")]
        public void Optional_identity_must_be_paired_even_when_the_other_field_is_null(string onlyField)
        {
            var request = ValidRequest();
            request[onlyField] = onlyField == "expected_document" ? @"D:\Drawings\elbows.dwg" : Fingerprint;
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("expected_document", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expected_fingerprint", error, StringComparison.OrdinalIgnoreCase);
            request[onlyField == "expected_document" ? "expected_fingerprint" : "expected_document"] = JValue.CreateNull();
            Assert.False(BlockInspectionInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("elbows.dwg")]
        [InlineData("D:elbows.dwg")]
        [InlineData("D:\\Drawings\\template.dwt")]
        [InlineData("D:\\Drawings\\elbows.dxf")]
        [InlineData("\\\\server\\elbows.dwg")]
        public void Identity_guard_requires_an_absolute_DWG_document(string document)
        {
            var request = GuardedRequest();
            request["expected_document"] = document;
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("expected_document", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("not-a-guid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void Identity_guard_requires_a_nonempty_GUID(string fingerprint)
        {
            var request = GuardedRequest();
            request["expected_fingerprint"] = fingerprint;
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("expected_fingerprint", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("expected_document", "42")]
        [InlineData("expected_document", "true")]
        [InlineData("expected_fingerprint", "42")]
        [InlineData("expected_fingerprint", "{}")]
        public void Identity_fields_are_not_coerced_to_strings(string field, string json)
        {
            var request = GuardedRequest();
            request[field] = JToken.Parse(json);
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains(field, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Input_validation_does_not_require_the_drawing_file_to_exist_on_the_server_host()
        {
            var request = GuardedRequest();
            request["expected_document"] = @"Z:\Missing-on-this-host\elbows.dwg";
            request["expected_fingerprint"] = Fingerprint.ToUpperInvariant();
            Assert.True(BlockInspectionInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(@"Z:\Missing-on-this-host\elbows.dwg", input.ExpectedDocument);
            Assert.Equal(Guid.Parse(Fingerprint), Guid.Parse(input.ExpectedFingerprint));
        }

        [Fact]
        public void Unknown_fields_are_rejected_instead_of_changing_or_broadening_the_read_scope()
        {
            var request = ValidRequest();
            request["all_blocks"] = true;
            Assert.False(BlockInspectionInput.TryParse(request, out _, out var error));
            Assert.Contains("all_blocks", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Actual_MCP_SDK_schema_exposes_one_required_handle_and_typed_optional_read_options()
        {
            var tool = McpServerTool.Create(typeof(BlockInspectionTools).GetMethod(nameof(BlockInspectionTools.InspectBlock)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_inspect_block", tool.Name);
            Assert.True(tool.Annotations.ReadOnlyHint);
            Assert.True(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "handle" }, tool.InputSchema.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
            var properties = tool.InputSchema.GetProperty("properties");
            Assert.Equal(new[] { "expected_document", "expected_fingerprint", "handle", "include_definition", "max_entities", "max_metadata_objects" },
                properties.EnumerateObject().Select(field => field.Name).OrderBy(field => field, StringComparer.Ordinal));
            foreach (var field in new[] { "handle", "expected_document", "expected_fingerprint" })
                AssertType(properties.GetProperty(field), "string");
            AssertType(properties.GetProperty("include_definition"), "boolean");
            AssertType(properties.GetProperty("max_entities"), "integer");
            AssertType(properties.GetProperty("max_metadata_objects"), "integer");
        }

        [Fact]
        public void Query_profile_registers_the_inspection_tool_with_and_without_read_only_mode()
        {
            var resolver = typeof(Bimwright.Dwg.Server.Program).GetMethod("ResolveToolTypesForRegistration",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(resolver);
            foreach (var readOnly in new[] { false, true })
            {
                var types = (IEnumerable<Type>)resolver.Invoke(null,
                    new object[] { new HashSet<string>(new[] { "query" }, StringComparer.OrdinalIgnoreCase), readOnly });
                Assert.Single(types, type => type == typeof(BlockInspectionTools));
            }
            var modifyTypes = (IEnumerable<Type>)resolver.Invoke(null,
                new object[] { new HashSet<string>(new[] { "modify" }, StringComparer.OrdinalIgnoreCase), false });
            Assert.DoesNotContain(typeof(BlockInspectionTools), modifyTypes);
        }

        [Theory]
        [InlineData("bad_handle", "handle")]
        [InlineData("entity_budget", "max_entities")]
        [InlineData("metadata_budget", "max_metadata_objects")]
        [InlineData("only_document", "expected_fingerprint")]
        [InlineData("only_fingerprint", "expected_document")]
        [InlineData("template_document", "expected_document")]
        [InlineData("bad_fingerprint", "expected_fingerprint")]
        public async Task Invalid_requests_fail_locally_before_a_native_gateway_call(string scenario, string expectedError)
        {
            var response = JObject.Parse(await BlockInspectionTools.InspectBlock(
                scenario == "bad_handle" ? " 1D4E3" : "1D4E3",
                expected_document: scenario == "only_document" || scenario == "bad_fingerprint" ? @"D:\Drawings\elbows.dwg" :
                    scenario == "template_document" ? @"D:\Drawings\template.dwt" : null,
                expected_fingerprint: scenario == "only_fingerprint" || scenario == "template_document" ? Fingerprint :
                    scenario == "bad_fingerprint" ? "invalid" : null,
                max_entities: scenario == "entity_budget" ? 5001 : 500,
                max_metadata_objects: scenario == "metadata_budget" ? -1 : 200));
            Assert.False(response.Value<bool>("ok"));
            Assert.Contains(expectedError, response.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
        }

        private static JObject ValidRequest() => new JObject { ["handle"] = "1D4E3" };

        private static JObject GuardedRequest()
        {
            var request = ValidRequest();
            request["expected_document"] = @"D:\Drawings\elbows.dwg";
            request["expected_fingerprint"] = Fingerprint;
            return request;
        }

        private static void AssertType(JsonElement schema, string expected)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String) Assert.Equal(expected, type.GetString());
            else Assert.Contains(type.EnumerateArray(), value => value.GetString() == expected);
        }
    }
}
