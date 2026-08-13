**[日本語](CHANGELOG.ja.md)**

# 3.0.0-rc.3

- feat: an avatar's own locomotion replaces ChilloutVR's locomotion layer (`Convert Locomotion Animator`)
  - VRChat's `proxy_*` placeholder clips are swapped for ChilloutVR's own animations
  - ChilloutVR's flying, swimming and emote states are rewired onto the avatar's own state machine, so they keep working
  - the landing plays ChilloutVR's landing animation on its own timing (`Play the landing animation`, on by default). Without it the landing freezes to a single pose and the body dips sharply
  - Tracking Control in this layer is left unconverted (`Convert Tracking Control in the locomotion layer`, off by default). VRChat's landing states commonly carry one, and converting it makes full-body tracking jitter in ChilloutVR
  - a Base layer holding only placeholders, or whose first layer has no default state, is left to ChilloutVR's own locomotion with a warning
- feat: the Action layer is merged into the locomotion layer and played from ChilloutVR's own emote menu (`Convert Action Animator`)
  - this works whether or not the locomotion layer is replaced
  - an Action animator that reads `VRCEmote` gets a generated layer feeding it from ChilloutVR's own emote parameter, so the quick menu's emote and cancel buttons drive it
  - layers past the first are merged too, unless the layer holds its emotes back by a means the merge cannot handle: an avatar mask, a zero default weight, additive blending, a first state with no conditional transition out of it, a return to that state from AnyState, or sub-state-machines of its own. Such a layer is skipped with a warning naming the reason, since its emotes simply go missing
- feat: the Sitting layer is merged in the same way (`Convert Sitting Animator`)
  - only an avatar with a seated animation of its own is affected; a stock Sitting layer is left to ChilloutVR's own seated pose
  - layers past the first are left out
- feat: `TrackingType` is fed from the game
  - ChilloutVR only knows whether full body tracking is on, so only 3 (head and hands) and 6 (full body) are produced
  - hip-only and feet-only cannot be told apart from full body, and the generic value 1 has no equivalent
- feat: every playable layer is converted by default
  - each conversion judges for itself whether it can stand in for what ChilloutVR already does, and leaves ChilloutVR's own layer alone when it cannot
  - an avatar that already carries a `VRC3CVR Avatar` component keeps the settings it was saved with
- feat: the settings are sorted into sections that fold
  - every heading folds and starts folded, so what greets you is the list of what can be set rather than all of it at once, with the convert button in reach
  - the parameter settings share one heading, the VRC components another, and the ones kept only for the conversions that came before them sit under `Legacy` at the end
  - the step numbers are gone, and each setting is named after what it sits on rather than after the machinery behind it
- feat: the `VRC3CVR Avatar` component has an icon of its own, and does not wear it in the scene view where it would sit over the avatar
- feat: the locomotion option is no longer labelled `NOT RECOMMEND`, and the Additive option no longer warns about the bicycle pose
- fix: Additive layers are blended additively
  - the Additive playable is additive by platform rule rather than by anything in the controller, so its layers are usually authored on Override. They were carried over on Override and replaced the merged pose instead of adding to it
  - the first layer's avatar mask is no longer applied, since VRChat ignores it and the avatar was authored with it having no effect
- fix: `VelocityX` / `VelocityZ` are avatar-local
  - ChilloutVR hands the animator a world-space velocity, so a blend tree authored against VRChat's avatar-local one played the wrong motion depending on which way the avatar faced
  - converted layers that read them are pointed at a generated avatar-local pair, which costs no sync budget
- fix: int dropdown options line up with the values they write. ChilloutVR addresses dropdown options by their index in the list, so an avatar whose option values did not start at 0 wrote a different value than the option named
- fix: the hands stop gesturing during an emote
  - ChilloutVR mutes the hands by zeroing the weight of the layers named `LeftHand` and `RightHand`. A converted Gesture layer kept the name VRChat gave it, which is spelled differently (`Left Hand` in the stock controller), so nothing was muted and the fingers kept animating
  - the Gesture layer whose name matches once case and non-alphanumeric characters are ignored is renamed to ChilloutVR's spelling. A layer is left alone if it is not the only such match, if it does not run at full weight, or if a layer of that name is already there
- fix: an emote no longer leaves the avatar in the bicycle pose for a time before the next one starts (`Zero-Weight States`)
  - an emote layer parks the body in a state once the emote is over -- stock's own blend-out, and the cleanup an emote tool waits a whole clip out in -- and VRChat showed nothing of either, since the Action playable's weight was already down by then. The merged layer plays one state at a time and has no weight left to hide them with, so they played for real, and a cleanup clip that animates nothing plays Unity's own humanoid default
  - these states are now left as soon as they are reached, which keeps whatever they carry firing. Choose `Change nothing` to keep VRChat's own timing instead, at the price of seeing what it was spent on. Leaving at once means the next emote follows on sooner than it does in VRChat, and the two cannot both be had in one layer
  - whatever the weight had left to fade over is handed to the way out, which is the same crossfade by another name. Stock asks for half a second of it and leaves a fifth of the way through its own clip, so the return to locomotion after an emote now blends over the rest rather than cutting
  - a layer whose weight control does not bound the raised span clearly enough to say which states were unseen is left alone
