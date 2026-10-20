using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Preferences;

public static partial class AutoCadSettingsCatalog
{
    private static void AddOtherPreferences(List<SettingDefinition> list)
    {
        const string systemSource = "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-F3EEF084-D027-4E12-B3F7-83B34E81D6EA.htm";
        const string outputSource = "https://help.autodesk.com/cloudhelp/2018/DEU/AutoCAD-ActiveX-Reference/files/GUID-55D3071F-86C0-4974-B972-397C7D3DF78D.htm";
        const string filesSource = "https://help.autodesk.com/cloudhelp/2019/CHT/AutoCAD-ActiveX-Reference/files/GUID-539ADB58-295E-4BF1-A0CB-D104163409F9.htm";
        const string profilesSource = "https://help.autodesk.com/cloudhelp/2025/DEU/AutoCAD-LT-ActiveX-Reference/files/GUID-EE065355-4343-4954-8C5F-04E2BA87891F.htm";

        Add(list, "open_save.auto_save_interval", "open_save", "AutoSaveInterval", SettingKind.Integer,
            "Automatic-save interval in minutes; 0 disables automatic saving. Changing the preference does not issue a save command.",
            min: 0, max: 600, effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2026/FRA/AutoCAD-LT-ActiveX-Reference/files/GUID-165DB51C-B8DB-4771-BF10-354E05083FC5.htm");
        Add(list, "open_save.incremental_save_percent", "open_save", "IncrementalSavePercent", SettingKind.Integer,
            "Percentage of wasted file space allowed before a full save; 0 makes future saves full saves.",
            min: 0, max: 100, effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-ActiveX-Reference/files/GUID-D8113829-856C-430C-9B30-DD6FFBE41754.htm");
        Add(list, "open_save.create_backup", "open_save", "CreateBackup", SettingKind.Boolean,
            "Whether future saves create a .bak backup file.", effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-ActiveX/files/GUID-5F412E54-831A-4FAE-8750-62110684C3A1.htm");
        Add(list, "open_save.full_crc_validation", "open_save", "FullCRCValidation", SettingKind.Boolean,
            "Whether cyclic redundancy checks are performed when objects are read.", effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2024/ITA/AutoCAD-LT-ActiveX-Reference/files/GUID-A8DD3DC9-EAED-41D6-A111-5669DC1ABF12.htm");
        Add(list, "open_save.log_file_on", "open_save", "LogFileOn", SettingKind.Boolean,
            "Whether command text is logged; enabling this can create log files at the configured existing location.",
            source: "https://help.autodesk.com/cloudhelp/2016/CHS/AutoCAD-ActiveX/files/GUID-F7B421D7-04E9-4FD3-A068-D137C5175254.htm");
        Add(list, "open_save.proxy_image", "open_save", "ProxyImage", SettingKind.Integer,
            "Proxy display: 0 hidden, 1 proxy graphics, 2 bounding boxes.", allowed: new[] { 0, 1, 2 },
            source: "https://help.autodesk.com/cloudhelp/2022/KOR/AutoCAD-ActiveX-Reference/files/GUID-66A38DB1-D923-4D21-BA0F-BDA5538D4528.htm");
        Add(list, "open_save.show_proxy_dialog_box", "open_save", "ShowProxyDialogBox", SettingKind.Boolean,
            "Whether to show a warning when a future opened drawing contains custom objects.", effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2015/PLK/AutoCAD-ActiveX/files/GUID-53D5ADBD-E295-449A-B623-0D18FD1CB22B.htm");
        Add(list, "open_save.save_preview_thumbnail", "open_save", "SavePreviewThumbnail", SettingKind.Boolean,
            "Raw preview-thumbnail preference as reported by AutoCAD.", writable: false, effect: "future",
            restriction: "Autodesk documents inverted Boolean semantics for this property; writing is excluded until native semantics have been explicitly verified.",
            source: "https://help.autodesk.com/cloudhelp/2022/DEU/AutoCAD-ActiveX-Reference/files/GUID-FBBEAF43-5EE2-476A-8ABE-BDE547D631C7.htm");
        Add(list, "open_save.temp_file_extension", "open_save", "TempFileExtension", SettingKind.Text,
            "Temporary-file extension (the native API specifies one to three characters).", writable: false, effect: "future",
            restriction: "Temporary-file naming needs a dedicated validated file-settings operation.",
            source: "https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-ActiveX-Reference/files/GUID-1D7AA573-0C0A-494E-873F-DC93BFDEE757.htm");
        Add(list, "open_save.xref_demand_load", "open_save", "XrefDemandLoad", SettingKind.Integer,
            "Xref demand loading: 0 disabled, 1 enabled, 2 enabled using a copy.", writable: false,
            allowed: new[] { 0, 1, 2 }, effect: "future",
            restriction: "Demand-loading mode can change locks on referenced drawings and needs an explicit xref workflow.",
            source: "https://help.autodesk.com/cloudhelp/2022/DEU/AutoCAD-ActiveX-Reference/files/GUID-A5AB9EB4-4AF9-4936-9C5F-14F2D5825F42.htm");
        Add(list, "open_save.demand_load_arx_app", "open_save", "DemandLoadARXApp", SettingKind.Integer,
            "Third-party application demand-loading flags: disabled, object detection, command invocation, or both.",
            writable: false, allowed: new[] { 0, 1, 2, 3 }, effect: "future",
            restriction: "Application code-loading policy is excluded from the settings writer.",
            source: "https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-ActiveX-Reference/files/GUID-EFA6C27E-9761-4094-8898-AFDFCF752F1A.htm");
        Add(list, "open_save.auto_audit", "open_save", "AutoAudit", SettingKind.Boolean,
            "Whether a future DXFIN/DXBIN operation automatically audits its result.", writable: false, effect: "future",
            restriction: "Automatic drawing-repair behavior is excluded from ordinary preference writes.",
            source: "https://help.autodesk.com/cloudhelp/2022/KOR/AutoCAD-ActiveX-Reference/files/GUID-0E58470A-02B7-4083-B75F-0BD6124C5FC5.htm");
        Add(list, "open_save.save_as_type", "open_save", "SaveAsType", SettingKind.Integer,
            "Native save-format enum for future saves; this preference does not save a drawing.", writable: false,
            effect: "future", restriction: "Output-format policy needs a dedicated named-format allowlist; obsolete and unknown enum values must never be written.",
            source: "https://help.autodesk.com/cloudhelp/2026/DEU/AutoCAD-ActiveX-Reference/files/GUID-5A1DCAF1-A066-4D04-9557-1F45B68A8031.htm");
        Add(list, "open_save.mru_number", "open_save", "MRUNumber", SettingKind.Integer,
            "Number of recent files reported by the native preference API.", writable: false,
            restriction: "The native ActiveX property is read-only.",
            source: "https://help.autodesk.com/cloudhelp/2022/KOR/AutoCAD-ActiveX-Reference/files/GUID-7F2A2CA1-E424-45E8-98F3-807B4CB0AB48.htm");

        Add(list, "user.keyboard_accelerator", "user", "KeyboardAccelerator", SettingKind.Integer,
            "Keyboard convention: 0 AutoCAD classic, 1 Windows standard.", allowed: new[] { 0, 1 },
            source: "https://help.autodesk.com/cloudhelp/2018/PLK/AutoCAD-ActiveX-Reference/files/GUID-1CAE83B8-7D64-4802-B5E3-4E09CE932FE4.htm");
        Add(list, "user.keyboard_priority", "user", "KeyboardPriority", SettingKind.Integer,
            "Coordinate input priority: 0 running object snaps, 1 typed coordinates, 2 typed coordinates except scripts.",
            allowed: new[] { 0, 1, 2 },
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-ED8B8F27-58DC-4AA0-A800-7E9D48C3BA87.htm");
        Add(list, "user.hyperlink_display_cursor", "user", "HyperlinkDisplayCursor", SettingKind.Boolean,
            "Whether hovering a hyperlink displays its cursor indication and shortcut menu.",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-AE6142DF-EE37-44A3-BBE6-4DBAF47C1469.htm");
        Add(list, "user.adc_insert_units_default_source", "user", "ADCInsertUnitsDefaultSource", SettingKind.Integer,
            "DesignCenter fallback source units for a drawing without insert units; documented writable range 0-20. Does not change current DWG units.",
            min: 0, max: 20, effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2015/RUS/AutoCAD-ActiveX/files/GUID-69DA30EF-94C0-460F-BFFF-9D51DCC4E9E3.htm");
        Add(list, "user.adc_insert_units_default_target", "user", "ADCInsertUnitsDefaultTarget", SettingKind.Integer,
            "DesignCenter fallback target units for a drawing without insert units; documented writable range 0-20. Does not change current DWG units.",
            min: 0, max: 20, effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2018/PLK/AutoCAD-ActiveX-Reference/files/GUID-5E2E6B8A-EE00-456D-B406-6B8DAEBA38AC.htm");
        const string shortcutRestriction = "These facade properties share shortcut-menu state and require group-aware dependency/rollback handling; generic independent writes are excluded.";
        Add(list, "user.shortcut_menu_display", "user", "ShortCutMenuDisplay", SettingKind.Boolean,
            "Whether drawing-area right click opens shortcut menus or sends Enter.", writable: false,
            restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2015/PLK/AutoCAD-ActiveX/files/GUID-B7281140-9B91-4381-95C7-FFEEBB92E012.htm");
        Add(list, "user.scm_default_mode", "user", "SCMDefaultMode", SettingKind.Integer,
            "Right click when idle: 0 repeat last command, 1 shortcut menu; requires shortcut menus enabled.",
            writable: false, allowed: new[] { 0, 1 }, restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-46F3E045-EE60-45B6-863E-0B050A2CFC1F.htm");
        Add(list, "user.scm_edit_mode", "user", "SCMEditMode", SettingKind.Integer,
            "Right click with objects selected: 0 repeat last command, 1 shortcut menu; requires shortcut menus enabled.",
            writable: false, allowed: new[] { 0, 1 }, restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-38275633-CD86-45D1-989F-37B37648A02C.htm");
        Add(list, "user.scm_command_mode", "user", "SCMCommandMode", SettingKind.Integer,
            "Right click during a command: 0 Enter, 1 menu when command options exist, 2 always menu; requires shortcut menus enabled.",
            writable: false, allowed: new[] { 0, 1, 2 }, restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2023/DEU/AutoCAD-ActiveX-Reference/files/GUID-EC02DC92-7342-4072-9641-38C86CC67CA7.htm");
        Add(list, "user.scm_time_mode", "user", "SCMTimeMode", SettingKind.Boolean,
            "Whether quick right click sends Enter while a long click opens a menu; requires shortcut menus enabled.",
            writable: false, restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-C0DD66CD-7C48-48E3-BFB4-83854285C35A.htm");
        Add(list, "user.scm_time_value", "user", "SCMTimeValue", SettingKind.Integer,
            "Long-right-click threshold in milliseconds, effective when timed right click is enabled.",
            writable: false, min: 100, max: 10000, restriction: shortcutRestriction,
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-9D486862-2570-49FE-979A-4286473F4FEB.htm");

        Add(list, "system.beep_on_error", "system", "BeepOnError", SettingKind.Boolean,
            "Whether invalid entries produce an alarm beep.",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-D3CDB0ED-4F45-4D6F-923B-8E8859CD3147.htm");
        Add(list, "system.display_ole_scale", "system", "DisplayOLEScale", SettingKind.Boolean,
            "Whether future OLE insertion displays the scaling dialog.", effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-7E0D3384-0746-4ED0-9363-F42F8E8607C6.htm");
        Add(list, "system.enable_startup_dialog", "system", "EnableStartupDialog", SettingKind.Boolean,
            "Whether to show the traditional startup dialog on startup or creation of a new drawing; this is not the modern Start-tab setting.",
            writable: false, effect: "next_start",
            restriction: "The Boolean facade cannot preserve all STARTUP modes (0-3); changing it requires a dedicated native-mode operation.",
            source: "https://help.autodesk.com/cloudhelp/2016/PTB/AutoCAD-ActiveX/files/GUID-33F3A3B6-1F5B-4031-9EDF-AA83E7DDD66B.htm");
        Add(list, "system.single_document_mode", "system", "SingleDocumentMode", SettingKind.Boolean,
            "Native single-document-mode compatibility preference.", writable: false,
            restriction: "Changing document lifecycle is excluded from the settings writer.",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-5033C801-C1ED-4DDF-8EE8-47B85B33412C.htm");
        Add(list, "system.store_sql_index", "system", "StoreSQLIndex", SettingKind.Boolean,
            "Whether an SQL index is stored with drawings.", writable: false, effect: "future",
            restriction: "Database-connectivity storage policy needs its own explicit workflow.",
            source: "https://help.autodesk.com/cloudhelp/2018/PLK/AutoCAD-ActiveX-Reference/files/GUID-B0D0DA13-AD30-4EA6-A3A6-0A5E1CDF5C2F.htm");
        Add(list, "system.tables_read_only", "system", "TablesReadOnly", SettingKind.Boolean,
            "Whether database-connectivity tables are opened read-only.", writable: false, effect: "future",
            restriction: "External database access policy needs its own explicit workflow.",
            source: "https://help.autodesk.com/cloudhelp/2022/KOR/AutoCAD-ActiveX-Reference/files/GUID-C1306320-148C-4FBE-B694-7D41588D32F8.htm");
        Add(list, "system.show_warning_messages", "system", "ShowWarningMessages", SettingKind.Boolean,
            "Native flag that resets suppressed warning dialogs when set true.", writable: false,
            restriction: "Resetting suppressed-dialog state is an action, not an independently rollbackable Boolean preference.",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-053AA468-031F-48C9-B334-422C8687EF1A.htm");
        Add(list, "system.load_acad_lsp_in_all_documents", "system", "LoadAcadLspInAllDocuments", SettingKind.Boolean,
            "Whether acad.lsp is loaded for all documents.", writable: false, effect: "future",
            restriction: "Startup code-loading policy is excluded from the settings writer.", source: systemSource);

        Add(list, "output.printer_paper_size_alert", "output", "PrinterPaperSizeAlert", SettingKind.Boolean,
            "Whether a future plot warns about a layout paper size different from its PC3 default.", effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2026/DEU/AutoCAD-ActiveX-Reference/files/GUID-E702EF7F-9092-4D4A-8B46-F71C22021A84.htm");
        Add(list, "output.printer_spool_alert", "output", "PrinterSpoolAlert", SettingKind.Integer,
            "Spool-conflict warnings: 0 always alert and log, 1 alert once and log, 2 log without alert, 3 neither alert nor log.",
            allowed: new[] { 0, 1, 2, 3 }, effect: "future",
            source: "https://help.autodesk.com/cloudhelp/2018/PLK/AutoCAD-ActiveX-Reference/files/GUID-FE9774DE-7CB8-45D9-A354-41E0FE0442D7.htm");
        Add(list, "output.ole_quality", "output", "OLEQuality", SettingKind.Integer,
            "Raw OLE plot-quality enum reported by the ActiveX preference API.",
            writable: false, effect: "future",
            restriction: "ActiveX enum and OLEQUALITY system-variable documentation differ; native mapping must be verified before writes.",
            source: "https://help.autodesk.com/cloudhelp/2018/DEU/AutoCAD-ActiveX-Reference/files/GUID-102ECFD0-3216-48E2-8AEC-53E640B432FA.htm");
        Add(list, "output.use_last_plot_settings", "output", "UseLastPlotSettings", SettingKind.Boolean,
            "Whether future plotting uses the settings of the last successful plot; does not launch plotting.", writable: false, effect: "future",
            restriction: "Applying previous plot settings requires a dedicated plotting workflow with drawing identity and side-effect verification.",
            source: "https://help.autodesk.com/cloudhelp/2016/PTB/AutoCAD-ActiveX/files/GUID-0E4CAF5C-E20E-4716-BF06-6B4F9C804426.htm");
        Add(list, "output.automatic_plot_log", "output", "AutomaticPlotLog", SettingKind.Boolean,
            "Whether future plot/publish operations automatically write their logs.", writable: false, effect: "future",
            restriction: "Plot-log facade dependencies need native group-state verification before independent writes.",
            source: "https://help.autodesk.com/cloudhelp/2015/PLK/AutoCAD-ActiveX/files/GUID-A89AA90D-E121-453E-977D-383ADDC8EF52.htm");
        Add(list, "output.continuous_plot_log", "output", "ContinuousPlotLog", SettingKind.Boolean,
            "Whether future plotting writes one continuous log instead of a separate log for each plot.", writable: false, effect: "future",
            restriction: "Plot-log facade dependencies need native group-state verification before independent writes.",
            source: "https://help.autodesk.com/cloudhelp/2015/PLK/AutoCAD-ActiveX/files/GUID-6867506E-542C-40A9-9578-68F382CAB208.htm");
        Add(list, "output.default_output_device", "output", "DefaultOutputDevice", SettingKind.Text,
            "Default output-device name.", writable: false, effect: "future",
            restriction: "Device selection needs an explicit plotting task and validation against installed devices.", source: outputSource);
        Add(list, "output.plot_legacy", "output", "PlotLegacy", SettingKind.Boolean,
            "Whether legacy plot scripts may run.", writable: false, effect: "future",
            restriction: "Script-execution policy is excluded from the settings writer.",
            source: "https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-ActiveX-Reference/files/GUID-14EA2D3F-6D49-44FC-8CE7-AE44F9314E29.htm");
        Add(list, "output.plot_policy", "output", "PlotPolicy", SettingKind.Integer,
            "Plot-style policy for future drawings: 0 named styles, 1 color-dependent legacy styles.", writable: false,
            allowed: new[] { 0, 1 }, effect: "future",
            restriction: "Plot-style policy and loaded STB/CTB dependencies need a dedicated workflow.", source: outputSource);
        Add(list, "output.default_plot_style_table", "output", "DefaultPlotStyleTable", SettingKind.Text,
            "Default plot-style table for future drawings.", writable: false, effect: "future",
            restriction: "Plot-style resource selection requires validation of installed tables and policy dependencies.", source: outputSource);
        Add(list, "output.default_plot_style_for_objects", "output", "DefaultPlotStyleForObjects", SettingKind.Text,
            "Default object plot style for future drawings under the named-style policy.", writable: false, effect: "future",
            restriction: "Named-style writes require the appropriate plot policy and an existing loaded style.", source: outputSource);
        Add(list, "output.default_plot_style_for_layer", "output", "DefaultPlotStyleForLayer", SettingKind.Text,
            "Default Layer 0 plot style for future drawings under the named-style policy.", writable: false, effect: "future",
            restriction: "Named-style writes require the appropriate plot policy and an existing loaded style.",
            source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT-ActiveX-Reference/files/GUID-EB767530-68D8-4B1D-8ADF-EFBCE7BD8E6F.htm");
        Add(list, "output.default_plot_to_file_path", "output", "DefaultPlotToFilePath", SettingKind.Text,
            "Default location for future plot-to-file operations.", writable: false, effect: "future",
            restriction: "Output-location changes require a dedicated path-validated operation.",
            source: "https://help.autodesk.com/cloudhelp/2026/DEU/AutoCAD-ActiveX-Reference/files/GUID-748E8F16-450E-4DB4-9624-E4AA93F170B2.htm");

        // File preferences are discoverable but deliberately excluded from generic writes.
        // This also avoids accepting arbitrary executable, search, output or network paths.
        const string fileRestriction = "File, resource, executable and search-location preferences are read-only in this API; changes require a dedicated path-validated workflow.";
        var fileProperties = new (string Id, string Property, string Description)[]
        {
            ("support_path", "SupportPath", "Support-file search paths."),
            ("drivers_path", "DriversPath", "Device-driver search location."),
            ("menu_file", "MenuFile", "Main menu/customization resource."),
            ("enterprise_menu_file", "EnterpriseMenuFile", "Enterprise menu/customization resource."),
            ("custom_icon_path", "CustomIconPath", "Custom icon resource location."),
            ("help_file_path", "HelpFilePath", "Help-file location."),
            ("default_internet_url", "DefaultInternetURL", "Default Internet resource URL."),
            ("text_editor", "TextEditor", "External text editor executable."),
            ("main_dictionary", "MainDictionary", "Main spelling dictionary resource."),
            ("custom_dictionary", "CustomDictionary", "Custom spelling dictionary resource."),
            ("alt_font_file", "AltFontFile", "Fallback font resource."),
            ("font_file_map", "FontFileMap", "Font mapping resource."),
            ("print_file", "PrintFile", "Temporary print-file naming preference."),
            ("print_spool_executable", "PrintSpoolExecutable", "Print spooler executable."),
            ("post_script_prolog_file", "PostScriptPrologFile", "PostScript prolog resource."),
            ("print_spooler_path", "PrintSpoolerPath", "Print-spool file location."),
            ("auto_save_path", "AutoSavePath", "Automatic-save file location."),
            ("template_dwg_path", "TemplateDwgPath", "Drawing-template search location."),
            ("log_file_path", "LogFilePath", "Command-log file location."),
            ("temp_file_path", "TempFilePath", "Temporary-file location."),
            ("temp_xref_path", "TempXrefPath", "Temporary xref copy location."),
            ("texture_map_path", "TextureMapPath", "Texture map resource search paths."),
            ("alt_tablet_menu_file", "AltTabletMenuFile", "Alternate tablet menu resource."),
            ("printer_config_path", "PrinterConfigPath", "Printer configuration file location."),
            ("printer_desc_path", "PrinterDescPath", "Printer description file location."),
            ("printer_style_sheet_path", "PrinterStyleSheetPath", "Plot-style file location."),
            ("workspace_path", "WorkspacePath", "Workspace resource location."),
            ("object_arx_path", "ObjectARXPath", "ObjectARX application resource location."),
            ("color_book_path", "ColorBookPath", "Color-book resource location."),
            ("tool_palette_path", "ToolPalettePath", "Tool palette resource location."),
            ("qnew_template_file", "QNewTemplateFile", "Template used by a future QNEW operation."),
            ("plot_log_file_path", "PlotLogFilePath", "Plot/publish log location."),
            ("page_setup_overrides_template_file", "PageSetupOverridesTemplateFile", "Page-setup override template resource."),
            ("active_inv_project", "ActiveInvProject", "Active Inventor project resource.")
        };
        foreach (var entry in fileProperties)
            Add(list, "files." + entry.Id, "files", entry.Property, SettingKind.Text,
                entry.Description, writable: false, effect: "future", restriction: fileRestriction, source: filesSource);
        Add(list, "files.config_file", "files", "ConfigFile", SettingKind.Text,
            "Hardware-device configuration file location.", writable: false, effect: "next_start",
            restriction: "The native ActiveX property is read-only and is selected through the AutoCAD launch configuration.",
            source: "https://help.autodesk.com/cloudhelp/2026/PTB/AutoCAD-LT-ActiveX-Reference/files/GUID-EDF1DB36-6B63-4F8E-A25E-4C21C8615D64.htm");
        Add(list, "files.license_server", "files", "LicenseServer", SettingKind.Text,
            "Legacy license-server value exposed by the native preference interface.", writable: false,
            restriction: "The native ActiveX property is read-only; licensing configuration is outside this API.", source: filesSource);
        Add(list, "profiles.active_profile", "profiles", "ActiveProfile", SettingKind.Text,
            "Exact current profile name used to guard writes; reading this never switches profiles.", writable: false,
            restriction: "Profile switching, importing, resetting, copying, renaming, exporting and deleting are excluded from the settings writer.",
            source: profilesSource);
    }
}
