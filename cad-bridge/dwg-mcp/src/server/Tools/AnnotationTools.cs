// Modified by the CAD team (2026-10-09): text/style tools; see cad-bridge/docs/TOOLS.md#text.
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class AnnotationTools
    {
        // Preserve existing C# callers; the only MCP registration lives in TextCreationTools.
        public static Task<string> CreateText(
            [Description("Text content to create.")] string text,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Optional text height.")] double? height = null,
            [Description("Optional text rotation in degrees.")] double? rotation = null,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional ACI color index for the new entity. Valid range: 1-256.")] int? color_index = null)
            => TextCreationTools.CreateText(text, position, height, rotation, layer, color_index);

        // Compatibility wrapper; drawing and annotation register TextCreationTools only once.
        public static Task<string> CreateMText(
            [Description("MText content to create.")] string text,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string location,
            [Description("Optional MText width.")] double? width = null,
            [Description("Optional text height.")] double? height = null,
            [Description("Optional text rotation in degrees.")] double? rotation = null,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional ACI color index for the new entity. Valid range: 1-256.")] int? color_index = null)
            => TextCreationTools.CreateMText(text, location, width, height, rotation, layer, color_index);

        // Compatibility helper; the sole MCP registration is in TextCreationTools.
        public static Task<string> CreateLeader(string points, string text = null,
            string layer = null, int? color_index = null)
            => TextCreationTools.CreateLeader(points, text, layer, color_index);

        [McpServerTool(Name = "dwg_create_table", ReadOnly = false, Idempotent = false), Description(
            "Create one native AutoCAD TABLE in the active drawing, in one guarded transaction. " +
            "Explicit row heights/column widths in drawing units, plain string cells, zero-based rectangular merges and cell formatting. " +
            "Existing local unlocked layer and local horizontal non-annotative text style only. " +
            "1-256 rows, 1-128 columns, at most 10000 cells/200000 characters. Fields, formulas and inline MText controls are rejected. " +
            "Merged non-anchor cells must be empty. No links, commands, new styles, implicit shrinking or saving. " +
            "dry_run checks input and native dependencies without constructing a TABLE. " +
            "Read back dimensions and cells after creation and inspect the view for clipping. Never retry an uncertain timed-out write.")]
        public static Task<string> CreateTable(
            string expected_document, string expected_fingerprint,
            TablePoint insertion_point, string[][] cells, double[] column_widths, double[] row_heights,
            string layer, string text_style, TableMergeRequest[] merges = null,
            TableCellFormatRequest[] cell_formats = null, double text_height = 2.5,
            double horizontal_margin = 0.4, double vertical_margin = 0.2,
            double rotation = 0, bool dry_run = false)
        {
            var options = new System.Text.Json.JsonSerializerOptions {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            try
            {
                var request = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(new {
                    expected_document, expected_fingerprint, insertion_point, cells, column_widths, row_heights,
                    layer, text_style, merges, cell_formats, text_height, horizontal_margin, vertical_margin, rotation, dry_run
                }, options));
                if (!Bimwright.Dwg.Plugin.Tables.TableInput.TryParse(request, out _, out var error))
                    return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
                return ToolGateway.LoggedCall("create_table", request, request);
            }
            catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.Text.Json.JsonException)
            { return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error = "invalid table input: " + ex.Message })); }
        }
    }

    public sealed class TablePoint
    {
        [System.Text.Json.Serialization.JsonRequired] public double x { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public double y { get; set; }
        public double z { get; set; }
    }
    public sealed class TableMergeRequest
    {
        [System.Text.Json.Serialization.JsonRequired] public int top { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public int left { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public int bottom { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public int right { get; set; }
    }
    public sealed class TableCellFormatRequest
    {
        [System.Text.Json.Serialization.JsonRequired] public int row { get; set; }
        [System.Text.Json.Serialization.JsonRequired] public int column { get; set; }
        public double? text_height { get; set; }
        [Description("Text angle in degrees: 0, 90, 180 or 270.")] public double? rotation { get; set; }
        [Description("middle_center, middle_left or middle_right.")] public string alignment { get; set; }
    }
}
