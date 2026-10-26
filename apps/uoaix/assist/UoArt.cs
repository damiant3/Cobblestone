// Item art from the 1.25 client's ARTIDX.MUL and ART.MUL: an item's entry is 0x4000 + its graphic, 12 bytes (offset,
// length, extra). A static image is a 4-byte header, width and height (u16), one u16 row offset per row counted in u16
// units from the end of that table, then per row (x skip, run length, run of 16-bit 555 colours) pairs ending in 0, 0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Uoaix.Assist;

public static class UoArt
{
    // An item's art cut to its drawn pixels for an icon (Damian, 2026-10-17: "make all the resource icons more zoomed
    // in, each is about half the right size"): a black pearl is a 5 x 5 dot in a 44 x 20 tile. The cut is padded to at
    // least IconMin square, so a zoomed icon is at most about twice its art.
    public const int IconMin = 14;
    const int ScaledKeep = 6000;

    static string? s_folder;
    static byte[]? s_index;
    static Map<int, Cached<Bitmap>> s_items = Map<int, Cached<Bitmap>>.Empty;
    static Map<int, Bitmap> s_icons = Map<int, Bitmap>.Empty;
    static Map<int, Bitmap> s_flats = Map<int, Bitmap>.Empty;
    static Map<int, Cached<Bitmap>> s_lands = Map<int, Cached<Bitmap>>.Empty;
    static Map<long, Cached<Bitmap>> s_scaled = Map<long, Cached<Bitmap>>.Empty;

    public static string? Folder
    {
        get => s_folder;
        set => s_folder = value;
    }

