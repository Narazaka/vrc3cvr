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
    class T
    {
        public static istring MissingCvrAvatar => new istring(
            "This avatar has no CVRAvatar component, so the CCK Control Panel will not list it "
                + "and it cannot be uploaded. RequireComponent does not apply retroactively to a "
                + "component restored from an older scene.",
            "このアバターには CVRAvatar コンポーネントがないため、CCK Control Panel に表示されず"
                + "アップロードできません。古いシーンから復元されたコンポーネントには RequireComponent が"
                + "遡って適用されないためです。");
        public static istring AddCvrAvatar => new istring("Add CVRAvatar", "CVRAvatar を追加");
    }

    SerializedProperty convertConfigProperty;

    void OnEnable()
    {
        convertConfigProperty = serializedObject.FindProperty(nameof(VRC3CVRAvatar.convertConfig));
    }

    public override void OnInspectorGUI()
    {
        var avatar = (VRC3CVRAvatar)target;
        var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();

        Localization.DrawLocaleSelector();
        CustomGUI.SmallLineGap();

        if (avatar.GetComponent<CVRAvatar>() == null)
        {
            CustomGUI.RenderWarningMessage(T.MissingCvrAvatar);
            if (GUILayout.Button(T.AddCvrAvatar))
            {
                foreach (var singleTarget in targets)
                {
                    var each = (VRC3CVRAvatar)singleTarget;
                    if (each != null && each.GetComponent<CVRAvatar>() == null)
                    {
                        Undo.AddComponent<CVRAvatar>(each.gameObject);
                    }
                }
            }
            CustomGUI.SmallLineGap();
        }

        VRC3CVRPanel.Draw(serializedObject, convertConfigProperty, avatar.convertConfig, descriptor, avatar);
    }
}
#endif
