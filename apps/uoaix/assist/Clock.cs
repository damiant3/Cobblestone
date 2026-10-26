// Time for the Assist (docs/Designs/Active/Apps/AssistTesting.md, instruments): the system clock live, a virtual clock
// under test, so that every timing boundary is tested to the millisecond and no test reads the wall clock.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist;

public interface IClock
{
    DateTime UtcNow
    {
        get;
    }

    void Sleep(int ms);

    // Whether signal was set within ms.
    bool Wait(ManualResetEventSlim signal, int ms);
}

public sealed class SystemClock : IClock
{
    static readonly SystemClock s_instance = new();

    SystemClock()
    {
    }

    public static SystemClock Instance => s_instance;

    public DateTime UtcNow => DateTime.UtcNow;

    public void Sleep(int ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "a sleep is 0 ms or more");
        Thread.Sleep(ms);
    }

    public bool Wait(ManualResetEventSlim signal, int ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "a wait is 0 ms or more");
        return signal.Wait(ms);
    }
}

// A source the virtual clock waits on before it moves time: a pipe holding bytes its reader has not yet taken.
public interface IQuiet
{
    bool IsQuiet
    {
        get;
    }
}

// Discrete-event time. Time moves only when every actor sleeps and every watched source is quiet; then exactly one
// sleeper wakes, the earliest due, the first to sleep among those due together. Every actor therefore runs alone, and
// a run is the same on every repetition.
public sealed class VirtualClock : IClock
{
    sealed class Sleeper
    {
        public Sleeper(DateTime due)
        {
            Due = due;
        }

        public DateTime Due
        {
            get;
        }

        public ManualResetEventSlim Woken { get; } = new(false);
    }

    readonly Lock m_gate = new();
    readonly List<Sleeper> m_sleepers = new();
    readonly List<IQuiet> m_quiet = new();
    DateTime m_now;
    int m_actors;

    public VirtualClock(DateTime start)
    {
        m_now = start;
    }

    public DateTime UtcNow
    {
        get
        {
            lock (m_gate)
                return m_now;
        }
    }

    public int Actors
    {
        get
        {
            lock (m_gate)
                return m_actors;
        }
    }

    public int Sleeping
    {
        get
        {
            lock (m_gate)
                return m_sleepers.Count;
        }
    }

    public void Watch(IQuiet source)
    {
        lock (m_gate)
            m_quiet.Add(source);
    }

    // The calling thread is an actor until Leave: time waits for its sleeps.
    public void Join()
    {
        lock (m_gate)
            m_actors++;
    }

    public void Leave()
    {
        lock (m_gate)
        {
            if (m_actors == 0)
                throw new InvalidOperationException("Leave without Join");
            m_actors--;
        }
        Settle();
    }

    // An actor waiting on something other than the clock (a thread's end) is not one while it waits: time may move.
    public void Outside(Action wait)
    {
        Leave();
        try
        {
            wait();
        }
        finally
        {
            Join();
        }
    }

    public void Sleep(int ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "a sleep is 0 ms or more");
        Sleeper sleeper;
        lock (m_gate)
        {
            sleeper = new Sleeper(m_now.AddMilliseconds(ms));
            m_sleepers.Add(sleeper);
        }
        Settle();
        sleeper.Woken.Wait();
    }

    // A wait on a signal in virtual time: looked at every WaitStepMs, so it ends at most that late after the signal.
    public const int WaitStepMs = 10;

    public bool Wait(ManualResetEventSlim signal, int ms)
    {
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "a wait is 0 ms or more");
        DateTime by = UtcNow.AddMilliseconds(ms);
        while (true)
        {
            if (signal.IsSet)
                return true;
            DateTime now = UtcNow;
            if (now >= by)
                return false;
            Sleep((int)Math.Min(WaitStepMs, Math.Ceiling((by - now).TotalMilliseconds)));
        }
    }

    // Runs body on a new actor thread and returns when it ends; an exception in body is thrown here.
    public void Run(Action body)
    {
        Exception? failed = null;
        Join();
        Thread thread = new(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failed = e;
            }
            finally
            {
                Leave();
            }
        });
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (failed is not null)
            throw new InvalidOperationException("the actor failed: " + failed.Message, failed);
    }

    // Wakes the next sleeper when every actor sleeps and every source is quiet. A source calls this when it turns
    // quiet; a sleep and a leave call it themselves.
    public void Settle()
    {
        lock (m_gate)
        {
            if (m_sleepers.Count == 0 || m_sleepers.Count < m_actors)
                return;
            foreach (IQuiet source in m_quiet)
            {
                if (!source.IsQuiet)
                    return;
            }
            int next = 0;
            for (int i = 1; i < m_sleepers.Count; i++)
            {
                if (m_sleepers[i].Due < m_sleepers[next].Due)
                    next = i;
            }
            Sleeper woken = m_sleepers[next];
            m_sleepers.RemoveAt(next);
            if (woken.Due > m_now)
                m_now = woken.Due;
            woken.Woken.Set();
        }
    }
}
