using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class InspectBlockHandler : IAcadCommand
    {
        public string Name => "inspect_block";
        public string Description => "Read one block reference, bounded dynamic properties and direct definition geometry without changing the drawing.";
        public CommandSchema Schema => CommandSchemas.InspectBlock;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!BlockInspectionInput.TryParse(parameters, out var input, out var error))
                return CommandResult.Fail(error);
            if (input.ExpectedDocument != null)
            {
                error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                if (error != null) return CommandResult.Fail(error);
            }
            try { return CommandResult.Success(BlockInspectionService.Read(doc, input)); }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to inspect block: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }
}
