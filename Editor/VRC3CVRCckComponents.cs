#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using ABI.CCK.Components;

// CVRAssetInfo carries the CVR content id and decides whether the avatar is listed in the CCK
// Control Panel. It is marked [DisallowMultipleComponent], but duplicates have been observed in
// practice -- the exact path is not known (undo, prefab overrides, or CVRAvatar's own OnValidate
// racing Unity's RequireComponent resolution). Duplicates are not cosmetic: the CCK reads a single
// component, so grabbing the copy without the id would upload as a brand new avatar and burn a
// content slot. Everything that touches CVRAssetInfo goes through here.
public static class VRC3CVRCckComponents
{
    // Returns the one CVRAssetInfo the avatar should have, creating it when absent and collapsing
    // duplicates into the one that carries a content id.
    public static CVRAssetInfo EnsureSingleAssetInfo(GameObject target, bool recordUndo)
    {
        var all = target.GetComponents<CVRAssetInfo>();

        if (all.Length == 0)
        {
            var created = recordUndo
                ? Undo.AddComponent<CVRAssetInfo>(target)
                : target.AddComponent<CVRAssetInfo>();
            created.type = CVRAssetInfo.AssetType.Avatar;
            return created;
        }

        // Prefer whichever one already knows the content id; losing it means the next upload
        // creates a new avatar instead of updating the existing one.
        var keep = all.FirstOrDefault(info => !string.IsNullOrEmpty(info.objectId)) ?? all[0];

        foreach (var extra in all)
        {
            if (extra == keep) continue;
            Debug.LogWarning(
                "VRC3CVR: removed a duplicate CVRAssetInfo from " + target.name
                    + ". Only one can be used, and the wrong one would have uploaded as a new avatar.",
                target);
            if (recordUndo) Undo.DestroyObjectImmediate(extra);
            else Object.DestroyImmediate(extra);
        }

        if (keep.type != CVRAssetInfo.AssetType.Avatar)
        {
            if (recordUndo) Undo.RecordObject(keep, "Set CVR asset type");
            keep.type = CVRAssetInfo.AssetType.Avatar;
            EditorUtility.SetDirty(keep);
        }

        return keep;
    }
}
#endif
