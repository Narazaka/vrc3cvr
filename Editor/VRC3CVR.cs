using UnityEditor;
using UnityEngine;
using PeanutTools_VRC3CVR.Localization;
using PeanutTools_VRC3CVR;
using VRC.SDK3.Avatars.Components;

public class VRC3CVR : EditorWindow
{
    class T
    {
        public static istring Description => new istring("Convert your VRChat avatar to ChilloutVR", "VRChat�A�o�^�[��ChilloutVR�A�o�^�[�ɕϊ�");
        public static istring Step1 => new istring("Step 1: Select your avatar", "Step 1: �A�o�^�[��I��");
        public static istring Avatar => new istring("Avatar", "�A�o�^�[");
        public static istring Step2 => new istring("Step 2: Configure settings", "Step 2: �ݒ�");
        public static istring ConvertLocomotionAnimator => new istring("Convert Locomotion Animator (NOT RECOMMEND)", "Locomotion���C���[��ϊ� (�񐄏�)");
        public static istring ConvertLocomotionAnimatorDescription => new istring("Locomotion state machines will very likely not convert over correctly and this option is better left unticked for now", "Locomotion�X�e�[�g�}�V���͐������ϊ�����Ȃ��\���������A���̃I�v�V�����͍��̂Ƃ���`�F�b�N���O���Ă������Ƃ������߂��܂�");
        public static istring ConvertAdditiveAnimator => new istring("Convert Additive Animator (additive blend layers)", "Additive���C���[��ϊ�");
        public static istring ConvertAdditiveAnimatorDescription => new istring("Additive state machine is commonly used for additively blended animations on the base avatar. May cause bicycle pose on certain avatars.", "Additive�X�e�[�g�}�V���́A�x�[�X�A�o�^�[�̉��Z�u�����h�A�j���[�V�����Ɉ�ʓI�Ɏg�p����܂��B����̃A�o�^�[�Ŏ��]�ԃ|�[�Y�������N�����\��������܂��B");
        public static istring ConvertGestureAnimator => new istring("Convert Gesture Animator (hands)", "Gesture���C���[��ϊ� (��)");
        public static istring ConvertGestureAnimatorDescription => new istring("If your avatar overwrites the default finger animations when performing expressions", "�A�o�^�[���\������s����Ƃ��Ƀf�t�H���g�̎w�̃A�j���[�V�������㏑������ꍇ��ON");
        public static istring ConvertActionAnimator => new istring("Convert Action Animator (NOT RECOMMEND)", "Action���C���[��ϊ� (�񐄏�)");
        public static istring ConvertActionAnimatorDescription => new istring("Actions (mostly used for emotes) will very likely not convert over correctly and this option is better left unticked for now", "�A�N�V���� (��ɃG���[�g�Ɏg�p�����) �͐������ϊ�����Ȃ��\���������A���̃I�v�V�����͍��̂Ƃ���`�F�b�N���O���Ă������Ƃ������߂��܂�");
        public static istring ConvertFXAnimator => new istring("Convert FX Animator (blendshapes, particles, ect.)", "FX���C���[��ϊ� (�u�����h�V�F�C�v�A�p�[�e�B�N���Ȃ�)");
        public static istring ConvertFXAnimatorDescription => new istring("FX state machine is commonly used all effects which don't affect the underlying rig, such as blendshapes and particle effects.", "FX�X�e�[�g�}�V���́A�u�����h�V�F�C�v��p�[�e�B�N���G�t�F�N�g�ȂǁA��b�I�ȃ��O�ɉe����^���Ȃ����ׂẴG�t�F�N�g�Ɉ�ʓI�Ɏg�p����܂��B");
        public static istring PreserveParameterSyncState => new istring("Preserve parameter sync state", "�p�����[�^�̓�����Ԃ�ێ�");
        public static istring PreserveParameterSyncStateDescription => new istring("In ChilloutVR, all Animation parameters that do not have a # prefix in their name will be synchronized. Turning this option on will add a # prefix to parameters that will not be synchronized.", "ChilloutVR�ł͖��O�̍ŏ���#���t���Ȃ�Animation�p�����[�^�[�͑S�ē�������܂��B���̃I�v�V������ON�ɂ���Ɠ�������Ȃ��p�����[�^�[��#�v���t�B�N�X��t���܂��B");
        public static istring ConvertVRCAnimatorLocomotionControl => new istring("Convert VRC Animator Locomotion Control", "VRC Animator Locomotion Control��ϊ�");
        public static istring ConvertVRCAnimatorLocomotionControlDescription => new istring("Converts the VRC Animator Locomotion Control to BodyControl", "VRC Animator Locomotion Control��BodyControl�ɕϊ�");
        public static istring ConvertVRCAnimatorTrackingControl => new istring("Convert VRC Animator Tracking Control", "VRC Animator Tracking Control��ϊ�");
        public static istring ConvertVRCAnimatorTrackingControlDescription => new istring("Converts the VRC Animator Tracking Control to BodyControl", "VRC Animator Tracking Control��BodyControl�ɕϊ�");
        public static istring ConvertVRCContactSendersAndReceivers => new istring("Convert VRC Contact Senders and Receivers to CVR Pointer and CVR Advanced Avatar Trigger", "VRC Contact Sender��Receiver��CVR Pointer��CVR Advanced Avatar Trigger�ɕϊ�");
        public static istring ConvertVRCContactSendersAndReceiversDescription => new istring("Unlike VRC Contact, CVR Pointer and Trigger only change values when the contact collides. This difference may cause compatibility issues.", "VRCContact�ƈ���āACVR Pointer��Trigger��Contact���Փ˂������ɂ����l��ύX���܂���B���̍��قɂ���Č݊����̖��𐶂���\��������܂��B");
        public static istring CollisionTagConvertionConfig => new istring("Collision Tag Convertion Config", "Collision Tag �ϊ��ݒ�");
        public static istring CollisionTagConvertionConfigDescription => new istring("Convert \"Head\" to \"mouth\" and \"Hand\"s and \"Finger\"s to \"index\"?", "\"Hand\"��\"mouth\"�ɁA\"Hand\"����\"Finger\"����\"index\"�ɕϊ�����?");
        public static istring CreateVRCContactEquivalentPointers => new istring("Create VRC Contact Equivalent CVR Pointers", "VRC Contact ������ CVR Pointer ���쐬");
        public static istring CreateVRCContactEquivalentPointersDescription => new istring("Creates CVR Pointers for VRC default Contact Senders", "VRC�f�t�H���g�� VRC Contact Sender�ɑ�������CVR Pointer���쐬���܂�");
        public static istring AdjustToVrcMenuOrder => new istring("Adjust to VRC menu order", "VRC���j���[�̏����ɒ���");
        public static istring UseHierarchicalMenuName => new istring("Use hierarchical menu name", "�K�w���j���[�����g�p");
        public static istring UseHierarchicalDropdownMenuName => new istring("Use hierarchical dropdown menu name", "�h���b�v�_�E�����j���[�����K�w��");
        public static istring AddActionMenuModAnnotations => new istring("Add Action Menu Mod annotations", "Action Menu Mod�p�̎�ʃ^�O��t�^");
        public static istring CloneAvatar => new istring("Clone avatar", "�A�o�^�[���N���[��");
        public static istring DeleteVRCAvatarDescriptorAndPipelineManager => new istring("Delete VRC Avatar Descriptor and Pipeline Manager", "VRC Avatar Descriptor��Pipeline Manager���폜");
        public static istring DeletePhysBonesAndColliders => new istring("Delete PhysBones and colliders", "PhysBones�ƃR���C�_�[���폜");
        public static istring DeleteContactsDescription => new istring("Always deletes contact receivers and senders", "VRC Contact Receiver��Sender�͏�ɍ폜����܂�");
        public static istring Step3 => new istring("Step 3: Convert", "Step 3: �ϊ�");
        public static istring Convert => new istring("Convert", "�ϊ�");
        public static istring ConvertDescription => new istring("Clones your original avatar to preserve it", "���̃A�o�^�[���N���[�����ĕϊ����܂�");
        public static istring ToeError(bool left) => new istring("You do not have a " + (left ? "left" : "right") + " toe bone configured", $"{(left ? "����" : "�E��")}�̂ܐ�̃{�[�����ݒ肳��Ă��܂���");
        public static istring ToeErrorDescription => new istring("You must configure this before you upload your avatar", "�A�o�^�[���A�b�v���[�h����O�ɐݒ肵�Ă�������");
    }

