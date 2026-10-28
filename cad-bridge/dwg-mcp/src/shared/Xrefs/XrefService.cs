using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices.Filters;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.EntityBatch;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Xrefs
{
    internal static class XrefService
    {
        internal static JObject List(Document doc, XrefListInput input)
            => Read(doc, input.MaxXrefs, input.MaxEntities);

        private static JObject Read(Document doc, int maxXrefs, int maxEntities)
        {
            var state = new ReadState(maxEntities);
            var identity = DocumentGuard.Capture(doc);
            var revision = DrawingRevision.For(doc);
            var startingRevision = revision.Revision;
            var rows = new JArray();
            int total = 0;
            bool graphComplete = true;
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            using (var graph = doc.Database.GetHostDwgXrefGraph(true))
            {
                total = Math.Max(0, graph.NumNodes - 1);
                for (int i = 1; i < graph.NumNodes; i++)
                {
                    if (rows.Count >= maxXrefs || !state.Continue())
                    { graphComplete = false; state.Stop(rows.Count >= maxXrefs ? "xref_limit" : "time_budget"); break; }
                    try
                    {
                        var node = graph.GetXrefNode(i);
                        var id = node.BlockTableRecordId;
                        if (id.IsNull || id.IsErased || id.IsEffectivelyErased)
                            throw new InvalidOperationException("xref graph node has no available host block-table record");
                        var record = tx.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                        if (record == null || !record.IsFromExternalReference)
                            throw new InvalidOperationException("xref graph node is not an external block definition");
                        var parents = new JArray();
                        for (int n = 0; n < node.NumIn; n++)
                        {
                            var parent = node.In(n) as XrefGraphNode;
                            if (parent != null) parents.Add(state.Text(parent.Name));
                        }
                        var row = new JObject
                        {
                            ["handle"] = Handle(id), ["name"] = state.Text(record.Name),
                            ["path"] = state.Text(record.PathName ?? ""), ["status"] = node.XrefStatus.ToString(),
                            ["is_overlay"] = record.IsFromOverlayReference, ["is_nested"] = node.IsNested,
                            ["is_unloaded"] = record.IsUnloaded,
                            ["parents"] = new JArray(parents.Values<string>().OrderBy(s => s, StringComparer.Ordinal))
                        };
                        bool referenceComplete;
                        row["inserts"] = ReadInserts(tx, record, state, out referenceComplete);
                        row["references_complete"] = referenceComplete;
                        row["reference_count_returned"] = ((JArray)row["inserts"]).Count;
                        row["direct_entities"] = ReadCounts(tx, record, state);
                        if (!state.Reserve(row))
                        { graphComplete = false; state.Stop("response_budget"); break; }
                        rows.Add(row);
                    }
                    catch (Exception ex)
                    {
                        graphComplete = false;
                        state.Error("xref node " + i.ToString(CultureInfo.InvariantCulture) + ": " + ErrorSanitizer.Sanitize(ex.Message));
                    }
                }
            }
            var sorted = new JArray(rows.Cast<JObject>().OrderBy(r => r.Value<string>("handle"), StringComparer.Ordinal)
                .Select(r => (JToken)r));
            bool unchanged = startingRevision == revision.Revision;
            bool metadataComplete = graphComplete && state.MetadataComplete && state.StopReason == null && unchanged && rows.Count == total;
            var graphMetadata = new JArray(sorted.Cast<JObject>().Select(row => new JObject
            {
                ["handle"] = row["handle"].DeepClone(), ["name"] = row["name"].DeepClone(), ["path"] = row["path"].DeepClone(),
                ["status"] = row["status"].DeepClone(), ["is_overlay"] = row["is_overlay"].DeepClone(),
                ["is_nested"] = row["is_nested"].DeepClone(), ["is_unloaded"] = row["is_unloaded"].DeepClone(),
                ["parents"] = row["parents"].DeepClone(), ["references_fingerprint"] = XrefContract.Hash(row["inserts"])
            }));
            var graphHash = XrefContract.Hash(graphMetadata);
            foreach (JObject row in sorted)
            {
                row["expected_xref"] = new JObject
                {
                    ["handle"] = row["handle"].DeepClone(), ["name"] = row["name"].DeepClone(), ["path"] = row["path"].DeepClone(),
                    ["status"] = row["status"].DeepClone(), ["is_overlay"] = row["is_overlay"].DeepClone(), ["is_nested"] = row["is_nested"].DeepClone(),
                    ["graph_fingerprint"] = graphHash, ["references_fingerprint"] = XrefContract.Hash(row["inserts"]),
                    ["document_session"] = revision.SessionId, ["document_revision"] = startingRevision,
                    ["guard_complete"] = metadataComplete && row.Value<bool>("references_complete")
                };
            }
            var result = new JObject
            {
                ["document_path"] = identity.DocumentPath, ["document_name"] = identity.DocumentName, ["fingerprint"] = identity.Fingerprint,
                ["scope"] = "host xref graph including nested/ghost nodes; direct inserts and bounded direct definition histograms",
                ["xrefs"] = sorted, ["xref_count"] = total, ["returned_count"] = sorted.Count,
                ["metadata_complete"] = metadataComplete, ["counts_requested"] = maxEntities != 0,
                ["counts_complete"] = maxEntities != 0 && state.CountsComplete,
                ["complete"] = metadataComplete && (maxEntities == 0 || state.CountsComplete),
                ["document_unchanged_during_read"] = unchanged,
                ["revision_diagnostics"] = revision.DiagnosticsSince(Math.Max(0, startingRevision - 16)),
                ["errors"] = state.Errors,
                ["stop_reason"] = state.StopReason, ["max_xrefs"] = maxXrefs, ["max_entities"] = maxEntities,
                ["references_scanned"] = state.ReferenceCount, ["entities_scanned"] = state.EntityCount,
                ["budget_ms"] = XrefContract.BudgetMilliseconds, ["elapsed_ms"] = state.Watch.ElapsedMilliseconds,
                ["response_limit_bytes"] = XrefContract.MaxResponseBytes,
                ["notes"] = new JArray("Direct counts do not recurse into ordinary block definitions; zero direct HATCH is not evidence that the reference contains no surfaces.",
                    "No external file is opened, reloaded, edited or saved. Limits are cooperative between native calls.",
                    "Copy expected_xref for one root to dwg_manage_xrefs. Any persistent document edit invalidates all returned guards; reread after each mutation.")
            };
            if (Bytes(result) > XrefContract.MaxResponseBytes)
            {
                result["xrefs"] = new JArray(); result["returned_count"] = 0; result["complete"] = false;
                result["metadata_complete"] = false; result["counts_complete"] = false; result["stop_reason"] = "response_budget";
            }
            return result;
        }

        private static JArray ReadInserts(Transaction tx, BlockTableRecord record, ReadState state, out bool complete)
        {
            complete = true;
            var inserts = new List<JObject>();
            var ids = record.GetBlockReferenceIds(true, false);
            foreach (ObjectId id in ids)
            {
                if (!state.Continue() || state.ReferenceCount >= XrefContract.MaxReferences)
                { complete = false; state.MetadataComplete = false; state.Stop(state.Continue() ? "reference_limit" : "time_budget"); break; }
                state.ReferenceCount++;
                try
                {
                    if (id.IsErased || id.IsEffectivelyErased) continue;
                    var block = tx.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (block == null) throw new InvalidOperationException("xref insert is not a block reference");
                    var owner = tx.GetObject(block.OwnerId, OpenMode.ForRead) as BlockTableRecord;
                    var layer = tx.GetObject(block.LayerId, OpenMode.ForRead) as LayerTableRecord;
                    if (owner == null || layer == null) throw new InvalidOperationException("insert owner or layer unavailable");
                    var insert = new JObject
                    {
                        ["handle"] = Handle(id), ["definition_handle"] = Handle(block.BlockTableRecord),
                        ["owner_handle"] = Handle(block.OwnerId), ["owner_name"] = state.Text(owner.Name),
                        ["owner_is_layout"] = owner.IsLayout, ["owner_is_external"] = owner.IsFromExternalReference || owner.IsDependent,
                        ["layer"] = state.Text(block.Layer), ["layer_locked"] = layer.IsLocked,
                        ["layer_off"] = layer.IsOff, ["layer_frozen"] = layer.IsFrozen,
                        ["block_transform"] = Matrix(block.BlockTransform), ["clip"] = ReadClip(tx, block, state)
                    };
                    state.MetadataBytes += Bytes(insert);
                    if (state.MetadataBytes > XrefContract.MaxResponseBytes / 2)
                    { complete = false; state.MetadataComplete = false; state.Stop("reference_response_budget"); break; }
                    inserts.Add(insert);
                }
                catch (Exception ex)
                {
                    complete = false; state.MetadataComplete = false;
                    state.Error("insert " + Handle(id) + ": " + ErrorSanitizer.Sanitize(ex.Message));
                }
            }
            return new JArray(inserts.OrderBy(r => r.Value<string>("handle"), StringComparer.Ordinal));
        }

        private static JObject ReadCounts(Transaction tx, BlockTableRecord record, ReadState state)
        {
            var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var layers = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int count = 0;
            bool complete = state.MaxEntities != 0;
            if (state.MaxEntities != 0)
            {
                foreach (ObjectId id in record)
                {
                    if (state.EntityCount >= state.MaxEntities || !state.Continue())
                    { complete = false; break; }
                    state.EntityCount++;
                    try
                    {
                        var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity == null || entity.IsErased) continue;
                        Increment(types, state.Text(entity.GetRXClass().DxfName ?? entity.GetType().Name));
                        Increment(layers, state.Text(entity.Layer)); count++;
                    }
                    catch (Exception ex)
                    { complete = false; state.Error("direct entity " + Handle(id) + ": " + ErrorSanitizer.Sanitize(ex.Message)); }
                }
            }
            if (!complete) state.CountsComplete = false;
            return new JObject { ["requested"] = state.MaxEntities != 0, ["complete"] = complete,
                ["count_returned"] = count, ["type_counts"] = JObject.FromObject(types), ["layer_counts"] = JObject.FromObject(layers),
                ["scope"] = "direct entities only; ordinary nested block definitions are not scanned" };
        }

        private static JObject ReadClip(Transaction tx, BlockReference block, ReadState state)
        {
            if (block.ExtensionDictionary.IsNull) return new JObject { ["present"] = false };
            var extension = tx.GetObject(block.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (extension == null) throw new InvalidOperationException("extension dictionary unavailable");
            if (!extension.Contains("ACAD_FILTER")) return new JObject { ["present"] = false };
            var filters = tx.GetObject(extension.GetAt("ACAD_FILTER"), OpenMode.ForRead) as DBDictionary;
            if (filters == null) throw new InvalidOperationException("ACAD_FILTER dictionary unavailable");
            if (!filters.Contains("SPATIAL")) return new JObject { ["present"] = false };
            var filter = tx.GetObject(filters.GetAt("SPATIAL"), OpenMode.ForRead) as SpatialFilter;
            if (filter == null) throw new InvalidOperationException("SPATIAL entry is not a native spatial filter");
            var definition = filter.Definition;
            var points = definition.GetPoints();
            if (points.Count > 4096) throw new InvalidOperationException("spatial filter exceeds 4096 boundary points");
            var boundary = new JArray();
            foreach (Point2d point in points)
            { if (!state.Continue()) throw new InvalidOperationException("time budget reached reading spatial filter"); boundary.Add(new JArray(Number(point.X), Number(point.Y))); }
            return new JObject { ["present"] = true, ["handle"] = Handle(filter.ObjectId), ["enabled"] = definition.Enabled,
                ["inverted"] = filter.Inverted, ["has_perspective_camera"] = filter.HasPerspectiveCamera,
                ["points"] = boundary, ["normal"] = new JArray(Number(definition.Normal.X), Number(definition.Normal.Y), Number(definition.Normal.Z)),
                ["elevation"] = Number(definition.Elevation), ["front_clip"] = Number(definition.FrontClip), ["back_clip"] = Number(definition.BackClip),
                ["clip_to_world"] = Matrix(filter.ClipSpaceToWorldCoordinateSystemTransform),
                ["original_inverse_block_transform"] = Matrix(filter.OriginalInverseBlockTransform) };
        }

        internal static CommandResult Manage(Document doc, XrefManageInput input)
        {
            var error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            if (doc.IsReadOnly) return CommandResult.Fail("the active drawing is read-only");
            if (!doc.Editor.IsQuiescent) return CommandResult.Fail("finish the active AutoCAD command before managing xrefs");
            error = EntityBatchInput.ValidateUndoState(doc.Database.UndoRecording, Convert.ToInt32(Application.GetSystemVariable("UNDOCTL")));
            if (error != null) return CommandResult.Fail(error);
            var before = Read(doc, XrefContract.MaxXrefs, 0);
            if (!before.Value<bool>("metadata_complete")) return CommandResult.Fail("xref preflight inventory is incomplete (including any ghost/unavailable host records); inspect dwg_list_xrefs errors; no mutation was attempted");
            var row = ((JArray)before["xrefs"]).Cast<JObject>().SingleOrDefault(r => r.Value<string>("handle") == input.Handle);
            if (row == null || !XrefManageInput.Matches(input.ExpectedXref, row["expected_xref"] as JObject))
            {
                var actual = row?["expected_xref"] as JObject;
                var changed = new JArray();
                foreach (var field in input.ExpectedXref.Properties())
                    if (!JToken.DeepEquals(field.Value, actual?[field.Name])) changed.Add(field.Name);
                var details = new JObject { ["changed_fields"] = changed,
                    ["expected_revision"] = input.ExpectedXref["document_revision"],
                    ["actual_revision"] = actual?["document_revision"],
                    ["revision_diagnostics"] = before["revision_diagnostics"] };
                return CommandResult.Fail("expected xref guard changed; reread dwg_list_xrefs. " + details.ToString(Formatting.None));
            }
            if (row.Value<bool>("is_nested")) return CommandResult.Fail("nested xrefs cannot be managed directly; select one root definition");
            var inserts = (JArray)row["inserts"];
            foreach (JObject insert in inserts)
                if (insert.Value<bool>("layer_locked") || insert.Value<bool>("owner_is_external") || !insert.Value<bool>("owner_is_layout"))
                    return CommandResult.Fail("xref insert " + insert.Value<string>("handle") + " is locked or not directly owned by a local model/paper layout");
            if (input.Action == "bind" && (row.Value<string>("status") != XrefStatus.Resolved.ToString() || row.Value<bool>("is_unloaded")))
                return CommandResult.Fail("bind requires a resolved, loaded root xref; the tool does not reload references");
            if (input.Action == "bind")
            {
                error = XrefContract.ValidateBindGraph(before["xrefs"] as JArray);
                if (error != null) return CommandResult.Fail(error + "; no mutation was attempted");
            }
            if (!CadHandleResolver.TryResolve(doc.Database, input.Handle, out var targetId, out error)) return CommandResult.Fail(error);
            var plan = new JObject { ["action"] = input.Action, ["target_handle"] = input.Handle,
                ["xref_name"] = row["name"].DeepClone(), ["affected_insert_count"] = inserts.Count,
                ["bind_mode"] = input.Action == "bind" ? "preserve_names" : null,
                ["dry_run"] = input.DryRun, ["mutation_attempted"] = false, ["completed"] = false,
                ["atomic"] = false, ["single_undo_verified"] = false, ["rollback_verified"] = false,
                ["note"] = "One root definition per call. Native xref operations can affect dependent symbols; no cross-operation atomicity or save is promised." };
            if (input.DryRun)
            { plan["operation_status"] = "validated"; plan["expected_xref"] = input.ExpectedXref.DeepClone(); return CommandResult.Success(plan); }
            var revision = DrawingRevision.For(doc);
            if (revision.SessionId != input.ExpectedXref.Value<string>("document_session") || revision.Revision != input.ExpectedXref.Value<long>("document_revision"))
                return CommandResult.Fail("document changed after xref preflight; no mutation was attempted");
            if (!doc.Editor.IsQuiescent) return CommandResult.Fail("an AutoCAD command became active after xref preflight; no mutation was attempted");
            plan["mutation_attempted"] = true;
            bool nativeReturned = false;
            try
            {
                // Close read-opened objects before invoking the whole-database native API.
                // The native implementation owns internal cloning/dependency work. Do not
                // claim a surrounding transaction makes that work completely atomic.
                using (var tx = doc.Database.TransactionManager.StartTransaction())
                {
                    if (input.Action == "bind")
                    {
                        var ids = new ObjectIdCollection { targetId };
                        doc.Database.BindXrefs(ids, false); // preserve xref-qualified symbol identities, never insert-style merge
                    }
                    else doc.Database.DetachXref(targetId);
                    tx.Commit();
                }
                nativeReturned = true;
                var after = Readback(doc, input, inserts);
                plan["readback"] = after;
                if (!after.Value<bool>("postcondition_verified"))
                    throw new InvalidOperationException("native operation returned but its expected postcondition was not confirmed");
                plan["operation_status"] = "completed"; plan["completed"] = true;
                plan["native_returned"] = true; plan["execution_unknown"] = false;
                return CommandResult.Success(plan);
            }
            catch (Exception ex)
            {
                plan["operation_status"] = "execution_unknown"; plan["native_returned"] = nativeReturned;
                plan["execution_unknown"] = true; plan["error"] = ErrorSanitizer.Sanitize(ex.Message);
                try { plan["readback"] = Readback(doc, input, inserts); }
                catch (Exception readError) { plan["readback_error"] = ErrorSanitizer.Sanitize(readError.Message); }
                plan["next_step"] = "Read current xref inventory and inspect actual state before any further write; do not automatically repeat this mutation.";
                return CommandResult.Fail(XrefFailureDetails.Format(plan));
            }
        }

        private static JObject Readback(Document doc, XrefManageInput input, JArray oldInserts)
        {
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                if (input.Action == "detach")
                {
                    bool definitionGone = !CadHandleResolver.TryResolve(doc.Database, input.Handle, out var id, out _) || id.IsErased || id.IsEffectivelyErased;
                    var remaining = new JArray();
                    foreach (JObject old in oldInserts)
                        if (CadHandleResolver.TryResolve(doc.Database, old.Value<string>("handle"), out var insertId, out _) && !insertId.IsErased && !insertId.IsEffectivelyErased)
                            remaining.Add(old.Value<string>("handle"));
                    return new JObject { ["definition_removed"] = definitionGone, ["remaining_original_insert_handles"] = remaining,
                        ["postcondition_verified"] = definitionGone && remaining.Count == 0 };
                }
                var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var name = input.ExpectedXref.Value<string>("name");
                if (!table.Has(name)) return new JObject { ["postcondition_verified"] = false, ["reason"] = "bound local definition not found by original name" };
                var record = (BlockTableRecord)tx.GetObject(table[name], OpenMode.ForRead);
                var state = new ReadState(0);
                var actual = ReadInserts(tx, record, state, out var complete);
                var priorComparable = ComparableInserts(oldInserts);
                var actualComparable = ComparableInserts(actual);
                bool local = !record.IsFromExternalReference && !record.IsDependent;
                bool unchanged = JToken.DeepEquals(priorComparable, actualComparable);
                return new JObject { ["definition_handle"] = Handle(record.ObjectId), ["definition_name"] = record.Name,
                    ["is_local"] = local, ["insert_metadata_complete"] = complete,
                    ["insert_placement_and_clipping_unchanged"] = unchanged, ["insert_handles"] = new JArray(actual.Cast<JObject>().Select(r => r.Value<string>("handle"))),
                    ["postcondition_verified"] = local && complete && unchanged };
            }
        }

        private static JArray ComparableInserts(JArray inserts)
        {
            var result = new JArray();
            foreach (JObject insert in inserts)
            {
                var comparable = (JObject)insert.DeepClone();
                comparable.Remove("definition_handle");
                // Native bind can create a new spatial-filter object while retaining
                // its full boundary and transform; compare its geometry, not its ID.
                (comparable["clip"] as JObject)?.Remove("handle");
                result.Add(comparable);
            }
            return result;
        }

        private static string Handle(ObjectId id) => id.IsNull ? null : id.Handle.ToString();
        private static JArray Matrix(Matrix3d matrix) => new JArray(matrix.ToArray().Select(Number));
        private static JToken Number(double n) => double.IsNaN(n) || double.IsInfinity(n)
            ? new JValue(n.ToString("R", CultureInfo.InvariantCulture)) : new JValue(n);
        private static int Bytes(JToken value) => Encoding.UTF8.GetByteCount(value.ToString(Formatting.None));
        private static void Increment(IDictionary<string, int> counts, string key) => counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

        private sealed class ReadState
        {
            internal readonly Stopwatch Watch = Stopwatch.StartNew();
            internal readonly JArray Errors = new JArray();
            internal readonly int MaxEntities;
            internal int EntityCount, ReferenceCount, MetadataBytes, ReservedBytes;
            internal bool MetadataComplete = true, CountsComplete = true;
            internal string StopReason;
            internal ReadState(int maxEntities) { MaxEntities = maxEntities; }
            internal bool Continue() => Watch.ElapsedMilliseconds < XrefContract.BudgetMilliseconds;
            internal void Stop(string reason) { if (StopReason == null) StopReason = reason; }
            internal string Text(string value)
            {
                if ((value ?? "").Length > XrefContract.MaxText) throw new InvalidOperationException("metadata string exceeds supported length");
                return value ?? "";
            }
            internal void Error(string value) { if (Errors.Count < 128) Errors.Add(value); }
            internal bool Reserve(JToken value)
            {
                ReservedBytes += Bytes(value);
                return ReservedBytes < XrefContract.MaxResponseBytes - 512 * 1024;
            }
        }
    }
}
