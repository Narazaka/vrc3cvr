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
    static AnimatorController MakeActionController(bool authored, bool declareVrcEmote = false)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ActionFoldTestFolder + "/Action.controller");
        var emoteParameter = declareVrcEmote ? "VRCEmote" : "Emote";
        controller.AddParameter(emoteParameter, AnimatorControllerParameterType.Int);
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

        wait.AddTransition(prepare).AddCondition(AnimatorConditionMode.Greater, 0f, emoteParameter);
        prepare.AddTransition(emote).AddCondition(AnimatorConditionMode.Equals, 2f, emoteParameter);
        emote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, 2f, emoteParameter);
        blendOut.AddExitTransition().hasExitTime = true;

        controller.AddLayer("ActionSecondLayer");
        controller.layers[1].stateMachine.AddState(SecondLayerStateName).motion = clip;

        return controller;
    }

    AnimatorController Convert(bool convertActionLayer, bool authoredAction = true, bool authoredBase = true, bool declareVrcEmote = false)
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
            animatorController = MakeActionController(authoredAction, declareVrcEmote),
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

    [Test]
    public void Convert_WithAnAuthoredActionAnimator_OnlyDuplicatesTheNotEqualExitAgainstCancelEmote()
    {
        var actionMachine = ChildMachineNamed(LocomotionMachineOf(Convert(convertActionLayer: true)), "Action");

        bool HasCancelEmoteEscape(string stateName) =>
            AllStatesOf(actionMachine).Single(state => state.name == stateName).transitions
                .Any(transition => transition.conditions.Any(condition => condition.parameter == "CancelEmote"));

        Assert.IsTrue(HasCancelEmoteEscape(EmoteStateName), "the NotEqual exit was not duplicated against CancelEmote");
        Assert.IsFalse(HasCancelEmoteEscape("Prepare"), "an entry-only (Equals) transition gained a CancelEmote escape");
        Assert.IsFalse(HasCancelEmoteEscape("WaitForActionOrAFK"), "an entry-only (Greater) transition gained a CancelEmote escape");
    }

    [Test]
    public void Convert_WithVRCEmoteDeclared_WiresTheHubEntryAndCancelEmoteEscapeToVRCEmote()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var root = LocomotionMachineOf(controller);
        var hub = root.defaultState;
        var actionMachine = ChildMachineNamed(root, "Action");

        AssertEntry(hub.transitions[0], actionMachine, "VRCEmote", AnimatorConditionMode.Greater);

        var standWave = AllStatesOf(actionMachine).Single(state => state.name == EmoteStateName);
        Assert.IsTrue(
            standWave.transitions.Any(transition => transition.conditions.Any(condition => condition.parameter == "CancelEmote")),
            "the VRCEmote-declaring machine's NotEqual exit was not duplicated against CancelEmote");
    }

    const string VrcEmoteCompatLayerPrefix = "VRC3CVR_VRCEmoteCompat";

    [Test]
    public void Convert_WithoutVRCEmoteDeclared_DoesNotInjectTheCompatFeedLayer()
    {
        var controller = Convert(convertActionLayer: true);
        Assert.IsFalse(controller.layers.Any(layer => layer.name.StartsWith(VrcEmoteCompatLayerPrefix)),
            "the compat feed layer was injected even though the fold reads Emote directly");
    }

    [Test]
    public void Convert_WithVRCEmoteDeclared_InjectsACompatFeedLayerThatMirrorsCcksEmoteBands()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var layer = controller.layers.Single(l => l.name.StartsWith(VrcEmoteCompatLayerPrefix));
        Assert.AreEqual(1f, layer.defaultWeight, "the compat feed layer does not run at full weight");

        float EnterValue(AnimatorState state) => ((AnimatorDriver)state.behaviours.Single()).EnterTasks.Single().aValue;
        float ExitValue(AnimatorState state) => ((AnimatorDriver)state.behaviours.Single()).ExitTasks.Single().aValue;

        var idle = layer.stateMachine.defaultState;
        Assert.AreEqual("Idle", idle.name);
        Assert.IsEmpty(idle.behaviours, "Idle writes VRCEmote on its own enter, clobbering a custom menu that drives it directly");

        // highest band first, mirroring CCK's own ordered Greater cascade
        CollectionAssert.AreEqual(
            new[] { "Emote8", "Emote7", "Emote6", "Emote5", "Emote4", "Emote3", "Emote2", "Emote1" },
            idle.transitions.Select(t => t.destinationState.name).ToArray(),
            "the entry bands are not ordered highest-first");
        CollectionAssert.AreEqual(
            new[] { 7f, 6f, 5f, 4f, 3f, 2f, 1f, 0f },
            idle.transitions.Select(t => t.conditions.Single().threshold).ToArray());
        Assert.IsTrue(idle.transitions.All(t =>
            t.conditions.Single().parameter == "Emote" && t.conditions.Single().mode == AnimatorConditionMode.Greater));

        for (var n = 1; n <= 8; n++)
        {
            var state = layer.stateMachine.states.Single(child => child.state.name == "Emote" + n).state;
            Assert.AreEqual((float)n, EnterValue(state), "Emote" + n + " enter");
            Assert.AreEqual(0f, ExitValue(state), "Emote" + n + " exit");
            Assert.IsTrue(state.transitions.All(t => t.destinationState == idle), "Emote" + n + " leaves anywhere but idle");

            var less = state.transitions.Single(t => t.conditions.Single().mode == AnimatorConditionMode.Less).conditions.Single();
            Assert.AreEqual("Emote", less.parameter);
            Assert.AreEqual((float)n, less.threshold);

            var greater = state.transitions.Single(t => t.conditions.Single().mode == AnimatorConditionMode.Greater).conditions.Single();
            Assert.AreEqual("Emote", greater.parameter);
            Assert.AreEqual((float)n, greater.threshold);

            Assert.IsTrue(state.transitions.Any(t => t.conditions.Single().parameter == "CancelEmote" && t.conditions.Single().mode == AnimatorConditionMode.If),
                "Emote" + n + " has no CancelEmote escape");
        }
    }

    // ---- driven: Animator.Update advances state, never pose, so state checks need no PlayableGraph ----

    static Animator DriveAnimator(GameObject avatar)
    {
        var animator = avatar.GetComponent<Animator>();
        // the controller was assigned to a component that already existed, and outside play mode
        // nothing rebinds it on its own -- until it does, the animator reports no layers at all
        animator.Rebind();
        return animator;
    }

    static int LocomotionLayerIndex(Animator animator) =>
        Enumerable.Range(0, animator.layerCount).Single(index => animator.GetLayerName(index) == "Locomotion/Emotes");

    const int DrivenFrameLimit = 240;

    static int FramesUntil(Animator animator, int layer, string stateName, int limit = DrivenFrameLimit)
    {
        var frames = 0;
        while (frames < limit &&
               animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != Animator.StringToHash(stateName))
        {
            animator.Update(1f / 60f);
            frames++;
        }
        return frames;
    }

    static int FramesUntilNot(Animator animator, int layer, string stateName, int limit = DrivenFrameLimit)
    {
        var frames = 0;
        while (frames < limit &&
               animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == Animator.StringToHash(stateName))
        {
            animator.Update(1f / 60f);
            frames++;
        }
        return frames;
    }

    [Test]
    public void Convert_WithEmoteSet_LeavesTheHubForTheFoldedActionMachine()
    {
        var controller = Convert(convertActionLayer: true);
        var hub = LocomotionMachineOf(controller).defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetFloat("Emote", 2f);
        Assert.Less(FramesUntilNot(animator, layer, hub.name), 30,
            "Emote never carried the avatar out of the hub and into the folded Action machine");
    }

    [Test]
    public void Convert_WithEmoteCleared_ReturnsToTheHubThroughBlendOut()
    {
        // VRCEmote (unlike the merged Emote) stays Int, so its NotEqual exit is not adapted away --
        // holding it at 2 has to keep the avatar there for this test to prove anything
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var root = LocomotionMachineOf(controller);
        var hub = root.defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetInteger("VRCEmote", 2);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "fixture: VRCEmote never settled the avatar into the emote itself");
        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, hub.name),
            "the avatar reached the hub without VRCEmote ever leaving 2");

        animator.SetInteger("VRCEmote", 0);
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "clearing VRCEmote never carried the avatar back to the hub through BlendOut");
    }

    [Test]
    public void Convert_WithCancelEmoteTriggered_ReturnsToTheHub()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var root = LocomotionMachineOf(controller);
        var hub = root.defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetInteger("VRCEmote", 2);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "fixture: VRCEmote never settled the avatar into the emote itself");
        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, hub.name),
            "the avatar reached the hub without VRCEmote ever leaving 2 or CancelEmote ever firing");

        animator.SetTrigger("CancelEmote");
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "CancelEmote never returned the avatar to the hub");
    }

    [Test]
    public void Convert_WithCrouchingActiveAndEmoteCancelled_RedispatchesToCrouchingAfterTheHub()
    {
        Convert(convertActionLayer: true, authoredBase: false, declareVrcEmote: true);
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetFloat("Upright", 0.49f);
        animator.SetBool("Crouching", true);
        Assert.Less(FramesUntil(animator, layer, "Crouching Locomotion"), DrivenFrameLimit,
            "fixture: Crouching never routed the avatar to its own locomotion stance");

        animator.SetInteger("VRCEmote", 2);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "fixture: VRCEmote never settled the avatar into the emote itself");
        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, "Crouching Locomotion"),
            "the avatar re-dispatched to Crouching without VRCEmote ever leaving 2 or CancelEmote ever firing");

        animator.SetInteger("VRCEmote", 0);
        animator.SetTrigger("CancelEmote");
        Assert.Less(FramesUntil(animator, layer, "Crouching Locomotion"), DrivenFrameLimit,
            "cancelling the emote did not re-dispatch the hub back to Crouching");
    }

    // ---- driven: the Emote-to-VRCEmote compat feed layer bridges ChilloutVR's own quick menu ----

    [Test]
    public void Convert_WithVRCEmoteDeclared_QuickMenuEmoteBridgesIntoTheFoldedMachine()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var hub = LocomotionMachineOf(controller).defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetFloat("Emote", 2f);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "ChilloutVR's own quick-menu Emote never bridged into VRCEmote and settled the avatar into the emote");

        animator.SetTrigger("CancelEmote");
        animator.SetFloat("Emote", 0f);
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "CancelEmote never returned the avatar to the hub");
    }
}
#endif
