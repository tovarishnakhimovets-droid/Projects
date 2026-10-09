# Shared AutoCAD MCP

These are the canonical bridge rules. Source lives in the Projects repository,
`cad-bridge/`; implementation is `upstream/dwg-mcp` (v2.0.1, already patched).
On this computer the source root is `D:\CAD-Automation\team\Projects\cad-bridge`;
other computers use their configured local Projects/cad-bridge checkout.
There is no nested Git repository. Before bridge edits read that checkout's
docs/CAD_WORKFLOW.md and relevant WORK_STATUS.md section; inspect current Git
changes from the enclosing Projects repository and preserve others' work.

New runtime packages carry an exact generated copy of this file in
server/current/bridge-docs/AGENTS.md. That folder contains runtime files, not the
source checkout. Drawing tasks need only the installed matching MCP. Development
requires the configured local Projects/cad-bridge checkout; if unavailable,
report the missing source and required operation instead of creating a replacement.

Keep one implementation tree, one docs/ directory and one WORK_STATUS.md in
cad-bridge. Store release artifacts in its releases/ directory. Do not add parallel
source, documentation, status, backup or archive folders. Runtime instruction copies
are generated from this file, never maintained separately. Preserve unique project
scripts and other chats' work when cleaning the workspace.

The active local AutoCAD 2027 runtime remains
`D:\CAD-Automation\releases\dwg-2027\2026-10-06-style-tools`.
Source cleanup or a Git merge does not replace installed DLLs or switch the MCP.

The user owns Git setup, branches, staging, commits, pushes and merges. Prepare
reviewable edits, explain small steps and inspect results; no Git mutations or
remote publishing unless the specific step is explicitly delegated. Merges stay
with the user. The agent edits/builds/offline-tests; the user performs AutoCAD UI
actions and explicitly enabled live tests. No live tests merely because a build passed.
Explain newly introduced command flags next to the command in plain language.

## Hosts and tools

- AutoCAD 2026 .NET 8: plugin-acad26 net8.0-windows, dwg-2026 / --target 2026.
  Use official AutoCAD.NET 25.1.0 compile references with UseAutoCadNuGetRefs=true.
  25.1.1 targets .NET 10; do not upgrade it automatically in this build.
- AutoCAD 2027: plugin-acad27 net10.0-windows, dwg-2027 / --target 2027;
  references from the installed AutoCAD 2027. Source is shared under src/shared.
- External stdio server: net8.0; packaged win-x64 EXE is self-contained.
- Both host years are supported by the same source changes. A developer's installed
  AutoCAD year does not narrow this contract. Build both plugins for changes to
  shared code; record which native/live checks remain unavailable on that computer.
- Profile query,modify,drawing,view: installed team.2/style-tools has 42 tools;
  the unreleased source candidate has 46. Check the connected server's tools/list
  before using candidate capabilities. AutoLISP is blocked by upstream: never
  bypass that refusal through C#. No legacy COM fallback here.

Before drawing edits use dwg_get_drawing_info. The source candidate returns the
active titled DWG's document_path/fingerprint and distinguishes an unsaved document
from its DWT template. New network/proxy writes require both identity fields and
fresh object expectations; dry_run validates without writing. Contract and scope
are in docs/CAD_WORKFLOW.md. Older installed builds have no full-path guard and
may report has_saved_path for a DWT; do not infer a saved DWG from that result.
Inspect only necessary objects. Prefer current selection when the user refers
to it; non-text selection has no typed reader, so use provided handles or a bounded
query without substituting unrelated objects. Coordinates are drawing units.

Unique drawing scripts remain in `D:\CAD-Automation\scripts` and
`D:\CAD-Automation\lisp`. Trusted C# scripts must guard the active document,
finish synchronously and use a transaction. Never use C# to bypass the upstream
AutoLISP refusal. Do not load an existing sandbox.lsp as a connection smoke test.

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

## Legacy AutoCAD 2023

The separate legacy server and tools live in `D:\CAD-Automation\legacy\autocad-2023`.
Use cad-automation when available; read that directory's README.md for schemas.
Otherwise use its shared PowerShell COM bridge with Windows PowerShell 5.1
(powershell.exe), not pwsh. Sandboxed COM/ROT lookup can return MK_E_UNAVAILABLE
while AutoCAD is running; use the authorized bridge outside the sandbox.
Never route AutoCAD 2026/2027 through this legacy COM server.
Resolve the active document on every call and pass the full path returned by
status for edits. Use current PICKFIRST selection when requested; do not replace
an empty selection with unrelated objects. Legacy AutoLISP must finish without
prompts and return a unique completion/error result, not rely on a fixed delay.
Explicit live-test and save authorization rules apply to this bridge too.
Reuse `D:\CAD-Automation\.venv` locally instead of creating another environment.

## Offline development and releases

Run the affected checks proportionately. Full .NET suite from this directory:
dotnet test upstream/dwg-mcp/tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release
Local Python diagnostics reuse `D:\CAD-Automation\.venv\Scripts\python.exe` and
requirements-dev.txt in this source root. Colleagues use their own Python
environment with those dependencies; no environment is distributed in Git. Schema
checks must use the actual MCP SDK; don't infer schemas from successful compilation.
Live switches require explicit user enablement, own identifiable scratch objects,
cleanup and no saves; preserve pre-existing changes and selection.

Keep stdout for MCP protocol. Preflight before edits, use transactions, report
rollback limits and real failures; no silent headless fallback. Record source,
offline, loaded and live state separately, plus unfinished work and its next step.

tools/package-team-dwg.ps1 packages both host plugins and their matching server,
and copies these canonical rules into the runtime package without editing them.
Use fresh output directories. Do not commit generated releases or Autodesk DLLs.
Existing patches document the applied import; normal subsequent changes use Git
commits, not repeated patch application. Do not copy the source/runtime into each
drawing project or work on loaded DLLs through cloud-synchronised folders.
