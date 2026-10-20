using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Preferences;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class GetCrosshairSettingsHandler : IAcadCommand
    {
        public string Name => "get_crosshair_settings";
        public string Description => "Read profile crosshair size and separate model/layout colors.";
        public CommandSchema Schema => CommandSchemas.GetCrosshairSettings;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try { return CommandResult.Success(new AutoCadCrosshairPreferences().Read().ToJson()); }
            catch (Exception ex) { return CommandResult.Fail("cannot read AutoCAD crosshair preferences: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class SetCrosshairSettingsHandler : IAcadCommand
    {
        public string Name => "set_crosshair_settings";
        public string Description => "Set explicit crosshair preferences, verifying native readback for model and layout.";
        public CommandSchema Schema => CommandSchemas.SetCrosshairSettings;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!CrosshairSettingsInput.TryParse(parameters, out var change, out var error)) return CommandResult.Fail(error);
            try
            {
                if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress) ||
                    Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0)
                    return CommandResult.Fail("finish the active command and close Options before changing profile preferences");
                return CrosshairSettingsService.Apply(new AutoCadCrosshairPreferences(), change);
            }
            catch (Exception ex) { return CommandResult.Fail("cannot access AutoCAD crosshair preferences: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
