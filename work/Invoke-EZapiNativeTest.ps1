param(
 [ValidateSet('RunPlan','Capture','Archive','Launch','Close')][string]$Action='Capture',
 [string]$Plan,[string]$EvidenceDir='work/native-0.1.18-20261020',
 [switch]$CaptureSteps,
 [ValidateRange(0,2147483647)][int]$ExpectedProcessId=0,
 [string]$GameDir='E:\steam\steamapps\common\Probably Stolen Demo'
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
$ezEvidence=(Resolve-Path -LiteralPath $EvidenceDir).Path
# 重放刚根据截图确认的步骤。不自动重试点击或重启；PID、窗口尺寸、焦点不对时停止。
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class EzNativeInput {
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int cmd);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint source,uint target,bool attach);
 [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
 public static void FocusWindow(IntPtr hwnd) {
  uint ignored;uint current=GetCurrentThreadId();uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
  bool attached=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true);
  try {ShowWindow(hwnd,9);BringWindowToTop(hwnd);SetForegroundWindow(hwnd);}
  finally {if(attached)AttachThreadInput(current,foreground,false);}
 }
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 // 同时发送原始鼠标移动；只移动系统光标会让游戏新版输入系统沿用上一次悬停位置。
 public static void MovePointer(int x,int y) {
  mouse_event(0x8001,(uint)Math.Round(x*65535.0/1919),(uint)Math.Round(y*65535.0/1079),0,UIntPtr.Zero);
 }
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 public struct RECT {public int Left,Top,Right,Bottom;}
}
'@
Add-Type -AssemblyName System.Drawing
function Get-EzGame {
 $ezGames=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)
 if($ezGames.Count -ne 1){throw "Expected one game process; found $($ezGames.Count)."}
 if($ExpectedProcessId -ne 0 -and $ezGames[0].Id -ne $ExpectedProcessId){throw 'Game process differs from bound PID; no input sent.'}
 return $ezGames[0]
}
function Save-EzScreen([string]$Name) {
 $ezProcess=Get-EzGame
 $ezRect=New-Object EzNativeInput+RECT
 [EzNativeInput]::GetWindowRect($ezProcess.MainWindowHandle,[ref]$ezRect)|Out-Null
 $ezBitmap=New-Object Drawing.Bitmap ($ezRect.Right-$ezRect.Left),($ezRect.Bottom-$ezRect.Top)
 $ezGraphics=[Drawing.Graphics]::FromImage($ezBitmap)
 try{$ezGraphics.CopyFromScreen($ezRect.Left,$ezRect.Top,0,0,$ezBitmap.Size);$ezBitmap.Save((Join-Path $ezEvidence "$Name.png"))}
 finally{$ezGraphics.Dispose();$ezBitmap.Dispose()}
}
function Focus-EzGame {
 $ezProcess=Get-EzGame
 # 非交互PowerShell有时不拥有前台输入队列；短暂关联当前前台线程后切换到已核对的游戏窗口。
 # finally立即解除关联，随后仍核对真实焦点/窗口几何，不以API返回值代替已聚焦证据。
 [EzNativeInput]::FocusWindow($ezProcess.MainWindowHandle)
 Start-Sleep -Milliseconds 800
 if([EzNativeInput]::GetForegroundWindow() -ne $ezProcess.MainWindowHandle){throw 'Game focus unavailable; no input sent.'}
 $ezRect=New-Object EzNativeInput+RECT
 [EzNativeInput]::GetWindowRect($ezProcess.MainWindowHandle,[ref]$ezRect)|Out-Null
 # Unity启动时可能先出现带边框的最大化窗口，再切到既定全屏；只等待同一窗口稳定，不调整用户窗口。
 $ezGeometryDeadline=(Get-Date).AddSeconds(15)
 while(($ezRect.Left -ne 0 -or $ezRect.Top -ne 0 -or $ezRect.Right -ne 1920 -or $ezRect.Bottom -ne 1080) -and (Get-Date) -lt $ezGeometryDeadline) {
  if((Get-EzGame).Id -ne $ezProcess.Id){throw 'Game process changed while waiting for window geometry.'}
  if([EzNativeInput]::GetForegroundWindow() -ne $ezProcess.MainWindowHandle){throw 'Game focus changed while waiting for window geometry; no input sent.'}
  Start-Sleep -Milliseconds 250
  [EzNativeInput]::GetWindowRect($ezProcess.MainWindowHandle,[ref]$ezRect)|Out-Null
 }
 if($ezRect.Left -ne 0 -or $ezRect.Top -ne 0 -or $ezRect.Right -ne 1920 -or $ezRect.Bottom -ne 1080){throw "Plan requires 1920x1080 at 0,0; actual bounds: $($ezRect.Left),$($ezRect.Top),$($ezRect.Right),$($ezRect.Bottom). No input sent."}
 return $ezProcess.Id
}
if($Action -eq 'Launch'){
 if(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Already running; inspect existing process.'}
 Start-Process -FilePath (Join-Path $GameDir 'Probably Stolen.exe') -WorkingDirectory $GameDir -WindowStyle Hidden
 return
}
if($Action -eq 'Close'){
 $ezProcess=Get-EzGame
 $ezProcess.CloseMainWindow()|Out-Null
 if(-not $ezProcess.WaitForExit(10000)){throw 'Game did not exit within 10s; inspect before installing.'}
 return
}
if($Action -eq 'Archive'){
 if($ExpectedProcessId -ne 0){$null=Get-EzGame}
 Copy-Item -LiteralPath (Join-Path $GameDir 'MelonLoader/Latest.log') -Destination (Join-Path $ezEvidence 'Latest.log')
 Copy-Item -LiteralPath (Join-Path $GameDir 'UserData/EZapi/diagnostics.json') -Destination (Join-Path $ezEvidence 'diagnostics.json')
 return
}
$ezPid=Focus-EzGame
if($Action -eq 'Capture'){Save-EzScreen ('capture-'+(Get-Date -Format 'HHmmssfff'));return}
$ezSteps=Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
# 在发送任何输入前校验整份计划，避免前几步已操作后才发现末尾参数有误。
$ezNames=New-Object 'System.Collections.Generic.HashSet[string]'
foreach($ezStep in $ezSteps){
 if($ezStep.kind -notin @('Click','Move','RightClick','DoubleClick','Drag','Wheel','Key','Snapshot')){throw "Unknown kind $($ezStep.kind)"}
 foreach($ezField in @('name','heldName','heldProbeName')){
  if($ezField -ne 'name' -and -not $ezStep.$ezField){continue}
  $ezCandidate=[string]$ezStep.$ezField
  if($ezCandidate -notmatch '^[A-Za-z0-9_-]+$' -or -not $ezNames.Add($ezCandidate)){throw 'Evidence names must be unique and contain only letters, digits, hyphen or underscore.'}
  if(Test-Path -LiteralPath (Join-Path $ezEvidence "$ezCandidate.png")){throw "Evidence already exists: $ezCandidate"}
 }
 if($ezStep.kind -in @('Click','Move','RightClick','DoubleClick','Drag','Wheel')){
  foreach($ezField in @('x','y')){if($null -eq $ezStep.$ezField -or [double]$ezStep.$ezField -ne [int]$ezStep.$ezField){throw "Integer $ezField required."}}
  if($ezStep.x -lt 0 -or $ezStep.x -gt 1919 -or $ezStep.y -lt 0 -or $ezStep.y -gt 1079){throw 'Pointer is outside the confirmed screen.'}
 }
 if($ezStep.kind -eq 'Drag'){
  if($ezStep.heldProbeName -and -not(Test-Path -LiteralPath (Join-Path $GameDir 'Mods/EZapi.TestTools.dll'))){throw 'Held drag observation requires the separately installed test tools.'}
  if($ezStep.heldProbeName -and (Test-Path -LiteralPath (Join-Path $ezEvidence ($ezStep.heldProbeName+'.json')))){throw 'Held drag evidence already exists.'}
  foreach($ezField in @('toX','toY')){if($null -eq $ezStep.$ezField -or [double]$ezStep.$ezField -ne [int]$ezStep.$ezField){throw "Integer $ezField required."}}
  if($ezStep.toX -lt 0 -or $ezStep.toX -gt 1919 -or $ezStep.toY -lt 0 -or $ezStep.toY -gt 1079){throw 'Drag destination is outside the confirmed screen.'}
 }
 if($ezStep.kind -eq 'Key' -and ($null -eq $ezStep.vk -or [double]$ezStep.vk -ne [int]$ezStep.vk -or $ezStep.vk -lt 1 -or $ezStep.vk -gt 254)){throw 'Invalid virtual key.'}
 if($ezStep.kind -eq 'Wheel' -and ($null -eq $ezStep.delta -or [double]$ezStep.delta -ne [int]$ezStep.delta -or $ezStep.delta -lt -12000 -or $ezStep.delta -gt 12000 -or $ezStep.delta -eq 0)){throw 'Wheel requires a nonzero integer delta in -12000..12000.'}
 if($null -ne $ezStep.waitMs -and ([double]$ezStep.waitMs -ne [int]$ezStep.waitMs -or $ezStep.waitMs -lt 0 -or $ezStep.waitMs -gt 10000)){throw 'Wait must be an integer between 0 and 10000ms.'}
}
$ezJournal=Join-Path $ezEvidence 'input-journal.jsonl'
foreach($ezStep in $ezSteps){
 if((Get-EzGame).Id -ne $ezPid){throw 'Process changed; aborting plan.'}
 $null=Focus-EzGame
 $ezName=([string]$ezStep.name) -replace '[^A-Za-z0-9_-]','_'
 if([string]::IsNullOrWhiteSpace($ezName)){throw 'Named evidence step required.'}
 if(Test-Path -LiteralPath (Join-Path $ezEvidence "$ezName.png")){throw "Evidence already exists: $ezName"}
 switch($ezStep.kind){
  'Click'{
   [EzNativeInput]::MovePointer($ezStep.x,$ezStep.y);Start-Sleep -Milliseconds 250
   [EzNativeInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
   try{Start-Sleep -Milliseconds 150}finally{[EzNativeInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
  }
  'Move'{[EzNativeInput]::MovePointer($ezStep.x,$ezStep.y)}
  'Wheel'{
   [EzNativeInput]::MovePointer($ezStep.x,$ezStep.y);Start-Sleep -Milliseconds 250
   # Win32 以 DWORD 传递有符号滚轮增量，按位转换以保留负数，避免 PowerShell 数值强转溢出。
   $ezWheelBits=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$ezStep.delta),0)
   [EzNativeInput]::mouse_event(0x0800,0,0,$ezWheelBits,[UIntPtr]::Zero)
  }
  'RightClick'{
   [EzNativeInput]::MovePointer($ezStep.x,$ezStep.y);Start-Sleep -Milliseconds 250
   [EzNativeInput]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
   try{Start-Sleep -Milliseconds 150}finally{[EzNativeInput]::mouse_event(16,0,0,0,[UIntPtr]::Zero)}
  }
  'DoubleClick'{
   [EzNativeInput]::MovePointer($ezStep.x,$ezStep.y);Start-Sleep -Milliseconds 250
   for($ezClick=0;$ezClick -lt 2;$ezClick++){
    [EzNativeInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    try{Start-Sleep -Milliseconds 70}finally{[EzNativeInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
    Start-Sleep -Milliseconds 70
   }
  }
  'Drag'{
   [EzNativeInput]::MovePointer($ezStep.x,$ezStep.y);Start-Sleep -Milliseconds 250
   [EzNativeInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
   try{
    Start-Sleep -Milliseconds 350
    for($ezI=1;$ezI -le 16;$ezI++){
     [EzNativeInput]::MovePointer(($ezStep.x+($ezStep.toX-$ezStep.x)*$ezI/16),($ezStep.y+($ezStep.toY-$ezStep.y)*$ezI/16))
     Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 350
    if($ezStep.heldProbeName){
     # 独立测试工具F6为只读；在仍按住鼠标时观察真正拖拽状态，异常也必须释放鼠标。
     $ezProbeAt=[DateTimeOffset]::Now
     [EzNativeInput]::keybd_event(117,0,0,[UIntPtr]::Zero)
     try{Start-Sleep -Milliseconds 150}finally{[EzNativeInput]::keybd_event(117,0,2,[UIntPtr]::Zero)}
     Start-Sleep -Milliseconds 450
     $ezProbeFile=Join-Path $GameDir 'UserData/EZapi/TestTools/endpoint-probe.json'
     $ezHeldProbe=Get-Content -LiteralPath $ezProbeFile -Raw -Encoding UTF8|ConvertFrom-Json
     if(-not $ezHeldProbe.passed -or -not $ezHeldProbe.dragActive -or [DateTimeOffset]$ezHeldProbe.at -lt $ezProbeAt){throw 'Native held drag was not confirmed; input is not acceptance.'}
     Copy-Item -LiteralPath $ezProbeFile -Destination (Join-Path $ezEvidence ($ezStep.heldProbeName+'.json'))
    }
    if($ezStep.heldName){Save-EzScreen ([string]$ezStep.heldName)}
   }finally{[EzNativeInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
  }
  'Key'{
   [EzNativeInput]::keybd_event([byte]$ezStep.vk,0,0,[UIntPtr]::Zero)
   try{Start-Sleep -Milliseconds 150}finally{[EzNativeInput]::keybd_event([byte]$ezStep.vk,0,2,[UIntPtr]::Zero)}
  }
  'Snapshot'{}
  default{throw "Unknown kind $($ezStep.kind)"}
 }
 $ezWait=if($null -eq $ezStep.waitMs){600}else{[int]$ezStep.waitMs}
 if($ezWait -lt 0 -or $ezWait -gt 10000){throw 'Wait must be between 0 and 10000ms.'}
 Start-Sleep -Milliseconds $ezWait
 $ezScreen=$null
 if($CaptureSteps -or $ezStep.kind -eq 'Snapshot'){Save-EzScreen $ezName;$ezScreen="$ezName.png"}
 [ordered]@{at=(Get-Date).ToString('o');pid=$ezPid;step=$ezStep;screen=$ezScreen} | ConvertTo-Json -Depth 6 -Compress | Add-Content -LiteralPath $ezJournal -Encoding UTF8
}
