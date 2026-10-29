using System;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Drawing revision")]
    public class DrawingRevisionTests : IDisposable
    {
        public DrawingRevisionTests() => DrawingRevision.Shutdown();
        public void Dispose() => DrawingRevision.Shutdown();

        [Fact]
        public void Repeated_context_reads_with_new_wrappers_keep_one_tracker()
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            Assert.NotSame(doc.Database, doc.Database);
            Assert.Equal(doc.Database, doc.Database);
            var first = DrawingRevision.For(doc);
            var second = DrawingRevision.For(doc);
            Assert.Same(first, second);
            Assert.Equal(first.SessionId, second.SessionId);
            Assert.Equal(5, native.Subscribers);
            Assert.Equal(1, Application.DocumentManager.Subscribers);
        }

        [Theory]
        [InlineData("modified")]
        [InlineData("appended")]
        [InlineData("erased")]
        [InlineData("unappended")]
        [InlineData("reappended")]
        public void New_wrappers_still_invalidate_on_each_drawing_event(string name)
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            var state = DrawingRevision.For(doc);
            doc.Database.Raise(name, new DBObject());
            Assert.Equal(1, DrawingRevision.For(doc).Revision);
            Assert.Equal(state.SessionId, DrawingRevision.For(doc).SessionId);
        }

        [Fact]
        public void Navigation_objects_do_not_invalidate_drawing_history()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            doc.Database.Raise("modified", new Viewport());
            doc.Database.Raise("modified", new ViewportTableRecord());
            Assert.Equal(0, state.Revision);
        }

        [Fact]
        public void Diagnostic_ring_is_bounded_and_marks_only_missing_requested_events()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            for (int batch = 0; batch < 2; batch++)
                using (state.BeginPhase("batch_" + batch))
                    for (int i = 1; i <= 10; i++)
                        doc.Database.Raise("modified", new DBObject { Handle = new Handle(batch * 10 + i) });
            var all = state.DiagnosticsSince(0);
            Assert.Equal(state.SessionId, (string)all["session"]);
            Assert.Equal(20, (long)all["current_revision"]);
            Assert.Equal(16, (int)all["capacity"]);
            Assert.True((bool)all["eventbuffer_truncated"]);
            var events = (JArray)all["events"];
            Assert.Equal(16, events.Count);
            Assert.Equal(5, (long)events[0]["revision"]);
            Assert.Equal(20, (long)events[15]["revision"]);
            Assert.False((bool)state.DiagnosticsSince(4)["eventbuffer_truncated"]);
            Assert.Equal(10, ((JArray)state.DiagnosticsSince(10)["events"]).Count);
            Assert.Empty((JArray)state.DiagnosticsSince(20)["events"]);
            Assert.Equal(20, state.Revision);
            Assert.Equal(0, state.GraphicsRevision);
        }

        [Theory]
        [InlineData("modified")]
        [InlineData("appended")]
        [InlineData("erased")]
        [InlineData("unappended")]
        [InlineData("reappended")]
        public void Diagnostics_preserve_event_kind_handle_flags_and_phase(string kind)
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var value = new DBObject { Handle = new Handle(0xA1), IsModified = true,
                IsModifiedXData = true, IsModifiedGraphics = true, IsNewObject = true,
                IsUndoing = true, IsEraseStatusToggled = true };
            using (state.BeginPhase("write_lock_acquire")) doc.Database.Raise(kind, value);
            var item = Assert.Single((JArray)state.DiagnosticsSince(0)["events"]);
            Assert.Equal(kind, (string)item["kind"]);
            Assert.Equal("A1", (string)item["handle"]);
            Assert.Equal(typeof(DBObject).FullName, (string)item["class"]);
            Assert.Equal("write_lock_acquire", (string)item["phase"]);
            Assert.True((bool)item["flags_complete"]);
            foreach (var name in new[] { "is_modified", "is_modified_xdata", "is_modified_graphics",
                "is_new_object", "is_undoing", "is_erase_status_toggled" }) Assert.True((bool)item[name]);
        }

        [Fact]
        public void Diagnostic_phases_restore_after_nesting_disposal_and_exception()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginPhase("lock"))
            {
                doc.Database.Raise("modified", new DBObject());
                var inner = state.BeginPhase("command");
                doc.Database.Raise("modified", new DBObject());
                inner.Dispose(); inner.Dispose();
                doc.Database.Raise("modified", new DBObject());
            }
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using (state.BeginPhase("failed_command")) throw new InvalidOperationException("native failure");
            }));
            doc.Database.Raise("modified", new DBObject());
            var items = (JArray)state.DiagnosticsSince(0)["events"];
            Assert.Equal("lock", (string)items[0]["phase"]);
            Assert.Equal("command", (string)items[1]["phase"]);
            Assert.Equal("lock", (string)items[2]["phase"]);
            Assert.Equal(3, items.Count);
            var diagnostics = state.DiagnosticsSince(0);
            Assert.Equal(1, (long)diagnostics["unrecorded_count"]);
            Assert.Null((string)diagnostics["active_phase"]);
            Assert.Equal(4, state.Revision);
        }

        [Fact]
        public void Diagnostics_do_not_add_invalidations_for_already_ignored_navigation_or_graphics()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginPhase("preflight"))
            using (state.BeginGraphicsRead())
            {
                doc.Database.Raise("modified", new Viewport());
                doc.Database.Raise("modified", new ViewportTableRecord());
                doc.Database.Raise("modified", GraphicsOnly());
                doc.Database.Raise("appended", GraphicsOnly());
            }
            Assert.Equal(1, state.Revision);
            Assert.Equal(1, state.GraphicsRevision);
            var diagnostics = state.DiagnosticsSince(0);
            var item = Assert.Single((JArray)diagnostics["events"]);
            Assert.Equal("appended", (string)item["kind"]);
            Assert.Equal("preflight", (string)item["phase"]);
            Assert.Equal(1, (long)diagnostics["graphics_revision"]);
        }

        [Fact]
        public void Failed_native_diagnostic_getters_are_unknown_and_do_not_block_invalidation()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginPhase("preflight")) doc.Database.Raise("modified", new UnreadableDiagnosticObject());
            Assert.Equal(1, state.Revision);
            var diagnostics = state.DiagnosticsSince(0);
            var item = Assert.Single((JArray)diagnostics["events"]);
            Assert.Equal(JTokenType.Null, item["handle"].Type);
            Assert.Equal(JTokenType.Null, item["is_modified"].Type);
            Assert.False((bool)item["flags_complete"]);
            Assert.False((bool)item["is_undoing"]);
            Assert.Equal(0, (long)diagnostics["diagnostic_capture_failures"]);
        }

        [Fact]
        public void Diagnostic_snapshots_are_detached_and_reads_do_not_call_native_getters_again()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var value = new CountingDiagnosticObject { Handle = new Handle(0xA1) };
            using (state.BeginPhase("preflight")) doc.Database.Raise("modified", value);
            Assert.Equal(1, value.HandleReads);
            Assert.Equal(6, value.FlagReads);
            var first = state.DiagnosticsSince(0);
            first["events"][0]["phase"] = "forged";
            first["events"][0]["is_modified"] = false;
            value.IsModified = false;
            value.Handle = new Handle(0xB2);
            var second = state.DiagnosticsSince(0);
            var item = Assert.Single((JArray)second["events"]);
            Assert.Equal("preflight", (string)item["phase"]);
            Assert.Equal("A1", (string)item["handle"]);
            Assert.True((bool)item["is_modified"]);
            Assert.Equal(1, value.HandleReads);
            Assert.Equal(6, value.FlagReads);
            Assert.Equal(1, state.Revision);
        }

        [Fact]
        public void Diagnostic_text_and_document_scope_are_bounded()
        {
            var doc = new Document();
            var otherDoc = new Document();
            var state = DrawingRevision.For(doc);
            var other = DrawingRevision.For(otherDoc);
            using (state.BeginPhase(new string('x', 10000)))
                doc.Database.Raise("modified", new RevisionDiagnosticTestObjectWithAnIntentionallyLongManagedTypeNameWhichExceedsTheWireClassNameBudgetAndMustBeTruncatedToKeepEveryRingRecordBounded());
            var item = Assert.Single((JArray)state.DiagnosticsSince(0)["events"]);
            Assert.Equal(64, ((string)item["phase"]).Length);
            Assert.Equal(128, ((string)item["class"]).Length);
            Assert.Empty((JArray)other.DiagnosticsSince(0)["events"]);
            Assert.NotEqual((string)state.DiagnosticsSince(0)["session"], (string)other.DiagnosticsSince(0)["session"]);
        }

        [Fact]
        public void Ordinary_events_do_not_read_native_diagnostics_or_allocate_records()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var value = new CountingDiagnosticObject();
            for (int i = 0; i < 1000; i++) doc.Database.Raise("modified", value);
            Assert.Equal(1000, state.Revision);
            Assert.Equal(0, value.HandleReads);
            Assert.Equal(0, value.FlagReads);
            var diagnostics = state.DiagnosticsSince(0);
            Assert.Empty((JArray)diagnostics["events"]);
            Assert.Equal(1000, (long)diagnostics["unrecorded_count"]);
            Assert.True((bool)diagnostics["eventbuffer_truncated"]);
        }

        [Fact]
        public void Each_diagnostic_phase_limits_native_reads_and_fresh_phase_has_fresh_budget()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var value = new CountingDiagnosticObject();
            using (state.BeginPhase("large_bind"))
                for (int i = 0; i < 1000; i++) doc.Database.Raise("modified", value);
            Assert.Equal(1000, state.Revision);
            Assert.Equal(16, value.HandleReads);
            Assert.Equal(16 * 6, value.FlagReads);
            var diagnostics = state.DiagnosticsSince(0);
            Assert.Equal(16, ((JArray)diagnostics["events"]).Count);
            Assert.Equal(984, (long)diagnostics["unrecorded_count"]);
            Assert.True((bool)diagnostics["eventbuffer_truncated"]);
            using (state.BeginPhase("write_lock_dispose")) doc.Database.Raise("modified", value);
            var last = state.DiagnosticsSince(1000);
            var item = Assert.Single((JArray)last["events"]);
            Assert.Equal("write_lock_dispose", (string)item["phase"]);
            Assert.Equal(1001, (long)item["revision"]);
            Assert.Equal(0, (long)last["unrecorded_count"]);
            Assert.False((bool)last["eventbuffer_truncated"]);
            Assert.Equal(17, value.HandleReads);
            Assert.Equal(17 * 6, value.FlagReads);
        }

        [Fact]
        public void Structural_callback_on_foreign_thread_skips_native_diagnostic_getters_but_invalidates()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var value = new CountingDiagnosticObject();
            Exception workerError = null;
            var worker = new Thread(() =>
            {
                try { doc.Database.Raise("appended", value); }
                catch (Exception error) { workerError = error; }
            });
            using (state.BeginPhase("write_lock_acquire"))
            {
                worker.Start();
                Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "foreign callback did not finish");
                Assert.Null(workerError);
                Assert.Equal(1, state.Revision);
                Assert.Equal(0, value.HandleReads);
                Assert.Equal(0, value.FlagReads);
                var skipped = state.DiagnosticsSince(0);
                Assert.Empty((JArray)skipped["events"]);
                Assert.Equal(1, (long)skipped["unrecorded_count"]);
                Assert.True((bool)skipped["eventbuffer_truncated"]);

                // The same phase still captures callbacks on its own thread.
                doc.Database.Raise("appended", value);
            }
            Assert.Equal(2, state.Revision);
            Assert.Equal(1, value.HandleReads);
            Assert.Equal(6, value.FlagReads);
            var ownThread = state.DiagnosticsSince(1);
            var item = Assert.Single((JArray)ownThread["events"]);
            Assert.Equal("write_lock_acquire", (string)item["phase"]);
            Assert.Equal("appended", (string)item["kind"]);
            Assert.Equal(0, (long)ownThread["unrecorded_count"]);
            Assert.False((bool)ownThread["eventbuffer_truncated"]);
        }
        [Fact]
        public void Future_diagnostic_revision_returns_empty_nonnegative_range_without_hiding_guard_error()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginPhase("preflight")) doc.Database.Raise("modified", new DBObject());
            var diagnostics = state.DiagnosticsSince(long.MaxValue);
            Assert.Equal(1, (long)diagnostics["current_revision"]);
            Assert.Equal(long.MaxValue, (long)diagnostics["after_revision"]);
            Assert.Empty((JArray)diagnostics["events"]);
            Assert.Equal(0, (long)diagnostics["unrecorded_count"]);
            Assert.False((bool)diagnostics["eventbuffer_truncated"]);
            Assert.Equal(1, state.Revision);
        }
        [Fact]
        public void Invalid_diagnostic_range_does_not_change_revision()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.DiagnosticsSince(-1));
            Assert.Equal(0, state.Revision);
        }

        private sealed class UnreadableDiagnosticObject : DBObject
        {
            public override Handle Handle { get => throw new InvalidOperationException("native handle unavailable"); set { } }
            public override bool IsModified { get => throw new InvalidOperationException("native flags unavailable"); set { } }
        }
        private sealed class CountingDiagnosticObject : DBObject
        {
            private Handle handle;
            internal int HandleReads { get; private set; }
            internal int FlagReads { get; private set; }
            public override Handle Handle { get { HandleReads++; return handle; } set { handle = value; } }
            public override bool IsModified { get { FlagReads++; return base.IsModified; } set { base.IsModified = value; } }
            public override bool IsModifiedXData { get { FlagReads++; return base.IsModifiedXData; } set { base.IsModifiedXData = value; } }
            public override bool IsModifiedGraphics { get { FlagReads++; return base.IsModifiedGraphics; } set { base.IsModifiedGraphics = value; } }
            public override bool IsNewObject { get { FlagReads++; return base.IsNewObject; } set { base.IsNewObject = value; } }
            public override bool IsUndoing { get { FlagReads++; return base.IsUndoing; } set { base.IsUndoing = value; } }
            public override bool IsEraseStatusToggled { get { FlagReads++; return base.IsEraseStatusToggled; } set { base.IsEraseStatusToggled = value; } }
        }
        private sealed class RevisionDiagnosticTestObjectWithAnIntentionallyLongManagedTypeNameWhichExceedsTheWireClassNameBudgetAndMustBeTruncatedToKeepEveryRingRecordBounded : DBObject { }
        private static Entity GraphicsOnly() => new Entity { IsModified = false, IsModifiedGraphics = true };

        [Fact]
        public void Only_bounded_rendering_accepts_proven_graphics_only_notifications()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginGraphicsRead()) doc.Database.Raise("modified", GraphicsOnly());
            Assert.Equal(0, state.Revision);
            Assert.Equal(1, state.GraphicsRevision);
            doc.Database.Raise("modified", GraphicsOnly());
            Assert.Equal(1, state.Revision);
        }

        [Theory]
        [InlineData("data")]
        [InlineData("xdata")]
        [InlineData("new")]
        [InlineData("undo")]
        [InlineData("erase")]
        [InlineData("unknown")]
        public void Persistent_or_unknown_changes_still_invalidate_during_rendering(string flag)
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var entity = GraphicsOnly();
            if (flag == "data") entity.IsModified = true;
            if (flag == "xdata") entity.IsModifiedXData = true;
            if (flag == "new") entity.IsNewObject = true;
            if (flag == "undo") entity.IsUndoing = true;
            if (flag == "erase") entity.IsEraseStatusToggled = true;
            if (flag == "unknown") entity.IsModifiedGraphics = false;
            using (state.BeginGraphicsRead()) doc.Database.Raise("modified", entity);
            Assert.Equal(1, state.Revision);
            Assert.Equal(0, state.GraphicsRevision);
        }

        [Theory]
        [InlineData("appended")]
        [InlineData("erased")]
        [InlineData("unappended")]
        [InlineData("reappended")]
        public void Structural_changes_are_never_filtered_by_rendering_flags(string name)
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginGraphicsRead()) doc.Database.Raise(name, GraphicsOnly());
            Assert.Equal(1, state.Revision);
        }

        [Fact]
        public void Native_nonentity_events_are_not_assumed_to_be_graphics()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginGraphicsRead())
                doc.Database.Raise("modified", new DBObject { IsModified = false, IsModifiedGraphics = true });
            Assert.Equal(1, state.Revision);
        }

        [Fact]
        public void Rendering_scopes_are_nested_and_disposed_once_even_after_failures()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            using (state.BeginGraphicsRead())
            {
                var inner = state.BeginGraphicsRead();
                inner.Dispose(); inner.Dispose();
                doc.Database.Raise("modified", GraphicsOnly());
            }
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using (state.BeginGraphicsRead()) throw new InvalidOperationException("native error");
            }));
            doc.Database.Raise("modified", GraphicsOnly());
            Assert.Equal(1, state.Revision);
            Assert.Equal(1, state.GraphicsRevision);
        }

        [Fact]
        public void Capture_region_and_restore_accept_graphics_notifications_but_keep_content_guard()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var current = ViewReadingTests.Context();
            JObject Read()
            {
                var context = (JObject)current.DeepClone();
                context["document"]["session_id"] = state.SessionId;
                context["document"]["observed_revision"] = state.Revision;
                return context;
            }
            JObject Render(JObject before)
            {
                using (state.BeginGraphicsRead()) doc.Database.Raise("modified", GraphicsOnly());
                CaptureViewContract.EnsureUnchanged(before, Read());
                return ViewReadingTests.Capture(before);
            }
            var root = Render(Read());
            var region = JObject.Parse("{'left':0.1,'top':0.2,'right':0.4,'bottom':0.6}");
            int applies = 0;
            JObject Navigate(JObject source, bool restoring) => ViewReadingWorkflow.Run(source, region, restoring,
                Read, camera =>
                {
                    applies++;
                    using (state.BeginGraphicsRead())
                    {
                        current["camera"] = camera.DeepClone();
                        doc.Database.Raise("modified", GraphicsOnly());
                    }
                }, Render);
            var child = Navigate(root, false);
            var restored = Navigate(root, true);
            Assert.Equal(0, state.Revision);
            Assert.Equal(5, state.GraphicsRevision);
            Assert.Equal((string)root["capture_id"], (string)child["parent_capture_id"]);
            Assert.True(JToken.DeepEquals(root["camera"], restored["camera"]));

            // A real reactor edit is detected even inside the same rendering scope.
            using (state.BeginGraphicsRead()) doc.Database.Raise("modified", new Entity { IsModifiedGraphics = true });
            Assert.Equal(1, state.Revision);
            Assert.Throws<InvalidOperationException>(() => Navigate(restored, false));
            Assert.Throws<InvalidOperationException>(() => Navigate(root, true));
            Assert.Equal(2, applies);
        }

        [Fact]
        public void Real_native_changes_during_capture_still_prevent_publication()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            var before = ViewReadingTests.Context();
            before["document"]["observed_revision"] = state.Revision;
            using (state.BeginGraphicsRead()) doc.Database.Raise("modified", new Entity { IsModifiedGraphics = true });
            var after = (JObject)before.DeepClone();
            after["document"]["observed_revision"] = state.Revision;
            Assert.Throws<InvalidOperationException>(() => CaptureViewContract.EnsureUnchanged(before, after));
        }

        [Fact]
        public void Documents_have_independent_sessions_and_revisions()
        {
            var firstDoc = new Document();
            var secondDoc = new Document();
            var first = DrawingRevision.For(firstDoc);
            var second = DrawingRevision.For(secondDoc);
            Assert.NotEqual(first.SessionId, second.SessionId);
            firstDoc.Database.Raise("modified", new DBObject());
            Assert.Equal(1, first.Revision);
            Assert.Equal(0, second.Revision);
        }

        [Fact]
        public void Closing_document_detaches_only_its_handlers()
        {
            var firstDoc = new Document();
            var secondDoc = new Document();
            var first = DrawingRevision.For(firstDoc);
            DrawingRevision.For(secondDoc);
            Application.DocumentManager.Destroy(firstDoc);
            Application.DocumentManager.Destroy(firstDoc);
            Assert.Equal(0, firstDoc.Database.Subscribers);
            Assert.Equal(5, secondDoc.Database.Subscribers);
            firstDoc.Database.Raise("modified", new DBObject());
            Assert.Equal(0, first.Revision);
        }

        [Fact]
        public void Shutdown_detaches_all_handlers_and_allows_fresh_session()
        {
            var doc = new Document();
            var other = new Document();
            var first = DrawingRevision.For(doc);
            DrawingRevision.For(other);
            DrawingRevision.Shutdown();
            DrawingRevision.Shutdown();
            Assert.Equal(0, doc.Database.Subscribers);
            Assert.Equal(0, other.Database.Subscribers);
            Assert.Equal(0, Application.DocumentManager.Subscribers);
            Assert.NotEqual(first.SessionId, DrawingRevision.For(doc).SessionId);
            Assert.Equal(5, doc.Database.Subscribers);
            Assert.Equal(1, Application.DocumentManager.Subscribers);
        }
    }

    [CollectionDefinition("Drawing revision", DisableParallelization = true)]
    public class DrawingRevisionCollection { }
}
