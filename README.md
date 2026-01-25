# VRC3CVR

- **[日本語](README.ja.md)**

Convert a VRChat SDK3 avatar to ChilloutVR with this Unity script.

- Keep a BACKUP of your original project.
- This is an fork of the forked project (https://github.com/SaracenOne/vrc3cvr) that is fork of the original project (https://github.com/imagitama/vrc3cvr).
- It is not recommend you convert avatars with custom locomotion controllers, only FX and Gesture controllers.

Tested with:

- VRChat Avatar SDK3 3.7.x, 3.10.1
- ChilloutVR CCK 3.13.4-3.15.x, CCK_4.0.0_Preview.19
- Unity 2022.3.22f1 (VRChat compatible)

## Usage

### 1. Convert

Go to [Releases](https://github.com/Narazaka/vrc3cvr/releases/latest) and expand "Assets" and download the `.unitypackage`.

1. Setup your VRChat avatars with Unity 2022.3.22f1/VRChat SDK 3.x (use VCC)
2. (optional) convert your PhysBones to DynamicBones by [PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone) etc.
3. Import the [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/) to that VRChat avatar project. (Don't worry about unity version mismatch)
4. Import the vrc3cvr `.unitypackage`
5. Click **Tools** -> VRC3CVR
6. Select the VRC avatar you want to convert
   - If you are using Modular Avatar or something non-destractive avatar build tool, try to "bake" avatar first (e.g. Tools -> Modular Avatar -> Manual bake avatar)
7. Click Convert

Want to convert your PhysBones to DynamicBones? Use these tools:

- https://github.com/FACS01-01/PhysBone-to-DynamicBone

You don't need to buy DynamicBones! Use this instead: https://github.com/Markcreator/VRChat-Tools or https://github.com/VRLabs/Dynamic-Bones-Stub

### 2. Export

**With CCK4 Preview, you can complete uploads in Unity 2022. The following steps are no longer necessary. (As of 2026-01-25, CCK4 Preview is available via the `#-nightly-news` channel on [Chillout VR Discord](https://discord.com/invite/ChilloutVR).)**

VRChat requires 2022.3.22f1 and CCK requires 2021.3.45f1.

So you have to bring the converted avatars from 2022 to 2021.

1. Drag and Drop the converted avatar to Project tab to create a prefab.
2. Right click the prefab and click "Export Package..."
3. Export unitypackage to where you want.

But...

"Export Package.." often have the problem of including script assets that should not be exported.
So I created an extension that allows exporting without script assets, etc.
Install [Export Package (Advanced)](https://github.com/Narazaka/ExportPackageAdvanced) and simply replace "Export Package..." to "Export Package (Advanced)..."

### 3. Upload

1. Setup the Unity 2021.3.45f1 project (CCK compatible)
2. Import the [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/) to that VRChat avatar project.
3. Add this project to VCC (by "Add Existing Project" button). Now you can install them by VCC. (Don't worry about unity version mismatch)
4. Install [under-2022-constraint-activator](https://openupm.com/packages/net.narazaka.unity.under-2022-constraint-activator/) for fix Constraint IsActive problem.
5. Import assets that the avatar depends (shaders etc.)
6. Import the exported avatar unitypackage
7. Upload it normally.

## What does it do?

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
  - references to `GestureLeftWeight`/`GestureRightWeight` are converted to `GestureLeft`/`GestureRight` (check your Fist animation!)
  - converts VRCParameterDriver etc.
- Convert VRC Contact Senders and Receivers to CVR Pointer and CVR Advanced Avatar Trigger
  - Unlike VRC Contact, CVR Pointer and Trigger only change values when the contact collides. This difference may cause compatibility issues.
  - Changing Shape Type in game is not supported.

## Mapping gestures

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

### Trigger weight

VRC has two parameters `GestureLeftWeight` and `GestureRightWeight`. They do not exist in CVR and instead check `GestureLeft` amount where 0.5 is 50% of the trigger for the fist animation.

## Ideas for future

- support jaw flap blendshape
- automatically detect jaw/mouth and move voice position
- GestureLeftWeight/GestureRightWeight

## Troubleshooting

### "VRCExpressionParameters.Parameter does not contain a definition for defaultValue" or another VRChat error

Update to a more recent version. Tested with at least VRChat Avatar SDK3.

### When performing a gesture my hands do not animate

Uncheck "My avatar has custom hand animations" and convert.

### "The type or namespace 'VRC' could not be found"

You need the VRC SDK in your project.
