#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

// The processor is normally driven by the CCK build pipeline, but OnPreProcessAvatar takes the
// object to convert as its argument, so it can be exercised directly.
public class VRC3CVRBuildProcessorTests
{
    const string TestFolder = "Assets/VRC3CVR_BuildProcessorTest";

    GameObject avatar;

    [TearDown]
    public void TearDown()
    {
        // Never leave a stubbed runner behind: it would silently disable baking for everyone else.
        VRC3CVRBaker.preprocessRunner = VRCBuildPipelineCallbacks.OnPreprocessAvatar;
        if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
        AssetDatabase.DeleteAsset(TestFolder);
    }

    VRCAvatarDescriptor GenerateAvatar()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        avatar = descriptor.gameObject;
        return descriptor;
    }

    [Test]
    public void OnPreProcessAvatar_ConvertsInPlaceWhenTheSettingsComponentIsPresent()
    {
        GenerateAvatar();
        var settings = Undo.AddComponent<VRC3CVRAvatar>(avatar);
        settings.convertConfig.autoBake = false;
        settings.convertConfig.saveAssets = false;

        new VRC3CVRBuildProcessor().OnPreProcessAvatar(avatar);

        Assert.IsNull(avatar.GetComponent<VRCAvatarDescriptor>(), "the VRChat descriptor is gone");
        Assert.IsNull(avatar.GetComponent<VRC3CVRAvatar>(), "settings must not ship in the bundle");
        // VRC3CVRAvatar's RequireComponent(CVRAvatar) plus OnValidate already attach a CVRAvatar
        // (and a valid CVRAssetInfo) the moment the settings component is added, before the
        // conversion runs. Only the conversion sets up the override controller, so check that
        // instead of presence, which would be true either way.
        var cvrAvatar = avatar.GetComponent<CVRAvatar>();
        Assert.IsNotNull(cvrAvatar);
        Assert.IsNotNull(cvrAvatar.overrides, "only the conversion sets up the override controller");
    }

    [Test]
    public void OnPreProcessAvatar_StopsTheUploadWithoutTheSettingsComponent()
    {
        var descriptor = GenerateAvatar();

        // CVRAvatar/CVRAssetInfo do not require VRC3CVRAvatar back (RequireComponent only
        // constrains the other direction), so an avatar can keep a valid CVRAssetInfo -- and stay
        // listed in the CCK Control Panel -- after VRC3CVRAvatar is removed or never added.
        // Uploading from there would publish a still-VRChat avatar to ChilloutVR and spend a content
        // slot on it, so the build has to stop rather than warn and continue.
        Assert.Throws<Exception>(() => new VRC3CVRBuildProcessor().OnPreProcessAvatar(avatar));

        Assert.IsNotNull(avatar.GetComponent<VRCAvatarDescriptor>(),
            "an avatar that is not managed by vrc3cvr must be left completely alone");
        Assert.AreSame(descriptor, avatar.GetComponent<VRCAvatarDescriptor>());
    }

    [Test]
    public void OnPreProcessAvatar_DoesNothingWhenTheAvatarIsAlreadyConverted()
    {
        GenerateAvatar();
        Undo.AddComponent<VRC3CVRAvatar>(avatar);
        UnityEngine.Object.DestroyImmediate(avatar.GetComponent<VRCAvatarDescriptor>());

        new VRC3CVRBuildProcessor().OnPreProcessAvatar(avatar);

        Assert.IsNotNull(avatar.GetComponent<VRC3CVRAvatar>(),
            "with no VRChat descriptor there is nothing to convert, so nothing is touched");
    }

    [Test]
    public void OnPreProcessAvatar_ThrowsWhenTheBakeIsRejected()
    {
        GenerateAvatar();
        var settings = Undo.AddComponent<VRC3CVRAvatar>(avatar);
        settings.convertConfig.autoBake = true;
        settings.convertConfig.saveAssets = false;
        VRC3CVRBaker.preprocessRunner = _ => false;

        Assert.Throws<Exception>(() => new VRC3CVRBuildProcessor().OnPreProcessAvatar(avatar));
        Assert.IsNotNull(avatar.GetComponent<VRCAvatarDescriptor>(),
            "a rejected bake must not leave a half-converted avatar in the bundle");
    }

    [Test]
    public void OnPreProcessAvatar_ConvertsAfterASuccessfulBake()
    {
        GenerateAvatar();
        var settings = Undo.AddComponent<VRC3CVRAvatar>(avatar);
        settings.convertConfig.autoBake = true;
        settings.convertConfig.saveAssets = false;
        // Stand in for the real hook chain: report success without modifying the avatar.
        VRC3CVRBaker.preprocessRunner = _ => true;

        new VRC3CVRBuildProcessor().OnPreProcessAvatar(avatar);

        Assert.IsNull(avatar.GetComponent<VRCAvatarDescriptor>());
        // See OnPreProcessAvatar_ConvertsInPlaceWhenTheSettingsComponentIsPresent: CVRAvatar's mere
        // presence would be true even without a conversion, so check what only the conversion sets.
        var cvrAvatar = avatar.GetComponent<CVRAvatar>();
        Assert.IsNotNull(cvrAvatar);
        Assert.IsNotNull(cvrAvatar.overrides, "only the conversion sets up the override controller");
    }
}
#endif
