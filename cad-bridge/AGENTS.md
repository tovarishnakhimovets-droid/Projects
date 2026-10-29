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

The agent manages Git for authorized bridge work: setup, branches, staging,
commits, fetch/pull, pushes, PRs, ordinary merges and conflict resolution.
The user has given standing authorization; do not ask again for each Git step
or hand routine Git work back to the user. Each person uses a permanent branch;
PRs target develop. The agent prepares changes, reviews, builds and offline-tests. The user
performs AutoCAD UI actions and explicitly enabled live tests. Do not authorize
live tests from a successful build. Explain new flags beside their commands.
Serialize overlapping source edits and live operations; do not overwrite another
chat's changes or switch its shared checkout underneath it.

## Before repository edits

Once before starting a new editing task, inspect Git status, branches, upstreams,
worktrees and any unfinished Git operation. Use the confirmed personal branch
with matching origin/<branch> upstream and no overlapping chat edits. Continue
your own unfinished authorized work without treating it as a new task; commit
only reviewed changes belonging to that work. Never include another chat's
unfinished changes just to make the checkout clean. Before updates, require
no staged, unstaged or untracked changes. Local develop must exist, track origin/develop and
not be checked out in another worktree. Then automatically run in order:
`git pull --ff-only`, `git pull --ff-only origin develop`, then
`git fetch origin refs/heads/develop:refs/heads/develop`.
The pulls update the personal branch and include shared develop; --ff-only allows
only fast-forward updates without a merge commit. The fetch advances local develop
without switching branches or changing the personal upstream: left of the colon
is the remote branch, right is the local branch. Never add + or force options.
Finally require `git merge-base --is-ancestor develop HEAD` to succeed: local
develop must be included in the personal branch even if GitHub changed between
requests. This command only checks ancestry; it does not merge.

If fast-forward is impossible, inspect both histories and perform an ordinary
merge on a clean, unshared checkout when the intended result is clear. Resolve
unambiguous conflicts, review the merged diff and run affected checks. Ask only
when intent, ownership or conflict resolution is genuinely unclear, or access
requires the user's participation. Preserve existing work; never auto-stash,
rebase, reset or force to bypass a failed check. Switch branches only after
checking cleanliness, worktree ownership and other chats' active paths.
Do not pull during unfinished edits or for read-only/drawing-only work; fetch
and integrate as needed after committing the work for publication/release.
Report completed updates if a later step fails; do not roll them back. After
updates, reread the applicable instructions and work log. The agent also
completes the Git publication and merge cycle; it is not a human handoff gate.

## Hosts and CAD operations

- AutoCAD 2026: plugin-acad26 / net8.0-windows; dwg-2026, target 2026. Compile-only
  AutoCAD.NET references are 25.1.0 via UseAutoCadNuGetRefs=true; 25.1.1 targets .NET 10.
- AutoCAD/Civil 3D 2027: plugin-acad27 / net10.0-windows; dwg-2027, target 2027;
  build against installed AutoCAD 2027 assemblies.
- The external stdio server targets net8.0; the packaged EXE is self-contained.
  Shared code is dwg-mcp/src/shared. Both plugin years remain supported.
- Verify the connected server's tools/list; source availability and old counts
  do not prove installed capabilities. Standard team toolsets: query,modify,drawing,view.

For several open DWGs, use dwg_list_documents and dwg_use_document with a fresh
document_id. This selects a target in this stdio server process without switching
the user's tab; separate server processes keep separate targets. Only the eight
database readers listed in docs/TOOLS.md support an inactive target. Other CAD
operations require that target to be the active tab; never switch or fall back
silently. Closed/reopened documents or listener restarts invalidate IDs. Omit the
ID or pass null to follow the active tab again. Keep handles and identity with
their own DWG; reread identity after changing the target.

Before drawing edits read dwg_get_drawing_info and only the necessary objects.
When the user refers to selected/highlighted objects, first call dwg_get_selection
with include_geometry=false in the user's active tab; reset an inactive target
with dwg_use_document(null) first. Use its fresh handles for further reading/writing.
Report empty/disabled/unavailable selection; do not substitute all objects, a search
or an earlier selection. A truncated or incomplete response describes only a subset;
never present its type counts as the entire selection. dwg_get_selected_texts is
for text extraction/grouping, not identification of arbitrary selected objects.
Coordinates use drawing units.
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

Before adding code, follow docs/CAD_WORKFLOW.md, section «Выбор способа доработки»:
reuse existing tools/instructions, add a bounded typed CAD operation, or create
a standalone AutoLISP command. Reuse alone does not require converting LISP
to MCP; keep one implementation. Inspect upstream and official Autodesk APIs/examples
when extending the bridge. A synchronous trusted C# prototype
is not a completed tool; guard the active document, finish inside the transaction,
and do not move CAD API calls to worker threads. Respect runtime refusals.
Shared standalone AutoLISP source is lisp/ in the local cad-bridge checkout.
Before creating or editing it, read docs/CAD_WORKFLOW.md, section
«Создание и обновление AutoLISP». Share changes through Git and run the local
checkout file; do not maintain NAS or installed-runtime copies. LISP-only changes
need no server/plugin rebuild or installer update.
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
