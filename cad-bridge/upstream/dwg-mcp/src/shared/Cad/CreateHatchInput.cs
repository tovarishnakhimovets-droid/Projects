using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    /// <summary>Host-free validation shared by the MCP facade and AutoCAD handler.</summary>
    public sealed class CreateHatchInput
    {
        private CreateHatchInput() { }

        public string BoundaryHandle { get; private set; }
        public long BoundaryHandleValue { get; private set; }
        public string Layer { get; private set; }
        public int? ColorIndex { get; private set; }
        public int[] ColorRgb { get; private set; }
        public bool Associative { get; private set; }
        public string DrawOrder { get; private set; }

        public static bool TryParse(JToken parameters, out CreateHatchInput input, out string error)
        {
            input = null;
            error = null;
            if (!(parameters is JObject obj))
            {
                error = "params must be an object";
                return false;
            }

            var handleToken = obj["boundary_handle"];
            if (handleToken?.Type != JTokenType.String)
            {
                error = "boundary_handle must be a non-empty hexadecimal string";
                return false;
            }

            var handle = handleToken.Value<string>();
            if (string.IsNullOrEmpty(handle) || handle.Any(c => !Uri.IsHexDigit(c)) ||
                !long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handleValue) ||
                handleValue <= 0L)
            {
                error = "boundary_handle must be a positive hexadecimal handle in the supported range, without whitespace";
                return false;
            }

            string layer = null;
            var layerToken = obj["layer"];
            if (IsProvided(layerToken))
            {
                if (layerToken.Type != JTokenType.String || string.IsNullOrWhiteSpace(layerToken.Value<string>()))
                {
                    error = "layer must be a non-empty layer name";
                    return false;
                }
                layer = layerToken.Value<string>();
            }

            var indexToken = obj["color_index"];
            var rgbToken = obj["color_rgb"];
            if (IsProvided(indexToken) && IsProvided(rgbToken))
            {
                error = "color_index and color_rgb are mutually exclusive";
                return false;
            }

            int? colorIndex = null;
            if (IsProvided(indexToken))
            {
                if (!TryReadInteger(indexToken, 1, 256, out var index))
                {
                    error = "color_index must be an integer ACI color index between 1 and 256";
                    return false;
                }
                colorIndex = index;
            }

            int[] colorRgb = null;
            if (IsProvided(rgbToken))
            {
                if (!(rgbToken is JArray rgb) || rgb.Count != 3)
                {
                    error = "color_rgb must be an array of exactly three integers between 0 and 255";
                    return false;
                }
                colorRgb = new int[3];
                for (var i = 0; i < colorRgb.Length; i++)
                {
                    if (!TryReadInteger(rgb[i], 0, 255, out colorRgb[i]))
                    {
                        error = "color_rgb[" + i + "] must be an integer between 0 and 255";
                        return false;
                    }
                }
            }

            var associative = true;
            var associativeToken = obj["associative"];
            if (IsProvided(associativeToken))
            {
                if (associativeToken.Type != JTokenType.Boolean)
                {
                    error = "associative must be a boolean";
                    return false;
                }
                associative = associativeToken.Value<bool>();
            }

            var drawOrder = "above_entities";
            var orderToken = obj["draw_order"];
            if (IsProvided(orderToken))
            {
                if (orderToken.Type != JTokenType.String ||
                    (orderToken.Value<string>() != "above_entities" && orderToken.Value<string>() != "below_entities"))
                {
                    error = "draw_order must be above_entities or below_entities";
                    return false;
                }
                drawOrder = orderToken.Value<string>();
            }

            input = new CreateHatchInput
            {
                BoundaryHandle = handle,
                BoundaryHandleValue = handleValue,
                Layer = layer,
                ColorIndex = colorIndex,
                ColorRgb = colorRgb,
                Associative = associative,
                DrawOrder = drawOrder
            };
            return true;
        }

        private static bool IsProvided(JToken token)
            => token != null && token.Type != JTokenType.Null;

        private static bool TryReadInteger(JToken token, int minimum, int maximum, out int value)
        {
            value = 0;
            if (token?.Type != JTokenType.Integer)
                return false;
            try
            {
                var number = token.Value<long>();
                if (number < minimum || number > maximum)
                    return false;
                value = (int)number;
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return false;
            }
        }
    }
}
