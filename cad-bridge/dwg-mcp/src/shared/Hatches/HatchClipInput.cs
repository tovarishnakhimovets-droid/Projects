using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Hatches
{
    // Shared validation: no Autodesk API or implicit drawing state.
    public sealed class HatchClipInput
    {
        public string Document { get; private set; }
        public string Fingerprint { get; private set; }
        public string[] Hatches { get; private set; }
        public string[] Boundaries { get; private set; }
        public string Session { get; private set; }
        public string PlanHash { get; private set; }
        public bool DryRun { get; private set; }
        public const int MaxHatches = 20, MaxBoundaries = 32, MaxSegments = 20000, MaxLoops = 1000, MaxOutput = 2000;

        public static bool TryParse(JToken token, bool plan, out HatchClipInput input, out string error)
        {
            input = null; error = null;
            try
            {
                var o = token as JObject; Need(o != null, "parameters must be an object");
                string[] names = plan ? new[] { "expected_document", "expected_fingerprint", "hatch_handles", "boundary_handles" }
                    : new[] { "expected_document", "expected_fingerprint", "hatch_handles", "boundary_handles", "expected_session", "expected_plan_hash", "dry_run" };
                Need(o.Properties().All(p => names.Contains(p.Name)), "unknown hatch clip parameter");
                Need(Encoding.UTF8.GetByteCount(o.ToString(Formatting.None)) <= 65536, "hatch clip input exceeds 64 KiB");
                string doc = Text(o, "expected_document", 32768), fp = Text(o, "expected_fingerprint", 64);
                Need(DrawingIdentityPolicy.NormalizeDwgPath(doc) != null, "expected_document must be a fully qualified DWG path");
                Need(Guid.TryParse(fp, out var guid) && guid != Guid.Empty, "expected_fingerprint must be a nonempty GUID");
                var hatches = Handles(o["hatch_handles"], MaxHatches, "hatch_handles");
                var boundaries = Handles(o["boundary_handles"], MaxBoundaries, "boundary_handles");
                Need(!hatches.Intersect(boundaries).Any(), "hatch and boundary handles must be distinct");
                string session = null, hash = null;
                if (!plan)
                {
                    session = Text(o, "expected_session", 64);
                    Need(Guid.TryParse(session, out var sessionGuid) && sessionGuid != Guid.Empty, "expected_session must be a nonempty reader session GUID");
                    session = sessionGuid.ToString("N");
                    hash = Text(o, "expected_plan_hash", 64).ToLowerInvariant();
                    Need(hash.Length == 64 && hash.All(Uri.IsHexDigit), "expected_plan_hash must be a SHA256 reader hash");
                }
                Need(o["dry_run"] == null || o["dry_run"].Type == JTokenType.Boolean, "dry_run must be boolean");
                input = new HatchClipInput { Document = doc, Fingerprint = fp, Hatches = hatches, Boundaries = boundaries,
                    Session = session, PlanHash = hash, DryRun = plan || o.Value<bool?>("dry_run") != false };
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException || e is FormatException)
            { error = e.Message; return false; }
        }
        private static string[] Handles(JToken token, int max, string name)
        {
            var a = token as JArray; Need(a != null && a.Count >= 1 && a.Count <= max, name + " must contain 1-" + max + " handles");
            var result = a.Select(v =>
            {
                Need(v.Type == JTokenType.String, name + " entries must be strings"); string s = ((string)v).Trim();
                Need(s.Length >= 1 && s.Length <= 16 && s.All(Uri.IsHexDigit) && ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n > 0 && n <= long.MaxValue, "invalid positive hexadecimal handle");
                return ulong.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture);
            }).ToArray();
            Need(result.Distinct(StringComparer.Ordinal).Count() == result.Length, "duplicate " + name);
            return result.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }
        private static string Text(JObject o, string key, int max)
        { Need(o[key]?.Type == JTokenType.String, key + " must be a string"); string s = (string)o[key]; Need(!string.IsNullOrWhiteSpace(s) && s.Length <= max, key + " is empty or too long"); return s; }
        public static string Hash(JToken value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }
        public static void Need(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
        public static double AreaTolerance(double area) => Math.Max(1e-6, Math.Abs(area) * 1e-9);
        // Alpha is a mode-specific native getter. Inherited/invalid values have
        // no numeric alpha; never evaluate the delegate for those modes.
        public static JObject TransparencySnapshot(bool byLayer, bool byBlock, bool byAlpha, bool invalid, Func<byte> readAlpha)
        {
            Need(new[] { byLayer, byBlock, byAlpha, invalid }.Count(v => v) == 1, "ambiguous transparency mode");
            if (!byAlpha) return new JObject { ["mode"] = byLayer ? "by_layer" : byBlock ? "by_block" : "invalid" };
            Need(readAlpha != null, "numeric alpha getter is required for ByAlpha");
            return new JObject { ["mode"] = "alpha", ["alpha"] = readAlpha() };
        }
        public static string CheckArea(double source, double result)
        {
            if (double.IsNaN(source) || double.IsInfinity(source) || source <= 0 || double.IsNaN(result) || double.IsInfinity(result) || result < 0) return "nonfinite or invalid clip area";
            return result > source + AreaTolerance(source) ? "clip increased source area" : null;
        }
    }
}
