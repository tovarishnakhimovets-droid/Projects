using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Drawing;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class LispTools
    {
        [McpServerTool(Name = "dwg_run_lisp", ReadOnly = false, Idempotent = false), Description(
            "Run an exact standalone .lsp file/function approved with MCPTRUSTLISP in the AutoCAD session. " +
            "Approval binds file path, SHA256 bytes and function; changed bytes require new approval. " +
            "Runs captured source, not a disk load: UTF-8/UTF-16 BOM, <=64 KiB; no *load-truename* or relative-load semantics. " +
            "Full-trust AutoLISP, not a sandbox; do not run untrusted dependencies, switch documents or save without permission. " +
            "Returns queued job_id, NOT execution success. Poll dwg_get_lisp_status; the user may need to answer AutoCAD prompts. " +
            "A job blocks other CAD operations. Never repeat an uncertain mutation or timeout automatically. " +
            "No inline code, batch or ToolBaker execution; scanner verdicts never grant approval.")]
        public static Task<string> RunLisp(
            [Description("Fully qualified local .lsp file approved in AutoCAD.")] string file,
            [Description("Approved Lisp function symbol, e.g. c:NETLEN or c:PLLENLEADERS; no expression.")] string command,
            [Description("Exact file SHA256, as shown by MCPTRUSTLISP/dwg_get_lisp_status.")] string expected_sha256,
            [Description("Exact named DWG path from fresh dwg_get_drawing_info.")] string expected_document,
            [Description("Exact DWG fingerprint from fresh dwg_get_drawing_info.")] string expected_fingerprint,
            [Description("JSON argument array (data only), default []; supports strings, finite numbers, booleans, null and lists. Max 100 nodes/depth 8.")] string arguments_json = null,
            [Description("Legacy inline input is always refused.")] string code = null)
        {
            if (ServerState.IsReadOnly) return Task.FromResult(ServerState.ReadOnlyError("dwg_run_lisp"));
            var error = code != null ? "inline LISP is not supported" : LispExecutionPolicy.ValidateCommand(command);
            if (error == null && string.IsNullOrWhiteSpace(file)) error = "file is required";
            if (error == null && !LispExecutionPolicy.IsSha256(expected_sha256)) error = "expected_sha256 must be 64 hexadecimal characters";
            if (error == null && DrawingIdentityPolicy.NormalizeDwgPath(expected_document) == null) error = "expected_document must be a fully qualified named DWG path";
            if (error == null && !Guid.TryParse(expected_fingerprint, out _)) error = "expected_fingerprint must be a DWG GUID";
            if (error != null) return Refuse(error);
            var request = new { file, command, expected_sha256, expected_document, expected_fingerprint, arguments_json };
            return ToolGateway.LoggedCall("run_lisp", request, new { file, command, expected_sha256, expected_document, expected_fingerprint });
        }

        internal static Task<string> Refuse(string error) => Task.FromResult(JsonConvert.SerializeObject(new {
            ok = false, blocked = true, error_code = LispExecutionPolicy.ErrorCode,
            execution_authorized = false, error = error + ". " + LispExecutionPolicy.Refusal + " Use dwg_inspect_lisp for static analysis only."
        }));
    }
}
