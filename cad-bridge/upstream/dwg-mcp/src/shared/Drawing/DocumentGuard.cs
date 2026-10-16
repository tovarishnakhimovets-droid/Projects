using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Drawing
{
    internal sealed class DrawingDocumentSnapshot
    {
        internal string DocumentPath { get; set; }
        internal string DocumentName { get; set; }
        internal string DatabaseFilename { get; set; }
        internal string OriginalFilename { get; set; }
        internal string Fingerprint { get; set; }
        internal bool IsTitled { get; set; }
        internal bool Exists { get; set; }
        internal bool IsReadOnlyDocument { get; set; }
        internal bool? IsReadOnlyFile { get; set; }
        internal int? DatabaseModified { get; set; }
    }

    internal static class DocumentGuard
    {
        internal static DrawingDocumentSnapshot Capture(Document doc)
        {
            var db = doc.Database;
            var snapshot = new DrawingDocumentSnapshot
            {
                DocumentName = Read(() => doc.Name),
                DatabaseFilename = Read(() => db.Filename),
                OriginalFilename = Read(() => db.OriginalFileName),
                Fingerprint = db.FingerprintGuid,
                IsReadOnlyDocument = doc.IsReadOnly,
                // This system variable is read only on the locked active document.
                IsTitled = ReadVariable("DWGTITLED") == 1,
                DatabaseModified = ReadVariable("DBMOD")
            };
            snapshot.DocumentPath = DrawingIdentityPolicy.SavedDocumentPath(
                snapshot.DocumentName, snapshot.IsTitled);
            snapshot.Exists = snapshot.DocumentPath != null && File.Exists(snapshot.DocumentPath);
            if (snapshot.Exists)
            {
                try
                {
                    snapshot.IsReadOnlyFile = (File.GetAttributes(snapshot.DocumentPath) & FileAttributes.ReadOnly) != 0;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return snapshot;
        }

        internal static string Validate(Document doc, string expectedDocument, string expectedFingerprint)
        {
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
                return "active document changed before preflight";
            var snapshot = Capture(doc);
            return DrawingIdentityPolicy.Validate(snapshot.DocumentPath, snapshot.Fingerprint,
                expectedDocument, expectedFingerprint);
        }

        private static string Read(Func<string> read)
        {
            try { return read(); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        private static int? ReadVariable(string name)
        {
            try { return Convert.ToInt32(Application.GetSystemVariable(name)); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
            catch (InvalidCastException) { return null; }
        }
    }
}
