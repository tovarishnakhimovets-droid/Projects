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
        public bool IsReadOnly;
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
}
namespace Autodesk.AutoCAD.DatabaseServices { public partial class Database { } }
namespace Autodesk.AutoCAD.EditorInput { public class Editor { public bool IsQuiescent = true; } }
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
        internal static void Watch(Autodesk.AutoCAD.ApplicationServices.Document document) { }
    }
}
