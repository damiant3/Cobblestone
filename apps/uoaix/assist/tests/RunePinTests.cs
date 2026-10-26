#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist.Tests;

// A rune picked by target is its serial (AssistIdentity.md stage 1): the mining bank rune and a cave's rune back keep
// the serial targeted, and PackRuneFor takes it first while it is in the pack and lands in the place or has not landed.
public static class RunePinTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Pinned = 0x44AD0001;
    const uint Other = 0x44AD0002;
    const uint Away = 0x44AD0003;
    const int RuneArt = 0x1F14;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("rune pin: a targeted rune in the pack with no landing is taken, no label asked", PinnedNoLanding),
        new TestCase("rune pin: a targeted rune landing elsewhere is not taken", PinnedLandsElsewhere),
        new TestCase("rune pin: a targeted rune not in the pack is not taken", PinnedNotInPack),
        new TestCase("rune pin: the bank rune set by label holds no serial; by its cursor it keeps the rune's",
        BankRuneSerial),
        new TestCase("rune pin: a cave's rune by its cursor keeps the serial in the cave file across a restart",
        CaveRuneSerial),
        new TestCase("rune pin: a targeted rune landing in the place is taken", PinnedLandsHere),
        new TestCase("rune pin: the bank rune cursor refuses a cancel, an item that is no rune, a rune outside the "
        + "pack and a rune with no label", BankCursorRefusals),
        new TestCase("rune pin: the cave rune cursor refuses an item that is no rune and a rune with no label",
        CaveCursorRefusals),
        new TestCase("rune pin: a cave line written before the serial column loads with none; an unknown cave is "
        + "refused", OldCaveLine),
        new TestCase("kinds: a rune, a shovel and a forge by their graphics, the ends and their neighbours",
        KindsByGraphic));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Kwik(FakeShard s, params uint[] runes)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        foreach (uint r in runes)
            s.ServerSends(Packets.Contained(r, RuneArt, 1, Pack));
        s.Clock.Sleep(1);
    }

    static int Looks(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x09 ? 1 : 0;
        return n;
    }

    // A target cursor's answer from the client: object kind, the cursor, the serial.
    static byte[] Targeted(uint cursor, uint serial)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[2] = (byte)(cursor >> 24);
        p[3] = (byte)(cursor >> 16);
        p[4] = (byte)(cursor >> 8);
        p[5] = (byte)cursor;
        p[7] = (byte)(serial >> 24);
        p[8] = (byte)(serial >> 16);
        p[9] = (byte)(serial >> 8);
        p[10] = (byte)serial;
        p[17] = RuneArt >> 8;
        p[18] = RuneArt & 0xFF;
        return p;
    }

    // The single-click label of each rune clicked: label.
    static void Labels(FakeShard s, string label)
    {
        s.Answer = p =>
        {
            if (p[0] != 0x09)
                return ImmutableArray<byte[]>.Empty;
            uint serial = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
            return ImmutableArray.Create<byte[]>(Packets.Speech(serial, "", label, 6));
        };
    }

    static void PinnedNoLanding()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Pinned, Other);
            Check.Equal(Pinned, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true, Pinned), "the pinned rune");
            Check.Equal(0, Looks(s), "no label asked");
        });
    }

    // Away lands at 2967,620 (a recall recorded it); a place that is not there takes another rune, never Away.
    static void PinnedLandsElsewhere()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Away);
            s.ServerSends(Packets.Contained(0x464FAC6C, 0x0F86, 30, Pack));
            s.ServerSends(Packets.Contained(0x464FABFC, 0x0F7B, 30, Pack));
            s.ServerSends(Packets.Contained(0x46B09470, 0x0F7A, 30, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            s.Answer = p =>
            {
                if (p[0] == 0x12 && p[3] == 0x56)
                {
                    s.Also(1500, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                    return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0,
                        0x5A));
                }
                if (p[0] == 0x6C)
                    return ImmutableArray.Create<byte[]>(Packets.Draw(2967, 620, 0, 0));
                return ImmutableArray<byte[]>.Empty;
            };
            Check.Null(f.Recall(Away), "the recall that records Away's landing");
            Labels(s, "minoc bank");
            Check.Equal(0u, f.PackRuneFor(s.Link, "minoc bank", (x, y) => x < 2000, Away),
                "not the rune landing elsewhere");
        });
    }

    // Away has landed at 2967,620 (PinnedLandsElsewhere's recall, or this case's own).
    static void PinnedLandsHere()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Away);
            s.ServerSends(Packets.Contained(0x464FAC6C, 0x0F86, 30, Pack));
            s.ServerSends(Packets.Contained(0x464FABFC, 0x0F7B, 30, Pack));
            s.ServerSends(Packets.Contained(0x46B09470, 0x0F7A, 30, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            s.Answer = p =>
            {
                if (p[0] == 0x12 && p[3] == 0x56)
                {
                    s.Also(1500, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                    return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0,
                        0x5A));
                }
                if (p[0] == 0x6C)
                    return ImmutableArray.Create<byte[]>(Packets.Draw(2967, 620, 0, 0));
                return ImmutableArray<byte[]>.Empty;
            };
            Check.Null(f.Recall(Away), "the recall that records Away's landing");
            Check.Equal(Away, f.PackRuneFor(s.Link, "anything", (x, y) => x == 2967 && y == 620, Away), "taken");
            Check.Equal(0, Looks(s), "no label asked");
        });
    }

    static void BankCursorRefusals()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Pinned);
            s.ServerSends(Packets.Contained(Other, 0x0E76, 1, Pack));
            s.ServerSends(Packets.Contained(Away, RuneArt, 1, 0x44ADFFFF));
            s.Clock.Sleep(1);
            try
            {
                foreach (uint target in new[] { 0u, Other, Away })
                {
                    s.ClientSends(Targeted(AssistFeatures.BankRuneCursor, target));
                    s.Clock.Sleep(50);
                    Check.True(f.ToolbagStatus.StartsWith("Bank rune not set: pick a rune inside your pack"),
                        "refused " + target.ToString("X") + ": " + f.ToolbagStatus);
                }
                s.ClientSends(Targeted(AssistFeatures.BankRuneCursor, Pinned));
                for (int i = 0; i < 200 && !f.ToolbagStatus.Contains("answered no label"); i++)
                    s.Clock.Sleep(100);
                Check.True(f.ToolbagStatus.Contains("answered no label"), "no label: " + f.ToolbagStatus);
                Check.Equal(0u, f.MineBankRuneSerial, "not set");
            }
            finally
            {
                f.MineBankRune = "";
            }
        });
    }

    static void CaveCursorRefusals()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Pinned);
            s.ServerSends(Packets.Contained(Other, 0x0E76, 1, Pack));
            s.Clock.Sleep(1);
            try
            {
                Check.True(f.MarkCave("PinRefuseCave", 5).StartsWith("OK"), "MarkCave");
                s.ClientSends(Targeted(AssistFeatures.CaveRuneCursor, Other));
                s.Clock.Sleep(50);
                Check.True(f.ToolbagStatus.StartsWith("Cave rune not set: pick a rune"), "no rune: " + f.ToolbagStatus);
                s.ClientSends(Targeted(AssistFeatures.CaveRuneCursor, Pinned));
                for (int i = 0; i < 200 && !f.ToolbagStatus.Contains("answered no label"); i++)
                    s.Clock.Sleep(100);
                Check.True(f.ToolbagStatus.Contains("answered no label"), "no label: " + f.ToolbagStatus);
                Check.Equal(0u, f.CaveRuneSerialOf("PinRefuseCave"), "not set");
            }
            finally
            {
                f.ForgetCave("PinRefuseCave");
                f.PickedCave = "";
            }
        });
    }

    static void OldCaveLine()
    {
        string data = RecallTests.DataFolder;
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            Check.True(f.SetCaveRune("NoSuchCave", "x").StartsWith("REFUSED no cave"), "unknown cave");
            Check.Equal(0u, f.CaveRuneSerialOf("NoSuchCave"), "no serial for an unknown cave");
            string file = "";
            foreach (string p in Directory.GetFiles(data, "caves-*"))
                file = p;
            string path = file.Length > 0 ? file : Path.Combine(data, "caves-test.txt");
            string[] before = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
            try
            {
                File.WriteAllLines(path,
                    new[] { string.Join("\t", "C", "OldCave", "100", "200", "0", "5", "Old Rune", "0", "0", "0", "0",
                    "0") });
                AssistFeatures again = new(new AssistLog(data, 4998), new AssistSettings(AssistRelay.SettingsPath),
                    s.Clock);
                try
                {
                    again.Attach(s.Link);
                    Check.Equal("Old Rune", again.CaveRuneOf("OldCave"), "loaded");
                    Check.Equal(0u, again.CaveRuneSerialOf("OldCave"), "no serial");
                }
                finally
                {
                    again.Shutdown();
                }
            }
            finally
            {
                File.WriteAllLines(path, before);
            }
        });
    }

    // AssistIdentity.md stage 4: recall runes 1F14..1F17; shovels 0F39 and 0F3A, never the pickaxes 0E85, 0E86;
    // forges 0FB1 and 197A..19A9 (blacksmith.m Q4ZM).
    static void KindsByGraphic()
    {
        foreach (int g in new[] { 0x1F14, 0x1F17 })
            Check.True(AssistFeatures.IsRuneArt(g), "rune " + g.ToString("X4"));
        foreach (int g in new[] { 0x1F13, 0x1F18 })
            Check.False(AssistFeatures.IsRuneArt(g), "not a rune " + g.ToString("X4"));
        foreach (int g in new[] { 0x0F39, 0x0F3A })
            Check.True(AssistFeatures.DigTool(g), "shovel " + g.ToString("X4"));
        foreach (int g in new[] { 0x0F38, 0x0F3B, 0x0E85, 0x0E86 })
            Check.False(AssistFeatures.DigTool(g), "no shovel " + g.ToString("X4"));
        foreach (int g in new[] { 0x0FB1, 0x197A, 0x19A9 })
            Check.True(AssistFeatures.IsForge(g), "forge " + g.ToString("X4"));
        foreach (int g in new[] { 0x0FB0, 0x0FB2, 0x1979, 0x19AA })
            Check.False(AssistFeatures.IsForge(g), "no forge " + g.ToString("X4"));
    }

    static void PinnedNotInPack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Other);
            Labels(s, "something else");
            Check.Equal(0u, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true, Pinned), "not taken");
        });
    }

    static void BankRuneSerial()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Pinned);
            try
            {
                f.MineBankRune = "minoc bank";
                Check.Equal(0u, f.MineBankRuneSerial, "by label: no serial");
                Labels(s, "Minoc Bank");
                Check.Null(f.BankRuneTarget(), "BankRuneTarget");
                s.ClientSends(Targeted(AssistFeatures.BankRuneCursor, Pinned));
                for (int i = 0; i < 100 && f.MineBankRuneSerial == 0; i++)
                    s.Clock.Sleep(100);
                Check.Equal(Pinned, f.MineBankRuneSerial, "by cursor: the rune's serial");
                Check.Equal("Minoc Bank", f.MineBankRune, "its label kept");
                f.MineBankRune = "britain bank";
                Check.Equal(0u, f.MineBankRuneSerial, "a label by name clears it");
            }
            finally
            {
                f.MineBankRune = "";
            }
        });
    }

    static void CaveRuneSerial()
    {
        string data = RecallTests.DataFolder;
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Pinned);
            try
            {
                Check.True(f.MarkCave("PinTestCave", 5).StartsWith("OK"), "MarkCave");
                Labels(s, "Home Cave");
                Check.Null(f.CaveRuneTarget(), "CaveRuneTarget");
                s.ClientSends(Targeted(AssistFeatures.CaveRuneCursor, Pinned));
                for (int i = 0; i < 100 && f.CaveRuneSerialOf("PinTestCave") == 0; i++)
                    s.Clock.Sleep(100);
                Check.Equal(Pinned, f.CaveRuneSerialOf("PinTestCave"), "the rune's serial");
                AssistFeatures again = new(new AssistLog(data, 4999), new AssistSettings(AssistRelay.SettingsPath),
                    s.Clock);
                try
                {
                    again.Attach(s.Link);
                    Check.Equal(Pinned, again.CaveRuneSerialOf("PinTestCave"), "kept across a restart");
                    Check.Equal("Home Cave", again.CaveRuneOf("PinTestCave"), "and its label");
                }
                finally
                {
                    again.Shutdown();
                }
                Check.True(f.SetCaveRune("PinTestCave", "home cave").Contains("rune home cave"), "by name");
                Check.Equal(0u, f.CaveRuneSerialOf("PinTestCave"), "a label by name clears it");
            }
            finally
            {
                f.ForgetCave("PinTestCave");
                f.PickedCave = "";
            }
        });
    }
}
