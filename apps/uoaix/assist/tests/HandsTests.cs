#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Text;

namespace Uoaix.Assist.Tests;

// The hands (Hands.cs; AssistStructure.md stage 2): a lift judged by the server's refusal, a busy refusal tried again,
// and every put-down checked against where the item lay (the refused drop that stalled Kwik, 2026-10-21).
public static class HandsTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x44AC0001;
    const uint Shirt = 0x44AC0002;
    const uint Other = 0x44AC0003;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("hands: a drop the server takes into the bag is put down", PutTaken),
        new TestCase("hands: a drop the server ignores is put back where it lay, dropped once", PutIgnored),
        new TestCase("hands: a part lift whose remainder stays as the lift left it landed; a part sent back beside "
        + "it, merged back, or a whole stack left is put back", PartLift),
        new TestCase("hands: on the ground, a part lift beside another stack of its kind landed; a whole stack off an "
        + "empty ground landed", PartLiftGround),
        new TestCase("hands: a drop the server sends back to the pack is put back", PutSentBack),
        new TestCase("hands: a drop merged into another stack (the item gone) is put down", PutMerged),
        new TestCase("hands: a lift the server refuses is not lifted and nothing is dropped", LiftRefused),
        new TestCase("hands: a lift refused as busy is tried again and taken, the refusal counted", BusyRetried),
        new TestCase("hands: a refusal records the last one and counts", RefusalsCounted),
        new TestCase("hands: an item never sent is put down on the drop's answer alone", PutUnknown),
        new TestCase("hands: the action scope holds the gate until disposed", ActHoldsGate),
        new TestCase("hands: TryAct while another action holds the gate is null", TryActHeld),
        new TestCase("hands: TryAct on a free gate holds it until disposed, a second dispose harmless", TryActFree),
        new TestCase("hands: a settle that throws lets the gate go, for Act and TryAct", SettleThrows),
        new TestCase("hands: a refusal within 5 s of a lift judged taken is a late one, seen for lift-settle",
        LateRefusal),
        new TestCase("hands: a late refusal after the player's own lift is the player's, not seen", LateAfterHandLift),
        new TestCase("hands: a refusal 5 s or more after the lift is not a late one", RefusalTooLate),
        new TestCase("hands: an answer that does not come in time settles the link and is false", AnswerTimesOut),
        new TestCase("hands: an answer waited out is timed for answer; one after the player's own lift is not",
        AnswerTimed),
        new TestCase("hands: an answer awaited on a closed link is false", AnswerClosed),
        new TestCase("hands: a lift the link refuses to send is refused with its reason", LiftNotSent),
        new TestCase("hands: a lift with its own settle is judged on it and sets no late-refusal mark", OwnSettle),
        new TestCase("hands: a lift refused as busy past the retries is refused, three lifts sent", BusyExhausted),
        new TestCase("hands: a drop the link refuses to send is refused, lifted", DropNotSent),
        new TestCase("hands: no game connection throws", NoLink),
        new TestCase("hands: a busy lift the link refuses to send, no refusal ever seen, is refused once", BusyNotSent),
        new TestCase("disarm: the shield's drop put back stops the disarm and says so; taken, it disarms",
        DisarmChecked),
        new TestCase("arm: the shield's equip the server ignores stops the arm; worn, it arms", ArmChecked),
        new TestCase("harvest: a pickup the server puts back stops with the server's answer, dropped once",
        GatherPutBack),
        new TestCase("cast: with a weapon and shield in hand the cast waits for both in the pack, and once aimed both "
        + "come back", CastDisarmsAimed),
        new TestCase("cast: a spell that asks no target re-arms after the cursor wait; a fizzle re-arms at once",
        CastNoCursorOrFizzle),
        new TestCase("cast: off, with empty hands or a spellbook in hand, the cast goes straight out",
        CastPassesThrough),
        new TestCase("cast: a Recall that fizzles is cast once more, then re-armed; two fizzles, two casts; then "
        + "aimed, re-armed on the aim", CastRecallFizzle),
        new TestCase("cast: a disarm the server refuses sends no cast; an item gone from the pack is not re-armed",
        CastRefusals));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static byte[] Delete(uint serial) => new byte[] { 0x1D, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    static byte[] Refusal(byte reason) => new byte[] { 0x27, reason };

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static int Sent(FakeShard s, byte op)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == op ? 1 : 0;
        return n;
    }

    static void With(Action<FakeShard, AssistFeatures, Hands> body)
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
            s.ServerSends(Packets.Contained(Shirt, 0x1517, 1, Pack));
            s.Clock.Sleep(1);
            Hands h = new(() => s.Link, s.Clock, f.Timings, () => "busy", new object());
            s.Link.ServerPacket += p =>
            {
                if (p[0] == 0x27)
                    h.Refused(p);
            };
            body(s, f, h);
        });
    }

    static void PutTaken()
    {
        With((s, f, h) =>
        {
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Shirt, 0x1517, 1, Bag))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Shirt, 1, () => s.Link.Drop(Shirt, Bag), out bool lifted);
            Check.True(lifted, "lifted");
            Check.True(o.Done, "put down: " + o);
            Check.Equal(Bag, s.Link.State.Items[Shirt]!.Container, "in the bag");
        });
    }

    const uint Gold = 0x473B8121;
    const uint GoldPart = 0x473BA570;
    const uint Pile = 0x45DD948B;

    // uo1998 2026-10-24 13:08:16: 3315 of 3915 gold lifted, the 600 left as the same serial where it lay, the 3315
    // merged into the bank's pile; the remainder alone, as the lift left it, is a drop that landed. A part the server
    // sends back beside the remainder, or merged back into it, is one put back.
    static void PartLift()
    {
        With((s, f, h) =>
        {
            s.ServerSends(Packets.Contained(Gold, 0x0EED, 3915, Pack));
            s.ServerSends(Packets.Contained(Pile, 0x0EED, 3402, Bag));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Packets.Contained(Gold, 0x0EED, 600, Pack))
                : p[0] == 0x08 ? ImmutableArray.Create(Delete(Pile), Packets.Contained(Pile, 0x0EED, 6717, Bag))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Gold, 3315, () => s.Link.Drop(Gold, Pile), out bool lifted);
            Check.True(lifted, "lifted");
            Check.True(o.Done, "landed: " + o);
            s.ServerSends(Packets.Contained(Gold, 0x0EED, 3915, Pack));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Packets.Contained(Gold, 0x0EED, 600, Pack))
                : p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(GoldPart, 0x0EED, 3315, Pack))
                : ImmutableArray<byte[]>.Empty;
            Check.Equal(Hands.PutBack, h.Put(Gold, 3315, () => s.Link.Drop(Gold, Pile), out _).Why,
                "a part sent back beside the remainder");
            s.ServerSends(Delete(GoldPart));
            s.ServerSends(Packets.Contained(Gold, 0x0EED, 3915, Pack));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Packets.Contained(Gold, 0x0EED, 600, Pack))
                : p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Gold, 0x0EED, 3915, Pack))
                : ImmutableArray<byte[]>.Empty;
            Check.Equal(Hands.PutBack, h.Put(Gold, 3315, () => s.Link.Drop(Gold, Pile), out _).Why,
                "a part merged back into the remainder");
            s.Answer = p => ImmutableArray<byte[]>.Empty;
            Check.Equal(Hands.PutBack, h.Put(Gold, 3915, () => s.Link.Drop(Gold, Pile), out _).Why,
                "a whole lift left where it lay");
        });
    }

    // On the ground: a part lift beside another stack of its kind on its tile, the remainder as the lift left it,
    // landed; a whole stack lifted from ground holding nothing else and taken into the bag, landed.
    static void PartLiftGround()
    {
        With((s, f, h) =>
        {
            s.ServerSends(Lying(Gold, 0x0EED, 100, 1001, 1000));
            s.ServerSends(Lying(GoldPart, 0x0EED, 50, 1001, 1000));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Lying(Gold, 0x0EED, 60, 1001, 1000))
                : p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(0x473BB000, 0x0EED, 40, Bag))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Gold, 40, () => s.Link.Drop(Gold, Bag), out bool lifted);
            Check.True(lifted && o.Done, "a part lift beside another stack landed: " + o);
            s.ServerSends(Delete(Gold));
            s.ServerSends(Delete(GoldPart));
            s.ServerSends(Lying(Gold, 0x0EED, 100, 1001, 1000));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Delete(Gold))
                : p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Gold, 0x0EED, 100, Bag))
                : ImmutableArray<byte[]>.Empty;
            Check.True(h.Put(Gold, 100, () => s.Link.Drop(Gold, Bag), out _).Done,
                "a whole stack off an empty ground landed");
        });
    }

    static void PutIgnored()
    {
        With((s, f, h) =>
        {
            Outcome o = h.Put(Shirt, 1, () => s.Link.Drop(Shirt, Bag), out bool lifted);
            Check.True(lifted, "lifted");
            Check.Equal(Ending.Refused, o.Ending, "refused");
            Check.Equal("the server put it back where it lay", o.Why, "why");
            Check.Equal(1, Sent(s, 0x08), "one drop");
        });
    }

    static void PutSentBack()
    {
        With((s, f, h) =>
        {
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Shirt, 0x1517, 1, Pack))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Shirt, 1, () => s.Link.DropGround(Shirt, 1000, 1000, 0), out bool lifted);
            Check.Equal("the server put it back where it lay", o.Why, "why");
        });
    }

    static void PutMerged()
    {
        With((s, f, h) =>
        {
            s.ServerSends(Packets.Contained(Other, 0x1517, 1, Bag));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Delete(Shirt)) : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Shirt, 1, () => s.Link.Drop(Shirt, Other), out bool lifted);
            Check.True(o.Done, "put down: " + o);
        });
    }

    static void LiftRefused()
    {
        With((s, f, h) =>
        {
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Refusal(1)) : ImmutableArray<byte[]>.Empty;
            Outcome o = h.Put(Shirt, 1, () => s.Link.Drop(Shirt, Bag), out bool lifted);
            Check.False(lifted, "not lifted");
            Check.Equal("the server refused the lift (reason 1)", o.Why, "why");
            Check.Equal(0, Sent(s, 0x08), "no drop");
        });
    }

    static void BusyRetried()
    {
        With((s, f, h) =>
        {
            int lifts = 0;
            s.Answer = p => p[0] == 0x07 && lifts++ == 0 ? ImmutableArray.Create<byte[]>(Refusal(Hands.BusyReason))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.BusyLift(Shirt, 1);
            Check.True(o.Done, "taken: " + o);
            Check.Equal(2, Sent(s, 0x07), "two lifts");
            Check.Equal(1L, h.Refusals, "one refusal");
            Check.True(h.LiftedAt > DateTime.MinValue, "lifted at");
        });
    }

    static void RefusalsCounted()
    {
        With((s, f, h) =>
        {
            Check.True(h.LastRefusal is null, "none yet");
            s.ServerSends(Refusal(2));
            s.ServerSends(Refusal(3));
            s.Clock.Sleep(10);
            Check.Equal(2L, h.Refusals, "two");
            Check.Equal((byte)3, h.LastRefusal![1], "the last");
        });
    }

    static void PutUnknown()
    {
        With((s, f, h) =>
        {
            Outcome o = h.Put(0x44ACFFFF, 1, () => s.Link.Drop(0x44ACFFFF, Bag), out bool lifted);
            Check.True(o.Done, "put down: " + o);
        });
    }

    static void LateRefusal()
    {
        With((s, f, h) =>
        {
            Check.True(h.Lift(Shirt, 1).Done, "taken");
            string before = f.Timings.SeenText("lift-settle");
            s.Clock.Sleep(1000);
            s.ServerSends(Refusal(1));
            s.Clock.Sleep(10);
            Check.True(f.Timings.SeenText("lift-settle") != before, "seen: " + f.Timings.SeenText("lift-settle"));
        });
    }

    static void LateAfterHandLift()
    {
        With((s, f, h) =>
        {
            Check.True(h.Lift(Shirt, 1).Done, "taken");
            string before = f.Timings.SeenText("lift-settle");
            s.Clock.Sleep(100);
            h.HandLift();
            s.ServerSends(Refusal(1));
            s.Clock.Sleep(10);
            Check.Equal(before, f.Timings.SeenText("lift-settle"), "not seen");
        });
    }

    static void RefusalTooLate()
    {
        With((s, f, h) =>
        {
            Check.True(h.Lift(Shirt, 1).Done, "taken");
            string before = f.Timings.SeenText("lift-settle");
            s.Clock.Sleep(Hands.LateRefusalMs);
            s.ServerSends(Refusal(1));
            s.Clock.Sleep(10);
            Check.Equal(before, f.Timings.SeenText("lift-settle"), "not seen");
        });
    }

    static void AnswerTimesOut()
    {
        With((s, f, h) =>
        {
            Check.Null(s.Link.Use(Bag, true), "Use");
            Check.False(h.Answered(300), "no answer");
            Check.True(s.Link.InFlight is null, "settled");
        });
    }

    // A use answered 500 ms on is a sample; the same with the player's lift between is the lift's answer, not seen
    // (2026-10-24 12:34:20: an unanswered bag timed by the player's lift at 3339 ms).
    static void AnswerTimed()
    {
        With((s, f, h) =>
        {
            string before = f.Timings.SeenText("answer");
            Check.Null(s.Link.Use(Bag, true), "Use");
            s.Also(500, () => s.ServerSends(Packets.Delete(Bag + 0x100)));
            Check.True(h.Answered(3000), "answered");
            string seen = f.Timings.SeenText("answer");
            Check.True(seen != before, "seen: " + seen);
            Check.Null(s.Link.Use(Bag, true), "Use again");
            s.Clock.Sleep(100);
            h.HandLift();
            s.Also(400, () => s.ServerSends(Packets.Delete(Bag + 0x101)));
            Check.True(h.Answered(3000), "answered after the lift");
            Check.Equal(seen, f.Timings.SeenText("answer"), "the lift's answer not seen");
        });
    }

    static void AnswerClosed()
    {
        With((s, f, h) =>
        {
            Check.Null(s.Link.Use(Bag, true), "Use");
            s.CloseServer();
            s.Clock.Sleep(10);
            Check.False(h.Answered(300), "closed");
        });
    }

    static void LiftNotSent()
    {
        With((s, f, h) =>
        {
            s.Disabled = "the test disabled the assist";
            Outcome o = h.Lift(Shirt, 1);
            Check.Equal(Ending.Refused, o.Ending, "refused");
            Check.True(o.Why.Contains("the test disabled the assist"), "why: " + o.Why);
            Check.Equal(0, Sent(s, 0x07), "nothing sent");
        });
    }

    static void OwnSettle()
    {
        With((s, f, h) =>
        {
            DateTime at = s.Clock.UtcNow;
            Check.True(h.Lift(s.Link, Shirt, 1, 100).Done, "taken");
            Check.True((s.Clock.UtcNow - at).TotalMilliseconds < f.Timings.Ms("lift-settle"), "on its own settle");
            string before = f.Timings.SeenText("lift-settle");
            s.ServerSends(Refusal(1));
            s.Clock.Sleep(10);
            Check.Equal(before, f.Timings.SeenText("lift-settle"), "its late refusal is its own");
        });
    }

    static void BusyExhausted()
    {
        With((s, f, h) =>
        {
            s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(Refusal(Hands.BusyReason))
                : ImmutableArray<byte[]>.Empty;
            Outcome o = h.BusyLift(Shirt, 1);
            Check.Equal(Ending.Refused, o.Ending, "refused");
            Check.Equal(Hands.BusyRetries + 1, Sent(s, 0x07), "lifts");
        });
    }

    static void DropNotSent()
    {
        With((s, f, h) =>
        {
            Outcome o = h.Put(Shirt, 1, () => "the drop could not be sent", out bool lifted);
            Check.True(lifted, "lifted");
            Check.Equal("the drop could not be sent", o.Why, "why");
        });
    }

    static void BusyNotSent()
    {
        With((s, f, h) =>
        {
            s.Disabled = "the test disabled the assist";
            Outcome o = h.BusyLift(Shirt, 1);
            Check.Equal(Ending.Refused, o.Ending, "refused");
            Check.Equal(0, Sent(s, 0x07), "nothing sent");
        });
    }

    const uint Shield = 0x44AC0010;
    const int ShieldArt = 0x1B76;

    static void DisarmChecked()
    {
        With((s, f, h) =>
        {
            s.ServerSends(Packets.Wear(Shield, ShieldArt, 2, Packets.Player));
            s.Clock.Sleep(1);
            string? why = f.ArmToggle("shield");
            Check.True(why is not null && why.StartsWith("disarm stopped: the server put it back where it lay"),
                "put back: " + why);
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Shield, ShieldArt, 1, Pack))
                : ImmutableArray<byte[]>.Empty;
            Check.Null(f.ArmToggle("shield"), "disarmed");
            Check.True(s.Link.State.Items[Shield]!.Layer < 0, "in the pack");
        });
    }

    static void ArmChecked()
    {
        With((s, f, h) =>
        {
            s.ServerSends(Packets.Wear(Shield, ShieldArt, 2, Packets.Player));
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x08 ? ImmutableArray.Create<byte[]>(Packets.Contained(Shield, ShieldArt, 1, Pack))
                : ImmutableArray<byte[]>.Empty;
            Check.Null(f.ArmToggle("shield"), "disarmed");
            s.Answer = p => ImmutableArray<byte[]>.Empty;
            string? why = f.ArmToggle("shield");
            Check.True(why is not null && why.StartsWith("arm stopped: the server put it back where it lay"),
                "ignored: " + why);
            s.Answer = p => p[0] == 0x13
                ? ImmutableArray.Create<byte[]>(Packets.Wear(Shield, ShieldArt, 2, Packets.Player))
                : ImmutableArray<byte[]>.Empty;
            Check.Null(f.ArmToggle("shield"), "armed");
            Check.Equal(2, s.Link.State.Items[Shield]!.Layer, "worn");
        });
    }

    const uint Katana = 0x44AC0011;
    const int KatanaArt = 0x13FF;
    const uint Book = 0x44AC0012;

    // 0x12 type 0x56: the client's cast of a spell by id.
    static byte[] CastOf(int spell)
    {
        byte[] t = Encoding.ASCII.GetBytes(spell.ToString());
        byte[] p = new byte[5 + t.Length];
        p[0] = 0x12;
        p[1] = 0;
        p[2] = (byte)p.Length;
        p[3] = 0x56;
        t.CopyTo(p, 4);
        return p;
    }

    // The shard for a cast: a drop into the pack taken, an equip worn, a cast answered by a target cursor when cursor.
    static void CastShard(FakeShard s, bool cursor, string? said = null)
    {
        s.Answer = p =>
        {
            uint item = p.Length >= 5 ? U32(p, 1) : 0;
            int art = item == Katana ? KatanaArt : ShieldArt;
            if (p[0] == 0x08)
                return ImmutableArray.Create<byte[]>(Packets.Contained(item, art, 1, Pack));
            if (p[0] == 0x13)
                return ImmutableArray.Create<byte[]>(Packets.Wear(item, art, p[5], Packets.Player));
            if (p[0] == 0x12 && p[3] == 0x56)
            {
                if (said is not null)
                    return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "System", said, 0, 0x3B2));
                if (cursor)
                    return ImmutableArray.Create<byte[]>(Packets.Cursor(0x00000777, 0x01));
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static void Armed(FakeShard s)
    {
        s.ServerSends(Packets.Wear(Katana, KatanaArt, 1, Packets.Player));
        s.ServerSends(Packets.Wear(Shield, ShieldArt, 2, Packets.Player));
        s.Clock.Sleep(1);
    }

    static int FirstSent(FakeShard s, Func<byte[], bool> match)
    {
        int i = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (match(p))
                return i;
            i++;
        }
        return -1;
    }

    static void WaitNote(FakeShard s, AssistFeatures f, string text)
    {
        for (int i = 0; i < 1200 && !f.Notes.Exists(n => n.Contains(text)); i++)
            s.Clock.Sleep(100);
    }

    static void CastDisarmsAimed()
    {
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, true);
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(32));
                for (int i = 0; i < 400 && FirstSent(s, p => p[0] == 0x12) < 0; i++)
                    s.Clock.Sleep(50);
                Check.True(FirstSent(s, p => p[0] == 0x12) > FirstSent(s, p => p[0] == 0x08),
                    "the cast after the drops");
                Check.Equal(2, Sent(s, 0x08), "both to the pack");
                Check.Equal(0, Sent(s, 0x13), "nothing worn before the aim");
                s.Clock.Sleep(500);
                byte[] aim = new byte[19];
                aim[0] = 0x6C;
                aim[2] = 0;
                aim[3] = 0x07;
                aim[4] = 0x77;
                s.ClientSends(aim);
                WaitNote(s, f, "Cast: re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.True(f.Notes.Exists(n => n.Contains("Cast: spell 32 done (aimed)")), "done on the aim");
            Check.Equal(1, s.Link.State.Items[Katana]!.Layer, "the katana back");
            Check.Equal(2, s.Link.State.Items[Shield]!.Layer, "the shield back");
        });
    }

    static void CastNoCursorOrFizzle()
    {
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, false);
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(6));
                WaitNote(s, f, "Cast: re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.True(f.Notes.Exists(n => n.Contains("Cast: spell 6 done (no cursor)")), "no cursor");
            Check.Equal(1, s.Link.State.Items[Katana]!.Layer, "the katana back");
        });
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, true, "The spell fizzles.");
            f.CastHands = true;
            DateTime began = s.Clock.UtcNow;
            try
            {
                s.ClientSends(CastOf(4));
                WaitNote(s, f, "Cast: re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.True((s.Clock.UtcNow - began).TotalMilliseconds < AssistFeatures.CastCursorMs,
                "at once, not after the cursor wait");
            Check.Equal(1, Sent(s, 0x12), "a Heal that fizzles is not cast again");
            Check.True(f.Notes.Exists(n => n.Contains("Cast: spell 4 done (fizzled); re-arming")), "fizzled, re-armed");
            Check.Equal(2, s.Link.State.Items[Shield]!.Layer, "the shield back");
        });
    }

    static void CastRecallFizzle()
    {
        // Every cast fizzles: two casts, then the re-arm.
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, true, "The spell fizzles.");
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(32));
                WaitNote(s, f, "Cast: re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.Equal(2, Sent(s, 0x12), "two casts");
            Check.True(f.Notes.Exists(n => n.Contains("Cast: spell 32 done (fizzled); casting the Recall once more")),
                "once more");
            Check.Equal(1, s.Link.State.Items[Katana]!.Layer, "the katana back");
        });
        // The first fizzles, the second asks for its rune: re-armed on the aim.
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, true);
            Func<byte[], ImmutableArray<byte[]>> shard = s.Answer!;
            int casts = 0;
            s.Answer = p => p[0] == 0x12 && p[3] == 0x56 && ++casts == 1
                ? ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "System", "The spell fizzles.", 0, 0x3B2))
                : shard(p);
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(32));
                for (int i = 0; i < 400 && Sent(s, 0x12) < 2; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(500);
                byte[] aim = new byte[19];
                aim[0] = 0x6C;
                aim[3] = 0x07;
                aim[4] = 0x77;
                s.ClientSends(aim);
                WaitNote(s, f, "Cast: re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.Equal(2, Sent(s, 0x12), "two casts");
            Check.True(f.Notes.Exists(n => n.Contains("Cast: spell 32 done (aimed); re-arming")), "aimed the second");
        });
    }

    static void CastPassesThrough()
    {
        // Off.
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, true);
            s.ClientSends(CastOf(32));
            s.Clock.Sleep(500);
            Check.Equal(0, FirstSent(s, p => p[0] == 0x12), "off: the cast first");
            Check.Equal(0, Sent(s, 0x08), "off: nothing to the pack");
        });
        // Empty hands, and a spellbook in hand.
        foreach (bool book in new[] { false, true })
        {
            With((s, f, h) =>
            {
                if (book)
                {
                    s.ServerSends(Packets.Wear(Book, 0x0EFA, 1, Packets.Player));
                    s.Clock.Sleep(1);
                }
                CastShard(s, true);
                f.CastHands = true;
                try
                {
                    s.ClientSends(CastOf(32));
                    s.Clock.Sleep(500);
                }
                finally
                {
                    f.CastHands = false;
                }
                Check.Equal(0, FirstSent(s, p => p[0] == 0x12),
                    (book ? "a spellbook" : "empty hands") + ": the cast first");
                Check.Equal(0, Sent(s, 0x08), (book ? "a spellbook" : "empty hands") + ": nothing to the pack");
            });
        }
    }

    static void CastRefusals()
    {
        // The drop into the pack ignored: no cast.
        With((s, f, h) =>
        {
            Armed(s);
            s.Answer = p => ImmutableArray<byte[]>.Empty;
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(32));
                WaitNote(s, f, "Cast: not cast");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.True(f.Notes.Exists(n =>
                n.Contains("Cast: not cast, the hands not freed: the cast's disarm stopped")), "said");
            Check.Equal(0, Sent(s, 0x12), "no cast");
        });
        // The katana gone from the pack before the re-arm.
        With((s, f, h) =>
        {
            Armed(s);
            CastShard(s, false);
            f.CastHands = true;
            try
            {
                s.ClientSends(CastOf(6));
                WaitNote(s, f, "done (no cursor)");
                s.ServerSends(Delete(Katana));
                WaitNote(s, f, "Cast: not re-armed");
            }
            finally
            {
                f.CastHands = false;
            }
            Check.True(f.Notes.Exists(n =>
                n.Contains("Cast: not re-armed: the 13FF 44AC0011 is no longer in the pack")), "said");
        });
    }

    // 0x1A: an item lying on the ground (amount follows the top bit), 16 bytes.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint t = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    static void GatherPutBack()
    {
        With((s, f, h) =>
        {
            const uint Wool = 0x44AC0020;
            s.ServerSends(Lying(Wool, 0x0DF8, 3, 1001, 1000));
            s.Clock.Sleep(1);
            AssistItem wool = s.Link.State.Ground[Wool]!;
            f.Gather(s.Link, wool, Pack, "wool");
            Check.Equal(1, Sent(s, 0x08), "one drop");
            Check.True(f.Sequence.StartsWith("harvest stopped: the server put it back where it lay"),
                "said: " + f.Sequence);
        });
    }

    static void NoLink()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Hands h = new(() => null, s.Clock, f.Timings, () => "busy", new object());
            Check.Throws<InvalidOperationException>(() => h.Answered(10), "Answered");
            Check.Throws<InvalidOperationException>(() => h.Lift(Shirt, 1), "Lift");
        });
    }

    static void ActHoldsGate()
    {
        object gate = new();
        RecallTests.WithFeatures((s, f) =>
        {
            Hands h = new(() => s.Link, s.Clock, f.Timings, () => "busy", gate);
            using (h.Act())
            {
                bool other = false;
                Thread t = new(() => other = Monitor.TryEnter(gate));
                t.Start();
                t.Join();
                Check.False(other, "another thread is kept out");
            }
            Check.True(Monitor.TryEnter(gate), "free after");
            Monitor.Exit(gate);
        });
    }

    static bool HeldElsewhere(object gate)
    {
        bool entered = false;
        Thread t = new(() =>
        {
            entered = Monitor.TryEnter(gate);
            if (entered)
                Monitor.Exit(gate);
        });
        t.Start();
        t.Join();
        return !entered;
    }

    static void TryActHeld()
    {
        object gate = new();
        RecallTests.WithFeatures((s, f) =>
        {
            Hands h = new(() => s.Link, s.Clock, f.Timings, () => "busy", gate);
            using ManualResetEventSlim held = new(false);
            using ManualResetEventSlim release = new(false);
            Thread t = new(() =>
            {
                lock (gate)
                {
                    held.Set();
                    release.Wait();
                }
            });
            t.Start();
            held.Wait();
            ActionScope? scope = h.TryAct();
            release.Set();
            t.Join();
            Check.True(scope is null, "no scope while another action holds the gate");
        });
    }

    static void TryActFree()
    {
        object gate = new();
        RecallTests.WithFeatures((s, f) =>
        {
            Hands h = new(() => s.Link, s.Clock, f.Timings, () => "busy", gate);
            ActionScope? scope = h.TryAct();
            Check.True(scope is not null, "a scope while the gate is free");
            Check.True(HeldElsewhere(gate), "the scope holds the gate");
            scope!.Dispose();
            scope.Dispose();
            Check.False(HeldElsewhere(gate), "free after, disposed twice");
        });
    }

    static void SettleThrows()
    {
        object gate = new();
        Check.Throws<InvalidOperationException>(() =>
            new ActionScope(gate, () => throw new InvalidOperationException("settle")), "Act");
        Check.False(HeldElsewhere(gate), "the gate is let go when Act's settle throws");
        Check.Throws<InvalidOperationException>(() =>
            ActionScope.TryEnter(gate, () => throw new InvalidOperationException("settle")), "TryAct");
        Check.False(HeldElsewhere(gate), "the gate is let go when TryAct's settle throws");
    }
}
