#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [ValidateRange(1, 10)][int]$Runs = 3,
    [ValidateRange(1, 60000)][double]$MaxStartupMilliseconds = 5000,
    [ValidateRange(1, 4096)][double]$MaxIdlePrivateMiB = 200,
    [ValidateRange(1, 4096)][double]$MaxPopupGrowthMiB = 32
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run FilePilot measurements on Windows.' }
$executable = (Get-Item -LiteralPath $ExecutablePath).FullName
if ([IO.Path]::GetFileName($executable) -ine 'FilePilot.exe') { throw 'Choose a built or extracted FilePilot.exe.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repository ('artifacts/performance/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($output) | Out-Null
$beforeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
$assembly = Join-Path ([IO.Path]::GetDirectoryName($executable)) 'FilePilot.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$reports = @()
function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}
foreach ($mode in @('popup', 'widget')) {
    for ($run = 1; $run -le $Runs; $run++) {
        $path = Join-Path $output "$mode-$run.json"
        $start = [Diagnostics.ProcessStartInfo]::new($executable)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        foreach ($argument in @('--diagnostics', $path, $mode)) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(30000)) {
                # Only this explicit synthetic diagnostic process is terminated on timeout.
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
                throw "Diagnostic timed out: $mode run $run. Inspect $output"
            }
            if ($process.ExitCode -ne 0) {
                $detail = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw } else { 'No diagnostic report was written.' }
                throw "Diagnostic failed: $mode run $run. $detail"
            }
        }
        finally { $process.Dispose() }
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if (-not $report['Succeeded'] -or $report['Mode'] -ne $mode -or -not $report['NativeApisAvailable'] -or
            $report['WindowsBuild'] -lt 14393 -or $report['Architecture'] -ne 'X64' -or
            -not $report['CodecSmoke']['OriginalPreserved'] -or -not $report['CodecSmoke']['CollisionRefused']) {
            throw "Unexpected compatibility/diagnostic result in $path"
        }
        $reports += $report
        Write-Host ("{0} run {1}: first frame {2:N0} ms, hidden private {3:N1} MiB, idle CPU {4:N1} ms" -f
            $mode, $run, $report['ProcessStartToContentRenderedMilliseconds'], ($report['HiddenIdle']['PrivateBytes'] / 1MB), $report['IdleCpuMilliseconds'])
    }
}
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $beforeHash) { throw 'The executable changed during measurement.' }
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'The managed application changed during measurement.' }
$summaries = @()
$violations = @()
foreach ($mode in @('popup', 'widget')) {
    $samples = @($reports | Where-Object { $_['Mode'] -eq $mode })
    $startups = @($samples | ForEach-Object { $_['ProcessStartToContentRenderedMilliseconds'] })
    $private = @($samples | ForEach-Object { $_['HiddenIdle']['PrivateBytes'] / 1MB })
    $working = @($samples | ForEach-Object { $_['HiddenIdle']['WorkingSetBytes'] / 1MB })
    $growth = @($samples | ForEach-Object { $sample = $_; ($sample['AfterPopupCycles']['PrivateBytes'] - $sample['HiddenIdle']['PrivateBytes']) / 1MB })
    $summaries += [ordered]@{ Mode = $mode; Runs = $Runs; MedianStartupMilliseconds = (Get-Median $startups)
        MaxStartupMilliseconds = ($startups | Measure-Object -Maximum).Maximum
        MedianHiddenPrivateMiB = (Get-Median $private); MedianHiddenWorkingSetMiB = (Get-Median $working)
        MaxPopupPrivateGrowthMiB = ($growth | Measure-Object -Maximum).Maximum
        MedianIdleCpuMilliseconds = (Get-Median @($samples | ForEach-Object { $_['IdleCpuMilliseconds'] })) }
    if (($startups | Measure-Object -Maximum).Maximum -gt $MaxStartupMilliseconds) { $violations += "$mode startup exceeded $MaxStartupMilliseconds ms" }
    if (($private | Measure-Object -Maximum).Maximum -gt $MaxIdlePrivateMiB) { $violations += "$mode hidden private memory exceeded $MaxIdlePrivateMiB MiB" }
    if (($growth | Measure-Object -Maximum).Maximum -gt $MaxPopupGrowthMiB) { $violations += "$mode popup memory growth exceeded $MaxPopupGrowthMiB MiB" }
}
$summaryPath = Join-Path $output 'summary.json'
$summary = [ordered]@{ SchemaVersion = 1; ExecutableSha256 = $beforeHash; ManagedAssemblySha256 = $assemblyHash; WindowsBuild = $reports[0]['WindowsBuild']
    Framework = $reports[0]['Framework']; CreatedUtc = [DateTime]::UtcNow.ToString('O'); Summaries = $summaries
    Budgets = @{ MaxStartupMilliseconds = $MaxStartupMilliseconds; MaxIdlePrivateMiB = $MaxIdlePrivateMiB; MaxPopupGrowthMiB = $MaxPopupGrowthMiB }
    Violations = $violations; Passed = ($violations.Count -eq 0) }
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 6))
$summaries | ForEach-Object { [pscustomobject]$_ } | Format-Table | Out-Host
if ($violations.Count) { throw "Performance smoke budget failed: $($violations -join '; '). Report: $summaryPath" }
[pscustomobject]@{ Passed = $true; Summary = $summaryPath; Reports = $output }
