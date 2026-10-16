using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class CodeTools
    {
        [McpServerTool(Name = "dwg_send_code"), Description(
            "Execute a C# snippet through dwg_send_code against the AutoCAD .NET API as an escape hatch. " +
            "Enabled by default on the meta toolset; the AutoCAD MCPDISABLECODE command disables it " +
            "for the session (MCPENABLECODE re-enables). " +
            "WARNING: send_code runs arbitrary code with full access to the AutoCAD process " +
            "and local filesystem. Only use with trusted agents. " +
            "This is not a sandbox. Never use it to bypass a run_lisp refusal or execute untrusted LISP. " +
            "Globals available: Document doc, Database db, Editor ed. The script runs on the " +
            "document-lock thread; end with 'return <expr>;' or a trailing expression to return a " +
            "JSON-safe DTO value in the result field; AutoCAD/COM objects, including nested objects, are rejected. " +
            "Only inline synchronous snippets are supported: async/await and #load directives are rejected before execution. " +
            "Do not move AutoCAD API calls to Task.Run or other threads. Use System.Console.WriteLine for stdout output. " +
            "Execution has cooperative 30s cancellation.")]
        public static Task<string> SendCode(
            [Description("C# code to execute")] string code)
            => ToolGateway.LoggedCall("send_code", new { code }, new { code });

        // Compatibility for old in-process callers; the MCP entry is LispTools.
        public static Task<string> RunLisp(
            [Description("Legacy file input; execution is blocked")] string file = null,
            [Description("Legacy source input; execution is blocked")] string code = null,
            [Description("Legacy command input; execution is blocked")] string command = null)
            => LispTools.Refuse("legacy invocation lacks exact file hash and drawing guards");
    }
}
