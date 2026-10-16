using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.View
{
    internal sealed class DrawingRevision
    {
        private static readonly object Gate = new object();
        private static ConditionalWeakTable<Document, DrawingRevision> states =
            new ConditionalWeakTable<Document, DrawingRevision>();
        private static readonly HashSet<DrawingRevision> LiveStates = new HashSet<DrawingRevision>();
        private static DocumentCollection documents;
        private Database database;
        private long revision;
        private int graphicsReadDepth;
        private long graphicsRevision;
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");
        internal long Revision => Interlocked.Read(ref revision);
        internal long GraphicsRevision => Interlocked.Read(ref graphicsRevision);

        // Only the synchronous native preview/view+regen calls enter this scope.
        // It is not a blanket suppression: data writes and structural events still
        // invalidate history even when a native reactor performs them inside a read.
        internal IDisposable BeginGraphicsRead()
        {
            Interlocked.Increment(ref graphicsReadDepth);
            return new GraphicsRead(this);
        }

        private sealed class GraphicsRead : IDisposable
        {
            private DrawingRevision owner;
            internal GraphicsRead(DrawingRevision owner) => this.owner = owner;
            public void Dispose()
            {
                var state = Interlocked.Exchange(ref owner, null);
                if (state != null) Interlocked.Decrement(ref state.graphicsReadDepth);
            }
        }
        internal static DrawingRevision For(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            lock (Gate)
            {
                if (documents == null)
                {
                    documents = Application.DocumentManager;
                    documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
                }
                return states.GetValue(document, Create);
            }
        }

        private static DrawingRevision Create(Document document)
        {
            // Document.Database can return a fresh managed wrapper on every read.
            // Key by the document lifetime and retain the wrapper owning our handlers.
            var state = new DrawingRevision { database = document.Database };
            state.database.ObjectModified += state.OnObjectModified;
            state.database.ObjectAppended += state.OnStructuralChange;
            state.database.ObjectErased += state.OnObjectErased;
            state.database.ObjectUnappended += state.OnStructuralChange;
            state.database.ObjectReappended += state.OnStructuralChange;
            LiveStates.Add(state);
            return state;
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs args)
        {
            lock (Gate)
            {
                if (!states.TryGetValue(args.Document, out var state)) return;
                state.Detach();
                states.Remove(args.Document);
                LiveStates.Remove(state);
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                foreach (var state in LiveStates) state.Detach();
                LiveStates.Clear();
                states = new ConditionalWeakTable<Document, DrawingRevision>();
                if (documents != null)
                    documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                documents = null;
            }
        }

        private void Detach()
        {
            database.ObjectModified -= OnObjectModified;
            database.ObjectAppended -= OnStructuralChange;
            database.ObjectErased -= OnObjectErased;
            database.ObjectUnappended -= OnStructuralChange;
            database.ObjectReappended -= OnStructuralChange;
            // AutoCAD owns this database; do not dispose it.
            database = null;
        }

        private void OnStructuralChange(object sender, ObjectEventArgs args) => Interlocked.Increment(ref revision);
        private void OnObjectErased(object sender, ObjectErasedEventArgs args) => Interlocked.Increment(ref revision);

        private void OnObjectModified(object sender, ObjectEventArgs args)
        {
            var value = args.DBObject;
            // Navigation itself updates view state; its full metadata is checked
            // independently. All other observed database-object changes invalidate history.
            if (value is ViewportTableRecord || value is Viewport) return;
            try
            {
                // IsModifiedGraphics alone also includes real entity changes.
                // IsModified reports AssertWriteEnabled; require it and every
                // other persistent-change flag to be false before accepting a
                // graphics-only notification from our bounded rendering call.
                if (Volatile.Read(ref graphicsReadDepth) > 0 && value is Entity &&
                    value.IsModifiedGraphics && !value.IsModified && !value.IsModifiedXData &&
                    !value.IsNewObject && !value.IsUndoing && !value.IsEraseStatusToggled)
                {
                    Interlocked.Increment(ref graphicsRevision);
                    return;
                }
            }
            catch
            {
                // Unreadable native flags are unknown changes, never proof of a read.
            }
            Interlocked.Increment(ref revision);
        }
    }
}
