// Modified by the CAD team (2026-10-07); see patches/ and docs/IMPORT_MANIFEST.json in cad-bridge.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.ToolCatalog;

namespace Bimwright.Dwg.Plugin
{
    public class CommandDispatcher
    {
        private readonly Dictionary<string, IAcadCommand> _commands;
        private readonly string _authToken;

        public static bool SendCodeEnabled { get; private set; }

        public static void SetSendCodeEnabled(bool enabled)
        {
            SendCodeEnabled = enabled;
        }

        public CommandDispatcher(string authToken)
        {
            _authToken = authToken;
            DocumentRouting.BeginSession();
            _commands = new Dictionary<string, IAcadCommand>
            {
                { "list_documents",          new DocumentRouting.ListCommand() },
                { "use_document",            new DocumentRouting.UseCommand() },
                { "get_drawing_info",        new GetDrawingInfoHandler() },
                { "get_crosshair_settings",  new GetCrosshairSettingsHandler() },
                { "set_crosshair_settings",  new SetCrosshairSettingsHandler() },
                { "get_settings",           new GetSettingsHandler() },
                { "set_settings",           new SetSettingsHandler() },
                { "inventory_proxies",       new InventoryProxiesHandler() },
                { "erase_proxy_objects",     new EraseProxyObjectsHandler() },
                { "node_network",            new NodeNetworkHandler() },
                { "move_network_endpoint",   new MoveNetworkEndpointHandler() },
                { "get_entity_properties",   new GetEntityPropertiesHandler() },
                { "inspect_block",           new InspectBlockHandler() },
                { "create_block_variant",    new CreateBlockVariantHandler() },
                { "update_block_angles",     new UpdateBlockAnglesHandler() },
                { "get_selected_texts",      new GetSelectedTextsHandler() },
                { "get_selection",           new GetSelectionHandler() },
                { "list_layers",             new ListLayersHandler() },
                { "list_text_styles",        new ListTextStylesHandler() },
                { "query_entities",          new QueryEntitiesHandler() },
                { "count_entities",          new CountEntitiesHandler() },
                { "select_by_layer",         new SelectByLayerHandler() },
                { "select_by_type",          new SelectByTypeHandler() },
                { "create_layer",            new CreateLayerHandler() },
                { "create_line",             new CreateLineHandler() },
                { "create_circle",           new CreateCircleHandler() },
                { "create_point",            new CreatePointHandler() },
                { "create_polyline",         new CreatePolylineHandler() },
                { "create_entities",         new CreateEntitiesHandler() },
                { "edit_polyline_vertices",  new EditPolylineVerticesHandler() },
                { "create_rectangle",        new CreateRectangleHandler() },
                { "create_arc",              new CreateArcHandler() },
                { "create_ellipse",          new CreateEllipseHandler() },
                { "create_text",             new CreateTextHandler() },
                { "create_mtext",            new CreateMTextHandler() },
                { "create_leader",           new CreateLeaderHandler() },
                { "create_table",            new CreateTableHandler() },
                { "read_table",              new ReadTableHandler() },
                { "list_blocks",             new ListBlocksHandler() },
                { "get_block_attributes",    new GetBlockAttributesHandler() },
                { "insert_block",            new InsertBlockHandler() },
                { "set_block_attributes",    new SetBlockAttributesHandler() },
                { "explode_block",           new ExplodeBlockHandler() },
                { "create_linear_dimension", new CreateLinearDimensionHandler() },
                { "create_aligned_dimension", new CreateAlignedDimensionHandler() },
                { "create_radial_dimension", new CreateRadialDimensionHandler() },
                { "create_diameter_dimension", new CreateDiameterDimensionHandler() },
                { "change_layer",            new ChangeLayerHandler() },
                { "change_color",            new ChangeColorHandler() },
                { "set_lineweight",          new SetLineweightHandler() },
                { "set_text_style",         new SetTextStyleHandler() },
                { "match_properties",       new MatchPropertiesHandler() },
                { "set_layer_lineweight",    new SetLayerLineweightHandler() },
                { "create_hatch",            new CreateHatchHandler() },
                { "move_entities",           new MoveEntitiesHandler() },
                { "rotate_entities",         new RotateEntitiesHandler() },
                { "scale_entities",          new ScaleEntitiesHandler() },
                { "copy_entities",           new CopyEntitiesHandler() },
                { "erase_entities",          new EraseEntitiesHandler() },
                { "offset_entities",         new OffsetEntitiesHandler() },
                { "update_texts",            new UpdateTextsHandler() },
                { "send_code",               new SendCodeHandler() },
                { "run_lisp",                new RunLispHandler() },
                { "get_lisp_status",         new GetLispStatusHandler() },
                { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
                { "list_baked_tools",        new ListBakedToolsHandler() },
                { "zoom_extents",            new ZoomExtentsHandler() },
                { "zoom_window",             new ZoomWindowHandler() },
                { "zoom_to_entity",          new ZoomToEntityHandler() },
                { "capture_view_image",      new CaptureViewImageHandler() },
                { "inspect_view_region",     new InspectViewRegionHandler() },
                { "restore_view",            new RestoreViewHandler() },
                { "export_dxf",              new ExportDxfHandler() },
                { "get_variables",           new GetVariablesHandler() },
                { "set_system_variable",     new SetSystemVariableHandler() },
                { "save_drawing",            new SaveDrawingHandler() },
                { "purge_drawing",           new PurgeDrawingHandler() },
            };
            _commands.Add("apply_bake", new ApplyBakeSuggestionHandler((cmd, parameters) => ValidateCommand(cmd, parameters, out _)));
            _commands.Add("batch_execute", new BatchExecuteHandler(ExecuteCommand));
            _commands.Add("run_baked_tool", new RunBakedToolHandler(ExecuteCommand));
        }

        public string Dispatch(string requestLine)
        {
            string id = null;
            string cmd = null;
            JToken parameters = null;
            try
            {
                var request = JObject.Parse(requestLine);
                id = (string)request["id"];

                var auth = (string)request["auth"];
                if (!string.Equals(auth, _authToken, StringComparison.Ordinal))
                    return ErrorJson(id, "unauthorized");

                cmd = (string)request["cmd"];
                parameters = request["params"];

                if (string.Equals(cmd, "set_tool_catalog", StringComparison.Ordinal))
                {
                    var catalogError = ToolCatalogStore.AcceptJson(parameters);
                    if (catalogError != null)
                        return ErrorJson(id, catalogError);
                    return SerializeResponse(id, cmd, CommandResult.Success(new { accepted = ToolCatalogStore.Snapshot().Length }));
                }

                var preflight = ValidateCommand(cmd, parameters, out var preflightHandler);
                if (!preflight.Ok)
                {
                    NotifyCompleted(cmd, null, false, preflight.Error, preflightHandler?.Description);
                    return SerializeResponse(id, cmd, preflight);
                }

                string documentId = null;
                if (cmd != "list_documents" && cmd != "get_lisp_status" &&
                    !DocumentRoutingPolicy.TryParseDocumentId(request["document_id"], out documentId, out var targetError))
                    return ErrorJson(id, targetError);

                CommandResult result;
                if (cmd == "get_lisp_status") result = ExecuteCommand(null, cmd, parameters);
                else if (cmd == "list_documents")
                    result = DocumentInvoker.InvokeApplication(() => ExecuteCommand(null, cmd, parameters));
                else if (cmd == "use_document")
                    result = DocumentInvoker.InvokeApplication(() => ExecuteCommand(
                        DocumentRouting.Resolve(documentId, allowMissingActive: true), cmd, parameters));
                else result = DocumentInvoker.Invoke(cmd, documentId, doc => ExecuteCommand(doc, cmd, parameters));

                string resultJson = null;
                if (result.Ok)
                {
                    try
                    {
                        var filtered = McpResponsePrivacy.FilterResult(cmd, result.Result);
                        if (filtered != null)
                            resultJson = JsonConvert.SerializeObject(filtered);
                    }
                    catch { }
                }

                // Queue acceptance is not execution completion.
                if (!(cmd == "run_lisp" && result.Ok))
                    NotifyCompleted(cmd, resultJson, result.Ok, result.Error, preflightHandler?.Description);
                return SerializeResponse(id, cmd, result);
            }
            catch (Exception ex)
            {
                NotifyCompleted(cmd, null, false, ex.Message, null);
                return ErrorJson(id, ex.Message);
            }
        }

        /// <summary>
        /// Fire-and-forget completion toast. Uses the privacy-filtered result
        /// (same policy as the wire response) — never raw result data.
        /// </summary>
        private static void NotifyCompleted(string cmd, string resultJson,
            bool success, string error, string description)
        {
            var notifier = App.ToastNotifier;
            if (notifier == null)
                return;
            try
            {
                notifier.OnCompleted(
                    cmd,
                    resultJson,
                    success,
                    success ? null : McpResponsePrivacy.SanitizeError(error),
                    description);
            }
            catch (Exception ex)
            {
                PluginLog.Debug("McpToastNotifier.OnCompleted failed: " + ex.Message);
            }
        }

        private CommandResult ExecuteCommand(Autodesk.AutoCAD.ApplicationServices.Document doc, string cmd, JToken parameters)
        {
            var preflight = ValidateCommand(cmd, parameters, out var handler);
            if (!preflight.Ok)
                return preflight;
            return handler.Execute(doc, parameters);
        }

        private CommandResult ValidateCommand(string cmd, JToken parameters, out IAcadCommand handler)
        {
            handler = null;
            // A queued/running LISP owns the drawing. Status is managed-only,
            // readable even while the user is answering an AutoCAD prompt.
            if (cmd != "get_lisp_status" && LispJobStore.Session.IsBusy)
                return CommandResult.Fail("a LISP job is queued/running; inspect dwg_get_lisp_status and finish/cancel it before another CAD operation");

            if (string.Equals(cmd, "send_code", StringComparison.Ordinal) && !SendCodeEnabled)
                return CommandResult.Fail("code execution is disabled for this AutoCAD session. Run MCPENABLECODE in AutoCAD to re-enable.");

            if (!_commands.TryGetValue(cmd, out handler))
                return CommandResult.Fail($"unknown command: {cmd}");

            var validation = SchemaValidator.Validate(cmd, parameters, handler.Schema);
            if (!validation.Ok)
                return CommandResult.Fail(validation.Error);

            return CommandResult.Success(null);
        }

        private static string SerializeResponse(string id, string cmd, CommandResult result)
        {
            return JsonConvert.SerializeObject(new
            {
                id,
                ok = result.Ok,
                result = result.Ok ? McpResponsePrivacy.FilterResult(cmd, result.Result) : null,
                error = result.Ok ? null : McpResponsePrivacy.SanitizeError(result.Error)
            });
        }

        public static string ErrorJson(string id, string error) =>
            JsonConvert.SerializeObject(new { id, ok = false, error = McpResponsePrivacy.SanitizeError(error) });
    }
}
