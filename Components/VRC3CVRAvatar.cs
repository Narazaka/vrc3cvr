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
// CVRAvatar is required because the CCK Control Panel only lists content that has one
// (ContentBuildPipeline.ValidateCommonParameters throws when there is none), and uploading from
// there is how the conversion runs. CVRAvatar in turn brings CVRAssetInfo, which is where the CVR
// content id lives, so the id rides along on the source avatar without vrc3cvr managing it.
[RequireComponent(typeof(CVRAvatar))]
[AddComponentMenu("VRC3CVR/VRC3CVR Avatar")]
public class VRC3CVRAvatar : MonoBehaviour
{
    public VRC3CVRConvertConfig convertConfig = new VRC3CVRConvertConfig();
}
#endif
