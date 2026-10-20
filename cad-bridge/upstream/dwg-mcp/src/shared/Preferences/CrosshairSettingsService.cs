using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    public static class CrosshairSettingsService
    {
        public static CommandResult Apply(ICrosshairPreferences preferences, CrosshairSettingsChange change)
        {
            CrosshairSettingsSnapshot before;
            try { before = preferences.Read(); }
            catch (Exception ex) { return CommandResult.Fail("cannot read crosshair preferences: " + ex.Message); }
            if (!string.Equals(before.Profile, change.ExpectedProfile, StringComparison.Ordinal))
                return CommandResult.Fail("active profile changed; read dwg_get_crosshair_settings before editing");

            var target = change.Target(before);
            var fields = new List<string>();
            if (target.Size != before.Size) fields.Add("crosshair_size");
            if (target.ModelColor != before.ModelColor) fields.Add("model_color_rgb");
            if (target.LayoutColor != before.LayoutColor) fields.Add("layout_color_rgb");
            if (change.DryRun) return CommandResult.Success(new
            { dry_run = true, before = before.ToJson(), target = target.ToJson(), changed = fields, applied = false, saved_dwg = false });

            var attempted = new List<string>();
            try
            {
                foreach (var field in fields)
                {
                    RequireProfile(preferences.Read(), before.Profile);
                    // A native setter can change state and then throw: include it in rollback.
                    attempted.Add(field);
                    Set(preferences, field, target);
                }
                var after = preferences.Read();
                if (!after.SameAs(target)) throw new InvalidOperationException("native readback does not match the requested preferences");
                return CommandResult.Success(new
                {
                    before = before.ToJson(), after = after.ToJson(), changed = fields,
                    applied = true, verified = true, saved_dwg = false, undo_supported = false
                });
            }
            catch (Exception ex)
            {
                // Preferences are outside database transactions/Undo. Report verified recovery,
                // never promise atomic rollback or silently retry an uncertain write.
                var recoveryErrors = new List<string>();
                for (int i = attempted.Count - 1; i >= 0; i--)
                {
                    try { RequireProfile(preferences.Read(), before.Profile); Set(preferences, attempted[i], before); }
                    catch (Exception recovery) { recoveryErrors.Add(recovery.Message); }
                }
                bool restored = false;
                try { restored = preferences.Read().SameAs(before); }
                catch (Exception recovery) { recoveryErrors.Add(recovery.Message); }
                return CommandResult.Fail("crosshair preferences failed: " + ex.Message +
                    "; rollback_verified=" + restored.ToString().ToLowerInvariant() +
                    (recoveryErrors.Count == 0 ? "" : "; rollback errors: " + string.Join("; ", recoveryErrors)) +
                    "; inspect dwg_get_crosshair_settings before any retry; drawing was not saved");
            }
        }

        private static void RequireProfile(CrosshairSettingsSnapshot state, string expected)
        {
            if (!string.Equals(state.Profile, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("active profile changed; do not write preferences into another profile");
        }

        private static void Set(ICrosshairPreferences preferences, string field, CrosshairSettingsSnapshot value)
        {
            switch (field)
            {
                case "crosshair_size": preferences.SetSize(value.Size); break;
                case "model_color_rgb": preferences.SetModelColor(value.ModelColor); break;
                case "layout_color_rgb": preferences.SetLayoutColor(value.LayoutColor); break;
                default: throw new InvalidOperationException("unsupported crosshair preference");
            }
        }
    }
}
