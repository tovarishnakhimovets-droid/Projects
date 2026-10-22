// Test-only resolver seam. Production identity mapping/policy are tested directly;
// linked DocumentInvoker tests supply explicit documents instead of Autodesk hosts.
using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    internal static class DocumentRouting
    {
        internal static Func<string, Document> ResolveOverride;

        internal static Document Resolve(string documentId, bool allowMissingActive = false)
        {
            if (ResolveOverride != null) return ResolveOverride(documentId);
            if (documentId != null) throw new InvalidOperationException("selected document is no longer open");
            var active = Application.DocumentManager.MdiActiveDocument;
            if (active == null && !allowMissingActive) throw new InvalidOperationException("no active document");
            return active;
        }
    }
}
