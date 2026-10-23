using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class TableSdkTests
    {
        [Fact]
        public void Create_schema_exposes_typed_matrix_merges_and_required_identity()
        {
            var tool = McpServerTool.Create(typeof(AnnotationTools).GetMethod(nameof(AnnotationTools.CreateTable)), (object)null).ProtocolTool;
            var schema = tool.InputSchema;
            var required = schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
            foreach (var key in new[] { "expected_document", "expected_fingerprint", "insertion_point", "cells", "column_widths", "row_heights", "layer", "text_style" })
                Assert.Contains(key, required);
            var props = schema.GetProperty("properties");
            Assert.Equal("array", props.GetProperty("cells").GetProperty("type").GetString());
            Assert.Equal("array", props.GetProperty("cells").GetProperty("items").GetProperty("type").GetString());
            Assert.True(props.GetProperty("merges").GetProperty("items").TryGetProperty("properties", out _));
            Assert.True(props.GetProperty("cell_formats").GetProperty("items").TryGetProperty("properties", out _));
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
        }

        [Fact]
        public async Task Invalid_table_is_refused_before_transport()
        {
            var reply = JObject.Parse(await AnnotationTools.CreateTable("relative.dwg", Guid.NewGuid().ToString(),
                new TablePoint { x = 0, y = 0 }, new[] { new[] { "A" } }, new[] { 10d }, new[] { 6d }, "0", "Standard"));
            Assert.False((bool)reply["ok"]);
        }

        [Fact]
        public void Reader_is_read_only_but_requires_active_target_and_read_lock()
        {
            var tool = McpServerTool.Create(typeof(TableQueryTools).GetMethod(nameof(TableQueryTools.ReadTable)), (object)null).ProtocolTool;
            Assert.True(tool.Annotations.ReadOnlyHint);
            Assert.True(tool.Annotations.IdempotentHint);
            Assert.True(DocumentRoutingPolicy.UsesReadLock("read_table"));
            Assert.False(DocumentRoutingPolicy.IsInactiveReadCommand("read_table"));
            Assert.NotNull(DocumentRoutingPolicy.ValidateCommandTarget("read_table", false));
            Assert.Null(DocumentRoutingPolicy.ValidateCommandTarget("read_table", true));
        }
    }
}
