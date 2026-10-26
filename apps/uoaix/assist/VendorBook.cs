// Other players' vendors (Damian, 2026-10-24: "Vendors are player controlled, not shopkeeps. they live on another
// player's house, cannot move. ... surfacing the inventory taken when I open a vendor backpack"): every vendor the
// catalogue read, with every item in its pack and bags, kept in vendors-<server>.txt beside the atlas.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

// One item as the vendor's pack showed it: its bag, the label its single click gave, the description and price of its
// Cost line ("Not for sale." for a key or a bag), and the name an identify gave a magic item.
public sealed record VendorItem(uint Serial, int Graphic, int Amount, uint Bag, string Name, string Description,
    string Price,
    string Identified)
{
    // uo1998 names an unidentified magic item "a magic <type>" (rundir identify.m Q4RB); identified, its label is the
    // magic item's own name.
    public bool Magic => (" " + Name + " ").Contains(" magic ", StringComparison.OrdinalIgnoreCase);

    public bool ForSale => int.TryParse(Price, out _) || Price.StartsWith("FREE", StringComparison.OrdinalIgnoreCase);
}

public sealed record VendorRecord(uint Serial, string Name, int X, int Y, string Wares, DateTime ReadAt,
    ImmutableArray<VendorItem> Items);

public sealed class VendorBook
{
    readonly string m_path;
    readonly IClock m_clock;
    readonly Lock m_gate = new();
    Map<uint, VendorRecord> m_vendors = Map<uint, VendorRecord>.Empty;
    long m_seq;

    public VendorBook(string path, IClock clock)
    {
        m_path = path;
        m_clock = clock;
        try
        {
            if (File.Exists(path))
                Load(File.ReadAllLines(path));
        }
        catch (IOException)
        {
        }
    }

    // Counts every change, for the Vendors tab to follow.
    public long Seq => Interlocked.Read(ref m_seq);

    public DateTime Now => m_clock.UtcNow.ToLocalTime();

    public VendorRecord? Get(uint vendor)
    {
        lock (m_gate)
            return m_vendors[vendor];
    }

    public ImmutableArray<VendorRecord> All()
    {
        ImmutableArray<VendorRecord>.Builder all = ImmutableArray.CreateBuilder<VendorRecord>();
        lock (m_gate)
        {
            foreach (KeyValuePair<uint, VendorRecord> kv in m_vendors)
                all.Add(kv.Value);
        }
        return all.ToImmutable();
    }

    // A read in place of the last; a name an earlier identify gave an item still in the pack is kept, so no charge is
    // spent on it again.
    public VendorRecord Put(VendorRecord read)
    {
        VendorRecord kept;
        lock (m_gate)
        {
            VendorRecord? was = m_vendors[read.Serial];
            ImmutableArray<VendorItem> items = read.Items;
            if (was is not null)
            {
                ImmutableArray<VendorItem>.Builder carried = ImmutableArray.CreateBuilder<VendorItem>(items.Length);
                foreach (VendorItem it in items)
                {
                    VendorItem? old = Find(was, it.Serial);
                    carried.Add(it.Identified.Length == 0 && old is not null && old.Identified.Length > 0
                        ? it with
                        {
                            Identified = old.Identified
                        }
                        : it);
                }
                items = carried.MoveToImmutable();
            }
            kept = read with
            {
                Items = items
            };
            m_vendors = m_vendors.Set(read.Serial, kept);
        }
        Changed();
        return kept;
    }

    // An identify's name for one item of a vendor.
    public void Identified(uint vendor, uint item, string name)
    {
        lock (m_gate)
        {
            VendorRecord? was = m_vendors[vendor];
            if (was is null)
                return;
            ImmutableArray<VendorItem>.Builder items = ImmutableArray.CreateBuilder<VendorItem>(was.Items.Length);
            foreach (VendorItem it in was.Items)
                items.Add(it.Serial == item ? it with
                {
                    Identified = name
                } : it);
            m_vendors = m_vendors.Set(vendor, was with
            {
                Items = items.MoveToImmutable()
            });
        }
        Changed();
    }

    public static VendorItem? Find(VendorRecord vendor, uint item)
    {
        foreach (VendorItem it in vendor.Items)
        {
            if (it.Serial == item)
                return it;
        }
        return null;
    }

    void Changed()
    {
        Interlocked.Increment(ref m_seq);
        Save();
    }

    // Tab-separated: "vendor", serial, name, x, y, wares, ticks; then "item", vendor, serial, graphic, amount, bag,
    // name, description, price, identified.
    void Load(string[] lines)
    {
        Map<uint, VendorRecord> vendors = Map<uint, VendorRecord>.Empty;
        List<string[]> items = new();
        foreach (string line in lines)
        {
            string[] t = line.Split('\t');
            if (t[0] == "vendor" && t.Length >= 7 && TryHex(t[1], out uint s) && int.TryParse(t[3], out int x)
                && int.TryParse(t[4], out int y) && long.TryParse(t[6], out long ticks))
            {
                vendors = vendors.Set(s, new VendorRecord(s, t[2], x, y, t[5], new DateTime(ticks), []));
            }
            else if (t[0] == "item" && t.Length >= 10)
            {
                items.Add(t);
            }
        }
        foreach (string[] t in items)
        {
            if (!TryHex(t[1], out uint v) || !vendors.TryGet(v, out VendorRecord? r) || !TryHex(t[2], out uint s)
                || !int.TryParse(t[3], out int g) || !int.TryParse(t[4], out int a) || !TryHex(t[5], out uint bag))
            {
                continue;
            }
            vendors = vendors.Set(v, r with
            {
                Items = r.Items.Add(new VendorItem(s, g, a, bag, t[6], t[7], t[8], t[9]))
            });
        }
        m_vendors = vendors;
    }

    void Save()
    {
        List<string> lines = new();
        lock (m_gate)
        {
            foreach (KeyValuePair<uint, VendorRecord> kv in m_vendors)
            {
                VendorRecord r = kv.Value;
                lines.Add(string.Join("\t", "vendor", r.Serial.ToString("X"), Clean(r.Name), r.X.ToString(),
                    r.Y.ToString(),
                    Clean(r.Wares), r.ReadAt.Ticks.ToString()));
                foreach (VendorItem it in r.Items)
                {
                    lines.Add(string.Join("\t", "item", r.Serial.ToString("X"), it.Serial.ToString("X"),
                        it.Graphic.ToString(),
                        it.Amount.ToString(), it.Bag.ToString("X"), Clean(it.Name), Clean(it.Description),
                        Clean(it.Price),
                        Clean(it.Identified)));
                }
            }
        }
        try
        {
            string? dir = Path.GetDirectoryName(m_path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            File.WriteAllLines(m_path, lines);
        }
        catch (IOException)
        {
        }
    }

    static bool TryHex(string text, out uint value) =>
        uint.TryParse(text, NumberStyles.HexNumber, null, out value);

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
