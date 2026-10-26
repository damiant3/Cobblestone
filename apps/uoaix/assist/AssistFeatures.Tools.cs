#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the tools: a toolbox and a toolbag per profession (Tools.cs, AssistTools.md) ----------
    public const uint ToolbagCursor = 0x544F4F4C;  // "TOOL", apart from PricerCursor and LoadoutCursor
    public volatile string ToolbagStatus = "";
    // Mining and tailoring first (Damian, 2026-10-21: "at least for mining and for tailor"); smithing's hammer and the
    // tinker's tools join because the bank's toolbag they came from is gone ("the bank toolbag should be disolved").
    // Road counts: Damian's 10 shovels in the mining bag ("the bag has 10"), and KitsWant kits, the homespun top-up
    // ("you should take a few kits on the sweeps").
    static readonly ImmutableArray<Profession> s_professions = ImmutableArray.Create(
        new Profession("mining", "Mining", "shovels", DigTool, 10),
        new Profession("tailoring", "Tailoring", "sewing kits", IsKit, KitsWant),
        new Profession("smithing", "Smithing", "smith's hammers", SmithTool, 1),
        new Profession("tinkering", "Tinkering", "tinker's tools", IsTinkerTool, 1));
    public static ImmutableArray<Profession> Professions => s_professions;
    static readonly Profession s_miningTools = s_professions[0], s_tailorTools = s_professions[1],
        s_smithTools = s_professions[2], s_tinkerTools = s_professions[3];
    static bool IsKit(int g)
    {
        return g == KitArt;
    }
    public ITools Tools
    {
        get; private set;
    }
    uint MineToolbag => Tools.Bag(s_miningTools);
    // A container is usable where the player stands when it is in the pack, in the bank box while the bank is open, or
    // lies (or is held in a container lying) within LiftReach.
    bool ContainerInReach(uint s)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return false;
        bool bankOpen = BankFresh(k);
        lock (k.State.Gate)
        {
            AssistItem? it;
            for (int i = 0; i < Inventory.ChainDepth && k.State.Items.TryGet(s, out it); i++)
            {
                if (s == k.State.Backpack)
                    return true;
                if (it.Layer >= 0 && it.Mobile == k.State.Player)
                    return it.Layer != BankLayer || bankOpen;
                s = it.Container;
            }
            if (s == k.State.Backpack)
                return true;
            AssistItem? g;
            return k.State.Ground.TryGet(s, out g) && Tiles(g.X, g.Y, k.State.X, k.State.Y) <= LiftReach;
        }
    }
    public const int BankLayer = 0x1D;
    // The tools of a profession loose in the pack moved into its bag.
    void ToolsToBag(GameLink k, Profession p)
    {
        uint bag = Tools.Home(p), pack = Inv(k).Pack;
        if (bag == 0 || bag == pack)
            return;
        List<uint> loose = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (p.IsTool(it.Graphic) && it.Container == pack)
                    loose.Add(it.Serial);
        if (loose.Count > 0)
        {
            string? w = MoveInto(loose, bag);
            if (w != null)
                Note("Tools: " + p.ToolsName + " into the " + p.Name.ToLowerInvariant() + " toolbag: " + w);
        }
    }
    // A toolbox within reach with nothing known inside opened once: the state drops a container's contents once the
    // player is GroundReach away (AssistState.SweepItems), and the box drawn again on the way back brings none (uo1998
    // 2026-10-21 03:18:20: the tailoring box 446F77D0 redrawn at 4697,1111 with its 3 kits unknown, and the loop
    // stopped for want of a kit at 03:19:01).
    void OpenToolbox(GameLink k, Profession p)
    {
        uint box = Tools.Box(p);
        if (box == 0 || !Tools.BoxInReach(p) || Inv(k).Inside(box, g => true).Length > 0)
            return;
        BusyWait(clickedAt);
        string? w = Peek(box);
        if (w != null)
            Note("Tools: opening the " + p.Name.ToLowerInvariant() + " toolbox: " + w);
    }
    // Up to want of a profession's tools from its toolbox, when the box is within reach (AssistTools.md: "each caller
    // of ToolsFromBank takes from its profession's toolbox instead"). Null once the pack holds want, else why not.
    string? ToolsFromBox(GameLink k, Profession p, int want)
    {
        if (PackCount(k, p.IsTool) >= want)
            return null;
        string name = p.Name.ToLowerInvariant();
        uint box = Tools.Box(p);
        if (box == 0)
            return "no " + name + " toolbox set";
        if (!Tools.BoxInReach(p))
            return "the " + name + " toolbox " + box.ToString("X") + " is not within reach";
        OpenToolbox(k, p);
        List<uint> take = new();
        int need = want - PackCount(k, p.IsTool);
        foreach (AssistItem it in Tools.InBox(p))
            if (take.Count < need)
                take.Add(it.Serial);
        if (take.Count == 0)
            return "no " + p.ToolsName + " in the " + name + " toolbox";
        string? w = MoveInto(take, Tools.Home(p));
        if (w != null)
            return p.ToolsName + " from the " + name + " toolbox: " + w;
        Note("Tools: " + take.Count + " " + p.ToolsName + " from the " + name + " toolbox");
        return PackCount(k, p.IsTool) >= want ? null
            : "the " + name + " toolbox held " + take.Count + " " + p.ToolsName + " of " + want;
    }
    // The bag topped up to its road count from the box, at the box (AssistTools.md, "Road counts": "only when the box
    // is the trip's next stop or within reach"). Null when topped up or nothing is to be done here, else why not.
    string? RefillBag(GameLink k, Profession p)
    {
        if (!Tools.BagCarried(p) || !Tools.BoxInReach(p))
            return null;
        uint bag = Tools.Bag(p);
        OpenToolbox(k, p);
        int have = Tools.InBag(p), need = Tools.Road(p) - have;
        if (need <= 0)
            return null;
        List<uint> take = new();
        foreach (AssistItem it in Tools.InBox(p))
            if (take.Count < need && !Within(k, it.Serial, bag))
                take.Add(it.Serial);
        if (take.Count == 0)
            return null;
        string? w = MoveInto(take, bag);
        if (w != null)
            return "topping up the " + p.Name.ToLowerInvariant() + " toolbag: " + w;
        Note("Tools: the " + p.Name.ToLowerInvariant() + " toolbag topped up with " + take.Count + " " + p.ToolsName
            + " from its toolbox (" + Tools.InBag(p) + " of " + Tools.Road(p) + ")");
        return null;
    }
    // The bags of the professions given topped up where their boxes are within reach.
    void RefillInReach(GameLink k, List<Profession> professions)
    {
        foreach (Profession p in professions)
        {
            if (circuitStop || k.Closed)
                return;
            string? w = RefillBag(k, p);
            if (w != null)
                Note("Tools: " + w);
        }
    }
    // The professions whose tools the circuit set to start uses (Damian, 2026-10-22: "not sure why the tongs and tinker
    // tools, they are deadweight here in cave mining"): mining's shovels; smithing's hammer and the tinker's tools it
    // makes a sledge hammer with; the tailoring circuits' kits ("you should take a few kits on the sweeps"); no others.
    List<Profession> CircuitProfessions()
    {
        List<Profession> l = new();
        if (m_mineMode)
        {
            l.Add(s_miningTools);
        }
        else if (m_smithMode)
        {
            l.Add(s_smithTools);
            l.Add(s_tinkerTools);
        }
        else if (!(m_buyMode || m_surveyMode || m_refreshMode || m_advertMode || m_spellbookMode || m_markMode))
        {
            l.Add(s_tailorTools);
        }

        return l;
    }
    // Picked by a target cursor: a toolbag is a container in the pack; a toolbox is any container, wherever it lies
    // (Damian, 2026-10-21: "the toolbox is any container near the work by practice. by code, it could be anywhere"),
    // the pack itself excepted.
    volatile string m_toolPick = "";
    public string? ToolTarget(string key)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        m_toolPick = key;
        bool box = key.EndsWith(".box");
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, ToolbagCursor);
        string? why = k.ToClient(p, "tool cursor");
        ToolbagStatus = why == null
            ? (box ? "Target the toolbox, wherever it lies." : "Target the toolbag in your pack.")
            : "No cursor: " + why;
        return why;
    }
    Profession? ToolPickProfession(string key)
    {
        foreach (Profession p in s_professions)
            if (key == ToolPlaces.BoxKey(p) || key == ToolPlaces.BagKey(p))
                return p;
        return null;
    }
    bool HoldToolbag(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != ToolbagCursor)
            return false;
        string key = m_toolPick;
        Profession? prof = ToolPickProfession(key);
        bool box = key.EndsWith(".box");
        if (prof == null)
        {
            ToolbagStatus = "No toolbox or toolbag was being picked.";
            return true;
        }
        string what = prof.Name + (box ? " toolbox" : " toolbag");
        uint s = U32(p, 7);
        if (s == 0 || p[1] != 0)
        {
            ToolbagStatus = what + " not set: pick a container, not the ground.";
            return true;
        }
        AssistItem? it = ItemOrGround(k, s);
        bool inPack;
        uint pack;
        lock (k.State.Gate)
        {
            inPack = PackTree(k).Contains(s);
            pack = k.State.Backpack;
        }
        if (it == null || !IsContainer(it.Graphic) || s == pack || !box && !inPack)
        {
            ToolbagStatus = what + " not set: pick a "
                + (box ? "container other than the pack" : "bag inside your pack") + ".";
            return true;
        }
        if (box)
            Tools.SetBox(prof, s);
        else
            Tools.SetBag(prof, s);
        ThreadPool.QueueUserWorkItem(_ => { BusyWait(clickedAt); Peek(s); ToolbagStatus = what + " " + s.ToString("X")
            + ": " + Inv(k).Inside(s, prof.IsTool).Length + " " + prof.ToolsName + "."; });
        return true;
    }
    // The bank rune picked by a target cursor (Damian, 2026-10-18: "bank rune should be a targeting selection"): a rune
    // in the pack, single-clicked for its label, which becomes MineBankRune.
    public const uint BankRuneCursor = 0x52554E45;  // "RUNE", apart from the other assist cursors
    // A recall rune by its graphic, blank or marked (AssistIdentity.md stage 4: a kind is its graphics, never its tile
    // name).
    public static bool IsRuneArt(int graphic)
    {
        return graphic >= 0x1F14 && graphic <= 0x1F17;
    }
    public string? BankRuneTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, BankRuneCursor);
        string? why = k.ToClient(p, "bank rune cursor");
        ToolbagStatus = why == null ? "Target the rune in your pack that recalls to the bank." : "No cursor: " + why;
        return why;
    }
    bool HoldBankRune(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != BankRuneCursor)
            return false;
        uint s = U32(p, 7);
        AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
        bool inPack = false;
        if (it != null)
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(s);
        if (it == null || !inPack || !IsRuneArt(it.Graphic))
        {
            ToolbagStatus = "Bank rune not set: pick a rune inside your pack.";
            return true;
        }
        Spawn("the bank rune's label", () =>
        {
            List<string> l = LabelsOf(k, new List<AssistItem> { it });
            string line = l.Count > 0 ? l[0] : "";
            int sp = line.IndexOf(' '), at = line.LastIndexOf(" at ");
            string label = sp > 0 && at > sp ? line.Substring(sp + 1, at - sp - 1).Trim() : "";
            if (label.Length == 0 || label == "no label")
            {
                ToolbagStatus = "Bank rune not set: the rune " + s.ToString("X") + " answered no label.";
                return;
            }
            MineBankRune = label;
            MineBankRuneSerial = s;
            lock (m_packRunesGate)
                m_packRunes = m_packRunes.Set(PlaceKey(label), s);
            ToolbagStatus = "Bank rune: " + label + " (" + s.ToString("X") + ").";
        }, true);
        return true;
    }
    // Ingots in the pack that send a trip to the storage (the ingot stash or the vendor).
    public int MineStoreAt
    {
        get => Math.Max(1, MineCfgInt("store-at", 500));
        set => Cfg.Set(Server + "mine.store-at", Math.Max(1, value).ToString());
    }
    // The shovels in the pack, the tool bag's first, and one in hand.
    List<uint> Shovels(GameLink k)
    {
        List<uint> bagged = new();
        List<uint> rest = new();
        uint bag = MineToolbag;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (DigTool(it.Graphic)
                    && (inside.Contains(it.Container) || (it.Layer >= 0 && it.Mobile == k.State.Player)))
                    (it.Container == bag && bag != 0 ? bagged : rest).Add(it.Serial);
        }
        bagged.AddRange(rest);
        return bagged;
    }
    // The mining toolbox put down in the cave by the player (Damian, 2026-10-22: "box and bag, seperate, bag stays with
    // you, box stays put. box usually in house, though we can move it to field"; "a box holds the bulk supplies, the
    // bag for the doing the work away from the shop (or just out of reach of the box)"): walked to and the bag topped
    // up from it, or the road count taken into the pack when no bag is carried. The circuit never lifts or drops a box
    // or a bag.
    // Within CircuitReach of the cave, the reach MineBack walks back from rather than recalls: a box in the house beside
    // the cave is fetched from before any shovel trip by rune (Damian, 2026-10-25: "there were shovels in the toolbox, i
    // don't think the circuit even checked"; the box at 2481,73, 43 tiles from Home2's mark, radius 24).
    bool BoxInCave(GameLink k, MineCave c)
    {
        int[]? at = MineBoxSpot(k);
        return at != null && Tiles(at[0], at[1], c.X, c.Y) <= CircuitReach;
    }
    // Where the mining toolbox lies: in view, else where it was last seen. The state drops a ground item out of view,
    // and the box went unfetched at 1 shovel from 27 tiles away (2026-10-22 17:26:05, the box at 2481,73, Kwik at
    // 2472,46).
    // The spot is kept in the settings (<server>tools.mining.box-at, "serial|x|y|z"): a relay restarted at the house and
    // a circuit started there went to the vendor and the bank rune at 0 shovels, the box unseen since the restart
    // (2026-10-25 16:00:22, the box at 2481,73).
    int[] m_mineBoxAt = null!; uint m_mineBoxSeen;
    // Kept, and written to the settings only when it moved.
    void MineBoxAt(uint box, int[] at)
    {
        bool same = m_mineBoxSeen == box && m_mineBoxAt != null && m_mineBoxAt[0] == at[0] && m_mineBoxAt[1] == at[1]
            && m_mineBoxAt[2] == at[2];
        m_mineBoxAt = at;
        m_mineBoxSeen = box;
        if (!same)
            Cfg.Set(Server + "tools.mining.box-at", box.ToString("X") + "|" + at[0] + "|" + at[1] + "|" + at[2]);
    }
    // The server's draw of the mining toolbox (0x1A: serial, art, an amount when the serial's top bit is set, x with a
    // facing byte after it when its top bit is set, y with two flag bits, z), kept wherever the player is (2026-10-25
    // 16:12:13, the box drawn at the house and the circuit started in the cave a minute later).
    void MineBoxDrawn(byte[] p)
    {
        if (p[0] != 0x1A || p.Length < 14)
            return;
        uint box = Tools.Box(s_miningTools), s = U32(p, 3);
        if (box == 0 || (s & 0x7FFFFFFF) != box)
            return;
        int i = 9 + ((s & 0x80000000) != 0 ? 2 : 0);
        if (p.Length < i + 5)
            return;
        int x = (p[i] << 8) | p[i + 1];
        int j = i + 2 + ((x & 0x8000) != 0 ? 1 : 0);
        if (p.Length < j + 3)
            return;
        int y = ((p[j] << 8) | p[j + 1]) & 0x3FFF;
        MineBoxAt(box, new[] { x & 0x7FFF, y, (sbyte)p[j + 2] });
    }
    int[]? MineBoxSpot(GameLink k)
    {
        uint box = Tools.Box(s_miningTools);
        if (box == 0)
            return null;
        int[]? at = GroundSpot(k, box);
        if (at != null)
        {
            MineBoxAt(box, at);
            return at;
        }
        if (m_mineBoxSeen == box)
            return m_mineBoxAt;
        string[] t = Cfg.Get(Server + "tools.mining.box-at", "").Split('|');
        int x, y, z;
        if (t.Length == 4 && t[0] == box.ToString("X") && int.TryParse(t[1], out x) && int.TryParse(t[2], out y)
            && int.TryParse(t[3], out z))
            return new[] { x, y, z };
        return null;
    }
    // The shovels a trip buys for the mining toolbox in the cave on top of the bag's road count (Damian, 2026-10-22:
    // "rebuy should fill the box first with 10-20 if available"): as many as the tinkers have, up to this.
    public const int BoxShovels = 20;
    // Back from a shovel trip: every shovel past the bag's road count into the mining toolbox, the bag kept at its road
    // count, then back to where the walk began. Null, or why not.
    string? ShovelsToBox(GameLink k, MineCave c)
    {
        int[]? at = MineBoxSpot(k);
        if (at == null)
            return "the mining toolbox's spot is unknown";
        int x0, y0;
        lock (k.State.Gate)
        {
            x0 = k.State.X;
            y0 = k.State.Y;
        }
        if (!Tools.BoxInReach(s_miningTools))
        {
            CircuitStatus = "Mine: to the mining toolbox with the new shovels";
            string? w = WalkTo(at[0], at[1], 1);
            if (w != null)
                return "to the mining toolbox at " + at[0] + "," + at[1] + ": " + w;
        }
        OpenToolbox(k, s_miningTools);
        List<uint> all = Shovels(k);
        int keep = Tools.Road(s_miningTools);
        string? put = null;
        int moved = 0;
        if (all.Count > keep)
        {
            // Shovels lists the bag's first: the bag keeps its first keep, the rest go in the box.
            List<uint> extra = all.GetRange(keep, all.Count - keep);
            lock (k.State.Gate)
                extra.RemoveAll(s => { AssistItem? it; return !k.State.Items.TryGet(s, out it)
                    || it.Layer >= 0; });
            if (extra.Count > 0)
            {
                put = MoveInto(extra, Tools.Box(s_miningTools));
                if (put == null)
                    moved = extra.Count;
            }
        }
        if (moved > 0)
            Note("Mine: " + moved + " shovels into the mining toolbox, " + keep + " kept in the bag");
        string? back = MineBack(k, c, x0, y0);
        if (back != null)
            Note("Mine: from the mining toolbox " + back);
        return put;
    }
    // The walk to the box returns to the stand it left (MineBack), or the stand the plan chose is lost (2026-10-22
    // 17:18:08: planned 2459,62, dug from 2466,59 after the box at 2481,73).
    string? CaveToolbox(GameLink k, MineCave c)
    {
        if (!BoxInCave(k, c))
            return "no mining toolbox lies within " + CircuitReach + " tiles of the cave";
        int x0, y0;
        lock (k.State.Gate)
        {
            x0 = k.State.X;
            y0 = k.State.Y;
        }
        string? took = BoxFetch(k);
        string? back = MineBack(k, c, x0, y0);
        if (back != null)
            Note("Mine: from the mining toolbox " + back);
        return took;
    }
    // To the mining toolbox at its last known spot (MineBoxSpot) and the shovels out of it: the bag topped up, or the
    // road count into the pack with no bag. Null, or why none came.
    string? BoxFetch(GameLink k)
    {
        int[]? at = MineBoxSpot(k);
        if (at == null)
            return "the mining toolbox's spot is unknown";
        if (!Tools.BoxInReach(s_miningTools))
        {
            CircuitStatus = "Mine: to the mining toolbox";
            string? w = WalkTo(at[0], at[1], 1);
            if (w != null)
                return "to the mining toolbox at " + at[0] + "," + at[1] + ": " + w;
        }
        string? took;
        if (Tools.BagCarried(s_miningTools))
        {
            took = RefillBag(k, s_miningTools);
            if (took == null && Tools.InBox(s_miningTools).Length == 0 && Shovels(k).Count <= 1)
                took = "no " + s_miningTools.ToolsName + " in the mining toolbox";
        }
        else
        {
            took = ToolsFromBox(k, s_miningTools, Math.Max(1, Tools.Road(s_miningTools)));
        }
        return took;
    }
}
