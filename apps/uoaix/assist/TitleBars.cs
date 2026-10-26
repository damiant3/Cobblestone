#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace Uoaix.Assist;

// Hits, mana and stamina as three stacked bars over the right of the client's title bar (Damian, 2026-10-15: "uo assist
// puts a graphic 3 bars stacked in the title bar of the client for hits, mana, stamina ... can we do similar").
// UOAssist paints inside the client; this is a window of the assist's own laid over the title bar, which never takes
// focus and lets every click through to the client beneath.
public sealed class TitleBars : Form
{
    public const int BarsWidth = 120, CaptionButtons = 150;
    public int Hits, MaxHits, Mana, MaxMana, Stam, MaxStam;
    public TitleBars()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 24);
        DoubleBuffered = true;
        Bounds = new Rectangle(-32000, -32000, BarsWidth, 20);
    }
    protected override bool ShowWithoutActivation => true;
    // WS_EX_LAYERED | WS_EX_TRANSPARENT (clicks pass through) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE.
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams p = base.CreateParams;
            p.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
            return p;
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Opacity = 0.97;
    }
    public void Set(int hits, int maxHits, int mana, int maxMana, int stam, int maxStam)
    {
        if (hits == Hits && maxHits == MaxHits && mana == Mana && maxMana == MaxMana && stam == Stam
            && maxStam == MaxStam)
            return;
        Hits = hits;
        MaxHits = maxHits;
        Mana = mana;
        MaxMana = maxMana;
        Stam = stam;
        MaxStam = maxStam;
        Invalidate();
    }
    // Recall reagents under RecallLow (Damian, 2026-10-19: "add reagent low warnings for recall regs next to the 3
    // color health meter in the title bar. use the reagent icons and a warning symbol of some kind when they get under
    // 10"): each low one its icon, a red warning triangle and its count, left of the bars, the window widened RegSlot
    // for each.
    public const int RegSlot = 46;
    int[]? m_regs = null;
    int LowRegs
    {
        get
        {
            int n = 0;
            if (m_regs != null)
                foreach (int c in m_regs)
                    if (c < AssistFeatures.RecallLow)
                        n++;
            return n;
        }
    }
    public int FullWidth => BarsWidth + LowRegs * RegSlot;
    // True when the number of low reagents changed (the window is placed again for its width).
    public bool SetRegs(int[] counts)
    {
        int before = LowRegs;
        bool same = counts == null ? m_regs == null : m_regs != null && m_regs.Length == counts.Length;
        if (same && counts != null)
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] != m_regs![i])
                    same = false;
        if (same)
            return false;
        m_regs = counts;
        Invalidate();
        return LowRegs != before;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int pad = 3, gap = 1, h = Math.Max(3, (Height - pad * 2 - gap * 2) / 3), left = LowRegs
            * RegSlot, w = Width - left - pad * 2;
        if (m_regs != null)
        {
            int x = 2;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < m_regs.Length; i++)
            {
                if (m_regs[i] >= AssistFeatures.RecallLow)
                    continue;
                int side = Height - 2;
                Bitmap? icon = UoArt.Flat(AssistFeatures.RecallReagents[i]);
                if (icon != null)
                    g.DrawImage(icon, new Rectangle(x, 1, side, side));
                int t = Math.Max(8, Height / 2);
                Point[] tri = new[] { new Point(x + side - t, Height - 1), new Point(x + side, Height - 1),
                    new Point(x + side - t / 2, Height - 1 - t) };
                using (SolidBrush red = new(Color.FromArgb(230, 30, 30)))
                    g.FillPolygon(red, tri);
                int cx = x + side - t / 2, bang = Math.Max(1, t / 6);
                g.FillRectangle(Brushes.White, cx - bang / 2f, Height - 1 - t * 0.62f, bang, t * 0.32f);
                g.FillRectangle(Brushes.White, cx - bang / 2f, Height - 1 - t * 0.22f, bang, bang);
                using (Font f = new("Arial", Math.Max(6, t * 0.75f), FontStyle.Bold))
                    g.DrawString(m_regs[i].ToString(), f, Brushes.Orange, x + side + 1, (Height - f.Height) / 2f);
                x += RegSlot;
            }
        }
        int[][] rows = new[] { new[] { Hits, MaxHits }, new[] { Mana, MaxMana }, new[] { Stam, MaxStam } };
        Color[] colors = new[] { Color.FromArgb(220, 40, 40), Color.FromArgb(50, 110, 240),
            Color.FromArgb(230, 200, 40) };
        for (int i = 0; i < 3; i++)
        {
            int y = pad + i * (h + gap), fill = rows[i][1] > 0 ? Math.Max(0, Math.Min(w, w * rows[i][0] / rows[i][1]))
                : 0;
            using (SolidBrush back = new(Color.FromArgb(60, 60, 60)))
                g.FillRectangle(back, left + pad, y, w, h);
            using (SolidBrush b = new(colors[i]))
                g.FillRectangle(b, left + pad, y, fill, h);
        }
    }
}
