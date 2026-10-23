using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Tables;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class ReadTableHandler : IAcadCommand
    {
        public string Name => "read_table";
        public string Description => "Read one bounded local plain-text native Table without updating layout or evaluating fields.";
        public CommandSchema Schema => CommandSchemas.ReadTable;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try { return CommandResult.Success(TableService.Read(doc, parameters as JObject)); }
            catch (Exception ex) { return CommandResult.Fail("table reading failed: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
