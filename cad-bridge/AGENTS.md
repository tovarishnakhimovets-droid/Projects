# Shared AutoCAD MCP source

Source root: this directory. Implementation: upstream/dwg-mcp (v2.0.1 snapshot,
already patched). There is no nested Git repository. Before bridge edits read
docs/CAD_WORKFLOW.md and the relevant WORK_STATUS.md section; inspect current
Git changes from the enclosing Projects repository and preserve others' work.

This import is under review in feat/cad-bridge. Until its first merge and explicit
source-pointer switch, D:\CAD-Automation\upstream\dwg-mcp remains the original
development source and the installed dwg-2027 runtime remains unchanged.
Serialize changes across both locations during this transition. Never silently
switch the global instructions, replace installed DLLs or deploy during import.

The user owns Git setup, branches, staging, commits, pushes and merges. Prepare
reviewable edits, explain small steps and inspect results; no Git mutations or
remote publishing unless the specific step is explicitly delegated. Merges stay
with the user. The agent edits/builds/offline-tests; the user performs AutoCAD UI
actions and explicitly enabled live tests. No live tests merely because a build passed.

## Hosts and tools

- AutoCAD 2026 .NET 8: plugin-acad26 net8.0-windows, dwg-2026 / --target 2026.
  Use official AutoCAD.NET 25.1.0 compile references with UseAutoCadNuGetRefs=true.
  25.1.1 targets .NET 10; do not upgrade it automatically in this build.
- AutoCAD 2027: plugin-acad27 net10.0-windows, dwg-2027 / --target 2027;
  references from the installed AutoCAD 2027. Source is shared under src/shared.
- External stdio server: net8.0; packaged win-x64 EXE is self-contained.
- Enabled profile: query,modify,drawing,view, 42 tools. AutoLISP is blocked by
  upstream: never bypass that refusal through C#. No legacy COM fallback here.

Before drawing edits use dwg_get_drawing_info. It targets the active document and
provides no full-path guard; has_saved_path may refer to a DWT template, not a saved
DWG. Inspect only necessary objects. Prefer current selection when the user refers
to it; non-text selection has no typed reader, so use provided handles or a bounded
query without substituting unrelated objects. Coordinates are drawing units.

Use typed tools, including dwg_set_lineweight (explicit mm/by_layer/by_block),
dwg_set_layer_lineweight (exact supported mm), dwg_create_hatch (one Circle or
closed planar lightweight Polyline, SOLID only, ACI/RGB, optional association).
No arbitrary hatch patterns or islands. Inspect ok and per-item results. Normal
MCP text may contain a failure; batches may partially succeed and do not provide
grouped Undo. Never repeat an uncertain timed-out mutation automatically.
Never save without a request or run purge/audit as a connection check.

Routine drawing tasks require no scratch DWGs, regression runs or extra logs.
For missing general capabilities inspect existing tools/upstream and official
Autodesk examples; bounded additions belong in typed MCP methods and plugin handlers.
Trusted synchronous C# via an explicitly enabled code profile is a prototype, not
a completed reusable tool. Keep project conventions outside bridge code.

## Offline development and releases

Run the affected checks proportionately. Full .NET suite from this directory:
dotnet test upstream/dwg-mcp/tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release
Python diagnostics use .venv/Scripts/python.exe and requirements-dev.txt. Schema
checks must use the actual MCP SDK; don't infer schemas from successful compilation.
Live switches require explicit user enablement, own identifiable scratch objects,
cleanup and no saves; preserve pre-existing changes and selection.

Keep stdout for MCP protocol. Preflight before edits, use transactions, report
rollback limits and real failures; no silent headless fallback. Record source,
offline, loaded and live state separately, plus unfinished work and its next step.

tools/package-team-dwg.ps1 packages both host plugins and their matching server.
Use fresh output directories. Do not commit generated releases or Autodesk DLLs.
Existing patches document the applied import; normal subsequent changes use Git
commits, not repeated patch application. Do not copy the source/runtime into each
drawing project or work on loaded DLLs through cloud-synchronised folders.
