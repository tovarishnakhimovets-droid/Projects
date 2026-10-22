using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Drawing revision")]
    public sealed class DocumentInvokerRoutingTests : IDisposable
    {
        private readonly Document active = new Document();
        private readonly Document inactive = new Document();

        public DocumentInvokerRoutingTests()
        {
            Application.DocumentManagerReadError = null;
            Application.DocumentManager.ResetDocuments();
            Application.DocumentManager.Create(active);
            Application.DocumentManager.Create(inactive);
            Application.DocumentManager.Activate(active);
            DocumentRouting.ResolveOverride = id => id == null ? active : inactive;
            DocumentInvoker.Initialize();
        }

        public void Dispose()
        {
            DocumentInvoker.Shutdown();
            DocumentRouting.ResolveOverride = null;
            Application.DocumentManager.ResetDocuments();
        }

        [Fact]
        public void InactiveReadUsesOnlyReadLockWithoutPromptOrTabChange()
        {
            var result = DocumentInvoker.Invoke("get_entity_properties", Guid.NewGuid().ToString("N"), doc =>
            {
                Assert.Same(inactive, doc);
                Assert.Equal(1, inactive.LockDepth);
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
            Assert.Equal(DocumentLockMode.Read, inactive.LastLockMode);
            Assert.False(inactive.LastLockPrompt);
            Assert.Equal(0, inactive.LockDepth);
            Assert.Equal(0, active.LockCount);
        }

        [Fact]
        public void InactiveWriteRefusesBeforeTakingLockOrCallingHandler()
        {
            var calls = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("create_line", Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Contains("requires", error.Message);
            Assert.Equal(0, calls);
            Assert.Equal(0, inactive.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
        }

        [Theory]
        [InlineData("get_drawing_info")]
        [InlineData("get_entity_properties")]
        [InlineData("query_entities")]
        [InlineData("count_entities")]
        [InlineData("list_layers")]
        [InlineData("list_text_styles")]
        [InlineData("select_by_layer")]
        [InlineData("select_by_type")]
        [InlineData("get_selection")]
        [InlineData("get_selected_texts")]
        public void ActiveReadUsesReadLockWithoutPrompt(string command)
        {
            var result = DocumentInvoker.Invoke(command, null, doc =>
            {
                Assert.Same(active, doc);
                Assert.Equal(1, active.LockDepth);
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Equal(DocumentLockMode.Read, active.LastLockMode);
            Assert.False(active.LastLockPrompt);
            Assert.Equal(0, active.LockDepth);
            Assert.Equal(0, inactive.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
        }

        [Theory]
        [InlineData("get_selection")]
        [InlineData("get_selected_texts")]
        public void InactiveSelectionRefusesDespiteUsingReadLockWhenActive(string command)
        {
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke(command, Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Equal(0, calls);
            Assert.Equal(0, inactive.LockCount);
            Assert.Equal(0, active.LockCount);
        }

        [Fact]
        public void FailedReadLockCannotActivateOrFallbackToCurrentDrawing()
        {
            inactive.LockError = new InvalidOperationException("target is locked");
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                DocumentInvoker.Invoke("query_entities", Guid.NewGuid().ToString("N"), doc => ++calls));
            Assert.Equal(0, calls);
            Assert.Equal(0, active.LockCount);
            Assert.Same(active, Application.DocumentManager.MdiActiveDocument);
        }

        [Fact]
        public void ApplicationControlNeedsNoActiveDocumentOrWriteLock()
        {
            Application.DocumentManager.Activate(null);
            Assert.Equal(7, DocumentInvoker.InvokeApplication(() => 7));
            Assert.Equal(0, active.LockCount);
            Assert.Equal(0, inactive.LockCount);
        }
    }
}
