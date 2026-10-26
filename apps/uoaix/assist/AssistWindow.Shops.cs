#nullable enable
using System.Collections.Immutable;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildShops()
    {
        // Shopkeepers and merchants (Damian, 2026-10-15: "add a shopkeeps and merchants as a main tab"): every vendor
        // the atlas holds, with its trade, place, distance and buy list; the search keeps vendors whose list holds the
        // text.
        Panel g = Group("Shopkeepers and merchants");
        FieldGrid shopGrid = Grid(g);
        m_shopFind = shopGrid.Line("Find an item", new TextBox { Width = 200 });
        m_shopFind.TextChanged += (s, e) => { m_shopShape = ""; FillShops(); };
        m_shopCity = Filter(shopGrid, "City", "shops.city");
        m_shopKind = Filter(shopGrid, "Type", "shops.kind");
        m_shops = Add(g, new SortList { View = View.Details, Height = 200, FullRowSelect = true, AutoSort = true });
        m_shops.Columns.Add("Name", 150);
        m_shops.Columns.Add("Trade", 95);
        m_shops.Columns.Add("Where", 260);
        m_shops.Columns.Add("Seen", 70);
        m_shops.Columns.Add("Thread restock", 95);
        m_shops.Columns.Add("Shirt price", 120);
        m_shops.Columns.Add("Depth", 95);
        m_shops.Columns.Add("Off the norm", 200);
        // Last (Damian, 2026-10-24: "tiles and items are both useless to me, make them the last columns").
        m_shops.Columns.Add("Tiles", 50);
        m_shops.Columns.Add("Items", 45);
        m_shopInfo = Add(g, new ListBox { Height = 80, HorizontalScrollbar = true });
        StockDraw(m_shopInfo);
        // Every buy list the vendor showed, by when it began (Damian, 2026-10-24: "show every survey as a drop down
        // list by date/time for each vendor when we pull a buy list, so we can see the changes over time").
        m_shopSurvey = Grid(g).Line("Buy list", Pick(260),
            "Every buy list this vendor showed, newest first; pick one to see its items and prices then.");
        m_shopSurvey.SelectedIndexChanged += (s, e) => { if (!m_surveyFilling) ShowSurvey(); };
        m_shopItems = Add(g,
            new SortList { View = View.Details, Height = ShopItemsMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        m_shopItems.Columns.Add("Item", 220);
        m_shopItems.Columns.Add("Price (gp)", 75, HorizontalAlignment.Right);
        m_shopItems.Columns.Add("Qty", 50, HorizontalAlignment.Right);
        m_shopItems.Columns.Add("Off the norm", 330);
        // Every sale to the vendor and purchase from it in the trades record (Damian, 2026-10-24: "shopkeepers and
        // merchants should also show the history of sales to those vendors, and purchases").
        m_shopTrades = Add(g,
            new SortList { View = View.Details, Height = ShopItemsMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "When:110", "Trade:90", "Item:200", "Qty:50", "Each (gp):70", "Total (gp):75" })
        {
            string[] a = c.Split(':');
            m_shopTrades.Columns.Add(a[0], int.Parse(a[1]),
                a[0] == "When" || a[0] == "Trade" || a[0] == "Item" ? HorizontalAlignment.Left
                : HorizontalAlignment.Right);
        }
        m_shops.SelectedIndexChanged += (s, e) => { if (!m_selecting
            && m_shops.SelectedItems.Count
            > 0) ShowNpc(m_shopInfo, m_shopItems, (uint)m_shops.SelectedItems[0].Tag!); };
    }

    // The vendor the Shops tab shows, and the line of its details that sums the shown buy list's off-the-norm rows.
    uint m_shopShown; int m_shopSumAt = -1; bool m_surveyFilling;
    sealed class SurveyItem
    {
        public AtlasSurvey Survey = null!; public override string ToString()
        {
            return Survey.At.ToString("yyyy-MM-dd HH:mm:ss") + "  (" + Survey.Items.Count + " items)";
        }
    }

    // A buy list (Damian, 2026-10-15: "sort the list by name and then by any quality/price descending. if magic items
    // are in the list, put them at the top and make them blue and bold", "include quantity in the list, make it a
    // grid"). Rows are "name: price gp x quantity", the quantity absent when no contents list came first; a 1998 item's
    // quality shows only in its price (Helmet at 169 and 183). Magic is read from the 1998 OSI name words, unseen on
    // uo1998's wire.
    static readonly ImmutableArray<string> s_magicWords = [ "magic", " of ruin", " of might", " of force", " of power",
        " of vanquishing", " of defense",
        " of guarding", " of hardening", " of fortification", " of invulnerability", "accurate", "surpassingly",
        "eminently", "exceedingly",
        "supremely", "durable", "substantial", "massive", "fortified", "indestructible", " charges" ];
    public static bool IsMagic(string s)
    {
        foreach (string w in s_magicWords)
            if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
    static string StockName(string i)
    {
        int c = i.LastIndexOf(": ");
        return string.Join(" ",
            (c < 0 ? i : i.Substring(0, c)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
    static string[] StockTail(string i)
    {
        int c = i.LastIndexOf(": ");
        return c < 0 ? new string[0] : i.Substring(c + 2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
    static int StockPrice(string i)
    {
        string[] t = StockTail(i);
        int p;
        return t.Length > 0 && int.TryParse(t[0], out p) ? p : 0;
    }
    static string StockQty(string i)
    {
        string[] t = StockTail(i);
        return t.Length > 3 && t[2] == "x" ? t[3] : "";
    }
    static List<string> SortedStock(List<string> items)
    {
        List<string> l = new(items);
        l.Sort((a, b) => { int m = IsMagic(b).CompareTo(IsMagic(a)); if (m
            != 0) return m; int n = string.Compare(StockName(a), StockName(b),
            StringComparison.OrdinalIgnoreCase); return n != 0 ? n : StockPrice(b).CompareTo(StockPrice(a)); });
        return l;
    }
    // Off the norm (Damian, 2026-10-15: "highlight any deviation from the norm. if they have cheap ingots or expensive
    // ones, or if they have large quantities available, or something unique unseen at others of the same type"): each
    // row against the same item at every OTHER recorded vendor. Cheap or dear is CheapPct below or DearPct above their
    // median price, large is LargeTimes their median quantity, unique is no other vendor of the same trade selling it.
    public const int CheapPct = 15, DearPct = 15, LargeTimes = 2;
    public const int OffCheap = 1, OffDear = 2, OffLarge = 4, OffUnique = 8;
    sealed class Offer
    {
        public uint Vendor; public string Kind = null!; public int Price, Qty;
    }
    // The NPC's own title when the server sent one (the UOAIX shard), else the shop region's trade (uo1998 sends bare
    // names). A clothier is a tailor shop, not a shopkeeper's trade, and its keepers are tailors and weavers (Damian,
    // 2026-10-24: "the two roles split out as tailor and weaver npc shop "roles" for the clothier shop. we only care
    // about the tailors, and we should type them as such in our data"): every keeper of a tailor shop region
    // (TailorShopKinds, the Spinning Wheels' WEAVER_ ones included) is the tailor or the weaver TailorStock reads.
    public static string Kind(AtlasNpc n)
    {
        if (n.Type == "Player vendor" && n.Wares.Length > 0)
            return n.Wares;
        if (n.Sub.Length > 0 && n.Sub != "vendor" && n.Sub != "untitled" && n.Sub != "unnamed")
            return n.Sub;
        string? shop = UoRegions.ShopKind(n.X, n.Y);
        if (shop is null)
            return n.Sub;
        if (AssistFeatures.TailorShopKinds.Contains(shop.ToUpperInvariant()))
            return AssistFeatures.TailorStock(n) ? "tailor" : "weaver";
        return shop;
    }
    static Map<string, List<Offer>> Market(List<AtlasNpc> vendors)
    {
        Map<string, List<Offer>> m = Map<string, List<Offer>>.Empty;
        foreach (AtlasNpc v in vendors)
        {
            string kind = Kind(v);
            foreach (string i in v.Items)
            {
                string key = StockName(i).ToLowerInvariant();
                List<Offer>? l;
                if (!m.TryGet(key, out l))
                    m = m.Set(key, l = new List<Offer>());
                int q;
                l.Add(new Offer { Vendor = v.Serial, Kind = kind, Price = StockPrice(i),
                    Qty = int.TryParse(StockQty(i), out q) ? q : -1 });
            }
        }
        return m;
    }
    static int Median(List<int> l)
    {
        l.Sort();
        return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }
    // The flags and the note for one row of vendor v; kindSellers is how many OTHER vendors of v's trade have a list.
    static int OffNorm(AtlasNpc v, string kind, string row, Map<string, List<Offer>> market, int kindSellers,
        out string note)
    {
        note = "";
        int flags = 0;
        List<Offer>? all;
        if (!market.TryGet(StockName(row).ToLowerInvariant(), out all))
            return 0;
        List<int> prices = new();
        List<int> qtys = new();
        Set<uint> shops = Set<uint>.Empty;
        bool sameKind = false;
        foreach (Offer o in all)
            if (o.Vendor != v.Serial)
            {
                prices.Add(o.Price);
                if (o.Qty >= 0)
                    qtys.Add(o.Qty);
                shops = shops.Add(o.Vendor);
                if (o.Kind == kind)
                    sameKind = true;
            }
        List<string> notes = new();
        int price = StockPrice(row), qty;
        bool hasQty = int.TryParse(StockQty(row), out qty);
        if (prices.Count > 0)
        {
            int med = Median(prices);
            if (price * 100 <= med * (100 - CheapPct))
            {
                flags |= OffCheap;
                notes.Add("cheap: " + price + " vs " + med + " typical at " + shops.Count + " other shops");
            }
            else if (price * 100 >= med * (100 + DearPct))
            {
                flags |= OffDear;
                notes.Add("expensive: " + price + " vs " + med + " typical at " + shops.Count + " other shops");
            }
        }
        if (hasQty && qtys.Count > 0)
        {
            int med = Median(qtys);
            if (med > 0 && qty >= med * LargeTimes)
            {
                flags |= OffLarge;
                notes.Add("large stock: " + qty + " vs " + med + " typical");
            }
        }
        if (!sameKind && kindSellers > 0)
        {
            flags |= OffUnique;
            notes.Add("unique: no other " + kind + " of " + kindSellers + " sells it");
        }
        note = string.Join("; ", notes);
        return flags;
    }
    static int KindSellers(AtlasNpc v, string kind, List<AtlasNpc> vendors)
    {
        int c = 0;
        foreach (AtlasNpc o in vendors)
            if (o.Serial != v.Serial && o.Items.Count > 0 && Kind(o) == kind)
                c++;
        return c;
    }
    static string OffSummary(int cheap, int dear, int large, int unique)
    {
        List<string> l = new();
        if (cheap > 0)
            l.Add(cheap + " cheap");
        if (dear > 0)
            l.Add(dear + " expensive");
        if (large > 0)
            l.Add(large + " large stock");
        if (unique > 0)
            l.Add(unique + " unique");
        return string.Join(", ", l);
    }
    static Color OffColor(int flags)
    {
        if ((flags & OffCheap) != 0)
            return Color.FromArgb(200, 240, 200);
        if ((flags & OffDear) != 0)
            return Color.FromArgb(250, 210, 210);
        if ((flags & OffUnique) != 0)
            return Color.FromArgb(255, 235, 175);
        if ((flags & OffLarge) != 0)
            return Color.FromArgb(215, 228, 250);
        return Color.Empty;
    }
    // The detail lists draw a magic item's row bold and blue.
    static void StockDraw(ListBox b)
    {
        b.DrawMode = DrawMode.OwnerDrawFixed;
        Font bold = new(b.Font, FontStyle.Bold);
        b.DrawItem += (s, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0)
                return;
            string? t = b.Items[e.Index].ToString();
            bool magic = t!.StartsWith("   ") && IsMagic(t);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(e.Graphics, t, magic ? bold : e.Font, e.Bounds,
                sel ? SystemColors.HighlightText : magic ? Color.Blue : e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };
    }
    // With a grid, the buy list goes to the grid (item, price, quantity) and the box keeps the rest.
    void ShowNpc(ListBox box, ListView grid, uint serial)
    {
        box.Items.Clear();
        if (grid != null)
            grid.Items.Clear();
        m_shopSumAt = -1;
        bool shopsTab = grid != null && grid == m_shopItems;
        if (shopsTab)
        {
            m_shopShown = 0;
            m_surveyFilling = true;
            m_shopSurvey.Items.Clear();
            m_surveyFilling = false;
            m_shopTrades.Items.Clear();
        }
        if (m_f.Atlas == null || serial == 0)
            return;
        try
        {
            ShowNpcRows(box, grid!, serial);
            if (shopsTab)
                FillShopHistory(box, serial);
        }
        finally { int w = 0; Font bold = new(box.Font, FontStyle.Bold); foreach (object o in box.Items) w = Math.Max(w,
            TextRenderer.MeasureText(o.ToString(), bold).Width); box.HorizontalExtent = w + 8; bold.Dispose(); }
    }
    // The Shops tab's vendor: its buy lists by when each began, newest first and shown (the current list stands for
    // them when none is recorded), and every trade with it, newest first, its totals in the details.
    void FillShopHistory(ListBox box, uint serial)
    {
        m_shopShown = serial;
        AtlasNpc? n = null;
        foreach (AtlasNpc x in m_f.Atlas.Shops())
            if (x.Serial == serial)
                n = x;
        List<AtlasSurvey> surveys = m_f.Atlas.Surveys(serial);
        if (surveys.Count == 0 && n != null && n.Items.Count > 0)
            surveys.Add(new AtlasSurvey { At = n.ItemsAt, Items = n.Items });
        m_surveyFilling = true;
        try
        {
            m_shopSurvey.BeginUpdate();
            foreach (AtlasSurvey sv in surveys)
                m_shopSurvey.Items.Add(new SurveyItem { Survey = sv });
            if (m_shopSurvey.Items.Count > 0)
                m_shopSurvey.SelectedIndex = 0;
            m_shopSurvey.EndUpdate();
        }
        finally { m_surveyFilling = false; }
        List<AtlasTrade> trades = m_f.Atlas.VendorTrades(serial);
        int spent = 0, earned = 0;
        m_shopTrades.BeginUpdate();
        for (int i = trades.Count - 1; i >= 0; i--)
        {
            AtlasTrade t = trades[i];
            int total = t.Amount * t.Price;
            if (t.Kind == "buy")
                spent += total;
            else
                earned += total;
            m_shopTrades.Items.Add(new ListViewItem(new[] { t.At.ToString("yyyy-MM-dd HH:mm"),
                t.Kind == "buy" ? "bought from" : "sold to", t.Item, t.Amount.ToString(), t.Price.ToString(),
                total.ToString() }));
        }
        m_shopTrades.EndUpdate();
        box.Items.Add(trades.Count == 0 ? "No trades with it on record."
            : "Trades: " + trades.Count + ", bought " + spent + " gp from it, sold " + earned
            + " gp to it; every one below");
    }
    // The buy list picked under Buy list, in the item grid, with its own off-the-norm summary in the details.
    void ShowSurvey()
    {
        if (m_shopSurvey.SelectedItem is not SurveyItem it || m_f.Atlas == null || m_shopShown == 0)
            return;
        AtlasNpc? n = null;
        foreach (AtlasNpc x in m_f.Atlas.Shops())
            if (x.Serial == m_shopShown)
                n = x;
        if (n == null)
            return;
        n.Items = it.Survey.Items;
        n.ItemsAt = it.Survey.At;
        m_shopItems.Items.Clear();
        string sum = FillStock(m_shopItems, n);
        if (m_shopSumAt >= 1 && m_shopSumAt < m_shopInfo.Items.Count)
        {
            m_shopInfo.Items[m_shopSumAt - 1] = "Sells " + n.Items.Count + " items (buy list of "
                + n.ItemsAt.ToString("MM-dd HH:mm") + "), below";
            m_shopInfo.Items[m_shopSumAt] = sum;
        }
    }
    // Fills the grid; returns the off-the-norm summary for the details list.
    string FillStock(ListView grid, AtlasNpc n)
    {
        List<AtlasNpc> vendors = m_f.Atlas.Shops();
        Map<string, List<Offer>> market = Market(vendors);
        string kind = Kind(n);
        int sellers = KindSellers(n, kind, vendors);
        int cheap = 0, dear = 0, large = 0, unique = 0;
        grid.BeginUpdate();
        Font bold = new(grid.Font, FontStyle.Bold);
        foreach (string i in SortedStock(n.Items))
        {
            string note;
            int off = OffNorm(n, kind, i, market, sellers, out note);
            if ((off & OffCheap) != 0)
                cheap++;
            if ((off & OffDear) != 0)
                dear++;
            if ((off & OffLarge) != 0)
                large++;
            if ((off & OffUnique) != 0)
                unique++;
            ListViewItem row = new(new[] { StockName(i), StockPrice(i).ToString(), StockQty(i), note })
            {
                UseItemStyleForSubItems = true
            };
            if (IsMagic(i))
            {
                row.ForeColor = Color.Blue;
                row.Font = bold;
            }
            if (off != 0)
                row.BackColor = OffColor(off);
            grid.Items.Add(row);
        }
        grid.EndUpdate();
        string sum = OffSummary(cheap, dear, large, unique);
        return "Off the norm against " + (vendors.Count - 1) + " other recorded vendors (" + sellers + " " + kind
            + "): " + (sum.Length > 0 ? sum : "nothing");
    }
    void ShowNpcRows(ListBox box, ListView grid, uint serial)
    {
        AtlasNpc? n = null;
        foreach (AtlasNpc x in m_f.Atlas.Visible())
            if (x.Serial == serial)
                n = x;
        if (n == null)
            foreach (AtlasNpc x in m_f.Atlas.Shops())
                if (x.Serial == serial)
                    n = x;
        if (n == null)
        {
            foreach (AtlasPlace p in m_f.Atlas.Places())
            {
                if (p.Serial == serial)
                {
                    // What it is first (Damian, 2026-10-20: "its a forge and anvil. it should say that in the
                    // description when clicked on!"), its recorded name after.
                    box.Items.Add(p.Kind + (p.Name.Length > 0 ? ": " + p.Name : "") + "  ("
                        + (p.Kind == "House" ? "multi 0x" + (p.Graphic & 0x3FFF).ToString("X") + ", " : "")
                        + p.Serial.ToString("X") + ")");
                    box.Items.Add("at " + p.X + "," + p.Y + "  " + Sextant(p.X, p.Y) + "  seen "
                        + p.Last.ToString("MM-dd HH:mm"));
                    box.Items.Add(p.Details.Length > 0 ? "Last word on it: " + p.Details
                        : (p.Kind == "House"
                        ? "No decay or condition line heard here yet; click its sign or stand by it." : ""));
                }
            }

            return;
        }
        box.Items.Add((n.Name.Length > 0 ? AssistFeatures.Untitled(n.Name) : "unnamed") + "  (" + n.Type + ": "
            + (n.Type == "Shopkeeper" ? Kind(n) : n.Sub) + ", "
            + n.Serial.ToString("X") + ")");
        string? where = UoRegions.Where(n.X, n.Y);
        box.Items.Add((where != null ? where + ", " : "") + "at " + n.X + "," + n.Y + "  " + Sextant(n.X, n.Y)
            + "  seen " + n.Last.ToString("MM-dd HH:mm"));
        if (n.Items.Count > 0 && grid != null)
        {
            box.Items.Add("Sells " + n.Items.Count + " items (buy list of " + n.ItemsAt.ToString("MM-dd HH:mm")
                + "), below");
            m_shopSumAt = box.Items.Add(FillStock(grid, n));
        }
        else if (n.Items.Count > 0)
        {
            box.Items.Add("Sells (buy list of " + n.ItemsAt.ToString("MM-dd HH:mm") + "):");
            foreach (string i in SortedStock(n.Items))
                box.Items.Add("   " + i);
        }
        else if (n.Type == "Shopkeeper")
        {
            box.Items.Add("No buy list yet: say \"vendor buy\" near it to record one.");
        }

        List<AtlasTrade> trades = m_f.Atlas.Trades(serial);
        if (trades.Count > 0 && (grid == null || grid != m_shopItems))
        {
            int spent = 0, earned = 0;
            foreach (AtlasTrade t in trades)
            {
                if (t.Kind == "buy")
                    spent += t.Amount * t.Price;
                else
                    earned += t.Amount * t.Price;
            }
            box.Items.Add("Trades: bought " + spent + " gp, sold " + earned + " gp");
            foreach (AtlasTrade t in trades)
                box.Items.Add("   " + t.At.ToString("MM-dd HH:mm") + " " + t.Kind + " " + t.Amount + " " + t.Item
                    + " at " + t.Price + " gp");
        }
    }
    // A filter's entries are "All" and the set; the pick is kept in the settings and survives a refill.
    bool m_choosing;
    void Choices(ComboBox box, SortedSet<string> set, string key)
    {
        List<string> want = new()
        { "All" };
        want.AddRange(set);
        string pick = m_f.Cfg.Get(key, "All");
        bool same = box.Items.Count == want.Count;
        for (int i = 0; same && i < want.Count; i++)
            same = (string?)box.Items[i] == want[i];
        m_choosing = true;
        try
        {
            if (!same)
            {
                box.BeginUpdate();
                box.Items.Clear();
                foreach (string w in want)
                    box.Items.Add(w);
                box.EndUpdate();
            }
            int at = want.IndexOf(pick);
            if (box.SelectedIndex != (at < 0 ? 0 : at))
                box.SelectedIndex = at < 0 ? 0 : at;
        }
        finally { m_choosing = false; }
    }
    ComboBox Filter(FieldGrid grid, string caption, string key)
    {
        ComboBox box = grid.Field(caption, Pick(130));
        box.SelectedIndexChanged += (s, e) => { if (m_choosing
            || box.SelectedItem == null) return; m_f.Cfg.Set(key,
            (string)box.SelectedItem); m_shopShape = ""; FillShops(); };
        return box;
    }
    static string City(AtlasNpc n)
    {
        return UoRegions.City(n.X, n.Y) ?? "(no city)";
    }

    long m_stockSeen; bool m_selecting;
    ListViewItem? ShopRow(uint serial)
    {
        foreach (ListViewItem it in m_shops.Items)
            if ((uint)it.Tag! == serial)
                return it;
        return null;
    }
    // Selects a vendor's row, clearing the filters and the search when they hide it, and shows its details.
    void PickShop(uint serial)
    {
        if (serial == 0 || m_shops == null)
            return;
        m_shopShape = "";
        FillShops();
        ListViewItem? row = ShopRow(serial);
        if (row == null)
        {
            m_f.Cfg.Set("shops.city", "All");
            m_f.Cfg.Set("shops.kind", "All");
            m_shopFind.Text = "";
            m_shopShape = "";
            FillShops();
            row = ShopRow(serial);
        }
        if (row == null)
            return;
        m_shops.SelectedItems.Clear();
        row.Selected = true;
        row.EnsureVisible();
        ShowNpc(m_shopInfo, m_shopItems, serial);
    }

    void FillShops()
    {
        if (m_f.Atlas == null || m_shops == null || !m_shops.Visible && m_page != "shops")
            return;
        string find = m_shopFind.Text.Trim();
        int px = m_mapView.CenterX, py = m_mapView.CenterY;
        // Player vendors have their own tab (Damian, 2026-10-24: "shops is about server controlled NPC shopkeeps").
        List<AtlasNpc> vendors = m_f.Atlas.Shops().FindAll(n => n.Type != "Player vendor");
        List<AtlasNpc> list = vendors;
        // City and type filters (Damian, 2026-10-15: "add a combobox to filter by city or all, then filter by npc type
        // when known, or by shop type"); each offers what the recorded vendors hold.
        SortedSet<string> cities = new();
        SortedSet<string> kinds = new();
        foreach (AtlasNpc n in vendors)
        {
            cities.Add(City(n));
            kinds.Add(Kind(n));
        }
        Choices(m_shopCity, cities, "shops.city");
        Choices(m_shopKind, kinds, "shops.kind");
        string? city = m_shopCity.SelectedIndex > 0 ? (string?)m_shopCity.SelectedItem
            : null, kindPick = m_shopKind.SelectedIndex > 0 ? (string?)m_shopKind.SelectedItem : null;
        if (city != null)
            list = list.FindAll(n => City(n) == city);
        if (kindPick != null)
            list = list.FindAll(n => Kind(n) == kindPick);
        if (find.Length > 0)
            list = list.FindAll(n => n.Items.Exists(i => i.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
                || n.Sub.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0);
        string shape = string.Join(",",
            list.ConvertAll(n => n.Serial + ":" + n.X + ":" + n.Y + ":" + n.Items.Count + ":" + n.Name)) + "|" + px / 8
            + "," + py / 8 + "|" + vendors.Count + "|" + DateTime.Now.ToString("HHmm");
        if (shape == m_shopShape)
            return;
        m_shopShape = shape;
        // Rows updated in place, never cleared (Damian, 2026-10-24: "the updates to the shopkeeps grid is too
        // disruptive to its use in a tailor shop"): a keeper walking about or the minute turning changes a cell, not
        // the list, so the selection, the scroll and the row order stay; only a vendor come or gone adds or drops its
        // row.
        m_shops.BeginUpdate();
        Map<uint, ListViewItem> rows = Map<uint, ListViewItem>.Empty;
        foreach (ListViewItem r in m_shops.Items)
            rows = rows.Set((uint)r.Tag!, r);
        Set<uint> listed = Set<uint>.Empty;
        Map<string, List<Offer>> market = Market(vendors);
        foreach (AtlasNpc n in list)
        {
            int d = Math.Max(Math.Abs(n.X - px), Math.Abs(n.Y - py));
            string? where = UoRegions.Where(n.X, n.Y), kind = Kind(n);
            int sellers = KindSellers(n, kind, vendors), cheap = 0, dear = 0, large = 0, unique = 0;
            foreach (string i in n.Items)
            {
                string note;
                int off = OffNorm(n, kind, i, market, sellers, out note);
                if ((off & OffCheap) != 0)
                    cheap++;
                if ((off & OffDear) != 0)
                    dear++;
                if ((off & OffLarge) != 0)
                    large++;
                if ((off & OffUnique) != 0)
                    unique++;
            }
            string[] fc = m_f.VendorForecast(n);
            // Every keeper by its bare name, its trade in Trade (Damian, 2026-10-24: "we need to be consistent with
            // this Stanley the tailor in the name. Either we all use that or none do").
            string[] cells = { n.Name.Length > 0 ? AssistFeatures.Untitled(n.Name) : "unnamed", kind,
                (where != null ? where + " " : "") + n.X + "," + n.Y, n.Last.ToString("MM-dd HH:mm"), fc[0], fc[1],
                fc[2], OffSummary(cheap, dear, large, unique), d.ToString(), n.Items.Count.ToString() };
            listed = listed.Add(n.Serial);
            if (rows.TryGet(n.Serial, out ListViewItem? it))
            {
                for (int c = 0; c < cells.Length; c++)
                {
                    if (it.SubItems[c].Text != cells[c])
                        it.SubItems[c].Text = cells[c];
                }
            }
            else
                m_shops.Items.Add(new ListViewItem(cells) { Tag = n.Serial });
        }
        foreach (KeyValuePair<uint, ListViewItem> kv in rows)
        {
            if (!listed.Contains(kv.Key))
                m_shops.Items.Remove(kv.Value);
        }
        m_shops.EndUpdate();
    }
}