    public static Bitmap? Icon(int graphic)
    {
        if (s_icons.TryGet(graphic, out Bitmap? cut))
            return cut;
        Bitmap? b = Item(graphic);
        if (b is null)
            return null;
        int x0 = b.Width;
        int y0 = b.Height;
        int x1 = -1;
        int y1 = -1;
        for (int y = 0; y < b.Height; y++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                if (b.GetPixel(x, y).A == 0)
                    continue;
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }
        }
        if (x1 < 0)
        {
            s_icons = s_icons.Set(graphic, b);
            return b;
        }
        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;
        int side = Math.Max(IconMin, Math.Max(w, h));
        Bitmap c = new(side, side, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(c))
        {
            g.DrawImage(b, new Rectangle((side - w) / 2, (side - h) / 2, w, h), new Rectangle(x0, y0, w, h),
                GraphicsUnit.Pixel);
        }
        s_icons = s_icons.Set(graphic, c);
        return c;
    }

    // A wall sign's art seen face on (Damian, 2026-10-19: "there is an icon version, not isomorphic"): the art hangs on
    // a wall drawn at the projection's 1-in-2 slope, so its columns are sheared by half a pixel each; of the two
    // directions, the one that leaves the cut shortest undoes it. Padded square as Icon is.
    public static Bitmap? Flat(int graphic)
    {
        if (s_flats.TryGet(graphic, out Bitmap? flat))
            return flat;
        Bitmap? b = Item(graphic);
        if (b is null)
            return null;
        List<Point> opaque = new();
        for (int y = 0; y < b.Height; y++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                if (b.GetPixel(x, y).A > 0)
                    opaque.Add(new Point(x, y));
            }
        }
        if (opaque.Count == 0)
        {
            Bitmap? icon = Icon(graphic);
            if (icon is not null)
                s_flats = s_flats.Set(graphic, icon);
            return icon;
        }
        float best = 0;
        int bestLo = 0;
        int bestH = int.MaxValue;
        int x0 = int.MaxValue;
        int x1 = -1;
        foreach (Point p in opaque)
        {
            x0 = Math.Min(x0, p.X);
            x1 = Math.Max(x1, p.X);
        }
        foreach (float k in new[] { 0.5f, -0.5f })
        {
            int lo = int.MaxValue;
            int hi = int.MinValue;
            foreach (Point p in opaque)
            {
                int v = (int)Math.Floor(p.Y + k * p.X);
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }
            if (hi - lo + 1 < bestH)
            {
                bestH = hi - lo + 1;
                bestLo = lo;
                best = k;
            }
        }
        int w = x1 - x0 + 1;
        int side = Math.Max(IconMin, Math.Max(w, bestH));
        Bitmap c = new(side, side, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(c))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Transform = new Matrix(1, best, 0, 1, (side - w) / 2 - x0, (side - bestH) / 2 - bestLo);
            g.DrawImageUnscaled(b, 0, 0);
        }
        s_flats = s_flats.Set(graphic, c);
        return c;
    }

    public static Bitmap? Item(int graphic)
    {
        string? folder = s_folder;
        if (folder is null)
            return null;
        if (s_items.TryGet(graphic, out Cached<Bitmap>? hit))
            return hit.Value;
        Bitmap? b = null;
        try
        {
            byte[] index = s_index ??= File.ReadAllBytes(Path.Combine(folder, "ARTIDX.MUL"));
            int i = 0x4000 + graphic;
            if ((i + 1) * 12 <= index.Length)
            {
                int off = BitConverter.ToInt32(index, i * 12);
                int len = BitConverter.ToInt32(index, i * 12 + 4);
                if (off >= 0 && len > 8)
                    b = Decode(ReadFile(folder, "ART.MUL", off, len));
            }
        }
        catch
        {
            b = null;
        }
        s_items = s_items.Set(graphic, new Cached<Bitmap>(b));
        return b;
    }

    // A land tile's art: ART.MUL entries below 0x4000 are raw 44 x 44 diamonds, rows 2, 4 ... 44 ... 4, 2 pixels wide,
    // centred, 5-5-5 u16 each.
    public static Bitmap? Land(int id)
    {
        string? folder = s_folder;
        if (folder is null || id < 0 || id >= 0x4000)
            return null;
        if (s_lands.TryGet(id, out Cached<Bitmap>? hit))
            return hit.Value;
        Bitmap? b = null;
        try
        {
            byte[] index = s_index ??= File.ReadAllBytes(Path.Combine(folder, "ARTIDX.MUL"));
            int off = BitConverter.ToInt32(index, id * 12);
            int len = BitConverter.ToInt32(index, id * 12 + 4);
            if (off >= 0 && len >= 2 * 44 * 22)
            {
                byte[] d = ReadFile(folder, "ART.MUL", off, len);
                int[] px = new int[44 * 44];
                int p = 0;
                for (int y = 0; y < 44; y++)
                {
                    int w = y < 22 ? 2 + y * 2 : 44 - (y - 22) * 2;
                    int x0 = 22 - w / 2;
                    for (int x = 0; x < w && p + 2 <= d.Length; x++, p += 2)
                        px[y * 44 + x0 + x] = UoRadar.ToArgb(BitConverter.ToUInt16(d, p));
                }
                b = FromPixels(px, 44, 44);
            }
        }
        catch
        {
            b = null;
        }
        s_lands = s_lands.Set(id, new Cached<Bitmap>(b));
        return b;
    }

    // Art scaled by percent, cached (land ids as is, statics + 0x10000), so drawing is a copy.
    public static Bitmap? Scaled(int key, int pct)
    {
        long k = (long)key << 16 | (uint)pct;
        if (s_scaled.TryGet(k, out Cached<Bitmap>? hit))
            return hit.Value;
        if (s_scaled.Count > ScaledKeep)
        {
            foreach (KeyValuePair<long, Cached<Bitmap>> e in s_scaled)
                e.Value.Value?.Dispose();
            s_scaled = Map<long, Cached<Bitmap>>.Empty;
        }
        Bitmap? src = key >= 0x10000 ? Item(key - 0x10000) : Land(key);
        Bitmap? s = null;
        if (src is not null)
        {
            // A land diamond is scaled 2 px larger, so neighbours overlap their soft (partly transparent) edges.
            int grow = key < 0x10000 ? 2 : 0;
            int w = Math.Max(1, src.Width * pct / 100) + grow;
            int h = Math.Max(1, src.Height * pct / 100) + grow;
            s = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using Graphics g = Graphics.FromImage(s);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }
        s_scaled = s_scaled.Set(k, new Cached<Bitmap>(s));
        return s;
    }

    // Length bytes of a client file from offset; a short file leaves the rest zero.
    public static byte[] ReadFile(string folder, string file, long offset, int length)
    {
        byte[] data = new byte[length];
        using FileStream fs = File.OpenRead(Path.Combine(folder, file));
        fs.Position = offset;
        int got = 0;
        while (got < length)
        {
            int n = fs.Read(data, got, length - got);
            if (n <= 0)
                break;
            got += n;
        }
        return data;
    }

    public static Bitmap FromPixels(int[] px, int w, int h)
    {
        Bitmap b = new(w, h, PixelFormat.Format32bppArgb);
        BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, bd.Scan0, px.Length);
        b.UnlockBits(bd);
        return b;
    }

    static Bitmap? Decode(byte[] d)
    {
        int w = BitConverter.ToUInt16(d, 4);
        int h = BitConverter.ToUInt16(d, 6);
        if (w <= 0 || h <= 0 || w > 512 || h > 512 || 8 + h * 2 > d.Length)
            return null;
        int[] px = new int[w * h];
        int start = 8 + h * 2;
        for (int y = 0; y < h; y++)
        {
            int p = start + BitConverter.ToUInt16(d, 8 + y * 2) * 2;
            int x = 0;
            while (p + 4 <= d.Length)
            {
                int skip = BitConverter.ToUInt16(d, p);
                int run = BitConverter.ToUInt16(d, p + 2);
                p += 4;
                if (skip + run == 0)
                    break;
                x += skip;
                for (int k = 0; k < run && p + 2 <= d.Length; k++, x++, p += 2)
                {
                    if (x >= 0 && x < w)
                        px[y * w + x] = UoRadar.ToArgb(BitConverter.ToUInt16(d, p));
                }
            }
        }
        return FromPixels(px, w, h);
    }
}
