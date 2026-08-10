#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;

public class VRC3CVRFaceTuneCheckpointTests
{
    GameObject avatar;
    GameObject snapshot;

    [TearDown]
    public void TearDown()
    {
        if (avatar != null) Object.DestroyImmediate(avatar);
        if (snapshot != null) Object.DestroyImmediate(snapshot);
    }

    // ---- IsFaceTuneNamespace ----
    // A dummy component cannot be declared under FaceTune's real namespace without depending on the
    // package, so the detection logic is tested through the namespace string it matches against.

    [Test]
    public void IsFaceTuneNamespace_MatchesFaceTuneAndItsSubNamespaces()
    {
        Assert.IsTrue(VRC3CVRFaceTuneCheckpoint.IsFaceTuneNamespace("Aoyon.FaceTune"));
        Assert.IsTrue(VRC3CVRFaceTuneCheckpoint.IsFaceTuneNamespace("Aoyon.FaceTune.Build"));
    }

    [Test]
    public void IsFaceTuneNamespace_RejectsUnrelatedOrNullNamespaces()
    {
        Assert.IsFalse(VRC3CVRFaceTuneCheckpoint.IsFaceTuneNamespace("Aoyon.FaceTuneAssistant"));
        Assert.IsFalse(VRC3CVRFaceTuneCheckpoint.IsFaceTuneNamespace("SomeOther.Namespace"));
        Assert.IsFalse(VRC3CVRFaceTuneCheckpoint.IsFaceTuneNamespace(null));
    }

    // ---- CheckConvertedAvatar ----

    static AnimatorController MakeControllerWithLayer(string layerName, params (string state, Motion motion)[] states)
    {
        var controller = new AnimatorController { name = "FaceTuneCheckpointTest" };
        controller.AddLayer(layerName);
        var machine = controller.layers[0].stateMachine;
        foreach (var (state, motion) in states)
        {
            machine.AddState(state).motion = motion;
        }
        return controller;
    }

    GameObject MakeAvatar(AnimatorController controller)
    {
        avatar = new GameObject("FaceTuneCheckpointTest_Avatar");
        avatar.AddComponent<Animator>().runtimeAnimatorController = controller;
        return avatar;
    }

    [Test]
    public void CheckConvertedAvatar_SilentWhenFaceTuneWasNotPresent()
    {
        MakeAvatar(MakeControllerWithLayer("Locomotion"));

        var warned = false;
        void Handler(string message, string stackTrace, LogType type) => warned |= type == LogType.Warning;
        Application.logMessageReceived += Handler;
        try
        {
            VRC3CVRFaceTuneCheckpoint.CheckConvertedAvatar(false, avatar);
        }
        finally
        {
            Application.logMessageReceived -= Handler;
        }

        Assert.IsFalse(warned, "no FaceTune components ever existed, so the checkpoint must not run at all");
    }

    [Test]
    public void CheckConvertedAvatar_WarnsWhenNoFaceTuneLayerSurvivedConversion()
    {
        MakeAvatar(MakeControllerWithLayer("Locomotion"));

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "FaceTune was present on the avatar before conversion, but the converted animator has no "
                + "\"FaceTune: \" layers -- it likely produced nothing during the build "
                + "(e.g. it could not resolve the face renderer).")));

        VRC3CVRFaceTuneCheckpoint.CheckConvertedAvatar(true, avatar);
    }

    [Test]
    public void CheckConvertedAvatar_WarnsWhenAFaceTuneStateHasNoMotion()
    {
        MakeAvatar(MakeControllerWithLayer("FaceTune: Expressions", ("Idle", null)));

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "FaceTune layer \"FaceTune: Expressions\" state \"Idle\" has no Motion "
                + "in the converted animator -- the generated clip did not survive the conversion.")));

        VRC3CVRFaceTuneCheckpoint.CheckConvertedAvatar(true, avatar);
    }

    // ---- FindPublicStaticMethod (dry run's reflection lookup) ----
    // Same reasoning as IsFaceTuneNamespace above: driven by an explicit type name so "FaceTune's API
    // is not there" is reachable without a project that actually lacks the package.

    [Test]
    public void FindPublicStaticMethod_ReturnsNullWhenTypeNameDoesNotExist()
    {
        Assert.IsNull(VRC3CVRFaceTuneCheckpoint.FindPublicStaticMethod("Aoyon.FaceTune.NoSuchType", "TryBuild"));
    }

    [Test]
    public void FindPublicStaticMethod_ReturnsNullWhenMethodNameDoesNotExist()
    {
        Assert.IsNull(VRC3CVRFaceTuneCheckpoint.FindPublicStaticMethod("Aoyon.FaceTune.AvatarContextBuilder", "NoSuchMethod"));
    }

    // ---- CheckConvertedAvatar's dry-run enrichment (E-series warning only) ----
    // These run against the real FaceTune package installed in this project (see task context), not a
    // fake -- the two fixtures below are chosen to land on deterministic AvatarContextBuildResult values.

    [Test]
    public void CheckConvertedAvatar_WarnsWithDryRunResultWhenSnapshotCannotResolveAnAvatarRoot()
    {
        MakeAvatar(MakeControllerWithLayer("Locomotion"));
        // No VRCAvatarDescriptor (or any other registered avatar-root type) anywhere on it, so
        // FaceTune's own AvatarContextBuilder.TryBuild fails at its first check.
        snapshot = new GameObject("FaceTuneCheckpointTest_UnresolvableSnapshot");

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "FaceTune was present on the avatar before conversion, but the converted animator has no "
                + "\"FaceTune: \" layers -- it likely produced nothing during the build "
                + "(e.g. it could not resolve the face renderer). A dry run of FaceTune's own "
                + "face-renderer resolution on the pre-bake avatar returned: NotFoundAvatarRoot.")));

        VRC3CVRFaceTuneCheckpoint.CheckConvertedAvatar(true, avatar, snapshot);
    }

    [Test]
    public void CheckConvertedAvatar_WarnsWithSuccessDryRunAndBuildTimeCloneSuspicionWhenSnapshotResolves()
    {
        MakeAvatar(MakeControllerWithLayer("Locomotion"));

        // FaceTune's face-renderer resolution chain: VRCAvatarDescriptor marks the avatar root, then
        // (absent any lipSync/eyelid blend shape config) it falls back to a child literally named
        // "Body" carrying a SkinnedMeshRenderer with a mesh -- see AvatarContextBuilder.TryGetFaceRenderer
        // and VRChatSupport.GetFaceRenderer.
        snapshot = new GameObject("FaceTuneCheckpointTest_ResolvableSnapshot");
        snapshot.AddComponent<VRCAvatarDescriptor>();
        var body = new GameObject("Body");
        body.transform.SetParent(snapshot.transform);
        body.AddComponent<SkinnedMeshRenderer>().sharedMesh = new Mesh();

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "FaceTune was present on the avatar before conversion, but the converted animator has no "
                + "\"FaceTune: \" layers -- it likely produced nothing during the build "
                + "(e.g. it could not resolve the face renderer). A dry run of FaceTune's own "
                + "face-renderer resolution on the pre-bake avatar returned: Success. That resolution "
                + "succeeds before the bake, so the failure is specific to the build-time clone state "
                + "-- suspect a hook earlier in the build chain altering it.")));

        VRC3CVRFaceTuneCheckpoint.CheckConvertedAvatar(true, avatar, snapshot);
    }
}
#endif
