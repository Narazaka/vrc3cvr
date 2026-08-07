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

public class VRC3CVRActionFoldTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    const string ActionFoldTestFolder = "Assets/VRC3CVR_ActionFoldTest";
    const string EmoteStateName = "StandWave";
    const string SecondLayerStateName = "ActionSecondLayerState";

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
        AssetDatabase.DeleteAsset(ActionFoldTestFolder);
    }

    // The shape stock Action has: a default state that waits, a Prepare that raises the playable
    // weight, the emote itself, and a BlendOut that drops the weight and leaves through Exit.
    static AnimatorController MakeActionController(bool authored)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ActionFoldTestFolder + "/Action.controller");
        controller.AddParameter("Emote", AnimatorControllerParameterType.Int);
        controller.AddParameter("AFK", AnimatorControllerParameterType.Bool);

        var clip = new AnimationClip { name = authored ? "MyOwnWave" : "proxy_stand_wave" };
        AssetDatabase.CreateAsset(clip, ActionFoldTestFolder + "/" + clip.name + ".anim");

        var machine = controller.layers[0].stateMachine;
        var wait = machine.AddState("WaitForActionOrAFK");
        machine.defaultState = wait;

        var prepare = machine.AddState("Prepare");
        var raiseWeight = prepare.AddStateMachineBehaviour<VRCPlayableLayerControl>();
        raiseWeight.goalWeight = 1f;
        raiseWeight.blendDuration = 0.25f;

        var emote = machine.AddState(EmoteStateName);
        emote.motion = clip;

        var blendOut = machine.AddState("BlendOut");
        blendOut.AddStateMachineBehaviour<VRCPlayableLayerControl>().goalWeight = 0f;

        wait.AddTransition(prepare).AddCondition(AnimatorConditionMode.Greater, 0f, "Emote");
        prepare.AddTransition(emote).AddCondition(AnimatorConditionMode.Equals, 2f, "Emote");
        emote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, 2f, "Emote");
        blendOut.AddExitTransition().hasExitTime = true;

        controller.AddLayer("ActionSecondLayer");
        controller.layers[1].stateMachine.AddState(SecondLayerStateName).motion = clip;

        return controller;
    }

    AnimatorController Convert(bool convertActionLayer, bool authoredAction = true, bool authoredBase = true)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(ActionFoldTestFolder);
        originalAvatar = descriptor.gameObject;

        var layers = descriptor.baseAnimationLayers;
        if (!authoredBase)
        {
            layers[(int)VRC3CVRCore.VRCBaseAnimatorID.BASE] = new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.Base,
                isDefault = true,
            };
        }
        layers[(int)VRC3CVRCore.VRCBaseAnimatorID.ACTION] = new VRCAvatarDescriptor.CustomAnimLayer
        {
            type = VRCAvatarDescriptor.AnimLayerType.Action,
            isDefault = false,
            animatorController = MakeActionController(authoredAction),
        };
        descriptor.baseAnimationLayers = layers;

        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
            convertLocomotionLayer = true,
            convertActionLayer = convertActionLayer,
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

    static void AssertEntry(
        AnimatorStateTransition transition, AnimatorStateMachine destination, string parameter, AnimatorConditionMode mode)
    {
        Assert.AreEqual(destination, transition.destinationStateMachine, parameter + " entry leads elsewhere");
        Assert.IsFalse(transition.hasExitTime, parameter + " entry waits for an exit time");
        Assert.AreEqual(0.25f, transition.duration, 1e-4f, parameter + " entry does not blend for the Action fade-in");
        Assert.AreEqual(1, transition.conditions.Length, parameter + " entry");
        Assert.AreEqual(parameter, transition.conditions[0].parameter, parameter + " entry");
        Assert.AreEqual(mode, transition.conditions[0].mode, parameter + " entry");
    }

    [Test]
    public void Convert_WithAnAuthoredActionAnimator_FoldsItIntoTheLocomotionHub()
    {
        var controller = Convert(convertActionLayer: true);
        var root = LocomotionMachineOf(controller);
        var hub = root.defaultState;

        var actionMachine = ChildMachineNamed(root, "Action");
        Assert.IsNotNull(actionMachine, "the Action machine was not folded into the locomotion layer");
        Assert.IsTrue(AllStatesOf(actionMachine).Any(state => state.name == EmoteStateName));
        Assert.IsNull(ChildMachineNamed(root, "Emotes"),
            "ChilloutVR's own emote machine was kept alongside the folded one");

        var hubTransitions = hub.transitions;
        AssertEntry(hubTransitions[0], actionMachine, "Emote", AnimatorConditionMode.Greater);
        AssertEntry(hubTransitions[1], actionMachine, "AFK", AnimatorConditionMode.If);

        var leave = root.GetStateMachineTransitions(actionMachine).Single();
        Assert.AreEqual(hub, leave.destinationState, "the folded machine has no way back to the hub");
        Assert.AreEqual(0, leave.conditions.Length, "the return to the hub is conditional");

        Assert.IsFalse(AllStatesOf(actionMachine).Any(state => state.behaviours.Any(b => b is VRCPlayableLayerControl)),
            "a playable weight control survived the fold");
        Assert.IsFalse(
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Any(state => state.name == SecondLayerStateName),
            "a layer the Action playable's weight used to gate was merged at full weight");
    }

    [Test]
    public void Convert_WithActionConversionOff_KeepsChilloutVRsOwnEmotes()
    {
        var root = LocomotionMachineOf(Convert(convertActionLayer: false));

        Assert.IsNull(ChildMachineNamed(root, "Action"));
        Assert.IsNotNull(ChildMachineNamed(root, "Emotes"), "ChilloutVR's own emote machine was dropped");
    }

    [Test]
    public void Convert_WithAProxyOnlyActionAnimator_ConvertsNoneOfIt()
    {
        var controller = Convert(convertActionLayer: true, authoredAction: false);
        var root = LocomotionMachineOf(controller);

        Assert.IsNull(ChildMachineNamed(root, "Action"));
        Assert.IsNotNull(ChildMachineNamed(root, "Emotes"), "ChilloutVR's own emote machine was dropped");
        Assert.IsFalse(
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Any(state => state.name == EmoteStateName),
            "the Action animator was merged as plain layers, losing the weight that gated it");
    }

    [Test]
    public void Convert_WithChilloutVRsOwnLocomotionKept_FoldsTheActionMachineIntoItAsWell()
    {
        var root = LocomotionMachineOf(Convert(convertActionLayer: true, authoredBase: false));
        var hub = root.defaultState;

        var actionMachine = ChildMachineNamed(root, "Action");
        Assert.IsNotNull(actionMachine, "the Action machine was not folded into ChilloutVR's own locomotion layer");
        Assert.IsNull(ChildMachineNamed(root, "Emotes"),
            "ChilloutVR's own emote machine was kept alongside the folded one");
        Assert.IsFalse(
            root.states.SelectMany(child => child.state.transitions)
                .Any(t => !t.isExit && t.destinationStateMachine == null && t.destinationState == null),
            "a transition to the removed emote machine was left dangling");

        // ChilloutVR emotes out of each stance it can emote from, not out of the hub alone
        foreach (var stance in new[] { "Standard Locomotion", "Crouching Locomotion", "Prone Locomotion" })
        {
            var state = root.states.Single(child => child.state.name == stance).state;
            AssertEntry(state.transitions[0], actionMachine, "Emote", AnimatorConditionMode.Greater);
            AssertEntry(state.transitions[1], actionMachine, "AFK", AnimatorConditionMode.If);
        }
        Assert.AreEqual(hub, root.states.Single(child => child.state.name == "Standard Locomotion").state,
            "fixture: ChilloutVR's hub is no longer Standard Locomotion");

        Assert.AreEqual(hub, root.GetStateMachineTransitions(actionMachine).Single().destinationState);
    }
}
#endif
