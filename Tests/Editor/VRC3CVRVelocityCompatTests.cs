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
}
#endif
