using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Bimwright.Dwg.Plugin.Network
{
    public sealed class NetworkPart
    {
        public string SourceHandle { get; internal set; }
        public string FromNode { get; internal set; }
        public string ToNode { get; internal set; }
        public NetworkPoint[] Vertices { get; internal set; }
        public double Length => NetworkGeometry.Length(Vertices);
    }

    public static class NetworkGeometry
    {
        private sealed class Cut
        {
            public double Distance;
            public string Handle;
            public NetworkPoint Point;
        }

        // Host-independent planning. Nodes are authoritative; no node is moved or created.
        public static NetworkPart[] Plan(NetworkSourceInput[] sources, NetworkNodeInput[] nodes,
            double snapTolerance, double geometryTolerance, double minimumLength)
        {
            var watch = Stopwatch.StartNew();
            var parts = new List<NetworkPart>();
            var usedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int comparisons = 0;
            for (int a = 0; a < nodes.Length; a++)
                for (int b = a + 1; b < nodes.Length; b++)
                {
                    Budget(ref comparisons, watch);
                    if (nodes[a].ExpectedPosition.Distance(nodes[b].ExpectedPosition) <= geometryTolerance)
                        throw new ArgumentException("coincident or indistinguishable nodes: " + nodes[a].Handle + ", " + nodes[b].Handle);
                }
            foreach (var source in sources)
            {
                var vertices = source.ExpectedVertices;
                ValidatePath(vertices, minimumLength);
                var distances = new double[vertices.Length];
                for (int i = 1; i < vertices.Length; i++)
                    distances[i] = distances[i - 1] + vertices[i - 1].Distance(vertices[i]);
                var cuts = new List<Cut>();
                foreach (var node in nodes)
                {
                    var point = node.ExpectedPosition;
                    // A +Z LWPolyline cannot represent a node at a different WCS elevation exactly.
                    if (point.Z != vertices[0].Z) continue;
                    double? cutDistance = null;
                    if (point.Distance(vertices[0]) <= snapTolerance) cutDistance = 0;
                    if (point.Distance(vertices[vertices.Length - 1]) <= snapTolerance)
                    {
                        if (cutDistance.HasValue)
                            throw new ArgumentException("node is within tolerance of both ends: " + source.Handle);
                        cutDistance = distances[distances.Length - 1];
                    }
                    for (int i = 0; i < vertices.Length - 1; i++)
                    {
                        Budget(ref comparisons, watch);
                        var projected = Project(point, vertices[i], vertices[i + 1], out var ratio);
                        if (projected.Distance(point) > snapTolerance) continue;
                        double along = distances[i] + ratio * (distances[i + 1] - distances[i]);
                        // Endpoint snapping takes priority only for its immediately adjacent segment.
                        if (cutDistance == 0 && i == 0) continue;
                        if (cutDistance == distances[distances.Length - 1] && i == vertices.Length - 2) continue;
                        if (cutDistance.HasValue && Math.Abs(cutDistance.Value - along) > geometryTolerance)
                            throw new ArgumentException("node has ambiguous projections on source " + source.Handle + ": " + node.Handle);
                        cutDistance = along;
                    }
                    if (!cutDistance.HasValue) continue;
                    cuts.Add(new Cut { Distance = cutDistance.Value, Handle = node.Handle, Point = point });
                    usedNodes.Add(node.Handle);
                }
                cuts.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (cuts.Count < 2 || cuts[0].Distance != 0 || cuts[cuts.Count - 1].Distance != distances[distances.Length - 1])
                    throw new ArgumentException("both source endpoints require a supplied node within snap_tolerance: " + source.Handle);
                for (int c = 0; c < cuts.Count - 1; c++)
                {
                    var first = cuts[c];
                    var last = cuts[c + 1];
                    if (last.Distance - first.Distance <= geometryTolerance)
                        throw new ArgumentException("multiple nodes occupy the same source location: " + source.Handle);
                    var result = new List<NetworkPoint> { first.Point };
                    for (int i = 1; i < vertices.Length - 1; i++)
                        if (distances[i] > first.Distance + geometryTolerance && distances[i] < last.Distance - geometryTolerance)
                            result.Add(vertices[i]);
                    result.Add(last.Point);
                    var path = result.ToArray();
                    ValidatePath(path, minimumLength);
                    parts.Add(new NetworkPart
                    {
                        SourceHandle = source.Handle, FromNode = first.Handle, ToNode = last.Handle, Vertices = path
                    });
                }
            }
            if (nodes.Any(node => !usedNodes.Contains(node.Handle)))
                throw new ArgumentException("every supplied node must lie on a supplied source within snap_tolerance");
            if (parts.Count > 10000 || parts.Sum(p => p.Vertices.Length) > NetworkRequest.MaxVertices)
                throw new ArgumentException("planned output exceeds the 10000 part / 20000 vertex budget");
            // Snapping must not leave another selected node in the interior of an output edge.
            foreach (var part in parts)
                foreach (var node in nodes)
                {
                    if (node.Handle == part.FromNode || node.Handle == part.ToNode) continue;
                    for (int i = 0; i < part.Vertices.Length - 1; i++)
                    {
                        Budget(ref comparisons, watch);
                        var closest = Project(node.ExpectedPosition, part.Vertices[i], part.Vertices[i + 1], out _);
                        if (closest.Distance(node.ExpectedPosition) <= geometryTolerance)
                            throw new ArgumentException("snapping would leave an uncut interior node: " + node.Handle);
                    }
                }
            return parts.ToArray();
        }

        public static NetworkPoint[] MoveEndpoint(NetworkPoint[] vertices, bool atStart,
            NetworkPoint expectedBlockPosition, NetworkPoint target, double minimumLength)
        {
            ValidatePath(vertices, minimumLength);
            int index = atStart ? 0 : vertices.Length - 1;
            if (!vertices[index].Exact(expectedBlockPosition))
                throw new ArgumentException("the existing endpoint and block insertion must already coincide exactly");
            if (target.Z != vertices[0].Z)
                throw new ArgumentException("target must retain the exact polyline elevation");
            var result = (NetworkPoint[])vertices.Clone();
            result[index] = target;
            ValidatePath(result, minimumLength);
            return result;
        }

        public static void ValidatePath(NetworkPoint[] vertices, double minimumLength)
        {
            if (vertices == null || vertices.Length < 2)
                throw new ArgumentException("a source must contain at least two vertices");
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = vertices[i];
                if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) ||
                    double.IsInfinity(point.Y) || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                    throw new ArgumentException("nonfinite source geometry");
                if (point.Z != vertices[0].Z)
                    throw new ArgumentException("only straight planar paths parallel to WCS XY are supported");
                if (i > 0 && vertices[i - 1].Distance(point) < minimumLength)
                    throw new ArgumentException("zero or short path segment below min_segment_length");
            }
        }

        public static double Length(NetworkPoint[] vertices)
        {
            double length = 0;
            for (int i = 1; i < vertices.Length; i++) length += vertices[i - 1].Distance(vertices[i]);
            return length;
        }

        private static NetworkPoint Project(NetworkPoint point, NetworkPoint a, NetworkPoint b, out double ratio)
        {
            double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
            double denominator = x * x + y * y + z * z;
            ratio = denominator == 0 ? 0 : Math.Max(0, Math.Min(1,
                ((point.X - a.X) * x + (point.Y - a.Y) * y + (point.Z - a.Z) * z) / denominator));
            return new NetworkPoint(a.X + ratio * x, a.Y + ratio * y, a.Z + ratio * z);
        }

        private static void Budget(ref int comparisons, Stopwatch watch)
        {
            comparisons++;
            if (comparisons > 4 * NetworkRequest.MaxComparisons)
                throw new ArgumentException("planning exceeds the 4000000 total comparison budget; reduce the explicit scope");
            if ((comparisons & 1023) == 0 && watch.ElapsedMilliseconds > 5000)
                throw new ArgumentException("planning exceeded five seconds; reduce the explicit scope");
        }
    }
}
