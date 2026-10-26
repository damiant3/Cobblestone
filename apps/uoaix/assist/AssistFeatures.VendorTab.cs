#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the Vendor tab (assist-plan.md stage 8) ----------
    // The 1998 player vendor (rundir scripts.wombat/vended.m), as uo1998 runs it (relay log 2026-10-20): the owner's
    // double-click on the vendor opens its pack (0x24, 18:43:16); an item the owner drops into it asks "Type in a price
    // for <item>." by a text entry (0x9A: serial, gump 0x1A, parent 0, the text); the client's answer is 0x9A with the
    // serial and gump echoed, type 1, then "<price> <description>" and its NUL (18:46:32, "1000" for 26 ingots); the
    // item then says "Cost:<price>" (type 0). A price over 1,000,000 is capped; a prompt nobody answers leaves the item
    // unpriced, and its first single click prices it at the item's own value (a shovel, "Cost:14", 18:43:30). A single
    // click on a vended item says "<description>.  Cost:<price>" (type 0; "Cost:FREE!", "Cost:Not for sale.") before
    // its name label (type 6).
    public const int VendedPrompt = 0x1A, VendedPriceMax = 1000000, VendedTextMax = 100;
    // NotForSale: the answer is words with no price before them, which vended.m reads as not for sale for a container,
    // a book or a keyring ("Cost:Not for sale."). X, Y: the spot in the vendor's pack, -1 for the next free one.
    public sealed class VendorEntry
    {
        public uint Serial;
        public int Amount, Price, X = -1, Y = -1; public bool NotForSale; public string What = null!, Description = "";
    }
    public volatile string VendorStatus = "";
    public volatile bool VendorBusy; volatile bool m_vendorStop; public long VendorSeq;
    readonly List<string[]> m_vendorStock = new();
    // A Cost line answering the assist's own click on a vendor's item stays off the client's screen, as HoldLabel's do.
    ValueMap<uint, DateTime> m_costLooking = ValueMap<uint, DateTime>.Empty; readonly Lock m_costLookingGate = new();
    bool HoldCostLine(byte[] p)
    {
        if (p[0] != 0x1C || p.Length <= 44)
            return false;
        uint s = U32(p, 3);
        DateTime until;
        lock (m_costLookingGate)
        {
            if (!m_costLooking.TryGet(s, out until))
                return false;
            if (Clock.UtcNow <= until)
                return true;
            m_costLooking = m_costLooking.Remove(s);
            return false;
        }
    }
    public void VendorStop()
    {
        m_vendorStop = true;
    }
    // The stock as last read: item, description, price, serial.
    public List<string[]> VendorStock()
    {
        lock (m_vendorStock)
            return new List<string[]>(m_vendorStock);
    }
    // The house vendor's pack opened by its double-click; its serial, or 0 with why.
    uint VendorOpen(GameLink k, out string? why, uint vendor = 0)
    {
        if (vendor == 0)
            vendor = HouseVendorSerial;
        why = null;
        if (vendor == 0)
        {
            why = "no house vendor is set (Houses tab, House vendor)";
            return 0;
        }
        uint pack = 0;
        ManualResetEventSlim got = new(false);
        Action<byte[]> h = p => { if (p[0] == 0x24 && p.Length >= 7 && pack == 0) { pack = U32(p, 1); got.Set(); } };
        k.ServerPacket += h;
        try
        {
            using (Act(k))
            {
                BusyWait(clickedAt);
                why = k.Use(vendor, false);
                if (why == null)
                    clickedAt = Clock.UtcNow;
            }
            if (why != null)
                return 0;
            if (!Clock.Wait(got, AnswerMs))
            {
                why = "the vendor " + vendor.ToString("X") + " opened no pack in " + AnswerMs + " ms (in reach?)";
                return 0;
            }
        }
        finally { k.ServerPacket -= h; }
        Answered(k, AnswerMs);
        Clock.Sleep(PricerOpenMs);
        return pack;
    }
    static string VendedText(VendorEntry e)
    {
        string d = (e.Description ?? "").Trim();
        if (d.Length > VendedTextMax)
            d = d.Substring(0, VendedTextMax);
        if (e.NotForSale)
        {
            d = d.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-', ' ');
            if (d.Length == 0)
                d = "Keys";
        }
        StringBuilder sb = new(e.NotForSale ? "" : Math.Min(e.Price, VendedPriceMax).ToString());
        if (d.Length > 0)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            foreach (char c in d)
                sb.Append(c >= 32 && c < 127 ? c : ' ');
        }
        return sb.ToString();
    }
    static string VendedCost(VendorEntry e)
    {
        return e.NotForSale ? "Not for sale." : Math.Min(e.Price, VendedPriceMax).ToString();
    }
    // Every entry handed to the vendor in turn (Damian, 2026-10-19: "bag loadouts with pricing"), at the busy pace; the
    // first that gets no prompt, no Cost line or a cost other than asked ends the handover with why (L-FUTILITY).
    public string? VendorGive(List<VendorEntry> entries, string aside = "")
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (VendorBusy)
            return "the vendor tab is already working";
        if (entries.Count == 0)
            return "nothing priced to hand over";
        uint backpack;
        lock (k.State.Gate)
            backpack = k.State.Backpack;
        foreach (VendorEntry e in entries)
            if (e.Serial == backpack)
                return "the backpack itself cannot be handed over";
        VendorBusy = true;
        m_vendorStop = false;
        VendorStatus = "Opening the vendor's pack...";
        Spawn("the vendor handover",
            () => { try { VendorGiveRun(k, entries); } finally { if (aside.Length
            > 0) VendorStatus += " " + aside; VendorBusy = false; } });
        return null;
    }
    void VendorGiveRun(GameLink k, List<VendorEntry> entries)
    {
        string? why;
        uint pack = VendorOpen(k, out why);
        if (pack == 0)
        {
            VendorStatus = "Handover: " + why;
            Note(VendorStatus);
            return;
        }
        int done = 0, gold = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (m_vendorStop || k.Closed)
            {
                VendorStatus = "Handover stopped after " + done + " of " + entries.Count + ".";
                Note(VendorStatus);
                return;
            }
            VendorEntry e = entries[i];
            VendorStatus = "Handing over " + (i + 1) + " of " + entries.Count + ": " + e.What + " at " + e.Price
                + "...";
            why = VendorGiveOne(k, pack, e, i);
            if (why != null)
            {
                VendorStatus = "Handover stopped at " + (i + 1) + " of " + entries.Count + " (" + e.What + "): " + why;
                Note(VendorStatus);
                return;
            }
            done++;
            gold += Math.Min(e.Price, VendedPriceMax);
        }
        VendorStatus = "Handed over " + done + " for " + gold + " gp in all.";
        Note("Vendor: " + VendorStatus);
    }
    // The prompt is the one that follows the drop, whatever its serial: a part lifted off a stack can come back under a
    // new serial, and the Cost line is matched to the prompt's.
    string? VendorGiveOne(GameLink k, uint pack, VendorEntry e, int slot)
    {
        byte[]? prompt = null;
        string? cost = null;
        uint priced = 0;
        bool dropped = false;
        ManualResetEventSlim asked = new(false);
        ManualResetEventSlim said = new(false);
        Action<byte[]> h = p =>
        {
            if (p[0] == 0x9A && p.Length >= 15 && dropped && prompt == null && U32(p, 7) == VendedPrompt)
            {
                prompt = p;
                priced = U32(p, 3);
                asked.Set();
            }
            else if (p[0] == 0x1C && p.Length > 44 && p[9] == 0 && priced != 0 && U32(p, 3) == priced)
            {
                string t = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                int c = t.IndexOf("Cost:");
                if (c >= 0)
                {
                    cost = t.Substring(c + 5).Trim();
                    said.Set();
                }
            }
        };
        int w0 = s_layoutBox[2] - s_layoutBox[0], x = s_layoutBox[0] + (slot * 13) % w0, y = s_layoutBox[1]
            + (slot * 13 / w0 * 12) % (s_layoutBox[3] - s_layoutBox[1]);
        if (e.X >= 0)
        {
            x = e.X;
            y = e.Y;
        }
        k.ServerPacket += h;
        try
        {
            string? why;
            using (Act(k))
                why = MoveStack(k, e.Serial, Math.Max(1, e.Amount), e.What, "the vendor handover",
                    () => { m_promptClaimUntil = Clock.UtcNow.AddMilliseconds(2
                    * AnswerMs); dropped = true; return k.DropAt(e.Serial, pack, x, y)!; });
            if (why != null)
                return why;
            if (!Clock.Wait(asked, AnswerMs))
                return "the vendor asked no price in " + AnswerMs + " ms";
            m_promptClaimUntil = DateTime.MinValue;
            string text = VendedText(e);
            byte[] t = Encoding.ASCII.GetBytes(text);
            byte[] r = new byte[16 + t.Length];
            r[0] = 0x9A;
            r[1] = (byte)(r.Length >> 8);
            r[2] = (byte)r.Length;
            Array.Copy(prompt!, 3, r, 3, 8);
            r[14] = 1;
            t.CopyTo(r, 15);
            using (Act(k))
                why = k.Inject(r, "price " + priced.ToString("X") + " " + text, false);
            if (why != null)
                return why;
            // The answer is ignored where the vendor's pack is not in view (vended.m, textentry: isFreelyViewable), as
            // from the guarded key spot outside the house (2026-10-21 12:20:21, no line at all); the item keeps its
            // default, and one single click reads it (lookedat: a key ring with no price says "Cost:Not for sale.").
            if (!Clock.Wait(said, AnswerMs))
            {
                using (Act(k))
                {
                    BusyWait(clickedAt);
                    why = k.Look(priced);
                    clickedAt = Clock.UtcNow;
                }
                if (why != null || !Clock.Wait(said, AnswerMs))
                    return "no Cost line came back in " + AnswerMs + " ms after \"" + text + "\", nor to a click on it";
            }
            if (cost != VendedCost(e))
                return "the vendor says Cost:" + cost + " after \"" + text + "\"";
            return null;
        }
        finally { k.ServerPacket -= h; m_promptClaimUntil = DateTime.MinValue; }
    }
    // The vendor's stock read by one single click on each item in its pack (assist-plan.md stage 8: "the vendor's stock
    // read by single clicks"); an item that says nothing reads "no answer" and is not clicked again.
    public string? VendorReadStock()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (VendorBusy)
            return "the vendor tab is already working";
        VendorBusy = true;
        m_vendorStop = false;
        VendorStatus = "Opening the vendor's pack...";
        Spawn("the vendor stock", () => { try { VendorStockRun(k); } finally { VendorBusy = false; } });
        return null;
    }
    void VendorStockRun(GameLink k)
    {
        string? why;
        uint pack = VendorOpen(k, out why);
        if (pack == 0)
        {
            VendorStatus = "Stock: " + why;
            Note(VendorStatus);
            return;
        }
        List<AssistItem> items = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == pack)
                    items.Add(it);
        items.Sort((a, b) => a.Serial.CompareTo(b.Serial));
        Map<uint, string> names;
        Map<uint, string> lines = VendorCosts(k, items, out names, out why);
        if (why != null)
            VendorStatus = "Stock stopped: " + why;
        List<string[]> rows = new();
        int total = 0, priced = 0;
        foreach (AssistItem it in items)
        {
            string? line, name;
            lines.TryGet(it.Serial, out line);
            names.TryGet(it.Serial, out name);
            string desc = "", price = "no answer";
            if (line != null)
            {
                int c = line.IndexOf("Cost:");
                price = line.Substring(c + 5).Trim();
                desc = line.Substring(0, c).Trim().TrimEnd('.');
            }
            int v;
            if (int.TryParse(price, out v))
            {
                total += v;
                priced++;
            }
            rows.Add(new[] { name ?? UoTiles.Name(it.Graphic), desc, price, it.Serial.ToString("X") });
        }
        lock (m_vendorStock)
        {
            m_vendorStock.Clear();
            m_vendorStock.AddRange(rows);
        }
        Interlocked.Increment(ref VendorSeq);
        if (!VendorStatus.StartsWith("Stock stopped"))
            VendorStatus = "Stock: " + items.Count + " items, " + priced + " priced, " + total + " gp in all.";
        Note("Vendor: " + VendorStatus);
    }
    // One single click on each item, its Cost line by serial and its name label into names, the lines kept off the
    // client. why: the click that could not go out, the clicks stopping there.
    Map<uint, string> VendorCosts(GameLink k, List<AssistItem> items, out Map<uint, string> names, out string? why)
    {
        why = null;
        Map<uint, string> lines = Map<uint, string>.Empty, labels = Map<uint, string>.Empty;
        Lock gate = new();
        Action<byte[]> h = p =>
        {
            if (p[0] != 0x1C || p.Length <= 44)
                return;
            uint s = U32(p, 3);
            string t = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
            lock (gate)
            {
                if (p[9] == 6)
                    labels = labels.Set(s, t);
                else if (t.Contains("Cost:"))
                    lines = lines.Set(s, t);
            }
        };
        DateTime hideUntil = Clock.UtcNow.AddMilliseconds(LabelWaitMs + LabelGapMs * (items.Count + 1));
        lock (m_costLookingGate)
            foreach (AssistItem it in items)
                m_costLooking = m_costLooking.Set(it.Serial, hideUntil);
        k.ServerPacket += h;
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (m_vendorStop || circuitStop || k.Closed)
                    break;
                VendorStatus = "Reading " + (i + 1) + " of " + items.Count + "...";
                string? w;
                using (Act(k))
                    w = k.Look(items[i].Serial);
                if (w != null)
                {
                    why = "at " + (i + 1) + " of " + items.Count + ": " + w;
                    break;
                }
                Clock.Sleep(LabelGapMs);
            }
            Wait.Until(Clock, () => lines.Count >= items.Count, LabelWaitMs, "the labels", stepMs: 50);
        }
        finally { k.ServerPacket -= h; }
        lock (gate)
        {
            names = labels;
            return lines;
        }
    }
    // ---------- the player vendor catalogue ----------
    // Damian, 2026-10-23: player house vendors catalogued from the stream when he opens their packs, every sub-pack
    // opened, the vendor given a description of its wares ("armor, weapons, magicals, scrolls, potions, ingots, a
    // hodgepodge"; "some vendors are just used for private utility like we do with house door keys"), nothing inside a
    // player house catalogued. The player's own double-click (0x06, seen by HoldClient, which runs before the packet
    // goes on, where ClientPacket runs after and can follow the answer; neither carries the assist's) on a mobile the
    // atlas types Player vendor or that says VendedSays to it, answered by a pack opened (0x24) within AnswerMs, starts
    // one read: every bag in the
    // pack opened once, CatalogBagsMax at most (one that does not open is not asked again, L-FUTILITY); every item
    // clicked once for its "<description>.  Cost:<price>" line and its name (VendorCosts, kept off the client); the
    // items for sale written to the vendor's atlas row as a buy list's rows ("name: price gp x amount", the Shops
    // tab's) and its wares described (VendorWares). Only the vendor's pack is read, never what lies in the house;
    // nothing is read while a circuit or the Vendor tab works.
    public const int CatalogBagsMax = PricerBagsMax;
    // Every vendor read, with every item of its pack (the Vendors tab).
    public VendorBook Vendors = null!;
    // uo1998's player vendors carry no title ("Leighton (invulnerable)", 2026-10-23 17:57:18): a vendor is known by the
    // line it says to the double-click before it opens its backpack (rundir vendor.m: barkTo(this, user, VendedSays),
    // then openContainer), and the atlas types it Player vendor from then on.
    public const string VendedSays = "Take a look at my goods.";
    volatile uint m_catalogAsked; volatile bool m_catalogSaid; DateTime m_catalogAskedAt;
    // Never holds the packet: false always.
    bool VendorCatalogClient(byte[] p)
    {
        if (p[0] != 0x06 || p.Length < 5 || Atlas == null)
            return false;
        m_catalogAsked = U32(p, 1) & 0x7FFFFFFF;
        m_catalogSaid = false;
        m_catalogAskedAt = Clock.UtcNow;
        return false;
    }
    void VendorCatalogServer(GameLink k, byte[] p)
    {
        if (p[0] == 0x1C && p.Length > 44 && m_catalogAsked != 0 && U32(p, 3) == m_catalogAsked)
        {
            if (Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Contains(VendedSays))
            {
                m_catalogSaid = true;
                Atlas.PlayerVendor(m_catalogAsked);
            }
            return;
        }
        if (p[0] != 0x24 || p.Length < 7 || m_catalogAsked == 0)
            return;
        uint vendor = m_catalogAsked;
        bool said = m_catalogSaid;
        m_catalogAsked = 0;
        m_catalogSaid = false;
        if ((Clock.UtcNow - m_catalogAskedAt).TotalMilliseconds > AnswerMs)
            return;
        if (!said && Atlas.TypeOf(vendor) != "Player vendor")
            return;
        // Our own house vendor is not read while a circuit or a loop works (Damian, 2026-10-24: "disable vendor
        // inventory checking on our own vendor at the door of our house when running a circuit"): its double-click is
        // for the key.
        bool own = vendor == HouseVendorSerial || Houses().Exists(hr => hr.Vendor == vendor);
        if (CircuitRunning || VendorBusy || own && m_loops.Running)
        {
            Note("Vendor: the catalogue of " + vendor.ToString("X") + " not read: "
                + (CircuitRunning ? "a circuit runs"
                : VendorBusy ? "the vendor tab is working" : "our own house vendor, a loop runs"));
            return;
        }
        uint pack = U32(p, 1);
        VendorBusy = true;
        m_vendorStop = false;
        Spawn("the vendor catalogue",
            () => { try { VendorCatalog(k, vendor, pack); } finally { VendorBusy = false; } });
    }
    // One bag opened by its double-click: true when the server opened it (0x24 for it) within AnswerMs.
    bool CatalogOpen(GameLink k, uint bag, out string? why)
    {
        ManualResetEventSlim got = new(false);
        Action<byte[]> h = p => { if (p[0] == 0x24 && p.Length >= 7 && U32(p, 1) == bag) got.Set(); };
        k.ServerPacket += h;
        try
        {
            using (Act(k))
            {
                BusyWait(clickedAt);
                why = k.Use(bag, false);
                if (why == null)
                    clickedAt = Clock.UtcNow;
            }
            if (why != null)
                return false;
            if (!Clock.Wait(got, AnswerMs))
            {
                why = "no answer in " + AnswerMs + " ms";
                return false;
            }
        }
        finally { k.ServerPacket -= h; }
        Clock.Sleep(PricerOpenMs);
        return true;
    }
    public const int CatalogBagReach = 2;
    // The vendor's distance from the player in tiles (Chebyshev), as the server measures a reach: where it is drawn,
    // else where the atlas last saw it; 0 when neither knows it, so its bags are asked as before.
    int VendorDistance(GameLink k, uint vendor)
    {
        int px, py, vx, vy;
        bool known;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            int[]? at;
            known = k.State.Mobiles.TryGet(vendor, out at);
            vx = known ? at![0] : 0;
            vy = known ? at![1] : 0;
        }
        if (!known && Atlas != null)
        {
            AtlasNpc? row = Atlas.Shops().Find(n => n.Serial == vendor);
            if (row != null)
            {
                known = true;
                vx = row.X;
                vy = row.Y;
            }
        }
        return known ? Tiles(vx, vy, px, py) : 0;
    }
    void VendorCatalog(GameLink k, uint vendor, uint pack)
    {
        AtlasNpc? row = Atlas.Shops().Find(n => n.Serial == vendor);
        string who = row != null && row.Name.Length > 0 ? row.Name : vendor.ToString("X");
        Clock.Sleep(PricerOpenMs);
        Set<uint> opened = Set<uint>.Of(pack);
        Queue<uint> queue = new();
        int shut = 0;
        Action<uint> bagsIn = b => { lock (k.State.Gate) foreach (AssistItem it in k.State.Items.Values) if (it.Container
            == b
            && IsContainer(it.Graphic)
            && !opened.Contains(it.Serial)) queue.Enqueue(it.Serial); };
        // A bag inside opens only within CatalogBagReach of the vendor, one less than its pack opens from (OUO
        // DispatchDoubleClick: an item past 2 tiles is "I can't reach that."; Damian, 2026-10-24: "i can open a vendor
        // primary pack at 1 farther range than the subconainers in it"): farther off, the pack's top level only, no bag
        // asked.
        int vd = VendorDistance(k, vendor);
        if (vd > CatalogBagReach)
            Note("Vendor: " + who + " stands " + vd + " tiles off; the bags in the pack open within " + CatalogBagReach
                + ", so only its top level is read");
        else
            bagsIn(pack);
        while (queue.Count > 0 && opened.Count < CatalogBagsMax && !m_vendorStop && !k.Closed)
        {
            uint b = queue.Dequeue();
            opened = opened.Add(b);
            string? w;
            if (!CatalogOpen(k, b, out w))
            {
                shut++;
                Note("Vendor: " + who + "'s bag " + b.ToString("X") + " did not open (" + w + "); not asked again");
                continue;
            }
            bagsIn(b);
        }
        List<AssistItem> items = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (opened.Contains(it.Container))
                    items.Add(it);
        items.Sort((a, b) => a.Serial.CompareTo(b.Serial));
        string? why = null;
        Map<uint, string> names = Map<uint, string>.Empty;
        Map<uint, string> lines = items.Count == 0 ? Map<uint, string>.Empty
            : VendorCosts(k, items, out names, out why);
        if (m_vendorStop || k.Closed || why != null)
        {
            VendorStatus = "The catalogue of " + who + " stopped" + (why != null ? ": " + why : "")
                + "; nothing written.";
            Note("Vendor: " + VendorStatus);
            return;
        }
        List<string> stock = new();
        List<VendorWares.Ware> wares = new();
        int unread = 0;
        foreach (AssistItem it in items)
        {
            string? line, label;
            lines.TryGet(it.Serial, out line);
            names.TryGet(it.Serial, out label);
            if (line == null)
            {
                unread++;
                continue;
            }
            if (string.IsNullOrEmpty(label))
                label = UoTiles.Name(it.Graphic);
            int c = line.IndexOf("Cost:");
            string price = line.Substring(c
                + 5).Trim(), desc = line.Substring(0, c).Trim().TrimEnd('.').Trim().Replace(": ", " ");
            int gp;
            bool sale = int.TryParse(price, out gp) || price.StartsWith("FREE");
            wares.Add(new VendorWares.Ware(label, sale));
            if (sale)
                stock.Add(label.Replace(": ", " ")
                    + (desc.Length > 0 && !desc.Equals(label, StringComparison.OrdinalIgnoreCase) ? " (" + desc + ")"
                    : "") + ": " + gp + " gp x " + Math.Max(1, it.Amount));
        }
        string kind = VendorWares.Describe(wares);
        VendorStatus = who + " catalogued: " + kind + ", " + stock.Count + " for sale of " + items.Count + " items in "
            + (opened.Count - shut) + " bags" + (shut > 0 ? ", " + shut + " bags shut" : "")
            + (unread > 0 ? ", " + unread + " unread" : "") + ".";
        List<VendorItem> read = new();
        foreach (AssistItem it in items)
        {
            string? line, label;
            lines.TryGet(it.Serial, out line);
            names.TryGet(it.Serial, out label);
            string desc = "", price = "no answer";
            if (line != null)
            {
                int c = line.IndexOf("Cost:");
                price = line.Substring(c + 5).Trim().TrimEnd('.').Trim();
                desc = line.Substring(0, c).Trim().TrimEnd('.').Trim();
            }
            read.Add(new VendorItem(it.Serial, it.Graphic, Math.Max(1, it.Amount), it.Container,
                string.IsNullOrEmpty(label) ? UoTiles.Name(it.Graphic) : label, desc, price, ""));
        }
        VendorRecord record = Vendors.Put(new VendorRecord(vendor, who, row != null ? row.X : 0,
            row != null ? row.Y : 0, kind, Vendors.Now, ImmutableArray.CreateRange(read)));
        Atlas.Catalogued(vendor, stock, kind);
        Note("Vendor: " + VendorStatus.TrimEnd('.'));
        string? idw = CatalogIdentify(k, record);
        if (idw != null)
        {
            VendorStatus = VendorStatus.TrimEnd('.') + "; " + idw + ".";
            Note("Vendor: " + who + ": " + idw);
        }
    }
    // ---------- identifying a vendor's magic items ----------
    // Damian, 2026-10-24: "if there is an item ID magical item in my pack, use it on the magical items therein and
    // update our information". uo1998's identify item (rundir classifier/effects.cls "useon identify", suffix "of
    // identification", identifyeffect.m): its use opens a target cursor; a target costs a charge and answers "It is:
    // <name>" for a magic item, its label for one with no magic name (identify.m Q4JM), "This magic item is out of
    // charges." at none left, and "You can't see that object well enough to identify it." for an item not in view. A
    // wand or a gnarled staff must be in hand first ("You must equip this item to use it.", spelskil.m Q49W, arts
    // Q50O).
    static readonly ImmutableArray<int> s_identifyArts = [ 0x0DF2, 0x0DF3, 0x0DF4, 0x0DF5, 0x13F8, 0x13F9 ];
    public const string IdentifyWord = "identification", IdentifiedSays = "It is: ";
    // Labels read from the pack's wands, once a session each (L-FUTILITY).
    Map<uint, string> m_identifyLabels = Map<uint, string>.Empty; readonly Lock m_identifyLabelsGate = new();
    // An identify item in the pack or a hand, its label read once by a single click; null when none.
    AssistItem? IdentifyTool(GameLink k)
    {
        Set<uint> inside = PackTree(k);
        uint me;
        List<AssistItem> found = new();
        lock (k.State.Gate)
        {
            me = k.State.Player;
            foreach (AssistItem it in k.State.Items.Values)
                if (s_identifyArts.IndexOf(it.Graphic) >= 0
                    && (inside.Contains(it.Container) || it.Mobile == me && (it.Layer == 1 || it.Layer == 2)))
                    found.Add(it);
        }
        foreach (AssistItem it in found)
        {
            string? label;
            m_identifyLabels.TryGet(it.Serial, out label);
            if (label == null)
            {
                label = ReadLabel(k, it.Serial) ?? "";
                lock (m_identifyLabelsGate)
                    m_identifyLabels = m_identifyLabels.Set(it.Serial, label);
            }
            if (label.IndexOf(IdentifyWord, StringComparison.OrdinalIgnoreCase) >= 0)
                return it;
        }
        return null;
    }
    // An item's name label (a 0x1C of type 6 for it) from one single click; null when none came in LabelWaitMs.
    string? ReadLabel(GameLink k, uint serial)
    {
        string? label = null;
        ManualResetEventSlim got = new(false);
        Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) == serial
            && p[9] == 6) { label = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim(); got.Set(); } };
        k.ServerPacket += h;
        try
        {
            string? w;
            using (Act(k))
            {
                BusyWait(clickedAt);
                w = k.Look(serial);
                clickedAt = Clock.UtcNow;
            }
            if (w == null)
                Clock.Wait(got, LabelWaitMs);
        }
        finally { k.ServerPacket -= h; }
        return label;
    }
    // Every magic item of the record not yet identified, once each, with the pack's identify item; null when there was
    // nothing to do, else what was done and why it stopped. The first refusal stops the run (L-FUTILITY); what was in
    // hand goes back after.
    string? CatalogIdentify(GameLink k, VendorRecord record)
    {
        List<VendorItem> todo = new();
        foreach (VendorItem it in record.Items)
            if (it.Magic && it.Identified.Length == 0)
                todo.Add(it);
        if (todo.Count == 0)
            return null;
        AssistItem? tool = IdentifyTool(k);
        if (tool == null)
            return todo.Count + " magic items not identified: no identify item in the pack";
        uint me, pack;
        lock (k.State.Gate)
        {
            me = k.State.Player;
            pack = k.State.Backpack;
        }
        List<uint[]> held = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if ((it.Layer == 1 || it.Layer == 2) && it.Mobile == me && it.Serial != tool.Serial)
                    held.Add(new[] { it.Serial, (uint)it.Layer });
        bool wasHeld = tool.Layer >= 0;
        int done = 0;
        string? stop = null;
        if (!wasHeld)
            stop = IdentifyEquip(k, tool, me, pack);
        for (int i = 0; stop == null && i < todo.Count && !m_vendorStop && !k.Closed; i++)
        {
            VendorStatus = "Identifying " + (i + 1) + " of " + todo.Count + " magic items...";
            string? name;
            stop = IdentifyOne(k, tool.Serial, todo[i], out name);
            if (name != null)
            {
                Vendors.Identified(record.Serial, todo[i].Serial, name);
                done++;
            }
        }
        if (!wasHeld)
        {
            string? w = IdentifyRestore(k, tool.Serial, held, me, pack);
            if (w != null && stop == null)
                stop = w;
        }
        return done + " of " + todo.Count + " magic items identified" + (stop != null ? "; stopped: " + stop : "");
    }
    // The tool into the hand its tile names, the hands' items put in the pack first. Null, or why not.
    string? IdentifyEquip(GameLink k, AssistItem tool, uint me, uint pack)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        int layer = tool.Graphic < items.Count && items[tool.Graphic].Layer > 0 ? items[tool.Graphic].Layer : 1;
        string? w = FreeHands(k);
        if (w != null)
            return "putting the held items away: " + w;
        using (Act(k))
        {
            w = BusyLift(k, tool.Serial, 1);
            if (w != null)
                return "lifting the identify item: " + w;
            w = k.Equip(tool.Serial, layer, me);
            if (w != null)
                return "equipping the identify item: " + w;
            Answered(k, AnswerMs);
        }
        return Wait.Until(Clock, () => { AssistItem? a = Item(k, tool.Serial); return a != null && a.Layer >= 0; },
            AnswerMs, "the identify item in hand", stepMs: 50).Done ? null
            : "the identify item did not go into your hand";
    }
    // The tool back in the pack and what was in hand equipped again. Null, or why not.
    string? IdentifyRestore(GameLink k, uint tool, List<uint[]> held, uint me, uint pack)
    {
        using (Act(k))
        {
            string? w = MoveStack(k, tool, 1, "the identify item", "Putting the identify item away",
                () => k.Drop(tool, pack)!);
            if (w != null)
                return w;
        }
        foreach (uint[] h in held)
        {
            using (Act(k))
            {
                string? w = BusyLift(k, h[0], 1);
                if (w != null)
                    return "lifting " + h[0].ToString("X") + " back into your hand: " + w;
                w = k.Equip(h[0], (int)h[1], me);
                if (w != null)
                    return w;
                Answered(k, AnswerMs);
            }
        }
        return null;
    }
    // One item identified: the tool used, its cursor aimed at the item, the answer read. name: what it is, null when
    // the answer named nothing. Null to go on, or why the run stops.
    string? IdentifyOne(GameLink k, uint tool, VendorItem item, out string? name)
    {
        name = null;
        string? label = null;
        ManualResetEventSlim got = new(false);
        Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) == item.Serial
            && p[9] == 6) { label = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim(); got.Set(); } };
        Heard mark;
        AssistItem? at = Item(k, item.Serial);
        if (at == null)
            return "the item " + item.Serial.ToString("X") + " is no longer in view";
        k.ServerPacket += h;
        try
        {
            using (Act(k))
            {
                BusyWait(clickedAt);
                mark = new(k.State);
                string? w = k.Use(tool, true);
                clickedAt = Clock.UtcNow;
                if (w != null)
                    return "the identify item's use: " + w;
                AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CursorMs));
                if (!k.MacroCursor)
                {
                    k.Settle();
                    string? said = mark.Last();
                    return "the identify item opened no target cursor" + (said != null ? " (\"" + said + "\")" : "");
                }
                w = k.Target(false, item.Serial, at.X, at.Y, 0, at.Graphic);
                if (w != null)
                    return "the identify's target: " + w;
                k.Settle();
            }
            Wait.Until(Clock,
                () => mark.Find(IdentifiedSays) != null || mark.Find("out of charges") != null
                || mark.Find("well enough") != null || got.IsSet, AnswerMs, "the identify's answer", stepMs: 50);
            string? it = mark.Find(IdentifiedSays);
            if (it != null)
            {
                name = it.Substring(it.IndexOf(IdentifiedSays, StringComparison.Ordinal)
                    + IdentifiedSays.Length).Trim();
                return mark.Find("out of charges") != null ? "the identify item is out of charges" : null;
            }
            if (mark.Find("out of charges") != null)
                return "the identify item is out of charges";
            if (mark.Find("well enough") != null)
                return "the server could not see " + item.Name + " well enough";
            if (got.IsSet)
            {
                name = "no magic name (" + label + ")";
                return null;
            }
            string? last = mark.Last();
            return "no answer to the identify of " + item.Name + (last != null ? " (\"" + last + "\")" : "");
        }
        finally { k.ServerPacket -= h; }
    }
    // The house vendor in view, walked to within a tile and its pack opened; the pack, or 0 with why.
    uint HouseVendorPack(GameLink k, out string? why)
    {
        uint vendor = HouseVendorSerial;
        why = null;
        if (vendor == 0)
        {
            why = "no house vendor is set (Houses tab, House vendor)";
            return 0;
        }
        int[]? at;
        if (!k.State.Mobiles.TryGet(vendor, out at))
            at = null;
        else
            at = new[] { at[0], at[1] };
        if (at == null)
        {
            why = "the house vendor " + vendor.ToString("X") + " is not in view";
            return 0;
        }
        CircuitStatus = "Mine: walking to the vendor";
        why = WalkTo(at[0], at[1], 1);
        if (why != null)
        {
            why = "walking to the vendor: " + why;
            return 0;
        }
        return VendorOpen(k, out why);
    }
    // Mining storage "vendor" (Damian, 2026-10-20: "another dump off for the ingots can be the player vendor where the
    // ingots are put up for sale at a certain price per ingot. make piles up to 1000"): the pack's ingots handed to the
    // house vendor in piles of at most VendorPileMax, each priced its count times the price per ingot. A pile already
    // for sale is not added to: a vendor's price is the item's, not each ingot's. put: how many.
    public const int VendorPileMax = 1000;
    public int MineVendorPrice
    {
        get => Math.Max(0, MineCfgInt("vendor-price", 0));
        set => Cfg.Set(Server + "mine.vendor-price", Math.Max(0, value).ToString());
    }
    // A vendor that refused once is full for the rest of the run (Damian, 2026-10-21: "once you get a refusal to store
    // at the vendor, skip checking during that circuit run"): the ingots go straight to the house floor.
    bool m_mineVendorFull, m_mineStashFull;
    const string StashRefused = "the ingot stash did not take the ingots; they stay in the pack";
    string? MineStoreVendor(GameLink k, out int put)
    {
        put = 0;
        int price = MineVendorPrice;
        if (price <= 0)
            return "no price per ingot is set for the vendor";
        if (PackCount(k, g => g == IngotArt) <= 0)
            return null;
        if (m_mineVendorFull)
        {
            int fp0;
            string? fw0 = MineStoreHomeFloor(k, out fp0);
            put += fp0;
            return fw0 == null ? null : "the vendor is full this run, and the house floor: " + fw0;
        }
        string? w;
        uint pack = HouseVendorPack(k, out w);
        if (pack == 0)
            return w;
        for (int pile = 0; !circuitStop && !k.Closed; pile++)
        {
            AssistItem? st = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && it.Graphic == IngotArt
                        && (st == null || it.Amount > st.Amount))
                        st = it;
            }
            int n = Math.Min(VendorPileMax, PackCount(k, g => g == IngotArt));
            if (st == null || n <= 0)
                break;
            n = Math.Min(n, Math.Max(1, st.Amount));
            CircuitStatus = "Mine: " + n + " ingots up for sale at " + n * price;
            w = VendorGiveOne(k, pack,
                new VendorEntry { Serial = st.Serial, Amount = n, Price = n * price, What = n + " iron ingots" }, pile);
            if (w != null)
            {
                // Refused and back in the pack: the vendor takes no more, and the house floor keeps the rest.
                AssistItem? back = Item(k, st.Serial);
                bool inPack;
                lock (k.State.Gate)
                    inPack = back != null && PackTree(k).Contains(back.Container);
                if (!inPack)
                    return "putting " + n + " ingots up for sale: " + w;
                Note("Mine: the vendor took no more ingots (" + w + "); to the house floor for the rest of this run");
                m_mineVendorFull = true;
                int fp;
                string? fw = MineStoreHomeFloor(k, out fp);
                put += fp;
                return fw == null ? null : "the vendor took no more, and the house floor: " + fw;
            }
            put += n;
            Note("Mine: " + n + " ingots for sale on the vendor at " + n * price + " gp");
        }
        return null;
    }
    // Shovels left on the house vendor at the default price (Damian, 2026-10-20: "i put shovels for sale on the vendor
    // but it really just a temporary spot for me to put them while mining and any shovel at the default price of 14 can
    // be taken from the vendor and used for mining"): each read by a click and taken back into the pack up to
    // MineCarry; one that does not reach the pack ends the taking. got: how many.
    public const string ShovelParkedCost = "14";
    string? ShovelsFromVendor(GameLink k, out int got)
    {
        got = 0;
        string? w;
        uint pack = HouseVendorPack(k, out w);
        if (pack == 0)
            return w;
        List<AssistItem> shovels = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == pack && DigTool(it.Graphic))
                    shovels.Add(it);
        if (shovels.Count == 0)
            return "the vendor holds no shovel";
        Map<uint, string> labels;
        Map<uint, string> costs = VendorCosts(k, shovels, out labels, out w);
        if (w != null)
            return "reading the vendor's shovels " + w;
        uint backpack;
        lock (k.State.Gate)
            backpack = k.State.Backpack;
        foreach (AssistItem s in shovels)
        {
            if (Shovels(k).Count >= MineCarry || circuitStop)
                break;
            string? line;
            costs.TryGet(s.Serial, out line);
            if (line == null || line.Substring(line.IndexOf("Cost:") + 5).Trim() != ShovelParkedCost)
                continue;
            using (Act(k))
                w = MoveStack(k, s.Serial, 1, "a shovel", "Taking a shovel from the vendor",
                    () => k.Drop(s.Serial, backpack)!);
            if (w != null)
                return w;
            AssistItem? back = Item(k, s.Serial);
            bool inPack;
            lock (k.State.Gate)
                inPack = back != null && PackTree(k).Contains(back.Container);
            if (!inPack)
                return "the shovel " + s.Serial.ToString("X") + " did not come into the pack";
            got++;
        }
        return got == 0 ? "no shovel on the vendor is at " + ShovelParkedCost + " gp" : null;
    }
}
