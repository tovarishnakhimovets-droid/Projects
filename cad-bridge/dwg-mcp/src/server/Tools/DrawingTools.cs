using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DrawingTools
    {
        [McpServerTool(Name = "dwg_list_layouts", ReadOnly = true, Idempotent = true), Description(
            "List up to 256 layouts with saved plot settings and block-record handles. Active DWG only; exact path/fingerprint required. Does not activate a layout or save.")]
        public static Task<string> ListLayouts(string expected_document, string expected_fingerprint)
        {
            var request = new { expected_document, expected_fingerprint };
            return ToolGateway.LoggedCall("list_layouts", request, request);
        }

        [McpServerTool(Name = "dwg_get_layout_entities", ReadOnly = true, Idempotent = true), Description(
            "Read direct entities from one exact named layout, including paperspace. Optional type: DBText, MText, BlockReference, Line or Polyline. At most 1000 results / 50000 scanned / 3s. Returns complete/truncated. Does not recurse into blocks, activate the layout or save.")]
        public static Task<string> GetLayoutEntities(string expected_document, string expected_fingerprint, string layout_name,
            string entity_type = null, string layer = null, int limit = 1000, bool include_geometry = false)
        {
            var request = new { expected_document, expected_fingerprint, layout_name, entity_type, layer, limit, include_geometry };
            return ToolGateway.LoggedCall("get_layout_entities", request, request);
        }

        [McpServerTool(Name = "dwg_get_variables", ReadOnly = true, Idempotent = true), Description(
            "Read current values of an allowlist of AutoCAD drawing system variables.")]
        public static Task<string> GetVariables()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("get_variables", request, request);
        }
    }
}
