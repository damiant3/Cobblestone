#nullable enable
using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildVendors()
    {
        // Player vendors: every vendor whose pack the catalogue read when Damian opened it, with every item in its pack
        // and bags, and the name an identify gave each magic item.
        Panel g = Group("Player vendors");
        FieldGrid vendorsGrid = Grid(g);
        m_vendorsFind = vendorsGrid.Line("Find an item", new TextBox { Width = 200 });
        m_vendorsFind.TextChanged += (s, e) => { m_vendorsShape = ""; FillVendors(); };
        m_vendorsNote = vendorsGrid.Line(null,
            Words("Open a player vendor's pack (double-click the vendor) to read it. A wand of identification in your "
            + "pack identifies its magic items, one charge each."));
        m_vendorsList = Add(g,
            new SortList { View = View.Details, Height = 180, FullRowSelect = true, AutoSort = true });
        foreach (string c in new[] { "Vendor:150", "Wares:150", "Where:240", "Tiles:50", "Items:45", "For sale:60",
            "Magic:50", "Identified:70", "Read:80" })
        {
            string[] a = c.Split(':');
            m_vendorsList.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_vendorItems = Add(g,
            new SortList { View = View.Details, Height = ShopItemsMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Item:200", "Identified:220", "Description:180", "Price:80", "Qty:45", "Bag:130",
            "Serial:75" })
        {
            string[] a = c.Split(':');
            m_vendorItems.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_vendorsList.SelectedIndexChanged += (s, e) => { if (!m_selecting
            && m_vendorsList.SelectedItems.Count > 0) ShowVendorItems((uint)m_vendorsList.SelectedItems[0].Tag!); };
    }

    TextBox m_vendorsFind = null!;
    Label m_vendorsNote = null!;
    SortList m_vendorsList = null!, m_vendorItems = null!; string m_vendorsShape = ""; uint m_vendorShown;
    void FillVendors()
    {
        if (m_f.Vendors == null || m_vendorsList == null || m_page != "vendors")
            return;
        string find = m_vendorsFind.Text.Trim();
        int px = m_mapView.CenterX, py = m_mapView.CenterY;
        List<VendorRecord> all = new(m_f.Vendors.All());
        if (find.Length > 0)
            all = all.FindAll(r => VendorHas(r, find));
        string shape = m_f.Vendors.Seq + "|" + find + "|" + all.Count + "|" + px / 8 + "," + py / 8;
        if (shape == m_vendorsShape)
            return;
        m_vendorsShape = shape;
        uint kept = m_vendorsList.SelectedItems.Count > 0 ? (uint)m_vendorsList.SelectedItems[0].Tag! : m_vendorShown;
        m_vendorsList.BeginUpdate();
        m_vendorsList.Items.Clear();
        foreach (VendorRecord r in all)
        {
            int d = Math.Max(Math.Abs(r.X - px), Math.Abs(r.Y - py)), sale = 0, magic = 0, known = 0;
            string? where = UoRegions.Where(r.X, r.Y);
            foreach (VendorItem i in r.Items)
            {
                if (i.ForSale)
                    sale++;
                if (i.Magic || i.Identified.Length > 0)
                    magic++;
                if (i.Identified.Length > 0)
                    known++;
            }
            m_vendorsList.Items.Add(new ListViewItem(new[] { r.Name, r.Wares,
                (where != null ? where + " " : "") + r.X + "," + r.Y, d.ToString(), r.Items.Length.ToString(),
                sale.ToString(), magic.ToString(), known.ToString(),
                r.ReadAt.ToString("MM-dd HH:mm") }) { Tag = r.Serial });
        }
        if (kept != 0)
            foreach (ListViewItem it in m_vendorsList.Items)
                if ((uint)it.Tag! == kept)
                {
                    m_selecting = true;
                    try
                    {
                        it.Selected = true;
                    }
                    finally { m_selecting = false; }
                }
        m_vendorsList.EndUpdate();
        if (kept != 0)
            ShowVendorItems(kept);
    }
    static bool VendorHas(VendorRecord r, string find)
    {
        if (r.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        foreach (VendorItem i in r.Items)
            if ((i.Name + " " + i.Identified + " " + i.Description).IndexOf(find, StringComparison.OrdinalIgnoreCase)
                >= 0)
                return true;
        return false;
    }
    // One vendor's items, each with the bag it lies in; a magic item not yet identified says so.
    void ShowVendorItems(uint serial)
    {
        m_vendorShown = serial;
        VendorRecord? r = m_f.Vendors?.Get(serial);
        m_vendorItems.BeginUpdate();
        m_vendorItems.Items.Clear();
        if (r != null)
        {
            foreach (VendorItem i in r.Items)
            {
                VendorItem? bag = VendorBook.Find(r, i.Bag);
                string ided = i.Identified.Length > 0 ? i.Identified : i.Magic ? "not identified" : "";
                m_vendorItems.Items.Add(new ListViewItem(new[] { i.Name, ided, i.Description, i.Price,
                    i.Amount.ToString(), bag != null ? bag.Name : "the vendor's pack",
                    i.Serial.ToString("X") }) { Tag = i.Serial });
            }
        }

        m_vendorItems.EndUpdate();
    }
}
