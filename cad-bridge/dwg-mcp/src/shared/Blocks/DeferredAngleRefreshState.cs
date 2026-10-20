using System;

namespace Bimwright.Dwg.Plugin.Blocks
{
    public enum DeferredAngleRefreshNotification
    {
        ExternalChange,
        DimensionChange,
        OwnAttributeChange
    }

    public enum DeferredAngleRefreshOutcome
    {
        Retry,
        Settled,
        Superseded,
        Exhausted,
        Failed
    }

    public readonly struct DeferredAngleRefreshPass
    {
        internal DeferredAngleRefreshPass(long token, long generation, long nativeRevision, int passNumber)
        { Token = token; Generation = generation; NativeRevision = nativeRevision; PassNumber = passNumber; }
        internal long Token { get; }
        public long Generation { get; }
        public long NativeRevision { get; }
        public int PassNumber { get; }
    }

    /// <summary>
    /// One document's bounded refresh episode. The caller executes one pass per Idle callback,
    /// releases the document lock before completing it, and supplies a monotonic clock.
    /// Native notifications only mark an episode dirty; actual external commands start a new generation.
    /// This policy has no CAD calls, timers or sleeps and is called on the host's single event thread.
    /// </summary>
    public sealed class DeferredAngleRefreshState
    {
        public const int MaximumPasses = 6;
        public const long MaximumActiveMilliseconds = 2000;
        private const int MaximumSnapshotCharacters = 8192;
        private enum Phase { None, Pending, Running, Settled, Exhausted, Failed }
        private Phase phase;
        private long nextToken, inFlightToken, inFlightGeneration, passStartedAt;
        private long lastClock = -1;
        private string lastReadOnlySnapshot;

        public long NativeRevision { get; private set; } = -1;
        public long Generation { get; private set; }
        public int PassCount { get; private set; }
        public long ActiveMilliseconds { get; private set; }
        public int StableObservations { get; private set; }
        public bool IsPending => phase == Phase.Pending;
        public bool IsRunning => phase == Phase.Running;
        public bool IsSettled => phase == Phase.Settled;
        public bool IsExhausted => phase == Phase.Exhausted;
        public string ExhaustionReason { get; private set; }
        public string LastReason { get; private set; }

        public void Notify(long nativeRevision,
            DeferredAngleRefreshNotification kind = DeferredAngleRefreshNotification.ExternalChange)
        {
            if (kind == DeferredAngleRefreshNotification.OwnAttributeChange) return;
            if (kind != DeferredAngleRefreshNotification.ExternalChange && kind != DeferredAngleRefreshNotification.DimensionChange)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (nativeRevision < 0) throw new ArgumentOutOfRangeException(nameof(nativeRevision));
            if (nativeRevision < NativeRevision) return; // A delayed older event must not supersede current work.

            if (kind == DeferredAngleRefreshNotification.ExternalChange)
            {
                NativeRevision = nativeRevision;
                StartGeneration();
                return;
            }
            if (nativeRevision == NativeRevision) return;
            NativeRevision = nativeRevision;
            // Regeneration must not renew an exhausted episode indefinitely. A subsequent external
            // command (including grip, Undo, Redo or cancel) can explicitly start another episode.
            if (phase == Phase.Exhausted || phase == Phase.Failed) return;
            if (phase == Phase.None || phase == Phase.Settled)
            {
                StartGeneration();
                return;
            }
            ResetStability();
            // Preserve Running and its lease: a native notification received during the lock
            // must be compared with the lease's revision after the lock is released.
        }

        public bool TryBeginPass(long monotonicMilliseconds, out DeferredAngleRefreshPass pass)
        {
            CheckClock(monotonicMilliseconds);
            pass = default;
            if (!IsPending) return false;
            if (BudgetReached()) { Exhaust(); return false; }
            phase = Phase.Running;
            passStartedAt = monotonicMilliseconds;
            inFlightGeneration = Generation;
            inFlightToken = ++nextToken;
            PassCount++;
            pass = new DeferredAngleRefreshPass(inFlightToken, Generation, NativeRevision, PassCount);
            return true;
        }

