using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Geometry
{
    public sealed class PolylineVertexState
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Bulge { get; set; }
        public double StartWidth { get; set; }
        public double EndWidth { get; set; }
        public PolylineVertexState Copy() => (PolylineVertexState)MemberwiseClone();
        public bool Same(PolylineVertexState other) => other != null && Index == other.Index &&
            X == other.X && Y == other.Y && Z == other.Z && Bulge == other.Bulge &&
            StartWidth == other.StartWidth && EndWidth == other.EndWidth;
    }

    public sealed class PolylineGeometryState
    {
        public string Layer { get; set; }
        public string OwnerHandle { get; set; }
        public double[] Normal { get; set; }
        public double Elevation { get; set; }
        public double Thickness { get; set; }
        public double ConstantWidth { get; set; }
        public bool Closed { get; set; }
        public PolylineVertexState[] Vertices { get; set; }
        public bool Same(PolylineGeometryState other) => other != null && Layer == other.Layer &&
            string.Equals(OwnerHandle, other.OwnerHandle, StringComparison.OrdinalIgnoreCase) &&
            Normal.SequenceEqual(other.Normal) && Elevation == other.Elevation && Thickness == other.Thickness &&
            ConstantWidth == other.ConstantWidth && Closed == other.Closed && Vertices.Length == other.Vertices.Length &&
            Vertices.Zip(other.Vertices, (a, b) => a.Same(b)).All(equal => equal);
    }

    public sealed class PolylinePositionEdit
    {
        public int Index { get; internal set; }
        public double[] Position { get; internal set; }
    }

    public sealed class PolylineVertexChange
    {
        public string Handle { get; internal set; }
        public PolylineGeometryState Expected { get; internal set; }
        public PolylinePositionEdit[] Edits { get; internal set; }
    }

    // Host-free validation/planning. Index edits preserve count/order and every segment's bulge/width.
    public sealed class PolylineVertexEditRequest
    {
        public const int MaxPolylines = 100;
        public const int MaxVertices = 20000;
        public const double CoordinateLimit = 1e12;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public bool DryRun { get; private set; }
        public double MinSegmentLength { get; private set; }
        public PolylineVertexChange[] Changes { get; private set; }

        public static bool TryParse(JToken token, out PolylineVertexEditRequest request, out string error)
        {
            request = null;
            error = null;
            try
            {
                var root = Object(token, "params");
                var value = new PolylineVertexEditRequest
                {
                    ExpectedDocument = Text(root, "expected_document"), ExpectedFingerprint = Text(root, "expected_fingerprint"),
                    DryRun = Boolean(root, "dry_run", false),
                    MinSegmentLength = root["min_segment_length"] == null ? 1e-9 : Number(root, "min_segment_length")
                };
                if (DrawingIdentityPolicy.NormalizeDwgPath(value.ExpectedDocument) == null)
                    throw new ArgumentException("expected_document must be a fully qualified DWG path");
                if (!Guid.TryParse(value.ExpectedFingerprint, out var guid) || guid == Guid.Empty)
                    throw new ArgumentException("expected_fingerprint must be a nonempty drawing GUID");
                if (value.MinSegmentLength <= 0) throw new ArgumentException("min_segment_length must be positive");
                var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int vertexBudget = 0;
                value.Changes = Array(root, "changes", 1, MaxPolylines).Select(item =>
                {
                    var change = Object(item, "changes");
                    string handle = Handle(change, "handle");
                    if (!handles.Add(handle)) throw new ArgumentException("duplicate polyline handle, including hexadecimal aliases: " + handle);
                    var expected = new PolylineGeometryState
                    {
                        Layer = Text(change, "expected_layer"), OwnerHandle = Handle(change, "expected_owner_handle"),
                        Normal = Point(change["expected_normal"], "expected_normal"), Elevation = Number(change, "expected_elevation"),
                        Thickness = Number(change, "expected_thickness"), ConstantWidth = Nonnegative(change, "expected_constant_width"),
                        Closed = Boolean(change, "expected_is_closed", null)
                    };
                    if (!expected.Normal.SequenceEqual(new[] { 0d, 0d, 1d }))
                        throw new ArgumentException("only lightweight polylines with normal exactly +Z are supported");
                    expected.Vertices = Array(change, "expected_vertices", 2, MaxVertices).Select((entry, index) =>
                    {
                        var vertex = Object(entry, "expected_vertices");
                        if (Integer(vertex, "index") != index)
                            throw new ArgumentException("expected_vertices must contain every index in order, starting at zero");
                        var result = new PolylineVertexState
                        {
                            Index = index, X = Number(vertex, "x"), Y = Number(vertex, "y"), Z = Number(vertex, "z"),
                            Bulge = Number(vertex, "bulge"), StartWidth = Nonnegative(vertex, "start_width"), EndWidth = Nonnegative(vertex, "end_width")
                        };
                        if (result.Z != expected.Elevation) throw new ArgumentException("expected vertex z must equal expected_elevation exactly");
                        return result;
                    }).ToArray();
                    vertexBudget += expected.Vertices.Length;
                    if (vertexBudget > MaxVertices) throw new ArgumentException("request exceeds the 20000 total vertex budget");
                    var indices = new HashSet<int>();
                    var edits = Array(change, "edits", 1, expected.Vertices.Length).Select(entry =>
                    {
                        var edit = Object(entry, "edits");
                        int index = Integer(edit, "index");
                        if (index < 0 || index >= expected.Vertices.Length || !indices.Add(index))
                            throw new ArgumentException("edit indices must be unique existing zero-based vertex indices");
                        var position = Point(edit["position"], "position");
                        if (position[2] != expected.Elevation) throw new ArgumentException("edits must retain the exact existing elevation");
                        return new PolylinePositionEdit { Index = index, Position = position };
                    }).ToArray();
                    var resultChange = new PolylineVertexChange { Handle = handle, Expected = expected, Edits = edits };
                    Plan(resultChange, expected, value.MinSegmentLength);
                    return resultChange;
                }).ToArray();
                request = value;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is FormatException)
            { error = ex.Message; return false; }
        }

        public static PolylineGeometryState Plan(PolylineVertexChange change, PolylineGeometryState actual, double minimum)
        {
            if (!change.Expected.Same(actual)) throw new ArgumentException("polyline snapshot changed: " + change.Handle);
            var vertices = actual.Vertices.Select(vertex => vertex.Copy()).ToArray();
            foreach (var edit in change.Edits)
            {
                vertices[edit.Index].X = edit.Position[0]; vertices[edit.Index].Y = edit.Position[1]; vertices[edit.Index].Z = edit.Position[2];
            }
            int count = actual.Closed ? vertices.Length : vertices.Length - 1;
            for (int index = 0; index < count; index++)
            {
                var a = vertices[index]; var b = vertices[(index + 1) % vertices.Length];
                double dx = a.X - b.X, dy = a.Y - b.Y;
                // Bound chord length even for bulged segments: zero chords are not valid arcs.
                if (Math.Sqrt(dx * dx + dy * dy) < minimum)
                    throw new ArgumentException("resulting segment chord is shorter than min_segment_length: " + change.Handle);
            }
            return new PolylineGeometryState
            {
                Layer = actual.Layer, OwnerHandle = actual.OwnerHandle, Normal = actual.Normal.ToArray(), Elevation = actual.Elevation,
                Thickness = actual.Thickness, ConstantWidth = actual.ConstantWidth, Closed = actual.Closed, Vertices = vertices
            };
        }

        private static JObject Object(JToken token, string name) => token as JObject ?? throw new ArgumentException(name + " must be an object");
        private static JArray Array(JObject obj, string name, int minimum, int maximum)
        {
            var array = obj[name] as JArray;
            if (array == null || array.Count < minimum || array.Count > maximum)
                throw new ArgumentException(name + " must contain " + minimum + ".." + maximum + " entries");
            return array;
        }
        private static string Text(JObject obj, string name)
        {
            if (obj[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(obj[name].Value<string>()))
                throw new ArgumentException(name + " must be a nonempty string");
            return obj[name].Value<string>();
        }
        private static string Handle(JObject obj, string name)
        {
            string text = Text(obj, name);
            if (text.Any(c => !Uri.IsHexDigit(c)) || !long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new ArgumentException(name + " must be a positive hexadecimal handle");
            return number.ToString("X", CultureInfo.InvariantCulture);
        }
        private static bool Boolean(JObject obj, string name, bool? fallback)
        {
            if (obj[name] == null && fallback.HasValue) return fallback.Value;
            if (obj[name]?.Type != JTokenType.Boolean) throw new ArgumentException(name + " must be boolean");
            return obj[name].Value<bool>();
        }
        private static int Integer(JObject obj, string name)
        {
            if (obj[name]?.Type != JTokenType.Integer) throw new ArgumentException(name + " must be an integer");
            return obj[name].Value<int>();
        }
        private static double Number(JObject obj, string name) => Numeric(obj[name], name);
        private static double Nonnegative(JObject obj, string name)
        {
            double number = Number(obj, name);
            if (number < 0) throw new ArgumentException(name + " must be nonnegative");
            return number;
        }
        private static double Numeric(JToken token, string name)
        {
            if (token?.Type != JTokenType.Integer && token?.Type != JTokenType.Float) throw new ArgumentException(name + " must be numeric");
            double number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > CoordinateLimit)
                throw new ArgumentException(name + " must be finite and between -1e12 and 1e12");
            return number;
        }
        private static double[] Point(JToken token, string name)
        {
            var array = token as JArray;
            if (array == null || array.Count != 3) throw new ArgumentException(name + " must contain exactly [x,y,z]");
            return array.Select(coordinate => Numeric(coordinate, name)).ToArray();
        }
    }
}
