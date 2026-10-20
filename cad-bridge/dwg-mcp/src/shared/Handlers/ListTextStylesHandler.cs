using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListTextStylesHandler : IAcadCommand
    {
        public string Name => "list_text_styles";
        public string Description => "Return bounded text-style metadata recorded in the current drawing.";
        public CommandSchema Schema => CommandSchemas.ListTextStyles;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!ListTextStylesInput.TryParse(parameters, out var input, out var error))
                return CommandResult.Fail(error);

            var styles = new List<object>();
            var truncated = false;
            var db = doc.Database;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var table = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId styleId in table)
                {
                    if (styles.Count == input.Limit)
                    {
                        truncated = true;
                        break;
                    }
                    var style = (TextStyleTableRecord)tx.GetObject(styleId, OpenMode.ForRead);
                    var record = CadTextStyleMetadata.Describe(style);
                    record["is_current"] = styleId == db.Textstyle;
                    styles.Add(record);
                }
                tx.Commit();
            }
            return CommandResult.Success(new { styles, count = styles.Count, limit = input.Limit, truncated });
        }
    }
}
