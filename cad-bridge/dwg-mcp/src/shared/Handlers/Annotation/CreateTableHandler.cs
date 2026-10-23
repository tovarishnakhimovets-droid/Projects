using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Tables;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateTableHandler : IAcadCommand
    {
        public string Name => "create_table";
        public string Description => "Create one bounded native Table with explicit sizes, merges and text in one guarded transaction; never save.";
        public CommandSchema Schema => CommandSchemas.CreateTable;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!TableInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            try { return CommandResult.Success(TableService.Create(doc, input)); }
            catch (Exception ex) { return CommandResult.Fail("table creation failed: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
