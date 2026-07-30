#if VRC_SDK_VRCSDK3
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;

// Generates a self-contained primitive humanoid avatar whose gimmicks make every in-game
// verification item of the conversion observable (see the table in issue #17 / #21):
//   G1 weight bar (analog squeeze) / G2 standalone weight condition (fixed 1 outside Fist)
//   G3 weight condition paired with Fist / V1 VelocityMagnitude / S1 MuteSelf / S2 VRMode
//   S3 Upright / C1..C6 the six constraint types / C7 Target Transform redirect
//   C8 same-type merge / C9 animated constraint properties (menu toggle)
// Each group can be shown/hidden from the expressions menu so items can be checked one at a
// time. Labels carry the expected behavior (in English: the built-in font has no CJK glyphs).
// Lives in Tests/ so it ships with the repository but not with the distributed unitypackage.
public static class VRC3CVRVerificationAvatar
{
    public const string DefaultAssetFolder = "Assets/VRC3CVR_VerificationAvatar";

    [MenuItem("Tools/VRC3CVR/Create Verification Avatar")]
    public static void CreateFromMenu()
    {
        var descriptor = Generate(DefaultAssetFolder);
        Selection.activeGameObject = descriptor.gameObject;
    }

    public static VRCAvatarDescriptor Generate(string assetFolder)
    {
        if (!AssetDatabase.IsValidFolder(assetFolder))
        {
            var parent = System.IO.Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(assetFolder));
        }

        var root = new GameObject("VRC3CVR Verification Avatar");
        var bones = BuildRig(root);
        var avatar = BuildHumanAvatar(root, bones, assetFolder);
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;

        var materials = new Materials(assetFolder);
        BuildGimmickObjects(root, materials);
        BuildConstraintObjects(root, bones, materials);

