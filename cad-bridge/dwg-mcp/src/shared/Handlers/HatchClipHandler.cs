using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Hatches;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class PlanHatchClipHandler : IAcadCommand
    {
        public string Name => "plan_hatch_clip";
        public string Description => "Plan exact hatch clipping to the union of explicit closed polyline masks; no database writes.";
        public CommandSchema Schema => CommandSchemas.PlanHatchClip;
        public CommandResult Execute(Document doc, JToken parameters) => Run(doc, parameters, true);
        internal static CommandResult Run(Document doc, JToken parameters, bool plan)
        {
            if (!HatchClipInput.TryParse(parameters, plan, out var input, out var error)) return CommandResult.Fail(error);
            try { return CommandResult.Success(HatchClipService.Execute(doc, input, plan)); }
            catch (Exception e)
            {
                PluginLog.Error(plan ? "plan_hatch_clip" : "clip_hatches", e);
                return CommandResult.Fail("hatch clip failed: " + ErrorSanitizer.Sanitize(e.Message));
            }
        }
    }
    public sealed class ClipHatchesHandler : IAcadCommand
    {
        public string Name => "clip_hatches";
        public string Description => "Clip explicit guarded nonassociative hatches in one transaction; default dry-run, no save.";
        public CommandSchema Schema => CommandSchemas.ClipHatches;
        public CommandResult Execute(Document doc, JToken parameters) => PlanHatchClipHandler.Run(doc, parameters, false);
    }
}
