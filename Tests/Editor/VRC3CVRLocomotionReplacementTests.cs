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

public class VRC3CVRLocomotionReplacementTests
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
        AssetDatabase.CreateAsset(proxy, ReplacementTestFolder + "/proxy_walk_forward.anim");
        var shared = new BlendTree { name = "SharedTree" };
        shared.AddChild(proxy);
        AssetDatabase.CreateAsset(shared, ReplacementTestFolder + "/SharedTree.asset");
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

    // The pose has to be one frame long: zero would be run as a one second loop, and the real clip's
    // own length is what strands the avatar.
    static void AssertIsPoseOf(Motion motion, string cckClipName)
    {
        var pose = (AnimationClip)motion;
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/CVR.CCK/Assets/Avatar/Animations/Locomotion/" + cckClipName + ".anim");
        Assert.AreEqual(cckClipName + "_Pose", pose.name);
        Assert.AreEqual(1f / 60f, pose.length, 1e-4f);
        Assert.IsTrue(pose.humanMotion, "the pose does not drive the humanoid rig");

        var bindings = AnimationUtility.GetCurveBindings(source);
        Assert.AreEqual(bindings.Length, AnimationUtility.GetCurveBindings(pose).Length,
            "the pose does not carry the same curves as the clip it came from");
        Assert.AreEqual(
            AnimationUtility.GetEditorCurve(source, bindings[0]).Evaluate(0f),
            AnimationUtility.GetEditorCurve(pose, bindings[0]).Evaluate(0f), 1e-4f,
            "the pose holds something other than the clip's first frame on " + bindings[0].propertyName);

        // everything but the timing, which belongs to the pose rather than the clip it came from
        var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
        var poseSettings = AnimationUtility.GetAnimationClipSettings(pose);
        bool[] RootMotionFlags(AnimationClipSettings s) => new[]
        {
            s.loopBlend, s.loopBlendOrientation, s.loopBlendPositionY, s.loopBlendPositionXZ,
            s.keepOriginalOrientation, s.keepOriginalPositionY, s.keepOriginalPositionXZ,
            s.heightFromFeet, s.mirror,
        };
        Assert.AreEqual(RootMotionFlags(sourceSettings), RootMotionFlags(poseSettings),
            "the pose applies its root curves differently than the clip it came from");
        Assert.AreEqual(sourceSettings.orientationOffsetY, poseSettings.orientationOffsetY, 1e-4f);
        Assert.AreEqual(sourceSettings.level, poseSettings.level, 1e-4f);
        Assert.AreEqual(sourceSettings.cycleOffset, poseSettings.cycleOffset, 1e-4f);
    }

    [Test]
    public void SubstitutePlaceholderClips_GivesAPassThroughStateThePoseOfItsChilloutVRClip()
    {
        var proxy = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_stand_still");
        var controller = MakeController("passThrough", proxy);
        var state = controller.layers[0].stateMachine.states[0].state;
        var exit = state.AddExitTransition();
        exit.hasExitTime = true;
        exit.exitTime = 0f;

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        AssertIsPoseOf(state.motion, "LocIdle");
        Assert.AreEqual(0f, state.transitions.Single().exitTime, "the exit time was rewritten");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    // The landing is the one pass-through that plays its ChilloutVR clip in full, on the exit
    // timing of the CCK's own JumpLand state -- the pose treatment would hold the landing crouch.
    [Test]
    public void SubstitutePlaceholderClips_PlaysTheLandingClipInFullOnTheCckJumpLandTiming()
    {
        var proxy = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_land_quick");
        var controller = MakeController("quickLand", proxy);
        var state = controller.layers[0].stateMachine.states[0].state;
        var exit = state.AddExitTransition();
        exit.hasExitTime = true;
        exit.exitTime = 1f;
        exit.duration = 0.1f;
        var conditional = state.AddExitTransition();
        conditional.hasExitTime = false;
        conditional.duration = 0.05f;

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        Assert.AreEqual("LocJumpLand", state.motion.name, "the landing does not play the real clip");
        Assert.Greater(((AnimationClip)state.motion).length, 0.5f, "the landing clip was reduced to a pose");
        var timed = state.transitions.Single(t => t.hasExitTime);
        Assert.AreEqual(0.5588235f, timed.exitTime, 1e-4f);
        Assert.AreEqual(0.25f, timed.duration, 1e-4f);
        Assert.IsTrue(timed.hasFixedDuration);
        Assert.AreEqual(0.05f, state.transitions.Single(t => !t.hasExitTime).duration, 1e-5f,
            "a conditional transition was rewritten");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void SubstitutePlaceholderClips_WithTheLandingAnimationOff_PosesTheLandingPassThrough()
    {
        var proxy = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_land_quick");
        var controller = MakeController("quickLand", proxy);
        var state = controller.layers[0].stateMachine.states[0].state;
        var exit = state.AddExitTransition();
        exit.hasExitTime = true;
        exit.exitTime = 1f;

        var core = new VRC3CVRCore { playLandingAnimation = false };
        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(core, new object[] { controller });

        AssertIsPoseOf(state.motion, "LocJumpLand");
        Assert.AreEqual(1f, state.transitions.Single().exitTime, "the exit time was rewritten");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void SubstitutePlaceholderClips_SubstitutesAZeroLengthPlaceholderWithNothingTimedAgainstIt()
    {
        var proxy = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_sit");
        var controller = MakeController("seated", proxy);
        var state = controller.layers[0].stateMachine.states[0].state;
        state.AddExitTransition().hasExitTime = false;

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        Assert.AreEqual("LocSitting", state.motion.name, "no exit time rides on this state's clip length");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void SubstitutePlaceholderClips_SubstitutesAPlaceholderThatHasRealLength()
    {
        // stock's HardLand: proxy_landing really is 1.03s long, so its exit time scales as normalized
        var proxy = new AnimationClip { name = "proxy_landing" };
        proxy.SetCurve("Placeholder", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1.033f, 1f));
        var controller = MakeController("hardLand", proxy);
        var state = controller.layers[0].stateMachine.states[0].state;
        var exit = state.AddExitTransition();
        exit.hasExitTime = true;
        exit.exitTime = 0.6f;

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        Assert.AreEqual("LocJumpLand", state.motion.name);
        Assert.AreEqual(0.6f, state.transitions.Single().exitTime, "the fraction the author asked for changed");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    // What the state machine holds cannot say whether the pass-through still passes: the crossing
    // comes round only as often as the motion behind it is long, and is not checked at all until the
    // entry blend finishes. Both are timing, so the animator is run.
    [Test]
    public void SubstitutePlaceholderClips_APassThroughStateIsStillLeftPromptlyAfterAStockEntryBlend()
    {
        var proxy = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_stand_still");
        var controller = MakeController("driven");
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        var root = controller.layers[0].stateMachine;
        var hub = root.AddState("Hub");
        root.defaultState = hub;
        var passMachine = root.AddStateMachine("PassThroughMachine");
        var passState = passMachine.AddState("PassThrough");
        passState.motion = proxy;
        StockTimed(passState.AddExitTransition()).exitTime = 0f;
        root.AddStateMachineTransition(passMachine, hub);
        var toPass = StockTimed(hub.AddTransition(passMachine));
        toPass.hasExitTime = false;
        toPass.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags)
            .Invoke(new VRC3CVRCore(), new object[] { controller });

        var go = new GameObject("PassThroughProbe");
        try
        {
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.SetBool("Grounded", true);
            animator.Update(0f);
            animator.SetBool("Grounded", false);
            Assert.Less(FramesUntil(animator, 0, "PassThrough"), 30, "the fixture never entered the state");

            animator.SetBool("Grounded", true);
            // entry blend 15F + a frame or two for the crossing + exit blend 15F. Tight enough to
            // fail the zero-length regression too, which measured ~60F before the pose landed here.
            Assert.Less(FramesUntil(animator, 0, "Hub"), 40,
                "the pass-through state held the avatar for a whole loop of a substituted clip");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    // The blend stock locomotion gives these transitions. A fixture that cut it to zero would let a
    // broken pass-through look healthy, since the entry blend is what hides the exit time's window.
    static AnimatorStateTransition StockTimed(AnimatorStateTransition transition)
    {
        transition.hasExitTime = true;
        transition.hasFixedDuration = true;
        transition.duration = 0.25f;
        return transition;
    }

    static int FramesUntil(Animator animator, int layer, string stateName, int limit = 240)
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

    // ProcessStateMachine replaces proxies too, on its own map, and runs after the substitution
    // above -- so both have to pose a pass-through. layerName only feeds a warning message.
    static void RunProcessStateMachine(AnimatorStateMachine machine) =>
        RunProcessStateMachine(new VRC3CVRCore(), machine);

    static void RunProcessStateMachine(VRC3CVRCore core, AnimatorStateMachine machine)
    {
        typeof(VRC3CVRCore).GetMethod("ProcessStateMachine", Flags)
            .Invoke(core, new object[] { machine, "TestLayer", new AnimatorControllerParameter[0] });
    }

    static AnimatorStateMachine MachineAround(AnimatorState state)
    {
        var machine = new AnimatorStateMachine();
        machine.states = new[] { new ChildAnimatorState { state = state } };
        return machine;
    }

    [Test]
    public void ProcessStateMachine_PosesTheClipsInAPassThroughStatesBlendTree()
    {
        var first = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_stand_still");
        var second = VRC3CVRVerificationAvatar.ZeroLengthClip("proxy_idle");
        var tree = new BlendTree { name = "PassThroughTree" };
        tree.AddChild(first);
        tree.AddChild(second);
        var state = new AnimatorState { name = "RestoreTracking", motion = tree };
        var exit = state.AddExitTransition();
        exit.hasExitTime = true;
        exit.exitTime = 0f;

        RunProcessStateMachine(MachineAround(state));

        foreach (var child in ((BlendTree)state.motion).children)
        {
            AssertIsPoseOf(child.motion, "LocIdle");
        }

        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
        Object.DestroyImmediate(tree);
    }

    [Test]
    public void ProcessStateMachine_LeavesABlendTreeAssetItDoesNotOwnAlone()
    {
        EnsureTestFolder();
        var proxy = new AnimationClip { name = "proxy_stand_still" };
        AssetDatabase.CreateAsset(proxy, ReplacementTestFolder + "/proxy_stand_still.anim");
        var shared = new BlendTree { name = "SharedTree" };
        shared.AddChild(proxy);
        AssetDatabase.CreateAsset(shared, ReplacementTestFolder + "/SharedTree.asset");
        var state = new AnimatorState { name = "S", motion = shared };

        RunProcessStateMachine(MachineAround(state));

        Assert.AreEqual("proxy_stand_still", shared.children[0].motion.name,
            "an asset outside the conversion's own clone was rewritten");
        Assert.AreNotSame(shared, state.motion);
        Assert.AreEqual("LocIdle", ((BlendTree)state.motion).children[0].motion.name);
    }

    // ---- tracking control in the replacement locomotion layer ----

    static AnimatorState StateWithTrackingControl()
    {
        var state = new AnimatorState { name = "QuickLand" };
        var control = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        control.trackingHip = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation;
        return state;
    }

    static VRC3CVRCore CoreOnReplacementLocomotionLayer(VRC3CVRCore core)
    {
        typeof(VRC3CVRCore).GetField("processingReplacementLocomotionLayer", Flags).SetValue(core, true);
        return core;
    }

    [Test]
    public void ProcessStateMachine_DropsTrackingControlInTheReplacementLocomotionLayer()
    {
        var state = StateWithTrackingControl();

        RunProcessStateMachine(CoreOnReplacementLocomotionLayer(new VRC3CVRCore()), MachineAround(state));

        Assert.IsEmpty(state.behaviours, "the replacement locomotion layer still adjusts IK weights");
    }

    [Test]
    public void ProcessStateMachine_ConvertsTrackingControlOutsideTheReplacementLocomotionLayer()
    {
        var state = StateWithTrackingControl();

        RunProcessStateMachine(MachineAround(state));

        var body = (BodyControl)state.behaviours.Single();
        var task = body.EnterTasks.Single();
        Assert.AreEqual(BodyControlTask.BodyMask.Pelvis, task.target);
        Assert.AreEqual(0f, task.targetWeight);
    }

    [Test]
    public void ProcessStateMachine_WithConversionOptedIn_ConvertsTrackingControlInTheReplacementLayerToo()
    {
        var state = StateWithTrackingControl();
        var core = CoreOnReplacementLocomotionLayer(new VRC3CVRCore { convertLocomotionTrackingControl = true });

        RunProcessStateMachine(core, MachineAround(state));

        Assert.IsInstanceOf<BodyControl>(state.behaviours.Single());
    }

    // ---- the derived Upright ----

    // Runs the driver's task list the way the client would, so the constants are checked by the
    // numbers they produce rather than by being read back.
    static float RunUprightTasks(System.Collections.Generic.List<ABI.CCK.Components.AnimatorDriverTask> tasks,
        float crouching, float prone, float vrMode, float sensor, int passes = 1)
    {
        var values = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Crouching", crouching }, { "Prone", prone }, { "VRMode", vrMode },
            { "UprightSensor", sensor }, { "#Upright", 0f }, { "#UprightCalc", 0f },
        };
        for (var pass = 0; pass < passes; pass++)
        {
            foreach (var task in tasks)
            {
                var a = values[task.aName];
                var b = task.bType == ABI.CCK.Components.AnimatorDriverTask.SourceType.Static
                    ? task.bValue
                    : values[task.bName];
                values[task.targetName] =
                    task.op == ABI.CCK.Components.AnimatorDriverTask.Operator.Multiplication ? a * b : a + b;
            }
        }
        return values["#Upright"];
    }

    static ABI.CCK.Components.AnimatorDriver BuildUprightFeedLayer(AnimatorController controller)
    {
        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);
        typeof(VRC3CVRCore).GetField("vrcBaseReplacesCckLocomotion", Flags).SetValue(core, true);
        typeof(VRC3CVRCore).GetField("generatedLayerNames", Flags).SetValue(core, new System.Collections.Generic.HashSet<string>());
        typeof(VRC3CVRCore).GetMethod("MakeUprightFeedLayer", Flags).Invoke(core, null);

        var layer = controller.layers.Single(l => l.name.StartsWith("VRC3CVR_Upright"));
        return (ABI.CCK.Components.AnimatorDriver)layer.stateMachine.states.Single().state.behaviours.Single();
    }

    static System.Collections.Generic.List<ABI.CCK.Components.AnimatorDriverTask> BuildUprightFeedLayer()
    {
        var controller = new AnimatorController { name = "uprightFeed" };
        controller.AddParameter("#Upright", AnimatorControllerParameterType.Float);
        var tasks = BuildUprightFeedLayer(controller).EnterTasks;
        Object.DestroyImmediate(controller);
        return tasks;
    }

    [Test]
    public void UprightFeedLayer_OnDesktopDerivesTheStanceValueAndInVrKeepsTheSensor()
    {
        var tasks = BuildUprightFeedLayer();

        // desktop: the discrete value the stance flags describe
        Assert.AreEqual(1.00f, RunUprightTasks(tasks, 0f, 0f, 0f, 0.42f), 1e-4f, "standing");
        Assert.AreEqual(0.55f, RunUprightTasks(tasks, 1f, 0f, 0f, 0.42f), 1e-4f, "crouching");
        Assert.AreEqual(0.20f, RunUprightTasks(tasks, 0f, 1f, 0f, 0.42f), 1e-4f, "prone");
        Assert.AreEqual(0.20f, RunUprightTasks(tasks, 1f, 1f, 0f, 0.42f), 1e-4f, "crouching and prone at once");
        // VR: the sensor, whatever the flags say, so a half-crouch survives
        Assert.AreEqual(0.42f, RunUprightTasks(tasks, 0f, 0f, 1f, 0.42f), 1e-4f, "VR standing");
        Assert.AreEqual(0.42f, RunUprightTasks(tasks, 1f, 0f, 1f, 0.42f), 1e-4f, "VR crouching");
        // the layer reruns every frame onto its own output, so the value has to be a function of the
        // inputs alone -- a second pass that drifted would mean it accumulates
        Assert.AreEqual(0.55f, RunUprightTasks(tasks, 1f, 0f, 0f, 0.42f, 2), 1e-4f, "crouching, run twice");
    }

    [Test]
    public void UprightFeedLayer_ReadsOnlySyncedInputsAndRunsOnRemoteCopies()
    {
        var controller = new AnimatorController { name = "uprightFeed" };
        controller.AddParameter("#Upright", AnimatorControllerParameterType.Float);
        var driver = BuildUprightFeedLayer(controller);
        var read = driver.EnterTasks
            .SelectMany(t => new[] { t.aName, t.bType == ABI.CCK.Components.AnimatorDriverTask.SourceType.Static ? null : t.bName })
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Where(n => n != "#Upright" && n != "#UprightCalc");
        CollectionAssert.AreEquivalent(new[] { "Crouching", "Prone", "VRMode", "UprightSensor" }, read.ToArray());
        Assert.IsFalse(driver.localOnly, "a remote copy has to derive the same value the wearer does");
        Object.DestroyImmediate(controller);
    }

    // ---- the replacement itself, over a whole conversion ----

    const string ReplacementTestFolder = "Assets/VRC3CVR_LocomotionReplacementTest";
    const string GroundStateName = "AvatarGroundLocomotion";
    const string AvatarOwnAnyStateParameter = "AvatarOwnFlag";

    GameObject originalAvatar;
    GameObject convertedAvatar;

    static void EnsureTestFolder()
    {
        if (!AssetDatabase.IsValidFolder(ReplacementTestFolder))
        {
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(ReplacementTestFolder));
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (originalAvatar != null) Object.DestroyImmediate(originalAvatar);
        if (convertedAvatar != null) Object.DestroyImmediate(convertedAvatar);
        originalAvatar = null;
        convertedAvatar = null;
        AssetDatabase.DeleteAsset(ReplacementTestFolder);
    }

    AnimatorController ConvertWithBaseLayer(bool authored, bool convertLocomotionLayer, bool hubless = false)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(ReplacementTestFolder);
        originalAvatar = descriptor.gameObject;

        // not proxy_stand_still: the generated avatar already writes one of those into this folder
        var clip = new AnimationClip { name = authored ? "AuthoredWalk" : "proxy_idle" };
        AssetDatabase.CreateAsset(clip, ReplacementTestFolder + "/" + clip.name + ".anim");
        var baseController = AnimatorController.CreateAnimatorControllerAtPath(ReplacementTestFolder + "/Base.controller");
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

    // Checks the invariant both declined-replacement paths share: the CVR locomotion layer stays
    // and the Base animator is dropped whole, never both.
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
        var controller = ConvertWithBaseLayer(authored: true, convertLocomotionLayer: false);
        AssertCckLocomotionKept(controller);
        // no replacement, so Upright keeps the plain client-fed arrangement
        Assert.IsTrue(controller.parameters.Any(p => p.name == "Upright"));
        Assert.IsFalse(controller.layers.Any(l => l.name.StartsWith("VRC3CVR_Upright")));
        var stream = convertedAvatar.GetComponent<ABI.CCK.Components.CVRParameterStream>();
        CollectionAssert.Contains(
            stream.entries.Select(entry => entry.type + " -> " + entry.parameterName).ToArray(),
            "AvatarUpright -> Upright");
    }

    [Test]
    public void Convert_WithABaseLayerThatHasNoDefaultState_KeepsChilloutVRsOwnLocomotion()
    {
        var controller = ConvertWithBaseLayer(authored: true, convertLocomotionLayer: true, hubless: true);

        AssertCckLocomotionKept(controller);
        Assert.AreEqual(1,
            controller.layers.SelectMany(layer => AllStatesOf(layer.stateMachine))
                .Count(state => state.name == "LocFlying"),
            "a salvaged state was wired in even though the replacement was declined");
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
