// A creature's picture for the map (Damian, 2026-10-16: "make the dots into the graphics from the game, scaled to be
// useful"): the first frame of its first action, facing down-right, from the 1.25 client's ANIM.IDX and ANIM.MUL.
// ANIM.IDX holds 12-byte entries (offset, length, extra) per body, action and direction: 110 a body below 200 (22
// actions), 65 a body from 200 to 399 (13), 175 above (35), 5 directions an action. An entry is a palette of 256 5-5-5
// colours, a frame count and per frame an offset from the palette's end; a frame is centre x and y (s16), width and
// height (u16), then runs until 0x7FFF7FFF: a u32 holding x (10 bits from bit 22), y (10 bits from bit 12), both biased
// by 0x200, and a length (12 bits), followed by that many palette indexes (UltimaSDK Frame).
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public static class UoAnim
{
    public const int Direction = 2;
    public const int SizedKeep = 2000;

    static readonly Lock s_bodyLock = new();
    static readonly Lock s_sizedLock = new();
    static FileStream? s_mul;
    static byte[]? s_index;
    static string? s_folder;
    static Map<int, Cached<Bitmap>> s_bodies = Map<int, Cached<Bitmap>>.Empty;
    static Map<long, Bitmap> s_sized = Map<long, Bitmap>.Empty;

    static int Entry(int body)
    {
        int at = body < 200 ? body * 110 : body < 400 ? 22000 + (body - 200) * 65 : 35000 + (body - 400) * 175;
        return at + Direction;
    }

    public static Bitmap? Body(int body)
    {
        string? folder = UoArt.Folder;
        if (folder is null || body <= 0)
            return null;
        lock (s_bodyLock)
        {
            if (s_bodies.TryGet(body, out Cached<Bitmap>? hit))
                return hit.Value;
            Bitmap? b = null;
            try
            {
                if (s_folder != folder || s_mul is null || s_index is null)
                {
                    s_mul?.Dispose();
                    s_mul = File.OpenRead(Path.Combine(folder, "ANIM.MUL"));
                    s_index = File.ReadAllBytes(Path.Combine(folder, "ANIM.IDX"));
                    s_folder = folder;
                }
                FileStream mul = s_mul;
                byte[] index = s_index;
                int e = Entry(body);
                if ((e + 1) * 12 <= index.Length)
                {
                    int off = BitConverter.ToInt32(index, e * 12);
                    int len = BitConverter.ToInt32(index, e * 12 + 4);
                    if (off >= 0 && len > 520 && len < 1 << 22 && off + (long)len <= mul.Length)
                    {
                        byte[] d = new byte[len];
                        mul.Position = off;
                        int got = 0;
                        while (got < len)
                        {
                            int n = mul.Read(d, got, len - got);
                            if (n <= 0)
                                break;
                            got += n;
                        }
                        b = Frame(d);
                    }
                }
            }
            catch
            {
                b = null;
            }
            s_bodies = s_bodies.Set(body, new Cached<Bitmap>(b));
            return b;
        }
    }

    // The picture scaled to the area of a `side`-pixel square (a long flat rat keeps its length), cached per body and
    // size so a repaint is a plain copy.
    public static Bitmap? Sized(int body, int side)
    {
        Bitmap? src = Body(body);
        if (src is null)
            return null;
        lock (s_sizedLock)
        {
            long key = (long)body << 16 | (uint)side;
            if (s_sized.TryGet(key, out Bitmap? got))
                return got;
            if (s_sized.Count > SizedKeep)
            {
                foreach (KeyValuePair<long, Bitmap> e in s_sized)
                    e.Value.Dispose();
                s_sized = Map<long, Bitmap>.Empty;
            }
            float k = (float)(side / Math.Sqrt((double)src.Width * src.Height));
            Bitmap b = new(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)),
                PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, b.Width, b.Height);
            }
            s_sized = s_sized.Set(key, b);
            return b;
        }
    }

    static Bitmap? Frame(byte[] d)
    {
        int[] pal = new int[256];
        for (int i = 0; i < 256; i++)
            pal[i] = UoRadar.ToArgb(BitConverter.ToUInt16(d, i * 2));
        int count = BitConverter.ToInt32(d, 512);
        if (count <= 0)
            return null;
        int at = 512 + BitConverter.ToInt32(d, 516);
        if (at + 8 > d.Length)
            return null;
        int cx = BitConverter.ToInt16(d, at);
        int cy = BitConverter.ToInt16(d, at + 2);
        int w = BitConverter.ToUInt16(d, at + 4);
        int h = BitConverter.ToUInt16(d, at + 6);
        if (w <= 0 || h <= 0 || w > 512 || h > 512)
            return null;
        int[] px = new int[w * h];
        int xBase = cx - 0x200;
        int yBase = cy + h - 0x200;
        at += 8;
        while (at + 4 <= d.Length)
        {
            uint head = BitConverter.ToUInt32(d, at);
            at += 4;
            if (head == 0x7FFF7FFF)
                break;
            head ^= (0x200u << 22) | (0x200u << 12);
            int x = xBase + (int)((head >> 22) & 0x3FF);
            int y = yBase + (int)((head >> 12) & 0x3FF);
            int run = (int)(head & 0xFFF);
            for (int i = 0; i < run && at < d.Length; i++, at++)
            {
                int xx = x + i;
                if (y >= 0 && y < h && xx >= 0 && xx < w)
                    px[y * w + xx] = pal[d[at]];
            }
        }
        return UoArt.FromPixels(px, w, h);
    }
}
