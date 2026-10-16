using System;

namespace Bimwright.Dwg.Plugin.Drawing
{
    public static class DrawingSavePolicy
    {
        public static string Preflight(string savedDocumentPath, bool? readOnly,
            string outputPath, bool confirm, string[] otherOpenPaths)
        {
            string target;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                if (!confirm) return "saving the active drawing file requires confirm=true";
                if (savedDocumentPath == null)
                    return "drawing has not been saved yet; specify an absolute output_path";
                if (readOnly == true) return "active drawing file is read-only";
                target = savedDocumentPath;
            }
            else target = outputPath;
            target = DrawingIdentityPolicy.NormalizeDwgPath(target);
            if (target == null) return "output_path must be a fully qualified DWG path";
            if (readOnly == true && string.Equals(target, DrawingIdentityPolicy.NormalizeDwgPath(savedDocumentPath), StringComparison.OrdinalIgnoreCase))
                return "active drawing file is read-only";
            foreach (var other in otherOpenPaths ?? Array.Empty<string>())
            {
                var otherPath = DrawingIdentityPolicy.NormalizeDwgPath(other);
                if (otherPath != null && string.Equals(target, otherPath, StringComparison.OrdinalIgnoreCase))
                    return "output_path is open in another AutoCAD document; close that document first";
            }
            return null;
        }
    }
}
