using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.EntityBatch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Tables
{
    // Native calls run only under DocumentInvoker's main-thread document lock.
    internal static class TableService
    {
        private const int BudgetMilliseconds = 15000;
        private const int MaximumResponseBytes = 4 * 1024 * 1024;

        internal static JObject Create(Document doc, TableInput input)
        {
            Require(doc != null, "an active document is required");
            Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
            Require(!doc.IsReadOnly, "drawing is read-only");
            Require(doc.Editor.IsQuiescent, "finish the active AutoCAD command before table creation");
            var db = doc.Database;
            Require(EntityBatchInput.ValidateUndoState(db.UndoRecording,
                Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
            var watch = Stopwatch.StartNew();
            Table created = null;
            bool resident = false, commitAttempted = false;
            string stage = "preflight";
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var owner = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    Require(LocalLayout(owner), "current drawing space must be a local writable layout");
                    var layers = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                    Require(layers.Has(input.Layer), "existing target layer not found: " + input.Layer);
                    var layer = (LayerTableRecord)tx.GetObject(layers[input.Layer], OpenMode.ForRead);
                    Require(!layer.IsLocked && !layer.IsDependent && !layer.IsErased,
                        "target layer must be local and unlocked");
                    var styles = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    Require(styles.Has(input.TextStyle), "existing text style not found: " + input.TextStyle);
                    var style = (TextStyleTableRecord)tx.GetObject(styles[input.TextStyle], OpenMode.ForRead);
                    Require(!style.IsErased && !style.IsDependent && !style.IsShapeFile &&
                        !style.IsVertical && style.Annotative != AnnotativeStates.True,
                        "text style must be local, horizontal and non-annotative");
                    Require(!db.Tablestyle.IsNull, "the current table style is unavailable");
                    var tableStyle = tx.GetObject(db.Tablestyle, OpenMode.ForRead) as TableStyle;
                    Require(tableStyle != null && !tableStyle.IsErased, "the current table style is unavailable");
                    string dataCellStyle = ResolveDataCellStyle(tableStyle);
                    Time(watch);
                    if (input.DryRun)
                        return new JObject
                        {
                            ["dry_run"] = true, ["committed"] = false, ["saved"] = false,
                            ["rows"] = input.Rows, ["columns"] = input.Columns, ["cell_style"] = dataCellStyle,
                            ["width"] = input.ColumnWidths.Sum(), ["height"] = input.RowHeights.Sum(),
                            ["native_layout_checked"] = false,
                            ["note"] = "Native layout is checked during creation; text fit requires visual review. No entities were constructed."
                        };

                    stage = "new table";
                    created = new Table();
                    created.SetDatabaseDefaults(db);
                    created.TableStyle = tableStyle.ObjectId;
                    created.LayerId = layer.ObjectId;
                    created.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    created.LinetypeId = db.ByLayerLinetype;
                    created.LineWeight = LineWeight.ByLayer;
                    created.Normal = Vector3d.ZAxis;
                    created.Position = new Point3d(input.Position.X, input.Position.Y, input.Position.Z);
                    double radians = Radians(input.Rotation);
                    created.Direction = new Vector3d(Math.Cos(radians), Math.Sin(radians), 0);
                    stage = "flow";
                    // TABLE wraps AcDb::FlowDirection (0/1), not the managed MText enum (3/4).
                    created.FlowDirection = (FlowDirection)TableFlowDirectionValue.TopToBottom;
                    stage = "shape";
                    created.SetSize(input.Rows, input.Columns);
                    stage = "unmerge defaults";
                    UnmergeDefaults(created);

                    for (int row = 0; row < input.Rows; row++)
                    {
                        Time(watch);
                        for (int column = 0; column < input.Columns; column++)
                        {
                            stage = "cell initialization " + row + "," + column;
                            var cell = created.Cells[row, column];
                            cell.DataType = new DataTypeParameter(DataType.String, UnitType.Unitless);
                            cell.DataFormat = string.Empty;
                            // Even an empty TextString can reset native appearance to the table style.
                            cell.TextString = EncodeText(input.Cells[row][column]);
                            cell.Style = dataCellStyle;
                            cell.IsMergeAllEnabled = false;
                            cell.IsBackgroundColorNone = true;
                            cell.Alignment = CellAlignment.MiddleCenter;
                            cell.TextStyleId = style.ObjectId;
                            cell.TextHeight = input.TextHeight;
                            cell.Borders.Left.Margin = input.HorizontalMargin;
                            cell.Borders.Right.Margin = input.HorizontalMargin;
                            cell.Borders.Top.Margin = input.VerticalMargin;
                            cell.Borders.Bottom.Margin = input.VerticalMargin;
                            cell.DataType = new DataTypeParameter(DataType.String, UnitType.Unitless);
                            cell.DataFormat = string.Empty;
                            if (cell.Contents.Count > 0)
                            {
                                cell.Contents[0].DataType = new DataTypeParameter(DataType.String, UnitType.Unitless);
                                cell.Contents[0].DataFormat = string.Empty;
                                cell.Contents[0].TextStyleId = style.ObjectId;
                                cell.Contents[0].TextHeight = input.TextHeight;
                                cell.Contents[0].Rotation = 0;
                            }
                        }
                    }
                    stage = "merges";
                    foreach (var merge in input.Merges)
                        created.MergeCells(CellRange.Create(created, merge.Top, merge.Left, merge.Bottom, merge.Right));
                    stage = "formats";
                    foreach (var format in input.Formats)
                    {
                        stage = "format " + format.Row + "," + format.Column;
                        var cell = created.Cells[format.Row, format.Column];
                        if (format.Alignment != null) cell.Alignment = Alignment(format.Alignment);
                        if (format.TextHeight.HasValue) cell.TextHeight = format.TextHeight.Value;
                        if (cell.Contents.Count > 0)
                        {
                            if (format.TextHeight.HasValue) cell.Contents[0].TextHeight = format.TextHeight.Value;
                            if (format.Rotation.HasValue) cell.Contents[0].Rotation = Radians(format.Rotation.Value);
                        }
                    }
                    // Apply exact sizes last: any native minimum-size growth must be detected below.
                    stage = "sizes";
                    for (int column = 0; column < input.Columns; column++)
                        created.Columns[column].Width = input.ColumnWidths[column];
                    for (int row = 0; row < input.Rows; row++)
                        created.Rows[row].Height = input.RowHeights[row];
                    Time(watch);
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
                    Require(EntityBatchInput.ValidateUndoState(db.UndoRecording,
                        Convert.ToInt32(Application.GetSystemVariable("UNDOCTL"))));
                    stage = "append";
                    owner.UpgradeOpen();
                    owner.AppendEntity(created);
                    tx.AddNewlyCreatedDBObject(created, true);
                    resident = true;
                    stage = "layout";
                    created.GenerateLayout();
                    // GenerateLayout alone does not create/update the table's display block.
                    stage = "recompute table block";
                    created.RecomputeTableBlock(true);
                    Time(watch);
                    stage = "describe";
                    var result = Describe(created, tx, watch);
                    stage = "verify";
                    VerifyCreated(created, input, result, style.ObjectId, dataCellStyle);
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
                    result["dry_run"] = false;
                    result["committed"] = true;
                    result["atomic"] = true;
                    result["saved"] = false;
                    result["document_path"] = input.ExpectedDocument;
                    result["fingerprint"] = input.ExpectedFingerprint;
                    result["single_undo_verified"] = false;
                    result["text_fit_verified"] = false;
                    CheckResponse(result);
                    Time(watch);
                    stage = "commit";
                    commitAttempted = true;
                    tx.Commit();
                    return result;
                }
            }
            catch (Exception ex)
            {
                string error = "TABLE creation stage " + stage + ": " + ex.Message;
                if (commitAttempted) error += "; Commit was attempted; inspect actual drawing state before retrying";
                throw new InvalidOperationException(error, ex);
            }
            finally { if (created != null && !resident) created.Dispose(); }
        }

        internal static JObject Read(Document doc, JObject parameters)
        {
            Require(doc != null, "an active document is required");
            Require(parameters != null, "params must be an object");
            foreach (var field in parameters.Properties())
                Require(field.Name == "handle" || field.Name == "expected_document" ||
                    field.Name == "expected_fingerprint", "unsupported read_table field: " + field.Name);
            string handle = RequiredString(parameters, "handle");
            string expectedDocument = RequiredString(parameters, "expected_document");
            string expectedFingerprint = RequiredString(parameters, "expected_fingerprint");
            Require(handle.Length <= 16, "handle must contain at most 16 hexadecimal characters");
            Require(CadWire.TryParseHandleValue(handle, out var handleValue, out var handleError), handleError);
            Require(handleValue > 0, "handle must be a positive hexadecimal identifier");
            Require(DocumentGuard.Validate(doc, expectedDocument, expectedFingerprint));
            var watch = Stopwatch.StartNew();
            JObject result;
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                ObjectId id;
                Require(doc.Database.TryGetObjectId(new Handle(handleValue), out id) &&
                    !id.IsNull && !id.IsErased && !id.IsEffectivelyErased, "table handle was not found");
                var table = tx.GetObject(id, OpenMode.ForRead) as Table;
                Require(table != null, "handle does not identify a native Table");
                var owner = tx.GetObject(table.OwnerId, OpenMode.ForRead) as BlockTableRecord;
                Require(owner != null && LocalLayout(owner),
                    "table must belong directly to a local drawing layout; block definitions and xrefs are outside this reader's scope");
                result = Describe(table, tx, watch);
                // Read-only: no setters, GenerateLayout, evaluation, UpgradeOpen or Commit.
            }
            Require(DocumentGuard.Validate(doc, expectedDocument, expectedFingerprint));
            result["read_only"] = true;
            result["document_path"] = expectedDocument;
            result["fingerprint"] = expectedFingerprint;
            result["text_fit_verified"] = false;
            CheckResponse(result);
            return result;
        }

        private static JObject Describe(Table table, Transaction tx, Stopwatch watch)
        {
            int rows = table.Rows.Count, columns = table.Columns.Count;
            Require(rows > 0 && columns > 0 && (long)rows * columns <= TableInput.MaxCells,
                "table must contain 1-10000 cells");
            var rowHeights = new JArray();
            var widths = new JArray();
            for (int row = 0; row < rows; row++) rowHeights.Add(Finite(table.Rows[row].Height));
            for (int column = 0; column < columns; column++) widths.Add(Finite(table.Columns[column].Width));
            var cells = new JArray();
            var formats = new JArray();
            var merges = new JArray();
            var seenMerges = new HashSet<string>(StringComparer.Ordinal);
            var nativeMerges = new List<TableMerge>();
            // Only anchors may report IsMerged=true. Build coverage before reading cell appearance.
            for (int row = 0; row < rows; row++)
            {
                Time(watch);
                for (int column = 0; column < columns; column++)
                    if (NativeValue(table.Cells[row, column].IsMerged, "cell merge state"))
                    {
                        var range = table.Cells[row, column].GetMergeRange();
                        Require(range != null && !range.IsNull, "native merged range is unavailable");
                        Require(range.TopRow <= row && row <= range.BottomRow &&
                            range.LeftColumn <= column && column <= range.RightColumn,
                            "native merged range does not contain its reporting cell");
                        string key = range.TopRow + ":" + range.LeftColumn + ":" + range.BottomRow + ":" + range.RightColumn;
                        if (!seenMerges.Add(key)) continue;
                        nativeMerges.Add(new TableMerge(range.TopRow, range.LeftColumn, range.BottomRow, range.RightColumn));
                        merges.Add(new JObject
                        {
                            ["top"] = range.TopRow, ["left"] = range.LeftColumn,
                            ["bottom"] = range.BottomRow, ["right"] = range.RightColumn
                        });
                    }
            }
            var cellMap = new TableCellMap(rows, columns, nativeMerges);
            var styleNames = new Dictionary<ObjectId, string>();
            int textCount = 0;
            for (int row = 0; row < rows; row++)
            {
                Time(watch);
                var cellRow = new JArray();
                for (int column = 0; column < columns; column++)
                {
                    try
                    {
                        var cell = table.Cells[row, column];
                        Require(!NativeValue(cell.IsLinked, "cell link state") && cell.DataLink.IsNull,
                            "data-linked table cells are outside the plain-text reader's scope");
                        Require(cell.Contents.Count <= 1,
                            "multiple-content table cells are outside the plain-text reader's scope");
                        if (cell.Contents.Count > 0)
                        {
                            var content = cell.Contents[0];
                            Require(content.ContentTypes == CellContentTypes.Value &&
                                content.FieldId.IsNull && !content.HasFormula,
                                "block, field and formula table cells are outside the plain-text reader's scope");
                            Require(PlainStringType(content.DataType) && string.IsNullOrEmpty(content.DataFormat),
                                "numeric, non-string or formatted-value table cells are outside the plain-text reader's scope");
                        }
                        string text = DecodeText(cell.TextString ?? string.Empty);
                        textCount += text.Length;
                        Require(textCount <= TableInput.MaxTextCharacters, "table text exceeds 200000 characters");
                        cellRow.Add(text);
                        if (cellMap.IsNonAnchor(row, column))
                        {
                            Require(text.Length == 0 && (cell.Contents.Count == 0 ||
                                string.IsNullOrEmpty(cell.Contents[0].TextString)),
                                "merged non-anchor contains hidden text");
                            // A non-anchor may retain one empty plain content, but has no independent appearance.
                            formats.Add(new JObject
                            {
                                ["row"] = row, ["column"] = column, ["is_merged_non_anchor"] = true,
                                ["merge_anchor"] = new JObject
                                {
                                    ["row"] = cellMap.AnchorRow(row, column),
                                    ["column"] = cellMap.AnchorColumn(row, column)
                                }
                            });
                            continue;
                        }
                        ObjectId styleId = NativeValue(cell.TextStyleId, "cell text style");
                        if (!styleNames.TryGetValue(styleId, out var styleName))
                        {
                            var style = styleId.IsNull ? null : tx.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord;
                            styleName = style?.Name;
                            styleNames.Add(styleId, styleName);
                        }
                        double textHeight = cell.Contents.Count == 0 ? NativeValue(cell.TextHeight, "cell text height") : cell.Contents[0].TextHeight;
                        double rotation = cell.Contents.Count == 0 ? 0 : cell.Contents[0].Rotation * 180 / Math.PI;
                        formats.Add(new JObject
                        {
                            ["row"] = row, ["column"] = column, ["is_merged_non_anchor"] = false,
                            ["text_height"] = Finite(textHeight),
                            ["rotation"] = Finite(rotation), ["alignment"] = AlignmentName(NativeValue(cell.Alignment, "cell alignment")),
                            ["text_style"] = styleName, ["cell_style"] = cell.Style,
                            ["horizontal_margin_left"] = NullableNumber(cell.Borders.Left.Margin),
                            ["horizontal_margin_right"] = NullableNumber(cell.Borders.Right.Margin),
                            ["vertical_margin_top"] = NullableNumber(cell.Borders.Top.Margin),
                            ["vertical_margin_bottom"] = NullableNumber(cell.Borders.Bottom.Margin)
                        });
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("TABLE readback cell " + row + "," + column + ": " + ex.Message, ex);
                    }
                }
                cells.Add(cellRow);
            }
            var bounds = table.GeometricExtents;
            var result = new JObject
            {
                ["handle"] = table.Handle.ToString(), ["type"] = "Table",
                ["layer"] = table.Layer, ["rows"] = rows, ["columns"] = columns,
                ["insertion_point"] = Point(table.Position), ["direction"] = Vector(table.Direction),
                ["normal"] = Vector(table.Normal),
                ["flow_direction"] = TableFlowDirectionValue.Name((int)table.FlowDirection),
                ["width"] = Finite(table.Width), ["height"] = Finite(table.Height),
                ["column_widths"] = widths, ["row_heights"] = rowHeights,
                ["cells"] = cells, ["cell_formats"] = formats, ["merges"] = merges,
                ["bounds"] = new JObject { ["min"] = Point(bounds.MinPoint), ["max"] = Point(bounds.MaxPoint) },
                ["complete"] = true, ["text_characters"] = textCount
            };
            CheckResponse(result);
            return result;
        }

        private static void VerifyCreated(Table table, TableInput input, JObject result, ObjectId styleId, string dataCellStyle)
        {
            Require(table.Rows.Count == input.Rows && table.Columns.Count == input.Columns,
                "native layout changed the requested table shape");
            for (int row = 0; row < input.Rows; row++)
                Require(Near(table.Rows[row].Height, input.RowHeights[row]),
                    "native layout changed row height at row " + row + "; table was not committed");
            for (int column = 0; column < input.Columns; column++)
                Require(Near(table.Columns[column].Width, input.ColumnWidths[column]),
                    "native layout changed column width at column " + column + "; table was not committed");
            Require(Near(table.Width, input.ColumnWidths.Sum()) && Near(table.Height, input.RowHeights.Sum()),
                "native layout changed the requested overall dimensions");
            var cellMap = new TableCellMap(input.Rows, input.Columns, input.Merges);
            var requestedFormats = input.Formats.ToDictionary(f => f.Row * input.Columns + f.Column);
            for (int row = 0; row < input.Rows; row++)
                for (int column = 0; column < input.Columns; column++)
                {
                    Require((string)result["cells"][row][column] == input.Cells[row][column],
                        "native cell text differs at " + row + "," + column);
                    if (cellMap.IsNonAnchor(row, column)) continue;
                    var cell = table.Cells[row, column];
                    requestedFormats.TryGetValue(row * input.Columns + column, out var format);
                    double height = format?.TextHeight ?? input.TextHeight;
                    double angle = format?.Rotation ?? 0;
                    CellAlignment alignment = format?.Alignment == null ? CellAlignment.MiddleCenter : Alignment(format.Alignment);
                    Require(cell.Style == dataCellStyle &&
                        PlainStringType(NativeValue(cell.DataType, "cell data type")) &&
                        string.IsNullOrEmpty(cell.DataFormat),
                        "native cell data type or format differs at " + row + "," + column);
                    Require(NativeValue(cell.Alignment, "cell alignment") == alignment &&
                        NativeValue(cell.TextStyleId, "cell text style") == styleId &&
                        Near(NativeValue(cell.TextHeight, "cell text height"), height),
                        "native cell appearance differs at " + row + "," + column);
                    if (cell.Contents.Count > 0)
                        Require(PlainStringType(cell.Contents[0].DataType) &&
                            string.IsNullOrEmpty(cell.Contents[0].DataFormat) &&
                            cell.Contents[0].TextStyleId == styleId && Near(cell.Contents[0].TextHeight, height) &&
                            AngleNear(cell.Contents[0].Rotation, Radians(angle)),
                            "native cell content appearance differs at " + row + "," + column);
                    Require(NearMargin(cell.Borders.Left.Margin, input.HorizontalMargin) &&
                        NearMargin(cell.Borders.Right.Margin, input.HorizontalMargin) &&
                        NearMargin(cell.Borders.Top.Margin, input.VerticalMargin) &&
                        NearMargin(cell.Borders.Bottom.Margin, input.VerticalMargin),
                        "native cell margins differ at " + row + "," + column);
                }
            var nativeMerges = (JArray)result["merges"];
            Require(nativeMerges.Count == input.Merges.Length, "native merge count differs from the request");
            foreach (var merge in input.Merges)
                Require(nativeMerges.Any(m => (int)m["top"] == merge.Top && (int)m["left"] == merge.Left &&
                    (int)m["bottom"] == merge.Bottom && (int)m["right"] == merge.Right),
                    "native merge ranges differ from the request");
            Require(table.Position.DistanceTo(new Point3d(input.Position.X, input.Position.Y, input.Position.Z)) <= 1e-6 &&
                table.Normal.IsEqualTo(Vector3d.ZAxis, new Tolerance(1e-9, 1e-9)),
                "native table origin or plane differs from the request");
            double radians = Radians(input.Rotation);
            Require(table.Direction.IsEqualTo(new Vector3d(Math.Cos(radians), Math.Sin(radians), 0),
                new Tolerance(1e-9, 1e-9)) && (int)table.FlowDirection == TableFlowDirectionValue.TopToBottom,
                "native table direction differs from the request");
            double width = input.ColumnWidths.Sum(), totalHeight = input.RowHeights.Sum();
            var x = table.Direction;
            var down = new Vector3d(x.Y, -x.X, 0);
            var origin = table.Position;
            var corners = new[] { origin, origin + x * width, origin + down * totalHeight,
                origin + x * width + down * totalHeight };
            var bounds = table.GeometricExtents;
            double tolerance = Math.Max(1e-5, Math.Max(width, totalHeight) * 1e-7);
            Require(bounds.MinPoint.X >= corners.Min(p => p.X) - tolerance &&
                bounds.MinPoint.Y >= corners.Min(p => p.Y) - tolerance &&
                bounds.MaxPoint.X <= corners.Max(p => p.X) + tolerance &&
                bounds.MaxPoint.Y <= corners.Max(p => p.Y) + tolerance &&
                Math.Abs(bounds.MinPoint.Z - origin.Z) <= tolerance &&
                Math.Abs(bounds.MaxPoint.Z - origin.Z) <= tolerance,
                "native display bounds exceed the requested table rectangle; table was not committed");
        }

        private static void UnmergeDefaults(Table table)
        {
            var ranges = new List<CellRange>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int row = 0; row < table.Rows.Count; row++)
                for (int column = 0; column < table.Columns.Count; column++)
                    if (NativeValue(table.Cells[row, column].IsMerged, "default cell merge state"))
                    {
                        var range = table.Cells[row, column].GetMergeRange();
                        Require(range != null && !range.IsNull, "default merged range is unavailable");
                        string key = range.TopRow + ":" + range.LeftColumn + ":" + range.BottomRow + ":" + range.RightColumn;
                        if (seen.Add(key)) ranges.Add(range);
                    }
            foreach (var range in ranges) table.UnmergeCells(range);
        }

        private static bool LocalLayout(BlockTableRecord owner) => owner.IsLayout && !owner.IsDependent &&
            !owner.IsFromExternalReference && !owner.IsFromOverlayReference;
        private static string RequiredString(JObject obj, string field)
        {
            Require(obj[field]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)obj[field]),
                field + " must be a nonempty string");
            string value = (string)obj[field];
            Require(value.Length <= 32768, field + " is too long");
            return value;
        }
        private static string EncodeText(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");
        private static string DecodeText(string text)
        {
            string plain = text.Replace("\\P", "\n").Replace("\r\n", "\n").Replace("\r", "\n");
            Require(plain.IndexOf('\\') < 0 && plain.IndexOf('{') < 0 && plain.IndexOf('}') < 0 &&
                plain.IndexOf('\0') < 0 && !plain.Contains("%<") && !plain.Contains("%%"),
                "formatted or field-bearing cell text is outside the plain-text reader's scope");
            return plain;
        }
        private static CellAlignment Alignment(string value)
        {
            if (value == "middle_left") return CellAlignment.MiddleLeft;
            if (value == "middle_right") return CellAlignment.MiddleRight;
            Require(value == "middle_center", "unsupported cell alignment");
            return CellAlignment.MiddleCenter;
        }
        private static string AlignmentName(CellAlignment value)
        {
            if (value == CellAlignment.MiddleLeft) return "middle_left";
            if (value == CellAlignment.MiddleRight) return "middle_right";
            if (value == CellAlignment.MiddleCenter) return "middle_center";
            return value.ToString();
        }
        private static string ResolveDataCellStyle(TableStyle tableStyle)
        {
            Require(tableStyle.CellStyles.Count <= 1024, "current table style has too many cell styles");
            var names = new List<string>();
            foreach (var item in tableStyle.CellStyles)
            {
                Require(item is string, "native cell style name is unavailable");
                string name = (string)item;
                if (tableStyle.CellClass(name) == CellClass.Data) names.Add(name);
            }
            // The documented default name is Data. Other/localized names come from native metadata only.
            if (names.Contains("Data", StringComparer.Ordinal)) return "Data";
            Require(names.Count == 1,
                "current table style must have Data or exactly one native Data-class cell style");
            return names[0];
        }
        private static bool PlainStringType(DataTypeParameter value) =>
            value.DataType == DataType.String && value.UnitType == UnitType.Unitless;
        private static T NativeValue<T>(T? value, string field) where T : struct
        {
            Require(value.HasValue, "native " + field + " is unavailable; no default was substituted");
            return value.Value;
        }
        private static double Radians(double degrees) => (degrees % 360d) * Math.PI / 180d;
        private static bool AngleNear(double first, double second) => Math.Abs(Math.IEEERemainder(first - second, 2 * Math.PI)) <= 1e-9;
        private static bool Near(double actual, double expected) => Math.Abs(Finite(actual) - expected) <= Math.Max(1e-6, Math.Abs(expected) * 1e-8);
        private static bool NearMargin(double? actual, double expected) => actual.HasValue && Near(actual.Value, expected);
        private static double Finite(double value) { Require(!double.IsNaN(value) && !double.IsInfinity(value), "native table contains a non-finite number"); return value; }
        private static JToken NullableNumber(double? value) => value.HasValue ? new JValue(Finite(value.Value)) : JValue.CreateNull();
        private static JObject Point(Point3d value) => new JObject { ["x"] = Finite(value.X), ["y"] = Finite(value.Y), ["z"] = Finite(value.Z) };
        private static JObject Vector(Vector3d value) => new JObject { ["x"] = Finite(value.X), ["y"] = Finite(value.Y), ["z"] = Finite(value.Z) };
        private static void CheckResponse(JObject value) => Require(Encoding.UTF8.GetByteCount(value.ToString(Formatting.None)) <= MaximumResponseBytes, "table readback exceeds 4 MiB UTF-8");
        private static void Time(Stopwatch watch) => Require(watch.ElapsedMilliseconds < BudgetMilliseconds, "15-second cooperative table budget exceeded");
        private static void Require(bool condition, string error) { if (!condition) throw new ArgumentException(error); }
        private static void Require(string error) { if (error != null) throw new ArgumentException(error); }
    }
}
