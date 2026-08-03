# Unreleased

- **BREAKING**: CCK4 is now required. CCK3 is no longer supported
- **BREAKING**: the NDMF plugin path is gone. `Tools -> Modular Avatar -> Manual bake avatar` no longer converts. The `VRC3CVRNDMF` component becomes `VRC3CVR Avatar` and its settings are preserved
- If you update over an older version an `Assets/PeanutTools/VRC3CVR/NDMF/` folder may be left behind. It is safe to delete
- feat: uploading from the CCK Control Panel now converts the avatar automatically, the same way Modular Avatar and VRCFury run during a VRChat upload. Add a `VRC3CVR Avatar` component and upload — no separate conversion step
- feat: non-destructive tools (VRCFury, Modular Avatar, Avatar Optimizer, ...) are now baked automatically before the conversion (`Auto bake`, on by default)
- feat: the avatar can now be converted from the `VRC3CVR Avatar` component inspector. The `Tools -> VRC3CVR` window and the inspector edit the same settings
- feat: `VRC3CVR Avatar` now requires a `CVRAvatar` component, which is what makes the avatar show up in the CCK Control Panel. The `CVRAvatar` and `CVRAssetInfo` an upload needs are now attached for you when you select the avatar, instead of having to add them by hand
- feat: only one `VRC3CVR Avatar` can be added per object
- feat: uploading an avatar that has no `VRC3CVR Avatar` component now fails with an error instead of publishing it unconverted as the VRChat avatar it still is
- feat: VRC Constraints are converted to Unity Constraints. Prefabulous is no longer needed for this
- feat: `GestureLeftWeight` / `GestureRightWeight` are converted, in either of two modes: rewritten into their consumers (default, no latency) or fed from a generated weight parameter (covers every usage, one frame of latency)
- feat: `Greater` / `Less` gesture conditions are converted instead of being silently dropped. VRChat and ChilloutVR number gestures differently, so each comparison expands into one transition per matching gesture
- feat: `VelocityMagnitude` is now fed, recomputed per client from `VelocityX/Y/Z` so it costs no sync bits, along with `MuteSelf`, `VRMode` and `Upright`
- feat: VRC state machine behaviours are removed after conversion. They would otherwise ship as missing scripts in the uploaded controller, since the VRC SDK assemblies do not exist in the ChilloutVR client
- fix: transition conditions now match the type the parameter actually has. An avatar that declares a built-in bool such as `IsLocal` as a Float — which a blend tree forces it to do — produced `uses parameter '...' which is not compatible with condition type` and that layer stopped working
- fix: the transitions leaving a sub-state-machine are converted too. They are stored on the parent state machine, so they were missed
- fix: the first layer of a merged VRC animator is no longer disabled. Unity forces layer 0 to run at weight 1 regardless of its serialized weight, so that weight is now baked in before merging moves the layer out of first place
- fix: the built-in avatar masks are loaded from the directory's real casing. On a case-sensitive filesystem every mask loaded as null and the layers ran unmasked over the whole humanoid rig, without anything being logged
- fix: an avatar with no expression parameters converts instead of throwing partway through and leaving a half-built avatar behind
- fix: combining avatar masks no longer drops restrictions on specific non-humanoid transforms. Props and bones excluded from a layer would animate again after conversion
- fix: negative toggle values no longer disappear from int dropdowns
- fix: int dropdown options that live directly in the root expression menu no longer lose the first character of their name
- fix: converting an Int parameter used only as a puppet sub-parameter no longer throws `InvalidOperationException` and aborts the conversion. The animator parameter is still converted; only the CVR menu entry is omitted, with a warning
- fix: a puppet's "changing" parameter that is also used by a toggle at value 1 no longer throws `ArgumentException` and aborts the conversion
- fix: a duplicate `CVRAssetInfo` no longer risks the upload picking the one without your content id
- fix: a conversion that fails no longer renames or retags the avatar it was working on
- fix: removed the missing toe bone error. It was presented as something to fix before uploading, but ChilloutVR does not require toe bones and the upload was never blocked

# 3.0.0-rc.1

- fix: null checks / minor bugs

# 3.0.0-rc.0

