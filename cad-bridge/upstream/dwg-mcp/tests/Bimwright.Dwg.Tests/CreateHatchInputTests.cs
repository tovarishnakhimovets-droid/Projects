using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CreateHatchInputTests
    {
        [Fact]
        public void DefaultsKeepBoundaryLayerAndByLayerColor()
        {
            Assert.True(CreateHatchInput.TryParse(JObject.Parse("{\"boundary_handle\":\"1aB\"}"), out var input, out var error), error);
            Assert.Equal(0x1abL, input.BoundaryHandleValue);
            Assert.Null(input.Layer);
            Assert.Null(input.ColorIndex);
            Assert.Null(input.ColorRgb);
            Assert.True(input.Associative);
            Assert.Equal("above_entities", input.DrawOrder);
        }

        [Fact]
        public void ExplicitOptionsAcceptNonAssociativeRgbFillBelowEntities()
        {
            var request = JObject.Parse("{\"boundary_handle\":\"2F\",\"layer\":\"НК1\",\"color_rgb\":[255,255,255],\"associative\":false,\"draw_order\":\"below_entities\"}");
            Assert.True(CreateHatchInput.TryParse(request, out var input, out var error), error);
            Assert.Equal("НК1", input.Layer);
            Assert.Equal(new[] { 255, 255, 255 }, input.ColorRgb);
            Assert.False(input.Associative);
            Assert.Equal("below_entities", input.DrawOrder);
        }

        [Theory]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":1}", 1)]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":256}", 256)]
        public void AciAcceptsBothEndpoints(string json, int expected)
        {
            Assert.True(CreateHatchInput.TryParse(JObject.Parse(json), out var input, out var error), error);
            Assert.Equal(expected, input.ColorIndex);
        }

        [Theory]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":7,\"color_rgb\":[255,255,255]}", "mutually exclusive")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":0}", "color_index")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":257}", "color_index")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":7.0}", "color_index")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_index\":\"7\"}", "color_index")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[255,255]}", "color_rgb")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[-1,0,0]}", "color_rgb[0]")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[0,256,0]}", "color_rgb[1]")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[0,0,255.0]}", "color_rgb[2]")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[0,0,\"255\"]}", "color_rgb[2]")]
        [InlineData("{\"boundary_handle\":\"A\",\"color_rgb\":[0,0,null]}", "color_rgb[2]")]
        [InlineData("{\"boundary_handle\":\"A\",\"associative\":\"false\"}", "associative")]
        [InlineData("{\"boundary_handle\":\"A\",\"draw_order\":\"top\"}", "draw_order")]
        [InlineData("{\"boundary_handle\":\"A\",\"layer\":\" \"}", "layer")]
        [InlineData("{\"boundary_handle\":\"A\",\"layer\":42}", "layer")]
        public void InvalidOptionsAreRejectedBeforeHostAccess(string json, string expectedError)
        {
            Assert.False(CreateHatchInput.TryParse(JObject.Parse(json), out var input, out var error));
            Assert.Null(input);
            Assert.Contains(expectedError, error);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"boundary_handle\":null}")]
        [InlineData("{\"boundary_handle\":42}")]
        [InlineData("{\"boundary_handle\":\"\"}")]
        [InlineData("{\"boundary_handle\":\"0\"}")]
        [InlineData("{\"boundary_handle\":\" A\"}")]
        [InlineData("{\"boundary_handle\":\"+A\"}")]
        [InlineData("{\"boundary_handle\":\"G\"}")]
        [InlineData("{\"boundary_handle\":\"8000000000000000\"}")]
        public void InvalidBoundaryHandlesAreRejected(string json)
        {
            Assert.False(CreateHatchInput.TryParse(JObject.Parse(json), out _, out var error));
            Assert.Contains("boundary_handle", error);
        }

        [Fact]
        public void NullOptionalFieldsUseDefaults()
        {
            var request = JObject.Parse("{\"boundary_handle\":\"A\",\"layer\":null,\"color_index\":null,\"color_rgb\":null,\"associative\":null,\"draw_order\":null}");
            Assert.True(CreateHatchInput.TryParse(request, out var input, out var error), error);
            Assert.Null(input.ColorIndex);
            Assert.Null(input.ColorRgb);
            Assert.True(input.Associative);
            Assert.Equal("above_entities", input.DrawOrder);
        }

        [Fact]
        public void NonObjectRequestAndOversizedColorIntegerReturnValidationErrors()
        {
            Assert.False(CreateHatchInput.TryParse(new JArray(), out _, out var objectError));
            Assert.Contains("object", objectError);
            Assert.False(CreateHatchInput.TryParse(JObject.Parse("{\"boundary_handle\":\"A\",\"color_index\":9223372036854775808}"), out _, out var colorError));
            Assert.Contains("color_index", colorError);
        }
    }
}
