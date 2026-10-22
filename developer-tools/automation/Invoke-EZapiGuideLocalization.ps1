#requires -Version 5.1
<#
.SYNOPSIS
Run opt-in en/zh UI fixtures in one explicitly bound disposable save.
已进入的测试档中批量检查中英展示；不启动游戏、不保存。
#>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$GameDir,
 [Parameter(Mandatory=$true)][string]$EvidenceDir,
 [Parameter(Mandatory=$true)][ValidateRange(0,2147483647)][int]$ExpectedSlot,
 [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ExpectedRunId,
 [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ExpectedVersion,
 [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedDllSHA256,
 [switch]$CaptureResult
)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
$options=Get-Content -LiteralPath (Join-Path $GameDir 'UserData/EZapi/developer-tools.json') -Raw -Encoding UTF8|ConvertFrom-Json
$session=Get-Content -LiteralPath (Join-Path $GameDir 'UserData/EZapi/regression-session.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($options.enabled -ne $true -or $options.regressionFixtures -ne $true){throw 'Enable both developer tools and regressionFixtures, bind a disposable run, then restart.'}
if($session.Slot -ne $ExpectedSlot -or $session.RunId -cne $ExpectedRunId){throw 'Regression-session binding differs from the requested disposable save.'}
if((Get-FileHash -LiteralPath (Join-Path $GameDir 'Mods/EZapi.dll') -Algorithm SHA256).Hash -ne $ExpectedDllSHA256){throw 'Installed DLL hash differs from ExpectedDllSHA256.'}
$games=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)
if($games.Count -ne 1){throw 'One already-loaded game process is required; no launch or restart performed.'}
$boundPid=$games[0].Id
if([IO.Path]::GetFullPath($games[0].Path) -ne [IO.Path]::GetFullPath((Join-Path $GameDir 'Probably Stolen.exe'))){throw 'Running game path differs from GameDir.'}
$processStart=$games[0].StartTime.ToUniversalTime()
$helper=Join-Path $PSScriptRoot 'Invoke-EZapiNativeTest.ps1'
$diagnostic=Join-Path $GameDir 'UserData/EZapi/diagnostics.json'
$first=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json
$boundRun=$first.lifecycle.run
if((Get-Item -LiteralPath $diagnostic).LastWriteTimeUtc -lt $processStart -or $first.version -cne $ExpectedVersion -or
 $null -eq $boundRun -or $boundRun.SaveSlotId -ne $ExpectedSlot -or $boundRun.RunId -cne $ExpectedRunId -or
 -not $first.lifecycle.ready -or $first.errors.Count -ne 0 -or -not $first.debugUi.open -or
 -not $first.debugUi.regressionFixtures -or -not $first.debugUi.regressionSession.configured -or $first.debugUi.regressionSession.error -or
 $first.debugUi.suppressedByGuide -or $first.debugUi.suppressedByWorkshop -or $first.guideLocalizationDebug.running){
 throw 'Fresh version/run/readiness/debug-session gate failed. Open the idle developer panel and export diagnostics.'
}
if(Test-Path -LiteralPath $EvidenceDir){throw 'Use a fresh evidence directory.'}
New-Item -ItemType Directory -Path $EvidenceDir|Out-Null
$root=(Resolve-Path -LiteralPath $EvidenceDir).Path
$requestedAt=[DateTimeOffset]::Now
function Read-State {
 $live=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)
 if($live.Count -ne 1 -or $live[0].Id -ne $boundPid){throw 'Bound process changed; no restart attempted.'}
 try{$d=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json}catch{return $null}
 if($null -eq $d){return $null} # Ignore a transient truncated diagnostics write.
 if($d.version -cne $ExpectedVersion -or -not $d.lifecycle.ready -or $null -eq $d.lifecycle.run -or
  $d.lifecycle.run.Epoch -ne $boundRun.Epoch -or $d.lifecycle.run.SaveSlotId -ne $ExpectedSlot -or
  $d.lifecycle.run.RunId -cne $ExpectedRunId -or $d.errors.Count -ne 0){throw 'Bound version/run/epoch/readiness changed.'}
 return $d
}
function Plan([string]$name,[object[]]$steps){
 $path=Join-Path $root ($name+'.plan.json')
 $steps|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $path -Encoding UTF8
 & $helper -Action RunPlan -Plan $path -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid
}
function Await-State([string]$name,[scriptblock]$test,[int]$timeoutSeconds){
 $deadline=(Get-Date).AddSeconds($timeoutSeconds)
 do{
  $d=Read-State
  if($null -ne $d.guideLocalizationDebug -and [DateTimeOffset]$d.guideLocalizationDebug.at -ge $requestedAt){
   $report=$d.guideLocalizationDebug
   if(-not $report.running -and ($report.error -or $report.failed -gt 0)){throw ('Native language batch failed: '+$report.error)}
   if(& $test $d){
    $d|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $root ($name+'-diagnostics.json')) -Encoding UTF8
    return $d
   }
  }
  Start-Sleep -Milliseconds 150
 }while((Get-Date) -lt $deadline)
 throw "Native language state missing: $name; no click retry."
}
try{
 Plan 'start' @(@{kind='Click';name='guide-locale-check';x=495;y=108;waitMs=250})
 $d=Await-State 'started' {param($d)$d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.allSurfaces} 25
 $screenshots=0
 if($CaptureResult){Plan 'english-visual' @(@{kind='Snapshot';name='guide-english-visual';waitMs=100});$screenshots++}
 $d=Await-State 'complete' {param($d)-not $d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.allSurfaces -and
  $d.guideLocalizationDebug.failed -eq 0 -and -not $d.recipeGuide.visible} 480
 if($d.guideLocalizationDebug.originalLocale -cne $d.guideLocalizationDebug.currentLocale -or $d.recipeGuide.images.cached -ne 0){throw 'Original locale or preview cache restoration failed.'}
 foreach($locale in @('en','zh')){foreach($surface in @('items','native-ui','machine-layout','reading','workshop','dialogue','phone','supplier')){
  $sample=@($d.guideLocalizationDebug.surfaceSamples|Where-Object {$_.locale -ceq $locale -and $_.surface -ceq $surface})
  if($sample.Count -ne 1){throw "Missing unique native report: $locale/$surface"}
  if($surface -in @('native-ui','machine-layout','reading','dialogue','phone','supplier') -and
   ($sample[0].report.failed -ne 0 -or @($sample[0].report.checks).Count -eq 0)){throw "Incomplete native checks: $locale/$surface"}
 }}
 & $helper -Action Archive -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid
 [ordered]@{success=$true;version=$ExpectedVersion;dllSHA256=$ExpectedDllSHA256.ToUpperInvariant();pid=$boundPid;run=$boundRun;
  passed=$d.guideLocalizationDebug.passed;failed=0;nativeResult=$d.guideLocalizationDebug;visualScreenshots=$screenshots;routineScreenshots=0;
  scope='Explicitly bound transient en/zh surfaces; fixture-dispatched supplier arrival; no natural visit/payment/delivery/disk-save acceptance.'}|
  ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $root 'ui-result.json') -Encoding UTF8
 Write-Output "Native bilingual batch passed: $($d.guideLocalizationDebug.passed) checks; original locale restored."
}catch{
 [ordered]@{success=$false;pid=$boundPid;error=$_.Exception.Message}|ConvertTo-Json -Depth 8|
  Set-Content -LiteralPath (Join-Path $root 'ui-failure.json') -Encoding UTF8
 try{& $helper -Action Capture -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid}catch{}
 throw
}
