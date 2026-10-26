#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the spellbook maker ----------
    // "Spellbooks" (circuit spellbook; Damian, 2026-10-18: "spellbook maker. it buys spellbooks and scrolls from the
    // vendors and stocks them. it should buy what it can and if inscription is adequate, fill up the books to the
    // scribes skill level, if it wont fail too much without giving gains"). The 1998 rules (scripts.wombat): a scroll
    // given to a spellbook puts its spell in the book once (spellbook.m); Inscription (skill 23, inscribe.m) targets a
    // blank scroll (0EF3 or 0E34) and offers by menu ("Choose a circle.", then the circle's spells) the spells in the
    // scribe's own spellbook whose circle the scribe's Magery can cast at all and whose mana the scribe has; the
    // reagents (spelskil.m Q4SL) and the circle's mana go, and testAndLearnSkill(Inscription, (circle - 1) * 1000 / 7,
    // 50) decides: a failure ruins the scroll and gains nothing, a success gains by 1000 less the chance. The chance is
    // (value - difficulty) * 2 + 500 tenths of a percent (SmithModel's reading of SkillCheck), so a spell is inscribed
    // only while its circle's chance is at least spellbook.chance percent (75 unset). The skill value is the one the
    // client shows; 1998 weighs stats into the checked value (GetSkillValue), which the client does not show.
    static readonly ImmutableArray<string> s_spellRows = [ "Clumsy|F7B,F88", "Create Food|F84,F85,F86",
        "Feeblemind|F85,F88",
        "Heal|F84,F85,F8D", "Magic Arrow|F7A,F88", "Night Sight|F8D,F8C", "Reactive Armor|F84,F8C,F8D",
        "Weaken|F84,F88", "Agility|F7B,F86", "Cunning|F86,F88", "Cure|F84,F85", "Harm|F88,F8D",
        "Magic Trap|F84,F8C,F8D", "Magic Untrap|F7B,F8C", "Protection|F84,F85,F8C", "Strength|F86,F88",
        "Bless|F84,F86", "Fireball|F7A,F8C", "Magic Lock|F8C,F7B,F84", "Poison|F88", "Telekinesis|F7B,F86",
        "Teleport|F7B,F86", "Unlock|F7B,F8C", "Wall of Stone|F7B,F84", "Archcure|F84,F85,F86",
        "Archprotection|F84,F85,F86,F8C", "Curse|F84,F88,F8C", "Fire Field|F7A,F8D,F8C",
        "Greater Heal|F84,F85,F86,F8D", "Lightning|F7A,F86,F8C", "Mana Drain|F7A,F86,F8D", "Recall|F7A,F7B,F86",
        "Blade Spirits|F7A,F86,F88", "Dispel Field|F84,F7A,F8D,F8C", "Incognito|F7B,F84,F88",
        "Magic Reflection|F84,F86,F8D", "Mind Blast|F7A,F86,F88,F8C", "Paralyze|F84,F86,F8D",
        "Poison Field|F7A,F88,F8D", "Summon Creature|F7B,F86,F8D", "Dispel|F84,F86,F8C", "Energy Bolt|F7A,F88",
        "Explosion|F7A,F86,F8C", "Invisibility|F7B,F88", "Mark|F7A,F7B,F86", "Mass Curse|F84,F86,F88,F8C",
        "Paralyze Field|F7A,F85,F8D", "Reveal|F7B,F8C", "Chain Lightning|F7A,F7B,F86,F8C",
        "Energy Field|F7A,F86,F8D,F8C", "Flamestrike|F8D,F8C", "Gate Travel|F7A,F86,F8C",
        "Mana Vampire|F86,F7A,F7B,F8D", "Mass Dispel|F7A,F84,F86,F8C", "Meteor Swarm|F7B,F8D,F86,F8C",
        "Polymorph|F7B,F86,F8D", "Earthquake|F7B,F85,F86,F8C", "Energy Vortex|F7A,F7B,F86,F88",
        "Resurrection|F7B,F84,F85", "Summon Air Elemental|F7B,F86,F8D", "Summon Daemon|F7B,F86,F8D,F8C",
        "Summon Earth Elemental|F7B,F86,F8D", "Summon Fire Elemental|F7B,F86,F8D,F8C",
        "Summon Water Elemental|F7B,F86,F8D" ];
    static readonly ImmutableArray<string> s_circleWords = [ "First", "Second", "Third", "Fourth", "Fifth", "Sixth",
        "Seventh",
        "Eighth" ];
    public const int SpellbookArt = 0x0EFA, InscribeSkill = 23, MagerySkillId = 25, InscribeAgainMax = 3,
        InscribeManaWaitMs = 180000;
    static readonly ImmutableArray<int> s_blankScrollArts = [ 0x0EF3, 0x0E34 ];
    // spelskil.m Q4T3: a spell's scroll art.
    static int ScrollArt(int spell)
    {
        return spell == 6 ? 0x1F2D : spell <= 5 ? 0x1F2E + spell : 0x1F34 + spell - 7;
    }
    static int SpellOfArt(int art)
    {
        for (int i = 0; i < 64; i++)
            if (ScrollArt(i) == art)
                return i;
        return -1;
    }
    static string SpellName(int spell)
    {
        return s_spellRows[spell].Split('|')[0];
    }
    static int[] SpellRegs(int spell)
    {
        return Array.ConvertAll(s_spellRows[spell].Split('|')[1].Split(','), s => Convert.ToInt32(s, 16));
    }
    static int CircleDifficulty(int spell)
    {
        return (spell / 8) * 1000 / 7;
    }
    // The 1998 check's chance in tenths of a percent, range 50 (Inscription) or 40 (Magery).
    static int CheckChance(int value, int difficulty, int range)
    {
        return Math.Max(0, Math.Min(1000, (value - difficulty) * 100 / range + 500));
    }
    static int Bits(ulong m)
    {
        int n = 0;
        while (m != 0)
        {
            m &= m - 1;
            n++;
        }
        return n;
    }
    public int SpellbookCount
    {
        get => Math.Max(1, Cfg.GetInt(Server + "spellbook.count", 1));
        set => Cfg.Set(Server + "spellbook.count", Math.Max(1, value).ToString());
    }
    public int SpellbookChance
    {
        get => Math.Max(1, Math.Min(100, Cfg.GetInt(Server + "spellbook.chance", 75)));
        set => Cfg.Set(Server + "spellbook.chance", Math.Max(1, Math.Min(100, value)).ToString());
    }
    public bool SpellbookInscribe
    {
        get => Cfg.Get(Server + "spellbook.inscribe", "on") == "on";
        set => Cfg.Set(Server + "spellbook.inscribe", value ? "on" : "off");
    }
    // Twice ServUO's prices unset: a spellbook 18 (SBMage), a blank scroll 5, a reagent 3 to 6; a spell scroll by its
    // circle.
    public int SpellbookPrice
    {
        get => Math.Max(1, Cfg.GetInt(Server + "spellbook.price", 36));
        set => Cfg.Set(Server + "spellbook.price", Math.Max(1, value).ToString());
    }
    public int ScrollPrice
    {
        get => Math.Max(1, Cfg.GetInt(Server + "spellbook.scrollprice", 200));
        set => Cfg.Set(Server + "spellbook.scrollprice", Math.Max(1, value).ToString());
    }
    volatile bool m_spellbookMode;
    public volatile string SpellbookStatus = "";
    public string? CircuitSpellbooks()
    {
        return StartCircuit(() => m_spellbookMode = true);
    }
    List<uint> PackSpellbooks(GameLink k)
    {
        List<uint> l = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && it.Graphic == SpellbookArt)
                    l.Add(it.Serial);
        }
        return l;
    }
    // The spells in a book, from the contents its open sends: a 1998 book holds scroll arts, ServUO's old-client packet
    // sends graphic 0 and amount spell + 1 under serials shared by every book (Packets.SpellbookContent), so the packet
    // is read as it comes rather than from the item table.
    ulong ReadSpells(GameLink k, uint book)
    {
        ulong m = 0;
        Action<byte[]> h = p =>
        {
            if (p[0] != 0x3C || p.Length < 5)
                return;
            int n = (p[3] << 8) | p[4];
            for (int i = 0; i < n && 5 + i * 19 + 19 <= p.Length; i++)
            {
                int at = 5 + i * 19;
                if (U32(p, at + 13) != book)
                    continue;
                int g = (p[at + 4] << 8) | p[at + 5], amt = (p[at + 7] << 8) | p[at + 8], s = SpellOfArt(g);
                if (s < 0 && amt >= 1 && amt <= 64)
                    s = amt - 1;
                if (s >= 0)
                    lock (this)
                        m |= 1UL << s;
            }
        };
        k.ServerPacket += h;
        try
        {
            BusyWait(clickedAt);
            string? w = Peek(book);
            if (w != null)
                Note("Spellbooks: opening " + book.ToString("X") + ": " + w);
            Clock.Sleep(300);
        }
        finally { k.ServerPacket -= h; }
        lock (this)
            return m;
    }
    // A spell's scrolls loose in the pack, outside every spellbook.
    AssistItem? LooseScroll(GameLink k, int art, out int count)
    {
        AssistItem? first = null;
        count = 0;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Graphic == art && inside.Contains(it.Container))
                {
                    AssistItem? c;
                    if (k.State.Items.TryGet(it.Container, out c) && c.Graphic == SpellbookArt)
                        continue;
                    count += Math.Max(1, it.Amount);
                    if (first == null)
                        first = it;
                }
        }
        return first;
    }
    // The loose scrolls given to each book short of their spell, one each (spellbook.m: a spell already there is
    // refused). A book that refuses a scroll is not given that spell again this run (L-FUTILITY).
    Set<string> m_bookRefused = Set<string>.Empty;
    int FillBooks(GameLink k, List<uint> books, ref ValueMap<uint, ulong> masks)
    {
        int put = 0;
        foreach (uint b in books)
        {
            for (int i = 0; i < 64 && !circuitStop && !k.Closed; i++)
            {
                if ((masks.Get(b, 0) & (1UL << i)) != 0 || m_bookRefused.Contains(b.ToString("X") + "|" + i))
                    continue;
                int before;
                AssistItem? sc = LooseScroll(k, ScrollArt(i), out before);
                if (sc == null)
                    continue;
                CircuitStatus = "Spellbooks: " + SpellName(i) + " into " + b.ToString("X");
                string? w;
                using (Act(k))
                {
                    w = BusyLift(k, sc.Serial, 1);
                    if (w == null)
                    {
                        w = k.Drop(sc.Serial, b);
                        if (w != null)
                            w = Stopped("Giving " + SpellName(i) + " to the book", sc.Serial, w);
                    }
                    Answered(k, AnswerMs);
                    Clock.Sleep(DropSettleMs);
                }
                if (w != null)
                {
                    Note("Spellbooks: " + SpellName(i) + ": " + w);
                    return put;
                }
                int after;
                LooseScroll(k, ScrollArt(i), out after);
                if (after < before)
                {
                    masks = masks.Set(b, masks.Get(b, 0) | 1UL << i);
                    put++;
                }
                else
                {
                    m_bookRefused = m_bookRefused.Add(b.ToString("X") + "|" + i);
                    Note("Spellbooks: the book " + b.ToString("X") + " did not take " + SpellName(i));
                }
            }
        }

        return put;
    }
    // One scroll inscribed: the skill used, its cursor aimed at the blank scroll, the circle and the spell picked from
    // the menus by their text. "made", "failed", "again", or why not.
    string InscribeOne(GameLink k, int spell, uint blank)
    {
        string circle = s_circleWords[spell / 8] + " Circle", name = SpellName(spell);
        int art = ScrollArt(spell), before;
        LooseScroll(k, art, out before);
        byte[]? menu = null;
        Action<byte[]> seen = p => { if (p[0] == 0x7C) menu = p; };
        Heard mark;
        BusyWait(clickedAt);
        using (Act(k))
        {
            mark = new(k.State);
            k.ClaimMenu = true;
            k.ServerPacket += seen;
            try
            {
                k.Settle();
                string? w = k.Skill(InscribeSkill, true);
                if (w != null)
                    return w;
                clickedAt = Clock.UtcNow;
                DateTime until = Clock.UtcNow.AddMilliseconds(MineCursorMs);
                AwaitCursor(k, until);
                if (!k.MacroCursor)
                {
                    k.Settle();
                    string? said = mark.Last();
                    if (said != null
                        && s_waitLines.Exists(wl => said.StartsWith(wl, StringComparison.OrdinalIgnoreCase)))
                        return "again";
                    return "Inscription opened no cursor" + (said != null ? "; the server said \"" + said + "\"" : "");
                }
                AssistItem? bl = Item(k, blank);
                w = k.Target(false, blank, 0, 0, 0, bl != null ? bl.Graphic : s_blankScrollArts[0]);
                if (w != null)
                    return "aiming at the blank scroll: " + w;
                foreach (string want in new[] { circle, name })
                {
                    Wait.Until(Clock, () => menu != null, AnswerMs, "the menu", closed: () => k.Closed);
                    byte[]? m = menu;
                    menu = null;
                    if (m == null)
                    {
                        string? said = mark.Last();
                        return said != null ? "\"" + said + "\"" : "no menu for " + want;
                    }
                    string? pick = null;
                    for (int i = 1; ; i++)
                    {
                        string? t = MenuItem(m, i);
                        if (t == null)
                            break;
                        if (string.Equals(t.Trim(), want, StringComparison.OrdinalIgnoreCase))
                        {
                            pick = t;
                            break;
                        }
                    }
                    if (pick == null)
                    {
                        byte[] cancel = new byte[13];
                        cancel[0] = 0x7D;
                        Array.Copy(m, 3, cancel, 1, 6);
                        k.Settle();
                        k.Inject(cancel, "menu cancel", false);
                        return "the menu does not offer " + want;
                    }
                    byte[]? r = MenuPick(m, pick);
                    k.Settle();
                    w = k.Inject(r!, "menu pick " + want, false);
                    if (w != null)
                        return w;
                }
            }
            finally { k.ServerPacket -= seen; k.ClaimMenu = false; }
        }
        DateTime end = Clock.UtcNow.AddMilliseconds(TailorMs);
        while (Clock.UtcNow < end && !k.Closed)
        {
            foreach (string j in mark.Answers())
            {
                if (j.IndexOf("inscribe the spell", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "made";
                if (j.IndexOf("fail to inscribe", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "failed";
                if (j.IndexOf("reagents", StringComparison.OrdinalIgnoreCase) >= 0
                    || j.IndexOf("mana", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "\"" + j + "\"";
            }
            Clock.Sleep(100);
        }
        int after;
        LooseScroll(k, art, out after);
        return after > before ? "made" : "no answer to the inscription";
    }
    // Whether the scribe inscribes the spell now: in the own book, Magery able to cast its circle at all, the mana
    // there to have, and the Inscription chance at the floor.
    bool CanInscribe(GameLink k, int spell, ulong own)
    {
        if ((own & (1UL << spell)) == 0)
            return false;
        int d = CircleDifficulty(spell), maxMana;
        lock (k.State.Gate)
            maxMana = k.State.MaxMana;
        return CheckChance(SkillOf(k, MagerySkillId), d, 40) > 0 && maxMana >= s_circleMana[spell / 8]
            && CheckChance(SkillOf(k, InscribeSkill), d, 50) >= SpellbookChance * 10;
    }
    string RunSpellbooks(GameLink k)
    {
        int bought = 0, filled = 0, inscribed = 0, ruined = 0, stocked = 0;
        string? end = null;
        try
        {
            string? city = CityOf(k);
            if (city == null)
                return end = "start in a town with a mage shop and a bank";
            OpenPackBags(k);
            // The scribe's own book is the pack's spellbook of the most spells; every other spellbook in the pack and
            // every one bought is stock.
            ValueMap<uint, ulong> masks = ValueMap<uint, ulong>.Empty;
            List<uint> books = new();
            foreach (uint b in PackSpellbooks(k))
            {
                CircuitStatus = "Spellbooks: reading " + b.ToString("X");
                if (!masks.ContainsKey(b))
                    books.Add(b);
                masks = masks.Set(b, ReadSpells(k, b));
            }
            uint own = 0;
            foreach (uint b in books)
                if (own == 0 || Bits(masks.Get(b, 0)) > Bits(masks.Get(own, 0)))
                    own = b;
            ulong ownMask = own != 0 ? masks.Get(own, 0) : 0;
            List<uint> stock = books.FindAll(b => b != own);
            List<UoRegions.Shop> shops = UoRegions.ShopsIn(city, s_mageShopKinds);
            int qx, qy;
            lock (k.State.Gate)
            {
                qx = k.State.X;
                qy = k.State.Y;
            }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
            if (shops.Count == 0)
                return end = "no mage shop in " + city;
            Func<List<BuyItem>, string> buy = list =>
            {
                ValueMap<string, int> left = ValueMap<string, int>.Empty;
                foreach (BuyItem i in list)
                    left = left.Set(i.Name!, i.Count);
                foreach (UoRegions.Shop s in shops)
                {
                    if (circuitStop || !list.Exists(i => left.Get(i.Name!, 0) > 0))
                        break;
                    string? bw = BuyInShop(k, s, list, ref left, ref bought);
                    if (bw != null)
                        return bw;
                }
                return null!;
            };
            // The books short of spellbook.count.
            int shortBooks = SpellbookCount - stock.Count;
            if (shortBooks > 0)
            {
                Set<uint> had = Set<uint>.Of(PackSpellbooks(k).ToArray());
                end = buy(new List<BuyItem> { new() { Name = "spellbook", Arts = new[] { SpellbookArt },
                    Count = shortBooks, MaxPrice = SpellbookPrice } });
                if (end != null)
                    return end;
                foreach (uint b in PackSpellbooks(k))
                    if (!had.Contains(b))
                    {
                        masks = masks.Set(b, 0);
                        stock.Add(b);
                    }
            }
            if (stock.Count == 0)
                return end = "no spellbook to fill: none bought in " + city
                    + ", and the pack's only one is the scribe's";
            Func<int, int> lacking = i => { int n = 0; foreach (uint b in stock) if ((masks.Get(b, 0) & (1UL << i))
                == 0) n++; return n; };
            // Every scroll the vendors stock that a book lacks, as many as the books lacking it.
            List<BuyItem> scrolls = new();
            for (int i = 0; i < 64; i++)
            {
                int have;
                LooseScroll(k, ScrollArt(i), out have);
                int n = lacking(i) - have;
                if (n > 0)
                    scrolls.Add(new BuyItem { Name = SpellName(i), Arts = new[] { ScrollArt(i) }, Count = n,
                        MaxPrice = ScrollPrice });
            }
            if (scrolls.Count > 0)
            {
                end = buy(scrolls);
                if (end != null)
                    return end;
            }
            filled += FillBooks(k, stock, ref masks);
            // Then the scribe's inscription, up to the skill: each spell a book still lacks that the scribe may
            // inscribe, the blank scrolls and reagents for the attempts its chance expects bought first.
            if (SpellbookInscribe && own != 0 && SkillOf(k, InscribeSkill) > 0 && !circuitStop)
            {
                List<int> plan = new();
                int attempts = 0;
                ValueMap<int, int> regs = ValueMap<int, int>.Empty;
                List<int> regOrder = new();
                for (int i = 0; i < 64; i++)
                {
                    int n = lacking(i);
                    if (n == 0 || !CanInscribe(k, i, ownMask))
                        continue;
                    int tries = (n * 1000 + CheckChance(SkillOf(k, InscribeSkill), CircleDifficulty(i), 50) - 1)
                        / CheckChance(SkillOf(k, InscribeSkill), CircleDifficulty(i), 50);
                    for (int c = 0; c < n; c++)
                        plan.Add(i);
                    attempts += tries;
                    foreach (int r in SpellRegs(i))
                    {
                        if (!regs.ContainsKey(r))
                            regOrder.Add(r);
                        regs = regs.Set(r, regs.Get(r, 0) + tries);
                    }
                }
                if (plan.Count > 0)
                {
                    List<BuyItem> supplies = new();
                    int blanks = PackCount(k, g => s_blankScrollArts.IndexOf(g) >= 0);
                    if (attempts > blanks)
                        supplies.Add(new BuyItem { Name = "blank scroll", Arts = s_blankScrollArts.ToArray(),
                            Count = attempts - blanks, MaxPrice = 10 });
                    foreach (int r in regOrder)
                    {
                        int need = regs.Get(r, 0), have = PackCount(k, g => g == r);
                        if (need > have)
                            supplies.Add(new BuyItem { Name = UoTiles.Name(r), Arts = new[] { r },
                                Count = need - have, MaxPrice = LoadoutPrice(r) });
                    }
                    if (supplies.Count > 0)
                    {
                        end = buy(supplies);
                        if (end != null)
                            return end;
                    }
                    int again = 0;
                    for (int p = 0; p < plan.Count && !circuitStop && !k.Closed; p++)
                    {
                        int spell = plan[p];
                        if (lacking(spell) == 0)
                            continue;
                        if (!CanInscribe(k, spell, ownMask))
                        {
                            Note("Spellbooks: " + SpellName(spell) + ": the chance is under " + SpellbookChance + "%");
                            continue;
                        }
                        int cost = s_circleMana[spell / 8];
                        DateTime manaBy = Clock.UtcNow.AddMilliseconds(InscribeManaWaitMs);
                        while (!circuitStop)
                        {
                            int mana;
                            lock (k.State.Gate)
                                mana = k.State.Mana;
                            if (mana >= cost)
                                break;
                            if (Clock.UtcNow > manaBy || k.Closed)
                            {
                                end = "mana stayed under " + cost;
                                break;
                            }
                            CircuitStatus = "Spellbooks: waiting for " + cost + " mana";
                            Clock.Sleep(1000);
                        }
                        if (end != null || circuitStop)
                            break;
                        AssistItem? blank = null;
                        lock (k.State.Gate)
                        {
                            Set<uint> inside = PackTree(k);
                            foreach (AssistItem it in k.State.Items.Values)
                                if (inside.Contains(it.Container) && s_blankScrollArts.IndexOf(it.Graphic) >= 0)
                                {
                                    blank = it;
                                    break;
                                }
                        }
                        if (blank == null)
                        {
                            end = "out of blank scrolls";
                            break;
                        }
                        bool regsHeld = Array.TrueForAll(SpellRegs(spell), r => PackCount(k, g => g == r) > 0);
                        if (!regsHeld)
                        {
                            Note("Spellbooks: " + SpellName(spell) + ": a reagent is short");
                            continue;
                        }
                        CircuitStatus = "Spellbooks: inscribing " + SpellName(spell) + " ("
                            + (SkillOf(k, InscribeSkill) / 10.0).ToString("0.0") + ")";
                        string res = InscribeOne(k, spell, blank.Serial);
                        if (res == "again")
                        {
                            if (++again > InscribeAgainMax)
                            {
                                end = "the server asked to wait " + again + " times in a row";
                                break;
                            }
                            Clock.Sleep(BusyMs);
                            p--;
                            continue;
                        }
                        again = 0;
                        // A failed check is the one repeat the answer asks for; any other answer ends the inscribing
                        // (L-FUTILITY).
                        if (res == "failed")
                        {
                            ruined++;
                            p--;
                            continue;
                        }
                        if (res != "made")
                        {
                            end = "inscribing " + SpellName(spell) + ": " + res;
                            break;
                        }
                        inscribed++;
                        filled += FillBooks(k, stock, ref masks);
                    }
                }
            }
            // The stock into the bank: the box's container already holding the most spellbooks, else the box.
            if (!circuitStop && !k.Closed)
            {
                string? sw = StockSpellbooks(k, city, stock, out stocked);
                if (sw != null)
                    end = (end != null ? end + "; " : "") + "stocking: " + sw;
            }
            if (end == null)
                end = circuitStop ? "stopped" : "done";
            return end;
        }
        finally
        {
            m_spellbookMode = false;
            SpellbookStatus = bought + " bought, " + filled + " spells into books, " + inscribed + " inscribed, "
                + ruined + " ruined, " + stocked + " books stocked; Inscription "
                + (SkillOf(k, InscribeSkill) / 10.0).ToString("0.0") + (end != null ? "; " + end : "");
            Note("Spellbooks: " + SpellbookStatus);
        }
    }
    // ---------- the rune marker ----------
    // "Mark runes" (circuit mark; Damian, 2026-10-18: "mark a rune to a spot at each of a class like banks, tailors,
    // etc. and we use smart randomizing selecting the spots so our customers dont end up blocking each other all buying
    // the same mark"): every place of the class (mark.class: bank, or a shop kind as the region file names it) in every
    // town, the town here first, mark.per runes each. Blank runes are the pack's runes a click reads as unmarked; runes
    // short are bought at the town's mage shops (mark.price, 30 unset, twice ServUO's 15). A rune is marked by a Mark
    // scroll in the pack when there is one (Kwik's Magery fizzles the spell), else by the spell; a fizzle is cast
    // again, any other answer ends it (L-FUTILITY). Each rune is then renamed "<place> <n>". The spots: standable tiles
    // inside the bank's or shop's region, the first at random, each next the farthest from every spot marked there
    // before (this run and earlier ones, marks-<server>.txt), picked at random among the best quarter, so two runes of
    // one place never land a buyer on the same tile.
    public const int MarkSpell = 45, MarkMana = 20, MarkTries = 5;
    public string MarkClass
    {
        get => Cfg.Get(Server + "mark.class", "bank");
        set => Cfg.Set(Server + "mark.class", (value ?? "bank").Trim().ToLowerInvariant());
    }
    public int MarkPer
    {
        get => Math.Max(1, Cfg.GetInt(Server + "mark.per", 1));
        set => Cfg.Set(Server + "mark.per", Math.Max(1, value).ToString());
    }
    public int MarkPrice
    {
        get => Math.Max(1, Cfg.GetInt(Server + "mark.price", 30));
        set => Cfg.Set(Server + "mark.price", Math.Max(1, value).ToString());
    }
    public static List<string> MarkClasses()
    {
        List<string> l = new()
        { "bank" };
        foreach (string c in UoRegions.Cities())
            foreach (UoRegions.Shop s in UoRegions.AllShopsIn(c))
            {
                string? kd = UoRegions.ShopKind(s.CX, s.CY);
                if (kd != null && !l.Contains(kd))
                    l.Add(kd);
            }
        l.Sort((a, b) => a == "bank" ? -1 : b == "bank" ? 1 : string.CompareOrdinal(a, b));
        return l;
    }
    volatile bool m_markMode; public volatile string MarkStatus = "";
    public string? CircuitMark()
    {
        return StartCircuit(() => m_markMode = true);
    }
    string MarksPath => ServerFile("marks");
    Map<string, List<int[]>> m_marks = Map<string, List<int[]>>.Empty; bool m_marksLoaded;
    readonly Lock m_marksGate = new();
    List<int[]> MarksAt(string place)
    {
        lock (m_marksGate)
        {
            LoadOnce(ref m_marksLoaded, MarksPath,
                line => { string[] t = line.Split('\t'); if (t.Length
                != 2) return; string[] xy = t[1].Split(','); int x, y; if (xy.Length == 2 && int.TryParse(xy[0], out x)
                && int.TryParse(xy[1],
                out y)) { List<int[]>? l; if (!m_marks.TryGet(t[0],
                out l)) m_marks = m_marks.Set(t[0], l = new List<int[]>()); l.Add(new[] { x, y }); } });
            List<int[]>? got;
            if (!m_marks.TryGet(place, out got))
                m_marks = m_marks.Set(place, got = new List<int[]>());
            return got;
        }
    }
    void MarkedAt(string place, int x, int y)
    {
        lock (m_marksGate)
        {
            MarksAt(place).Add(new[] { x, y });
            try
            {
                File.AppendAllText(MarksPath, place + "\t" + x + "," + y + Environment.NewLine);
            }
            catch { }
        }
    }
    readonly Random m_markRandom = new();
    // The next spot of a place: among its standable, unoccupied tiles, the farthest from every spot marked there, one
    // of the best quarter at random; null when the place has no standable tile.
    int[]? MarkSpot(GameLink k, string place, List<int[]> rects)
    {
        List<int[]> used = MarksAt(place);
        int[]? entrance = EntranceSpot(k, rects, used);
        if (entrance != null)
            return entrance;
        List<int[]> cands = new();
        foreach (int[] r in rects)
        {
            for (int x = r[0]; x < r[0] + r[2]; x++)
            {
                for (int y = r[1]; y < r[1] + r[3]; y++)
                {
                    int z = UoMap.StandZ(x, y, UoMap.LandStandZ(x, y));
                    if (z == int.MinValue || Occupied(k, x, y))
                        continue;
                    int d = int.MaxValue;
                    foreach (int[] u in used)
                        d = Math.Min(d, Tiles(x, y, u[0], u[1]));
                    cands.Add(new[] { x, y, d });
                }
            }
        }

        if (cands.Count == 0)
            return null;
        cands.Sort((a, b) => b[2].CompareTo(a[2]));
        int top = Math.Max(1, cands.Count / 4);
        int[] pick = cands[m_markRandom.Next(top)];
        return new[] { pick[0], pick[1] };
    }
    // A rune's spot outside the place (Damian, 2026-10-19: "start marking our runes to shops 2 steps outside its front
    // door, if it has one, or just outside its main enterance ... both our vesper runes got screwed when the npc
    // wandered onto our spot and got stuck there"): each door on or beside the place's rectangles (a map static, or a
    // door item in view) whose way out is standable two steps on, else each edge tile of the place a step leaves into
    // the open; its spot two steps out. The front one is the spot with the most open standable ground around it
    // (EntranceOpenR). A later rune of the place takes the nearest free tile outside within EntranceSpread of it, never
    // a door's, never beside a spot marked before. Null when the place shows no way out.
    // EntranceView: the place is walked to within this many tiles before its spot is chosen, inside the client's view.
    public const int EntranceOpenR = 2, EntranceSpread = 3, EntranceView = 8;
    int[]? EntranceSpot(GameLink k, List<int[]> rects, List<int[]> used)
    {
        return EntranceSpot(rects, used, BlockersNow(k).Ground, (x, y) => Occupied(k, x, y));
    }
    // ground: the items in view by tile (art first), occupied: someone stands there.
    static int[]? EntranceSpot(List<int[]> rects, List<int[]> used, Map<long, List<int[]>> ground,
        Func<int, int, bool> occupied)
    {
        int[] DX = { 0, 1, 0, -1 }, DY = { -1, 0, 1, 0 };
        Func<int, int, bool> stands = (x, y) => UoMap.StandZ(x, y, UoMap.LandStandZ(x, y)) != int.MinValue;
        Set<long> doors = Set<long>.Empty;
        List<long> doorOrder = new();
        foreach (int[] r in rects)
        {
            for (int x = r[0] - 1; x <= r[0] + r[2]; x++)
            {
                for (int y = r[1] - 1; y <= r[1] + r[3]; y++)
                {
                    long t = ((long)x << 16) | (uint)y;
                    List<int[]>? on;
                    if ((UoMap.StaticsAt(x, y).Exists(s => IsDoor(s[0]))
                        || (ground.TryGet(t, out on) && on.Exists(g => IsDoor(g[0])))) && !doors.Contains(t))
                    {
                        doors = doors.Add(t);
                        doorOrder.Add(t);
                    }
                }
            }
        }

        List<int[]> spots = new();
        if (doors.Count > 0)
        {
            foreach (long d in doorOrder)
            {
                int x = (int)(d >> 16), y = (int)(d & 0xFFFF);
                for (int i = 0; i < 4; i++)
                {
                    int ox = x + DX[i], oy = y + DY[i], sx = x + 2 * DX[i], sy = y + 2 * DY[i];
                    if (!InRects(rects, ox, oy) && !InRects(rects, sx, sy) && stands(ox, oy) && stands(sx, sy))
                        spots.Add(new[] { sx, sy });
                }
            }
        }
        // No door, or none opening outward within the place's rectangles (The Right Fit, The Bubbling Brew): its open
        // edges.
        if (spots.Count == 0)
        {
            foreach (int[] r in rects)
            {
                for (int x = r[0]; x < r[0] + r[2]; x++)
                {
                    for (int y = r[1]; y < r[1] + r[3]; y++)
                    {
                        if (!stands(x, y))
                            continue;
                        int z = UoMap.StandZ(x, y, UoMap.LandStandZ(x, y));
                        for (int i = 0; i < 4; i++)
                        {
                            int ox = x + DX[i], oy = y + DY[i], sx = x + 2 * DX[i], sy = y + 2 * DY[i];
                            if (InRects(rects, ox, oy) || InRects(rects, sx, sy)
                                || UoMap.StepZ(x, y, z, ox, oy) == int.MinValue || !stands(sx, sy))
                                continue;
                            spots.Add(new[] { sx, sy });
                        }
                    }
                }
            }
        }
        if (spots.Count == 0)
            return null;
        int[]? front = null;
        int open = -1;
        foreach (int[] s in spots)
        {
            int n = 0;
            for (int x = s[0] - EntranceOpenR; x <= s[0] + EntranceOpenR; x++)
                for (int y = s[1] - EntranceOpenR; y <= s[1] + EntranceOpenR; y++)
                    if (!InRects(rects, x, y) && stands(x, y))
                        n++;
            if (n > open)
            {
                open = n;
                front = s;
            }
        }
        int[]? best = null;
        int bestD = int.MaxValue;
        for (int x = front![0] - EntranceSpread; x <= front[0] + EntranceSpread; x++)
        {
            for (int y = front[1] - EntranceSpread; y <= front[1] + EntranceSpread; y++)
            {
                if (InRects(rects, x, y) || doors.Contains(((long)x << 16) | (uint)y) || !stands(x, y)
                    || occupied(x, y) || used.Exists(u => Tiles(u[0], u[1], x, y) <= 1))
                    continue;
                int d = Tiles(x, y, front[0], front[1]);
                if (d < bestD)
                {
                    bestD = d;
                    best = new[] { x, y };
                }
            }
        }

        return best;
    }
    // The pack's runes a click reads as unmarked.
    List<uint> BlankRunes(GameLink k)
    {
        List<uint> l = new();
        foreach (string line in PackRuneLabels(k))
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0)
                continue;
            string label = line.Substring(sp + 1).ToLowerInvariant();
            if (label.Contains("unmarked") || label.Contains("blank"))
                l.Add(Convert.ToUInt32(line.Substring(0, sp), 16));
        }
        return l;
    }
    // One rune marked where the player stands: a Mark scroll used, else the spell cast, the cursor aimed at the rune;
    // done when its label is no longer unmarked. Null, or why not.
    string? MarkRune(GameLink k, uint rune)
    {
        int scrollArt = ScrollArt(44);
        string? fh = FreeHands(k);
        if (fh != null)
            return "the hands before the Mark: " + fh;
        for (int attempt = 1; attempt <= MarkTries && !circuitStop; attempt++)
        {
            foreach (int r in s_recallReagents)
                if (PackCount(k, g => g == r) == 0 && PackCount(k, g => g == scrollArt) == 0)
                    return "no " + UoTiles.Name(r) + " in the pack for Mark";
            AssistItem? scroll = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && it.Graphic == scrollArt)
                    {
                        AssistItem? c;
                        if (k.State.Items.TryGet(it.Container, out c) && c.Graphic == SpellbookArt)
                            continue;
                        scroll = it;
                        break;
                    }
            }
            DateTime manaBy = Clock.UtcNow.AddMilliseconds(ManaWaitMs);
            while (true)
            {
                int mana;
                lock (k.State.Gate)
                    mana = k.State.Mana;
                if (mana >= MarkMana)
                    break;
                if (Clock.UtcNow > manaBy || k.Closed)
                    return "mana stayed under " + MarkMana;
                CircuitStatus = "Mark: waiting for " + MarkMana + " mana";
                Clock.Sleep(500);
            }
            Heard mark;
            BusyWait(clickedAt);
            using (Act(k))
            {
                mark = new(k.State);
                string? why = scroll != null ? k.Use(scroll.Serial, true) : k.Cast(MarkSpell, true);
                clickedAt = Clock.UtcNow;
                if (why != null)
                    return why;
                DateTime until = Clock.UtcNow.AddMilliseconds(CastCursorMs);
                AwaitCursor(k, until, mark);
                if (k.MacroCursor)
                {
                    AssistItem? it = Item(k, rune);
                    if (it == null)
                    {
                        k.Settle();
                        return "the rune " + rune.ToString("X") + " is not in sight";
                    }
                    why = k.Target(false, rune, it.X, it.Y, it.Z, it.Graphic);
                    if (why != null)
                        return "aiming at the rune: " + why;
                }
            }
            Clock.Sleep(AnswerMs);
            List<string> heard = mark.Answers();
            string label = AllLabels(k, rune).ToLowerInvariant();
            if (label.Length > 0 && !label.Contains("unmarked") && !label.Contains("blank"))
                return null;
            if (heard.Exists(j => j.IndexOf("fizzle", StringComparison.OrdinalIgnoreCase) >= 0
                || j.StartsWith("You must wait", StringComparison.OrdinalIgnoreCase)))
            {
                Clock.Sleep(Ms("busy:recall"));
                continue;
            }
            string? said = null;
            foreach (string j in heard)
                said = j;
            return "the Mark did not take" + (said != null ? " (\"" + said + "\")" : "");
        }
        return "the Mark fizzled " + MarkTries + " times";
    }
    string RunMark(GameLink k)
    {
        int marked = 0, bought = 0;
        string? end = null;
        string cls = MarkClass;
        int per = MarkPer;
        try
        {
            string? here = CityOf(k);
            if (here == null)
                return end = "start in a town with a mage shop";
            // Every town holding the class, the town here first.
            List<string> towns = new();
            foreach (string c in UoRegions.Cities())
            {
                if (Banned(c))
                    continue;
                bool has = cls == "bank" ? UoRegions.BanksIn(c, 0, 0).Count > 0
                    : UoRegions.ShopsIn(c, new[] { cls.ToUpperInvariant() }).Count > 0;
                if (has)
                    towns.Add(c);
            }
            towns.Remove(here);
            if (cls == "bank" ? UoRegions.BanksIn(here, 0, 0).Count > 0
                : UoRegions.ShopsIn(here, new[] { cls.ToUpperInvariant() }).Count > 0)
                towns.Insert(0, here);
            if (towns.Count == 0)
                return end = "no town holds a " + cls;
            int places = 0;
            foreach (string c in towns)
                places += cls == "bank" ? UoRegions.BanksIn(c, 0, 0).Count
                    : UoRegions.ShopsIn(c, new[] { cls.ToUpperInvariant() }).Count;
            // The runes short, bought here.
            OpenPackBags(k);
            List<uint> blanks = BlankRunes(k);
            int need = places * per - blanks.Count;
            if (need > 0)
            {
                List<BuyItem> list = new()
                { new BuyItem { Name = "recall rune", Arts = new[] { 0x1F14 }, Count = need, MaxPrice = MarkPrice } };
                ValueMap<string, int> left = ValueMap<string, int>.Of(("recall rune", need));
                List<UoRegions.Shop> shops = UoRegions.ShopsIn(here, s_mageShopKinds);
                int qx, qy;
                lock (k.State.Gate)
                {
                    qx = k.State.X;
                    qy = k.State.Y;
                }
                shops.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
                foreach (UoRegions.Shop s in shops)
                {
                    if (circuitStop || left.Get("recall rune", 0) <= 0)
                        break;
                    end = BuyInShop(k, s, list, ref left, ref bought);
                    if (end != null)
                        return end;
                }
                blanks = BlankRunes(k);
            }
            if (blanks.Count == 0)
                return end = "no blank rune in the pack, and none bought in " + here;
            foreach (string town in towns)
            {
                if (circuitStop || blanks.Count == 0)
                    break;
                if (CityOf(k) != town)
                {
                    string? tr = TripReady(k);
                    if (tr != null)
                    {
                        end = tr;
                        break;
                    }
                    CircuitStatus = "Mark: travelling to " + town;
                    string? tw = BankRecall(k, town, false);
                    if (tw != null)
                    {
                        Note("Mark: " + town + ": " + tw);
                        if (TripStops(tw))
                        {
                            end = "travelling to " + town + ": " + tw;
                            break;
                        }
                        continue;
                    }
                    if (CityOf(k) != town)
                    {
                        end = "the rune for " + town + " landed in " + (CityOf(k) ?? "no town");
                        break;
                    }
                }
                List<KeyValuePair<string, List<int[]>>> spots = new();
                if (cls == "bank")
                {
                    int n = 0;
                    foreach (int[] b in UoRegions.BanksIn(town, 0, 0))
                    {
                        n++;
                        spots.Add(new KeyValuePair<string, List<int[]>>(town + " bank" + (n > 1 ? " " + n : ""),
                            UoRegions.BankRects(b[0], b[1])));
                    }
                }
                else
                {
                    foreach (UoRegions.Shop s in UoRegions.ShopsIn(town, new[] { cls.ToUpperInvariant() }))
                        spots.Add(new KeyValuePair<string, List<int[]>>(s.Text,
                            new List<int[]> { new[] { s.X, s.Y, s.W, s.H } }));
                }

                foreach (KeyValuePair<string, List<int[]>> place in spots)
                {
                    // The place in view first, so its door items are known to EntranceSpot.
                    {
                        int[] r0 = place.Value[0];
                        int cx = r0[0] + r0[2] / 2, cy = r0[1] + r0[3] / 2, px, py;
                        lock (k.State.Gate)
                        {
                            px = k.State.X;
                            py = k.State.Y;
                        }
                        if (Tiles(px, py, cx, cy) > EntranceView)
                        {
                            CircuitStatus = "Mark: walking to " + place.Key;
                            string? vw = WalkTo(cx, cy, EntranceView);
                            if (vw != null)
                                Note("Mark: " + place.Key + ": " + vw);
                        }
                    }
                    // The rune's number: the place's runes marked so far, in the file, plus this run's.
                    int recorded = 0;
                    try
                    {
                        if (File.Exists(MarksPath))
                            foreach (string line in File.ReadAllLines(MarksPath))
                                if (line.StartsWith(place.Key + "\t"))
                                    recorded++;
                    }
                    catch { }
                    int[] markedHere = new[] { recorded };
                    for (int i = 0; i < per && blanks.Count > 0 && !circuitStop; i++)
                    {
                        int[]? spot = MarkSpot(k, place.Key, place.Value);
                        if (spot == null)
                        {
                            Note("Mark: " + place.Key + ": no tile to stand on");
                            break;
                        }
                        CircuitStatus = "Mark: walking to " + place.Key + " (" + spot[0] + "," + spot[1] + ")";
                        string? ww = WalkTo(spot[0], spot[1], 0);
                        // A spot not reached is passed over for this run only, not recorded as marked.
                        if (ww != null)
                        {
                            Note("Mark: " + place.Key + ": " + spot[0] + "," + spot[1] + " not reached: " + ww);
                            lock (m_marksGate)
                                MarksAt(place.Key).Add(new[] { spot[0], spot[1] });
                            continue;
                        }
                        uint rune = blanks[0];
                        CircuitStatus = "Mark: marking a rune at " + place.Key;
                        string? mw = MarkRune(k, rune);
                        if (mw != null)
                        {
                            end = place.Key + ": " + mw;
                            break;
                        }
                        blanks.RemoveAt(0);
                        marked++;
                        MarkedAt(place.Key, spot[0], spot[1]);
                        int n = ++markedHere[0];
                        string? rw = Rename(rune, place.Key + " " + n);
                        if (rw != null)
                            Note("Mark: renaming the rune for " + place.Key + ": " + rw);
                        Note("Mark: " + place.Key + " " + n + " at " + spot[0] + "," + spot[1]);
                    }
                    if (end != null || circuitStop)
                        break;
                }
                if (end != null)
                    break;
            }
            if (end == null)
                end = circuitStop ? "stopped" : marked + " runes marked";
            return end;
        }
        finally { m_markMode = false; MarkStatus = bought + " runes bought, " + marked + " marked (" + cls + ", " + per
            + " a place)" + (end != null ? "; " + end : ""); Note("Mark: " + MarkStatus); }
    }
    string? StockSpellbooks(GameLink k, string city, List<uint> books, out int stocked)
    {
        stocked = 0;
        string? why = ToCityBank(k, city, "the spellbooks");
        if (why != null)
            return "the bank: " + why;
        byte[]? open;
        using (Act(k))
        {
            open = SayBank(k);
        }
        if (open == null)
            return NoBanker();
        uint box = BankBox(open);
        List<uint> bags = BoxBags(k, box);
        uint store = box;
        int most = 0;
        lock (k.State.Gate)
        {
            ValueMap<uint, int> n = ValueMap<uint, int>.Empty;
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Graphic == SpellbookArt && bags.Contains(it.Container))
                    n = n.Set(it.Container, n.Get(it.Container, 0) + 1);
            foreach (KeyValuePair<uint, int> kv in n)
                if (kv.Value > most)
                {
                    most = kv.Value;
                    store = kv.Key;
                }
        }
        foreach (uint bs in BankBags("bankstore"))
            if (bags.Contains(bs))
            {
                store = bs;
                break;
            }
        List<uint> move = books.FindAll(s => Item(k, s) != null);
        string? w = MoveInto(move, store);
        if (w != null)
            return w;
        stocked = move.Count;
        Note("Spellbooks: " + stocked + " books into " + store.ToString("X") + " in the bank box");
        return null;
    }
    // At the city's bank: the bought goods into the bank (into the container in the box already holding the most of
    // them, else the box itself), ReagentRefill of each recall reagent kept in the pack for travel, and the purse
    // brought to BuyPurse: the gold over it onto the box's piles (BankDeposit), the gold short of it from the box's own
    // piles, never a bag's (Damian, 2026-10-17: "keep storing and pulling from the now smaller top level pile of
    // gold"). Null, or why not. The survey (Damian, 2026-10-17: "visit all the vendors in a town and just get their
    // inventory. if not current town, recall there, and end at a bank. no transactions, just data collection"): the
    // town's shops nearest first, each walked into, its people named by a click, and each keeper asked "buy" once by
    // name, which sends its stock and prices to the atlas; nothing is bought. Then the walk to the town's nearest bank,
    // where nothing is said. A keeper that gives no proper answer ends it (assist-plan.md rule 1). Each shop's line
    // goes into reports\survey-<server>-<time>.txt. The shop the player stands in surveyed while there (Damian,
    // 2026-10-18: "this store hasn't been properly surveyed"; "that should have happenend"): its keepers named by a
    // click, and each one whose stock the atlas never read asked "buy" once, nothing bought; a keeper is asked so once
    // a session (L-FUTILITY). A keeper that gives no proper answer ends it.
    Set<uint> m_surveyedKeepers = Set<uint>.Empty; readonly Lock m_surveyedKeepersGate = new();
    void SurveyHere(GameLink k)
    {
        if (Atlas == null)
            return;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        string? city = UoRegions.City(px, py);
        if (city == null)
            return;
        // The smallest shop holding the player: HOUSING_NO_JHELOM, "Jhelom Islands", held the whole island and sent the
        // tailor to a jeweller and a weaponsmith (2026-10-18 00:18:40).
        UoRegions.Shop? shop = null;
        foreach (UoRegions.Shop s in UoRegions.AllShopsIn(city))
            if (s.Holds(px, py, 1) && (shop == null || s.W * s.H < shop.W * shop.H))
                shop = s;
        if (shop == null)
            return;
        NameUnnamed(VendorHear * 4);
        foreach (AtlasNpc v in Atlas.Shops().FindAll(n =>
            n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && n.Items.Count == 0
            && (Clock.UtcNow.ToLocalTime() - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs))
        {
            if (circuitStop)
                return;
            lock (m_surveyedKeepersGate)
            {
                if (m_surveyedKeepers.Contains(v.Serial))
                    continue;
                m_surveyedKeepers = m_surveyedKeepers.Add(v.Serial);
            }
            string name = v.Name.Split(' ')[0];
            int got, stock;
            CircuitStatus = "Reading " + name + "'s stock in " + UoRegions.Display(shop);
            string? bw = VendorBuy(g => false, 0, out got, out stock, name);
            AtlasNpc? now = Atlas.Shops().Find(n => n.Serial == v.Serial);
            Note("Survey: " + UoRegions.Display(shop) + ", " + name + ": "
                + (bw ?? (now == null ? 0 : now.Items.Count) + " items listed"));
            if (Silent(bw!))
                return;
        }
    }
    string RunSurvey(GameLink k)
    {
        List<string> report = new();
        string? end = null;
        int read = 0;
        string? town = SurveyTarget();
        try
        {
            if (town == null)
                return "no town to survey: stand in one or pick one";
            if (CityOf(k) != town)
            {
                string? ready = TripReady(k);
                if (ready != null)
                    return ready;
                CircuitStatus = "Survey: travelling to " + town;
                string? tw = BankRecall(k, town, false);
                if (tw != null)
                    return "travelling to " + town + ": " + tw;
                if (CityOf(k) != town)
                    return "the rune for " + town + " landed in " + (CityOf(k) ?? "no city");
            }
            List<UoRegions.Shop> left = SurveyShops();
            Set<uint> asked = Set<uint>.Empty;
            while (left.Count > 0 && !circuitStop)
            {
                int px, py;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                int qx = px, qy = py;
                left.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
                UoRegions.Shop shop = left[0];
                left.RemoveAt(0);
                CircuitStatus = "Survey: " + UoRegions.Display(shop);
                string? w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
                if (w != null)
                {
                    report.Add(UoRegions.Display(shop) + ": not reached: " + w);
                    continue;
                }
                MobileLabels(VendorHear * 4);
                List<AtlasNpc> keepers = Atlas == null ? new List<AtlasNpc>()
                    : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2)
                    && (Clock.UtcNow.ToLocalTime() - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
                if (keepers.Count == 0)
                {
                    report.Add(UoRegions.Display(shop) + ": no shopkeeper known by name");
                    continue;
                }
                foreach (AtlasNpc v in keepers)
                {
                    if (circuitStop || asked.Contains(v.Serial))
                        continue;
                    asked = asked.Add(v.Serial);
                    string name = v.Name.Split(' ')[0];
                    int got, stock;
                    CircuitStatus = "Survey: " + name + "'s stock in " + UoRegions.Display(shop);
                    // Wanting none of anything: the buy window is read and nothing is picked.
                    string? bw = VendorBuy(g => false, 0, out got, out stock, name);
                    if (Silent(bw!))
                    {
                        report.Add(UoRegions.Display(shop) + ", " + name + ": " + bw);
                        end = "asking " + name + ": " + bw;
                        break;
                    }
                    if (bw != null && bw.StartsWith("away: "))
                    {
                        report.Add(UoRegions.Display(shop) + ", " + name + ": " + bw);
                        Note("Survey: " + report[report.Count - 1]);
                        continue;
                    }
                    AtlasNpc? now = Atlas!.Shops().Find(n => n.Serial == v.Serial);
                    int items = now == null ? 0 : now.Items.Count;
                    if (items > 0)
                        read++;
                    report.Add(UoRegions.Display(shop) + ", " + name + ": " + items + " items listed"
                        + (items > 0
                        ? " (" + string.Join(", ", now!.Items.GetRange(0, Math.Min(4, items)))
                        + (items > 4 ? ", ..." : "") + ")" : ""));
                    Note("Survey: " + report[report.Count - 1]);
                }
                if (end != null)
                    break;
            }
            // End at a bank: walked to, nothing said.
            if (end == null && !circuitStop)
            {
                int x, y;
                lock (k.State.Gate)
                {
                    x = k.State.X;
                    y = k.State.Y;
                }
                List<int[]> banks = CityBanks(town, x, y);
                string? bw = banks.Count == 0 ? "no bank known in " + town : null;
                foreach (int[] b in banks)
                {
                    if (BankSkipped(b))
                        continue;
                    CircuitStatus = "Survey: walking to the bank";
                    bw = ToBanker(k, b, false);
                    if (bw == null)
                        break;
                    SkipBank(b);
                }
                if (bw != null)
                    report.Add("the bank: " + bw);
            }
        }
        finally
        {
            m_surveyMode = false;
            TourReport = string.Join("\r\n", report);
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,
                    "survey-" + ServerSafe + "-" + Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmm") + ".txt"),
                    TourReport);
            }
            catch { }
            Note("Survey: " + (town ?? "no town") + ", " + read + " stock lists read");
        }
        return end ?? (circuitStop ? "stopped" : "survey of " + town + " done: " + read + " stock lists read");
    }
}
