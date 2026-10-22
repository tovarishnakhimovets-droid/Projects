using System;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // These transport cases temporarily alter the same process-wide routing state
    // used by production. Do not overlap them with unrelated server-state tests.
    [CollectionDefinition("Document server routing", DisableParallelization = true)]
    public sealed class DocumentServerRoutingCollection { }

    [Collection("Document server routing")]
    public sealed class DocumentToolsTests
    {
        private const string DocumentA = "b606d2f2e2aa4141bbc128d87a3d648f";
        private const string DocumentB = "2fcde70d15eb427db633461962b3eb50";

        [Theory]
        [InlineData(DocumentA)]
        [InlineData("B606D2F2E2AA4141BBC128D87A3D648F")]
        [InlineData("b606d2f2-e2aa-4141-bbc1-28d87a3d648f")]
        public void Target_accepts_only_an_opaque_nonzero_GUID_and_canonicalizes_it(string documentId)
        {
            Assert.True(DocumentRequestTarget.TryCreate(documentId, out var target, out var error), error);
            Assert.Equal(DocumentA, target.DocumentId);
        }

        [Fact]
        public void Omission_means_follow_active_instead_of_a_pinned_document()
        {
            Assert.True(DocumentRequestTarget.TryCreate(null, out var target, out var error), error);
            Assert.Same(DocumentRequestTarget.FollowActive, target);
            Assert.Null(target.DocumentId);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("C:\\drawings\\plan.dwg")]
        [InlineData("plan.dwg")]
        [InlineData("{b606d2f2-e2aa-4141-bbc1-28d87a3d648f}")]
        [InlineData(" b606d2f2-e2aa-4141-bbc1-28d87a3d648f")]
        [InlineData("b606d2f2-e2aa-4141-bbc1-28d87a3d648f ")]
        [InlineData("00000000000000000000000000000000")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void Invalid_selector_does_not_resemble_active_fallback(string documentId)
        {
            Assert.False(DocumentRequestTarget.TryCreate(documentId, out var target, out var error));
            Assert.Null(target);
            Assert.Contains("document_id", error);
        }

        [Fact]
        public void SDK_advertises_readonly_session_selection_with_nullable_optional_ID()
        {
            var use = McpServerTool.Create(typeof(DocumentTools).GetMethod(nameof(DocumentTools.UseDocument)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_use_document", use.Name);
            Assert.True(use.Annotations.ReadOnlyHint);
            Assert.True(use.Annotations.IdempotentHint);
            Assert.False(use.InputSchema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
            var properties = use.InputSchema.GetProperty("properties");
            Assert.Equal(new[] { "document_id" }, properties.EnumerateObject().Select(value => value.Name).ToArray());
            var types = properties.GetProperty("document_id").GetProperty("type")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("string", types);
            Assert.Contains("null", types);

            var list = McpServerTool.Create(typeof(DocumentTools).GetMethod(nameof(DocumentTools.ListDocuments)),
                (object)null).ProtocolTool;
            Assert.Equal("dwg_list_documents", list.Name);
            Assert.True(list.Annotations.ReadOnlyHint);
            Assert.True(list.Annotations.IdempotentHint);
        }

        [Fact]
        public async Task Selection_is_published_only_after_native_confirmation()
        {
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            DocumentRequestTarget selected = DocumentRequestTarget.FollowActive;
            DocumentRequestTarget sent = null;
            var call = DocumentTools.UseDocumentCore(DocumentA, target =>
            {
                sent = target;
                return reply.Task;
            }, target => selected = target);

            Assert.Equal(DocumentA, sent.DocumentId);
            Assert.Null(selected.DocumentId);
            reply.SetResult(Confirmed(DocumentA));
            var result = await call;
            Assert.True(JObject.Parse(result).Value<bool>("ok"));
            Assert.Equal(DocumentA, selected.DocumentId);
        }

        [Fact]
        public async Task Native_refusal_preserves_the_previous_target_without_fallback()
        {
            DocumentRequestTarget.TryCreate(DocumentA, out var selected, out _);
            var previous = selected;
            var refused = JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = "document is closed" });
            var result = await DocumentTools.UseDocumentCore(DocumentB, _ => Task.FromResult(refused),
                target => selected = target);
            Assert.Equal(refused, result);
            Assert.Same(previous, selected);
        }

        [Fact]
        public async Task Invalid_selector_never_calls_the_plugin_or_changes_selection()
        {
            var called = false;
            var selected = false;
            var result = await DocumentTools.UseDocumentCore("plan.dwg", _ =>
            {
                called = true;
                return Task.FromResult(Confirmed(DocumentA));
            }, _ => selected = true);
            Assert.False(JObject.Parse(result).Value<bool>("ok"));
            Assert.False(called);
            Assert.False(selected);
        }

        [Theory]
        [InlineData("not JSON")]
        [InlineData("null")]
        [InlineData("{\"ok\":true,\"result\":null}")]
        [InlineData("{\"ok\":true,\"result\":{\"document_id\":\"2fcde70d15eb427db633461962b3eb50\"}}")]
        public async Task A_successful_envelope_without_matching_native_document_does_not_pin(string reply)
        {
            var selected = false;
            var result = await DocumentTools.UseDocumentCore(DocumentA, _ => Task.FromResult(reply),
                _ => selected = true);
            Assert.False(JObject.Parse(result).Value<bool>("ok"));
            Assert.False(selected);
        }

        [Fact]
        public async Task Follow_active_can_be_restored_when_no_drawings_are_open()
        {
            DocumentRequestTarget.TryCreate(DocumentA, out var selected, out _);
            var result = await DocumentTools.UseDocumentCore(null, target =>
            {
                Assert.Null(target.DocumentId);
                return Task.FromResult("{\"ok\":true,\"result\":null,\"error\":null}");
            }, target => selected = target);
            Assert.True(JObject.Parse(result).Value<bool>("ok"));
            Assert.Same(DocumentRequestTarget.FollowActive, selected);
        }

        [Fact]
        public async Task Transport_snapshots_the_session_target_before_discovery_runs()
        {
            var original = ServerState.DocumentTarget;
            DocumentRequestTarget.TryCreate(DocumentA, out var targetA, out _);
            DocumentRequestTarget.TryCreate(DocumentB, out var targetB, out _);
            JObject received = null;
            using var fake = new FakePluginServer(line =>
            {
                received = JObject.Parse(line);
                return JsonConvert.SerializeObject(new McpResponse { Id = received.Value<string>("id"), Ok = true });
            });
            try
            {
                ServerState.DocumentTarget = targetA;
                var client = new PluginClient(() =>
                {
                    ServerState.DocumentTarget = targetB;
                    return new DiscoveryInfo { Port = fake.Port, Token = "test" };
                });
                Assert.True((await client.SendAsync("query_entities", new { })).Ok);
                Assert.Equal(DocumentA, received.Value<string>("document_id"));
                Assert.Equal(DocumentB, ServerState.DocumentTarget.DocumentId);
            }
            finally { ServerState.DocumentTarget = original; }
        }

        [Fact]
        public async Task Explicit_follow_active_bypasses_a_stale_session_pin()
        {
            var original = ServerState.DocumentTarget;
            DocumentRequestTarget.TryCreate(DocumentA, out var pinned, out _);
            JObject received = null;
            using var fake = new FakePluginServer(line =>
            {
                received = JObject.Parse(line);
                return JsonConvert.SerializeObject(new McpResponse { Id = received.Value<string>("id"), Ok = true });
            });
            try
            {
                ServerState.DocumentTarget = pinned;
                var client = new PluginClient(() => new DiscoveryInfo { Port = fake.Port, Token = "test" });
                Assert.True((await client.SendAsync("list_documents", new { },
                    documentTarget: DocumentRequestTarget.FollowActive)).Ok);
                Assert.Null(received.Property("document_id"));
                Assert.Equal(DocumentA, ServerState.DocumentTarget.DocumentId);
            }
            finally { ServerState.DocumentTarget = original; }
        }

        private static string Confirmed(string documentId)
            => JsonConvert.SerializeObject(new McpResponse
            {
                Ok = true,
                Result = new { document_id = documentId, document_name = "plan.dwg", is_active = false }
            });
    }
}
