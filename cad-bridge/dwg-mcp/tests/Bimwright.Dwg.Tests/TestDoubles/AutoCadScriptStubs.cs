// Test-only host types for compiling the real script handlers. No Autodesk DLLs,
// native calls, document edits, or live MCP connections are used by these doubles.
using System;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public class Document
    {
        private Autodesk.AutoCAD.DatabaseServices.Database database = new Autodesk.AutoCAD.DatabaseServices.Database();
        public Func<Autodesk.AutoCAD.DatabaseServices.Database> DatabaseFactory;
        public Autodesk.AutoCAD.DatabaseServices.Database Database
        {
            get => DatabaseFactory == null ? database : DatabaseFactory();
            set => database = value;
        }
        public Autodesk.AutoCAD.EditorInput.Editor Editor = new Autodesk.AutoCAD.EditorInput.Editor();
        public int NativeGetterReads;
        public int MutationCount;
        public Action<string> OnSendString;
        private bool isReadOnly;
        public Exception IsReadOnlyReadError;
        public bool IsReadOnly
        {
            get => IsReadOnlyReadError == null ? isReadOnly : throw IsReadOnlyReadError;
            set => isReadOnly = value;
        }
        public string ThrowOnCommandSubscribe;
        public Exception LockError;
        public int LockCount;
        private CommandEventHandler ended, cancelled, failed;
        public event CommandEventHandler CommandEnded
        {
            add { Subscribe("ended"); ended += value; }
            remove { ended -= value; }
        }
        public event CommandEventHandler CommandCancelled
        {
            add { Subscribe("cancelled"); cancelled += value; }
            remove { cancelled -= value; }
        }
        public event CommandEventHandler CommandFailed
        {
            add { Subscribe("failed"); failed += value; }
            remove { failed -= value; }
        }
        public int CommandSubscribers => Count(ended) + Count(cancelled) + Count(failed);
        private static int Count(Delegate value) => value?.GetInvocationList().Length ?? 0;
        private void Subscribe(string name)
        {
            if (ThrowOnCommandSubscribe == name) throw new InvalidOperationException("native command subscription failed: " + name);
        }
        public void RaiseCommand(string name)
        {
            var args = new CommandEventArgs();
            if (name == "ended") ended?.Invoke(this, args);
            else if (name == "cancelled") cancelled?.Invoke(this, args);
            else if (name == "failed") failed?.Invoke(this, args);
            else throw new ArgumentException(name);
        }
        public IDisposable LockDocument()
        {
            if (LockError != null) throw LockError;
            LockCount++;
            return new DocumentLock();
        }
        private sealed class DocumentLock : IDisposable { public void Dispose() { } }
        public string LispGuardError;

        public string NativeProperty
        {
            get
            {
                NativeGetterReads++;
                throw new InvalidOperationException("Host getters must not be traversed by JSON serialization.");
            }
        }

        public void SendStringToExecute(string input, bool activate, bool wrapUpInactiveDoc, bool echoCommand)
            => OnSendString(input);
    }
    public class CommandEventArgs : EventArgs { }
    public delegate void CommandEventHandler(object sender, CommandEventArgs args);
}
namespace Autodesk.AutoCAD.DatabaseServices { public partial class Database { } }
namespace Autodesk.AutoCAD.EditorInput
{
    public class Editor
    {
        private bool isQuiescent = true;
        public Exception QuiescenceReadError;
        public bool IsQuiescent
        {
            get => QuiescenceReadError == null ? isQuiescent : throw QuiescenceReadError;
            set => isQuiescent = value;
        }
        public readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
        public Exception WriteMessageError;
        public void WriteMessage(string message)
        {
            if (WriteMessageError != null) throw WriteMessageError;
            Messages.Add(message);
        }
    }
}
namespace Autodesk.AutoCAD.Geometry { public struct Point3d { } }

namespace Bimwright.Dwg.Plugin.Drawing
{
    // Explicit seam for the native identity guard; actual policy has separate tests.
    internal static class DocumentGuard
    {
        internal static string Validate(Autodesk.AutoCAD.ApplicationServices.Document doc, string path, string fingerprint)
            => doc == null ? "no active document" : doc.LispGuardError;
    }
}

namespace Bimwright.Dwg.Plugin
{
    internal static class LispCommands
    {
        internal static void Initialize()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnIdle;
        }
        internal static void Shutdown() => Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
        private static void OnIdle(object sender, EventArgs args) { }
        internal static void Watch(Autodesk.AutoCAD.ApplicationServices.Document document) { }
    }
}
