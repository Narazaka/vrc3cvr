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
        var innerTree = new BlendTree { name = "InnerTree" };
        innerTree.AddChild(own);
        var outerTree = new BlendTree { name = "OuterTree" };
        outerTree.AddChild(innerTree);
        var controller = MakeController("tree", outerTree);

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(own);
        Object.DestroyImmediate(innerTree);
        Object.DestroyImmediate(outerTree);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_LooksInsideSubStateMachines()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var controller = MakeController("sub", proxy);
        var root = controller.layers[0].stateMachine;
        var sub = root.AddStateMachine("Sub");
        sub.AddState("S0").motion = own;

        Assert.IsTrue(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(controller);
    }

    [Test]
    public void HasAuthoredMotion_FalseForAnEmptyController()
    {
        var controller = MakeController("empty");

        Assert.IsFalse(InvokeHasAuthoredMotion(controller));

        Object.DestroyImmediate(controller);
    }

    [Test]
    public void PlaceholderSubstitutions_CoverTheLocomotionProxiesAndPointAtRealCckClips()
    {
        var map = (System.Collections.Generic.Dictionary<string, string>)
            typeof(VRC3CVRCore).GetField("placeholderClipSubstitutions", Flags).GetValue(null);

        // the proxies a locomotion blend tree actually references
        foreach (var proxy in new[]
        {
            "proxy_stand_still", "proxy_walk_forward", "proxy_walk_backward",
            "proxy_strafe_right", "proxy_run_forward", "proxy_run_backward",
            "proxy_crouch_still", "proxy_crouch_walk_forward",
            "proxy_low_crawl_still", "proxy_low_crawl_forward",
            "proxy_fall_short", "proxy_landing", "proxy_sit",
        })
        {
            Assert.IsTrue(map.ContainsKey(proxy), proxy + " has no ChilloutVR counterpart");
        }

        foreach (var pair in map)
        {
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/CVR.CCK/Assets/Avatar/Animations/Locomotion/" + pair.Value + ".anim");
            Assert.IsNotNull(clip, pair.Key + " maps to " + pair.Value + ", which is not in the CCK");
        }
    }

    [Test]
    public void SubstitutePlaceholderClips_ReplacesProxiesInsideBlendTreesAndLeavesAuthoredClipsAlone()
    {
        var proxy = new AnimationClip { name = "proxy_walk_forward" };
        var own = new AnimationClip { name = "MyCoolWalk" };
        var tree = new BlendTree { name = "Tree" };
        tree.AddChild(proxy);
        tree.AddChild(own);
        var controller = MakeController("tree", tree);

        var core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetMethod("SubstitutePlaceholderClips", Flags).Invoke(core, new object[] { controller });

        var children = ((BlendTree)controller.layers[0].stateMachine.states[0].state.motion).children;
        Assert.AreEqual("LocWalkingForward", children[0].motion.name);
        Assert.AreEqual("MyCoolWalk", children[1].motion.name, "the author's own clip is untouched");

        Object.DestroyImmediate(proxy);
        Object.DestroyImmediate(own);
        Object.DestroyImmediate(tree);
        Object.DestroyImmediate(controller);
    }
}
#endif
