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

    // ---- the graft itself, over a whole conversion ----

    const string GraftTestFolder = "Assets/VRC3CVR_BaseGraftTest";
    const string GroundStateName = "AvatarGroundLocomotion";

    GameObject originalAvatar;
    GameObject convertedAvatar;

    [TearDown]
    public void TearDown()
    {
        if (originalAvatar != null) Object.DestroyImmediate(originalAvatar);
        if (convertedAvatar != null) Object.DestroyImmediate(convertedAvatar);
        originalAvatar = null;
        convertedAvatar = null;
        AssetDatabase.DeleteAsset(GraftTestFolder);
    }

    AnimatorController ConvertWithBaseLayer(bool authored, bool convertLocomotionLayer)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(GraftTestFolder);
        originalAvatar = descriptor.gameObject;

        var clip = new AnimationClip { name = authored ? "AuthoredWalk" : "proxy_stand_still" };
        AssetDatabase.CreateAsset(clip, GraftTestFolder + "/" + clip.name + ".anim");
        var baseController = AnimatorController.CreateAnimatorControllerAtPath(GraftTestFolder + "/Base.controller");
        baseController.layers[0].stateMachine.AddState(GroundStateName).motion = clip;

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

    static void AssertCckLocomotionKept(AnimatorController controller)
    {
        var machine = LocomotionLayerOf(controller).stateMachine;
        Assert.IsTrue(machine.states.Any(child => child.state.name == "LocFlying"));
        Assert.IsFalse(AllStatesOf(machine).Any(state => state.name == GroundStateName),
            "the avatar's own locomotion took the layer over");
    }

    static void AssertMovementModeReconnected(
        AnimatorStateMachine root, AnimatorStateMachine ground, string parameter, string stateName)
    {
        var mode = root.stateMachines.Single(child => child.stateMachine.name == parameter).stateMachine;
        Assert.IsTrue(AllStatesOf(mode).Any(state => state.name == stateName));

        var enter = root.GetStateMachineTransitions(ground).Single(t => t.destinationStateMachine == mode);
        Assert.AreEqual(1, enter.conditions.Length);
        Assert.AreEqual(parameter, enter.conditions[0].parameter);
        Assert.AreEqual(AnimatorConditionMode.If, enter.conditions[0].mode);

        var leave = root.GetStateMachineTransitions(mode).Single();
        Assert.AreEqual(ground, leave.destinationStateMachine);
        Assert.AreEqual(1, leave.conditions.Length);
        Assert.AreEqual(parameter, leave.conditions[0].parameter);
        Assert.AreEqual(AnimatorConditionMode.IfNot, leave.conditions[0].mode);
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
    public void Convert_WithAnAuthoredBaseLayer_TakesOverLocomotionAndReconnectsFlightAndSwimming()
    {
        var controller = ConvertWithBaseLayer(authored: true, convertLocomotionLayer: true);
        var root = LocomotionLayerOf(controller).stateMachine;

        var ground = root.entryTransitions.Single().destinationStateMachine;
        Assert.IsNotNull(ground);
        Assert.IsTrue(AllStatesOf(ground).Any(state => state.name == GroundStateName));

        AssertMovementModeReconnected(root, ground, "Flying", "LocFlying");
        AssertMovementModeReconnected(root, ground, "Swimming", "Swimming");

        foreach (var parameter in new[] { "Flying", "Swimming" })
        {
            Assert.IsTrue(
                controller.parameters.Any(p => p.name == parameter && p.type == AnimatorControllerParameterType.Bool),
                parameter + " is no longer declared");
        }
    }
}
#endif
