using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Geometry;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    public sealed class PolylineExpectedVertex
    {
        [System.Text.Json.Serialization.JsonRequired] public int index { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double x { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double y { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double z { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double bulge { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double start_width { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double end_width { get; set; }
    }
    public sealed class PolylineVertexPositionEdit
    {
        [System.Text.Json.Serialization.JsonRequired, Description("Existing zero-based vertex index; indices must be unique within this polyline.")]
        public int index { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("New WCS position [x,y,z] in drawing units, retaining existing elevation exactly.")]
        public double[] position { get; set; }
    }
    public sealed class PolylineVertexEdit
    {
        [System.Text.Json.Serialization.JsonRequired] public string handle { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public string expected_layer { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public string expected_owner_handle { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Full ordered vertices from fresh dwg_get_entity_properties with includeGeometry=true, without rounding.")]
        public PolylineExpectedVertex[] expected_vertices { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Expected normal [x,y,z]; initial scope requires exactly [0,0,1].")]
        public double[] expected_normal { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double expected_elevation { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double expected_thickness { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double expected_constant_width { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public bool expected_is_closed { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public PolylineVertexPositionEdit[] edits { get; set; }
    }
    [McpServerToolType]
    public sealed class PolylineTools
    {
        [McpServerTool(Name = "dwg_edit_polyline_vertices", ReadOnly = false, Idempotent = false), Description(
            "Edit existing vertex positions of 1-100 explicitly guarded current-space lightweight Polylines without erasing or recreating them. " +
            "Requires full titled DWG path/fingerprint and exact full snapshots, including ordered vertices/bulges/widths and layer/owner/normal/elevation/thickness/closed. " +
            "Up to 20000 total vertices; finite WCS coordinates bounded to +/-1e12 drawing units, normal exactly +Z, elevation unchanged. " +
            "Preserves vertex count/order, bulges, per-vertex widths, constant width, closed state, native handle, properties, ownership and XData. " +
            "Only existing index positions are edited; no insert/delete/reorder or block-definition/xref/locked-layer edits. " +
            "Preflights all targets, then one transaction with exact native readback; dry_run uses no setters. Does not save or promise a grouped Undo. " +
            "Native associations may react. A commit failure or timeout is uncertain: inspect before any retry.")]
        public static Task<string> EditPolylineVertices(string expected_document, string expected_fingerprint,
            PolylineVertexEdit[] changes, double min_segment_length = 1e-9, bool dry_run = false)
        {
            try
            {
                var request = new JObject
                {
                    ["expected_document"] = expected_document, ["expected_fingerprint"] = expected_fingerprint,
                    ["changes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(changes)),
                    ["min_segment_length"] = min_segment_length, ["dry_run"] = dry_run
                };
                if (!PolylineVertexEditRequest.TryParse(request, out _, out var error)) return InputError(error);
                return ToolGateway.LoggedCall("edit_polyline_vertices", request, request);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
            { return InputError("invalid polyline vertex edit: " + ex.Message); }
        }
        private static Task<string> InputError(string error) => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
