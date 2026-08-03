#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using PeanutTools_VRC3CVR.Localization;

// Runs every VRChat SDK preprocess hook on a clone of the avatar, which is what applies
// VRCFury, NDMF/Modular Avatar, Avatar Optimizer and every other non-destructive tool.
// VRCFury's own "Build a Test Copy" does exactly this, so this is the supported path.
//
// Deliberately knows nothing about vrc3cvr or the CCK: it takes a VRChat avatar and returns a
// baked VRChat avatar. Components to strip before baking are passed in by the caller.
public static class VRC3CVRBaker
{
    class T
    {
        public static istring HookRejected => new istring(
            "A VRChat SDK preprocess hook rejected the avatar. "
                + "Check the console for the tool that reported it.",
            "VRChat SDK の preprocess フックがアバターを拒否しました。"
                + "どのツールが報告したかはコンソールを確認してください。");
    }

    public class Result
    {
        public bool succeeded;
        public GameObject bakedAvatar;
        public string errorMessage;
    }

    // Indirection purely so tests can drive the failure paths. Registering a real
    // IVRCSDKPreprocessAvatarCallback for that would make TypeCache run it during every actual
    // VRChat build in this project, which is not acceptable. Tests must restore this in TearDown.
    internal static Func<GameObject, bool> preprocessRunner = VRCBuildPipelineCallbacks.OnPreprocessAvatar;

    public static Result Bake(GameObject original, params Type[] componentTypesToRemove)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));

        var clone = UnityEngine.Object.Instantiate(original);
        try
        {
            clone.name = original.name + " (Baked)";
            clone.SetActive(true);
            // A prefab asset has no valid scene, so only move when there is one to move to.
            if (original.scene.IsValid() && clone.scene != original.scene)
            {
                SceneManager.MoveGameObjectToScene(clone, original.scene);
            }
            Undo.RegisterCreatedObjectUndo(clone, "VRC3CVR Bake");

            foreach (var componentType in componentTypesToRemove)
            {
                var component = clone.GetComponent(componentType);
                if (component != null) UnityEngine.Object.DestroyImmediate(component);
            }

            // The only failure signals we trust: the documented false return and a thrown
            // exception. Scanning the log for error strings is unreliable and is not done.
            if (!preprocessRunner(clone))
            {
                UnityEngine.Object.DestroyImmediate(clone);
                return new Result
                {
                    succeeded = false,
                    errorMessage = T.HookRejected,
                };
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return new Result { succeeded = false, errorMessage = exception.ToString() };
        }

        return new Result { succeeded = true, bakedAvatar = clone };
    }

    // Runs the hooks on the object it is given, without cloning. For callers whose framework
    // already made the clone and owns its lifetime — the CCK upload path. Goes through the same
    // preprocessRunner seam as Bake(), so tests can drive its failure paths too.
    public static bool BakeInPlace(GameObject avatar)
    {
        if (avatar == null) throw new ArgumentNullException(nameof(avatar));
        return preprocessRunner(avatar);
    }
}
#endif
