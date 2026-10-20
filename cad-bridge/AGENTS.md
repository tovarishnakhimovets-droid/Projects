# Shared AutoCAD MCP

These are the canonical bridge rules. Source is Projects/cad-bridge/dwg-mcp,
a patched upstream v2.0.1 snapshot, with no nested Git. Resolve paths relative
to the user's local cad-bridge checkout. Before bridge edits read
docs/CAD_WORKFLOW.md and the relevant WORK_STATUS.md section;
inspect current Git changes and preserve other chats' work.

## Organization and ownership

Keep one implementation in dwg-mcp/, one docs/ directory and one WORK_STATUS.md.
Reuse existing folders. No parallel sources, instruction copies, backup folders
or per-drawing servers. Runtime bridge-docs are generated exact copies from
these sources during packaging; never maintain them separately. In installed
bridge-docs, referenced docs are sibling files: read TOOLS.md, CAD_WORKFLOW.md
and TEAM_SETUP.md there; docs/... paths refer to the source checkout. Drawing-specific
conventions remain in the drawing project's instructions. Preserve unique scripts.
State substantial moves/deletions before doing them and check active runtime paths.

Each computer has its own local checkout and installed runtime. The standard
runtime is %LOCALAPPDATA%\Bimwright\Dwg\server\current. NAS holds immutable version
folders of the installer; it does not host the running shared EXE/DLL. Git updates
source, not installed binaries. Missing source is a limitation to report, not a
reason to create a replacement. The setup/development/release sequence is solely
docs/TEAM_SETUP.md.

The user owns Git setup, branches, staging, commits, pushes and merges unless
that specific step is explicitly delegated. Automatic fast-forward pulls below
are explicitly authorized; ordinary merges remain with the user. Each person uses a permanent branch;
PRs target develop. The agent prepares changes, builds and offline-tests. The user
performs AutoCAD UI actions and explicitly enabled live tests. Do not authorize
live tests from a successful build. Explain new flags beside their commands.
Serialize overlapping source edits and live operations; do not overwrite another
chat's changes or switch its shared checkout underneath it.

## Before repository edits

Once before starting a new editing task, inspect Git status, the current branch,
its upstream and any unfinished Git operation. Only on the confirmed personal
branch, with matching origin/<branch> upstream, no staged, unstaged or untracked
changes reported by Git, and no overlapping chat edits, automatically run in order:
`git pull --ff-only`, then `git pull --ff-only origin develop`.
The first updates the personal branch; the second includes freshly fetched shared
develop without switching branches or changing the personal upstream. --ff-only
allows fast-forward updates and creates no merge commit.

If prerequisites fail or either command fails, stop before new edits and explain
the exact condition. Preserve existing work; never auto-stash, rebase, reset,
force, switch branches or perform an ordinary merge. Do not pull mid-task or for
read-only/drawing-only work. Report successful updates even if the second pull
fails; do not roll them back. After updates, reread the applicable instructions
and work log. Commit, push and conflict resolution remain human-controlled.

## Hosts and CAD operations

- AutoCAD 2026: plugin-acad26 / net8.0-windows; dwg-2026, target 2026. Compile-only
  AutoCAD.NET references are 25.1.0 via UseAutoCadNuGetRefs=true; 25.1.1 targets .NET 10.
- AutoCAD/Civil 3D 2027: plugin-acad27 / net10.0-windows; dwg-2027, target 2027;
  build against installed AutoCAD 2027 assemblies.
- The external stdio server targets net8.0; the packaged EXE is self-contained.
  Shared code is dwg-mcp/src/shared. Both plugin years remain supported.
- Verify the connected server's tools/list; source availability and old counts
  do not prove installed capabilities. Standard team toolsets: query,modify,drawing,view.

Before drawing edits read dwg_get_drawing_info and only the necessary objects.
Prefer the current selection when requested; do not substitute unrelated objects
for an empty selection or an unavailable reader. Coordinates use drawing units.
Fresh full DWG path/fingerprint and object expectations are required by guarded
write tools. An unsaved DWG is not its DWT template. Existing old runtimes may
misreport has_saved_path; do not infer a saved DWG from that field alone.

Use typed MCP operations and their documented limits in docs/TOOLS.md.
Layers, styles and object expectations come from the current drawing. Preflight
before writing, use transactions, inspect top-level ok and per-item outcomes.
Batches may partially succeed; separate calls do not share atomicity or grouped
Undo. Never automatically retry an uncertain timed-out mutation: read state first.
Never save without a request, or use purge/audit as a connection check.
Routine drawing work needs no scratch DWGs, regression runs or extra logs.

Application preferences use dwg_list_settings/dwg_get_settings and guarded
setters with exact current profile and fresh expected values; dry_run first.
Crosshair model/layout RGB are independent. Preferences are outside DWG/Undo;
inspect rollback_verified and reread after failures, never retry automatically.
Catalog entries and readback do not prove native availability or visible effects.
Do not bypass read-only restrictions through registry/arbitrary code. Finish
commands and close Options first. Full contracts are in docs/TOOLS.md, sections
«Общие настройки AutoCAD» and «Настройки перекрестия»; verify runtime tools/list.

Missing general operations belong in bounded typed tools after inspecting
upstream and official Autodesk APIs/examples. A synchronous trusted C# prototype
is not a completed tool; guard the active document, finish inside the transaction,
and do not move CAD API calls to worker threads. Respect runtime refusals.
Unique local prototypes remain D:\CAD-Automation\scripts and D:\CAD-Automation\lisp;
never load sandbox.lsp as a connection test.

AutoLISP trust, launch and completion rules are in
docs/CAD_WORKFLOW.md, section «Запуск AutoLISP».
Only the user grants session trust for an exact local path/SHA256/function through
MCPTRUSTLISP. Scanner verdicts and request flags do not grant trust. queued is not
success: poll dwg_get_lisp_status to a terminal result. Interactive prompts belong
to the user. A job can leave partial changes; no rollback or sandbox is promised.
MCPDISABLELISP/MCPSTOP clear approvals; running code must be cancelled manually.
No inline, batch, ToolBaker or C# bypass of LISP restrictions. Saves still need a request.

## Verification and legacy

Run affected checks proportionately. Shared-code changes require both plugin
builds and actual MCP SDK schema checks; full .NET tests run from cad-bridge:
`dotnet test dwg-mcp/tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release`.
Python diagnostics use requirements-dev.txt and the existing local environment
(D:\CAD-Automation\.venv on this machine). Colleagues use their own environment.
Keep stdout exclusively for MCP. Record source/offline/installed/loaded/live
state separately in WORK_STATUS.md, with incomplete work and its next step.
Do not repeat successful checks or rebuild packages for documentation-only edits.
Live tests need explicit enablement, identifiable scratch objects, cleanup and no saves.

AutoCAD 2023 is separate: D:\CAD-Automation\legacy\autocad-2023. Read its README
before cad-automation work. Never route AutoCAD 2026/2027 through legacy COM.
Legacy PowerShell COM requires powershell.exe, not pwsh; resolve the active document
each call, pass the actual full path, preserve selection and await real completion.
Explicit live-test and save rules apply there too. Reuse the existing .venv.
