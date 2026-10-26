#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

// Stage 4 of assist-plan.md: canned loops, arm and disarm, drinking without hotkeys, reagent autopickup, resource
// counts, skills and stats advice, display options. Every action goes through GameLink, which enforces rules 1 to 4;
// one feature acts at a time (actionGate), so two never have actions in flight together (rule 3).

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // Skill ids are the client's, 0-based (Hiding 21, as RunUO's SkillInfo.Table[21]).
    // Target: 0 none, 1 an item or creature the first iteration picks. Pairs: tool uses recorded, each with its target.
    static readonly ImmutableArray<LoopKind> s_loops = [
        new() { Name = "Mining (1 tool)", Pairs = 1, DelayMs = 2500,
            Source = "Damian, 2026-10-15: \"for mining 1 its 2500ms\"; \"the delay for mining is 2500 ... in uoassist "
            + "exactly. never fails at that ... for shovel or pickaxe\"" },
        new() { Name = "Mining (2 tools)", Pairs = 2, DelayMs = 1250,
        Source = "Damian, 2026-10-15: \"for 2 its 1250 per tool use\"" },
        new() { Name = "Blacksmithing (until out of ingots)", Skill = 7, Smith = 1,
        Source = "Damian, 2026-10-15: \"get smithy in there for loops. both build until exhausted of ingots and build "
        + "until exceptional\"" },
        new() { Name = "Blacksmithing (until exceptional)", Skill = 7, Smith = 2,
        Source = "Damian, 2026-10-15: \"get smithy in there for loops. both build until exhausted of ingots and build "
        + "until exceptional\"" },
        new() { Name = "Smelting (one ore at a time)", Skill = MiningSkill, Smelt = 1, DelayMs = 2500,
        Source = "Damian, 2026-10-16: \"given a target on the ground, take 1 of it and put it in backpack, then smelt "
        + "that one item. first iteration is manual\", and of 1000 ms \"the wait between steps is a little too "
        + "short\": uo1998 refused a smelt 1.9 s after the last; 2500 ms is the mining tool's" },
        new() { Name = "Tailoring (cloth, then bolts)", Skill = 34, Tailor = 1, DelayMs = 2500,
        Source = "Damian, 2026-10-16: \"use that pile of cloth until there isn't enough left over, then start a new "
        + "bolt from the same pile of bolts if possible\"; no delay was given: uo1998 answered every demo sewing at 2 "
        + "to 8 s apart (relay log 04:19:39 to 04:22:09), and 2500 ms is the mining tool's" },
        new() { Name = "Magery (training, circles 1 to 8)", Skill = 25, Magery = 1,
        Source = "Damian, 2026-10-15: \"add magery loops too\", \"for each spell level\", \"a loop for training the "
        + "skill, and it needs to go all the way to circle 8\"" },
        new() { Name = "Magery (your spell)", Skill = 25, Magery = 2,
        Source = "Damian, 2026-10-19: \"the magery loop should let me pick the spell. it is trying to cast too hard "
        + "of spells here for my level when lower spells would be better\"" },
        new() { Name = "Hiding", Skill = 21, DelayMs = 12500,
        Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new() { Name = "Spirit Speak", Skill = 32, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new() { Name = "Detect Hidden", Skill = 14, Target = 1, DelayMs = 12500,
        Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new() { Name = "Arms Lore", Skill = 4, Target = 1, DelayMs = 12500,
        Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new() { Name = "Anatomy", Skill = 1, Target = 1, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new() { Name = "Animal Lore", Skill = 2, Target = 1, DelayMs = 12500,
        Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new() { Name = "Item Identification", Skill = 3, Target = 1, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new() { Name = "Evaluating Intelligence", Skill = 16, Target = 1, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new() { Name = "Forensic Evaluation", Skill = 19, Target = 1, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new() { Name = "Taste Identification", Skill = 36, Target = 1, DelayMs = 5000, Probe = true,
        Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
    ];
    public static ImmutableArray<LoopKind> Loops => s_loops;
    // The bisect's bounds: a probe never goes under MinDelayMs (twice RunUO's 500 ms ActionDelay), it stops once the
    // working and refused delays are within SettleMs, and a loop the server refuses past MaxDelayMs stops.
    public const int MinDelayMs = 1000, SettleMs = 250, MaxDelayMs = 60000, ProbeAfterRounds = 3;
    static volatile bool s_pacing;
    public static bool Pacing { get => s_pacing; set => s_pacing = value; }

    // The timings (AssistTimings.cs); the window and the control port read and set them through Timings alone.
    public ITimings Timings
    {
        get; private set;
    }
    int Ms(string key)
    {
        return Timings.Ms(key);
    }
    void SetMs(string key, int ms, string why)
    {
        Timings.Set(key, ms, why);
    }
    void Seen(string key, int ms)
    {
        Timings.Seen(key, ms);
    }
    bool AutoTune => Timings.AutoTune;

    // A loop stops on these journal lines: the UOAIX shard's (GameHarvestState.codex:69-70), RunUO's worn tool
    // (HarvestSystem cliloc 1044038), and uo1998's empty seam, worded apart from the shard's (relay log 2026-10-15;
    // Damian: "need to stop the mining macro if the seam runs out").
    const string WornOut = "You have worn out your tool!";
    static readonly ImmutableArray<string> s_stopLines = [
        "There is no metal to mine here.", "There is no metal here to mine.", "You cannot mine there.",
            "You are too far away to mine that!", WornOut,
        "More reagents are needed", "You do not have enough reagents",
    ];
    public static ImmutableArray<string> StopLines => s_stopLines;
    // uo1998's full pack. The loop puts the pack's largest ore stack down at the player's feet and goes on (Damian,
    // 2026-10-15: "full pack should dump the biggest load of ore out of your pack on to the ground at your feet, like
    // the server will do, but instead of the server, we do with the ore you have, not the overflow").
    static readonly ImmutableArray<string> s_fullPackLines = [ "put it at your feet",
        "That container cannot hold more weight." ];
    public static ImmutableArray<string> FullPackLines => s_fullPackLines;
    static readonly ImmutableArray<int> s_oreGraphics = [ 0x19B7, 0x19B8, 0x19B9, 0x19BA ];
    // The bag full (Damian, 2026-10-16: "i said stop when the bag is FULL", "and the next dig is going to fucking
    // OVERFLOW", "INSTEAD move the WHOLE BIG PILE IN MY BAG", "NOT 1 FUCKING PIECE OF ORE"): the server's own status
    // weight (0x11) plus one dig's ore passes PackLimit ("its like 450, not 400"; the status read 451 when uo1998 put a
    // dig at the feet, relay log 2026-10-16 19:42:34 to 19:42:46, each dig adding 20), or the server says it put ore at
    // the feet. Then the biggest ore pile in the pack, whole, goes on the tile beside the player: every hand drop
    // uo1998 kept went beside the player, and both drops on the player's own tile came back into the pack (19:42:47,
    // 20:04:30). The dump waits DumpAfterDigMs after the last dig's target, so the server has handed over that dig's
    // ore.
    public const int PackLimit = 450, DigGain = 20;
    int DropSettleMs => Ms("drop-settle");
    int PendingDigMs => Ms("pending-dig");
    // Digs whose result has not come back (Damian, 2026-10-16: "sometimes when a bag is full, there is a pending shovel
    // pull. moving the ore too soon will cause the single to drop in the bag ... keep track of the pending response
    // from the other shovel, and if there is a pending response, just do nothing instead"): a mining target adds one, a
    // dig result line takes the oldest off, and one older than PendingDigMs is taken as answered.
    // One line a dig ("You destroyed the shovel." follows a dig's own line, relay log 2026-10-16 20:21:21, so it is not
    // one).
    static readonly ImmutableArray<string> s_digResults = [ "You dig some ore", "You loosen some rocks",
        "There is no metal",
        "You cannot mine", "is too far away to mine" ];
    readonly Queue<DateTime> m_pendingDigs = new();
    int PendingDigs()
    {
        lock (m_pendingDigs)
        {
            while (m_pendingDigs.Count > 0 && (Clock.UtcNow - m_pendingDigs.Peek()).TotalMilliseconds > PendingDigMs)
                m_pendingDigs.Dequeue();
            return m_pendingDigs.Count;
        }
    }
    void DigResult(byte[] p)
    {
        if (p[0] != 0x1C || p.Length <= 44)
            return;
        string said = Encoding.ASCII.GetString(p, 44, p.Length - 44);
        foreach (string r in s_digResults)
        {
            if (said.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DateTime at = DateTime.MinValue;
                lock (m_pendingDigs)
                    if (m_pendingDigs.Count > 0)
                        at = m_pendingDigs.Dequeue();
                if (at != DateTime.MinValue)
                    Seen("pending-dig", (int)(Clock.UtcNow - at).TotalMilliseconds);
                return;
            }
        }
    }
    const string NoOre = "the pack is full and holds no ore to put down";
    static IInventory Inv(GameLink k)
    {
        return new Inventory(k.State);
    }
    static Set<uint> PackTree(GameLink k)
    {
        return Inv(k).PackTree();
    }
    public int PackLimitFor => Cfg.GetInt(Server + "pack.limit", PackLimit);
    bool PackNearFull(GameLink k)
    {
        int w;
        lock (k.State.Gate)
            w = k.State.Weight;
        return w > 0 && w + DigGain > PackLimitFor;
    }
    string? DumpOre(GameLink k)
    {
        AssistItem? best = null;
        int x, y, z;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
            Set<uint> inside = PackTree(k);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
            {
                AssistItem it = kv.Value;
                if (it.Layer < 0 && inside.Contains(it.Container) && s_oreGraphics.IndexOf(it.Graphic) >= 0
                    && (best == null || it.Amount > best.Amount))
                    best = it;
            }
        }
        if (best == null)
            return NoOre;
        int amount = Math.Max(1, best.Amount), dx = x, dy = y + 1;
        // A pile of the same ore (any ore art, the same hue) within one tile takes the load (Damian, 2026-10-16: "if
        // there is an identical type of ore in 1 tile grid around the character, just move it to that existing pile
        // instead"): the drop names the pile, which stacks it; a pile that sends the load back leaves the tile beside
        // the player.
        AssistItem? pile = null;
        lock (k.State.Gate)
        {
            foreach (AssistItem g in k.State.Ground.Values)
                if (s_oreGraphics.IndexOf(g.Graphic) >= 0 && g.Hue == best.Hue
                    && Math.Max(Math.Abs(g.X - x), Math.Abs(g.Y - y)) <= 1 && (pile == null || g.Amount > pile.Amount))
                    pile = g;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Answered(k, AnswerMs);
            DateTime since = m_lastUse > m_harvestActAt ? m_lastUse : m_harvestActAt;
            int wait = BusyMs - (int)(Clock.UtcNow - since).TotalMilliseconds;
            if (wait > 0)
                Clock.Sleep(wait);
            string? why = Lifted(k, best.Serial, amount);
            m_harvestActAt = Clock.UtcNow;
            if (why != null)
                return "putting ore down: " + why;
            AssistItem? onto = pile;
            why = onto != null ? k.Drop(best.Serial, onto.Serial) : k.DropGround(best.Serial, dx, dy, z);
            if (why != null)
                return Stopped("putting ore down", best.Serial, why);
            Answered(k, AnswerMs);
            Clock.Sleep(DropSettleMs);
            bool back;
            lock (k.State.Gate)
            {
                AssistItem? now;
                back = k.State.Items.TryGet(best.Serial, out now) && PackTree(k).Contains(now.Container);
            }
            m_lastUse = Clock.UtcNow;
            if (back)
            {
                if (onto != null)
                {
                    pile = null;
                }
                else
                {
                    dx = x - 1;
                    dy = y;
                }
                continue;
            }
            Leave(best.Serial);  // autopickup leaves the stack where it was put
            if (onto != null)
                Leave(onto.Serial);
            int weight;
            lock (k.State.Gate)
                weight = k.State.Weight;
            Note("Loop: the bag is full; put the whole pile of " + amount + " " + UoTiles.Name(best.Graphic)
                + (onto != null ? " on the ore pile " + onto.Serial.ToString("X") + " at " + onto.X + "," + onto.Y
                : " beside you (" + dx + "," + dy + "," + z + ")") + "; weight now " + weight);
            return null;
        }
        return "putting ore down: the server sent the pile back into the pack twice";
    }
    // The loop's delay is too short for this server: RunUO cliloc 500118 (a skill too soon) and 500119 (an action too
    // soon), and the UOAIX shard's harvest swing (GameHarvest.codex:42, 2400 ms a mining swing per player).
    static readonly ImmutableArray<string> s_waitLines = [ "You must wait", "Wait before gathering again",
        "You have not yet recovered", "You are already casting" ];
    public static ImmutableArray<string> WaitLines => s_waitLines;

    // Pre-AOS strength requirements, RunUO Scripts/Items (OldStrReq, OldStrengthReq); shields have none before AOS.
    static readonly ImmutableArray<AdviceRow> s_advice = [
        Row("str10", 10, "a katana, kryss, scimitar or cutlass",
            "Weapons/Swords/Katana.cs, Kryss.cs, Scimitar.cs, Cutlass.cs", 0x13FF, 0x1401, 0x13B6, 0x1441),
        Row("str15", 15, "a leather tunic", "Armor/Leather/LeatherChest.cs", 0x13CC),
        Row("str20", 20, "a chain tunic, chain coif or ringmail tunic, a mace, maul or bow",
        "Armor/Chain/ChainChest.cs, Armor/Helmets/ChainCoif.cs, Armor/Ring/RingmailChest.cs, Weapons/Maces/Mace.cs, "
        + "Maul.cs, Weapons/Ranged/Bow.cs", 0x13BF, 0x13BB, 0x13EC, 0xF5C, 0x143B, 0x13B2),
        Row("str25", 25, "a broadsword or longsword", "Weapons/Swords/Broadsword.cs, Longsword.cs", 0xF5E, 0xF61),
        Row("str30", 30, "a war mace, spear, quarter staff or crossbow",
        "Weapons/Maces/WarMace.cs, SpearsAndForks/Spear.cs, Staves/QuarterStaff.cs, Ranged/Crossbow.cs", 0x1407, 0xF62,
        0xE89, 0xF50),
        Row("str35", 35, "studded leather, a war axe, executioner's axe, two handed axe or hammer pick",
        "Armor/Studded/StuddedChest.cs, Weapons/Axes/WarAxe.cs, ExecutionersAxe.cs, TwoHandedAxe.cs, "
        + "Maces/HammerPick.cs", 0x13DB, 0x13B0, 0xF45, 0x1443, 0x143D),
        Row("str40", 40,
        "bone armor, a plate helm or plate arms, a battle axe, large battle axe, war hammer, bardiche, viking sword "
        + "or heavy crossbow",
        "Armor/Bone/BoneChest.cs, Armor/Helmets/PlateHelm.cs, Armor/Plate/PlateArms.cs, Weapons/Axes/BattleAxe.cs, "
        + "LargeBattleAxe.cs, Maces/WarHammer.cs, PoleArms/Bardiche.cs, Swords/VikingSword.cs, "
        + "Ranged/HeavyCrossbow.cs", 0x144F, 0x1412, 0x1410, 0xF47, 0x13FB, 0x1439, 0xF4D, 0x13B9, 0x13FD),
        Row("str45", 45, "a double axe or halberd", "Weapons/Axes/DoubleAxe.cs, PoleArms/Halberd.cs", 0xF4B, 0x143E),
        Row("str60", 60, "a platemail breastplate", "Armor/Plate/PlateChest.cs", 0x1415),
        new() { Key = "mining65", Skill = 45, At = 650,
        Text = "you can now mine colored ore, on a server that has it (65.0 Mining)",
        Source = "RunUO Scripts/Engines/Harvest/Mining.cs:87" },
    ];
    public static ImmutableArray<AdviceRow> Advice => s_advice;
    static AdviceRow Row(string key, int at, string what, string files, params int[] graphics)
    {
        return new AdviceRow { Key = key, Strength = true, At = at,
            Text = "you can now wear or wield " + what + " (strength " + at + ")",
            Source = "RunUO Scripts/Items/" + files, Graphics = graphics };
    }

    // RunUO Server/Mobile.cs:4243 ActionDelay 500 ms between item actions; a lift the server accepts sends nothing, so
    // one is taken as accepted when no 0x27 refusal has come in LiftSettleMs.
    int LiftSettleMs => Ms("lift-settle");
    int AnswerMs => Ms("answer");
    int CursorMs => Ms("cursor");
    // RunUO Server/Mobile.cs:4272: a lift reaches 2 tiles.
    public const int LiftReach = 2;
    // RunUO Scripts/Items/Skill Items/Magical/Potions: heal 0xF0C, cure 0xF07, refresh 0xF0B, agility 0xF08, strength
    // 0xF09, night sight 0xF06; every one needs a free hand (BasePotion.cs:75, 102). Poison and explosion are not drunk
    // here.
    static readonly ImmutableArray<int> s_drinkable = [ 0xF0C, 0xF07, 0xF0B, 0xF08, 0xF09, 0xF06 ];
    public static ImmutableArray<int> Drinkable => s_drinkable;
    // RunUO Scripts/Items/Shields: bronze 0x1B72 to metal 0x1B7B.
    public static bool IsShield(int g)
    {
        return g >= 0x1B72 && g <= 0x1B7B;
    }
    // RunUO Scripts/Items/Resources/Reagents: black pearl, bloodmoss, garlic, ginseng, mandrake root, nightshade,
    // sulfurous ash, spiders' silk.
    public const string DefaultReagents = "F7A,F7B,F84,F85,F86,F88,F8C,F8D";
    static readonly string[][] s_countGroups = {
        new[] { "ore", "19B7,19B8,19B9,19BA", "harvest" }, new[] { "ingots", "1BF2", "create" },
            new[] { "logs", "1BDD", "harvest" }, new[] { "boards", "1BD7", "create" },
        // Damian, 2026-10-18: "to harvest resources add fish, those food ribs, feathers" (1.25 TILEDATA: fish
        // 09CC-09CF, raw ribs 09F1, cooked ribs 09F2, feathers 1BD1-1BD2).
        new[] { "fish", "9CC,9CD,9CE,9CF", "harvest" }, new[] { "ribs", "9F1,9F2", "harvest" },
        new[] { "feathers", "1BD1,1BD2", "harvest" },
        // Damian, 2026-10-19: "to harvest resources add wool and bails of cotton", "there seems to be many kinds of
        // these things too". The 1998 scripts: a sheep sheared alive drops 0DF8 (bladed.m), its corpse sheared or
        // harvested gives 101F (bladed.m, universal_harvest.m); a cotton plant picked (harvest.m) and a vendor's
        // raw_cotton (defines 3577) are both 0DF9; raw cotton 0DEF is named but nothing makes it; flax bundles
        // 1A9C-1A9D spin to thread (spinning.m).
        new[] { "wool", "DF8,101F", "harvest" }, new[] { "cotton", "DF9,DEF", "harvest" },
        new[] { "flax", "1A9C,1A9D", "harvest" },
        // Damian, 2026-10-18: "add empty bottles and blank scrolls and empty scrollbooks". A spellbook's art is the
        // same empty or full, so every spellbook counts.
        new[] { "blank scrolls", "EF3", "create" }, new[] { "spellbooks", "EFA,E3B", "create" },
        new[] { "bandages", "E21", "combat" }, new[] { "arrows", "F3F", "combat" }, new[] { "bolts", "1BFB", "combat" },
        // The gems, by their 1.25 tile data names (TILEDATA.MUL 0F0F..0F30).
        new[] { "amber", "F25", "gems" }, new[] { "amethyst", "F16,F17,F22,F2E", "gems" },
        new[] { "citrine", "F15,F23,F24,F2C", "gems" },
        new[] { "diamond", "F26,F27,F28,F29,F30", "gems" }, new[] { "emerald", "F10,F2F", "gems" },
        new[] { "ruby", "F13,F14,F1A,F1C,F1D,F2A,F2B", "gems" },
        new[] { "sapphire", "F11,F12,F19,F1F", "gems" }, new[] { "star sapphire", "F0F,F1B,F21", "gems" },
        new[] { "tourmaline", "F18,F1E,F20,F2D", "gems" },
        // The potions by the 1998 colour for each (yellow heal, orange cure, red refresh, blue agility, white strength,
        // black night sight, green poison, purple explosion), and the empty bottles alchemy fills.
        new[] { "heal", "F0C", "potions" }, new[] { "cure", "F07", "potions" }, new[] { "refresh", "F0B", "potions" },
        new[] { "agility", "F08", "potions" }, new[] { "strength", "F09", "potions" },
        new[] { "night sight", "F06", "potions" },
        new[] { "poison", "F0A", "potions" }, new[] { "explosion", "F0D", "potions" },
        new[] { "empty bottles", "F0E", "potions" },
    };
    public static string[][] CountGroups => s_countGroups;
    // The resource panel's subgroups, in panel order (Damian, 2026-10-15: "group resources into subgroups: harvest,
    // create, reagents, and combat"; 2026-10-18: "under resources, add gems as subgroup", "add potions as subgroup").
    static readonly ImmutableArray<string> s_resourceGroups = [ "harvest", "create", "reagents", "gems", "potions",
        "combat" ];
    public static ImmutableArray<string> ResourceGroups => s_resourceGroups;
    // The subgroup an added item lands in: 1.25 art of ore, logs, fish, hides, wool, cotton and feathers is harvest;
    // bandages, arrows and bolts are combat; a reagent is reagents; a gem is gems; a potion or an empty bottle is
    // potions; anything else is create.
    public static string GroupOf(int g)
    {
        if (ReagentName(g) != null)
            return "reagents";
        if (g >= 0x0F0F && g <= 0x0F30)
            return "gems";
        if (g >= 0x0F06 && g <= 0x0F0E)
            return "potions";
        if ((g >= 0x19B7 && g <= 0x19BA) || (g >= 0x1BDD && g <= 0x1BE2) || (g >= 0x09CC && g <= 0x09CF) || g == 0x1078
            || g == 0x1079 || g == 0x0DF8 || g == 0x0DF9 || g == 0x101F || g == 0x0DEF || g == 0x1A9C || g == 0x1A9D
            || g == 0x1BD1 || g == 0x1BD2 || g == 0x09F1 || g == 0x09F2)
            return "harvest";
        if (g == 0x0E21 || g == 0x0F3F || g == 0x1BFB || (g >= 0x0F06 && g <= 0x0F0E))
            return "combat";
        return "create";
    }
    // What the UO window's title bar can show; hits, mana and stamina are on until the player turns them off.
    static readonly ImmutableArray<string> s_displayNames = [ "hits", "mana", "stamina", "strength", "dexterity",
        "intelligence", "weight", "gold", "armor" ];
    public static ImmutableArray<string> DisplayNames => s_displayNames;
    static readonly ImmutableArray<string> s_titleDefaults = [ "hits", "mana", "stamina" ];

    readonly AssistLog m_log; public readonly AssistSettings Cfg;
    readonly object m_actionGate = new();
    // One action at a time on the link (Hands.cs): every section takes the action gate through Act or TryAct.
    readonly Hands m_hands;
    // The action gate held and the previous action's answer waited out, for one step's actions (ActionScope).
    ActionScope Act(GameLink k)
    {
        return m_hands.Act(k);
    }
    // Act, or null when another action holds the gate: a background loop skips its turn.
    ActionScope? TryAct(GameLink k)
    {
        return m_hands.TryAct(k);
    }
    GameLink m_link = null!;
    // The loops' runner (AssistStructure.md stage 3): one loop at a time, its stop the runner's.
    readonly TaskRunner m_loops;
    // The loops' stop while none runs: set by a login and a stop, cleared by a loop's start and a manual action.
    internal volatile bool idleStop;
    bool stop => m_loops.Running ? m_loops.Stopped : idleStop;
    public volatile string Status = "idle", Sequence = "", LoopWords = "";
    public readonly List<string> Notes = new(); public long NotesAdded;
    ValueMap<string, int> m_seen = ValueMap<string, int>.Empty; readonly Lock m_seenSkillsGate = new();
    uint m_lastEquip; DateTime m_lastEquipAt;
    Set<uint> m_leave = Set<uint>.Empty; readonly Lock m_leaveGate = new();
    void Leave(uint serial)
    {
        lock (m_leaveGate)
            m_leave = m_leave.Add(serial);
    }
    bool Left(uint serial)
    {
        return m_leave.Contains(serial);
    }

    // Every time the assist reads or waits out (AssistTesting.md): the system clock live, a virtual one under test.
    public readonly IClock Clock;
    public AssistFeatures(AssistLog log, AssistSettings cfg, IClock? clock = null)
    {
        Clock = clock ?? SystemClock.Instance;
        m_loops = new TaskRunner(NewThread);
        m_weaveRunner = new TaskRunner(NewThread);
        m_loadoutRunner = new TaskRunner(NewThread);
        // A held-back auto-tune is noted "Timing REPORT" and told in the client in orange (HUES.MUL 0x002B).
        m_log = log;
        Cfg = cfg;
        Timings = new AssistTimings(cfg, () => Server, ServerFile, Note,
            r => { Note("Timing REPORT: " + r); GameLink? lk = m_link; if (lk != null
            && !lk.Closed) lk.Tell(r, 0x002B); }, Clock);
        m_hands = new Hands(() => m_link, Clock, Timings, () => BusyKey, m_actionGate);
        Tools = new ToolPlaces(s_professions, cfg, () => Server,
            () => { GameLink? k = m_link; return k == null ? null : Inv(k); }, ContainerInReach);
        m_lumberGather = new LumberGather(cfg, () => Server);
        m_featherGather = new FeatherGather(cfg, () => Server);
        m_mineGather = new MineGather(cfg, () => Server);
        Spawn("the pickup loop", PickupLoop);
        Spawn("the harvest loop", HarvestLoop);
    }
    // A thread or pool item of the assist's own: an exception in it is noted and ends it, where unhandled it ends the
    // relay.
    void Spawn(string name, Action run, bool pool = false)
    {
        Action safe = () => { try { run(); } catch (Exception e) { Note(name + " stopped: " + e.GetType().Name + " "
            + e.Message); } };
        if (pool && Clock is SystemClock)
        {
            ThreadPool.QueueUserWorkItem(_ => safe());
            return;
        }
        Thread t = NewThread(() => safe());
        t.Start();
    }
    // The assist's own loops end and its timer stops: a test's features, never the relay's (which end with the
    // process).
    volatile bool m_shutdown;
    public void Shutdown()
    {
        m_shutdown = true;
        Timer t = m_mountTimer;
        if (t != null)
            t.Dispose();
    }
    // A background thread of the assist's own; under a virtual clock an actor from before it starts, so time waits for
    // it.
    Thread NewThread(Action run)
    {
        VirtualClock? vc = Clock as VirtualClock;
        if (vc != null)
            vc.Join();
        Thread t = new(() => { try { run(); } finally { if (vc != null) vc.Leave(); } });
        t.IsBackground = true;
        return t;
    }

    public GameLink Link => m_link;
    public void Fault(string what)
    {
        Note("FAULT " + what.Replace("\r", " ").Replace("\n", " | "));
    }
    void Note(string text)
    {
        lock (Notes)
        {
            Notes.Add(Clock.UtcNow.ToLocalTime().ToString("HH:mm:ss") + " " + text);
            NotesAdded++;
            if (Notes.Count > 100)
                Notes.RemoveAt(0);
        }
        m_log.Line(m_link != null ? m_link.Id : 0, "--", "ASSIST " + text);
    }

    // A client-only journal line on every skill change, as UOAssist wrote one (Damian, 2026-10-15: "its generated chat
    // in the client on skill bumps"), green up and red down ("uoassist shows drops green. i'd like to see them red").
    // Hues in the 1.25 HUES.MUL: 0x0044 is green (mid colour rgb 16,168,0), 0x0026 red (rgb 168,16,0).
    public const int RiseHue = 0x0044, DropHue = 0x0026;
    ValueMap<int, int> m_lastSkills = ValueMap<int, int>.Empty; readonly Lock m_lastSkillsGate = new();
    void SkillNotices(GameLink k)
    {
        List<Tuple<string, int>> said = new();
        lock (k.State.Gate)
            lock (m_lastSkillsGate)
            {
                foreach (KeyValuePair<int, int> kv in k.State.Skills)
                {
                    int was;
                    bool known = m_lastSkills.TryGet(kv.Key, out was);
                    m_lastSkills = m_lastSkills.Set(kv.Key, kv.Value);
                    if (!known || was == kv.Value)
                        continue;
                    int i = kv.Key - 1;
                    string name = i >= 0 && i < AssistWindow.SkillNames.Length ? AssistWindow.SkillNames[i]
                        : "skill " + kv.Key;
                    int d = kv.Value - was;
                    said.Add(Tuple.Create("Your skill in " + name + " has " + (d > 0 ? "increased" : "decreased")
                        + " by " + (Math.Abs(d) / 10.0).ToString("0.0") + ". It is now "
                        + (kv.Value / 10.0).ToString("0.0") + ".", d > 0 ? RiseHue : DropHue));
                }
            }
        foreach (Tuple<string, int> s in said)
            k.Tell(s.Item1, s.Item2);
    }

    // At login (0x55) ask for the status and the skills, as the client does when its status bar and skill gump open
    // (Damian, 2026-10-15: "we need to invoke status to get that info", "we should do that automatically on login"):
    // uo1998 sends 0x11 and the 0x3A list only when asked. One request at a time, each after the last one's answer.
    void LoginRequests(GameLink k)
    {
        Clock.Sleep(1000);
        using (Act(k))
        {
            uint me;
            lock (k.State.Gate)
                me = k.State.Player;
            if (me == 0 || k.Closed)
                return;
            foreach (int type in new[] { 4, 5 })
            {
                string? why = k.Status(type, me);
                if (why != null)
                {
                    Note("Login " + (type == 4 ? "status" : "skills") + " request not sent: " + why);
                    return;
                }
                Answered(k, AnswerMs);
            }
        }
        if (OpenBagsAtLogin)
            OpenPackOnScreen(k);
    }
    // The pack and the bags in it opened on the client's screen at login (Damian, 2026-10-22: "auto-open backpack on
    // login. sometimes my pack is full of shirts and i can't see the bags underneath to open them"): plain
    // double-clicks the client sees, not Peek's held opens; never a book or a key ring. The client places each window
    // itself.
    public bool OpenBagsAtLogin
    {
        get => Cfg.Get(Server + "bags.open-at-login", "off") == "on";
        set => Cfg.Set(Server + "bags.open-at-login", value ? "on" : "off");
    }
    void OpenPackOnScreen(GameLink k)
    {
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        if (pack == 0 || k.Closed)
            return;
        BusyWait(clickedAt);
        using (Act(k))
        {
            string? w = k.Use(pack);
            clickedAt = Clock.UtcNow;
            if (w != null)
            {
                Note("Pack: opening the pack at login: " + w);
                return;
            }
        }
        Wait.Until(Clock, () => m_contents.ContainsKey(pack), AnswerMs, "the pack's contents", stepMs: 50);
        List<uint> bags = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == pack && PackBag(it.Graphic))
                    bags.Add(it.Serial);
        bags.Sort();
        foreach (uint b in bags)
        {
            if (k.Closed)
                return;
            BusyWait(clickedAt);
            using (Act(k))
            {
                string? w = k.Use(b);
                clickedAt = Clock.UtcNow;
                if (w != null)
                {
                    Note("Pack: opening " + b.ToString("X") + " at login: " + w);
                    return;
                }
            }
        }
        Note("Pack: opened at login with its " + bags.Count + " bag" + (bags.Count == 1 ? "" : "s"));
    }

    // A client journal line when the load first crosses 50, 75 and 95% of the 1998 carry limit, strength * 4 + 30
    // (Damian, 2026-10-15: "add a warning, for 50%, 75%, 95% to the client like with skill gains"), and LoadOverPct,
    // past the 1% grace over the limit; red only there (Damian, 2026-10-20: "367 of 382 is not worthy of red, only
    // orange"), orange only from 95% (Damian, 2026-10-21: 76% "should have been a yellow, not orange condition"). A
    // mark is said again only after the load has dropped below it. Hues in the 1.25 HUES.MUL: 0x0035 yellow (rgb
    // 168,168,0), 0x002B orange (168,64,0), 0x0026 red (168,16,0).
    public const int LoadOverPct = 101;
    static readonly ImmutableArray<int> s_loadMarks = [ 50, 75, 95, LoadOverPct ],
        s_loadHues = [ 0x0035, 0x0035, 0x002B, 0x0026 ];
    // A mark is said again only after the load stayed below it for RearmMs: uo1998 sends each dig's status twice, the
    // first without the ore stack (84 stones, then 224, at 16:29:59), and that dip re-said every mark.
    const int RearmMs = 5000;
    int m_loadSaid;  // the highest mark said since the load was last below it
    DateTime m_belowSince = DateTime.MaxValue;
    void WeightNotices(GameLink k)
    {
        int str, weight;
        lock (k.State.Gate)
        {
            str = k.State.Str;
            weight = k.State.Weight;
        }
        if (str <= 0)
            return;
        int max = str * 4 + 30, pct = weight * 100 / max, reached = 0;
        for (int i = 0; i < s_loadMarks.Length; i++)
            if (pct >= s_loadMarks[i])
                reached = i + 1;
        if (reached >= m_loadSaid)
        {
            m_belowSince = DateTime.MaxValue;
            if (reached > m_loadSaid)
                k.Tell("Your load is " + weight + " of " + max + " stones (" + pct + "%)"
                    + (reached == s_loadMarks.Length
                    ? ": past " + max + " every step costs a hundred times the stamina." : "."),
                    s_loadHues[reached - 1]);
            m_loadSaid = reached;
        }
        else if (m_belowSince == DateTime.MaxValue)
        {
            m_belowSince = Clock.UtcNow;
        }
        else if ((Clock.UtcNow - m_belowSince).TotalMilliseconds >= RearmMs)
        {
            m_loadSaid = reached;
            m_belowSince = DateTime.MaxValue;
        }
    }

    // Stamina warnings (Damian, 2026-10-15: "add a stamina warning for both you and mount"): the player's when it falls
    // below 25% and 10% of the maximum, said again after StaminaRearmMs back above; the mount's stamina stays on the
    // server, which says "Your horse is very fatigued." when it crosses 10% (OUO mobile.c CMobile_HandleStaminaDrain),
    // and that line is repeated in red. Hues: 0x002B orange, 0x0026 red (HUES.MUL, as the load warnings).
    static readonly ImmutableArray<int> s_staminaMarks = [ 25, 10 ], s_staminaHues = [ 0x002B, 0x0026 ];
    const int StaminaRearmMs = 5000;
    int m_staminaSaid; DateTime m_staminaAbove = DateTime.MaxValue;
    void StaminaNotices(GameLink k)
    {
        int stam, max;
        lock (k.State.Gate)
        {
            stam = k.State.Stam;
            max = k.State.MaxStam;
        }
        if (max <= 0)
            return;
        int pct = stam * 100 / max, reached = 0;
        for (int i = 0; i < s_staminaMarks.Length; i++)
            if (pct < s_staminaMarks[i])
                reached = i + 1;
        if (reached >= m_staminaSaid)
        {
            m_staminaAbove = DateTime.MaxValue;
            if (reached > m_staminaSaid)
                k.Tell("Your stamina is " + stam + " of " + max + " (" + pct + "%)"
                    + (reached == s_staminaMarks.Length ? ": rest before you run." : "."), s_staminaHues[reached - 1]);
            m_staminaSaid = reached;
        }
        else if (m_staminaAbove == DateTime.MaxValue)
        {
            m_staminaAbove = Clock.UtcNow;
        }
        else if ((Clock.UtcNow - m_staminaAbove).TotalMilliseconds >= StaminaRearmMs)
        {
            m_staminaSaid = reached;
            m_staminaAbove = DateTime.MaxValue;
        }
    }

    // Mount recovery (Damian, 2026-10-15: "i need to know when the mount is recovered"). The server never sends the
    // mount's stamina, so the assist keeps an estimate by the OUO rules: each 250 ms tick takes 18 off a mobile's
    // stamina-loss counter and each 200 taken off is a point regained, a ridden mount included (npc.c
    // CNPCHash_RegenMoveTick runs regen before its parent test); a step ridden adds the step cost / 10 to the mount's
    // counter, 100 walking and 400 running (player.c HandleMovement, mobile.c HandleStaminaDrain). "Your horse is very
    // fatigued." puts the mount at 10% of its maximum and "Your horse is too fatigued to move." at 0. The maximum is
    // 75, the top of a horse's dexterity (ServUO Horse.cs 56 to 75), and the counter starts a full 200 owed: the notice
    // can be late, never early.
    const int MountMaxStam = 75, MountTickMs = 250, MountRegen = 18, MountUnit = 200, MountHue = 0x0044;
    readonly Lock m_mountGate = new();
    long m_mountOwed = -1; DateTime m_mountAt, m_mountTold; Timer m_mountTimer = null!;
    void MountAnchor(GameLink k, int stam)
    {
        long eta;
        bool quiet;
        lock (m_mountGate)
        {
            // "Too fatigued to move" comes once per refused step: said once, and the estimate restarted at each.
            quiet = m_mountOwed >= 0 && (Clock.UtcNow - m_mountTold).TotalMilliseconds < 10000;
            m_mountOwed = (long)(MountMaxStam - stam) * MountUnit + MountUnit;
            m_mountAt = Clock.UtcNow;
            eta = m_mountOwed / MountRegen * MountTickMs / 1000;
            if (!quiet)
                m_mountTold = m_mountAt;
        }
        if (!quiet)
        {
            k.Tell(stam == 0 ? "Your mount is out of stamina: at rest it recovers in about " + (eta + 59) / 60 + " min."
            : "Your mount is at 10% stamina or less: at rest it recovers in about " + (eta + 59) / 60 + " min.",
            s_staminaHues[1]);
        }
    }
    void MountSettle()
    {
        bool done = false;
        lock (m_mountGate)
        {
            if (m_mountOwed < 0)
                return;
            long ticks = (long)((Clock.UtcNow - m_mountAt).TotalMilliseconds / MountTickMs);
            m_mountOwed -= ticks * MountRegen;
            m_mountAt = m_mountAt.AddMilliseconds(ticks * MountTickMs);
            if (m_mountOwed <= 0)
            {
                m_mountOwed = -1;
                done = true;
            }
        }
        GameLink k = m_link;
        if (done && k != null)
            k.Tell("Your mount has recovered its stamina (estimated).", MountHue);
    }
    void MountStep(GameLink k, byte[] p)
    {
        bool riding = false;
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Value.Layer == 0x19 && kv.Value.Mobile == k.State.Player)
                {
                    riding = true;
                    break;
                }
        if (!riding)
            return;
        MountSettle();
        lock (m_mountGate)
            if (m_mountOwed >= 0)
                m_mountOwed += (p[1] & 0x80) != 0 ? 40 : 10;
    }

    // Pets and the mount (Damian, 2026-10-15: "add pet and mount happiness"). uo1998 tells a pet's mood only when it is
    // fed: food dropped on the pet, then the pet says "Your pet looks happier." (relay log 02:57:37, the pet wind
    // 055A47EB), so a pet is learnt from that line and its happiness shown as its feedings; the mount is the pet whose
    // double-click put an item on the player's mount layer (0x19) at once. Kept in the settings ("pets": serial, name,
    // last fed ticks, feedings, 1 for the mount; | between pets).
    // History (Damian, 2026-10-15: "keep history for each pet of its feedings, kills, commands, etc"): every event a
    // line in pets-<server>.txt beside the settings (time, serial, name, event), the newest PetEventsKeep in memory for
    // the window. A command is the player's own speech that starts with the pet's name or "all"; a kill is a death
    // (0xAF) of a mobile the pet was last seen fighting (0x2F) within PetFightMs, the 1.25 packets, not yet seen on
    // uo1998's wire.
    public sealed class Pet
    {
        public uint Serial;
        public string Name = ""; public DateTime LastFed; public int Fed, Kills, Commands; public bool Mount;
    }
    public const int PetEventsKeep = 200, PetFightMs = 30000;
    readonly List<string> m_petEvents = new();
    ValueMap<uint, uint> m_foughtBy = ValueMap<uint, uint>.Empty;
    ValueMap<uint, DateTime> m_foughtAt = ValueMap<uint, DateTime>.Empty; readonly Lock m_foughtGate = new();
    uint m_mountItem;
    // The server as a file name part, and a file of this server's beside the settings: <name>-<server>.txt.
    string ServerSafe => Server.TrimEnd('.').Replace(':', '-').Replace(',', '-');
    public string ServerFile(string name)
    {
        return Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, name + "-" + ServerSafe + ".txt");
    }
    // A file read into memory once, line by line, under the caller's lock on what it fills; a line that throws ends the
    // read, as an unreadable file does.
    static void LoadOnce(ref bool loaded, string path, Action<string> line)
    {
        if (loaded)
            return;
        loaded = true;
        try
        {
            if (File.Exists(path))
                foreach (string l in File.ReadAllLines(path))
                    line(l);
        }
        catch { }
    }
    string PetsPath => ServerFile("pets");
    void PetEvent(Pet p, string what)
    {
        string line = Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\t" + p.Serial.ToString("X") + "\t"
            + (p.Name.Length > 0 ? p.Name : "-") + "\t" + what;
        lock (m_petEvents)
        {
            m_petEvents.Add(line);
            if (m_petEvents.Count > PetEventsKeep)
                m_petEvents.RemoveAt(0);
        }
        try
        {
            File.AppendAllText(PetsPath, line + "\r\n");
        }
        catch { }
    }
    // The newest events first, for the window.
    // A house or boat the atlas holds within StaleReach of the player that the server has not sent StaleMs after the
    // player came near is gone, and forgotten (Damian, 2026-10-18: "im standing on a beige dot", "there is no house";
    // the atlas kept multi 0x6C at 2479,71 after it was taken down). Run from the window's tick.
    public const int StaleReach = 12, StaleMs = 10000;
    ValueMap<uint, DateTime> m_nearSince = ValueMap<uint, DateTime>.Empty;
    public void ForgetGoneMultis()
    {
        GameLink k = m_link;
        if (k == null || k.Closed || Atlas == null)
            return;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        DateTime now = Clock.UtcNow;
        Set<uint> near = Set<uint>.Empty;
        foreach (AtlasPlace p in Atlas.Places())
        {
            if ((p.Kind != "House" && p.Kind != "Boat") || Tiles(p.X, p.Y, px, py) > StaleReach)
                continue;
            near = near.Add(p.Serial);
            if (UoMap.HasMulti(p.Serial))
            {
                m_nearSince = m_nearSince.Remove(p.Serial);
                continue;
            }
            DateTime since;
            if (!m_nearSince.TryGet(p.Serial, out since))
            {
                m_nearSince = m_nearSince.Set(p.Serial, now);
                continue;
            }
            if ((now - since).TotalMilliseconds >= StaleMs)
            {
                Atlas.Forget(p.Serial);
                m_nearSince = m_nearSince.Remove(p.Serial);
                Note("Map: the " + p.Kind.ToLowerInvariant() + " at " + p.X + "," + p.Y + " is gone; forgotten");
            }
        }
        foreach (uint s in m_nearSince.Keys)
            if (!near.Contains(s))
                m_nearSince = m_nearSince.Remove(s);
    }
    // The pack holds a material of the mill, spin and weave work: wheat sheaves, cotton or wool, thread or yarn.
    public bool WorkMaterialsHeld()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return false;
        return PackCount(k,
            g => s_sheaves.IndexOf(g) >= 0 || s_fibres.IndexOf(g) >= 0
            || s_strands.IndexOf(g) >= 0) > 0;
    }
    // Mounted, or a pet of the list in view (a follower): what the Pets and mount section has to show.
    public bool PetsAround()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return false;
        if (Riding(k))
            return true;
        List<uint> mine;
        lock (m_pets)
            mine = m_pets.ConvertAll(p => p.Serial);
        lock (k.State.Gate)
            foreach (uint s in mine)
                if (k.State.Mobiles.ContainsKey(s))
                    return true;
        return false;
    }
    public string[] PetHistory()
    {
        lock (m_petEvents)
        {
            if (m_petEvents.Count == 0)
            {
                try
                {
                    if (File.Exists(PetsPath))
                    {
                        string[] all = File.ReadAllLines(PetsPath);
                        for (int i = Math.Max(0, all.Length - PetEventsKeep); i < all.Length; i++)
                            m_petEvents.Add(all[i]);
                    }
                }
                catch { }
            }

            string[] a = m_petEvents.ToArray();
            Array.Reverse(a);
            return Array.ConvertAll(a, s => s.Replace("\t", "  "));
        }
    }
    readonly List<Pet> m_pets = new();
    uint m_usedMobile; DateTime m_usedMobileAt; bool m_petsLoaded;
    public const int MountUseMs = 3000;
    Pet? PetOf(uint s)
    {
        if (!m_petsLoaded)
        {
            m_petsLoaded = true;
            foreach (string e in Cfg.Get("pets", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] t = e.Split(',');
                if (t.Length < 5)
                    continue;
                try
                {
                    m_pets.Add(new Pet { Serial = Convert.ToUInt32(t[0], 16), Name = t[1],
                        LastFed = new DateTime(long.Parse(t[2])), Fed = int.Parse(t[3]), Mount = t[4] == "1",
                        Kills = t.Length > 5 ? int.Parse(t[5]) : 0, Commands = t.Length > 6 ? int.Parse(t[6]) : 0 });
                }
                catch { }
            }
        }
        foreach (Pet p in m_pets)
            if (p.Serial == s)
                return p;
        return null;
    }
    void SavePets()
    {
        Cfg.Set("pets",
            string.Join("|",
            m_pets.ConvertAll(p =>
            p.Serial.ToString("X") + "," + p.Name.Replace(",", " ").Replace("|", " ") + "," + p.LastFed.Ticks + ","
            + p.Fed + "," + (p.Mount ? "1" : "0") + "," + p.Kills + "," + p.Commands)));
    }
    // The player's own speech (0x03: type, hue, font, then text): a command to every pet named first, or to all.
    void PetCommand(byte[] p)
    {
        if (p.Length <= 8)
            return;
        string text = Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0').Trim();
        if (text.Length == 0)
            return;
        string lower = text.ToLowerInvariant();
        lock (m_pets)
        {
            PetOf(0);
            bool any = false;
            foreach (Pet pet in m_pets)
            {
                bool all = lower.StartsWith("all "), named = pet.Name.Length > 0
                    && lower.StartsWith(pet.Name.ToLowerInvariant() + " ");
                if (!all && !named)
                    continue;
                pet.Commands++;
                any = true;
                PetEvent(pet, "command: " + text);
            }
            if (any)
                SavePets();
        }
    }
    void PetPacket(GameLink k, byte[] p)
    {
        if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) != 0xFFFFFFFF
            && Encoding.ASCII.GetString(p, 44, p.Length - 44).IndexOf("pet looks happier",
            StringComparison.OrdinalIgnoreCase) >= 0)
        {
            uint s = U32(p, 3);
            string name = Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
            lock (m_pets)
            {
                Pet? pet = PetOf(s);
                if (pet == null)
                {
                    pet = new Pet { Serial = s };
                    m_pets.Add(pet);
                }
                if (name.Length > 0)
                    pet.Name = name;
                pet.LastFed = Clock.UtcNow.ToLocalTime();
                pet.Fed++;
                SavePets();
                PetEvent(pet, "fed: looks happier");
            }
            // Fed while dismounted, the mount is taken as rested: a stack of 9 or more food gives ServUO's 15 a piece
            // less 50, past a horse's 75 (BaseCreature.CheckFeed), and the feeding drops a stack of 10.
            bool riding;
            lock (k.State.Gate)
            {
                riding = false;
                foreach (AssistItem it in k.State.Items.Values)
                    if (it.Layer == MountLayer && it.Mobile == k.State.Player)
                        riding = true;
            }
            if (!riding)
                lock (m_mountGate)
                    m_mountOwed = -1;
        }
        // 0x2F fight: a byte, attacker, defender. 0xAF death: the dying mobile, its corpse.
        if (p[0] == 0x2F && p.Length >= 10)
            lock (m_foughtGate)
            {
                if (m_foughtBy.Count > 2000)
                {
                    m_foughtBy = m_foughtBy.Clear();
                    m_foughtAt = m_foughtAt.Clear();
                }
                uint a = U32(p, 2), d = U32(p, 6);
                m_foughtBy = m_foughtBy.Set(d, a);
                m_foughtAt = m_foughtAt.Set(d, Clock.UtcNow);
                m_foughtBy = m_foughtBy.Set(a, d);
                m_foughtAt = m_foughtAt.Set(a, Clock.UtcNow);
            }
        if (p[0] == 0xAF && p.Length >= 5)
        {
            uint dead = U32(p, 1);
            uint by = 0;
            DateTime at;
            lock (m_foughtGate)
                if (m_foughtBy.TryGet(dead, out by)
                    && (!m_foughtAt.TryGet(dead, out at) || (Clock.UtcNow - at).TotalMilliseconds > PetFightMs))
                    by = 0;
            lock (m_pets)
            {
                Pet? victim = PetOf(dead);
                if (victim != null)
                    PetEvent(victim, "died");
                Pet? killer = by != 0 ? PetOf(by) : null;
                if (killer != null)
                {
                    killer.Kills++;
                    SavePets();
                    PetEvent(killer, "kill: " + MobileName(k, dead));
                    KillAt(k, dead, MobileName(k, dead), killer.Name, p.Length >= 9 ? U32(p, 5) : 0);
                }
            }
        }
        // The mount item leaving the player: dismounted.
        if (p[0] == 0x1D && p.Length >= 5 && m_mountItem != 0 && U32(p, 1) == m_mountItem)
        {
            m_mountItem = 0;
            lock (m_pets)
            {
                PetOf(0);
                foreach (Pet pet in m_pets)
                    if (pet.Mount)
                        PetEvent(pet, "dismounted");
            }
        }
        // 0x2E: item, graphic, a byte, layer (8), mobile (9): the mount layer on the player just after a double-click
        // on a mobile.
        if (p[0] == 0x2E && p.Length >= 13 && p[8] == 0x19 && m_usedMobile != 0
            && (Clock.UtcNow - m_usedMobileAt).TotalMilliseconds < MountUseMs)
        {
            uint me;
            lock (k.State.Gate)
                me = k.State.Player;
            if (U32(p, 9) == me)
            {
                string? name;
                lock (k.State.Gate)
                    k.State.Names.TryGet(m_usedMobile, out name);
                lock (m_pets)
                {
                    Pet? pet = PetOf(m_usedMobile);
                    if (pet == null)
                    {
                        pet = new Pet { Serial = m_usedMobile, Name = name ?? "" };
                        m_pets.Add(pet);
                    }
                    foreach (Pet o in m_pets)
                        o.Mount = o == pet;
                    SavePets();
                    PetEvent(pet, "mounted");
                }
                m_usedMobile = 0;
                m_mountItem = U32(p, 1);
            }
        }
    }
    // One line a pet for the window: its name, the mount, its feedings, and the mount's estimated stamina.
    public string PetLines()
    {
        List<string> lines = new();
        lock (m_pets)
        {
            PetOf(0);
            foreach (Pet p in m_pets)
            {
                string fed = (p.Fed == 0 ? "not fed while watched"
                    : "fed " + (int)(Clock.UtcNow.ToLocalTime() - p.LastFed).TotalMinutes + " min ago ("
                    + p.LastFed.ToString("HH:mm") + "), " + p.Fed + (p.Fed == 1 ? " feeding" : " feedings")) + ", "
                    + p.Kills + " kills, " + p.Commands + " commands";
                lines.Add((p.Name.Length > 0 ? p.Name : p.Serial.ToString("X")) + (p.Mount ? " (your mount)" : "")
                    + ": " + fed + (p.Mount ? "; " + MountStamina() : ""));
            }
        }
        return lines.Count == 0 ? "No pet known yet: feed a pet and it is learnt from \"Your pet looks happier.\""
            : string.Join("\n", lines);
    }
    string MountStamina()
    {
        MountSettle();
        long owed;
        lock (m_mountGate)
            owed = m_mountOwed;
        if (owed < 0)
            return "stamina: no fatigue since the last full estimate";
        long stam = Math.Max(0, MountMaxStam - (owed + MountUnit - 1) / MountUnit);
        return "stamina about " + stam + " of " + MountMaxStam + " (estimated)";
    }

    public void Attach(GameLink k)
    {
        // A trip running on the connection being replaced ends with it.
        if (m_trip != null)
        {
            string? path;
            string? why = EndTrip(out path);
            if (why != null)
                Note("Trip: " + why);
        }
        m_link = k;
        idleStop = true;
        m_loops.Stop();
        m_loadSaid = 0;
        m_staminaSaid = 0;
        // A new connection knows no container's contents: the pack keeps its serial across a login, and the old list
        // made a pack never opened since look counted (Resources read low, 2026-10-18).
        lock (m_contentsGate)
        {
            m_contents = m_contents.Clear();
            m_contentAmounts = m_contentAmounts.Clear();
        }
        k.ServerPacket += p => PetPacket(k, p);
        k.ServerPacket += HouseWatchServer;
        k.ClientPacket += HouseWatchClient;
        k.ClientPacket += p => { if (p[0] == 0x03) PetCommand(p); };
        k.ClientPacket += p => LootClient(k, p);
        k.ServerPacket += p => TripPacket(k, p);
        k.ServerPacket += p => { if (p[0] == 0x1C
            && p.Length
            > 44) RuneRelabel(k, U32(p, 3), Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0')); };
        k.ClientPacket += p => { if (p[0] == 0x9A && p.Length > 15
            && U32(p, 11)
            == 1) RuneRelabel(k, U32(p, 3), Encoding.ASCII.GetString(p, 15, p.Length - 15).TrimEnd('\0')); };
        k.ServerPacket += DigResult;
        k.ServerPacket += MineBoxDrawn;
        k.ServerPacket += p => SeenServer(k, p);
        k.ServerPacket += p => { if (p[0] == 0x25) RestocksSeen(k); };
        lock (m_pendingDigs)
            m_pendingDigs.Clear();
        k.ClientPacket += p => { if (p[0] == 0x06
            && p.Length
            >= 5) { uint s = U32(p, 1)
            & 0x7FFFFFFF; bool mobile; lock (k.State.Gate) mobile = k.State.Bodies.ContainsKey(s)
            && s != k.State.Player; if (mobile) { m_usedMobile = s; m_usedMobileAt = Clock.UtcNow; } } };
        lock (m_mountGate)
            m_mountOwed = -1;
        if (m_mountTimer == null)
            m_mountTimer = new Timer(_ => MountSettle(), null, 1000, 1000);
        k.ClientPacket += p => { if (p[0] == 0x02 && p.Length >= 3) MountStep(k, p); };
        lock (m_seenSkillsGate)
            m_seen = m_seen.Clear();
        lock (m_lastSkillsGate)
            m_lastSkills = m_lastSkills.Clear();
        k.ServerPacket += p =>
        {
            if (p[0] == 0x27)
                m_hands.Refused(p);
            if (p[0] == 0x11 || p[0] == 0x3A)
                CheckAdvice(k);
            if (p[0] == 0x3A)
                SkillNotices(k);
            if (p[0] == 0x11)
                WeightNotices(k);
            if (p[0] == 0x11 || p[0] == 0xA3)
                StaminaNotices(k);
            if (p[0] == 0x2C && p.Length >= 2 && p[1] == 0)
                PlayerDied(k);
            if (p[0] == 0x1C && p.Length > 44)
            {
                string said = Encoding.ASCII.GetString(p, 44, p.Length - 44);
                if (said.IndexOf("horse is very fatigued", StringComparison.OrdinalIgnoreCase) >= 0)
                    MountAnchor(k, MountMaxStam / 10);
                else if (said.IndexOf("horse is too fatigued to move", StringComparison.OrdinalIgnoreCase) >= 0
                    || said.IndexOf("mount is too fatigued to move", StringComparison.OrdinalIgnoreCase) >= 0)
                    MountAnchor(k, 0);
            }
            if (p[0] == 0x55)
            {
                m_loginAt = Clock.UtcNow;
                ThreadPool.QueueUserWorkItem(_ => LoginRequests(k));
                ThreadPool.QueueUserWorkItem(_ => SessionTrip());
            }
            if (p[0] == 0xAF && p.Length >= 5)
                Died(k, U32(p, 1), p.Length >= 9 ? U32(p, 5) : 0);
            if (p[0] == 0x27)
                m_liftSerial = 0;
            People(k, p);
            if (p[0] == 0x1C)
                CheckStrengthRefusal(k);
        };
        k.ClientPacket += p => { if (p[0] == 0x3B
            || p[0] == 0x9F) Traded(k, p, true); else if (p[0]
            == 0x07) m_hands.HandLift(); else if (p[0] == 0x7D) m_handPickAt = Clock.UtcNow; };
        k.ServerPacket += BuyClosed;
        k.ClientPacket += p => HandMove(k, p);
        k.ServerPacket += p => CureWatch(k, p);
        // A spell's target (0x6C: type, cursor id, flags, then the clicked serial) is a fight as well: 6 of those 13
        // deaths were a cast (0x12) then a target on the creature.
        k.ClientPacket += p => { if (p[0] == 0x05
            && p.Length
            >= 5) Fought(U32(p, 1)); else if (p[0] == 0x6C && p.Length >= 11 && p[1] == 0
            && U32(p, 7) != 0) Fought(U32(p, 7)); };
        // A fight the server starts counts too (Damian, 2026-10-16: "the map is slow to update the kills i make"):
        // uo1998 sets the combatant (0xAA) and reports the player swinging (0x2F, attacker then defender) with no 0x05
        // from the client, so 13 deaths in a row (relay log 2026-10-16 16:27 to 16:37) made no kill mark.
        k.ServerPacket += p =>
        {
            if (p[0] == 0xAA && p.Length >= 5 && U32(p, 1) != 0)
            {
                Fought(U32(p, 1));
            }
            else if (p[0] == 0x2F && p.Length >= 10)
            {
                uint me;
                lock (k.State.Gate)
                    me = k.State.Player;
                if (U32(p, 2) == me)
                    Fought(U32(p, 6));
            }
        };
        if (PeopleReportPath.Length == 0)
        {
            string safe = ServerSafe;
            PeopleReportPath = Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports",
                "people-" + safe + "-" + Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmm") + ".md");
            Atlas = new AssistAtlas(Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!,
                "atlas-" + safe + ".txt"), Clock);
            m_reportTimer = new Timer(_ => { WritePeopleReport(); Atlas.Save(); }, null, ReportMs, ReportMs);
            Spawn("the look loop", LookLoop);
        }
        // The player's own skill use (0x12 type 0x24, "<skill> 0"): 1998 skills share one timer, so any skill counts.
        k.ClientPacket += p => { if (p[0] == 0x12 && p.Length > 4 && p[3] == 0x24) m_ownSkillAt = Clock.UtcNow; };
        if (Smith == null)
            Smith = new SmithBook(ServerFile("smithing"), Clock);
        if (Vendors == null)
            Vendors = new VendorBook(ServerFile("vendors"), Clock);
        if (Tailor == null)
            Tailor = new SmithBook(ServerFile("tailoring"), Clock);
        foreach (CraftKind c in Crafts)
            if (c.Book == null)
                c.Book = new SmithBook(ServerFile("craft-" + c.Name.ToLowerInvariant()), Clock);
        k.ServerPacket += p => SmithPacket(k, p);
        k.ClientPacket += p => { if (p[0] == 0x06
            && p.Length
            >= 5) { AssistItem? it = Item(k, U32(p, 1) & 0x7FFFFFFF); m_craftTool = it == null ? null
            : CraftOf(it.Graphic); m_craftToolAt = Clock.UtcNow; } };
        // The last pick decides: a pick with no metal cost (a tailoring menu, uo1998 04:19:45) is no smith build, so
        // its "You create" is not recorded as one.
        k.ClientPacket += p =>
        {
            if (p[0] != 0x7D || p.Length < 9)
                return;
            string? t = MenuItem(m_lastMenuSeen, (p[7] << 8) | p[8]);
            CraftKind? c = CraftNow();
            if (c != null && CraftCost(t!) > 0)
            {
                m_lastCraft = c;
                m_lastCraftItem = SmithBook.Display(t!);
                m_lastCraftAt = Clock.UtcNow;
            }
            else
            {
                m_lastCraft = null;
            }

            if (c == null && MetalCost(t!) > 0)
            {
                m_lastBuild = SmithBook.Display(t!);
                m_lastBuildAt = Clock.UtcNow;
            }
            else
            {
                m_lastBuild = null;
            }

            if (ClothCost(t!) > 0)
            {
                m_lastSewn = SmithBook.Display(t!);
                m_lastSewnAt = Clock.UtcNow;
            }
            else
            {
                m_lastSewn = null;
            }
        };
        k.ClientPacket += p => HandDigClient(k, p);
        k.ServerPacket += p => HandDigServer(k);
        k.ServerPacket += p => VendorCatalogServer(k, p);
        k.ClientPacket += p => { if (p[0] == 0x13
            && p.Length
            >= 5) { m_lastEquip = ((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8)
            | p[4]; m_lastEquipAt = Clock.UtcNow; } };
        lock (m_markersGate)
            m_markers = m_markers.Clear();
        k.HoldClient = p =>
            HoldLoginSelfUse(k, p) || HoldHouse(k, p) || HoldRecord(k, p) || HoldMarker(k, p) || HoldPotion(k, p)
            || HoldCast(k, p) || HoldPricer(k, p) || HoldLoadout(k, p) || HoldToolbag(k, p) || HoldBankBag(k, p)
            || HoldOverflow(k, p) || HoldHouseVendor(k, p) || HoldHouseRegister(k, p) || HoldStash(k, p)
            || HoldLayout(k, p) || HoldBankRune(k, p) || HoldCaveRune(k, p) || HoldLumber(k, p) || HoldFeathers(k, p)
            || VendorCatalogClient(p) || SeenLifted(k, p);
        k.HoldServer = p =>
            HoldDigChatter(p) || HoldSound(p) || HoldShop(p) || HoldPrompt(p) || HoldLabel(p) || HoldCostLine(p)
            || (p[0] == 0x27 && k.AssistLift);
        k.ServerPacket += p => CircuitWatch(k, p);
        // The assist speaks in the player's own hue and font.
        k.ClientPacket += p => { if (p[0] == 0x03
            && p.Length >= 8) { k.SpeechHue = (p[4] << 8) | p[5]; k.SpeechFont = (p[6] << 8) | p[7]; } };
        AssistGuard? guard = AssistGuard.Current;
        if (guard != null)
            k.ServerPacket += guard.Server;
    }

    // uo1998's system lines on every dig (relay log 2026-10-16 01:08:47 to 01:08:58, sender FFFFFFFF), kept off the
    // client's screen while a mining loop runs (Damian, 2026-10-16); the loop still reads them from the journal.
    static readonly ImmutableArray<string> s_digChatter = [ "You loosen some rocks", "You dig some",
        "Where do you wish to dig?" ];
    // A label (0x1C type 6) answering the assist's own single click (LabelsOf) stays off the client's screen until
    // shortly after the clicks (Damian, 2026-10-17: "a flood of \"unmarked recall rune\" to show up in my chat").
    ValueMap<uint, DateTime> m_looking = ValueMap<uint, DateTime>.Empty; readonly Lock m_lookingGate = new();
    bool HoldLabel(byte[] p)
    {
        if (p[0] != 0x1C || p.Length <= 44 || p[9] != 6)
            return false;
        uint s = U32(p, 3);
        DateTime until;
        lock (m_lookingGate)
        {
            if (!m_looking.TryGet(s, out until))
                return false;
            if (Clock.UtcNow <= until)
                return true;
            m_looking = m_looking.Remove(s);
            return false;
        }
    }
    bool HoldDigChatter(byte[] p)
    {
        LoopRun? run = m_currentRun;
        if (p[0] != 0x1C || p.Length <= 44 || run == null || !run.Name.StartsWith("Mining") || U32(p, 3) != 0xFFFFFFFF)
            return false;
        string text = Encoding.ASCII.GetString(p, 44, p.Length - 44);
        foreach (string c in s_digChatter)
            if (text.StartsWith(c))
                return true;
        return false;
    }

    // The buy list (0x74) runs in the reverse of its stock's order (0x3C): Jannelle's 43 entries end "Shuttle of thread
    // @3" for the stock's first item, 0x0FA0 x19, and begin "Sewing kit @3" for its last, 0x0F9D (uo1998 2026-10-17
    // 00:17:52); paired straight, a thread buy was written as "Sewing kit". The stock index of list entry i; -1 when
    // the two differ in length.
    static int StockIndex(int i, int listCount, int stockCount)
    {
        return listCount == stockCount ? stockCount - 1 - i : -1;
    }
}
