using System;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class SelectionReadInputTests
    {
        [Fact]
        public void EmptyObjectUsesBoundedIdentificationDefaults()
        {
            Assert.True(SelectionReadInput.TryParse(new JObject(), out var input, out var error), error);
            Assert.Equal(100, input.MaxEntities);
            Assert.False(input.IncludeGeometry);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(100, true)]
        [InlineData(500, true)]
        public void AcceptsExplicitLimitsAndGeometry(int maxEntities, bool includeGeometry)
        {
            var parameters = new JObject
            {
                ["max_entities"] = maxEntities,
                ["include_geometry"] = includeGeometry
            };

            Assert.True(SelectionReadInput.TryParse(parameters, out var input, out var error), error);
            Assert.Equal(maxEntities, input.MaxEntities);
            Assert.Equal(includeGeometry, input.IncludeGeometry);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("\"selection\"")]
        public void RejectsMissingOrNonObjectParameters(string json)
        {
            var parameters = json == null ? null : JToken.Parse(json);
            Assert.False(SelectionReadInput.TryParse(parameters, out var input, out var error));
            Assert.Null(input);
            Assert.Equal("get_selection params must be an object", error);
        }

        [Theory]
        [InlineData("{\"max_entities\":null}")]
        [InlineData("{\"max_entities\":0}")]
        [InlineData("{\"max_entities\":-1}")]
        [InlineData("{\"max_entities\":501}")]
        [InlineData("{\"max_entities\":100.0}")]
        [InlineData("{\"max_entities\":1.5}")]
        [InlineData("{\"max_entities\":\"100\"}")]
        [InlineData("{\"max_entities\":true}")]
        [InlineData("{\"max_entities\":[]}")]
        [InlineData("{\"max_entities\":{}}")]
        [InlineData("{\"max_entities\":9223372036854775808}")]
        public void RejectsInvalidOrCoercedLimits(string json)
        {
            Assert.False(SelectionReadInput.TryParse(JToken.Parse(json), out var input, out var error));
            Assert.Null(input);
            Assert.Equal("max_entities must be an integer between 1 and 500", error);
        }

        [Theory]
        [InlineData("{\"include_geometry\":null}")]
        [InlineData("{\"include_geometry\":0}")]
        [InlineData("{\"include_geometry\":\"false\"}")]
        [InlineData("{\"include_geometry\":[]}")]
        [InlineData("{\"include_geometry\":{}}")]
        public void RejectsInvalidOrCoercedGeometryFlag(string json)
        {
            Assert.False(SelectionReadInput.TryParse(JToken.Parse(json), out var input, out var error));
            Assert.Null(input);
            Assert.Equal("include_geometry must be a boolean", error);
        }

        [Theory]
        [InlineData("limit")]
        [InlineData("max_Entities")]
        [InlineData("expected_document")]
        [InlineData("include_attributes")]
        public void RejectsUnknownFieldsInsteadOfIgnoringThem(string field)
        {
            var parameters = new JObject { [field] = 1 };
            Assert.False(SelectionReadInput.TryParse(parameters, out var input, out var error));
            Assert.Null(input);
            Assert.Equal("unknown get_selection field: " + field, error);
        }

        [Fact]
        public void CountsAllSelectedButOnlyReturnedTypesWhenTruncated()
        {
            var summary = new SelectionReadSummary(selectedCount: 7, maxEntities: 3);
            summary.Add("Line", true);
            summary.Add("Circle", true);
            summary.Add("Line", true);

            Assert.Equal(7, summary.SelectedCount);
            Assert.Equal(3, summary.ReturnLimit);
            Assert.Equal(3, summary.ReturnedCount);
            Assert.Equal(2, summary.TypeCounts["Line"]);
            Assert.Equal(1, summary.TypeCounts["Circle"]);
            Assert.Equal(0, summary.Errors);
            Assert.True(summary.Truncated);
            Assert.False(summary.Complete);
            Assert.Throws<InvalidOperationException>(() => summary.Add("Circle", true));
        }

        [Fact]
        public void ExactLimitIsCompleteOnlyAfterEveryRowHasBeenRead()
        {
            var summary = new SelectionReadSummary(selectedCount: 2, maxEntities: 2);
            Assert.False(summary.Complete);
            summary.Add("BlockReference", true);
            Assert.False(summary.Complete);
            summary.Add("BlockReference", true);

            Assert.False(summary.Truncated);
            Assert.True(summary.Complete);
            Assert.Equal(2, summary.TypeCounts["BlockReference"]);
        }

        [Fact]
        public void ItemErrorsRetainCountAndPreventCompleteEvenWithoutTruncation()
        {
            var summary = new SelectionReadSummary(selectedCount: 3, maxEntities: 100);
            summary.Add("Line", true);
            summary.Add("Circle", false);
            summary.Add(null, false);

            Assert.Equal(3, summary.ReturnedCount);
            Assert.Equal(2, summary.Errors);
            Assert.False(summary.Truncated);
            Assert.False(summary.Complete);
            // A known native type remains countable even when another property fails.
            Assert.Equal(1, summary.TypeCounts["Circle"]);
            Assert.Equal(2, summary.TypeCounts.Count);
        }

        [Fact]
        public void EmptySelectionDoesNotInventRowsOrTypeCounts()
        {
            var summary = new SelectionReadSummary(selectedCount: 0, maxEntities: 100);
            Assert.Equal(0, summary.ReturnLimit);
            Assert.Equal(0, summary.ReturnedCount);
            Assert.Equal(0, summary.Errors);
            Assert.Empty(summary.TypeCounts);
            Assert.False(summary.Truncated);
            Assert.True(summary.Complete);
            Assert.Throws<InvalidOperationException>(() => summary.Add("Line", true));
        }

        [Theory]
        [InlineData(-1, 100)]
        [InlineData(1, 0)]
        [InlineData(1, 501)]
        public void SummaryCannotCircumventInputLimits(int selectedCount, int maxEntities)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionReadSummary(selectedCount, maxEntities));
        }
    }
}
