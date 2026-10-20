using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Proxy;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class InventoryProxiesHandler : IAcadCommand
    {
        public string Name => "inventory_proxies";
        public string Description => "Read bounded full-database proxy metadata, ownership and native erase flags.";
        public CommandSchema Schema => CommandSchemas.InventoryProxies;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!ProxyInventoryInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            if (input.ExpectedDocument != null)
            {
                error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                if (error != null) return CommandResult.Fail(error);
            }
            try { return CommandResult.Success(ProxyInventory.Read(doc, input)); }
            catch (Exception ex) { return CommandResult.Fail("failed to inventory proxies: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class EraseProxyObjectsHandler : IAcadCommand
    {
        public string Name => "erase_proxy_objects";
        public string Description => "Erase only explicit nongraphical proxy objects after guarded preflight in one transaction.";
        public CommandSchema Schema => CommandSchemas.EraseProxyObjects;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!ProxyEraseInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            if (doc.IsReadOnly) return CommandResult.Fail("the active drawing is read-only");
            try
            {
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var targets = new List<ProxyObject>();
                    var handles = new List<string>();
                    foreach (var expected in input.Targets)
                    {
                        if (!db.TryGetObjectId(new Handle(expected.HandleValue), out var id) || id.IsNull)
                            return CommandResult.Fail("handle " + expected.Handle + ": object not found");
                        if (id.IsErased || id.IsEffectivelyErased)
                            return CommandResult.Fail("handle " + expected.Handle + ": object or owner is erased or unavailable");
                        var proxy = tx.GetObject(id, OpenMode.ForRead) as ProxyObject;
                        if (proxy == null) return CommandResult.Fail("handle " + expected.Handle + ": object is not a nongraphical ProxyObject");
                        if (proxy.OwnerId.IsNull || !expected.Matches(proxy.OriginalClassName, proxy.OriginalDxfName,
                            proxy.ApplicationDescription, proxy.OwnerId.Handle.Value))
                            return CommandResult.Fail("handle " + expected.Handle + ": expected proxy class, application or owner changed");
                        if ((proxy.ProxyFlags & 1) == 0)
                            return CommandResult.Fail("handle " + expected.Handle + ": native proxy erase permission is absent");
                        var references = proxy.GetReferences();
                        // Conservative first scope: the managed reference fields are not relied on.
                        // Reject all custom-data references instead of guessing pointer/ownership types.
                        if (references == null || references.Count != 0)
                            return CommandResult.Fail("handle " + expected.Handle + ": custom-data references are outside the supported erase scope");
                        if (!proxy.ExtensionDictionary.IsNull)
                        {
                            var dictionary = tx.GetObject(proxy.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                            if (dictionary == null || dictionary.Count != 0)
                                return CommandResult.Fail("handle " + expected.Handle + ": nonempty extension dictionary is outside the supported erase scope");
                        }
                        var owners = ProxyOwners.Read(tx, proxy, 32, null, 0);
                        if (!owners.Complete || owners.Erased || owners.Xref || owners.LockedLayer)
                            return CommandResult.Fail("handle " + expected.Handle + ": owner chain is unavailable, erased, xref-dependent or on a locked layer");
                        targets.Add(proxy);
                        handles.Add(proxy.Handle.ToString());
                    }
                    if (input.DryRun) return CommandResult.Success(new { dry_run = true, committed = false,
                        validated_handles = handles.ToArray(), count = handles.Count, atomic = true,
                        erase_scope = "reference-free nongraphical ProxyObject; no dictionary or native roots" });
                    // Every target and owner is checked before any write access or erase operation.
                    foreach (var target in targets)
                    {
                        target.UpgradeOpen();
                        if (!target.IsWriteEnabled) return CommandResult.Fail("proxy object cannot be opened for write");
                    }
                    foreach (var target in targets) target.Erase();
                    foreach (var target in targets)
                        if (!target.ObjectId.IsErased) throw new InvalidOperationException("native erase was not confirmed for " + target.Handle);
                    tx.Commit();
                    return CommandResult.Success(new { dry_run = false, committed = true, erased_handles = handles.ToArray(),
                        count = handles.Count, atomic = true, implicit_roots = false,
                        scope_note = "Only explicit proxy objects were erased. Native reactors may affect associated database state; no global cleanup or save was performed." });
                }
            }
            catch (Exception ex)
            {
                // Disposal aborts an uncommitted transaction; an error never commits a partial batch.
                return CommandResult.Fail("failed to erase proxy objects: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }
}
