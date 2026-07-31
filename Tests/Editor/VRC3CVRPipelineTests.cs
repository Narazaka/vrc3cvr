#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;

public class VRC3CVRPipelineTests
{
    const string TestFolder = "Assets/VRC3CVR_PipelineTest";

    GameObject original;
    GameObject converted;

    [TearDown]
    public void TearDown()
    {
        if (original != null) Object.DestroyImmediate(original);
        if (converted != null) Object.DestroyImmediate(converted);
        AssetDatabase.DeleteAsset(TestFolder);
    }

    VRCAvatarDescriptor GenerateAvatar()
    {
        var descriptor = VRC3CVRVerificationAvatar.Generate(TestFolder);
        original = descriptor.gameObject;
        return descriptor;
    }

    static VRC3CVRConvertConfig ConfigWithBake(bool autoBake) => new VRC3CVRConvertConfig
    {
        autoBake = autoBake,
        shouldCloneAvatar = true,
        saveAssets = false,
    };

    [Test]
    public void Convert_WithAutoBake_ProducesACvrAvatarAndKeepsTheOriginal()
    {
        var descriptor = GenerateAvatar();

        var result = VRC3CVRPipeline.Convert(descriptor, ConfigWithBake(true));
        converted = result.convertedAvatar;

        Assert.IsTrue(result.succeeded, result.errorMessage);
        Assert.IsTrue(result.usedBake);
        Assert.IsNotNull(converted.GetComponent<CVRAvatar>());
        Assert.AreNotSame(original, converted);
        Assert.IsNotNull(original.GetComponent<VRCAvatarDescriptor>(), "the original avatar is untouched");
        Assert.IsTrue(original.activeSelf, "the original avatar is not hidden");
    }

    [Test]
    public void Convert_WithoutAutoBake_StillProducesACvrAvatar()
    {
        var descriptor = GenerateAvatar();

        var result = VRC3CVRPipeline.Convert(descriptor, ConfigWithBake(false));
        converted = result.convertedAvatar;

        Assert.IsTrue(result.succeeded, result.errorMessage);
        Assert.IsFalse(result.usedBake);
        Assert.IsNotNull(converted.GetComponent<CVRAvatar>());
        Assert.AreNotSame(original, converted);

        // Without a VRC3CVRAvatar component nothing brings CVRAssetInfo along, so the conversion
        // has to establish it or the result cannot be uploaded.
        var assetInfo = converted.GetComponent<CVRAssetInfo>();
        Assert.IsNotNull(assetInfo, "the converted avatar must be listable in the CCK Control Panel");
        Assert.AreEqual(CVRAssetInfo.AssetType.Avatar, assetInfo.type);
    }

    [Test]
    public void Convert_StripsTheSettingsComponentAndMarksTheAssetInfoAsAvatar()
    {
        var descriptor = GenerateAvatar();
        Undo.AddComponent<VRC3CVRAvatar>(original);

        var result = VRC3CVRPipeline.Convert(descriptor, ConfigWithBake(false));
        converted = result.convertedAvatar;

        Assert.IsTrue(result.succeeded, result.errorMessage);
        Assert.IsNull(converted.GetComponent<VRC3CVRAvatar>(), "settings must not ship in the converted avatar");
        // shouldCloneAvatar makes `converted` a clone of `original`, and VRC3CVRAvatar's
        // RequireComponent(CVRAvatar) already gave `original` a CVRAvatar and a valid CVRAssetInfo
        // before Convert() ran (see AddingTheSettingsComponent_BringsTheCckComponentsWithIt), so
        // those would clone across regardless of whether the conversion did anything. Only the
        // conversion sets up the override controller, so check that instead.
        var cvrAvatar = converted.GetComponent<CVRAvatar>();
        Assert.IsNotNull(cvrAvatar);
        Assert.IsNotNull(cvrAvatar.overrides, "only the conversion sets up the override controller");
    }

    [Test]
    public void AddingTheSettingsComponent_BringsTheCckComponentsWithIt()
    {
        GenerateAvatar();

        Undo.AddComponent<VRC3CVRAvatar>(original);

        // RequireComponent pulls in CVRAvatar, and adding it that way does run its Reset/OnValidate,
        // which is what attaches CVRAssetInfo and sets the type. Measured, not assumed: the CCK
        // Control Panel only lists content whose CVRAssetInfo has a valid type, so the whole
        // upload-time conversion design rests on this holding.
        Assert.IsNotNull(original.GetComponent<CVRAvatar>(), "RequireComponent should attach CVRAvatar");
        var assetInfo = original.GetComponent<CVRAssetInfo>();
        Assert.IsNotNull(assetInfo, "CVRAvatar's OnValidate should attach CVRAssetInfo");
        Assert.AreEqual(CVRAssetInfo.AssetType.Avatar, assetInfo.type,
            "without a valid type the avatar is filtered out of the CCK Control Panel listing");
    }

    [Test]
    public void GetConvertBlocker_ReportsAMissingAvatar()
    {
        Assert.IsNotNull(VRC3CVRPipeline.GetConvertBlocker(null));
    }

    [Test]
    public void GetConvertBlocker_AllowsAValidAvatar()
    {
        var descriptor = GenerateAvatar();
        Assert.IsNull(VRC3CVRPipeline.GetConvertBlocker(descriptor));
    }
}
#endif
