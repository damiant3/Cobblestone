// One action at a time on the game link (docs/Designs/Active/Apps/AssistStructure.md, stage 2): the action gate, the
// wait for the last action's answer, the busy time between actions, lifts judged by the server's refusal, and every
// put-down checked against where the item lay. AssistFeatures reaches it through members of the old names until each
// task moves to its own class (stage 3); each takes the link it acts on, as those members did.
#nullable enable
using System;
using System.Threading;

namespace Uoaix.Assist;

public interface IHands
{
    ActionScope Act();
    ActionScope? TryAct();
    bool Answered(int ms);
    void BusyWait(DateTime since);
    DateTime ClickedAt
    {
        get; set;
    }
    Outcome Lift(uint serial, int amount);
    Outcome BusyLift(uint serial, int amount);
    // Lifted, dropped by drop, its answer waited out, and checked: still where it lay, the server put it back.
    Outcome Put(uint serial, int amount, Func<string?> drop, out bool lifted);
}

// Where an item lay: its container (0 on the ground), its place there and its layer (-1 not worn).
public sealed record Spot(uint Container, int X, int Y, int Layer);

public sealed class Hands : IHands
{
    // A lift refused as busy (0x27 reason 0xFF) is tried again this many times after the busy time.
    public const int BusyRetries = 2;
    // A refusal that comes after the lift was judged accepted, within LateRefusalMs: lift-settle was too short. Only
    // when no lift of the player's own was sent after it: the player's lift refused as busy (2026-10-21 03:54:14.648,
    // 27 FF) was taken for a late refusal of the weave's bolt lift 3.5 s before and raised lift-settle from 450 to 5350
    // ms, and every bolt then waited 5.35 s between its lift and its drop.
    public const int LateRefusalMs = 5000;
    public const byte BusyReason = 0xFF;
    public const string PutBack = "the server put it back where it lay";
    // The put-down's poll, as the other put-down waits poll (the axe in hand, the held item in the pack).
    public const int LandedStepMs = 50;

    readonly Func<GameLink?> m_link;
    readonly IClock m_clock;
    readonly ITimings m_timings;
    readonly Func<string> m_busyKey;
    readonly object m_gate;
    volatile byte[]? m_lastRefusal;
    long m_refusals;
    // m_liftSentAt: the last lift sent, answered or refused; the server's busy time runs from it (a lift refused out of
    // range at 06:37:26.357 and the next lift 624 ms later refused busy). m_handLiftAt: the player's own last lift.
    DateTime m_liftAt;
    DateTime m_liftJudgedAt = DateTime.MinValue;
    DateTime m_lastRefusalAt;
    DateTime m_liftSentAt = DateTime.MinValue;
    DateTime m_handLiftAt = DateTime.MinValue;
    DateTime m_clickedAt = DateTime.MinValue;
    DateTime m_liftedAt = DateTime.MinValue;

    public Hands(Func<GameLink?> link, IClock clock, ITimings timings, Func<string> busyKey, object gate)
    {
        m_link = link;
        m_clock = clock;
        m_timings = timings;
        m_busyKey = busyKey;
        m_gate = gate;
    }

    GameLink Link => m_link() ?? throw new InvalidOperationException("no game connection");

    int AnswerMs => m_timings.Ms("answer");

    int LiftSettleMs => m_timings.Ms("lift-settle");

    int DropSettleMs => m_timings.Ms("drop-settle");

    int BusyMs => m_timings.Ms(m_busyKey());

    // The server's last refusal (0x27), null before the first.
    public byte[]? LastRefusal => m_lastRefusal;

    public long Refusals => Interlocked.Read(ref m_refusals);

    public DateTime ClickedAt
    {
        get => m_clickedAt;
        set => m_clickedAt = value;
    }

    public DateTime LiftedAt => m_liftedAt;

    public DateTime LiftSentAt => m_liftSentAt;

