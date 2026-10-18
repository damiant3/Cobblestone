// The NPC atlas (Damian, 2026-10-15: "map should update to show shop keeps, bankers, other npcs. add filters by type and
// subtype. show regions on the map, and drill down for specific npc item lists when the vendor buy list comes up", "add a
// shopkeeps and merchants as a main tab"). Every mobile seen, typed by the title in its name ("Bob the banker"): bankers,
// shopkeepers, healers, guards and other titled NPCs are kept per server in atlas-<server>.txt; people, ghosts and
// creatures only while seen. A vendor's buy list (0x74) is kept on the vendor.
// No using directives: assist.ps1 compiles this with Assist.cs as one unit.

public sealed class AtlasNpc {
    public uint Serial; public string Name = "", Type = "", Sub = ""; public int Body, X, Y; public bool Criminal, ShopPack;
    public System.DateTime Last, ItemsAt; public System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
    public bool Kept { get { return Type == "Banker" || Type == "Shopkeeper" || Type == "Player vendor" || Type == "Healer" || Type == "Guard" || Type == "NPC"; } }
}

// Houses, guildstones and trades (Damian, 2026-10-15: "record all transactions, buy and sell with each npc vendor and also
// keep track of player vendors and houses, showing house decay level and any other details. if you see guildstones,
// record them and show them on the map").
public sealed class AtlasPlace { public uint Serial; public string Kind = "", Name = "", Details = ""; public int Graphic, X, Y; public System.DateTime Last; }
public sealed class AtlasTrade { public System.DateTime At; public string Kind = "", Vendor = "", Item = ""; public uint VendorSerial; public int X, Y, Amount, Price; }

