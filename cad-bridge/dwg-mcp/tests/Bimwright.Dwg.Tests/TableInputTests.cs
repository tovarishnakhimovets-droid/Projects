using System;
using System.Linq;
using System.Text;
using Bimwright.Dwg.Plugin.Tables;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class TableInputTests
    {
        private static JObject Request(int rows = 2, int columns = 2)
        {
            var cells = new JArray(Enumerable.Range(0, rows)
                .Select(_ => new JArray(Enumerable.Repeat("", columns))));
            if (rows > 0 && columns > 0) cells[0][0] = "N1";
            return new JObject
            {
                ["expected_document"] = @"D:\Drawings\sheet.dwg",
                ["expected_fingerprint"] = "1e3a3e15-4e07-c947-a816-9105ed7ec14d",
                ["insertion_point"] = new JObject { ["x"] = 12, ["y"] = 34, ["z"] = 5 },
                ["cells"] = cells,
                ["column_widths"] = new JArray(Enumerable.Repeat(12d, columns)),
                ["row_heights"] = new JArray(Enumerable.Repeat(5d, rows)),
                ["layer"] = "ТК",
                ["text_style"] = "Standard"
            };
        }

        private static JObject Merge(int top = 0, int left = 0, int bottom = 0, int right = 1)
            => new JObject { ["top"] = top, ["left"] = left, ["bottom"] = bottom, ["right"] = right };

        private static JObject Format(int row = 0, int column = 0)
            => new JObject { ["row"] = row, ["column"] = column };

        private static string Rejected(JToken request)
        {
            Assert.False(TableInput.TryParse(request, out var input, out var error));
            Assert.Null(input);
            Assert.False(string.IsNullOrWhiteSpace(error));
            return error;
        }

        [Fact]
        public void Defaults_preserve_plain_strings_coordinates_and_an_independent_plan()
        {
            var request = Request();
            request["cells"][0][1] = "Первая\r\nВторая";
            request["cells"][1][0] = "2х300";
            request["cells"][1][1] = "100+200";
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(2, input.Rows); Assert.Equal(2, input.Columns);
            Assert.Equal(12, input.Position.X); Assert.Equal(34, input.Position.Y); Assert.Equal(5, input.Position.Z);
            Assert.Equal("Первая\nВторая", input.Cells[0][1]);
            Assert.Equal("2х300", input.Cells[1][0]); Assert.Equal("100+200", input.Cells[1][1]);
            Assert.Equal(2.5, input.TextHeight); Assert.Equal(0.4, input.HorizontalMargin);
            Assert.Equal(0.2, input.VerticalMargin); Assert.Equal(0, input.Rotation);
            Assert.False(input.DryRun); Assert.Empty(input.Merges); Assert.Empty(input.Formats);
            request["cells"][0][0] = "changed";
            request["column_widths"][0] = 999;
            Assert.Equal("N1", input.Cells[0][0]); Assert.Equal(12, input.ColumnWidths[0]);
        }

        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("42")] [InlineData("true")]
        public void Root_requires_an_object(string json) => Rejected(JToken.Parse(json));

        [Theory]
        [InlineData("expected_document")] [InlineData("expected_fingerprint")]
        [InlineData("insertion_point")] [InlineData("cells")]
        [InlineData("column_widths")] [InlineData("row_heights")]
        [InlineData("layer")] [InlineData("text_style")]
        public void Mandatory_fields_cannot_be_omitted(string field)
        {
            var request = Request(); request.Remove(field); Rejected(request);
        }

        [Theory]
        [InlineData("expected_document", "\"sheet.dwg\"")]
        [InlineData("expected_document", "\"D:\\\\Drawings\\\\template.dwt\"")]
        [InlineData("expected_document", "\"https://example.org/sheet.dwg\"")]
        [InlineData("expected_fingerprint", "\"not-a-guid\"")]
        [InlineData("expected_fingerprint", "\"00000000-0000-0000-0000-000000000000\"")]
        [InlineData("layer", "\" \"")] [InlineData("text_style", "null")]
        [InlineData("dry_run", "\"true\"")]
        public void Identity_names_and_boolean_do_not_coerce_invalid_values(string field, string json)
        {
            var request = Request(); request[field] = JToken.Parse(json); Rejected(request);
        }

        [Theory]
        [InlineData("root")] [InlineData("point")] [InlineData("merge")] [InlineData("format")]
        public void Unknown_properties_never_become_arbitrary_native_setters(string where)
        {
            var request = Request();
            if (where == "root") request["save"] = true;
            if (where == "point") request["insertion_point"]["normal"] = 1;
            if (where == "merge")
            {
                var merge = Merge(); merge["command"] = "ERASE ALL";
                request["merges"] = new JArray(merge);
            }
            if (where == "format")
            {
                var format = Format(); format["setter"] = "TextStyleId";
                request["cell_formats"] = new JArray(format);
            }
            Assert.Contains("unsupported", Rejected(request));
        }

        [Theory]
        [InlineData(256, 1)] [InlineData(1, 128)] [InlineData(100, 100)] [InlineData(22, 43)]
        public void Supported_shape_boundaries_and_the_full_well_schedule_fit(int rows, int columns)
        {
            Assert.True(TableInput.TryParse(Request(rows, columns), out var input, out var error), error);
            Assert.Equal(rows, input.Rows); Assert.Equal(columns, input.Columns);
        }

        [Theory]
        [InlineData(0, 2)] [InlineData(2, 0)] [InlineData(257, 1)]
        [InlineData(1, 129)] [InlineData(101, 100)]
        public void Shape_budgets_reject_empty_oversized_and_too_many_cells(int rows, int columns)
            => Rejected(Request(rows, columns));

        [Theory]
        [InlineData("row")] [InlineData("ragged")] [InlineData("widths")] [InlineData("heights")]
        public void Every_matrix_row_and_dimension_array_must_match(string scenario)
        {
            var request = Request();
            if (scenario == "row") request["cells"][1] = "not a row";
            if (scenario == "ragged") request["cells"][1] = new JArray("");
            if (scenario == "widths") request["column_widths"] = new JArray(12);
            if (scenario == "heights") request["row_heights"] = new JArray(5, 5, 5);
            Rejected(request);
        }

        [Theory]
        [InlineData("x", double.NaN)] [InlineData("y", double.PositiveInfinity)]
        [InlineData("z", double.NegativeInfinity)] [InlineData("x", 1000000000001d)]
        [InlineData("y", -1000000000001d)]
        public void Coordinates_are_finite_and_bounded(string axis, double value)
        {
            var request = Request(); request["insertion_point"][axis] = value; Rejected(request);
        }

        [Fact]
        public void Position_allows_coordinate_boundaries_and_omitted_z()
        {
            var request = Request();
            request["insertion_point"] = new JObject { ["x"] = 1e12, ["y"] = -1e12 };
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(0, input.Position.Z);
        }

        [Theory]
        [InlineData("column_widths", 0)] [InlineData("row_heights", -1)]
        [InlineData("column_widths", 1000001)] [InlineData("row_heights", double.NaN)]
        [InlineData("column_widths", double.PositiveInfinity)]
        public void Dimensions_are_positive_finite_and_bounded(string field, double value)
        {
            var request = Request(); request[field][0] = value; Rejected(request);
        }

        [Theory]
        [InlineData("text_height", 0.99)] [InlineData("text_height", 1001)]
        [InlineData("horizontal_margin", -0.1)] [InlineData("vertical_margin", 1001)]
        [InlineData("rotation", double.PositiveInfinity)]
        public void Appearance_sizes_have_explicit_finite_ranges(string field, double value)
        {
            var request = Request(); request[field] = value; Rejected(request);
        }

        [Theory]
        [InlineData("column_widths", 0.8)] [InlineData("row_heights", 0.4)]
        public void Margins_cannot_consume_the_whole_inner_cell(string field, double size)
        {
            var request = Request(); request[field][0] = size;
            Assert.Contains("margin", Rejected(request));
        }

        [Fact]
        public void Zero_margins_and_large_finite_table_rotation_are_explicitly_supported()
        {
            var request = Request();
            request["horizontal_margin"] = 0; request["vertical_margin"] = 0;
            request["rotation"] = double.MaxValue; request["text_height"] = 1;
            request["dry_run"] = true;
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(double.MaxValue, input.Rotation); Assert.True(input.DryRun);
            request["text_height"] = 1000;
            Assert.True(TableInput.TryParse(request, out _, out error), error);
        }

        [Theory]
        [InlineData("%<\\AcExpr 1+1>%")] [InlineData("=1+1")] [InlineData("  =SUM(A1:A2)")]
        [InlineData("A\\PB")] [InlineData("A{B}")] [InlineData("A}B")]
        [InlineData("bad\0text")] [InlineData("tab\ttext")]
        public void Cells_reject_fields_formulas_inline_MText_and_hidden_controls(string text)
        {
            var request = Request(); request["cells"][0][0] = text; Rejected(request);
        }

        [Theory]
        [InlineData("null")] [InlineData("42")] [InlineData("true")] [InlineData("{}")]
        public void Cell_values_are_strings_without_automatic_numeric_or_null_conversion(string json)
        {
            var request = Request(); request["cells"][0][0] = JToken.Parse(json); Rejected(request);
        }

        [Theory]
        [InlineData("%%c300")] [InlineData("45%%d")] [InlineData("%%p1")]
        [InlineData("%%uText%%u")] [InlineData("%%123")]
        public void Legacy_AutoCAD_text_controls_are_not_literal_cell_strings(string text)
        {
            var request = Request(); request["cells"][0][0] = text;
            Assert.Contains("plain text", Rejected(request));
        }

        [Fact]
        public void Empty_dash_and_numeric_text_remain_distinct()
        {
            var request = Request(); request["cells"] = new JArray(new JArray("", "-"), new JArray("0", "5,06"));
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal("", input.Cells[0][0]); Assert.Equal("-", input.Cells[0][1]);
            Assert.Equal("0", input.Cells[1][0]); Assert.Equal("5,06", input.Cells[1][1]);
        }

        [Fact]
        public void Per_cell_character_limit_has_a_checked_boundary()
        {
            var request = Request(); request["cells"][0][0] = new string('Ж', 4096);
            Assert.True(TableInput.TryParse(request, out _, out var error), error);
            request["cells"][0][0] = new string('Ж', 4097);
            Assert.Contains("4096", Rejected(request));
        }

        [Fact]
        public void Aggregate_text_limit_applies_even_when_every_cell_is_below_its_own_limit()
        {
            var request = Request(1, 50);
            for (int column = 0; column < 50; column++) request["cells"][0][column] = new string('Ж', 4000);
            Assert.True(TableInput.TryParse(request, out _, out var error), error);
            request["cells"][0][0] = new string('Ж', 4001);
            Assert.Contains("200000", Rejected(request));
        }

        [Fact]
        public void Byte_budget_counts_UTF8_rather_than_UTF16_characters()
        {
            var request = Request(); request["layer"] = new string('Ж', TableInput.MaxInputBytes / 2 + 1);
            string json = request.ToString(Formatting.None);
            Assert.True(json.Length < TableInput.MaxInputBytes);
            Assert.True(Encoding.UTF8.GetByteCount(json) > TableInput.MaxInputBytes);
            Assert.Contains("1 MiB", Rejected(request));
        }

        [Fact]
        public void Nonoverlapping_header_merges_retain_their_anchor_text_and_format()
        {
            var request = Request(4, 3);
            request["cells"][0][0] = "Трубы, мм"; request["cells"][2][0] = "Размеры";
            request["merges"] = new JArray(Merge(0, 0, 1, 2), Merge(2, 0, 2, 2));
            var format = Format(); format["rotation"] = 90; format["text_height"] = 2.5; format["alignment"] = "middle_center";
            request["cell_formats"] = new JArray(format);
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(2, input.Merges.Length);
            Assert.Equal(1, input.Merges[0].Bottom); Assert.Equal(2, input.Merges[0].Right);
            Assert.Equal("Трубы, мм", input.Cells[0][0]);
            Assert.Equal(90d, input.Formats[0].Rotation); Assert.Equal("middle_center", input.Formats[0].Alignment);
        }

        [Theory]
        [InlineData("overlap")] [InlineData("duplicate")] [InlineData("single")]
        [InlineData("outside")] [InlineData("reversed")] [InlineData("fractional")]
        public void Ambiguous_or_invalid_merge_ranges_are_refused_as_a_whole(string scenario)
        {
            var request = Request(3, 3);
            JArray merges;
            if (scenario == "overlap") merges = new JArray(Merge(0, 0, 0, 1), Merge(0, 1, 1, 2));
            else if (scenario == "duplicate") merges = new JArray(Merge(), Merge());
            else if (scenario == "single") merges = new JArray(Merge(0, 0, 0, 0));
            else if (scenario == "outside") merges = new JArray(Merge(0, 0, 0, 3));
            else if (scenario == "reversed") merges = new JArray(Merge(1, 0, 0, 1));
            else { var merge = Merge(); merge["left"] = 0.5; merges = new JArray(merge); }
            request["merges"] = merges; Rejected(request);
        }

        [Theory]
        [InlineData("value")] [InlineData(" ")] [InlineData("\n")]
        public void Merging_never_discards_even_whitespace_text_in_a_non_anchor(string text)
        {
            var request = Request(); request["cells"][0][1] = text; request["merges"] = new JArray(Merge());
            Assert.Contains("discard", Rejected(request));
        }

        [Fact]
        public void Merged_non_anchor_formatting_and_duplicate_formats_are_rejected()
        {
            var request = Request(); request["merges"] = new JArray(Merge());
            request["cell_formats"] = new JArray(Format(0, 1));
            Assert.Contains("non-anchor", Rejected(request));
            request.Remove("merges");
            request["cell_formats"] = new JArray(Format(), Format());
            Assert.Contains("repeat", Rejected(request));
        }

        [Theory]
        [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
        public void All_supported_cell_rotations_and_alignments_are_preserved(int rotation)
        {
            var request = Request();
            var formats = new JArray();
            string[] alignments = { "middle_center", "middle_left", "middle_right" };
            for (int index = 0; index < 3; index++)
            {
                var format = Format(index / 2, index % 2);
                format["rotation"] = rotation; format["alignment"] = alignments[index]; formats.Add(format);
            }
            request["cell_formats"] = formats;
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.All(input.Formats, format => Assert.Equal((double)rotation, format.Rotation));
            Assert.Equal(alignments, input.Formats.Select(format => format.Alignment));
        }

        [Theory]
        [InlineData("rotation", "45")] [InlineData("rotation", "360")] [InlineData("rotation", "-90")]
        [InlineData("text_height", "0.5")] [InlineData("alignment", "\"top_left\"")]
        [InlineData("row", "2")] [InlineData("column", "-1")] [InlineData("row", "0.5")]
        public void Unsupported_cell_format_properties_do_not_reach_native_API(string field, string json)
        {
            var request = Request(); var format = Format(); format[field] = JToken.Parse(json);
            request["cell_formats"] = new JArray(format); Rejected(request);
        }

        [Theory]
        [InlineData("\r\n")] [InlineData("\r")] [InlineData("\n")]
        public void All_newline_styles_normalize_to_LF_for_native_readback(string newline)
        {
            var request = Request(); request["cells"][0][0] = "Первая" + newline + "Вторая";
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal("Первая\nВторая", input.Cells[0][0]);
        }
        [Fact]
        public void Optional_null_overrides_mean_no_override_and_z_defaults_to_zero()
        {
            var request = Request();
            ((JObject)request["insertion_point"]).Remove("z");
            request["rotation"] = JValue.CreateNull(); request["merges"] = JValue.CreateNull();
            var format = Format(); format["text_height"] = JValue.CreateNull(); format["alignment"] = JValue.CreateNull();
            request["cell_formats"] = new JArray(format);
            Assert.True(TableInput.TryParse(request, out var input, out var error), error);
            Assert.Equal(0, input.Position.Z); Assert.Equal(0, input.Rotation);
            Assert.Null(input.Formats[0].TextHeight); Assert.Null(input.Formats[0].Rotation);
            Assert.Null(input.Formats[0].Alignment);
        }
    }
}
