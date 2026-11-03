using System;
using System.IO;

namespace Bimwright.Dwg.Plugin.Drawing
{
    // Host-free identity policy. A template filename is never a saved DWG identity.
    public static class DrawingIdentityPolicy
    {
        public static string SavedDocumentPath(string documentName, bool isTitled)
        {
            if (!isTitled) return null;
            return NormalizeDwgPath(documentName);
        }

        // The active editor may be a named DXF; this is not a saved DWG identity.
        public static string ActiveDocumentPath(string name, bool isTitled)
        {
            return isTitled ? NormalizeCadPath(name, true) : null;
        }

        public static string NormalizeDwgPath(string value) => NormalizeCadPath(value, false);
        public static string NormalizeActiveDocumentPath(string value) => NormalizeCadPath(value, true);

        private static string NormalizeCadPath(string value, bool allowDxf)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Replace('/', '\\');
            // Reject drive-relative, root-relative and URL paths on Windows.
            bool absolute = value.Length >= 3 && char.IsLetter(value[0]) &&
                value[1] == ':' && value[2] == '\\';
            bool unc = value.StartsWith("\\\\", StringComparison.Ordinal) &&
                value.TrimStart('\\').Split('\\').Length >= 3;
            if (!absolute && !unc) return null;
            try
            {
                var extension = Path.GetExtension(value);
                if (!string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase) &&
                    !(allowDxf && string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase)))
                    return null;
                return Path.GetFullPath(value);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        public static string Validate(string actualPath, string actualFingerprint,
            string expectedDocument, string expectedFingerprint)
            => ValidateCore(actualPath, actualFingerprint, expectedDocument, expectedFingerprint, false);

        public static string ValidateActive(string actualPath, string actualFingerprint,
            string expectedDocument, string expectedFingerprint)
            => ValidateCore(actualPath, actualFingerprint, expectedDocument, expectedFingerprint, true);

        private static string ValidateCore(string actualPath, string actualFingerprint,
            string expectedDocument, string expectedFingerprint, bool allowDxf)
        {
            var expectedPath = NormalizeCadPath(expectedDocument, allowDxf);
            if (expectedPath == null)
                return allowDxf ? "expected_document must be a fully qualified DWG or DXF path" : "expected_document must be a fully qualified DWG path";
            if (actualPath == null)
                return allowDxf ? "active drawing has no named DWG or DXF identity" : "active drawing has no saved DWG identity; save it explicitly first";
            actualPath = NormalizeCadPath(actualPath, allowDxf);
            if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                return "active document does not match expected_document";
            Guid expected, actual;
            if (!Guid.TryParse(expectedFingerprint, out expected) || expected == Guid.Empty)
                return "expected_fingerprint must be a nonempty drawing GUID";
            if (!Guid.TryParse(actualFingerprint, out actual) || actual == Guid.Empty || actual != expected)
                return "active drawing fingerprint does not match expected_fingerprint";
            return null;
        }
    }
}