        public DeferredAngleRefreshOutcome CompletePass(DeferredAngleRefreshPass pass,
            string snapshot, bool wrote, long monotonicMilliseconds)
        {
            if (!FinishPass(pass, monotonicMilliseconds)) return DeferredAngleRefreshOutcome.Superseded;
            if (string.IsNullOrWhiteSpace(snapshot) || snapshot.Length > MaximumSnapshotCharacters)
            {
                phase = Phase.Failed; LastReason = "invalid_snapshot"; ResetStability();
                return DeferredAngleRefreshOutcome.Failed;
            }
            if (wrote || NativeRevision != pass.NativeRevision)
                ResetStability();
            else
            {
                StableObservations = string.Equals(snapshot, lastReadOnlySnapshot, StringComparison.Ordinal)
                    ? StableObservations + 1 : 1;
                lastReadOnlySnapshot = snapshot;
            }
            // Both observations must come from separate read-only passes, and CompletePass must
            // be called only after lock release. A write is never accepted as a stable observation.
            if (StableObservations >= 2 && ActiveMilliseconds <= MaximumActiveMilliseconds)
            {
                phase = Phase.Settled; LastReason = null;
                return DeferredAngleRefreshOutcome.Settled;
            }
            if (BudgetReached()) return Exhaust();
            phase = Phase.Pending;
            return DeferredAngleRefreshOutcome.Retry;
        }

        public DeferredAngleRefreshOutcome FailPass(DeferredAngleRefreshPass pass,
            bool retryable, long monotonicMilliseconds)
        {
            if (!FinishPass(pass, monotonicMilliseconds)) return DeferredAngleRefreshOutcome.Superseded;
            ResetStability();
            if (!retryable)
            {
                phase = Phase.Failed; LastReason = "nonretryable_failure";
                return DeferredAngleRefreshOutcome.Failed;
            }
            if (BudgetReached()) return Exhaust();
            phase = Phase.Pending; LastReason = "retryable_failure";
            return DeferredAngleRefreshOutcome.Retry;
        }

        private void StartGeneration()
        {
            Generation++;
            PassCount = 0; ActiveMilliseconds = 0;
            ExhaustionReason = null; LastReason = null; ResetStability();
            // An external request during an in-flight pass is retained. Its old completion
            // releases the lease but cannot consume the new generation's budget or snapshot.
            phase = inFlightToken == 0 ? Phase.Pending : Phase.Running;
        }

        private bool FinishPass(DeferredAngleRefreshPass pass, long monotonicMilliseconds)
        {
            if (inFlightToken == 0 || pass.Token != inFlightToken || pass.Generation != inFlightGeneration)
                throw new InvalidOperationException("refresh pass is not the current in-flight lease");
            CheckClock(monotonicMilliseconds);
            inFlightToken = 0;
            if (pass.Generation != Generation)
            {
                phase = Phase.Pending;
                return false;
            }
            var elapsed = monotonicMilliseconds - passStartedAt;
            ActiveMilliseconds = elapsed > long.MaxValue - ActiveMilliseconds
                ? long.MaxValue : ActiveMilliseconds + elapsed;
            return true;
        }

        private bool BudgetReached() => PassCount >= MaximumPasses || ActiveMilliseconds >= MaximumActiveMilliseconds;
        private DeferredAngleRefreshOutcome Exhaust()
        {
            phase = Phase.Exhausted; ResetStability();
            ExhaustionReason = ActiveMilliseconds >= MaximumActiveMilliseconds ? "active_time_budget" : "pass_limit";
            LastReason = ExhaustionReason;
            return DeferredAngleRefreshOutcome.Exhausted;
        }
        private void ResetStability() { StableObservations = 0; lastReadOnlySnapshot = null; }
        private void CheckClock(long now)
        {
            if (now < 0 || now < lastClock) throw new ArgumentOutOfRangeException(nameof(now), "clock must be monotonic");
            lastClock = now;
        }
    }
}