    // A 0x27 from the server: a lift refused, perhaps late for one already judged taken.
    public void Refused(byte[] p)
    {
        m_lastRefusalAt = m_clock.UtcNow;
        if (m_liftJudgedAt != DateTime.MinValue && m_handLiftAt > m_liftAt)
        {
            m_liftJudgedAt = DateTime.MinValue;
        }
        else if (m_liftJudgedAt != DateTime.MinValue
            && (m_lastRefusalAt - m_liftJudgedAt).TotalMilliseconds < LateRefusalMs)
        {
            m_liftJudgedAt = DateTime.MinValue;
            m_timings.Seen("lift-settle", (int)(m_lastRefusalAt - m_liftAt).TotalMilliseconds);
        }
        m_lastRefusal = p;
        Interlocked.Increment(ref m_refusals);
    }

    // The player's own lift from the client.
    public void HandLift()
    {
        m_handLiftAt = m_clock.UtcNow;
    }

    public ActionScope Act() => Act(Link);

    public ActionScope Act(GameLink k) => new(m_gate, () => Answered(k, AnswerMs));

    public ActionScope? TryAct() => TryAct(Link);

    // Act when no other action holds the gate, else null.
    public ActionScope? TryAct(GameLink k) => ActionScope.TryEnter(m_gate, () => Answered(k, AnswerMs));

    public bool Answered(int ms) => Answered(Link, ms);

    // The last action's answer waited out up to ms; false when it did not come (the link then settled) or the link
    // closed.
    public bool Answered(GameLink k, int ms)
    {
        DateTime until = m_clock.UtcNow.AddMilliseconds(ms);
        bool waited = k.InFlight is not null;
        DateTime from = k.InFlightAt;
        while (k.InFlight is not null && m_clock.UtcNow < until)
        {
            if (k.Closed)
                return false;
            m_clock.Sleep(20);
        }
        if (k.InFlight is not null)
        {
            k.Settle();
            return false;
        }
        // An answer after the player's own lift can be the lift's: a house vendor's bag, never answered, was timed by
        // the 0x1D of the player's lift at 3339 ms and moved "answer" (2026-10-24 12:34:20). Such a sample is not this
        // action's.
        if (waited && m_handLiftAt < from)
            m_timings.Seen("answer", (int)(m_clock.UtcNow - from).TotalMilliseconds);
        return true;
    }

    // A double-click or a lift leaves the player busy (BusyMs) from since.
    public void BusyWait(DateTime since)
    {
        int wait = BusyMs - (int)(m_clock.UtcNow - since).TotalMilliseconds;
        if (wait > 0)
            m_clock.Sleep(wait);
    }

    public Outcome Lift(uint serial, int amount) => Lift(Link, serial, amount);

    // A lift: refused when a 0x27 comes back, taken when none has in LiftSettleMs, or in settleMs from a caller that
    // tunes its own settle (PickupLoop), whose late refusals are its own and do not raise lift-settle.
    public Outcome Lift(GameLink k, uint serial, int amount, int settleMs = -1)
    {
        long before = Refusals;
        string? why = k.Lift(serial, amount);
        if (why is not null)
            return Outcome.Refused(why);
        DateTime at = m_clock.UtcNow;
        m_liftJudgedAt = DateTime.MinValue;
        m_liftSentAt = at;
        m_clock.Sleep(settleMs < 0 ? LiftSettleMs : settleMs);
        k.Settle();
        if (Refusals != before)
        {
            m_timings.Seen("lift-settle", (int)(m_lastRefusalAt - at).TotalMilliseconds);
            return Outcome.Refused("the server refused the lift (reason " + m_lastRefusal![1] + ")");
        }
        if (settleMs < 0)
        {
            m_liftAt = at;
            m_liftJudgedAt = m_clock.UtcNow;
        }
        return Outcome.Ok;
    }

    public Outcome BusyLift(uint serial, int amount) => BusyLift(Link, serial, amount);

