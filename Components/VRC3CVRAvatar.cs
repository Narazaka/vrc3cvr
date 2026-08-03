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
// CVRAssetInfo -- not CVRAvatar -- is what decides whether the avatar is listed in the panel at all
// (CCKAssetInfoManager.IsAssetInfoValid), and it holds the CVR content id, so the id rides along on
// the source avatar without vrc3cvr managing it. It is deliberately not required here: CVRAvatar
// already attaches it from its own OnValidate/Reset (measured), and requiring it a second time here
// let Unity's RequireComponent dependency resolution race that OnValidate and attach a duplicate
// CVRAssetInfo despite its own [DisallowMultipleComponent] (observed in practice). Anything that
// still needs to guarantee CVRAssetInfo exists should go through VRC3CVRCckComponents, which also
// collapses duplicates if Unity manages to create one anyway.
[RequireComponent(typeof(CVRAvatar))]
[DisallowMultipleComponent]
[AddComponentMenu("VRC3CVR/VRC3CVR Avatar")]
public class VRC3CVRAvatar : MonoBehaviour
{
    public VRC3CVRConvertConfig convertConfig = new VRC3CVRConvertConfig();
}
#endif
