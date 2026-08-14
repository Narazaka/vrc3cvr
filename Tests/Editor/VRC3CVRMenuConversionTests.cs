#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

// See VRC3CVRGestureConversionTests for why these live in Assembly-CSharp-Editor and use reflection.
//
// FindMenuButtonsAndToggles returns Dictionary<string, Dictionary<float, MenuNameAndType>> where
// MenuNameAndType is a private nested class, so it is exercised indirectly through its only
// caller, ConvertVrcParametersToChillout, and asserted on via the public-ish CVRAvatar output
// instead of reflecting into the private intermediate structure.
public class VRC3CVRMenuConversionTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    GameObject avatarObject;
    VRCAvatarDescriptor descriptor;
    CVRAvatar cvrAvatar;
    VRC3CVRCore core;

    [SetUp]
    public void SetUp()
    {
        avatarObject = new GameObject("MenuTestAvatar");
        descriptor = avatarObject.AddComponent<VRCAvatarDescriptor>();
        cvrAvatar = avatarObject.AddComponent<CVRAvatar>();
        cvrAvatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>() };

        core = new VRC3CVRCore { vrcAvatarDescriptor = descriptor };
        typeof(VRC3CVRCore).GetField("cvrAvatar", Flags).SetValue(core, cvrAvatar);
        typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).SetValue(core, new AnimatorController { name = "menuTest" });
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(avatarObject);
    }

    // ---- menu/parameter construction helpers ----

    static VRCExpressionParameters.Parameter Param(string name, VRCExpressionParameters.ValueType type, float defaultValue = 0f)
    {
        return new VRCExpressionParameters.Parameter { name = name, valueType = type, defaultValue = defaultValue };
    }

    static VRCExpressionsMenu.Control Toggle(string name, string paramName, float value)
    {
        return new VRCExpressionsMenu.Control
        {
            name = name,
            type = VRCExpressionsMenu.Control.ControlType.Toggle,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName },
            value = value,
        };
    }

    static VRCExpressionsMenu.Control Button(string name, string paramName, float value)
    {
        var control = Toggle(name, paramName, value);
        control.type = VRCExpressionsMenu.Control.ControlType.Button;
        return control;
    }

    static VRCExpressionsMenu.Control RadialPuppet(string name, string subParamName)
    {
        return new VRCExpressionsMenu.Control
        {
            name = name,
            type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
            // no "changing" bool wired up -- see the dedicated Bug_ test below for that combination
            parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = subParamName } },
        };
    }

    static VRCExpressionsMenu.Control SubMenuControl(string name, VRCExpressionsMenu subMenu)
    {
        return new VRCExpressionsMenu.Control
        {
            name = name,
            type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            subMenu = subMenu,
        };
    }

    static VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
    {
        var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        menu.controls = controls.ToList();
        return menu;
    }

    void SetMenu(VRCExpressionsMenu menu)
    {
        descriptor.expressionsMenu = menu;
    }

    void SetParams(params VRCExpressionParameters.Parameter[] parameters)
    {
        var vrcParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        vrcParams.parameters = parameters;
        descriptor.expressionParameters = vrcParams;
    }

    // Unwraps the TargetInvocationException that MethodInfo.Invoke wraps thrown exceptions in, so
    // Assert.Throws<T> can match the real exception type coming out of ConvertVrcParametersToChillout.
    void Convert()
    {
        try
        {
            typeof(VRC3CVRCore).GetMethod("ConvertVrcParametersToChillout", Flags).Invoke(core, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    List<CVRAdvancedSettingsEntry> Settings => cvrAvatar.avatarSettings.settings;

    // ---- MenuName / MenuNameWithoutStack / GetMenuNameCommonParent ----

    [Test]
    public void MenuNameWithoutStack_StripsEverythingUpToLastSlash()
    {
        var result = (string)typeof(VRC3CVRCore).GetMethod("MenuNameWithoutStack", Flags).Invoke(core, new object[] { "A/B/C" });
        Assert.AreEqual("C", result);
    }

    [Test]
    public void MenuNameWithoutStack_NoSlash_ReturnsUnchanged()
    {
        var result = (string)typeof(VRC3CVRCore).GetMethod("MenuNameWithoutStack", Flags).Invoke(core, new object[] { "Leaf" });
        Assert.AreEqual("Leaf", result);
    }

    [Test]
    public void MenuName_HierarchicalTrue_ReturnsFullPath()
    {
        core.useHierarchicalMenuName = true;
        var result = (string)typeof(VRC3CVRCore).GetMethod("MenuName", Flags).Invoke(core, new object[] { "A/B" });
        Assert.AreEqual("A/B", result);
    }

    [Test]
    public void MenuName_HierarchicalFalse_ReturnsLeafOnly()
    {
        core.useHierarchicalMenuName = false;
        var result = (string)typeof(VRC3CVRCore).GetMethod("MenuName", Flags).Invoke(core, new object[] { "A/B" });
        Assert.AreEqual("B", result);
    }

    [Test]
    public void GetMenuNameCommonParent_ReturnsSharedPrefix()
    {
        var result = (string)typeof(VRC3CVRCore).GetMethod("GetMenuNameCommonParent", Flags)
            .Invoke(core, new object[] { new[] { "Outfits/Colors/Red", "Outfits/Colors/Green" } });
        Assert.AreEqual("Outfits/Colors", result);
    }

    [Test]
    public void GetMenuNameCommonParent_DivergingPaths_ReturnsCommonAncestorOnly()
    {
        var result = (string)typeof(VRC3CVRCore).GetMethod("GetMenuNameCommonParent", Flags)
            .Invoke(core, new object[] { new[] { "Outfits/Colors/Red", "Outfits/Sizes/Small" } });
        Assert.AreEqual("Outfits", result);
    }

    [Test]
    public void GetMenuNameCommonParent_NoCommonParent_ReturnsEmptyString()
    {
        var result = (string)typeof(VRC3CVRCore).GetMethod("GetMenuNameCommonParent", Flags)
            .Invoke(core, new object[] { new[] { "Red", "Green" } });
        Assert.AreEqual("", result);
    }

    // ---- Bool parameters ----

    [Test]
    public void Bool_SingleToggleControl_ConvertsToToggleEntry()
    {
        SetMenu(Menu(Toggle("MyToggle", "MyBool", 1f)));
        SetParams(Param("MyBool", VRCExpressionParameters.ValueType.Bool, defaultValue: 1f));

        Convert();

        Assert.AreEqual(1, Settings.Count);
        var entry = Settings[0];
        Assert.AreEqual("MyBool", entry.machineName);
        Assert.AreEqual("MyToggle", entry.name);
        Assert.IsTrue(entry.unlinkNameFromMachineName);
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Toggle, entry.type);
        var toggle = (CVRAdvancesAvatarSettingGameObjectToggle)entry.setting;
        Assert.IsTrue(toggle.defaultValue);
        Assert.AreEqual(CVRAdvancesAvatarSettingBase.ParameterType.Bool, toggle.usedType);
    }

    // ---- Int parameters ----

    [Test]
    public void Int_SingleToggleWithValueOne_ConvertsToToggleEntry()
    {
        // Special case in ConvertVrcParametersToChillout: an Int parameter with exactly one menu
        // entry at value == 1 is treated as a plain on/off toggle instead of a one-option dropdown.
        SetMenu(Menu(Toggle("Enable", "MyInt", 1f)));
        SetParams(Param("MyInt", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));

        Convert();

        Assert.AreEqual(1, Settings.Count);
        var entry = Settings[0];
        Assert.AreEqual("Enable", entry.name);
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Toggle, entry.type);
        var toggle = (CVRAdvancesAvatarSettingGameObjectToggle)entry.setting;
        Assert.IsTrue(toggle.defaultValue);
    }

    [Test]
    public void Int_MultipleTogglesInSubmenu_ConvertsToDropdownWithOrderedOptions()
    {
        var subMenu = Menu(
            Toggle("Red", "Color", 0f),
            Toggle("Green", "Color", 1f),
            Toggle("Blue", "Color", 2f));
        SetMenu(Menu(SubMenuControl("Colors", subMenu)));
        SetParams(Param("Color", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));

        Convert();

        Assert.AreEqual(1, Settings.Count);
        var entry = Settings[0];
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Dropdown, entry.type);
        Assert.AreEqual("Colors", entry.name);
        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)entry.setting;
        Assert.AreEqual(new[] { "Red", "Green", "Blue" }, dropdown.options.Select(o => o.name).ToArray());
        Assert.AreEqual(1, dropdown.defaultValue);
        Assert.AreEqual(CVRAdvancesAvatarSettingBase.ParameterType.Int, dropdown.usedType);
    }

    // ---- Float parameters ----

    [Test]
    public void Float_RadialPuppet_ConvertsToSliderEntry()
    {
        SetMenu(Menu(RadialPuppet("Volume", "VolumeLevel")));
        SetParams(Param("VolumeLevel", VRCExpressionParameters.ValueType.Float, defaultValue: 0.5f));

        Convert();

        Assert.AreEqual(1, Settings.Count);
        var entry = Settings[0];
        Assert.AreEqual("Volume", entry.name);
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Slider, entry.type);
        var slider = (CVRAdvancesAvatarSettingSlider)entry.setting;
        Assert.AreEqual(0.5f, slider.defaultValue);
        Assert.AreEqual(CVRAdvancesAvatarSettingBase.ParameterType.Float, slider.usedType);
    }

    // ---- useHierarchicalMenuName ----

    [Test]
    public void HierarchicalMenuName_True_UsesFullPath()
    {
        var colorsMenu = Menu(Toggle("Red", "IsRed", 1f));
        var outfitsMenu = Menu(SubMenuControl("Colors", colorsMenu));
        SetMenu(Menu(SubMenuControl("Outfits", outfitsMenu)));
        SetParams(Param("IsRed", VRCExpressionParameters.ValueType.Bool));
        core.useHierarchicalMenuName = true;

        Convert();

        Assert.AreEqual("Outfits/Colors/Red", Settings[0].name);
    }

    [Test]
    public void HierarchicalMenuName_False_UsesLeafNameOnly()
    {
        var colorsMenu = Menu(Toggle("Red", "IsRed", 1f));
        var outfitsMenu = Menu(SubMenuControl("Colors", colorsMenu));
        SetMenu(Menu(SubMenuControl("Outfits", outfitsMenu)));
        SetParams(Param("IsRed", VRCExpressionParameters.ValueType.Bool));
        core.useHierarchicalMenuName = false;

        Convert();

        Assert.AreEqual("Red", Settings[0].name);
    }

    // ---- adjustToVrcMenuOrder ----

    [Test]
    public void AdjustToVrcMenuOrder_True_OrdersSettingsByMenuAppearance()
    {
        // The menu presents "Beta" before "Alpha", but the VRCExpressionParameters array lists
        // Alpha first.
        SetMenu(Menu(
            Toggle("Beta", "Beta", 1f),
            Toggle("Alpha", "Alpha", 1f)));
        SetParams(
            Param("Alpha", VRCExpressionParameters.ValueType.Bool),
            Param("Beta", VRCExpressionParameters.ValueType.Bool));
        core.adjustToVrcMenuOrder = true;

        Convert();

        Assert.AreEqual(new[] { "Beta", "Alpha" }, Settings.Select(s => s.machineName).ToArray());
    }

    [Test]
    public void AdjustToVrcMenuOrder_False_KeepsVrcParameterArrayOrder()
    {
        SetMenu(Menu(
            Toggle("Beta", "Beta", 1f),
            Toggle("Alpha", "Alpha", 1f)));
        SetParams(
            Param("Alpha", VRCExpressionParameters.ValueType.Bool),
            Param("Beta", VRCExpressionParameters.ValueType.Bool));
        core.adjustToVrcMenuOrder = false;

        Convert();

        Assert.AreEqual(new[] { "Alpha", "Beta" }, Settings.Select(s => s.machineName).ToArray());
    }

    // ---- Bugs: boundary / exceptional cases ----

    [Test]
    public void ToggleValueOneThenPuppetChangingSameParameter_KeepsExistingToggleEntry()
    {
        // A puppet's "changing" indicator (control.parameter) is a boolean that VRChat sets to true
        // (key 1) while the puppet is being manipulated. Here that indicator shares its parameter
        // ("Shared") with an earlier Toggle control that was already registered at value 1 ("Enable").
        // The existing key-1 entry describes the parameter more usefully than a generic "Spin
        // Changing" placeholder would, so it must win: the puppet's changing indicator is a no-op for
        // an already-registered key, not a crash and not a silent overwrite.
        var puppet = new VRCExpressionsMenu.Control
        {
            name = "Spin",
            type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = "Shared" },
            value = 0f,
            subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "SpinAmount" } },
        };
        SetMenu(Menu(
            Toggle("Enable", "Shared", 1f),
            puppet));
        SetParams(
            Param("Shared", VRCExpressionParameters.ValueType.Bool),
            Param("SpinAmount", VRCExpressionParameters.ValueType.Float));

        Convert();

        Assert.AreEqual(2, Settings.Count);
        var sharedEntry = Settings.Single(s => s.machineName == "Shared");
        Assert.AreEqual("Enable", sharedEntry.name);
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Toggle, sharedEntry.type);
        var spinAmountEntry = Settings.Single(s => s.machineName == "SpinAmount");
        Assert.AreEqual("Spin", spinAmountEntry.name);
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Slider, spinAmountEntry.type);
    }

    [Test]
    public void IntParameterOnlyUsedAsPuppetSubParameter_ProducesNoMenuEntry()
    {
        // VRChat's puppet controls (Radial/TwoAxis/FourAxis) only accept Float parameters for their
        // subParameters -- the value is driven continuously by stick/dial position, not chosen from a
        // discrete list. An Int-typed parameter referenced only that way (e.g. a hand-edited or
        // malformed menu asset) therefore has no set of named options to build a CVR dropdown from,
        // and no toggle semantics either (it's never set to a fixed value by a Toggle/Button). The
        // correct conversion is to skip generating a menu entry for it -- not crash, not fabricate a
        // dropdown out of nothing -- while still converting the underlying animator parameter itself.
        var puppet = new VRCExpressionsMenu.Control
        {
            name = "Aim",
            type = VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            subParameters = new[]
            {
                new VRCExpressionsMenu.Control.Parameter { name = "AimX" },
                new VRCExpressionsMenu.Control.Parameter { name = "" },
            },
        };
        SetMenu(Menu(puppet));
        SetParams(Param("AimX", VRCExpressionParameters.ValueType.Int));

        Convert();

        Assert.AreEqual(0, Settings.Count);
    }

    [Test]
    public void IntParameterUsedAsBothDropdownAndPuppetSubParameter_IgnoresPuppetEntryInDropdown()
    {
        // Same puppet-subParameter NaN registration as above, but this time the Int parameter is also
        // driven by a normal Toggle-group dropdown. The NaN "continuous value" entry carries no
        // discrete option and must not corrupt the dropdown's option range/count -- the dropdown
        // should come out exactly as if the puppet reference wasn't there at all.
        var puppet = new VRCExpressionsMenu.Control
        {
            name = "Aim",
            type = VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            subParameters = new[]
            {
                new VRCExpressionsMenu.Control.Parameter { name = "Color" },
                new VRCExpressionsMenu.Control.Parameter { name = "" },
            },
        };
        var subMenu = Menu(
            Toggle("Red", "Color", 0f),
            Toggle("Green", "Color", 1f),
            Toggle("Blue", "Color", 2f));
        SetMenu(Menu(SubMenuControl("Colors", subMenu), puppet));
        SetParams(Param("Color", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));

        Convert();

        Assert.AreEqual(1, Settings.Count);
        var entry = Settings[0];
        Assert.AreEqual(CVRAdvancedSettingsEntry.SettingsType.Dropdown, entry.type);
        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)entry.setting;
        Assert.AreEqual(new[] { "Red", "Green", "Blue" }, dropdown.options.Select(o => o.name).ToArray());
    }

    [Test]
    public void NegativeToggleValue_IsKeptThroughTheIndirectionLayer()
    {
        // CVR dropdown options carry no per-option value field -- the option's list index is the
        // value it sets (AddCondition(Equals, i, ...) in CVRAdvancedAvatarSettings) -- so there is
        // no index that could stand for a negative control.value. The menu drives a local selector
        // instead, and the generated layer writes the negative value the option means.
        core.useHierarchicalDropdownMenuName = false; // isolate from the flat-menu naming bug below
        SetMenu(Menu(
            Toggle("Negative", "Mode", -1f),
            Toggle("Zero", "Mode", 0f),
            Toggle("One", "Mode", 1f)));
        SetParams(Param("Mode", VRCExpressionParameters.ValueType.Int, defaultValue: 0f));

        Convert();
        MakeIntMenuIndirectionLayers();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        Assert.AreEqual(new[] { "Negative", "Zero", "One" }, dropdown.options.Select(o => o.name).ToArray());
        Assert.AreEqual(1, dropdown.defaultValue);
        Assert.AreEqual("#ModeIdx", Settings[0].machineName);
        Assert.AreEqual(new[] { -1f, 0f, 1f }, WrittenValues("#ModeIdx"));
    }

    [Test]
    public void GappedToggleValues_AreWrittenByTheIndirectionLayer()
    {
        // The values a menu assigns are the avatar's own business -- an animator can dispatch on 3
        // and 7 with nothing in between -- but a dropdown can only offer them in a row. Padding the
        // gaps with placeholder options would put values in the menu that the avatar has no state
        // for, so the options stay exactly the ones the menu has and the layer writes their values.
        SetUpGappedModeMenu(defaultValue: 7f);

        Convert();
        MakeIntMenuIndirectionLayers();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        // index 0 is the deselected state: VRChat leaves the parameter at 0 with no toggle picked,
        // and a CVR dropdown always has one option selected
        Assert.AreEqual(new[] { "---", "Three", "Seven" }, dropdown.options.Select(o => o.name).ToArray());
        Assert.AreEqual(2, dropdown.defaultValue);
        Assert.AreEqual(new[] { 0f, 3f, 7f }, WrittenValues("#ModeIdx"));
        Assert.AreEqual(2, Controller.parameters.Single(p => p.name == "#ModeIdx").defaultInt);
    }

    [Test]
    public void ContiguousToggleValues_KeepDrivingTheParameterDirectly()
    {
        // Numbered 0..N-1 the option's own position is already the value it sets, so there is
        // nothing for a layer to translate.
        SetUpContiguousColorMenu();

        Convert();
        MakeIntMenuIndirectionLayers();

        Assert.AreEqual("Color", Settings[0].machineName);
        Assert.AreEqual(0, Controller.layers.Length);
    }

    [Test]
    public void ParameterMovedByTheAvatarItself_MovesTheMenuSelectionWithIt()
    {
        // The menu no longer sets the parameter directly, so a parameter driver, a contact or a
        // second menu moving it would leave the dropdown showing the option it last picked. Each
        // option is entered from the parameter's side as well, which puts the menu back in step.
        Controller.AddParameter("Mode", AnimatorControllerParameterType.Int);
        SetUpGappedModeMenu(defaultValue: 3f);

        Convert();
        MakeIntMenuIndirectionLayers();

        // option index 2 ("Seven") is entered either by the menu picking it or by Mode reaching 7
        var toSeven = Layer("#ModeIdx").stateMachine.anyStateTransitions
            .Where(t => t.destinationState.name == "7")
            .Select(t => t.conditions.Single())
            .ToArray();
        Assert.AreEqual(new[] { "#ModeIdx", "Mode" }, toSeven.Select(c => c.parameter).ToArray());
        Assert.AreEqual(new[] { 2f, 7f }, toSeven.Select(c => c.threshold).ToArray());
        // and entering it writes both sides, so neither can drift from the other
        Assert.AreEqual(new[] { 7f, 2f }, Layer("#ModeIdx").stateMachine.states
            .Single(s => s.state.name == "7").state.behaviours
            .OfType<AnimatorDriver>().Single().EnterTasks.Select(t => t.aValue).ToArray());
    }

    [Test]
    public void TheIndirectionLayerWritesNothingOnARemoteCopy()
    {
        // A remote copy runs its state machines just the same, but what the menu drives never
        // reaches it -- # parameters are not synced -- so it would sit in the default option and
        // overwrite the value that sync had just delivered. localOnly is what stops that.
        SetUpGappedModeMenu(defaultValue: 3f);

        Convert();
        MakeIntMenuIndirectionLayers();

        Assert.That(Layer("#ModeIdx").stateMachine.states
            .SelectMany(s => s.state.behaviours.OfType<AnimatorDriver>())
            .All(d => d.localOnly));
    }

    [Test]
    public void FloatTypedParameter_IsReadBackByABandRatherThanEquals()
    {
        // A menu's Int parameter can end up declared Float -- a blend tree axis has to be one, and
        // the merge takes the first declaration. Equals only reads an Int, so the value comes back
        // through the half-unit band around it instead.
        Controller.AddParameter("Mode", AnimatorControllerParameterType.Float);
        SetUpGappedModeMenu(defaultValue: 3f);

        Convert();
        MakeIntMenuIndirectionLayers();

        var readBack = Layer("#ModeIdx").stateMachine.anyStateTransitions
            .Single(t => t.destinationState.name == "7" && t.conditions[0].parameter == "Mode");
        Assert.AreEqual(new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less },
            readBack.conditions.Select(c => c.mode).ToArray());
        Assert.AreEqual(new[] { 6.5f, 7.5f }, readBack.conditions.Select(c => c.threshold).ToArray());
    }

    [Test]
    public void ButtonMenu_CarriesItsActionMenuModAnnotationOnTheMenuParameter()
    {
        // The annotation says how the menu control behaves, so it belongs on the parameter the menu
        // drives -- which is the selector once the menu has been rebuilt.
        var subMenu = Menu(
            Button("Three", "Mode", 3f),
            Button("Seven", "Mode", 7f));
        SetMenu(Menu(SubMenuControl("Modes", subMenu)));
        SetParams(Param("Mode", VRCExpressionParameters.ValueType.Int));

        Convert();
        MakeIntMenuIndirectionLayers();

        Assert.AreEqual("#ModeIdx<impulse=0.1>", Settings[0].machineName);
    }

    [Test]
    public void DefaultValueNoOptionCarries_OpensOnTheFirstOption()
    {
        // Nothing in the menu sets 5, so no option can be shown as the one selected at start.
        SetUpGappedModeMenu(defaultValue: 5f);

        Convert();
        MakeIntMenuIndirectionLayers();

        Assert.AreEqual(0, ((CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting).defaultValue);
        Assert.AreEqual(0, Controller.parameters.Single(p => p.name == "#ModeIdx").defaultInt);
    }

    [Test]
    public void UnsyncedParameter_IsWrittenUnderTheNameTheConversionGaveIt()
    {
        // The rebuild runs after the parameters have been renamed -- a parameter VRChat did not sync
        // is given a "#" here too -- so it has to write the name the rest of the animator ended up
        // reading, not the one the menu asset named.
        Controller.AddParameter("Mode", AnimatorControllerParameterType.Int);
        SetUpGappedModeMenu(defaultValue: 3f, networkSynced: false);

        Convert();
        typeof(VRC3CVRCore).GetField("contactReceiverParameters", Flags).SetValue(core, new HashSet<string>());
        typeof(VRC3CVRCore).GetMethod("AdjustParameterNames", Flags).Invoke(core, null);
        MakeIntMenuIndirectionLayers();

        Assert.AreEqual("#ModeIdx", Settings[0].machineName);
        var written = Layer("#ModeIdx").stateMachine.states
            .Single(s => s.state.name == "7").state.behaviours
            .OfType<AnimatorDriver>().Single().EnterTasks[0];
        Assert.AreEqual("#Mode", written.targetName);
        Assert.AreEqual(7f, written.aValue);
    }

    [Test]
    public void SplitByHierarchy_LeavesEachItemWhereItWas()
    {
        // A dropdown can only stand in one place, so items spread across submenus were gathered
        // into whichever folder they share. Split, each stays an entry of its own where it was.
        core.splitIntMenuByHierarchy = true;
        SetMenu(Menu(
            SubMenuControl("Dance", Menu(Toggle("Wave", "Mode", 3f))),
            SubMenuControl("Sit", Menu(Toggle("Chair", "Mode", 7f)))));
        SetParams(Param("Mode", VRCExpressionParameters.ValueType.Int, defaultValue: 3f));

        Convert();
        MakeIntMenuIndirectionLayers();

        Assert.AreEqual(new[] { "Dance/Wave", "Sit/Chair" }, Settings.Select(s => s.name).ToArray());
        Assert.AreEqual(new[] { "#Mode_3", "#Mode_7" }, Settings.Select(s => s.machineName).ToArray());
        Assert.AreEqual(new[] { true, false },
            Settings.Select(s => ((CVRAdvancesAvatarSettingGameObjectToggle)s.setting).defaultValue).ToArray());
        // no entry stands for 0, the value nothing selected leaves behind
        Assert.AreEqual(2, Settings.Count);
    }

    [Test]
    public void SplitByHierarchy_TicksOneBoxAndUnticksTheOneItLeaves()
    {
        // What made the group exclusive in VRChat was the one Int parameter behind it. Each option
        // ticks its own box on the way in and unticks it on the way out, so picking a second one
        // clears the first without either of them having to know the whole set.
        core.splitIntMenuByHierarchy = true;
        SetUpGappedModeMenu(defaultValue: 3f);

        Convert();
        MakeIntMenuIndirectionLayers();

        var seven = Layer("#Mode_7").stateMachine.states.Single(s => s.state.name == "7").state
            .behaviours.OfType<AnimatorDriver>().Single();
        Assert.AreEqual(new[] { "Mode", "#Mode_7" }, seven.EnterTasks.Select(t => t.targetName).ToArray());
        Assert.AreEqual(new[] { 7f, 1f }, seven.EnterTasks.Select(t => t.aValue).ToArray());
        Assert.AreEqual(new[] { "#Mode_7" }, seven.ExitTasks.Select(t => t.targetName).ToArray());
        Assert.AreEqual(new[] { 0f }, seven.ExitTasks.Select(t => t.aValue).ToArray());
        // ticking a box is what selects its option
        Assert.AreEqual(AnimatorConditionMode.If, Layer("#Mode_7").stateMachine.anyStateTransitions
            .Single(t => t.destinationState.name == "7" && t.conditions[0].parameter == "#Mode_7")
            .conditions.Single().mode);
    }

    [Test]
    public void SplitByHierarchy_UntickingTheLastBoxLeavesTheParameterAtZero()
    {
        // Nothing selected is the parameter at 0 in VRChat, and with every box cleared that is the
        // state the menu is in -- there being no box of its own to reach it by.
        core.splitIntMenuByHierarchy = true;
        SetUpGappedModeMenu(defaultValue: 3f);

        Convert();
        MakeIntMenuIndirectionLayers();

        var toZero = Layer("#Mode_3").stateMachine.anyStateTransitions.Single(t => t.destinationState.name == "0");
        Assert.AreEqual(new[] { "#Mode_3", "#Mode_7" }, toZero.conditions.Select(c => c.parameter).ToArray());
        Assert.That(toZero.conditions.All(c => c.mode == AnimatorConditionMode.IfNot));
        Assert.AreEqual(new[] { 0f }, Layer("#Mode_3").stateMachine.states.Single(s => s.state.name == "0").state
            .behaviours.OfType<AnimatorDriver>().Single().EnterTasks.Select(t => t.aValue).ToArray());
    }

    void SetUpContiguousColorMenu()
    {
        var subMenu = Menu(
            Toggle("Red", "Color", 0f),
            Toggle("Green", "Color", 1f));
        SetMenu(Menu(SubMenuControl("Colors", subMenu)));
        SetParams(Param("Color", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));
    }

    // 3 and 7 with nothing in between: a dropdown can only offer its options in a row, so this is
    // the numbering that has to be rebuilt.
    void SetUpGappedModeMenu(float defaultValue, bool networkSynced = true)
    {
        var subMenu = Menu(
            Toggle("Three", "Mode", 3f),
            Toggle("Seven", "Mode", 7f));
        SetMenu(Menu(SubMenuControl("Modes", subMenu)));
        var parameter = Param("Mode", VRCExpressionParameters.ValueType.Int, defaultValue);
        parameter.networkSynced = networkSynced;
        SetParams(parameter);
    }

    void MakeIntMenuIndirectionLayers()
    {
        typeof(VRC3CVRCore).GetMethod("MakeIntMenuIndirectionLayers", Flags).Invoke(core, null);
    }

    AnimatorController Controller =>
        (AnimatorController)typeof(VRC3CVRCore).GetField("chilloutAnimatorController", Flags).GetValue(core);

    AnimatorControllerLayer Layer(string selector) =>
        Controller.layers.Single(l => l.stateMachine.anyStateTransitions
            .Any(t => t.conditions.Any(c => c.parameter == selector)));

    // The value each option of the selector's generated layer writes, by option index.
    float[] WrittenValues(string selector)
    {
        return Layer(selector).stateMachine.anyStateTransitions
            .Where(t => t.conditions[0].parameter == selector)
            .OrderBy(t => t.conditions[0].threshold)
            .Select(t => t.destinationState.behaviours.OfType<AnimatorDriver>().Single().EnterTasks[0].aValue)
            .ToArray();
    }

    [Test]
    public void IntDropdown_OptionValuesStartAtOne_KeepsZeroOriginIndexAlignment()
    {
        // Regression for the option-list-shift bug: menus that never assign value 0 to any option
        // (VRChat's own emote menu, and NEmote-style custom menus, both start at 1 since 0 means
        // "no selection") used to have their option list start at the lowest key present -- shifting
        // every option's list index, and therefore its CVR value, down by one. The list must instead
        // start at index 0, with a "---" placeholder standing in for the unused value 0.
        var subMenu = Menu(
            Toggle("First", "Emote", 1f),
            Toggle("Second", "Emote", 2f),
            Toggle("Third", "Emote", 3f));
        SetMenu(Menu(SubMenuControl("Emotes", subMenu)));
        SetParams(Param("Emote", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));

        Convert();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        Assert.AreEqual(new[] { "---", "First", "Second", "Third" }, dropdown.options.Select(o => o.name).ToArray());
        Assert.AreEqual(1, dropdown.defaultValue);
    }

    [Test]
    public void Bug_FlatTopLevelIntDropdown_OptionNamesLoseFirstCharacter()
    {
        // REAL BUG (found while writing the "happy path" dropdown test above): when the dropdown's
        // Toggle controls sit directly in the root menu (no common submenu), GetMenuNameCommonParent
        // returns "" for the common parent. useHierarchicalDropdownMenuName's default (true) then
        // does `name.Substring(menuName.Length + 1)` for every option -- the "+1" is meant to skip
        // the "/" that separates a submenu prefix from the leaf name (see the nested-submenu test
        // above, where it works correctly), but there is no such separator when menuName is "", so
        // Substring(1) silently eats the first character of every option name.
        SetMenu(Menu(
            Toggle("Red", "Color", 0f),
            Toggle("Green", "Color", 1f),
            Toggle("Blue", "Color", 2f)));
        SetParams(Param("Color", VRCExpressionParameters.ValueType.Int, defaultValue: 1f));

        Convert();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        // This is what should happen -- fails today because of the off-by-one Substring above.
        Assert.AreEqual(new[] { "Red", "Green", "Blue" }, dropdown.options.Select(o => o.name).ToArray());
    }
}
#endif
