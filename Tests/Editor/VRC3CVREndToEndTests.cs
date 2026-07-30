#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
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
