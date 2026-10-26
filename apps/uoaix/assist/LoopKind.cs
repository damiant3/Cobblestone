#nullable enable

namespace Uoaix.Assist;

public sealed class LoopKind
{
    // Probe: the delay is the 5 s assumption, so a loop that keeps working tries shorter delays too.
    public string Name = null!;
    public int Skill = -1;
    public int Target; public int Pairs; public int DelayMs; public bool Probe; public string Source = null!;
    // Smith: 1 builds until the pack runs out of ingots, 2 until an exceptional item; the hammer's menu picks are
    // recorded.
    public int Smith;
    // Magery: 1 trains Magery, each cast at the circle the skill can gain on (AssistFeatures.RunMagery).
    public int Magery;
    // Smelt: 1 smelts one ore at a time from a pile on the ground (AssistFeatures.RunSmelt).
    public int Smelt;
    // Tailor: 1 sews from a cloth pile, then from a bolt of the same stack (AssistFeatures.RunTailor).
    public int Tailor;
}
