// Modified by the CAD team (2026-10-07); see patches/ and docs/IMPORT_MANIFEST.json in cad-bridge.
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DrawingWriteTools
    {
        [McpServerTool(Name = "dwg_select_layout", ReadOnly = false), Description(
            "Explicitly select an existing layout in the active guarded DWG and enter paper space for a sheet. Requires exact current layout. dry_run defaults true. Changes editor view only; no new layout or drawing save.")]
        public static Task<string> SelectLayout(string expected_document, string expected_fingerprint, string layout_name,
            string expected_current_layout, bool dry_run = true)
        {
            var request = new { expected_document, expected_fingerprint, layout_name, expected_current_layout, dry_run };
            return ToolGateway.LoggedCall("select_layout", request, request);
        }

        [McpServerTool(Name = "dwg_plot_pdf", ReadOnly = false), Description(
            "Print the already-current layout to a one-page PDF using its saved PDF device, medium, scale/window and plot style. First read dwg_list_layouts and pass JSON.stringify(plot_settings) as the expected_plot_settings JSON string. dry_run defaults true. No device/media substitution or implicit layout activation. Uses foreground plotting with BACKGROUNDPLOT temporarily 0 and verified restoration, plus a hidden native progress object. Produces PDF in local Temp then publishes to the target. Does not clear DBMOD or save DWG. Existing output requires overwrite_existing=true. Synchronous native plot: a transport timeout is uncertain; inspect output before any retry.")]
        public static Task<string> PlotPdf(string expected_document, string expected_fingerprint, string layout_name,
            [Description("JSON object string copied from fresh dwg_list_layouts.plot_settings, at most 65536 characters.")] string expected_plot_settings, string output_path, bool overwrite_existing = false,
            bool allow_repo_output = false, bool dry_run = true)
        {
            if (!TryParsePlotSettings(expected_plot_settings, out var settings, out var error))
                return Task.FromResult(Newtonsoft.Json.JsonConvert.SerializeObject(new { ok = false, error }));
            var request = new JObject { ["expected_document"] = expected_document, ["expected_fingerprint"] = expected_fingerprint,
                ["layout_name"] = layout_name, ["expected_plot_settings"] = settings,
                ["output_path"] = output_path, ["overwrite_existing"] = overwrite_existing,
                ["allow_repo_output"] = allow_repo_output, ["dry_run"] = dry_run };
            return ToolGateway.LoggedCall("plot_pdf", request, request);
        }

        internal static bool TryParsePlotSettings(string json, out JObject settings, out string error)
        {
            settings = null;
            error = "expected_plot_settings must be a JSON object string with at most 65536 characters";
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return false;
            try
            {
                using var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json))
                { MaxDepth = 16, DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
                settings = JObject.Load(reader, new Newtonsoft.Json.Linq.JsonLoadSettings
                { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new Newtonsoft.Json.JsonReaderException("unexpected content after the object");
                // JSON.stringify emits integral doubles as integers. Native PlotSettings uses doubles in these arrays.
                foreach (var field in new[] { "paper_size", "origin", "window", "custom_scale" })
                    if (settings[field] is JArray values)
                        for (int i = 0; i < values.Count; i++)
                            if (values[i].Type == JTokenType.Integer) values[i] = new JValue(values[i].Value<double>());
                error = null;
                return true;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                settings = null;
                error += ": " + ex.Message;
                return false;
            }
        }

        [McpServerTool(Name = "dwg_set_system_variable", ReadOnly = false), Description(
            "Set the value of a drawing system variable. Rejects variable names not in the write allowlist. " +
            "LWDISPLAY accepts a boolean or exactly 0/1 and controls visible lineweights; does not save.")]
        public static Task<string> SetSystemVariable(
            [Description("Name of the system variable to set.")] string name,
            [Description("New value. Types are coerced automatically depending on the variable.")] JsonElement value)
        {
            var request = new JObject
            {
                ["name"] = name,
                ["value"] = JToken.Parse(value.GetRawText())
            };
            return ToolGateway.LoggedCall("set_system_variable", request, request);
        }

        [McpServerTool(Name = "dwg_save_drawing", ReadOnly = false), Description(
            "Save the current drawing. If output_path is omitted, saves the current drawing file (requires confirm=true).")]
        public static Task<string> SaveDrawing(
            [Description("Optional absolute path to save the drawing to. If specified, behaves like SaveAs.")] string output_path = null,
            [Description("Must be set to true when saving to the active drawing file without a path.")] bool? confirm = null,
            [Description("If true, allow overwriting an existing file when output_path is specified.")] bool? overwrite_existing = null,
            [Description("If true, allow saving the drawing inside the repository directory.")] bool? allow_repo_output = null,
            [Description("Optional expected full DWG path; provide together with expected_fingerprint to guard the active document.")] string expected_document = null,
            [Description("Optional drawing GUID from dwg_get_drawing_info; requires expected_document.")] string expected_fingerprint = null)
        {
            var request = new JObject();
            if (output_path != null) request["output_path"] = output_path;
            if (confirm.HasValue) request["confirm"] = confirm.Value;
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;
            if (expected_document != null) request["expected_document"] = expected_document;
            if (expected_fingerprint != null) request["expected_fingerprint"] = expected_fingerprint;

            return ToolGateway.LoggedCall("save_drawing", request, request);
        }

        [McpServerTool(Name = "dwg_purge_drawing", ReadOnly = false), Description(
            "Purge unused named objects (layers, blocks, styles) from the current drawing.")]
        public static Task<string> PurgeDrawing(
            [Description("If true, list items that would be purged without actually purging them.")] bool? dry_run = null,
            [Description("Must be set to true when dry_run is false to execute the purge.")] bool? confirm = null)
        {
            var request = new JObject();
            if (dry_run.HasValue) request["dry_run"] = dry_run.Value;
            if (confirm.HasValue) request["confirm"] = confirm.Value;

            return ToolGateway.LoggedCall("purge_drawing", request, request);
        }
    }
}
