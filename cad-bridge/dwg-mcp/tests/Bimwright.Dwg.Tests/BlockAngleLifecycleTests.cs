using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.View;
using Xunit;

namespace Bimwright.Dwg.Tests
{
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
            BlockAngleBindingService.LifecycleInspectedIds.Clear();
            BlockAngleBindingService.LifecycleReferences.Clear();
            BlockAngleBindingService.LifecycleRefreshOverride = null;
            BlockAngleBindingService.LifecycleApplyOverride = null;
            BlockAngleBindingService.LifecyclePendingWrites = false;
            BlockAngleBindingService.LifecycleApplyCount = 0;
            BlockAngleBindingService.LifecycleSnapshotReads = 0;
        }

        public void Dispose()
        {
            BlockAngleBindingService.Shutdown();
            Documents.ResetDocuments();
            BlockAngleBindingService.LifecycleRefreshOverride = null;
            BlockAngleBindingService.LifecycleApplyOverride = null;
            BlockAngleBindingService.LifecyclePendingWrites = false;
        }

        private static Document ActiveDocument()
        {
            var doc = new Document();
            Documents.Create(doc);
            Documents.Activate(doc);
            return doc;
        }

        private static BlockReference Reference(Document doc, long id = 101)
        {
            var reference = new BlockReference { ObjectId = new ObjectId(id) };
            reference.Angles["Arm"] = 0;
            if (!BlockAngleBindingService.LifecycleReferences.TryGetValue(doc, out var references))
                BlockAngleBindingService.LifecycleReferences[doc] = references = new Dictionary<ObjectId, BlockReference>();
            references.Add(reference.ObjectId, reference);
            return reference;
        }

        private static void Rotate(Document doc, BlockReference reference, double angle = 1)
        {
            doc.Database.Raise("opened", reference);
            reference.Rotation = angle;
            doc.Database.Raise("modified", reference);
        }

        [Theory]
        [InlineData("created")]
        [InlineData("activated")]
        [InlineData("destroyed")]
        public void Null_event_arguments_and_null_documents_are_safe(string name)
        {
            BlockAngleBindingService.Initialize();
            Assert.Null(Record.Exception(() => RaiseDocumentEvent(name, null)));
            Assert.Null(Record.Exception(() => RaiseDocumentEvent(name, new DocumentCollectionEventArgs())));
            Assert.Null(Record.Exception(() => Documents.Activate(null)));
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
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
        public void Invalid_document_does_not_attach_or_prevent_a_valid_document(string kind)
        {
            var invalid = ActiveDocument();
            var database = invalid.Database;
            if (kind == "null_database") invalid.Database = null;
            if (kind == "database_throws") invalid.DatabaseFactory = () => throw new InvalidOperationException("not ready");
            if (kind == "disposed_database") database.IsDisposed = true;
            Assert.Null(Record.Exception(BlockAngleBindingService.Initialize));
            Assert.Null(Record.Exception(() => Documents.Activate(invalid)));
            Assert.Equal(0, invalid.CommandSubscribers);
            Assert.Equal(0, database.Subscribers);
            var valid = ActiveDocument();
            Assert.Equal(2, valid.Database.Subscribers);
            Rotate(valid, Reference(valid));
            Application.RaiseIdle();
            Assert.Same(valid, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
        }

        [Fact]
        public void Database_becoming_ready_attaches_on_activation_without_periodic_polling()
        {
            var doc = ActiveDocument();
            doc.DatabaseFactory = () => null;
            BlockAngleBindingService.Initialize();
            Assert.Equal(0, Application.IdleSubscribers);
            doc.DatabaseFactory = null;
            Documents.Activate(doc);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(2, doc.Database.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Initialize_and_all_command_completions_never_scan_or_hook_idle()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Documents.Activate(doc);
            foreach (var command in new[] { "ended", "cancelled", "failed" }) doc.RaiseCommand(command);
            for (var i = 0; i < 10; i++) Application.RaiseIdle();
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, BlockAngleBindingService.LifecycleSnapshotReads);
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, doc.LockCount);
        }

        [Theory]
        [InlineData("active")]
        [InlineData("readonly")]
        [InlineData("quiescent")]
        public void Native_preflight_exception_unhooks_idle_until_a_new_object_event(string phase)
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            Rotate(doc, reference);
            var error = new InvalidOperationException("native preflight unavailable");
            if (phase == "active") Documents.ActiveDocumentReadError = error;
            if (phase == "readonly") doc.IsReadOnlyReadError = error;
            if (phase == "quiescent") doc.Editor.QuiescenceReadError = error;
            try { Assert.Null(Record.Exception(Application.RaiseIdle)); }
            finally
            {
                Documents.ActiveDocumentReadError = null;
                doc.IsReadOnlyReadError = null;
                doc.Editor.QuiescenceReadError = null;
            }
            Assert.Equal(0, Application.IdleSubscribers);
            for (var i = 0; i < 10; i++) Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Rotate(doc, reference, 2);
            Application.RaiseIdle();
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, Application.IdleSubscribers);
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
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(2, doc.Database.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
            Rotate(doc, Reference(doc));
            Assert.Equal(1, Application.IdleSubscribers);
            Documents.Destroy(doc);
            Documents.Destroy(doc);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            BlockAngleBindingService.Shutdown();
            BlockAngleBindingService.Shutdown();
            Assert.Equal(0, Documents.Subscribers);
        }

        [Theory]
        [InlineData("command")]
        [InlineData("database")]
        public void Failed_partial_attachment_unsubscribes_and_can_retry(string kind)
        {
            var doc = ActiveDocument();
            if (kind == "command") doc.ThrowOnCommandSubscribe = "failed";
            else doc.Database.ThrowOnObjectModifiedSubscribe = true;
            BlockAngleBindingService.Initialize();
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            doc.ThrowOnCommandSubscribe = null;
            doc.Database.ThrowOnObjectModifiedSubscribe = false;
            Documents.Activate(doc);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(2, doc.Database.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Failed_global_initialization_unwinds_subscriptions_and_can_retry()
        {
            var doc = ActiveDocument();
            Documents.ThrowOnSubscribe = "activated";
            Assert.Throws<InvalidOperationException>(BlockAngleBindingService.Initialize);
            Assert.Equal(0, Documents.Subscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            Documents.ThrowOnSubscribe = null;
            BlockAngleBindingService.Initialize();
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(2, doc.Database.Subscribers);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Open_without_modification_and_unrelated_objects_never_schedule_work()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            doc.Database.Raise("opened", reference);
            Assert.Equal(0, Application.IdleSubscribers);
            doc.RaiseCommand("ended");
            doc.Database.Raise("modified", reference); // Stale baseline was discarded.
            foreach (var value in new DBObject[] { new Entity(), new LineAngularDimension2(), new Point3AngularDimension() })
            {
                doc.Database.Raise("opened", value);
                doc.Database.Raise("modified", value);
            }
            doc.Database.Raise("opened", null);
            doc.Database.Raise("modified", null);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
        }

        [Fact]
        public void Static_blocks_and_modified_without_before_snapshot_are_ignored()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            doc.Database.Raise("modified", reference);
            reference.IsDynamicBlock = false;
            Rotate(doc, reference);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, BlockAngleBindingService.LifecycleSnapshotReads);
        }

        [Fact]
        public void Only_the_rotated_reference_is_inspected_once_and_no_background_work_remains()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var target = Reference(doc);
            for (var id = 200; id < 12000; id++) Reference(doc, id);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            Rotate(doc, target);
            doc.Database.Raise("modified", target); // Coalesced native notifications.
            doc.RaiseCommand("ended");
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(1, Application.IdleSubscribers);
            Application.RaiseIdle();
            Assert.Equal(target.ObjectId, Assert.Single(BlockAngleBindingService.LifecycleInspectedIds).Id);
            Assert.Equal(1, BlockAngleBindingService.LifecycleApplyCount);
            Assert.Equal(1, doc.LockCount);
            Assert.Equal(0, Application.IdleSubscribers);
            for (var i = 0; i < 10; i++) { Application.RaiseIdle(); doc.RaiseCommand("ended"); }
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
        }

        [Fact]
        public void Non_angle_changes_and_rotate_then_restore_skip_the_write_lock()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            doc.Database.Raise("opened", reference);
            reference.IsModifiedGraphics = true;
            reference.Rotation = 1;
            reference.Rotation = 0;
            doc.Database.Raise("modified", reference);
            Application.RaiseIdle();
            Assert.Single(BlockAngleBindingService.LifecycleInspectedIds);
            Assert.Equal(0, BlockAngleBindingService.LifecycleApplyCount);
            Assert.Equal(0, doc.LockCount);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Dynamic_angle_change_is_detected_with_constant_reference_rotation()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            doc.Database.Raise("opened", reference);
            reference.Angles["Arm"] = 0.5;
            doc.Database.Raise("modified", reference);
            Application.RaiseIdle();
            Assert.Equal(1, BlockAngleBindingService.LifecycleApplyCount);
            Assert.Equal(1, doc.LockCount);
        }

        [Fact]
        public void Snapshot_equality_checks_rotation_named_angles_and_tolerance()
        {
            var first = new BlockReference();
            var second = new BlockReference();
            first.Angles["Arm"] = second.Angles["Arm"] = 1;
            Assert.True(BlockAngleBindingService.LifecycleSnapshotsEqual(first, second));
            second.Rotation = 1e-10;
            second.Angles["Arm"] += 1e-10;
            Assert.True(BlockAngleBindingService.LifecycleSnapshotsEqual(first, second));
            second.Angles["Arm"] = 2;
            Assert.False(BlockAngleBindingService.LifecycleSnapshotsEqual(first, second));
            second.Angles.Clear(); second.Angles["Other"] = 1;
            Assert.False(BlockAngleBindingService.LifecycleSnapshotsEqual(first, second));
            second.Angles["Arm"] = 1;
            Assert.False(BlockAngleBindingService.LifecycleSnapshotsEqual(first, second));
        }

        [Fact]
        public void First_before_snapshot_is_not_replaced_by_a_later_write_open()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            doc.Database.Raise("opened", reference);
            reference.Rotation = 1;
            doc.Database.Raise("opened", reference);
            doc.Database.Raise("modified", reference);
            Application.RaiseIdle();
            Assert.Equal(1, BlockAngleBindingService.LifecycleApplyCount);
        }

        [Fact]
        public void Busy_editor_defers_pending_work_without_inspection_or_lock()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Rotate(doc, Reference(doc));
            doc.Editor.IsQuiescent = false;
            Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, doc.LockCount);
            Assert.Equal(1, Application.IdleSubscribers);
            doc.Editor.IsQuiescent = true;
            Application.RaiseIdle();
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Theory]
        [InlineData("readonly")]
        [InlineData("disposed")]
        public void Unavailable_document_discards_pending_work_without_retry(string kind)
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            Rotate(doc, Reference(doc));
            if (kind == "readonly") doc.IsReadOnly = true; else doc.Database.IsDisposed = true;
            Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, Application.IdleSubscribers);
            doc.IsReadOnly = false; doc.Database.IsDisposed = false;
            doc.RaiseCommand("ended");
            Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
        }

        [Fact]
        public void Pending_work_is_isolated_per_document_and_resumes_only_on_its_activation()
        {
            var first = ActiveDocument();
            var second = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var target = Reference(first);
            Reference(second); // Same handle value in another database.
            Rotate(first, target);
            Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, Application.IdleSubscribers);
            Documents.Activate(first);
            Application.RaiseIdle();
            Assert.Same(first, Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments));
            Assert.Same(first, Assert.Single(BlockAngleBindingService.LifecycleInspectedIds).Document);
        }

        [Theory]
        [InlineData("inspect")]
        [InlineData("lock")]
        [InlineData("write")]
        public void One_shot_failure_releases_state_and_does_not_retry_on_commands_or_idle(string phase)
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            var error = new InvalidOperationException("native surface unavailable");
            if (phase == "inspect") BlockAngleBindingService.LifecycleRefreshOverride = _ => throw error;
            if (phase == "lock") doc.LockError = error;
            if (phase == "write") BlockAngleBindingService.LifecycleApplyOverride = _ => throw error;
            Rotate(doc, reference);
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Equal(0, doc.LockDepth);
            Assert.Equal(0, BlockAngleBindingService.LifecycleOwnWriteCount(doc));
            Assert.Equal(0, Application.IdleSubscribers);
            for (var i = 0; i < 10; i++) { doc.RaiseCommand("ended"); Application.RaiseIdle(); }
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Single(doc.Editor.Messages);
            BlockAngleBindingService.LifecycleRefreshOverride = null;
            BlockAngleBindingService.LifecycleApplyOverride = null;
            doc.LockError = null;
            Rotate(doc, reference, 2);
            Application.RaiseIdle();
            Assert.Equal(2, BlockAngleBindingService.LifecycleRefreshedDocuments.Count);
        }

        [Fact]
        public void Repeated_identical_failures_on_new_rotations_are_reported_once()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecycleRefreshOverride = _ => throw new InvalidOperationException("same failure");
            Rotate(doc, reference, 1); Application.RaiseIdle();
            Rotate(doc, reference, 2); Application.RaiseIdle();
            Assert.Equal(2, BlockAngleBindingService.LifecycleRefreshedDocuments.Count);
            Assert.Single(doc.Editor.Messages);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Own_reference_writes_and_nested_idle_do_not_enqueue_another_pass()
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            var reference = Reference(doc);
            BlockAngleBindingService.LifecyclePendingWrites = true;
            BlockAngleBindingService.LifecycleApplyOverride = current =>
            {
                Assert.Equal(1, current.LockDepth);
                Assert.Equal(2, BlockAngleBindingService.LifecycleOwnWriteCount(current));
                current.Database.Raise("opened", reference);
                current.Database.Raise("modified", reference);
                Application.RaiseIdle();
            };
            Rotate(doc, reference);
            Application.RaiseIdle();
            Application.RaiseIdle();
            Assert.Single(BlockAngleBindingService.LifecycleRefreshedDocuments);
            Assert.Equal(0, BlockAngleBindingService.LifecycleOwnWriteCount(doc));
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Theory]
        [InlineData("active_document")]
        [InlineData("readonly")]
        public void Document_is_revalidated_after_taking_write_lock(string change)
        {
            var doc = ActiveDocument();
            BlockAngleBindingService.Initialize();
            BlockAngleBindingService.LifecyclePendingWrites = true;
            doc.OnLockAcquired = () =>
            {
                if (change == "active_document") Documents.Activate(null); else doc.IsReadOnly = true;
            };
            Rotate(doc, Reference(doc));
            Application.RaiseIdle();
            Assert.Equal(1, doc.LockCount);
            Assert.Equal(0, doc.LockDepth);
            Assert.Equal(0, BlockAngleBindingService.LifecycleApplyCount);
            Assert.Equal(0, Application.IdleSubscribers);
        }

        [Fact]
        public void Closing_pending_document_detaches_the_exact_retained_database_wrapper()
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            Documents.Create(doc); Documents.Activate(doc);
            BlockAngleBindingService.Initialize();
            Assert.Equal(2, native.Subscribers);
            Rotate(doc, Reference(doc));
            Assert.Equal(1, Application.IdleSubscribers);
            Documents.Destroy(doc);
            Assert.Equal(0, native.Subscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, Application.IdleSubscribers);
            Application.RaiseIdle();
            Assert.Empty(BlockAngleBindingService.LifecycleRefreshedDocuments);
        }
    }
}
