# VRC3CVR

- **[日本語](README.ja.md)**

Convert a VRChat SDK3 avatar to ChilloutVR with this Unity script.

- Keep a BACKUP of your original project.
- This is a fork of the forked project (https://github.com/SaracenOne/vrc3cvr) that is a fork of the original project (https://github.com/imagitama/vrc3cvr).
- It is not recommended you convert avatars with custom locomotion controllers, only FX and Gesture controllers.

Tested with:

- VRChat Avatar SDK3 3.10.2
- ChilloutVR CCK_4.0.0
- Unity 2022.3.22f1 (VRChat compatible)

## What does it do?

Most things work as-is, except for PhysBone-specific features and features that require layers other than FX/Gesture.

- adds a ChilloutVR avatar component (if missing)
- sets the face mesh
- sets the visemes
- sets the blink blendshapes
- sets the viewpoint and voice position to the VRChat avatar viewpoint
- adds an advanced avatar setting for each VRChat parameter
  - sliders for all float params
  - toggle for all boolean params
  - dropdown for all int params (toggle if only 1 int found)
- converts each animator controller (gestures, FX, etc.) to support ChilloutVR's gesture system
  - references to `GestureLeftWeight`/`GestureRightWeight` are converted (two selectable modes: rewrite onto `GestureLeft`/`GestureRight` with no latency, or generate a weight parameter that reproduces every usage with one frame of latency)
  - converts VRCParameterDriver etc.
- Convert VRC Contact Senders and Receivers to CVR Pointer and CVR Advanced Avatar Trigger
  - Unlike VRC Contact, CVR Pointer and Trigger only change values when the contact collides. This difference may cause compatibility issues.
  - Changing Shape Type in game is not supported.
- converts VRC Constraints to Unity Constraints (including animated constraint properties)

### Unsupported Shaders

ChilloutVR implements SPS-I (Single Pass Stereo Instancing), which was previously discussed for VRChat but never implemented there.

Shaders that do not support SPS-I may render incorrectly (e.g., only visible in one eye in VR).

lilToon and other major shaders should work fine, but less common shaders may have issues.

## Usage

### Tools

- VRC3CVR: Go to [Releases](https://github.com/Narazaka/vrc3cvr/releases/latest) and expand "Assets" and download the `.unitypackage`.
- CCK4: [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/)
- Modular Avatar (optional): https://modular-avatar.nadena.dev/
- PhysBone to DynamicBone converter (optional): https://github.com/FACS01-01/PhysBone-to-DynamicBone
- DynamicBone stub if you haven't purchased it (optional): https://github.com/VRLabs/Dynamic-Bones-Stub

### 0. Convert PhysBones to DynamicBones (optional)

You can skip this step if you just want to try it out.

Use the following tool:

- https://github.com/FACS01-01/PhysBone-to-DynamicBone

You don't need to buy DynamicBones! Use these alternatives:

- https://github.com/VRLabs/Dynamic-Bones-Stub
- https://github.com/Markcreator/VRChat-Tools

**What about child bones that should not move?**

CVR's DynamicBone seems to be an older version, so Exclusion (equivalent to PhysBone's ignore) does not work on bones directly under the Root.

To solve this, I created a tool called [ExcludeChildBones](https://github.com/Narazaka/ExcludeChildBones). Use it if needed. (It works via Modular Avatar (NDMF), so add the component, configure it, then bake again.)

### 1. Convert

There are two ways to convert. **Uploading from the CCK Control Panel converts automatically** — you do not need to convert by hand first. Convert manually only when you want to inspect the result in Unity.

#### Automatic (recommended)

1. Setup your VRChat avatars with Unity 2022.3.22f1 / VRChat SDK 3.x (use VCC)
2. (optional) convert your PhysBones to DynamicBones by [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone) etc.
3. Import ChilloutVR CCK4 to the VRChat avatar project.
4. Import the vrc3cvr `.unitypackage`
5. Add a **VRC3CVR Avatar** component to the avatar root. A **CVRAvatar** component is added with it — leave it alone, the conversion fills it in.
6. Upload from the CCK Control Panel as you normally would.

The conversion runs during the upload, the same way Modular Avatar and VRCFury run during a VRChat upload. Nothing is left behind in your scene.

#### Manual

Use this when you want to look at the converted avatar in Unity before uploading.

1. Steps 1-5 above
2. Press **Convert** in the `VRC3CVR Avatar` inspector, or open **Tools** -> **VRC3CVR** and pick the avatar there. Both edit the same settings.

**Auto bake** is on by default, so VRCFury, Modular Avatar, Avatar Optimizer and any other tool that hooks the VRChat SDK build is applied automatically before the conversion. You no longer need to bake the avatar yourself first. This applies to both ways of converting.

> The avatar a **manual** conversion produces references generated assets that live in a temporary folder and are destroyed by the next build. Upload it rather than keeping it. Uploading from the CCK Control Panel is not affected — the bundle is built before anything is discarded.

### 2. Upload

Upload from the CCK Control Panel. If the avatar still has a `VRC3CVR Avatar` component, it is converted automatically during the upload, so upload the VRChat avatar itself — there is no separate converted object to pick.

## Tips

### Upload limit

There are only about 20 avatar slots by default.

You may be able to get more by subscribing.

### Japanese localization

ChilloutVR's default UI garbles non-ASCII characters (such as Japanese).

If you want to use it in Japanese, install the [Japanese localization patch](https://github.com/Narazaka/chilloutvr-jp-translation-tool).

### CVR is less convenient than VRC

In CVR, inconveniences are resolved with mods.

Use https://github.com/knah/CVRMelonAssistant etc. to install mods.

(CVR was originally created as a platform in response to VRChat's ban on mods.)

## Troubleshooting

### "VRCExpressionParameters.Parameter does not contain a definition for defaultValue" or another VRChat error

Update to a more recent version. Tested with at least VRChat Avatar SDK3.

### When performing a gesture my hands do not animate

Uncheck "My avatar has custom hand animations" and convert.

### "The type or namespace 'VRC' could not be found"

You need the VRC SDK in your project.

## Conversion Details

### Mapping gestures

Mapping of VRC gestures to CVR:

| Gesture     | VRC | CVR |
| ----------- | --- | --- |
| Nothing     | 0   | 0   |
| Fist        | 1   | 1   |
| Open Hand   | 2   | -1  |
| Point       | 3   | 4   |
| Peace       | 4   | 5   |
| Rock'n'Roll | 5   | 6   |
| Gun         | 6   | 3   |
| Thumbs Up   | 7   | 2   |

#### Trigger weight

VRC has two parameters `GestureLeftWeight` and `GestureRightWeight` (0 while Neutral, the analog squeeze while Fist, fixed 1 for other gestures). They do not exist in CVR; instead the `GestureLeft` value itself is the squeeze amount during Fist (0.5 is 50% of the trigger). vrc3cvr offers two conversion modes: rewriting weight references onto `GestureLeft` (no latency), or generating a weight parameter that reproduces the VRChat behavior for every usage (one frame of latency).
