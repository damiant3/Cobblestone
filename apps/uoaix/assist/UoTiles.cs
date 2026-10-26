// Item names, flags and weights from the 1.25 client's TILEDATA.MUL (the pre-High Seas layout): 512 land blocks of a
// 4-byte header and 32 entries of 26 bytes, then item blocks of a 4-byte header and 32 entries of 37 bytes (flags u32,
// weight, quality, u16, u8, quantity, animation u16, u8, hue, u16, height, name of 20 ASCII bytes).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Uoaix.Assist;

public static class UoTiles
{
    // Misc: the record's int16 at offset 6, a weapon's entry in the server's weapon table (OUO load.c:303,
    // item.c:5906). Layer: the byte at offset 5, the layer a wearable item goes on (1 one hand, 2 both hands).
    public sealed record Tile(int Graphic, int Weight, int Height, int Misc, uint Flags, string Name, int Layer = 0);

    // Never lifted: weight 255, or a background, wall, impassable, surface, bridge, window, internal, foliage, roof or
    // door tile.
    const uint Fixed = 0x1 | 0x10 | 0x40 | 0x200 | 0x400 | 0x1000 | 0x10000 | 0x20000 | 0x10000000 | 0x20000000;
    // The tile data's Generic flag: the art stacks.
    const uint Generic = 0x800;
    const int LandBytes = 512 * (4 + 32 * 26);
    const int ItemBlock = 4 + 32 * 37;

    static List<Tile>? s_items;

    public static List<Tile> Items()
    {
        if (s_items is not null)
            return s_items;
        List<Tile> list = new();
        string? folder = UoArt.Folder;
        if (folder is not null)
        {
            try
            {
                byte[] d = File.ReadAllBytes(Path.Combine(folder, "TILEDATA.MUL"));
                for (int g = 0; ; g++)
                {
                    int at = LandBytes + (g / 32) * ItemBlock + 4 + (g % 32) * 37;
                    if (at + 37 > d.Length)
                        break;
                    list.Add(new Tile(g, d[at + 4], d[at + 16], BitConverter.ToInt16(d, at + 6),
                        BitConverter.ToUInt32(d, at), Singular(d, at + 17), d[at + 5]));
                }
            }
            catch
            {
            }
        }
        if (list.Count > 0)
            s_items = list;
        return list;
    }

    // The tile data forgotten, for a folder changed under it (the suites' TestMap).
    public static void Reset()
    {
        s_items = null;
    }

    // A name carries its plural as %s or %es/% markers ("iron ingot%s", "Black Pearl%s%"): the singular drops them.
    static string Singular(byte[] d, int at)
    {
        int n = 0;
        while (n < 20 && d[at + n] != 0)
            n++;
        StringBuilder sb = new();
        bool marked = false;
        foreach (char ch in Encoding.ASCII.GetString(d, at, n))
        {
            if (ch == '%')
                marked = !marked;
            else if (!marked)
                sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    public static string Name(int graphic)
    {
        List<Tile> l = Items();
        bool named = graphic >= 0 && graphic < l.Count && l[graphic].Name.Length > 0;
        return named ? l[graphic].Name : graphic.ToString("X4");
    }

    public static bool Liftable(Tile t)
    {
        return t.Weight < 255 && (t.Flags & Fixed) == 0 && t.Name.Length > 0;
    }

    // An art the tile data does not hold is taken to stack.
    public static bool Stacks(int graphic)
    {
        List<Tile> l = Items();
        return graphic < 0 || graphic >= l.Count || (l[graphic].Flags & Generic) != 0;
    }
}
