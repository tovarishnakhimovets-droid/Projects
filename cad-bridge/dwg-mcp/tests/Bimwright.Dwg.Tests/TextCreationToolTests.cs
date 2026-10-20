using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Annotation;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TextCreationToolTests
    {
        [Fact]
        public void Actual_MCP_schema_keeps_legacy_arguments_and_adds_optional_appearance()
        {
            var tool = McpServerTool.Create(typeof(TextCreationTools).GetMethod(nameof(TextCreationTools.CreateText)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_create_text", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "position", "text" }, tool.InputSchema.GetProperty("required").EnumerateArray()
                .Select(field => field.GetString()).OrderBy(field => field, StringComparer.Ordinal));
            var properties = tool.InputSchema.GetProperty("properties");
            Assert.Contains("string", properties.GetProperty("position").GetProperty("type").ToString());
            Assert.Contains("string", properties.GetProperty("text_style").GetProperty("type").ToString());
            Assert.Contains("number", properties.GetProperty("width_factor").GetProperty("type").ToString());
        }

        [Fact]
        public void CreateText_has_exactly_one_MCP_attribute_and_retains_CSharp_compatibility_helper()
        {
            var oldMethod = typeof(AnnotationTools).GetMethod(nameof(AnnotationTools.CreateText));
            Assert.NotNull(oldMethod);
            Assert.Null(oldMethod.GetCustomAttribute<McpServerToolAttribute>());
            var attributes = typeof(TextCreationTools).GetMethods().Concat(typeof(AnnotationTools).GetMethods())
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute?.Name == "dwg_create_text");
            Assert.Single(attributes);
        }

        [Fact]
        public void Wire_request_defaults_to_explicit_ByLayer_and_retains_drawing_units()
        {
            Assert.True(TextCreationTools.TryBuildRequest("Т1", "{\"x\":10,\"y\":20}",
                null, null, null, null, null, null, out var request, out var error), error);
            Assert.Equal(256, request.Value<int>("color_index"));
            Assert.IsType<JObject>(request["position"]);
            Assert.Equal(10, request["position"].Value<double>("x"));
            Assert.Null(request["height"]);
            Assert.Null(request["text_style"]);
            Assert.Null(request["width_factor"]);
        }

        [Fact]
        public void Wire_request_preserves_explicit_style_dimensions_rotation_and_color()
        {
            Assert.True(TextCreationTools.TryBuildRequest("Т1", "{\"x\":10,\"y\":20,\"z\":3}",
                3.5, 90, "A-TEXT", 30, "GOST TYPE A", 0.8, out var request, out var error), error);
            Assert.Equal("GOST TYPE A", request.Value<string>("text_style"));
            Assert.Equal(0.8, request.Value<double>("width_factor"));
            Assert.Equal(3.5, request.Value<double>("height"));
            Assert.Equal(90, request.Value<double>("rotation"));
            Assert.Equal("A-TEXT", request.Value<string>("layer"));
            Assert.Equal(30, request.Value<int>("color_index"));
        }

        [Theory]
        [InlineData("point_root", "position")]
        [InlineData("point_string_coordinate", "position")]
        [InlineData("blank_style", "text_style")]
        [InlineData("negative_width", "width_factor")]
        [InlineData("nan_width", "width_factor")]
        [InlineData("infinite_height", "height")]
        [InlineData("infinite_rotation", "rotation")]
        [InlineData("color_zero", "color_index")]
        [InlineData("multiline", "single line")]
        public async Task Invalid_appearance_is_rejected_before_any_CAD_call(string scenario, string expected)
        {
            var position = scenario switch
            {
                "point_root" => "[]",
                "point_string_coordinate" => "{\"x\":\"1\",\"y\":0}",
                _ => "{\"x\":0,\"y\":0}"
            };
            var response = JObject.Parse(await TextCreationTools.CreateText(
                scenario == "multiline" ? "A\nB" : "A", position,
                height: scenario == "infinite_height" ? double.PositiveInfinity : null,
                rotation: scenario == "infinite_rotation" ? double.PositiveInfinity : null,
                color_index: scenario == "color_zero" ? 0 : null,
                text_style: scenario == "blank_style" ? " " : null,
                width_factor: scenario == "negative_width" ? -1 : scenario == "nan_width" ? double.NaN : null));
            Assert.False(response.Value<bool>("ok"));
            Assert.Contains(expected, response.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Shared_parser_preserves_defaults_without_adopting_style_record_geometry()
        {
            Assert.True(AnnotationRequestValidation.TryReadDbText(
                JObject.Parse("{\"text\":\"A\",\"position\":{\"x\":1,\"y\":2},\"text_style\":\"GOST TYPE A\"}"),
                out var input, out var error), error);
            Assert.Equal(2.5, input.Height);
            Assert.Null(input.WidthFactor);
            Assert.Equal(0, input.RotationDegrees);
            Assert.Equal(0, input.Z);
            Assert.Equal(256, input.ColorIndex);
            Assert.Equal("GOST TYPE A", input.TextStyle);
        }

        [Theory]
        [InlineData("text", "1", "text")]
        [InlineData("text_style", "5", "text_style")]
        [InlineData("width_factor", "\"0.8\"", "width_factor")]
        [InlineData("width_factor", "0", "width_factor")]
        [InlineData("height", "-1", "height")]
        [InlineData("layer", "true", "layer")]
        [InlineData("color_index", "2.5", "color_index")]
        [InlineData("color_index", "257", "color_index")]
        [InlineData("color_index", "9223372036854775808", "color_index")]
        public void Native_shared_parser_rejects_wrong_types_and_ranges(string field, string json, string expected)
        {
            var request = JObject.Parse("{\"text\":\"A\",\"position\":{\"x\":1,\"y\":2}}");
            request[field] = JToken.Parse(json);
            Assert.False(AnnotationRequestValidation.TryReadDbText(request, out _, out var error));
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Shared_parser_rejects_nonfinite_point_and_does_not_coerce_numeric_strings()
        {
            var request = JObject.Parse("{\"text\":\"A\",\"position\":{\"x\":1,\"y\":2}}");
            request["position"]["x"] = double.NaN;
            Assert.False(AnnotationRequestValidation.TryReadDbText(request, out _, out var error));
            Assert.Contains("position", error);
        }
    }
}
