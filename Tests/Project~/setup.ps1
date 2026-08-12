<#
.SYNOPSIS
Builds the minimal Unity project that the EditMode tests run in.

.DESCRIPTION
Every step is skipped when it is already done, so re-running converges on the same
state instead of rebuilding. Delete the project directory to start over.

Runs no editor: everything here is files. That is what lets CI hand the whole editor
side, licence included, to game-ci/unity-test-runner. See README.md.
#>
[CmdletBinding()]
param(
    # Where to build the project. Kept outside the repository: it is disposable and must
    # not end up inside a Unity project that would try to import it.
    [string]$Path = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'vrc3cvr-test-project')
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$template = 'https://github.com/vrchat-community/template-avatar'
$cckInfoUrl = 'https://api.chilloutvr.net/1/public/cck/info'

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

# 2. VRChat SDK.
Write-Host 'sdk: vrc-get resolve'
vrc-get resolve --project $Path
if ($LASTEXITCODE -ne 0) { throw 'vrc-get resolve failed (install it with: winget install anatawa12.vrc-get)' }

# 3. CCK. The version and the download URL come from the same public API the download
#    page reads, so this tracks whatever CCK is current without a pinned URL here.
#
#    Unpacked rather than imported. A .unitypackage is a gzipped tar of one directory per
#    asset, holding the file (`asset`), its meta (`asset.meta`) and where it belongs
#    (`pathname`) -- an entry with no `asset` is a folder. Putting those where they belong
#    is the whole of what importing does to a project on disk, and doing it here means this
#    script never needs an editor, and so never needs a licence.
$cck = (Invoke-RestMethod $cckInfoUrl).data.cckInfo
$marker = Join-Path $Path '.cck-version'
if ((Test-Path $marker) -and (Get-Content $marker -Raw).Trim() -eq $cck.cckVersion) {
    Write-Host "cck: already $($cck.cckVersion)"
} else {
    Write-Host "cck: unpacking $($cck.cckVersion)"
    # CCK 4 wants a clean import on every update; leftover files break it.
    foreach ($old in 'ABI.CCK', 'ABI.MODS', 'ABI.QA', 'CVR.CCK') {
        $dir = Join-Path $Path "Assets/$old"
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
        if (Test-Path "$dir.meta") { Remove-Item "$dir.meta" -Force }
    }

    $package = Join-Path ([IO.Path]::GetTempPath()) "CCK_$($cck.cckVersion).unitypackage"
    if (-not (Test-Path $package)) {
        Invoke-WebRequest $cck.cckDownloadUrl -OutFile $package
    }

    $unpacked = Join-Path ([IO.Path]::GetTempPath()) "CCK_$($cck.cckVersion)_unpacked"
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    New-Item -ItemType Directory -Force $unpacked | Out-Null
    # .NET rather than the tar on PATH: a GNU tar reads C:\... as a host name, and which
    # tar answers depends on what else is installed
    $stream = [IO.File]::OpenRead($package)
    try {
        $gzip = New-Object IO.Compression.GZipStream($stream, [IO.Compression.CompressionMode]::Decompress)
        try { [System.Formats.Tar.TarFile]::ExtractToDirectory($gzip, $unpacked, $true) }
        finally { $gzip.Dispose() }
    } finally { $stream.Dispose() }

    $assets = (Resolve-Path (Join-Path $Path 'Assets')).Path
    foreach ($entry in Get-ChildItem $unpacked -Directory) {
        $pathnameFile = Join-Path $entry.FullName 'pathname'
        if (-not (Test-Path $pathnameFile)) { continue }
        # the file can carry more than one line; the path is the first
        $pathname = (Get-Content $pathnameFile -TotalCount 1).Trim()

        # An archive says where its own contents go, so it gets to be wrong. Only paths
        # that land inside this project's Assets are written.
        $destination = [IO.Path]::GetFullPath((Join-Path $Path $pathname))
        if (-not $destination.StartsWith($assets + [IO.Path]::DirectorySeparatorChar)) {
            throw "the CCK package wants to write outside Assets: $pathname"
        }

        $asset = Join-Path $entry.FullName 'asset'
        if (Test-Path $asset) {
            New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
            Copy-Item $asset $destination -Force
        } else {
            New-Item -ItemType Directory -Force $destination | Out-Null
        }

        $meta = Join-Path $entry.FullName 'asset.meta'
        if (Test-Path $meta) { Copy-Item $meta "$destination.meta" -Force }
    }
    Remove-Item $unpacked -Recurse -Force

    Set-Content $marker $cck.cckVersion
}

# 4. The scripting defines.
#
#    The SDK and the CCK each add their own from [InitializeOnLoad], and the conversion
#    code and every test sit behind `#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS`. Waiting for
#    them does not work: a `-quit` session ends before the write lands, and a session long
#    enough to write them has already collected its tests from an assembly compiled
#    without them -- so the first run reports zero tests. The CCK also writes its two
#    symbols from the same stale local, so each write drops the other one.
#
#    Both packages are in place by now, so the defines they would add are already true.
$settings = Join-Path $Path 'ProjectSettings/ProjectSettings.asset'
$text = [IO.File]::ReadAllText($settings)

# ProjectSettings.asset holds several `Standalone:` keys; the define list is the one under
# scriptingDefineSymbols. Only that value is rewritten, so every other byte of the file --
# including its line endings -- is left as Unity wrote it.
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
    if ($IsWindows) {
        # Junction over symlink: it needs no elevation and no developer mode. Its target is
        # stored absolute, which is fine for a project only this machine ever opens.
        New-Item -ItemType Junction -Path $link -Target $repo | Out-Null
    } else {
        # Relative, so the link still resolves when the project is mounted somewhere else
        # -- which is exactly what happens when unity-test-runner runs it in a container.
        Push-Location (Split-Path $link)
        try {
            $relative = (Resolve-Path -Relative $repo)
            New-Item -ItemType SymbolicLink -Path (Split-Path $link -Leaf) -Target $relative | Out-Null
        } finally { Pop-Location }
    }
}

Write-Host ''
Write-Host "project ready: $Path"
Write-Host "run the tests with: ./run-tests.ps1 -Path '$Path'"
