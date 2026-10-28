using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BlockExplodeTests
    {
        public static JObject Snapshot() => new JObject
        {
            ["handle"] = "A1", ["owner_handle"] = "1F", ["definition_handle"] = "B2",
            ["definition_name"] = "Surface", ["layer"] = "Покрытия",
            ["position"] = Point(100, 200, 0), ["normal"] = Point(0, 0, 1), ["scale"] = Point(1, 1, 1),
            ["rotation_radians"] = 0.0, ["attribute_count"] = 0, ["definition_entity_count"] = 3
        };
        private static JObject Point(double x, double y, double z) => new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
        private static JObject Identity() => new JObject { ["expected_document"] = @"D:\Plans\Surface.dwg", ["expected_fingerprint"] = "0c612ca4-e486-454d-9240-f35e934a461d" };
        private static JObject Plan()
        { var o = Identity(); o["handles"] = new JArray("a1"); return o; }
        private static JObject Write()
        {
            var o = Identity(); o["expected_session"] = "11111111111111111111111111111111"; o["expected_revision"] = 123L;
            o["expected_snapshots"] = new JArray(Snapshot()); return o;
        }

        [Fact]
        public void PlanCanonicalizesAndDefaultsToBoundedReadOnly()
        {
            var o = Plan(); o["handles"] = new JArray("00a1");
            Assert.True(BlockExplodeInput.TryParse(o, true, out var input, out var error), error);
            Assert.Equal(new[] { "A1" }, input.Handles); Assert.Equal(20000, input.MaxEntities); Assert.True(input.DryRun);
            Assert.Null(input.Snapshots);
        }
        [Fact]
        public void MutationDefaultsToDryRunAndCanonicalizesSnapshots()
        {
            var o = Write(); o["expected_snapshots"][0]["handle"] = "00a1";
            Assert.True(BlockExplodeInput.TryParse(o, false, out var input, out var error), error);
            Assert.True(input.DryRun); Assert.Equal(123, input.ExpectedRevision); Assert.Equal("A1", input.Handles[0]);
        }
        [Fact]
        public void ExplicitWriteRequiresBooleanFalse()
        {
            var o = Write(); o["dry_run"] = false;
            Assert.True(BlockExplodeInput.TryParse(o, false, out var input, out var error), error); Assert.False(input.DryRun);
            o["dry_run"] = "false"; Assert.False(BlockExplodeInput.TryParse(o, false, out _, out _));
        }
        [Theory]
        [InlineData("expected_document", "\"relative.dwg\"")]
        [InlineData("expected_fingerprint", "\"00000000-0000-0000-0000-000000000000\"")]
        [InlineData("expected_session", "\"not-a-session\"")]
        [InlineData("expected_revision", "-1")]
        [InlineData("expected_revision", "1.5")]
        [InlineData("expected_revision", "9223372036854775808")]
        [InlineData("max_entities", "20001")]
        [InlineData("max_entities", "0")]
        [InlineData("max_entities", "\"20000\"")]
        [InlineData("expected_snapshots", "[]")]
        [InlineData("expected_snapshots", "[null]")]
        [InlineData("expected_snapshots", "[1]")]
        public void RejectsMalformedGuardBeforeGateway(string field, string json)
        { var o = Write(); o[field] = JToken.Parse(json); Assert.False(BlockExplodeInput.TryParse(o, false, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error)); }
        [Fact]
        public void RejectsUnknownFieldsAndDuplicateHandleAliases()
        {
            var o = Plan(); o["recursive"] = true; Assert.False(BlockExplodeInput.TryParse(o, true, out _, out _));
            o = Plan(); o["handles"] = new JArray("A1", "00a1"); Assert.False(BlockExplodeInput.TryParse(o, true, out _, out _));
            o = Write(); var extra = Snapshot(); extra["handle"] = "00a1"; ((JArray)o["expected_snapshots"]).Add(extra);
            Assert.False(BlockExplodeInput.TryParse(o, false, out _, out _));
            o = Write(); o["expected_snapshots"][0]["allow_attributes"] = true; Assert.False(BlockExplodeInput.TryParse(o, false, out _, out _));
        }
        [Theory]
        [InlineData("attribute_count", "1")]
        [InlineData("definition_entity_count", "0")]
        [InlineData("definition_entity_count", "20001")]
        [InlineData("rotation_radians", "\"0\"")]
        [InlineData("scale", "{\"x\":1,\"y\":2,\"z\":1}")]
        [InlineData("scale", "{\"x\":-1,\"y\":-1,\"z\":-1}")]
        [InlineData("position", "{\"x\":1e100,\"y\":2,\"z\":0}")]
        public void RefusesUnsupportedSnapshot(string field, string json)
        { var o = Write(); o["expected_snapshots"][0][field] = JToken.Parse(json); Assert.False(BlockExplodeInput.TryParse(o, false, out _, out _)); }
        [Fact]
        public void AggregateLimitIsCheckedBeforeMaterialization()
        {
            var o = Write(); o["max_entities"] = 5; var s = Snapshot(); s["handle"] = "A2";
            ((JArray)o["expected_snapshots"]).Add(s);
            Assert.False(BlockExplodeInput.TryParse(o, false, out _, out var error)); Assert.Contains("count exceeds", error);
        }
        [Fact]
        public void BoundsReferenceCountForBothOperations()
        {
            var o = Plan(); o["handles"] = new JArray(Enumerable.Range(1, 101).Select(n => n.ToString("X")));
            Assert.False(BlockExplodeInput.TryParse(o, true, out _, out _));
            o = Write(); o["expected_snapshots"] = new JArray(Enumerable.Range(1, 101).Select(n => { var s = Snapshot(); s["handle"] = n.ToString("X"); return s; }));
            Assert.False(BlockExplodeInput.TryParse(o, false, out _, out _));
        }
        [Fact]
        public void SnapshotComparisonAcceptsIntegerWireNumbersButDetectsStaleness()
        {
            var a = Snapshot(); var b = (JObject)a.DeepClone();
            b["rotation_radians"] = 0; b["scale"]["x"] = 1;
            Assert.True(BlockExplodeInput.SnapshotMatches(a, b));
            foreach (var key in new[] { "handle", "owner_handle", "definition_handle", "definition_name", "layer" })
            { b = (JObject)a.DeepClone(); b[key] = "changed"; Assert.False(BlockExplodeInput.SnapshotMatches(a, b)); }
            b = (JObject)a.DeepClone(); b["position"]["x"] = 100.00000001; Assert.False(BlockExplodeInput.SnapshotMatches(a, b));
            b = (JObject)a.DeepClone(); b["definition_entity_count"] = 4; Assert.False(BlockExplodeInput.SnapshotMatches(a, b));
        }
        [Fact]
        public void ActualMcpSchemaIsTypedAndMarksReadVersusWrite()
        {
            var read = McpServerTool.Create(typeof(BlockExplodeQueryTools).GetMethod(nameof(BlockExplodeQueryTools.PlanBlockExplode)), (object)null).ProtocolTool;
            var write = McpServerTool.Create(typeof(BlockExplodeWriteTools).GetMethod(nameof(BlockExplodeWriteTools.ExplodeBlocks)), (object)null).ProtocolTool;
            Assert.True(read.Annotations.ReadOnlyHint); Assert.False(write.Annotations.ReadOnlyHint);
            var schema = JObject.Parse(write.InputSchema.ToString());
            Assert.Equal("array", (string)schema["properties"]["expected_snapshots"]["type"]);
            Assert.Equal("integer", (string)schema["properties"]["expected_revision"]["type"]);
            var required = (JArray)schema["required"];
            Assert.Contains("expected_session", required.Values<string>()); Assert.Contains("expected_snapshots", required.Values<string>());
        }
        [Fact]
        public async Task InvalidIdentityNeverReachesCadGateway()
        {
            var result = JObject.Parse(await BlockExplodeQueryTools.PlanBlockExplode("relative.dwg", Guid.NewGuid().ToString(), new[] { "A1" }));
            Assert.False(result.Value<bool>("ok")); Assert.Contains("full DWG", (string)result["error"]);
        }
        [Fact]
        public void NativeReadAndDryRunReturnBeforeExplodeOrWrite()
        {
            var source = Read("Blocks", "BlockExplodeService.cs");
            int noWrite = source.IndexOf("if (plan || input.DryRun)", StringComparison.Ordinal);
            int write = source.IndexOf("var handles = AppendExploded(", StringComparison.Ordinal);
            int erase = source.IndexOf("block.Erase();", StringComparison.Ordinal);
            Assert.True(noWrite >= 0 && write > noWrite && erase > write);
            Assert.Contains("CheckRevision(revision, input);", source);
            Assert.Contains("Require(SameCounts(histogram, outputTypes)", source);
            Assert.DoesNotContain("ExplodeToOwnerSpace", source);
            Assert.DoesNotContain("SaveAs(", source);
        }
        internal static string Read(params string[] parts)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "shared"));
            return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        }
    }
}
