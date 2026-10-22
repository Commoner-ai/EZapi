param([string]$GameDir='E:\steam\steamapps\common\Probably Stolen Demo',[string]$EvidenceDir='work/native-0.1.73-usability/locale-ui')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $EvidenceDir -Force|Out-Null
$root=(Resolve-Path -LiteralPath $EvidenceDir).Path
$helper=Join-Path $PSScriptRoot 'Invoke-EZapiNativeTest.ps1'
$diagnostic=Join-Path $GameDir 'UserData/EZapi/diagnostics.json'
$boundPid=(Get-Process -Name 'Probably Stolen').Id
$first=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json
$boundRun=$first.lifecycle.run
if($first.version -ne '0.1.73-usability' -or $boundRun.SaveSlotId -ne 534 -or $boundRun.RunId -ne 'e39e848c-36b7-40e9-ab7e-b6adcb9881b8'){throw 'Guide locale version/run gate failed.'}
function Read-State {
 if((Get-Process -Name 'Probably Stolen').Id -ne $boundPid){throw 'Guide locale PID changed.'}
 try{$d=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json}catch{return $null}
 if($null -eq $d){return $null} # Empty file between truncate/write is not a runtime failure.
 if($d.version -ne '0.1.73-usability' -or $d.lifecycle.run.Epoch -ne $boundRun.Epoch -or $d.errors.Count -ne 0){
  $d|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $root 'runtime-gate-failure.json') -Encoding UTF8
  throw 'Guide locale runtime gate changed.'
 }
 return $d
}
function Plan([string]$name,[object[]]$steps){
 $path=Join-Path $root ($name+'.plan.json');if(Test-Path -LiteralPath $path){throw 'Use fresh evidence directory.'}
 $steps|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $path -Encoding UTF8
 & $helper -Action RunPlan -Plan $path -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid
}
function Await-State([string]$name,[scriptblock]$test,[int]$timeoutSeconds=45){
 $deadline=(Get-Date).AddSeconds($timeoutSeconds)
 do{$d=Read-State;if($null -ne $d.guideLocalizationDebug -and -not $d.guideLocalizationDebug.running -and ($d.guideLocalizationDebug.error -or $d.guideLocalizationDebug.failed -gt 0)){$failed=@($d.guideLocalizationDebug.checks|Where-Object {-not $_.passed}|Select-Object -First 1);$detail=if($failed.Count){$failed[0].name}else{$d.guideLocalizationDebug.error};throw ("Native language batch failed: "+$detail)};if($null -ne $d -and (& $test $d)){$d|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $root ($name+'-diagnostics.json')) -Encoding UTF8;return $d};Start-Sleep -Milliseconds 150}while((Get-Date) -lt $deadline)
 throw "Guide locale state missing: $name"
}
try{
 Plan 'start' @(@{kind='Click';name='guide-locale-check';x=495;y=108;waitMs=250})
 $d=Await-State 'english' {param($d)$d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.stage -eq 1 -and $d.recipeGuide.locale -eq 'en' -and $d.recipeGuide.resolved.MachineName -eq 'EZapi image probe' -and $d.recipeGuide.renderedText -like '*Field kit product*' -and @($d.recipeGuide.images.entries|Where-Object {$_.ItemId -eq 'ezapi.sample.water_container' -and $_.ready -and -not $_.Error}).Count -eq 1}
 Plan 'english-visual' @(@{kind='Snapshot';name='guide-english-visual';waitMs=100})
 $d=Await-State 'chinese' {param($d)
  $observedGuide=$d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.stage -eq 2 -and $d.recipeGuide.locale -eq 'zh' -and $d.recipeGuide.resolved.MachineName -eq 'EZapi图片检查机' -and $d.recipeGuide.renderedText -like '*PNG示意产物*' -and @($d.recipeGuide.images.entries|Where-Object {$_.ItemId -eq 'ezapi.sample.water_container' -and $_.ready -and -not $_.Error}).Count -eq 1
  $completed=$null -ne $d.guideLocalizationDebug -and -not $d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.allSurfaces -and $d.guideLocalizationDebug.failed -eq 0 -and @($d.guideLocalizationDebug.checks|Where-Object {$_.passed -and $_.name -eq 'zh: native locale drives rendered machine/output/requirement/quantity/note'}).Count -eq 1
  return $observedGuide -or $completed
 } 300
 $visualScreenshots=1
 if($d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.stage -eq 2){Plan 'chinese-visual' @(@{kind='Snapshot';name='guide-chinese-visual';waitMs=100});$visualScreenshots++}
 $d=Await-State 'complete' {param($d)$null -ne $d.guideLocalizationDebug -and -not $d.guideLocalizationDebug.running -and $d.guideLocalizationDebug.allSurfaces -and $d.guideLocalizationDebug.failed -eq 0 -and -not $d.recipeGuide.visible} 480
 if($d.guideLocalizationDebug.originalLocale -ne $d.guideLocalizationDebug.currentLocale -or $d.recipeGuide.images.cached -ne 0){throw 'Original locale/cache restoration failed.'}
 $requiredSurfaces=@('items','machine-layout','reading','workshop','dialogue','phone','supplier')
 foreach($locale in @('en','zh')){foreach($surface in $requiredSurfaces){
  $sample=@($d.guideLocalizationDebug.surfaceSamples|Where-Object {$_.locale -eq $locale -and $_.surface -eq $surface})
  if($sample.Count -ne 1){throw "Missing unique native localization report: $locale/$surface"}
  if($surface -in @('machine-layout','reading','dialogue','phone','supplier') -and ($sample[0].report.failed -ne 0 -or @($sample[0].report.checks).Count -eq 0)){throw "Invalid native checks: $locale/$surface"}
 }}
 & $helper -Action Archive -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid
 [ordered]@{success=$true;version='0.1.73-usability';pid=$boundPid;run=$boundRun;passed=$d.guideLocalizationDebug.passed;failed=0;nativeResult=$d.guideLocalizationDebug;visualScreenshots=$visualScreenshots;routineScreenshots=0}|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $root 'ui-result.json') -Encoding UTF8
}catch{
 [ordered]@{success=$false;pid=$boundPid;error=$_.Exception.Message}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'ui-failure.json') -Encoding UTF8
 & $helper -Action Capture -EvidenceDir $root -GameDir $GameDir -ExpectedProcessId $boundPid
 throw
}
