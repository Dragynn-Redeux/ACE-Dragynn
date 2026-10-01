using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class PackIconsTests
{
    [TestMethod]
    public void TryParse_TakesStyleAndColorInEitherOrder()
    {
        Assert.IsTrue(PackIcons.TryParse(new[] { "sack", "blue" }, out var a));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.Sack, "blue", false), a);

        Assert.IsTrue(PackIcons.TryParse(new[] { "Blue", "SACK" }, out var b));
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TryParse_LeavesOutWhatIsntGiven()
    {
        Assert.IsTrue(PackIcons.TryParse(new[] { "red" }, out var colorOnly));
        Assert.AreEqual(new PackIconRequest(null, "red", false), colorOnly);

        Assert.IsTrue(PackIcons.TryParse(new[] { "pack" }, out var styleOnly));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.Pack, null, false), styleOnly);
    }

    [TestMethod]
    public void TryParse_ReadsGreyAsGrayAndDefault()
    {
        Assert.IsTrue(PackIcons.TryParse(new[] { "grey", "backpack" }, out var grey));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.Pack, "gray", false), grey);

        Assert.IsTrue(PackIcons.TryParse(new[] { "default" }, out var reset));
        Assert.IsTrue(reset.Default);
    }

    [TestMethod]
    public void TryParse_RefusesUnknownRepeatedOrMixedWords()
    {
        Assert.IsFalse(PackIcons.TryParse(new[] { "pink" }, out _));
        Assert.IsFalse(PackIcons.TryParse(new[] { "blue", "red" }, out _));
        Assert.IsFalse(PackIcons.TryParse(new[] { "pack", "sack" }, out _));
        Assert.IsFalse(PackIcons.TryParse(new[] { "default", "blue" }, out _));
        Assert.IsFalse(PackIcons.TryParse(new string[0], out _));
    }

    [TestMethod]
    public void TryParse_ReadsPouchNames()
    {
        Assert.IsTrue(PackIcons.TryParse(new[] { "small", "pouch", "red" }, out var small));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.SmallPouch, "red", false), small);

        Assert.IsTrue(PackIcons.TryParse(new[] { "blue", "small", "belt", "pouch" }, out var smallBelt));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.SmallPouch, "blue", false), smallBelt);

        Assert.IsTrue(PackIcons.TryParse(new[] { "belt", "pouch", "teal" }, out var belt));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.Pouch, "teal", false), belt);

        Assert.IsTrue(PackIcons.TryParse(new[] { "Pouch" }, out var pouch));
        Assert.AreEqual(new PackIconRequest(PackIconStyle.Pouch, null, false), pouch);

        Assert.IsFalse(PackIcons.TryParse(new[] { "small", "blue" }, out _));
    }

    [TestMethod]
    public void TryParse_HasNoOrange()
    {
        Assert.IsFalse(PackIcons.TryParse(new[] { "orange" }, out _));
    }

    [TestMethod]
    public void PickIcon_UsesTheColorsVendorTemplate()
    {
        // a plain Pack's table: Gold is its brown, BlueGreen its teal
        var icons = new Dictionary<uint, uint>
        {
            { (uint)PaletteTemplate.Gold, 0x06001BAF },
            { (uint)PaletteTemplate.Black, 0x06001BB4 },
            { (uint)PaletteTemplate.BlueGreen, 0x06000001 },
            { (uint)PaletteTemplate.Aqua, 0x06000002 },
            { (uint)PaletteTemplate.Red, 0 }
        };

        Assert.AreEqual(0x06001BAFu, PackIcons.PickIcon(PackIconStyle.Pack, "brown", icons));
        Assert.AreEqual(0x06001BB4u, PackIcons.PickIcon(PackIconStyle.Pack, "black", icons));
        Assert.AreEqual(0x06000001u, PackIcons.PickIcon(PackIconStyle.Pack, "teal", icons));

        // an entry with no icon doesn't count, and a color the table lacks has none
        Assert.IsNull(PackIcons.PickIcon(PackIconStyle.Pack, "red", icons));
        Assert.IsNull(PackIcons.PickIcon(PackIconStyle.Pack, "purple", icons));
    }

    [TestMethod]
    public void PickIcon_PouchesUseTheDyeTemplatesAndHaveNoWhite()
    {
        var icons = new Dictionary<uint, uint>
        {
            { (uint)PaletteTemplate.Brown, 0x06001011 },
            { (uint)PaletteTemplate.Gold, 0x06000003 },
            { (uint)PaletteTemplate.DyeWinterBlue, 0x06000004 },
            { (uint)PaletteTemplate.BlueGreen, 0x06000005 },
            { (uint)PaletteTemplate.DyeWinterSilver, 0x06000006 },
            { (uint)PaletteTemplate.White, 0x06000007 }
        };

        Assert.AreEqual(0x06001011u, PackIcons.PickIcon(PackIconStyle.Pouch, "brown", icons));
        Assert.AreEqual(0x06000004u, PackIcons.PickIcon(PackIconStyle.SmallPouch, "teal", icons));
        Assert.AreEqual(0x06000006u, PackIcons.PickIcon(PackIconStyle.Pouch, "gray", icons));
        Assert.IsNull(PackIcons.PickIcon(PackIconStyle.Pouch, "white", icons));

        // the same table read as a pack's takes the pack templates
        Assert.AreEqual(0x06000003u, PackIcons.PickIcon(PackIconStyle.Pack, "brown", icons));
        Assert.AreEqual(0x06000007u, PackIcons.PickIcon(PackIconStyle.Pack, "white", icons));
    }
}
