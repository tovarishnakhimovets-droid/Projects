using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Geometry;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class EditPolylineVerticesHandler : IAcadCommand
    {
        public string Name => "edit_polyline_vertices";
        public string Description => "Edit guarded lightweight polyline vertex positions in place, preserving identity and segment metadata.";
        public CommandSchema Schema => CommandSchemas.EditPolylineVertices;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!PolylineVertexEditRequest.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            bool commitStarted = false;
            try
            {
                Require(LineweightTargetPreflight.TryCheckDrawing(doc, out error), error);
                var watch = Stopwatch.StartNew();
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var targets = new Dictionary<string, Polyline>();
                    var plans = new Dictionary<string, PolylineGeometryState>();
                    var metadata = new Dictionary<string, PreservedMetadata>();
                    // All targets are read, guarded and planned before any UpgradeOpen or setter.
                    foreach (var change in input.Changes)
                    {
                        Require(CadHandleResolver.TryResolve(db, change.Handle, out var id, out error), error);
                        Require(!id.IsErased && !id.IsEffectivelyErased, "polyline is erased: " + change.Handle);
                        var polyline = tx.GetObject(id, OpenMode.ForRead) as Polyline;
                        Require(polyline != null && polyline.GetType() == typeof(Polyline), "target must be a native lightweight Polyline: " + change.Handle);
                        Require(polyline.OwnerId == db.CurrentSpaceId, "target must belong to the current layout space: " + change.Handle);
                        Require(LineweightTargetPreflight.TryCheckEntity(tx, polyline, out error), error);
                        Require(polyline.NumberOfVertices == change.Expected.Vertices.Length, "polyline vertex count changed: " + change.Handle);
                        Require(polyline.Normal.X == 0 && polyline.Normal.Y == 0 && polyline.Normal.Z == 1, "only exact +Z normal is supported");
                        targets.Add(change.Handle, polyline);
                        plans.Add(change.Handle, PolylineVertexEditRequest.Plan(change, Capture(polyline), input.MinSegmentLength));
                        metadata.Add(change.Handle, PreservedMetadata.Capture(polyline));
                        CheckTime(watch);
                    }
                    if (!input.DryRun)
                    {
                        error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                        Require(error == null, error);
                        Require(LineweightTargetPreflight.TryCheckDrawing(doc, out error), error);
                        foreach (var polyline in targets.Values)
                        {
                            polyline.UpgradeOpen();
                            Require(polyline.IsWriteEnabled, "target cannot be opened for writing");
                        }
                        foreach (var change in input.Changes)
                        {
                            Require(change.Expected.Same(Capture(targets[change.Handle])), "polyline changed while opening targets for writing");
                            Require(metadata[change.Handle].Same(targets[change.Handle]), "polyline metadata changed while opening targets for writing");
                        }
                        foreach (var change in input.Changes)
                        {
                            var polyline = targets[change.Handle];
                            foreach (var edit in change.Edits)
                            {
                                var old = polyline.GetPoint3dAt(edit.Index);
                                if (old.X == edit.Position[0] && old.Y == edit.Position[1]) continue;
                                // SetPointAt consumes OCS; exactly +Z makes XY equal WCS XY. Elevation is retained.
                                // https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Polyline_SetPointAt_int_Point2d.html
                                polyline.SetPointAt(edit.Index, new Point2d(edit.Position[0], edit.Position[1]));
                            }
                            CheckTime(watch);
                        }
                        // Verify every target after all setters, catching synchronous native side effects on earlier targets.
                        foreach (var change in input.Changes)
                        {
                            var polyline = targets[change.Handle];
                            Require(plans[change.Handle].Same(Capture(polyline)), "native vertex or segment metadata readback mismatch; transaction aborted");
                            Require(metadata[change.Handle].Same(polyline), "native identity/style/XData changed; transaction aborted");
                            Require(polyline.Handle.ToString().Equals(change.Handle, StringComparison.OrdinalIgnoreCase), "native handle changed; transaction aborted");
                            CheckTime(watch);
                        }
                    }
                    var result = CommandResult.Success(new
                    {
                        document = doc.Name, fingerprint = db.FingerprintGuid.ToString(), dry_run = input.DryRun,
                        committed = !input.DryRun, atomic = true, grouped_undo = false, saved = false,
                        count = input.Changes.Length,
                        results = input.Changes.Select(change => new
                        {
                            handle = change.Handle, retained_handle = true, edited_indices = change.Edits.Select(edit => edit.Index).ToArray(),
                            vertices = plans[change.Handle].Vertices.Select(vertex => new
                            {
                                index = vertex.Index, x = vertex.X, y = vertex.Y, z = vertex.Z,
                                bulge = vertex.Bulge, start_width = vertex.StartWidth, end_width = vertex.EndWidth
                            }).ToArray()
                        }).ToArray()
                    });
                    if (!input.DryRun)
                    {
                        commitStarted = true;
                        tx.Commit();
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("polyline vertex edit failed: " + ErrorSanitizer.Sanitize(ex.Message) +
                    (commitStarted ? "; commit outcome is uncertain; inspect before retry" : ""));
            }
        }

        private static PolylineGeometryState Capture(Polyline polyline)
        {
            var vertices = new PolylineVertexState[polyline.NumberOfVertices];
            for (int index = 0; index < vertices.Length; index++)
            {
                var point = polyline.GetPoint3dAt(index);
                vertices[index] = new PolylineVertexState
                {
                    Index = index, X = point.X, Y = point.Y, Z = point.Z, Bulge = polyline.GetBulgeAt(index),
                    StartWidth = polyline.GetStartWidthAt(index), EndWidth = polyline.GetEndWidthAt(index)
                };
            }
            return new PolylineGeometryState
            {
                Layer = polyline.Layer, OwnerHandle = polyline.OwnerId.Handle.ToString(),
                Normal = new[] { polyline.Normal.X, polyline.Normal.Y, polyline.Normal.Z },
                Elevation = polyline.Elevation, Thickness = polyline.Thickness, ConstantWidth = polyline.ConstantWidth,
                Closed = polyline.Closed, Vertices = vertices
            };
        }

        private sealed class PreservedMetadata
        {
            private object[] properties;
            private TypedValue[] xdata;
            private ObjectId[] reactors;
            internal static PreservedMetadata Capture(Polyline polyline)
            {
                using (var buffer = polyline.XData)
                    return new PreservedMetadata
                    {
                        properties = Properties(polyline),
                        xdata = (buffer?.AsArray() ?? Array.Empty<TypedValue>()).Select(value =>
                            new TypedValue(value.TypeCode, value.Value is byte[] bytes ? bytes.ToArray() : value.Value)).ToArray(),
                        reactors = polyline.GetPersistentReactorIds().Cast<ObjectId>().ToArray()
                    };
            }
            internal bool Same(Polyline polyline)
            {
                if (!properties.SequenceEqual(Properties(polyline)) || !reactors.SequenceEqual(polyline.GetPersistentReactorIds().Cast<ObjectId>())) return false;
                using (var buffer = polyline.XData)
                {
                    var actual = buffer?.AsArray() ?? Array.Empty<TypedValue>();
                    return xdata.Length == actual.Length && xdata.Zip(actual, (a, b) =>
                        a.TypeCode == b.TypeCode && StructuralComparisons.StructuralEqualityComparer.Equals(a.Value, b.Value)).All(equal => equal);
                }
            }
            private static object[] Properties(Polyline polyline) => new object[]
            {
                polyline.ObjectId, polyline.OwnerId, polyline.LayerId, polyline.LinetypeId, polyline.LinetypeScale,
                polyline.LineWeight, polyline.ColorIndex, polyline.Color.ColorMethod, polyline.Color.ColorValue,
                polyline.Transparency, polyline.Visible, polyline.ExtensionDictionary, polyline.MaterialId,
                polyline.PlotStyleNameId, polyline.Plinegen, polyline.Annotative
            };
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new ArgumentException(message ?? "polyline preflight failed"); }
        private static void CheckTime(Stopwatch watch)
        { Require(watch.ElapsedMilliseconds <= 15000, "operation exceeded fifteen seconds; transaction aborted; reduce scope"); }
    }
}
