using Bimwright.Dwg.Plugin.Annotation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AnnotationRequestValidationTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryValidateTextContent_RejectsBlankText(string text)
        {
            var ok = AnnotationRequestValidation.TryValidateTextContent(text, "text", out var error);

            Assert.False(ok);
            Assert.Contains("text", error);
        }

        [Fact]
        public void TryReadLeader_AcceptsTwoWcsPointsAndDefaults()
        {
            var parameters = LeaderParameters();
            parameters["points"][0]["z"] = 12.0;
            parameters["points"][1]["z"] = 12.0;
            Assert.True(AnnotationRequestValidation.TryReadLeader(parameters, out var input, out var error), error);
            Assert.Equal(0, input.ArrowPoint.X);
            Assert.Equal(5, input.TextPoint.X);
            Assert.Equal(12, input.ArrowPoint.Z);
            Assert.Equal(12, input.TextPoint.Z);
            Assert.Null(input.Text);
            Assert.Null(input.TextStyle);
            Assert.Null(input.Layer);
            Assert.Equal(2.5, input.Height);
            Assert.Equal(2.5, input.ArrowSize);
            Assert.Equal(256, input.ColorIndex);
        }

        [Fact]
        public void TryReadLeader_PreservesExplicitTextAndAppearance()
        {
            var parameters = LeaderParameters();
            parameters["text"] = "Т1\\PПримечание";
            parameters["height"] = 3.5;
            parameters["arrow_size"] = 1.25;
            parameters["text_style"] = "GOST TYPE A";
            parameters["layer"] = "A-NOTE";
            parameters["color_index"] = 6;
            Assert.True(AnnotationRequestValidation.TryReadLeader(parameters, out var input, out var error), error);
            Assert.Equal("Т1\\PПримечание", input.Text);
            Assert.Equal(3.5, input.Height);
            Assert.Equal(1.25, input.ArrowSize);
            Assert.Equal("GOST TYPE A", input.TextStyle);
            Assert.Equal("A-NOTE", input.Layer);
            Assert.Equal(6, input.ColorIndex);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public void TryValidateLeaderPointCount_RequiresExactlyTwoPoints(int count)
        {
            Assert.False(AnnotationRequestValidation.TryValidateLeaderPointCount(count, out var error));
            Assert.Contains("exactly 2", error);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("[{\"x\":0,\"y\":0}]")]
        [InlineData("[{\"x\":0,\"y\":0},{\"x\":5,\"y\":5},{\"x\":10,\"y\":10}]")]
        [InlineData("[null,{\"x\":5,\"y\":5}]")]
        [InlineData("[{\"x\":\"0\",\"y\":0},{\"x\":5,\"y\":5}]")]
        [InlineData("[{\"x\":0},{\"x\":5,\"y\":5}]")]
        [InlineData("[{\"x\":0,\"y\":0},{\"x\":0,\"y\":0}]")]
        [InlineData("[{\"x\":0,\"y\":0,\"z\":1},{\"x\":5,\"y\":5}]")]
        public void TryReadLeader_RejectsInvalidPointGeometry(string pointsJson)
        {
            var parameters = LeaderParameters();
            parameters["points"] = JToken.Parse(pointsJson);
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error));
            Assert.Contains("points", error);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void TryReadLeader_RejectsNonFiniteCoordinates(double value)
        {
            var parameters = LeaderParameters();
            parameters["points"][1]["x"] = new JValue(value);
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error));
            Assert.Contains("finite", error);
        }

        [Theory]
        [InlineData("height", 0)]
        [InlineData("height", -1)]
        [InlineData("height", double.NaN)]
        [InlineData("height", double.PositiveInfinity)]
        [InlineData("arrow_size", 0)]
        [InlineData("arrow_size", -1)]
        [InlineData("arrow_size", double.NegativeInfinity)]
        public void TryReadLeader_RejectsNonPositiveOrNonFiniteSizes(string field, double value)
        {
            var parameters = LeaderParameters();
            parameters[field] = new JValue(value);
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error));
            Assert.Contains(field, error);
        }

        [Theory]
        [InlineData("height", "\"2.5\"")]
        [InlineData("arrow_size", "true")]
        [InlineData("text", "42")]
        [InlineData("text_style", "42")]
        [InlineData("text_style", "\" \"")]
        [InlineData("layer", "[]")]
        [InlineData("color_index", "1.5")]
        [InlineData("color_index", "0")]
        [InlineData("color_index", "257")]
        public void TryReadLeader_RejectsInvalidAppearanceTokens(string field, string json)
        {
            var parameters = LeaderParameters();
            parameters[field] = JToken.Parse(json);
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error));
            Assert.Contains(field, error);
        }

        [Theory]
        [InlineData("%<\\AcVar Date >%")]
        [InlineData("bad\0text")]
        public void TryReadLeader_RejectsFieldsAndNullCharacters(string text)
        {
            var parameters = LeaderParameters();
            parameters["text"] = text;
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error));
            Assert.Contains("text", error);
        }

        [Fact]
        public void TryReadLeader_EnforcesTextLengthLimit()
        {
            var parameters = LeaderParameters();
            parameters["text"] = new string('A', 16384);
            Assert.True(AnnotationRequestValidation.TryReadLeader(parameters, out _, out var error), error);
            parameters["text"] = new string('A', 16385);
            Assert.False(AnnotationRequestValidation.TryReadLeader(parameters, out _, out error));
            Assert.Contains("16384", error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void TryReadLeader_AllowsNoAnnotation(string text)
        {
            var parameters = LeaderParameters();
            parameters["text"] = text == null ? JValue.CreateNull() : new JValue(text);
            Assert.True(AnnotationRequestValidation.TryReadLeader(parameters, out var input, out var error), error);
            Assert.Null(input.Text);
        }

        private static JObject LeaderParameters()
            => JObject.Parse("{\"points\":[{\"x\":0,\"y\":0},{\"x\":5,\"y\":5}]}");

        [Theory]
        [InlineData(0, 2, "rows")]
        [InlineData(2, 0, "columns")]
        public void TryValidateTableShape_RejectsNonPositiveDimensions(
            int rows,
            int columns,
            string expectedError)
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                rows,
                columns,
                JArray.Parse("[[\"A\",\"B\"],[\"C\",\"D\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains(expectedError, error);
        }

        [Fact]
        public void TryValidateTableShape_RejectsTooFewRows()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                2,
                2,
                JArray.Parse("[[\"A\",\"B\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains("rows", error);
        }

        [Fact]
        public void TryValidateTableShape_RejectsTooFewColumns()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                1,
                2,
                JArray.Parse("[[\"A\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains("columns", error);
        }

        [Fact]
        public void TryValidateTableShape_RejectsCellsOutsideDimensions()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                1,
                1,
                JArray.Parse("[[\"A\",\"B\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains("cells", error);
        }

        [Fact]
        public void TryValidateTableShape_AcceptsCellsWithinDimensions()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                2,
                2,
                JArray.Parse("[[\"A\",\"B\"],[\"C\",\"D\"]]"),
                out var error);

            Assert.True(ok, error);
        }
    }
}
