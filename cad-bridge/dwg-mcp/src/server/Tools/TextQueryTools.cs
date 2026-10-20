using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Cad;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class TextQueryTools
    {
        [McpServerTool(Name = "dwg_list_text_styles", ReadOnly = true, Idempotent = true), Description(
            "List text styles recorded in the current drawing, including name/handle, font and big-font files, " +
            "typeface and font descriptor settings when available, fixed height, width factor and oblique angle in radians. " +
            "Returns styles, count, limit and truncated; defaults to 100 records and permits 1-1000. " +
            "These are stored style settings; they do not confirm installed/resolved fonts or MText inline overrides.")]
        public static Task<string> ListTextStyles(
            [Description("Maximum number of styles returned: integer 1-1000; default 100.")] int limit = ListTextStylesInput.DefaultLimit)
        {
            var request = new JObject { ["limit"] = limit };
            if (!ListTextStylesInput.TryParse(request, out _, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall("list_text_styles", request, request);
        }
    }
}
