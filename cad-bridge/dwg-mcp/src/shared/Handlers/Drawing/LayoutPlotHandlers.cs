using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class LayoutReadHandler : IAcadCommand
    {
        public LayoutReadHandler(string name) { Name = name; }
        public string Name { get; }
        public string Description => "Read bounded layout metadata or direct layout entities without changing the editor.";
        public CommandSchema Schema => Name == "list_layouts" ? CommandSchemas.ListLayouts : CommandSchemas.GetLayoutEntities;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                var p = (JObject)parameters;
                var error = DocumentGuard.Validate(doc, p.Value<string>("expected_document"), p.Value<string>("expected_fingerprint"));
                if (error != null) return CommandResult.Fail(error);
                if (Name == "list_layouts") return CommandResult.Success(LayoutPlotService.List(doc));
                return CommandResult.Success(LayoutPlotService.Entities(doc, p.Value<string>("layout_name"),
                    p.Value<string>("entity_type"), p.Value<string>("layer"), p.Value<int?>("limit") ?? 1000,
                    p.Value<bool?>("include_geometry") ?? false));
            }
            catch (Exception ex) { return CommandResult.Fail(Name + " failed: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class LayoutWriteHandler : IAcadCommand
    {
        public LayoutWriteHandler(string name) { Name = name; }
        public string Name { get; }
        public string Description => "Explicit guarded layout selection or one-page PDF plotting, with no drawing save.";
        public CommandSchema Schema => Name == "select_layout" ? CommandSchemas.SelectLayout : CommandSchemas.PlotPdf;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                var p = (JObject)parameters;
                var error = DocumentGuard.Validate(doc, p.Value<string>("expected_document"), p.Value<string>("expected_fingerprint"));
                if (error != null) return CommandResult.Fail(error);
                var dryRun = p.Value<bool?>("dry_run") ?? true;
                if (Name == "select_layout") return CommandResult.Success(LayoutPlotService.Select(doc,
                    p.Value<string>("layout_name"), p.Value<string>("expected_current_layout"), dryRun));
                return CommandResult.Success(LayoutPlotService.Plot(doc, p.Value<string>("layout_name"),
                    p["expected_plot_settings"] as JObject, p.Value<string>("output_path"),
                    p.Value<bool?>("overwrite_existing") ?? false, p.Value<bool?>("allow_repo_output") ?? false, dryRun));
            }
            catch (Exception ex) { return CommandResult.Fail(Name + " failed: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
