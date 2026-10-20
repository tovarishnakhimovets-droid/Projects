using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateHatchHandler : IAcadCommand
    {
        public string Name => "create_hatch";
        public string Description => "Create a solid hatch from one circle or closed lightweight polyline in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateHatch;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!CreateHatchInput.TryParse(parameters, out var input, out var inputError))
                return CommandResult.Fail(inputError);
            if (doc == null)
                return CommandResult.Fail("an active document is required");

            try
            {
                if (doc.IsReadOnly)
                    return CommandResult.Fail("the active document is read-only");
                var db = doc.Database;
                if (db == null)
                    return CommandResult.Fail("the active document has no database");

                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CadHandleResolver.TryResolve(db, input.BoundaryHandle, out var boundaryId, out var handleError))
                        return CommandResult.Fail("boundary_handle: " + handleError);
                    if (!boundaryId.IsValid || boundaryId.IsErased || boundaryId.IsEffectivelyErased)
                        return CommandResult.Fail("boundary_handle must identify a valid, non-erased entity");

                    var boundary = tx.GetObject(boundaryId, OpenMode.ForRead) as Entity;
                    if (boundary == null)
                        return CommandResult.Fail("boundary_handle must identify an entity");
                    if (boundary.OwnerId != db.CurrentSpaceId)
                        return CommandResult.Fail("the boundary must belong to the current drawing space");
                    if (!TryGetBoundaryPlane(boundary, out var normal, out var elevation, out var boundaryError))
                        return CommandResult.Fail(boundaryError);

                    var currentSpace = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    if (currentSpace.IsDependent || currentSpace.IsFromExternalReference || currentSpace.IsFromOverlayReference)
                        return CommandResult.Fail("the current drawing space is an external reference and is read-only");
                    if (!TryGetWritableLayer(tx, boundary.LayerId, "boundary", out _, out var boundaryLayerError))
                        return CommandResult.Fail(boundaryLayerError);

                    var targetLayerId = boundary.LayerId;
                    if (input.Layer != null)
                    {
                        var layers = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (!layers.Has(input.Layer))
                            return CommandResult.Fail("the target layer must already exist: " + input.Layer);
                        targetLayerId = layers[input.Layer];
                    }
                    if (!TryGetWritableLayer(tx, targetLayerId, "target", out var layerName, out var targetLayerError))
                        return CommandResult.Fail(targetLayerError);

                    // All input, geometry, ownership and layer checks finish before
                    // opening anything for write or creating a database entity.
                    if (!currentSpace.IsWriteEnabled)
                        currentSpace.UpgradeOpen();
                    if (input.Associative && !boundary.IsWriteEnabled)
                        boundary.UpgradeOpen();
                    var drawOrder = (DrawOrderTable)tx.GetObject(currentSpace.DrawOrderTableId, OpenMode.ForWrite);

                    using (var hatch = new Hatch())
                    {
                        hatch.SetDatabaseDefaults(db);
                        hatch.LayerId = targetLayerId;
                        hatch.Normal = normal;
                        hatch.Elevation = elevation;
                        hatch.HatchStyle = HatchStyle.Normal;
                        ApplyColor(hatch, input);

                        var hatchId = currentSpace.AppendEntity(hatch);
                        tx.AddNewlyCreatedDBObject(hatch, true);
                        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                        // Autodesk requires database residency before Associative,
                        // and Associative must be set before building the loop.
                        hatch.Associative = input.Associative;
                        hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { boundaryId });
                        hatch.EvaluateHatch(true);

                        var hatchIds = new ObjectIdCollection { hatchId };
                        if (input.DrawOrder == "above_entities")
                        {
                            drawOrder.MoveToTop(hatchIds);
                            drawOrder.MoveAbove(new ObjectIdCollection { boundaryId }, hatchId);
                        }
                        else
                        {
                            drawOrder.MoveToBottom(hatchIds);
                        }

                        var result = new
                        {
                            handle = hatch.Handle.ToString(),
                            boundary_handle = boundary.Handle.ToString(),
                            pattern = hatch.PatternName,
                            layer = layerName,
                            color = DescribeColor(input),
                            associative = hatch.Associative,
                            draw_order = input.DrawOrder
                        };
                        tx.Commit();
                        return CommandResult.Success(result);
                    }
                }
            }
            catch (Exception ex)
            {
                // Disposing the uncommitted transaction also removes a hatch
                // whose loop evaluation or draw-order adjustment failed.
                return CommandResult.Fail("failed to create hatch: " + ex.Message);
            }
        }

        private static bool TryGetWritableLayer(
            Transaction tx, ObjectId layerId, string role, out string layerName, out string error)
        {
            layerName = null;
            error = null;
            if (layerId.IsNull || !layerId.IsValid || layerId.IsErased || layerId.IsEffectivelyErased)
            {
                error = "the " + role + " layer is invalid or erased";
                return false;
            }
            var layer = tx.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
            if (layer == null)
            {
                error = "the " + role + " layer is invalid";
                return false;
            }
            layerName = layer.Name;
            if (layer.IsLocked)
            {
                error = "the " + role + " layer is locked: " + layerName;
                return false;
            }
            if (layer.IsDependent)
            {
                error = "the " + role + " layer is an external-reference layer and is read-only: " + layerName;
                return false;
            }
            return true;
        }

        private static bool TryGetBoundaryPlane(
            Entity boundary, out Vector3d normal, out double elevation, out string error)
        {
            normal = Vector3d.ZAxis;
            elevation = 0d;
            error = null;
            if (!boundary.IsPlanar)
            {
                error = "the hatch boundary must be planar";
                return false;
            }

            double area;
            if (boundary is Circle circle)
            {
                if (!IsFinite(circle.Radius) || circle.Radius <= 0d ||
                    !IsFinite(circle.Center.X) || !IsFinite(circle.Center.Y) || !IsFinite(circle.Center.Z))
                {
                    error = "the boundary circle must have finite coordinates and a positive radius";
                    return false;
                }
                normal = circle.Normal;
                if (!TryNormalize(ref normal))
                {
                    error = "the hatch boundary must have a finite, nonzero plane normal";
                    return false;
                }
                elevation = new Vector3d(circle.Center.X, circle.Center.Y, circle.Center.Z).DotProduct(normal);
                area = Math.PI * circle.Radius * circle.Radius;
            }
            else if (boundary is Polyline polyline)
            {
                if (!polyline.Closed || polyline.NumberOfVertices < 2)
                {
                    error = "the lightweight polyline boundary must be closed and contain at least two vertices";
                    return false;
                }
                for (var i = 0; i < polyline.NumberOfVertices; i++)
                {
                    var point = polyline.GetPoint2dAt(i);
                    if (!IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(polyline.GetBulgeAt(i)))
                    {
                        error = "the lightweight polyline boundary must have finite vertices and bulges";
                        return false;
                    }
                }
                normal = polyline.Normal;
                if (!TryNormalize(ref normal))
                {
                    error = "the hatch boundary must have a finite, nonzero plane normal";
                    return false;
                }
                elevation = polyline.Elevation;
                area = Math.Abs(polyline.Area);
            }
            else
            {
                error = "boundary_handle must identify a circle or a closed lightweight polyline";
                return false;
            }

            if (!IsFinite(elevation) || !IsFinite(area) || area <= 0d)
            {
                error = "the hatch boundary must enclose a finite, nonzero area on a finite plane";
                return false;
            }
            return true;
        }

        private static bool TryNormalize(ref Vector3d normal)
        {
            if (!IsFinite(normal.X) || !IsFinite(normal.Y) || !IsFinite(normal.Z) ||
                !IsFinite(normal.Length) || normal.Length <= 0d)
                return false;
            normal = normal.GetNormal();
            return true;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void ApplyColor(Entity entity, CreateHatchInput input)
        {
            if (input.ColorRgb != null)
            {
                entity.Color = Color.FromRgb((byte)input.ColorRgb[0], (byte)input.ColorRgb[1], (byte)input.ColorRgb[2]);
                return;
            }
            var index = input.ColorIndex ?? 256;
            entity.Color = Color.FromColorIndex(index == 256 ? ColorMethod.ByLayer : ColorMethod.ByAci, (short)index);
        }

        private static object DescribeColor(CreateHatchInput input)
        {
            var index = input.ColorIndex ?? 256;
            return new
            {
                method = input.ColorRgb != null ? "ByColor" : index == 256 ? "ByLayer" : "ByAci",
                color_index = input.ColorRgb != null ? (int?)null : index,
                rgb = input.ColorRgb
            };
        }
    }
}
