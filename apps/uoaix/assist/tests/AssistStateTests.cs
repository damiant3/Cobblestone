#nullable enable
using System;
using System.Collections.Immutable;
using System.Text;

namespace Uoaix.Assist.Tests;

// The game state as the server's packets build it (AssistState.Server, AssistTesting.md T2), each packet at the lengths
// its reader tests, one byte short of them, and for another serial than the player's.
public static class AssistStateTests
{
    const uint Other = 0x0000ABCD;
    const uint Item = 0x40001000;
    const uint Box = 0x40002000;
    static readonly DateTime s_start = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("state: hits, mana and stamina updates are the player's at 9 bytes; 8 bytes or another serial "
            + "change nothing", Vitals),
        new TestCase("state: the paperdoll title is the player's at 65 bytes; 64 or another serial change nothing",
        Title),
        new TestCase("state: a full status sets the stats once, the starting stats only the first time; a short one "
        + "only the hits", Status),
        new TestCase("state: one skill numbers from 0, the full list from 1, in 4- or 6-byte entries; any other list "
        + "is a fault", Skills),
        new TestCase("state: a deleted serial leaves the items, the ground, the mobiles and the pack, a ground item "
        + "kept as GroundGone; death is kept", DeleteAndDeath),
        new TestCase("state: speech names its speaker, keeps labels and the invulnerable, the guard lines, and the "
        + "player chat", Speech),
        new TestCase("state: a container's contents replace what it held; a cut-off entry ends the list", Contents),
        new TestCase("inventory: the pack's item count is every bag's contents, a spellbook or key ring one item with "
        + "nothing inside counted", ItemCount),
        new TestCase("state: an incoming mobile is placed with its notoriety; the player's own carries its body, "
        + "notoriety and gear", Incoming),
        new TestCase("state: a ground item's amount, art step, facing byte and hue flags are read in turn", GroundItem),
        new TestCase("state: a step answered moves or turns the player; an unknown answer changes nothing; a refusal "
        + "resets the steps", Steps),
        new TestCase("state: a redraw far off just after a step with no cursor is a teleporter learned at the step's "
        + "tile", Jump),
        new TestCase("state: the summary names the player, its pack and its fault", Summary),
        new TestCase("state: a jump with every step answered is learned where the player stood; one 1500 ms after the "
        + "step is none", JumpEdges),
        new TestCase("state: mobiles past GroundReach are dropped, and contents whose holder is gone are swept once "
        + "the player moves on", PruneAndSweep),
        new TestCase("state: past MobileKeep labels the labelled and the invulnerable start over", LabelCap),
        new TestCase("state: the summary counts skills learned and moved, and names an open cursor", SummaryDetail),
        new TestCase("state: at ItemKeep ground items a new one is not kept, a known one still moves", GroundCap),
        new TestCase("state: at ItemKeep contained items a new one sweeps the left-behind first, and is kept only if "
        + "room was made", ItemCap),
        new TestCase("state: short mobile packets set only what their length holds; another's redraw moves nobody",
        ShortPackets),
        new TestCase("state: a name's unprintable bytes read as '?'; a ghost body, either one, is dead, a human one "
        + "alive", NamesAndGhosts),
        new TestCase("state: past MobileKeep names start over; a status with no name names nobody", NameCap),
        new TestCase("state: a second unreadable packet keeps the first fault; a 0x77 under 7 bytes and a 10-byte "
        + "redraw change nothing", Faults),
        new TestCase("state: a jump passes a resent step already answered, over a map takes the step rule's z, after "
        + "a turn is where the player stood", JumpDetails),
        new TestCase("state: past 64 jumps the oldest is dropped", JumpCap),
        new TestCase("state: a step answered past an older one drops the older from the queue", AckPastOlder),
        new TestCase("state: a 36-byte status names nobody and sets nothing; a type 0 status sets the hits, not the "
        + "stats", StatusEdges),
        new TestCase("state: the player's 11-byte line is the player's; a 0x194 body or no name is no chat; chat "
        + "keeps JournalKeep", SpeechEdges),
        new TestCase("state: contents of a box never seen are all it holds", ContentsNewBox),
        new TestCase("state: an 8-byte 0x78 sets nothing; the gear ends at a zero serial; a hue cut off reads 0",
        IncomingEdges),
        new TestCase("state: a ground item cut after a stepped graphic is a fault; a hue or a z cut off reads 0",
        GroundEdges),
        new TestCase("state: at ItemKeep a known item still moves; another mobile's gear is not the player's",
        ItemEdges),
        new TestCase("state: the sweep keeps an item in no container, in the player, held by a mobile in view, or 17 "
        + "deep in the pack", SweepKeeps),
        new TestCase("state: a skill list with no zero id, or its last entry cut, is a fault", SkillEdges),
        new TestCase("state: a box with nothing known in it counts 0; the summary counts worn items only",
        CountAndWorn),
        new TestCase("restock: a vendor's ware back with more than it had is a restock; with less or the same, a "
        + "purchase", RestockRise),
        new TestCase("restock: a ware back past RestockPairMs, in a box no vendor wears, never destroyed, or that "
        + "does not stack is none", RestockNot),
        new TestCase("restock: one a vendor within RestockGapMs; RestockKeep kept; the gear's stock box read past a "
        + "hued item", RestockLimits),
        new TestCase("restock: past MobileKeep vendors and ItemKeep wares gone the records start over", RestockCaps));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static AssistState Player()
    {
        AssistState state = new();
        state.Clock = new VirtualClock(s_start);
        state.Server(new byte[] { 0x3A, 0x00, 0x03 });
        state.Server(Packets.LoginConfirm(1000, 1000, 0));
        return state;
    }

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

    // 0xA1-0xA3: serial, maximum, current.
    static byte[] Vital(byte op, uint serial, int max, int now, int length = 9)
    {
        byte[] p = new byte[length];
        p[0] = op;
        Put32(p, 1, serial);
        if (length >= 9)
        {
            Put16(p, 5, max);
            Put16(p, 7, now);
        }
        return p;
    }

    static void Vitals()
    {
        AssistState s = Player();
        s.Server(Vital(0xA1, Packets.Player, 90, 80));
        s.Server(Vital(0xA2, Packets.Player, 70, 60));
        s.Server(Vital(0xA3, Packets.Player, 50, 40));
        Check.Equal(80 * 1000 + 90, s.Hits * 1000 + s.MaxHits, "hits");
        Check.Equal(60 * 1000 + 70, s.Mana * 1000 + s.MaxMana, "mana");
        Check.Equal(40 * 1000 + 50, s.Stam * 1000 + s.MaxStam, "stamina");
        foreach (byte op in new byte[] { 0xA1, 0xA2, 0xA3 })
        {
            s.Server(Vital(op, Other, 1, 1));
            s.Server(Vital(op, Packets.Player, 1, 1, 8));
        }
        Check.Equal(80 + 60 + 40, s.Hits + s.Mana + s.Stam, "unchanged by another serial or 8 bytes");
    }

    static byte[] TitleOf(uint serial, string text, int length)
    {
        byte[] p = new byte[length];
        p[0] = 0x88;
        Put32(p, 1, serial);
        Encoding.ASCII.GetBytes(text).CopyTo(p, 5);
        return p;
    }

    static void Title()
    {
        AssistState s = Player();
        s.Server(TitleOf(Packets.Player, "Kwik, Master Tailor", 64));
        Check.Equal("", s.Title, "64 bytes");
        s.Server(TitleOf(Other, "Morena", 65));
        Check.Equal("", s.Title, "another serial");
        s.Server(TitleOf(Packets.Player, "Kwik, Master Tailor", 65));
        Check.Equal("Kwik, Master Tailor", s.Title, "the title");
    }

    static void Status()
    {
        AssistState s = Player();
        s.Server(Packets.Status(51));
        Check.Equal("Kwik", s.Name, "name");
        Check.Equal(85 * 1000 + 85, s.Hits * 1000 + s.MaxHits, "hits");
        Check.Equal(51, s.Mana, "mana");
        Check.Equal(110, s.Weight, "weight");
        Check.Equal(s.Str, s.StrStart, "the starting strength");
        byte[] stronger = Packets.Status(51);
        Put16(stronger, 44, 90);
        s.Server(stronger);
        Check.Equal(90, s.Str, "strength");
        Check.Equal(85, s.StrStart, "the starting strength kept");
        byte[] shortOne = new byte[43];
        Array.Copy(Packets.Status(51), shortOne, 43);
        shortOne[1] = 0;
        shortOne[2] = 43;
        Put16(shortOne, 37, 12);
        s.Server(shortOne);
        Check.Equal(12, s.Hits, "a 43-byte status sets the hits");
        Check.Equal(90, s.Str, "and not the stats");
        byte[] another = Packets.Status(51);
        Put32(another, 3, Other);
        Put16(another, 37, 1);
        s.Server(another);
        Check.Equal(12, s.Hits, "another serial's status");
    }

    static byte[] SkillList(int size, params int[] values)
    {
        byte[] p = new byte[4 + values.Length * size + 2];
        p[0] = 0x3A;
        Put16(p, 1, p.Length);
        p[3] = 0;
        for (int i = 0; i < values.Length; i++)
        {
            Put16(p, 4 + i * size, i + 1);
            Put16(p, 6 + i * size, values[i]);
            // A 6-byte entry's base value: a zero there reads as the 4-byte list's end.
            if (size == 6)
                Put16(p, 8 + i * size, values[i]);
        }
        return p;
    }

    static void Skills()
    {
        AssistState s = Player();
        s.Server(SkillList(4, 100, 200, 300));
        Check.Equal(4, s.SkillEntry, "4-byte entries");
        Check.Equal(300, s.Skills[3], "skill 3");
        AssistState six = Player();
        six.Server(SkillList(6, 10, 20));
        Check.Equal(6, six.SkillEntry, "6-byte entries");
        Check.Equal(20, six.Skills[2], "skill 2");
        byte[] one = { 0x3A, 0x00, 0x08, 0xFF, 0x00, 0x19, 0x01, 0xF9 };
        s.Server(one);
        Check.Equal(505, s.Skills[26], "Magery from 0-based 25");
        Check.Equal(505, s.SkillStart[26], "its start");
        s.Server(new byte[] { 0x3A, 0x00, 0x07, 0xFF, 0x00, 0x19, 0x01 });
        s.Server(new byte[] { 0x3A, 0x00, 0x03 });
        s.Server(new byte[] { 0x3A, 0x00, 0x08, 0x01, 0x00, 0x01, 0x00, 0x05 });
        Check.True(s.Fault is null, "short and other types ignored: " + s.Fault);
        byte[] bad = SkillList(4, 1, 2);
        Put16(bad, 8, 7);
        s.Server(bad);
        Check.True(s.Fault is not null && s.Fault.StartsWith("opcode 3A"), "a list in no size is a fault: " + s.Fault);
    }

    static void DeleteAndDeath()
    {
        AssistState s = Player();
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        s.Server(Packets.Contained(Item, 0x0F39, 1, Box));
        s.Server(Packets.Ground(Item + 1, 0x0F39, 1001, 1001, 0));
        s.Server(Packets.Mobile(Other, 1002, 1002));
        Check.Equal(Box, s.Backpack, "the pack");
        s.Server(Packets.Delete(Item));
        Check.True(s.GroundGone == null, "a contained item deleted: no GroundGone");
        foreach (uint gone in new[] { Item + 1, Other, Box })
            s.Server(Packets.Delete(gone));
        Check.True(s.GroundGone != null && s.GroundGone.Serial == Item + 1 && s.GroundGone.X == 1001
            && s.GroundGone.Y == 1001, "the ground item kept as GroundGone, past the deletes after it");
        Check.False(s.Items.ContainsKey(Item), "the item");
        Check.False(s.Ground.ContainsKey(Item + 1), "the ground item");
        Check.False(s.Mobiles.ContainsKey(Other), "the mobile");
        Check.Equal(0u, s.Backpack, "the pack gone");
        Check.False(s.Dead, "alive");
        s.Server(new byte[] { 0x2C, 0x00 });
        Check.True(s.Dead, "dead");
    }

    static byte[] SpeechAs(uint serial, string name, string text, byte type, int body)
    {
        byte[] p = Packets.Speech(serial, name, text, type);
        Put16(p, 7, body);
        return p;
    }

    static void Speech()
    {
        AssistState s = Player();
        s.Server(Packets.Speech(Other, "Morena", "Hello", 0));
        Check.Equal("Morena", s.Names[Other], "named");
        Check.Equal(1, s.Chat.Count, "a human's speech is chat");
        s.Server(SpeechAs(Other, "Morena", "Grr", 0, 0x0D));
        s.Server(SpeechAs(Other, "Morena", "a label", 6, 0x190));
        s.Server(SpeechAs(Other | 0x40000000, "a bag", "a bag", 0, 0x190));
        s.Server(SpeechAs(Other, "System", "hi", 0, 0x190));
        s.Server(SpeechAs(Other, "Morena", "hi", 3, 0x190));
        foreach (byte type in new byte[] { 2, 8, 9 })
            s.Server(SpeechAs(Other, "Morena", "hi", type, 0x193));
        Check.Equal(4, s.Chat.Count, "speech, emote, whisper and yell of a human body are chat; nothing else");
        s.Server(Packets.Speech(Item, "", "a vendor (invulnerable)", 6));
        Check.True(s.Labelled.Contains(Item) && s.Invulnerable.Contains(Item), "labelled, invulnerable");
        s.Server(Packets.Speech(0xFFFFFFFF, "", "You are now under the protection of the town guards.", 0));
        Check.Equal(true, s.Guarded, "guarded");
        s.Server(Packets.Speech(0xFFFFFFFF, "", "You have left the protection of the town guards.", 0));
        Check.Equal(false, s.Guarded, "left");
    }

    static void Contents()
    {
        AssistState s = Player();
        s.Server(Packets.Contained(Item, 0x0F39, 1, Box));
        s.Server(Packets.Contents(Box, Item + 1, Item + 2));
        Check.False(s.Items.ContainsKey(Item), "the old content gone");
        Check.Equal(2, s.Count(Box), "two in the box");
        byte[] cut = Packets.Contents(Box, Item + 3, Item + 4);
        byte[] shorter = new byte[cut.Length - 1];
        Array.Copy(cut, shorter, shorter.Length);
        s.Server(shorter);
        Check.Equal(1, s.Count(Box), "the cut entry not read");
    }

    // Three shirts, a bag of two, a spellbook of 64 scrolls and a key ring of two keys: 3 + 1 + 2 + 1 + 1 (OUO
    // CContainer_CountItems), a worn item and an item beside the pack not counted.
    static void ItemCount()
    {
        const uint Pack = 0x40003000, Bag = 0x40003001, Book = 0x40003002, Ring = 0x40003003, Worn = 0x40003004, Out = 0x40003005;
        AssistState s = Player();
        s.Server(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.Server(Packets.Wear(Worn, 0x1517, 0x05, Packets.Player));
        for (uint i = 0; i < 3; i++)
            s.Server(Packets.Contained(Item + i, 0x1EFD, 1, Pack));
        s.Server(Packets.Contained(Bag, 0x0E76, 1, Pack));
        s.Server(Packets.Contained(Item + 10, 0x0F39, 1, Bag));
        s.Server(Packets.Contained(Item + 11, 0x0F39, 1, Bag));
        s.Server(Packets.Contained(Book, 0x0EFA, 1, Pack));
        for (uint i = 0; i < 64; i++)
            s.Server(Packets.Contained(Item + 100 + i, 0x1F2D + (int)i, 1, Book));
        s.Server(Packets.Contained(Ring, 0x1011, 1, Pack));
        s.Server(Packets.Contained(Item + 200, 0x100E, 1, Ring));
        s.Server(Packets.Contained(Item + 201, 0x100E, 1, Ring));
        s.Server(Packets.Contained(Out, 0x0F39, 1, Box));
        Check.Equal(8, new Inventory(s).ItemCount(), "the count");
        AssistState none = Player();
        Check.Equal(0, new Inventory(none).ItemCount(), "no pack");
    }

    // 0x78: length, serial (0x80000000 the player's 'no notoriety' flag), body, place, facing, hue, flags (17),
    // notoriety (18), then gear: serial, graphic (0x8000: a hue follows), layer.
    static byte[] Incoming78(uint serial, int x, int y, byte flags, byte noto,
        params (uint Serial, int Graphic, int Layer, int Hue)[] gear)
    {
        int len = 19;
        foreach ((uint _, int g, int _, int _) in gear)
            len += 7 + ((g & 0x8000) != 0 ? 2 : 0);
        byte[] p = new byte[len + 4];
        p[0] = 0x78;
        Put16(p, 1, p.Length);
        Put32(p, 3, serial);
        Put16(p, 7, 0x190);
        Put16(p, 9, x);
        Put16(p, 11, y);
        p[17] = flags;
        p[18] = noto;
        int at = 19;
        foreach ((uint s, int g, int layer, int hue) in gear)
        {
            Put32(p, at, s);
            Put16(p, at + 4, g);
            p[at + 6] = (byte)layer;
            if ((g & 0x8000) != 0)
                Put16(p, at + 7, hue);
            at += 7 + ((g & 0x8000) != 0 ? 2 : 0);
        }
        return p;
    }

    static void Incoming()
    {
        AssistState s = Player();
        s.Server(Incoming78(Other, 1003, 1004, 0, 6));
        Check.Equal(1003 * 10000 + 1004, s.Mobiles[Other]![0] * 10000 + s.Mobiles[Other]![1], "placed");
        Check.Equal(6, s.Notorieties[Other], "its notoriety");
        s.Server(Incoming78(Packets.Player, 1000, 1000, 0x04, 3, (Item, 0x8F39, 2, 0x0455),
            (Item + 1, 0x0E75, 0x15, 0)));
        Check.Equal(3, s.Notoriety, "the player's notoriety");
        Check.True(s.Poisoned, "poisoned");
        Check.Equal(0x0F39 * 0x10000 + 0x0455, s.Items[Item]!.Graphic * 0x10000 + s.Items[Item]!.Hue,
            "gear with its hue");
        Check.Equal(Item + 1, s.Backpack, "the pack from the gear");
        s.Server(Incoming78(Packets.Player | 0x80000000, 1000, 1000, 0, 5));
        Check.Equal(3, s.Notoriety, "the flagged serial keeps the notoriety");
        s.Server(new byte[] { 0x77, 0x02, 0xBE, 0x4F, 0x79, 0x01, 0x90 });
        s.Server(Packets.Mobile(Other, 1010, 1011));
        Check.Equal(1010, s.Mobiles[Other]![0], "0x77 moves another");
    }

    static void GroundItem()
    {
        AssistState s = Player();
        // serial | 0x80000000: an amount follows; graphic | 0x8000: a step byte follows; x | 0x8000: a facing byte; y |
        // 0x8000: a hue after z.
        byte[] p = { 0x1A, 0x00, 0x14, 0xC0, 0x00, 0x30, 0x00, 0x86, 0xA5, 0x00, 0x05, 0x01, 0x83, 0xE9, 0x83, 0xEA,
            0x02, 0x07, 0x04, 0x55 };
        s.Server(p);
        AssistItem g = s.Ground[0x40003000]!;
        Check.Equal(0x06A6, g.Graphic, "the art stepped");
        Check.Equal(1001 * 10000 + 1002, g.X * 10000 + g.Y, "the place");
        Check.Equal(7, g.Z, "z after the facing byte");
        Check.Equal(0x0455, g.Hue, "the hue");
        s.Server(Packets.Ground(0x40003001, 0x0F39, 1000 + AssistState.GroundReach + 1, 1000, 0));
        s.Server(Packets.StepRefused(0, 1000, 1000));
        Check.False(s.Ground.ContainsKey(0x40003001), "past GroundReach pruned");
    }

    static void Steps()
    {
        AssistState s = Player();
        s.Client(new byte[] { 0x02, 0x02, 0x05 });
        s.Server(Packets.StepAck(5));
        Check.Equal(1001, s.X, "a step in the facing moves");
        s.Client(new byte[] { 0x02, 0x04, 0x06 });
        s.Server(Packets.StepAck(6));
        Check.Equal(1001 * 10 + 4, s.X * 10 + s.Facing, "a new facing only turns");
        s.Server(Packets.StepAck(9));
        Check.Equal(1001, s.X, "an answer to no step");
        s.Client(new byte[] { 0x02, 0x04, 0x07 });
        s.Server(Packets.StepRefused(7, 1001, 1000, 4));
        s.Server(Packets.StepAck(7));
        Check.Equal(1000, s.Y, "the refusal reset the step");
        s.Server(Packets.Cursor(0x00000001, 0));
        Check.True(s.CursorOpen, "the server's cursor");
        s.Client(new byte[] { 0x6C, 0x00 });
        Check.False(s.CursorOpen, "the client's target closes it");
    }

    static void Jump()
    {
        AssistState s = Player();
        s.Client(new byte[] { 0x02, 0x02, 0x01 });
        s.Server(Packets.StepAck(1));
        s.Client(new byte[] { 0x02, 0x02, 0x02 });
        s.Server(Packets.Draw(1500, 1500, 0, 0));
        Check.Equal(1, s.Jumps.Count, "learned");
        Check.Equal("1002,1000,1500,1500",
            s.Jumps[0][0] + "," + s.Jumps[0][1] + "," + s.Jumps[0][3] + "," + s.Jumps[0][4],
            "from the unanswered step's tile");
        s.Server(Packets.Cursor(1, 0));
        s.Client(new byte[] { 0x02, 0x02, 0x03 });
        s.Server(Packets.StepAck(3));
        s.Server(Packets.Draw(2000, 2000, 0, 0));
        Check.Equal(1, s.Jumps.Count, "a cursor just open: a recall, not a teleporter");
    }

    static void JumpEdges()
    {
        AssistState s = Player();
        s.Client(new byte[] { 0x02, 0x02, 0x01 });
        s.Server(Packets.StepAck(1));
        s.Server(Packets.Draw(1500, 1500, 0, 0));
        Check.Equal(1, s.Jumps.Count, "learned");
        Check.Equal("1001,1000", s.Jumps[0][0] + "," + s.Jumps[0][1], "where the player stood, every step answered");
        AssistState late = Player();
        late.Client(new byte[] { 0x02, 0x02, 0x01 });
        late.Server(Packets.StepAck(1));
        ((VirtualClock)late.Clock).Run(() => late.Clock.Sleep(AssistState.JumpStepMs));
        late.Server(Packets.Draw(1500, 1500, 0, 0));
        Check.Equal(0, late.Jumps.Count, "a redraw JumpStepMs after the step is no teleporter");
        Check.Equal(1500, late.X, "but moves the player");
    }

    static void PruneAndSweep()
    {
        AssistState s = Player();
        s.Server(Packets.Mobile(Other, 1000 + AssistState.GroundReach, 1000));
        s.Server(Packets.Mobile(Other + 1, 1000 + AssistState.GroundReach + 1, 1000));
        s.Server(Packets.Ground(Box, 0x0E76, 1001, 1001, 0));
        s.Server(Packets.Contained(Item, 0x0F39, 1, Box));
        s.Server(Packets.Contained(Item + 1, 0x0F39, 1, 0x40009999));
        s.Server(Packets.StepRefused(0, 1000, 1000));
        Check.True(s.Mobiles.ContainsKey(Other), "at GroundReach kept");
        Check.False(s.Mobiles.ContainsKey(Other + 1), "past it dropped");
        s.Server(Packets.StepRefused(0, 1000 + AssistState.GroundReach + 1, 1000));
        Check.False(s.Items.ContainsKey(Item + 1), "contents of a holder not in view swept");
        Check.True(s.Items.ContainsKey(Item), "the box at GroundReach keeps its contents");
        s.Server(Packets.StepRefused(0, 1000 + 2 * AssistState.GroundReach + 2, 1000));
        Check.False(s.Ground.ContainsKey(Box), "the box left behind");
        Check.False(s.Items.ContainsKey(Item), "and its contents swept");
    }

    static void LabelCap()
    {
        AssistState s = Player();
        s.Server(Packets.Speech(Item, "", "a vendor (invulnerable)", 6));
        for (uint i = 1; i <= AssistState.MobileKeep; i++)
            s.Server(Packets.Speech(Item + i, "", "a label", 6));
        Check.True(s.Invulnerable.Contains(Item), "kept up to MobileKeep");
        s.Server(Packets.Speech(Item + AssistState.MobileKeep + 1, "", "a label", 6));
        Check.False(s.Invulnerable.Contains(Item), "past it, started over");
        Check.Equal(1, s.Labelled.Count, "the new label alone");
    }

    static void SummaryDetail()
    {
        AssistState s = Player();
        s.Server(SkillList(4, 0, 200));
        s.Server(new byte[] { 0x3A, 0x00, 0x08, 0xFF, 0x00, 0x01, 0x00, 0xD2 });
        s.Server(Packets.Cursor(0x0000ABCD, 0));
        string line = s.Summary();
        Check.True(line.Contains(" skills 2 (1 above 0, 1 moved, entry 4)"), "the skills: " + line);
        Check.True(line.Contains(" pack ? ") && line.EndsWith(" cursor ABCD type 0"), "no pack, the cursor: " + line);
    }

    static void GroundCap()
    {
        AssistState s = Player();
        for (uint i = 0; i < AssistState.ItemKeep; i++)
            s.Server(Packets.Ground(0x41000000 + i, 0x0F39, 1001, 1001, 0));
        Check.Equal(AssistState.ItemKeep, s.Ground.Count, "full");
        s.Server(Packets.Ground(0x42000000, 0x0F39, 1001, 1001, 0));
        Check.False(s.Ground.ContainsKey(0x42000000), "a new one not kept");
        s.Server(Packets.Ground(0x41000000, 0x0F39, 1002, 1002, 0));
        Check.Equal(1002, s.Ground[0x41000000]!.X, "a known one moves");
    }

    static void ItemCap()
    {
        AssistState s = Player();
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        for (uint i = 0; i < AssistState.ItemKeep - 2; i++)
            s.Server(Packets.Contained(0x41000000 + i, 0x0F39, 1, Box));
        s.Server(Packets.Contained(Item, 0x0F39, 1, 0x40009999));
        Check.Equal(AssistState.ItemKeep, s.Items.Count, "full: the pack, its contents and one left behind");
        s.Server(Packets.Contained(Item + 1, 0x0F39, 1, Box));
        Check.True(s.Items.ContainsKey(Item + 1), "kept: the left-behind swept to make room");
        Check.False(s.Items.ContainsKey(Item), "the left-behind gone");
        s.Server(Packets.Contained(Item + 2, 0x0F39, 1, Box));
        Check.False(s.Items.ContainsKey(Item + 2), "no room left to make: not kept");
    }

    static byte[] Cut(byte[] p, int length)
    {
        byte[] q = new byte[length];
        Array.Copy(p, q, Math.Min(length, p.Length));
        return q;
    }

    static void ShortPackets()
    {
        AssistState s = Player();
        s.Server(Cut(Packets.Mobile(Other, 1003, 1003), 11));
        Check.False(s.Mobiles.ContainsKey(Other), "0x77 of 11 bytes places nobody");
        s.Server(Cut(Packets.Mobile(Other, 1003, 1003), 16));
        Check.True(s.Mobiles.ContainsKey(Other) && !s.Notorieties.ContainsKey(Other),
            "16 bytes place it, no notoriety");
        s.Server(Cut(Incoming78(Other + 1, 1004, 1004, 0, 6), 13));
        Check.False(s.Mobiles.ContainsKey(Other + 1), "0x78 of 13 bytes places nobody");
        s.Server(Cut(Incoming78(Other + 1, 1004, 1004, 0, 6), 18));
        Check.True(s.Mobiles.ContainsKey(Other + 1) && !s.Notorieties.ContainsKey(Other + 1),
            "18 bytes place it, no notoriety");
        s.Server(Cut(Incoming78(Packets.Player, 1000, 1000, 0x04, 3), 18));
        Check.False(s.Poisoned, "the player's 0x78 of 18 bytes carries no flags");
        byte[] draw = Packets.Draw(1010, 1010, 0, 0x04);
        draw[4] = 0x7A;
        s.Server(draw);
        Check.Equal(1000, s.X, "another's 0x20 moves nobody");
        s.Server(Cut(Packets.Draw(1001, 1000, 0, 0x04), 19));
        Check.Equal(1001, s.X, "the player's redraw");
        s.Server(Packets.Ground(Item, 0x0F39, 1002, 1002, 5));
        Check.Equal(5 * 1000 + 0, s.Ground[Item]!.Z * 1000 + s.Ground[Item]!.Hue, "a plain ground item: z, no hue");
    }

    static byte[] StatusOf(uint serial, int length)
    {
        byte[] p = Cut(Packets.Status(51), length);
        Put32(p, 3, serial);
        return p;
    }

    static byte[] DrawAs(int body)
    {
        byte[] p = Packets.Draw(1000, 1000, 0, 0);
        Put16(p, 5, body);
        return p;
    }

    static void NamesAndGhosts()
    {
        AssistState s = Player();
        byte[] p = StatusOf(Other, 66);
        p[8] = 0x07;
        p[9] = 0xC9;
        s.Server(p);
        Check.Equal("K??k", s.Names[Other], "a control byte and a byte past 126");
        s.Server(DrawAs(0x192));
        Check.True(s.Dead, "0x192 dead");
        s.Server(DrawAs(0x190));
        Check.False(s.Dead, "0x190 alive");
        s.Server(DrawAs(0x193));
        Check.True(s.Dead, "0x193 dead");
        s.Server(DrawAs(0x194));
        Check.False(s.Dead, "0x194 alive");
    }

    static void NameCap()
    {
        AssistState s = Player();
        byte[] nameless = StatusOf(Other, 66);
        nameless[7] = 0;
        s.Server(nameless);
        Check.False(s.Names.ContainsKey(Other), "no name");
        for (uint i = 0; i <= AssistState.MobileKeep; i++)
            s.Server(StatusOf(0x00010000 + i, 37));
        Check.Equal(AssistState.MobileKeep + 1, s.Names.Count, "kept past MobileKeep by one");
        s.Server(StatusOf(Other, 37));
        Check.Equal(1, s.Names.Count, "then started over");
        Check.Equal("Kwik", s.Names[Other], "the new name");
    }

    static void Faults()
    {
        AssistState s = Player();
        byte[] bad = SkillList(4, 1, 2);
        Put16(bad, 8, 7);
        s.Server(bad);
        s.Server(Cut(Packets.Draw(1010, 1010, 0, 0), 10));
        Check.True(s.Fault is not null && s.Fault.StartsWith("opcode 3A"), "the first fault kept: " + s.Fault);
        AssistState draw = Player();
        draw.Server(Cut(Packets.Draw(1010, 1010, 0, 0x04), 10));
        Check.True(draw.Fault is not null && draw.Fault.StartsWith("opcode 20 length 10"),
            "a 10-byte redraw is a fault: " + draw.Fault);
        Check.Equal(1000, draw.X, "and moves nobody");
        Check.False(draw.Poisoned, "nor poisons");
        draw.Server(Cut(Packets.Mobile(Other, 1003, 1003), 6));
        Check.False(draw.Bodies.ContainsKey(Other) || draw.Mobiles.ContainsKey(Other), "a 6-byte 0x77 places nothing");
    }

    // Over the test map's grass at z 0 (tiles 0 to 255), the player redrawn at z 5 before the unanswered step: without
    // a map the jump would keep z 5.
    static void JumpDetails()
    {
        new TestMap().Run(() =>
        {
            AssistState s = new();
            s.Clock = new VirtualClock(s_start);
            s.Server(Packets.LoginConfirm(100, 100, 5));
            s.Client(Packets.Step(2, 1));
            s.Client(Packets.Step(2, 1));
            s.Server(Packets.StepAck(1));
            s.Server(Packets.Draw(101, 100, 5, 0));
            s.Client(Packets.Step(2, 2));
            s.Server(Packets.Draw(200, 200, 0, 0));
            Check.Equal(1, s.Jumps.Count, "learned");
            Check.Equal("102,100,0", s.Jumps[0][0] + "," + s.Jumps[0][1] + "," + s.Jumps[0][2],
                "the second step's tile at the grass's z, the resent first passed");
            AssistState turn = Player();
            turn.Client(Packets.Step(2, 1));
            turn.Server(Packets.StepAck(1));
            turn.Client(Packets.Step(4, 2));
            turn.Server(Packets.Draw(1500, 1500, 0, 0));
            Check.Equal("1001,1000", turn.Jumps[0][0] + "," + turn.Jumps[0][1],
                "a turn unanswered: where the player stood");
        });
    }

    static void JumpCap()
    {
        AssistState s = Player();
        for (int i = 0; i <= 64; i++)
        {
            s.Client(Packets.Step(2, i + 1));
            s.Server(Packets.StepAck(i + 1));
            s.Server(Packets.Draw(i % 2 == 0 ? 1500 : 1000, 1000, 0, 0));
        }
        Check.Equal(64, s.Jumps.Count, "64 kept");
        Check.Equal(1501, s.Jumps[0][0], "the first dropped: the second, from 1501, is the oldest");
    }

    static void AckPastOlder()
    {
        AssistState s = Player();
        s.Client(Packets.Step(2, 1));
        s.Client(Packets.Step(2, 2));
        s.Server(Packets.StepAck(2));
        Check.Equal(1001, s.X, "the second step's answer moves");
        s.Server(Packets.Draw(1500, 1500, 0, 0));
        Check.Equal("1001,1000", s.Jumps[0][0] + "," + s.Jumps[0][1],
            "the first step left the queue: the jump is where the player stood");
    }

    static void StatusEdges()
    {
        AssistState s = Player();
        s.Server(StatusOf(Packets.Player, 36));
        Check.False(s.Names.ContainsKey(Packets.Player), "36 bytes name nobody");
        Check.Equal(0, s.Hits, "nor set the hits");
        byte[] zero = Packets.Status(51);
        zero[42] = 0;
        s.Server(zero);
        Check.Equal("Kwik", s.Name, "type 0: the name");
        Check.Equal(85, s.Hits, "the hits");
        Check.Equal(0, s.Str, "not the stats");
        Check.Equal(-1, s.StrStart, "nor the starting stats");
    }

    static void SpeechEdges()
    {
        AssistState s = Player();
        long seq = s.JournalSeq;
        s.Server(Cut(Packets.Speech(Packets.Player, "", "x", 0, AssistState.BarkHue), 11));
        Check.True(s.PlayerSpokeSince(seq), "11 bytes have no hue: the player's own line");
        s.Server(SpeechAs(Other, "Morena", "hi", 0, 0x194));
        s.Server(Packets.Speech(Other, "", "hi", 0));
        Check.Equal(0, s.Chat.Count, "a 0x194 body or no name is no chat");
        for (int i = 0; i <= AssistState.JournalKeep; i++)
            s.Server(Packets.Speech(Other, "Morena", "line " + i, 0));
        Check.Equal(AssistState.JournalKeep, s.Chat.Count, "JournalKeep kept");
        Check.Equal("line 1", s.Chat[0][2], "the oldest dropped");
    }

    static void ContentsNewBox()
    {
        AssistState s = Player();
        s.Server(Packets.Contents(Box, Item));
        Check.Equal(1, s.Count(Box), "the one item");
    }

    static void IncomingEdges()
    {
        AssistState s = Player();
        s.Server(Cut(Incoming78(Other, 1003, 1003, 0, 6), 8));
        Check.False(s.Bodies.ContainsKey(Other) || s.Mobiles.ContainsKey(Other), "8 bytes of another place nothing");
        s.Server(DrawAs(0x192));
        s.Server(Cut(Incoming78(Packets.Player, 1000, 1000, 0x04, 3), 8));
        Check.True(s.Dead && !s.Poisoned && s.Notoriety == 0, "8 bytes of the player change nothing");
        Check.True(s.Fault is null, "and are no fault: " + s.Fault);
        s.Server(Incoming78(Packets.Player, 1000, 1000, 0, 3, (Item, 0x0F39, 2, 0), (0, 0, 0, 0),
            (Item + 1, 0x0F39, 3, 0)));
        Check.True(s.Items.ContainsKey(Item) && !s.Items.ContainsKey(Item + 1), "the gear ends at the zero serial");
        s.Server(Cut(Incoming78(Packets.Player, 1000, 1000, 0, 3, (Item + 2, 0x8F39, 2, 0x0455)), 26));
        Check.Equal(0x0F39 * 0x10000, s.Items[Item + 2]!.Graphic * 0x10000 + s.Items[Item + 2]!.Hue,
            "a hue cut off reads 0");
    }

    static void GroundEdges()
    {
        AssistState s = Player();
        s.Server(new byte[] { 0x1A, 0x00, 0x09, 0x40, 0x00, 0x30, 0x00, 0x86, 0xA5 });
        Check.True(s.Fault is not null && s.Fault.StartsWith("opcode 1A length 9"),
            "cut after a stepped graphic: " + s.Fault);
        Check.False(s.Ground.ContainsKey(0x40003000), "and not kept");
        byte[] hued = Packets.Ground(Item, 0x0F39, 1001, 1001, 5);
        hued[11] |= 0x80;
        s.Server(hued);
        Check.Equal(5 * 1000 + 0, s.Ground[Item]!.Z * 1000 + s.Ground[Item]!.Hue, "a hue flagged and cut off: z, hue 0");
        s.Server(Cut(Packets.Ground(Item + 1, 0x0F39, 1001, 1001, 5), 13));
        Check.Equal(0, s.Ground[Item + 1]!.Z, "a z cut off reads 0");
    }

    static void ItemEdges()
    {
        AssistState s = Player();
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        for (uint i = 0; i < AssistState.ItemKeep - 1; i++)
            s.Server(Packets.Contained(0x41000000 + i, 0x0F39, 1, Box));
        Check.Equal(AssistState.ItemKeep, s.Items.Count, "full");
        s.Server(Packets.Contained(0x41000000, 0x0F39, 1, 0x41000001));
        Check.Equal(0x41000001u, s.Items[0x41000000]!.Container, "a known one moves at ItemKeep");
        AssistState other = Player();
        other.Server(Packets.Wear(Item, 0x1517, 0x05, Other));
        Check.False(other.Items.ContainsKey(Item), "another mobile's gear");
    }

    static void SweepKeeps()
    {
        AssistState s = Player();
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        s.Server(Packets.Contained(Item, 0x0F39, 1, 0));
        s.Server(Packets.Contained(Item + 1, 0x0F39, 1, Packets.Player));
        s.Server(Packets.Mobile(Other, 1000 + AssistState.GroundReach + 1, 1000));
        s.Server(Packets.Contained(Item + 2, 0x0F39, 1, Other));
        s.Server(Packets.Contained(Item + 10, 0x0E76, 1, Box));
        for (uint i = 11; i <= 27; i++)
            s.Server(Packets.Contained(Item + i, 0x0E76, 1, Item + i - 1));
        s.Server(Packets.Contained(Item + 3, 0x0F39, 1, 0x40009999));
        s.Server(Packets.StepRefused(0, 1000 + AssistState.GroundReach + 1, 1000));
        Check.False(s.Items.ContainsKey(Item + 3), "the control: a holder not in view swept");
        Check.True(s.Items.ContainsKey(Item), "in no container kept");
        Check.True(s.Items.ContainsKey(Item + 1), "in the player kept");
        Check.True(s.Items.ContainsKey(Item + 2), "held by a mobile in view kept");
        Check.True(s.Items.ContainsKey(Item + 27), "17 deep in the pack kept");
    }

    static void SkillEdges()
    {
        AssistState s = Player();
        byte[] list = SkillList(4, 100, 200);
        s.Server(Cut(list, list.Length - 2));
        Check.True(s.Fault is not null && s.Fault.Contains("fits neither"), "no zero id: " + s.Fault);
        AssistState cut = Player();
        cut.Server(Cut(list, 10));
        Check.True(cut.Fault is not null && cut.Fault.Contains("fits neither"), "the last entry cut: " + cut.Fault);
        Check.Equal(0, cut.Skills.Count, "no skill read");
    }

    static void CountAndWorn()
    {
        AssistState s = Player();
        Check.Equal(0, s.Count(Box), "nothing known");
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        s.Server(Packets.Contained(Item, 0x0F39, 1, Box));
        Check.True(s.Summary().Contains(" worn 1 "), "worn: " + s.Summary());
    }

    const uint Atta = 0x00096B16, AttaBox = 0x40096B17, Ware = 0x40096B18;

    // Atta drawn wearing her stock box at layer 0x1A, a ware of thread in it.
    static AssistState Atta20()
    {
        AssistState s = Player();
        s.Server(Incoming78(Atta, 1001, 1000, 0, 1, (AttaBox, 0x0E75, AssistState.StockBoxLayer, 0)));
        s.Server(Packets.Contained(Ware, 0x0FA0, 20, AttaBox));
        return s;
    }

    static void Pair(AssistState s, uint ware, int amount, uint box, int gapMs = 0)
    {
        s.Server(Packets.Delete(ware));
        if (gapMs > 0)
            ((VirtualClock)s.Clock).Run(() => s.Clock.Sleep(gapMs));
        s.Server(Packets.Contained(ware, 0x0FA0, amount, box));
    }

    static void Later(AssistState s, int ms) => ((VirtualClock)s.Clock).Run(() => s.Clock.Sleep(ms));

    static void RestockRise()
    {
        AssistState s = Atta20();
        Check.Equal(Atta, s.VendorBoxes[AttaBox], "the stock box is Atta's");
        Pair(s, Ware, 21, AttaBox);
        Check.Equal(1, s.Restocks.Count, "21 after 20: a restock");
        Check.Equal(Atta, s.Restocks[0].Vendor, "Atta's");
        Check.Equal(s.Clock.UtcNow, s.Restocks[0].At, "now");
        Later(s, AssistState.RestockGapMs);
        Pair(s, Ware, 15, AttaBox);
        Pair(s, Ware, 15, AttaBox);
        Check.Equal(1, s.Restocks.Count, "15 after 21, then 15 after 15: purchases");
    }

    static void RestockNot()
    {
        AssistState s = Atta20();
        Pair(s, Ware, 25, AttaBox, AssistState.RestockPairMs + 1);
        Check.Equal(0, s.Restocks.Count, "back past RestockPairMs");
        Pair(s, Ware, 30, AttaBox, AssistState.RestockPairMs);
        Check.Equal(1, s.Restocks.Count, "back at RestockPairMs");
        AssistState other = Player();
        other.Server(Packets.Contained(Ware, 0x0FA0, 20, Box));
        Pair(other, Ware, 21, Box);
        Check.Equal(0, other.Restocks.Count, "a box no vendor wears");
        AssistState fresh = Atta20();
        fresh.Server(Packets.Contained(Ware + 1, 0x0FA0, 21, AttaBox));
        Check.Equal(0, fresh.Restocks.Count, "a ware never destroyed");
        AssistState gone = Atta20();
        gone.Server(Packets.Delete(Ware));
        gone.Server(Packets.Contained(Ware, 0x0FA0, 21, Box));
        Check.Equal(0, gone.Restocks.Count, "back in a box no vendor wears");
        new TestMap().Run(() =>
        {
            AssistState bag = Player();
            bag.Server(Incoming78(Atta, 1001, 1000, 0, 1, (AttaBox, 0x0E75, AssistState.StockBoxLayer, 0)));
            bag.Server(Packets.Contained(Ware, TestMap.Bag, 1, AttaBox));
            bag.Server(Packets.Delete(Ware));
            bag.Server(Packets.Contained(Ware, TestMap.Bag, 5, AttaBox));
            Check.Equal(0, bag.Restocks.Count, "a ware that does not stack (its amount is no count)");
        });
    }

    static void RestockLimits()
    {
        AssistState s = Atta20();
        Pair(s, Ware, 21, AttaBox);
        Later(s, AssistState.RestockGapMs - 1);
        Pair(s, Ware, 22, AttaBox);
        Check.Equal(1, s.Restocks.Count, "within RestockGapMs: the same restock");
        Later(s, 1);
        Pair(s, Ware, 23, AttaBox);
        Check.Equal(2, s.Restocks.Count, "at RestockGapMs: the next");
        AssistState many = Player();
        for (uint v = 0; v <= AssistState.RestockKeep; v++)
        {
            many.Server(Incoming78(0x00100000 + v, 1001, 1000, 0, 1,
                (0x41200000 + v, 0x0E75, AssistState.StockBoxLayer, 0)));
            many.Server(Packets.Contained(0x41300000 + v, 0x0FA0, 1, 0x41200000 + v));
            Pair(many, 0x41300000 + v, 2, 0x41200000 + v);
        }
        Check.Equal(AssistState.RestockKeep, many.Restocks.Count, "RestockKeep kept");
        Check.Equal(0x00100001u, many.Restocks[0].Vendor, "the oldest dropped");
        AssistState hued = Player();
        hued.Server(Incoming78(Atta, 1001, 1000, 0, 1, (Item, 0x9F39, 1, 0x0455),
            (AttaBox, 0x0E75, AssistState.StockBoxLayer, 0), (0, 0, 0, 0),
            (Box, 0x0E75, AssistState.StockBoxLayer, 0)));
        Check.True(hued.VendorBoxes.ContainsKey(AttaBox) && !hued.VendorBoxes.ContainsKey(Box),
            "the box past a hued item, nothing past a zero serial");
    }

    static void RestockCaps()
    {
        AssistState s = Player();
        for (uint v = 0; v <= AssistState.MobileKeep; v++)
            s.Server(Incoming78(0x00200000 + v, 1001, 1000, 0, 1,
                (0x41400000 + v, 0x0E75, AssistState.StockBoxLayer, 0)));
        Check.Equal(AssistState.MobileKeep + 1, s.VendorBoxes.Count, "kept past MobileKeep by one");
        s.Server(Incoming78(Atta, 1001, 1000, 0, 1, (AttaBox, 0x0E75, AssistState.StockBoxLayer, 0)));
        Check.Equal(1, s.VendorBoxes.Count, "then started over");
        for (uint w = 0; w <= AssistState.ItemKeep; w++)
        {
            s.Server(Packets.Contained(0x41500000 + w, 0x0FA0, 1, AttaBox));
            s.Server(Packets.Delete(0x41500000 + w));
        }
        s.Server(Packets.Contained(Ware, 0x0FA0, 1, AttaBox));
        Pair(s, Ware, 2, AttaBox);
        Check.Equal(1, s.Restocks.Count, "past ItemKeep wares gone, a ware's pair still read");
        AssistState v2 = Player();
        for (uint v = 0; v <= AssistState.MobileKeep + 1; v++)
        {
            v2.Server(Incoming78(0x00300000 + v, 1001, 1000, 0, 1,
                (0x41600000 + v, 0x0E75, AssistState.StockBoxLayer, 0)));
            v2.Server(Packets.Contained(0x41700000 + v, 0x0FA0, 1, 0x41600000 + v));
            Pair(v2, 0x41700000 + v, 2, 0x41600000 + v);
        }
        Check.Equal(AssistState.RestockKeep, v2.Restocks.Count, "past MobileKeep vendors restocked, each still one");
    }

    static void Summary()
    {
        AssistState s = Player();
        s.Server(Packets.Status(51));
        s.Server(Packets.Wear(Box, 0x0E75, 0x15, Packets.Player));
        string line = s.Summary();
        Check.True(line.StartsWith("player " + Packets.Player.ToString("X") + " 'Kwik' at 1000,1000,0"),
            "the player: " + line);
        Check.True(line.Contains("pack " + Box.ToString("X") + " holding 0") && line.EndsWith(" no cursor"),
            "the pack, no cursor: " + line);
        s.Server(new byte[] { 0x3A, 0x00, 0x08, 0x00, 0x00, 0x07, 0x00, 0x05 });
        Check.True(s.Summary().Contains(" FAULT opcode 3A"), "the fault");
    }
}
