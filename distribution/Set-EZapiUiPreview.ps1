#requires -Version 5.1
<#
.SYNOPSIS
Stage one bounded UI layout for explicit in-game preview reload. Does not launch or save the game.
将布局放到原生UI预览目录；在游戏开发面板点击“UI 预览”重载。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$LayoutPath
)
$ErrorActionPreference='Stop'
$gameRoot=(Resolve-Path -LiteralPath $GameDir).Path
$layout=(Resolve-Path -LiteralPath $LayoutPath).Path
if(-not(Test-Path -LiteralPath (Join-Path $gameRoot 'Mods/EZapi.dll'))){throw 'Install the EZapi runtime first.'}
if((Get-Item -LiteralPath $layout).Length -gt 65536){throw 'Layout exceeds 64 KiB.'}
$layoutBytes=[IO.File]::ReadAllBytes($layout)
$layoutText=[Text.Encoding]::UTF8.GetString($layoutBytes).TrimStart([char]0xFEFF)
$data=$layoutText|ConvertFrom-Json
if($data.schemaVersion -ne 1 -or $null -eq $data.root){throw 'Expected schemaVersion=1 and a root layout node.'}
# This is staging only. The actual runtime parser rejects unknown/duplicate fields,
# validates all node bounds, and leaves an existing preview intact on parse failure.
$folder=Join-Path $gameRoot 'UserData/EZapi/UiPreview'
New-Item -ItemType Directory -Path $folder -Force|Out-Null
$target=Join-Path $folder 'layout.json'
if([IO.Path]::GetFullPath($layout) -eq [IO.Path]::GetFullPath($target)){
    Write-Output 'Layout already staged. Click UI preview to reload.'
    return
}
if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination ($target+'.backup-'+[DateTime]::Now.ToString('yyyyMMddHHmmssfff'))}
$temporary=Join-Path $folder ('layout-'+[Guid]::NewGuid().ToString('N')+'.tmp')
try{
    [IO.File]::WriteAllBytes($temporary,$layoutBytes)
    Move-Item -LiteralPath $temporary -Destination $target -Force
}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
Write-Output 'Layout staged. In the ordinary EZapi developer panel, click UI preview; export diagnostics for uiPreview measurements. No save or gameplay action requested.'
