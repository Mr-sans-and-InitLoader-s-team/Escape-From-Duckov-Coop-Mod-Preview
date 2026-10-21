param([string[]]$Roles = @('host','client'))
$ErrorActionPreference = 'Stop'
$taskRepository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$taskSourceGame = $env:DUCKOV_GAME_DIRECTORY
$taskRunRoot = Join-Path $taskRepository 'artifacts\runtime-tests'
$taskSourceProfile = Join-Path $env:USERPROFILE 'AppData\LocalLow\TeamSoda\Duckov'
$taskTestDll = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\CoopRuntimeTests.dll'
New-Item -ItemType Directory -Path $taskRunRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $taskRunRoot 'original-save-hashes.json'))) {
Get-ChildItem -LiteralPath (Join-Path $taskSourceProfile 'Saves') -File | ForEach-Object {
    [pscustomobject]@{ Path=$_.FullName; SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRunRoot 'original-save-hashes.json') -Encoding utf8
}
Add-Type -Path (Join-Path $taskSourceGame 'BepInEx\core\Mono.Cecil.dll')
foreach ($taskRole in $Roles) {
    if ($taskRole -notmatch '^[a-z][a-z0-9]*$') { throw 'Invalid test role' }
    $taskRoleRoot = Join-Path $taskRunRoot $taskRole
    $taskGame = Join-Path $taskRoleRoot 'game'
    if (Test-Path -LiteralPath $taskGame) { throw "Test game already exists: $taskGame" }
    New-Item -ItemType Directory -Path $taskGame | Out-Null
    foreach ($taskName in @('Duckov.exe','UnityPlayer.dll','UnityCrashHandler64.exe','WinPixEventRuntime.dll','steam_appid.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskSourceGame $taskName))) {
            if ($taskName -eq 'WinPixEventRuntime.dll') { continue }
            if ($taskName -eq 'steam_appid.txt') {
                Set-Content -LiteralPath (Join-Path $taskGame $taskName) -Value '3167020' -Encoding ascii
                continue
            }
        }
        New-Item -ItemType HardLink -Path (Join-Path $taskGame $taskName) -Target (Join-Path $taskSourceGame $taskName) | Out-Null
    }
    New-Item -ItemType Junction -Path (Join-Path $taskGame 'MonoBleedingEdge') -Target (Join-Path $taskSourceGame 'MonoBleedingEdge') | Out-Null
    $taskData = Join-Path $taskGame 'Duckov_Data'
    New-Item -ItemType Directory -Path $taskData | Out-Null
    foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskSourceGame 'Duckov_Data') -File) {
        New-Item -ItemType HardLink -Path (Join-Path $taskData $taskFile.Name) -Target $taskFile.FullName | Out-Null
    }
    foreach ($taskDir in Get-ChildItem -LiteralPath (Join-Path $taskSourceGame 'Duckov_Data') -Directory) {
        if ($taskDir.Name -in @('Managed','Mods')) { continue }
        New-Item -ItemType Junction -Path (Join-Path $taskData $taskDir.Name) -Target $taskDir.FullName | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $taskSourceGame 'Duckov_Data\Managed') -Destination (Join-Path $taskData 'Managed') -Recurse
    $taskModDir = Join-Path $taskData 'Mods\3591341282'
    New-Item -ItemType Directory -Path $taskModDir -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $taskSourceGame 'Duckov_Data\Mods\3591341282') | Copy-Item -Destination $taskModDir -Recurse
    Copy-Item -LiteralPath (Join-Path $taskRepository 'artifacts\corpse-remainder\staging\联机Mod1\EscapeFromDuckovCoopMod.dll') -Destination (Join-Path $taskModDir 'EscapeFromDuckovCoopMod.dll') -Force
    $taskManaged = Join-Path $taskData 'Managed'
    Copy-Item -LiteralPath $taskTestDll -Destination $taskManaged
    Copy-Item -LiteralPath (Join-Path $taskRepository 'Shared\0Harmony.dll') -Destination $taskManaged
    Copy-Item -LiteralPath (Join-Path $taskRepository 'Shared\LiteNetLib.dll') -Destination $taskManaged
    Copy-Item -LiteralPath (Join-Path $taskModDir 'EscapeFromDuckovModApi.dll') -Destination $taskManaged
    $taskProfile = Join-Path $taskRoleRoot 'profile'
    New-Item -ItemType Directory -Path (Join-Path $taskProfile 'Saves') -Force | Out-Null
    foreach ($taskName in @('Save_1.sav','Global.json','Mods.ES3','Options.ES3')) {
        Copy-Item -LiteralPath (Join-Path $taskSourceProfile ('Saves\' + $taskName)) -Destination (Join-Path $taskProfile 'Saves')
    }
    $taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    $taskResolver.AddSearchDirectory($taskManaged)
    $taskParameters = [Mono.Cecil.ReaderParameters]::new()
    $taskParameters.AssemblyResolver = $taskResolver
    $taskCorePath = Join-Path $taskManaged 'UnityEngine.CoreModule.dll'
    $taskCore = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskCorePath, $taskParameters)
    $taskCoreMain = $taskCore.MainModule
    $taskApp = $taskCoreMain.Types | Where-Object FullName -eq 'UnityEngine.Application'
    $taskPersistent = $taskApp.Methods | Where-Object Name -eq 'get_persistentDataPath'
    $taskPersistent.ImplAttributes = [Mono.Cecil.MethodImplAttributes]::IL
    $taskPersistent.IsPInvokeImpl = $false
    $taskPersistent.Body = [Mono.Cecil.Cil.MethodBody]::new($taskPersistent)
    $taskPersistent.Body.Instructions.Add([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ldstr, $taskProfile))
    $taskPersistent.Body.Instructions.Add([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $taskTests = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskManaged 'CoopRuntimeTests.dll'), $taskParameters)
    $taskBootstrap = ($taskTests.MainModule.Types | Where-Object FullName -eq 'CoopRuntimeTests.Bootstrap').Methods | Where-Object Name -eq 'Ensure'
    $taskEnsureRef = $taskCoreMain.ImportReference($taskBootstrap)
    $taskGameObjectType = $taskCoreMain.Types | Where-Object FullName -eq 'UnityEngine.GameObject'
    foreach ($taskCtor in $taskGameObjectType.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.HasBody }) {
        $taskIL = $taskCtor.Body.GetILProcessor()
        foreach ($taskRet in @($taskCtor.Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ret })) {
            $taskIL.InsertBefore($taskRet, [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Call, $taskEnsureRef))
        }
    }
    $taskCore.Write($taskCorePath + '.test')
    $taskCore.Dispose()
    $taskTests.Dispose()
    $taskResolver.Dispose()
    if (-not ([IO.Path]::GetFullPath($taskCorePath).StartsWith([IO.Path]::GetFullPath($taskRunRoot) + [IO.Path]::DirectorySeparatorChar))) { throw 'Clone path escaped test workspace' }
    Move-Item -LiteralPath ($taskCorePath + '.test') -Destination $taskCorePath -Force
    New-Item -ItemType Directory -Path (Join-Path $taskRoleRoot 'commands'),(Join-Path $taskRoleRoot 'results') -Force | Out-Null
    Write-Output "Prepared isolated $taskRole at $taskGame"
}
