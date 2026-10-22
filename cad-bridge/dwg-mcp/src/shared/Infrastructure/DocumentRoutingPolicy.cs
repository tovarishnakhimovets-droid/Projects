using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Shared wire validation and capability policy; contains no Autodesk types.
    internal static class DocumentRoutingPolicy
    {
        internal static readonly IReadOnlyList<string> InactiveReadCommands = Array.AsReadOnly(new[]
        {
            "get_drawing_info", "get_entity_properties", "query_entities", "count_entities",
            "list_layers", "list_text_styles", "select_by_layer", "select_by_type"
        });

        internal static bool TryParseDocumentId(JToken token, out string documentId, out string error)
        {
            documentId = null;
            error = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String || !TryNormalizeDocumentId(token.Value<string>(), out documentId))
            {
                error = "document_id must be a nonzero GUID in N or D format, or null to follow the active drawing";
                return false;
            }
            return true;
        }

        internal static bool TryNormalizeDocumentId(string value, out string documentId)
        {
            documentId = null;
            if (value == null || (value.Length != 32 && value.Length != 36)) return false;
            if (!Guid.TryParseExact(value, "N", out var id) && !Guid.TryParseExact(value, "D", out id)) return false;
            if (id == Guid.Empty) return false;
            documentId = id.ToString("N");
            return true;
        }

        internal static bool IsInactiveReadCommand(string command)
        {
            foreach (var allowed in InactiveReadCommands)
                if (string.Equals(command, allowed, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool UsesReadLock(string command)
            => IsInactiveReadCommand(command) || command == "get_selection" || command == "get_selected_texts";

        internal static string ValidateCommandTarget(string command, bool isActive)
            => isActive || IsInactiveReadCommand(command) || command == "use_document"
                ? null
                : "command '" + command + "' requires the selected drawing to be the active AutoCAD tab; " +
                  "the bridge did not switch tabs or fall back to another drawing";
    }

    // IDs belong to open object instances, not filenames or copied DWG fingerprints.
    // Weak keys cannot keep closed native documents alive.
    internal sealed class OpenDocumentIdentityMap<T> where T : class
    {
        private sealed class Identity { internal readonly string Id = Guid.NewGuid().ToString("N"); }
        private readonly ConditionalWeakTable<T, Identity> identities = new ConditionalWeakTable<T, Identity>();

        internal string GetId(T document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return identities.GetValue(document, _ => new Identity()).Id;
        }

        internal T Find(string documentId, IEnumerable<T> openDocuments)
        {
            if (!DocumentRoutingPolicy.TryNormalizeDocumentId(documentId, out var normalized)) return null;
            if (openDocuments == null) throw new ArgumentNullException(nameof(openDocuments));
            foreach (var document in openDocuments)
                if (document != null && string.Equals(GetId(document), normalized, StringComparison.Ordinal)) return document;
            return null;
        }
    }
}
