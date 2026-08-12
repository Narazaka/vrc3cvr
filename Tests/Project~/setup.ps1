<#
.SYNOPSIS
Builds the minimal Unity project that the EditMode tests run in.

.DESCRIPTION
Every step is skipped when it is already done, so re-running converges on the same
state instead of rebuilding. Delete the project directory to start over.

See README.md for what this exists for.
#>
[CmdletBinding()]
param(
    # Where to build the project. Kept outside the repository: it is disposable and must
    # not end up inside a Unity project that would try to import it.
    [string]$Path = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'vrc3cvr-test-project'),
    # Unity executable. Resolved from the Hub's default location for the project's own
    # editor version when not given.
    [string]$UnityPath
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$template = 'https://github.com/vrchat-community/template-avatar'
$cckInfoUrl = 'https://api.chilloutvr.net/1/public/cck/info'

# Unity must run with its own Editor directory as the working directory, otherwise the
# shader compiler cannot resolve the built-in CGIncludes and everything renders pink.
function Invoke-Unity([string]$exe, [string[]]$unityArgs) {
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) `
        -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru
    return $process.ExitCode
}

function Resolve-Unity([string]$projectPath) {
    if ($UnityPath) { return $UnityPath }
    $version = (Select-String -Path (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt') `
        -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
    $exe = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
    if (-not (Test-Path $exe)) {
        throw "Unity $version not found at $exe. Install it or pass -UnityPath."
    }
    return $exe
}

# 1. The project skeleton. Cloning VRChat's own template rather than assembling one by
#    hand keeps the ProjectSettings the SDK expects, so a test that fails here fails for
#    a reason other than the environment.
if (Test-Path (Join-Path $Path 'ProjectSettings/ProjectVersion.txt')) {
    Write-Host "project: already at $Path"
} else {
    Write-Host "project: cloning $template"
    git clone --depth 1 $template $Path
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    # The project holds no state worth versioning.
    Remove-Item (Join-Path $Path '.git') -Recurse -Force
}

$unity = Resolve-Unity $Path

# 2. VRChat SDK.
Write-Host 'sdk: vrc-get resolve'
vrc-get resolve --project $Path
if ($LASTEXITCODE -ne 0) { throw 'vrc-get resolve failed (install it with: winget install anatawa12.vrc-get)' }

# 3. CCK. The version and the download URL come from the same public API the download
#    page reads, so this tracks whatever CCK is current without a pinned URL here.
$cck = (Invoke-RestMethod $cckInfoUrl).data.cckInfo
$marker = Join-Path $Path '.cck-version'
if ((Test-Path $marker) -and (Get-Content $marker -Raw).Trim() -eq $cck.cckVersion) {
    Write-Host "cck: already $($cck.cckVersion)"
} else {
    Write-Host "cck: importing $($cck.cckVersion)"
    # CCK 4 requires a clean import on every update; leftover files break it.
    foreach ($old in 'ABI.CCK', 'ABI.MODS', 'ABI.QA', 'CVR.CCK') {
        $dir = Join-Path $Path "Assets/$old"
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
        if (Test-Path "$dir.meta") { Remove-Item "$dir.meta" -Force }
    }
    $package = Join-Path ([IO.Path]::GetTempPath()) "CCK_$($cck.cckVersion).unitypackage"
    if (-not (Test-Path $package)) {
        Invoke-WebRequest $cck.cckDownloadUrl -OutFile $package
    }
    $exit = Invoke-Unity $unity @('-batchmode', '-quit', '-nographics',
        '-projectPath', $Path, '-importPackage', $package, '-logFile', '-')
    if ($exit -ne 0) { throw "CCK import failed (exit $exit)" }
    Set-Content $marker $cck.cckVersion
}

# 4. The scripting defines, written before Unity ever compiles vrc3cvr.
#
#    The SDK and the CCK each add their own from [InitializeOnLoad], and the conversion
#    code and every test sit behind `#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS`. Waiting for
#    them does not work: a `-quit` session ends before the write lands, and a session long
#    enough to write them has already collected its tests from an assembly compiled
#    without them — so the first run reports zero tests. The CCK also writes its two
#    symbols from the same stale local, so each write drops the other one.
#
#    Both packages are installed by now, so the defines they are about to add are already
#    true. Writing them here just makes them true one compile earlier.
$settings = Join-Path $Path 'ProjectSettings/ProjectSettings.asset'
$text = [IO.File]::ReadAllText($settings)

# ProjectSettings.asset holds several `Standalone:` keys; the define list is the one under
# scriptingDefineSymbols. Only that value is rewritten, so every other byte of the file —
# including its line endings — is left as Unity wrote it.
$key = "`n    Standalone: "
$anchor = $text.IndexOf('scriptingDefineSymbols:')
if ($anchor -lt 0) { throw "no scriptingDefineSymbols in $settings" }
$at = $text.IndexOf($key, $anchor)
if ($at -lt 0) { throw "no Standalone entry under scriptingDefineSymbols in $settings" }

$from = $at + $key.Length
$to = $text.IndexOfAny([char[]]@("`r", "`n"), $from)
$defines = $text.Substring($from, $to - $from)
$missing = @('VRC_SDK_VRCSDK3', 'CVR_CCK_EXISTS', 'CVR_CCK_4_OR_NEWER' |
    Where-Object { $defines -split ';' -notcontains $_ })
if ($missing.Count -eq 0) {
    Write-Host "defines: already $defines"
} else {
    $defines = (@($defines -split ';' | Where-Object { $_ }) + $missing) -join ';'
    [IO.File]::WriteAllText($settings, $text.Substring(0, $from) + $defines + $text.Substring($to))
    Write-Host "defines: added $($missing -join ', ')"
}

# 5. vrc3cvr itself, linked rather than copied so the working copy is what gets tested.
$link = Join-Path $Path 'Assets/PeanutTools/VRC3CVR'
if (Test-Path $link) {
    Write-Host 'vrc3cvr: already linked'
} else {
    Write-Host "vrc3cvr: linking $repo"
    New-Item -ItemType Directory -Force (Split-Path $link) | Out-Null
    # Junction over symlink on Windows: it needs no elevation and no developer mode.
    $type = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $type -Path $link -Target $repo | Out-Null
}

Write-Host ''
Write-Host "project ready: $Path"
Write-Host "run the tests with: ./run-tests.ps1 -Path '$Path'"
