#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// VendorWares: a player vendor's wares in a few words, from its items' labels.
public static class VendorWaresTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("wares: each kind by its label, magic words first, tools before weapons, armor before clothing",
            Kinds),
        new TestCase("wares: a weapon is whole label words spelling a table name; a bowl, a cut of ribs and a "
        + "skullcap are not", WeaponWords),
        new TestCase("wares: nothing, or nothing for sale, is private utility", Utility),
        new TestCase("wares: one kind, then two by count and a tie by first seen; items not for sale are not counted",
        Named),
        new TestCase("wares: a quarter is named and less is not; past three kinds, or only other, is a hodgepodge",
        Shares));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static VendorWares.Ware W(string name, bool sale = true) => new(name, sale);

    static string Describe(params VendorWares.Ware[] w) => VendorWares.Describe(new List<VendorWares.Ware>(w));

    static void Kinds()
    {
        (string Label, string Kind)[] rows =
        {
            ("26 iron ingots", "ingots"), ("a scroll of fireball", "scrolls"), ("a greater heal potion", "potions"),
            ("12 black pearl", "reagents"), ("a katana of vanquishing", "magicals"), ("a pickaxe", "tools"),
            ("a smithy hammer", "tools"), ("a war hammer", "weapons"), ("a studded tunic", "armor"),
            ("platemail legs", "armor"), ("a breastplate", "armor"), ("a tunic", "clothing"),
            ("a fancy shirt", "clothing"),
            ("a bolt of cloth", "cloth"), ("20 crossbow bolts", "ammunition"), ("50 arrows", "ammunition"),
            ("100 logs", "wood"), ("a pile of hides", "hides"), ("a ruby", "gems"), ("a key ring", "keys"),
            ("a candle", "other"),
        };
        foreach ((string label, string kind) in rows)
            Check.Equal(kind, VendorWares.KindOf(label), label);
    }

    static void WeaponWords()
    {
        Check.Equal("weapons", VendorWares.KindOf("a broadsword"), "the table's Broad Sword in one word");
        Check.Equal("weapons", VendorWares.KindOf("a short spear"), "two words");
        Check.Equal("weapons", VendorWares.KindOf("an executioner's axe"), "an apostrophe kept");
        Check.Equal("weapons", VendorWares.KindOf("a large battle axe"), "a TILEDATA axe the table names apart");
        Check.Equal("weapons", VendorWares.KindOf("3 katanas"), "a plural");
        Check.Equal("weapons", VendorWares.KindOf("a two-handed axe"), "a hyphen dropped");
        Check.Equal("other", VendorWares.KindOf("a bowl"), "bow is not a word of it");
        Check.Equal("other", VendorWares.KindOf("a cut of ribs"), "the table's Cut row is no weapon");
        Check.Equal("other", VendorWares.KindOf("a skullcap"), "nor its Skullcap row");
    }

    static void Utility()
    {
        Check.Equal(VendorWares.Utility, Describe(), "nothing");
        Check.Equal(VendorWares.Utility, Describe(W("a key ring", false), W("a bag", false)),
            "keys and a bag not for sale");
    }

    static void Named()
    {
        Check.Equal("ingots", Describe(W("26 iron ingots"), W("50 iron ingots")), "one kind");
        Check.Equal("armor, weapons", Describe(W("a katana"), W("a plate helm"), W("platemail legs")), "by count");
        Check.Equal("weapons, armor", Describe(W("a katana"), W("a plate helm")), "a tie, the first seen first");
        Check.Equal("potions", Describe(W("a greater heal potion"), W("a key ring", false), W("a key ring", false)),
            "keys not for sale not counted");
    }

    static void Shares()
    {
        Check.Equal("ingots", Describe(W("ingots"), W("a candle"), W("a bowl"), W("a lantern")),
            "1 of 4 is a quarter, named");
        Check.Equal(VendorWares.Hodgepodge,
            Describe(W("ingots"), W("a candle"), W("a bowl"), W("a lantern"), W("a book")), "1 of 5 is not");
        Check.Equal(VendorWares.Hodgepodge, Describe(W("ingots"), W("a scroll"), W("a potion"), W("a ruby")),
            "four kinds of a quarter each");
        Check.Equal("ingots, scrolls, potions", Describe(W("ingots"), W("a scroll"), W("a potion")), "three kinds");
        Check.Equal(VendorWares.Hodgepodge, Describe(W("a candle"), W("a bowl")), "only other");
    }
}
