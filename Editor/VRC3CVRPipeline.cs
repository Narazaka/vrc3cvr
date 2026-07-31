#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEngine;
using ABI.CCK.Components;
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

            var core = VRC3CVRCore.FromConfig(workingConfig);
            GameObject converted;
            try
            {
                core.Convert();
                converted = core.chilloutAvatar;
            }
            catch (Exception exception)
            {
                // Deliberately leaves the clone in the scene: a half-converted avatar is what the
                // user needs to diagnose why the conversion threw. Only bake failures clean up.
                return new Result { succeeded = false, errorMessage = exception.ToString() };
            }

            // With autoBake the baker stripped this before the hooks ran; without it nothing has.
            var leftoverSettings = converted.GetComponent<VRC3CVRAvatar>();
            if (leftoverSettings != null) UnityEngine.Object.DestroyImmediate(leftoverSettings);

            // AddComponent<CVRAvatar>() from code runs neither Reset nor OnValidate, so the type
            // the CCK would normally set is still 0 (invalid) here.
            var assetInfo = converted.GetComponent<CVRAssetInfo>();
            if (assetInfo != null) assetInfo.type = CVRAssetInfo.AssetType.Avatar;

            return new Result { succeeded = true, convertedAvatar = converted, usedBake = usedBake };
        }
        finally
        {
            isRunning = false;
        }
    }
}
#endif