    // The lift BusyMs after the last double-click; refused as busy, tried again after BusyMs up to BusyRetries times,
    // with auto-tune raising busy on a refusal and lifts taken lowering it.
    public Outcome BusyLift(GameLink k, uint serial, int amount)
    {
        for (int attempt = 0; ; attempt++)
        {
            BusyWait(m_clickedAt);
            Outcome o = Lift(k, serial, amount);
            if (o.Done)
            {
                m_liftedAt = m_clock.UtcNow;
                m_timings.Accepted(m_busyKey());
                return o;
            }
            byte[]? r = m_lastRefusal;
            if (r is null || r[1] != BusyReason || attempt >= BusyRetries)
                return o;
            m_timings.Refused(m_busyKey(), "the server refused a lift as busy");
            m_clickedAt = m_clock.UtcNow;
        }
    }

    // Where an item lies as the server last sent it, null when it is not known.
    public static Spot? Where(GameLink k, uint serial)
    {
        lock (k.State.Gate)
        {
            if (!k.State.Items.TryGet(serial, out AssistItem? it) && !k.State.Ground.TryGet(serial, out it))
                return null;
            return new Spot(it.Container, it.X, it.Y, it.Layer);
        }
    }

    public Outcome Put(uint serial, int amount, Func<string?> drop,
        out bool lifted) => Put(Link, serial, amount, drop, out lifted);

    // The refused drop is the case (AssistStructure.md stage 2: Kwik stalled 110 minutes on 2026-10-21 after a drop the
    // server did not take): a drop the server refuses comes back where the item lay (uo1998 sent both ore drops on the
    // player's own tile back into the pack, 2026-10-16 19:42:47 and 20:04:30), so an item there after the answer was
    // not put down. An item moved, merged into another (gone) or worn was.
    public Outcome Put(GameLink k, uint serial, int amount, Func<string?> drop, out bool lifted)
    {
        Spot? from = Where(k, serial);
        int had = AmountOf(k, serial, out int graphic);
        Outcome o = BusyLift(k, serial, amount);
        lifted = o.Done;
        if (!o.Done)
            return o;
        int beside = from is null ? 0 : Beside(k, serial, graphic, from);
        string? why = drop();
        if (why is not null)
            return Outcome.Refused(why);
        Answered(k, AnswerMs);
        m_clock.Sleep(DropSettleMs);
        Outcome landed = Landed(k, serial, from);
        // A part lift leaves its remainder where it lay under the same serial (uo1998 2026-10-24 13:08:16: 3315 of 3915
        // gold lifted, 600 left as 473B8121, the 3315 merged into the bank's pile, read as put back and the circuit
        // ended): it landed when the remainder holds what the lift left and no stack of its kind came back beside it.
        if (!landed.Done && amount < had && AmountOf(k, serial, out _) == had - amount
            && Beside(k, serial, graphic, from!) == beside)
            return Outcome.Ok;
        return landed;
    }

    // An item's amount and graphic as the server last sent them; 0 when it is not known.
    static int AmountOf(GameLink k, uint serial, out int graphic)
    {
        lock (k.State.Gate)
        {
            graphic = -1;
            if (!k.State.Items.TryGet(serial, out AssistItem? it) && !k.State.Ground.TryGet(serial, out it))
                return 0;
            graphic = it.Graphic;
            return it.Amount;
        }
    }

    // The amount of the other stacks of a graphic where an item lies: in its container, or on its tile.
    static int Beside(GameLink k, uint serial, int graphic, Spot at)
    {
        int n = 0;
        lock (k.State.Gate)
        {
            foreach (AssistItem it in at.Container != 0 ? k.State.Items.Values : k.State.Ground.Values)
            {
                bool there = at.Container != 0 ? it.Container == at.Container : it.X == at.X && it.Y == at.Y;
                n += it.Serial != serial && there && it.Graphic == graphic ? Math.Max(1, it.Amount) : 0;
            }
        }
        return n;
    }

    // A drop or an equip sent: done once the item no longer lies where it lay before its lift (from, null when it was
    // never seen), refused when the server's answer left it there.
    public Outcome Landed(GameLink k, uint serial, Spot? from)
    {
        if (from is null)
            return Outcome.Ok;
        Outcome moved = Wait.Until(m_clock, () => Where(k, serial) != from, AnswerMs, "the drop's answer",
            closed: () => k.Closed, stepMs: LandedStepMs);
        return moved.Done ? Outcome.Ok : Outcome.Refused(PutBack);
    }
}
