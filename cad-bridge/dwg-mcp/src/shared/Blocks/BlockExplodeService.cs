using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.EntityBatch;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static class BlockExplodeService
    {
        internal static JObject Execute(Document doc, BlockExplodeInput input, bool plan)
        {
            Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint) == null, "active drawing identity changed");
            var db = doc.Database;
            var revision = DrawingRevision.For(doc);
            long startRevision = revision.Revision;
            if (!plan) CheckRevision(revision, input);
            var watch = Stopwatch.StartNew();
            bool commitAttempted = false;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    Require(db.CurrentSpaceId == modelId, "activate model space before planning or exploding blocks");
                    var snapshots = new JArray();
                    var references = new List<BlockReference>();
                    var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
                    int total = 0;
                    foreach (var handle in input.Handles)
                    {
                        Time(watch);
                        Require(BlockReferenceResolver.TryOpen(db, tx, handle, OpenMode.ForRead, out var block, out var error), error);
                        var snapshot = Describe(db, tx, block, modelId, input.MaxEntities - total, histogram, watch);
                        total += (int)snapshot["definition_entity_count"];
                        snapshots.Add(snapshot);
                        references.Add(block);
                    }
                    Time(watch);
                    Require(revision.Revision == startRevision, "drawing changed during block inspection; make a new plan");
                    if (!plan)
                    {
                        CheckRevision(revision, input);
                        for (int i = 0; i < snapshots.Count; i++)
                            Require(BlockExplodeInput.SnapshotMatches((JObject)snapshots[i], (JObject)input.Snapshots[i]), "block snapshot changed: " + input.Handles[i]);
                        Require(!doc.IsReadOnly, "drawing is read-only");
                        Require(doc.Editor.IsQuiescent, "finish the active AutoCAD command before exploding blocks");
                        Require(EntityBatchInput.ValidateUndoState(db.UndoRecording, Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
                    }
                    if (plan || input.DryRun)
                    {
                        // Never call Entity.Explode during inspection/dry-run: it constructs unmanaged objects.
                        return new JObject { ["document_path"] = input.ExpectedDocument, ["fingerprint"] = input.ExpectedFingerprint,
                            ["drawing_session"] = revision.SessionId, ["drawing_revision"] = revision.Revision,
                            ["complete"] = true, ["dry_run"] = true, ["committed"] = false, ["saved"] = false,
                            ["snapshots"] = snapshots, ["reference_count"] = references.Count,
                            ["direct_entity_count"] = total, ["direct_entity_types"] = JObject.FromObject(histogram),
                            ["scope"] = "one level of each explicit model-space block; native output not constructed" };
                    }
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint) == null, "active drawing identity changed before write");
                    CheckRevision(revision, input);
                    var results = new JArray();
                    var outputTypes = new Dictionary<string, int>(StringComparer.Ordinal);
                    int created = 0;
                    for (int i = 0; i < references.Count; i++)
                    {
                        Time(watch);
                        var block = references[i];
                        var handles = AppendExploded(tx, block, input.MaxEntities - created, () => Time(watch),
                            entity => ValidateOutput(tx, db, entity, outputTypes));
                        created += handles.Length;
                        Require(handles.Length == (int)snapshots[i]["definition_entity_count"], "native explode did not preserve the direct entity count");
                        block.UpgradeOpen();
                        block.Erase();
                        Require(block.IsErased, "source block was not erased");
                        results.Add(new JObject { ["handle"] = block.Handle.ToString(), ["erased"] = true, ["created_handles"] = new JArray(handles) });
                    }
                    Require(SameCounts(histogram, outputTypes), "native explode changed direct entity types; batch aborted");
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint) == null, "active drawing identity changed before commit");
                    Time(watch);
                    var result = new JObject { ["document_path"] = input.ExpectedDocument, ["fingerprint"] = input.ExpectedFingerprint,
                        ["dry_run"] = false, ["committed"] = true, ["saved"] = false, ["atomic"] = true,
                        ["reference_count"] = references.Count, ["created_count"] = created, ["created_types"] = JObject.FromObject(outputTypes),
                        ["results"] = results, ["single_undo_verified"] = false,
                        ["scope"] = "one native explode level; existing hatches remain hatches; nested blocks remain references" };
                    commitAttempted = true;
                    tx.Commit();
                    return result;
                }
            }
            catch (Exception e) when (commitAttempted)
            { throw new InvalidOperationException("Commit was attempted; inspect actual drawing state before retrying: " + e.Message, e); }
        }

        private static JObject Describe(Database db, Transaction tx, BlockReference block, ObjectId modelId,
            int remaining, Dictionary<string, int> types, Stopwatch watch)
        {
            Require(block.GetType() == typeof(BlockReference), "only ordinary BlockReference objects are supported");
            Require(!block.IsErased && !block.ObjectId.IsEffectivelyErased && block.OwnerId == modelId, "source must be a live direct model-space block reference");
            Require(!block.IsDynamicBlock && block.Annotative != AnnotativeStates.True, "dynamic or annotative block references are unsupported");
            Require(block.AttributeCollection.Count == 0, "block attributes would be lost by native explode; source refused");
            Require(block.ExtensionDictionary.IsNull && !block.HasFields, "block extension dictionary/clipping/fields are unsupported");
            using (var data = block.XData) Require(data == null || data.AsArray().Length == 0, "block XData would be lost by explode");
            Require(block.GetPersistentReactorIds().Count == 0, "block persistent associations are unsupported");
            var scale = block.ScaleFactors;
            Require(scale.X > 0 && scale.X == scale.Y && scale.Y == scale.Z, "only positive uniform block scale is supported");
            CheckLayer(tx, block.LayerId);
            var definition = (BlockTableRecord)tx.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            Require(!definition.IsLayout && !definition.IsDependent && !definition.IsFromExternalReference && !definition.IsFromOverlayReference,
                "xref, dependent or layout block definitions must not be exploded");
            Require(!definition.IsDynamicBlock && definition.Annotative != AnnotativeStates.True && definition.Explodable, "definition is dynamic/annotative or does not allow explode");
            int count = 0;
            foreach (ObjectId childId in definition)
            {
                Time(watch);
                Require(++count <= remaining, "aggregate direct definition entity count exceeds max_entities");
                var child = tx.GetObject(childId, OpenMode.ForRead) as Entity;
                Require(child != null && !child.IsErased, "definition child is not a live Entity");
                Require(!(child is AttributeDefinition), "attribute definitions are unsupported");
                CheckLayer(tx, child.LayerId);
                Increment(types, child.GetType().Name);
            }
            Require(count > 0, "empty block definitions are not supported");
            return new JObject { ["handle"] = block.Handle.ToString(), ["owner_handle"] = block.OwnerId.Handle.ToString(),
                ["definition_handle"] = definition.Handle.ToString(), ["definition_name"] = definition.Name,
                ["layer"] = block.Layer, ["position"] = Point(block.Position), ["normal"] = Vector(block.Normal),
                ["scale"] = new JObject { ["x"] = scale.X, ["y"] = scale.Y, ["z"] = scale.Z },
                ["rotation_radians"] = block.Rotation, ["attribute_count"] = 0, ["definition_entity_count"] = count };
        }

        // Shared by the guarded operation and the legacy one-reference handler.
        // Cleanup ownership transfers only after both native append and transaction registration succeed.
        internal static string[] AppendExploded(Transaction tx, BlockReference blockReference, int maxEntities = int.MaxValue,
            Action checkBudget = null, Action<Entity> validateOutput = null)
        {
            using (var exploded = new DBObjectCollection())
            {
                var resident = new HashSet<DBObject>();
                try
                {
                    blockReference.Explode(exploded);
                    checkBudget?.Invoke();
                    Require(exploded.Count <= maxEntities, "native exploded output exceeds max_entities");
                    if (validateOutput != null) Require(exploded.Count > 0, "native exploded output is empty");
                    foreach (DBObject value in exploded)
                    {
                        checkBudget?.Invoke();
                        Require(value is Entity, "native exploded object is not an Entity");
                        validateOutput?.Invoke((Entity)value);
                    }
                    Require(!blockReference.OwnerId.IsNull, "block reference owner is unavailable");
                    var owner = tx.GetObject(blockReference.OwnerId, OpenMode.ForWrite) as BlockTableRecord;
                    Require(owner != null, "block reference owner is not a block table record");
                    var handles = new List<string>();
                    foreach (DBObject value in exploded)
                    {
                        checkBudget?.Invoke();
                        var entity = (Entity)value;
                        owner.AppendEntity(entity);
                        tx.AddNewlyCreatedDBObject(entity, true);
                        resident.Add(entity);
                        Require(entity.OwnerId == owner.ObjectId && !entity.ObjectId.IsNull && !entity.IsErased,
                            "exploded entity did not retain its source owner");
                        handles.Add(entity.Handle.ToString());
                    }
                    return handles.ToArray();
                }
                finally
                {
                    foreach (DBObject value in exploded) if (!resident.Contains(value)) value.Dispose();
                }
            }
        }

        private static void ValidateOutput(Transaction tx, Database db, Entity value, Dictionary<string, int> types)
        {
            Require(value.ObjectId.IsNull, "native explode unexpectedly returned a database-resident entity");
            CheckLayer(tx, value.LayerId);
            Require(!(value is AttributeDefinition) && !(value is AttributeReference), "native explode emitted an unsupported attribute");
            Increment(types, value.GetType().Name);
        }
        private static void CheckLayer(Transaction tx, ObjectId id)
        {
            var layer = (LayerTableRecord)tx.GetObject(id, OpenMode.ForRead);
            Require(!layer.IsLocked && !layer.IsDependent && !layer.IsErased, "locked, dependent or erased layer is unsupported: " + layer.Name);
        }
        private static void CheckRevision(DrawingRevision revision, BlockExplodeInput input)
            => Require(revision.SessionId == input.ExpectedSession && revision.Revision == input.ExpectedRevision,
                "drawing session/revision changed; obtain a new block explode plan");
        private static bool SameCounts(Dictionary<string, int> a, Dictionary<string, int> b)
        { if (a.Count != b.Count) return false; foreach (var item in a) if (!b.TryGetValue(item.Key, out var n) || n != item.Value) return false; return true; }
        private static void Increment(Dictionary<string, int> values, string key)
        { values.TryGetValue(key, out var n); values[key] = n + 1; }
        private static JObject Point(Point3d p) => new JObject { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
        private static JObject Vector(Vector3d p) => new JObject { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
        private static void Time(Stopwatch watch) => Require(watch.ElapsedMilliseconds <= BlockExplodeInput.BudgetMilliseconds, "block explode cooperative deadline exceeded; native calls cannot be interrupted");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Require(string error) { if (error != null) throw new InvalidOperationException(error); }
    }
}
