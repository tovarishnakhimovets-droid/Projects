using System;
using System.Collections.Generic;
using System.Threading;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public static class ServerState
    {
        private static DocumentRequestTarget _documentTarget = DocumentRequestTarget.FollowActive;

        public static DwgMcpConfig Config { get; set; } = new DwgMcpConfig();

        // This state belongs to one stdio server process, never to the shared plugin.
        public static DocumentRequestTarget DocumentTarget
        {
            get => Volatile.Read(ref _documentTarget);
            internal set => Volatile.Write(ref _documentTarget, value ?? DocumentRequestTarget.FollowActive);
        }

        /// <summary>Tool classes registered for this process. The catalog push reads this list.</summary>
        public static IReadOnlyList<Type> EnabledToolTypes { get; set; } = Array.Empty<Type>();

        public static bool IsReadOnly => Config?.ReadOnlyOrDefault ?? false;

        public static string ReadOnlyError(string toolName)
            => JsonConvert.SerializeObject(new
            {
                ok = false,
                error = $"Tool '{toolName}' is disabled because BIMWRIGHT_DWG_READ_ONLY is enabled."
            });
    }

    /// <summary>An immutable routing snapshot. A null ID follows the user's active DWG.</summary>
    public sealed class DocumentRequestTarget
    {
        public static DocumentRequestTarget FollowActive { get; } = new DocumentRequestTarget(null);

        private DocumentRequestTarget(string documentId) => DocumentId = documentId;

        public string DocumentId { get; }

        public static bool TryCreate(string documentId, out DocumentRequestTarget target, out string error)
        {
            target = null;
            error = null;
            if (documentId == null)
            {
                target = FollowActive;
                return true;
            }

            if (!DocumentRoutingPolicy.TryNormalizeDocumentId(documentId, out var normalized))
            {
                error = "document_id must be a nonzero GUID returned by dwg_list_documents; omit it to follow the active drawing";
                return false;
            }

            target = new DocumentRequestTarget(normalized);
            return true;
        }
    }
}
