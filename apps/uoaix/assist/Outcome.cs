// How a step of the Assist ended (docs/Designs/Active/Apps/AssistStructure.md, primitives): done, or why not, with
// what the server said when it said something. Damian, 2026-10-21: "yes on the Outcome".
#nullable enable

namespace Uoaix.Assist;

public enum Ending
{
    Done,
    Refused,
    TimedOut,
    Stopped,
    Closed,
}

public sealed record Outcome(Ending Ending, string Why, string? Said = null)
{
    static readonly Outcome s_ok = new(Ending.Done, "done");

    public static Outcome Ok => s_ok;

    public bool Done => Ending == Ending.Done;

    public static Outcome Refused(string why, string? said = null)
    {
        return new(Ending.Refused, why, said);
    }

    public static Outcome TimedOut(string why)
    {
        return new(Ending.TimedOut, why);
    }

    public static Outcome Stopped()
    {
        return new(Ending.Stopped, "stopped");
    }

    public static Outcome Closed()
    {
        return new(Ending.Closed, "the connection closed");
    }

    // The reason string the code before Outcome returned: null when done.
    public string? AsReason()
    {
        return Done ? null : ToString();
    }

    public override string ToString()
    {
        return Said is null ? Why : Why + " (\"" + Said + "\")";
    }
}
