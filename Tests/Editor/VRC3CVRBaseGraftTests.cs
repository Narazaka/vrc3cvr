#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

public class VRC3CVRBaseGraftTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    static bool InvokeHasAuthoredMotion(AnimatorController controller) =>
        (bool)typeof(VRC3CVRCore).GetMethod("HasAuthoredMotion", Flags).Invoke(null, new object[] { controller });

    static AnimatorController MakeController(string name, params Motion[] motions)
    {
        var controller = new AnimatorController { name = name };
        controller.AddLayer("L");
        var layers = controller.layers;
        var machine = layers[0].stateMachine;
        for (var i = 0; i < motions.Length; i++)
        {
            machine.AddState("S" + i).motion = motions[i];
        }
        return controller;
    }

    [Test]
    public void HasAuthoredMotion_FalseWhenEveryClipIsAVrchatPlaceholder()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var controller = MakeController("allProxy", proxy);

        Assert.IsFalse(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_TrueWhenAnyClipIsTheAvatarsOwn()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var controller = MakeController("mixed", proxy, own);

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_LooksInsideBlendTrees()
    {
        var own = new AnimationClip { name = "MyCoolWalk" };
        var tree = new BlendTree { name = "Tree" };
        tree.AddChild(own);
        var controller = MakeController("tree", tree);

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(own);
        Object.DestroyImmediate(tree);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_FalseForAnEmptyController()
    {
        var controller = MakeController("empty");

        Assert.IsFalse(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(controller);
    }
}
#endif
