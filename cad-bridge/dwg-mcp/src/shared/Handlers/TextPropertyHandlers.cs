using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class SetTextStyleHandler : IAcadCommand
    {
        public string Name => "set_text_style";
        public string Description => "Assign an existing text style to an atomic bounded batch of plain DBText or MText.";
        public CommandSchema Schema => CommandSchemas.SetTextStyle;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!PropertyStyleInput.TrySet(parameters as JObject, out var handles, out var name, out var error))
                return CommandResult.Fail(error);
            try
            {
                if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var table = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    if (!table.Has(name)) return CommandResult.Fail("text style does not exist: " + name);
                    var styleId = table[name];
                    if (!TextPropertyBatch.TryStyle(tx, styleId, out error)) return CommandResult.Fail(error);
                    if (!TextPropertyBatch.TryTargets(db, tx, handles, out var targets, out error)) return CommandResult.Fail(error);
                    foreach (var entity in targets)
                        if (!TextPropertyBatch.TryTextTarget(entity, out error))
                            return CommandResult.Fail("handle " + entity.Handle + ": " + error);
                    TextPropertyBatch.OpenAll(targets);
                    var changed = new List<string>();
                    foreach (var entity in targets)
                    {
                        if (TextPropertyBatch.GetStyle(entity) == styleId) continue;
                        TextPropertyBatch.SetStyle(entity, styleId);
                        changed.Add(entity.Handle.ToString());
                    }
                    tx.Commit();
                    return CommandResult.Success(new { handles = changed.ToArray(), count = changed.Count,
                        examined = targets.Count, text_style = name, atomic = true,
                        inline_font_overrides = "preserved" });
                }
            }
            catch (Exception ex) { return CommandResult.Fail("failed to set text style: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public sealed class MatchPropertiesHandler : IAcadCommand
    {
        public string Name => "match_properties";
        public string Description => "Copy only explicitly selected styling properties from one existing entity, in one transaction.";
        public CommandSchema Schema => CommandSchemas.MatchProperties;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!PropertyStyleInput.TryMatch(parameters as JObject, out var sourceHandle, out var handles, out var properties, out var error))
                return CommandResult.Fail(error);
            try
            {
                if (!LineweightTargetPreflight.TryCheckDrawing(doc, out error)) return CommandResult.Fail(error);
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!TextPropertyBatch.TryTargets(db, tx, new[] { sourceHandle }, out var sources, out error))
                        return CommandResult.Fail("source: " + error);
                    var source = sources[0];
                    if (!TextPropertyBatch.TryTargets(db, tx, handles, out var targets, out error)) return CommandResult.Fail(error);
                    bool text = properties.Any(p => p == "text_style" || p == "height" || p == "width_factor" || p == "oblique");
                    bool dbTextOnly = properties.Contains("width_factor") || properties.Contains("oblique");
                    foreach (var entity in new[] { source }.Concat(targets))
                    {
                        if (text && !TextPropertyBatch.TryTextTarget(entity, out error))
                            return CommandResult.Fail("handle " + entity.Handle + ": " + error);
                        if (dbTextOnly && entity.GetType() != typeof(DBText))
                            return CommandResult.Fail("width_factor and oblique require plain DBText for source and all targets");
                    }
                    if (properties.Contains("text_style") && !TextPropertyBatch.TryStyle(tx, TextPropertyBatch.GetStyle(source), out error))
                        return CommandResult.Fail(error);
                    // Snapshot every selected value before any target is opened for write.
                    var values = new Dictionary<string, object>();
                    foreach (var property in properties) values[property] = Read(source, property);
                    TextPropertyBatch.OpenAll(targets);
                    foreach (var entity in targets)
                        foreach (var property in properties) Write(entity, property, values[property]);
                    tx.Commit();
                    return CommandResult.Success(new { source_handle = source.Handle.ToString(),
                        handles = targets.Select(e => e.Handle.ToString()).ToArray(), count = targets.Count,
                        properties, atomic = true, inline_font_overrides = "preserved" });
                }
            }
            catch (Exception ex) { return CommandResult.Fail("failed to match properties: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }

        private static object Read(Entity entity, string property)
        {
            switch (property)
            {
                case "layer": return entity.LayerId;
                case "color": return entity.Color;
                case "linetype": return entity.LinetypeId;
                case "linetype_scale": return entity.LinetypeScale;
                case "lineweight": return entity.LineWeight;
                case "text_style": return TextPropertyBatch.GetStyle(entity);
                case "height": return entity is DBText t ? t.Height : ((MText)entity).TextHeight;
                case "width_factor": return ((DBText)entity).WidthFactor;
                case "oblique": return ((DBText)entity).Oblique;
                default: throw new InvalidOperationException("unsupported property");
            }
        }

        private static void Write(Entity entity, string property, object value)
        {
            switch (property)
            {
                case "layer": entity.LayerId = (ObjectId)value; break;
                case "color": entity.Color = (Autodesk.AutoCAD.Colors.Color)value; break;
                case "linetype": entity.LinetypeId = (ObjectId)value; break;
                case "linetype_scale": entity.LinetypeScale = (double)value; break;
                case "lineweight": entity.LineWeight = (LineWeight)value; break;
                case "text_style": TextPropertyBatch.SetStyle(entity, (ObjectId)value); break;
                case "height": if (entity is DBText t) t.Height = (double)value; else ((MText)entity).TextHeight = (double)value; break;
                case "width_factor": ((DBText)entity).WidthFactor = (double)value; break;
                case "oblique": ((DBText)entity).Oblique = (double)value; break;
                default: throw new InvalidOperationException("unsupported property");
            }
        }
    }

    internal static class TextPropertyBatch
    {
        internal static bool IsPlainText(Entity entity) => entity.GetType() == typeof(DBText) || entity.GetType() == typeof(MText);
        internal static bool TryTextTarget(Entity entity, out string error)
        {
            error = null;
            if (!IsPlainText(entity) || entity.Annotative == AnnotativeStates.True)
                error = "text properties require non-annotative plain DBText or MText; attributes are excluded";
            else if (entity is DBText t && (t.HorizontalMode == TextHorizontalMode.TextAlign || t.HorizontalMode == TextHorizontalMode.TextFit))
                error = "aligned/fit DBText requires a separate two-anchor operation";
            return error == null;
        }
        internal static ObjectId GetStyle(Entity entity) => entity is DBText t ? t.TextStyleId : ((MText)entity).TextStyleId;
        internal static void SetStyle(Entity entity, ObjectId id)
        {
            // Retain the justification reference anchor. AutoCAD may recompute the derived
            // insertion point/extents for non-default justification when the object closes.
            // Calling AdjustAlignment here would introduce unnecessary geometry changes.
            if (entity is DBText t)
            {
                bool defaultAlignment = t.IsDefaultAlignment;
                var anchor = defaultAlignment ? t.Position : t.AlignmentPoint;
                t.TextStyleId = id;
                if (defaultAlignment) t.Position = anchor; else t.AlignmentPoint = anchor;
            }
            else
            {
                var m = (MText)entity;
                var anchor = m.Location;
                m.TextStyleId = id;
                m.Location = anchor;
            }
        }

        internal static bool TryStyle(Transaction tx, ObjectId id, out string error)
        {
            error = null;
            if (id.IsNull || id.IsErased) { error = "text style is unavailable"; return false; }
            var style = tx.GetObject(id, OpenMode.ForRead) as TextStyleTableRecord;
            if (style == null || style.IsDependent || style.IsErased || style.IsShapeFile) error = "text style is unavailable, xref-dependent or a shape record";
            else if (style.Annotative == AnnotativeStates.True) error = "annotative text styles require a separate context-aware operation";
            return error == null;
        }

        internal static bool TryTargets(Database db, Transaction tx, string[] handles, out List<Entity> result, out string error)
        {
            result = new List<Entity>();
            error = null;
            foreach (var handle in handles)
            {
                if (!CadHandleResolver.TryResolve(db, handle, out var id, out error)) { error = "handle " + handle + ": " + error; return false; }
                if (id.IsErased) { error = "handle " + handle + ": entity is erased"; return false; }
                var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) { error = "handle " + handle + ": object is not an entity"; return false; }
                if (!LineweightTargetPreflight.TryCheckEntity(tx, entity, out error)) { error = "handle " + handle + ": " + error; return false; }
                result.Add(entity);
            }
            return true;
        }

        internal static void OpenAll(IEnumerable<Entity> entities)
        {
            foreach (var entity in entities)
            {
                entity.UpgradeOpen();
                if (!entity.IsWriteEnabled) throw new InvalidOperationException("entity is read-only");
            }
        }
    }
}
