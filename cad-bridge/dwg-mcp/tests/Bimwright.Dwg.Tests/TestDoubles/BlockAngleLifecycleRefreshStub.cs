using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // Only the geometric database pass is replaced. Initialize, attachment,
    // event dispatch, guards, failure cleanup and scheduling use production code.
    internal static partial class BlockAngleBindingService
    {
        internal static readonly List<Document> LifecycleRefreshedDocuments = new List<Document>();
        internal static Action<Document> LifecycleRefreshOverride;
        internal static bool LifecyclePendingWrites;
        internal static int LifecycleApplyCount;
        internal static Action<Document> LifecycleApplyOverride;
        internal static readonly ObjectId LifecycleAttributeId = new ObjectId(100);
        internal static readonly ObjectId LifecycleReferenceId = new ObjectId(101);
        internal static readonly ObjectId LifecycleDefinitionId = new ObjectId(102);

        internal static int LifecycleOwnWriteCount(Document document)
            => Documents.TryGetValue(document, out var context) ? context.OwnWrites.Count : 0;

        private static RefreshPlan InspectRefresh(RefreshContext context)
        {
            LifecycleRefreshedDocuments.Add(context.Document);
            LifecycleRefreshOverride?.Invoke(context.Document);
            var plan = new RefreshPlan { StartedAt = Clock.ElapsedMilliseconds, Snapshot = "stable-native-angle" };
            plan.BoundReferences.Add(LifecycleReferenceId);
            plan.EvaluatedDefinitions.Add(LifecycleDefinitionId);
            if (LifecyclePendingWrites)
                plan.Updates.Add(new AttributeRefreshUpdate
                {
                    AttributeId = LifecycleAttributeId, ReferenceId = LifecycleReferenceId,
                    DefinitionId = LifecycleDefinitionId, Tag = "ANGLE", OldText = "90", NewText = "91",
                    Binding = new JObject(), ExpectedAngles = new JObject()
                });
            return plan;
        }

        private static RefreshReadback ApplyRefresh(RefreshContext context, RefreshPlan plan)
        {
            LifecycleApplyCount++;
            context.OwnWrites.Add(LifecycleAttributeId);
            context.OwnWrites.Add(LifecycleReferenceId);
            LifecycleApplyOverride?.Invoke(context.Document);
            LifecyclePendingWrites = false;
            return new RefreshReadback { Snapshot = plan.Snapshot, Wrote = true };
        }
    }
}
