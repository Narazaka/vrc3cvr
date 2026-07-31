#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using PeanutTools_VRC3CVR;
using PeanutTools_VRC3CVR.Localization;
using VRC.SDK3.Avatars.Components;

// The one place the conversion UI is drawn. Both the Tools -> VRC3CVR window and the
// VRC3CVRAvatar inspector call this, so the two can never drift apart.
//
// autoBake and shouldCloneAvatar are drawn here rather than in VRC3CVRConvertConfigDrawer:
// that drawer lists its fields by hand and deliberately leaves these two to the caller.
public static class VRC3CVRPanel
{
    class T
    {
        public static istring Settings => new istring("Step 2: Configure settings", "Step 2: 設定");
        public static istring ConvertStep => new istring("Step 3: Convert", "Step 3: 変換");
        public static istring AutoBake => new istring("Auto bake", "自動ベイク");
        public static istring AutoBakeDescription => new istring(
            "Applies VRCFury, Modular Avatar, Avatar Optimizer and other non-destructive tools "
                + "before converting, by running the VRChat SDK build hooks on a clone",
            "変換前に VRCFury・Modular Avatar・Avatar Optimizer などの非破壊ツールを適用します"
                + "（クローンに対して VRChat SDK のビルドフックを実行します）");
        public static istring CloneAvatar => new istring("Clone avatar", "アバターをクローン");
        public static istring CloneForcedByBake => new istring(
            "Auto bake always works on a clone",
            "自動ベイク時は常にクローンされます");
        public static istring Convert => new istring("Convert", "変換");
        public static istring ConvertDescription => new istring(
            "Clones your original avatar to preserve it",
            "元のアバターをクローンして変換します");
        public static istring SaveSettings => new istring("Save settings to the avatar", "設定をアバターに保存");
        public static istring SaveSettingsDescription => new istring(
            "Adds a VRC3CVR Avatar component so these settings travel with the avatar",
            "VRC3CVR Avatar コンポーネントを追加し、設定をアバターと一緒に持ち運べるようにします");
        public static istring VolatileBakeResult => new istring(
            "Bake generated assets live in a temporary folder and are destroyed by the next build. "
                + "Upload this result rather than keeping it.",
            "ベイクで生成されたアセットは一時フォルダにあり、次回ビルドで消えます。"
                + "この結果は保存用ではなく、アップロードして使ってください。");
        public static istring ToeError(bool left) => new istring(
            "You do not have a " + (left ? "left" : "right") + " toe bone configured",
            $"{(left ? "左足" : "右足")}のつま先のボーンが設定されていません");
        public static istring ToeErrorDescription => new istring(
            "You must configure this before you upload your avatar",
            "アバターをアップロードする前に設定してください");
    }

    public static void Draw(
        SerializedObject serializedObject,
        SerializedProperty convertConfigProperty,
        VRC3CVRConvertConfig config,
        VRCAvatarDescriptor descriptor,
        VRC3CVRAvatar component)
    {
        serializedObject.UpdateIfRequiredOrScript();

        CustomGUI.BoldLabel(T.Settings);
        EditorGUILayout.PropertyField(convertConfigProperty);

        CustomGUI.SmallLineGap();
        CustomGUI.BoldLabel(T.ConvertStep);
        CustomGUI.SmallLineGap();

        var cloneProperty = convertConfigProperty.FindPropertyRelative(nameof(VRC3CVRConvertConfig.shouldCloneAvatar));
        var autoBakeProperty = convertConfigProperty.FindPropertyRelative(nameof(VRC3CVRConvertConfig.autoBake));

        // Read before the field: PropertyField commits the new value during MouseDown, and a
        // GUILayout entry that appears in the same event that the Layout pass did not count
        // throws "Getting control N's position in a group with only N controls".
        var autoBakeWasOn = autoBakeProperty.boolValue;

        EditorGUILayout.PropertyField(autoBakeProperty, T.AutoBake.GUIContent);
        CustomGUI.HelpLabel(T.AutoBakeDescription);

        // While baking the clone is made by VRC3CVRBaker, so this toggle has no effect.
        EditorGUI.BeginDisabledGroup(autoBakeWasOn);
        EditorGUILayout.PropertyField(cloneProperty, T.CloneAvatar.GUIContent);
        EditorGUI.EndDisabledGroup();
        if (autoBakeWasOn)
        {
            CustomGUI.HelpLabel(T.CloneForcedByBake);
        }

        serializedObject.ApplyModifiedProperties();

        if (component == null)
        {
            CustomGUI.SmallLineGap();
            if (GUILayout.Button(T.SaveSettings) && descriptor != null)
            {
                var added = Undo.AddComponent<VRC3CVRAvatar>(descriptor.gameObject);
                added.convertConfig.CopyFrom(config);
                added.convertConfig.vrcAvatarDescriptor = null;
                EditorUtility.SetDirty(added);
            }
            CustomGUI.HelpLabel(T.SaveSettingsDescription);
        }

        CustomGUI.SmallLineGap();

        var blocker = VRC3CVRPipeline.GetConvertBlocker(descriptor);
        if (blocker != null)
        {
            CustomGUI.RenderWarningMessage(blocker);
        }

        EditorGUI.BeginDisabledGroup(blocker != null);
        if (GUILayout.Button(T.Convert))
        {
            RunConvert(descriptor, config);
        }
        EditorGUI.EndDisabledGroup();
        CustomGUI.HelpLabel(T.ConvertDescription);

        DrawToeWarnings(descriptor);
    }

    static void RunConvert(VRCAvatarDescriptor descriptor, VRC3CVRConvertConfig config)
    {
        var result = VRC3CVRPipeline.Convert(descriptor, config);
        if (!result.succeeded)
        {
            Debug.LogError("VRC3CVR: " + result.errorMessage);
            return;
        }
        Selection.activeGameObject = result.convertedAvatar;
        if (result.usedBake)
        {
            // No asset rehoming is done, so say plainly that this output is not for keeping.
            Debug.LogWarning("VRC3CVR: " + (string)T.VolatileBakeResult);
        }
    }

    static void DrawToeWarnings(VRCAvatarDescriptor descriptor)
    {
        if (descriptor == null) return;
        var animator = descriptor.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return;

        var leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        var rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
        if (leftToes != null && rightToes != null) return;

        CustomGUI.SmallLineGap();
        if (leftToes == null) CustomGUI.RenderErrorMessage(T.ToeError(true));
        if (rightToes == null) CustomGUI.RenderErrorMessage(T.ToeError(false));
        CustomGUI.RenderWarningMessage(T.ToeErrorDescription);
    }
}
#endif
