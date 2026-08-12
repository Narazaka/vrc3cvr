#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRCExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRCConstraintBase = VRC.Dynamics.VRCConstraintBase;

[Serializable]
public class VRC3CVRCore : VRC3CVRConvertConfig
{
    public Animator animator { get; private set; }
    bool isConverting = false;
    CVRAvatar cvrAvatar;
    SkinnedMeshRenderer bodySkinnedMeshRenderer;
    Vector3 vrcViewPosition;
    string[] vrcVisemeBlendShapes;
    string blinkBlendshapeName;
    AnimatorController chilloutAnimatorController;
    AnimatorController[] vrcAnimatorControllers;
    Dictionary<string, string[]> contactComponentPathRemap;
    Dictionary<(string path, Type vrcType), (string path, int sourceIndexOffset)> constraintComponentPathRemap;
    HashSet<string> constantContactProxiedParameters;
    HashSet<string> contactReceiverParameters;
    HashSet<string> localTriggerPaths;
    HashSet<string> localPointerPaths;
    List<(Transform parentBone, Transform createdRoot)> newContactRoots;
    GameObject chilloutAvatarGameObject;
    public GameObject chilloutAvatar => chilloutAvatarGameObject;

    public static VRC3CVRCore FromConfig(VRC3CVRConvertConfig config)
    {
        var core = new VRC3CVRCore();
        core.CopyFrom(config);
        return core;
    }

    [Serializable]
    public enum VRCBaseAnimatorID
    {
        BASE,
        ADDITIVE,
        GESTURE,
        ACTION,
        FX,
        MAX
    }

    // This stores generated extra avatar masks based on the VRC hardcoded animator masks combined with individual layer masks.
    Dictionary<(AvatarMask, AvatarMask), AvatarMask> avatarMaskCombineCache = new Dictionary<(AvatarMask, AvatarMask), AvatarMask>();

    HashSet<string> generatedLayerNames = new HashSet<string>();

    // This mask will mask all other layer masks from the gesture animator, and is derived from the
    // *first* layer.
    AvatarMask gestureMask;

    AvatarMask emptyMask;
    AvatarMask fullMask;
    AvatarMask musclesOnlyMask;

    // must match the directory on disk exactly: on a case-sensitive filesystem a mismatch makes
    // every mask load return null, which leaves layers unmasked instead of raising anything
    static readonly string EditorMaskDir = "Assets/PeanutTools/VRC3CVR/Editor";

    static AvatarMask LoadMask(string fileName) =>
        (AvatarMask)AssetDatabase.LoadAssetAtPath($"{EditorMaskDir}/{fileName}", typeof(AvatarMask));

    // Hands combined from both ChilloutVR animationClips
    AnimationClip handCombinedFistAnimationClip;
    AnimationClip handCombinedGunAnimationClip;
    AnimationClip handCombinedOpenAnimationClip;
    AnimationClip handCombinedPeaceAnimationClip;
    AnimationClip handCombinedPointAnimationClip;
    AnimationClip handCombinedRelaxedAnimationClip;
    AnimationClip handCombinedRockNRollAnimationClip;
    AnimationClip handCombinedThumbsUpAnimationClip;

    public bool GetIsReadyForConvert()
    {
        return vrcAvatarDescriptor != null;
    }

    void SetAnimator()
    {
        // this is not necessary for VRC or CVR but it helps people test their controller
        animator = chilloutAvatarGameObject.GetComponent<Animator>();
        animator.runtimeAnimatorController = chilloutAnimatorController;
    }

    void CreateChilloutAvatar()
    {
        if (shouldCloneAvatar)
        {
            chilloutAvatarGameObject = UnityEngine.Object.Instantiate(vrcAvatarDescriptor.gameObject);
            chilloutAvatarGameObject.name = vrcAvatarDescriptor.gameObject.name + " (ChilloutVR)";
            chilloutAvatarGameObject.SetActive(true);
        }
        else
        {
            chilloutAvatarGameObject = vrcAvatarDescriptor.gameObject;
        }
    }

    void HideOriginalAvatar()
    {
        vrcAvatarDescriptor.gameObject.SetActive(false);
    }

    public void Convert()
    {
        if (isConverting == true)
        {
            Debug.Log("Cannot convert - already in progress");
            return;
        }

        _emptyClip = null;

        isConverting = true;

        try
        {
            Debug.Log("Starting to convert...");

            AssetDatabase.Refresh();

            // Generate Combined hand animations
            CreateCombinedHandAnimations();

            // Clear the cache
            avatarMaskCombineCache = new Dictionary<(AvatarMask, AvatarMask), AvatarMask>();
            gestureMask = null;
            generatedLayerNames = new HashSet<string>();

            // Load masks
            emptyMask = LoadMask("vrc3cvrEmptyMask.mask");
            fullMask = LoadMask("vrc3cvrFullMask.mask");
            musclesOnlyMask = LoadMask("vrc3cvrMusclesOnly.mask");

            CreateChilloutAvatar();
            GetValuesFromVrcAvatar();
            CreateChilloutComponentIfNeeded();
            PopulateChilloutComponent();
            CreateEmptyChilloutAnimator();
            MergeVrcAnimatorsIntoChilloutAnimator();
            contactReceiverParameters = new HashSet<string>();
            if (convertVRCContactSendersAndReceivers)
            {
                ConvertContactsToCVRComponents();
                ExcludeContactsFromDynamicBones();
                RemapAnimationOfContactComponent();
                MakeProxyLayersOfConstantContactParameters();
                EnsureLocalOnlyContacts();
            }
            if (createVRCContactEquivalentPointers)
            {
                CreateVRCContactEquivalentPointers();
            }
            if (convertVrcConstraints)
            {
                ConvertVrcConstraintsToUnityConstraints();
                RemapAnimationOfConstraintComponent();
            }
            SetAnimator();
            ConvertVrcParametersToChillout();
            SetNonZeroDefaultValueParameters();
            AdjustParameterNames();
            MakeGestureWeightFeedLayers();
            MakeVelocityMagnitudeFeedLayer();
            MakeVrcEmoteCompatFeedLayer();
            // After AdjustParameterNames, like the feed layers above, so the names are final.
            RemapVelocityToAvatarLocal();
            // Before the streams, twice over: they route AvatarUpright at whatever this leaves
            // reading it, and this is what declares VRMode, without which they emit no DeviceMode
            // entry at all -- and an unfed VRMode reads 0, silently discretising Upright in VR.
            MakeUprightFeedLayer();
            // Before the streams too: this is what declares the full body flag they route
            MakeTrackingTypeFeedLayer();
            MakeGameStateParameterStreams();
            InsertChilloutOverride();

            ConvertVrcComponents();
            if (shouldDeleteVRCAvatarDescriptorAndPipelineManager)
            {
                DeleteVrcComponents();
            }

            if (shouldCloneAvatar)
            {
                HideOriginalAvatar();
            }

            if (saveAssets)
            {
                SaveChilloutAnimator();
                SaveChilloutOverride();
            }

            // Clear the cache
            avatarMaskCombineCache = new Dictionary<(AvatarMask, AvatarMask), AvatarMask>();
            gestureMask = null;
            generatedLayerNames = new HashSet<string>();

            Debug.Log("Conversion complete!");
        }
        finally
        {
            isConverting = false;
        }
    }

    Transform GetHeadBoneTransform(Animator animator)
    {
        if (animator)
        {
            return animator.GetBoneTransform(HumanBodyBones.Head);
        }
        else
        {
            return null;
        }
    }

    void InsertChilloutOverride()
    {
        Debug.Log("Inserting chillout override controller...");

        AnimatorOverrideController overrideController = new AnimatorOverrideController(chilloutAnimatorController);
        overrideController.name = chilloutAvatarGameObject.name + "_ChilloutVR Overrides";

        cvrAvatar.overrides = overrideController;

        EditorUtility.SetDirty(cvrAvatar);

        Debug.Log("Inserted!");
    }

    void SaveChilloutOverride()
    {
        AssetDatabase.CreateAsset(cvrAvatar.overrides, "Assets/" + outputDirName + "/" + cvrAvatar.overrides.name + ".overrideController");
    }

    void ConvertVrcComponents()
    {
        if (convertVrcHeadChops) ConvertVrcHeadChops();
        if (convertVrcSpatialAudioSources) ConvertVrcAudio();
    }

