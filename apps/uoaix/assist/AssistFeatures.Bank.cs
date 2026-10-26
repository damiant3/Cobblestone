#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the bank (assist-plan.md stage 6) ----------
    // Damian, 2026-10-17: "walk to the bank wall, say bank, and deposit the money carefully not to overflow the pile of
    // gold there, but to use it if possible since there are limits to items and weight as well". A banker hears "bank"
    // through the bank's walls, so the walk ends BankHear tiles from the nearest banker. The pack keeps PurseKeep gold
    // for thread; the rest goes onto the bank's gold piles: the fullest pile that takes it all under BankStackMax, else
    // the pile with the most room (filled to BankStackMax), and a new pile only when every pile is full.
    // BankOver: a bank the circuit would walk to only for the deposit (after a sale, before a wait) is walked to only
    // above this; at a recall's bank the gold over PurseKeep goes in.
    // BankOverDefault: 5000 (Damian, 2026-10-19: "make the banking interupt threshold 5k").
    public const int BankStackMax = 60000, BankHear = 8, PurseKeepDefault = 300, BankOverDefault = 5000;
    // Both set on the Circuits tab (Damian, 2026-10-18: "need a configurable bank gold amount control in ui"):
    // bank.keep and bank.over; the walk for a deposit never starts under what the purse keeps.
    public int PurseKeep
    {
        get => Math.Max(0, Cfg.GetInt(Server + "bank.keep", PurseKeepDefault));
        set => Cfg.Set(Server + "bank.keep", Math.Max(0, value).ToString());
    }
    public int BankOver
    {
        get => Math.Max(PurseKeep, Cfg.GetInt(Server + "bank.over", BankOverDefault));
        set => Cfg.Set(Server + "bank.over", Math.Max(0, value).ToString());
    }
    // The gold over the purse into the box whenever it is open, whatever it was opened for (Damian, 2026-10-18: "we got
    // 5200 in the bag and we just left the bank with it for no reason"; the box was opened for sewing kits at
    // 03:16:34). The buy circuit's purse stays in the pack (2026-10-19 22:53: 609 gold taken up to the purse, then 600
    // put back here 7 s later); its last bank brings the purse down to PurseKeep itself (BuyBank, final).
    void DepositWhileOpen(GameLink k)
    {
        int keep = m_buyMode ? Math.Max(PurseKeep, BuyPurse)
            : KeepRules.PackKeeps(PurseKeep, m_keepPurseNeed, m_keepMode && CircuitRunning);
        if (PackCount(k, g => g == GoldArt) <= keep)
            return;
        int put;
        string? w = BankDeposit(keep, out put);
        Note(w == null ? "Bank: " + put + " gold in while the box was open"
            : "Bank: the gold, while the box was open: " + w);
    }
    public string? BankDeposit(int keep, out int put)
    {
        put = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        // Near enough a banker to be heard, unless the box is open from here already (ToBanker).
        if (Atlas != null && !BankFresh(k))
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            AtlasNpc? banker = null;
            foreach (AtlasNpc n in Atlas.Visible())
                if (n.Type == "Banker"
                    && (banker == null || Tiles(n.X, n.Y, px, py) < Tiles(banker.X, banker.Y, px, py)))
                    banker = n;
            // uo1998's bankers carry no "the banker" title (Britain's: Blythe, Francis, Elwood, Aida), so an unknown
            // banker is asked from here: the answer, or none, settles it.
            if (banker != null && Tiles(banker.X, banker.Y, px, py) > BankHear)
            {
                string? w = WalkTo(banker.X, banker.Y, BankHear);
                if (w != null)
                    return "walking to the bank: " + w;
            }
        }
        using (Act(k))
        {
            byte[]? open = SayBank(k);
            if (open == null)
                return NoBanker();
            uint box = BankBox(open);
            for (int round = 0; round < 20; round++)
            {
                int purse = PackCount(k, g => g == GoldArt);
                lock (k.State.Gate)
                    purse = Math.Max(purse, k.State.Gold);
                if (purse <= keep)
                    break;
                AssistItem? stack = null;
                lock (k.State.Gate)
                {
                    Set<uint> inside = PackTree(k);
                    foreach (AssistItem it in k.State.Items.Values)
                        if (inside.Contains(it.Container) && it.Graphic == GoldArt
                            && (stack == null || it.Amount > stack.Amount))
                            stack = it;
                }
                if (stack == null)
                    return "the pack's gold is not known: open the pack once";
                int amount = Math.Min(Math.Max(1, stack.Amount), purse - keep);
                AssistItem? fits = null, roomiest = null;
                lock (k.State.Gate)
                {
                    foreach (AssistItem it in k.State.Items.Values)
                    {
                        if (it.Container == box && it.Graphic == GoldArt)
                        {
                            int a = Math.Max(1, it.Amount);
                            if (a + amount <= BankStackMax && (fits == null || a > fits.Amount))
                                fits = it;
                            if (a < BankStackMax && (roomiest == null || a < roomiest.Amount))
                                roomiest = it;
                        }
                    }
                }

                AssistItem? onto = fits ?? roomiest;
                if (onto != null && fits == null)
                    amount = Math.Min(amount, BankStackMax - Math.Max(1, onto.Amount));
                string? why = MoveStack(k, stack.Serial, amount, "gold", "Depositing gold",
                    () => k.Drop(stack.Serial, onto != null ? onto.Serial : box)!);
                if (why != null)
                    return why;
                int after = PackCount(k, g => g == GoldArt);
                lock (k.State.Gate)
                    after = Math.Min(after, k.State.Gold);
                if (after >= purse)
                    return "the gold did not leave the pack";
                put += purse - after;
                Note("Bank: put " + (purse - after) + " gold "
                    + (onto != null ? "on the pile " + onto.Serial.ToString("X") : "in a new pile") + "; " + after
                    + " in the pack");
            }
            return null;
        }
    }

    // Where an item of art goes in the pack so it costs no item of the pack's limit: onto the pack's biggest stack of
    // it (a drop on a stack joins it), else into the bag already holding any of arts (Damian, 2026-10-17: "you have bag
    // inside your backpack with reagents in them"), else the pack itself. except: a stack not to drop onto.
    uint StackOnto(GameLink k, int art, ICollection<int> arts, uint except)
    {
        // An item that does not stack (no 0x800 in the 1.25 tile data, a recall rune) is refused when dropped on
        // another.
        List<UoTiles.Tile> tiles = UoTiles.Items();
        bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            AssistItem? best = null;
            uint bag = 0;
            foreach (AssistItem it in k.State.Items.Values)
            {
                if (!inside.Contains(it.Container) || it.Serial == except)
                    continue;
                if (stacks && it.Graphic == art && (best == null || it.Amount > best.Amount))
                    best = it;
                if (bag == 0 && it.Container != k.State.Backpack && arts.Contains(it.Graphic))
                    bag = it.Container;
            }
            return best != null ? best.Serial : bag != 0 ? bag : k.State.Backpack;
        }
    }
    // Every stack of each art in the pack and its bags joined onto the biggest one (Damian, 2026-10-17: "merge those
    // loose reagents with the bag"). Null, or why not; how many stacks were joined comes back.
    public string? PackMerge(List<int> arts, out int joined)
    {
        joined = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        using (Act(k))
        {
            foreach (int art in arts)
            {
                for (int round = 0; round < 20 && !circuitStop; round++)
                {
                    AssistItem? small = null;
                    int stacks = 0;
                    lock (k.State.Gate)
                    {
                        Set<uint> inside = PackTree(k);
                        foreach (AssistItem it in k.State.Items.Values)
                            if (inside.Contains(it.Container) && it.Graphic == art)
                            {
                                stacks++;
                                if (small == null || it.Amount < small.Amount)
                                    small = it;
                            }
                    }
                    if (stacks < 2)
                        break;
                    uint onto = StackOnto(k, art, arts, small!.Serial);
                    string? why = MoveStack(k, small.Serial, Math.Max(1, small.Amount), UoTiles.Name(art),
                        "Joining " + UoTiles.Name(art), () => k.Drop(small.Serial, onto)!);
                    if (why != null)
                        return why;
                    if (Item(k, small.Serial) != null && Item(k, small.Serial)!.Container != onto)
                        return UoTiles.Name(art) + " did not join its stack";
                    joined++;
                }
            }
            return null;
        }
    }
    // Up to want of each art from the bank box into the pack, for what a trip needs (Damian, 2026-10-17: "there is
    // extra reagents in your bank box"; the design's refill at any bank). The box opens to "bank" as for a deposit; a
    // stack is lifted only as far as the pack still wants. Null, or why not; taken, art by art, comes back.
    public string? BankTake(List<int> arts, int want, out string taken)
    {
        return BankTake(arts, a => want, out taken);
    }
    // The purse brought up to target gold from the bank box (BankTake: the box's top-level piles only), the bank not
    // asked when the pack holds target already: the one purse fill (AssistTesting.md T3). Null, or why the take failed;
    // a box short of gold is no failure, and taken says what came ("" when nothing was asked).
    public string? PurseTo(int target, out string taken)
    {
        taken = "";
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        if (PackCount(k, g => g == GoldArt) >= target)
            return null;
        return BankTake(new List<int> { GoldArt }, target, out taken);
    }
    public string? BankTake(List<int> arts, Func<int, int> wantOf, out string taken)
    {
        taken = "";
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        using (Act(k))
        {
            byte[]? open = SayBank(k);
            if (open == null)
                return NoBanker();
            uint pack;
            lock (k.State.Gate)
                pack = k.State.Backpack;
            if (pack == 0)
                return "the pack is not known: open the pack once";
            uint box = BankBox(open);
            Set<uint> tree = BankTree(k, box, true);
            List<string> parts = new();
            foreach (int art in arts)
            {
                int got = 0, want = wantOf(art);
                for (int round = 0; round < 10 && !circuitStop; round++)
                {
                    int need = want - PackCount(k, g => g == art);
                    if (need <= 0)
                        break;
                    AssistItem? stack = null;
                    // Gold only from the box's own top-level piles, never a bag's (Damian, 2026-10-24: "any gold pile
                    // in the top level of the bank is fine, just dont go digging in bags for gold ever. gold in
                    // subcontainers in the bank is savings, for a major project").
                    lock (k.State.Gate)
                        foreach (AssistItem it in k.State.Items.Values)
                            if (tree.Contains(it.Container) && it.Graphic == art
                                && (art != GoldArt || it.Container == box)
                                && (stack == null || it.Amount > stack.Amount))
                                stack = it;
                    if (stack == null)
                        break;
                    int amount = Math.Min(Math.Max(1, stack.Amount), need), before = PackCount(k, g => g == art);
                    Heard mark = new(k.State);
                    string? why = MoveStack(k, stack.Serial, amount, UoTiles.Name(art),
                        "Taking " + UoTiles.Name(art) + " from the bank",
                        () => k.Drop(stack.Serial, StackOnto(k, art, arts, stack.Serial))!);
                    if (why != null)
                        return why;
                    // Counted only when it reached the pack; a refused drop ("That container cannot hold more items.",
                    // the pack at 113 items, ten times over at 08:43:05) stops the take rather than trying it again.
                    int moved = PackCount(k, g => g == art) - before;
                    if (moved <= 0)
                    {
                        string? said = mark.Last();
                        parts.Add(UoTiles.Name(art) + " " + got + " (pack " + before + ")");
                        taken = string.Join(", ", parts);
                        return UoTiles.Name(art) + " did not reach the pack"
                            + (said != null ? ": \"" + said + "\"" : "");
                    }
                    got += moved;
                }
                parts.Add(UoTiles.Name(art) + " " + got + " (pack " + PackCount(k, g => g == art) + ")");
            }
            taken = string.Join(", ", parts);
            Note("Bank: took " + taken);
            return null;
        }
    }
    // Every stack of each art in the pack and its bags into one container (Damian, 2026-10-17: "put these all in the
    // subpack in the upper left corner of the main pack"): onto its stack of the art when it has one and the art
    // stacks, else into the container. Null, or why not; how many stacks moved comes back.
    public string? PutInto(List<int> arts, uint into, out int moved)
    {
        moved = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        List<UoTiles.Tile> tiles = UoTiles.Items();
        using (Act(k))
        {
            foreach (int art in arts)
            {
                bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
                for (int round = 0; round < 20 && !circuitStop; round++)
                {
                    AssistItem? stack = null, onto = null;
                    lock (k.State.Gate)
                    {
                        Set<uint> inside = PackTree(k);
                        foreach (AssistItem it in k.State.Items.Values)
                        {
                            if (it.Graphic != art)
                                continue;
                            if (it.Container == into)
                            {
                                if (stacks && (onto == null || it.Amount > onto.Amount))
                                    onto = it;
                            }
                            else if (inside.Contains(it.Container) && (stack == null || it.Amount > stack.Amount))
                            {
                                stack = it;
                            }
                        }
                    }
                    if (stack == null)
                        break;
                    string? why = MoveStack(k, stack.Serial, Math.Max(1, stack.Amount), UoTiles.Name(art),
                        "Moving " + UoTiles.Name(art), () => k.Drop(stack.Serial, onto != null ? onto.Serial : into)!);
                    if (why != null)
                        return why;
                    AssistItem? now = Item(k, stack.Serial);
                    if (now != null && now.Container != into && now.Container != (onto != null ? onto.Serial : into))
                        return UoTiles.Name(art) + " did not reach " + into.ToString("X");
                    moved++;
                }
            }
            return null;
        }
    }

    // Exactly these items into one container, by serial (Damian, 2026-10-17: "put those runes back in the bank": the
    // pack's Skara Brae rune is the same art and stays). Null, or why not.
    public string? MoveInto(List<uint> serials, uint into)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        using (Act(k))
        {
            foreach (uint s in serials)
            {
                AssistItem? it = Item(k, s);
                if (it == null)
                    return s.ToString("X") + " is not known";
                if (it.Container == into)
                    continue;
                string? why = MoveStack(k, s, Math.Max(1, it.Amount), s.ToString("X"),
                    "Moving " + UoTiles.Name(it.Graphic), () => k.Drop(s, into)!);
                if (why != null)
                    return why;
                AssistItem? now = Item(k, s);
                if (now == null || now.Container != into)
                    return s.ToString("X") + " did not reach " + into.ToString("X");
            }
            return null;
        }
    }

    // One item off the ground or out of any container into a container, the pack unset, the way a hand drags it
    // (Damian, 2026-10-25: a 20188 gold pile the client stopped drawing, "we need a 'get the hidden stuff' tool"):
    // amount of it, unset all; into the pack no more than the carry limit takes, so a heavy pile comes a batch at a
    // time. Null, or why not; moved is the amount that went.
    public string? GetItem(uint serial, int amount, uint into, out int moved)
    {
        moved = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        AssistItem? it;
        bool toPack;
        lock (k.State.Gate)
        {
            it = Item(k, serial);
            if (it == null)
                k.State.Ground.TryGet(serial, out it);
            if (into == 0)
                into = k.State.Backpack;
            toPack = into == k.State.Backpack || PackTree(k).Contains(into);
        }
        if (it == null)
            return serial.ToString("X") + " is not known";
        if (into == 0)
            return "no backpack known";
        int n = Math.Max(1, it.Amount);
        if (amount > 0)
            n = Math.Min(n, amount);
        int limit = CarryLimit(k);
        if (toPack && limit > 0)
        {
            int fits = FitsIn(it.Graphic, limit - WeightNow(k));
            if (fits <= 0)
                return "no room in the pack (" + WeightNow(k) + " of " + limit + " stones)";
            n = Math.Min(n, fits);
        }
        ManualStart();
        string? why;
        using (Act(k))
            why = MoveStack(k, serial, n, serial.ToString("X"), "Getting " + UoTiles.Name(it.Graphic),
                () => k.Drop(serial, into)!);
        if (why != null)
            return why;
        moved = n;
        return null;
    }

    // Each art in one container brought to exactly want (Damian, 2026-10-17: "we want to make it 50 of each", the
    // vendor's in-progress reagent bag): the excess lifted off the container's biggest stack onto the source's
    // stack, a shortfall lifted from the source's own stacks only, never from a bag inside it; the source is source,
    // else the pack. A move that changes nothing stops the fill (L-FUTILITY). Null, or why not; each art's count
    // after, and any shortfall, come back.
    public string? Fill(uint into, int want, List<int> arts, uint source, out string filled)
    {
        filled = "";
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        using (Act(k))
        {
            uint pack = source;
            if (pack == 0)
                lock (k.State.Gate)
                    pack = k.State.Backpack;
            if (pack == 0)
                return "the pack is not known: open the pack once";
            List<string> parts = new();
            Func<uint, int, int> count = (box, art) => { int n = 0; lock (k.State.Gate) foreach (AssistItem it in k.State.Items.Values) if (it.Container
                == box
                && it.Graphic == art) n += Math.Max(1, it.Amount); return n; };
            Func<uint, int, AssistItem> biggest = (box, art) => { AssistItem? b = null; lock (k.State.Gate) foreach (AssistItem it in k.State.Items.Values) if (it.Container
                == box
                && it.Graphic == art
                && (b == null || it.Amount > b.Amount)) b = it; return b!; };
            foreach (int art in arts)
            {
                for (int round = 0; round < 10 && !circuitStop; round++)
                {
                    int have = count(into, art);
                    if (have == want)
                        break;
                    AssistItem from, onto;
                    int amount;
                    if (have > want)
                    {
                        from = biggest(into, art);
                        onto = biggest(pack, art);
                        amount = have - want;
                    }
                    else
                    {
                        from = biggest(pack, art);
                        if (from == null)
                            break;
                        onto = biggest(into, art);
                        amount = Math.Min(Math.Max(1, from.Amount), want - have);
                    }
                    string? why = BusyLift(k, from.Serial, amount);
                    if (why != null)
                    {
                        parts.Add(UoTiles.Name(art) + " " + have);
                        filled = string.Join(", ", parts);
                        return "lifting " + UoTiles.Name(art) + ": " + why;
                    }
                    why = k.Drop(from.Serial, onto != null ? onto.Serial : have > want ? pack : into);
                    if (why != null)
                    {
                        filled = string.Join(", ", parts);
                        return Stopped("Filling " + UoTiles.Name(art), from.Serial, why);
                    }
                    Answered(k, AnswerMs);
                    Clock.Sleep(DropSettleMs);
                    if (count(into, art) == have)
                    {
                        parts.Add(UoTiles.Name(art) + " " + have);
                        filled = string.Join(", ", parts);
                        return UoTiles.Name(art) + " did not move; the item " + from.Serial.ToString("X") + " is "
                            + (Item(k, from.Serial) == null ? "held by the server"
                            : "in container " + Item(k, from.Serial)!.Container.ToString("X"));
                    }
                }
                int after = count(into, art);
                parts.Add(UoTiles.Name(art) + " " + after + (after < want ? " (short " + (want - after) + ")" : ""));
            }
            filled = string.Join(", ", parts);
            Note("Fill: " + filled);
            return null;
        }
    }

    // Every stack of each art in the pack and its bags onto the bank's biggest stack of it, else into the bank's bag
    // already holding any of arts, else the box (Damian, 2026-10-17: "merge in these reagents with the stuff there").
    // Null, or why not; put, art by art, and the bank's count of each after, come back.
    public string? BankPut(List<int> arts, out string put)
    {
        put = "";
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        using (Act(k))
        {
            byte[]? open = SayBank(k);
            if (open == null)
                return NoBanker();
            uint box = BankBox(open);
            Set<uint> tree = BankTree(k, box, true);
            Func<int, int> banked = art => { int n = 0; lock (k.State.Gate) foreach (AssistItem it in k.State.Items.Values) if (tree.Contains(it.Container)
                && it.Graphic == art) n += Math.Max(1, it.Amount); return n; };
            List<string> parts = new();
            foreach (int art in arts)
            {
                int moved = 0;
                for (int round = 0; round < 20 && !circuitStop; round++)
                {
                    AssistItem? stack = null, onto = null;
                    uint bag = 0;
                    lock (k.State.Gate)
                    {
                        Set<uint> inside = PackTree(k);
                        foreach (AssistItem it in k.State.Items.Values)
                        {
                            if (inside.Contains(it.Container) && it.Graphic == art
                                && (stack == null || it.Amount > stack.Amount))
                                stack = it;
                            // A stack in a bag before one loose in the box (Damian: "i keep my regs in bank in a
                            // subcontiner").
                            if (tree.Contains(it.Container) && it.Graphic == art
                                && (onto == null || (onto.Container == box && it.Container != box)
                                || ((onto.Container == box) == (it.Container == box) && it.Amount > onto.Amount)))
                                onto = it;
                            if (bag == 0 && tree.Contains(it.Container) && it.Container != box
                                && arts.Contains(it.Graphic))
                                bag = it.Container;
                        }
                    }
                    if (stack == null)
                        break;
                    int before = PackCount(k, g => g == art), amount = Math.Max(1, stack.Amount);
                    string? why = MoveStack(k, stack.Serial, amount, UoTiles.Name(art), "Banking " + UoTiles.Name(art),
                        () => k.Drop(stack.Serial, onto != null ? onto.Serial : bag != 0 ? bag : box)!);
                    if (why != null)
                        return why;
                    int left = PackCount(k, g => g == art);
                    if (left >= before)
                    {
                        parts.Add(UoTiles.Name(art) + " " + moved);
                        put = string.Join(", ", parts);
                        return UoTiles.Name(art) + " did not leave the pack";
                    }
                    moved += before - left;
                }
                parts.Add(UoTiles.Name(art) + " " + moved + " (bank " + banked(art) + ")");
            }
            put = string.Join(", ", parts);
            Note("Bank: put " + put);
            return null;
        }
    }

    // ---------- the tailoring circuit's record, for the window's Circuit tab ----------
    // Per thread vendor: its thread stock as last bought from, when it was bought out, and each restock's size, kept in
    // circuit-<server>.txt beside the settings (tab-separated: serial, name, x, y, stock, stock ticks, bought-out
    // ticks, restocks as stock@ticks;...), so the restock timers outlive a relay restart. A bought-out vendor restocks
    // once, at the server's next restock tick (about 17 minutes, tailoring-circuit.md Next 2), not on a clock of its
    // own: stock grew one step in 163 of 178 pairs of buy-outs at one vendor under 62 minutes apart, and the next
    // buy-out found it restocked 3 of 6 times under 15 minutes, 6 of 8 at 15 to 20, 18 of 20 at 20 to 30 (trades to
    // 2026-10-20; Damian, 2026-10-20: "6 can be adjusted with real data we have collected, not my best guess").
    public const int RestockMinutes = 17;
    public sealed class CircuitVendor
    {
        public uint Serial;
        public string Name = "";
        public int X, Y, Stock = -1;
        public DateTime StockAt, BoughtOutAt;
        public List<int[]> Restocks = new(); public List<long> RestockTicks = new();
    }
    Map<uint, CircuitVendor> m_circuitVendors = Map<uint, CircuitVendor>.Empty;
    readonly Lock m_circuitVendorsGate = new();
    bool m_circuitLoaded;
    public volatile string CircuitStatus = "No circuit running: steps are run one at a time from the control port.";
    public DateTime CircuitStart = DateTime.MinValue;
    // The circuit running, with its sub-option ("Tailoring: Sell only"), for the window's light.
    public volatile string CircuitName = "";
    string CircuitPath => ServerFile("circuit");
    void LoadCircuit()
    {
        if (m_circuitLoaded)
            return;
        m_circuitLoaded = true;
        try
        {
            if (!File.Exists(CircuitPath))
                return;
            foreach (string line in File.ReadAllLines(CircuitPath))
            {
                string[] t = line.Split('\t');
                if (t.Length < 8)
                    continue;
                CircuitVendor v = new()
                {
                    Serial = Convert.ToUInt32(t[0], 16),
                    Name = t[1],
                    X = int.Parse(t[2]),
                    Y = int.Parse(t[3]),
                    Stock = int.Parse(t[4]),
                    StockAt = new DateTime(long.Parse(t[5])),
                    BoughtOutAt = new DateTime(long.Parse(t[6]))
                };
                foreach (string r in t[7].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] p = r.Split('@');
                    if (p.Length == 2)
                    {
                        v.Restocks.Add(new[] { int.Parse(p[0]) });
                        v.RestockTicks.Add(long.Parse(p[1]));
                    }
                }
                m_circuitVendors = m_circuitVendors.Set(v.Serial, v);
            }
        }
        catch { }
    }
    void SaveCircuit()
    {
        List<string> lines = new();
        foreach (CircuitVendor v in m_circuitVendors.Values)
        {
            StringBuilder rs = new();
            for (int i = 0; i < v.Restocks.Count; i++)
                rs.Append(v.Restocks[i][0] + "@" + v.RestockTicks[i] + ";");
            lines.Add(v.Serial.ToString("X") + "\t" + v.Name.Replace('\t', ' ') + "\t" + v.X + "\t" + v.Y + "\t"
                + v.Stock + "\t" + v.StockAt.Ticks + "\t" + v.BoughtOutAt.Ticks + "\t" + rs);
        }
        try
        {
            File.WriteAllLines(CircuitPath, lines);
        }
        catch { }
        Interlocked.Increment(ref CircuitVendorsSeq);
    }
    public long CircuitVendorsSeq;
    // A thread buy at a vendor: the stock it showed (before buying) and what it has left. A vendor bought out before
    // that shows stock again has restocked, and that stock is kept as one of its restocks.
    void CircuitStock(uint vendor, string name, int shown, int left)
    {
        lock (m_circuitVendorsGate)
        {
            LoadCircuit();
            CircuitVendor? v;
            if (!m_circuitVendors.TryGet(vendor, out v))
                m_circuitVendors = m_circuitVendors.Set(vendor, v = new CircuitVendor { Serial = vendor });
            if (name.Length > 0)
                v.Name = name;
            if (Atlas != null)
                foreach (AtlasNpc n in Atlas.Visible())
                    if (n.Serial == vendor)
                    {
                        v.X = n.X;
                        v.Y = n.Y;
                        if (v.Name.Length == 0)
                            v.Name = n.Name;
                    }
            DateTime now = Clock.UtcNow.ToLocalTime();
            if (v.Stock == 0 && shown > 0)
            {
                v.Restocks.Add(new[] { shown });
                v.RestockTicks.Add(now.Ticks);
                if (v.Restocks.Count > 50)
                {
                    v.Restocks.RemoveAt(0);
                    v.RestockTicks.RemoveAt(0);
                }
            }
            v.BoughtOutAt = KeepRules.BoughtOut(v.BoughtOutAt, v.Stock, shown, left, now);
            v.Stock = left;
            v.StockAt = now;
            SaveCircuit();
        }
    }
    // A vendor whose read stock is no tailor's (TailorStock) is dropped from the record, and the record saved.
    public List<CircuitVendor> CircuitVendors()
    {
        lock (m_circuitVendorsGate)
        {
            LoadCircuit();
            if (Atlas != null)
            {
                List<uint> drop = new();
                foreach (AtlasNpc n in Atlas.Shops())
                    if (m_circuitVendors.ContainsKey(n.Serial) && !TailorStock(n))
                        drop.Add(n.Serial);
                if (drop.Count > 0)
                {
                    foreach (uint s in drop)
                        m_circuitVendors = m_circuitVendors.Remove(s);
                    SaveCircuit();
                }
            }
            return new List<CircuitVendor>(m_circuitVendors.Values);
        }
    }
    // What the pack holds for the circuit: thread, bolts, cloth, shirts, gold and the recall reagents.
    // The circuit job card's live lines (Damian, 2026-10-20: "what is the circuit's current task, next task, current
    // count of threads, bolts, cloth, and shirts in bag. i got gold status everywhere else").
    public volatile string CircuitNext = "";
    public string CircuitCounts()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        return "thread " + PackCount(k, IsThreadArt) + ", bolts " + PackCount(k, IsBolt) + ", cloth "
            + PackCount(k, IsCloth) + ", shirts " + PackCount(k, IsShirtArt);
    }
    public string CircuitPack()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        int gold = PackCount(k, g => g == GoldArt);
        lock (k.State.Gate)
            gold = Math.Max(gold, k.State.Gold);
        List<string> r = new();
        foreach (int g in s_recallReagents)
            r.Add(UoTiles.Name(g) + " " + PackCount(k, x => x == g));
        return "Gold " + gold + ", thread " + PackCount(k, g => g == 0x0FA0 || g == 0x0FA1) + ", bolts "
            + PackCount(k, IsBolt) + ", cloth piles " + PackCloth(k).Count
            + ", for sale " + PackCount(k, IsShirtArt) + "; " + string.Join(", ", r);
    }
}
