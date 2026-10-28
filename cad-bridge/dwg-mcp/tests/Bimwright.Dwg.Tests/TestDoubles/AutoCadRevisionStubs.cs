using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Autodesk.AutoCAD.DatabaseServices
{
    public class DBObject
    {
        public ObjectId ObjectId { get; set; }
        public ObjectId OwnerId { get; set; }
        public bool IsModified { get; set; } = true;
        public bool IsModifiedGraphics { get; set; }
        public bool IsModifiedXData { get; set; }
        public bool IsNewObject { get; set; }
        public bool IsUndoing { get; set; }
        public bool IsEraseStatusToggled { get; set; }
    }
    public class Entity : DBObject { }
    public class BlockReference : Entity
    {
        public bool IsDynamicBlock { get; set; } = true;
        public double Rotation { get; set; }
        public Dictionary<string, double> Angles { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
    }
    public class LineAngularDimension2 : Entity { }
    public class Point3AngularDimension : Entity { }
    public class Viewport : Entity { }
    public class ViewportTableRecord : DBObject { }
    public class ObjectEventArgs : EventArgs { public DBObject DBObject { get; set; } }
    public class ObjectErasedEventArgs : ObjectEventArgs { }
    public delegate void ObjectEventHandler(object sender, ObjectEventArgs args);
    public delegate void ObjectErasedEventHandler(object sender, ObjectErasedEventArgs args);

    // Models the observed native behavior: multiple equal Database wrappers share
    // one native event source, but have different managed reference identities.
    public partial class Database
    {
        private sealed class NativeState
        {
            internal ObjectEventHandler Opened, Modified, Appended, Unappended, Reappended;
            internal ObjectErasedEventHandler Erased;
            internal bool Disposed;
            internal bool FailModifiedSubscribe;
        }
        private readonly NativeState state;
        public Database() : this(new NativeState()) { }
        private Database(NativeState state) { this.state = state; }
        public Database NewWrapper() => new Database(state);
        public override bool Equals(object other) => other is Database db && ReferenceEquals(state, db.state);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(state);
        public bool IsDisposed { get => state.Disposed; set => state.Disposed = value; }
        public bool ThrowOnObjectModifiedSubscribe { get => state.FailModifiedSubscribe; set => state.FailModifiedSubscribe = value; }
        public event ObjectEventHandler ObjectOpenedForModify { add => state.Opened += value; remove => state.Opened -= value; }
        public event ObjectEventHandler ObjectModified
        {
            add
            {
                if (state.FailModifiedSubscribe) throw new InvalidOperationException("native database subscription failed");
                state.Modified += value;
            }
            remove => state.Modified -= value;
        }
        public event ObjectEventHandler ObjectAppended { add => state.Appended += value; remove => state.Appended -= value; }
        public event ObjectEventHandler ObjectUnappended { add => state.Unappended += value; remove => state.Unappended -= value; }
        public event ObjectEventHandler ObjectReappended { add => state.Reappended += value; remove => state.Reappended -= value; }
        public event ObjectErasedEventHandler ObjectErased { add => state.Erased += value; remove => state.Erased -= value; }
        public int Subscribers => Count(state.Opened) + Count(state.Modified) + Count(state.Appended) + Count(state.Unappended) + Count(state.Reappended) + Count(state.Erased);
        private static int Count(Delegate value) => value?.GetInvocationList().Length ?? 0;
        public void Raise(string name, DBObject value)
        {
            var args = new ObjectEventArgs { DBObject = value };
            switch (name)
            {
                case "opened": state.Opened?.Invoke(this, args); break;
                case "modified": state.Modified?.Invoke(this, args); break;
                case "appended": state.Appended?.Invoke(this, args); break;
                case "unappended": state.Unappended?.Invoke(this, args); break;
                case "reappended": state.Reappended?.Invoke(this, args); break;
                case "erased": state.Erased?.Invoke(this, new ObjectErasedEventArgs { DBObject = value }); break;
                default: throw new ArgumentException(name);
            }
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    public class DocumentCollectionEventArgs : EventArgs { public Document Document { get; set; } }
    public delegate void DocumentCollectionEventHandler(object sender, DocumentCollectionEventArgs args);
    public class DocumentCollection : IEnumerable
    {
        private readonly List<Document> documents = new List<Document>();
        private Document activeDocument;
        public Exception ActiveDocumentReadError;
        public Document MdiActiveDocument
        {
            get => ActiveDocumentReadError == null ? activeDocument : throw ActiveDocumentReadError;
            set => activeDocument = value;
        }
        public string ThrowOnSubscribe;
        private DocumentCollectionEventHandler created, activated, destroyed;
        public event DocumentCollectionEventHandler DocumentCreated
        {
            add { Subscribe("created"); created += value; }
            remove { created -= value; }
        }
        public event DocumentCollectionEventHandler DocumentActivated
        {
            add { Subscribe("activated"); activated += value; }
            remove { activated -= value; }
        }
        public event DocumentCollectionEventHandler DocumentToBeDestroyed
        {
            add { Subscribe("destroyed"); destroyed += value; }
            remove { destroyed -= value; }
        }
        private void Subscribe(string name)
        {
            if (ThrowOnSubscribe == name) throw new InvalidOperationException("native document collection subscription failed: " + name);
        }
        private static int Count(Delegate value) => value?.GetInvocationList().Length ?? 0;
        public int Subscribers => Count(created) + Count(activated) + Count(destroyed);
        public IEnumerator GetEnumerator() => documents.GetEnumerator();
        public void Create(Document document)
        {
            if (document != null && !documents.Contains(document)) documents.Add(document);
            RaiseCreated(new DocumentCollectionEventArgs { Document = document });
        }
        public void Activate(Document document)
        {
            MdiActiveDocument = document;
            RaiseActivated(new DocumentCollectionEventArgs { Document = document });
        }
        public void Destroy(Document document)
        {
            RaiseDestroyed(new DocumentCollectionEventArgs { Document = document });
            documents.Remove(document);
            if (ReferenceEquals(MdiActiveDocument, document)) MdiActiveDocument = null;
        }
        public void RaiseCreated(DocumentCollectionEventArgs args) => created?.Invoke(this, args);
        public void RaiseActivated(DocumentCollectionEventArgs args) => activated?.Invoke(this, args);
        public void RaiseDestroyed(DocumentCollectionEventArgs args) => destroyed?.Invoke(this, args);
        public void ResetDocuments() { documents.Clear(); MdiActiveDocument = null; ThrowOnSubscribe = null; ActiveDocumentReadError = null; }
    }
    public static class Application
    {
        private static readonly DocumentCollection documents = new DocumentCollection();
        public static Exception DocumentManagerReadError;
        public static DocumentCollection DocumentManager => DocumentManagerReadError == null ? documents : throw DocumentManagerReadError;
        public static NativeMainWindow MainWindow { get; } = new NativeMainWindow();
        public static void ShowModelessWindow(object window) { }
        public static event EventHandler Idle;
        public static int IdleSubscribers => Idle?.GetInvocationList().Length ?? 0;
        public static void RaiseIdle() => Idle?.Invoke(null, EventArgs.Empty);
    }
    public sealed class NativeMainWindow { public IntPtr Handle => IntPtr.Zero; }
}
