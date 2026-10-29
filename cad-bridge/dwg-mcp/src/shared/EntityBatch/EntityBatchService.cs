using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.EntityBatch
{
    internal static class EntityBatchService
    {
        internal static JObject Create(Document doc, EntityBatchInput input)
        {
            Require(doc != null, "an active document is required");
            Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
            Require(!doc.IsReadOnly, "drawing is read-only");
            Require(doc.Editor.IsQuiescent, "finish the active AutoCAD command before batch creation");
            var db = doc.Database;
            Require(EntityBatchInput.ValidateUndoState(db.UndoRecording, Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
            var watch = Stopwatch.StartNew();
            var prepared = new List<Entity>();
            var resident = new HashSet<Entity>();
            bool commitAttempted = false;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var owner = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    Require(owner.IsLayout && !owner.IsDependent && !owner.IsFromExternalReference && !owner.IsFromOverlayReference,
                        "current drawing space must be a local writable layout");
                    var layers = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var styles = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    var layerIds = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
                    // Read every native dependency before entity construction or opening anything for write.
                    var preparedById = new Dictionary<string, Entity>(StringComparer.Ordinal);
                    foreach (var item in input.Items)
                    {
                        Time(watch);
                        if (!layerIds.ContainsKey(item.Layer))
                        {
                            Require(layers.Has(item.Layer), "existing target layer not found: " + item.Layer);
                            var layer = (LayerTableRecord)tx.GetObject(layers[item.Layer], OpenMode.ForRead);
                            Require(!layer.IsLocked && !layer.IsDependent && !layer.IsErased, "locked/dependent target layer: " + item.Layer);
                            layerIds.Add(item.Layer, layer.ObjectId);
                        }
                        if (item.Type == "text" || item.Type == "mtext")
                        {
                            string name = (string)item.Parameters["text_style"];
                            Require(styles.Has(name), "existing text style not found: " + name);
                            var style = (TextStyleTableRecord)tx.GetObject(styles[name], OpenMode.ForRead);
                            Require(!style.IsErased && !style.IsDependent && !style.IsShapeFile && !style.IsVertical && style.Annotative != AnnotativeStates.True,
                                "text style must be local, horizontal and non-annotative: " + name);
                        }
                    }
                    var plan = new JArray();
                    foreach (var item in input.Items) plan.Add(new JObject { ["id"] = item.Id, ["type"] = item.Type, ["layer"] = item.Layer });
                    if (input.DryRun) return new JObject { ["dry_run"] = true, ["committed"] = false, ["saved"] = false,
                        ["count"] = input.Items.Length, ["plan"] = plan, ["single_undo_verified"] = false };

                    // Native entity constructors can fail. Prepare the entire transient set before DB writes.
                    foreach (var item in input.Items)
                    {
                        Time(watch);
                        var entity = Make(item, db, tx);
                        prepared.Add(entity);
                        entity.LayerId = layerIds[item.Layer];
                        entity.Color = Color(item.Parameters);
                        entity.LinetypeId = db.ByLayerLinetype;
                        entity.LineWeight = LineWeight.ByLayer;
                        if (Present(item.Parameters, "lineweight_mm"))
                        {
                            Require(LineweightSpec.TryParse(item.Parameters["lineweight_mm"], "explicit", out var weight, out var error), error);
                            entity.LineWeight = (LineWeight)weight;
                        }
                        if (entity is Hatch fill)
                        {
                            var boundary = preparedById[(string)item.Parameters["boundary_id"]];
                            fill.Normal = Vector3d.ZAxis;
                            fill.Elevation = boundary is Circle circle ? circle.Center.Z : ((Polyline)boundary).Elevation;
                        }
                        preparedById.Add(item.Id, entity);
                    }
                    Time(watch);
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
                    Require(EntityBatchInput.ValidateUndoState(db.UndoRecording, Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
                    // DocumentInvoker owns one main-thread document lock around this entire handler.
                    // Autodesk documents its lock/unlock as a command Undo bracket. StartUndoRecord
                    // is not exposed by the pinned managed API. Do not disable Undo or send commands.
                    owner.UpgradeOpen();
                    var byId = new Dictionary<string, Entity>(StringComparer.Ordinal);
                    var results = new JArray();
                    DrawOrderTable drawOrder = null;
                    for (int i = 0; i < input.Items.Length; i++)
                    {
                        Time(watch);
                        var item = input.Items[i]; var entity = prepared[i];
                        owner.AppendEntity(entity); tx.AddNewlyCreatedDBObject(entity, true); resident.Add(entity);
                        byId.Add(item.Id, entity);
                        if (entity is Hatch hatch)
                        {
                            var boundary = byId[(string)item.Parameters["boundary_id"]];
                            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                            hatch.Associative = item.Parameters.Value<bool?>("associative") ?? true;
                            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { boundary.ObjectId });
                            hatch.EvaluateHatch(true);
                            if (drawOrder == null) drawOrder = (DrawOrderTable)tx.GetObject(owner.DrawOrderTableId, OpenMode.ForWrite);
                            drawOrder.MoveToTop(new ObjectIdCollection { hatch.ObjectId });
                            drawOrder.MoveAbove(new ObjectIdCollection { boundary.ObjectId }, hatch.ObjectId);
                        }
                        Require(entity.OwnerId == owner.ObjectId && !entity.ObjectId.IsNull && !entity.IsErased,
                            "created entity did not retain its local owner");
                        results.Add(new JObject { ["id"] = item.Id, ["type"] = item.Type, ["handle"] = entity.Handle.ToString(),
                            ["layer"] = item.Layer });
                    }
                    Time(watch);
                    var result = new JObject { ["dry_run"] = false, ["committed"] = true, ["saved"] = false,
                        ["count"] = results.Count, ["entities"] = results, ["atomic"] = true,
                        ["document_path"] = input.ExpectedDocument, ["fingerprint"] = input.ExpectedFingerprint,
                        ["undo_scope"] = "one invocation document lock and one transaction", ["single_undo_verified"] = false };
                    commitAttempted = true; tx.Commit(); return result;
                }
            }
            catch (Exception ex) when (commitAttempted)
            { throw new InvalidOperationException("Commit was attempted; inspect actual drawing state before retrying: " + ex.Message, ex); }
            finally
            {
                foreach (var entity in prepared) if (!resident.Contains(entity)) entity.Dispose();
            }
        }

        private static Entity Make(EntityBatchItemInput item, Database db, Transaction tx)
        {
            var p = item.Parameters;
            Entity entity = null;
            try
            {
                switch (item.Type)
                {
                    case "line": entity = new Line(Point(p["start"]), Point(p["end"])); break;
                    case "circle": entity = new Circle(Point(p["center"]), Vector3d.ZAxis, (double)p["radius"]); break;
                    case "point": entity = new DBPoint(Point(p["position"])); break;
                    case "arc": entity = new Arc(Point(p["center"]), (double)p["radius"], (double)p["start_angle"] * Math.PI / 180, (double)p["end_angle"] * Math.PI / 180); break;
                    case "ellipse":
                        double rotation = Radians(p.Value<double?>("rotation") ?? 0), major = (double)p["major_radius"];
                        entity = new Ellipse(Point(p["center"]), Vector3d.ZAxis,
                            new Vector3d(Math.Cos(rotation) * major, Math.Sin(rotation) * major, 0), (double)p["minor_radius"] / major, 0, Math.PI * 2); break;
                    case "polyline":
                        var poly = new Polyline(); entity = poly;
                        var points = (JArray)p["points"];
                        for (int i = 0; i < points.Count; i++) { var point = Point(points[i]); poly.AddVertexAt(i, new Point2d(point.X, point.Y), p["bulges"] is JArray bulges ? (double)bulges[i] : 0, 0, 0); }
                        poly.Elevation = Point(points[0]).Z; poly.Closed = p.Value<bool?>("closed") ?? false; break;
                    case "rectangle":
                        var rectangle = new Polyline(); entity = rectangle;
                        var origin = Point(p["position"]); double width = (double)p["width"], height = (double)p["height"];
                        double angle = Radians(p.Value<double?>("rotation") ?? 0), c = Math.Cos(angle), s = Math.Sin(angle);
                        var corners = new[] { new Point2d(0, 0), new Point2d(width, 0), new Point2d(width, height), new Point2d(0, height) };
                        for (int i = 0; i < 4; i++) rectangle.AddVertexAt(i, new Point2d(origin.X + corners[i].X * c - corners[i].Y * s, origin.Y + corners[i].X * s + corners[i].Y * c), 0, 0, 0);
                        rectangle.Elevation = origin.Z; rectangle.Closed = true; break;
                    case "text":
                        var textRequest = TextRequest(p, false);
                        Require(AnnotationEntityFactory.TryCreateText(textRequest, out var text, out var textError, db, tx), textError);
                        return text;
                    case "mtext":
                        var mtextRequest = TextRequest(p, true);
                        Require(AnnotationEntityFactory.TryCreateMText(mtextRequest, out var mtext, out var mtextError, db, tx), mtextError);
                        return mtext;
                    case "hatch":
                        entity = new Hatch { Normal = Vector3d.ZAxis, HatchStyle = HatchStyle.Normal };
                        // Hatch elevation is assigned from its validated local boundary during preparation.
                        break;
                    default: throw new ArgumentException("unsupported creation type");
                }
                entity.SetDatabaseDefaults(db);
                return entity;
            }
            catch { entity?.Dispose(); throw; }
        }
        private static JObject TextRequest(JObject p, bool mtext)
        {
            var result = new JObject { ["text"] = p["text"].DeepClone(), [mtext ? "location" : "position"] = p["position"].DeepClone(),
                ["text_style"] = p["text_style"].DeepClone(), ["height"] = p.Value<double?>("height") ?? 2.5,
                ["rotation"] = p.Value<double?>("rotation") ?? 0 };
            if (mtext) result["width"] = p.Value<double?>("width") ?? 100;
            else result["width_factor"] = p.Value<double?>("width_factor") ?? 1;
            return result;
        }
        private static Color Color(JObject p)
        {
            if (p["color_rgb"] is JArray rgb) return Autodesk.AutoCAD.Colors.Color.FromRgb((byte)(int)rgb[0], (byte)(int)rgb[1], (byte)(int)rgb[2]);
            int index = p.Value<int?>("color_index") ?? 256;
            return Autodesk.AutoCAD.Colors.Color.FromColorIndex(index == 256 ? ColorMethod.ByLayer : ColorMethod.ByAci, (short)index);
        }
        private static Point3d Point(JToken p) => new Point3d((double)p["x"], (double)p["y"], p.Value<double?>("z") ?? 0);
        private static double Radians(double degrees) => degrees % 360 * Math.PI / 180;
        private static bool Present(JObject p, string name) => p[name] != null && p[name].Type != JTokenType.Null;
        private static void Require(bool condition, string error) { if (!condition) throw new ArgumentException(error); }
        private static void Require(string error) { if (error != null) throw new ArgumentException(error); }
        private static void Time(Stopwatch watch) => Require(watch.ElapsedMilliseconds < 15000, "15-second cooperative batch budget exceeded");
    }
}
