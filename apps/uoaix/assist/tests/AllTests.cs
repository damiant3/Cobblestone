#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

public static class AllTests
{
    static readonly ImmutableArray<TestCase> s_cases = ClockTests.Cases
        .AddRange(PipeTests.Cases)
        .AddRange(GameLinkTests.Cases)
        .AddRange(WireTests.Cases)
        .AddRange(GameLinkFlowTests.Cases)
        .AddRange(GateTests.Cases)
        .AddRange(RecallTests.Cases)
        .AddRange(JournalTests.Cases)
        .AddRange(KeepRulesTests.Cases)
        .AddRange(BagsTests.Cases)
        .AddRange(MinePlannerTests.Cases)
        .AddRange(ShopDisplayTests.Cases)
        .AddRange(RuneChoiceTests.Cases)
        .AddRange(HouseKeyTests.Cases)
        .AddRange(TimingsTests.Cases)
        .AddRange(AtlasTests.Cases)
        .AddRange(LoadoutTests.Cases)
        .AddRange(LumberTests.Cases)
        .AddRange(CollectionTests.Cases)
        .AddRange(HandsTests.Cases)
        .AddRange(RunePinTests.Cases)
        .AddRange(StashTests.Cases)
        .AddRange(LightTests.Cases)
        .AddRange(PileOrderTests.Cases)
        .AddRange(PlannerTests.Cases)
        .AddRange(VendorWaresTests.Cases)
        .AddRange(VendorCatalogTests.Cases)
        .AddRange(GatherTests.Cases)
        .AddRange(GatherOrderTests.Cases)
        .AddRange(PickupTests.Cases)
        .AddRange(DoorWalkTests.Cases)
        .AddRange(PackRuneTests.Cases)
        .AddRange(PurseTests.Cases)
        .AddRange(KindTests.Cases)
        .AddRange(AssistStateTests.Cases)
        .AddRange(PurseFillTests.Cases)
        .AddRange(VendorBuyTests.Cases)
        .AddRange(KeepLapTests.Cases)
        .AddRange(TailorTitleTests.Cases)
        .AddRange(CircuitTopTests.Cases)
        .AddRange(HomespunWorldTests.Cases)
        .AddRange(HomespunTownsTests.Cases)
        .AddRange(BuyCircuitTests.Cases)
        .AddRange(TaskRunnerTests.Cases)
        .AddRange(FeatherTests.Cases)
        .AddRange(ReagentTests.Cases)
        .AddRange(TailorStepTests.Cases)
        .AddRange(MineFeetTests.Cases)
        .AddRange(PaceTests.Cases)
        .AddRange(SmeltTests.Cases);

    public static ImmutableArray<TestCase> Cases => s_cases;

    public static int Run()
    {
        return TestRunner.Run(s_cases, Console.WriteLine);
    }
}
