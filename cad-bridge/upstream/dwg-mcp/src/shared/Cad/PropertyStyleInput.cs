using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // Host-free validation is shared by the MCP facade and native handlers.
    internal static class PropertyStyleInput
    {
        internal static readonly string[] AllowedProperties =
        {
            "layer", "color", "linetype", "linetype_scale", "lineweight",
            "text_style", "height", "width_factor", "oblique"
        };

        internal static bool TryHandle(string text, out long value, out string error)
        {
            value = 0;
            error = null;
            if (string.IsNullOrEmpty(text) || text.Any(c => !Uri.IsHexDigit(c)) ||
                !long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                error = "handle must be a non-zero hexadecimal string within the supported range";
                return false;
            }
            return true;
        }

        internal static bool TryHandles(JToken token, string field, out string[] handles, out string error)
        {
            handles = null;
            error = null;
            if (!(token is JArray array) || array.Count < 1 || array.Count > 1000)
            {
                error = field + " must contain 1-1000 hexadecimal strings";
                return false;
            }
            var seen = new HashSet<long>();
            var result = new List<string>();
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String || !TryHandle(item.Value<string>(), out var value, out error))
                {
                    error = field + ": " + (error ?? "entries must be strings");
                    return false;
                }
                if (!seen.Add(value))
                {
                    error = field + " must not contain duplicate handles or aliases";
                    return false;
                }
                result.Add(item.Value<string>());
            }
            handles = result.ToArray();
            return true;
        }

        internal static bool TrySet(JObject obj, out string[] handles, out string style, out string error)
        {
            handles = null;
            style = null;
            error = null;
            if (obj == null) { error = "params must be an object"; return false; }
            if (!TryHandles(obj["handles"], "handles", out handles, out error)) return false;
            if (obj["text_style"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(obj.Value<string>("text_style")))
            {
                error = "text_style must name an existing text style";
                return false;
            }
            style = obj.Value<string>("text_style");
            return true;
        }

        internal static bool TryMatch(JObject obj, out string source, out string[] targets, out string[] properties, out string error)
        {
            source = null;
            targets = null;
            properties = null;
            error = null;
            if (obj == null) { error = "params must be an object"; return false; }
            if (obj["source_handle"]?.Type != JTokenType.String ||
                !TryHandle(obj.Value<string>("source_handle"), out var sourceValue, out error))
            {
                error = "source_handle: " + (error ?? "must be a hexadecimal string");
                return false;
            }
            source = obj.Value<string>("source_handle");
            if (!TryHandles(obj["target_handles"], "target_handles", out targets, out error)) return false;
            foreach (var target in targets)
            {
                TryHandle(target, out var value, out _);
                if (value == sourceValue) { error = "source_handle must not be a target"; return false; }
            }
            if (!(obj["properties"] is JArray list) || list.Count < 1 || list.Count > AllowedProperties.Length)
            {
                error = "properties must be a non-empty list of supported property names";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (item.Type != JTokenType.String || !AllowedProperties.Contains(item.Value<string>(), StringComparer.Ordinal))
                {
                    error = "unsupported properties entry; allowed: " + string.Join(", ", AllowedProperties);
                    return false;
                }
                if (!seen.Add(item.Value<string>())) { error = "properties must not contain duplicates"; return false; }
            }
            properties = list.Values<string>().ToArray();
            return true;
        }
    }
}
