using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Drawing
{
    // Host-free validation shared by the native handlers and offline tests.
    public static class LayoutPlotPolicy
    {
        public static string ValidateLayoutName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
                return "layout_name must contain 1..255 characters";
            foreach (char c in name) if (char.IsControl(c)) return "layout_name contains a control character";
            return null;
        }

        public static string ValidateEntityFilter(string type, int limit)
        {
            if (limit < 1 || limit > 1000) return "limit must be 1..1000";
            if (type != null && type != "DBText" && type != "MText" && type != "BlockReference" && type != "Line" && type != "Polyline")
                return "entity_type must be DBText, MText, BlockReference, Line, Polyline or null";
            return null;
        }

        public static string ValidateSnapshot(JObject expected, JObject actual)
            => expected == null || !JToken.DeepEquals(expected, actual)
                ? "plot settings changed; reread dwg_list_layouts before printing" : null;

        public static bool IsPdfDevice(string device)
            => !string.IsNullOrWhiteSpace(device) && device.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasPdfHeader(byte[] bytes)
            => bytes != null && bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';
    }
}
