using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Annotation;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Required = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace Bimwright.Dwg.Server.Tools
{
    public sealed class LeaderPositionChange
    {
        [Required] public string handle { get; set; }
        [Required] public int expected_leader_index { get; set; }
        [Required] public int expected_line_index { get; set; }
        [Required] public string expected_layer { get; set; }
        [Required] public string expected_owner_handle { get; set; }
        [Required, Description("Exact native MText.Contents, including all formatting escapes.")] public string expected_text { get; set; }
        [Required] public double[] expected_text_location { get; set; }
        [Required, Description("Exactly two complete WCS [x,y,z] vertices: arrow tip then terminal.")] public double[][] expected_vertices { get; set; }
        [Required] public double[] expected_dogleg { get; set; }
        [Required, Description("Exact native MLeader.TextLocation anchor [x,y,z], which may be the right text edge; not a bounding-box corner.")] public double[] new_text_location { get; set; }
        [Required, Description("Exact last leader vertex [x,y,z], consistent with the native text anchor, dogleg and landing; incompatible targets are refused.")] public double[] new_terminal { get; set; }
        [Required, Description("left or right: horizontal dogleg direction toward the text.")] public string dogleg_side { get; set; }
    }
    [McpServerToolType]
    public sealed class LeaderTools
    {
        [McpServerTool(Name = "dwg_reposition_leaders", ReadOnly = false, Idempotent = false), Description(
            "Reposition text and the terminal of 1-100 existing plain native MLeaders, retaining the exact arrow tip. " +
            "Read fresh get_entity_properties(includeGeometry=true).reposition_snapshot first. Exact path, fingerprint, handles, owner/layer, native MText contents and geometry are guarded. " +
            "Only horizontal non-annotative MText leaders parallel to WCS XY, one leader/one straight line/two vertices, no text frame. " +
            "new_text_location is the native content anchor (possibly the right text edge), not a bounding-box corner. new_terminal is the last leader vertex, " +
            "both explicit WCS [x,y,z] in drawing units, retaining elevation. Native coupling must produce both exact targets or the transaction aborts. " +
            "dogleg_side changes its direction toward the text, preserving existing length and enable flags. " +
            "Preserves identity, text/content/height, colors, arrow and other native graphics. All targets preflight before one transaction; actual vertices/text bounds returned. " +
            "dry_run defaults true and invokes no setters. Does not save, recreate objects, route leaders or promise grouped Undo. Inspect before retry after an uncertain timeout.")]
        public static Task<string> RepositionLeaders(string expected_document, string expected_fingerprint, LeaderPositionChange[] changes, bool dry_run = true)
        {
            if (!TryBuildRequest(expected_document, expected_fingerprint, changes, dry_run, out var request, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall("reposition_leaders", request, request);
        }
        internal static bool TryBuildRequest(string document, string fingerprint, LeaderPositionChange[] changes, bool dryRun, out JObject request, out string error)
        {
            request = null; error = null;
            try
            {
                var candidate = new JObject
                {
                    ["expected_document"] = document,
                    ["expected_fingerprint"] = fingerprint,
                    ["changes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(changes)),
                    ["dry_run"] = dryRun
                };
                if (!LeaderRepositionRequest.TryParse(candidate, out _, out error)) return false;
                request = candidate; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException) { error = ex.Message; return false; }
        }
    }
}
