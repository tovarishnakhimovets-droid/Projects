// Modified by the CAD team (2026-10-07); see patches/ and docs/IMPORT_MANIFEST.json in cad-bridge.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args != null && args.Any(a => a == "--help" || a == "-h"))
            {
                PrintHelp();
                return;
            }

            var config = DwgMcpConfig.Load(args);
            ValidateTarget(config.Target);
            ServerState.Config = config;
            WarnIfUnwiredOptions(config);
            var enabled = ToolsetFilter.Resolve(config);

            var builder = Host.CreateApplicationBuilder(args);
            // MCP stdio transport owns stdout. Route logs to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            var mcp = builder.Services
                .AddMcpServer(ConfigureMcpServerOptions)
                .WithStdioServerTransport();
            mcp = RegisterToolsets(mcp, enabled);

            await builder.Build().RunAsync();
        }

        private static void ConfigureMcpServerOptions(ModelContextProtocol.Server.McpServerOptions opts)
        {
            opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "dwg-mcp",
                Title = "DWG MCP",
                Version = ReleaseVersion(),
                Description = "Model Context Protocol gateway for Autodesk AutoCAD DWG workflows",
                WebsiteUrl = "https://github.com/bimwright/dwg-mcp"
            };
            opts.ServerInstructions = ServerInstructionsText;
        }

        private static string ReleaseVersion()
        {
            var version = typeof(Program).Assembly.GetName().Version;
            return version.Minor == 0
                ? version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : version.ToString(2);
        }

        private const string ServerInstructionsText =
@"dwg-mcp - MCP gateway for Autodesk AutoCAD DWG drawings. Use for current drawing metadata and selection, layers, entity properties, lines/circles, plain MLeader, DBText, MText and explicit text rewriting.

AutoCAD routing: versions are 4-digit years 2022..2027. If multiple AutoCAD instances run, use dwg_list_available_targets then dwg_switch_target, or start the server with --target 2022|2023|2024|2025|2026|2027.

Document routing: dwg_list_documents lists already open DWGs. dwg_use_document chooses a fresh opaque document_id for this MCP server process without switching the user's active tab. Null/omission follows the active tab again, including when no documents are open. Inactive targets permit only get_drawing_info, get_entity_properties, query_entities, count_entities, list_layers, list_text_styles, select_by_layer and select_by_type. Writes, Editor/selection, variables, settings, view and LISP require the target to be active. Stale IDs refuse without fallback. Never carry handles across DWGs; reread identity after selecting a target. Reset to follow-active before reading the user's selected objects.

