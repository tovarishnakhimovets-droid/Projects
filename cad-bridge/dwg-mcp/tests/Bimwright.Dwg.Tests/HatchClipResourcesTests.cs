using System;
using System.Collections.Generic;
using System.Threading;
using Bimwright.Dwg.Plugin.Hatches;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HatchClipResourcesTests
    {
        private sealed class Probe : IDisposable
        {
            private readonly Action cleanup;
            internal int Count;
            internal Probe(Action cleanup) { this.cleanup = cleanup; }
            public void Dispose() { Count++; cleanup(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CreatedWrappersCloseOnOwnerThreadBeforeTransactionEnds(bool failAppend)
        {
            int owner = Environment.CurrentManagedThreadId; bool transactionAlive = true;
            var order = new List<string>();
            void Check(string name) { Assert.True(transactionAlive); Assert.Equal(owner, Environment.CurrentManagedThreadId); order.Add(name); }
            try
            {
                using (var transaction = new Probe(() => { transactionAlive = false; order.Add("transaction"); }))
                using (var resources = new HatchClipResources())
                {
                    resources.Own(new Probe(() => Check("curve")));
                    resources.Own(new Probe(() => Check("hatch")));
                    if (failAppend) throw new ArgumentException("native append failed");
                    resources.Dispose();
                    order.Add("commit");
                }
            }
            catch (ArgumentException) when (failAppend) { }
            Assert.Equal(failAppend ? new[] { "hatch", "curve", "transaction" } : new[] { "hatch", "curve", "commit", "transaction" }, order);
        }

        [Fact]
        public void CleanupContinuesAfterFailuresAndDoesNotRetryNativeDisposals()
        {
            var order = new List<int>(); var resources = new HatchClipResources();
            var first = resources.Own(new Probe(() => { order.Add(1); throw new InvalidOperationException("first"); }));
            var middle = resources.Own(new Probe(() => order.Add(2)));
            var last = resources.Own(new Probe(() => { order.Add(3); throw new ArgumentException("last"); }));
            var error = Assert.Throws<AggregateException>(() => resources.Dispose());
            Assert.Equal(2, error.InnerExceptions.Count); Assert.Equal(new[] { 3, 2, 1 }, order);
            resources.Dispose();
            Assert.Equal(1, first.Count); Assert.Equal(1, middle.Count); Assert.Equal(1, last.Count);
        }

        [Fact]
        public void DuplicateRegistrationDoesNotDoubleDisposeAndClosedScopeRejectsNewObjects()
        {
            var resource = new Probe(() => { }); var resources = new HatchClipResources();
            Assert.Same(resource, resources.Own(resource)); resources.Own(resource);
            Assert.Throws<ArgumentNullException>(() => resources.Own<Probe>(null));
            resources.Dispose(); Assert.Equal(1, resource.Count);
            Assert.Throws<ObjectDisposedException>(() => resources.Own(new Probe(() => { })));
        }

        [Fact]
        public void ForeignThreadCannotTransferOrReleaseCadWrappers()
        {
            var resources = new HatchClipResources(); var probe = resources.Own(new Probe(() => { }));
            Exception transfer = null, release = null;
            var thread = new Thread(() =>
            {
                try { resources.Own(probe); } catch (Exception e) { transfer = e; }
                try { resources.Dispose(); } catch (Exception e) { release = e; }
            });
            thread.Start(); thread.Join();
            Assert.IsType<InvalidOperationException>(transfer); Assert.IsType<InvalidOperationException>(release);
            Assert.Equal(0, probe.Count); resources.Dispose(); Assert.Equal(1, probe.Count);
        }
    }
}
