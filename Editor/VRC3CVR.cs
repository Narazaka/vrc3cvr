#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using PeanutTools_VRC3CVR.Localization;
using PeanutTools_VRC3CVR;
using VRC.SDK3.Avatars.Components;

public class VRC3CVR : EditorWindow
{
    class T
    {
        public static istring Description => new istring("Convert your VRChat avatar to ChilloutVR", "VRChatアバターをChilloutVRアバターに変換");
        public static istring Step1 => new istring("Step 1: Select your avatar", "Step 1: アバターを選択");
        public static istring Avatar => new istring("Avatar", "アバター");
        public static istring Step2 => new istring("Step 2: Configure settings", "Step 2: 設定");
        public static istring Step3 => new istring("Step 3: Convert", "Step 3: 変換");
        public static istring CloneAvatar => new istring("Clone avatar", "アバターをクローン");
        public static istring Convert => new istring("Convert", "変換");
        public static istring ConvertDescription => new istring("Clones your original avatar to preserve it", "元のアバターをクローンして変換します");
        public static istring ToeError(bool left) => new istring("You do not have a " + (left ? "left" : "right") + " toe bone configured", $"{(left ? "左足" : "右足")}のつま先のボーンが設定されていません");
        public static istring ToeErrorDescription => new istring("You must configure this before you upload your avatar", "アバターをアップロードする前に設定してください");
    }

    [MenuItem("Tools/VRC3CVR")]
    public static void ShowWindow()
    {
        var window = GetWindow<VRC3CVR>();
        window.titleContent = new GUIContent("VRC3CVR");
        window.minSize = new Vector2(250, 50);
    }

    [SerializeField] VRC3CVRCore _vrc3cvr;
    VRC3CVRCore vrc3cvr
    {
        get
        {
            if (_vrc3cvr == null)
            {
                _vrc3cvr = new VRC3CVRCore();
            }
            return _vrc3cvr;
        }
    }

    Vector2 scrollPosition;
    SerializedObject serializedObject;
    SerializedProperty vrc3cvrProperty;
    SerializedProperty shouldCloneAvatarProperty;

    void OnEnable()
    {
        serializedObject = new SerializedObject(this);
        _vrc3cvr = new VRC3CVRCore();
        vrc3cvrProperty = serializedObject.FindProperty("_vrc3cvr");
        shouldCloneAvatarProperty = vrc3cvrProperty.FindPropertyRelative(nameof(VRC3CVRConvertConfig.shouldCloneAvatar));
    }

    void OnGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width));

        CustomGUI.BoldLabel("VRC3CVR");
        CustomGUI.HelpLabel(T.Description);

        Localization.DrawLocaleSelector();

        CustomGUI.LineGap();

        CustomGUI.HorizontalRule();

        CustomGUI.LineGap();

        CustomGUI.BoldLabel(T.Step1);
        CustomGUI.SmallLineGap();
        vrc3cvr.vrcAvatarDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(T.Avatar, vrc3cvr.vrcAvatarDescriptor, typeof(VRCAvatarDescriptor), true);
        CustomGUI.SmallLineGap();
        CustomGUI.BoldLabel(T.Step2);

        EditorGUILayout.PropertyField(vrc3cvrProperty);

        CustomGUI.BoldLabel(T.Step3);

        CustomGUI.SmallLineGap();

        EditorGUILayout.PropertyField(shouldCloneAvatarProperty, T.CloneAvatar.GUIContent);

        EditorGUI.BeginDisabledGroup(vrc3cvr.GetIsReadyForConvert() == false);
        if (GUILayout.Button(T.Convert))
        {
            vrc3cvr.Convert();
        }
        EditorGUI.EndDisabledGroup();
        CustomGUI.HelpLabel(T.ConvertDescription);

        if (vrc3cvr.animator != null)
        {
            Transform leftToesTransform = vrc3cvr.animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightToesTransform = vrc3cvr.animator.GetBoneTransform(HumanBodyBones.RightToes);

            if (leftToesTransform == null || rightToesTransform == null)
            {
                CustomGUI.SmallLineGap();

                if (leftToesTransform == null)
                {
                    CustomGUI.RenderErrorMessage(T.ToeError(true));
                }
                if (rightToesTransform == null)
                {
                    CustomGUI.RenderErrorMessage(T.ToeError(false));
                }
                CustomGUI.RenderWarningMessage(T.ToeErrorDescription);
            }
        }

        CustomGUI.SmallLineGap();

        CustomGUI.MyLinks("vrc3cvr");

        EditorGUILayout.EndScrollView();
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
