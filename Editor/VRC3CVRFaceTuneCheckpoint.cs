#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// FaceTune's build pass that strips its own components (RemoveFaceTuneComponentsPass) always runs,
// whether or not the rest of the build produced anything -- so a silent no-op (e.g. it could not
// resolve the face renderer) leaves no trace of its own in the Console. This is what notices instead:
// remember whether FaceTune was on the avatar before the bake, then look for FaceTune's own output
// in the converted animator afterward.
public static class VRC3CVRFaceTuneCheckpoint
{
    // FaceTune's runtime components all live directly under this namespace. Checked by name, not by
    // type, so vrc3cvr keeps compiling and running in projects that never installed the package.
    const string FaceTuneNamespace = "Aoyon.FaceTune";
    const string FaceTuneLayerPrefix = "FaceTune: ";
    const string NdmfGeneratedAssetPrefix = "Packages/nadena.dev.ndmf/__Generated/";

    public static bool WasFaceTunePresent(GameObject avatar)
    {
        return avatar.GetComponentsInChildren<Component>(true)
            .Any(component => component != null && IsFaceTuneNamespace(component.GetType().Namespace));
    }

    internal static bool IsFaceTuneNamespace(string typeNamespace)
    {
        return typeNamespace == FaceTuneNamespace
            || (typeNamespace != null && typeNamespace.StartsWith(FaceTuneNamespace + "."));
    }

    public static void CheckConvertedAvatar(bool faceTuneWasPresent, GameObject convertedAvatar)
    {
        if (!faceTuneWasPresent) return;

        // A diagnostic must never be the reason a conversion (or upload) fails: catch everything,
        // including future Unity API changes this was never measured against, and only warn.
        try
        {
            var controller = GetAnimatorController(convertedAvatar);
            var layers = controller != null ? controller.layers : new AnimatorControllerLayer[0];
            var faceTuneLayers = layers.Where(layer => layer.name.StartsWith(FaceTuneLayerPrefix)).ToList();

            if (faceTuneLayers.Count == 0)
            {
                Debug.LogWarning("FaceTune was present on the avatar before conversion, but the converted "
                    + "animator has no \"FaceTune: \" layers -- it likely produced nothing during the build "
                    + "(e.g. it could not resolve the face renderer).");
                return;
            }

            foreach (var layer in faceTuneLayers)
            {
                foreach (var childState in layer.stateMachine.states)
                {
                    var state = childState.state;
                    var motion = state.motion;
                    if (motion == null)
                    {
                        Debug.LogWarning($"FaceTune layer \"{layer.name}\" state \"{state.name}\" has no Motion "
                            + "in the converted animator -- the generated clip did not survive the conversion.");
                        continue;
                    }

                    var path = AssetDatabase.GetAssetPath(motion);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith(NdmfGeneratedAssetPrefix))
                    {
                        Debug.LogWarning($"FaceTune layer \"{layer.name}\" state \"{state.name}\" still "
                            + $"references a temporary NDMF asset ({path}) -- the expression will disappear "
                            + "once that gets cleaned up.");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"VRC3CVR: the FaceTune conversion checkpoint itself failed ({exception.Message}); skipping it rather than blocking the conversion.");
        }
    }

    static AnimatorController GetAnimatorController(GameObject avatar)
    {
        var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
        if (animator == null) return null;

        switch (animator.runtimeAnimatorController)
        {
            case AnimatorController controller: return controller;
            case AnimatorOverrideController overrideController: return overrideController.runtimeAnimatorController as AnimatorController;
            default: return null;
        }
    }
}
#endif
