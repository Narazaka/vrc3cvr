# The test project

Two scripts that build a Unity project holding **only** what the EditMode tests need —
Unity 2022.3.22f1, the VRChat SDK, the CCK and vrc3cvr itself — and run the tests in it.

The project this repository is normally developed in carries hundreds of VPM packages and
a full avatar's assets. That is what makes a domain reload take minutes, and it is why the
whole suite takes as long as it does. None of it is needed to convert an avatar.

The scripts also make the suite runnable from a command line rather than from the Test
Runner window, which is the shape a CI job needs.

```powershell
./setup.ps1                                       # build it (minutes on the first run)
./run-tests.ps1                                   # whole suite
./run-tests.ps1 -Filter VRC3CVRGestureConversionTests
```

The project is built at `%LOCALAPPDATA%\vrc3cvr-test-project`; pass `-Path` for somewhere
else. vrc3cvr is linked into it, not copied, so the working copy is what gets tested.

Everything `setup.ps1` does is skipped when it is already done, so re-run it whenever the
CCK releases a new version. Delete the project directory to start over.

That also means a failed run is fixed by running it again: `vrc-get` has been seen to fail
with `os error 5` on freshly cloned files, which did not reproduce from an identical clean
state, so it is something on the machine holding a handle rather than a step that is wrong.

## Requirements

`setup.ps1` starts no editor: it clones, downloads, unpacks and links, and that is all.
Only `run-tests.ps1` needs Unity.

- [`vrc-get`](https://github.com/vrc-get/vrc-get) on `PATH` — `winget install anatawa12.vrc-get`
- `git`
- Unity 2022.3.22f1, for `run-tests.ps1`. Pass `-UnityPath` if it is not in the Hub's
  default location

The VRChat SDK and the CCK are both fetched without credentials: the SDK through
`vrc-get`, the CCK from the same public API the CCK download page reads, which reports the
current version and its download URL.

The CCK is unpacked rather than imported. A `.unitypackage` is a gzipped tar holding one
directory per asset — the file, its `.meta`, and the path it belongs at — so putting those
where they belong is all an import does to a project on disk. Doing it without an editor is
what keeps a licence out of this script, and out of CI.

## In CI

`.github/workflows/tests.yml` builds the project with `setup.ps1` and then hands the
editor, the licence and the test run to
[game-ci/unity-test-runner](https://game.ci/docs/github/test-runner). `run-tests.ps1` is not
used there: it is for running the suite by hand.

Three secrets, all of them the action's: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.
`UNITY_LICENSE` holds a Personal licence file (`.ulf`), contents and all — activate one in
Unity Hub under `Preferences → Licenses → Add` and copy what lands in
`C:\ProgramData\Unity\Unity_lic.ulf`. A licence is not tied to a platform, so one activated
on Windows is what a Linux runner uses. Taking a licence overwrites that file, so put the
one already there aside first if it belongs to a different account.

**A licence file carries a serial, and an editor prints that serial into its log** with the
last four characters blanked. GitHub's masking covers the console and not the contents of a
file, so the workflow uploads the test results rather than the folder the action puts them
in, which also holds that log. The console shows the log either way.

## This does not replace the Test Runner window

`Tools/VRC3CVR Repro/Run VRC3CVR Tests` and the filter file it reads still work, and are
still the way to run tests inside a full project against a real avatar. See `../README.md`.
