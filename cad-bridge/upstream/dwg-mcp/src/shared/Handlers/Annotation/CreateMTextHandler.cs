using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateMTextHandler : IAcadCommand
    {
        public string Name => "create_mtext";
        public string Description => "Create multi-line text in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateMText;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationRequestValidation.TryReadMText(obj, out var input, out var error))
                return CommandResult.Fail(error);
            if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);

            MText entity = null;
            var ownsEntity = false;
            try
            {
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    // Resolve style before ensuring a layer or appending any object.
                    if (!AnnotationEntityFactory.TryCreateMText(obj, out entity, out error, db, tx))
                        return CommandResult.Fail(error);
                    ownsEntity = true;
                    var appearance = input.Appearance;
                    if (appearance.Layer != null)
                    {
                        if (!CadLayerService.TryEnsureLayer(db, tx, appearance.Layer,
                            appearance.ColorIndex == 256 ? 7 : appearance.ColorIndex,
                            out var layerId, out _, out error))
                            return CommandResult.Fail(ErrorSanitizer.Sanitize(error));
                        entity.LayerId = layerId;
                    }
                    var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (!LineweightTargetPreflight.TryCheckLayer(layer, out error)) return CommandResult.Fail(error);
                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity);
                    ownsEntity = false;
                    var result = new { handle = entity.Handle.ToString(),
                        entity = CadEntityProperties.Describe(entity, tx, includeGeometry: true) };
                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create mtext: " + ErrorSanitizer.Sanitize(ex.Message));
            }
            finally
            {
                if (ownsEntity) entity?.Dispose();
            }
        }
    }
}
