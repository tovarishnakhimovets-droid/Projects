using System;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class DocumentRoutingPolicyTests
    {
        [Theory]
        [InlineData("BD2176EBF1FC47D38904E009CB5B604C")]
        [InlineData("BD2176EB-F1FC-47D3-8904-E009CB5B604C")]
        public void DocumentIdsNormalizeOnlyTheirWireRepresentation(string value)
        {
            Assert.True(DocumentRoutingPolicy.TryNormalizeDocumentId(value, out var result));
            Assert.Equal("bd2176ebf1fc47d38904e009cb5b604c", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("{BD2176EB-F1FC-47D3-8904-E009CB5B604C}")]
        [InlineData(" BD2176EBF1FC47D38904E009CB5B604C")]
        [InlineData("00000000000000000000000000000000")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("D:\\copy.dwg")]
        public void MalformedEmptyAndFilenameSelectorsNeverSelectADrawing(string value)
        {
            Assert.False(DocumentRoutingPolicy.TryNormalizeDocumentId(value, out var result));
            Assert.Null(result);
        }

        [Fact]
        public void OmittedOrNullSelectorMeansFollowActive()
        {
            Assert.True(DocumentRoutingPolicy.TryParseDocumentId(null, out var omitted, out var error), error);
            Assert.Null(omitted);
            Assert.True(DocumentRoutingPolicy.TryParseDocumentId(JValue.CreateNull(), out var explicitNull, out error), error);
            Assert.Null(explicitNull);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("\"\"")]
        public void SelectorDoesNotCoerceNonGuidValues(string json)
        {
            Assert.False(DocumentRoutingPolicy.TryParseDocumentId(JToken.Parse(json), out var id, out var error));
            Assert.Null(id);
            Assert.Contains("document_id", error);
        }

        [Theory]
        [InlineData("get_drawing_info")]
        [InlineData("get_entity_properties")]
        [InlineData("query_entities")]
        [InlineData("count_entities")]
        [InlineData("list_layers")]
        [InlineData("list_text_styles")]
        [InlineData("select_by_layer")]
        [InlineData("select_by_type")]
        public void OnlyAuditedDatabaseReadersAreAllowedInInactiveDrawings(string command)
        {
            Assert.True(DocumentRoutingPolicy.IsInactiveReadCommand(command));
            Assert.Null(DocumentRoutingPolicy.ValidateCommandTarget(command, isActive: false));
        }

        [Theory]
        [InlineData("get_selection")]
        [InlineData("get_selected_texts")]
        [InlineData("create_line")]
        [InlineData("update_texts")]
        [InlineData("send_code")]
        [InlineData("run_lisp")]
        [InlineData("zoom_window")]
        [InlineData("capture_view_image")]
        [InlineData("get_variables")]
        [InlineData("set_system_variable")]
        [InlineData("get_settings")]
        [InlineData("set_settings")]
        [InlineData("batch_execute")]
        [InlineData("unknown")]
        [InlineData("GET_DRAWING_INFO")]
        public void UnsupportedInactiveOperationsAreExplicitRefusals(string command)
        {
            Assert.False(DocumentRoutingPolicy.IsInactiveReadCommand(command));
            Assert.Contains("did not switch", DocumentRoutingPolicy.ValidateCommandTarget(command, isActive: false));
            Assert.Null(DocumentRoutingPolicy.ValidateCommandTarget(command, isActive: true));
        }

        [Fact]
        public void ChoosingAnInactiveTargetIsControlOnly()
        {
            Assert.Null(DocumentRoutingPolicy.ValidateCommandTarget("use_document", isActive: false));
            Assert.Equal(8, DocumentRoutingPolicy.InactiveReadCommands.Count);
        }

        [Fact]
        public void IdentityIsStablePerObjectAndUniqueEvenForEqualCopies()
        {
            var map = new OpenDocumentIdentityMap<EqualCopy>();
            var first = new EqualCopy();
            var second = new EqualCopy();
            var id = map.GetId(first);
            Assert.Equal(id, map.GetId(first));
            Assert.NotEqual(id, map.GetId(second));
            Assert.Same(first, map.Find(Guid.ParseExact(id, "N").ToString("D"), new[] { second, first }));
        }

        [Fact]
        public void ClosedAndReopenedDocumentsNeverFallbackToAnotherOpenDocument()
        {
            var map = new OpenDocumentIdentityMap<object>();
            var closed = new object();
            var other = new object();
            var reopened = new object();
            var closedId = map.GetId(closed);
            Assert.Null(map.Find(closedId, new[] { other, reopened }));
            Assert.NotEqual(closedId, map.GetId(reopened));
            Assert.Null(map.Find(Guid.NewGuid().ToString("N"), new[] { other }));
            Assert.Null(map.Find(closedId, Array.Empty<object>()));
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void GuardedWritersTakeReadLockUnlessExplicitBooleanFalse(string command)
        {
            Assert.True(DocumentRoutingPolicy.UsesReadLock(command));
            Assert.True(DocumentRoutingPolicy.UsesReadLock(command, new JObject()));
            foreach (var value in new JToken[] { JValue.CreateNull(), new JValue(true), new JValue("false"), new JValue(0), new JObject() })
                Assert.True(DocumentRoutingPolicy.UsesReadLock(command, new JObject { ["dry_run"] = value }));
            Assert.False(DocumentRoutingPolicy.UsesReadLock(command, new JObject { ["dry_run"] = false }));
            Assert.NotNull(DocumentRoutingPolicy.ValidateCommandTarget(command, false));
        }

        [Theory]
        [InlineData("create_line")]
        [InlineData("erase_entities")]
        [InlineData("set_system_variable")]
        public void DryRunFieldDoesNotChangeUnrelatedWriterLockPolicy(string command)
        {
            Assert.False(DocumentRoutingPolicy.UsesReadLock(command, new JObject { ["dry_run"] = true }));
        }
        private sealed class EqualCopy
        {
            public override bool Equals(object obj) => obj is EqualCopy;
            public override int GetHashCode() => 1;
        }
    }
}
