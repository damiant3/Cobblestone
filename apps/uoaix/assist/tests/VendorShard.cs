// A uo1998 vendor for a FakeShard (docs/Designs/Active/Apps/AssistTesting.md, T3: the fake shard's vendors): in view
// wearing its stock box (layer 0x1A); "vendor buy" answered by the box's contents (0x3C), the buy list (0x74, in the
// reverse of the stock's order, OUO MakePacket_SHOP_DATA) and the buy window (0x24 gump 0x30); a buy (client 0x3B)
// answered by the vendor's total, the goods in the pack, the stock and the purse brought down, and the window's close.
#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;

namespace Uoaix.Assist.Tests;

public sealed class VendorShard
{
    sealed class Ware
    {
        public uint Serial;
        public int Graphic;
        public int Amount;
        public int Price;
        public string Name = "";
    }

    readonly Lock m_gate = new();
    readonly FakeShard m_shard;
    readonly uint m_vendor;
    readonly uint m_box;
    readonly uint m_pack;
    readonly uint m_gold;
    readonly List<Ware> m_stock = new();
    uint m_next = 0x45E00000;
    // The player's gold pile's amount, shared by every vendor of one world that hands in a purse.
    readonly int[] m_purse;

    int m_goldAmount
    {
        get => m_purse[0];
        set => m_purse[0] = value;
    }

    // The vendor at x, y wearing its stock box; the player's pack and its one gold pile. called: its name, Name unless
    // given. purse: the gold pile's amount shared with the world's other vendors (one pile, one count); goldAmount sets
    // it when none is given.
    public VendorShard(FakeShard shard, uint vendor, uint box, int x, int y, uint pack, uint gold, int goldAmount,
        string called = Name, int[]? purse = null)
    {
        Called = called;
        m_purse = purse ?? new[] { goldAmount };
        m_shard = shard;
        m_vendor = vendor;
        m_box = box;
        m_pack = pack;
        m_gold = gold;
        // Its label first, as a click names it, so the atlas has its name when it is drawn.
        shard.ServerSends(Packets.Speech(vendor, Called, Called, 6));
        shard.ServerSends(Incoming(vendor, x, y, box));
        shard.ServerSends(Packets.Contained(gold, AssistFeatures.GoldArt, m_goldAmount, pack));
        shard.Answer = Answer;
        shard.Clock.Sleep(1);
    }

    // Its name: it answers "<name> buy" as it answers "vendor buy".
    public const string Name = "Morena";

    // This vendor's name.
    public string Called
    {
        get;
    }

    // When set, the vendor answers "vendor buy" with nothing.
    public bool Silent
    {
        get; set;
    }

    // When set, the first ask is answered "Let us go back to my shop." (uo1998, a vendor asked away from its shop), and
    // the vendor is drawn at its home 1000 ms later.
    public int[]? GoesHomeTo
    {
        get; set;
    }

    // The price each unit is charged, when it differs from the list's.
    public int ChargeEach
    {
        get; set;
    }

    public int Buys
    {
        get; private set;
    }

    // Every item its sales put in the pack: serial and art each.
    public readonly List<uint[]> Created = new();

    public void Stock(uint serial, int graphic, int amount, int price, string name)
    {
        lock (m_gate)
            m_stock.Add(new Ware { Serial = serial, Graphic = graphic, Amount = amount, Price = price, Name = name });
    }

    // A ware's amount set back to amount (a restock), its box's contents told as uo1998 re-creates it.
    public byte[] Restock(uint serial, int amount)
    {
        lock (m_gate)
        {
            Ware? w = m_stock.Find(x => x.Serial == serial);
            if (w is null)
                return Packets.Delete(serial);
            w.Amount = amount;
            return Packets.Contained(w.Serial, w.Graphic, w.Amount, m_box);
        }
    }

    // The player paid gold (a sale to the vendor): the pack's pile grown, its packet.
    public byte[] Paid(int gold)
    {
        lock (m_gate)
        {
            m_goldAmount += gold;
            return Packets.Contained(m_gold, AssistFeatures.GoldArt, m_goldAmount, m_pack);
        }
    }

    public int Gold
    {
        get
        {
            lock (m_gate)
                return m_goldAmount;
        }
    }

