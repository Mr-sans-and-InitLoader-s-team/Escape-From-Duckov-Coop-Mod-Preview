param([int]$TargetProcessId,[int]$TargetThreadId,[string]$OutputPath)
$ErrorActionPreference='Stop'
$taskProcess=Get-Process -Id $TargetProcessId
$taskModules=@($taskProcess.Modules | ForEach-Object { @{Name=$_.ModuleName;Start=$_.BaseAddress.ToInt64();End=$_.BaseAddress.ToInt64()+$_.ModuleMemorySize} })
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeStackProbe {
 [DllImport("kernel32",SetLastError=true)] public static extern IntPtr OpenProcess(uint access,bool inherit,int id);
 [DllImport("kernel32",SetLastError=true)] public static extern IntPtr OpenThread(uint access,bool inherit,int id);
 [DllImport("kernel32",SetLastError=true)] public static extern uint SuspendThread(IntPtr thread);
 [DllImport("kernel32",SetLastError=true)] public static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32",SetLastError=true)] public static extern bool GetThreadContext(IntPtr thread,IntPtr context);
 [DllImport("kernel32",SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] data,int size,out IntPtr read);
 [DllImport("kernel32")] public static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] public static extern IntPtr LoadLibrary(string path);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr library,string name);
 [DllImport("dbghelp",CharSet=CharSet.Ansi,SetLastError=true)] public static extern bool SymInitialize(IntPtr process,string path,bool invade);
 [DllImport("dbghelp",SetLastError=true)] public static extern bool SymCleanup(IntPtr process);
 [DllImport("dbghelp",SetLastError=true)] public static extern bool SymFromAddr(IntPtr process,ulong address,out ulong displacement,IntPtr symbol);
 [DllImport("dbghelp",SetLastError=true)] public static extern bool StackWalk64(uint machine,IntPtr process,IntPtr thread,IntPtr frame,IntPtr context,IntPtr read,IntPtr function,IntPtr module,IntPtr translate);
}
'@
$taskProcessHandle=[NativeStackProbe]::OpenProcess(0x0410,$false,$TargetProcessId)
$taskThreadHandle=[NativeStackProbe]::OpenThread(0x004A,$false,$TargetThreadId)
if($taskProcessHandle -eq [IntPtr]::Zero -or $taskThreadHandle -eq [IntPtr]::Zero){throw 'Unable to open exact test process/thread'}
$taskRaw=[Runtime.InteropServices.Marshal]::AllocHGlobal(1280)
$taskContext=[IntPtr](($taskRaw.ToInt64()+15) -band -16L)
$taskFrame=[Runtime.InteropServices.Marshal]::AllocHGlobal(1024)
$taskSymbol=[Runtime.InteropServices.Marshal]::AllocHGlobal(2136)
$taskLibrary=[NativeStackProbe]::LoadLibrary('dbghelp.dll')
$taskFunction=[NativeStackProbe]::GetProcAddress($taskLibrary,'SymFunctionTableAccess64')
$taskModule=[NativeStackProbe]::GetProcAddress($taskLibrary,'SymGetModuleBase64')
$taskFrames=@();$taskSuspended=$false
try {
 [NativeStackProbe]::SymInitialize($taskProcessHandle,$null,$true)|Out-Null
 if([NativeStackProbe]::SuspendThread($taskThreadHandle) -eq [uint32]::MaxValue){throw 'Suspend failed'}
 $taskSuspended=$true
 for($i=0;$i -lt 1232;$i++){[Runtime.InteropServices.Marshal]::WriteByte($taskContext,$i,0)}
 [Runtime.InteropServices.Marshal]::WriteInt32($taskContext,48,0x0010001F)
 if(-not [NativeStackProbe]::GetThreadContext($taskThreadHandle,$taskContext)){throw 'GetThreadContext failed'}
 $taskStackPointer=[Runtime.InteropServices.Marshal]::ReadInt64($taskContext,152)
 $taskStackBytes=New-Object byte[] 65536
 $taskBytesRead=[IntPtr]::Zero
 if([NativeStackProbe]::ReadProcessMemory($taskProcessHandle,[IntPtr]$taskStackPointer,$taskStackBytes,$taskStackBytes.Length,[ref]$taskBytesRead)){
  $taskRawAddresses=@()
  for($i=0;$i -lt $taskStackBytes.Length;$i+=8){
   $taskCandidate=[BitConverter]::ToInt64($taskStackBytes,$i)
   if($taskCandidate -gt 0x100000000L -and $taskCandidate -lt 0x7FF000000000L){$taskRawAddresses+=[pscustomobject]@{Address=('{0:X16}' -f $taskCandidate);Module=$null;StackOffset=$i}}
  }
  $taskRawAddresses | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($OutputPath,'raw.json')) -Encoding UTF8
 }
 $taskR8=[Runtime.InteropServices.Marshal]::ReadInt64($taskContext,184)
 $taskObjectAttributes=New-Object byte[] 48
 $taskBytesRead=[IntPtr]::Zero
 if([NativeStackProbe]::ReadProcessMemory($taskProcessHandle,[IntPtr]$taskR8,$taskObjectAttributes,48,[ref]$taskBytesRead)){
  $taskObjectName=[BitConverter]::ToInt64($taskObjectAttributes,16)
  $taskUnicode=New-Object byte[] 16
  if([NativeStackProbe]::ReadProcessMemory($taskProcessHandle,[IntPtr]$taskObjectName,$taskUnicode,16,[ref]$taskBytesRead)){
   $taskLength=[BitConverter]::ToUInt16($taskUnicode,0)
   if($taskLength -gt 0 -and $taskLength -lt 32768){
    $taskPathBytes=New-Object byte[] $taskLength
    if([NativeStackProbe]::ReadProcessMemory($taskProcessHandle,[IntPtr][BitConverter]::ToInt64($taskUnicode,8),$taskPathBytes,$taskLength,[ref]$taskBytesRead)){
     Write-Output ('Native file call: '+[Text.Encoding]::Unicode.GetString($taskPathBytes))
    }
   }
  }
 }
 for($i=0;$i -lt 1024;$i++){[Runtime.InteropServices.Marshal]::WriteByte($taskFrame,$i,0)}
 foreach($taskPair in @(@(0,248),@(32,160),@(48,152))){
  [Runtime.InteropServices.Marshal]::WriteInt64($taskFrame,$taskPair[0],[Runtime.InteropServices.Marshal]::ReadInt64($taskContext,$taskPair[1]))
  [Runtime.InteropServices.Marshal]::WriteInt32($taskFrame,$taskPair[0]+12,3)
 }
 for($i=0;$i -lt 40;$i++){
  $taskAddress=[Runtime.InteropServices.Marshal]::ReadInt64($taskFrame,0)
  if($taskAddress -eq 0){break}
  $taskMatch=$taskModules|Where-Object {$taskAddress -ge $_.Start -and $taskAddress -lt $_.End}|Select-Object -First 1
  [Runtime.InteropServices.Marshal]::WriteInt32($taskSymbol,0,88)
  [Runtime.InteropServices.Marshal]::WriteInt32($taskSymbol,80,2048)
  [ulong]$taskDisplacement=0
  $taskName=if([NativeStackProbe]::SymFromAddr($taskProcessHandle,[ulong]$taskAddress,[ref]$taskDisplacement,$taskSymbol)){[Runtime.InteropServices.Marshal]::PtrToStringAnsi([IntPtr]($taskSymbol.ToInt64()+84))}else{$null}
  $taskFrames+=[pscustomobject]@{Address=('{0:X16}' -f $taskAddress);Module=$taskMatch.Name;Offset=if($taskMatch){'{0:X}' -f ($taskAddress-$taskMatch.Start)}else{$null};Symbol=$taskName;Displacement=$taskDisplacement}
  if(-not [NativeStackProbe]::StackWalk64(0x8664,$taskProcessHandle,$taskThreadHandle,$taskFrame,$taskContext,[IntPtr]::Zero,$taskFunction,$taskModule,[IntPtr]::Zero)){break}
 }
} finally {
 if($taskSuspended){[NativeStackProbe]::ResumeThread($taskThreadHandle)|Out-Null}
 [NativeStackProbe]::SymCleanup($taskProcessHandle)|Out-Null
 [NativeStackProbe]::CloseHandle($taskThreadHandle)|Out-Null
 [NativeStackProbe]::CloseHandle($taskProcessHandle)|Out-Null
 [Runtime.InteropServices.Marshal]::FreeHGlobal($taskRaw)
 [Runtime.InteropServices.Marshal]::FreeHGlobal($taskFrame)
 [Runtime.InteropServices.Marshal]::FreeHGlobal($taskSymbol)
}
$taskFrames | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$taskFrames | Format-Table -AutoSize
