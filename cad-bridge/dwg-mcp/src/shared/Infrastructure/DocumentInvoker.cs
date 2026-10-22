using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    public static class DocumentInvoker
    {
        private static MainThreadExecutor mainThread;

        // Called by the add-in entry point on AutoCAD's main thread, before
        // accepting transport requests. Document locking alone is not UI dispatch.
        internal static void Initialize()
        {
            if (mainThread != null) return;
            mainThread = new MainThreadExecutor();
            Application.Idle += OnIdle;
        }

        internal static void Shutdown()
        {
            Application.Idle -= OnIdle;
            mainThread = null;
        }

        private static void OnIdle(object sender, EventArgs args) => mainThread?.RunPending();

        /// <summary>
        /// Serializes AutoCAD API access and runs the action on its main thread.
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action)
            => Invoke(null, null, action);

        internal static T InvokeApplication<T>(Func<T> action)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return DwgApiExecutor.Invoke(() => executor.Invoke(action));
        }

        internal static T Invoke<T>(string command, string documentId, Func<Document, T> action)
            => InvokeApplication(() =>
            {
                var doc = DocumentRouting.Resolve(documentId);
                var isActive = ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc);
                var error = DocumentRoutingPolicy.ValidateCommandTarget(command, isActive);
                if (error != null) throw new InvalidOperationException(error);
                if (DocumentRoutingPolicy.UsesReadLock(command))
                {
                    // Protect the inspected database without taking a write lock
                    // or prompting the user to activate/finish another drawing.
                    using (doc.LockDocument(DocumentLockMode.Read, null, null, false)) return action(doc);
                }
                using (doc.LockDocument())
                {
                    return action(doc);
                }
            });
    }
}
