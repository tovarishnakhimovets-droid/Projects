using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class MTextCreationToolTests
    {
        [Fact]
        public void Mcp_schema_preserves_legacy_arguments_and_exposes_existing_style()
        {
            var tool = McpServerTool.Create(typeof(TextCreationTools).GetMethod(nameof(TextCreationTools.CreateMText)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_create_mtext", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "location", "text" }, tool.InputSchema.GetProperty("required")
                .EnumerateArray().Select(v => v.GetString()).OrderBy(v => v, StringComparer.Ordinal));
            Assert.Contains("string", tool.InputSchema.GetProperty("properties")
                .GetProperty("text_style").GetProperty("type").ToString());
            Assert.Null(typeof(AnnotationTools).GetMethod(nameof(AnnotationTools.CreateMText))
                .GetCustomAttribute<McpServerToolAttribute>());
            Assert.Single(typeof(TextCreationTools).GetMethods().Concat(typeof(AnnotationTools).GetMethods())
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
                .Where(a => a?.Name == "dwg_create_mtext"));
        }

        [Fact]
        public void Parser_preserves_multiline_native_formatting_style_and_no_wrap()
        {
            var request = JObject.Parse("{\"text\":\"A\\nB\\\\P{\\\\C1;Т1}\",\"location\":{\"x\":12,\"y\":34},\"width\":0,\"text_style\":\"GOST TYPE A\"}");
            Assert.True(AnnotationRequestValidation.TryReadMText(request, out var input, out var error), error);
            Assert.Equal(request.Value<string>("text"), input.Appearance.Text);
            Assert.Equal(12, input.Appearance.X);
            Assert.Equal(34, input.Appearance.Y);
            Assert.Equal(0, input.Appearance.Z);
            Assert.Equal(2.5, input.Appearance.Height);
            Assert.Equal(0, input.Appearance.RotationDegrees);
            Assert.Equal(256, input.Appearance.ColorIndex);
            Assert.Equal("GOST TYPE A", input.Appearance.TextStyle);
            Assert.Equal(0, input.Width);
            Assert.True(SchemaValidator.Validate("create_mtext", request, CommandSchemas.CreateMText).Ok);
        }

        [Fact]
        public void Wire_request_keeps_explicit_appearance_and_WCS_location()
        {
            Assert.True(TextCreationTools.TryBuildMTextRequest("Т1\\PТ2", "{\"x\":1,\"y\":2,\"z\":3}",
                30, 4, 90, "TEXT", 30, "GOST TYPE A", out var request, out var error), error);
            Assert.Equal(30, request.Value<double>("width"));
            Assert.Equal(4, request.Value<double>("height"));
            Assert.Equal(90, request.Value<double>("rotation"));
            Assert.Equal(30, request.Value<int>("color_index"));
            Assert.Equal(3, request["location"].Value<double>("z"));
            Assert.Equal("GOST TYPE A", request.Value<string>("text_style"));
        }

        [Theory]
        [InlineData("negative_width", "width")]
        [InlineData("nan_width", "width")]
        [InlineData("infinite_height", "height")]
        [InlineData("infinite_rotation", "rotation")]
        [InlineData("blank_style", "text_style")]
        [InlineData("bad_point", "location")]
        [InlineData("non_numeric_point", "location")]
        [InlineData("null_character", "null characters")]
        [InlineData("bad_color", "color_index")]
        public async Task Invalid_request_fails_before_CAD_call(string scenario, string message)
        {
            var result = JObject.Parse(await TextCreationTools.CreateMText(
                scenario == "null_character" ? "A\0B" : "A\\PB",
                scenario == "bad_point" ? "[]" : scenario == "non_numeric_point" ? "{\"x\":\"1\",\"y\":0}" : "{\"x\":0,\"y\":0}",
                width: scenario == "negative_width" ? -1 : scenario == "nan_width" ? double.NaN : null,
                height: scenario == "infinite_height" ? double.PositiveInfinity : null,
                rotation: scenario == "infinite_rotation" ? double.NegativeInfinity : null,
                text_style: scenario == "blank_style" ? " " : null,
                color_index: scenario == "bad_color" ? 257 : null));
            Assert.False(result.Value<bool>("ok"));
            Assert.Contains(message, result.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
        }
    }
}
