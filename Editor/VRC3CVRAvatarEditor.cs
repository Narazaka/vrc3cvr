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
        EnsureAssetInfo();
    }

    // RequireComponent only fires when the component is added through the inspector, so a
    // VRC3CVRAvatar restored from an older scene can be missing its CVRAssetInfo.
    void EnsureAssetInfo()
    {
        foreach (var singleTarget in targets)
        {
            var avatar = (VRC3CVRAvatar)singleTarget;
            if (avatar == null) continue;
            if (avatar.GetComponent<CVRAssetInfo>() == null)
            {
                Undo.AddComponent<CVRAssetInfo>(avatar.gameObject);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        EditorGUILayout.PropertyField(convertConfigProperty);
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
