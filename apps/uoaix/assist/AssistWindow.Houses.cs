#nullable enable
using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildHouses()
    {
        // The houses (AssistHouses.md): each registered with its own vendor and the one ring on it, its devices
        // scanned.
        Panel g = Group("Houses");
        FieldGrid hsg = Grid(g);
        m_housesNote = Words("");
        Button houseReg = Btn("Register (target its vendor)",
            () => { string? w = m_f.HouseRegisterTarget(""); if (w != null) m_housesNote.Text = w; });
        m_tipper.SetToolTip(houseReg,
            "Stand inside the house or beside it, its vendor in reach with this house's one key ring on it, then "
            + "target the vendor. The house is named from its sign; its forge, anvil, looms and spinning wheel are "
            + "scanned, and a house with none is storage. A new house is private.");
        hsg.Line(null,
            Words("Private or Public sets the house picked in the list. Every private house's door follows House door "
            + "below."));
        Func<bool, Button> modeBtn = priv =>
            Btn(priv ? "Private" : "Public",
            () => { string r = m_houseRows.SelectedItems.Count > 0 ? m_houseRows.SelectedItems[0].Text
            : ""; string? w = r.Length == 0 ? "pick a house below" : m_f.HouseMode(r, priv); m_housesNote.Text = w
            ?? m_f.HouseStatus; });
        Button housePriv = modeBtn(true);
        m_tipper.SetToolTip(housePriv,
            "Locked: every walk through its door is the key routine (nobody near, the sweep, its ring off its vendor "
            + "at the doorstep, the door locked behind, the ring back).");
        Button housePub = modeBtn(false);
        m_tipper.SetToolTip(housePub,
            "Unlocked: its door is walked as any door, its ring stays on its vendor, and nothing of value is stored "
            + "in it.");
        Button houseForget = Btn("Forget",
            () => { string r = m_houseRows.SelectedItems.Count > 0 ? m_houseRows.SelectedItems[0].Text
            : ""; string? w = r.Length == 0 ? "pick a house below" : m_f.HouseForget(r); m_housesNote.Text = w
            ?? m_f.HouseStatus; });
        hsg.Line(null, Strip(houseReg, housePriv, housePub, houseForget));
        hsg.Line(null, m_housesNote);
        m_houseRows = Add(g,
            new SortList { View = View.Details, Height = PricerMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "House:170", "Mode:60", "Door:80", "Vendor:80", "Ring:80", "Devices:260" })
        {
            string[] a = c.Split(':');
            m_houseRows.Columns.Add(a[0], int.Parse(a[1]));
        }
        // The door's protections, every private house's (they were the mining setup's while there was one house).
        g = Group("House door (every private house)");
        FieldGrid hdg = Grid(g);
        TextBox houseIgnore = hdg.Field("Door ignores", new TextBox { Width = 200, Text = m_f.HomeIgnoreText },
            "Players the house door does not wait for, by name, ; between (a neighbour you trust). Everyone else in "
            + "view holds the door.");
        houseIgnore.Leave += (s, e) => m_f.HomeIgnoreText = houseIgnore.Text;
        CheckBox houseSweep = hdg.Wide(new CheckBox { Text = "Sweep the door's tiles before the key", AutoSize = true,
            Checked = m_f.HomeSweepOn },
            "Before the key comes off the vendor, every open tile within " + AssistFeatures.HomeSweepTiles
            + (" of the doorstep is walked onto; a step refused onto an empty tile is someone hidden there, searched "
            + "once, and the key stays on the vendor. A circuit's first return is always swept; off, every later one "
            + "enters once nobody is in view."));
        houseSweep.CheckedChanged += (s, e) => m_f.HomeSweepOn = houseSweep.Checked;
        CheckBox houseRing = hdg.Wide(new CheckBox { Text = "Search for hidden before the key (guild war)",
            AutoSize = true, Checked = m_f.HomeRingOn },
            "Nine Detecting Hidden searches about the door (the doorstep and eight spots 5 tiles out) before the key "
            + "comes off the vendor; any answer but \"nothing hidden\" leaves the key there. Worth it only with good "
            + "Detecting Hidden: a failed check reads like a clear search.");
        houseRing.CheckedChanged += (s, e) => m_f.HomeRingOn = houseRing.Checked;
        // The one house of the mining forge, while no house is registered; a registered house's own mode decides
        // instead.
        g = Group("No house registered");
        m_houseLockBox = Grid(g).Wide(new CheckBox { Text = "Keep the house locked (the one house of the mining forge)",
            AutoSize = true, Checked = m_f.HouseLocked },
            "Only while no house is registered: every walk through the house door with nobody near, the key taken off "
            + "the vendor, the door shut behind and locked, and the key put back before walking on. Once a house is "
            + "registered, its Private or Public mode decides and this is greyed out.");
        m_houseLockBox.CheckedChanged += (s, e) => m_f.HouseLocked = m_houseLockBox.Checked;

        // The house vendor (Damian, 2026-10-19: "add a vendor tab, with features for setting prices, bag loadouts with
        // pricing"; on the Houses tab, 2026-10-21: "move house vendor UI from vendor to houses"): the Bags tab's priced
        // bag handed over item by item at the pricer's prices or whole at one price and description, each price prompt
        // answered by the assist; the vendor's stock read by single clicks (AssistFeatures.VendorGive).
        g = Group("House vendor");
        FieldGrid vg = Grid(g);
        m_vendorNote = Words("");
        // The house picked here names the vendor this group and the mining storage use.
        m_vendorHouse = vg.Field("House", Pick(220),
            "The registered house whose vendor this works on. Target a vendor instead for one in no registered house.");
        Hold(m_vendorHouse);
        m_vendorHouse.SelectedIndexChanged += (s, e) => { if (m_fillingHouses
            || m_vendorHouse.SelectedItem
            == null) return; string? w = m_f.VendorHousePick((string)m_vendorHouse.SelectedItem); m_vendorNote.Text = w
            ?? m_f.HouseVendorStatus; };
        Button vendorAim = Btn("Target a vendor",
            () => { string? w = m_f.HouseVendorTarget(); m_vendorNote.Text = w ?? m_f.HouseVendorStatus; });
        m_tipper.SetToolTip(vendorAim, "A vendor in no registered house: target it. Its pack is the vendor's stock.");
        vg.Line(null, vendorAim);
        NumericUpDown wholePrice = vg.Field("Whole bag price", Num(0, AssistFeatures.VendedPriceMax, 0, v => { }, 100),
            "The price for the bag handed over whole; 0 takes the pricer's total for the bag.");
        TextBox wholeDesc = vg.Field("Description", new TextBox { Width = 220 },
            "Said before the price on a single click, as \"100 Reagent Bag.  Cost:3000\". Optional.");
        Button byItem = Btn("Hand over item by item", () => VendorHandOver(false, 0, ""));
        m_tipper.SetToolTip(byItem,
            "Every item in the Bags tab's priced bag (Bag pricer) goes into the vendor's pack at its unit price times "
            + "its amount; items with no price stay in the bag. Stand by the vendor.");
        Button wholeBag = Btn("Hand over whole", () => VendorHandOver(true, (int)wholePrice.Value, wholeDesc.Text));
        m_tipper.SetToolTip(wholeBag,
            "The Bags tab's priced bag (Bag pricer) itself goes into the vendor's pack, sold as one at the whole bag "
            + "price.");
        Button readStock = Btn("Read stock",
            () => { string? w = m_f.VendorReadStock(); if (w != null) m_vendorNote.Text = w; });
        m_tipper.SetToolTip(readStock,
            "Opens the vendor's pack and clicks each item once for its price and description.");
        vg.Line(null, Strip(byItem, wholeBag, readStock, Btn("Stop", () => m_f.VendorStop())));
        vg.Line(null, m_vendorNote);
        m_vendorRows = Add(g,
            new SortList { View = View.Details, Height = PricerMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Item:160", "Description:160", "Price:70", "Serial:80" })
        {
            string[] a = c.Split(':');
            m_vendorRows.Columns.Add(a[0], int.Parse(a[1]));
        }
        FillHouses();
    }

    // The vendor handover's entries from the pricer's bag: each item at its unit price times its amount, or the bag
    // whole.
    Label m_vendorNote = null!; SortList m_vendorRows = null!; long m_vendorSeen;
    void VendorHandOver(bool wholeBag, int price, string desc)
    {
        if (m_f.PricerBag == 0)
        {
            m_vendorNote.Text = "Price a bag first (Bag pricer, Target a bag).";
            return;
        }
        ValueMap<string, int> unit = ValueMap<string, int>.Empty;
        int total = 0;
        foreach (PriceRow r in PriceRows())
        {
            unit = unit.Set(r.Key, r.Unit);
            total += r.Unit * r.Qty;
        }
        List<AssistFeatures.VendorEntry> list = new();
        int unpriced = 0;
        if (wholeBag)
        {
            int p = price > 0 ? price : total;
            if (p <= 0)
            {
                m_vendorNote.Text = "The bag has no price: type a whole bag price.";
                return;
            }
            list.Add(new AssistFeatures.VendorEntry { Serial = m_f.PricerBag, Amount = 1, Price = p, What = "the bag",
                Description = desc });
        }
        else
        {
            foreach (AssistItem it in m_f.PricerItems())
            {
                string name = UoTiles.Name(it.Graphic);
                int u, n = Math.Max(1, it.Amount);
                if (!unit.TryGet(PriceKey(name), out u) || u <= 0)
                {
                    unpriced++;
                    continue;
                }
                list.Add(new AssistFeatures.VendorEntry { Serial = it.Serial, Amount = n, Price = u * n,
                    What = (n > 1 ? n + " " : "") + name });
            }
        }

        string? w = m_f.VendorGive(list, unpriced > 0 ? unpriced + " unpriced items stay in the bag." : "");
        if (w != null)
            m_vendorNote.Text = w;
    }
    void FillVendor()
    {
        if (m_vendorRows == null)
            return;
        m_vendorRows.BeginUpdate();
        m_vendorRows.Items.Clear();
        foreach (string[] r in m_f.VendorStock())
            m_vendorRows.Items.Add(new ListViewItem(r));
        m_vendorRows.EndUpdate();
    }

    SortList m_houseRows = null!;
    Label m_housesNote = null!; long m_housesSeen = -1; ComboBox m_vendorHouse = null!; bool m_fillingHouses;
    void FillHouses()
    {
        if (m_houseRows == null)
            return;
        m_housesSeen = m_f.HouseSeq;
        List<AssistFeatures.HouseRec> hs = m_f.Houses();
        m_houseRows.BeginUpdate();
        m_houseRows.Items.Clear();
        foreach (AssistFeatures.HouseRec h in hs)
            m_houseRows.Items.Add(new ListViewItem(new[] { h.Name, h.Private ? "private" : "public",
                h.DoorX + "," + h.DoorY, h.Vendor.ToString("X"), h.Ring.ToString("X"),
                h.Devices.Length > 0 ? h.Devices : "storage" }));
        m_houseRows.EndUpdate();
        if (m_houseLockBox != null)
            m_houseLockBox.Enabled = hs.Count == 0;
        if (m_vendorHouse == null || m_dropped > 0)
            return;
        m_fillingHouses = true;
        try
        {
            m_vendorHouse.Items.Clear();
            string on = m_f.VendorHouseName();
            foreach (AssistFeatures.HouseRec h in hs)
            {
                m_vendorHouse.Items.Add(h.Name);
                if (h.Name == on)
                    m_vendorHouse.SelectedIndex = m_vendorHouse.Items.Count - 1;
            }
        }
        finally { m_fillingHouses = false; }
    }
}
