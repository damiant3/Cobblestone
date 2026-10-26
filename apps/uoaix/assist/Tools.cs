// A toolbox and a toolbag per profession (docs/Designs/Active/Apps/AssistTools.md). Damian, 2026-10-21: "we need a
// toolbag for each skill, at least for mining and for tailor. we need a shop toolbox and a bag toolbag"; "the idea of
// the box is it is the big store, the bag the ones you take on the road"; "they could be different for each
// profession, but could be shared. same technically with toolbox".
#nullable enable
using System;
using System.Collections.Immutable;
using System.Globalization;

namespace Uoaix.Assist;

// Road: how many of its tools the profession's bag carries.
public sealed record Profession(string Key, string Name, string ToolsName, Func<int, bool> IsTool, int Road);

public interface ITools
{
    ImmutableArray<Profession> All
    {
        get;
    }
    Profession? Of(string key);
    uint Box(Profession p);
    uint Bag(Profession p);
    void SetBox(Profession p, uint serial);
    void SetBag(Profession p, uint serial);
    int Road(Profession p);
    void SetRoad(Profession p, int n);
    uint Home(Profession p);
    bool BagCarried(Profession p);
    bool BoxInReach(Profession p);
    int InBag(Profession p);
    ImmutableArray<AssistItem> InBox(Profession p);
    uint Spare(Profession p);
}

public sealed class ToolPlaces : ITools
{
    public const int RoadMax = 100;

    readonly ImmutableArray<Profession> m_all;
    readonly AssistSettings m_cfg;
    readonly Func<string> m_server;
    readonly Func<IInventory?> m_inventory;
    readonly Func<uint, bool> m_reach;

    // reach: whether a container can be used where the player stands.
    public ToolPlaces(ImmutableArray<Profession> all, AssistSettings cfg, Func<string> server,
        Func<IInventory?> inventory, Func<uint, bool> reach)
    {
        m_all = all;
        m_cfg = cfg;
        m_server = server;
        m_inventory = inventory;
        m_reach = reach;
    }

    public ImmutableArray<Profession> All => m_all;

    public static string BoxKey(Profession p) => "tools." + p.Key + ".box";

    public static string BagKey(Profession p) => "tools." + p.Key + ".bag";

    public Profession? Of(string key)
    {
        foreach (Profession p in m_all)
        {
            if (p.Key == key)
                return p;
        }
        return null;
    }

    public uint Box(Profession p)
    {
        return Serial(m_cfg.Get(m_server() + BoxKey(p), ""));
    }

    // A profession with no bag of its own uses the one tool bag every circuit shared before (<server>packtoolbag, the
    // mining circuit's mine.toolbag before it).
    public uint Bag(Profession p)
    {
        string server = m_server();
        string v = m_cfg.Get(server + BagKey(p), "");
        if (v.Length == 0)
            v = m_cfg.Get(server + "packtoolbag", "");
        if (v.Length == 0)
            v = m_cfg.Get(server + "mine.toolbag", "");
        return Serial(v);
    }

    public void SetBox(Profession p, uint serial)
    {
        m_cfg.Set(m_server() + BoxKey(p), serial == 0 ? "" : serial.ToString("X"));
    }

    public void SetBag(Profession p, uint serial)
    {
        m_cfg.Set(m_server() + BagKey(p), serial == 0 ? "" : serial.ToString("X"));
    }

    public int Road(Profession p)
    {
        return Math.Clamp(m_cfg.GetInt(m_server() + "tools." + p.Key + ".road", p.Road), 0, RoadMax);
    }

    public void SetRoad(Profession p, int n)
    {
        m_cfg.Set(m_server() + "tools." + p.Key + ".road", Math.Clamp(n, 0, RoadMax).ToString());
    }

    // Where a tool of the profession goes: its bag while the bag is in the pack, else the pack.
    public uint Home(Profession p)
    {
        IInventory? inv = m_inventory();
        if (inv is null)
            return 0;
        return BagCarried(p) ? Bag(p) : inv.Pack;
    }

    public bool BagCarried(Profession p)
    {
        IInventory? inv = m_inventory();
        uint bag = Bag(p);
        return inv is not null && bag != 0 && inv.PackTree().Contains(bag);
    }

    public bool BoxInReach(Profession p)
    {
        uint box = Box(p);
        return box != 0 && m_reach(box);
    }

    public int InBag(Profession p)
    {
        IInventory? inv = m_inventory();
        uint bag = Bag(p);
        if (inv is null || bag == 0)
            return 0;
        return inv.Inside(bag, p.IsTool).Length;
    }

    public ImmutableArray<AssistItem> InBox(Profession p)
    {
        IInventory? inv = m_inventory();
        uint box = Box(p);
        if (inv is null || box == 0)
            return [];
        return inv.Inside(box, p.IsTool);
    }

    // A tool to use: from the bag, else anywhere in the pack, else straight from the box when it lies within reach
    // (Damian, 2026-10-21: "if in the house, where the kit is needed, then you can use it directly instead of the
    // ceremony of putting into your bag first"); 0 for none.
    public uint Spare(Profession p)
    {
        IInventory? inv = m_inventory();
        if (inv is null)
            return 0;
        if (BagCarried(p))
        {
            ImmutableArray<AssistItem> bagged = inv.Inside(Bag(p), p.IsTool);
            if (bagged.Length > 0)
                return bagged[0].Serial;
        }
        uint pack = inv.Pack;
        if (pack != 0)
        {
            ImmutableArray<AssistItem> carried = inv.Inside(pack, p.IsTool);
            if (carried.Length > 0)
                return carried[0].Serial;
        }
        if (!BoxInReach(p))
            return 0;
        ImmutableArray<AssistItem> boxed = InBox(p);
        return boxed.Length > 0 ? boxed[0].Serial : 0;
    }

    static uint Serial(string v)
    {
        return uint.TryParse(v, NumberStyles.HexNumber, null, out uint s) ? s : 0;
    }
}
