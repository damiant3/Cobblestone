// A section's controls on one grid (Damian, 2026-10-19: "laying things out in a grid not flow pattern, aligned nicely
// with the rows before and after, grouped into logical units, not wrapping labels and controls"). A field is a caption
// and its control in one slot, and every slot of a grid is as wide as the grid's widest caption plus its widest
// control, so the captions and the controls stand in columns and a row holds as many slots as the width allows. A line
// (a caption, then one control to the end of the row), a heading, a fold and a wide control each take a row of their
// own; the lines' captions share a column of their own. Nothing wraps.
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Uoaix.Assist;

public sealed class FieldGrid : Panel
{
    public const int RowH = 28;
    public const int HeadH = 20;
    public const int Gap = 14;
    public const int CapGap = 4;

    enum Kind
    {
        Slot, Line, Head, Wide
    }

    sealed class Item
    {
        public Kind Kind;
        public Control? Cap;
        public Control? Ctl;
        public bool Hidden;
        public bool Fill;
        public bool Shut;
        public Item? Fold;
    }

    readonly List<Item> m_items = new();
    readonly ToolTip m_tips;
    readonly Action m_changed;
    Item? m_fold;

    public bool Banded;

    // Damian, 2026-10-21: "a little less padding between the show types options".
    public int SlotGap = Gap;

    public FieldGrid(ToolTip tips, Action changed)
    {
        m_tips = tips;
        m_changed = changed;
        Margin = new Padding(0, 1, 0, 1);
        Height = RowH;
    }

