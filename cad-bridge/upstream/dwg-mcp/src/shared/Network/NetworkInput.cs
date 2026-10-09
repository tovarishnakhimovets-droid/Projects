using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Network
{
    public readonly struct NetworkPoint
    {
        public NetworkPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double[] ToArray() => new[] { X, Y, Z };
        public double Distance(NetworkPoint other)
        {
            double x = X - other.X, y = Y - other.Y, z = Z - other.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        public bool Exact(NetworkPoint other) => X == other.X && Y == other.Y && Z == other.Z;
    }

    public sealed class NetworkSourceInput
    {
        public string Handle { get; internal set; }
        public string ExpectedType { get; internal set; }
        public string ExpectedLayer { get; internal set; }
        public string ExpectedOwnerHandle { get; internal set; }
        public NetworkPoint[] ExpectedVertices { get; internal set; }
    }

    public sealed class NetworkNodeInput
    {
        public string Handle { get; internal set; }
        public NetworkPoint ExpectedPosition { get; internal set; }
    }

    public sealed class NetworkAttributeInput
    {
        public string Handle { get; internal set; }
        public string Tag { get; internal set; }
        public string Text { get; internal set; }
        public NetworkPoint Position { get; internal set; }
        public NetworkPoint AlignmentPoint { get; internal set; }
    }

    public sealed class NetworkEndpointInput
    {
        public string PolylineHandle { get; internal set; }
        public string BlockHandle { get; internal set; }
        public bool AtStart { get; internal set; }
        public NetworkPoint[] ExpectedVertices { get; internal set; }
        public NetworkPoint ExpectedBlockPosition { get; internal set; }
        public NetworkPoint Target { get; internal set; }
        public string ExpectedPolylineLayer { get; internal set; }
        public string ExpectedBlockLayer { get; internal set; }
        public string ExpectedBlockName { get; internal set; }
        public NetworkAttributeInput[] ExpectedAttributes { get; internal set; }
    }

    public sealed class NetworkRequest
    {
        public const int MaxSources = 1000;
        public const int MaxNodes = 2000;
        public const int MaxVertices = 20000;
        public const int MaxComparisons = 1000000;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public double GeometryTolerance { get; private set; }
        public double MinSegmentLength { get; private set; }
        public double SnapTolerance { get; private set; }
        public bool DryRun { get; private set; }
        public NetworkSourceInput[] Sources { get; private set; }
        public NetworkNodeInput[] Nodes { get; private set; }
        public NetworkEndpointInput[] Changes { get; private set; }

        public static bool TryParse(JToken parameters, bool noding, out NetworkRequest request, out string error)
        {
            request = null;
            error = null;
            try
            {
                var root = parameters as JObject ?? throw new ArgumentException("params must be an object");
                var result = new NetworkRequest
                {
                    ExpectedDocument = Text(root, "expected_document"),
                    ExpectedFingerprint = Text(root, "expected_fingerprint"),
                    GeometryTolerance = Number(root, "geometry_tolerance", 1e-9),
                    MinSegmentLength = Number(root, "min_segment_length", 1e-6),
                    SnapTolerance = noding ? Number(root, "snap_tolerance", null) : 0,
                    DryRun = Boolean(root, "dry_run", false)
                };
                if (!Guid.TryParse(result.ExpectedFingerprint, out var guid) || guid == Guid.Empty)
                    throw new ArgumentException("expected_fingerprint must be a nonempty GUID");
                if (result.GeometryTolerance <= 0 || result.MinSegmentLength <= result.GeometryTolerance)
                    throw new ArgumentException("require 0 < geometry_tolerance < min_segment_length");
                if (noding && result.SnapTolerance < result.GeometryTolerance)
                    throw new ArgumentException("snap_tolerance must be at least geometry_tolerance");
                var handles = new HashSet<long>();
                int vertices = 0;
                if (noding)
                {
                    result.Sources = Array(root, "sources", 1, MaxSources).Select(t =>
                    {
                        var obj = Object(t, "sources");
                        var source = new NetworkSourceInput
                        {
                            Handle = Handle(obj, "handle", handles),
                            ExpectedType = Text(obj, "expected_type"),
                            ExpectedLayer = Text(obj, "expected_layer"),
                            ExpectedOwnerHandle = Handle(obj, "expected_owner_handle", null),
                            ExpectedVertices = Points(obj, "expected_vertices")
                        };
                        if (source.ExpectedType != "Line" && source.ExpectedType != "Polyline")
                            throw new ArgumentException("expected_type must be Line or Polyline");
                        if (source.ExpectedType == "Line" && source.ExpectedVertices.Length != 2)
                            throw new ArgumentException("Line expected_vertices must contain two points");
                        vertices += source.ExpectedVertices.Length;
                        if (vertices > MaxVertices)
                            throw new ArgumentException("request exceeds the 20000 vertex budget");
                        return source;
                    }).ToArray();
                    result.Nodes = Array(root, "nodes", 1, MaxNodes).Select(t =>
                    {
                        var obj = Object(t, "nodes");
                        return new NetworkNodeInput
                        {
                            Handle = Handle(obj, "handle", handles),
                            ExpectedPosition = Point(obj["expected_position"])
                        };
                    }).ToArray();
                    if ((long)vertices * result.Nodes.Length > MaxComparisons)
                        throw new ArgumentException("vertices * nodes exceeds the 1000000 comparison budget; reduce the explicit scope");
                }
                else
                {
                    result.Changes = Array(root, "changes", 1, MaxSources).Select(t =>
                    {
                        var obj = Object(t, "changes");
                        string endpoint = Text(obj, "endpoint");
                        if (endpoint != "start" && endpoint != "end")
                            throw new ArgumentException("endpoint must be start or end");
                        var change = new NetworkEndpointInput
                        {
                            PolylineHandle = Handle(obj, "polyline_handle", handles),
                            BlockHandle = Handle(obj, "block_handle", handles),
                            AtStart = endpoint == "start",
                            ExpectedVertices = Points(obj, "expected_vertices"),
                            ExpectedBlockPosition = Point(obj["expected_block_position"]),
                            Target = Point(obj["target"]),
                            ExpectedPolylineLayer = Text(obj, "expected_polyline_layer"),
                            ExpectedBlockLayer = Text(obj, "expected_block_layer"),
                            ExpectedBlockName = Text(obj, "expected_block_name")
                        };
                        change.ExpectedAttributes = Array(obj, "expected_attributes", 0, 1000).Select(a =>
                        {
                            var attribute = Object(a, "expected_attributes");
                            return new NetworkAttributeInput
                            {
                                Handle = Handle(attribute, "handle", handles),
                                Tag = Text(attribute, "tag"),
                                Text = Text(attribute, "text", true),
                                Position = Point(attribute["position"]),
                                AlignmentPoint = Point(attribute["alignment_point"])
                            };
                        }).ToArray();
                        vertices += change.ExpectedVertices.Length;
                        if (vertices > MaxVertices || handles.Count > 20000)
                            throw new ArgumentException("request exceeds the 20000 vertex/object budget");
                        return change;
                    }).ToArray();
                }
                if (vertices > MaxVertices || handles.Count > 20000)
                    throw new ArgumentException("request exceeds the 20000 vertex/object budget");
                request = result;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is FormatException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static JObject Object(JToken token, string field)
            => token as JObject ?? throw new ArgumentException(field + " entries must be objects");
        private static JArray Array(JObject obj, string field, int minimum, int maximum)
        {
            var array = obj[field] as JArray;
            if (array == null || array.Count < minimum || array.Count > maximum)
                throw new ArgumentException(field + " must be an array containing " + minimum + " to " + maximum + " entries");
            return array;
        }
        private static string Text(JObject obj, string field, bool allowEmpty = false)
        {
            var token = obj[field];
            if (token?.Type != JTokenType.String || (!allowEmpty && string.IsNullOrWhiteSpace(token.Value<string>())))
                throw new ArgumentException(field + " must be " + (allowEmpty ? "a string" : "a nonempty string"));
            return token.Value<string>();
        }
        private static string Handle(JObject obj, string field, HashSet<long> distinct)
        {
            string text = Text(obj, field);
            if (text.Any(c => !Uri.IsHexDigit(c)) || !long.TryParse(text, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException(field + " must be a positive hexadecimal handle");
            if (distinct != null && !distinct.Add(value))
                throw new ArgumentException("duplicate object handle (including hexadecimal aliases): " + text);
            if (distinct != null && distinct.Count > 20000)
                throw new ArgumentException("request exceeds the 20000 object budget");
            return value.ToString("X", CultureInfo.InvariantCulture);
        }
        private static double Number(JObject obj, string field, double? fallback)
        {
            var token = obj[field];
            if (token == null && fallback.HasValue) return fallback.Value;
            if (token?.Type != JTokenType.Integer && token?.Type != JTokenType.Float)
                throw new ArgumentException(field + " must be numeric");
            double value = token.Value<double>();
            if (!Finite(value)) throw new ArgumentException(field + " must be finite");
            return value;
        }
        private static bool Boolean(JObject obj, string field, bool fallback)
        {
            var token = obj[field];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Boolean) throw new ArgumentException(field + " must be boolean");
            return token.Value<bool>();
        }
        private static NetworkPoint[] Points(JObject obj, string field)
            => Array(obj, field, 2, MaxVertices).Select(Point).ToArray();
        public static NetworkPoint Point(JToken token)
        {
            var array = token as JArray;
            if (array == null || array.Count != 3) throw new ArgumentException("point must contain exactly [x,y,z]");
            var values = array.Select(t =>
            {
                if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
                    throw new ArgumentException("point coordinates must be numeric");
                double value = t.Value<double>();
                if (!Finite(value) || Math.Abs(value) > 1e12)
                    throw new ArgumentException("point coordinates must be finite and between -1e12 and 1e12");
                return value;
            }).ToArray();
            return new NetworkPoint(values[0], values[1], values[2]);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
