using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRCExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
using VRC.SDK3.Avatars.Components;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using VRC.SDK3.Dynamics.Contact.Components;

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
    HashSet<string> constantContactProxiedParameters;
    HashSet<string> localTriggerPaths;
    HashSet<string> localPointerPaths;
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

    // This mask will mask all other layer masks from the gesture animator, and is derived from the
    // *first* layer.
    AvatarMask gestureMask;

    AvatarMask emptyMask;
    AvatarMask fullMask;
    AvatarMask musclesOnlyMask;

    // Hands combined from both ChilloutVR animationClips
    AnimationClip handCombinedFistAnimationClip;
    AnimationClip handCombinedGunAnimationClip;
    AnimationClip handCombinedOpenAnimationClip;
    AnimationClip handCombinedPeaceAnimationClip;
    AnimationClip handCombinedPointAnimationClip;
    AnimationClip handCombinedRelaxedAnimationClip;
    AnimationClip handCombinedRockNRollAnimationClip;
    AnimationClip handCombinedThumbsUpAnimationClip;

    bool GetAreToeBonesSet()
    {
        return true;
    }

    public bool GetIsReadyForConvert()
    {
        return vrcAvatarDescriptor != null;
    }

    void SetAnimator()
    {
        // this is not necessary for VRC or CVR but it helps people test their controller
        // and lets us query for Toe bones for our GUI
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
        }

        _emptyClip = null;

        isConverting = true;

        Debug.Log("Starting to convert...");

        AssetDatabase.Refresh();

        // Generate Combined hand animations
        CreateCombinedHandAnimations();

        // Clear the cache
        avatarMaskCombineCache = new Dictionary<(AvatarMask, AvatarMask), AvatarMask>();
        gestureMask = null;

        // Load hardcoded masks
        emptyMask = (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrEmptyMask.mask", typeof(AvatarMask));
        fullMask = (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrFullMask.mask", typeof(AvatarMask));
        musclesOnlyMask = (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrMusclesOnly.mask", typeof(AvatarMask));

        CreateChilloutAvatar();
        GetValuesFromVrcAvatar();
        CreateChilloutComponentIfNeeded();
        PopulateChilloutComponent();
        CreateEmptyChilloutAnimator();
        MergeVrcAnimatorsIntoChilloutAnimator();
        if (convertVRCContactSendersAndReceivers)
        {
            ConvertContactsToCVRComponents();
            RemapAnimationOfContactComponent();
            MakeProxyLayersOfConstantContactParameters();
            EnsureLocalOnlyContacts();
        }
        if (createVRCContactEquivalentPointers)
        {
            CreateVRCContactEquivalentPointers();
        }
        SetAnimator();
        ConvertVrcParametersToChillout();
        if (preserveParameterSyncState || addActionMenuModAnnotations)
        {
            AdjustParameterNames();
        }
        FixChilloutAnimatorForPreview();
        InsertChilloutOverride();

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

        Debug.Log("Conversion complete!");

        isConverting = false;
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

    void BuildChilloutAnimatorWithParams()
    {
        Debug.Log("Building chillout animator with params...");


        Debug.Log("Settings" + cvrAvatar.avatarSettings);

        foreach (UnityEditor.Editor go in Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor)))
        {
            // This method is private in CCK
            MethodInfo privateMethod = go.GetType().GetMethod("CreateAnimator", BindingFlags.NonPublic | BindingFlags.Instance);

            if (privateMethod != null)
            {
                MethodInfo onInspectorGUIMethod = go.GetType().GetMethod("OnInspectorGUI");
                onInspectorGUIMethod.Invoke(go, new object[] { });

                privateMethod.Invoke(go, new object[] { });
            }
        }

        cvrAvatar.overrides = cvrAvatar.avatarSettings.overrides;

        Debug.Log("Chillout animator with params built");
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
                    if (!idTable.ContainsKey(control.value))
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
                        if (intIdTable.Count == 1 && intIdTable.First().Key == 1)
                        {
                            var menuNameAndType = intIdTable.First().Value;
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
                            var lastIndex = (int)intIdTable.Last().Key;
                            var menuEntryNames = new List<string>();
                            for (var j = 0; j < lastIndex + 1; j++)
                            {
                                menuEntryNames.Add(intIdTable.TryGetValue(j, out var menuEntry) ? menuEntry.name : "---");
                            }
                            var menuName = GetMenuNameCommonParent(menuEntryNames.Where(name => name != "---"));
                            menuEntryNames = menuEntryNames.Select(name =>
                            {
                                if (name == "---") return "---";
                                if (useHierarchicalDropdownMenuName) return name.Substring(menuName.Length + 1);
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
                            if (intIdTable.Values.All(v => v.type == VRCExpressionsMenu.Control.ControlType.Button))
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
        "Toggle",
        "Sitting",
        "Crouching",
        "Prone",
        "Flying",
        "IsLocal",
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
            preserveParameters = vrcAvatarDescriptor.expressionParameters.parameters?.Where(p => p.networkSynced).Select(p => p.name).ToHashSet() ?? new HashSet<string>();
            preserveParameters.UnionWith(PreDefinedParameterNames);
            preserveParameters.UnionWith(muscleNames);
        }
        else
        {
            // all
            preserveParameters = vrcAvatarDescriptor.expressionParameters.parameters?.Select(p => p.name).ToHashSet() ?? new HashSet<string>();
            preserveParameters.UnionWith(chilloutAnimatorController.parameters.Select(p => p.name));
            preserveParameters.UnionWith(muscleNames);
        }
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
        chilloutAnimatorController.parameters = parameters;

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
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    RenameParameterType GetRenameParameterType(string name)
    {
        var type = RenameParameterType.None;
        if (!string.IsNullOrEmpty(name))
        {
            if (!preserveParameters.Contains(name))
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

    void RenameParameterNameIfNeeded(ref string name)
    {
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
                return;
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

    AnimatorTransition[] ProcessTransitions(AnimatorTransition[] transitions)
    {
        return ProcessTransitions<AnimatorTransition>(transitions);
    }

    AnimatorStateTransition[] ProcessTransitions(AnimatorStateTransition[] transitions)
    {
        return ProcessTransitions<AnimatorStateTransition>(transitions);
    }

    AnimatorTranstitionType[] ProcessTransitions<AnimatorTranstitionType>(AnimatorTranstitionType[] transitions) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        List<AnimatorTranstitionType> transitionsToAdd = new List<AnimatorTranstitionType>();

        for (int t = 0; t < transitions.Length; t++)
        {
            List<AnimatorCondition> conditionsToAdd = new List<AnimatorCondition>();
            AnimatorTranstitionType transition = transitions[t];

            // Debug.Log(transitions[t].conditions.Length + " conditions");

            ProcessTransition(transition, transitionsToAdd, conditionsToAdd);
        }

        AnimatorTranstitionType[] newTransitions = new AnimatorTranstitionType[transitions.Length + transitionsToAdd.Count];

        transitions.CopyTo(newTransitions, 0);
        transitionsToAdd.ToArray().CopyTo(newTransitions, transitions.Length);

        return newTransitions;
    }

    void ProcessTransition<AnimatorTranstitionType>(AnimatorTranstitionType transition, List<AnimatorTranstitionType> transitionsToAdd, List<AnimatorCondition> conditionsToAdd, bool isDuplicate = false) where AnimatorTranstitionType : AnimatorTransitionBase, new()
    {
        // Convert GestureLeft/GestureRight to ChilloutVR
        for (int c = 0; c < transition.conditions.Length; c++)
        {
            AnimatorCondition condition = transition.conditions[c];

            if (condition.parameter == "GestureLeft" || condition.parameter == "GestureRight")
            {
                float chilloutGestureNumber = GetChilloutGestureNumberForVrchatGestureNumber(condition.threshold);

                if (condition.mode == AnimatorConditionMode.Equals)
                {
                    float thresholdLow = (float)(chilloutGestureNumber - 0.1);
                    float thresholdHigh = (float)(chilloutGestureNumber + 0.1);

                    // Look for GestureWeight and adjust threshold
                    if (chilloutGestureNumber == 1f) // Fist only
                    {
                        thresholdLow = 0.01f;

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
                    else if (chilloutGestureNumber == 0f)
                    {
                        thresholdHigh = 0.01f;

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

                        ProcessTransition(newTransition, transitionsToAdd2, conditionsToAdd2, true);
                        newTransition.conditions = conditionsToAdd2.ToArray();

                        transitionsToAdd.Add(newTransition);
                    }
                }
            }
            else if (condition.parameter == "GestureLeftWeight" || condition.parameter == "GestureRightWeight")
            {
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

                // Create condition if gesture weight is used by itself
                if (!gestureFound)
                {
                    float thresholdLow = -0.1f;
                    float thresholdHigh = 1.1f;

                    if (condition.mode == AnimatorConditionMode.Less)
                    {
                        thresholdHigh = condition.threshold;
                    }
                    else
                    {
                        thresholdLow = condition.threshold;
                    }

                    string parameterName = condition.parameter == "GestureLeftWeight" ? "GestureLeft" : "GestureRight";

                    // Create replace conditions for ChilloutVR
                    AnimatorCondition newConditionLessThan = new AnimatorCondition();
                    newConditionLessThan.parameter = parameterName;
                    newConditionLessThan.mode = AnimatorConditionMode.Less;
                    newConditionLessThan.threshold = thresholdHigh;

                    conditionsToAdd.Add(newConditionLessThan);

                    AnimatorCondition newConditionGreaterThan = new AnimatorCondition();
                    newConditionGreaterThan.parameter = parameterName;
                    newConditionGreaterThan.mode = AnimatorConditionMode.Greater;
                    newConditionGreaterThan.threshold = thresholdLow;

                    conditionsToAdd.Add(newConditionGreaterThan);
                }
            }
            else
            {
                conditionsToAdd.Add(condition);
            }
        }

        transition.conditions = conditionsToAdd.ToArray();
    }

    Motion ReplaceProxyAnimationClip(Motion clip)
    {
        if (clip)
        {
            switch (clip.name)
            {
                case "proxy_hands_fist":
                    if (handCombinedFistAnimationClip)
                    {
                        return handCombinedFistAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_gun":
                    if (handCombinedGunAnimationClip)
                    {
                        return handCombinedGunAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_idle":
                    if (handCombinedRelaxedAnimationClip)
                    {
                        return handCombinedRelaxedAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_idle2":
                    if (handCombinedRelaxedAnimationClip)
                    {
                        return handCombinedRelaxedAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_open":
                    if (handCombinedOpenAnimationClip)
                    {
                        return handCombinedOpenAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_peace":
                    if (handCombinedPeaceAnimationClip)
                    {
                        return handCombinedPeaceAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_point":
                    if (handCombinedPointAnimationClip)
                    {
                        return handCombinedPointAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_rock":
                    if (handCombinedRockNRollAnimationClip)
                    {
                        return handCombinedRockNRollAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_hands_thumbs_up":
                    if (handCombinedThumbsUpAnimationClip)
                    {
                        return handCombinedThumbsUpAnimationClip;
                    }
                    else
                    {
                        return clip;
                    }
                case "proxy_stand_still":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocIdle.anim", typeof(AnimationClip));
                case "proxy_idle":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocIdle.anim", typeof(AnimationClip));
                case "proxy_idle_2":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocIdle.anim", typeof(AnimationClip));
                case "proxy_idle_3":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocIdle.anim", typeof(AnimationClip));
                case "proxy_run_forward":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocRunningForward.anim", typeof(AnimationClip));
                case "proxy_run_backward":
                    return (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/LocRunningBackward.anim", typeof(AnimationClip));
                default:
                    return clip;
            }
        }
        else
        {
            return clip;
        }
    }

    void ProcessStateMachine(AnimatorStateMachine stateMachine, ref AnimatorControllerParameter[] parameters)
    {
        for (int s = 0; s < stateMachine.states.Length; s++)
        {
            // Debug.Log(stateMachine.states[s].state.transitions.Length + " transitions");

            AnimatorState state = stateMachine.states[s].state;

            // assuming they only ever check weight for the Fist animation
            if (state.timeParameter == "GestureLeftWeight")
            {
                state.timeParameter = "GestureLeft";
            }
            else if (state.timeParameter == "GestureRightWeight")
            {
                state.timeParameter = "GestureRight";
            }

            state.timeParameter = state.timeParameter;

            if (state.motion is BlendTree)
            {
                BlendTree blendTree = (BlendTree)state.motion;

                // X
                if (blendTree.blendParameter == "GestureLeftWeight")
                {
                    blendTree.blendParameter = "GestureLeft";
                }
                else if (blendTree.blendParameter == "GestureRightWeight")
                {
                    blendTree.blendParameter = "GestureRight";
                }
                // Y
                if (blendTree.blendParameterY == "GestureLeftWeight")
                {
                    blendTree.blendParameterY = "GestureLeft";
                }
                else if (blendTree.blendParameterY == "GestureRightWeight")
                {
                    blendTree.blendParameterY = "GestureRight";
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
                                    op = AnimatorDriverTask.Operator.LessThen,
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
                                    bValue = (vrcParameter.destMax - vrcParameter.destMin) / (vrcParameter.sourceMax - vrcParameter.sourceMin),
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
                else if (behaviour is VRCAnimatorLocomotionControl)
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
                else if (behaviour is VRCAnimatorTrackingControl)
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

            AnimatorStateTransition[] newTransitions = ProcessTransitions(state.transitions);
            state.transitions = newTransitions;
        }

        stateMachine.anyStateTransitions = ProcessTransitions(stateMachine.anyStateTransitions);
        stateMachine.entryTransitions = ProcessTransitions(stateMachine.entryTransitions);

        if (stateMachine.stateMachines.Length > 0)
        {
            // Debug.Log("Found " + stateMachine.stateMachines.Length + " child state machines");
        }

        foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
        {
            ProcessStateMachine(childStateMachine.stateMachine, ref parameters);
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
                    return (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrHandLeft.mask", typeof(AvatarMask));
                case "vrc_Hand Right":
                    return (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrHandRight.mask", typeof(AvatarMask));
                case "vrc_HandsOnly":
                    return (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrHandsOnly.mask", typeof(AvatarMask));
                case "vrc_MusclesOnly":
                    return (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/PeanutTools/vrc3cvr/Editor/vrc3cvrMusclesOnly.mask", typeof(AvatarMask));
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

    void CreateCombinedHandAnimations()
    {
        AnimationClip handLeftGunAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftGun.anim", typeof(AnimationClip));
        AnimationClip handRightGunAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightGun.anim", typeof(AnimationClip));
        if (handLeftGunAnimationClip && handRightGunAnimationClip)
        {
            handCombinedGunAnimationClip = CombineAnimationClips(handLeftGunAnimationClip, handRightGunAnimationClip);
            handCombinedGunAnimationClip.name = "HandCombinedGun";
        }

        AnimationClip handLeftOpenAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftOpen.anim", typeof(AnimationClip));
        AnimationClip handRightOpenAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightOpen.anim", typeof(AnimationClip));
        if (handLeftOpenAnimationClip && handRightOpenAnimationClip)
        {
            handCombinedOpenAnimationClip = CombineAnimationClips(handLeftOpenAnimationClip, handRightOpenAnimationClip);
            handCombinedOpenAnimationClip.name = "HandCombinedOpen";
        }

        AnimationClip handLeftPeaceAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftPeace.anim", typeof(AnimationClip));
        AnimationClip handRightPeaceAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightPeace.anim", typeof(AnimationClip));
        if (handLeftPeaceAnimationClip && handRightPeaceAnimationClip)
        {
            handCombinedPeaceAnimationClip = CombineAnimationClips(handLeftPeaceAnimationClip, handRightPeaceAnimationClip);
            handCombinedPeaceAnimationClip.name = "HandCombinedPeace";
        }

        AnimationClip handLeftPointAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftPoint.anim", typeof(AnimationClip));
        AnimationClip handRightPointAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightPoint.anim", typeof(AnimationClip));
        if (handLeftPointAnimationClip && handRightPointAnimationClip)
        {
            handCombinedPointAnimationClip = CombineAnimationClips(handLeftPointAnimationClip, handRightPointAnimationClip);
            handCombinedPointAnimationClip.name = "HandCombinedPoint";
        }

        AnimationClip handLeftRockNRollAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftRockNRoll.anim", typeof(AnimationClip));
        AnimationClip handRightRockNRollAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightRockNRoll.anim", typeof(AnimationClip));
        if (handLeftRockNRollAnimationClip && handRightRockNRollAnimationClip)
        {
            handCombinedRockNRollAnimationClip = CombineAnimationClips(handLeftRockNRollAnimationClip, handRightRockNRollAnimationClip);
            handCombinedRockNRollAnimationClip.name = "HandCombinedRockNRoll";
        }

        AnimationClip handLeftThumbsUpAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftThumbsUp.anim", typeof(AnimationClip));
        AnimationClip handRightThumbsUpAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightThumbsUp.anim", typeof(AnimationClip));
        if (handLeftThumbsUpAnimationClip && handRightThumbsUpAnimationClip)
        {
            handCombinedThumbsUpAnimationClip = CombineAnimationClips(handLeftThumbsUpAnimationClip, handRightThumbsUpAnimationClip);
            handCombinedThumbsUpAnimationClip.name = "HandCombinedThumbsUp";
        }

        //
        AnimationClip handLeftRelaxedAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftRelaxed.anim", typeof(AnimationClip));
        AnimationClip handRightRelaxedAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightRelaxed.anim", typeof(AnimationClip));
        if (handLeftRelaxedAnimationClip && handRightRelaxedAnimationClip)
        {
            handCombinedRelaxedAnimationClip = CombineAnimationClips(handLeftRelaxedAnimationClip, handRightRelaxedAnimationClip);
            handCombinedRelaxedAnimationClip.name = "HandCombinedRelaxed";
        }

        AnimationClip handLeftFistAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandLeftFist.anim", typeof(AnimationClip));
        AnimationClip handRightFistAnimationClip = (AnimationClip)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/HandRightFist.anim", typeof(AnimationClip));
        if (handLeftFistAnimationClip && handRightFistAnimationClip)
        {
            handCombinedFistAnimationClip = CombineAnimationClips(handLeftFistAnimationClip, handRightFistAnimationClip);
            handCombinedFistAnimationClip.name = "HandCombinedFist";
        }

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
                return GetCombinedAvatarMask(ReplaceVRCMask(fullMask), ReplaceVRCMask(originalMask));
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

        for (int i = 0; i < newAnimatorController.layers.Length; i++)
        {
            AnimatorControllerLayer layer = newAnimatorController.layers[i];

            if (layer.stateMachine.states.Length > 0)
            { // Do not copy empty layers
                Debug.Log("Layer \"" + layer.name + "\" with " + layer.stateMachine.states.Length + " states");

                var parameters = newAnimatorController.parameters;
                ProcessStateMachine(layer.stateMachine, ref parameters);
                newAnimatorController.parameters = parameters;

                layer.avatarMask = GetAvatarMaskForLayerAndVRCAnimator(animatorID, i, layer.avatarMask);
                var layers = newAnimatorController.layers;
                layers[i] = layer;
                newAnimatorController.layers = layers;
            }
        }

        new CopyAnimatorController(newAnimatorController).CopyControllerTo(chilloutAnimatorController);

        Debug.Log("Merged");
    }

    void FixChilloutAnimatorForPreview()
    {
        if (chilloutAnimatorController.parameters.Any(p => p.name == "Grounded" && p.defaultBool == false))
        {
            var parameters = chilloutAnimatorController.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == "Grounded")
                {
                    parameters[i] = new AnimatorControllerParameter
                    {
                        name = "Grounded",
                        type = AnimatorControllerParameterType.Bool,
                        defaultBool = true,
                    };
                    break;
                }
            }
            chilloutAnimatorController.parameters = parameters;
        }
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

    void CreateEmptyChilloutAnimator()
    {
        var sourceAnimator = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/ABI.CCK/Animations/AvatarAnimator.controller");
        chilloutAnimatorController = new CopyAnimatorController(sourceAnimator).CopyController();
        chilloutAnimatorController.name = cvrAvatar.gameObject.name + "_ChilloutVR_Gestures";

        Debug.Log("Loading animator...");

        if (chilloutAnimatorController == null)
        {
            throw new Exception("Failed to load the created animator!");
        }

        Debug.Log("Found number of layers: " + chilloutAnimatorController.layers.Length);

        if (chilloutAnimatorController.layers.Length != 4)
        {
            throw new Exception("Animator controller has unexpected number of layers: " + chilloutAnimatorController.layers.Length);
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

        foreach (AnimatorControllerLayer layer in chilloutAnimatorController.layers)
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

        if (vrcViewPosition == null)
        {
            throw new Exception("Could not find view position from VRC component!");
        }

        Debug.Log("View position: " + vrcViewPosition);

        vrcVisemeBlendShapes = vrcAvatarDescriptor.VisemeBlendShapes;

        if (vrcViewPosition == null)
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

        if (eyelidsBlendshapes.Length >= 1 && eyelidsBlendshapes[0] != -1)
        {
            if (bodySkinnedMeshRenderer != null)
            {
                int blinkBlendshapeIdx = eyelidsBlendshapes[0];
                Mesh mesh = bodySkinnedMeshRenderer.sharedMesh;

                if (blinkBlendshapeIdx > mesh.blendShapeCount)
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

        cvrAvatar.useVisemeLipsync = true;

        for (int i = 0; i < vrcVisemeBlendShapes.Length; i++)
        {
            cvrAvatar.visemeBlendshapes[i] = vrcVisemeBlendShapes[i];
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
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_head, "Head");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_torso, "Torso");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_handL, "Hand", "HandL");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_handR, "Hand", "HandR");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_footL, "Foot", "FootL");
        AddContactEquivalentPointer(false, vrcAvatarDescriptor.collider_footR, "Foot", "FootR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerIndexL, "Finger", "FingerL", "FingerIndex", "FingerIndexL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerIndexR, "Finger", "FingerR", "FingerIndex", "FingerIndexR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerMiddleL, "Finger", "FingerL", "FingerMiddle", "FingerMiddleL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerMiddleR, "Finger", "FingerR", "FingerMiddle", "FingerMiddleR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerRingL, "Finger", "FingerL", "FingerRing", "FingerRingL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerRingR, "Finger", "FingerR", "FingerRing", "FingerRingR");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerLittleL, "Finger", "FingerL", "FingerLittle", "FingerLittleL");
        AddContactEquivalentPointer(true, vrcAvatarDescriptor.collider_fingerLittleR, "Finger", "FingerR", "FingerLittle", "FingerLittleR");
    }

    void AddContactEquivalentPointer(bool forceSphere, VRCAvatarDescriptor.ColliderConfig config,  params string[] collisionTags)
    {
        if (config.state == VRCAvatarDescriptor.ColliderConfig.State.Disabled)
        {
            return;
        }
        var transform = cvrAvatar.transform.Find(RelativePath(vrcAvatarDescriptor.transform, config.transform));
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
        localPointerPaths = new HashSet<string>();
        localTriggerPaths = new HashSet<string>();
        foreach (var sender in senders)
        {
            if (sender.collisionTags.Count == 0)
            {
                continue;
            }
            var collisionTagToCVRType = MakeCollisionTagToCVRType(sender.gameObject);
            var originalPath = ChilloutAvatarRelativePath(sender);
            var remappedPaths = new List<string>();
            var collisionTags = sender.collisionTags.SelectMany(collisionTagToCVRType).Distinct().ToArray(); ;
            if (collisionTags.Length == 1)
            {
                var contactGameObject = SuitableContactObjectWithCollider(sender.gameObject, sender);
                var cvrPointer = contactGameObject.AddComponent<CVRPointer>();
                cvrPointer.type = collisionTags.FirstOrDefault();
                remappedPaths.Add(ChilloutAvatarRelativePath(contactGameObject));
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
            if (originalPath != remappedPath)
            {
                contactComponentPathRemap[originalPath] = new[] { remappedPath };
            }
            UnityEngine.Object.DestroyImmediate(receiver);
        }
    }

    Func<string, string[]> MakeCollisionTagToCVRType(GameObject gameObject)
    {
        var configs = FindConfigsInParent(gameObject.transform);
        var config = VRC3CVRCollisionTagConvertionConfig.WithInherits(configs.Reverse());
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
            chilloutAnimatorController.AddLayer(layer);
        }
        chilloutAnimatorController.parameters = parameters;
    }

    void EnsureLocalOnlyContacts()
    {
        if (localPointerPaths.Count == 0 && localTriggerPaths.Count == 0)
        {
            return;
        }
        if (!chilloutAnimatorController.parameters.Any(p => p.name == "IsLocal"))
        {
            var parameters = chilloutAnimatorController.parameters;
            ArrayUtility.Add(ref parameters, new AnimatorControllerParameter
            {
                name = "IsLocal",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
            });
            chilloutAnimatorController.parameters = parameters;
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
                    conditions = new AnimatorCondition[]
                    {
                        new AnimatorCondition
                        {
                            mode = AnimatorConditionMode.If,
                            parameter = "IsLocal",
                            threshold = 1f,
                        },
                    },
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
                    conditions = new AnimatorCondition[]
                    {
                        new AnimatorCondition
                        {
                            mode = AnimatorConditionMode.IfNot,
                            parameter = "IsLocal",
                            threshold = 1f,
                        },
                    },
                },
            },
        };
        var layerName = chilloutAnimatorController.MakeUniqueLayerName("VRC3CVR_LocalOnlyContacts");
        chilloutAnimatorController.AddLayer(new AnimatorControllerLayer
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