Tools use prefix dwg_:
- Crosshair preferences: dwg_get_crosshair_settings reads the active profile, size and separate model/layout colors. dwg_set_crosshair_settings changes only explicitly supplied size/RGB fields, requires the exact current profile and verifies native readback. Pass BOTH model_color_rgb and layout_color_rgb for both spaces. Preferences persist outside DWG/Undo; finish active commands/close Options first. No UI automation or drawing save. On failure inspect rollback_verified and reread before retrying.
- Other application preferences: dwg_list_settings is the fixed typed catalog with ranges, effect timing and read-only restrictions. Read only needed IDs using dwg_get_settings; pass its exact profile and per-ID typed values as expected_value to dwg_set_settings. Validate with dry_run first. New colors use RGB; raw OLE colors are valid expectations. Native readback verifies stored preferences, not screen refresh or future restart/plot effects. Unsupported properties have explicit per-ID availability errors. No arbitrary preference paths, security/code-loading/path/profile changes, DWG save or automatic retry.
- query: dwg_get_drawing_info, dwg_list_layers, and dwg_get_entity_properties read the session target document. When the user refers to selected/highlighted objects, first call dwg_get_selection with include_geometry=false; use its fresh handles. Report empty/disabled/unavailable selection without substituting searches, all entities or earlier handles. type_counts describes returned rows only; selected_count is the full native selection size before limiting. dwg_get_selected_texts separately extracts and groups selected text; it is not a general selection reader. dwg_list_text_styles reads existing style/font metadata; dwg_inspect_lisp statically scans .lsp files or inline AutoLISP for known suspicious patterns (verdict clean/caution/dangerous). No verdict certifies safety or authorizes execution. dwg_get_lisp_status reads session approvals and tracked LISP job state.
- modify: dwg_create_layer, dwg_create_line, dwg_create_circle, dwg_change_layer, dwg_update_texts and dwg_collapse_and_rewrite write explicit drawing/text changes without automatic formatting. Translation orchestration and BWUNICODE styling live in CAD_WORKFLOW and the standalone LISP library. dwg_set_text_style assigns an existing style; dwg_match_properties copies an explicit property list. dwg_edit_polyline_vertices edits guarded existing indices in place without erasing the Polyline. dwg_run_lisp queues a session-approved standalone file/function.
- drawing: dwg_create_text and dwg_create_mtext create editable text using an existing style. dwg_create_leader creates one plain straight MLeader from exactly two WCS points, with a closed filled arrow and no landing/dogleg/frame. These three tools are also in annotation without duplicate registration. dwg_create_entities creates one bounded heterogeneous plan with local IDs, full document identity, all-target preflight, dry_run and one transaction. It is separate from legacy sequential dwg_batch_execute; native single-Undo behavior still needs verification. LWDISPLAY can be read and set using boolean or 0/1 values.
- tables: dwg_create_table (drawing/annotation) creates one guarded native TABLE from typed plain-string cells, explicit WCS sizes, zero-based merges and cell formats; use dry_run first, keep merged non-anchor cells empty, then verify readback and the view. dwg_read_table (query/drawing/annotation, also read-only) reads one TABLE by handle and exact saved path/fingerprint. Both require the active DWG; neither saves. Native text fit and single-Undo behavior require live verification.
- meta: dwg_batch_execute, dwg_list_available_targets, dwg_get_current_target, dwg_switch_target, dwg_send_code, dwg_run_lisp.
- view: Start visual reading with dwg_capture_view_image (inline image plus JSON). If region_navigation.supported, choose a normalized top-left-origin rectangle on that image and call dwg_inspect_view_region with its capture_id. Inspect the new image before deciding the next region. Keep ancestor capture IDs and use dwg_restore_view to return to an overview. Use each newly returned ID for further navigation. Do not interpret a failed image_delivery as a failed zoom: take a smaller fresh capture. Region navigation is limited to one top-down, unrotated, orthographic model-space viewport. Verify inferred labels/dimensions against CAD text and entity data. Do not claim unseen regions have been reviewed.
- optional toolbaker, when enabled: dwg_list_baked_tools, dwg_run_baked_tool, dwg_list_bake_suggestions, dwg_accept_bake_suggestion, dwg_dismiss_bake_suggestion, dwg_create_bake_issue_draft.
- dwg_send_code is on by default; the AutoCAD MCPDISABLECODE command disables it for the session (MCPENABLECODE re-enables).
- dwg_run_lisp runs only exact standalone file/function versions approved through AutoCAD MCPTRUSTLISP; queued is not completed. Query tool dwg_get_lisp_status reports actual job state even while the user answers AutoCAD prompts. No inline, batch or ToolBaker LISP, no bypass through send_code. Approved scripts and C# run with full process trust, not in a sandbox.

