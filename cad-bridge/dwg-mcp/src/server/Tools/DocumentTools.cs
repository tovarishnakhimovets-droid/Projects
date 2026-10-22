#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DocumentTools
    {
        private static readonly SemaphoreSlim SelectionGate = new SemaphoreSlim(1, 1);

        [McpServerTool(Name = "dwg_list_documents", ReadOnly = true, Idempotent = true), Description(
            "List the DWGs already open in this AutoCAD process, including opaque document_id, path, " +
            "fingerprint, active/read-only flags and supported inactive read commands. Does not open, " +
            "activate, close, save or change any document. Works even when a previous selection is stale.")]
        public static Task<string> ListDocuments()
            => ToolGateway.LoggedCall("list_documents", new { }, new { }, DocumentRequestTarget.FollowActive);

        [McpServerTool(Name = "dwg_use_document", ReadOnly = true, Idempotent = true), Description(
            "Choose the open DWG used by subsequent tools in this MCP server session without changing " +
            "the user's active AutoCAD tab, selection or view. Use a fresh document_id from dwg_list_documents; " +
            "omit it or pass null to follow the active tab again, including when no DWGs are open. " +
            "Only documented database read commands support an inactive target; other commands refuse. " +
            "Closed/reopened documents and plugin restarts invalidate IDs; there is no fallback to another DWG.")]
        public static async Task<string> UseDocument(
            [Description("Opaque document_id from dwg_list_documents, or null/omission to follow the active DWG.")] string? document_id = null)
        {
            await SelectionGate.WaitAsync();
            try
            {
                return await UseDocumentCore(document_id,
                    target => ToolGateway.LoggedCall("use_document", new { document_id }, new { }, target),
                    target => ServerState.DocumentTarget = target);
            }
            finally
            {
                SelectionGate.Release();
            }
        }

        // Native resolution must succeed before a new selection becomes visible to other calls.
        internal static async Task<string> UseDocumentCore(string? documentId,
            Func<DocumentRequestTarget, Task<string>> send, Action<DocumentRequestTarget> select)
        {
            if (!DocumentRequestTarget.TryCreate(documentId, out var target, out var error))
                return Failure(error);

            var json = await send(target);
            McpResponse? response;
            try { response = JsonConvert.DeserializeObject<McpResponse>(json); }
            catch (JsonException) { return Failure("invalid response while selecting the drawing"); }
            if (response == null) return Failure("empty response while selecting the drawing");
            if (!response.Ok) return json;

            if (target.DocumentId != null)
            {
                var descriptor = response.Result as JObject;
                if (descriptor?["document_id"]?.Type != JTokenType.String ||
                    !DocumentRequestTarget.TryCreate(descriptor.Value<string>("document_id"), out var resolved, out _) ||
                    resolved.DocumentId != target.DocumentId)
                    return Failure("plugin did not confirm the requested document_id");
            }

            select(target);
            return json;
        }

        private static string Failure(string error)
            => JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = error });
    }
}
