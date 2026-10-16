using System;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class LispJobStoreTests
    {
        private const string ScriptFile = @"C:\dwg-policy-tests\approved.lsp";
        private const string DrawingFile = @"C:\dwg-policy-tests\drawing.dwg";
        private const string Fingerprint = "12345678-1234-1234-1234-123456789abc";

        [Fact]
        public void Approval_matches_the_exact_file_hash_and_entry_with_windows_case_rules()
        {
            var store = new LispJobStore();
            var source = Source();
            store.Approve(source, "c:ENTRY");

            Assert.True(store.IsApproved(Source(ScriptFile.ToUpperInvariant(), source.Sha256.ToLowerInvariant()), "C:entry"));
            Assert.False(store.IsApproved(Source(@"C:\dwg-policy-tests\other.lsp"), "c:ENTRY"));
            Assert.False(store.IsApproved(Source(sha256: new string('B', 64)), "c:ENTRY"));
            Assert.False(store.IsApproved(source, "c:OTHER"));
            Assert.Throws<InvalidOperationException>(() => Queue(store, Source(sha256: new string('B', 64)), new object()));
            Assert.False(store.IsBusy);

            var replacement = Source(sha256: new string('B', 64));
            store.Approve(replacement, "c:ENTRY");
            Assert.False(store.IsApproved(source, "c:ENTRY"));
            Assert.True(store.IsApproved(replacement, "c:ENTRY"));
        }

        [Fact]
        public void Queue_requires_approval_and_does_not_create_a_job_on_failure()
        {
            var store = new LispJobStore();
            var error = Assert.Throws<InvalidOperationException>(() => Queue(store, Source(), new object()));
            Assert.Contains("LISP execution is blocked", error.Message);
            Assert.Empty((JArray)Snapshot(store)["jobs"]);
            Assert.False(store.IsBusy);
        }

        [Fact]
        public void Only_one_queued_or_running_job_is_allowed_and_trust_cannot_change_while_busy()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            Assert.True(store.IsBusy);
            Assert.Same(job, store.Current);
            Assert.Same(job, store.Pending(job.Nonce));
            Assert.Throws<InvalidOperationException>(() => Queue(store, source, document));
            Assert.Throws<InvalidOperationException>(() => store.Approve(source, "c:OTHER"));

            store.NativeStart(document);
            Assert.True(store.Begin(job));
            Assert.Same(job, store.Running);
            Assert.Null(store.Pending(job.Nonce));
            Assert.Throws<InvalidOperationException>(() => Queue(store, source, document));
        }

        [Fact]
        public void Wrong_nonce_queued_callbacks_and_duplicate_callbacks_are_ignored()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.Record(job.Nonce, true, "before-start");
            Assert.False(job.CallbackReceived);

            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.Record("unrelated-nonce", true, "wrong");
            Assert.False(job.CallbackReceived);
            store.Record(job.Nonce, true, "first");
            store.Record(job.Nonce, false, "late-error");
            Assert.True(job.CallbackReceived);
            Assert.True(job.ReturnedSuccessfully);
            Assert.Equal("first", job.Result);
            Assert.Null(job.Error);
        }

        [Fact]
        public void Callback_does_not_complete_or_release_the_job_until_the_native_end()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.Record(job.Nonce, true, "done");
            store.Finish(job);

            Assert.Equal("running", job.State);
            Assert.True(store.IsBusy);
            Assert.False((bool)Status(store, job)["completion_confirmed"]);
            Assert.NotNull(job.Expression);
            Assert.NotNull(job.Source.Source);

            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            Assert.Equal("completed", job.State);
            Assert.False(store.IsBusy);
            Assert.True((bool)Status(store, job)["completion_confirmed"]);
            Assert.False((bool)Status(store, job)["may_have_partial_changes"]);
            Assert.Null(job.Expression);
            Assert.Null(job.Source.Source);
        }

        [Fact]
        public void Other_documents_and_nested_lisp_ends_cannot_finalize_the_running_job()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.NativeStart(document);
            store.Record(job.Nonce, true, "done");

            store.NativeEnd(new object(), cancelled: false);
            Assert.Equal(2, job.LispDepth);
            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            Assert.Equal(1, job.LispDepth);
            Assert.False(job.NativeEnded);
            Assert.Equal("running", job.State);
            Assert.True(store.IsBusy);

            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            Assert.Equal("completed", job.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Missing_result_or_native_cancellation_reports_failure_with_possible_changes(bool recordSuccess)
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            if (recordSuccess) store.Record(job.Nonce, true, "early-result");
            store.NativeEnd(document, cancelled: recordSuccess);
            store.Finish(job);

            var status = Status(store, job);
            Assert.Equal("failed", (string)status["status"]);
            Assert.True((bool)status["execution_started"]);
            Assert.True((bool)status["may_have_partial_changes"]);
            Assert.False(store.IsBusy);
            Assert.Contains(recordSuccess ? "cancelled" : "without a result", (string)status["error"]);
            Assert.Equal(recordSuccess, (bool)status["completion_confirmed"]);
            Assert.Null(job.Expression);
            Assert.Null(job.Source.Source);
        }

        [Fact]
        public void A_trapped_script_error_is_confirmed_only_after_native_end_and_keeps_possible_changes()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.Record(job.Nonce, false, "script error after a mutation");
            store.Finish(job);
            Assert.True(store.IsBusy);
            Assert.False((bool)Status(store, job)["completion_confirmed"]);

            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            Assert.Equal("failed", job.State);
            Assert.True((bool)Status(store, job)["completion_confirmed"]);
            Assert.True((bool)Status(store, job)["may_have_partial_changes"]);
            Assert.Equal("script error after a mutation", job.Error);
        }

        [Fact]
        public void Revocation_cancels_a_queued_job_and_blocks_its_later_begin()
        {
            var store = new LispJobStore();
            var source = Source();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, new object());
            store.Revoke();

            Assert.False(store.IsApproved(source, "c:ENTRY"));
            Assert.False(store.Begin(job));
            Assert.Null(store.Pending(job.Nonce));
            Assert.Equal("failed", job.State);
            Assert.False(store.IsBusy);
            Assert.False((bool)Status(store, job)["execution_started"]);
            Assert.False((bool)Status(store, job)["may_have_partial_changes"]);
            Assert.Null(job.Expression);
            Assert.Null(job.Source.Source);
        }

        [Fact]
        public void Revocation_of_running_code_keeps_the_job_busy_until_actual_native_end()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.Revoke();

            Assert.False(store.IsApproved(source, "c:ENTRY"));
            Assert.Equal("running", job.State);
            Assert.True(store.IsBusy);
            Assert.Throws<InvalidOperationException>(() => Queue(store, source, document));
            store.Record(job.Nonce, true, "finished");
            store.Finish(job);
            Assert.True(store.IsBusy);
            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            Assert.Equal("completed", job.State);
            Assert.False(store.IsBusy);
        }

        [Fact]
        public void Queued_expiry_blocks_later_begin_and_never_expires_running_code()
        {
            var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            var store = new LispJobStore(() => now);
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var expired = Queue(store, source, document);
            now = now.AddMinutes(2);
            Assert.False(store.Begin(expired));
            Assert.Null(store.Pending(expired.Nonce));
            Assert.False(store.IsBusy);
            Assert.Equal("failed", expired.State);
            Assert.False(expired.Started);
            Assert.Contains("expired before execution", expired.Error);

            var running = Queue(store, source, document);
            store.NativeStart(document);
            Assert.True(store.Begin(running));
            now = now.AddHours(1);
            Assert.True(store.IsBusy);
            Assert.Equal("running", (string)Status(store, running)["status"]);
        }

        [Fact]
        public void Status_and_terminal_history_do_not_expose_source_expression_arguments_or_nonce()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            var job = Queue(store, source, document, "[\"argument-private-marker\"]");

            AssertRedacted(Snapshot(store), job);
            AssertRedacted(Status(store, job), job);
            store.NativeStart(document);
            Assert.True(store.Begin(job));
            store.Record(job.Nonce, true, new string('r', 9000));
            Assert.Equal(8192, ((string)Status(store, job)["result"]).Length);
            AssertRedacted(Status(store, job), job);
            store.NativeEnd(document, cancelled: false);
            store.Finish(job);
            AssertRedacted(Snapshot(store), job);
            Assert.Null(job.Expression);
            Assert.Null(job.Source.Source);
        }

        [Fact]
        public void History_keeps_only_the_last_sixteen_jobs_and_unknown_ids_fail()
        {
            var store = new LispJobStore();
            var source = Source();
            var document = new object();
            store.Approve(source, "c:ENTRY");
            string firstId = null;
            LispJob last = null;
            for (var i = 0; i < 20; i++)
            {
                last = Queue(store, source, document);
                firstId = firstId ?? last.Id;
                store.NativeStart(document);
                Assert.True(store.Begin(last));
                store.Record(last.Nonce, true, "ok");
                store.NativeEnd(document, cancelled: false);
                store.Finish(last);
            }

            var jobs = (JArray)Snapshot(store)["jobs"];
            Assert.Equal(16, jobs.Count);
            Assert.All(jobs, token => Assert.Equal("completed", (string)token["status"]));
            Assert.Equal(last.Id, (string)jobs.Last()["job_id"]);
            Assert.Throws<ArgumentException>(() => store.Snapshot(firstId));
            Assert.Throws<ArgumentException>(() => store.Snapshot("unrelated-session-job"));
        }

        private static LispSourceSnapshot Source(string file = ScriptFile, string sha256 = null)
            => new LispSourceSnapshot(file, sha256 ?? new string('A', 64),
                "; source-private-marker\n(defun c:ENTRY () nil)\n");

        private static LispJob Queue(LispJobStore store, LispSourceSnapshot source, object document, string arguments = "[]")
            => store.Queue(source, "c:ENTRY", arguments, DrawingFile, Fingerprint, document);

        private static JObject Snapshot(LispJobStore store) => JObject.FromObject(store.Snapshot());
        private static JObject Status(LispJobStore store, LispJob job) => JObject.FromObject(store.Snapshot(job.Id));

        private static void AssertRedacted(JObject status, LispJob job)
        {
            var text = status.ToString();
            Assert.DoesNotContain("source-private-marker", text);
            Assert.DoesNotContain("argument-private-marker", text);
            Assert.DoesNotContain(job.Nonce, text);
            Assert.DoesNotContain("BIMWRIGHT_MCP_LISP_BEGIN", text);
            var forbidden = new[] { "source", "expression", "document", "argumentsJson", "nonce" };
            Assert.DoesNotContain(status.Descendants().OfType<JProperty>(), property =>
                forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
        }
    }
}
