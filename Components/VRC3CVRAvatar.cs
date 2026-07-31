#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEngine;
using ABI.CCK.Components;

// Holds every VRC3CVR setting for one avatar. Logic lives in VRC3CVRPipeline; this is data only.
//
// This file deliberately sits outside Runtime/ because that folder has VRC3CVR.Runtime.asmdef,
// and an asmdef assembly cannot reference the predefined Assembly-CSharp where the CCK lives.
// Being outside any asmdef lets it see both the CCK and VRC3CVR.Runtime (autoReferenced).
//
// CVRAssetInfo is required because it is where the CVR content id (objectId) is stored: the
// conversion clone is disposable, so the id has to live on the source avatar and ride along
// through Instantiate. On this object CVRAssetInfo.type stays 0 (invalid) because CVRAvatar is
// absent, which keeps the un-converted avatar out of the CCK Control Panel listing.
[RequireComponent(typeof(CVRAssetInfo))]
[AddComponentMenu("VRC3CVR/VRC3CVR Avatar")]
public class VRC3CVRAvatar : MonoBehaviour
{
    public VRC3CVRConvertConfig convertConfig = new VRC3CVRConvertConfig();
}
#endif
