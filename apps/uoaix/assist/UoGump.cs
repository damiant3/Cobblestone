// The client's gump art (Damian, 2026-10-19: "add gump decoder to assist for sure"), from the 1.25 GUMPIDX.MUL and
// GUMPART.MUL: GUMPIDX holds 12-byte entries (offset, length, extra) per gump id, the extra width << 16 | height. A
// gump is a table of one u32 per row, that row's start in 4-byte units from the gump's start, then each row's runs of a
// 5-5-5 colour (u16, 0 transparent) and a pixel count (u16), until the next row's start (UltimaSDK Gumps).
#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public static class UoGump
{
    static readonly Lock s_lock = new();
    static byte[]? s_index;
    static Map<int, Cached<Bitmap>> s_cache = Map<int, Cached<Bitmap>>.Empty;

    public static int Count => Index() is byte[] index ? index.Length / 12 : 0;

    static byte[]? Index()
    {
        if (s_index is not null)
            return s_index;
        string? folder = UoArt.Folder;
        if (folder is null)
            return null;
        try
        {
            s_index = File.ReadAllBytes(Path.Combine(folder, "GUMPIDX.MUL"));
        }
        catch
        {
            s_index = null;
        }
        return s_index;
    }

    // A gump's width and height, or null when the client has none at id.
    public static Size? SizeOf(int id)
    {
        lock (s_lock)
        {
            byte[]? index = Index();
            if (index is null || id < 0 || (id + 1) * 12 > index.Length)
                return null;
            int off = BitConverter.ToInt32(index, id * 12);
            int len = BitConverter.ToInt32(index, id * 12 + 4);
            int ex = BitConverter.ToInt32(index, id * 12 + 8);
            if (off < 0 || len <= 0)
                return null;
            return new Size((ex >> 16) & 0xFFFF, ex & 0xFFFF);
        }
    }

    public static Bitmap? Get(int id)
    {
        lock (s_lock)
        {
            if (s_cache.TryGet(id, out Cached<Bitmap>? hit))
                return hit.Value;
            Bitmap? b = null;
            Size? size = SizeOf(id);
            string? folder = UoArt.Folder;
            byte[]? index = s_index;
            if (size is Size sz && sz.Width > 0 && sz.Height > 0 && folder is not null && index is not null)
            {
                try
                {
                    int off = BitConverter.ToInt32(index, id * 12);
                    int len = BitConverter.ToInt32(index, id * 12 + 4);
                    b = Decode(UoArt.ReadFile(folder, "GUMPART.MUL", off, len), len, sz.Width, sz.Height);
                }
                catch
                {
                    b = null;
                }
            }
            s_cache = s_cache.Set(id, new Cached<Bitmap>(b));
            return b;
        }
    }

    static Bitmap Decode(byte[] d, int len, int w, int h)
    {
        int[] px = new int[w * h];
        for (int y = 0; y < h && (y + 1) * 4 <= len; y++)
        {
            int p = BitConverter.ToInt32(d, y * 4) * 4;
            int end = y + 1 < h && (y + 2) * 4 <= len ? BitConverter.ToInt32(d, (y + 1) * 4) * 4 : len;
            for (int x = 0; p + 4 <= end && p + 4 <= len && x < w; p += 4)
            {
                ushort c = BitConverter.ToUInt16(d, p);
                int n = BitConverter.ToUInt16(d, p + 2);
                if (c != 0)
                {
                    int argb = UoRadar.ToArgb(c);
                    for (int i = 0; i < n && x + i < w; i++)
                        px[y * w + x + i] = argb;
                }
                x += n;
            }
        }
        return UoArt.FromPixels(px, w, h);
    }
}
