#if CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Generates a ChilloutVR-NATIVE probe avatar: built with the CCK directly, uploaded as-is, never
// run through the conversion.
//
// Why it does not go through vrc3cvr: everything it measures is a question about what the
// ChilloutVR client hands an animator — the space and unit of the core velocity parameters, what
// each CVRParameterStream source actually reports, whether an AnimatorDriver can reconstruct an
// avatar-local velocity from them. None of that is a question about the conversion, so putting the
// conversion in the path would only add a second thing that can be wrong.
//
// It is also deliberately NOT a probe assembled at runtime by the verification mod. A component a
// mod adds to a worn avatar is not guaranteed to behave like one that shipped with the avatar: the
// client may collect and initialise streams at avatar load, and may cache the Rigidbody or
// transform an entry resolves against. The only reading that settles a design question is the one
// taken through the real mechanism, so the probe ships inside the uploaded avatar.
//
// Upload it once and reuse the same content id, like the other verification avatars, and record
// the id as "probe=<id>" in VerificationAvatarIds.txt.
public static class VRC3CVRCvrProbeAvatar
{
    public const string DefaultAssetFolder = "Assets/VRC3CVR_CvrProbeAvatar";

    // Fed by the client to any avatar that declares them (ABI.CCK.Scripts.CVRCommon).
    public const string MovementX = "MovementX";
    public const string MovementY = "MovementY";
    public const string VelocityX = "VelocityX";
    public const string VelocityY = "VelocityY";
    public const string VelocityZ = "VelocityZ";

    // Written by the parameter stream; the names are the probe's readout.
    public const string StreamRigidBodySpeed = "PrbRbSpeed";
    public const string StreamRigidBodyVelX = "PrbRbVelX";
    public const string StreamRigidBodyVelZ = "PrbRbVelZ";
    public const string StreamRigidBodyLocalVelX = "PrbRbLocalVelX";
    public const string StreamRigidBodyLocalVelZ = "PrbRbLocalVelZ";
    public const string StreamWorldYaw = "PrbWorldYaw";
    public const string StreamInputMoveX = "PrbInputMoveX";
    public const string StreamInputMoveY = "PrbInputMoveY";
    public const string StreamUpright = "PrbUpright";

    // Written by the AnimatorDriver layer — the conversion under test.
    public const string DerivedSpeed = "PrbSpeed";
    public const string DerivedMoveMagnitude = "PrbMoveMag";
    public const string DerivedLocalVelX = "PrbReconX";
    public const string DerivedLocalVelZ = "PrbReconZ";

    [MenuItem("Tools/VRC3CVR/Create CVR Probe Avatar")]
    public static void CreateFromMenu()
    {
        var avatar = Generate(DefaultAssetFolder);
        Selection.activeGameObject = avatar.gameObject;
    }

    public static CVRAvatar Generate(string assetFolder)
    {
        if (!AssetDatabase.IsValidFolder(assetFolder))
        {
            var parent = System.IO.Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(assetFolder));
        }

        var root = new GameObject("VRC3CVR CVR Probe Avatar");
        var bones = BuildRig(root);
        var animator = root.AddComponent<Animator>();
        animator.avatar = BuildHumanAvatar(root, bones, assetFolder);

        var controller = BuildController(assetFolder);

        var cvrAvatar = root.AddComponent<CVRAvatar>();
        cvrAvatar.viewPosition = new Vector3(0f, 1.45f, 0.1f);
        cvrAvatar.voicePosition = new Vector3(0f, 1.45f, 0.1f);
        cvrAvatar.avatarUsesAdvancedSettings = true;
        cvrAvatar.avatarSettings = new CVRAdvancedAvatarSettings
        {
            initialized = true,
            settings = new List<CVRAdvancedSettingsEntry>(),
            baseController = controller,
        };
        // The override controller is the one the client actually runs — baseController alone is
        // "part of autogen" and leaves the worn avatar on the CCK's stock animator, which is
        // exactly what the first upload of this probe did: it came back with the stock 13
        // parameters and none of the probe's own.
        var overrideController = new AnimatorOverrideController(controller)
        {
            name = "CvrProbe Overrides",
        };
        AssetDatabase.CreateAsset(overrideController, assetFolder + "/CvrProbe.overrideController");
        cvrAvatar.overrides = overrideController;

        BuildParameterStream(root);

