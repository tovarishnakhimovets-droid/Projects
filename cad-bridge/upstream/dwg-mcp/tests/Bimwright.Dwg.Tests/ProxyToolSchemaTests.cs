using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class ProxyToolSchemaTests
    {
        [Fact]
        public void Actual_SDK_inventory_schema_has_typed_budgets_and_read_only_metadata()
        {
            var tool = McpServerTool.Create(typeof(ProxyTools).GetMethod(nameof(ProxyTools.InventoryProxies)), (object)null).ProtocolTool;
            Assert.Equal("dwg_inventory_proxies", tool.Name);
            Assert.True(tool.Annotations.ReadOnlyHint);
            foreach (var field in new[] { "max_handles", "budget_ms", "max_results", "owner_depth" })
                AssertType(tool.InputSchema.GetProperty("properties").GetProperty(field), "integer");
            foreach (var field in new[] { "scope", "after_handle", "expected_document", "expected_fingerprint" })
                AssertType(tool.InputSchema.GetProperty("properties").GetProperty(field), "string");
            if (tool.InputSchema.TryGetProperty("required", out var required)) Assert.Empty(required.EnumerateArray());
        }

        [Fact]
        public void Actual_SDK_erase_schema_has_required_nested_scalar_expectations()
        {
            var tool = McpServerTool.Create(typeof(ProxyWriteTools).GetMethod(nameof(ProxyWriteTools.EraseProxyObjects)), (object)null).ProtocolTool;
            Assert.Equal("dwg_erase_proxy_objects", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "expected_document", "expected_fingerprint", "targets" },
                tool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x, StringComparer.Ordinal));
            var targets = tool.InputSchema.GetProperty("properties").GetProperty("targets");
            AssertType(targets, "array");
            var item = targets.GetProperty("items");
            AssertType(item, "object");
            Assert.False(item.TryGetProperty("$ref", out _));
            var expectedFields = new[] { "expected_application", "expected_original_class", "expected_original_dxf", "expected_owner_handle", "handle" };
            Assert.Equal(expectedFields, item.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x, StringComparer.Ordinal));
            foreach (var field in expectedFields) AssertType(item.GetProperty("properties").GetProperty(field), "string");
        }

        [Fact]
        public async Task Bad_tool_requests_fail_locally_before_contacting_CAD()
        {
            var inventory = JObject.Parse(await ProxyTools.InventoryProxies(after_handle: "A1"));
            Assert.False(inventory.Value<bool>("ok"));
            Assert.Contains("expected_document", inventory.Value<string>("error"));
            var erase = JObject.Parse(await ProxyWriteTools.EraseProxyObjects(@"D:\Drawings\network.dwg",
                "cf85e9e9-6928-4f15-bd8c-dc86c493b46b", new[] { new ProxyEraseTarget { handle = "1A" } }));
            Assert.False(erase.Value<bool>("ok"));
            Assert.Contains("expected_original_class", erase.Value<string>("error"));
        }

        private static void AssertType(JsonElement schema, string expected)
        {
            var type = schema.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String) Assert.Equal(expected, type.GetString());
            else Assert.Contains(type.EnumerateArray(), value => value.GetString() == expected);
        }
    }
}
