#nullable enable
using System.Collections.Immutable;
using System;
using System.IO;

namespace Uoaix.Assist.Tests;

// Doors on a walk (AssistFeatures.WalkTo, AssistTesting.md T3) against a shard that answers every step (StepShard): a
// wall across the way at x 105 with a doorway at 105,100, the player walking from 100,100 to 110,100.
public static class DoorWalkTests
{
    const uint Leaf = 0x40001F3C;
    const uint Holder = 0x0000BEEF;
    const int WallX = 105;
    const int DoorY = 100;
    const int GapY = 110;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("doors: a set art reads shut at an even step from its base, open at an odd one, and nothing past "
            + "the set", ArtSets),
        new TestCase("doors: a shut door on the route is opened before the step onto it, no step refused", ShutAhead),
        new TestCase("doors: an open door on the route is walked through and never used", OpenAhead),
        new TestCase("doors: a shut door ahead that does not swing is used once; with no way round there is no way",
        NoSwingNoWay),
        new TestCase("doors: a shut door ahead that does not swing is used once, and the walk goes round by the gap",
        NoSwingGap),
        new TestCase("doors: someone beside a shut door ahead holds nothing: it is opened and walked through (Tracy, "
        + "2026-10-24)", HeldAhead),
        new TestCase("doors: with two steps in flight the door ahead waits for their answers, then opens, no step "
        + "refused", ShutAheadInFlight),
        new TestCase("doors: the player's own house vendor beside a door holds nothing; the door is used",
        OwnVendorAhead),
        new TestCase("doors: a shut door on the floor above the doorway is never used", OtherFloor),
        new TestCase("doors: \"You must wait\" asks for the use again, and the second use swings it", WaitAsksAgain),
        new TestCase("doors: a door its use deletes is no swing, never used again; with no way round there is no way",
        Vanished),
        new TestCase("doors: a door art in no set is found by its refused step, opened, and the step taken",
        UnknownArt),
        new TestCase("doors: a door whose art misreads it costs one refused step, the refusal's use opening it",
        Misread),
        new TestCase("doors: an open leaf refused on its tile is never used, and its tile is no wall to the next walk",
        LeafNoWall),
        new TestCase("doors: a door the walk used, refused on its tile again, leaves no wall for the next walk",
        UsedNoWall),
        new TestCase("doors: a refused tile with no door is a wall to the next walk", RefusedIsWall),
        new TestCase("doors: a private registered house's door, its multi unread, is never used; the walk goes round",
        HouseDoorByRecord),
        new TestCase("doors: a door the walk opened that shuts by itself before the step is opened again and walked "
        + "through", ShutsAgain));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // The wall at WallX from y 0 to the map's edge, open at the doorway and at GapY when gap.
    static TestMap Wall(bool gap)
    {
        TestMap map = new TestMap().WallLine(WallX, 0, WallX, DoorY - 1);
        if (!gap)
            return map.WallLine(WallX, DoorY + 1, WallX, TestMap.Size - 1);
        return map.WallLine(WallX, DoorY + 1, WallX, GapY - 1).WallLine(WallX, GapY + 1, WallX, TestMap.Size - 1);
    }

    // Every case of a run shares one settings file (RecallTests.WithFeatures): no house is set up, so no door is a
    // locked house's and no mobile the house vendor.
    static void Walk(TestMap map, Action<FakeShard, AssistFeatures, StepShard> body)
    {
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            f.Cfg.Set(f.Server + "house.vendor", "");
            f.Cfg.Set(f.Server + "house.locked", "off");
            body(s, f, new StepShard(s, map, 100, DoorY));
        }));
    }

    static string? To(AssistFeatures f, int x, int y) => f.WalkTo(x, y, 0, false, AssistFeatures.AnyFloor, false);

    static void Arrived(StepShard w, int x, int y)
    {
        Check.Equal(x * 65536 + y, w.X * 65536 + w.Y, "the player at " + x + "," + y + ", not " + w.X + "," + w.Y);
    }

    static void ArtSets()
    {
        Check.Equal(null, AssistFeatures.DoorShut(0x00E7), "below the first set");
        Check.Equal(true, AssistFeatures.DoorShut(0x00E8), "the base");
        Check.Equal(false, AssistFeatures.DoorShut(0x00E9), "one past the base");
        Check.Equal(false, AssistFeatures.DoorShut(0x00F7), "the set's last art");
        Check.Equal(null, AssistFeatures.DoorShut(0x00F8), "one past the set");
        Check.Equal(true, AssistFeatures.DoorShut(0x06A5), "uo1998's wooden door shut");
        Check.Equal(false, AssistFeatures.DoorShut(0x06A6), "uo1998's wooden door open");
        Check.Equal(true, AssistFeatures.DoorShut(0x0839), "a set starting at an odd art");
        Check.Equal(false, AssistFeatures.DoorShut(0x083A), "one past it");
        Check.Equal(null, AssistFeatures.DoorShut(0x190D), "below the pair");
        Check.Equal(true, AssistFeatures.DoorShut(0x190E), "the pair shut");
        Check.Equal(false, AssistFeatures.DoorShut(0x190F), "the pair open");
        Check.Equal(null, AssistFeatures.DoorShut(0x1910), "past the pair");
        Check.Equal(false, AssistFeatures.DoorShut(0x1FFC), "the last set's last art");
        Check.Equal(null, AssistFeatures.DoorShut(0x1FFD), "past the last set");
    }

    // A mobile 10 tiles off holds no door.
    static void ShutAhead()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            s.ServerSends(Packets.Mobile(Holder, WallX, DoorY - 10));
            s.Clock.Sleep(1);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    static void OpenAhead()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorOpen, WallX, DoorY, false);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(0, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    static void NoSwingNoWay()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true, swings: false);
            string? why = To(f, 110, DoorY);
            Check.True(why is not null && why.StartsWith("no way from"), "the walk: " + why);
            Check.Equal(1, w.Uses(Leaf), "uses (L-FUTILITY)");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    static void NoSwingGap()
    {
        Walk(Wall(true), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true, swings: false);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses (L-FUTILITY)");
            Check.Equal(1, w.Entered(WallX, GapY), "through the gap");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    static void HeldAhead()
    {
        Walk(Wall(true), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            s.ServerSends(Packets.Mobile(Holder, WallX + 1, DoorY + 1));
            s.Clock.Sleep(1);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses");
            Check.Equal(1, w.Entered(WallX, DoorY), "through the door");
            Check.Equal(0, w.Entered(WallX, GapY), "not round by the gap");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    // A door the walk used, its tile refused once more: no wall to the next walk, which opens the door again and goes
    // through.
    static void UsedNoWall()
    {
        Walk(Wall(true), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            w.RefuseOnce(WallX, DoorY);
            Check.Null(To(f, 110, DoorY), "the first walk");
            Arrived(w, 110, DoorY);
            Check.Equal(0, w.Entered(WallX, DoorY), "the first walk not through the door");
            Check.Null(To(f, 100, DoorY), "the walk back");
            Arrived(w, 100, DoorY);
            Check.Equal(1, w.Entered(WallX, DoorY), "the walk back through the door");
        });
    }

    // The steps answered late, so two are in flight when the door comes next: the use waits for both answers.
    // No gap: the door is the only way. An art in no set, so the refused step opens it (the walk's opened set, as a
    // door found by its refusal is); then it has shut by itself when the step onto it comes (Britain, Incantations &
    // Enchantments, 2026-10-24 14:57:22: read as people in the way, pushes and a stamina rest).
    static void ShutsAgain()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.Door, WallX, DoorY, true, shutsOnStep: true);
            string? why = To(f, 110, DoorY);
            string notes;
            lock (f.Notes)
                notes = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Walk:")));
            Check.Null(why, "the walk (uses " + w.Uses(Leaf) + ", refused " + w.Refusals + "): " + notes);
            Arrived(w, 110, DoorY);
            Check.Equal(2, w.Uses(Leaf), "opened, shut by itself, opened again");
            Check.Equal(1, w.Entered(WallX, DoorY), "through the door");
        });
    }

    static void ShutAheadInFlight()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.AnswerMs = 1000;
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    // The player's own house vendor beside the door holds nothing (Damian, 2026-10-20: "rex is my vendor").
    static void OwnVendorAhead()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            f.Cfg.Set(f.Server + "house.vendor", Holder.ToString("X"));
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            s.ServerSends(Packets.Mobile(Holder, WallX + 1, DoorY + 1));
            s.Clock.Sleep(1);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    // A shut door 20 above, on the floor over the doorway, bars nothing below and is never used.
    static void OtherFloor()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true, z: 20);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(0, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    // "You must wait" asks for the use again (L-FUTILITY's one repeat): used twice, swung the second time.
    static void WaitAsksAgain()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true, waitFirst: true);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(2, w.Uses(Leaf), "uses");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    // A door deleted by its use is no swing: the walk does not use it again, and with no way round there is no way.
    static void Vanished()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true, vanishes: true);
            string? why = To(f, 110, DoorY);
            Check.True(why is not null && why.StartsWith("no way from"), "the walk: " + why);
            Check.False(w.Standing(Leaf), "the door gone");
            Check.Equal(0, w.Refusals, "refused steps");
        });
    }

    static void UnknownArt()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.Door, WallX, DoorY, true);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "uses");
            Check.Equal(1, w.Refusals, "refused steps");
        });
    }

    // The art reads shut and the door stands open: the use ahead shuts it, the step is refused, and the refusal's use
    // opens it again.
    static void Misread()
    {
        Walk(Wall(false), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, false);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(2, w.Uses(Leaf), "uses");
            Check.Equal(1, w.Refusals, "refused steps");
        });
    }

    static void LeafNoWall()
    {
        Walk(Wall(true), (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorOpen, WallX, DoorY, false);
            w.RefuseOnce(WallX, DoorY);
            Check.Null(To(f, 110, DoorY), "the first walk");
            Arrived(w, 110, DoorY);
            Check.Equal(0, w.Uses(Leaf), "uses");
            Check.Equal(1, w.Refusals, "refused steps");
            Check.Equal(0, w.Entered(WallX, DoorY), "the first walk not through the door");
            Check.Null(To(f, 100, DoorY), "the walk back");
            Arrived(w, 100, DoorY);
            Check.Equal(1, w.Entered(WallX, DoorY), "the walk back through the door");
            Check.Equal(0, w.Uses(Leaf), "uses on the way back");
        });
    }

    // The houses file holding lines for the body's run (removed after: every other case runs with no house); the
    // features read it at their first look, so each arm is a walk of its own.
    static void WithHouses(TestMap map, string[] lines, Action<FakeShard, AssistFeatures, StepShard> body)
    {
        Walk(map, (s, f, w) =>
        {
            string data = Path.GetDirectoryName(AssistRelay.SettingsPath)!;
            string safe = f.Server.TrimEnd('.').Replace(':', '-').Replace(',', '-');
            string path = Path.Combine(data, "houses-" + safe + ".txt");
            File.WriteAllLines(path, lines);
            try
            {
                body(s, f, w);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    static string HouseLine(int x, int y, string kind) => "H\tTest House\t" + x + "\t" + y + "\t1\t2\t" + kind + "\t";

    // The doorway's door is a registered private house's, as the Moonglow house's door at 22:50:54: never used, whether
    // the house resolves from its multi or only by the record; a public house's door, and a private house's far off,
    // leave the door to the walk.
    static void HouseDoorByRecord()
    {
        WithHouses(Wall(true), new[] { HouseLine(WallX, DoorY, "private") }, (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(0, w.Uses(Leaf), "uses");
            Check.Equal(1, w.Entered(WallX, GapY), "through the gap");
            Check.True(f.Notes.Exists(n => n.Contains("its door refused by the house record")), "noted");
        });
        const uint House = 0x40000002;
        WithHouses(Wall(true).MultiDoor(0x40, 0, 0), new[] { HouseLine(WallX, DoorY, "private") }, (s, f, w) =>
        {
            try
            {
                UoMap.PutMulti(House, 0x40, WallX, DoorY, 0);
                w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
                Check.Null(To(f, 110, DoorY), "the walk");
                Arrived(w, 110, DoorY);
                Check.Equal(0, w.Uses(Leaf), "uses, the house resolved");
                Check.False(f.Notes.Exists(n => n.Contains("did not resolve from its multi")), "resolved");
            }
            finally
            {
                UoMap.DropMulti(House);
            }
        });
        WithHouses(Wall(false), new[] { HouseLine(WallX, DoorY, "public"), HouseLine(WallX, DoorY + 40, "private") },
            (s, f, w) =>
        {
            w.Door(Leaf, TestMap.DoorShut, WallX, DoorY, true);
            Check.Null(To(f, 110, DoorY), "the walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Uses(Leaf), "a public house's door used");
        });
    }

    // The control of LeafNoWall: the same refusal with no door, and the walk back goes round by the gap.
    static void RefusedIsWall()
    {
        Walk(Wall(true), (s, f, w) =>
        {
            w.RefuseOnce(WallX, DoorY);
            Check.Null(To(f, 110, DoorY), "the first walk");
            Arrived(w, 110, DoorY);
            Check.Equal(1, w.Refusals, "refused steps");
            Check.Null(To(f, 100, DoorY), "the walk back");
            Arrived(w, 100, DoorY);
            Check.Equal(0, w.Entered(WallX, DoorY), "never through the remembered wall");
            Check.Equal(2, w.Entered(WallX, GapY), "through the gap both ways");
        });
    }
}
