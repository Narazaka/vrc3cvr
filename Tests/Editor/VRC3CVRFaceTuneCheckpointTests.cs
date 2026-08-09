#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

public class VRC3CVRFaceTuneCheckpointTests
{
    GameObject avatar;

    [TearDown]
    public void TearDown()
    {
        if (avatar != null) Object.DestroyImmediate(avatar);
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
}
#endif
