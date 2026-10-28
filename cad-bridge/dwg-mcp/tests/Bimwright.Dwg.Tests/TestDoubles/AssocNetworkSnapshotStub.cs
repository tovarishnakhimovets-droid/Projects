using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    // Native filing itself is compiled against both Autodesk SDKs. These doubles
    // inject independent snapshot outcomes to exercise the real invoker gate.
    internal sealed class AssocNetworkSnapshot
    {
        internal static Action<Document> OnCapture;
        internal static Func<Document, IEnumerable<string>, string> OnMatches;
        internal int NetworkCount => 1;
        internal long TotalBytes => 128;
        internal static AssocNetworkSnapshot Capture(Document doc)
        { OnCapture?.Invoke(doc); return new AssocNetworkSnapshot(); }
        internal bool Matches(Document doc, IEnumerable<string> handles, out string error)
        { error = OnMatches?.Invoke(doc, handles); return error == null; }
        internal static void Reset() { OnCapture = null; OnMatches = null; }
    }
}
