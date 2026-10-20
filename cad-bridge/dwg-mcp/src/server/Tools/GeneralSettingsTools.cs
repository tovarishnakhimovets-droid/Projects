using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Preferences;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class GeneralSettingsQueryTools
    {
        [McpServerTool(Name = "dwg_list_settings", ReadOnly = true, Idempotent = true), Description(
            "List the fixed AutoCAD application-preference catalog, types, bounds, writability, restrictions and effect timing. " +
            "Optional group: display, drafting, selection, open_save, user, system, output, files, profiles. " +
            "Static capability catalog; does not contact AutoCAD or prove native availability. Not every Options/Civil 3D setting is supported.")]
        public static Task<string> ListSettings(string group = null)
        {
            var definitions = AutoCadSettingsCatalog.All.Where(d => group == null || d.Group == group).ToArray();
            if (definitions.Length == 0) return Failure("unknown settings group; use dwg_list_settings without a group");
            return Task.FromResult(JsonConvert.SerializeObject(new
            {
                ok = true, result = new
                {
                    settings = definitions.Select(d => d.ToJson()).ToArray(),
                    native_availability = "requires dwg_get_settings in the loaded host", saved_dwg = false
                }
            }));
        }

        [McpServerTool(Name = "dwg_get_settings", ReadOnly = true, Idempotent = true), Description(
            "Read 1-32 exact IDs from dwg_list_settings in the active AutoCAD profile. Returns per-property availability and typed values. " +
            "Copy profile and value into dwg_set_settings expectations. Color value.ole_color preserves raw native bits; rgb is a display companion. " +
            "Never substitute defaults for unavailable values. No change, space switch or save.")]
        public static Task<string> GetSettings([Description("1-32 distinct IDs from dwg_list_settings.")] string[] ids)
        {
            try { AutoCadSettingsCatalog.Select(ids); }
            catch (ArgumentException ex) { return Failure(ex.Message); }
            var request = new JObject { ["ids"] = new JArray(ids) };
            return ToolGateway.LoggedCall("get_settings", request, request);
        }

        internal static Task<string> Failure(string error) => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }

    [McpServerToolType]
    public sealed class GeneralSettingsWriteTools
    {
        [McpServerTool(Name = "dwg_set_settings", ReadOnly = false, Idempotent = false), Description(
            "Change 1-32 writable application preferences from the fixed catalog. First get fresh exact profile and values with dwg_get_settings. " +
            "Each change needs id, expected_value and value, each value with exactly one typed field: boolean, integer or rgb. " +
            "Color expected_value may use raw ole_color from the getter; new colors require RGB [R,G,B], not ACI. " +
            "Preflights ALL values before setters; dry_run returns the plan without writing. Verifies stored native values, not screen refresh/restart effects. " +
            "Requires a free command line and closed Options. Preferences persist outside DWG/Undo. On failure inspect rollback_verified and reread; " +
            "restoration is best effort, not atomic. No automatic timeout retry, no profile switching or DWG save.")]
        public static Task<string> SetSettings(
            [Description("Exact active profile returned by dwg_get_settings.")] string expected_profile,
            [Description("1-32 explicit setting changes with fresh expected_value and desired value. Exactly one value field per object.")] SettingChange[] changes,
            [Description("Preflight and preview only; do not invoke any setter.")] bool dry_run = false)
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var request = new JObject
            {
                ["expected_profile"] = expected_profile, ["changes"] = changes == null ? JValue.CreateNull() : JArray.FromObject(changes, serializer),
                ["dry_run"] = dry_run
            };
            if (!GeneralSettingsInput.TryParse(request, out _, out var error)) return GeneralSettingsQueryTools.Failure(error);
            return ToolGateway.LoggedCall("set_settings", request, request);
        }
    }
}
