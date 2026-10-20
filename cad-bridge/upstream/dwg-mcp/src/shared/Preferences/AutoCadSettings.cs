using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Preferences
{
    // In-process Application.Preferences, always called through the main-thread dispatcher.
    // Reflection is limited to properties from our fixed catalog, never a user-supplied path.
    public sealed class AutoCadSettings : IAutoCadSettings
    {
        private readonly object _preferences = Application.Preferences;
        public string Profile => Convert.ToString(Get(Get(_preferences, "Profiles"), "ActiveProfile"), CultureInfo.InvariantCulture);

        public JToken Read(SettingDefinition definition)
        {
            CheckDefinition(definition);
            var value = Get(Get(_preferences, definition.NativeGroup), definition.Property);
            switch (definition.Kind)
            {
                case SettingKind.Boolean: return new JValue(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                case SettingKind.Integer: return new JValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                case SettingKind.Color: return new JValue((long)unchecked((uint)Convert.ToInt64(value, CultureInfo.InvariantCulture)));
                default: return new JValue(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        public void Write(SettingDefinition definition, JToken value)
        {
            CheckDefinition(definition);
            if (!definition.Writable) throw new InvalidOperationException("read-only setting");
            object native = definition.Kind switch
            {
                SettingKind.Boolean => (object)value.Value<bool>(),
                SettingKind.Integer => value.Value<int>(),
                SettingKind.Color => checked((uint)value.Value<long>()),
                _ => throw new InvalidOperationException("text/path preferences require a dedicated validated operation")
            };
            var group = Get(_preferences, definition.NativeGroup);
            // Managed interop wrappers can expose enum property types. Preserve their
            // exact setter type; plain COM IDispatch accepts the underlying VT_I4.
            var property = group.GetType().GetProperty(definition.Property);
            if (property?.PropertyType.IsEnum == true)
                native = Enum.ToObject(property.PropertyType, native);
            Invoke(group, definition.Property, BindingFlags.SetProperty, new[] { native });
        }

        private static object Get(object target, string property) => Invoke(target, property, BindingFlags.GetProperty, null);

        private static object Invoke(object target, string property, BindingFlags flags, object[] arguments)
        {
            try { return target.GetType().InvokeMember(property, flags, null, target, arguments, CultureInfo.InvariantCulture); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static void CheckDefinition(SettingDefinition definition)
        {
            if (definition == null || !ReferenceEquals(AutoCadSettingsCatalog.Find(definition.Id), definition))
                throw new InvalidOperationException("setting is outside the fixed catalog");
        }
    }
}
