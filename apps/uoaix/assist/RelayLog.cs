// Where a game link writes its lines (docs/Designs/Active/Apps/AssistTesting.md): the relay's log file live
// (AssistLog), a list in memory under test.
#nullable enable

namespace Uoaix.Assist;

public interface IRelayLog
{
    void Line(int id, string dir, string text);

    // The recent lines kept whole in a crash file; its path, or null when it could not be written.
    string? Crash(string why);
}
