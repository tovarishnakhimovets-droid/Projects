using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Drawing
{
    // Only BACKGROUNDPLOT uses this scope in the native service; delegates make restoration testable without AutoCAD.
    internal sealed class ForegroundPlotScope : IDisposable
    {
        private readonly Func<int> read;
        private readonly Action<int> write;
        private readonly int original;
        private bool disposed;

        internal ForegroundPlotScope(Func<int> read, Action<int> write)
        {
            this.read = read;
            this.write = write;
            original = read();
            try
            {
                write(0);
                if (read() != 0) throw new InvalidOperationException("BACKGROUNDPLOT did not enter foreground mode");
            }
            catch
            {
                write(original);
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            write(original);
            if (read() != original) throw new InvalidOperationException("BACKGROUNDPLOT restoration was not verified");
        }
    }

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

        public static string ValidateSavedDevice(string saved, string[] available)
            => Array.Exists(available ?? Array.Empty<string>(), name => string.Equals(name, saved, StringComparison.Ordinal))
                ? null : "saved PDF device is not available exactly: " + saved;

        public static string ValidateSavedMedia(string saved, string[] available)
            => Array.Exists(available ?? Array.Empty<string>(), name => string.Equals(name, saved, StringComparison.Ordinal))
                ? null : "saved canonical media is not available exactly on the PDF device: " + saved;

        public static string ValidateUnchangedSettings(JObject saved, JObject prepared)
        {
            if (saved == null || prepared == null) return "native plot settings are unavailable; printing refused";
            foreach (var property in saved.Properties())
                if (!prepared.TryGetValue(property.Name, out var value) || !JToken.DeepEquals(property.Value, value))
                    return "native plot validation changed saved setting '" + property.Name + "'; printing refused";
            return saved.Count == prepared.Count ? null : "native plot validation changed setting fields; printing refused";
        }

        public static bool IsPdfDevice(string device)
            => !string.IsNullOrWhiteSpace(device) && device.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasPdfHeader(byte[] bytes)
            => bytes != null && bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';
    }
}
