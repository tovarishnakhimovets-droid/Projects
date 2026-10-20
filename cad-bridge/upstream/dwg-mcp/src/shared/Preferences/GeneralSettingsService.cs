using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    public static class GeneralSettingsService
    {
        public static CommandResult Read(IAutoCadSettings settings, string[] ids)
        {
            try
            {
                var definitions = AutoCadSettingsCatalog.Select(ids);
                var profile = settings.Profile;
                var values = new JArray();
                foreach (var def in definitions)
                {
                    RequireProfile(settings, profile);
                    try
                    {
                        var value = settings.Read(def);
                        var item = new JObject
                        {
                            ["id"] = def.Id, ["available"] = true, ["writable"] = def.Writable,
                            ["value"] = GeneralSettingsInput.FormatValue(def, value), ["effect"] = def.Effect
                        };
                        if (def.Kind == SettingKind.Color)
                        {
                            long raw = value.Value<long>();
                            item["rgb"] = raw <= 0xffffff ? new JArray(raw & 255, (raw >> 8) & 255, (raw >> 16) & 255) : null;
                        }
                        values.Add(item);
                    }
                    catch (Exception ex)
                    {
                        values.Add(new JObject { ["id"] = def.Id, ["available"] = false, ["error"] = ErrorSanitizer.Sanitize(ex.Message) });
                    }
                }
                RequireProfile(settings, profile);
                return CommandResult.Success(new { profile, settings = values, saved_dwg = false });
            }
            catch (Exception ex) { return CommandResult.Fail("cannot read settings: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }

        public static CommandResult Apply(IAutoCadSettings settings, SettingsChangeRequest request)
        {
            var before = new Dictionary<string, JToken>(StringComparer.Ordinal);
            var changed = new List<ParsedSettingChange>();
            try
            {
                RequireProfile(settings, request.ExpectedProfile);
                foreach (var change in request.Changes)
                {
                    RequireProfile(settings, request.ExpectedProfile);
                    var value = settings.Read(change.Definition);
                    before.Add(change.Definition.Id, value.DeepClone());
                    if (!JToken.DeepEquals(value, change.ExpectedValue))
                        throw new InvalidOperationException("stale expected_value for " + change.Definition.Id + "; read dwg_get_settings again");
                    if (!JToken.DeepEquals(value, change.Value)) changed.Add(change);
                }
                RequireProfile(settings, request.ExpectedProfile);
            }
            catch (Exception ex) { return CommandResult.Fail("settings preflight failed: " + ErrorSanitizer.Sanitize(ex.Message)); }

            var preview = request.Changes.Select(c => new
            {
                id = c.Definition.Id, before = GeneralSettingsInput.FormatValue(c.Definition, before[c.Definition.Id]),
                target = GeneralSettingsInput.FormatValue(c.Definition, c.Value), effect = c.Definition.Effect
            }).ToArray();
            if (request.DryRun) return CommandResult.Success(new
            { dry_run = true, profile = request.ExpectedProfile, settings = preview, changed = changed.Select(c => c.Definition.Id).ToArray(), applied = false, saved_dwg = false });

            var attempted = new List<ParsedSettingChange>();
            try
            {
                foreach (var change in changed)
                {
                    RequireProfile(settings, request.ExpectedProfile);
                    // Recheck just before each write; earlier setters must not change another requested value.
                    if (!JToken.DeepEquals(settings.Read(change.Definition), before[change.Definition.Id]))
                        throw new InvalidOperationException("setting changed during the operation: " + change.Definition.Id);
                    attempted.Add(change); // A setter may mutate and then throw.
                    settings.Write(change.Definition, change.Value.DeepClone());
                }
                foreach (var change in request.Changes)
                {
                    RequireProfile(settings, request.ExpectedProfile);
                    if (!JToken.DeepEquals(settings.Read(change.Definition), change.Value))
                        throw new InvalidOperationException("native readback mismatch for " + change.Definition.Id);
                }
                RequireProfile(settings, request.ExpectedProfile);
                return CommandResult.Success(new
                {
                    profile = request.ExpectedProfile, settings = preview, changed = changed.Select(c => c.Definition.Id).ToArray(),
                    applied = changed.Count > 0, verified = true, verification = "stored_native_values",
                    saved_dwg = false, undo_supported = false
                });
            }
            catch (Exception ex)
            {
                var errors = new List<string>();
                for (var i = attempted.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        RequireProfile(settings, request.ExpectedProfile);
                        settings.Write(attempted[i].Definition, before[attempted[i].Definition.Id].DeepClone());
                    }
                    catch (Exception recovery) { errors.Add(ErrorSanitizer.Sanitize(recovery.Message)); }
                }
                bool restored = true;
                try
                {
                    RequireProfile(settings, request.ExpectedProfile);
                    foreach (var change in request.Changes)
                        if (!JToken.DeepEquals(settings.Read(change.Definition), before[change.Definition.Id])) restored = false;
                    RequireProfile(settings, request.ExpectedProfile);
                }
                catch (Exception recovery) { restored = false; errors.Add(ErrorSanitizer.Sanitize(recovery.Message)); }
                return CommandResult.Fail("settings update failed: " + ErrorSanitizer.Sanitize(ex.Message) +
                    "; rollback_verified=" + restored.ToString().ToLowerInvariant() +
                    (errors.Count == 0 ? "" : "; rollback errors: " + string.Join("; ", errors)) +
                    "; read dwg_get_settings before any retry; drawing was not saved");
            }
        }

        private static void RequireProfile(IAutoCadSettings settings, string expected)
        {
            if (!string.Equals(settings.Profile, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("active profile changed; no writes into another profile are allowed");
        }
    }
}
