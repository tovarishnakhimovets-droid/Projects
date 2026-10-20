using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Preferences;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class CrosshairQueryTools
    {
        [McpServerTool(Name = "dwg_get_crosshair_settings", ReadOnly = true, Idempotent = true), Description(
            "Read current AutoCAD profile, crosshair size (1-100), separate model/layout RGB and raw OLE colors. " +
            "Application preferences, not DWG entity colors. Does not change settings, switch spaces or save.")]
        public static Task<string> GetCrosshairSettings() => ToolGateway.LoggedCall(
            "get_crosshair_settings", new JObject(), new JObject());
    }

    [McpServerToolType]
    public sealed class CrosshairWriteTools
    {
        [McpServerTool(Name = "dwg_set_crosshair_settings", ReadOnly = false, Idempotent = true), Description(
            "Set whitelisted AutoCAD profile crosshair preferences. Read dwg_get_crosshair_settings first and pass its exact profile. " +
            "Size 100 is full-screen. Colors are integer [R,G,B], not ACI. Model and layout are independent: pass BOTH to change both. " +
            "Omitted settings remain unchanged. dry_run preflights without setters. Requires a free command line/closed Options. " +
            "Verifies actual native readback; on failure attempts restoration and reports rollback verification. " +
            "Profile settings persist outside DWG/Undo; no transaction/atomicity guarantee. Never automatically retry a timeout. No save.")]
        public static Task<string> SetCrosshairSettings(
            [Description("Exact profile returned by dwg_get_crosshair_settings.")] string expected_profile,
            [Description("Optional integer crosshair size, 1-100; 100 fills the screen.")] int? crosshair_size = null,
            [Description("Optional model-space crosshair [R,G,B], three integer components 0-255.")] int[] model_color_rgb = null,
            [Description("Optional paper-space/layout crosshair [R,G,B], independent of model color.")] int[] layout_color_rgb = null,
            [Description("Validate and return the target preferences without writing.")] bool dry_run = false)
        {
            var request = new JObject { ["expected_profile"] = expected_profile, ["dry_run"] = dry_run };
            if (crosshair_size.HasValue) request["crosshair_size"] = crosshair_size.Value;
            if (model_color_rgb != null) request["model_color_rgb"] = new JArray(model_color_rgb);
            if (layout_color_rgb != null) request["layout_color_rgb"] = new JArray(layout_color_rgb);
            if (!CrosshairSettingsInput.TryParse(request, out _, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall("set_crosshair_settings", request, request);
        }
    }
}
