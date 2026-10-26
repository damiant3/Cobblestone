// The task contract (docs/Designs/Active/Apps/AssistStructure.md, stage 3): every task is an ITask, run by a TaskRunner
// on a thread of its own with one stop, its TaskContext's.
#nullable enable
using System;
using System.Threading;

namespace Uoaix.Assist;

public interface ITask
{
    string Name
    {
        get;
    }

    TaskTimings Timings
    {
        get;
    }

    // Why the task ended; it stops at its next step once the context is stopped.
    string Run(TaskContext context);
}

public sealed class TaskContext
{
    readonly CancellationTokenSource m_stop = new();

    public TaskContext(TaskTimings timings)
    {
        Timings = timings;
    }

    public TaskTimings Timings
    {
        get;
    }

    public CancellationToken Token => m_stop.Token;

    public bool Stopped => m_stop.IsCancellationRequested;

    public void Stop()
    {
        m_stop.Cancel();
    }
}

// One task at a time: Start refuses while one runs; Stop stops the running one's context.
public sealed class TaskRunner
{
    sealed record Started(ITask Task, TaskContext Context, Thread Thread);

    readonly Lock m_gate = new();
    readonly Func<Action, Thread> m_newThread;
    Started? m_run;

    // newThread makes the task's thread, not yet started.
    public TaskRunner(Func<Action, Thread> newThread)
    {
        m_newThread = newThread;
    }

    // The running task, or null. The caller holds m_gate.
    Started? Live()
    {
        return m_run is not null && m_run.Thread.IsAlive ? m_run : null;
    }

    public bool Running
    {
        get
        {
            lock (m_gate)
                return Live() is not null;
        }
    }

    // The running task's name, or null.
    public string? Current
    {
        get
        {
            lock (m_gate)
            {
                Started? r = Live();
                return r is null ? null : r.Task.Name;
            }
        }
    }

    // Whether the running task was asked to stop; false with none running.
    public bool Stopped
    {
        get
        {
            lock (m_gate)
            {
                Started? r = Live();
                return r is not null && r.Context.Stopped;
            }
        }
    }

    string? m_lastEnd;

    // Why the last task ended, or null while none has.
    public string? LastEnd
    {
        get
        {
            lock (m_gate)
                return m_lastEnd;
        }
    }

    // Whether thread is the running task's.
    public bool Runs(Thread thread)
    {
        lock (m_gate)
        {
            Started? r = Live();
            return r is not null && r.Thread == thread;
        }
    }

    // Null, or why the task cannot start.
    public string? Start(ITask task)
    {
        lock (m_gate)
        {
            Started? r = Live();
            if (r is not null)
                return r.Task.Name + " is running";
            TaskContext context = new(task.Timings);
            Thread thread = m_newThread(() =>
            {
                string why;
                try
                {
                    why = task.Run(context);
                }
                catch (Exception e)
                {
                    why = "failed: " + e.GetType().Name + " " + e.Message;
                }
                lock (m_gate)
                    m_lastEnd = why;
            });
            thread.IsBackground = true;
            m_run = new Started(task, context, thread);
            thread.Start();
            return null;
        }
    }

    // Stops the running task at its next step; false with none running.
    public bool Stop()
    {
        lock (m_gate)
        {
            Started? r = Live();
            if (r is null)
                return false;
            r.Context.Stop();
            return true;
        }
    }
}
