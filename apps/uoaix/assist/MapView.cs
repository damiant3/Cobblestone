// The map tab's view: the tiles around (CenterX, CenterY) at Zoom pixels a tile, the trail, the people and the player.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Uoaix.Assist;

public sealed class MapView : Control
{
    // Game view from IsoZoom (Damian, 2026-10-15: "same with buildings", "the cool thing was seeing the building with
    // all the correct tiling and such"): the client's own projection, a tile a 44 x 44 diamond (scaled), x to the lower
    // right, y to the lower left, 4 pixels up a z step, every land tile and static drawn with its art back to front.
    // From 4 px a tile (Damian, 2026-10-15: "theres still 3 levels of zoom where its like the symbols and the
    // transition doesn't look great"): 20 to 55 ms a view at 4, 6 and 8 (480 x 500, Britain and the Yew woods).
    // SignMin: a sign is at least this many px zoomed out (Damian, 2026-10-19: "make em bigger zoomed out").
    public const int IsoZoom = 4;
    public const int SignMin = 20;
    public const int StarFlashMs = 400;
    // From TextureZoom up the land is drawn with its texture (Damian, 2026-10-15: "can you make our maps look as good
    // on the zoom in as https://uomaps.com/"). Every zoom is hill-shaded.
    public const int TextureZoom = 4;

    // Zoom steps, in pixels a tile.
    static readonly ImmutableArray<int> s_zoomSteps = [ 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 ];

    // A creature's symbol in percent of the normal size by its TripLore level (Damian, 2026-10-16: "the size of the
    // symbols should be roughly equiv to the value/difficulty of the critter. a big red circle for a rat, versus the
    // same size for a liche is bad"): harmless 50, newbie 75, medium 125, hard 175, endgame 225; a name the table lacks
    // stays 100.
    static readonly ImmutableArray<int> s_tierScales = ImmutableArray.Create(50, 75, 125, 175, 225);

    static readonly ImmutableArray<string> s_pinKinds =
        ImmutableArray.Create("My house", "Friend's house", "Enemy's house", "House test", "Other");

    public sealed record Mark
    {
        public int X
        {
            get; init;
        }
        public int Y
        {
            get; init;
        }
        public int Body
        {
            get; init;
        }
        public Color Color
        {
            get; init;
        }
        public uint Serial
        {
            get; init;
        }
        public int Scale { get; init; } = 100;
    }

    public sealed record Pin
    {
        public int X
        {
            get; init;
        }
        public int Y
        {
            get; init;
        }
        public string Kind { get; init; } = "Other";
        public string Label { get; init; } = "";
    }

    public static ImmutableArray<int> ZoomSteps => s_zoomSteps;
    public static ImmutableArray<string> PinKinds => s_pinKinds;

    public int CenterX
    {
        get; set;
    }
    public int CenterY
    {
        get; set;
    }
    public int Zoom { get; set; } = 2;

    public Func<Point[]>? Trail;
    public Action<int, int>? Hover;
    public Func<AssistFeatures.Corpse[]>? Corpses;
    public Func<Mark[]>? Marks;
    public Func<bool>? ShowRegions;
    // Harvested 8x8 chunks: x, y of the corner and readiness per mille (0 empty, 1000 ready); null draws none.
    public Func<List<int[]>?>? Chunks;
    // Piles dropped for a collection: x, y and the amount each, shown with the chunks so they can be harvested by hand
    // (Damian, 2026-10-22: "include drop spots for manual harvest if it comes to that"); null draws none.
    public Func<List<int[]>?>? Drops;
    // The lumberjack's places (AssistFeatures.LumberSpots: x, y, kind 0 sentinel, 1 collection point, 2 Chop from),
    // each a labelled dot at every zoom; null draws none.
    public Func<List<int[]>?>? Spots;
    // Resource piles seen on the ground (AssistFeatures.ResourceSpots: x, y, kind 1 ore, 2 feathers, 3 hides, 4
    // reagents, amount), each a tile in its kind's colour with its amount; null draws none.
    public Func<List<int[]>?>? Resources;
    // The server's decoration kept by the catalogue (AssistFeatures.DecorIn: graphic, x, y, z on the box's tiles),
    // drawn among each tile's statics on the game view; DecorSeq moves when it changes. Null draws none.
    public Func<int, int, int, int, List<int[]>>? Decor;
    public Func<int>? DecorSeq;
    static readonly ImmutableArray<Color> s_resourceColors = ImmutableArray.Create(Color.FromArgb(139, 90, 43),
        Color.SlateGray, Color.WhiteSmoke, Color.Peru, Color.MediumPurple);
    public static ImmutableArray<Color> ResourceColors => s_resourceColors;
    public Action<uint>? Picked;
    public Action<uint>? Opened;
    public uint Selected;
    public Func<string>? Caption;
    public Func<AssistFeatures.Waypoint[]>? Course;
    public Func<AssistFeatures.KillMark[]>? Kills;
    public Func<int[][]>? Route;
    public Func<string>? RouteLabel;
    public Func<Pin[]>? Pins;
    public Action<int, int, Point>? RightClick;
    // Where the hovered teleport-only spot can be cast from: flashing gold stars (Damian, 2026-10-19: "a new symbol
    // should be a star and flashing gold").
    public Func<int[][]>? Stars;
    // The house pretest (AssistHousePlacement.md stage 3): a left click taken by HouseClick while a house is picked,
    // and its footprint's tiles (x, y, 1 fits or 0 fails) filled green or red.
    public Func<int, int, bool>? HouseClick;
    public Func<List<int[]>>? HouseTiles;
    // The player's registered houses: each one's door tile and name.
    public Func<List<(int X, int Y, string Name)>>? OwnHouses;
    // Dragging pans the view off the player by (OffX, OffY) tiles (Damian, 2026-10-15: "in the map, let drag move
    // around"); a double-click on empty map comes back to the player.
    public int OffX;
    public int OffY;

