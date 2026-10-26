#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The task contract (TaskContract.cs, AssistStructure.md stage 3): one task at a time, its one stop, its end.
public static class TaskRunnerTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("task: a task runs on its own thread, its end kept; a second is refused while it runs", RunsOne),
        new TestCase("task: a stop reaches the running task's context, and with none running stops nothing", Stops),
        new TestCase("task: a task that throws ends with the failure, and the next one starts", Throws),
        new TestCase("loop: a skill loop runs on the loops' runner, a second is refused, its stop ends it, and the "
        + "next starts fresh", SkillLoop),
        new TestCase("weave: mill, spin and weave runs on its runner and ends with its reason, no station in reach",
        WeaveNoStation));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static readonly TaskTimings s_timings = new("test", "", ImmutableArray<string>.Empty);

    sealed class Gated : ITask
    {
        public readonly ManualResetEventSlim Go = new(false);
        public readonly ManualResetEventSlim Began = new(false);
        public Thread? On;
        public TaskContext? Context;
        public Exception? Throw;

        public string Name => "gated";

        public TaskTimings Timings => s_timings;

        public string Run(TaskContext context)
        {
            On = Thread.CurrentThread;
            Context = context;
            Began.Set();
            Go.Wait();
            if (Throw is not null)
                throw Throw;
            return context.Stopped ? "stopped" : "done";
        }
    }

    static TaskRunner Runner(out Func<Thread?> last)
    {
        Thread? made = null;
        last = () => made;
        return new TaskRunner(run =>
        {
            made = new Thread(() => run());
            return made;
        });
    }

    static void Ended(TaskRunner r, Func<Thread?> last)
    {
        Thread? t = last();
        Check.True(t is not null && t.Join(5000), "the task's thread ended");
        Check.False(r.Running, "no longer running");
    }

    static void RunsOne()
    {
        TaskRunner r = Runner(out Func<Thread?> last);
        Check.Null(r.LastEnd, "no end yet");
        Check.Null(r.Current, "nothing running");
        Gated a = new();
        Check.Null(r.Start(a), "Start");
        Check.True(a.Began.Wait(5000), "began");
        Check.True(r.Running, "running");
        Check.Equal("gated", r.Current, "its name");
        Check.True(r.Runs(a.On!), "its thread");
        Check.False(r.Runs(Thread.CurrentThread), "not this one");
        Check.True(a.On!.IsBackground, "a background thread");
        Check.Equal("gated is running", r.Start(new Gated()), "a second refused");
        Check.True(ReferenceEquals(s_timings, a.Context!.Timings), "the task's timings in its context");
        a.Go.Set();
        Ended(r, last);
        Check.Equal("done", r.LastEnd, "its end");
        Check.Null(r.Current, "nothing running after");
        Check.False(r.Runs(a.On!), "its thread no longer the runner's");
    }

    static void Stops()
    {
        TaskRunner r = Runner(out Func<Thread?> last);
        Check.False(r.Stop(), "none running");
        Check.False(r.Stopped, "nothing stopped");
        Gated a = new();
        Check.Null(r.Start(a), "Start");
        Check.True(a.Began.Wait(5000), "began");
        Check.False(r.Stopped, "not stopped yet");
        Check.False(a.Context!.Token.IsCancellationRequested, "its token clear");
        Check.True(r.Stop(), "stopped");
        Check.True(r.Stopped && a.Context.Stopped && a.Context.Token.IsCancellationRequested, "the context's stop");
        a.Go.Set();
        Ended(r, last);
        Check.Equal("stopped", r.LastEnd, "its end");
        Check.False(r.Stopped, "with none running nothing is stopped");
        Gated b = new();
        Check.Null(r.Start(b), "the next");
        Check.True(b.Began.Wait(5000), "began");
        Check.False(b.Context!.Stopped, "a fresh stop");
        b.Go.Set();
        Ended(r, last);
    }

    static int SkillUses(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x12 && p[3] == 0x24 ? 1 : 0;
        return n;
    }

    // A skill loop with no target (Hiding, skill 21, 10 s between uses) against a shard that answers nothing.
    static void SkillLoop()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            LoopKind l = new()
            {
                Name = "Hiding",
                Skill = 21,
                DelayMs = 10000
            };
            Check.Null(f.Start(l, 5, 0, null!), "Start");
            for (int i = 0; i < 600 && SkillUses(s) == 0; i++)
                s.Clock.Sleep(100);
            Check.Equal(1, SkillUses(s), "the first use");
            string? second = f.Start(l, 5, 0, null!);
            Check.True(second is not null && second.StartsWith("a loop is running"), "a second refused: " + second);
            f.Stop("the test");
            for (int i = 0; i < 600 && f.Status != "Loop Hiding stopped"; i++)
                s.Clock.Sleep(100);
            Check.Equal("Loop Hiding stopped", f.Status, "ended on the stop");
            s.Clock.Sleep(30000);
            Check.Equal(1, SkillUses(s), "no use after the stop");
            Check.Null(f.Start(l, 1, 0, null!), "the next starts");
            for (int i = 0; i < 600 && !f.Status.StartsWith("Loop Hiding done"); i++)
                s.Clock.Sleep(100);
            Check.Equal("Loop Hiding done: 1 rounds", f.Status, "its stop fresh: it ran its round");
            Check.Equal(2, SkillUses(s), "its use");
        });
    }

    static void WeaveNoStation()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(0x42BE4F7C, 0x0E75, 0x15, Packets.Player));
            s.Clock.Sleep(1);
            Check.Null(f.WorkUserToggle(), "WorkUserToggle");
            for (int i = 0; i < 600 && f.WorkRunning; i++)
                s.Clock.Sleep(100);
            Check.False(f.WorkRunning, "ended");
            Check.True(f.WorkEnd.StartsWith("no flour mill, spinning wheel or loom within "),
                "its reason: " + f.WorkEnd);
        });
    }

    static void Throws()
    {
        TaskRunner r = Runner(out Func<Thread?> last);
        Gated a = new()
        {
            Throw = new InvalidOperationException("broken")
        };
        Check.Null(r.Start(a), "Start");
        a.Go.Set();
        Ended(r, last);
        Check.Equal("failed: InvalidOperationException broken", r.LastEnd, "the failure");
        Gated b = new();
        Check.Null(r.Start(b), "the next starts");
        b.Go.Set();
        Ended(r, last);
        Check.Equal("done", r.LastEnd, "its end");
    }
}