- fix: a parameter with no name is dropped instead of carried over. VRChat ignores such a parameter, so a tool that generates one by accident leaves no trace and the avatar uploads normally, but ChilloutVR cannot load that avatar and leaves you in its own default avatar instead

# 3.0.0-rc.2

- **BREAKING**: the NDMF plugin path is gone. `Tools -> Modular Avatar -> Manual bake avatar` no longer converts. The `VRC3CVRNDMF` component becomes `VRC3CVR Avatar` and its settings are preserved
- If you update over an older version an `Assets/PeanutTools/VRC3CVR/NDMF/` folder may be left behind. It is safe to delete
- feat: the CCK Control Panel now converts the avatar for you as it uploads. Add a `VRC3CVR Avatar` component — it brings the `CVRAvatar` and `CVRAssetInfo` the CCK needs with it — and press upload. There is no separate conversion step any more, the same way Modular Avatar and VRCFury run during a VRChat upload
- feat: non-destructive tools (VRCFury, Modular Avatar, Avatar Optimizer, ...) are baked before the conversion, so you no longer have to bake them yourself first (`Auto bake`, on by default)
- An avatar produced by a **manual** conversion with `Auto bake` references generated assets that live in a temporary folder and are destroyed by the next build. Upload that result rather than keeping it. Uploading from the CCK Control Panel is not affected
- feat: the avatar can also be converted from the `VRC3CVR Avatar` inspector. The `Tools -> VRC3CVR` window and the inspector edit the same settings, so an avatar never has two sets of them, and the window can save its settings onto the avatar as a `VRC3CVR Avatar` component
- feat: VRC Constraints are converted to Unity Constraints (`Convert VRC Constraints`, on by default). Prefabulous is no longer needed for constraint conversion. Animation clips that drive constraint properties are rebound to the Unity equivalents, and a constraint with a Target Transform moves to that transform's GameObject. VRC-only features (`FreezeToWorld`, `SolveInLocalSpace`) have no equivalent and are dropped with a warning
- feat: `VelocityMagnitude` is supplied. ChilloutVR has no equivalent, so each client recomputes it from the `VelocityX/Y/Z` the client already feeds, which costs no sync budget
- feat: `MuteSelf`, `VRMode` and `Upright` are fed from the game (`Feed MuteSelf / VRMode / Upright`, on by default). They are declared as synced parameters, so an avatar that uses them spends sync budget on them
- feat: how `GestureLeftWeight` / `GestureRightWeight` are converted can now be chosen (`GestureLeftWeight/GestureRightWeight conversion`). Rewriting them onto `GestureLeft` / `GestureRight` stays the default and now also reproduces VRChat's "fixed 1 outside Fist" rule in weight conditions and weight-driven 1D blend trees. The new mode keeps the weight parameters and feeds them from `GestureLeft` instead, which covers motion time states and 2D blend trees as well, at the cost of one frame of latency
- feat: `Greater` / `Less` conditions on `GestureLeft` / `GestureRight` are converted instead of being dropped. VRChat and ChilloutVR number gestures differently, so each comparison expands into one transition per matching gesture
- fix: transition conditions now match the type the parameter actually has. An avatar that declares a built-in bool such as `IsLocal` as a Float — which a blend tree forces it to do — produced `uses parameter '...' which is not compatible with condition type` and that layer stopped working. A condition with no equivalent for the real type is dropped with a warning, since keeping it would stop the whole layer
- fix: a blend tree nested inside another blend tree is converted too when it is driven by `GestureLeftWeight` / `GestureRightWeight`. Only the top-level tree on a state was rewritten, so a nested one stayed on a parameter ChilloutVR never drives and always played its lowest-threshold child
- fix: the transitions leaving a sub-state-machine are converted too. They are stored on the parent state machine, so they were missed
- fix: the first layer of a merged VRC animator is no longer disabled. Unity forces layer 0 to run at weight 1 regardless of its serialized weight, so that weight is now baked in before merging moves the layer out of first place
- fix: an avatar with no expression parameters converts instead of throwing partway through and leaving a half-built avatar behind
- fix: combining avatar masks no longer drops restrictions on specific non-humanoid transforms. Props and bones excluded from a layer would animate again after conversion
- fix: negative toggle values no longer disappear from int dropdowns
- fix: int dropdown options no longer lose the first character of their name when they share no common parent submenu
- fix: converting an Int parameter used only as a puppet sub-parameter no longer throws `InvalidOperationException` and aborts the conversion. The animator parameter is still converted; only the CVR menu entry is omitted, with a warning
- fix: a puppet's "changing" parameter that is also used by a toggle at value 1 no longer throws `ArgumentException` and aborts the conversion
- fix: VRC state machine behaviours are removed after conversion, so the converted controller no longer carries missing script references
- fix: the built-in avatar masks are loaded from the directory's real casing. On a case-sensitive filesystem they all loaded as null and every layer ran unmasked over the whole humanoid rig, with nothing logged
- fix: the missing toe bone error is gone. It told you to fix something before uploading, but ChilloutVR does not require toe bones

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
