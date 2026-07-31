#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PeanutTools_VRC3CVR.Localization;

// Orchestrates bake -> convert. Holds no UI: callers render whatever this returns.
public static class VRC3CVRPipeline
{
    class T
    {
        public static istring NoAvatar => new istring(
            "Select an avatar first",
            "先にアバターを選択してください");
        public static istring AlreadyRunning => new istring(
            "A conversion is already running",
            "変換がすでに実行中です");
        public static istring LegacyComponent => new istring(
            "This avatar still has the old VRC3CVRNDMF component. Remove it and add VRC3CVR Avatar instead.",
            "このアバターには旧 VRC3CVRNDMF コンポーネントが残っています。削除して VRC3CVR Avatar を付け直してください。");
        public static istring NoDescriptorAfterBake => new istring(
            "The bake result has no VRCAvatarDescriptor. A tool in the build chain removed it.",
            "ベイク結果に VRCAvatarDescriptor がありません。ビルド途中のツールが削除しています。");
    }

    // The pre-rename component type. Only present if an old install survived a package import;
    // looked up by name so this compiles whether or not it exists.
    const string LegacyComponentTypeName = "PeanutTools_VRC3CVR.NDMF.VRC3CVRNDMF";

    static bool isRunning;

    public class Result
    {
        public bool succeeded;
        public GameObject convertedAvatar;
        public string errorMessage;
        public bool usedBake;
    }

    static Type FindLegacyComponentType()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(LegacyComponentTypeName))
            .FirstOrDefault(type => type != null);
    }

    public static string GetConvertBlocker(VRCAvatarDescriptor descriptor)
    {
        if (descriptor == null) return T.NoAvatar;
        if (isRunning) return T.AlreadyRunning;

        var legacyType = FindLegacyComponentType();
        if (legacyType != null && descriptor.GetComponent(legacyType) != null) return T.LegacyComponent;

        return null;
    }

    public static Result Convert(VRCAvatarDescriptor descriptor, VRC3CVRConvertConfig config)
    {
        var blocker = GetConvertBlocker(descriptor);
        if (blocker != null) return new Result { succeeded = false, errorMessage = blocker };

        isRunning = true;
        try
        {
            // Copy so the caller's serialized settings are never mutated by the pipeline.
            var workingConfig = new VRC3CVRConvertConfig();
            workingConfig.CopyFrom(config);

            var target = descriptor;
            var usedBake = false;
            // Read before the bake makes its clone: the baker names its result "<name> (Baked)",
            // and this is what the normal (non-bake) path would have named the conversion output.
            var originalName = descriptor.gameObject.name;

            if (workingConfig.autoBake)
            {
                var bakeResult = VRC3CVRBaker.Bake(descriptor.gameObject, typeof(VRC3CVRAvatar));
                if (!bakeResult.succeeded)
                {
                    return new Result { succeeded = false, errorMessage = bakeResult.errorMessage };
                }
                target = bakeResult.bakedAvatar.GetComponent<VRCAvatarDescriptor>();
                if (target == null)
                {
                    UnityEngine.Object.DestroyImmediate(bakeResult.bakedAvatar);
                    return new Result { succeeded = false, errorMessage = T.NoDescriptorAfterBake };
                }
                usedBake = true;
                // The baker already made the clone; converting in place avoids a second one.
                workingConfig.shouldCloneAvatar = false;
            }

            workingConfig.vrcAvatarDescriptor = target;

            VRC3CVRCore core = null;
            GameObject converted;
            try
            {
                core = VRC3CVRCore.FromConfig(workingConfig);
                core.Convert();
                converted = core.chilloutAvatar;
            }
            catch (Exception exception)
            {
                // Deliberately leaves the clone in the scene: a half-converted avatar is what the
                // user needs to diagnose why the conversion threw. Only bake failures clean up.
                // Tag it EditorOnly so it drops out of the CCK Control Panel listing
                // (CCKAssetInfoManager.IsAssetInfoValid excludes EditorOnly) -- otherwise a failed
                // run leaves something named like a success that is one click from being uploaded.
                if (core != null && core.chilloutAvatar != null)
                {
                    core.chilloutAvatar.tag = "EditorOnly";
                    core.chilloutAvatar.name += " (FAILED)";
                }
                return new Result { succeeded = false, errorMessage = exception.ToString() };
            }

            // With autoBake the baker stripped this before the hooks ran; without it nothing has.
            var leftoverSettings = converted.GetComponent<VRC3CVRAvatar>();
            if (leftoverSettings != null) UnityEngine.Object.DestroyImmediate(leftoverSettings);

            // The CCK Control Panel lists content by looking for a CVRAssetInfo with a valid type
            // (CCKAssetInfoManager.IsAssetInfoValid). CVRAvatar does not require one -- it attaches
            // it from OnValidate. Adding VRC3CVRAvatar was measured to cascade into that, but the
            // path that gets here is different: VRC3CVRCore adds CVRAvatar directly for an avatar
            // that had no VRC3CVRAvatar component, and that path has not been measured. Establish
            // the component rather than depend on the answer -- it is a no-op when it is already
            // there, and it collapses duplicates if more than one ended up on the converted avatar.
            VRC3CVRCckComponents.EnsureSingleAssetInfo(converted, recordUndo: false);

            // The baker's name is an intermediate step's label; the user ships this object.
            if (usedBake) converted.name = originalName + " (ChilloutVR)";

            return new Result { succeeded = true, convertedAvatar = converted, usedBake = usedBake };
        }
        finally
        {
            isRunning = false;
        }
    }
}
#endif
