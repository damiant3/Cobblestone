// A uo1998 bank for a FakeShard (docs/Designs/Active/Apps/AssistTesting.md, T3: the fake shard's bank box): "bank"
// opens the box with its contents; a bag in it opens to its own; a lift is taken silently, and its drop puts the amount
// lifted where it was dropped, a part lifted leaving the rest in place under a new serial, as the server splits a
// stack.
#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;

namespace Uoaix.Assist.Tests;

public sealed class BankShard
{
    sealed class Held
    {
        public int Graphic;
        public int Amount;
        public uint Container;
    }

    readonly Lock m_gate = new();
    readonly FakeShard m_shard;
    readonly uint m_box;
    Map<uint, Held> m_items = Map<uint, Held>.Empty;
    uint m_lifted;
    int m_liftAmount;
    uint m_nextSerial = 0x45F00000;

    public BankShard(FakeShard shard, uint box)
    {
        m_shard = shard;
        m_box = box;
        shard.Answer = Answer;
    }

    public int Lifts
    {
        get; private set;
    }

    Set<uint> m_opened = Set<uint>.Empty;

    // Whether the bag was opened by a double-click.
    public bool Opened(uint bag)
    {
        lock (m_gate)
            return m_opened.Contains(bag);
    }

    // An item in the box, or in a bag of it (container), the bags included.
    public void Put(uint serial, int graphic, int amount, uint container)
    {
        lock (m_gate)
            m_items = m_items.Set(serial, new Held { Graphic = graphic, Amount = amount, Container = container });
    }

    // How much of graphic lies directly in container.
    public int In(uint container, int graphic)
    {
        int n = 0;
        lock (m_gate)
        {
            foreach (KeyValuePair<uint, Held> kv in m_items)
            {
                if (kv.Value.Container == container && kv.Value.Graphic == graphic)
                    n += kv.Value.Amount;
            }
        }
        return n;
    }

    static void Put32(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    static void Put16(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 8);
        b[at + 1] = (byte)v;
    }

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    // 0x3C: what lies directly in container, 19 bytes an item. The caller holds m_gate.
    byte[] ContentsOf(uint container)
    {
        List<KeyValuePair<uint, Held>> inside = new();
        foreach (KeyValuePair<uint, Held> kv in m_items)
        {
            if (kv.Value.Container == container)
                inside.Add(kv);
        }
        byte[] p = new byte[5 + 19 * inside.Count];
        p[0] = 0x3C;
        Put16(p, 1, p.Length);
        Put16(p, 3, inside.Count);
        for (int i = 0; i < inside.Count; i++)
        {
            int at = 5 + 19 * i;
            Put32(p, at, inside[i].Key);
            Put16(p, at + 4, inside[i].Value.Graphic);
            Put16(p, at + 7, inside[i].Value.Amount);
            Put16(p, at + 9, 44);
            Put16(p, at + 11, 65);
            Put32(p, at + 13, container);
        }
        return p;
    }

    // Its replies to one client packet, empty for one not its own: chained with another shard's where both run.
    public ImmutableArray<byte[]> Answer(byte[] p)
    {
        lock (m_gate)
        {
            if (p[0] == 0x03 && p.Length > 8 && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == "bank")
                return ImmutableArray.Create(Packets.Open(m_box), ContentsOf(m_box));
            if (p[0] == 0x06 && m_items.TryGet(U32(p, 1), out Held? bag) && bag.Container != 0)
            {
                m_opened = m_opened.Add(U32(p, 1));
                return ImmutableArray.Create(Packets.Open(U32(p, 1)), ContentsOf(U32(p, 1)));
            }
            if (p[0] == 0x07)
            {
                Lifts++;
                m_lifted = U32(p, 1);
                m_liftAmount = p[5] << 8 | p[6];
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] == 0x08 && U32(p, 1) == m_lifted && m_items.TryGet(m_lifted, out Held? it))
                return Dropped(m_lifted, it, U32(p, 10));
            return ImmutableArray<byte[]>.Empty;
        }
    }

    // The caller holds m_gate.
    ImmutableArray<byte[]> Dropped(uint serial, Held it, uint into)
    {
        ImmutableArray<byte[]>.Builder replies = ImmutableArray.CreateBuilder<byte[]>();
        int amount = m_liftAmount <= 0 || m_liftAmount > it.Amount ? it.Amount : m_liftAmount;
        if (amount < it.Amount)
        {
            uint rest = m_nextSerial++;
            m_items = m_items.Set(rest,
                new Held { Graphic = it.Graphic, Amount = it.Amount - amount, Container = it.Container });
            replies.Add(Packets.Contained(rest, it.Graphic, it.Amount - amount, it.Container));
        }
        m_items = m_items.Set(serial, new Held { Graphic = it.Graphic, Amount = amount, Container = into });
        replies.Add(Packets.Contained(serial, it.Graphic, amount, into));
        m_lifted = 0;
        return replies.ToImmutable();
    }
}
