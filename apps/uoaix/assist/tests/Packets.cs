// The 1.25.32 packets the suites send, each at its full wire length (ProtocolFacts.md; AssistState reads them at these
// offsets).
#nullable enable
using System.Text;

namespace Uoaix.Assist.Tests;

public static class Packets
{
    public const uint Player = 0x02BE4F79;

    static void Put32(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    static void Put16(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 8);
        b[at + 1] = (byte)v;
    }

    // 0x1B: the player's serial, body, place, height and facing, 37 bytes.
    public static byte[] LoginConfirm(int x, int y, int z)
    {
        byte[] p = new byte[37];
        p[0] = 0x1B;
        Put32(p, 1, Player);
        Put16(p, 9, 0x190);
        Put16(p, 11, x);
        Put16(p, 13, y);
        p[16] = (byte)(sbyte)z;
        p[17] = 2;
        return p;
    }

    // 0x20: the player drawn: serial, body, hue, flags (0x80 hidden), place, facing, height, 19 bytes.
    public static byte[] Draw(int x, int y, int z, byte flags)
    {
        byte[] p = new byte[19];
        p[0] = 0x20;
        Put32(p, 1, Player);
        Put16(p, 5, 0x190);
        Put16(p, 8, 0x83EA);
        p[10] = flags;
        Put16(p, 11, x);
        Put16(p, 13, y);
        p[17] = 2;
        p[18] = (byte)(sbyte)z;
        return p;
    }

    public static byte[] Step(int dir, int seq) => new byte[] { 0x02, (byte)dir, (byte)seq };

    public static byte[] StepAck(int seq) => new byte[] { 0x22, (byte)seq, 0x01 };

    public static byte[] StepRefused(int seq, int x, int y,
        int facing = 2) => new byte[] { 0x21, (byte)seq, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y,
        (byte)facing, 0 };

    // 0x1A: an item on the ground: length, serial, graphic, place and height, 14 bytes.
    public static byte[] Ground(uint serial, int graphic, int x, int y, int z)
    {
        byte[] p = new byte[14];
        p[0] = 0x1A;
        Put16(p, 1, p.Length);
        Put32(p, 3, serial & 0x7FFFFFFF);
        Put16(p, 7, graphic);
        Put16(p, 9, x);
        Put16(p, 11, y);
        p[13] = (byte)(sbyte)z;
        return p;
    }

    // 0x1D: an object deleted: its serial, 5 bytes.
    public static byte[] Delete(uint serial)
    {
        byte[] p = new byte[5];
        p[0] = 0x1D;
        Put32(p, 1, serial);
        return p;
    }

    // 0x77: a mobile moved: serial, body, place, height, facing, hue, flags, notoriety, 17 bytes.
    public static byte[] Mobile(uint serial, int x, int y)
    {
        byte[] p = new byte[17];
        p[0] = 0x77;
        Put32(p, 1, serial);
        Put16(p, 5, 0x190);
        Put16(p, 7, x);
        Put16(p, 9, y);
        p[16] = 1;
        return p;
    }

    // 0x6C from the server: a target cursor, object type, its id and flags, 19 bytes.
    public static byte[] Cursor(uint id, byte flags)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        Put32(p, 2, id);
        p[6] = flags;
        return p;
    }

    // 0x1C: a line from serial (0xFFFFFFFF the system), name and text in hue, ending in a NUL.
    public static byte[] Speech(uint serial, string name, string text, byte type, int hue = 0x3B2)
    {
        byte[] t = Encoding.ASCII.GetBytes(text);
        byte[] p = new byte[45 + t.Length];
        p[0] = 0x1C;
        Put16(p, 1, p.Length);
        Put32(p, 3, serial);
        Put16(p, 7, serial == 0xFFFFFFFF ? 0xFFFF : 0x190);
        p[9] = type;
        Put16(p, 10, hue);
        Put16(p, 12, 3);
        Encoding.ASCII.GetBytes(name).CopyTo(p, 14);
        t.CopyTo(p, 44);
        return p;
    }

    public static byte[] Use(uint serial)
    {
        byte[] p = new byte[5];
        p[0] = 0x06;
        Put32(p, 1, serial);
        return p;
    }

    public static byte[] Lift(uint serial, int amount)
    {
        byte[] p = new byte[7];
        p[0] = 0x07;
        Put32(p, 1, serial);
        Put16(p, 5, amount);
        return p;
    }

    // 0x2E: an item worn: serial, graphic, layer (0x15 the backpack), the mobile, hue, 15 bytes.
    public static byte[] Wear(uint serial, int graphic, int layer, uint mobile)
    {
        byte[] p = new byte[15];
        p[0] = 0x2E;
        Put32(p, 1, serial);
        Put16(p, 5, graphic);
        p[8] = (byte)layer;
        Put32(p, 9, mobile);
        return p;
    }

    // 0x25: an item put in a container: serial, graphic, amount, x, y, the container, hue, 20 bytes.
    public static byte[] Contained(uint serial, int graphic, int amount, uint container)
    {
        byte[] p = new byte[20];
        p[0] = 0x25;
        Put32(p, 1, serial);
        Put16(p, 5, graphic);
        Put16(p, 8, amount);
        Put16(p, 10, 44);
        Put16(p, 12, 65);
        Put32(p, 14, container);
        return p;
    }

    // 0x24: a container opened: its serial and gump, 7 bytes (0x3C for a backpack-like bag).
    public static byte[] Open(uint container)
    {
        byte[] p = new byte[7];
        p[0] = 0x24;
        Put32(p, 1, container);
        Put16(p, 5, 0x3C);
        return p;
    }

    // 0x3C: a container's contents: length, count, then serial, graphic, a byte, amount, x, y, the container, hue, 19
    // bytes each.
    public static byte[] Contents(uint container, params uint[] items)
    {
        byte[] p = new byte[5 + 19 * items.Length];
        p[0] = 0x3C;
        Put16(p, 1, p.Length);
        Put16(p, 3, items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            int at = 5 + 19 * i;
            Put32(p, at, items[i]);
            Put16(p, at + 4, 0x0F39);
            Put16(p, at + 7, 1);
            Put16(p, at + 9, 44);
            Put16(p, at + 11, 65);
            Put32(p, at + 13, container);
        }
        return p;
    }

    // 0x11: the player's full status (type 1): name, hits, then str to weight with mana at 54, 66 bytes.
    public static byte[] Status(int mana)
    {
        byte[] p = new byte[66];
        p[0] = 0x11;
        Put16(p, 1, p.Length);
        Put32(p, 3, Player);
        Encoding.ASCII.GetBytes("Kwik").CopyTo(p, 7);
        Put16(p, 37, 85);
        Put16(p, 39, 85);
        p[42] = 1;
        Put16(p, 44, 85);
        Put16(p, 46, 88);
        Put16(p, 48, 51);
        Put16(p, 50, 88);
        Put16(p, 52, 88);
        Put16(p, 54, mana);
        Put16(p, 56, 51);
        Put16(p, 64, 110);
        return p;
    }

    // 0x7C: an item menu with no title and no entries, 10 bytes.
    public static byte[] Menu(uint serial)
    {
        byte[] p = new byte[10];
        p[0] = 0x7C;
        Put16(p, 1, p.Length);
        Put32(p, 3, serial);
        Put16(p, 7, 1);
        return p;
    }

    // A client packet of the table's length for op, its bytes after the opcode counting up from 1.
    public static byte[] Filled(int op, int length)
    {
        byte[] p = new byte[length];
        p[0] = (byte)op;
        for (int i = 1; i < length; i++)
            p[i] = (byte)i;
        return p;
    }
}
