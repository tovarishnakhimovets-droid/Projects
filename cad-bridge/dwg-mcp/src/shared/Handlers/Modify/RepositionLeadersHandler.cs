using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class RepositionLeadersHandler : IAcadCommand
    {
        public string Name => "reposition_leaders";
        public string Description => "Move guarded plain MLeader text and terminal, preserving its arrow and appearance.";
        public CommandSchema Schema => CommandSchemas.RepositionLeaders;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!LeaderRepositionRequest.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            bool commitStarted = false;
            try
            {
                Require(LineweightTargetPreflight.TryCheckDrawing(doc, out error), error);
                var db = doc.Database; var watch = Stopwatch.StartNew();
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var targets = new Dictionary<string, MLeader>(); var retained = new Dictionary<string, Metadata>();
                    foreach (var change in input.Changes)
                    {
                        Require(CadHandleResolver.TryResolve(db, change.Handle, out var id, out error), error);
                        Require(!id.IsErased && !id.IsEffectivelyErased, "leader is erased");
                        var leader = tx.GetObject(id, OpenMode.ForRead) as MLeader;
                        Require(leader != null && leader.OwnerId == db.CurrentSpaceId, "leader must belong to current layout space");
                        Require(LineweightTargetPreflight.TryCheckEntity(tx, leader, out error), error);
                        LeaderRepositionRequest.CheckSnapshot(change, LeaderRepositionNative.Capture(leader, out _, out _));
                        targets.Add(change.Handle, leader); retained.Add(change.Handle, Metadata.Capture(leader)); CheckTime(watch);
                    }
                    if (!input.DryRun)
                    {
                        Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint) == null, "active drawing changed");
                        foreach (var leader in targets.Values) { leader.UpgradeOpen(); Require(leader.IsWriteEnabled, "leader cannot be opened for writing"); }
                        foreach (var change in input.Changes)
                        {
                            var leader = targets[change.Handle]; LeaderRepositionRequest.CheckSnapshot(change, LeaderRepositionNative.Capture(leader, out _, out _));
                            Require(retained[change.Handle].Same(leader), "leader metadata changed during preflight");
                        }
                        foreach (var change in input.Changes)
                        {
                            var leader = targets[change.Handle]; LeaderRepositionNative.Capture(leader, out int cluster, out int line);
                            double length = leader.GetDoglegLength(cluster);
                            // Native setters only: never TransformBy/MoveMLeader, and never rewrite MText content.
                            // TextLocation is the native content anchor, not necessarily its bounding-box corner.
                            double sign = change.DoglegSide == "right" ? 1d : -1d;
                            leader.SetDogleg(cluster, new Vector3d(sign, 0, 0)); leader.SetDoglegLength(cluster, length);
                            leader.TextLocation = LeaderRepositionNative.Point(change.NewTextLocation);
                            leader.SetLastVertex(line, LeaderRepositionNative.Point(change.NewTerminal));
                            CheckTime(watch);
                        }
                        foreach (var change in input.Changes)
                        {
                            var leader = targets[change.Handle]; var state = LeaderRepositionNative.Capture(leader, out _, out _);
                            Require(state.LeaderIndex == change.Expected.LeaderIndex && state.LineIndex == change.Expected.LineIndex &&
                                state.TextLocation.SequenceEqual(change.NewTextLocation) && state.Vertices[0].SequenceEqual(change.Expected.Vertices[0]) &&
                                state.Vertices[1].SequenceEqual(change.NewTerminal) && Math.Sign(state.Dogleg[0]) == (change.DoglegSide == "right" ? 1 : -1),
                                "native text/arrow/terminal/dogleg readback mismatch; transaction aborted");
                            Require(retained[change.Handle].Same(leader), "native text/appearance/identity changed; transaction aborted");
                            CheckTime(watch);
                        }
                    }
                    var results = input.Changes.Select(change => new
                    {
                        handle = change.Handle,
                        arrow_preserved = true,
                        actual_snapshot = LeaderRepositionNative.Describe(targets[change.Handle]),
                        text_bounds = LeaderRepositionNative.Bounds(targets[change.Handle]),
                        proposed_text_location = change.NewTextLocation,
                        proposed_terminal = change.NewTerminal,
                        dogleg_side = change.DoglegSide
                    }).ToArray();
                    if (!input.DryRun) { commitStarted = true; tx.Commit(); }
                    return CommandResult.Success(new { dry_run = input.DryRun, committed = !input.DryRun, atomic = true, grouped_undo = false, saved = false, count = results.Length, results });
                }
            }
            catch (Exception ex) { return CommandResult.Fail("leader reposition failed: " + ErrorSanitizer.Sanitize(ex.Message) + (commitStarted ? "; commit outcome uncertain; inspect before retry" : "")); }
        }
        private sealed class Metadata
        {
            private object[] values; private TypedValue[] xdata; private ObjectId[] reactors;
            internal static Metadata Capture(MLeader leader)
            {
                using (var buffer = leader.XData) return new Metadata
                {
                    values = Properties(leader),
                    xdata = (buffer?.AsArray() ?? Array.Empty<TypedValue>()).Select(x => new TypedValue(x.TypeCode, x.Value is byte[] b ? b.ToArray() : x.Value)).ToArray(),
                    reactors = leader.GetPersistentReactorIds().Cast<ObjectId>().ToArray()
                };
            }
            internal bool Same(MLeader leader)
            {
                if (!values.SequenceEqual(Properties(leader)) || !reactors.SequenceEqual(leader.GetPersistentReactorIds().Cast<ObjectId>())) return false;
                using (var buffer = leader.XData) { var actual = buffer?.AsArray() ?? Array.Empty<TypedValue>(); return xdata.Length == actual.Length && xdata.Zip(actual, (a, b) => a.TypeCode == b.TypeCode && StructuralComparisons.StructuralEqualityComparer.Equals(a.Value, b.Value)).All(x => x); }
            }
            private static object[] Properties(MLeader l)
            {
                var cluster = l.GetLeaderIndexes().Cast<int>().Single(); var line = l.GetLeaderLineIndexes(cluster).Cast<int>().Single();
                using (var t = l.MText) return new object[]{l.ObjectId,l.OwnerId,l.LayerId,l.LinetypeId,l.LinetypeScale,l.LineWeight,
                    l.Color.ColorMethod,l.Color.ColorValue,l.Transparency,l.Visible,l.ExtensionDictionary,l.MaterialId,l.PlotStyleNameId,
                    l.MLeaderStyle,l.ContentType,l.TextStyleId,l.TextHeight,l.ArrowSize,l.ArrowSymbolId,l.Scale,l.LeaderLineType,l.LeaderLineTypeId,l.LeaderLineWeight,
                    l.LeaderLineColor.ColorMethod,l.LeaderLineColor.ColorValue,l.TextColor.ColorMethod,l.TextColor.ColorValue,
                    l.EnableLanding,l.EnableDogleg,l.EnableFrameText,l.EnableAnnotationScale,l.ExtendLeaderToText,l.LandingGap,l.GetDoglegLength(cluster),
                    l.GetArrowSymbolId(line),l.GetLeaderLineTypeId(line),l.GetLeaderLineWeight(line),l.GetLeaderLineColor(line).ColorMethod,l.GetLeaderLineColor(line).ColorValue,
                    t.Contents,t.TextHeight,t.TextStyleId,t.Width,t.Rotation,t.Color.ColorMethod,t.Color.ColorValue};
            }
        }
        private static void Require(bool condition, string error) => LeaderRepositionNative.Require(condition, error);
        private static void CheckTime(Stopwatch watch) => Require(watch.ElapsedMilliseconds <= 15000, "operation exceeded fifteen seconds; reduce scope");
    }
}
