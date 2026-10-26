// What every gathering circuit shares (docs/Designs/Done/Apps/AssistHarvest.md, stage 1): the place the goods are
// carried to, At my feet, the total that makes the collection due, and the record of the piles waiting for it. A
// subclass names its settings and the art it gathers.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Uoaix.Assist;

// A pile waiting for the collection: its tile, how many it holds, and wild (found lying, not dropped by us).
public sealed record GatherPile(int X, int Y, int Amount, bool Wild);

public abstract class Gather
{
    readonly AssistSettings m_cfg;
    readonly Func<string> m_server;

    protected Gather(AssistSettings cfg, Func<string> server)
    {
        m_cfg = cfg;
        m_server = server;
    }

    // The settings' prefix: <server><Key>.piles, .feet, .collect-at and .<PlaceName>.
    public abstract string Key
    {
        get;
    }

    // The setting holding the place, and the setup button that targets it, as the answers name it.
    public abstract string PlaceName
    {
        get;
    }
    public abstract string PlaceButton
    {
        get;
    }

    public abstract int CollectAtDefault
    {
        get;
    }

    // The art this gathering drops at feet and collects, and the art whose TILEDATA weight prices a carried pile's
    // unit.
    public abstract bool Gathered(int graphic);
    public abstract int WeightArt
    {
        get;
    }

    // A pile that fits in the pack is carried to its parent rather than hopped (CarryPile); false hops every pile.
    public virtual bool Carries => true;

    // How the notes name the gathering ("Lumber") and its goods ("logs").
    public abstract string Title
    {
        get;
    }
    public abstract string Goods
    {
        get;
    }

    // How many the current run has put down; the run sets it to 0 at its start.
    public int Stored
    {
        get; set;
    }

    // The tool by its name ("axe", the setting's and the answers'), and the art that is one.
    public abstract string ToolName
    {
        get;
    }
    public abstract bool IsTool(int graphic);

    // The tool set by its target, by serial; 0 unset.
    public uint Tool
    {
        get => uint.TryParse(m_cfg.Get(Setting(ToolName), ""), NumberStyles.HexNumber, null, out uint v) ? v : 0;
        set => m_cfg.Set(Setting(ToolName), value == 0 ? "" : value.ToString("X"));
    }

    string Setting(string name) => m_server() + Key + "." + name;

    // Where the goods go: "ground|0|x|y|z", "item|serial|x|y|z" (a bag, a pile, anything they drop on) or
    // "mobile|serial"; "feet|0" from before At my feet was a setting of its own.
    public string PlaceSpec
    {
        get => m_cfg.Get(Setting(PlaceName), "");
        set => m_cfg.Set(Setting(PlaceName), value ?? "");
    }

    // At my feet: each full pack goes on a pile beside the player, and the place is where the collection carries them.
    public bool AtFeet
    {
        get => m_cfg.Get(Setting("feet"), "off") == "on" || PlaceSpec.StartsWith("feet", StringComparison.Ordinal);
        set
        {
            m_cfg.Set(Setting("feet"), value ? "on" : "off");
            if (!value && PlaceSpec.StartsWith("feet", StringComparison.Ordinal))
                PlaceSpec = "";
        }
    }

    public int CollectAt
    {
        get => Math.Max(1, m_cfg.GetInt(Setting("collect-at"), CollectAtDefault));
        set => m_cfg.Set(Setting("collect-at"), Math.Max(1, value).ToString());
    }

    // At my feet, with the goods dropped reaching Collect at: the collection runs before the gathering goes on.
    public bool CollectDue => AtFeet && Dropped() >= CollectAt;

    // A new start's own part: every pile forgotten (piles and goods: how many were), and with a tile the place is it,
    // At my feet kept (an old "feet|0" place is At my feet until the place is set).
    public void NewStart(int[]? tile, out int piles, out int goods)
    {
        ImmutableArray<GatherPile> was = Piles();
        piles = was.Length;
        goods = 0;
        foreach (GatherPile p in was)
            goods += p.Amount;
        ClearPiles();
        if (tile is null)
            return;
        bool feet = AtFeet;
        PlaceSpec = "ground|0|" + tile[0] + "|" + tile[1] + "|" + tile[2];
        if (feet)
            AtFeet = true;
    }

