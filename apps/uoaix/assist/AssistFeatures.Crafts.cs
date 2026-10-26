#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- magery training ----------
    // Each cast is at the circle the skill gains on (pre-AOS RunUO MagerySpell.GetCastSkills: circle n gains between
    // 14.29 (n - 1) - 20 and 14.29 (n - 1) + 20 skill, the chance rising across it): the highest circle the skill has
    // passed the 40% point of, 14.29 (n - 1) - 4: circle 2 from 10.3, 3 from 24.6, 4 from 38.9, 5 from 53.2, 6 from
    // 67.5, 7 from 81.8 and 8 from 96.0 ((tenths + 40) * 7 / 1000 + 1; codex_run fact 951040c8). Each circle
    // casts a spell safe on oneself, and a target cursor it opens is aimed at the player. Mana is pre-AOS's per circle.
    // Each circle keeps its own delay, found by the loops' bisect on the server's "not yet recovered".
    // Each spell's reagents (the 1998 reagent table: black pearl 0F7A, blood moss 0F7B, garlic 0F84, ginseng 0F85,
    // mandrake root 0F86, nightshade 0F88, sulfurous ash 0F8C, spiders' silk 0F8D), counted in the loop's status
    // (Damian, 2026-10-18: "add reagent counters for the spell in that area once running").
    sealed class TrainSpell
    {
        public int Id; public string Name = null!; public bool Area; public int[] Regs = null!;
    }
    static readonly ImmutableArray<TrainSpell> s_trainSpells = [
        new() { Id = 6, Name = "Night Sight", Regs = new[] { 0xF8C, 0xF8D } },
            new() { Id = 9, Name = "Agility", Regs = new[] { 0xF7B, 0xF86 } },
        new() { Id = 17, Name = "Bless", Regs = new[] { 0xF84, 0xF86 } },
        new() { Id = 29, Name = "Greater Heal", Regs = new[] { 0xF84, 0xF85, 0xF86, 0xF8D } },
        new() { Id = 36, Name = "Magic Reflection", Regs = new[] { 0xF84, 0xF86, 0xF8D } },
        new() { Id = 44, Name = "Invisibility", Regs = new[] { 0xF7B, 0xF88 } },
        new() { Id = 53, Name = "Mana Vampire", Regs = new[] { 0xF7A, 0xF7B, 0xF86, 0xF8D } },
        new() { Id = 57, Name = "Earthquake", Area = true, Regs = new[] { 0xF7B, 0xF85, 0xF86, 0xF8C } },
    ];
    // The client's spell ids, 1 to 64 (OUO gm_names.h gm_spell_names), eight a circle.
    static readonly ImmutableArray<string> s_spellNames = [
        "Clumsy", "Create Food", "Feeblemind", "Heal", "Magic Arrow", "Night Sight", "Reactive Armor", "Weaken",
        "Agility", "Cunning", "Cure", "Harm", "Magic Trap", "Magic Untrap", "Protection", "Strength",
        "Bless", "Fireball", "Magic Lock", "Poison", "Telekinesis", "Teleport", "Unlock", "Wall of Stone",
        "Archcure", "Archprotection", "Curse", "Fire Field", "Greater Heal", "Lightning", "Mana Drain", "Recall",
        "Blade Spirits", "Dispel Field", "Incognito", "Magic Reflection", "Mind Blast", "Paralyze", "Poison Field",
        "Summon Creature",
        "Dispel", "Energy Bolt", "Explosion", "Invisibility", "Mark", "Mass Curse", "Paralyze Field", "Reveal",
        "Chain Lightning", "Energy Field", "Flamestrike", "Gate Travel", "Mana Vampire", "Mass Dispel", "Meteor Swarm",
        "Polymorph",
        "Earthquake", "Energy Vortex", "Resurrection", "Summon Air Elemental", "Summon Daemon",
        "Summon Earth Elemental", "Summon Fire Elemental", "Summon Water Elemental" ];
    // Round zero of "Magery (your spell)": the player's own cast (0x12 type 0x56) and the target the client answered
    // its cursor with (0x6C), or none when no cursor came within CastCursorMs.
    sealed class PickedCast
    {
        public int Id; public bool HasTarget, Ground; public uint Serial; public int X, Y, Z, Graphic;
    }
    PickedCast? PickCast(GameLink k, LoopKind l)
    {
        PickedCast? got = null;
        DateTime castAt = DateTime.MinValue;
        bool aimed = false;
        Action<byte[]> h = p =>
        {
            if (p[0] == 0x12 && p.Length > 5 && p[3] == 0x56)
            {
                int n = 0, i = 4;
                while (i < p.Length && p[i] >= (byte)'0' && p[i] <= (byte)'9')
                    n = n * 10 + (p[i++] - '0');
                if (n >= 1 && n <= 64)
                {
                    got = new PickedCast { Id = n };
                    castAt = Clock.UtcNow;
                    aimed = false;
                }
            }
            else if (p[0] == 0x6C && p.Length >= 19 && got != null && !aimed)
            {
                aimed = true;
                got.HasTarget = true;
                got.Ground = p[1] == 1;
                got.Serial = U32(p, 7);
                got.X = (p[11] << 8) | p[12];
                got.Y = (p[13] << 8) | p[14];
                got.Z = (sbyte)p[16];
                got.Graphic = (p[17] << 8) | p[18];
            }
        };
        k.ClientPacket += h;
        try
        {
            Status = "Loop " + l.Name
                + ": round zero: cast the spell you want to train, and aim it if it asks; the loop repeats it";
            DateTime by = Clock.UtcNow.AddMinutes(2);
            while (!stop && !k.Closed && Clock.UtcNow < by)
            {
                if (got != null && (aimed || (Clock.UtcNow - castAt).TotalMilliseconds > CastCursorMs))
                    return got;
                Clock.Sleep(100);
            }
            return null;
        }
        finally { k.ClientPacket -= h; }
    }
    string RegCounts(GameLink k, TrainSpell sp)
    {
        return string.Join(", ",
            Array.ConvertAll(sp.Regs, r => (ReagentName(r) ?? r.ToString("X4")) + " " + PackCount(k, g => g == r)));
    }
    static readonly ImmutableArray<int> s_circleMana = [ 4, 6, 9, 11, 14, 20, 40, 50 ];
    static readonly ImmutableArray<LoopKind> s_circlePace = [
        new() { Name = "Magery circle 1", DelayMs = 1500, Probe = true },
            new() { Name = "Magery circle 2", DelayMs = 2000, Probe = true },
        new() { Name = "Magery circle 3", DelayMs = 2500, Probe = true },
        new() { Name = "Magery circle 4", DelayMs = 3000, Probe = true },
        new() { Name = "Magery circle 5", DelayMs = 3500, Probe = true },
        new() { Name = "Magery circle 6", DelayMs = 4000, Probe = true },
        new() { Name = "Magery circle 7", DelayMs = 4500, Probe = true },
        new() { Name = "Magery circle 8", DelayMs = 5000, Probe = true },
    ];
    public static ImmutableArray<LoopKind> CirclePace => s_circlePace;
    public const int MagerySkill = 25;
    int ManaWaitMs => Ms("mana-wait");
    public static int CircleFor(int tenths)
    {
        return Math.Max(1, Math.Min(8, (Math.Max(0, tenths) + 40) * 7 / 1000 + 1));
    }
    void RunMagery(GameLink k, LoopKind l, int rounds, int untilSkill, LastTargets last)
    {
        string why = "done: " + rounds + " casts";
        int clean = 0;
        m_refusedInARow = 0;
        s_pacing = true;
        BeginRun(k, l, MagerySkill);
        // What it does, said at the start (Damian, 2026-10-18: "i just forgot how it works, that needs to be evident in
        // the ui messages"): no pick is asked for; the spell comes from the skill.
        OpenPackBags(k);  // reagents in a bag unopened since the login count none, and the loop would stop on them
        int skill0 = SkillOf(k, MagerySkill), c0 = CircleFor(skill0);
        LoopWords = l.Magery == 2
            ? "Loop " + l.Name
            + (": cast the spell you want to train once, and aim it if it asks (yourself, an item or the ground); the "
            + "loop repeats that cast at that target, at its circle's pace; waits for mana; ") + rounds + " casts"
            + (untilSkill > 0 ? ", or until Magery reaches " + (untilSkill / 10.0).ToString("0.0") : "") + ", or F2"
            : "Loop " + l.Name
            + ": a training loop, nothing to pick: it casts the spell of the circle your Magery trains on (now circle "
            + c0 + ", " + s_trainSpells[c0 - 1].Name + ", at Magery " + (skill0 / 10.0).ToString("0.0")
            + "), moving up a circle as the skill rises, aimed at you; waits for mana; " + rounds + " casts"
            + (untilSkill > 0 ? ", or until Magery reaches " + (untilSkill / 10.0).ToString("0.0") : "")
            + ", or F2. Spells by circle: " + string.Join(", ", s_trainSpells.ConvertAll(t => t.Name));
        Note(LoopWords);
        try
        {
            PickedCast? pick = null;
            if (l.Magery == 2)
            {
                if (last != null)
                {
                    pick = last.Cast;
                }
                else
                {
                    pick = PickCast(k, l);
                    if (pick == null)
                    {
                        why = stop || k.Closed ? "stopped" : "stopped: no spell was cast in 2 minutes";
                        return;
                    }
                    m_lastUse = Clock.UtcNow;
                    Keep(l, new LastTargets { Cast = pick, Until = untilSkill });
                }
                Note("Loop " + l.Name + ": repeating " + s_spellNames[pick.Id - 1] + " (circle "
                    + ((pick.Id - 1) / 8 + 1) + ")"
                    + (!pick.HasTarget ? ", no target"
                    : pick.Ground ? " at the ground " + pick.X + "," + pick.Y + "," + pick.Z
                    : " at " + pick.Serial.ToString("X")));
            }
            for (int round = 1; round <= (rounds = RoundsNow(rounds))
                && (untilSkill = UntilNow(untilSkill)) > int.MinValue; round++)
            {
                string? end = RoundStop(k);
                if (end != null)
                {
                    why = end;
                    return;
                }
                int skill = SkillOf(k, MagerySkill), c = pick != null ? (pick.Id - 1) / 8 + 1 : CircleFor(skill);
                TrainSpell sp = pick != null
                    ? new TrainSpell { Id = pick.Id, Name = s_spellNames[pick.Id - 1], Regs = new int[0] }
                    : s_trainSpells[c - 1];
                LoopKind pace = s_circlePace[c - 1];
                if (sp.Area && k.State.Guarded == true)
                {
                    why = "stopped: Magery reached circle 8, and Earthquake hurts everyone near you; the loop does "
                        + "not cast it inside a guarded zone";
                    return;
                }
                Status = "Loop " + l.Name + " (training, no pick): cast " + round + " of " + rounds + ": circle " + c
                    + ", " + sp.Name + " (Magery " + (skill / 10.0).ToString("0.0") + "); reagents: "
                    + RegCounts(k, sp);
                foreach (int r in sp.Regs)
                    if (PackCount(k, g => g == r) == 0)
                    {
                        why = "stopped: no " + (ReagentName(r) ?? r.ToString("X4")) + " in the pack for " + sp.Name;
                        return;
                    }
                // Mana first: pre-AOS cost per circle.
                DateTime manaBy = Clock.UtcNow.AddMilliseconds(ManaWaitMs);
                while (true)
                {
                    int mana;
                    lock (k.State.Gate)
                        mana = k.State.Mana;
                    if (mana >= s_circleMana[c - 1])
                        break;
                    if (stop || k.Closed)
                    {
                        why = stop ? "stopped" : "stopped: the connection closed";
                        return;
                    }
                    if (Clock.UtcNow > manaBy)
                    {
                        why = "stopped: mana stayed under " + s_circleMana[c - 1] + " for " + ManaWaitMs / 60000
                            + " min";
                        return;
                    }
                    Status = "Loop " + l.Name + ": waiting for mana " + mana + " of " + s_circleMana[c - 1] + " for "
                        + sp.Name;
                    Clock.Sleep(500);
                }
                DateTime due = m_lastUse.AddMilliseconds(Delay(pace));
                Outcome waited = Wait.Till(Clock, due, () => stop, () => k.Closed);
                if (!waited.Done)
                {
                    why = waited.Ending == Ending.Stopped ? "stopped" : "stopped: the connection closed";
                    return;
                }
                string? heard;
                using (Act(k))
                {
                    m_markJournal = new(k.State);
                    string? w = k.Cast(sp.Id, true);
                    if (w != null)
                    {
                        why = "stopped: " + w;
                        return;
                    }
                    m_lastUse = Clock.UtcNow;
                    // A targeted spell's cursor comes after its cast time, its words first: ended by the words,
                    // Invisibility's cursor went unanswered and every later cast was "You are already casting a spell."
                    // (2026-10-20 00:09:43).
                    bool targeted = pick != null ? pick.HasTarget : !(sp.Area || sp.Id == 36);
                    if (!targeted)
                        AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CursorMs), null!,
                            () => m_markJournal.Answers().Count > 0);
                    else
                        AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CastCursorMs), m_markJournal);
                    if (k.MacroCursor)
                    {
                        uint me;
                        int x, y, z, body;
                        lock (k.State.Gate)
                        {
                            me = k.State.Player;
                            x = k.State.X;
                            y = k.State.Y;
                            z = k.State.Z;
                            k.State.Bodies.TryGet(me, out body);
                        }
                        // The picked target as the client aimed it; the player himself wherever he stands now.
                        if (pick != null && pick.HasTarget && (pick.Ground || pick.Serial != me))
                            w = k.Target(pick.Ground, pick.Serial, pick.X, pick.Y, pick.Z, pick.Graphic);
                        else
                            w = k.Target(false, me, x, y, z, body);
                        if (w != null)
                        {
                            why = "stopped: aiming " + sp.Name + ": " + w;
                            return;
                        }
                    }
                    Answered(k, AnswerMs);
                    Clock.Sleep(300);
                    heard = m_markJournal.Has("insufficient mana") ? "mana" : null;
                    if (heard == null)
                        heard = Journal(k, pace);
                }
                if (heard == "mana" || heard == "again")
                {
                    round--;
                    continue;
                }
                if (heard != null)
                {
                    why = "stopped: " + heard;
                    return;
                }
                if (m_currentRun != null)
                    m_currentRun.Rounds = round;
                if (!m_refusedThisRound)
                    m_refusedInARow = 0;
                clean = m_refusedThisRound ? 0 : clean + 1;
                m_refusedThisRound = false;
                if (clean >= ProbeAfterRounds)
                {
                    Worked(pace);
                    clean = 0;
                }
                if (untilSkill > 0 && SkillOf(k, MagerySkill) >= untilSkill)
                {
                    why = "done: Magery reached " + (SkillOf(k, MagerySkill) / 10.0).ToString("0.0");
                    return;
                }
            }
        }
        catch (Exception e) { why = "stopped: " + e.Message; }
        finally { EndRun(k, l, why); }
    }

    void RunSmith(GameLink k, LoopKind l, int rounds, int untilSkill, LastTargets last)
    {
        string why = "done: " + rounds + " rounds";
        int made = 0, exceptional = 0, failed = 0;
        try
        {
            uint hammer;
            byte[]? lastMenu = null;
            List<byte[]> picks = new();
            string? item = null;
            int cost = 0;
            if (last != null)
            {
                hammer = last.Tool;
                picks = last.Picks;
                item = last.Item;
                cost = last.Cost;
                m_lastBuild = item;
                m_lastBuildAt = Clock.UtcNow;
            }
            else
            {
                Status = "Loop " + l.Name + ": round zero: double-click your smith's hammer and build the item once";
                byte[]? use = Watch(k, 0x06, 120000);
                if (use == null)
                {
                    why = "stopped: no tool was double-clicked";
                    return;
                }
                hammer = U32(use, 1);
                Action<byte[]> menus = p => { if (p[0] == 0x7C) lastMenu = p; };
                k.ServerPacket += menus;
                Heard mark0 = new(k.State);
                try
                {
                    while (picks.Count < SmithPicksMax)
                    {
                        Status = "Loop " + l.Name + ": round zero: pick from the menu";
                        byte[]? pick = Watch(k, 0x7D, 120000);
                        if (pick == null)
                        {
                            why = "stopped: no menu pick";
                            return;
                        }
                        int index = (pick[7] << 8) | pick[8];
                        if (index == 0)
                        {
                            why = "stopped: the menu was cancelled";
                            return;
                        }
                        picks.Add(pick);
                        item = MenuItem(lastMenu!, index);
                        cost = MetalCost(item!);
                        if (cost > 0)
                            break;
                    }
                }
                finally { k.ServerPacket -= menus; }
                if (cost == 0)
                {
                    why = "stopped: no pick named a metal cost (last: " + (item ?? "none") + ")";
                    return;
                }
                item = item!.StartsWith("Build ") ? item.Substring(6) : item;
                m_lastBuild = item;
                m_lastBuildAt = Clock.UtcNow;
                Status = "Loop " + l.Name + ": round zero: building " + item;
                string first = CraftResult(k, mark0);
                if (first.StartsWith("\""))
                {
                    why = "stopped: " + first;
                    return;
                }
                if (first == "exceptional")
                    exceptional++;
                else if (first == "made")
                    made++;
                else if (first == "failed")
                    failed++;
                Keep(l, new LastTargets { Tool = hammer, Picks = picks, Item = item, Cost = cost, Until = untilSkill });
            }
            LoopWords = "Loop " + l.Name + ": " + (last != null ? "the last targets, no round zero: " : "")
                + "double-click the hammer, pick " + picks.Count + " menus to " + item + " (" + cost
                + " ingots each), wait for the result; "
                + (l.Smith == 1 ? "until the pack holds fewer than " + cost + " ingots" : "until one is exceptional")
                + ", or " + rounds + " rounds, or F2";
            Note(LoopWords);
            if (l.Smith == 2 && exceptional > 0)
            {
                why = "done: round zero was exceptional";
                return;
            }
            BeginRun(k, l, l.Skill);
            for (int round = 1; round <= (rounds = RoundsNow(rounds))
                && (untilSkill = UntilNow(untilSkill)) > int.MinValue; round++)
            {
                string? end = RoundStop(k);
                if (end != null)
                {
                    why = end;
                    return;
                }
                int ingots = PackIngots(k);
                if (ingots < cost)
                {
                    why = "done: " + ingots + " ingots left, " + item + " takes " + cost;
                    return;
                }
                Status = "Loop " + l.Name + ": round " + round + " of " + rounds + ": " + item + " (" + ingots
                    + " ingots)";
                Heard mark;
                string? w = null;
                using (Act(k))
                {
                    mark = new(k.State);
                    k.ClaimMenu = true;
                    try
                    {
                        Action<byte[]> seen = p => { if (p[0] == 0x7C) lastMenu = p; };
                        lastMenu = null;
                        k.ServerPacket += seen;
                        try
                        {
                            k.Settle();
                            w = k.Use(hammer, false);
                            for (int i = 0; w == null && i < picks.Count; i++)
                            {
                                Wait.Until(Clock, () => lastMenu != null, AnswerMs, "the menu", closed: () => k.Closed,
                                    stepMs: 20);
                                byte[]? m = lastMenu;
                                lastMenu = null;
                                if (m == null)
                                {
                                    string? said = mark.Last();
                                    w = said != null ? "\"" + said + "\"" : "the hammer opened no menu";
                                    break;
                                }
                                byte[] r = (byte[])picks[i].Clone();
                                Array.Copy(m, 3, r, 1, 6);  // this menu's dialog serial and id
                                k.Settle();
                                w = k.Inject(r, "menu pick " + ((r[7] << 8) | r[8]), false);
                            }
                        }
                        finally { k.ServerPacket -= seen; }
                    }
                    finally { k.ClaimMenu = false; }
                    k.Settle();
                }
                if (w != null)
                {
                    why = "stopped: " + w;
                    return;
                }
                string got = CraftResult(k, mark);
                if (got.StartsWith("\"") || got.StartsWith("no result") || got == "stopped"
                    || got.StartsWith("the connection"))
                {
                    why = (got == "stopped" ? "" : "stopped: ") + got;
                    if (got == "stopped")
                        why = "stopped";
                    return;
                }
                if (got == "exceptional")
                    exceptional++;
                else if (got == "made")
                    made++;
                else
                    failed++;
                m_currentRun!.Rounds = round;
                if (l.Smith == 2 && got == "exceptional")
                {
                    why = "done: an exceptional " + item + " in round " + round;
                    return;
                }
                if (untilSkill > 0)
                {
                    int v;
                    lock (k.State.Gate)
                        k.State.Skills.TryGet(l.Skill + 1, out v);
                    if (v >= untilSkill)
                    {
                        why = "done: the skill reached " + (v / 10.0).ToString("0.0");
                        return;
                    }
                }
            }
        }
        catch (Exception e) { why = "stopped: " + e.Message; }
        finally
        {
            k.ClaimMenu = false;
            EndRun(k, l, why, " (" + made + " made, " + exceptional + " exceptional, " + failed + " failed)");
        }
    }

    // ---------- smelting ----------
    // One ore at a time (Damian, 2026-10-16: "given a target on the ground, take 1 of it and put it in backpack, then
    // smelt that one item. first iteration is manual"). Round zero is the player's: a lift from an ore pile on the
    // ground, the drop on a bag in the pack, a double-click on that ore and the forge picked. uo1998 keeps the pile's
    // serial on a part lift and gives the dropped piece a new one (relay log 2026-10-16 02:23:09 to 02:23:21), so the
    // single is the ore that appears in the bag after the drop; a pile gone under its serial is found again by ore art
    // and hue within a tile. A piece that lands as more than one is never used: its double-click would smelt the whole
    // stack. A single comes as amount 0 (0x25 at 02:36:54, relay log 2026-10-16), so only an amount above one is a
    // stack. uo1998 smelts without a line (02:36:58: the single deleted, an ingot stack of 2 into the pack), so the
    // result is the single gone, with the pack's ingots up (smelted) or not (failed). The UOAIX shard says "You smelt
    // the ore ..." (2026-10-15 15:28:26); RunUO cliloc 501990 "You burn away the impurities ..." is a failure. The
    // single still there after SmeltMs stops the loop with the last line heard.
    int SmeltMs => Ms("smelt");
    int SmeltSettleMs => Ms("smelt-settle");
    static readonly ImmutableArray<string> s_smeltLines = [ "You smelt" ],
        s_smeltFailLines = [ "burn away", "You fail" ];
    static readonly ImmutableArray<string> s_smeltPrompts = [ "Select the forge", "Select another pile" ];
    // Ingots in the pack, a stack sent as amount 0 counted as one.
    int SmeltIngots(GameLink k)
    {
        int n = 0;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Value.Graphic == IngotArt && inside.Contains(kv.Value.Container))
                    n += Math.Max(1, kv.Value.Amount);
        }
        return n;
    }
    static bool IsOre(int graphic)
    {
        return s_oreGraphics.IndexOf(graphic) >= 0;
    }
    static int Tiles(int x, int y, int x2, int y2)
    {
        return Math.Max(Math.Abs(x - x2), Math.Abs(y - y2));
    }
    // A wait line since mark: "again" to repeat after the bisect's new delay, or why the loop stops; null when none.
    string? SmeltWait(GameLink k, LoopKind l, Heard mark)
    {
        foreach (string j in mark.Answers())
        {
            foreach (string w in s_waitLines)
            {
                if (j.StartsWith(w, StringComparison.OrdinalIgnoreCase))
                {
                    string? why = Refused(l);
                    if (why != null)
                        return why;
                    m_lastUse = Clock.UtcNow;
                    m_refusedThisRound = true;
                    return "again";
                }
            }
        }

        return null;
    }
    string SmeltResult(GameLink k, LoopKind l, Heard mark, uint ore, int ingots)
    {
        DateTime start = Clock.UtcNow;
        DateTime until = start.AddMilliseconds(SmeltMs);
        string? last = null;
        // The smelting loop's stop, or the circuit's when the circuit smelts: the loop's is set whenever no loop runs,
        // and the mining circuit's first smelt ended "stopped" with its ingots on the way (2026-10-18 18:59:33).
        Func<bool> halted = () => Thread.CurrentThread == m_circuitThread ? circuitStop : stop;
        while (Clock.UtcNow < until && !halted() && !k.Closed)
        {
            foreach (string j in mark.Answers())
            {
                foreach (string s in s_smeltLines)
                    if (j.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        Seen("smelt", (int)(Clock.UtcNow - start).TotalMilliseconds);
                        return "smelted";
                    }
                foreach (string s in s_smeltFailLines)
                    if (j.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Seen("smelt", (int)(Clock.UtcNow - start).TotalMilliseconds);
                        return "failed";
                    }
                bool prompt = false;
                foreach (string s in s_smeltPrompts)
                    if (j.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                        prompt = true;
                if (!prompt)
                    last = j;
            }
            string? wait = SmeltWait(k, l, mark);
            if (wait != null)
                return wait;
            if (Item(k, ore) == null)
            {
                Seen("smelt", (int)(Clock.UtcNow - start).TotalMilliseconds);
                Clock.Sleep(SmeltSettleMs);
                if (Item(k, ore) == null)
                    return SmeltIngots(k) > ingots ? "smelted" : "failed";
            }
            Clock.Sleep(50);
        }
        return halted() ? "stopped"
            : k.Closed ? "the connection closed"
            : "no smelting result in " + SmeltMs / 1000 + " s: the ore " + ore.ToString("X") + " is still there"
            + (last != null ? "; the server said \"" + last + "\"" : "");
    }
    // A double-click or a lift leaves the player busy (BusyMs): uo1998 refused a lift 0.78 s after the loop's
    // double-click ("You must wait to perform another action." and 0x27 reason FF, relay log 2026-10-16 03:01:41.703 to
    // 03:01:42.682). The lift waits BusyMs after the last double-click and the double-click BusyMs after the lift; a
    // lift still refused as busy is tried again after BusyMs; with auto-tune on a refusal raises busy and lifts taken
    // lower it (Timings.Refused, Timings.Accepted).
    public const int BusyRetries = Hands.BusyRetries;
    DateTime clickedAt
    {
        get => m_hands.ClickedAt;
        set => m_hands.ClickedAt = value;
    }
    DateTime liftedAt => m_hands.LiftedAt;
    void BusyWait(DateTime since)
    {
        m_hands.BusyWait(since);
    }
    string? BusyLift(GameLink k, uint serial, int amount = 1)
    {
        return m_hands.BusyLift(k, serial, amount).AsReason();
    }
    // A drop or an equip sent by hand after its lift, checked put down (Hands.Landed; from, where the item lay before
    // the lift). Null, or Stopped(doing, ...) for a drop the server put back.
    string? Landed(GameLink k, uint serial, Spot from, string doing)
    {
        Outcome o = m_hands.Landed(k, serial, from);
        return o.Done ? null : Stopped(doing, serial, o.ToString());
    }
    static Spot? Where(GameLink k, uint serial)
    {
        return Hands.Where(k, serial);
    }
    // A stack lifted (BusyLift), dropped by drop and checked put down (Hands.Put). Null, or why not: "lifting <what>:
    // ..." for the lift, Stopped(doing, ...) for the drop and for a drop the server put back.
    string? MoveStack(GameLink k, uint serial, int amount, string what, string doing, Func<string> drop)
    {
        bool lifted;
        Outcome o = m_hands.Put(k, serial, amount, drop, out lifted);
        if (o.Done)
            return null;
        return lifted ? Stopped(doing, serial, o.ToString()) : "lifting " + what + ": " + o;
    }
    // Whole pile: after round zero the loop runs once for each ore left in the pile (the server sends a pile of one as
    // amount 0), in place of the rounds the player set.
    public bool WholePile
    {
        get => Cfg.Get("smelt.whole", "off") == "on";
        set => Cfg.Set("smelt.whole", value ? "on" : "off");
    }
    int PileLeft(GameLink k, AssistItem pile)
    {
        lock (k.State.Gate)
        {
            AssistItem? g;
            if (k.State.Ground.TryGet(pile.Serial, out g))
                return Math.Max(1, g.Amount);
            AssistItem? best = null;
            foreach (AssistItem c in k.State.Ground.Values)
                if (IsOre(c.Graphic) && c.Hue == pile.Hue && Tiles(c.X, c.Y, pile.X, pile.Y) <= 1
                    && (best == null || c.Amount > best.Amount))
                    best = c;
            return best == null ? 0 : Math.Max(1, best.Amount);
        }
    }
    // A failed smelt can leave the ore in the bag ("You burn away the impurities but are left with less useable
    // metal.", then the ore sent back into the bag, relay log 2026-10-16 03:05:02): that ore is smelted again, a retry
    // not counted as a round, up to MaxSmeltRetries times in a row.
    public const int MaxSmeltRetries = 10;
    bool StillInBag(GameLink k, uint ore, uint bag)
    {
        Clock.Sleep(SmeltSettleMs);
        AssistItem? it = Item(k, ore);
        return it != null && it.Container == bag && it.Amount <= SmeltTake(it.Graphic);
    }
    // The ore one smelt takes (Damian, 2026-10-25: "smart smelting 1 at a time, as the smallest ore size needs 2 per
    // smelt"): rundir ore.m makes metal / 2 ingots and answers "There is not enough metal-bearing ore in this pile to
    // make an ingot." under 1, so the smallest pile (19B7) is taken two at a time, every other one.
    public const int SmallOre = 0x19B7;
    public static int SmeltTake(int graphic)
    {
        return graphic == SmallOre ? 2 : 1;
    }
    void RunSmelt(GameLink k, LoopKind l, int rounds, int untilSkill, LastTargets last)
    {
        string why = "done: " + rounds + " rounds";
        int smelted = 0, failed = 0;
        try
        {
            // The pile as the lift found it: the server takes it off the ground before it sends the rest back.
            AssistItem? pile = null;
            uint bag, single = 0;
            Step forge;
            if (last != null)
            {
                AssistItem p0 = last.Pile;
                pile = new AssistItem { Serial = p0.Serial, Graphic = p0.Graphic, Hue = p0.Hue, X = p0.X, Y = p0.Y,
                    Z = p0.Z, Amount = p0.Amount };
                bag = last.Bag;
                forge = last.Forge.Copy();
                m_lastUse = Clock.UtcNow;
                m_refusedInARow = 0;
            }
            else
            {
                Action<byte[]> lifting = p =>
                {
                    if (p[0] != 0x07 || p.Length < 7)
                        return;
                    lock (k.State.Gate)
                    {
                        AssistItem? g;
                        if (k.State.Ground.TryGet(U32(p, 1), out g))
                            pile = new AssistItem { Serial = g.Serial, Graphic = g.Graphic, Hue = g.Hue, X = g.X,
                                Y = g.Y, Z = g.Z, Amount = g.Amount };
                    }
                };
                byte[]? lift;
                k.ClientPacket += lifting;
                try
                {
                    Status = "Loop " + l.Name + ": round zero: lift one ore from a pile on the ground";
                    lift = Watch(k, 0x07, 120000);
                }
                finally { k.ClientPacket -= lifting; }
                if (lift == null)
                {
                    why = "stopped: nothing was lifted";
                    return;
                }
                if (pile == null || !IsOre(pile.Graphic))
                {
                    why = "stopped: the lift was not from an ore pile on the ground";
                    return;
                }
                Status = "Loop " + l.Name + ": round zero: drop the ore on a bag in your pack";
                byte[]? drop = Watch(k, 0x08, 120000);
                if (drop == null)
                {
                    why = "stopped: the ore was not dropped";
                    return;
                }
                bag = U32(drop, 10);
                AssistItem? onto = Item(k, bag);
                bool inPack;
                lock (k.State.Gate)
                    inPack = PackTree(k).Contains(bag);
                if (!inPack)
                {
                    why = "stopped: the ore was dropped outside the pack; drop it on your backpack or a bag in it";
                    return;
                }
                if (onto != null && IsOre(onto.Graphic))
                {
                    why = "stopped: the ore was dropped onto ore; drop it on the bag itself";
                    return;
                }
                Status = "Loop " + l.Name + ": round zero: double-click the ore";
                byte[]? used = Watch(k, 0x06, 120000);
                if (used == null)
                {
                    why = "stopped: the ore was not double-clicked";
                    return;
                }
                clickedAt = Clock.UtcNow;
                Heard mark0 = new(k.State);
                int ingots0 = SmeltIngots(k);
                Status = "Loop " + l.Name + ": round zero: pick the forge";
                byte[]? t = Watch(k, 0x6C, 120000);
                if (t == null)
                {
                    why = "stopped: no forge was picked";
                    return;
                }
                forge = TargetStep(t);
                if (!forge.Ground && forge.Serial == 0)
                {
                    why = "stopped: the cursor was cancelled";
                    return;
                }
                m_lastUse = Clock.UtcNow;
                m_refusedInARow = 0;
                string first = SmeltResult(k, l, mark0, U32(used, 1), ingots0);
                if (first == "smelted")
                {
                    smelted++;
                }
                else if (first == "failed")
                {
                    failed++;
                }
                else if (first != "again")
                {
                    why = "stopped: round zero: " + first;
                    return;
                }
                Keep(l, new LastTargets { Pile = new AssistItem { Serial = pile.Serial, Graphic = pile.Graphic,
                    Hue = pile.Hue, X = pile.X, Y = pile.Y, Z = pile.Z, Amount = pile.Amount }, Bag = bag,
                    Forge = forge.Copy(), Until = untilSkill });
                if (first == "failed" && StillInBag(k, U32(used, 1), bag))
                    single = U32(used, 1);
            }
            Leave(pile.Serial);
            if (WholePile)
            {
                rounds = PileLeft(k, pile);
                if (rounds == 0)
                {
                    why = last != null ? "done: the ore pile is used up"
                        : "done: round zero took the last ore of the pile";
                    return;
                }
            }
            LoopWords = "Loop " + l.Name + ": " + (last != null ? "the last targets, no round zero: " : "")
                + "lift one ore (two of the smallest) from the pile at " + pile.X + "," + pile.Y + ", drop it in "
                + bag.ToString("X")
                + ", double-click it, " + forge.Words + ", wait for the result and " + Delay(l)
                + " ms from the last smelt; until the pile is gone, or " + rounds + " rounds"
                + (untilSkill > 0 ? ", or until Mining reaches " + (untilSkill / 10.0).ToString("0.0") : "")
                + ", or F2";
            Note(LoopWords);
            s_pacing = true;
            int clean = 0;
            BeginRun(k, l, MiningSkill);
            int retries = 0;
            for (int round = 1; round <= (rounds = RoundsNow(rounds))
                && (untilSkill = UntilNow(untilSkill)) > int.MinValue; round++)
            {
                string? end = RoundStop(k);
                if (end != null)
                {
                    why = end;
                    return;
                }
                m_refusedThisRound = false;
                Status = "Loop " + l.Name + ": round " + round + " of " + rounds + " (" + smelted + " smelted, "
                    + failed + " failed)";
                Heard? mark = null;
                int ingots = 0;
                string? w = null;
                using (Act(k))
                {
                    // A single left by a refused smelt is used again.
                    AssistItem? have = single != 0 ? Item(k, single) : null;
                    if (have == null || have.Container != bag || have.Amount > SmeltTake(have.Graphic))
                    {
                        single = 0;
                        AssistItem? src = null;
                        int px, py;
                        lock (k.State.Gate)
                        {
                            px = k.State.X;
                            py = k.State.Y;
                            AssistItem? g;
                            if (k.State.Ground.TryGet(pile.Serial, out g))
                                src = g;
                            else
                                foreach (AssistItem c in k.State.Ground.Values)
                                    if (IsOre(c.Graphic) && c.Hue == pile.Hue && Tiles(c.X, c.Y, pile.X, pile.Y) <= 1
                                        && (src == null || c.Amount > src.Amount))
                                        src = c;
                        }
                        if (src == null)
                        {
                            w = "done: the ore pile is used up";
                        }
                        else if (Tiles(src.X, src.Y, px, py) > LiftReach)
                        {
                            w = "stopped: the ore pile is " + Tiles(src.X, src.Y, px, py)
                                + " tiles away; a lift reaches " + LiftReach;
                        }
                        else
                        {
                            pile.Serial = src.Serial;
                            Leave(src.Serial);
                            Set<uint> before = Set<uint>.Empty;
                            lock (k.State.Gate)
                                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                                    if (kv.Value.Container == bag)
                                        before = before.Add(kv.Key);
                            int take = Math.Min(SmeltTake(src.Graphic), Math.Max(1, src.Amount));
                            w = BusyLift(k, src.Serial, take);
                            if (w != null)
                            {
                                w = "stopped: lifting " + take + " ore: " + w;
                            }
                            else if ((w = k.Drop(src.Serial, bag)) != null)
                            {
                                w = "stopped: " + Stopped("Smelting", src.Serial, w);
                            }
                            else
                            {
                                Answered(k, AnswerMs);
                                DateTime by = Clock.UtcNow.AddMilliseconds(AnswerMs);
                                AssistItem? got = null;
                                while (got == null && Clock.UtcNow < by && !k.Closed)
                                {
                                    lock (k.State.Gate)
                                        foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                                            if (kv.Value.Container == bag && !before.Contains(kv.Key)
                                                && IsOre(kv.Value.Graphic))
                                            {
                                                got = kv.Value;
                                                break;
                                            }
                                    if (got == null)
                                        Clock.Sleep(50);
                                }
                                if (got == null)
                                    w = "stopped: the lifted ore did not appear in " + bag.ToString("X")
                                        + " as an item of its own (stacked onto ore there?)";
                                else if (got.Amount > take)
                                    w = "stopped: the ore landed as a stack of " + got.Amount + " ("
                                        + got.Serial.ToString("X") + "), more than the " + take + " lifted";
                                else
                                    single = got.Serial;
                            }
                        }
                    }
                }
                if (w != null)
                {
                    why = w;
                    return;
                }
                // The server refuses a double-click 1.9 s after the last smelt's target (relay log 2026-10-16
                // 02:42:40.555 to 02:42:42.444, "You must wait to perform another action."); the lift and the drop at
                // 1.1 s were accepted. So the delay runs from the target to the next double-click, and the single is
                // fetched inside it.
                DateTime due = m_lastUse.AddMilliseconds(Delay(l));
                Outcome waited = Wait.Till(Clock, due, () => stop, () => k.Closed);
                if (!waited.Done)
                {
                    why = waited.Ending == Ending.Stopped ? "stopped" : "stopped: the connection closed";
                    return;
                }
                using (Act(k))
                {
                    if (Item(k, single) == null)
                        w = "stopped: the ore " + single.ToString("X") + " left the bag before its smelt";
                    if (w == null)
                    {
                        BusyWait(liftedAt);
                        mark = new(k.State);
                        ingots = SmeltIngots(k);
                        w = k.Use(single, true);
                        if (w != null)
                        {
                            w = "stopped: " + w;
                        }
                        else
                        {
                            m_lastUse = clickedAt = Clock.UtcNow;
                            DateTime until = Clock.UtcNow.AddMilliseconds(CursorMs);
                            AwaitCursor(k, until);
                            if (k.MacroCursor)
                                Seen("cursor", (int)(Clock.UtcNow - m_lastUse).TotalMilliseconds);
                            if (!k.MacroCursor)
                            {
                                k.Settle();
                                w = SmeltWait(k, l, mark);
                                if (w == null)
                                {
                                    string? said = mark.Last();
                                    w = "stopped: the ore opened no forge cursor"
                                        + (said != null ? "; the server said \"" + said + "\"" : "");
                                }
                                else if (w != "again")
                                {
                                    w = "stopped: " + w;
                                }
                            }
                            else
                            {
                                w = k.Target(forge.Ground, forge.Serial, forge.X, forge.Y, forge.Z, forge.Graphic);
                                if (w != null)
                                    w = "stopped: aiming at the forge: " + w;
                                else
                                    m_lastUse = Clock.UtcNow;
                            }
                        }
                    }
                }
                if (w == "again")
                {
                    round--;
                    continue;
                }
                if (w != null)
                {
                    why = w;
                    return;
                }
                string res = SmeltResult(k, l, mark!, single, ingots);
                if (res == "again")
                {
                    round--;
                    continue;
                }
                if (res == "smelted")
                {
                    smelted++;
                }
                else if (res == "failed")
                {
                    failed++;
                }
                else
                {
                    why = "stopped: " + res;
                    return;
                }
                if (res == "failed" && StillInBag(k, single, bag))
                {
                    if (++retries > MaxSmeltRetries)
                    {
                        why = "stopped: the ore " + single.ToString("X") + " failed " + retries + " times in a row";
                        return;
                    }
                    Note("Loop " + l.Name + ": the smelt failed and the ore " + single.ToString("X")
                        + " is still in the bag; smelting it again");
                    round--;
                    continue;
                }
                retries = 0;
                single = 0;
                m_currentRun!.Rounds = round;
                if (!m_refusedThisRound)
                    m_refusedInARow = 0;
                clean = m_refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds)
                {
                    Worked(l);
                    clean = 0;
                }
                if (untilSkill > 0 && SkillOf(k, MiningSkill) >= untilSkill)
                {
                    why = "done: Mining reached " + (SkillOf(k, MiningSkill) / 10.0).ToString("0.0");
                    return;
                }
            }
        }
        catch (Exception e) { why = "stopped: " + e.Message; }
        finally
        {
            EndRun(k, l, why, " (" + smelted + " smelted, " + failed + " failed)");
        }
    }

    // ---------- tailoring ----------
    // Damian, 2026-10-16: "start with tareting a bolt or pile of bolts, use one of that during the demo, it will
    // produce a pile of cloth and the garment if sucessful. use that pile of cloth until there isn't enough left over,
    // then start a new bolt from the same pile of bolts if possible". uo1998, relay log 2026-10-16 04:19:39 to
    // 04:22:09: a double-click on the sewing kit says "What cloth shall I use this sewing kit on?" and opens a cursor;
    // a bolt (0x0F95, a stack of 2) targeted is cut and its left-over cloth (0x1766) put in the pack ("You place the
    // left-over cloth pieces into your backpack"), stacked onto the pile already there (3 to 7, same serial); then item
    // menus whose dialog serial is the kit ("Choose a category.", "What kind of shirt?"). "You create the fancy shirt
    // and put it in your backpack." took 8 cloth, as the menu says (Damian, 2026-10-21: "its def 8"); "Tailoring
    // failed. Some of the cloth is ruined." took none. Round zero is the player's: the kit, the bolts or the cloth, the
    // picks. Each later round sews from the cloth pile the last bolt fed (the pack's cloth item that appeared or grew;
    // cloth of another kind may not stack), and cuts a bolt from the same stack when that pile is gone or answers with
    // no menu or no result: how uo1998 says "not enough cloth" was not seen.
    static readonly ImmutableArray<string> s_tailorFailLines = [ "failed", "ruined" ];
    static readonly ImmutableArray<string> s_tailorPrompts = [ "What cloth", "You place the left-over" ];
    static bool IsCloth(int g)
    {
        string n = UoTiles.Name(g).ToLowerInvariant();
        return n.Contains("cloth") && !n.Contains("bolt");
    }
    // The bolts of cloth' arts, not the name: "bolt" also names the Energy Bolt scroll (0x1F56), sewn at once the
    // spellbook was opened (2026-10-17 09:10:26, "Can't use a sewing kit on that.").
    static bool IsBolt(int g)
    {
        return g >= 0x0F95 && g <= 0x0F9C;
    }
    int TailorMs => Ms("tailor");
    ValueMap<uint, int> PackCloth(GameLink k)
    {
        ValueMap<uint, int> d = ValueMap<uint, int>.Empty;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (inside.Contains(kv.Value.Container) && IsCloth(kv.Value.Graphic))
                    d = d.Set(kv.Key, Math.Max(1, kv.Value.Amount));
        }
        return d;
    }
    // The cloth a bolt fed: the pack's cloth item that appeared or grew since before; 0 when none did.
    uint FedCloth(GameLink k, ValueMap<uint, int> before)
    {
        foreach (KeyValuePair<uint, int> kv in PackCloth(k))
        {
            int was;
            if (!before.TryGet(kv.Key, out was) || kv.Value > was)
                return kv.Key;
        }
        return 0;
    }
    string? TailorLast(GameLink k, Heard mark)
    {
        string? last = null;
        foreach (string j in mark.Answers())
        {
            bool prompt = false;
            foreach (string s in s_tailorPrompts)
                if (j.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    prompt = true;
            if (!prompt)
                last = j;
        }
        return last;
    }
    // "made", "exceptional", "failed", "again" (a wait line), or why there was no result.
    // The sample is timed from the last menu pick, the loop's or the player's own (handPickAt): round zero's wait began
    // before the player picked the menus by hand, and counting his clicking (2026-10-21 05:10:27.7 to 05:10:30.036) as
    // the server's 2515 ms asked tailor to go 2000 to 3900 ms; the server had answered 204 ms after the last pick.
    internal string TailorResult(GameLink k, LoopKind l, Heard mark, int ms)
    {
        DateTime start = Clock.UtcNow;
        DateTime until = start.AddMilliseconds(ms);
        Func<int> took = () => { DateTime from = start; if (m_sewPickedAt
            > from) from = m_sewPickedAt; if (m_handPickAt
            > from) from = m_handPickAt; return (int)(Clock.UtcNow - from).TotalMilliseconds; };
        while (Clock.UtcNow < until && !stop && !k.Closed)
        {
            foreach (string j in mark.Answers())
            {
                if (j.StartsWith("You create", StringComparison.OrdinalIgnoreCase))
                {
                    // No quality line to wait for: 1998 tailoring has none (tailor.m, "You create the ..." is the last
                    // line).
                    Seen("tailor", took());
                    return "made";
                }
                foreach (string f in s_tailorFailLines)
                    if (j.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Seen("tailor", took());
                        return "failed";
                    }
            }
            string? wait = SmeltWait(k, l, mark);
            if (wait != null)
                return wait;
            Clock.Sleep(50);
        }
        string? said = TailorLast(k, mark);
        return stop ? "stopped"
            : k.Closed ? "the connection closed"
            : "no result in " + ms / 1000 + " s" + (said != null ? "; the server said \"" + said + "\"" : "");
    }
    // One sewing under actionGate: the kit used, its cursor aimed at the material, the recorded picks replayed into
    // menus kept off the client's screen. Null when the last pick went out; "no cursor" or "no menu" when the server
    // answered without one; otherwise why the loop stops.
    string? Sew(GameLink k, uint kit, uint material, List<byte[]> picks)
    {
        return Sew(k, kit, material, picks.Count,
            (i, m) => { byte[] r = (byte[])picks[i].Clone(); Array.Copy(m, 3, r, 1, 6); return r; });
    }
    // A menu's pick by its text (the entry whose text begins so): 0x7D, the menu's dialog serial and id, the entry's
    // 1-based index, model and hue (uo1998 04:19:44, 7D 45ADB8BD 0011 0001 1517 0000); null when no entry matches. An
    // entry is model, hue, a length byte and the text.
    static byte[]? MenuPick(byte[] m, string text)
    {
        if (m == null || m.Length < 11)
            return null;
        int at = 10 + m[9];
        if (at >= m.Length)
            return null;
        int n = m[at];
        at++;
        for (int i = 1; i <= n && at + 5 <= m.Length; i++)
        {
            int len = m[at + 4];
            if (at + 5 + len > m.Length)
                return null;
            // An entry with no text is named by its art (uo1998's tinker menu lists its tools by picture only,
            // 23:40:49).
            string name = len > 0 ? Encoding.ASCII.GetString(m, at + 5, len) : UoTiles.Name((m[at] << 8) | m[at + 1]);
            if (name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                byte[] r = new byte[13];
                r[0] = 0x7D;
                Array.Copy(m, 3, r, 1, 6);
                r[7] = (byte)(i >> 8);
                r[8] = (byte)i;
                r[9] = m[at];
                r[10] = m[at + 1];
                r[11] = m[at + 2];
                r[12] = m[at + 3];
                return r;
            }
            at += 5 + len;
        }
        return null;
    }
    // picked: true once the pick answered names the item itself, so no further menu comes.
    // When the last menu pick went out: the server's busy time after a failed sew runs from it (a use 0.25 s after the
    // pick was refused, one 0.86 s after accepted, 2026-10-19 21:44:25 to 21:44:27).
    DateTime m_sewPickedAt = DateTime.MinValue, m_handPickAt = DateTime.MinValue;
    string? Sew(GameLink k, uint kit, uint material, int menus, Func<int, byte[], byte[]> pick,
        Func<bool>? picked = null)
    {
        // A material on the floor (homespun's bolts) is aimed at with its own height.
        AssistItem? mat = Item(k, material);
        int matZ = 0;
        if (mat == null)
        {
            k.State.Ground.TryGet(material, out mat);
            if (mat != null)
                matZ = mat.Z;
        }
        if (mat == null)
            return "the cloth " + material.ToString("X") + " is gone";
        byte[]? menu = null;
        Action<byte[]> seen = p => { if (p[0] == 0x7C) menu = p; };
        k.ClaimMenu = true;
        k.ServerPacket += seen;
        try
        {
            // The busy runs from the use reaching the server, not from its answer (about 110 ms later, relay log
            // 13:49).
            DateTime sent = Clock.UtcNow;
            string? w = k.Use(kit, true);
            if (w != null)
                return w;
            m_lastUse = sent;
            DateTime until = Clock.UtcNow.AddMilliseconds(CursorMs);
            AwaitCursor(k, until);
            if (!k.MacroCursor)
            {
                k.Settle();
                return "no cursor";
            }
            Seen("cursor", (int)(Clock.UtcNow - m_lastUse).TotalMilliseconds);
            w = k.Target(false, material, mat.X, mat.Y, matZ, mat.Graphic);
            if (w != null)
                return "aiming at the cloth: " + w;
            for (int i = 0; i < menus; i++)
            {
                Wait.Until(Clock, () => menu != null, AnswerMs, "the menu", closed: () => k.Closed, stepMs: 20);
                byte[]? m = menu;
                menu = null;
                if (m == null)
                    return "no menu";
                byte[] r = pick(i, m);
                if (r == null)
                    return "the menu has no entry for this item";
                k.Settle();
                w = k.Inject(r, "menu pick " + ((r[7] << 8) | r[8]), false);
                if (w != null)
                    return w;
                m_sewPickedAt = Clock.UtcNow;
                if (picked != null && picked())
                    break;
            }
            return null;
        }
        finally { k.ServerPacket -= seen; k.ClaimMenu = false; }
    }
    // Scraps (Damian, 2026-10-16: "just move the scrap pile the floor somewere", "just look for a tile that is
    // available like in mining"): cloth the server would not sew is lifted whole and put on a tile beside the player,
    // as DumpOre puts ore down: the four tiles around (south, west, east, north), those with nothing on them first, the
    // next tried when the pile comes back into the pack. Autopickup leaves it there. A lift of fewer than the pile
    // leaves the rest in the pack, which is lifted again the same way.
    // The tile of the last drop that left the pack is tried first while it is within ScrapReach (Damian, 2026-10-17:
    // "add to the remove scraps code a rememberance of where the last successful drop happened, and try there again if
    // possible"); the pack has an item limit as well as a weight limit, so scraps kept in it cost both.
    static readonly int[,] s_scrapTiles = { { 0, 1 }, { -1, 0 }, { 1, 0 }, { 0, -1 } };
    public const int ScrapReach = 2;
    int m_scrapsDropped; int[] m_lastScrapTile = null!;
    // Inside a house the trash goes outside its east wall, the open tile nearest the wall's middle, walked to and back
    // (Damian, 2026-10-20: "lets use the east side of the building, away from the front door and outside the house as
    // the trash can for this kind of object", "and also for scraps of cloth"): an unsmeltable ore dropped at the feet
    // lay on the door's tile, 2479,74, and blocked the way out.
    public const int TrashSearch = 4;
    int[]? HouseTrash(GameLink k)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        int[]? h = UoMap.HouseAround(px, py);
        if (h == null)
            return null;
        int tx = h[2] + 1, mid = (h[1] + h[3]) / 2;
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        for (int d = 0; d <= TrashSearch; d++)
        {
            foreach (int ty in new[] { mid + d, mid - d })
            {
                int lz = UoMap.LandStandZ(tx, ty);
                if (lz == int.MinValue)
                    continue;
                if (Open(k, tx, ty, lz, ref seen) != int.MinValue && UoMap.HouseAround(tx, ty) == null)
                    return new[] { tx, ty };
            }
        }

        return null;
    }
    void DropScraps(GameLink k, uint cloth)
    {
        string? end = null;
        int x = 0, y = 0, z = 0, px, py, pz;
        List<int[]> tiles = new();
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        int px0 = px, py0 = py;
        int[]? trash = HouseTrash(k);
        bool trashTrip = false;
        if (trash == null && UoMap.HouseAround(px, py) != null)
        {
            Note("Loop: " + cloth.ToString("X") + " stays in the pack: no open tile outside the house's east wall");
            return;
        }
        if (trash != null)
        {
            string? tw = WalkTo(trash[0], trash[1], 1, false, AnyFloor, false);
            if (tw != null)
            {
                Note("Loop: " + cloth.ToString("X") + " stays in the pack: walking to the trash at " + trash[0] + ","
                    + trash[1] + ": " + tw);
                return;
            }
            trashTrip = true;
            tiles.Add(trash);
        }
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            int[] last = m_lastScrapTile;
            if (trash == null && last != null && Tiles(last[0], last[1], px, py) <= ScrapReach)
                tiles.Add(new[] { last[0], last[1] });
            Set<long> taken = Set<long>.Empty;
            foreach (AssistItem g in k.State.Ground.Values)
                taken = taken.Add(((long)g.X << 16) | (uint)g.Y);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < s_scrapTiles.GetLength(0); i++)
                {
                    int tx = px + s_scrapTiles[i, 0], ty = py + s_scrapTiles[i, 1];
                    if (taken.Contains(((long)tx << 16) | (uint)ty) == (pass == 1)
                        && !tiles.Exists(t => t[0] == tx && t[1] == ty))
                        tiles.Add(new[] { tx, ty });
                }
            }
        }
        // A circuit's drop ends on the circuit's stop, a loop's on the loop's (a login sets the loop's stop).
        bool onCircuit = Thread.CurrentThread == m_circuitThread;
        for (int attempt = 0; attempt < tiles.Count && Item(k, cloth) != null && !(onCircuit ? circuitStop : stop)
            && !k.Closed; attempt++)
        {
            using (Act(k))
            {
                AssistItem? c = Item(k, cloth);
                if (c == null)
                    break;
                lock (k.State.Gate)
                    z = k.State.Z;
                int dx = tiles[attempt][0], dy = tiles[attempt][1];
                int dz = z;
                string? w = MoveStack(k, cloth, Math.Max(1, c.Amount), "the scrap cloth", "Putting scrap cloth down",
                    () => k.DropGround(cloth, dx, dy, dz)!);
                if (w != null)
                {
                    end = w;
                    break;
                }
                Leave(cloth);
                x = dx;
                y = dy;
                if (Item(k, cloth) == null)
                    m_lastScrapTile = new[] { dx, dy };
            }
        }
        if (end == null && Item(k, cloth) != null)
            end = "it came back into the pack from every tile around you";
        if (end == null)
            m_scrapsDropped++;
        Note("Loop: the scrap cloth " + cloth.ToString("X")
            + (end == null ? " is on the ground at " + x + "," + y : " stayed in the pack: " + end));
        if (trashTrip)
        {
            string? bw = WalkTo(px0, py0, 0, false, pz, false);
            if (bw != null)
                Note("Loop: back from the trash to " + px0 + "," + py0 + ": " + bw);
        }
    }
    // All cloth: the loop runs until the cloth and the bolts are used up, in place of the rounds the player set.
    public bool AllCloth
    {
        get => Cfg.Get("tailor.all", "off") == "on";
        set => Cfg.Set("tailor.all", value ? "on" : "off");
    }
    void RunTailor(GameLink k, LoopKind l, int rounds, int untilSkill, LastTargets last)
    {
        bool all = AllCloth;
        if (all)
            rounds = int.MaxValue;
        string count = all ? "until all the cloth is used" : rounds + " rounds";
        string why = "done: " + count;
        int made = 0, exceptional = 0, failed = 0, cut = 0;
        m_scrapsDropped = 0;
        try
        {
            // The pack and its bags opened first, as a circuit's start does: cloth in a bag unopened since the login
            // counts none.
            uint pack0;
            lock (k.State.Gate)
                pack0 = k.State.Backpack;
            bool known0;
            lock (m_contentsGate)
                known0 = pack0 != 0 && m_contents.ContainsKey(pack0);
            if (pack0 != 0 && !known0)
            {
                Peek(pack0);
                BusyWait(clickedAt);
            }
            OpenPackBags(k);
            uint kit, bolt, cloth;
            List<byte[]> picks;
            string? sewnItem, got = null;
            // The last targets: the kit and the menu picks; the pack's largest bolt stack and largest cloth pile now.
            if (last != null)
            {
                kit = last.Tool;
                picks = last.Picks;
                sewnItem = last.Item;
                AssistItem? big = null;
                foreach (AssistItem b in PackBolts(k))
                    if (big == null || b.Amount > big.Amount)
                        big = b;
                bolt = big != null ? big.Serial : 0;
                cloth = 0;
                int most = 0;
                foreach (KeyValuePair<uint, int> kv in PackCloth(k))
                    if (kv.Value > most)
                    {
                        most = kv.Value;
                        cloth = kv.Key;
                    }
                // The material targeted in round zero first, wherever it lies (Damian, 2026-10-21: "fix the last
                // targets button to accept non-pack targets").
                AssistItem? mat0 = last.Material != 0 ? ItemOrGround(k, last.Material) : null;
                if (mat0 != null && IsBolt(mat0.Graphic))
                    bolt = mat0.Serial;
                else if (mat0 != null && IsCloth(mat0.Graphic))
                    cloth = mat0.Serial;
                // None in the pack: the largest bolt stack and cloth pile on the ground within reach (Damian,
                // 2026-10-21: "use whatever you target, it doesn't have to be in pack").
                if (bolt == 0 && cloth == 0)
                {
                    int gx, gy;
                    lock (k.State.Gate)
                    {
                        gx = k.State.X;
                        gy = k.State.Y;
                    }
                    AssistItem? gb = null, gc = null;
                    lock (k.State.Gate)
                    {
                        foreach (AssistItem g in k.State.Ground.Values)
                        {
                            if (Tiles(g.X, g.Y, gx, gy) > VendorReach)
                                continue;
                            if (IsBolt(g.Graphic) && (gb == null || g.Amount > gb.Amount))
                                gb = g;
                            else if (IsCloth(g.Graphic) && (gc == null || g.Amount > gc.Amount))
                                gc = g;
                        }
                    }

                    bolt = gb != null ? gb.Serial : 0;
                    cloth = gc != null ? gc.Serial : 0;
                }
                if (bolt == 0 && cloth == 0)
                {
                    why = "stopped: no bolts or cloth in the pack or on the ground within " + VendorReach + " tiles";
                    return;
                }
            }
            else
            {
                // Round zero step by step (Damian, 2026-10-18: "when it starts the help needs to be more step by step
                // helpful like others in the circuits"): what the loop will do, then each step the player takes,
                // numbered.
                Note("Loop " + l.Name
                    + (": sew one item yourself and the loop repeats it, cutting your bolts first (their cloth stacks "
                    + "on one pile), then sewing that pile down, until the material runs out or F2. Your pack holds ")
                    + PackCloth(k).Count + " cloth piles and " + PackBolts(k).Count + " bolt stacks.");
                Status = "Loop " + l.Name
                    + ": step 1 of 3: double-click your sewing kit (in your pack or its tool bag)";
                Note(Status);
                byte[]? use = Watch(k, 0x06, 120000);
                if (use == null)
                {
                    why = stop ? "stopped" : "stopped: no sewing kit was double-clicked in 2 minutes (step 1)";
                    return;
                }
                kit = U32(use, 1) & 0x7FFFFFFF;
                ValueMap<uint, int> before = PackCloth(k);
                Status = "Loop " + l.Name + ": step 2 of 3: target the cloth to sew from, or a bolt to cut first";
                Note(Status);
                byte[]? t = Watch(k, 0x6C, 120000);
                if (t == null)
                {
                    why = stop ? "stopped" : "stopped: nothing was targeted in 2 minutes (step 2)";
                    return;
                }
                Heard mark0 = new(k.State);
                // In the pack or on the ground (Damian, 2026-10-21: "fix the loop to use whatever you target, it
                // doesn't have to be in pack").
                uint first = U32(t, 7);
                AssistItem? firstItem = first != 0 ? ItemOrGround(k, first) : null;
                if (firstItem == null)
                {
                    why = "stopped: target the bolts or the cloth";
                    return;
                }
                bool fromBolt = IsBolt(firstItem.Graphic);
                if (!fromBolt && !IsCloth(firstItem.Graphic))
                {
                    why = "stopped: the target, a " + UoTiles.Name(firstItem.Graphic) + ", is neither bolts nor cloth";
                    return;
                }
                // The picks, until the result.
                picks = new List<byte[]>();
                Action<byte[]> picked = p => { if (p[0] == 0x7D) lock (picks) picks.Add(p); };
                k.ClientPacket += picked;
                try
                {
                    Status = "Loop " + l.Name
                        + (": step 3 of 3: pick the item in the sewing menus (shirts, then Fancy shirt, say); the loop "
                        + "then sews it again and again");
                    Note(Status);
                    got = TailorResult(k, l, mark0, 120000);
                }
                finally { k.ClientPacket -= picked; }
                if (got != "made" && got != "exceptional" && got != "failed")
                {
                    why = "stopped: round zero: " + got;
                    return;
                }
                if (picks.Count == 0)
                {
                    why = "stopped: round zero made no menu pick";
                    return;
                }
                foreach (byte[] p in picks)
                    if (((p[7] << 8) | p[8]) == 0)
                    {
                        why = "stopped: a menu was cancelled in round zero";
                        return;
                    }
                if (got == "failed")
                    failed++;
                else if (got == "exceptional")
                    exceptional++;
                else
                    made++;
                // The item round zero's last pick named; the loop's own picks never reach the client, so each round
                // renews it for the tailoring book.
                sewnItem = m_lastSewn;
                Clock.Sleep(SmeltSettleMs);
                // Started from cloth, the pack's largest bolt stack follows it (the loop said "then from your bolts"
                // and ended with a bolt stack unused, 2026-10-19 12:36:41).
                AssistItem? packBolt = null;
                if (!fromBolt)
                    foreach (AssistItem b in PackBolts(k))
                        if (packBolt == null || b.Amount > packBolt.Amount)
                            packBolt = b;
                bolt = fromBolt ? first : packBolt != null ? packBolt.Serial : 0;
                cloth = fromBolt ? FedCloth(k, before) : first;
                if (fromBolt)
                    cut++;
                Keep(l, new LastTargets { Tool = kit, Picks = picks, Item = sewnItem, Until = untilSkill,
                    Material = first });
            }
            LoopWords = "Loop " + l.Name + ": " + (last != null ? "the last targets, no round zero: " : "")
                + "double-click the sewing kit, target "
                + (bolt != 0 ? "a bolt from " + bolt.ToString("X") + " while any is left, then " : "")
                + (cloth != 0 ? "the cloth " + cloth.ToString("X") : "the cloth the bolts fed") + ", pick "
                + picks.Count + " menus; " + count
                + (untilSkill > 0 ? ", or until Tailoring reaches " + (untilSkill / 10.0).ToString("0.0") : "")
                + ", or F2";
            Note(LoopWords);
            OpenPackBags(k);  // PackFull counts every bag's items, and an unopened bag's are unknown
            s_pacing = true;
            m_refusedInARow = 0;
            int clean = 0;
            bool failedLast = got == "failed";
            BeginRun(k, l, l.Skill);
            for (int round = 1; round <= (rounds = RoundsNow(rounds))
                && (untilSkill = UntilNow(untilSkill)) > int.MinValue; round++)
            {
                string? end = RoundStop(k);
                if (end != null)
                {
                    why = end;
                    return;
                }
                m_refusedThisRound = false;
                // The kit is sent again now and then (deleted and re-sent, 04:21:24.977 to 04:21:25.858): gone past
                // StackResendMs, it is used up.
                DateTime kitBy = Clock.UtcNow.AddMilliseconds(StackResendMs);
                while (Item(k, kit) == null && Clock.UtcNow < kitBy)
                    Clock.Sleep(20);
                // A worn-out kit is replaced from the tailoring toolbag first, then the rest of the pack, then the
                // tailoring toolbox when it is within reach (Damian, 2026-10-19: "add in toolbag awareness and sewing
                // kit switch to the loop. do not add buy sewing kit when empty").
                if (Item(k, kit) == null)
                {
                    uint spare = Tools.Spare(s_tailorTools);
                    // Opening the box is a double-click: the kit found in it waits out the busy time (the kit used 280
                    // ms after the box opened drew "You must wait to perform another action.", 2026-10-21 04:45:25).
                    if (spare == 0)
                    {
                        OpenToolbox(k, s_tailorTools);
                        spare = Tools.Spare(s_tailorTools);
                        BusyWait(clickedAt);
                    }
                    if (spare == 0)
                    {
                        why = "stopped: the sewing kit " + kit.ToString("X")
                            + (" is gone (worn out?) and no other kit is in the pack, its tailoring toolbag or a "
                            + "tailoring toolbox within reach");
                        return;
                    }
                    Note("Loop " + l.Name + ": the sewing kit " + kit.ToString("X") + " is gone; sewing on with "
                        + spare.ToString("X"));
                    kit = spare;
                }
                {
                    string? full = PackFull(k);
                    if (full != null)
                    {
                        why = "done: " + full;
                        return;
                    }
                }
                // The bolts first, the cloth after (Damian, 2026-10-19: "target the bolts all first -> they auto stack
                // together whereas the scrap at the end of shirt run do not"): a bolt cut puts its cloth on the one
                // pile, so the run leaves one scrap, not one a bolt (12 from one pile in 22.5 s against 4 a bolt in
                // 11.4 s, 13:53). A stack cut from is deleted and sent again one fewer, so a missing bolt is waited for
                // as the kit is.
                if (bolt != 0)
                {
                    Wait.Until(Clock, () => ItemOrGround(k, bolt) != null, StackResendMs, "the bolt");
                    if (ItemOrGround(k, bolt) == null)
                        bolt = 0;
                }
                bool fromCloth = bolt == 0 && cloth != 0 && ItemOrGround(k, cloth) != null;
                uint mat = fromCloth ? cloth : bolt;
                if (mat == 0 || ItemOrGround(k, mat) == null)
                {
                    why = "done: " + (bolt != 0 ? "the bolts are used up" : "the cloth is used up");
                    return;
                }
                Status = "Loop " + l.Name + ": round " + round + (all ? "" : " of " + rounds) + " from the "
                    + (fromCloth ? "cloth" : "bolts") + " (" + made + " made, " + exceptional + " exceptional, "
                    + failed + " failed)";
                // After a failure the next use also waits out the server's busy time from the sew's last menu pick
                // (sewPickedAt; Damian, 2026-10-19, "sounds right"): a failure answers about 0.2 s after that pick,
                // inside the 1998 server's busy, so a pace under it drew "You must wait" and a 2.2 s retry after every
                // failed sew (8 of 8, 18:55 to 19:03).
                DateTime due = SewDue(m_lastUse, Delay(l), BusyMs, failedLast, m_sewPickedAt, DateTime.MinValue);
                Outcome waited = Wait.Till(Clock, due, () => stop, () => k.Closed, stepMs: 10);
                if (!waited.Done)
                {
                    why = waited.Ending == Ending.Stopped ? "stopped" : "stopped: the connection closed";
                    return;
                }
                ValueMap<uint, int> pre = PackCloth(k);
                Heard mark;
                string? w;
                using (Act(k))
                {
                    mark = new(k.State);
                    if (sewnItem != null)
                    {
                        m_lastSewn = sewnItem;
                        m_lastSewnAt = Clock.UtcNow;
                    }
                    w = Sew(k, kit, mat, picks);
                }
                string? res = w == null ? TailorResult(k, l, mark, TailorMs)
                    : w == "no cursor" || w == "no menu" ? (SmeltWait(k, l, mark) ?? w) : null;
                if (w != null && res == null)
                {
                    why = "stopped: " + w;
                    return;
                }
                if (res == "again")
                {
                    round--;
                    continue;
                }
                if (res != "made" && res != "exceptional" && res != "failed")
                {
                    string? said = res == "stopped" ? null : TailorLast(k, mark);
                    // Cloth the server answered without sewing is scraps; a pile that only went quiet (lag) stays.
                    if (fromCloth && said != null && Item(k, cloth) != null)
                        DropScraps(k, cloth);
                    if (res != "stopped" && fromCloth && bolt != 0 && ItemOrGround(k, bolt) != null)
                    {
                        Note("Loop " + l.Name + ": the cloth " + cloth.ToString("X") + " gave "
                            + (res == "no menu" || res == "no cursor" ? res : "no result")
                            + (said != null ? " (\"" + said + "\")" : "") + "; cutting a bolt");
                        cloth = 0;
                        round--;
                        continue;
                    }
                    if (res == "stopped")
                    {
                        why = "stopped";
                        return;
                    }
                    if (fromCloth && said != null && (bolt == 0 || ItemOrGround(k, bolt) == null))
                    {
                        why = "done: all the cloth is used (the server said \"" + said + "\")";
                        return;
                    }
                    why = "stopped: the " + (fromCloth ? "cloth" : "bolt") + " gave " + res
                        + (said != null && res!.IndexOf(said) < 0 ? "; the server said \"" + said + "\"" : "");
                    return;
                }
                if (res == "failed")
                    failed++;
                else if (res == "exceptional")
                    exceptional++;
                else
                    made++;
                failedLast = res == "failed";
                if (!fromCloth)
                {
                    cut++;
                    Clock.Sleep(SmeltSettleMs);
                    uint fed = FedCloth(k, pre);
                    if (fed != 0)
                        cloth = fed;
                }
                m_currentRun!.Rounds = round;
                if (!m_refusedThisRound)
                    m_refusedInARow = 0;
                clean = m_refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds)
                {
                    Worked(l);
                    clean = 0;
                }
                if (untilSkill > 0 && SkillOf(k, l.Skill) >= untilSkill)
                {
                    why = "done: Tailoring reached " + (SkillOf(k, l.Skill) / 10.0).ToString("0.0");
                    return;
                }
            }
        }
        catch (Exception e) { why = "stopped: " + e.Message; }
        finally
        {
            k.ClaimMenu = false;
            EndRun(k, l, why,
                " (" + made + " made, " + exceptional + " exceptional, " + failed + " failed, " + cut + " bolts cut, "
                + m_scrapsDropped + " scrap piles put down)");
        }
    }

    // ---------- smithing value: what each build made and what NPCs pay for it ----------
    public SmithBook Smith = null!, Tailor = null!;
    // The other crafts' value (Damian, 2026-10-18: "expand it to cover all other skills too"): each craft's book is fed
    // as smithing's is, from the menus of the tool double-clicked last (by its name in the 1.25 tile data, the first
    // craft whose tool word it holds): an entry's cost where it shows one ("takes N <material>", or ", N <material>."),
    // the build picked and its result line, what NPCs offer and pay. Skill ids are the 1998 ones (inscribe.m 0x17,
    // alchemy.m 0x00, fletcher.m 0x08).
    public sealed class CraftKind
    {
        public string Name = null!, Unit = null!;
        public int Skill; public string[] Tools = null!; public SmithBook Book = null!;
    }
    public readonly CraftKind[] Crafts = {
        new() { Name = "Carpentry", Skill = 11, Unit = "board",
            Tools = new[] { "saw", "froe", "inshave", "scorp", "plane", "draw knife" } },
        new() { Name = "Tinkering", Skill = 37, Unit = "ingot", Tools = new[] { "tinker" } },
        new() { Name = "Bowcraft", Skill = 8, Unit = "log", Tools = new[] { "dagger", "knife" } },
        new() { Name = "Alchemy", Skill = 0, Unit = "reagent", Tools = new[] { "mortar" } },
        new() { Name = "Cartography", Skill = 12, Unit = "blank map", Tools = new[] { "mapmaker" } },
        new() { Name = "Inscription", Skill = 23, Unit = "scroll", Tools = new[] { "scribe" } },
        new() { Name = "Cooking", Skill = 13, Unit = "ingredient",
        Tools = new[] { "rolling pin", "skillet", "sifter" } },
    };
    volatile CraftKind? m_craftTool = null, m_lastCraft = null;
    DateTime m_craftToolAt, m_lastCraftAt; volatile string m_lastCraftItem = null!;
    CraftKind? CraftOf(int graphic)
    {
        string n = UoTiles.Name(graphic).ToLowerInvariant();
        foreach (CraftKind c in Crafts)
            foreach (string t in c.Tools)
                if (n.Contains(t))
                    return c;
        return null;
    }
    CraftKind? CraftNow()
    {
        CraftKind? c = m_craftTool;
        return c != null && (Clock.UtcNow - m_craftToolAt).TotalSeconds < 60 ? c : null;
    }
    static int CraftCost(string text)
    {
        if (text == null)
            return 0;
        int v;
        int at = text.IndexOf("takes ", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            string[] p = text.Substring(at + 6).Split(' ');
            return p.Length >= 2 && int.TryParse(p[0], out v) ? v : 0;
        }
        int comma = text.LastIndexOf(',');
        if (comma < 0)
            return 0;
        string[] q = text.Substring(comma + 1).Trim().Split(' ');
        return q.Length >= 2 && int.TryParse(q[0], out v) ? v : 0;
    }
    // A result line: "exceptional", "average" or "failed"; null when the line is not a result.
    static string? CraftLine(string text)
    {
        foreach (string f in new[] { "failed", "fail to", "ruined", "toss the failed", "You burn", "destroyed",
            "lost some" })
            if (text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return "failed";
        foreach (string s in new[] { "You create", "You make", "You put the", "You pour the completed",
            "You carefully" })
            if (text.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return text.IndexOf("exceptional", StringComparison.OrdinalIgnoreCase) >= 0 ? "exceptional" : "average";
        return null;
    }
    // Tailoring's cost: "Fancy shirt - takes 8 cloth." (uo1998 tailor menus, relay log 2026-10-16 04:19:44).
    public const int TailoringSkill = 34;
    static int ClothCost(string text)
    {
        if (text == null)
            return 0;
        int at = text.IndexOf("takes ", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return 0;
        string[] parts = text.Substring(at + 6).Split(' ');
        int v;
        return parts.Length >= 2 && parts[1].StartsWith("cloth", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[0], out v) ? v : 0;
    }
    volatile string? m_lastSewn = null; DateTime m_lastSewnAt;
    volatile byte[] m_lastMenuSeen = null!; volatile string? m_lastBuild = null; DateTime m_lastBuildAt;
    readonly List<object[]> m_recentAdds = new();  // serial, graphic, at
    object[]? m_pendingCraft = null; volatile string? m_pendingQuality = null;
    public const int SmithySkill = 7, CraftMatchMs = 2000;
    int QualityWaitMs => Ms("quality-wait");
    int LabelWaitMs => Ms("label-wait");
    void SmithPacket(GameLink k, byte[] p)
    {
        if (Smith == null)
            return;
        switch (p[0])
        {
            case 0x1D:
                if (p.Length >= 5)
                    SaleDeleted(U32(p, 1));
                break;
            case 0x54:
                if (p.Length >= 4 && ((p[2] << 8) | p[3]) == HammerSound)
                {
                    // The gap between two bangs of one build is what craft-quiet waits out.
                    DateTime now = Clock.UtcNow;
                    int gap = (int)(now - m_lastBang).TotalMilliseconds;
                    if (gap < CraftQuietMs * 3)
                        Seen("craft-quiet", gap);
                    m_lastBang = now;
                }
                break;
            case 0x7C:
                m_lastMenuSeen = p;
                {
                    CraftKind? craft = CraftNow();
                    for (int i = 1; i < 64; i++)
                    {
                        string? t = MenuItem(p, i);
                        if (t == null)
                            break;
                        if (craft != null)
                        {
                            int cc = CraftCost(t);
                            if (cc > 0)
                                craft.Book.Cost(t, cc);
                            continue;
                        }
                        int c = MetalCost(t);
                        if (c > 0)
                            Smith.Cost(t, c);
                        c = ClothCost(t);
                        if (c > 0 && Tailor != null)
                            Tailor.Cost(t, c);
                    }
                }
                break;
            // An item into a container: serial, graphic, a byte, amount, x, y, container, hue (uo1998 22:21:53,
            // 2545679DFE1411...).
            case 0x25:
                if (p.Length >= 20)
                    lock (m_recentAdds)
                    {
                        m_recentAdds.Add(new object[] { U32(p, 1), (p[5] << 8) | p[6], Clock.UtcNow });
                        if (m_recentAdds.Count > 12)
                            m_recentAdds.RemoveAt(0);
                    }
                break;
            case 0x1C:
                if (p.Length <= 44 || U32(p, 3) != 0xFFFFFFFF)
                    break;
                string text = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim();
                if (m_lastCraft != null && (Clock.UtcNow - m_lastCraftAt).TotalSeconds < 60)
                {
                    CraftKind c = m_lastCraft;
                    string? q = CraftLine(text);
                    if (q != null)
                    {
                        m_lastCraft = null;
                        c.Book.Crafted(0, m_lastCraftItem, q, SkillOf(k, c.Skill));
                    }
                    break;
                }
                if (Tailor != null && m_lastSewn != null && (Clock.UtcNow - m_lastSewnAt).TotalSeconds < 60)
                {
                    string sewn = m_lastSewn;
                    if (text.StartsWith("You create", StringComparison.OrdinalIgnoreCase))
                    {
                        // The garment, not the cloth pile sent again beside it (04:20:25.950: the pile 45ADDD06 then
                        // the shirt).
                        uint garment = 0;
                        DateTime now = Clock.UtcNow;
                        lock (m_recentAdds)
                        {
                            for (int i = m_recentAdds.Count - 1; i >= 0; i--)
                            {
                                object[] a = m_recentAdds[i];
                                if (!IsCloth((int)a[1]) && !IsBolt((int)a[1])
                                    && (now - (DateTime)a[2]).TotalMilliseconds < CraftMatchMs)
                                {
                                    garment = (uint)a[0];
                                    break;
                                }
                            }
                        }

                        long mark = k.State.JournalSeq - 1;
                        int skill = SkillOf(k, TailoringSkill);
                        Spawn("the tailoring result", () =>
                        {
                            Clock.Sleep(QualityWaitMs);
                            string q = "average";
                            foreach (string j in k.State.AnswersBetween(mark, long.MaxValue))
                                if (j.IndexOf("exceptional", StringComparison.OrdinalIgnoreCase) >= 0)
                                    q = "exceptional";
                            Tailor.Crafted(garment, sewn, q, skill);
                        }, true);
                    }
                    else if (text.IndexOf("Tailoring failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Tailor.Crafted(0, sewn, "failed", SkillOf(k, TailoringSkill));
                    }

                    break;
                }
                if (text.StartsWith("You create", StringComparison.OrdinalIgnoreCase))
                {
                    object[]? made = null;
                    DateTime now = Clock.UtcNow;
                    lock (m_recentAdds)
                    {
                        for (int i = m_recentAdds.Count - 1; i >= 0; i--)
                        {
                            object[] a = m_recentAdds[i];
                            if ((int)a[1] != IngotArt && (now - (DateTime)a[2]).TotalMilliseconds < CraftMatchMs)
                            {
                                made = a;
                                break;
                            }
                        }
                    }

                    if (made == null || m_lastBuild == null || (now - m_lastBuildAt).TotalSeconds >= 60)
                        break;
                    string item = m_lastBuild;
                    m_pendingCraft = new object[] { made[0], item, (int)made[1] };
                    m_pendingQuality = null;
                    Spawn("the smithing result", () => CraftDone(k), true);
                }
                else if (m_pendingCraft != null
                    && (text.IndexOf("quality", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("exceptional", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    m_pendingQuality = text;
                }
                else if (m_lastBuild != null && (Clock.UtcNow - m_lastBuildAt).TotalSeconds < 60)
                {
                    foreach (string f in s_craftFailLines)
                        if (text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Smith.Crafted(0, m_lastBuild, "failed", SkillOf(k, SmithySkill));
                            break;
                        }
                }

                break;
            // The sell list (OUO MakePacket_SHOP_SELL): what an NPC offers for each of the player's items, by its
            // quality.
            case 0x9E:
                if (p.Length < 9)
                    break;
                {
                    int n = (p[7] << 8) | p[8], at = 9;
                    // Tailored goods offered against carried, per item, for the vendor's demand.
                    ValueMap<string, int> offered = ValueMap<string, int>.Empty;
                    ValueMap<string, int> graphicOf = ValueMap<string, int>.Empty;
                    for (int i = 0; i < n && at + 14 <= p.Length; i++)
                    {
                        uint s = U32(p, at);
                        int graphic = (p[at + 4] << 8) | p[at + 5], price = (p[at + 10] << 8)
                            | p[at + 11], len = (p[at + 12] << 8) | p[at + 13];
                        if (at + 14 + len > p.Length)
                            break;
                        string name = Encoding.ASCII.GetString(p, at + 14, len).TrimEnd('\0');
                        string? q = Smith.QualityOf(s);
                        if (q != null || Smith.CostOf(name) > 0)
                            Smith.Offer(name, q ?? "unknown", price);
                        foreach (CraftKind c in Crafts)
                            if (c.Book != null && c.Book.CostOf(name) > 0)
                                c.Book.Offer(name, "unknown", price);
                        if (Tailor != null)
                        {
                            string? tq = Tailor.QualityOf(s);
                            if (tq != null || Tailor.CostOf(name) > 0)
                            {
                                Tailor.Offer(name, tq ?? "unknown", price);
                                offered = offered.Set(name, offered.Get(name, 0) + 1);
                                graphicOf = graphicOf.Set(name, graphic);
                            }
                        }
                        at += 14 + len;
                    }
                    if (offered.Count > 0 && Atlas != null)
                    {
                        uint vendor = U32(p, 3);
                        string vname = vendor.ToString("X");
                        foreach (AtlasNpc v in Atlas.Visible())
                            if (v.Serial == vendor)
                                vname = v.Name;
                        foreach (KeyValuePair<string, int> kv in offered)
                        {
                            int carried = 0, g = graphicOf.Get(kv.Key, 0);
                            lock (k.State.Gate)
                            {
                                Set<uint> inside = PackTree(k);
                                foreach (AssistItem it in k.State.Items.Values)
                                    if (it.Graphic == g && inside.Contains(it.Container))
                                        carried++;
                            }
                            Tailor!.Want(vname, kv.Key, kv.Value, Math.Max(carried, kv.Value));
                        }
                    }
                }
                break;
        }
    }
    // After the quality line had its chance: record the build, read the item's own label, and tell the player in the
    // game's text (Damian, 2026-10-15: "if we know we got max quality we should say that. if we know more details at
    // all than just exceptional, make that an output in the chat stream").
    void CraftDone(GameLink k)
    {
        Clock.Sleep(QualityWaitMs);
        object[]? c = m_pendingCraft;
        m_pendingCraft = null;
        if (c == null)
            return;
        uint serial = (uint)c[0];
        string? item = (string)c[1], line = m_pendingQuality;
        string q = line == null ? "average"
            : line.IndexOf("exceptional", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("higher than average", StringComparison.OrdinalIgnoreCase) >= 0 ? "exceptional"
            : line.IndexOf("below average", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("barely", StringComparison.OrdinalIgnoreCase) >= 0 ? "below average" : "average";
        int skill = SkillOf(k, SmithySkill);
        Smith.Crafted(serial, item, q, skill);
        string? label = null;
        ManualResetEventSlim got = new(false);
        Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) == serial
            && p[9] == 6) { label = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim(); got.Set(); } };
        k.ServerPacket += h;
        DateTime asked = Clock.UtcNow;
        try
        {
            for (int i = 0; i < 10 && k.Look(serial) != null; i++)
                Clock.Sleep(150);
            if (Clock.Wait(got, LabelWaitMs))
                Seen("label-wait", (int)(Clock.UtcNow - asked).TotalMilliseconds);
        }
        finally { k.ServerPacket -= h; }
        k.Tell("Made " + item + ": " + q + (q == "exceptional" ? ", the highest quality this server reports" : "")
            + (label != null && label.Length > 0 ? "; it reads \"" + label + "\"" : "") + " (smithing "
            + (skill / 10.0).ToString("0.0") + ")", q == "exceptional" ? 0x59 : 0x3B2);
    }
}
