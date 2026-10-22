param(
    [string]$GameDir,
    [string]$OutputDir,
    [switch]$OfflineOnly
)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    if ($OfflineOnly) {
        & "$PSScriptRoot/scripts/Test-EZapiOffline.ps1"
        return
    }
    if (-not $GameDir) { throw 'Supply -GameDir, or use -OfflineOnly without game files.' }
    $GameDir = (Resolve-Path -LiteralPath $GameDir).Path
    if (-not (Test-Path -LiteralPath "$GameDir/MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll")) {
        throw 'Game IL2CPP assemblies missing. Install compatible MelonLoader and run the game once.'
    }
    if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot 'outputs/EZapi-0.1.73-source-built' }
    $OutputDir = [IO.Path]::GetFullPath($OutputDir)
    if (Test-Path -LiteralPath $OutputDir) { throw 'Use a fresh OutputDir.' }
    foreach ($taskProject in @('EZapi/EZapi.csproj','EZapi.Sample/ExampleMod.csproj','EZapi.ContentValidator/EZapi.ContentValidator.csproj')) {
        dotnet build "src/$taskProject" -c Release "-p:GameDir=$GameDir"
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $taskProject" }
    }
    $taskDll = Join-Path $PSScriptRoot 'src/EZapi/bin/Release/net6.0/EZapi.dll'
    dotnet build docs/examples/minimal-dll/SimpleMod.csproj -c Release "-p:GameDir=$GameDir" "-p:EZapiDll=$taskDll"
    if ($LASTEXITCODE -ne 0) { throw 'Independent consumer example failed.' }
    & "$PSScriptRoot/scripts/Test-EZapiOffline.ps1"
    dotnet build work/ezapi-inspect/Inspect.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Metadata inspector build failed.' }
    python -X utf8 work/build-public-api-index.py --version 0.1.73
    if ($LASTEXITCODE -ne 0) { throw 'API index generation failed.' }
    foreach ($taskScript in @('sync-curated-api-types.py','verify-public-api-index.py')) {
        python -X utf8 "work/$taskScript"
        if ($LASTEXITCODE -ne 0) { throw "Index verification failed: $taskScript" }
    }
    New-Item -ItemType Directory -Path "$OutputDir/Mods/EZapiPacks/ezapi.sample", "$OutputDir/examples", "$OutputDir/tools/ezapi-validate" -Force | Out-Null
    Copy-Item -LiteralPath $taskDll -Destination "$OutputDir/Mods/EZapi.dll"
    Copy-Item -LiteralPath 'src/EZapi/bin/Release/net6.0/EZapi.xml' -Destination "$OutputDir/Mods/EZapi.xml"
    Copy-Item -LiteralPath 'src/EZapi.Sample/bin/Release/net6.0/EZapi.Sample.dll' -Destination "$OutputDir/examples/EZapi.Sample.dll"
    Copy-Item 'src/EZapi.ContentValidator/bin/Release/net8.0/*' -Destination "$OutputDir/tools/ezapi-validate/"
    Copy-Item -LiteralPath 'src/EZapi.Sample/pack.json','src/EZapi.Sample/Content/items.json' -Destination "$OutputDir/Mods/EZapiPacks/ezapi.sample/"
    foreach ($taskFolder in @('Textures','LootTables','Localization')) {
        Copy-Item -LiteralPath "src/EZapi.Sample/$taskFolder" -Destination "$OutputDir/Mods/EZapiPacks/ezapi.sample/$taskFolder" -Recurse
    }
    Copy-Item -LiteralPath 'docs' -Destination "$OutputDir/docs" -Recurse
    dotnet src/EZapi.ContentValidator/bin/Release/net8.0/ezapi-validate.dll --packs "$OutputDir/Mods/EZapiPacks"
    if ($LASTEXITCODE -ne 0) { throw 'Sample content pack failed validation.' }
    $taskReport = [ordered]@{
        schemaVersion=1; version='0.1.73'; kind='offline-development-candidate'
        at=[DateTimeOffset]::Now.ToString('o'); offlineVerified=$true
        nativeRuntimeVerified=$false; fullGoalComplete=$false
        build=[ordered]@{formalProjects=3;minimalDllExampleVerified=$true;offlineSuites=59;cliProcessChecks=25;samplePackValidated=$true;publicMetadataVerified=$true;capabilityIndexVerified=$true}
        dllSHA256=(Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash
        publicApiIndexSHA256=(Get-FileHash -LiteralPath 'docs/public-api.index.json' -Algorithm SHA256).Hash
        pending=@('Current-DLL native UI','Mixed-Mod layouts','Native save and reload')
    }
    $taskReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$OutputDir/verification.json" -Encoding UTF8
    Write-Output "Candidate built: $OutputDir. Native runtime has not been verified."
} finally { Pop-Location }
