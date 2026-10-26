// A player vendor's wares in a few words (Damian, 2026-10-23: "armor, weapons, magicals, scrolls, potions, ingots, a
// hodgepodge"; "some vendors are just used for private utility like we do with house door keys").
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public static class VendorWares
{
    public const string Utility = "private utility";
    public const string Hodgepodge = "a hodgepodge";
    // A kind is named when it holds at least SharePct percent of the items for sale; more than MaxKinds named, or none,
    // is a hodgepodge.
    public const int SharePct = 25;
    public const int MaxKinds = 3;

    public sealed record Ware(string Name, bool ForSale);

    // First match wins: a label of magic words (AssistWindow.IsMagic) is magicals whatever it is, tools and ammunition
    // before weapons (the 1998 weapon table holds the pickaxe, the smithy hammer, the knives and the cleaver, and
    // "crossbow bolts" holds a crossbow), and armor before clothing, as
    // "studded tunic" and "tunic" both end in tunic. A word matches at the start of a word of the label.
    static readonly ImmutableArray<(string Kind, ImmutableArray<string> Words)> s_beforeWeapons = ImmutableArray.Create(
        ("tools", ImmutableArray.Create("pickaxe", "shovel", "smith", "tongs", "saw", "sewing kit", "scissors",
            "tinker",
            "lockpick", "skinning knife", "butcher knife", "cleaver", "fishing pole", "dovetail", "froe", "inshave",
            "drawknife", "scorp", "mortar")),
        ("ammunition", ImmutableArray.Create("arrow", "crossbow bolt")));
    static readonly ImmutableArray<(string Kind, ImmutableArray<string> Words)> s_kinds = ImmutableArray.Create(
        ("ingots", ImmutableArray.Create("ingot")),
        ("scrolls", ImmutableArray.Create("scroll")),
        ("potions", ImmutableArray.Create("potion")),
        ("reagents", ImmutableArray.Create("black pearl", "blood moss", "garlic", "ginseng", "mandrake", "nightshade",
            "spider's silk", "sulfurous ash")),
        ("armor", ImmutableArray.Create("plate", "breastplate", "chain", "ringmail", "ring mail", "studded", "bone",
            "helm", "bascinet",
            "gorget", "shield", "leather gloves", "leather cap", "leather sleeves", "leather leggings", "leather tunic",
            "female leather", "leather skirt", "leather bustier", "leather shorts")),
        ("clothing", ImmutableArray.Create("shirt", "robe", "cloak", "dress", "skirt", "kilt", "pants", "sandals",
            "shoes", "boots", "hat", "cap", "bonnet", "doublet", "tunic", "surcoat", "apron", "sash", "bandana")),
        ("cloth", ImmutableArray.Create("cloth", "yarn", "thread")),
        ("wood", ImmutableArray.Create("log", "board")),
        ("hides", ImmutableArray.Create("hide", "leather")),
        ("gems", ImmutableArray.Create("diamond", "ruby", "sapphire", "emerald", "amethyst", "citrine", "tourmaline",
            "star sapphire", "amber")),
        ("keys", ImmutableArray.Create("key")));

    // The 1998 weapon table's names (UoWeapons) with their spaces taken out, as the labels write them ("Broad Sword" is
    // "a broadsword"); its non-weapon rows "Cut" and "Skullcap" left out, and the TILEDATA names of the axes and the
    // heavy crossbow, which the table names apart ("Bad Axe", "Evil Axe", "Big Crossbow"), added.
    static readonly ImmutableArray<string> s_weapons = WeaponNames();

    static ImmutableArray<string> WeaponNames()
    {
        ImmutableArray<string>.Builder b = ImmutableArray.CreateBuilder<string>();
        b.AddRange("axe", "doubleaxe", "battleaxe", "largebattleaxe", "heavycrossbow");
        foreach (string row in UoWeapons.Rows)
        {
            string[] f = row.Split('|');
            if (f.Length < 5 || f[4] == "Shield" || f[1] == "Cut" || f[1] == "Skullcap")
                continue;
            string n = f[1].Replace(" ", "").Replace("-", "").ToLowerInvariant();
            if (!b.Contains(n))
                b.Add(n);
        }
        return b.ToImmutable();
    }

    static bool HasWord(string low, string w)
    {
        for (int i = low.IndexOf(w, StringComparison.Ordinal); i
            >= 0; i = low.IndexOf(w, i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || !char.IsLetter(low[i - 1]))
                return true;
        }
        return false;
    }

    static string? KindIn(string low, ImmutableArray<(string Kind, ImmutableArray<string> Words)> kinds)
    {
        foreach ((string kind, ImmutableArray<string> words) in kinds)
        {
            foreach (string w in words)
            {
                if (HasWord(low, w))
                    return kind;
            }
        }
        return null;
    }

    // A weapon is one, two or three words of the label that spell a table name, a plural's s dropped.
    static bool IsWeapon(string low)
    {
        string[] t = low.Replace("-", "").Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < t.Length; i++)
        {
            string run = "";
            for (int j = i; j < Math.Min(t.Length, i + 3); j++)
            {
                run += t[j];
                string one = run.EndsWith("s", StringComparison.Ordinal) ? run.Substring(0, run.Length - 1) : run;
                if (s_weapons.Contains(run) || s_weapons.Contains(one))
                    return true;
            }
        }
        return false;
    }

    // The kind of one item by its label: magicals, tools or ammunition, a weapon of the 1998 table, a kind of s_kinds,
    // or "other".
    public static string KindOf(string name)
    {
        if (AssistWindow.IsMagic(name))
            return "magicals";
        string low = name.ToLowerInvariant();
        string? early = KindIn(low, s_beforeWeapons);
        if (early is not null)
            return early;
        if (IsWeapon(low))
            return "weapons";
        return KindIn(low, s_kinds) ?? "other";
    }

    // Nothing for sale is private utility; else the kinds of SharePct or more of the items for sale, most first, joined
    // by ", ", or a hodgepodge when none or more than MaxKinds reach it. "other" is never named.
    public static string Describe(IReadOnlyList<Ware> wares)
    {
        List<string> kinds = new();
        List<int> counts = new();
        int sale = 0;
        foreach (Ware w in wares)
        {
            if (!w.ForSale)
                continue;
            sale++;
            string k = KindOf(w.Name);
            int i = kinds.IndexOf(k);
            if (i < 0)
            {
                kinds.Add(k);
                counts.Add(1);
            }
            else
            {
                counts[i]++;
            }
        }
        if (sale == 0)
            return Utility;
        List<int> named = new();
        for (int i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] != "other" && counts[i] * 100 >= sale * SharePct)
                named.Add(i);
        }
        if (named.Count == 0 || named.Count > MaxKinds)
            return Hodgepodge;
        named.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));
        return string.Join(", ", named.ConvertAll(i => kinds[i]));
    }
}
