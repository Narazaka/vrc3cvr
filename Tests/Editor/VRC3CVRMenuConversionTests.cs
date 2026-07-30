#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
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
    public void Bug_ToggleValueOneThenPuppetChangingSameParameter_ThrowsArgumentException()
    {
        // REAL BUG in FindMenuButtonsAndToggles's local TreatChanging(): it checks
        // `idTable.ContainsKey(control.value)` but then unconditionally does `idTable.Add(1, ...)`.
        // If the same VRC parameter was already registered under key 1 by an earlier Toggle control,
        // and a puppet's "changing" indicator (control.value != 1) references that same parameter,
        // ContainsKey(control.value) is false (the dictionary only has key 1) so the guard passes,
        // but Add(1, ...) then collides with the pre-existing key 1 entry and throws.
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

        Assert.Throws<ArgumentException>(Convert);
    }

    [Test]
    public void Bug_IntParameterOnlyUsedAsPuppetSubParameter_ThrowsInvalidOperationException()
    {
        // REAL BUG: an Int-typed VRC parameter referenced only via a puppet's subParameters (never
        // through a Toggle/Button at an integer value) ends up in toggleTable keyed solely at
        // float.NaN. ConvertVrcParametersToChillout's Int branch then does
        // `(int)intIdTable.Keys.Max()` (i.e. (int)NaN) as the dropdown's upper bound; whatever that
        // cast produces, the option-building loop can never actually find a NaN-keyed entry, so
        // menuEntryNames ends up empty (or full of "---" placeholders) either way, and
        // GetMenuNameCommonParent(...).First() throws on the empty filtered sequence.
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

        Assert.Throws<InvalidOperationException>(Convert);
    }

    [Test]
    public void Bug_NegativeToggleValue_IsSilentlyDroppedFromDropdown()
    {
        // REAL BUG: FindMenuButtonsAndToggles stores toggle entries keyed by the raw VRC
        // control.value, which can be negative, but ConvertVrcParametersToChillout's Int branch
        // only walks `for (j = 0; j < lastIndex + 1; j++)` starting at zero. A control with a
        // negative value is therefore silently omitted from the resulting dropdown instead of
        // being clamped, reordered, or reported.
        core.useHierarchicalDropdownMenuName = false; // isolate from the flat-menu naming bug below
        SetMenu(Menu(
            Toggle("Negative", "Mode", -1f),
            Toggle("Zero", "Mode", 0f),
            Toggle("One", "Mode", 1f)));
        SetParams(Param("Mode", VRCExpressionParameters.ValueType.Int, defaultValue: -1f));

        Convert();

        var dropdown = (CVRAdvancesAvatarSettingGameObjectDropdown)Settings[0].setting;
        // Correct behavior would keep all three options in control order; today "Negative" vanishes
        // because the option-building loop starts at index 0.
        Assert.AreEqual(new[] { "Negative", "Zero", "One" }, dropdown.options.Select(o => o.name).ToArray());
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
