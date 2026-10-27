#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the smithing circuit (Damian, 2026-10-18: smith the ladder's item for the skill, sell to the smiths in
    // reach, bank gold over 300, tinker a sledge hammer when none, until Blacksmithing reads 100.0; "no skill lock
    // assumption") ----------
    // It works where it starts: the 1998 hammer needs an anvil and a forge within 3 tiles (blacksmith.m Q4ZM). The
    // hammer's menus are walked by their text (SmithPick): "Build Armor", then "Build Ring Armor", "Build Chain Armor"
    // or "Build Plate Armor" ("Build Helmets" under it), then "Build <item>, N metal." A category holding one entry the
    // player can build is flattened into its parent, and an item needing more metal or skill than the player has is
    // left off (blacksmith.m Q4DY and Q4A1). Ingots come from smith.ingots: bank (the city's bank box), buy (a smith in
    // reach) or mine (the mining circuit's storage, mine.store).
    public const int SmithSkill = 7, SmithDone = 1000, SmithIngotTake = 100, SmithAgainMax = 3;
    // Sold once the pack is full (Damian, 2026-10-18: "fill the backpack up with more coifs than just 5. use
    // weight/capacity here, not a hardcoded limit"): the next piece, at the heaviest made art's tile weight, would pass
    // the carry limit (MineLimit), or the pack holds PackItemsMax - 1 items counting every bag (a piece does not
    // stack).
    bool SmithPackFull(GameLink k, Set<int> made)
    {
        List<UoTiles.Tile> tiles = UoTiles.Items();
        int each = 1;
        foreach (int g in made)
            if (g >= 0 && g < tiles.Count && tiles[g].Weight < 255)
                each = Math.Max(each, (int)tiles[g].Weight);
        int w, held = Inv(k).ItemCount();
        lock (k.State.Gate)
            w = k.State.Weight;
        return held >= PackItemsMax - 1 || (w > 0 && w + each > MineLimit(k));
    }
    public string SmithIngots
    {
        get => Cfg.Get(Server + "smith.ingots", "bank");
        set => Cfg.Set(Server + "smith.ingots",
            value == "buy" || value == "mine" || value == "harvest" ? value : "bank");
    }
    // The mining circuit's storage for one run of it: the smithing circuit's harvest keeps the ingots in the pack.
    volatile string? m_mineStoreFor = null;
    // The ingots taken from the bank or bought at a time (Damian, 2026-10-18: "make the ingot count loadout a config in
    // the ui"), SmithIngotTake unless set.
    public int SmithIngotLoad
    {
        get => Math.Max(1, Cfg.GetInt(Server + "smith.ingots.load", SmithIngotTake));
        set => Cfg.Set(Server + "smith.ingots.load", Math.Max(1, value).ToString());
    }
    public string SmithTinker
    {
        get => Cfg.Get(Server + "smith.tinker", "Tools|Sledge");
        set => Cfg.Set(Server + "smith.tinker", value);
    }
    static bool SmithTool(int g)
    {
        string n = UoTiles.Name(g).ToLowerInvariant();
        return n.Contains("smith") || n.Contains("sledge") || n == "tongs";
    }
    // A menu entry or ladder step as one name: "Build Platemail gorget, 10 metal." and "plate gorget" are both
    // "plate gorget"; the menu's plate tunic is "Platemail".
    static string SmithNorm(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (s.StartsWith("build "))
            s = s.Substring(6);
        int cut = s.IndexOfAny(new[] { ',', ':' });
        if (cut >= 0)
            s = s.Substring(0, cut);
        s = s.Replace("  ", " ").Trim();
        if (s == "platemail")
            return "plate tunic";
        return s.StartsWith("platemail ") ? "plate " + s.Substring(10) : s;
    }
    // The hammer's menu, blacksmith.m Q4Y8: each category's path and the arts built under it; an item's name is its
    // 1.25 tile data name ("katana%s%" is "katana").
    static readonly object[][] s_smithMenu = {
        new object[] { new[] { "build armor", "build ring armor" }, new[] { 0x13EB, 0x13EF, 0x13F0, 0x13EC } },
        new object[] { new[] { "build armor", "build chain armor" }, new[] { 0x13BB, 0x13BE, 0x13BF } },
        new object[] { new[] { "build armor", "build plate armor", "build helmets" },
        new[] { 0x140A, 0x140C, 0x140E, 0x1408, 0x1412 } },
        new object[] { new[] { "build armor", "build plate armor" },
        new[] { 0x1413, 0x1414, 0x1410, 0x1411, 0x1415, 0x1C04 } },
        new object[] { new[] { "build shields" }, new[] { 0x1B73, 0x1B72, 0x1B7B, 0x1B78, 0x1B74, 0x1B76 } },
        new object[] { new[] { "build weapons", "build blades" },
        new[] { 0x0F51, 0x1441, 0x13FF, 0x1401, 0x13B6, 0x0F5E, 0x0F61, 0x13B9 } },
        new object[] { new[] { "build weapons", "build axes" },
        new[] { 0x0F47, 0x0F49, 0x0F45, 0x1443, 0x0F4B, 0x13FB, 0x13B0 } },
        new object[] { new[] { "build weapons", "build pole arms" }, new[] { 0x1403, 0x0F62, 0x1405, 0x0F4D, 0x143F } },
        new object[] { new[] { "build weapons", "build bludgeoning weapons" },
        new[] { 0x0F5C, 0x143B, 0x1407, 0x1439, 0x143D } },
    };
    // A tile data name read as a player writes it: "rub%ies/y%" is "ruby", "katana%s%" is "katana".
    static string TileWord(string n)
    {
        int a;
        while ((a = n.IndexOf('%')) >= 0)
        {
            int b = n.IndexOf('%', a + 1);
            if (b < 0)
                break;
            string inner = n.Substring(a + 1, b - a - 1);
            int sl = inner.IndexOf('/');
            n = n.Substring(0, a) + (sl >= 0 ? inner.Substring(sl + 1) : "") + n.Substring(b + 1);
        }
        return n;
    }
    static Map<string, string[]>? s_smithPaths;
    // Every smithable item by name (SmithNorm of its tile name), with its path; null before the tile data is read.
    public static Map<string, string[]>? SmithItems()
    {
        if (s_smithPaths != null)
            return s_smithPaths;
        if (UoTiles.Items().Count == 0)
            return null;
        Map<string, string[]> d = Map<string, string[]>.Empty;
        foreach (object[] e in s_smithMenu)
            foreach (int art in (int[])e[1])
            {
                string n = SmithNorm(TileWord(UoTiles.Name(art)));
                if (n.Length > 0 && !d.ContainsKey(n))
                    d = d.Set(n, (string[])e[0]);
            }
        return s_smithPaths = d;
    }
    // The categories above an item, outermost first; null for an item the circuit has no path for.
    static List<string>? SmithChain(string n)
    {
        Map<string, string[]>? items = SmithItems();
        string[]? path;
        if (items != null && items.TryGet(n, out path))
            return new List<string>(path);
        List<string> c = new();
        if (n.Contains("shield") || n.Contains("buckler"))
        {
            c.Add("build shields");
            return c;
        }
        c.Add("build armor");
        if (n.StartsWith("ringmail"))
        {
            c.Add("build ring armor");
        }
        else if (n.StartsWith("chainmail"))
        {
            c.Add("build chain armor");
        }
        else if (n.StartsWith("plate") || n.Contains("helm") || n.Contains("bascinet"))
        {
            c.Add("build plate armor");
            if (n.Contains("helm") || n.Contains("bascinet"))
                c.Add("build helmets");
        }
        else
        {
            return null;
        }

        return c;
    }
    ValueMap<string, int> m_smithCost = ValueMap<string, int>.Empty;
    int SmithCostOf(string item)
    {
        string n = SmithNorm(item);
        int c;
        if (m_smithCost.TryGet(n, out c))
            return c;
        foreach (string[] r in s_smithDifficulty)
            if (SmithNorm(r[0]) == n)
                return int.Parse(r[1]);
        return 25;
    }
    // One build's menus under actionGate: the hammer used, each menu answered with the item when it lists it, else with
    // the deepest of the item's categories it lists. Null once the item is picked (art: its model), "again" on a wait
    // line, or why not; a menu offering neither is cancelled.
    string? SmithPick(GameLink k, uint hammer, string item, Heard mark, out int art)
    {
        art = 0;
        string want = SmithNorm(item);
        List<string>? chain = SmithChain(want);
        if (chain == null)
            return "no menu path is known for " + item;
        byte[]? menu = null;
        Action<byte[]> seen = p => { if (p[0] == 0x7C) menu = p; };
        k.ClaimMenu = true;
        k.ServerPacket += seen;
        try
        {
            m_craftTool = null;
            k.Settle();
            string? w = k.Use(hammer, false);
            if (w != null)
                return w;
            for (int depth = 0; depth < SmithPicksMax; depth++)
            {
                Wait.Until(Clock, () => menu != null, AnswerMs, "the menu", closed: () => k.Closed, stepMs: 20);
                byte[]? m = menu;
                menu = null;
                if (m == null)
                {
                    string? said = mark.Last();
                    if (said != null
                        && s_waitLines.Exists(wl => said.StartsWith(wl, StringComparison.OrdinalIgnoreCase)))
                        return "again";
                    return said != null ? "\"" + said + "\"" : "the hammer opened no menu";
                }
                string? text = null;
                int deepest = -1;
                for (int i = 1; ; i++)
                {
                    string? t = MenuItem(m, i);
                    if (t == null)
                        break;
                    // An item names its metal; a category ("Build Plate Armor") names none.
                    if (SmithNorm(t) == want && MetalCost(t) > 0)
                    {
                        text = t;
                        deepest = int.MaxValue;
                        break;
                    }
                    int c = chain.IndexOf(t.Trim().ToLowerInvariant());
                    if (c > deepest)
                    {
                        deepest = c;
                        text = t;
                    }
                }
                if (text == null)
                {
                    byte[] cancel = new byte[13];
                    cancel[0] = 0x7D;
                    Array.Copy(m, 3, cancel, 1, 6);
                    k.Settle();
                    k.Inject(cancel, "menu cancel", false);
                    return "the hammer's menu offers neither " + item
                        + " nor its category (too few ingots or too little skill)";
                }
                byte[]? r = MenuPick(m, text);
                k.Settle();
                w = k.Inject(r!, "menu pick " + ((r![7] << 8) | r[8]), false);
                if (w != null)
                    return w;
                if (deepest == int.MaxValue)
                {
                    art = (r[9] << 8) | r[10];
                    int cost = MetalCost(text);
                    if (cost > 0)
                        m_smithCost = m_smithCost.Set(want, cost);
                    m_lastBuild = SmithBook.Display(text);
                    m_lastBuildAt = Clock.UtcNow;
                    return null;
                }
            }
            return "no item after " + SmithPicksMax + " menus";
        }
        finally { k.ServerPacket -= seen; k.ClaimMenu = false; }
    }
    // The 1998 smithy (blacksmith.m Q4ZM): an anvil is 0FAF or 0FB0, a forge 0FB1 or 197A..19A9, each within 3 tiles of
    // the smith.
    static bool IsAnvil(int g)
    {
        return g == 0x0FAF || g == 0x0FB0;
    }
    static bool IsSmithForge(int g)
    {
        return g == 0x0FB1 || (g >= 0x197A && g <= 0x19A9);
    }
    public const int SmithyReach = 3, SmithyScan = 60;
    // Every anvil and forge known near (x, y): those the atlas recorded as world items (Damian, 2026-10-18: "integrate
    // with the atlas which should record where anvils are"), and the map's statics within SmithyScan tiles.
    List<int[]> SmithyParts(int x, int y, bool anvils)
    {
        List<int[]> l = new();
        if (Atlas != null)
            foreach (AtlasPlace p in Atlas.Places())
                if (p.Kind == (anvils ? "Anvil" : "Forge") && Tiles(p.X, p.Y, x, y) <= SmithyScan)
                    l.Add(new[] { p.X, p.Y });
        GameLink k = m_link;
        if (k != null)
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if ((anvils ? IsAnvil(g.Graphic) : IsSmithForge(g.Graphic))
                        && !l.Exists(t => t[0] == g.X && t[1] == g.Y))
                        l.Add(new[] { g.X, g.Y });
        for (int tx = x - SmithyScan; tx <= x + SmithyScan; tx++)
        {
            for (int ty = y - SmithyScan; ty <= y + SmithyScan; ty++)
                foreach (int[] s in UoMap.StaticsAt(tx, ty))
                    if (anvils ? IsAnvil(s[0]) : IsSmithForge(s[0]))
                    {
                        l.Add(new[] { tx, ty });
                        break;
                    }
        }

        return l;
    }
    // The spot to smith from: the player's tile when an anvil and a forge are both within SmithyReach of it; else the
    // tile between the nearest anvil with a forge within 2 * SmithyReach of it and that forge, walked to. Null, or why
    // not. Minoc's anvils and forges are server items, none in the map's statics (2026-10-18: 16377 statics around its
    // bank, no anvil or forge among them), and the client is sent an item only in view. So the town's smith shops and
    // forges (the region file's BLACKSMITH, ARMORER and WEAPONRY shops and its miners' guilds; Damian, 2026-10-18:
    // "make it check forges and smith shops to inform the atlas") are walked into, nearest first, each one the atlas
    // holds no anvil or forge for, the items in view there going into the atlas; then the search runs again.
    static readonly ImmutableArray<string> s_smithyShopKinds = [ "BLACKSMITH", "ARMORER", "WEAPONRY", "MINERSGUILD" ];
    Set<string> m_smithyLooked = Set<string>.Empty;
    string? ToSmithy(GameLink k, out int sx, out int sy)
    {
        string? w = ToSmithyOnce(k, out sx, out sy);
        if (w == null)
            return w;
        string? city = CityOf(k);
        if (city == null)
            return w + "; not in a town";
        List<UoRegions.Shop> shops = UoRegions.ShopsIn(city, s_smithyShopKinds);
        List<AtlasPlace> known = Atlas == null ? new List<AtlasPlace>()
            : Atlas.Places().FindAll(p => p.Kind == "Anvil" || p.Kind == "Forge");
        shops.RemoveAll(s => m_smithyLooked.Contains(s.Key) || known.Exists(p => s.Holds(p.X, p.Y, 1)));
        int looked = 0;
        while (shops.Count > 0 && !circuitStop)
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            UoRegions.Shop s = shops[0];
            shops.RemoveAt(0);
            m_smithyLooked = m_smithyLooked.Add(s.Key);
            looked++;
            CircuitStatus = "Smithing: looking for an anvil in " + s.Text;
            string? ww = WalkTo(s.CX, s.CY, 2);
            if (ww != null)
            {
                Note("Smithing: " + s.Text + ": not reached: " + ww);
                continue;
            }
            Clock.Sleep(AnswerMs);
            int anvils = SmithyParts(s.CX, s.CY,
                true).FindAll(a => s.Holds(a[0], a[1], 1)).Count, forges = SmithyParts(s.CX, s.CY,
                false).FindAll(f => s.Holds(f[0], f[1], 1)).Count;
            Note("Smithing: " + s.Text + ": " + anvils + " anvils, " + forges + " forges");
        }
        if (looked == 0)
            return w + "; every smith shop in " + city + " was looked in";
        return ToSmithyOnce(k, out sx, out sy);
    }
    string? ToSmithyOnce(GameLink k, out int sx, out int sy)
    {
        lock (k.State.Gate)
        {
            sx = k.State.X;
            sy = k.State.Y;
        }
        int px = sx, py = sy;
        List<int[]> anvils = SmithyParts(px, py, true);
        List<int[]> forges = SmithyParts(px, py, false);
        if (anvils.Exists(a => Tiles(a[0], a[1], px, py) <= SmithyReach)
            && forges.Exists(f => Tiles(f[0], f[1], px, py) <= SmithyReach))
            return null;
        List<int[]> pairs = new();
        foreach (int[] a in anvils)
        {
            int[]? near = null;
            foreach (int[] f in forges)
                if (Tiles(a[0], a[1], f[0], f[1]) <= 2 * SmithyReach
                    && (near == null || Tiles(a[0], a[1], f[0], f[1]) < Tiles(a[0], a[1], near[0], near[1])))
                    near = f;
            if (near != null)
                pairs.Add(new[] { (a[0] + near[0]) / 2, (a[1] + near[1]) / 2 });
        }
        if (pairs.Count == 0)
            return "no anvil with a forge beside it known within " + SmithyScan + " tiles (" + anvils.Count
                + " anvils, " + forges.Count + " forges)";
        pairs.Sort((a, b) => Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        string? w = null;
        foreach (int[] s in pairs)
        {
            CircuitStatus = "Smithing: walking to the anvil at " + s[0] + "," + s[1];
            w = WalkTo(s[0], s[1], 1);
            if (w == null)
            {
                lock (k.State.Gate)
                {
                    sx = k.State.X;
                    sy = k.State.Y;
                }
                return null;
            }
            Note("Smithing: the anvil at " + s[0] + "," + s[1] + ": " + w);
        }
        return "no anvil reached: " + w;
    }
    // What the circuit smiths for (Damian, 2026-10-18: "make it an option to build for other reasons like pure profit
    // or to make suits and gear for people and sales on merchants. like a ring suit, a five pack of kats or spears"):
    // skill, the ladder until 100.0; profit, the item of the most gold per ingot the smithing book knows, sold to NPCs
    // until the ingots run out; order, the order list, SmithPickOf times each item built and the best kept by NPC
    // price.
    public string SmithGoal
    {
        get => Cfg.Get(Server + "smith.goal", "skill");
        set => Cfg.Set(Server + "smith.goal", value == "profit" || value == "order" ? value : "skill");
    }
    public string SmithOrder
    {
        get => Cfg.Get(Server + "smith.order", "");
        set => Cfg.Set(Server + "smith.order", value ?? "");
    }
    public int SmithPickOf
    {
        get => Math.Max(1, Cfg.GetInt(Server + "smith.pick", 2));
        set => Cfg.Set(Server + "smith.pick", Math.Max(1, value).ToString());
    }
    static readonly string[][] s_smithOrderPresets = {
        new[] { "Ring suit", "ringmail tunic@1;ringmail leggings@1;ringmail sleeves@1;ringmail gloves@1" },
        new[] { "Chain suit",
        "chainmail coif@1;chainmail tunic@1;chainmail leggings@1;ringmail sleeves@1;ringmail gloves@1" },
        new[] { "Plate suit", "plate helm@1;plate gorget@1;plate arms@1;plate gloves@1;plate legs@1;plate tunic@1" },
        new[] { "Five katanas", "katana@5" }, new[] { "Five spears", "spear@5" },
    };
    public static string[][] SmithOrderPresets => s_smithOrderPresets;
    // The order as name and count, "item@count;..."; an item the hammer's menu does not hold is left out and said.
    List<KeyValuePair<string, int>> SmithOrderLines(out string? bad)
    {
        List<KeyValuePair<string, int>> l = new();
        List<string> no = new();
        foreach (string e in SmithOrder.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int at = e.LastIndexOf('@');
            int n = 1;
            string name = SmithNorm(at > 0 ? e.Substring(0, at) : e);
            if (at > 0 && !int.TryParse(e.Substring(at + 1).Trim(), out n))
                n = 1;
            if (SmithChain(name) == null)
                no.Add(name);
            else if (n > 0)
                l.Add(new KeyValuePair<string, int>(name, n));
        }
        bad = no.Count > 0 ? "not on the hammer's menu: " + string.Join(", ", no) : null;
        return l;
    }
    // The smithable item of the most gold per ingot near this skill, from the smithing book; null when none has an NPC
    // price.
    string? SmithProfitItem(int skill)
    {
        if (Smith == null)
            return null;
        foreach (SmithRow r in Smith.Rows(skill))
            if (r.PerIngot > 0 && SmithChain(SmithNorm(r.Item)) != null)
                return SmithNorm(r.Item);
        return null;
    }
    // A smith's tool in the pack before the anvil (Damian, 2026-10-18: "on go, it has no ingots or smith hammer. it
    // runs to anvil first ... the backpack toolbag has a tinker tool in it. it gets the ingots, but then never makes a
    // hammer ... there also is a tinker in town"): the smithing toolbox first, when within reach; then a sledge hammer
    // tinkered from the pack's ingots; then one bought from the town's tinkers and smiths. Null, or why there is none.
    static readonly ImmutableArray<string> s_smithToolShops = [ "TINKER", "BLACKSMITH", "ARMORER", "WEAPONRY" ];
    string? SmithHammer(GameLink k, string city)
    {
        if (PackCount(k, SmithTool) > 0)
            return null;
        CircuitStatus = "Smithing: a hammer from the toolbox";
        string? bw = ToolsFromBox(k, s_smithTools, 1);
        if (bw == null)
            return null;
        Note("Smithing: " + bw);
        // The purse from the bank, for the hammer's purchase if the tinkering fails (every shop refused a pack of 0
        // gold, 23:41:06 to 23:42:01).
        if (city != null && PackCount(k, g => g == GoldArt) < PurseKeep)
        {
            string? gw = ToCityBank(k, city, "the purse");
            if (gw == null)
            {
                string gt;
                gw = PurseTo(PurseKeep, out gt);
            }
            if (gw != null)
                Note("Smithing: the purse from the bank: " + gw);
        }
        CircuitStatus = "Smithing: tinkering a sledge hammer";
        string? tw = TinkerTool(k, SmithTinker.Split('|'), s_smithTools, 1, 1, "a sledge hammer");
        if (tw == null)
            return null;
        Note("Smithing: tinkering a sledge hammer: " + tw);
        CircuitStatus = "Smithing: buying a smith's hammer";
        string? buy = BuyTool(k, s_smithTools, s_smithToolShops);
        if (buy == null)
            return null;
        return "no smith's hammer: tinkering one: " + tw + "; buying one: " + buy;
    }
    string RunSmithing(GameLink k)
    {
        string? city = CityOf(k);
        Set<int> made = Set<int>.Empty;
        int built = 0, failed = 0, sold = 0, goldIn = 0, again = 0, from = SkillOf(k, SmithSkill);
        string? end = null;
        int sx, sy;
        string? goal = SmithGoal, badOrder = null;
        List<KeyValuePair<string, int>>? order = goal == "order" ? SmithOrderLines(out badOrder) : null;
        int line = 0, pick = SmithPickOf;
        Map<string, List<uint>> lineBuilt = Map<string, List<uint>>.Empty;
        Set<uint> seen = Set<uint>.Empty;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container))
                    seen = seen.Add(it.Serial);
        }
        try
        {
            if (badOrder != null)
                Note("Smithing: " + badOrder);
            if (order != null && order.Count == 0)
                return "the order holds nothing the hammer can build";
            // The ingots, then the hammer, then the anvil: an empty pack takes its ingots from the bank first, when the
            // bank is their source, and the tinkering uses them.
            if (city != null && SmithIngots == "bank" && PackIngots(k) == 0)
            {
                CircuitStatus = "Smithing: ingots from the bank";
                string? bw = BankHere(k, city);
                if (bw == null)
                {
                    string taken;
                    bw = BankTake(new List<int> { IngotArt }, SmithIngotLoad, out taken);
                }
                if (bw != null)
                {
                    end = "ingots from the bank: " + bw;
                    return end;
                }
            }
            end = SmithHammer(k, city!);
            if (end != null)
                return end;
            end = ToSmithy(k, out sx, out sy);
            if (end != null)
                return end;
            while (end == null && !circuitStop && !k.Closed)
            {
                int skill = SkillOf(k, SmithSkill);
                string? item;
                if (goal == "order")
                {
                    while (line < order!.Count && lineBuilt.ContainsKey(order[line].Key)
                        && lineBuilt[order[line].Key]!.Count >= order[line].Value * pick)
                        line++;
                    if (line == order.Count)
                    {
                        end = "the order is built";
                        break;
                    }
                    item = order[line].Key;
                }
                else if (goal == "profit")
                {
                    item = SmithProfitItem(skill);
                    if (item == null)
                    {
                        end = "no item you can smith has an NPC price yet (Maker tab, Smithing value)";
                        break;
                    }
                }
                else
                {
                    if (skill >= SmithDone)
                    {
                        end = "Blacksmithing reads " + (skill / 10.0).ToString("0.0");
                        break;
                    }
                    item = SmithNow(skill);
                    if (item == null)
                    {
                        end = "the ladder has no item for " + (skill / 10.0).ToString("0.0");
                        break;
                    }
                }
                int cost = SmithCostOf(item);
                // Sold at 5 made, or before a trip for ingots; the gold over the purse banked above BankOver. An
                // order's pieces are kept until it is built.
                if (goal != "order" && made.Count > 0 && (SmithPackFull(k, made) || PackIngots(k) < cost))
                {
                    end = SmithSell(k, made, sx, sy, ref sold, ref goldIn);
                    if (end != null)
                        break;
                }
                if (city != null && PackCount(k, g => g == GoldArt) > BankOver)
                {
                    CircuitStatus = "Smithing: banking the gold";
                    string? bw = BankHere(k, city);
                    if (bw != null)
                        Note("Smithing: " + bw);
                    string? ww = WalkTo(sx, sy, 0);
                    if (ww != null)
                    {
                        end = "back to the anvil: " + ww;
                        break;
                    }
                }
                if (PackIngots(k) < cost)
                {
                    end = SmithIngotsIn(k, city!, cost, sx, sy);
                    if (end != null)
                        break;
                }
                if (PackCount(k, SmithTool) == 0)
                {
                    string? tw = SmithHammer(k, city!);
                    if (tw != null)
                    {
                        end = tw;
                        break;
                    }
                    string? ww = WalkTo(sx, sy, 0);
                    if (ww != null)
                    {
                        end = "back to the anvil: " + ww;
                        break;
                    }
                }
                AssistItem? hammer = null;
                lock (k.State.Gate)
                {
                    Set<uint> inside = PackTree(k);
                    foreach (AssistItem it in k.State.Items.Values)
                        if ((inside.Contains(it.Container) || (it.Layer >= 0 && it.Mobile == k.State.Player))
                            && SmithTool(it.Graphic))
                        {
                            hammer = it;
                            break;
                        }
                }
                if (hammer == null)
                {
                    end = "no smith's hammer in the pack";
                    break;
                }
                CircuitStatus = "Smithing: " + item + " at " + (skill / 10.0).ToString("0.0") + " (" + PackIngots(k)
                    + " ingots)";
                BusyWait(clickedAt);
                Heard mark;
                string? w;
                int art;
                using (Act(k))
                {
                    mark = new(k.State);
                    w = SmithPick(k, hammer.Serial, item, mark, out art);
                    clickedAt = Clock.UtcNow;
                }
                if (w == "again")
                {
                    if (++again > SmithAgainMax)
                    {
                        end = "the server asked to wait " + again + " times in a row";
                        break;
                    }
                    Clock.Sleep(Ms("busy:smith"));
                    continue;
                }
                again = 0;
                if (w != null)
                {
                    end = item + ": " + w;
                    break;
                }
                if (art != 0)
                    made = made.Add(art);
                string got = CraftResult(k, mark, true);
                if (got == "failed")
                {
                    failed++;
                }
                else if (got == "made" || got == "exceptional")
                {
                    built++;
                }
                else
                {
                    end = item + ": " + got;
                    break;
                }
                // An order's piece by its serial: the new item of the art picked in the pack.
                if (goal == "order" && got != "failed")
                {
                    uint piece = 0;
                    for (int look = 0; look < 5 && piece == 0; look++)
                    {
                        lock (k.State.Gate)
                        {
                            Set<uint> inside = PackTree(k);
                            foreach (AssistItem it in k.State.Items.Values)
                                if (inside.Contains(it.Container) && it.Graphic == art && !seen.Contains(it.Serial))
                                {
                                    piece = it.Serial;
                                    break;
                                }
                        }
                        if (piece == 0)
                            Clock.Sleep(200);
                    }
                    if (piece == 0)
                    {
                        end = item + ": made, but no new " + item + " is in the pack";
                        break;
                    }
                    seen = seen.Add(piece);
                    List<uint>? pieces;
                    if (!lineBuilt.TryGet(item, out pieces))
                        lineBuilt = lineBuilt.Set(item, pieces = new List<uint>());
                    pieces.Add(piece);
                }
            }
            if (end == null)
                end = circuitStop ? "stopped" : "the connection closed";
            // Ended on its own: what was made sold, then the gold over the purse and the ingots left into the bank
            // (Damian, 2026-10-18: "this circuit ended without selling the last legs made and banking the gold and
            // excess iron").
            if (!circuitStop && !k.Closed)
            {
                // A smith that gave no proper answer is not asked again (L-FUTILITY).
                string? sw = made.Count == 0 || end.StartsWith("asking ") ? null
                    : goal == "order" ? OrderPick(k, order!, lineBuilt, made, sx, sy, ref sold, ref goldIn)
                    : SmithSell(k, made, sx, sy, ref sold, ref goldIn);
                if (sw != null)
                    end += "; " + (goal == "order" ? "" : "selling: ") + sw;
                if (city != null && !circuitStop)
                {
                    CircuitStatus = "Smithing: to the bank";
                    string? bw = BankHere(k, city);
                    if (bw == null && PackIngots(k) > 0)
                    {
                        string put;
                        bw = BankPut(new List<int> { IngotArt }, out put);
                    }
                    end += bw == null ? "; the gold and ingots banked" : "; the bank: " + bw;
                }
            }
            return end;
        }
        finally
        {
            m_smithMode = false;
            SmithStatus = built + " made, " + failed + " failed, " + sold + " sold for " + goldIn
                + " gold; Blacksmithing " + (from / 10.0).ToString("0.0") + " to "
                + (SkillOf(k, SmithSkill) / 10.0).ToString("0.0") + (end != null ? "; " + end : "");
            Note("Smithing: " + SmithStatus);
        }
    }
    public volatile string SmithStatus = "";
    // An order's pick (Damian, 2026-10-18: "when making for sale to people (not npc) we pick only those items that have
    // the best resale value at npc for a check of the hidden quality we can't see"): the nearest smith in reach is
    // asked "sell" and its list is read and answered with nothing; each order line keeps its count of pieces at the
    // highest prices, and the rest are sold to that smith. The kept pieces stay in the pack. What was kept and sold
    // comes back.
    string OrderPick(GameLink k, List<KeyValuePair<string, int>> order, Map<string, List<uint>> built,
        Set<int> made, int sx, int sy, ref int sold, ref int goldIn)
    {
        List<AtlasNpc> smiths = SmithBuyers(k, made, sx, sy);
        if (smiths.Count == 0)
            return "no smith in reach to read prices from; every piece kept";
        string name = smiths[0].Name.Split(' ')[0];
        string? near = NearVendor(k, name);
        if (near != null)
            return "reading prices at " + name + ": " + near + "; every piece kept";
        byte[]? list;
        using (Act(k))
        {
            CircuitStatus = "Smithing: reading " + name + "'s prices";
            list = AskNear(k, name, "sell", 0x9E);
        }
        if (list == null || list.Length < 9)
            return "asking " + name + ": " + NoAnswer(name, "sell") + "; every piece kept";
        ValueMap<uint, int> price = ValueMap<uint, int>.Empty;
        int n = (list[7] << 8) | list[8], at = 9;
        for (int i = 0; i < n && at + 14 <= list.Length; i++)
        {
            uint s = U32(list, at);
            int len = (list[at + 12] << 8) | list[at + 13];
            price = price.Set(s, (list[at + 10] << 8) | list[at + 11]);
            at += 14 + len;
        }
        Set<uint> keep = Set<uint>.Empty;
        Set<uint> mine = Set<uint>.Empty;
        List<string> parts = new();
        foreach (KeyValuePair<string, int> o in order)
        {
            List<uint>? pieces;
            if (!built.TryGet(o.Key, out pieces) || pieces.Count == 0)
            {
                parts.Add(o.Key + ": none built");
                continue;
            }
            Func<uint, int> p = s => price.Get(s, -1);
            List<uint> sorted = new(pieces);
            sorted.Sort((a, b) => p(b).CompareTo(p(a)));
            List<uint> kept = sorted.GetRange(0, Math.Min(o.Value, sorted.Count));
            foreach (uint s in kept)
                keep = keep.Add(s);
            foreach (uint s in pieces)
                mine = mine.Add(s);
            parts.Add(o.Key + ": kept " + kept.Count + " of " + o.Value + " (" + name + " pays "
                + string.Join(", ", kept.ConvertAll(s => p(s) < 0 ? "?" : p(s).ToString())) + "), "
                + (pieces.Count - kept.Count) + " to sell");
        }
        Note("Smithing: order pick: " + string.Join("; ", parts));
        if (mine.Count > keep.Count)
        {
            int s2, gd, under;
            string? w = VendorSell(g => made.Contains(g), g => 1, out s2, out gd, out under, name, int.MaxValue,
                s => mine.Contains(s) && !keep.Contains(s));
            sold += s2;
            goldIn += gd;
            if (w != null)
                parts.Add("selling the rest to " + name + ": " + w);
        }
        return string.Join("; ", parts);
    }
    // The made items sold to each smith in reach of the anvil whose stock list names armour, by name, 5 a sale at any
    // price; then back to the anvil. Null, or why the circuit ends.
    // The smiths in reach of the anvil to sell made items to (Damian, 2026-10-18: "not sure why we went to the tanner
    // shop to sell coifs", "we walked out to the mine smith instead of the closer ones in town"): a vendor buys what it
    // sells, so those whose stock names a made item, else those whose stock holds anything the hammer builds (Dillon
    // the tanner listed plate armour and refused the coifs); nearest by the planned walk from the player, a vendor no
    // walk reaches last (Ranard at the mine, 53 tiles straight, was taken before Minoc's forgery, 63).
    List<AtlasNpc> SmithBuyers(GameLink k, Set<int> made, int sx, int sy)
    {
        List<AtlasNpc> all = Atlas == null ? new List<AtlasNpc>()
            : Atlas.Shops().FindAll(n => n.Name.Length > 0 && Tiles(n.X, n.Y, sx, sy) <= CircuitReach
            && n.Items.Exists(s => SmithChain(SmithNorm(s)) != null));
        Set<string> names = Set<string>.Empty;
        foreach (int g in made)
            names = names.Add(SmithNorm(UoTiles.Name(g)));
        List<AtlasNpc> smiths = all.FindAll(n => n.Items.Exists(s => names.Contains(SmithNorm(s))));
        if (smiths.Count == 0)
            smiths = all;
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        ValueMap<AtlasNpc, int> walk = ValueMap<AtlasNpc, int>.Empty;
        foreach (AtlasNpc n in smiths)
        {
            List<int>? r = PlanPath(k, px, py, pz, n.X, n.Y, VendorReach, Set<long>.Empty);
            walk = walk.Set(n, r == null ? int.MaxValue : r.Count);
        }
        smiths.Sort((a, b) => walk.Get(a, 0) != walk.Get(b, 0) ? walk.Get(a, 0).CompareTo(walk.Get(b, 0))
            : Tiles(a.X, a.Y, sx, sy).CompareTo(Tiles(b.X, b.Y, sx, sy)));
        return smiths;
    }
    string? SmithSell(GameLink k, Set<int> made, int sx, int sy, ref int sold, ref int goldIn)
    {
        List<AtlasNpc> smiths = SmithBuyers(k, made, sx, sy);
        if (smiths.Count == 0)
        {
            Note("Smithing: no smith in reach is known to deal in armour; nothing sold");
            return null;
        }
        foreach (AtlasNpc v in smiths)
        {
            if (circuitStop || PackCount(k, g => made.Contains(g)) == 0)
                break;
            string name = v.Name.Split(' ')[0];
            int s, gd, under;
            CircuitStatus = "Smithing: selling to " + name;
            string? w = VendorSell(g => made.Contains(g), 1, out s, out gd, out under, name);
            sold += s;
            goldIn += gd;
            Note("Smithing: " + name + ": " + (w ?? "sold " + s + " for " + gd));
            if (Silent(w!))
                return "asking " + name + ": " + w;
        }
        string? back = WalkTo(sx, sy, 0);
        return back == null ? null : "back to the anvil: " + back;
    }
    // Ingots for the next build: from the city's bank box (the gold over the purse banked on the same visit), or bought
    // from a smith in reach whose stock lists iron ingots; then back to the anvil. Null, or why the circuit ends.
    // With smith.ingots mine, the mining circuit's storage is the source: an ingot stash in the bank box is the bank
    // box here, one in the house is walked to and lifted from, and the backpack and vendor storage leave the pack.
    string? SmithIngotsIn(GameLink k, string city, int need, int sx, int sy)
    {
        string from = SmithIngots, store = MineStorage;
        if (from == "mine")
            from = store == "stash" ? (StashInBank ? "bank" : "stash") : "pack";
        // Harvest (Damian, 2026-10-18: "add "run mining circuit" option to go harvest ingots first"): the mining
        // circuit run at the picked cave with the ingots kept in the pack until it is full, then back to the smithy's
        // town by the bank rune and walked to the anvil.
        if (from == "harvest")
        {
            if (PickedCave!.Length == 0)
                return "harvesting ingots needs a picked cave with its rune (the Cave mining setup)";
            if (city != null && PackBankRune(k) == 0)
                return "harvesting ingots needs the bank rune in the pack, to come back to " + city;
            CircuitStatus = "Smithing: to the cave " + PickedCave + " for ingots";
            string mw;
            m_mineStoreFor = "backpack";
            try
            {
                mw = RunMine(k);
            }
            finally { m_mineStoreFor = null; }
            Note("Smithing: mining for ingots: " + mw);
            if (circuitStop)
                return "stopped";
            if (city != null && CityOf(k) != city)
            {
                uint rune = PackBankRune(k);
                if (rune == 0)
                    return "no bank rune in the pack to come back to " + city;
                CircuitStatus = "Smithing: recalling to " + city;
                string? rw = Recall(rune);
                if (rw != null)
                    return "recalling to the smithy: " + rw;
                if (CityOf(k) != city)
                    return "the bank rune landed in " + (CityOf(k) ?? "no town") + ", not " + city;
            }
        }
        if (from == "pack")
        {
            int held = PackIngots(k);
            return held >= need ? null
                : "out of ingots: " + held + " in the pack, " + need
                + " wanted (the mining storage is the backpack or the vendor)";
        }
        if (from == "stash")
        {
            int[]? at;
            StashPile(k, out at);
            if (at == null)
                return "the mine's ingot stash is not set, or not in view: target it again";
            int fx = at[0], fy = at[1];
            if (Tiles(fx, fy, sx, sy) > CircuitReach)
                return "the mine's ingot stash at " + fx + "," + fy + " is " + Tiles(fx, fy, sx, sy)
                    + " tiles from the anvil";
            CircuitStatus = "Smithing: ingots from the mine's stash at " + fx + "," + fy;
            string? mw = WalkTo(fx, fy, 1);
            if (mw != null)
                return "walking to the mine's stash: " + mw;
            AssistItem? pile = StashPile(k, out at);
            if (pile == null && StashSerial != 0)
            {
                BusyWait(clickedAt);
                Peek(StashSerial);
                pile = StashPile(k, out at);
            }
            if (pile == null)
                return "no ingots in the mine's stash at " + fx + "," + fy;
            uint pack;
            lock (k.State.Gate)
                pack = k.State.Backpack;
            int before = PackIngots(k);
            using (Act(k))
            {
                mw = MoveStack(k, pile.Serial, Math.Min(Math.Max(1, pile.Amount), SmithIngotLoad), "the mine's ingots",
                    "Taking ingots", () => k.Drop(pile.Serial, pack)!);
                if (mw != null)
                    return mw;
            }
            if (PackIngots(k) <= before)
                return "the mine's ingots did not reach the pack";
            Note("Smithing: " + (PackIngots(k) - before) + " ingots from the mine's storage at " + fx + "," + fy);
        }
        else if (from == "buy")
        {
            List<AtlasNpc> sellers = Atlas == null ? new List<AtlasNpc>()
                : Atlas.Shops().FindAll(n => n.Name.Length > 0 && Tiles(n.X, n.Y, sx, sy) <= CircuitReach
                && n.Items.Exists(s => s.StartsWith("Iron ingot", StringComparison.OrdinalIgnoreCase)));
            sellers.Sort((a, b) => Tiles(a.X, a.Y, sx, sy).CompareTo(Tiles(b.X, b.Y, sx, sy)));
            foreach (AtlasNpc v in sellers)
            {
                if (circuitStop || PackIngots(k) >= need)
                    break;
                string name = v.Name.Split(' ')[0];
                int got, left;
                CircuitStatus = "Smithing: buying ingots from " + name;
                string? w = VendorBuy(g => g == IngotArt, SmithIngotLoad, out got, out left, name);
                Note("Smithing: ingots from " + name + ": " + (w ?? "bought " + got));
                if (Silent(w!))
                    return "asking " + name + ": " + w;
            }
        }
        else
        {
            if (city == null)
                return "not in a city with a bank, and ingots come from the bank";
            CircuitStatus = "Smithing: ingots from the bank";
            string? bw = BankHere(k, city);
            if (bw != null)
                return "the bank: " + bw;
            string taken;
            bw = BankTake(new List<int> { IngotArt }, SmithIngotLoad, out taken);
            if (bw != null)
                return "ingots from the bank: " + bw;
        }
        string? back = WalkTo(sx, sy, 0);
        if (back != null)
            return "back to the anvil: " + back;
        int have = PackIngots(k);
        return have >= need ? null
            : "out of ingots: " + have + " in the pack, " + need + " wanted (" + SmithIngots + ")";
    }
    string RunRefresh(GameLink k)
    {
        string? city = CityOf(k);
        if (city == null)
            return "not in a city";
        // The bank and the loadout first, then the food (Damian, 2026-10-18: "refresh should loadout first, then get
        // food"; the food shops refused a pack of 0 gold, 03:18:58 and 03:20:06, the purse coming only at the bank
        // after them). Horse food only while riding ("not buy horsefood unless mounted"; a recorded horse not ridden
        // sent it to the stables).
        List<string> said = new();
        bool mount = Riding(k);
        CircuitStatus = "Refresh: at the bank";
        {
            string? bw = BankHere(k, city);
            if (bw == null)
            {
                string gt;
                string? gw = PurseTo(PurseKeep, out gt);
                if (gw != null)
                    said.Add("the purse: " + gw);
            }
            said.Add((bw != null ? "bank: " + bw : RefreshLoadout(k, city))!);
        }
        if (circuitStop)
            return "stopped";
        string? fw = BuyFood(k, city, mount);
        if (fw != null)
        {
            if (Silent(fw) || fw == "stopped")
                return fw;
            said.Add("food: " + fw);
        }
        if (!circuitStop)
        {
            CircuitStatus = "Refresh: eating";
            said.Add(EatFull(k, mount));
        }
        if (mount && !circuitStop)
        {
            CircuitStatus = "Refresh: feeding the mount";
            said.Add(FeedMount(k) ?? "the mount fed");
        }
        RefreshStatus = string.Join("; ", said);
        Note("Refresh: " + RefreshStatus);
        return circuitStop ? "stopped" : "refresh done: " + RefreshStatus;
    }
    // Food from the city's food shops (the ticked ones when any are), provisioners first, then farms and markets, then
    // stables, each kind nearest first: HorseFeed apples or pears for the mount, and for the player the food of the
    // most satiety a keeper stocks, enough for FullSatiety. Null once both are in the pack, else what is short.
    string? BuyFood(GameLink k, string city, bool mount)
    {
        Func<int> horseWant = () => mount
            ? Math.Max(0, HorseFeed - PackCount(k, g => s_horseFood.IndexOf(g) >= 0)) : 0;
        if (horseWant() == 0 && SelfFood(k, mount) >= FullSatiety)
            return null;
        List<UoRegions.Shop> shops = CircuitShops("refresh").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
        if (shops.Count == 0)
            return "no food shop in " + city;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        Func<UoRegions.Shop, int> rank = s => { string? kind = UoRegions.ShopKind(s.CX, s.CY); int r = kind == null ? -1
            : s_foodShopKinds.IndexOf(kind.ToUpperInvariant()); return r < 0 ? s_foodShopKinds.Length : r; };
        shops.Sort((a, b) => rank(a) != rank(b) ? rank(a).CompareTo(rank(b))
            : Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
        foreach (UoRegions.Shop shop in shops)
        {
            if (circuitStop)
                return "stopped";
            if (horseWant() == 0 && SelfFood(k, mount) >= FullSatiety)
                break;
            int qx, qy;
            lock (k.State.Gate)
            {
                qx = k.State.X;
                qy = k.State.Y;
            }
            CircuitStatus = "Refresh: walking into " + UoRegions.Display(shop);
            string? w = shop.Holds(qx, qy, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
            if (w != null)
            {
                Note("Refresh: walking into " + UoRegions.Display(shop) + ": " + w);
                continue;
            }
            NameUnnamed(VendorHear * 4);
            List<AtlasNpc> keepers = Atlas == null ? new List<AtlasNpc>()
                : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2)
                && (Clock.UtcNow.ToLocalTime() - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
            foreach (AtlasNpc v in keepers)
            {
                if (circuitStop)
                    return "stopped";
                int hw = horseWant();
                bool selfShort = SelfFood(k, mount) < FullSatiety;
                if (hw == 0 && !selfShort)
                    break;
                string name = v.Name.Split(' ')[0];
                int got, left;
                // The first ask reads the stock list, and buys the mount's fruit from it.
                CircuitStatus = "Refresh: food from " + name + " in " + UoRegions.Display(shop);
                string? bw = VendorBuy(g => hw > 0 && s_horseFood.IndexOf(g) >= 0, hw, out got, out left, name);
                if (Silent(bw!))
                    return "asking " + name + ": " + bw;
                if (bw != null && bw.StartsWith("away: "))
                {
                    Note("Refresh: " + name + ": " + bw);
                    continue;
                }
                if (!selfShort)
                    continue;
                int best = BestFood(k, v.Serial);
                if (best == 0)
                    continue;
                int each = s_satiety.Get(best, 1), want = (FullSatiety - SelfFood(k, mount) + each - 1) / each;
                bw = VendorBuy(g => g == best, want, out got, out left, name);
                if (Silent(bw!))
                    return "asking " + name + ": " + bw;
                if (bw != null)
                    Note("Refresh: " + name + ": " + bw);
            }
        }
        return horseWant() > 0 ? "no apples or pears found in " + city + "'s food shops"
            : SelfFood(k, mount) < FullSatiety ? "not enough food for full found in " + city : null;
    }
    // Eats the pack's food of the most satiety, one at a time, until the server says full ("too full" or "stuffed");
    // an eat with no answer ends it (L-FUTILITY). What happened, for the report.
    string EatFull(GameLink k, bool mount)
    {
        int eaten = 0;
        for (int i = 0; i < EatMax && !circuitStop && !k.Closed; i++)
        {
            AssistItem? food = null;
            int fs = 0;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                {
                    int s;
                    if (!inside.Contains(it.Container) || !s_satiety.TryGet(it.Graphic, out s))
                        continue;
                    if (mount && s_horseFood.IndexOf(it.Graphic) >= 0
                        && PackCount(k, g => g == it.Graphic) <= HorseFeed)
                        continue;
                    if (s > fs)
                    {
                        fs = s;
                        food = it;
                    }
                }
            }
            if (food == null)
                return eaten == 0 ? "no food in the pack to eat"
                    : "ate " + eaten + ", then the food ran out before full";
            Heard mark;
            using (Act(k))
            {
                mark = new(k.State);
                string? why = k.Use(food.Serial, false);
                if (why != null)
                    return "eating: " + why;
            }
            string? line = null;
            Wait.Until(Clock,
                () => { foreach (string j in mark.Answers()) if (s_eatLines.Exists(e =>
                j.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)) line = j; return line != null; },
                EatAnswerMs, "the eating's answer", stepMs: 50);
            if (line == null)
                return "no answer to eating the " + UoTiles.Name(food.Graphic)
                    + (eaten > 0 ? " after " + eaten + " eaten" : "");
            if (line.IndexOf("can't eat", StringComparison.OrdinalIgnoreCase) >= 0)
                return "the server said \"" + line + "\"";
            if (line.IndexOf("too full", StringComparison.OrdinalIgnoreCase) >= 0)
                return "full (" + eaten + " eaten)";
            eaten++;
            if (line.IndexOf("stuffed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "full (" + eaten + " eaten)";
            Clock.Sleep(Ms("busy:eat"));
        }
        return "ate " + eaten;
    }
    // The mount handed HorseFeed apples or pears: dismounted first when ridden, and mounted again after "Your pet looks
    // happier." Null when fed, else why not.
    string? FeedMount(GameLink k)
    {
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        bool riding = Riding(k);
        uint horse = MountSerial();
        AssistItem? fruit = null;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && s_horseFood.IndexOf(it.Graphic) >= 0
                    && (fruit == null || it.Amount > fruit.Amount))
                    fruit = it;
        }
        if (fruit == null)
            return "no apples or pears for the mount";
        Map<uint, int[]> before = k.State.Mobiles;
        if (riding)
        {
            // The last bite leaves the player busy: a dismount 187 ms after it was answered "You must wait to perform
            // another action." and dropped (uo1998 2026-10-18 08:25:51).
            Clock.Sleep(Ms("busy:eat"));
            using (Act(k))
            {
                string? why = k.Use(me, false);
                if (why != null)
                    return "dismounting: " + why;
            }
            Wait.Until(Clock, () => !Riding(k), MountWaitMs, "the dismount", stepMs: 50);
            if (Riding(k))
                return "the dismount did not happen";
        }
        // The mount in view: the one known, else the mobile that came into view beside the player at the dismount.
        DateTime seenBy = Clock.UtcNow.AddMilliseconds(MountWaitMs);
        bool inView = false;
        while (!inView && Clock.UtcNow < seenBy)
        {
            lock (k.State.Gate)
            {
                if (horse != 0 && k.State.Mobiles.ContainsKey(horse))
                    inView = true;
                else
                    foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                        if (!before.ContainsKey(kv.Key) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= 2)
                        {
                            horse = kv.Key;
                            inView = true;
                            break;
                        }
            }
            if (!inView)
                Clock.Sleep(50);
        }
        if (!inView)
            return "the mount is not in view";
        using Heard heard = new(k.State);
        string? w;
        using (Act(k))
        {
            int take = Math.Min(HorseFeed, Math.Max(1, fruit.Amount));
            w = Lifted(k, fruit.Serial, take);
            if (w == null)
                w = k.Drop(fruit.Serial, horse);
        }
        bool happy = false;
        if (w == null)
            happy = Wait.Until(Clock, () => heard.Has("looks happier"), EatAnswerMs, "the feeding", stepMs: 50).Done;
        if (riding)
        {
            using (Act(k))
            {
                k.Use(horse, false);
            }
            Wait.Until(Clock, () => Riding(k), MountWaitMs, "the mount", stepMs: 50);
            if (!Riding(k))
                Note("Refresh: mounting again did not happen; the mount waits beside you");
        }
        return w != null ? "feeding the mount: " + w : happy ? null : "the mount took no food (no \"looks happier\")";
    }
    // The loadout bag refilled from the storage bag in the open bank box: the two bags of the last loadout picked by
    // hand. With no storage bag set, RefreshFromBank.
    // Not again inside its own bank visit, nor within LoadoutAgainMs of the last (a circuit's own refill after a bank
    // stop that already refilled on the reagent warning).
    public const int LoadoutAgainMs = 60000;
    bool m_loadoutRunning; DateTime m_loadoutAt = DateTime.MinValue;
    string? RefreshLoadout(GameLink k, string city)
    {
        if (m_loadoutRunning)
            return null;
        double since = (Clock.UtcNow - m_loadoutAt).TotalMilliseconds;
        if (since < LoadoutAgainMs)
            return "loadout: refilled " + (int)(since / 1000) + " s ago";
        m_loadoutRunning = true;
        try
        {
            return RefreshLoadoutOnce(k, city);
        }
        finally { m_loadoutRunning = false; m_loadoutAt = Clock.UtcNow; }
    }
    string RefreshLoadoutOnce(GameLink k, string city)
    {
        uint bag, storage;
        bool hasBag = uint.TryParse(Cfg.Get("loadout.bag", ""), NumberStyles.HexNumber, null, out bag);
        if (!uint.TryParse(Cfg.Get("loadout.storage", ""), NumberStyles.HexNumber, null, out storage))
            return RefreshFromBank(k, city, hasBag ? bag : 0);
        if (!hasBag)
            return "loadout: pick the loadout bag and the storage bag once by hand (Pricer tab, Reagent loadout)";
        bool there;
        lock (k.State.Gate)
            there = k.State.Items.ContainsKey(bag) && k.State.Items.ContainsKey(storage);
        if (!there)
            return "loadout: the loadout bag " + bag.ToString("X") + " or the storage bag " + storage.ToString("X")
                + " is not in the pack or the open bank box";
        CircuitStatus = "Refresh: refilling the loadout";
        LoadoutRun(k, bag, storage);
        return "loadout: " + LoadoutStatus;
    }
    public string LoadoutNoStorage()
    {
        Cfg.Set("loadout.storage", "");
        return "Refresh refills the loadout from every bag in the bank box, and buys what the bank lacks.";
    }
    // A reagent's highest price on a shortfall buy: the shopping list's when it names the reagent, else twice RunUO's
    // SBMage price (5 for pearl and bloodmoss, 3 for the rest), as a depleted stock charges more (4 against a listed 3,
    // Jannelle's thread, 2026-10-17).
    int LoadoutPrice(int g)
    {
        foreach (BuyItem i in BuyList())
            if (Array.IndexOf(i.Arts, g) >= 0)
                return i.MaxPrice;
        return g == 0xF7A || g == 0xF7B ? 10 : 6;
    }
    // With no storage bag set (Damian, 2026-10-18): the loadout's reagents taken from every bag in the open bank box;
    // what the bank lacks bought in the city's mage shops (the buy circuit's picked ones here, else every one here),
    // exactly the shortfall, with gold from the box's own piles; then the change banked and the reagents into the
    // loadout bag.
    string RefreshFromBank(GameLink k, string city, uint bag)
    {
        if (bag != 0)
        {
            bool inPack;
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(bag);
            if (!inPack)
                bag = 0;
        }
        List<int> arts = new();
        foreach (int g in LoadoutReagents())
            if (LoadoutWant(g) > 0)
                arts.Add(g);
        CircuitStatus = "Refresh: the loadout from the bank";
        string taken;
        string? w = BankTake(arts, LoadoutWant, out taken);
        if (w != null)
            return "loadout from the bank: " + w;
        List<string> said = new()
        { "from the bank " + taken };
        List<BuyItem> list = new();
        ValueMap<string, int> left = ValueMap<string, int>.Empty;
        int cost = 0;
        foreach (int g in arts)
        {
            int s = LoadoutWant(g) - PackCount(k, x => x == g);
            if (s <= 0)
                continue;
            BuyItem i = new()
            {
                Name = ReagentName(g),
                Arts = new[] { g },
                Count = s,
                MaxPrice = LoadoutPrice(g)
            };
            list.Add(i);
            left = left.Set(i.Name!, s);
            cost += s * i.MaxPrice;
        }
        if (list.Count > 0 && !circuitStop)
        {
            w = PurseTo(PurseKeep + cost, out taken);
            if (w != null)
                return string.Join("; ", said) + "; gold from the bank: " + w;
            List<UoRegions.Shop> shops = CircuitShops("buy").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
            if (shops.Count == 0)
                shops = AllShops("buy").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            int bought = 0;
            string? end = shops.Count == 0 ? "no mage shop in " + city : null;
            foreach (UoRegions.Shop shop in shops)
            {
                if (circuitStop || !list.Exists(i => left.Get(i.Name!, 0) > 0))
                    break;
                end = BuyInShop(k, shop, list, ref left, ref bought);
                if (end != null)
                    break;
            }
            List<string> still = new();
            foreach (BuyItem i in list)
                if (left.Get(i.Name!, 0) > 0)
                    still.Add(left.Get(i.Name!, 0) + " " + i.Name);
            said.Add("bought " + bought + (still.Count > 0 ? ", still short " + string.Join(", ", still) : "")
                + (end != null ? " (" + end + ")" : ""));
            if (!circuitStop)
            {
                CircuitStatus = "Refresh: banking the change";
                string? bw = BankHere(k, city);
                if (bw != null)
                    said.Add("the change: " + bw);
            }
        }
        if (bag != 0 && !circuitStop)
        {
            int moved;
            w = PutInto(arts, bag, out moved);
            if (w != null)
                said.Add("into the loadout bag: " + w);
        }
        return "loadout: " + string.Join("; ", said);
    }
    // final: where the circuit ends, everything bought goes into the bank, none kept back for travel, and the purse
    // drops to PurseKeep (Damian, 2026-10-17: "the ending should have included putting the pearl in the bank bag, and
    // dropping back to 300gp rest purse").
    // purseTo: the purse left at the end, gold only deposited down to it, never taken (homespun's way home); -1 the buy
    // circuit's.
    string? BuyBank(GameLink k, string? city, bool final = false, List<BuyItem>? goods = null, int purseTo = -1)
    {
        city = ToBankTown(k, city!);
        if (city == null)
            return NoBankTown;
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        List<int[]> banks = CityBanks(city, x, y);
        int[]? bank = null;
        string? why = null;
        foreach (int[] b in banks)
        {
            if (circuitStop)
                return "stopped";
            if (BankSkipped(b))
                continue;
            // ToBanker returns null only with the box open; a distance check from the region's centre after it threw an
            // open box away and walked to Britain's other bank, the gold and goods left in the pack (2026-10-18 12:48).
            CircuitStatus = "Buy: walking to the bank in " + city;
            string? w = ToBanker(k, b);
            if (w != null)
            {
                SkipBank(b);
                why = "walking to the bank: " + w;
                continue;
            }
            bank = b;
            FailOk("bank " + b[0] + "," + b[1]);
            break;
        }
        if (banks.Count > 0 && bank == null)
            return why ?? "every bank in " + city + " failed";
        byte[]? open;
        using (Act(k))
        {
            open = SayBank(k);
        }
        if (open == null)
        {
            if (bank != null)
                SkipBank(bank);
            return NoBanker();
        }
        if (bank != null)
            BankHeard(bank);
        uint box = BankBox(open);
        Set<uint> tree = BankTree(k, box, false);
        Set<int> arts = Set<int>.Empty;
        foreach (BuyItem i in goods ?? BuyList())
            foreach (int a in i.Arts)
                arts = arts.Add(a);
        // The store: the container in the box holding the most of the list's goods.
        uint store = box;
        int most = 0;
        lock (k.State.Gate)
        {
            ValueMap<uint, int> units = ValueMap<uint, int>.Empty;
            foreach (AssistItem it in k.State.Items.Values)
                if (tree.Contains(it.Container) && it.Container != box && arts.Contains(it.Graphic))
                    units = units.Set(it.Container, units.Get(it.Container, 0) + Math.Max(1, it.Amount));
            foreach (KeyValuePair<uint, int> kv in units)
                if (kv.Value > most)
                {
                    most = kv.Value;
                    store = kv.Key;
                }
            // The bank store picked on the Circuits tab, while it lies in the box.
            foreach (uint bs in BankBags("bankstore"))
                if (tree.Contains(bs))
                {
                    store = bs;
                    break;
                }
        }
        List<UoTiles.Tile> tiles = UoTiles.Items();
        using (Act(k))
        {
            foreach (int art in arts)
            {
                int keep = !final && s_recallReagents.IndexOf(art) >= 0 ? ReagentRefill : 0;
                bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
                for (int round = 0; round < 20 && !circuitStop; round++)
                {
                    int have = PackCount(k, g => g == art);
                    if (have <= keep)
                        break;
                    AssistItem? stack = null, onto = null;
                    lock (k.State.Gate)
                    {
                        Set<uint> inside = PackTree(k);
                        foreach (AssistItem it in k.State.Items.Values)
                        {
                            if (it.Graphic != art)
                                continue;
                            if (inside.Contains(it.Container) && (stack == null || it.Amount > stack.Amount))
                                stack = it;
                            else if (it.Container == store && stacks && (onto == null || it.Amount > onto.Amount))
                                onto = it;
                        }
                    }
                    if (stack == null)
                        break;
                    int amount = Math.Min(Math.Max(1, stack.Amount), have - keep);
                    string? w = MoveStack(k, stack.Serial, amount, UoTiles.Name(art), "Storing " + UoTiles.Name(art),
                        () => k.Drop(stack.Serial, onto != null ? onto.Serial : store)!);
                    if (w != null)
                        return w;
                    if (PackCount(k, g => g == art) >= have)
                        return UoTiles.Name(art) + " did not leave the pack";
                }
            }
        }
        int stored = 0;
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == store && arts.Contains(it.Graphic))
                    stored += Math.Max(1, it.Amount);
        Note("Buy: the bank in " + city + " holds " + stored + " of the list's goods in " + store.ToString("X"));
        int purse = PackCount(k, g => g == GoldArt), target = purseTo >= 0 ? purseTo : final ? PurseKeep : BuyPurse;
        if (purse > target)
        {
            int put;
            string? w = BankDeposit(target, out put);
            if (w != null)
                return "depositing: " + w;
        }
        else if (!final && purseTo < 0 && purse < BuyPurse)
        {
            string taken;
            string? w = BankTake(new List<int> { GoldArt }, BuyPurse, out taken);
            if (w != null)
                return "gold from the bank: " + w;
            if (PackCount(k, g => g == GoldArt) == 0)
                return BankGoldNone;
        }
        return null;
    }
}
