#requires -Version 5.1
<#
.SYNOPSIS
Create a small bilingual JSON pack or independent DLL Mod. / 创建中英双语起步工程。
.DESCRIPTION
Runs offline. The destination must not exist; generated files are never installed automatically.
离线运行；拒绝覆盖已有目录。名称和简介跟随游戏语言，作者可继续补充 CSV 语言。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Json','Dll')][string]$Kind,
    [Parameter(Mandatory=$true)][string]$ModId,
    [Parameter(Mandatory=$true)][string]$OutputDir,
    [string]$ItemId,
    [string]$NameZh='示例物品',
    [string]$NameEn='Example item',
    [string]$DescriptionZh='一个普通示例物品。',
    [string]$DescriptionEn='An ordinary example item.',
    [ValidateRange(0,1000000000)][long]$Value=25,
    [ValidateSet('Native','Extended')][string]$TooltipStyle='Native',
    [string]$SdkRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if (-not $SdkRoot) { $SdkRoot=Split-Path -Parent $PSScriptRoot }

# IDs are permanent save identities, independent of display language.
# ID 与显示文字分离；保存过的物品不可随意改 ID。
if ($ModId.Length -gt 120 -or $ModId -cnotmatch '^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+$') {
    throw 'Use a lowercase namespaced ModId, e.g. author.repair (max 120 characters).'
}
if (-not $ItemId) { $ItemId=$ModId+'.item' }
if ($ItemId.Length -gt 160 -or $ItemId -cnotmatch '^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+$' -or
    -not $ItemId.StartsWith($ModId+'.',[StringComparison]::Ordinal)) {
    throw 'ItemId must start with ModId plus a dot and use a valid namespaced ID (max 160).'
}
foreach ($entry in @(@($NameZh,200),@($NameEn,200),@($DescriptionZh,4000),@($DescriptionEn,4000))) {
    if ([string]::IsNullOrWhiteSpace($entry[0]) -or $entry[0].Length -gt $entry[1] -or
        $entry[0] -match '[\x00-\x1f\x7f]') {
        throw 'Provide readable single-line Chinese/English names (max 200) and descriptions (max 4000).'
    }
}
$SdkRoot=(Resolve-Path -LiteralPath $SdkRoot).Path
$status=Get-Content -LiteralPath (Join-Path $SdkRoot 'docs/release-status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$target=[IO.Path]::GetFullPath($OutputDir)
if (Test-Path -LiteralPath $target) { throw 'OutputDir already exists. Choose a new directory; existing work is never overwritten.' }
$parent=[IO.Path]::GetDirectoryName($target)
if (-not $parent -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
    throw 'OutputDir must have an existing parent directory.'
}
$parent=(Resolve-Path -LiteralPath $parent).Path
$stage=Join-Path $parent ('.ezapi-create-'+[guid]::NewGuid().ToString('N'))
$files=New-Object 'System.Collections.Generic.List[string]'
$utf8=New-Object Text.UTF8Encoding($false)
function Write-Generated([string]$relative,[string]$content) {
    $path=Join-Path $stage $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path,$content,$utf8)
    $files.Add($relative.Replace('\','/'))
}
function Csv-Row([string]$key,[string]$value) {
    # Escape literal braces as well as quotes: CSV values use numbered string.Format.
    # 花括号是字面文字，不让作者的名称误变成格式占位符。
    $value=$value.Replace('{','{{').Replace('}','}}').Replace('"','""')
    return $key+',"'+$value+'"'
}
function CSharp-Literal([string]$value) { return '@"'+$value.Replace('"','""')+'"' }

try {
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    if ($Kind -eq 'Json') {
        $manifest=[ordered]@{id=$ModId;name=$ModId;ezapiVersion='0.1';version='1.0.0';
            minEzapiVersion=$status.assemblyVersion;requires=@('items.json','items.types','items.text','localization.resources')}
        $item=[ordered]@{id=$ItemId;modId=$ModId;name=$NameEn;localizedName=@{key='item_name'};
            shortDescription=$DescriptionEn;localizedShortDescription=@{key='item_description'};
            types=@('MATERIAL');width=1;height=1;value=$Value;descriptionPlacement='native';
            tooltip=@{showSource=($TooltipStyle -eq 'Extended')}}
        Write-Generated 'pack.json' (($manifest | ConvertTo-Json -Depth 8)+"`n")
        Write-Generated 'items.json' ((ConvertTo-Json -InputObject @($item) -Depth 8)+"`n")
        Write-Generated 'Localization/en.csv' ((Csv-Row 'item_name' $NameEn)+"`n"+(Csv-Row 'item_description' $DescriptionEn)+"`n")
        Write-Generated 'Localization/zh.csv' ((Csv-Row 'item_name' $NameZh)+"`n"+(Csv-Row 'item_description' $DescriptionZh)+"`n")
        $instructions=@'
# JSON Mod / JSON 内容包

The two CSV files provide Chinese/English names and descriptions. The game language selects the translation.
名称与简介跟随游戏设置；可在 Localization 中添加其它官方语言 CSV，缺译时回退英语。

1. From the SDK root, validate this directory with:
   `dotnet tools/ezapi-validate/ezapi-validate.dll --pack '<this directory>'`
2. While the game is closed, copy this directory to `Mods/EZapiPacks/<ModId>`.
3. Check registration Applied state; enable the optional developer panel to generate one item by ID in a test save.
4. Test English and Chinese tooltips, normal dragging, and an ordinary save/reload.

在 SDK 根目录校验此包，游戏退出后复制到 Mods/EZapiPacks。用测试档查看目录 Applied 状态，再通过可选调试面板生成一件物品；分别检查中英文字、拖拽与正常保存重载。
The item is 1x1, without a custom image or activation effect. Add your own PNG via the SDK item-art/content-pack guide.
默认是 1x1 普通物品；图像和使用效果按对应指南添加。声明接收不等于已生成或已保存，物品 ID 与游戏语言无关。

Start with the SDK's `docs/AI-START-HERE.zh-CN.md` or `docs/AI-START-HERE.en.md`.
'@
    } else {
        # Build a standalone caller using the same documented, compiled SDK contract.
        $projectPath=Join-Path $SdkRoot 'docs/examples/minimal-dll/SimpleMod.csproj'
        $template=Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
        if (-not $template.Contains('<AssemblyName>EZapi.SimpleMod</AssemblyName>')) { throw 'SDK minimal project template is incompatible.' }
        Write-Generated 'Mod.csproj' ($template.Replace('<AssemblyName>EZapi.SimpleMod</AssemblyName>','<AssemblyName>'+$ModId+'</AssemblyName>'))
        $code=@'
using EZapi;
using MelonLoader;

[assembly: MelonInfo(typeof(EZapi.Generated.Mod), __MOD_NAME__, "1.0.0", "Author")]
[assembly: MelonGame(null, null)]
[assembly: MelonAdditionalDependencies("EZapi")]
namespace EZapi.Generated;

/// <summary>Declares an item only; names follow game language. 只声明物品；名称跟随游戏语言。</summary>
public sealed class Mod : MelonMod
{
    public override void OnInitializeMelon()
    {
        var result=ItemRegistry.Register(new ItemDefinition {
            Id=__ITEM_ID__, Name=__NAME_EN__, LocalizedName=new(__NAME_ZH__,__NAME_EN__),
            ShortDescription=__DESC_EN__, LocalizedShortDescription=new(__DESC_ZH__,__DESC_EN__),
            // Default plain material. Choose actual gameplay types. 默认普通材料，按玩法改分类。
            Types=new[] { "MATERIAL" }, Width=1, Height=1, Value=__VALUE__,
            DescriptionPlacement="native", Tooltip=new() { ShowSource=__SHOW_SOURCE__ }
        });
        // Accepted is queued, not Applied or delivered. Accepted 不代表原生应用或发放成功。
        if (!result.Accepted) LoggerInstance.Error(result.Message);
        else LoggerInstance.Msg("Item declaration accepted: "+result.ItemId);
    }
}
'@
        $replacements=@{'__MOD_NAME__'=(CSharp-Literal $ModId);'__ITEM_ID__'=(CSharp-Literal $ItemId);
            '__NAME_ZH__'=(CSharp-Literal $NameZh);'__NAME_EN__'=(CSharp-Literal $NameEn);
            '__DESC_ZH__'=(CSharp-Literal $DescriptionZh);'__DESC_EN__'=(CSharp-Literal $DescriptionEn);
            '__VALUE__'=$Value.ToString([Globalization.CultureInfo]::InvariantCulture);
            '__SHOW_SOURCE__'=($TooltipStyle -eq 'Extended').ToString().ToLowerInvariant()}
        # Single-pass substitution keeps marker-looking user text literal.
        $code=[regex]::Replace($code,'__(?:MOD_NAME|ITEM_ID|NAME_ZH|NAME_EN|DESC_ZH|DESC_EN|VALUE|SHOW_SOURCE)__',{
            param($match) $replacements[$match.Value]
        })
        Write-Generated 'Mod.cs' $code
        $instructions=@'
# DLL Mod / DLL 模组

Build from this directory:
`dotnet build ./Mod.csproj -c Release -p:GameDir='<game root>'`
If EZapi is not yet installed, also supply `-p:EZapiDll='<SDK root>/Mods/EZapi.dll'`.

游戏目录需有 MelonLoader，引用游戏 Mods 中的 EZapi 或显式指定 SDK DLL。只将本工程生成的 Mod DLL 复制到游戏 Mods，不额外复制依赖程序集。
Copy only this project's DLL from bin/Release/net6.0 to the game Mods directory while the game is closed. Keep the separately installed EZapi.dll and MelonLoader.

The inline Chinese/English descriptors follow game language; no OS-language setting or translated item IDs.
初始化只声明普通物品；中英名称和简介跟随游戏设置，ID 保持不变。用可选调试面板在测试档生成物品，检查 Applied、两种语言、拖拽和正常保存重载。
No automatic delivery or save is performed. Add your own image/behavior following the SDK's item guides.

Start with the SDK's `docs/AI-START-HERE.zh-CN.md` or `docs/AI-START-HERE.en.md`.
'@
    }
    $styleNote="`nTooltip style / Tooltip 风格: $TooltipStyle.`n"+@'
The generator defaults to Native: native descriptions, attribution disabled. Extended enables attribution.
生成器默认 Native：原生说明字段并关闭来源行。Extended 开启来源；这只是现有字段配方，不是新增 JSON 属性。
The default MATERIAL is an example gameplay choice; choose real item types. Instructions belong in shortDescription;
narrative belongs in flavorText. Do not repeat native category/value/properties. See docs/tooltip-style.en.md or .zh-CN.md.
默认 MATERIAL 是普通材料示例；按真实玩法改分类。操作说明放 shortDescription，背景叙述放 flavorText，不重复原生分类、价格和属性。
'@
    Write-Generated 'README.md' ($instructions+"`n"+$styleNote+"`n")
    # Directory.Move refuses a destination that appeared after the initial check.
    [IO.Directory]::Move($stage,$target)
} catch {
    # Delete only this invocation's checked temporary directory, never OutputDir.
    $checked=[IO.Path]::GetFullPath($stage)
    if ([IO.Path]::GetDirectoryName($checked) -eq $parent -and
        [IO.Path]::GetFileName($checked) -match '^\.ezapi-create-[a-f0-9]{32}$' -and
        (Test-Path -LiteralPath $checked -PathType Container)) {
        Remove-Item -LiteralPath $checked -Recurse -Force
    }
    throw
}
[ordered]@{kind=$Kind;directory=$target;modId=$ModId;itemId=$ItemId;
    frameworkVersion=$status.assemblyVersion;tooltipStyle=$TooltipStyle;languages=@('zh','en');files=@($files);installed=$false} | ConvertTo-Json -Depth 5
