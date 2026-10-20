using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Preferences;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class GeneralSettingsTests
    {
        private const string Profile = "<<C3D_Metric>>";
        private static SettingDefinition Size => AutoCadSettingsCatalog.All.Single(d => d.Property == "CursorSize");
        private static SettingDefinition Flag => AutoCadSettingsCatalog.All.First(d => d.Writable && d.Kind == SettingKind.Boolean);
        private static SettingDefinition Color => AutoCadSettingsCatalog.All.First(d => d.Writable && d.Kind == SettingKind.Color);
        private static SettingDefinition ReadOnlyText => AutoCadSettingsCatalog.All.First(d => d.Kind == SettingKind.Text && !d.Writable);

        private static JObject Typed(string name, JToken value) => new JObject { [name] = value };
        private static JObject Integer(int value) => Typed("integer", value);
        private static JObject Boolean(bool value) => Typed("boolean", value);
        private static JObject Rgb(int red, int green, int blue) => Typed("rgb", new JArray(red, green, blue));
        private static JObject Ole(long value) => Typed("ole_color", value);
        private static JObject Item(SettingDefinition definition, JObject before, JObject value) => new JObject
        {
            ["id"] = definition.Id, ["expected_value"] = before, ["value"] = value
        };
        private static JObject Request(params JObject[] changes) => new JObject
        {
            ["expected_profile"] = Profile, ["changes"] = new JArray(changes)
        };
        private static JObject SizeRequest() => Request(Item(Size, Integer(5), Integer(100)));
        private static SettingsChangeRequest Parse(JObject obj)
        {
            Assert.True(GeneralSettingsInput.TryParse(obj, out var request, out var error), error);
            return request;
        }

        [Fact]
        public void Catalog_has_unique_ids_and_native_properties_and_consistent_lookup()
        {
            Assert.NotEmpty(AutoCadSettingsCatalog.All);
            Assert.Equal(AutoCadSettingsCatalog.All.Count, AutoCadSettingsCatalog.All.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(AutoCadSettingsCatalog.All.Count, AutoCadSettingsCatalog.All.Select(d => d.Group + "." + d.Property).Distinct(StringComparer.Ordinal).Count());
            foreach (var definition in AutoCadSettingsCatalog.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(definition.Id));
                Assert.False(string.IsNullOrWhiteSpace(definition.Group));
                Assert.False(string.IsNullOrWhiteSpace(definition.Property));
                Assert.Same(definition, AutoCadSettingsCatalog.Find(definition.Id));
            }
        }

        [Fact]
        public void Catalog_selection_is_bounded_explicit_and_keeps_requested_order()
        {
            Assert.Throws<ArgumentException>(() => AutoCadSettingsCatalog.Select(null));
            Assert.Throws<ArgumentException>(() => AutoCadSettingsCatalog.Select(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => AutoCadSettingsCatalog.Select(new[] { Size.Id, "not.a.native.setting" }));
            Assert.Throws<ArgumentException>(() => AutoCadSettingsCatalog.Select(new[] { Size.Id, Size.Id }));
            Assert.Throws<ArgumentException>(() => AutoCadSettingsCatalog.Select(Enumerable.Repeat(Size.Id, 33).ToArray()));
            Assert.Equal(new[] { Color.Id, Size.Id }, AutoCadSettingsCatalog.Select(new[] { Color.Id, Size.Id }).Select(d => d.Id));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("101")]
        [InlineData("100.0")]
        [InlineData("\"100\"")]
        [InlineData("true")]
        [InlineData("null")]
        [InlineData("99999999999999999999999999999999")]
        public void Integer_settings_reject_out_of_range_or_untyped_numbers(string json)
        {
            var request = SizeRequest();
            request["changes"][0]["value"]["integer"] = JToken.Parse(json);
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("\"true\"")]
        [InlineData("null")]
        public void Boolean_settings_require_json_booleans(string json)
        {
            var request = Request(Item(Flag, Boolean(false), Typed("boolean", JToken.Parse(json))));
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("[0,256,0]")]
        [InlineData("[0,-1,0]")]
        [InlineData("[0,255.0,0]")]
        [InlineData("[0,255]")]
        [InlineData("[0,255,0,1]")]
        [InlineData("[0,\"255\",0]")]
        [InlineData("[99999999999999999999999999999999,0,0]")]
        [InlineData("null")]
        public void Color_targets_require_exact_integer_rgb_triples(string json)
        {
            var request = Request(Item(Color, Ole(0xffffff), Typed("rgb", JToken.Parse(json))));
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void Color_expected_values_preserve_ole_system_flags_and_targets_use_bgr_native_order()
        {
            var parsed = Parse(Request(Item(Color, Ole(0x80000005L), Rgb(255, 16, 32))));
            Assert.Equal(0x80000005L, parsed.Changes.Single().ExpectedValue.Value<long>());
            Assert.Equal(0x2010ffL, parsed.Changes.Single().Value.Value<long>());
            var rgbExpectation = Parse(Request(Item(Color, Rgb(255, 16, 32), Rgb(0, 255, 0))));
            Assert.Equal(0x2010ffL, rgbExpectation.Changes.Single().ExpectedValue.Value<long>());
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("4294967296")]
        [InlineData("1.0")]
        [InlineData("\"1\"")]
        [InlineData("99999999999999999999999999999999")]
        public void Ole_expected_color_requires_an_unsigned_32_bit_integer(string json)
        {
            var request = Request(Item(Color, Typed("ole_color", JToken.Parse(json)), Rgb(0, 255, 0)));
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void Color_target_cannot_assign_a_raw_ole_color_or_system_color_flag()
        {
            Assert.False(GeneralSettingsInput.TryParse(Request(Item(Color, Ole(0), Ole(0x80000005L))), out _, out _));
        }

        [Fact]
        public void Integer_enum_targets_use_catalog_members_and_expectations_preserve_unknown_native_values()
        {
            var definitions = AutoCadSettingsCatalog.All.Where(d => d.Writable && d.Kind == SettingKind.Integer && d.Allowed != null).ToArray();
            Assert.NotEmpty(definitions);
            foreach (var definition in definitions)
            {
                foreach (var allowed in definition.Allowed)
                    Assert.True(GeneralSettingsInput.TryParse(Request(Item(definition, Integer(99999), Integer(allowed))), out _, out var error), error);
                int excluded = Enumerable.Range(-1, 100).First(value => !definition.Allowed.Contains(value));
                Assert.False(GeneralSettingsInput.TryParse(Request(Item(definition, Integer(0), Integer(excluded))), out _, out _));
            }
            Assert.True(GeneralSettingsInput.TryParse(Request(Item(Size, Integer(1000), Integer(100))), out _, out _));
        }

        [Fact]
        public void Lossy_bitmask_facades_paths_profile_switch_and_reset_operations_stay_readonly()
        {
            foreach (var definition in AutoCadSettingsCatalog.All.Where(d => d.Kind == SettingKind.Text ||
                new[] { "AutoSnapMarker", "AutoSnapMagnet", "AutoSnapTooltip", "PolarTrackingVector", "FullScreenTrackingVector",
                    "AutoTrackTooltip", "PickAdd", "PickAuto", "PickDrag", "DisplayGrips", "PickGroup", "ShowWarningMessages" }.Contains(d.Property)))
            {
                Assert.False(definition.Writable);
                Assert.False(string.IsNullOrWhiteSpace(definition.Restriction));
            }
        }

        [Fact]
        public void Each_typed_value_has_exactly_one_known_component_and_matches_the_setting_kind()
        {
            foreach (var value in new JToken[]
            {
                new JObject(), new JObject { ["integer"] = 100, ["boolean"] = true },
                new JObject { ["integer"] = 100, ["future"] = true }, Boolean(true), new JValue(100), JValue.CreateNull()
            })
            {
                var request = SizeRequest(); request["changes"][0]["value"] = value;
                Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
            }
        }

        [Fact]
        public void Change_requests_are_strict_and_never_accept_unknown_or_readonly_settings()
        {
            var invalid = new List<JObject>();
            var unknownRoot = SizeRequest(); unknownRoot["security"] = true; invalid.Add(unknownRoot);
            var unknownItem = SizeRequest(); unknownItem["changes"][0]["force"] = true; invalid.Add(unknownItem);
            var missingExpected = SizeRequest(); ((JObject)missingExpected["changes"][0]).Remove("expected_value"); invalid.Add(missingExpected);
            var unknownId = SizeRequest(); unknownId["changes"][0]["id"] = "security.trusted_paths"; invalid.Add(unknownId);
            var duplicate = SizeRequest(); ((JArray)duplicate["changes"]).Add(duplicate["changes"][0].DeepClone()); invalid.Add(duplicate);
            var empty = SizeRequest(); empty["changes"] = new JArray(); invalid.Add(empty);
            var oversized = SizeRequest(); oversized["changes"] = new JArray(Enumerable.Range(0, 33).Select(_ => oversized["changes"][0].DeepClone())); invalid.Add(oversized);
            invalid.Add(Request(Item(ReadOnlyText, Typed("text", "before"), Typed("text", "after"))));
            foreach (var request in invalid) Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
            Assert.False(GeneralSettingsInput.TryParse(new JArray(), out _, out _));
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("\"   \"")]
        [InlineData("null")]
        [InlineData("123")]
        public void Exact_nonempty_profile_is_required(string json)
        {
            var request = SizeRequest(); request["expected_profile"] = JToken.Parse(json);
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void Profile_length_and_dry_run_are_validated_without_coercion()
        {
            var request = SizeRequest(); request["expected_profile"] = new string('P', 257);
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
            request = SizeRequest(); request["dry_run"] = 1;
            Assert.False(GeneralSettingsInput.TryParse(request, out _, out _));
            request["dry_run"] = true; Assert.True(GeneralSettingsInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void Multiple_native_settings_apply_and_unrequested_values_are_preserved()
        {
            var native = new FakeSettings();
            var request = Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)));
            var result = GeneralSettingsService.Apply(native, Parse(request));
            Assert.True(result.Ok, result.Error);
            Assert.Equal(100, native.Values[Size.Id].Value<int>());
            Assert.True(native.Values[Flag.Id].Value<bool>());
            Assert.Equal(0xffffffL, native.Values[Color.Id].Value<long>());
            Assert.Equal(new[] { Size.Id, Flag.Id }, native.Writes);
        }

        [Fact]
        public void Dry_run_and_already_applied_targets_never_call_a_native_setter()
        {
            var native = new FakeSettings(); var request = SizeRequest(); request["dry_run"] = true;
            var result = GeneralSettingsService.Apply(native, Parse(request));
            Assert.True(result.Ok, result.Error); Assert.Empty(native.Writes);
            Assert.Equal(5, native.Values[Size.Id].Value<int>());
            request = Request(Item(Size, Integer(5), Integer(5)));
            result = GeneralSettingsService.Apply(native, Parse(request));
            Assert.True(result.Ok, result.Error); Assert.Empty(native.Writes);
        }

        [Fact]
        public void A_stale_second_expected_value_prevents_the_first_write_too()
        {
            var native = new FakeSettings();
            var request = Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(true), Boolean(false)));
            var result = GeneralSettingsService.Apply(native, Parse(request));
            Assert.False(result.Ok); Assert.Empty(native.Writes);
            Assert.Equal(5, native.Values[Size.Id].Value<int>());
        }

        [Fact]
        public void Dry_run_still_rejects_stale_expected_values()
        {
            var native = new FakeSettings(); var request = Request(Item(Size, Integer(4), Integer(100))); request["dry_run"] = true;
            Assert.False(GeneralSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Empty(native.Writes);
        }

        [Fact]
        public void A_different_profile_or_failed_preflight_read_prevents_all_writes()
        {
            var native = new FakeSettings { Profile = "another-profile" };
            Assert.False(GeneralSettingsService.Apply(native, Parse(SizeRequest())).Ok);
            Assert.Empty(native.Writes);
            native = new FakeSettings { FailReadId = Flag.Id };
            var request = Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)));
            Assert.False(GeneralSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Empty(native.Writes);
        }

        [Fact]
        public void An_ignored_native_write_is_failure_and_recovery_is_verified()
        {
            var native = new FakeSettings { IgnoreWriteId = Flag.Id };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=true", result.Error);
            Assert.Equal(5, native.Values[Size.Id].Value<int>()); Assert.False(native.Values[Flag.Id].Value<bool>());
        }

        [Fact]
        public void A_setter_that_changes_then_throws_is_restored_before_earlier_writes()
        {
            var native = new FakeSettings { ThrowAfterWriteId = Flag.Id };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=true", result.Error);
            Assert.Equal(new[] { Size.Id, Flag.Id, Flag.Id, Size.Id }, native.Writes);
            Assert.Equal(5, native.Values[Size.Id].Value<int>()); Assert.False(native.Values[Flag.Id].Value<bool>());
        }

        [Fact]
        public void Failed_recovery_reports_uncertainty_and_keeps_other_recovery_attempts()
        {
            var native = new FakeSettings { ThrowAfterWriteId = Flag.Id, FailRestoreId = Flag.Id };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Equal(5, native.Values[Size.Id].Value<int>()); Assert.True(native.Values[Flag.Id].Value<bool>());
            Assert.Equal(new[] { Size.Id, Flag.Id, Flag.Id, Size.Id }, native.Writes);
        }

        [Fact]
        public void Native_readback_failure_attempts_recovery_instead_of_claiming_success()
        {
            var native = new FakeSettings { FailReadOnceAfterWrite = true };
            var result = GeneralSettingsService.Apply(native, Parse(SizeRequest()));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=true", result.Error);
            Assert.Equal(new[] { Size.Id, Size.Id }, native.Writes);
            Assert.Equal(5, native.Values[Size.Id].Value<int>());
        }

        [Fact]
        public void A_profile_switch_after_a_setter_blocks_later_writes_and_foreign_profile_recovery()
        {
            var native = new FakeSettings { SwitchProfileAfterWriteId = Size.Id };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Equal(new[] { Size.Id }, native.Writes);
        }

        [Fact]
        public void A_native_side_effect_on_a_later_setting_is_detected_before_its_setter()
        {
            var native = new FakeSettings { ChangeFlagAfterSizeWrite = true };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(true)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Equal(new[] { Size.Id, Size.Id }, native.Writes);
            Assert.Equal(5, native.Values[Size.Id].Value<int>());
            Assert.True(native.Values[Flag.Id].Value<bool>());
        }

        [Fact]
        public void Native_readback_checks_requested_noops_for_setter_side_effects_too()
        {
            var native = new FakeSettings { ChangeFlagAfterSizeWrite = true };
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(Size, Integer(5), Integer(100)), Item(Flag, Boolean(false), Boolean(false)))));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Equal(new[] { Size.Id, Size.Id }, native.Writes);
        }

        [Fact]
        public void Read_returns_per_property_availability_without_guessing_values_or_writing()
        {
            var native = new FakeSettings { FailReadId = Flag.Id };
            var result = GeneralSettingsService.Read(native, new[] { Size.Id, Flag.Id, Color.Id });
            Assert.True(result.Ok, result.Error); Assert.Empty(native.Writes);
            var response = JObject.FromObject(result.Result);
            var settings = (JArray)response["settings"];
            Assert.Equal(Profile, response.Value<string>("profile"));
            Assert.False(response.Value<bool>("saved_dwg"));
            Assert.Equal(new[] { Size.Id, Flag.Id, Color.Id }, settings.Select(v => v.Value<string>("id")));
            Assert.True(settings[0].Value<bool>("available"));
            Assert.Equal(5, settings[0]["value"].Value<int>("integer"));
            Assert.False(settings[1].Value<bool>("available"));
            Assert.Null(settings[1]["value"]);
            Assert.NotNull(settings[1]["error"]);
            Assert.True(settings[2].Value<bool>("available"));
        }

        [Fact]
        public void Read_preserves_raw_system_colors_without_inventing_rgb()
        {
            var native = new FakeSettings(); native.Values[Color.Id] = new JValue(0x80000005L);
            var result = GeneralSettingsService.Read(native, new[] { Color.Id });
            Assert.True(result.Ok, result.Error);
            var setting = JObject.FromObject(result.Result)["settings"][0];
            Assert.Equal(0x80000005L, setting["value"].Value<long>("ole_color"));
            Assert.Equal(JTokenType.Null, setting["rgb"].Type);
        }

        [Fact]
        public void Invalid_read_selection_is_rejected_before_native_property_reads()
        {
            var native = new FakeSettings();
            Assert.False(GeneralSettingsService.Read(native, new[] { Size.Id, "not.a.setting" }).Ok);
            Assert.Empty(native.Reads); Assert.Empty(native.Writes);
        }

        [Fact]
        public void A_profile_switch_during_read_prevents_a_mixed_profile_snapshot()
        {
            var native = new FakeSettings { SwitchProfileAfterReadId = Size.Id };
            var result = GeneralSettingsService.Read(native, new[] { Size.Id, Flag.Id });
            Assert.False(result.Ok); Assert.Empty(native.Writes);
            Assert.Equal(new[] { Size.Id }, native.Reads);
        }

        [Fact]
        public void Non_immediate_effect_is_reported_separately_from_stored_value_verification()
        {
            var definition = AutoCadSettingsCatalog.All.Single(d => d.Property == "LayoutCreateViewport");
            var native = new FakeSettings();
            var result = GeneralSettingsService.Apply(native, Parse(Request(Item(definition, Boolean(false), Boolean(true)))));
            Assert.True(result.Ok, result.Error);
            var response = JObject.FromObject(result.Result);
            Assert.Equal("stored_native_values", response.Value<string>("verification"));
            Assert.Equal(definition.Effect, response["settings"][0].Value<string>("effect"));
            Assert.NotEqual("immediate", definition.Effect);
        }

        [Fact]
        public void Actual_SDK_schemas_require_explicit_ids_profile_and_every_nested_change_field()
        {
            var list = McpServerTool.Create(typeof(GeneralSettingsQueryTools).GetMethod(nameof(GeneralSettingsQueryTools.ListSettings)), (object)null).ProtocolTool;
            var getter = McpServerTool.Create(typeof(GeneralSettingsQueryTools).GetMethod(nameof(GeneralSettingsQueryTools.GetSettings)), (object)null).ProtocolTool;
            var setter = McpServerTool.Create(typeof(GeneralSettingsWriteTools).GetMethod(nameof(GeneralSettingsWriteTools.SetSettings)), (object)null).ProtocolTool;
            Assert.True(list.Annotations.ReadOnlyHint); Assert.True(getter.Annotations.ReadOnlyHint);
            Assert.False(setter.Annotations.ReadOnlyHint); Assert.False(setter.Annotations.IdempotentHint);
            Assert.Equal(new[] { "ids" }, Required(getter.InputSchema));
            AssertType(getter.InputSchema.GetProperty("properties").GetProperty("ids"), "array");
            AssertType(getter.InputSchema.GetProperty("properties").GetProperty("ids").GetProperty("items"), "string");
            Assert.Equal(new[] { "changes", "expected_profile" }, Required(setter.InputSchema).OrderBy(x => x));
            AssertType(setter.InputSchema.GetProperty("properties").GetProperty("dry_run"), "boolean");
            var changes = Resolve(setter.InputSchema, setter.InputSchema.GetProperty("properties").GetProperty("changes").GetProperty("items"));
            AssertType(changes, "object");
            Assert.Equal(new[] { "expected_value", "id", "value" }, Required(changes).OrderBy(x => x));
            AssertType(changes.GetProperty("properties").GetProperty("id"), "string");
            foreach (var name in new[] { "expected_value", "value" })
            {
                var value = Resolve(setter.InputSchema, changes.GetProperty("properties").GetProperty(name));
                AssertType(value, "object");
                var fields = value.GetProperty("properties");
                AssertType(Resolve(setter.InputSchema, fields.GetProperty("boolean")), "boolean");
                AssertType(Resolve(setter.InputSchema, fields.GetProperty("integer")), "integer");
                AssertType(Resolve(setter.InputSchema, fields.GetProperty("text")), "string");
                AssertType(Resolve(setter.InputSchema, fields.GetProperty("ole_color")), "integer");
                var rgb = Resolve(setter.InputSchema, fields.GetProperty("rgb"));
                AssertType(rgb, "array");
                AssertType(Resolve(setter.InputSchema, rgb.GetProperty("items")), "integer");
            }
        }

        [Fact]
        public void SDK_DTO_binding_requires_nested_fields_and_roundtrips_typed_plugin_input()
        {
            var request = SizeRequest();
            var changes = System.Text.Json.JsonSerializer.Deserialize<SettingChange[]>(request["changes"].ToString());
            request["changes"] = JToken.Parse(System.Text.Json.JsonSerializer.Serialize(changes));
            Assert.True(GeneralSettingsInput.TryParse(request, out _, out var error), error);
            Assert.Throws<JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<SettingChange>("{\"id\":\"x\",\"value\":{\"integer\":1}}"));
            Assert.Throws<JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<SettingChange>("{\"id\":\"x\",\"expected_value\":{\"integer\":1}}"));
            Assert.Throws<JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<SettingChange>("{\"expected_value\":{\"integer\":1},\"value\":{\"integer\":2}}"));
            Assert.Throws<JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<SettingValue>("{\"integer\":1,\"force\":true}"));
            Assert.Throws<JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<SettingChange>("{\"id\":\"x\",\"expected_value\":{\"integer\":1},\"value\":{\"integer\":2},\"force\":true}"));
        }

        [Fact]
        public async Task Catalog_and_invalid_tool_requests_do_not_require_a_host_connection()
        {
            var catalog = JObject.Parse(await GeneralSettingsQueryTools.ListSettings("display"));
            Assert.True(catalog.Value<bool>("ok"));
            Assert.All(catalog["result"]["settings"], value => Assert.Equal("display", value.Value<string>("group")));
            Assert.False(JObject.Parse(await GeneralSettingsQueryTools.ListSettings("arbitrary.native.property")).Value<bool>("ok"));
            Assert.False(JObject.Parse(await GeneralSettingsQueryTools.GetSettings(Array.Empty<string>())).Value<bool>("ok"));
            Assert.False(JObject.Parse(await GeneralSettingsQueryTools.GetSettings(new[] { "unknown.id" })).Value<bool>("ok"));
            var changes = new[] { new SettingChange { id = Size.Id, expected_value = new SettingValue { integer = 5 }, value = new SettingValue { integer = 101 } } };
            Assert.False(JObject.Parse(await GeneralSettingsWriteTools.SetSettings(Profile, changes)).Value<bool>("ok"));
            Assert.False(JObject.Parse(await GeneralSettingsWriteTools.SetSettings(Profile, null)).Value<bool>("ok"));
        }

        private static IEnumerable<string> Required(JsonElement schema) => schema.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        private static JsonElement Resolve(JsonElement root, JsonElement schema)
        {
            if (!schema.TryGetProperty("$ref", out var reference)) return schema;
            var resolved = root;
            foreach (var component in reference.GetString().Split('/').Skip(1))
                resolved = resolved.GetProperty(component.Replace("~1", "/").Replace("~0", "~"));
            return resolved;
        }
        private static void AssertType(JsonElement schema, string expected)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String) Assert.Equal(expected, type.GetString());
            else Assert.Contains(type.EnumerateArray(), item => item.GetString() == expected);
        }

        private sealed class FakeSettings : IAutoCadSettings
        {
            public string Profile { get; set; } = GeneralSettingsTests.Profile;
            public readonly Dictionary<string, JToken> Values = new Dictionary<string, JToken>(StringComparer.Ordinal);
            private readonly Dictionary<string, JToken> _original = new Dictionary<string, JToken>(StringComparer.Ordinal);
            public readonly List<string> Writes = new List<string>();
            public readonly List<string> Reads = new List<string>();
            public string IgnoreWriteId, ThrowAfterWriteId, FailRestoreId, SwitchProfileAfterWriteId, SwitchProfileAfterReadId, FailReadId;
            public bool FailReadOnceAfterWrite, ChangeFlagAfterSizeWrite;
            private bool _writeThrew;

            public FakeSettings()
            {
                foreach (var definition in AutoCadSettingsCatalog.All)
                {
                    JToken value = definition.Kind == SettingKind.Boolean ? new JValue(false) :
                        definition.Kind == SettingKind.Color ? new JValue(0xffffffL) :
                        definition.Kind == SettingKind.Integer ? new JValue(5) : new JValue("read-only-value");
                    Values.Add(definition.Id, value); _original.Add(definition.Id, value.DeepClone());
                }
            }

            public JToken Read(SettingDefinition definition)
            {
                Reads.Add(definition.Id);
                if (definition.Id == FailReadId) throw new InvalidOperationException("native property is unsupported");
                if (FailReadOnceAfterWrite && Writes.Count > 0)
                {
                    FailReadOnceAfterWrite = false;
                    throw new InvalidOperationException("native readback failed once");
                }
                if (definition.Id == SwitchProfileAfterReadId) Profile = "another-profile";
                return Values[definition.Id].DeepClone();
            }

            public void Write(SettingDefinition definition, JToken value)
            {
                Writes.Add(definition.Id);
                if (definition.Id == FailRestoreId && JToken.DeepEquals(value, _original[definition.Id]))
                    throw new InvalidOperationException("native restoration was refused");
                if (definition.Id != IgnoreWriteId) Values[definition.Id] = value.DeepClone();
                if (ChangeFlagAfterSizeWrite && definition.Id == Size.Id && value.Value<int>() == 100)
                    Values[Flag.Id] = new JValue(true);
                if (definition.Id == SwitchProfileAfterWriteId) Profile = "another-profile";
                if (definition.Id == ThrowAfterWriteId && !_writeThrew)
                {
                    _writeThrew = true;
                    throw new InvalidOperationException("native setter changed the value then failed");
                }
            }
        }
    }
}
