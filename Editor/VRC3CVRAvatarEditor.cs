#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;
using PeanutTools_VRC3CVR;
using PeanutTools_VRC3CVR.Localization;
using VRC.SDK3.Avatars.Components;

[CustomEditor(typeof(VRC3CVRAvatar))]
public class VRC3CVRAvatarEditor : Editor
{
    SerializedProperty convertConfigProperty;

    void OnEnable()
    {
        convertConfigProperty = serializedObject.FindProperty(nameof(VRC3CVRAvatar.convertConfig));
        EnsureCckComponents();
    }

    // RequireComponent does not apply retroactively, so a VRC3CVRAvatar restored from a scene or
    // prefab authored before those attributes existed has neither component. Without them the
    // avatar is not listed in the CCK Control Panel and cannot be uploaded at all, so attach them
    // rather than asking the user to. This only ever fires once per migrated avatar.
    void EnsureCckComponents()
    {
        foreach (var singleTarget in targets)
        {
            var avatar = (VRC3CVRAvatar)singleTarget;
            if (avatar == null) continue;
            if (avatar.GetComponent<CVRAvatar>() == null)
            {
                Undo.AddComponent<CVRAvatar>(avatar.gameObject);
            }
            // CVRAvatar only attaches CVRAssetInfo from OnValidate, which Undo.AddComponent does
            // not run, and CVRAssetInfo is what actually decides whether the avatar is listed
            // (CCKAssetInfoManager.IsAssetInfoValid).
            if (avatar.GetComponent<CVRAssetInfo>() == null)
            {
                Undo.AddComponent<CVRAssetInfo>(avatar.gameObject).type = CVRAssetInfo.AssetType.Avatar;
            }
        }
    }

    public override void OnInspectorGUI()
    {
        var avatar = (VRC3CVRAvatar)target;
        var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();

        Localization.DrawLocaleSelector();
        CustomGUI.SmallLineGap();

        VRC3CVRPanel.Draw(serializedObject, convertConfigProperty, avatar.convertConfig, descriptor, avatar);
    }
}
#endif
