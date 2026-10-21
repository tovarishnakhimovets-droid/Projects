using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateLeaderHandler : IAcadCommand
    {
        public string Name => "create_leader";
        public string Description => "Create a straight MLeader with a closed filled arrow in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateLeader;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationRequestValidation.TryReadLeader(obj, out var input, out var error))
                return CommandResult.Fail(error);
            if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);

            MLeader entity = null;
            var ownsEntity = false;
            try
            {
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    // Resolve the text style before a layer or entity can be written.
                    if (!AnnotationEntityFactory.TryCreateLeader(obj, out entity, out error, db, tx))
                        return CommandResult.Fail(error);
                    ownsEntity = true;
                    if (input.Layer != null)
                    {
                        if (!CadLayerService.TryEnsureLayer(db, tx, input.Layer,
                            input.ColorIndex == 256 ? 7 : input.ColorIndex,
                            out var layerId, out _, out error))
                            return CommandResult.Fail(ErrorSanitizer.Sanitize(error));
                        entity.LayerId = layerId;
                    }
                    var layer = (LayerTableRecord)tx.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (!LineweightTargetPreflight.TryCheckLayer(layer, out error)) return CommandResult.Fail(error);
                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity);
                    ownsEntity = false;
                    var result = new
                    {
                        handle = entity.Handle.ToString(),
                        entity = CadEntityProperties.Describe(entity, tx, includeGeometry: true),
                        leader = new
                        {
                            content_type = entity.ContentType.ToString(),
                            leader_line_type = entity.LeaderLineType.ToString(),
                            arrow_size = entity.ArrowSize,
                            arrow_symbol_handle = entity.ArrowSymbolId.IsNull ? null : entity.ArrowSymbolId.Handle.ToString(),
                            enable_landing = entity.EnableLanding,
                            enable_dogleg = entity.EnableDogleg,
                            enable_frame_text = entity.EnableFrameText,
                            enable_annotation_scale = entity.EnableAnnotationScale,
                            scale = entity.Scale
                        }
                    };
                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create leader: " + ErrorSanitizer.Sanitize(ex.Message));
            }
            finally
            {
                if (ownsEntity) entity?.Dispose();
            }
        }
    }
}