    void ConvertVrcHeadChops()
    {
        var headchops = chilloutAvatarGameObject.GetComponentsInChildren<VRCHeadChop>(true);
        foreach (var headchop in headchops)
        {
            foreach (var setting in headchop.targetBones)
            {
                // TODO: Apply Condition (anim emulation required)
                if (setting.transform == null)
                {
                    continue;
                }
                var scaleFactor = setting.scaleFactor * headchop.globalScaleFactor;
                var isShown = Mathf.Approximately(scaleFactor, 1f);
                var isHidden = Mathf.Approximately(scaleFactor, 0f);
                // ignore other scale factors (cannot convert)
                if (isShown || isHidden)
                {
                    Debug.Log($"Converting VRCHeadChop on {setting.transform.gameObject.name} to FPRExclusion (isShown={isShown})");
                    var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(chilloutAvatarGameObject.transform, "VRCHeadChop"));
                    var fprExclusion = go.AddComponent<FPRExclusion>();
                    fprExclusion.isShown = isShown;
                    fprExclusion.shrinkToZero = true;
                    fprExclusion.target = setting.transform;
                    go.transform.SetParent(chilloutAvatarGameObject.transform, false);
                }
                else
                {
                    Debug.LogWarning($"Cannot convert VRCHeadChop on {setting.transform.gameObject.name} with scaleFactor={scaleFactor}");
                }
            }
            UnityEngine.Object.DestroyImmediate(headchop);
        }
    }

    void ConvertVrcAudio()
    {
        var vrcSpatialAudioSources = chilloutAvatarGameObject.GetComponentsInChildren<VRCSpatialAudioSource>(true);
        var onspAudioSources = chilloutAvatarGameObject.GetComponentsInChildren<ONSPAudioSource>(true);
        Debug.Log($"Converting {vrcSpatialAudioSources.Length} VRCSpatialAudioSource and {onspAudioSources.Length} ONSPAudioSource components...");

        foreach (var spatial in vrcSpatialAudioSources)
        {
            Debug.Log($"Converting VRCSpatialAudioSource on {spatial.gameObject.name}");
            var audioSource = spatial.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.spatialBlend = spatial.EnableSpatialization ? 1f : 0f;
                if (!spatial.UseAudioSourceVolumeCurve)
                {
                    audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                    audioSource.minDistance = spatial.Near;
                    audioSource.maxDistance = spatial.Far;
                    audioSource.volume = spatial.Gain / 10f; // Gain ???
                }
                EditorUtility.SetDirty(audioSource);
            }
            UnityEngine.Object.DestroyImmediate(spatial);
        }
        foreach (var onsp in onspAudioSources)
        {
            Debug.Log($"Converting AudioSource on {onsp.gameObject.name}");
            var audioSource = onsp.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.spatialBlend = onsp.EnableSpatialization ? 1f : 0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.minDistance = onsp.Near;
                audioSource.maxDistance = onsp.Far;
                audioSource.volume = onsp.Gain / 10f; // Gain ???
                EditorUtility.SetDirty(audioSource);
            }
            UnityEngine.Object.DestroyImmediate(onsp);
        }
    }

    void DeleteVrcComponents()
    {
        Debug.Log("Deleting VRC components...");

        UnityEngine.Object.DestroyImmediate(chilloutAvatarGameObject.GetComponent(typeof(VRC.Core.PipelineManager)));

        var vrcComponents = chilloutAvatarGameObject.GetComponentsInChildren(typeof(Component), true).ToList().Where(c => c.GetType().Name.StartsWith("VRC")).ToList();

        if (vrcComponents.Count > 0)
        {
            Debug.Log("Found " + vrcComponents.Count + " VRC components");

            foreach (var component in vrcComponents)
            {
                string componentName = component.GetType().Name;

                if (!shouldDeletePhysBones && componentName.Contains("PhysBone"))
                {
                    continue;
                }

                Debug.Log(component.name + "." + componentName);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        Debug.Log("VRC components deleted");
    }

    List<int> GetAllIntOptionsForParamFromAnimatorController(string paramName, AnimatorController animatorController)
    {
        // TODO: Check special "any state" property

        List<int> results = new List<int>();

        foreach (AnimatorControllerLayer layer in animatorController.layers)
        {
            foreach (ChildAnimatorState state in layer.stateMachine.states)
            {
                foreach (AnimatorStateTransition transition in state.state.transitions)
                {
                    foreach (AnimatorCondition condition in transition.conditions)
                    {
                        if (condition.parameter == paramName && results.Contains((int)condition.threshold) == false)
                        {
                            Debug.Log("Adding " + condition.threshold + " as option for param " + paramName);
                            results.Add((int)condition.threshold);
                        }
                    }
                }
            }
        }

        return results;
    }

    List<int> GetAllIntOptionsForParam(string paramName)
    {
        List<int> results = new List<int>();

        Debug.Log("Getting all int options for param \"" + paramName + "\"...");

        for (int i = 0; i < vrcAnimatorControllers.Length; i++)
        {
            // if the user has not selected anything
            if (vrcAnimatorControllers[i] == null)
            {
                continue;
            }

            List<int> newResults = GetAllIntOptionsForParamFromAnimatorController(paramName, vrcAnimatorControllers[i]);

            foreach (int newResult in newResults)
            {
                if (results.Contains(newResult) == false)
                {
                    results.Add(newResult);
                }
            }
        }

        Debug.Log("Found " + results.Count + " int options: " + string.Join(", ", results.ToArray()));

        if (results.Count == 0)
        {
            Debug.Log("Found 0 int options for param " + paramName + " - this is probably not what you want!");
        }

        return results;
    }

    List<CVRAdvancedSettingsDropDownEntry> ConvertIntToGameObjectDropdownOptions(List<int> ints)
    {
        List<CVRAdvancedSettingsDropDownEntry> entries = new List<CVRAdvancedSettingsDropDownEntry>();

        ints.Sort();

        foreach (int value in ints)
        {
            entries.Add(new CVRAdvancedSettingsDropDownEntry()
            {
                name = value.ToString()
            });
        }

        return entries;
    }

    void MatchAnimatorParameterToVRCParameter(VRCExpressionParameter vrcParam)
    {
        AnimatorControllerParameter[] parameters = chilloutAnimatorController.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == vrcParam.name)
            {
                switch (parameters[i].type)
                {
                    case AnimatorControllerParameterType.Bool:
                        parameters[i].defaultBool = vrcParam.defaultValue == 1 ? true : false;
                        break;
                    case AnimatorControllerParameterType.Int:
                        parameters[i].defaultInt = (int)vrcParam.defaultValue;
                        break;
                    case AnimatorControllerParameterType.Float:
                        parameters[i].defaultFloat = vrcParam.defaultValue;
                        break;
                }
            }
        }
        chilloutAnimatorController.parameters = parameters;
    }

    List<string> parameterOrder;
    void AddParameterOrder(string name)
    {
        if (!parameterOrder.Contains(name))
        {
            parameterOrder.Add(name);
        }
    }
    HashSet<string> impulseParameters;

    class MenuNameAndType
    {
        public readonly VRCExpressionsMenu.Control.ControlType type;
        public readonly string name;
        public MenuNameAndType(VRCExpressionsMenu.Control.ControlType type, string name)
        {
            this.type = type;
            this.name = name;
        }
        public MenuNameAndType Name(string name)
        {
            return new MenuNameAndType(type, name);
        }
        public bool IsButton
        {
            get => type == VRCExpressionsMenu.Control.ControlType.Button;
        }
    }


    Dictionary<string, Dictionary<float, MenuNameAndType>> FindMenuButtonsAndToggles(VRCExpressionsMenu menu, Dictionary<string, Dictionary<float, MenuNameAndType>> toggleTable, string[] subMenuStack)
    {
        var basePath = string.Join("", subMenuStack.Select(s => s + "/"));
        if (menu != null)
        {
            void TreatChanging(VRCExpressionsMenu.Control control)
            {
                if (!string.IsNullOrEmpty(control.parameter.name))
                {
                    AddParameterOrder(control.parameter.name);
                    if (!toggleTable.TryGetValue(control.parameter.name, out var idTable))
                    {
                        idTable = new Dictionary<float, MenuNameAndType>();
                    }
                    // The "changing" indicator is a boolean flag that VRChat sets to true (1) while
                    // the puppet is actively being manipulated; control.value is not meaningful here
                    // (it only applies to Toggle/Button controls), so the guard must check the same
                    // key (1) that is actually added below. Checking control.value instead let this
                    // collide with a pre-existing key-1 entry (e.g. a Toggle at value 1 on the same
                    // parameter) and throw ArgumentException from idTable.Add.
                    if (!idTable.ContainsKey(1))
                    {
                        idTable.Add(1, new MenuNameAndType(control.type, $"{basePath}{control.name} Changing"));
                    }
                    toggleTable[control.parameter.name] = idTable;
                }
            }
            void TreatLabeledSubParameter(VRCExpressionsMenu.Control control, int index, int labelIndex, string fallbackSuffix)
            {
                if (control.subParameters != null && control.subParameters.Length > index && control.subParameters[index] != null && !string.IsNullOrEmpty(control.subParameters[index].name))
                {
                    var parameterName = control.subParameters[index].name;
                    AddParameterOrder(parameterName);
                    if (!toggleTable.TryGetValue(parameterName, out var idTable))
                    {
                        idTable = new Dictionary<float, MenuNameAndType>();
                    }
                    if (!idTable.ContainsKey(float.NaN))
                    {
                        idTable.Add(float.NaN, new MenuNameAndType(control.type, control.labels != null && control.labels.Length > labelIndex && !string.IsNullOrWhiteSpace(control.labels[labelIndex].name) ? $"{basePath}{control.name} {control.labels[labelIndex].name}" : $"{basePath}{control.name} {fallbackSuffix}"));
                    }
                    toggleTable[parameterName] = idTable;
                }
            }
            foreach (VRCExpressionsMenu.Control control in menu.controls)
            {
                if (control.type == VRCExpressionsMenu.Control.ControlType.Toggle || control.type == VRCExpressionsMenu.Control.ControlType.Button)
                {
                    AddParameterOrder(control.parameter.name);
                    Dictionary<float, MenuNameAndType> idTable;
                    if (toggleTable.ContainsKey(control.parameter.name))
                    {
                        idTable = toggleTable[control.parameter.name];
                    }
                    else
                    {
                        idTable = new Dictionary<float, MenuNameAndType>();
                    }

                    if (!idTable.ContainsKey(control.value))
                    {
                        idTable.Add(control.value, new MenuNameAndType(control.type, basePath + control.name));
                    }

                    toggleTable[control.parameter.name] = idTable;
                }
                else if (control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                {
                    TreatChanging(control);
                    if (control.subParameters != null && control.subParameters.Length >= 1 && control.subParameters[0] != null && !string.IsNullOrEmpty(control.subParameters[0].name))
                    {
                        var parameterName = control.subParameters[0].name;
                        AddParameterOrder(parameterName);
                        if (!toggleTable.TryGetValue(parameterName, out var idTable))
                        {
                            idTable = new Dictionary<float, MenuNameAndType>();
                        }
                        if (!idTable.ContainsKey(float.NaN))
                        {
                            idTable.Add(float.NaN, new MenuNameAndType(control.type, basePath + control.name));
                        }
                        toggleTable[parameterName] = idTable;
                    }
                }
                else if (control.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet)
                {
                    TreatChanging(control);
                    TreatLabeledSubParameter(control, 0, 1, "Horizontal");
                    TreatLabeledSubParameter(control, 1, 0, "Vertical");
                }
                else if (control.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet)
                {
                    TreatChanging(control);
                    TreatLabeledSubParameter(control, 0, 0, "Up");
                    TreatLabeledSubParameter(control, 1, 1, "Right");
                    TreatLabeledSubParameter(control, 2, 2, "Down");
                    TreatLabeledSubParameter(control, 3, 3, "Left");
                }
                else if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    toggleTable = FindMenuButtonsAndToggles(control.subMenu, toggleTable, subMenuStack.Concat(new string[] { control.name }).ToArray());
                }
            }
        }

        return toggleTable;
    }

    void ConvertVrcParametersToChillout()
    {
        Debug.Log("Converting vrc parameters to chillout...");

        VRCExpressionParameters vrcParams = vrcAvatarDescriptor.expressionParameters;

        List<CVRAdvancedSettingsEntry> newParams = new List<CVRAdvancedSettingsEntry>();

        parameterOrder = new List<string>();
        impulseParameters = new HashSet<string>();
        Dictionary<string, Dictionary<float, MenuNameAndType>> toggleTable = FindMenuButtonsAndToggles(vrcAvatarDescriptor.expressionsMenu, new Dictionary<string, Dictionary<float, MenuNameAndType>>(), new string[0]);

        for (int i = 0; i < vrcParams?.parameters?.Length; i++)
        {
            VRCExpressionParameter vrcParam = vrcParams.parameters[i];

            Debug.Log("Param \"" + vrcParam.name + "\" type \"" + vrcParam.valueType + "\" default \"" + vrcParam.defaultValue + "\"");

            if (vrcParam.name == "")
            {
                Debug.Log("Empty-named parameter. Skipping.");
                continue;
            }

            CVRAdvancedSettingsEntry newParam = null;

            switch (vrcParam.valueType)
            {
                case VRCExpressionParameters.ValueType.Int:
                    if (toggleTable.TryGetValue(vrcParam.name, out var intIdTable))
                    {
                        // float.NaN keys come from TreatLabeledSubParameter/RadialPuppet's subParameter
                        // handling: they register the parameter that continuously drives a puppet axis
                        // (radial/2D/4D). An Int can be picked there -- the SDK only warns about Bool --
                        // though it barely works in VRChat either, where the axis only ever drives 0 and 1.
                        // Such an entry has no discrete "selected option" value, so it must be excluded
                        // from the dropdown's option range/count calculations below, or a puppet-only Int
                        // parameter (NaN is its only key) turns into (int)NaN range math and an empty
                        // option list. Losing the menu entry is acceptable; crashing the conversion is not.
                        var discreteIdTable = intIdTable.Where(p => !float.IsNaN(p.Key)).ToDictionary(p => p.Key, p => p.Value);

                        if (discreteIdTable.Count == 0)
                        {
                            Debug.LogWarning($"Int parameter \"{vrcParam.name}\" is only referenced as a puppet sub-parameter, where VRChat itself only drives it between 0 and 1. It has no discrete options to build a menu entry from, so no CVR menu entry is generated for it (the animator parameter itself is still converted).");
                        }
                        else if (discreteIdTable.Count == 1 && discreteIdTable.First().Key == 1)
                        {
                            var menuNameAndType = discreteIdTable.First().Value;
                            Debug.Log("Param has only one option and value = 1 so we are making a toggle instead");
                            newParam = new CVRAdvancedSettingsEntry()
                            {
                                name = MenuName(menuNameAndType.name),
                                machineName = vrcParam.name,
                                unlinkNameFromMachineName = true,
                                setting = new CVRAdvancesAvatarSettingGameObjectToggle()
                                {
                                    defaultValue = vrcParam.defaultValue == 1 ? true : false,
                                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool
                                },
                            };
                            if (menuNameAndType.type == VRCExpressionsMenu.Control.ControlType.Button)
                            {
                                impulseParameters.Add(vrcParam.name);
                            }
                        }
                        else
                        {
                            // control.value (and therefore the key here) can be negative; start the
                            // range at the lowest key instead of assuming it is always 0, or negative
                            // options are silently dropped from the resulting dropdown.
                            var firstIndex = (int)discreteIdTable.Keys.Min();
                            var lastIndex = (int)discreteIdTable.Keys.Max();
                            var menuEntryNames = new List<string>();
                            for (var j = firstIndex; j < lastIndex + 1; j++)
                            {
                                menuEntryNames.Add(discreteIdTable.TryGetValue(j, out var menuEntry) ? menuEntry.name : "---");
                            }
                            var menuName = GetMenuNameCommonParent(menuEntryNames.Where(name => name != "---"));
                            menuEntryNames = menuEntryNames.Select(name =>
                            {
                                if (name == "---") return "---";
                                // menuName is the shared submenu prefix (without a trailing "/"); when the
                                // toggles live directly in the root menu there is no common submenu, so
                                // GetMenuNameCommonParent returns "" and there is no "/" separator to skip.
                                if (useHierarchicalDropdownMenuName) return string.IsNullOrEmpty(menuName) ? name : name.Substring(menuName.Length + 1);
                                return MenuNameWithoutStack(name);
                            }).ToList();
                            newParam = new CVRAdvancedSettingsEntry()
                            {
                                name = menuName,
                                machineName = vrcParam.name,
                                unlinkNameFromMachineName = true,
                                type = CVRAdvancedSettingsEntry.SettingsType.Dropdown,
                                setting = new CVRAdvancesAvatarSettingGameObjectDropdown()
                                {
                                    defaultValue = (int)vrcParam.defaultValue,
                                    options = menuEntryNames.Select(name => new CVRAdvancedSettingsDropDownEntry { name = name }).ToList(),
                                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Int
                                }
                            };
                            if (discreteIdTable.Values.All(v => v.type == VRCExpressionsMenu.Control.ControlType.Button))
                            {
                                impulseParameters.Add(vrcParam.name);
                            }
                        }
                    }
                    break;

                case VRCExpressionParameters.ValueType.Float:
                    if (toggleTable.TryGetValue(vrcParam.name, out var floatIdTable) && floatIdTable.Count > 0)
                    {
                        var menuNameAndType = floatIdTable.First().Value;
                        newParam = new CVRAdvancedSettingsEntry()
                        {
                            name = MenuName(menuNameAndType.name) ?? vrcParam.name,
                            machineName = vrcParam.name,
                            unlinkNameFromMachineName = true,
                            type = CVRAdvancedSettingsEntry.SettingsType.Slider,
                            setting = new CVRAdvancesAvatarSettingSlider()
                            {
                                defaultValue = vrcParam.defaultValue,
                                usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float
                            }
                        };
                        if (menuNameAndType.type == VRCExpressionsMenu.Control.ControlType.Button)
                        {
                            impulseParameters.Add(vrcParam.name);
                        }
                    }
                    break;

                case VRCExpressionParameters.ValueType.Bool:
                    if (toggleTable.TryGetValue(vrcParam.name, out var idTable) && idTable.Count > 0)
                    {
                        var menuNameAndType = idTable.OrderBy(p => p.Key == 1 ? float.PositiveInfinity : p.Key).Last().Value;
                        newParam = new CVRAdvancedSettingsEntry()
                        {
                            name = MenuName(menuNameAndType.name) ?? vrcParam.name,
                            machineName = vrcParam.name,
                            unlinkNameFromMachineName = true,
                            setting = new CVRAdvancesAvatarSettingGameObjectToggle()
                            {
                                defaultValue = vrcParam.defaultValue != 0 ? true : false,
                                usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool
                            }
                        };
                        if (menuNameAndType.type == VRCExpressionsMenu.Control.ControlType.Button)
                        {
                            impulseParameters.Add(vrcParam.name);
                        }
                    }
                    break;

                default:
                    throw new Exception("Cannot convert vrc parameter to chillout: unknown type \"" + vrcParam.valueType + "\"");
            }

            MatchAnimatorParameterToVRCParameter(vrcParam);

            if (newParam != null)
            {
                newParams.Add(newParam);
            }
        }

        if (adjustToVrcMenuOrder)
        {
            newParams = newParams.OrderBy(p =>
            {
                var index = parameterOrder.IndexOf(p.machineName);
                return index == -1 ? int.MaxValue : index;
            }).ToList();
        }

        cvrAvatar.avatarSettings.settings = newParams;

        Debug.Log("Finished converting vrc params");
    }

    string MenuName(string menuName)
    {
        if (useHierarchicalMenuName)
        {
            return menuName;
        }
        return MenuNameWithoutStack(menuName);
    }

    string MenuNameWithoutStack(string menuName)
    {
        if (string.IsNullOrEmpty(menuName)) return menuName;
        var slashIndex = menuName.LastIndexOf('/');
        if (slashIndex != -1)
        {
            return menuName.Substring(slashIndex + 1);
        }
        else
        {
            return menuName;
        }
    }

    string GetMenuNameCommonParent(IEnumerable<string> menuNames)
    {
        var commonStack = menuNames.First().Split("/").SkipLast(1).ToArray();
        foreach (var menuName in menuNames)
        {
            var stack = menuName.Split("/").SkipLast(1).ToArray();
            for (var i = 0; i < commonStack.Length; i++)
            {
                if (i >= stack.Length || commonStack[i] != stack[i])
                {
                    commonStack = commonStack.Take(i).ToArray();
                    break;
                }
            }
        }
        return string.Join("/", commonStack);
    }

    static HashSet<string> PreDefinedParameterNames = new HashSet<string>
    {
        "MovementX",
        "MovementY",
        "Grounded",
        "Emote",
        "CancelEmote",
        "GestureLeft",
        "GestureRight",
        "GestureLeftIdx",
        "GestureRightIdx",
        "Toggle",
        "Sitting",
        "Crouching",
        "Prone",
        "Flying",
        "Swimming",
        "IsLocal",
        "DistanceTo",
        "VisemeIdx",
        "VisemeLoudness",
        "IsFriend",
        "VelocityX",
        "VelocityY",
        "VelocityZ",
        "AFK",
    };

    static Dictionary<string, string> parameterRenameMap = new Dictionary<string, string>
    {
        { "Viseme", "VisemeIdx" },
        { "Voice", "VisemeLoudness" },
        { "Seated", "Sitting" },
        { "InStation", "Sitting" },
        { "IsOnFriendsList", "IsFriend" },
    };

    static Dictionary<string, float> nonZeroDefaultValueMap = new Dictionary<string, float>
    {
        { "Grounded", 1f },
        { "ScaleFactor", 1f },
        { "ScaleFactorInverse", 1f },
        { "EyeHeightAsPercent", 1f },
        // zero is the prone band, so an unset Upright shows a frame of lying down on load
        { "Upright", 1f },
    };

    HashSet<string> preserveParameters;

    // A humanoid clip's muscle, IK-goal and root-motion curves are bound exactly like a curve that
    // writes an animator parameter -- typeof(Animator), empty path -- and Unity hands some of them
    // back under names it cannot resolve ("unknown_*"), which no allow-list can enumerate. Whether
    // the animator declares a parameter of that name is the only thing that tells the two apart.
    // Snapshotted before the declarations are renamed, since the curves still carry the old names.
    HashSet<string> declaredParameterNames = new HashSet<string>();

    static HashSet<string> _muscleNames;
    static HashSet<string> muscleNames
    {
        get
        {
            if (_muscleNames == null)
            {
                _muscleNames = new HashSet<string>(HumanTrait.MuscleName.Select(name =>
                {
                    var match = handRe.Match(name);
                    if (match.Success)
                    {
                        return $"{match.Groups[1].Value}Hand.{match.Groups[2].Value}.{match.Groups[3].Value}";
                    }
                    return name;
                }));
                _muscleNames.UnionWith(
                    new string[]
                    {
                        "Motion",
                        "Root",
                        "LeftHand",
                        "RightHand",
                        "LeftFoot",
                        "RightFoot",
                    }
                    .SelectMany(basename => new string[] { $"{basename}Q", $"{basename}T" })
                    .SelectMany(basename => new string[] { "x", "y", "z", "w" }.Select(a => $"{basename}.{a}"))
                    );
            }
            return _muscleNames;
        }
    }
    static System.Text.RegularExpressions.Regex handRe = new System.Text.RegularExpressions.Regex(@"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.*)$");

    void AdjustParameterNames()
    {
        if (preserveParameterSyncState)
        {
            // avatars without an expression menu have no expression parameters at all
            preserveParameters = vrcAvatarDescriptor.expressionParameters?.parameters?.Where(p => p.networkSynced).Select(p => p.name).ToHashSet() ?? new HashSet<string>();
            preserveParameters.UnionWith(PreDefinedParameterNames);
            preserveParameters.UnionWith(muscleNames);
        }
        else
        {
            // all
            preserveParameters = vrcAvatarDescriptor.expressionParameters?.parameters?.Select(p => p.name).ToHashSet() ?? new HashSet<string>();
            preserveParameters.UnionWith(chilloutAnimatorController.parameters.Select(p => p.name));
            preserveParameters.UnionWith(muscleNames);
        }
        if (adjustContactParameterSync) preserveParameters.UnionWith(contactReceiverParameters);
        // Stream-fed parameters only run on the wearer's client; keeping them synced (no # prefix)
        // lets CVR's normal parameter sync carry the values to remotes (see MakeGameStateParameterStreams)
        if (feedGameStateParameters) preserveParameters.UnionWith(GameStateParameterStreams.Select(s => s.parameterName));
        // an avatar whose locomotion replaced CVR's derives Upright rather than receiving it, so it
        // stops being a synced input and becomes a driven local value (MakeUprightFeedLayer, same guard)
        if (feedGameStateParameters && vrcBaseReplacesCckLocomotion) preserveParameters.Remove("Upright");
        // likewise TrackingType, which every client derives from the synced flag instead of receiving
        // (MakeTrackingTypeFeedLayer)
        if (feedGameStateParameters) preserveParameters.Remove("TrackingType");
        if (!addActionMenuModAnnotations)
        {
            impulseParameters = new HashSet<string>();
        }

        AdjustParameterNamesOnAnimator();
        AdjustParameterNamesOnAdvancedSettings();
        AdjustParameterNamesOnCVRAdvancedAvatarSettingsTrigger();
    }

    void AdjustParameterNamesOnAnimator()
    {
        var parameters = chilloutAnimatorController.parameters;
        declaredParameterNames = parameters.Select(p => p.name).ToHashSet();
        for (var i = 0; i < parameters.Length; ++i)
        {
            var t = GetRenameParameterType(parameters[i].name);
            if (t != RenameParameterType.None)
            {
                var param = parameters[i];
                param.name = RenameParameterName(param.name, t);
                parameters[i] = param;
            }
        }
        // duplicate(by rename) removal
        var parameterSet = new HashSet<string>();
        var newParameters = new List<AnimatorControllerParameter>();
        for (var i = 0; i < parameters.Length; ++i)
        {
            if (parameterSet.Add(parameters[i].name))
            {
                newParameters.Add(parameters[i]);
            }
        }
        chilloutAnimatorController.parameters = newParameters.ToArray();

        foreach (var layer in chilloutAnimatorController.layers)
        {
            AdjustParameterNamesOnStateMachine(layer.stateMachine);
        }
    }

    void AdjustParameterNamesOnStateMachine(AnimatorStateMachine stateMachine)
    {
        var anyStateTransitions = stateMachine.anyStateTransitions;
        if (AdjustParameterNamesOnTransitions(anyStateTransitions))
        {
            stateMachine.anyStateTransitions = anyStateTransitions;
        }
        var entryTransitions = stateMachine.entryTransitions;
        if (AdjustParameterNamesOnTransitions(entryTransitions))
        {
            stateMachine.entryTransitions = entryTransitions;
        }
        foreach (var childState in stateMachine.states)
        {
            AdjustParameterNamesOnState(childState.state);
            var transitions = childState.state.transitions;
            if (AdjustParameterNamesOnTransitions(transitions))
            {
                childState.state.transitions = transitions;
            }
            var behaviours = childState.state.behaviours;
            foreach (var behaviour in behaviours)
            {
                if (behaviour is AnimatorDriver driver)
                {
                    foreach (var task in driver.EnterTasks)
                    {
                        AdjustParameterNamesOnAnimatorDriverTask(task);
                    }
                    foreach (var task in driver.ExitTasks)
                    {
                        AdjustParameterNamesOnAnimatorDriverTask(task);
                    }
                }
            }
            if (childState.state.motion is BlendTree blendTree)
            {
                childState.state.motion = AdjustParameterNamesOnBlendTree(blendTree);
            }
            else if (childState.state.motion is AnimationClip clip)
            {
                childState.state.motion = AdjustParameterNamesOnAnimationClip(clip);
            }
        }
        foreach (var subMachine in stateMachine.stateMachines)
        {
            var transitions = stateMachine.GetStateMachineTransitions(subMachine.stateMachine);
            if (AdjustParameterNamesOnTransitions(transitions))
            {
                stateMachine.SetStateMachineTransitions(subMachine.stateMachine, transitions);
            }
        }
        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            AdjustParameterNamesOnStateMachine(childStateMachine.stateMachine);
        }
    }

    void AdjustParameterNamesOnState(AnimatorState state)
    {
        var timeParameter = state.timeParameter;
        if (!string.IsNullOrEmpty(timeParameter))
        {
            RenameParameterNameIfNeeded(ref timeParameter);
            state.timeParameter = timeParameter;
        }
        var speedParameter = state.speedParameter;
        if (!string.IsNullOrEmpty(speedParameter))
        {
            RenameParameterNameIfNeeded(ref speedParameter);
            state.speedParameter = speedParameter;
        }
        var cycleOffsetParameter = state.cycleOffsetParameter;
        if (!string.IsNullOrEmpty(cycleOffsetParameter))
        {
            RenameParameterNameIfNeeded(ref cycleOffsetParameter);
            state.cycleOffsetParameter = cycleOffsetParameter;
        }
        var mirrorParameter = state.mirrorParameter;
        if (!string.IsNullOrEmpty(mirrorParameter))
        {
            RenameParameterNameIfNeeded(ref mirrorParameter);
            state.mirrorParameter = mirrorParameter;
        }
    }

    bool AdjustParameterNamesOnTransitions(AnimatorTransitionBase[] transitions)
    {
        var changedAll = false;
        foreach (var transition in transitions)
        {
            var conditions = transition.conditions;
            var changed = false;
            for (var i = 0; i < conditions.Length; ++i)
            {
                var t = GetRenameParameterType(conditions[i].parameter);
                if (t != RenameParameterType.None)
                {
                    var condition = conditions[i];
                    condition.parameter = RenameParameterName(condition.parameter, t);
                    conditions[i] = condition;
                    changed = true;
                }
            }
            if (changed)
            {
                transition.conditions = conditions;
                changedAll = true;
            }
        }
        return changedAll;
    }

    BlendTree AdjustParameterNamesOnBlendTree(BlendTree blendTree)
    {
        BlendTree newBlendTree = null;
        BlendTree EnsureNewBlendTree()
        {
            if (newBlendTree != null) return newBlendTree;
            newBlendTree = CopyAnimatorController.CopyBlendTree(null, blendTree, false);
            newBlendTree.name = blendTree.name + "_Remapped";
            return newBlendTree;
        }
        void ChangeChild(BlendTree b, int i, Func<ChildMotion, ChildMotion> convert)
        {
            var children = b.children;
            children[i] = convert(children[i]);
            b.children = children;
        }
        {
            var t = GetRenameParameterType(blendTree.blendParameter);
            if (t != RenameParameterType.None)
            {
                EnsureNewBlendTree().blendParameter = RenameParameterName(blendTree.blendParameter, t);
            }
        }
        {
            var t = GetRenameParameterType(blendTree.blendParameterY);
            if (t != RenameParameterType.None)
            {
                EnsureNewBlendTree().blendParameterY = RenameParameterName(blendTree.blendParameterY, t);
            }
        }
        var children = blendTree.children;
        for (var i = 0; i < children.Length; ++i)
        {
            var child = children[i];
            var t = GetRenameParameterType(child.directBlendParameter);
            if (t != RenameParameterType.None)
            {
                ChangeChild(EnsureNewBlendTree(), i, cm =>
                {
                    cm.directBlendParameter = RenameParameterName(cm.directBlendParameter, t);
                    return cm;
                });
            }
            if (child.motion is BlendTree childBlendTree)
            {
                var newChildBlendTree = AdjustParameterNamesOnBlendTree(childBlendTree);
                if (newChildBlendTree != childBlendTree)
                {
                    ChangeChild(EnsureNewBlendTree(), i, cm =>
                    {
                        cm.motion = newChildBlendTree;
                        return cm;
                    });
                }
            }
            else if (child.motion is AnimationClip clip)
            {
                var newClip = AdjustParameterNamesOnAnimationClip(clip);
                if (newClip != clip)
                {
                    ChangeChild(EnsureNewBlendTree(), i, cm =>
                    {
                        cm.motion = newClip;
                        return cm;
                    });
                }
            }
        }
        return newBlendTree ?? blendTree;
    }

    AnimationClip AdjustParameterNamesOnAnimationClip(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var targets = new (EditorCurveBinding binding, RenameParameterType type)[bindings.Length];
        var j = 0;
        foreach (var binding in bindings)
        {
            if (binding.type == typeof(Animator) && declaredParameterNames.Contains(binding.propertyName))
            {
                var t = GetRenameParameterType(binding.propertyName);
                if (t != RenameParameterType.None)
                {
                    targets[j++] = (binding, t);
                }
            }
        }
        if (j == 0)
        {
            return clip;
        }
        Array.Resize(ref targets, j);
        var newClip = CopyAnimatorController.CopyAnimationClip(clip);
        newClip.name = clip.name + "_Remapped";
        foreach (var target in targets)
        {
            var binding = target.binding;
            var newBinding = binding;
            newBinding.propertyName = RenameParameterName(binding.propertyName, target.type);
            AnimationUtility.SetEditorCurve(newClip, binding, null);
            AnimationUtility.SetEditorCurve(newClip, newBinding, AnimationUtility.GetEditorCurve(clip, binding));
        }
        return newClip;
    }

    void AdjustParameterNamesOnAnimatorDriverTask(AnimatorDriverTask task)
    {
        RenameParameterNameIfNeeded(ref task.targetName);
        RenameParameterNameIfNeeded(ref task.aName);
        RenameParameterNameIfNeeded(ref task.bName);
        RenameParameterNameIfNeeded(ref task.cName);
    }

    void AdjustParameterNamesOnAdvancedSettings()
    {
        foreach (var setting in cvrAvatar.avatarSettings.settings)
        {
            RenameParameterNameIfNeeded(ref setting.machineName);
        }
    }

    void AdjustParameterNamesOnCVRAdvancedAvatarSettingsTrigger()
    {
        var triggers = cvrAvatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>();
        foreach (var trigger in triggers)
        {
            RenameParameterNameIfNeeded(ref trigger.settingName);
            foreach (var setting in trigger.enterTasks)
            {
                RenameParameterNameIfNeeded(ref setting.settingName);
            }
            foreach (var setting in trigger.exitTasks)
            {
                RenameParameterNameIfNeeded(ref setting.settingName);
            }
            foreach (var setting in trigger.stayTasks)
            {
                RenameParameterNameIfNeeded(ref setting.settingName);
            }
        }
    }

    [System.Flags]
    enum RenameParameterType
    {
        None = 0,
        NonSync = 1 << 0,
        Impulse = 1 << 1,
        Rename = 1 << 2,
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    RenameParameterType GetRenameParameterType(string name)
    {
        var type = RenameParameterType.None;
        if (!string.IsNullOrEmpty(name))
        {
            // Transition conditions, blend tree axes and AAP curve bindings call this pair directly
            // rather than through RenameParameterNameIfNeeded, so a targeted WalkParameterNames pass
            // has to branch here too or those call sites would stay blind to it.
            if (parameterRenamer != null)
            {
                return parameterRenamer(name) != name ? RenameParameterType.Rename : RenameParameterType.None;
            }
            var renamedName = name;
            if (parameterRenameMap.ContainsKey(name))
            {
                type |= RenameParameterType.Rename;
                renamedName = parameterRenameMap[name];
            }
            if (!preserveParameters.Contains(name) && !preserveParameters.Contains(renamedName))
            {
                type |= RenameParameterType.NonSync;
            }
            if (impulseParameters.Contains(name))
            {
                type |= RenameParameterType.Impulse;
            }
        }
        return type;
    }

    string RenameParameterName(string name, RenameParameterType type)
    {
        if (parameterRenamer != null)
        {
            return parameterRenamer(name);
        }
        if (type.HasFlag(RenameParameterType.Rename))
        {
            name = parameterRenameMap[name];
        }
        if (type.HasFlag(RenameParameterType.NonSync))
        {
            name = NonSyncParameterName(name);
        }
        if (type.HasFlag(RenameParameterType.Impulse))
        {
            name = ImpulseParameterName(name);
        }
        return name;
    }

    // Overrides the rename rule for the duration of a WalkParameterNames pass.
    System.Func<string, string> parameterRenamer;

    // Reuses the AdjustParameterNames* walker: it is the only complete inventory of the places a
    // parameter name can hide.
    //
    // Declarations are left alone — the original parameter is still the client-fed input its
    // replacement is computed from. Generated layers are skipped: their references are what their
    // own generator chose on purpose (the magnitude layer reads world-space VelocityX/Z
    // deliberately), so rewriting them would both corrupt that layer and manufacture a false "this
    // parameter is used" signal out of its own output.
    void WalkParameterNames(System.Func<string, string> renamer)
    {
        parameterRenamer = renamer;
        declaredParameterNames = chilloutAnimatorController.parameters.Select(p => p.name).ToHashSet();
        try
        {
            foreach (var layer in chilloutAnimatorController.layers)
            {
                if (generatedLayerNames.Contains(layer.name))
                {
                    continue;
                }
                AdjustParameterNamesOnStateMachine(layer.stateMachine);
            }
        }
        finally
        {
            parameterRenamer = null;
        }
    }

    // The one way to add a layer, so nothing can be left out of generatedLayerNames by forgetting.
    void AddGeneratedLayer(AnimatorControllerLayer layer)
    {
        chilloutAnimatorController.AddLayer(layer);
        generatedLayerNames.Add(layer.name);
    }

    void RenameParameterNameIfNeeded(ref string name)
    {
        if (parameterRenamer != null)
        {
            name = parameterRenamer(name);
            return;
        }
        var type = GetRenameParameterType(name);
        if (type != RenameParameterType.None)
        {
            name = RenameParameterName(name, type);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    string NonSyncParameterName(string name) => "#" + name;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    string ImpulseParameterName(string name) => name + "<impulse=0.1>";

    void MergeVrcAnimatorsIntoChilloutAnimator()
    {
        Debug.Log("Merging " + vrcAnimatorControllers.Length + " vrc animators into chillout animator...");

        for (int i = 0; i < vrcAnimatorControllers.Length; i++)
        {
            // if the user has not selected anything
            if (vrcAnimatorControllers[i] == null)
            {
                continue;
            }

            if (i >= (int)VRCBaseAnimatorID.MAX || i < 0)
            {
                Debug.Log("Unknown VRC animator id");
                continue;
            }

            VRCBaseAnimatorID baseAnimatorID = (VRCBaseAnimatorID)i;

            // A declined replacement leaves the CVR locomotion layer in place, so this one has to
            // go (CreateEmptyChilloutAnimator explains why the two cannot coexist). Reaching here
            // with the replacement off means the user asked for the conversion and the gate turned
            // it down: an unassigned Base layer left a null behind and the check above already
            // skipped it.
            if (baseAnimatorID == VRCBaseAnimatorID.BASE && !vrcBaseReplacesCckLocomotion)
            {
                Debug.LogWarning("Not converting the Base animator: "
                    + (HasAuthoredMotion(vrcAnimatorControllers[i])
                        ? "its first layer has no default state, so the salvaged movement modes would have nothing to hang off"
                        : "every clip in it is one of VRChat's proxy_* placeholders, which the client swaps for its own animations at runtime")
                    + ". ChilloutVR's own locomotion layer is kept instead.");
                continue;
            }

            if (baseAnimatorID == VRCBaseAnimatorID.ACTION)
            {
                if (vrcActionFoldsIntoCckLocomotion)
                {
                    FoldActionMachine(
                        chilloutAnimatorController.layers.FirstOrDefault(layer => layer.name == CckLocomotionLayerName),
                        vrcAnimatorControllers[i]);
                }
                else
                {
                    Debug.LogWarning("Not converting the Action animator: every clip in it is one of VRChat's proxy_* placeholders, which the client swaps for its own animations at runtime. ChilloutVR's own emote machine is kept instead.");
                }
                continue;
            }

            MergeVrcAnimatorIntoChilloutAnimator(vrcAnimatorControllers[i], baseAnimatorID);
        }

        // Not one of the base animation layers, so it has no turn in the loop above.
        if (vrcSittingFoldsIntoCckLocomotion)
        {
            FoldSittingMachine(
                chilloutAnimatorController.layers.FirstOrDefault(layer => layer.name == CckLocomotionLayerName),
                VrcSittingAnimatorController());
        }
        else if (convertSittingLayer)
        {
            Debug.Log("Not converting the Sitting animator: it has no seated animation of its own. ChilloutVR's own seated state is kept instead.");
        }

        Debug.Log("Finished merging all animators");
    }

    float GetChilloutGestureNumberForVrchatGestureNumber(float vrchatGestureNumber)
    {
        switch (vrchatGestureNumber)
        {
            // no gesture
            case 0:
                return 0;
            // fist
            case 1:
                return 1;
            // open hand
            case 2:
                return -1;
            // point
            case 3:
                return 4;
            // peace
            case 4:
                return 5;
            // rock n roll
            case 5:
                return 6;
            // gun
            case 6:
                return 3;
            // thumbs up
            case 7:
                return 2;
            default:
                throw new Exception("Cannot get chillout gesture number for vrchat gesture number: " + vrchatGestureNumber);
        }
    }

    AnimatorControllerParameter[] GetParametersWithoutDupes(AnimatorControllerParameter[] newParams, AnimatorControllerParameter[] existingParams)
    {
        List<AnimatorControllerParameter> finalParams = new List<AnimatorControllerParameter>(existingParams);

        for (int x = 0; x < newParams.Length; x++)
        {
            bool doesAlreadyExist = false;

            for (int y = 0; y < existingParams.Length; y++)
            {
                if (existingParams[y].name == newParams[x].name)
                {
                    doesAlreadyExist = true;
                }
            }

            //  Debug.Log("WITHOUT DUPE: " + newParams[x].name + " yes? " + (doesAlreadyExist == true ? "EXISTS" : " NO EXISTS"));

            if (doesAlreadyExist == false)
            {
                finalParams.Add(newParams[x]);
            }
        }

        return finalParams.ToArray();
    }

    AnimatorTransition[] ProcessTransitions(AnimatorTransition[] transitions, string layerName, string context)
    {
        return ProcessTransitions<AnimatorTransition>(transitions, layerName, context);
    }

    AnimatorStateTransition[] ProcessTransitions(AnimatorStateTransition[] transitions, string layerName, string context)
    {
        return ProcessTransitions<AnimatorStateTransition>(transitions, layerName, context);
    }

    AnimatorTranstitionType[] ProcessTransitions<AnimatorTranstitionType>(AnimatorTranstitionType[] transitions, string layerName, string context) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        // Built once per batch rather than once per condition -- chilloutAnimatorController.parameters
        // returns a fresh copy array on every access. By now it already holds the merged (first-wins)
        // type for every parameter this animator could reference, including ones this very animator
        // introduces (see the early CopyParametersTo call in MergeVrcAnimatorIntoChilloutAnimator).
        // chilloutAnimatorController itself is only null in tests that exercise this method in
        // isolation (see VRC3CVRGestureConversionTests); nothing is known about any parameter's type
        // there, so every condition simply passes through TryAdapt's "not found" branch unchanged.
        var parameterTypes = chilloutAnimatorController != null
            ? chilloutAnimatorController.parameters.ToDictionary(p => p.name, p => p.type)
            : new Dictionary<string, AnimatorControllerParameterType>();

        List<AnimatorTranstitionType> transitionsToAdd = new List<AnimatorTranstitionType>();

        for (int t = 0; t < transitions.Length; t++)
        {
            List<AnimatorCondition> conditionsToAdd = new List<AnimatorCondition>();
            AnimatorTranstitionType transition = transitions[t];

            // Debug.Log(transitions[t].conditions.Length + " conditions");

            ProcessTransition(transition, transitionsToAdd, conditionsToAdd, layerName, context, parameterTypes);
        }

        AnimatorTranstitionType[] newTransitions = new AnimatorTranstitionType[transitions.Length + transitionsToAdd.Count];

        transitions.CopyTo(newTransitions, 0);
        transitionsToAdd.ToArray().CopyTo(newTransitions, transitions.Length);

        return newTransitions;
    }

    // layerName/context/parameterTypes exist purely to adapt each generated condition's mode to
    // the type its parameter actually has on chilloutAnimatorController, and to warn when that
    // adaptation is not possible -- see VRC3CVRConditionTypes for why the mismatch happens.
    void ProcessTransition<AnimatorTranstitionType>(AnimatorTranstitionType transition, List<AnimatorTranstitionType> transitionsToAdd, List<AnimatorCondition> conditionsToAdd, string layerName, string context, Dictionary<string, AnimatorControllerParameterType> parameterTypes, bool isDuplicate = false) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        // Convert GestureLeft/GestureRight to ChilloutVR
        for (int c = 0; c < transition.conditions.Length; c++)
        {
            AnimatorCondition condition = transition.conditions[c];

            if (condition.parameter == "GestureLeft" || condition.parameter == "GestureRight")
            {
                if (condition.mode == AnimatorConditionMode.Greater || condition.mode == AnimatorConditionMode.Less)
                {
                    // VRChat and ChilloutVR use different gesture numbering so a numeric
                    // comparison cannot be carried over. Expand into one transition per
                    // matching VRChat gesture and convert each as an Equals condition.
                    List<float> matchingGestures = new List<float>();
                    for (int g = 0; g <= 7; g++)
                    {
                        if (condition.mode == AnimatorConditionMode.Greater ? g > condition.threshold : g < condition.threshold)
                        {
                            matchingGestures.Add(g);
                        }
                    }

                    if (matchingGestures.Count == 0)
                    {
                        // No gesture can satisfy this condition; keep the transition unreachable
                        Debug.LogWarning("Gesture condition \"" + condition.parameter + " " + condition.mode + " " + condition.threshold + "\" can never be satisfied");
                        AnimatorCondition impossibleCondition = new AnimatorCondition();
                        impossibleCondition.parameter = condition.parameter;
                        impossibleCondition.mode = AnimatorConditionMode.Greater;
                        impossibleCondition.threshold = 9999f;
                        conditionsToAdd.Add(impossibleCondition);
                        continue;
                    }

                    for (int m = 1; m < matchingGestures.Count; m++)
                    {
                        AnimatorTranstitionType newTransition = DuplicateTransitionWithGestureEquals(transition, c, matchingGestures[m]);

                        List<AnimatorTranstitionType> transitionsToAdd2 = new List<AnimatorTranstitionType>();
                        List<AnimatorCondition> conditionsToAdd2 = new List<AnimatorCondition>();

                        ProcessTransition(newTransition, transitionsToAdd2, conditionsToAdd2, layerName, context, parameterTypes, isDuplicate);
                        newTransition.conditions = conditionsToAdd2.ToArray();

                        transitionsToAdd.Add(newTransition);
                        transitionsToAdd.AddRange(transitionsToAdd2);
                    }

                    // The first match is converted in place by the Equals branch below
                    condition.mode = AnimatorConditionMode.Equals;
                    condition.threshold = matchingGestures[0];
                }

                float chilloutGestureNumber = GetChilloutGestureNumberForVrchatGestureNumber(condition.threshold);

                if (condition.mode == AnimatorConditionMode.Equals)
                {
                    float thresholdLow = (float)(chilloutGestureNumber - 0.1);
                    float thresholdHigh = (float)(chilloutGestureNumber + 0.1);

                    // Look for GestureWeight and adjust threshold
                    if (chilloutGestureNumber == 1f) // Fist only
                    {
                        thresholdLow = 0.01f;

                        if (gestureWeightConversionMode == GestureWeightConversionMode.FoldToGestureLeft)
                        {
                            for (int w = 0; w < transition.conditions.Length; w++)
                            {
                                AnimatorCondition conditionW = transition.conditions[w];
                                if (
                                    (condition.parameter == "GestureLeft" && conditionW.parameter == "GestureLeftWeight") ||
                                    (condition.parameter == "GestureRight" && conditionW.parameter == "GestureRightWeight")
                                )
                                {
                                    if (conditionW.mode == AnimatorConditionMode.Less)
                                    {
                                        thresholdHigh = conditionW.threshold;
                                    }
                                    else
                                    {
                                        thresholdLow = conditionW.threshold;
                                    }
                                }
                            }
                        }
                    }
                    else if (chilloutGestureNumber == 0f)
                    {
                        thresholdHigh = 0.01f;

                        if (gestureWeightConversionMode == GestureWeightConversionMode.FoldToGestureLeft)
                        {
                            for (int w = 0; w < transition.conditions.Length; w++)
                            {
                                AnimatorCondition conditionW = transition.conditions[w];
                                if (
                                    (condition.parameter == "GestureLeft" && conditionW.parameter == "GestureLeftWeight") ||
                                    (condition.parameter == "GestureRight" && conditionW.parameter == "GestureRightWeight")
                                )
                                {
                                    if (conditionW.mode == AnimatorConditionMode.Less)
                                    {
                                        thresholdHigh = conditionW.threshold;
                                    }
                                    else
                                    {
                                        thresholdLow = conditionW.threshold;
                                    }
                                }
                            }
                        }
                    }

                    // Create replace conditions for ChilloutVR
                    AnimatorCondition newConditionLessThan = new AnimatorCondition();
                    newConditionLessThan.parameter = condition.parameter;
                    newConditionLessThan.mode = AnimatorConditionMode.Less;
                    newConditionLessThan.threshold = thresholdHigh;

                    conditionsToAdd.Add(newConditionLessThan);

                    AnimatorCondition newConditionGreaterThan = new AnimatorCondition();
                    newConditionGreaterThan.parameter = condition.parameter;
                    newConditionGreaterThan.mode = AnimatorConditionMode.Greater;
                    newConditionGreaterThan.threshold = thresholdLow;

                    conditionsToAdd.Add(newConditionGreaterThan);
                }
                else if (condition.mode == AnimatorConditionMode.NotEqual)
                {
                    float thresholdLow = (float)(chilloutGestureNumber - 0.1);
                    float thresholdHigh = (float)(chilloutGestureNumber + 0.1);

                    if (chilloutGestureNumber == 1f) // Fist only
                    {
                        thresholdLow = 0.01f;
                    }
                    else if (chilloutGestureNumber == 0f)
                    {
                        thresholdHigh = 0.01f;
                    }

                    if (isDuplicate)
                    {
                        // Add greater than transition to duplicate
                        AnimatorCondition newConditionGreaterThan = new AnimatorCondition();
                        newConditionGreaterThan.parameter = condition.parameter;
                        newConditionGreaterThan.mode = AnimatorConditionMode.Greater;
                        newConditionGreaterThan.threshold = thresholdHigh;

                        conditionsToAdd.Add(newConditionGreaterThan);

                    }
                    else
                    {
                        // Change transition to use less than
                        AnimatorCondition newConditionLessThan = new AnimatorCondition();
                        newConditionLessThan.parameter = condition.parameter;
                        newConditionLessThan.mode = AnimatorConditionMode.Less;
                        newConditionLessThan.threshold = thresholdLow;

                        conditionsToAdd.Add(newConditionLessThan);

                        // Duplicate transition to create the "or greater than" transition
                        AnimatorTranstitionType newTransition = new AnimatorTranstitionType();
                        if (newTransition is AnimatorStateTransition)
                        {
                            AnimatorStateTransition newTransitionTyped = newTransition as AnimatorStateTransition;
                            AnimatorStateTransition transitionTyped = transition as AnimatorStateTransition;
                            newTransitionTyped.duration = transitionTyped.duration;
                            newTransitionTyped.canTransitionToSelf = transitionTyped.canTransitionToSelf;
                            newTransitionTyped.exitTime = transitionTyped.exitTime;
                            newTransitionTyped.hasExitTime = transitionTyped.hasExitTime;
                            newTransitionTyped.hasFixedDuration = transitionTyped.hasFixedDuration;
                            newTransitionTyped.interruptionSource = transitionTyped.interruptionSource;
                            newTransitionTyped.offset = transitionTyped.offset;
                            newTransitionTyped.orderedInterruption = transitionTyped.orderedInterruption;
                        }

                        newTransition.name = transition.name;
                        newTransition.destinationState = transition.destinationState;
                        newTransition.destinationStateMachine = transition.destinationStateMachine;
                        newTransition.hideFlags = transition.hideFlags;
                        newTransition.isExit = transition.isExit;
                        newTransition.solo = transition.solo;
                        newTransition.mute = transition.mute;

                        for (int c2 = 0; c2 < transition.conditions.Length; c2++)
                        {
                            newTransition.AddCondition(transition.conditions[c2].mode, transition.conditions[c2].threshold, transition.conditions[c2].parameter);
                        }

                        List<AnimatorTranstitionType> transitionsToAdd2 = new List<AnimatorTranstitionType>();
                        List<AnimatorCondition> conditionsToAdd2 = new List<AnimatorCondition>();

                        ProcessTransition(newTransition, transitionsToAdd2, conditionsToAdd2, layerName, context, parameterTypes, true);
                        newTransition.conditions = conditionsToAdd2.ToArray();

                        transitionsToAdd.Add(newTransition);
                        // The duplicate may itself have been expanded into further transitions
                        transitionsToAdd.AddRange(transitionsToAdd2);
                    }
                }
                else
                {
                    // If/IfNot cannot appear on an int parameter but keep the condition just in case
                    conditionsToAdd.Add(condition);
                }
            }
            else if (condition.parameter == "GestureLeftWeight" || condition.parameter == "GestureRightWeight")
            {
                if (gestureWeightConversionMode == GestureWeightConversionMode.DerivedParameter)
                {
                    // The weight parameter survives and is fed from GestureLeft (see MakeGestureWeightFeedLayers)
                    conditionsToAdd.Add(condition);
                    continue;
                }

                // Look for fist gesture and create condition if needed
                bool gestureFound = false;

                for (int w = 0; w < transition.conditions.Length; w++)
                {
                    AnimatorCondition conditionW = transition.conditions[w];
                    if (
                        (condition.parameter == "GestureLeftWeight" && conditionW.parameter == "GestureLeft") ||
                        (condition.parameter == "GestureRightWeight" && conditionW.parameter == "GestureRight")
                    )
                    {
                        if (conditionW.threshold == 1f)
                        {
                            gestureFound = true;
                            break;
                        }
                    }
                }

                // Create condition if gesture weight is used by itself.
                // VRChat semantics: weight is 0 while Neutral, the analog squeeze while Fist,
                // and fixed 1 for every other gesture.
                if (!gestureFound)
                {
                    string parameterName = condition.parameter == "GestureLeftWeight" ? "GestureLeft" : "GestureRight";

                    // Float conditions only support Greater/Less
                    if (condition.mode == AnimatorConditionMode.Less)
                    {
                        if (condition.threshold > 1f)
                        {
                            // weight <= 1 always: the condition is always satisfied, drop it
                        }
                        else if (condition.threshold <= 0f)
                        {
                            // weight >= 0 always: keep the transition unreachable
                            AnimatorCondition impossibleCondition = new AnimatorCondition();
                            impossibleCondition.parameter = parameterName;
                            impossibleCondition.mode = AnimatorConditionMode.Greater;
                            impossibleCondition.threshold = 9999f;
                            conditionsToAdd.Add(impossibleCondition);
                        }
                        else
                        {
                            // Neutral (weight 0) or Fist squeezing below the threshold; the two bands
                            // are adjacent in ChilloutVR so one range covers both. Other gestures
                            // (fixed 1) never satisfy weight < threshold <= 1.
                            AnimatorCondition newConditionGreaterThan = new AnimatorCondition();
                            newConditionGreaterThan.parameter = parameterName;
                            newConditionGreaterThan.mode = AnimatorConditionMode.Greater;
                            newConditionGreaterThan.threshold = -0.1f;

                            conditionsToAdd.Add(newConditionGreaterThan);

                            AnimatorCondition newConditionLessThan = new AnimatorCondition();
                            newConditionLessThan.parameter = parameterName;
                            newConditionLessThan.mode = AnimatorConditionMode.Less;
                            newConditionLessThan.threshold = condition.threshold;

                            conditionsToAdd.Add(newConditionLessThan);
                        }
                    }
                    else
                    {
                        if (condition.threshold < 0f)
                        {
                            // weight >= 0 always: the condition is always satisfied, drop it
                        }
                        else if (condition.threshold >= 1f)
                        {
                            // weight <= 1 always: keep the transition unreachable
                            AnimatorCondition impossibleCondition = new AnimatorCondition();
                            impossibleCondition.parameter = parameterName;
                            impossibleCondition.mode = AnimatorConditionMode.Greater;
                            impossibleCondition.threshold = 9999f;
                            conditionsToAdd.Add(impossibleCondition);
                        }
                        else
                        {
                            // Fist squeezing above the threshold...
                            AnimatorCondition newConditionLessThan = new AnimatorCondition();
                            newConditionLessThan.parameter = parameterName;
                            newConditionLessThan.mode = AnimatorConditionMode.Less;
                            newConditionLessThan.threshold = 1.1f;

                            conditionsToAdd.Add(newConditionLessThan);

                            AnimatorCondition newConditionGreaterThan = new AnimatorCondition();
                            newConditionGreaterThan.parameter = parameterName;
                            newConditionGreaterThan.mode = AnimatorConditionMode.Greater;
                            newConditionGreaterThan.threshold = Mathf.Max(condition.threshold, 0.01f);

                            conditionsToAdd.Add(newConditionGreaterThan);

                            // ...or any non-Neutral non-Fist gesture, whose weight is fixed 1
                            AddGestureWeightRunDuplicates(transition, c, parameterName, transitionsToAdd, layerName, context, parameterTypes);
                        }
                    }
                }
            }
            else
            {
                conditionsToAdd.Add(condition);
            }
        }

        // Adapt every condition to the type its parameter actually has on chilloutAnimatorController
        // right before it is committed to the transition -- conditionsToAdd is mutated in place (not
        // just used to set transition.conditions here) because callers of the recursive
        // ProcessTransition calls above re-read the same list object afterwards.
        for (int i = conditionsToAdd.Count - 1; i >= 0; i--)
        {
            var conditionToAdd = conditionsToAdd[i];
            if (!parameterTypes.TryGetValue(conditionToAdd.parameter, out var parameterType))
            {
                // Not a parameter chilloutAnimatorController knows about yet -- somebody else's problem.
                continue;
            }

            if (VRC3CVRConditionTypes.TryAdapt(conditionToAdd, parameterType, out var adaptedCondition))
            {
                conditionsToAdd[i] = adaptedCondition;
            }
            else
            {
                Debug.LogWarning($"VRC3CVR: dropped a transition condition on layer '{layerName}', {context} -- parameter '{conditionToAdd.parameter}' is {parameterType} and has no equivalent for condition mode {conditionToAdd.mode}. The transition may now behave differently than on VRChat.");
                conditionsToAdd.RemoveAt(i);
            }
        }

        transition.conditions = conditionsToAdd.ToArray();
    }

    AnimatorTranstitionType DuplicateTransitionWithGestureEquals<AnimatorTranstitionType>(AnimatorTranstitionType transition, int conditionIndex, float gestureThreshold) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        AnimatorTranstitionType newTransition = new AnimatorTranstitionType();
        if (newTransition is AnimatorStateTransition)
        {
            AnimatorStateTransition newTransitionTyped = newTransition as AnimatorStateTransition;
            AnimatorStateTransition transitionTyped = transition as AnimatorStateTransition;
            newTransitionTyped.duration = transitionTyped.duration;
            newTransitionTyped.canTransitionToSelf = transitionTyped.canTransitionToSelf;
            newTransitionTyped.exitTime = transitionTyped.exitTime;
            newTransitionTyped.hasExitTime = transitionTyped.hasExitTime;
            newTransitionTyped.hasFixedDuration = transitionTyped.hasFixedDuration;
            newTransitionTyped.interruptionSource = transitionTyped.interruptionSource;
            newTransitionTyped.offset = transitionTyped.offset;
            newTransitionTyped.orderedInterruption = transitionTyped.orderedInterruption;
        }

        newTransition.name = transition.name;
        newTransition.destinationState = transition.destinationState;
        newTransition.destinationStateMachine = transition.destinationStateMachine;
        newTransition.hideFlags = transition.hideFlags;
        newTransition.isExit = transition.isExit;
        newTransition.solo = transition.solo;
        newTransition.mute = transition.mute;

        for (int c = 0; c < transition.conditions.Length; c++)
        {
            if (c == conditionIndex)
            {
                newTransition.AddCondition(AnimatorConditionMode.Equals, gestureThreshold, transition.conditions[c].parameter);
            }
            else
            {
                newTransition.AddCondition(transition.conditions[c].mode, transition.conditions[c].threshold, transition.conditions[c].parameter);
            }
        }

        return newTransition;
    }

    // Fold mode: redrive weight-driven blend trees with GestureLeft/GestureRight. During Fist the
    // gesture value is the squeeze amount itself. Other gestures read fixed 1 in VRChat, so widen
    // Simple1D trees with boundary children at -1 (open hand) and 2 (gestures 2..6) holding the
    // weight==1 motion; the tree clamps to them outside the original 0..1 range.
    void FoldGestureWeightOnBlendTree(BlendTree blendTree)
    {
        var isWeightX = blendTree.blendParameter == "GestureLeftWeight" || blendTree.blendParameter == "GestureRightWeight";
        if (isWeightX)
        {
            blendTree.blendParameter = blendTree.blendParameter == "GestureLeftWeight" ? "GestureLeft" : "GestureRight";
        }
        if (blendTree.blendParameterY == "GestureLeftWeight" || blendTree.blendParameterY == "GestureRightWeight")
        {
            blendTree.blendParameterY = blendTree.blendParameterY == "GestureLeftWeight" ? "GestureLeft" : "GestureRight";
            Debug.LogWarning("2D blend tree \"" + blendTree.name + "\" is driven by a gesture weight on its Y axis; the fixed weight 1 of non-Fist gestures cannot be reproduced");
        }

        if (isWeightX)
        {
            var children = blendTree.children;
            if (blendTree.blendType == BlendTreeType.Simple1D && children.Length > 0)
            {
                var topChild = children[0];
                foreach (var child in children)
                {
                    if (child.threshold > topChild.threshold)
                    {
                        topChild = child;
                    }
                }
                var newChildren = new ChildMotion[children.Length + 2];
                newChildren[0] = new ChildMotion { motion = topChild.motion, threshold = -1f, timeScale = topChild.timeScale, cycleOffset = topChild.cycleOffset, mirror = topChild.mirror };
                System.Array.Copy(children, 0, newChildren, 1, children.Length);
                newChildren[newChildren.Length - 1] = new ChildMotion { motion = topChild.motion, threshold = 2f, timeScale = topChild.timeScale, cycleOffset = topChild.cycleOffset, mirror = topChild.mirror };

                // Assign thresholds/range in this exact order: setting min/max clamps existing
                // children into the range, and automatic thresholds redistribute on assignment
                blendTree.useAutomaticThresholds = false;
                blendTree.minThreshold = -1f;
                blendTree.maxThreshold = 2f;
                blendTree.children = newChildren;
            }
            else if (blendTree.blendType != BlendTreeType.Simple1D)
            {
                Debug.LogWarning("Blend tree \"" + blendTree.name + "\" of type " + blendTree.blendType + " is driven by a gesture weight; the fixed weight 1 of non-Fist gestures cannot be reproduced");
            }
        }

        foreach (var child in blendTree.children)
        {
            if (child.motion is BlendTree childBlendTree)
            {
                FoldGestureWeightOnBlendTree(childBlendTree);
            }
        }
    }

    // Fold mode: a standalone weight condition is also satisfied by every non-Neutral
    // non-Fist gesture (their weight is fixed 1 in VRChat). Those gestures sit at
    // -1 (open hand) and 2..6 in ChilloutVR, so add one OR transition per range.
    void AddGestureWeightRunDuplicates<AnimatorTranstitionType>(AnimatorTranstitionType transition, int conditionIndex, string gestureParameterName, List<AnimatorTranstitionType> transitionsToAdd, string layerName, string context, Dictionary<string, AnimatorControllerParameterType> parameterTypes) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        // GestureLeft/GestureRight are always Float in ChilloutVR (declared as such on the template
        // AvatarAnimator.controller before any VRC layer is merged in), so these Greater/Less
        // conditions never need TryAdapt -- they are added straight onto the already-adapted list below.
        var runConditions = new AnimatorCondition[]
        {
            new AnimatorCondition { parameter = gestureParameterName, mode = AnimatorConditionMode.Less, threshold = -0.9f },
            new AnimatorCondition { parameter = gestureParameterName, mode = AnimatorConditionMode.Greater, threshold = 1.9f },
        };
        foreach (var runCondition in runConditions)
        {
            AnimatorTranstitionType newTransition = DuplicateTransitionWithoutCondition(transition, conditionIndex);

            List<AnimatorTranstitionType> transitionsToAdd2 = new List<AnimatorTranstitionType>();
            List<AnimatorCondition> conditionsToAdd2 = new List<AnimatorCondition>();

            ProcessTransition(newTransition, transitionsToAdd2, conditionsToAdd2, layerName, context, parameterTypes);
            conditionsToAdd2.Add(runCondition);
            newTransition.conditions = conditionsToAdd2.ToArray();

            transitionsToAdd.Add(newTransition);
            // Nested duplicates are OR branches of the remaining conditions and must carry the range condition too
            foreach (var nestedTransition in transitionsToAdd2)
            {
                var nestedConditions = nestedTransition.conditions;
                ArrayUtility.Add(ref nestedConditions, runCondition);
                nestedTransition.conditions = nestedConditions;
            }
            transitionsToAdd.AddRange(transitionsToAdd2);
        }
    }

    AnimatorTranstitionType DuplicateTransitionWithoutCondition<AnimatorTranstitionType>(AnimatorTranstitionType transition, int conditionIndex) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        AnimatorTranstitionType newTransition = new AnimatorTranstitionType();
        if (newTransition is AnimatorStateTransition)
        {
            AnimatorStateTransition newTransitionTyped = newTransition as AnimatorStateTransition;
            AnimatorStateTransition transitionTyped = transition as AnimatorStateTransition;
            newTransitionTyped.duration = transitionTyped.duration;
            newTransitionTyped.canTransitionToSelf = transitionTyped.canTransitionToSelf;
            newTransitionTyped.exitTime = transitionTyped.exitTime;
            newTransitionTyped.hasExitTime = transitionTyped.hasExitTime;
            newTransitionTyped.hasFixedDuration = transitionTyped.hasFixedDuration;
            newTransitionTyped.interruptionSource = transitionTyped.interruptionSource;
            newTransitionTyped.offset = transitionTyped.offset;
            newTransitionTyped.orderedInterruption = transitionTyped.orderedInterruption;
        }

        newTransition.name = transition.name;
        newTransition.destinationState = transition.destinationState;
        newTransition.destinationStateMachine = transition.destinationStateMachine;
        newTransition.hideFlags = transition.hideFlags;
        newTransition.isExit = transition.isExit;
        newTransition.solo = transition.solo;
        newTransition.mute = transition.mute;

        for (int c = 0; c < transition.conditions.Length; c++)
        {
            if (c == conditionIndex)
            {
                continue;
            }
            newTransition.AddCondition(transition.conditions[c].mode, transition.conditions[c].threshold, transition.conditions[c].parameter);
        }

        return newTransition;
    }

    static readonly string LocomotionAnimationPath = "Assets/CVR.CCK/Assets/Avatar/Animations/Locomotion";

    Dictionary<string, Func<AnimationClip>> BuildProxyHandClipMap() => new Dictionary<string, Func<AnimationClip>>
    {
        { "proxy_hands_fist", () => handCombinedFistAnimationClip },
        { "proxy_hands_gun", () => handCombinedGunAnimationClip },
        { "proxy_hands_idle", () => handCombinedRelaxedAnimationClip },
        { "proxy_hands_idle2", () => handCombinedRelaxedAnimationClip },
        { "proxy_hands_open", () => handCombinedOpenAnimationClip },
        { "proxy_hands_peace", () => handCombinedPeaceAnimationClip },
        { "proxy_hands_point", () => handCombinedPointAnimationClip },
        { "proxy_hands_rock", () => handCombinedRockNRollAnimationClip },
        { "proxy_hands_thumbs_up", () => handCombinedThumbsUpAnimationClip },
    };

    static readonly Dictionary<string, string> proxyLocomotionClipMap = new Dictionary<string, string>
    {
        { "proxy_stand_still", "LocIdle.anim" },
        { "proxy_idle", "LocIdle.anim" },
        { "proxy_idle2", "LocIdle.anim" },
        { "proxy_idle3", "LocIdle.anim" },
        { "proxy_run_forward", "LocRunningForward.anim" },
        { "proxy_run_backward", "LocRunningBackward.anim" },
    };

    // Copies before replacing, for the reason given on SubstitutedMotion.
    BlendTree ProxySubstitutedBlendTree(BlendTree tree, bool poseOnly)
    {
        var children = tree.children;
        var changed = false;
        for (var i = 0; i < children.Length; i++)
        {
            if (!(children[i].motion is AnimationClip))
            {
                continue;
            }
            var replacement = ReplaceProxyAnimationClip(children[i].motion, poseOnly);
            if (replacement != children[i].motion)
            {
                children[i].motion = replacement;
                changed = true;
            }
        }
        if (!changed)
        {
            return tree;
        }
        var owned = CopyAnimatorController.CopyBlendTree(null, tree, false);
        owned.children = children;
        return owned;
    }

    Motion ReplaceProxyAnimationClip(Motion clip, bool poseOnly)
    {
        if (!clip) return clip;

        var handClipMap = BuildProxyHandClipMap();
        if (handClipMap.TryGetValue(clip.name, out var getClip))
        {
            var replacement = getClip();
            return replacement ? (poseOnly ? PoseClipOf(replacement) : replacement) : clip;
        }

        if (proxyLocomotionClipMap.TryGetValue(clip.name, out var locomotionFile))
        {
            var locomotion = (AnimationClip)AssetDatabase.LoadAssetAtPath($"{LocomotionAnimationPath}/{locomotionFile}", typeof(AnimationClip));
            return poseOnly && locomotion ? PoseClipOf(locomotion) : locomotion;
        }

        return clip;
    }

    void ProcessStateMachine(AnimatorStateMachine stateMachine, string layerName, ref AnimatorControllerParameter[] parameters)
    {
        for (int s = 0; s < stateMachine.states.Length; s++)
        {
            // Debug.Log(stateMachine.states[s].state.transitions.Length + " transitions");

            AnimatorState state = stateMachine.states[s].state;

            if (gestureWeightConversionMode == GestureWeightConversionMode.FoldToGestureLeft)
            {
                // GestureLeft is only the weight while Fist; outside Fist a non-looping clip clamps
                // to its end (= weight 1, accidentally correct for gestures 2..6) but open hand (-1)
                // clamps to 0 and looping clips wrap, so motion time stays partially accurate.
                if (state.timeParameter == "GestureLeftWeight")
                {
                    state.timeParameter = "GestureLeft";
                }
                else if (state.timeParameter == "GestureRightWeight")
                {
                    state.timeParameter = "GestureRight";
                }
            }
            // In DerivedParameter mode weight references are kept; the parameter is fed from
            // GestureLeft (MakeGestureWeightFeedLayers) and renamed later by AdjustParameterNames

            var passThrough = IsTimedPassThrough(state);
            if (state.motion is BlendTree)
            {
                BlendTree blendTree = (BlendTree)state.motion;

                if (gestureWeightConversionMode == GestureWeightConversionMode.FoldToGestureLeft)
                {
                    FoldGestureWeightOnBlendTree(blendTree);
                }

                // a tree is as long as its children, so a pass-through state's children have to be
                // reduced to poses too or the state gets the length back through them
                state.motion = ProxySubstitutedBlendTree(blendTree, passThrough);
            }
            else if (state.motion is AnimationClip)
            {
                state.motion = ReplaceProxyAnimationClip(state.motion, passThrough);
            }

            var parameters2 = parameters;
            AnimatorDriverTask.ParameterType TypeOf(string name) => AnimatorDriverParameterType(parameters2, name);

            foreach (var behaviour in state.behaviours)
            {
                if (behaviour is VRCAvatarParameterDriver)
                {
                    var vrcDriver = behaviour as VRCAvatarParameterDriver;
                    var cvrDriver = state.AddStateMachineBehaviour<AnimatorDriver>();
                    cvrDriver.localOnly = vrcDriver.localOnly;
                    for (int i = 0; i < vrcDriver.parameters.Count; i++)
                    {
                        var vrcParameter = vrcDriver.parameters[i];
                        if (vrcParameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set)
                        {
                            cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Set,
                                targetName = vrcParameter.name,
                                targetType = TypeOf(vrcParameter.name),
                                aType = AnimatorDriverTask.SourceType.Static,
                                aValue = vrcParameter.value,
                            });
                        }
                        else if (vrcParameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add)
                        {
                            cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Addition,
                                targetName = vrcParameter.name,
                                targetType = TypeOf(vrcParameter.name),
                                aType = AnimatorDriverTask.SourceType.Parameter,
                                aParamType = TypeOf(vrcParameter.name),
                                aName = vrcParameter.name,
                                bType = AnimatorDriverTask.SourceType.Static,
                                bValue = vrcParameter.value,
                            });
                        }
                        else if (vrcParameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random)
                        {
                            var type = TypeOf(vrcParameter.name);
                            if (type == AnimatorDriverTask.ParameterType.Int || type == AnimatorDriverTask.ParameterType.Float)
                            {
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Set,
                                    targetName = vrcParameter.name,
                                    targetType = type,
                                    aType = AnimatorDriverTask.SourceType.Random,
                                    aValue = vrcParameter.valueMin,
                                    aMax = vrcParameter.valueMax,
                                });
                            }
                            else
                            {
                                var newParameter = new AnimatorControllerParameter { type = AnimatorControllerParameterType.Float, name = vrcParameter.name + "_Random_" + GUID.Generate().ToString() };
                                ArrayUtility.Add(ref parameters, newParameter);
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Set,
                                    targetName = newParameter.name,
                                    targetType = AnimatorDriverTask.ParameterType.Float,
                                    aType = AnimatorDriverTask.SourceType.Random,
                                    aParamType = TypeOf(vrcParameter.name),
                                    aValue = 0f,
                                    aMax = 1f,
                                });
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.LessThan,
                                    targetName = vrcParameter.name,
                                    targetType = TypeOf(vrcParameter.name),
                                    aType = AnimatorDriverTask.SourceType.Parameter,
                                    aParamType = AnimatorDriverTask.ParameterType.Float,
                                    aName = newParameter.name,
                                    bType = AnimatorDriverTask.SourceType.Static,
                                    bValue = vrcParameter.chance,
                                });
                            }
                        }
                        else
                        {
                            if (vrcParameter.convertRange)
                            {
                                var sourceRange = vrcParameter.sourceMax - vrcParameter.sourceMin;
                                if (sourceRange == 0f)
                                {
                                    Debug.LogWarning($"Parameter \"{vrcParameter.name}\" has zero source range (sourceMin == sourceMax == {vrcParameter.sourceMin}), skipping convertRange");
                                }
                                else
                                {
                                // src (srcMin - srcMax) => dst (dstMin - dstMax)
                                // dst = (src - srcMin) * (dstMax - dstMin) / (srcMax - srcMin) + dstMin
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Subtraction,
                                    targetName = vrcParameter.name,
                                    targetType = TypeOf(vrcParameter.name),
                                    aType = AnimatorDriverTask.SourceType.Parameter,
                                    aParamType = TypeOf(vrcParameter.source),
                                    aName = vrcParameter.source,
                                    bType = AnimatorDriverTask.SourceType.Static,
                                    bValue = vrcParameter.sourceMin,
                                });
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Multiplication,
                                    targetName = vrcParameter.name,
                                    targetType = TypeOf(vrcParameter.name),
                                    aType = AnimatorDriverTask.SourceType.Parameter,
                                    aParamType = TypeOf(vrcParameter.name),
                                    aName = vrcParameter.name,
                                    bType = AnimatorDriverTask.SourceType.Static,
                                    bValue = (vrcParameter.destMax - vrcParameter.destMin) / sourceRange,
                                });
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Addition,
                                    targetName = vrcParameter.name,
                                    targetType = TypeOf(vrcParameter.name),
                                    aType = AnimatorDriverTask.SourceType.Parameter,
                                    aParamType = TypeOf(vrcParameter.name),
                                    aName = vrcParameter.name,
                                    bType = AnimatorDriverTask.SourceType.Static,
                                    bValue = vrcParameter.destMin,
                                });
                                }
                            }
                            else
                            {
                                cvrDriver.EnterTasks.Add(new AnimatorDriverTask
                                {
                                    op = AnimatorDriverTask.Operator.Set,
                                    targetName = vrcParameter.name,
                                    targetType = TypeOf(vrcParameter.name),
                                    aType = AnimatorDriverTask.SourceType.Parameter,
                                    aParamType = TypeOf(vrcParameter.source),
                                    aName = vrcParameter.source,
                                });
                            }
                        }
                    }
                }
                else if (behaviour is VRCAnimatorLocomotionControl && convertVRCAnimatorLocomotionControl)
                {
                    var bodyControl = state.behaviours.FirstOrDefault(b => b is BodyControl) as BodyControl;
                    if (bodyControl == null) bodyControl = state.AddStateMachineBehaviour<BodyControl>();
                    var vrcLocomotionControl = behaviour as VRCAnimatorLocomotionControl;
                    bodyControl.EnterTasks.Add(new BodyControlTask
                    {
                        target = BodyControlTask.BodyMask.Locomotion,
                        targetWeight = vrcLocomotionControl.disableLocomotion ? 0f : 1f,
                    });
                }
                // Everything that ends up in the layer standing in for CVR's locomotion is exempt:
                // the layer it replaces never touches the IK weights, so tracking controls carried
                // across would fire where the platform expects none -- VRChat's stock landing
                // states would seize the legs and hips from full-body trackers for an instant on
                // every landing, and a folded emote would hold the head and hands for its length.
                else if (behaviour is VRCAnimatorTrackingControl && convertVRCAnimatorTrackingControl
                    && (convertLocomotionTrackingControl || !processingIntegratedLocomotionLayer))
                {
                    var vrcTrackingControl = behaviour as VRCAnimatorTrackingControl;
                    if (vrcTrackingControl.trackingHead != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange ||
                        vrcTrackingControl.trackingLeftHand != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange ||
                        vrcTrackingControl.trackingRightHand != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange ||
                        vrcTrackingControl.trackingLeftFoot != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange ||
                        vrcTrackingControl.trackingRightFoot != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange ||
                        vrcTrackingControl.trackingHip != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange)
                    {
                        var bodyControl = state.behaviours.FirstOrDefault(b => b is BodyControl) as BodyControl;
                        if (bodyControl == null) bodyControl = state.AddStateMachineBehaviour<BodyControl>();
                        void Adjust(VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType vrcTrackingType, BodyControlTask.BodyMask cvrBodyMask)
                        {
                            if (vrcTrackingType != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange)
                            {
                                bodyControl.EnterTasks.Add(new BodyControlTask
                                {
                                    target = cvrBodyMask,
                                    targetWeight = vrcTrackingType == VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Tracking ? 1f : 0f,
                                });
                            }
                        }
                        Adjust(vrcTrackingControl.trackingHead, BodyControlTask.BodyMask.Head);
                        Adjust(vrcTrackingControl.trackingLeftHand, BodyControlTask.BodyMask.LeftArm);
                        Adjust(vrcTrackingControl.trackingRightHand, BodyControlTask.BodyMask.RightArm);
                        Adjust(vrcTrackingControl.trackingLeftFoot, BodyControlTask.BodyMask.LeftLeg);
                        Adjust(vrcTrackingControl.trackingRightFoot, BodyControlTask.BodyMask.RightLeg);
                        Adjust(vrcTrackingControl.trackingHip, BodyControlTask.BodyMask.Pelvis);
                    }
                }
            }

            // VRC behaviours have been converted to CVR equivalents (or have none);
            // remove them so they do not remain as dead components in the CVR controller
            var stateBehaviours = state.behaviours;
            var keptBehaviours = stateBehaviours.Where(b => !IsVrcStateMachineBehaviour(b)).ToArray();
            if (keptBehaviours.Length != stateBehaviours.Length)
            {
                state.behaviours = keptBehaviours;
                foreach (var behaviour in stateBehaviours.Where(IsVrcStateMachineBehaviour))
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }

            AnimatorStateTransition[] newTransitions = ProcessTransitions(state.transitions, layerName, $"state '{state.name}'");
            state.transitions = newTransitions;
        }

        // VRC behaviours attached to the state machine itself are never converted; remove them too
        var machineBehaviours = stateMachine.behaviours;
        var keptMachineBehaviours = machineBehaviours.Where(b => !IsVrcStateMachineBehaviour(b)).ToArray();
        if (keptMachineBehaviours.Length != machineBehaviours.Length)
        {
            stateMachine.behaviours = keptMachineBehaviours;
            foreach (var behaviour in machineBehaviours.Where(IsVrcStateMachineBehaviour))
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
        }

        stateMachine.anyStateTransitions = ProcessTransitions(stateMachine.anyStateTransitions, layerName, "AnyState");
        stateMachine.entryTransitions = ProcessTransitions(stateMachine.entryTransitions, layerName, "Entry");

        // A sub-state-machine's own outgoing transitions (to a sibling state, to another
        // sub-state-machine, or to Exit) are stored on its *parent*, not on the sub-state-machine
        // itself -- reachable only through GetStateMachineTransitions/SetStateMachineTransitions.
        // The recursion below descends into the child, which never sees them, so they have to be
        // picked up here.
        foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
        {
            var subMachine = childStateMachine.stateMachine;
            var subMachineTransitions = stateMachine.GetStateMachineTransitions(subMachine);
            if (subMachineTransitions.Length == 0)
            {
                continue;
            }
            stateMachine.SetStateMachineTransitions(
                subMachine,
                ProcessTransitions(subMachineTransitions, layerName, $"sub-state-machine '{subMachine.name}'"));
        }

        if (stateMachine.stateMachines.Length > 0)
        {
            // Debug.Log("Found " + stateMachine.stateMachines.Length + " child state machines");
        }

        foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
        {
            ProcessStateMachine(childStateMachine.stateMachine, layerName, ref parameters);
        }
    }

    static bool IsVrcStateMachineBehaviour(StateMachineBehaviour behaviour)
    {
        if (behaviour == null) return false;
        var ns = behaviour.GetType().Namespace;
        return ns != null && (ns == "VRC" || ns.StartsWith("VRC."));
    }

    // A clip VRChat ships rather than the avatar's author: the proxy_* placeholders the client
    // swaps for its own internal animations at runtime, and anything inside the VRChat packages.
    // The real walk lives in the client, so a converted avatar gains nothing by carrying these.
    static bool IsVrchatPlaceholderClip(AnimationClip clip)
    {
        if (clip == null)
        {
            return true;
        }
        if (clip.name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var path = AssetDatabase.GetAssetPath(clip) ?? "";
        return path.IndexOf("com.vrchat.", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool HasAuthoredMotion(AnimatorController controller)
    {
        if (controller == null)
        {
            return false;
        }
        return controller.layers.Any(layer => MachineHasAuthoredMotion(layer.stateMachine));
    }

    static bool MachineHasAuthoredMotion(AnimatorStateMachine machine) =>
        AllStatesOf(machine).Any(state => MotionHasAuthoredClip(state.motion));

    static bool MotionHasAuthoredClip(Motion motion)
    {
        if (motion is AnimationClip clip)
        {
            return !IsVrchatPlaceholderClip(clip);
        }
        if (motion is BlendTree tree)
        {
            foreach (var child in tree.children)
            {
                if (MotionHasAuthoredClip(child.motion))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // VRChat's placeholders and the ChilloutVR animations that stand in the same place. The client
    // swaps proxy_* for its own internal animations at runtime, so carrying them across would ship
    // VRChat's preview clips as the avatar's walk. The CCK's own set is this platform's equivalent.
    static readonly Dictionary<string, string> placeholderClipSubstitutions = new Dictionary<string, string>
    {
        { "proxy_stand_still", "LocIdle" },
        { "proxy_idle", "LocIdle" },
        { "proxy_idle2", "LocIdle" },
        { "proxy_idle3", "LocIdle" },
        { "proxy_stand_still2", "LocIdle" },
        { "proxy_stand_still3", "LocIdle" },
        { "proxy_walk_forward", "LocWalkingForward" },
        { "proxy_walk_backward", "LocWalkingBackwards" },
        { "proxy_strafe_right", "LocWalkingStrafeRight" },
        { "proxy_strafe_right_45", "LocWalkingStrafeRightForwards" },
        { "proxy_strafe_right_135", "LocWalkingStrafeRightBackwards" },
        { "proxy_run_forward", "LocRunningForward" },
        { "proxy_sprint_forward", "LocRunningForward" },
        { "proxy_run_backward", "LocRunningBackward" },
        { "proxy_run_strafe_right", "LocRunningStrafeRight" },
        { "proxy_run_strafe_right_45", "LocRunningStrafeRightForwards" },
        { "proxy_run_strafe_right_135", "LocRunningStrafeRightBackwards" },
        { "proxy_crouch_still", "LocCrouchIdle" },
        { "proxy_crouch_walk_forward", "LocCrouchForward" },
        { "proxy_crouch_walk_right", "LocCrouchRight" },
        { "proxy_crouch_walk_right_45", "LocCrouchForward" },
        { "proxy_crouch_walk_right_135", "LocCrouchBackward" },
        { "proxy_low_crawl_still", "LocProneIdle" },
        { "proxy_low_crawl_idle", "LocProneIdle" },
        { "proxy_low_crawl_forward", "LocProneForward" },
        { "proxy_low_crawl_right", "LocProneRight" },
        { "proxy_fall_short", "LocJumpAir" },
        { "proxy_fall_long", "LocJumpAir" },
        { "proxy_landing", "LocJumpLand" },
        { "proxy_land_quick", "LocJumpLand" },
        { "proxy_sit", "LocSitting" },
        { "proxy_sit2", "LocSitting" },
    };

    const string CckLocomotionClipPath = "Assets/CVR.CCK/Assets/Avatar/Animations/Locomotion/";

    void SubstitutePlaceholderClips(AnimatorController controller)
    {
        foreach (var layer in controller.layers)
        {
            foreach (var state in AllStatesOf(layer.stateMachine))
            {
                if (PlayLandingClip(state))
                {
                    continue;
                }
                state.motion = SubstitutedMotion(state.motion, IsTimedPassThrough(state));
            }
        }
    }

    const string CckJumpLandClipName = "LocJumpLand";
    const string CckJumpLandStateName = "JumpLand";

    bool cckJumpLandExitSearched;
    AnimatorStateTransition cckJumpLandExit;

    // The one pass-through the pose treatment makes worse: LocJumpLand's first frame is the deep
    // landing crouch, planted at a fixed root height unlike the feet-based standing states around
    // it, so every blend through the pose drops the body and hauls it back up. ChilloutVR's own
    // JumpLand instead plays the clip most of the way through and leaves on an exit time, which is
    // what absorbs the crouch -- so the landing state gets the whole clip on that same timing.
    bool PlayLandingClip(AnimatorState state)
    {
        if (!playLandingAnimation
            || !IsTimedPassThrough(state)
            || !(state.motion is AnimationClip clip)
            || !placeholderClipSubstitutions.TryGetValue(clip.name, out var replacement)
            || replacement != CckJumpLandClipName)
        {
            return false;
        }
        var cckExit = CckJumpLandExit();
        var landing = SubstitutedClip(clip);
        if (cckExit == null || landing == null)
        {
            return false;
        }
        state.motion = landing;
        foreach (var transition in state.transitions)
        {
            if (!transition.hasExitTime)
            {
                continue;
            }
            transition.exitTime = cckExit.exitTime;
            transition.duration = cckExit.duration;
            transition.hasFixedDuration = cckExit.hasFixedDuration;
        }
        return true;
    }

    // Read from the CCK's shipped controller rather than hardcoded, so the timing follows whatever
    // CCK version is installed.
    AnimatorStateTransition CckJumpLandExit()
    {
        if (cckJumpLandExitSearched)
        {
            return cckJumpLandExit;
        }
        cckJumpLandExitSearched = true;
        var cckController = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AnimatorPath}/AvatarAnimator.controller");
        var cckLocomotionLayer = cckController == null
            ? null
            : cckController.layers.FirstOrDefault(layer => layer.name == CckLocomotionLayerName);
        cckJumpLandExit = cckLocomotionLayer == null
            ? null
            : AllStatesOf(cckLocomotionLayer.stateMachine)
                .Where(state => state.name == CckJumpLandStateName)
                .SelectMany(state => state.transitions)
                .FirstOrDefault(transition => transition.hasExitTime);
        if (cckJumpLandExit == null)
        {
            Debug.LogWarning($"Could not read the {CckJumpLandStateName} exit timing from the CCK's AvatarAnimator.controller; landing pass-through states keep the pose treatment instead of playing {CckJumpLandClipName}");
        }
        return cckJumpLandExit;
    }

    // VRChat swaps each proxy_* for the real animation of the same name, and it is the real one's
    // length the layer was timed against. There is no standing animation, so the stand_still family
    // resolves to a static pose of next to no length -- which is what lets a state exist purely to be
    // passed through on an exit time: JumpAndFall's RestoreTracking, QuickLand, a custom decision
    // chain. proxy_landing is the one SDK proxy with real length (1.03s), precisely because
    // HardLand's exit time of 0.6 had to be authored against it. So the placeholder lengths mirror
    // the real ones, and a state whose placeholder has no length with an exit time riding on it wants
    // the ChilloutVR clip's pose rather than the clip.
    static bool IsTimedPassThrough(AnimatorState state) =>
        state.motion != null
        && state.motion.averageDuration == 0f
        && state.transitions.Any(transition => transition.hasExitTime);

    readonly Dictionary<AnimationClip, AnimationClip> poseClips = new Dictionary<AnimationClip, AnimationClip>();

    // The clip's first frame, held for one frame rather than none: a zero-length clip is run as a one
    // second loop, so its exit time only comes round once a second, where one frame comes round every
    // frame -- still in reach under an entry transition, which suppresses the check while it blends.
    AnimationClip PoseClipOf(AnimationClip source)
    {
        if (source == null)
        {
            return null;
        }
        if (poseClips.TryGetValue(source, out var cached))
        {
            return cached;
        }
        var bindings = AnimationUtility.GetCurveBindings(source);
        var held = new AnimationCurve[bindings.Length];
        for (var i = 0; i < bindings.Length; i++)
        {
            held[i] = AnimationCurve.Constant(0f, 1f / 60f,
                AnimationUtility.GetEditorCurve(source, bindings[i]).Evaluate(0f));
        }
        var pose = new AnimationClip { name = source.name + "_Pose", frameRate = source.frameRate };
        AnimationUtility.SetEditorCurves(pose, bindings, held);
        // A clip's settings decide how its root curves are applied, and the CCK sets them per clip:
        // LocJumpLand sinks the root on landing and leans on KeepOriginalPositionY to place the body
        // anyway. Held at defaults, the pose would take that sunken RootT.y as a real displacement
        // and bury the avatar. The timing is the only part of the source's settings that cannot come
        // across, since the pose is one frame rather than the whole clip. Written after the curves:
        // setting them first lets the curves overwrite stopTime.
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        settings.startTime = 0f;
        settings.stopTime = 1f / 60f;
        AnimationUtility.SetAnimationClipSettings(pose, settings);
        poseClips[source] = pose;
        return pose;
    }

    Motion SubstitutedMotion(Motion motion, bool poseOnly) => MapMotion(motion, clip =>
    {
        var substituted = SubstitutedClip(clip);
        return substituted == null ? clip : poseOnly ? PoseClipOf(substituted) : substituted;
    });

    // Whatever a state plays, one clip at a time. A tree that any of its clips changed under is
    // handed back as a copy rather than edited: CopyAnimatorController shares rather than copies a
    // blend tree that lives in an asset of its own, so the tree reached here can still be the
    // avatar's -- or another controller's.
    static Motion MapMotion(Motion motion, Func<AnimationClip, AnimationClip> mapClip)
    {
        if (motion is AnimationClip clip)
        {
            return mapClip(clip);
        }
        if (motion is BlendTree tree)
        {
            var children = tree.children;
            var changed = false;
            for (var i = 0; i < children.Length; i++)
            {
                var mapped = MapMotion(children[i].motion, mapClip);
                if (mapped != children[i].motion)
                {
                    children[i].motion = mapped;
                    changed = true;
                }
            }
            if (!changed)
            {
                return tree;
            }
            var owned = CopyAnimatorController.CopyBlendTree(null, tree, false);
            owned.children = children;
            return owned;
        }
        return motion;
    }

    AnimationClip SubstitutedClip(AnimationClip clip)
    {
        if (clip == null || !placeholderClipSubstitutions.TryGetValue(clip.name, out var replacement))
        {
            return null;
        }
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(CckLocomotionClipPath + replacement + ".anim");
    }

    const string CckEmoteClipPath = "Assets/CVR.CCK/Assets/Avatar/Animations/Emotes/";

    // A stock Action machine's emotes are proxy_* placeholders like its locomotion, and ChilloutVR
    // ships the eight its own wheel offers -- Emote1..8, one per slot. Filling a slot from that set
    // plays what the wheel promises, and settles two more things the client reads off the clip's
    // name: the quick menu labels a slot after the clip it finds named for it, and an avatar counts
    // as emoting -- which is what holds its gestures back -- while the clip it plays is one of these.
    // ChilloutVR's own Emote states are native one-shots -- entered, they run their clip once and
    // leave unconditionally on exit time -- and a state substituted to play one of those clips needs
    // that same ending, or nothing but a changed number ever carries it out again.
    static void SubstituteEmoteProxyClips(Dictionary<AnimatorState, int> emoteNumbers, string emoteParameterName)
    {
        var substituted = new List<AnimatorState>();
        foreach (var entry in emoteNumbers)
        {
            if (!(entry.Key.motion is AnimationClip clip) || !IsVrchatPlaceholderClip(clip))
            {
                continue;
            }
            var cckEmote = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                CckEmoteClipPath + "Emote" + entry.Value + ".anim");
            if (cckEmote != null)
            {
                entry.Key.motion = cckEmote;
                substituted.Add(entry.Key);
            }
        }
        AddOneShotEmoteEscapes(substituted, emoteParameterName);
    }

    static void AddOneShotEmoteEscapes(IEnumerable<AnimatorState> states, string emoteParameterName)
    {
        foreach (var state in states)
        {
            var transitions = state.transitions;
            var heldByEmote = transitions.FirstOrDefault(
                transition => transition.conditions.Any(condition =>
                    condition.parameter == emoteParameterName && condition.mode == AnimatorConditionMode.NotEqual));
            if (heldByEmote == null)
            {
                // Already a one-shot, but of a clip that is no longer there: VRChat cut these exit
                // times to its own animation, and the substitution would otherwise stop ChilloutVR's
                // partway through.
                foreach (var runsOut in transitions.Where(
                    transition => transition.hasExitTime && transition.conditions.Length == 0))
                {
                    runsOut.exitTime = 1f;
                }
                continue;
            }
            var oneShot = new AnimatorStateTransition
            {
                destinationState = heldByEmote.destinationState,
                destinationStateMachine = heldByEmote.destinationStateMachine,
                isExit = heldByEmote.isExit,
                duration = heldByEmote.duration,
                hasFixedDuration = true,
                hasExitTime = true,
                exitTime = 1f,
            };
            ArrayUtility.Add(ref transitions, oneShot);
            state.transitions = transitions;
        }
    }

    // True from wherever a VRCPlayableLayerControl on this machine raises the Action playable's
    // goal to 1, through to wherever one drops it back to 0; a state with no control of its own
    // carries whatever the states reaching it carried, and a state reached both raised and not
    // resolves raised, logged as a warning since it means this machine's own Prepare/BlendOut shape
    // does not hold clean.
    //
    // That inheritance is only worth reading where the machine draws the span clearly enough to
    // bound it, which takes two things, and null -- fall back to what the machine dispatches on --
    // where either is missing:
    //
    // - Both ends present. A machine that only ever raises the weight leaves the region with no
    //   lower boundary, and it runs on down the blend-out and into the idle, which would tell
    //   ChilloutVR the avatar is emoting while it stands still.
    // - Every inheriting state reached from one particular state. An edge out of AnyState or Entry
    //   says nothing about what its destination inherits -- AnyState reaches it from raised and
    //   unraised alike, Entry from outside the machine -- and a sub-machine's own Entry/Exit is the
    //   same edge one level down. A state carrying its own control is not affected: it answers for
    //   itself however it was reached.
    static IEnumerable<AnimatorState> ActionPlayableWeightRaisedStates(AnimatorStateMachine machine, string layerName)
    {
        var states = AllStatesOf(machine).ToList();
        var ownGoal = states.ToDictionary(state => state, ActionPlayableLayerControlGoal);
        if (!ownGoal.Values.Any(goal => goal == true) || !ownGoal.Values.Any(goal => goal == false))
        {
            return null;
        }
        if (machine.stateMachines.Length > 0 ||
            machine.anyStateTransitions.Cast<AnimatorTransitionBase>().Concat(machine.entryTransitions).Any(
                transition => transition.destinationState != null
                    && !ActionPlayableLayerControlGoal(transition.destinationState).HasValue))
        {
            return null;
        }

        var predecessorsOf = states.ToDictionary(state => state,
            state => states.Where(candidate => candidate.transitions.Any(t => t.destinationState == state)).ToList());
        var raised = states.ToDictionary(state => state, state => ownGoal[state] ?? false);
        bool changed;
        do
        {
            changed = false;
            foreach (var state in states)
            {
                if (ownGoal[state].HasValue || raised[state])
                {
                    continue;
                }
                if (predecessorsOf[state].Any(predecessor => raised[predecessor]))
                {
                    raised[state] = true;
                    changed = true;
                }
            }
        } while (changed);

        foreach (var state in states)
        {
            if (ownGoal[state].HasValue)
            {
                continue;
            }
            if (predecessorsOf[state].Any(predecessor => raised[predecessor]) &&
                predecessorsOf[state].Any(predecessor => !raised[predecessor]))
            {
                Debug.LogWarning($"\"{state.name}\" in the Action animator's \"{layerName}\" layer is reached both with and without the Action playable's weight raised; treating it as raised.");
            }
        }

        return states.Where(state => raised[state]);
    }

    // One control per playable: a state that drops FX or raises Gesture says nothing about the
    // playable whose weight stood in for the tracked pose, and reading it as if it did would put
    // the region's boundary wherever an FX-driving tool happened to leave one.
    static VRCPlayableLayerControl ActionPlayableLayerControlOf(AnimatorState state) =>
        state == null
            ? null
            : state.behaviours.OfType<VRCPlayableLayerControl>().FirstOrDefault(
                behaviour => behaviour.layer == VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action);

    static bool? ActionPlayableLayerControlGoal(AnimatorState state)
    {
        var control = ActionPlayableLayerControlOf(state);
        return control == null ? (bool?)null : control.goalWeight == 1f;
    }

    // Which emote each state plays, read off the number the machine enters it on, and read before
    // ProcessStateMachine adapts those conditions to ChilloutVR's parameter types. How a state
    // leaves does not name its emote: VRChat holds an emote that loops against its own number and
    // lets one that ends by itself run out on exit time alone, so only the way in covers both.
    // A state entered on two numbers keeps the first -- one state per emote is the shape every
    // machine met so far has.
    static Dictionary<AnimatorState, int> EmoteNumbersOf(AnimatorStateMachine machine, string emoteParameterName)
    {
        var numbers = new Dictionary<AnimatorState, int>();
        foreach (var transition in AllStatesOf(machine).SelectMany(state => state.transitions.Cast<AnimatorTransitionBase>())
            .Concat(AllMachinesOf(machine).SelectMany(
                child => child.anyStateTransitions.Cast<AnimatorTransitionBase>().Concat(child.entryTransitions))))
        {
            if (transition.destinationState == null || numbers.ContainsKey(transition.destinationState))
            {
                continue;
            }
            foreach (var condition in transition.conditions)
            {
                if (condition.parameter == emoteParameterName && condition.mode == AnimatorConditionMode.Equals)
                {
                    numbers[transition.destinationState] = (int)condition.threshold;
                    break;
                }
            }
        }
        return numbers;
    }

    // ChilloutVR reads the name of whichever clip is actually playing on Locomotion/Emotes to
    // decide an avatar is emoting, and that flag alone is what releases VRIK for the clip's own
    // duration. The span VRChat kept the Action playable's own weight raised for -- computed above
    // -- is exactly the span that decision should cover, since that clip is what stood in for the
    // tracked pose throughout it; a machine whose own shape does not bound that span falls back to
    // whatever it dispatches on directly. A clip already named for it -- ChilloutVR's
    // own substituted Emote{n}, or an author who happened to name theirs the same way -- is left
    // alone, and a VRChat placeholder is never renamed on its own account, substituted or not. The
    // rename goes into a copy of the clip: the one reached here can be shared -- with a state
    // outside the span, with another machine, with the avatar's own asset -- and renaming it where
    // it lies would tell the client every one of those is an emote too.
    static void RenameEmoteClips(
        AnimatorStateMachine machine, string layerName, IEnumerable<AnimatorState> equalsDispatchFallback)
    {
        // one copy per clip, not per state that plays it: the states of a machine share their clips
        // freely, and a copy each would be that many identical animations saved into the avatar
        var renamed = new Dictionary<AnimationClip, AnimationClip>();
        foreach (var state in (ActionPlayableWeightRaisedStates(machine, layerName) ?? equalsDispatchFallback)
            .Where(state => state != null))
        {
            state.motion = MapMotion(state.motion, clip => EmoteNamedClip(clip, renamed));
        }
    }

    static AnimationClip EmoteNamedClip(AnimationClip clip, Dictionary<AnimationClip, AnimationClip> renamed)
    {
        if (clip == null || clip.name.Contains("Emote") || IsVrchatPlaceholderClip(clip))
        {
            return clip;
        }
        if (renamed.TryGetValue(clip, out var cached))
        {
            return cached;
        }
        var owned = CopyAnimatorController.CopyAnimationClip(clip);
        owned.name = "Emote_" + owned.name;
        renamed[clip] = owned;
        return owned;
    }

    const string CckLocomotionLayerName = "Locomotion/Emotes";

    // Set while the CVR locomotion layer is dropped in favour of the avatar's own Base layer.
    bool vrcBaseReplacesCckLocomotion;

    // Set while the VRC Action playable is folded into the integrated locomotion layer instead of
    // being merged as layers of its own (FoldActionMachine).
    bool vrcActionFoldsIntoCckLocomotion;

    // Set while FoldActionMachine reads VRCEmote rather than Emote (MakeVrcEmoteCompatFeedLayer).
    bool vrcActionFoldReadsVrcEmote;

    // The machine FoldActionMachine moved in; MakeVrcEmoteCompatFeedLayer drops the VRCEmote latch
    // on its way out, once AdjustParameterNames has settled the name to drop.
    AnimatorStateMachine foldedActionMachine;

    // Which emote each of its states plays (EmoteNumbersOf), kept for MakeVrcEmoteCompatFeedLayer:
    // the number a state has to let go of is the one it was entered on.
    Dictionary<AnimatorState, int> foldedActionEmoteNumbers = new Dictionary<AnimatorState, int>();

    // Set while the VRC Sitting playable is folded into the integrated locomotion layer
    // (FoldSittingMachine).
    bool vrcSittingFoldsIntoCckLocomotion;

    // Set while CVR's own seated state is carried across with the movement modes (BaseAnswersSitting).
    bool salvagesCckSitting;

    // Set while ProcessStateMachine walks a machine that ends up in the layer owning ChilloutVR's
    // locomotion: the Base layer that takes it over, or a machine folded into it.
    bool processingIntegratedLocomotionLayer;

    // Every salvaged mode is wired to the layer's default state, so a first layer without one
    // cannot take the CVR layer's place. Decided before that layer is dropped, or the avatar would be left
    // with neither locomotion.
    static bool HasLocomotionHub(AnimatorController controller)
    {
        if (controller == null || controller.layers.Length == 0)
        {
            return false;
        }
        var machine = controller.layers[0].stateMachine;
        return machine != null && machine.defaultState != null;
    }

    const string CckFlyingStateName = "LocFlying";
    const string CckSwimmingStateName = "Swimming";
    const string CckSittingStateName = "Sitting";
    const string CckEmotesMachineName = "Emotes";

    Dictionary<string, AnimatorState> salvagedMovementModeStates = new Dictionary<string, AnimatorState>();
    AnimatorStateMachine salvagedEmotesMachine;

    // The parts of the CVR locomotion layer nothing in a converted Base layer can answer: the two
    // movement modes VRChat has no concept of, the seat when nothing else answers Sitting, and the
    // quick-menu emotes, whose Emote/CancelEmote parameters stay declared and would otherwise drive
    // nothing.
    void SalvageCckMovementModeStates(AnimatorControllerLayer[] cckLayers)
    {
        salvagedMovementModeStates = new Dictionary<string, AnimatorState>();
        salvagedEmotesMachine = null;
        foreach (var layer in cckLayers)
        {
            if (layer.name != CckLocomotionLayerName)
            {
                continue;
            }
            foreach (var state in AllStatesOf(layer.stateMachine))
            {
                if (state.name == CckFlyingStateName || state.name == CckSwimmingStateName
                    || (state.name == CckSittingStateName && salvagesCckSitting))
                {
                    salvagedMovementModeStates[state.name] = state;
                }
            }
            if (vrcActionFoldsIntoCckLocomotion)
            {
                continue;
            }
            foreach (var childMachine in layer.stateMachine.stateMachines)
            {
                if (childMachine.stateMachine != null && childMachine.stateMachine.name == CckEmotesMachineName)
                {
                    salvagedEmotesMachine = childMachine.stateMachine;
                }
            }
        }
    }

    // The CVR locomotion layer is a hub-and-spoke: every mode leads back to the state the layer
    // starts in. What leads into them varies -- swimming and the seat from that same state, the
    // emotes from each of the three stances, and flight from AnyState, because it has to interrupt
    // whatever stance is running. Reproduced here with the avatar's own default state as the hub
    // they all return to, and every stance leading into them (StancesOf), flight excepted. Emotes
    // keep their nested machine, whose states leave through its Exit node -- which only goes
    // anywhere because the hub-bound transition below is registered for the machine on its parent.
    void RewireCckMovementModes(AnimatorControllerLayer locomotionLayer)
    {
        var machine = locomotionLayer.stateMachine;
        var hub = machine != null ? machine.defaultState : null;
        if (hub == null)
        {
            return;
        }

        var rewiredFromAnyState = new List<AnimatorStateTransition>();
        // read before the modes below join the layer's root, where they would count as stances
        var rewiredFromStances = StancesOf(machine)
            .ToDictionary(stance => stance, stance => new List<AnimatorStateTransition>());
        var ownAnyStateTransitionCount = machine.anyStateTransitions.Length;
        var states = machine.states;
        var y = 0f;

        AnimatorState Adopt(string stateName)
        {
            if (!salvagedMovementModeStates.TryGetValue(stateName, out var state) || state == null)
            {
                return null;
            }
            ArrayUtility.Add(ref states, new ChildAnimatorState { state = state, position = new Vector3(600f, y, 0f) });
            y += 100f;
            // whatever this used to lead to went out with the rest of the CVR locomotion layer
            state.transitions = new AnimatorStateTransition[0];
            return state;
        }

        var flying = Adopt(CckFlyingStateName);
        var swimming = Adopt(CckSwimmingStateName);
        var sitting = Adopt(CckSittingStateName);
        machine.states = states;

        if (flying != null)
        {
            var enter = Timed(machine.AddAnyStateTransition(flying), 0f);
            // without this a Flying that stays true restarts the state every frame
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Flying");
            rewiredFromAnyState.Add(enter);

            Timed(flying.AddTransition(hub), 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Flying");

            if (ownAnyStateTransitionCount > 0)
            {
                Debug.LogWarning($"The Base animator's first layer has {ownAnyStateTransitionCount} AnyState transition(s) of its own. ChilloutVR's flight state is entered from AnyState and does not suppress them, so while flying any of them whose conditions hold -- an airborne one especially -- fires out of flight, which is re-entered on the next frame. The avatar will flicker between flying and its own airborne animation.");
            }
        }

        if (swimming != null)
        {
            foreach (var stance in rewiredFromStances)
            {
                var enter = Timed(stance.Key.AddTransition(swimming), 0.25f);
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
                stance.Value.Add(enter);
            }

            Timed(swimming.AddTransition(hub), 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Swimming");
        }

        if (sitting != null)
        {
            // CVR sits down and stands up on the frame the client flips Sitting, with no blend
            foreach (var stance in rewiredFromStances)
            {
                var enter = Timed(stance.Key.AddTransition(sitting), 0f);
                enter.AddCondition(AnimatorConditionMode.If, 0f, SittingParameterName);
                stance.Value.Add(enter);
            }

            Timed(sitting.AddTransition(hub), 0f).AddCondition(AnimatorConditionMode.IfNot, 0f, SittingParameterName);
        }

        if (salvagedEmotesMachine != null)
        {
            var childMachines = machine.stateMachines;
            ArrayUtility.Add(ref childMachines, new ChildAnimatorStateMachine
            {
                stateMachine = salvagedEmotesMachine,
                position = new Vector3(600f, y, 0f),
            });
            machine.stateMachines = childMachines;

            foreach (var stance in rewiredFromStances)
            {
                var enter = Timed(stance.Key.AddTransition(salvagedEmotesMachine), 0f);
                enter.AddCondition(AnimatorConditionMode.Greater, 0f, EmoteParameterName);
                stance.Value.Add(enter);
            }

            // unconditional, as CVR has it: an emote that ends lands back on the hub and the hub
            // re-dispatches on the next frame, which is what lets the stance it started from resume
            machine.AddStateMachineTransition(salvagedEmotesMachine, hub);
        }

        machine.anyStateTransitions = PutFirst(machine.anyStateTransitions, rewiredFromAnyState);
        foreach (var stance in rewiredFromStances)
        {
            stance.Key.transitions = PutFirst(stance.Key.transitions, stance.Value);
        }
    }

    const string FoldedActionMachineName = "Action";
    const string FoldedActionMachineNamePrefix = "Action:";
    const string AfkParameterName = "AFK";
    const string CancelEmoteParameterName = "CancelEmote";
    const string VrcEmoteParameterName = "VRCEmote";
    const string EmoteParameterName = "Emote";

    // VRChat runs Action on a playable of its own and fades that playable's weight in and out around
    // it; ChilloutVR has a single Override series, so the weight has to become structure. The machine
    // moves in as a sub-state-machine of the layer that owns the body, the fade-in becomes a
    // conditional transition from that layer's hub, and the machine's own Exit node -- which meant
    // "restart" while it was a layer root and means "leave" as a child -- is caught by the parent's
    // unconditional transition back to the hub. The states that raised and dropped the playable
    // weight are left with nothing to say and go out with the rest of the VRC behaviours.
    // ChilloutVR's own Emotes machine goes too: it answers the same quick-menu Emote the folded
    // machine now also answers -- directly when the fold reads Emote itself, or through the
    // Emote-to-VRCEmote compat feed layer below when it reads VRCEmote instead -- and two machines
    // driving the body off the same value would fight over it. The folded machine takes over its
    // dispatch as well as its emotes, reached from every stance rather than from the hub alone
    // (StancesOf).
    // The AFK entry is only wired when the Action animator declares AFK, since stock answers it and
    // a machine that never mentions it has no AFK branch to reach. The emote number is read under
    // whichever name the Action controller itself declares -- VRCEmote when it does, Emote otherwise
    // -- rather than renamed to ChilloutVR's Emote, since the avatar's own custom expression menu
    // converts to an Advanced Avatar Settings entry that still drives VRCEmote by that name, and a
    // rename would leave that entry and ChilloutVR's own quick menu both driving the same value
    // unguarded. CancelEmote -- the quick menu's cancel -- is answered by duplicating each state's
    // own NotEqual exit against that same parameter (the exit an emote number changing away already
    // uses), so the cancel button reaches exactly where deselecting the emote would have; states that
    // only gate entry are left alone, since duplicating those would consume a cancel by advancing
    // into the machine instead of leaving it.
    // Every layer past the first folds the same way and for the same reason: the tools that add
    // emotes append a layer to the Action playable rather than replacing it, so a built avatar's
    // Action is stock underneath and the tool's own machine on top, and a machine folded in is
    // exclusive with the locomotion states by construction -- which is what the playable weight was
    // doing for all of them. Their entry conditions and their emote parameter are read off each
    // machine rather than assumed, since only the first layer answers VRChat's own VRCEmote. The
    // emotes and their Write Defaults are carried across untouched: one layer has room for a single
    // Write Defaults setting throughout, so there is no way to silence a machine's idle from inside
    // it. VRChat ran these layers at once with the upper one winning and the fold runs them one at a
    // time, which looks the same except where two were meant to overlap.
    void FoldActionMachine(AnimatorControllerLayer integratedLayer, AnimatorController actionController)
    {
        var machine = integratedLayer != null ? integratedLayer.stateMachine : null;
        var hub = machine != null ? machine.defaultState : null;
        if (hub == null)
        {
            Debug.LogWarning("Not converting the Action animator: the converted locomotion layer has no default state to dispatch emotes from.");
            return;
        }

        var emoteParameterName = actionController.parameters.Any(parameter => parameter.name == VrcEmoteParameterName)
            ? VrcEmoteParameterName
            : EmoteParameterName;
        vrcActionFoldReadsVrcEmote = emoteParameterName == VrcEmoteParameterName;

        RemoveCckEmotesMachine(machine);

        // read before the clone, whose VRC behaviours the processing below throws away
        var blendDurations = actionController.layers
            .Select(layer => ActionPrepareBlendDuration(layer.stateMachine)).ToArray();

        var clonedActionController = new CopyAnimatorController(actionController).CopyController();
        var clonedLayers = clonedActionController.layers;

        var actionMachine = clonedLayers[0].stateMachine;
        actionMachine.name = FoldedActionMachineName;
        foldedActionMachine = actionMachine;
        // All three read the machine as VRChat left it, so all three run before the processing
        // below: it adapts these conditions to ChilloutVR's own parameters -- on the Emote path the
        // NotEqual ones against a float, which drops them entirely -- and throws the VRC behaviours
        // away with them.
        foldedActionEmoteNumbers = EmoteNumbersOf(actionMachine, emoteParameterName);
        SubstituteEmoteProxyClips(foldedActionEmoteNumbers, emoteParameterName);
        RenameEmoteClips(actionMachine, clonedLayers[0].name, foldedActionEmoteNumbers.Keys);
        AddCancelEmoteEscapes(actionMachine, emoteParameterName, foldedActionEmoteNumbers);

        var addedMachines = new List<(AnimatorStateMachine machine, float blendDuration, string layerName)>();
        for (var i = 1; i < clonedLayers.Length; i++)
        {
            var refusal = AddedActionLayerRefusal(clonedLayers[i]);
            if (refusal != null)
            {
                Debug.LogWarning($"Not converting the Action animator's \"{clonedLayers[i].name}\" layer: {refusal}");
                continue;
            }
            var added = clonedLayers[i].stateMachine;
            // kept, since the machine is about to be renamed and the warnings below name the layer
            var layerName = clonedLayers[i].name;
            added.name = FoldedActionMachineNamePrefix + layerName;
            RenameEmoteClips(added, layerName, DispatchTransitionsOf(added).Select(transition => transition.destinationState));
            foreach (var parameter in DispatchParametersOf(added))
            {
                AddCancelEmoteEscapes(added, parameter, EmoteNumbersOf(added, parameter));
            }
            addedMachines.Add((added, blendDurations[i], layerName));
        }

        // registered before the processing below so this machine's conditions are adapted against
        // the types the merged controller already holds, and again after, since a converted Random
        // driver declares parameters of its own (see MergeVrcAnimatorIntoChilloutAnimator)
        new CopyAnimatorController(clonedActionController).CopyParametersTo(chilloutAnimatorController);
        var parameters = clonedActionController.parameters;
        processingIntegratedLocomotionLayer = true;
        ProcessStateMachine(actionMachine, integratedLayer.name, ref parameters);
        foreach (var added in addedMachines)
        {
            ProcessStateMachine(added.machine, integratedLayer.name, ref parameters);
        }
        processingIntegratedLocomotionLayer = false;
        clonedActionController.parameters = parameters;
        new CopyAnimatorController(clonedActionController).CopyParametersTo(chilloutAnimatorController);

        var childMachines = machine.stateMachines;
        ArrayUtility.Add(ref childMachines, new ChildAnimatorStateMachine
        {
            stateMachine = actionMachine,
            position = new Vector3(900f, 0f, 0f),
        });
        machine.stateMachines = childMachines;

        // after the processing above, as MergeVrcAnimatorIntoChilloutAnimator rewires and for the
        // reason stated there
        var answersAfk = chilloutAnimatorController.parameters.Any(parameter => parameter.name == AfkParameterName);
        foreach (var dispatch in StancesOf(machine).ToList())
        {
            var rewired = new List<AnimatorStateTransition>();
            var onEmote = Timed(dispatch.AddTransition(actionMachine), blendDurations[0]);
            onEmote.AddCondition(AnimatorConditionMode.Greater, 0f, emoteParameterName);
            rewired.Add(onEmote);

            if (answersAfk)
            {
                var onAfk = Timed(dispatch.AddTransition(actionMachine), blendDurations[0]);
                onAfk.AddCondition(AnimatorConditionMode.If, 0f, AfkParameterName);
                rewired.Add(onAfk);
            }

            dispatch.transitions = PutFirst(dispatch.transitions, rewired);
        }

        machine.AddStateMachineTransition(actionMachine, hub);

        var y = 0f;
        foreach (var added in addedMachines)
        {
            y += 200f;
            FoldAddedActionLayer(machine, hub, added.machine, added.blendDuration, added.layerName, new Vector3(1200f, y, 0f));
        }
    }

    // What the machine's idle used to leave on is what the stances now enter on. A transition out of
    // the idle with nothing to say for itself -- unconditional, or riding an exit time alone -- would
    // carry every stance into the machine as soon as the avatar stood still, so only the conditional
    // ones dispatch; the same read is what the refusal below tests a layer for, and what the cancel
    // escapes take their parameter from.
    static IEnumerable<AnimatorStateTransition> DispatchTransitionsOf(AnimatorStateMachine machine) =>
        machine != null && machine.defaultState != null
            ? machine.defaultState.transitions.Where(transition => !transition.isExit && transition.conditions.Length > 0)
            : Enumerable.Empty<AnimatorStateTransition>();

    static IEnumerable<string> DispatchParametersOf(AnimatorStateMachine machine) =>
        DispatchTransitionsOf(machine)
            .SelectMany(transition => transition.conditions)
            .Select(condition => condition.parameter)
            .Distinct();

    // What this refuses, with the reason, since the emotes in a refused layer simply go missing. A
    // fold is a layer running at full override weight whose states all leave through the machine's
    // own Exit, so anything the layer held back by other means, and any way out that Exit cannot be
    // made to stand for, is turned away rather than folded in wrong.
    static string AddedActionLayerRefusal(AnimatorControllerLayer layer)
    {
        if (layer.avatarMask != null)
        {
            return "it is masked to part of the avatar, and the layer it would fold into owns all of it.";
        }
        if (layer.defaultWeight == 0f)
        {
            return "it sits at zero weight until something raises it, and the layer it would fold into has no weight of its own to keep it silent with.";
        }
        if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
        {
            return "it is added on top of the pose underneath, and the layer it would fold into replaces that pose rather than adding to it.";
        }
        if (!DispatchTransitionsOf(layer.stateMachine).Any())
        {
            return "the state it starts in has no conditional transition out of it, so there is nothing for the locomotion stances to dispatch on.";
        }
        if (layer.stateMachine.anyStateTransitions.Any(transition => transition.destinationState == layer.stateMachine.defaultState))
        {
            return "it returns to the state it starts in from AnyState, and Unity has no AnyState transition to Exit to turn that into, so an emote would never hand the body back.";
        }
        if (layer.stateMachine.stateMachines.Length > 0)
        {
            return "it holds sub-state-machines of its own, whose Exit leads to their own parent rather than out of the fold, so an emote inside one would never hand the body back.";
        }
        if (!MachineHasAuthoredMotion(layer.stateMachine))
        {
            return "every clip in it is one of VRChat's proxy_* placeholders, which the client swaps for its own animations at runtime.";
        }
        return null;
    }

    void FoldAddedActionLayer(
        AnimatorStateMachine machine, AnimatorState hub, AnimatorStateMachine added, float blendDuration,
        string layerName, Vector3 position)
    {
        var dispatches = DispatchTransitionsOf(added).Select(transition => transition.conditions).ToList();
        if (dispatches.Count == 0)
        {
            // conditions can be adapted away against a parameter the merged controller holds as a
            // float, and an entry left without any would fire out of every stance on sight
            Debug.LogWarning($"Not converting the Action animator's \"{layerName}\" layer: none of its dispatch conditions survived the conversion to ChilloutVR's parameter types.");
            return;
        }

        var childMachines = machine.stateMachines;
        ArrayUtility.Add(ref childMachines, new ChildAnimatorStateMachine { stateMachine = added, position = position });
        machine.stateMachines = childMachines;

        var rewiredFromStances = StancesOf(machine).ToDictionary(stance => stance, stance => new List<AnimatorStateTransition>());
        foreach (var conditions in dispatches)
        {
            foreach (var stance in rewiredFromStances)
            {
                var enter = Timed(stance.Key.AddTransition(added), blendDuration);
                foreach (var condition in conditions)
                {
                    enter.AddCondition(condition.mode, condition.threshold, condition.parameter);
                }
                stance.Value.Add(enter);
            }
        }
        foreach (var stance in rewiredFromStances)
        {
            stance.Key.transitions = PutFirst(stance.Key.transitions, stance.Value);
        }

        // The idle is both the way in and the way out: entered, the machine lands there and
        // dispatches on the condition that just carried it in, and an emote that is done heads back
        // to it -- which as a child machine means heading out to the parent's hub instead, so the
        // stance the emote started from can be picked again. The idle itself stays as the landing
        // point, with the conditions and timing of the transitions to it carried onto the way out.
        foreach (var state in AllStatesOf(added))
        {
            if (state == added.defaultState)
            {
                continue;
            }
            foreach (var transition in state.transitions)
            {
                if (transition.destinationState != added.defaultState)
                {
                    continue;
                }
                transition.destinationState = null;
                transition.isExit = true;
            }
        }

        machine.AddStateMachineTransition(added, hub);
    }

    static void AddCancelEmoteEscapes(
        AnimatorStateMachine actionMachine, string emoteParameterName, Dictionary<AnimatorState, int> emoteNumbers)
    {
        foreach (var state in AllStatesOf(actionMachine))
        {
            // The state the machine starts in is the way in, not a way out, and a layer whose idle
            // dispatches on NotEqual would otherwise gain an escape that carries the cancel further
            // into the machine.
            if (state == actionMachine.defaultState)
            {
                continue;
            }
            var transitions = state.transitions;
            // A cancel leaves the way the state already leaves: by the exit it is held against the
            // emote number by, or -- for an emote that ends by itself -- by the one it runs out into.
            var wayOut = transitions.FirstOrDefault(
                transition => transition.conditions.Any(condition =>
                    condition.parameter == emoteParameterName && condition.mode == AnimatorConditionMode.NotEqual))
                ?? (emoteNumbers.ContainsKey(state)
                    ? transitions.FirstOrDefault(
                        transition => transition.hasExitTime && transition.conditions.Length == 0)
                    : null);
            if (wayOut == null)
            {
                continue;
            }
            var escape = Timed(new AnimatorStateTransition
            {
                destinationState = wayOut.destinationState,
                destinationStateMachine = wayOut.destinationStateMachine,
                isExit = wayOut.isExit,
            }, wayOut.duration);
            escape.AddCondition(AnimatorConditionMode.If, 0f, CancelEmoteParameterName);
            ArrayUtility.Add(ref transitions, escape);
            state.transitions = transitions;
        }
    }

    const string FoldedSittingMachineName = "Sitting";
    const string SittingParameterName = "Sitting";
    const float SittingBlendDuration = 0.25f;

    AnimatorController VrcSittingAnimatorController() =>
        (vrcAvatarDescriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
            .FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Sitting)
            .animatorController as AnimatorController;

    // The Sitting playable is folded the same way the Action one is, but VRChat holds its weight at
    // zero for as long as the player is standing, which leaves the machine with no exit structure at
    // all: nothing inside it says how to stand up, because the weight said it. Reducing that weight
    // to structure therefore means synthesising the way out -- every state gains a transition to the
    // machine's Exit node the moment Sitting drops -- on top of moving the machine in and wiring an
    // entry from each stance. ChilloutVR's own seated state is dropped along the way for the same
    // reason the Emotes machine is (FoldActionMachine): two states answering the same Sitting would
    // fight over the body.
    void FoldSittingMachine(AnimatorControllerLayer integratedLayer, AnimatorController sittingController)
    {
        var machine = integratedLayer != null ? integratedLayer.stateMachine : null;
        var hub = machine != null ? machine.defaultState : null;
        if (hub == null)
        {
            Debug.LogWarning("Not converting the Sitting animator: the converted locomotion layer has no default state to sit down from.");
            return;
        }

        // when the Base layer took the locomotion layer over, CVR's seat was never salvaged into it
        if (!vrcBaseReplacesCckLocomotion)
        {
            RemoveCckSittingState(machine);
        }

        var clonedSittingController = new CopyAnimatorController(sittingController).CopyController();
        var clonedLayers = clonedSittingController.layers;
        if (clonedLayers.Length > 1)
        {
            Debug.LogWarning($"Not converting {clonedLayers.Length - 1} layer(s) of the Sitting animator past the first: VRChat kept them off the avatar by holding the Sitting playable's weight at zero, and the folded machine has no weight of its own to hold them back with.");
        }

        var sittingMachine = clonedLayers[0].stateMachine;
        sittingMachine.name = FoldedSittingMachineName;

        // registered around the processing for the reason FoldActionMachine states
        new CopyAnimatorController(clonedSittingController).CopyParametersTo(chilloutAnimatorController);
        var parameters = clonedSittingController.parameters;
        processingIntegratedLocomotionLayer = true;
        ProcessStateMachine(sittingMachine, integratedLayer.name, ref parameters);
        processingIntegratedLocomotionLayer = false;
        clonedSittingController.parameters = parameters;
        new CopyAnimatorController(clonedSittingController).CopyParametersTo(chilloutAnimatorController);

        var childMachines = machine.stateMachines;
        ArrayUtility.Add(ref childMachines, new ChildAnimatorStateMachine
        {
            stateMachine = sittingMachine,
            position = new Vector3(900f, 200f, 0f),
        });
        machine.stateMachines = childMachines;

        // after the processing above, as the conditions below are already in CVR's own vocabulary
        foreach (var state in AllStatesOf(sittingMachine))
        {
            Timed(state.AddExitTransition(), SittingBlendDuration)
                .AddCondition(AnimatorConditionMode.IfNot, 0f, SittingParameterName);
        }

        foreach (var stance in StancesOf(machine).ToList())
        {
            var enter = Timed(stance.AddTransition(sittingMachine), SittingBlendDuration);
            enter.AddCondition(AnimatorConditionMode.If, 0f, SittingParameterName);
            stance.transitions = PutFirst(stance.transitions, new List<AnimatorStateTransition> { enter });
        }

        machine.AddStateMachineTransition(sittingMachine, hub);
    }

    static void RemoveCckSittingState(AnimatorStateMachine machine)
    {
        var sitting = machine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state != null && state.name == CckSittingStateName);
        if (sitting == null)
        {
            return;
        }
        foreach (var child in machine.states)
        {
            child.state.transitions = child.state.transitions
                .Where(transition => transition.destinationState != sitting)
                .ToArray();
        }
        machine.anyStateTransitions = machine.anyStateTransitions
            .Where(transition => transition.destinationState != sitting)
            .ToArray();
        machine.RemoveState(sitting);
    }

    // Sitting under every name a Base layer could be reading it by: the one below is read before the
    // conversion, so VRChat's own names for it are still in place.
    static readonly string[] SittingParameterNames = { SittingParameterName, "Seated", "InStation" };

    // A Base layer derived from VRChat's stock one carries its own seated branch. Handing it CVR's
    // seated state as well would leave both answering Sitting, so the salvage stands down.
    static bool BaseAnswersSitting(AnimatorController controller)
    {
        var machine = controller != null && controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
        if (machine == null)
        {
            return false;
        }
        return AllStatesOf(machine)
            .SelectMany(state => state.transitions.Cast<AnimatorTransitionBase>())
            .Concat(machine.anyStateTransitions)
            .Concat(machine.entryTransitions)
            .Any(transition => transition.conditions.Any(condition => SittingParameterNames.Contains(condition.parameter)));
    }

    static void RemoveCckEmotesMachine(AnimatorStateMachine machine)
    {
        var emotes = machine.stateMachines
            .Select(child => child.stateMachine)
            .FirstOrDefault(child => child != null && child.name == CckEmotesMachineName);
        if (emotes == null)
        {
            return;
        }
        foreach (var child in machine.states)
        {
            child.state.transitions = child.state.transitions
                .Where(transition => transition.destinationStateMachine != emotes)
                .ToArray();
        }
        machine.RemoveStateMachine(emotes);
    }

    const float DefaultActionBlendDuration = 0.25f;

    // The entry blend the Action playable's own fade-in was worth.
    static float ActionPrepareBlendDuration(AnimatorStateMachine machine)
    {
        var start = machine != null ? machine.defaultState : null;
        if (start == null)
        {
            return DefaultActionBlendDuration;
        }
        foreach (var transition in start.transitions)
        {
            var raise = ActionPlayableLayerControlOf(transition.destinationState);
            if (raise != null && raise.goalWeight == 1f)
            {
                return raise.blendDuration;
            }
        }
        return DefaultActionBlendDuration;
    }

    static AnimatorStateTransition Timed(AnimatorStateTransition transition, float duration)
    {
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        return transition;
    }

    // Ahead of whatever the avatar's own layer already had: these answer game states it was never
    // written for, and one of its own conditions holding would otherwise shadow them.
    static AnimatorStateTransition[] PutFirst(AnimatorStateTransition[] all, List<AnimatorStateTransition> rewired) =>
        rewired.Concat(all.Where(transition => !rewired.Contains(transition))).ToArray();

    // Everything the integrated locomotion layer answers a game state from. The layer's default
    // state is not enough on its own: an avatar whose first layer leaves it on a condition that
    // already holds -- a custom stance gated on a toggle that is off by default, say -- passes
    // through it in a single frame and lives somewhere else entirely, and every entry hung off the
    // hub alone would then be unreachable for the rest of the session. Taking the whole root
    // instead lands between the two clients: above ChilloutVR, which emotes out of its three
    // stances, and below VRChat, whose Action and Sitting playables faded in over any locomotion
    // state at all (an avatar's airborne states live in a sub-state-machine and are left out with
    // it, as they are in ChilloutVR's own dispatch). The movement modes are not stances and stay
    // out: flight is entered from AnyState and would pull itself straight back in after leaving.
    // They are recognised by name, which is what makes the layer ChilloutVR's own and the layer that
    // replaced it read alike -- so the state the layer starts in is held in whatever it is named,
    // since an avatar that happened to name it one of those would otherwise be left with no way in
    // at all.
    static IEnumerable<AnimatorState> StancesOf(AnimatorStateMachine machine) =>
        machine.states.Select(child => child.state).Where(state => state != null
            && (state == machine.defaultState
                || (state.name != CckFlyingStateName
                    && state.name != CckSwimmingStateName
                    && state.name != CckSittingStateName)));

    static IEnumerable<AnimatorStateMachine> AllMachinesOf(AnimatorStateMachine machine)
    {
        if (machine == null)
        {
            yield break;
        }
        var stack = new Stack<AnimatorStateMachine>();
        var seen = new HashSet<AnimatorStateMachine>();
        stack.Push(machine);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == null || !seen.Add(current))
            {
                continue;
            }
            yield return current;
            foreach (var sub in current.stateMachines)
            {
                stack.Push(sub.stateMachine);
            }
        }
    }

    static IEnumerable<AnimatorState> AllStatesOf(AnimatorStateMachine machine) =>
        AllMachinesOf(machine).SelectMany(current => current.states)
            .Where(child => child.state != null).Select(child => child.state);

    static AnimatorDriverTask.ParameterType AnimatorDriverParameterType(AnimatorControllerParameter[] parameters, string name)
    {
        var parameter = parameters.FirstOrDefault(p => p.name == name);
        if (parameter == null) return AnimatorDriverTask.ParameterType.Float;
        switch (parameter.type)
        {
            case AnimatorControllerParameterType.Bool: return AnimatorDriverTask.ParameterType.Bool;
            case AnimatorControllerParameterType.Int: return AnimatorDriverTask.ParameterType.Int;
            case AnimatorControllerParameterType.Float: return AnimatorDriverTask.ParameterType.Float;
            case AnimatorControllerParameterType.Trigger: return AnimatorDriverTask.ParameterType.Trigger;
        }
        return AnimatorDriverTask.ParameterType.None;
    }

    AvatarMask ReplaceVRCMask(AvatarMask mask)
    {
        if (mask)
        {
            switch (mask.name)
            {
                case "vrc_Hand Left":
                    return LoadMask("vrc3cvrHandLeft.mask");
                case "vrc_Hand Right":
                    return LoadMask("vrc3cvrHandRight.mask");
                case "vrc_HandsOnly":
                    return LoadMask("vrc3cvrHandsOnly.mask");
                case "vrc_MusclesOnly":
                    return LoadMask("vrc3cvrMusclesOnly.mask");
                default:
                    return mask;
            }
        }
        return mask;
    }

    AnimationClip CombineAnimationClips(AnimationClip animationClipA, AnimationClip animationClipB)
    {
        AnimationClip animationClipCombined = new AnimationClip();

        foreach (EditorCurveBinding i in AnimationUtility.GetCurveBindings(animationClipA))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClipA, i);
            animationClipCombined.SetCurve(i.path, i.type, i.propertyName, curve);
        }

        foreach (EditorCurveBinding i in AnimationUtility.GetCurveBindings(animationClipB))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClipB, i);
            animationClipCombined.SetCurve(i.path, i.type, i.propertyName, curve);
        }

        return animationClipCombined;
    }

    static readonly string HandAnimationPath = "Assets/CVR.CCK/Assets/Avatar/Animations/Hands";

    AnimationClip LoadCombinedHandAnimation(string gestureName)
    {
        var left = (AnimationClip)AssetDatabase.LoadAssetAtPath($"{HandAnimationPath}/HandLeft{gestureName}.anim", typeof(AnimationClip));
        var right = (AnimationClip)AssetDatabase.LoadAssetAtPath($"{HandAnimationPath}/HandRight{gestureName}.anim", typeof(AnimationClip));
        if (left && right)
        {
            var combined = CombineAnimationClips(left, right);
            combined.name = $"HandCombined{gestureName}";
            return combined;
        }
        return null;
    }

    void CreateCombinedHandAnimations()
    {
        handCombinedGunAnimationClip = LoadCombinedHandAnimation("Gun");
        handCombinedOpenAnimationClip = LoadCombinedHandAnimation("Open");
        handCombinedPeaceAnimationClip = LoadCombinedHandAnimation("Peace");
        handCombinedPointAnimationClip = LoadCombinedHandAnimation("Point");
        handCombinedRockNRollAnimationClip = LoadCombinedHandAnimation("RockNRoll");
        handCombinedThumbsUpAnimationClip = LoadCombinedHandAnimation("ThumbsUp");
        handCombinedRelaxedAnimationClip = LoadCombinedHandAnimation("Relaxed");
        handCombinedFistAnimationClip = LoadCombinedHandAnimation("Fist");

        if (handCombinedRelaxedAnimationClip && handCombinedFistAnimationClip)
        {
            List<EditorCurveBinding> editorCurveBindingsRelaxed = new List<EditorCurveBinding>();
            List<AnimationCurve> relaxedCurves = new List<AnimationCurve>();

            foreach (EditorCurveBinding i in AnimationUtility.GetCurveBindings(handCombinedRelaxedAnimationClip))
            {
                editorCurveBindingsRelaxed.Add(i);
                AnimationCurve curve = AnimationUtility.GetEditorCurve(handCombinedRelaxedAnimationClip, i);
                relaxedCurves.Add(curve);
            }

            List<EditorCurveBinding> editorCurveBindingsFist = new List<EditorCurveBinding>();
            List<AnimationCurve> fistCurves = new List<AnimationCurve>();

            foreach (EditorCurveBinding i in AnimationUtility.GetCurveBindings(handCombinedFistAnimationClip))
            {
                editorCurveBindingsFist.Add(i);
                AnimationCurve curve = AnimationUtility.GetEditorCurve(handCombinedFistAnimationClip, i);
                fistCurves.Add(curve);
            }

            handCombinedFistAnimationClip.ClearCurves();
            for (int i = 0; i < fistCurves.Count; i++)
            {
                AnimationCurve newCurve = new AnimationCurve();

                bool foundMatch = false;
                for (int j = 0; j < editorCurveBindingsRelaxed.Count; j++)
                {
                    if (editorCurveBindingsFist[i].propertyName == editorCurveBindingsRelaxed[j].propertyName)
                    {
                        newCurve.AddKey(relaxedCurves[j].keys[0]);
                        foundMatch = true;
                        continue;
                    }
                }

                if (!foundMatch)
                {
                    newCurve.AddKey(fistCurves[i].keys[0]);
                }

                newCurve.AddKey(fistCurves[i].keys[1]);

                handCombinedFistAnimationClip.SetCurve(editorCurveBindingsFist[i].path, editorCurveBindingsFist[i].type, editorCurveBindingsFist[i].propertyName, newCurve);
            }
        }
    }

    AvatarMask GetCombinedAvatarMask(AvatarMask baseMask, AvatarMask layerMask)
    {
        if (baseMask == null)
        {
            return layerMask;
        }

        if (layerMask == null)
        {
            return baseMask;
        }

        if (avatarMaskCombineCache.ContainsKey((baseMask, layerMask)))
        {
            return avatarMaskCombineCache[(baseMask, layerMask)];
        }
        else
        {
            AvatarMask combinedAvatarMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                combinedAvatarMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
                    layerMask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)i) & baseMask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)i));
            }

            // transformCount == 0 means "no transform-path restriction" (every transform is
            // implicitly active) -- our built-in masks (fullMask/musclesOnlyMask/etc.) never
            // restrict specific transforms, only humanoid body parts, so a path missing from one
            // side's list must default to active rather than being treated as excluded. Otherwise
            // combining with one of those built-ins would silently wipe out any transform-path
            // restriction the other (e.g. per-layer) mask defines. A path only ends up inactive in
            // the combined mask if either side explicitly marks it inactive.
            Dictionary<string, bool> GetTransformActiveByPath(AvatarMask mask)
            {
                var map = new Dictionary<string, bool>();
                for (int i = 0; i < mask.transformCount; i++)
                {
                    map[mask.GetTransformPath(i)] = mask.GetTransformActive(i);
                }
                return map;
            }

            var baseTransforms = GetTransformActiveByPath(baseMask);
            var layerTransforms = GetTransformActiveByPath(layerMask);
            var allTransformPaths = baseTransforms.Keys.Union(layerTransforms.Keys).ToList();
            combinedAvatarMask.transformCount = allTransformPaths.Count;
            for (int i = 0; i < allTransformPaths.Count; i++)
            {
                var path = allTransformPaths[i];
                var baseActive = !baseTransforms.TryGetValue(path, out var baseTransformActive) || baseTransformActive;
                var layerActive = !layerTransforms.TryGetValue(path, out var layerTransformActive) || layerTransformActive;
                combinedAvatarMask.SetTransformPath(i, path);
                combinedAvatarMask.SetTransformActive(i, baseActive && layerActive);
            }

            avatarMaskCombineCache[(baseMask, layerMask)] = combinedAvatarMask;
            if (baseMask.name != "" && layerMask.name != "")
            {
                combinedAvatarMask.name = baseMask.name + "_" + layerMask.name;
            }
            return combinedAvatarMask;
        }
    }

    AvatarMask GetAvatarMaskForLayerAndVRCAnimator(VRCBaseAnimatorID animatorID, int layerID, AvatarMask originalMask)
    {
        if (animatorID >= VRCBaseAnimatorID.MAX)
        {
            Debug.LogError("Invalid base animator id");
        }

        switch (animatorID)
        {
            case VRCBaseAnimatorID.BASE:
                return GetCombinedAvatarMask(ReplaceVRCMask(fullMask), ReplaceVRCMask(originalMask));
            case VRCBaseAnimatorID.ADDITIVE:
                // VRChat ignores the Additive playable's first layer mask, so the avatar was
                // authored with that mask having no effect.
                return layerID == 0
                    ? ReplaceVRCMask(fullMask)
                    : GetCombinedAvatarMask(ReplaceVRCMask(fullMask), ReplaceVRCMask(originalMask));
            case VRCBaseAnimatorID.GESTURE:
                if (layerID == 0)
                {
                    gestureMask = ReplaceVRCMask(originalMask);
                    return gestureMask;
                }
                else
                {
                    return GetCombinedAvatarMask(ReplaceVRCMask(gestureMask), ReplaceVRCMask(originalMask));
                }
            case VRCBaseAnimatorID.ACTION:
                return GetCombinedAvatarMask(ReplaceVRCMask(musclesOnlyMask), ReplaceVRCMask(originalMask));
            case VRCBaseAnimatorID.FX:
                return emptyMask;
            default:
                Debug.Log("Unknown VRC animator id");
                return null;
        }
    }

    void MergeVrcAnimatorIntoChilloutAnimator(AnimatorController originalAnimatorController, VRCBaseAnimatorID animatorID)
    {
        Debug.Log("Merging vrc animator \"" + originalAnimatorController.name + "\"...");

        var newAnimatorController = new CopyAnimatorController(originalAnimatorController).CopyController();

        var thisAnimatorReplacesCckLocomotion = animatorID == VRCBaseAnimatorID.BASE && vrcBaseReplacesCckLocomotion;
        if (thisAnimatorReplacesCckLocomotion)
        {
            // The deep clone above and never originalAnimatorController: this rewrites clip
            // references in place, and the avatar's own animator asset must not be touched.
            SubstitutePlaceholderClips(newAnimatorController);
        }

        // Register this animator's own parameters before processing its transitions below;
        // otherwise a parameter only this animator declares (e.g. IsLocal) is unknown until
        // CopyControllerTo merges it in later, and its conditions go out unadapted. Idempotent.
        new CopyAnimatorController(newAnimatorController).CopyParametersTo(chilloutAnimatorController);

        var controllerLayers = newAnimatorController.layers;
        var layersModified = false;
        // Unity forces the first layer's runtime weight to 1 regardless of its serialized value
        // (many controllers serialize it as 0). After merging this layer is no longer first,
        // so bake the forced weight in to keep it enabled.
        if (controllerLayers.Length > 0 && controllerLayers[0].defaultWeight != 1f)
        {
            controllerLayers[0].defaultWeight = 1f;
            layersModified = true;
        }
        for (int i = 0; i < controllerLayers.Length; i++)
        {
            AnimatorControllerLayer layer = controllerLayers[i];

            if (layer.stateMachine.states.Length > 0)
            { // Do not copy empty layers
                Debug.Log("Layer \"" + layer.name + "\" with " + layer.stateMachine.states.Length + " states");

                var parameters = newAnimatorController.parameters;
                // the replacement takes the animator's first layer, decided below after processing
                processingIntegratedLocomotionLayer = thisAnimatorReplacesCckLocomotion && i == 0;
                ProcessStateMachine(layer.stateMachine, layer.name, ref parameters);
                processingIntegratedLocomotionLayer = false;
                newAnimatorController.parameters = parameters;

                layer.avatarMask = GetAvatarMaskForLayerAndVRCAnimator(animatorID, i, layer.avatarMask);
                if (animatorID == VRCBaseAnimatorID.ADDITIVE)
                {
                    // The Additive playable is additive by platform rule, not by anything in the
                    // controller, so an author's layers are usually left on Override. Every layer
                    // becomes additive rather than only the first: one that stayed on Override
                    // would replace the whole merged pose, not just the additive contribution.
                    layer.blendingMode = AnimatorLayerBlendingMode.Additive;
                }
                controllerLayers[i] = layer;
                layersModified = true;
            }
        }
        // After the loop above, so the conditions this adds are already in CVR's own vocabulary
        // and must not go through ProcessStateMachine's VRChat-to-CVR adaptation.
        if (thisAnimatorReplacesCckLocomotion && controllerLayers.Length > 0)
        {
            controllerLayers[0].name = CckLocomotionLayerName;
            // the CVR layer this replaces ran the IK pass; VRChat's stock Base layer does not
            controllerLayers[0].iKPass = true;
            RewireCckMovementModes(controllerLayers[0]);
            layersModified = true;
        }
        if (layersModified)
        {
            newAnimatorController.layers = controllerLayers;
        }

        new CopyAnimatorController(newAnimatorController).CopyControllerTo(chilloutAnimatorController);

        Debug.Log("Merged");
    }

    void SetNonZeroDefaultValueParameters()
    {
        var parameters = chilloutAnimatorController.parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (nonZeroDefaultValueMap.TryGetValue(parameters[i].name, out var value))
            {
                parameters[i] = new AnimatorControllerParameter
                {
                    name = parameters[i].name,
                    type = parameters[i].type,
                    defaultFloat = value,
                    defaultInt = Mathf.RoundToInt(value),
                    defaultBool = value > 0f,
                };
            }
        }
        chilloutAnimatorController.parameters = parameters;
    }

    void SaveChilloutAnimator()
    {
        Directory.CreateDirectory(Application.dataPath + "/" + outputDirName);
        string pathInsideAssets = outputDirName + "/" + chilloutAnimatorController.name + ".controller";
        string pathToCreatedAnimator = "Assets/" + pathInsideAssets;
        // ReplaceFile() doesn't actually replace for some reason so make sure there is none already there
        FileUtil.DeleteFileOrDirectory(pathToCreatedAnimator);
        AssetDatabase.Refresh();

        new SaveAnimatorController(chilloutAnimatorController).Save(pathToCreatedAnimator);
    }

    static readonly string AnimatorPath = "Assets/CVR.CCK/Assets/Avatar/Animations";

    void CreateEmptyChilloutAnimator()
    {
        var sourceAnimator = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AnimatorPath}/AvatarAnimator.controller");

        if (sourceAnimator == null)
        {
            throw new Exception("Failed to load the created animator!");
        }

        chilloutAnimatorController = new CopyAnimatorController(sourceAnimator).CopyController();
        chilloutAnimatorController.name = cvrAvatar.gameObject.name + "_ChilloutVR_Gestures";

        Debug.Log("Loading animator...");

        var existingLayers = chilloutAnimatorController.layers;

        Debug.Log("Found number of layers: " + existingLayers.Length);

        if (existingLayers.Length != 4)
        {
            throw new Exception("Animator controller has unexpected number of layers: " + existingLayers.Length);
        }

        List<AnimatorControllerLayer> newLayers = new List<AnimatorControllerLayer>();

        List<string> allowedLayerNames = new List<string> { CckLocomotionLayerName };

        if (convertGestureLayer && vrcAvatarDescriptor.baseAnimationLayers[(int)VRCBaseAnimatorID.GESTURE].animatorController)
        {
            Debug.Log("Deleting CVR hand layers...");
        }
        else
        {
            Debug.Log("Not deleting CVR hand layers...");
            allowedLayerNames.Add("LeftHand");
            allowedLayerNames.Add("RightHand");
        }

        // ChilloutVR has no playable layers, so one Override layer series owns the body pose:
        // merged above this one a VRC Base layer could only replace it, never supplement it, and
        // CVR's movement sliders and stance buttons would then be answered nowhere. An avatar that
        // ships locomotion of its own therefore takes the layer over instead of stacking onto it.
        // Most Base layers are nothing but proxy_* references, and swapping CVR's locomotion for
        // those would be a downgrade, so HasAuthoredMotion is the gate.
        var baseAnimatorController = vrcAnimatorControllers.Length > (int)VRCBaseAnimatorID.BASE
            ? vrcAnimatorControllers[(int)VRCBaseAnimatorID.BASE]
            : null;
        vrcBaseReplacesCckLocomotion = convertLocomotionLayer
            && HasAuthoredMotion(baseAnimatorController)
            && HasLocomotionHub(baseAnimatorController);

        var actionAnimatorController = vrcAnimatorControllers.Length > (int)VRCBaseAnimatorID.ACTION
            ? vrcAnimatorControllers[(int)VRCBaseAnimatorID.ACTION]
            : null;
        vrcActionFoldsIntoCckLocomotion = convertActionLayer && HasAuthoredMotion(actionAnimatorController);

        // A stock Sitting layer only manages tracking and has no seated pose of its own, so it fails
        // HasAuthoredMotion and leaves the seat to ChilloutVR, which is what it was already doing.
        vrcSittingFoldsIntoCckLocomotion = convertSittingLayer && HasAuthoredMotion(VrcSittingAnimatorController());
        salvagesCckSitting = !vrcSittingFoldsIntoCckLocomotion && !BaseAnswersSitting(baseAnimatorController);

        if (vrcBaseReplacesCckLocomotion)
        {
            Debug.Log("The Base animator has locomotion of its own - replacing the CVR locomotion layer with it");
            SalvageCckMovementModeStates(existingLayers);
            allowedLayerNames.Remove(CckLocomotionLayerName);
        }
        else
        {
            Debug.Log("Keeping the CVR locomotion layer");
        }

        foreach (AnimatorControllerLayer layer in existingLayers)
        {
            if (allowedLayerNames.Contains(layer.name))
            {
                newLayers.Add(layer);
            }
        }

        chilloutAnimatorController.layers = newLayers.ToArray();

        Debug.Log("Setting animator...");

        cvrAvatar.avatarSettings.baseController = chilloutAnimatorController;

        Debug.Log("Chillout animator created");

        EditorUtility.SetDirty(cvrAvatar);
    }

    void GetValuesFromVrcAvatar()
    {
        Debug.Log("Getting values from VRC avatar component...");

        bodySkinnedMeshRenderer = vrcAvatarDescriptor.VisemeSkinnedMesh;

        if (bodySkinnedMeshRenderer == null)
        {
            Debug.LogWarning("Could not find viseme skinned mesh from VRC component");
        }
        else
        {
            Debug.Log("Body skinned mesh renderer: " + bodySkinnedMeshRenderer);
        }

        vrcViewPosition = vrcAvatarDescriptor.ViewPosition;

        Debug.Log("View position: " + vrcViewPosition);

        vrcVisemeBlendShapes = vrcAvatarDescriptor.VisemeBlendShapes;

        if (vrcVisemeBlendShapes == null)
        {
            Debug.LogWarning("Could not find viseme blend shapes from VRC component");
        }
        else
        {
            if (vrcVisemeBlendShapes.Length == 0)
            {
                Debug.LogWarning("Found 0 viseme blend shapes from VRC component");
            }
            else
            {
                Debug.Log("Visemes: " + string.Join(", ", vrcVisemeBlendShapes));
            }
        }

        int[] eyelidsBlendshapes = vrcAvatarDescriptor.customEyeLookSettings.eyelidsBlendshapes;

        if (eyelidsBlendshapes != null && eyelidsBlendshapes.Length >= 1 && eyelidsBlendshapes[0] != -1)
        {
            if (bodySkinnedMeshRenderer != null)
            {
                int blinkBlendshapeIdx = eyelidsBlendshapes[0];
                Mesh mesh = bodySkinnedMeshRenderer.sharedMesh;

                if (blinkBlendshapeIdx >= mesh.blendShapeCount)
                {
                    Debug.LogWarning("Could not use eyelid blendshape at index " + blinkBlendshapeIdx.ToString() + ": does not exist in mesh!");
                }
                else
                {
                    blinkBlendshapeName = mesh.GetBlendShapeName(blinkBlendshapeIdx);
                    Debug.Log("Blink blendshape: " + blinkBlendshapeName);
                }
            }
            else
            {
                Debug.LogWarning("Eyelid blendshapes are set but no skinned mesh renderer found");
            }
        }
        else
        {
            Debug.Log("No blink blendshape set");
        }

        VRCAvatarDescriptor.CustomAnimLayer[] vrcCustomAnimLayers = vrcAvatarDescriptor.baseAnimationLayers;
        vrcAnimatorControllers = new AnimatorController[vrcCustomAnimLayers.Length];

        for (int i = 0; i < vrcCustomAnimLayers.Length; i++)
        {
            // Ignore animators not checked for conversion
            if (i == (int)VRCBaseAnimatorID.BASE && !convertLocomotionLayer)
            {
                continue;
            }
            else if (i == (int)VRCBaseAnimatorID.ADDITIVE && !convertAdditiveLayer)
            {
                continue;
            }
            else if (i == (int)VRCBaseAnimatorID.GESTURE && !convertGestureLayer)
            {
                continue;
            }
            else if (i == (int)VRCBaseAnimatorID.ACTION && !convertActionLayer)
            {
                continue;
            }
            else if (i == (int)VRCBaseAnimatorID.FX && !convertFXLayer)
            {
                continue;
            }

            vrcAnimatorControllers[i] = vrcCustomAnimLayers[i].animatorController as AnimatorController;
        }

        Debug.Log("Found number of vrc base animation layers: " + vrcAvatarDescriptor.baseAnimationLayers.Length);
    }

    SkinnedMeshRenderer GetSkinnedMeshRendererInCVRAvatar()
    {
        string pathToSkinnedMeshRenderer = GetPathToGameObjectInsideAvatar(bodySkinnedMeshRenderer.gameObject);

        Debug.Log("Path to body skinned mesh renderer: " + pathToSkinnedMeshRenderer);

        var match = cvrAvatar.transform.Find(pathToSkinnedMeshRenderer.Remove(0, 1));

        if (match == null)
        {
            Debug.LogWarning("Could not find body inside the CVR avatar");
            return null;
        }

        SkinnedMeshRenderer skinnedMeshRenderer = match.GetComponent<SkinnedMeshRenderer>();

        if (skinnedMeshRenderer == null)
        {
            Debug.LogWarning("Could not find body skinned mesh renderer inside the CVR avatar");
            return null;
        }

        return skinnedMeshRenderer;
    }

    public static string GetPathToGameObjectInsideAvatar(GameObject obj)
    {
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;

            if (obj.transform.parent != null)
            {
                path = "/" + obj.name + path;
            }
        }
        return path;
    }

    void PopulateChilloutComponent()
    {
        Debug.Log("Populating chillout avatar component...");

        if (bodySkinnedMeshRenderer != null)
        {
            Debug.Log("Setting face mesh...");

            cvrAvatar.bodyMesh = GetSkinnedMeshRendererInCVRAvatar();
        }
        else
        {
            Debug.Log("No body skinned mesh renderer found so not setting CVR body mesh");
        }

        Debug.Log("Setting blinking...");

        if (string.IsNullOrEmpty(blinkBlendshapeName) == false)
        {
            cvrAvatar.useBlinkBlendshapes = true;
            cvrAvatar.blinkBlendshape[0] = blinkBlendshapeName;
        }
        else
        {
            Debug.LogWarning("Cannot set blink: no blendshapes found");
        }

        Debug.Log("Setting visemes...");

        if (vrcVisemeBlendShapes != null && vrcVisemeBlendShapes.Length > 0)
        {
            cvrAvatar.useVisemeLipsync = true;

            for (int i = 0; i < vrcVisemeBlendShapes.Length; i++)
            {
                cvrAvatar.visemeBlendshapes[i] = vrcVisemeBlendShapes[i];
            }
        }
        else
        {
            Debug.LogWarning("Cannot set visemes: no viseme blend shapes found");
        }

        Debug.Log("Setting view and voice position...");

        cvrAvatar.viewPosition = vrcViewPosition;
        cvrAvatar.voicePosition = vrcViewPosition;

        // Set the voice position to the root of the head bone by default since that will match VRC behaviour (I think)
        Transform headBoneTransform = GetHeadBoneTransform(cvrAvatar.GetComponent<Animator>());
        if (headBoneTransform)
        {
            cvrAvatar.voicePosition = cvrAvatar.transform.transform.InverseTransformPoint(headBoneTransform.transform.position);
            cvrAvatar.voicePosition.Scale(cvrAvatar.gameObject.transform.localScale);
        }

        Debug.Log("Enabling advanced avatar settings...");

        cvrAvatar.avatarUsesAdvancedSettings = true;

        // there is a slight delay before this happens which makes our script not work
        cvrAvatar.avatarSettings = new CVRAdvancedAvatarSettings();
        cvrAvatar.avatarSettings.settings = new List<CVRAdvancedSettingsEntry>();
        cvrAvatar.avatarSettings.initialized = true;

        EditorUtility.SetDirty(cvrAvatar);

        Debug.Log("Finished populating chillout component");
    }

    void CreateChilloutComponentIfNeeded()
    {
        cvrAvatar = chilloutAvatarGameObject.GetComponent<CVRAvatar>();

        if (cvrAvatar != null)
        {
            Debug.Log("Avatar has a CVRAvatar, skipping...");
            return;
        }

        Debug.Log("Avatar does not have a CVRAvatar, adding...");

        cvrAvatar = chilloutAvatarGameObject.AddComponent<CVRAvatar>() as CVRAvatar;

        Debug.Log("CVRAvatar component added");
    }

    void CreateVRCContactEquivalentPointers()
    {
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_head, HumanBodyBones.Head, "Head");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_torso, HumanBodyBones.Chest, "Torso");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_handL, HumanBodyBones.LeftHand, "Hand", "HandL");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_handR, HumanBodyBones.RightHand, "Hand", "HandR");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_footL, HumanBodyBones.LeftFoot, "Foot", "FootL");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_footR, HumanBodyBones.RightFoot, "Foot", "FootR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerIndexL, HumanBodyBones.LeftIndexDistal, "Finger", "FingerL", "FingerIndex", "FingerIndexL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerIndexR, HumanBodyBones.RightIndexDistal, "Finger", "FingerR", "FingerIndex", "FingerIndexR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerMiddleL, HumanBodyBones.LeftMiddleDistal, "Finger", "FingerL", "FingerMiddle", "FingerMiddleL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerMiddleR, HumanBodyBones.RightMiddleDistal, "Finger", "FingerR", "FingerMiddle", "FingerMiddleR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerRingL, HumanBodyBones.LeftRingDistal, "Finger", "FingerL", "FingerRing", "FingerRingL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerRingR, HumanBodyBones.RightRingDistal, "Finger", "FingerR", "FingerRing", "FingerRingR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerLittleL, HumanBodyBones.LeftLittleDistal, "Finger", "FingerL", "FingerLittle", "FingerLittleL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerLittleR, HumanBodyBones.RightLittleDistal, "Finger", "FingerR", "FingerLittle", "FingerLittleR");
    }

    void AddContactEquivalentPointer(bool forceSphere, VRCAvatarDescriptor.ColliderConfig config, HumanBodyBones bone, params string[] collisionTags)
    {
        if (config.state == VRCAvatarDescriptor.ColliderConfig.State.Disabled)
        {
            return;
        }
        var colliderParentTransform = config.transform;
        if (colliderParentTransform == null)
        {
            var animator = vrcAvatarDescriptor.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning($"Cannot add VRChat default contact equivalent: {bone}");
                return;
            }
            colliderParentTransform = animator.GetBoneTransform(bone);
            if (colliderParentTransform == null)
            {
                Debug.LogWarning($"Cannot add VRChat default contact equivalent: {bone}");
                return;
            }
        }
        var transform = cvrAvatar.transform.Find(RelativePath(vrcAvatarDescriptor.transform, colliderParentTransform));
        foreach (var collisionTag in collisionTags)
        {
            var name = GameObjectUtility.GetUniqueNameForSibling(transform, $"{transform.name}_{collisionTag}");
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(transform, false);
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;
            var contactGameObject = SuitableContactObjectWithCollider(
                gameObject,
                true,
                config.height == 0 || forceSphere ? VRC.Dynamics.ContactBase.ShapeType.Sphere : VRC.Dynamics.ContactBase.ShapeType.Capsule,
                config.radius,
                config.position,
                config.height,
                config.rotation
                );
            var cvrPointer = contactGameObject.AddComponent<CVRPointer>();
            cvrPointer.type = collisionTag;
        }
    }


    void ConvertContactsToCVRComponents()
    {
        var senders = chilloutAvatarGameObject.GetComponentsInChildren<VRCContactSender>(true);
        var receivers = chilloutAvatarGameObject.GetComponentsInChildren<VRCContactReceiver>(true);
        contactComponentPathRemap = new Dictionary<string, string[]>();
        constantContactProxiedParameters = new HashSet<string>();
        contactReceiverParameters = new HashSet<string>();
        localPointerPaths = new HashSet<string>();
        localTriggerPaths = new HashSet<string>();
        newContactRoots = new List<(Transform, Transform)>();
        foreach (var sender in senders)
        {
            if (sender.collisionTags.Count == 0)
            {
                continue;
            }
            var collisionTagToCVRType = MakeCollisionTagToCVRType(sender.gameObject);
            var originalPath = ChilloutAvatarRelativePath(sender);
            var remappedPaths = new List<string>();
            var collisionTags = sender.collisionTags.SelectMany(collisionTagToCVRType).Distinct().ToArray();
            if (collisionTags.Length == 1)
            {
                var contactGameObject = SuitableContactObjectWithCollider(sender.gameObject, sender);
                var cvrPointer = contactGameObject.AddComponent<CVRPointer>();
                cvrPointer.type = collisionTags.FirstOrDefault();
                remappedPaths.Add(ChilloutAvatarRelativePath(contactGameObject));
                newContactRoots.Add((sender.transform, contactGameObject.transform));
            }
            else
            {
                foreach (var collisionTag in collisionTags)
                {
                    var name = GameObjectUtility.GetUniqueNameForSibling(sender.transform, $"{sender.name}_{collisionTag}");
                    var gameObject = new GameObject(name);
                    gameObject.transform.SetParent(sender.transform, false);
                    gameObject.transform.localPosition = Vector3.zero;
                    gameObject.transform.localRotation = Quaternion.identity;
                    gameObject.transform.localScale = Vector3.one;
                    var contactGameObject = SuitableContactObjectWithCollider(gameObject, sender);
                    var cvrPointer = contactGameObject.AddComponent<CVRPointer>();
                    cvrPointer.type = collisionTag;
                    remappedPaths.Add(ChilloutAvatarRelativePath(contactGameObject));
                    newContactRoots.Add((sender.transform, gameObject.transform));
                }
            }
            if (sender.IsLocalOnly)
            {
                localPointerPaths.UnionWith(remappedPaths);
            }
            if (!(remappedPaths.Count == 1 && remappedPaths[0] == originalPath))
            {
                contactComponentPathRemap[originalPath] = remappedPaths.ToArray();
            }
            UnityEngine.Object.DestroyImmediate(sender);
        }
        foreach (var receiver in receivers)
        {
            if (receiver.collisionTags.Count == 0)
            {
                continue;
            }
            var collisionTagToCVRType = MakeCollisionTagToCVRType(receiver.gameObject);
            var contactGameObject = SuitableContactObjectWithCollider(receiver.gameObject, receiver);
            newContactRoots.Add((receiver.transform, contactGameObject.transform));
            var cvrTrigger = contactGameObject.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            cvrTrigger.useAdvancedTrigger = true;
            cvrTrigger.isLocalInteractable = receiver.allowSelf;
            cvrTrigger.isNetworkInteractable = receiver.allowOthers;
            cvrTrigger.allowedTypes = receiver.collisionTags.SelectMany(collisionTagToCVRType).Distinct().ToArray();
            if (receiver.receiverType == VRC.Dynamics.ContactReceiver.ReceiverType.Constant)
            {
                var proxyParameter = ConstantContactProxiedParameterName(receiver.parameter);
                constantContactProxiedParameters.Add(receiver.parameter);
                // Count the number of pointers that are inside, so that if one is inside, it will be true
                // see MakeProxyLayersOfConstantContactParameters
                cvrTrigger.enterTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Add,
                    settingName = proxyParameter,
                    settingValue = 1f,
                    delay = 0f,
                    holdTime = 0f,
                });
                cvrTrigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Subtract,
                    settingName = proxyParameter,
                    settingValue = 1f,
                    delay = 0f,
                    holdTime = 0f,
                });
            }
            else if (receiver.receiverType == VRC.Dynamics.ContactReceiver.ReceiverType.OnEnter)
            {
                cvrTrigger.enterTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                    settingName = receiver.parameter,
                    settingValue = 1f,
                    delay = 0f,
                    holdTime = 0f,
                });
                cvrTrigger.enterTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                    settingName = receiver.parameter,
                    settingValue = 0f,
                    delay = 1f / 60,
                    holdTime = 0f,
                });
            }
            else
            {
                cvrTrigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromDistance,
                    settingName = receiver.parameter,
                    // caution: inversed!
                    minValue = 1f,
                    maxValue = 0f,
                });
            }
            var originalPath = ChilloutAvatarRelativePath(receiver);
            var remappedPath = ChilloutAvatarRelativePath(contactGameObject);
            if (receiver.IsLocalOnly)
            {
                localTriggerPaths.Add(remappedPath);
            }
            else
            {
                if (!string.IsNullOrEmpty(receiver.parameter)) contactReceiverParameters.Add(receiver.parameter);
            }
            if (originalPath != remappedPath)
            {
                contactComponentPathRemap[originalPath] = new[] { remappedPath };
            }
            UnityEngine.Object.DestroyImmediate(receiver);
        }
    }

    void ExcludeContactsFromDynamicBones()
    {
        if (newContactRoots == null || newContactRoots.Count == 0) return;

        // DynamicBone型をリフレクションで検索
        var dynamicBoneType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == "DynamicBone");
        if (dynamicBoneType == null) return;

        var rootField = dynamicBoneType.GetField("m_Root");
        var dynamicBones = chilloutAvatarGameObject.GetComponentsInChildren(dynamicBoneType, true);
        var dynamicBoneRoots = new HashSet<Transform>();
        foreach (var db in dynamicBones)
        {
            var root = rootField?.GetValue(db) as Transform;
            if (root == null) root = (db as Component).transform;
            dynamicBoneRoots.Add(root);
        }
        if (dynamicBoneRoots.Count == 0) return;

        Transform FindDynamicBoneRoot(Transform t)
        {
            while (t != null && t != chilloutAvatarGameObject.transform)
            {
                if (dynamicBoneRoots.Contains(t)) return t;
                t = t.parent;
            }
            return null;
        }

        // DynamicBone root配下にあるcontactをparentBoneでグループ化
        var groups = newContactRoots
            .Select(x => (x.parentBone, x.createdRoot, dbRoot: FindDynamicBoneRoot(x.parentBone)))
            .Where(x => x.dbRoot != null)
            .GroupBy(x => x.parentBone);

        foreach (var group in groups)
        {
            var parentBone = group.Key;
            var dbRoot = group.First().dbRoot;
            var dbRootParent = dbRoot.parent != null ? dbRoot.parent : chilloutAvatarGameObject.transform;

            // exclude-child-bonesパターンのコンテナを検索または作成
            var container = FindExistingExcludeChildBonesContainer(dbRootParent, parentBone);
            if (container == null)
            {
                var containerName = GameObjectUtility.GetUniqueNameForSibling(
                    dbRootParent,
                    $"{parentBone.name}_ExcludeChildBones");
                container = new GameObject(containerName).transform;
                container.SetParent(parentBone, false);
                container.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                container.localScale = Vector3.one;
                container.SetParent(dbRootParent, true); // world座標維持

                // Constraint追加
                var parentConstraint = container.gameObject.AddComponent<ParentConstraint>();
                parentConstraint.AddSource(new ConstraintSource
                {
                    sourceTransform = parentBone,
                    weight = 1f
                });
                parentConstraint.constraintActive = true;

                var scaleConstraint = container.gameObject.AddComponent<ScaleConstraint>();
                scaleConstraint.AddSource(new ConstraintSource
                {
                    sourceTransform = parentBone,
                    weight = 1f
                });
                scaleConstraint.constraintActive = true;
            }

            // 各contactオブジェクトを移動し、パス更新
            foreach (var (_, createdRoot, _) in group)
            {
                var oldPath = ChilloutAvatarRelativePath(createdRoot);
                createdRoot.SetParent(container, true); // world座標維持
                var newPath = ChilloutAvatarRelativePath(createdRoot);

                UpdateContactPaths(oldPath, newPath);
            }
        }
    }

    static Transform FindExistingExcludeChildBonesContainer(Transform searchParent, Transform parentBone)
    {
        var expectedName = $"{parentBone.name}_ExcludeChildBones";
        for (int i = 0; i < searchParent.childCount; i++)
        {
            var child = searchParent.GetChild(i);
            if (child.name != expectedName) continue;

            var pc = child.GetComponent<ParentConstraint>();
            if (pc == null || pc.sourceCount == 0 || pc.GetSource(0).sourceTransform != parentBone) continue;

            var sc = child.GetComponent<ScaleConstraint>();
            if (sc == null || sc.sourceCount == 0 || sc.GetSource(0).sourceTransform != parentBone) continue;

            return child;
        }
        return null;
    }

    void UpdateContactPaths(string oldPath, string newPath)
    {
        // contactComponentPathRemap の値(remapped paths)を更新
        foreach (var key in contactComponentPathRemap.Keys.ToArray())
        {
            var paths = contactComponentPathRemap[key];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ReplacePath(paths[i], oldPath, newPath);
            }
        }

        // localPointerPaths 更新
        ReplacePathsInSet(localPointerPaths, oldPath, newPath);
        // localTriggerPaths 更新
        ReplacePathsInSet(localTriggerPaths, oldPath, newPath);
    }

    static string ReplacePath(string path, string oldPrefix, string newPrefix)
    {
        if (path == oldPrefix) return newPrefix;
        if (path.StartsWith(oldPrefix + "/"))
            return newPrefix + path.Substring(oldPrefix.Length);
        return path;
    }

    static void ReplacePathsInSet(HashSet<string> set, string oldPrefix, string newPrefix)
    {
        var toRemove = new List<string>();
        var toAdd = new List<string>();
        foreach (var path in set)
        {
            var replaced = ReplacePath(path, oldPrefix, newPrefix);
            if (replaced != path)
            {
                toRemove.Add(path);
                toAdd.Add(replaced);
            }
        }
        foreach (var p in toRemove) set.Remove(p);
        foreach (var p in toAdd) set.Add(p);
    }

    Func<string, string[]> MakeCollisionTagToCVRType(GameObject gameObject)
    {
        var configs = FindConfigsInParent(gameObject.transform);
        var config = VRC3CVRCollisionTagConvertionConfig.WithInherits(new VRC3CVRCollisionTagConvertionConfig[] { collisionTagConvertionConfig }.Concat(configs.Reverse()));
        return config.CollisionTagToCVRType;
    }

    IEnumerable<VRC3CVRCollisionTagConvertionConfig> FindConfigsInParent(Transform transform)
    {
        while (transform != null && transform != chilloutAvatarGameObject.transform)
        {
            var conversion = transform.GetComponent<VRC3CVRCollisionTagConvertion>();
            if (conversion != null)
            {
                yield return conversion.config;
            }
            if (collisionTagConvertionConfigWithPaths != null)
            {
                var path = ChilloutAvatarRelativePath(transform);
                var config = collisionTagConvertionConfigWithPaths.FirstOrDefault(p => p.path == path);
                if (config != null)
                {
                    yield return config.config;
                }
            }
            transform = transform.parent;
        }
    }

    void RemapAnimationOfContactComponent()
    {
        foreach (var layer in chilloutAnimatorController.layers)
        {
            if (layer.stateMachine != null)
            {
                RemapAnimationOfContactComponent(layer.stateMachine);
            }
        }
    }

    void RemapAnimationOfContactComponent(AnimatorStateMachine stateMachine)
    {
        foreach (var childState in stateMachine.states)
        {
            if (childState.state.motion is AnimationClip)
            {
                var newClip = RemapAnimationClipOfContactComponent(childState.state.motion as AnimationClip);
                if (newClip != null)
                {
                    childState.state.motion = newClip;
                }
            }
            if (childState.state.motion is BlendTree)
            {
                RemapAnimationOfContactComponent(childState.state.motion as BlendTree);
            }
        }
        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            RemapAnimationOfContactComponent(childStateMachine.stateMachine);
        }
    }

    void RemapAnimationOfContactComponent(BlendTree blendTree)
    {
        var children = blendTree.children;
        for (var i = 0; i < children.Length; ++i)
        {
            var childMotion = children[i];
            if (childMotion.motion is AnimationClip)
            {
                var newClip = RemapAnimationClipOfContactComponent(childMotion.motion as AnimationClip);
                if (newClip != null)
                {
                    childMotion.motion = newClip;
                    children[i] = childMotion;
                }
            }
            else if (childMotion.motion is BlendTree)
            {
                RemapAnimationOfContactComponent(childMotion.motion as BlendTree);
            }
        }
        blendTree.children = children;
    }

    AnimationClip RemapAnimationClipOfContactComponent(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        AnimationClip newClip = null;
        foreach (var binding in bindings)
        {
            if ((binding.type == typeof(VRCContactReceiver) || binding.type == typeof(VRCContactSender)))
            {
                if (newClip == null)
                {
                    newClip = new AnimationClip
                    {
                        name = clip.name + "_Remapped",
                        legacy = clip.legacy,
                        frameRate = clip.frameRate,
                        wrapMode = clip.wrapMode,
                    };
                    EditorUtility.CopySerialized(clip, newClip);
                }
                var curve = AnimationUtility.GetEditorCurve(newClip, binding);
                if (!contactComponentPathRemap.TryGetValue(binding.path, out var remappedPaths))
                {
                    remappedPaths = new string[] { binding.path };
                }
                foreach (var remappedPath in remappedPaths)
                {
                    foreach (var convertedBinding in ConvertBindingOfContactComponent(binding))
                    {
                        newClip.SetCurve(remappedPath, convertedBinding.type, convertedBinding.propertyName, curve);
                    }
                }
            }
        }
        if (newClip != null) Debug.Log($"Remapped: {clip}");
        return newClip;
    }

    IEnumerable<EditorCurveBinding> ConvertBindingOfContactComponent(EditorCurveBinding binding)
    {
        if (binding.propertyName == "m_Enabled")
        {
            return new EditorCurveBinding[]
            {
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(GameObject),
                    propertyName = "m_IsActive",
                },
            };
        }
        if (binding.propertyName == nameof(VRC.Dynamics.ContactBase.radius))
        {
            return new EditorCurveBinding[]
            {
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(SphereCollider),
                    propertyName = "m_Radius",
                },
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(CapsuleCollider),
                    propertyName = "m_Radius",
                },
            };
        }
        if (binding.propertyName == nameof(VRC.Dynamics.ContactBase.height))
        {
            return new EditorCurveBinding[]
            {
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(CapsuleCollider),
                    propertyName = "m_Height",
                },
            };
        }
        var positionAxis = Array.IndexOf(contactPositionProperties, binding.propertyName);
        if (positionAxis != -1)
        {
            return new EditorCurveBinding[]
            {
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(Transform),
                    propertyName = "localPosition." + contactAxis[positionAxis],
                },
            };
        }
        var rotationAxis = Array.IndexOf(contactRotationProperties, binding.propertyName);
        if (rotationAxis != -1)
        {
            return new EditorCurveBinding[]
            {
                new EditorCurveBinding
                {
                    path = binding.path,
                    type = typeof(Transform),
                    propertyName = "m_LocalRotation." + contactAxis[rotationAxis],
                },
            };
        }
        return new EditorCurveBinding[]
        {
            new EditorCurveBinding
            {
                path = binding.path,
                type = binding.type == typeof(VRCContactReceiver) ? typeof(CVRAdvancedAvatarSettingsTrigger) : typeof(CVRPointer),
                propertyName = binding.propertyName,
            }
        };
    }

    static string[] contactAxis = new string[] { "x", "y", "z", "w" };
    static string[] contactPositionProperties = new string[] { "position.x", "position.y", "position.z" };
    static string[] contactRotationProperties = new string[] { "rotation.x", "rotation.y", "rotation.z", "rotation.w" };

    void MakeProxyLayersOfConstantContactParameters()
    {
        var parameters = chilloutAnimatorController.parameters;
        AnimatorDriverTask.ParameterType TypeOf(string name) => AnimatorDriverParameterType(parameters, name);

        foreach (var parameterName in constantContactProxiedParameters)
        {
            var proxyParameter = new AnimatorControllerParameter
            {
                name = ConstantContactProxiedParameterName(parameterName),
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0,
            };
            ArrayUtility.Add(ref parameters, proxyParameter);
            var activeState = new AnimatorState
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = "Active",
                writeDefaultValues = true,
                motion = emptyClip,
                transitions = new AnimatorStateTransition[]
                {
                    new AnimatorStateTransition
                    {
                        hideFlags = HideFlags.HideInHierarchy,
                        hasExitTime = false,
                        hasFixedDuration = true,
                        exitTime = 0f,
                        duration = 0f,
                        offset = 0f,
                        isExit = true,
                        conditions = new AnimatorCondition[]
                        {
                            new AnimatorCondition
                            {
                                mode = AnimatorConditionMode.Equals,
                                parameter = proxyParameter.name,
                                threshold = 0f,
                            },
                        },
                    },
                },
                behaviours = new StateMachineBehaviour[]
                {
                    new AnimatorDriver
                    {
                        hideFlags = HideFlags.HideInHierarchy,
                        localOnly = false,
                        EnterTasks = new List<AnimatorDriverTask>
                        {
                            new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Set,
                                targetName = parameterName,
                                targetType = TypeOf(parameterName),
                                aType = AnimatorDriverTask.SourceType.Static,
                                aValue = 1f,
                            },
                        },
                        ExitTasks = new List<AnimatorDriverTask>
                        {
                            new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Set,
                                targetName = parameterName,
                                targetType = TypeOf(parameterName),
                                aType = AnimatorDriverTask.SourceType.Static,
                                aValue = 0f,
                            },
                        },
                    },
                },
            };
            var idleState = new AnimatorState
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = "Idle",
                writeDefaultValues = true,
                motion = emptyClip,
                transitions = new AnimatorStateTransition[]
                {
                    new AnimatorStateTransition
                    {
                        hideFlags = HideFlags.HideInHierarchy,
                        hasExitTime = false,
                        hasFixedDuration = true,
                        exitTime = 0f,
                        duration = 0f,
                        offset = 0f,
                        destinationState = activeState,
                        conditions = new AnimatorCondition[]
                        {
                            new AnimatorCondition
                            {
                                mode = AnimatorConditionMode.Greater,
                                parameter = proxyParameter.name,
                                threshold = 0f,
                            },
                        },
                    },
                },
            };
            var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_" + ConstantContactProxiedParameterName(parameterName));
            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = emptyMask,
                stateMachine = new AnimatorStateMachine
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    name = layerName,
                    entryPosition = new Vector3(0, -100),
                    exitPosition = new Vector3(0, 200),
                    anyStatePosition = new Vector3(0, -300),
                    defaultState = idleState,
                    states = new ChildAnimatorState[]
                    {
                        new ChildAnimatorState { state = idleState, position = new Vector3(0, 0) },
                        new ChildAnimatorState { state = activeState, position = new Vector3(0, 100) },
                    },
                },
            };
            AddGeneratedLayer(layer);
        }
        chilloutAnimatorController.parameters = parameters;
    }

    void MakeGestureWeightFeedLayers()
    {
        if (gestureWeightConversionMode != GestureWeightConversionMode.DerivedParameter)
        {
            return;
        }
        MakeGestureWeightFeedLayer("GestureLeft", "GestureLeftWeight");
        MakeGestureWeightFeedLayer("GestureRight", "GestureRightWeight");
    }

    // DerivedParameter mode: rebuild VRChat's weight semantics (Neutral: 0 / Fist: analog squeeze /
    // other gestures: fixed 1) from the gesture value. A Simple1D blend tree over GestureLeft with
    // clips that write the weight parameter gives the exact piecewise function: the 0..1 segment is
    // the identity ramp, and the tree clamps to 1 outside it (open hand -1 and gestures 2..6).
    // Consumers read the parameter one animator evaluation later (one frame of latency).
    // Runs after AdjustParameterNames so parameter names are final.
    void MakeGestureWeightFeedLayer(string gestureParameterName, string weightParameterName)
    {
        var parameters = chilloutAnimatorController.parameters;
        var weightParameter = parameters.FirstOrDefault(p => p.name == weightParameterName) ??
            parameters.FirstOrDefault(p => p.name == NonSyncParameterName(weightParameterName));
        if (weightParameter == null)
        {
            // the avatar does not use this weight parameter
            return;
        }
        if (!parameters.Any(p => p.name == gestureParameterName))
        {
            ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
            {
                name = gestureParameterName,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
            chilloutAnimatorController.parameters = parameters;
        }

        AnimationClip MakeWeightClip(float value)
        {
            var clip = new AnimationClip { name = "VRC3CVR_" + weightParameterName + "_" + value };
            clip.SetCurve("", typeof(Animator), weightParameter.name, AnimationCurve.Constant(0f, 1f, value));
            return clip;
        }
        var zeroClip = MakeWeightClip(0f);
        var oneClip = MakeWeightClip(1f);

        var blendTree = new BlendTree
        {
            name = "VRC3CVR_" + weightParameterName,
            hideFlags = HideFlags.HideInHierarchy,
            blendType = BlendTreeType.Simple1D,
            blendParameter = gestureParameterName,
            useAutomaticThresholds = false,
            minThreshold = -1f,
            maxThreshold = 2f,
        };
        blendTree.children = new ChildMotion[]
        {
            new ChildMotion { motion = oneClip, threshold = -1f, timeScale = 1f },
            new ChildMotion { motion = zeroClip, threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = oneClip, threshold = 1f, timeScale = 1f },
            new ChildMotion { motion = oneClip, threshold = 2f, timeScale = 1f },
        };

        var feedState = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Feed",
            writeDefaultValues = true,
            motion = blendTree,
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_" + weightParameterName);
        var layer = new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = feedState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = feedState, position = new Vector3(0, 0) },
                },
            },
        };
        AddGeneratedLayer(layer);
    }

    // VRChat supplies VelocityMagnitude; ChilloutVR does not, so recompute it from the
    // VelocityX/Y/Z core parameters, which the client feeds on every avatar copy (locals via the
    // movement system, remotes via PuppetMaster from the replicated movement data). The result
    // stays local (each client computes its own copy), so this costs no sync bits.
    // Runs after AdjustParameterNames so parameter names are final.
    void MakeVelocityMagnitudeFeedLayer()
    {
        var parameters = chilloutAnimatorController.parameters;
        var magnitudeParameter = parameters.FirstOrDefault(p => p.name == "VelocityMagnitude") ??
            parameters.FirstOrDefault(p => p.name == NonSyncParameterName("VelocityMagnitude"));
        if (magnitudeParameter == null)
        {
            // the avatar does not use VelocityMagnitude
            return;
        }

        foreach (var inputName in new[] { "VelocityX", "VelocityY", "VelocityZ" })
        {
            if (!parameters.Any(p => p.name == inputName))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = inputName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f,
                });
            }
        }
        var scratchParameter = new AnimatorControllerParameter
        {
            name = NonSyncParameterName("VelocityMagnitudeCalc"),
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 0f,
        };
        ArrayUtility.Add(ref parameters, scratchParameter);
        chilloutAnimatorController.parameters = parameters;

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

        var scratch = scratchParameter.name;
        var target = magnitudeParameter.name;
        // A short clip gives the state a length so the self transition re-enters it (and reruns
        // the driver) every tick; the animated property is undeclared and does nothing
        var tickClip = new AnimationClip { name = "VRC3CVR_VelocityMagnitudeTick" };
        tickClip.SetCurve("", typeof(Animator), NonSyncParameterName("VelocityMagnitudeTick"), AnimationCurve.Constant(0f, 1f / 60f, 0f));
        var recomputeState = new AnimatorState
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
                    // remote copies run this too; their VelocityX/Y/Z come from the replicated movement
                    localOnly = false,
                    EnterTasks = new List<AnimatorDriverTask>
                    {
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "VelocityX", "VelocityX"),
                        Task(AnimatorDriverTask.Operator.Multiplication, target, "VelocityY", "VelocityY"),
                        Task(AnimatorDriverTask.Operator.Addition, scratch, scratch, target),
                        Task(AnimatorDriverTask.Operator.Multiplication, target, "VelocityZ", "VelocityZ"),
                        Task(AnimatorDriverTask.Operator.Addition, scratch, scratch, target),
                        Task(AnimatorDriverTask.Operator.Power, target, scratch, null, 0.5f),
                    },
                },
            },
        };
        recomputeState.transitions = new AnimatorStateTransition[]
        {
            new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                hasExitTime = true,
                exitTime = 1f,
                hasFixedDuration = true,
                duration = 0f,
                offset = 0f,
                destinationState = recomputeState,
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_VelocityMagnitude");
        var layer = new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = recomputeState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = recomputeState, position = new Vector3(0, 0) },
                },
            },
        };
        AddGeneratedLayer(layer);
    }

    // VelocityY is deliberately untouched: the player only ever rotates about Y, so the vertical
    // axis is the same number in world space and in avatar space. VelocityMagnitude is untouched
    // for the same kind of reason — a magnitude has no orientation.
    void RemapVelocityToAvatarLocal()
    {
        var localX = NonSyncParameterName("VelocityXLocal");
        var localZ = NonSyncParameterName("VelocityZLocal");
        var uses = false;
        WalkParameterNames(name =>
        {
            if (name == "VelocityX")
            {
                uses = true;
                return localX;
            }
            if (name == "VelocityZ")
            {
                uses = true;
                return localZ;
            }
            return name;
        });
        if (!uses)
        {
            return;
        }
        // declared here rather than by the feed layer, whose guard is "does anything read these"
        var parameters = chilloutAnimatorController.parameters;
        foreach (var derived in new[] { localX, localZ })
        {
            if (!parameters.Any(p => p.name == derived))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = derived,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f,
                });
            }
        }
        chilloutAnimatorController.parameters = parameters;
        MakeLocomotionVelocityFeedLayer();
    }

    // ChilloutVR reports VelocityX/Y/Z in WORLD space (measured in game), while every VRChat layer
    // was authored against an avatar-LOCAL VelocityX/VelocityZ. The reconstruction cannot be written
    // back into VelocityX/Z — the client rewrites those every frame — so it lands in derived
    // parameters that RemapVelocityToAvatarLocal points the converted layers at.
    //
    // MovementX/Y supply the direction: player-local by construction, +X right and +Y forward, the
    // same axes VRChat means. Only their direction, though — 0.5 is the walk ring and 1.0 the run
    // ring, so the magnitude has to come from the world velocity instead. Both are ChilloutVR
    // synced core parameters, so this costs no sync bits and holds on remote copies too.
    void MakeLocomotionVelocityFeedLayer()
    {
        var parameters = chilloutAnimatorController.parameters;
        var localX = NonSyncParameterName("VelocityXLocal");
        var localZ = NonSyncParameterName("VelocityZLocal");
        if (!parameters.Any(p => p.name == localX) && !parameters.Any(p => p.name == localZ))
        {
            return;
        }
        foreach (var inputName in new[] { "VelocityX", "VelocityZ", "MovementX", "MovementY" })
        {
            if (!parameters.Any(p => p.name == inputName))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = inputName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f,
                });
            }
        }
        foreach (var derived in new[] { localX, localZ })
        {
            if (!parameters.Any(p => p.name == derived))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = derived,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f,
                });
            }
        }
        var scratch = NonSyncParameterName("VelocityLocalCalc");
        ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
        {
            name = scratch,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 0f,
        });
        chilloutAnimatorController.parameters = parameters;

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

        var tickClip = new AnimationClip { name = "VRC3CVR_LocomotionVelocityTick" };
        tickClip.SetCurve("", typeof(Animator), NonSyncParameterName("LocomotionVelocityTick"), AnimationCurve.Constant(0f, 1f / 60f, 0f));
        var recomputeState = new AnimatorState
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
                    localOnly = false,
                    EnterTasks = new List<AnimatorDriverTask>
                    {
                        // ground speed: a locomotion tree's space has no vertical axis
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "VelocityX", "VelocityX"),
                        Task(AnimatorDriverTask.Operator.Multiplication, localX, "VelocityZ", "VelocityZ"),
                        Task(AnimatorDriverTask.Operator.Addition, scratch, scratch, localX),
                        Task(AnimatorDriverTask.Operator.Power, scratch, scratch, null, 0.5f),
                        Task(AnimatorDriverTask.Operator.Multiplication, localZ, "MovementX", "MovementX"),
                        Task(AnimatorDriverTask.Operator.Multiplication, localX, "MovementY", "MovementY"),
                        Task(AnimatorDriverTask.Operator.Addition, localZ, localZ, localX),
                        Task(AnimatorDriverTask.Operator.Power, localZ, localZ, null, 0.5f),
                        // scale = speed / (ring + epsilon); standing still leaves speed ~0, so the
                        // scale collapses to ~0 instead of dividing by zero
                        Task(AnimatorDriverTask.Operator.Addition, localX, localZ, null, 0.0001f),
                        Task(AnimatorDriverTask.Operator.Division, localX, scratch, localX),
                        // Z first: filling X overwrites the scale both of them need
                        Task(AnimatorDriverTask.Operator.Multiplication, localZ, "MovementY", localX),
                        Task(AnimatorDriverTask.Operator.Multiplication, localX, "MovementX", localX),
                    },
                },
            },
        };
        recomputeState.transitions = new AnimatorStateTransition[]
        {
            new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                hasExitTime = true,
                exitTime = 1f,
                hasFixedDuration = true,
                duration = 0f,
                offset = 0f,
                destinationState = recomputeState,
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_LocomotionVelocity");
        AddGeneratedLayer(new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = recomputeState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = recomputeState, position = new Vector3(0, 0) },
                },
            },
        });
    }

    static AnimatorDriverTask DriverTask(AnimatorControllerParameter[] declared, AnimatorDriverTask.Operator op,
        string target, AnimatorDriverTask.ParameterType targetType, string a, string b, float bValue)
    {
        return new AnimatorDriverTask
        {
            op = op,
            targetName = target,
            targetType = targetType,
            aType = AnimatorDriverTask.SourceType.Parameter,
            aParamType = AnimatorDriverParameterType(declared, a),
            aName = a,
            bType = b == null ? AnimatorDriverTask.SourceType.Static : AnimatorDriverTask.SourceType.Parameter,
            bParamType = b == null ? AnimatorDriverTask.ParameterType.Float : AnimatorDriverParameterType(declared, b),
            bName = b ?? "",
            bValue = bValue,
        };
    }

    const string UprightSensorParameter = "UprightSensor";

    // Set while Upright is computed by the layer below instead of received from the client.
    bool uprightIsDerived;

    // ChilloutVR's AvatarUpright is the avatar's measured pose height -- an OUTPUT of the animator --
    // where VRChat's Upright is view and tracking height, an INPUT to it. On desktop the pose is
    // whatever the animator draws, so feeding the output back in as the input deadlocks: the stance
    // machine will not crouch until Upright falls, and Upright cannot fall until it crouches. In VR
    // the head is pinned to the headset and the hips follow the real head height, so AvatarUpright is
    // a genuine input there and the loop never closes. Desktop therefore gets a discrete value built
    // from the stance the client already knows, and VR keeps the continuous sensor -- which is what
    // preserves a deliberate half-crouch that no stance flag describes.
    //
    // Every input has to be synced, or a remote copy would compute from zeroes: Crouching, Prone and
    // VRMode already are, and the sensor takes over Upright's own sync slot.
    void MakeUprightFeedLayer()
    {
        var upright = NonSyncParameterName("Upright");
        var parameters = chilloutAnimatorController.parameters;
        if (!feedGameStateParameters || !vrcBaseReplacesCckLocomotion || !parameters.Any(p => p.name == upright))
        {
            return;
        }

        foreach (var (input, type) in new[]
        {
            ("Crouching", AnimatorControllerParameterType.Bool),
            ("Prone", AnimatorControllerParameterType.Bool),
            ("VRMode", AnimatorControllerParameterType.Int),
        })
        {
            if (!parameters.Any(p => p.name == input))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter { name = input, type = type });
            }
        }
        var scratch = NonSyncParameterName("UprightCalc");
        foreach (var (derived, defaultFloat) in new[] { (UprightSensorParameter, 1f), (scratch, 0f) })
        {
            if (!parameters.Any(p => p.name == derived))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = derived,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = defaultFloat,
                });
            }
        }
        chilloutAnimatorController.parameters = parameters;

        var declared = chilloutAnimatorController.parameters;
        AnimatorDriverTask Task(AnimatorDriverTask.Operator op, string target, string a, string b, float bValue = 0f) =>
            DriverTask(declared, op, target, AnimatorDriverTask.ParameterType.Float, a, b, bValue);

        var tickClip = new AnimationClip { name = "VRC3CVR_UprightTick" };
        tickClip.SetCurve("", typeof(Animator), NonSyncParameterName("UprightTick"), AnimationCurve.Constant(0f, 1f / 60f, 0f));
        var recomputeState = new AnimatorState
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
                    localOnly = false,
                    EnterTasks = new List<AnimatorDriverTask>
                    {
                        // 1 - 0.45*Crouching - 0.8*Prone + 0.45*Crouching*Prone, which lands on
                        // standing 1, crouching 0.55, prone 0.2, and prone again when both are set
                        Task(AnimatorDriverTask.Operator.Multiplication, upright, "Crouching", null, -0.45f),
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "Prone", null, -0.8f),
                        Task(AnimatorDriverTask.Operator.Addition, upright, upright, scratch),
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "Crouching", "Prone"),
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, scratch, null, 0.45f),
                        Task(AnimatorDriverTask.Operator.Addition, upright, upright, scratch),
                        Task(AnimatorDriverTask.Operator.Addition, upright, upright, null, 1f),
                        // lerp on VRMode, which is 0 or 1: desktop keeps the value above, VR replaces
                        // it with the sensor
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "VRMode", null, -1f),
                        Task(AnimatorDriverTask.Operator.Addition, scratch, scratch, null, 1f),
                        Task(AnimatorDriverTask.Operator.Multiplication, upright, upright, scratch),
                        Task(AnimatorDriverTask.Operator.Multiplication, scratch, "VRMode", UprightSensorParameter),
                        Task(AnimatorDriverTask.Operator.Addition, upright, upright, scratch),
                    },
                },
            },
        };
        recomputeState.transitions = new AnimatorStateTransition[]
        {
            new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                hasExitTime = true,
                exitTime = 1f,
                hasFixedDuration = true,
                duration = 0f,
                offset = 0f,
                destinationState = recomputeState,
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_Upright");
        AddGeneratedLayer(new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = recomputeState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = recomputeState, position = new Vector3(0, 0) },
                },
            },
        });
        uprightIsDerived = true;
    }

    const string FullBodyParameter = "TrackingTypeFullBody";

    // VRChat's TrackingType counts the wearer's tracked points; ChilloutVR reports only whether full
    // body is on, which leaves two values a converted humanoid can honestly report. 3 covers both
    // head-and-hands VR and desktop -- VRChat gives desktop humanoids 3 as well, and has the avatar
    // tell the two apart by VRMode -- and 6 covers full body. The 4 and 5 of a hip-only or feet-only
    // rig are indistinguishable from 6 behind a single flag, and 1 belongs to generic rigs.
    //
    // The flag is what gets synced and every client derives the number from it, so a remote copy
    // costs a bool rather than TrackingType's int (see MakeGameStateParameterStreams).
    void MakeTrackingTypeFeedLayer()
    {
        var trackingType = NonSyncParameterName("TrackingType");
        var parameters = chilloutAnimatorController.parameters;
        if (!feedGameStateParameters || !parameters.Any(p => p.name == trackingType))
        {
            return;
        }
        if (!parameters.Any(p => p.name == FullBodyParameter))
        {
            ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
            {
                name = FullBodyParameter,
                type = AnimatorControllerParameterType.Bool,
            });
            chilloutAnimatorController.parameters = parameters;
        }

        var declared = chilloutAnimatorController.parameters;
        var targetType = AnimatorDriverParameterType(declared, trackingType);
        var tickClip = new AnimationClip { name = "VRC3CVR_TrackingTypeTick" };
        tickClip.SetCurve("", typeof(Animator), NonSyncParameterName("TrackingTypeTick"), AnimationCurve.Constant(0f, 1f / 60f, 0f));
        var recomputeState = new AnimatorState
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
                    localOnly = false,
                    EnterTasks = new List<AnimatorDriverTask>
                    {
                        // 3 + 3 * FullBody
                        DriverTask(declared, AnimatorDriverTask.Operator.Multiplication, trackingType, targetType, FullBodyParameter, null, 3f),
                        DriverTask(declared, AnimatorDriverTask.Operator.Addition, trackingType, targetType, trackingType, null, 3f),
                    },
                },
            },
        };
        recomputeState.transitions = new AnimatorStateTransition[]
        {
            new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                hasExitTime = true,
                exitTime = 1f,
                hasFixedDuration = true,
                duration = 0f,
                offset = 0f,
                destinationState = recomputeState,
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_TrackingType");
        AddGeneratedLayer(new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = recomputeState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = recomputeState, position = new Vector3(0, 0) },
                },
            },
        });
    }

    const string VrcEmoteCompatLayerName = "VRC3CVR_VRCEmoteCompat";
    const int VrcEmoteCount = 8;

    // Bridges ChilloutVR's own quick-menu Emote onto VRCEmote for a fold that reads VRCEmote, so
    // removing ChilloutVR's own Emotes machine (FoldActionMachine) does not silence that menu.
    // ChilloutVR reports a press as a pulse of about a tenth of a second rather than a value it
    // holds, while a VRChat Action layer reads the number for as long as the emote runs -- so the
    // number is latched here on the way in and let go only on a cancel or as the emote that was
    // holding it ends. ChilloutVR's own Emotes machine absorbs the same pulse the same way, by
    // latching its band on entry and leaving on its clip rather than on the number.
    void MakeVrcEmoteCompatFeedLayer()
    {
        if (!vrcActionFoldReadsVrcEmote)
        {
            return;
        }

        var declared = chilloutAnimatorController.parameters;
        var vrcEmote = declared.FirstOrDefault(p => p.name == VrcEmoteParameterName)
            ?? declared.FirstOrDefault(p => p.name == NonSyncParameterName(VrcEmoteParameterName));
        if (vrcEmote == null)
        {
            return;
        }
        var vrcEmoteType = AnimatorDriverParameterType(declared, vrcEmote.name);

        AnimatorDriverTask SetVrcEmote(float value) => new AnimatorDriverTask
        {
            op = AnimatorDriverTask.Operator.Set,
            targetName = vrcEmote.name,
            targetType = vrcEmoteType,
            aType = AnimatorDriverTask.SourceType.Static,
            aValue = value,
        };

        var idle = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Idle",
            writeDefaultValues = false,
        };

        AnimatorDriver Writes(float value) => new AnimatorDriver
        {
            hideFlags = HideFlags.HideInHierarchy,
            localOnly = true,
            EnterTasks = new List<AnimatorDriverTask> { SetVrcEmote(value) },
        };

        // Only entering writes, which is what leaves a custom menu driving VRCEmote directly alone:
        // while Emote sits at 0 this layer holds in Idle and touches nothing.
        AnimatorState MakeEmoteState(string name, float vrcEmoteValue) => new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = name,
            writeDefaultValues = false,
            behaviours = new StateMachineBehaviour[] { Writes(vrcEmoteValue) },
        };

        var cancel = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Cancel",
            writeDefaultValues = false,
            behaviours = new StateMachineBehaviour[] { Writes(0f) },
        };

        AnimatorStateTransition MakeTransition(AnimatorState destination, AnimatorConditionMode mode, float threshold, string parameter) =>
            Timed(new AnimatorStateTransition
            {
                hideFlags = HideFlags.HideInHierarchy,
                destinationState = destination,
                conditions = new AnimatorCondition[]
                {
                    new AnimatorCondition { mode = mode, parameter = parameter, threshold = threshold },
                },
            }, 0f);

        var emoteStates = Enumerable.Range(1, VrcEmoteCount).Select(n => MakeEmoteState("Emote" + n, n)).ToArray();

        // highest band first, mirroring the ordered Greater cascade CCK's own Emotes machine dispatches with
        idle.transitions = Enumerable.Range(1, VrcEmoteCount).Reverse()
            .Select(n => MakeTransition(emoteStates[n - 1], AnimatorConditionMode.Greater, n - 1, EmoteParameterName))
            .ToArray();

        for (var n = 1; n <= VrcEmoteCount; n++)
        {
            emoteStates[n - 1].transitions = new[]
            {
                MakeTransition(cancel, AnimatorConditionMode.If, 0f, CancelEmoteParameterName),
                MakeTransition(idle, AnimatorConditionMode.Less, n, EmoteParameterName),
                MakeTransition(idle, AnimatorConditionMode.Greater, n, EmoteParameterName),
            };
        }
        cancel.transitions = new[]
        {
            Timed(new AnimatorStateTransition { hideFlags = HideFlags.HideInHierarchy, destinationState = idle }, 0f),
        };

        // A one-shot emote ends by running out of its own machine rather than by the number changing,
        // and the hub dispatches on that number, so the latch comes down as the emote is left --
        // but only while it is still the number that emote was holding. Leaving because the
        // selection moved on hands the latch to the emote that comes next, and taking it down there
        // would strand the new one. The test needs two tasks because one carries a single operator:
        // the first asks whether the number is still ours, the second answers with 0 or leaves it be.
        if (foldedActionEmoteNumbers.Count == 0)
        {
            Debug.LogWarning($"No state of the Action animator is entered on a value of {VrcEmoteParameterName}, so nothing lowers it once an emote ends and the avatar will play it again as soon as it finishes. Dispatch each emote state on its own number, as VRChat's stock Action layer does.");
        }
        else
        {
            var stillOurs = NonSyncParameterName("VRCEmoteHeld");
            var parameters = chilloutAnimatorController.parameters;
            if (!parameters.Any(p => p.name == stillOurs))
            {
                ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
                {
                    name = stillOurs,
                    type = AnimatorControllerParameterType.Int,
                });
                chilloutAnimatorController.parameters = parameters;
            }
            var stillOursType = AnimatorDriverParameterType(chilloutAnimatorController.parameters, stillOurs);

            foreach (var entry in foldedActionEmoteNumbers)
            {
                var behaviours = entry.Key.behaviours;
                ArrayUtility.Add(ref behaviours, new AnimatorDriver
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    localOnly = true,
                    ExitTasks = new List<AnimatorDriverTask>
                    {
                        new AnimatorDriverTask
                        {
                            op = AnimatorDriverTask.Operator.Equal,
                            targetName = stillOurs,
                            targetType = stillOursType,
                            aType = AnimatorDriverTask.SourceType.Parameter,
                            aName = vrcEmote.name,
                            aParamType = vrcEmoteType,
                            bType = AnimatorDriverTask.SourceType.Static,
                            bValue = entry.Value,
                        },
                        new AnimatorDriverTask
                        {
                            op = AnimatorDriverTask.Operator.Conditional,
                            targetName = vrcEmote.name,
                            targetType = vrcEmoteType,
                            aType = AnimatorDriverTask.SourceType.Parameter,
                            aName = stillOurs,
                            aParamType = stillOursType,
                            bType = AnimatorDriverTask.SourceType.Static,
                            bValue = 0f,
                            cType = AnimatorDriverTask.SourceType.Parameter,
                            cName = vrcEmote.name,
                            cParamType = vrcEmoteType,
                        },
                    },
                });
                entry.Key.behaviours = behaviours;
            }
        }

        var layerName = chilloutAnimatorController.MakeUniqueLayerName(VrcEmoteCompatLayerName);
        AddGeneratedLayer(new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = emptyMask,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                exitPosition = new Vector3(0, 200),
                anyStatePosition = new Vector3(0, -300),
                defaultState = idle,
                states = new[] { idle }.Concat(emoteStates).Concat(new[] { cancel })
                    .Select((state, i) => new ChildAnimatorState { state = state, position = new Vector3(0, i * 100) })
                    .ToArray(),
            },
        });
    }

    // VRChat built-ins that ChilloutVR does not supply to the animator. The client's parameter
    // stream provides equivalent sources; each stream type's semantics were verified against the
    // decompiled client (DeviceMode: isUsingVr ? 1 : 0, matching VRMode). The last entry is not a
    // VRChat name: it is the input the derived TrackingType is built from (MakeTrackingTypeFeedLayer).
    static readonly (string parameterName, CVRParameterStreamEntry.Type streamType)[] GameStateParameterStreams =
    {
        ("MuteSelf", CVRParameterStreamEntry.Type.LocalPlayerMuted),
        ("VRMode", CVRParameterStreamEntry.Type.DeviceMode),
        ("Upright", CVRParameterStreamEntry.Type.AvatarUpright),
        (FullBodyParameter, CVRParameterStreamEntry.Type.LocalPlayerFullBodyEnabled),
    };

    // Feed the parameters above on the wearer's client via CVRParameterStream; the parameters
    // are kept synced (AdjustParameterNames) so remotes receive the values through CVR's normal
    // parameter sync. Runs after AdjustParameterNames so parameter names are final.
    void MakeGameStateParameterStreams()
    {
        if (!feedGameStateParameters)
        {
            return;
        }
        var parameters = chilloutAnimatorController.parameters;
        var streamedEntries = new List<CVRParameterStreamEntry>();
        foreach (var (parameterName, streamType) in GameStateParameterStreams)
        {
            var target = uprightIsDerived && streamType == CVRParameterStreamEntry.Type.AvatarUpright
                ? UprightSensorParameter
                : parameterName;
            if (!parameters.Any(p => p.name == target))
            {
                // the avatar does not use this parameter
                continue;
            }
            streamedEntries.Add(new CVRParameterStreamEntry
            {
                type = streamType,
                targetType = CVRParameterStreamEntry.TargetType.AvatarAnimator,
                applicationType = CVRParameterStreamEntry.ApplicationType.Override,
                parameterName = target,
            });
        }
        if (streamedEntries.Count == 0)
        {
            return;
        }

        var stream = chilloutAvatarGameObject.GetComponent<CVRParameterStream>();
        if (stream == null)
        {
            stream = chilloutAvatarGameObject.AddComponent<CVRParameterStream>();
        }
        stream.referenceType = CVRParameterStream.ReferenceType.Avatar;
        // Replace our own entry types so reconversion stays idempotent, but keep any other entries
        var streamedTypes = GameStateParameterStreams.Select(s => s.streamType).ToHashSet();
        stream.entries.RemoveAll(entry => entry != null && streamedTypes.Contains(entry.type));
        stream.entries.AddRange(streamedEntries);
    }

    // VRC Constraints -> Unity Constraints. The field and animation property mappings are the
    // reverse of the VRC SDK's own Unity->VRC converter tables in AvatarDynamicsSetup.cs
    // (ConstraintAnimatorTypeRebindDictionary / ConstraintAnimatorPropertyRebindDictionary /
    // ConstraintAnimatorArrayPostfixPropertyRebindDictionary).
    static readonly Dictionary<Type, Type> vrcToUnityConstraintTypeMap = new Dictionary<Type, Type>
    {
        { typeof(VRCPositionConstraint), typeof(PositionConstraint) },
        { typeof(VRCRotationConstraint), typeof(RotationConstraint) },
        { typeof(VRCScaleConstraint), typeof(ScaleConstraint) },
        { typeof(VRCParentConstraint), typeof(ParentConstraint) },
        { typeof(VRCAimConstraint), typeof(AimConstraint) },
        { typeof(VRCLookAtConstraint), typeof(LookAtConstraint) },
    };

    void ConvertVrcConstraintsToUnityConstraints()
    {
        constraintComponentPathRemap = new Dictionary<(string, Type), (string, int)>();
        var avatarRoot = chilloutAvatarGameObject.transform;
        foreach (var vrcConstraint in chilloutAvatarGameObject.GetComponentsInChildren<VRCConstraintBase>(true))
        {
            if (!vrcToUnityConstraintTypeMap.TryGetValue(vrcConstraint.GetType(), out var unityType))
            {
                Debug.LogWarning($"Unknown VRC constraint type \"{vrcConstraint.GetType().Name}\" on \"{vrcConstraint.name}\" is not converted");
                continue;
            }

            // Unity constraints can only move their own transform, so the converted constraint
            // lives on the target's GameObject when a Target Transform is set
            var host = vrcConstraint.GetEffectiveTargetTransform();
            if (host == null)
            {
                host = vrcConstraint.transform;
            }
            if (host != avatarRoot && !host.IsChildOf(avatarRoot))
            {
                Debug.LogWarning($"VRC constraint on \"{vrcConstraint.name}\" targets \"{host.name}\" outside the avatar; attaching the converted constraint to itself instead");
                host = vrcConstraint.transform;
            }

            if (vrcConstraint.FreezeToWorld)
            {
                Debug.LogWarning($"VRC constraint on \"{vrcConstraint.name}\" uses FreezeToWorld, which has no Unity equivalent and is dropped");
            }
            if (vrcConstraint.SolveInLocalSpace)
            {
                Debug.LogWarning($"VRC constraint on \"{vrcConstraint.name}\" uses SolveInLocalSpace, which has no Unity equivalent; the converted constraint solves in world space");
            }

            // Unity constraints are [DisallowMultipleComponent]; merge sources into an existing one
            var existingConstraint = host.GetComponent(unityType);
            var merged = existingConstraint != null;
            var unityConstraint = (IConstraint)(existingConstraint != null ? existingConstraint : host.gameObject.AddComponent(unityType));
            if (merged)
            {
                Debug.LogWarning($"Multiple constraints of type {unityType.Name} on \"{host.name}\"; sources are merged and the first constraint's settings win");
            }

            var sourceIndexOffset = AddConstraintSources(vrcConstraint, unityConstraint);

            if (!merged)
            {
                switch (vrcConstraint)
                {
                    case VRCPositionConstraint c:
                    {
                        var unity = (PositionConstraint)unityConstraint;
                        unity.translationAtRest = c.PositionAtRest;
                        unity.translationOffset = c.PositionOffset;
                        unity.translationAxis = ConstraintAxesFrom(c.AffectsPositionX, c.AffectsPositionY, c.AffectsPositionZ);
                        break;
                    }
                    case VRCRotationConstraint c:
                    {
                        var unity = (RotationConstraint)unityConstraint;
                        unity.rotationAtRest = c.RotationAtRest;
                        unity.rotationOffset = c.RotationOffset;
                        unity.rotationAxis = ConstraintAxesFrom(c.AffectsRotationX, c.AffectsRotationY, c.AffectsRotationZ);
                        break;
                    }
                    case VRCScaleConstraint c:
                    {
                        var unity = (ScaleConstraint)unityConstraint;
                        unity.scaleAtRest = c.ScaleAtRest;
                        unity.scaleOffset = c.ScaleOffset;
                        unity.scalingAxis = ConstraintAxesFrom(c.AffectsScaleX, c.AffectsScaleY, c.AffectsScaleZ);
                        break;
                    }
                    case VRCParentConstraint c:
                    {
                        var unity = (ParentConstraint)unityConstraint;
                        unity.translationAtRest = c.PositionAtRest;
                        unity.rotationAtRest = c.RotationAtRest;
                        unity.translationAxis = ConstraintAxesFrom(c.AffectsPositionX, c.AffectsPositionY, c.AffectsPositionZ);
                        unity.rotationAxis = ConstraintAxesFrom(c.AffectsRotationX, c.AffectsRotationY, c.AffectsRotationZ);
                        break;
                    }
                    case VRCAimConstraint c:
                    {
                        var unity = (AimConstraint)unityConstraint;
                        unity.aimVector = c.AimAxis;
                        unity.upVector = c.UpAxis;
                        unity.worldUpVector = c.WorldUpVector;
                        unity.worldUpObject = c.WorldUpTransform;
                        // the enum values are identical (SceneUp/ObjectUp/ObjectRotationUp/Vector/None)
                        unity.worldUpType = (AimConstraint.WorldUpType)(int)c.WorldUp;
                        unity.rotationAtRest = c.RotationAtRest;
                        unity.rotationOffset = c.RotationOffset;
                        unity.rotationAxis = ConstraintAxesFrom(c.AffectsRotationX, c.AffectsRotationY, c.AffectsRotationZ);
                        break;
                    }
                    case VRCLookAtConstraint c:
                    {
                        var unity = (LookAtConstraint)unityConstraint;
                        unity.roll = c.Roll;
                        unity.useUpObject = c.UseUpTransform;
                        unity.worldUpObject = c.WorldUpTransform;
                        unity.rotationAtRest = c.RotationAtRest;
                        unity.rotationOffset = c.RotationOffset;
                        break;
                    }
                }

                ((Behaviour)unityConstraint).enabled = vrcConstraint.enabled;
                unityConstraint.weight = vrcConstraint.GlobalWeight;
                unityConstraint.locked = vrcConstraint.Locked;
                // activate last so Unity does not recompute the rest state from the current pose
                unityConstraint.constraintActive = vrcConstraint.IsActive;
            }

            var oldPath = ChilloutAvatarRelativePath(vrcConstraint);
            var newPath = ChilloutAvatarRelativePath(host.gameObject);
            var remapKey = (oldPath, vrcConstraint.GetType());
            if (!constraintComponentPathRemap.ContainsKey(remapKey))
            {
                // first component wins: Unity resolves animation bindings to the first
                // component of a type, so keep the first converted constraint's mapping
                constraintComponentPathRemap[remapKey] = (newPath, sourceIndexOffset);
            }
            UnityEngine.Object.DestroyImmediate(vrcConstraint);
        }
    }

    static Axis ConstraintAxesFrom(bool x, bool y, bool z)
    {
        return (x ? Axis.X : Axis.None) | (y ? Axis.Y : Axis.None) | (z ? Axis.Z : Axis.None);
    }

    // Returns the index at which this constraint's sources begin in the (possibly merged)
    // Unity constraint, so animated per-source properties can be re-indexed
    int AddConstraintSources(VRCConstraintBase vrcConstraint, IConstraint unityConstraint)
    {
        // Sources with a null transform are kept: they preserve the indices of animated
        // per-source properties and their weight still participates in weight normalization
        var sources = new List<ConstraintSource>();
        unityConstraint.GetSources(sources);
        var baseIndex = sources.Count;
        var vrcSources = new List<VRC.Dynamics.VRCConstraintSource>();
        foreach (var source in vrcConstraint.Sources)
        {
            vrcSources.Add(source);
            sources.Add(new ConstraintSource
            {
                sourceTransform = source.SourceTransform,
                weight = source.Weight,
            });
        }
        unityConstraint.SetSources(sources);
        if (unityConstraint is ParentConstraint parentConstraint)
        {
            for (var i = 0; i < vrcSources.Count; i++)
            {
                parentConstraint.SetTranslationOffset(baseIndex + i, vrcSources[i].ParentPositionOffset);
                parentConstraint.SetRotationOffset(baseIndex + i, vrcSources[i].ParentRotationOffset);
            }
        }
        return baseIndex;
    }

    // Reverse of the VRC SDK's ConstraintAnimatorPropertyRebindDictionary
    static readonly Dictionary<string, string> vrcToUnityConstraintPropertyMap = new Dictionary<string, string>
    {
        { "m_Enabled", "m_Enabled" },
        { "IsActive", "m_Active" },
        { "GlobalWeight", "m_Weight" },
        { "Locked", "m_IsLocked" },
        { "PositionAtRest.x", "m_TranslationAtRest.x" },
        { "PositionAtRest.y", "m_TranslationAtRest.y" },
        { "PositionAtRest.z", "m_TranslationAtRest.z" },
        { "PositionOffset.x", "m_TranslationOffset.x" },
        { "PositionOffset.y", "m_TranslationOffset.y" },
        { "PositionOffset.z", "m_TranslationOffset.z" },
        { "AffectsPositionX", "m_AffectTranslationX" },
        { "AffectsPositionY", "m_AffectTranslationY" },
        { "AffectsPositionZ", "m_AffectTranslationZ" },
        { "RotationAtRest.x", "m_RotationAtRest.x" },
        { "RotationAtRest.y", "m_RotationAtRest.y" },
        { "RotationAtRest.z", "m_RotationAtRest.z" },
        { "RotationOffset.x", "m_RotationOffset.x" },
        { "RotationOffset.y", "m_RotationOffset.y" },
        { "RotationOffset.z", "m_RotationOffset.z" },
        { "AffectsRotationX", "m_AffectRotationX" },
        { "AffectsRotationY", "m_AffectRotationY" },
        { "AffectsRotationZ", "m_AffectRotationZ" },
        { "ScaleAtRest.x", "m_ScaleAtRest.x" },
        { "ScaleAtRest.y", "m_ScaleAtRest.y" },
        { "ScaleAtRest.z", "m_ScaleAtRest.z" },
        { "ScaleOffset.x", "m_ScaleOffset.x" },
        { "ScaleOffset.y", "m_ScaleOffset.y" },
        { "ScaleOffset.z", "m_ScaleOffset.z" },
        { "AffectsScaleX", "m_AffectScalingX" },
        { "AffectsScaleY", "m_AffectScalingY" },
        { "AffectsScaleZ", "m_AffectScalingZ" },
        { "AimAxis.x", "m_AimVector.x" },
        { "AimAxis.y", "m_AimVector.y" },
        { "AimAxis.z", "m_AimVector.z" },
        { "UpAxis.x", "m_UpVector.x" },
        { "UpAxis.y", "m_UpVector.y" },
        { "UpAxis.z", "m_UpVector.z" },
        { "WorldUpVector.x", "m_WorldUpVector.x" },
        { "WorldUpVector.y", "m_WorldUpVector.y" },
        { "WorldUpVector.z", "m_WorldUpVector.z" },
        { "WorldUpTransform", "m_WorldUpObject" },
        { "WorldUp", "m_UpType" },
        { "UseUpTransform", "m_UseUpObject" },
        { "Roll", "m_Roll" },
    };

    // Reverse of the VRC SDK's ConstraintAnimatorArrayPostfixPropertyRebindDictionary
    static readonly System.Text.RegularExpressions.Regex constraintSourcePropertyRe = new System.Text.RegularExpressions.Regex(@"^Sources\.source(\d+)\.(.+)$");
    static readonly Dictionary<string, string> vrcToUnityConstraintSourcePropertyMap = new Dictionary<string, string>
    {
        { "SourceTransform", "m_Sources.Array.data[{0}].sourceTransform" },
        { "Weight", "m_Sources.Array.data[{0}].weight" },
        { "ParentPositionOffset.x", "m_TranslationOffsets.Array.data[{0}].x" },
        { "ParentPositionOffset.y", "m_TranslationOffsets.Array.data[{0}].y" },
        { "ParentPositionOffset.z", "m_TranslationOffsets.Array.data[{0}].z" },
        { "ParentRotationOffset.x", "m_RotationOffsets.Array.data[{0}].x" },
        { "ParentRotationOffset.y", "m_RotationOffsets.Array.data[{0}].y" },
        { "ParentRotationOffset.z", "m_RotationOffsets.Array.data[{0}].z" },
    };

    bool TryConvertConstraintBinding(EditorCurveBinding binding, out EditorCurveBinding converted)
    {
        converted = default;
        if (!vrcToUnityConstraintTypeMap.TryGetValue(binding.type, out var unityType))
        {
            return false;
        }
        var path = binding.path;
        var sourceIndexOffset = 0;
        if (constraintComponentPathRemap.TryGetValue((binding.path, binding.type), out var remapped))
        {
            path = remapped.path;
            sourceIndexOffset = remapped.sourceIndexOffset;
        }
        string propertyName;
        if (vrcToUnityConstraintPropertyMap.TryGetValue(binding.propertyName, out var mappedProperty))
        {
            propertyName = mappedProperty;
        }
        else
        {
            var match = constraintSourcePropertyRe.Match(binding.propertyName);
            if (!match.Success || !vrcToUnityConstraintSourcePropertyMap.TryGetValue(match.Groups[2].Value, out var mappedSourceProperty))
            {
                // VRC-only properties (FreezeToWorld, SolveInLocalSpace, RebakeOffsetsWhenUnfrozen, TargetTransform)
                return false;
            }
            // when constraints were merged, this constraint's sources sit after the ones merged before it
            var sourceIndex = int.Parse(match.Groups[1].Value) + sourceIndexOffset;
            propertyName = string.Format(mappedSourceProperty, sourceIndex);
        }
        converted = new EditorCurveBinding
        {
            path = path,
            type = unityType,
            propertyName = propertyName,
        };
        return true;
    }

    void RemapAnimationOfConstraintComponent()
    {
        foreach (var layer in chilloutAnimatorController.layers)
        {
            if (layer.stateMachine != null)
            {
                RemapAnimationOfConstraintComponent(layer.stateMachine);
            }
        }
    }

    void RemapAnimationOfConstraintComponent(AnimatorStateMachine stateMachine)
    {
        foreach (var childState in stateMachine.states)
        {
            if (childState.state.motion is AnimationClip)
            {
                var newClip = RemapAnimationClipOfConstraintComponent(childState.state.motion as AnimationClip);
                if (newClip != null)
                {
                    childState.state.motion = newClip;
                }
            }
            if (childState.state.motion is BlendTree)
            {
                RemapAnimationOfConstraintComponent(childState.state.motion as BlendTree);
            }
        }
        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            RemapAnimationOfConstraintComponent(childStateMachine.stateMachine);
        }
    }

    void RemapAnimationOfConstraintComponent(BlendTree blendTree)
    {
        var children = blendTree.children;
        for (var i = 0; i < children.Length; ++i)
        {
            var childMotion = children[i];
            if (childMotion.motion is AnimationClip)
            {
                var newClip = RemapAnimationClipOfConstraintComponent(childMotion.motion as AnimationClip);
                if (newClip != null)
                {
                    childMotion.motion = newClip;
                    children[i] = childMotion;
                }
            }
            else if (childMotion.motion is BlendTree)
            {
                RemapAnimationOfConstraintComponent(childMotion.motion as BlendTree);
            }
        }
        blendTree.children = children;
    }

    AnimationClip RemapAnimationClipOfConstraintComponent(AnimationClip clip)
    {
        var floatBindings = AnimationUtility.GetCurveBindings(clip);
        var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        if (!floatBindings.Concat(objectBindings).Any(b => vrcToUnityConstraintTypeMap.ContainsKey(b.type)))
        {
            return null;
        }
        var newClip = new AnimationClip
        {
            name = clip.name + "_Remapped",
            legacy = clip.legacy,
            frameRate = clip.frameRate,
            wrapMode = clip.wrapMode,
        };
        EditorUtility.CopySerialized(clip, newClip);
        newClip.name = clip.name + "_Remapped";
        foreach (var binding in floatBindings)
        {
            if (!vrcToUnityConstraintTypeMap.ContainsKey(binding.type))
            {
                continue;
            }
            var curve = AnimationUtility.GetEditorCurve(newClip, binding);
            AnimationUtility.SetEditorCurve(newClip, binding, null);
            if (TryConvertConstraintBinding(binding, out var converted))
            {
                AnimationUtility.SetEditorCurve(newClip, converted, curve);
            }
            else
            {
                Debug.LogWarning($"Animated constraint property \"{binding.propertyName}\" at \"{binding.path}\" in clip \"{clip.name}\" has no Unity equivalent and is dropped");
            }
        }
        foreach (var binding in objectBindings)
        {
            if (!vrcToUnityConstraintTypeMap.ContainsKey(binding.type))
            {
                continue;
            }
            var objectCurve = AnimationUtility.GetObjectReferenceCurve(newClip, binding);
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, null);
            if (TryConvertConstraintBinding(binding, out var converted))
            {
                AnimationUtility.SetObjectReferenceCurve(newClip, converted, objectCurve);
            }
            else
            {
                Debug.LogWarning($"Animated constraint property \"{binding.propertyName}\" at \"{binding.path}\" in clip \"{clip.name}\" has no Unity equivalent and is dropped");
            }
        }
        Debug.Log($"Remapped constraints: {clip}");
        return newClip;
    }

    void EnsureLocalOnlyContacts()
    {
        if (localPointerPaths.Count == 0 && localTriggerPaths.Count == 0)
        {
            return;
        }
        // The avatar may already declare IsLocal itself, and not necessarily as a Bool: a blend
        // tree's blend parameter has to be a Float, so an avatar that drives anything off IsLocal
        // through a blend tree declares it Float. CopyParametersTo keeps the first declaration it
        // sees, so that type reaches here. Match the condition mode to whatever is actually there
        // instead of assuming Bool -- assuming it produces
        // "uses parameter 'IsLocal' which is not compatible with condition type" and a dead layer.
        var existingIsLocal = chilloutAnimatorController.parameters.FirstOrDefault(p => p.name == "IsLocal");
        AnimatorCondition localCondition;
        AnimatorCondition remoteCondition;

        if (existingIsLocal == null)
        {
            var parameters = chilloutAnimatorController.parameters;
            ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
            {
                name = "IsLocal",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            chilloutAnimatorController.parameters = parameters;
            localCondition = new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "IsLocal", threshold = 1f };
            remoteCondition = new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "IsLocal", threshold = 1f };
        }
        else
        {
            var rawLocal = new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "IsLocal", threshold = 1f };
            var rawRemote = new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "IsLocal", threshold = 1f };
            if (!VRC3CVRConditionTypes.TryAdapt(rawLocal, existingIsLocal.type, out localCondition)
                || !VRC3CVRConditionTypes.TryAdapt(rawRemote, existingIsLocal.type, out remoteCondition))
            {
                // A Trigger cannot express "not fired", so there is no pair of conditions that
                // would work. Skip the layer rather than emit a broken one.
                Debug.LogWarning(
                    "VRC3CVR: the avatar declares IsLocal as " + existingIsLocal.type
                        + ", which cannot drive the local-only contact layer. "
                        + "Local-only contacts will stay enabled on remote copies.");
                return;
            }

            if (existingIsLocal.type != AnimatorControllerParameterType.Bool)
            {
                Debug.LogWarning(
                    "VRC3CVR: the avatar declares IsLocal as " + existingIsLocal.type
                        + " rather than Bool, so the local-only contact layer compares it numerically. "
                        + "Check that ChilloutVR actually drives IsLocal in that type on your avatar.");
            }
        }

        var remoteClip = new AnimationClip { name = "VRC3CVR_DisableLocalOnlyContactsOnRemote" };
        foreach (var path in localPointerPaths)
        {
            var binding = new EditorCurveBinding
            {
                path = path,
                type = typeof(GameObject),
                propertyName = "m_IsActive",
            };
            AnimationUtility.SetEditorCurve(remoteClip, binding, AnimationCurve.Linear(0f, 0f, 1f / 60, 0f));
        }
        foreach (var path in localTriggerPaths)
        {
            var binding = new EditorCurveBinding
            {
                path = path,
                type = typeof(GameObject),
                propertyName = "m_IsActive",
            };
            AnimationUtility.SetEditorCurve(remoteClip, binding, AnimationCurve.Linear(0f, 0f, 1f / 60, 0f));
        }
        var remoteState = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Remote",
            writeDefaultValues = true,
            motion = remoteClip,
        };
        var localState = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Local",
            writeDefaultValues = true,
            motion = emptyClip,
        };
        var idleState = new AnimatorState
        {
            hideFlags = HideFlags.HideInHierarchy,
            name = "Idle",
            writeDefaultValues = true,
            motion = emptyClip,
            transitions = new AnimatorStateTransition[]
            {
                new AnimatorStateTransition
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    hasExitTime = false,
                    hasFixedDuration = true,
                    exitTime = 0f,
                    duration = 0f,
                    offset = 0f,
                    destinationState = localState,
                    conditions = new AnimatorCondition[] { localCondition },
                },
                new AnimatorStateTransition
                {
                    hideFlags = HideFlags.HideInHierarchy,
                    hasExitTime = false,
                    hasFixedDuration = true,
                    exitTime = 0f,
                    duration = 0f,
                    offset = 0f,
                    destinationState = remoteState,
                    conditions = new AnimatorCondition[] { remoteCondition },
                },
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_LocalOnlyContacts");
        AddGeneratedLayer(new AnimatorControllerLayer
        {
            name = layerName,
            avatarMask = emptyMask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 1f,
            stateMachine = new AnimatorStateMachine
            {
                hideFlags = HideFlags.HideInHierarchy,
                name = layerName,
                entryPosition = new Vector3(0, -100),
                anyStatePosition = new Vector3(0, -300),
                exitPosition = new Vector3(0, 200),
                defaultState = idleState,
                states = new ChildAnimatorState[]
                {
                    new ChildAnimatorState { state = idleState, position = new Vector3(0, 0) },
                    new ChildAnimatorState { state = localState, position = new Vector3(300, 0) },
                    new ChildAnimatorState { state = remoteState, position = new Vector3(-300, 0) },
                },
            },
        });
    }

    AnimationClip _emptyClip;
    AnimationClip emptyClip
    {
        get
        {
            if (_emptyClip == null)
            {
                _emptyClip = new AnimationClip
                {
                    name = "VRC3CVR_Empty",
                };
            }
            return _emptyClip;
        }
    }

    static GameObject SuitableContactObjectWithCollider(GameObject targetGameObject, VRC.Dynamics.ContactBase contact) =>
        SuitableContactObjectWithCollider(targetGameObject, contact is VRCContactSender, contact.shapeType, contact.radius, contact.position, contact.height, contact.rotation);

    static GameObject SuitableContactObjectWithCollider(GameObject targetGameObject, bool isSender, VRC.Dynamics.ContactBase.ShapeType shapeType, float radius, Vector3 position, float height, Quaternion rotation)
    {
        var name = isSender ? nameof(VRCContactSender) : nameof(VRCContactReceiver);
        if (shapeType == VRC.Dynamics.ContactBase.ShapeType.Sphere)
        {
            var contactGameObject = new GameObject(name);
            contactGameObject.transform.SetParent(targetGameObject.transform, false);
            contactGameObject.transform.localPosition = position;
            contactGameObject.transform.localRotation = Quaternion.identity;
            contactGameObject.transform.localScale = Vector3.one;
            var collider = contactGameObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = radius;
            collider.center = Vector3.zero;
            return contactGameObject;
        }
        else
        {
            var contactGameObject = new GameObject(name);
            contactGameObject.transform.SetParent(targetGameObject.transform, false);
            contactGameObject.transform.localPosition = position;
            contactGameObject.transform.localRotation = rotation;
            contactGameObject.transform.localScale = Vector3.one;
            var collider = contactGameObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.radius = radius;
            collider.height = height;
            collider.center = Vector3.zero;
            collider.direction = 1; // Y
            return contactGameObject;
        }
    }

    static string ConstantContactProxiedParameterName(string parameterName)
    {
        return $"{parameterName}_CVRAdvancedAvatarSettingsTrigger_Proxy";
    }

    string ChilloutAvatarRelativePath(Component child) => ChilloutAvatarRelativePath(child.transform);
    string ChilloutAvatarRelativePath(GameObject child) => ChilloutAvatarRelativePath(child.transform);
    string ChilloutAvatarRelativePath(Transform child) => RelativePath(chilloutAvatar.transform, child);

    static string RelativePath(Transform parent, Transform child)
    {
        string path = child.name;
        while (child.parent != null && child.parent != parent)
        {
            child = child.parent;
            path = child.name + "/" + path;
        }
        return path;
    }
}
#endif