    Mark[] m_shown = new Mark[0];
    Point m_dragFrom;
    int m_dragOffX;
    int m_dragOffY;
    bool m_dragging;
    bool m_dragged;
    bool m_starsShown;
    Bitmap? m_tiles;
    int m_drawnX = -1;
    int m_drawnY = -1;
    int m_drawnZoom;
    int m_drawnW;
    int m_drawnH;
    int m_drawnMultis;
    int m_drawnDecor;
    int m_drawnScale = 1;
    readonly List<int[]> m_canopies = new();

    public MapView()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
        SetStyle(ControlStyles.Selectable, true);
        Timer flash = new()
        {
            Interval = StarFlashMs
        };
        flash.Tick += (s, e) =>
        {
            if (m_starsShown)
                Invalidate();
        };
        flash.Start();
    }

    public static int TierScale(string name)
    {
        TripLore.Lore? l = TripLore.Of(name);
        return l is null ? 100 : s_tierScales[Math.Max(0, Math.Min(4, l.Tier))];
    }

    public static Color PinColor(string kind)
    {
        return kind switch
        {
            "My house" => Color.Gold,
            "Friend's house" => Color.LimeGreen,
            "Enemy's house" => Color.Red,
            "House test" => Color.Cyan,
            _ => Color.White,
        };
    }

    public Point TileAt(Point p)
    {
        return ToTile(p.X, p.Y);
    }

    public Point PixelOf(int x, int y)
    {
        return ToPixel(x, y);
    }

    int ViewX => CenterX + OffX;
    int ViewY => CenterY + OffY;
    // Every zoom shares the game's orientation (Damian, 2026-10-15: "when you get zooomed in, it switches the
    // orientation"): below IsoZoom the radar colours are laid out on the same diamonds, Zoom pixels from a tile's
    // centre to its corner.
    bool Iso => true;
    int IsoPct => Zoom * 100 / 22;
    double Half => Zoom >= IsoZoom ? 22.0 * IsoPct / 100 : Zoom;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        m_dragging = true;
        m_dragged = false;
        m_dragFrom = e.Location;
        m_dragOffX = OffX;
        m_dragOffY = OffY;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;
        if (m_dragging && m_dragged)
            Invalidate();
        m_dragging = false;
    }

    Mark? Nearest(Point p)
    {
        Mark? best = null;
        int bestD = 100;
        foreach (Mark m in m_shown)
        {
            Point at = ToPixel(m.X, m.Y);
            int d = (at.X - p.X) * (at.X - p.X) + (at.Y - p.Y) * (at.Y - p.Y);
            if (d <= bestD)
            {
                bestD = d;
                best = m;
            }
        }
        return best;
    }

    // A click picks the nearest mark within 10 pixels; the end of a drag is not a click.
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (m_dragged)
            return;
        if (e.Button == MouseButtons.Right)
        {
            Point t = ToTile(e.X, e.Y);
            RightClick?.Invoke(t.X, t.Y, e.Location);
            return;
        }
        if (HouseClick is not null)
        {
            Point t = ToTile(e.X, e.Y);
            if (HouseClick(t.X, t.Y))
                return;
        }
        Mark? best = Nearest(e.Location);
        Selected = best is not null ? best.Serial : 0;
        Picked?.Invoke(Selected);
        Invalidate();
    }

    // A double-click on a mark opens it (a vendor's Shops entry, Damian, 2026-10-15); on empty map it re-centres.
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        Mark? best = Nearest(e.Location);
        if (best is not null && best.Serial != 0)
        {
            Opened?.Invoke(best.Serial);
        }
        else
        {
            OffX = OffY = 0;
            Invalidate();
        }
    }

    static Point[] StarPoints(Point c, int r)
    {
        Point[] pts = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            int ri = i % 2 == 0 ? r : r * 2 / 5;
            pts[i] = new Point(c.X + (int)Math.Round(ri * Math.Cos(a)), c.Y + (int)Math.Round(ri * Math.Sin(a)));
        }
        return pts;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (m_dragging && (e.Button & MouseButtons.Left) != 0)
        {
            int dx = e.X - m_dragFrom.X;
            int dy = e.Y - m_dragFrom.Y;
            if (m_dragged || Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            {
                m_dragged = true;
                if (Iso)
                {
                    double a = dx / Half;
                    double b = dy / Half;
                    OffX = m_dragOffX - (int)Math.Round((a + b) / 2);
                    OffY = m_dragOffY - (int)Math.Round((b - a) / 2);
                }
                else
                {
                    OffX = m_dragOffX - dx / Zoom;
                    OffY = m_dragOffY - dy / Zoom;
                }
                Invalidate();
            }
        }
        if (Hover is not null)
        {
            Point t = ToTile(e.X, e.Y);
            Hover(t.X, t.Y);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Hover?.Invoke(-1, -1);
    }

    bool Drawn(int w, int h)
    {
        return m_tiles is not null && m_drawnX == ViewX && m_drawnY == ViewY && m_drawnZoom == Zoom && m_drawnW == w
            && m_drawnH == h;
    }

    void Remember(int w, int h, int scale)
    {
        m_drawnX = ViewX;
        m_drawnY = ViewY;
        m_drawnZoom = Zoom;
        m_drawnW = w;
        m_drawnH = h;
        m_drawnScale = scale;
    }

    Bitmap Canvas(int w, int h, PixelFormat format, bool sameFormat)
    {
        bool fits = m_tiles is not null && m_tiles.Width == w && m_tiles.Height == h;
        if (m_tiles is null || !fits || (sameFormat && m_tiles.PixelFormat != format))
        {
            m_tiles?.Dispose();
            m_tiles = new Bitmap(w, h, format);
        }
        return m_tiles;
    }

    static void Blit(Bitmap target, int[] px)
    {
        BitmapData data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        target.UnlockBits(data);
    }

    void RenderRadarIso()
    {
        int W = Width;
        int H = Height;
        double half = Half;
        if (Drawn(W, H))
            return;
        Bitmap tiles = Canvas(W, H, PixelFormat.Format32bppArgb, true);
        int[] px = new int[W * H];
        int black = unchecked((int)0xFF000000);
        for (int py = 0; py < H; py++)
        {
            double b = (py - H / 2.0) / half;
            for (int pxx = 0; pxx < W; pxx++)
            {
                double a = (pxx - W / 2.0) / half;
                int x = ViewX + (int)Math.Floor((a + b) / 2 + 0.5);
                int y = ViewY + (int)Math.Floor((b - a) / 2 + 0.5);
                UoRadar.Cells? blk = x < 0 || y < 0 ? null : UoRadar.Block(x >> 3, y >> 3);
                if (blk is null)
                {
                    px[py * W + pxx] = black;
                    continue;
                }
                int cell = (y & 7) * 8 + (x & 7);
                int z = blk.Z[cell];
                px[py * W + pxx] = UoRadar.Shade(UoRadar.ToArgb(blk.Color[cell]), z, UoRadar.TopZ(x - 1, y - 1, z));
            }
        }
        Blit(tiles, px);
        Remember(W, H, 1);
    }

    Point ToTile(int px, int py)
    {
        if (Iso)
        {
            double a = (px - Width / 2.0) / Half;
            double b = (py - Height / 2.0) / Half;
            return new Point(ViewX + (int)Math.Round((a + b) / 2), ViewY + (int)Math.Round((b - a) / 2));
        }
        int tw = Width / Zoom;
        int th = Height / Zoom;
        return new Point(ViewX - tw / 2 + px / Zoom, ViewY - th / 2 + py / Zoom);
    }

    Point ToPixel(int x, int y)
    {
        if (Iso)
        {
            int dx = x - ViewX;
            int dy = y - ViewY;
            int lz = UoRadar.LandZAt(x, y, 0);
            return new Point((int)(Width / 2 + (dx - dy) * Half),
                (int)(Height / 2 + (dx + dy) * Half - lz * 4 * Half / 22));
        }
        int tw = Width / Zoom;
        int th = Height / Zoom;
        return new Point((x - (ViewX - tw / 2)) * Zoom + Zoom / 2, (y - (ViewY - th / 2)) * Zoom + Zoom / 2);
    }

    void RenderIso()
    {
        int W = Width;
        int H = Height;
        int pct = IsoPct;
        double half = Half;
        double zk = 4 * half / 22;
        int decorSeq = DecorSeq?.Invoke() ?? 0;
        if (Drawn(W, H) && m_drawnMultis == UoMap.MultiSeq && m_drawnDecor == decorSeq)
            return;
        m_drawnMultis = UoMap.MultiSeq;
        m_drawnDecor = decorSeq;
        Bitmap tiles = Canvas(W, H, PixelFormat.Format32bppPArgb, false);
        int R = (int)((W + H) / (2 * half)) + 8;
        Map<long, List<int[]>> fills = DecorByTile(ViewX - R, ViewY - R, ViewX + R, ViewY + R);
        using (Graphics g = Graphics.FromImage(tiles))
        {
            g.Clear(Color.Black);
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            // Back to front: by x + y, then x; on a tile its land, then its statics lowest first.
            for (int d = -2 * R; d <= 2 * R; d++)
            {
                for (int dx = -R; dx <= R; dx++)
                {
                    int dy = d - dx;
                    if (dy < -R || dy > R)
                        continue;
                    double sx = W / 2.0 + (dx - dy) * half;
                    double sy = H / 2.0 + (dx + dy) * half;
                    if (sx < -2 * half || sx > W + 2 * half || sy < -2 * half || sy > H + 130 * zk)
                        continue;
                    int x = ViewX + dx;
                    int y = ViewY + dy;
                    if (x < 0 || y < 0)
                        continue;
                    UoRadar.Cells? blk = UoRadar.Block(x >> 3, y >> 3);
                    if (blk is null)
                        continue;
                    DrawIsoTile(g, blk, x, y, sx, sy, half, zk, pct, fills[((long)x << 16) | (uint)y]);
                }
            }
        }
        Remember(W, H, 1);
    }

    Map<long, List<int[]>> DecorByTile(int x0, int y0, int x1, int y1)
    {
        List<int[]>? items = Decor?.Invoke(x0, y0, x1, y1);
        if (items is null || items.Count == 0)
            return Map<long, List<int[]>>.Empty;
        ImmutableDictionary<long, List<int[]>>.Builder byTile = ImmutableDictionary.CreateBuilder<long, List<int[]>>();
        foreach (int[] d in items)
        {
            long t = ((long)d[1] << 16) | (uint)d[2];
            if (!byTile.TryGetValue(t, out List<int[]>? l))
                byTile[t] = l = new List<int[]>();
            l.Add(d);
        }
        return Map<long, List<int[]>>.From(byTile.ToImmutable());
    }

    static void DrawIsoTile(Graphics g, UoRadar.Cells blk, int x, int y, double sx, double sy, double half, double zk,
        int pct, List<int[]>? fill)
    {
        int cell = (y & 7) * 8 + (x & 7);
        Bitmap? land = UoArt.Scaled(blk.Land[cell], pct);
        if (land is not null)
        {
            // Land is drawn flat at its own height, so a slope leaves a seam toward a lower neighbour: the diamond is
            // drawn again down to the lowest of its southern neighbours to close it.
            int lz = blk.LandZ[cell];
            int low = Math.Min(UoRadar.LandZAt(x + 1, y, lz),
                Math.Min(UoRadar.LandZAt(x, y + 1, lz), UoRadar.LandZAt(x + 1, y + 1, lz)));
            int lx = (int)(sx - land.Width / 2.0);
            int top = (int)(sy - half - lz * zk);
            int bottom = (int)(sy - half - low * zk);
            int step = Math.Max(1, (int)(half / 2));
            for (int fy = bottom; fy > top; fy -= step)
                g.DrawImageUnscaled(land, lx, fy);
            g.DrawImageUnscaled(land, lx, top);
        }
        foreach (int v in TileOrder(blk.ByCell[cell], UoMap.MultiAt(x, y), fill))
        {
            Bitmap? art = UoArt.Scaled(0x10000 + (v & 0xFFFF), pct);
            if (art is not null)
            {
                int top = (int)(sy + half - art.Height - (sbyte)(v >> 16) * zk);
                g.DrawImageUnscaled(art, (int)(sx - art.Width / 2.0), top);
            }
        }
    }

    // A tile's statics (graphic | z << 16, in their order), a house's parts (graphic, z, as MULTI.MUL lists them) and
    // the items lying there (graphic, x, y, z), drawn as one list by UoRadar.Priority, a stable insertion: on equal
    // priorities a static before a house part before an item. A house's parts drawn after every item cut its door's
    // foot off with the floor on the door's tile (Damian, 2026-10-23); sorted by z alone, a floor at a wall's z drew
    // over the wall's foot (2026-10-24; multi 0x6C's south and east edges, a floor 0x4AE to 0x4B3 and a wall 0x132 to
    // 0x13B at z 7).
    public static List<int> TileOrder(List<int>? statics, List<int[]> parts, List<int[]>? items)
    {
        List<int> here = statics is null ? new List<int>() : new List<int>(statics);
        foreach (int[] p in parts)
            InsertByPriority(here, p[0], p[1]);
        if (items is not null)
        {
            foreach (int[] d in items)
                InsertByPriority(here, d[0], d[3]);
        }

        return here;
    }

    static void InsertByPriority(List<int> here, int graphic, int z)
    {
        int pri = UoRadar.Priority(graphic, z);
        int j = here.Count;
        while (j > 0 && UoRadar.Priority(here[j - 1] & 0xFFFF, (sbyte)(here[j - 1] >> 16)) > pri)
            j--;
        here.Insert(j, (graphic & 0xFFFF) | (z & 0xFF) << 16);
    }

    void Render()
    {
        if (Zoom >= IsoZoom)
        {
            RenderIso();
            return;
        }
        if (Iso)
        {
            RenderRadarIso();
            return;
        }
        RenderFlat();
    }

    void RenderFlat()
    {
        int tw = Math.Max(1, Width / Zoom);
        int th = Math.Max(1, Height / Zoom);
        int x0 = ViewX - tw / 2;
        int y0 = ViewY - th / 2;
        if (Drawn(tw, th))
            return;
        bool tex = Zoom >= TextureZoom;
        int s = tex ? Zoom : 1;
        int bw = tw * s;
        int bh = th * s;
        Bitmap tiles = Canvas(bw, bh, PixelFormat.Format32bppArgb, false);
        int[] px = new int[bw * bh];
        int black = unchecked((int)0xFF000000);
        for (int ty = 0; ty < th; ty++)
        {
            int y = y0 + ty;
            UoRadar.Cells? blk = null;
            int lastBx = int.MinValue;
            for (int tx = 0; tx < tw; tx++)
            {
                int x = x0 + tx;
                int bx = x >> 3;
                if (x >= 0 && y >= 0 && bx != lastBx)
                {
                    blk = UoRadar.Block(bx, y >> 3);
                    lastBx = bx;
                }
                if (x < 0 || y < 0 || blk is null)
                {
                    for (int j = 0; j < s; j++)
                    {
                        for (int i = 0; i < s; i++)
                            px[(ty * s + j) * bw + tx * s + i] = black;
                    }
                    continue;
                }
                FlatTile(px, bw, blk, x, y, tx, ty, s, tex);
            }
        }
        DrawCanopies(px, bw, bh, x0, y0, s);
        Blit(tiles, px);
        Remember(tw, th, Zoom / s);
    }

    void FlatTile(int[] px, int bw, UoRadar.Cells blk, int x, int y, int tx, int ty, int s, bool tex)
    {
        int cell = (y & 7) * 8 + (x & 7);
        int z = blk.Z[cell];
        int zNw = UoRadar.TopZ(x - 1, y - 1, z);
        int flat = UoRadar.ToArgb(blk.Color[cell]);
        if (!tex)
        {
            px[ty * bw + tx] = UoRadar.Shade(flat, z, zNw);
            return;
        }
        // Foliage: the ground drawn beneath, and a canopy laid over it once the ground is down.
        int leafy = blk.Covered[cell] ? UoFoliage.Kind(blk.Static[cell]) : UoTex.IsWater(blk.Land[cell]) ? 3 : 0;
        if (leafy == 3)
        {
            // Water: the client's water radar colours run near black, so a blue with a ripple.
            for (int j = 0; j < s; j++)
            {
                for (int i = 0; i < s; i++)
                {
                    int wx = x * s + i;
                    int wy = y * s + j;
                    int ripple = (int)(10 * Math.Sin(wx * 0.35 + wy * 0.18) + 6 * Math.Sin(wy * 0.5 - wx * 0.12));
                    px[(ty * s + j) * bw + tx * s + i] = UoRadar.Scale(unchecked((int)0xFF1E4F86), 100 + ripple);
                }
            }
            return;
        }
        if (leafy == 2)
        {
            // A crown in leaf green when the radar colour is not green (a trunk's brown or grey), varied per tree.
            int r = (flat >> 16) & 255;
            int gr = (flat >> 8) & 255;
            int b = flat & 255;
            int leaf = gr > r + 8 && gr > b + 8
                ? flat
                : UoRadar.Scale(unchecked((int)0xFF2F5A26), 85 + (int)((uint)(x * 7919 + y * 104729) % 35));
            m_canopies.Add(new[] { tx * s + s / 2, ty * s + s / 2, s * 3 / 4 + 1, leaf });
        }
        else if (leafy == 1 && s >= 12)
        {
            m_canopies.Add(new[] { tx * s + s / 2, ty * s + s / 2, s / 4, flat });
        }
        int[]? t = blk.Covered[cell] && leafy == 0 ? null : UoTex.Land(blk.Land[cell], s);
        // A static's edge where the surface steps against its neighbour is drawn dark, so roofs, walls and buildings
        // keep their outline at close zoom.
        bool edgeW = false;
        bool edgeN = false;
        bool edgeE = false;
        bool edgeS = false;
        if (blk.Covered[cell] && leafy == 0 && s >= 8)
        {
            edgeW = Math.Abs(UoRadar.TopZ(x - 1, y, z) - z) >= 3;
            edgeN = Math.Abs(UoRadar.TopZ(x, y - 1, z) - z) >= 3;
            edgeE = Math.Abs(UoRadar.TopZ(x + 1, y, z) - z) >= 3;
            edgeS = Math.Abs(UoRadar.TopZ(x, y + 1, z) - z) >= 3;
        }
        for (int j = 0; j < s; j++)
        {
            for (int i = 0; i < s; i++)
            {
                int c = UoRadar.Shade(t is not null ? t[j * s + i] : flat, z, zNw);
                if ((edgeW && i == 0) || (edgeN && j == 0) || (edgeE && i == s - 1) || (edgeS && j == s - 1))
                    c = UoRadar.Shade(c, 0, 8);
                px[(ty * s + j) * bw + tx * s + i] = c;
            }
        }
    }

    // Canopies north to south, so a southern tree overlaps the one behind it: lit from the northwest, darker at the
    // rim, mottled per pixel so a crown reads as leaves rather than a disc.
    void DrawCanopies(int[] px, int bw, int bh, int x0, int y0, int s)
    {
        foreach (int[] c in m_canopies)
        {
            int cx = c[0];
            int cy = c[1];
            int r = c[2];
            int col = c[3];
            for (int y = Math.Max(0, cy - r); y <= Math.Min(bh - 1, cy + r); y++)
            {
                for (int x = Math.Max(0, cx - r); x <= Math.Min(bw - 1, cx + r); x++)
                {
                    int dx = x - cx;
                    int dy = y - cy;
                    int d2 = dx * dx + dy * dy;
                    if (d2 > r * r)
                        continue;
                    int hx = dx + r * 2 / 5;
                    int hy = dy + r * 2 / 5;
                    int light = 125 - (int)(55 * Math.Sqrt(hx * hx + hy * hy) / (r * 1.4 + 1));
                    uint h = (uint)((x0 * s + x) * 73856093) ^ (uint)((y0 * s + y) * 19349663);
                    h ^= h >> 13;
                    h *= 0x5bd1e995;
                    h ^= h >> 15;
                    light += (int)(h % 21) - 10;
                    if (d2 > r * r * 3 / 4)
                        light -= 18;
                    px[y * bw + x] = UoRadar.Scale(col, light);
                }
            }
        }
        m_canopies.Clear();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 4 || Height < 4)
            return;
        // A drag slides the last view and renders once at the mouse's release (Damian, 2026-10-21: "its pretty slow to
        // move around the map when its a little bigger than normal"; a render is every tile of the view, its area's
        // cost).
        bool sliding = m_dragging && m_dragged && m_tiles is not null && m_drawnZoom == Zoom && m_drawnW == Width
            && m_drawnH == Height && m_drawnScale == 1;
        if (!sliding)
            Render();
        Bitmap? tiles = m_tiles;
        if (tiles is null)
            return;
        Graphics gfx = e.Graphics;
        gfx.InterpolationMode = InterpolationMode.NearestNeighbor;
        gfx.PixelOffsetMode = PixelOffsetMode.Half;
        if (sliding)
        {
            int ddx = ViewX - m_drawnX;
            int ddy = ViewY - m_drawnY;
            gfx.Clear(Color.Black);
            gfx.DrawImageUnscaled(tiles, (int)Math.Round(-(ddx - ddy) * Half), (int)Math.Round(-(ddx + ddy) * Half));
        }
        else
        {
            gfx.DrawImage(tiles, new Rectangle(0, 0, tiles.Width * m_drawnScale, tiles.Height * m_drawnScale));
        }
        PaintTrail(gfx);
        PaintRegions(gfx);
        PaintChunks(gfx);
        PaintResources(gfx);
        PaintPlaces(gfx);
        PaintMarks(gfx);
        PaintStars(gfx);
        PaintPins(gfx);
        PaintSpots(gfx);
        PaintKills(gfx);
        PaintCorpses(gfx);
        PaintCourse(gfx);
    }

    void PaintTrail(Graphics gfx)
    {
        Point[] trail = Trail?.Invoke() ?? new Point[0];
        if (trail.Length <= 1)
            return;
        Point[] pts = new Point[trail.Length];
        for (int i = 0; i < trail.Length; i++)
            pts[i] = ToPixel(trail[i].X, trail[i].Y);
        using Pen pen = new(Color.FromArgb(200, 255, 220, 0), Math.Max(1, Math.Min(4, Zoom / 2)));
        gfx.DrawLines(pen, pts);
    }

    // The tiles the view spans, from ToTile of its corners, widened by pad.
    Rectangle ViewTiles(int pad)
    {
        Point c0 = ToTile(0, 0);
        Point c1 = ToTile(Width, 0);
        Point c2 = ToTile(0, Height);
        Point c3 = ToTile(Width, Height);
        int x0 = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X)) - pad;
        int x1 = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X)) + pad;
        int y0 = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y)) - pad;
        int y1 = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y)) + pad;
        return Rectangle.FromLTRB(x0, y0, x1, y1);
    }

    // The tile rectangle from (x0, y0) to (x1, y1), as a diamond in the game's orientation.
    Point[] Diamond(int x0, int y0, int x1, int y1)
    {
        return new[] { ToPixel(x0, y0), ToPixel(x1, y0), ToPixel(x1, y1), ToPixel(x0, y1) };
    }

    void PaintRegions(Graphics gfx)
    {
        if (ShowRegions is null || !ShowRegions())
            return;
        Rectangle v = ViewTiles(0);
        using Pen pen = new(Color.FromArgb(160, 120, 200, 255), 1);
        using SolidBrush b = new(Color.FromArgb(220, 160, 220, 255));
        foreach (UoRegions.Box r in UoRegions.InView(v.Left, v.Top, v.Right, v.Bottom))
        {
            Point[] pts = Diamond(r.X, r.Y, r.X + r.W, r.Y + r.H);
            gfx.DrawPolygon(pen, pts);
            if (r.W * Half > 70)
                gfx.DrawString(r.Label, Font, b, pts[0].X - 20, pts[0].Y + 4);
        }
    }

    // Each harvested chunk shaded from red (empty) to light green (ready) by its readiness, translucent over the map.
    void PaintChunks(Graphics gfx)
    {
        List<int[]>? chunks = Chunks?.Invoke() ?? new List<int[]>();
        Rectangle v = ViewTiles(8);
        foreach (int[] c in chunks)
        {
            if (c[0] + 8 < v.Left || c[0] > v.Right || c[1] + 8 < v.Top || c[1] > v.Bottom)
                continue;
            Color shade = ChunkShade(c[2]);
            using SolidBrush fill = new(Color.FromArgb(90, shade));
            using Pen edge = new(Color.FromArgb(170, shade), 1);
            Point[] pts = Diamond(c[0], c[1], c[0] + 8, c[1] + 8);
            gfx.FillPolygon(fill, pts);
            gfx.DrawPolygon(edge, pts);
        }
        List<int[]>? drops = Drops?.Invoke();
        if (drops is null)
            return;
        using SolidBrush pile = new(s_dropShade);
        using SolidBrush wildPile = new(s_wildShade);
        using Pen rim = new(Color.Black, 1);
        using SolidBrush text = new(Color.White);
        foreach (int[] d in drops)
        {
            if (d[0] < v.Left || d[0] > v.Right || d[1] < v.Top || d[1] > v.Bottom)
                continue;
            bool wild = d.Length > 3 && d[3] == 1;
            Point[] pts = Diamond(d[0], d[1], d[0] + 1, d[1] + 1);
            gfx.FillPolygon(wild ? wildPile : pile, pts);
            gfx.DrawPolygon(rim, pts);
            gfx.DrawString((wild ? "wild " : "") + d[2], Font, text, pts[0].X + 4, pts[0].Y - 4);
        }
    }

    void PaintResources(Graphics gfx)
    {
        List<int[]>? res = Resources?.Invoke();
        if (res is null)
            return;
        Rectangle v = ViewTiles(2);
        using Pen rim = new(Color.Black, 1);
        using SolidBrush text = new(Color.White);
        foreach (int[] r in res)
        {
            if (r[2] <= 0 || r[2] >= s_resourceColors.Length || r[0] < v.Left || r[0] > v.Right || r[1] < v.Top
                || r[1] > v.Bottom)
                continue;
            Point[] pts = Diamond(r[0], r[1], r[0] + 1, r[1] + 1);
            using SolidBrush fill = new(Color.FromArgb(230, s_resourceColors[r[2]]));
            gfx.FillPolygon(fill, pts);
            gfx.DrawPolygon(rim, pts);
            gfx.DrawString(r[3].ToString(), Font, text, pts[0].X + 4, pts[0].Y - 4);
        }
    }

    // A dropped pile: a log's brown, apart from the chunks' red to green.
    static readonly Color s_dropShade = Color.FromArgb(230, 139, 90, 43);
    public static Color DropShade => s_dropShade;
    // Wild logs (ours forgotten, or anyone's): a paler wood, labelled "wild".
    static readonly Color s_wildShade = Color.FromArgb(230, 196, 160, 110);
    public static Color WildShade => s_wildShade;

    // Red at 0, light green at 1000, a straight blend between.
    public static Color ChunkShade(int perMille)
    {
        double t = Math.Clamp(perMille, 0, 1000) / 1000.0;
        Color from = Color.FromArgb(220, 30, 30), to = Color.LightGreen;
        return Color.FromArgb((int)(from.R + (to.R - from.R) * t), (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }

    // Player houses at every zoom, the house pretest's tiles, and each building's sign at its centre, at the game's own
    // scale for this zoom (Half / 22) with a floor of SignMin px; a bank's is twice the size.
    void PaintPlaces(Graphics gfx)
    {
        Rectangle v = ViewTiles(2);
        // The footprint filled and outlined in the house colour, as the radar and the map's statics hold none of them.
        // A registered house of the player's, the one whose footprint holds its door (or is a tile from it), in gold
        // with its name (Damian, 2026-10-24: "it also is my house, so it deserves to be recognized as that on the map,
        // with a special icon or color code").
        List<(int X, int Y, string Name)> own = OwnHouses?.Invoke() ?? new();
        using (SolidBrush fill = new(Color.FromArgb(Zoom >= IsoZoom ? 40 : 110, Color.SandyBrown)))
        using (Pen edge = new(Color.SandyBrown, 2))
        using (SolidBrush ownFill = new(Color.FromArgb(Zoom >= IsoZoom ? 50 : 130, Color.Gold)))
        using (Pen ownEdge = new(Color.Gold, 3))
        using (Font ownFont = new("Segoe UI", 8f, FontStyle.Bold))
        {
            foreach (int[] hb in UoMap.HousesIn(v.Left, v.Top, v.Right, v.Bottom))
            {
                Point[] pts = Diamond(hb[0], hb[1], hb[2] + 1, hb[3] + 1);
                string? name = null;
                foreach ((int X, int Y, string Name) o in own)
                {
                    if (o.X >= hb[0] - 1 && o.X <= hb[2] + 1 && o.Y >= hb[1] - 1 && o.Y <= hb[3] + 1)
                        name = o.Name;
                }
                gfx.FillPolygon(name is null ? fill : ownFill, pts);
                gfx.DrawPolygon(name is null ? edge : ownEdge, pts);
                if (name is not null)
                {
                    Point top = pts[0];
                    foreach (Point p in pts)
                    {
                        if (p.Y < top.Y)
                            top = p;
                    }
                    string label = "⌂ " + (name.Length > 0 ? name : "your house");
                    SizeF size = gfx.MeasureString(label, ownFont);
                    gfx.FillRectangle(Brushes.Black, top.X - size.Width / 2 - 2, top.Y - size.Height - 2,
                        size.Width + 4, size.Height);
                    gfx.DrawString(label, ownFont, Brushes.Gold, top.X - size.Width / 2, top.Y - size.Height - 2);
                }
            }
        }
        List<int[]>? house = HouseTiles?.Invoke();
        if (house is not null)
        {
            using SolidBrush ok = new(Color.FromArgb(110, Color.Lime));
            using SolidBrush bad = new(Color.FromArgb(150, Color.Red));
            foreach (int[] t in house)
                gfx.FillPolygon(t[2] != 0 ? ok : bad, Diamond(t[0], t[1], t[0] + 1, t[1] + 1));
        }
        InterpolationMode was = gfx.InterpolationMode;
        gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
        foreach (UoRegions.Sign sg in UoRegions.SignsInView(v.Left, v.Top, v.Right, v.Bottom))
        {
            Bitmap? icon = UoArt.Flat(sg.Art);
            if (icon is null)
                continue;
            int side = Math.Max(SignMin, (int)(icon.Width * Half / 22)) * (sg.Bank ? 2 : 1);
            Point at = ToPixel(sg.X, sg.Y);
            gfx.DrawImage(icon, new Rectangle(at.X - side / 2, at.Y - side / 2, side, side));
        }
        gfx.InterpolationMode = was;
    }

    // A creature (not a human body) is its game picture, its longer side scaled by its level, standing on a ring in its
    // type's colour; people, vendors and places stay dots.
    void PaintMarks(Graphics gfx)
    {
        m_shown = Marks?.Invoke() ?? new Mark[0];
        foreach (Mark m in m_shown)
        {
            Point at = ToPixel(m.X, m.Y);
            int r = Math.Max(2, Math.Min(18, Math.Max(2, Math.Min(8, Zoom)) * m.Scale / 100));
            Bitmap? pic = m.Body > 0
                ? UoAnim.Sized(m.Body, Math.Max(8, Math.Min(80, Math.Max(18, Math.Min(40, Zoom * 5)) * m.Scale / 100)))
                : null;
            if (pic is not null)
            {
                int pw = pic.Width;
                int ph = pic.Height;
                using (Pen ring = new(m.Color, 2))
                    gfx.DrawEllipse(ring, at.X - pw / 3, at.Y - 2, pw * 2 / 3, 5);
                gfx.DrawImageUnscaled(pic, at.X - pw / 2, at.Y - ph + 2);
                if (m.Serial == Selected)
                {
                    using Pen pen = new(Color.White, 2);
                    gfx.DrawRectangle(pen, at.X - pw / 2 - 2, at.Y - ph, pw + 4, ph + 4);
                }
                continue;
            }
            using (SolidBrush b = new(m.Color))
                gfx.FillEllipse(b, at.X - r, at.Y - r, r * 2, r * 2);
            if (m.Serial == Selected)
            {
                using Pen pen = new(Color.White, 2);
                gfx.DrawEllipse(pen, at.X - r - 3, at.Y - r - 3, r * 2 + 6, r * 2 + 6);
            }
        }
    }

    void PaintStars(Graphics gfx)
    {
        int[][]? stars = Stars?.Invoke();
        m_starsShown = stars is not null && stars.Length > 0;
        if (stars is null || !m_starsShown)
            return;
        bool lit = (Environment.TickCount / StarFlashMs) % 2 == 0;
        int sr = Math.Max(5, Math.Min(12, Zoom * 2));
        using SolidBrush b = new(lit ? Color.Gold : Color.FromArgb(150, 110, 0));
        using Pen edge = new(Color.Black, 1);
        foreach (int[] t in stars)
        {
            Point[] pts = StarPoints(ToPixel(t[0], t[1]), sr);
            gfx.FillPolygon(b, pts);
            gfx.DrawPolygon(edge, pts);
        }
    }

    static readonly ImmutableArray<string> s_spotNames = ImmutableArray.Create("Sentinel", "Collection point",
        "Chop from");
    static readonly ImmutableArray<Color> s_spotColors = ImmutableArray.Create(Color.LimeGreen, Color.Orange,
        Color.DeepSkyBlue);

    void PaintSpots(Graphics gfx)
    {
        List<int[]>? spots = Spots?.Invoke();
        if (spots is null)
            return;
        int r = Math.Max(4, Math.Min(9, Zoom + 2));
        using Pen edge = new(Color.Black, 2);
        using SolidBrush bg = new(Color.FromArgb(160, 0, 0, 0));
        foreach (int[] s in spots)
        {
            if (s[2] < 0 || s[2] >= s_spotNames.Length)
                continue;
            Point at = ToPixel(s[0], s[1]);
            using SolidBrush b = new(s_spotColors[s[2]]);
            gfx.FillEllipse(b, at.X - r, at.Y - r, r * 2, r * 2);
            gfx.DrawEllipse(edge, at.X - r, at.Y - r, r * 2, r * 2);
            string label = s_spotNames[s[2]];
            SizeF size = gfx.MeasureString(label, Font);
            gfx.FillRectangle(bg, at.X + r + 2, at.Y - size.Height / 2, size.Width + 2, size.Height);
            gfx.DrawString(label, Font, b, at.X + r + 3, at.Y - size.Height / 2);
        }
    }

    // Custom pins (Damian, 2026-10-15: "add custom pins to the map, marking your own house, plus the houses of friends
    // and enemies alike"): a coloured pin with its label, at every zoom.
    void PaintPins(Graphics gfx)
    {
        if (Pins is null)
            return;
        foreach (Pin p in Pins())
        {
            Point at = ToPixel(p.X, p.Y);
            using SolidBrush b = new(PinColor(p.Kind));
            using Pen edge = new(Color.Black, 1);
            gfx.FillEllipse(b, at.X - 5, at.Y - 14, 10, 10);
            gfx.DrawEllipse(edge, at.X - 5, at.Y - 14, 10, 10);
            Point[] tip = { new(at.X - 3, at.Y - 6), new(at.X + 3, at.Y - 6), new(at.X, at.Y) };
            gfx.FillPolygon(b, tip);
            SizeF size = gfx.MeasureString(p.Label, Font);
            using (SolidBrush bg = new(Color.FromArgb(160, 0, 0, 0)))
                gfx.FillRectangle(bg, at.X + 7, at.Y - 16, size.Width + 2, size.Height);
            gfx.DrawString(p.Label, Font, b, at.X + 8, at.Y - 16);
        }
    }

    // The session's kills: a red cross, named from 8 px a tile.
    void PaintKills(Graphics gfx)
    {
        if (Kills is null)
            return;
        using Pen pen = new(Color.Red, 2);
        using SolidBrush b = new(Color.FromArgb(255, 200, 200));
        foreach (AssistFeatures.KillMark k in Kills())
        {
            Point at = ToPixel(k.X, k.Y);
            int h = Math.Max(2, 4 * TierScale(k.Name) / 100);
            gfx.DrawLine(pen, at.X - h, at.Y - h, at.X + h, at.Y + h);
            gfx.DrawLine(pen, at.X - h, at.Y + h, at.X + h, at.Y - h);
            if (Zoom >= 8)
            {
                string by = k.By != "you" ? " (" + k.By + ")" : "";
                string text = k.Name + by + (k.Loot.Length > 0 ? ": " + k.Loot : "");
                gfx.DrawString(text, Font, b, at.X + 6, at.Y - 6);
            }
        }
    }

    void PaintCorpses(Graphics gfx)
    {
        if (Corpses is null)
            return;
        foreach (AssistFeatures.Corpse c in Corpses())
        {
            Point at = ToPixel(c.X, c.Y);
            int r = Math.Max(6, Math.Min(16, Zoom * 2));
            using (Pen pen = new(Color.Magenta, 3))
            {
                gfx.DrawEllipse(pen, at.X - r, at.Y - r, r * 2, r * 2);
                gfx.DrawLine(pen, at.X - r / 2, at.Y - r / 2, at.X + r / 2, at.Y + r / 2);
                gfx.DrawLine(pen, at.X - r / 2, at.Y + r / 2, at.X + r / 2, at.Y - r / 2);
            }
            using SolidBrush b = new(Color.Magenta);
            gfx.DrawString("corpse " + c.At.ToString("HH:mm"), Font, b, at.X + r + 2, at.Y - 7);
        }
    }

    // The map search's route, tile by tile, and the place at its end; the course to a healer while dead, a dashed line
    // from the player to each, labelled with its distance; the player's cross and coordinates (Damian, 2026-10-15: "put
    // chararcter current coords on the map view").
    void PaintCourse(Graphics gfx)
    {
        Point me = ToPixel(CenterX, CenterY);
        int s = Math.Max(8, Math.Min(24, Zoom * 3));
        int[][]? route = Route?.Invoke();
        if (route is not null && route.Length > 1)
        {
            Point[] pts = Array.ConvertAll(route, t => ToPixel(t[0], t[1]));
            using (Pen pen = new(Color.Orange, 3))
                gfx.DrawLines(pen, pts);
            Point end = pts[pts.Length - 1];
            string? label = RouteLabel?.Invoke();
            using SolidBrush b = new(Color.Orange);
            gfx.FillEllipse(b, end.X - 6, end.Y - 6, 12, 12);
            if (!string.IsNullOrEmpty(label))
                gfx.DrawString(label, Font, b, end.X + 8, end.Y - 7);
        }
        if (Course is not null)
        {
            foreach (AssistFeatures.Waypoint w in Course())
            {
                Point to = ToPixel(w.X, w.Y);
                int d = Math.Max(Math.Abs(w.X - CenterX), Math.Abs(w.Y - CenterY));
                using (Pen pen = new(Color.LimeGreen, 3)
                {
                    DashStyle = DashStyle.Dash
                })
                    gfx.DrawLine(pen, me, to);
                using SolidBrush b = new(Color.LimeGreen);
                gfx.FillEllipse(b, to.X - 6, to.Y - 6, 12, 12);
                string text = w.Label + ": " + d + " tiles " + AssistFeatures.Heading(w.X - CenterX, w.Y - CenterY);
                gfx.DrawString(text, Font, b, to.X + 8, to.Y - 7);
            }
        }
        using (Pen pen = new(Color.Cyan, 3))
        {
            gfx.DrawLine(pen, me.X - s, me.Y, me.X + s, me.Y);
            gfx.DrawLine(pen, me.X, me.Y - s, me.X, me.Y + s);
        }
        string? where = Caption?.Invoke();
        if (!string.IsNullOrEmpty(where))
        {
            SizeF size = gfx.MeasureString(where, Font);
            using (SolidBrush bg = new(Color.FromArgb(170, 0, 0, 0)))
                gfx.FillRectangle(bg, 2, 2, size.Width + 8, size.Height + 4);
            gfx.DrawString(where, Font, Brushes.Cyan, 6, 4);
        }
    }
}
