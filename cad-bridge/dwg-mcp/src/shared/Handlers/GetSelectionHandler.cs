using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class GetSelectionHandler : IAcadCommand
    {
        public string Name => "get_selection";
        public string Description => "Read the current pickfirst selection without changing it or prompting.";
        public CommandSchema Schema => CommandSchemas.GetSelection;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!SelectionReadInput.TryParse(parameters, out var input, out var error))
                return CommandResult.Fail(error);

            // CommandDispatcher invokes this handler on AutoCAD's main thread.
            try
            {
                var identity = DocumentGuard.Capture(doc);
                var pickfirst = Convert.ToInt32(Application.GetSystemVariable("PICKFIRST"), CultureInfo.InvariantCulture);
                var rows = new List<object>();
                var handles = new List<string>();
                if (pickfirst == 0)
                    return Snapshot(identity, "disabled", null, pickfirst,
                        new SelectionReadSummary(0, input.MaxEntities), handles, rows);

                var prompt = doc.Editor.SelectImplied();
                using (var selection = prompt.Value)
                {
                    var nativeStatus = prompt.Status.ToString();
                    // SelectImplied normally returns Error when no pickfirst set exists.
                    // Other non-OK statuses are failures, not evidence of an empty selection.
                    if (prompt.Status != PromptStatus.OK && prompt.Status != PromptStatus.Error)
                        return CommandResult.Fail("current selection is unavailable: " + nativeStatus);
                    if (prompt.Status != PromptStatus.OK || selection == null || selection.Count == 0)
                        return Snapshot(identity, "empty", nativeStatus, pickfirst,
                            new SelectionReadSummary(0, input.MaxEntities), handles, rows);

                    var summary = new SelectionReadSummary(selection.Count, input.MaxEntities);
                    using (var tx = doc.Database.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (SelectedObject selected in selection)
                        {
                            string handle = null, entityType = null, itemError = null;
                            IDictionary<string, object> description = null;
                            try
                            {
                                if (selected == null || selected.ObjectId.IsNull)
                                    throw new InvalidOperationException("selected object is unavailable");

                                var id = selected.ObjectId;
                                handle = id.Handle.ToString();
                                var obj = tx.GetObject(id, OpenMode.ForRead);
                                if (!(obj is Entity entity))
                                    throw new InvalidOperationException("selected object is not an entity");

                                entityType = entity.GetType().Name;
                                description = (IDictionary<string, object>)CadEntityProperties.Describe(entity, tx, input.IncludeGeometry);
                                entityType = description["type"] as string ?? entityType;
                                itemError = AddRuntimeClass(description, entity);
                            }
                            catch (Exception ex)
                            {
                                itemError = ex.Message;
                            }

                            // Retain failures as rows; never replace an unreadable selection
                            // with unrelated entities or silently remove it from the count.
                            var ok = itemError == null;
                            handles.Add(handle);
                            rows.Add(new { handle, ok, entity = description, error = itemError });
                            summary.Add(entityType, ok);
                            if (summary.ReturnedCount == summary.ReturnLimit) break;
                        }
                    }

                    return Snapshot(identity, "ok", nativeStatus, pickfirst, summary, handles, rows);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to read current selection: " + ex.Message);
            }
        }

        private static string AddRuntimeClass(IDictionary<string, object> description, Entity entity)
        {
            description["dxf_name"] = null;
            description["class_name"] = null;
            description["metadata_error"] = null;
            try
            {
                var runtimeClass = entity.GetRXClass();
                description["dxf_name"] = runtimeClass.DxfName;
                description["class_name"] = runtimeClass.Name;
                return null;
            }
            catch (Exception ex)
            {
                // Optional native class metadata must not hide the ordinary properties.
                var error = "failed to read runtime class metadata: " + ex.Message;
                description["metadata_error"] = error;
                return error;
            }
        }

        private static CommandResult Snapshot(DrawingDocumentSnapshot identity, string status,
            string nativeStatus, int pickfirst, SelectionReadSummary summary, List<string> handles, List<object> rows)
        {
            return CommandResult.Success(new
            {
                document_name = identity.DocumentName,
                document_path = identity.DocumentPath,
                fingerprint = identity.Fingerprint,
                status,
                native_prompt_status = nativeStatus,
                pickfirst,
                selected_count = summary.SelectedCount,
                returned_count = summary.ReturnedCount,
                handles,
                entities = rows,
                complete = status != "disabled" && summary.Complete,
                truncated = summary.Truncated,
                errors = summary.Errors,
                type_counts = new { scope = "returned", counts = summary.TypeCounts }
            });
        }
    }
}
