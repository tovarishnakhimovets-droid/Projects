using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Network;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class NodeNetworkHandler : IAcadCommand
    {
        public string Name => "node_network";
        public string Description => "Split and snap explicitly guarded straight planar network paths at existing nodes in one transaction.";
        public CommandSchema Schema => CommandSchemas.NodeNetwork;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!NetworkRequest.TryParse(parameters, true, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            try
            {
                var db = doc.Database;
                var watch = Stopwatch.StartNew();
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var sources = new Dictionary<string, Entity>();
                    foreach (var expected in input.Sources)
                    {
                        var entity = NetworkNative.Entity(tx, db, expected.Handle, true);
                        NetworkNative.Require(entity.GetType().Name == expected.ExpectedType, "source class changed: " + expected.Handle);
                        NetworkNative.Require(entity.Layer == expected.ExpectedLayer &&
                            entity.OwnerId.Handle.ToString().Equals(expected.ExpectedOwnerHandle, StringComparison.OrdinalIgnoreCase),
                            "source layer or owner changed: " + expected.Handle);
                        var actual = NetworkNative.Vertices(entity, input.MinSegmentLength);
                        NetworkNative.CheckVertices(actual, expected.ExpectedVertices, input.GeometryTolerance, expected.Handle);
                        expected.ExpectedVertices = actual;
                        sources.Add(expected.Handle, entity);
                        NetworkNative.Time(watch);
                    }
                    foreach (var expected in input.Nodes)
                    {
                        var entity = NetworkNative.Entity(tx, db, expected.Handle, false);
                        NetworkPoint actual;
                        if (entity is DBPoint point) actual = NetworkNative.Point(point.Position);
                        else if (entity is BlockReference reference)
                        {
                            NetworkNative.CheckBlock(tx, reference);
                            actual = NetworkNative.Point(reference.Position);
                        }
                        else throw new ArgumentException("node must be a Point or BlockReference: " + expected.Handle);
                        NetworkNative.Require(actual.Distance(expected.ExpectedPosition) <= input.GeometryTolerance,
                            "node position changed: " + expected.Handle);
                        expected.ExpectedPosition = actual;
                        NetworkNative.Time(watch);
                    }
                    var plan = NetworkGeometry.Plan(input.Sources, input.Nodes, input.SnapTolerance,
                        input.GeometryTolerance, input.MinSegmentLength);
                    var groups = plan.GroupBy(part => part.SourceHandle).ToArray();
                    // Cloning linked metadata can duplicate identifiers and associations. Refuse before any write.
                    foreach (var group in groups)
                        if (group.Count() > 1) NetworkNative.CheckCloneMetadata(sources[group.Key]);
                    double beforeLength = input.Sources.Sum(source => NetworkGeometry.Length(source.ExpectedVertices));
                    var nodes = input.Nodes.ToDictionary(node => node.Handle, node => node.ExpectedPosition);
                    if (input.DryRun)
                        return CommandResult.Success(new
                        {
                            document = doc.Name, fingerprint = db.FingerprintGuid.ToString(), dry_run = true,
                            committed = false, mode = "split_and_snap", source_count = sources.Count,
                            planned_part_count = plan.Length,
                            length_before = beforeLength, length_after = plan.Sum(part => part.Length),
                            splits = plan.Select(part => new
                            {
                                source_handle = part.SourceHandle, from_node = part.FromNode, to_node = part.ToNode,
                                vertices = part.Vertices.Select(point => point.ToArray()).ToArray()
                            }).ToArray(), saved = false
                        });
                    error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                    if (error != null) return CommandResult.Fail(error);
                    NetworkNative.Require(!doc.IsReadOnly, "the active drawing is read-only");
                    var owner = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    foreach (var entity in sources.Values)
                    {
                        entity.UpgradeOpen();
                        NetworkNative.Require(entity.IsWriteEnabled, "source cannot be opened for writing");
                    }
                    // Make every clone before reshaping its source, preserving original native entity properties.
                    var pending = new List<Tuple<NetworkPart, Entity, bool>>();
                    var unownedClones = new List<Entity>();
                    try
                    {
                        foreach (var group in groups)
                        {
                            bool first = true;
                            foreach (var part in group)
                            {
                                var entity = first ? sources[group.Key] : (Entity)sources[group.Key].Clone();
                                if (!first) unownedClones.Add(entity);
                                pending.Add(Tuple.Create(part, entity, first));
                                first = false;
                            }
                        }
                        var results = new List<object>();
                        foreach (var item in pending)
                        {
                            var part = item.Item1;
                            var entity = item.Item2;
                            NetworkNative.WriteVertices(entity, part.Vertices);
                            if (!item.Item3)
                            {
                                owner.AppendEntity(entity);
                                tx.AddNewlyCreatedDBObject(entity, true);
                                unownedClones.Remove(entity);
                            }
                            var actual = NetworkNative.Vertices(entity, input.MinSegmentLength);
                            NetworkNative.CheckVertices(actual, part.Vertices, 0, entity.Handle.ToString());
                            NetworkNative.Require(actual[0].Exact(nodes[part.FromNode]) &&
                                actual[actual.Length - 1].Exact(nodes[part.ToNode]), "final endpoints are not exact; transaction aborted");
                            NetworkNative.Require(entity.OwnerId == sources[part.SourceHandle].OwnerId,
                                "split owner changed; transaction aborted");
                            results.Add(new
                            {
                                source_handle = part.SourceHandle, handle = entity.Handle.ToString(), retained_source = item.Item3,
                                from_node = part.FromNode, to_node = part.ToNode,
                                owner_handle = entity.OwnerId.Handle.ToString(), layer = entity.Layer,
                                vertices = actual.Select(point => point.ToArray()).ToArray(), length = part.Length, endpoint_gap = 0
                            });
                            NetworkNative.Time(watch);
                        }
                        var result = CommandResult.Success(new
                        {
                            document = doc.Name, fingerprint = db.FingerprintGuid.ToString(), dry_run = false,
                            committed = true, mode = "split_and_snap", source_count = sources.Count,
                            part_count = plan.Length, created_count = plan.Length - sources.Count,
                            length_before = beforeLength, length_after = plan.Sum(part => part.Length),
                            exact_endpoints = true, splits = results.ToArray(), saved = false
                        });
                        tx.Commit();
                        return result;
                    }
                    finally
                    {
                        foreach (var entity in unownedClones) entity.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("network noding failed: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public sealed class MoveNetworkEndpointHandler : IAcadCommand
    {
        public string Name => "move_network_endpoint";
        public string Description => "Move guarded polyline endpoints and existing consumer blocks with attached attributes in one transaction.";
        public CommandSchema Schema => CommandSchemas.MoveNetworkEndpoint;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!NetworkRequest.TryParse(parameters, false, out var input, out var error)) return CommandResult.Fail(error);
            error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
            if (error != null) return CommandResult.Fail(error);
            try
            {
                var db = doc.Database;
                var watch = Stopwatch.StartNew();
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var polylines = new Dictionary<string, Polyline>();
                    var blocks = new Dictionary<string, BlockReference>();
                    var attributes = new Dictionary<string, AttributeReference>();
                    var paths = new Dictionary<string, NetworkPoint[]>();
                    var initial = new Dictionary<string, NetworkPoint[]>();
                    foreach (var change in input.Changes)
                    {
                        var polyline = NetworkNative.Entity(tx, db, change.PolylineHandle, true) as Polyline;
                        var block = NetworkNative.Entity(tx, db, change.BlockHandle, true) as BlockReference;
                        NetworkNative.Require(polyline != null && block != null, "requires a Polyline and BlockReference");
                        NetworkNative.CheckBlock(tx, block);
                        NetworkNative.Require(!block.IsDynamicBlock, "dynamic consumer blocks are unsupported");
                        NetworkNative.Require(NetworkNative.PlusZ(block.Normal), "consumer block normal must be +Z for exact planar endpoint placement");
                        var definition = (BlockTableRecord)tx.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                        NetworkNative.Require(definition.Name == change.ExpectedBlockName &&
                            polyline.Layer == change.ExpectedPolylineLayer && block.Layer == change.ExpectedBlockLayer,
                            "polyline/block name or layer changed");
                        var actual = NetworkNative.Vertices(polyline, input.MinSegmentLength);
                        NetworkNative.CheckVertices(actual, change.ExpectedVertices, input.GeometryTolerance, change.PolylineHandle);
                        var position = NetworkNative.Point(block.Position);
                        NetworkNative.Require(position.Distance(change.ExpectedBlockPosition) <= input.GeometryTolerance,
                            "block insertion changed: " + change.BlockHandle);
                        initial.Add(change.PolylineHandle, actual);
                        paths.Add(change.PolylineHandle, NetworkGeometry.MoveEndpoint(actual, change.AtStart,
                            position, change.Target, input.MinSegmentLength));
                        change.ExpectedBlockPosition = position;
                        NetworkNative.Require(block.AttributeCollection.Count == change.ExpectedAttributes.Length,
                            "attached attribute membership changed: " + change.BlockHandle);
                        var expectedAttributes = change.ExpectedAttributes.ToDictionary(attribute => attribute.Handle);
                        foreach (ObjectId id in block.AttributeCollection)
                        {
                            var attribute = tx.GetObject(id, OpenMode.ForRead) as AttributeReference;
                            NetworkNative.Require(attribute != null && !attribute.IsErased && !id.IsEffectivelyErased,
                                "attached attribute is unavailable");
                            NetworkNative.Require(expectedAttributes.TryGetValue(id.Handle.ToString(), out var expected),
                                "attached attribute handle changed");
                            NetworkNative.Require(!attribute.IsMTextAttribute && attribute.Annotative != AnnotativeStates.True,
                                "multiline or annotative attributes are unsupported");
                            NetworkNative.Require(attribute.Tag == expected.Tag && attribute.TextString == expected.Text &&
                                NetworkNative.Point(attribute.Position).Distance(expected.Position) <= input.GeometryTolerance &&
                                NetworkNative.Point(attribute.AlignmentPoint).Distance(expected.AlignmentPoint) <= input.GeometryTolerance,
                                "attribute text or geometry changed: " + expected.Handle);
                            NetworkNative.Require(LineweightTargetPreflight.TryCheckEntity(tx, attribute, out var attributeError),
                                attributeError ?? "attribute is not writable");
                            expected.Position = NetworkNative.Point(attribute.Position);
                            expected.AlignmentPoint = NetworkNative.Point(attribute.AlignmentPoint);
                            attributes.Add(expected.Handle, attribute);
                        }
                        polylines.Add(change.PolylineHandle, polyline);
                        blocks.Add(change.BlockHandle, block);
                        NetworkNative.Time(watch);
                    }
                    if (!input.DryRun)
                    {
                        error = DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint);
                        if (error != null) return CommandResult.Fail(error);
                        NetworkNative.Require(!doc.IsReadOnly, "the active drawing is read-only");
                        foreach (var entity in polylines.Values.Cast<Entity>().Concat(blocks.Values).Concat(attributes.Values))
                        {
                            entity.UpgradeOpen();
                            NetworkNative.Require(entity.IsWriteEnabled, "target cannot be opened for writing");
                        }
                    }
                    var results = new List<object>();
                    foreach (var change in input.Changes)
                    {
                        var polyline = polylines[change.PolylineHandle];
                        var block = blocks[change.BlockHandle];
                        var target = NetworkNative.Point(change.Target);
                        if (!input.DryRun)
                        {
                            int index = change.AtStart ? 0 : polyline.NumberOfVertices - 1;
                            polyline.SetPointAt(index, new Point2d(target.X, target.Y));
                            var displacement = target - block.Position;
                            var matrix = Matrix3d.Displacement(displacement);
                            double rotation = block.Rotation;
                            var scale = block.ScaleFactors;
                            var normal = block.Normal;
                            block.TransformBy(matrix);
                            block.Position = target;
                            NetworkNative.Require(block.Rotation == rotation && block.ScaleFactors == scale && block.Normal == normal,
                                "block rotation, scale or normal changed; transaction aborted");
                            foreach (var expected in change.ExpectedAttributes)
                            {
                                var attribute = attributes[expected.Handle];
                                var wantedPosition = NetworkNative.Point(expected.Position) + displacement;
                                var wantedAlignment = NetworkNative.Point(expected.AlignmentPoint) + displacement;
                                bool usesAlignment = attribute.HorizontalMode != TextHorizontalMode.TextLeft ||
                                    attribute.VerticalMode != TextVerticalMode.TextBase;
                                // Native BlockReference.TransformBy normally includes attributes. Avoid translating twice.
                                if (attribute.Position.DistanceTo(wantedPosition) > input.GeometryTolerance ||
                                    (usesAlignment && attribute.AlignmentPoint.DistanceTo(wantedAlignment) > input.GeometryTolerance))
                                {
                                    NetworkNative.Require(attribute.Position.DistanceTo(NetworkNative.Point(expected.Position)) <= input.GeometryTolerance &&
                                        (!usesAlignment || attribute.AlignmentPoint.DistanceTo(NetworkNative.Point(expected.AlignmentPoint)) <= input.GeometryTolerance),
                                        "unexpected native attribute transform; transaction aborted");
                                    attribute.TransformBy(matrix);
                                }
                                NetworkNative.Require(attribute.Position.DistanceTo(wantedPosition) <= input.GeometryTolerance &&
                                    (!usesAlignment || attribute.AlignmentPoint.DistanceTo(wantedAlignment) <= input.GeometryTolerance) &&
                                    attribute.Tag == expected.Tag && attribute.TextString == expected.Text,
                                    "attribute displacement or text mismatch; transaction aborted");
                            }
                            var actual = NetworkNative.Vertices(polyline, input.MinSegmentLength);
                            NetworkNative.CheckVertices(actual, paths[change.PolylineHandle], 0, change.PolylineHandle);
                            NetworkNative.Require(NetworkNative.Point(polyline.GetPoint3dAt(index)).Exact(NetworkNative.Point(block.Position)) &&
                                NetworkNative.Point(block.Position).Exact(change.Target),
                                "final endpoint and block insertion are not exact; transaction aborted");
                        }
                        results.Add(new
                        {
                            polyline_handle = change.PolylineHandle, block_handle = change.BlockHandle,
                            endpoint = change.AtStart ? "start" : "end", position = change.Target.ToArray(),
                            length_before = NetworkGeometry.Length(initial[change.PolylineHandle]),
                            length_after = NetworkGeometry.Length(paths[change.PolylineHandle]),
                            attribute_count = change.ExpectedAttributes.Length, endpoint_gap = 0
                        });
                        NetworkNative.Time(watch);
                    }
                    var result = CommandResult.Success(new
                    {
                        document = doc.Name, fingerprint = db.FingerprintGuid.ToString(), dry_run = input.DryRun,
                        committed = !input.DryRun, count = input.Changes.Length,
                        exact_endpoints = !input.DryRun, changes = results.ToArray(), saved = false
                    });
                    if (!input.DryRun) tx.Commit();
                    return result;
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("network endpoint change failed: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    internal static class NetworkNative
    {
        internal static void Require(bool condition, string error)
        { if (!condition) throw new ArgumentException(error); }
        internal static NetworkPoint Point(Point3d point) => new NetworkPoint(point.X, point.Y, point.Z);
        internal static Point3d Point(NetworkPoint point) => new Point3d(point.X, point.Y, point.Z);
        internal static bool PlusZ(Vector3d vector) => vector.X == 0 && vector.Y == 0 && vector.Z == 1;
        internal static void Time(Stopwatch watch)
        { Require(watch.ElapsedMilliseconds <= 15000, "operation exceeded fifteen seconds; transaction aborted; reduce scope"); }
        internal static Entity Entity(Transaction tx, Database db, string handle, bool writing)
        {
            Require(CadHandleResolver.TryResolve(db, handle, out var id, out var error), error ?? "handle is unavailable");
            Require(!id.IsErased && !id.IsEffectivelyErased, "object is erased: " + handle);
            var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
            Require(entity != null && entity.OwnerId == db.CurrentSpaceId,
                "target must be a top-level entity in the current layout space: " + handle);
            Require(entity.Annotative != AnnotativeStates.True, "annotative network entities are unsupported");
            if (writing)
                Require(LineweightTargetPreflight.TryCheckEntity(tx, entity, out error), error ?? "target is not writable");
            return entity;
        }
        internal static void CheckBlock(Transaction tx, BlockReference block)
        {
            var definition = (BlockTableRecord)tx.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            Require(!definition.IsDependent && !definition.IsFromExternalReference && !definition.IsFromOverlayReference,
                "external reference blocks are unsupported");
        }
        internal static NetworkPoint[] Vertices(Entity entity, double minimumLength)
        {
            NetworkPoint[] points;
            if (entity is Line line)
            {
                Require(line.Thickness == 0, "line thickness is unsupported");
                points = new[] { Point(line.StartPoint), Point(line.EndPoint) };
            }
            else if (entity is Polyline polyline)
            {
                Require(!polyline.Closed && PlusZ(polyline.Normal) && polyline.Thickness == 0,
                    "only open +Z planar lightweight polylines without thickness are supported");
                Require(polyline.NumberOfVertices >= 2 && polyline.NumberOfVertices <= NetworkRequest.MaxVertices,
                    "polyline vertex count is unsupported");
                points = new NetworkPoint[polyline.NumberOfVertices];
                for (int i = 0; i < points.Length; i++)
                {
                    Require(polyline.GetBulgeAt(i) == 0 && polyline.GetStartWidthAt(i) == 0 && polyline.GetEndWidthAt(i) == 0,
                        "curved or nonzero-width polyline segments are unsupported");
                    points[i] = Point(polyline.GetPoint3dAt(i));
                }
            }
            else throw new ArgumentException("source must be a Line or open lightweight Polyline; other curves and 3D polylines are unsupported");
            NetworkGeometry.ValidatePath(points, minimumLength);
            return points;
        }
        internal static void CheckVertices(NetworkPoint[] actual, NetworkPoint[] expected, double tolerance, string handle)
        {
            Require(actual.Length == expected.Length, "source vertex count changed: " + handle);
            for (int i = 0; i < actual.Length; i++)
                Require(tolerance == 0 ? actual[i].Exact(expected[i]) : actual[i].Distance(expected[i]) <= tolerance,
                    "source geometry changed or output is not exact: " + handle);
        }
        internal static void CheckCloneMetadata(Entity entity)
        {
            Require(entity.ExtensionDictionary.IsNull && entity.GetPersistentReactorIds().Count == 0,
                "splitting entities with extension dictionaries or persistent associations is unsupported");
            using (var xdata = entity.XData)
                Require(xdata == null || xdata.AsArray().Length == 0, "splitting entities with XData is unsupported");
        }
        internal static void WriteVertices(Entity entity, NetworkPoint[] points)
        {
            if (entity is Line line)
            {
                Require(points.Length == 2, "line split has an invalid vertex count");
                line.StartPoint = Point(points[0]);
                line.EndPoint = Point(points[1]);
                return;
            }
            var polyline = (Polyline)entity;
            // Keep at least two vertices throughout; removing all vertices causes eDegenerateGeometry.
            while (polyline.NumberOfVertices > points.Length) polyline.RemoveVertexAt(polyline.NumberOfVertices - 1);
            for (int i = 0; i < points.Length; i++)
            {
                var point = new Point2d(points[i].X, points[i].Y);
                if (i < polyline.NumberOfVertices) polyline.SetPointAt(i, point);
                else polyline.AddVertexAt(i, point, 0, 0, 0);
            }
            polyline.Elevation = points[0].Z;
        }
    }
}
