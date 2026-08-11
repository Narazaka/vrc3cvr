#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The conversion code lives in the predefined Assembly-CSharp-Editor (no asmdef, because the CCK
// has none either), so these tests live there too and reach private members via reflection.
public class VRC3CVRGestureConversionTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static VRC3CVRCore MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode mode)
    {
        return new VRC3CVRCore { gestureWeightConversionMode = mode };
    }

    static AnimatorStateTransition[] ProcessTransitions(VRC3CVRCore core, params AnimatorCondition[] conditions)
    {
        var method = typeof(VRC3CVRCore).GetMethods(Flags)
            .First(m => m.Name == "ProcessTransitions" && !m.IsGenericMethod &&
                        m.GetParameters()[0].ParameterType == typeof(AnimatorStateTransition[]));
        var transition = new AnimatorStateTransition { destinationState = new AnimatorState { name = "dest" } };
        foreach (var condition in conditions)
        {
            transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }
        // layerName/context only feed a warning message when a condition has to be dropped (see
        // VRC3CVRCore.ProcessTransition); these tests don't exercise that path, so placeholders are fine.
        // MakeCore below never sets chilloutAnimatorController, so no parameter type is known for
        // anything here -- every condition these tests write is expected to pass through unchanged
        // except for the Gesture/GestureWeight-specific rewriting under test.
        return (AnimatorStateTransition[])method.Invoke(core, new object[] { new[] { transition }, "TestLayer", "TestContext" });
    }

    static AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold)
    {
        return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
    }

    static string[] Render(AnimatorStateTransition[] transitions)
    {
        return transitions.Select(t =>
            string.Join(" AND ", t.conditions.Select(c => c.parameter + " " + c.mode + " " + c.threshold.ToString("0.###")))
        ).ToArray();
    }

    // ---- Fold mode: standalone weight conditions ----

    [Test]
    public void Fold_StandaloneWeightGreater_ExpandsToFistAndFixedOneGestures()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core,
            Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f),
            Cond("SomeBool", AnimatorConditionMode.If, 0f));
        Assert.AreEqual(new[]
        {
            "GestureLeft Less 1.1 AND GestureLeft Greater 0.5 AND SomeBool If 0",
            "SomeBool If 0 AND GestureLeft Less -0.9",
            "SomeBool If 0 AND GestureLeft Greater 1.9",
        }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightGreaterZero_FloorsAtFistBoundary()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureRightWeight", AnimatorConditionMode.Greater, 0f));
        Assert.AreEqual(new[]
        {
            "GestureRight Less 1.1 AND GestureRight Greater 0.01",
            "GestureRight Less -0.9",
            "GestureRight Greater 1.9",
        }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightLess_CoversNeutralAndFistInOneRange()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureLeftWeight", AnimatorConditionMode.Less, 0.5f));
        Assert.AreEqual(new[] { "GestureLeft Greater -0.1 AND GestureLeft Less 0.5" }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightGreaterNegative_AlwaysTrueDropsCondition()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core,
            Cond("GestureLeftWeight", AnimatorConditionMode.Greater, -1f),
            Cond("SomeBool", AnimatorConditionMode.If, 0f));
        Assert.AreEqual(new[] { "SomeBool If 0" }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightGreaterOne_NeverTrueKeepsTransitionUnreachable()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 1f));
        Assert.AreEqual(new[] { "GestureLeft Greater 9999" }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightLessAboveOne_AlwaysTrueDropsCondition()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core,
            Cond("GestureLeftWeight", AnimatorConditionMode.Less, 1.5f),
            Cond("SomeBool", AnimatorConditionMode.If, 0f));
        Assert.AreEqual(new[] { "SomeBool If 0" }, Render(result));
    }

    [Test]
    public void Fold_StandaloneWeightLessZero_NeverTrueKeepsTransitionUnreachable()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureLeftWeight", AnimatorConditionMode.Less, 0f));
        Assert.AreEqual(new[] { "GestureLeft Greater 9999" }, Render(result));
    }

    [Test]
    public void Fold_WeightPairedWithFist_FoldsIntoThreshold()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core,
            Cond("GestureLeft", AnimatorConditionMode.Equals, 1f),
            Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f));
        Assert.AreEqual(new[] { "GestureLeft Less 1.1 AND GestureLeft Greater 0.5" }, Render(result));
    }

    // ---- Derived mode: weight conditions survive ----

    [Test]
    public void Derived_StandaloneWeightCondition_IsKept()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.DerivedParameter);
        var result = ProcessTransitions(core,
            Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f),
            Cond("SomeBool", AnimatorConditionMode.If, 0f));
        Assert.AreEqual(new[] { "GestureLeftWeight Greater 0.5 AND SomeBool If 0" }, Render(result));
    }

    [Test]
    public void Derived_WeightPairedWithFist_KeepsWeightConditionAndDefaultFistBand()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.DerivedParameter);
        var result = ProcessTransitions(core,
            Cond("GestureLeft", AnimatorConditionMode.Equals, 1f),
            Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f));
        Assert.AreEqual(new[] { "GestureLeft Less 1.1 AND GestureLeft Greater 0.01 AND GestureLeftWeight Greater 0.5" }, Render(result));
    }

    // ---- Gesture number conversion (Greater/Less expansion) ----

    [Test]
    public void GestureGreater_ExpandsToOneTransitionPerMatchingGesture()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        // VRC gestures {4,5,6,7} map to CVR {5,6,3,2}
        var result = ProcessTransitions(core, Cond("GestureLeft", AnimatorConditionMode.Greater, 3f));
        Assert.AreEqual(new[]
        {
            "GestureLeft Less 5.1 AND GestureLeft Greater 4.9",
            "GestureLeft Less 6.1 AND GestureLeft Greater 5.9",
            "GestureLeft Less 3.1 AND GestureLeft Greater 2.9",
            "GestureLeft Less 2.1 AND GestureLeft Greater 1.9",
        }, Render(result));
    }

    [Test]
    public void GestureLessZero_NeverTrueKeepsTransitionUnreachable()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureLeft", AnimatorConditionMode.Less, 0f));
        Assert.AreEqual(new[] { "GestureLeft Greater 9999" }, Render(result));
    }

    [Test]
    public void GestureNotEqualZero_SplitsIntoTwoTransitions()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var result = ProcessTransitions(core, Cond("GestureLeft", AnimatorConditionMode.NotEqual, 0f));
        Assert.AreEqual(new[]
        {
            "GestureLeft Less -0.1",
            "GestureLeft Greater 0.01",
        }, Render(result));
    }

    [Test]
    public void GestureEquals_ConvertsToBand()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        // VRC peace (4) maps to CVR 5
        var result = ProcessTransitions(core, Cond("GestureLeft", AnimatorConditionMode.Equals, 4f));
        Assert.AreEqual(new[] { "GestureLeft Less 5.1 AND GestureLeft Greater 4.9" }, Render(result));
    }

    // ---- Fold mode: weight-driven blend trees ----

    [Test]
    public void FoldBlendTree_RedrivesByGestureAndAddsFixedOneBoundaries()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var clipA = new AnimationClip { name = "A" };
        var clipC = new AnimationClip { name = "C" };
        var tree = new BlendTree
        {
            blendType = BlendTreeType.Simple1D,
            blendParameter = "GestureLeftWeight",
            useAutomaticThresholds = false,
        };
        var children = tree.children;
        ArrayUtility.Add(ref children, new ChildMotion { motion = clipA, threshold = 0f, timeScale = 1f });
        ArrayUtility.Add(ref children, new ChildMotion { motion = clipC, threshold = 1f, timeScale = 1f });
        tree.children = children;

        typeof(VRC3CVRCore).GetMethod("FoldGestureWeightOnBlendTree", Flags).Invoke(core, new object[] { tree });

        Assert.AreEqual("GestureLeft", tree.blendParameter);
        Assert.IsFalse(tree.useAutomaticThresholds);
        Assert.AreEqual(
            new[] { "C@-1", "A@0", "C@1", "C@2" },
            tree.children.Select(ch => ch.motion.name + "@" + ch.threshold.ToString("0.###")).ToArray());
    }

    [Test]
    public void FoldBlendTree_ProcessesNestedTrees()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var clipA = new AnimationClip { name = "A" };
        var clipC = new AnimationClip { name = "C" };
        var inner = new BlendTree
        {
            blendType = BlendTreeType.Simple1D,
            blendParameter = "GestureRightWeight",
            useAutomaticThresholds = false,
        };
        var innerChildren = inner.children;
        ArrayUtility.Add(ref innerChildren, new ChildMotion { motion = clipA, threshold = 0f, timeScale = 1f });
        ArrayUtility.Add(ref innerChildren, new ChildMotion { motion = clipC, threshold = 1f, timeScale = 1f });
        inner.children = innerChildren;

        var outer = new BlendTree
        {
            blendType = BlendTreeType.Simple1D,
            blendParameter = "SomeFloat",
            useAutomaticThresholds = false,
        };
        var outerChildren = outer.children;
        ArrayUtility.Add(ref outerChildren, new ChildMotion { motion = inner, threshold = 0f, timeScale = 1f });
        outer.children = outerChildren;

        typeof(VRC3CVRCore).GetMethod("FoldGestureWeightOnBlendTree", Flags).Invoke(core, new object[] { outer });

        Assert.AreEqual("SomeFloat", outer.blendParameter);
        Assert.AreEqual("GestureRight", inner.blendParameter);
        Assert.AreEqual(4, inner.children.Length);
    }

    // ---- Derived mode: generated weight feed layer ----

    [Test]
    public void FeedLayer_GeneratesParameterCurveBlendTree()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.DerivedParameter);
        var controller = new AnimatorController { name = "feedTest" };
        controller.AddParameter("#GestureLeftWeight", AnimatorControllerParameterType.Float);
        controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Float);
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);

        typeof(VRC3CVRCore).GetMethod("MakeGestureWeightFeedLayers", Flags).Invoke(core, null);

        Assert.AreEqual(1, controller.layers.Length);
        var layer = controller.layers[0];
        Assert.AreEqual(1f, layer.defaultWeight);
        var tree = (BlendTree)layer.stateMachine.states.Single().state.motion;
        Assert.AreEqual("GestureLeft", tree.blendParameter);
        Assert.AreEqual(
            new[] { "@-1:#GestureLeftWeight=1", "@0:#GestureLeftWeight=0", "@1:#GestureLeftWeight=1", "@2:#GestureLeftWeight=1" },
            tree.children.Select(ch =>
            {
                var clip = (AnimationClip)ch.motion;
                var binding = AnimationUtility.GetCurveBindings(clip).Single();
                var value = AnimationUtility.GetEditorCurve(clip, binding).keys[0].value;
                return "@" + ch.threshold.ToString("0.###") + ":" + binding.propertyName + "=" + value;
            }).ToArray());

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void FeedLayer_SkippedWhenWeightParameterUnused()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.DerivedParameter);
        var controller = new AnimatorController { name = "feedTest" };
        controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Float);
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);

        typeof(VRC3CVRCore).GetMethod("MakeGestureWeightFeedLayers", Flags).Invoke(core, null);

        Assert.AreEqual(0, controller.layers.Length);
        Object.DestroyImmediate(controller);
    }

    // ---- VelocityMagnitude recomputation layer ----

    [Test]
    public void VelocityMagnitudeLayer_RecomputesFromVelocityComponents()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var controller = new AnimatorController { name = "velocityTest" };
        controller.AddParameter("#VelocityMagnitude", AnimatorControllerParameterType.Float);
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);

        typeof(VRC3CVRCore).GetMethod("MakeVelocityMagnitudeFeedLayer", Flags).Invoke(core, null);

        // VelocityX/Y/Z are declared on demand, plus the scratch parameter
        var parameterNames = controller.parameters.Select(p => p.name).ToArray();
        CollectionAssert.Contains(parameterNames, "VelocityX");
        CollectionAssert.Contains(parameterNames, "VelocityY");
        CollectionAssert.Contains(parameterNames, "VelocityZ");
        CollectionAssert.Contains(parameterNames, "#VelocityMagnitudeCalc");

        Assert.AreEqual(1, controller.layers.Length);
        var layer = controller.layers[0];
        Assert.AreEqual(1f, layer.defaultWeight);
        var state = layer.stateMachine.states.Single().state;
        // self transition keeps the driver ticking
        Assert.AreEqual(state, state.transitions.Single().destinationState);
        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.IsFalse(driver.localOnly);
        Assert.AreEqual(
            new[]
            {
                "Multiplication #VelocityMagnitudeCalc = VelocityX, VelocityX",
                "Multiplication #VelocityMagnitude = VelocityY, VelocityY",
                "Addition #VelocityMagnitudeCalc = #VelocityMagnitudeCalc, #VelocityMagnitude",
                "Multiplication #VelocityMagnitude = VelocityZ, VelocityZ",
                "Addition #VelocityMagnitudeCalc = #VelocityMagnitudeCalc, #VelocityMagnitude",
                "Power #VelocityMagnitude = #VelocityMagnitudeCalc, 0.5",
            },
            driver.EnterTasks.Select(t =>
                t.op + " " + t.targetName + " = " + t.aName + ", " +
                (t.bType == ABI.CCK.Components.AnimatorDriverTask.SourceType.Static ? t.bValue.ToString("0.#") : t.bName)
            ).ToArray());

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void VelocityMagnitudeLayer_SkippedWhenParameterUnused()
    {
        var core = MakeCore(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var controller = new AnimatorController { name = "velocityTest" };
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);

        typeof(VRC3CVRCore).GetMethod("MakeVelocityMagnitudeFeedLayer", Flags).Invoke(core, null);

        Assert.AreEqual(0, controller.layers.Length);
        Object.DestroyImmediate(controller);
    }

    // ---- Game state parameter streams (MuteSelf / VRMode / Upright) ----

    static VRC3CVRCore MakeStreamCore(AnimatorController controller, GameObject avatar, bool feed = true)
    {
        var core = new VRC3CVRCore { feedGameStateParameters = feed };
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);
        typeof(VRC3CVRCore).GetField("chilloutAvatarGameObject", Flags).SetValue(core, avatar);
        return core;
    }

    [Test]
    public void GameStateStream_GeneratesEntriesForDeclaredParameters()
    {
        AnimatorController controller = null;
        GameObject avatar = null;
        try
        {
            controller = new AnimatorController { name = "streamTest" };
            avatar = new GameObject("StreamTestAvatar");
            controller.AddParameter("MuteSelf", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Upright", AnimatorControllerParameterType.Float);
            var core = MakeStreamCore(controller, avatar);

            typeof(VRC3CVRCore).GetMethod("MakeGameStateParameterStreams", Flags).Invoke(core, null);

            var stream = avatar.GetComponent<ABI.CCK.Components.CVRParameterStream>();
            Assert.IsNotNull(stream);
            Assert.AreEqual(ABI.CCK.Components.CVRParameterStream.ReferenceType.Avatar, stream.referenceType);
            // VRMode is not declared, so only the two declared parameters get entries
            Assert.AreEqual(
                new[]
                {
                    "LocalPlayerMuted -> MuteSelf (AvatarAnimator, Override)",
                    "AvatarUpright -> Upright (AvatarAnimator, Override)",
                },
                stream.entries.Select(e => e.type + " -> " + e.parameterName + " (" + e.targetType + ", " + e.applicationType + ")").ToArray());
        }
        finally
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            if (controller != null) Object.DestroyImmediate(controller);
        }
    }

    [Test]
    public void GameStateStream_IsIdempotentAndKeepsForeignEntries()
    {
        AnimatorController controller = null;
        GameObject avatar = null;
        try
        {
            controller = new AnimatorController { name = "streamTest" };
            avatar = new GameObject("StreamTestAvatar");
            controller.AddParameter("VRMode", AnimatorControllerParameterType.Int);
            var core = MakeStreamCore(controller, avatar);

            // a pre-existing entry of an unrelated type must survive
            var existingStream = avatar.AddComponent<ABI.CCK.Components.CVRParameterStream>();
            existingStream.entries.Add(new ABI.CCK.Components.CVRParameterStreamEntry
            {
                type = ABI.CCK.Components.CVRParameterStreamEntry.Type.TimeSeconds,
                parameterName = "UserParam",
            });

            var method = typeof(VRC3CVRCore).GetMethod("MakeGameStateParameterStreams", Flags);
            method.Invoke(core, null);
            method.Invoke(core, null);

            var stream = avatar.GetComponent<ABI.CCK.Components.CVRParameterStream>();
            Assert.AreEqual(
                new[] { "TimeSeconds -> UserParam", "DeviceMode -> VRMode" },
                stream.entries.Select(e => e.type + " -> " + e.parameterName).ToArray());
        }
        finally
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            if (controller != null) Object.DestroyImmediate(controller);
        }
    }

    [Test]
    public void GameStateStream_SkippedWhenDisabledOrUnused()
    {
        AnimatorController controller = null;
        GameObject avatarDisabled = null;
        AnimatorController controllerUnused = null;
        GameObject avatarUnused = null;
        try
        {
            controller = new AnimatorController { name = "streamTest" };
            avatarDisabled = new GameObject("StreamTestAvatarDisabled");
            controller.AddParameter("MuteSelf", AnimatorControllerParameterType.Bool);
            var coreDisabled = MakeStreamCore(controller, avatarDisabled, feed: false);
            var method = typeof(VRC3CVRCore).GetMethod("MakeGameStateParameterStreams", Flags);

            method.Invoke(coreDisabled, null);
            Assert.IsNull(avatarDisabled.GetComponent<ABI.CCK.Components.CVRParameterStream>());

            controllerUnused = new AnimatorController { name = "streamTestUnused" };
            avatarUnused = new GameObject("StreamTestAvatarUnused");
            var coreUnused = MakeStreamCore(controllerUnused, avatarUnused);

            method.Invoke(coreUnused, null);
            Assert.IsNull(avatarUnused.GetComponent<ABI.CCK.Components.CVRParameterStream>());
        }
        finally
        {
            if (avatarDisabled != null) Object.DestroyImmediate(avatarDisabled);
            if (avatarUnused != null) Object.DestroyImmediate(avatarUnused);
            if (controller != null) Object.DestroyImmediate(controller);
            if (controllerUnused != null) Object.DestroyImmediate(controllerUnused);
        }
    }
}
#endif
