#Requires -Version 5.1
[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$')][string]$Version = '2.0.1-team.2', [string]$OutputDir,
      [ValidateSet(2026, 2027)][int[]]$Years = @(2026, 2027), [switch]$FinalizeOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$repo = Join-Path $root 'upstream\dwg-mcp'
if (-not $OutputDir) { $OutputDir = Join-Path $root "releases\team\$Version" }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
if (-not $OutputDir.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Team package output must be inside the CAD workspace.' }
$zip = Join-Path $OutputDir "DwgMcp.Setup-v$Version-win-x64.zip"
if (-not $FinalizeOnly -and ((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath ($zip + '.sha256')))) {
    throw 'Release ZIP or SHA256 file already exists. Choose a new version or output directory.'
}
$extras = Join-Path $root 'packaging\team'
foreach ($file in @('INSTALL-RU.md', 'setup-codex.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $extras $file))) { throw "Missing package extra: $file" }
}
$canonicalInstructions = Join-Path $root 'AGENTS.md'
if (-not (Test-Path -LiteralPath $canonicalInstructions -PathType Leaf)) { throw 'Missing canonical bridge AGENTS.md.' }
$stage = Join-Path $OutputDir 'stage'
if (-not $FinalizeOnly) {
    & (Join-Path $repo 'scripts\package-client-setup.ps1') -Config Release -RepoRoot $repo -Version $Version -OutputDir $OutputDir -Years $Years -UseAutoCad2026NuGetRefs -AllowDirty
} elseif (-not (Test-Path -LiteralPath (Join-Path $stage 'manifest.json'))) { throw 'FinalizeOnly requires a successfully built upstream package stage.' }
Copy-Item -LiteralPath (Join-Path $extras 'INSTALL-RU.md'), (Join-Path $extras 'setup-codex.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
$runtimeDocs = Join-Path $stage 'server\bridge-docs'
New-Item -ItemType Directory -Path $runtimeDocs -Force | Out-Null
Copy-Item -LiteralPath $canonicalInstructions -Destination (Join-Path $runtimeDocs 'AGENTS.md') -Force
Copy-Item -LiteralPath (Join-Path $extras 'INSTALL-RU.md'), (Join-Path $root 'docs\TEAM_SETUP.md'), (Join-Path $root 'docs\IMPORT_MANIFEST.json') -Destination $runtimeDocs -Force
$changes = Join-Path $runtimeDocs 'changes'
New-Item -ItemType Directory -Path $changes -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'patches') -Filter 'dwg-mcp-*.patch' -File | Copy-Item -Destination $changes
$manifestPath = Join-Path $stage 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($year in $Years) { if ($manifest.packedAutocadYears -notcontains $year) { throw "Package does not contain requested AutoCAD $year" } }
$manifest.version = $Version
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
