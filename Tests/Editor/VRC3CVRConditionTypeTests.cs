#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;

// Unit tests for VRC3CVRConditionTypes.TryAdapt, plus one end-to-end test proving the pipeline
// actually rewrites a transition condition whose parameter changed type during conversion --
// and does so early enough that Unity's own AnimatorController never logs the resulting Console
// error even transiently.
//
// The scenario this whole file exists for: a VRChat avatar drives something off IsLocal through a
// blend tree, which forces IsLocal to be declared Float (blend parameters must be Float). Every
// transition condition written against IsLocal elsewhere in the avatar -- typically an If/IfNot,
// since IsLocal reads as a bool everywhere else -- is still valid on VRChat (VRChat lets bool-typed
// conditions target a Float parameter) but is rejected by Unity's own AnimatorController once
// copied over, as "uses parameter 'IsLocal' which is not compatible with condition type". See
// VRC3CVRConditionTypes.cs for why adapting the condition (rather than forcing the parameter back
// to Bool) is correct: ChilloutVR's CVRAnimatorManager type-casts on send regardless.
//
// ProcessTransition now performs this adaptation itself, at the point each condition is generated,
// using chilloutAnimatorController's already-merged parameter types (see VRC3CVRCore.cs). The
// end-of-conversion AdaptTransitionConditionTypesToParameterTypes pass still exists as a safety net
// for anything that reaches chilloutAnimatorController by some other route, but for this scenario it
// should now have nothing left to do.
public class VRC3CVRConditionTypeTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static AnimatorCondition Cond(AnimatorConditionMode mode, float threshold) =>
        new AnimatorCondition { parameter = "P", mode = mode, threshold = threshold };

    // ---- Bool target ----

    [Test]
    public void Bool_If_StaysIf()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.If, 1f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.If, adapted.mode);
    }

    [Test]
    public void Bool_IfNot_StaysIfNot()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.IfNot, 1f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.IfNot, adapted.mode);
    }

    [Test]
    public void Bool_GreaterHalf_BecomesIf()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Greater, 0.5f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.If, adapted.mode);
        Assert.AreEqual(0f, adapted.threshold);
    }

    [Test]
    public void Bool_LessHalf_BecomesIfNot()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Less, 0.5f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.IfNot, adapted.mode);
        Assert.AreEqual(0f, adapted.threshold);
    }

    [Test]
    public void Bool_EqualsOne_BecomesIf()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 1f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.If, adapted.mode);
    }

    [Test]
    public void Bool_EqualsZero_BecomesIfNot()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 0f), AnimatorControllerParameterType.Bool, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.IfNot, adapted.mode);
    }

    [Test]
    public void Bool_EqualsOtherValue_HasNoEquivalent()
    {
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 3f), AnimatorControllerParameterType.Bool, out _));
    }

    [Test]
    public void Bool_GreaterAtOrAboveOne_HasNoEquivalent()
    {
        // ">= 1" is never true for a bool cast to 0/1, so there is no matching If/IfNot.
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Greater, 1f), AnimatorControllerParameterType.Bool, out _));
    }

    [Test]
    public void Bool_LessAtOrBelowZero_HasNoEquivalent()
    {
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Less, 0f), AnimatorControllerParameterType.Bool, out _));
    }

    // ---- Float target ----

    [Test]
    public void Float_If_BecomesGreaterHalf()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.If, 1f), AnimatorControllerParameterType.Float, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.Greater, adapted.mode);
        Assert.AreEqual(0.5f, adapted.threshold);
    }

    [Test]
    public void Float_IfNot_BecomesLessHalf()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.IfNot, 1f), AnimatorControllerParameterType.Float, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.Less, adapted.mode);
        Assert.AreEqual(0.5f, adapted.threshold);
    }

    [Test]
    public void Float_GreaterAndLess_PassThroughUnchanged()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Greater, 0.3f), AnimatorControllerParameterType.Float, out var greaterAdapted));
        Assert.AreEqual(AnimatorConditionMode.Greater, greaterAdapted.mode);
        Assert.AreEqual(0.3f, greaterAdapted.threshold);

        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Less, 0.7f), AnimatorControllerParameterType.Float, out var lessAdapted));
        Assert.AreEqual(AnimatorConditionMode.Less, lessAdapted.mode);
        Assert.AreEqual(0.7f, lessAdapted.threshold);
    }

    [Test]
    public void Float_EqualsOrNotEqual_HasNoEquivalent()
    {
        // Exact float equality has no single-condition equivalent to fall back to.
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 3f), AnimatorControllerParameterType.Float, out _));
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.NotEqual, 3f), AnimatorControllerParameterType.Float, out _));
    }

    // ---- Int target ----

    [Test]
    public void Int_If_BecomesGreaterZero()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.If, 1f), AnimatorControllerParameterType.Int, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.Greater, adapted.mode);
        Assert.AreEqual(0f, adapted.threshold);
    }

    [Test]
    public void Int_IfNot_BecomesLessOne()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.IfNot, 1f), AnimatorControllerParameterType.Int, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.Less, adapted.mode);
        Assert.AreEqual(1f, adapted.threshold);
    }

    [Test]
    public void Int_Equals_PassesThroughUnchanged()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 3f), AnimatorControllerParameterType.Int, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.Equals, adapted.mode);
        Assert.AreEqual(3f, adapted.threshold);
    }

    [Test]
    public void Int_NotEqual_PassesThroughUnchanged()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.NotEqual, 3f), AnimatorControllerParameterType.Int, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.NotEqual, adapted.mode);
    }

    // ---- Trigger target ----

    [Test]
    public void Trigger_If_Succeeds()
    {
        Assert.IsTrue(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.If, 1f), AnimatorControllerParameterType.Trigger, out var adapted));
        Assert.AreEqual(AnimatorConditionMode.If, adapted.mode);
    }

    [Test]
    public void Trigger_IfNot_HasNoEquivalent()
    {
        // A trigger cannot express "has not fired".
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.IfNot, 1f), AnimatorControllerParameterType.Trigger, out _));
    }

    [Test]
    public void Trigger_GreaterOrEquals_HasNoEquivalent()
    {
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Greater, 0.5f), AnimatorControllerParameterType.Trigger, out _));
        Assert.IsFalse(VRC3CVRConditionTypes.TryAdapt(Cond(AnimatorConditionMode.Equals, 1f), AnimatorControllerParameterType.Trigger, out _));
    }

    // ---- End-to-end: the whole conversion pipeline actually applies the adaptation ----

    const string TestFolder = "Assets/VRC3CVR_ConditionTypeTest";

    [Test]
    public void ConvertVerificationAvatar_FloatIsLocalIfCondition_BecomesGreaterHalf()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        var original = descriptor.gameObject;
        GameObject converted = null;
        try
        {
            var gestureLayer = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.Gesture);
            var gestureController = (AnimatorController)gestureLayer.animatorController;

            // A blend tree is what normally forces IsLocal to be declared Float on a real avatar
            // (see the class comment); the type is what matters here, so declare it directly.
            gestureController.AddParameter("IsLocal", AnimatorControllerParameterType.Float);
            var stateMachine = gestureController.layers[0].stateMachine;
            var neutral = stateMachine.states.Single(s => s.state.name == "Neutral").state;
            var fist = stateMachine.states.Single(s => s.state.name == "Fist").state;
            var transition = neutral.AddTransition(fist);
            transition.hasExitTime = false;
            transition.duration = 0f;
            // Written as if IsLocal were still Bool -- this is what breaks without the fix.
            transition.AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");

            var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
            {
                vrcAvatarDescriptor = descriptor,
                shouldCloneAvatar = true,
                saveAssets = false,
            });

            // No LogAssert.ignoreFailingMessages here, deliberately: Unity Test Framework fails a
            // test on any unhandled LogType.Error, and Unity's own AnimatorController logs exactly
            // that (the Console error from the bug report) the instant a live controller has a
            // condition that is not compatible with its parameter's declared type. ProcessTransition
            // now adapts every condition it generates against chilloutAnimatorController's
            // already-merged parameter types (see VRC3CVRCore.cs) before it is ever written onto a
            // transition, so that combination should never exist on a live controller in the first
            // place. If this call logs an error, the test fails right here -- that failure *is* the
            // regression check for the original bug report.
            core.Convert();
            converted = core.chilloutAvatar;
            Assert.IsNotNull(converted);

            // The end-of-conversion safety net (AdaptTransitionConditionTypesToParameterTypes)
            // should have had nothing left to adapt or drop: everything reachable through
            // ProcessTransition -- which this scenario's Neutral -> Fist transition is -- gets fixed
            // at generation time now, not as an afterthought.
            var adaptedCount = (int)typeof(VRC3CVRCore).GetField("lastAdaptedConditionCount", Flags).GetValue(core);
            var droppedCount = (int)typeof(VRC3CVRCore).GetField("lastDroppedConditionCount", Flags).GetValue(core);
            Assert.AreEqual(0, adaptedCount, "ProcessTransition should have already adapted this condition when it was generated, leaving nothing for the end-of-conversion pass to do");
            Assert.AreEqual(0, droppedCount, "nothing in this scenario should be unrecoverable");

            var cvrAvatar = converted.GetComponent<CVRAvatar>();
            var controller = (AnimatorController)cvrAvatar.avatarSettings.baseController;

            var isLocalParameter = controller.parameters.Single(p => p.name == "IsLocal");
            Assert.AreEqual(AnimatorControllerParameterType.Float, isLocalParameter.type,
                "the merge keeps whichever declaration it saw first -- Float here -- which is the whole premise of this test");

            var convertedNeutral = controller.layers
                .SelectMany(layer => layer.stateMachine.states)
                .Select(s => s.state)
                .Single(s => s.name == "Neutral");
            var convertedCondition = convertedNeutral.transitions
                .Single(t => t.destinationState != null && t.destinationState.name == "Fist")
                .conditions.Single();

            Assert.AreEqual(AnimatorConditionMode.Greater, convertedCondition.mode);
            Assert.AreEqual(0.5f, convertedCondition.threshold);
        }
        finally
        {
            Object.DestroyImmediate(original);
            if (converted != null) Object.DestroyImmediate(converted);
            AssetDatabase.DeleteAsset(TestFolder);
        }
    }
}
#endif
