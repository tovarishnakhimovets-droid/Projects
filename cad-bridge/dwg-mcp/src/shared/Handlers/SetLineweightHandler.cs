using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SetLineweightHandler : IAcadCommand
    {
        public string Name => "set_lineweight";
        public string Description => "Set lineweight on a bounded entity batch after complete preflight, in one transaction.";
        public CommandSchema Schema => CommandSchemas.SetLineweight;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj)) return CommandResult.Fail("params must be an object");
            var handles = obj["handles"] as JArray;
            if (handles == null || handles.Count < 1 || handles.Count > 1000)
                return CommandResult.Fail("handles must be an array containing 1 to 1000 strings");
            var modeToken = obj["mode"];
            if (modeToken != null && modeToken.Type != JTokenType.String)
                return CommandResult.Fail("mode must be a string");
            if (!LineweightSpec.TryParse(obj["lineweight_mm"], modeToken?.Value<string>(), out var weight, out var error))
                return CommandResult.Fail(error);

            var requested = new List<string>();
            var distinct = new HashSet<long>();
            foreach (var token in handles)
            {
                if (token.Type != JTokenType.String)
                    return CommandResult.Fail("handles entries must be hexadecimal strings");
                var handle = token.Value<string>();
                if (!CadWire.TryParseHandleValue(handle, out var handleValue, out error))
                    return CommandResult.Fail(error);
                // Reject aliases such as 1/01 as well as case-insensitive duplicates.
                if (!distinct.Add(handleValue)) return CommandResult.Fail("handles must not contain duplicates");
                requested.Add(handle);
            }

            try
            {
                if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var entities = new List<Entity>();
                    foreach (var handle in requested)
                    {
                        if (!CadHandleResolver.TryResolve(db, handle, out var id, out error))
                            return CommandResult.Fail($"handle {handle}: {error}");
                        if (id.IsErased) return CommandResult.Fail($"handle {handle}: entity is erased");
                        var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity == null) return CommandResult.Fail($"handle {handle}: object is not an entity");
                        if (!LineweightTargetPreflight.TryCheckEntity(tx, entity, out error))
                            return CommandResult.Fail($"handle {handle}: {error}");
                        entities.Add(entity);
                    }

                    // Prove all targets can be opened for write before the first setter.
                    foreach (var entity in entities)
                    {
                        entity.UpgradeOpen();
                        if (!entity.IsWriteEnabled) return CommandResult.Fail("entity is read-only");
                    }

                    var changed = new List<string>();
                    foreach (var entity in entities)
                    {
                        if ((int)entity.LineWeight == weight) continue;
                        entity.LineWeight = (LineWeight)weight;
                        changed.Add(entity.Handle.ToString());
                    }
                    tx.Commit();
                    return CommandResult.Success(new
                    {
                        handles = changed.ToArray(),
                        lineweight_mm = LineweightSpec.GetMillimeters(weight),
                        mode = LineweightSpec.GetMode(weight),
                        count = changed.Count
                    });
                }
            }
            catch (Exception ex)
            {
                // An uncommitted transaction aborts the entire batch, including setter failures.
                return CommandResult.Fail("failed to set lineweight: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    internal static class LineweightTargetPreflight
    {
        internal static bool TryCheckDrawing(Document doc, out string error)
        {
            error = null;
            if (doc == null || !ReferenceEquals(doc, Application.DocumentManager.MdiActiveDocument))
            {
                error = "the target document is no longer active";
                return false;
            }
            if (doc.IsReadOnly)
            {
                error = "the active drawing is read-only";
                return false;
            }
            return true;
        }

        internal static bool TryCheckLayer(LayerTableRecord layer, out string error)
        {
            error = null;
            if (layer.IsErased || layer.ObjectId.IsErased) error = "layer is erased";
            else if (layer.IsDependent) error = "xref-dependent layer cannot be modified";
            else if (layer.IsLocked) error = "layer is locked";
            return error == null;
        }

        internal static bool TryCheckEntity(Transaction tx, Entity entity, out string error)
        {
            error = null;
            if (entity.IsErased || entity.ObjectId.IsErased)
            {
                error = "entity is erased";
                return false;
            }
            var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
            if (!TryCheckLayer(layer, out error)) return false;

            DBObject current = entity;
            var visited = new HashSet<ObjectId>();
            while (current != null)
            {
                if (!visited.Add(current.ObjectId))
                {
                    error = "entity has an invalid owner chain";
                    return false;
                }
                if (current is BlockReference reference)
                {
                    var referencedBlock = (BlockTableRecord)tx.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    if (referencedBlock.IsDependent || referencedBlock.IsFromExternalReference)
                    {
                        error = "xref entity cannot be modified";
                        return false;
                    }
                }
                if (current is BlockTableRecord owner)
                {
                    if (owner.IsDependent || owner.IsFromExternalReference)
                    {
                        error = "xref entity cannot be modified";
                        return false;
                    }
                    return true;
                }
                if (current.OwnerId.IsNull)
                {
                    error = "entity has no owning block";
                    return false;
                }
                current = tx.GetObject(current.OwnerId, OpenMode.ForRead);
            }
            error = "entity owner is unavailable";
            return false;
        }
    }
}
