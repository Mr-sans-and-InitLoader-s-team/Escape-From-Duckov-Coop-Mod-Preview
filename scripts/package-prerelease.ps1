param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$buildInfo = Get-Content -LiteralPath (Join-Path $repoRoot 'EscapeFromDuckovCoopMod/BuildInfo.cs') -Raw
$version = [regex]::Match($buildInfo, 'ModVersion\s*=\s*"([^"]+)"').Groups[1].Value
if ($version -notmatch '-Pre$') { throw 'This script only packages Pre builds.' }
if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory 'Duckov_Data/Managed/TeamSoda.Duckov.Core.dll'))) {
    throw 'GameDirectory must contain Duckov_Data/Managed/TeamSoda.Duckov.Core.dll.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("artifacts/releases/$version/" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Choose a new output directory to avoid including stale files.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$buildRoot = Join-Path $outputRoot 'build'
$logPath = Join-Path $outputRoot 'build.log'
& dotnet build (Join-Path $repoRoot 'EscapeFromDuckovCoopMod.sln') -c Release "-p:DUCKOV_GAME_DIRECTORY=$GameDirectory" "-p:DUCKOV_MODS_DIRECTORY=$buildRoot" --nologo -v:minimal *> $logPath
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $logPath -Tail 40; throw 'Release build failed.' }

$stageRoot = Join-Path $outputRoot 'stage'
$modRoot = Join-Path $stageRoot '联机Mod1'
New-Item -ItemType Directory -Path (Join-Path $modRoot 'Localization') -Force | Out-Null
# Package by allowlist. Never copy the user's installed mod directory or Config.
foreach ($name in @('EscapeFromDuckovCoopMod.dll', 'EscapeFromDuckovModApi.dll')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot "联机Mod1/$name") -Destination $modRoot
}
$dllInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $modRoot 'EscapeFromDuckovCoopMod.dll'))
if ($dllInfo.ProductVersion -ne $version) { throw 'Compiled DLL does not match the package version.' }
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Localization') -Filter '*.json' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modRoot 'Localization')
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/info.ini') -Destination $modRoot
foreach ($name in @('LICENSE.txt', 'LICENSE_RESTRICTIONS.txt')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $modRoot
}

$checksums = Get-ChildItem -LiteralPath $modRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($modRoot, $_.FullName).Replace('\', '/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
}
[IO.File]::WriteAllLines((Join-Path $modRoot 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
$zipPath = Join-Path $outputRoot "EscapeFromDuckovCoopMod-$version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stageRoot, $zipPath)
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries | Where-Object { $_.FullName -match '(^|/)Config(/|$)|\.pdb$|(^|/)(bin|obj|saves?)(/|$)' }) {
        throw 'Package contains an excluded path.'
    }
    $fileCount = @($archive.Entries | Where-Object { $_.Name }).Count
    if ($fileCount -ne 13) { throw "Unexpected package file count: $fileCount" }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), "$zipHash  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Version=$version;Archive=$zipPath;SHA256=$zipHash;Files=$fileCount;BuildLog=$logPath} | ConvertTo-Json -Compress
