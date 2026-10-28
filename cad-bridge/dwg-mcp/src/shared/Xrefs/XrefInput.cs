using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Xrefs
{
    // Host-free wire rules. The graph and reference hashes describe inspected state,
    // while the existing document revision invalidates snapshots after native edits.
    public static class XrefContract
    {
        public const int MaxXrefs = 512;
        public const int MaxReferences = 10000;
        public const int MaxEntities = 100000;
        public const int MaxResponseBytes = 2 * 1024 * 1024;
        public const int BudgetMilliseconds = 10000;
        public const int MaxText = 4096;

        public static bool TryHandle(string text, out long value)
        {
            value = 0;
            return !string.IsNullOrEmpty(text) && text.Length <= 16 && text.All(Uri.IsHexDigit) &&
                long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        // Initial native contract is deliberately stricter than selecting one
        // root: do not begin whole-database clone work with any unresolved graph
        // node. Detach irrelevant roots and reread before binding the remainder.
        public static string ValidateBindGraph(JArray nodes)
        {
            if (nodes == null) return "bind requires complete xref graph metadata";
            foreach (var token in nodes)
            {
                if (!(token is JObject node) || node["status"]?.Type != JTokenType.String || node["is_unloaded"]?.Type != JTokenType.Boolean)
                    return "bind requires complete status/load metadata for every graph node";
                if (node.Value<string>("status") != "Resolved" || node.Value<bool>("is_unloaded"))
                    return "bind requires every node of the inspected xref graph to be resolved and loaded in this initial contract; detach irrelevant roots and reread first";
            }
            return null;
        }
        public static string Hash(JToken value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(value).ToString(Formatting.None))))
                    .Replace("-", "").ToLowerInvariant();
        }

        private static JToken Canonical(JToken value)
        {
            if (value is JObject obj)
                return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new JProperty(p.Name, Canonical(p.Value))));
            if (value is JArray array) return new JArray(array.Select(Canonical));
            return value?.DeepClone() ?? JValue.CreateNull();
        }

        public static bool TryIdentity(JObject obj, bool required, out string document, out string fingerprint, out string error)
        {
            document = fingerprint = error = null;
            bool hasDocument = Present(obj["expected_document"]), hasFingerprint = Present(obj["expected_fingerprint"]);
            if (!required && !hasDocument && !hasFingerprint) return true;
            if (!hasDocument || !hasFingerprint)
            { error = "expected_document and expected_fingerprint must be supplied together"; return false; }
            if (!Text(obj["expected_document"], false, out document) || DrawingIdentityPolicy.NormalizeDwgPath(document) == null)
            { error = "expected_document must be a full absolute DWG path"; return false; }
            if (!Text(obj["expected_fingerprint"], false, out fingerprint) || !Guid.TryParse(fingerprint, out var id) || id == Guid.Empty)
            { error = "expected_fingerprint must be a nonempty drawing GUID"; return false; }
            fingerprint = id.ToString("D");
            return true;
        }

        internal static bool Present(JToken token) => token != null && token.Type != JTokenType.Null;
        internal static bool Text(JToken token, bool empty, out string value)
        {
            value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return value != null && value.Length <= MaxText && (empty || !string.IsNullOrWhiteSpace(value));
        }
        internal static bool Integer(JObject obj, string key, int fallback, int min, int max, out int value, out string error)
        {
            value = fallback; error = null;
            if (!Present(obj[key])) return true;
            if (obj[key].Type == JTokenType.Integer)
            {
                try
                {
                    var n = obj[key].Value<long>();
                    if (n >= min && n <= max) { value = (int)n; return true; }
                }
                catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException) { }
            }
            error = key + " must be an integer between " + min + " and " + max; return false;
        }
        internal static bool Digest(JToken token, out string value)
        {
            value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return value != null && value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }
    }

    // Dispatcher deliberately serializes Result only on success. Keep bounded
    // uncertainty/readback details in Error so they survive its failure path.
    public static class XrefFailureDetails
    {
        public static string Format(JObject plan)
        {
            var details = new JObject
            {
                ["operation_status"] = "execution_unknown", ["execution_unknown"] = true,
                ["completed"] = false, ["rollback_verified"] = false,
                ["action"] = Limited(plan.Value<string>("action"), 16),
                ["target_handle"] = Limited(plan.Value<string>("target_handle"), 16),
                ["native_returned"] = plan.Value<bool>("native_returned"),
                ["error"] = Limited(plan.Value<string>("error"), 1024),
                ["readback_error"] = Limited(plan.Value<string>("readback_error"), 512),
                ["next_step"] = "Read current xref state before further writes; never automatically retry."
            };
            if (plan["readback"] is JObject readback)
            {
                var summary = new JObject();
                foreach (var key in new[] { "definition_removed", "is_local", "insert_metadata_complete",
                    "insert_placement_and_clipping_unchanged", "postcondition_verified" })
                    if (readback[key]?.Type == JTokenType.Boolean) summary[key] = readback[key].DeepClone();
                foreach (var key in new[] { "definition_handle", "definition_name", "reason" })
                    if (readback[key]?.Type == JTokenType.String) summary[key] = Limited(readback.Value<string>(key), 512);
                if (readback["remaining_original_insert_handles"] is JArray remaining)
                {
                    summary["remaining_original_insert_count"] = remaining.Count;
                    summary["remaining_original_insert_handles"] = new JArray(remaining.Take(16).Select(t => Limited(t.Value<string>(), 16)));
                    summary["remaining_handles_truncated"] = remaining.Count > 16;
                }
                if (readback["insert_handles"] is JArray inserts) summary["insert_count"] = inserts.Count;
                details["readback"] = summary;
            }
            return "xref native operation has an uncertain outcome; details=" + details.ToString(Formatting.None);
        }
        private static string Limited(string value, int max) => value == null || value.Length <= max ? value : value.Substring(0, max);
    }
    public sealed class XrefListInput
    {
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public int MaxXrefs { get; private set; }
        public int MaxEntities { get; private set; }
        public static bool TryParse(JToken parameters, out XrefListInput input, out string error)
        {
            input = null; error = null;
            var obj = parameters == null || parameters.Type == JTokenType.Null ? new JObject() : parameters as JObject;
            if (obj == null) { error = "params must be an object"; return false; }
            if (!XrefContract.TryIdentity(obj, false, out var document, out var fingerprint, out error) ||
                !XrefContract.Integer(obj, "max_xrefs", 128, 1, XrefContract.MaxXrefs, out var maxXrefs, out error) ||
                !XrefContract.Integer(obj, "max_entities", 10000, 0, XrefContract.MaxEntities, out var maxEntities, out error)) return false;
            input = new XrefListInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                MaxXrefs = maxXrefs, MaxEntities = maxEntities };
            return true;
        }
    }

    public sealed class XrefManageInput
    {
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public string Action { get; private set; }
        public JObject ExpectedXref { get; private set; }
        public string Handle => ExpectedXref.Value<string>("handle");
        public bool DryRun { get; private set; }

        public static bool TryParse(JToken parameters, out XrefManageInput input, out string error)
        {
            input = null; error = null;
            if (!(parameters is JObject obj)) { error = "params must be an object"; return false; }
            if (!XrefContract.TryIdentity(obj, true, out var document, out var fingerprint, out error)) return false;
            var action = obj["action"]?.Type == JTokenType.String ? obj["action"].Value<string>() : null;
            if (action != "bind" && action != "detach") { error = "action must be bind or detach"; return false; }
            if (!(obj["expected_xref"] is JObject expected) || !ValidateExpectation(expected, out error))
            { error = error ?? "expected_xref must be the complete expected_xref object returned by dwg_list_xrefs"; return false; }
            bool dryRun = true;
            if (XrefContract.Present(obj["dry_run"]))
            {
                if (obj["dry_run"].Type != JTokenType.Boolean) { error = "dry_run must be a boolean"; return false; }
                dryRun = obj["dry_run"].Value<bool>();
            }
            input = new XrefManageInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                Action = action, ExpectedXref = (JObject)expected.DeepClone(), DryRun = dryRun };
            return true;
        }

        public static bool Matches(JObject expected, JObject actual) => expected != null && actual != null && JToken.DeepEquals(expected, actual);

        private static bool ValidateExpectation(JObject expected, out string error)
        {
            error = null;
            var fields = new[] { "handle", "name", "path", "status", "is_overlay", "is_nested", "graph_fingerprint",
                "references_fingerprint", "document_session", "document_revision", "guard_complete" };
            if (expected.Properties().Count() != fields.Length || expected.Properties().Any(p => !fields.Contains(p.Name)))
            { error = "expected_xref must contain the exact guard fields from dwg_list_xrefs"; return false; }
            if (!XrefContract.Text(expected["handle"], false, out var handle) || !XrefContract.TryHandle(handle, out var handleValue) ||
                handle != handleValue.ToString("X", CultureInfo.InvariantCulture))
            { error = "expected_xref.handle must be the canonical positive hexadecimal handle from the reader"; return false; }
            if (!XrefContract.Text(expected["name"], false, out _) || !XrefContract.Text(expected["path"], true, out _) ||
                !XrefContract.Text(expected["status"], false, out _))
            { error = "expected_xref requires exact bounded name/path/status strings"; return false; }
            if (expected["is_overlay"]?.Type != JTokenType.Boolean || expected["is_nested"]?.Type != JTokenType.Boolean ||
                expected.Value<bool>("is_nested") || expected["guard_complete"]?.Type != JTokenType.Boolean || !expected.Value<bool>("guard_complete"))
            { error = "only a complete guard for one non-nested root xref is accepted"; return false; }
            if (!XrefContract.Digest(expected["graph_fingerprint"], out _) || !XrefContract.Digest(expected["references_fingerprint"], out _) ||
                !XrefContract.Text(expected["document_session"], false, out var session) || !Guid.TryParseExact(session, "N", out var id) || id == Guid.Empty)
            { error = "expected_xref requires reader graph/reference SHA256 digests and document session"; return false; }
            try
            {
                if (expected["document_revision"]?.Type != JTokenType.Integer || expected.Value<long>("document_revision") < 0)
                { error = "expected_xref.document_revision must be a nonnegative integer"; return false; }
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException)
            { error = "expected_xref.document_revision is out of range"; return false; }
            return true;
        }
    }
}
