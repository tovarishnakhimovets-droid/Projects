using System;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Hatches;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HatchClipTests
    {
        [Fact]
        public void ScalarXDataKeepsExactOrderTypesValuesAndIndependentStorage()
        {
            var data = new JArray(new JArray(1001, "APP"), new JArray(1002, "{"),
                new JArray(1000, "Кровля"), new JArray(1000, ""), new JArray(1003, "0"),
                new JArray(1004, Convert.ToBase64String(new byte[] { 0, 127, 255 })),
                new JArray(1010, new JArray(1.25, -2.5, 0)), new JArray(1011, new JArray(2e6, -2e7, 0)),
                new JArray(1012, new JArray(-1, 2, 3)), new JArray(1013, new JArray(0, 0, 1)),
                new JArray(1040, 0.123456789012345), new JArray(1041, 5.0), new JArray(1042, 1.0),
                new JArray(1070, short.MinValue), new JArray(1071, int.MaxValue), new JArray(1002, "}"),
                new JArray(1001, "APP2"), new JArray(1000, "second group"));
            var snapshot = HatchClipInput.XDataSnapshot(data);
            Assert.True(JToken.DeepEquals(data, snapshot));
            string original = HatchClipInput.Hash(snapshot);
            data[10][1] = 0.2;
            Assert.Equal(original, HatchClipInput.Hash(snapshot));
            Assert.NotEqual(original, HatchClipInput.Hash(HatchClipInput.XDataSnapshot(data)));
            var swapped = (JArray)snapshot.DeepClone();
            var first = swapped[2].DeepClone(); swapped[2] = swapped[3].DeepClone(); swapped[3] = first;
            Assert.NotEqual(original, HatchClipInput.Hash(HatchClipInput.XDataSnapshot(swapped)));
            Assert.Empty(HatchClipInput.XDataSnapshot(new JArray()));
        }
        [Theory]
        [InlineData(1005, "\"AB12\"")]
        [InlineData(1006, "1")]
        [InlineData(1000, "1")]
        [InlineData(1002, "\"command\"")]
        [InlineData(1002, "\"}\"")]
        [InlineData(1004, "\"not base64!\"")]
        [InlineData(1010, "[1,2]")]
        [InlineData(1011, "[1,2,\"3\"]")]
        [InlineData(1040, "\"1.5\"")]
        [InlineData(1070, "32768")]
        [InlineData(1071, "2147483648")]
        [InlineData(1070, "1.5")]
        public void XDataLinksUnknownCodesAndWrongTypesRefuse(int code, string json)
        {
            var data = new JArray(new JArray(1001, "APP"), new JArray(code, JToken.Parse(json)));
            Assert.ThrowsAny<Exception>(() => HatchClipInput.XDataSnapshot(data));
        }
        [Fact]
        public void XDataGroupAndSizeBoundsRefuseWithoutDiscardingRecords()
        {
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1000, "orphan"))));
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1001, "APP"), new JArray(1002, "{"))));
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1001, "APP"), new JArray(1002, "{"), new JArray(1001, "APP2"))));
            var tooMany = new JArray(new JArray(1001, "APP"));
            for (int i = 0; i < 2048; i++) tooMany.Add(new JArray(1070, 0));
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(tooMany));
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1001, "APP"), new JArray(1000, new string('x', 65536)))));
            Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1001, "APP"), new JArray(1004, Convert.ToBase64String(new byte[128])))));
            foreach (double n in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentException>(() => HatchClipInput.XDataSnapshot(new JArray(new JArray(1001, "APP"), new JArray(1040, n))));
        }
        [Theory]
        [InlineData(true, false, false, false, "by_layer")]
        [InlineData(false, true, false, false, "by_block")]
        [InlineData(false, false, false, true, "invalid")]
        public void NonnumericTransparencyNeverInvokesNativeAlphaGetter(bool layer, bool block, bool alpha, bool invalid, string mode)
        {
            var snapshot = HatchClipInput.TransparencySnapshot(layer, block, alpha, invalid,
                () => throw new InvalidOperationException("eInvalidKey: Alpha is unavailable"));
            Assert.Equal(mode, snapshot.Value<string>("mode")); Assert.Null(snapshot["alpha"]);
        }
        [Theory]
        [InlineData((byte)0)] [InlineData((byte)123)] [InlineData((byte)255)]
        public void ExplicitAlphaIsReadOnceAndKeepsItsExactValue(byte value)
        {
            int reads = 0;
            var snapshot = HatchClipInput.TransparencySnapshot(false, false, true, false, () => { reads++; return value; });
            Assert.Equal(1, reads); Assert.Equal("alpha", snapshot.Value<string>("mode")); Assert.Equal(value, snapshot.Value<byte>("alpha"));
        }
        [Fact]
        public void InheritedAndOpaqueTransparencyRemainDifferentSnapshotGuards()
        {
            var inherited = HatchClipInput.TransparencySnapshot(true, false, false, false, null);
            var opaque = HatchClipInput.TransparencySnapshot(false, false, true, false, () => 255);
            Assert.NotEqual(HatchClipInput.Hash(inherited), HatchClipInput.Hash(opaque));
            Assert.Throws<ArgumentException>(() => HatchClipInput.TransparencySnapshot(true, true, false, false, null));
            Assert.Throws<ArgumentException>(() => HatchClipInput.TransparencySnapshot(false, false, false, false, null));
            Assert.Throws<ArgumentException>(() => HatchClipInput.TransparencySnapshot(false, false, true, false, null));
        }
        private static JObject Plan() => new JObject {
            ["expected_document"] = @"D:\Work\surface.dwg", ["expected_fingerprint"] = "739091b3-e588-a041-882e-fdfad3af3b45",
            ["hatch_handles"] = new JArray("a1", "00a2"), ["boundary_handles"] = new JArray("b1") };
        private static JObject Write()
        {
            var o = Plan(); o["expected_session"] = "a1111111111111111111111111111111"; o["expected_plan_hash"] = new string('a', 64); return o;
        }
        [Fact]
        public void HandlesNormalizeAndWriterDefaultsToDryRun()
        {
            Assert.True(HatchClipInput.TryParse(Write(), false, out var input, out var error), error);
            Assert.True(input.DryRun); Assert.Equal(new[] { "A1", "A2" }, input.Hatches);
            var o = Write(); o["dry_run"] = false;
            Assert.True(HatchClipInput.TryParse(o, false, out input, out error), error); Assert.False(input.DryRun);
        }
        [Theory]
        [InlineData("expected_document", "\"relative.dwg\"")]
        [InlineData("expected_fingerprint", "\"00000000-0000-0000-0000-000000000000\"")]
        [InlineData("expected_session", "\"invalid\"")]
        [InlineData("expected_session", "\"00000000-0000-0000-0000-000000000000\"")]
        [InlineData("expected_plan_hash", "\"abc\"")]
        [InlineData("dry_run", "\"false\"")]
        [InlineData("hatch_handles", "[]")]
        [InlineData("hatch_handles", "[\"A1\",\"00a1\"]")]
        [InlineData("hatch_handles", "[1]")]
        [InlineData("hatch_handles", "[\"0\"]")]
        [InlineData("hatch_handles", "[\"FFFFFFFFFFFFFFFF\"]")]
        [InlineData("boundary_handles", "[\"A1\"]")]
        [InlineData("boundary_handles", "null")]
        [InlineData("save", "true")]
        public void InvalidOrAmbiguousInputsRefuseBeforeGateway(string key, string json)
        {
            var o = Write(); o[key] = JToken.Parse(json);
            Assert.False(HatchClipInput.TryParse(o, false, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
        }
        [Fact]
        public void BatchesAreBoundedAndPlanRejectsWriterFields()
        {
            var o = Plan(); o["hatch_handles"] = new JArray(Enumerable.Range(1, 21).Select(i => i.ToString("X")));
            Assert.False(HatchClipInput.TryParse(o, true, out _, out _));
            o = Plan(); o["boundary_handles"] = new JArray(Enumerable.Range(0x100, 33).Select(i => i.ToString("X")));
            Assert.False(HatchClipInput.TryParse(o, true, out _, out _));
            Assert.False(HatchClipInput.TryParse(Write(), true, out _, out _));
        }
        [Fact]
        public void TinyFragmentsAreValidAndAreaIncreaseOrNonfiniteResultsRefuse()
        {
            Assert.Null(HatchClipInput.CheckArea(100, 1e-15)); Assert.Null(HatchClipInput.CheckArea(100, 0));
            Assert.NotNull(HatchClipInput.CheckArea(100, 101)); Assert.NotNull(HatchClipInput.CheckArea(double.NaN, 0));
            Assert.NotNull(HatchClipInput.CheckArea(100, double.PositiveInfinity)); Assert.NotNull(HatchClipInput.CheckArea(100, -1));
            var o = Plan(); var hash = HatchClipInput.Hash(o); o["boundary_handles"][0] = "B2";
            Assert.NotEqual(hash, HatchClipInput.Hash(o));
        }
        [Fact]
        public void ActualSdkSchemaCarriesExplicitGuardsAndSafeDefault()
        {
            var read = McpServerTool.Create(typeof(HatchClipQueryTools).GetMethod(nameof(HatchClipQueryTools.PlanHatchClip)), (object)null).ProtocolTool;
            var write = McpServerTool.Create(typeof(HatchClipWriteTools).GetMethod(nameof(HatchClipWriteTools.ClipHatches)), (object)null).ProtocolTool;
            Assert.True(read.Annotations.ReadOnlyHint); Assert.False(write.Annotations.ReadOnlyHint); Assert.False(write.Annotations.IdempotentHint);
            var schema = JObject.Parse(write.InputSchema.ToString());
            Assert.Equal("array", (string)schema["properties"]["hatch_handles"]["type"]);
            Assert.Equal("string", (string)schema["properties"]["hatch_handles"]["items"]["type"]);
            Assert.True((bool)schema["properties"]["dry_run"]["default"]);
            Assert.Contains("expected_session", schema["required"].Values<string>());
            Assert.Contains("expected_plan_hash", schema["required"].Values<string>());
            Assert.True(SchemaValidator.Validate("plan_hatch_clip", Plan(), CommandSchemas.PlanHatchClip).Ok);
            Assert.True(SchemaValidator.Validate("clip_hatches", Write(), CommandSchemas.ClipHatches).Ok);
        }
        [Fact]
        public void DryRunAndPlanAlwaysUseReadLockButRequireActiveTarget()
        {
            Assert.True(DocumentRoutingPolicy.UsesReadLock("plan_hatch_clip"));
            Assert.True(DocumentRoutingPolicy.UsesReadLock("clip_hatches", Write()));
            var o = Write(); o["dry_run"] = false; Assert.False(DocumentRoutingPolicy.UsesReadLock("clip_hatches", o));
            o["dry_run"] = "false"; Assert.True(DocumentRoutingPolicy.UsesReadLock("clip_hatches", o));
            Assert.NotNull(DocumentRoutingPolicy.ValidateCommandTarget("plan_hatch_clip", false));
            Assert.NotNull(DocumentRoutingPolicy.ValidateCommandTarget("clip_hatches", false));
        }
        [Fact]
        public async Task InvalidRequestsDoNotConnectToAutoCad()
        {
            var result = JObject.Parse(await HatchClipQueryTools.PlanHatchClip("relative.dwg", Guid.NewGuid().ToString(), new[] { "A1" }, new[] { "B1" }));
            Assert.False(result.Value<bool>("ok"));
            result = JObject.Parse(await HatchClipWriteTools.ClipHatches(@"D:\Work\surface.dwg", Guid.NewGuid().ToString(), new[] { "A1" }, new[] { "B1" }, "bad", new string('a', 64), false));
            Assert.False(result.Value<bool>("ok"));
        }
    }
}
