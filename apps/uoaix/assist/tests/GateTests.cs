#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The server's busy rule held at the send layer (GameLink.ActionGapMs): a double-click or a lift goes no sooner than
// ActionGapMs after the last one either side sent.
public static class GateTests
{
    static readonly DateTime s_start = new(2026, 10, 22, 6, 41, 45, DateTimeKind.Utc);
    const uint Bag = 0x43DD0F1A;
    const uint Other = 0x46C22C1D;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("gate: the gap is the measured 1100 ms, past the server's 1000 and its jitter", GapIsMeasured),
        new TestCase("gate: the first action goes at once", FirstAtOnce),
        new TestCase("gate: an action asked 1099 ms after the last goes at 1100", AskedAt1099),
        new TestCase("gate: an action asked 1100 ms after the last goes at once", AskedAt1100),
        new TestCase("gate: the bank bags opened 854 ms apart at 06:43:49 now open 1100 apart", MorningBagOpens),
        new TestCase("gate: the client's own double-click starts the gap", ClientDoubleClickCounts),
        new TestCase("gate: the client's own lift starts the gap", ClientLiftCounts),
        new TestCase("gate: the assist's lift starts the gap", AssistLiftCounts),
        new TestCase("gate: a look is not held", LookNotHeld),
        new TestCase("gate: a drop is not held", DropNotHeld),
        new TestCase("gate: the client acting during the wait moves it", ClientActsDuringWait),
        new TestCase("gate: an action waits for a half-sent client packet, then the gap", HalfSentClient),
        new TestCase("gate: a link closed during the wait refuses", ClosedDuringWait),
        new TestCase("gate: the player dying during the wait refuses", DeadDuringWait),
        new TestCase("gate: the fail-safe tripping during the wait refuses", FailSafeDuringWait),
        new TestCase("gate: a released client action is held as well", ReleaseHeld),
        new TestCase("gate: a hold handler releasing in the client's place sends at once, as the client would have",
        ReleaseInPlace));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static double At(FakeShard s, int i) => (s.ServerGotAt[i] - s_start).TotalMilliseconds;

    static double Last(FakeShard s) => At(s, s.ServerGotAt.Length - 1);

    // The server answers every double-click with its container's gump, so the action is not left in flight.
    static void Answering(FakeShard s)
    {
        s.Answer = p => p[0] == 0x06
            ? ImmutableArray.Create<byte[]>(new byte[] { 0x24, p[1], p[2], p[3], p[4], 0x00, 0x3C })
            : ImmutableArray<byte[]>.Empty;
    }

    // A change to the gap is a change to a measured value (GameLink.ActionGapMs): the measurement is redone first.
    static void GapIsMeasured()
    {
        Check.Equal(1100, GameLink.ActionGapMs, "ActionGapMs");
    }

    static void FirstAtOnce()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Use(Bag, false), "Use");
            Check.Equal(0.0, Last(s), "ms the use went at");
        });
    }

    static void SecondAfter(int askedMs, double expectedMs)
    {
        GameLinkTests.WithLink(s =>
        {
            Answering(s);
            Check.Null(s.Link.Use(Bag, false), "the first use");
            s.Clock.Sleep(askedMs);
            Check.Null(s.Link.Use(Other, false), "the second use");
            Check.Equal(expectedMs, Last(s), "ms the second use went at");
        });
    }

    static void AskedAt1099() => SecondAfter(GameLink.ActionGapMs - 1, GameLink.ActionGapMs);

    static void AskedAt1100() => SecondAfter(GameLink.ActionGapMs, GameLink.ActionGapMs);

    static void MorningBagOpens() => SecondAfter(854, GameLink.ActionGapMs);

    static void ClientDoubleClickCounts()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(Packets.Use(Other));
            s.Clock.Sleep(1);
            Check.Null(s.Link.Use(Bag, false), "the assist's use");
            Check.Equal((double)GameLink.ActionGapMs, Last(s), "ms the assist's use went at");
        });
    }

    static void ClientLiftCounts()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(Packets.Lift(Other, 1));
            s.Clock.Sleep(1);
            Check.Null(s.Link.Use(Bag, false), "the assist's use");
            Check.Equal((double)GameLink.ActionGapMs, Last(s), "ms the assist's use went at");
        });
    }

    static void AssistLiftCounts()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Lift(Other, 1), "the lift");
            s.Link.Settle();
            Check.Null(s.Link.Use(Bag, false), "the use");
            Check.Equal((double)GameLink.ActionGapMs, Last(s), "ms the use went at");
        });
    }

    static void LookNotHeld()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(Packets.Use(Other));
            s.Clock.Sleep(1);
            Check.Null(s.Link.Look(Bag), "the look");
            Check.Equal(1.0, Last(s), "ms the look went at");
        });
    }

    static void DropNotHeld()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Lift(Other, 1), "the lift");
            s.Link.Settle();
            Check.Null(s.Link.Drop(Other, 0x42BE4F7C), "the drop");
            Check.Equal(0.0, Last(s), "ms the drop went at");
        });
    }

    static void ClientActsDuringWait()
    {
        GameLinkTests.WithLink(s =>
        {
            Answering(s);
            s.Link.Use(Bag, false);
            s.Clock.Sleep(1);
            Thread client = s.Also(499, () => s.ClientSends(Packets.Use(0x11111111)));
            Check.Null(s.Link.Use(Other, false), "the assist's second use");
            s.Joined(client);
            Check.Equal(500.0, At(s, 1), "ms the client's use went at");
            Check.Equal(500.0 + GameLink.ActionGapMs, Last(s), "ms the assist's second use went at");
        });
    }

    static void HalfSentClient()
    {
        GameLinkTests.WithLink(s =>
        {
            byte[] mine = Packets.Use(0x11111111);
            s.ClientSends(mine, 0, 3);
            s.Clock.Sleep(1);
            Thread client = s.Also(299, () => s.ClientSends(mine, 3, 2));
            Check.Null(s.Link.Use(Bag, false), "the assist's use");
            s.Joined(client);
            Check.Equal(300.0, At(s, 0), "ms the client's use went at");
            Check.Equal(300.0 + GameLink.ActionGapMs, Last(s), "ms the assist's use went at");
        });
    }

    static void RefusedDuringWait(Action<FakeShard> change, string expected)
    {
        GameLinkTests.WithLink(s =>
        {
            Answering(s);
            s.Link.Use(Bag, false);
            s.Clock.Sleep(1);
            Thread other = s.Also(499, () => change(s));
            Check.Equal(expected, s.Link.Use(Other, false), "the second use");
            s.Joined(other);
            Check.Equal(1, s.ServerGot.Length, "packets on the wire");
        });
    }

    static void ClosedDuringWait() => RefusedDuringWait(s => s.Link.Closed = true, "the connection is closed");

    static void DeadDuringWait() => RefusedDuringWait(s => s.Link.State.Dead = true, "the player is dead");

    static void FailSafeDuringWait() => RefusedDuringWait(s => s.Disabled = "the server refused assist actions",
        "assist disabled by the fail-safe: the server refused assist actions");

    // AssistFeatures.HoldLoginSelfUse: the client's double-click on itself goes out as a paperdoll request, released
    // from inside the hold on the relay's client thread.
    static void ReleaseInPlace()
    {
        GameLinkTests.WithLink(s =>
        {
            Answering(s);
            s.Link.Use(Bag, false);
            s.Clock.Sleep(1);
            s.Link.HoldClient = p =>
            {
                if (p[0] != 0x06 || p[1] != 0x02)
                    return false;
                s.Link.Release(Packets.Use(0x82BE4F79), "login paperdoll", false);
                return true;
            };
            s.ClientSends(Packets.Use(Packets.Player));
            s.Clock.Sleep(1);
            Check.Equal("0682BE4F79", UoHex.Of(s.ServerGot[s.ServerGot.Length - 1]), "the paperdoll request");
            Check.Equal(1.0, Last(s), "ms it went at");
        });
    }

    static void ReleaseHeld()
    {
        GameLinkTests.WithLink(s =>
        {
            Answering(s);
            s.Link.Use(Bag, false);
            s.Clock.Sleep(1);
            Check.Null(s.Link.Release(Packets.Use(Other), "use by the player", false), "Release");
            Check.Equal((double)GameLink.ActionGapMs, Last(s), "ms the released use went at");
        });
    }
}
