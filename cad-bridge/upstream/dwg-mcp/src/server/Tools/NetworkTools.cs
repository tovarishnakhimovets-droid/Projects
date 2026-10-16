using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Network;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace Bimwright.Dwg.Server.Tools
{
    public sealed class NetworkSource
    {
        [JsonRequired, Description("Unique hexadecimal source handle.")] public string handle { get; set; }
        [JsonRequired, Description("Line or Polyline; only straight open paths parallel to WCS XY.")] public string expected_type { get; set; }
        [JsonRequired, Description("Exact existing source layer name.")] public string expected_layer { get; set; }
        [JsonRequired, Description("Existing owning layout BlockTableRecord handle.")] public string expected_owner_handle { get; set; }
        [JsonRequired, Description("Complete ordered WCS vertices, each exactly [x,y,z].")] public double[][] expected_vertices { get; set; }
    }

    public sealed class NetworkNode
    {
        [JsonRequired, Description("Existing Point or BlockReference handle; no node is moved or created.")] public string handle { get; set; }
        [JsonRequired, Description("Expected WCS insertion/point location [x,y,z].")] public double[] expected_position { get; set; }
    }

    public sealed class NetworkAttribute
    {
        [JsonRequired, Description("Existing attached AttributeReference handle.")] public string handle { get; set; }
        [JsonRequired, Description("Exact existing attribute tag.")] public string tag { get; set; }
        [JsonRequired, Description("Exact existing text, including an empty value.")] public string text { get; set; }
        [JsonRequired, Description("Existing WCS attribute position [x,y,z].")] public double[] position { get; set; }
        [JsonRequired, Description("Existing WCS alignment point [x,y,z], including for left baseline attributes.")] public double[] alignment_point { get; set; }
    }

    public sealed class NetworkEndpointChange
    {
        [JsonRequired, Description("Existing open lightweight Polyline handle.")] public string polyline_handle { get; set; }
        [JsonRequired, Description("Existing simple BlockReference handle in the same current layout space.")] public string block_handle { get; set; }
        [JsonRequired, Description("start or end; other vertices remain unchanged.")] public string endpoint { get; set; }
        [JsonRequired, Description("Complete ordered WCS polyline vertices, each [x,y,z].")] public double[][] expected_vertices { get; set; }
        [JsonRequired, Description("Expected WCS block insertion [x,y,z], already exactly coincident with the endpoint.")] public double[] expected_block_position { get; set; }
        [JsonRequired, Description("New exact common endpoint/insertion [x,y,z], retaining the existing elevation.")] public double[] target { get; set; }
        [JsonRequired, Description("Exact existing polyline layer.")] public string expected_polyline_layer { get; set; }
        [JsonRequired, Description("Exact existing block layer.")] public string expected_block_layer { get; set; }
        [JsonRequired, Description("Exact existing block definition name.")] public string expected_block_name { get; set; }
        [JsonRequired, Description("Complete attached attribute list, or [] when none; single line attributes only.")] public NetworkAttribute[] expected_attributes { get; set; }
    }

    [McpServerToolType]
    public sealed class NetworkTools
    {
        [McpServerTool(Name = "dwg_node_network", ReadOnly = false, Idempotent = false), Description(
            "Split and snap 1-1000 explicitly guarded Line/open lightweight Polyline sources at 1-2000 existing nodes. " +
            "Both ends of every source require a supplied node within snap_tolerance; all supplied interior nodes are cut. " +
            "Coordinates and tolerances are drawing units in WCS. Straight paths parallel to XY only, no widths, thickness or bulges. " +
            "Preserves other bends, native graphics and layout ownership; first piece retains its source handle. " +
            "Refuses ambiguous/coincident nodes and splitting linked XData, dictionaries or persistent associations. " +
            "Split/snap only: does not merge sources or create/move nodes. Scope is limited to 20000 vertices and one million vertices*nodes. " +
            "Checks the expected full titled DWG path, fingerprint, all objects and complete geometry before one atomic transaction. " +
            "dry_run plans without any writes. Returns actual split handles and exact endpoint validation. Does not save. " +
            "An uncertain timed-out mutation must not be repeated automatically.")]
        public static Task<string> NodeNetwork(
            [Description("Expected full path of the active titled DWG, from dwg_get_drawing_info.document_path.")] string expected_document,
            [Description("Expected database fingerprint GUID from dwg_get_drawing_info.fingerprint.")] string expected_fingerprint,
            [Description("Explicit source snapshots including owner, layer, type and all vertices.")] NetworkSource[] sources,
            [Description("Explicit existing node handles and expected locations.")] NetworkNode[] nodes,
            [Description("Maximum distance from a selected source to its authoritative existing node, in drawing units.")] double snap_tolerance,
            [Description("Snapshot comparison tolerance; positive and less than min_segment_length.")] double geometry_tolerance = 1e-9,
            [Description("Minimum allowed length of every resulting straight segment, in drawing units.")] double min_segment_length = 1e-6,
            [Description("True returns the validated plan without creating objects or editing geometry.")] bool dry_run = false)
            => Call("node_network", true, expected_document, expected_fingerprint, sources, nodes, null,
                snap_tolerance, geometry_tolerance, min_segment_length, dry_run);

        [McpServerTool(Name = "dwg_move_network_endpoint", ReadOnly = false, Idempotent = false), Description(
            "Atomically edit 1-1000 explicitly guarded open lightweight Polyline endpoints and translate their existing consumer blocks " +
            "with every attached attribute. The old endpoint must already coincide exactly with the block insertion. " +
            "The final endpoint and insertion equal target exactly; all other vertices and attribute texts remain unchanged. " +
            "WCS coordinates in drawing units; +Z planar straight zero-width polylines only, retaining elevation. " +
            "Refuses xrefs, dynamic/annotative or non +Z blocks, multiline/annotative attributes, locked layers, changed snapshots and short segments. " +
            "Complete attribute handles/texts/positions must match. Checks full titled DWG path and fingerprint, then uses one transaction. " +
            "dry_run performs no setters or transforms. Does not save. An uncertain timed-out mutation must not be repeated automatically.")]
        public static Task<string> MoveNetworkEndpoint(
            [Description("Expected full path of the active titled DWG, from dwg_get_drawing_info.document_path.")] string expected_document,
            [Description("Expected database fingerprint GUID from dwg_get_drawing_info.fingerprint.")] string expected_fingerprint,
            [Description("Explicit endpoint/block snapshots with complete attached attribute lists and targets.")] NetworkEndpointChange[] changes,
            [Description("Snapshot and attribute comparison tolerance; positive and less than min_segment_length.")] double geometry_tolerance = 1e-9,
            [Description("Minimum allowed resulting straight segment length, in drawing units.")] double min_segment_length = 1e-6,
            [Description("True validates and returns proposed positions without any writes or transforms.")] bool dry_run = false)
            => Call("move_network_endpoint", false, expected_document, expected_fingerprint, null, null, changes,
                0, geometry_tolerance, min_segment_length, dry_run);

        private static Task<string> Call(string command, bool noding, string document, string fingerprint,
            NetworkSource[] sources, NetworkNode[] nodes, NetworkEndpointChange[] changes,
            double snap, double geometry, double minimum, bool dryRun)
        {
            try
            {
                var request = new JObject
                {
                    ["expected_document"] = document, ["expected_fingerprint"] = fingerprint,
                    ["geometry_tolerance"] = geometry, ["min_segment_length"] = minimum, ["dry_run"] = dryRun
                };
                // Use the same System.Text.Json property names as the SDK schema and argument binding.
                if (noding)
                {
                    request["sources"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(sources));
                    request["nodes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(nodes));
                    request["snap_tolerance"] = snap;
                }
                else request["changes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(changes));
                if (!NetworkRequest.TryParse(request, noding, out _, out var error)) return InputError(error);
                return ToolGateway.LoggedCall(command, request, request);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
            {
                return InputError("invalid network request: " + ex.Message);
            }
        }

        private static Task<string> InputError(string error)
            => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
