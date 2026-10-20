using System;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    /// <summary>Reads native angular annotation from the reference's current evaluated definition.</summary>
    internal static class BlockAngularDimensionReader
    {
        internal const int BudgetMilliseconds = 5000;
        internal const int MaximumResponseBytes = 256 * 1024;
        internal const int MaximumStringCharacters = 8192;

        internal static JObject ReadCurrent(Transaction transaction, BlockReference reference, int maxEntities = 500)
            => ReadCurrent(transaction, reference, maxEntities, null);

        // The inspector supplies its shared deadline; binding readers still use the local deadline.
        internal static JObject ReadCurrent(Transaction transaction, BlockReference reference,
            int maxEntities, Func<bool> continueInspection)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (maxEntities < 1 || maxEntities > 5000) throw new ArgumentOutOfRangeException(nameof(maxEntities));
            var watch = Stopwatch.StartNew();
            var dimensions = new JArray();
            var errors = new JArray();
            var complete = true;
            var stringsTruncated = false;
            var scanned = 0;
            var matching = 0;
            var itemBytes = 0;
            string stopReason = null;
            string evaluatedHandle = null;
            try
            {
                var definitionId = reference.BlockTableRecord;
                if (definitionId.IsNull || definitionId.IsErased || definitionId.IsEffectivelyErased)
                    throw new InvalidOperationException("current evaluated definition is erased or unavailable");
                evaluatedHandle = definitionId.Handle.ToString();
                var definition = transaction.GetObject(definitionId, OpenMode.ForRead) as BlockTableRecord;
                if (definition == null || definition.IsLayout || definition.IsDependent ||
                    definition.IsFromExternalReference || definition.IsFromOverlayReference)
                    throw new InvalidOperationException("current evaluated definition is outside the local block scope");

                foreach (ObjectId id in definition)
                {
                    if (scanned >= maxEntities)
                    { complete = false; stopReason = "entity_limit"; break; }
                    if (watch.ElapsedMilliseconds >= BudgetMilliseconds ||
                        (continueInspection != null && !continueInspection()))
                    { complete = false; stopReason = "time_budget"; break; }
                    scanned++;
                    try
                    {
                        if (id.IsNull || id.IsErased || id.IsEffectivelyErased)
                            throw new InvalidOperationException("direct definition entity is erased or unavailable");
                        var entity = transaction.GetObject(id, OpenMode.ForRead);
                        if (entity.OwnerId != definitionId)
                            throw new InvalidOperationException("entity is not owned by the current evaluated definition");
                        if (!(entity is LineAngularDimension2) && !(entity is Point3AngularDimension)) continue;
                        matching++;
                        var item = Describe((Dimension)entity, ref stringsTruncated);
                        var bytes = Encoding.UTF8.GetByteCount(item.ToString(Formatting.None));
                        if (itemBytes + bytes > MaximumResponseBytes - 16 * 1024)
                        { complete = false; stopReason = "response_budget"; break; }
                        itemBytes += bytes;
                        dimensions.Add(item);
                    }
                    catch (Exception exception)
                    {
                        complete = false;
                        AddError(errors, id.IsNull ? null : id.Handle.ToString(), exception);
                    }
                }
            }
            catch (Exception exception)
            {
                complete = false;
                AddError(errors, evaluatedHandle, exception);
            }
            if (stringsTruncated) complete = false;
            string unique = null;
            string unsupported = null;
            if (!complete) unsupported = "angular dimension inspection is incomplete";
            else if (matching == 0) unsupported = "current evaluated definition has no direct angular dimension";
            else if (matching != 1) unsupported = "current evaluated definition has multiple direct angular dimensions";
            else if (dimensions.Count == 1 && dimensions[0].Value<bool>("numeric_supported"))
                unique = dimensions[0].Value<string>("numeric_text");
            else if (dimensions.Count == 1) unsupported = dimensions[0].Value<string>("numeric_unsupported_reason");
            else unsupported = "angular dimension record is unavailable";

            var result = new JObject
            {
                ["requested"] = true, ["reference_handle"] = reference.Handle.ToString(),
                ["evaluated_definition_handle"] = evaluatedHandle,
                ["scope"] = "direct angular dimensions of the current evaluated BlockTableRecord; definition-local coordinates",
                ["selector"] = "unique_direct_angular_dimension",
                ["matching_count"] = matching, ["complete"] = complete,
                ["dimensions"] = dimensions, ["unique_numeric_text"] = unique,
                ["numeric_unsupported_reason"] = unsupported, ["error"] = unsupported,
                ["scanned_entities"] = scanned, ["max_entities"] = maxEntities,
                ["strings_truncated"] = stringsTruncated, ["stop_reason"] = stopReason,
                ["errors"] = errors, ["budget_ms"] = BudgetMilliseconds,
                ["elapsed_ms"] = watch.ElapsedMilliseconds, ["response_limit_bytes"] = MaximumResponseBytes,
                ["display_note"] = "Native FormatMeasurement uses current dimension settings. Cached render blocks are not traversed or regenerated; no fields are evaluated."
            };
            if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaximumResponseBytes)
            {
                result["dimensions"] = new JArray(); result["unique_numeric_text"] = null;
                result["complete"] = false; result["stop_reason"] = "response_budget";
                result["numeric_unsupported_reason"] = "angular dimension response exceeded its byte budget";
                result["error"] = result["numeric_unsupported_reason"];
            }
            return result;
        }

        private static JObject Describe(Dimension dimension, ref bool stringsTruncated)
        {
            var measurement = dimension.Measurement;
            RequireFinite(measurement);
            var degrees = measurement * 180d / Math.PI;
            RequireFinite(degrees);
            var text = BoundText(dimension.DimensionText, ref stringsTruncated);
            var hadFields = dimension.HasFields;
            var item = new JObject
            {
                ["handle"] = dimension.Handle.ToString(), ["owner_handle"] = dimension.OwnerId.Handle.ToString(),
                ["type"] = dimension is LineAngularDimension2 ? "LineAngularDimension2" : "Point3AngularDimension",
                ["measurement_raw"] = measurement, ["measurement_radians"] = measurement,
                ["measurement_degrees"] = degrees, ["dimension_text"] = text,
                ["has_fields"] = hadFields, ["dimaunit"] = dimension.Dimaunit,
                ["dimadec"] = dimension.Dimadec, ["dimdec"] = dimension.Dimdec,
                ["dimazin"] = dimension.Dimazin, ["dimtol"] = dimension.Dimtol,
                ["dimlim"] = dimension.Dimlim, ["normal"] = Vector(dimension.Normal),
                ["numeric_supported"] = false, ["numeric_text"] = null
            };
            if (dimension is LineAngularDimension2 lines)
            {
                item["xline1_start"] = Point(lines.XLine1Start); item["xline1_end"] = Point(lines.XLine1End);
                item["xline2_start"] = Point(lines.XLine2Start); item["xline2_end"] = Point(lines.XLine2End);
                item["arc_point"] = Point(lines.ArcPoint);
            }
            else if (dimension is Point3AngularDimension points)
            {
                item["center_point"] = Point(points.CenterPoint);
                item["xline1_point"] = Point(points.XLine1Point); item["xline2_point"] = Point(points.XLine2Point);
                item["arc_point"] = Point(points.ArcPoint);
            }
            string formatted = null, plain = null;
            // Native fields may carry evaluators: never invoke formatting on a field-bearing dimension.
            if (!hadFields)
            {
                // Do not format a truncated override as if it were the complete native annotation.
                if (dimension.DimensionText == text)
                {
                    var nativeFormatted = dimension.FormatMeasurement(measurement, dimension.DimensionText);
                    formatted = BoundText(nativeFormatted, ref stringsTruncated);
                    if (nativeFormatted != null && nativeFormatted.Length <= MaximumStringCharacters)
                        using (var transient = new MText())
                        {
                            // This MText is never added to a database; Contents only supplies native text decoding.
                            transient.Contents = formatted;
                            plain = BoundText(transient.Text, ref stringsTruncated);
                        }
                }
            }
            item["formatted_mtext"] = formatted; item["formatted_plain_text"] = plain;
            string reason = null;
            if (hadFields) reason = "dimension contains a native field";
            else if (dimension.Dimaunit != 0) reason = "numeric binding requires decimal-degree angular units";
            else if (dimension.Dimtol || dimension.Dimlim) reason = "dimension contains tolerances or limits";
            else if (!string.IsNullOrEmpty(text) && text.Trim() != "<>")
                reason = "dimension annotation contains a literal or mixed text override";
            else if (stringsTruncated) reason = "dimension text or formatting is truncated";
            else if (!NumericAngleTextPolicy.TryGetNumeric(plain, out var numeric, out var numericError))
                reason = numericError;
            else { item["numeric_supported"] = true; item["numeric_text"] = numeric; }
            item["numeric_unsupported_reason"] = reason;
            return item;
        }

        private static string BoundText(string value, ref bool stringsTruncated)
        {
            if (value == null || value.Length <= MaximumStringCharacters) return value;
            stringsTruncated = true;
            return value.Substring(0, MaximumStringCharacters);
        }
        private static JObject Point(Point3d point)
        {
            RequireFinite(point.X); RequireFinite(point.Y); RequireFinite(point.Z);
            return new JObject { ["x"] = point.X, ["y"] = point.Y, ["z"] = point.Z };
        }
        private static JObject Vector(Vector3d vector)
        {
            RequireFinite(vector.X); RequireFinite(vector.Y); RequireFinite(vector.Z);
            return new JObject { ["x"] = vector.X, ["y"] = vector.Y, ["z"] = vector.Z };
        }
        private static void RequireFinite(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new InvalidOperationException("angular dimension returned a non-finite scalar");
        }
        private static void AddError(JArray errors, string handle, Exception exception)
        {
            if (errors.Count >= 16) return;
            var error = ErrorSanitizer.Sanitize(exception.Message);
            errors.Add(new JObject { ["handle"] = handle,
                ["error"] = error.Length <= 1024 ? error : error.Substring(0, 1024) });
        }
    }
}
