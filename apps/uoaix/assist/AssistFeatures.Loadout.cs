#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- reagent loadout ----------
    // Damian, 2026-10-16: "its the reagent bag loadout tool. it takes two targets, first one is loadout bag, second is
    // storage. ... a configurable amount of each. start with 15 of each, plus 5 extra of recall regs". Only the
    // difference moves (Damian, 2026-10-19: "just move the exact amount the exact direction from pile to pile instead
    // of remove all and put all"): a reagent over its amount sends the excess onto the storage pile of its kind (or
    // into the storage bag when it holds none), a reagent under it takes the shortfall off the storage piles onto the
    // loadout's pile of its kind (or into the loadout bag), and one at its amount is not touched. Each move is one part
    // lift.
    public const uint LoadoutCursor = 0x52454147;  // "REAG", apart from PricerCursor
    public const int LoadoutBase = 15, LoadoutRecallExtra = 5;
    int LoadoutCursorGapMs => Ms("loadout-cursor-gap");
    static readonly ImmutableArray<int> s_recallReagents = [ 0xF7A, 0xF7B, 0xF86 ];
    public static ImmutableArray<int> RecallReagents => s_recallReagents;  // black pearl, bloodmoss, mandrake root
    // The recall reagents in the pack, its bags included, for the title bar's warning (RecallLow); null until the pack
    // has been read (PackStale), so an unread pack warns of nothing.
    public const int RecallLow = 10;
    public int[]? RecallRegCounts()
    {
        GameLink k = m_link;
        if (k == null || k.Closed || PackStale())
            return null;
        return s_recallReagents.ConvertAll(r => PackCount(k, g => g == r));
    }
    // The recall reagents the pack holds fewer than least of, its bags included: the one reagent check
    // (AssistTesting.md T3), at TripReagents for a trip and RecallLow for the warning and the bank stop it makes due.
    List<int> RecallRegsUnder(GameLink k, int least)
    {
        List<int> l = new();
        foreach (int r in s_recallReagents)
            if (PackCount(k, g => g == r) < least)
                l.Add(r);
        return l;
    }
    // A recall reagent under RecallLow in a pack that has been read: an unread pack warns of nothing.
    bool RecallRegsLow(GameLink k)
    {
        return RecallRegCounts() != null && RecallRegsUnder(k, RecallLow).Count > 0;
    }
    // The recall reagents first, then the rest by art: the Bags tab's first row is the recall set (Damian, 2026-10-19:
    // "put the mandrake on the first row with the other recall regs").
    public static int[] LoadoutReagents()
    {
        List<int> l = new(s_reagentNames.Keys);
        l.Sort((a, b) => { int ra = s_recallReagents.IndexOf(a) >= 0 ? 0
            : 1, rb = s_recallReagents.IndexOf(b) >= 0
            ? 0 : 1; return ra != rb ? ra - rb : a - b; });
        return l.ToArray();
    }
    public int LoadoutWant(int g)
    {
        return Cfg.GetInt("loadout." + g.ToString("X"),
            LoadoutBase + (s_recallReagents.IndexOf(g) >= 0 ? LoadoutRecallExtra : 0));
    }
    public void SetLoadoutWant(int g, int n)
    {
        Cfg.Set("loadout." + g.ToString("X"), Math.Max(0, n).ToString());
    }
    public volatile string LoadoutStatus = "Target the loadout bag, then the storage bag.";
    volatile int m_loadoutStep; volatile uint m_loadoutBag;
    public string? LoadoutTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        m_loadoutStep = 1;
        return LoadoutCursorOut(k, "Pick the loadout bag.");
    }
    // The loadout button: the refill at once when both bags are set, the two target cursors only when either is unset.
    public string? LoadoutGo()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        uint bag, storage;
        if (!uint.TryParse(Cfg.Get("loadout.bag", ""), NumberStyles.HexNumber, null, out bag)
            || !uint.TryParse(Cfg.Get("loadout.storage", ""), NumberStyles.HexNumber, null, out storage))
            return LoadoutTarget();
        return LoadoutStart(k, bag, storage);
    }
    // The loadout bag's count of the reagent; -1 when the bag is unset or not seen.
    public int LoadoutHave(int g)
    {
        GameLink k = m_link;
        uint bag;
        if (k == null || k.Closed || !uint.TryParse(Cfg.Get("loadout.bag", ""), NumberStyles.HexNumber, null, out bag))
            return -1;
        lock (k.State.Gate)
            if (!k.State.Items.ContainsKey(bag))
                return -1;
        return LoadoutCount(k, bag, g);
    }
    int LoadoutCount(GameLink k, uint bag, int g)
    {
        int n = 0;
        foreach (AssistItem st in Inside(k, bag, g))
            n += Math.Max(1, st.Amount);
        return n;
    }
    // A move put down: the loadout bag's count of the reagent at want after the drop's answer. A part lift's remainder
    // keeps its serial where it lay (uo1998, 2026-10-16 02:23:09), so the move is judged by the count, not by the
    // item's place.
    bool LoadoutLanded(GameLink k, uint bag, int g, int want)
    {
        return Wait.Until(Clock, () => LoadoutCount(k, bag, g) == want, AnswerMs, "the move", closed: () => k.Closed,
            stepMs: Hands.LandedStepMs).Done;
    }
    string? LoadoutCursorOut(GameLink k, string ask)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, LoadoutCursor);
        string? why = k.ToClient(p, "loadout cursor");
        LoadoutStatus = why == null ? ask : "No cursor: " + why;
        if (why != null)
            m_loadoutStep = 0;
        return why;
    }
    bool HoldLoadout(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != LoadoutCursor)
            return false;
        uint s = U32(p, 7);
        if (s == 0 || p[1] != 0)
        {
            m_loadoutStep = 0;
            LoadoutStatus = "Loadout cancelled: pick a bag, not the ground.";
            return true;
        }
        if (m_loadoutStep == 1)
        {
            m_loadoutBag = s;
            m_loadoutStep = 2;
            ThreadPool.QueueUserWorkItem(_ => { Clock.Sleep(LoadoutCursorGapMs); LoadoutCursorOut(k,
                "Now pick the storage bag."); });
            return true;
        }
        m_loadoutStep = 0;
        uint bag = m_loadoutBag;
        if (s == bag)
        {
            LoadoutStatus = "Loadout cancelled: the storage bag is the loadout bag.";
            return true;
        }
        // Kept for the refresh circuit, which refills the loadout at the bank with no cursor.
        Cfg.Set("loadout.bag", bag.ToString("X"));
        Cfg.Set("loadout.storage", s.ToString("X"));
        LoadoutStart(k, bag, s);
        return true;
    }
    List<AssistItem> Inside(GameLink k, uint bag, int graphic)
    {
        List<AssistItem> l = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == bag && (graphic < 0 ? ReagentName(it.Graphic) != null : it.Graphic == graphic))
                    l.Add(it);
        return l;
    }
    // While it runs the window's border is green (AssistWindow.Border).
    public volatile bool LoadoutRunning;
    // Its end said in full (Damian, 2026-10-22: "the status text should be explicit and say Done, Success: Stored 0,
    // ...").
    const string LoadoutFailed = "Done, Failed: ";
    // The loadout's runner (AssistStructure.md stage 3): one refill at a time, a second start refused.
    readonly TaskRunner m_loadoutRunner;
    string? LoadoutStart(GameLink k, uint bag, uint storage)
    {
        string? w = m_loadoutRunner.Start(new LoadoutTask(this, k, bag, storage));
        if (w != null)
            return w;
        LoadoutStatus = "Running: refilling the loadout...";
        return null;
    }
    sealed class LoadoutTask : ITask
    {
        readonly AssistFeatures m_f; readonly GameLink m_k; readonly uint m_bag, m_storage;
        public LoadoutTask(AssistFeatures f, GameLink k, uint bag, uint storage)
        {
            m_f = f;
            m_k = k;
            m_bag = bag;
            m_storage = storage;
        }
        public string Name => "the reagent loadout";
        public TaskTimings Timings => TaskTimingRegistry.Loadout;
        public string Run(TaskContext context)
        {
            try
            {
                m_f.LoadoutRun(m_k, m_bag, m_storage);
            }
            catch (Exception e) { m_f.Note("the reagent loadout stopped: " + e.GetType().Name + " " + e.Message); }
            return m_f.LoadoutStatus;
        }
    }
    void LoadoutRun(GameLink k, uint bag, uint storage)
    {
        LoadoutRunning = true;
        try
        {
            LoadoutSteps(k, bag, storage);
        }
        finally { LoadoutRunning = false; }
    }
    void LoadoutSteps(GameLink k, uint bag, uint storage)
    {
        s_busyTask = "loadout";
        using (Act(k))
        {
            DateTime opened = Clock.UtcNow;
            foreach (uint b in new[] { bag, storage })
            {
                HarvestGap();
                string? why = k.Use(b, false);
                opened = Clock.UtcNow;
                if (why != null)
                {
                    LoadoutStatus = LoadoutFailed + "could not open " + b.ToString("X") + ": " + why;
                    return;
                }
                Clock.Sleep(PricerOpenMs);
            }
            // A bag's double-click leaves the player busy, and the first lift came 0.83 s after the storage bag's and
            // was refused (reason 255, 2026-10-21 22:33:27): the loadout's busy is waited out from it.
            BusyWait(opened);
            int stored = 0, taken = 0;
            List<string> got = new();
            List<string> short_ = new();
            foreach (int g in LoadoutReagents())
            {
                int want = Math.Max(0, LoadoutWant(g)), have = LoadoutCount(k, bag, g);
                // Over: the excess off the loadout's piles onto storage.
                foreach (AssistItem st in Inside(k, bag, g))
                {
                    if (have <= want)
                        break;
                    int put = Math.Min(have - want, Math.Max(1, st.Amount));
                    List<AssistItem> onto = Inside(k, storage, g);
                    LoadoutStatus = "Running: storing " + put + " " + ReagentName(g) + "...";
                    HarvestGap();
                    string? why = Lifted(k, st.Serial, put);
                    if (why != null)
                    {
                        LoadoutStatus = LoadoutFailed + Stopped("loadout", st.Serial, why) + " (stored " + stored
                            + ", took " + taken + ")";
                        return;
                    }
                    why = k.Drop(st.Serial, onto.Count > 0 ? onto[0].Serial : storage);
                    if (why != null)
                    {
                        LoadoutStatus = LoadoutFailed + Stopped("loadout", st.Serial, why) + " (stored " + stored
                            + ", took " + taken + ")";
                        return;
                    }
                    Answered(k, AnswerMs);
                    if (!LoadoutLanded(k, bag, g, have - put))
                    {
                        LoadoutStatus = LoadoutFailed + "the server put " + put + " " + ReagentName(g)
                            + " back where they lay (stored " + stored + ", took " + taken + ")";
                        return;
                    }
                    have -= put;
                    stored += put;
                }
                // Under: the shortfall off the storage piles onto the loadout's.
                foreach (AssistItem st in Inside(k, storage, g))
                {
                    if (have >= want)
                        break;
                    int take = Math.Min(want - have, Math.Max(1, st.Amount));
                    List<AssistItem> onto = Inside(k, bag, g);
                    LoadoutStatus = "Running: taking " + take + " " + ReagentName(g) + "...";
                    HarvestGap();
                    string? why = Lifted(k, st.Serial, take);
                    if (why != null)
                    {
                        LoadoutStatus = LoadoutFailed + Stopped("loadout", st.Serial, why) + " (stored " + stored
                            + ", took " + taken + ")";
                        return;
                    }
                    why = k.Drop(st.Serial, onto.Count > 0 ? onto[0].Serial : bag);
                    if (why != null)
                    {
                        LoadoutStatus = LoadoutFailed + Stopped("loadout", st.Serial, why) + " (stored " + stored
                            + ", took " + taken + ")";
                        return;
                    }
                    Answered(k, AnswerMs);
                    if (!LoadoutLanded(k, bag, g, have + take))
                    {
                        LoadoutStatus = LoadoutFailed + "the server put " + take + " " + ReagentName(g)
                            + " back where they lay (stored " + stored + ", took " + taken + ")";
                        return;
                    }
                    have += take;
                    taken += take;
                }
                if (want > 0)
                    got.Add(have + " " + ReagentName(g));
                if (have < want)
                    short_.Add(ReagentName(g) + " (" + have + " of " + want + ")");
            }
            LoadoutStatus = (short_.Count > 0 ? "Done, Short: " : "Done, Success: ") + "Stored " + stored + ", Took "
                + taken + "; loaded " + string.Join(", ", got)
                + (short_.Count > 0 ? "; short of " + string.Join(", ", short_) : "") + ".";
            Note("Loadout: " + LoadoutStatus);
        }
    }
    // Every item inside the bag, at any depth, less the containers themselves.
    public List<AssistItem> PricerItems()
    {
        List<AssistItem> list = new();
        GameLink k = m_link;
        uint bag = PricerBag;
        if (k == null || bag == 0)
            return list;
        lock (k.State.Gate)
        {
            Set<uint> inside = Set<uint>.Of(bag);
            for (int pass = 0; pass < 8; pass++)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (inside.Contains(kv.Value.Container))
                        inside = inside.Add(kv.Key);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Key != bag && inside.Contains(kv.Value.Container) && !IsContainer(kv.Value.Graphic))
                    list.Add(kv.Value);
        }
        return list;
    }
    // Automatic cure (Damian, 2026-10-19: "automatically quafe a cure potion if availabe when poisoned, and to
    // automatically cast cure if not or that doesn't work"). On the player's poisoned bit: a cure potion from the pack
    // (the weapon to the pack and back, as the hand drink does), then, still poisoned, Cure at himself. Cast again only
    // on the server's answer that asks for it, a fizzle or "You have failed to cure" (L-FUTILITY); anything else ends
    // it with why. The 1998 lines: "You feel cured of poison!" (the potion, script 3847), "You have failed to cure"
    // (curebase.m).
    public const int CurePotionArt = 0xF07, CureSpell = 11, CureMana = 6, CureCastTries = 5, CureSettleMs = 300,
        CureWaitMs = 3000;
    static readonly ImmutableArray<int> s_cureReagents = [ 0xF84, 0xF85 ];  // garlic, ginseng
    public bool AutoCure
    {
        get => Cfg.Get("auto.cure", "0") == "1";
        set => Cfg.Set("auto.cure", value ? "1" : "0");
    }
    int m_curing;
    void CureWatch(GameLink k, byte[] p)
    {
        if ((p[0] != 0x20 && p[0] != 0x77 && p[0] != 0x78) || !AutoCure || !k.State.Poisoned || k.State.Dead)
            return;
        if (Interlocked.CompareExchange(ref m_curing, 1, 0) != 0)
            return;
        Spawn("the cure", () => { try { CureRun(k); } finally { m_curing = 0; } });
    }
    bool CuredBy(GameLink k, int ms)
    {
        Wait.Until(Clock, () => !k.State.Poisoned, ms, "the cure", closed: () => k.Closed, stepMs: 100);
        return !k.State.Poisoned;
    }
    void CureRun(GameLink k)
    {
        Clock.Sleep(CureSettleMs);
        if (!k.State.Poisoned || k.Closed)
            return;
        AssistItem? potion = null;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && it.Graphic == CurePotionArt)
                {
                    potion = it;
                    break;
                }
        }
        if (potion != null)
        {
            Note("Cure: poisoned, drinking a cure potion");
            byte[] use = new byte[5];
            use[0] = 0x06;
            Put32(use, 1, potion.Serial);
            AssistItem? one = Hand(k, 1);
            AssistItem? two = Hand(k, 2);
            bool twoWeapon = two != null && !IsShield(two.Graphic);
            if (twoWeapon || one != null && two != null)
                Drink(k, use, (twoWeapon ? two : one)!);
            else
                using (Act(k))
                {
                    string? w = k.Use(potion.Serial, false);
                    if (w != null)
                        Note("Cure: the potion: " + w);
                }
            if (CuredBy(k, CureWaitMs))
            {
                Note("Cure: the potion cured you");
                return;
            }
            Note("Cure: still poisoned after the potion");
        }
        // With Casting's disarm on, the hands freed for every Cure cast and filled again after (CastHands).
        List<AssistItem> held = CastHands ? HeldForCast(k) : new List<AssistItem>();
        if (held.Count > 0)
        {
            string? dw = DisarmForCast(k, held);
            if (dw != null)
            {
                Note("Cure: the hands not freed for Cure: " + dw);
                return;
            }
        }
        try
        {
            CureCasts(k);
        }
        finally
        {
            if (held.Count > 0 && !k.Closed)
            {
                Clock.Sleep(LiftSettleMs);
                string? rw = RearmAfterCast(k, held);
                Note(rw == null ? "Cure: re-armed" : "Cure: not re-armed: " + rw);
            }
        }
    }
    void CureCasts(GameLink k)
    {
        for (int attempt = 1; attempt <= CureCastTries && k.State.Poisoned && !k.Closed; attempt++)
        {
            foreach (int r in s_cureReagents)
                if (PackCount(k, g => g == r) == 0)
                {
                    Note("Cure: still poisoned, and no " + UoTiles.Name(r) + " in the pack to cast Cure");
                    k.Tell("Poisoned: no cure potion worked and no " + UoTiles.Name(r) + " for Cure.", 0x0026);
                    return;
                }
            int mana;
            lock (k.State.Gate)
                mana = k.State.Mana;
            if (mana < CureMana)
            {
                Note("Cure: still poisoned, mana " + mana + " is under Cure's " + CureMana);
                k.Tell("Poisoned: not enough mana for Cure.", 0x0026);
                return;
            }
            Heard mark;
            using (Act(k))
            {
                mark = new(k.State);
                string? w = k.Cast(CureSpell, true);
                if (w != null)
                {
                    Note("Cure: casting: " + w);
                    return;
                }
                AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CastCursorMs), mark);
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
                    w = k.Target(false, me, x, y, z, body);
                    if (w != null)
                    {
                        Note("Cure: aiming at you: " + w);
                        return;
                    }
                }
            }
            if (CuredBy(k, CureWaitMs))
            {
                Note("Cure: the spell cured you" + (attempt > 1 ? " (cast " + attempt + ")" : ""));
                return;
            }
            List<string> heard = mark.Answers();
            bool again = heard.Exists(j => j.IndexOf("fizzle", StringComparison.OrdinalIgnoreCase) >= 0
                || j.IndexOf("failed to cure", StringComparison.OrdinalIgnoreCase) >= 0);
            string said = heard.Count > 0 ? heard[heard.Count - 1] : "no answer";
            if (!again)
            {
                Note("Cure: still poisoned after Cure (\"" + said + "\"); not cast again");
                return;
            }
            Note("Cure: cast " + attempt + " did not cure (\"" + said + "\"), casting again");
        }
        if (k.State.Poisoned)
            Note("Cure: still poisoned after " + CureCastTries + " casts");
    }
    bool HoldPotion(GameLink k, byte[] p)
    {
        if (p[0] != 0x06 || p.Length != 5)
            return false;
        if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null)
            return false;
        AssistItem? it = Item(k, U32(p, 1));
        if (it == null || s_drinkable.IndexOf(it.Graphic) < 0)
            return false;
        AssistItem? one = Hand(k, 1);
        AssistItem? two = Hand(k, 2);
        bool twoWeapon = two != null && !IsShield(two.Graphic);
        if (!(twoWeapon || (one != null && two != null)))
            return false;
        AssistItem? weapon = twoWeapon ? two : one;
        Spawn("the drink", () => Drink(k, p, weapon!));
        return true;
    }
    void Drink(GameLink k, byte[] held, AssistItem weapon)
    {
        using (Act(k))
        {
            uint pack, me;
            lock (k.State.Gate)
            {
                pack = k.State.Backpack;
                me = k.State.Player;
            }
            Sequence = "Drink: lift " + weapon.Graphic.ToString("X4") + " from your hand";
            Spot? inHand = Where(k, weapon.Serial);
            string? why = Lifted(k, weapon.Serial, 1);
            if (why != null)
            {
                Stopped("drink", weapon.Serial, why);
                return;
            }
            Sequence = "Drink: put " + weapon.Graphic.ToString("X4") + " in the backpack";
            why = k.Drop(weapon.Serial, pack);
            if (why != null)
            {
                Stopped("drink", weapon.Serial, why);
                return;
            }
            Answered(k, AnswerMs);
            if (Landed(k, weapon.Serial, inHand!, "drink") != null)
                return;
            Sequence = "Drink: drink";
            why = k.Release(held, "drink " + U32(held, 1).ToString("X"));
            if (why != null)
            {
                Stopped("drink", weapon.Serial, why);
                return;
            }
            Answered(k, AnswerMs);
            Clock.Sleep(LiftSettleMs);
            Sequence = "Drink: lift " + weapon.Graphic.ToString("X4") + " back";
            Spot? inPack = Where(k, weapon.Serial);
            why = Lifted(k, weapon.Serial, 1);
            if (why != null)
            {
                Stopped("drink", weapon.Serial, why);
                return;
            }
            Sequence = "Drink: equip " + weapon.Graphic.ToString("X4") + " on layer " + weapon.Layer;
            why = k.Equip(weapon.Serial, weapon.Layer, me);
            if (why != null)
            {
                Stopped("drink", weapon.Serial, why);
                return;
            }
            Answered(k, AnswerMs);
            if (Landed(k, weapon.Serial, inPack!, "drink") != null)
                return;
            Sequence = "Drank, rearmed";
            Note(Sequence);
        }
    }

    // ---------- casting with the hands full ----------
    // Damian, 2026-10-23: "add to assist in the actions general automatic the disarming for casting spells, and
    // re-arming when the spell is done. mostly for recall, but with heals and cures and such". uo1998 refuses every
    // spell while a hand holds anything but a spellbook (rundir spelskil.m Q49Q, "Your hands must be free to cast
    // spells."). With cast.hands on, the player's cast (0x12 type 0x56) with a hand full is held: the held items go to
    // the pack, the cast goes out, and when the spell is done (the player answered its cursor, the server said a
    // CastEnds line, or no cursor came within CastCursorMs) they come back to their layers. CastAimMs at most for the
    // player to aim.
    public const int CastAimMs = 60000;
    public bool CastHands
    {
        get => Cfg.Get("cast.hands", "off") == "on";
        set => Cfg.Set("cast.hands", value ? "on" : "off");
    }
    volatile bool m_castHeld;
    bool HoldCast(GameLink k, byte[] p)
    {
        if (!CastHands || p[0] != 0x12 || p.Length < 5 || p[3] != 0x56 || m_castHeld)
            return false;
        if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null)
            return false;
        List<AssistItem> held = HeldForCast(k);
        if (held.Count == 0)
            return false;
        m_castHeld = true;
        Spawn("the cast", () => { try { CastFreed(k, p, held); } finally { m_castHeld = false; } });
        return true;
    }
    // What a hand holds that a cast refuses: layers 1 and 2, but a spellbook.
    List<AssistItem> HeldForCast(GameLink k)
    {
        List<AssistItem> held = new();
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        foreach (int layer in new[] { 1, 2 })
        {
            AssistItem? h = Hand(k, layer);
            if (h != null && h.Mobile == me && h.Graphic != SpellbookArt)
                held.Add(h);
        }
        return held;
    }
    // The held items to the pack. Null, or why not.
    string? DisarmForCast(GameLink k, List<AssistItem> held)
    {
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        if (pack == 0)
            return "the backpack is not known yet";
        using (Act(k))
        {
            foreach (AssistItem h in held)
            {
                Sequence = "Cast: put " + h.Graphic.ToString("X4") + " from your hand in the backpack";
                Spot? from = Where(k, h.Serial);
                string? why = Lifted(k, h.Serial, 1);
                if (why != null)
                    return Stopped("the cast's disarm", h.Serial, why);
                why = k.Drop(h.Serial, pack);
                if (why != null)
                    return Stopped("the cast's disarm", h.Serial, why);
                Answered(k, AnswerMs);
                why = Landed(k, h.Serial, from!, "the cast's disarm");
                if (why != null)
                    return why;
            }
        }

        return null;
    }
    // The held items back on their layers. Null, or why not.
    string? RearmAfterCast(GameLink k, List<AssistItem> held)
    {
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        using (Act(k))
        {
            foreach (AssistItem h in held)
            {
                if (Item(k, h.Serial) == null)
                    return "the " + h.Graphic.ToString("X4") + " " + h.Serial.ToString("X")
                        + " is no longer in the pack";
                Sequence = "Cast: " + h.Graphic.ToString("X4") + " back on layer " + h.Layer;
                Spot? from = Where(k, h.Serial);
                string? why = Lifted(k, h.Serial, 1);
                if (why != null)
                    return Stopped("the cast's re-arm", h.Serial, why);
                why = k.Equip(h.Serial, h.Layer, me);
                if (why != null)
                    return Stopped("the cast's re-arm", h.Serial, why);
                Answered(k, AnswerMs);
                why = Landed(k, h.Serial, from!, "the cast's re-arm");
                if (why != null)
                    return why;
            }
        }

        return null;
    }
    void CastFreed(GameLink k, byte[] cast, List<AssistItem> held)
    {
        string spell = Encoding.ASCII.GetString(cast, 4, cast.Length - 4).TrimEnd('\0');
        string? dw = DisarmForCast(k, held);
        if (dw != null)
        {
            Note("Cast: not cast, the hands not freed: " + dw);
            return;
        }
        bool cursor = false, aimed = false;
        Action<byte[]> fromServer = q => { if (q[0] == 0x6C) cursor = true; };
        Action<byte[]> fromClient = q => { if (q[0] == 0x6C && cursor) aimed = true; };
        k.ServerPacket += fromServer;
        k.ClientPacket += fromClient;
        try
        {
            // A Recall that fizzles is cast once more before the re-arm (Damian, 2026-10-23: "a fizzle on recall should
            // try the recall again once, then rearm. other spells should re-arm regardless of spell fail or success").
            for (int tries = 1; tries <= 2; tries++)
            {
                cursor = aimed = false;
                Heard mark = new(k.State);
                string? why = k.Release(cast,
                    "cast " + spell + " with the hands freed" + (tries > 1 ? ", again after a fizzle" : ""));
                if (why != null)
                {
                    Note("Cast: spell " + spell + " not sent: " + why);
                    break;
                }
                DateTime began = Clock.UtcNow;
                Wait.Until(Clock,
                    () => aimed || k.Closed
                    || mark.Answers().Exists(j =>
                    s_castEnds.Exists(e => j.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0))
                    || (!cursor && (Clock.UtcNow - began).TotalMilliseconds >= CastCursorMs), CastAimMs,
                    "the spell done", stepMs: 100);
                bool fizzled = !aimed
                    && mark.Answers().Exists(j => j.IndexOf("fizzle", StringComparison.OrdinalIgnoreCase) >= 0);
                Note("Cast: spell " + spell + " done ("
                    + (aimed ? "aimed" : fizzled ? "fizzled" : cursor ? "the cursor not answered" : "no cursor") + ")"
                    + (fizzled && spell == RecallSpell.ToString() && tries == 1 ? "; casting the Recall once more"
                    : "; re-arming"));
                if (!(fizzled && spell == RecallSpell.ToString()) || k.Closed)
                    break;
                Clock.Sleep(LiftSettleMs);
            }
        }
        finally { k.ServerPacket -= fromServer; k.ClientPacket -= fromClient; }
        if (k.Closed)
            return;
        Clock.Sleep(LiftSettleMs);
        string? rw = RearmAfterCast(k, held);
        Note(rw == null ? "Cast: re-armed" : "Cast: not re-armed: " + rw);
    }

    // ---------- reagent autopickup ----------
    public bool Pickup
    {
        get => Cfg.Get("pickup", "off") == "on";
        set => Cfg.Set("pickup", value ? "on" : "off");
    }
    public int Reach => Math.Max(1, Cfg.GetInt(Server + "pickup.reach", LiftReach));
    public int[] Reagents()
    {
        List<int> l = new();
        foreach (string h in Cfg.Get("reagents", DefaultReagents).Split(new[] { ',' },
            StringSplitOptions.RemoveEmptyEntries))
            l.Add(Convert.ToInt32(h, 16));
        return l.ToArray();
    }
    public void SetReagents(int[] g)
    {
        string[] parts = new string[g.Length];
        for (int i = 0; i < g.Length; i++)
            parts[i] = g[i].ToString("X");
        Cfg.Set("reagents", string.Join(",", parts));
    }

    bool m_overweightNoted;
    // A step answered within the "pickup-walking" timing is walking.
    bool Moving(GameLink k)
    {
        int ms = Ms("pickup-walking");
        lock (k.State.Gate)
            return (Clock.UtcNow - k.State.StepAt).TotalMilliseconds < ms;
    }
    // Items the server refused as out of range, and the player's distance from each then (PickupLoop's thread only).
    ValueMap<uint, int> m_outOfRange = ValueMap<uint, int>.Empty, m_busyTries = ValueMap<uint, int>.Empty;
    // The adaptive pace (Damian, 2026-10-17: "tries to fire all its messages with slightly less delay till it notices a
    // problem, then backs off"): the gap from the last pickup's drop to the next lift (from busy) and the lift's settle
    // (from lift-settle) each come down PickupStepMs after every clean pickup, never under its floor. A busy refusal
    // raises the gap, a refusal after the lift was judged accepted raises the settle (and the gap too when it is busy),
    // by PickupBackoffMs, and the refused value plus a step becomes that one's floor; PickupProbeAfter clean pickups at
    // a floor lower it a step, so a server that has grown quicker is found again. Nothing comes down for the
    // "pickup-hold" timing after any pickup refusal, so the probing costs at most one of the guard's 3 lift refusals in
    // 60 s (it tripped at 06:32:42 with the gap probing on). Kept per server (pickup.gap, pickup.settle and their
    // .floor).
    public const int PickupStepMs = 25, PickupBackoffMs = 150, PickupProbeAfter = 100, PickupSaveEvery = 20,
        PickupSettleLeastMs = 50;
    DateTime m_pickupRefusedAt = DateTime.MinValue;
    sealed class PickupPace
    {
        public string Key = null!, Name = null!; public int Ms, Floor, Least, AtFloor, Logged;
    }
    PickupPace m_pickupGap = null!, m_pickupSettle = null!;
    int m_pickupClean;
    PickupPace PaceOf(string key, string name, int start, int least)
    {
        int ms = Math.Max(least, Cfg.GetInt(Server + "pickup." + key, start));
        return new PickupPace { Key = Server + "pickup." + key, Name = name, Least = least, Ms = ms, Logged = ms,
            Floor = Math.Max(least, Cfg.GetInt(Server + "pickup." + key + ".floor", least)) };
    }
    readonly Lock m_paceGate = new();
    void PaceStart()
    {
        lock (m_paceGate)
            if (m_pickupGap == null)
            {
                m_pickupSettle = PaceOf("settle", "Pickup lift settle", LiftSettleMs, PickupSettleLeastMs);
                m_pickupGap = PaceOf("lift-gap", "Pickup lift gap", BusyMs, 0);
            }
    }
    void PaceSave(PickupPace p, string why)
    {
        Cfg.Set(p.Key, p.Ms.ToString());
        Cfg.Set(p.Key + ".floor", p.Floor.ToString());
        Timings.Log(p.Name, p.Logged, p.Ms, why);
        p.Logged = p.Ms;
    }
    // The Item pickup section's live boxes: a value set by hand is also the floor, so the pace does not probe under it
    // until PickupProbeAfter clean pickups lower the floor.
    public int PickupPaceMs(string key)
    {
        PaceStart();
        return key == "pickup:gap" ? m_pickupGap.Ms : m_pickupSettle.Ms;
    }
    public void SetPickupPace(string key, int ms)
    {
        PaceStart();
        PickupPace p = key == "pickup:gap" ? m_pickupGap : m_pickupSettle;
        p.Ms = p.Floor = Math.Max(p.Least, Math.Min(TimingCatalog.MaxMs, ms));
        p.AtFloor = 0;
        PaceSave(p, "set by you");
    }
    void PaceDown(PickupPace p)
    {
        if (p.Ms - PickupStepMs >= p.Floor)
        {
            p.Ms -= PickupStepMs;
            p.AtFloor = 0;
            return;
        }
        if (++p.AtFloor >= PickupProbeAfter && p.Floor - PickupStepMs >= p.Least)
        {
            p.Floor -= PickupStepMs;
            p.AtFloor = 0;
        }
    }
    void PaceBack(PickupPace p, string why)
    {
        int was = p.Ms;
        p.Floor = was + PickupStepMs;
        p.Ms = was + PickupBackoffMs;
        p.AtFloor = 0;
        PaceSave(p, "backed off: " + why);
        Note("Pickup: " + why + " at " + (p == m_pickupGap ? "gap" : "settle") + " " + was + " ms; now " + p.Ms
            + " ms, floor " + p.Floor + " ms");
    }
    public string PickupPaceText
    {
        get
        {
            PickupPace g = m_pickupGap;
            PickupPace s = m_pickupSettle;
            return g == null ? "pace not started"
                : "gap " + g.Ms + " ms (floor " + g.Floor + "), settle " + s.Ms + " ms (floor " + s.Floor + ")";
        }
    }
    void PickupLoop()
    {
        s_busyTask = "pickup";
        while (!m_shutdown)
        {
            Clock.Sleep(Math.Max(1, Ms("pickup-scan")));
            GameLink k = m_link;
            if (k == null || k.Closed || k.Halted != null || !Pickup || k.State.Dead)
                continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null)
                continue;
            PaceStart();
            // A lift leaves the player busy, refused or not: "You must wait to perform another action." (uo1998
            // 2026-10-17 14:29:51, 06:37:26). The pace's gap from the last lift sent, or the full BusyMs after any
            // other action since (a harvest's pluck), is waited out BEFORE the item is chosen, so the first one met on
            // a walk goes at once and the ones after keep the gap, each chosen where the player is then (Damian,
            // 2026-10-17: "when walking the first one encountered needs to fire sooner, and the ones after need the
            // delay to prevent the failsafe").
            bool paced = m_harvestActAt <= liftSentAt;
            if ((paced ? m_pickupGap.Ms - (int)(Clock.UtcNow - liftSentAt).TotalMilliseconds
                : BusyMs - (int)(Clock.UtcNow - m_harvestActAt).TotalMilliseconds) > 0)
                continue;
            int reach = Reach;
            int[] reagents = Reagents();
            AssistItem? best = null;
            int bestD = int.MaxValue;
            uint pack;
            int fromX, fromY;
            // Moving, only what is beside the player: a lift at the full reach went out between steps and the server, a
            // step on, refused it out of range (Damian, 2026-10-17: "its going off too soon"; 05:17:08 and 05:23:38).
            if (Moving(k))
                reach = Math.Min(reach, 1);
            lock (k.State.Gate)
            {
                pack = k.State.Backpack;
                fromX = k.State.X;
                fromY = k.State.Y;
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                {
                    AssistItem g = kv.Value;
                    if (Left(g.Serial) || Array.IndexOf(reagents, g.Graphic) < 0)
                        continue;
                    int d = Math.Max(Math.Abs(g.X - k.State.X), Math.Abs(g.Y - k.State.Y)), was;
                    if (m_outOfRange.TryGet(g.Serial, out was) && d >= was)
                        continue;
                    if (d <= reach && d < bestD)
                    {
                        best = g;
                        bestD = d;
                    }
                }
            }
            if (best == null || pack == 0)
                continue;
            // The 1998 carry limit is strength * 4 + 30 (OUO mobile.c CMobile_GetMaxWeight); past it every step costs
            // ten times the load in stamina instead of a tenth (CMobile_HandleStaminaDrain), so pickup stops short of
            // it. OUO item.c:4165-4169 weighs a weightless stack at (amount + 9) / 10 or (amount + 49) / 50; the larger
            // is used.
            int str, weight;
            lock (k.State.Gate)
            {
                str = k.State.Str;
                weight = k.State.Weight;
            }
            if (str > 0 && weight + Math.Max(1, (best.Amount + 9) / 10) > str * 4 + 30)
            {
                if (!m_overweightNoted)
                {
                    Note("Pickup: paused, the pack is at " + weight + " of " + (str * 4 + 30) + " stones");
                    m_overweightNoted = true;
                }
                continue;
            }
            m_overweightNoted = false;
            using (ActionScope? scope = TryAct(k))
            {
                if (scope == null)
                    continue;
                string? why = Lifted(k, best.Serial, best.Amount, m_pickupSettle.Ms);
                if (why != null)
                {
                    m_pickupRefusedAt = Clock.UtcNow;
                    // Busy (0xFF) asks for a repeat: lifted again after the busy time, BusyRetries times at most.
                    if (lastRefusal != null && lastRefusal[1] == 0xFF)
                    {
                        if (paced)
                            PaceBack(m_pickupGap, "busy");
                        int tries;
                        if (m_busyTries.Count > 2000)
                            m_busyTries = m_busyTries.Clear();
                        m_busyTries.TryGet(best.Serial, out tries);
                        m_busyTries = m_busyTries.Set(best.Serial, ++tries);
                        if (tries <= BusyRetries)
                        {
                            Note("Pickup: busy; " + best.Serial.ToString("X") + " is lifted again after the busy time");
                            continue;
                        }
                    }
                    // Out of range or out of sight is an answer about where the player stood: the item is lifted again
                    // only from nearer than that (a pearl refused mid-walk at 14:14:17 was left alone with Kwik
                    // standing on it). Any other refusal leaves it for good.
                    if (lastRefusal != null && (lastRefusal[1] == 1 || lastRefusal[1] == 2))
                    {
                        int nowD;
                        lock (k.State.Gate)
                            nowD = Math.Max(Math.Abs(best.X - k.State.X), Math.Abs(best.Y - k.State.Y));
                        if (m_outOfRange.Count > 2000)
                            m_outOfRange = m_outOfRange.Clear();
                        m_outOfRange = m_outOfRange.Set(best.Serial, Math.Max(nowD, bestD));
                    }
                    else
                    {
                        Leave(best.Serial);
                    }
                    // LRReason.OutOfRange is 1 (RunUO Server/Mobile.cs): this server's reach is shorter than that, but
                    // only when the player stood still; a player walking away was out of range by the step (uo1998
                    // 2026-10-17 14:10:08, refused at 1 tile mid-walk, reach cut to 0 and every ginseng beside him
                    // left). Never under 1 tile.
                    bool still;
                    lock (k.State.Gate)
                        still = k.State.X == fromX && k.State.Y == fromY;
                    if (lastRefusal != null && lastRefusal[1] == 1 && still && bestD > 1)
                    {
                        Cfg.Set(Server + "pickup.reach", (bestD - 1).ToString());
                        Note("Pickup: the server refused at " + bestD + " tiles; reach is now " + (bestD - 1));
                    }
                    else
                    {
                        Note("Pickup: " + why + "; left " + best.Serial.ToString("X") + " alone");
                    }

                    continue;
                }
                long judged = m_hands.Refusals;
                why = k.Drop(best.Serial, pack);
                if (why != null)
                {
                    Stopped("pickup", best.Serial, why);
                    Leave(best.Serial);
                    continue;
                }
                Answered(k, AnswerMs);
                // A refusal after the settle: the lift was judged accepted too soon, and a busy one came of too short a
                // gap as well. Busy asks for a repeat; any other reason leaves the item.
                if (m_hands.Refusals != judged)
                {
                    m_pickupRefusedAt = Clock.UtcNow;
                    byte[]? r = lastRefusal;
                    PaceBack(m_pickupSettle,
                        "a refusal after the settle (reason " + (r != null ? r[1].ToString("X") : "?") + ")");
                    if (r != null && r[1] == 0xFF)
                    {
                        if (paced)
                            PaceBack(m_pickupGap, "busy, seen after the settle");
                    }
                    else
                    {
                        Leave(best.Serial);
                    }

                    continue;
                }
                if ((Clock.UtcNow - m_pickupRefusedAt).TotalMilliseconds > Ms("pickup-hold"))
                {
                    PaceDown(m_pickupGap);
                    PaceDown(m_pickupSettle);
                }
                if (++m_pickupClean % PickupSaveEvery == 0)
                {
                    string probed = "probed down over " + PickupSaveEvery + " clean pickups";
                    PaceSave(m_pickupGap, probed);
                    PaceSave(m_pickupSettle, probed);
                }
                SeenForget(best.X, best.Y, best.Graphic);
                Note("Pickup: " + best.Amount + " of " + best.Graphic.ToString("X4") + " into the backpack");
                lock (m_pickedGate)
                    m_picked = m_picked.Set(best.Graphic, m_picked.Get(best.Graphic, 0) + best.Amount);
            }
        }
    }
}
