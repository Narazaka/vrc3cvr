#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

// See VRC3CVRGestureConversionTests for why these live in Assembly-CSharp-Editor and use reflection.
public class VRC3CVRConstraintConversionTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    GameObject avatar;
    VRC3CVRCore core;

    [SetUp]
    public void SetUp()
    {
        avatar = new GameObject("ConstraintTestAvatar");
        core = new VRC3CVRCore();
        typeof(VRC3CVRCore).GetField("chilloutAvatarGameObject", Flags).SetValue(core, avatar);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(avatar);
    }

    GameObject Child(string name, GameObject parent = null)
    {
        var child = new GameObject(name);
        child.transform.SetParent((parent != null ? parent : avatar).transform, false);
        return child;
    }

    void Convert()
    {
        typeof(VRC3CVRCore).GetMethod("ConvertVrcConstraintsToUnityConstraints", Flags).Invoke(core, null);
    }

    static void SetSources(VRCConstraintBase constraint, params VRCConstraintSource[] sources)
    {
        constraint.Sources.SetLength(sources.Length);
        for (var i = 0; i < sources.Length; i++)
        {
            constraint.Sources[i] = sources[i];
        }
    }

    [Test]
    public void ParentConstraint_ConvertsFieldsSourcesAndOffsets()
    {
        var target = Child("Target");
        var sourceA = Child("SourceA");
        var sourceB = Child("SourceB");
        var vrc = target.AddComponent<VRCParentConstraint>();
        vrc.GlobalWeight = 0.5f;
        vrc.Locked = true;
        vrc.IsActive = true;
        vrc.PositionAtRest = new Vector3(1f, 2f, 3f);
        vrc.RotationAtRest = new Vector3(10f, 20f, 30f);
        vrc.AffectsPositionX = true;
        vrc.AffectsPositionY = false;
        vrc.AffectsPositionZ = true;
        vrc.AffectsRotationX = false;
        vrc.AffectsRotationY = true;
        vrc.AffectsRotationZ = false;
        SetSources(vrc,
            new VRCConstraintSource(sourceA.transform, 1f, new Vector3(0.1f, 0.2f, 0.3f), new Vector3(1f, 2f, 3f)),
            new VRCConstraintSource(sourceB.transform, 0.25f, Vector3.zero, Vector3.zero));

        Convert();

        Assert.IsNull(target.GetComponent<VRCParentConstraint>());
        var unity = target.GetComponent<ParentConstraint>();
        Assert.IsNotNull(unity);
        Assert.AreEqual(0.5f, unity.weight);
        Assert.IsTrue(unity.locked);
        Assert.IsTrue(unity.constraintActive);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), unity.translationAtRest);
        Assert.AreEqual(new Vector3(10f, 20f, 30f), unity.rotationAtRest);
        Assert.AreEqual(Axis.X | Axis.Z, unity.translationAxis);
        Assert.AreEqual(Axis.Y, unity.rotationAxis);
        Assert.AreEqual(2, unity.sourceCount);
        Assert.AreEqual(sourceA.transform, unity.GetSource(0).sourceTransform);
        Assert.AreEqual(1f, unity.GetSource(0).weight);
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), unity.GetTranslationOffset(0));
        Assert.AreEqual(new Vector3(1f, 2f, 3f), unity.GetRotationOffset(0));
        Assert.AreEqual(sourceB.transform, unity.GetSource(1).sourceTransform);
        Assert.AreEqual(0.25f, unity.GetSource(1).weight);
    }

    [Test]
    public void AimConstraint_ConvertsWorldUpSettings()
    {
        var target = Child("Target");
        var source = Child("Source");
        var worldUp = Child("WorldUp");
        var vrc = target.AddComponent<VRCAimConstraint>();
        vrc.IsActive = true;
        vrc.AimAxis = new Vector3(0f, 0f, 1f);
        vrc.UpAxis = new Vector3(0f, 1f, 0f);
        vrc.WorldUpVector = new Vector3(1f, 0f, 0f);
        vrc.WorldUpTransform = worldUp.transform;
        vrc.WorldUp = VRCConstraintBase.WorldUpType.ObjectRotationUp;
        SetSources(vrc, new VRCConstraintSource(source.transform, 1f, Vector3.zero, Vector3.zero));

        Convert();

        var unity = target.GetComponent<AimConstraint>();
        Assert.IsNotNull(unity);
        Assert.AreEqual(new Vector3(0f, 0f, 1f), unity.aimVector);
        Assert.AreEqual(new Vector3(0f, 1f, 0f), unity.upVector);
        Assert.AreEqual(new Vector3(1f, 0f, 0f), unity.worldUpVector);
        Assert.AreEqual(worldUp.transform, unity.worldUpObject);
        Assert.AreEqual(AimConstraint.WorldUpType.ObjectRotationUp, unity.worldUpType);
    }

    [Test]
    public void TargetTransform_AttachesConstraintToTargetAndRecordsRemap()
    {
        var holder = Child("Holder");
        var actualTarget = Child("ActualTarget");
        var source = Child("Source");
        var vrc = holder.AddComponent<VRCPositionConstraint>();
        vrc.IsActive = true;
        vrc.TargetTransform = actualTarget.transform;
        SetSources(vrc, new VRCConstraintSource(source.transform, 1f, Vector3.zero, Vector3.zero));

        Convert();

        Assert.IsNull(holder.GetComponent<PositionConstraint>());
        Assert.IsNotNull(actualTarget.GetComponent<PositionConstraint>());

        var remap = (System.Collections.IDictionary)typeof(VRC3CVRCore).GetField("constraintComponentPathRemap", Flags).GetValue(core);
        Assert.AreEqual(("ActualTarget", 0), remap[("Holder", typeof(VRCPositionConstraint))]);
    }

    [Test]
    public void NullSources_AreKeptToPreserveIndicesAndWeightNormalization()
    {
        var target = Child("Target");
        var sourceB = Child("SourceB");
        var vrc = target.AddComponent<VRCPositionConstraint>();
        vrc.IsActive = true;
        SetSources(vrc,
            new VRCConstraintSource(null, 0.75f, Vector3.zero, Vector3.zero),
            new VRCConstraintSource(sourceB.transform, 1f, Vector3.zero, Vector3.zero));

        Convert();

        var unity = target.GetComponent<PositionConstraint>();
        Assert.AreEqual(2, unity.sourceCount);
        Assert.IsNull(unity.GetSource(0).sourceTransform);
        Assert.AreEqual(0.75f, unity.GetSource(0).weight);
        Assert.AreEqual(sourceB.transform, unity.GetSource(1).sourceTransform);
    }

    [Test]
    public void OverflowSources_BeyondSixteenAreConverted()
    {
        var target = Child("Target");
        var vrc = target.AddComponent<VRCPositionConstraint>();
        vrc.IsActive = true;
        var sources = Enumerable.Range(0, 18)
            .Select(i => new VRCConstraintSource(Child("Source" + i).transform, 1f, Vector3.zero, Vector3.zero))
            .ToArray();
        SetSources(vrc, sources);

        Convert();

        var unity = target.GetComponent<PositionConstraint>();
        Assert.AreEqual(18, unity.sourceCount);
        Assert.AreEqual("Source17", unity.GetSource(17).sourceTransform.name);
    }

    [Test]
    public void MultipleConstraintsOfSameType_MergeSources()
    {
        var target = Child("Target");
        var sourceA = Child("SourceA");
        var sourceB = Child("SourceB");
        var vrcA = target.AddComponent<VRCPositionConstraint>();
        vrcA.IsActive = true;
        SetSources(vrcA, new VRCConstraintSource(sourceA.transform, 1f, Vector3.zero, Vector3.zero));
        var vrcB = target.AddComponent<VRCPositionConstraint>();
        vrcB.IsActive = true;
        SetSources(vrcB, new VRCConstraintSource(sourceB.transform, 0.5f, Vector3.zero, Vector3.zero));

        Convert();

        var unityConstraints = target.GetComponents<PositionConstraint>();
        Assert.AreEqual(1, unityConstraints.Length);
        Assert.AreEqual(2, unityConstraints[0].sourceCount);
    }

    [Test]
    public void AnimationClip_RebindsConstraintCurvesAndDropsVrcOnlyProperties()
    {
        var holder = Child("Holder");
        var actualTarget = Child("ActualTarget");
        var source = Child("Source");
        var vrc = holder.AddComponent<VRCParentConstraint>();
        vrc.IsActive = true;
        vrc.TargetTransform = actualTarget.transform;
        SetSources(vrc, new VRCConstraintSource(source.transform, 1f, Vector3.zero, Vector3.zero));
        Convert();

        var clip = new AnimationClip { name = "constraintAnim" };
        var curve = AnimationCurve.Constant(0f, 1f, 1f);
        clip.SetCurve("Holder", typeof(VRCParentConstraint), "IsActive", curve);
        clip.SetCurve("Holder", typeof(VRCParentConstraint), "GlobalWeight", curve);
        clip.SetCurve("Holder", typeof(VRCParentConstraint), "Sources.source0.Weight", curve);
        clip.SetCurve("Holder", typeof(VRCParentConstraint), "FreezeToWorld", curve);
        // unrelated control curve (GameObject.m_IsActive does not get expanded by SetCurve)
        clip.SetCurve("SomethingElse", typeof(GameObject), "m_IsActive", curve);

        var newClip = (AnimationClip)typeof(VRC3CVRCore).GetMethod("RemapAnimationClipOfConstraintComponent", Flags)
            .Invoke(core, new object[] { clip });

        Assert.IsNotNull(newClip);
        var bindings = AnimationUtility.GetCurveBindings(newClip)
            .Select(b => b.path + "|" + b.type.Name + "|" + b.propertyName).ToArray();
        CollectionAssert.Contains(bindings, "ActualTarget|ParentConstraint|m_Active");
        CollectionAssert.Contains(bindings, "ActualTarget|ParentConstraint|m_Weight");
        CollectionAssert.Contains(bindings, "ActualTarget|ParentConstraint|m_Sources.Array.data[0].weight");
        CollectionAssert.Contains(bindings, "SomethingElse|GameObject|m_IsActive");
        // the VRC-typed originals and the inconvertible FreezeToWorld are gone
        Assert.IsFalse(bindings.Any(b => b.Contains("VRCParentConstraint")), string.Join(" ; ", bindings));
        Assert.AreEqual(4, bindings.Length, string.Join(" ; ", bindings));
    }

    [Test]
    public void MergedOnSameGameObject_SourceBindingsKeepFirstConstraintIndices()
    {
        // Unity resolves animation bindings to the first component of a type, so bindings on a
        // GameObject with two same-type VRC constraints animated the first one; after merging,
        // the first constraint's sources still start at index 0
        var target = Child("Target");
        var sourceA = Child("SourceA");
        var sourceB = Child("SourceB");
        var vrcA = target.AddComponent<VRCPositionConstraint>();
        vrcA.IsActive = true;
        SetSources(vrcA, new VRCConstraintSource(sourceA.transform, 1f, Vector3.zero, Vector3.zero));
        var vrcB = target.AddComponent<VRCPositionConstraint>();
        vrcB.IsActive = true;
        SetSources(vrcB, new VRCConstraintSource(sourceB.transform, 0.5f, Vector3.zero, Vector3.zero));
        Convert();

        var clip = new AnimationClip { name = "mergedAnim" };
        clip.SetCurve("Target", typeof(VRCPositionConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0f, 1f, 1f));

        var newClip = (AnimationClip)typeof(VRC3CVRCore).GetMethod("RemapAnimationClipOfConstraintComponent", Flags)
            .Invoke(core, new object[] { clip });

        var bindings = AnimationUtility.GetCurveBindings(newClip)
            .Select(b => b.path + "|" + b.type.Name + "|" + b.propertyName).ToArray();
        Assert.AreEqual(new[] { "Target|PositionConstraint|m_Sources.Array.data[0].weight" }, bindings);
    }

    [Test]
    public void MergedAcrossGameObjects_SourceBindingsAreIndexOffset()
    {
        // Constraints on different GameObjects merging into the same target host are
        // independently animatable in VRC; the later one's sources sit after the earlier
        // one's in the merged constraint, so its per-source bindings must be re-indexed
        var holder1 = Child("Holder1");
        var holder2 = Child("Holder2");
        var target = Child("SharedTarget");
        var sourceA = Child("SourceA");
        var sourceB = Child("SourceB");
        var vrcA = holder1.AddComponent<VRCPositionConstraint>();
        vrcA.IsActive = true;
        vrcA.TargetTransform = target.transform;
        SetSources(vrcA, new VRCConstraintSource(sourceA.transform, 1f, Vector3.zero, Vector3.zero));
        var vrcB = holder2.AddComponent<VRCPositionConstraint>();
        vrcB.IsActive = true;
        vrcB.TargetTransform = target.transform;
        SetSources(vrcB, new VRCConstraintSource(sourceB.transform, 0.5f, Vector3.zero, Vector3.zero));
        Convert();

        Assert.AreEqual(2, target.GetComponent<PositionConstraint>().sourceCount);

        var clip = new AnimationClip { name = "crossMergedAnim" };
        var curve = AnimationCurve.Constant(0f, 1f, 1f);
        clip.SetCurve("Holder1", typeof(VRCPositionConstraint), "Sources.source0.Weight", curve);
        clip.SetCurve("Holder2", typeof(VRCPositionConstraint), "Sources.source0.Weight", curve);

        var newClip = (AnimationClip)typeof(VRC3CVRCore).GetMethod("RemapAnimationClipOfConstraintComponent", Flags)
            .Invoke(core, new object[] { clip });

        var bindings = AnimationUtility.GetCurveBindings(newClip)
            .Select(b => b.path + "|" + b.type.Name + "|" + b.propertyName).OrderBy(s => s).ToArray();
        Assert.AreEqual(new[]
        {
            "SharedTarget|PositionConstraint|m_Sources.Array.data[0].weight",
            "SharedTarget|PositionConstraint|m_Sources.Array.data[1].weight",
        }, bindings);
    }

    [Test]
    public void AnimationClip_WithoutConstraintBindings_IsLeftUntouched()
    {
        typeof(VRC3CVRCore).GetField("constraintComponentPathRemap", Flags)
            .SetValue(core, new System.Collections.Generic.Dictionary<(string, System.Type), (string, int)>());
        var clip = new AnimationClip { name = "plainAnim" };
        clip.SetCurve("Something", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 1f));

        var newClip = typeof(VRC3CVRCore).GetMethod("RemapAnimationClipOfConstraintComponent", Flags)
            .Invoke(core, new object[] { clip });

        Assert.IsNull(newClip);
    }
}
#endif
