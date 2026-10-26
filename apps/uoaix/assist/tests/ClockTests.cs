#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Uoaix.Assist.Tests;

public static class ClockTests
{
    static readonly DateTime s_start = new(2026, 10, 22, 6, 39, 0, DateTimeKind.Utc);

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("clock: system sleep of -1 ms is refused", SystemSleepNegative),
        new TestCase("clock: system sleep of 0 ms returns", SystemSleepZero),
        new TestCase("clock: the system clock reads the wall clock", SystemReadsWallClock),
        new TestCase("clock: virtual time starts where it was set", StartsAtStart),
        new TestCase("clock: virtual sleep of -1 ms is refused", VirtualSleepNegative),
        new TestCase("clock: virtual sleep of 0 ms leaves the time", VirtualSleepZero),
        new TestCase("clock: virtual sleep of 1 ms moves the time 1 ms", VirtualSleepOne),
        new TestCase("clock: virtual sleeps add up exactly", VirtualSleepsAdd),
        new TestCase("clock: the earliest due wakes first", EarliestFirst),
        new TestCase("clock: sleepers due together wake in the order they slept", TiesInOrder),
        new TestCase("clock: a sleeper due 1 ms earlier wakes first though it slept second", OneMsEarlierWins),
        new TestCase("clock: time waits for an actor that is not sleeping", WaitsForRunningActor),
        new TestCase("clock: time waits for a source that is not quiet", WaitsForSource),
        new TestCase("clock: an actor waiting outside the clock lets time move", OutsideLetsTimeMove),
        new TestCase("clock: an actor is one again after its outside wait throws", OutsideRejoinsOnThrow),
        new TestCase("clock: leave without join is refused", LeaveWithoutJoin),
        new TestCase("clock: an actor's exception reaches the caller of Run", RunThrows),
        new TestCase("clock: Run leaves no actor behind", RunLeaves),
        new TestCase("clock wait: a signal already set answers at once", WaitAlreadySet),
        new TestCase("clock wait: a signal set at the last ms is seen", WaitSetAtLastMs),
        new TestCase("clock wait: a signal never set is false at exactly the limit", WaitNeverSet),
        new TestCase("clock wait: a wait of 0 ms answers at once", WaitZero),
        new TestCase("clock wait: a wait of -1 ms is refused on both clocks", WaitNegative),
        new TestCase("clock wait: the system clock waits on the signal itself", SystemWait),
        new TestCase("wait: done at once ends at once", UntilDoneAtOnce),
        new TestCase("wait: a wait not done times out at exactly its limit", UntilTimesOut),
        new TestCase("wait: a stop ends it, before the time", UntilStopped),
        new TestCase("wait: a close ends it", UntilClosed),
        new TestCase("wait: For's timeout is its success, a stop is not", ForOutcomes),
        new TestCase("wait: Till runs to its due; a due past is done at once, a stop with it", TillDue),
        new TestCase("wait: Till ends early on a stop or a closed link, the stop first", TillEnds),
        new TestCase("outcome: done has no reason", OutcomeDone),
        new TestCase("outcome: a refusal carries the server's words", OutcomeRefused),
        new TestCase("outcome: a refusal without words is its reason alone", OutcomeRefusedBare),
        new TestCase("outcome: stopped and closed say so", OutcomeStoppedClosed));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void SystemSleepNegative()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => SystemClock.Instance.Sleep(-1), "Sleep(-1)");
    }

    static void SystemSleepZero()
    {
        SystemClock.Instance.Sleep(0);
    }

    // The system clock's own contract is the wall clock: read between two readings of it.
    static void SystemReadsWallClock()
    {
        DateTime before = DateTime.UtcNow;
        DateTime read = SystemClock.Instance.UtcNow;
        DateTime after = DateTime.UtcNow;
        Check.True(before <= read && read <= after,
            "UtcNow " + read.ToString("O") + " between " + before.ToString("O") + " and " + after.ToString("O"));
    }

    static void StartsAtStart()
    {
        Check.Equal(s_start, new VirtualClock(s_start).UtcNow, "UtcNow");
    }

    static void VirtualSleepNegative()
    {
        VirtualClock clock = new(s_start);
        Check.Throws<ArgumentOutOfRangeException>(() => clock.Sleep(-1), "Sleep(-1)");
        Check.Equal(0, clock.Sleeping, "sleepers after the refusal");
    }

    static void VirtualSleepZero()
    {
        VirtualClock clock = new(s_start);
        clock.Run(() => clock.Sleep(0));
        Check.Equal(s_start, clock.UtcNow, "UtcNow");
    }

    static void VirtualSleepOne()
    {
        VirtualClock clock = new(s_start);
        clock.Run(() => clock.Sleep(1));
        Check.Equal(s_start.AddMilliseconds(1), clock.UtcNow, "UtcNow");
    }

    static void VirtualSleepsAdd()
    {
        VirtualClock clock = new(s_start);
        clock.Run(() =>
        {
            clock.Sleep(853);
            clock.Sleep(1);
            clock.Sleep(146);
        });
        Check.Equal(s_start.AddMilliseconds(1000), clock.UtcNow, "UtcNow");
    }

    // Two actors run under one Run: the outer actor starts the inner one and both sleep.
    static List<string> TwoActors(VirtualClock clock, int firstMs, int secondMs)
    {
        List<string> woke = new();
        Lock gate = new();
        clock.Run(() =>
        {
            clock.Join();
            Thread second = new(() =>
            {
                try
                {
                    clock.Sleep(secondMs);
                    lock (gate)
                        woke.Add("second at " + (clock.UtcNow - s_start).TotalMilliseconds);
                }
                finally
                {
                    clock.Leave();
                }
            });
            second.IsBackground = true;
            second.Start();
            // The inner actor sleeps first; time cannot move meanwhile, as this actor is not asleep.
            while (clock.Sleeping == 0)
                Thread.Yield();
            clock.Sleep(firstMs);
            lock (gate)
                woke.Add("first at " + (clock.UtcNow - s_start).TotalMilliseconds);
            clock.Outside(() => second.Join());
        });
        return woke;
    }

    static void EarliestFirst()
    {
        VirtualClock clock = new(s_start);
        List<string> woke = TwoActors(clock, 20, 10);
        Check.Equal("second at 10,first at 20", string.Join(",", woke), "wake order");
    }

    static void TiesInOrder()
    {
        VirtualClock clock = new(s_start);
        List<string> woke = TwoActors(clock, 10, 10);
        Check.Equal("second at 10,first at 10", string.Join(",", woke), "wake order");
    }

    static void OneMsEarlierWins()
    {
        VirtualClock clock = new(s_start);
        List<string> woke = TwoActors(clock, 10, 11);
        Check.Equal("first at 10,second at 11", string.Join(",", woke), "wake order");
    }

    static void WaitsForRunningActor()
    {
        VirtualClock clock = new(s_start);
        ManualResetEventSlim release = new(false);
        DateTime seenByBusy = DateTime.MinValue;
        clock.Run(() =>
        {
            clock.Join();
            Thread busy = new(() =>
            {
                try
                {
                    release.Wait();
                    seenByBusy = clock.UtcNow;
                }
                finally
                {
                    clock.Leave();
                }
            });
            busy.IsBackground = true;
            busy.Start();
            Thread releaser = new(() =>
            {
                while (clock.Sleeping == 0)
                    Thread.Yield();
                release.Set();
            });
            releaser.IsBackground = true;
            releaser.Start();
            clock.Sleep(50);
            busy.Join();
        });
        Check.Equal(s_start, seenByBusy, "the time the busy actor saw while the sleeper waited");
        Check.Equal(s_start.AddMilliseconds(50), clock.UtcNow, "UtcNow after both");
    }

    sealed class Source : IQuiet
    {
        public volatile bool Quiet;

        public bool IsQuiet => Quiet;
    }

    static void WaitsForSource()
    {
        VirtualClock clock = new(s_start);
        Source source = new();
        clock.Watch(source);
        Thread actor = new(() =>
        {
            clock.Join();
            try
            {
                clock.Sleep(5);
            }
            finally
            {
                clock.Leave();
            }
        });
        actor.IsBackground = true;
        actor.Start();
        while (clock.Sleeping == 0)
            Thread.Yield();
        clock.Settle();
        Check.Equal(s_start, clock.UtcNow, "UtcNow while the source is busy");
        Check.Equal(1, clock.Sleeping, "sleepers while the source is busy");
        source.Quiet = true;
        clock.Settle();
        actor.Join();
        Check.Equal(s_start.AddMilliseconds(5), clock.UtcNow, "UtcNow once the source is quiet");
    }

    static void OutsideLetsTimeMove()
    {
        VirtualClock clock = new(s_start);
        DateTime sleeperWoke = DateTime.MinValue;
        clock.Run(() =>
        {
            clock.Join();
            Thread sleeper = new(() =>
            {
                try
                {
                    clock.Sleep(30);
                    sleeperWoke = clock.UtcNow;
                }
                finally
                {
                    clock.Leave();
                }
            });
            sleeper.IsBackground = true;
            sleeper.Start();
            clock.Outside(() => sleeper.Join());
        });
        Check.Equal(s_start.AddMilliseconds(30), sleeperWoke, "the sleeper's wake");
    }

    static void OutsideRejoinsOnThrow()
    {
        VirtualClock clock = new(s_start);
        clock.Join();
        Check.Throws<FormatException>(() => clock.Outside(() => throw new FormatException("wait failed")), "Outside");
        Check.Equal(1, clock.Actors, "actors after the throw");
        clock.Leave();
    }

    static void LeaveWithoutJoin()
    {
        Check.Throws<InvalidOperationException>(() => new VirtualClock(s_start).Leave(), "Leave");
    }

    static void RunThrows()
    {
        VirtualClock clock = new(s_start);
        InvalidOperationException e = Check.Throws<InvalidOperationException>(() =>
            clock.Run(() => throw new FormatException("inner")), "Run");
        Check.True(e.InnerException is FormatException, "the inner exception kept");
    }

    static void WaitAlreadySet()
    {
        VirtualClock clock = new(s_start);
        bool got = false;
        clock.Run(() => got = clock.Wait(new ManualResetEventSlim(true), 500));
        Check.True(got, "Wait");
        Check.Equal(s_start, clock.UtcNow, "UtcNow");
    }

    static void WaitSetAtLastMs()
    {
        VirtualClock clock = new(s_start);
        ManualResetEventSlim signal = new(false);
        bool got = false;
        clock.Run(() =>
        {
            clock.Join();
            Thread setter = new(() =>
            {
                try
                {
                    clock.Sleep(499);
                    signal.Set();
                }
                finally
                {
                    clock.Leave();
                }
            });
            setter.IsBackground = true;
            setter.Start();
            got = clock.Wait(signal, 500);
            clock.Outside(() => setter.Join());
        });
        Check.True(got, "Wait");
    }

    static void WaitNeverSet()
    {
        VirtualClock clock = new(s_start);
        bool got = true;
        clock.Run(() => got = clock.Wait(new ManualResetEventSlim(false), 500));
        Check.False(got, "Wait");
        Check.Equal(s_start.AddMilliseconds(500), clock.UtcNow, "UtcNow");
    }

    static void WaitZero()
    {
        VirtualClock clock = new(s_start);
        bool got = true;
        clock.Run(() => got = clock.Wait(new ManualResetEventSlim(false), 0));
        Check.False(got, "Wait");
        Check.Equal(s_start, clock.UtcNow, "UtcNow");
    }

    static void WaitNegative()
    {
        Check.Throws<ArgumentOutOfRangeException>(() =>
            new VirtualClock(s_start).Wait(new ManualResetEventSlim(false), -1), "virtual");
        Check.Throws<ArgumentOutOfRangeException>(() => SystemClock.Instance.Wait(new ManualResetEventSlim(false), -1),
            "system");
    }

    static void SystemWait()
    {
        Check.True(SystemClock.Instance.Wait(new ManualResetEventSlim(true), 0), "a set signal");
        Check.False(SystemClock.Instance.Wait(new ManualResetEventSlim(false), 0), "an unset signal");
    }

    static void UntilDoneAtOnce()
    {
        VirtualClock clock = new(s_start);
        Outcome o = Outcome.Stopped();
        clock.Run(() => o = Wait.Until(clock, () => true, 500, "it"));
        Check.Equal(Ending.Done, o.Ending, "Ending");
        Check.Equal(s_start, clock.UtcNow, "UtcNow");
    }

    static void UntilTimesOut()
    {
        VirtualClock clock = new(s_start);
        Outcome o = Outcome.Ok;
        clock.Run(() => o = Wait.Until(clock, () => false, 500, "the gump"));
        Check.Equal(Ending.TimedOut, o.Ending, "Ending");
        Check.Equal("the gump did not come in 500 ms", o.Why, "Why");
        Check.Equal(s_start.AddMilliseconds(500), clock.UtcNow, "UtcNow");
    }

    static void UntilStopped()
    {
        VirtualClock clock = new(s_start);
        Outcome o = Outcome.Ok;
        int asked = 0;
        clock.Run(() => o = Wait.Until(clock, () => false, 500, "it", () => ++asked > 2));
        Check.Equal(Ending.Stopped, o.Ending, "Ending");
        Check.Equal(s_start.AddMilliseconds(2 * Wait.StepMs), clock.UtcNow, "UtcNow at the stop");
    }

    static void UntilClosed()
    {
        VirtualClock clock = new(s_start);
        Outcome o = Outcome.Ok;
        clock.Run(() => o = Wait.Until(clock, () => false, 500, "it", null, () => true));
        Check.Equal(Ending.Closed, o.Ending, "Ending");
        Outcome open = Outcome.Ok;
        clock.Run(() => open = Wait.Until(clock, () => false, 100, "it", null, () => false));
        Check.Equal(Ending.TimedOut, open.Ending, "a link still open");
    }

    static void ForOutcomes()
    {
        VirtualClock clock = new(s_start);
        Outcome whole = Outcome.Stopped();
        Outcome stopped = Outcome.Ok;
        clock.Run(() =>
        {
            whole = Wait.For(clock, 300);
            stopped = Wait.For(clock, 300, () => true);
        });
        Check.Equal(Ending.Done, whole.Ending, "a wait out whole");
        Check.Equal(Ending.Stopped, stopped.Ending, "a stopped wait");
        Check.Equal(s_start.AddMilliseconds(300), clock.UtcNow, "UtcNow");
    }

    static void TillDue()
    {
        VirtualClock clock = new(s_start);
        Outcome whole = Outcome.Stopped();
        Outcome past = Outcome.Stopped();
        clock.Run(() =>
        {
            whole = Wait.Till(clock, s_start.AddMilliseconds(300));
            past = Wait.Till(clock, s_start, () => true, () => true);
        });
        Check.Equal(Ending.Done, whole.Ending, "a wait to its due");
        Check.Equal(s_start.AddMilliseconds(300), clock.UtcNow, "UtcNow at the due");
        Check.Equal(Ending.Done, past.Ending, "a due past, stopped and closed asked nothing");
    }

    static void TillEnds()
    {
        VirtualClock clock = new(s_start);
        Outcome stopped = Outcome.Ok;
        Outcome closed = Outcome.Ok;
        int asked = 0;
        clock.Run(() => stopped = Wait.Till(clock, s_start.AddMilliseconds(500), () => ++asked > 2, () => false));
        Check.Equal(Ending.Stopped, stopped.Ending, "a stop");
        Check.Equal(s_start.AddMilliseconds(100), clock.UtcNow, "UtcNow at the stop, two polls of 50 ms in");
        clock.Run(() => closed = Wait.Till(clock, clock.UtcNow.AddMilliseconds(500), () => false, () => true));
        Check.Equal(Ending.Closed, closed.Ending, "a closed link");
        Outcome stop = Outcome.Ok;
        clock.Run(() => stop = Wait.Till(clock, clock.UtcNow.AddMilliseconds(500), () => true, () => true));
        Check.Equal(Ending.Stopped, stop.Ending, "a stop before a close");
    }

    static void OutcomeDone()
    {
        Check.True(Outcome.Ok.Done, "Done");
        Check.Null(Outcome.Ok.AsReason(), "AsReason");
    }

    static void OutcomeRefused()
    {
        Outcome o = Outcome.Refused("the recall did not move you", "You must wait to perform another action.");
        Check.False(o.Done, "Done");
        Check.Equal(Ending.Refused, o.Ending, "Ending");
        Check.Equal("the recall did not move you (\"You must wait to perform another action.\")", o.AsReason(),
            "AsReason");
    }

    static void OutcomeRefusedBare()
    {
        Check.Equal("no rune", Outcome.Refused("no rune").AsReason(), "AsReason");
    }

    static void OutcomeStoppedClosed()
    {
        Check.Equal("stopped", Outcome.Stopped().AsReason(), "Stopped");
        Check.Equal("the connection closed", Outcome.Closed().AsReason(), "Closed");
    }

    static void RunLeaves()
    {
        VirtualClock clock = new(s_start);
        clock.Run(() => clock.Sleep(3));
        Check.Equal(0, clock.Actors, "actors after Run");
        Check.Equal(0, clock.Sleeping, "sleepers after Run");
    }
}
