using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // This reader deliberately does not decode undocumented dynamic-block graph data.
    // Every DB object is opened ForRead; pointers found in Xrecords are only described.
    internal static class BlockInspectionService
    {
        internal const int MaxResponseBytes = 1024 * 1024;
        internal const int BudgetMilliseconds = 10000;
        internal const int MaxStringCharacters = 8192;
        internal const int MaxMetadataDepth = 4;
        internal const int MaxAllowedValues = 256;
        internal const int MaxPolylineVertices = 512;
        internal const int MaxXrecordValues = 256;

        internal static JObject Read(Document doc, BlockInspectionInput input)
        {
            var identity = DocumentGuard.Capture(doc);
            var state = new InspectionState(input);
            JObject reference;
            JObject definition;
            var evaluatedAngularDimensions = new JObject { ["requested"] = false, ["complete"] = false,
                ["reason"] = "not_requested", ["matching_count"] = 0, ["dimensions"] = new JArray(), ["unique_numeric_text"] = null };
            bool dynamic;
            using (var tx = doc.Database.TransactionManager.StartTransaction())
            {
                if (!BlockReferenceResolver.TryOpen(doc.Database, tx, input.Handle, OpenMode.ForRead,
                    out var block, out var error)) throw new InvalidOperationException(error);
                if (block.ObjectId.IsErased || block.ObjectId.IsEffectivelyErased)
                    throw new InvalidOperationException("block reference or its owner is erased");

                dynamic = block.IsDynamicBlock;
                var definitionId = dynamic ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                var record = tx.GetObject(definitionId, OpenMode.ForRead) as BlockTableRecord;
                if (record == null) throw new InvalidOperationException("block definition is unavailable");
                if (record.IsLayout) throw new InvalidOperationException("layout block definitions are outside the inspection scope");
                if (record.IsFromExternalReference || record.IsFromOverlayReference || record.IsDependent)
                    throw new InvalidOperationException("xref and dependent block definitions are outside the inspection scope");

                reference = ReferenceIdentity(block);
                reference["effective_name"] = state.Text(record.Name);
                reference["definition_handle"] = HandleOf(definitionId);
                reference["evaluated_definition_handle"] = HandleOf(block.BlockTableRecord);
                definition = new JObject
                {
                    ["handle"] = HandleOf(definitionId), ["name"] = state.Text(record.Name),
                    ["origin"] = Point(record.Origin), ["units"] = record.Units.ToString(),
                    ["is_dynamic"] = dynamic, ["is_anonymous"] = record.IsAnonymous,
                    ["coordinate_system"] = "block definition local coordinates; no insertion transform applied",
                    ["geometry_requested"] = input.IncludeDefinition
                };

                ReadAttributes(block, tx, state);
                if (dynamic) ReadDynamicProperties(block, state);
                ReadExtension(block, tx, "reference", state);
                if (input.IncludeDefinition)
                {
                    ReadDefinition(record, tx, state);
                    ReadExtension(record, tx, "definition", state);
                    evaluatedAngularDimensions = BlockAngularDimensionReader.ReadCurrent(tx, block, input.MaxEntities, state.Continue);
                    state.StringsTruncated |= evaluatedAngularDimensions.Value<bool>("strings_truncated");
                    if (!state.Reserve(evaluatedAngularDimensions))
                        evaluatedAngularDimensions = new JObject { ["requested"] = true, ["complete"] = false,
                            ["reason"] = "response_budget", ["dimensions"] = new JArray(), ["unique_numeric_text"] = null };
                    if (!evaluatedAngularDimensions.Value<bool>("complete"))
                        state.Warn("Current evaluated angular dimension inspection is incomplete; numeric annotation binding is unavailable.");
                }
                // No Commit: disposing this transaction closes only read-opened objects.
            }

            state.Trim(reference);
            state.Trim(definition);
            if (dynamic)
                state.Warn("The public managed reader reports graph object envelopes only; parameter/action graph contents and relationships are not decoded.");
            var result = new JObject
            {
                ["document"] = state.Text(identity.DocumentPath ?? identity.DocumentName),
                ["document_path"] = state.Text(identity.DocumentPath),
                ["document_name"] = state.Text(identity.DocumentName),
                ["fingerprint"] = state.Text(identity.Fingerprint),
                ["read_only"] = true, ["scope"] = "one reference, its direct effective definition and current evaluated angular dimensions; owned dictionary metadata only",
                ["complete"] = false,
                ["inspection_complete"] = false,
                ["graph_contents_complete"] = !dynamic && state.MetadataContentsComplete,
                ["reference"] = reference, ["definition"] = definition,
                ["attributes"] = state.Attributes, ["attribute_count"] = state.AttributeCount,
                ["attributes_complete"] = state.AttributesComplete,
                ["native_fields"] = state.Fields, ["native_fields_complete"] = state.FieldsComplete,
                ["native_field_count"] = state.Fields.Count,
                ["field_scope"] = "default text field of each inspected AttributeReference/AttributeDefinition and its native child fields; cached status only, no evaluation",
                ["dynamic_properties"] = state.Properties, ["dynamic_property_count"] = state.PropertyCount,
                ["dynamic_properties_complete"] = state.PropertiesComplete,
                ["definition_entities"] = state.Entities, ["definition_entities_complete"] = state.EntitiesComplete,
                ["evaluated_angular_dimensions"] = evaluatedAngularDimensions,
                ["metadata_objects"] = state.Metadata, ["metadata_complete"] = state.MetadataComplete,
                ["metadata_contents_complete"] = state.MetadataContentsComplete,
                ["strings_truncated"] = state.StringsTruncated,
                ["max_entities"] = input.MaxEntities, ["max_metadata_objects"] = input.MaxMetadataObjects,
                ["attribute_limit"] = state.AttributeLimit, ["dynamic_property_limit"] = state.PropertyLimit,
                ["allowed_values_limit"] = MaxAllowedValues, ["polyline_vertex_limit"] = MaxPolylineVertices,
                ["xrecord_value_limit"] = MaxXrecordValues, ["metadata_depth_limit"] = MaxMetadataDepth,
                ["response_limit_bytes"] = MaxResponseBytes, ["budget_ms"] = BudgetMilliseconds,
                ["elapsed_ms"] = state.Watch.ElapsedMilliseconds, ["stop_reason"] = state.StopReason,
                ["warnings"] = state.Warnings, ["errors"] = state.Errors,
                ["error_count"] = state.ErrorCount, ["errors_truncated"] = state.ErrorCount > state.Errors.Count,
                ["angle_units"] = "raw numeric angular values and entity rotations are radians; dynamic values also include degrees",
                ["limits_note"] = "The time deadline is cooperative between native API calls; it cannot interrupt a single blocked host call. Nested block definitions and arbitrary object references are not traversed."
            };
            // Identity strings above can also be truncated; derive completeness only
            // after all response fields have passed through the bounded scalar reader.
            var scopeComplete = state.AttributesComplete && state.PropertiesComplete && state.FieldsComplete &&
                state.EntitiesComplete && state.MetadataComplete && !state.StringsTruncated &&
                (!input.IncludeDefinition || evaluatedAngularDimensions.Value<bool>("complete")) &&
                state.ErrorCount == 0 && state.StopReason == null;
            result["inspection_complete"] = scopeComplete;
            result["complete"] = scopeComplete && !dynamic && state.MetadataContentsComplete;
            // Item accounting leaves 64 KiB for identity, flags and diagnostics. Guard the
            // final wire payload too, including pathological dictionary keys or escaping.
            if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxResponseBytes - 1024)
            {
                result["attributes"] = new JArray(); result["dynamic_properties"] = new JArray();
                result["definition_entities"] = new JArray(); result["metadata_objects"] = new JArray();
                result["native_fields"] = new JArray(); result["native_fields_complete"] = false;
                result["evaluated_angular_dimensions"] = new JObject { ["requested"] = input.IncludeDefinition,
                    ["complete"] = false, ["reason"] = "response_budget", ["dimensions"] = new JArray(), ["unique_numeric_text"] = null };
                result["native_field_count"] = 0;
                result["complete"] = false; result["inspection_complete"] = false;
                result["attributes_complete"] = false; result["dynamic_properties_complete"] = false;
                result["definition_entities_complete"] = false; result["metadata_complete"] = false;
                result["metadata_contents_complete"] = false; result["graph_contents_complete"] = false;
                result["stop_reason"] = "response_budget";
                state.Warn("Final response exceeded its byte budget; item arrays were omitted.");
            }
            return result;
        }

        private static JObject ReferenceIdentity(BlockReference block) => new JObject
        {
            ["handle"] = block.Handle.ToString(), ["owner_handle"] = HandleOf(block.OwnerId),
            ["position"] = Point(block.Position), ["normal"] = Vector(block.Normal),
            ["rotation_radians"] = block.Rotation,
            ["scale"] = new JObject { ["x"] = block.ScaleFactors.X, ["y"] = block.ScaleFactors.Y, ["z"] = block.ScaleFactors.Z },
            ["is_dynamic"] = block.IsDynamicBlock
        };

        private static void ReadAttributes(BlockReference block, Transaction tx, InspectionState state)
        {
            state.AttributeCount = block.AttributeCollection.Count;
            var attempts = 0;
            foreach (ObjectId id in block.AttributeCollection)
            {
                if (!state.Continue() || attempts++ >= state.AttributeLimit)
                { state.AttributesComplete = false; state.Warn("Attribute inspection reached its item or time/byte limit."); break; }
                try
                {
                    var attribute = OpenOwned(tx, id, block.ObjectId) as AttributeReference;
                    if (attribute == null) throw new InvalidOperationException("owned attribute is not an AttributeReference");
                    var item = new JObject
                    {
                        ["handle"] = attribute.Handle.ToString(), ["owner_handle"] = HandleOf(attribute.OwnerId),
                        ["tag"] = state.Text(attribute.Tag), ["text"] = state.Text(attribute.TextString),
                        ["value"] = state.Text(attribute.TextString), ["position"] = Point(attribute.Position),
                        ["alignment_point"] = Point(attribute.AlignmentPoint), ["rotation_radians"] = attribute.Rotation,
                        ["height"] = attribute.Height, ["invisible"] = attribute.Invisible,
                        ["constant"] = attribute.IsConstant, ["preset"] = attribute.IsPreset,
                        ["verifiable"] = attribute.IsVerifiable, ["lock_position_in_block"] = attribute.LockPositionInBlock,
                        ["is_mtext_attribute"] = attribute.IsMTextAttribute
                    };
                    if (attribute.IsMTextAttribute)
                    {
                        using (var mtext = attribute.MTextAttribute)
                            item["mtext_contents"] = mtext == null ? null : state.Text(mtext.Contents);
                    }
                    ReadAttributeFields(attribute, tx, item, state, "attribute_reference", attribute.Tag);
                    if (!state.Add(state.Attributes, item)) { state.AttributesComplete = false; break; }
                }
                catch (Exception ex) { state.AttributesComplete = false; state.Error(HandleOf(id), ex); }
            }
            state.AttributesComplete &= state.Attributes.Count == state.AttributeCount;
        }

        private static void ReadDynamicProperties(BlockReference block, InspectionState state)
        {
            using (var properties = block.DynamicBlockReferencePropertyCollection)
            {
                state.PropertyCount = properties.Count;
                var attempts = 0;
                foreach (DynamicBlockReferenceProperty property in properties)
                {
                    if (!state.Continue() || attempts++ >= state.PropertyLimit)
                    { state.PropertiesComplete = false; state.Warn("Dynamic property inspection reached its item or time/byte limit."); break; }
                    try
                    {
                        var value = property.Value;
                        var units = property.UnitsType.ToString();
                        var item = new JObject
                        {
                            ["name"] = state.Text(property.PropertyName), ["description"] = state.Text(property.Description),
                            ["type_code"] = property.PropertyTypeCode, ["value"] = Scalar(value, state),
                            ["value_type"] = value == null ? null : value.GetType().FullName,
                            ["read_only"] = property.ReadOnly, ["units_type"] = units,
                            ["raw_units"] = units == "Angular" ? "radians" : units == "Distance" ? "drawing_units" : units == "Area" ? "drawing_units_squared" : "unitless",
                            ["show"] = property.Show, ["visible_in_current_visibility_state"] = property.VisibleInCurrentVisibilityState
                        };
                        if (units == "Angular" && TryFiniteNumber(value, out var radians))
                        { item["value_radians"] = radians; item["value_degrees"] = radians * 180d / Math.PI; }
                        var allowed = property.GetAllowedValues();
                        var values = new JArray();
                        var allowedComplete = true;
                        if (allowed != null)
                            for (var i = 0; i < allowed.Length; i++)
                            {
                                if (!state.Continue() || i >= MaxAllowedValues) { allowedComplete = false; break; }
                                values.Add(Scalar(allowed[i], state));
                            }
                        item["allowed_values"] = values;
                        item["allowed_value_count"] = allowed?.Length ?? 0;
                        item["allowed_values_complete"] = allowedComplete;
                        item["allowed_values_note"] = "An empty list does not certify unrestricted values or expose minimum/maximum constraints.";
                        if (!allowedComplete) state.PropertiesComplete = false;
                        if (!state.Add(state.Properties, item)) { state.PropertiesComplete = false; break; }
                    }
                    catch (Exception ex) { state.PropertiesComplete = false; state.Error(block.Handle.ToString(), ex); }
                }
                state.PropertiesComplete &= state.Properties.Count == state.PropertyCount;
            }
        }

        private static void ReadDefinition(BlockTableRecord record, Transaction tx, InspectionState state)
        {
            var attempts = 0;
            foreach (ObjectId id in record)
            {
                if (!state.Continue() || attempts++ >= state.Input.MaxEntities)
                { state.EntitiesComplete = false; state.Warn("Direct definition geometry reached its item or time/byte limit."); break; }
                try
                {
                    var obj = OpenOwned(tx, id, record.ObjectId);
                    var item = Envelope(obj, state);
                    if (obj is Entity entity)
                    {
                        var safeGeometry = entity is Line || entity is Circle || entity is Arc || entity is DBPoint ||
                            entity is Ellipse || entity is DBText || entity is MText;
                        var geometry = JObject.FromObject(CadEntityProperties.Describe(entity, tx, safeGeometry));
                        if (entity is Circle circle)
                        { geometry["normal"] = Vector(circle.Normal); geometry["thickness"] = circle.Thickness; }
                        else if (entity is Arc arc)
                        { geometry["normal"] = Vector(arc.Normal); geometry["thickness"] = arc.Thickness; }
                        else if (entity is Ellipse ellipse)
                            geometry["normal"] = Vector(ellipse.Normal);
                        state.Trim(geometry);
                        if (entity is Polyline polyline) DescribePolyline(polyline, geometry, state);
                        else if (entity is BlockReference nested)
                        {
                            geometry.Merge(ReferenceIdentity(nested));
                            geometry["nested_definition_handle"] = HandleOf(nested.BlockTableRecord);
                            geometry["nested_definition_inspected"] = false;
                        }
                        else if (!safeGeometry)
                        {
                            geometry["geometry_complete"] = false;
                            state.EntitiesComplete = false;
                            state.Warn("Unsupported direct entity geometry is represented by its class and basic properties only.");
                        }
                        if (entity is AttributeDefinition attribute)
                        {
                            geometry["tag"] = state.Text(attribute.Tag); geometry["prompt"] = state.Text(attribute.Prompt);
                            geometry["default_text"] = state.Text(attribute.TextString);
                            geometry["constant"] = attribute.Constant; geometry["invisible"] = attribute.Invisible;
                            geometry["preset"] = attribute.Preset; geometry["verifiable"] = attribute.Verifiable;
                            geometry["lock_position_in_block"] = attribute.LockPositionInBlock;
                            geometry["is_mtext_attribute_definition"] = attribute.IsMTextAttributeDefinition;
                            if (attribute.IsMTextAttributeDefinition)
                            {
                                using (var mtext = attribute.MTextAttributeDefinition)
                                    geometry["mtext_default_contents"] = mtext == null ? null : state.Text(mtext.Contents);
                            }
                            ReadAttributeFields(attribute, tx, geometry, state, "attribute_definition", attribute.Tag);
                        }
                        item["geometry"] = geometry;
                    }
                    else { state.EntitiesComplete = false; item["geometry_complete"] = false; }
                    if (!state.Add(state.Entities, item)) { state.EntitiesComplete = false; break; }
                    ReadExtension(obj, tx, "definition_entity/" + obj.Handle, state);
                }
                catch (Exception ex) { state.EntitiesComplete = false; state.Error(HandleOf(id), ex); }
            }
        }

        private static void DescribePolyline(Polyline polyline, JObject item, InspectionState state)
        {
            item["vertex_count"] = polyline.NumberOfVertices; item["is_closed"] = polyline.Closed;
            item["normal"] = Vector(polyline.Normal); item["elevation"] = polyline.Elevation;
            item["constant_width"] = polyline.ConstantWidth; item["thickness"] = polyline.Thickness;
            var vertices = new JArray();
            var complete = true;
            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                if (!state.Continue() || i >= MaxPolylineVertices) { complete = false; break; }
                var point = polyline.GetPoint3dAt(i);
                vertices.Add(new JObject { ["index"] = i, ["point"] = Point(point),
                    ["bulge"] = polyline.GetBulgeAt(i), ["start_width"] = polyline.GetStartWidthAt(i),
                    ["end_width"] = polyline.GetEndWidthAt(i) });
            }
            item["vertices"] = vertices; item["vertices_complete"] = complete;
            if (!complete) state.EntitiesComplete = false;
        }

        private static void ReadAttributeFields(DBObject attribute, Transaction tx, JObject item,
            InspectionState state, string kind, string tag)
        {
            item["fields_complete"] = false;
            item["text_field_handle"] = null;
            try
            {
                var present = attribute.HasFields;
                item["fields_present"] = present;
                if (!present) { item["fields_complete"] = true; return; }
                if (!FieldBudget(state)) { state.FieldsComplete = false; return; }
                // GetField() retrieves the native default text field. No property-name
                // guessing, evaluation, or opening objects referenced by field code.
                var id = attribute.GetField();
                item["text_field_handle"] = HandleOf(id);
                if (id.IsNull) throw new InvalidOperationException("attribute reports fields but its default text field is unavailable");
                var complete = ReadField(id, tx, state, attribute.Handle.ToString(), kind, tag, null, 0);
                item["fields_complete"] = complete;
                if (!complete) state.FieldsComplete = false;
            }
            catch (Exception ex)
            {
                state.FieldsComplete = false;
                state.Error(attribute.Handle.ToString(), ex);
            }
        }

        private static bool ReadField(ObjectId id, Transaction tx, InspectionState state,
            string sourceHandle, string sourceKind, string sourceTag, string parentHandle, int depth)
        {
            if (!FieldBudget(state)) return false;
            state.MetadataAttempts++;
            if (!state.FieldVisited.Add(id))
            { state.Warn("Repeated native child field was not inspected twice."); return false; }
            try
            {
                if (id.IsNull || id.IsErased || id.IsEffectivelyErased)
                    throw new InvalidOperationException("native field is erased or unavailable");
                // These IDs come only from documented GetField/GetChildrenIds associations,
                // never from ObjectId values or strings embedded in the field expression.
                var field = tx.GetObject(id, OpenMode.ForRead) as Field;
                if (field == null) throw new InvalidOperationException("native field association does not resolve to a Field");
                var status = field.EvaluationStatus;
                var evaluatorId = field.EvaluatorId;
                var errorMessage = status.ErrorMessage;
                var metadataComplete = (evaluatorId == null || evaluatorId.Length <= MaxStringCharacters) &&
                    (errorMessage == null || errorMessage.Length <= MaxStringCharacters);
                var code = field.GetFieldCode(FieldCodeFlags.FieldCode | FieldCodeFlags.ObjectReference | FieldCodeFlags.AddMarkers);
                var codeComplete = code == null || code.Length <= MaxStringCharacters;
                var children = field.GetChildrenIds();
                var childCount = children?.Length ?? 0;
                var item = Envelope(field, state);
                item["source_attribute_handle"] = sourceHandle;
                item["source_attribute_kind"] = sourceKind;
                item["source_attribute_tag"] = state.Text(sourceTag);
                item["parent_field_handle"] = parentHandle;
                item["depth"] = depth;
                item["raw_field_code"] = state.Text(code);
                item["field_code_complete"] = codeComplete;
                item["field_code_flags"] = "FieldCode|ObjectReference|AddMarkers";
                item["evaluator_id"] = state.Text(evaluatorId);
                item["field_metadata_complete"] = metadataComplete;
                item["is_text_field"] = field.IsTextField;
                item["evaluation_option"] = field.EvaluationOption.ToString();
                item["evaluation_status"] = status.Status.ToString();
                item["evaluation_status_code"] = (int)status.Status;
                item["evaluation_error_code"] = status.ErrorCode;
                item["evaluation_error_message"] = state.Text(errorMessage);
                item["state"] = field.State.ToString();
                item["child_count"] = childCount;
                item["children_complete"] = childCount == 0;
                item["evaluated_by_inspection"] = false;
                item["expression_references_followed"] = false;
                if (!state.Add(state.Fields, item)) return false;
                if (childCount != 0 && depth >= MaxMetadataDepth)
                { state.Warn("Native field child depth limit reached."); return false; }
                var childrenComplete = true;
                if (children != null)
                    foreach (var child in children)
                    {
                        if (!FieldBudget(state)) { childrenComplete = false; break; }
                        childrenComplete &= ReadField(child, tx, state, sourceHandle, sourceKind, sourceTag,
                            field.Handle.ToString(), depth + 1);
                    }
                item["children_complete"] = childrenComplete;
                return codeComplete && metadataComplete && childrenComplete;
            }
            catch (Exception ex) { state.Error(HandleOf(id), ex); return false; }
        }

        private static bool FieldBudget(InspectionState state)
        {
            if (state.Continue() && state.MetadataAttempts < state.Input.MaxMetadataObjects) return true;
            state.MetadataComplete = false;
            state.Warn("Native field inspection shares the owned metadata object, time and byte budgets; fields were omitted at the limit.");
            return false;
        }

        private static void ReadExtension(DBObject owner, Transaction tx, string path, InspectionState state)
        {
            if (owner.ExtensionDictionary.IsNull) return;
            ReadMetadata(owner.ExtensionDictionary, owner.ObjectId, tx, path + "/extension_dictionary", 0, state);
        }

        private static void ReadMetadata(ObjectId id, ObjectId owner, Transaction tx, string path, int depth, InspectionState state)
        {
            if (!state.Continue() || state.MetadataAttempts >= state.Input.MaxMetadataObjects)
            { state.MetadataComplete = false; state.Warn("Owned metadata reached its item or time/byte limit."); return; }
            state.MetadataAttempts++;
            if (!state.MetadataVisited.Add(id))
            { state.MetadataComplete = false; state.Warn("Repeated owned metadata object was not traversed twice."); return; }
            try
            {
                var obj = OpenOwned(tx, id, owner);
                var item = Envelope(obj, state);
                item["path"] = state.Text(path); item["depth"] = depth;
                if (obj is DBDictionary dictionary)
                {
                    item["kind"] = "dictionary"; item["entry_count"] = dictionary.Count;
                    item["entries_complete"] = dictionary.Count == 0;
                    if (!state.Add(state.Metadata, item)) { state.MetadataComplete = false; return; }
                    if (dictionary.Count != 0 && depth >= MaxMetadataDepth)
                    { state.MetadataComplete = false; state.Warn("Owned dictionary depth limit reached."); return; }
                    var entries = 0;
                    foreach (DBDictionaryEntry entry in dictionary)
                    {
                        if (!state.Continue() || state.MetadataAttempts >= state.Input.MaxMetadataObjects)
                        { state.MetadataComplete = false; break; }
                        ReadMetadata(entry.Value, dictionary.ObjectId, tx, path + "/" + state.Text(entry.Key), depth + 1, state);
                        entries++;
                    }
                    item["entries_complete"] = entries == dictionary.Count;
                }
                else if (obj is Xrecord xrecord)
                {
                    item["kind"] = "xrecord";
                    var values = new JArray();
                    var complete = true;
                    // Enumerate the native record itself, avoiding an unbounded Data.AsArray().
                    foreach (TypedValue value in xrecord)
                    {
                        if (!state.Continue() || values.Count >= MaxXrecordValues) { complete = false; break; }
                        values.Add(new JObject { ["type_code"] = value.TypeCode, ["value"] = MetadataScalar(value.Value, state) });
                    }
                    item["values"] = values; item["values_complete"] = complete;
                    item["references_followed"] = false;
                    if (!complete) state.MetadataComplete = false;
                    if (!state.Add(state.Metadata, item)) { state.MetadataComplete = false; return; }
                }
                else
                {
                    item["kind"] = "native_object_envelope"; item["contents_complete"] = false;
                    state.MetadataContentsComplete = false;
                    if (!state.Add(state.Metadata, item)) { state.MetadataComplete = false; return; }
                }
                if (!obj.ExtensionDictionary.IsNull)
                {
                    if (depth >= MaxMetadataDepth) { state.MetadataComplete = false; return; }
                    ReadMetadata(obj.ExtensionDictionary, obj.ObjectId, tx, path + "/extension_dictionary", depth + 1, state);
                }
            }
            catch (Exception ex) { state.MetadataComplete = false; state.Error(HandleOf(id), ex); }
        }

        private static DBObject OpenOwned(Transaction tx, ObjectId id, ObjectId expectedOwner)
        {
            if (id.IsNull || id.IsErased || id.IsEffectivelyErased)
                throw new InvalidOperationException("owned object is erased or unavailable");
            var obj = tx.GetObject(id, OpenMode.ForRead);
            if (obj.OwnerId != expectedOwner) throw new InvalidOperationException("object is not owned by the inspected container");
            return obj;
        }

        private static JObject Envelope(DBObject obj, InspectionState state)
        {
            var rx = obj.ObjectId.ObjectClass;
            return new JObject { ["handle"] = obj.Handle.ToString(), ["owner_handle"] = HandleOf(obj.OwnerId),
                ["managed_type"] = state.Text(obj.GetType().FullName), ["native_class"] = state.Text(rx.Name),
                ["dxf_name"] = state.Text(rx.DxfName), ["application"] = state.Text(rx.AppName),
                ["extension_dictionary_handle"] = HandleOf(obj.ExtensionDictionary) };
        }

        private static JToken MetadataScalar(object value, InspectionState state)
        {
            if (value is ObjectId id) return new JObject { ["handle"] = HandleOf(id), ["reference_followed"] = false };
            if (value is Handle handle) return new JObject { ["handle"] = handle.ToString(), ["reference_followed"] = false };
            if (value is Point3d point) return Point(point);
            if (value is Point2d point2) return new JObject { ["x"] = point2.X, ["y"] = point2.Y };
            if (value is Vector3d vector) return Vector(vector);
            if (value is byte[] bytes)
            { state.MetadataContentsComplete = false; return new JObject { ["binary_length"] = bytes.Length, ["binary_omitted"] = true }; }
            return Scalar(value, state, dynamicProperty: false);
        }

        private static JToken Scalar(object value, InspectionState state, bool dynamicProperty = true)
        {
            if (value == null) return JValue.CreateNull();
            if (value is string text) return new JValue(state.Text(text));
            if (value is bool boolean) return new JValue(boolean);
            if (TryFiniteNumber(value, out _)) return new JValue(value);
            if (dynamicProperty) state.PropertiesComplete = false;
            else state.MetadataContentsComplete = false;
            state.Warn("Non-scalar or non-finite values are represented by type envelopes only.");
            return new JObject { ["managed_type"] = value.GetType().FullName, ["value_omitted"] = true };
        }

        private static bool TryFiniteNumber(object value, out double number)
        {
            number = 0;
            if (!(value is byte || value is sbyte || value is short || value is ushort || value is int ||
                value is uint || value is long || value is ulong || value is float || value is double || value is decimal)) return false;
            number = Convert.ToDouble(value);
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static string HandleOf(ObjectId id) => id.IsNull ? null : id.Handle.ToString();
        private static JObject Point(Point3d point) => new JObject { ["x"] = point.X, ["y"] = point.Y, ["z"] = point.Z };
        private static JObject Vector(Vector3d vector) => new JObject { ["x"] = vector.X, ["y"] = vector.Y, ["z"] = vector.Z };

        private sealed class InspectionState
        {
            internal readonly BlockInspectionInput Input;
            internal readonly Stopwatch Watch = Stopwatch.StartNew();
            internal readonly JArray Attributes = new JArray(), Properties = new JArray(), Entities = new JArray(),
                Metadata = new JArray(), Fields = new JArray(), Warnings = new JArray(), Errors = new JArray();
            internal readonly HashSet<ObjectId> MetadataVisited = new HashSet<ObjectId>();
            internal readonly HashSet<ObjectId> FieldVisited = new HashSet<ObjectId>();
            private readonly HashSet<string> warningSet = new HashSet<string>(StringComparer.Ordinal);
            internal readonly int AttributeLimit, PropertyLimit;
            internal int AttributeCount, PropertyCount, MetadataAttempts, ErrorCount, ItemBytes;
            internal bool AttributesComplete = true, PropertiesComplete = true, EntitiesComplete = true,
                MetadataComplete = true, MetadataContentsComplete = true, FieldsComplete = true, StringsTruncated;
            internal string StopReason;

            internal InspectionState(BlockInspectionInput input)
            { Input = input; AttributeLimit = Math.Min(input.MaxEntities, 512); PropertyLimit = Math.Min(input.MaxEntities, 256); }

            internal bool Continue()
            {
                if (StopReason != null) return false;
                if (Watch.ElapsedMilliseconds < BudgetMilliseconds) return true;
                StopReason = "time_budget"; return false;
            }

            internal bool Add(JArray destination, JObject item)
            {
                if (!Reserve(item)) return false;
                destination.Add(item); return true;
            }

            internal bool Reserve(JObject item)
            {
                Trim(item);
                var bytes = Encoding.UTF8.GetByteCount(item.ToString(Formatting.None));
                if (ItemBytes + bytes > MaxResponseBytes - 64 * 1024)
                { StopReason = "response_budget"; Warn("Inspection reached its response byte limit."); return false; }
                ItemBytes += bytes; return true;
            }

            internal string Text(string value)
            {
                if (value == null || value.Length <= MaxStringCharacters) return value;
                StringsTruncated = true;
                Warn("Long text or metadata strings were truncated to the declared character limit.");
                return value.Substring(0, MaxStringCharacters);
            }

            internal void Trim(JToken token)
            {
                if (token is JValue value && value.Type == JTokenType.String) value.Value = Text(value.Value<string>());
                else if (token is JValue numeric && numeric.Type == JTokenType.Float)
                {
                    var number = numeric.Value<double>();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    { numeric.Value = null; EntitiesComplete = false; Warn("Non-finite geometry value was omitted."); }
                }
                else if (token is JContainer container)
                    foreach (var child in container.Children()) Trim(child);
            }

            internal void Warn(string warning)
            { if (Warnings.Count < 25 && warningSet.Add(warning)) Warnings.Add(warning); }

            internal void Error(string handle, Exception exception)
            {
                ErrorCount++;
                if (Errors.Count < 50)
                {
                    var message = ErrorSanitizer.Sanitize(exception.Message);
                    Errors.Add(new JObject { ["handle"] = handle,
                        ["error"] = message.Length <= 1024 ? message : message.Substring(0, 1024) });
                }
            }
        }
    }
}