    static void Put32(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    static void Put16(List<byte> b, int v)
    {
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    static byte[] Framed(List<byte> b)
    {
        b[1] = (byte)(b.Count >> 8);
        b[2] = (byte)b.Count;
        return b.ToArray();
    }

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    // 0x78: the vendor drawn, wearing box at layer 0x1A.
    static byte[] Incoming(uint vendor, int x, int y, uint box)
    {
        List<byte> b = new() { 0x78, 0, 0 };
        Put32(b, vendor);
        Put16(b, 0x190);
        Put16(b, x);
        Put16(b, y);
        b.Add(0);
        b.Add(2);
        Put16(b, 0);
        b.Add(0);
        b.Add(1);
        Put32(b, box);
        Put16(b, 0x0E75);
        b.Add(0x1A);
        Put32(b, 0);
        return Framed(b);
    }

    // The caller holds m_gate.
    byte[] Contents()
    {
        // A sold-out ware is no longer in the box (uo1998 sends 0 for a pile of one, so a 0 would read as one left).
        List<Ware> left = m_stock.FindAll(w => w.Amount > 0);
        List<byte> b = new() { 0x3C, 0, 0 };
        Put16(b, left.Count);
        foreach (Ware w in left)
        {
            Put32(b, w.Serial);
            Put16(b, w.Graphic);
            b.Add(0);
            Put16(b, w.Amount);
            Put16(b, 44);
            Put16(b, 65);
            Put32(b, m_box);
            Put16(b, 0);
        }
        return Framed(b);
    }

    // 0x74: the box, the count, then price (u32), name length and name per ware, in the reverse of the stock's order.
    // The caller holds m_gate.
    byte[] BuyList()
    {
        List<Ware> left = m_stock.FindAll(w => w.Amount > 0);
        List<byte> b = new() { 0x74, 0, 0 };
        Put32(b, m_box);
        b.Add((byte)left.Count);
        for (int i = left.Count - 1; i >= 0; i--)
        {
            Put32(b, (uint)left[i].Price);
            byte[] name = Encoding.ASCII.GetBytes(left[i].Name);
            b.Add((byte)name.Length);
            b.AddRange(name);
        }
        return Framed(b);
    }

    // The vendor drawn with its stock and buy list, as an earlier "buy" left them in the atlas.
    public ImmutableArray<byte[]> Known(int x, int y)
    {
        lock (m_gate)
            return ImmutableArray.Create(Incoming(m_vendor, x, y, m_box), Contents(), BuyList());
    }

    // Its replies to one client packet, empty for one not its own: chained with another shard's where both run.
    public ImmutableArray<byte[]> Answer(byte[] p)
    {
        lock (m_gate)
        {
            string said = p[0] == 0x03 && p.Length > 8 ? Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0')
                : "";
            if (said == "vendor buy" || said == Called + " buy")
            {
                if (Silent)
                    return ImmutableArray<byte[]>.Empty;
                if (GoesHomeTo is int[] home)
                {
                    GoesHomeTo = null;
                    m_shard.Also(1000, () => m_shard.ServerSends(Packets.Mobile(m_vendor, home[0], home[1])));
                    return ImmutableArray.Create<byte[]>(Packets.Speech(m_vendor, Called, "Let us go back to my shop.",
                        0));
                }
                byte[] window = { 0x24, (byte)(m_box >> 24), (byte)(m_box >> 16), (byte)(m_box >> 8), (byte)m_box,
                    0x00, 0x30 };
                return ImmutableArray.Create(Contents(), BuyList(), window);
            }
            if (p[0] == 0x3B && p.Length >= 8)
                return Bought(p);
            return ImmutableArray<byte[]>.Empty;
        }
    }

    // The caller holds m_gate.
    ImmutableArray<byte[]> Bought(byte[] p)
    {
        Buys++;
        ImmutableArray<byte[]>.Builder replies = ImmutableArray.CreateBuilder<byte[]>();
        List<byte[]> goods = new();
        int total = 0;
        for (int at = 8; at + 7 <= p.Length; at += 7)
        {
            uint serial = U32(p, at + 1);
            int amount = p[at + 5] << 8 | p[at + 6];
            Ware? w = m_stock.Find(x => x.Serial == serial);
            if (w is null || amount <= 0 || amount > w.Amount)
                continue;
            total += amount * (ChargeEach > 0 ? ChargeEach : w.Price);
            w.Amount -= amount;
            Created.Add(new[] { m_next, (uint)w.Graphic });
            goods.Add(Packets.Contained(m_next++, w.Graphic, amount, m_pack));
            goods.Add(w.Amount > 0 ? Packets.Contained(w.Serial, w.Graphic, w.Amount, m_box)
                : Packets.Delete(w.Serial));
        }
        replies.Add(Packets.Speech(m_vendor, Called, "The total of thy purchase is " + total + " gold.", 0));
        m_goldAmount -= total;
        replies.Add(Packets.Contained(m_gold, AssistFeatures.GoldArt, m_goldAmount, m_pack));
        replies.AddRange(goods);
        replies.Add(new byte[] { 0x3B, 0x00, 0x08, (byte)(m_vendor >> 24), (byte)(m_vendor >> 16),
            (byte)(m_vendor >> 8), (byte)m_vendor, 0x00 });
        return replies.ToImmutable();
    }
}
