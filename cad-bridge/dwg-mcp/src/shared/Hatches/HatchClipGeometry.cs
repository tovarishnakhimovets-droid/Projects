using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Hatches
{
    // Only unopened transient curves enter the modeler. Never pass a resident
    // write-open entity to Region.CreateFromCurves (Autodesk documents a crash).
    internal sealed class HatchClipGeometry
    {
        internal readonly Point3d Origin;
        private readonly Stopwatch watch;
        private int segments, loops;
        internal HatchClipGeometry(Point3d origin, Stopwatch watch) { Origin = origin; this.watch = watch; }
        internal void Time() => Need(watch.ElapsedMilliseconds < 15000, "15-second cooperative hatch clip budget exceeded");
        private static void Need(bool ok, string why) => HatchClipInput.Need(ok, why);
        internal void Segment() { Time(); Need(++segments <= HatchClipInput.MaxSegments, "clip exceeds 20000 total boundary segments"); }

        internal Region PolylineRegion(Polyline p)
        {
            Need(p.Closed && p.NumberOfVertices >= 3 && p.Normal == Vector3d.ZAxis && p.Elevation == 0 && p.Thickness == 0, "mask requires a closed +Z polyline at elevation0");
            using (var clone = (Polyline)p.Clone())
            {
                Need(clone.Database == null, "mask clone must be transient");
                for (int i = 0; i < clone.NumberOfVertices; i++) { Segment(); Need(clone.GetStartWidthAt(i) == 0 && clone.GetEndWidthAt(i) == 0 && clone.ConstantWidth == 0, "mask widths are unsupported"); }
                // Localize the polyline BEFORE Explode computes arc endpoints.
                // Translating already exploded WCS arcs retains their rounding gaps.
                clone.TransformBy(Matrix3d.Displacement(Point3d.Origin - Origin));
                var curves = new DBObjectCollection();
                try { clone.Explode(curves); return MakeRegion(curves); }
                finally { foreach (DBObject o in curves) o.Dispose(); }
            }
        }
        private Region MakeRegion(DBObjectCollection curves)
        {
            Time();
            foreach (DBObject o in curves) Need(o is Curve && o.Database == null && o.ObjectId.IsNull, "modeler input must contain unopened transient curves");
            // All callers construct their curves in this geometry's local frame.
            var result = Region.CreateFromCurves(curves);
            if (result.Count != 1)
            {
                foreach (DBObject o in result) o.Dispose();
                throw new ArgumentException("a boundary loop did not produce exactly one valid region");
            }
            var region = (Region)result[0];
            try { Need(!region.IsNull && region.Area > 0, "empty or invalid boundary region"); return region; }
            catch { region.Dispose(); throw; }
        }
        internal JObject HatchShape(Hatch hatch)
        {
            ReadHatch(hatch, out var shape, false);
            return shape;
        }
        internal List<Region> HatchRegions(Hatch hatch, out JObject shape) => ReadHatch(hatch, out shape, true);
        private List<Region> ReadHatch(Hatch hatch, out JObject shape, bool buildRegions)
        {
            Need(hatch.Normal == Vector3d.ZAxis && hatch.Elevation == 0, "hatch requires +Z and elevation0");
            Need((int)hatch.HatchStyle >= 0 && (int)hatch.HatchStyle <= 2, "unsupported hatch island style");
            shape = new JObject { ["normal"] = new JArray(0, 0, 1), ["elevation"] = hatch.Elevation, ["style"] = (int)hatch.HatchStyle };
            var signature = new JArray(); shape["loops"] = signature;
            var rings = new List<Region>();
            try
            {
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    Time(); Need(++loops <= HatchClipInput.MaxLoops, "clip exceeds1000 total source loops");
                    var loop = hatch.GetLoopAt(i); var curves = new DBObjectCollection(); var data = new JArray();
                    signature.Add(new JObject { ["type"] = (int)hatch.LoopTypeAt(i), ["edges"] = data });
                    try
                    {
                        if (loop.IsPolyline)
                        {
                            using (var p = new Polyline())
                            {
                                for (int v = 0; v < loop.Polyline.Count; v++)
                                {
                                    Segment(); var b = loop.Polyline[v];
                                    data.Add(new JArray(b.Vertex.X, b.Vertex.Y, b.Bulge));
                                    if (buildRegions) p.AddVertexAt(v, new Point2d(b.Vertex.X - Origin.X, b.Vertex.Y - Origin.Y), b.Bulge, 0, 0);
                                }
                                if (buildRegions) { p.Closed = true; p.Explode(curves); }
                            }
                        }
                        else foreach (Curve2d curve in loop.Curves)
                        {
                            Segment();
                            if (curve is LineSegment2d line)
                            {
                                data.Add(new JArray("line", line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y));
                                if (buildRegions) curves.Add(new Line(Local(line.StartPoint), Local(line.EndPoint)));
                            }
                            else if (curve is CircularArc2d arc)
                            {
                                data.Add(new JArray("arc", arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle, arc.ReferenceVector.X, arc.ReferenceVector.Y, arc.IsClockWise));
                                if (!buildRegions) continue;
                                var center = Local(arc.Center);
                                if (Math.Abs(arc.EndAngle - arc.StartAngle) == Math.PI * 2) curves.Add(new Circle(center, Vector3d.ZAxis, arc.Radius));
                                else
                                {
                                    double a = arc.ReferenceVector.Angle + (arc.IsClockWise ? -arc.EndAngle : arc.StartAngle);
                                    double b = arc.ReferenceVector.Angle + (arc.IsClockWise ? -arc.StartAngle : arc.EndAngle);
                                    curves.Add(new Arc(center, arc.Radius, a, b));
                                }
                            }
                            else if (curve is EllipticalArc2d ellipse)
                            {
                                // AppendLoop may return a circular boundary as EllipticalArc2d.
                                // Preserve both axes, radii and parameters in native geometry;
                                // do not fit a circle or tessellate an actual ellipse.
                                data.Add(new JArray("ellipse", ellipse.Center.X, ellipse.Center.Y,
                                    ellipse.MajorAxis.X, ellipse.MajorAxis.Y, ellipse.MinorAxis.X, ellipse.MinorAxis.Y,
                                    ellipse.MajorRadius, ellipse.MinorRadius, ellipse.StartAngle, ellipse.EndAngle));
                                if (!buildRegions) continue;
                                using (var native = new EllipticalArc3d(Local(ellipse.Center),
                                    new Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0),
                                    new Vector3d(ellipse.MinorAxis.X, ellipse.MinorAxis.Y, 0),
                                    ellipse.MajorRadius, ellipse.MinorRadius, ellipse.StartAngle, ellipse.EndAngle))
                                {
                                    var converted = Curve.CreateFromGeCurve(native);
                                    if (converted is Ellipse || converted is Arc || converted is Circle) curves.Add(converted);
                                    else { converted?.Dispose(); throw new ArgumentException("native ellipse conversion failed; no approximation was made"); }
                                }
                            }
                            else throw new ArgumentException("unsupported hatch edge: " + curve.GetType().Name + "; no approximation was made");
                        }
                        if (buildRegions) rings.Add(MakeRegion(curves));
                    }
                    finally { foreach (DBObject o in curves) o.Dispose(); }
                }
                Need(hatch.NumberOfLoops > 0, "hatch has no loops");
                return buildRegions ? FillRings(rings, (int)hatch.HatchStyle) : new List<Region>();
            }
            finally { foreach (var r in rings) r.Dispose(); }
        }
        private Point3d Local(Point2d point) => new Point3d(point.X - Origin.X, point.Y - Origin.Y, 0);

        private List<Region> FillRings(List<Region> rings, int style)
        {
            var parent = Enumerable.Repeat(-1, rings.Count).ToArray();
            var areas = rings.Select(r => r.Area).ToArray();
            for (int i = 0; i < rings.Count; i++)
                for (int j = i + 1; j < rings.Count; j++)
                {
                    Time(); if (!Overlap(rings[i], rings[j])) continue;
                    using (var intersection = Boolean(rings[i], rings[j], BooleanOperationType.BoolIntersect))
                    {
                        if (Empty(intersection)) continue;
                        int small = areas[i] <= areas[j] ? i : j, big = small == i ? j : i;
                        using (var difference = Boolean(rings[small], rings[big], BooleanOperationType.BoolSubtract))
                            Need(Empty(difference), "hatch loops overlap without containment");
                        using (var reverse = Boolean(rings[big], rings[small], BooleanOperationType.BoolSubtract))
                            Need(!Empty(reverse), "duplicate/coincident hatch loops");
                        if (parent[small] < 0 || areas[big] < areas[parent[small]]) parent[small] = big;
                    }
                }
            var output = new List<Region>();
            try
            {
                foreach (int i in HatchIslandPolicy.FilledLoops(style, parent))
                {
                    var filled = (Region)rings[i].Clone();
                    try
                    {
                        for (int j = 0; j < rings.Count; j++) if (HatchIslandPolicy.SubtractChild(style) && parent[j] == i)
                        { using (var hole = (Region)rings[j].Clone()) filled.BooleanOperation(BooleanOperationType.BoolSubtract, hole); }
                        Need(!Empty(filled), "empty filled loop"); output.Add(filled); filled = null;
                    }
                    finally { filled?.Dispose(); }
                }
                return output;
            }
            catch { foreach (var r in output) r.Dispose(); throw; }
        }
        internal Region Boolean(Region a, Region b, BooleanOperationType operation)
        {
            Time();
            var result = (Region)a.Clone();
            try { using (var operand = (Region)b.Clone()) result.BooleanOperation(operation, operand); return result; }
            catch { result.Dispose(); throw; }
        }
        internal static bool Empty(Region r) => r.IsNull || r.Area == 0;
        internal static bool Overlap(Region a, Region b)
        {
            var x = a.GeometricExtents; var y = b.GeometricExtents;
            return x.MinPoint.X <= y.MaxPoint.X && y.MinPoint.X <= x.MaxPoint.X && x.MinPoint.Y <= y.MaxPoint.Y && y.MinPoint.Y <= x.MaxPoint.Y;
        }
        internal void UnionMasks(List<Region> masks)
        {
            for (int i = 0; i < masks.Count; i++)
                for (int j = i + 1; j < masks.Count; j++)
                {
                    Time(); if (!Overlap(masks[i], masks[j])) continue;
                    using (var common = Boolean(masks[i], masks[j], BooleanOperationType.BoolIntersect))
                    {
                        if (Empty(common)) continue;
                        using (var other = (Region)masks[j].Clone()) masks[i].BooleanOperation(BooleanOperationType.BoolUnite, other);
                        masks[j].Dispose(); masks.RemoveAt(j); i = -1; break;
                    }
                }
        }
        internal List<Region> Intersect(List<Region> source, List<Region> masks, out bool entirelyInside)
        {
            var result = new List<Region>(); entirelyInside = true;
            try
            {
                foreach (var s in source)
                {
                    var remainder = (Region)s.Clone();
                    try
                    {
                        foreach (var m in masks)
                        {
                            Time(); if (!Overlap(s, m)) continue;
                            var piece = Boolean(s, m, BooleanOperationType.BoolIntersect);
                            if (Empty(piece)) piece.Dispose(); else result.Add(piece);
                            if (!Empty(remainder)) { using (var other = (Region)m.Clone()) remainder.BooleanOperation(BooleanOperationType.BoolSubtract, other); }
                        }
                        if (!Empty(remainder)) entirelyInside = false;
                    }
                    finally { remainder.Dispose(); }
                }
                return result;
            }
            catch { foreach (var r in result) r.Dispose(); throw; }
        }
    }
}
