using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Cad;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class EntityStyleTools
    {
        [McpServerTool(Name = "dwg_set_lineweight", ReadOnly = false, Idempotent = true), Description(
            "Set lineweight for 1-1000 entities identified by handles. " +
            "mode is explicit (requires lineweight_mm), by_layer, or by_block. " +
            "Millimeters must be an exact supported AutoCAD lineweight, e.g. 0.30. " +
            "All targets are checked before writing; the whole request is one transaction. " +
            "Does not change LWDISPLAY or save the drawing.")]
        public static Task<string> SetLineweight(
            [Description("Array of unique AutoCAD hexadecimal handles.")] string[] handles,
            [Description("Lineweight in millimeters, required only for mode=explicit.")] double? lineweight_mm = null,
            [Description("explicit, by_layer, or by_block.")] string mode = "explicit")
        {
            if (!ValidateNames(handles, "handles", out var error)) return InputError(error);
            var weight = lineweight_mm.HasValue ? new JValue(lineweight_mm.Value) : null;
            if (!LineweightSpec.TryParse(weight, mode, out _, out error)) return InputError(error);
            var request = new JObject { ["handles"] = new JArray(handles), ["mode"] = mode };
            if (weight != null) request["lineweight_mm"] = weight;
            return ToolGateway.LoggedCall("set_lineweight", request, request);
        }

        [McpServerTool(Name = "dwg_set_layer_lineweight", ReadOnly = false, Idempotent = true), Description(
            "Set the explicit lineweight of 1-1000 existing layers in millimeters. " +
            "For example 0.30 sets 0.30 mm. Entities using ByLayer inherit this value. " +
            "Locked, dependent, missing, or unwritable targets refuse the whole request. " +
            "All changes use one transaction; does not change LWDISPLAY or save.")]
        public static Task<string> SetLayerLineweight(
            [Description("Array of unique existing layer names.")] string[] layers,
            [Description("An exact supported AutoCAD lineweight in millimeters, e.g. 0.30.")] double lineweight_mm)
        {
            if (!ValidateNames(layers, "layers", out var error)) return InputError(error);
            if (!LineweightSpec.TryParse(new JValue(lineweight_mm), "explicit", out _, out error))
                return InputError(error);
            var request = new JObject
            {
                ["layers"] = new JArray(layers),
                ["lineweight_mm"] = lineweight_mm
            };
            return ToolGateway.LoggedCall("set_layer_lineweight", request, request);
        }

        [McpServerTool(Name = "dwg_create_hatch", ReadOnly = false, Idempotent = false), Description(
            "Create one SOLID Hatch inside a Circle or a closed planar lightweight Polyline " +
            "identified by boundary_handle in the current space. Holes/islands and other " +
            "patterns are not supported. layer must exist; defaults to the boundary layer. " +
            "Use either ACI color_index or true color_rgb [r,g,b], or omit both for ByLayer. " +
            "above_entities places the fill above existing entities and the boundary above " +
            "the fill; below_entities puts the fill at the bottom. Returns the hatch handle. " +
            "Validates before writing and creates the hatch in one transaction. Does not save.")]
        public static Task<string> CreateHatch(
            [Description("Handle of one Circle or closed lightweight Polyline in the current space.")] string boundary_handle,
            [Description("Optional existing target layer; defaults to the boundary layer.")] string layer = null,
            [Description("Optional ACI index 1-256 (256 means ByLayer). Cannot be combined with color_rgb.")] int? color_index = null,
            [Description("Optional true color [r,g,b], exactly three integers 0-255. White: [255,255,255].")] int[] color_rgb = null,
            [Description("Associate the hatch with its boundary.")] bool associative = true,
            [Description("above_entities or below_entities.")] string draw_order = "above_entities")
        {
            var request = new JObject
            {
                ["boundary_handle"] = boundary_handle,
                ["associative"] = associative,
                ["draw_order"] = draw_order
            };
            if (layer != null) request["layer"] = layer;
            if (color_index.HasValue) request["color_index"] = color_index.Value;
            if (color_rgb != null) request["color_rgb"] = new JArray(color_rgb);
            if (!CreateHatchInput.TryParse(request, out _, out var error)) return InputError(error);
            return ToolGateway.LoggedCall("create_hatch", request, request);
        }

        private static bool ValidateNames(string[] names, string field, out string error)
        {
            error = null;
            if (names == null || names.Length == 0 || names.Length > 1000 ||
                names.Any(string.IsNullOrWhiteSpace))
            {
                error = field + " must contain 1-1000 non-empty strings";
                return false;
            }
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            {
                error = field + " must not contain duplicates";
                return false;
            }
            return true;
        }

        private static Task<string> InputError(string error)
            => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
