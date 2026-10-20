// Modified by the CAD team (2026-10-09): text/style tools; see cad-bridge/docs/TOOLS.md#text.
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Annotation
{
    internal static class AnnotationRequestValidation
    {
        // Shared by the MCP facade and native factory; no AutoCAD assembly is needed.
        internal static bool TryReadDbText(JObject obj, out DbTextInput input, out string error)
            => TryReadText(obj, "position", true, out input, out error);

        internal static bool TryReadMText(JObject obj, out MTextInput input, out string error)
        {
            input = null;
            if (!TryReadText(obj, "location", false, out var appearance, out error)) return false;
            if (!TryReadNumber(obj, "width", 0, false, false, out var width, out error)) return false;
            if (width < 0) { error = "width must be finite and non-negative"; return false; }
            input = new MTextInput { Appearance = appearance, Width = width };
            return true;
        }

        private static bool TryReadText(JObject obj, string pointField, bool singleLine,
            out DbTextInput input, out string error)
        {
            input = null;
            error = null;
            if (obj == null) { error = "params must be an object"; return false; }
            var textToken = obj["text"];
            if (textToken?.Type != JTokenType.String ||
                !TryValidateTextContent(textToken.Value<string>(), "text", out error))
            {
                error = error ?? "text must be a non-empty string";
                return false;
            }
            var text = textToken.Value<string>();
            if (text.IndexOf('\0') >= 0 || (singleLine && text.IndexOfAny(new[] { '\r', '\n' }) >= 0))
            {
                error = singleLine ? "text must contain a single line without null characters"
                    : "text must not contain null characters";
                return false;
            }
            if (!(obj[pointField] is JObject position))
            {
                error = pointField + " must be an object with numeric x and y fields";
                return false;
            }
            if (!TryReadNumber(position, "x", 0, true, false, out var x, out error) ||
                !TryReadNumber(position, "y", 0, true, false, out var y, out error) ||
                !TryReadNumber(position, "z", 0, false, false, out var z, out error))
            {
                error = pointField + " " + error;
                return false;
            }
            if (!TryReadNumber(obj, "height", 2.5, false, true, out var height, out error) ||
                !TryReadNumber(obj, "rotation", 0, false, false, out var rotation, out error) ||
                !TryReadNumber(obj, "width_factor", 1, false, true, out var width, out error) ||
                !TryReadOptionalName(obj, "text_style", out var textStyle, out error) ||
                !TryReadOptionalName(obj, "layer", out var layer, out error)) return false;

            var color = 256;
            var colorToken = obj["color_index"];
            if (colorToken != null && colorToken.Type != JTokenType.Null)
            {
                if (colorToken.Type != JTokenType.Integer)
                {
                    error = "color_index must be an integer ACI index between 1 and 256";
                    return false;
                }
                try
                {
                    var value = colorToken.Value<long>();
                    if (value < 1 || value > 256)
                    {
                        error = "color_index must be between 1 and 256";
                        return false;
                    }
                    color = (int)value;
                }
                catch (System.Exception ex) when (ex is System.OverflowException ||
                    ex is System.FormatException || ex is System.InvalidCastException)
                {
                    error = "color_index must be an integer ACI index between 1 and 256";
                    return false;
                }
            }
            input = new DbTextInput
            {
                Text = text, X = x, Y = y, Z = z, Height = height, RotationDegrees = rotation,
                TextStyle = textStyle, Layer = layer, ColorIndex = color,
                WidthFactor = obj["width_factor"] == null || obj["width_factor"].Type == JTokenType.Null
                    ? (double?)null : width
            };
            return true;
        }

        private static bool TryReadOptionalName(JObject obj, string field, out string value, out string error)
        {
            value = null;
            error = null;
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                error = field + " must be a non-empty string";
                return false;
            }
            value = token.Value<string>();
            return true;
        }

        private static bool TryReadNumber(JObject obj, string field, double fallback,
            bool required, bool positive, out double value, out string error)
        {
            value = fallback;
            error = null;
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (!required) return true;
                error = field + " is required";
                return false;
            }
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = field + " must be numeric";
                return false;
            }
            try { value = token.Value<double>(); }
            catch (System.Exception ex) when (ex is System.OverflowException ||
                ex is System.FormatException || ex is System.InvalidCastException)
            {
                error = field + " must be finite";
                return false;
            }
            if (double.IsNaN(value) || double.IsInfinity(value) || (positive && value <= 0))
            {
                error = field + (positive ? " must be finite and positive" : " must be finite");
                return false;
            }
            return true;
        }

        internal static bool TryValidateTextContent(string text, string fieldName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = fieldName + " must be non-empty text";
                return false;
            }

            return true;
        }

        internal static bool TryValidateLeaderPointCount(int count, out string error)
        {
            error = null;
            if (count < 2)
            {
                error = "points must contain at least 2 point objects";
                return false;
            }

            return true;
        }

        internal static bool TryValidateTableShape(int rows, int columns, JArray cells, out string error)
        {
            error = null;
            if (rows <= 0)
            {
                error = "rows must be a positive integer";
                return false;
            }

            if (columns <= 0)
            {
                error = "columns must be a positive integer";
                return false;
            }

            if (cells == null)
            {
                error = "cells must be an array of row arrays";
                return false;
            }

            if (cells.Count != rows)
            {
                error = "cells row count must equal rows";
                return false;
            }

            for (var row = 0; row < cells.Count; row++)
            {
                var cellRow = cells[row] as JArray;
                if (cellRow == null)
                {
                    error = "cells[" + row + "] must be an array";
                    return false;
                }

                if (cellRow.Count != columns)
                {
                    error = "cells[" + row + "] column count must equal columns";
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class DbTextInput
    {
        internal string Text { get; set; }
        internal double X { get; set; }
        internal double Y { get; set; }
        internal double Z { get; set; }
        internal double Height { get; set; }
        internal double RotationDegrees { get; set; }
        internal string TextStyle { get; set; }
        internal string Layer { get; set; }
        internal double? WidthFactor { get; set; }
        internal int ColorIndex { get; set; }
    }

    internal sealed class MTextInput
    {
        internal DbTextInput Appearance { get; set; }
        internal double Width { get; set; }
    }
}
