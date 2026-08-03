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
    // duplicates down to the first one.
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

        // The CCK caches GetComponent<CVRAssetInfo>() -- the first one -- and reads it after the
        // build processors run, so destroying that particular component makes the upload throw and
        // skips the CCK's own cleanup. Always keep the first and fold anything the others carry
        // into it, rather than picking by content id.
        var keep = all[0];

        if (all.Length > 1)
        {
            var distinctIds = all
                .Select(info => info.objectId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToArray();
            if (distinctIds.Length > 1)
            {
                // Every candidate id is real and different -- there is no way to tell which one is
                // correct from here, and picking wrong burns a content slot on the next upload.
                // Leave everything alone rather than guess.
                Debug.LogError(
                    "VRC3CVR: " + target.name + " has multiple CVRAssetInfo components with different "
                        + "content ids (" + string.Join(", ", distinctIds) + "). Not sure which one is "
                        + "correct, so none were changed or removed -- resolve this manually.",
                    target);
                return keep;
            }

            foreach (var extra in all)
            {
                if (extra == keep) continue;

                // Do not lose a content id that only the duplicate knows: without it the next
                // upload creates a brand new avatar and burns a content slot.
                if (string.IsNullOrEmpty(keep.objectId) && !string.IsNullOrEmpty(extra.objectId))
                {
                    if (recordUndo) Undo.RecordObject(keep, "Keep CVR content id");
                    keep.objectId = extra.objectId;
                    EditorUtility.SetDirty(keep);
                }

                Debug.LogWarning(
                    "VRC3CVR: removed a duplicate CVRAssetInfo from " + target.name
                        + ". The CCK always reads the first CVRAssetInfo component, so that one is kept "
                        + "and the extra is removed.",
                    target);
                if (recordUndo) Undo.DestroyObjectImmediate(extra);
                else Object.DestroyImmediate(extra);
            }
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
