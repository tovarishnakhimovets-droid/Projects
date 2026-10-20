using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Preferences;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class CrosshairSettingsTests
    {
        private static JObject Request() => new JObject { ["expected_profile"] = "<<C3D_Metric>>", ["crosshair_size"] = 100 };
        private static CrosshairSettingsChange Parse(JObject obj)
        {
            Assert.True(CrosshairSettingsInput.TryParse(obj, out var change, out var error), error);
            return change;
        }

        [Theory]
        [InlineData("crosshair_size", "0")]
        [InlineData("crosshair_size", "101")]
        [InlineData("crosshair_size", "100.0")]
        [InlineData("crosshair_size", "\"100\"")]
        [InlineData("crosshair_size", "null")]
        [InlineData("crosshair_size", "9999999999999999999999999999999999")]
        [InlineData("model_color_rgb", "[0,256,0]")]
        [InlineData("model_color_rgb", "[0,-1,0]")]
        [InlineData("model_color_rgb", "[0,255.0,0]")]
        [InlineData("model_color_rgb", "[0,255]")]
        [InlineData("layout_color_rgb", "[0,255,0,1]")]
        [InlineData("layout_color_rgb", "null")]
        [InlineData("dry_run", "1")]
        [InlineData("expected_profile", "\"\"")]
        [InlineData("security", "true")]
        public void Invalid_input_is_rejected_before_host_access(string name, string json)
        {
            var obj = Request(); obj[name] = JToken.Parse(json);
            Assert.False(CrosshairSettingsInput.TryParse(obj, out _, out _));
        }

        [Fact]
        public void Empty_change_is_not_a_write_request()
        {
            Assert.False(CrosshairSettingsInput.TryParse(new JObject { ["expected_profile"] = "P" }, out _, out _));
        }

        [Fact]
        public void Rgb_uses_ole_bgr_order_and_does_not_guess_system_colors()
        {
            var request = Request(); request["model_color_rgb"] = new JArray(255, 16, 32);
            var parsed = Parse(request);
            Assert.Equal(0x2010ff, parsed.ModelColor);
            Assert.Equal(new[] { 255, 16, 32 }, CrosshairSettingsSnapshot.Rgb(parsed.ModelColor.Value).Values<int>());
            Assert.Equal(JTokenType.Null, CrosshairSettingsSnapshot.Rgb(unchecked((int)0x80000005)).Type);
        }

        [Fact]
        public void Model_and_layout_are_applied_independently_with_verified_readback()
        {
            var native = new FakePreferences();
            var request = Request();
            request["model_color_rgb"] = new JArray(0, 255, 0);
            request["layout_color_rgb"] = new JArray(255, 0, 0);
            var result = CrosshairSettingsService.Apply(native, Parse(request));
            Assert.True(result.Ok, result.Error);
            Assert.Equal(100, native.State.Size);
            Assert.Equal(0x00ff00, native.State.ModelColor);
            Assert.Equal(0x0000ff, native.State.LayoutColor);
            var response = JObject.FromObject(result.Result);
            Assert.True(response.Value<bool>("verified"));
            Assert.False(response.Value<bool>("saved_dwg"));
            Assert.Equal(new[] { 255, 0, 0 }, response["after"]["layout_color_rgb"].Values<int>());
        }

        [Fact]
        public void One_context_change_preserves_size_and_other_context()
        {
            var native = new FakePreferences(); var before = native.State;
            var request = new JObject { ["expected_profile"] = before.Profile, ["model_color_rgb"] = new JArray(0, 255, 0) };
            Assert.True(CrosshairSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Equal(before.Size, native.State.Size);
            Assert.Equal(before.LayoutColor, native.State.LayoutColor);
            Assert.Equal(new[] { "model" }, native.Writes);
        }

        [Fact]
        public void Dry_run_and_repeated_target_do_not_call_native_setters()
        {
            var native = new FakePreferences(); var request = Request(); request["dry_run"] = true;
            Assert.True(CrosshairSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Empty(native.Writes);
            request["dry_run"] = false; request["crosshair_size"] = native.State.Size;
            Assert.True(CrosshairSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Empty(native.Writes);
        }

        [Fact]
        public void Stale_profile_is_rejected_without_any_setter()
        {
            var native = new FakePreferences(); var request = Request(); request["expected_profile"] = "other";
            Assert.False(CrosshairSettingsService.Apply(native, Parse(request)).Ok);
            Assert.Empty(native.Writes);
        }

        [Fact]
        public void Ignored_color_setter_is_a_failure_not_false_success()
        {
            var native = new FakePreferences { IgnoreModelWrite = true }; var before = native.State;
            var request = Request(); request["model_color_rgb"] = new JArray(0, 255, 0);
            var result = CrosshairSettingsService.Apply(native, Parse(request));
            Assert.False(result.Ok);
            Assert.Contains("readback", result.Error);
            Assert.Contains("rollback_verified=true", result.Error);
            Assert.True(native.State.SameAs(before));
        }

        [Fact]
        public void Setter_that_changes_then_throws_is_also_rolled_back()
        {
            var native = new FakePreferences { FailLayoutOnce = true }; var before = native.State;
            var request = Request(); request["model_color_rgb"] = new JArray(0, 255, 0); request["layout_color_rgb"] = new JArray(0, 255, 0);
            var result = CrosshairSettingsService.Apply(native, Parse(request));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=true", result.Error);
            Assert.True(native.State.SameAs(before));
            Assert.Equal(new[] { "size", "model", "layout", "layout", "model", "size" }, native.Writes);
        }

        [Fact]
        public void Failed_restoration_is_reported_as_unverified()
        {
            var native = new FakePreferences { FailLayoutOnce = true, FailModelRestore = true };
            var request = Request(); request["model_color_rgb"] = new JArray(0, 255, 0); request["layout_color_rgb"] = new JArray(0, 255, 0);
            var result = CrosshairSettingsService.Apply(native, Parse(request));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Contains("inspect dwg_get_crosshair_settings", result.Error);
        }

        [Fact]
        public void Profile_switch_inside_setter_never_writes_recovery_into_new_profile()
        {
            var native = new FakePreferences { ChangeProfileOnSize = true };
            var result = CrosshairSettingsService.Apply(native, Parse(Request()));
            Assert.False(result.Ok); Assert.Contains("rollback_verified=false", result.Error);
            Assert.Equal(new[] { "size" }, native.Writes);
        }

        [Fact]
        public void Actual_sdk_schema_exposes_typed_rgb_and_required_profile()
        {
            var getter = McpServerTool.Create(typeof(CrosshairQueryTools).GetMethod("GetCrosshairSettings"), (object)null).ProtocolTool;
            var setter = McpServerTool.Create(typeof(CrosshairWriteTools).GetMethod("SetCrosshairSettings"), (object)null).ProtocolTool;
            Assert.True(getter.Annotations.ReadOnlyHint);
            Assert.False(setter.Annotations.ReadOnlyHint);
            var schema = setter.InputSchema;
            Assert.Equal(new[] { "expected_profile" }, schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
            foreach (string name in new[] { "model_color_rgb", "layout_color_rgb" })
                Assert.Equal("integer", schema.GetProperty("properties").GetProperty(name).GetProperty("items").GetProperty("type").GetString());
        }

        [Fact]
        public async Task Invalid_server_request_returns_error_without_a_host_connection()
        {
            var result = JObject.Parse(await CrosshairWriteTools.SetCrosshairSettings("P", 101));
            Assert.False(result.Value<bool>("ok")); Assert.Contains("1 to 100", result.Value<string>("error"));
        }

        private sealed class FakePreferences : ICrosshairPreferences
        {
            public CrosshairSettingsSnapshot State = new CrosshairSettingsSnapshot("<<C3D_Metric>>", 5, 0xffffff, 0);
            public readonly List<string> Writes = new List<string>();
            public bool IgnoreModelWrite, FailLayoutOnce, FailModelRestore, ChangeProfileOnSize;
            public CrosshairSettingsSnapshot Read() => State;
            public void SetSize(int value)
            {
                Writes.Add("size");
                State = new CrosshairSettingsSnapshot(ChangeProfileOnSize ? "another-profile" : State.Profile, value, State.ModelColor, State.LayoutColor);
            }
            public void SetModelColor(int value)
            {
                Writes.Add("model");
                if (FailModelRestore && value == 0xffffff) throw new InvalidOperationException("model restore refused");
                if (!IgnoreModelWrite) State = new CrosshairSettingsSnapshot(State.Profile, State.Size, value, State.LayoutColor);
            }
            public void SetLayoutColor(int value)
            {
                Writes.Add("layout"); State = new CrosshairSettingsSnapshot(State.Profile, State.Size, State.ModelColor, value);
                if (FailLayoutOnce) { FailLayoutOnce = false; throw new InvalidOperationException("native setter failed after changing layout"); }
            }
        }
    }
}
