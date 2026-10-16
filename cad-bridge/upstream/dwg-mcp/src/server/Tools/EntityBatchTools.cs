using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.EntityBatch;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace Bimwright.Dwg.Server.Tools
{
    public sealed class EntityBatchPoint
    {
        [JsonRequired] public double x { get; set; }
        [JsonRequired] public double y { get; set; }
        public double z { get; set; }
    }
    public sealed class EntityBatchEntity
    {
        [JsonRequired, Description("Unique case-sensitive local ID, 1-64 ASCII letters/digits/_/-. Not a DWG handle.")] public string id { get; set; }
        [JsonRequired, Description("line,circle,point,polyline,rectangle,arc,ellipse,text,mtext,hatch. Only fields belonging to that type are accepted.")] public string type { get; set; }
        [JsonRequired, Description("Exact existing local unlocked target layer. No layers are created.")] public string layer { get; set; }
        public int? color_index { get; set; }
        [Description("Alternative RGB color: three integers 0-255.")] public int[] color_rgb { get; set; }
        [Description("Optional exact supported AutoCAD millimeter weight. Omit for ByLayer.")] public double? lineweight_mm { get; set; }
        [Description("Line start WCS point.")] public EntityBatchPoint start { get; set; }
        [Description("Line end WCS point.")] public EntityBatchPoint end { get; set; }
        [Description("Circle, arc or ellipse WCS center.")] public EntityBatchPoint center { get; set; }
        [Description("Point/TEXT/MTEXT position; rectangle lower-left local origin, in WCS drawing units.")] public EntityBatchPoint position { get; set; }
        public double? radius { get; set; }
        [Description("Straight lightweight polyline vertices, 2-512. One exact WCS elevation; no repeated closing vertex.")] public EntityBatchPoint[] points { get; set; }
        public bool? closed { get; set; }
        [Description("Positive rectangle width or MTEXT wrapping width. MTEXT default 100 drawing units.")] public double? width { get; set; }
        [Description("Positive rectangle height or text height. Text default 2.5 drawing units.")] public double? height { get; set; }
        [Description("Rectangle, ellipse or text rotation in degrees.")] public double? rotation { get; set; }
        [Description("Arc degrees: 0 <= start_angle < end_angle <= 360.")] public double? start_angle { get; set; }
        public double? end_angle { get; set; }
        [Description("Full ellipse major radius in drawing units.")] public double? major_radius { get; set; }
        [Description("Full ellipse minor radius, <= major_radius; ratio at least 0.000001.")] public double? minor_radius { get; set; }
        [Description("TEXT single-line or MTEXT contents, <=16384 characters, no fields.")] public string text { get; set; }
        [Description("Required for TEXT/MTEXT: existing local horizontal non-annotative style.")] public string text_style { get; set; }
        [Description("TEXT positive width factor; default 1.")] public double? width_factor { get; set; }
        [Description("SOLID hatch boundary: earlier local ID of circle, rectangle or simple closed polyline.")] public string boundary_id { get; set; }
        [Description("Hatch boundary association; default true.")] public bool? associative { get; set; }
    }

    [McpServerToolType]
    public sealed class EntityBatchTools
    {
        [McpServerTool(Name = "dwg_create_entities", ReadOnly = false, Idempotent = false), Description(
            "Create 1-2000 heterogeneous whitelisted entities as one guarded operation and one native transaction. " +
            "No command strings, code, arbitrary setters, existing-entity edits or new layers/styles. Explicit existing layer on every entity. " +
            "WCS drawing units, +Z planar curves, straight zero-width polylines, full ellipses, field-free TEXT/MTEXT and SOLID hatch only. " +
            "Hatches reference earlier local boundary IDs and put their boundary above the fill. Validates the entire plan, document identity, " +
            "layers/styles and Undo state before writes; dry_run performs no entity construction or writes. " +
            "Limits: 2 MiB UTF-8 input, 20000 total polyline/rectangle vertices, 15-second cooperative native deadline. " +
            "Returns local ID to handle results. Uses the invocation's single document lock/Undo bracket; single-U behavior requires live verification. " +
            "Undo must be enabled, with no active group or Control One. Does not save; never retry an uncertain timed-out mutation.")]
        public static Task<string> CreateEntities(string expected_document, string expected_fingerprint,
            EntityBatchEntity[] entities, bool dry_run = false)
        {
            try
            {
                var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                var request = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(
                    new { expected_document, expected_fingerprint, entities, dry_run }, options));
                if (!EntityBatchInput.TryParse(request, out _, out var error)) return Error(error);
                return ToolGateway.LoggedCall("create_entities", request, request);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
            { return Error("invalid entity batch: " + ex.Message); }
        }
        private static Task<string> Error(string error) => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
