using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Hatches
{
    // Created wrappers must be disposed on the CAD thread while their transaction
    // is still alive, including after an append or native geometry exception.
    internal sealed class HatchClipResources : IDisposable
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly List<IDisposable> objects = new List<IDisposable>();
        private readonly HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private bool disposed;

        internal T Own<T>(T value) where T : class, IDisposable
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(HatchClipResources));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (seen.Add(value)) objects.Add(value);
            return value;
        }

        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            List<Exception> failures = null;
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                try { objects[i].Dispose(); }
                catch (Exception error) { (failures ??= new List<Exception>()).Add(error); }
            }
            objects.Clear(); seen.Clear();
            if (failures != null) throw new AggregateException("Hatch clip wrapper cleanup failed; inspect drawing state before retrying", failures);
        }

        private void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Hatch clip resources must stay on their creating thread");
        }
    }
}
