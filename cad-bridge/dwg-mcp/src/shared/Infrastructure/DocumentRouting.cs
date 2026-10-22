using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Called only through DocumentInvoker on AutoCAD's main thread. There is no
    // global chosen document: each authenticated request carries its own ID.
    internal static class DocumentRouting
    {
        private static OpenDocumentIdentityMap<Document> identities = new OpenDocumentIdentityMap<Document>();

        internal static void BeginSession()
            => identities = new OpenDocumentIdentityMap<Document>();

        internal static Document Resolve(string documentId, bool allowMissingActive = false)
        {
            if (documentId == null)
            {
                var active = Application.DocumentManager.MdiActiveDocument;
                if (active == null && !allowMissingActive) throw new InvalidOperationException("no active document");
                return active;
            }
            var document = identities.Find(documentId, OpenDocuments());
            if (document == null)
                throw new InvalidOperationException("selected document is no longer open in this AutoCAD session; " +
                    "list documents and choose again; the active drawing was not substituted");
            return document;
        }

        internal static object ListDocuments()
        {
            var active = Application.DocumentManager.MdiActiveDocument;
            var documents = new List<object>();
            foreach (var document in OpenDocuments()) documents.Add(Describe(document));
            return new { documents, active_document_id = active == null ? null : identities.GetId(active) };
        }

        internal static object Describe(Document document)
        {
            if (document == null) return null;
            var identity = DocumentGuard.Capture(document);
            return new
            {
                document_id = identities.GetId(document),
                document_name = FileName(identity.DocumentName),
                document_path = identity.DocumentPath,
                fingerprint = identity.Fingerprint,
                is_active = ReferenceEquals(Application.DocumentManager.MdiActiveDocument, document),
                is_named = identity.IsTitled,
                is_read_only = identity.IsReadOnlyDocument,
                inactive_read_commands = DocumentRoutingPolicy.InactiveReadCommands
            };
        }

        private static IEnumerable<Document> OpenDocuments()
        {
            foreach (Document document in Application.DocumentManager)
                if (document != null) yield return document;
        }

        private static string FileName(string name)
        {
            try { return string.IsNullOrWhiteSpace(name) ? name : Path.GetFileName(name); }
            catch (ArgumentException) { return name; }
        }

        private static string ValidateEmptyParameters(JToken parameters)
        {
            if (parameters == null || parameters.Type == JTokenType.Null) return null;
            if (!(parameters is JObject obj) || obj.HasValues) return "document control params must be an empty object";
            return null;
        }

        internal sealed class ListCommand : IAcadCommand
        {
            public string Name => "list_documents";
            public string Description => "List open documents without activating a drawing.";
            public CommandSchema Schema => CommandSchema.Object();
            public CommandResult Execute(Document document, JToken parameters)
            {
                var error = ValidateEmptyParameters(parameters);
                return error == null ? CommandResult.Success(ListDocuments()) : CommandResult.Fail(error);
            }
        }

        internal sealed class UseCommand : IAcadCommand
        {
            public string Name => "use_document";
            public string Description => "Validate a request document target without changing the active tab.";
            public CommandSchema Schema => CommandSchema.Object();
            public CommandResult Execute(Document document, JToken parameters)
            {
                var error = ValidateEmptyParameters(parameters);
                return error == null ? CommandResult.Success(Describe(document)) : CommandResult.Fail(error);
            }
        }
    }
}
