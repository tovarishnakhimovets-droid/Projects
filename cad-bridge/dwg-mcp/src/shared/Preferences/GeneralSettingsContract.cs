using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    // Concrete DTOs keep MCP's actual SDK schema typed, including nested array items.
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public sealed class SettingValue
    {
        public bool? boolean { get; set; }
        public int? integer { get; set; }
        public string text { get; set; }
        public int[] rgb { get; set; }
        public long? ole_color { get; set; }
    }

    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public sealed class SettingChange
    {
        [System.Text.Json.Serialization.JsonRequired]
        public string id { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public SettingValue expected_value { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public SettingValue value { get; set; }
    }

    public sealed class ParsedSettingChange
    {
        public SettingDefinition Definition { get; internal set; }
        public JToken ExpectedValue { get; internal set; }
        public JToken Value { get; internal set; }
    }

    public sealed class SettingsChangeRequest
    {
        public string ExpectedProfile { get; internal set; }
        public IReadOnlyList<ParsedSettingChange> Changes { get; internal set; }
        public bool DryRun { get; internal set; }
    }

    public interface IAutoCadSettings
    {
        string Profile { get; }
        JToken Read(SettingDefinition definition);
        void Write(SettingDefinition definition, JToken value);
    }

    public static class GeneralSettingsInput
    {
        public static bool TryParse(JToken input, out SettingsChangeRequest request, out string error)
        {
            request = null; error = null;
            try
            {
                var obj = StrictObject(input, "request", "expected_profile", "changes", "dry_run");
                var profile = obj["expected_profile"];
                if (profile?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)profile) || ((string)profile).Length > 256)
                    throw new ArgumentException("expected_profile must be the exact profile from dwg_get_settings (1-256 characters)");
                if (obj["dry_run"] != null && obj["dry_run"].Type != JTokenType.Boolean)
                    throw new ArgumentException("dry_run must be a boolean");
                if (!(obj["changes"] is JArray changes) || changes.Count < 1 || changes.Count > 32)
                    throw new ArgumentException("changes must contain 1-32 entries");
                var parsed = new List<ParsedSettingChange>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in changes)
                {
                    var change = StrictObject(token, "change", "id", "expected_value", "value");
                    if (change["id"]?.Type != JTokenType.String) throw new ArgumentException("each change requires a setting id");
                    var id = (string)change["id"];
                    var def = AutoCadSettingsCatalog.Find(id);
                    if (def == null) throw new ArgumentException("unknown setting ID; use dwg_list_settings");
                    if (!seen.Add(id)) throw new ArgumentException("duplicate setting IDs are not allowed");
                    if (!def.Writable) throw new ArgumentException(id + " is read-only: " + def.Restriction);
                    parsed.Add(new ParsedSettingChange
                    {
                        Definition = def,
                        ExpectedValue = ParseValue(def, change["expected_value"], true),
                        Value = ParseValue(def, change["value"], false)
                    });
                }
                request = new SettingsChangeRequest
                { ExpectedProfile = (string)profile, Changes = parsed.AsReadOnly(), DryRun = (bool?)obj["dry_run"] ?? false };
                return true;
            }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            catch (OverflowException) { error = "setting value is outside its numeric range"; return false; }
            catch (InvalidCastException) { error = "setting value is outside its numeric range or has an invalid type"; return false; }
        }

        private static JObject StrictObject(JToken input, string label, params string[] fields)
        {
            if (!(input is JObject obj)) throw new ArgumentException(label + " must be an object");
            if (obj.Properties().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal)))
                throw new ArgumentException(label + " contains an unknown field");
            return obj;
        }

        private static JToken ParseValue(SettingDefinition def, JToken input, bool expected)
        {
            var obj = StrictObject(input, def.Id + " value", "boolean", "integer", "text", "rgb", "ole_color");
            var entries = obj.Properties().Where(x => x.Value.Type != JTokenType.Null).ToArray();
            if (entries.Length != 1) throw new ArgumentException(def.Id + " value requires exactly one typed field");
            var field = entries[0];
            switch (def.Kind)
            {
                case SettingKind.Boolean:
                    if (field.Name == "boolean" && field.Value.Type == JTokenType.Boolean) return field.Value.DeepClone();
                    break;
                case SettingKind.Integer:
                    if (field.Name != "integer" || field.Value.Type != JTokenType.Integer) break;
                    var integer = field.Value.Value<long>();
                    // Expected values may legitimately be outside our conservative writable range.
                    if (integer < int.MinValue || integer > int.MaxValue || (!expected &&
                        ((def.Minimum.HasValue && integer < def.Minimum) || (def.Maximum.HasValue && integer > def.Maximum) ||
                        (def.Allowed != null && !def.Allowed.Contains((int)integer)))))
                        throw new ArgumentException(def.Id + " integer is outside its documented writable range");
                    return new JValue((int)integer);
                case SettingKind.Color:
                    if (expected && field.Name == "ole_color" && field.Value.Type == JTokenType.Integer)
                    {
                        var raw = field.Value.Value<long>();
                        if (raw < 0 || raw > uint.MaxValue) throw new ArgumentException("ole_color must be 0-4294967295");
                        return new JValue(raw);
                    }
                    if (field.Name != "rgb" || !(field.Value is JArray rgb) || rgb.Count != 3 ||
                        rgb.Any(v => v.Type != JTokenType.Integer || v.Value<long>() < 0 || v.Value<long>() > 255)) break;
                    return new JValue((long)rgb[0] + ((long)rgb[1] << 8) + ((long)rgb[2] << 16));
                case SettingKind.Text:
                    if (field.Name == "text" && field.Value.Type == JTokenType.String) return field.Value.DeepClone();
                    break;
            }
            throw new ArgumentException(def.Id + " requires a matching typed value; colors require integer rgb [R,G,B] (expected_value may use raw ole_color)");
        }

        public static JObject FormatValue(SettingDefinition def, JToken value)
        {
            var field = def.Kind switch
            { SettingKind.Boolean => "boolean", SettingKind.Integer => "integer", SettingKind.Color => "ole_color", _ => "text" };
            return new JObject { [field] = value.DeepClone() };
        }
    }
}
