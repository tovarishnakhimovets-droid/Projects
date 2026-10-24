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
