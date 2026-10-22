param(



 [string]$GameDir='E:\steam\steamapps\common\Probably Stolen Demo',



 [string]$EvidenceDir=('work/native-batch-'+(Get-Date -Format 'yyyyMMdd-HHmmss')),



 [string]$ExpectedVersion='0.1.73-usability',



 [int]$ExpectedSlot=534,



 [ValidateSet('Layouts','Transactions','Terminal','Automation','Furnace','Materials','Recipes','ExternalPower','SpriteAssets','Inspection','NightStock','NpcStock','MachineRegistration','Registration','SaveGraph','Endpoint')][string[]]$Groups=@('Inspection','NightStock','NpcStock','MachineRegistration','Registration','SaveGraph','Endpoint'),



 [ValidateRange(2,60)][int]$ResultTimeoutSeconds=20,



 [switch]$CaptureResult



)



# 一次进入、同一 PID/周目连续检查。仅重放已确认的固定布局，不用截图识别常规按钮。



# 新增组必须填写按钮、报告字段及结果断言；不能用“点击成功”代替实际测试通过。



$ErrorActionPreference='Stop'
$batchOptions=Get-Content -LiteralPath (Join-Path $GameDir 'UserData/EZapi/developer-tools.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($batchOptions.enabled -ne $true -or $batchOptions.regressionFixtures -ne $true){throw 'Regression batch requires both developer settings true and a disposable test save.'}




if($Groups.Count -eq 0 -or @($Groups|Select-Object -Unique).Count -ne $Groups.Count){throw 'Choose at least one group, without duplicates.'}



$helper=Join-Path $PSScriptRoot 'Invoke-EZapiNativeTest.ps1'



New-Item -ItemType Directory -Path $EvidenceDir -Force|Out-Null



$batchRoot=(Resolve-Path -LiteralPath $EvidenceDir).Path



$token='batch-'+(Get-Date -Format 'HHmmssfff')



$diagnostic=Join-Path $GameDir 'UserData/EZapi/diagnostics.json'



$results=New-Object 'System.Collections.Generic.List[object]'



$totalPassed=0



$boundPid=0



$boundRun=$null



$catalog=@{



 Layouts=@{x=304;y=372;field='machineLayoutDebug';minimum=46}
 Transactions=@{x=634;y=372;field='machineTransactionDebug';minimum=40}
 Terminal=@{x=110;y=444;field='manufacturingTerminalDebug';minimum=80}
 Automation=@{x=634;y=408;field='machineAutomationDebug';minimum=135}
 Furnace=@{x=634;y=444;field='nativeFurnaceDebug';minimum=150}



 SpriteAssets=@{x=495;y=601;field='spriteAssetsDebug';minimum=33}



 Materials=@{x=304;y=408;field='machineMaterialDebug';minimum=450}



 Recipes=@{x=110;y=408;field='machineRecipeDebug';minimum=10}



 ExternalPower=@{x=495;y=444;field='externalPowerDebug';minimum=60}



 SaveGraph=@{x=634;y=748;field='saveGraphDebug';minimum=33}



 Endpoint=@{x=110;y=560;field='inventoryEndpointDebug';minimum=14}



 MachineRegistration=@{x=495;y=408;field='machineRegistrationDebug';minimum=124}



 Registration=@{y=712;field='registrationDebug';minimum=67}



 Inspection=@{y=601;field='itemInspectionDebug';minimum=425}



 NightStock=@{y=640;field='itemStockDebug';minimum=13}



 NpcStock=@{y=676;field='npcStockDebug';minimum=34}



}



function Read-BatchState {



 $live=@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)



 if($live.Count -ne 1 -or $live[0].Id -ne $boundPid){throw 'Batch PID changed; no further input sent.'}



 # 游戏直接覆盖 JSON 时可能读到半份文件，等待下一轮完整快照。



 try {$d=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json}catch{return $null}



 if($d.version -ne $ExpectedVersion -or $null -eq $d.lifecycle.run -or [int]$d.lifecycle.run.SaveSlotId -ne $ExpectedSlot -or -not $d.lifecycle.ready -or $d.errors.Count -ne 0){throw 'Batch version, slot, readiness or bootstrap errors changed.'}



 if($d.lifecycle.run.RunId -ne $boundRun.RunId -or $d.lifecycle.run.Epoch -ne $boundRun.Epoch){throw 'Batch run identity changed; no further input sent.'}



 if(-not $d.debugUi.open -or $d.debugUi.suppressedByWorkshop -or $d.debugUi.suppressedByGuide){throw 'Batch debug panel is not available.'}



 return $d



}



