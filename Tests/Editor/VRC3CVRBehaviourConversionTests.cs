#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

// Unit tests for the state-machine-behaviour conversion done inside VRC3CVRCore.ProcessStateMachine
// (VRCAvatarParameterDriver -> AnimatorDriver, VRCAnimatorTrackingControl/VRCAnimatorLocomotionControl
// -> BodyControl, VRC behaviour stripping) plus the avatar-mask helpers used while merging layers.
// These call the target private methods directly through reflection with minimal hand-built
// AnimatorState/AnimatorStateMachine objects instead of converting a whole avatar (see
// VRC3CVREndToEndTests for that). See VRC3CVRGestureConversionTests for why these live in
// Assembly-CSharp-Editor.
public class VRC3CVRBehaviourConversionTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags StaticFlags = BindingFlags.NonPublic | BindingFlags.Static;

    // ---- reflection helpers ----

    static (AnimatorStateMachine machine, AnimatorState state) MakeSingleStateMachine(string stateName = "State")
    {
        var state = new AnimatorState { name = stateName };
        var machine = new AnimatorStateMachine();
        machine.states = new[] { new ChildAnimatorState { state = state } };
        return (machine, state);
    }

    static AnimatorControllerParameter Param(string name, AnimatorControllerParameterType type) =>
        new AnimatorControllerParameter { name = name, type = type };

    // Runs ProcessStateMachine and returns the (possibly grown, e.g. by a Random(Bool) scratch
    // parameter) parameter array threaded through the `ref` argument. layerName only feeds the
    // condition-type-adaptation warning message (see VRC3CVRCore.ProcessTransition), so a fixed
    // placeholder is fine here -- none of these tests assert on it.
    static AnimatorControllerParameter[] RunProcessStateMachine(VRC3CVRCore core, AnimatorStateMachine machine, AnimatorControllerParameter[] parameters)
    {
        var method = typeof(VRC3CVRCore).GetMethod("ProcessStateMachine", Flags);
        var args = new object[] { machine, "TestLayer", parameters };
        method.Invoke(core, args);
        return (AnimatorControllerParameter[])args[2];
    }

    static AvatarMask InvokeGetCombinedAvatarMask(VRC3CVRCore core, AvatarMask baseMask, AvatarMask layerMask) =>
        (AvatarMask)typeof(VRC3CVRCore).GetMethod("GetCombinedAvatarMask", Flags).Invoke(core, new object[] { baseMask, layerMask });

    static AvatarMask InvokeGetAvatarMaskForLayerAndVRCAnimator(VRC3CVRCore core, VRC3CVRCore.VRCBaseAnimatorID animatorID, int layerID, AvatarMask originalMask) =>
        (AvatarMask)typeof(VRC3CVRCore).GetMethod("GetAvatarMaskForLayerAndVRCAnimator", Flags).Invoke(core, new object[] { animatorID, layerID, originalMask });

    static AvatarMask InvokeReplaceVRCMask(VRC3CVRCore core, AvatarMask mask) =>
        (AvatarMask)typeof(VRC3CVRCore).GetMethod("ReplaceVRCMask", Flags).Invoke(core, new object[] { mask });

    static void SetPrivateField(VRC3CVRCore core, string name, object value) =>
        typeof(VRC3CVRCore).GetField(name, Flags).SetValue(core, value);

    static object GetPrivateField(VRC3CVRCore core, string name) =>
        typeof(VRC3CVRCore).GetField(name, Flags).GetValue(core);

    static AvatarMask MakeMask(string name, params AvatarMaskBodyPart[] activeParts)
    {
        var mask = new AvatarMask { name = name };
        for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        }
        foreach (var part in activeParts)
        {
            mask.SetHumanoidBodyPartActive(part, true);
        }
        return mask;
    }

    // Renders the fields relevant to a task's actual runtime behaviour into one comparable string:
    // op, target (type+name), and each operand (Static -> value, Random -> min..max, Parameter ->
    // type+name). Mirrors the projection style used in VRC3CVRGestureConversionTests.
    static string Describe(ABI.CCK.Components.AnimatorDriverTask t)
    {
        string Src(ABI.CCK.Components.AnimatorDriverTask.SourceType type, float value, float max, ABI.CCK.Components.AnimatorDriverTask.ParameterType paramType, string name)
        {
            switch (type)
            {
                case ABI.CCK.Components.AnimatorDriverTask.SourceType.Static: return value.ToString("0.###");
                case ABI.CCK.Components.AnimatorDriverTask.SourceType.Random: return "Random(" + value.ToString("0.###") + ".." + max.ToString("0.###") + ")";
                case ABI.CCK.Components.AnimatorDriverTask.SourceType.Parameter: return paramType + ":" + name;
                default: return "?";
            }
        }

        var text = t.op + " " + t.targetType + ":" + t.targetName + " = " + Src(t.aType, t.aValue, t.aMax, t.aParamType, t.aName);
        if (t.op != ABI.CCK.Components.AnimatorDriverTask.Operator.Set)
        {
            text += ", " + Src(t.bType, t.bValue, t.bMax, t.bParamType, t.bName);
        }
        return text;
    }

    // ==== VRCAvatarParameterDriver -> AnimatorDriver ====

    [Test]
    public void ParameterDriver_Set_ProducesStaticSetTask()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.localOnly = true;
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "TargetFloat",
            type = VRC_AvatarParameterDriver.ChangeType.Set,
            value = 0.75f,
        });

        RunProcessStateMachine(core, machine, new[] { Param("TargetFloat", AnimatorControllerParameterType.Float) });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.IsTrue(driver.localOnly, "localOnly must be carried over from the VRC driver");
        Assert.AreEqual(new[] { "Set Float:TargetFloat = 0.75" }, driver.EnterTasks.Select(Describe).ToArray());
    }

    [Test]
    public void ParameterDriver_Add_ProducesSelfReferencingAdditionTask()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Counter",
            type = VRC_AvatarParameterDriver.ChangeType.Add,
            value = 5f,
        });

        RunProcessStateMachine(core, machine, new[] { Param("Counter", AnimatorControllerParameterType.Int) });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        // Add has no separate source: VRC adds `value` to the same parameter it targets.
        Assert.AreEqual(new[] { "Addition Int:Counter = Int:Counter, 5" }, driver.EnterTasks.Select(Describe).ToArray());
    }

    [Test]
    public void ParameterDriver_RandomInt_ProducesSingleRandomSetTask()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Dice",
            type = VRC_AvatarParameterDriver.ChangeType.Random,
            valueMin = 1f,
            valueMax = 10f,
        });

        RunProcessStateMachine(core, machine, new[] { Param("Dice", AnimatorControllerParameterType.Int) });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.AreEqual(new[] { "Set Int:Dice = Random(1..10)" }, driver.EnterTasks.Select(Describe).ToArray());
    }

    [Test]
    public void ParameterDriver_RandomFloat_ProducesSingleRandomSetTask()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Jitter",
            type = VRC_AvatarParameterDriver.ChangeType.Random,
            valueMin = -1f,
            valueMax = 1f,
        });

        RunProcessStateMachine(core, machine, new[] { Param("Jitter", AnimatorControllerParameterType.Float) });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.AreEqual(new[] { "Set Float:Jitter = Random(-1..1)" }, driver.EnterTasks.Select(Describe).ToArray());
    }

    // CVR's AnimatorDriverTask has no coin-flip source, so Random on a Bool/Trigger parameter must
    // be decomposed into: draw a scratch Float in [0,1), then compare it against `chance`.
    [Test]
    public void ParameterDriver_RandomBool_DecomposesIntoScratchFloatAndLessThanTasks()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Flag",
            type = VRC_AvatarParameterDriver.ChangeType.Random,
            chance = 0.3f,
        });

        var resultParameters = RunProcessStateMachine(core, machine, new[] { Param("Flag", AnimatorControllerParameterType.Bool) });

        // A scratch Float parameter must have been declared to hold the random draw.
        Assert.AreEqual(2, resultParameters.Length, "a scratch parameter for the random draw must be appended");
        var scratch = resultParameters[1];
        Assert.AreEqual(AnimatorControllerParameterType.Float, scratch.type);
        StringAssert.StartsWith("Flag_Random_", scratch.name);

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.AreEqual(
            new[]
            {
                "Set Float:" + scratch.name + " = Random(0..1)",
                "LessThan Bool:Flag = Float:" + scratch.name + ", 0.3",
            },
            driver.EnterTasks.Select(Describe).ToArray());
    }

    [Test]
    public void ParameterDriver_Copy_WithoutConvertRange_ProducesPlainSetTask()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Dest",
            type = VRC_AvatarParameterDriver.ChangeType.Copy,
            source = "Src",
            convertRange = false,
        });

        RunProcessStateMachine(core, machine, new[]
        {
            Param("Dest", AnimatorControllerParameterType.Float),
            Param("Src", AnimatorControllerParameterType.Int),
        });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        Assert.AreEqual(new[] { "Set Float:Dest = Int:Src" }, driver.EnterTasks.Select(Describe).ToArray());
    }

    // convertRange remaps [sourceMin, sourceMax] onto [destMin, destMax]. AnimatorDriverTask has no
    // single "remap" op, so this must expand into Subtract srcMin, Multiply by the range ratio, then
    // Add destMin - all three chained through the destination parameter itself.
    [Test]
    public void ParameterDriver_CopyWithConvertRange_ExpandsIntoThreeChainedTasks()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Dest",
            type = VRC_AvatarParameterDriver.ChangeType.Copy,
            source = "Src",
            convertRange = true,
            sourceMin = 0f,
            sourceMax = 10f,
            destMin = -1f,
            destMax = 1f,
        });

        RunProcessStateMachine(core, machine, new[]
        {
            Param("Dest", AnimatorControllerParameterType.Float),
            Param("Src", AnimatorControllerParameterType.Int),
        });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        // coefficient = (destMax - destMin) / (sourceMax - sourceMin) = (1 - -1) / 10 = 0.2
        Assert.AreEqual(
            new[]
            {
                "Subtraction Float:Dest = Int:Src, 0",
                "Multiplication Float:Dest = Float:Dest, 0.2",
                "Addition Float:Dest = Float:Dest, -1",
            },
            driver.EnterTasks.Select(Describe).ToArray());
    }

    [Test]
    public void ParameterDriver_CopyWithConvertRange_ZeroSourceRange_WarnsAndSkips()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "Dest",
            type = VRC_AvatarParameterDriver.ChangeType.Copy,
            source = "Src",
            convertRange = true,
            sourceMin = 5f,
            sourceMax = 5f,
            destMin = -1f,
            destMax = 1f,
        });

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "Parameter \"Dest\" has zero source range (sourceMin == sourceMax == 5), skipping convertRange")));

        RunProcessStateMachine(core, machine, new[]
        {
            Param("Dest", AnimatorControllerParameterType.Float),
            Param("Src", AnimatorControllerParameterType.Int),
        });

        var driver = (ABI.CCK.Components.AnimatorDriver)state.behaviours.Single();
        CollectionAssert.IsEmpty(driver.EnterTasks, "a zero source range must not produce a divide-by-zero coefficient task");
    }

    // ==== VRCAnimatorTrackingControl / VRCAnimatorLocomotionControl -> BodyControl ====

    [Test]
    public void TrackingControl_ConvertsChangedPartsAndSkipsNoChange()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        tracking.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Tracking;
        tracking.trackingLeftHand = VRC_AnimatorTrackingControl.TrackingType.Animation;
        tracking.trackingRightHand = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingLeftFoot = VRC_AnimatorTrackingControl.TrackingType.Tracking;
        tracking.trackingRightFoot = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingHip = VRC_AnimatorTrackingControl.TrackingType.Animation;

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        var bodyControl = (ABI.CCK.Components.BodyControl)state.behaviours.Single();
        Assert.AreEqual(
            new[] { "Head=1", "LeftArm=0", "LeftLeg=1", "Pelvis=0" },
            bodyControl.EnterTasks.Select(t => t.target + "=" + t.targetWeight.ToString("0.#")).ToArray(),
            "RightArm/RightFoot (NoChange) must not produce tasks, and order must follow Head/LeftHand/RightHand/LeftFoot/RightFoot/Hip");
    }

    [Test]
    public void TrackingControl_AllNoChange_CreatesNoBodyControl()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        // Set explicitly rather than relying on the field defaults, so this test does not depend on
        // NoChange happening to be the SDK enum's default (ordinal 0) value.
        tracking.trackingHead = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingLeftHand = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingRightHand = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingLeftFoot = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingRightFoot = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        tracking.trackingHip = VRC_AnimatorTrackingControl.TrackingType.NoChange;

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        Assert.AreEqual(0, state.behaviours.Length, "an all-NoChange tracking control must not create a BodyControl, and must still be stripped");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LocomotionControl_AddsLocomotionBodyControlTask(bool disableLocomotion)
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
        locomotion.disableLocomotion = disableLocomotion;

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        var bodyControl = (ABI.CCK.Components.BodyControl)state.behaviours.Single();
        var task = bodyControl.EnterTasks.Single();
        Assert.AreEqual(ABI.CCK.Components.BodyControlTask.BodyMask.Locomotion, task.target);
        Assert.AreEqual(disableLocomotion ? 0f : 1f, task.targetWeight);
    }

    // Both behaviours target the same CVR concept (BodyControl); a state that has both a VRC
    // tracking control and a VRC locomotion control must end up with exactly one BodyControl
    // carrying both tasks, not two competing BodyControl instances.
    [Test]
    public void TrackingControlAndLocomotionControl_ShareASingleBodyControl()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        tracking.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Tracking;
        var locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
        locomotion.disableLocomotion = true;

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        var bodyControls = state.behaviours.OfType<ABI.CCK.Components.BodyControl>().ToArray();
        Assert.AreEqual(1, bodyControls.Length, "exactly one BodyControl must be created for the state");
        Assert.AreEqual(
            new[] { "Head=1", "Locomotion=0" },
            bodyControls[0].EnterTasks.Select(t => t.target + "=" + t.targetWeight.ToString("0.#")).ToArray(),
            "both the tracking task and the locomotion task must land on the same BodyControl");
        // no other behaviours (VRC originals) may remain
        Assert.AreEqual(1, state.behaviours.Length);
    }

    [Test]
    public void TrackingAndLocomotionControl_NotConverted_WhenConfigDisabled()
    {
        var core = new VRC3CVRCore
        {
            convertVRCAnimatorTrackingControl = false,
            convertVRCAnimatorLocomotionControl = false,
        };
        var (machine, state) = MakeSingleStateMachine();
        var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        tracking.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Tracking;
        var locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
        locomotion.disableLocomotion = true;

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        Assert.AreEqual(0, state.behaviours.Length, "disabled conversion options must still strip the VRC behaviours, but create no BodyControl");
    }

    // ==== VRC behaviour removal ====

    [Test]
    public void IsVrcStateMachineBehaviour_TrueForVrcNamespaceFalseForCvrAndNull()
    {
        var method = typeof(VRC3CVRCore).GetMethod("IsVrcStateMachineBehaviour", StaticFlags);
        var state = new AnimatorState { name = "Probe" };
        var vrc = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        var cvrDriver = state.AddStateMachineBehaviour<ABI.CCK.Components.AnimatorDriver>();
        var cvrBody = state.AddStateMachineBehaviour<ABI.CCK.Components.BodyControl>();

        Assert.IsTrue((bool)method.Invoke(null, new object[] { vrc }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { cvrDriver }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { cvrBody }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { null }));
    }

    // A VRC behaviour type ProcessStateMachine does not know how to convert (e.g. the "temporary
    // pose space" toggle) must still be stripped, exactly like the ones that do get converted, so
    // the merged controller never carries dangling VRC components.
    [Test]
    public void UnhandledVrcBehaviour_IsRemovedWithoutCvrReplacement()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        state.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();

        RunProcessStateMachine(core, machine, new AnimatorControllerParameter[0]);

        Assert.AreEqual(0, state.behaviours.Length);
    }

    [Test]
    public void MixOfHandledAndUnhandledVrcBehaviours_LeavesOnlyTheCvrEquivalent()
    {
        var core = new VRC3CVRCore();
        var (machine, state) = MakeSingleStateMachine();
        var vrcDriver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
        vrcDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
        {
            name = "TargetFloat",
            type = VRC_AvatarParameterDriver.ChangeType.Set,
            value = 1f,
        });
        state.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();

        RunProcessStateMachine(core, machine, new[] { Param("TargetFloat", AnimatorControllerParameterType.Float) });

        Assert.AreEqual(1, state.behaviours.Length);
        Assert.IsInstanceOf<ABI.CCK.Components.AnimatorDriver>(state.behaviours[0]);
    }

    // ==== AvatarMask semantics ====

    [Test]
    public void GetCombinedAvatarMask_PreservesTransformPathsFromBothInputMasks()
    {
        var core = new VRC3CVRCore();

        AvatarMask MakeMaskWithTransform(string name, string path)
        {
            var mask = new AvatarMask { name = name };
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            }
            mask.transformCount = 1;
            mask.SetTransformPath(0, path);
            mask.SetTransformActive(0, true);
            return mask;
        }

        var baseMask = MakeMaskWithTransform("Base", "Root/Prop");
        var layerMask = MakeMaskWithTransform("Layer", "Root/Prop");

        var combined = InvokeGetCombinedAvatarMask(core, baseMask, layerMask);

        // Both inputs restrict the same non-humanoid transform, so the combined mask must keep
        // restricting it too, or every layer that ends up using this combined mask stops masking
        // that transform at all.
        Assert.AreEqual(1, combined.transformCount,
            "REAL BUG: GetCombinedAvatarMask builds its result as `new AvatarMask()` and only copies " +
            "humanoid body part flags across (the AvatarMaskBodyPart loop); it never reads or writes " +
            "transformCount/GetTransformPath/GetTransformActive, so any non-humanoid transform-path " +
            "restriction on either input mask is silently dropped from the combined mask.");
    }

    [TestCase(VRC3CVRCore.VRCBaseAnimatorID.BASE)]
    [TestCase(VRC3CVRCore.VRCBaseAnimatorID.ADDITIVE)]
    public void GetAvatarMaskForLayer_BaseAndAdditive_IntersectWithFullMask(VRC3CVRCore.VRCBaseAnimatorID animatorID)
    {
        var core = new VRC3CVRCore();
        SetPrivateField(core, "fullMask", MakeMask("Full", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm));
        SetPrivateField(core, "musclesOnlyMask", MakeMask("Muscles"));
        SetPrivateField(core, "emptyMask", MakeMask("Empty"));

        var layerMask = MakeMask("Layer", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.Head);

        var result = InvokeGetAvatarMaskForLayerAndVRCAnimator(core, animatorID, 0, layerMask);

        Assert.IsTrue(result.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm), "active in both full mask and layer mask");
        Assert.IsFalse(result.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm), "layer mask excluded RightArm");
        Assert.IsFalse(result.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head), "full mask excluded Head");
    }

    [Test]
    public void GetAvatarMaskForLayer_Action_IntersectsWithMusclesOnlyMaskNotFullMask()
    {
        var core = new VRC3CVRCore();
        // Full mask allows RightArm; muscles-only mask does not. Action layers must be restricted
        // by the muscles-only baseline, not accidentally by the (more permissive) full mask.
        SetPrivateField(core, "fullMask", MakeMask("Full", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm));
        SetPrivateField(core, "musclesOnlyMask", MakeMask("Muscles", AvatarMaskBodyPart.LeftArm));
        SetPrivateField(core, "emptyMask", MakeMask("Empty"));

        var layerMask = MakeMask("Layer", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm);

        var result = InvokeGetAvatarMaskForLayerAndVRCAnimator(core, VRC3CVRCore.VRCBaseAnimatorID.ACTION, 0, layerMask);

        Assert.IsTrue(result.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm));
        Assert.IsFalse(result.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
            "musclesOnlyMask excluded RightArm even though the full mask and the layer mask both allow it");
    }

    [TestCase(0)]
    [TestCase(1)]
    public void GetAvatarMaskForLayer_FX_AlwaysReturnsEmptyMaskRegardlessOfLayerMask(int layerID)
    {
        var core = new VRC3CVRCore();
        var empty = MakeMask("Empty");
        SetPrivateField(core, "emptyMask", empty);
        SetPrivateField(core, "fullMask", MakeMask("Full", AvatarMaskBodyPart.LeftArm));
        SetPrivateField(core, "musclesOnlyMask", MakeMask("Muscles"));

        var layerMask = MakeMask("Layer", AvatarMaskBodyPart.Head);
        var result = InvokeGetAvatarMaskForLayerAndVRCAnimator(core, VRC3CVRCore.VRCBaseAnimatorID.FX, layerID, layerMask);

        Assert.AreSame(empty, result, "FX layers must always be forced to the empty mask, ignoring the original layer mask entirely");
    }

    // The gesture mask is special-cased: layer 0 has nothing to combine with yet and becomes the
    // baseline ("derived from the *first* layer", per the field's own comment); every later gesture
    // layer must then be intersected against that baseline.
    [Test]
    public void GetAvatarMaskForLayer_Gesture_FirstLayerBecomesBaselineForLaterLayers()
    {
        var core = new VRC3CVRCore();
        SetPrivateField(core, "fullMask", MakeMask("Full"));
        SetPrivateField(core, "musclesOnlyMask", MakeMask("Muscles"));
        SetPrivateField(core, "emptyMask", MakeMask("Empty"));

        var layer0Mask = MakeMask("GestureLayer0", AvatarMaskBodyPart.LeftArm);
        var result0 = InvokeGetAvatarMaskForLayerAndVRCAnimator(core, VRC3CVRCore.VRCBaseAnimatorID.GESTURE, 0, layer0Mask);
        Assert.AreSame(layer0Mask, result0, "layer 0 has nothing to combine with yet, so it should pass through unchanged");
        Assert.AreSame(layer0Mask, GetPrivateField(core, "gestureMask"), "layer 0's mask must become the baseline for later gesture layers");

        var layer1Mask = MakeMask("GestureLayer1", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm);
        var result1 = InvokeGetAvatarMaskForLayerAndVRCAnimator(core, VRC3CVRCore.VRCBaseAnimatorID.GESTURE, 1, layer1Mask);

        Assert.IsTrue(result1.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm), "both layer 0's baseline and layer 1's mask allow LeftArm");
        Assert.IsFalse(result1.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm), "layer 0's baseline excluded RightArm, so layer 1 must not override it");
    }

    [Test]
    public void ReplaceVRCMask_ReplacesBuiltInVrcMaskNamesWithCckEquivalentsAndPassesOthersThrough()
    {
        var core = new VRC3CVRCore();
        var loadMask = typeof(VRC3CVRCore).GetMethod("LoadMask", StaticFlags);
        AvatarMask Expected(string fileName) => (AvatarMask)loadMask.Invoke(null, new object[] { fileName });

        Assert.AreSame(Expected("vrc3cvrHandLeft.mask"), InvokeReplaceVRCMask(core, new AvatarMask { name = "vrc_Hand Left" }));
        Assert.AreSame(Expected("vrc3cvrHandRight.mask"), InvokeReplaceVRCMask(core, new AvatarMask { name = "vrc_Hand Right" }));
        Assert.AreSame(Expected("vrc3cvrHandsOnly.mask"), InvokeReplaceVRCMask(core, new AvatarMask { name = "vrc_HandsOnly" }));
        Assert.AreSame(Expected("vrc3cvrMusclesOnly.mask"), InvokeReplaceVRCMask(core, new AvatarMask { name = "vrc_MusclesOnly" }));

        var custom = new AvatarMask { name = "MyLayerMask" };
        Assert.AreSame(custom, InvokeReplaceVRCMask(core, custom), "non-VRC-builtin masks must pass through unchanged");

        Assert.IsNull(InvokeReplaceVRCMask(core, null), "a null mask must pass through unchanged");
    }
}
#endif
