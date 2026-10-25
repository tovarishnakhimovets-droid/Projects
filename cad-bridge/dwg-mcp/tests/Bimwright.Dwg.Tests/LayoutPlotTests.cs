using System;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class LayoutPlotTests
    {
        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("bad\nname")]
        public void Layout_name_rejects_blank_or_control_text(string name)
            => Assert.NotNull(LayoutPlotPolicy.ValidateLayoutName(name));

        [Fact]
        public void Existing_unicode_layout_names_are_accepted()
            => Assert.Null(LayoutPlotPolicy.ValidateLayoutName("Стройгенплан"));

        [Theory]
        [InlineData("Hatch", 1)] [InlineData("BlockReference", 0)] [InlineData(null, 1001)]
        public void Entity_query_rejects_unsupported_filter_and_unbounded_limits(string type, int limit)
            => Assert.NotNull(LayoutPlotPolicy.ValidateEntityFilter(type, limit));

        [Theory]
        [InlineData(null)] [InlineData("DBText")] [InlineData("MText")] [InlineData("BlockReference")] [InlineData("Line")] [InlineData("Polyline")]
        public void Entity_query_accepts_only_bounded_direct_types(string type)
            => Assert.Null(LayoutPlotPolicy.ValidateEntityFilter(type, 1000));

        [Fact]
        public void Plot_snapshot_requires_all_original_settings_without_changes()
        {
            var actual = JObject.Parse("{\"device\":\"DWG To PDF.pc3\",\"scale\":500,\"window\":[1,2,3,4]}");
            Assert.Null(LayoutPlotPolicy.ValidateSnapshot((JObject)actual.DeepClone(), actual));
            var changed = (JObject)actual.DeepClone(); changed["scale"] = 1;
            Assert.NotNull(LayoutPlotPolicy.ValidateSnapshot(changed, actual));
            Assert.NotNull(LayoutPlotPolicy.ValidateSnapshot(new JObject(), actual));
        }

        [Theory]
        [InlineData("DWG To PDF.pc3", true)] [InlineData("AutoCAD PDF (High Quality Print).pc3", true)]
        [InlineData("Microsoft Print to PDF", true)] [InlineData("HP plotter.pc3", false)] [InlineData(null, false)]
        public void Only_saved_PDF_device_is_accepted(string device, bool expected)
            => Assert.Equal(expected, LayoutPlotPolicy.IsPdfDevice(device));

        [Fact]
        public void Saved_device_and_canonical_media_require_exact_available_names()
        {
            Assert.Null(LayoutPlotPolicy.ValidateSavedDevice("DWG To PDF.pc3", new[] { "DWG To PDF.pc3" }));
            Assert.NotNull(LayoutPlotPolicy.ValidateSavedDevice("DWG To PDF.pc3", new[] { "Other PDF.pc3" }));
            Assert.NotNull(LayoutPlotPolicy.ValidateSavedDevice("DWG To PDF.pc3", null));
            Assert.Null(LayoutPlotPolicy.ValidateSavedMedia("ISO_full_bleed_A0", new[] { "ISO_full_bleed_A0" }));
            Assert.NotNull(LayoutPlotPolicy.ValidateSavedMedia("ISO_full_bleed_A0", new[] { "iso_full_bleed_A0" }));
            Assert.NotNull(LayoutPlotPolicy.ValidateSavedMedia("ISO_full_bleed_A0", new[] { "ISO_A0" }));
        }

        [Theory]
        [InlineData("device")] [InlineData("media")] [InlineData("style_sheet")] [InlineData("paper_units")]
        [InlineData("rotation")] [InlineData("plot_type")] [InlineData("paper_size")] [InlineData("origin")]
        [InlineData("window")] [InlineData("use_standard_scale")] [InlineData("standard_scale")]
        [InlineData("custom_scale")] [InlineData("centered")] [InlineData("plot_with_styles")]
        [InlineData("print_lineweights")] [InlineData("scale_lineweights")]
        public void Prepared_and_validated_settings_must_never_substitute_any_saved_value(string field)
        {
            var saved = JObject.Parse("{\"device\":\"PDF.pc3\",\"media\":\"A0\",\"style_sheet\":\"monochrome.ctb\",\"paper_units\":\"Millimeters\",\"rotation\":\"Degrees090\",\"plot_type\":\"Window\",\"paper_size\":[841.0,1189.0],\"origin\":[0.0,0.0],\"window\":[-300.0,-254.0,888.0,586.0],\"use_standard_scale\":false,\"standard_scale\":\"Scale1To1\",\"custom_scale\":[1.0,1.0],\"centered\":false,\"plot_with_styles\":true,\"print_lineweights\":true,\"scale_lineweights\":false}");
            Assert.Null(LayoutPlotPolicy.ValidateUnchangedSettings(saved, (JObject)saved.DeepClone()));
            var changed = (JObject)saved.DeepClone(); changed[field] = "substituted";
            string error = LayoutPlotPolicy.ValidateUnchangedSettings(saved, changed);
            Assert.Contains(field, error);
            Assert.NotNull(LayoutPlotPolicy.ValidateUnchangedSettings(saved, new JObject()));
        }

        [Fact]
        public void Foreground_scope_restores_previous_background_value_after_success()
        {
            int value = 2;
            using (new ForegroundPlotScope(() => value, v => value = v)) Assert.Equal(0, value);
            Assert.Equal(2, value);
        }

        [Fact]
        public void Foreground_scope_restores_previous_background_value_after_plot_exception()
        {
            int value = 3;
            Action failingPlot = () =>
            {
                using var scope = new ForegroundPlotScope(() => value, v => value = v);
                throw new InvalidOperationException("plot failure");
            };
            Assert.Throws<InvalidOperationException>(failingPlot);
            Assert.Equal(3, value);
        }

        [Fact]
        public void Foreground_scope_refuses_an_unverified_setter_and_reports_failed_restore()
        {
            int value = 2;
            Assert.Throws<InvalidOperationException>(() => new ForegroundPlotScope(() => value, _ => { }));
            Assert.Equal(2, value);
            var scope = new ForegroundPlotScope(() => value, v => { if (v == 0) value = 0; });
            var error = Assert.Throws<InvalidOperationException>(() => scope.Dispose());
            Assert.Contains("restoration", error.Message);
        }

        [Fact]
        public void Output_must_have_the_PDF_magic_header()
        {
            Assert.True(LayoutPlotPolicy.HasPdfHeader(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7")));
            Assert.False(LayoutPlotPolicy.HasPdfHeader(System.Text.Encoding.ASCII.GetBytes("hello")));
            Assert.False(LayoutPlotPolicy.HasPdfHeader(new byte[4]));
        }

        [Theory]
        [InlineData(typeof(DrawingTools), nameof(DrawingTools.ListLayouts), true, "expected_document,expected_fingerprint")]
        [InlineData(typeof(DrawingTools), nameof(DrawingTools.GetLayoutEntities), true, "expected_document,expected_fingerprint,layout_name")]
        [InlineData(typeof(DrawingWriteTools), nameof(DrawingWriteTools.SelectLayout), false, "expected_document,expected_fingerprint,layout_name,expected_current_layout")]
        [InlineData(typeof(DrawingWriteTools), nameof(DrawingWriteTools.PlotPdf), false, "expected_document,expected_fingerprint,layout_name,expected_plot_settings,output_path")]
        public void Actual_MCP_schema_has_correct_effects_and_required_guards(Type type, string method, bool readOnly, string required)
        {
            var t = McpServerTool.Create(type.GetMethod(method), (object)null).ProtocolTool;
            Assert.Equal(readOnly, t.Annotations.ReadOnlyHint);
            Assert.Equal(required.Split(',').OrderBy(x => x), t.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x));
        }

        [Fact]
        public void Plot_settings_JSON_string_has_string_MCP_schema_and_object_native_payload()
        {
            var tool = McpServerTool.Create(typeof(DrawingWriteTools).GetMethod(nameof(DrawingWriteTools.PlotPdf)), (object)null).ProtocolTool;
            Assert.Equal("string", tool.InputSchema.GetProperty("properties").GetProperty("expected_plot_settings").GetProperty("type").GetString());
            Assert.True(DrawingWriteTools.TryParsePlotSettings("{\"device\":\"DWG To PDF.pc3\",\"paper_size\":[420,297],\"custom_scale\":[1,500]}", out var parsed, out var error), error);
            var native = new JObject { ["device"] = "DWG To PDF.pc3", ["paper_size"] = new JArray(420d, 297d), ["custom_scale"] = new JArray(1d, 500d) };
            Assert.Null(LayoutPlotPolicy.ValidateSnapshot(parsed, native));
            var wire = JObject.Parse(parsed.ToString());
            Assert.Equal(JTokenType.Object, wire.Type);
            Assert.Null(LayoutPlotPolicy.ValidateSnapshot(wire, native));
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("[]")] [InlineData("null")] [InlineData("true")]
        [InlineData("{bad}")] [InlineData("{} {}")]
        [InlineData("{\"device\":\"one\",\"device\":\"two\"}")]
        public async System.Threading.Tasks.Task Invalid_plot_settings_are_rejected_before_contacting_CAD(string json)
        {
            var response = JObject.Parse(await DrawingWriteTools.PlotPdf(@"D:\a.dwg", "fingerprint", "Лист1", json, @"D:\a.pdf"));
            Assert.False(response.Value<bool>("ok"));
            Assert.Contains("expected_plot_settings", response.Value<string>("error"));
        }

        [Fact]
        public void Oversized_or_excessively_nested_settings_are_rejected_locally()
        {
            Assert.False(DrawingWriteTools.TryParsePlotSettings(new string('x', 65537), out _, out _));
            string deep = string.Concat(System.Linq.Enumerable.Repeat("{\"a\":", 18)) + "{}" + new string('}', 18);
            Assert.False(DrawingWriteTools.TryParsePlotSettings(deep, out _, out _));
        }

        [Fact]
        public void Plugin_schema_refuses_missing_identity_and_stale_expected_settings_shape()
        {
            var p = JObject.Parse("{\"expected_document\":\"D:\\\\a.dwg\",\"expected_fingerprint\":\"a\",\"layout_name\":\"Лист1\",\"expected_plot_settings\":{},\"output_path\":\"D:\\\\a.pdf\"}");
            Assert.True(SchemaValidator.Validate("plot_pdf", p, CommandSchemas.PlotPdf).Ok);
            p["expected_plot_settings"] = "any";
            Assert.False(SchemaValidator.Validate("plot_pdf", p, CommandSchemas.PlotPdf).Ok);
            p["expected_plot_settings"] = new JObject(); p.Remove("expected_fingerprint");
            Assert.False(SchemaValidator.Validate("plot_pdf", p, CommandSchemas.PlotPdf).Ok);
        }
    }
}
