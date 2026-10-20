using System;
using System.Collections.Generic;
using System.Globalization;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    public sealed class BlockInspectionInput
    {
        public const int DefaultMaxEntities = 500;
        public const int MaximumMaxEntities = 5000;
        public const int DefaultMaxMetadataObjects = 200;
        public const int MaximumMaxMetadataObjects = 1000;
        public const int MaximumResponseBytes = 1024 * 1024;
        private static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
        {
            "handle", "expected_document", "expected_fingerprint", "include_definition",
            "max_entities", "max_metadata_objects"
        };
        private BlockInspectionInput() { }
        public string Handle { get; private set; }
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public bool IncludeDefinition { get; private set; }
        public int MaxEntities { get; private set; }
        public int MaxMetadataObjects { get; private set; }

        // Identical host-free preflight at the MCP and native boundaries.
        public static bool TryParse(JToken parameters, out BlockInspectionInput input, out string error)
        {
            input = null;
            error = null;
            if (!(parameters is JObject obj))
            {
                error = "inspect_block params must be an object";
                return false;
            }
            foreach (var field in obj.Properties())
            {
                if (!Fields.Contains(field.Name))
                {
                    error = "unknown inspect_block field: " + field.Name;
                    return false;
                }
            }
            var handleToken = obj["handle"];
            if (handleToken?.Type != JTokenType.String)
            {
                error = "handle must be a nonempty hexadecimal string";
                return false;
            }
            var handle = handleToken.Value<string>();
            if (string.IsNullOrEmpty(handle) || handle.Length > 16)
            {
                error = "handle must contain 1 to 16 hexadecimal characters";
                return false;
            }
            foreach (var ch in handle)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    error = "handle must contain only hexadecimal characters";
                    return false;
                }
            }
            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                error = "handle is outside the supported positive signed 64-bit range";
                return false;
            }
            string document = null, fingerprint = null;
            var docToken = obj["expected_document"];
            var fpToken = obj["expected_fingerprint"];
            bool hasDoc = docToken != null && docToken.Type != JTokenType.Null;
            bool hasFp = fpToken != null && fpToken.Type != JTokenType.Null;
            if (hasDoc != hasFp)
            {
                error = "expected_document and expected_fingerprint must be supplied together";
                return false;
            }
            if (hasDoc)
            {
                if (docToken.Type != JTokenType.String || fpToken.Type != JTokenType.String)
                {
                    error = "expected_document and expected_fingerprint must be strings";
                    return false;
                }
                var path = docToken.Value<string>();
                if (path == null || path.Length > 4096 || (document = DrawingIdentityPolicy.NormalizeDwgPath(path)) == null)
                {
                    error = "expected_document must be a fully qualified DWG path";
                    return false;
                }
                if (!Guid.TryParse(fpToken.Value<string>(), out var guid) || guid == Guid.Empty)
                {
                    error = "expected_fingerprint must be a nonempty drawing GUID";
                    return false;
                }
                fingerprint = guid.ToString("D");
            }
            var includeDefinition = true;
            var includeToken = obj["include_definition"];
            if (includeToken != null && includeToken.Type != JTokenType.Null)
            {
                if (includeToken.Type != JTokenType.Boolean)
                {
                    error = "include_definition must be a boolean";
                    return false;
                }
                includeDefinition = includeToken.Value<bool>();
            }
            if (!ReadInteger(obj, "max_entities", DefaultMaxEntities, 1, MaximumMaxEntities, out var maxEntities, out error) ||
                !ReadInteger(obj, "max_metadata_objects", DefaultMaxMetadataObjects, 0, MaximumMaxMetadataObjects, out var maxMetadata, out error))
                return false;
            input = new BlockInspectionInput
            {
                Handle = value.ToString("X", CultureInfo.InvariantCulture),
                ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                IncludeDefinition = includeDefinition, MaxEntities = maxEntities, MaxMetadataObjects = maxMetadata
            };
            return true;
        }
        private static bool ReadInteger(JObject obj, string name, int fallback, int min, int max,
            out int value, out string error)
        {
            value = fallback;
            error = null;
            var token = obj[name];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.Integer ||
                !int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
            {
                error = name + " must be an integer between " + min + " and " + max;
                return false;
            }
            return true;
        }
    }
}
