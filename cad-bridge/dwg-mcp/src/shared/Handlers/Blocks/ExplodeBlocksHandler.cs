using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class PlanBlockExplodeHandler : IAcadCommand
    {
        public string Name => "plan_block_explode";
        public string Description => "Read bounded ordinary-block explode snapshots without creating entities or modifying the drawing.";
        public CommandSchema Schema => CommandSchemas.PlanBlockExplode;
        public CommandResult Execute(Document doc, JToken parameters) => Run(doc, parameters, true);
        internal static CommandResult Run(Document doc, JToken parameters, bool plan)
        {
            if (!BlockExplodeInput.TryParse(parameters, plan, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            try { return CommandResult.Success(BlockExplodeService.Execute(doc, input, plan)); }
            catch (Exception e) { return CommandResult.Fail("block explode failed: " + ErrorSanitizer.Sanitize(e.Message)); }
        }
    }
    public sealed class ExplodeBlocksHandler : IAcadCommand
    {
        public string Name => "explode_blocks";
        public string Description => "Explode explicitly guarded ordinary block references one level in one bounded transaction; no save.";
        public CommandSchema Schema => CommandSchemas.ExplodeBlocks;
        public CommandResult Execute(Document doc, JToken parameters) => PlanBlockExplodeHandler.Run(doc, parameters, false);
    }
}
