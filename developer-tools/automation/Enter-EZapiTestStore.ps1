param(


 [string]$GameDir='E:\steam\steamapps\common\Probably Stolen Demo',


 [string]$EvidenceDir=('work/native-entry-'+(Get-Date -Format 'yyyyMMdd-HHmmss')),


 [Parameter(Mandatory=$true)][int]$ExpectedSlot,


 [string]$ExpectedVersion='0.1.73-usability',


 [ValidateRange(30,300)][int]$StartupTimeoutSeconds=150,


 [switch]$CaptureResult


)


$ErrorActionPreference='Stop'
# Validate opt-in before launching or sending any input. The public developer
# panel supports this entry path without registering regression-only content.
$entryOptionsPath=Join-Path $GameDir 'UserData/EZapi/developer-tools.json'
if(-not(Test-Path -LiteralPath $entryOptionsPath)){throw 'Enable developer tools, restart, then run entry automation.'}
$entryOptions=Get-Content -LiteralPath $entryOptionsPath -Raw -Encoding UTF8|ConvertFrom-Json
if($entryOptions.enabled -ne $true){throw 'Developer tools must be explicitly enabled.'}



$helper=Join-Path $PSScriptRoot 'Invoke-EZapiNativeTest.ps1'


New-Item -ItemType Directory -Path $EvidenceDir -Force|Out-Null


$entryRoot=(Resolve-Path -LiteralPath $EvidenceDir).Path


$logPath=Join-Path $GameDir 'MelonLoader/Latest.log'


$diagnosticsPath=Join-Path $GameDir 'UserData/EZapi/diagnostics.json'


$entryStarted=Get-Date


$entryToken='entry-'+$entryStarted.ToString('HHmmssfff')


$entryProcess=$null


function Assert-EntryProcess {


 $live=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)


 if($live.Count -ne 1 -or $live[0].Id -ne $entryProcess.Id){throw 'Game process changed; entry aborted without restarting.'}


}


function Read-EntryDiagnostics {


 if(-not(Test-Path -LiteralPath $diagnosticsPath)){return $null}


 if((Get-Item -LiteralPath $diagnosticsPath).LastWriteTimeUtc -lt $entryProcess.StartTime.ToUniversalTime()){return $null}


 try {return Get-Content -LiteralPath $diagnosticsPath -Raw -Encoding UTF8|ConvertFrom-Json}


 catch {return $null} # 写入中的短暂 JSON 不作状态证据；等待下一次完整读取。


}


function Read-EntryLog {


 if(-not(Test-Path -LiteralPath $logPath)){return ''}


 if((Get-Item -LiteralPath $logPath).LastWriteTimeUtc -lt $entryProcess.StartTime.ToUniversalTime()){return ''}


 return Get-Content -LiteralPath $logPath -Raw -Encoding UTF8


}


function Wait-EntryState([string]$Name,[scriptblock]$Check,[int]$Seconds=45) {


 $deadline=(Get-Date).AddSeconds($Seconds)


 do {


  Assert-EntryProcess


  if(& $Check){Write-Output "Entry state: $Name";return}


  Start-Sleep -Milliseconds 350


 }while((Get-Date) -lt $deadline)


 throw "Timed out waiting for $Name; no click retry or process restart."


}


function Invoke-EntryPlan([string]$Name,[object[]]$Steps) {


 Assert-EntryProcess


 $path=Join-Path $entryRoot "$entryToken-$Name.plan.json"


 @($Steps)|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $path -Encoding UTF8


 & $helper -Action RunPlan -Plan $path -EvidenceDir $entryRoot -GameDir $GameDir -ExpectedProcessId $entryProcess.Id


}


