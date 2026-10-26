// What the player carries and stands by (docs/Designs/Active/Apps/AssistStructure.md, stage 2), read from what the
// server has sent: the pack's tree of containers, counts, weight and the carry limit.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public interface IInventory
{
    uint Pack
    {
        get;
    }
    Set<uint> PackTree();
    int Count(Func<int, bool> graphic);
    int ItemCount();
    AssistItem? Largest(Func<int, bool> graphic);
    AssistItem? Item(uint serial);
    AssistItem? ItemOrGround(uint serial);
    bool Within(uint serial, uint root);
    ImmutableArray<AssistItem> Inside(uint root, Func<int, bool> graphic);
    int Weight
    {
        get;
    }
    int CarryLimit
    {
        get;
    }
}

public sealed class Inventory : IInventory
{
    // A pack's bags are followed this many deep, and an item's chain of containers this many up.
    public const int PackDepth = 8, ChainDepth = 16;

    readonly AssistState m_state;

    public Inventory(AssistState state)
    {
        m_state = state;
    }

    public uint Pack
    {
        get
        {
            lock (m_state.Gate)
                return m_state.Backpack;
        }
    }

    // The pack and every container inside it.
    public Set<uint> PackTree()
    {
        ImmutableHashSet<uint>.Builder inside = ImmutableHashSet.CreateBuilder<uint>();
        lock (m_state.Gate)
        {
            if (m_state.Backpack != 0)
                inside.Add(m_state.Backpack);
            for (int pass = 0; pass < PackDepth; pass++)
            {
                foreach (KeyValuePair<uint, AssistItem> kv in m_state.Items)
                {
                    if (inside.Contains(kv.Value.Container))
                        inside.Add(kv.Key);
                }
            }
        }
        return Set<uint>.From(inside.ToImmutable());
    }

    // An item that does not stack is one, whatever amount the server's 0x3C gives it (uo1998 sent 176 for one fancy
    // shirt).
    public int Count(Func<int, bool> graphic)
    {
        int n = 0;
        lock (m_state.Gate)
        {
            Set<uint> inside = PackTree();
            foreach (AssistItem it in m_state.Items.Values)
            {
                if (inside.Contains(it.Container) && graphic(it.Graphic))
                    n += UoTiles.Stacks(it.Graphic) ? Math.Max(1, it.Amount) : 1;
            }
        }
        return n;
    }

    // The items the server counts against a pack's ItemsMax (OUO CContainer_CanHold, container.c:1063: the count of
    // every bag's contents, CContainer_CountItems): a spellbook or a key ring is one item and nothing inside it counts
    // (CItem_IsNonCountable, item.c:2980). Counting a 64-spell book's scrolls stopped the tailoring loop at "127 items"
    // in a pack the server had room in (Damian, 2026-10-24: "the tailor loop is wrong about the number of items in my
    // pack").
    public const int ItemsMax = 125;
    static readonly ImmutableArray<int> s_uncounted = ImmutableArray.Create(0x0EFA, 0x1011, 0x1769, 0x176A, 0x176B,
        0x2AF8);

    public int ItemCount()
    {
        int n = 0;
        lock (m_state.Gate)
        {
            if (m_state.Backpack == 0)
                return 0;
            ImmutableHashSet<uint>.Builder counted = ImmutableHashSet.CreateBuilder<uint>();
            ImmutableHashSet<uint>.Builder open = ImmutableHashSet.CreateBuilder<uint>();
            open.Add(m_state.Backpack);
            for (int pass = 0; pass < PackDepth; pass++)
            {
                foreach (KeyValuePair<uint, AssistItem> kv in m_state.Items)
                {
                    if (!open.Contains(kv.Value.Container) || !counted.Add(kv.Key))
                        continue;
                    n++;
                    if (!s_uncounted.Contains(kv.Value.Graphic))
                        open.Add(kv.Key);
                }
            }
        }
        return n;
    }

    public AssistItem? Largest(Func<int, bool> graphic)
    {
        AssistItem? best = null;
        lock (m_state.Gate)
        {
            Set<uint> inside = PackTree();
            foreach (AssistItem it in m_state.Items.Values)
            {
                if (inside.Contains(it.Container) && graphic(it.Graphic) && (best is null || it.Amount > best.Amount))
                    best = it;
            }
        }
        return best;
    }

    public AssistItem? Item(uint serial)
    {
        lock (m_state.Gate)
            return m_state.Items[serial];
    }

    public AssistItem? ItemOrGround(uint serial)
    {
        lock (m_state.Gate)
        {
            if (m_state.Items.TryGet(serial, out AssistItem? it) || m_state.Ground.TryGet(serial, out it))
                return it;
            return null;
        }
    }

    // Whether an item lies somewhere inside root, by its chain of containers.
    public bool Within(uint serial, uint root)
    {
        lock (m_state.Gate)
        {
            for (int i = 0; i < ChainDepth && m_state.Items.TryGet(serial, out AssistItem? it); i++)
            {
                if (it.Container == root)
                    return true;
                serial = it.Container;
            }
        }
        return false;
    }

    // The items of a kind anywhere inside root, as far as its contents have been sent.
    public ImmutableArray<AssistItem> Inside(uint root, Func<int, bool> graphic)
    {
        ImmutableArray<AssistItem>.Builder found = ImmutableArray.CreateBuilder<AssistItem>();
        lock (m_state.Gate)
        {
            foreach (AssistItem it in m_state.Items.Values)
            {
                if (graphic(it.Graphic) && Within(it.Serial, root))
                    found.Add(it);
            }
        }
        return found.ToImmutable();
    }

    public int Weight
    {
        get
        {
            lock (m_state.Gate)
                return m_state.Weight;
        }
    }

    // The 1998 carry limit, strength * 4 + 30; 0 until the status has come.
    public int CarryLimit
    {
        get
        {
            lock (m_state.Gate)
                return m_state.Str > 0 ? m_state.Str * 4 + 30 : 0;
        }
    }
}
