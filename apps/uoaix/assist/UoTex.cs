#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

// Land textures (TEXIDX.MUL per texture: offset, length, extra; TEXMAPS.MUL 5-5-5 u16 pixels, 64x64 at 0x2000 bytes,
// 128x128 at 0x8000), each land id's texture from TILEDATA's land entries (flags u32, texture u16, name 20), scaled by
// area average to the map's tile size and cached per (texture, size).
public static class UoTex
{
    static ushort[]? s_landTexture; static bool[]? s_landWater; static byte[]? s_index; static string? s_folder;
    // Land named "water" in TILEDATA (the ocean's land tiles carry no texture).
    public static bool IsWater(int landId)
    {
        lock (s_scaledGate)
            return Open() && landId >= 0 && landId < s_landWater!.Length && s_landWater[landId];
    }
    static Map<long, int[]> s_scaled = Map<long, int[]>.Empty; static readonly Lock s_scaledGate = new();
    const int ScaledKeep = 4000;
    static bool Open()
    {
        if (UoArt.Folder == null)
            return false;
        if (s_folder == UoArt.Folder && s_index != null)
            return true;
        try
        {
            byte[] td = File.ReadAllBytes(Path.Combine(UoArt.Folder, "TILEDATA.MUL"));
            s_landTexture = new ushort[0x4000];
            s_landWater = new bool[0x4000];
            for (int id = 0; id < 0x4000; id++)
            {
                int at = (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26 + 4;
                if (at + 22 > td.Length)
                    continue;
                s_landTexture[id] = BitConverter.ToUInt16(td, at);
                int n = 0;
                while (n < 20 && td[at + 2 + n] != 0)
                    n++;
                s_landWater[id] = Encoding.ASCII.GetString(td, at + 2, n).Trim().ToLowerInvariant() == "water";
            }
            s_index = File.ReadAllBytes(Path.Combine(UoArt.Folder, "TEXIDX.MUL"));
            s_folder = UoArt.Folder;
            s_scaled = s_scaled.Clear();
            return true;
        }
        catch { s_index = null; return false; }
    }
    // The land id's texture as size x size ARGB pixels, or null when it has none.
    public static int[]? Land(int landId, int size)
    {
        lock (s_scaledGate)
        {
            if (!Open() || landId < 0 || landId >= s_landTexture!.Length)
                return null;
            int tex = s_landTexture[landId];
            long key = (long)tex << 16 | (uint)size;
            int[]? got;
            if (s_scaled.TryGet(key, out got))
                return got;
            if (s_scaled.Count > ScaledKeep)
                s_scaled = s_scaled.Clear();
            int[]? px = null;
            if (tex > 0 && (tex + 1) * 12 <= s_index!.Length)
            {
                int off = BitConverter.ToInt32(s_index, tex * 12), len = BitConverter.ToInt32(s_index, tex * 12 + 4);
                int s = len == 0x8000 ? 128 : len == 0x2000 ? 64 : 0;
                if (off >= 0 && s > 0)
                {
                    byte[] d = new byte[len];
                    using (FileStream fs = File.OpenRead(Path.Combine(UoArt.Folder!, "TEXMAPS.MUL")))
                    {
                        fs.Position = off;
                        int n = 0;
                        while (n < len)
                        {
                            int r = fs.Read(d, n, len - n);
                            if (r <= 0)
                                break;
                            n += r;
                        }
                    }
                    px = new int[size * size];
                    for (int j = 0; j < size; j++)
                    {
                        for (int i = 0; i < size; i++)
                        {
                            int u0 = i * s / size, u1 = Math.Max(u0 + 1, (i + 1) * s / size), v0 = j * s
                                / size, v1 = Math.Max(v0 + 1, (j + 1) * s / size), r = 0, g = 0, b = 0, n = 0;
                            for (int v = v0; v < v1; v++)
                                for (int u = u0; u < u1; u++)
                                {
                                    int c = BitConverter.ToUInt16(d, (v * s + u) * 2);
                                    r += (c >> 10) & 31;
                                    g += (c >> 5) & 31;
                                    b += c & 31;
                                    n++;
                                }
                            px[j * size + i] = unchecked((int)0xFF000000) | (r * 255 / 31 / n) << 16
                                | (g * 255 / 31 / n) << 8 | (b * 255 / 31 / n);
                        }
                    }
                }
            }
            s_scaled = s_scaled.Set(key, px!);
            return px;
        }
    }
}
