using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Proxy
{
    /// <summary>Host-free request rules shared by the MCP facade and native handlers.</summary>
    public static class ProxyContract
    {
        public const int MaxResponseBytes = 2 * 1024 * 1024;
        public const int MaxMetadataCharacters = 4096;
        public const int MaxTargets = 1000;

        public static bool TryHandle(string text, out long value)
        {
            value = 0;
            return !string.IsNullOrEmpty(text) && text.Length <= 16 &&
                text.All(Uri.IsHexDigit) &&
                long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        public static string Classify(string originalClass, string originalDxf, string application)
        {
            // This is a display hint, never permission to erase or select extra objects.
            var text = (originalClass ?? "") + " " + (originalDxf ?? "") + " " + (application ?? "");
            if (Contains(text, "Aecc") || Contains(text, "Civil")) return "civil";
            if (Contains(text, "Aec")) return "aec";
            if (Contains(text, "AcMap") || Contains(text, "AcDbMap") || Contains(text, "Map 3D")) return "map";
            return "unknown";
        }

        internal static bool TryIdentity(JObject obj, bool required, out string document, out string fingerprint, out string error)
        {
            document = fingerprint = error = null;
            var hasDocument = Provided(obj["expected_document"]);
            var hasFingerprint = Provided(obj["expected_fingerprint"]);
            if (!required && !hasDocument && !hasFingerprint) return true;
            if (!hasDocument || !hasFingerprint)
            {
                error = "expected_document and expected_fingerprint must be supplied together";
                return false;
            }
            if (!TryString(obj["expected_document"], false, out document) ||
                DrawingIdentityPolicy.NormalizeDwgPath(document) == null)
            {
                error = "expected_document must be a full absolute DWG path";
                return false;
            }
            if (!TryString(obj["expected_fingerprint"], false, out fingerprint) ||
                !Guid.TryParse(fingerprint, out var parsed) || parsed == Guid.Empty)
            {
                error = "expected_fingerprint must be a non-empty drawing GUID";
                return false;
            }
            fingerprint = parsed.ToString("D");
            return true;
        }

        internal static bool TryInteger(JObject obj, string name, int fallback, int min, int max, out int value, out string error)
        {
            value = fallback;
            error = null;
            var token = obj[name];
            if (!Provided(token)) return true;
            if (token.Type == JTokenType.Integer)
            {
                try
                {
                    var number = token.Value<long>();
                    if (number >= min && number <= max) { value = (int)number; return true; }
                }
                catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException) { }
            }
            error = name + " must be an integer between " + min + " and " + max;
            return false;
        }

        internal static bool Provided(JToken token) => token != null && token.Type != JTokenType.Null;

        internal static bool TryString(JToken token, bool allowEmpty, out string text)
        {
            text = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return text != null && text.Length <= MaxMetadataCharacters &&
                (allowEmpty || !string.IsNullOrWhiteSpace(text));
        }

        private static bool Contains(string text, string term) => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class ProxyInventoryInput
    {
        public long AfterHandle { get; private set; }
        public int MaxHandles { get; private set; }
        public int BudgetMilliseconds { get; private set; }
        public int MaxResults { get; private set; }
        public int OwnerDepth { get; private set; }
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }

        public static bool TryParse(JToken parameters, out ProxyInventoryInput input, out string error)
        {
            input = null;
            error = null;
            var obj = parameters == null || parameters.Type == JTokenType.Null ? new JObject() : parameters as JObject;
            if (obj == null) { error = "params must be an object"; return false; }
            if (ProxyContract.Provided(obj["scope"]) &&
                (obj["scope"].Type != JTokenType.String || obj["scope"].Value<string>() != "full_database"))
            { error = "scope must be full_database"; return false; }
            long after = 0;
            if (ProxyContract.Provided(obj["after_handle"]) &&
                (obj["after_handle"].Type != JTokenType.String || !ProxyContract.TryHandle(obj["after_handle"].Value<string>(), out after)))
            { error = "after_handle must be a positive hexadecimal handle in the supported range"; return false; }
            if (!ProxyContract.TryIdentity(obj, after != 0, out var document, out var fingerprint, out error)) return false;
            if (!ProxyContract.TryInteger(obj, "max_handles", 500000, 1, 1000000, out var maxHandles, out error) ||
                !ProxyContract.TryInteger(obj, "budget_ms", 5000, 100, 20000, out var budget, out error) ||
                !ProxyContract.TryInteger(obj, "max_results", 1000, 1, 5000, out var maxResults, out error) ||
                !ProxyContract.TryInteger(obj, "owner_depth", 8, 1, 32, out var depth, out error)) return false;
            input = new ProxyInventoryInput { AfterHandle = after, ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                MaxHandles = maxHandles, BudgetMilliseconds = budget, MaxResults = maxResults, OwnerDepth = depth };
            return true;
        }
    }

    public sealed class ProxyEraseInput
    {
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public IReadOnlyList<ProxyEraseExpectation> Targets { get; private set; }
        public bool DryRun { get; private set; }

        public static bool TryParse(JToken parameters, out ProxyEraseInput input, out string error)
        {
            input = null;
            error = null;
            if (!(parameters is JObject obj)) { error = "params must be an object"; return false; }
            if (!ProxyContract.TryIdentity(obj, true, out var document, out var fingerprint, out error)) return false;
            if (!(obj["targets"] is JArray targets) || targets.Count == 0 || targets.Count > ProxyContract.MaxTargets)
            { error = "targets must contain 1-1000 explicit proxy-object expectations"; return false; }
            var distinct = new HashSet<long>();
            var parsedTargets = new List<ProxyEraseExpectation>();
            foreach (var token in targets)
            {
                if (!(token is JObject target) || !ProxyContract.TryString(target["handle"], false, out var handle) ||
                    !ProxyContract.TryHandle(handle, out var handleValue))
                { error = "each target must have a positive hexadecimal handle"; return false; }
                if (!distinct.Add(handleValue)) { error = "targets must not contain duplicate handles or aliases"; return false; }
                if (!ProxyContract.TryString(target["expected_original_class"], false, out var className) ||
                    !ProxyContract.TryString(target["expected_original_dxf"], true, out var dxfName) ||
                    !ProxyContract.TryString(target["expected_application"], true, out var application))
                { error = "each target requires exact expected_original_class, expected_original_dxf and expected_application strings"; return false; }
                if (!ProxyContract.TryString(target["expected_owner_handle"], false, out var owner) ||
                    !ProxyContract.TryHandle(owner, out var ownerValue))
                { error = "each target requires expected_owner_handle as a positive hexadecimal handle"; return false; }
                parsedTargets.Add(new ProxyEraseExpectation(handleValue, className, dxfName, application, ownerValue));
            }
            var dryRun = false;
            if (ProxyContract.Provided(obj["dry_run"]))
            {
                if (obj["dry_run"].Type != JTokenType.Boolean) { error = "dry_run must be a boolean"; return false; }
                dryRun = obj["dry_run"].Value<bool>();
            }
            input = new ProxyEraseInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint, Targets = parsedTargets, DryRun = dryRun };
            return true;
        }
    }

    public sealed class ProxyEraseExpectation
    {
        public ProxyEraseExpectation(long handle, string className, string dxfName, string application, long ownerHandle)
        { HandleValue = handle; OriginalClass = className; OriginalDxf = dxfName; Application = application; OwnerHandleValue = ownerHandle; }
        public long HandleValue { get; }
        public string Handle => HandleValue.ToString("X", CultureInfo.InvariantCulture);
        public string OriginalClass { get; }
        public string OriginalDxf { get; }
        public string Application { get; }
        public long OwnerHandleValue { get; }

        public bool Matches(string className, string dxfName, string application, long ownerHandle)
            => string.Equals(OriginalClass, className, StringComparison.Ordinal) &&
                string.Equals(OriginalDxf, dxfName, StringComparison.Ordinal) &&
                string.Equals(Application, application, StringComparison.Ordinal) && OwnerHandleValue == ownerHandle;
    }
}
