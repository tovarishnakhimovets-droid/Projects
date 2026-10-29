using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Xrefs;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class ListXrefsHandler : IAcadCommand
    {
        public string Name => "list_xrefs";
        public string Description => "Read the bounded host xref graph, guarded insert metadata, clipping and direct entity histograms.";
        public CommandSchema Schema => CommandSchemas.ListXrefs;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!XrefListInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            if (input.ExpectedDocument != null)
            {
                error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                if (error != null) return CommandResult.Fail(error);
            }
            try { return CommandResult.Success(XrefService.List(doc, input)); }
            catch (Exception ex) { return CommandResult.Fail("failed to list xrefs: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class BindCurrentXrefHandler : IAcadCommand
    {
        public string Name => "bind_current_xref";
        public string Description => "Bind the whole current loaded root definition after fresh metadata checks under the write lock; no detach, explode or save.";
        public CommandSchema Schema => CommandSchemas.BindCurrentXref;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!XrefBindCurrentInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            try { return XrefService.BindCurrent(doc, input); }
            catch (Exception ex) { return CommandResult.Fail("current xref bind preflight failed before native mutation: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class ManageXrefsHandler : IAcadCommand
    {
        public string Name => "manage_xrefs";
        public string Description => "Bind with preserved symbol identities or detach exactly one guarded root xref; never save.";
        public CommandSchema Schema => CommandSchemas.ManageXrefs;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!XrefManageInput.TryParse(parameters, out var input, out var error)) return CommandResult.Fail(error);
            try { return XrefService.Manage(doc, input); }
            catch (Exception ex) { return CommandResult.Fail("xref preflight failed before native mutation: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
