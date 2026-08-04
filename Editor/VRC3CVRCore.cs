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
            // After AdjustParameterNames, like the feed layers above, so the names are final.
            RemapVelocityToAvatarLocal();
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
    };

    HashSet<string> preserveParameters;

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
            if (binding.type == typeof(Animator))
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

            MergeVrcAnimatorIntoChilloutAnimator(vrcAnimatorControllers[i], baseAnimatorID);
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
        { "proxy_idle_2", "LocIdle.anim" },
        { "proxy_idle_3", "LocIdle.anim" },
        { "proxy_run_forward", "LocRunningForward.anim" },
        { "proxy_run_backward", "LocRunningBackward.anim" },
    };

    Motion ReplaceProxyAnimationClip(Motion clip)
    {
        if (!clip) return clip;

        var handClipMap = BuildProxyHandClipMap();
        if (handClipMap.TryGetValue(clip.name, out var getClip))
        {
            var replacement = getClip();
            return replacement ? replacement : clip;
        }

        if (proxyLocomotionClipMap.TryGetValue(clip.name, out var locomotionFile))
        {
            return (AnimationClip)AssetDatabase.LoadAssetAtPath($"{LocomotionAnimationPath}/{locomotionFile}", typeof(AnimationClip));
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

            if (state.motion is BlendTree)
            {
                BlendTree blendTree = (BlendTree)state.motion;

                if (gestureWeightConversionMode == GestureWeightConversionMode.FoldToGestureLeft)
                {
                    FoldGestureWeightOnBlendTree(blendTree);
                }

                ChildMotion[] blendTreeMotions = blendTree.children;

                for (int i = 0; i < blendTreeMotions.Count(); i++)
                {
                    if (blendTreeMotions[i].motion is AnimationClip)
                    {
                        blendTreeMotions[i].motion = ReplaceProxyAnimationClip(blendTreeMotions[i].motion);
                    }
                }

                blendTree.children = blendTreeMotions;
            }
            else if (state.motion is AnimationClip)
            {
                state.motion = ReplaceProxyAnimationClip(state.motion);
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
                else if (behaviour is VRCAnimatorTrackingControl && convertVRCAnimatorTrackingControl)
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
        foreach (var layer in controller.layers)
        {
            foreach (var state in AllStatesOf(layer.stateMachine))
            {
                if (MotionHasAuthoredClip(state.motion))
                {
                    return true;
                }
            }
        }
        return false;
    }

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
                state.motion = SubstitutedMotion(state.motion);
            }
        }
    }

    Motion SubstitutedMotion(Motion motion)
    {
        if (motion is AnimationClip clip)
        {
            return SubstitutedClip(clip) ?? (Motion)clip;
        }
        if (motion is BlendTree tree)
        {
            var children = tree.children;
            var changed = false;
            for (var i = 0; i < children.Length; i++)
            {
                var substituted = SubstitutedMotion(children[i].motion);
                if (substituted != children[i].motion)
                {
                    children[i].motion = substituted;
                    changed = true;
                }
            }
            if (changed)
            {
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
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

    static IEnumerable<AnimatorState> AllStatesOf(AnimatorStateMachine machine)
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
            foreach (var child in current.states)
            {
                if (child.state != null)
                {
                    yield return child.state;
                }
            }
            foreach (var sub in current.stateMachines)
            {
                stack.Push(sub.stateMachine);
            }
        }
    }

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
                ProcessStateMachine(layer.stateMachine, layer.name, ref parameters);
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

        string[] allowedLayerNames;

        if (convertGestureLayer && vrcAvatarDescriptor.baseAnimationLayers[(int)VRCBaseAnimatorID.GESTURE].animatorController)
        {
            Debug.Log("Deleting CVR hand layers...");
            allowedLayerNames = new string[] { "Locomotion/Emotes" };
        }
        else
        {
            Debug.Log("Not deleting CVR hand layers...");
            allowedLayerNames = new string[] { "Locomotion/Emotes", "LeftHand", "RightHand" };
        }

        foreach (AnimatorControllerLayer layer in existingLayers)
        {
            if (Array.IndexOf(allowedLayerNames, layer.name) != -1)
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

    // VRChat built-ins that ChilloutVR does not supply to the animator. The client's parameter
    // stream provides equivalent sources; each stream type's semantics were verified against the
    // decompiled client (DeviceMode: isUsingVr ? 1 : 0, matching VRMode).
    static readonly (string parameterName, CVRParameterStreamEntry.Type streamType)[] GameStateParameterStreams =
    {
        ("MuteSelf", CVRParameterStreamEntry.Type.LocalPlayerMuted),
        ("VRMode", CVRParameterStreamEntry.Type.DeviceMode),
        ("Upright", CVRParameterStreamEntry.Type.AvatarUpright),
    };

    // Feed MuteSelf/VRMode/Upright on the wearer's client via CVRParameterStream; the parameters
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
            if (!parameters.Any(p => p.name == parameterName))
            {
                // the avatar does not use this parameter
                continue;
            }
            streamedEntries.Add(new CVRParameterStreamEntry
            {
                type = streamType,
                targetType = CVRParameterStreamEntry.TargetType.AvatarAnimator,
                applicationType = CVRParameterStreamEntry.ApplicationType.Override,
                parameterName = parameterName,
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
