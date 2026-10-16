using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Bimwright.Dwg.Plugin.Drawing;

[assembly: CommandClass(typeof(Bimwright.Dwg.Plugin.LispCommands))]

namespace Bimwright.Dwg.Plugin
{
    public sealed class LispCommands
    {
        private static Document watchedDocument;
        internal static void Initialize() { Application.Idle -= OnIdle; Application.Idle += OnIdle; }
        internal static void Shutdown() { Application.Idle -= OnIdle; Watch(null); LispJobStore.Session.Revoke(); }

        internal static void Watch(Document doc)
        {
            if (ReferenceEquals(watchedDocument, doc)) return;
            if (watchedDocument != null)
            {
                watchedDocument.LispWillStart -= OnLispStart;
                watchedDocument.LispEnded -= OnLispEnd;
                watchedDocument.LispCancelled -= OnLispCancel;
                Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentDestroyed;
            }
            watchedDocument = doc;
            if (doc != null)
            {
                doc.LispWillStart += OnLispStart;
                doc.LispEnded += OnLispEnd;
                doc.LispCancelled += OnLispCancel;
                Application.DocumentManager.DocumentToBeDestroyed += OnDocumentDestroyed;
            }
        }

        private static void OnLispStart(object sender, EventArgs args) => LispJobStore.Session.NativeStart(watchedDocument);
        private static void OnLispEnd(object sender, EventArgs args) => LispJobStore.Session.NativeEnd(watchedDocument, false);
        private static void OnLispCancel(object sender, EventArgs args) => LispJobStore.Session.NativeEnd(watchedDocument, true);
        private static void OnDocumentDestroyed(object sender, DocumentCollectionEventArgs args)
        {
            var job = LispJobStore.Session.Current;
            if (job != null && ReferenceEquals(job.Document, args.Document))
                LispJobStore.Session.Fail(job, "LISP document was closed; completion is unknown and changes may remain");
            if (ReferenceEquals(watchedDocument, args.Document)) Watch(null);
        }

        [CommandMethod("MCPTRUSTLISP", CommandFlags.Modal)]
        public static void Trust()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                if (LispJobStore.Session.IsBusy) throw new InvalidOperationException("finish/cancel the active LISP first");
                var selected = doc.Editor.GetFileNameForOpen(new PromptOpenFileOptions("\nSelect standalone UTF-8 .lsp to trust for this session:") {
                    Filter = "AutoLISP (*.lsp)|*.lsp", DialogCaption = "MCP: trust a LISP file" });
                if (selected.Status != PromptStatus.OK) return;
                var source = LispExecutionPolicy.Capture(selected.StringResult);
                var entry = doc.Editor.GetString(new PromptStringOptions("\nLISP function (e.g. c:NETLEN): ") { AllowSpaces = false });
                if (entry.Status != PromptStatus.OK) return;
                var error = LispExecutionPolicy.ValidateCommand(entry.StringResult);
                if (error != null) throw new ArgumentException(error);
                doc.Editor.WriteMessage("\nFile: " + source.File + "\nSHA256: " + source.Sha256 + "\nFunction: " + entry.StringResult
                    + "\nTrusted LISP has full AutoCAD/filesystem access. This is not a sandbox. Changed bytes need new approval.");
                var options = new PromptKeywordOptions("\nAllow this file/function in this AutoCAD session? [Yes/No] <No>: ");
                options.Keywords.Add("Yes"); options.Keywords.Add("No"); options.Keywords.Default = "No"; options.AllowNone = true;
                var answer = doc.Editor.GetKeywords(options);
                if (answer.Status == PromptStatus.OK && answer.StringResult == "Yes")
                {
                    LispJobStore.Session.Approve(source, entry.StringResult);
                    doc.Editor.WriteMessage("\n[Bimwright.Dwg] LISP file/function approved for this session.\n");
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n[Bimwright.Dwg] " + ex.Message + "\n"); }
        }

        [CommandMethod("MCPDISABLELISP", CommandFlags.Modal)]
        public static void Disable()
        {
            LispJobStore.Session.Revoke();
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[Bimwright.Dwg] LISP approvals cleared; pending jobs revoked. Running code must be cancelled manually.\n");
        }

        [LispFunction("BIMWRIGHT_MCP_LISP_BEGIN")]
        public static int Begin(ResultBuffer arguments)
        {
            var values = arguments?.AsArray();
            if (values == null || values.Length != 1 || !(values[0].Value is string nonce)) return 0;
            var job = LispJobStore.Session.Pending(nonce);
            if (job == null) return 0;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                var error = !ReferenceEquals(doc, job.Document) ? "active AutoCAD document changed before LISP execution"
                    : DocumentGuard.Validate(doc, job.ExpectedDocument, job.ExpectedFingerprint);
                if (error != null) throw new InvalidOperationException(error);
                if (doc.IsReadOnly) throw new InvalidOperationException("active AutoCAD document is read-only");
                if (Convert.ToInt32(Application.GetSystemVariable("LISPSYS")) == 0)
                    throw new InvalidOperationException("LISPSYS must be 1 or 2 for Unicode LISP; change it and restart AutoCAD");
                return LispJobStore.Session.Begin(job) ? 1 : 0;
            }
            catch (System.Exception ex) { LispJobStore.Session.Fail(job, ex.Message); return 0; }
        }

        [LispFunction("BIMWRIGHT_MCP_LISP_RESULT")]
        public static int Result(ResultBuffer arguments)
        {
            var values = arguments?.AsArray();
            if (values == null || values.Length != 3 || !(values[0].Value is string nonce)
                || !(values[2].Value is string result)) return 0;
            var job = LispJobStore.Session.Running;
            if (job == null || job.Nonce != nonce) return 0;
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, job.Document))
                LispJobStore.Session.Record(nonce, false, "LISP changed the active document; verify changes manually");
            else LispJobStore.Session.Record(nonce, Convert.ToInt32(values[1].Value) == 1, result);
            return 1;
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            var job = LispJobStore.Session.Running;
            if (job == null)
            {
                if (!LispJobStore.Session.IsBusy) Watch(null);
                return;
            }
            try
            {
                // CMDACTIVE bit32 is not visible reliably from Idle. A native
                // LispEnded/Cancelled event for the owned expression is required.
                if (job.NativeEnded && job.LispDepth == 0)
                {
                    LispJobStore.Session.Finish(job);
                    Watch(null);
                    App.ToastNotifier?.OnCompleted("run_lisp", null, job.State == "completed",
                        job.State == "completed" ? null : McpResponsePrivacy.SanitizeError(job.Error), "Run LISP");
                }
            }
            catch (System.Exception ex) { LispJobStore.Session.Fail(job, "cannot confirm LISP completion: " + ex.Message); }
        }
    }
}