try {


 $games=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)


 if($games.Count -eq 0){& $helper -Action Launch -EvidenceDir $entryRoot -GameDir $GameDir}


 # Steam 启动器可能先创建一个短命同名进程。尚未发送输入时，等待带窗口且已写出


 # 本次版本诊断的实际游戏进程；绑定以后若 PID 改变，仍立即停止而不自动重启。


 $bindDeadline=(Get-Date).AddSeconds($StartupTimeoutSeconds)


 do {


  $candidates=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)


  if($candidates.Count -eq 1 -and $candidates[0].MainWindowHandle -ne [IntPtr]::Zero -and (Test-Path -LiteralPath $diagnosticsPath)) {


   $candidate=$candidates[0]


   $probe=$null


   try {if((Get-Item -LiteralPath $diagnosticsPath).LastWriteTimeUtc -ge $candidate.StartTime.ToUniversalTime()){$probe=Get-Content -LiteralPath $diagnosticsPath -Raw -Encoding UTF8|ConvertFrom-Json}}catch {$probe=$null}


   if($null -ne $probe -and $probe.version -eq $ExpectedVersion){$entryProcess=$candidate;break}


  }


  Start-Sleep -Milliseconds 350


 }while((Get-Date) -lt $bindDeadline)


 if($null -eq $entryProcess){throw "No initialized current-version game window appeared within ${StartupTimeoutSeconds}s; no input sent or restart attempted."}


 Wait-EntryState 'EZapi initialized in current process' { $d=Read-EntryDiagnostics; $null -ne $d -and $d.version -eq $ExpectedVersion }


 $initial=Read-EntryDiagnostics


 if($null -eq $initial.lifecycle.run) {


  # 当前进程启动后初始化完成，固定主菜单和最近测试档路径；不保存、不创建新局。


  # 点击前由 helper 验证 PID、焦点和窗口几何，不逐步截图。


  Start-Sleep -Milliseconds 1200


  Invoke-EntryPlan 'load' @(


   @{kind='Click';name="$entryToken-load-menu";x=1212;y=674;waitMs=900},


   @{kind='Click';name="$entryToken-load-recent";x=1196;y=280;waitMs=2500}


  )


  Wait-EntryState 'framework loaded without internal fixture dependency' { (Read-EntryLog) -match 'EZapi 0[.]1[.]72 initialized through official hooks' }


  Start-Sleep -Milliseconds 1200


  Invoke-EntryPlan 'store' @(


   @{kind='Click';name="$entryToken-close-report";x=1628;y=94;waitMs=700},


   @{kind='Click';name="$entryToken-debug-toggle";x=95;y=29;waitMs=700}


  )


 } else {


  if([int]$initial.lifecycle.run.SaveSlotId -ne $ExpectedSlot){throw "Existing run is slot $($initial.lifecycle.run.SaveSlotId), expected $ExpectedSlot; no gameplay input sent."}


  if($initial.debugUi.suppressedByWorkshop -or $initial.debugUi.suppressedByGuide){throw 'A test modal is already open; finish or close it before entering the fixed store routine.'}


  if(-not $initial.debugUi.open) {


   Invoke-EntryPlan 'open-debug' @(@{kind='Click';name="$entryToken-debug-toggle";x=95;y=29;waitMs=700})


  }


 }


 $exportBefore=if(Test-Path -LiteralPath $diagnosticsPath){(Get-Item -LiteralPath $diagnosticsPath).LastWriteTimeUtc}else{[datetime]::MinValue}


 Invoke-EntryPlan 'export' @(@{kind='Click';name="$entryToken-export";x=92;y=141;waitMs=700})


 Wait-EntryState 'fresh loaded-run diagnostic' {


  $d=Read-EntryDiagnostics


  $null -ne $d -and (Get-Item -LiteralPath $diagnosticsPath).LastWriteTimeUtc -gt $exportBefore -and $null -ne $d.lifecycle.run


 }


 $current=Read-EntryDiagnostics


 if([int]$current.lifecycle.run.SaveSlotId -ne $ExpectedSlot){throw "Loaded slot $($current.lifecycle.run.SaveSlotId), expected $ExpectedSlot; stopped before any test action."}


 if($current.version -ne $ExpectedVersion -or -not $current.lifecycle.ready -or $current.errors.Count -ne 0 -or -not $current.debugUi.open -or $current.debugUi.suppressedByWorkshop -or $current.debugUi.suppressedByGuide){throw 'Loaded-run version/readiness/errors/debug panel check failed.'}


 Copy-Item -LiteralPath $diagnosticsPath -Destination (Join-Path $entryRoot "$entryToken-ready-diagnostics.json")


 if($CaptureResult){& $helper -Action Capture -EvidenceDir $entryRoot -GameDir $GameDir}


 [ordered]@{success=$true;pid=$entryProcess.Id;version=$current.version;slot=$current.lifecycle.run.SaveSlotId;run=$current.lifecycle.run.RunId;started=$entryStarted.ToString('o');finished=(Get-Date).ToString('o');routineScreenshots=[bool]$CaptureResult;scope='fixed 1920x1080 load-recent-test-slot path; log/diagnostic gates; no save request or test gameplay action'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $entryRoot "$entryToken-result.json") -Encoding UTF8


 Write-Output "Test store ready: slot $ExpectedSlot, PID $($entryProcess.Id); routine steps used no screenshots."


} catch {


 $reason=$_.Exception.Message


 [ordered]@{success=$false;reason=$reason;at=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $entryRoot "$entryToken-failure.json") -Encoding UTF8


 try {& $helper -Action Capture -EvidenceDir $entryRoot -GameDir $GameDir}catch {Write-Warning 'Could not capture failed entry state.'}


 throw $reason


}