        var fx = BuildFxController(assetFolder);
        var descriptor = root.AddComponent<VRCAvatarDescriptor>();
        descriptor.ViewPosition = new Vector3(0f, 1.45f, 0.1f);
        descriptor.customizeAnimationLayers = true;
        descriptor.baseAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[]
        {
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base, isDefault = true },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Additive, isDefault = true },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = true },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Action, isDefault = true },
            new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = fx },
        };
        descriptor.customExpressions = true;
        descriptor.expressionParameters = BuildExpressionParameters(assetFolder);
        descriptor.expressionsMenu = BuildExpressionsMenu(assetFolder);

        AssetDatabase.SaveAssets();
        return descriptor;
    }

    // ---- rig ----

    class Bones
    {
        public Transform hips, spine, head;
        public Transform leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot;
        public Transform leftUpperArm, leftLowerArm, leftHand, rightUpperArm, rightLowerArm, rightHand;
        public Transform armature;
    }

    static Bones BuildRig(GameObject root)
    {
        Transform Bone(string name, Transform parent, Vector3 worldPosition, float visualSize = 0.06f)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.position = worldPosition;
            if (visualSize > 0f)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = name + "_Visual";
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(bone, false);
                visual.transform.localScale = Vector3.one * visualSize;
            }
            return bone;
        }

        var bones = new Bones();
        bones.armature = Bone("Armature", root.transform, Vector3.zero, 0f);
        bones.hips = Bone("Hips", bones.armature, new Vector3(0f, 0.9f, 0f), 0.12f);
        bones.spine = Bone("Spine", bones.hips, new Vector3(0f, 1.1f, 0f), 0.1f);
        bones.head = Bone("Head", bones.spine, new Vector3(0f, 1.45f, 0f), 0.14f);
        bones.leftUpperLeg = Bone("LeftUpperLeg", bones.hips, new Vector3(0.1f, 0.85f, 0f));
        bones.leftLowerLeg = Bone("LeftLowerLeg", bones.leftUpperLeg, new Vector3(0.1f, 0.45f, 0f));
        bones.leftFoot = Bone("LeftFoot", bones.leftLowerLeg, new Vector3(0.1f, 0.05f, 0f));
        bones.rightUpperLeg = Bone("RightUpperLeg", bones.hips, new Vector3(-0.1f, 0.85f, 0f));
        bones.rightLowerLeg = Bone("RightLowerLeg", bones.rightUpperLeg, new Vector3(-0.1f, 0.45f, 0f));
        bones.rightFoot = Bone("RightFoot", bones.rightLowerLeg, new Vector3(-0.1f, 0.05f, 0f));
        bones.leftUpperArm = Bone("LeftUpperArm", bones.spine, new Vector3(0.2f, 1.35f, 0f));
        bones.leftLowerArm = Bone("LeftLowerArm", bones.leftUpperArm, new Vector3(0.45f, 1.35f, 0f));
        bones.leftHand = Bone("LeftHand", bones.leftLowerArm, new Vector3(0.7f, 1.35f, 0f));
        bones.rightUpperArm = Bone("RightUpperArm", bones.spine, new Vector3(-0.2f, 1.35f, 0f));
        bones.rightLowerArm = Bone("RightLowerArm", bones.rightUpperArm, new Vector3(-0.45f, 1.35f, 0f));
        bones.rightHand = Bone("RightHand", bones.rightLowerArm, new Vector3(-0.7f, 1.35f, 0f));
        return bones;
    }

    static Avatar BuildHumanAvatar(GameObject root, Bones bones, string assetFolder)
    {
        var humanBones = new List<HumanBone>();
        void Map(string humanName, Transform bone)
        {
            humanBones.Add(new HumanBone { humanName = humanName, boneName = bone.name, limit = new HumanLimit { useDefaultValues = true } });
        }
        Map("Hips", bones.hips);
        Map("Spine", bones.spine);
        Map("Head", bones.head);
        Map("LeftUpperLeg", bones.leftUpperLeg);
        Map("LeftLowerLeg", bones.leftLowerLeg);
        Map("LeftFoot", bones.leftFoot);
        Map("RightUpperLeg", bones.rightUpperLeg);
        Map("RightLowerLeg", bones.rightLowerLeg);
        Map("RightFoot", bones.rightFoot);
        Map("LeftUpperArm", bones.leftUpperArm);
        Map("LeftLowerArm", bones.leftLowerArm);
        Map("LeftHand", bones.leftHand);
        Map("RightUpperArm", bones.rightUpperArm);
        Map("RightLowerArm", bones.rightLowerArm);
        Map("RightHand", bones.rightHand);

        var skeleton = new List<SkeletonBone> { SkeletonOf(root.transform) };
        void AddSkeleton(Transform bone)
        {
            skeleton.Add(SkeletonOf(bone));
            foreach (Transform child in bone)
            {
                if (!child.name.EndsWith("_Visual"))
                {
                    AddSkeleton(child);
                }
            }
        }
        AddSkeleton(bones.armature);

        var description = new HumanDescription
        {
            human = humanBones.ToArray(),
            skeleton = skeleton.ToArray(),
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            armStretch = 0.05f,
            legStretch = 0.05f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
        if (!avatar.isValid)
        {
            throw new System.Exception("Generated human avatar is not valid");
        }
        avatar.name = "VRC3CVRVerificationAvatar";
        AssetDatabase.CreateAsset(avatar, assetFolder + "/VerificationAvatar.asset");
        return avatar;
    }

    static SkeletonBone SkeletonOf(Transform transform)
    {
        return new SkeletonBone
        {
            name = transform.name,
            position = transform.localPosition,
            rotation = transform.localRotation,
            scale = transform.localScale,
        };
    }

    // ---- materials / labels ----

    class Materials
    {
        public Material white, red, green, blue, yellow;
        public Materials(string assetFolder)
        {
            white = Make(assetFolder, "White", Color.white);
            red = Make(assetFolder, "Red", Color.red);
            green = Make(assetFolder, "Green", Color.green);
            blue = Make(assetFolder, "Blue", new Color(0.3f, 0.5f, 1f));
            yellow = Make(assetFolder, "Yellow", Color.yellow);
        }
        static Material Make(string assetFolder, string name, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, assetFolder + "/" + name + ".mat");
            return material;
        }
    }

    static GameObject Marker(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, string description, PrimitiveType primitive = PrimitiveType.Cube)
    {
        var marker = GameObject.CreatePrimitive(primitive);
        marker.name = name;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localScale = scale;
        marker.GetComponent<Renderer>().sharedMaterial = material;
        // the label is a sibling: markers get rescaled by animation or leave their rest spot
        // to follow constraint sources, while the label marks the station
        if (description != null)
        {
            AddLabel(parent, localPosition + new Vector3(0f, 0.28f, 0f), name, description);
        }
        return marker;
    }

    static void AddLabel(Transform parent, Vector3 localPosition, string title, string description)
    {
        var label = new GameObject(title + "_Label");
        label.transform.SetParent(parent, false);
        label.transform.localPosition = localPosition;
        label.transform.localScale = Vector3.one * 0.008f;
        var textMesh = label.AddComponent<TextMesh>();
        textMesh.richText = true;
        textMesh.text = title + "\n<size=26>" + description + "</size>";
        textMesh.fontSize = 44;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<MeshRenderer>().sharedMaterial = textMesh.font.material;
    }

    // ---- gimmick objects (driven by the FX controller) ----

    static void BuildGimmickObjects(GameObject root, Materials materials)
    {
        var panel = new GameObject("Panel").transform;
        panel.SetParent(root.transform, false);
        panel.localPosition = new Vector3(0f, 1.1f, 0.8f);

        var gesture = new GameObject("Gesture").transform;
        gesture.SetParent(panel, false);
        Marker("WeightBar", gesture, new Vector3(-0.5f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.05f), materials.white,
            "grows as you squeeze the L trigger\n(smooth 0..1, any gesture switch glitch?)");
        Marker("WeightGate", gesture, new Vector3(-0.3f, 0f, 0f), Vector3.one * 0.06f, materials.yellow,
            "ON while L weight > 0.5\nmust also be ON during open hand / point etc.");
        Marker("FistGate", gesture, new Vector3(-0.1f, 0f, 0f), Vector3.one * 0.06f, materials.red,
            "ON only while L Fist squeezed > 0.5\nmust stay OFF on other gestures");

        var state = new GameObject("State").transform;
        state.SetParent(panel, false);
        Marker("VelocityBar", state, new Vector3(0.1f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.05f), materials.green,
            "grows with move speed\ncheck from a remote viewer too");
        Marker("UprightBar", state, new Vector3(0.3f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.05f), materials.blue,
            "shrinks while crouching / prone\ncheck from a remote viewer too");
        Marker("MuteMarker", state, new Vector3(0.5f, 0f, 0f), Vector3.one * 0.06f, materials.red,
            "visible while you are muted\ncheck from a remote viewer too", PrimitiveType.Sphere);
        Marker("VRMarker", state, new Vector3(0.7f, 0f, 0f), Vector3.one * 0.06f, materials.green,
            "green = wearer is in VR / blue = desktop\ncheck from a remote viewer too");
        Marker("DesktopMarker", state, new Vector3(0.7f, 0f, 0f), Vector3.one * 0.055f, materials.blue, null);
    }

    // ---- constraint objects ----

    static void BuildConstraintObjects(GameObject root, Bones bones, Materials materials)
    {
        var row = new GameObject("Constraints").transform;
        row.SetParent(root.transform, false);
        row.localPosition = new Vector3(0f, 1.7f, 0.8f);

        void Sources(VRCConstraintBase constraint, params (Transform transform, float weight)[] sources)
        {
            constraint.Sources.SetLength(sources.Length);
            for (var i = 0; i < sources.Length; i++)
            {
                constraint.Sources[i] = new VRCConstraintSource(sources[i].transform, sources[i].weight, Vector3.zero, Vector3.zero);
            }
        }

        // C1 Parent: hovers above the hand via the per-source offset
        var parentC = Marker("ParentC", row, new Vector3(-0.8f, 0f, 0f), Vector3.one * 0.05f, materials.white,
            "hovers 15cm above the L hand\n(position + rotation follow)");
        var parentConstraint = parentC.AddComponent<VRCParentConstraint>();
        parentConstraint.Sources.SetLength(1);
        parentConstraint.Sources[0] = new VRCConstraintSource(bones.leftHand, 1f, new Vector3(0f, 0.15f, 0f), Vector3.zero);
        parentConstraint.Locked = true;
        parentConstraint.IsActive = true;

        // C2 Position: follows the hand with a global offset
        var positionC = Marker("PositionC", row, new Vector3(-0.6f, 0f, 0f), Vector3.one * 0.05f, materials.yellow,
            "follows the L hand, 25cm above");
        var positionConstraint = positionC.AddComponent<VRCPositionConstraint>();
        Sources(positionConstraint, (bones.leftHand, 1f));
        positionConstraint.PositionOffset = new Vector3(0f, 0.25f, 0f);
        positionConstraint.Locked = true;
        positionConstraint.IsActive = true;

        // C3 Rotation: nose marker shows the hand rotation
        var rotationC = Marker("RotationC", row, new Vector3(-0.4f, 0f, 0f), Vector3.one * 0.05f, materials.green,
            "stays here; nose copies the L hand rotation");
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.transform.SetParent(rotationC.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0f, 0.8f);
        nose.transform.localScale = new Vector3(0.3f, 0.3f, 0.8f);
        var rotationConstraint = rotationC.AddComponent<VRCRotationConstraint>();
        Sources(rotationConstraint, (bones.leftHand, 1f));
        rotationConstraint.Locked = true;
        rotationConstraint.IsActive = true;

        // C4 Scale: follows the hand scale; the offset keeps the cube at its visual size
        var scaleC = Marker("ScaleC", row, new Vector3(-0.2f, 0f, 0f), Vector3.one * 0.05f, materials.blue,
            "stays this size (5cm)\nif it becomes huge/zero the scale conversion is broken");
        var scaleConstraint = scaleC.AddComponent<VRCScaleConstraint>();
        Sources(scaleConstraint, (bones.leftHand, 1f));
        scaleConstraint.ScaleOffset = Vector3.one * 0.05f;
        scaleConstraint.Locked = true;
        scaleConstraint.IsActive = true;

        // C5 Aim: pointer aims at the hand
        var aimC = Marker("AimC", row, new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.03f, 0.2f), materials.red,
            "stays here; points at the L hand\n(MOST IMPORTANT: unproven in CVR)");
        var aimConstraint = aimC.AddComponent<VRCAimConstraint>();
        Sources(aimConstraint, (bones.leftHand, 1f));
        aimConstraint.AimAxis = new Vector3(0f, 0f, 1f);
        aimConstraint.UpAxis = new Vector3(0f, 1f, 0f);
        aimConstraint.Locked = true;
        aimConstraint.IsActive = true;

        // C6 LookAt: pointer looks at the hand
        var lookAtC = Marker("LookAtC", row, new Vector3(0.2f, 0f, 0f), new Vector3(0.03f, 0.03f, 0.2f), materials.white,
            "stays here; looks at the L hand\n(MOST IMPORTANT: unproven in CVR)");
        var lookAtConstraint = lookAtC.AddComponent<VRCLookAtConstraint>();
        Sources(lookAtConstraint, (bones.leftHand, 1f));
        lookAtConstraint.Locked = true;
        lookAtConstraint.IsActive = true;

        // C7 Target Transform redirect: the component sits on RedirHolder, the cube follows
        var redirHolder = new GameObject("RedirHolder");
        redirHolder.transform.SetParent(row, false);
        redirHolder.transform.localPosition = new Vector3(0.4f, 0f, 0f);
        var redirTarget = Marker("RedirTarget", row, new Vector3(0.4f, -0.1f, 0f), Vector3.one * 0.05f, materials.yellow,
            "follows the L hand, 35cm above\n(the constraint sits on another object)");
        var redirConstraint = redirHolder.AddComponent<VRCPositionConstraint>();
        Sources(redirConstraint, (bones.leftHand, 1f));
        redirConstraint.TargetTransform = redirTarget.transform;
        redirConstraint.PositionOffset = new Vector3(0f, 0.35f, 0f);
        redirConstraint.Locked = true;
        redirConstraint.IsActive = true;

        // C8 same-type merge: two position constraints on one object (hand + head sources)
        var mergeC = Marker("MergeC", row, new Vector3(0.6f, 0f, 0f), Vector3.one * 0.05f, materials.green,
            "floats between the L hand and the head\n(two constraints merged into one)");
        var mergeA = mergeC.AddComponent<VRCPositionConstraint>();
        Sources(mergeA, (bones.leftHand, 1f));
        mergeA.PositionOffset = new Vector3(0f, 0.45f, 0f);
        mergeA.Locked = true;
        mergeA.IsActive = true;
        var mergeB = mergeC.AddComponent<VRCPositionConstraint>();
        Sources(mergeB, (bones.head, 1f));
        mergeB.Locked = true;
        mergeB.IsActive = true;

        // C9 animated constraint: the menu toggle animates IsActive/GlobalWeight/source weight
        var animC = Marker("AnimC", row, new Vector3(0.8f, 0f, 0f), Vector3.one * 0.05f, materials.red,
            "menu [Anim Constraint] ON: follows the L hand 55cm above\nOFF: returns here");
        var animConstraint = animC.AddComponent<VRCPositionConstraint>();
        Sources(animConstraint, (bones.leftHand, 1f));
        animConstraint.PositionOffset = new Vector3(0f, 0.55f, 0f);
        animConstraint.Locked = true;
        animConstraint.IsActive = false;
    }

    // ---- FX controller ----

    static AnimatorController BuildFxController(string assetFolder)
    {
        var fx = AnimatorController.CreateAnimatorControllerAtPath(assetFolder + "/VerificationFX.controller");
        fx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
        fx.AddParameter("GestureLeftWeight", AnimatorControllerParameterType.Float);
        fx.AddParameter("VelocityMagnitude", AnimatorControllerParameterType.Float);
        fx.AddParameter("Upright", AnimatorControllerParameterType.Float);
        fx.AddParameter("VRMode", AnimatorControllerParameterType.Int);
        fx.AddParameter("MuteSelf", AnimatorControllerParameterType.Bool);
        fx.AddParameter("AnimConstraint", AnimatorControllerParameterType.Bool);
        fx.AddParameter("ShowGesture", AnimatorControllerParameterType.Bool);
        fx.AddParameter("ShowState", AnimatorControllerParameterType.Bool);
        fx.AddParameter("ShowConstraints", AnimatorControllerParameterType.Bool);

        AnimationClip Clip(string name, params (string path, System.Type type, string property, float value)[] curves)
        {
            var clip = new AnimationClip { name = name };
            foreach (var curve in curves)
            {
                clip.SetCurve(curve.path, curve.type, curve.property, AnimationCurve.Constant(0f, 1f / 60f, curve.value));
            }
            AssetDatabase.CreateAsset(clip, assetFolder + "/" + name + ".anim");
            return clip;
        }

        AnimationClip BarClip(string name, string path, float scaleY)
        {
            return Clip(name,
                (path, typeof(Transform), "m_LocalScale.x", 0.05f),
                (path, typeof(Transform), "m_LocalScale.y", scaleY),
                (path, typeof(Transform), "m_LocalScale.z", 0.05f));
        }

        void BlendTreeLayer(string layerName, string blendParameter, AnimationClip low, float lowThreshold, AnimationClip high, float highThreshold)
        {
            fx.AddLayer(layerName);
            var layerIndex = fx.layers.Length - 1;
            var tree = new BlendTree
            {
                name = layerName + " Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = blendParameter,
                useAutomaticThresholds = false,
                minThreshold = lowThreshold,
                maxThreshold = highThreshold,
                hideFlags = HideFlags.HideInHierarchy,
            };
            tree.AddChild(low, lowThreshold);
            tree.AddChild(high, highThreshold);
            AssetDatabase.AddObjectToAsset(tree, fx);
            var state = fx.layers[layerIndex].stateMachine.AddState(layerName + " State");
            state.motion = tree;
            state.writeDefaultValues = true;
        }

        void ToggleLayer(string layerName, AnimationClip offClip, AnimationClip onClip, AnimatorCondition[] onConditions, AnimatorCondition[][] offConditionSets, bool defaultOn = false)
        {
            fx.AddLayer(layerName);
            var stateMachine = fx.layers[fx.layers.Length - 1].stateMachine;
            var off = stateMachine.AddState("Off");
            off.motion = offClip;
            off.writeDefaultValues = true;
            var on = stateMachine.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = true;
            stateMachine.defaultState = defaultOn ? on : off;

            var toOn = off.AddTransition(on);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            foreach (var condition in onConditions)
            {
                toOn.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }
            foreach (var conditionSet in offConditionSets)
            {
                var toOff = on.AddTransition(off);
                toOff.hasExitTime = false;
                toOff.duration = 0f;
                foreach (var condition in conditionSet)
                {
                    toOff.AddCondition(condition.mode, condition.threshold, condition.parameter);
                }
            }
        }

        AnimatorCondition Cond(string parameter, AnimatorConditionMode mode, float threshold)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        void GroupToggleLayer(string layerName, string parameterName, string path)
        {
            ToggleLayer(layerName,
                Clip(layerName.Replace(" ", "") + "_Off", (path, typeof(GameObject), "m_IsActive", 0f)),
                Clip(layerName.Replace(" ", "") + "_On", (path, typeof(GameObject), "m_IsActive", 1f)),
                new[] { Cond(parameterName, AnimatorConditionMode.If, 0f) },
                new[] { new[] { Cond(parameterName, AnimatorConditionMode.IfNot, 0f) } },
                defaultOn: true);
        }

        // group visibility toggles
        GroupToggleLayer("Show Gesture", "ShowGesture", "Panel/Gesture");
        GroupToggleLayer("Show State", "ShowState", "Panel/State");
        GroupToggleLayer("Show Constraints", "ShowConstraints", "Constraints");

        // G1: analog squeeze bar
        BlendTreeLayer("G1 WeightBar", "GestureLeftWeight",
            BarClip("G1_Low", "Panel/Gesture/WeightBar", 0.02f), 0f,
            BarClip("G1_High", "Panel/Gesture/WeightBar", 0.4f), 1f);

        // G2: standalone weight condition (should also fire on non-Fist gestures where weight is fixed 1)
        ToggleLayer("G2 WeightGate",
            Clip("G2_Off", ("Panel/Gesture/WeightGate", typeof(GameObject), "m_IsActive", 0f)),
            Clip("G2_On", ("Panel/Gesture/WeightGate", typeof(GameObject), "m_IsActive", 1f)),
            new[] { Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f) },
            new[] { new[] { Cond("GestureLeftWeight", AnimatorConditionMode.Less, 0.5f) } });

        // G3: weight condition paired with Fist (must NOT fire on other gestures)
        ToggleLayer("G3 FistGate",
            Clip("G3_Off", ("Panel/Gesture/FistGate", typeof(GameObject), "m_IsActive", 0f)),
            Clip("G3_On", ("Panel/Gesture/FistGate", typeof(GameObject), "m_IsActive", 1f)),
            new[] { Cond("GestureLeft", AnimatorConditionMode.Equals, 1f), Cond("GestureLeftWeight", AnimatorConditionMode.Greater, 0.5f) },
            new[]
            {
                new[] { Cond("GestureLeft", AnimatorConditionMode.NotEqual, 1f) },
                new[] { Cond("GestureLeftWeight", AnimatorConditionMode.Less, 0.5f) },
            });

        // V1: velocity bar (0..4 m/s)
        BlendTreeLayer("V1 Velocity", "VelocityMagnitude",
            BarClip("V1_Low", "Panel/State/VelocityBar", 0.02f), 0f,
            BarClip("V1_High", "Panel/State/VelocityBar", 0.4f), 4f);

        // S3: upright bar
        BlendTreeLayer("S3 Upright", "Upright",
            BarClip("S3_Low", "Panel/State/UprightBar", 0.02f), 0f,
            BarClip("S3_High", "Panel/State/UprightBar", 0.4f), 1f);

        // S1: mute marker
        ToggleLayer("S1 Mute",
            Clip("S1_Off", ("Panel/State/MuteMarker", typeof(GameObject), "m_IsActive", 0f)),
            Clip("S1_On", ("Panel/State/MuteMarker", typeof(GameObject), "m_IsActive", 1f)),
            new[] { Cond("MuteSelf", AnimatorConditionMode.If, 0f) },
            new[] { new[] { Cond("MuteSelf", AnimatorConditionMode.IfNot, 0f) } });

        // S2: VR (green) / desktop (blue) marker
        ToggleLayer("S2 VRMode",
            Clip("S2_Desktop",
                ("Panel/State/VRMarker", typeof(GameObject), "m_IsActive", 0f),
                ("Panel/State/DesktopMarker", typeof(GameObject), "m_IsActive", 1f)),
            Clip("S2_VR",
                ("Panel/State/VRMarker", typeof(GameObject), "m_IsActive", 1f),
                ("Panel/State/DesktopMarker", typeof(GameObject), "m_IsActive", 0f)),
            new[] { Cond("VRMode", AnimatorConditionMode.Equals, 1f) },
            new[] { new[] { Cond("VRMode", AnimatorConditionMode.NotEqual, 1f) } });

        // C9: menu toggle animating constraint properties
        ToggleLayer("C9 AnimConstraint",
            Clip("C9_Off",
                ("Constraints/AnimC", typeof(VRCPositionConstraint), "IsActive", 0f),
                ("Constraints/AnimC", typeof(VRCPositionConstraint), "GlobalWeight", 0f)),
            Clip("C9_On",
                ("Constraints/AnimC", typeof(VRCPositionConstraint), "IsActive", 1f),
                ("Constraints/AnimC", typeof(VRCPositionConstraint), "GlobalWeight", 1f),
                ("Constraints/AnimC", typeof(VRCPositionConstraint), "Sources.source0.Weight", 1f)),
            new[] { Cond("AnimConstraint", AnimatorConditionMode.If, 0f) },
            new[] { new[] { Cond("AnimConstraint", AnimatorConditionMode.IfNot, 0f) } });

        // AddLayer creates layers with weight 0; force every layer to full weight
        var layers = fx.layers;
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i].defaultWeight = 1f;
        }
        fx.layers = layers;

        return fx;
    }

    // ---- expressions ----

    static VRCExpressionParameters BuildExpressionParameters(string assetFolder)
    {
        VRCExpressionParameters.Parameter Bool(string name, float defaultValue)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = defaultValue,
                saved = false,
                networkSynced = true,
            };
        }
        var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        parameters.name = "VerificationParameters";
        parameters.parameters = new VRCExpressionParameters.Parameter[]
        {
            Bool("AnimConstraint", 0f),
            Bool("ShowGesture", 1f),
            Bool("ShowState", 1f),
            Bool("ShowConstraints", 1f),
        };
        AssetDatabase.CreateAsset(parameters, assetFolder + "/VerificationParameters.asset");
        return parameters;
    }

    static VRCExpressionsMenu BuildExpressionsMenu(string assetFolder)
    {
        VRCExpressionsMenu.Control Toggle(string name, string parameterName)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName },
                value = 1f,
            };
        }
        var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        menu.name = "VerificationMenu";
        menu.controls.Add(Toggle("Anim Constraint", "AnimConstraint"));
        menu.controls.Add(Toggle("Show Gesture", "ShowGesture"));
        menu.controls.Add(Toggle("Show State", "ShowState"));
        menu.controls.Add(Toggle("Show Constraints", "ShowConstraints"));
        AssetDatabase.CreateAsset(menu, assetFolder + "/VerificationMenu.asset");
        return menu;
    }
}
#endif
