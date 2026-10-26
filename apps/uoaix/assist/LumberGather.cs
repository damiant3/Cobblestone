// The lumberjack's gathering (AssistHarvest.md stage 1): logs, carried to Logs to, its settings under lumber.
#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public sealed class LumberGather : Gather
{
    // The logs bladed.m makes (1BDD) and their other facing, as an inclusive range.
    static readonly ImmutableArray<int> s_logs = ImmutableArray.Create(0x1BDD, 0x1BE2);

    // bladed.m Q4YR: the axes that chop, as inclusive ranges.
    static readonly ImmutableArray<int> s_axes = ImmutableArray.Create(0x0F43, 0x0F4E, 0x13AF, 0x13B0, 0x13FA, 0x13FB,
        0x143E,
        0x143F, 0x1442, 0x1443);

    public static bool IsAxe(int graphic)
    {
        for (int i = 0; i + 1 < s_axes.Length; i += 2)
        {
            if (graphic >= s_axes[i] && graphic <= s_axes[i + 1])
                return true;
        }
        return false;
    }

    public override string ToolName => "axe";

    public override bool IsTool(int graphic) => IsAxe(graphic);

    public LumberGather(AssistSettings cfg, Func<string> server)
        : base(cfg, server)
    {
    }

    public static bool IsLog(int graphic) => graphic >= s_logs[0] && graphic <= s_logs[1];

    public override string Key => "lumber";

    public override string PlaceName => "logs";

    public override string PlaceButton => "Logs to";

    public override int CollectAtDefault => 1000;

    public override bool Gathered(int graphic) => IsLog(graphic);

    public override int WeightArt => s_logs[0];

    public override string Title => "Lumber";

    public override string Goods => "logs";
}
