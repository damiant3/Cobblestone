#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildBags()
    {
        // The bag pricer (Damian, 2026-10-15: "a default price to sell at on player vendor, based on known market
        // conditions and a reasonable markup over city prices when in bulk ... let each price be configurable", "mine
        // are all gm made, so there is my pricing for the better than store quality").
        // The reagent loadout (Damian, 2026-10-16: "like the pricer, its the reagent bag loadout tool"): two targets,
        // the loadout bag then the storage bag; the amounts to pull back are kept per reagent.
        // Every bag setting in one place (AssistFeatures.BagSettings): where the bag is and what it holds, Set and
        // Clear.
        Panel g = Group("Bags");
        FieldGrid bg = Grid(g);
        m_bagNote = Words("");
        foreach (string[] setting in AssistFeatures.BagSettings)
        {
            string key = setting[0];
            Label where = Words("");
            Button set = Btn("Set", () => { string? w = m_f.BagTarget(key); m_bagNote.Text = w ?? ""; });
            Button unset = Btn("Clear", () => { m_f.BagClear(key); m_bagNote.Text = setting[1] + " cleared."; });
            bg.Line(setting[1], Strip(set, unset, where));
            m_tipper.SetToolTip(set, setting[2]);
            m_bagRows.Add(Tuple.Create(key, where));
        }
        // Each profession's road count (AssistTools.md, "Road counts"): the tools its bag carries, topped up from its
        // toolbox.
        foreach (Profession p in m_f.Tools.All)
        {
            Profession prof = p;
            bg.Field(prof.Name + " road count",
                Num(0, ToolPlaces.RoadMax, m_f.Tools.Road(prof), v => m_f.Tools.SetRoad(prof, v), 1),
                "How many " + prof.ToolsName + " the " + prof.Name.ToLowerInvariant()
                + (" toolbag carries. Wherever its toolbox is within reach (a circuit's start, the house), the bag is "
                + "topped up to this many from the box."));
        }
        CheckBox openAtLogin = bg.Wide(new CheckBox { Text = "Open the pack and its bags at login", AutoSize = true,
            Checked = m_f.OpenBagsAtLogin },
            "At each login the pack opens on your screen, then every bag directly in it (never a book or a key ring). "
            + "The client places each window itself.");
        openAtLogin.CheckedChanged += (s, e) => m_f.OpenBagsAtLogin = openAtLogin.Checked;
        bg.Line(null, m_bagNote);
        // The layout helper (AssistFeatures.LayoutBag): a shape, an ASCII picture for "ascii", then the bag targeted.
        g = Group("Bag Organizer");
        FieldGrid layg = Grid(g);
        ComboBox shapePick = Pick(110, AssistFeatures.LayoutShapes.ToArray());
        shapePick.SelectedIndex = 0;
        TextBox artBox = new()
        {
            Multiline = true,
            Height = 80,
            Width = 220,
            Font = new Font(FontFamily.GenericMonospace, 9),
            ScrollBars = ScrollBars.Vertical
        };
        m_layoutNote = Words("");
        layg.Line("Shape",
            Strip(shapePick,
            Btn("Target a bag",
            () => { string? w = m_f.LayoutTarget(((string?)shapePick.SelectedItem)!, artBox.Text); if (w
            != null) m_layoutNote.Text = w; }), m_layoutNote));
        layg.Line("Picture", artBox);
        m_tipper.SetToolTip(artBox,
            "For the ascii shape: type a picture; every character that is not a space is a slot, filled in reading "
            + "order, the picture stretched over the bag.");
        m_tipper.SetToolTip(shapePick,
            "Square: rows and columns. Circle: a ring. Pentagram: the star in one stroke. Ascii: the picture below.");
        g = Group("Reagent loadout");
        FieldGrid lg2 = Grid(g);
        m_loadoutStatus = Words("");
        // The targets only when a bag is unset (Damian, 2026-10-20: "button should only request targets if they are not
        // previously set. there is now reset targets in the bags expando").
        Button refill = Btn("Refill loadout",
            () => { string? w = m_f.LoadoutGo(); if (w != null) m_loadoutStatus.Text = w; });
        m_tipper.SetToolTip(refill,
            "Refills the loadout bag from the storage bag. Asks for the two bags only when either is unset; Bags, Set "
            + "and Clear, picks them again.");
        lg2.Line(null, Strip(refill, TimingsTag(() => TaskTimingRegistry.Loadout), m_loadoutStatus));
        foreach (int rg in AssistFeatures.LoadoutReagents())
        {
            int graphic = rg;
            NumericUpDown box = lg2.Field(AssistFeatures.ReagentName(graphic)!,
                Num(0, 999, m_f.LoadoutWant(graphic), v => m_f.SetLoadoutWant(graphic, v)),
                "How many of this the loadout bag is refilled to. Green: the bag holds that many; gold: some, short "
                + "of it; red: none; grey: the bag unset or not seen.");
            m_loadoutRows.Add(Tuple.Create(graphic, lg2.CaptionOf(box)!));
        }

        g = Group("Bag pricer");
        FieldGrid pg2 = Grid(g);
        m_pricerStatus = Words("");
        pg2.Line(null, Strip(Btn("Target a bag", () => m_f.PricerTarget()), m_pricerStatus));
        m_bulkPct = Percent(pg2, "Bulk markup (%)", "pricer.bulk", BulkPct,
            "The markup over the city price for an item held in bulk.");
        m_craftPct = Percent(pg2, "GM-made markup (%)", "pricer.craft", CraftPct,
            "The markup for armour, weapons and clothing when they are GM-made.");
        m_gmMade = pg2.Wide(new CheckBox { Text = "My armour, weapons and clothing are GM-made", AutoSize = true,
            Checked = m_f.Cfg.Get("pricer.gm", "on") == "on" });
        m_gmMade.CheckedChanged += (s, e) => { m_f.Cfg.Set("pricer.gm",
            m_gmMade.Checked ? "on" : "off"); FillPricer(); };
        m_pricer = Add(g,
            new DataGridView { Height = 300, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window });
        foreach (string c in new[] { "Item", "Qty", "City price", "Markup", "Unit price (edit)", "Total" })
            m_pricer.Columns.Add(c, c);
        foreach (DataGridViewColumn c in m_pricer.Columns)
        {
            c.ReadOnly = c.Index != PriceCol;
            c.SortMode = DataGridViewColumnSortMode.Automatic;
        }
        m_pricer.Columns[PriceCol].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 220);
        m_pricer.CellEndEdit += (s, e) => PriceEdited(e.RowIndex);
        m_pricerTotal = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });
    }

    Label m_loadoutStatus = null!;
    readonly List<Tuple<int, Control>> m_loadoutRows = new();


    // ---- the bag pricer. A unit price is the city price (the median of every recorded vendor's buy-list price for the
    // item) plus the bulk markup, or the GM-made markup for wearables (TILEDATA flag 0x400000: armour, weapons,
    // clothing) when GM-made is ticked; a price the player types is kept per item in the settings and wins. Empty
    // clears it.
    public const int BulkPct = 25, CraftPct = 50, PriceCol = 4;
    DataGridView m_pricer = null!;
    Label m_pricerStatus = null!, m_pricerTotal = null!;
    NumericUpDown m_bulkPct = null!, m_craftPct = null!;
    CheckBox m_gmMade = null!; long m_pricerSeen; bool m_pricerFilling;
    NumericUpDown Percent(FieldGrid grid, string caption, string key, int dflt, string tip)
    {
        return grid.Field(caption,
            Num(0, 1000, m_f.Cfg.GetInt(key, dflt), v => { m_f.Cfg.Set(key, v.ToString()); FillPricer(); }), tip);
    }
    static string PriceKey(string name)
    {
        string s = string.Join(" ",
            name.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("a "))
            s = s.Substring(2);
        else if (s.StartsWith("an "))
            s = s.Substring(3);
        if (s.EndsWith("s") && s.Length > 3)
            s = s.Substring(0, s.Length - 1);
        return s;
    }
    sealed class PriceRow
    {
        public string Name = null!, Key = null!;
        public int Qty, City, Vendors, Unit; public bool Wearable, Mine; public string Markup = null!;
    }
    List<PriceRow> PriceRows()
    {
        Map<string, PriceRow> rows = Map<string, PriceRow>.Empty;
        List<UoTiles.Tile> tiles = UoTiles.Items();
        foreach (AssistItem it in m_f.PricerItems())
        {
            string name = UoTiles.Name(it.Graphic), key = PriceKey(name);
            PriceRow? r;
            if (!rows.TryGet(key, out r))
                rows = rows.Set(key, r = new PriceRow { Name = name, Key = key,
                    Wearable = it.Graphic < tiles.Count && (tiles[it.Graphic].Flags & 0x00400000) != 0 });
            r.Qty += Math.Max(1, it.Amount);
        }
        Map<string, List<int>> market = Map<string, List<int>>.Empty;
        if (m_f.Atlas != null)
            foreach (AtlasNpc v in m_f.Atlas.Shops())
                foreach (string i in v.Items)
                {
                    string key = PriceKey(StockName(i));
                    List<int>? l;
                    if (!market.TryGet(key, out l))
                        market = market.Set(key, l = new List<int>());
                    l.Add(StockPrice(i));
                }
        int bulk = (int)m_bulkPct.Value, craft = (int)m_craftPct.Value;
        bool gm = m_gmMade.Checked;
        foreach (PriceRow r in rows.Values)
        {
            List<int>? l;
            if (market.TryGet(r.Key, out l))
            {
                r.Vendors = l.Count;
                r.City = Median(new List<int>(l));
            }
            int m = r.Wearable && gm ? craft : bulk;
            r.Markup = (r.Wearable && gm ? "GM-made " : "bulk ") + m + "%";
            r.Unit = r.City > 0 ? (int)Math.Round(r.City * (100 + m) / 100.0) : 0;
            int mine;
            if (int.TryParse(m_f.Cfg.Get("pricer.price." + r.Key, ""), out mine))
            {
                r.Unit = mine;
                r.Mine = true;
                r.Markup = "your price";
            }
        }
        List<PriceRow> list = new(rows.Values);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
    void FillPricer()
    {
        if (m_pricer == null || m_pricerFilling)
            return;
        m_pricerFilling = true;
        try
        {
            List<PriceRow> rows = PriceRows();
            m_pricer.Rows.Clear();
            int total = 0, count = 0, unpriced = 0;
            foreach (PriceRow r in rows)
            {
                int i = m_pricer.Rows.Add(r.Name, r.Qty,
                    r.City > 0 ? r.City + " (" + r.Vendors + " vendor" + (r.Vendors == 1 ? "" : "s") + ")"
                    : "none recorded", r.Markup, r.Unit > 0 ? r.Unit.ToString() : "", r.Unit * r.Qty);
                m_pricer.Rows[i].Tag = r.Key;
                if (r.Unit == 0)
                {
                    unpriced++;
                    m_pricer.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(250, 215, 215);
                }
                total += r.Unit * r.Qty;
                count += r.Qty;
            }
            m_pricerTotal.Text = rows.Count == 0 ? ""
                : "Price the bag at " + total + " gp: " + count + " items in " + rows.Count + " kinds"
                + (unpriced > 0 ? "; " + unpriced + " kinds have no city price, type one in" : "") + ".";
        }
        finally { m_pricerFilling = false; }
    }
    void PriceEdited(int row)
    {
        if (m_pricerFilling || row < 0 || row >= m_pricer.Rows.Count)
            return;
        string? key = ((string?)m_pricer.Rows[row].Tag)!, text = Convert.ToString(m_pricer.Rows[row].Cells[PriceCol].Value
            ?? "")!.Trim();
        int v;
        m_f.Cfg.Set("pricer.price." + key, int.TryParse(text, out v) && v > 0 ? v.ToString() : "");
        BeginInvoke(new Action(FillPricer));
    }
}
