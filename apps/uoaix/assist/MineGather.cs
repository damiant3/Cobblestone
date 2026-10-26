// The cave miner's gathering (AssistHarvest.md's family): ore, carried to Ore to, its settings under mine (Damian,
// 2026-10-25: "lets add to the cave miner circuit the same drop at feet and gather to collection spot stuff we do with
// lumberjack").
#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public sealed class MineGather : Gather
{
    // The 1.25 client's ore piles (0x19B7 to 0x19BA), the arts a dig puts in the pack.
    static readonly ImmutableArray<int> s_ore = ImmutableArray.Create(0x19B7, 0x19BA);

    public MineGather(AssistSettings cfg, Func<string> server)
        : base(cfg, server)
    {
    }

    public static bool IsOre(int graphic) => graphic >= s_ore[0] && graphic <= s_ore[1];

    public override string Key => "mine";

    public override string PlaceName => "ore-to";

    public override string PlaceButton => "Ore to";

    public override int CollectAtDefault => 200;

    public override bool Gathered(int graphic) => IsOre(graphic);

    public override int WeightArt => s_ore[0];

    // Ore is hopped, never carried (Damian, 2026-10-25: "we definitely need auto-bunny hop here to gather the ores"):
    // uo1998 put an 11-ore pile back where it lay when the collection carried it into a 91-stone pack (15:28:40, 2420,91).
    public override bool Carries => false;

    public override string Title => "Mine";

    public override string Goods => "ore";

    public override string ToolName => "shovel";

    public override bool IsTool(int graphic) => AssistFeatures.DigTool(graphic);
}
