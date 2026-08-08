#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;

public class VRC3CVRSittingFoldTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    const string SittingFoldTestFolder = "Assets/VRC3CVR_SittingFoldTest";
    const string SitEnterStateName = "SitDown";
    const string SitPoseStateName = "SitPose";
    const string BaseStateName = "AvatarGroundLocomotion";
    const string BaseSeatedStateName = "AvatarOwnSeat";

    GameObject originalAvatar;
    GameObject convertedAvatar;

    static IEnumerable<AnimatorState> AllStatesOf(AnimatorStateMachine machine) =>
        (IEnumerable<AnimatorState>)typeof(VRC3CVRCore).GetMethod("AllStatesOf", Flags).Invoke(null, new object[] { machine });

    [TearDown]
    public void TearDown()
    {
        if (originalAvatar != null) Object.DestroyImmediate(originalAvatar);
        if (convertedAvatar != null) Object.DestroyImmediate(convertedAvatar);
        originalAvatar = null;
        convertedAvatar = null;
        AssetDatabase.DeleteAsset(SittingFoldTestFolder);
    }

    static AnimationClip MakeClip(string name)
    {
        var clip = new AnimationClip { name = name };
        AssetDatabase.CreateAsset(clip, SittingFoldTestFolder + "/" + name + ".anim");
        return clip;
    }

    // The shape a custom Sitting layer has: VRChat holds the whole layer's weight at zero until the
    // player is seated, so nothing in it says how to leave.
    static AnimatorController MakeSittingController(bool authored)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(SittingFoldTestFolder + "/Sitting.controller");
        controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);

        var clip = MakeClip(authored ? "MyOwnSit" : "proxy_sit");
        var machine = controller.layers[0].stateMachine;

        var enter = machine.AddState(SitEnterStateName);
        enter.motion = clip;
        machine.defaultState = enter;

        var pose = machine.AddState(SitPoseStateName);
        pose.motion = clip;

        enter.AddTransition(pose).AddCondition(AnimatorConditionMode.If, 0f, "Seated");
        return controller;
    }

    static AnimatorController MakeBaseController(bool readsSeated)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(SittingFoldTestFolder + "/Base.controller");
        controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);

        var clip = MakeClip("AuthoredWalk");
        var machine = controller.layers[0].stateMachine;
        var ground = machine.AddState(BaseStateName);
        ground.motion = clip;
        machine.defaultState = ground;

        if (readsSeated)
        {
            var seat = machine.AddState(BaseSeatedStateName);
            seat.motion = clip;
            ground.AddTransition(seat).AddCondition(AnimatorConditionMode.If, 0f, "Seated");
            seat.AddTransition(ground).AddCondition(AnimatorConditionMode.IfNot, 0f, "Seated");
        }

        return controller;
    }

    AnimatorController Convert(bool convertSittingLayer, bool authoredSitting = true,
        bool baseReadsSeated = false, bool keepCckLocomotion = false)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(SittingFoldTestFolder);
        originalAvatar = descriptor.gameObject;

        var layers = descriptor.baseAnimationLayers;
        layers[(int)VRC3CVRCore.VRCBaseAnimatorID.BASE] = keepCckLocomotion
            ? new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Base,
                isDefault = true,
            }
            : new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Base,
                isDefault = false,
                animatorController = MakeBaseController(baseReadsSeated),
            };
        descriptor.baseAnimationLayers = layers;

        descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[]
        {
            new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Sitting,
                isDefault = false,
                animatorController = MakeSittingController(authoredSitting),
            },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.TPose, isDefault = true },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.IKPose, isDefault = true },
        };

        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
            convertLocomotionLayer = true,
            convertSittingLayer = convertSittingLayer,
        });
        core.Convert();
        convertedAvatar = core.chilloutAvatar;
        Assert.IsNotNull(convertedAvatar);

        var controller = convertedAvatar.GetComponent<CVRAvatar>().avatarSettings.baseController as AnimatorController;
        Assert.IsNotNull(controller);
        return controller;
    }

    static AnimatorStateMachine LocomotionMachineOf(AnimatorController controller) =>
        controller.layers.Single(layer => layer.name == "Locomotion/Emotes").stateMachine;

    static AnimatorStateMachine ChildMachineNamed(AnimatorStateMachine machine, string name) =>
        machine.stateMachines.Select(child => child.stateMachine).FirstOrDefault(child => child != null && child.name == name);

    static AnimatorState StateNamed(AnimatorStateMachine machine, string name) =>
        machine.states.Select(child => child.state).FirstOrDefault(state => state.name == name);

    static void AssertCondition(AnimatorTransitionBase transition, AnimatorConditionMode mode, string what)
    {
        Assert.AreEqual(1, transition.conditions.Length, what);
        Assert.AreEqual("Sitting", transition.conditions[0].parameter, what);
        Assert.AreEqual(mode, transition.conditions[0].mode, what);
    }

    [Test]
    public void Convert_WithAnAuthoredSittingAnimator_FoldsItIntoTheLocomotionHub()
    {
        var root = LocomotionMachineOf(Convert(convertSittingLayer: true));
        var hub = root.defaultState;
        Assert.AreEqual(BaseStateName, hub.name, "fixture: the avatar's own locomotion is not the hub");

        var sittingMachine = ChildMachineNamed(root, "Sitting");
        Assert.IsNotNull(sittingMachine, "the Sitting machine was not folded into the locomotion layer");
        Assert.IsTrue(AllStatesOf(sittingMachine).Any(state => state.name == SitPoseStateName));
        Assert.IsNull(StateNamed(root, "Sitting"),
            "ChilloutVR's own seat was salvaged alongside the folded machine");

        var enter = hub.transitions[0];
        Assert.AreEqual(sittingMachine, enter.destinationStateMachine, "the hub does not lead into the folded machine");
        Assert.IsFalse(enter.hasExitTime, "the sitting entry waits for an exit time");
        Assert.AreEqual(0.25f, enter.duration, 1e-4f, "the sitting entry does not blend");
        AssertCondition(enter, AnimatorConditionMode.If, "sitting entry");

        foreach (var state in AllStatesOf(sittingMachine))
        {
            var escape = state.transitions.SingleOrDefault(transition => transition.isExit);
            Assert.IsNotNull(escape, state.name + " has no way out of the folded machine");
            Assert.IsFalse(escape.hasExitTime, state.name + " exit waits for an exit time");
            Assert.AreEqual(0.25f, escape.duration, 1e-4f, state.name + " exit does not blend");
            AssertCondition(escape, AnimatorConditionMode.IfNot, state.name + " exit");
        }

        var leave = root.GetStateMachineTransitions(sittingMachine).Single();
        Assert.AreEqual(hub, leave.destinationState, "the folded machine has no way back to the hub");
        Assert.AreEqual(0, leave.conditions.Length, "the return to the hub is conditional");
    }

    [Test]
    public void Convert_WithSittingConversionOff_SalvagesChilloutVRsOwnSeat()
    {
        var root = LocomotionMachineOf(Convert(convertSittingLayer: false));
        var hub = root.defaultState;

        Assert.IsNull(ChildMachineNamed(root, "Sitting"));
        var seat = StateNamed(root, "Sitting");
        Assert.IsNotNull(seat, "ChilloutVR's own seat was dropped with the rest of its locomotion layer");

        // ChilloutVR's own wiring, which the replaced layer has to reproduce
        var enter = hub.transitions.Single(transition => transition.destinationState == seat);
        Assert.IsFalse(enter.hasExitTime, "the seat entry waits for an exit time");
        Assert.AreEqual(0f, enter.duration, 1e-4f, "the seat entry does not enter on ChilloutVR's own timing");
        AssertCondition(enter, AnimatorConditionMode.If, "seat entry");

        var leave = seat.transitions.Single();
        Assert.AreEqual(hub, leave.destinationState, "the salvaged seat does not lead back to the hub");
        Assert.IsFalse(leave.hasExitTime, "the seat exit waits for an exit time");
        Assert.AreEqual(0f, leave.duration, 1e-4f, "the seat exit does not leave on ChilloutVR's own timing");
        AssertCondition(leave, AnimatorConditionMode.IfNot, "seat exit");
    }

    [Test]
    public void Convert_WithAProxyOnlySittingAnimator_ConvertsNoneOfIt()
    {
        var controller = Convert(convertSittingLayer: true, authoredSitting: false);
        var root = LocomotionMachineOf(controller);

        Assert.IsNull(ChildMachineNamed(root, "Sitting"));
        Assert.IsNotNull(StateNamed(root, "Sitting"), "ChilloutVR's own seat was dropped");
        Assert.IsFalse(
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Any(state => state.name == SitPoseStateName),
            "the Sitting animator was folded in even though it only holds VRChat's placeholders");
    }

    [Test]
    public void Convert_WithABaseLayerThatAnswersSeatedItself_KeepsChilloutVRsSeatOut()
    {
        var root = LocomotionMachineOf(Convert(convertSittingLayer: false, baseReadsSeated: true));

        Assert.IsNotNull(StateNamed(root, BaseSeatedStateName), "fixture: the Base layer lost its own seat");
        Assert.IsNull(StateNamed(root, "Sitting"),
            "ChilloutVR's seat was salvaged next to the Base layer's own, so both answer Sitting at once");
    }

    [Test]
    public void Convert_WithChilloutVRsOwnLocomotionKept_FoldsTheSittingMachineIntoItAndDropsItsOwnSeat()
    {
        var root = LocomotionMachineOf(Convert(convertSittingLayer: true, keepCckLocomotion: true));
        var hub = root.defaultState;

        var sittingMachine = ChildMachineNamed(root, "Sitting");
        Assert.IsNotNull(sittingMachine, "the Sitting machine was not folded into ChilloutVR's own locomotion layer");
        Assert.IsNull(StateNamed(root, "Sitting"),
            "ChilloutVR's own seat was kept alongside the folded machine");
        Assert.IsFalse(
            root.states.SelectMany(child => child.state.transitions)
                .Any(transition => !transition.isExit && transition.destinationState == null && transition.destinationStateMachine == null),
            "a transition to the removed seat was left dangling");

        Assert.AreEqual(sittingMachine, hub.transitions[0].destinationStateMachine);
        Assert.AreEqual(hub, root.GetStateMachineTransitions(sittingMachine).Single().destinationState);
    }

    // ---- driven: Animator.Update advances state, never pose, so state checks need no PlayableGraph ----

    const int DrivenFrameLimit = 240;

    static int FramesUntil(Animator animator, int layer, string stateName)
    {
        var frames = 0;
        while (frames < DrivenFrameLimit &&
               animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != Animator.StringToHash(stateName))
        {
            animator.Update(1f / 60f);
            frames++;
        }
        return frames;
    }

    [Test]
    public void Convert_WithSittingSet_EntersTheFoldedMachineAndReturnsToTheHubWhenItClears()
    {
        var hub = LocomotionMachineOf(Convert(convertSittingLayer: true)).defaultState;
        var animator = convertedAvatar.GetComponent<Animator>();
        // the controller was assigned to a component that already existed, and outside play mode
        // nothing rebinds it on its own -- until it does, the animator reports no layers at all
        animator.Rebind();
        var layer = Enumerable.Range(0, animator.layerCount)
            .Single(index => animator.GetLayerName(index) == "Locomotion/Emotes");

        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, SitPoseStateName),
            "the avatar sat down without Sitting ever being set");

        animator.SetBool("Sitting", true);
        Assert.Less(FramesUntil(animator, layer, SitPoseStateName), DrivenFrameLimit,
            "Sitting never carried the avatar out of the hub and into the folded machine");

        animator.SetBool("Sitting", false);
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "clearing Sitting never carried the avatar back to the hub");
    }
}
#endif
