using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // Only the geometric database pass is replaced. Initialize, attachment,
    // event dispatch, guards, failure cleanup and scheduling use production code.
    internal static partial class BlockAngleBindingService
    {
        internal static readonly List<Document> LifecycleRefreshedDocuments = new List<Document>();
        internal static Action<Document> LifecycleRefreshOverride;

        private static RefreshReadback Refresh(RefreshContext context)
        {
            LifecycleRefreshedDocuments.Add(context.Document);
            LifecycleRefreshOverride?.Invoke(context.Document);
            return new RefreshReadback { Snapshot = "stable-native-angle", Wrote = false };
        }
    }
}
