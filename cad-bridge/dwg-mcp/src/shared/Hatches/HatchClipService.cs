using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Exception = System.Exception;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.EntityBatch;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Hatches
{
    internal static class HatchClipService
    {
        private sealed class Item : IDisposable
        {
            internal Hatch Source;
            internal JObject Signature, Receipt;
            internal string Action;
            internal readonly List<Region> Pieces = new List<Region>();
            internal readonly List<List<List<Curve>>> Faces = new List<List<List<Curve>>>();
            internal readonly List<Hatch> Outputs = new List<Hatch>(); // wrappers owned by the inner resource scope
            public void Dispose()
            {
                foreach (var r in Pieces) r.Dispose();
            }
        }

        internal static JObject Execute(Document doc, HatchClipInput input, bool plan)
        {
            Need(DocumentGuard.Validate(doc, input.Document, input.Fingerprint) == null, "active drawing identity changed");
            var db = doc.Database;
            var revision = DrawingRevision.For(doc);
            long start = revision.Revision;
            if (!plan) Need(revision.SessionId == input.Session, "drawing session changed; obtain a fresh plan");
            var watch = Stopwatch.StartNew();
            var masks = new List<Region>(); var items = new List<Item>();
            bool commitAttempted = false;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                using (var resources = new HatchClipResources())
                {
                    var modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    Need(db.CurrentSpaceId == modelId, "activate model space before clipping");
                    var boundaries = input.Boundaries.Select(h => Open<Polyline>(db, tx, h, modelId)).ToArray();
                    var boundarySignatures = new JArray(boundaries.Select(p => MaskSignature(p)));
                    var geometry = new HatchClipGeometry(boundaries[0].GetPoint3dAt(0), watch);
                    foreach (var p in boundaries) masks.Add(geometry.PolylineRegion(p));
                    geometry.UnionMasks(masks);
                    var sourceSignatures = new JArray(); var receipts = new JArray();
                    int outputCount = 0;
                    foreach (string handle in input.Hatches)
                    {
                        geometry.Time();
                        var hatch = Open<Hatch>(db, tx, handle, modelId);
                        Need(!hatch.Associative, "disable existing hatch associativity before clipping: " + handle);
                        Need(!hatch.IsGradient, "gradient hatches are unsupported: " + handle);
                        var item = new Item { Source = hatch }; items.Add(item);
                        var source = geometry.HatchRegions(hatch, out var shape);
                        try
                        {
                            item.Signature = SourceSignature(hatch, shape); sourceSignatures.Add(item.Signature);
                            double before = source.Sum(r => r.Area);
                            item.Pieces.AddRange(geometry.Intersect(source, masks, out var inside));
                            double after = item.Pieces.Sum(r => r.Area);
                            Need(HatchClipInput.CheckArea(before, after) == null, "invalid clip area: " + handle);
                            item.Action = inside ? "keep" : item.Pieces.Count == 0 ? "remove" : "clip";
                            if (item.Action == "clip")
                                foreach (var piece in item.Pieces) ReadFaces(piece, item, geometry, resources, ref outputCount);
                            item.Receipt = new JObject { ["handle"] = handle, ["action"] = item.Action,
                                ["source_area"] = before, ["retained_area"] = after,
                                ["removed_area"] = Math.Max(0, before - after), ["output_hatch_count"] = item.Faces.Count,
                                ["reported_hatch_area"] = JValue.CreateNull(), ["reported_hatch_area_read"] = false,
                                ["xdata_records"] = ((JArray)item.Signature["appearance"]["xdata"]).Count };
                            receipts.Add(item.Receipt);
                        }
                        finally { foreach (var r in source) r.Dispose(); }
                    }
                    geometry.Time();
                    Need(revision.Revision == start, "drawing changed during clip planning; obtain a fresh plan");
                    string hash = HatchClipInput.Hash(new JObject { ["session"] = revision.SessionId,
                        ["document"] = input.Document, ["fingerprint"] = db.FingerprintGuid,
                        ["boundaries"] = boundarySignatures, ["sources"] = sourceSignatures });
                    if (!plan) Need(hash == input.PlanHash, "hatch or boundary snapshot changed; obtain a fresh plan");
                    var result = new JObject { ["document_path"] = input.Document, ["fingerprint"] = db.FingerprintGuid,
                        ["drawing_session"] = revision.SessionId, ["preflight_revision"] = start, ["plan_hash"] = hash,
                        ["complete"] = true, ["dry_run"] = plan || input.DryRun, ["committed"] = false, ["saved"] = false,
                        ["mask_semantics"] = "positive union of explicit closed polylines", ["results"] = receipts,
                        ["source_area"] = items.Sum(i => i.Receipt.Value<double>("source_area")),
                        ["retained_area"] = items.Sum(i => i.Receipt.Value<double>("retained_area")),
                        ["planned_created_count"] = outputCount, ["single_undo_verified"] = false };
                    if (plan || input.DryRun) return result;
                    Need(!doc.IsReadOnly && doc.Editor.IsQuiescent, "drawing is read-only or AutoCAD has an active command");
                    Need(EntityBatchInput.ValidateUndoState(db.UndoRecording, Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
                    Need(DocumentGuard.Validate(doc, input.Document, input.Fingerprint) == null, "active drawing changed before clipping");
                    var model = (BlockTableRecord)tx.GetObject(modelId, OpenMode.ForWrite);
                    int created = 0;
                    foreach (var item in items)
                    {
                        geometry.Time();
                        if (item.Action == "keep") { item.Receipt["retained_handle"] = item.Source.Handle.ToString(); continue; }
                        var handles = new JArray(); var newIds = new ObjectIdCollection(); var rebuilt = new List<Region>();
                        try
                        {
                            foreach (var face in item.Faces)
                            {
                                var hatch = AppendFace(tx, model, item.Source, face, geometry, resources);
                                item.Outputs.Add(hatch);
                                handles.Add(hatch.Handle.ToString()); newIds.Add(hatch.ObjectId); created++;
                                rebuilt.AddRange(geometry.HatchRegions(hatch, out _));
                            }
                            double expected = item.Receipt.Value<double>("retained_area"), actual = rebuilt.Sum(r => r.Area);
                            double tolerance = HatchClipInput.AreaTolerance(expected);
                            Need(Math.Abs(actual - expected) <= tolerance, "rebuilt hatch area differs from planned clip");
                            // Check both missing and extra geometry; equal areas alone do not prove placement.
                            CheckCoverage(geometry, rebuilt, item.Pieces, tolerance);
                            CheckCoverage(geometry, item.Pieces, rebuilt, tolerance);
                        }
                        finally { foreach (var r in rebuilt) r.Dispose(); }
                        // A reactor triggered by appends must not silently change the source being erased.
                        Open<Hatch>(db, tx, item.Source.Handle.ToString(), modelId);
                        var current = geometry.HatchRegions(item.Source, out var currentShape);
                        try { Need(JToken.DeepEquals(item.Signature, SourceSignature(item.Source, currentShape)), "source changed during append; transaction aborted"); }
                        finally { foreach (var r in current) r.Dispose(); }
                        if (newIds.Count > 0)
                            ((DrawOrderTable)tx.GetObject(model.DrawOrderTableId, OpenMode.ForWrite)).MoveBelow(newIds, item.Source.ObjectId);
                        item.Source.UpgradeOpen(); item.Source.Erase();
                        Need(item.Source.IsErased, "source hatch was not erased");
                        item.Receipt["erased"] = true; item.Receipt["created_handles"] = handles;
                    }
                    // Inspect the final batch state as well: later appends/erases
                    // may have triggered application-level native reactors.
                    foreach (var item in items)
                    {
                        if (item.Action == "keep")
                        {
                            Open<Hatch>(db, tx, item.Source.Handle.ToString(), modelId);
                            var current = geometry.HatchRegions(item.Source, out var currentShape);
                            try { Need(JToken.DeepEquals(item.Signature, SourceSignature(item.Source, currentShape)), "retained hatch changed during clipping"); }
                            finally { foreach (var r in current) r.Dispose(); }
                            continue;
                        }
                        var finalRegions = new List<Region>();
                        try
                        {
                            foreach (var hatch in item.Outputs)
                            {
                                Open<Hatch>(db, tx, hatch.Handle.ToString(), modelId);
                                Need(JToken.DeepEquals(item.Signature["appearance"], AppearanceSignature(hatch)), "output appearance changed during clipping");
                                finalRegions.AddRange(geometry.HatchRegions(hatch, out _));
                            }
                            double expected = item.Receipt.Value<double>("retained_area"), actual = finalRegions.Sum(r => r.Area);
                            double tolerance = HatchClipInput.AreaTolerance(expected);
                            Need(Math.Abs(actual - expected) <= tolerance, "final output area differs from plan");
                            CheckCoverage(geometry, finalRegions, item.Pieces, tolerance);
                            CheckCoverage(geometry, item.Pieces, finalRegions, tolerance);
                        }
                        finally { foreach (var r in finalRegions) r.Dispose(); }
                    }
                    foreach (var boundary in boundaries) Open<Polyline>(db, tx, boundary.Handle.ToString(), modelId);
                    Need(JToken.DeepEquals(boundarySignatures, new JArray(boundaries.Select(p => MaskSignature(p)))), "boundary changed during clipping; transaction aborted");
                    Need(DocumentGuard.Validate(doc, input.Document, input.Fingerprint) == null, "active drawing changed before commit");
                    geometry.Time();
                    result["created_count"] = created; result["dry_run"] = false; result["atomic"] = true;
                    // Commit can close/uncreate database objects. Release the new
                    // managed wrappers before either Commit or Abort reaches them.
                    resources.Dispose();
                    commitAttempted = true; tx.Commit(); result["committed"] = true;
                    return result;
                }
            }
            catch (Exception e) when (commitAttempted)
            { throw new InvalidOperationException("Commit was attempted; inspect actual drawing state before retrying: " + e.Message, e); }
            finally { foreach (var item in items) item.Dispose(); foreach (var mask in masks) mask.Dispose(); }
        }

        private static T Open<T>(Database db, Transaction tx, string handle, ObjectId model) where T : Entity
        {
            var id = db.GetObjectId(false, new Handle(long.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0);
            var entity = tx.GetObject(id, OpenMode.ForRead) as T;
            Need(entity != null && entity.GetType() == typeof(T) && !entity.IsErased && !id.IsEffectivelyErased && entity.OwnerId == model,
                "target must be a live direct model-space " + typeof(T).Name + ": " + handle);
            Need(entity.ExtensionDictionary.IsNull && !entity.HasFields && entity.Annotative != AnnotativeStates.True, "extension dictionary, fields or annotation are unsupported: " + handle);
            XDataSignature(entity); // Validate bounded supported metadata before geometry work.
            Need(entity.GetPersistentReactorIds().Count == 0, "persistent associations are unsupported: " + handle);
            var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsDependent && !layer.IsErased, "locked or dependent layer: " + layer.Name);
            return entity;
        }
        private static JObject MaskSignature(Polyline p)
        {
            var vertices = new JArray();
            for (int i = 0; i < p.NumberOfVertices; i++) vertices.Add(new JArray(p.GetPoint2dAt(i).X, p.GetPoint2dAt(i).Y, p.GetBulgeAt(i), p.GetStartWidthAt(i), p.GetEndWidthAt(i)));
            return new JObject { ["handle"] = p.Handle.ToString(), ["owner"] = p.OwnerId.Handle.ToString(), ["layer"] = p.Layer,
                ["closed"] = p.Closed, ["normal"] = new JArray(p.Normal.X, p.Normal.Y, p.Normal.Z),
                ["elevation"] = p.Elevation, ["thickness"] = p.Thickness, ["width"] = p.ConstantWidth, ["vertices"] = vertices,
                ["xdata"] = XDataSignature(p) };
        }
        private static JObject SourceSignature(Hatch h, JObject shape) => new JObject {
            ["handle"] = h.Handle.ToString(), ["owner"] = h.OwnerId.Handle.ToString(), ["appearance"] = AppearanceSignature(h), ["shape"] = shape };
        private static JObject AppearanceSignature(Hatch h) => new JObject {
            ["layer"] = h.Layer,
            ["color"] = h.Color.ToString(), ["background"] = h.BackgroundColor.ToString(),
            ["transparency"] = TransparencySignature(h), ["lineweight"] = (int)h.LineWeight,
            ["linetype"] = h.Linetype, ["linetype_scale"] = h.LinetypeScale, ["visible"] = h.Visible,
            ["associative"] = h.Associative, ["pattern"] = h.PatternName, ["pattern_type"] = (int)h.PatternType,
            ["angle"] = h.PatternAngle, ["scale"] = h.PatternScale, ["space"] = h.PatternSpace,
            ["double"] = h.PatternDouble, ["origin"] = new JArray(h.Origin.X, h.Origin.Y),
            ["definitions"] = PatternSignature(h), ["xdata"] = XDataSignature(h) };

        private static JArray XDataSignature(Entity entity)
        {
            var records = new JArray();
            using (var buffer = entity.XData)
            {
                var values = buffer?.AsArray() ?? Array.Empty<TypedValue>();
                Need(values.Length <= 2048, "XData exceeds2048 records");
                foreach (var value in values)
                {
                    JToken data;
                    if (value.Value is Point3d point) data = new JArray(point.X, point.Y, point.Z);
                    else if (value.Value is byte[] bytes) data = new JValue(Convert.ToBase64String(bytes));
                    else if (value.Value is string || value.Value is double || value.Value is short || value.Value is int)
                        data = new JValue(value.Value);
                    else throw new ArgumentException("unsupported native XData value for code " + value.TypeCode);
                    records.Add(new JArray(value.TypeCode, data));
                }
            }
            return HatchClipInput.XDataSnapshot(records);
        }

        private static JObject TransparencySignature(Hatch hatch)
        {
            var transparency = hatch.Transparency;
            return HatchClipInput.TransparencySnapshot(transparency.IsByLayer, transparency.IsByBlock,
                transparency.IsByAlpha, transparency.IsInvalid, () => transparency.Alpha);
        }

        private static JArray PatternSignature(Hatch hatch)
        {
            Need(hatch.NumberOfPatternDefinitions <= 256, "pattern exceeds256 definition lines");
            var definitions = new JArray();
            for (int i = 0; i < hatch.NumberOfPatternDefinitions; i++)
            {
                var p = hatch.GetPatternDefinitionAt(i); var dashes = p.GetDashes();
                Need(dashes.Count <= 512, "pattern definition exceeds512 dashes");
                definitions.Add(new JObject { ["angle"] = p.Angle, ["base"] = new JArray(p.BaseX, p.BaseY),
                    ["offset"] = new JArray(p.OffsetX, p.OffsetY), ["dashes"] = new JArray(dashes.Cast<double>()) });
            }
            return definitions;
        }

        private static void ReadFaces(Region region, Item item, HatchClipGeometry geometry, HatchClipResources resources, ref int count)
        {
            using (var brep = new Brep(region))
                foreach (var face in brep.Faces)
                using (face)
                {
                    geometry.Time(); Need(++count <= HatchClipInput.MaxOutput, "output exceeds2000 hatches");
                    var loops = new List<List<Curve>>(); item.Faces.Add(loops);
                    int exteriors = 0;
                    foreach (var loop in face.Loops)
                    using (loop)
                    {
                        var curves = new List<Curve>();
                        bool exterior = loop.LoopType == LoopType.LoopExterior;
                        if (exterior) { loops.Insert(0, curves); exteriors++; } else loops.Add(curves);
                        foreach (var edge in loop.Edges)
                        using (edge)
                        {
                            geometry.Segment();
                            using (var external = edge.Curve as ExternalCurve3d)
                            {
                                Need(external != null, "unsupported BREP edge wrapper");
                                using (var native = external.NativeCurve)
                                {
                                    var curve = Curve.CreateFromGeCurve(native);
                                    Need(curve != null, "BREP edge could not be converted"); resources.Own(curve); curves.Add(curve);
                                    Need(curve is Line || curve is Arc || curve is Circle || curve is Ellipse, "unsupported output curve; no approximation was made");
                                }
                            }
                        }
                        Need(curves.Count > 0, "empty BREP loop");
                    }
                    Need(exteriors == 1, "BREP face must have exactly one exterior loop");
                }
        }
        private static Hatch AppendFace(Transaction tx, BlockTableRecord model, Hatch source, List<List<Curve>> face, HatchClipGeometry geometry, HatchClipResources resources)
        {
            var boundaryIds = new List<ObjectIdCollection>();
            foreach (var loop in face)
            {
                var ids = new ObjectIdCollection(); boundaryIds.Add(ids);
                for (int i = 0; i < loop.Count; i++)
                {
                    geometry.Time(); var curve = loop[i];
                    curve.TransformBy(Matrix3d.Displacement(geometry.Origin - Point3d.Origin));
                    curve.LayerId = source.LayerId;
                    model.AppendEntity(curve); tx.AddNewlyCreatedDBObject(curve, true); loop[i] = null;
                    ids.Add(curve.ObjectId);
                }
            }
            var hatch = resources.Own((Hatch)source.Clone());
            while (hatch.NumberOfLoops > 0) hatch.RemoveLoopAt(hatch.NumberOfLoops - 1);
            model.AppendEntity(hatch); tx.AddNewlyCreatedDBObject(hatch, true);
            hatch.Associative = false;
            for (int i = 0; i < boundaryIds.Count; i++) hatch.AppendLoop(i == 0 ? HatchLoopTypes.Outermost : HatchLoopTypes.Default, boundaryIds[i]);
            hatch.EvaluateHatch(true);
            Need(!hatch.Associative && hatch.NumberOfLoops == boundaryIds.Count, "rebuilt hatch loop count or association changed");
            Need(JToken.DeepEquals(AppearanceSignature(source), AppearanceSignature(hatch)), "native hatch rebuild changed appearance");
            foreach (var ids in boundaryIds) foreach (ObjectId id in ids) ((Entity)tx.GetObject(id, OpenMode.ForWrite)).Erase();
            return hatch;
        }
        private static void CheckCoverage(HatchClipGeometry geometry, List<Region> sources, List<Region> cover, double tolerance)
        {
            double uncovered = 0;
            foreach (var source in sources)
            {
                var rest = (Region)source.Clone();
                try
                {
                    foreach (var mask in cover)
                    {
                        geometry.Time(); if (HatchClipGeometry.Empty(rest)) break;
                        using (var operand = (Region)mask.Clone()) rest.BooleanOperation(BooleanOperationType.BoolSubtract, operand);
                    }
                    if (!HatchClipGeometry.Empty(rest)) uncovered += rest.Area;
                }
                finally { rest.Dispose(); }
            }
            Need(uncovered <= tolerance, "rebuilt geometry differs from planned clip");
        }
        private static void Need(bool ok, string message) => HatchClipInput.Need(ok, message);
        private static void Need(string error) { if (error != null) throw new ArgumentException(error); }
    }
}
