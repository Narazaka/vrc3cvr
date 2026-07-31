#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

// Unit tests for VRC3CVRCore.EnsureLocalOnlyContacts: the generated layer that disables
// local-only VRC Contacts on remote copies of the avatar, branching on an IsLocal parameter.
//
// The avatar may already declare IsLocal itself before this method runs -- CopyParametersTo is a
// first-declaration-wins merge, and a blend tree's blend parameter has to be a Float, so an
// avatar that drives anything off IsLocal through a blend tree declares it Float, not Bool. The
// condition mode this layer emits has to match whatever type is actually there: emitting
// If/IfNot (Bool-only) against a Float parameter produces "uses parameter 'IsLocal' which is not
// compatible with condition type" and a dead layer. See VRC3CVRGestureConversionTests for why
// these tests live in Assembly-CSharp-Editor and reach private members through reflection.
public class VRC3CVRLocalOnlyContactsTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static VRC3CVRCore MakeCore(AnimatorController controller)
    {
        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);
        // A non-empty local-only path is what makes EnsureLocalOnlyContacts do anything at all --
        // the layer only exists to disable local-only VRC Contacts remotely.
        typeof(VRC3CVRCore).GetField("localPointerPaths", Flags).SetValue(core, new HashSet<string>());
        typeof(VRC3CVRCore).GetField("localTriggerPaths", Flags).SetValue(core, new HashSet<string> { "SomeLocalOnlyReceiver" });
        return core;
    }

    static void Invoke(VRC3CVRCore core)
    {
        typeof(VRC3CVRCore).GetMethod("EnsureLocalOnlyContacts", Flags).Invoke(core, null);
    }

    static AnimatorControllerLayer FindLayer(AnimatorController controller) =>
        controller.layers.SingleOrDefault(l => l.name == "VRC3CVR_LocalOnlyContacts");

    static AnimatorStateTransition[] IdleTransitions(AnimatorController controller)
    {
        var layer = FindLayer(controller);
        Assert.IsNotNull(layer, "expected the VRC3CVR_LocalOnlyContacts layer to have been created");
        var idle = layer.stateMachine.states.Single(s => s.state.name == "Idle").state;
        return idle.transitions;
    }

    static void AssertModes(AnimatorController controller, AnimatorConditionMode localMode, float localThreshold, AnimatorConditionMode remoteMode, float remoteThreshold)
    {
        var transitions = IdleTransitions(controller);
        Assert.AreEqual(2, transitions.Length);
        var toLocal = transitions.Single(t => t.destinationState.name == "Local").conditions.Single();
        var toRemote = transitions.Single(t => t.destinationState.name == "Remote").conditions.Single();
        Assert.AreEqual(localMode, toLocal.mode, "condition mode for the Idle -> Local transition");
        Assert.AreEqual(localThreshold, toLocal.threshold, "threshold for the Idle -> Local transition");
        Assert.AreEqual(remoteMode, toRemote.mode, "condition mode for the Idle -> Remote transition");
        Assert.AreEqual(remoteThreshold, toRemote.threshold, "threshold for the Idle -> Remote transition");
    }

    [Test]
    public void FloatIsLocal_UsesGreaterLessInsteadOfIfIfNot()
    {
        var controller = new AnimatorController { name = "floatIsLocal" };
        controller.AddParameter("IsLocal", AnimatorControllerParameterType.Float);
        var core = MakeCore(controller);

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "VRC3CVR: the avatar declares IsLocal as " + AnimatorControllerParameterType.Float
                + " rather than Bool, so the local-only contact layer compares it numerically. "
                + "Check that ChilloutVR actually drives IsLocal in that type on your avatar.")));

        Invoke(core);

        AssertModes(controller, AnimatorConditionMode.Greater, 0.5f, AnimatorConditionMode.Less, 0.5f);
        Assert.AreEqual(1, controller.parameters.Count(p => p.name == "IsLocal"), "must not add a second IsLocal parameter");

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void MissingIsLocal_IsAddedAsBoolAndUsesIfIfNot()
    {
        var controller = new AnimatorController { name = "missingIsLocal" };
        var core = MakeCore(controller);

        Invoke(core);

        var declared = controller.parameters.Single(p => p.name == "IsLocal");
        Assert.AreEqual(AnimatorControllerParameterType.Bool, declared.type);
        AssertModes(controller, AnimatorConditionMode.If, 1f, AnimatorConditionMode.IfNot, 1f);

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void BoolIsLocal_UsesIfIfNot()
    {
        var controller = new AnimatorController { name = "boolIsLocal" };
        controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
        var core = MakeCore(controller);

        Invoke(core);

        Assert.AreEqual(1, controller.parameters.Count(p => p.name == "IsLocal"), "must not add a second IsLocal parameter");
        AssertModes(controller, AnimatorConditionMode.If, 1f, AnimatorConditionMode.IfNot, 1f);

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void IntIsLocal_UsesGreaterZeroAndLessOne()
    {
        var controller = new AnimatorController { name = "intIsLocal" };
        controller.AddParameter("IsLocal", AnimatorControllerParameterType.Int);
        var core = MakeCore(controller);

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "VRC3CVR: the avatar declares IsLocal as " + AnimatorControllerParameterType.Int
                + " rather than Bool, so the local-only contact layer compares it numerically. "
                + "Check that ChilloutVR actually drives IsLocal in that type on your avatar.")));

        Invoke(core);

        AssertModes(controller, AnimatorConditionMode.Greater, 0f, AnimatorConditionMode.Less, 1f);

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void TriggerIsLocal_CannotExpressNotFiredSoLayerIsSkipped()
    {
        var controller = new AnimatorController { name = "triggerIsLocal" };
        controller.AddParameter("IsLocal", AnimatorControllerParameterType.Trigger);
        var core = MakeCore(controller);

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "VRC3CVR: the avatar declares IsLocal as " + AnimatorControllerParameterType.Trigger
                + ", which cannot drive the local-only contact layer. "
                + "Local-only contacts will stay enabled on remote copies.")));

        Invoke(core);

        Assert.IsNull(FindLayer(controller), "a Trigger IsLocal cannot drive the layer, so no layer should be added");

        Object.DestroyImmediate(controller);
    }
}
#endif
