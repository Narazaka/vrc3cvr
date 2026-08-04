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
}
#endif
