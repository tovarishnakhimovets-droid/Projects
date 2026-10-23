using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Tables
{
    // Closed, host-free input for one native table. No fields, formulas or arbitrary setters.
    internal sealed class TableInput
    {
        internal const int MaxRows = 256, MaxColumns = 128, MaxCells = 10000;
        internal const int MaxCellCharacters = 4096, MaxTextCharacters = 200000;
        internal const int MaxInputBytes = 1024 * 1024;

        internal string ExpectedDocument { get; private set; }
        internal string ExpectedFingerprint { get; private set; }
        internal CadPointInput Position { get; private set; }
        internal string[][] Cells { get; private set; }
        internal double[] ColumnWidths { get; private set; }
        internal double[] RowHeights { get; private set; }
        internal string Layer { get; private set; }
        internal string TextStyle { get; private set; }
        internal double TextHeight { get; private set; }
        internal double HorizontalMargin { get; private set; }
        internal double VerticalMargin { get; private set; }
        internal double Rotation { get; private set; }
        internal TableMerge[] Merges { get; private set; }
        internal TableCellFormat[] Formats { get; private set; }
        internal bool DryRun { get; private set; }
        internal int Rows => Cells.Length;
        internal int Columns => ColumnWidths.Length;

        internal static bool TryParse(JToken token, out TableInput input, out string error)
        {
            input = null;
            error = null;
            try
            {
                var root = token as JObject;
                Need(root != null, "params must be an object");
                Need(Encoding.UTF8.GetByteCount(root.ToString(Formatting.None)) <= MaxInputBytes,
                    "table input exceeds 1 MiB UTF-8");
                Keys(root, "expected_document", "expected_fingerprint", "insertion_point", "cells",
                    "column_widths", "row_heights", "layer", "text_style", "text_height",
                    "horizontal_margin", "vertical_margin", "rotation", "merges", "cell_formats", "dry_run");

                var document = RequiredText(root, "expected_document", 32768);
                Need(DrawingIdentityPolicy.NormalizeDwgPath(document) != null,
                    "expected_document must be a fully qualified DWG path");
                var fingerprint = RequiredText(root, "expected_fingerprint", 64);
                Need(Guid.TryParse(fingerprint, out var guid) && guid != Guid.Empty,
                    "expected_fingerprint must be a nonempty drawing GUID");
                var pointObject = root["insertion_point"] as JObject;
                Need(pointObject != null, "insertion_point must be an object");
                Keys(pointObject, "x", "y", "z");
                Need(CadWire.TryParsePoint(pointObject, out var position, out var pointError),
                    "insertion_point " + pointError);
                Need(Math.Abs(position.X) <= 1e12 && Math.Abs(position.Y) <= 1e12 && Math.Abs(position.Z) <= 1e12,
                    "insertion_point coordinates must be finite and within +/-1e12");

                var entries = root["cells"] as JArray;
                Need(entries != null && entries.Count >= 1 && entries.Count <= MaxRows,
                    "cells must contain 1-256 row arrays");
                var firstRow = entries[0] as JArray;
                Need(firstRow != null && firstRow.Count >= 1 && firstRow.Count <= MaxColumns,
                    "cells must contain 1-128 columns");
                int rows = entries.Count, columns = firstRow.Count;
                Need(rows * columns <= MaxCells, "table exceeds 10000 cells");
                var cells = new string[rows][];
                int characters = 0;
                for (int row = 0; row < rows; row++)
                {
                    var values = entries[row] as JArray;
                    Need(values != null && values.Count == columns, "cells must be a rectangular string matrix");
                    cells[row] = new string[columns];
                    for (int column = 0; column < columns; column++)
                    {
                        var value = values[column];
                        Need(value != null && value.Type == JTokenType.String,
                            "cells[" + row + "][" + column + "] must be a plain string");
                        string text = value.Value<string>();
                        Need(text.Length <= MaxCellCharacters, "each cell is limited to 4096 UTF-16 characters");
                        characters += text.Length;
                        Need(characters <= MaxTextCharacters, "table text exceeds 200000 UTF-16 characters");
                        Need(text.IndexOf("%<", StringComparison.Ordinal) < 0 &&
                            !text.TrimStart().StartsWith("=", StringComparison.Ordinal),
                            "cells must not contain fields or formulas");
                        Need(text.IndexOf('\\') < 0 && text.IndexOf('{') < 0 && text.IndexOf('}') < 0 && !text.Contains("%%"),
                            "cells must be plain text without inline MText formatting");
                        Need(!text.Any(c => char.IsControl(c) && c != '\r' && c != '\n'),
                            "cells must not contain control characters other than newlines");
                        cells[row][column] = text.Replace("\r\n", "\n").Replace('\r', '\n');
                    }
                }

                var widths = Dimensions(root["column_widths"], columns, "column_widths");
                var heights = Dimensions(root["row_heights"], rows, "row_heights");
                var layer = RequiredText(root, "layer", 255);
                var style = RequiredText(root, "text_style", 255);
                Need(!layer.Any(char.IsControl) && !style.Any(char.IsControl),
                    "layer and text_style must not contain control characters");
                double textHeight = Number(root, "text_height", 2.5, 1, 1000);
                double horizontalMargin = Number(root, "horizontal_margin", 0.4, 0, 1000);
                double verticalMargin = Number(root, "vertical_margin", 0.2, 0, 1000);
                Need(2 * horizontalMargin < widths.Min(),
                    "horizontal_margin must leave positive inner width in every column");
                Need(2 * verticalMargin < heights.Min(),
                    "vertical_margin must leave positive inner height in every row");
                double rotation = Number(root, "rotation", 0, -double.MaxValue, double.MaxValue);

                var covered = new bool[rows, columns];
                var nonAnchor = new bool[rows, columns];
                var merges = new List<TableMerge>();
                var mergeEntries = OptionalArray(root, "merges");
                if (mergeEntries != null)
                {
                    Need(mergeEntries.Count <= rows * columns / 2, "merges exceed the available nonoverlapping cells");
                    foreach (var entry in mergeEntries)
                    {
                        var merge = entry as JObject;
                        Need(merge != null, "merges entries must be objects");
                        Keys(merge, "top", "left", "bottom", "right");
                        int top = Integer(merge, "top", 0, rows - 1);
                        int left = Integer(merge, "left", 0, columns - 1);
                        int bottom = Integer(merge, "bottom", top, rows - 1);
                        int right = Integer(merge, "right", left, columns - 1);
                        Need(top != bottom || left != right, "merges must cover at least two cells");
                        for (int row = top; row <= bottom; row++)
                            for (int column = left; column <= right; column++)
                            {
                                Need(!covered[row, column], "merges must not overlap");
                                covered[row, column] = true;
                                if (row == top && column == left) continue;
                                Need(cells[row][column].Length == 0,
                                    "merges would discard non-anchor cell text");
                                nonAnchor[row, column] = true;
                            }
                        merges.Add(new TableMerge(top, left, bottom, right));
                    }
                }

                var formats = new List<TableCellFormat>();
                var formatEntries = OptionalArray(root, "cell_formats");
                var formatted = new HashSet<int>();
                if (formatEntries != null)
                {
                    Need(formatEntries.Count <= rows * columns, "cell_formats exceed the number of table cells");
                    foreach (var entry in formatEntries)
                    {
                        var format = entry as JObject;
                        Need(format != null, "cell_formats entries must be objects");
                        Keys(format, "row", "column", "text_height", "rotation", "alignment");
                        int row = Integer(format, "row", 0, rows - 1);
                        int column = Integer(format, "column", 0, columns - 1);
                        Need(formatted.Add(row * columns + column), "cell_formats must not repeat a row/column");
                        Need(!nonAnchor[row, column], "cell_formats must not target a merged non-anchor cell");
                        double? cellHeight = Present(format, "text_height") ? Number(format, "text_height", 2.5, 1, 1000) : (double?)null;
                        double? cellRotation = Present(format, "rotation") ? Number(format, "rotation", 0, 0, 270) : (double?)null;
                        Need(!cellRotation.HasValue || cellRotation == 0 || cellRotation == 90 ||
                            cellRotation == 180 || cellRotation == 270,
                            "cell_formats rotation must be 0, 90, 180 or 270 degrees");
                        string alignment = null;
                        if (Present(format, "alignment"))
                        {
                            alignment = RequiredText(format, "alignment", 32);
                            Need(alignment == "middle_center" || alignment == "middle_left" || alignment == "middle_right",
                                "cell_formats alignment must be middle_center, middle_left or middle_right");
                        }
                        formats.Add(new TableCellFormat(row, column, cellHeight, cellRotation, alignment));
                    }
                }

                input = new TableInput
                {
                    ExpectedDocument = document, ExpectedFingerprint = fingerprint, Position = position,
                    Cells = cells, ColumnWidths = widths, RowHeights = heights, Layer = layer, TextStyle = style,
                    TextHeight = textHeight, HorizontalMargin = horizontalMargin, VerticalMargin = verticalMargin,
                    Rotation = rotation, Merges = merges.ToArray(), Formats = formats.ToArray(),
                    DryRun = Boolean(root, "dry_run", false)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException ||
                ex is FormatException || ex is OverflowException || ex is JsonException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static double[] Dimensions(JToken token, int count, string field)
        {
            var entries = token as JArray;
            Need(entries != null && entries.Count == count, field + " length must match the table dimension");
            var result = new double[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Numeric(entries[i], field);
                Need(result[i] > 0 && result[i] <= 1e6,
                    field + " values must be positive, finite and at most 1000000");
            }
            return result;
        }

        private static string RequiredText(JObject obj, string field, int maxCharacters)
        {
            var token = obj[field];
            Need(token != null && token.Type == JTokenType.String, field + " must be a nonblank string");
            string value = token.Value<string>();
            Need(!string.IsNullOrWhiteSpace(value) && value.Length <= maxCharacters,
                field + " must be nonblank and at most " + maxCharacters + " characters");
            return value;
        }

        private static double Number(JObject obj, string field, double fallback, double minimum, double maximum)
        {
            if (!Present(obj, field)) return fallback;
            double value = Numeric(obj[field], field);
            Need(value >= minimum && value <= maximum, field + " is outside its supported range");
            return value;
        }

        private static double Numeric(JToken token, string field)
        {
            Need(token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float),
                field + " must be numeric");
            double value = token.Value<double>();
            Need(!double.IsNaN(value) && !double.IsInfinity(value), field + " must be finite");
            return value;
        }

        private static int Integer(JObject obj, string field, int minimum, int maximum)
        {
            var token = obj[field];
            Need(token != null && token.Type == JTokenType.Integer, field + " must be an integer");
            long value = token.Value<long>();
            Need(value >= minimum && value <= maximum, field + " is outside the table bounds");
            return (int)value;
        }

        private static bool Boolean(JObject obj, string field, bool fallback)
        {
            if (!Present(obj, field)) return fallback;
            Need(obj[field].Type == JTokenType.Boolean, field + " must be boolean");
            return obj[field].Value<bool>();
        }

        private static JArray OptionalArray(JObject obj, string field)
        {
            if (!Present(obj, field)) return null;
            var array = obj[field] as JArray;
            Need(array != null, field + " must be an array");
            return array;
        }

        private static bool Present(JObject obj, string field)
            => obj[field] != null && obj[field].Type != JTokenType.Null;

        private static void Keys(JObject obj, params string[] allowed)
        {
            var known = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (var property in obj.Properties())
                Need(known.Contains(property.Name), "unsupported table property: " + property.Name);
        }

        private static void Need(bool condition, string error)
        {
            if (!condition) throw new ArgumentException(error);
        }
    }

    internal sealed class TableMerge
    {
        internal TableMerge(int top, int left, int bottom, int right)
        { Top = top; Left = left; Bottom = bottom; Right = right; }
        internal int Top { get; }
        internal int Left { get; }
        internal int Bottom { get; }
        internal int Right { get; }
    }

    internal sealed class TableCellFormat
    {
        internal TableCellFormat(int row, int column, double? textHeight, double? rotation, string alignment)
        { Row = row; Column = column; TextHeight = textHeight; Rotation = rotation; Alignment = alignment; }
        internal int Row { get; }
        internal int Column { get; }
        internal double? TextHeight { get; }
        internal double? Rotation { get; }
        internal string Alignment { get; }
    }
}
