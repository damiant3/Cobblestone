// The feather harvest's gathering (AssistHarvest.md stage 3): feathers, carried to Feathers to, its settings under
// feathers; the birds it hunts and the blades that carve them.
#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public sealed class FeatherGather : Gather
{
    // The feathers bladed.m makes (1BD1) and their other facing.
    static readonly ImmutableArray<int> s_feathers = ImmutableArray.Create(0x1BD1, 0x1BD2);

    // The arts uo1998 gives bladed.m (rundir scripts.wombat, each <art>.m "inherits bladed"), as inclusive ranges:
    // knives, cleavers, skinning knives, every axe, daggers, swords, spears, katanas, kryss, halberds, cutlasses.
    static readonly ImmutableArray<int> s_blades = ImmutableArray.Create(0x09A5, 0x09A6, 0x09F6, 0x09F7, 0x0EC2, 0x0EC5,
        0x0F43, 0x0F4E, 0x0F51, 0x0F52, 0x0F5E, 0x0F63, 0x13AF, 0x13B0, 0x13B5, 0x13BA, 0x13F6, 0x13F7, 0x13FA, 0x13FB,
        0x13FE, 0x1403, 0x143E, 0x1443);

    // Birds by body (the shard's spawn data, WorldSpawnData.codex, on the client's bodies): 5 the eagle; 6 bird,
    // canary, crow, falcon, hawk, raven and tropical bird; 0xD0 the chicken (seen on uo1998, 2026-10-23). The harpies
    // (30) are threats, never game (Damian, 2026-10-23: "you can target any other birds too, they are all the same
    // afaik").
    static readonly ImmutableArray<int> s_birds = ImmutableArray.Create(0x05, 0x06, 0xD0);

    // The meat bladed.m makes from a corpse's meat bank: a cut of raw ribs.
    public const int MeatArt = 0x09F1;

    public FeatherGather(AssistSettings cfg, Func<string> server)
        : base(cfg, server)
    {
    }

    static bool InRanges(ImmutableArray<int> ranges, int graphic)
    {
        for (int i = 0; i + 1 < ranges.Length; i += 2)
        {
            if (graphic >= ranges[i] && graphic <= ranges[i + 1])
                return true;
        }
        return false;
    }

    public static bool IsFeather(int graphic) => graphic >= s_feathers[0] && graphic <= s_feathers[1];

    public static bool IsBlade(int graphic) => InRanges(s_blades, graphic);

    public static bool IsBird(int body) => s_birds.Contains(body);

    public static bool IsMeat(int graphic) => graphic == MeatArt;

    public override string Key => "feathers";

    public override string PlaceName => "place";

    public override string PlaceButton => "Feathers to";

    public override int CollectAtDefault => 500;

    public override bool Gathered(int graphic) => IsFeather(graphic);

    public override int WeightArt => s_feathers[0];

    public override string Title => "Feathers";

    public override string Goods => "feathers";

    public override string ToolName => "blade";

    public override bool IsTool(int graphic) => IsBlade(graphic);
}
