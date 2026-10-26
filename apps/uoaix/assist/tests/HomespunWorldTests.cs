#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;

namespace Uoaix.Assist.Tests;

// The homespun tailoring circuit run for hours in a small world (Damian, 2026-10-24: "your code is negative value until
// you make it work as it is supposed to without excuses and without and more using the human being as a test rig"):
// Hometown with the house (walls, a door, a floor, the thread pile), Shoptown with The Stitch and its tailor; the house
// rune and the shop's rune in the tailor runes bag; recalls that land by rune and cost mana, mana that comes back,
// lifts and drops onto the pile. Each case checks the circuit's rules over its run: never stopping of itself, no lap
// without progress, no recall without a task, no ask repeated after no answer.
public static class HomespunWorldTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x402A0010;
    const uint ShopRune = 0x402A0011;
    const uint HouseRune = 0x402A0012;
    const uint House = 0x402A0020;
    const uint Door = 0x402A0021;
    const uint Vendor = 0x002A0030;
    const uint Box = 0x402A0031;
    const uint Gold = 0x402A0032;
    const int HomeX = 60, HomeY = 60, ShopX = 193, ShopY = 193;
    const int MaxMana = 51, RecallCost = 11;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("homespun world: home dry, a tailor selling thread and kits: three hours with no stop, no stall, "
            + "a bounded number of recalls and asks, thread brought home", () => Run(30, false, 10, true)),
        new TestCase("homespun world: one of each recall reagent and no bank anywhere: three hours with no stop, no "
        + "BROKEN shop, no recall it cannot make", () => Run(1, false, 2, false)),
        new TestCase("homespun world: the shop's rune lands blocked every time: three hours with no stop, the blocked "
        + "rune cast once a restock at most", () => Run(30, true, 12, false)),
        new TestCase("homespun world: 40 shirts at home, a tailor buying 20 a restock and restocking thread: three "
        + "hours selling all 40, thread home, no stall, bounded recalls", () => Run(30, false, 24, true, 40, true)),
        new TestCase("homespun world: 800 cloth on the house floor and 3 kits of 30 uses: three hours sewing, "
        + "selling, kits bought as they wear, no stall, bounded recalls",
        () => Run(30, false, 40, false, 0, true, 800, 3)),
        new TestCase("homespun world: the first sew's kit use never answered (a server stall): the kit not used again "
        + "on the stall, no THREAD OVERFLOW with no shirts carried, the cloth sewn after",
        () => Run(30, false, 40, false, 0, true, 800, 3, 1)));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // The server's side of the world, beside StepShard's walking and VendorShard's tailor.
    sealed class World
    {
        readonly Lock m_gate = new();
        readonly FakeShard m_shard;
        readonly StepShard m_step;
        public VendorShard? Tailor;
        public int Mana = MaxMana;
        public int Casts;
        public bool Done;
        // Every recall by the shop's rune answered "Something is blocking the location.", its mana spent.
        public bool BlockShop;
        // Each recall's time and rune, for a failure's message.
        public readonly List<string> CastLog = new();
        // The shirts in the pack, and how many more the tailor buys before her next restock, at ShirtPrice each.
        public readonly List<uint> Shirts = new();
        public int BuysLeft;
        public int Sold;
        public const int ShirtPrice = 25, BuysPerRestock = 20;
        // Sewing: each kit's uses left (a worn kit deleted, as tailor.m does), the floor's cut cloth, the kit in use
        // and the menu step; 8 cloth a fancy shirt.
        public ValueMap<uint, int> Kits = ValueMap<uint, int>.Empty;
        public const uint ClothPile = 0x402A0060;
        public int Cloth, Sewn;
        public const int KitUses = 30, ShirtCloth = 8;
        // Kit uses still to be answered by nothing, as uo1998 answered none for 6 s (2026-10-25 20:39:57), and when
        // the last was.
        public int StallSews;
        public DateTime StalledAt = DateTime.MinValue;
        uint m_sewKit;
        int m_sewStep;
        uint m_nextShirt = 0x402A2000;
        bool m_casting;
        uint m_lifted;
        int m_liftAmount;
        Map<uint, int[]> m_ground = Map<uint, int[]>.Empty;

        public World(FakeShard shard, StepShard step)
        {
            m_shard = shard;
            m_step = step;
        }

        // Thread lying in the house: the amount on the ground within the house's square.
        public int ThreadHome
        {
            get
            {
                int n = 0;
                lock (m_gate)
                {
                    foreach (int[] g in m_ground.Values)
                    {
                        if (Math.Abs(g[1] - HomeX) <= 3 && Math.Abs(g[2] - HomeY) <= 3)
                            n += g[0];
                    }
                }

                return n;
            }
        }

        // The tailor's restock every RestockPeriodSeconds, as uo1998's tick: her thread and kits back, her buying
        // renewed.
        public void Restocks()
        {
            m_shard.Also((int)(KeepRules.RestockPeriodSeconds * 1000), () =>
            {
                if (Done || Tailor is null)
                    return;
                byte[] thread = Tailor.Restock(0x402A0050, 20), kits = Tailor.Restock(0x402A0051, 10);
                lock (m_gate)
                    BuysLeft = BuysPerRestock;
                try
                {
                    m_shard.ServerSends(thread);
                    m_shard.ServerSends(kits);
                }
                catch (IOException)
                {
                    return;
                }
                Restocks();
            });
        }

        // Mana back one point every two seconds, as a meditation-free 1998 character's is, told by its status.
        public void Tick()
        {
            m_shard.Also(2000, () =>
            {
                bool done;
                int mana;
                lock (m_gate)
                {
                    done = Done;
                    if (Mana < MaxMana)
                        Mana++;
                    mana = Mana;
                }
                if (done)
                    return;
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

        static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

        public ImmutableArray<byte[]> Answer(byte[] p)
        {
            ImmutableArray<byte[]> own = Own(p);
            ImmutableArray<byte[]> step = m_step.Answer(p);
            ImmutableArray<byte[]> tailor = Tailor is null ? ImmutableArray<byte[]>.Empty : Tailor.Answer(p);
            return own.AddRange(step).AddRange(tailor);
        }

        // A kit: one the world gave, or one the tailor sold (its art), counted from KitUses on its first use.
        bool IsKit(uint serial)
        {
            if (Kits.ContainsKey(serial))
                return true;
            if (Tailor is null || !Tailor.Created.Exists(c => c[0] == serial && c[1] == AssistFeatures.KitArt))
                return false;
            Kits = Kits.Set(serial, KitUses);
            return true;
        }

        // 0x7C: dialog serial, menu id, the question, then model, hue, a length byte and the text per entry.
        static byte[] Menu(uint dialog, int id, string question, params string[] entries)
        {
            List<byte> b = new() { 0x7C, 0, 0, (byte)(dialog >> 24), (byte)(dialog >> 16), (byte)(dialog >> 8),
                (byte)dialog, (byte)(id >> 8), (byte)id };
            byte[] q = Encoding.ASCII.GetBytes(question);
            b.Add((byte)q.Length);
            b.AddRange(q);
            b.Add((byte)entries.Length);
            foreach (string e in entries)
            {
                byte[] t = Encoding.ASCII.GetBytes(e);
                b.AddRange(new byte[] { 0x1E, 0xFD, 0, 0, (byte)t.Length });
                b.AddRange(t);
            }
            b[1] = (byte)(b.Count >> 8);
            b[2] = (byte)b.Count;
            return b.ToArray();
        }

        // The sew: the kit's use answered by a cursor, the cloth aimed at by the categories, the pick by the shirts,
        // the pick by the shirt a second on (8 cloth gone, the kit worn); null for a packet not the sew's. The caller
        // holds m_gate.
        ImmutableArray<byte[]>? Sewing(byte[] p)
        {
            if (p[0] == 0x06 && p.Length >= 5 && IsKit(U32(p, 1)))
            {
                if (StallSews > 0)
                {
                    StallSews--;
                    StalledAt = m_shard.Clock.UtcNow;
                    return ImmutableArray<byte[]>.Empty;
                }
                m_sewKit = U32(p, 1);
                m_sewStep = 1;
                return ImmutableArray.Create<byte[]>(Packets.Cursor(0x0007237B, 0x00));
            }
            if (p[0] == 0x6C && m_sewStep == 1 && p.Length >= 11)
            {
                if (U32(p, 7) != ClothPile || Cloth < ShirtCloth)
                {
                    m_sewStep = 0;
                    return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                        "You don't have enough cloth to make that.", 0));
                }
                m_sewStep = 2;
                return ImmutableArray.Create<byte[]>(Menu(0x45AD0001, 0x11, "Choose a category.", "shirts", "pants"));
            }
            if (p[0] == 0x7D && m_sewStep == 2)
            {
                m_sewStep = 3;
                return ImmutableArray.Create<byte[]>(Menu(0x45AD0002, 0x12, "What kind of shirt?", "Fancy shirt",
                    "Shirt"));
            }
            if (p[0] == 0x7D && m_sewStep == 3)
            {
                m_sewStep = 0;
                uint kit = m_sewKit;
                m_shard.Also(1000, () =>
                {
                    List<byte[]> r = new();
                    lock (m_gate)
                    {
                        Cloth -= ShirtCloth;
                        r.Add(Cloth > 0 ? Lying(ClothPile, TestMap.Cloth, Cloth, HomeX + 1, HomeY + 1)
                            : Packets.Delete(ClothPile));
                        uint shirt = m_nextShirt++;
                        Shirts.Add(shirt);
                        Sewn++;
                        r.Add(Packets.Contained(shirt, 0x1EFD, 1, Pack));
                        r.Add(Packets.Speech(0xFFFFFFFF, "", "You create the fancy shirt and put it in your backpack.",
                            0));
                        int uses = Kits.Get(kit, 0) - 1;
                        Kits = Kits.Set(kit, uses);
                        if (uses <= 0)
                        {
                            Kits = Kits.Remove(kit);
                            r.Add(Packets.Delete(kit));
                        }
                    }
                    try
                    {
                        foreach (byte[] b in r)
                            m_shard.ServerSends(b);
                    }
                    catch (IOException)
                    {
                    }
                });
                return ImmutableArray<byte[]>.Empty;
            }
            return null;
        }

        ImmutableArray<byte[]> Own(byte[] p)
        {
            lock (m_gate)
            {
                if (Sewing(p) is ImmutableArray<byte[]> sewn)
                    return sewn;
                // Recall: the words, the cursor 1500 ms on, the rune targeted: landed by the rune, 11 mana spent.
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
                    CastLog.Add(m_shard.Clock.UtcNow.ToLocalTime().ToString("HH:mm")
                        + (rune == HouseRune ? " home" : rune == ShopRune ? " shop" : " other"));
                    int x = rune == HouseRune ? HomeX : ShopX - 1, y = rune == HouseRune ? HomeY + 1 : ShopY;
                    if (rune != HouseRune && rune != ShopRune)
                        return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                            "That is not a recall rune.", 0));
                    if (rune == ShopRune && BlockShop)
                    {
                        Mana -= RecallCost;
                        return ImmutableArray.Create(Packets.Speech(0xFFFFFFFF, "",
                            "Something is blocking the location.", 0), Packets.Status(Mana));
                    }
                    Mana -= RecallCost;
                    m_step.MoveTo(x, y);
                    // The server draws what is in range of the landing and drops what is not: the tailor at her shop,
                    // the floor's cloth at home (the client forgets ground items out of view).
                    byte[] tailor = rune == ShopRune ? Packets.Mobile(Vendor, ShopX, ShopY) : Packets.Delete(Vendor);
                    if (rune == HouseRune && Cloth > 0)
                        return ImmutableArray.Create(Packets.Draw(x, y, 0, 0), Packets.Status(Mana), tailor,
                            Lying(ClothPile, TestMap.Cloth, Cloth, HomeX + 1, HomeY + 1));
                    return ImmutableArray.Create(Packets.Draw(x, y, 0, 0), Packets.Status(Mana), tailor);
                }
                // "<name> sell": the shirts carried, up to what she buys before her restock, offered at ShirtPrice
                // (0x9E: vendor, count, then serial, art, hue, amount, price, name length and name each); none, her
                // refusal.
                if (p[0] == 0x03 && p.Length > 8
                    && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == VendorShard.Name + " sell")
                {
                    if (Shirts.Count == 0 || BuysLeft <= 0)
                        return ImmutableArray.Create<byte[]>(Packets.Speech(Vendor, VendorShard.Name,
                            "You have nothing I would be interested in.", 0));
                    int n = Math.Min(Shirts.Count, BuysLeft);
                    uint vs = Vendor;
                    List<byte> b = new() { 0x9E, 0, 0, (byte)(vs >> 24), (byte)(vs >> 16), (byte)(vs >> 8), (byte)vs,
                        (byte)(n >> 8), (byte)n };
                    byte[] name = Encoding.ASCII.GetBytes("fancy shirt");
                    for (int i = 0; i < n; i++)
                    {
                        uint sv = Shirts[i];
                        b.AddRange(new byte[] { (byte)(sv >> 24), (byte)(sv >> 16), (byte)(sv >> 8), (byte)sv, 0x1E,
                            0xFD, 0, 0, 0, 1, (byte)(ShirtPrice >> 8), (byte)ShirtPrice, 0, (byte)name.Length });
                        b.AddRange(name);
                    }
                    b[1] = (byte)(b.Count >> 8);
                    b[2] = (byte)b.Count;
                    return ImmutableArray.Create<byte[]>(b.ToArray());
                }
                // 0x9F, the sale: the shirts named go, paid ShirtPrice each.
                if (p[0] == 0x9F && p.Length >= 9 && Tailor is not null)
                {
                    ImmutableArray<byte[]>.Builder r = ImmutableArray.CreateBuilder<byte[]>();
                    int paid = 0;
                    for (int at = 9; at + 6 <= p.Length && BuysLeft > 0; at += 6)
                    {
                        uint sv = U32(p, at);
                        if (!Shirts.Remove(sv))
                            continue;
                        BuysLeft--;
                        Sold++;
                        paid += ShirtPrice;
                        r.Add(Packets.Delete(sv));
                    }
                    if (paid > 0)
                        r.Add(Tailor.Paid(paid));
                    return r.ToImmutable();
                }
                // A lift taken silently; its drop on the ground lies there, onto a stack of the same art there joins
                // it.
                if (p[0] == 0x07 && p.Length >= 7)
                {
                    m_lifted = U32(p, 1);
                    m_liftAmount = p[5] << 8 | p[6];
                    return ImmutableArray<byte[]>.Empty;
                }
                if (p[0] == 0x08 && p.Length >= 14 && U32(p, 1) == m_lifted)
                {
                    uint into = U32(p, 10);
                    int x = p[5] << 8 | p[6], y = p[7] << 8 | p[8];
                    if (into != 0xFFFFFFFF && !m_ground.ContainsKey(into))
                        return ImmutableArray<byte[]>.Empty;
                    if (m_ground.TryGet(into, out int[]? onto))
                    {
                        onto[0] += Math.Max(1, m_liftAmount);
                        m_lifted = 0;
                        return ImmutableArray.Create(Packets.Delete(U32(p, 1)),
                            Lying(into, 0x0FA0, onto[0], onto[1], onto[2]));
                    }
                    m_ground = m_ground.Set(m_lifted, new[] { Math.Max(1, m_liftAmount), x, y });
                    byte[] lying = Lying(m_lifted, 0x0FA0, Math.Max(1, m_liftAmount), x, y);
                    m_lifted = 0;
                    return ImmutableArray.Create<byte[]>(lying);
                }
                return ImmutableArray<byte[]>.Empty;
            }
        }
    }

    // 0x1A: an item lying on the ground (amount follows the top bit), 16 bytes.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint t = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    // The run's offer, trade, attempt and circuit-vendor records emptied, in the files and in the features' memory.
    internal static void ClearRecords(AssistFeatures f)
    {
        foreach (string name in new[] { "offers", "attempts", "circuit", "restocks", "underfloor" })
        {
            try
            {
                File.Delete(f.ServerFile(name));
            }
            catch (IOException)
            {
            }
        }

        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        // By name, so a field renamed or retyped throws here rather than leaving an earlier case's records in place.
        foreach (string field in new[] { "m_sellOffers", "m_circuitVendors", "m_underFloor" })
        {
            FieldInfo records = typeof(AssistFeatures).GetField(field, flags)!;
            records.SetValue(f, records.FieldType.GetProperty("Empty")!.GetValue(null));
        }

        foreach (string loaded in new[] { "m_sellOffersLoaded", "m_circuitLoaded", "m_underFloorLoaded" })
            typeof(AssistFeatures).GetField(loaded, flags)!.SetValue(f, false);
    }

    static int Said(FakeShard s, string text)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x03 && p.Length > 8 && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == text)
                n++;
        }

        return n;
    }

    // Kwik inside the house beside the empty pile spot, no thread, no kit, no shirts, 500 gold, the recall reagents;
    // the tailor in Shoptown with 20 thread and 10 kits.
    // regs: each recall reagent carried; blockShop: the shop's rune lands blocked; maxCasts: the recalls allowed in
    // three hours; wantHome: thread must end on the pile.
    static void Run(int regs, bool blockShop, int maxCasts, bool wantHome, int shirtsAtStart = 0,
        bool restocks = false, int clothAtStart = 0, int kitsAtStart = 0, int stallSews = 0)
    {
        int sewn = 0, overflows = 0, usesOnStall = 0;
        DateTime stalledAt = DateTime.MinValue;
        string asksAt = "";
        string broken = "", castLog = "";
        int soldAll = 0;
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-homespunworld-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 0 0 120 120 -128 127 0 CITY_HOMETOWN a b c d e Hometown",
            "R 130 130 120 120 -128 127 0 CITY_SHOPTOWN a b c d e Shoptown",
            "R 190 190 7 7 -128 127 0 CLOTHIER_SHOPTOWN a b c d e The Stitch",
        });
        TestMap map = new TestMap().MultiHouse(0x40, 4);
        string said = "";
        int casts = 0, asks = 0, sells = 0, home = 0, stalls = 0, errors = 0, tops = 0;
        bool running = false;
        try
        {
            map.Run(() => RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                // The offer and trade records are the run's, and every case starts its virtual clock at the same hour:
                // an earlier case's refusals would read as this case's of a moment ago.
                ClearRecords(f);
                f.Homespun = true;
                f.HouseLocked = false;
                f.Cfg.Set(f.Server + "runes.shop", Bag.ToString("X"));
                f.Cfg.Set(f.Server + "overflow.rune", HouseRune.ToString("X"));
                f.Cfg.Set(f.Server + "overflow.pile", HomeX + "," + HomeY + ",0");
                StepShard step = new(s, map, HomeX, HomeY - 1);
                World world = new(s, step);
                s.Answer = world.Answer;
                UoMap.PutMulti(House, 0x40, HomeX, HomeY, 0);
                step.Door(Door, TestMap.DoorShut, HomeX, HomeY + 4, true);
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
                s.ServerSends(Packets.Contained(ShopRune, 0x1F14, 1, Bag));
                s.ServerSends(Packets.Speech(ShopRune, "", "The Stitch", 6));
                s.ServerSends(Packets.Contained(HouseRune, 0x1F14, 1, Pack));
                s.ServerSends(Packets.Speech(HouseRune, "", "Home", 6));
                s.ServerSends(Packets.Contained(0x402A0040, 0x0F86, regs, Pack));
                s.ServerSends(Packets.Contained(0x402A0041, 0x0F7B, regs, Pack));
                s.ServerSends(Packets.Contained(0x402A0042, 0x0F7A, regs, Pack));
                world.BlockShop = blockShop;
                world.StallSews = stallSews;
                world.BuysLeft = World.BuysPerRestock;
                for (uint i = 0; i < shirtsAtStart; i++)
                {
                    s.ServerSends(Packets.Contained(0x402A1000 + i, 0x1EFD, 1, Pack));
                    world.Shirts.Add(0x402A1000 + i);
                }
                for (uint i = 0; i < kitsAtStart; i++)
                {
                    s.ServerSends(Packets.Contained(0x402A0070 + i, AssistFeatures.KitArt, 1, Pack));
                    world.Kits = world.Kits.Set(0x402A0070 + i, World.KitUses);
                }
                if (clothAtStart > 0)
                {
                    world.Cloth = clothAtStart;
                    s.ServerSends(Lying(World.ClothPile, TestMap.Cloth, clothAtStart, HomeX + 1, HomeY + 1));
                }
                // Bolts beside the cloth, the next thing a kit could be aimed at after the stall (the live floor's).
                if (stallSews > 0)
                    s.ServerSends(Lying(0x402A0061, 0x0F95, 10, HomeX + 1, HomeY + 1));
                s.ServerSends(Packets.Status(MaxMana));
                s.Clock.Sleep(1);
                VendorShard tailor = new(s, Vendor, Box, ShopX, ShopY, Pack, Gold, 500);
                tailor.Stock(0x402A0050, 0x0FA0, 20, 3, "Spool of thread");
                tailor.Stock(0x402A0051, AssistFeatures.KitArt, 10, 3, "Sewing kit");
                // She sells fancy shirts too, as every 1998 tailor does: her list names the item, so she deals in it.
                tailor.Stock(0x402A0052, 0x1EFD, 10, 40, "Fancy shirt");
                s.ServerSends(Packets.Speech(Vendor, VendorShard.Name, VendorShard.Name + " the tailor", 6));
                world.Tailor = tailor;
                s.Answer = world.Answer;
                world.Tick();
                if (restocks)
                    world.Restocks();
                s.Clock.Sleep(1);
                Check.Null(f.CircuitGo(), "CircuitGo");
                for (int i = 0; i < 3 * 60 * 60 && f.CircuitRunning; i++)
                    s.Clock.Sleep(1000);
                running = f.CircuitRunning;
                lock (f.Notes)
                {
                    said = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Circuit:") || n.Contains("Walk:")));
                    stalls = f.Notes.FindAll(n => n.Contains("no progress in")).Count;
                    errors = f.Notes.FindAll(n => n.Contains("an error in the shop loop")
                        || n.Contains("an error at the top")).Count;
                    tops = f.Notes.FindAll(n => n.Contains("Circuit: the top of the circuit")).Count;
                }
                // From the client's journal: the notes keep only the latest.
                foreach (byte[] p in s.ClientGot)
                {
                    if (p[0] == 0x1C && Encoding.ASCII.GetString(p).Contains("THREAD OVERFLOW"))
                        overflows++;
                }
                casts = world.Casts;
                castLog = string.Join(", ", world.CastLog);
                soldAll = world.Sold;
                sewn = world.Sewn;
                ImmutableArray<byte[]> got = s.ServerGot;
                ImmutableArray<DateTime> at = s.ServerGotAt;
                stalledAt = world.StalledAt;
                for (int gi = 0; gi < got.Length; gi++)
                {
                    // A kit used again within the 10 s after the unanswered use.
                    if (stalledAt != DateTime.MinValue && got[gi][0] == 0x06 && got[gi].Length >= 5
                        && at[gi] > stalledAt && at[gi] <= stalledAt.AddSeconds(10))
                    {
                        uint used = (uint)(got[gi][1] << 24 | got[gi][2] << 16 | got[gi][3] << 8 | got[gi][4]);
                        if (world.Kits.ContainsKey(used) || used >= 0x402A0070 && used < 0x402A0070 + kitsAtStart)
                            usesOnStall++;
                    }
                    if (got[gi][0] == 0x03 && got[gi].Length > 8
                        && Encoding.ASCII.GetString(got[gi], 8,
                        got[gi].Length - 8).TrimEnd('\0').StartsWith(VendorShard.Name + " "))
                        asksAt += at[gi].ToLocalTime().ToString("HH:mm") + " "
                            + Encoding.ASCII.GetString(got[gi], 8,
                            got[gi].Length - 8).TrimEnd('\0').Substring(VendorShard.Name.Length + 1) + ", ";
                }

                broken = f.BrokenList();
                asks = Said(s, VendorShard.Name + " buy");
                sells = Said(s, VendorShard.Name + " sell");
                home = world.ThreadHome;
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
                f.Cfg.Set(f.Server + "runes.shop", "");
                f.Cfg.Set(f.Server + "overflow.rune", "");
                f.Cfg.Set(f.Server + "overflow.pile", "");
                f.Homespun = false;
            });
        }
        string tail = said.Length > 3000 ? said.Substring(said.Length - 3000) : said;
        Check.True(running, "still running after three hours: " + tail);
        if (stallSews > 0)
        {
            Check.True(stalledAt != DateTime.MinValue, "a kit use went unanswered: " + tail);
            Check.Equal(0, usesOnStall, "the kit not used again on the unanswered use: " + tail);
            Check.Equal(0, overflows, "no THREAD OVERFLOW: the tailor buys every shirt: " + tail);
        }
        Check.Equal(0, errors, "no error: " + tail);
        Check.Equal(0, stalls, "no stalled lap: " + tail);
        // A BROKEN mark is a backoff that lasts a restock; the tailor is never one, and neither is the shop unless the
        // shop itself could not be reached (a blocked rune with no bank to fall back on). A reagent shortage is the
        // circuit's.
        Check.False(broken.Contains(Vendor.ToString("X")), "the tailor never BROKEN: [" + broken + "]");
        if (!blockShop)
            Check.False(broken.Contains("The Stitch"),
                "the shop never BROKEN by a reagent shortage or a dry home: [" + broken + "]");
        if (wantHome)
            Check.True(casts >= 2, "it went for thread and came home: " + casts + " casts: " + tail);
        // The tailor here never restocks, and the house has no loom: one trip for thread and home, then nothing to
        // recall for. The 175 of the first run (home, dry, the shop, nothing, home again every two minutes) and the 41
        // of the old top's home test both fail this.
        Check.True(casts <= maxCasts,
            "recalls bounded by the work (" + maxCasts + "): [" + castLog + "] " + casts + " casts, " + tops
            + " tops: " + tail);
        Check.True(asks <= 30, "the tailor asked a bounded number of times: " + asks + ": " + tail);
        if (wantHome)
            Check.True(home > 0, "thread brought home onto the pile: " + home + ": " + tail);
        if (clothAtStart == 0)
        {
            Check.Equal(shirtsAtStart, soldAll, "every shirt carried sold: [" + castLog + "] " + tail);
        }
        else
        {
            // 800 cloth is 100 shirts; the tailor buys 20 a restock, three restocks in three hours.
            Check.True(sewn >= 60, "sewn at home: " + sewn + " [" + castLog + "] " + tail);
            Check.True(soldAll >= 60, "sold: " + soldAll + " [" + castLog + "] asks {" + asksAt + "} " + tail);
            // Full, she is asked again at her next restock, not a fixed span on: the asks that sold nothing (5 shirts
            // an ask that sold) are the one that finds her full at each sale and a probe a restock at most, 7 in three
            // hours (19 asks at 60 sold).
            Check.True(sells - soldAll / 5 <= 7, "the full tailor asked again at her restock: " + sells + " sell asks, "
                + soldAll + " sold {" + asksAt + "}");
        }
    }
}
