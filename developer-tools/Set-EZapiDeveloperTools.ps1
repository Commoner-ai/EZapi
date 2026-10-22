param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [switch]$Disable
)
$ErrorActionPreference='Stop'
$gameRoot=(Resolve-Path -LiteralPath $GameDir).Path
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'MelonLoader/net6/MelonLoader.dll'))){throw 'Select the game root with MelonLoader installed.'}
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'Mods/EZapi.dll'))){throw 'Install the EZapi runtime first.'}
if(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Exit the game before changing developer tools.'}
$folder=Join-Path $gameRoot 'UserData/EZapi'
New-Item -ItemType Directory -Path $folder -Force|Out-Null
$file=Join-Path $folder 'developer-tools.json'
if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination ($file+'.backup-'+[DateTime]::Now.ToString('yyyyMMddHHmmssfff'))}
@{enabled=(-not $Disable);regressionFixtures=$false}|ConvertTo-Json|Set-Content -LiteralPath $file -Encoding UTF8
Write-Output "Developer tools enabled=$(-not $Disable). Restart the game. No save files or content packs changed."
