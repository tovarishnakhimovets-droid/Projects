using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.View;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // The real service and DrawingRevision share a fake process-wide Application.
    [Collection("Drawing revision")]
    public sealed class BlockAngleLifecycleTests : IDisposable
    {
        private static DocumentCollection Documents => Application.DocumentManager;

        public BlockAngleLifecycleTests()
        {
            BlockAngleBindingService.Shutdown();
            DrawingRevision.Shutdown();
            Documents.ResetDocuments();
            BlockAngleBindingService.LifecycleRefreshedDocuments.Clear();
            BlockAngleBindingService.LifecycleRefreshOverride = null;
        }

        public void Dispose()
        {
            BlockAngleBindingService.Shutdown();
            Documents.ResetDocuments();
            BlockAngleBindingService.LifecycleRefreshOverride = null;
        }

        private static Document ActiveDocument()
        {
            var doc = new Document();
            Documents.Create(doc);
            Documents.Activate(doc);
            return doc;
        }

        [Theory]
        [InlineData("created")]
        [InlineData("activated")]
        [InlineData("destroyed")]
        public void Null_native_event_arguments_are_safe(string name)
        {
            BlockAngleBindingService.Initialize();
            Assert.Null(Record.Exception(() => RaiseDocumentEvent(name, null)));
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(3, Documents.Subscribers);
        }

        [Theory]
        [InlineData("created")]
        [InlineData("activated")]
        [InlineData("destroyed")]
        public void Native_events_with_null_document_are_safe(string name)
        {
            BlockAngleBindingService.Initialize();
            Assert.Null(Record.Exception(() => RaiseDocumentEvent(name,
                new DocumentCollectionEventArgs { Document = null })));
            Assert.Null(Record.Exception(() => Documents.Activate(null)));
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
        }

        private static void RaiseDocumentEvent(string name, DocumentCollectionEventArgs args)
        {
            if (name == "created") Documents.RaiseCreated(args);
            else if (name == "activated") Documents.RaiseActivated(args);
            else Documents.RaiseDestroyed(args);
        }

        [Theory]
        [InlineData("null_database")]
        [InlineData("database_throws")]
        [InlineData("disposed_database")]
        public void Invalid_document_does_not_attach_or_prevent_later_valid_document(string kind)
        {
            var invalid = new Document();
            var database = invalid.Database;
            if (kind == "null_database") invalid.Database = null;
            if (kind == "database_throws") invalid.DatabaseFactory = () => throw new InvalidOperationException("document is not ready");
            if (kind == "disposed_database") database.IsDisposed = true;
            Documents.Create(invalid);

            Assert.Null(Record.Exception(BlockAngleBindingService.Initialize));
            Assert.Null(Record.Exception(() => Documents.Activate(invalid)));
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Equal(0, invalid.CommandSubscribers);
            Assert.Equal(0, database.Subscribers);
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);

            var valid = ActiveDocument();
            Assert.Equal(3, valid.CommandSubscribers);
            Assert.Equal(1, valid.Database.Subscribers);
            Application.RaiseIdle();
            Assert.Same(valid, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Document_can_attach_after_its_database_becomes_available()
        {
            var doc = new Document { DatabaseFactory = () => null };
            Documents.Create(doc);
            BlockAngleBindingService.Initialize();
            Documents.Activate(doc);
            Assert.Equal(0, doc.CommandSubscribers);
            doc.DatabaseFactory = null;
            Documents.Activate(doc);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
            Application.RaiseIdle();
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Idle_attaches_late_ready_active_database_without_another_activation_event()
        {
            var doc = new Document { DatabaseFactory = () => null };
            Documents.Create(doc);
            Documents.Activate(doc);
            BlockAngleBindingService.Initialize();
            Application.RaiseIdle();
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);

            doc.DatabaseFactory = null;
            Application.RaiseIdle();
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Initialize_attach_detach_and_shutdown_are_idempotent()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            BlockAngleBindingService.Initialize();
            Documents.Create(doc);
            Documents.Activate(doc);
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(1, Application.IdleSubscribers);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);

            Documents.Destroy(doc);
            Documents.Destroy(doc);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            BlockAngleBindingService.Shutdown();
            BlockAngleBindingService.Shutdown();
            Assert.Equal(0, Documents.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);

            var later = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Assert.Equal(3, later.CommandSubscribers);
            Assert.Equal(1, later.Database.Subscribers);
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(1, Application.IdleSubscribers);
        }

        [Fact]
        public void Closing_active_document_detaches_it_and_next_document_still_refreshes()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Application.RaiseIdle();
            Documents.Destroy(doc);
            doc.Database.IsDisposed = true;
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);

            var later = ActiveDocument();
            Application.RaiseIdle();
            Assert.Equal(new[] { doc, later }, BlockAngleBindingService.LifecycleRefreshedDocuments);
        }

        [Fact]
        public void Null_active_document_skips_refresh_and_later_activation_is_safe()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Documents.Activate(null);
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Documents.Activate(doc);
            Application.RaiseIdle();
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Theory]
        [InlineData("command_subscription")]
        [InlineData("database_subscription")]
        public void Failed_partial_attachment_unsubscribes_all_completed_steps_and_can_retry(string kind)
        {
            var doc = ActiveDocument();
            if (kind == "command_subscription") doc.ThrowOnCommandSubscribe = "failed";
            else doc.Database.ThrowOnObjectModifiedSubscribe = true;

            Assert.Null(Record.Exception(BlockAngleBindingService.Initialize));
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            doc.ThrowOnCommandSubscribe = null;
            doc.Database.ThrowOnObjectModifiedSubscribe = false;
            Documents.Activate(doc);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
            Application.RaiseIdle();
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Failed_global_initialization_unwinds_native_subscriptions_and_can_retry()
        {
            var doc = ActiveDocument();
            Documents.ThrowOnSubscribe = "activated";
            Assert.Throws<InvalidOperationException>(BlockAngleBindingService.Initialize);
            Assert.Equal(0, Documents.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);

            Documents.ThrowOnSubscribe = null;
            BlockAngleBindingService.Initialize();
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(1, Application.IdleSubscribers);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
            Application.RaiseIdle();
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Database_disposed_after_attachment_is_not_refreshed()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            doc.Database.IsDisposed = true;
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Null(Record.Exception(() => doc.RaiseCommand("ended")));
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Documents.Destroy(doc);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
        }

        [Theory]
        [InlineData("readonly")]
        [InlineData("quiescent")]
        [InlineData("lock")]
        [InlineData("refresh")]
        public void Native_preflight_and_refresh_failures_never_escape_idle_and_next_pass_can_run(string kind)
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var failure = new InvalidOperationException("native document is temporarily unavailable");
            if (kind == "readonly") doc.IsReadOnlyReadError = failure;
            if (kind == "quiescent") doc.Editor.QuiescenceReadError = failure;
            if (kind == "lock") doc.LockError = failure;
            if (kind == "refresh") BlockAngleBindingService.LifecycleRefreshOverride = _ => throw failure;
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Equal(kind == "refresh" ? 1 : 0, BlockAngleBindingService.LifecycleRefreshedDocuments.Count);

            doc.IsReadOnlyReadError = null;
            doc.Editor.QuiescenceReadError = null;
            doc.LockError = null;
            BlockAngleBindingService.LifecycleRefreshOverride = null;
            Application.RaiseIdle();
            Assert.Equal(kind == "refresh" ? 2 : 1, BlockAngleBindingService.LifecycleRefreshedDocuments.Count);
        }

        [Fact]
        public void Native_preflight_failure_does_not_escape_command_callback()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            doc.IsReadOnlyReadError = new InvalidOperationException("document closed during native command callback");
            foreach (var name in new[] { "ended", "cancelled", "failed" })
                Assert.Null(Record.Exception(() => doc.RaiseCommand(name)));
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            doc.IsReadOnlyReadError = null;
            doc.RaiseCommand("ended");
            Assert.Same(doc, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Attach_retains_the_database_wrapper_that_owns_its_subscription()
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            Documents.Create(doc);
            Documents.Activate(doc);
            BlockAngleBindingService.Initialize();
            Documents.Activate(doc);
            Assert.Equal(1, native.Subscribers);
            Documents.Destroy(doc);
            Assert.Equal(0, native.Subscribers);
            Assert.Equal(0, doc.CommandSubscribers);
        }
    }
}
