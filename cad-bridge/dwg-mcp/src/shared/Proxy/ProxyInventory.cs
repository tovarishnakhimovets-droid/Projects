using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Proxy
{
    internal static class ProxyInventory
    {
        internal static JObject Read(Document doc, ProxyInventoryInput input)
        {
            var identity = DocumentGuard.Capture(doc);
            var db = doc.Database;
            var seed = db.Handseed.Value;
            if (seed <= 0) throw new InvalidOperationException("database handseed is outside the supported positive handle range");
            if (input.AfterHandle >= seed) throw new InvalidOperationException("after_handle is beyond the current database handseed");
            var watch = Stopwatch.StartNew();
            var items = new JArray();
            var errors = new JArray();
            long last = input.AfterHandle;
            var attempts = 0;
            var resolved = 0;
            var erased = 0;
            var errorCount = 0;
            var itemBytes = 0;
            string stopReason = null;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                for (var number = input.AfterHandle + 1; number < seed; number++)
                {
                    if (attempts >= input.MaxHandles) { stopReason = "handle_budget"; break; }
                    if (watch.ElapsedMilliseconds >= input.BudgetMilliseconds) { stopReason = "time_budget"; break; }
                    if (items.Count >= input.MaxResults) { stopReason = "result_budget"; break; }
                    attempts++;
                    try
                    {
                        if (!db.TryGetObjectId(new Handle(number), out var id) || id.IsNull) { last = number; continue; }
                        resolved++;
                        if (id.IsErased) { erased++; last = number; continue; }
                        var rx = id.ObjectClass;
                        if (!IsProxyClass(rx.Name, rx.DxfName)) { last = number; continue; }
                        var obj = tx.GetObject(id, OpenMode.ForRead, true);
                        var item = Describe(tx, obj, input.OwnerDepth, watch, input.BudgetMilliseconds);
                        var bytes = Encoding.UTF8.GetByteCount(item.ToString(Formatting.None));
                        // Leave room for bounded errors, identity, counters and the command envelope.
                        if (itemBytes + bytes > ProxyContract.MaxResponseBytes - 128 * 1024)
                        { stopReason = "response_budget"; break; }
                        items.Add(item);
                        itemBytes += bytes;
                        if (item["metadata_complete"].Value<bool>() == false)
                        {
                            errorCount++;
                            AddError(errors, number, "proxy metadata or owner inspection is incomplete");
                        }
                        last = number;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        AddError(errors, number, ErrorSanitizer.Sanitize(ex.Message));
                        last = number;
                    }
                }
                // This transaction is read-only and is closed without committing any changes.
            }
            var handseedChanged = db.Handseed.Value != seed;
            var rangeComplete = last >= seed - 1 && stopReason == null && errorCount == 0 && !handseedChanged;
            return new JObject
            {
                ["document"] = identity.DocumentPath ?? identity.DocumentName,
                ["document_path"] = identity.DocumentPath,
                ["document_name"] = identity.DocumentName,
                ["db_filename"] = identity.DatabaseFilename,
                ["fingerprint"] = identity.Fingerprint,
                ["scope"] = "full_database",
                ["complete"] = input.AfterHandle == 0 && rangeComplete,
                ["range_complete"] = rangeComplete,
                ["start_after_handle"] = input.AfterHandle == 0 ? JValue.CreateNull() : new JValue(input.AfterHandle.ToString("X")),
                ["last_handle_scanned"] = last.ToString("X"),
                ["next_after_handle"] = last < seed - 1 && last > 0 ? new JValue(last.ToString("X")) : JValue.CreateNull(),
                ["handseed"] = seed.ToString("X"),
                ["handseed_changed"] = handseedChanged,
                ["stop_reason"] = stopReason,
                ["handle_attempts"] = attempts,
                ["resolved_count"] = resolved,
                ["directly_erased_skipped"] = erased,
                ["proxy_count"] = items.Count,
                ["elapsed_ms"] = watch.ElapsedMilliseconds,
                ["max_handles"] = input.MaxHandles,
                ["budget_ms"] = input.BudgetMilliseconds,
                ["max_results"] = input.MaxResults,
                ["owner_depth"] = input.OwnerDepth,
                ["response_limit_bytes"] = ProxyContract.MaxResponseBytes,
                ["proxies"] = items,
                ["error_count"] = errorCount,
                ["errors"] = errors,
                ["errors_truncated"] = errorCount > errors.Count,
                ["classification_basis"] = "case-insensitive metadata text heuristic; not erase permission",
                ["continuation_note"] = "Pages are separate reads. Match document/fingerprint/handseed across pages; only a complete first-page scan proves full database coverage."
            };
        }

        private static JObject Describe(Transaction tx, DBObject obj, int depth, Stopwatch watch, int budget)
        {
            var item = new JObject
            {
                ["handle"] = obj.Handle.ToString(),
                ["managed_type"] = obj.GetType().FullName,
                ["rx_class"] = obj.ObjectId.ObjectClass.Name,
                ["rx_dxf"] = obj.ObjectId.ObjectClass.DxfName,
                ["rx_application"] = obj.ObjectId.ObjectClass.AppName,
                ["owner_handle"] = HandleOf(obj.OwnerId),
                ["extension_dictionary"] = HandleOf(obj.ExtensionDictionary),
                ["is_entity"] = obj is Entity,
                ["is_effectively_erased"] = obj.ObjectId.IsEffectivelyErased,
                ["layer"] = obj is Entity entity ? entity.Layer : null
            };
            string className = null, dxfName = null, application = null;
            int? flags = null, referenceCount = null, extensionCount = null;
            string metadataError = null;
            if (obj is ProxyObject proxy)
            {
                className = proxy.OriginalClassName;
                dxfName = proxy.OriginalDxfName;
                application = proxy.ApplicationDescription;
                flags = proxy.ProxyFlags;
                var references = proxy.GetReferences();
                if (references == null) metadataError = "proxy references are unavailable";
                else referenceCount = references.Count;
            }
            else if (obj is ProxyEntity proxyEntity)
            {
                className = proxyEntity.OriginalClassName;
                dxfName = proxyEntity.OriginalDxfName;
                application = proxyEntity.ApplicationDescription;
                flags = proxyEntity.ProxyFlags;
                item["graphics_metafile_type"] = proxyEntity.GraphicsMetafileType.ToString();
                var references = proxyEntity.GetReferences();
                if (references == null) metadataError = "proxy references are unavailable";
                else referenceCount = references.Count;
            }
            else metadataError = "proxy RX class lacks a managed ProxyObject/ProxyEntity wrapper";
            if (metadataError == null && (string.IsNullOrWhiteSpace(className) || dxfName == null || application == null))
                metadataError = "original proxy class, DXF name or application is unavailable";
            if (obj.ExtensionDictionary.IsNull) extensionCount = 0;
            else extensionCount = ((DBDictionary)tx.GetObject(obj.ExtensionDictionary, OpenMode.ForRead)).Count;
            var owners = ProxyOwners.Read(tx, obj, depth, watch, budget);
            item["kind"] = obj is ProxyObject ? "proxy_object" : obj is ProxyEntity ? "proxy_entity" : "unsupported_proxy";
            item["original_class"] = className;
            item["original_dxf"] = dxfName;
            item["application"] = application;
            item["proxy_flags"] = flags.HasValue ? new JValue(flags.Value) : JValue.CreateNull();
            item["erase_allowed"] = flags.HasValue ? new JValue((flags.Value & 1) != 0) : JValue.CreateNull();
            item["classification"] = ProxyContract.Classify(className, dxfName, application);
            item["reference_count"] = referenceCount.HasValue ? new JValue(referenceCount.Value) : JValue.CreateNull();
            item["extension_dictionary_count"] = extensionCount.HasValue ? new JValue(extensionCount.Value) : JValue.CreateNull();
            item["owner_chain"] = owners.Chain;
            item["owners_complete"] = owners.Complete;
            item["owner_error"] = owners.Error;
            item["owner_erased"] = owners.Erased;
            item["owner_is_xref"] = owners.Xref;
            item["owner_layer_locked"] = owners.LockedLayer;
            item["metadata_error"] = metadataError;
            var truncated = TrimStrings(item);
            item["metadata_truncated"] = truncated;
            item["metadata_complete"] = metadataError == null && owners.Complete && !truncated;
            item["erase_scope_supported"] = obj is ProxyObject && flags.HasValue && (flags.Value & 1) != 0 &&
                referenceCount == 0 && extensionCount == 0 && owners.Complete && !owners.Erased && !owners.Xref &&
                !owners.LockedLayer && !obj.ObjectId.IsEffectivelyErased && !truncated && metadataError == null;
            return item;
        }

        private static bool IsProxyClass(string name, string dxf)
            => string.Equals(name, "AcDbProxyObject", StringComparison.Ordinal) ||
                string.Equals(name, "AcDbProxyEntity", StringComparison.Ordinal) ||
                string.Equals(dxf, "ACAD_PROXY_OBJECT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dxf, "ACAD_PROXY_ENTITY", StringComparison.OrdinalIgnoreCase);

        internal static string HandleOf(ObjectId id) => id.IsNull ? null : id.Handle.ToString();

        private static void AddError(JArray errors, long number, string message)
        {
            if (errors.Count < 50) errors.Add(new JObject { ["handle"] = number.ToString("X"),
                ["error"] = message != null && message.Length > 1024 ? message.Substring(0, 1024) : message });
        }

        private static bool TrimStrings(JToken token)
        {
            var changed = false;
            if (token is JValue value && value.Type == JTokenType.String && value.Value<string>().Length > ProxyContract.MaxMetadataCharacters)
            { value.Value = value.Value<string>().Substring(0, ProxyContract.MaxMetadataCharacters); return true; }
            if (token is JContainer container)
                foreach (var child in container.Children()) changed |= TrimStrings(child);
            return changed;
        }
    }

    internal sealed class ProxyOwnerInspection
    {
        internal JArray Chain = new JArray();
        internal bool Complete;
        internal bool Erased;
        internal bool Xref;
        internal bool LockedLayer;
        internal string Error;
    }

    internal static class ProxyOwners
    {
        internal static ProxyOwnerInspection Read(Transaction tx, DBObject target, int depth, Stopwatch watch, int budget)
        {
            var result = new ProxyOwnerInspection();
            var visited = new HashSet<ObjectId> { target.ObjectId };
            var current = target;
            try
            {
                for (var level = 0; level < depth; level++)
                {
                    if (watch != null && watch.ElapsedMilliseconds >= budget)
                    { result.Error = "owner time budget exceeded"; return result; }
                    var id = current.OwnerId;
                    if (id.IsNull)
                    {
                        result.Complete = current is SymbolTable || current.ObjectId == target.Database.NamedObjectsDictionaryId;
                        if (!result.Complete) result.Error = "owner chain ends before a database root";
                        return result;
                    }
                    if (!visited.Add(id)) { result.Error = "cyclic owner chain"; return result; }
                    var owner = tx.GetObject(id, OpenMode.ForRead, true);
                    var node = new JObject { ["handle"] = owner.Handle.ToString(), ["managed_type"] = owner.GetType().FullName,
                        ["rx_class"] = owner.ObjectId.ObjectClass.Name, ["is_erased"] = id.IsErased };
                    result.Erased |= id.IsErased;
                    if (owner is DBDictionary dictionary) node["dictionary_key"] = dictionary.NameAt(current.ObjectId);
                    if (owner is BlockTableRecord block)
                    {
                        node["block_name"] = block.Name;
                        node["is_xref"] = block.IsFromExternalReference || block.IsDependent;
                        result.Xref |= block.IsFromExternalReference || block.IsDependent;
                    }
                    if (owner is Entity entity)
                    {
                        var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
                        node["layer"] = entity.Layer;
                        node["layer_locked"] = layer.IsLocked;
                        result.LockedLayer |= layer.IsLocked;
                        result.Xref |= layer.IsDependent;
                    }
                    result.Chain.Add(node);
                    current = owner;
                    if (current is SymbolTable || current.ObjectId == target.Database.NamedObjectsDictionaryId)
                    { result.Complete = true; return result; }
                }
                result.Error = "owner depth budget exceeded";
            }
            catch (Exception ex) { result.Error = ErrorSanitizer.Sanitize(ex.Message); }
            return result;
        }
    }
}