- feat: docs for CCK4
- fix: docs for VRCConstraints conversion (Prefabulous)
- feat: adjust to CCK4 default Auto-Generated Avatar Pointers
- fix: The Contact conversion settings in the VRC3CVR component were not taken into account

# 3.0.0-beta.13

- fix: LessThen -> LessThan, to match CCK_4.0.0-Preview.25 and later

# 3.0.0-beta.12

- fix: docs

# 3.0.0-beta.11

- fix: Fixed an issue where DynamicBone stopped working when Contacts were present on objects under a DynamicBone hierarchy.
- feat: Contact enabled animations are now correctly converted to GameObject active state.

# 3.0.0-beta.10

- Fixed an issue where per-path Collision Tag conversion settings did not function correctly in the component.
- Ignore invalid range conversions in VRC Avatar Parameter Driver.
- Prevent errors when Viseme BlendShapes are missing.
- Prevent the process from getting stuck in the converting state.
- Refactoring.

# 3.0.0-beta.9

- fix: Fixed an issue where only one error was shown when both toe bones were missing.
- fix: Fixed missing error checks for viseme and blink blendshapes.
- fix: Fixed an issue where conversion could be triggered again while already in progress.
- fix: Code cleanup.

# 3.0.0-beta.8

- feat: Convert VRC Head Chop (only when the Scale is 0 or 1).
- feat: Convert VRC Spatial Audio Source (experimental: there is no guarantee that gain or distance values are converted correctly).

# 3.0.0-beta.7

- fix: Probably fixed an issue where conversion failed when the VRC Avatar Descriptor collider settings contained internally invalid data.

# 3.0.0-beta.6

- feat: Converted Contacts now work remotely

# 3.0.0-beta.5

- fix: conversion of state params

# 3.0.0-beta.4

- fix: no VRCEmote => Emote (revert)

# 3.0.0-beta.3

- feat: Improved parameter compatibility
  - The following parameters can now be replaced with their CVR equivalents:
    - VRCEmote => Emote
    - Viseme => VisemeIdx
    - Voice => VisemeLoudness
    - Seated => Sitting
    - InStation => Sitting
    - IsOnFriendsList => IsFriend
  - The default values for the following parameters have been set to 1:
    - ScaleFactor
    - ScaleFactorInverse
    - EyeHeightAsPercent

# 3.0.0-beta.2

- feat: CCK_4.0.0_Preview.19 compatible!

# 3.0.0-beta.1

- fix: PB converter URL
- chore(breaking): NDMF>=1.8

# 3.0.0-beta.0

- feat: NDMF Plugin
- feat: Tag conversion now takes the parent’s components into account.
- feat: Tag conversion settings by path
- feat: UI improvement

# 2.2.0

- feat: Action Menu mod "impulse" annotations
- feat: preserve parameter sync state
- fix: do not add extra parameters to menu
- fix: BlendTree / AnimationClip copying

# 2.2.0-beta.2

- fix: humanoid animation conversion

# 2.2.0-beta.1

- feat: Action Menu mod "impulse" annotations
- feat: preserve parameter sync state
- fix: do not add extra parameters to menu
- fix: BlendTree / AnimationClip copying

# 2.1.0

- feat: Action Menu mod "hidden" annotations

# 2.0.0

- **adjust to CCK 3.15.x!**
- feat: adjust to vrc menu order
- feat: improved Menu name detection
- feat: hierarchical menu name
- feat: VRCParameterDriver conversion
- feat: VRCAnimatorLocomotionControl conversion
- feat: VRCAnimatorTrackingControl conversion (partial: except eyes, fingers, mouth)
- feat: VRC Contacts conversion
  - new: VRC3CVRCollisionTagConvertion component (Attach to the same object as VRCContacts)
- feat: Grounded param = true by default (convenient for preview)
- feat: make some methods and fields public for automation
- ui: moved menu "PeanutTools/VRC3CVR" to general "Tools/VRC3CVR"
- ui: GUI rework
- ui: ja-JP localization
- fix: Animator Controller generation to be able to use with Modular Avatar
- fix: animator's "name"

# 2.0.0-rc.17

- feat: dropdown menu name
- feat: hierarchical menu name

# 2.0.0-rc.16

- feat: contact anim remap fpr position/rotation/radius/height

# 2.0.0-rc.15

