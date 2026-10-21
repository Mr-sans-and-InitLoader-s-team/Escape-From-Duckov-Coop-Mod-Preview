param([string[]]$Roles = @('host','client'),[switch]$NoMod)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $repo 'artifacts\runtime-tests'
$modBuild = Join-Path $repo 'artifacts\corpse-remainder\staging\联机Mod1'
$harness = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\CoopRuntimeTests.dll'
$started = @()
foreach ($role in $Roles) {
    if ($role -notmatch '^[a-z][a-z0-9]*$') { throw 'Invalid test role' }
    $game = Join-Path $testRoot "$role\game"
    $exe = Join-Path $game 'Duckov.exe'
    $running = Get-CimInstance Win32_Process -Filter "Name='Duckov.exe'" |
        Where-Object { $_.ExecutablePath -eq $exe -and $_.ThreadCount -gt 0 }
    if ($running) { throw "Test instance is still running: $role" }
    foreach ($file in @('EscapeFromDuckovCoopMod.dll', 'EscapeFromDuckovModApi.dll')) {
        Copy-Item -LiteralPath (Join-Path $modBuild $file) -Destination (Join-Path $game "Duckov_Data\Mods\3591341282\$file") -Force
    }
    Copy-Item -LiteralPath $harness -Destination (Join-Path $game 'Duckov_Data\Managed\CoopRuntimeTests.dll') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Localization') -Destination (Join-Path $game 'Duckov_Data\Mods\3591341282') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $modBuild 'EscapeFromDuckovModApi.dll') -Destination (Join-Path $game 'Duckov_Data\Managed\EscapeFromDuckovModApi.dll') -Force
    $log = Join-Path $testRoot "$role\game.log"
    $taskGameArguments=@(
        "--coop-test-role=$role", '-screen-width', '960', '-screen-height', '540', '-screen-fullscreen', '0',
        '-batchmode', '-force-d3d11', '-logFile', $log)
    if($NoMod){$taskGameArguments+='--coop-test-no-mod'}
    $process = Start-Process -FilePath $exe -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $taskGameArguments
    $started += [pscustomobject]@{ role=$role; id=$process.Id; exe=$exe; startedUtc=[DateTime]::UtcNow.ToString('o') }
}
$processFile = Join-Path $testRoot 'processes.json'
$previous = if (Test-Path -LiteralPath $processFile) { @(Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json) } else { @() }
@($previous | Where-Object { $_.role -notin $Roles }) + $started | ConvertTo-Json | Set-Content -LiteralPath $processFile -Encoding UTF8
$started