    // Where the collection carries the piles, from the place: a tile, or an item with its tile (onto, the item); a pack
    // animal moves and is no place for it. itemTile: an item's tile where it lies on the ground, else null. Null, or
    // why there is none.
    public string? CollectPlace(Func<uint, int[]?> itemTile, out int x, out int y, out int z, out uint onto)
    {
        x = y = z = 0;
        onto = 0;
        string[] t = PlaceSpec.Split('|');
        if (t.Length < 2 || t[0] == "feet")
            return "no place for the collection: target one with " + PlaceButton;
        if (t[0] == "mobile")
            return "a pack animal moves: target a tile, a bag or a pile with " + PlaceButton + " for the collection";
        if (t.Length < 5)
            return "the place for the collection is not readable: target it again with " + PlaceButton;
        x = int.Parse(t[2]);
        y = int.Parse(t[3]);
        z = int.Parse(t[4]);
        if (t[0] == "item")
        {
            onto = Convert.ToUInt32(t[1], 16);
            int[]? at = itemTile(onto);
            if (at is not null)
            {
                x = at[0];
                y = at[1];
            }
        }
        return null;
    }

    // <Key>.piles: "x,y,amount" separated by ";", ",1" after a wild pile's; an entry not of that shape is skipped.
    public ImmutableArray<GatherPile> Piles()
    {
        ImmutableArray<GatherPile>.Builder l = ImmutableArray.CreateBuilder<GatherPile>();
        foreach (string e in m_cfg.Get(Setting("piles"), "").Split(new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = e.Split(',');
            if ((f.Length == 3 || f.Length == 4) && int.TryParse(f[0], out int x) && int.TryParse(f[1], out int y)
                && int.TryParse(f[2], out int n))
            {
                l.Add(new GatherPile(x, y, n, f.Length == 4 && f[3] == "1"));
            }
        }
        return l.ToImmutable();
    }

    public void SetPiles(IEnumerable<GatherPile> piles)
    {
        List<string> parts = new();
        foreach (GatherPile p in piles)
            parts.Add(p.X + "," + p.Y + "," + p.Amount + (p.Wild ? ",1" : ""));
        m_cfg.Set(Setting("piles"), string.Join(";", parts));
    }

    public void ClearPiles() => m_cfg.Set(Setting("piles"), "");

    // Ours put on a pile make it ours; wild put on one leave it as it was.
    public void AddPile(int x, int y, int amount, bool wild = false)
    {
        List<GatherPile> l = new(Piles());
        int i = l.FindIndex(e => e.X == x && e.Y == y);
        if (i >= 0)
            l[i] = l[i] with
            {
                Amount = l[i].Amount + amount,
                Wild = l[i].Wild && wild
            };
        else
            l.Add(new GatherPile(x, y, amount, wild));
        SetPiles(l);
    }

    // The pile at fx, fy now lies at tx, ty, merged with any pile there (wild only when both were); gone, it reached
    // the collection's place.
    public void MovePile(int fx, int fy, int tx, int ty, bool gone)
    {
        List<GatherPile> l = new(Piles());
        int i = l.FindIndex(e => e.X == fx && e.Y == fy);
        if (i < 0)
            return;
        GatherPile p = l[i];
        l.RemoveAt(i);
        int j = gone ? -1 : l.FindIndex(e => e.X == tx && e.Y == ty);
        if (j >= 0)
            l[j] = l[j] with
            {
                Amount = l[j].Amount + p.Amount,
                Wild = l[j].Wild && p.Wild
            };
        else if (!gone)
            l.Add(p with
            {
                X = tx,
                Y = ty
            });
        SetPiles(l);
    }

    public void RemovePile(int x, int y)
    {
        List<GatherPile> l = new(Piles());
        l.RemoveAll(e => e.X == x && e.Y == y);
        SetPiles(l);
    }

    public int Dropped()
    {
        int n = 0;
        foreach (GatherPile p in Piles())
            n += p.Amount;
        return n;
    }

    public int Wild()
    {
        int n = 0;
        foreach (GatherPile p in Piles())
        {
            if (p.Wild)
                n += p.Amount;
        }
        return n;
    }
}
