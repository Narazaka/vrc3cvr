#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;

// The icon on the component is there to be found in the inspector, not to be drawn over the avatar
// in the scene: an avatar carries one of these and would wear its icon wherever its root is. Unity
// turns a script's icon on in the scene as soon as the script has one, so it is turned back off
// here, once per load -- which is also to say that turning it on again lasts until the next one.
[InitializeOnLoad]
static class VRC3CVRGizmoIcons
{
    static VRC3CVRGizmoIcons()
    {
        // not during the static constructor itself: the gizmo settings are not up yet that early
        EditorApplication.delayCall += Hide;
    }

    static void Hide()
    {
        EditorApplication.delayCall -= Hide;
        GizmoUtility.SetIconEnabled(typeof(VRC3CVRAvatar), false);
    }
}
#endif
