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

public class VRC3CVRBaseGraftTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    static bool InvokeHasAuthoredMotion(AnimatorController controller) =>
        (bool)typeof(VRC3CVRCore).GetMethod("HasAuthoredMotion", Flags).Invoke(null, new object[] { controller });

    static IEnumerable<AnimatorState> AllStatesOf(AnimatorStateMachine machine) =>
        (IEnumerable<AnimatorState>)typeof(VRC3CVRCore).GetMethod("AllStatesOf", Flags).Invoke(null, new object[] { machine });

    static AnimatorController MakeController(string name, params Motion[] motions)
    {
        var controller = new AnimatorController { name = name };
        controller.AddLayer("L");
        var layers = controller.layers;
        var machine = layers[0].stateMachine;
        for (var i = 0; i < motions.Length; i++)
        {
            machine.AddState("S" + i).motion = motions[i];
        }
        return controller;
    }

    [Test]
    public void HasAuthoredMotion_FalseWhenEveryClipIsAVrchatPlaceholder()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var controller = MakeController("allProxy", proxy);

        Assert.IsFalse(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_TrueWhenAnyClipIsTheAvatarsOwn()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var controller = MakeController("mixed", proxy, own);

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_LooksInsideBlendTrees()
    {
        var own = new AnimationClip { name = "MyCoolWalk" };
        var innerTree = new BlendTree { name = "InnerTree" };
        innerTree.AddChild(own);
        var outerTree = new BlendTree { name = "OuterTree" };
        outerTree.AddChild(innerTree);
        var controller = MakeController("tree", outerTree);

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(own);
        Object.DestroyImmediate(innerTree);
        Object.DestroyImmediate(outerTree);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_LooksInsideSubStateMachines()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var controller = MakeController("sub", proxy);
        var root = controller.layers[0].stateMachine;
        var sub = root.AddStateMachine("Sub");
        sub.AddState("S0").motion = own;

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_FalseForAnEmptyController()
    {
        var controller = MakeController("empty");

        Assert.IsFalse(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void PlaceholderSubstitutions_CoverTheLocomotionProxiesAndPointAtRealCckClips()
    {
        var map = (System.Collections.Generic.Dictionary<string, string>)
            typeof(VRC3CVRCore).GetField("placeholderClipSubstitutions", Flags).GetValue(null);

        // the proxies a locomotion blend tree actually references
        foreach (var proxy in new[]
        {
            "proxy_stand_still", "proxy_walk_forward", "proxy_walk_backward",
            "proxy_strafe_right", "proxy_run_forward", "proxy_run_backward",
            "proxy_crouch_still", "proxy_crouch_walk_forward",
            "proxy_low_crawl_still", "proxy_low_crawl_forward",
            "proxy_fall_short", "proxy_landing", "proxy_sit",
        })
        {
            Assert.IsTrue(map.ContainsKey(proxy), proxy + " has no ChilloutVR counterpart");
        }

        foreach (var pair in map)
        {
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/CVR.CCK/Assets/Avatar/Animations/Locomotion/" + pair.Value + ".anim");
            Assert.IsNotNull(clip, pair.Key + " maps to " + pair.Value + ", which is not in the CCK");
        }
    }

    [Test]
    public void SubstitutePlaceholderClips_ReplacesProxiesInsideBlendTreesAndLeavesAuthoredClipsAlone()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var tree = new BlendTree { name = "Tree" };
        tree.AddChild(proxy);
        tree.AddChild(own);
        var controller = MakeController("tree", tree);

        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags).Invoke(core, new object[] { controller });

        var children = ((BlendTree)controller.layers[0].stateMachine.states[0].state.motion).children;
        Assert.AreEqual("LocWalkingForward", children[0].motion.name);
        Assert.AreEqual("MyCoolWalk", children[1].motion.name, "the author's own clip is untouched");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(tree);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void SubstitutePlaceholderClips_LeavesABlendTreeAssetItDoesNotOwnAlone()
    {
        EnsureTestFolder();
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        AssetDatabase.CreateAsset(proxy, GraftTestFolder + "/proxy_walk_forward.anim");
        var shared = new BlendTree { name = "SharedTree" };
        shared.AddChild(proxy);
        AssetDatabase.CreateAsset(shared, GraftTestFolder + "/SharedTree.asset");
        var controller = MakeController("shared", shared);

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        Assert.AreEqual("proxy_walk_forward", shared.children[0].motion.name,
            "an asset outside the conversion's own clone was rewritten");
        var substituted = (BlendTree)controller.layers[0].stateMachine.states[0].state.motion;
        Assert.AreNotSame(shared, substituted);
        Assert.AreEqual("LocWalkingForward", substituted.children[0].motion.name);

        Object.DestroyImmediate(controller);
    }

    // ---- the graft itself, over a whole conversion ----

    const string GraftTestFolder = "Assets/VRC3CVR_BaseGraftTest";
    const string GroundStateName = "AvatarGroundLocomotion";
    const string AvatarOwnAnyStateParameter = "AvatarOwnFlag";

    GameObject originalAvatar;
    GameObject convertedAvatar;

    static void EnsureTestFolder()
    {
        if (!AssetDatabase.IsValidFolder(GraftTestFolder))
        {
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(GraftTestFolder));
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (originalAvatar != null) Object.DestroyImmediate(originalAvatar);
        if (convertedAvatar != null) Object.DestroyImmediate(convertedAvatar);
        originalAvatar = null;
        convertedAvatar = null;
        AssetDatabase.DeleteAsset(GraftTestFolder);
    }

    AnimatorController ConvertWithBaseLayer(bool authored, bool convertLocomotionLayer, bool hubless = false)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(GraftTestFolder);
        originalAvatar = descriptor.gameObject;

        var clip = new AnimationClip { name = authored ? "AuthoredWalk" : "proxy_stand_still" };
        AssetDatabase.CreateAsset(clip, GraftTestFolder + "/" + clip.name + ".anim");
        var baseController = AnimatorController.CreateAnimatorControllerAtPath(GraftTestFolder + "/Base.controller");
        baseController.AddParameter(AvatarOwnAnyStateParameter, AnimatorControllerParameterType.Bool);
        var baseMachine = baseController.layers[0].stateMachine;
        if (hubless)
        {
            // Unity resolves a default state through sub-state-machines and refuses to have it
            // cleared, so the only shape that really has none is a first layer with no states at
            // all -- which leaves the motion HasAuthoredMotion looks for on a later layer.
            baseController.AddLayer("Authored");
            baseController.layers[1].stateMachine.AddState(GroundStateName).motion = clip;
            Assert.IsNull(baseMachine.defaultState, "fixture: the first layer still has a default state");
        }
        else
        {
            var groundState = baseMachine.AddState(GroundStateName);
            groundState.motion = clip;
            baseMachine.AddAnyStateTransition(groundState)
                .AddCondition(AnimatorConditionMode.If, 0f, AvatarOwnAnyStateParameter);
            groundState.AddExitTransition().AddCondition(AnimatorConditionMode.If, 0f, AvatarOwnAnyStateParameter);
        }

        var layers = descriptor.baseAnimationLayers;
        layers[0] = new VRCAvatarDescriptor.CustomAnimLayer
        {
            type = VRCAvatarDescriptor.AnimLayerType.Base,
            isDefault = false,
            animatorController = baseController,
        };
        descriptor.baseAnimationLayers = layers;

        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
            convertLocomotionLayer = convertLocomotionLayer,
        });
        core.Convert();
        convertedAvatar = core.chilloutAvatar;
        Assert.IsNotNull(convertedAvatar);

        var controller = convertedAvatar.GetComponent<CVRAvatar>().avatarSettings.baseController as AnimatorController;
        Assert.IsNotNull(controller);
        return controller;
    }

    static AnimatorControllerLayer LocomotionLayerOf(AnimatorController controller) =>
        controller.layers.Single(layer => layer.name == "Locomotion/Emotes");

    // ChilloutVR's locomotion layer and layers merged from the VRC Base must never coexist: merged
    // above it, an Override layer at weight 1 replaces it rather than adds to it. So a declined
    // graft keeps the CCK layer and drops the Base animator whole.
    static void AssertCckLocomotionKept(AnimatorController controller)
    {
        var machine = LocomotionLayerOf(controller).stateMachine;
        Assert.IsTrue(machine.states.Any(child => child.state.name == "LocFlying"));
        Assert.IsFalse(
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Any(state => state.name == GroundStateName),
            "the avatar's own Base layer was merged alongside ChilloutVR's locomotion");
        Assert.IsFalse(controller.animationClips.Any(clip => clip.name.StartsWith("proxy_")),
            "a VRChat placeholder clip came across with the Base layer");
    }

    static void AssertCondition(
        AnimatorTransitionBase transition, string parameter, AnimatorConditionMode mode, string what)
    {
        Assert.AreEqual(1, transition.conditions.Length, what);
        Assert.AreEqual(parameter, transition.conditions[0].parameter, what);
        Assert.AreEqual(mode, transition.conditions[0].mode, what);
    }

    static void AssertModeReachableAndLeavable(
        AnimatorStateMachine root, AnimatorState hub, AnimatorStateTransition entry, string stateName, string parameter)
    {
        var state = root.states.Single(child => child.state.name == stateName).state;
        Assert.AreEqual(state, entry.destinationState);
        Assert.IsFalse(entry.hasExitTime, parameter + " entry waits for an exit time");
        AssertCondition(entry, parameter, AnimatorConditionMode.If, parameter + " entry");

        var leave = state.transitions.Single();
        Assert.AreEqual(hub, leave.destinationState, stateName + " does not lead back to the hub");
        Assert.IsFalse(leave.hasExitTime, stateName + " exit waits for an exit time");
        AssertCondition(leave, parameter, AnimatorConditionMode.IfNot, stateName + " exit");
    }

    [Test]
    public void Convert_WithAProxyOnlyBaseLayer_KeepsChilloutVRsOwnLocomotion()
    {
        AssertCckLocomotionKept(ConvertWithBaseLayer(authored: false, convertLocomotionLayer: true));
    }

    [Test]
    public void Convert_WithLocomotionConversionOff_KeepsChilloutVRsOwnLocomotion()
    {
        AssertCckLocomotionKept(ConvertWithBaseLayer(authored: true, convertLocomotionLayer: false));
    }

    [Test]
    public void Convert_WithABaseLayerThatHasNoDefaultState_KeepsChilloutVRsOwnLocomotion()
    {
        var controller = ConvertWithBaseLayer(authored: true, convertLocomotionLayer: true, hubless: true);

        AssertCckLocomotionKept(controller);
        Assert.AreEqual(1,
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Count(state => state.name == "LocFlying"),
            "a salvaged state was grafted even though the graft was declined");
    }

    [Test]
    public void Convert_WithAnAuthoredBaseLayer_TakesOverLocomotionAndReconnectsFlightSwimmingAndEmotes()
    {
        var controller = ConvertWithBaseLayer(authored: true, convertLocomotionLayer: true);
        var locomotionLayer = LocomotionLayerOf(controller);
        var root = locomotionLayer.stateMachine;

        var hub = root.defaultState;
        Assert.AreEqual(GroundStateName, hub.name, "the avatar's own locomotion is not the hub");
        Assert.IsTrue(locomotionLayer.iKPass, "the layer that owns the body runs no IK pass");

        var anyStateTransitions = root.anyStateTransitions;
        Assert.AreEqual(2, anyStateTransitions.Length);
        Assert.IsFalse(anyStateTransitions[0].canTransitionToSelf, "the flight entry can transition to itself");
        AssertModeReachableAndLeavable(root, hub, anyStateTransitions[0], "LocFlying", "Flying");
        Assert.AreEqual(GroundStateName, anyStateTransitions[1].destinationState.name,
            "the avatar's own AnyState transition does not come last");

        var hubTransitions = hub.transitions;
        Assert.AreEqual(3, hubTransitions.Length);
        Assert.IsTrue(hubTransitions[2].isExit, "the avatar's own hub transition does not come last");
        AssertModeReachableAndLeavable(root, hub, hubTransitions[0], "Swimming", "Swimming");

        var emotes = root.stateMachines.Single(child => child.stateMachine.name == "Emotes").stateMachine;
        Assert.IsTrue(AllStatesOf(emotes).Any(state => state.name == "Emote1"));
        Assert.AreEqual(emotes, hubTransitions[1].destinationStateMachine);
        Assert.IsFalse(hubTransitions[1].hasExitTime, "the emote entry waits for an exit time");
        AssertCondition(hubTransitions[1], "Emote", AnimatorConditionMode.Greater, "emote entry");
        var leaveEmotes = root.GetStateMachineTransitions(emotes).Single();
        Assert.AreEqual(hub, leaveEmotes.destinationState, "the Emotes machine has no way back out");
        Assert.AreEqual(0, leaveEmotes.conditions.Length, "the emote return is conditional");
        Assert.IsTrue(AllStatesOf(emotes).All(state => state.transitions.Any(t => t.isExit)),
            "an emote has no transition to the Exit node");

        foreach (var parameter in new[] { "Flying", "Swimming", "Emote", "CancelEmote" })
        {
            Assert.IsTrue(controller.parameters.Any(p => p.name == parameter),
                parameter + " is no longer declared");
        }
    }
}
#endif
