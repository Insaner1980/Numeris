#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$PlanOnly,
    [switch]$AllowExternalUpload,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$SonarArgs
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [Console]::OutputEncoding

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$ReportPath
    )

    & dotnet @Arguments 2>&1 |
        ForEach-Object {
            # Keep credentials out of console output and the saved report.
            $line = [string]$_
            if (-not [string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
                $line = $line.Replace($env:SONAR_TOKEN, '[REDACTED_SECRET]')
            }
            $line
        } |
        Tee-Object -FilePath $ReportPath -Append |
        Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-komento epäonnistui (exit $LASTEXITCODE)."
    }
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$reportsDir = Join-Path $projectRoot 'reports'
$reportPath = Join-Path $reportsDir 'sonar.txt'
$coveragePath = Join-Path $reportsDir 'sonar-coverage.xml'
$projectKey = 'Insaner1980_Numeris'
$organization = 'insaner1980'
$projectUrl = "https://sonarcloud.io/project/overview?id=$projectKey"

if ($PlanOnly) {
    Write-Output @(
        'sonar'
        '  - dotnet restore + Debug/x64 build + Numeris.Tests console harness with XML coverage + SonarQube Cloud upload'
        '  - requires SONAR_TOKEN (the local sonar profile loads the saved CLI credential)'
        '  - direct script invocation requires -AllowExternalUpload'
        "  - project: $projectKey"
        "  - organization: $organization"
        '  - host: https://sonarcloud.io'
        "  - report: $reportPath"
        "  - coverage: $coveragePath"
    )
    exit 0
}

if ($SonarArgs.Count -gt 0) {
    $cli = Get-Command sonar.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $cli) {
        throw 'sonar.exe ei löytynyt PATHista.'
    }
    & $cli.Source @SonarArgs
    exit $LASTEXITCODE
}

if (-not $AllowExternalUpload) {
    throw 'Sonar-analyysi lähettää analyysituloksen SonarQube Cloudiin. Käytä -AllowExternalUpload.'
}
if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
    throw 'SONAR_TOKEN ei ole asetettu tälle PowerShell-istunnolle.'
}

New-Item -ItemType Directory -Force -Path $reportsDir | Out-Null
if (Test-Path -LiteralPath $coveragePath) {
    Remove-Item -LiteralPath $coveragePath -Force
}
Set-Content -LiteralPath $reportPath -Encoding utf8 -Value @(
    'sonar'
    "Root: $projectRoot"
    "Project: $projectKey"
    "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ''
)

Push-Location -LiteralPath $projectRoot
try {
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @('tool', 'restore')
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'restore', 'Numeris.Tests/Numeris.Tests.csproj', '-p:Platform=x64'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'run', 'dotnet-sonarscanner', '--', 'begin'
        "/k:$projectKey"
        "/o:$organization"
        '/d:sonar.host.url=https://sonarcloud.io'
        "/d:sonar.token=$env:SONAR_TOKEN"
        '/d:sonar.cs.vscoveragexml.reportsPaths=reports/sonar-coverage.xml'
        '/d:sonar.exclusions=**/bin/**,**/obj/**,reports/**,Numeris/AppPackages/**,Numeris/Assets/**'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'build', 'Numeris.Tests/Numeris.Tests.csproj', '-c', 'Debug', '-p:Platform=x64'
        '--no-restore', '--no-incremental', '-m:1'
    )
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'run', 'dotnet-coverage', '--', 'collect'
        'dotnet run --project Numeris.Tests/Numeris.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --no-launch-profile'
        '-f', 'xml', '-o', $coveragePath
    )
    if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) {
        throw 'Kattavuusraportti puuttuu. Analyysia ei lähetetty.'
    }
    [xml]$coverage = Get-Content -LiteralPath $coveragePath -Raw
    if ($coverage.DocumentElement.Name -ne 'results' -or
        $null -eq $coverage.SelectSingleNode('/results/modules/module[@name="Numeris.dll" or @name="numeris.dll"]')) {
        throw 'Kattavuusraportti ei sisällä Numeris-moduulin VS Coverage XML -dataa. Analyysia ei lähetetty.'
    }
    Invoke-DotNetCommand -ReportPath $reportPath -Arguments @(
        'tool', 'run', 'dotnet-sonarscanner', '--', 'end'
        "/d:sonar.token=$env:SONAR_TOKEN"
    )
}
finally {
    Pop-Location
}

Write-Output "Tulokset: $projectUrl"
