using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // Host-free checks run at both the MCP and AutoCAD boundaries.
    public sealed class BlockExplodeInput
    {
        public const int MaximumReferences = 100;
        public const int MaximumEntities = 20000;
        public const int BudgetMilliseconds = 15000;
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public string ExpectedSession { get; private set; }
        public long ExpectedRevision { get; private set; }
        public JArray Snapshots { get; private set; }
        public string[] Handles { get; private set; }
        public int MaxEntities { get; private set; }
        public bool DryRun { get; private set; }

        public static bool TryParse(JToken token, bool plan, out BlockExplodeInput input, out string error)
        {
            input = null; error = null;
            try
            {
                var o = BlockEditValidation.Object(token);
                if (plan) BlockEditValidation.Keys(o, "expected_document", "expected_fingerprint", "handles", "max_entities");
                else BlockEditValidation.Keys(o, "expected_document", "expected_fingerprint", "expected_session", "expected_revision", "expected_snapshots", "max_entities", "dry_run");
                BlockEditValidation.Identity(o, out var document, out var fingerprint);
                int limit = Integer(o["max_entities"], MaximumEntities, 1, MaximumEntities, "max_entities");
                var handles = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                JArray snapshots = null;
                string session = null;
                long revision = 0;
                if (plan)
                {
                    foreach (var h in BlockEditValidation.Array(o["handles"], 1, MaximumReferences))
                    {
                        var handle = CanonicalHandle(h);
                        BlockEditValidation.Require(seen.Add(handle), "duplicate block handle");
                        handles.Add(handle);
                    }
                }
                else
                {
                    session = BlockEditValidation.String(o, "expected_session", 32, false);
                    BlockEditValidation.Require(Guid.TryParseExact(session, "N", out var id) && id != Guid.Empty, "expected_session must be a nonempty N-format GUID from the plan");
                    session = id.ToString("N");
                    var r = o["expected_revision"];
                    BlockEditValidation.Require(r?.Type == JTokenType.Integer && long.TryParse(r.ToString(), out revision) && revision >= 0, "expected_revision must be a nonnegative integer from the plan");
                    snapshots = (JArray)BlockEditValidation.Array(o["expected_snapshots"], 1, MaximumReferences).DeepClone();
                    int count = 0;
                    foreach (var item in snapshots)
                    {
                        var s = BlockEditValidation.Object(item);
                        ValidateSnapshot(s);
                        var handle = CanonicalHandle(s["handle"]);
                        BlockEditValidation.Require(seen.Add(handle), "duplicate block snapshot handle");
                        s["handle"] = handle;
                        foreach (var key in new[] { "owner_handle", "definition_handle" }) s[key] = CanonicalHandle(s[key]);
                        handles.Add(handle);
                        count = checked(count + (int)s["definition_entity_count"]);
                    }
                    BlockEditValidation.Require(count <= limit, "planned direct entity count exceeds max_entities");
                }
                var dryRun = true;
                if (!plan && o["dry_run"] != null) dryRun = BlockEditValidation.Boolean(o["dry_run"]);
                input = new BlockExplodeInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint,
                    ExpectedSession = session, ExpectedRevision = revision, Snapshots = snapshots, Handles = handles.ToArray(),
                    MaxEntities = limit, DryRun = dryRun };
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidCastException || e is OverflowException)
            { error = e.Message; return false; }
        }

        public static bool SnapshotMatches(JObject actual, JObject expected)
        {
            if (actual == null || expected == null) return false;
            foreach (var key in new[] { "handle", "owner_handle", "definition_handle" })
                if (!string.Equals((string)actual[key], (string)expected[key], StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var key in new[] { "definition_name", "layer" })
                if (!string.Equals((string)actual[key], (string)expected[key], StringComparison.Ordinal)) return false;
            foreach (var key in new[] { "definition_entity_count", "attribute_count" })
                if ((int)actual[key] != (int)expected[key]) return false;
            if ((double)actual["rotation_radians"] != (double)expected["rotation_radians"]) return false;
            foreach (var key in new[] { "position", "normal", "scale" })
                foreach (var axis in new[] { "x", "y", "z" })
                    if ((double)actual[key][axis] != (double)expected[key][axis]) return false;
            return true;
        }

        private static void ValidateSnapshot(JObject s)
        {
            BlockEditValidation.Keys(s, "handle", "owner_handle", "definition_handle", "definition_name", "layer", "position", "normal", "scale", "rotation_radians", "attribute_count", "definition_entity_count");
            foreach (var k in new[] { "handle", "owner_handle", "definition_handle" }) CanonicalHandle(s[k]);
            foreach (var k in new[] { "definition_name", "layer" }) BlockEditValidation.String(s, k, 255, false);
            foreach (var k in new[] { "position", "normal", "scale" }) BlockEditValidation.Point(s[k]);
            BlockEditValidation.Number(s["rotation_radians"]);
            BlockEditValidation.Require(Integer(s["attribute_count"], -1, 0, 0, "attribute_count") == 0, "attributes are unsupported");
            Integer(s["definition_entity_count"], -1, 1, MaximumEntities, "definition_entity_count");
            var scale = s["scale"];
            double x = (double)scale["x"], y = (double)scale["y"], z = (double)scale["z"];
            BlockEditValidation.Require(x > 0 && x == y && y == z, "only positive uniform scale is supported");
        }
        private static int Integer(JToken token, int fallback, int min, int max, string name)
        {
            int n = fallback;
            if (token != null) BlockEditValidation.Require(token.Type == JTokenType.Integer && int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n), name + " must be an integer");
            BlockEditValidation.Require(n >= min && n <= max, name + " is outside " + min + ".." + max);
            return n;
        }
        private static string CanonicalHandle(JToken t)
        {
            var text = BlockEditValidation.Handle(t);
            return long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture);
        }
    }
}
