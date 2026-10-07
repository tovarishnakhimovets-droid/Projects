# AutoCAD 2026 / 2027 bridge: installed runtime

Use `dwg-2026` for AutoCAD 2026 or `dwg-2027` for AutoCAD 2027, matching the
configured local year and the current user request. The enabled profile is
query,modify,drawing,view (42 tools). The MCP is a local stdio process;
Codex starts it, while the plugin is loaded by the installed AutoCAD bundle.
AutoCAD 2026 with .NET 8 uses plugin-acad26 (net8.0-windows);
AutoCAD 2027 uses plugin-acad27 (net10.0-windows). The external net8.0 server
includes its own runtime. No drawing-project server copies are required.
Code is shared; year-specific DLLs must not be substituted for one another.
The 2026 build uses Autodesk compile references AutoCAD.NET 25.1.0; later
25.1.1 targets .NET 10. Native CAD operations are verified on 2027; native
2026 loading and live operations remain pending until confirmed by its user.

Before edits call dwg_get_drawing_info and inspect only necessary objects.
The bridge targets the active document; it has no full drawing-path guard.
has_saved_path can refer to a DWT template and does not prove a saved DWG.
Never save unless requested. Do not use purge/audit as a connection check.
Do not repeat a mutation whose result is uncertain after a timeout.
Inspect ok and per-item results: normal MCP text can contain a failed operation.
Do not substitute unrelated objects for an unavailable non-text selection.

Prefer typed tools. dwg_set_lineweight supports explicit mm, by_layer and
by_block; dwg_set_layer_lineweight supports exact AutoCAD mm values.
dwg_create_hatch supports SOLID only, from one Circle or closed planar
lightweight Polyline in the current space, ACI or RGB, with optional association.
It does not support arbitrary patterns or islands. AutoLISP execution is blocked.
Lineweight 0.30 mm, white RGB [255,255,255] and ByLayer are supported.
Coordinates use drawing units. Project layers and symbols belong in project rules.

If a reusable operation is missing, define its parameters, units, boundaries
and failure behavior, inspect upstream and Autodesk references, then implement
it in the shared source workspace. A successful custom scenario is a prototype.
Check source changes before editing; preserve other chats' work. Do not replace
this shared bridge with another server or put its source into each drawing folder.
The agent edits/builds/offline-tests; the user performs AutoCAD UI actions and
explicitly enabled live tests. Tests must clean only their own objects and must
not save a drawing. Record source/offline/loaded/live state and the next step.

This runtime package contains source patches under bridge-docs/changes.
Source development requires a separate local checkout; see bridge-docs/TEAM_SETUP.md,
bridge-docs/INSTALL-RU.md and the configured source workspace's instructions.
Use separate local Git checkouts for concurrent development. Exchange versioned
ZIP releases through shared storage; never edit a loaded DLL through cloud sync.
