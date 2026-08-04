#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Guards for failures that are silent or fatal in the field: missing assets, avatars without
// optional data, and regressions of bugs that were already fixed once.
// See VRC3CVRGestureConversionTests for why these live in Assembly-CSharp-Editor.
public class VRC3CVRConversionRobustnessTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    const string TestFolder = "Assets/VRC3CVR_RobustnessTest";

    GameObject original;
    GameObject converted;

    [TearDown]
    public void TearDown()
    {
        if (original != null) Object.DestroyImmediate(original);
        if (converted != null) Object.DestroyImmediate(converted);
        AssetDatabase.DeleteAsset(TestFolder);
    }

    // The avatar masks are loaded by path. A case mismatch (or a moved file) makes every load
    // return null, and nothing throws: layers then run unmasked and overwrite the whole humanoid
    // rig, which looks like the avatar freezing in a T-pose rather than like a missing asset.
    [Test]
    public void MaskAssets_AreAllLoadable()
    {
        var maskDir = (string)typeof(VRC3CVRCore)
            .GetField("EditorMaskDir", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);
        Assert.IsTrue(AssetDatabase.IsValidFolder(maskDir),
            "the mask directory \"" + maskDir + "\" does not exist (a path or casing mismatch makes every mask load return null)");

        var loadMask = typeof(VRC3CVRCore).GetMethod("LoadMask", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(loadMask, "LoadMask was renamed; update this test");
        var maskNames = new[]
        {
            "vrc3cvrEmptyMask.mask",
            "vrc3cvrFullMask.mask",
            "vrc3cvrMusclesOnly.mask",
            "vrc3cvrHandLeft.mask",
            "vrc3cvrHandRight.mask",
            "vrc3cvrHandsOnly.mask",
        };
        var missing = maskNames.Where(name => loadMask.Invoke(null, new object[] { name }) == null).ToArray();
        CollectionAssert.IsEmpty(missing, "these masks could not be loaded from \"" + maskDir + "\"");
    }

    // Avatars without an expression menu leave expressionParameters null; the conversion must
    // still finish instead of dying half-way and leaving a broken avatar behind.
    [Test]
    public void Convert_WithoutExpressionParameters_Completes()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        original = descriptor.gameObject;
        descriptor.customExpressions = false;
        descriptor.expressionParameters = null;
        descriptor.expressionsMenu = null;

        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
        });
        core.Convert();
        converted = core.chilloutAvatar;

        Assert.IsNotNull(converted, "the conversion produced no avatar");
        var cvrAvatar = converted.GetComponent<ABI.CCK.Components.CVRAvatar>();
        Assert.IsNotNull(cvrAvatar, "the converted avatar has no CVRAvatar");
        Assert.IsNotNull(cvrAvatar.avatarSettings.baseController, "the converted avatar has no animator controller");
    }

    // Unity forces the runtime weight of layer 0 to 1 whatever the serialized value says, so a
    // controller whose first layer is serialized at 0 works in VRChat and silently stops working
    // once merging moves it out of first place. Regression guard for that fix.
    [Test]
    public void MergedController_ForcesFirstLayerWeightToOne()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        original = descriptor.gameObject;

        var fx = descriptor.baseAnimationLayers
            .First(layer => layer.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX)
            .animatorController as AnimatorController;
        Assert.IsNotNull(fx);
        var layers = fx.layers;
        var firstLayerName = layers[0].name;
        layers[0].defaultWeight = 0f;
        fx.layers = layers;

        var core = VRC3CVRCore.FromConfig(new VRC3CVRConvertConfig
        {
            vrcAvatarDescriptor = descriptor,
            shouldCloneAvatar = true,
            saveAssets = false,
        });
        core.Convert();
        converted = core.chilloutAvatar;

        var merged = converted.GetComponent<ABI.CCK.Components.CVRAvatar>().avatarSettings.baseController as AnimatorController;
        var mergedLayer = merged.layers.FirstOrDefault(layer => layer.name == firstLayerName);
        Assert.IsNotNull(mergedLayer, "the source layer \"" + firstLayerName + "\" is missing from the merged controller");
        Assert.AreEqual(1f, mergedLayer.defaultWeight,
            "the first source layer kept its serialized weight of 0 and would never run after merging");
    }

    // The parameter-rename pass deletes the curve it rewrites and re-adds it under the new name, so
    // mistaking a humanoid curve for a parameter write destroys the animation rather than aliasing
    // it. The Gesture*Weight tests cover the declared side; this is the other half.
    [Test]
    public void AdjustParameterNames_LeavesACurveAloneWhenNoParameterOfThatNameIsDeclared()
    {
        var controller = new AnimatorController { name = "humanoidCurveTest" };
        controller.AddParameter("Declared", AnimatorControllerParameterType.Float);
        controller.AddLayer("L");
        var clip = new AnimationClip { name = "Walk" };
        // how Unity hands back a humanoid curve whose name it cannot resolve
        clip.SetCurve("", typeof(Animator), "unknown_2", AnimationCurve.Constant(0f, 1f, 1f));
        controller.layers[0].stateMachine.AddState("S").motion = clip;

        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, controller);
        typeof(VRC3CVRCore).GetField("preserveParameters", Flags).SetValue(core, new System.Collections.Generic.HashSet<string>());
        typeof(VRC3CVRCore).GetField("impulseParameters", Flags).SetValue(core, new System.Collections.Generic.HashSet<string>());
        typeof(VRC3CVRCore).GetMethod("AdjustParameterNamesOnAnimator", Flags).Invoke(core, null);

        Assert.AreSame(clip, controller.layers[0].stateMachine.states.Single().state.motion,
            "the clip was replaced by a rewritten copy");
        Assert.AreEqual("Walk", clip.name);
        CollectionAssert.AreEqual(new[] { "unknown_2" },
            AnimationUtility.GetCurveBindings(clip).Select(binding => binding.propertyName).ToArray());
        Assert.AreEqual("#Declared", controller.parameters.Single().name,
            "the fixture no longer renames anything, so it cannot show that curves are spared");

        Object.DestroyImmediate(clip);
        Object.DestroyImmediate(controller);
    }
}
#endif