    static Label Caption(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(0) };
    }

    Item Put(Kind kind, Control? cap, Control? ctl, string? tip, bool fill = false)
    {
        Item it = new()
        {
            Kind = kind,
            Cap = cap,
            Ctl = ctl,
            Fill = fill,
            Fold = m_fold
        };
        foreach (Control? c in new[] { cap, ctl })
        {
            if (c is null)
                continue;
            Controls.Add(c);
            if (tip is not null)
                m_tips.SetToolTip(c, tip);
        }
        m_items.Add(it);
        return it;
    }

    public T Field<T>(string caption, T ctl, string? tip = null) where T : Control
    {
        Put(Kind.Slot, Caption(caption), ctl, tip);
        return ctl;
    }

    // A slot whose caption is a control of its own: an action's button beside its key, a checkbox beside its number.
    public T Pair<T>(Control cap, T ctl, string? tip = null) where T : Control
    {
        Put(Kind.Slot, cap, ctl, tip);
        return ctl;
    }

    public T Cell<T>(T ctl, string? tip = null) where T : Control
    {
        Put(Kind.Slot, null, ctl, tip);
        return ctl;
    }

    public T Line<T>(string? caption, T ctl, string? tip = null, bool fill = false) where T : Control
    {
        Put(Kind.Line, caption is null ? null : Caption(caption), ctl, tip, fill);
        return ctl;
    }

    public T Wide<T>(T ctl, string? tip = null) where T : Control
    {
        Put(Kind.Wide, null, ctl, tip);
        return ctl;
    }

    public Label Head(string text)
    {
        Label l = Caption(text);
        Put(Kind.Head, l, null, null);
        return l;
    }

    // A heading that folds the items after it, up to the next fold; kept tells the owner the new state to remember.
    public void Fold(string text, bool shut, Action<bool> kept)
    {
        Button b = new()
        {
            Text = (shut ? "[+] " : "[-] ") + text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        b.FlatAppearance.BorderSize = 0;
        m_fold = null;
        Item it = Put(Kind.Head, b, null, null);
        it.Shut = shut;
        m_fold = it;
        b.Click += (s, e) =>
        {
            it.Shut = !it.Shut;
            b.Text = (it.Shut ? "[+] " : "[-] ") + text;
            kept(it.Shut);
            m_changed();
        };
    }

    public void Show(Control c, bool on)
    {
        bool moved = false;
        foreach (Item it in m_items)
        {
            if ((it.Ctl == c || it.Cap == c) && it.Hidden == on)
            {
                it.Hidden = !on;
                moved = true;
            }
        }
        if (moved)
            m_changed();
    }

    public Control? CaptionOf(Control c)
    {
        foreach (Item it in m_items)
        {
            if (it.Ctl == c)
                return it.Cap;
        }
        return null;
    }

    public void Empty()
    {
        foreach (Item it in m_items)
        {
            foreach (Control? c in new[] { it.Cap, it.Ctl })
            {
                if (c is null)
                    continue;
                Controls.Remove(c);
                c.Dispose();
            }
        }
        m_items.Clear();
        m_fold = null;
    }

    static bool Shown(Item it)
    {
        return !it.Hidden && (it.Fold is null || !it.Fold.Shut);
    }

    // A label, checkbox, button or strip sized to its content measures by its preferred size; any other control (a text
    // box is AutoSize for its height alone) by its own.
    static bool Fits(Control c)
    {
        return c.AutoSize && (c is Label || c is ButtonBase || c is FlowLayoutPanel);
    }

    static int W(Control c)
    {
        return Fits(c) ? c.PreferredSize.Width : c.Width;
    }

    static int H(Control c)
    {
        return Fits(c) ? c.PreferredSize.Height : c.Height;
    }

    static void Place(Control c, int x, int y, int h)
    {
        int ch = H(c);
        c.Location = new Point(x, y + Math.Max(0, (h - ch) / 2));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange(Width);
    }

    // Places every shown item at the width given and answers the height they take.
    public int Arrange(int width)
    {
        // Lines keep a caption column of their own, as wide as their longest caption, so a short line caption ("Bags")
        // is not pushed out to the slots' widest one.
        int cap = 0;
        int ctl = 0;
        int cell = 0;
        int lineCap = 0;
        foreach (Item it in m_items)
        {
            if (!Shown(it))
                continue;
            if (it.Kind == Kind.Slot)
            {
                if (it.Cap is null)
                {
                    cell = Math.Max(cell, W(it.Ctl!));
                }
                else
                {
                    cap = Math.Max(cap, W(it.Cap));
                    ctl = Math.Max(ctl, W(it.Ctl!));
                }
            }
            else if (it.Kind == Kind.Line && it.Cap is not null)
            {
                lineCap = Math.Max(lineCap, W(it.Cap));
            }
        }
        // Banded: each heading stands in a column of its own, level with the first row of its slots.
        int band = 0;
        if (Banded)
        {
            foreach (Item it in m_items)
            {
                if (it.Kind == Kind.Head && Shown(it))
                    band = Math.Max(band, W(it.Cap!));
            }
            if (band > 0)
                band += Gap;
        }
        int slot = Math.Max(cell, cap + CapGap + ctl);
        int per = Math.Max(1, (width - band + SlotGap) / (slot + SlotGap));
        int y = 0;
        int col = 0;
        foreach (Item it in m_items)
        {
            bool on = Shown(it);
            if (it.Cap is not null && it.Cap.Visible != on)
                it.Cap.Visible = on;
            if (it.Ctl is not null && it.Ctl.Visible != on)
                it.Ctl.Visible = on;
            if (!on)
                continue;
            if (it.Kind == Kind.Slot)
            {
                if (col == per)
                {
                    y += RowH;
                    col = 0;
                }
                int x = band + col * (slot + SlotGap);
                if (it.Cap is null)
                {
                    Place(it.Ctl!, x, y, RowH);
                }
                else
                {
                    Place(it.Cap, x, y, RowH);
                    Place(it.Ctl!, x + cap + CapGap, y, RowH);
                }
                col++;
                continue;
            }
            if (col > 0)
            {
                y += RowH;
                col = 0;
            }
            if (it.Kind == Kind.Head && it.Cap is Label head && !head.Font.Bold)
                head.Font = new Font(Font, FontStyle.Bold);
            if (Banded && it.Kind == Kind.Head && it.Cap is Label)
            {
                Place(it.Cap, 0, y, RowH);
                continue;
            }
            int h = it.Kind == Kind.Head ? HeadH : Math.Max(RowH, H(it.Ctl!) + 2);
            if (it.Kind == Kind.Line)
            {
                int x = it.Cap is null ? 0 : lineCap + CapGap + 4;
                if (it.Cap is not null)
                    Place(it.Cap, 0, y, h);
                if (it.Fill)
                    it.Ctl!.Width = Math.Max(60, width - x);
                Place(it.Ctl!, x, y, h);
            }
            else
            {
                Place(it.Cap ?? it.Ctl!, 0, y, h);
            }
            y += h;
        }
        if (col > 0)
            y += RowH;
        return Math.Max(4, y);
    }
}
