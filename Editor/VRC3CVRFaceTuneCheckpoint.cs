#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// FaceTune's build pass that strips its own components (RemoveFaceTuneComponentsPass) always runs,
// whether or not the rest of the build produced anything -- so a silent no-op (e.g. it could not
// resolve the face renderer) leaves no trace of its own in the Console, and FaceTune itself discards
// the failure reason (AvatarContextBuilder.TryBuild's `out result`) at every call site. This is what
// notices instead: remember whether FaceTune was on the avatar before the bake, then look for
// FaceTune's own output in the converted animator afterward -- and when that output is missing,
// report a dry run of that same TryBuild call (by reflection, read-only -- verified against
// BlendShapeUtility.GetBlendShapesAndSetWeightToZero, which despite its name only records zeroes into
// a list it returns and never calls SkinnedMeshRenderer.SetBlendShapeWeight) made against the avatar
// before the bake, since both conversion paths can rewrite that avatar in place once baking starts.
public static class VRC3CVRFaceTuneCheckpoint
{
    // FaceTune's runtime components all live directly under this namespace. Checked by name, not by
    // type, so vrc3cvr keeps compiling and running in projects that never installed the package.
    const string FaceTuneNamespace = "Aoyon.FaceTune";
    const string FaceTuneLayerPrefix = "FaceTune: ";
    const string NdmfGeneratedAssetPrefix = "Packages/nadena.dev.ndmf/__Generated/";
    const string AvatarContextBuilderTypeName = "Aoyon.FaceTune.AvatarContextBuilder";
    const string TryBuildMethodName = "TryBuild";

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

    // Split out like IsFaceTuneNamespace above: takes the type/method names as arguments so a test can
    // drive "FaceTune's API is not there" deterministically, without needing a project that lacks it.
    internal static MethodInfo FindPublicStaticMethod(string typeName, string methodName)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName))
            .FirstOrDefault(t => t != null);
        return type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
    }

    // Call this before the bake, alongside WasFaceTunePresent -- both conversion paths can rewrite
    // avatar in place once baking starts, so this is the last point it still matches what FaceTune's
    // own build would have seen. Returns null (skip) when FaceTune's API is missing, its signature no
    // longer matches, or reflection fails for any other reason; never throws.
    public static string DryRunFaceRendererResolution(GameObject avatar)
    {
        if (avatar == null) return null;
        try
        {
            var tryBuild = FindPublicStaticMethod(AvatarContextBuilderTypeName, TryBuildMethodName);
            if (tryBuild == null) return null;

            // Sized from the method itself, not hardcoded, so a FaceTune update that adds another
            // trailing optional parameter doesn't turn into a silent TargetParameterCountException.
            var args = new object[tryBuild.GetParameters().Length];
            args[0] = avatar;
            tryBuild.Invoke(null, args);
            return args[2]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static void CheckConvertedAvatar(bool faceTuneWasPresent, GameObject convertedAvatar, string preBakeDryRunResult = null)
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
                var message = "FaceTune was present on the avatar before conversion, but the converted "
                    + "animator has no \"FaceTune: \" layers -- it likely produced nothing during the build "
                    + "(e.g. it could not resolve the face renderer).";

                if (preBakeDryRunResult != null)
                {
                    message += " A dry run of FaceTune's own face-renderer resolution on the pre-bake "
                        + $"avatar returned: {preBakeDryRunResult}.";
                    if (preBakeDryRunResult == "Success")
                    {
                        message += " That resolution succeeds before the bake, so the failure is specific "
                            + "to the build-time clone state -- suspect a hook earlier in the build chain altering it.";
                    }
                }

                Debug.LogWarning(message);
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
