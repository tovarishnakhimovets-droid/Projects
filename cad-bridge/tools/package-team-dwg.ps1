#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$')][string]$Version, [string]$OutputDir,
      [ValidateSet(2026, 2027)][int[]]$Years = @(2026, 2027), [switch]$FinalizeOnly, [switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$repo = Join-Path $root 'dwg-mcp'
if (-not $OutputDir) { $OutputDir = Join-Path $root "releases\$Version" }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
if (-not $OutputDir.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Team package output must be inside the CAD workspace.' }
$zip = Join-Path $OutputDir "DwgMcp.Setup-v$Version-win-x64.zip"
if ((Test-Path -LiteralPath ($zip + '.sha256')) -or (-not $FinalizeOnly -and (Test-Path -LiteralPath $zip))) {
    throw 'Release ZIP or SHA256 file already exists. Choose a new version or output directory.'
}
$extras = Join-Path $root 'packaging'
foreach ($file in @('INSTALL-RU.md', 'setup-codex.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $extras $file))) { throw "Missing package extra: $file" }
}
$canonicalInstructions = Join-Path $root 'AGENTS.md'
if (-not (Test-Path -LiteralPath $canonicalInstructions -PathType Leaf)) { throw 'Missing canonical bridge AGENTS.md.' }
$runtimeDocNames = @('TEAM_SETUP.md', 'IMPORT_MANIFEST.json', 'CAD_WORKFLOW.md', 'TOOLS.md')
$runtimeDocPaths = @($runtimeDocNames | ForEach-Object {
    $docPath = Join-Path $root "docs\$_"
    if (-not (Test-Path -LiteralPath $docPath -PathType Leaf)) { throw "Missing runtime documentation: $_" }
    $docPath
})
$gitRoot = $root
while (-not (Test-Path -LiteralPath (Join-Path $gitRoot '.git'))) {
    $parent = Split-Path -Parent $gitRoot
    if (-not $parent -or $parent -eq $gitRoot) { throw 'Cannot identify enclosing Git working tree.' }
    $gitRoot = $parent
}
function Get-PackageSourceState {
    $head = & git -c "safe.directory=$gitRoot" -C $gitRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $head) { throw 'Cannot identify package source commit.' }
    $changes = & git -c "safe.directory=$gitRoot" -C $gitRoot status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect package source working tree.' }
    [pscustomobject]@{ Commit = $head.Trim(); Dirty = [bool]$changes }
}
$sourceBefore = Get-PackageSourceState
if ($sourceBefore.Dirty -and -not $AllowDirty) {
    throw 'Working tree has uncommitted changes. Commit release preparation first, or use -AllowDirty for a throwaway test package.'
}
$stage = Join-Path $OutputDir 'stage'
if (-not $FinalizeOnly) {
    & (Join-Path $repo 'scripts\package-client-setup.ps1') -Config Release -RepoRoot $repo -Version $Version -OutputDir $OutputDir -Years $Years -UseAutoCad2026NuGetRefs -AllowDirty:$AllowDirty
} elseif (-not (Test-Path -LiteralPath (Join-Path $stage 'manifest.json'))) { throw 'FinalizeOnly requires a successfully built upstream package stage.' }
$manifestPath = Join-Path $stage 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$sourceAfter = Get-PackageSourceState
if ($sourceAfter.Commit -ne $sourceBefore.Commit -or $manifest.commit -ne $sourceAfter.Commit) {
    throw 'Package stage does not match the current source commit. Build a new package; do not relabel existing binaries.'
}
if ($manifest.version -ne $Version) { throw 'Package stage version does not match the requested version.' }
if ($manifest.dirty -isnot [bool]) { throw 'Package stage has no valid source dirty flag.' }
$packageDirty = $sourceBefore.Dirty -or $sourceAfter.Dirty -or $manifest.dirty
if ($packageDirty -and -not $AllowDirty) {
    throw 'Source or package stage is dirty. Build from committed sources, or use -AllowDirty for a throwaway test package.'
}
foreach ($year in $Years) { if ($manifest.packedAutocadYears -notcontains $year) { throw "Package does not contain requested AutoCAD $year" } }
$manifest.dirty = [bool]$packageDirty
Copy-Item -LiteralPath (Join-Path $extras 'INSTALL-RU.md'), (Join-Path $extras 'setup-codex.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
@"
# DWG MCP - team release $Version

Start with [INSTALL-RU.md](INSTALL-RU.md).
Package contents, AutoCAD years, source commit and file SHA256 hashes: [manifest.json](manifest.json).
Shared operating rules: [server/bridge-docs/AGENTS.md](server/bridge-docs/AGENTS.md).
"@ | Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding UTF8
$runtimeDocs = Join-Path $stage 'server\bridge-docs'
New-Item -ItemType Directory -Path $runtimeDocs -Force | Out-Null
Copy-Item -LiteralPath $canonicalInstructions -Destination (Join-Path $runtimeDocs 'AGENTS.md') -Force
Copy-Item -LiteralPath (Join-Path $extras 'INSTALL-RU.md') -Destination $runtimeDocs -Force
Copy-Item -LiteralPath $runtimeDocPaths -Destination $runtimeDocs -Force
$changes = Join-Path $runtimeDocs 'changes'
New-Item -ItemType Directory -Path $changes -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'patches') -Filter 'dwg-mcp-*.patch' -File | Copy-Item -Destination $changes
[ordered]@{ version = $Version; packedAutocadYears = @($manifest.packedAutocadYears); mcpVersion = '2.0.1' } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'server\bridge-docs\release.json') -Encoding UTF8
[xml]$bundleXml = Get-Content -LiteralPath (Join-Path $stage 'bundle\PackageContents.xml') -Raw
$bundleXml.ApplicationPackage.AppVersion = ($Version -split '[-+]')[0]
$bundleXml.Save((Join-Path $stage 'bundle\PackageContents.xml'))
$manifest | Add-Member -NotePropertyName distribution -NotePropertyValue 'CAD team; upstream MCP version remains 2.0.1' -Force
$manifest.files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.FullName -ne $manifestPath } | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($stage.Length).TrimStart('\').Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length }
})
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', "$hash  $([IO.Path]::GetFileName($zip))`r`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Team ZIP: $zip"
Write-Host "SHA256: $hash"
