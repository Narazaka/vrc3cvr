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
    public void NegativeToggleValue_IsDroppedWithWarning()
    {
        // CVR dropdown options carry no per-option value field -- the option's list index is the
        // value it sets (AddCondition(Equals, i, ...) in CVRAdvancedAvatarSettings) -- so there is
        // no index that could stand in for a negative control.value. It is dropped, with a warning,
        // rather than shifting the whole option list to make room for it.
        core.useHierarchicalDropdownMenuName = false; // isolate from the flat-menu naming bug below
        SetMenu(Menu(
            Toggle("Negative", "Mode", -1f),
            Toggle("Zero", "Mode", 0f),
            Toggle("One", "Mode", 1f)));
        SetParams(Param("Mode", VRCExpressionParameters.ValueType.Int, defaultValue: 0f));

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
            "Param \"Mode\" has option value(s) -1 which are negative; CVR dropdown options are "
                + "addressed by list index and can't represent a negative value, so those option(s) are dropped.")));

        Convert();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        Assert.AreEqual(new[] { "Zero", "One" }, dropdown.options.Select(o => o.name).ToArray());
        Assert.AreEqual(0, dropdown.defaultValue);
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
