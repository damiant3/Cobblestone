#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

public static class GameLinkTests
{
    static readonly DateTime s_start = new(2026, 10, 22, 6, 41, 45, DateTimeKind.Utc);
    const uint Bag = 0x43DD0F1A;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("link: a use reaches the server as the client sends it", UseBytes),
        new TestCase("link: nothing goes out on a closed link", ClosedRefuses),
        new TestCase("link: nothing the assist starts goes out while the player is dead", DeadRefusesInject),
        new TestCase("link: a released client packet goes out while the player is dead", DeadReleases),
        new TestCase("link: nothing goes out once halted", HaltedRefuses),
        new TestCase("link: an opcode the assist never sends is refused", OpcodeRefused),
        new TestCase("link: a fixed packet 1 byte short is refused", FixedShort),
        new TestCase("link: a fixed packet 1 byte long is refused", FixedLong),
        new TestCase("link: a variable packet whose header disagrees is refused", VariableHeaderWrong),
        new TestCase("link: a variable packet of 2 bytes is refused", VariableTooShort),
        new TestCase("link: an unanswered action holds the next for 2999 ms", BusyAt2999),
        new TestCase("link: an unanswered action is let go at 3000 ms", ReleasedAt3000),
        new TestCase("link: the server's answer clears the action", AnswerClears),
        new TestCase("link: a look is not tracked", LookUntracked),
        new TestCase("link: a door's own redraw answers its use", DoorAnswers),
        new TestCase("link: a lift from the assist is marked the assist's", AssistLiftMarked));

    public static ImmutableArray<TestCase> Cases => s_cases;

    public static void WithLink(Action<FakeShard> body) => WithLink((s, c) => body(s), false);

    // The link on a SkewClock when skewed.
    public static void WithLink(Action<FakeShard, SkewClock?> body, bool skewed)
    {
        VirtualClock clock = new(s_start);
        SkewClock? skew = skewed ? new SkewClock(clock) : null;
        FakeShard shard = new(clock, skew);
        shard.Start();
        try
        {
            clock.Run(() => body(shard, skew));
        }
        finally
        {
            shard.Stop();
        }
    }

    static byte[] UsePacket(uint serial) => new byte[] { 0x06, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    static void UseBytes()
    {
        WithLink(s =>
        {
            Check.Null(s.Link.Use(Bag, false), "Use");
            Check.Equal(1, s.ServerGot.Length, "packets the server got");
            Check.Bytes(UsePacket(Bag), s.ServerGot[0], "the use");
            Check.Equal("use 43DD0F1A", s.Link.InFlight, "InFlight");
        });
    }

    static void ClosedRefuses()
    {
        WithLink(s =>
        {
            s.Link.Closed = true;
            Check.Equal("the connection is closed", s.Link.Use(Bag), "Use");
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
        });
    }

    static void DeadRefusesInject()
    {
        WithLink(s =>
        {
            s.Link.State.Dead = true;
            Check.Equal("the player is dead", s.Link.Use(Bag), "Use");
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
        });
    }

    static void DeadReleases()
    {
        WithLink(s =>
        {
            s.Link.State.Dead = true;
            Check.Null(s.Link.Release(UsePacket(Bag), "use by the player"), "Release");
            Check.Equal(1, s.ServerGot.Length, "packets the server got");
        });
    }

    static void HaltedRefuses()
    {
        WithLink(s =>
        {
            s.Link.Halted = "a test halt";
            Check.Equal("halted: a test halt", s.Link.Use(Bag), "Use");
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
        });
    }

    static void OpcodeRefused()
    {
        WithLink(s =>
        {
            Check.Equal("opcode 01 is not one the assist sends",
                s.Link.Inject(new byte[] { 0x01, 0, 0, 0, 0 }, "logout"), "Inject");
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
        });
    }

    static void FixedShort()
    {
        WithLink(s =>
        {
            Check.Equal("length 4 is not the client's for 06", s.Link.Inject(new byte[] { 0x06, 0, 0, 0 }, "use"),
                "Inject");
        });
    }

    static void FixedLong()
    {
        WithLink(s =>
        {
            Check.Equal("length 6 is not the client's for 06",
                s.Link.Inject(new byte[] { 0x06, 0, 0, 0, 1, 0 }, "use"), "Inject");
        });
    }

    static void VariableHeaderWrong()
    {
        WithLink(s =>
        {
            // 0x03 speech is variable: bytes 1-2 carry the length, here 9 for a packet of 10.
            byte[] p = new byte[10];
            p[0] = 0x03;
            p[2] = 9;
            Check.Equal("length 10 is not the client's for 03", s.Link.Inject(p, "say", false), "Inject");
        });
    }

    static void VariableTooShort()
    {
        WithLink(s =>
        {
            Check.Equal("length 2 is not the client's for 03", s.Link.Inject(new byte[] { 0x03, 0 }, "say", false),
                "Inject");
        });
    }

    static void BusyAt2999()
    {
        WithLink(s =>
        {
            Check.Null(s.Link.Use(Bag, false), "the first use");
            s.Clock.Sleep(GameLink.AnswerMs - 1);
            Check.Equal("busy: use 43DD0F1A is unanswered", s.Link.Use(Bag, false), "the second use");
            Check.Equal(1, s.ServerGot.Length, "packets the server got");
        });
    }

    static void ReleasedAt3000()
    {
        WithLink(s =>
        {
            Check.Null(s.Link.Use(Bag, false), "the first use");
            s.Clock.Sleep(GameLink.AnswerMs);
            Check.Null(s.Link.Use(Bag, false), "the second use");
            Check.Equal(2, s.ServerGot.Length, "packets the server got");
            Check.True(s.Log.Lines.Contains("c1 -- UNANSWERED use 43DD0F1A after 3000 ms"), "the UNANSWERED line");
        });
    }

    // 0x24: the container's gump, the server's answer to a bag's use.
    static byte[] Gump(uint serial) => new byte[] { 0x24, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial, 0x00, 0x3C };

    static void AnswerClears()
    {
        WithLink(s =>
        {
            s.Answer = p => p[0] == 0x06 ? ImmutableArray.Create<byte[]>(Gump(Bag)) : ImmutableArray<byte[]>.Empty;
            Check.Null(s.Link.Use(Bag, false), "Use");
            s.Clock.Sleep(1);
            Check.Null(s.Link.InFlight, "InFlight after the answer");
            Check.True(s.Log.Lines.Contains("c1 -- ANSWERED use 43DD0F1A by 24"), "the ANSWERED line");
        });
    }

    static void LookUntracked()
    {
        WithLink(s =>
        {
            Check.Null(s.Link.Look(Bag), "Look");
            Check.Null(s.Link.InFlight, "InFlight");
            Check.Bytes(new byte[] { 0x09, 0x43, 0xDD, 0x0F, 0x1A }, s.ServerGot[0], "the look");
        });
    }

    static void DoorAnswers()
    {
        WithLink(s =>
        {
            const uint door = 0x40001A90;
            // 0x1A world item: length, the serial (high bit: an amount follows), graphic, amount, x, y, z.
            byte[] redraw = new byte[] { 0x1A, 0x00, 0x10, 0xC0, 0x00, 0x1A, 0x90, 0x06, 0x75, 0x00, 0x01, 0x05, 0xC4,
                0x06, 0x84, 0x00 };
            s.Answer = p => p[0] == 0x06 ? ImmutableArray.Create<byte[]>(redraw) : ImmutableArray<byte[]>.Empty;
            Check.Null(s.Link.Use(door, false), "Use");
            s.Clock.Sleep(1);
            Check.Null(s.Link.InFlight, "InFlight after the redraw");
        });
    }

    static void AssistLiftMarked()
    {
        WithLink(s =>
        {
            Check.Null(s.Link.Lift(Bag, 1), "Lift");
            Check.True(s.Link.AssistLift, "AssistLift after the assist's lift");
        });
    }
}
