#nullable enable
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text;

namespace Uoaix.Assist.Tests;

// GameLink's two relay threads and every packet it builds, against the fake shard.
public static class GameLinkFlowTests
{
    const uint Bag = 0x43DD0F1A;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("relay up: a client packet reaches the server unchanged", ClientRelayed),
        new TestCase("relay up: a held client packet goes nowhere", ClientHeld),
        new TestCase("relay up: a watcher that throws is logged and the next still runs", ClientSubscriberFails),
        new TestCase("relay up: every password field is masked in the log, not on the wire", PasswordsMasked),
        new TestCase("relay up: the client's lift takes the lift back from the assist", ClientLiftClears),
        new TestCase("relay up: a client step is numbered by the relay and its answer numbered back",
        ClientStepRenumbered),
        new TestCase("relay up: step numbers run 0 to 255, then from 1", StepNumbersWrap),
        new TestCase("relay up: an injection waits behind a half-sent client packet", InjectionWaitsForClient),
        new TestCase("relay up: an unframed client packet halts the assist and passes the rest re-encrypted",
        ClientUnframed),
        new TestCase("relay up: a reset client writes a crash line and ends the link", ClientReset),
        new TestCase("relay up: a closed client ends the link", ClientClosed),
        new TestCase("relay up: a server that refuses writes ends the link, blamed on the server", ServerWriteFails),
        new TestCase("relay down: a server packet reaches the client unchanged", ServerPacketShown),
        new TestCase("relay down: an assist step's answer is kept from the client, who is shown the player",
        AssistStepHidden),
        new TestCase("relay down: show mode 0 shows nothing after an assist step", AssistStepShowModeZero),
        new TestCase("relay down: the refusal of an assist step is the assist's", AssistStepRefused),
        new TestCase("relay down: the refusal of a client step is numbered back to the client", ClientStepRefused),
        new TestCase("relay down: a refusal of no known step is counted the assist's", UnknownStepRefused),
        new TestCase("relay down: an answer to no known step passes as it came", UnknownStepAck),
        new TestCase("relay down: the player redrawn starts the numbering over", DrawRestartsNumbering),
        new TestCase("relay down: a claimed cursor is the assist's and not shown", CursorClaimed),
        new TestCase("relay down: a use's claim still holds at 3000 ms", ClaimHoldsAt3000),
        new TestCase("relay down: a use's claim is gone at 3001 ms and the cursor goes to the client",
        ClaimExpiresAt3001),
        new TestCase("relay down: a cast's claim still holds at 8000 ms", CastClaimHoldsAt8000),
        new TestCase("relay down: a claimed menu is not shown", MenuClaimed),
        new TestCase("relay down: an unclaimed menu is shown", MenuShown),
        new TestCase("relay down: a held server packet still reaches the watchers", ServerHeld),
        new TestCase("relay down: a watcher that throws is logged", ServerSubscriberFails),
        new TestCase("relay: a pass held 300 ms or more is logged RELAY SLOW with where the time went, either way; "
        + "299 ms is not", RelaySlow),
        new TestCase("relay down: another speaker's line does not answer an action", OtherSpeakerNoAnswer),
        new TestCase("relay down: the player's own line answers an action", PlayerLineAnswers),
        new TestCase("relay down: a unit past the limit undecoded halts and passes the rest raw", ServerUnframed),
        new TestCase("relay down: a closed server ends the link", ServerClosed),
        new TestCase("send: a skill is the client's skill-gump use", SkillBytes),
        new TestCase("send: a cast is the client's spellbook cast", CastBytes),
        new TestCase("send: a line is typed as the client types it", SayBytes),
        new TestCase("send: the same unanswered line is refused at 119999 ms", SayRefusedBefore120s),
        new TestCase("send: the same unanswered line goes again at 120000 ms", SayAllowedAt120s),
        new TestCase("send: a move of 3 tiles allows the line again", SayMovedThree),
        new TestCase("send: a move of 2 tiles does not", SayMovedTwo),
        new TestCase("send: a line heard may be said again", SayHeard),
        new TestCase("send: hearing another line changes nothing", SayHeardOther),
        new TestCase("send: a forgotten line may be said again", SayForgotten),
        new TestCase("send: a refused line is not counted as said", SayRefusedNotCounted),
        new TestCase("send: a buy, a sale, a status request, drops and an equip are built byte for byte", OtherBytes),
        new TestCase("send: settling lets the action go", SettleClears),
        new TestCase("target: refused with no assist cursor", TargetNoCursor),
        new TestCase("target: an object echoes the cursor's id and flags", TargetObject),
        new TestCase("target: the ground carries no serial", TargetGround),
        new TestCase("client: a packet for the client only reaches it and is logged", ToClientBytes),
        new TestCase("client: a journal line of 200 characters is kept whole", TellAt200),
        new TestCase("client: a journal line of 201 characters is cut to 200", TellAt201),
        new TestCase("client: a step for the client to walk", MoveClientBytes),
        new TestCase("client: nothing reaches a closed client", ClosedClientRefuses),
        new TestCase("show: the player cannot be shown before the server drew it", ShowBeforeDraw),
        new TestCase("show: show mode 0 shows nothing until the walk ends", ShowModeZero),
        new TestCase("show: show mode 0x77 moves the player as a mobile", ShowMode77),
        new TestCase("show: a step drops the hidden flag from the drawing", StepUnhides),
        new TestCase("edge: a second desync keeps the first halt's reason", HaltOnce),
        new TestCase("edge: a hold that declines lets the client packet through", ClientHoldDeclines),
        new TestCase("edge: a reset with another message writes no crash line", ResetOtherMessage),
        new TestCase("edge: closes that throw still end the link", CloseThrows),
        new TestCase("edge: an injection queued behind a client packet that unframes is never sent",
        QueuedDroppedOnUnframe),
        new TestCase("edge: a server packet the framer refuses halts mid-read and passes the rest raw",
        ServerFramerDesync),
        new TestCase("edge: a lift in flight is not answered by another's line", LiftNotAnswered),
        new TestCase("edge: another item's redraw does not answer a door's use", OtherItemRedraw),
        new TestCase("edge: a redraw of 6 bytes does not answer a door's use", ShortRedrawSix),
        new TestCase("edge: a redraw of 7 bytes answers a door's use", ShortRedrawSeven),
        new TestCase("edge: a line of 6 bytes answers an action", ShortLineSix),
        new TestCase("edge: a line of 7 bytes from another does not", ShortLineSeven),
        new TestCase("edge: a server hold that declines shows the packet", ServerHoldDeclines),
        new TestCase("edge: a system line answers an action", SystemLineAnswers),
        new TestCase("edge: nothing goes out while the fail-safe has the assist disabled", FailSafeRefuses),
        new TestCase("edge: a skills request is tracked as one", SkillsRequest),
        new TestCase("edge: a cursor the client answered cannot be targeted", TargetAfterClientAnswered),
        new TestCase("edge: a refused target keeps the cursor the assist's", TargetRefusedKeepsCursor),
        new TestCase("edge: another mobile's redraw keeps the step numbering", OtherMobileDraw),
        new TestCase("edge: a closed link cannot show the player", ShowClosed),
        new TestCase("edge: an unframed server stream cannot show the player", ShowRaw),
        new TestCase("edge: an assist step answered before any drawing shows nothing", StepBeforeDraw),
        new TestCase("edge: show mode 0x77 draws the player whole at the end", ShowMode77Final),
        new TestCase("relay down: a reset server ends the link, blamed on the server", ServerReset),
        new TestCase("relay down: a client that refuses writes ends the link, blamed on the client", ClientWriteFails));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Settle(FakeShard s) => s.Clock.Sleep(1);

    static void Login(FakeShard s)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        Settle(s);
    }

    static bool Logged(FakeShard s, string line) => s.Log.Lines.Contains(line);

    static bool ClientGotHex(FakeShard s, string hex)
    {
        foreach (byte[] p in s.ClientGot)
        {
            if (UoHex.Of(p) == hex)
                return true;
        }
        return false;
    }

    static byte[] LastClientGot(FakeShard s, byte op)
    {
        ImmutableArray<byte[]> got = s.ClientGot;
        for (int i = got.Length - 1; i >= 0; i--)
        {
            if (got[i][0] == op)
                return got[i];
        }
        throw new CheckFailed("the client got no " + op.ToString("X2"));
    }

    static void ClientRelayed()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            Check.Bytes(Packets.Use(Bag), s.ServerGot[0], "the server's packet");
            Check.True(Logged(s, "c1 C>S 0643DD0F1A"), "the C>S line");
        });
    }

    static void ClientHeld()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.HoldClient = p => p[0] == 0x06;
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
            Check.True(Logged(s, "c1 C>S 0643DD0F1A (held by the assist)"), "the held line");
        });
    }

    static void ClientSubscriberFails()
    {
        GameLinkTests.WithLink(s =>
        {
            bool second = false;
            s.Link.ClientPacket += p => throw new FormatException("boom");
            s.Link.ClientPacket += p => second = true;
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            Check.True(Logged(s, "c1 -- C>S SUBSCRIBER FAILED on 06: FormatException boom"), "the failure line");
            Check.True(second, "the second watcher ran");
        });
    }

    static void PasswordsMasked()
    {
        (int Op, int From)[] fields = { (0x80, 31), (0x91, 35), (0x5D, 35), (0x83, 1) };
        foreach ((int op, int from) in fields)
        {
            GameLinkTests.WithLink(s =>
            {
                int length = FakeShard.Wire.Lengths[op];
                byte[] p = Packets.Filled(op, length);
                s.ClientSends(p);
                Settle(s);
                byte[] masked = (byte[])p.Clone();
                for (int i = from; i < from + 30 && i < masked.Length; i++)
                    masked[i] = 0;
                Check.True(Logged(s, "c1 C>S " + UoHex.Of(masked)), "the masked line for " + op.ToString("X2"));
                Check.Bytes(p, s.ServerGot[0], "the packet on the wire for " + op.ToString("X2"));
            });
        }
    }

    static void ClientLiftClears()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Lift(Bag, 1), "the assist's lift");
            s.Link.Settle();
            s.ClientSends(Packets.Lift(Bag, 1));
            Settle(s);
            Check.False(s.Link.AssistLift, "AssistLift after the client's lift");
        });
    }

    static void ClientStepRenumbered()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ClientSends(Packets.Step(2, 7));
            Settle(s);
            Check.Bytes(Packets.Step(2, 0), s.ServerGot[s.ServerGot.Length - 1], "the step on the wire");
            s.ServerSends(Packets.StepAck(0));
            Settle(s);
            Check.True(ClientGotHex(s, "220701"), "the answer numbered back to 7");
            Check.Equal(1, s.Link.ClientStepAcks, "ClientStepAcks");
        });
    }

    static void StepNumbersWrap()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            for (int i = 0; i < 257; i++)
            {
                s.ClientSends(Packets.Step(2, i & 0xFF));
                Settle(s);
            }
            ImmutableArray<byte[]> got = s.ServerGot;
            Check.Equal(255, (int)got[255][2], "the 256th step's number");
            Check.Equal(1, (int)got[256][2], "the 257th step's number");
        });
    }

    static void InjectionWaitsForClient()
    {
        GameLinkTests.WithLink(s =>
        {
            byte[] mine = Packets.Use(0x11111111);
            s.ClientSends(mine, 0, 3);
            Settle(s);
            Check.Null(s.Link.Look(Bag), "the assist's look");
            Check.Equal(0, s.ServerGot.Length, "packets on the wire while the client's is half sent");
            s.ClientSends(mine, 3, 2);
            Settle(s);
            Check.Equal(2, s.ServerGot.Length, "packets on the wire");
            Check.Bytes(mine, s.ServerGot[0], "the client's first");
            Check.Equal("0943DD0F1A", UoHex.Of(s.ServerGot[1]), "the assist's after");
        });
    }

    static void ClientUnframed()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(new byte[] { 0xFF, 1, 2 });
            Settle(s);
            Check.True(Logged(s,
                "c1 -- HALT client packet unframed: opcode FF past the length table; client bytes now pass re-encrypted"),
                "the HALT line");
            int before = s.ServerBytes;
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            Check.Equal(before + 5, s.ServerBytes, "bytes on the wire after the halt");
            Check.Equal("halted: client packet unframed: opcode FF past the length table; client bytes now pass "
                + "re-encrypted", s.Link.Use(Bag), "a use after the halt");
        });
    }

    static void ClientReset()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ResetClient("An existing connection was forcibly closed by the remote host.");
            s.WaitEnded();
            Check.True(s.Link.Closed, "Closed");
            Check.True(Logged(s, "c1 -- ENDED BY CLIENT"), "the END line");
            Check.True(Logged(s, "c1 -- C>S END An existing connection was forcibly closed by the remote host."),
                "the C>S END line");
            Check.True(Logged(s, "c1 -- CRASH FILE not written"), "the CRASH FILE line");
        });
    }

    static void ClientClosed()
    {
        GameLinkTests.WithLink(s =>
        {
            s.CloseClient();
            s.WaitEnded();
            Check.True(s.Link.Closed, "Closed");
            Check.True(Logged(s, "c1 -- ENDED BY CLIENT"), "the END line");
        });
    }

    static void ServerWriteFails()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ServerRefusesWrites = true;
            s.ClientSends(Packets.Use(Bag));
            s.WaitEnded();
            Check.True(s.Link.Closed, "Closed");
            Check.True(Logged(s, "c1 -- ENDED BY SERVER"), "the END line");
            Check.True(Logged(s, "c1 -- C>S END the server's socket is gone"), "the C>S END line");
        });
    }

    static void ServerPacketShown()
    {
        GameLinkTests.WithLink(s =>
        {
            byte[] line = Packets.Speech(0xFFFFFFFF, "", "Welcome", 0);
            s.ServerSends(line);
            Settle(s);
            Check.True(ClientGotHex(s, UoHex.Of(line)), "the client got the line");
            Check.True(Logged(s, "c1 S>C " + UoHex.Of(line)), "the S>C line");
        });
    }

    static void AssistStepHidden()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            int draws = 0;
            foreach (byte[] p in s.ClientGot)
                draws += p[0] == 0x20 ? 1 : 0;
            Check.Null(s.Link.Step(2), "Step");
            s.ServerSends(Packets.StepAck(0));
            Settle(s);
            Check.Equal(1, s.Link.StepAcks, "StepAcks");
            Check.False(ClientGotHex(s, "220001"), "the client got the assist's answer");
            int after = 0;
            foreach (byte[] p in s.ClientGot)
                after += p[0] == 0x20 ? 1 : 0;
            Check.Equal(draws + 1, after, "draws shown to the client");
            Check.True(Logged(s, "c1 S>C 220001 (assist step)"), "the assist step line");
        });
    }

    static void AssistStepShowModeZero()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.Link.ShowMode = 0;
            int before = s.ClientGot.Length;
            s.Link.Step(2);
            s.ServerSends(Packets.StepAck(0));
            Settle(s);
            Check.Equal(before, s.ClientGot.Length, "packets the client got");
        });
    }

    static void AssistStepRefused()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Step(2);
            s.ServerSends(Packets.StepRefused(0, 1000, 1000));
            Settle(s);
            Check.Equal(1, s.Link.StepRefusals, "StepRefusals");
            Check.Equal(0, s.Link.ClientStepRefusals, "ClientStepRefusals");
            Check.Equal(0, (int)LastClientGot(s, 0x21)[1], "the refusal's number to the client");
        });
    }

    static void ClientStepRefused()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ClientSends(Packets.Step(2, 9));
            Settle(s);
            s.ServerSends(Packets.StepRefused(0, 1000, 1000));
            Settle(s);
            Check.Equal(1, s.Link.ClientStepRefusals, "ClientStepRefusals");
            Check.Equal(9, (int)LastClientGot(s, 0x21)[1], "the refusal numbered back");
        });
    }

    static void UnknownStepRefused()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.StepRefused(5, 1000, 1000));
            Settle(s);
            Check.Equal(1, s.Link.StepRefusals, "StepRefusals");
            Check.Equal(0, s.Link.ClientStepRefusals, "ClientStepRefusals");
        });
    }

    static void UnknownStepAck()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.StepAck(9));
            Settle(s);
            Check.True(ClientGotHex(s, "220901"), "the answer as it came");
            Check.Equal(0, s.Link.StepAcks + s.Link.ClientStepAcks, "acks counted");
        });
    }

    static void DrawRestartsNumbering()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ClientSends(Packets.Step(2, 4));
            Settle(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.ClientSends(Packets.Step(2, 5));
            Settle(s);
            Check.Equal(0, (int)s.ServerGot[s.ServerGot.Length - 1][2], "the number after the redraw");
        });
    }

    static void CursorAfter(FakeShard s, int waitMs, bool cast)
    {
        Login(s);
        if (cast)
            Check.Null(s.Link.Cast(32), "Cast");
        else
            Check.Null(s.Link.Use(Bag), "Use");
        s.Clock.Sleep(waitMs);
        s.ServerSends(Packets.Cursor(0x1234, 0x03));
        Settle(s);
    }

    static void CursorClaimed()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, 0, false);
            Check.True(s.Link.MacroCursor, "MacroCursor");
            Check.Equal(3, s.Link.CursorFlags, "CursorFlags");
            Check.False(ClientGotHex(s, UoHex.Of(Packets.Cursor(0x1234, 0x03))), "the client got the cursor");
            Check.True(Logged(s, "c1 S>C " + UoHex.Of(Packets.Cursor(0x1234, 0x03)) + " (assist cursor, not shown)"),
                "the cursor line");
        });
    }

    static void ClaimHoldsAt3000()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, GameLink.ClaimMs, false);
            Check.True(s.Link.MacroCursor, "MacroCursor");
        });
    }

    static void ClaimExpiresAt3001()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, GameLink.ClaimMs + 1, false);
            Check.False(s.Link.MacroCursor, "MacroCursor");
            Check.True(ClientGotHex(s, UoHex.Of(Packets.Cursor(0x1234, 0x03))), "the client got the cursor");
        });
    }

    static void CastClaimHoldsAt8000()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, GameLink.CastClaimMs, true);
            Check.True(s.Link.MacroCursor, "MacroCursor");
        });
    }

    static void MenuClaimed()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.ClaimMenu = true;
            s.ServerSends(Packets.Menu(Bag));
            Settle(s);
            Check.False(ClientGotHex(s, UoHex.Of(Packets.Menu(Bag))), "the client got the menu");
            Check.True(Logged(s, "c1 S>C " + UoHex.Of(Packets.Menu(Bag)) + " (assist menu, not shown)"),
                "the menu line");
        });
    }

    static void MenuShown()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ServerSends(Packets.Menu(Bag));
            Settle(s);
            Check.True(ClientGotHex(s, UoHex.Of(Packets.Menu(Bag))), "the client got the menu");
        });
    }

    static void ServerHeld()
    {
        GameLinkTests.WithLink(s =>
        {
            bool seen = false;
            byte[] line = Packets.Speech(0xFFFFFFFF, "", "held", 0);
            s.Link.HoldServer = p => p[0] == 0x1C;
            s.Link.ServerPacket += p => seen |= p[0] == 0x1C;
            s.ServerSends(line);
            Settle(s);
            Check.True(seen, "a watcher saw it");
            Check.False(ClientGotHex(s, UoHex.Of(line)), "the client got it");
            Check.True(Logged(s, "c1 S>C " + UoHex.Of(line) + " (held by the assist)"), "the held line");
        });
    }

    static void ServerSubscriberFails()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.ServerPacket += p => throw new FormatException("bang");
            s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "x", 0));
            Settle(s);
            Check.True(Logged(s, "c1 -- S>C SUBSCRIBER FAILED on 1C: FormatException bang"), "the failure line");
        });
    }

    static string? SlowLine(FakeShard s)
    {
        foreach (string l in s.Log.Lines)
        {
            if (l.Contains("RELAY SLOW"))
                return l;
        }
        return null;
    }

    static void RelaySlow()
    {
        byte[] line = Packets.Speech(0xFFFFFFFF, "", "slow", 0);
        GameLinkTests.WithLink((s, c) =>
        {
            s.Link.ServerPacket += p => { };
            s.Link.ServerPacket += p => c!.Skew(400);
            s.ServerSends(line);
            Settle(s);
            string? l = SlowLine(s);
            Check.True(l is not null
                && l.StartsWith("c1 -- RELAY SLOW S>C 400 ms for 1 packets: state lock 0 ms, watchers 400 ms (slowest "),
                "the line: " + l);
            Check.True(l is not null && l.Contains("RelaySlow")
                && l.Contains(" on 1C, 400 ms), holds 0 ms, waiting on the client side's lock 0 ms, socket write 0 "
                + "ms, the rest (the log, decoding) 0 ms"), "the slowest named: " + l);
            Check.True(ClientGotHex(s, UoHex.Of(line)), "the client got it");
        }, true);
        GameLinkTests.WithLink((s, c) =>
        {
            s.Link.ServerPacket += p => c!.Skew(299);
            s.ServerSends(line);
            Settle(s);
            Check.Null(SlowLine(s), "299 ms is no slow pass");
        }, true);
        GameLinkTests.WithLink((s, c) =>
        {
            s.Link.HoldClient = p => { c!.Skew(500); return false; };
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            string? l = SlowLine(s);
            Check.True(l is not null
                && l.StartsWith("c1 -- RELAY SLOW C>S 500 ms for 1 packets: state lock 0 ms, watchers 0 ms, holds 500 "
                + "ms, waiting on the server side's lock 0 ms"), "the client's line: " + l);
            Check.Bytes(Packets.Use(Bag), s.ServerGot[0], "the server got it");
        }, true);
    }

    static void OtherSpeakerNoAnswer()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Use(Bag, false);
            s.ServerSends(Packets.Speech(0x0000ABCD, "Morena", "Hello", 0));
            Settle(s);
            Check.Equal("use 43DD0F1A", s.Link.InFlight, "InFlight");
        });
    }

    static void PlayerLineAnswers()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Use(Bag, false);
            s.ServerSends(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
            Settle(s);
            Check.Null(s.Link.InFlight, "InFlight");
        });
    }

    static void ServerUnframed()
    {
        GameLinkTests.WithLink(s =>
        {
            const int Limit = 65536 * 11 / 8 + 2;
            byte[] unended = s.Unended(Limit * 8 / FakeShard.Wire.Book[0] + 64);
            s.ServerSendsRaw(unended);
            Settle(s);
            Check.True(Logged(s,
                "c1 -- HALT server packet unframed: a server unit past " + Limit
                + " bytes undecoded; server bytes now pass raw"), "the HALT line");
            int before = s.ClientBytes;
            s.ServerSendsRaw(new byte[] { 1, 2, 3 });
            Settle(s);
            Check.Equal(before + 3, s.ClientBytes, "raw bytes to the client");
            Check.Equal("server stream unframed", s.Link.ToClient(new byte[] { 0x97, 1 }, "step"), "ToClient");
            Check.Equal("server stream unframed", s.Link.Tell("x"), "Tell");
            Check.Equal("server stream unframed", s.Link.MoveClient(1), "MoveClient");
        });
    }

    static void ServerClosed()
    {
        GameLinkTests.WithLink(s =>
        {
            s.CloseServer();
            s.WaitEnded();
            Check.True(s.Link.Closed, "Closed");
            Check.True(Logged(s, "c1 -- ENDED BY SERVER"), "the END line");
        });
    }

    static void SkillBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Skill(25, false), "Skill");
            Check.Equal("120009243235203000", UoHex.Of(s.ServerGot[0]), "the skill");
        });
    }

    static void CastBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Cast(32), "Cast");
            Check.Equal("1200075633320000".Substring(0, 14), UoHex.Of(s.ServerGot[0]), "the cast");
        });
    }

    static void SayBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Say("vendor buy"), "Say");
            Check.Equal("030013" + "00" + "005A" + "0003" + "76656E646F7220627579" + "00", UoHex.Of(s.ServerGot[0]),
                "the line");
        });
    }

    static void SayRefusedBefore120s()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.Clock.Sleep(GameLink.SayRepeatMs - 1);
            Check.Equal("said \"bank\" 119 s ago from here, and it got no answer", s.Link.Say("bank"), "the repeat");
        });
    }

    static void SayAllowedAt120s()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.Clock.Sleep(GameLink.SayRepeatMs);
            Check.Null(s.Link.Say("bank"), "the repeat");
        });
    }

    static void SayAfterMove(int tiles, bool allowed)
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.ServerSends(Packets.StepRefused(0, 1000 + tiles, 1000));
            Settle(s);
            string? why = s.Link.Say("bank");
            if (allowed)
                Check.Null(why, "the line after " + tiles + " tiles");
            else
                Check.True(why is not null && why.StartsWith("said \"bank\""),
                    "the line after " + tiles + " tiles: " + why);
        });
    }

    static void SayMovedThree() => SayAfterMove(GameLink.SayMovedTiles, true);

    static void SayMovedTwo() => SayAfterMove(GameLink.SayMovedTiles - 1, false);

    static void SayHeard()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.Link.Heard("BANK");
            Check.Null(s.Link.Say("bank"), "the line after it was heard");
        });
    }

    static void SayHeardOther()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.Link.Heard("vendor buy");
            Check.True(s.Link.Say("bank") is not null, "the line refused");
        });
    }

    static void SayForgotten()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Say("bank");
            s.Link.ForgetSaid();
            Check.Null(s.Link.Say("bank"), "the line after the guard was forgotten");
        });
    }

    static void SayRefusedNotCounted()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.State.Dead = true;
            Check.Equal("the player is dead", s.Link.Say("bank"), "the line while dead");
            s.Link.State.Dead = false;
            Check.Null(s.Link.Say("bank"), "the line once alive");
        });
    }

    static void OtherBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Buy(0x000764E0, new List<uint[]> { new uint[] { 0x40076518, 10 } }), "Buy");
            Check.Null(s.Link.Sell(0x000764E0, new List<uint[]> { new uint[] { 0x40001234, 5 } }), "Sell");
            Check.Null(s.Link.Status(4, Packets.Player), "Status");
            s.Link.Settle();
            Check.Null(s.Link.Drop(Bag, 0x42BE4F7C), "Drop");
            s.Link.Settle();
            Check.Null(s.Link.DropAt(Bag, 0x42BE4F7C, 44, 65), "DropAt");
            s.Link.Settle();
            Check.Null(s.Link.DropGround(Bag, 4699, 1110, -5), "DropGround");
            s.Link.Settle();
            Check.Null(s.Link.Equip(Bag, 0x19, Packets.Player), "Equip");
            ImmutableArray<byte[]> got = s.ServerGot;
            Check.Equal("3B000F000764E0021A40076518000A", UoHex.Of(got[0]), "the buy");
            Check.Equal("9F000F000764E00001400012340005", UoHex.Of(got[1]), "the sale");
            Check.Equal("34EDEDEDED0402BE4F79", UoHex.Of(got[2]), "the status request");
            Check.Equal("0843DD0F1AFFFFFFFF0042BE4F7C", UoHex.Of(got[3]), "the drop");
            Check.Equal("0843DD0F1A002C0041" + "0042BE4F7C", UoHex.Of(got[4]), "the drop at");
            Check.Equal("0843DD0F1A125B0456FBFFFFFFFF", UoHex.Of(got[5]), "the ground drop");
            Check.Equal("1343DD0F1A1902BE4F79", UoHex.Of(got[6]), "the equip");
        });
    }

    static void SettleClears()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Use(Bag, false);
            s.Link.Settle();
            Check.Null(s.Link.InFlight, "InFlight");
        });
    }

    static void TargetNoCursor()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Equal("no assist cursor is open", s.Link.Target(false, Bag, 1, 2, 3, 4), "Target");
        });
    }

    static void TargetObject()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, 0, false);
            Check.Null(s.Link.Target(false, 0x44DE6EA5, 93, 47, 0, 0x1F14), "Target");
            Check.Equal("6C000000123403" + "44DE6EA5" + "005D002F" + "0000" + "1F14",
                UoHex.Of(s.ServerGot[s.ServerGot.Length - 1]), "the target");
            Check.False(s.Link.MacroCursor, "MacroCursor after the target");
        });
    }

    static void TargetGround()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, 0, false);
            Check.Null(s.Link.Target(true, 0xDEADBEEF, 4699, 1110, -2, 0), "Target");
            Check.Equal("6C010000123403" + "00000000" + "125B0456" + "00FE" + "0000",
                UoHex.Of(s.ServerGot[s.ServerGot.Length - 1]), "the target");
        });
    }

    static void ToClientBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.ToClient(new byte[] { 0x97, 3 }, "a step"), "ToClient");
            Settle(s);
            Check.True(ClientGotHex(s, "9703"), "the client got it");
            Check.True(Logged(s, "c1 INJ S>C a step 9703"), "the INJ line");
        });
    }

    static string TellText(FakeShard s)
    {
        byte[] p = LastClientGot(s, 0x1C);
        return Encoding.ASCII.GetString(p, 44, p.Length - 45);
    }

    static void TellAt200()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Tell(new string('a', 200)), "Tell");
            Settle(s);
            Check.Equal(200, TellText(s).Length, "characters shown");
        });
    }

    static void TellAt201()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Tell(new string('a', 201)), "Tell");
            Settle(s);
            Check.Equal(200, TellText(s).Length, "characters shown");
        });
    }

    static void MoveClientBytes()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.MoveClient(3), "MoveClient");
            Settle(s);
            Check.True(ClientGotHex(s, "9703"), "the client got it");
        });
    }

    static void ClosedClientRefuses()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Closed = true;
            Check.Equal("the connection is closed", s.Link.ToClient(new byte[] { 0x97, 1 }, "x"), "ToClient");
            Check.Equal("the connection is closed", s.Link.Tell("x"), "Tell");
            Check.Equal("the connection is closed", s.Link.MoveClient(1), "MoveClient");
        });
    }

    static void ShowBeforeDraw()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Equal("the player has not been drawn yet", s.Link.ShowPlayer(), "ShowPlayer");
        });
    }

    static void ShowModeZero()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.Link.ShowMode = 0;
            int before = s.ClientGot.Length;
            Check.Null(s.Link.ShowPlayer(), "ShowPlayer mid-walk");
            Settle(s);
            Check.Equal(before, s.ClientGot.Length, "packets the client got mid-walk");
            Check.Null(s.Link.ShowPlayer(true), "ShowPlayer at the end");
            Settle(s);
            Check.Equal(0x20, (int)s.ClientGot[s.ClientGot.Length - 1][0], "the drawing at the end");
        });
    }

    static void ShowMode77()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.Link.ShowMode = 0x77;
            Check.Null(s.Link.ShowPlayer(), "ShowPlayer");
            Settle(s);
            byte[] moved = LastClientGot(s, 0x77);
            Check.Equal(17, moved.Length, "the 0x77's length");
            Check.Equal("7702BE4F790190" + "03E8" + "03E8", UoHex.Of(moved).Substring(0, 22), "serial, body and place");
        });
    }

    static void StepUnhides()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0x80));
            Settle(s);
            s.Link.Step(2);
            s.Link.ShowPlayer(true);
            Settle(s);
            Check.Equal(0, s.ClientGot[s.ClientGot.Length - 1][10] & 0x80, "the hidden flag in the drawing");
        });
    }

    static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] both = new byte[a.Length + b.Length];
        a.CopyTo(both, 0);
        b.CopyTo(both, a.Length);
        return both;
    }

    static int HaltLines(FakeShard s)
    {
        int n = 0;
        foreach (string line in s.Log.Lines)
            n += line.StartsWith("c1 -- HALT ") ? 1 : 0;
        return n;
    }

    static void HaltOnce()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientSends(new byte[] { 0xFF, 1, 2 });
            Settle(s);
            string first = s.Link.Halted;
            UoHuffman huff = new(Copy(FakeShard.Wire.Book));
            s.ServerSendsRaw(huff.Encode(new byte[] { 0xFF, 0, 0 }));
            Settle(s);
            Check.Equal(first, s.Link.Halted, "Halted");
            Check.Equal(1, HaltLines(s), "HALT lines");
        });
    }

    static int[] Copy(ImmutableArray<int> values)
    {
        int[] copy = new int[values.Length];
        values.CopyTo(copy);
        return copy;
    }

    static void ClientHoldDeclines()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.HoldClient = p => p[0] == 0x07;
            s.ClientSends(Packets.Use(Bag));
            Settle(s);
            Check.Equal(1, s.ServerGot.Length, "packets the server got");
        });
    }

    static void ResetOtherMessage()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ResetClient("Connection aborted.");
            s.WaitEnded();
            Check.True(Logged(s, "c1 -- ENDED BY CLIENT"), "the END line");
            foreach (string line in s.Log.Lines)
                Check.False(line.Contains("CRASH"), "a crash line: " + line);
        });
    }

    static void CloseThrows()
    {
        GameLinkTests.WithLink(s =>
        {
            s.CloseThrows = true;
            s.CloseClient();
            s.WaitEnded();
            Check.True(s.Link.Closed, "Closed");
        });
    }

    static void QueuedDroppedOnUnframe()
    {
        GameLinkTests.WithLink(s =>
        {
            byte[] mine = Packets.Use(0x11111111);
            s.ClientSends(mine, 0, 3);
            Settle(s);
            Check.Null(s.Link.Look(Bag), "the assist's look, queued");
            s.ClientSends(Concat(new byte[] { mine[3], mine[4] }, new byte[] { 0xFF }));
            Settle(s);
            Check.Equal(1, s.ServerGot.Length, "packets framed on the wire");
            Check.Bytes(mine, s.ServerGot[0], "the client's own");
            Check.Equal(6, s.ServerBytes, "bytes on the wire: the client's packet and its 0xFF, not the queued look");
        });
    }

    static void ServerFramerDesync()
    {
        GameLinkTests.WithLink(s =>
        {
            UoHuffman huff = new(Copy(FakeShard.Wire.Book));
            byte[] bad = huff.Encode(new byte[] { 0xFF, 0, 0 });
            byte[] after = huff.Encode(Packets.Speech(0xFFFFFFFF, "", "after", 0));
            int before = s.ClientBytes;
            s.ServerSendsRaw(Concat(bad, after));
            Settle(s);
            Check.True(Logged(s,
                "c1 -- HALT server packet unframed: opcode FF past the length table; server bytes now pass raw"),
                "the HALT line");
            Check.Equal(before + bad.Length + after.Length, s.ClientBytes, "bytes to the client, all raw");
        });
    }

    static void LiftNotAnswered()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Lift(Bag, 1);
            s.ServerSends(Packets.Speech(0x0000ABCD, "Morena", "Hello", 0));
            Settle(s);
            Check.Equal("lift 43DD0F1A", s.Link.InFlight, "InFlight");
        });
    }

    // 0x1A: its declared length, the serial (high bit: an amount follows), then whatever bytes the case gives.
    static byte[] Redraw(uint serial, int length)
    {
        byte[] p = new byte[length];
        p[0] = 0x1A;
        p[1] = (byte)(length >> 8);
        p[2] = (byte)length;
        uint s = serial | 0x80000000;
        for (int i = 0; i < 4 && 3 + i < length; i++)
            p[3 + i] = (byte)(s >> (24 - 8 * i));
        return p;
    }

    static void OtherItemRedraw()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Use(0x40001A90, false);
            s.ServerSends(Redraw(0x40001A91, 16));
            Settle(s);
            Check.Equal("use 40001A90", s.Link.InFlight, "InFlight");
        });
    }

    static void ShortRedrawSix()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Use(0x40001A90, false);
            s.ServerSends(Redraw(0x40001A90, 6));
            Settle(s);
            Check.Equal("use 40001A90", s.Link.InFlight, "InFlight");
        });
    }

    static void ShortRedrawSeven()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Use(0x40001A90, false);
            s.ServerSends(Redraw(0x40001A90, 7));
            Settle(s);
            Check.Null(s.Link.InFlight, "InFlight");
        });
    }

    static void ShortLineSix()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Link.Use(Bag, false);
            s.ServerSends(new byte[] { 0x1C, 0x00, 0x06, 0x00, 0x00, 0xAB });
            Settle(s);
            Check.Null(s.Link.InFlight, "InFlight");
        });
    }

    static void ShortLineSeven()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Use(Bag, false);
            s.ServerSends(new byte[] { 0x1C, 0x00, 0x07, 0x00, 0x00, 0xAB, 0xCD });
            Settle(s);
            Check.Equal("use 43DD0F1A", s.Link.InFlight, "InFlight");
        });
    }

    static void ServerHoldDeclines()
    {
        GameLinkTests.WithLink(s =>
        {
            byte[] line = Packets.Speech(0xFFFFFFFF, "", "shown", 0);
            s.Link.HoldServer = p => false;
            s.ServerSends(line);
            Settle(s);
            Check.True(ClientGotHex(s, UoHex.Of(line)), "the client got it");
        });
    }

    static void SystemLineAnswers()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.Link.Use(Bag, false);
            s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "You must wait to perform another action.", 0));
            Settle(s);
            Check.Null(s.Link.InFlight, "InFlight");
        });
    }

    static void FailSafeRefuses()
    {
        GameLinkTests.WithLink(s =>
        {
            s.Disabled = "the server refused assist actions (4 in 60 s)";
            Check.Equal("assist disabled by the fail-safe: the server refused assist actions (4 in 60 s)",
                s.Link.Use(Bag), "Use");
            Check.Equal(0, s.ServerGot.Length, "packets the server got");
        });
    }

    static void SkillsRequest()
    {
        GameLinkTests.WithLink(s =>
        {
            Check.Null(s.Link.Status(5, Packets.Player), "Status");
            Check.Equal("skills request", s.Link.InFlight, "InFlight");
        });
    }

    static void TargetAfterClientAnswered()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, 0, false);
            s.ClientSends(Packets.Filled(0x6C, FakeShard.Wire.Lengths[0x6C]));
            Settle(s);
            Check.Equal("no assist cursor is open", s.Link.Target(false, Bag, 1, 2, 3, 4), "Target");
        });
    }

    static void TargetRefusedKeepsCursor()
    {
        GameLinkTests.WithLink(s =>
        {
            CursorAfter(s, 0, false);
            s.Link.Halted = "a test halt";
            Check.Equal("halted: a test halt", s.Link.Target(false, Bag, 1, 2, 3, 4), "Target");
            Check.True(s.Link.MacroCursor, "MacroCursor");
        });
    }

    static void OtherMobileDraw()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ClientSends(Packets.Step(2, 4));
            Settle(s);
            byte[] other = Packets.Draw(1001, 1000, 0, 0);
            other[1] = 0x00;
            other[2] = 0x00;
            other[3] = 0x12;
            other[4] = 0x34;
            s.ServerSends(other);
            Settle(s);
            s.ClientSends(Packets.Step(2, 5));
            Settle(s);
            Check.Equal(1, (int)s.ServerGot[s.ServerGot.Length - 1][2], "the number after another's redraw");
        });
    }

    static void ShowClosed()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.Link.Closed = true;
            Check.Equal("the player has not been drawn yet", s.Link.ShowPlayer(true), "ShowPlayer");
        });
    }

    static void ShowRaw()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            UoHuffman huff = new(Copy(FakeShard.Wire.Book));
            s.ServerSendsRaw(huff.Encode(new byte[] { 0xFF, 0, 0 }));
            Settle(s);
            Check.Equal("server stream unframed", s.Link.ShowPlayer(true), "ShowPlayer");
        });
    }

    static void StepBeforeDraw()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            int before = s.ClientGot.Length;
            s.Link.Step(2);
            s.ServerSends(Packets.StepAck(0));
            Settle(s);
            Check.Equal(before, s.ClientGot.Length, "packets the client got");
        });
    }

    static void ServerReset()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ResetServer("An existing connection was forcibly closed by the remote host.");
            s.WaitEnded();
            Check.True(Logged(s, "c1 -- ENDED BY SERVER"), "the END line");
            Check.True(Logged(s, "c1 -- S>C END An existing connection was forcibly closed by the remote host."),
                "the S>C END line");
        });
    }

    static void ClientWriteFails()
    {
        GameLinkTests.WithLink(s =>
        {
            s.ClientRefusesWrites = true;
            s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "x", 0));
            s.WaitEnded();
            Check.True(Logged(s, "c1 -- ENDED BY CLIENT"), "the END line");
            Check.True(Logged(s, "c1 -- S>C END the client's socket is gone"), "the S>C END line");
        });
    }

    static void ShowMode77Final()
    {
        GameLinkTests.WithLink(s =>
        {
            Login(s);
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            Settle(s);
            s.Link.ShowMode = 0x77;
            Check.Null(s.Link.ShowPlayer(true), "ShowPlayer");
            Settle(s);
            Check.Equal(0x20, (int)s.ClientGot[s.ClientGot.Length - 1][0], "the drawing at the end");
        });
    }
}
