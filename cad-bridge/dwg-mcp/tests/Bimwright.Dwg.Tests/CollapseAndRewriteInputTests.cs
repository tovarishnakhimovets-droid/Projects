using System;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Rewriting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CollapseAndRewriteInputTests
    {
        private static JObject Valid(params JObject[] clusters) => new JObject
        {
            ["expected_document"] = @"D:\Drawings\network.dwg",
            ["expected_fingerprint"] = "7a9d5c0c-4e9a-4cc4-8fe0-0123456789ab",
            ["clusters"] = new JArray(clusters.Length == 0 ? new[] { Item("a") } : clusters)
        };
        private static JObject Item(string handle) => new JObject { ["anchor_handle"] = handle, ["new_text"] = "Translated\\Ptext" };
        private static CollapseAndRewriteInput Parse(JToken token)
        {
            Assert.True(CollapseAndRewriteInput.TryParse(token, out var input, out var error), error);
            Assert.Null(error);
            return input;
        }
        private static void Refuse(JToken token, string expected)
        {
            Assert.False(CollapseAndRewriteInput.TryParse(token, out var input, out var error));
            Assert.Null(input);
            Assert.Contains(expected, error);
        }

        [Fact]
        public void Defaults_preserve_anchor_dimensions_and_style_without_a_scale_policy()
        {
            var cluster = Assert.Single(Parse(Valid()).Clusters);
            Assert.Equal("A", cluster.AnchorHandle);
            Assert.Equal("Translated\\Ptext", cluster.NewText);
            Assert.Empty(cluster.DeleteHandles);
            Assert.False(cluster.ConvertToMText);
            Assert.False(cluster.CreatesMText);
            Assert.Null(cluster.TextHeight);
            Assert.Null(cluster.MtextWidth);
        }

        [Fact]
        public void Explicit_geometry_and_delete_handles_are_retained_in_owner_units()
        {
            var item = Item("a");
            item["delete_handles"] = new JArray("000b", "c");
            item["convert_to_mtext"] = true;
            item["text_height"] = 3.75;
            item["mtext_width"] = 0;
            var cluster = Assert.Single(Parse(Valid(item)).Clusters);
            Assert.True(cluster.CreatesMText);
            Assert.Equal(new[] { "B", "C" }, cluster.DeleteHandles);
            Assert.Equal(3.75, cluster.TextHeight);
            Assert.Equal(0, cluster.MtextWidth);
        }

        [Fact]
        public void Delete_handles_alone_request_conversion_and_empty_new_text_is_allowed()
        {
            var item = Item("1"); item["delete_handles"] = new JArray("2"); item["new_text"] = "";
            var cluster = Assert.Single(Parse(Valid(item)).Clusters);
            Assert.True(cluster.CreatesMText);
            Assert.Equal("", cluster.NewText);
        }

        [Theory]
        [InlineData("expected_document", "DWG path")]
        [InlineData("expected_fingerprint", "string")]
        public void Identity_is_required(string field, string error)
        {
            var input = Valid(); input.Remove(field);
            Refuse(input, field == "expected_document" ? "string" : error);
        }

        [Theory]
        [InlineData("drawing.dwg")]
        [InlineData("D:relative.dwg")]
        [InlineData(@"D:\Drawings\template.dwt")]
        [InlineData("https://example.com/drawing.dwg")]
        public void Identity_requires_a_full_saved_dwg_path(string path)
        { var input = Valid(); input["expected_document"] = path; Refuse(input, "fully qualified DWG path"); }

        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("not-a-guid")]
        public void Identity_requires_a_nonzero_guid(string fingerprint)
        { var input = Valid(); input["expected_fingerprint"] = fingerprint; Refuse(input, "nonempty drawing GUID"); }

        [Theory]
        [InlineData("apply_unicode_style")]
        [InlineData("final_scale")]
        [InlineData("active_document")]
        public void Top_level_legacy_and_unknown_properties_are_refused(string field)
        { var input = Valid(); input[field] = false; Refuse(input, "unsupported field: " + field); }

        [Theory]
        [InlineData("apply_unicode_style")]
        [InlineData("final_scale")]
        [InlineData("block_scale")]
        [InlineData("render_mode")]
        [InlineData("text_heigth")]
        public void Cluster_legacy_and_unknown_properties_are_refused(string field)
        { var item = Item("1"); item[field] = 1; Refuse(Valid(item), "unsupported field: " + field); }

        [Theory]
        [InlineData("0")]
        [InlineData("-A")]
        [InlineData("+A")]
        [InlineData("0xA")]
        [InlineData(" A")]
        [InlineData("G")]
        [InlineData("10000000000000000")]
        [InlineData("8000000000000000")]
        [InlineData("FFFFFFFFFFFFFFFF")]
        public void Malformed_handles_are_refused_before_native_lookup(string handle)
        { Refuse(Valid(Item(handle)), "handle"); }

        [Fact]
        public void All_handle_roles_share_one_numeric_identity_set_across_clusters()
        {
            var first = Item("1"); first["delete_handles"] = new JArray("000a");
            Refuse(Valid(first, Item("A")), "more than once");
            first["delete_handles"] = new JArray("01");
            Refuse(Valid(first), "more than once");
        }

        [Fact]
        public void Entire_batch_is_refused_if_a_later_cluster_is_malformed()
        {
            var later = Item("2"); later["new_text"] = 42;
            Refuse(Valid(Item("1"), later), "new_text must be a string");
        }

        [Theory]
        [InlineData("mtext_width", -1)]
        [InlineData("text_height", 0)]
        [InlineData("text_height", -1)]
        public void Dimensions_must_be_in_their_finite_range(string field, double value)
        { var item = Item("1"); item[field] = value; Refuse(Valid(item), "finite"); }

        [Theory]
        [InlineData("mtext_width")]
        [InlineData("text_height")]
        public void Dimensions_reject_nan_infinity_strings_and_null(string field)
        {
            foreach (var value in new JToken[] { double.NaN, double.PositiveInfinity, "3", JValue.CreateNull() })
            { var item = Item("1"); item[field] = value; Refuse(Valid(item), field); }
        }

        [Fact]
        public void Fields_nul_and_oversized_text_are_refused_but_exact_limit_is_accepted()
        {
            foreach (var value in new[] { "field %<code>%", "null\0value", new string('x', 16385) })
            { var item = Item("1"); item["new_text"] = value; Refuse(Valid(item), "new_text"); }
            var maximum = Item("1"); maximum["new_text"] = new string('x', 16384);
            Assert.Equal(16384, Parse(Valid(maximum)).Clusters[0].NewText.Length);
        }

        [Fact]
        public void Boolean_and_delete_array_are_strict_and_do_not_accept_null()
        {
            foreach (var value in new JToken[] { "true", 1, JValue.CreateNull() })
            { var item = Item("1"); item["convert_to_mtext"] = value; Refuse(Valid(item), "boolean"); }
            foreach (var value in new JToken[] { "A", 1, JValue.CreateNull() })
            { var item = Item("1"); item["delete_handles"] = value; Refuse(Valid(item), "array"); }
            var numeric = Item("1"); numeric["delete_handles"] = new JArray(2); Refuse(Valid(numeric), "strings");
        }

        [Fact]
        public void Cluster_count_has_closed_limits()
        {
            var input = Valid(); input["clusters"] = new JArray(); Refuse(input, "1-100");
            input["clusters"] = new JArray(Enumerable.Range(1, 100).Select(i => Item(i.ToString("X", CultureInfo.InvariantCulture))));
            Assert.Equal(100, Parse(input).Clusters.Length);
            ((JArray)input["clusters"]).Add(Item("FFFF")); Refuse(input, "1-100");
        }

        [Fact]
        public void Handle_count_has_one_batch_limit_including_anchors()
        {
            var item = Item("1");
            item["delete_handles"] = new JArray(Enumerable.Range(2, 999).Select(i => i.ToString("X", CultureInfo.InvariantCulture)));
            Assert.Equal(999, Parse(Valid(item)).Clusters[0].DeleteHandles.Count);
            ((JArray)item["delete_handles"]).Add("FFFF"); Refuse(Valid(item), "at most 1000");
        }

        [Fact]
        public void Structural_errors_are_refusals_instead_of_cast_exceptions()
        {
            Refuse(null, "object"); Refuse(new JArray(), "object");
            var input = Valid(); input["clusters"] = new JArray(7); Refuse(input, "every cluster");
            input["clusters"] = "oops"; Refuse(input, "1-100");
        }
    }
}
