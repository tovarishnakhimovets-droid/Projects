using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.EntityBatch;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class CreateEntitiesHandler : IAcadCommand
    {
        public string Name => "create_entities";
        public string Description => "Create a bounded heterogeneous whitelisted plan in one guarded native transaction; never save.";
        public CommandSchema Schema => CommandSchemas.CreateEntities;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!EntityBatchInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            try { return CommandResult.Success(EntityBatchService.Create(doc, input)); }
            catch (Exception ex) { return CommandResult.Fail("entity batch creation failed: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
