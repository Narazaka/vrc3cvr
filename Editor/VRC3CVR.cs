#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using PeanutTools_VRC3CVR.Localization;
using PeanutTools_VRC3CVR;
using VRC.SDK3.Avatars.Components;

// A thin proxy over VRC3CVRAvatar. When the selected avatar has that component this window edits
// it directly, so there is never a second copy of the settings that could disagree with it.
public class VRC3CVR : EditorWindow
{
    class T
    {
        public static istring Description => new istring(
            "Convert your VRChat avatar to ChilloutVR",
            "VRChatアバターをChilloutVRアバターに変換");
        public static istring Step1 => new istring("Step 1: Select your avatar", "Step 1: アバターを選択");
        public static istring Avatar => new istring("Avatar", "アバター");
        public static istring EditingComponent => new istring(
            "Editing the VRC3CVR Avatar component on this avatar",
            "このアバターの VRC3CVR Avatar コンポーネントを編集しています");
    }

    [MenuItem("Tools/VRC3CVR")]
    public static void ShowWindow()
    {
        var window = GetWindow<VRC3CVR>();
        window.titleContent = new GUIContent("VRC3CVR");
        window.minSize = new Vector2(250, 50);
    }

    // Used only while the selected avatar has no VRC3CVRAvatar component.
    [SerializeField] VRC3CVRConvertConfig fallbackConfig = new VRC3CVRConvertConfig();
    [SerializeField] VRCAvatarDescriptor selectedAvatar;

    Vector2 scrollPosition;
    SerializedObject windowSerializedObject;
    SerializedObject componentSerializedObject;
    VRC3CVRAvatar boundComponent;

    void OnEnable()
    {
        windowSerializedObject = new SerializedObject(this);
    }

    // Rebuilt whenever the avatar (and therefore the component to edit) changes.
    void BindTo(VRC3CVRAvatar component)
    {
        boundComponent = component;
        componentSerializedObject = component != null ? new SerializedObject(component) : null;
    }

    void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width));

        CustomGUI.BoldLabel("VRC3CVR");
        CustomGUI.HelpLabel(T.Description);
        Localization.DrawLocaleSelector();
        CustomGUI.LineGap();
        CustomGUI.HorizontalRule();
        CustomGUI.LineGap();

        CustomGUI.BoldLabel(T.Step1);
        CustomGUI.SmallLineGap();
        var pickedAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
            T.Avatar, selectedAvatar, typeof(VRCAvatarDescriptor), true);
        if (pickedAvatar != selectedAvatar)
        {
            selectedAvatar = pickedAvatar;
            // The object picker and drag-and-drop both commit during an event that has no Layout
            // pass of its own (ExecuteCommand / DragPerform). Switching avatars changes how many
            // GUILayout entries the rest of this window wants (the component-bound panel below
            // draws different controls than the fallback one), which throws "Getting control N's
            // position in a group with only N controls" if we keep drawing in this same event.
            // Bail out and let the next event lay the window out from scratch.
            EditorGUILayout.EndScrollView();
            GUIUtility.ExitGUI();
        }
        CustomGUI.SmallLineGap();

        var component = selectedAvatar != null ? selectedAvatar.GetComponent<VRC3CVRAvatar>() : null;
        if (component != boundComponent) BindTo(component);

        if (component != null)
        {
            CustomGUI.HelpLabel(T.EditingComponent);
            VRC3CVRPanel.Draw(
                componentSerializedObject,
                componentSerializedObject.FindProperty(nameof(VRC3CVRAvatar.convertConfig)),
                component.convertConfig,
                selectedAvatar,
                component);
        }
        else
        {
            VRC3CVRPanel.Draw(
                windowSerializedObject,
                windowSerializedObject.FindProperty(nameof(fallbackConfig)),
                fallbackConfig,
                selectedAvatar,
                null);
        }

        CustomGUI.SmallLineGap();
        CustomGUI.MyLinks("vrc3cvr");

        EditorGUILayout.EndScrollView();
    }
}
#endif
