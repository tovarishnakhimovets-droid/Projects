using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.Annotation
{
    internal static class LeaderRepositionNative
    {
        internal static LeaderRepositionState Capture(MLeader leader, out int leaderIndex, out int lineIndex)
        {
            Require(leader.GetType() == typeof(MLeader) && leader.ContentType == ContentType.MTextContent,
                "target must be a native MLeader with MText");
            Require(leader.Annotative != AnnotativeStates.True && !leader.EnableAnnotationScale && !leader.EnableFrameText,
                "annotative leaders and text frames are unsupported");
            using (var plane = leader.GetPlane())
                Require(plane.Normal.X == 0 && plane.Normal.Y == 0 && plane.Normal.Z == 1, "only exact WCS +Z leaders are supported");
            var leaders = leader.GetLeaderIndexes().Cast<int>().ToArray();
            Require(leaders.Length == 1 && leader.LeaderCount == 1 && leader.LeaderLineCount == 1,
                "exactly one leader and one leader line required");
            leaderIndex = leaders[0];
            var lines = leader.GetLeaderLineIndexes(leaderIndex).Cast<int>().ToArray();
            Require(lines.Length == 1, "exactly one leader line required"); lineIndex = lines[0];
            Require(leader.VerticesCount(lineIndex) == 2 && leader.GetLeaderLineType(lineIndex) == LeaderType.StraightLeader,
                "exactly two vertices on one straight line required");
            var dogleg = leader.GetDogleg(leaderIndex);
            Require(dogleg.X != 0 && dogleg.Y == 0 && dogleg.Z == 0, "only horizontal X doglegs are supported");
            using (var text = leader.MText)
            {
                Require(text != null && !text.HasFields && text.Annotative != AnnotativeStates.True &&
                    text.Normal.X == 0 && text.Normal.Y == 0 && text.Normal.Z == 1 && text.Rotation == 0 && !string.IsNullOrWhiteSpace(text.Contents),
                    "MText must be nonempty, horizontal, non-annotative and without fields");
                var first = leader.GetFirstVertex(lineIndex); var last = leader.GetLastVertex(lineIndex);
                Require(first.Z == last.Z && first.Z == leader.TextLocation.Z, "leader elevation must be constant");
                return new LeaderRepositionState
                {
                    LeaderIndex = leaderIndex,
                    LineIndex = lineIndex,
                    Layer = leader.Layer,
                    Owner = leader.OwnerId.Handle.ToString(),
                    Text = text.Contents,
                    TextLocation = Point(leader.TextLocation),
                    Dogleg = Vector(dogleg),
                    Vertices = new[] { Point(first), Point(last) }
                };
            }
        }
        internal static object Describe(MLeader leader)
        {
            var state = Capture(leader, out _, out _);
            return Snapshot(leader.Handle.ToString(), state);
        }
        internal static object Snapshot(string handle, LeaderRepositionState state) => new
        {
            handle,
            expected_leader_index = state.LeaderIndex,
            expected_line_index = state.LineIndex,
            expected_layer = state.Layer,
            expected_owner_handle = state.Owner,
            expected_text = state.Text,
            expected_text_location = state.TextLocation,
            expected_vertices = state.Vertices,
            expected_dogleg = state.Dogleg
        };
        internal static object Bounds(MLeader leader)
        { var ext = leader.GetContentGeomExtents(); return new { min = Point(ext.MinPoint), max = Point(ext.MaxPoint) }; }
        internal static double[] Point(Point3d p) => new[] { p.X, p.Y, p.Z };
        internal static double[] Vector(Vector3d p) => new[] { p.X, p.Y, p.Z };
        internal static Point3d Point(double[] p) => new Point3d(p[0], p[1], p[2]);
        internal static void Require(bool condition, string error)
        { if (!condition) throw new ArgumentException(error); }
    }
}
