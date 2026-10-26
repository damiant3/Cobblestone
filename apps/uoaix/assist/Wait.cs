// Waiting for something the server or the game does (docs/Designs/Active/Apps/AssistStructure.md, primitives): one
// loop in place of the deadline loops each step wrote out by hand, on the clock it is given (AssistTesting.md).
#nullable enable
using System;

namespace Uoaix.Assist;

public static class Wait
{
    public const int StepMs = 20;

    // Polls done every stepMs until it holds (Done), ms run out (TimedOut, with what), stopped holds (Stopped) or
    // closed holds (Closed). done is asked first, so a step already done ends at once.
    public static Outcome Until(IClock clock, Func<bool> done, int ms, string what, Func<bool>? stopped = null,
        Func<bool>? closed = null, int stepMs = StepMs)
    {
        DateTime by = clock.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            if (done())
                return Outcome.Ok;
            if (stopped is not null && stopped())
                return Outcome.Stopped();
            if (closed is not null && closed())
                return Outcome.Closed();
            if (clock.UtcNow >= by)
                return Outcome.TimedOut(what + " did not come in " + ms + " ms");
            clock.Sleep(stepMs);
        }
    }

    // Waits until due, ending early only when stopped or closed holds. The time is asked first, so a due already past
    // ends at once, done, whatever stopped says.
    public static Outcome Till(IClock clock, DateTime due, Func<bool>? stopped = null, Func<bool>? closed = null,
        int stepMs = 50)
    {
        while (clock.UtcNow < due)
        {
            if (stopped is not null && stopped())
                return Outcome.Stopped();
            if (closed is not null && closed())
                return Outcome.Closed();
            clock.Sleep(stepMs);
        }
        return Outcome.Ok;
    }

    // Waits out ms whole, ending early only when stopped or closed holds.
    public static Outcome For(IClock clock, int ms, Func<bool>? stopped = null, Func<bool>? closed = null,
        int stepMs = 50)
    {
        Outcome o = Until(clock, () => false, ms, "the wait", stopped, closed, stepMs);
        return o.Ending == Ending.TimedOut ? Outcome.Ok : o;
    }
}
