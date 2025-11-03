param(
    [string]$ProjectDir
)

$buildInfoPath = Join-Path $ProjectDir "EscapeFromDuckovCoopMod\BuildInfo.cs"

if (-not (Test-Path $buildInfoPath)) {
    Write-Warning "BuildInfo.cs not found at: $buildInfoPath"
    exit 0
}

$hash = (git log -1 --format="%h" 2>$null)
if ([string]::IsNullOrEmpty($hash)) {
    $hash = 'dev'
}

$content = Get-Content $buildInfoPath -Raw
$newContent = $content -replace '(?<=internal const string GitCommit = ").*?(?=";)', $hash

if ($content -ne $newContent) {
    Set-Content $buildInfoPath $newContent -NoNewline
    Write-Host "Updated GitCommit to: $hash"
} else {
    Write-Host "GitCommit already up to date: $hash"
}

