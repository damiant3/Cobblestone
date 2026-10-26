#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Uoaix.Assist.Tests;

// The homespun circuit over three towns (HomespunWorldTests has one shop): Hometown with the house; Twotown with two
// tailor shops forty tiles apart, walked between; Thirdtown with one. Each tailor sells thread and kits, buys up to 20
// shirts a restock and restocks every RestockPeriodSeconds; the server draws a tailor within ViewRange of the player and
// drops her past it. The loops of 2026-10-24 (16:09: short of mana, every shop passed over in a lap; 18:38: two shops
// of one town lapped) lived in exactly this shape of order.
public static class HomespunTownsTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x402B0010;
    const uint HouseRune = 0x402B0012;
    const uint House = 0x402B0020;
    const uint Door = 0x402B0021;
    const uint Gold = 0x402B0032;
    const int HomeX = 30, HomeY = 30;
    // The bank of Thirdtown (region 200,180 10 by 10, center 205,185) and the tile its banker answers from.
    const uint BankBox = 0x402B0050;
    const int BankSpotX = 204, BankSpotY = 185;
    const int MaxMana = 51, RecallCost = 11, ViewRange = 18, ShirtPrice = 25, BuysPerRestock = 20;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("homespun towns: 60 shirts, 15 mana, three tailors in two towns: three hours selling all 60, no "
            + "stall, no recall between Twotown's two shops, bounded recalls", () => Towns(false, true, 60, 40)),
        new TestCase("homespun towns: Kimmie never answers: the other two sell all 60, Kimmie asked about once a "
        + "restock, no stall, no stop", () => Towns(true, true, 60, 40)),
        // Nobody restocks: the shops are looked at again once a restock for thread come back (about ten restocks in
        // three hours, two recalls each at most); before the unread-stock bound it recalled every top, 151 times.
        new TestCase("homespun towns: nobody restocks: one round of buying and selling (60 sold, 20 each), then a "
        + "look once a restock, no stall, bounded recalls", () => Towns(false, false, 60, 24)),
        // Gold over a bank-over of 800: banked at Thirdtown's bank on the way home, the pack never far past it.
        new TestCase("homespun towns: bank-over 800, sales growing the gold: banked at Thirdtown's bank, the pack "
        + "never far over the setting, all 60 sold, no stall, bounded recalls", () => Towns(false, true, 60, 50, 800)),
        // The fourth 2026-10-24 audit (1): carried cloth or bolts made every shop work, though homespun sews only at
        // home.
        new TestCase("homespun towns: nobody restocks and 50 bolts carried: the bolts make no shop work, recalls "
        + "bounded as with none", () => Towns(false, false, 60, 24, bolts: 50)),
        // Morena walked off after the first "sell" and answered from past a sale's reach (Damian, 2026-10-25).
        new TestCase("homespun towns: Morena walks to her shop's far edge at the first sell: closed in on and sold to, "
        + "all 60 sold, bounded recalls", () => Towns(false, true, 60, 40, walksOff: true)),
        // Thread is bought after the shirts are sold (Damian, 2026-10-25): a shop selling thread and buying no shirts
        // is not recalled to with shirts carried, but for the first look at its stock.
        new TestCase("homespun towns: Morena sells thread and buys no shirts: no recall to her with shirts carried after "
        + "the first, all 60 sold, bounded recalls", () => Towns(false, true, 60, 40, threadOnly: true)),
        // The house in no town, a teleporter beside it into Twotown (the Moonglow house's pad, Damian 2026-10-25):
        // Twotown's shops are walked to from home, never recalled to.
        new TestCase("homespun towns: the house in no town, a teleporter beside it into Twotown: no recall from home to "
        + "a Twotown shop, all 60 sold, bounded recalls", () => Towns(false, true, 60, 40, homeWalk: true)),
        // The Moonglow house in "telescope in Moonglow" (2026-10-26 11:22:18: a 244-tile walk to Brian recalled).
        new TestCase("homespun towns: the house in a guard zone named \"telescope in Twotown\", the teleporter into "
        + "Twotown: no recall from home to a Twotown shop, none home from Twotown, all 60 sold, bounded recalls",
        () => Towns(false, true, 60, 40, homeWalk: true, telescope: true)),
        // A check for a restock is not worth a trip (Damian, 2026-10-25: "if we have to travel, it costs at least 15gp
        // of pain"): a tailor left full is recalled to only once she has surely restocked.
        new TestCase("homespun towns: tailors buying 10 a restock: no recall to a tailor still full, all 60 sold, "
        + "bounded recalls", () => Towns(false, true, 60, 40, buys: 10)),
        // The sell order (Damian, 2026-10-26): each tailor sold down in its order, then rested two restocks.
        new TestCase("homespun towns: sell order Morena, Ella, Kimmie with 100 shirts: sold to in that order, each "
        + "sold down then rested two restocks, all 100 sold, bounded recalls",
        () => Towns(false, true, 100, 40, sellOrder: "Morena,Ella,Kimmie", startShirts: 100)),
        // The reagents planned ahead (Damian, 2026-10-26: "if it fits normal cycle don't make special trip").
        new TestCase("homespun towns: sell order Ella, Morena, Kimmie with 5 of each recall reagent and The Stitch a "
        + "walk from Thirdtown's bank: no reagent trip before The Stitch, its bank refills them after its sale",
        () => Towns(false, true, 100, 40, sellOrder: "Ella,Morena,Kimmie", startShirts: 100, regs: 5)));
    // The pad beside the house and where it lands, in Twotown; the pad back, in Twotown, and where it lands by the house
    // (Moonglow's pair, 4663,1134 and 4449,1115).
    const int PadX = 36, PadY = 36, PadLandX = 76, PadLandY = 76;
    const int BackPadX = 72, BackPadY = 72, BackLandX = 37, BackLandY = 37;

    public static ImmutableArray<TestCase> Cases => s_cases;

    sealed class Shop
    {
        public string Name = "", Tailor = "", Town = "";
        public int X, Y;
        public uint Vendor, Box, Rune, Thread, Kits, Shirts;
        public VendorShard? Shard;
        public int BuysLeft = BuysPerRestock;
        public bool Silent;
        public int LandX => X - 1;
        // Where the tailor stands now; WalksOff: at the first "sell" she walks to her shop's edge away from the
        // player (Morena, Vesper, 2026-10-25). Asks past SaleReach answered by the conversation, and shirts bought.
        public int VX, VY, Convo, SoldHere;
        public bool WalksOff, WalkedOff;
        // Sells thread and kits only: no shirt in stock, buys none.
        public bool NoShirts;
    }

    // The 1998 shopkeeper opens a sale only to a speaker within 3 tiles (rundir scripts.wombat/human.m:2027); past it
    // the conversation answers.
    const int SaleReach = 3;

    static readonly ImmutableArray<Shop> s_shops =
    [
        new() { Name = "The North Needle", Tailor = "Ella", Town = "Twotown", X = 83, Y = 83, Vendor = 0x002B0101,
            Box = 0x402B0102, Rune = 0x402B0103, Thread = 0x402B0104, Kits = 0x402B0105, Shirts = 0x402B0106 },
        new() { Name = "The South Needle", Tailor = "Kimmie", Town = "Twotown", X = 123, Y = 123, Vendor = 0x002B0201,
        Box = 0x402B0202, Rune = 0x402B0203, Thread = 0x402B0204, Kits = 0x402B0205, Shirts = 0x402B0206 },
        new() { Name = "The Stitch", Tailor = "Morena", Town = "Thirdtown", X = 193, Y = 193, Vendor = 0x002B0301,
        Box = 0x402B0302, Rune = 0x402B0303, Thread = 0x402B0304, Kits = 0x402B0305, Shirts = 0x402B0306 },
    ];

    sealed class World
    {
        readonly Lock m_gate = new();
        readonly FakeShard m_shard;
        readonly StepShard m_step;
        Set<uint> m_shown = Set<uint>.Empty;
        Map<uint, int[]> m_ground = Map<uint, int[]>.Empty;
        public readonly List<uint> Shirts = new();
        public readonly List<string> CastLog = new();
        // Each sale: the minutes since the start and the tailor.
        public readonly List<Tuple<double, string>> SaleLog = new();
        public DateTime Start;
        // The pack's one gold pile, shared by the three tailors.
        public readonly int[] Purse = { 500 };
        // The bank of Thirdtown: its box's gold piles by serial, and the gold banked in all.
        public ValueMap<uint, int> BoxGold = ValueMap<uint, int>.Empty;
        public int Banked;
        uint m_nextPile = 0x402B5000;
        public int Mana, Casts, Sold;
        // Recalls by a Twotown shop's rune made from inside Twotown (the walk rule: never between Twotown's shops).
        public int SameTown;
        // Recalls to a shop buying no shirts made with shirts carried.
        public int ThreadTrips;
        // The pad at PadX, PadY moves whoever steps on it to PadLandX, PadLandY; recalls by a Twotown shop's rune made
        // from home (west and north of 60).
        public bool Pad;
        public int HomeRecalls;
        // Recalls by the house rune made from inside Twotown.
        public int HomeFromTwotown;
        // Recalls to a shop whose tailor, asked before anything sold there, was full with shirts carried; the shirts
        // each tailor buys a restock.
        public int WastedTrips, Buys = BuysPerRestock;
        Shop? m_landedAt;
        bool m_soldSinceLanding;
        public bool Done;
        bool m_casting;
        uint m_lifted;
        int m_liftAmount;

        public World(FakeShard shard, StepShard step, int mana)
        {
            m_shard = shard;
            m_step = step;
            Mana = mana;
        }

        static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

        public void Tick()
        {
            m_shard.Also(2000, () =>
            {
                int mana;
                lock (m_gate)
                {
                    if (Done)
                        return;
                    if (Mana < MaxMana)
                        Mana++;
                    mana = Mana;
                }
                try
                {
                    m_shard.ServerSends(Packets.Status(mana));
                }
                catch (IOException)
                {
                    return;
                }
                Tick();
            });
        }

        public void Restocks()
        {
            m_shard.Also((int)(KeepRules.RestockPeriodSeconds * 1000), () =>
            {
                if (Done)
                    return;
                try
                {
                    foreach (Shop sh in s_shops)
                    {
                        lock (m_gate)
                            sh.BuysLeft = sh.NoShirts ? 0 : Buys;
                        m_shard.ServerSends(sh.Shard!.Restock(sh.Thread, 20));
                        m_shard.ServerSends(sh.Shard.Restock(sh.Kits, 10));
                    }
                }
                catch (IOException)
                {
                    return;
                }
                Restocks();
            });
        }

        // The tailors drawn within ViewRange of x, y and dropped past it. The caller holds m_gate.
        List<byte[]> View(int x, int y)
        {
            List<byte[]> r = new();
            foreach (Shop sh in s_shops)
            {
                bool near = Math.Max(Math.Abs(sh.VX - x), Math.Abs(sh.VY - y)) <= ViewRange;
                bool shown = m_shown.Contains(sh.Vendor);
                if (near && !shown)
                {
                    m_shown = m_shown.Add(sh.Vendor);
                    r.Add(Packets.Mobile(sh.Vendor, sh.VX, sh.VY));
                }
                else if (!near && shown)
                {
                    m_shown = m_shown.Remove(sh.Vendor);
                    r.Add(Packets.Delete(sh.Vendor));
                }
            }
            return r;
        }

        public ImmutableArray<byte[]> Answer(byte[] p)
        {
            ImmutableArray<byte[]> own = Own(p);
            ImmutableArray<byte[]> step = m_step.Answer(p);
            ImmutableArray<byte[]>.Builder all = ImmutableArray.CreateBuilder<byte[]>();
            all.AddRange(own);
            all.AddRange(step);
            foreach (Shop sh in s_shops)
                all.AddRange(sh.Shard!.Answer(p));
            if (p[0] == 0x02)
            {
                lock (m_gate)
                {
                    if (Pad && m_step.X == PadX && m_step.Y == PadY)
                    {
                        m_step.MoveTo(PadLandX, PadLandY);
                        all.Add(Packets.Draw(PadLandX, PadLandY, 0, 0));
                    }
                    else if (Pad && m_step.X == BackPadX && m_step.Y == BackPadY)
                    {
                        m_step.MoveTo(BackLandX, BackLandY);
                        all.Add(Packets.Draw(BackLandX, BackLandY, 0, 0));
                    }
                    all.AddRange(View(m_step.X, m_step.Y));
                }
            }

            return all.ToImmutable();
        }

        ImmutableArray<byte[]> Own(byte[] p)
        {
            lock (m_gate)
            {
                if (p[0] == 0x12 && p.Length > 3 && p[3] == 0x56 && !m_casting)
                {
                    Casts++;
                    if (Mana < RecallCost)
                        return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                            "Insufficient mana for this spell.", 0));
                    m_casting = true;
                    m_shard.Also(1500, () => m_shard.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                    return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0,
                        0x5A));
                }
                if (p[0] == 0x6C && m_casting && p.Length >= 11)
                {
                    m_casting = false;
                    uint rune = U32(p, 7);
                    Shop? to = s_shops.Find(sh => sh.Rune == rune);
                    if (to is null && rune != HouseRune)
                        return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                            "That is not a recall rune.", 0));
                    int x = to is null ? HomeX : to.LandX, y = to is null ? HomeY + 1 : to.Y;
                    CastLog.Add(m_shard.Clock.UtcNow.ToLocalTime().ToString("HH:mm") + " "
                        + (to is null ? "home" : to.Tailor) + " from " + m_step.X + "," + m_step.Y);
                    if (to is not null && to.Town == "Twotown" && m_step.X >= 70 && m_step.X < 150 && m_step.Y >= 70
                        && m_step.Y < 150)
                        SameTown++;
                    if (to is not null && to.NoShirts && Shirts.Count > 0)
                        ThreadTrips++;
                    if (to is not null && to.Town == "Twotown" && m_step.X < 60 && m_step.Y < 60)
                        HomeRecalls++;
                    if (to is null && m_step.X >= 70 && m_step.X < 150 && m_step.Y >= 70 && m_step.Y < 150)
                        HomeFromTwotown++;
                    m_landedAt = to;
                    m_soldSinceLanding = false;
                    Mana -= RecallCost;
                    m_step.MoveTo(x, y);
                    List<byte[]> r = new() { Packets.Draw(x, y, 0, 0), Packets.Status(Mana) };
                    r.AddRange(View(x, y));
                    return r.ToImmutableArray();
                }
                if (p[0] == 0x03 && p.Length > 8)
                {
                    string said = Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0');
                    // "bank" heard within 5 tiles of the bank's spot: the box opened with its gold piles.
                    if (said == "bank")
                    {
                        if (Math.Max(Math.Abs(m_step.X - BankSpotX), Math.Abs(m_step.Y - BankSpotY)) > 5)
                            return ImmutableArray<byte[]>.Empty;
                        List<KeyValuePair<uint, int>> gold = BoxGold.Ordered(Comparer<uint>.Default);
                        uint[] piles = new uint[gold.Count];
                        for (int i = 0; i < gold.Count; i++)
                            piles[i] = gold[i].Key;
                        List<byte[]> r = new() { Packets.Open(BankBox) };
                        byte[] contents = Packets.Contents(BankBox, piles);
                        r.Add(contents);
                        foreach (KeyValuePair<uint, int> g in gold)
                            r.Add(Packets.Contained(g.Key, AssistFeatures.GoldArt, g.Value, BankBox));
                        return r.ToImmutableArray();
                    }
                    Shop? at = s_shops.Find(sh => said == sh.Tailor + " sell");
                    if (at is not null && at.Silent)
                        return ImmutableArray<byte[]>.Empty;
                    if (at is not null)
                    {
                        if (at.WalksOff && !at.WalkedOff)
                        {
                            at.WalkedOff = true;
                            at.VX = m_step.X <= at.X ? at.X + 3 : at.X - 3;
                        }
                        if (Math.Max(Math.Abs(m_step.X - at.VX), Math.Abs(m_step.Y - at.VY)) > SaleReach)
                        {
                            at.Convo++;
                            return ImmutableArray.Create(Packets.Mobile(at.Vendor, at.VX, at.VY),
                                Packets.Speech(at.Vendor, at.Tailor, "I may be interested in what thou have-let me see "
                                + "it. Otherwise thou art welcome to buy my goods.", 0));
                        }
                        if (Shirts.Count > 0 && at.BuysLeft <= 0 && at == m_landedAt && !m_soldSinceLanding)
                            WastedTrips++;
                        if (Shirts.Count == 0 || at.BuysLeft <= 0)
                            return ImmutableArray.Create<byte[]>(Packets.Speech(at.Vendor, at.Tailor,
                                "You have nothing I would be interested in.", 0));
                        int n = Math.Min(Shirts.Count, at.BuysLeft);
                        uint vs = at.Vendor;
                        List<byte> b = new() { 0x9E, 0, 0, (byte)(vs >> 24), (byte)(vs >> 16), (byte)(vs >> 8),
                            (byte)vs, (byte)(n >> 8), (byte)n };
                        byte[] name = Encoding.ASCII.GetBytes("fancy shirt");
                        for (int i = 0; i < n; i++)
                        {
                            uint sv = Shirts[i];
                            b.AddRange(new byte[] { (byte)(sv >> 24), (byte)(sv >> 16), (byte)(sv >> 8), (byte)sv,
                                0x1E, 0xFD, 0, 0, 0, 1, 0, ShirtPrice, 0, (byte)name.Length });
                            b.AddRange(name);
                        }
                        b[1] = (byte)(b.Count >> 8);
                        b[2] = (byte)b.Count;
                        return ImmutableArray.Create<byte[]>(b.ToArray());
                    }
                }
                if (p[0] == 0x9F && p.Length >= 9)
                {
                    Shop? at = s_shops.Find(sh => sh.Vendor == U32(p, 3));
                    if (at is null)
                        return ImmutableArray<byte[]>.Empty;
                    ImmutableArray<byte[]>.Builder r = ImmutableArray.CreateBuilder<byte[]>();
                    int paid = 0;
                    for (int i = 9; i + 6 <= p.Length && at.BuysLeft > 0; i += 6)
                    {
                        uint sv = U32(p, i);
                        if (!Shirts.Remove(sv))
                            continue;
                        at.BuysLeft--;
                        at.SoldHere++;
                        Sold++;
                        paid += ShirtPrice;
                        r.Add(Packets.Delete(sv));
                    }
                    if (paid > 0)
                    {
                        r.Add(at.Shard!.Paid(paid));
                        m_soldSinceLanding = true;
                        SaleLog.Add(Tuple.Create((m_shard.Clock.UtcNow - Start).TotalMinutes, at.Tailor));
                    }
                    return r.ToImmutable();
                }
                if (p[0] == 0x07 && p.Length >= 7)
                {
                    m_lifted = U32(p, 1);
                    m_liftAmount = p[5] << 8 | p[6];
                    return ImmutableArray<byte[]>.Empty;
                }
                // The pack's gold dropped into the bank box, or onto a pile in it: banked.
                if (p[0] == 0x08 && p.Length >= 14 && U32(p, 1) == m_lifted && m_lifted == Gold
                    && (U32(p, 10) == BankBox || BoxGold.ContainsKey(U32(p, 10))))
                {
                    uint into = U32(p, 10);
                    int n = Math.Min(m_liftAmount <= 0 ? Purse[0] : m_liftAmount, Purse[0]);
                    m_lifted = 0;
                    Purse[0] -= n;
                    Banked += n;
                    uint pile = into == BankBox ? m_nextPile++ : into;
                    int piled = BoxGold.Get(pile, 0) + n;
                    BoxGold = BoxGold.Set(pile, piled);
                    return ImmutableArray.Create(Packets.Contained(Gold, AssistFeatures.GoldArt, Purse[0], Pack),
                        Packets.Contained(pile, AssistFeatures.GoldArt, piled, BankBox));
                }
                if (p[0] == 0x08 && p.Length >= 14 && U32(p, 1) == m_lifted)
                {
                    uint into = U32(p, 10);
                    int x = p[5] << 8 | p[6], y = p[7] << 8 | p[8];
                    int n = Math.Max(1, m_liftAmount);
                    m_lifted = 0;
                    if (m_ground.TryGet(into, out int[]? onto))
                    {
                        onto[0] += n;
                        return ImmutableArray.Create(Packets.Delete(U32(p, 1)),
                            Lying(into, 0x0FA0, onto[0], onto[1], onto[2]));
                    }
                    if (into != 0xFFFFFFFF)
                        return ImmutableArray<byte[]>.Empty;
                    m_ground = m_ground.Set(U32(p, 1), new[] { n, x, y });
                    return ImmutableArray.Create<byte[]>(Lying(U32(p, 1), 0x0FA0, n, x, y));
                }
                return ImmutableArray<byte[]>.Empty;
            }
        }
    }

    // The server file of learned teleporters set to these lines (by "\n"), or emptied (null), and the features' copy
    // dropped so the file is read again.
    static void SetTeleporters(AssistFeatures f, string? line)
    {
        string p = f.ServerFile("teleporters");
        try
        {
            if (line == null)
                File.Delete(p);
            else
                File.WriteAllLines(p, line.Split('\n'));
        }
        catch (IOException)
        {
        }

        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo known = typeof(AssistFeatures).GetField("m_teleporters", flags)!;
        known.SetValue(f, known.FieldType.GetProperty("Empty")!.GetValue(null));
        typeof(AssistFeatures).GetField("m_teleportersLoaded", flags)!.SetValue(f, false);
    }

    // The notes (joined by " | ") naming what, the first `most` of them.
    static string Around(string notes, string what, int most)
    {
        List<string> l = new();
        foreach (string n in notes.Split(" | "))
            if (n.Contains(what) && l.Count < most)
                l.Add(n);
        return string.Join(" | ", l);
    }

    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint t = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    // Kwik home with 60 shirts and 15 mana, the recall reagents, 500 gold; three tailors, each buying 20 a restock.
    // silent: Kimmie answers nothing; restocks: the tailors restock; wantSold: the shirts sold in three hours;
    // maxCasts: the recalls allowed.
    static void Towns(bool silent, bool restocks, int wantSold, int maxCasts, int bankOver = 0, int bolts = 0,
        bool walksOff = false, bool threadOnly = false, bool homeWalk = false, int buys = BuysPerRestock,
        string sellOrder = "", int startShirts = 60, int regs = 30, bool telescope = false)
    {
        int homeFromTwotown = 0;
        bool bank = bankOver > 0 || regs < AssistFeatures.RecallLow;
        ImmutableArray<string> walkBankWas = AssistFeatures.WalkBankShops;
        int banked = 0, goldEnd = 0, threadTrips = 0, homeRecalls = 0, wastedTrips = 0;
        List<Tuple<double, string>> sales = new();
        List<string> allNotes = new();
        int kimmieAsked = 0;
        string idleWhy = "";
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-homespuntowns-" + Environment.ProcessId + ".txt");
        List<string> regions = new()
        {
            "R 70 70 80 80 -128 127 0 CITY_TWOTOWN a b c d e Twotown",
            "R 170 170 80 80 -128 127 0 CITY_THIRDTOWN a b c d e Thirdtown",
        };
        if (!homeWalk)
            regions.Insert(0, "R 0 0 60 60 -128 127 0 CITY_HOMETOWN a b c d e Hometown");
        // The Moonglow house stands in a guard zone named for a landmark of the town, a town of its own to City.
        if (telescope)
            regions.Add("R 20 20 25 25 -128 127 0 JUSTICE_TWOTOWN_3 a b c d e telescope in Twotown");
        foreach (Shop sh in s_shops)
            regions.Add("R " + (sh.X - 3) + " " + (sh.Y - 3) + " 7 7 -128 127 0 CLOTHIER_" + sh.Town.ToUpperInvariant()
                + " a b c d e " + sh.Name);
        if (bank)
            regions.Add("R 200 180 10 10 -128 127 0 BANK_THIRDTOWN a b c d e Bank of Thirdtown");
        File.WriteAllLines(path, regions);
        TestMap map = new TestMap().MultiHouse(0x40, 4);
        string said = "", castLog = "", broken = "";
        int casts = 0, sold = 0, stalls = 0, errors = 0, sameTown = 0;
        bool running = false;
        try
        {
            map.Run(() => RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                HomespunWorldTests.ClearRecords(f);
                SetTeleporters(f, !homeWalk ? null : PadX + "," + PadY + ",0>" + PadLandX + "," + PadLandY + ",0"
                    + (telescope ? "\n" + BackPadX + "," + BackPadY + ",0>" + BackLandX + "," + BackLandY + ",0" : ""));
                f.Homespun = true;
                f.HouseLocked = false;
                f.SellOrder = ImmutableArray.Create(sellOrder.Split(',', StringSplitOptions.RemoveEmptyEntries));
                f.SellRest = 2;
                f.Cfg.Set(f.Server + "runes.shop", Bag.ToString("X"));
                f.Cfg.Set(f.Server + "overflow.rune", HouseRune.ToString("X"));
                f.Cfg.Set(f.Server + "overflow.pile", HomeX + "," + HomeY + ",0");
                int overWas = f.BankOver;
                if (bankOver > 0)
                    f.BankOver = bankOver;
                if (bank)
                    f.Cfg.Set(f.Server + "bankspot.205,185", BankSpotX + "," + BankSpotY);
                // The reagents' case: The Stitch is the shop a walk from a bank, Thirdtown's.
                if (regs < AssistFeatures.RecallLow)
                    AssistFeatures.WalkBankShops = ImmutableArray.Create(
                        UoRegions.AllShopsIn("Thirdtown").Find(x => x.Text == "The Stitch")!.Key);
                StepShard step = new(s, map, HomeX, HomeY - 1);
                World world = new(s, step, 15);
                world.Pad = homeWalk;
                world.Buys = buys;
                s.Answer = world.Answer;
                UoMap.PutMulti(House, 0x40, HomeX, HomeY, 0);
                step.Door(Door, TestMap.DoorShut, HomeX, HomeY + 4, true);
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
                foreach (Shop sh in s_shops)
                {
                    s.ServerSends(Packets.Contained(sh.Rune, 0x1F14, 1, Bag));
                    s.ServerSends(Packets.Speech(sh.Rune, "", sh.Name, 6));
                }
                s.ServerSends(Packets.Contained(HouseRune, 0x1F14, 1, Pack));
                s.ServerSends(Packets.Speech(HouseRune, "", "Home", 6));
                s.ServerSends(Packets.Contained(0x402B0040, 0x0F86, regs, Pack));
                s.ServerSends(Packets.Contained(0x402B0041, 0x0F7B, regs, Pack));
                s.ServerSends(Packets.Contained(0x402B0042, 0x0F7A, regs, Pack));
                if (bolts > 0)
                    s.ServerSends(Packets.Contained(0x402B0080, 0x0F95, bolts, Pack));
                for (uint i = 0; i < startShirts; i++)
                {
                    s.ServerSends(Packets.Contained(0x402B1000 + i, 0x1EFD, 1, Pack));
                    world.Shirts.Add(0x402B1000 + i);
                }
                s.ServerSends(Packets.Status(15));
                s.Clock.Sleep(1);
                bool first = true;
                foreach (Shop sh in s_shops)
                {
                    VendorShard v = new(s, sh.Vendor, sh.Box, sh.X, sh.Y, Pack, Gold, 500, sh.Tailor, world.Purse);
                    first = false;
                    v.Stock(sh.Thread, 0x0FA0, 20, 3, "Spool of thread");
                    v.Stock(sh.Kits, AssistFeatures.KitArt, 10, 3, "Sewing kit");
                    sh.NoShirts = threadOnly && sh.Tailor == "Morena";
                    if (!sh.NoShirts)
                        v.Stock(sh.Shirts, 0x1EFD, 10, 40, "Fancy shirt");
                    s.ServerSends(Packets.Speech(sh.Vendor, sh.Tailor, sh.Tailor + " the tailor", 6));
                    sh.Shard = v;
                    sh.BuysLeft = sh.NoShirts ? 0 : world.Buys;
                    sh.VX = sh.X;
                    sh.VY = sh.Y;
                    sh.Convo = sh.SoldHere = 0;
                    sh.WalksOff = walksOff && sh.Tailor == "Morena";
                    sh.WalkedOff = false;
                    sh.Silent = silent && sh.Tailor == "Kimmie";
                    v.Silent = sh.Silent;
                    // A sell order names tailors the atlas knows from an earlier day.
                    if (sellOrder.Length > 0)
                        foreach (byte[] kp in v.Known(sh.X, sh.Y))
                            s.ServerSends(kp);
                    // Out of view of home: dropped until the player comes within range.
                    s.ServerSends(Packets.Delete(sh.Vendor));
                }
                _ = first;
                s.Answer = world.Answer;
                world.Tick();
                if (restocks)
                    world.Restocks();
                s.Clock.Sleep(1);
                world.Start = s.Clock.UtcNow;
                Check.Null(f.CircuitGo(), "CircuitGo");
                // Every note as it is written: the features keep the last 100.
                long seen = 0;
                for (int i = 0; i < 3 * 60 * 60 && f.CircuitRunning; i++)
                {
                    s.Clock.Sleep(1000);
                    lock (f.Notes)
                    {
                        int fresh = (int)Math.Min(f.NotesAdded - seen, f.Notes.Count);
                        for (int j = f.Notes.Count - fresh; j < f.Notes.Count; j++)
                            allNotes.Add(f.Notes[j]);
                        seen = f.NotesAdded;
                    }
                }
                running = f.CircuitRunning;
                lock (f.Notes)
                {
                    said = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Circuit:") || n.Contains("Walk:")));
                    stalls = f.Notes.FindAll(n => n.Contains("no progress in")).Count;
                    errors = f.Notes.FindAll(n => n.Contains("an error in the shop loop")
                        || n.Contains("an error at the top")).Count;
                }
                casts = world.Casts;
                castLog = string.Join(", ", world.CastLog);
                sold = world.Sold;
                sameTown = world.SameTown;
                threadTrips = world.ThreadTrips;
                homeRecalls = world.HomeRecalls;
                homeFromTwotown = world.HomeFromTwotown;
                wastedTrips = world.WastedTrips;
                sales.AddRange(world.SaleLog);
                foreach (byte[] sp in s.ServerGot)
                {
                    if (sp[0] == 0x03 && sp.Length > 8
                        && Encoding.ASCII.GetString(sp, 8, sp.Length - 8).TrimEnd('\0').StartsWith("Kimmie "))
                        kimmieAsked++;
                }

                broken = f.BrokenList();
                banked = world.Banked;
                goldEnd = world.Purse[0];
                f.BankOver = overWas;
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                MethodInfo idleOf = typeof(AssistFeatures).GetMethod("ShopIdle", flags)!;
                foreach (string town in new[] { "Twotown", "Thirdtown" })
                {
                    foreach (UoRegions.Shop us in UoRegions.AllShopsIn(town))
                        idleWhy += us.Text + "=" + (idleOf.Invoke(f, new object[] { s.Link, us, town }) ?? "WORK")
                            + "; ";
                }

                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                world.Done = true;
                s.Clock.Sleep(3000);
            }));
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
            RecallTests.WithFeatures((s, f) =>
            {
                SetTeleporters(f, null);
                f.Cfg.Set(f.Server + "runes.shop", "");
                f.Cfg.Set(f.Server + "overflow.rune", "");
                f.Cfg.Set(f.Server + "overflow.pile", "");
                f.Homespun = false;
                f.SellOrder = ImmutableArray<string>.Empty;
                AssistFeatures.WalkBankShops = walkBankWas;
            });
        }
        string tail = said.Length > 2500 ? said.Substring(said.Length - 2500) : said;
        Check.True(running, "still running after three hours: " + tail);
        Check.Equal(0, errors, "no error: " + tail);
        Check.Equal(0, stalls, "no stalled lap: " + tail);
        Check.Equal(wantSold, sold, "the shirts sold: [" + castLog + "] " + tail);
        Check.Equal(0, sameTown, "no recall from one Twotown shop to the other: [" + castLog + "]");
        Check.True(casts <= maxCasts,
            "recalls bounded (" + maxCasts + "): " + casts + " [" + castLog + "] {" + idleWhy + "} " + tail);
        // Silent: asked once and passed over for a restock each time, buy and sell: about 2 a restock in three hours.
        if (silent)
            Check.True(kimmieAsked <= 24, "Kimmie, who never answers, asked about once a restock: " + kimmieAsked);
        foreach (Shop sh in s_shops)
            Check.False(broken.Contains(sh.Vendor.ToString("X")), "no tailor BROKEN: [" + broken + "]");
        if (buys < BuysPerRestock)
            Check.Equal(0, wastedTrips, "no recall to a tailor still full: [" + castLog + "] " + tail);
        if (homeWalk)
            Check.Equal(0, homeRecalls, "no recall from home to a Twotown shop: [" + castLog + "] " + tail);
        // Home walked to from the house's town (Damian, 2026-10-26: "run home from brian").
        if (telescope)
            Check.Equal(0, homeFromTwotown, "no recall home from Twotown: [" + castLog + "] "
                + Around(string.Join(" | ", allNotes), "walking home", 4));
        if (threadOnly)
            Check.True(threadTrips <= 1, "recalls to the thread-only shop with shirts carried: " + threadTrips + " ["
                + castLog + "] " + tail);
        if (walksOff)
        {
            Shop morena = s_shops.Find(sh => sh.Tailor == "Morena")!;
            // Once: the answer from afar is followed by the walk to her, never by the same ask from where it failed.
            Check.Equal(1, morena.Convo, "Morena asked from past her sale's reach once");
            Check.True(morena.SoldHere > 0, "Morena closed in on and sold to: " + morena.SoldHere + " (" + morena.Convo
                + " answers from afar) " + tail);
        }
        if (sellOrder.Length > 0)
        {
            string log = string.Join(", ", sales.ConvertAll(t => t.Item1.ToString("F0") + " " + t.Item2));
            List<string> first = new();
            foreach (Tuple<double, string> t in sales)
                if (!first.Contains(t.Item2))
                    first.Add(t.Item2);
            Check.Equal(sellOrder.Replace(",", " "), string.Join(" ", first), "the tailors first sold to, in order: "
                + log);
            // Sold down, then rested two restocks: no tailor sold to again within 1.25 periods of her last sale before
            // a gap (one restock's rest would sell again at her first restock, about one period on).
            double period = KeepRules.RestockPeriodSeconds / 60;
            ValueMap<string, double> last = ValueMap<string, double>.Empty;
            foreach (Tuple<double, string> t in sales)
            {
                if (last.TryGet(t.Item2, out double was) && t.Item1 - was > 2)
                    Check.True(t.Item1 - was >= 1.25 * period, t.Item2 + " sold to again "
                        + (t.Item1 - was).ToString("F0") + " minutes after her rest began: " + log + " "
                        + Around(string.Join(" | ", allNotes), t.Item2, 14));
                last = last.Set(t.Item2, t.Item1);
            }
        }
        // The reagents planned ahead (Damian, 2026-10-26): under the warning but enough for the recalls to The Stitch,
        // the shop by a bank, no trip for them before it; leaving it, its bank refills them.
        if (regs < AssistFeatures.RecallLow)
        {
            int soldMorena = allNotes.FindIndex(n => n.Contains("to Morena"));
            int regsStop = allNotes.FindIndex(n => n.Contains("recall reagents"));
            string notes = string.Join(" | ", allNotes.GetRange(0, Math.Min(40, allNotes.Count)));
            Check.True(soldMorena >= 0, "sold to Morena: " + notes);
            Check.True(regsStop > soldMorena, "the reagents' bank stop after The Stitch's sale, none before: " + notes);        }
        if (bankOver > 0)
        {
            Check.True(banked > 0,
                "gold banked: " + banked + " banked, " + goldEnd + " in the pack: [" + castLog + "] " + tail);
            Check.True(goldEnd <= bankOver + BuysPerRestock * ShirtPrice,
                "the pack never far past the bank-over at the end: " + goldEnd);
        }
    }
}