try {



 & (Join-Path $PSScriptRoot 'Enter-EZapiTestStore.ps1') -GameDir $GameDir -EvidenceDir $batchRoot -ExpectedVersion $ExpectedVersion -ExpectedSlot $ExpectedSlot



 $boundPid=(Get-Process -Name 'Probably Stolen').Id



 $initial=Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8|ConvertFrom-Json



 $boundRun=$initial.lifecycle.run



 foreach($group in $Groups){



  $definition=$catalog[$group]



  $d=Read-BatchState



  if($null -eq $d){throw 'Cannot read complete diagnostic before sending input.'}



  $previousReport=$d.($definition.field)



  $previousAt=if($null -ne $previousReport){[string]$previousReport.at}else{''}



  $started=[DateTimeOffset]::Now



  $writeBefore=(Get-Item -LiteralPath $diagnostic).LastWriteTimeUtc



  $plan=Join-Path $batchRoot "$token-$group.plan.json"



  @(@{kind='Click';name="$token-$group";x=$(if($definition.ContainsKey('x')){$definition.x}else{634});y=$definition.y;waitMs=500})|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $plan -Encoding UTF8



  & $helper -Action RunPlan -Plan $plan -EvidenceDir $batchRoot -GameDir $GameDir -ExpectedProcessId $boundPid



  $deadline=(Get-Date).AddSeconds($ResultTimeoutSeconds)



  $report=$null



  do {



   $d=Read-BatchState



   if($null -ne $d){



    $candidate=$d.($definition.field)



    if($null -ne $candidate -and [string]$candidate.at -ne $previousAt -and [DateTimeOffset]$candidate.at -ge $started -and (Get-Item -LiteralPath $diagnostic).LastWriteTimeUtc -gt $writeBefore){$report=$candidate;break}



   }



   Start-Sleep -Milliseconds 250



  }while((Get-Date) -lt $deadline)



  if($null -eq $report){throw "$group produced no fresh result; stopped without repeating click."}



  # 保存刚验收的同一份内存快照，避免随后诊断覆盖导致证据与断言不同。



  $d|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $batchRoot "$token-$group-diagnostics.json") -Encoding UTF8



  if([int]$report.failed -ne 0 -or [int]$report.passed -lt $definition.minimum -or @($report.checks|Where-Object {-not $_.passed}).Count -ne 0){throw "$group native assertions failed."}



  switch($group){
   Recipes {foreach($mode in @('readonly-recipe-readonly','readonly-recipe-machine-condition','readonly-recipe-false-condition','readonly-recipe-item-condition','readonly-recipe-false-item-condition','readonly-recipe-liquid-condition','readonly-recipe-false-liquid-condition','readonly-recipe-liquid-amount','readonly-recipe-output-count','readonly-recipe-output-id','readonly-recipe-power-cost','readonly-recipe-liquid-output-amount','readonly-recipe-liquid-output-value','readonly-recipe-liquid-output-quality','readonly-recipe-transaction-factory','readonly-recipe-transaction-validate','readonly-recipe-prepare-input-tag','readonly-recipe-prepare-machine-tag','readonly-recipe-prepare-battery-tag','readonly-recipe-prepare-water-tag','readonly-recipe-prepare-output-tag','readonly-recipe-container-condition','readonly-recipe-container-contents')){if(@($report.details|Where-Object {$_.mode -eq $mode}).Count -ne 1){throw "Recipe read-only scenario missing: $mode"}}}
   Automation {foreach($mode in @('mutate-tag','mutate-machine-tag')){if(@($report.details|Where-Object {$_.mode -eq $mode}).Count -ne 1){throw "Automation tag scenario missing: $mode"}}}




   Furnace {



    foreach($requiredMode in @('native','commit','zero','empty','insufficient','disconnected','validate','debit-throw','verify','late-verify','native-output-policy','dispose','old-output','full','old-destroy','restore-false','nested-reject','nested-commit','nested-rollback','nested-metadata','nested-destroy','nested-frozen','old-nested-commit','old-nested-rollback','empty-window','nested-zero','nested-liquid','old-nested-destroy')){



     if(@($report.details|Where-Object {$_.mode -eq $requiredMode}).Count -ne 1){throw "Required furnace scenario missing: $requiredMode"}



    }



    if($d.externalPower.openTransactions -ne 0 -or $d.externalPower.furnace.active -or $d.inventoryGuards.freeze.items -gt 0){throw 'Furnace transaction cleanup incomplete.'}



   }



   Materials {



    foreach($requiredMode in @('component-mixture','component-partial-output','liquid-output-empty','liquid-output-dynamic','liquid-output-rollback','liquid-output-partial-fill','condition-allow','condition-output-entry','condition-input-entry','condition-output-stored','condition-input-stored','condition-and','condition-throw','condition-dispose','condition-recipe','graph-reject','graph-consume','graph-consume-auto','graph-output-refused','graph-metadata-rollback','graph-cleanup-fault','graph-partial-output','graph-verify-rollback','graph-stage-refused','graph-frozen-child','graph-readonly-resolver','graph-prepare-mutation')){



     if(@($report.details|Where-Object {$_.mode -eq $requiredMode}).Count -ne 1){throw "Required material scenario missing: $requiredMode"}



    }



   }



   SaveGraph {if(-not $d.saveGraph.canDecode){throw 'Save graph load sequence unavailable.'}}



   Endpoint {if(-not $d.inventoryEndpoints.ready -or $null -ne $d.inventoryEndpoints.error){throw 'Endpoint readiness/fault check failed.'}}



   MachineRegistration {foreach($requiredMode in @('required-empty','optional-empty','required-empty-source','required-source','optional-positive','required-frozen','required-external','required-disconnected','borrow-custom','borrow-bound','borrow-template-mismatch','borrow-foreign','borrow-duplicate','borrow-canonical','borrow-required-downgrade','borrow-prepare-error','night-audit-readonly','night-audit-machine-tag','night-audit-input-tag','night-audit-nested-tag','night-audit-nested-value','night-audit-nested-state','night-audit-machine-shape','night-audit-nested-shape','night-audit-nested-topology','night-audit-nested-lock','night-audit-input-quantity','night-audit-binding-swap')){if(@($report.details|Where-Object {$_.mode -eq $requiredMode}).Count -ne 1){throw "Required power scenario missing: $requiredMode"}}}



   Registration {
    if(-not $d.registrations.ready){throw 'Registration service not ready.'}
    $itemDataChecks=@($report.checks|Where-Object {$_.name -like 'item-data:*'})
    if($itemDataChecks.Count -lt 18 -or @($itemDataChecks|Where-Object {-not $_.passed}).Count -ne 0){throw 'Native item-data fixture checks are missing or failed.'}
    if($d.inventoryGuards.freeze.items -ne 0){throw 'Item-data test left a native item frozen.'}
   }



   Inspection {if($report.rows.Count -ne 84 -or $report.errors.Count -ne 0){throw 'Inspection fixture rows/errors failed.'}}



   NightStock {if(-not $d.itemStock.available -or $d.itemStock.contextDepth -ne 0 -or $d.itemStock.busy){throw 'Night stock hooks or context cleanup failed.'}}



   NpcStock {if(-not $d.npcStock.available -or $d.npcStock.scopeDepth -ne 0 -or $d.npcStock.routing -or $null -ne $d.npcStock.lastError){throw 'NPC stock hooks or scope cleanup failed.'}}



  }



  $results.Add([ordered]@{group=$group;passed=$report.passed;failed=$report.failed;at=$report.at;scope=$report.scope})



  $totalPassed += [int]$report.passed



  Write-Output "$group : $($report.passed) passed, $($report.failed) failed."



 }



 & $helper -Action Archive -EvidenceDir $batchRoot -GameDir $GameDir -ExpectedProcessId $boundPid



 if($CaptureResult){& $helper -Action Capture -EvidenceDir $batchRoot -GameDir $GameDir -ExpectedProcessId $boundPid}



 [ordered]@{success=$true;version=$ExpectedVersion;pid=$boundPid;slot=$ExpectedSlot;run=$boundRun;groups=$results.ToArray();passed=$totalPassed;failed=0;routineScreenshots=[bool]$CaptureResult;finished=[DateTimeOffset]::Now;scope='temporary native fixtures and isolated native callbacks in one process/run; no actual NPC dialogue, shelf placement, purchase or save verification'}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $batchRoot "$token-result.json") -Encoding UTF8



} catch {



 [ordered]@{success=$false;pid=$boundPid;completed=$results.ToArray();reason=$_.Exception.Message;at=[DateTimeOffset]::Now}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $batchRoot "$token-failure.json") -Encoding UTF8



 try{& $helper -Action Capture -EvidenceDir $batchRoot -GameDir $GameDir -ExpectedProcessId $boundPid}catch{Write-Warning 'Failed-state capture unavailable.'}



 throw



}



