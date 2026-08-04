# Tests

Two layers of automated testing, neither of which ships in the released `.unitypackage`
(`.github/workflows/release.yml` filters `Tests/` and `VerificationMod~/` out of the package).

| Layer | Where | What it proves | Needs |
| --- | --- | --- | --- |
| Unit / conversion tests | `Tests/Editor/*.cs` | The converter produces the intended animator, components and animation bindings | Unity only |
| In-game verification | `VerificationMod~/` + the verification avatar | The converted avatar actually behaves correctly **inside ChilloutVR** | ChilloutVR + MelonLoader + an upload |

## 1. Unit and conversion tests (Unity)

EditMode NUnit tests. They live in the predefined `Assembly-CSharp-Editor` (the CCK has no
asmdef, and a test asmdef cannot reference predefined assemblies), so private members are
reached with reflection.

Run them from **Window → General → Test Runner → EditMode → Run All**, or filter on the
`VRC3CVR` class-name prefix.

- `VRC3CVRGestureConversionTests` — gesture number conversion, both `gestureWeightConversionMode`
  modes, weight-driven blend tree restructuring, the derived-weight feed layer, the
  VelocityMagnitude layer, the game-state parameter streams
- `VRC3CVRConstraintConversionTests` — the six constraint types, Target Transform redirect,
  same-type merge, animation binding rebinding (including merged source indices)
- `VRC3CVREndToEndTests` — generates the verification avatar and runs the **whole** conversion
  on it in both gesture weight modes, then asserts on the result

## 2. In-game verification (ChilloutVR)

Unity cannot prove that a converted avatar behaves correctly in ChilloutVR: constraints,
gesture weights, parameter streams and animator-written parameters (AAP) are all client
behavior. This layer converts a purpose-built avatar, uploads it, and has a MelonLoader mod
drive the game and machine-check the results.

There are two avatars in this layer, and they answer different kinds of question:

| Avatar | Built by | Path | Answers |
| --- | --- | --- | --- |
| Verification avatar | `VRC3CVRVerificationAvatar.cs` | VRChat avatar → **converted** → uploaded | Does the conversion produce an avatar that behaves correctly? |
| CVR probe avatar | `VRC3CVRCvrProbeAvatar.cs` | ChilloutVR avatar → uploaded **as-is** | What does the ChilloutVR client hand an animator? |

The probe deliberately skips the conversion. Its questions — the space and unit of the core
velocity parameters, what each `CVRParameterStream` source reports, whether an `AnimatorDriver` can
reconstruct an avatar-local velocity — are about the client, so putting the conversion in the path
would only add a second thing that can be wrong. It is equally deliberate that the probe ships
**inside an uploaded avatar** rather than being assembled at runtime by the mod: a component a mod
adds to a worn avatar is not guaranteed to behave like one that shipped with it (the client may
collect and initialise streams at avatar load, and may cache what each entry resolves against), so
a runtime-assembled probe cannot settle a design question.

The probe is optional — a run without a `probe=` id still checks the conversion in full.

### 2.1 Generate the verification avatar

`Tools → VRC3CVR → Create Verification Avatar` builds a self-contained primitive humanoid
(`VRC3CVRVerificationAvatar.cs`) into the scene, with assets under
`Assets/VRC3CVR_VerificationAvatar/`.

Each gimmick carries a label stating what it should do in game, and the mod asserts the same
thing — so the avatar and `Mod.cs` are the list of verification items; this document does not
repeat it. The expressions menu also has `Show Gesture` / `Show State` / `Show Constraints` so
the groups can be inspected one at a time by hand.

### 2.2 Convert and upload

