using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // Replace native DB access only. Event selection, snapshot comparison,
    // one-shot scheduling, guards and lock cleanup use production code.
    internal static partial class BlockAngleBindingService
    {
        internal static readonly List<Document> LifecycleRefreshedDocuments = new List<Document>();
        internal static readonly List<(Document Document, ObjectId Id)> LifecycleInspectedIds =
            new List<(Document, ObjectId)>();
        internal static readonly Dictionary<Document, Dictionary<ObjectId, BlockReference>> LifecycleReferences =
            new Dictionary<Document, Dictionary<ObjectId, BlockReference>>();
        internal static Action<Document> LifecycleRefreshOverride;
        internal static Action<Document> LifecycleApplyOverride;
        internal static bool LifecyclePendingWrites;
        internal static int LifecycleApplyCount, LifecycleSnapshotReads;
        internal static readonly ObjectId LifecycleAttributeId = new ObjectId(100);
        internal static readonly ObjectId LifecycleReferenceId = new ObjectId(101);
        internal static readonly ObjectId LifecycleDefinitionId = new ObjectId(102);

        internal static int LifecycleOwnWriteCount(Document document)
            => Documents.TryGetValue(document, out var context) ? context.OwnWrites.Count : 0;

        private static AngleSnapshot ReadAngleSnapshot(BlockReference reference)
        {
            if (!reference.IsDynamicBlock) return null;
            LifecycleSnapshotReads++;
            return new AngleSnapshot
            {
                Rotation = reference.Rotation,
                Angles = new Dictionary<string, double>(reference.Angles)
            };
        }

        internal static bool LifecycleSnapshotsEqual(BlockReference first, BlockReference second)
            => ReadAngleSnapshot(first).SameAs(ReadAngleSnapshot(second));

        private static RefreshPlan InspectRefresh(RefreshContext context, Dictionary<ObjectId, AngleSnapshot> changed)
        {
            LifecycleRefreshedDocuments.Add(context.Document);
            LifecycleRefreshOverride?.Invoke(context.Document);
            var plan = new RefreshPlan { StartedAt = Clock.ElapsedMilliseconds };
            foreach (var item in changed)
            {
                LifecycleInspectedIds.Add((context.Document, item.Key));
                var reference = LifecycleReferences[context.Document][item.Key];
                if (item.Value.SameAs(ReadAngleSnapshot(reference)) || !LifecyclePendingWrites) continue;
                plan.Updates.Add(new AttributeRefreshUpdate
                {
                    AttributeId = new ObjectId(1000 + item.Key.Value), ReferenceId = item.Key,
                    DefinitionId = LifecycleDefinitionId, Tag = "ANGLE", OldText = "90", NewText = "91",
                    Binding = new JObject(), ExpectedAngles = JObject.FromObject(reference.Angles),
                    ExpectedRotation = reference.Rotation
                });
            }
            return plan;
        }

        private static void ApplyRefresh(RefreshContext context, RefreshPlan plan)
        {
            LifecycleApplyCount++;
            foreach (var update in plan.Updates)
            {
                context.OwnWrites.Add(update.AttributeId);
                context.OwnWrites.Add(update.ReferenceId);
            }
            LifecycleApplyOverride?.Invoke(context.Document);
            LifecyclePendingWrites = false;
        }
    }
}
