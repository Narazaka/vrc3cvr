using System;
using VRC.SDK3.Avatars.Components;

[Serializable]
public class VRC3CVRConvertConfig
{
    public VRCAvatarDescriptor vrcAvatarDescriptor;
    public string outputDirName = "VRC3CVR_Output";
    public bool shouldCloneAvatar = true;
    public bool saveAssets = true;

    public bool convertLocomotionLayer = false;
    public bool convertAdditiveLayer = false;
    public bool convertGestureLayer = true;
    public bool convertActionLayer = false;
    public bool convertFXLayer = true;
    public bool preserveParameterSyncState = true;
    public bool convertVRCAnimatorLocomotionControl = true;
    public bool convertVRCAnimatorTrackingControl = true;
    public bool convertVRCContactSendersAndReceivers = true;
    public VRC3CVRCollisionTagConvertionConfig collisionTagConvertionConfig = VRC3CVRCollisionTagConvertionConfig.DefaultConfig;
    public VRC3CVRCollisionTagConvertionConfigWithPath[] collisionTagConvertionConfigWithPaths = new VRC3CVRCollisionTagConvertionConfigWithPath[]
    {
        new VRC3CVRCollisionTagConvertionConfigWithPath
        {
            path = "Armature/Hips/motchiri_shader",
            config = new VRC3CVRCollisionTagConvertionConfig
            {
                All = VRC3CVRCollisionTagConvertionConfig.Operation.Keep,
            },
        }
    };
    public bool createVRCContactEquivalentPointers = true;
    public bool adjustToVrcMenuOrder = true;
    public bool useHierarchicalMenuName = true;
    public bool useHierarchicalDropdownMenuName = true;
    public bool addActionMenuModAnnotations = true;
    public bool shouldDeleteVRCAvatarDescriptorAndPipelineManager = true;
    public bool shouldDeletePhysBones = true;

    public void CopyFrom(VRC3CVRConvertConfig other)
    {
        vrcAvatarDescriptor = other.vrcAvatarDescriptor;
        outputDirName = other.outputDirName;
        shouldCloneAvatar = other.shouldCloneAvatar;
        saveAssets = other.saveAssets;

        convertLocomotionLayer = other.convertLocomotionLayer;
        convertAdditiveLayer = other.convertAdditiveLayer;
        convertGestureLayer = other.convertGestureLayer;
        convertActionLayer = other.convertActionLayer;
        convertFXLayer = other.convertFXLayer;
        preserveParameterSyncState = other.preserveParameterSyncState;
        convertVRCAnimatorLocomotionControl = other.convertVRCAnimatorLocomotionControl;
        convertVRCAnimatorTrackingControl = other.convertVRCAnimatorTrackingControl;
        convertVRCContactSendersAndReceivers = other.convertVRCContactSendersAndReceivers;
        collisionTagConvertionConfig = other.collisionTagConvertionConfig;
        createVRCContactEquivalentPointers = other.createVRCContactEquivalentPointers;
        adjustToVrcMenuOrder = other.adjustToVrcMenuOrder;
        useHierarchicalMenuName = other.useHierarchicalMenuName;
        useHierarchicalDropdownMenuName = other.useHierarchicalDropdownMenuName;
        addActionMenuModAnnotations = other.addActionMenuModAnnotations;
        shouldDeleteVRCAvatarDescriptorAndPipelineManager = other.shouldDeleteVRCAvatarDescriptorAndPipelineManager;
        shouldDeletePhysBones = other.shouldDeletePhysBones;
    }
}
