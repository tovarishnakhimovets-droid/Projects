#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServerInstallRoot = (Join-Path $env:LOCALAPPDATA 'Bimwright\Dwg\server\current'),
    [ValidateSet(2026, 2027)][int]$AcadYear = 2027,
    [string]$BridgeSourceRoot
)
$ErrorActionPreference = 'Stop'
$ServerInstallRoot = [IO.Path]::GetFullPath($ServerInstallRoot)
$exe = Join-Path $ServerInstallRoot 'dwg-mcp.exe'
$instructions = Join-Path $ServerInstallRoot 'bridge-docs\AGENTS.md'
$mcpName = "dwg-$AcadYear"
foreach ($path in @($exe, $instructions)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Install the package first. Missing: $path" }
}
$releaseFile = Join-Path $ServerInstallRoot 'bridge-docs\release.json'
if (Test-Path -LiteralPath $releaseFile) {
    $releaseInfo = Get-Content -LiteralPath $releaseFile -Raw | ConvertFrom-Json
    if ($releaseInfo.packedAutocadYears -notcontains $AcadYear) { throw "This release has no AutoCAD $AcadYear plugin. Install the matching package first." }
} elseif ($AcadYear -ne 2027) { throw 'The previous 2027-only release cannot configure AutoCAD 2026. Install the new team package first.' }
if ($BridgeSourceRoot) {
    $BridgeSourceRoot = (Resolve-Path -LiteralPath $BridgeSourceRoot).Path
    foreach ($relative in @('AGENTS.md', 'docs\CAD_WORKFLOW.md', 'WORK_STATUS.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $BridgeSourceRoot $relative))) { throw "Incomplete source workspace: $BridgeSourceRoot ($relative missing)" }
    }
}
$codexDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$codexDir = [IO.Path]::GetFullPath($codexDir)
$config = Join-Path $codexDir 'config.toml'
$codexCommand = Get-Command codex -ErrorAction SilentlyContinue
$configureCodex = {
if ($codexCommand) {
    if ($PSCmdlet.ShouldProcess($config, "Back up config and register local $mcpName MCP")) {
        if (Test-Path -LiteralPath $config) {
            Copy-Item -LiteralPath $config -Destination ($config + '.before-dwg-' + [guid]::NewGuid().ToString('N') + '.bak')
        }
        & $codexCommand mcp add $mcpName --env BIMWRIGHT_DWG_READ_ONLY=false --env BIMWRIGHT_DWG_ENABLE_TOOLBAKER=false -- $exe --target $AcadYear --toolsets query,modify,drawing,view
        if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed. The preceding config backup is retained.' }
        & $codexCommand mcp get $mcpName --json
        if ($LASTEXITCODE -ne 0) { throw 'Codex could not read the registered MCP entry.' }
    }
} else {
    $tomlExe = ConvertTo-Json -InputObject $exe -Compress
    Write-Host "Codex CLI is not in PATH. Add/replace ONLY this entry in $config using the Codex settings editor:"
    Write-Host @"
[mcp_servers.$mcpName]
command = $tomlExe
args = ["--target", "$AcadYear", "--toolsets", "query,modify,drawing,view"]
enabled = true
[mcp_servers.$mcpName.env]
BIMWRIGHT_DWG_READ_ONLY = "false"
BIMWRIGHT_DWG_ENABLE_TOOLBAKER = "false"
"@
}
}

# Codex loads the override file instead of AGENTS.md when it exists.
$agentFile = Join-Path $codexDir 'AGENTS.override.md'
if (-not (Test-Path -LiteralPath $agentFile)) { $agentFile = Join-Path $codexDir 'AGENTS.md' }
$begin = '<!-- BEGIN SHARED DWG BRIDGE -->'
$end = '<!-- END SHARED DWG BRIDGE -->'
$block = @"
$begin
## Shared AutoCAD $AcadYear bridge
For AutoCAD drawing tasks, read "$instructions" before using CAD tools.
Use the local $mcpName MCP with query,modify,drawing,view, target $AcadYear.
Keep drawing conventions in the drawing project's instructions. Never save without a request.
Reusable missing operations belong in typed bridge tools; a custom scenario is a prototype.
"@
if ($BridgeSourceRoot) {
    $block += "`r`nFor bridge development read $BridgeSourceRoot\AGENTS.md, docs\CAD_WORKFLOW.md and WORK_STATUS.md. Check current changes and preserve other chats' work. The agent builds/offline-tests; the user performs AutoCAD UI actions and explicitly enabled live tests."
} else {
    $block += "`r`nThis installation contains runtime files and source patches, not a source workspace. If development is needed, report the missing source checkout and requested tool contract; do not create a second replacement server."
}
$block += "`r`n$end"
$existing = if (Test-Path -LiteralPath $agentFile) { [IO.File]::ReadAllText($agentFile) } else { '' }
$previousInstructions = $existing
$existing = $existing.Replace('<!-- BEGIN SHARED DWG 2027 -->', $begin).Replace('<!-- END SHARED DWG 2027 -->', $end)
$starts = ([regex]::Matches($existing, [regex]::Escape($begin))).Count
$ends = ([regex]::Matches($existing, [regex]::Escape($end))).Count
if ($starts -gt 1 -or $ends -gt 1 -or $starts -ne $ends) { throw "Malformed bridge instruction markers in $agentFile; preserve and repair them before rerunning." }
if ($starts -eq 1) {
    $pattern = '(?s)' + [regex]::Escape($begin) + '.*?' + [regex]::Escape($end)
    $updated = [regex]::Replace($existing, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $block })
} else { $updated = $existing.TrimEnd() + "`r`n`r`n" + $block + "`r`n" }
& $configureCodex
if ($updated -ne $previousInstructions -and $PSCmdlet.ShouldProcess($agentFile, 'Preserve existing instructions and update CAD bridge section')) {
    New-Item -ItemType Directory -Path $codexDir -Force | Out-Null
    if (Test-Path -LiteralPath $agentFile) { Copy-Item -LiteralPath $agentFile -Destination ($agentFile + '.before-dwg-' + [guid]::NewGuid().ToString('N') + '.bak') }
    [IO.File]::WriteAllText($agentFile, $updated, (New-Object Text.UTF8Encoding($false)))
}
Write-Host "Restart Codex after configuration. Then open AutoCAD $AcadYear and ask Codex to check $mcpName without drawing edits."
