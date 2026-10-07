#Requires -Version 5.1
# Modified by the CAD team (2026-10-07); see patches/ and docs/IMPORT_MANIFEST.json in cad-bridge.
<#
.SYNOPSIS
  Build the end-user dwg-mcp setup ZIP (self-contained server + AutoCAD plugins that compile here).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Release',
    [string]$RepoRoot,
    [string]$Version,
    [string]$OutputDir,
    [ValidateSet(2022, 2023, 2024, 2025, 2026, 2027)]
    [int[]]$Years,
    [switch]$UseAutoCad2026NuGetRefs,

    # Package an uncommitted working tree for a throwaway test build.
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = (Resolve-Path $RepoRoot).Path

# Vendor sources can live inside a larger team repository. Find its actual
# Git root before applying the process-local safe.directory exception.
$gitRoot = $RepoRoot
while (-not (Test-Path -LiteralPath (Join-Path $gitRoot '.git'))) {
    $parent = Split-Path -Parent $gitRoot
    if (-not $parent -or $parent -eq $gitRoot) { throw 'Cannot identify enclosing Git working tree.' }
    $gitRoot = $parent
}

# A package must match a commit, otherwise testers exercise code nobody can
# identify. -AllowDirty marks the manifest instead.
$dirty = $false
$status = $null
try {
    $status = & git -c "safe.directory=$gitRoot" -C $gitRoot status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify Git working tree.' }
} catch { throw "Cannot inspect package source: $_" }
if ($status) {
    if (-not $AllowDirty) { throw 'Working tree has uncommitted changes. Commit them so the package matches a commit, or pass -AllowDirty for a throwaway test package.' }
    $dirty = $true
    Write-Warning 'Packaging a dirty working tree (-AllowDirty): manifest records dirty=true.'
}
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'build\client-setup' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

if (-not $Version) {
    $serverJsonPath = Join-Path $RepoRoot 'server.json'
    $Version = ((Get-Content -Raw -Path $serverJsonPath) | ConvertFrom-Json).version
}
if (-not $Version) { throw 'Pass -Version or set server.json version.' }

$displayVersion = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$stageRoot = Join-Path $OutputDir 'stage'
$serverStage = Join-Path $stageRoot 'server'
$bundleStage = Join-Path $stageRoot 'bundle'
$contentsStage = Join-Path $bundleStage 'Contents'

$stageRoot = [IO.Path]::GetFullPath($stageRoot)
$outputPrefix = $OutputDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $stageRoot.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path -Leaf $stageRoot) -ne 'stage') { throw "Unsafe stage path: $stageRoot" }
if (Test-Path -LiteralPath $stageRoot) {
    # An explicit empty output directory keeps releases immutable and avoids
    # recursive removal of paths supplied by the caller.
    throw "Package stage already exists: $stageRoot. Choose an empty OutputDir."
}
New-Item -ItemType Directory -Path $serverStage, $contentsStage -Force | Out-Null

Write-Host "=== dwg-mcp package-client-setup ($displayVersion) ==="

$serverProject = Join-Path $RepoRoot 'src\server\Bimwright.Dwg.Server.csproj'
& dotnet publish $serverProject -c $Config -r win-x64 --self-contained true /p:PublishSingleFile=true -o $serverStage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$published = Join-Path $serverStage 'Bimwright.Dwg.Server.exe'
$friendly = Join-Path $serverStage 'dwg-mcp.exe'
if (-not (Test-Path $published)) { throw "Missing $published" }
Move-Item $published $friendly -Force

$yearSpecs = @(
    @{ Year = 2022; Suffix = '22'; Tfm = 'net48' }
    @{ Year = 2023; Suffix = '23'; Tfm = 'net48' }
    @{ Year = 2024; Suffix = '24'; Tfm = 'net48' }
    @{ Year = 2025; Suffix = '25'; Tfm = 'net8.0-windows' }
    @{ Year = 2026; Suffix = '26'; Tfm = 'net8.0-windows' }
    @{ Year = 2027; Suffix = '27'; Tfm = 'net10.0-windows' }
)
if ($Years) { $yearSpecs = @($yearSpecs | Where-Object { $Years -contains $_.Year }) }

