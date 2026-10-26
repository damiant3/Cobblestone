#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist.Tests;

// Gather's record and settings (AssistHarvest.md stage 1), on a LumberGather over a settings file of the test's own.
public static class GatherTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("gather: the pile record reads its own format, skips a bad entry, writes it back the same",
            Record),
        new TestCase("gather: ours on a pile make it ours, wild on one keep it; a move merges (wild only when both), "
        + "gone leaves it", AddMove),
        new TestCase("gather: dropped and wild count the record; a remove and a clear empty it", Counts),
        new TestCase("gather: the settings are the logger's own keys, under the server's prefix", Keys),
        new TestCase("gather: the collection's place: a tile, an item where it lies now, never feet or a pack animal, "
        + "unreadable named", Place),
        new TestCase("gather: at my feet, the old feet place, its clearing; Collect at at least 1 and 1000 unset",
        Feet),
        new TestCase("gather: the collection is due at feet from Collect at on, never below it or off feet", Due),
        new TestCase("gather: a new start forgets every pile and counts them; with a tile the place is it, At my feet "
        + "kept", NewStart));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void With(Action<LumberGather, AssistSettings> body)
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-gather-" + Environment.ProcessId + ".txt");
        File.Delete(path);
        try
        {
            AssistSettings cfg = new(path);
            body(new LumberGather(cfg, () => "srv."), cfg);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string Text(LumberGather g) => string.Join(" ",
        new List<GatherPile>(g.Piles()).ConvertAll(p => p.X + "," + p.Y + ":" + p.Amount + (p.Wild ? "w" : "")));

    static void Record()
    {
        With((g, cfg) =>
        {
            cfg.Set("srv.lumber.piles", "1,2,30;bad;3,4,5,1;5,6,x;a,1,2;1,b,2;7,8,9,0");
            Check.Equal("1,2:30 3,4:5w 7,8:9", Text(g), "three read, four skipped");
            g.SetPiles(g.Piles());
            Check.Equal("1,2,30;3,4,5,1;7,8,9", cfg.Get("srv.lumber.piles", ""), "written back");
        });
    }

    static void AddMove()
    {
        With((g, cfg) =>
        {
            g.AddPile(1, 1, 10, wild: true);
            g.AddPile(1, 1, 5, wild: true);
            Check.Equal("1,1:15w", Text(g), "wild on wild stays wild");
            g.AddPile(1, 1, 5);
            Check.Equal("1,1:20", Text(g), "ours on it make it ours");
            g.AddPile(2, 2, 4, wild: true);
            g.AddPile(2, 2, 1);
            g.AddPile(2, 2, 1, wild: true);
            Check.Equal("1,1:20 2,2:6", Text(g), "wild on ours keep it ours");
            g.AddPile(3, 3, 7, wild: true);
            g.AddPile(4, 4, 8, wild: true);
            g.MovePile(3, 3, 4, 4, false);
            Check.Equal("1,1:20 2,2:6 4,4:15w", Text(g), "wild onto wild stays wild");
            g.MovePile(4, 4, 2, 2, false);
            Check.Equal("1,1:20 2,2:21", Text(g), "wild onto ours is ours");
            g.MovePile(1, 1, 2, 9, false);
            Check.Equal("2,2:21 2,9:20", Text(g), "onto an empty tile of the same column: moved, at the end");
            g.MovePile(2, 9, 9, 9, false);
            Check.Equal("2,2:21 9,9:20", Text(g), "and on");
            g.MovePile(9, 9, 0, 0, true);
            Check.Equal("2,2:21", Text(g), "gone: left the record");
            g.MovePile(5, 5, 2, 2, false);
            Check.Equal("2,2:21", Text(g), "a tile the record lacks: nothing moves");
            g.AddPile(6, 6, 2, wild: true);
            g.MovePile(2, 2, 6, 6, false);
            Check.Equal("6,6:23", Text(g), "ours onto wild is ours");
        });
    }

    static void Counts()
    {
        With((g, cfg) =>
        {
            Check.Equal(0, g.Dropped(), "none");
            Check.Equal(0, g.Wild(), "none wild");
            g.AddPile(1, 1, 10);
            g.AddPile(2, 2, 4, wild: true);
            Check.Equal(14, g.Dropped(), "dropped");
            Check.Equal(4, g.Wild(), "wild");
            g.RemovePile(2, 2);
            Check.Equal("1,1:10", Text(g), "removed");
            g.RemovePile(7, 7);
            g.RemovePile(1, 9);
            Check.Equal("1,1:10", Text(g), "a tile it lacks, of another column or the same: nothing");
            g.ClearPiles();
            Check.Equal("", cfg.Get("srv.lumber.piles", "x"), "cleared");
        });
    }

    static void Keys()
    {
        With((g, cfg) =>
        {
            Check.Equal("lumber", g.Key, "prefix");
            g.PlaceSpec = "ground|0|1|2|3";
            g.AtFeet = true;
            g.CollectAt = 40;
            g.AddPile(1, 1, 1);
            Check.Equal("ground|0|1|2|3", cfg.Get("srv.lumber.logs", ""), "the place");
            Check.Equal("on", cfg.Get("srv.lumber.feet", ""), "at my feet");
            Check.Equal("40", cfg.Get("srv.lumber.collect-at", ""), "collect at");
            Check.Equal("1,1,1", cfg.Get("srv.lumber.piles", ""), "the piles");
            Check.True(g.Gathered(0x1BDD) && g.Gathered(0x1BE2) && !g.Gathered(0x1BDC) && !g.Gathered(0x1BE3),
                "logs, by their range's ends");
            Check.Equal(0u, g.Tool, "no axe set");
            g.Tool = 0x40001234;
            Check.Equal("40001234", cfg.Get("srv.lumber.axe", ""), "the axe");
            Check.Equal(0x40001234u, g.Tool, "read back");
            g.Tool = 0;
            Check.Equal("", cfg.Get("srv.lumber.axe", "x"), "unset");
            cfg.Set("srv.lumber.axe", "zz");
            Check.Equal(0u, g.Tool, "unreadable: none");
            Check.True(g.IsTool(0x0F43) && g.IsTool(0x0F4E) && g.IsTool(0x1443) && !g.IsTool(0x0F42)
                && !g.IsTool(0x0F4F) && !g.IsTool(0x1444), "axes, by their ranges' ends");
            Check.Equal("axe|Lumber|logs|" + 0x1BDD, g.ToolName + "|" + g.Title + "|" + g.Goods + "|" + g.WeightArt,
                "names and the weight's art");
        });
    }

    static void Place()
    {
        With((g, cfg) =>
        {
            int[]? Nowhere(uint s) => null;
            int[]? Lies(uint s) => s == 0xABC ? new[] { 50, 60 } : null;
            Check.True(g.CollectPlace(Nowhere, out _, out _, out _, out _)!.Contains("target one with Logs to"),
                "unset");
            g.PlaceSpec = "feet|0";
            Check.True(g.CollectPlace(Nowhere, out _, out _, out _, out _)!.Contains("target one with Logs to"),
                "feet");
            g.PlaceSpec = "mobile|ABC";
            Check.True(g.CollectPlace(Lies, out _, out _, out _, out _)!.StartsWith("a pack animal moves"),
                "a pack animal");
            g.PlaceSpec = "item|ABC";
            Check.True(g.CollectPlace(Lies, out _, out _, out _, out _)!.Contains("not readable"),
                "an item with no tile");
            g.PlaceSpec = "ground|0|10|20|5";
            Check.Null(g.CollectPlace(Nowhere, out int x, out int y, out int z, out uint onto), "a tile");
            Check.Equal("10,20,5,0", x + "," + y + "," + z + "," + onto, "its tile");
            g.PlaceSpec = "item|ABC|10|20|5";
            Check.Null(g.CollectPlace(Lies, out x, out y, out z, out onto), "an item lying");
            Check.Equal("50,60,5," + 0xABC, x + "," + y + "," + z + "," + onto, "where it lies now");
            Check.Null(g.CollectPlace(Nowhere, out x, out y, out z, out onto), "an item out of view");
            Check.Equal("10,20,5," + 0xABC, x + "," + y + "," + z + "," + onto, "where it was targeted");
        });
    }

    static void Feet()
    {
        With((g, cfg) =>
        {
            Check.False(g.AtFeet, "unset: off");
            g.PlaceSpec = "feet|0";
            Check.True(g.AtFeet, "the old feet place is at my feet");
            g.AtFeet = false;
            Check.Equal("", g.PlaceSpec, "off clears the old feet place");
            g.PlaceSpec = "ground|0|1|2|3";
            g.AtFeet = true;
            g.AtFeet = false;
            Check.Equal("ground|0|1|2|3", g.PlaceSpec, "off keeps a real place");
            Check.Equal(1000, g.CollectAt, "unset: 1000");
            g.CollectAt = 0;
            Check.Equal(1, g.CollectAt, "at least 1");
            g.PlaceSpec = null!;
            Check.Equal("", g.PlaceSpec, "a null place is unset");
        });
    }

    static void Due()
    {
        With((g, cfg) =>
        {
            g.CollectAt = 40;
            g.AddPile(1, 1, 39);
            Check.False(g.CollectDue, "off feet");
            g.AtFeet = true;
            Check.False(g.CollectDue, "39 of 40");
            g.AddPile(2, 2, 1, wild: true);
            Check.True(g.CollectDue, "40 of 40, a wild pile counted");
            g.ClearPiles();
            Check.False(g.CollectDue, "none");
        });
    }

    static void NewStart()
    {
        With((g, cfg) =>
        {
            g.AddPile(1, 1, 10);
            g.AddPile(2, 2, 5, wild: true);
            g.PlaceSpec = "feet|0";
            g.NewStart(null, out int piles, out int goods);
            Check.Equal("2 15", piles + " " + goods, "counted");
            Check.Equal("", Text(g), "forgotten");
            Check.Equal("feet|0", g.PlaceSpec, "no tile: the place stands");
            g.AddPile(3, 3, 1);
            g.NewStart(new[] { 7, 8, 9 }, out piles, out goods);
            Check.Equal("1 1", piles + " " + goods, "counted again");
            Check.Equal("ground|0|7|8|9", g.PlaceSpec, "the place is the tile");
            Check.True(g.AtFeet, "the old feet place's At my feet kept");
            g.AtFeet = false;
            g.NewStart(new[] { 1, 2, 3 }, out piles, out goods);
            Check.Equal("0 0", piles + " " + goods, "none");
            Check.False(g.AtFeet, "off stays off");
        });
    }
}
