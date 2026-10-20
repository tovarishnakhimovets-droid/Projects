using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    public enum SettingKind { Boolean, Integer, Color, Text }

    public sealed class SettingDefinition
    {
        public string Id { get; internal set; }
        public string Group { get; internal set; }
        public string Property { get; internal set; }
        public SettingKind Kind { get; internal set; }
        public bool Writable { get; internal set; }
        public int? Minimum { get; internal set; }
        public int? Maximum { get; internal set; }
        public int[] Allowed { get; internal set; }
        public string Description { get; internal set; }
        public string Effect { get; internal set; }
        public string Restriction { get; internal set; }
        public string Source { get; internal set; }

        public object ToJson() => new
        {
            id = Id, group = Group, type = Kind.ToString().ToLowerInvariant(), writable = Writable,
            minimum = Minimum, maximum = Maximum, allowed = Allowed, description = Description,
            effect = Effect, restriction = Restriction, source = Source,
            native_property = "Application.Preferences." + NativeGroup + "." + Property,
            scope = "application_preferences", undo_supported = false
        };

        internal string NativeGroup => Group switch
        {
            "display" => "Display", "drafting" => "Drafting", "selection" => "Selection",
            "open_save" => "OpenSave", "user" => "User", "system" => "System",
            "output" => "Output", "files" => "Files", "profiles" => "Profiles",
            _ => throw new InvalidOperationException("unsupported preference group")
        };
    }

    // Only entries in this source-controlled catalog may reach the native adapter.
    // A property being present in COM does not make it safe to expose for writing.
    public static partial class AutoCadSettingsCatalog
    {
        public static IReadOnlyList<SettingDefinition> All { get; } = Build();

        private static IReadOnlyList<SettingDefinition> Build()
        {
            var list = new List<SettingDefinition>();
            AddDisplayDraftingSelection(list);
            AddOtherPreferences(list);
            if (list.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new InvalidOperationException("duplicate settings catalog id");
            return list.AsReadOnly();
        }

        public static SettingDefinition Find(string id) => All.FirstOrDefault(x => x.Id == id);

        public static IReadOnlyList<SettingDefinition> Select(string[] ids)
        {
            if (ids == null || ids.Length < 1 || ids.Length > 32)
                throw new ArgumentException("ids must contain 1-32 setting IDs from dwg_list_settings");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new ArgumentException("duplicate setting IDs are not allowed");
            return ids.Select(id => Find(id) ?? throw new ArgumentException("unknown setting ID; use dwg_list_settings")).ToArray();
        }

        private static void Add(List<SettingDefinition> list, string id, string group, string property,
            SettingKind kind, string description, bool writable = true, int? min = null, int? max = null,
            int[] allowed = null, string effect = "immediate", string restriction = null, string source = null)
        {
            list.Add(new SettingDefinition
            {
                Id = id, Group = group, Property = property, Kind = kind, Description = description,
                Writable = writable, Minimum = min, Maximum = max, Allowed = allowed,
                Effect = effect, Restriction = restriction, Source = source
            });
        }
    }
}
