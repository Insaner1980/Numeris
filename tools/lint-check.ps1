[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [switch]$ResolveOnly
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [Console]::OutputEncoding

function Get-RepositoryRoot {
    param([string]$Start)

    $dir = (Resolve-Path -LiteralPath $Start).Path
    while (-not [string]::IsNullOrWhiteSpace($dir)) {
        if (Test-Path -LiteralPath (Join-Path $dir ".git")) {
            return $dir
        }
        $parent = Split-Path -Parent $dir
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $dir) {
            return (Resolve-Path -LiteralPath $Start).Path
        }
        $dir = $parent
    }
}

function Write-ReportHeader {
    param([string]$Path, [string]$Title, [string]$Command)

    Set-Content -LiteralPath $Path -Value @(
        $Title
        "Root: $script:RepoRoot"
        "Command: $Command"
        "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        ""
    )
}

function Invoke-LoggedCommand {
    param(
        [string]$ReportName,
        [string]$Title,
        [string]$CommandText,
        [scriptblock]$Command,
        [string]$SkipReason = ""
    )

    $reportPath = Join-Path $script:ReportsDir $ReportName
    Write-ReportHeader -Path $reportPath -Title $Title -Command $CommandText

    if (-not [string]::IsNullOrWhiteSpace($SkipReason)) {
        Add-Content -LiteralPath $reportPath -Value "SKIPPED: $SkipReason"
        return 0
    }

    Push-Location -LiteralPath $script:RepoRoot
    try {
        & $Command *>&1 | Tee-Object -FilePath $reportPath -Append | Out-Host
        if ($null -ne $global:LASTEXITCODE) {
            return [int]$global:LASTEXITCODE
        }
        return 0
    }
    catch {
        Add-Content -LiteralPath $reportPath -Value $_.Exception.Message
        return 1
    }
    finally {
        Pop-Location
    }
}

function Add-CheckResult {
    param(
        [string]$Name,
        [string]$ReportName,
        [int]$ExitCode,
        [string]$SkipReason = ""
    )

    $script:CheckResults.Add([pscustomobject]@{
        Name = $Name
        Report = Join-Path $script:ReportsDir $ReportName
        ExitCode = $ExitCode
        Skipped = -not [string]::IsNullOrWhiteSpace($SkipReason)
    }) | Out-Null
}

function Show-CheckSummary {
    Write-Host ""
    Write-Host "lint-check summary"
    Write-Host "Root: $script:RepoRoot"
    foreach ($result in $script:CheckResults) {
        $status = if ($result.Skipped) { "SKIPPED" } elseif ($result.ExitCode -eq 0) { "OK" } else { "FAILED ($($result.ExitCode))" }
        Write-Host ("{0}: {1} -> {2}" -f $result.Name, $status, $result.Report)
    }
}

function Get-GradleCommand {
    $gradlewBat = Join-Path $script:RepoRoot "gradlew.bat"
    $gradlew = Join-Path $script:RepoRoot "gradlew"
    if (Test-Path -LiteralPath $gradlewBat) { return $gradlewBat }
    if (Test-Path -LiteralPath $gradlew) { return $gradlew }
    if (Get-Command gradle -ErrorAction SilentlyContinue) { return "gradle" }
    return $null
}

function Get-DotNetSolution {
    Get-ChildItem -LiteralPath $script:RepoRoot -Filter "*.sln*" -File | Select-Object -First 1 -ExpandProperty FullName
}

$script:RepoRoot = Get-RepositoryRoot -Start $Root
if ($ResolveOnly) {
    Write-Output $script:RepoRoot
    exit 0
}

$script:ReportsDir = Join-Path $script:RepoRoot "reports"
New-Item -ItemType Directory -Force -Path $script:ReportsDir | Out-Null

$gradle = Get-GradleCommand
$solution = Get-DotNetSolution
$packageJson = Join-Path $script:RepoRoot "package.json"
$exitCode = 0
$script:CheckResults = [System.Collections.Generic.List[object]]::new()

$code = Invoke-LoggedCommand `
    -ReportName "ktlint.txt" `
    -Title "ktlint" `
    -CommandText "reports/ktlint.txt :: ktlintCheck" `
    -Command { & $gradle "ktlintCheck" "--console=plain" } `
    -SkipReason $(if ($null -eq $gradle) { "No Gradle project or gradle command found." } else { "" })
Add-CheckResult -Name "ktlint" -ReportName "ktlint.txt" -ExitCode $code -SkipReason $(if ($null -eq $gradle) { "No Gradle project or gradle command found." } else { "" })
if ($code -ne 0) { $exitCode = $code }

$code = Invoke-LoggedCommand `
    -ReportName "detekt.txt" `
    -Title "detekt" `
    -CommandText "reports/detekt.txt :: detekt" `
    -Command { & $gradle "detekt" "--console=plain" } `
    -SkipReason $(if ($null -eq $gradle) { "No Gradle project or gradle command found." } else { "" })
Add-CheckResult -Name "detekt" -ReportName "detekt.txt" -ExitCode $code -SkipReason $(if ($null -eq $gradle) { "No Gradle project or gradle command found." } else { "" })
if ($code -ne 0) { $exitCode = $code }

if ($null -ne $gradle) {
    $lintCommand = { & $gradle "lint" "--console=plain" }
    $lintText = "reports/lint.txt :: Android lint"
    $skip = ""
}
elseif ($null -ne $solution) {
    $lintCommand = { dotnet format $solution --verify-no-changes --verbosity diagnostic }
    $lintText = "reports/lint.txt :: dotnet format --verify-no-changes"
    $skip = ""
}
elseif (Test-Path -LiteralPath $packageJson) {
    $lintCommand = { npm run check }
    $lintText = "reports/lint.txt :: npm run check"
    $skip = ""
}
else {
    $lintCommand = { }
    $lintText = "reports/lint.txt"
    $skip = "No Gradle, .NET solution, or package.json check target found."
}

$code = Invoke-LoggedCommand `
    -ReportName "lint.txt" `
    -Title "lint" `
    -CommandText $lintText `
    -Command $lintCommand `
    -SkipReason $skip
Add-CheckResult -Name "lint" -ReportName "lint.txt" -ExitCode $code -SkipReason $skip
if ($code -ne 0) { $exitCode = $code }

Show-CheckSummary
exit $exitCode
