#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [ValidateRange(1, 10)][int]$Runs = 3,
    [switch]$Extended,
    [ValidateRange(1, 60000)][double]$MaxStartupMilliseconds = 5000,
    [ValidateRange(1, 4096)][double]$MaxIdlePrivateMiB = 200,
    [ValidateRange(1, 4096)][double]$MaxPopupGrowthMiB = 32,
    [ValidateRange(30, 3600)][int]$IdleSeconds = 30,
    [ValidateSet('both', 'popup', 'widget')][string]$Mode = 'both'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('IdleSeconds') -and -not $Extended) { throw 'IdleSeconds requires the explicit Extended diagnostic workload.' }
$requestedIdleSeconds = if ($Extended) { $IdleSeconds } else { 3 }
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
$diagnosticModes = if ($Mode -eq 'both') { @('popup', 'widget') } else { @($Mode) }
function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}
foreach ($diagnosticMode in $diagnosticModes) {
    for ($run = 1; $run -le $Runs; $run++) {
        $path = Join-Path $output "$diagnosticMode-$run.json"
        $start = [Diagnostics.ProcessStartInfo]::new($executable)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        foreach ($argument in @('--diagnostics', $path, $diagnosticMode)) { $start.ArgumentList.Add($argument) }
        if ($Extended) { $start.ArgumentList.Add('--extended') }
        if ($PSBoundParameters.ContainsKey('IdleSeconds')) {
            $start.ArgumentList.Add('--idle-seconds'); $start.ArgumentList.Add($IdleSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
        }
        $process = [Diagnostics.Process]::Start($start)
        try {
            $timeoutMilliseconds = if ($PSBoundParameters.ContainsKey('IdleSeconds')) { ($IdleSeconds + 60) * 1000 } elseif ($Extended) { 90000 } else { 30000 }
            if (-not $process.WaitForExit($timeoutMilliseconds)) {
                # Only this explicit synthetic diagnostic process is terminated on timeout.
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
                throw "Diagnostic timed out: $diagnosticMode run $run. Inspect $output"
            }
            if ($process.ExitCode -ne 0) {
                $detail = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw } else { 'No diagnostic report was written.' }
                throw "Diagnostic failed: $diagnosticMode run $run. $detail"
            }
        }
        finally { $process.Dispose() }
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if (($report.ContainsKey('RequestedIdleSeconds') -and $report['RequestedIdleSeconds'] -ne $requestedIdleSeconds) -or
            ($PSBoundParameters.ContainsKey('IdleSeconds') -and
                (-not $report.ContainsKey('RequestedIdleSeconds') -or @($report['IdleSamples']).Count -lt 1 -or $report['IdleWallMilliseconds'] -lt ($IdleSeconds * 1000 - 50)))) {
            throw "Unexpected diagnostic idle interval in $path"
        }
        if (-not $report['Succeeded'] -or $report['Mode'] -ne $diagnosticMode -or -not $report['NativeApisAvailable'] -or
            $report['Extended'] -ne $Extended.IsPresent -or
            $report['PopupCycles'] -ne $(if ($Extended) { 100 } else { 20 }) -or
            $report['SettingsCycles'] -ne $(if ($Extended) { 10 } else { 0 }) -or
            $report['WindowsBuild'] -lt 14393 -or $report['Architecture'] -ne 'X64' -or
            -not $report['CodecSmoke']['OriginalPreserved'] -or -not $report['CodecSmoke']['CollisionRefused']) {
            throw "Unexpected compatibility/diagnostic result in $path"
        }
        $reports += $report
        if ($report.ContainsKey('Checkpoints')) {
            foreach ($checkpoint in $report['Checkpoints']) {
                $memory = $checkpoint['Memory']
                Write-Host ("checkpoint {0}/{1}/{2}: private={3:N3} MiB managed={4:N3} MiB lastGCCommitted={5:N3} MiB allocated={6:N3} MiB handles={7} GDI={8} USER={9} windows={10}/{11} GC={12}/{13}/{14}" -f
                    $diagnosticMode, $checkpoint['Phase'], $checkpoint['Cycle'], ($memory['PrivateBytes'] / 1MB), ($memory['ManagedBytes'] / 1MB), ($memory['GcCommittedBytes'] / 1MB), ($memory['AllocatedBytes'] / 1MB),
                    $memory['Handles'], $memory['GdiObjects'], $memory['UserObjects'], $checkpoint['Windows'], $checkpoint['OwnedWindows'], $memory['Gen0Collections'], $memory['Gen1Collections'], $memory['Gen2Collections'])
            }
        }
        Write-Host ("{0} run {1}: first frame {2:N0} ms, hidden private {3:N1} MiB, idle CPU {4:N1} ms" -f
            $diagnosticMode, $run, $report['ProcessStartToContentRenderedMilliseconds'], ($report['HiddenIdle']['PrivateBytes'] / 1MB), $report['IdleCpuMilliseconds'])
    }
}
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $beforeHash) { throw 'The executable changed during measurement.' }
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'The managed application changed during measurement.' }
$summaries = @()
$violations = @()
foreach ($diagnosticMode in $diagnosticModes) {
    $samples = @($reports | Where-Object { $_['Mode'] -eq $diagnosticMode })
    $startups = @($samples | ForEach-Object { $_['ProcessStartToContentRenderedMilliseconds'] })
    $private = @($samples | ForEach-Object { $_['HiddenIdle']['PrivateBytes'] / 1MB })
    $sampledPrivate = @($samples | ForEach-Object {
        if ($_.ContainsKey('IdleSamples')) { $_['IdleSamples'] | ForEach-Object { $_['Memory']['PrivateBytes'] / 1MB } }
        else { $_['HiddenIdle']['PrivateBytes'] / 1MB } # Older default diagnostic reports remain readable.
    })
    $working = @($samples | ForEach-Object { $_['HiddenIdle']['WorkingSetBytes'] / 1MB })
    $growth = @($samples | ForEach-Object { $sample = $_; ($sample['AfterPopupCycles']['PrivateBytes'] - $sample['HiddenIdle']['PrivateBytes']) / 1MB })
    $summaries += [ordered]@{ Mode = $diagnosticMode; Runs = $Runs; MedianStartupMilliseconds = (Get-Median $startups)
        MaxStartupMilliseconds = ($startups | Measure-Object -Maximum).Maximum
        MedianHiddenPrivateMiB = (Get-Median $private); MedianHiddenWorkingSetMiB = (Get-Median $working)
        MaxPopupPrivateGrowthMiB = ($growth | Measure-Object -Maximum).Maximum
        MedianIdleCpuMilliseconds = (Get-Median @($samples | ForEach-Object { $_['IdleCpuMilliseconds'] })) }
    $summaries[-1]['MaxSampledIdlePrivateMiB'] = ($sampledPrivate | Measure-Object -Maximum).Maximum
    if (($startups | Measure-Object -Maximum).Maximum -gt $MaxStartupMilliseconds) { $violations += "$diagnosticMode startup exceeded $MaxStartupMilliseconds ms" }
    if (($sampledPrivate | Measure-Object -Maximum).Maximum -gt $MaxIdlePrivateMiB) { $violations += "$diagnosticMode hidden private memory exceeded $MaxIdlePrivateMiB MiB" }
    if (($growth | Measure-Object -Maximum).Maximum -gt $MaxPopupGrowthMiB) { $violations += "$diagnosticMode popup memory growth exceeded $MaxPopupGrowthMiB MiB" }
}
$summaryPath = Join-Path $output 'summary.json'
$summary = [ordered]@{ SchemaVersion = 1; Extended = $Extended.IsPresent; RequestedIdleSeconds = $requestedIdleSeconds; ExecutableSha256 = $beforeHash; ManagedAssemblySha256 = $assemblyHash; WindowsBuild = $reports[0]['WindowsBuild']
    Framework = $reports[0]['Framework']; CreatedUtc = [DateTime]::UtcNow.ToString('O'); Summaries = $summaries
    Budgets = @{ MaxStartupMilliseconds = $MaxStartupMilliseconds; MaxIdlePrivateMiB = $MaxIdlePrivateMiB; MaxPopupGrowthMiB = $MaxPopupGrowthMiB }
    Violations = $violations; Passed = ($violations.Count -eq 0) }
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 6))
$summaries | ForEach-Object { [pscustomobject]$_ } | Format-Table | Out-Host
if ($violations.Count) { throw "Performance smoke budget failed: $($violations -join '; '). Report: $summaryPath" }
[pscustomobject]@{ Passed = $true; Summary = $summaryPath; Reports = $output }
