using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    /// <summary>
    /// Host-free wire validation. AutoCAD LineWeight stores hundredths of a millimeter.
    /// https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_LineWeight.html
    /// </summary>
    public static class LineweightSpec
    {
        private static readonly int[] ExplicitValues =
        {
            0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50,
            53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211
        };

        public static bool TryParse(JToken mm, string mode, out int value, out string error)
        {
            value = 0;
            error = null;
            mode = mode ?? "explicit";

            if (mode == "by_layer" || mode == "by_block")
            {
                // A supplied JSON null is also a conflicting lineweight_mm field.
                if (mm != null)
                {
                    error = "lineweight_mm must be omitted for by_layer or by_block mode";
                    return false;
                }

                value = mode == "by_layer" ? -1 : -2;
                return true;
            }

            if (mode != "explicit")
            {
                error = "mode must be explicit, by_layer, or by_block";
                return false;
            }

            if (mm == null || (mm.Type != JTokenType.Float && mm.Type != JTokenType.Integer))
            {
                error = "explicit mode requires a numeric lineweight_mm";
                return false;
            }

            double millimeters;
            try
            {
                millimeters = mm.Value<double>();
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                error = "lineweight_mm must be a finite supported AutoCAD value";
                return false;
            }

            if (double.IsNaN(millimeters) || double.IsInfinity(millimeters))
            {
                error = "lineweight_mm must be finite";
                return false;
            }

            // Compare against exact wire values, rather than rounding mm * 100.
            foreach (var candidate in ExplicitValues)
            {
                if (millimeters == candidate / 100d)
                {
                    value = candidate;
                    return true;
                }
            }

            error = "lineweight_mm must be one of the supported AutoCAD values; no rounding is applied";
            return false;
        }

        public static bool TryParseLayer(JToken mm, out int value, out string error)
            => TryParse(mm, "explicit", out value, out error);

        public static double? GetMillimeters(int value)
        {
            var mode = GetMode(value);
            return mode == "explicit" ? (double?)(value / 100d) : null;
        }

        public static string GetMode(int value)
        {
            switch (value)
            {
                case -1: return "by_layer";
                case -2: return "by_block";
                case -3: return "default";
                default:
                    if (Array.IndexOf(ExplicitValues, value) >= 0) return "explicit";
                    throw new ArgumentOutOfRangeException(nameof(value), "Unsupported AutoCAD lineweight");
            }
        }
    }
}
