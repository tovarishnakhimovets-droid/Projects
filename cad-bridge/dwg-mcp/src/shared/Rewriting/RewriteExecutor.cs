using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Bimwright.Dwg.Plugin.Handlers;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    // Each cluster owns its transaction. No layout, style, layer or scale policy is applied.
    internal static class RewriteExecutor
    {
        public static RewriteResult Execute(Document doc, CollapseAndRewriteInput input, RewriteRequest request)
        {
            try
            {
                Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
                Require(LineweightTargetPreflight.TryCheckDrawing(doc, out var error), error);
                var db = doc.Database;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var sources = new List<Entity> { ReadTarget(db, tx, request.AnchorHandle, request.CreatesMText) };
                    foreach (var handle in request.DeleteHandles) sources.Add(ReadTarget(db, tx, handle, true));
                    var anchor = sources[0];
                    var directOwner = tx.GetObject(anchor.OwnerId, OpenMode.ForRead);
                    var owner = OwningRecord(tx, anchor);
                    if (request.CreatesMText)
                    {
                        Require(directOwner is BlockTableRecord, "merge anchor must belong directly to a block record");
                        foreach (var source in sources)
                            Require(source.OwnerId == anchor.OwnerId, "all merge sources must have the same owner");
                    }
                    var geometry = ReadGeometry(anchor, request.CreatesMText);
                    var originalLayerId = anchor.LayerId;
                    if (request.CreatesMText)
                    {
                        foreach (var source in sources)
                        {
                            var sourceGeometry = ReadGeometry(source, true);
                            Require(SameVector(sourceGeometry.Normal.GetNormal(), geometry.Normal.GetNormal()) &&
                                SameVector(sourceGeometry.Direction.GetNormal(), geometry.Direction.GetNormal()),
                                "merge sources must share the anchor normal and reading direction");
                            Require(Math.Abs((sourceGeometry.Location - geometry.Location).DotProduct(geometry.Normal.GetNormal())) <= 1e-8,
                                "merge sources must lie in the anchor plane");
                        }
                    }
                    double height = request.TextHeight ?? geometry.Height;
                    double width = request.MtextWidth ?? (request.CreatesMText
                        ? MergeWidth(sources, geometry) : geometry.Width);
                    Require(Finite(height) && height > 0 && Finite(width) && width >= 0, "text height or width is invalid");
                    if (!request.CreatesMText && anchor is DBText)
                    {
                        Require(!request.MtextWidth.HasValue, "mtext_width requires MText, MLeader text or conversion to MText");
                        Require(request.NewText.IndexOfAny(new[] { '\r', '\n' }) < 0 && !request.NewText.Contains("\\P"),
                            "DBText and single-line attributes require single-line new_text; request conversion for multiline text");
                    }
                    // Every target, owner, layer, style and geometry is checked ForRead before any write-open.
                    Require(DocumentGuard.Validate(doc, input.ExpectedDocument, input.ExpectedFingerprint));
                    Require(LineweightTargetPreflight.TryCheckDrawing(doc, out error), error);
                    foreach (var source in sources) { source.UpgradeOpen(); Require(source.IsWriteEnabled, "text cannot be opened for writing"); }
                    Entity resultEntity;
                    RewriteAction action;
                    if (request.CreatesMText)
                    {
                        owner.UpgradeOpen(); Require(owner.IsWriteEnabled, "owner cannot be opened for writing");
                        var created = new MText();
                        try
                        {
                            created.SetDatabaseDefaults(db);
                            // Native entity properties are inherited explicitly, without a team style/layer policy.
                            created.LayerId = anchor.LayerId;
                            created.Color = anchor.Color;
                            created.LinetypeId = anchor.LinetypeId;
                            created.LinetypeScale = anchor.LinetypeScale;
                            created.LineWeight = anchor.LineWeight;
                            created.Transparency = anchor.Transparency;
                            created.TextStyleId = geometry.StyleId;
                            created.Normal = geometry.Normal;
                            created.Direction = geometry.Direction;
                            created.Attachment = geometry.Attachment;
                            created.Location = geometry.Location;
                            created.TextHeight = height;
                            created.Width = width;
                            created.Contents = request.NewText;
                            owner.AppendEntity(created);
                            tx.AddNewlyCreatedDBObject(created, true);
                            foreach (var source in sources) source.Erase();
                            resultEntity = created;
                        }
                        catch { if (created.ObjectId.IsNull) created.Dispose(); throw; }
                        action = owner.IsLayout ? RewriteAction.Collapse : RewriteAction.RewriteInBlock;
                    }
                    else
                    {
                        Update(anchor, request);
                        resultEntity = anchor;
                        action = RewriteAction.Update;
                    }
                    var actual = ReadGeometry(resultEntity, false);
                    Require(ReadContent(resultEntity) == request.NewText, "native text readback mismatch; cluster aborted");
                    Require(SameNumber(actual.Height, height) && SameNumber(actual.Width, width),
                        "native text dimension readback mismatch; cluster aborted");
                    if (resultEntity is MLeader resultLeader && request.TextHeight.HasValue)
                        Require(SameNumber(resultLeader.TextHeight, request.TextHeight.Value),
                            "native MLeader text height readback mismatch; cluster aborted");
                    if (!request.CreatesMText)
                    {
                        Require((actual.Location - geometry.Location).Length <= 1e-8 &&
                            SameVector(actual.Normal, geometry.Normal) && SameVector(actual.Direction, geometry.Direction) &&
                            actual.StyleId == geometry.StyleId && resultEntity.LayerId == originalLayerId &&
                            actual.Attachment == geometry.Attachment && actual.Justification == geometry.Justification,
                            "native in-place anchor, orientation, style, layer or justification changed; cluster aborted");
                    }
                    var result = new RewriteResult
                    {
                        Ok = true, AnchorHandle = request.AnchorHandle, NewHandle = resultEntity.Handle.ToString(),
                        Action = action, DeletedCount = request.CreatesMText ? sources.Count : 0,
                        OwnerHandle = resultEntity.OwnerId.Handle.ToString(), OwnerName = owner.Name,
                        OwnerIsLayout = owner.IsLayout, FinalWidth = actual.Width, FinalTextHeight = actual.Height
                    };
                    Require(Finite(actual.Height) && actual.Height > 0 && Finite(actual.Width) && actual.Width >= 0,
                        "native text dimension readback is invalid; cluster aborted");
                    tx.Commit();
                    return result;
                }
            }
            catch (Exception ex)
            {
                return new RewriteResult { AnchorHandle = request.AnchorHandle, Ok = false,
                    Error = ErrorSanitizer.Sanitize(ex.Message) };
            }
        }

        private static Entity ReadTarget(Database db, Transaction tx, string handle, bool merging)
        {
            Require(CadHandleResolver.TryResolve(db, handle, out var id, out var error), error);
            Require(!id.IsErased && !id.IsEffectivelyErased, "text is erased: " + handle);
            var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
            Require(entity != null, "handle is not an entity: " + handle);
            bool ordinary = entity.GetType() == typeof(DBText) || entity.GetType() == typeof(MText);
            Require(ordinary || !merging && (entity.GetType() == typeof(AttributeReference) || entity.GetType() == typeof(MLeader)),
                merging ? "merge only supports ordinary DBText and MText; attributes and leaders cannot be erased"
                    : "target must be ordinary DBText, MText, single-line AttributeReference or MLeader MText");
            Require(!entity.HasFields && entity.Annotative != AnnotativeStates.True,
                "field-bearing or annotative text is unsupported: " + handle);
            var content = ReadContent(entity);
            Require(content != null && !content.Contains("%<") && content.IndexOf('\0') < 0,
                "field codes or null characters in existing text are unsupported: " + handle);
            Require(LineweightTargetPreflight.TryCheckEntity(tx, entity, out error), error);
            var owner = OwningRecord(tx, entity);
            Require(!owner.IsDependent && !owner.IsFromExternalReference && !owner.IsFromOverlayReference,
                "xref or dependent owner is unsupported");
            Require(owner.IsLayout || !owner.IsAnonymous && !owner.IsDynamicBlock,
                "anonymous or dynamic block definitions are unsupported");
            if (entity is AttributeReference attribute)
            {
                Require(!attribute.IsMTextAttribute, "multiline attributes are unsupported");
                var reference = tx.GetObject(attribute.OwnerId, OpenMode.ForRead) as BlockReference;
                Require(reference != null && reference.Annotative != AnnotativeStates.True, "attribute owner is unavailable or annotative");
                Require(LineweightTargetPreflight.TryCheckEntity(tx, reference, out error), error);
            }
            var geometry = ReadGeometry(entity, merging);
            var style = tx.GetObject(geometry.StyleId, OpenMode.ForRead) as TextStyleTableRecord;
            Require(style != null && !style.IsErased && !style.IsDependent && !style.IsShapeFile && style.Annotative != AnnotativeStates.True,
                "text style is unavailable, dependent, a shape style or annotative");
            Require(!merging || !style.IsVertical, "vertical text style conversion is unsupported");
            return entity;
        }

        private static BlockTableRecord OwningRecord(Transaction tx, Entity entity)
        {
            var direct = tx.GetObject(entity.OwnerId, OpenMode.ForRead);
            if (direct is BlockReference reference)
                direct = tx.GetObject(reference.OwnerId, OpenMode.ForRead);
            var owner = direct as BlockTableRecord;
            Require(owner != null && !owner.IsErased, "text has no valid owning block record");
            return owner;
        }

        private static TextGeometry ReadGeometry(Entity entity, bool converting)
        {
            if (entity is DBText text)
            {
                Require(Finite(text.Height) && text.Height > 0 && Finite(text.Rotation), "DBText height or rotation is invalid");
                CheckNormal(text.Normal);
                Require(Enum.IsDefined(typeof(TextHorizontalMode), text.HorizontalMode) && Enum.IsDefined(typeof(TextVerticalMode), text.VerticalMode),
                    "invalid DBText justification");
                Require(text.HorizontalMode != TextHorizontalMode.TextAlign && text.HorizontalMode != TextHorizontalMode.TextFit,
                    "aligned/fit DBText requires a separate two-anchor operation");
                var direction = Vector3d.XAxis.TransformBy(Matrix3d.PlaneToWorld(text.Normal)).RotateBy(text.Rotation, text.Normal);
                var location = text.IsDefaultAlignment ? text.Position : text.AlignmentPoint;
                CheckPoint(location);
                var attachment = AttachmentPoint.TopLeft;
                if (converting)
                {
                    Require(!text.IsMirroredInX && !text.IsMirroredInY && Math.Abs(text.Oblique) <= 1e-12,
                        "mirrored or oblique DBText conversion is unsupported");
                    Require(text.HorizontalMode == TextHorizontalMode.TextLeft && text.VerticalMode == TextVerticalMode.TextBase,
                        "DBText conversion requires left/baseline justification; other justification requires an explicit placement operation");
                    // Move in the text's own plane, retaining the full owner-local Z coordinate.
                    location += text.Normal.GetNormal().CrossProduct(direction.GetNormal()) * text.Height;
                }
                return new TextGeometry { Location = location, Normal = text.Normal, Direction = direction,
                    Attachment = attachment, StyleId = text.TextStyleId, Height = text.Height, Width = 0,
                    Justification = (int)text.HorizontalMode * 100 + (int)text.VerticalMode };
            }
            if (entity is MText mtext) return ReadMText(mtext);
            var leader = entity as MLeader;
            Require(leader != null && leader.ContentType == ContentType.MTextContent, "MLeader must contain MText");
            using (var inner = leader.MText)
            {
                Require(inner != null && !inner.HasFields && inner.Annotative != AnnotativeStates.True,
                    "MLeader MText is unavailable, field-bearing or annotative");
                return ReadMText(inner);
            }
        }

        private static TextGeometry ReadMText(MText text)
        {
            CheckNormal(text.Normal); CheckPoint(text.Location);
            Require(Finite(text.Direction.X) && Finite(text.Direction.Y) && Finite(text.Direction.Z) && text.Direction.Length > 1e-12,
                "MText direction is invalid");
            Require(Math.Abs(text.Normal.GetNormal().DotProduct(text.Direction.GetNormal())) <= 1e-9, "MText direction must lie in its normal plane");
            Require((int)text.Attachment >= (int)AttachmentPoint.TopLeft && (int)text.Attachment <= (int)AttachmentPoint.BottomRight,
                "MText attachment is unsupported");
            Require(Finite(text.TextHeight) && text.TextHeight > 0 && Finite(text.Width) && text.Width >= 0,
                "MText height or width is invalid");
            return new TextGeometry { Location = text.Location, Normal = text.Normal, Direction = text.Direction,
                Attachment = text.Attachment, StyleId = text.TextStyleId, Height = text.TextHeight, Width = text.Width };
        }

        private static double MergeWidth(List<Entity> sources, TextGeometry geometry)
        {
            if (geometry.Width > 0) return geometry.Width;
            // Project native bounds into the anchor's reading direction. Never invent a width on failed bounds.
            var direction = geometry.Direction.GetNormal();
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            foreach (var source in sources)
            {
                Extents3d bounds;
                try { bounds = source.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { throw new InvalidOperationException("native text bounds unavailable; supply mtext_width explicitly"); }
                CheckPoint(bounds.MinPoint); CheckPoint(bounds.MaxPoint);
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Point3d((corner & 1) == 0 ? bounds.MinPoint.X : bounds.MaxPoint.X,
                        (corner & 2) == 0 ? bounds.MinPoint.Y : bounds.MaxPoint.Y,
                        (corner & 4) == 0 ? bounds.MinPoint.Z : bounds.MaxPoint.Z);
                    double distance = (point - geometry.Location).DotProduct(direction);
                    minimum = Math.Min(minimum, distance); maximum = Math.Max(maximum, distance);
                }
            }
            return maximum - minimum;
        }

        private static void Update(Entity entity, RewriteRequest request)
        {
            if (entity is DBText text)
            {
                bool defaultAlignment = text.IsDefaultAlignment;
                var anchor = defaultAlignment ? text.Position : text.AlignmentPoint;
                text.TextString = request.NewText;
                if (request.TextHeight.HasValue) text.Height = request.TextHeight.Value;
                if (defaultAlignment) text.Position = anchor; else text.AlignmentPoint = anchor;
            }
            else if (entity is MText mtext)
            {
                mtext.Contents = request.NewText;
                if (request.TextHeight.HasValue) mtext.TextHeight = request.TextHeight.Value;
                if (request.MtextWidth.HasValue) mtext.Width = request.MtextWidth.Value;
            }
            else
            {
                var leader = (MLeader)entity;
                if (request.TextHeight.HasValue) leader.TextHeight = request.TextHeight.Value;
                using (var inner = leader.MText)
                {
                    inner.Contents = request.NewText;
                    if (request.TextHeight.HasValue) inner.TextHeight = request.TextHeight.Value;
                    if (request.MtextWidth.HasValue) inner.Width = request.MtextWidth.Value;
                    leader.MText = inner;
                }
            }
        }

        private static string ReadContent(Entity entity)
        {
            if (entity is DBText text) return text.TextString;
            if (entity is MText mtext) return mtext.Contents;
            var leader = entity as MLeader;
            Require(leader != null && leader.ContentType == ContentType.MTextContent, "MLeader must contain MText");
            using (var inner = leader.MText) return inner?.Contents;
        }

        private static void CheckNormal(Vector3d normal)
        {
            Require(Finite(normal.X) && Finite(normal.Y) && Finite(normal.Z) && Finite(normal.Length) && normal.Length > 1e-12,
                "text normal is invalid");
        }
        private static void CheckPoint(Point3d point) => Require(Finite(point.X) && Finite(point.Y) && Finite(point.Z), "text position or bounds is invalid");
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool SameNumber(double actual, double expected) => Finite(actual) && Math.Abs(actual - expected) <= 1e-9 * Math.Max(1, Math.Abs(expected));
        private static bool SameVector(Vector3d actual, Vector3d expected) => (actual - expected).Length <= 1e-9;
        private static void Require(string error) { if (error != null) throw new InvalidOperationException(error); }
        private static void Require(bool valid, string error) { if (!valid) throw new InvalidOperationException(error); }
        private sealed class TextGeometry
        {
            internal Point3d Location;
            internal Vector3d Normal, Direction;
            internal AttachmentPoint Attachment;
            internal ObjectId StyleId;
            internal double Height, Width;
            internal int Justification;
        }
    }
}
