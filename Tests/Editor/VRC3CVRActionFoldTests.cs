#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

public class VRC3CVRActionFoldTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    const string ActionFoldTestFolder = "Assets/VRC3CVR_ActionFoldTest";
    const string EmoteStateName = "StandWave";
    const string SecondEmoteStateName = "StandPoint";
    const string OneShotEmoteStateName = "StandClap";
    const string SecondLayerStateName = "ActionSecondLayerState";

    const string ProxyEmoteClipName = "proxy_stand_dance";
    // ChilloutVR's wheel offers eight emotes, so the seated slot has nothing standing in the same place
    static readonly (string state, int slot)[] ProxyEmotes = { ("StandProxyEmote", 3), ("SitProxyEmote", 12) };

    const string AddedLayerName = "HhotateA_EMK_Emote";
    const string AddedLayerParameterName = "HhotateA_EMK_Emote";
    const string AddedLayerMachineName = "Action:HhotateA_EMK_Emote";
    const string AddedLayerIdleStateName = "Default";
    const float AddedLayerBlendDuration = 0.1f;

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
    static AnimatorController MakeActionController(bool authored, bool declareVrcEmote = false,
        bool addedLayer = false, bool addedLayerMasked = false, string addedLayerParameter = AddedLayerParameterName,
        bool proxyEmotes = false)
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

        var secondEmote = machine.AddState(SecondEmoteStateName);
        secondEmote.motion = clip;

        // stock's own shape for an emote that ends by itself: it runs out and leaves on its exit
        // time, with the emote number still standing
        var oneShotEmote = machine.AddState(OneShotEmoteStateName);
        oneShotEmote.motion = clip;

        var blendOut = machine.AddState("BlendOut");
        blendOut.AddStateMachineBehaviour<VRCPlayableLayerControl>().goalWeight = 0f;

        wait.AddTransition(prepare).AddCondition(AnimatorConditionMode.Greater, 0f, emoteParameter);
        prepare.AddTransition(emote).AddCondition(AnimatorConditionMode.Equals, 2f, emoteParameter);
        emote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, 2f, emoteParameter);
        prepare.AddTransition(secondEmote).AddCondition(AnimatorConditionMode.Equals, 5f, emoteParameter);
        secondEmote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, 5f, emoteParameter);
        prepare.AddTransition(oneShotEmote).AddCondition(AnimatorConditionMode.Equals, 7f, emoteParameter);
        oneShotEmote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, 7f, emoteParameter);
        oneShotEmote.AddTransition(blendOut).hasExitTime = true;
        blendOut.AddExitTransition().hasExitTime = true;

        // what a built avatar's Action still is underneath whatever its author replaced: VRChat's
        // own placeholders, which nothing outside its client swaps for an animation
        if (proxyEmotes)
        {
            var proxy = new AnimationClip { name = ProxyEmoteClipName };
            AssetDatabase.CreateAsset(proxy, ActionFoldTestFolder + "/" + ProxyEmoteClipName + ".anim");
            foreach (var (stateName, slot) in ProxyEmotes)
            {
                var proxyEmote = machine.AddState(stateName);
                proxyEmote.motion = proxy;
                prepare.AddTransition(proxyEmote).AddCondition(AnimatorConditionMode.Equals, slot, emoteParameter);
                proxyEmote.AddTransition(blendOut).AddCondition(AnimatorConditionMode.NotEqual, slot, emoteParameter);
            }
        }

        controller.AddLayer("ActionSecondLayer");
        controller.layers[1].stateMachine.AddState(SecondLayerStateName).motion = clip;

        if (addedLayer)
        {
            AddNEmoteShapedLayer(controller, clip, addedLayerMasked, addedLayerParameter);
        }

        return controller;
    }

    // What an emote-adding tool leaves on the Action playable, NEmote's shape down to the state
    // names: an empty idle the layer sits in, one state per emote entered on its own number, and a
    // Reset that runs out and drops back to the idle.
    static void AddNEmoteShapedLayer(AnimatorController controller, AnimationClip clip, bool masked, string parameterName)
    {
        if (!controller.parameters.Any(parameter => parameter.name == parameterName))
        {
            controller.AddParameter(parameterName, AnimatorControllerParameterType.Int);
        }
        controller.AddLayer(AddedLayerName);

        var layers = controller.layers;
        var added = layers[layers.Length - 1];
        // Unity adds a layer at zero weight; a tool that appends emotes raises it, or nothing it
        // added would ever play
        added.defaultWeight = 1f;
        if (masked)
        {
            var mask = new AvatarMask { name = "AddedLayerMask" };
            AssetDatabase.CreateAsset(mask, ActionFoldTestFolder + "/AddedLayerMask.mask");
            added.avatarMask = mask;
        }
        controller.layers = layers;

        var machine = added.stateMachine;
        var idle = machine.AddState(AddedLayerIdleStateName);
        idle.writeDefaultValues = false;
        machine.defaultState = idle;

        for (var n = 1; n <= 2; n++)
        {
            var emote = machine.AddState("Emote" + n);
            emote.motion = clip;
            emote.writeDefaultValues = false;
            var raiseWeight = emote.AddStateMachineBehaviour<VRCPlayableLayerControl>();
            raiseWeight.goalWeight = 1f;
            raiseWeight.blendDuration = AddedLayerBlendDuration;

            var reset = machine.AddState("Reset" + n);
            reset.motion = clip;
            reset.writeDefaultValues = false;

            idle.AddTransition(emote).AddCondition(AnimatorConditionMode.Equals, n, parameterName);
            emote.AddTransition(reset).AddCondition(AnimatorConditionMode.NotEqual, n, parameterName);
            reset.AddTransition(idle).hasExitTime = true;
        }
    }

    AnimatorController Convert(bool convertActionLayer, bool authoredAction = true, bool authoredBase = true,
        bool declareVrcEmote = false, bool vrcEmoteIsSynced = true, bool addedLayer = false, bool addedLayerMasked = false,
        string addedLayerParameter = AddedLayerParameterName, bool proxyEmotes = false)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(ActionFoldTestFolder);
        originalAvatar = descriptor.gameObject;

        if (declareVrcEmote && !vrcEmoteIsSynced)
        {
            descriptor.expressionParameters.parameters =
                descriptor.expressionParameters.parameters.Where(p => p.name != "VRCEmote").ToArray();
        }

        if (addedLayer && addedLayerParameter == AddedLayerParameterName)
        {
            // the tools that add these layers sync their parameter through the avatar's own
            // expression parameters, which is what keeps the name unprefixed after the conversion
            var expressionParameters = descriptor.expressionParameters.parameters.ToList();
            expressionParameters.Add(new VRCExpressionParameters.Parameter
            {
                name = AddedLayerParameterName,
                valueType = VRCExpressionParameters.ValueType.Int,
                networkSynced = true,
            });
            descriptor.expressionParameters.parameters = expressionParameters.ToArray();
        }

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
            animatorController = MakeActionController(
                authoredAction, declareVrcEmote, addedLayer, addedLayerMasked, addedLayerParameter, proxyEmotes),
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

        // ChilloutVR emotes out of each stance it can emote from, not out of the hub alone. Its
        // airborne states are at the layer's root rather than in a machine of their own, so they
        // come along; LocFlying/Swimming/Sitting must not, being modes rather than stances -- and
        // an emote entered from flight would be undone by the AnyState transition that put the
        // avatar there, one frame later, forever.
        CollectionAssert.AreEquivalent(
            new[] { "Standard Locomotion", "Crouching Locomotion", "Prone Locomotion", "JumpStart", "JumpAir", "JumpLand" },
            root.states
                .Where(child => child.state.transitions.Any(t => t.destinationStateMachine == actionMachine))
                .Select(child => child.state.name).ToArray(),
            "the stances the folded Action machine is entered from");

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

    // ---- the layers an emote-adding tool appends to Action, folded the same way the first one is ----

    [Test]
    public void Convert_WithAnAddedActionLayer_FoldsItAsAMachineOfItsOwn()
    {
        var root = LocomotionMachineOf(Convert(convertActionLayer: true, addedLayer: true));

        Assert.IsNotNull(ChildMachineNamed(root, "Action"), "the stock Action machine was not folded");
        var added = ChildMachineNamed(root, AddedLayerMachineName);
        Assert.IsNotNull(added, "the layer an emote tool added to Action was dropped");
        CollectionAssert.IsSubsetOf(
            new[] { AddedLayerIdleStateName, "Emote1", "Reset1", "Emote2", "Reset2" },
            AllStatesOf(added).Select(state => state.name).ToArray());
        Assert.IsFalse(AllStatesOf(added).Any(state => state.behaviours.Any(b => b is VRCPlayableLayerControl)),
            "a playable weight control survived the fold");
    }

    [Test]
    public void Convert_WithAnAddedActionLayer_EntersItFromEveryStanceOnEachConditionItsIdleLeftOn()
    {
        var root = LocomotionMachineOf(Convert(convertActionLayer: true, authoredBase: false, addedLayer: true));
        var added = ChildMachineNamed(root, AddedLayerMachineName);

        foreach (var stance in new[] { "Standard Locomotion", "Crouching Locomotion", "Prone Locomotion" })
        {
            var entries = root.states.Single(child => child.state.name == stance).state.transitions
                .Where(transition => transition.destinationStateMachine == added).ToArray();
            CollectionAssert.AreEquivalent(new[] { 1f, 2f },
                entries.Select(entry => entry.conditions.Single().threshold).ToArray(), stance + " entries");
            foreach (var entry in entries)
            {
                Assert.AreEqual(AddedLayerParameterName, entry.conditions.Single().parameter, stance + " entry");
                Assert.AreEqual(AnimatorConditionMode.Equals, entry.conditions.Single().mode, stance + " entry");
                Assert.IsFalse(entry.hasExitTime, stance + " entry waits for an exit time");
                Assert.AreEqual(AddedLayerBlendDuration, entry.duration, 1e-4f,
                    stance + " entry does not blend for the fade-in the layer's own weight control was worth");
            }
        }
    }

    [Test]
    public void Convert_WithAnAddedActionLayer_LeavesItThroughExitAndReturnsToTheHub()
    {
        var root = LocomotionMachineOf(Convert(convertActionLayer: true, addedLayer: true));
        var added = ChildMachineNamed(root, AddedLayerMachineName);

        var back = AllStatesOf(added).Single(state => state.name == "Reset1").transitions.Single();
        Assert.IsTrue(back.isExit, "the way back to the idle was not turned into a way out of the machine");
        Assert.IsNull(back.destinationState);
        Assert.IsTrue(back.hasExitTime, "the exit time the reset was timed against was lost");
        Assert.IsNotNull(AllStatesOf(added).SingleOrDefault(state => state.name == AddedLayerIdleStateName),
            "the idle the machine is entered through was removed");

        var leave = root.GetStateMachineTransitions(added).Single();
        Assert.AreEqual(root.defaultState, leave.destinationState, "the folded machine has no way back to the hub");
        Assert.AreEqual(0, leave.conditions.Length, "the return to the hub is conditional");
    }

    [Test]
    public void Convert_WithAnAddedActionLayer_LeavesItsWriteDefaultsAlone()
    {
        var added = ChildMachineNamed(
            LocomotionMachineOf(Convert(convertActionLayer: true, addedLayer: true)), AddedLayerMachineName);

        Assert.IsNotNull(added, "the layer an emote tool added to Action was dropped");
        Assert.IsFalse(AllStatesOf(added).Any(state => state.writeDefaultValues),
            "folding rewrote Write Defaults, mixing both settings inside one layer");
    }

    [Test]
    public void Convert_WithAnAddedActionLayer_AnswersCancelEmoteOnTheParameterThatLayerReads()
    {
        var added = ChildMachineNamed(
            LocomotionMachineOf(Convert(convertActionLayer: true, addedLayer: true)), AddedLayerMachineName);

        var escape = AllStatesOf(added).Single(state => state.name == "Emote1").transitions
            .Single(transition => transition.conditions.Any(condition => condition.parameter == "CancelEmote"));
        Assert.AreEqual(AllStatesOf(added).Single(state => state.name == "Reset1"), escape.destinationState,
            "the cancel does not reach where deselecting the emote would have");
        Assert.IsFalse(
            AllStatesOf(added).Single(state => state.name == AddedLayerIdleStateName).transitions
                .Any(transition => transition.conditions.Any(condition => condition.parameter == "CancelEmote")),
            "an entry-only (Equals) transition gained a CancelEmote escape");
    }

    [Test]
    public void Convert_WithAMaskedAddedActionLayer_LeavesItOutAndSaysWhy()
    {
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "Not converting the Action animator's \"" + AddedLayerName + "\" layer")));

        var root = LocomotionMachineOf(Convert(convertActionLayer: true, addedLayer: true, addedLayerMasked: true));

        Assert.IsNull(ChildMachineNamed(root, AddedLayerMachineName),
            "a layer masked to part of the body was folded into the layer that owns all of it");
        Assert.IsNotNull(ChildMachineNamed(root, "Action"), "the stock Action machine went out with the masked one");
    }

    // ChilloutVR declares Emote as a Float, and CopyParametersTo keeps that type, so a layer that
    // dispatched on it with Equals is left with an unconditional entry once the conversion has
    // adapted what it could -- an entry that would fire out of every stance on sight.
    [Test]
    public void Convert_WithAnAddedActionLayerDispatchingOnAFloatParameter_LeavesItOutAndSaysWhy()
    {
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "Not converting the Action animator's \"" + AddedLayerName
            + "\" layer: none of its dispatch conditions survived")));

        var root = LocomotionMachineOf(
            Convert(convertActionLayer: true, addedLayer: true, addedLayerParameter: "Emote"));

        Assert.IsNull(ChildMachineNamed(root, AddedLayerMachineName),
            "a layer whose entry conditions were all adapted away was folded in anyway");
        Assert.IsNotNull(ChildMachineNamed(root, "Action"), "the stock Action machine went out with the refused one");
    }

    // ---- the stock emotes underneath, played from ChilloutVR's own set of eight ----

    static string EmoteClipNameOf(AnimatorStateMachine actionMachine, string stateName) =>
        AllStatesOf(actionMachine).Single(state => state.name == stateName).motion.name;

    [Test]
    public void Convert_WithAStockEmoteProxy_PlaysChilloutVRsOwnEmoteOfThatSlot()
    {
        var actionMachine = ChildMachineNamed(
            LocomotionMachineOf(Convert(convertActionLayer: true, proxyEmotes: true)), "Action");

        Assert.AreEqual("Emote3", EmoteClipNameOf(actionMachine, ProxyEmotes[0].state),
            "a placeholder only VRChat's client swaps was left to play as itself");
        Assert.AreEqual("MyOwnWave", EmoteClipNameOf(actionMachine, EmoteStateName),
            "an emote the author animated was replaced by ChilloutVR's own");
    }

    [Test]
    public void Convert_WithAnEmoteProxyOnASlotChilloutVRHasNoEmoteFor_LeavesItAlone()
    {
        var actionMachine = ChildMachineNamed(
            LocomotionMachineOf(Convert(convertActionLayer: true, proxyEmotes: true)), "Action");

        Assert.AreEqual(ProxyEmoteClipName, EmoteClipNameOf(actionMachine, ProxyEmotes[1].state),
            "a slot outside ChilloutVR's eight was filled from them anyway");
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

        var idle = layer.stateMachine.defaultState;
        Assert.AreEqual("Idle", idle.name);
        Assert.IsEmpty(idle.behaviours, "Idle writes VRCEmote on its own enter, clobbering a custom menu that drives it directly");

        var cancel = layer.stateMachine.states.Single(child => child.state.name == "Cancel").state;
        Assert.AreEqual(0f, EnterValue(cancel), "the cancel state does not clear VRCEmote");
        Assert.AreEqual(idle, cancel.transitions.Single().destinationState, "the cancel state does not fall back to Idle");
        Assert.IsEmpty(cancel.transitions.Single().conditions, "the cancel state's fall back to Idle is conditional");

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
            Assert.IsEmpty(((AnimatorDriver)state.behaviours.Single()).ExitTasks,
                "Emote" + n + " clears VRCEmote as ChilloutVR's pulse ends, so the latch never outlives it");

            var less = state.transitions.Single(t => t.conditions.Single().mode == AnimatorConditionMode.Less);
            Assert.AreEqual(idle, less.destinationState, "Emote" + n + " leaves the band anywhere but Idle");
            Assert.AreEqual("Emote", less.conditions.Single().parameter);
            Assert.AreEqual((float)n, less.conditions.Single().threshold);

            var greater = state.transitions.Single(t => t.conditions.Single().mode == AnimatorConditionMode.Greater);
            Assert.AreEqual(idle, greater.destinationState, "Emote" + n + " leaves the band anywhere but Idle");
            Assert.AreEqual("Emote", greater.conditions.Single().parameter);
            Assert.AreEqual((float)n, greater.conditions.Single().threshold);

            var escape = state.transitions.Single(t => t.conditions.Single().parameter == "CancelEmote");
            Assert.AreEqual(AnimatorConditionMode.If, escape.conditions.Single().mode, "Emote" + n + " has no CancelEmote escape");
            Assert.AreEqual(cancel, escape.destinationState, "Emote" + n + "'s cancel skips the state that clears VRCEmote");
        }
    }

    [Test]
    public void Convert_WithVRCEmoteNotSynced_TargetsTheNonSyncPrefixedParameter()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true, vrcEmoteIsSynced: false);
        var layer = controller.layers.Single(l => l.name.StartsWith(VrcEmoteCompatLayerPrefix));

        string EnterTarget(string stateName) => ((AnimatorDriver)layer.stateMachine.states
            .Single(child => child.state.name == stateName).state.behaviours.Single()).EnterTasks.Single().targetName;

        Assert.AreEqual("#VRCEmote", EnterTarget("Emote1"));
        Assert.AreEqual("#VRCEmote", EnterTarget("Cancel"));
    }

    [Test]
    public void Convert_WithVRCEmoteDeclared_ReleasesTheLatchOnlyWhileTheEmoteStillHoldsIt()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var actionMachine = ChildMachineNamed(LocomotionMachineOf(controller), "Action");

        AnimatorDriver ReleaseOn(string stateName) => AllStatesOf(actionMachine)
            .Single(state => state.name == stateName).behaviours.OfType<AnimatorDriver>().SingleOrDefault();

        // every emote state holds a number of its own, and each has to test against its own
        foreach (var (stateName, number) in new[]
                 { (EmoteStateName, 2f), (SecondEmoteStateName, 5f), (OneShotEmoteStateName, 7f) })
        {
            var release = ReleaseOn(stateName);
            Assert.IsNotNull(release, stateName + " never lets go of the emote number it holds");
            Assert.IsEmpty(release.EnterTasks, stateName + " lets go of the number on the way in as well as out");
            Assert.AreEqual(2, release.ExitTasks.Count, stateName + " release");

            var test = release.ExitTasks[0];
            Assert.AreEqual(AnimatorDriverTask.Operator.Equal, test.op, stateName + " release test");
            Assert.AreEqual("#VRCEmoteHeld", test.targetName);
            Assert.AreEqual("VRCEmote", test.aName);
            Assert.AreEqual(number, test.bValue, stateName + " tests against a number it does not hold");

            var answer = release.ExitTasks[1];
            Assert.AreEqual(AnimatorDriverTask.Operator.Conditional, answer.op, stateName + " release answer");
            Assert.AreEqual("VRCEmote", answer.targetName);
            Assert.AreEqual("#VRCEmoteHeld", answer.aName);
            Assert.AreEqual(0f, answer.bValue, stateName + " does not let the number go when it is still its own");
            Assert.AreEqual("VRCEmote", answer.cName, stateName + " drops a number that had already moved on");
        }

        Assert.IsNull(ReleaseOn("BlendOut"), "a state that holds no emote number lets go of one anyway");
        Assert.IsNull(ReleaseOn("WaitForActionOrAFK"), "a state that holds no emote number lets go of one anyway");
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

    [Test]
    public void Convert_WithAnAddedActionLayersNumberSet_PlaysItsEmoteAndReturnsToTheHub()
    {
        var controller = Convert(convertActionLayer: true, addedLayer: true);
        var hub = LocomotionMachineOf(controller).defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetInteger(AddedLayerParameterName, 1);
        Assert.Less(FramesUntil(animator, layer, "Emote1"), DrivenFrameLimit,
            "the added layer's own number never carried the avatar out of the hub and into its emote");
        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, hub.name),
            "the avatar left the emote without its number ever changing");

        animator.SetInteger(AddedLayerParameterName, 0);
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "clearing the number never carried the avatar back to the hub through the reset");
    }

    [Test]
    public void Convert_WithAnAddedActionLayersEmotePlaying_CancelEmoteLeavesTheEmote()
    {
        Convert(convertActionLayer: true, addedLayer: true);
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetInteger(AddedLayerParameterName, 1);
        Assert.Less(FramesUntil(animator, layer, "Emote1"), DrivenFrameLimit,
            "fixture: the added layer's emote never started");

        // the number stays where the tool's own menu left it, so the reset -- where deselecting the
        // emote would have gone -- is reachable by nothing but the cancel
        animator.SetTrigger("CancelEmote");
        Assert.Less(FramesUntil(animator, layer, "Reset1"), DrivenFrameLimit,
            "the quick menu's cancel never reached an emote a tool added");
    }

    [Test]
    public void Convert_WithCrouchingActiveAndAnAddedActionLayersEmoteEnded_RedispatchesToCrouching()
    {
        Convert(convertActionLayer: true, authoredBase: false, addedLayer: true);
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetFloat("Upright", 0.49f);
        animator.SetBool("Crouching", true);
        Assert.Less(FramesUntil(animator, layer, "Crouching Locomotion"), DrivenFrameLimit,
            "fixture: Crouching never routed the avatar to its own locomotion stance");

        animator.SetInteger(AddedLayerParameterName, 1);
        Assert.Less(FramesUntil(animator, layer, "Emote1"), DrivenFrameLimit,
            "the added layer's emote is not entered from a stance other than the hub");

        animator.SetInteger(AddedLayerParameterName, 0);
        Assert.Less(FramesUntil(animator, layer, "Crouching Locomotion"), DrivenFrameLimit,
            "the emote ending did not re-dispatch the hub back to Crouching");
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

    // ChilloutVR holds the emote number for about a tenth of a second and then clears it itself
    static void Pulse(Animator animator, float emote)
    {
        animator.SetFloat("Emote", emote);
        for (var frame = 0; frame < 6; frame++)
        {
            animator.Update(1f / 60f);
        }
        animator.SetFloat("Emote", 0f);
    }

    [Test]
    public void Convert_WithEmotePulsed_LatchesVRCEmoteUntilTheEmoteIsCancelled()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var hub = LocomotionMachineOf(controller).defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        Pulse(animator, 2f);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "the emote never settled once ChilloutVR's pulse had ended");
        Assert.AreEqual(2, animator.GetInteger("VRCEmote"), "the latch came down with the pulse");

        animator.SetTrigger("CancelEmote");
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "CancelEmote never returned the avatar to the hub");
        Assert.AreEqual(0, animator.GetInteger("VRCEmote"), "the latch outlived the emote it was holding");

        Pulse(animator, 2f);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "pressing the same emote again did not start it again");
    }

    [Test]
    public void Convert_WithAnEmoteThatEndsOnItsOwn_LetsTheNumberGoAndDoesNotStartItAgain()
    {
        var controller = Convert(convertActionLayer: true, declareVrcEmote: true);
        var hub = LocomotionMachineOf(controller).defaultState;
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        Pulse(animator, 7f);
        Assert.Less(FramesUntil(animator, layer, OneShotEmoteStateName), DrivenFrameLimit,
            "fixture: the one-shot emote never started");

        // it runs out on its own, with nothing having touched the number
        Assert.Less(FramesUntil(animator, layer, hub.name), DrivenFrameLimit,
            "the emote never ended on its own");
        Assert.AreEqual(0, animator.GetInteger("VRCEmote"),
            "an emote that ended on its own left its number standing, and the hub dispatches on that");

        // the number is what the hub dispatches on, so a number left standing shows up as the emote
        // starting over rather than as a stale parameter
        Assert.AreEqual(DrivenFrameLimit, FramesUntil(animator, layer, OneShotEmoteStateName),
            "the emote started itself again after ending");
    }

    [Test]
    public void Convert_WithEmoteSwitchedWhilePlaying_ReachesTheEmoteThatWasSwitchedTo()
    {
        Convert(convertActionLayer: true, declareVrcEmote: true);
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        Pulse(animator, 2f);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "fixture: the first emote never started");

        // driven past BlendOut and back through the hub, which is the whole point: the first emote
        // lets go of the number on its way out, and stopping at the switch would never see that
        Pulse(animator, 5f);
        Assert.Less(FramesUntil(animator, layer, SecondEmoteStateName), DrivenFrameLimit,
            "switching emotes while one played never reached the second one");
        Assert.AreEqual(5, animator.GetInteger("VRCEmote"),
            "the emote being switched away from took the new number down with it");
    }

    [Test]
    public void Convert_WithEmoteSwitchedDirectly_SettlesVRCEmoteOnTheNewSelection()
    {
        Convert(convertActionLayer: true, declareVrcEmote: true);
        var animator = DriveAnimator(convertedAvatar);
        var layer = LocomotionLayerIndex(animator);

        animator.SetFloat("Emote", 2f);
        Assert.Less(FramesUntil(animator, layer, EmoteStateName), DrivenFrameLimit,
            "fixture: Emote never settled the avatar into the emote itself");

        animator.SetFloat("Emote", 5f);
        var frames = 0;
        while (frames < DrivenFrameLimit && animator.GetInteger("VRCEmote") != 5)
        {
            animator.Update(1f / 60f);
            frames++;
        }
        Assert.AreEqual(5, animator.GetInteger("VRCEmote"),
            "switching Emote directly from 2 to 5 did not settle VRCEmote on the new selection");
    }
}
#endif
