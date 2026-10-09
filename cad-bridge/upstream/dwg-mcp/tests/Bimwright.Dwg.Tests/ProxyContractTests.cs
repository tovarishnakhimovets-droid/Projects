using System;
using Bimwright.Dwg.Plugin.Proxy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class ProxyContractTests
    {
        private const string Fingerprint = "cf85e9e9-6928-4f15-bd8c-dc86c493b46b";

        [Fact]
        public void Inventory_defaults_are_bounded_and_only_full_database_is_supported()
        {
            Assert.True(ProxyInventoryInput.TryParse(null, out var input, out var error), error);
            Assert.Equal(500000, input.MaxHandles);
            Assert.Equal(5000, input.BudgetMilliseconds);
            Assert.Equal(1000, input.MaxResults);
            Assert.Equal(8, input.OwnerDepth);
            Assert.False(ProxyInventoryInput.TryParse(new JObject { ["scope"] = "model_space" }, out _, out _));
        }

        [Theory]
        [InlineData("max_handles", "0")]
        [InlineData("max_handles", "1000001")]
        [InlineData("max_handles", "1.5")]
        [InlineData("budget_ms", "0")]
        [InlineData("budget_ms", "20001")]
        [InlineData("budget_ms", "\"5000\"")]
        [InlineData("max_results", "0")]
        [InlineData("max_results", "5001")]
        [InlineData("owner_depth", "0")]
        [InlineData("owner_depth", "33")]
        public void Inventory_rejects_unbounded_or_wrongly_typed_budgets(string field, string json)
        {
            Assert.False(ProxyInventoryInput.TryParse(new JObject { [field] = JToken.Parse(json) }, out _, out var error));
            Assert.Contains(field, error);
        }

        [Fact]
        public void Continuation_requires_a_paired_document_guard()
        {
            var request = new JObject { ["after_handle"] = "A1" };
            Assert.False(ProxyInventoryInput.TryParse(request, out _, out var error));
            Assert.Contains("expected_document", error);
            request["expected_document"] = @"D:\Drawings\network.dwg";
            Assert.False(ProxyInventoryInput.TryParse(request, out _, out _));
            request["expected_fingerprint"] = Fingerprint;
            Assert.True(ProxyInventoryInput.TryParse(request, out var input, out error), error);
            Assert.Equal(161L, input.AfterHandle);
        }

        [Theory]
        [InlineData("0")]
        [InlineData(" A1")]
        [InlineData("-1")]
        [InlineData("8000000000000000")]
        [InlineData("0xA1")]
        public void Handles_reject_whitespace_zero_and_unsupported_values(string handle)
            => Assert.False(ProxyContract.TryHandle(handle, out _));

        [Fact]
        public void Erase_expectations_preserve_exact_metadata_and_normalize_handle_aliases()
        {
            var request = ValidErase();
            Assert.True(ProxyEraseInput.TryParse(request, out var input, out var error), error);
            Assert.True(input.Targets[0].Matches("AeccThing", "AECC_THING", "Civil 3D", 0x20));
            Assert.False(input.Targets[0].Matches("aeccthing", "AECC_THING", "Civil 3D", 0x20));
            Assert.False(input.Targets[0].Matches("AeccThing", "AECC_OTHER", "Civil 3D", 0x20));
            Assert.False(input.Targets[0].Matches("AeccThing", "AECC_THING", "Civil 3D ", 0x20));
            Assert.False(input.Targets[0].Matches("AeccThing", "AECC_THING", "Civil 3D", 0x21));
            var duplicate = (JObject)request["targets"][0].DeepClone();
            duplicate["handle"] = "01a";
            ((JArray)request["targets"]).Add(duplicate);
            Assert.False(ProxyEraseInput.TryParse(request, out _, out error));
            Assert.Contains("duplicate", error);
        }

        [Theory]
        [InlineData("expected_original_class")]
        [InlineData("expected_original_dxf")]
        [InlineData("expected_application")]
        [InlineData("expected_owner_handle")]
        public void Erase_never_accepts_a_handle_without_exact_expected_metadata(string field)
        {
            var request = ValidErase();
            ((JObject)request["targets"][0]).Remove(field);
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("network.dwg")]
        [InlineData("D:network.dwg")]
        [InlineData("D:\\Drawings\\template.dwt")]
        [InlineData("\\\\server\\network.dwg")]
        public void Erase_requires_an_absolute_DWG_path(string document)
        {
            var request = ValidErase();
            request["expected_document"] = document;
            Assert.False(ProxyEraseInput.TryParse(request, out _, out var error));
            Assert.Contains("absolute DWG", error);
        }

        [Fact]
        public void Erase_requires_nonempty_fingerprint_and_boolean_dry_run()
        {
            var request = ValidErase();
            request["expected_fingerprint"] = Guid.Empty.ToString();
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
            request["expected_fingerprint"] = Fingerprint;
            request["dry_run"] = "true";
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
            request["dry_run"] = true;
            Assert.True(ProxyEraseInput.TryParse(request, out var input, out _));
            Assert.True(input.DryRun);
        }

        [Fact]
        public void Metadata_expectations_allow_reported_empty_DXF_and_application_but_not_missing_or_oversized_data()
        {
            var request = ValidErase();
            var target = (JObject)request["targets"][0];
            target["expected_original_dxf"] = "";
            target["expected_application"] = "";
            Assert.True(ProxyEraseInput.TryParse(request, out _, out var error), error);
            target["expected_original_class"] = new string('X', ProxyContract.MaxMetadataCharacters + 1);
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
            target["expected_original_class"] = "AeccThing";
            target["expected_application"] = JValue.CreateNull();
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("expected_document", "null")]
        [InlineData("expected_fingerprint", "null")]
        [InlineData("targets", "[null]")]
        [InlineData("targets", "[\"1A\"]")]
        public void Erase_refuses_missing_identity_or_unclassified_target_shapes(string field, string value)
        {
            var request = ValidErase();
            request[field] = JToken.Parse(value);
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void Erase_refuses_empty_or_overlarge_target_sets()
        {
            var request = ValidErase();
            var target = request["targets"][0].DeepClone();
            request["targets"] = new JArray();
            Assert.False(ProxyEraseInput.TryParse(request, out _, out _));
            var array = (JArray)request["targets"];
            for (var i = 0; i <= ProxyContract.MaxTargets; i++) array.Add(target.DeepClone());
            Assert.False(ProxyEraseInput.TryParse(request, out _, out var error));
            Assert.Contains("1-1000", error);
        }

        [Theory]
        [InlineData("AeccThing", "", "", "civil")]
        [InlineData("AecThing", "", "", "aec")]
        [InlineData("AcDbMapThing", "", "", "map")]
        [InlineData("CustomThing", "", "Third party", "unknown")]
        public void Classification_is_only_a_metadata_hint(string className, string dxf, string app, string expected)
            => Assert.Equal(expected, ProxyContract.Classify(className, dxf, app));

        private static JObject ValidErase() => new JObject
        {
            ["expected_document"] = @"D:\Drawings\network.dwg",
            ["expected_fingerprint"] = Fingerprint,
            ["targets"] = new JArray(new JObject { ["handle"] = "1A", ["expected_original_class"] = "AeccThing",
                ["expected_original_dxf"] = "AECC_THING", ["expected_application"] = "Civil 3D", ["expected_owner_handle"] = "20" })
        };
    }
}
