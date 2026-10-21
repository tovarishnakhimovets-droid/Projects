using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    // Parse the entire batch before looking up a document or opening CAD objects.
    public sealed class CollapseAndRewriteInput
    {
        public const int MaxClusters = 100, MaxHandles = 1000, MaxTextLength = 16384;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public RewriteRequest[] Clusters { get; private set; }

        public static bool TryParse(JToken token, out CollapseAndRewriteInput input, out string error)
        {
            input = null; error = null;
            try
            {
                var root = token as JObject;
                Need(root != null, "params must be an object");
                Keys(root, "expected_document", "expected_fingerprint", "clusters");
                var document = Text(root, "expected_document", 32768);
                document = DrawingIdentityPolicy.NormalizeDwgPath(document);
                Need(document != null, "expected_document must be a fully qualified DWG path");
                var fingerprint = Text(root, "expected_fingerprint", 64);
                Need(Guid.TryParse(fingerprint, out var guid) && guid != Guid.Empty,
                    "expected_fingerprint must be a nonempty drawing GUID");
                var clusters = root["clusters"] as JArray;
                Need(clusters != null && clusters.Count >= 1 && clusters.Count <= MaxClusters,
                    "clusters must contain 1-100 objects");
                var handles = new HashSet<string>(StringComparer.Ordinal);
                var requests = new List<RewriteRequest>();
                foreach (var tokenItem in clusters)
                {
                    var item = tokenItem as JObject;
                    Need(item != null, "every cluster must be an object");
                    Keys(item, "anchor_handle", "new_text", "delete_handles", "convert_to_mtext", "mtext_width", "text_height");
                    var request = new RewriteRequest
                    {
                        AnchorHandle = Handle(item["anchor_handle"], handles),
                        NewText = Text(item, "new_text", MaxTextLength, allowEmpty: true),
                        ConvertToMText = Boolean(item, "convert_to_mtext"),
                        MtextWidth = Number(item, "mtext_width", allowZero: true),
                        TextHeight = Number(item, "text_height", allowZero: false)
                    };
                    Need(!request.NewText.Contains("%<") && request.NewText.IndexOf('\0') < 0,
                        "new_text must not contain fields or null characters");
                    if (item.Property("delete_handles") != null)
                    {
                        var deletes = item["delete_handles"] as JArray;
                        Need(deletes != null, "delete_handles must be an array");
                        foreach (var handle in deletes) request.DeleteHandles.Add(Handle(handle, handles));
                    }
                    requests.Add(request);
                }
                input = new CollapseAndRewriteInput
                {
                    ExpectedDocument = document, ExpectedFingerprint = guid.ToString("D"), Clusters = requests.ToArray()
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            { error = ex.Message; return false; }
        }

        private static string Handle(JToken token, HashSet<string> handles)
        {
            Need(token?.Type == JTokenType.String, "handles must be hexadecimal strings");
            var text = (string)token;
            Need(text.Length >= 1 && text.Length <= 16, "handles must contain 1-16 hexadecimal digits");
            Need(CadWire.TryParseHandleValue(text, out var number, out var error), error);
            Need(number != 0,
                "handles must be nonzero hexadecimal values");
            var canonical = number.ToString("X", CultureInfo.InvariantCulture);
            Need(handles.Add(canonical), "handle occurs more than once in the batch: " + canonical);
            Need(handles.Count <= MaxHandles, "batch must contain at most 1000 handles");
            return canonical;
        }

        private static string Text(JObject root, string name, int maximum, bool allowEmpty = false)
        {
            Need(root[name]?.Type == JTokenType.String, name + " must be a string");
            var value = (string)root[name];
            Need(value.Length <= maximum && (allowEmpty || !string.IsNullOrWhiteSpace(value)), name + " must be " + (allowEmpty ? "" : "nonempty and ") + "<= " + maximum + " characters");
            return value;
        }

        private static bool Boolean(JObject item, string name)
        {
            if (item.Property(name) == null) return false;
            Need(item[name].Type == JTokenType.Boolean, name + " must be boolean");
            return (bool)item[name];
        }

        private static double? Number(JObject item, string name, bool allowZero)
        {
            if (item.Property(name) == null) return null;
            var value = item[name];
            Need(value.Type == JTokenType.Integer || value.Type == JTokenType.Float, name + " must be numeric");
            var number = (double)value;
            Need(!double.IsNaN(number) && !double.IsInfinity(number) && (allowZero ? number >= 0 : number > 0),
                name + " must be finite and " + (allowZero ? "nonnegative" : "positive"));
            return number;
        }

        private static void Keys(JObject item, params string[] allowed)
        {
            foreach (var property in item.Properties())
                Need(allowed.Contains(property.Name, StringComparer.Ordinal), "unsupported field: " + property.Name);
        }
        private static void Need(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
