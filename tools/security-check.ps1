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

function Write-CheckSummary {
    Write-Host ""
    Write-Host "security-check summary"
    Write-Host "Root: $script:RepoRoot"
    foreach ($result in $script:CheckResults) {
        $status = if ($result.Skipped) { "SKIPPED" } elseif ($result.ExitCode -eq 0) { "OK" } else { "FAILED ($($result.ExitCode))" }
        Write-Host ("{0}: {1} -> {2}" -f $result.Name, $status, $result.Report)
    }
}

function Get-DotNetSolution {
    Get-ChildItem -LiteralPath $script:RepoRoot -Filter "*.sln*" -File | Select-Object -First 1 -ExpandProperty FullName
}

function Get-DependencyCheckCommand {
    foreach ($name in @("dependency-check.bat", "dependency-check", "dependency-check.sh")) {
        $cmd = Get-Command $name -CommandType Application,ExternalScript -ErrorAction SilentlyContinue
        if ($null -ne $cmd) {
            return $cmd.Source
        }
    }
    return $null
}

$script:RepoRoot = Get-RepositoryRoot -Start $Root
if ($ResolveOnly) {
    Write-Output $script:RepoRoot
    exit 0
}

$script:ReportsDir = Join-Path $script:RepoRoot "reports"
New-Item -ItemType Directory -Force -Path $script:ReportsDir | Out-Null

$semgrep = Get-Command semgrep -ErrorAction SilentlyContinue
$dependencyCheck = Get-DependencyCheckCommand
$solution = Get-DotNetSolution
$packageLock = Join-Path $script:RepoRoot "package-lock.json"
$cargoLock = Join-Path $script:RepoRoot "Cargo.lock"
$semgrepConfig = if ($null -ne $solution) { "p/csharp" } elseif (Test-Path -LiteralPath $packageLock) { "p/javascript" } elseif (Test-Path -LiteralPath $cargoLock) { "p/rust" } else { "p/default" }
$exitCode = 0
$script:CheckResults = [System.Collections.Generic.List[object]]::new()

$code = Invoke-LoggedCommand `
    -ReportName "security-code.txt" `
    -Title "semgrep" `
    -CommandText "reports/security-code.txt :: semgrep scan --config $semgrepConfig --error --metrics=off" `
    -Command { semgrep scan --config $semgrepConfig --error --metrics=off . } `
    -SkipReason $(if ($null -eq $semgrep) { "semgrep was not found on PATH." } else { "" })
Add-CheckResult -Name "semgrep" -ReportName "security-code.txt" -ExitCode $code -SkipReason $(if ($null -eq $semgrep) { "semgrep was not found on PATH." } else { "" })
if ($code -ne 0) { $exitCode = $code }

if ($null -ne $dependencyCheck) {
    $depsCommand = {
        & $dependencyCheck `
            "--project" (Split-Path -Leaf $script:RepoRoot) `
            "--scan" $script:RepoRoot `
            "--out" $script:ReportsDir `
            "--format" "HTML" `
            "--format" "JSON" `
            "--prettyPrint"
    }
    $depsText = "reports/security-deps.txt :: OWASP dependency-check"
    $skip = ""
}
elseif ($null -ne $solution) {
    $depsCommand = { dotnet list $solution package --vulnerable --include-transitive }
    $depsText = "reports/security-deps.txt :: dotnet list package --vulnerable --include-transitive"
    $skip = "OWASP dependency-check was not found; using dotnet vulnerability audit fallback."
}
elseif (Test-Path -LiteralPath $packageLock) {
    $depsCommand = { npm audit --audit-level=moderate }
    $depsText = "reports/security-deps.txt :: npm audit --audit-level=moderate"
    $skip = "OWASP dependency-check was not found; using npm audit fallback."
}
elseif ((Test-Path -LiteralPath $cargoLock) -and (Get-Command cargo-audit -ErrorAction SilentlyContinue)) {
    $depsCommand = { cargo audit }
    $depsText = "reports/security-deps.txt :: cargo audit"
    $skip = "OWASP dependency-check was not found; using cargo audit fallback."
}
else {
    $depsCommand = { }
    $depsText = "reports/security-deps.txt"
    $skip = "No OWASP dependency-check command or supported fallback dependency audit was found."
}

if ($skip -like "OWASP dependency-check was not found; using *") {
    $reportPath = Join-Path $script:ReportsDir "security-deps.txt"
    Write-ReportHeader -Path $reportPath -Title "dependency audit" -Command $depsText
    Add-Content -LiteralPath $reportPath -Value "NOTE: $skip"
    Push-Location -LiteralPath $script:RepoRoot
    try {
        & $depsCommand *>&1 | Tee-Object -FilePath $reportPath -Append
        if ($null -ne $global:LASTEXITCODE -and $global:LASTEXITCODE -ne 0) {
            $exitCode = [int]$global:LASTEXITCODE
        }
        Add-CheckResult -Name "dependency audit" -ReportName "security-deps.txt" -ExitCode $(if ($null -ne $global:LASTEXITCODE) { [int]$global:LASTEXITCODE } else { 0 })
    }
    finally {
        Pop-Location
    }
}
else {
    $code = Invoke-LoggedCommand `
        -ReportName "security-deps.txt" `
        -Title "dependency audit" `
        -CommandText $depsText `
        -Command $depsCommand `
        -SkipReason $skip
    Add-CheckResult -Name "dependency audit" -ReportName "security-deps.txt" -ExitCode $code -SkipReason $skip
    if ($code -ne 0) { $exitCode = $code }
}

Write-CheckSummary
exit $exitCode
