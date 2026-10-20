// Modified by the CAD team (2026-10-09): text/style tools; see cad-bridge/docs/TOOLS.md#text.
using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateTextHandler : IAcadCommand
    {
        public string Name => "create_text";
        public string Description => "Create single-line text in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateText;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationRequestValidation.TryReadDbText(obj, out var input, out var error))
                return CommandResult.Fail(error);
            if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);

            DBText entity = null;
            var ownsEntity = false;
            try
            {
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    // Resolve an explicitly requested style before any layer creation or append.
                    if (!AnnotationEntityFactory.TryCreateText(obj, out entity, out error, db, tx))
                        return CommandResult.Fail(error);
                    ownsEntity = true;
                    if (input.Layer != null)
                    {
                        // A new layer needs an explicit color; the text itself can inherit it.
                        var layerColor = input.ColorIndex == 256 ? 7 : input.ColorIndex;
                        if (!CadLayerService.TryEnsureLayer(db, tx, input.Layer, layerColor,
                            out var layerId, out _, out error))
                            return CommandResult.Fail(ErrorSanitizer.Sanitize(error));
                        entity.LayerId = layerId;
                    }
                    var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (!LineweightTargetPreflight.TryCheckLayer(layer, out error)) return CommandResult.Fail(error);
                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity);
                    // The transaction owns the DBText only after append succeeds.
                    ownsEntity = false;
                    var described = CadEntityProperties.Describe(entity, tx, includeGeometry: true);
                    var result = new { handle = entity.Handle.ToString(), entity = described };
                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create text: " + ErrorSanitizer.Sanitize(ex.Message));
            }
            finally
            {
                if (ownsEntity) entity?.Dispose();
            }
        }
    }
}
