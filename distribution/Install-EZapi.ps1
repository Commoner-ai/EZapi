param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
$gameRoot=(Resolve-Path -LiteralPath $GameDir).Path
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'MelonLoader/net6/MelonLoader.dll'))){throw 'Select the game root with MelonLoader IL2CPP .NET6 installed.'}
if(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Exit the game before installing EZapi.'}
$source=Join-Path $PSScriptRoot 'Mods/EZapi.dll'
$checks=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'files.sha256.json') -Raw -Encoding UTF8|ConvertFrom-Json
$expected=@($checks|Where-Object {$_.path -eq 'Mods/EZapi.dll'})
if($expected.Count -ne 1 -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected[0].sha256){throw 'Runtime checksum mismatch. Download the package again.'}
$mods=Join-Path $gameRoot 'Mods';New-Item -ItemType Directory -Path $mods -Force|Out-Null
$destination=Join-Path $mods 'EZapi.dll'
if(Test-Path -LiteralPath $destination){
    $backup=Join-Path $gameRoot ('UserData/EZapi/Backups/'+[DateTime]::Now.ToString('yyyyMMddHHmmssfff'))
    New-Item -ItemType Directory -Path $backup -Force|Out-Null
    Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'EZapi.dll')
}
Copy-Item -LiteralPath $source -Destination $destination
if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected[0].sha256){throw 'Installed DLL checksum mismatch.'}
Write-Output 'EZapi installed. Existing content packs, developer preferences and saves were preserved. Install exactly one EZapi.dll.'
