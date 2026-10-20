using System;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Blocks;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Drawing revision")]
    public sealed class PluginStartupTests : IDisposable
    {
        private readonly App app = new App();
        private static DocumentCollection Documents => Application.DocumentManager;

        public PluginStartupTests()
        {
            Application.DocumentManagerReadError = null;
            app.Terminate();
            Documents.ResetDocuments();
            StartupHostFake.Reset();
            BlockAngleBindingService.LifecycleRefreshedDocuments.Clear();
            BlockAngleBindingService.LifecycleRefreshOverride = null;
        }

        public void Dispose()
        {
            Application.DocumentManagerReadError = null;
            Documents.ActiveDocumentReadError = null;
            app.Terminate();
            Documents.ResetDocuments();
            StartupHostFake.Reset();
        }

        private static Document ActiveDocument()
        {
            var doc = new Document();
            Documents.Create(doc);
            Documents.Activate(doc);
            return doc;
        }

        [Fact]
        public void Initialize_does_not_read_active_database_or_start_listener_synchronously()
        {
            int reads = 0;
            var doc = ActiveDocument();
            doc.DatabaseFactory = () => { reads++; throw new InvalidOperationException("database not ready"); };
            Assert.Null(Record.Exception(app.Initialize));
            Assert.Equal(0, reads);
            Assert.Equal(0, StartupHostFake.Constructed);
            Assert.False(App.ListenerRunning);
            Assert.Equal(1, Application.IdleSubscribers);
        }

        [Fact]
        public void Repeated_initialize_and_idle_start_once_only_after_document_is_ready()
        {
            app.Initialize();
            app.Initialize();
            Application.RaiseIdle();
            Assert.Equal(0, StartupHostFake.Started);
            Assert.Equal(1, Application.IdleSubscribers);
            var doc = ActiveDocument();
            var database = doc.Database;
            doc.Database = null;
            Application.RaiseIdle();
            Assert.Equal(0, StartupHostFake.Started);
            doc.Database = database;
            doc.Editor.IsQuiescent = false;
            Application.RaiseIdle();
            Assert.Equal(0, StartupHostFake.Started);
            doc.Editor.IsQuiescent = true;
            Application.RaiseIdle();
            Application.RaiseIdle();
            app.Initialize();
            Application.RaiseIdle();
            Assert.Equal(1, StartupHostFake.Constructed);
            Assert.Equal(1, StartupHostFake.Started);
            Assert.True(App.ListenerRunning);
            Assert.True(App.ListenerUsesPipe);
            Assert.Equal(4, Application.IdleSubscribers);
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, database.Subscribers);
        }

        [Theory]
        [InlineData("stop")]
        [InlineData("terminate")]
        public void Stop_or_terminate_before_ready_idle_cancels_automatic_start(string action)
        {
            app.Initialize();
            if (action == "stop") App.McpStop(); else app.Terminate();
            var doc = ActiveDocument();
            Application.RaiseIdle();
            Assert.Equal(0, StartupHostFake.Constructed);
            Assert.Equal(0, StartupHostFake.Started);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, Documents.Subscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
        }

        [Fact]
        public void Manual_start_cancels_pending_autostart_without_duplicate_handlers()
        {
            var doc = ActiveDocument();
            app.Initialize();
            App.McpStart();
            App.McpStart();
            Application.RaiseIdle();
            Application.RaiseIdle();
            Assert.Equal(1, StartupHostFake.Started);
            Assert.Equal(4, Application.IdleSubscribers);
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
        }

        [Fact]
        public void Partial_listener_start_failure_cleans_services_and_manual_retry_starts_once()
        {
            var doc = ActiveDocument();
            StartupHostFake.FailNextStart = true;
            app.Initialize();
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Equal(1, StartupHostFake.Started);
            Assert.Equal(1, StartupHostFake.Stopped);
            Assert.Equal(1, StartupHostFake.ToastShutdowns);
            Assert.False(App.ListenerRunning);
            Assert.Null(App.ToastNotifier);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, Documents.Subscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(_ => 1));

            App.McpStart();
            Application.RaiseIdle();
            Assert.Equal(2, StartupHostFake.Started);
            Assert.True(App.ListenerRunning);
            Assert.Equal(4, Application.IdleSubscribers);
            Assert.Equal(3, Documents.Subscribers);
            Assert.Equal(3, doc.CommandSubscribers);
            Assert.Equal(1, doc.Database.Subscribers);
        }

        [Theory]
        [InlineData("document_manager")]
        [InlineData("active_document")]
        [InlineData("database")]
        [InlineData("editor")]
        public void Deferred_startup_and_error_reporting_do_not_escape_native_failures(string kind)
        {
            var doc = ActiveDocument();
            var failure = new InvalidOperationException("native startup surface unavailable");
            doc.Editor.WriteMessageError = failure;
            app.Initialize();
            if (kind == "document_manager") Application.DocumentManagerReadError = failure;
            if (kind == "active_document") Documents.ActiveDocumentReadError = failure;
            if (kind == "database") doc.DatabaseFactory = () => throw failure;
            if (kind == "editor") doc.Editor.QuiescenceReadError = failure;
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.Equal(0, StartupHostFake.Started);
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.False(App.ListenerRunning);
        }

        [Fact]
        public void Start_failure_reporting_is_safe_when_editor_write_throws()
        {
            var doc = ActiveDocument();
            doc.Editor.WriteMessageError = new InvalidOperationException("editor unavailable");
            StartupHostFake.FailNextStart = true;
            Assert.Null(Record.Exception(App.McpStart));
            Assert.False(App.ListenerRunning);
            Assert.Equal(0, Application.IdleSubscribers);
            App.McpStart();
            Assert.True(App.ListenerRunning);
            Assert.Equal(2, StartupHostFake.Started);
        }

        [Fact]
        public void Optional_toast_failure_disables_its_idle_callback_without_stopping_listener()
        {
            ActiveDocument();
            App.McpStart();
            StartupHostFake.ToastFlushError = new InvalidOperationException("toast unavailable");
            Assert.Null(Record.Exception(Application.RaiseIdle));
            Assert.True(App.ListenerRunning);
            Assert.Equal(3, Application.IdleSubscribers);
            Assert.Null(Record.Exception(Application.RaiseIdle));
        }

        [Fact]
        public void Started_app_uses_real_document_invoker_and_termination_removes_it()
        {
            var doc = ActiveDocument();
            App.McpStart();
            Assert.Same(doc, DocumentInvoker.Invoke(current => current));
            int host = Thread.CurrentThread.ManagedThreadId;
            var request = Task.Run(() => DocumentInvoker.Invoke(_ => Thread.CurrentThread.ManagedThreadId));
#pragma warning disable xUnit1031
            Assert.True(SpinWait.SpinUntil(() => { Application.RaiseIdle(); return request.IsCompleted; }, 5000));
            Assert.Equal(host, request.GetAwaiter().GetResult());
#pragma warning restore xUnit1031
            app.Terminate();
            Assert.Equal(0, Application.IdleSubscribers);
            Assert.Equal(0, doc.CommandSubscribers);
            Assert.Equal(0, doc.Database.Subscribers);
            Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(_ => 1));
        }
    }
}
