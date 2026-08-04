#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

public class VRC3CVRVelocityCompatTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static VRC3CVRCore MakeCore(AnimatorController controller)
    {
        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);
        return core;
    }

    [Test]
    public void WalkParameterNames_RewritesTransitionConditionsAndBlendTreeAxes()
    {
        var controller = new AnimatorController { name = "walkTest" };
        controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityZ", AnimatorControllerParameterType.Float);
        controller.AddLayer("L");

        var layers = controller.layers;
        var machine = layers[0].stateMachine;
        var tree = new BlendTree
        {
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "VelocityX",
            blendParameterY = "VelocityZ",
        };
        var state = machine.AddState("S");
        state.motion = tree;
        var transition = machine.AddAnyStateTransition(state);
        transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "VelocityX");

        var core = MakeCore(controller);
        typeof(VRC3CVRCore).GetMethod("WalkParameterNames", Flags)
            .Invoke(core, new object[] { (System.Func<string, string>)(name => name == "VelocityX" ? "#LocalX" : name) });

        var walked = controller.layers[0].stateMachine;
        Assert.AreEqual("#LocalX", walked.anyStateTransitions[0].conditions[0].parameter);
        var walkedTree = (BlendTree)walked.states[0].state.motion;
        Assert.AreEqual("#LocalX", walkedTree.blendParameter);
        Assert.AreEqual("VelocityZ", walkedTree.blendParameterY, "マッピングに無い名前は触らない");

        // WalkParameterNames must only redirect layer REFERENCES, never the parameter
        // DECLARATIONS (VRC3CVRCore.cs:1296-1299) — VelocityX/VelocityZ are the client-fed inputs
        // the feed layer's derived values are computed from, so losing the declaration here would
        // silently break that feed layer.
        var declaredNames = controller.parameters.Select(p => p.name).ToArray();
        CollectionAssert.Contains(declaredNames, "VelocityX");
        CollectionAssert.Contains(declaredNames, "VelocityZ");

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void LocomotionVelocityLayer_DerivesLocalVelocityFromMovementAndSpeed()
    {
        var controller = new AnimatorController { name = "velocityLocalTest" };
        controller.AddParameter("#VelocityXLocal", AnimatorControllerParameterType.Float);
        controller.AddParameter("#VelocityZLocal", AnimatorControllerParameterType.Float);
        var core = MakeCore(controller);

        typeof(VRC3CVRCore).GetMethod("MakeLocomotionVelocityFeedLayer", Flags).Invoke(core, null);

        var names = controller.parameters.Select(p => p.name).ToArray();
        CollectionAssert.Contains(names, "VelocityX");
        CollectionAssert.Contains(names, "VelocityZ");
        CollectionAssert.Contains(names, "MovementX");
        CollectionAssert.Contains(names, "MovementY");
        CollectionAssert.Contains(names, "#VelocityLocalCalc");

        Assert.AreEqual(1, controller.layers.Length);
        var layer = controller.layers[0];
        Assert.AreEqual(1f, layer.defaultWeight);
        var state = layer.stateMachine.states.Single().state;
        Assert.AreEqual(state, state.transitions.Single().destinationState);
        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.IsFalse(driver.localOnly, "リモートでも MovementX/Y と VelocityX/Z は供給されるので走らせる");
        Assert.AreEqual(
            new[]
            {
                // 地上速度 -> #VelocityLocalCalc
                "Multiplication #VelocityLocalCalc = VelocityX, VelocityX",
                "Multiplication #VelocityXLocal = VelocityZ, VelocityZ",
                "Addition #VelocityLocalCalc = #VelocityLocalCalc, #VelocityXLocal",
                "Power #VelocityLocalCalc = #VelocityLocalCalc, 0.5",
                // 歩き 0.5 / 走り 1.0 のリング -> #VelocityZLocal
                "Multiplication #VelocityZLocal = MovementX, MovementX",
                "Multiplication #VelocityXLocal = MovementY, MovementY",
                "Addition #VelocityZLocal = #VelocityZLocal, #VelocityXLocal",
                "Power #VelocityZLocal = #VelocityZLocal, 0.5",
                // scale = speed / (ring + eps)
                "Addition #VelocityXLocal = #VelocityZLocal, 0.0001",
                "Division #VelocityXLocal = #VelocityLocalCalc, #VelocityXLocal",
                // Z が先。X を埋めると両者が使う scale が消える
                "Multiplication #VelocityZLocal = MovementY, #VelocityXLocal",
                "Multiplication #VelocityXLocal = MovementX, #VelocityXLocal",
            },
            driver.EnterTasks.Select(t =>
                t.op + " " + t.targetName + " = " + t.aName + ", " +
                (t.bType == ABI.CCK.Components.AnimatorDriverTask.SourceType.Static ? t.bValue.ToString("0.####") : t.bName)
            ).ToArray());

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void LocomotionVelocityLayer_SkippedWhenParametersUnused()
    {
        var controller = new AnimatorController { name = "velocityLocalTest" };
        var core = MakeCore(controller);

        typeof(VRC3CVRCore).GetMethod("MakeLocomotionVelocityFeedLayer", Flags).Invoke(core, null);

        Assert.AreEqual(0, controller.layers.Length);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void RemapVelocity_PointsConvertedLayersAtTheDerivedParameters()
    {
        var controller = new AnimatorController { name = "remapTest" };
        controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityY", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityZ", AnimatorControllerParameterType.Float);
        controller.AddLayer("L");
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("S");
        state.motion = new BlendTree
        {
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "VelocityX",
            blendParameterY = "VelocityZ",
        };
        var core = MakeCore(controller);

        typeof(VRC3CVRCore).GetMethod("RemapVelocityToAvatarLocal", Flags).Invoke(core, null);

        var tree = (BlendTree)controller.layers[0].stateMachine.states[0].state.motion;
        Assert.AreEqual("#VelocityXLocal", tree.blendParameter);
        Assert.AreEqual("#VelocityZLocal", tree.blendParameterY);
        // the derived parameters exist and the feed layer was added
        var names = controller.parameters.Select(p => p.name).ToArray();
        CollectionAssert.Contains(names, "#VelocityXLocal");
        CollectionAssert.Contains(names, "#VelocityZLocal");
        Assert.AreEqual(1, controller.layers.Count(l => l.name.StartsWith("VRC3CVR_LocomotionVelocity")));

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void RemapVelocity_LeavesVerticalVelocityAlone()
    {
        var controller = new AnimatorController { name = "remapTest" };
        controller.AddParameter("VelocityY", AnimatorControllerParameterType.Float);
        controller.AddLayer("L");
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("S");
        var transition = machine.AddAnyStateTransition(state);
        transition.AddCondition(AnimatorConditionMode.Less, -0.5f, "VelocityY");
        var core = MakeCore(controller);

        typeof(VRC3CVRCore).GetMethod("RemapVelocityToAvatarLocal", Flags).Invoke(core, null);

        // a yaw-only rotation leaves the vertical axis identical in both spaces
        Assert.AreEqual("VelocityY",
            controller.layers[0].stateMachine.anyStateTransitions[0].conditions[0].parameter);
        Assert.AreEqual(0, controller.layers.Count(l => l.name.StartsWith("VRC3CVR_LocomotionVelocity")),
            "VelocityX/Z を誰も読んでいないので導出層は要らない");

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void RemapVelocity_DoesNotCorruptAnAlreadyGeneratedVelocityMagnitudeLayer()
    {
        var controller = new AnimatorController { name = "remapAfterMagnitudeTest" };
        controller.AddParameter("VelocityMagnitude", AnimatorControllerParameterType.Float);
        var core = MakeCore(controller);

        // Reproduce the exact hazard: a vrc3cvr-generated feed layer with its own literal
        // VelocityX/Y/Z reads already exists by the time the remap runs (this is the actual order
        // Convert() uses — MakeVelocityMagnitudeFeedLayer before RemapVelocityToAvatarLocal). The
        // remap must not rewrite that layer's own reads, nor mistake them for avatar usage.
        typeof(VRC3CVRCore).GetMethod("MakeVelocityMagnitudeFeedLayer", Flags).Invoke(core, null);
        typeof(VRC3CVRCore).GetMethod("RemapVelocityToAvatarLocal", Flags).Invoke(core, null);

        var magnitudeLayer = controller.layers.Single(l => l.name.StartsWith("VRC3CVR_VelocityMagnitude"));
        var driver = (ABI.CCK.Components.AnimatorDriver)magnitudeLayer.stateMachine.states.Single().state.behaviours.Single();
        var referencedNames = driver.EnterTasks.SelectMany(t => new[] { t.aName, t.bName }).ToArray();
        CollectionAssert.Contains(referencedNames, "VelocityX");
        CollectionAssert.Contains(referencedNames, "VelocityZ");
        CollectionAssert.DoesNotContain(referencedNames, "#VelocityXLocal",
            "VelocityMagnitude レイヤー自身の読み取りは書き換えられてはいけない");
        CollectionAssert.DoesNotContain(referencedNames, "#VelocityZLocal",
            "VelocityMagnitude レイヤー自身の読み取りは書き換えられてはいけない");

        // nothing outside the magnitude layer's own generated content references VelocityX/Z, so
        // the remap must not manufacture a "used" signal purely from that layer's own reads
        Assert.AreEqual(0, controller.layers.Count(l => l.name.StartsWith("VRC3CVR_LocomotionVelocity")),
            "VelocityMagnitude の生成レイヤー自身の参照を「使用」と誤検知してはいけない");

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void RemapVelocity_RewritesASourceLayerWhoseNameCollidesWithTheGeneratedPrefix()
    {
        var controller = new AnimatorController { name = "collisionTest" };
        controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityZ", AnimatorControllerParameterType.Float);
        // An original VRC avatar layer can legally be named anything in Unity's Animator window,
        // including something that collides with vrc3cvr's own "VRC3CVR_" generated-layer naming
        // convention. It was never routed through AddGeneratedLayer, so it must still be walked.
        controller.AddLayer("VRC3CVR_MyCoolLayer");
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("S");
        var transition = machine.AddAnyStateTransition(state);
        transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "VelocityX");
        var core = MakeCore(controller);

        typeof(VRC3CVRCore).GetMethod("RemapVelocityToAvatarLocal", Flags).Invoke(core, null);

        Assert.AreEqual("#VelocityXLocal",
            controller.layers[0].stateMachine.anyStateTransitions[0].conditions[0].parameter,
            "生成レイヤーの命名規則と偶然衝突しただけの元アバターレイヤーも書き換え対象");
        Assert.AreEqual(1, controller.layers.Count(l => l.name.StartsWith("VRC3CVR_LocomotionVelocity")));

        Object.DestroyImmediate(controller);
    }
}
#endif
