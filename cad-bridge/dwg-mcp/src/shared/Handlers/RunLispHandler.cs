using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Queue exact session-approved source; native callbacks guard and report execution.
    /// </summary>
    public class RunLispHandler : IAcadCommand
    {
        public string Name => "run_lisp";
        public string Description => "Run an explicitly approved standalone LISP file/function; returns a tracked job.";
        public CommandSchema Schema => CommandSchemas.RunLisp;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            LispJob job = null;
            try
            {
                var validation = SchemaValidator.Validate(Name, parameters, Schema);
                if (!validation.Ok) return CommandResult.Fail(validation.Error);
                if (parameters["code"] != null) return CommandResult.Fail("inline LISP is not supported; use an approved .lsp file");
                var file = parameters["file"].Value<string>();
                var command = parameters["command"].Value<string>();
                var error = LispExecutionPolicy.ValidateCommand(command);
                if (error != null) return CommandResult.Fail(error);
                var expectedDocument = parameters["expected_document"].Value<string>();
                var expectedFingerprint = parameters["expected_fingerprint"].Value<string>();
                error = DocumentGuard.Validate(doc, expectedDocument, expectedFingerprint);
                if (error != null) return CommandResult.Fail(error);
                if (doc.IsReadOnly) return CommandResult.Fail("active AutoCAD document is read-only");
                if (!doc.Editor.IsQuiescent) return CommandResult.Fail("AutoCAD command line is busy; finish or cancel the current command before running LISP");
                var source = LispExecutionPolicy.Capture(file);
                var sha = parameters["expected_sha256"].Value<string>();
                if (!LispExecutionPolicy.IsSha256(sha) || !string.Equals(source.Sha256, sha, System.StringComparison.OrdinalIgnoreCase))
                    return CommandResult.Fail("LISP file SHA256 does not match expected_sha256");
                job = LispJobStore.Session.Queue(source, command, parameters["arguments_json"]?.Value<string>(),
                    expectedDocument, expectedFingerprint, doc);
                LispCommands.Watch(doc);
                doc.SendStringToExecute(job.Expression, true, false, false);
                return CommandResult.Success(LispJobStore.Session.Snapshot(job.Id));
            }
            catch (System.Exception ex)
            {
                if (job != null) LispJobStore.Session.Fail(job, "could not queue LISP: " + ex.Message);
                return CommandResult.Fail(ex.Message);
            }
        }
    }

    public sealed class GetLispStatusHandler : IAcadCommand
    {
        public string Name => "get_lisp_status";
        public string Description => "Read LISP approvals and actual job state without waiting for AutoCAD input.";
        public CommandSchema Schema => CommandSchemas.GetLispStatus;
        public CommandResult Execute(Document doc, JToken parameters)
        {
            try { return CommandResult.Success(LispJobStore.Session.Snapshot(parameters?["job_id"]?.Value<string>())); }
            catch (System.Exception ex) { return CommandResult.Fail(ex.Message); }
        }
    }
}
