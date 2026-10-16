using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.EntityBatch
{
    // Closed host-free creation plan, never a command dispatcher.
    public sealed class EntityBatchInput
    {
        public const int MaxEntities = 2000, MaxVertices = 20000, MaxInputBytes = 2 * 1024 * 1024;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public bool DryRun { get; private set; }
        public EntityBatchItemInput[] Items { get; private set; }

        public static bool TryParse(JToken token, out EntityBatchInput input, out string error)
        {
            input = null; error = null;
            try
            {
                var root = token as JObject; Need(root != null, "params must be an object");
                Keys(root, "expected_document", "expected_fingerprint", "entities", "dry_run");
                Need(Encoding.UTF8.GetByteCount(root.ToString(Formatting.None)) <= MaxInputBytes, "batch input exceeds 2 MiB UTF-8");
                var document = Text(root, "expected_document", 32768);
                Need(DrawingIdentityPolicy.NormalizeDwgPath(document) != null, "expected_document must be a fully qualified DWG path");
                var fingerprint = Text(root, "expected_fingerprint", 64);
                Need(Guid.TryParse(fingerprint, out var guid) && guid != Guid.Empty, "expected_fingerprint must be a nonempty drawing GUID");
                var entries = root["entities"] as JArray;
                Need(entries != null && entries.Count >= 1 && entries.Count <= MaxEntities, "entities must contain 1-2000 objects");
                var items = new List<EntityBatchItemInput>();
                var ids = new Dictionary<string, EntityBatchItemInput>(StringComparer.Ordinal);
                int vertices = 0, intersections = 0;
                foreach (var entry in entries)
                {
                    var obj = entry as JObject; Need(obj != null, "every entity must be an object");
                    string id = Text(obj, "id", 64);
                    Need(id.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-'), "id must use ASCII letters, digits, underscore or hyphen");
                    Need(!ids.ContainsKey(id), "duplicate entity id: " + id);
                    string type = Text(obj, "type", 16).ToLowerInvariant();
                    var allowed = new List<string> { "id", "type", "layer", "color_index", "color_rgb", "lineweight_mm" };
                    switch (type)
                    {
                        case "line":
                            allowed.AddRange(new[] { "start", "end" }); Point(obj["start"]); Point(obj["end"]);
                            Need(Distance(obj["start"], obj["end"]) > 1e-9, "line endpoints must be distinct"); break;
                        case "circle":
                            allowed.AddRange(new[] { "center", "radius" }); Point(obj["center"]); Positive(obj, "radius"); break;
                        case "point": allowed.Add("position"); Point(obj["position"]); break;
                        case "polyline":
                            allowed.AddRange(new[] { "points", "closed" });
                            var points = obj["points"] as JArray;
                            Need(points != null && points.Count >= 2 && points.Count <= 512, "polyline points must contain 2-512 objects");
                            bool closed = Boolean(obj, "closed", false);
                            Need(!closed || points.Count >= 3, "closed polyline requires at least three vertices");
                            foreach (var p in points) Point(p);
                            double elevation = Coordinate(points[0], "z");
                            for (int i = 0; i < points.Count; i++)
                            {
                                Need(Coordinate(points[i], "z") == elevation, "polyline vertices require one exact WCS elevation");
                                if (i > 0) Need(Distance(points[i - 1], points[i]) > 1e-9, "polyline has a zero-length segment");
                            }
                            if (closed) Need(Distance(points[points.Count - 1], points[0]) > 1e-9, "omit repeated closing vertex");
                            vertices += points.Count; break;
                        case "rectangle":
                            allowed.AddRange(new[] { "position", "width", "height", "rotation" });
                            Point(obj["position"]); Positive(obj, "width"); Positive(obj, "height"); OptionalNumber(obj, "rotation", 0); vertices += 4; break;
                        case "arc":
                            allowed.AddRange(new[] { "center", "radius", "start_angle", "end_angle" });
                            Point(obj["center"]); Positive(obj, "radius");
                            var first = Number(obj["start_angle"], "start_angle"); var last = Number(obj["end_angle"], "end_angle");
                            Need(first >= 0 && last > first && last <= 360 && last - first < 360, "arc requires 0 <= start_angle < end_angle <= 360 and sweep < 360 degrees"); break;
                        case "ellipse":
                            allowed.AddRange(new[] { "center", "major_radius", "minor_radius", "rotation" });
                            Point(obj["center"]); var major = Positive(obj, "major_radius"); var minor = Positive(obj, "minor_radius");
                            Need(minor <= major && minor / major >= 1e-6, "ellipse requires 0.000001 <= minor_radius/major_radius <= 1");
                            OptionalNumber(obj, "rotation", 0); break;
                        case "text": case "mtext":
                            allowed.AddRange(new[] { "position", "text", "text_style", "height", "rotation" });
                            if (type == "text") allowed.Add("width_factor"); else allowed.Add("width");
                            Point(obj["position"]); var content = Text(obj, "text", 16384);
                            Need(!content.Contains("%<") && content.IndexOf('\0') < 0, "fields and null characters are not supported");
                            Need(type != "text" || content.IndexOfAny(new[] { '\r', '\n' }) < 0, "TEXT requires single-line content");
                            Text(obj, "text_style", 255); PositiveOptional(obj, "height", 2.5); OptionalNumber(obj, "rotation", 0);
                            if (type == "text") PositiveOptional(obj, "width_factor", 1); else PositiveOptional(obj, "width", 100); break;
                        case "hatch":
                            allowed.AddRange(new[] { "boundary_id", "associative" }); string boundaryId = Text(obj, "boundary_id", 64);
                            Need(ids.TryGetValue(boundaryId, out var boundary), "hatch boundary_id must name an earlier entity");
                            Need(boundary.Type == "circle" || boundary.Type == "rectangle" || boundary.Type == "polyline" && Boolean(boundary.Parameters, "closed", false), "hatch boundary must be circle, rectangle or closed polyline");
                            if (boundary.Type == "polyline") CheckSimpleBoundary((JArray)boundary.Parameters["points"], ref intersections);
                            Boolean(obj, "associative", true); break;
                        default: throw new ArgumentException("unsupported entity type: " + type);
                    }
                    Keys(obj, allowed.ToArray()); Need(vertices <= MaxVertices, "batch exceeds 20000 total polyline/rectangle vertices");
                    string layer = Text(obj, "layer", 255); Color(obj);
                    if (Present(obj, "lineweight_mm")) Need(LineweightSpec.TryParse(obj["lineweight_mm"], "explicit", out _, out var weightError), weightError);
                    var item = new EntityBatchItemInput(id, type, layer, (JObject)obj.DeepClone()); item.Parameters["type"] = type;
                    items.Add(item); ids.Add(id, item);
                }
                input = new EntityBatchInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                    DryRun = Boolean(root, "dry_run", false), Items = items.ToArray() }; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is FormatException || ex is InvalidCastException)
            { error = ex.Message; return false; }
        }

        public static string ValidateUndoState(bool recording, int undoControl)
        {
            if (!recording || (undoControl & 1) == 0) return "Undo recording must be enabled before batch creation";
            if ((undoControl & 2) != 0) return "UNDO Control One is outside the batch Undo contract";
            if ((undoControl & 8) != 0) return "finish the existing Undo group before batch creation";
            return null;
        }
        private static void Color(JObject obj)
        {
            Need(!Present(obj, "color_index") || !Present(obj, "color_rgb"), "use color_index or color_rgb, not both");
            if (Present(obj, "color_index"))
            { var n = obj["color_index"]; Need(n.Type == JTokenType.Integer && Number(n, "color_index") >= 1 && Number(n, "color_index") <= 256, "color_index must be integer 1-256"); }
            if (Present(obj, "color_rgb"))
            {
                var rgb = obj["color_rgb"] as JArray; Need(rgb != null && rgb.Count == 3, "color_rgb requires three integers");
                foreach (var n in rgb) Need(n.Type == JTokenType.Integer && Number(n, "color_rgb") >= 0 && Number(n, "color_rgb") <= 255, "color_rgb values must be integers 0-255");
            }
        }
        private static void Point(JToken token)
        {
            var p = token as JObject; Need(p != null, "point must be an object with numeric x, y and optional z");
            Keys(p, "x", "y", "z"); Number(p["x"], "point.x"); Number(p["y"], "point.y"); if (Present(p, "z")) Number(p["z"], "point.z");
        }
        private static double Coordinate(JToken p, string name) => name == "z" && !Present((JObject)p, name) ? 0 : (double)p[name];
        private static double Distance(JToken a, JToken b)
        { double x = Coordinate(a, "x") - Coordinate(b, "x"), y = Coordinate(a, "y") - Coordinate(b, "y"), z = Coordinate(a, "z") - Coordinate(b, "z"); return Math.Sqrt(x * x + y * y + z * z); }
        private static void CheckSimpleBoundary(JArray points, ref int comparisons)
        {
            // Use a local origin: WCS shoelace products can lose a small contour's
            // entire area when both coordinates carry a large survey offset.
            // Compensated accumulation also limits cancellation for concave loops.
            double area = 0, compensation = 0;
            var origin = points[0];
            for (int i = 0; i < points.Count; i++)
            {
                int next = (i + 1) % points.Count;
                double term = Cross(origin, points[i], points[next]) - compensation;
                double sum = area + term;
                compensation = (sum - area) - term;
                area = sum;
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (j == next || i == 0 && j == points.Count - 1) continue;
                    Need(++comparisons <= 1000000, "hatch boundary comparison budget exceeded");
                    Need(!Intersects(points[i], points[next], points[j], points[(j + 1) % points.Count]), "hatch boundary cannot intersect or touch itself");
                }
            }
            Need(Math.Abs(area) > 1e-12, "hatch boundary has zero area");
        }
        private static bool Intersects(JToken a, JToken b, JToken c, JToken d)
        {
            if (Math.Max(Coordinate(a, "x"), Coordinate(b, "x")) < Math.Min(Coordinate(c, "x"), Coordinate(d, "x")) ||
                Math.Max(Coordinate(c, "x"), Coordinate(d, "x")) < Math.Min(Coordinate(a, "x"), Coordinate(b, "x")) ||
                Math.Max(Coordinate(a, "y"), Coordinate(b, "y")) < Math.Min(Coordinate(c, "y"), Coordinate(d, "y")) ||
                Math.Max(Coordinate(c, "y"), Coordinate(d, "y")) < Math.Min(Coordinate(a, "y"), Coordinate(b, "y"))) return false;
            double s1 = Cross(a, b, c), s2 = Cross(a, b, d), s3 = Cross(c, d, a), s4 = Cross(c, d, b);
            return (s1 == 0 || s2 == 0 || Math.Sign(s1) != Math.Sign(s2)) && (s3 == 0 || s4 == 0 || Math.Sign(s3) != Math.Sign(s4));
        }
        private static double Cross(JToken a, JToken b, JToken c) => (Coordinate(b, "x") - Coordinate(a, "x")) * (Coordinate(c, "y") - Coordinate(a, "y")) - (Coordinate(b, "y") - Coordinate(a, "y")) * (Coordinate(c, "x") - Coordinate(a, "x"));
        private static string Text(JObject obj, string field, int maximum)
        { Need(obj[field]?.Type == JTokenType.String, field + " must be a string"); string value = (string)obj[field]; Need(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum, field + " must be nonempty and <= " + maximum + " characters"); return value; }
        private static double Number(JToken value, string field)
        { Need(value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float), field + " must be numeric"); double n = (double)value; Need(!double.IsNaN(n) && !double.IsInfinity(n) && Math.Abs(n) <= 1e12, field + " must be finite with absolute value <= 1e12"); return n; }
        private static double Positive(JObject obj, string field) { double value = Number(obj[field], field); Need(value > 0, field + " must be positive"); return value; }
        private static double PositiveOptional(JObject obj, string field, double fallback) { double value = OptionalNumber(obj, field, fallback); Need(value > 0, field + " must be positive"); return value; }
        private static double OptionalNumber(JObject obj, string field, double fallback) => Present(obj, field) ? Number(obj[field], field) : fallback;
        private static bool Boolean(JObject obj, string field, bool fallback) { if (!Present(obj, field)) return fallback; Need(obj[field].Type == JTokenType.Boolean, field + " must be boolean"); return (bool)obj[field]; }
        private static bool Present(JObject obj, string field) => obj[field] != null && obj[field].Type != JTokenType.Null;
        private static void Keys(JObject obj, params string[] allowed) { foreach (var p in obj.Properties()) Need(allowed.Contains(p.Name, StringComparer.Ordinal), "unsupported field: " + p.Name); }
        private static void Need(bool valid, string error) { if (!valid) throw new ArgumentException(error); }
    }
    public sealed class EntityBatchItemInput
    {
        public string Id { get; }
        public string Type { get; }
        public string Layer { get; }
        public JObject Parameters { get; }
        internal EntityBatchItemInput(string id, string type, string layer, JObject parameters) { Id = id; Type = type; Layer = layer; Parameters = parameters; }
    }
}
