// Modified by the CAD team (2026-10-09): text/style tools; see cad-bridge/docs/TEXT_TOOLS.md.
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Annotation
{
    internal static class AnnotationEntityFactory
    {
        private const double DefaultTextHeight = 2.5d;
        private const double DefaultTableRowHeight = 8d;
        private const double DefaultTableColumnWidth = 35d;

        internal static bool TryCreateText(JObject obj, out DBText entity, out string error,
            Database database = null, Transaction transaction = null)
        {
            entity = null;
            error = null;

            if (!AnnotationRequestValidation.TryReadDbText(obj, out var input, out error)) return false;

            DBText created = null;
            var ownsEntity = false;
            try
            {
                if (!TryResolveTextStyle(input.TextStyle, database, transaction, out var textStyleId, out error))
                    return false;
                created = new DBText();
                ownsEntity = true;
                if (database == null) created.SetDatabaseDefaults();
                else created.SetDatabaseDefaults(database);
                if (!textStyleId.IsNull) created.TextStyleId = textStyleId;
                created.TextString = input.Text;
                created.Position = new Point3d(input.X, input.Y, input.Z);
                created.Height = input.Height;
                created.Rotation = DegreesToRadians(input.RotationDegrees % 360d);
                if (input.WidthFactor.HasValue) created.WidthFactor = input.WidthFactor.Value;
                created.Color = Color.FromColorIndex(input.ColorIndex == 256 ? ColorMethod.ByLayer : ColorMethod.ByAci,
                    (short)input.ColorIndex);
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (System.Exception ex)
            {
                error = "failed to create text entity: " + ErrorSanitizer.Sanitize(ex.Message);
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static bool TryCreateMText(JObject obj, out MText entity, out string error,
            Database database = null, Transaction transaction = null)
        {
            entity = null;
            error = null;

            if (!AnnotationRequestValidation.TryReadMText(obj, out var input, out error)) return false;
            var appearance = input.Appearance;

            MText created = null;
            var ownsEntity = false;
            try
            {
                if (!TryResolveTextStyle(appearance.TextStyle, database, transaction, out var textStyleId, out error))
                    return false;
                created = new MText();
                ownsEntity = true;
                if (database == null) created.SetDatabaseDefaults();
                else created.SetDatabaseDefaults(database);
                if (!textStyleId.IsNull) created.TextStyleId = textStyleId;
                created.Location = new Point3d(appearance.X, appearance.Y, appearance.Z);
                created.TextHeight = appearance.Height;
                created.Rotation = DegreesToRadians(appearance.RotationDegrees % 360d);
                created.Width = input.Width;
                created.Color = Color.FromColorIndex(appearance.ColorIndex == 256 ? ColorMethod.ByLayer : ColorMethod.ByAci,
                    (short)appearance.ColorIndex);
                // Contents last avoids repeated native layout work while setting appearance.
                created.Contents = appearance.Text;

                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (System.Exception ex)
            {
                error = "failed to create mtext entity: " + ErrorSanitizer.Sanitize(ex.Message);
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        private static bool TryResolveTextStyle(string name, Database database, Transaction transaction,
            out ObjectId id, out string error)
        {
            id = ObjectId.Null;
            error = null;
            if (name == null) return true;
            if (database == null || transaction == null)
            {
                error = "text_style requires the active database transaction";
                return false;
            }
            var styles = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
            if (!styles.Has(name))
            {
                error = "text_style does not exist in the active drawing";
                return false;
            }
            id = styles[name];
            var style = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (style.IsErased || style.IsDependent || style.IsShapeFile || style.IsVertical ||
                style.Annotative == AnnotativeStates.True)
            {
                error = "text_style must be an existing local horizontal non-annotative text style";
                return false;
            }
            return true;
        }

        internal static bool TryCreateLeader(JObject obj, out MLeader entity, out string error)
        {
            entity = null;
            error = null;

            if (!TryParsePoints(obj["points"], out var points, out error))
            {
                return false;
            }

            MLeader created = null;
            var ownsEntity = false;
            try
            {
                created = new MLeader();
                ownsEntity = true;
                created.SetDatabaseDefaults();
                var leaderLineIndex = created.AddLeaderLine(ToPoint3d(points[0]));
                for (var i = 1; i < points.Count; i++)
                {
                    created.AddLastVertex(leaderLineIndex, ToPoint3d(points[i]));
                }

                var text = obj["text"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    created.ContentType = ContentType.MTextContent;
                    created.TextHeight = DefaultTextHeight;
                    var textLocation = ToPoint3d(points[points.Count - 1]);
                    created.TextLocation = textLocation;
                    using (var leaderText = new MText())
                    {
                        leaderText.SetDatabaseDefaults();
                        leaderText.Contents = text;
                        leaderText.Location = textLocation;
                        leaderText.TextHeight = DefaultTextHeight;
                        created.MText = leaderText;
                    }
                }

                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (System.Exception ex)
            {
                error = "failed to create leader entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static bool TryCreateTable(JObject obj, out Table entity, out string error)
        {
            entity = null;
            error = null;

            if (!CadWire.TryParsePoint(obj["insertion_point"], out var insertionPoint, out var pointError))
            {
                error = "insertion_point " + pointError;
                return false;
            }

            if (!TryReadPositiveInt(obj, "rows", out var rows, out error) ||
                !TryReadPositiveInt(obj, "columns", out var columns, out error))
            {
                return false;
            }

            var cells = obj["cells"] as JArray;
            if (!AnnotationRequestValidation.TryValidateTableShape(rows, columns, cells, out error))
            {
                return false;
            }

            Table created = null;
            var ownsEntity = false;
            try
            {
                created = new Table();
                ownsEntity = true;
                created.SetDatabaseDefaults();
                created.Position = ToPoint3d(insertionPoint);
                created.SetSize(rows, columns);
                created.SetRowHeight(DefaultTableRowHeight);
                created.SetColumnWidth(DefaultTableColumnWidth);
                FillCells(created, cells);
                created.GenerateLayout();
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (System.Exception ex)
            {
                error = "failed to create table entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);

        private static bool TryParsePoints(
            JToken token,
            out List<CadPointInput> points,
            out string error)
        {
            points = null;
            error = null;

            var array = token as JArray;
            if (array == null)
            {
                error = "points must be an array of point objects";
                return false;
            }

            var parsed = new List<CadPointInput>();
            for (var i = 0; i < array.Count; i++)
            {
                if (!CadWire.TryParsePoint(array[i], out var point, out var pointError))
                {
                    error = "points[" + i + "] " + pointError;
                    return false;
                }

                parsed.Add(point);
            }

            if (!AnnotationRequestValidation.TryValidateLeaderPointCount(parsed.Count, out error))
            {
                return false;
            }

            points = parsed;
            return true;
        }

        private static void FillCells(Table table, JArray cells)
        {
            for (var row = 0; row < cells.Count; row++)
            {
                var cellRow = (JArray)cells[row];
                for (var column = 0; column < cellRow.Count; column++)
                {
                    table.Cells[row, column].TextString = CellToString(cellRow[column]);
                }
            }
        }

        private static string CellToString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            return token.ToString(Formatting.None);
        }

        private static bool TransferToCaller<T>(T created, out T entity, ref bool ownsEntity)
            where T : DBObject
        {
            entity = created;
            // Caller owns the entity after a successful factory return.
            ownsEntity = false;
            return true;
        }

        private static void DisposeIfOwned(DBObject entity, bool ownsEntity)
        {
            if (ownsEntity)
            {
                entity?.Dispose();
            }
        }

        private static bool TryReadPositiveInt(
            JObject obj,
            string fieldName,
            out int value,
            out string error)
        {
            value = 0;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                error = fieldName + " is required";
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                error = fieldName + " must be a positive integer";
                return false;
            }

            value = token.Value<int>();
            if (value <= 0)
            {
                error = fieldName + " must be a positive integer";
                return false;
            }

            return true;
        }

        private static bool TryReadOptionalPositiveDouble(
            JObject obj,
            string fieldName,
            double fallback,
            out double value,
            out string error)
        {
            if (!TryReadOptionalFiniteDouble(obj, fieldName, fallback, out value, out error))
            {
                return false;
            }

            if (obj[fieldName] != null && obj[fieldName].Type != JTokenType.Null && value <= 0d)
            {
                error = fieldName + " must be a finite positive number";
                return false;
            }

            return true;
        }

        private static bool TryReadOptionalFiniteDouble(
            JObject obj,
            string fieldName,
            double fallback,
            out double value,
            out string error)
        {
            value = fallback;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = fieldName + " must be numeric";
                return false;
            }

            value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = fieldName + " must be finite";
                return false;
            }

            return true;
        }

        private static double DegreesToRadians(double degrees)
            => degrees * System.Math.PI / 180d;
    }
}
