using System;
using VRC.SDK3.Avatars.Components;

[Serializable]
public class VRC3CVRConvertConfig
{
    public VRCAvatarDescriptor vrcAvatarDescriptor;
    public string outputDirName = "VRC3CVR_Output";
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
    public bool createVRCContactEquivalentPointers = true;
    public bool adjustToVrcMenuOrder = true;
    public bool useHierarchicalMenuName = true;
    public bool useHierarchicalDropdownMenuName = true;
    public bool addActionMenuModAnnotations = true;
    public bool shouldCloneAvatar = true;
    public bool shouldDeleteVRCAvatarDescriptorAndPipelineManager = true;
    public bool shouldDeletePhysBones = true;
    public bool saveAssets = true;

    public void CopyFrom(VRC3CVRConvertConfig other)
    {
        vrcAvatarDescriptor = other.vrcAvatarDescriptor;
        outputDirName = other.outputDirName;
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
        shouldCloneAvatar = other.shouldCloneAvatar;
        shouldDeleteVRCAvatarDescriptorAndPipelineManager = other.shouldDeleteVRCAvatarDescriptorAndPipelineManager;
        shouldDeletePhysBones = other.shouldDeletePhysBones;
        saveAssets = other.saveAssets;
    }
}
