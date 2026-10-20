using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Preferences;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class GetSettingsHandler : IAcadCommand
    {
        public string Name => "get_settings";
        public string Description => "Read explicit cataloged application preferences.";
        public CommandSchema Schema => CommandSchemas.GetSettings;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                if (!(parameters is JObject obj) || obj.Properties().Any(p => p.Name != "ids") ||
                    !(obj["ids"] is JArray ids) || ids.Any(t => t.Type != JTokenType.String))
                    return CommandResult.Fail("ids must be an array of catalog setting IDs");
                return GeneralSettingsService.Read(new AutoCadSettings(), ids.Values<string>().ToArray());
            }
            catch (Exception ex) { return CommandResult.Fail("cannot access preferences: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class SetSettingsHandler : IAcadCommand
    {
        public string Name => "set_settings";
        public string Description => "Change explicitly cataloged profile preferences with expectations and native verification.";
        public CommandSchema Schema => CommandSchemas.SetSettings;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!GeneralSettingsInput.TryParse(parameters, out var request, out var error)) return CommandResult.Fail(error);
            try
            {
                if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress) || Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0)
                    return CommandResult.Fail("finish the active command and close Options before changing preferences");
                return GeneralSettingsService.Apply(new AutoCadSettings(), request);
            }
            catch (Exception ex) { return CommandResult.Fail("cannot access preferences: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }
}
