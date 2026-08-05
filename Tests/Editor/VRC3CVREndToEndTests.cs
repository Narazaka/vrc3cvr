#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using ABI.CCK.Components;

// End-to-end: generate the verification avatar and run the whole conversion on it.
public class VRC3CVREndToEndTests
{
    const string TestFolder = "Assets/VRC3CVR_VerificationAvatarTest";

    GameObject original;
    GameObject converted;

    [TearDown]
    public void TearDown()
    {
        if (original != null) Object.DestroyImmediate(original);
        if (converted != null) Object.DestroyImmediate(converted);
        AssetDatabase.DeleteAsset(TestFolder);
    }

    GameObject Convert(VRC3CVRConvertConfig.GestureWeightConversionMode mode)
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        original = descriptor.gameObject;
        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
            gestureWeightConversionMode = mode,
            // off by default, and the avatar's Base layer is only looked at when it is on
            convertLocomotionLayer = true,
        });
        core.Convert();
        converted = core.chilloutAvatar;
        Assert.IsNotNull(converted);
        return converted;
    }

    static AnimatorController ControllerOf(GameObject avatar)
    {
        var cvrAvatar = avatar.GetComponent<CVRAvatar>();
        Assert.IsNotNull(cvrAvatar);
        var controller = cvrAvatar.avatarSettings.baseController as AnimatorController;
        Assert.IsNotNull(controller);
        return controller;
    }

    [Test]
    public void ConvertVerificationAvatar_FoldMode()
    {
        var avatar = Convert(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var controller = ControllerOf(avatar);

        // constraints: all VRC constraints replaced by Unity constraints
        Assert.AreEqual(0, avatar.GetComponentsInChildren<VRC.Dynamics.VRCConstraintBase>(true).Length);
        var constraints = avatar.transform.Find("Constraints");
        Assert.IsNotNull(constraints.Find("ParentC").GetComponent<ParentConstraint>());
        var positionC = constraints.Find("PositionC").GetComponent<PositionConstraint>();
        Assert.AreEqual(1, positionC.sourceCount);
        Assert.AreEqual("LeftHand", positionC.GetSource(0).sourceTransform.name);
        Assert.AreEqual(new Vector3(0f, 0.25f, 0f), positionC.translationOffset);
        Assert.IsTrue(positionC.constraintActive);
        Assert.IsNotNull(constraints.Find("RotationC").GetComponent<RotationConstraint>());
        Assert.IsNotNull(constraints.Find("ScaleC").GetComponent<ScaleConstraint>());
        Assert.IsNotNull(constraints.Find("AimC").GetComponent<AimConstraint>());
        Assert.IsNotNull(constraints.Find("LookAtC").GetComponent<LookAtConstraint>());
        // Target Transform redirect: the converted constraint lives on the target
        Assert.IsNull(constraints.Find("RedirHolder").GetComponent<PositionConstraint>());
        Assert.IsNotNull(constraints.Find("RedirTarget").GetComponent<PositionConstraint>());
        // same-type merge
        var merged = constraints.Find("MergeC").GetComponents<PositionConstraint>();
        Assert.AreEqual(1, merged.Length);
        Assert.AreEqual(2, merged[0].sourceCount);

        // animated constraint clip is rebound to the Unity constraint
        var clipBindings = controller.animationClips
            .SelectMany(clip => AnimationUtility.GetCurveBindings(clip))
            .Select(binding => binding.path + "|" + binding.type.Name + "|" + binding.propertyName)
            .ToHashSet();
        Assert.IsTrue(clipBindings.Contains("Constraints/AnimC|PositionConstraint|m_Active"));
        Assert.IsTrue(clipBindings.Contains("Constraints/AnimC|PositionConstraint|m_Weight"));
        Assert.IsTrue(clipBindings.Contains("Constraints/AnimC|PositionConstraint|m_Sources.Array.data[0].weight"));
        Assert.IsFalse(clipBindings.Any(binding => binding.Contains("VRCPositionConstraint")),
            string.Join(" ; ", clipBindings.Where(binding => binding.Contains("VRC"))));

        // fold mode: the weight blend tree is redriven by GestureLeft with fixed-1 boundaries
        var layers = controller.layers;
        var weightBarLayer = layers.First(layer => layer.name.Contains("G1 WeightBar"));
        var tree = (BlendTree)weightBarLayer.stateMachine.states.Single().state.motion;
        Assert.AreEqual("GestureLeft", tree.blendParameter);
        Assert.AreEqual(4, tree.children.Length);
        Assert.IsFalse(layers.Any(layer => layer.name.Contains("VRC3CVR_GestureLeftWeight")));

        // velocity feed layer exists; the now-unreferenced weight parameter stays declared as local
        Assert.IsTrue(layers.Any(layer => layer.name.Contains("VRC3CVR_VelocityMagnitude")));
        Assert.IsTrue(controller.parameters.Any(parameter => parameter.name == "#GestureLeftWeight"));

        // game state stream: wearer-side sources for the synced parameters
        var stream = avatar.GetComponent<CVRParameterStream>();
        Assert.IsNotNull(stream);
        Assert.AreEqual(
            new[] { "LocalPlayerMuted -> MuteSelf", "DeviceMode -> VRMode", "AvatarUpright -> Upright" },
            stream.entries.Select(entry => entry.type + " -> " + entry.parameterName).ToArray());
        // synced (no # prefix)
        Assert.IsTrue(controller.parameters.Any(parameter => parameter.name == "MuteSelf"));
        Assert.IsTrue(controller.parameters.Any(parameter => parameter.name == "VRMode"));
        Assert.IsTrue(controller.parameters.Any(parameter => parameter.name == "Upright"));
    }

    // VRC3CVRBaseGraftTests already proves the graft mechanism on purpose-built controllers; this
    // only asks whether the verification avatar — the one that gets uploaded and checked in game —
    // really came out of the conversion grafted.
    [Test]
    public void ConvertVerificationAvatar_GraftsItsOwnLocomotionOntoTheChilloutVRLayer()
    {
        var avatar = Convert(VRC3CVRConvertConfig.GestureWeightConversionMode.FoldToGestureLeft);
        var controller = ControllerOf(avatar);
        var layer = controller.layers.Single(l => l.name == "Locomotion/Emotes");
        var machine = layer.stateMachine;

        Assert.IsTrue(layer.iKPass);
        Assert.AreEqual("Locomotion", machine.defaultState.name);

        var hubClips = ((BlendTree)machine.defaultState.motion).children
            .Select(child => child.motion.name).ToArray();
        Assert.AreEqual(new[] { "Base_CustomIdle", "LocWalkingForward" }, hubClips);
        Assert.IsFalse(ClipNamesOf(machine).Any(name => name.StartsWith("proxy_")),
            string.Join(" ; ", ClipNamesOf(machine)));

        Assert.IsTrue(machine.states.Any(child => child.state.name == "LocFlying"));
        Assert.IsTrue(machine.states.Any(child => child.state.name == "Swimming"));
        Assert.IsTrue(machine.stateMachines.Any(child => child.stateMachine.name == "Emotes"));

        // Nothing in the converted asset shows whether the pass-through still passes, so it is run.
        var animator = avatar.GetComponent<Animator>();
        // the controller was assigned to a component that already existed, and outside play mode
        // nothing rebinds it on its own -- until it does, the animator reports no layers at all
        animator.Rebind();
        var layerIndex = Enumerable.Range(0, animator.layerCount)
            .Single(index => animator.GetLayerName(index) == "Locomotion/Emotes");
        animator.SetBool("Grounded", true);
        animator.Update(0f);
        animator.SetBool("Grounded", false);
        animator.Update(1f / 60f);
        Assert.AreEqual(Animator.StringToHash("RestoreTracking"),
            animator.GetCurrentAnimatorStateInfo(layerIndex).shortNameHash, "the avatar never went airborne");

        animator.SetBool("Grounded", true);
        var frames = 0;
        while (frames < 10 &&
               animator.GetCurrentAnimatorStateInfo(layerIndex).shortNameHash != Animator.StringToHash("Locomotion"))
        {
            animator.Update(1f / 60f);
            frames++;
        }
        Assert.Less(frames, 10, "landing left the avatar stuck in the pass-through state");
    }

    static IEnumerable<string> ClipNamesOf(AnimatorStateMachine machine)
    {
        return machine.states.SelectMany(child => MotionNamesOf(child.state.motion))
            .Concat(machine.stateMachines.SelectMany(child => ClipNamesOf(child.stateMachine)));
    }

    static IEnumerable<string> MotionNamesOf(Motion motion)
    {
        if (motion is BlendTree tree)
        {
            return tree.children.SelectMany(child => MotionNamesOf(child.motion));
        }
        return motion != null ? new[] { motion.name } : Enumerable.Empty<string>();
    }

    [Test]
    public void ConvertVerificationAvatar_DerivedMode()
    {
        var avatar = Convert(VRC3CVRConvertConfig.GestureWeightConversionMode.DerivedParameter);
        var controller = ControllerOf(avatar);
        var layers = controller.layers;

        // derived mode: the weight parameter survives (local) and is fed by the generated layer
        Assert.IsTrue(controller.parameters.Any(parameter => parameter.name == "#GestureLeftWeight"));
        Assert.IsTrue(layers.Any(layer => layer.name.Contains("VRC3CVR_GestureLeftWeight")));
        var weightBarLayer = layers.First(layer => layer.name.Contains("G1 WeightBar"));
        var tree = (BlendTree)weightBarLayer.stateMachine.states.Single().state.motion;
        Assert.AreEqual("#GestureLeftWeight", tree.blendParameter);
        Assert.AreEqual(2, tree.children.Length);
    }
}
#endif
