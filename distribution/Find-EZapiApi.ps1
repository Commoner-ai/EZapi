#requires -Version 5.1
<#
.SYNOPSIS
Search actual SDK signatures, tasks and errors. / 查询真实公开签名、任务和错误。
.DESCRIPTION
Reads the shipped indexes offline. Exact member IDs preserve overload identity.
只读取索引，不加载游戏 DLL。中文/英文任务命中后可找到对应领域的真实类型和签名。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Query,
    [string]$Type,
    [ValidateRange(1,100)][int]$Limit=12,
    [string]$SdkRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if (-not $SdkRoot) { $SdkRoot=Split-Path -Parent $PSScriptRoot }
$SdkRoot=(Resolve-Path -LiteralPath $SdkRoot).Path
$api=Get-Content -LiteralPath (Join-Path $SdkRoot 'docs/public-api.index.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$tasks=Get-Content -LiteralPath (Join-Path $SdkRoot 'docs/developer-workflows.index.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$errors=Get-Content -LiteralPath (Join-Path $SdkRoot 'docs/errors.index.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$words=@($Query.Trim() -split '\s+' | Where-Object { $_ })
if ($words.Count -eq 0) { throw 'Query must contain readable text.' }
function Match-All([string]$text) {
    foreach ($word in $words) { if ($text.IndexOf($word,[StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false } }
    return $true
}
function Summary($value) {
    if ($null -ne $value -and $null -ne $value.PSObject.Properties['summary']) { return [string]$value.summary }
    return ''
}
$domains=@($api.domains | Where-Object { Match-All ($_.id+' '+$_.taskZh+' '+$_.taskEn) })
$workflows=@($tasks.workflows | Where-Object { Match-All ($_ | ConvertTo-Json -Depth 12 -Compress) })
$matches=New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in $api.types) {
    if ($Type -and $entry.name -cne $Type) { continue }
    $domainMatch=@($domains | Where-Object { $_.id -eq $entry.domain }).Count -gt 0
    $typeMatch=Match-All ($entry.name+' '+(Summary $entry.documentation))
    if ($typeMatch -or $domainMatch) {
        $matches.Add([ordered]@{kind='type';id=$entry.id;type=$entry.name;domain=$entry.domain;
            summary=(Summary $entry.documentation);score=$(if($typeMatch){2}else{1})})
    }
    foreach ($group in @('methods','properties','fields','events')) {
        foreach ($member in $entry.$group) {
            if ($member.compilerGenerated) { continue }
            $text=$entry.name+' '+$member.name+' '+$member.id+' '+(Summary $member.documentation)
            if (Match-All $text) {
                $row=[ordered]@{kind=$group;id=$member.id;type=$entry.name;name=$member.name;domain=$entry.domain;
                    summary=(Summary $member.documentation);score=3}
                # Return the real declaration, including overload parameters and defaults.
                $row['declaration']=$member
                $matches.Add($row)
            }
        }
    }
}
$errorMatches=New-Object 'System.Collections.Generic.List[object]'
# Error-index shape is independent of API metadata; keep matching rows intact.
foreach ($property in $errors.PSObject.Properties) {
    if ($property.Name -eq 'documentationContext') { continue }
    foreach ($row in @($property.Value)) {
        if ($null -ne $row -and (Match-All ($row | ConvertTo-Json -Depth 20 -Compress))) {
            $errorMatches.Add([ordered]@{section=$property.Name;entry=$row})
        }
    }
}
$ordered=@($matches | Sort-Object @{Expression={ $_.score };Descending=$true},@{Expression={ $_.id }})
[ordered]@{query=$Query;assemblyVersion=$api.assemblyVersion;dllSha256=$api.dllSha256;
    domains=@($domains | Select-Object -First $Limit);workflows=@($workflows | Select-Object -First $Limit);
    declarations=@($ordered | Select-Object -First $Limit);declarationMatches=$ordered.Count;
    errors=@($errorMatches | Select-Object -First $Limit);
    truncated=($ordered.Count -gt $Limit -or $domains.Count -gt $Limit -or $workflows.Count -gt $Limit -or $errorMatches.Count -gt $Limit);
    exactSignatureIndex='docs/public-api.index.json';nativeVerificationPerformed=$false} | ConvertTo-Json -Depth 30
