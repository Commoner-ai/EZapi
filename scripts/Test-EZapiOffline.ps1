param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    # Real source + explicit boundary stubs; this does not verify native gameplay.
    New-Item -ItemType Directory -Path 'work' -Force | Out-Null
    dotnet build src/EZapi.ContentValidator/EZapi.ContentValidator.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Offline validator build failed.' }
    $taskSuites = @(Get-ChildItem 'work/ezapi-*-tests/*.csproj' -File | Sort-Object FullName)
    if ($taskSuites.Count -ne 67) { throw "Expected 67 offline suites, found $($taskSuites.Count)." }
    foreach ($taskSuite in $taskSuites) {
        Write-Output "Running $($taskSuite.Directory.Name)"
        dotnet run --project $taskSuite.FullName -c Release --framework net8.0
        if ($LASTEXITCODE -ne 0) { throw "Offline suite failed: $($taskSuite.Name)" }
    }
    python -X utf8 work/verify-validator-cli.py
    if ($LASTEXITCODE -ne 0) { throw 'CLI process checks failed.' }
    python -X utf8 work/verify-capability-index.py
    if ($LASTEXITCODE -ne 0) { throw 'Capability index checks failed.' }
    [ordered]@{
        passed = $true; offlineSuites = $taskSuites.Count; cliProcessChecks = 25
        nativeRuntimeVerified = $false; gameStarted = $false
        at = [DateTimeOffset]::Now.ToString('o')
    } | ConvertTo-Json | Set-Content work/offline-tests-report.json -Encoding UTF8
} finally { Pop-Location }