        AssetDatabase.SaveAssets();
        return cvrAvatar;
    }

    // ---- parameter stream ----

    // On the ROOT deliberately. The Transform sources read "the transform where the Parameter
    // Stream component is placed", and the Rigidbody sources resolve to "the Rigidbody on the same
    // or a parent GameObject" — which, on a worn avatar, means walking up into the player rig. Both
    // of those are exactly the resolutions a conversion would have to rely on.
    static void BuildParameterStream(GameObject root)
    {
        var stream = root.AddComponent<CVRParameterStream>();
        stream.referenceType = CVRParameterStream.ReferenceType.Avatar;
        stream.entries = new List<CVRParameterStreamEntry>();

        void Entry(CVRParameterStreamEntry.Type type, string parameterName)
        {
            stream.entries.Add(new CVRParameterStreamEntry
            {
                type = type,
                targetType = CVRParameterStreamEntry.TargetType.AvatarAnimator,
                applicationType = CVRParameterStreamEntry.ApplicationType.Override,
                parameterName = parameterName,
            });
        }

        // Does a Rigidbody source resolve to anything on a worn avatar, and does its LOCAL variant
        // carry an avatar-local velocity? A mod-side read of Rigidbody.velocity on the nearest
        // ancestor came back zero, which suggests the client's character controller moves a
        // kinematic body — but that is a reading of Unity's property, not of what this source
        // reports, so it settles nothing on its own.
        Entry(CVRParameterStreamEntry.Type.RigidBodySpeed, StreamRigidBodySpeed);
        Entry(CVRParameterStreamEntry.Type.RigidBodyVelocityX, StreamRigidBodyVelX);
        Entry(CVRParameterStreamEntry.Type.RigidBodyVelocityZ, StreamRigidBodyVelZ);
        Entry(CVRParameterStreamEntry.Type.RigidBodyLocalVelocityX, StreamRigidBodyLocalVelX);
        Entry(CVRParameterStreamEntry.Type.RigidBodyLocalVelocityZ, StreamRigidBodyLocalVelZ);

        // The fallback route if no Rigidbody source works: yaw plus trigonometry. The CCK
        // documents the value range of the Transform rotation sources as unknown, so the range and
        // the sign convention have to be measured before anything can be built on them.
        Entry(CVRParameterStreamEntry.Type.TransformGlobalRotationY, StreamWorldYaw);

        // Movement INPUT is player-local by construction. If it matches the MovementX/MovementY
        // core parameters, the conversion needs neither a Rigidbody source nor trigonometry.
        Entry(CVRParameterStreamEntry.Type.InputMovementX, StreamInputMoveX);
        Entry(CVRParameterStreamEntry.Type.InputMovementY, StreamInputMoveY);

        // Already measured through the conversion (standing 0.968 / crouching 0.491 / prone 0.092);
        // carried here to confirm the same reading with no conversion in the path.
        Entry(CVRParameterStreamEntry.Type.AvatarUpright, StreamUpright);
    }

    // ---- animator ----

    static AnimatorController BuildController(string assetFolder)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(assetFolder + "/CvrProbe.controller");
        // Unity gives a fresh controller one layer; the driver lives there.
        foreach (var name in new[]
        {
            MovementX, MovementY, VelocityX, VelocityY, VelocityZ,
            StreamRigidBodySpeed, StreamRigidBodyVelX, StreamRigidBodyVelZ,
            StreamRigidBodyLocalVelX, StreamRigidBodyLocalVelZ,
            StreamWorldYaw, StreamInputMoveX, StreamInputMoveY, StreamUpright,
            DerivedSpeed, DerivedMoveMagnitude, DerivedLocalVelX, DerivedLocalVelZ,
        })
        {
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        AddDriverLayer(controller);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    // The conversion under test, in the form it would take in the converter.
    //
    // ChilloutVR reports VelocityX/Z in WORLD space (measured), while VRChat's locomotion blend
    // trees are built in avatar-LOCAL space. MovementX/Y point the right way — measured at
    // (0, +0.5) walking forward, (0, -0.5) backward and (+0.5, 0) strafing right, so +Y is forward
    // and +X is right, exactly VRChat's VelocityZ / VelocityX axes — but they are NOT a unit
    // vector: 0.5 is the walk ring and 1.0 the run ring, matching the rings in the CCK's own
    // locomotion trees. Multiplying them by the speed would therefore halve a walk.
    //
    // So the direction comes from MovementX/Y and the magnitude from the world velocity, which is
    // frame-independent:
    //
    //     scale = |world ground velocity| / |(MovementX, MovementY)|
    //     local velocity = (MovementX, MovementY) * scale
    //
    // The epsilon on the divisor is what keeps standing still well defined: at rest the numerator
    // is ~0, so the scale collapses to ~0 rather than dividing by zero.
    static void AddDriverLayer(AnimatorController controller)
    {
        AnimatorDriverTask Task(AnimatorDriverTask.Operator op, string targetName, string aName, string bName, float bValue = 0f)
        {
            return new AnimatorDriverTask
            {
                op = op,
                targetName = targetName,
                targetType = AnimatorDriverTask.ParameterType.Float,
                aType = AnimatorDriverTask.SourceType.Parameter,
                aParamType = AnimatorDriverTask.ParameterType.Float,
                aName = aName,
                bType = bName == null ? AnimatorDriverTask.SourceType.Static : AnimatorDriverTask.SourceType.Parameter,
                bParamType = AnimatorDriverTask.ParameterType.Float,
                bName = bName ?? "",
                bValue = bValue,
            };
        }

        // A short clip gives the state a length, so the self transition re-enters it — and reruns
        // the driver — every tick. The animated property is undeclared and does nothing.
        var tickClip = new AnimationClip { name = "VRC3CVR_ProbeTick" };
        tickClip.SetCurve("", typeof(Animator), "VRC3CVR_ProbeTick", AnimationCurve.Constant(0f, 1f / 60f, 0f));
        AssetDatabase.AddObjectToAsset(tickClip, controller);

        var state = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Recompute",
            writeDefaultValues = false,
            motion = tickClip,
            behaviours = new StateMachineBehaviour[]
            {
                new AnimatorDriver
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    // remote copies run this too, so the reconstruction has to hold there as well
                    localOnly = false,
                    // Tasks run in order and may read and write the same parameter, so the two
                    // outputs double as scratch space until the step that fills them.
                    EnterTasks = new List<AnimatorDriverTask>
                    {
                        // ground speed: the Y axis is not part of a locomotion tree's space
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedSpeed, VelocityX, VelocityX),
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedLocalVelZ, VelocityZ, VelocityZ),
                        Task(AnimatorDriverTask.Operator.Addition, DerivedSpeed, DerivedSpeed, DerivedLocalVelZ),
                        Task(AnimatorDriverTask.Operator.Power, DerivedSpeed, DerivedSpeed, null, 0.5f),
                        // |(MovementX, MovementY)| — the walk/run ring, reported for its own sake
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedMoveMagnitude, MovementX, MovementX),
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedLocalVelZ, MovementY, MovementY),
                        Task(AnimatorDriverTask.Operator.Addition, DerivedMoveMagnitude, DerivedMoveMagnitude, DerivedLocalVelZ),
                        Task(AnimatorDriverTask.Operator.Power, DerivedMoveMagnitude, DerivedMoveMagnitude, null, 0.5f),
                        // scale = speed / (ring + epsilon), parked in ReconX until it is consumed
                        Task(AnimatorDriverTask.Operator.Addition, DerivedLocalVelX, DerivedMoveMagnitude, null, 0.0001f),
                        Task(AnimatorDriverTask.Operator.Division, DerivedLocalVelX, DerivedSpeed, DerivedLocalVelX),
                        // Z first: filling X overwrites the scale both of them need
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedLocalVelZ, MovementY, DerivedLocalVelX),
                        Task(AnimatorDriverTask.Operator.Multiplication, DerivedLocalVelX, MovementX, DerivedLocalVelX),
                    },
                },
            },
        };
        state.transitions = new AnimatorStateTransition[]
        {
            new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                hasExitTime = true,
                exitTime = 1f,
                hasFixedDuration = true,
                duration = 0f,
                offset = 0f,
                destinationState = state,
            },
        };
        AssetDatabase.AddObjectToAsset(state, controller);
        foreach (var behaviour in state.behaviours)
        {
            AssetDatabase.AddObjectToAsset(behaviour, controller);
        }
        foreach (var transition in state.transitions)
        {
            AssetDatabase.AddObjectToAsset(transition, controller);
        }

        var stateMachine = controller.layers[0].stateMachine;
        stateMachine.states = new ChildAnimatorState[]
        {
            new ChildAnimatorState { state = state, position = new Vector3(0f, 0f) },
        };
        stateMachine.defaultState = state;
    }

    // ---- rig ----

    // A copy of the primitive humanoid the VRChat-side fixture builds, rather than a reference to
    // it: this file must compile with only the CCK present, and that one is behind the VRChat SDK
    // guard. The rig is incidental here — it exists so ChilloutVR treats this as a humanoid avatar
    // with locomotion, which is the condition the measured parameters are fed under.
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
        avatar.name = "VRC3CVRCvrProbeAvatar";
        AssetDatabase.CreateAsset(avatar, assetFolder + "/CvrProbeAvatar.asset");
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
}
#endif
