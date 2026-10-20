using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class EntityStyleToolSchemaTests
    {
        [Theory]
        [InlineData(nameof(EntityStyleTools.SetLineweight), "dwg_set_lineweight", "handles")]
        [InlineData(nameof(EntityStyleTools.SetLayerLineweight), "dwg_set_layer_lineweight", "layers,lineweight_mm")]
        [InlineData(nameof(EntityStyleTools.CreateHatch), "dwg_create_hatch", "boundary_handle")]
        public void Actual_MCP_tools_have_write_metadata_and_only_mandatory_arguments(
            string methodName, string toolName, string requiredFields)
        {
            var tool = CreateTool(methodName).ProtocolTool;
            Assert.Equal(toolName, tool.Name);
            Assert.NotNull(tool.Annotations);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(
                requiredFields.Split(',').OrderBy(name => name, StringComparer.Ordinal),
                tool.InputSchema.GetProperty("required").EnumerateArray()
                    .Select(field => field.GetString()).OrderBy(name => name, StringComparer.Ordinal));
        }

        [Theory]
        [InlineData(nameof(EntityStyleTools.SetLineweight), "handles", "string")]
        [InlineData(nameof(EntityStyleTools.SetLayerLineweight), "layers", "string")]
        [InlineData(nameof(EntityStyleTools.CreateHatch), "color_rgb", "integer")]
        public void Actual_MCP_array_arguments_have_scalar_items_instead_of_recursive_JToken_schema(
            string methodName, string fieldName, string itemType)
        {
            var schema = CreateTool(methodName).ProtocolTool.InputSchema
                .GetProperty("properties").GetProperty(fieldName);
            AssertSchemaType(schema, "array");
            var items = schema.GetProperty("items");
            AssertSchemaType(items, itemType);
            Assert.False(items.TryGetProperty("$ref", out _));
        }

        [Fact]
        public void Modify_toolset_registers_entity_style_tools_and_read_only_profile_excludes_them()
        {
            var resolver = typeof(Bimwright.Dwg.Server.Program).GetMethod(
                "ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(resolver);
            var enabled = new HashSet<string>(new[] { "modify" }, StringComparer.OrdinalIgnoreCase);
            var writable = Assert.IsAssignableFrom<IEnumerable<Type>>(
                resolver.Invoke(null, new object[] { enabled, false }));
            var readOnly = Assert.IsAssignableFrom<IEnumerable<Type>>(
                resolver.Invoke(null, new object[] { enabled, true }));
            Assert.Contains(typeof(EntityStyleTools), writable);
            Assert.DoesNotContain(typeof(EntityStyleTools), readOnly);
        }

        [Theory]
        [InlineData("SetLineweight", "set_lineweight", "{\"handles\":[\"1A\",\"2B\"],\"lineweight_mm\":0.30,\"mode\":\"explicit\"}")]
        [InlineData("SetLineweight", "set_lineweight", "{\"handles\":[\"1A\"],\"mode\":\"by_layer\"}")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight", "{\"layers\":[\"A-PIPE\",\"A-HATCH\"],\"lineweight_mm\":0.30}")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight", "{\"layers\":[\"A-PIPE\"],\"lineweight_mm\":0}")]
        [InlineData("CreateHatch", "create_hatch", "{\"boundary_handle\":\"1A\"}")]
        [InlineData("CreateHatch", "create_hatch", "{\"boundary_handle\":\"1A\",\"layer\":\"A-HATCH\",\"color_index\":7,\"associative\":true}")]
        [InlineData("CreateHatch", "create_hatch", "{\"boundary_handle\":\"1A\",\"color_rgb\":[139,69,19],\"associative\":false,\"draw_order\":\"below_entities\"}")]
        public void Handler_schemas_accept_valid_parameter_shapes(string schemaName, string commandName, string json)
        {
            var result = SchemaValidator.Validate(commandName, JObject.Parse(json), GetCommandSchema(schemaName));
            Assert.True(result.Ok, result.Error);
        }

        [Theory]
        [InlineData("SetLineweight", "set_lineweight", "handles")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight", "layers")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight", "lineweight_mm")]
        [InlineData("CreateHatch", "create_hatch", "boundary_handle")]
        public void Handler_schemas_reject_missing_mandatory_fields(
            string schemaName, string commandName, string fieldName)
        {
            var parameters = ValidParameters(commandName);
            parameters.Remove(fieldName);
            var result = SchemaValidator.Validate(commandName, parameters, GetCommandSchema(schemaName));
            Assert.False(result.Ok);
            Assert.Contains(fieldName, result.Error);
        }

        [Theory]
        [InlineData("SetLineweight", "set_lineweight", "handles", "\"1A\"")]
        [InlineData("SetLineweight", "set_lineweight", "lineweight_mm", "\"0.30\"")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight", "layers", "{}")]
        [InlineData("CreateHatch", "create_hatch", "boundary_handle", "[\"1A\"]")]
        [InlineData("CreateHatch", "create_hatch", "color_rgb", "\"139,69,19\"")]
        [InlineData("CreateHatch", "create_hatch", "color_index", "7.5")]
        [InlineData("CreateHatch", "create_hatch", "associative", "\"true\"")]
        public void Handler_schemas_reject_wrong_field_token_types(
            string schemaName, string commandName, string fieldName, string invalidJson)
        {
            var parameters = ValidParameters(commandName);
            parameters[fieldName] = JToken.Parse(invalidJson);
            var result = SchemaValidator.Validate(commandName, parameters, GetCommandSchema(schemaName));
            Assert.False(result.Ok);
            Assert.Contains(fieldName, result.Error);
        }

        [Theory]
        [InlineData("SetLineweight", "set_lineweight")]
        [InlineData("SetLayerLineweight", "set_layer_lineweight")]
        [InlineData("CreateHatch", "create_hatch")]
        public void Handler_schemas_reject_array_request_roots(string schemaName, string commandName)
        {
            var result = SchemaValidator.Validate(commandName, new JArray(), GetCommandSchema(schemaName));
            Assert.False(result.Ok);
            Assert.Contains("object", result.Error);
        }

        [Theory]
        [InlineData("empty_handles", "handles")]
        [InlineData("unsupported_lineweight", "lineweight")]
        [InlineData("conflicting_colors", "color")]
        [InlineData("unknown_draw_order", "draw_order")]
        public async Task Invalid_tool_requests_return_validation_errors_before_contacting_CAD(
            string scenario, string expectedError)
        {
            Task<string> call = scenario switch
            {
                "empty_handles" => EntityStyleTools.SetLineweight(Array.Empty<string>(), 0.30),
                "unsupported_lineweight" => EntityStyleTools.SetLayerLineweight(new[] { "A-PIPE" }, 0.123),
                "conflicting_colors" => EntityStyleTools.CreateHatch("1A", color_index: 7, color_rgb: new[] { 139, 69, 19 }),
                "unknown_draw_order" => EntityStyleTools.CreateHatch("1A", draw_order: "unexpected"),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var response = JObject.Parse(await call);
            Assert.False((bool?)response["ok"]);
            Assert.Contains(expectedError, response.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
        }

        private static McpServerTool CreateTool(string methodName)
            => McpServerTool.Create(typeof(EntityStyleTools).GetMethod(methodName), (object)null);

        private static void AssertSchemaType(JsonElement schema, string expected)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String)
                Assert.Equal(expected, type.GetString());
            else
                Assert.Contains(type.EnumerateArray(), item => item.GetString() == expected);
        }

        private static CommandSchema GetCommandSchema(string schemaName)
        {
            var field = typeof(CommandSchemas).GetField(schemaName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            return Assert.IsType<CommandSchema>(field.GetValue(null));
        }

        private static JObject ValidParameters(string commandName) => commandName switch
        {
            "set_lineweight" => JObject.Parse("{\"handles\":[\"1A\"],\"lineweight_mm\":0.30}"),
            "set_layer_lineweight" => JObject.Parse("{\"layers\":[\"A-PIPE\"],\"lineweight_mm\":0.30}"),
            "create_hatch" => JObject.Parse("{\"boundary_handle\":\"1A\"}"),
            _ => throw new ArgumentOutOfRangeException(nameof(commandName))
        };
    }
}
