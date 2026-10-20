using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Preferences
{
    public static partial class AutoCadSettingsCatalog
    {
        private static void AddDisplayDraftingSelection(List<SettingDefinition> list)
        {
            const string docs = "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/";
            const string snapMaskRestriction = "Read-only: this Boolean facade represents part of AUTOSNAP. A dedicated raw-mask operation is required to preserve all unrepresented bits.";
            const string trackMaskRestriction = "Read-only: this Boolean facade represents part of TRACKPATH. A dedicated raw-mask operation is required to preserve all unrepresented bits.";

            // These are profile preferences, outside the drawing transaction/Undo.
            // Display colors are OLE_COLOR values; snap/grip colors below are ACI.
            // Scope was also checked against their native variables: CURSORSIZE,
            // LAYOUTTAB, IMAGEHLT, XFADECTL, APBOX, APERTURE, PICKFIRST, PICKBOX,
            // GRIPBLOCK, GRIPHOT, GRIPCOLOR and GRIPSIZE are registry/user settings.
            Add(list, "display.crosshair_size", "display", "CursorSize", SettingKind.Integer,
                "Crosshair size as a percentage of the drawing window; 100 fills the window.", min: 1, max: 100,
                source: docs + "GUID-5158B66E-025B-455A-8815-340D784DD974.htm");
            Add(list, "display.model_crosshair_color", "display", "ModelCrosshairColor", SettingKind.Color,
                "Crosshair and associated text color in model space.",
                source: docs + "GUID-1AD5F1B8-300A-4FD3-A3C8-B4A9F4B98EC9.htm");
            Add(list, "display.layout_crosshair_color", "display", "LayoutCrosshairColor", SettingKind.Color,
                "Crosshair and associated text color in paper space layouts.", effect: "regen_may_be_required",
                source: docs + "GUID-464211ED-2883-41D7-BA53-421ADB81F0FD.htm");
            Add(list, "display.model_background_color", "display", "GraphicsWinModelBackgrndColor", SettingKind.Color,
                "Background color of the model space drawing window.",
                source: docs + "GUID-5ECA99D3-3864-4CEB-A7FA-73755F952825.htm");
            Add(list, "display.layout_background_color", "display", "GraphicsWinLayoutBackgrndColor", SettingKind.Color,
                "Background color of the paper space drawing window.",
                source: docs + "GUID-BD4D1509-D9EF-469B-B2E9-A57B03026363.htm");
            Add(list, "display.text_window_background_color", "display", "TextWinBackgrndColor", SettingKind.Color,
                "Background color of the text window.",
                source: docs + "GUID-E03018A2-A944-4AD5-B9D6-C6BCE14082C2.htm");
            Add(list, "display.text_window_text_color", "display", "TextWinTextColor", SettingKind.Color,
                "Text color of the text window.",
                source: "https://help.autodesk.com/cloudhelp/2018/DEU/AutoCAD-ActiveX-Reference/files/GUID-4A0B5642-10C6-4BC4-A178-3380E221B63D.htm");
            Add(list, "display.tracking_vector_color", "display", "AutoTrackingVecColor", SettingKind.Color,
                "Color of the AutoTrack tracking vector.",
                source: docs + "GUID-2CB3BCFA-A3A0-448B-9082-C91BE0EDDF5D.htm");
            Add(list, "display.layout_tabs_visible", "display", "DisplayLayoutTabs", SettingKind.Boolean,
                "Display the Model and layout tabs in the drawing editor.",
                source: "https://help.autodesk.com/cloudhelp/2024/FRA/AutoCAD-LT-ActiveX-Reference/files/GUID-A7553FB9-EF49-4D32-9D6C-B5DA270769FE.htm");
            Add(list, "display.scroll_bars_visible", "display", "DisplayScrollBars", SettingKind.Boolean,
                "Display scroll bars at the bottom and right of the drawing window.",
                source: docs + "GUID-8471281D-A9DA-4D99-88F7-F78216373262.htm");
            Add(list, "display.raster_frame_highlight", "display", "ImageFrameHighlight", SettingKind.Boolean,
                "Highlight the raster image frame when selected instead of the image content.",
                source: docs + "GUID-FB5E2FBC-2733-4C98-8DD2-571095EC9779.htm");
            Add(list, "display.layout_create_viewport", "display", "LayoutCreateViewport", SettingKind.Boolean,
                "Automatically create a viewport in newly created layouts.", effect: "new_layouts",
                source: docs + "GUID-C8DE3522-D524-4F44-92FA-4355DE99298D.htm");
            Add(list, "display.layout_display_margins", "display", "LayoutDisplayMargins", SettingKind.Boolean,
                "Display printable-area margins in layouts.", effect: "regen_may_be_required",
                source: docs + "GUID-D533152D-FB52-45EC-9F22-4DEE380DFEFD.htm");
            Add(list, "display.layout_display_paper", "display", "LayoutDisplayPaper", SettingKind.Boolean,
                "Display the paper background in layouts.", effect: "regen_may_be_required",
                source: docs + "GUID-365FE258-D291-455B-9316-52180AE4B267.htm");
            Add(list, "display.layout_display_paper_shadow", "display", "LayoutDisplayPaperShadow", SettingKind.Boolean,
                "Display the paper background shadow in layouts.", effect: "regen_may_be_required",
                source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT-ActiveX-Reference/files/GUID-7AC169DF-6642-4497-A972-E6C635297F49.htm");
            Add(list, "display.layout_show_plot_setup", "display", "LayoutShowPlotSetup", SettingKind.Boolean,
                "Display the plot setup dialog when a new layout is created.", effect: "new_layouts",
                source: docs + "GUID-576C287A-D207-4E55-B80C-CA7D3880E4A9.htm");
            Add(list, "display.xref_fade_intensity", "display", "XRefFadeIntensity", SettingKind.Integer,
                "Fading percentage while a reference is edited in place, also used by alternate annotation representations; stored in XFADECTL.", min: 1, max: 90,
                effect: "regen_may_be_required",
                source: docs + "GUID-7EA86D18-6866-49D9-9718-392453A9E2DE.htm");

            Add(list, "drafting.autosnap_marker", "drafting", "AutoSnapMarker", SettingKind.Boolean,
                "Display the geometric AutoSnap marker at an object snap point.", writable: false,
                restriction: snapMaskRestriction,
                source: docs + "GUID-A6A0729C-7953-4754-80C1-268576923463.htm");
            Add(list, "drafting.autosnap_magnet", "drafting", "AutoSnapMagnet", SettingKind.Boolean,
                "Move the crosshair automatically to the nearest object snap point.", writable: false,
                restriction: snapMaskRestriction,
                source: "https://help.autodesk.com/cloudhelp/2016/PTB/AutoCAD-ActiveX/files/GUID-5B032EAD-FA0A-4513-84E4-3F5C2B39D43C.htm");
            Add(list, "drafting.autosnap_tooltip", "drafting", "AutoSnapTooltip", SettingKind.Boolean,
                "Display a tooltip identifying the object snap point.", writable: false,
                restriction: snapMaskRestriction,
                source: "https://help.autodesk.com/cloudhelp/2018/DEU/AutoCAD-ActiveX-Reference/files/GUID-0FB95B52-07FB-4F8F-A429-762A37F090CD.htm");
            Add(list, "drafting.autosnap_aperture", "drafting", "AutoSnapAperture", SettingKind.Boolean,
                "Display the AutoSnap aperture box when applicable; stored in APBOX.",
                source: docs + "GUID-50E9F842-080C-4168-8FCB-A69106511A98.htm");
            Add(list, "drafting.autosnap_aperture_size", "drafting", "AutoSnapApertureSize", SettingKind.Integer,
                "Object snap target box size in device-independent pixels; stored in APERTURE.", min: 1, max: 50,
                source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-33A0F8E8-9981-4111-AD73-128B20D7975C.htm");
            Add(list, "drafting.autosnap_marker_color", "drafting", "AutoSnapMarkerColor", SettingKind.Integer,
                "AutoSnap marker ACI color: 1 red, 2 yellow, 3 green, 4 cyan, 5 blue, 6 magenta, 7 white.", min: 1, max: 7,
                source: docs + "GUID-ADEE24EE-9C1D-4551-9CF4-EEA68D53C7FF.htm");
            Add(list, "drafting.autosnap_marker_size", "drafting", "AutoSnapMarkerSize", SettingKind.Integer,
                "AutoSnap marker size in pixels.", min: 1, max: 20,
                source: docs + "GUID-13574F50-ED66-4842-9CF1-841D8ABA798E.htm");
            Add(list, "drafting.polar_tracking_vector", "drafting", "PolarTrackingVector", SettingKind.Boolean,
                "Display the polar tracking vector.", writable: false, restriction: trackMaskRestriction,
                source: docs + "GUID-C0ECC387-AA40-4F91-8801-9D3F13D4AF1F.htm");
            Add(list, "drafting.full_screen_tracking_vector", "drafting", "FullScreenTrackingVector", SettingKind.Boolean,
                "Display tracking vectors as infinite lines.", writable: false, restriction: trackMaskRestriction,
                source: docs + "GUID-14FDF9EC-6E9A-4A6E-B856-8C485270E9CA.htm");
            Add(list, "drafting.autotrack_tooltip", "drafting", "AutoTrackTooltip", SettingKind.Boolean,
                "Display a tooltip when the cursor is over an alignment path.", writable: false,
                restriction: snapMaskRestriction,
                source: docs + "GUID-036A054F-DAB4-43FE-8590-452FA8AEF174.htm");
            Add(list, "drafting.alignment_point_acquisition", "drafting", "AlignmentPointAcquisition", SettingKind.Integer,
                "Acquire alignment points automatically (0) or only while Shift is pressed (1).", writable: false, min: 0, max: 1,
                restriction: "Read-only: alignment point acquisition shares the POLARMODE bit mask with angle and tracking modes; a dedicated raw-mask operation is required for exact preservation and rollback.",
                source: docs + "GUID-2BD6A159-06E9-44C9-B007-D1E8BB61B1E0.htm");

            Add(list, "selection.pick_first", "selection", "PickFirst", SettingKind.Boolean,
                "Allow objects to be selected before a command is started; stored in PICKFIRST.",
                source: docs + "GUID-A6E39C97-5758-4B08-9258-4002659F6FC4.htm");
            Add(list, "selection.pick_box_size", "selection", "PickBoxSize", SettingKind.Integer,
                "Object selection target height in pixels; stored in PICKBOX.", min: 0, max: 50,
                source: docs + "GUID-04FDE533-18A4-4F53-8089-B00DFE89EE84.htm");
            Add(list, "selection.display_grips_within_blocks", "selection", "DisplayGripsWithinBlocks", SettingKind.Boolean,
                "Assign grips to objects inside a block instead of only its insertion point; stored in GRIPBLOCK.",
                source: "https://help.autodesk.com/cloudhelp/2024/ITA/AutoCAD-LT-ActiveX-Reference/files/GUID-B450F089-9767-423B-A807-6C6A0A4D53BC.htm");
            Add(list, "selection.grip_color_selected", "selection", "GripColorSelected", SettingKind.Integer,
                "ACI color of selected grips; stored in GRIPHOT.", min: 1, max: 255,
                source: "https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-LT-ActiveX-Reference/files/GUID-ADF39087-7952-457E-A375-42329C247930.htm");
            Add(list, "selection.grip_color_unselected", "selection", "GripColorUnselected", SettingKind.Integer,
                "ACI color of unselected grips; stored in GRIPCOLOR.", min: 1, max: 255,
                source: docs + "GUID-A10642A8-A73C-4CCB-AE35-02A434B55D2F.htm");
            Add(list, "selection.grip_size", "selection", "GripSize", SettingKind.Integer,
                "Grip box size in pixels; practical values are 1 to 20, with 1 to 255 accepted by AutoCAD.", min: 1, max: 255,
                source: docs + "GUID-DF4803CE-9092-42DB-987F-0AAD179DB9B9.htm");

            // Modern native settings have more states than these Boolean facades.
            // Publishing them as read-only prevents lossy snapshots and rollback.
            Add(list, "selection.pick_add", "selection", "PickAdd", SettingKind.Boolean,
                "Legacy Boolean view of Shift-based selection addition.", writable: false,
                restriction: "Read-only: PICKADD has multiple native modes that this Boolean facade cannot represent or restore exactly.",
                source: "https://help.autodesk.com/cloudhelp/2020/CHT/AutoCAD-ActiveX-Reference/files/GUID-FBD52FC0-7832-488C-903A-5FF300D74D35.htm");
            Add(list, "selection.pick_auto", "selection", "PickAuto", SettingKind.Boolean,
                "Legacy Boolean view of automatic selection windowing.", writable: false,
                restriction: "Read-only: PICKAUTO is a native bit mask containing window and lasso modes that this Boolean facade cannot preserve.",
                source: docs + "GUID-30EAE946-68A6-4C07-BBF5-ED7AF30869F6.htm");
            Add(list, "selection.pick_drag", "selection", "PickDrag", SettingKind.Boolean,
                "Legacy Boolean view of dragging a selection window.", writable: false,
                restriction: "Read-only: PICKDRAG includes a third native mode that this Boolean facade cannot represent or restore exactly.",
                source: "https://help.autodesk.com/cloudhelp/2026/DEU/AutoCAD-LT-ActiveX-Reference/files/GUID-FABF477A-1820-4729-BDD8-5595A4968CBF.htm");
            Add(list, "selection.display_grips", "selection", "DisplayGrips", SettingKind.Boolean,
                "Legacy Boolean view of displaying selection grips.", writable: false,
                restriction: "Read-only: GRIPS has a separate midpoint-grip mode that this Boolean facade cannot represent or restore exactly.",
                source: docs + "GUID-3B348F7F-5D90-48DF-9305-06306F451AC2.htm");
            Add(list, "selection.pick_group", "selection", "PickGroup", SettingKind.Boolean,
                "Legacy Boolean view of selecting an entire group from one member.", writable: false,
                restriction: "Read-only: group selection shares native PICKSTYLE state with associative-hatch selection; use a dedicated raw-mask operation to preserve both.",
                source: docs + "GUID-07482E80-06FB-48A1-9702-46D5089C1FFE.htm");
        }
    }
}
