using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Xrefs;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class XrefQueryTools
    {
        [McpServerTool(Name = "dwg_list_xrefs", ReadOnly = true, Idempotent = true), Description(
            "Read the active drawing's xref graph, including nested/ghost nodes, exact direct insert transforms and clipping metadata. " +
            "Optional bounded histograms describe direct entities by type and layer; they do not recurse into ordinary blocks, so no direct HATCH does not mean no surfaces. " +
            "Returns completeness, errors and expected_xref guards for root operations. Persistent edits invalidate guards; reread after each write. " +
            "Limits: 512 xrefs, 10000 insert metadata records, 100000 direct entities, 2 MiB output, 10-second cooperative budget. " +
            "No geometry dump, file opening/reloading, view change or save. Active drawing only.")]
        public static Task<string> ListXrefs(
            [Description("Optional full absolute DWG path guard, paired with expected_fingerprint.")] string expected_document = null,
            [Description("Optional drawing fingerprint GUID paired with expected_document.")] string expected_fingerprint = null,
            [Description("Maximum xref graph nodes returned, integer 1-512; default 128.")] int max_xrefs = 128,
            [Description("Global direct-entity scan limit 0-100000; zero skips histograms and still reads guarded reference metadata.")] int max_entities = 10000)
        {
            var request = new JObject { ["max_xrefs"] = max_xrefs, ["max_entities"] = max_entities };
            if (expected_document != null) request["expected_document"] = expected_document;
            if (expected_fingerprint != null) request["expected_fingerprint"] = expected_fingerprint;
            if (!XrefListInput.TryParse(request, out _, out var error)) return XrefInputError.Return(error);
            return ToolGateway.LoggedCall("list_xrefs", request, request);
        }
    }

    [McpServerToolType]
    public sealed class XrefWriteTools
    {
        [McpServerTool(Name = "dwg_bind_current_xref", ReadOnly = false, Idempotent = false), Description(
            "Bind exactly one current root xref with preserved symbol identities. " +
            "Requires the complete expected_xref from dwg_list_xrefs and exact DWG identity. Session/revision must match before acquiring the native write lock. " +
            "Under the acquired lock, recheck the full current graph, root identity, insert transforms, clipping, ownership, layers and resolved/loaded status before binding the entire current definition. " +
            "Lock acquisition may run associative evaluation; this contract does not promise unchanged whole-database or associative-network state across that transition. " +
            "dry_run defaults true and checks only metadata under a read lock. One root per call; no detach, explode, reload, source-file edit or save. " +
            "Top-level calls only; batch_execute and baked tools do not support this operation. " +
            "Returns actual native/readback outcome; complete rollback and grouped Undo are not promised. Native calls cannot be interrupted by the cooperative deadline. " +
            "After a timeout or uncertain result, inspect current state and never automatically retry.")]
        public static Task<string> BindCurrentXref(
            [Description("Exact full saved DWG path from dwg_list_xrefs.")] string expected_document,
            [Description("Exact drawing fingerprint GUID from dwg_list_xrefs.")] string expected_fingerprint,
            [Description("Complete expected_xref for one root from the latest reader; its session/revision is strict before lock acquisition, and all root/graph/reference metadata is rechecked under the lock.")] XrefTargetExpectation expected_xref,
            [Description("Read and validate only; default true. Set false deliberately to bind one current root definition.")] bool dry_run = true)
        {
            var request = new JObject { ["expected_document"] = expected_document, ["expected_fingerprint"] = expected_fingerprint,
                ["expected_xref"] = expected_xref == null ? JValue.CreateNull() : JObject.FromObject(expected_xref), ["dry_run"] = dry_run };
            if (!XrefBindCurrentInput.TryParse(request, out _, out var error)) return XrefInputError.Return(error);
            return ToolGateway.LoggedCall("bind_current_xref", request, request);
        }

        [McpServerTool(Name = "dwg_manage_xrefs", ReadOnly = false, Idempotent = false), Description(
            "Bind or detach exactly one explicit root xref after matching its full reader guard, current document path/fingerprint and revision. " +
            "Every insert must be directly owned by a local model/paper layout on an unlocked layer. Bind conservatively requires every graph node resolved/loaded and preserves xref-qualified symbol identities; no insert-style name merging. " +
            "dry_run defaults true and performs only preflight reads. Native bind/detach affects the whole selected definition and its dependent symbols. " +
            "Returns actual native/readback outcome; failures keep bounded JSON diagnostics in the error field. Does not promise complete transaction rollback or grouped Undo. No reload, source-file edit or save. " +
            "Native calls cannot be interrupted by the cooperative deadline. After a timeout or uncertain result, inspect current state and never automatically retry.")]
        public static Task<string> ManageXrefs(
            [Description("Exact full saved DWG path from dwg_list_xrefs.")] string expected_document,
            [Description("Exact drawing fingerprint GUID from dwg_list_xrefs.")] string expected_fingerprint,
            [Description("bind preserves symbol identities; detach removes the selected root definition and all its inserts.")] string action,
            [Description("Complete expected_xref object copied from one root row in the latest dwg_list_xrefs response.")] XrefTargetExpectation expected_xref,
            [Description("Read and validate only; default true. Set false deliberately for one native operation.")] bool dry_run = true)
        {
            var request = new JObject { ["expected_document"] = expected_document, ["expected_fingerprint"] = expected_fingerprint,
                ["action"] = action, ["expected_xref"] = expected_xref == null ? JValue.CreateNull() : JObject.FromObject(expected_xref), ["dry_run"] = dry_run };
            if (!XrefManageInput.TryParse(request, out _, out var error)) return XrefInputError.Return(error);
            return ToolGateway.LoggedCall("manage_xrefs", request, request);
        }
    }

    public sealed class XrefTargetExpectation
    {
        [System.Text.Json.Serialization.JsonRequired, Description("Canonical positive hexadecimal root BTR handle from the reader.")]
        public string handle { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact xref definition name.")]
        public string name { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact stored source path, including empty if reported.")]
        public string path { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact reader xref status.")]
        public string status { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact reader overlay flag.")]
        public bool is_overlay { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Must be false; nested definitions are not mutated directly.")]
        public bool is_nested { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Reader SHA256 of complete graph metadata and reference snapshots.")]
        public string graph_fingerprint { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Reader SHA256 of this definition's sorted insert transforms, ownership, layers and clipping.")]
        public string references_fingerprint { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Reader document lifetime/revision session identifier.")]
        public string document_session { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Exact nonnegative document revision from the reader.")]
        public long document_revision { get; set; }
        [System.Text.Json.Serialization.JsonRequired, Description("Must be true; partial inventories cannot authorize changes.")]
        public bool guard_complete { get; set; }
    }

    internal static class XrefInputError
    {
        internal static Task<string> Return(string error) => Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
    }
}
