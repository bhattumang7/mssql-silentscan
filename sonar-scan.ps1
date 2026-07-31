#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Single entry point for SonarQube analysis of SilentScan.

.DESCRIPTION
    Uses SonarScanner for .NET (dotnet-sonarscanner), which is the ONLY scanner
    that can analyze C#: it hooks into the MSBuild compilation so the Roslyn
    analyzers run. In the same pass it also picks up the non-.NET files
    (corpus manifest, docs, test fixture .sql), so this is the single entry
    point for the whole repo.

    Layers covered:
      - .NET (Core/Cli/Verify/Bench/Tests) .. C#
      - Test fixtures ........................ T-SQL (tests/**/fixtures/*.sql)
      - Infra & ops ........................... Docker Compose, shell
      - Secrets detection ..................... whole repo

.PARAMETER Password
    SonarQube admin password, used to mint a short-lived analysis token.
    A hardcoded default is acceptable only because this script is gitignored
    (see .gitignore) and never committed. Override with -Password if it changes.

.PARAMETER WithCoverage
    Also run the .NET test suite and import coverage. On by default per
    CLAUDE.md's 99% coverage target; pass -WithCoverage:$false to skip for a
    quick lint-only pass.

.EXAMPLE
    ./sonar-scan.ps1
    ./sonar-scan.ps1 -WithCoverage:$false
#>

param(
    [string]$Password     = 'SonarPassword@1',
    [string]$HostUrl      = 'http://localhost:9010',
    [string]$ProjectKey   = 'silentscan',
    [switch]$WithCoverage = $true
)

$ErrorActionPreference = 'Stop'
$RootDir  = $PSScriptRoot
$Solution = Join-Path $RootDir 'SilentScan.slnx'

# -- Preflight ---------------------------------------------------------------
if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
    throw "dotnet-sonarscanner not found. Install it with:`n  dotnet tool install --global dotnet-sonarscanner"
}
if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
    throw "java not found on PATH. SonarScanner for .NET requires a Java 17+ runtime."
}
try {
    $status = Invoke-RestMethod "$HostUrl/api/system/status" -TimeoutSec 10
    if ($status.status -ne 'UP') { throw "SonarQube status is '$($status.status)', expected 'UP'." }
} catch {
    throw "Cannot reach SonarQube at $HostUrl. Is the container running? ($($_.Exception.Message))"
}

# -- Token -------------------------------------------------------------------
Write-Host "Generating a one-time analysis token..." -ForegroundColor Yellow
$credentials = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:$Password"))
$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "$HostUrl/api/user_tokens/generate" `
    -Headers @{ Authorization = "Basic $credentials" } `
    -Body @{ name = "$ProjectKey-scan-$([DateTimeOffset]::Now.ToUnixTimeSeconds())" }
if (-not $tokenResponse.token) { throw "Failed to generate a SonarQube token." }
$Token = $tokenResponse.token

# -- sonar-project.properties ------------------------------------------------
# SonarScanner for .NET refuses to start when this file exists, because it
# derives sonar.sources from the MSBuild graph itself. Every setting the file
# held now lives in the begin step below. Stash it for the run, always restore.
$PropsFile    = Join-Path $RootDir 'sonar-project.properties'
$PropsBackup  = "$PropsFile.scanbak"
$PropsStashed = $false
if (Test-Path $PropsFile) {
    Move-Item -Force $PropsFile $PropsBackup
    $PropsStashed = $true
}

Write-Host ""
Write-Host "=== SonarQube Analysis ===" -ForegroundColor Cyan
Write-Host "Project  : $ProjectKey"
Write-Host "Host     : $HostUrl"
Write-Host "Layers   : .NET (Core/Cli/Verify/Bench/Tests) + SQL fixtures + IaC + secrets"
Write-Host "Coverage : $(if ($WithCoverage) { 'enabled' } else { 'skipped' })"
Write-Host ""

$dotnetTestFailed = $false
$buildFailed      = $false

Push-Location $RootDir
try {
    # -- [1/4] Clean ---------------------------------------------------------
    Write-Host "[1/4] Cleaning previous analysis artifacts..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force (Join-Path $RootDir '.sonarqube')   -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $RootDir 'TestResults')  -ErrorAction SilentlyContinue

    # -- [2/4] Begin ---------------------------------------------------------
    # Notes:
    #  - sonar.sources / sonar.tests are NOT set: the .NET scanner derives them
    #    from the MSBuild graph, and setting them makes it fail. The xUnit
    #    project (SilentScan.Tests) is auto-detected as tests.
    #  - sonar.scanner.scanAll=true is what pulls non-MSBuild files (fixture
    #    .sql, docker-compose.yml, shell scripts) in alongside the compiled C#.
    Write-Host "[2/4] sonarscanner begin..." -ForegroundColor Yellow
    $beginArgs = @(
        "/k:$ProjectKey"
        "/n:SilentScan"
        "/d:sonar.host.url=$HostUrl"
        "/d:sonar.token=$Token"
        "/d:sonar.scanner.scanAll=true"
        "/d:sonar.sourceEncoding=UTF-8"
        "/d:sonar.exclusions=**/bin/**,**/obj/**,**/corpus/**,**/.sonarqube/**,**/*.scanbak"
    )
    if ($WithCoverage) {
        $beginArgs += @(
            "/d:sonar.cs.opencover.reportsPaths=$RootDir/TestResults/**/*.opencover.xml"
            "/d:sonar.cs.vstest.reportsPaths=$RootDir/TestResults/**/*.trx"
        )
    }
    dotnet sonarscanner begin @beginArgs
    if ($LASTEXITCODE -ne 0) { throw "sonarscanner begin failed" }

    # -- [3/4] Build (this is what makes C# analysis happen) -----------------
    # --no-incremental is mandatory: the scanner only sees files that are
    # actually recompiled, so an up-to-date incremental build yields an empty
    # C# analysis. A failing project is not fatal - everything that did compile
    # is still analyzed - but it is called out loudly.
    Write-Host ""
    Write-Host "[3/4] Building solution (Roslyn analyzers run here)..." -ForegroundColor Yellow
    dotnet build $Solution --no-incremental -v minimal --nologo
    if ($LASTEXITCODE -ne 0) {
        $buildFailed = $true
        Write-Warning "Build reported errors. Projects that failed to compile are NOT analyzed - their C# results will be missing. Continuing so the rest of the scan still uploads."
    }

    if ($WithCoverage) {
        Write-Host ""
        Write-Host "      Collecting .NET coverage..." -ForegroundColor Yellow
        dotnet test $Solution --no-build `
            --collect "XPlat Code Coverage;Format=opencover" `
            --results-directory (Join-Path $RootDir 'TestResults') `
            --logger trx `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            $dotnetTestFailed = $true
            Write-Warning ".NET tests had failures - coverage will still be uploaded"
        }
    }

    # -- [4/4] End / upload --------------------------------------------------
    Write-Host ""
    Write-Host "[4/4] sonarscanner end (analysis + upload)..." -ForegroundColor Yellow
    $endOutput = dotnet sonarscanner end /d:sonar.token="$Token" 2>&1
    $endExit   = $LASTEXITCODE

    # Surface what is worth reading; hide routine INFO chatter.
    $endOutput | Where-Object {
        ($_ -match '\b(WARN|ERROR)\b') -or
        ($_ -match 'EXECUTION (SUCCESS|FAILURE)') -or
        ($_ -notmatch '^\d{2}:\d{2}:\d{2}\.\d{3}\s+INFO\s')
    } | ForEach-Object {
        if     ($_ -match '\b(ERROR|FAILURE)\b') { Write-Host $_ -ForegroundColor Red }
        elseif ($_ -match '\bWARN\b')            { Write-Host $_ -ForegroundColor Yellow }
        else                                     { Write-Host $_ }
    }
    if ($endExit -ne 0) { throw "sonarscanner end failed" }

    # -- Wait for background processing --------------------------------------
    # The upload above is async: SonarQube queues a Compute Engine task and
    # returns immediately. Issues aren't queryable against this run's data
    # until that task reports SUCCESS, so poll it rather than assuming the
    # scanner exiting means results are ready.
    $taskIdLine = $endOutput | Select-String -Pattern 'api/ce/task\?id=([\w-]+)' | Select-Object -Last 1
    if ($taskIdLine -and $taskIdLine.Matches[0].Groups[1].Success) {
        $taskId = $taskIdLine.Matches[0].Groups[1].Value
        Write-Host ""
        Write-Host "Waiting for SonarQube to process analysis (task $taskId)..." -ForegroundColor Yellow
        $ceStatus = 'PENDING'
        $elapsed = 0
        while ($ceStatus -in @('PENDING', 'IN_PROGRESS') -and $elapsed -lt 120) {
            Start-Sleep -Seconds 2
            $elapsed += 2
            $task = Invoke-RestMethod -Uri "$HostUrl/api/ce/task?id=$taskId" `
                -Headers @{ Authorization = "Basic $credentials" }
            $ceStatus = $task.task.status
        }
        Write-Host "Processing status: $ceStatus" -ForegroundColor $(if ($ceStatus -eq 'SUCCESS') { 'Green' } else { 'Red' })
        if ($ceStatus -ne 'SUCCESS') { throw "SonarQube background processing did not succeed (status: $ceStatus)" }
    } else {
        Write-Warning "Could not find the Compute Engine task id in scanner output; skipping the processing-complete wait."
    }
}
finally {
    Pop-Location
    if ($PropsStashed -and (Test-Path $PropsBackup)) {
        Move-Item -Force $PropsBackup $PropsFile
    }
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Dashboard: $HostUrl/dashboard?id=$ProjectKey" -ForegroundColor Cyan
if ($buildFailed)      { Write-Warning "Build did not fully succeed - some C# files were not analyzed." }
if ($dotnetTestFailed) { Write-Warning ".NET tests had failures (see above)." }
