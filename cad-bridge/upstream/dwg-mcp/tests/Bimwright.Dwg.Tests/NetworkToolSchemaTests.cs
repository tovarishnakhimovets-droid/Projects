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
    public sealed class NetworkToolSchemaTests
    {
        [Theory]
        [InlineData(nameof(NetworkTools.NodeNetwork), "dwg_node_network", "expected_document,expected_fingerprint,sources,nodes,snap_tolerance")]
        [InlineData(nameof(NetworkTools.MoveNetworkEndpoint), "dwg_move_network_endpoint", "expected_document,expected_fingerprint,changes")]
        public void Actual_SDK_schemas_require_document_identity_and_explicit_scope(string method, string name, string required)
        {
            var tool = McpServerTool.Create(typeof(NetworkTools).GetMethod(method), (object)null).ProtocolTool;
            Assert.Equal(name, tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(required.Split(',').OrderBy(s => s),
                Required(tool.InputSchema).OrderBy(s => s));
            AssertType(tool.InputSchema.GetProperty("properties").GetProperty("dry_run"), "boolean");
            AssertType(tool.InputSchema.GetProperty("properties").GetProperty("geometry_tolerance"), "number");
        }

        [Fact]
        public void Actual_SDK_describes_every_nested_source_node_change_and_attribute_field()
        {
            var noding = Schema(nameof(NetworkTools.NodeNetwork));
            var source = Items(noding, noding.GetProperty("properties").GetProperty("sources"));
            AssertType(source, "object");
            Assert.Equal(new[] { "expected_layer", "expected_owner_handle", "expected_type", "expected_vertices", "handle" },
                Required(source).OrderBy(s => s));
            var vertices = source.GetProperty("properties").GetProperty("expected_vertices");
            AssertType(vertices, "array");
            AssertType(Items(noding, vertices), "array");
            AssertType(Items(noding, Items(noding, vertices)), "number");
            var node = Items(noding, noding.GetProperty("properties").GetProperty("nodes"));
            Assert.Equal(new[] { "expected_position", "handle" }, Required(node).OrderBy(s => s));

            var endpoint = Schema(nameof(NetworkTools.MoveNetworkEndpoint));
            var change = Items(endpoint, endpoint.GetProperty("properties").GetProperty("changes"));
            Assert.Equal(new[] { "block_handle", "endpoint", "expected_attributes", "expected_block_layer", "expected_block_name",
                    "expected_block_position", "expected_polyline_layer", "expected_vertices", "polyline_handle", "target" },
                Required(change).OrderBy(s => s));
            var attribute = Items(endpoint, change.GetProperty("properties").GetProperty("expected_attributes"));
            Assert.Equal(new[] { "alignment_point", "handle", "position", "tag", "text" }, Required(attribute).OrderBy(s => s));
            AssertType(attribute.GetProperty("properties").GetProperty("text"), "string");
        }

        [Fact]
        public void SDK_DTO_binding_requires_nested_fields_and_roundtrips_the_plugin_contract()
        {
            var wire = NetworkGeometryTests.NodingJson(new[] { new[] { 0d, 0d, 0d }, new[] { 10d, 0d, 0d } },
                new[] { new[] { 0d, 0d, 0d }, new[] { 10d, 0d, 0d } });
            var sources = System.Text.Json.JsonSerializer.Deserialize<NetworkSource[]>(wire["sources"].ToString());
            var nodes = System.Text.Json.JsonSerializer.Deserialize<NetworkNode[]>(wire["nodes"].ToString());
            Assert.Equal("A", sources[0].handle);
            wire["sources"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(sources));
            wire["nodes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(nodes));
            Assert.True(Bimwright.Dwg.Plugin.Network.NetworkRequest.TryParse(wire, true, out _, out var error), error);
            Assert.Throws<System.Text.Json.JsonException>(() =>
                System.Text.Json.JsonSerializer.Deserialize<NetworkSource[]>("[{\"handle\":\"A\"}]"));
            Assert.Throws<System.Text.Json.JsonException>(() =>
                System.Text.Json.JsonSerializer.Deserialize<NetworkAttribute>("{\"handle\":\"AA\",\"tag\":\"ID\",\"text\":\"C1\",\"position\":[0,0,0]}"));
        }

        [Fact]
        public void Network_tools_require_modify_profile_and_are_excluded_from_read_only_registration()
        {
            var resolver = typeof(Bimwright.Dwg.Server.Program).GetMethod("ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            var enabled = new HashSet<string>(new[] { "modify" }, StringComparer.OrdinalIgnoreCase);
            var writable = (IEnumerable<Type>)resolver.Invoke(null, new object[] { enabled, false });
            var readOnly = (IEnumerable<Type>)resolver.Invoke(null, new object[] { enabled, true });
            Assert.Contains(typeof(NetworkTools), writable);
            Assert.DoesNotContain(typeof(NetworkTools), readOnly);
        }

        [Fact]
        public async Task Invalid_requests_return_errors_without_contacting_CAD()
        {
            var node = JObject.Parse(await NetworkTools.NodeNetwork(@"D:\x.dwg", "11223344-1122-3344-5566-778899aabbcc",
                Array.Empty<NetworkSource>(), Array.Empty<NetworkNode>(), 0.001));
            Assert.False(node.Value<bool>("ok"));
            Assert.Contains("sources", node.Value<string>("error"));
            var endpoint = JObject.Parse(await NetworkTools.MoveNetworkEndpoint(@"D:\x.dwg", "bad-fingerprint",
                Array.Empty<NetworkEndpointChange>()));
            Assert.False(endpoint.Value<bool>("ok"));
            Assert.Contains("fingerprint", endpoint.Value<string>("error"));
        }

        [Theory]
        [InlineData("NodeNetwork", "node_network")]
        [InlineData("MoveNetworkEndpoint", "move_network_endpoint")]
        public void Plugin_schema_requires_identity_and_rejects_invalid_option_types(string schemaName, string command)
        {
            var schema = (CommandSchema)typeof(CommandSchemas).GetField(schemaName).GetValue(null);
            var request = new JObject
            {
                ["expected_document"] = @"D:\x.dwg", ["expected_fingerprint"] = "11223344-1122-3344-5566-778899aabbcc",
                ["sources"] = new JArray(), ["nodes"] = new JArray(), ["changes"] = new JArray(), ["snap_tolerance"] = 0.001
            };
            Assert.True(SchemaValidator.Validate(command, request, schema).Ok);
            request.Remove("expected_fingerprint");
            Assert.False(SchemaValidator.Validate(command, request, schema).Ok);
            request["expected_fingerprint"] = "11223344-1122-3344-5566-778899aabbcc";
            request["dry_run"] = "true";
            Assert.False(SchemaValidator.Validate(command, request, schema).Ok);
        }

        private static JsonElement Schema(string method)
            => McpServerTool.Create(typeof(NetworkTools).GetMethod(method), (object)null).ProtocolTool.InputSchema;
        private static string[] Required(JsonElement schema)
            => schema.GetProperty("required").EnumerateArray().Select(s => s.GetString()).ToArray();
        private static JsonElement Items(JsonElement root, JsonElement array)
        {
            AssertType(array, "array");
            var result = array.GetProperty("items");
            if (result.TryGetProperty("$ref", out var reference))
            {
                string[] components = reference.GetString().Split('/');
                Assert.Equal("#", components[0]);
                result = root;
                foreach (var component in components.Skip(1))
                    result = result.GetProperty(component.Replace("~1", "/").Replace("~0", "~"));
            }
            return result;
        }
        private static void AssertType(JsonElement schema, string expected)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String) Assert.Equal(expected, type.GetString());
            else Assert.Contains(type.EnumerateArray(), item => item.GetString() == expected);
        }
    }
}
