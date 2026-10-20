using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    // Autodesk's supported Application.Preferences API, invoked in the host's main
    // thread via DocumentInvoker. No external COM/ROT bridge, registry edits or UI clicks.
    public sealed class AutoCadCrosshairPreferences : ICrosshairPreferences
    {
        private readonly AutoCadSettings _settings = new AutoCadSettings();

        public CrosshairSettingsSnapshot Read() => new CrosshairSettingsSnapshot(
            _settings.Profile, ReadInteger("display.crosshair_size"),
            unchecked((int)ReadColor("display.model_crosshair_color")),
            unchecked((int)ReadColor("display.layout_crosshair_color")));

        public void SetSize(int value) => Write("display.crosshair_size", new JValue(value));
        public void SetModelColor(int value) => Write("display.model_crosshair_color", new JValue((long)unchecked((uint)value)));
        public void SetLayoutColor(int value) => Write("display.layout_crosshair_color", new JValue((long)unchecked((uint)value)));
        private int ReadInteger(string id) => _settings.Read(AutoCadSettingsCatalog.Find(id)).Value<int>();
        private long ReadColor(string id) => _settings.Read(AutoCadSettingsCatalog.Find(id)).Value<long>();
        private void Write(string id, JToken value) => _settings.Write(AutoCadSettingsCatalog.Find(id), value);
    }
}
