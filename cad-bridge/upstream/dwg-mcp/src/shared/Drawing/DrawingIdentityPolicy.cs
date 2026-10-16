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

        public static string NormalizeDwgPath(string value)
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
                if (!string.Equals(Path.GetExtension(value), ".dwg", StringComparison.OrdinalIgnoreCase))
                    return null;
                return Path.GetFullPath(value);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        public static string Validate(string actualPath, string actualFingerprint,
            string expectedDocument, string expectedFingerprint)
        {
            var expectedPath = NormalizeDwgPath(expectedDocument);
            if (expectedPath == null)
                return "expected_document must be a fully qualified DWG path";
            if (actualPath == null)
                return "active drawing has no saved DWG identity; save it explicitly first";
            actualPath = NormalizeDwgPath(actualPath);
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
