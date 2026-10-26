#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

// Picks one item type: carried ones first, then ones on the ground nearby, then stackables, then by weight; a filter
// box narrows by name. Double-click or Add returns the graphic.
public sealed class ItemPicker : Form
{
    const int PickIcon = 32;
    int m_picked = -1; readonly ListView m_list; readonly TextBox m_filter; readonly List<string[]> m_rows = new();
    public static int Pick(IWin32Window owner, string title, Set<int> carried, Set<int> near)
    {
        using (ItemPicker p = new(title, carried, near))
        {
            p.ShowDialog(owner);
            return p.m_picked;
        }
    }
    ItemPicker(string title, Set<int> carried, Set<int> near)
    {
        Text = title;
        Size = new Size(480, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 8.5f);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        m_filter = new TextBox { Dock = DockStyle.Top };
        m_list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        m_list.Columns.Add("Item", 250);
        m_list.Columns.Add("Where", 70);
        m_list.Columns.Add("Weight", 50);
        m_list.Columns.Add("Graphic", 55);
        // Each row shows the item's art (Damian, 2026-10-18: "add graphical icons of each that can be shown, since
        // there are lots of books for example... which one?"), drawn as the row is painted: an icon costs a scan of its
        // art, too slow for every row of the list at once. The image list only sets the row height.
        m_list.SmallImageList = new ImageList { ImageSize = new Size(PickIcon, PickIcon) };
        m_list.OwnerDraw = true;
        m_list.DrawColumnHeader += (s, e) => e.DrawDefault = true;
        m_list.DrawSubItem += (s, e) =>
        {
            bool on = e.Item!.Selected;
            Color back = on ? SystemColors.Highlight : SystemColors.Window;
            Color fore = on ? SystemColors.HighlightText : SystemColors.WindowText;
            using (SolidBrush br = new(back))
                e.Graphics.FillRectangle(br, e.Bounds);
            Rectangle r = e.Bounds;
            if (e.ColumnIndex == 0)
            {
                Bitmap? art = UoArt.Icon(Convert.ToInt32(e.Item.SubItems[3].Text, 16));
                if (art != null)
                {
                    float k = Math.Min((float)(PickIcon - 2) / art.Width, (float)(PickIcon - 2) / art.Height);
                    int w = (int)(art.Width * k), h = (int)(art.Height * k);
                    e.Graphics.DrawImage(art, r.X + 2 + (PickIcon - 2 - w) / 2, r.Y + (r.Height - h) / 2, w, h);
                }
                r = new Rectangle(r.X + PickIcon + 6, r.Y, r.Width - PickIcon - 6, r.Height);
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, m_list.Font, r, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.RightToLeft
        };
        Button ok = new()
        {
            Text = "Add"
        };
        Button cancel = new()
        {
            Text = "Cancel"
        };
        ok.Click += (s, e) => Choose();
        cancel.Click += (s, e) => Close();
        m_list.DoubleClick += (s, e) => Choose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(m_list);
        Controls.Add(m_filter);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 18, Text = "Type to filter by name." });
        Controls.Add(buttons);
        List<UoTiles.Tile> tiles = new();
        foreach (UoTiles.Tile t in UoTiles.Items())
            if (UoTiles.Liftable(t) || carried.Contains(t.Graphic) || near.Contains(t.Graphic))
                tiles.Add(t);
        Func<UoTiles.Tile, int> rank = t =>
            (carried.Contains(t.Graphic) ? 0 : near.Contains(t.Graphic) ? 1 : (t.Flags & 0x800) != 0 ? 2 : 3);
        tiles.Sort((a, b) => { int d = rank(a).CompareTo(rank(b)); if (d
            != 0) return d; d = a.Weight.CompareTo(b.Weight); return d != 0 ? d
            : string.CompareOrdinal(a.Name, b.Name); });
        foreach (UoTiles.Tile t in tiles)
            m_rows.Add(new[] { t.Name.Length > 0 ? t.Name : t.Graphic.ToString("X4"),
                carried.Contains(t.Graphic) ? "carried"
                : near.Contains(t.Graphic) ? "nearby" : (t.Flags & 0x800) != 0 ? "stacks" : "", t.Weight.ToString(),
                t.Graphic.ToString("X4") });
        m_filter.TextChanged += (s, e) => Fill();
        Fill();
        AcceptButton = ok;
        CancelButton = cancel;
    }
    void Fill()
    {
        string q = m_filter.Text.Trim();
        m_list.BeginUpdate();
        m_list.Items.Clear();
        int shown = 0;
        foreach (string[] r in m_rows)
        {
            if (q.Length > 0 && r[0].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                && r[3].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            m_list.Items.Add(new ListViewItem(r));
            if (++shown >= 2000)
                break;
        }
        m_list.EndUpdate();
    }
    void Choose()
    {
        if (m_list.SelectedItems.Count == 0)
            return;
        m_picked = Convert.ToInt32(m_list.SelectedItems[0].SubItems[3].Text, 16);
        DialogResult = DialogResult.OK;
        Close();
    }
}
