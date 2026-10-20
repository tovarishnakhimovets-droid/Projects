using System;
using System.Globalization;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class LineweightSpecTests
    {
        [Theory]
        [InlineData("ru-RU")]
        [InlineData("en-US")]
        public void Parse_UsesNumericMillimetersIndependentOfCulture(string culture)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.True(LineweightSpec.TryParse(JToken.Parse("0.3"), null, out var value, out var error), error);
                Assert.Equal(30, value);
                Assert.Null(error);
                Assert.False(LineweightSpec.TryParse(new JValue("0,3"), "explicit", out _, out _));
                Assert.False(LineweightSpec.TryParse(new JValue("0.3"), "explicit", out _, out _));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Theory]
        [InlineData(0d, 0)]
        [InlineData(0.05d, 5)]
        [InlineData(0.09d, 9)]
        [InlineData(0.13d, 13)]
        [InlineData(0.15d, 15)]
        [InlineData(0.18d, 18)]
        [InlineData(0.20d, 20)]
        [InlineData(0.25d, 25)]
        [InlineData(0.30d, 30)]
        [InlineData(0.35d, 35)]
        [InlineData(0.40d, 40)]
        [InlineData(0.50d, 50)]
        [InlineData(0.53d, 53)]
        [InlineData(0.60d, 60)]
        [InlineData(0.70d, 70)]
        [InlineData(0.80d, 80)]
        [InlineData(0.90d, 90)]
        [InlineData(1.00d, 100)]
        [InlineData(1.06d, 106)]
        [InlineData(1.20d, 120)]
        [InlineData(1.40d, 140)]
        [InlineData(1.58d, 158)]
        [InlineData(2.00d, 200)]
        [InlineData(2.11d, 211)]
        public void Parse_AcceptsTheDocumentedAutodeskWeights(double mm, int expected)
        {
            Assert.True(LineweightSpec.TryParseLayer(new JValue(mm), out var value, out var error), error);
            Assert.Equal(expected, value);
            Assert.Equal(mm, LineweightSpec.GetMillimeters(value));
            Assert.Equal("explicit", LineweightSpec.GetMode(value));
        }

        [Theory]
        [InlineData(0.31d)]
        [InlineData(0.30000000000000004d)]
        [InlineData(-0.3d)]
        [InlineData(3d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Parse_RejectsUnsupportedOrNonFiniteWeightsWithoutRounding(double mm)
        {
            Assert.False(LineweightSpec.TryParse(new JValue(mm), "explicit", out _, out var error));
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("by_layer", -1)]
        [InlineData("by_block", -2)]
        public void Parse_InheritedModesRequireNoMillimeterField(string mode, int expected)
        {
            Assert.True(LineweightSpec.TryParse(null, mode, out var value, out var error), error);
            Assert.Equal(expected, value);
            Assert.Equal(mode, LineweightSpec.GetMode(value));
            Assert.Null(LineweightSpec.GetMillimeters(value));
            Assert.False(LineweightSpec.TryParse(new JValue(0.3d), mode, out _, out _));
            Assert.False(LineweightSpec.TryParse(JValue.CreateNull(), mode, out _, out _));
        }

        [Fact]
        public void Parse_RejectsMissingWeightWrongTypesAndUnsupportedModes()
        {
            Assert.False(LineweightSpec.TryParse(null, "explicit", out _, out _));
            Assert.False(LineweightSpec.TryParse(JValue.CreateNull(), "explicit", out _, out _));
            Assert.False(LineweightSpec.TryParse(new JValue(true), "explicit", out _, out _));
            Assert.False(LineweightSpec.TryParse(new JObject(), "explicit", out _, out _));
            Assert.False(LineweightSpec.TryParse(new JValue(0.3d), "default", out _, out _));
            Assert.False(LineweightSpec.TryParse(new JValue(0.3d), "ByLayer", out _, out _));
            Assert.False(LineweightSpec.TryParse(new JValue(0.3d), "", out _, out _));
        }

        [Fact]
        public void Read_DefaultHasNoExplicitMillimeterValue()
        {
            Assert.Equal("default", LineweightSpec.GetMode(-3));
            Assert.Null(LineweightSpec.GetMillimeters(-3));
            // ByDIPs is an internal Autodesk value, not a supported drawing mode.
            Assert.Throws<ArgumentOutOfRangeException>(() => LineweightSpec.GetMode(-4));
        }
    }
}
