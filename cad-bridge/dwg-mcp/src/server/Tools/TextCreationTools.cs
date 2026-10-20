using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Annotation;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class TextCreationTools
    {
        [McpServerTool(Name = "dwg_create_mtext", ReadOnly = false, Idempotent = false), Description(
            "Create one editable multiline MText in the current drawing space. location is a JSON WCS point " +
            "in drawing units; height defaults to 2.5 and rotation is in degrees. Optional text_style must " +
            "be an existing local horizontal non-annotative style. Width is non-negative; zero or omission " +
            "disables automatic word wrap. Text retains native MText formatting. Color defaults to ByLayer. " +
            "Does not change the current text style or save the drawing.")]
        public static Task<string> CreateMText(
            [Description("Non-empty multiline text or native MText formatted contents.")] string text,
            [Description("JSON WCS point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string location,
            [Description("Finite non-negative wrapping width in drawing units; default 0 (no wrap).")] double? width = null,
            [Description("Finite positive text height in drawing units; default 2.5.")] double? height = null,
            [Description("Finite rotation in degrees; default 0.")] double? rotation = null,
            [Description("Optional layer name; a missing layer is created in the same transaction.")] string layer = null,
            [Description("ACI index 1-256; default 256 means ByLayer.")] int? color_index = null,
            [Description("Optional existing local horizontal non-annotative text style name.")] string text_style = null)
        {
            if (!TryBuildMTextRequest(text, location, width, height, rotation, layer, color_index,
                text_style, out var request, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall("create_mtext", request, request);
        }

        internal static bool TryBuildMTextRequest(string text, string location, double? width,
            double? height, double? rotation, string layer, int? colorIndex, string textStyle,
            out JObject request, out string error)
        {
            request = null;
            error = null;
            JObject point;
            try { point = JObject.Parse(location ?? ""); }
            catch (JsonException)
            {
                error = "location must be a JSON point object with numeric x and y fields";
                return false;
            }
            var candidate = new JObject
            {
                ["text"] = text, ["location"] = point, ["color_index"] = colorIndex ?? 256
            };
            if (width.HasValue) candidate["width"] = width.Value;
            if (height.HasValue) candidate["height"] = height.Value;
            if (rotation.HasValue) candidate["rotation"] = rotation.Value;
            if (layer != null) candidate["layer"] = layer;
            if (textStyle != null) candidate["text_style"] = textStyle;
            if (!AnnotationRequestValidation.TryReadMText(candidate, out _, out error)) return false;
            request = candidate;
            return true;
        }

        [McpServerTool(Name = "dwg_create_text", ReadOnly = false, Idempotent = false), Description(
            "Create one single-line DBText in the current AutoCAD space. position is a JSON point " +
            "object with finite numeric x, y, and optional z, in WCS drawing units. Height defaults " +
            "to 2.5 and rotation is in degrees. text_style must already exist; this tool does not " +
            "create or modify styles or change the current text style. Optional width_factor is " +
            "an entity override. Color defaults to explicit ByLayer (ACI 256). Does not save.")]
        public static Task<string> CreateText(
            [Description("Non-empty single-line text content.")] string text,
            [Description("JSON WCS point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Optional finite positive text height in drawing units; default 2.5.")] double? height = null,
            [Description("Optional finite text rotation in degrees; default 0.")] double? rotation = null,
            [Description("Optional target layer name; existing creation semantics are retained.")] string layer = null,
            [Description("ACI index 1-256. Omit or use 256 for ByLayer.")] int? color_index = null,
            [Description("Optional existing local horizontal non-annotative text style name.")] string text_style = null,
            [Description("Optional finite positive entity width factor.")] double? width_factor = null)
        {
            if (!TryBuildRequest(text, position, height, rotation, layer, color_index,
                text_style, width_factor, out var request, out var error))
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
            return ToolGateway.LoggedCall("create_text", request, request);
        }

        internal static bool TryBuildRequest(string text, string position, double? height,
            double? rotation, string layer, int? colorIndex, string textStyle, double? widthFactor,
            out JObject request, out string error)
        {
            request = null;
            error = null;
            JObject point;
            try { point = JObject.Parse(position ?? ""); }
            catch (JsonException)
            {
                error = "position must be a JSON point object with numeric x and y fields";
                return false;
            }
            var candidate = new JObject
            {
                ["text"] = text,
                ["position"] = point,
                ["color_index"] = colorIndex ?? 256
            };
            if (height.HasValue) candidate["height"] = height.Value;
            if (rotation.HasValue) candidate["rotation"] = rotation.Value;
            if (layer != null) candidate["layer"] = layer;
            if (textStyle != null) candidate["text_style"] = textStyle;
            if (widthFactor.HasValue) candidate["width_factor"] = widthFactor.Value;
            if (!AnnotationRequestValidation.TryReadDbText(candidate, out _, out error)) return false;
            request = candidate;
            return true;
        }
    }
}
