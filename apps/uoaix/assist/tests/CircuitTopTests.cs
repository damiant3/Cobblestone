#nullable enable
using System.Collections.Immutable;
using System;
using System.IO;

namespace Uoaix.Assist.Tests;

// The tailoring circuit run whole, through its start (CircuitGo), never ending of itself (Damian, 2026-10-24: "this
// circuit should be able to run continuously, forever, with no stops. every single terminal condition should have a
// reset back tot the top of the circuit"; AssistFeatures.CircuitTop).
public static class CircuitTopTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x40290F10;
    const uint Rune = 0x40290F11;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("top: a round with nothing to do goes back to the top after two minutes, again and again, the "
            + "shop loop run each time, no bank stop with no task; a stop ends it", NothingToDo),
        new TestCase("top: unit rules: only a stop, takeover, closed link, fail-safe or death ends; the rest, a "
        + "stall, home dry, a silence or an error go back to the top after two minutes", Rules),
        new TestCase("top: a blocked rune is not cast for a restock, then may be; homespun's pass marks last a "
        + "restock, the plain circuit's a day; home not reached is home dry", Marks));

    static void Marks()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint blocked = 0x45C8C04E;
            Check.False(f.RuneBlocked(blocked), "not blocked before");
            f.BlockRune(blocked);
            Check.True(f.RuneBlocked(blocked), "blocked");
            s.Clock.Sleep(AssistFeatures.RestockMinutes * 60000 - 1000);
            Check.True(f.RuneBlocked(blocked), "still blocked a second before the restock");
            s.Clock.Sleep(2000);
            Check.False(f.RuneBlocked(blocked), "free after the restock");
            bool was = f.Homespun;
            f.Homespun = true;
            Check.Equal(AssistFeatures.RestockSpan, f.PassSpan, "homespun: a restock");
            f.Homespun = false;
            Check.Equal(TimeSpan.FromDays(1), f.PassSpan, "the plain circuit: a day");
            f.Homespun = was;
        });
        Check.True(AssistFeatures.IsHomeDry(AssistFeatures.HomeDry + " (home not reached: the landing is blocked)"),
            "home not reached");
        Check.False(AssistFeatures.IsHomeDry("homespun, sewing: no kit"), "a sewing failure is not dry");
        Check.True(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry + AssistFeatures.HomeEmptyNote + ")"),
            "nothing in the house: home known dry");
        Check.True(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry + AssistFeatures.HomeWasDryNote),
            "known dry, said again: still dry");
        Check.False(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry + AssistFeatures.HomeNoKitNote),
            "no kit: home not known dry");
        Check.False(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry + " (home not reached: the landing is blocked)"),
            "home not reached: not known dry");
        Check.False(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry
            + " (no kit and no thread to take home; Gold 10)"), "not gone home: not known dry");
        Check.False(AssistFeatures.MarksHomeDry(AssistFeatures.HomeDry + AssistFeatures.HomeFullNote
            + "76 shirts carried)"), "a full pack: home not known dry");
        Check.False(AssistFeatures.MarksHomeDry("homespun, sewing: no kit"), "a sewing failure: not dry");
        Check.True(AssistFeatures.HomeTask(10, 78, 0), "thread to store, shirts or not");
        Check.True(AssistFeatures.HomeTask(0, 0, 2), "no shirts and a kit: sew from the pile");
        Check.False(AssistFeatures.HomeTask(0, 0, 0), "no shirts, no kit, no thread: no task");
        Check.False(AssistFeatures.HomeTask(0, 78, 2), "shirts to sell and nothing to store: the shops, not home");
    }

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Rules()
    {
        Check.True(AssistFeatures.TopEnds("stopped"), "a stop");
        Check.True(AssistFeatures.TopEnds("the player is dead"), "death");
        Check.False(AssistFeatures.TopEnds("resting: a round of all 12 shops had nothing to sell, sew or buy"),
            "the rest");
        Check.False(AssistFeatures.TopEnds("no progress in 24 passes (The Right Fit)"), "a stall");
        Check.False(AssistFeatures.TopEnds("selling to Nana: no answer"), "a vendor's silence");
        Check.False(AssistFeatures.TopEnds("homespun: no shirts to sell and none sewn"), "home dry");
        Check.False(AssistFeatures.TopEnds("an error in the shop loop: NullReferenceException: x"), "an error");
        Check.Equal(120000, AssistFeatures.TopWaitMs, "two minutes (Damian: \"2 minute wait and recall home\")");
    }

    // Testtown's one tailor shop, approved by the tailor runes bag's rune labelled for it; the player inside with five
    // fancy shirts, 51 mana, and nobody in the shop to sell to: every round has nothing to do.
    static void NothingToDo()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-circuittop-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Homespun = false;
                f.Cfg.Set(f.Server + "runes.shop", Bag.ToString("X"));
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
                s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Bag));
                s.ServerSends(Packets.Speech(Rune, "", "The Stitch", 6));
                for (uint i = 0; i < 5; i++)
                    s.ServerSends(Packets.Contained(0x40290F20 + i, 0x1EFD, 1, Pack));
                // The recall reagents, 30 each: no "reagents low" task at a bank.
                s.ServerSends(Packets.Contained(0x40290F30, 0x0F86, 30, Pack));
                s.ServerSends(Packets.Contained(0x40290F31, 0x0F7B, 30, Pack));
                s.ServerSends(Packets.Contained(0x40290F32, 0x0F7A, 30, Pack));
                s.ServerSends(Packets.Status(51));
                s.Clock.Sleep(1);
                Check.Null(f.CircuitGo(), "CircuitGo");
                int tops = 0;
                for (int i = 0; i < 3 * 60 * 60 && f.CircuitRunning && tops < 3; i++)
                {
                    s.Clock.Sleep(1000);
                    lock (f.Notes)
                        tops = f.Notes.FindAll(n => n.Contains("Circuit: the top of the circuit")).Count;
                }
                bool running = f.CircuitRunning;
                string said;
                int passes;
                lock (f.Notes)
                {
                    said = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Circuit:")));
                    passes = f.Notes.FindAll(n => n.Contains("Circuit: make, shop 1 of 1, The Stitch")).Count;
                }
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                Check.True(running, "still running after three tops: " + said);
                Check.Equal(3, tops, "three tops: " + said);
                Check.True(said.Contains("resting: a round of all 1 shops had nothing to sell, sew or buy; back to "
                    + "the top of the circuit in 120 s"), "the rest sent back to the top: " + said);
                Check.True(passes >= 4 && passes <= 4 * 3,
                    "the shop loop run at each top, no lap: " + passes + " passes");
                Check.False(said.Contains("rest: banking") || said.Contains("rest: not in a town"),
                    "no bank stop with no task: " + said);
                lock (f.Notes)
                    Check.Equal(0, f.Notes.FindAll(n => n.Contains("Recalling") || n.Contains("recalled to")).Count,
                        "no recall at all: nothing anywhere to do");
                Check.False(f.CircuitRunning, "a stop ends it");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
            RecallTests.WithFeatures((s, f) => f.Cfg.Set(f.Server + "runes.shop", ""));
        }
    }
}