$packedYears = @()
foreach ($y in $yearSpecs) {
    $csproj = Join-Path $RepoRoot ("src\plugin-acad{0}\Bimwright.Dwg.Plugin.Acad{0}.csproj" -f $y.Suffix)
    Write-Host "[plugin] AutoCAD $($y.Year)"
    # Never overwrite a DLL currently loaded in AutoCAD from bin/Release.
    $outDir = Join-Path $OutputDir ("plugin-build\{0}" -f $y.Year)
    $referenceArgs = @()
    if ($y.Year -eq 2026 -and $UseAutoCad2026NuGetRefs) { $referenceArgs = @('-p:UseAutoCadNuGetRefs=true') }
    & dotnet build $csproj -c $Config --nologo -v q "-p:OutputPath=$outDir\" -p:CopyLocalLockFileAssemblies=true @referenceArgs
    if ($LASTEXITCODE -ne 0) {
        if ($Years) { throw "Requested AutoCAD $($y.Year) plugin build failed: $LASTEXITCODE" }
        Write-Warning "Skipping AutoCAD $($y.Year) (SDK/refs missing or build failed)."
        continue
    }
    $dll = Join-Path $outDir ("Bimwright.Dwg.Plugin.Acad{0}.dll" -f $y.Suffix)
    if (-not (Test-Path $dll)) { throw "Built but missing $dll" }
    $dest = Join-Path $contentsStage "$($y.Year)"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Get-ChildItem -LiteralPath $outDir -File | Where-Object {
        $_.Name -notmatch '^(accoremgd|acdbmgd|acmgd)\.dll$'
    } | Copy-Item -Destination $dest -Force
    if (Test-Path (Join-Path $outDir 'Fonts')) {
        Copy-Item (Join-Path $outDir 'Fonts') (Join-Path $dest 'Fonts') -Recurse -Force
    }
    $packedYears += $y.Year
}

if ($packedYears.Count -eq 0) { throw 'No AutoCAD plugin years compiled. Cannot ship an empty ZIP.' }

$templatePath = Join-Path $RepoRoot 'scripts\PackageContents.xml'
[xml]$manifestXml = Get-Content -Raw $templatePath
$manifestXml.ApplicationPackage.AppVersion = (($Version -replace '^v', '') -split '[-+]')[0]
$toRemove = @()
foreach ($comp in @($manifestXml.ApplicationPackage.Components)) {
    $mod = [string]$comp.ComponentEntry.ModuleName
    $keep = $false
    foreach ($yr in $packedYears) {
        if ($mod -match "/$yr/") { $keep = $true; break }
    }
    if (-not $keep) { $toRemove += $comp }
}
foreach ($comp in $toRemove) { [void]$manifestXml.ApplicationPackage.RemoveChild($comp) }
$manifestXml.Save((Join-Path $bundleStage 'PackageContents.xml'))

Copy-Item (Join-Path $RepoRoot 'scripts\install.ps1') (Join-Path $stageRoot 'install.ps1') -Force
Copy-Item (Join-Path $RepoRoot 'scripts\uninstall-all.ps1') (Join-Path $stageRoot 'uninstall.ps1') -Force
Copy-Item (Join-Path $RepoRoot 'scripts\uninstall-all.ps1') (Join-Path $stageRoot 'uninstall-all.ps1') -Force
Copy-Item (Join-Path $RepoRoot 'README.md') (Join-Path $stageRoot 'README.md') -Force
Copy-Item (Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md') (Join-Path $stageRoot 'THIRD_PARTY_NOTICES.md') -Force

function Get-Rel([string]$Root, [string]$Path) {
    return $Path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
}
# Stream the hash instead of Get-FileHash: same code path as
# install.ps1's Assert-SetupManifest, and no module dependency.
function Get-Sha256Lower([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

$commit = ''
try {
    $head = & git -c "safe.directory=$gitRoot" -C $gitRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) { $commit = $head.Trim() }
} catch { }

$files = @()
foreach ($f in @(Get-ChildItem $stageRoot -File -Recurse | Sort-Object FullName)) {
    $files += [ordered]@{ path = Get-Rel $stageRoot $f.FullName; sha256 = Get-Sha256Lower $f.FullName; bytes = $f.Length }
}

$manifest = [ordered]@{
    name = 'DwgMcp.Setup'
    version = $Version
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    commit = $commit
    dirty = $dirty
    platform = 'win-x64'
    packedAutocadYears = @($packedYears)
    supportedAutocadYears = @(2022, 2023, 2024, 2025, 2026, 2027)
    server = [ordered]@{ command = 'server/dwg-mcp.exe'; selfContained = $true; requiresDotnet = $false }
    files = $files
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stageRoot 'manifest.json') -Encoding UTF8

$setupZip = Join-Path $OutputDir ("DwgMcp.Setup-{0}-win-x64.zip" -f $displayVersion)
if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $setupZip -Force
Write-Host "Output : $setupZip"
Write-Host "Years  : $($packedYears -join ', ')"
