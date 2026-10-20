using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    // Host-free contract used at both protocol boundaries. No CAD code is accepted.
    public sealed class BlockVariantInput
    {
        public string ExpectedDocument { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public string NewName { get; private set; }
        public bool DryRun { get; private set; }
        public double GeometryTolerance { get; private set; }
        public JObject InsertionPoint { get; private set; }
        public JObject ExpectedReference { get; private set; }
        public JArray PolylineEdits { get; private set; }
        public JArray AttributeEdits { get; private set; }
        public JArray MTextEdits { get; private set; }
        public JObject AttributeValues { get; private set; }
        public static bool TryParse(JToken token, out BlockVariantInput input, out string error)
        {
            input = null; error = null;
            try
            {
                var o = BlockEditValidation.Object(token);
                BlockEditValidation.Keys(o, "expected_document", "expected_fingerprint", "new_name", "dry_run", "geometry_tolerance", "insertion_point", "expected_reference", "polyline_edits", "attribute_edits", "mtext_edits", "attribute_values");
                BlockEditValidation.Identity(o, out var document, out var fingerprint);
                var name = BlockEditValidation.String(o, "new_name", 255, false);
                BlockEditValidation.Require(!name.StartsWith("*") && name.IndexOfAny(new[] {'<','>','/','\\','\"',':',';','?','*','|',',','='}) < 0 && !name.Any(char.IsControl), "new_name is not a valid independent block name");
                var point = BlockEditValidation.Point(o["insertion_point"]);
                var source = BlockEditValidation.Object(o["expected_reference"]);
                foreach (var key in new[] {"handle", "definition_handle", "owner_handle"}) BlockEditValidation.Handle(source[key]);
                BlockEditValidation.String(source, "effective_name", 255, false);
                BlockEditValidation.String(source, "layer", 255, false);
                foreach (var key in new[] {"position", "normal", "scale"}) BlockEditValidation.Point(source[key]);
                BlockEditValidation.Number(source["rotation_radians"]);
                BlockEditValidation.Angles(source["dynamic_values"], true);
                var attrs = BlockEditValidation.Array(source["attributes"], 0, 32);
                var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in attrs)
                {
                    var a = BlockEditValidation.Object(item);
                    BlockEditValidation.Require(handles.Add(BlockEditValidation.Handle(a["handle"])), "duplicate attribute handle");
                    BlockEditValidation.Require(tags.Add(BlockEditValidation.String(a, "tag", 255, false)), "duplicate attribute tag");
                    BlockEditValidation.String(a, "text", 8192, true);
                    BlockEditValidation.Point(a["position"]); BlockEditValidation.Point(a["alignment_point"]);
                    BlockEditValidation.Number(a["rotation_radians"]); BlockEditValidation.Number(a["height"]);
                    foreach (var key in new[] {"invisible", "constant", "preset", "verifiable", "lock_position_in_block", "is_mtext_attribute", "fields_present"}) BlockEditValidation.Boolean(a[key]);
                }
                var polys = BlockEditValidation.Array(o["polyline_edits"], 1, 32);
                var attrEdits = BlockEditValidation.Array(o["attribute_edits"], 0, 32);
                var texts = BlockEditValidation.Array(o["mtext_edits"], 0, 8);
                handles.Clear();
                foreach (var item in polys)
                {
                    var e = BlockEditValidation.Object(item);
                    BlockEditValidation.Keys(e, "handle", "expected_layer", "expected_vertices", "new_vertices");
                    BlockEditValidation.Require(handles.Add(BlockEditValidation.Handle(e["handle"])), "duplicate edit handle");
                    BlockEditValidation.String(e, "expected_layer", 255, false);
                    var before = BlockEditValidation.Array(e["expected_vertices"], 2, 2);
                    var after = BlockEditValidation.Array(e["new_vertices"], 2, 2);
                    foreach (var vertex in before)
                    {
                        var v = BlockEditValidation.Object(vertex);
                        BlockEditValidation.Keys(v, "point", "bulge", "start_width", "end_width");
                        BlockEditValidation.Point(v["point"]);
                        foreach (var k in new[] {"bulge", "start_width", "end_width"}) BlockEditValidation.Require(BlockEditValidation.Number(v[k]) == 0, "only straight zero-width polylines are supported");
                    }
                    foreach (var vertex in after) BlockEditValidation.Point(vertex);
                    BlockEditValidation.Require(!JToken.DeepEquals(after[0], after[1]), "new segment cannot have coincident ends");
                    BlockEditValidation.Require(BlockEditValidation.Number(after[0]["z"]) == BlockEditValidation.Number(after[1]["z"]), "new segment must be planar");
                }
                foreach (var item in attrEdits)
                {
                    var e = BlockEditValidation.Object(item);
                    BlockEditValidation.Keys(e, "handle", "tag", "expected_layer", "expected_default", "new_default");
                    BlockEditValidation.Require(handles.Add(BlockEditValidation.Handle(e["handle"])), "duplicate edit handle");
                    BlockEditValidation.String(e, "tag", 255, false); BlockEditValidation.String(e, "expected_layer", 255, false);
                    BlockEditValidation.String(e, "expected_default", 8192, true); BlockEditValidation.String(e, "new_default", 8192, true);
                }
                foreach (var item in texts)
                {
                    var e = BlockEditValidation.Object(item);
                    BlockEditValidation.Keys(e, "handle", "expected_layer", "expected_text", "new_text", "expected_location");
                    BlockEditValidation.Require(handles.Add(BlockEditValidation.Handle(e["handle"])), "duplicate edit handle");
                    BlockEditValidation.String(e, "expected_layer", 255, false);
                    BlockEditValidation.String(e, "expected_text", 8192, true); BlockEditValidation.String(e, "new_text", 8192, true);
                    BlockEditValidation.Point(e["expected_location"]);
                }
                var values = BlockEditValidation.Object(o["attribute_values"]);
                BlockEditValidation.Require(values.Count <= 32, "too many attribute values");
                foreach (var p in values.Properties())
                {
                    BlockEditValidation.Require(tags.Contains(p.Name), "attribute override tag does not exist in source: " + p.Name);
                    BlockEditValidation.String(values, p.Name, 8192, true);
                }
                double tolerance = o["geometry_tolerance"] == null ? 1e-8 : BlockEditValidation.Number(o["geometry_tolerance"]);
                BlockEditValidation.Require(tolerance > 0 && tolerance <= 1e-4, "geometry_tolerance must be >0 and <=0.0001");
                input = new BlockVariantInput { ExpectedDocument = document, ExpectedFingerprint = fingerprint, NewName = name, DryRun = o["dry_run"] == null ? false : BlockEditValidation.Boolean(o["dry_run"]), GeometryTolerance = tolerance, InsertionPoint = point, ExpectedReference = source, PolylineEdits = polys, AttributeEdits = attrEdits, MTextEdits = texts, AttributeValues = values };
                return true;
            }
            catch (ArgumentException ex) { error = ex.Message; return false; }
        }
    }
    internal static class BlockEditValidation
    {
        internal static void Require(bool ok, string error) { if (!ok) throw new ArgumentException(error); }
        internal static JObject Object(JToken value) { Require(value is JObject, "expected an object"); return (JObject)value; }
        internal static JArray Array(JToken value, int min, int max) { Require(value is JArray a && a.Count >= min && a.Count <= max, "array size must be " + min + ".." + max); return (JArray)value; }
        internal static void Keys(JObject obj, params string[] keys) { var names = new HashSet<string>(keys, StringComparer.Ordinal); foreach (var p in obj.Properties()) Require(names.Contains(p.Name), "unknown input field: " + p.Name); }
        internal static string String(JObject o, string key, int max, bool empty) { var t = o[key]; Require(t?.Type == JTokenType.String, key + " must be a string"); var s = t.Value<string>(); Require(s != null && s.Length <= max && (empty || !string.IsNullOrWhiteSpace(s)), key + " length is invalid"); return s; }
        internal static double Number(JToken t) { Require(t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float), "expected a numeric value"); var n = t.Value<double>(); Require(!double.IsNaN(n) && !double.IsInfinity(n) && Math.Abs(n) <= 1e12, "numeric value is non-finite or out of range"); return n; }
        internal static bool Boolean(JToken t) { Require(t?.Type == JTokenType.Boolean, "expected a boolean"); return t.Value<bool>(); }
        internal static JObject Point(JToken t) { var p = Object(t); Keys(p, "x", "y", "z"); foreach (var k in new[] {"x", "y", "z"}) Number(p[k]); return p; }
        internal static string Handle(JToken t) { Require(t?.Type == JTokenType.String, "handle must be a string"); var s = t.Value<string>(); Require(!string.IsNullOrEmpty(s) && s.Length <= 16 && s.All(Uri.IsHexDigit) && long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n > 0, "invalid positive hexadecimal handle"); return s.ToUpperInvariant(); }
        internal static void Identity(JObject o, out string path, out string fingerprint)
        {
            path = DrawingIdentityPolicy.NormalizeDwgPath(String(o, "expected_document", 4096, false)); Require(path != null, "expected_document must be a full DWG path");
            Require(Guid.TryParse(String(o, "expected_fingerprint", 64, false), out var guid) && guid != Guid.Empty, "expected_fingerprint must be a nonempty GUID"); fingerprint = guid.ToString("D");
        }
        internal static JObject Angles(JToken t, bool allowEmpty)
        {
            var o = Object(t); Require(o.Count <= 16 && (allowEmpty || o.Count > 0), "angles must contain 1..16 named properties");
            foreach (var p in o.Properties()) { Require(p.Name.Length > 0 && p.Name.Length <= 255, "invalid angle property name"); Require(Math.Abs(Number(p.Value)) <= Math.PI * 2000, "angular value out of range (raw radians)"); } return o;
        }
    }
}
