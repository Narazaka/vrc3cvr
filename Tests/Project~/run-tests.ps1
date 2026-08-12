<#
.SYNOPSIS
Runs the EditMode tests in the project built by setup.ps1.

.EXAMPLE
./run-tests.ps1
./run-tests.ps1 -Filter VRC3CVRGestureConversionTests
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'vrc3cvr-test-project'),
    [string]$UnityPath,
    # NUnit filter, same syntax as the Test Runner window: a class name or a fully
    # qualified test name. Comma-separate for several.
    [string]$Filter,
    # Conversion tests touch meshes; pass this if one turns out to need a device.
    [switch]$Graphics
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Path 'ProjectSettings/ProjectVersion.txt'))) {
    throw "No test project at $Path. Run ./setup.ps1 first."
}

if (-not $UnityPath) {
    $version = (Select-String -Path (Join-Path $Path 'ProjectSettings/ProjectVersion.txt') `
        -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
    $UnityPath = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
}

$results = Join-Path $Path 'TestResults.xml'
$log = Join-Path $Path 'TestRun.log'
if (Test-Path $results) { Remove-Item $results }

$unityArgs = @(
    '-batchmode'
    '-projectPath', $Path
    '-runTests'
    '-testPlatform', 'EditMode'
    '-testResults', $results
    '-logFile', $log
)
if (-not $Graphics) { $unityArgs += '-nographics' }
if ($Filter) { $unityArgs += @('-testFilter', $Filter) }

# Working directory as in setup.ps1: Unity resolves its built-in shader includes relative
# to its own Editor directory.
$started = Get-Date
$process = Start-Process -FilePath $UnityPath -WorkingDirectory (Split-Path $UnityPath) `
    -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru
$elapsed = (Get-Date) - $started

if (-not (Test-Path $results)) {
    throw "Unity exited $($process.ExitCode) without writing results. See $log"
}

# The log is tens of MB; the result file is what anyone actually wants to read.
$xml = [xml](Get-Content $results -Raw)
$run = $xml.'test-run'
Write-Host ''
# Inconclusive is reported alongside failed: Unity exits 2 for either, so a summary that
# only counted failures would read as a green run that returned an error.
Write-Host ("{0} tests, {1} failed, {2} inconclusive, {3} skipped in {4:n0}s (wall {5:n0}s)" -f `
    $run.total, $run.failed, $run.inconclusive, $run.skipped, [double]$run.duration, $elapsed.TotalSeconds)

foreach ($case in $xml.SelectNodes("//test-case[@result='Inconclusive']")) {
    Write-Host ''
    Write-Host "INCONCLUSIVE $($case.fullname)" -ForegroundColor Yellow
    Write-Host $case.reason.message.InnerText
}

foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
    Write-Host ''
    Write-Host "FAIL $($case.fullname)" -ForegroundColor Red
    Write-Host $case.failure.message.InnerText
}

Write-Host ''
Write-Host "results: $results"
Write-Host "log:     $log"

# Unity exits 2 for an inconclusive test as much as for a failing one. Failures are what
# makes a run bad, so that is what this answers on.
exit ([int]$run.failed -gt 0 ? 1 : 0)
