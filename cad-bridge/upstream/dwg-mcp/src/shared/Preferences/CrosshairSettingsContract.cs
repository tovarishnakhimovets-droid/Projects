using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    // Profile preferences are application state, not DWG/database properties.
    public sealed class CrosshairSettingsSnapshot
    {
        public string Profile { get; }
        public int Size { get; }
        public int ModelColor { get; }
        public int LayoutColor { get; }

        public CrosshairSettingsSnapshot(string profile, int size, int modelColor, int layoutColor)
        {
            Profile = profile; Size = size; ModelColor = modelColor; LayoutColor = layoutColor;
        }

        public bool SameAs(CrosshairSettingsSnapshot other) => other != null &&
            string.Equals(Profile, other.Profile, StringComparison.Ordinal) &&
            Size == other.Size && ModelColor == other.ModelColor && LayoutColor == other.LayoutColor;

        public JObject ToJson() => new JObject
        {
            ["profile"] = Profile,
            ["crosshair_size"] = Size,
            ["model_color_rgb"] = Rgb(ModelColor),
            ["layout_color_rgb"] = Rgb(LayoutColor),
            ["model_color_ole"] = ModelColor,
            ["layout_color_ole"] = LayoutColor,
            ["scope"] = "autocad_profile",
            ["saved_dwg"] = false
        };

        // OLE_COLOR is COLORREF (0x00BBGGRR), not an ACI or 0xRRGGBB value.
        // Preserve system-color flags for rollback; don't invent an RGB for them.
        public static JToken Rgb(int color) => ((uint)color & 0xff000000u) != 0
            ? JValue.CreateNull()
            : new JArray(color & 255, (color >> 8) & 255, (color >> 16) & 255);
    }

    public sealed class CrosshairSettingsChange
    {
        public string ExpectedProfile { get; internal set; }
        public int? Size { get; internal set; }
        public int? ModelColor { get; internal set; }
        public int? LayoutColor { get; internal set; }
        public bool DryRun { get; internal set; }

        public CrosshairSettingsSnapshot Target(CrosshairSettingsSnapshot before) =>
            new CrosshairSettingsSnapshot(before.Profile, Size ?? before.Size,
                ModelColor ?? before.ModelColor, LayoutColor ?? before.LayoutColor);
    }

    public static class CrosshairSettingsInput
    {
        private static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
        { "expected_profile", "crosshair_size", "model_color_rgb", "layout_color_rgb", "dry_run" };

        public static bool TryParse(JToken token, out CrosshairSettingsChange change, out string error)
        {
            change = null; error = null;
            if (!(token is JObject obj)) { error = "params must be an object"; return false; }
            foreach (var property in obj.Properties())
                if (!Fields.Contains(property.Name)) { error = "unsupported setting: " + property.Name; return false; }
            var profile = obj["expected_profile"];
            if (profile?.Type != JTokenType.String || string.IsNullOrWhiteSpace(profile.Value<string>()) ||
                profile.Value<string>().Length > 256)
            { error = "expected_profile must be the exact nonempty profile from dwg_get_crosshair_settings (max 256 characters)"; return false; }

            var result = new CrosshairSettingsChange { ExpectedProfile = profile.Value<string>() };
            if (obj.TryGetValue("crosshair_size", out var size))
            {
                if (!TryInteger(size, 1, 100, out var value))
                { error = "crosshair_size must be an integer from 1 to 100"; return false; }
                result.Size = value;
            }
            if (obj.TryGetValue("model_color_rgb", out var model))
            {
                if (!TryColor(model, out var value))
                { error = "model_color_rgb must contain exactly three integer RGB components from 0 to 255"; return false; }
                result.ModelColor = value;
            }
            if (obj.TryGetValue("layout_color_rgb", out var layout))
            {
                if (!TryColor(layout, out var value))
                { error = "layout_color_rgb must contain exactly three integer RGB components from 0 to 255"; return false; }
                result.LayoutColor = value;
            }
            if (obj.TryGetValue("dry_run", out var dryRun))
            {
                if (dryRun.Type != JTokenType.Boolean) { error = "dry_run must be a boolean"; return false; }
                result.DryRun = dryRun.Value<bool>();
            }
            if (!result.Size.HasValue && !result.ModelColor.HasValue && !result.LayoutColor.HasValue)
            { error = "provide crosshair_size, model_color_rgb or layout_color_rgb"; return false; }
            change = result; return true;
        }

        private static bool TryColor(JToken token, out int color)
        {
            color = 0;
            if (!(token is JArray rgb) || rgb.Count != 3) return false;
            if (!TryInteger(rgb[0], 0, 255, out var r) || !TryInteger(rgb[1], 0, 255, out var g) ||
                !TryInteger(rgb[2], 0, 255, out var b)) return false;
            color = r | (g << 8) | (b << 16); return true;
        }

        private static bool TryInteger(JToken token, int min, int max, out int value)
        {
            value = 0;
            if (token?.Type != JTokenType.Integer) return false;
            try
            {
                long number = token.Value<long>();
                if (number < min || number > max) return false;
                value = (int)number; return true;
            }
            catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException) { return false; }
        }
    }

    public interface ICrosshairPreferences
    {
        CrosshairSettingsSnapshot Read();
        void SetSize(int value);
        void SetModelColor(int value);
        void SetLayoutColor(int value);
    }
}
