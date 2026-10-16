using System;
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