    [MenuItem("Tools/VRC3CVR")]
    public static void ShowWindow()
    {
        var window = GetWindow<VRC3CVR>();
        window.titleContent = new GUIContent("VRC3CVR");
        window.minSize = new Vector2(250, 50);
    }

    [SerializeField] VRC3CVRCore _vrc3cvr;
    VRC3CVRCore vrc3cvr
    {
        get
        {
            if (_vrc3cvr == null)
            {
                _vrc3cvr = new VRC3CVRCore();
            }
            return _vrc3cvr;
        }
    }

    Vector2 scrollPosition;
    SerializedObject serializedObject;
    SerializedProperty collisionTagConvertionConfigProperty;

    void OnEnable()
    {
        serializedObject = new SerializedObject(this);
        _vrc3cvr = new VRC3CVRCore();
        collisionTagConvertionConfigProperty = serializedObject.FindProperty("_vrc3cvr").FindPropertyRelative("collisionTagConvertionConfig");
    }

    void OnGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width));

        CustomGUI.BoldLabel("VRC3CVR");
        CustomGUI.HelpLabel(T.Description);

        Localization.DrawLocaleSelector();

        CustomGUI.LineGap();

        CustomGUI.HorizontalRule();

        CustomGUI.LineGap();

        CustomGUI.BoldLabel(T.Step1);
        CustomGUI.SmallLineGap();
        vrc3cvr.vrcAvatarDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(T.Avatar, vrc3cvr.vrcAvatarDescriptor, typeof(VRCAvatarDescriptor), true);
        CustomGUI.SmallLineGap();
        CustomGUI.BoldLabel(T.Step2);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertLocomotionLayer = GUILayout.Toggle(vrc3cvr.convertLocomotionLayer, T.ConvertLocomotionAnimator);
        CustomGUI.HelpLabel(T.ConvertLocomotionAnimatorDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertAdditiveLayer = GUILayout.Toggle(vrc3cvr.convertAdditiveLayer, T.ConvertAdditiveAnimator);
        CustomGUI.HelpLabel(T.ConvertAdditiveAnimatorDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertGestureLayer = GUILayout.Toggle(vrc3cvr.convertGestureLayer, T.ConvertGestureAnimator);
        CustomGUI.HelpLabel(T.ConvertGestureAnimatorDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertActionLayer = GUILayout.Toggle(vrc3cvr.convertActionLayer, T.ConvertActionAnimator);
        CustomGUI.HelpLabel(T.ConvertActionAnimatorDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertFXLayer = GUILayout.Toggle(vrc3cvr.convertFXLayer, T.ConvertFXAnimator);
        CustomGUI.HelpLabel(T.ConvertFXAnimatorDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.preserveParameterSyncState = GUILayout.Toggle(vrc3cvr.preserveParameterSyncState, T.PreserveParameterSyncState);
        CustomGUI.HelpLabel(T.PreserveParameterSyncStateDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertVRCAnimatorLocomotionControl = GUILayout.Toggle(vrc3cvr.convertVRCAnimatorLocomotionControl, T.ConvertVRCAnimatorLocomotionControl);
        CustomGUI.HelpLabel(T.ConvertVRCAnimatorLocomotionControlDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertVRCAnimatorTrackingControl = GUILayout.Toggle(vrc3cvr.convertVRCAnimatorTrackingControl, T.ConvertVRCAnimatorTrackingControl);
        CustomGUI.HelpLabel(T.ConvertVRCAnimatorTrackingControlDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.convertVRCContactSendersAndReceivers = GUILayout.Toggle(vrc3cvr.convertVRCContactSendersAndReceivers, T.ConvertVRCContactSendersAndReceivers);
        CustomGUI.HelpLabel(T.ConvertVRCContactSendersAndReceiversDescription);

        EditorGUILayout.PropertyField(collisionTagConvertionConfigProperty, T.CollisionTagConvertionConfig.GUIContent, true);
        CustomGUI.HelpLabel(T.CollisionTagConvertionConfigDescription);

        CustomGUI.SmallLineGap();

        vrc3cvr.createVRCContactEquivalentPointers = GUILayout.Toggle(vrc3cvr.createVRCContactEquivalentPointers, T.CreateVRCContactEquivalentPointers);
        CustomGUI.HelpLabel(T.CreateVRCContactEquivalentPointersDescription);

        CustomGUI.SmallLineGap();

        CustomGUI.RenderLink("Physbone -> DynamicBone Tool?", "https://github.com/Dreadrith/PhysBone-Converter");

        CustomGUI.SmallLineGap();

        vrc3cvr.adjustToVrcMenuOrder = GUILayout.Toggle(vrc3cvr.adjustToVrcMenuOrder, T.AdjustToVrcMenuOrder);

        CustomGUI.SmallLineGap();

        vrc3cvr.useHierarchicalMenuName = GUILayout.Toggle(vrc3cvr.useHierarchicalMenuName, T.UseHierarchicalMenuName);
        vrc3cvr.useHierarchicalDropdownMenuName = GUILayout.Toggle(vrc3cvr.useHierarchicalDropdownMenuName, T.UseHierarchicalDropdownMenuName);
        vrc3cvr.addActionMenuModAnnotations = GUILayout.Toggle(vrc3cvr.addActionMenuModAnnotations, T.AddActionMenuModAnnotations);

        CustomGUI.SmallLineGap();

        vrc3cvr.shouldCloneAvatar = GUILayout.Toggle(vrc3cvr.shouldCloneAvatar, T.CloneAvatar);

        CustomGUI.SmallLineGap();

        vrc3cvr.shouldDeleteVRCAvatarDescriptorAndPipelineManager = GUILayout.Toggle(vrc3cvr.shouldDeleteVRCAvatarDescriptorAndPipelineManager, T.DeleteVRCAvatarDescriptorAndPipelineManager);

        CustomGUI.SmallLineGap();

        vrc3cvr.shouldDeletePhysBones = GUILayout.Toggle(vrc3cvr.shouldDeletePhysBones, T.DeletePhysBonesAndColliders);
        CustomGUI.HelpLabel(T.DeleteContactsDescription);

        CustomGUI.SmallLineGap();

        CustomGUI.BoldLabel(T.Step3);

        CustomGUI.SmallLineGap();

        EditorGUI.BeginDisabledGroup(vrc3cvr.GetIsReadyForConvert() == false);
        if (GUILayout.Button(T.Convert))
        {
            vrc3cvr.Convert();
        }
        EditorGUI.EndDisabledGroup();
        CustomGUI.HelpLabel(T.ConvertDescription);

        if (vrc3cvr.animator != null)
        {
            Transform leftToesTransform = vrc3cvr.animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform righToesTransform = vrc3cvr.animator.GetBoneTransform(HumanBodyBones.RightToes);

            if (leftToesTransform == null || righToesTransform == null)
            {
                CustomGUI.SmallLineGap();

                CustomGUI.RenderErrorMessage(T.ToeError(leftToesTransform == null));
                CustomGUI.RenderWarningMessage(T.ToeErrorDescription);
            }
        }

        CustomGUI.SmallLineGap();

        CustomGUI.MyLinks("vrc3cvr");

        EditorGUILayout.EndScrollView();
        serializedObject.ApplyModifiedProperties();
    }
}
