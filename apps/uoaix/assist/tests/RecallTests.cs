#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The recall (AssistFeatures.Recall) against a uo1998 that keeps its cast state, with the failures of 2026-10-22
// 06:41:45 to 06:42:11 replayed: the player's own click answered "You must wait" during the cast, and item labels in
// the journal.
public static class RecallTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Rune = 0x44DE6EA5;
    const uint Bag = 0x43DD0F1A;
    const int CursorAfterMs = 1500;
    static int s_port = 3000;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("recall: a cast answered by its cursor and a landing recalls on the first cast", CleanRecall),
        new TestCase("recall: the player's click answered \"You must wait\" mid-cast does not end the cast (06:41:45)",
        PlayersClickMidCast),
        new TestCase("recall: item labels in the journal are not the recall's answer (06:41:56)", LabelsAreNotAnswers),
        new TestCase("recall: the axe in hand goes to the pack before the cast (21:54:31)", AxePutAwayFirst),
        new TestCase("recall: \"Your hands must be free\" ends the recall at once, cast once (21:54:31)",
        HandsBarkEnds),
        new TestCase("journal: a hue 0x22 bark over the player is the server's line, the spell's words are the player's",
        BarkIsServers),
        new TestCase("recall: the axe's drop put back ends the recall before any cast", AxePutBack),
        new TestCase("recall: the axe the server keeps in a hand ends the recall before any cast", AxeStaysInHand));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // The features run on a scratch data folder, never the player's: AssistRelay.SettingsPath names the folder every
    // record file goes in (ServerFile). The folder is the run's own: named by the process id alone, a later run given a
    // reused id read an earlier run's kept rune labels, and PackRuneTests asked no label (2026-10-23, 58 of 631
    // folders).
    static readonly string s_data = Path.Combine(Path.GetTempPath(),
        "assist-tests-" + Environment.ProcessId + "-" + DateTime.UtcNow.Ticks);

    public static string DataFolder => s_data;

    public static void WithFeatures(Action<FakeShard, AssistFeatures> body)
    {
        string data = s_data;
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uoaix-assist");
        if (Path.GetFullPath(data).StartsWith(Path.GetFullPath(real), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("the test data folder is the player's own: " + data);
        Directory.CreateDirectory(data);
        AssistRelay.SettingsPath = Path.Combine(data, "settings.txt");
        GameLinkTests.WithLink(s =>
        {
            // A port of its own: AssistLog names its file by port and second.
            AssistFeatures f = new(new AssistLog(data, Interlocked.Increment(ref s_port)),
                new AssistSettings(AssistRelay.SettingsPath), s.Clock);
            try
            {
                f.Attach(s.Link);
                body(s, f);
            }
            finally
            {
                f.Shutdown();
            }
        });
    }

    static void Kwik(FakeShard s)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Pack));
        s.ServerSends(Packets.Contained(0x464FAC6C, 0x0F86, 30, Pack));
        s.ServerSends(Packets.Contained(0x464FABFC, 0x0F7B, 30, Pack));
        s.ServerSends(Packets.Contained(0x46B09470, 0x0F7A, 30, Pack));
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
    }

    // uo1998's cast: the words at once and the cursor CursorAfterMs later; a second cast while one waits for its target
    // is refused; the target lands the player at 2967,620; the player's own double-click is answered "You must wait".
    static void Shard(FakeShard s)
    {
        bool casting = false;
        s.Answer = p =>
        {
            if (p[0] == 0x12 && p[3] == 0x56)
            {
                if (casting)
                    return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                        "You are already casting a spell.", 0));
                casting = true;
                s.Also(CursorAfterMs, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
            }
            if (p[0] == 0x6C)
            {
                casting = false;
                return ImmutableArray.Create<byte[]>(Packets.Draw(2967, 620, 0, 0));
            }
            if (p[0] == 0x06)
                return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                    "You must wait to perform another action.", 0));
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static int Casts(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x12 && p[3] == 0x56 ? 1 : 0;
        return n;
    }

    static void CleanRecall()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            Shard(s);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(1, Casts(s), "casts");
            Check.Equal(2967, s.Link.State.X, "X");
        });
    }

    static void PlayersClickMidCast()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            Shard(s);
            s.Also(12, () => s.ClientSends(Packets.Use(Bag)));
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(1, Casts(s), "casts");
        });
    }

    // A label: the item's own serial, type 6, no name (AssistState puts it in the journal as bare text).
    static byte[] Label(uint item, string text)
    {
        byte[] p = Packets.Speech(item, "", text, 6);
        return p;
    }

    const uint Axe = 0x46E3F2E7;
    const int AxeArt = 0x0F43;
    const uint Book = 0x40000E1A;

    // uo1998's refusal of a cast with the hands full: barked over the player in hue 0x22 (spelskil.m Q49Q).
    static byte[] HandsBark() => Packets.Speech(Packets.Player, "Kwik", "Your hands must be free to cast spells.", 0,
        AssistState.BarkHue);

    // The shard of Shard, with what the hands hold: a drop into the pack puts the item there, and a cast while held
    // (other than the spellbook) is barked at instead of taken.
    static void HandsShard(FakeShard s, uint held)
    {
        bool inHand = true;
        bool casting = false;
        s.Answer = p =>
        {
            uint item = p.Length >= 5 ? (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]) : 0;
            if (p[0] == 0x08 && item == held)
            {
                inHand = false;
                return ImmutableArray.Create<byte[]>(Packets.Contained(held, AxeArt, 1, Pack));
            }
            if (p[0] == 0x12 && p[3] == 0x56)
            {
                if (inHand)
                    return ImmutableArray.Create<byte[]>(HandsBark());
                if (casting)
                    return ImmutableArray<byte[]>.Empty;
                casting = true;
                s.Also(CursorAfterMs, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0, 0x5A));
            }
            if (p[0] == 0x6C)
                return ImmutableArray.Create<byte[]>(Packets.Draw(2967, 620, 0, 0));
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static int FirstAt(FakeShard s, Func<byte[], bool> match)
    {
        ImmutableArray<byte[]> got = s.ServerGot;
        for (int i = 0; i < got.Length; i++)
        {
            if (match(got[i]))
                return i;
        }
        return -1;
    }

    static void AxePutAwayFirst()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Wear(Axe, AxeArt, 2, Packets.Player));
            s.Clock.Sleep(1);
            HandsShard(s, Axe);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(1, Casts(s), "casts");
            int drop = FirstAt(s, p => p[0] == 0x08);
            int cast = FirstAt(s, p => p[0] == 0x12 && p[3] == 0x56);
            Check.True(drop >= 0 && drop < cast, "the axe dropped before the cast");
            Check.Equal(2967, s.Link.State.X, "X");
        });
    }

    // The spellbook stays in hand (it is no bar to a cast); a bark answering the cast all the same ends the recall
    // without waiting out the move or casting again (L-FUTILITY).
    static void HandsBarkEnds()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Wear(Book, AssistFeatures.SpellbookArt, 1, Packets.Player));
            s.Clock.Sleep(1);
            HandsShard(s, Book);
            DateTime at = s.Clock.UtcNow;
            string? why = f.Recall(Rune);
            Check.Equal("the recall did not move you (\"Kwik: Your hands must be free to cast spells.\")", why,
                "Recall");
            Check.Equal(1, Casts(s), "casts");
            Check.Equal(-1, FirstAt(s, p => p[0] == 0x08), "the spellbook not dropped");
            Check.True((s.Clock.UtcNow - at).TotalMilliseconds < AssistFeatures.CastCursorMs,
                "ended before the cursor wait ran out");
        });
    }

    static void AxePutBack()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Wear(Axe, AxeArt, 2, Packets.Player));
            s.Clock.Sleep(1);
            Shard(s);
            string? why = f.Recall(Rune);
            Check.True(why is not null && why.StartsWith("the hands before the recall: ")
                && why.Contains("the server put it back where it lay"), "Recall: " + why);
            Check.Equal(0, Casts(s), "casts");
        });
    }

    static void AxeStaysInHand()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Wear(Axe, AxeArt, 2, Packets.Player));
            s.Clock.Sleep(1);
            Shard(s);
            Func<byte[], ImmutableArray<byte[]>> cast = s.Answer!;
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Wear(Axe, AxeArt, 1, Packets.Player))
                : cast(p);
            string? why = f.Recall(Rune);
            Check.Equal("the hands before the recall: the held item " + Axe.ToString("X") + " stayed in your hand",
                why, "Recall");
            Check.Equal(0, Casts(s), "casts");
        });
    }

    static void BarkIsServers()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            long mark = s.Link.State.JournalSeq;
            s.ServerSends(HandsBark());
            s.Clock.Sleep(50);
            Check.Equal(1, s.Link.State.ServerSince(mark).Count, "the bark is the server's");
            Check.False(s.Link.State.PlayerSpokeSince(mark), "the bark is not the player's words");
            long words = s.Link.State.JournalSeq;
            s.ServerSends(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0, 0x5A));
            s.Clock.Sleep(50);
            Check.Equal(0, s.Link.State.ServerSince(words).Count, "the words are not the server's");
            Check.True(s.Link.State.PlayerSpokeSince(words), "the words are the player's");
        });
    }

    static void LabelsAreNotAnswers()
    {
        WithFeatures((s, f) =>
        {
            Kwik(s);
            Shard(s);
            s.Also(12, () => s.ClientSends(Packets.Use(Bag)));
            // A label holding a word that ends a cast ("reagents"), shown while the cast waits for its cursor.
            s.Also(600, () => s.ServerSends(Label(0x43DD0F1A, "a bag of reagents")));
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(1, Casts(s), "casts");
        });
    }
}
