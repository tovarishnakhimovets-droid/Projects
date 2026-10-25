using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Annotation
{
    public sealed class LeaderRepositionState
    {
        public string Layer { get; set; }
        public string Owner { get; set; }
        public int LeaderIndex { get; set; }
        public int LineIndex { get; set; }
        public string Text { get; set; }
        public double[] TextLocation { get; set; }
        public double[][] Vertices { get; set; }
        public double[] Dogleg { get; set; }
        public bool Same(LeaderRepositionState other) => other != null && LeaderIndex == other.LeaderIndex && LineIndex == other.LineIndex && Layer == other.Layer &&
            string.Equals(Owner, other.Owner, StringComparison.OrdinalIgnoreCase) && Text == other.Text &&
            TextLocation.SequenceEqual(other.TextLocation) && Dogleg.SequenceEqual(other.Dogleg) &&
            Vertices.Length == other.Vertices.Length && Vertices.Zip(other.Vertices, (a, b) => a.SequenceEqual(b)).All(x => x);
    }
    public sealed class LeaderRepositionChange
    {
        public string Handle { get; set; }
        public LeaderRepositionState Expected { get; set; }
        public double[] NewTextLocation { get; set; }
        public double[] NewTerminal { get; set; }
        public string DoglegSide { get; set; }
    }
    // Host-free input validation; snapshots are exact, with no coordinate rounding or fuzzy text matching.
    public sealed class LeaderRepositionRequest
    {
        public const int MaxLeaders = 100;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public bool DryRun { get; private set; }
        public LeaderRepositionChange[] Changes { get; private set; }
        public static bool TryParse(JToken token, out LeaderRepositionRequest result, out string error)
        {
            result = null; error = null;
            try
            {
                var root = Obj(token, "params");
                Only(root, "expected_document", "expected_fingerprint", "changes", "dry_run");
                var request = new LeaderRepositionRequest
                {
                    ExpectedDocument = Text(root, "expected_document"),
                    ExpectedFingerprint = Text(root, "expected_fingerprint"),
                    DryRun = root["dry_run"] == null || Bool(root, "dry_run")
                };
                if (DrawingIdentityPolicy.NormalizeDwgPath(request.ExpectedDocument) == null) throw new ArgumentException("expected_document must be a full DWG path");
                if (!Guid.TryParse(request.ExpectedFingerprint, out var guid) || guid == Guid.Empty) throw new ArgumentException("expected_fingerprint must be a nonempty GUID");
                var changes = root["changes"] as JArray;
                if (changes == null || changes.Count < 1 || changes.Count > MaxLeaders) throw new ArgumentException("changes must contain 1..100 entries");
                var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                request.Changes = changes.Select(value =>
                {
                    var item = Obj(value, "change");
                    Only(item, "handle", "expected_layer", "expected_owner_handle", "expected_leader_index", "expected_line_index", "expected_text", "expected_text_location", "expected_vertices", "expected_dogleg", "new_text_location", "new_terminal", "dogleg_side");
                    var handle = Handle(item, "handle");
                    if (!handles.Add(handle)) throw new ArgumentException("duplicate leader handle, including hexadecimal aliases");
                    var text = Text(item, "expected_text");
                    if (text.Length > 20000) throw new ArgumentException("expected_text exceeds 20000 characters");
                    var vertices = item["expected_vertices"] as JArray;
                    if (vertices == null || vertices.Count != 2) throw new ArgumentException("expected_vertices must contain exactly the arrow and terminal [x,y,z]");
                    var change = new LeaderRepositionChange
                    {
                        Handle = handle,
                        Expected = new LeaderRepositionState
                        {
                            Layer = Text(item, "expected_layer"),
                            Owner = Handle(item, "expected_owner_handle"),
                            LeaderIndex = Index(item, "expected_leader_index"),
                            LineIndex = Index(item, "expected_line_index"),
                            Text = text,
                            TextLocation = Point(item["expected_text_location"]),
                            Dogleg = Point(item["expected_dogleg"]),
                            Vertices = vertices.Select(Point).ToArray()
                        },
                        NewTextLocation = Point(item["new_text_location"]),
                        NewTerminal = Point(item["new_terminal"]),
                        DoglegSide = Text(item, "dogleg_side")
                    };
                    if (change.DoglegSide != "left" && change.DoglegSide != "right") throw new ArgumentException("dogleg_side must be left or right (direction toward text)");
                    double z = change.Expected.Vertices[0][2];
                    if (change.Expected.Vertices[1][2] != z || change.Expected.TextLocation[2] != z || change.NewTextLocation[2] != z || change.NewTerminal[2] != z)
                        throw new ArgumentException("all points must retain the exact existing WCS elevation");
                    if (change.Expected.Dogleg[1] != 0 || change.Expected.Dogleg[2] != 0 || change.Expected.Dogleg[0] == 0) throw new ArgumentException("expected_dogleg must be a nonzero horizontal X vector");
                    if (DistanceSquared(change.Expected.Vertices[0], change.NewTerminal) <= 1e-18) throw new ArgumentException("new leader segment must be longer than 1e-9 drawing units");
                    return change;
                }).ToArray();
                result = request; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException || ex is OverflowException || ex is FormatException)
            { error = ex.Message; return false; }
        }
        public static void CheckSnapshot(LeaderRepositionChange change, LeaderRepositionState actual)
        { if (!change.Expected.Same(actual)) throw new ArgumentException("leader snapshot changed: " + change.Handle); }
        private static double DistanceSquared(double[] a, double[] b) => a.Zip(b, (x, y) => (x - y) * (x - y)).Sum();
        private static JObject Obj(JToken t, string name) => t as JObject ?? throw new ArgumentException(name + " must be an object");
        private static void Only(JObject obj, params string[] names)
        { foreach (var property in obj.Properties()) if (!names.Contains(property.Name)) throw new ArgumentException("unknown field: " + property.Name); }
        private static string Text(JObject obj, string name)
        { if (obj[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(obj[name].Value<string>())) throw new ArgumentException(name + " must be a nonempty string"); return obj[name].Value<string>(); }
        private static string Handle(JObject obj, string name)
        { var s = Text(obj, name); if (s.Any(c => !Uri.IsHexDigit(c)) || !long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) || n <= 0) throw new ArgumentException(name + " must be a positive hex handle"); return n.ToString("X", CultureInfo.InvariantCulture); }
        private static int Index(JObject obj, string name)
        { if (obj[name]?.Type != JTokenType.Integer || obj[name].Value<long>() < 0 || obj[name].Value<long>() > int.MaxValue) throw new ArgumentException(name + " must be a nonnegative integer"); return obj[name].Value<int>(); }
        private static bool Bool(JObject obj, string name)
        { if (obj[name]?.Type != JTokenType.Boolean) throw new ArgumentException(name + " must be boolean"); return obj[name].Value<bool>(); }
        private static double[] Point(JToken token)
        { var a = token as JArray; if (a == null || a.Count != 3) throw new ArgumentException("point/vector must contain exactly three numeric coordinates"); return a.Select(t => { if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new ArgumentException("coordinates must be numeric"); var n = t.Value<double>(); if (double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n) > 1e12) throw new ArgumentException("coordinates must be finite and bounded to +/-1e12"); return n; }).ToArray(); }
    }
}
