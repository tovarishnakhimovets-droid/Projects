using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Pure managed state. Status never waits for AutoCAD's busy main thread.
    internal sealed class LispJob
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal string Nonce = Guid.NewGuid().ToString("N");
        internal LispSourceSnapshot Source;
        internal string Command;
        internal string Expression;
        internal string ExpectedDocument;
        internal string ExpectedFingerprint;
        internal object Document;
        internal DateTime QueuedUtc;
        internal string State = "queued";
        internal bool Started;
        internal bool CallbackReceived;
        internal bool ReturnedSuccessfully;
        internal bool NativeEnded;
        internal int LispDepth;
        internal string Result;
        internal string Error;
    }

    internal sealed class LispJobStore
    {
        internal static readonly LispJobStore Session = new LispJobStore();
        private readonly object gate = new object();
        private readonly Dictionary<string, Tuple<string, string, string>> approvals =
            new Dictionary<string, Tuple<string, string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LispJob> jobs = new List<LispJob>();
        private readonly Func<DateTime> clock;

        internal LispJobStore(Func<DateTime> clock = null) { this.clock = clock ?? (() => DateTime.UtcNow); }
        private static string Key(string file, string command) => file + "\n" + command;
        private static bool Active(LispJob job) => job.State == "queued" || job.State == "running";

        internal void Approve(LispSourceSnapshot source, string command)
        {
            var error = LispExecutionPolicy.ValidateCommand(command);
            if (error != null) throw new ArgumentException(error);
            lock (gate)
            {
                Expire();
                if (jobs.Any(Active)) throw new InvalidOperationException("a LISP job is active; finish or cancel it first");
                if (approvals.Count >= 64 && !approvals.ContainsKey(Key(source.File, command)))
                    throw new InvalidOperationException("session approval limit reached; use MCPDISABLELISP to clear approvals");
                approvals[Key(source.File, command)] = Tuple.Create(source.File, source.Sha256, command);
            }
        }

        internal bool IsApproved(LispSourceSnapshot source, string command)
        {
            lock (gate)
                return approvals.TryGetValue(Key(source.File, command), out var value)
                    && string.Equals(value.Item2, source.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsBusy { get { lock (gate) { Expire(); return jobs.Any(Active); } } }

        internal LispJob Queue(LispSourceSnapshot source, string command, string argumentsJson,
            string expectedDocument, string expectedFingerprint, object document)
        {
            lock (gate)
            {
                Expire();
                if (jobs.Any(Active)) throw new InvalidOperationException("a LISP job is already queued/running; inspect its status, do not repeat execution");
                if (!IsApproved(source, command)) throw new InvalidOperationException(LispExecutionPolicy.Refusal);
                var job = new LispJob { Source = source, Command = command, QueuedUtc = clock(),
                    ExpectedDocument = expectedDocument, ExpectedFingerprint = expectedFingerprint, Document = document };
                job.Expression = LispExecutionPolicy.BuildExpression(source, command, argumentsJson,
                    "BIMWRIGHT_MCP_LISP_RESULT", job.Nonce);
                // The first form calls the native guard immediately before evaluating source.
                job.Expression = "(if (= 1 (BIMWRIGHT_MCP_LISP_BEGIN " + LispExecutionPolicy.Quote(job.Nonce)
                    + ")) " + job.Expression + ")\n";
                while (jobs.Count >= 16) jobs.RemoveAt(0);
                jobs.Add(job);
                return job;
            }
        }

        internal LispJob Pending(string nonce)
        {
            lock (gate) { Expire(); return jobs.FirstOrDefault(j => j.Nonce == nonce && j.State == "queued"); }
        }

        internal bool Begin(LispJob job)
        {
            lock (gate)
            {
                Expire();
                if (job.State != "queued" || !IsApproved(job.Source, job.Command)) return false;
                if (job.LispDepth <= 0)
                {
                    Fail(job, "native Lisp start was not observed; source was not evaluated");
                    return false;
                }
                job.State = "running"; job.Started = true;
                return true;
            }
        }

        internal void Record(string nonce, bool success, string result)
        {
            lock (gate)
            {
                var job = jobs.FirstOrDefault(j => j.Nonce == nonce && j.State == "running");
                if (job == null || job.CallbackReceived) return;
                job.CallbackReceived = true; job.ReturnedSuccessfully = success;
                result = result == null ? "" : result.Substring(0, Math.Min(result.Length, 8192));
                if (success) job.Result = result; else job.Error = result;
            }
        }

        internal LispJob Running { get { lock (gate) return jobs.FirstOrDefault(j => j.State == "running"); } }
        internal LispJob Current { get { lock (gate) { Expire(); return jobs.FirstOrDefault(Active); } } }

        internal void NativeStart(object document)
        {
            lock (gate)
            {
                var job = jobs.FirstOrDefault(j => Active(j) && ReferenceEquals(j.Document, document));
                if (job != null) job.LispDepth++;
            }
        }

        internal void NativeEnd(object document, bool cancelled)
        {
            lock (gate)
            {
                var job = jobs.FirstOrDefault(j => Active(j) && ReferenceEquals(j.Document, document));
                if (job == null || job.LispDepth <= 0) return;
                job.LispDepth--;
                if (job.State == "running" && job.LispDepth == 0)
                {
                    job.NativeEnded = true;
                    if (cancelled)
                    {
                        job.ReturnedSuccessfully = false;
                        job.Error = "AutoCAD cancelled LISP; drawing changes may remain";
                    }
                }
            }
        }

        internal void Finish(LispJob job)
        {
            lock (gate)
            {
                if (job.State != "running" || !job.NativeEnded || job.LispDepth != 0) return;
                job.State = job.CallbackReceived && job.ReturnedSuccessfully ? "completed" : "failed";
                if (!job.CallbackReceived) job.Error = "LISP ended without a result (cancel/error); drawing changes may remain. Do not rerun automatically.";
                ReleaseSource(job);
            }
        }

        internal void Fail(LispJob job, string error)
        {
            lock (gate)
            {
                if (!Active(job)) return;
                job.State = "failed"; job.Error = error;
                ReleaseSource(job);
            }
        }

        internal void Revoke()
        {
            lock (gate)
            {
                approvals.Clear();
                foreach (var job in jobs.Where(j => j.State == "queued"))
                    Fail(job, "LISP permission was revoked before execution; no source was evaluated");
                // Running trusted code cannot be forcibly rolled back or terminated here.
            }
        }

        private void Expire()
        {
            foreach (var job in jobs.Where(j => j.State == "queued" && clock() - j.QueuedUtc > TimeSpan.FromMinutes(1)))
                Fail(job, "queued LISP expired before execution; no source was evaluated");
        }

        private static void ReleaseSource(LispJob job)
        {
            job.Expression = null;
            // Keep only metadata in terminal history, not source or arguments.
            job.Source = new LispSourceSnapshot(job.Source.File, job.Source.Sha256, null);
        }

        internal object Snapshot(string jobId = null)
        {
            lock (gate)
            {
                Expire();
                if (jobId != null)
                {
                    var job = jobs.FirstOrDefault(j => j.Id == jobId);
                    if (job == null) throw new ArgumentException("unknown LISP job_id for this AutoCAD session");
                    return Status(job);
                }
                return new {
                    approvals = approvals.Values.Select(a => new { file = a.Item1, sha256 = a.Item2, command = a.Item3 }).ToArray(),
                    jobs = jobs.Select(Status).ToArray(),
                    busy = jobs.Any(Active)
                };
            }
        }

        private static object Status(LispJob job) => new {
            job_id = job.Id, status = job.State, execution_started = job.Started,
            completion_confirmed = job.NativeEnded && job.LispDepth == 0 && (job.State == "completed" || (job.State == "failed" && job.CallbackReceived)),
            may_have_partial_changes = job.State == "failed" && job.Started,
            file = job.Source.File, sha256 = job.Source.Sha256, command = job.Command,
            queued_utc = job.QueuedUtc, result = job.Result, error = job.Error
        };
    }
}
