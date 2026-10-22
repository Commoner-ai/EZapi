param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [switch]$Disable,
    [switch]$EnableRegressionFixtures,
    [Nullable[int]]$RegressionSlot,
    [string]$RegressionRunId
)
$ErrorActionPreference='Stop'
if($Disable -and $EnableRegressionFixtures){throw 'Disable and EnableRegressionFixtures are mutually exclusive.'}
if($EnableRegressionFixtures){
    $bindingGuid=[Guid]::Empty
    if($null -eq $RegressionSlot -or $RegressionSlot -lt 0 -or
       -not [Guid]::TryParseExact($RegressionRunId,'D',[ref]$bindingGuid) -or $bindingGuid -eq [Guid]::Empty){
        throw 'Regression fixtures require an explicit disposable RegressionSlot >= 0 and nonempty D-format RegressionRunId.'
    }
}elseif($PSBoundParameters.ContainsKey('RegressionSlot') -or $PSBoundParameters.ContainsKey('RegressionRunId')){
    throw 'Session binding parameters require EnableRegressionFixtures.'
}
$gameRoot=(Resolve-Path -LiteralPath $GameDir).Path
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'MelonLoader/net6/MelonLoader.dll'))){throw 'Select the game root with MelonLoader installed.'}
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'Mods/EZapi.dll'))){throw 'Install the EZapi runtime first.'}
if(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Exit the game before changing developer tools.'}
$folder=Join-Path $gameRoot 'UserData/EZapi'
New-Item -ItemType Directory -Path $folder -Force|Out-Null
$file=Join-Path $folder 'developer-tools.json'
$sessionFile=Join-Path $folder 'regression-session.json'
$stamp=[DateTime]::Now.ToString('yyyyMMddHHmmssfff')
foreach($target in @($file,$sessionFile)){
    if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination ($target+'.backup-'+$stamp)}
}
if($EnableRegressionFixtures){
    @{Slot=[int]$RegressionSlot;RunId=$RegressionRunId}|ConvertTo-Json|Set-Content -LiteralPath $sessionFile -Encoding UTF8
}
@{enabled=(-not $Disable);regressionFixtures=[bool]$EnableRegressionFixtures}|ConvertTo-Json|Set-Content -LiteralPath $file -Encoding UTF8
Write-Output "Developer tools enabled=$(-not $Disable); regressionFixtures=$([bool]$EnableRegressionFixtures). Restart the game. No save files or content packs changed."
if($EnableRegressionFixtures){Write-Output 'Transient locale tests are bound to the specified run. No tests run automatically; persistent fixtures retain separate restrictions.'}
