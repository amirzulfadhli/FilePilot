#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateRange(1, 10)][int]$Runs = 5,
    [string]$TestResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run WPF acceptance on Windows after a Release build.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository 'artifacts'
if (-not $TestResultsDirectory) {
    $TestResultsDirectory = Join-Path $artifacts ('wpf-results/' + [Guid]::NewGuid().ToString('N'))
}
$results = [IO.Path]::GetFullPath($TestResultsDirectory, $repository)
if (-not $results.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $results)) { throw 'TestResultsDirectory must be a new directory inside artifacts.' }
for ($parent = [IO.Path]::GetDirectoryName($results); $parent; $parent = [IO.Path]::GetDirectoryName($parent)) {
    if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Test result paths cannot contain reparse points.'
    }
}
[IO.Directory]::CreateDirectory($results) | Out-Null
Push-Location $repository
try {
    for ($attempt = 1; $attempt -le $Runs; $attempt++) {
        $reportName = "Wpf-$attempt.trx"
        # Each invocation owns a new testhost: WPF permits one Application per process.
        & dotnet test tests/FilePilot.Tests/FilePilot.Tests.csproj -c Release --no-build --no-restore `
            --filter 'FullyQualifiedName=FilePilot.Tests.WindowTests.WindowAndBothThemeDictionariesLoadOnStaThread' `
            --logger "trx;LogFileName=$reportName" --results-directory $results --verbosity minimal | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "WPF repetition $attempt failed. Inspect checkpoints in $results/$reportName." }
        [xml]$report = [IO.File]::ReadAllText((Join-Path $results $reportName))
        $counters = $report.TestRun.ResultSummary.Counters
        if ([int]$counters.total -ne 1 -or [int]$counters.executed -ne 1 -or [int]$counters.passed -ne 1 -or
            [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) {
            throw "WPF repetition $attempt did not execute and pass exactly one test."
        }
    }
    [pscustomobject]@{ Passed = $Runs; Failed = 0; Reports = $results }
}
finally { Pop-Location }