// Smithing value (Damian, 2026-10-15: "calculate the best ingot efficient item to make and sell the npcs based on smithy
// skill ... remember sales and how many and what prices. note that prices change based on quality"): ingot costs from the
// smith's menus, each build with its quality and the skill then, each NPC offer and sale with the item's quality, kept in
// smithing-<server>.txt beside the atlas (tab-separated, one record a line, first field the kind).
// The same book serves tailoring (Damian, 2026-10-16: "a \"best items to make\" for tailor like smithy, that is context
// aware of char skill and prices they sell at ... i sometimes sell the vendor down and i can't get all the sales i need
// there"), its cost in cloth from the tailor's menus ("Fancy shirt - takes 8 cloth."), kept in tailoring-<server>.txt.
// Demand: uo1998 kept fancy shirts at 27 gp at every vendor (trades 2026-10-16: Nana 60, Morena 36, Ella 24, Lynn 9
// sold), so selling a vendor down is its sell list leaving items out, not a lower price. Each sell list records, per item
// the player carries, how many the vendor offered to buy against how many were carried; the latest within DemandHours
// per vendor is that vendor's demand.
public sealed class SmithRow { public string Item; public int Cost, Builds, Exceptional, Failed, PriceNormal, PriceExceptional, Sold, SoldGold, Wanted, Full; public double PerIngot; public string Basis, Demand = ""; }
public sealed class SmithBook {
    readonly string path; readonly object gate = new object();
    public const int DemandHours = 24;
    readonly System.Collections.Generic.Dictionary<string, string[]> wants = new System.Collections.Generic.Dictionary<string, string[]>();  // vendor|key: ticks, vendor, key, offered, carried
    readonly System.Collections.Generic.Dictionary<string, int> costs = new System.Collections.Generic.Dictionary<string, int>();
    readonly System.Collections.Generic.Dictionary<string, string> names = new System.Collections.Generic.Dictionary<string, string>();
    readonly System.Collections.Generic.List<string[]> crafts = new System.Collections.Generic.List<string[]>();  // ticks, serial, key, quality, skill
    readonly System.Collections.Generic.List<string[]> sales = new System.Collections.Generic.List<string[]>();   // ticks, vendor, key, quality, amount, price
    readonly System.Collections.Generic.Dictionary<string, int> offers = new System.Collections.Generic.Dictionary<string, int>();  // key|quality, latest price
    readonly System.Collections.Generic.Dictionary<uint, string> quality = new System.Collections.Generic.Dictionary<uint, string>();
    public const int NearSkill = 50, NearBuildsMin = 3;
    public SmithBook(string path) {
        this.path = path;
        try { if (System.IO.File.Exists(path)) foreach (var line in System.IO.File.ReadAllLines(path)) Apply(line.Split('\t')); } catch { }
    }
    // "Build Platemail legs, 20 metal." and "platemail legs" name the same item, as "Fancy shirt - takes 8 cloth." and
    // "fancy shirt" do.
    static string Bare(string name) {
        string s = name.Trim().TrimEnd('.'); if (s.StartsWith("Build ", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
        int c = s.IndexOf(','); if (c >= 0) s = s.Substring(0, c);
        c = s.IndexOf(" - "); if (c >= 0) s = s.Substring(0, c);
        return s.Trim();
    }
    public static string Key(string name) {
        string s = Bare(name);
        s = string.Join(" ", s.ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("a ")) s = s.Substring(2); else if (s.StartsWith("an ")) s = s.Substring(3);
        if (s.EndsWith("s") && s.Length > 3) s = s.Substring(0, s.Length - 1);
        return s;
    }
    public static string Display(string name) { return Bare(name); }
    void Apply(string[] t) {
        if (t.Length < 2) return; int n; uint u;
        switch (t[0]) {
            case "cost": if (t.Length >= 4 && int.TryParse(t[3], out n)) { costs[t[1]] = n; names[t[1]] = t[2]; } break;
            case "craft": if (t.Length >= 6) { crafts.Add(new[] { t[1], t[2], t[3], t[4], t[5] }); if (uint.TryParse(t[2], System.Globalization.NumberStyles.HexNumber, null, out u)) quality[u] = t[4]; } break;
            case "sale": if (t.Length >= 7) sales.Add(new[] { t[1], t[2], t[3], t[4], t[5], t[6] }); break;
            case "offer": if (t.Length >= 5 && int.TryParse(t[4], out n)) offers[t[2] + "|" + t[3]] = n; break;
            case "want": if (t.Length >= 6) wants[t[2] + "|" + t[3]] = new[] { t[1], t[2], t[3], t[4], t[5] }; break;
        }
    }
    // A vendor's latest demand for an item, as the Tailoring value shows it ("15 of 20", "4+"); empty when none within
    // DemandHours.
    public string WantAt(string vendor, string item) {
        string[] w; lock (gate) { if (!wants.TryGetValue(vendor + "|" + Key(item), out w)) return ""; }
        long ticks; int off, car; if (!long.TryParse(w[0], out ticks) || !int.TryParse(w[3], out off) || !int.TryParse(w[4], out car)) return "";
        if ((System.DateTime.Now - new System.DateTime(ticks)).TotalHours > DemandHours) return "";
        return off < car ? off + " of " + car : off + "+";
    }
    // A vendor's sell list offered to buy this many of an item the player carried this many of.
    public void Want(string vendor, string item, int offered, int carried) {
        string key = Key(item); string[] had;
        lock (gate) { if (wants.TryGetValue(vendor + "|" + key, out had) && had[3] == offered.ToString() && had[4] == carried.ToString() && (System.DateTime.Now - new System.DateTime(long.Parse(had[0]))).TotalHours < 1) return; }
        Add("want", System.DateTime.Now.Ticks.ToString(), vendor, key, offered.ToString(), carried.ToString());
    }
    void Add(params string[] t) {
        for (int i = 0; i < t.Length; i++) t[i] = t[i].Replace('\t', ' ');
        lock (gate) { Apply(t); try { System.IO.File.AppendAllText(path, string.Join("\t", t) + "\r\n"); } catch { } }
    }
    public void Cost(string menuText, int metal) {
        string key = Key(menuText); int had; lock (gate) { if (costs.TryGetValue(key, out had) && had == metal) return; }
        Add("cost", key, Display(menuText), metal.ToString());
    }
    public int CostOf(string name) { int n; lock (gate) return costs.TryGetValue(Key(name), out n) ? n : 0; }
    public void Crafted(uint serial, string item, string q, int skill) { Add("craft", System.DateTime.Now.Ticks.ToString(), serial.ToString("X"), Key(item), q, skill.ToString()); }
    public string QualityOf(uint serial) { string q; lock (gate) return quality.TryGetValue(serial, out q) ? q : null; }
    public void Offer(string item, string q, int price) { int had; lock (gate) { if (offers.TryGetValue(Key(item) + "|" + q, out had) && had == price) return; } Add("offer", System.DateTime.Now.Ticks.ToString(), Key(item), q, price.ToString()); }
    public void Sold(string vendor, string item, string q, int amount, int price) { Add("sale", System.DateTime.Now.Ticks.ToString(), vendor, Key(item), q, amount.ToString(), price.ToString()); }
    // Exceptional against everything else (average and below sell together here); a failure is a build that gave nothing.
    static bool Exc(string q) { return q == "exceptional"; }
    public System.Collections.Generic.List<SmithRow> Rows(int skill) {
        var rows = new System.Collections.Generic.List<SmithRow>();
        lock (gate) foreach (var kv in costs) {
            var r = new SmithRow { Item = names[kv.Key], Cost = kv.Value };
            var mine = crafts.FindAll(c => c[2] == kv.Key); var near = mine.FindAll(c => { int s; return int.TryParse(c[4], out s) && System.Math.Abs(s - skill) <= NearSkill; });
            var used = near.Count >= NearBuildsMin ? near : mine;
            r.Builds = used.Count; foreach (var c in used) { if (c[3] == "failed") r.Failed++; else if (Exc(c[3])) r.Exceptional++; }
            r.Basis = used.Count == 0 ? "no builds recorded" : used.Count + " builds " + (used == near ? "within 5.0 of your skill" : "at any skill");
            long sumN = 0, sumE = 0; int cntN = 0, cntE = 0;
            foreach (var s in sales) if (s[2] == kv.Key) { int a, p; if (!int.TryParse(s[4], out a) || !int.TryParse(s[5], out p)) continue; r.Sold += a; r.SoldGold += a * p; if (Exc(s[3])) { sumE += (long)a * p; cntE += a; } else { sumN += (long)a * p; cntN += a; } }
            int o;
            r.PriceNormal = cntN > 0 ? (int)(sumN / cntN) : offers.TryGetValue(kv.Key + "|average", out o) ? o : offers.TryGetValue(kv.Key + "|below average", out o) ? o : offers.TryGetValue(kv.Key + "|unknown", out o) ? o : 0;
            r.PriceExceptional = cntE > 0 ? (int)(sumE / cntE) : offers.TryGetValue(kv.Key + "|exceptional", out o) ? o : 0;
            int pn = r.PriceNormal > 0 ? r.PriceNormal : r.PriceExceptional, pe = r.PriceExceptional > 0 ? r.PriceExceptional : r.PriceNormal;
            double perBuild = r.Builds == 0 ? pn : ((double)r.Exceptional * pe + (double)(r.Builds - r.Exceptional - r.Failed) * pn) / r.Builds;
            r.PerIngot = r.Cost > 0 ? perBuild / r.Cost : 0;
            // Demand: each vendor's latest sell list within DemandHours. A vendor that offered fewer than were carried is
            // full at that count ("Nana 15 of 20"); one that took all it was shown wants at least that many ("Nana 15+").
            var parts = new System.Collections.Generic.List<string>();
            foreach (var w in wants.Values) {
                if (w[2] != kv.Key) continue; long ticks; int off, car;
                if (!long.TryParse(w[0], out ticks) || !int.TryParse(w[3], out off) || !int.TryParse(w[4], out car)) continue;
                if ((System.DateTime.Now - new System.DateTime(ticks)).TotalHours > DemandHours) continue;
                r.Wanted += off; if (off < car) r.Full++;
                parts.Add(w[1] + " " + off + (off < car ? " of " + car : "+"));
            }
            r.Demand = string.Join(", ", parts);
            rows.Add(r);
        }
        rows.Sort((a, b) => b.PerIngot.CompareTo(a.PerIngot));
        return rows;
    }
}

public sealed class AssistAtlas {
    public static readonly string[] Types = { "Banker", "Shopkeeper", "Player vendor", "Healer", "Guard", "NPC", "Person", "Ghost", "Creature", "House", "Guildstone", "Anvil", "Forge" };
    // 1998 shop titles (OUO templates and the shard's vendor roles); a title holding one of these is a shopkeeper.
    static readonly string[] ShopTitles = {
        "provisioner", "blacksmith", "armourer", "armorer", "weaponsmith", "weapon", "tailor", "weaver", "baker", "butcher", "cook",
        "tavern", "innkeeper", "barkeep", "bartender", "bowyer", "fletcher", "carpenter", "mage", "alchemist", "herbalist",
        "jewel", "scribe", "tinker", "mapmaker", "shipwright", "stablemaster", "animal trainer", "fur", "fisherman", "farmer",
        "beekeeper", "gypsy", "veterinarian", "waiter", "architect", "realtor", "estate", "leather", "tanner", "glassblower",
        "merchant", "vendor", "shopkeeper", "potter", "cobbler", "jeweler", "smith", "trader", "seller", "dealer", "minter",
    };
    public const int PeopleShownMs = 60000, KeepMax = 20000, StockJoinMs = 5000;
    // The vendor whose buy list came in last, and a count of buy lists, for the Shops tab to follow.
    public volatile uint Stocked; public long StockedSeq;
    readonly string path; readonly System.Collections.Generic.Dictionary<uint, AtlasNpc> npcs = new System.Collections.Generic.Dictionary<uint, AtlasNpc>();
    bool dirty;

    public AssistAtlas(string path) {
        this.path = path;
        try {
            if (System.IO.File.Exists(path)) foreach (var line in System.IO.File.ReadAllLines(path)) {
                var t = line.Split('\t'); if (t.Length < 9) continue;
                var n = new AtlasNpc { Serial = System.Convert.ToUInt32(t[0], 16), Type = t[1], Sub = t[2], Name = t[3], Body = int.Parse(t[4]), X = int.Parse(t[5]), Y = int.Parse(t[6]), Last = new System.DateTime(long.Parse(t[7])) };
                if (t[8].Length > 0) n.Items.AddRange(t[8].Split('|'));
                if (t.Length > 9) n.ItemsAt = new System.DateTime(long.Parse(t[9]));
                n.ShopPack = n.Type == "Shopkeeper";
                npcs[n.Serial] = n;
            }
        } catch { }
    }

    public static void Classify(string name, int body, out string type, out string sub) {
        if (body < 0x190 || body > 0x193) { type = "Creature"; sub = name.Length > 0 ? name.ToLowerInvariant() : "body 0x" + body.ToString("X3"); return; }
        if (body >= 0x192) { type = "Ghost"; sub = "ghost"; return; }
        int at = name.LastIndexOf(" the ", System.StringComparison.OrdinalIgnoreCase);
        if (at < 0) { type = "Person"; sub = name.Length > 0 ? "untitled" : "unnamed"; return; }
        string title = name.Substring(at + 5).Trim().ToLowerInvariant(); sub = title;
        if (title == "vendor") type = "Player vendor";  // a player's hired vendor is titled "the vendor" (RunUO PlayerVendor)
        else if (title.Contains("banker")) type = "Banker";
        else if (title.Contains("healer")) type = "Healer";
        else if (title.Contains("guard")) type = "Guard";
        else { type = "NPC"; foreach (var s in ShopTitles) if (title.Contains(s)) { type = "Shopkeeper"; break; } }
    }

    public void Seen(uint s, int body, int x, int y, string name, bool criminal) {
        lock (npcs) {
            AtlasNpc n; if (!npcs.TryGetValue(s, out n)) { if (npcs.Count >= KeepMax) return; npcs[s] = n = new AtlasNpc { Serial = s }; }
            n.Body = body; n.X = x; n.Y = y; n.Last = System.DateTime.Now; n.Criminal = criminal;
            if (name.Length > 0 && n.Name.IndexOf(" the ", System.StringComparison.OrdinalIgnoreCase) < 0) n.Name = name;
            string was = n.Type + n.Sub; Reclass(n);
            if (n.Kept || was != n.Type + n.Sub) dirty = true;
        }
    }
    public void Named(uint s, string name) {
        lock (npcs) {
            AtlasNpc n; if (!npcs.TryGetValue(s, out n) || name.Length == 0 || n.Name == name) return;
            n.Name = name; Reclass(n); dirty = true;
        }
    }
    public void NamedIfNone(uint s, string name) { lock (npcs) { AtlasNpc n; if (npcs.TryGetValue(s, out n) && n.Name.Length > 0) return; } Named(s, name); }
    // A human with no name, or a name without a title (speech gives the bare name), is clicked once for its label.
    public bool NeedsName(uint s) { lock (npcs) { AtlasNpc n; return npcs.TryGetValue(s, out n) && n.Body >= 0x190 && n.Body <= 0x191 && n.Name.IndexOf(" the ", System.StringComparison.OrdinalIgnoreCase) < 0; } }
    public void ShopPack(uint s) {
        lock (npcs) { AtlasNpc n; if (!npcs.TryGetValue(s, out n) || n.ShopPack) return; n.ShopPack = true; Reclass(n); dirty = true; }
    }
    // A mobile that showed a buy list or wears a shop pack is a shopkeeper whatever its name says.
    static void Reclass(AtlasNpc n) { string type, sub; Classify(n.Name, n.Body, out type, out sub); if ((n.Items.Count > 0 || n.ShopPack) && (type == "Person" || type == "NPC")) { type = "Shopkeeper"; if (sub == "untitled" || sub == "unnamed") sub = "vendor"; } n.Type = type; n.Sub = sub; }
    public void Stock(uint vendor, System.Collections.Generic.List<string> items) {
        // One "vendor buy" sends a list per shop pack (uo1998: the 0x1A stock, then the 0x1B resale, often empty): an empty
        // list changes nothing, and a list within StockJoinMs of the last one joins it.
        if (items.Count == 0) return;
        lock (npcs) {
            AtlasNpc n; if (!npcs.TryGetValue(vendor, out n)) return; var now = System.DateTime.Now;
            if ((now - n.ItemsAt).TotalMilliseconds < StockJoinMs) n.Items.AddRange(items); else n.Items = items;
            n.ItemsAt = now; Reclass(n); dirty = true; Stocked = vendor; StockedSeq++;
        }
    }
    // Every kept NPC, and everyone else seen in the last PeopleShownMs; copies.
    public System.Collections.Generic.List<AtlasNpc> Visible() {
        var list = new System.Collections.Generic.List<AtlasNpc>(); var now = System.DateTime.Now;
        lock (npcs) foreach (var n in npcs.Values) if (n.Kept || (now - n.Last).TotalMilliseconds < PeopleShownMs) list.Add(Copy(n));
        return list;
    }
    public System.Collections.Generic.List<AtlasNpc> Shops() {
        var list = new System.Collections.Generic.List<AtlasNpc>();
        lock (npcs) foreach (var n in npcs.Values) if (n.Type == "Shopkeeper" || n.Type == "Player vendor" || n.Items.Count > 0) list.Add(Copy(n));
        return list;
    }
    // Every type:subtype seen, for the map's filter.
    public System.Collections.Generic.SortedSet<string> Kinds() {
        var set = new System.Collections.Generic.SortedSet<string>(); lock (npcs) foreach (var n in npcs.Values) set.Add(n.Type + ":" + n.Sub);
        lock (places) { LoadPlaces(); foreach (var p in places.Values) set.Add(p.Kind + ":" + PlaceSub(p)); }
        return set;
    }
    static AtlasNpc Copy(AtlasNpc n) { return new AtlasNpc { Serial = n.Serial, Name = n.Name, Type = n.Type, Sub = n.Sub, Body = n.Body, X = n.X, Y = n.Y, Criminal = n.Criminal, Last = n.Last, ItemsAt = n.ItemsAt, Items = new System.Collections.Generic.List<string>(n.Items) }; }

    // ---- houses and guildstones: a multi (0x1A graphic with 0x4000) is a house; a gravestone-art item (0x0ED4, the 1998
    // guildstone) whose label names a guild is a guildstone. Kept in places-<server>.txt beside the atlas.
    readonly System.Collections.Generic.Dictionary<uint, AtlasPlace> places = new System.Collections.Generic.Dictionary<uint, AtlasPlace>();
    bool placesLoaded;
    string PlacesPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path).Replace("atlas-", "places-")); } }
    void LoadPlaces() {
        if (placesLoaded) return; placesLoaded = true;
        try { if (System.IO.File.Exists(PlacesPath)) foreach (var line in System.IO.File.ReadAllLines(PlacesPath)) { var t = line.Split('\t'); if (t.Length < 8) continue; var p = new AtlasPlace { Serial = System.Convert.ToUInt32(t[0], 16), Kind = t[1], Name = t[2], Details = t[3], Graphic = int.Parse(t[4]), X = int.Parse(t[5]), Y = int.Parse(t[6]), Last = new System.DateTime(long.Parse(t[7])) }; places[p.Serial] = p; } } catch { }
    }
    public static string PlaceSub(AtlasPlace p) { return p.Kind == "House" ? "multi 0x" + (p.Graphic & 0x3FFF).ToString("X") : p.Kind.ToLowerInvariant(); }
    public void Place(uint s, string kind, int graphic, int x, int y) {
        lock (places) { LoadPlaces(); AtlasPlace p; if (!places.TryGetValue(s, out p)) places[s] = p = new AtlasPlace { Serial = s, Kind = kind }; p.Graphic = graphic; p.X = x; p.Y = y; p.Last = System.DateTime.Now; if (kind.Length > 0) p.Kind = kind; dirty = true; }
    }
    public bool HasPlace(uint s) { lock (places) { LoadPlaces(); return places.ContainsKey(s); } }
    public void PlaceName(uint s, string name) { lock (places) { AtlasPlace p; if (places.TryGetValue(s, out p) && name.Length > 0) { p.Name = name; dirty = true; } } }
    public void PlaceDetail(uint s, string detail) { lock (places) { AtlasPlace p; if (places.TryGetValue(s, out p)) { p.Details = System.DateTime.Now.ToString("MM-dd HH:mm") + " " + detail; dirty = true; } } }
    public void Forget(uint s) { lock (places) { if (places.Remove(s)) dirty = true; } }
    // The nearest house within reach of a tile, 0 when none.
    public uint HouseNear(int x, int y, int reach) {
        lock (places) { LoadPlaces(); uint best = 0; int bd = reach + 1; foreach (var p in places.Values) if (p.Kind == "House") { int d = System.Math.Max(System.Math.Abs(p.X - x), System.Math.Abs(p.Y - y)); if (d < bd) { bd = d; best = p.Serial; } } return best; }
    }
    public System.Collections.Generic.List<AtlasPlace> Places() {
        var l = new System.Collections.Generic.List<AtlasPlace>(); lock (places) { LoadPlaces(); foreach (var p in places.Values) l.Add(new AtlasPlace { Serial = p.Serial, Kind = p.Kind, Name = p.Name, Details = p.Details, Graphic = p.Graphic, X = p.X, Y = p.Y, Last = p.Last }); } return l;
    }

    // ---- trades: one row per item bought or sold, appended to trades-<server>.csv and kept in memory for the Shops tab.
    readonly System.Collections.Generic.List<AtlasTrade> trades = new System.Collections.Generic.List<AtlasTrade>();
    string TradesPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path).Replace("atlas-", "trades-").Replace(".txt", ".csv")); } }
    public void Trade(AtlasTrade t) {
        LoadTrades();
        lock (trades) { trades.Add(t); if (trades.Count > 5000) trades.RemoveAt(0); }
        try {
            bool fresh = !System.IO.File.Exists(TradesPath);
            System.IO.File.AppendAllText(TradesPath, (fresh ? "time,kind,vendor,vendor serial,x,y,item,amount,unit price,total\r\n" : "")
                + t.At.ToString("yyyy-MM-dd HH:mm:ss") + "," + t.Kind + "," + Csv(t.Vendor) + "," + t.VendorSerial.ToString("X") + "," + t.X + "," + t.Y + "," + Csv(t.Item) + "," + t.Amount + "," + t.Price + "," + (t.Amount * t.Price) + "\r\n");
        } catch { }
    }
    static string Csv(string s) { return "\"" + s.Replace("\"", "'") + "\""; }
    public System.Collections.Generic.List<AtlasTrade> Trades(uint vendor) { LoadTrades(); lock (trades) return trades.FindAll(t => vendor == 0 || t.VendorSerial == vendor); }
    // The file's rows back to memory once, the newest 5000, so the Circuit tab's totals outlive a relay restart.
    bool tradesLoaded;
    void LoadTrades() {
        lock (trades) {
            if (tradesLoaded) return; tradesLoaded = true;
            try {
                if (!System.IO.File.Exists(TradesPath)) return;
                var lines = System.IO.File.ReadAllLines(TradesPath); var old = new System.Collections.Generic.List<AtlasTrade>();
                for (int i = System.Math.Max(1, lines.Length - 5000); i < lines.Length; i++) {
                    // time,kind,"vendor",serial,x,y,"item",amount,unit price,total; a quoted field holds no quote (Csv).
                    var f = new System.Collections.Generic.List<string>(); var cur = new System.Text.StringBuilder(); bool quoted = false;
                    foreach (char ch in lines[i]) { if (ch == '"') quoted = !quoted; else if (ch == ',' && !quoted) { f.Add(cur.ToString()); cur.Clear(); } else cur.Append(ch); }
                    f.Add(cur.ToString());
                    System.DateTime at; uint s; int x, y, a, p;
                    if (f.Count < 9 || !System.DateTime.TryParse(f[0], out at) || !uint.TryParse(f[3], System.Globalization.NumberStyles.HexNumber, null, out s) || !int.TryParse(f[4], out x) || !int.TryParse(f[5], out y) || !int.TryParse(f[7], out a) || !int.TryParse(f[8], out p)) continue;
                    old.Add(new AtlasTrade { At = at, Kind = f[1], Vendor = f[2], VendorSerial = s, X = x, Y = y, Item = f[6], Amount = a, Price = p });
                }
                trades.InsertRange(0, old); if (trades.Count > 5000) trades.RemoveRange(0, trades.Count - 5000);
            } catch { }
        }
    }

    public void Save() {
        var lines = new System.Collections.Generic.List<string>();
        lock (npcs) {
            if (!dirty) return; dirty = false;
            foreach (var n in npcs.Values) if (n.Kept) lines.Add(n.Serial.ToString("X") + "\t" + n.Type + "\t" + Clean(n.Sub) + "\t" + Clean(n.Name) + "\t" + n.Body + "\t" + n.X + "\t" + n.Y + "\t" + n.Last.Ticks + "\t" + string.Join("|", n.Items.ConvertAll(Clean)) + "\t" + n.ItemsAt.Ticks);
        }
        var placeLines = new System.Collections.Generic.List<string>();
        lock (places) foreach (var p in places.Values) placeLines.Add(p.Serial.ToString("X") + "\t" + p.Kind + "\t" + Clean(p.Name) + "\t" + Clean(p.Details) + "\t" + p.Graphic + "\t" + p.X + "\t" + p.Y + "\t" + p.Last.Ticks);
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); System.IO.File.WriteAllLines(path, lines); if (placesLoaded) System.IO.File.WriteAllLines(PlacesPath, placeLines); } catch { }
    }
    static string Clean(string s) { return s.Replace('\t', ' ').Replace('|', '/'); }
}
