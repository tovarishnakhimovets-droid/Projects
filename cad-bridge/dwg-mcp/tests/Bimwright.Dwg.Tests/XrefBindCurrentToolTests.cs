using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Xrefs;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;
using static Bimwright.Dwg.Tests.McpSchemaAssertions;

namespace Bimwright.Dwg.Tests
{
    public sealed class XrefBindCurrentToolTests
    {
        private const string Document = @"D:\work\surfaces.dwg";
        private const string Fingerprint = "3108b3be-79da-4435-a6da-d242ba9b2357";
        private static readonly string[] GuardFields =
        {
            "document_revision", "document_session", "graph_fingerprint", "guard_complete",
            "handle", "is_nested", "is_overlay", "name", "path", "references_fingerprint", "status"
        };

        [Fact]
        public void ActualSdkSchemaRequiresTypedRootGuardAndDefaultsToDryRun()
        {
            var method = typeof(XrefWriteTools).GetMethod(nameof(XrefWriteTools.BindCurrentXref));
            var tool = McpServerTool.Create(method, (object)null).ProtocolTool;
            Assert.Equal("dwg_bind_current_xref", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            var schema = tool.InputSchema;
            Assert.Equal(new[] { "expected_document", "expected_fingerprint", "expected_xref" }, Required(schema).OrderBy(x => x));
            var properties = schema.GetProperty("properties");
            Assert.Equal(new[] { "dry_run", "expected_document", "expected_fingerprint", "expected_xref" },
                properties.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
            AssertSchemaType(properties.GetProperty("expected_document"), "string", true);
            AssertSchemaType(properties.GetProperty("expected_fingerprint"), "string", true);
            var dryRun = properties.GetProperty("dry_run");
            AssertSchemaType(dryRun, "boolean");
            Assert.True(dryRun.GetProperty("default").GetBoolean());
            var guard = Resolve(schema, properties.GetProperty("expected_xref"));
            AssertSchemaType(guard, "object", true);
            Assert.Equal(GuardFields, Required(guard).OrderBy(x => x));
            AssertSchemaType(guard.GetProperty("properties").GetProperty("document_revision"), "integer");
            foreach (string field in new[] { "is_nested", "is_overlay", "guard_complete" })
                AssertSchemaType(guard.GetProperty("properties").GetProperty(field), "boolean");
            Assert.DoesNotContain("action", properties.EnumerateObject().Select(x => x.Name));
            var option = method.GetParameters().Single(p => p.Name == "dry_run");
            Assert.Equal(typeof(bool), option.ParameterType);
            Assert.True(option.HasDefaultValue);
            Assert.Equal(true, option.DefaultValue);
        }

        [Fact]
        public void GuardDtoRoundTripsTheNativeContractAndRejectsMissingFields()
        {
            var guard = Expected();
            var json = JsonSerializer.Serialize(guard);
            var request = new JObject
            {
                ["expected_document"] = Document, ["expected_fingerprint"] = Fingerprint,
                ["expected_xref"] = JToken.Parse(json)
            };
            Assert.True(XrefBindCurrentInput.TryParse(request, out _, out var error), error);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<XrefTargetExpectation>("{\"handle\":\"A1\"}"));
        }

        [Theory]
        [InlineData("expected_document")]
        [InlineData("expected_fingerprint")]
        [InlineData("expected_xref")]
        public void PluginSchemaRequiresEachIdentityAndScopeField(string field)
        {
            var request = ValidRequest();
            Assert.True(SchemaValidator.Validate("bind_current_xref", request, CommandSchemas.BindCurrentXref).Ok);
            request.Remove(field);
            var result = SchemaValidator.Validate("bind_current_xref", request, CommandSchemas.BindCurrentXref);
            Assert.False(result.Ok);
            Assert.Contains(field, result.Error);
        }

        [Theory]
        [InlineData("dry_run", "true")]
        [InlineData("expected_xref", "A1")]
        [InlineData("expected_document", " ")]
        [InlineData("expected_fingerprint", " ")]
        public void PluginSchemaRejectsWrongOptionTypesAndEmptyIdentity(string field, string value)
        {
            var request = ValidRequest();
            request[field] = value;
            Assert.False(SchemaValidator.Validate("bind_current_xref", request, CommandSchemas.BindCurrentXref).Ok);
        }

        [Fact]
        public void RegistrationUsesModifyProfileAndExcludesReadOnly()
        {
            var resolver = typeof(Bimwright.Dwg.Server.Program).GetMethod("ResolveToolTypesForRegistration",
                BindingFlags.NonPublic | BindingFlags.Static);
            var modify = new HashSet<string>(new[] { "modify" }, StringComparer.OrdinalIgnoreCase);
            var query = new HashSet<string>(new[] { "query" }, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(typeof(XrefWriteTools), (IEnumerable<Type>)resolver.Invoke(null, new object[] { modify, false }));
            Assert.DoesNotContain(typeof(XrefWriteTools), (IEnumerable<Type>)resolver.Invoke(null, new object[] { modify, true }));
            Assert.DoesNotContain(typeof(XrefWriteTools), (IEnumerable<Type>)resolver.Invoke(null, new object[] { query, false }));
        }

        [Theory]
        [InlineData("document")]
        [InlineData("fingerprint")]
        [InlineData("scope")]
        [InlineData("incomplete_guard")]
        public async Task InvalidWrapperInputsFailLocallyWithoutCadContact(string failure)
        {
            string document = Document, fingerprint = Fingerprint;
            var guard = Expected();
            if (failure == "document") document = null;
            if (failure == "fingerprint") fingerprint = null;
            if (failure == "scope") guard = null;
            if (failure == "incomplete_guard") guard.guard_complete = false;
            var result = JObject.Parse(await XrefWriteTools.BindCurrentXref(document, fingerprint, guard));
            Assert.False(result.Value<bool>("ok"));
            string expectedError = failure == "document"
                ? "expected_document must be a full absolute DWG path"
                : failure == "fingerprint" ? "expected_fingerprint must be a nonempty drawing GUID"
                : failure == "scope" ? "expected_xref" : "complete guard";
            Assert.Contains(expectedError, result.Value<string>("error"));
        }

        private static JObject ValidRequest() => new JObject
        {
            ["expected_document"] = Document, ["expected_fingerprint"] = Fingerprint,
            ["expected_xref"] = JObject.FromObject(Expected()), ["dry_run"] = true
        };

        private static XrefTargetExpectation Expected() => new XrefTargetExpectation
        {
            handle = "A1", name = "Surface", path = @".\surface.dwg", status = "Resolved",
            is_overlay = false, is_nested = false, graph_fingerprint = new string('a', 64),
            references_fingerprint = new string('b', 64), document_session = "bd53d445e22b49629ec21d4d170229ff",
            document_revision = 7, guard_complete = true
        };

        private static string[] Required(JsonElement schema)
            => schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();

        private static JsonElement Resolve(JsonElement root, JsonElement schema)
        {
            if (!schema.TryGetProperty("$ref", out var reference)) return schema;
            string path = reference.GetString();
            Assert.StartsWith("#/", path, StringComparison.Ordinal);
            var value = root;
            foreach (string part in path.Substring(2).Split('/'))
                value = value.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
            return value;
        }
    }
}
