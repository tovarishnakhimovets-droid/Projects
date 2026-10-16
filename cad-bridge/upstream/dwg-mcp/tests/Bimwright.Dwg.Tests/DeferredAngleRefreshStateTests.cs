using System;
using Bimwright.Dwg.Plugin.Blocks;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class DeferredAngleRefreshStateTests
    {
        [Fact]
        public void A_write_requires_two_later_matching_read_only_passes()
        {
            var state = NewEpisode();
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "measurement=108.922;native=108,9;attribute=108,9", true, 0, 10));
            Assert.Equal(0, state.StableObservations);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "measurement=108.922;native=108,9;attribute=108,9", false, 20, 30));
            Assert.True(state.IsPending);
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "measurement=108.922;native=108,9;attribute=108,9", false, 40, 50));
            Assert.True(state.IsSettled);
            Assert.False(state.IsPending);
            Assert.False(state.TryBeginPass(60, out _));
        }

        [Fact]
        public void Even_an_already_matching_value_requires_a_post_lock_recheck()
        {
            var state = NewEpisode();
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "all-current", false, 0, 1));
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "all-current", false, 2, 3));
        }

        [Fact]
        public void Native_dimension_change_during_the_lock_is_retained()
        {
            var state = NewEpisode(10);
            Assert.True(state.TryBeginPass(0, out var ticket));
            state.Notify(11, DeferredAngleRefreshNotification.DimensionChange);
            Assert.True(state.IsRunning);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, state.CompletePass(ticket, "old-geometry", false, 1));
            Assert.Equal(0, state.StableObservations);
            Assert.Equal(1, state.PassCount);
            Assert.Equal(1, state.Generation);
            Assert.Equal(11, state.NativeRevision);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "new-geometry", false, 2, 3));
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "new-geometry", false, 4, 5));
        }

        [Fact]
        public void A_late_event_after_the_first_completion_resets_stability()
        {
            var state = NewEpisode(10);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "cached-old-value", false, 0, 1));
            state.Notify(11, DeferredAngleRefreshNotification.DimensionChange);
            Assert.Equal(0, state.StableObservations);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "evaluated-new-value", true, 2, 3));
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "evaluated-new-value", false, 4, 5));
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "evaluated-new-value", false, 6, 7));
        }

        [Fact]
        public void A_native_event_after_settlement_starts_a_fresh_bounded_episode()
        {
            var state = NewEpisode(10);
            Pass(state, "initial", false, 0, 1);
            Pass(state, "initial", false, 2, 3);
            state.Notify(11, DeferredAngleRefreshNotification.DimensionChange);
            Assert.True(state.IsPending);
            Assert.Equal(2, state.Generation);
            Assert.Equal(0, state.PassCount);
            Assert.Equal(0, state.ActiveMilliseconds);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "late-native-value", false, 4, 5));
        }

        [Fact]
        public void A_new_external_generation_cannot_be_removed_by_an_old_completion()
        {
            var state = NewEpisode(10);
            Assert.True(state.TryBeginPass(0, out var old));
            state.Notify(11, DeferredAngleRefreshNotification.ExternalChange);
            Assert.False(state.TryBeginPass(1, out _));
            Assert.Equal(DeferredAngleRefreshOutcome.Superseded, state.CompletePass(old, "stale-input", true, 1500));
            Assert.True(state.IsPending);
            Assert.Equal(2, state.Generation);
            Assert.Equal(0, state.PassCount);
            Assert.Equal(0, state.ActiveMilliseconds);
            Assert.Equal(0, state.StableObservations);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "latest-input", false, 1501, 1502));
        }

        [Fact]
        public void Own_attribute_notifications_do_not_invalidate_or_restart_work()
        {
            var state = NewEpisode(10);
            Assert.True(state.TryBeginPass(0, out var ticket));
            state.Notify(100, DeferredAngleRefreshNotification.OwnAttributeChange);
            Assert.Equal(10, state.NativeRevision);
            Assert.Equal(1, state.Generation);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, state.CompletePass(ticket, "native-and-attribute-current", false, 1));
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "native-and-attribute-current", false, 2, 3));
            state.Notify(101, DeferredAngleRefreshNotification.OwnAttributeChange);
            Assert.True(state.IsSettled);
        }

        [Fact]
        public void A_changed_raw_source_requires_recheck_even_when_display_rounds_to_the_same_text()
        {
            var state = NewEpisode();
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "raw=13.224;display=13,2;attribute=13,2", false, 0, 1));
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "raw=13.210;display=13,2;attribute=13,2", false, 2, 3));
            Assert.Equal(1, state.StableObservations);
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "raw=13.210;display=13,2;attribute=13,2", false, 4, 5));
        }

        [Fact]
        public void Repeated_native_dirty_events_cannot_extend_the_six_pass_budget()
        {
            var state = NewEpisode(10);
            for (var i = 0; i < DeferredAngleRefreshState.MaximumPasses; i++)
            {
                Assert.True(state.TryBeginPass(i * 2, out var ticket));
                state.Notify(11 + i, DeferredAngleRefreshNotification.DimensionChange);
                Assert.Equal(i == 5 ? DeferredAngleRefreshOutcome.Exhausted : DeferredAngleRefreshOutcome.Retry,
                    state.CompletePass(ticket, "native-changing", false, i * 2 + 1));
            }
            Assert.True(state.IsExhausted);
            Assert.Equal("pass_limit", state.ExhaustionReason);
            state.Notify(1000, DeferredAngleRefreshNotification.DimensionChange);
            Assert.False(state.IsPending);
            Assert.False(state.TryBeginPass(13, out _));
        }

        [Fact]
        public void A_real_external_change_restarts_an_exhausted_episode_even_at_the_same_revision()
        {
            var state = NewEpisode(10);
            for (var i = 0; i < 6; i++) Pass(state, "changing-" + i, true, i * 2, i * 2 + 1);
            Assert.True(state.IsExhausted);
            state.Notify(10, DeferredAngleRefreshNotification.ExternalChange);
            Assert.True(state.IsPending);
            Assert.Null(state.ExhaustionReason);
            Assert.Equal(0, state.PassCount);
            Assert.Equal(0, state.ActiveMilliseconds);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "new-command", false, 20, 21));
        }

        [Fact]
        public void The_two_second_budget_charges_active_work_but_not_idle_intervals()
        {
            var state = NewEpisode();
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "initial-write", true, 0, 900));
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "current", false, 100000, 100900));
            Assert.Equal(1800, state.ActiveMilliseconds);
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "current", false, 200000, 200100));
            Assert.Equal(1900, state.ActiveMilliseconds);
        }

        [Fact]
        public void Active_time_exhaustion_stops_retrying_and_preserves_a_reason()
        {
            var state = NewEpisode();
            Pass(state, "written", true, 0, 1100);
            Assert.Equal(DeferredAngleRefreshOutcome.Exhausted, Pass(state, "changed-again", true, 10000, 10900));
            Assert.True(state.IsExhausted);
            Assert.Equal("active_time_budget", state.ExhaustionReason);
            Assert.Equal(2000, state.ActiveMilliseconds);
            Assert.False(state.TryBeginPass(10901, out _));
        }

        [Fact]
        public void Retryable_lock_failure_retains_work_but_consumes_a_bounded_attempt()
        {
            var state = NewEpisode();
            Assert.True(state.TryBeginPass(0, out var ticket));
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, state.FailPass(ticket, true, 1));
            Assert.True(state.IsPending);
            Assert.Equal(1, state.PassCount);
            Assert.Equal(0, state.StableObservations);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, Pass(state, "current", false, 2, 3));
            Assert.Equal(DeferredAngleRefreshOutcome.Settled, Pass(state, "current", false, 4, 5));
        }

        [Fact]
        public void Permanent_incomplete_read_does_not_silently_retry_until_a_new_command()
        {
            var state = NewEpisode();
            Assert.True(state.TryBeginPass(0, out var ticket));
            Assert.Equal(DeferredAngleRefreshOutcome.Failed, state.FailPass(ticket, false, 1));
            Assert.False(state.IsPending);
            Assert.Equal("nonretryable_failure", state.LastReason);
            state.Notify(11, DeferredAngleRefreshNotification.DimensionChange);
            Assert.False(state.IsPending);
            state.Notify(11, DeferredAngleRefreshNotification.ExternalChange);
            Assert.True(state.IsPending);
        }

        [Fact]
        public void A_pass_cannot_be_completed_twice_or_overlap_another_pass()
        {
            var state = NewEpisode();
            Assert.True(state.TryBeginPass(0, out var ticket));
            Assert.False(state.TryBeginPass(1, out _));
            state.CompletePass(ticket, "current", false, 2);
            Assert.Throws<InvalidOperationException>(() => state.CompletePass(ticket, "current", false, 3));
            Assert.Throws<InvalidOperationException>(() => state.FailPass(default, true, 3));
        }

        [Fact]
        public void Clock_must_be_monotonic_and_old_native_notifications_do_not_supersede_current_work()
        {
            var state = NewEpisode(10);
            state.Notify(9, DeferredAngleRefreshNotification.ExternalChange);
            Assert.Equal(10, state.NativeRevision);
            Assert.Equal(1, state.Generation);
            Assert.True(state.TryBeginPass(10, out var ticket));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.CompletePass(ticket, "current", false, 9));
            Assert.True(state.IsRunning);
            Assert.Equal(DeferredAngleRefreshOutcome.Retry, state.CompletePass(ticket, "current", false, 11));
        }

        private static DeferredAngleRefreshState NewEpisode(long revision = 1)
        {
            var state = new DeferredAngleRefreshState();
            state.Notify(revision);
            return state;
        }
        private static DeferredAngleRefreshOutcome Pass(DeferredAngleRefreshState state,
            string snapshot, bool wrote, long begin, long end)
        {
            Assert.True(state.TryBeginPass(begin, out var ticket));
            return state.CompletePass(ticket, snapshot, wrote, end);
        }
    }
}
