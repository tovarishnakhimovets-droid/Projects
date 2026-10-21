using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Reads text from the pickfirst selection and returns spatial groups.
    /// The response is a snapshot; no cross-document writeback state is stored.
    /// </summary>
    public class GetSelectedTextsHandler : IAcadCommand
    {
        public string Name => "get_selected_texts";
        public string Description => "Read and cluster currently selected AutoCAD text entities.";
        public CommandSchema Schema => CommandSchemas.GetSelectedTexts;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var ed = doc.Editor;
            var pickfirst = ed.SelectImplied();
            if (pickfirst.Status != PromptStatus.OK || pickfirst.Value == null || pickfirst.Value.Count == 0)
                return CommandResult.Fail("no selection");

            var records = new List<EntityRecord>();
            using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (SelectedObject so in pickfirst.Value)
                {
                    var obj = tx.GetObject(so.ObjectId, OpenMode.ForRead);
                    CollectEntityRecords(tx, obj, Matrix3d.Identity, null, records, depth: 0);
                }
            }

            var options = ClusterOptions.FromWire(parameters?["grouping_strength"]?.Value<string>());
            bool includeEntities = parameters?["include_entities"]?.Value<bool>() ?? false;

            var clusters = SpatialClusterer.Cluster(records, options);

            var lightweight = clusters.Select(c => new
            {
                id = c.Id,
                text = c.CombinedText,
                entity_count = c.AllHandles.Count,
                in_block = c.InBlock,
                entities = includeEntities ? c.Entities.Select(e => new
                {
                    handle = e.Handle,
                    kind = e.Kind,
                    text = e.Text,
                    x = e.X,
                    y = e.Y,
                    z = e.Z,
                    height = e.Height,
                    layer = e.Layer,
                    owner_handle = e.OwnerHandle,
                    block_handle = e.BlockHandle,
                    block_scale = e.BlockScale,
                    local_position = new { x = e.LocalX, y = e.LocalY, z = e.LocalZ },
                    local_height = e.LocalHeight,
                    bounds_available = e.BoundsAvailable,
                    bounds = new { min_x = e.BoundsMinX, max_x = e.BoundsMaxX,
                        min_y = e.BoundsMinY, max_y = e.BoundsMaxY }
                }).ToList() : null
            }).ToList();

            return CommandResult.Success(new { clusters = lightweight });
        }

        private const int MaxBlockDepth = 1;

        private static void CollectEntityRecords(
            Transaction tx,
            DBObject obj,
            Matrix3d xform,
            string blockHandle,
            List<EntityRecord> records,
            int depth)
        {
            switch (obj)
            {
                // IMPORTANT: AttributeReference and AttributeDefinition both
                // inherit from DBText, so they MUST be matched before DBText.
                case AttributeReference attRef:
                    records.Add(BuildEntityRecord(
                        attRef, attRef.TextString,
                        attRef.Position, attRef.Height,
                        xform, blockHandle));
                    break;

                case AttributeDefinition attDef:
                    if (attDef.Constant)
                    {
                        records.Add(BuildEntityRecord(
                            attDef, attDef.TextString,
                            attDef.Position, attDef.Height,
                            xform, blockHandle));
                    }
                    break;

                case DBText dbText:
                    records.Add(BuildEntityRecord(
                        dbText, dbText.TextString,
                        dbText.Position, dbText.Height,
                        xform, blockHandle));
                    break;

                case MText mText:
                    records.Add(BuildEntityRecord(
                        mText, mText.Contents,
                        mText.Location, mText.TextHeight,
                        xform, blockHandle));
                    break;

                case MLeader mLeader:
                    string mlText;
                    using (var text = mLeader.MText)
                        mlText = text?.Contents ?? string.Empty;
                    Point3d mlPos;
                    try { mlPos = mLeader.GetFirstVertex(0); }
                    catch { mlPos = Point3d.Origin; }
                    records.Add(BuildEntityRecord(
                        mLeader, mlText,
                        mlPos, mLeader.TextHeight,
                        xform, blockHandle));
                    break;

                case BlockReference br:
                    // BlockReference wrappers are NOT collected as EntityRecords (no text).
                    // Recurse into per-instance attribute references — already world-space.
                    string brHandleStr = br.Handle.ToString();
                    foreach (ObjectId aId in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tx.GetObject(aId, OpenMode.ForRead);
                        CollectEntityRecords(tx, att, xform, brHandleStr, records, depth);
                    }

                    // Recurse into block definition (one level deep).
                    if (depth < MaxBlockDepth)
                    {
                        var blockDef = (BlockTableRecord)tx.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        var childXform = br.BlockTransform.PostMultiplyBy(xform);
                        foreach (ObjectId childId in blockDef)
                        {
                            var child = tx.GetObject(childId, OpenMode.ForRead);
                            if (child is BlockReference) continue; // skip nested, MVP
                            CollectEntityRecords(tx, child, childXform, brHandleStr, records, depth + 1);
                        }
                    }
                    break;

                // Other entity types: silently skip
            }
        }

        private static EntityRecord BuildEntityRecord(
            Entity ent, string text,
            Point3d localPos, double localHeight,
            Matrix3d xform, string blockHandle)
        {
            var worldPos = localPos.TransformBy(xform);
            double worldH = localHeight * YScaleOf(xform);

            double boundsMinX = worldPos.X, boundsMaxX = worldPos.X;
            double boundsMinY = worldPos.Y, boundsMaxY = worldPos.Y;
            bool boundsAvailable = false;
            try
            {
                var ext = ent.GeometricExtents;
                var corners = new[]
                {
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
                };
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var c in corners)
                {
                    var t = c.TransformBy(xform);
                    if (t.X < minX) minX = t.X;
                    if (t.Y < minY) minY = t.Y;
                    if (t.X > maxX) maxX = t.X;
                    if (t.Y > maxY) maxY = t.Y;
                }
                boundsMinX = minX; boundsMaxX = maxX;
                boundsMinY = minY; boundsMaxY = maxY;
                boundsAvailable = true;
            }
            catch { /* use position fallback */ }

            return new EntityRecord
            {
                Handle = ent.Handle.ToString(),
                Kind = GetEntityKind(ent),
                Text = text,
                X = worldPos.X,
                Y = worldPos.Y,
                Z = worldPos.Z,
                Height = worldH > 0 ? worldH : localHeight,
                LocalX = localPos.X,
                LocalY = localPos.Y,
                LocalZ = localPos.Z,
                LocalHeight = localHeight,
                OwnerHandle = ent.OwnerId.IsNull ? null : ent.OwnerId.Handle.ToString(),
                Layer = ent.Layer,
                BlockHandle = blockHandle,
                BoundsMinX = boundsMinX,
                BoundsMaxX = boundsMaxX,
                BoundsMinY = boundsMinY,
                BoundsMaxY = boundsMaxY,
                BoundsAvailable = boundsAvailable,
                BlockScale = YScaleOf(xform)
            };
        }

        private static string GetEntityKind(Entity ent)
        {
            switch (ent)
            {
                case AttributeReference _:
                    return "AttributeReference";
                case AttributeDefinition _:
                    return "AttributeDefinition";
                case DBText _:
                    return "DBText";
                case MText _:
                    return "MText";
                case MLeader _:
                    return "MLeader";
                default:
                    return ent.GetType().Name;
            }
        }

        private static double YScaleOf(Matrix3d m) => m.CoordinateSystem3d.Yaxis.Length;
    }
}
