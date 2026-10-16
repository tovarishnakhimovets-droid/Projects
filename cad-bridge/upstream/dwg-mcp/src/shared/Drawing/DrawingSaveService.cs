using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Export;

namespace Bimwright.Dwg.Plugin.Drawing
{
    internal static class DrawingSaveService
    {
        internal static string Save(Document doc, string outputPath, bool? confirm, bool overwriteExisting, bool allowRepoOutput, out string error)
        {
            error = null;

            var identity = DocumentGuard.Capture(doc);
            var otherOpenPaths = new List<string>();
            foreach (Document other in Application.DocumentManager)
            {
                if (ReferenceEquals(other, doc)) continue;
                otherOpenPaths.Add(other.Name);
                otherOpenPaths.Add(other.Database.Filename);
            }
            error = DrawingSavePolicy.Preflight(identity.DocumentPath,
                identity.IsReadOnlyDocument || identity.IsReadOnlyFile == true,
                outputPath, confirm == true, otherOpenPaths.ToArray());
            if (error != null) return null;

            string pathError = null;
            var normalizedPath = string.IsNullOrWhiteSpace(outputPath)
                ? identity.DocumentPath
                : ExportPathPolicy.ValidateAndNormalize(
                    outputPath,
                    ".dwg",
                    overwriteExisting,
                    allowRepoOutput,
                    out pathError);
            if (normalizedPath == null)
            {
                error = pathError;
                return null;
            }
            if (File.Exists(normalizedPath) &&
                (File.GetAttributes(normalizedPath) & FileAttributes.ReadOnly) != 0)
            {
                error = "target drawing file is read-only";
                return null;
            }
            // bBakAndRename=true retains the .bak and updates the editor document
            // name. The two-argument overload does not provide that lifecycle.
            doc.Database.SaveAs(normalizedPath, true, DwgVersion.Current, doc.Database.SecurityParameters);
            var after = DocumentGuard.Capture(doc);
            if (!string.Equals(after.DocumentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "file write completed but active document name did not update; inspect drawing info before retrying";
                return null;
            }
            return normalizedPath;
        }
    }
}
