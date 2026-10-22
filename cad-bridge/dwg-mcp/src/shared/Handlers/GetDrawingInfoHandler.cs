using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class GetDrawingInfoHandler : IAcadCommand
    {
        public string Name => "get_drawing_info";
        public string Description => "Return small metadata for the current AutoCAD drawing.";
        public CommandSchema Schema => CommandSchemas.GetDrawingInfo;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var db = doc.Database;
            var identity = DocumentGuard.Capture(doc);
            var documentName = identity.DocumentName;
            var databaseFilename = identity.DatabaseFilename;
            var originalFilename = identity.OriginalFilename;
            var safeDocumentName = SafeFileName(documentName);
            var drawingName = FirstNonBlank(
                safeDocumentName,
                SafeFileName(databaseFilename),
                SafeFileName(originalFilename));

            return CommandResult.Success(new
            {
                drawing_name = drawingName,
                document_name = safeDocumentName,
                has_saved_path = identity.DocumentPath != null,
                document_path = identity.DocumentPath,
                fingerprint = identity.Fingerprint,
                database_filename = databaseFilename,
                original_filename = originalFilename,
                is_titled = identity.IsTitled,
                file_exists = identity.Exists,
                file_read_only = identity.IsReadOnlyFile,
                document_read_only = identity.IsReadOnlyDocument,
                dbmod = identity.DatabaseModified,
                current_layer = GetCurrentLayerName(db),
                current_space = db.TileMode ? "model" : "paper",
                current_space_record = GetCurrentSpaceRecordName(db),
                current_layout = GetCurrentLayoutName(db),
                tile_mode = db.TileMode,
                insunits = db.Insunits.ToString(),
                insunits_code = (int)db.Insunits,
                lunits = db.Lunits,
                aunits = db.Aunits,
                measurement = db.Measurement.ToString()
            });
        }

        private static string GetCurrentLayerName(Database db)
        {
            try
            {
                using (var tx = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var layer = (LayerTableRecord)tx.GetObject(db.Clayer, OpenMode.ForRead);
                    var name = layer.Name;
                    return name;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentSpaceRecordName(Database db)
        {
            try
            {
                using (var tx = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var currentSpace = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    var name = currentSpace.Name;
                    return name;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentLayoutName(Database db)
        {
            try
            {
                using (var tx = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var spaceId = db.CurrentSpaceId;
                    // MSPACE inside a paper viewport still belongs to its sheet.
                    // Read that database's paper viewport, not the active UI manager.
                    if (!db.TileMode)
                    {
                        if (db.PaperSpaceVportId.IsNull) return null;
                        var viewport = tx.GetObject(db.PaperSpaceVportId, OpenMode.ForRead) as Viewport;
                        if (viewport == null) return null;
                        spaceId = viewport.OwnerId;
                    }
                    var space = tx.GetObject(spaceId, OpenMode.ForRead) as BlockTableRecord;
                    if (space == null || space.LayoutId.IsNull) return null;
                    var layout = tx.GetObject(space.LayoutId, OpenMode.ForRead) as Layout;
                    return layout?.LayoutName;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var fileName = Path.GetFileName(value.Trim());
                return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
