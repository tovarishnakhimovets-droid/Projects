using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public sealed class LispMcpContractTests
    {
        [Fact]
        public void Sdk_schema_requires_hash_function_and_document_guards()
        {
            var tool = McpServerTool.Create(typeof(LispTools).GetMethod(nameof(LispTools.RunLisp)), (object)null).ProtocolTool;
            Assert.Equal("dwg_run_lisp", tool.Name);
            Assert.False(tool.Annotations.ReadOnlyHint);
            Assert.False(tool.Annotations.IdempotentHint);
            Assert.Equal(new[] { "command", "expected_document", "expected_fingerprint", "expected_sha256", "file" },
                tool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x).ToArray());
            var status = McpServerTool.Create(typeof(QueryTools).GetMethod(nameof(QueryTools.GetLispStatus)), (object)null).ProtocolTool;
            Assert.True(status.Annotations.ReadOnlyHint);
            Assert.True(status.Annotations.IdempotentHint);
        }

        [Fact]
        public async Task Inline_or_invalid_hash_is_refused_without_target_discovery()
        {
            var config = ServerState.Config;
            ServerState.Config = new DwgMcpConfig { Target = "never-connect" };
            try
            {
                var inline = JObject.Parse(await LispTools.RunLisp("not-read.lsp", "c:TEST", new string('A', 64),
                    @"C:\test.dwg", Guid.NewGuid().ToString(), code: "(shell secret)"));
                Assert.False(inline.Value<bool>("ok"));
                Assert.Contains("inline", inline.Value<string>("error"));
                var badHash = JObject.Parse(await LispTools.RunLisp("not-read.lsp", "c:TEST", "bad", @"C:\test.dwg", Guid.NewGuid().ToString()));
                Assert.False(badHash.Value<bool>("ok"));
                Assert.Contains("sha256", badHash.Value<string>("error"));
            }
            finally { ServerState.Config = config; }
        }

        [Fact]
        public async Task Read_only_refusal_never_discovers_a_target()
        {
            var config = ServerState.Config;
            ServerState.Config = new DwgMcpConfig { Target = "never-connect", ReadOnly = true };
            try
            {
                var response = JObject.Parse(await LispTools.RunLisp(@"C:\test.lsp", "c:TEST", new string('A', 64),
                    @"C:\test.dwg", Guid.NewGuid().ToString()));
                Assert.False(response.Value<bool>("ok"));
                Assert.Contains("READ_ONLY", response.Value<string>("error"));
            }
            finally { ServerState.Config = config; }
        }

        [Fact]
        public void Busy_command_line_is_refused_before_file_read_or_queue()
        {
            var doc = new Document { OnSendString = _ => throw new InvalidOperationException("must not queue") };
            doc.Editor.IsQuiescent = false;
            var request = JObject.FromObject(new { file = @"C:\nonexistent.lsp", command = "c:TEST",
                expected_sha256 = new string('A', 64), expected_document = @"C:\test.dwg",
                expected_fingerprint = Guid.NewGuid().ToString() });
            var response = new RunLispHandler().Execute(doc, request);
            Assert.False(response.Ok);
            Assert.Contains("command line is busy", response.Error);
        }

        [Fact]
        public void Actual_handler_queues_only_approved_exact_source_and_never_claims_completion()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lsp");
            File.WriteAllText(path, "(defun c:TEST () 42)");
            LispJobStore.Session.Revoke();
            try
            {
                var source = LispExecutionPolicy.Capture(path);
                var request = JObject.FromObject(new { file = path, command = "c:TEST", expected_sha256 = source.Sha256,
                    expected_document = @"C:\test.dwg", expected_fingerprint = Guid.NewGuid().ToString() });
                var count = 0;
                var doc = new Document { OnSendString = expression => { Assert.Contains("BIMWRIGHT_MCP_LISP_BEGIN", expression); count++; } };
                var handler = new RunLispHandler();
                Assert.False(handler.Execute(doc, request).Ok);
                Assert.Equal(0, count);
                LispJobStore.Session.Approve(source, "c:TEST");
                var accepted = handler.Execute(doc, request);
                Assert.True(accepted.Ok, accepted.Error);
                var status = JObject.FromObject(accepted.Result);
                Assert.Equal("queued", status.Value<string>("status"));
                Assert.False(status.Value<bool>("execution_started"));
                Assert.False(status.Value<bool>("completion_confirmed"));
                Assert.Equal(1, count);
                Assert.False(handler.Execute(doc, request).Ok);
                Assert.Equal(1, count);
            }
            finally { LispJobStore.Session.Revoke(); File.Delete(path); }
        }
    }
}
