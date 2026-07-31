#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using UnityEngine;
using CVR.CCKEditor.ContentBuilder;
using PeanutTools_VRC3CVR.Localization;
using VRC.SDK3.Avatars.Components;

// Converts a VRChat avatar to ChilloutVR while the CCK is uploading it, so the user just presses
// upload in the CCK Control Panel. This mirrors how Modular Avatar and VRCFury hook the VRChat
// SDK build: the framework clones the avatar and hands it over, the tool rewrites it in place.
//
// The CCK clones into TempBuildAsset.RootObject and disposes of it afterwards, so nothing here
// creates or destroys the object. CCK's own validation runs after this callback, which is what
// makes converting here legal at all.
public class VRC3CVRBuildProcessor : CCKBuildProcessor
{
    class T
    {
        public static istring BakeFailed => new istring(
            "A VRChat SDK preprocess hook rejected the avatar during the VRC3CVR bake. "
                + "Check the console for the tool that reported it.",
            "VRC3CVR のベイク中に VRChat SDK の preprocess フックがアバターを拒否しました。"
                + "どのツールが報告したかはコンソールを確認してください。");
        public static istring NoDescriptorAfterBake => new istring(
            "The bake removed the VRCAvatarDescriptor, so there is nothing left to convert for "
                + "this upload. A tool in the build chain is misconfigured.",
            "ベイクによって VRCAvatarDescriptor が失われ、このアップロードで変換するものが"
                + "なくなりました。ビルド途中のツールの設定に問題があります。");
    }

    public override void OnPreProcessAvatar(GameObject avatar)
    {
        var settings = avatar.GetComponent<VRC3CVRAvatar>();
        // Not managed by vrc3cvr: an already-converted ChilloutVR avatar being re-uploaded.
        if (settings == null) return;
        // No VRChat side left to convert.
        if (avatar.GetComponent<VRCAvatarDescriptor>() == null) return;

        var config = new VRC3CVRConvertConfig();
        config.CopyFrom(settings.convertConfig);

        // Settings are authoring data; they must not reach the uploaded bundle. Removing it before
        // the bake also keeps it away from tools that strip unknown components.
        UnityEngine.Object.DestroyImmediate(settings);

        if (config.autoBake)
        {
            // The CCK build does not run VRChat's hooks, so ask for them explicitly. Unlike the
            // manual path there is no clone to make: the SDK contract is to rewrite what it is given.
            if (!VRC3CVRBaker.BakeInPlace(avatar))
            {
                throw new Exception(T.BakeFailed);
            }
        }

        var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
        if (descriptor == null) throw new Exception(T.NoDescriptorAfterBake);

        config.vrcAvatarDescriptor = descriptor;
        // The CCK already owns this object; a clone here would be uploaded empty.
        config.shouldCloneAvatar = false;
        // PrefabUtility.SaveAsPrefabAsset cannot serialize a reference to an AnimatorOverrideController
        // that only lives in memory, so the animator has to be written to disk or the uploaded avatar
        // has none at all. Not the user's choice on this path.
        config.saveAssets = true;
        VRC3CVRCore.FromConfig(config).Convert();

        // Defensive: by this point CVRAvatar is required by VRC3CVRAvatar and the CCK already
        // rejected anything whose type is not Avatar, so this is normally a no-op. It costs nothing
        // and keeps the invariant explicit for a caller that reaches here another way. Also
        // collapses duplicates: CVRAssetInfo is [DisallowMultipleComponent], but duplicates have
        // been observed anyway, and picking the wrong one mid-upload would burn a content slot.
        VRC3CVRCckComponents.EnsureSingleAssetInfo(avatar, recordUndo: false);
    }
}
#endif
