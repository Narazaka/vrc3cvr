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
