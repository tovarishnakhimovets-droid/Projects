using System;
using System.Runtime.ExceptionServices;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Plugin.Xrefs;
using Bimwright.Dwg.Plugin.Hatches;
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
            => InvokeApplication(() =>
            {
                var doc = DocumentRouting.Resolve(documentId);
                var isActive = ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc);
                var error = DocumentRoutingPolicy.ValidateCommandTarget(command, isActive);
                if (error != null) throw new InvalidOperationException(error);
                if (DocumentRoutingPolicy.UsesReadLock(command, parameters))
                {
                    // Protect the inspected database without taking a write lock
                    // or prompting the user to activate/finish another drawing.
                    using (doc.LockDocument(DocumentLockMode.Read, null, null, false)) return action(doc);
                }
                if (command == "clip_hatches")
                {
                    if (!HatchClipInput.TryParse(parameters, false, out var clip, out var clipError))
                        throw new InvalidOperationException(clipError);
                    // This command compares a complete source/mask geometry hash
                    // afresh under the write lock. Other database edits need not
                    // invalidate it, but a different open-document session must.
                    return InvokeGuardedWrite(doc, action, clip.Session, DrawingRevision.For(doc).Revision, true);
                }
                var expected = ReadWriteExpectation(command, parameters);
                if (expected.HasValue)
                    return InvokeGuardedWrite(doc, action, expected.Value.Session, expected.Value.Revision, command == "bind_current_xref");
                using (doc.LockDocument())
                {
                    return action(doc);
                }
            });

        private static (string Session, long Revision)? ReadWriteExpectation(string command, JToken parameters)
        {
            // Reuse the exact host-free command contracts before taking a write
            // lock; invalid or stale requests must not enter the native writer.
            if (command == "bind_current_xref")
            {
                if (!XrefBindCurrentInput.TryParse(parameters, out var current, out var error))
                    throw new InvalidOperationException(error);
                return (current.ManageInput.ExpectedXref.Value<string>("document_session"),
                    current.ManageInput.ExpectedXref.Value<long>("document_revision"));
            }
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

        private static T InvokeGuardedWrite<T>(Document doc, Func<Document, T> action, string expectedSession, long expectedRevision, bool refreshUnderWriteLock)
        {
            var state = DrawingRevision.For(doc);
            RequireRevision(state, expectedSession, expectedRevision, "before_write_lock");
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
                // Legacy operations retain their exact revision guard. The new
                // bind-current and hatch-clip contracts use a fresh complete
                // preflight under this lock. They do not claim
                // that lock-mode evaluation left the whole database unchanged.
                if (!refreshUnderWriteLock || state.SessionId != expectedSession || state.Revision < expectedRevision)
                    RequireRevision(state, expectedSession, expectedRevision, "after_write_lock");
                using (state.BeginPhase("write_command"))
                {
                    handlerInvoked = true;
                    result = action(doc);
                    handlerReturned = true;
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
                "source_revision", "preflight_revision", "preflight_refreshed_under_write_lock", "semantics",
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
