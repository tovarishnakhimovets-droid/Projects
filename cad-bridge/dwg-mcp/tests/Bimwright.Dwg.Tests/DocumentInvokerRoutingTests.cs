using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.View;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;
using System.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Drawing revision")]
    public sealed class DocumentInvokerRoutingTests : IDisposable
    {
        private readonly Document active = new Document();
        private readonly Document inactive = new Document();

        public DocumentInvokerRoutingTests()
        {
            DrawingRevision.Shutdown();
            Application.DocumentManagerReadError = null;
            Application.DocumentManager.ResetDocuments();
            Application.DocumentManager.Create(active);
            Application.DocumentManager.Create(inactive);
            Application.DocumentManager.Activate(active);
            DocumentRouting.ResolveOverride = id => id == null ? active : inactive;
            DocumentInvoker.Initialize();
        }

        public void Dispose()
        {
            DocumentInvoker.Shutdown();
            DrawingRevision.Shutdown();
            DocumentRouting.ResolveOverride = null;
            Application.DocumentManager.ResetDocuments();
        }

        [Fact]
        public void InactiveReadUsesOnlyReadLockWithoutPromptOrTabChange()
        {
            var result = DocumentInvoker.Invoke("get_entity_properties", Guid.NewGuid().ToString("N"), doc =>
            {
                Assert.Same(inactive, doc);
                Assert.Equal(1, inactive.LockDepth);
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
            Assert.Equal(DocumentLockMode.Read, inactive.LastLockMode);
            Assert.False(inactive.LastLockPrompt);
            Assert.Equal(0, inactive.LockDepth);
            Assert.Equal(0, active.LockCount);
        }

        [Fact]
        public void InactiveWriteRefusesBeforeTakingLockOrCallingHandler()
        {
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("create_line", Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Contains("requires", error.Message);
            Assert.Equal(0, calls);
            Assert.Equal(0, inactive.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
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
        [InlineData("get_selection")]
        [InlineData("get_selected_texts")]
        public void ActiveReadUsesReadLockWithoutPrompt(string command)
        {
            var result = DocumentInvoker.Invoke(command, null, doc =>
            {
                Assert.Same(active, doc);
                Assert.Equal(1, active.LockDepth);
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Equal(DocumentLockMode.Read, active.LastLockMode);
            Assert.False(active.LastLockPrompt);
            Assert.Equal(0, active.LockDepth);
            Assert.Equal(0, inactive.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
        }

        [Theory]
        [InlineData("get_selection")]
        [InlineData("get_selected_texts")]
        public void InactiveSelectionRefusesDespiteUsingReadLockWhenActive(string command)
        {
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Equal(0, calls);
            Assert.Equal(0, inactive.LockCount);
            Assert.Equal(0, active.LockCount);
        }

        [Fact]
        public void FailedReadLockCannotActivateOrFallbackToCurrentDrawing()
        {
            inactive.LockError = new InvalidOperationException("target is locked");
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("query_entities", Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
        }

        [Theory]
        [InlineData("manage_xrefs", false)]
        [InlineData("manage_xrefs", true)]
        [InlineData("explode_blocks", false)]
        [InlineData("explode_blocks", true)]
        public void GuardedDryRunUsesReadLockWithoutNativeLockRevisionNoise(string command, bool omitDryRun)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state, dryRun: true);
            if (omitDryRun) parameters.Remove("dry_run");
            active.OnLockAcquired = () =>
            {
                if (active.LastLockMode != DocumentLockMode.Read)
                    for (int i = 0; i < 3; i++) active.Database.Raise("modified", new Entity());
            };
            var result = DocumentInvoker.Invoke(command, null, doc =>
            {
                Assert.Equal(DocumentLockMode.Read, doc.LastLockMode);
                Assert.Equal(1, doc.LockDepth);
                return 42;
            }, parameters);
            Assert.Equal(42, result);
            Assert.Equal(0, state.Revision);
            Assert.Equal(0, active.LockDepth);
            Assert.Empty((JArray)state.DiagnosticsSince(0)["events"]);
        }

        [Theory]
        [InlineData("manage_xrefs", false)]
        [InlineData("manage_xrefs", true)]
        [InlineData("explode_blocks", false)]
        [InlineData("explode_blocks", true)]
        public void StaleGuardRefusesBeforeWriteLockOrHandler(string command, bool staleSession)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            if (staleSession)
            {
                if (command == "manage_xrefs") parameters["expected_xref"]["document_session"] = Guid.NewGuid().ToString("N");
                else parameters["expected_session"] = Guid.NewGuid().ToString("N");
            }
            else active.Database.Raise("modified", new Entity());
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, null, doc => ++calls, parameters));
            Assert.Contains("before_write_lock", error.Message);
            Assert.Contains("\"handler_invoked\":false", error.Message);
            Assert.Equal(0, active.LockCount);
            Assert.Equal(0, calls);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void WriteLockRevisionEventsAreDiagnosedAndNeverCompensated(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            active.OnLockAcquired = () =>
            {
                for (int i = 0; i < 3; i++) active.Database.Raise("modified", new Entity());
            };
            active.OnLockReleased = () => active.Database.Raise("modified", new Entity());
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, null, doc => ++calls, parameters));
            Assert.Contains("after_write_lock", error.Message);
            Assert.Contains("write_lock_acquire", error.Message);
            Assert.Contains("\"expected_revision\":0", error.Message);
            Assert.Contains("\"actual_revision\":3", error.Message);
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockDepth);
            Assert.Equal(4, state.Revision);
            Assert.Contains(((JArray)state.DiagnosticsSince(0)["events"]).Values<JObject>(), e => e.Value<string>("phase") == "write_lock_dispose");
            Assert.True(error.Message.Length < 8192);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void UnchangedRealWriterRunsOnceUnderLockAndMayMakeItsOwnChanges(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            var calls = 0;
            Assert.Equal(42, DocumentInvoker.Invoke(command, null, doc =>
            {
                Assert.Equal(1, doc.LockDepth);
                calls++;
                doc.Database.Raise("modified", new Entity());
                return 42;
            }, parameters));
            Assert.Equal(1, calls);
            Assert.Equal(0, active.LockDepth);
            Assert.Equal(1, state.Revision);
            Assert.Equal("write_command", ((JArray)state.DiagnosticsSince(0)["events"])[0].Value<string>("phase"));
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void InvalidFullWriterContractRefusesBeforeTakingLock(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            parameters["expected_document"] = "relative.dwg";
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(command, null, doc => ++calls, parameters));
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockCount);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void GuardedHandlerFailureReleasesLockAndRestoresDiagnosticPhase(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            var error = Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke<int>(command, null,
                doc => throw new InvalidOperationException("handler failed"), parameters));
            Assert.Equal("handler failed", error.Message);
            Assert.Equal(0, active.LockDepth);
            active.Database.Raise("modified", new Entity());
            Assert.Equal(1, state.Revision);
            var diagnostic = state.DiagnosticsSince(0);
            Assert.Equal(JTokenType.Null, diagnostic["active_phase"].Type);
            Assert.Empty((JArray)diagnostic["events"]);
            Assert.Equal(1, diagnostic.Value<long>("unrecorded_count"));
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void FailedWriteLockWithRevisionEventsReportsThemAndNeverInvokesHandler(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            active.OnLockAcquired = () =>
            {
                active.Database.Raise("modified", new Entity());
                throw new InvalidOperationException("native lock failed");
            };
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, null, doc => ++calls, parameters));
            Assert.Contains("write_lock_acquire_failed", error.Message);
            Assert.Contains("native lock failed", error.Message);
            Assert.Equal("native lock failed", error.InnerException.Message);
            Assert.Equal(1, state.Revision);
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockDepth);
        }
        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void ReleaseFailurePreservesStaleAcquireRefusalAndHandlerWasNotInvoked(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            active.OnLockAcquired = () => active.Database.Raise("modified", new Entity());
            active.OnLockReleased = () => throw new InvalidOperationException("release failed");
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, null, doc => ++calls, parameters));
            var diagnostic = ReleaseDiagnostic(error);
            Assert.False(diagnostic.Value<bool>("handler_invoked"));
            Assert.False(diagnostic.Value<bool>("handler_returned"));
            Assert.False(diagnostic.Value<bool>("execution_unknown"));
            Assert.Contains("after_write_lock", diagnostic.Value<string>("primary_error"));
            Assert.Contains("after_write_lock", error.InnerException.Message);
            Assert.Equal("release failed", diagnostic.Value<string>("release_error"));
            Assert.Equal(0, calls); Assert.Equal(0, active.LockDepth);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void ReleaseFailureRetainsSuccessfulNativeReceiptWithoutLargePayload(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            active.OnLockReleased = () => throw new InvalidOperationException("release failed after commit");
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(command, null, doc =>
            {
                calls++;
                return CommandResult.Success(new JObject
                {
                    ["completed"] = true, ["operation_status"] = "completed", ["native_returned"] = true,
                    ["committed"] = true, ["created_count"] = 3, ["huge_payload"] = new string('x', 100000),
                    ["readback"] = new JObject { ["postcondition_verified"] = true,
                        ["insert_handles"] = new JArray(Enumerable.Repeat("A1", 10000)) }
                });
            }, parameters));
            var diagnostic = ReleaseDiagnostic(error);
            Assert.True(diagnostic.Value<bool>("handler_invoked")); Assert.True(diagnostic.Value<bool>("handler_returned"));
            Assert.True(diagnostic.Value<bool>("execution_unknown")); Assert.Equal(1, calls);
            Assert.True(diagnostic["handler_receipt"].Value<bool>("ok"));
            Assert.True(diagnostic["handler_receipt"]["result"].Value<bool>("completed"));
            Assert.True(diagnostic["handler_receipt"]["result"].Value<bool>("committed"));
            Assert.Equal(3, diagnostic["handler_receipt"]["result"].Value<int>("created_count"));
            Assert.True(diagnostic["handler_receipt"]["readback"].Value<bool>("postcondition_verified"));
            Assert.Equal(10000, diagnostic["handler_receipt"]["readback"].Value<int>("insert_count"));
            Assert.DoesNotContain("huge_payload", error.Message); Assert.DoesNotContain("insert_handles", error.Message);
            Assert.True(error.Message.Length < 8192); Assert.Equal(0, active.LockDepth);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void ReleaseFailurePreservesPrimaryHandlerExceptionAndUnknownExecution(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            var primary = new InvalidOperationException("native handler failed");
            active.OnLockReleased = () => throw new InvalidOperationException("release also failed");
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke<CommandResult>(command, null, doc => throw primary, parameters));
            var diagnostic = ReleaseDiagnostic(error);
            Assert.Same(primary, error.InnerException);
            Assert.Equal("native handler failed", diagnostic.Value<string>("primary_error"));
            Assert.Equal("release also failed", diagnostic.Value<string>("release_error"));
            Assert.True(diagnostic.Value<bool>("handler_invoked")); Assert.False(diagnostic.Value<bool>("handler_returned"));
            Assert.True(diagnostic.Value<bool>("execution_unknown")); Assert.Equal(0, active.LockDepth);
        }

        [Theory]
        [InlineData("manage_xrefs")]
        [InlineData("explode_blocks")]
        public void ReleaseFailureRetainsFailedCommandReceiptWithoutTraversingNativeResult(string command)
        {
            var state = DrawingRevision.For(active);
            var parameters = GuardedRequest(command, state);
            active.OnLockReleased = () => throw new InvalidOperationException("release failed");
            var error = Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(command, null, doc =>
                new CommandResult { Ok = false, Error = "preflight failed", Result = active }, parameters));
            var diagnostic = ReleaseDiagnostic(error);
            Assert.False(diagnostic["handler_receipt"].Value<bool>("ok"));
            Assert.Equal("preflight failed", diagnostic["handler_receipt"].Value<string>("error"));
            Assert.Equal(typeof(Document).FullName, diagnostic["handler_receipt"].Value<string>("result_type"));
            Assert.True(diagnostic.Value<bool>("handler_returned")); Assert.True(diagnostic.Value<bool>("execution_unknown"));
            Assert.Equal(0, active.NativeGetterReads); Assert.Equal(0, active.LockDepth);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CurrentRootDryRunUsesReadLockAndKeepsDefaultTrue(bool omitDryRun)
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state, true);
            if (omitDryRun) parameters.Remove("dry_run");
            int calls = 0;
            Assert.Equal(42, DocumentInvoker.Invoke("bind_current_xref", null, doc =>
            {
                calls++;
                Assert.Equal(DocumentLockMode.Read, doc.LastLockMode);
                return 42;
            }, parameters));
            Assert.Equal(1, calls);
            Assert.Equal(0, state.Revision);
            Assert.Equal(0, active.LockDepth);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CurrentRootStaleGuardFailsBeforeNativeLock(bool staleSession)
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state);
            if (staleSession) parameters["expected_xref"]["document_session"] = Guid.NewGuid().ToString("N");
            else active.Database.Raise("modified", new Entity());
            int calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("bind_current_xref", null, doc => ++calls, parameters));
            Assert.Contains("before_write_lock", error.Message);
            Assert.Equal(0, active.LockCount);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void CurrentRootBindRechecksCurrentMetadataWithoutRewritingSourceRevision()
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state);
            var original = parameters.DeepClone();
            active.OnLockAcquired = () =>
            {
                for (int i = 0; i < 3; i++) active.Database.Raise("modified", new Entity());
            };
            active.OnLockReleased = () => active.Database.Raise("modified", new Entity());
            int calls = 0;
            var result = DocumentInvoker.Invoke("bind_current_xref", null, doc =>
            {
                calls++;
                Assert.Equal(3, state.Revision);
                Assert.Equal(0, parameters["expected_xref"].Value<long>("document_revision"));
                Assert.Equal(1, doc.LockDepth);
                return CommandResult.Success(new JObject
                {
                    ["semantics"] = "current_root_definition", ["source_revision"] = 0,
                    ["preflight_revision"] = 3, ["preflight_refreshed_under_write_lock"] = true
                });
            }, parameters);
            Assert.True(result.Ok);
            Assert.Equal(1, calls);
            Assert.True(JToken.DeepEquals(original, parameters));
            Assert.Equal(4, state.Revision);
            Assert.Equal(0, active.LockDepth);
            Assert.Equal(4, ((JArray)state.DiagnosticsSince(0)["events"]).Count);
        }

        [Fact]
        public void CurrentRootHandlerRefusalDoesNotBecomeNativeSuccess()
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state);
            active.OnLockAcquired = () => active.Database.Raise("modified", new Entity());
            var result = DocumentInvoker.Invoke("bind_current_xref", null,
                doc => CommandResult.Fail("reference metadata changed"), parameters);
            Assert.False(result.Ok);
            Assert.Equal("reference metadata changed", result.Error);
            Assert.Equal(0, active.LockDepth);
        }

        [Fact]
        public void CurrentRootAcquireFailureDoesNotInvokeHandlerEvenAfterEvaluation()
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state);
            active.OnLockAcquired = () =>
            {
                active.Database.Raise("modified", new Entity());
                throw new InvalidOperationException("native lock failed");
            };
            int calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("bind_current_xref", null, doc => ++calls, parameters));
            Assert.Contains("write_lock_acquire_failed", error.Message);
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockDepth);
        }

        [Fact]
        public void CurrentRootReleaseFailureRetainsCurrentPreflightReceipt()
        {
            var state = DrawingRevision.For(active);
            var parameters = CurrentRootRequest(state);
            active.OnLockAcquired = () => active.Database.Raise("modified", new Entity());
            active.OnLockReleased = () => throw new InvalidOperationException("release failed");
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("bind_current_xref", null, doc => CommandResult.Success(new JObject
                {
                    ["completed"] = true, ["semantics"] = "current_root_definition",
                    ["source_revision"] = 0, ["preflight_revision"] = 1,
                    ["preflight_refreshed_under_write_lock"] = true
                }), parameters));
            var receipt = ReleaseDiagnostic(error)["handler_receipt"]["result"];
            Assert.True(receipt.Value<bool>("completed"));
            Assert.Equal(0, receipt.Value<long>("source_revision"));
            Assert.Equal(1, receipt.Value<long>("preflight_revision"));
            Assert.Equal("current_root_definition", receipt.Value<string>("semantics"));
            Assert.True(receipt.Value<bool>("preflight_refreshed_under_write_lock"));
        }

        private static JObject CurrentRootRequest(DrawingRevision state, bool dryRun = false)
        {
            var request = GuardedRequest("manage_xrefs", state, dryRun);
            request.Remove("action");
            return request;
        }

        private static JObject ReleaseDiagnostic(InvalidOperationException error)
        {
            var diagnostic = JObject.Parse(error.Message.Substring(error.Message.IndexOf('{')));
            Assert.Equal("write_lock_dispose_failed", diagnostic.Value<string>("phase"));
            Assert.True(diagnostic.Value<bool>("retry_forbidden"));
            Assert.Contains("do not automatically repeat", diagnostic.Value<string>("next_step"));
            return diagnostic;
        }
        private static JObject GuardedRequest(string command, DrawingRevision state, bool dryRun = false)
        {
            var request = new JObject
            {
                ["expected_document"] = @"D:\Plans\Surface.dwg",
                ["expected_fingerprint"] = "0c612ca4-e486-454d-9240-f35e934a461d", ["dry_run"] = dryRun
            };
            if (command == "manage_xrefs")
            {
                request["action"] = "bind";
                request["expected_xref"] = new JObject
                {
                    ["handle"] = "A1", ["name"] = "Surface", ["path"] = @".\Surface.dwg", ["status"] = "Resolved",
                    ["is_overlay"] = false, ["is_nested"] = false, ["graph_fingerprint"] = new string('a', 64),
                    ["references_fingerprint"] = new string('b', 64), ["document_session"] = state.SessionId,
                    ["document_revision"] = state.Revision, ["guard_complete"] = true
                };
            }
            else
            {
                request["expected_session"] = state.SessionId;
                request["expected_revision"] = state.Revision;
                request["expected_snapshots"] = new JArray(new JObject
                {
                    ["handle"] = "A1", ["owner_handle"] = "1F", ["definition_handle"] = "B2", ["definition_name"] = "Surface",
                    ["layer"] = "Surface", ["position"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
                    ["normal"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 1 },
                    ["scale"] = new JObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 },
                    ["rotation_radians"] = 0, ["attribute_count"] = 0, ["definition_entity_count"] = 3
                });
            }
            return request;
        }
        [Fact]
        public void ApplicationControlNeedsNoActiveDocumentOrWriteLock()
        {
            Application.DocumentManager.Activate(null);
            Assert.Equal(7, DocumentInvoker.InvokeApplication(() => 7));
            Assert.Equal(0, active.LockCount);
            Assert.Equal(0, inactive.LockCount);
        }
    }
}
