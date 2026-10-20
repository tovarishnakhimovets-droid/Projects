using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SetLayerLineweightHandler : IAcadCommand
    {
        public string Name => "set_layer_lineweight";
        public string Description => "Set an explicit lineweight on existing unlocked local layers in one transaction.";
        public CommandSchema Schema => CommandSchemas.SetLayerLineweight;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj)) return CommandResult.Fail("params must be an object");
            var names = obj["layers"] as JArray;
            if (names == null || names.Count < 1 || names.Count > 1000)
                return CommandResult.Fail("layers must be an array containing 1 to 1000 strings");
            if (!LineweightSpec.TryParseLayer(obj["lineweight_mm"], out var weight, out var error))
                return CommandResult.Fail(error);

            var requested = new List<string>();
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in names)
            {
                if (token.Type != JTokenType.String) return CommandResult.Fail("layers entries must be strings");
                var name = token.Value<string>();
                if (string.IsNullOrWhiteSpace(name)) return CommandResult.Fail("layer name must not be empty");
                if (!distinct.Add(name)) return CommandResult.Fail("layers must not contain duplicates");
                requested.Add(name);
            }

            try
            {
                if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);
                foreach (var name in requested)
                    if (!CadLayerService.TryValidateLayerName(name, out error)) return CommandResult.Fail(error);

                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var table = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var layers = new List<LayerTableRecord>();
                    foreach (var name in requested)
                    {
                        if (!table.Has(name)) return CommandResult.Fail($"layer not found: {name}");
                        var id = table[name];
                        if (id.IsErased) return CommandResult.Fail($"layer is erased: {name}");
                        var layer = (LayerTableRecord)tx.GetObject(id, OpenMode.ForRead);
                        if (!LineweightTargetPreflight.TryCheckLayer(layer, out error))
                            return CommandResult.Fail($"layer {name}: {error}");
                        layers.Add(layer);
                    }

                    // Opening every layer first keeps late read-only failures ahead of writes.
                    foreach (var layer in layers)
                    {
                        layer.UpgradeOpen();
                        if (!layer.IsWriteEnabled) return CommandResult.Fail("layer is read-only");
                    }

                    var changed = new List<string>();
                    foreach (var layer in layers)
                    {
                        if ((int)layer.LineWeight == weight) continue;
                        layer.LineWeight = (LineWeight)weight;
                        changed.Add(layer.Name);
                    }
                    tx.Commit();
                    return CommandResult.Success(new
                    {
                        layers = changed.ToArray(),
                        lineweight_mm = LineweightSpec.GetMillimeters(weight),
                        mode = "explicit",
                        count = changed.Count
                    });
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to set layer lineweight: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }
}