Convert the avatar with `Tools → VRC3CVR` (once per gesture weight mode) and upload with the
CCK Control Panel, or drive both programmatically — the CCK exposes
`ContentBuilderAPI.BuildAndUpload(assetInfo, BuildConfig.Default, uploadInfo, new LegalAssurance(true, true), ct)`,
which needs no UI (see issue #23).

Tick **Convert locomotion animator**: it is off by default, and the L checks are about the
avatar's own Base layer, which the conversion only looks at when it is on.

Upload once, then **reuse the same content ids on every later run**: an upload replaces whatever
id it is given, and CVR accounts have a limited number of content slots, so uploading fresh each
time burns them. The ids identify personal CVR content and are deliberately not stored in this
repository — write them into `VRC3CVR_VerificationAvatarIds.txt` next to the game (and next to the
Unity project for the editor-side uploader), one per line:

```
fold=<content id of the rewrite-mode avatar>
derived=<content id of the derived-mode avatar>
probe=<content id of the CVR probe avatar>
```

The probe avatar is built by `Tools → VRC3CVR → Create CVR Probe Avatar` and uploaded with the CCK
Control Panel with **no conversion step** — it is already a ChilloutVR avatar.

Both the mod and the uploader refuse to run for an entry that is missing rather than creating a
new avatar.

### 2.3 Build and install the mod

```
cd VerificationMod~
dotnet build -c Release
copy bin\Release\VRC3CVRVerification.dll "<ChilloutVR>\Mods\"
```

The project references the game and MelonLoader assemblies directly; override the game path
with `-p:CvrDir="..."` if it is not the default Steam location.

Put `VRC3CVR_VerificationAvatarIds.txt` (see 2.2) next to `ChilloutVR.exe` so the mod knows which
avatars to wear.

### 2.4 Run

- Create an empty file `<ChilloutVR>\VRC3CVR_AutoVerify.flag` to have the suite run
  automatically once the game is up, or press **F10** in game to start it manually
- **F9** re-runs the checks for the avatar currently worn

The suite switches to each avatar in turn and injects everything it needs — no manual input:

| Injected | How |
| --- | --- |
| Gestures | `PlayerSetup.SetGestureLeft()`, re-applied every frame |
| Walking | Harmony postfix on `CVRInputManager.UpdateInput` (the input system overwrites `movementVector` every frame, so a plain write loses the race). Diagonal, so `VelocityMagnitude` differs from every single axis and the sum of squares is actually proven |
| Crouching | `BetterBetterCharacterController.crouching` |
| Prone | Reflection on the character controller — the member is not guaranteed across client versions, so a rename degrades to "not measured" instead of breaking the build |
| Heading | The player transform's rotation, re-asserted every frame (the movement system owns it otherwise). Used by M1 to tell an avatar-local `VelocityX/Z` from a world-space one: an axis-aligned heading makes both readings identical, so the test would pass for either |
| Mute | `Comms_Manager.IsMicMuted` |
| Menu toggles | `PlayerSetup.ChangeAnimatorParam()` |

Results are appended to `<ChilloutVR>\VRC3CVR_VerificationReport.txt` as `PASS` / `FAIL` /
`INFO` lines, per avatar, and progress goes to the MelonLoader console as `[step]` lines.

### 2.5 Reading the results

- **Watch for the terminal marker, not for the file going quiet.** The suite ends with a single
  `INFO done <timestamp>` line after the last avatar — polling for "no growth for a while" adds
  that whole quiet period to the detection delay and can leave the game running long after there
  is anything to wait for:
  ```
  f="<ChilloutVR>/VRC3CVR_VerificationReport.txt"
  until [ "$(grep -c '^INFO done' "$f")" -ge 1 ]; do sleep 2; done
  ```
- The report is written **per avatar**, so a hang in a later avatar cannot lose earlier results
- A coroutine cannot `try`/`catch` across a `yield`, so every synchronous block runs through a
  helper that turns an exception into a `FAIL` line instead of a silent death
- If the console stops emitting `[step]` lines, the run is stuck — check the last step

### Pitfalls learned the hard way

- **Read parameters by type.** `Animator.GetFloat` silently returns 0 for `Bool` and `Int`
  parameters; use `GetBool` / `GetInteger`. Two checks passed for the wrong reason before this
  was fixed
- **Wait for the avatar id, not just the rig.** Avatar switching is asynchronous and the
  previously worn verification avatar carries the same objects, so the mod waits until the worn
  object's name contains the target content id
- **Teleporting does not create velocity.** `AvatarAnimatorManager.VelocityX` comes from
  `characterMovement.velocity`, so the player has to be moved by the movement system
- **A deactivated constraint keeps its last pose.** It does not return to its rest position (the
  same in VRChat), so C9's OFF case asserts on `constraintActive` / `weight` rather than position
- **Merged constraints apply the offset once.** Sources are weight-averaged, then the surviving
  constraint's offset is added on top — it is not averaged with them
- **The AAP-derived weight is one frame late** by construction, so V1 accepts a match against the
  current or the previous frame