General CAD tools follow the active AutoCAD document unless this MCP session selected an explicit target. Only the audited database readers above support an inactive target. Entity arguments use AutoCAD hex handles from the same target document.
Read fresh selection/entity data before text writeback. Collapse/rewrite requires the current saved drawing path and fingerprint; inspect per-item outcomes/new handles. Read-only mode also permits view navigation and image file output, but no drawing edits.";

        private static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled)
        {
            var toolTypes = new List<Type>(ResolveToolTypesForRegistration(enabled, ServerState.IsReadOnly));
            ServerState.EnabledToolTypes = toolTypes;
            foreach (var toolType in toolTypes)
            {
                mcp = RegisterToolType(mcp, toolType);
            }

            return mcp;
        }

        private static IEnumerable<Type> ResolveToolTypesForRegistration(HashSet<string> enabled, bool readOnly)
        {
            enabled = enabled ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var toolTypes = new List<Type>();
            if (enabled.Contains("query"))
            {
                toolTypes.Add(typeof(QueryTools));
                toolTypes.Add(typeof(DocumentTools));
                toolTypes.Add(typeof(CrosshairQueryTools));
                toolTypes.Add(typeof(GeneralSettingsQueryTools));
                toolTypes.Add(typeof(TextQueryTools));
                toolTypes.Add(typeof(BlockInspectionTools));
                toolTypes.Add(typeof(XrefQueryTools));
                toolTypes.Add(typeof(BlockExplodeQueryTools));
                toolTypes.Add(typeof(HatchClipQueryTools));
                toolTypes.Add(typeof(ProxyTools));
            }
            if (enabled.Contains("modify") && !readOnly)
            {
                toolTypes.Add(typeof(ModifyTools));
                toolTypes.Add(typeof(CrosshairWriteTools));
                toolTypes.Add(typeof(GeneralSettingsWriteTools));
                toolTypes.Add(typeof(EntityStyleTools));
                toolTypes.Add(typeof(ProxyWriteTools));
                toolTypes.Add(typeof(NetworkTools));
                toolTypes.Add(typeof(PolylineTools));
                toolTypes.Add(typeof(LeaderTools));
                toolTypes.Add(typeof(BlockVariantTools));
                toolTypes.Add(typeof(XrefWriteTools));
                toolTypes.Add(typeof(BlockExplodeWriteTools));
                toolTypes.Add(typeof(HatchClipWriteTools));
            }
            if (enabled.Contains("meta"))
            {
                toolTypes.Add(typeof(MetaTools));
                if (!readOnly) toolTypes.Add(typeof(BatchTools));
            }
            if (enabled.Contains("toolbaker"))
            {
                toolTypes.Add(typeof(ToolBakerTools));
                if (!readOnly) toolTypes.Add(typeof(ToolBakerWriteTools));
            }
            // dwg_send_code ships on the default surface via meta (rvt-mcp parity); the
            // legacy "code" toolset name is still honored as an explicit opt-in alias.
            if ((enabled.Contains("meta") || enabled.Contains("code")) && !readOnly) toolTypes.Add(typeof(CodeTools));
            if ((enabled.Contains("modify") || enabled.Contains("meta") || enabled.Contains("code")) && !readOnly) toolTypes.Add(typeof(LispTools));
            if ((enabled.Contains("annotation") || enabled.Contains("drawing")) && !readOnly) toolTypes.Add(typeof(AnnotationTools));
            if (enabled.Contains("query") || enabled.Contains("drawing") || enabled.Contains("annotation"))
                toolTypes.Add(typeof(TableQueryTools));
            if ((enabled.Contains("annotation") || enabled.Contains("drawing")) && !readOnly)
            {
                toolTypes.Add(typeof(TextCreationTools));
                if (enabled.Contains("drawing")) toolTypes.Add(typeof(EntityBatchTools));
            }
            if (enabled.Contains("block"))
            {
                toolTypes.Add(typeof(BlockTools));
                if (!readOnly) toolTypes.Add(typeof(BlockWriteTools));
            }
            if (enabled.Contains("dimension") && !readOnly) toolTypes.Add(typeof(DimensionTools));

            if (enabled.Contains("view"))
            {
                toolTypes.Add(typeof(ViewTools));
            }
            if (enabled.Contains("export") && !readOnly)
            {
                toolTypes.Add(typeof(ExportTools));
            }
            if (enabled.Contains("drawing"))
            {
                toolTypes.Add(typeof(DrawingTools));
                if (!readOnly) toolTypes.Add(typeof(DrawingWriteTools));
            }
            return toolTypes;
        }

        private static IMcpServerBuilder RegisterToolType(IMcpServerBuilder mcp, Type toolType)
        {
            if (toolType == typeof(QueryTools)) return mcp.WithTools<QueryTools>();
            if (toolType == typeof(DocumentTools)) return mcp.WithTools<DocumentTools>();
            if (toolType == typeof(CrosshairQueryTools)) return mcp.WithTools<CrosshairQueryTools>();
            if (toolType == typeof(CrosshairWriteTools)) return mcp.WithTools<CrosshairWriteTools>();
            if (toolType == typeof(GeneralSettingsQueryTools)) return mcp.WithTools<GeneralSettingsQueryTools>();
            if (toolType == typeof(GeneralSettingsWriteTools)) return mcp.WithTools<GeneralSettingsWriteTools>();
            if (toolType == typeof(TextQueryTools)) return mcp.WithTools<TextQueryTools>();
            if (toolType == typeof(BlockInspectionTools)) return mcp.WithTools<BlockInspectionTools>();
            if (toolType == typeof(XrefQueryTools)) return mcp.WithTools<XrefQueryTools>();
            if (toolType == typeof(XrefWriteTools)) return mcp.WithTools<XrefWriteTools>();
            if (toolType == typeof(BlockExplodeQueryTools)) return mcp.WithTools<BlockExplodeQueryTools>();
            if (toolType == typeof(BlockExplodeWriteTools)) return mcp.WithTools<BlockExplodeWriteTools>();
            if (toolType == typeof(HatchClipQueryTools)) return mcp.WithTools<HatchClipQueryTools>();
            if (toolType == typeof(HatchClipWriteTools)) return mcp.WithTools<HatchClipWriteTools>();
            if (toolType == typeof(BlockVariantTools)) return mcp.WithTools<BlockVariantTools>();
            if (toolType == typeof(TextCreationTools)) return mcp.WithTools<TextCreationTools>();
            if (toolType == typeof(EntityBatchTools)) return mcp.WithTools<EntityBatchTools>();
            if (toolType == typeof(PolylineTools)) return mcp.WithTools<PolylineTools>();
            if (toolType == typeof(LeaderTools)) return mcp.WithTools<LeaderTools>();
            if (toolType == typeof(ModifyTools)) return mcp.WithTools<ModifyTools>();
            if (toolType == typeof(EntityStyleTools)) return mcp.WithTools<EntityStyleTools>();
            if (toolType == typeof(ProxyTools)) return mcp.WithTools<ProxyTools>();
            if (toolType == typeof(ProxyWriteTools)) return mcp.WithTools<ProxyWriteTools>();
            if (toolType == typeof(NetworkTools)) return mcp.WithTools<NetworkTools>();
            if (toolType == typeof(LispTools)) return mcp.WithTools<LispTools>();
            if (toolType == typeof(MetaTools)) return mcp.WithTools<MetaTools>();
            if (toolType == typeof(BatchTools)) return mcp.WithTools<BatchTools>();
            if (toolType == typeof(ToolBakerTools)) return mcp.WithTools<ToolBakerTools>();
            if (toolType == typeof(ToolBakerWriteTools)) return mcp.WithTools<ToolBakerWriteTools>();
            if (toolType == typeof(CodeTools)) return mcp.WithTools<CodeTools>();
            if (toolType == typeof(AnnotationTools)) return mcp.WithTools<AnnotationTools>();
            if (toolType == typeof(TableQueryTools)) return mcp.WithTools<TableQueryTools>();
            if (toolType == typeof(BlockTools)) return mcp.WithTools<BlockTools>();
            if (toolType == typeof(BlockWriteTools)) return mcp.WithTools<BlockWriteTools>();
            if (toolType == typeof(DimensionTools)) return mcp.WithTools<DimensionTools>();
            if (toolType == typeof(ViewTools)) return mcp.WithTools<ViewTools>();
            if (toolType == typeof(ExportTools)) return mcp.WithTools<ExportTools>();
            if (toolType == typeof(DrawingTools)) return mcp.WithTools<DrawingTools>();
            if (toolType == typeof(DrawingWriteTools)) return mcp.WithTools<DrawingWriteTools>();

            throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
        }

        private static void ValidateTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            AuthToken.NormalizeTarget(target);
        }

        public static string UnwiredOptionWarning(DwgMcpConfig config)
        {
            if (config != null && config.AllowLanBindOrDefault)
            {
                return "warning: --allow-lan-bind / BIMWRIGHT_DWG_ALLOW_LAN_BIND is parsed but plugin-side LAN binding is not yet implemented in v1.0; the AutoCAD plugin still listens on loopback only.";
            }

            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
                "dwg-mcp - MCP stdio gateway for Autodesk AutoCAD 2022-2027.\n" +
                "\n" +
                "Usage: dwg-mcp [options]\n" +
                "\n" +
                "Options:\n" +
                "  --target <year>        Pin to AutoCAD 2022|2023|2024|2025|2026|2027.\n" +
                "  --toolsets <csv>       Toolsets to register (default: query,create,view,meta).\n" +
                "                         'all' enables the full surface.\n" +
                "  --read-only            Strip every write/execute tool.\n" +
                "  --enable-toolbaker / --disable-toolbaker\n" +
                "  --allow-lan-bind       Parsed but not yet implemented (loopback only).\n" +
                "  --log-level <level>    trace|debug|information|warning|error.\n" +
                "  --config <path>        JSON config file. Default:\n" +
                "                         %LOCALAPPDATA%\\Bimwright\\Dwg\\dwgmcp.config.json\n" +
                "\n" +
                "Environment overrides: BIMWRIGHT_DWG_TARGET, BIMWRIGHT_DWG_TOOLSETS,\n" +
                "BIMWRIGHT_DWG_READ_ONLY, BIMWRIGHT_DWG_ENABLE_TOOLBAKER,\n" +
                "BIMWRIGHT_DWG_ALLOW_LAN_BIND, BIMWRIGHT_DWG_LOG_LEVEL.\n" +
                "\n" +
                "  -h, --help             Show this help and exit.");
        }

        private static void WarnIfUnwiredOptions(DwgMcpConfig config)
        {
            var warning = UnwiredOptionWarning(config);
            if (!string.IsNullOrEmpty(warning))
            {
                Console.Error.WriteLine(warning);
            }
        }
    }
}
