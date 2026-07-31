#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEngine;
using ABI.CCK.Components;

// Holds every VRC3CVR setting for one avatar. Logic lives in VRC3CVRPipeline and
// VRC3CVRBuildProcessor; this is data only.
//
// This file deliberately sits outside Runtime/ because that folder has VRC3CVR.Runtime.asmdef,
// and an asmdef assembly cannot reference the predefined Assembly-CSharp where the CCK lives.
// Being outside any asmdef lets it see both the CCK and VRC3CVR.Runtime (autoReferenced).
//
// CVRAvatar is required because uploading from the CCK Control Panel is how the conversion runs,
// and the CCK rejects a build with no CVRAvatar (ContentBuildPipeline.ValidateCommonParameters).
// Adding it through the inspector also triggers CVRAvatar.OnValidate, which attaches the
// CVRAssetInfo that actually decides whether the avatar is listed in the panel, and which holds
// the CVR content id -- so the id rides along on the source avatar without vrc3cvr managing it.
// Code paths that add CVRAvatar themselves must attach CVRAssetInfo explicitly; OnValidate does
// not run for them.
[RequireComponent(typeof(CVRAvatar))]
[DisallowMultipleComponent]
[AddComponentMenu("VRC3CVR/VRC3CVR Avatar")]
public class VRC3CVRAvatar : MonoBehaviour
{
    public VRC3CVRConvertConfig convertConfig = new VRC3CVRConvertConfig();
}
#endif