- fix: convert contacts' localOnly

# 2.0.0-rc.14

- feat: convert contacts' localOnly

# 2.0.0-rc.13

- feat: VRC Contacts conversion
  - new: VRC3CVRCollisionTagConvertion component (Attach to the same object as VRCContacts)
- fix: Default values for animator-only parameters were being cleared to zero.

# 2.0.0-rc.12

- feat: Grounded param = true by default (convenient for preview)

# 2.0.0-rc.11

- feat: make some methods and fields public for automation

# 2.0.0-rc.10

- fix: convert error with some avatars

# 2.0.0-rc.9

- feat: VRCParameterDriver conversion
- feat: VRCAnimatorTrackingControl conversion (partial: except eyes, fingers, mouth)
- feat: VRCAnimatorLocomotionControl conversion

# 2.0.0-rc.8

- fix: Fixed problem with transitions between state machines not being copied (This is a problem for complex animators)

# 2.0.0-rc.7

- feat: adjust to vrc menu order

# 2.0.0-rc.6

- Bool/Float Menu name detection
- GUI rework
- ja-JP localization

# 2.0.0-rc.5

- fix save

# 2.0.0-rc.4

- fix save state machine

# 2.0.0-rc.3

- Fix release

# 2.0.0-rc.2

- Fix animator's "name"

# 2.0.0-rc.1

- Fix Animator Controller generation to be able to use with MA
- moved menu "PeanutTools/VRC3CVR" to general "Tools/VRC3CVR"

# 2.0.0-rc.0

- adjust to CCK 3.13.4

# 1.2.6S

- Fix blend tree Y parameter naming

# 1.2.5S

- Rebase onto main branch
- Fix scaling of voice position
- Fix threshold generation between hand idle and fist
- Prevent null error with empty blend tree motions

# 1.2.4S

- Add toggles to choose all which of the five VRChat base animators to convert and ignore, along with explanations
- Voice position is now placed at the base of the head bone (if found) rather than the eye position
- Fix assignment of face mesh if the avatar is was placed in the root of scene

# 1.2.3S

- Fix bug with VRC3CVR_Ouput directory not being created

# 1.2.2S

- Improve support of animator masking on all animators

# 1.2.1S

- Hotfix to address error on avatars without a VRC ExpressionMenu

# 1.2.0S

- Match CVR restrictions on parameter names
- Make deletion of VRC components optional
- Fix weight of first layer of each animator
- Add empty masking to FX layers
- Scrape VRC menu for correct integer parameter names
- Add support for converting gesture animator with correct masking and proxy animations

# 1.1.1

- fix face mesh using the old mesh

# 1.1.0

- properly delete all VRC components
- fixed converting avatars without a skinned mesh renderer
- properly log warnings
- added clone toggle

# 1.0.3

- do not override parameter type to float

# 1.0.2

- fix crashes

# 1.0.1

- ignore no visemes detected

# 1.0.0

- renamed to "vrc3cvr" to match github repo
- updated with latest VRCSDK and CCK
- improved UI
- fixed null reference error ([issue 9](https://github.com/imagitama/vrc3cvr/issues/9))
- clones original avatar to preserve
- added message about converting PhysBones

# 0.0.12

- added extra logging for github issue #8

# 0.0.11

- changed time parameter and blend trees to use `GestureLeft`/`GestureRight` instead of `GestureLeftWeight`/`GestureRightWeight`
- fixed crash when no blink blendshapes

# 0.0.10

- output if the left or right toe bones are not set

# 0.0.9

- added checkbox to decide if to delete the `LeftHand` and `RightHand` layers provided by CVR

# 0.0.8

- show a toggle instead of a dropdown if only 1 dropdown item

# 0.0.7

- fixed resting gesture showing open-hand/surprised gesture

# 0.0.6

- fixed `NotEqual` int conditions not properly converting to floats

# 0.0.5

- do not render dropdown if no conditions use the int VRC param

# 0.0.4

- dropdowns for int VRC params

# 0.0.3

- use toggles (Game Object Toggles) for boolean params

# 0.0.2

- fix animator controller not working because of duplicate layer names
- changed back to sliders
- changed `NotEqual` condition to `LessThan` the float value

# 0.0.1

Initial release.
