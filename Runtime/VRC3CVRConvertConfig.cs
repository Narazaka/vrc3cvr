#if VRC_SDK_VRCSDK3
using System;
using VRC.SDK3.Avatars.Components;

[Serializable]
public class VRC3CVRConvertConfig
{
    // VRChat's Gesture*Weight is 0 while Neutral, the analog squeeze while Fist,
    // and fixed 1 for every other gesture. ChilloutVR has no weight parameter;
    // its GestureLeft/GestureRight float itself is the squeeze amount during Fist.
    public enum GestureWeightConversionMode
    {
        // Rewrite every weight reference onto GestureLeft/GestureRight, compiling the
        // "fixed 1 outside Fist" rule into the consumers: extra OR transitions for weight
        // conditions and boundary children for weight-driven 1D blend trees.
        // Zero latency; motion time states keep the plain fold (partially accurate).
        FoldToGestureLeft,
        // Keep the weight parameters and feed them from GestureLeft via a generated
        // blend tree layer that writes the parameter (AAP). Faithful for every consumer
        // including motion time, at the cost of one frame of latency.
        DerivedParameter,
    }

    public VRCAvatarDescriptor vrcAvatarDescriptor;
    public string outputDirName = "VRC3CVR_Output";
    public bool shouldCloneAvatar = true;
    // Run every VRChat SDK preprocess hook (VRCFury, NDMF/Modular Avatar, AAO, ...) on a clone
    // before converting, so non-destructive tools are applied. The clone is made by
    // VRC3CVRBaker, so VRC3CVRCore is told not to clone again.
    public bool autoBake = true;
    public bool saveAssets = true;

    public bool convertLocomotionLayer = true;
    // Only takes effect when the Base layer replaces the CVR locomotion layer, which
    // convertLocomotionLayer gates.
    public bool playLandingAnimation = true;
    // Covers every machine that ends up in the layer owning CVR's locomotion: the Base layer that
    // takes it over and the machines folded into it.
    public bool convertLocomotionTrackingControl = false;
    public bool convertAdditiveLayer = true;
    public bool convertGestureLayer = true;
    public bool convertActionLayer = true;
    public bool convertSittingLayer = true;
    public bool convertFXLayer = true;
    public bool preserveParameterSyncState = true;
    public bool convertVRCAnimatorLocomotionControl = true;
    public bool convertVRCAnimatorTrackingControl = true;
    public GestureWeightConversionMode gestureWeightConversionMode = GestureWeightConversionMode.FoldToGestureLeft;
    // Feed MuteSelf/VRMode/Upright from the game via CVRParameterStream. The stream only runs on
    // the wearer's client, so these parameters are declared synced (no # prefix) and CVR's normal
    // parameter sync carries the values to remotes. Sync cost per CVRAvatar.GetParameterSyncUsage:
    // non-bool 32 bits each, bools packed 8 per 8 bits.
    public bool feedGameStateParameters = true;
    public bool convertVRCContactSendersAndReceivers = true;
    public VRC3CVRCollisionTagConvertionConfig collisionTagConvertionConfig = VRC3CVRCollisionTagConvertionConfig.DefaultConfig;
    public VRC3CVRCollisionTagConvertionConfigWithPath[] collisionTagConvertionConfigWithPaths = new VRC3CVRCollisionTagConvertionConfigWithPath[] {};
    public bool createVRCContactEquivalentPointers = false;
    public bool adjustContactParameterSync = true;
    public bool adjustToVrcMenuOrder = true;
    public bool useHierarchicalMenuName = true;
    public bool useHierarchicalDropdownMenuName = true;
    public bool addActionMenuModAnnotations = true;
    public bool convertVrcConstraints = true;
    public bool convertVrcHeadChops = true;
    public bool convertVrcSpatialAudioSources = true;
    public bool shouldDeleteVRCAvatarDescriptorAndPipelineManager = true;
    public bool shouldDeletePhysBones = true;

    public void CopyFrom(VRC3CVRConvertConfig other)
    {
        vrcAvatarDescriptor = other.vrcAvatarDescriptor;
        outputDirName = other.outputDirName;
        shouldCloneAvatar = other.shouldCloneAvatar;
        autoBake = other.autoBake;
        saveAssets = other.saveAssets;

        convertLocomotionLayer = other.convertLocomotionLayer;
        playLandingAnimation = other.playLandingAnimation;
        convertLocomotionTrackingControl = other.convertLocomotionTrackingControl;
        convertAdditiveLayer = other.convertAdditiveLayer;
        convertGestureLayer = other.convertGestureLayer;
        convertActionLayer = other.convertActionLayer;
        convertSittingLayer = other.convertSittingLayer;
        convertFXLayer = other.convertFXLayer;
        preserveParameterSyncState = other.preserveParameterSyncState;
        convertVRCAnimatorLocomotionControl = other.convertVRCAnimatorLocomotionControl;
        convertVRCAnimatorTrackingControl = other.convertVRCAnimatorTrackingControl;
        gestureWeightConversionMode = other.gestureWeightConversionMode;
        feedGameStateParameters = other.feedGameStateParameters;
        convertVRCContactSendersAndReceivers = other.convertVRCContactSendersAndReceivers;
        collisionTagConvertionConfig = other.collisionTagConvertionConfig;
        collisionTagConvertionConfigWithPaths = other.collisionTagConvertionConfigWithPaths;
        createVRCContactEquivalentPointers = other.createVRCContactEquivalentPointers;
        adjustContactParameterSync = other.adjustContactParameterSync;
        adjustToVrcMenuOrder = other.adjustToVrcMenuOrder;
        useHierarchicalMenuName = other.useHierarchicalMenuName;
        useHierarchicalDropdownMenuName = other.useHierarchicalDropdownMenuName;
        addActionMenuModAnnotations = other.addActionMenuModAnnotations;
        convertVrcConstraints = other.convertVrcConstraints;
        convertVrcHeadChops = other.convertVrcHeadChops;
        convertVrcSpatialAudioSources = other.convertVrcSpatialAudioSources;
        shouldDeleteVRCAvatarDescriptorAndPipelineManager = other.shouldDeleteVRCAvatarDescriptorAndPipelineManager;
        shouldDeletePhysBones = other.shouldDeletePhysBones;
    }
}
#endif
