using System;
using System.Runtime.ExceptionServices;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Plugin.Xrefs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public static class DocumentInvoker
    {
        private static MainThreadExecutor mainThread;

        // Called by the add-in entry point on AutoCAD's main thread, before
        // accepting transport requests. Document locking alone is not UI dispatch.
        internal static void Initialize()
        {
            if (mainThread != null) return;
            mainThread = new MainThreadExecutor();
            Application.Idle += OnIdle;
        }

        internal static void Shutdown()
        {
            Application.Idle -= OnIdle;
            mainThread = null;
        }

        private static void OnIdle(object sender, EventArgs args) => mainThread?.RunPending();

        /// <summary>
        /// Serializes AutoCAD API access and runs the action on its main thread.
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action)
            => Invoke(null, null, action);

        internal static T InvokeApplication<T>(Func<T> action)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return DwgApiExecutor.Invoke(() => executor.Invoke(action));
        }

        internal static T Invoke<T>(string command, string documentId, Func<Document, T> action, JToken parameters = null)
            => InvokeWithParameters(command, documentId, (doc, _) => action(doc), parameters);

        internal static T InvokeWithParameters<T>(string command, string documentId,
            Func<Document, JToken, T> action, JToken parameters = null)
            => InvokeApplication(() =>
            {
                // A verified lock boundary may refresh only the private revision
                // expectation. Caller input and all object expectations stay intact.
                var effectiveParameters = parameters?.DeepClone();
                var doc = DocumentRouting.Resolve(documentId);
                var isActive = ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc);
                var error = DocumentRoutingPolicy.ValidateCommandTarget(command, isActive);
                if (error != null) throw new InvalidOperationException(error);
                if (DocumentRoutingPolicy.UsesReadLock(command, effectiveParameters))
                {
                    // Protect the inspected database without taking a write lock
                    // or prompting the user to activate/finish another drawing.
                    using (doc.LockDocument(DocumentLockMode.Read, null, null, false)) return action(doc, effectiveParameters);
                }
                var expected = ReadWriteExpectation(command, effectiveParameters);
                if (expected.HasValue)
                    return InvokeGuardedWrite(doc, action, command, effectiveParameters, expected.Value.Session, expected.Value.Revision);
                using (doc.LockDocument())
                {
                    return action(doc, effectiveParameters);
                }
            });

        private static (string Session, long Revision)? ReadWriteExpectation(string command, JToken parameters)
        {
            // Reuse the exact host-free command contracts before taking a write
            // lock; invalid or stale requests must not enter the native writer.
            if (command == "manage_xrefs")
            {
                if (!XrefManageInput.TryParse(parameters, out var input, out var error))
                    throw new InvalidOperationException(error);
                return (input.ExpectedXref.Value<string>("document_session"), input.ExpectedXref.Value<long>("document_revision"));
            }
            if (command == "explode_blocks")
            {
                if (!BlockExplodeInput.TryParse(parameters, false, out var input, out var error))
                    throw new InvalidOperationException(error);
                return (input.ExpectedSession, input.ExpectedRevision);
            }
            return null;
        }

        private static T InvokeGuardedWrite<T>(Document doc, Func<Document, JToken, T> action, string command,
            JToken effectiveParameters, string expectedSession, long expectedRevision)
        {
            var state = DrawingRevision.For(doc);
            RequireRevision(state, expectedSession, expectedRevision, "before_write_lock");
            AssocNetworkSnapshot beforeNetworks = null;
            string snapshotError = null;
            // Acquisition/release failures are not snapshot unavailability: they
            // must stop the request before attempting another native lock.
            using (doc.LockDocument(DocumentLockMode.Read, null, null, false))
            {
                try { beforeNetworks = AssocNetworkSnapshot.Capture(doc); }
                catch (Exception error) { snapshotError = Bounded(error.Message, 512); }
            }
            // Snapshot reads and read-lock release are not allowed to invalidate
            // the request either. Unknown capture affects only the exceptional path.
            RequireRevision(state, expectedSession, expectedRevision, "before_write_lock");
            JObject boundaryReceipt = null;
            IDisposable writeLock = null;
            Exception primaryFailure = null;
            Exception releaseFailure = null;
            T result = default(T);
            bool handlerInvoked = false;
            bool handlerReturned = false;
            try
            {
                try
                {
                    using (state.BeginPhase("write_lock_acquire"))
                        writeLock = doc.LockDocument();
                }
                catch (Exception error)
                {
                    RequireRevision(state, expectedSession, expectedRevision, "write_lock_acquire_failed", error);
                    throw;
                }
                long acquiredRevision = state.Revision;
                if (state.SessionId != expectedSession || acquiredRevision != expectedRevision)
                {
                    var diagnostic = state.DiagnosticsSince(expectedRevision);
                    string proofError = snapshotError;
                    bool verified = state.SessionId == expectedSession && beforeNetworks != null &&
                        LockBoundaryProof.TryValidate(diagnostic, expectedSession, expectedRevision, acquiredRevision,
                            out var affectedHandles, out proofError) &&
                        beforeNetworks.Matches(doc, affectedHandles, out proofError);
                    // Native filing is a read, but reactors and getter failures are
                    // still unknown. Check again immediately before refreshing input.
                    if (!verified || state.SessionId != expectedSession || state.Revision != acquiredRevision)
                        RequireRevision(state, expectedSession, expectedRevision, "after_write_lock",
                            proofError == null ? null : new InvalidOperationException(Bounded(proofError, 512)));
                    if (command == "manage_xrefs") effectiveParameters["expected_xref"]["document_revision"] = acquiredRevision;
                    else effectiveParameters["expected_revision"] = acquiredRevision;
                    boundaryReceipt = new JObject
                    {
                        ["source_revision"] = expectedRevision, ["acquired_revision"] = acquiredRevision,
                        ["notification_count"] = acquiredRevision - expectedRevision,
                        ["persisted_network_state_equal"] = true, ["network_count"] = beforeNetworks.NetworkCount,
                        ["snapshot_bytes"] = beforeNetworks.TotalBytes,
                        ["object_expectations_rechecked"] = true
                    };
                }
                using (state.BeginPhase("write_command"))
                {
                    handlerInvoked = true;
                    result = action(doc, effectiveParameters);
                    handlerReturned = true;
                    if (boundaryReceipt != null && result is CommandResult receipt && receipt.Result is JObject payload)
                        payload["lock_boundary_validation"] = boundaryReceipt.DeepClone();
                }
            }
            catch (Exception error) { primaryFailure = error; }
            try
            {
                using (state.BeginPhase("write_lock_dispose")) writeLock?.Dispose();
            }
            catch (Exception error) { releaseFailure = error; }

            // Releasing a native lock can itself fail. Never let that replace a
            // preflight refusal, nor hide a receipt for an already-run writer.
            if (releaseFailure != null)
            {
                var diagnostic = new JObject
                {
                    ["phase"] = "write_lock_dispose_failed", ["handler_invoked"] = handlerInvoked,
                    ["handler_returned"] = handlerReturned, ["execution_unknown"] = handlerInvoked,
                    ["retry_forbidden"] = true, ["primary_error"] = Bounded(primaryFailure?.Message, 1024),
                    ["release_error"] = Bounded(releaseFailure.Message, 512),
                    ["handler_receipt"] = handlerReturned ? Receipt(result) : null,
                    ["lock_boundary_validation"] = boundaryReceipt,
                    ["revision_diagnostics"] = ReleaseDiagnostics(state, expectedRevision),
                    ["next_step"] = "Inspect current drawing and the handler receipt; do not automatically repeat this mutation."
                };
                throw new InvalidOperationException("native write-lock release failed; preserve the prior outcome and inspect current state: " +
                    diagnostic.ToString(Formatting.None), primaryFailure ?? releaseFailure);
            }
            if (primaryFailure != null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            return result;
        }

        private static JObject Receipt(object value)
        {
            // These two native handlers return CommandResult/JObject. Do not use
            // reflection or serialize an arbitrary object/native document graph.
            var receipt = new JObject();
            if (value is CommandResult command)
            {
                receipt["ok"] = command.Ok;
                receipt["error"] = Bounded(command.Error, 1024);
                value = command.Result;
            }
            if (value is JObject result)
            {
                receipt["result"] = ReceiptFields(result);
                if (result["readback"] is JObject readback)
                {
                    var summary = ReceiptFields(readback);
                    if (readback["remaining_original_insert_handles"] is JArray remaining) summary["remaining_original_insert_count"] = remaining.Count;
                    if (readback["insert_handles"] is JArray inserts) summary["insert_count"] = inserts.Count;
                    receipt["readback"] = summary;
                }
                if (result["results"] is JArray results) receipt["returned_result_count"] = results.Count;
            }
            else receipt["result_type"] = Bounded(value?.GetType().FullName, 128);
            return receipt;
        }

        private static JObject ReceiptFields(JObject value)
        {
            var summary = new JObject();
            foreach (var key in new[] { "action", "target_handle", "xref_name", "dry_run", "mutation_attempted",
                "completed", "operation_status", "native_returned", "execution_unknown", "committed", "saved",
                "atomic", "affected_insert_count", "reference_count", "created_count", "rollback_verified",
                "postcondition_verified", "definition_removed", "definition_handle", "definition_name", "is_local",
                "insert_metadata_complete", "insert_placement_and_clipping_unchanged" })
            {
                var token = value[key];
                if (!(token is JValue scalar)) continue;
                if (token.Type == JTokenType.String) summary[key] = Bounded(scalar.Value as string, 256);
                else if (token.Type == JTokenType.Boolean || token.Type == JTokenType.Null ||
                    scalar.Value is int || scalar.Value is long || scalar.Value is short || scalar.Value is byte)
                    summary[key] = token.DeepClone();
            }
            return summary;
        }

        private static JObject ReleaseDiagnostics(DrawingRevision state, long afterRevision)
        {
            var diagnostic = state.DiagnosticsSince(afterRevision);
            if (diagnostic["events"] is JArray events && events.Count > 8)
            {
                var recent = new JArray();
                for (int i = events.Count - 8; i < events.Count; i++) recent.Add(events[i].DeepClone());
                var omitted = events.Count - recent.Count;
                diagnostic["events_omitted_from_error"] = omitted;
                diagnostic["unrecorded_count"] = diagnostic.Value<long>("unrecorded_count") + omitted;
                diagnostic["eventbuffer_truncated"] = true;
                diagnostic["events"] = recent;
            }
            return diagnostic;
        }

        private static string Bounded(string value, int limit)
            => value == null || value.Length <= limit ? value : value.Substring(0, limit);

        private static void RequireRevision(DrawingRevision state, string expectedSession, long expectedRevision, string phase, Exception cause = null)
        {
            if (state.SessionId == expectedSession && state.Revision == expectedRevision) return;
            var diagnostic = new JObject
            {
                ["phase"] = phase, ["expected_session"] = expectedSession,
                ["expected_revision"] = expectedRevision, ["actual_session"] = state.SessionId,
                ["actual_revision"] = state.Revision, ["handler_invoked"] = false,
                ["events"] = state.DiagnosticsSince(expectedRevision), ["lock_error"] = cause == null ? null : cause.Message.Substring(0, Math.Min(cause.Message.Length, 512))
            };
            throw new InvalidOperationException("drawing session/revision changed before native writer; no handler was invoked: " + diagnostic.ToString(Formatting.None), cause);
        }
    }
}
