using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class LockBoundaryProofTests
    {
        private const string Session = "cae61e58c53649b5a76fc2b41c53ad71";
        private static readonly string[] Flags = {
            "is_modified", "is_modified_xdata", "is_modified_graphics",
            "is_new_object", "is_undoing", "is_erase_status_toggled"
        };

        [Fact]
        public void Complete_acquire_notifications_return_distinct_sorted_handles_without_mutating_trace()
        {
            var trace = Trace(7, "F0", "A1", "F0");
            var original = trace.DeepClone();
            Assert.True(LockBoundaryProof.TryValidate(trace, Session, 7, 10, out var handles, out var error), error);
            Assert.Equal(new[] { "A1", "F0" }, handles);
            Assert.True(JToken.DeepEquals(original, trace));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(16)]
        public void Complete_trace_accepts_each_supported_event_count(int count)
        {
            var names = new string[count];
            Array.Fill(names, "A1");
            Assert.True(LockBoundaryProof.TryValidate(Trace(20, names), Session, 20, 20 + count, out var handles, out var error), error);
            Assert.Equal(new[] { "A1" }, handles);
        }

        [Fact]
        public void Valid_range_near_long_maximum_does_not_overflow()
        {
            Assert.True(LockBoundaryProof.TryValidate(Trace(long.MaxValue - 2, "A1", "B2"), Session,
                long.MaxValue - 2, long.MaxValue, out var handles, out var error), error);
            Assert.Equal(new[] { "A1", "B2" }, handles);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(2, 1)]
        [InlineData(0, 17)]
        [InlineData(long.MaxValue, 0)]
        [InlineData(0, long.MaxValue)]
        public void Invalid_or_unbounded_revision_range_refuses(long expected, long actual)
        {
            Refuses(Trace(0, "A1"), expected, actual);
        }

        public static IEnumerable<object[]> InvalidSummaryFields()
        {
            yield return new object[] { "session", new JValue("another-session") };
            yield return new object[] { "session", JValue.CreateNull() };
            yield return new object[] { "current_revision", new JValue(6) };
            yield return new object[] { "current_revision", new JValue("8") };
            yield return new object[] { "current_revision", new JValue(8.0) };
            yield return new object[] { "after_revision", new JValue(6) };
            yield return new object[] { "after_revision", new JValue("7") };
            yield return new object[] { "eventbuffer_truncated", new JValue(true) };
            yield return new object[] { "eventbuffer_truncated", new JValue("false") };
            yield return new object[] { "unrecorded_count", new JValue(1) };
            yield return new object[] { "unrecorded_count", new JValue(-1) };
            yield return new object[] { "unrecorded_count", new JValue("0") };
            yield return new object[] { "diagnostic_capture_failures", new JValue(1) };
            yield return new object[] { "diagnostic_capture_failures", new JValue("0") };
            yield return new object[] { "events", JValue.CreateNull() };
            yield return new object[] { "events", new JObject() };
        }

        [Theory]
        [MemberData(nameof(InvalidSummaryFields))]
        public void Incomplete_or_inconsistent_summary_refuses(string name, JToken value)
        {
            var trace = Trace(7, "A1");
            trace[name] = value.DeepClone();
            Refuses(trace);
        }

        [Theory]
        [InlineData("session")]
        [InlineData("current_revision")]
        [InlineData("after_revision")]
        [InlineData("eventbuffer_truncated")]
        [InlineData("unrecorded_count")]
        [InlineData("diagnostic_capture_failures")]
        [InlineData("events")]
        public void Missing_summary_field_refuses(string name)
        {
            var trace = Trace(7, "A1");
            trace.Remove(name);
            Refuses(trace);
        }

        [Fact]
        public void Null_trace_refuses()
        {
            Refuses(null);
        }

        [Fact]
        public void Summary_from_another_expected_session_refuses()
        {
            Assert.False(LockBoundaryProof.TryValidate(Trace(7, "A1"), "another-session", 7, 8,
                out var handles, out var error));
            Assert.Empty(handles);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("extra")]
        [InlineData("duplicate")]
        [InlineData("gap")]
        [InlineData("reversed")]
        [InlineData("nonobject")]
        public void Event_count_shape_and_sequence_must_cover_exact_revision_range(string defect)
        {
            var trace = Trace(7, "A1", "B2");
            var events = (JArray)trace["events"];
            if (defect == "missing") events.RemoveAt(1);
            if (defect == "extra") events.Add(Event(10, "C3"));
            if (defect == "duplicate") events[1]["revision"] = 8;
            if (defect == "gap") events[1]["revision"] = 10;
            if (defect == "reversed") { events[0]["revision"] = 9; events[1]["revision"] = 8; }
            if (defect == "nonobject") events[1] = new JValue("unknown event");
            Refuses(trace, 7, 9);
        }

        public static IEnumerable<object[]> InvalidEventFields()
        {
            yield return new object[] { "revision", new JValue("8") };
            yield return new object[] { "revision", new JValue(8.0) };
            yield return new object[] { "kind", new JValue("appended") };
            yield return new object[] { "kind", new JValue("erased") };
            yield return new object[] { "kind", new JValue("unappended") };
            yield return new object[] { "kind", new JValue("reappended") };
            yield return new object[] { "kind", new JValue("Modified") };
            yield return new object[] { "class", new JValue("Autodesk.AutoCAD.DatabaseServices.Entity") };
            yield return new object[] { "class", new JValue("Custom.AssocNetwork") };
            yield return new object[] { "class", new JValue("Autodesk.AutoCAD.DatabaseServices.AssocNetworkSubclass") };
            yield return new object[] { "phase", new JValue("write_command") };
            yield return new object[] { "phase", new JValue("write_lock_dispose") };
            yield return new object[] { "phase", new JValue("write_lock_acquire_failed") };
            yield return new object[] { "phase", JValue.CreateNull() };
            yield return new object[] { "flags_complete", new JValue(false) };
            yield return new object[] { "flags_complete", new JValue("true") };
        }

        [Theory]
        [MemberData(nameof(InvalidEventFields))]
        public void Different_or_unknown_native_event_refuses(string name, JToken value)
        {
            var trace = Trace(7, "A1");
            trace["events"][0][name] = value.DeepClone();
            Refuses(trace);
        }

        [Theory]
        [InlineData("revision")]
        [InlineData("kind")]
        [InlineData("class")]
        [InlineData("phase")]
        [InlineData("flags_complete")]
        [InlineData("handle")]
        public void Missing_event_identity_or_completeness_field_refuses(string name)
        {
            var trace = Trace(7, "A1");
            ((JObject)trace["events"][0]).Remove(name);
            Refuses(trace);
        }

        public static IEnumerable<object[]> EveryUnknownOrSetFlag()
        {
            foreach (var flag in Flags)
            {
                yield return new object[] { flag, new JValue(true) };
                yield return new object[] { flag, JValue.CreateNull() };
                yield return new object[] { flag, new JValue("false") };
                yield return new object[] { flag, new JValue(0) };
            }
        }

        [Theory]
        [MemberData(nameof(EveryUnknownOrSetFlag))]
        public void Every_native_flag_must_be_a_known_false_boolean(string name, JToken value)
        {
            var trace = Trace(7, "A1");
            trace["events"][0][name] = value.DeepClone();
            Refuses(trace);
        }

        public static IEnumerable<object[]> EveryFlag()
        {
            foreach (var flag in Flags) yield return new object[] { flag };
        }

        [Theory]
        [MemberData(nameof(EveryFlag))]
        public void Missing_native_flag_refuses(string name)
        {
            var trace = Trace(7, "A1");
            ((JObject)trace["events"][0]).Remove(name);
            Refuses(trace);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("00")]
        [InlineData("0A")]
        [InlineData("a1")]
        [InlineData("0xA1")]
        [InlineData(" A1")]
        [InlineData("A1 ")]
        [InlineData("-1")]
        [InlineData("G1")]
        [InlineData("10000000000000000")]
        public void Invalid_or_noncanonical_handle_refuses(string handle)
        {
            Refuses(Trace(7, handle));
        }

        [Fact]
        public void Refusal_does_not_mutate_diagnostics_or_return_partial_handles()
        {
            var trace = Trace(7, "A1", "B2");
            trace["events"][1]["is_modified"] = true;
            var original = trace.DeepClone();
            Refuses(trace, 7, 9);
            Assert.True(JToken.DeepEquals(original, trace));
        }

        private static void Refuses(JObject trace, long expected = 7, long actual = 8)
        {
            Assert.False(LockBoundaryProof.TryValidate(trace, Session, expected, actual, out var handles, out var error));
            Assert.Empty(handles);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        private static JObject Trace(long expected, params string[] handles)
        {
            var events = new JArray();
            for (var i = 0; i < handles.Length; i++) events.Add(Event(expected + i + 1, handles[i]));
            return new JObject {
                ["session"] = Session, ["current_revision"] = expected + handles.Length,
                ["after_revision"] = expected, ["eventbuffer_truncated"] = false,
                ["unrecorded_count"] = 0, ["diagnostic_capture_failures"] = 0, ["events"] = events
            };
        }

        private static JObject Event(long revision, string handle)
        {
            var item = new JObject {
                ["revision"] = revision, ["kind"] = "modified",
                ["class"] = "Autodesk.AutoCAD.DatabaseServices.AssocNetwork", ["handle"] = handle,
                ["flags_complete"] = true, ["phase"] = "write_lock_acquire"
            };
            foreach (var flag in Flags) item[flag] = false;
            return item;
        }
    }
}
