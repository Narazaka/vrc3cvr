#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

public class VRC3CVRBakerTests
{
    const string TestFolder = "Assets/VRC3CVR_BakerTest";

    GameObject original;
    GameObject baked;

    [TearDown]
    public void TearDown()
    {
        // Never leave a stubbed runner behind: it would silently disable baking for everyone else.
        VRC3CVRBaker.preprocessRunner = VRCBuildPipelineCallbacks.OnPreprocessAvatar;
        if (original != null) UnityEngine.Object.DestroyImmediate(original);
        if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
        AssetDatabase.DeleteAsset(TestFolder);
    }

    VRCAvatarDescriptor GenerateAvatar()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        original = descriptor.gameObject;
        return descriptor;
    }

    [Test]
    public void Bake_ReturnsACloneAndLeavesTheOriginalAlone()
    {
        var descriptor = GenerateAvatar();
        var originalChildCount = original.transform.childCount;

        var result = VRC3CVRBaker.Bake(original);
        baked = result.bakedAvatar;

        Assert.IsTrue(result.succeeded, result.errorMessage);
        Assert.IsNotNull(baked);
        Assert.AreNotSame(original, baked);
        Assert.IsNotNull(descriptor, "the original descriptor must survive");
        Assert.AreEqual(originalChildCount, original.transform.childCount);
        Assert.IsNotNull(baked.GetComponent<VRCAvatarDescriptor>(), "the bake result is still a VRChat avatar");
    }

    [Test]
    public void Bake_RemovesTheRequestedComponentsFromTheClone()
    {
        GenerateAvatar();
        Undo.AddComponent<VRC3CVRAvatar>(original);
        Assert.IsNotNull(original.GetComponent<VRC3CVRAvatar>());

        var result = VRC3CVRBaker.Bake(original, typeof(VRC3CVRAvatar));
        baked = result.bakedAvatar;

        Assert.IsTrue(result.succeeded, result.errorMessage);
        Assert.IsNull(baked.GetComponent<VRC3CVRAvatar>(), "the settings component must not reach the bake");
        Assert.IsNotNull(original.GetComponent<VRC3CVRAvatar>(), "the original keeps its settings");
    }

    [Test]
    public void Bake_DestroysTheCloneWhenAPreprocessHookFails()
    {
        GenerateAvatar();
        var rootCountBefore = original.scene.rootCount;
        VRC3CVRBaker.preprocessRunner = _ => false;

        var result = VRC3CVRBaker.Bake(original);

        Assert.IsFalse(result.succeeded);
        Assert.IsNull(result.bakedAvatar);
        Assert.IsNotEmpty(result.errorMessage);
        Assert.AreEqual(rootCountBefore, original.scene.rootCount, "the failed clone must not be left in the scene");
    }

    [Test]
    public void Bake_DestroysTheCloneWhenAPreprocessHookThrows()
    {
        GenerateAvatar();
        var rootCountBefore = original.scene.rootCount;
        VRC3CVRBaker.preprocessRunner = _ => throw new Exception("hook exploded");

        var result = VRC3CVRBaker.Bake(original);

        Assert.IsFalse(result.succeeded);
        Assert.IsNull(result.bakedAvatar);
        StringAssert.Contains("hook exploded", result.errorMessage);
        Assert.AreEqual(rootCountBefore, original.scene.rootCount, "the failed clone must not be left in the scene");
    }
}
#endif
