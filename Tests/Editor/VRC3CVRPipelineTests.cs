#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.TestTools;
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
        // Must be renamed before VRC3CVRCore.Convert() runs, not after: Convert() reads the
        // GameObject's name mid-conversion to build asset names and file paths, so a rename that
        // happens afterward leaves disk assets permanently named after the baker's intermediate
        // "(Baked)" step while the scene shows "(ChilloutVR)".
        Assert.AreEqual(original.name + " (ChilloutVR)", converted.name);
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
    public void Convert_StripsTheSettingsComponentAndSetsUpTheOverrideController()
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
    public void AddingTheSettingsComponent_DoesNotDuplicateTheAssetInfo()
    {
        GenerateAvatar();

        Undo.AddComponent<VRC3CVRAvatar>(original);

        // CVRAvatar attaches CVRAssetInfo from its own OnValidate. If VRC3CVRAvatar also requires
        // CVRAssetInfo directly, the dependency resolution can attach a second one.
        Assert.AreEqual(1, original.GetComponents<CVRAssetInfo>().Length,
            "exactly one CVRAssetInfo, no duplicates");
        Assert.AreEqual(1, original.GetComponents<CVRAvatar>().Length,
            "exactly one CVRAvatar, no duplicates");
    }

    [Test]
    public void EnsureSingleAssetInfo_CreatesOneWhenAbsent()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_Bare");
        try
        {
            var info = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.IsNotNull(info);
            Assert.AreEqual(1, go.GetComponents<CVRAssetInfo>().Length);
            Assert.AreEqual(CVRAssetInfo.AssetType.Avatar, info.type);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnsureSingleAssetInfo_LeavesAnExistingValidOneAlone()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_Valid");
        try
        {
            var info = go.AddComponent<CVRAssetInfo>();
            info.type = CVRAssetInfo.AssetType.Avatar;
            info.objectId = "keep-me";

            var result = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.AreSame(info, result, "must not replace an already-valid CVRAssetInfo");
            Assert.AreEqual("keep-me", result.objectId);
            Assert.AreEqual(1, go.GetComponents<CVRAssetInfo>().Length);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnsureSingleAssetInfo_FixesAnInvalidType()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_InvalidType");
        try
        {
            var info = go.AddComponent<CVRAssetInfo>();
            info.type = 0; // Enum starts at 1 ("Starting enums at 1 should be illegal"); 0 is invalid.

            var result = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.AreSame(info, result);
            Assert.AreEqual(CVRAssetInfo.AssetType.Avatar, result.type);
            Assert.AreEqual(1, go.GetComponents<CVRAssetInfo>().Length);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // Best-effort: attempts an AddComponent-family call that Unity may refuse for a
    // [DisallowMultipleComponent] type. Measured on this Unity/CCK version: such refusals throw
    // ArgumentException rather than silently no-op'ing, so this only exists to let the calling test
    // try several techniques in a row without an uncaught exception failing the test outright.
    static void TryAddDuplicate(System.Action attempt)
    {
        try
        {
            attempt();
        }
        catch (System.ArgumentException)
        {
            // Expected refusal; the caller checks GetComponents().Length and tries the next technique.
        }
    }

    // CVRAssetInfo carries [DisallowMultipleComponent]. Measured: a second AddComponent call --
    // whether via Undo.AddComponent, ObjectFactory.AddComponent, or CopyComponent/
    // PasteComponentAsNew -- throws ArgumentException on this Unity/CCK version rather than
    // silently failing. That is exactly what makes the real-world duplicate (built via
    // VRC3CVRAvatar's now-removed RequireComponent(CVRAssetInfo) racing CVRAvatar's own OnValidate
    // inside one dependency-resolution pass) surprising in the first place: no ordinary
    // AddComponent call can reproduce it. Callers must fall back to Assert.Inconclusive when this
    // returns only one component -- the important fact (could an artificial duplicate be produced
    // at all, and how) is still reported rather than silently asserting nothing.
    static CVRAssetInfo[] TryCreateArtificialDuplicate(GameObject go, CVRAssetInfo first)
    {
        if (go.GetComponents<CVRAssetInfo>().Length < 2)
        {
            TryAddDuplicate(() => Undo.AddComponent<CVRAssetInfo>(go));
        }
        if (go.GetComponents<CVRAssetInfo>().Length < 2)
        {
            TryAddDuplicate(() => ObjectFactory.AddComponent<CVRAssetInfo>(go));
        }
        if (go.GetComponents<CVRAssetInfo>().Length < 2)
        {
            TryAddDuplicate(() =>
            {
                ComponentUtility.CopyComponent(first);
                ComponentUtility.PasteComponentAsNew(go);
            });
        }
        return go.GetComponents<CVRAssetInfo>();
    }

    const string InconclusiveDuplicateMessage =
        "Could not artificially create a duplicate CVRAssetInfo: Undo.AddComponent, "
            + "ObjectFactory.AddComponent, and CopyComponent/PasteComponentAsNew all "
            + "throw ArgumentException on this Unity/CCK version when one already "
            + "exists. EnsureSingleAssetInfo's duplicate-collapsing path is untested "
            + "here -- it remains a safety net for the real-world path (RequireComponent "
            + "racing CVRAvatar.OnValidate within one dependency-resolution pass), which "
            + "a single AddComponent call cannot reproduce.";

    [Test]
    public void EnsureSingleAssetInfo_CollapsesArtificialDuplicates_KeepsTheFirstComponent()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_Duplicate");
        try
        {
            var first = go.AddComponent<CVRAssetInfo>();
            first.type = CVRAssetInfo.AssetType.Avatar;
            first.objectId = "existing-content-id";

            var before = TryCreateArtificialDuplicate(go, first);
            if (before.Length < 2)
            {
                Assert.Inconclusive(InconclusiveDuplicateMessage);
            }

            Assert.AreSame(first, before[0], "the component under test must stay first in component order");

            var kept = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.AreSame(first, kept,
                "the CCK caches GetComponent<CVRAssetInfo>() -- the first one -- so that is the one that must survive");
            Assert.AreEqual(1, go.GetComponents<CVRAssetInfo>().Length, "duplicates must be collapsed to one");
            Assert.AreEqual("existing-content-id", kept.objectId,
                "the first component already carried the content id and must keep it");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnsureSingleAssetInfo_FoldsAContentIdKnownOnlyToTheDuplicateIntoTheFirstComponent()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_DuplicateWithIdOnSecond");
        try
        {
            var first = go.AddComponent<CVRAssetInfo>();
            first.type = CVRAssetInfo.AssetType.Avatar;
            // Deliberately left empty here, and only set on the duplicate after it exists (below):
            // CopyComponent/PasteComponentAsNew would otherwise clone whatever id "first" has at
            // copy time, defeating the point of this test.

            var before = TryCreateArtificialDuplicate(go, first);
            if (before.Length < 2)
            {
                Assert.Inconclusive(InconclusiveDuplicateMessage);
            }

            Assert.AreSame(first, before[0], "the component under test must stay first in component order");
            before[1].objectId = "id-known-only-to-the-duplicate";

            var kept = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.AreSame(first, kept,
                "the CCK caches GetComponent<CVRAssetInfo>() -- the first one -- so that is the one that must survive");
            Assert.AreEqual(1, go.GetComponents<CVRAssetInfo>().Length, "duplicates must be collapsed to one");
            Assert.AreEqual("id-known-only-to-the-duplicate", kept.objectId,
                "a content id known only to the removed duplicate must be folded into the survivor, "
                    + "or the next upload would burn a content slot");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnsureSingleAssetInfo_ReportsAndDoesNothingWhenDuplicatesDisagreeOnContentId()
    {
        var go = new GameObject("VRC3CVRCckComponentsTest_ConflictingIds");
        try
        {
            var first = go.AddComponent<CVRAssetInfo>();
            first.type = CVRAssetInfo.AssetType.Avatar;
            first.objectId = "id-a";

            var before = TryCreateArtificialDuplicate(go, first);
            if (before.Length < 2)
            {
                Assert.Inconclusive(InconclusiveDuplicateMessage);
            }

            Assert.AreSame(first, before[0], "the component under test must stay first in component order");
            // A different, equally real content id -- there is no way to tell from here which of
            // the two is the one the CCK/backend actually knows about.
            before[1].objectId = "id-b";

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(go.name)));
            var kept = VRC3CVRCckComponents.EnsureSingleAssetInfo(go, recordUndo: false);

            Assert.AreSame(first, kept);
            Assert.AreEqual(2, go.GetComponents<CVRAssetInfo>().Length,
                "an unresolvable conflict must not destroy anything -- guessing wrong burns a content slot");
            Assert.AreEqual("id-a", first.objectId, "must not be overwritten when the correct id is unknown");
            Assert.AreEqual("id-b", before[1].objectId, "must not be overwritten when the correct id is unknown");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
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

    // With autoBake off and shouldCloneAvatar off, VRC3CVRCore.CreateChilloutAvatar hands back
    // vrcAvatarDescriptor.gameObject itself instead of a clone -- so core.chilloutAvatar IS the
    // user's own scene object. A bare descriptor with no Animator makes SetAnimator throw
    // (GetComponent<Animator>() returns null, then the null is dereferenced). The catch block used
    // to tag that same object EditorOnly and rename it on failure: the user's own avatar would drop
    // out of the CCK Control Panel and get destroyed by the next VRChat/CCK build, with no Undo
    // record to recover it. The catch block no longer touches the object at all on failure; this
    // guards the invariant that a failed conversion never modifies the original avatar.
    [Test]
    public void Convert_WhenConvertingInPlaceFails_DoesNotModifyTheOriginalAvatar()
    {
        var go = new GameObject("VRC3CVRPipelineTest_BareDescriptor");
        original = go;
        var descriptor = go.AddComponent<VRCAvatarDescriptor>();

        var config = new VRC3CVRConvertConfig
        {
            autoBake = false,
            shouldCloneAvatar = false,
            saveAssets = false,
        };

        var result = VRC3CVRPipeline.Convert(descriptor, config);

        Assert.IsFalse(result.succeeded, "a descriptor with no Animator must fail SetAnimator");
        Assert.AreEqual("VRC3CVRPipelineTest_BareDescriptor", go.name,
            "the user's own avatar must not be renamed when there is no clone to fall back to");
        Assert.AreNotEqual("EditorOnly", go.tag,
            "the user's own avatar must not be tagged EditorOnly -- that gets it destroyed by the next build");
    }
}
#endif
