using Bimwright.Dwg.Plugin.Xrefs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class XrefContractTests
    {
        private static JObject Request()
            => new JObject
            {
                ["expected_document"] = @"D:\work\surfaces.dwg", ["expected_fingerprint"] = "3108b3be-79da-4435-a6da-d242ba9b2357", ["action"] = "bind",
                ["expected_xref"] = new JObject
                {
                    ["handle"] = "AB1", ["name"] = "paving", ["path"] = @"..\paving.dwg", ["status"] = "Resolved",
                    ["is_overlay"] = false, ["is_nested"] = false, ["graph_fingerprint"] = new string('a', 64),
                    ["references_fingerprint"] = new string('b', 64), ["document_session"] = "25c0d693262748a8984f651acbe91aa7",
                    ["document_revision"] = 7, ["guard_complete"] = true
                }
            };

        [Fact]
        public void InventoryCanSkipCountsButIdentityIsPaired()
        {
            Assert.True(XrefListInput.TryParse(new JObject { ["max_entities"] = 0 }, out var input, out _));
            Assert.Equal(0, input.MaxEntities);
            Assert.False(XrefListInput.TryParse(new JObject { ["expected_document"] = @"D:\work\a.dwg" }, out _, out _));
            Assert.False(XrefListInput.TryParse(new JObject { ["max_xrefs"] = 513 }, out _, out _));
            Assert.False(XrefListInput.TryParse(new JObject { ["max_entities"] = 0.5 }, out _, out _));
        }

        [Fact]
        public void BindRefusesUnresolvedOrUnloadedGraphNodesBeforeNativeCloneWork()
        {
            var graph = new JArray(new JObject { ["status"] = "Resolved", ["is_unloaded"] = false });
            Assert.Null(XrefContract.ValidateBindGraph(graph));
            graph.Add(new JObject { ["status"] = "Unresolved", ["is_unloaded"] = false });
            Assert.NotNull(XrefContract.ValidateBindGraph(graph));
            graph[1]["status"] = "Resolved"; graph[1]["is_unloaded"] = true;
            Assert.NotNull(XrefContract.ValidateBindGraph(graph));
            Assert.NotNull(XrefContract.ValidateBindGraph(new JArray(new JObject { ["status"] = "Resolved" })));
        }
        [Fact]
        public void MutationDefaultsToDryRunAndSupportsOnlyTwoActions()
        {
            Assert.True(XrefManageInput.TryParse(Request(), out var input, out _));
            Assert.True(input.DryRun);
            var request = Request(); request["action"] = "reload";
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
            request = Request(); request["dry_run"] = "false";
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("is_nested", true)]
        [InlineData("guard_complete", false)]
        public void PartialAndNestedGuardsCannotAuthorizeAMutation(string field, bool value)
        {
            var request = Request(); request["expected_xref"][field] = value;
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void GuardRejectsMissingFieldsMalformedHashesSessionsAndRevision()
        {
            var request = Request(); ((JObject)request["expected_xref"]).Remove("graph_fingerprint");
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
            request = Request(); request["expected_xref"]["references_fingerprint"] = new string('z', 64);
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
            request = Request(); request["expected_xref"]["document_session"] = "00000000000000000000000000000000";
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
            request = Request(); request["expected_xref"]["document_revision"] = -1;
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
            request = Request(); request["expected_xref"]["handle"] = "ab1";
            Assert.False(XrefManageInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void EverySnapshotFieldParticipatesInTheStaleGuard()
        {
            var original = (JObject)Request()["expected_xref"];
            var current = (JObject)original.DeepClone();
            Assert.True(XrefManageInput.Matches(original, current));
            current["document_revision"] = 8;
            Assert.False(XrefManageInput.Matches(original, current));
            current = (JObject)original.DeepClone(); current["references_fingerprint"] = new string('c', 64);
            Assert.False(XrefManageInput.Matches(original, current));
        }

        private static JObject BindCurrentRequest()
        {
            var request = Request();
            request.Remove("action");
            return request;
        }

        [Fact]
        public void CurrentBindUsesTheSameCompleteGuardAndDefaultsToReadOnlyPreflight()
        {
            var request = BindCurrentRequest();
            var original = request.DeepClone();
            Assert.True(XrefBindCurrentInput.TryParse(request, out var input, out var error), error);
            Assert.True(input.ManageInput.DryRun);
            Assert.Equal("bind", input.ManageInput.Action);
            Assert.True(JToken.DeepEquals(original, request));
            Assert.NotSame(request["expected_xref"], input.ManageInput.ExpectedXref);
            request["dry_run"] = false;
            Assert.True(XrefBindCurrentInput.TryParse(request, out input, out error), error);
            Assert.False(input.ManageInput.DryRun);
        }

        [Theory]
        [InlineData("action")]
        [InlineData("max_entities")]
        [InlineData("ignore_revision")]
        [InlineData("expected_revision")]
        [InlineData("bind_insert_style")]
        public void CurrentBindCannotBeExtendedToAnotherActionOrAWeakerGuard(string field)
        {
            var request = BindCurrentRequest();
            request[field] = field == "action" ? new JValue("detach") : new JValue(true);
            Assert.False(XrefBindCurrentInput.TryParse(request, out _, out _));
        }

        [Theory]
        [InlineData("expected_document")]
        [InlineData("expected_fingerprint")]
        [InlineData("expected_xref")]
        public void CurrentBindRequiresEveryIdentityAndSnapshotInput(string field)
        {
            var request = BindCurrentRequest();
            request.Remove(field);
            Assert.False(XrefBindCurrentInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void CurrentBindStillRejectsMalformedDryRunAndIncompleteGuards()
        {
            var request = BindCurrentRequest(); request["dry_run"] = "false";
            Assert.False(XrefBindCurrentInput.TryParse(request, out _, out _));
            request = BindCurrentRequest(); request["expected_xref"]["guard_complete"] = false;
            Assert.False(XrefBindCurrentInput.TryParse(request, out _, out _));
            request = BindCurrentRequest(); request["expected_xref"]["is_nested"] = true;
            Assert.False(XrefBindCurrentInput.TryParse(request, out _, out _));
        }

        [Fact]
        public void CurrentMetadataIgnoresOnlyTheLockBoundaryRevisionWithoutChangingEitherSnapshot()
        {
            var source = (JObject)Request()["expected_xref"];
            var current = (JObject)source.DeepClone();
            current["document_revision"] = 10;
            var originalSource = source.DeepClone();
            var originalCurrent = current.DeepClone();
            Assert.True(XrefBindCurrentInput.MatchesCurrentMetadata(source, current));
            Assert.False(XrefManageInput.Matches(source, current));
            Assert.True(JToken.DeepEquals(originalSource, source));
            Assert.True(JToken.DeepEquals(originalCurrent, current));
        }

        [Theory]
        [InlineData("handle", "AB2")]
        [InlineData("name", "another surface")]
        [InlineData("path", @"..\another.dwg")]
        [InlineData("status", "Unresolved")]
        [InlineData("document_session", "52796c39d024444f823c8e7e271963c8")]
        [InlineData("graph_fingerprint", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
        [InlineData("references_fingerprint", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
        public void CurrentMetadataStillChecksTargetGraphTransformsClippingAndSession(string field, string changed)
        {
            var source = (JObject)Request()["expected_xref"];
            var current = (JObject)source.DeepClone();
            current["document_revision"] = 10;
            current[field] = changed;
            Assert.False(XrefBindCurrentInput.MatchesCurrentMetadata(source, current));
        }

        [Theory]
        [InlineData("is_overlay", true)]
        [InlineData("is_nested", true)]
        [InlineData("guard_complete", false)]
        public void CurrentMetadataStillChecksRootFlagsAndCompleteness(string field, bool changed)
        {
            var source = (JObject)Request()["expected_xref"];
            var current = (JObject)source.DeepClone();
            current["document_revision"] = 10;
            current[field] = changed;
            Assert.False(XrefBindCurrentInput.MatchesCurrentMetadata(source, current));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("missing")]
        [InlineData("negative")]
        [InlineData("string")]
        public void CurrentMetadataDoesNotAcceptAMalformedFreshRevision(string failure)
        {
            var source = (JObject)Request()["expected_xref"];
            var current = (JObject)source.DeepClone();
            if (failure == null) current["document_revision"] = JValue.CreateNull();
            if (failure == "missing") current.Remove("document_revision");
            if (failure == "negative") current["document_revision"] = -1;
            if (failure == "string") current["document_revision"] = "10";
            Assert.False(XrefBindCurrentInput.MatchesCurrentMetadata(source, current));
            Assert.False(XrefBindCurrentInput.MatchesCurrentMetadata(null, source));
            Assert.False(XrefBindCurrentInput.MatchesCurrentMetadata(source, null));
        }

        [Fact]
        public void CurrentBindUncertainReceiptKeepsItsExplicitSemanticsAndBothRevisions()
        {
            var plan = new JObject
            {
                ["action"] = "bind", ["target_handle"] = "AB1", ["native_returned"] = true,
                ["semantics"] = "current_root_definition", ["source_revision"] = 7,
                ["preflight_revision"] = 10, ["preflight_refreshed_under_write_lock"] = true
            };
            var message = XrefFailureDetails.Format(plan);
            var details = JObject.Parse(message.Substring(message.IndexOf("details=", System.StringComparison.Ordinal) + 8));
            Assert.Equal("current_root_definition", details.Value<string>("semantics"));
            Assert.Equal(7, details.Value<long>("source_revision"));
            Assert.Equal(10, details.Value<long>("preflight_revision"));
            Assert.True(details.Value<bool>("preflight_refreshed_under_write_lock"));
            Assert.True(details.Value<bool>("execution_unknown"));
            Assert.DoesNotContain("persisted_network_state_equal", message);
        }

        [Fact]
        public void NativeMutationRequiresAQuiescentEditorBeforeItIsMarkedAttempted()
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "shared", "Xrefs", "XrefService.cs"));
            var source = System.IO.File.ReadAllText(path);
            var manage = source.IndexOf("internal static CommandResult Manage", System.StringComparison.Ordinal);
            var guard = source.IndexOf("if (!doc.Editor.IsQuiescent)", manage, System.StringComparison.Ordinal);
            var secondGuard = source.IndexOf("if (!doc.Editor.IsQuiescent)", guard + 1, System.StringComparison.Ordinal);
            var attempted = source.IndexOf("plan[\"mutation_attempted\"] = true;", manage, System.StringComparison.Ordinal);
            var bind = source.IndexOf("doc.Database.BindXrefs", manage, System.StringComparison.Ordinal);
            var detach = source.IndexOf("doc.Database.DetachXref", manage, System.StringComparison.Ordinal);
            Assert.True(manage >= 0 && guard > manage && secondGuard > guard && attempted > secondGuard);
            Assert.True(bind > attempted && detach > attempted);
            Assert.Contains("return CommandResult.Fail(XrefFailureDetails.Format(plan));", source);
        }
        [Fact]
        public void UncertainFailureDetailsSurviveTheActualErrorPrivacyAndWireEnvelope()
        {
            var remaining = new JArray();
            for (int i = 0; i < 10000; i++) remaining.Add(i.ToString("X"));
            var plan = new JObject { ["action"] = "detach", ["target_handle"] = "AB1", ["native_returned"] = true,
                ["error"] = new string('x', 20000), ["readback"] = new JObject {
                    ["definition_removed"] = false, ["postcondition_verified"] = false, ["remaining_original_insert_handles"] = remaining } };
            var result = Bimwright.Dwg.Plugin.CommandResult.Fail(XrefFailureDetails.Format(plan));
            // This is the dispatcher's failure branch: Result is discarded; only
            // the real error privacy filter and JSON Error field cross the wire.
            var wire = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(new {
                ok = result.Ok, result = (object)null, error = Bimwright.Dwg.Plugin.McpResponsePrivacy.SanitizeError(result.Error) }));
            Assert.False(wire.Value<bool>("ok"));
            var message = wire.Value<string>("error");
            Assert.True(message.Length < 8192);
            var details = JObject.Parse(message.Substring(message.IndexOf("details=", System.StringComparison.Ordinal) + 8));
            Assert.True(details.Value<bool>("execution_unknown"));
            Assert.True(details.Value<bool>("native_returned"));
            Assert.False(details.Value<bool>("completed"));
            Assert.Equal(10000, details["readback"].Value<int>("remaining_original_insert_count"));
            Assert.Equal(16, ((JArray)details["readback"]["remaining_original_insert_handles"]).Count);
            Assert.True(details["readback"].Value<bool>("remaining_handles_truncated"));
        }
        [Fact]
        public void HashCanonicalizesObjectKeyOrderButPreservesArrayOrderAndGeometry()
        {
            Assert.Equal(XrefContract.Hash(JObject.Parse("{\"x\":1,\"y\":2}")), XrefContract.Hash(JObject.Parse("{\"y\":2,\"x\":1}")));
            Assert.NotEqual(XrefContract.Hash(new JArray(1, 2)), XrefContract.Hash(new JArray(2, 1)));
            Assert.NotEqual(XrefContract.Hash(JObject.Parse("{\"matrix\":[1,0],\"clip\":false}")),
                XrefContract.Hash(JObject.Parse("{\"matrix\":[1,1],\"clip\":false}")));
        }
    }
}
