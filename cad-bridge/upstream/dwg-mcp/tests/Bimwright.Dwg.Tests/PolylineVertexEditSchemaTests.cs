using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Geometry;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class PolylineVertexEditSchemaTests
    {
        [Fact]
        public void Actual_MCP_schema_requires_full_nested_snapshot_and_explicit_indices()
        {
            var tool = McpServerTool.Create(typeof(PolylineTools).GetMethod(nameof(PolylineTools.EditPolylineVertices)), (object)null).ProtocolTool;
            Assert.Equal("dwg_edit_polyline_vertices", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            var schema = tool.InputSchema;
            Assert.Equal(new[] { "changes", "expected_document", "expected_fingerprint" }, Required(schema));
            var change = Items(schema, schema.GetProperty("properties").GetProperty("changes"));
            Assert.Equal(new[] { "edits", "expected_constant_width", "expected_elevation", "expected_is_closed", "expected_layer",
                "expected_normal", "expected_owner_handle", "expected_thickness", "expected_vertices", "handle" }, Required(change));
            var vertex = Items(schema, change.GetProperty("properties").GetProperty("expected_vertices"));
            Assert.Equal(new[] { "bulge", "end_width", "index", "start_width", "x", "y", "z" }, Required(vertex));
            var edit = Items(schema, change.GetProperty("properties").GetProperty("edits"));
            Assert.Equal(new[] { "index", "position" }, Required(edit));
        }

        [Fact]
        public void SDK_binding_does_not_default_missing_snapshot_fields_and_roundtrips_the_wire_contract()
        {
            Assert.Throws<System.Text.Json.JsonException>(() => JsonSerializer.Deserialize<PolylineExpectedVertex>("{\"index\":0,\"x\":0,\"y\":0,\"z\":0}"));
            var wire = PolylineVertexEditTests.Request();
            var changes = JsonSerializer.Deserialize<PolylineVertexEdit[]>(wire["changes"].ToString());
            Assert.False(changes[0].expected_is_closed);
            wire["changes"] = JToken.Parse(JsonSerializer.Serialize(changes));
            Assert.True(PolylineVertexEditRequest.TryParse(wire, out _, out var error), error);
        }

        [Fact]
        public async Task Invalid_requests_are_refused_before_any_CAD_dispatch()
        {
            var result = JObject.Parse(await PolylineTools.EditPolylineVertices(@"D:\test.dwg", "not-a-guid", Array.Empty<PolylineVertexEdit>()));
            Assert.False(result.Value<bool>("ok"));
            Assert.Contains("expected_fingerprint", result.Value<string>("error"));
        }

        private static string[] Required(JsonElement schema) => schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).OrderBy(item => item).ToArray();
        private static JsonElement Items(JsonElement root, JsonElement array)
        {
            var result = array.GetProperty("items");
            if (result.TryGetProperty("$ref", out var reference))
            {
                result = root;
                foreach (var component in reference.GetString().Split('/').Skip(1))
                    result = result.GetProperty(component.Replace("~1", "/").Replace("~0", "~"));
            }
            return result;
        }
    }
}
