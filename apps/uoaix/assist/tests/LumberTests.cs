#nullable enable
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace Uoaix.Assist.Tests;

// The lumberjack (AssistFeatures, the lumberjack circuit; Damian, 2026-10-22): the 1998 server's chop answers
// (rundir bladed.m), its trees and axes (itemmanip.m Q50A, bladed.m Q4YR), one chop against the fake shard, the two
// target cursors, and the logs put wherever the player targeted.
public static class LumberTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Axe = 0x44AA0001;
    const uint Logs = 0x44AA0002;
    const uint Logs2 = 0x44AA0003;
    const uint Bag = 0x44AA0004;
    const uint Mule = 0x0000BEEF;
    const uint Pile = 0x44AA0005;
    const int AxeArt = 0x0F43;
    const int LogArt = 0x1BDD;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("lumber: every chop answer of bladed.m is read, an unrelated line is none", Answers),
        new TestCase("lumber: the tree and axe lists hold their range ends and nothing past them", Lists),
        new TestCase("lumber: a chop answered \"You put some logs\" got logs", ChopLogs),
        new TestCase("lumber: a chop answered \"fail to produce any useable wood\" failed", ChopFailed),
        new TestCase("lumber: a chop answered \"not enough wood here\" found the chunk empty", ChopEmpty),
        new TestCase("lumber: a chop answered \"You can't use an axe on that\" is no tree", ChopNotTree),
        new TestCase("lumber: a use answered \"You must wait\" with no cursor waits", ChopWait),
        new TestCase("lumber: a chop with no answer in 20 s is none", ChopSilent),
        new TestCase("lumber: the axe cursor keeps an axe and refuses anything else", AxeCursor),
        new TestCase("lumber: the logs cursor keeps a tile, an item or a mobile, and a cancel keeps the last",
        LogsCursor),
        new TestCase("lumber: both buttons send their cursor; an unreadable place refuses the store", Cursors),
        new TestCase("mine: the Store to cursor keeps the miner's place, apart from the logs'", MineStoreCursor),
        new TestCase("mine: targeted storage puts every ingot stack on the pack animal beside you", MineStoreMule),
        new TestCase("mine: targeted storage with no place set refuses, and names Store to", MineStoreUnset),
        new TestCase("mine: mine-setup storage target is kept and shown", MineStoreSetup),
        new TestCase("mine: before any strike no respawn is predicted", NoStrikeNoPrediction),
        new TestCase("mine: unmeasured, the next respawn is the server's 1024 s after the last \"no metal\" before "
        + "the strike", ServerCycle),
        new TestCase("mine: a measured cycle shorter than the server's replaces it", MeasuredCycle),
        new TestCase("mine: a measured cycle longer than the server's (HomeCave's 2537 min) is several, and the "
        + "server's is used", LongCycleIgnored),
        new TestCase("mine: a strike 42 hours after the last (a night away) records no cycle", AbsenceNotMeasured),
        new TestCase("mine: a prediction long past rolls forward by whole cycles", RollsForward),
        new TestCase("mine: a \"no metal\" from before the previous strike is no bound on this one", OldEmptyIgnored),
        new TestCase("lumber: logs to a tile are dropped on the ground there, every stack", StoreGround),
        new TestCase("lumber: logs to a tile with a log pile go onto the pile", StoreOntoPile),
        new TestCase("lumber: logs to a bag are dropped into the bag", StoreBag),
        new TestCase("lumber: logs to a pack animal beside you go onto it with no walk", StoreMule),
        new TestCase("lumber: logs the server does not take end the store with the count left", StoreRefused),
        new TestCase("lumber: logs to a pack animal not in view end the store", MuleAway),
        new TestCase("lumber: no place set refuses the store", NoPlace),
        new TestCase("lumber: logs at my feet go on the tile beside you, never your own, and the pile is kept with "
        + "its count", StoreFeet),
        new TestCase("lumber: at my feet, a tile whose drop the server puts back is passed for the next one beside",
        StoreFeetNextTile),
        new TestCase("lumber: at my feet, a log pile beside you takes the logs", StoreFeetOntoPile),
        new TestCase("lumber: at my feet, the nearest log pile within reach takes the logs, never one on your own tile",
        StoreFeetReach),
        new TestCase("lumber: at my feet, logs put on the collection's own place are not recorded as a pile",
        StoreFeetAtPlace),
        new TestCase("lumber: with no axe the circuit stops and tells the player", NoAxe),
        new TestCase("lumber: the circuit puts the held item away, takes the axe in hand and chops the tree beside it",
        Chops),
        new TestCase("lumber: a chunk with no wood rests and the next chop is a tree in another chunk", EmptyMovesOn),
        new TestCase("lumber: a tile that is no tree is not chopped again", NotTreeMovesOn),
        new TestCase("lumber: every tree in reach in a chunk found empty ends the circuit naming Clear empties",
        AllResting),
        new TestCase("lumber: the tree on the line from Chop from to the collection first, then the nearer in its band",
        LineFirst),
        new TestCase("lumber: Chop from targeted moves the oval's far focus; a cancel keeps it; Here unsets it",
        FromSpot),
        new TestCase("lumber: a chunk found empty is skipped and counted, and chopped again once cleared",
        EmptySkipped),
        new TestCase("lumber: a chunk found empty in an earlier run shows in the grid, the hover and the map",
        EmptyEarlier),
        new TestCase("lumber: Chop from unset, unreadable or null is where you stand; another cursor's target is not "
        + "Chop from's", FromFallbacks),
        new TestCase("lumber: no tree left but none in an empty chunk ends naming no Clear", NoneLeft),
        new TestCase("sentinel: the first chunk found empty is the sentinel, a later one is not", SentinelFirst),
        new TestCase("sentinel: still empty at a resume, every empty stands and the virgin tree is next",
        SentinelStillEmpty),
        new TestCase("sentinel: wood at a resume is a new day, every empty and the sentinel forgotten", SentinelWood),
        new TestCase("sentinel: a failed chop of it is chopped again, then its answer counts", SentinelFailedAgain),
        new TestCase("sentinel: one no longer empty, not a tree, silent or out of reach proves nothing",
        SentinelNothing),
        new TestCase("sentinel: an unreadable or null record is none; with no axe it is not chopped and the circuit "
        + "says so", SentinelEdges),
        new TestCase("sentinel: one 2 tiles away at a resume is not walked to; the tree beside the player is chopped "
        + "first", SentinelNotWalked),
        new TestCase("lumber: the axe found in hand before a walk and in the pack after it is equipped again; gone, "
        + "refused", EquipAfterWalk),
        new TestCase("lumber: no walk of the circuit recalls, though a rune lands beside the far tree; after it, a "
        + "walk recalls", NoRecall),
        new TestCase("sentinel: \"You must wait\" chops it again; \"not in hand\" ends the circuit", SentinelAnswers),
        new TestCase("lumber: three chops with no answer end the circuit", SilentEnds),
        new TestCase("lumber: the server saying the axe is not in hand ends the circuit", NotEquippedEnds),
        new TestCase("lumber: a full pack puts the logs on the pack animal before the next chop", FullStores),
        new TestCase("lumber: a threat in view is run from before any chop", ThreatRuns),
        new TestCase("lumber: on uo1998 a liche at notoriety -125 is run from before any chop", ThreatByNotoriety),
        new TestCase("lumber: on uo1998 notoriety -120 is a threat, -119 and a horse at +1 are none", ThreatNotoBounds),
        new TestCase("lumber: the player's own pet at notoriety -125 is no threat", ThreatNotOwnPet),
        new TestCase("lumber: off uo1998 a notoriety byte of 0x83 is no threat", ThreatNotoOnlyUo1998),
        new TestCase("session: the status reads uo1998's notoriety byte signed with its level, elsewhere the 1.25 name",
        NotorietyByte),
        new TestCase("map: a house's parts on a tile draw lowest first, a floor before a wall at its z, equal ones as "
        + "listed, its door after them", PartOrder),
        new TestCase("order: a band out costs its width in walking: the next band beside the player beats this band "
        + "across the grove", OrderLookAhead),
        new TestCase("order: a tree in a lower band a few tiles off still beats the next band beside the player",
        OrderBandStill),
        new TestCase("new chop: the first tree chopped, giving logs, is the sentinel", NewChopFirstSentinel),
        new TestCase("new chop: a first chop that is no tree is not the sentinel; the next tree is",
        NewChopNotTreeFirst),
        new TestCase("resume: a sentinel whose chunk still has wood is kept and not probed", ResumeLiveSentinel),
        new TestCase("state: 0x78 and 0x77 keep another mobile's notoriety, never the player's, an 0x78 with none "
        + "keeps none", StateNotoriety),
        new TestCase("state: a 0x1A graphic's 0x8000 adds the next byte (a door opened is its open art), after the "
        + "amount when one is sent", StateDoorOpened),
        new TestCase("state: past MobileKeep mobiles the notorieties are cleared and the newest kept",
        StateNotorietyBound),
        new TestCase("lumber: no tree within 40 tiles ends the circuit", NoTreeEnds),
        new TestCase("lumber: no place for the logs refuses to start chopping", NoPlaceEnds));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static byte[] Delete(uint serial) => new byte[] { 0x1D, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    // 0x1A: length, serial with the top bit (an amount follows), graphic, amount, x, y, z.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint s = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)s,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    static byte[] Moved(uint serial, int x, int y)
    {
        byte[] p = new byte[17];
        p[0] = 0x77;
        p[1] = (byte)(serial >> 24);
        p[2] = (byte)(serial >> 16);
        p[3] = (byte)(serial >> 8);
        p[4] = (byte)serial;
        p[5] = 0;
        p[6] = 0xC8;
        p[7] = (byte)(x >> 8);
        p[8] = (byte)x;
        p[9] = (byte)(y >> 8);
        p[10] = (byte)y;
        p[16] = 1;
        return p;
    }

    // A target cursor's answer from the client: kind (0 object, 1 ground), the cursor, serial, x, y, z, graphic.
    static byte[] Targeted(uint cursor, int kind, uint serial, int x, int y, int z, int graphic)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)kind;
        p[2] = (byte)(cursor >> 24);
        p[3] = (byte)(cursor >> 16);
        p[4] = (byte)(cursor >> 8);
        p[5] = (byte)cursor;
        p[7] = (byte)(serial >> 24);
        p[8] = (byte)(serial >> 16);
        p[9] = (byte)(serial >> 8);
        p[10] = (byte)serial;
        p[11] = (byte)(x >> 8);
        p[12] = (byte)x;
        p[13] = (byte)(y >> 8);
        p[14] = (byte)y;
        p[16] = (byte)(sbyte)z;
        p[17] = (byte)(graphic >> 8);
        p[18] = (byte)graphic;
        return p;
    }

    static void Kwik(FakeShard s, bool logs)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
        s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
        if (logs)
        {
            s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
            s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
        }
        s.Clock.Sleep(1);
    }

    static void Answers()
    {
        Check.Equal(AssistFeatures.ChopAnswer.Logs, AssistFeatures.ChopSaid("You put some logs into your backpack."),
            "logs");
        Check.Equal(AssistFeatures.ChopAnswer.Failed,
            AssistFeatures.ChopSaid("You hack at the tree for a while, but fail to produce any useable wood."),
            "failed");
        Check.Equal(AssistFeatures.ChopAnswer.Empty,
            AssistFeatures.ChopSaid("There's not enough wood here to harvest."), "empty");
        Check.Equal(AssistFeatures.ChopAnswer.NotTree, AssistFeatures.ChopSaid("You can't use an axe on that."),
            "not a tree");
        Check.Equal(AssistFeatures.ChopAnswer.NotTree, AssistFeatures.ChopSaid("You can't use a bladed item on that."),
            "bladed");
        Check.Equal(AssistFeatures.ChopAnswer.NotTree, AssistFeatures.ChopSaid("That is too far away."), "too far");
        Check.Equal(AssistFeatures.ChopAnswer.NotEquipped,
            AssistFeatures.ChopSaid("The axe must me equipped for any serious wood chopping."), "not equipped");
        Check.Equal(AssistFeatures.ChopAnswer.Wait,
            AssistFeatures.ChopSaid("You must wait to perform another action."), "wait");
        Check.Equal(AssistFeatures.ChopAnswer.None, AssistFeatures.ChopSaid("Kwik: hello"), "unrelated");
    }

    static void Lists()
    {
        foreach (int t in new[] { 0x0C95, 0x0C96, 0x0C9E, 0x0CCA, 0x0CE6, 0x0CE8, 0x0CF8, 0x0D03, 0x0D41, 0x0D53,
            0x0D6E, 0x0D7F, 0x0D84, 0x0D90, 0x0D94, 0x0DAB, 0x12B6, 0x12BE, 0x12C0, 0x12C7, 0x1323 })
            Check.True(AssistFeatures.IsTree(t), "tree " + t.ToString("X4"));
        foreach (int t in new[] { 0x0C94, 0x0C97, 0x0C9D, 0x0C9F, 0x0CC9, 0x0CE7, 0x0CE9, 0x0CF7, 0x0D04, 0x0D40,
            0x0D54, 0x0D6D, 0x0D80, 0x0D83, 0x0D91, 0x0D93, 0x0DAC, 0x12B5, 0x12BF, 0x12C8, 0x1322, 0x1324 })
            Check.False(AssistFeatures.IsTree(t), "not a tree " + t.ToString("X4"));
        foreach (int a in new[] { 0x0F43, 0x0F4E, 0x13AF, 0x13B0, 0x13FA, 0x13FB, 0x143E, 0x143F, 0x1442, 0x1443 })
            Check.True(AssistFeatures.IsAxe(a), "axe " + a.ToString("X4"));
        foreach (int a in new[] { 0x0F42, 0x0F4F, 0x13AE, 0x13B1, 0x13F9, 0x13FC, 0x143D, 0x1440, 0x1441, 0x1444 })
            Check.False(AssistFeatures.IsAxe(a), "not an axe " + a.ToString("X4"));
    }

    // The shard's chop: the use answered by a cursor (or, with useLine, by that line alone); the target by line.
    static void ChopShard(FakeShard s, string? useLine, string? targetLine)
    {
        s.Answer = p =>
        {
            if (p[0] == 0x06)
            {
                return useLine is not null
                    ? ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "", useLine, 0))
                    : ImmutableArray.Create<byte[]>(Packets.Cursor(0x00001234, 0x01));
            }

            if (p[0] == 0x6C && targetLine is not null)
            {
                s.Also(2500, () => s.ServerSends(Packets.Speech(0xFFFFFFFF, "", targetLine, 0)));
                return ImmutableArray<byte[]>.Empty;
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static AssistFeatures.ChopAnswer Chop(string? useLine, string? targetLine)
    {
        AssistFeatures.ChopAnswer got = AssistFeatures.ChopAnswer.None;
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            ChopShard(s, useLine, targetLine);
            got = f.LumberChop(s.Link, Axe, 1001, 1000, 0, 0x0CCA);
        });
        return got;
    }

    static void ChopLogs() => Check.Equal(AssistFeatures.ChopAnswer.Logs,
        Chop(null, "You put some logs into your backpack."), "answer");
    static void ChopFailed() => Check.Equal(AssistFeatures.ChopAnswer.Failed,
        Chop(null, "You hack at the tree for a while, but fail to produce any useable wood."), "answer");
    static void ChopEmpty() => Check.Equal(AssistFeatures.ChopAnswer.Empty,
        Chop(null, "There's not enough wood here to harvest."), "answer");
    static void ChopNotTree() => Check.Equal(AssistFeatures.ChopAnswer.NotTree,
        Chop(null, "You can't use an axe on that."), "answer");
    static void ChopWait() => Check.Equal(AssistFeatures.ChopAnswer.Wait,
        Chop("You must wait to perform another action.", null), "answer");
    static void ChopSilent() => Check.Equal(AssistFeatures.ChopAnswer.None, Chop(null, null), "answer");

    static void AxeCursor()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberAxe = 0;
            s.ClientSends(Targeted(AssistFeatures.LumberAxeCursor, 0, Logs, 0, 0, 0, LogArt));
            s.Clock.Sleep(1);
            Check.Equal(0u, f.LumberAxe, "the logs are no axe");
            s.ClientSends(Targeted(AssistFeatures.LumberAxeCursor, 0, Axe, 0, 0, 0, AxeArt));
            s.Clock.Sleep(1);
            Check.Equal(Axe, f.LumberAxe, "the axe");
            Check.Equal(0, Sent(s, 0x6C), "no cursor answer reached the server");
        });
    }

    static void LogsCursor()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            s.ServerSends(Moved(Mule, 1001, 1000));
            s.Clock.Sleep(1);
            s.ClientSends(Targeted(AssistFeatures.LumberLogsCursor, 1, 0, 1003, 1004, 5, 0));
            s.Clock.Sleep(1);
            Check.Equal("ground|0|1003|1004|5", f.LumberLogsTo, "a tile");
            s.ClientSends(Targeted(AssistFeatures.LumberLogsCursor, 0, Bag, 1000, 1000, 0, 0x0E76));
            s.Clock.Sleep(1);
            Check.Equal("item|" + Bag.ToString("X") + "|1000|1000|0", f.LumberLogsTo, "an item");
            s.ClientSends(Targeted(AssistFeatures.LumberLogsCursor, 0, Mule, 1001, 1000, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("mobile|" + Mule.ToString("X"), f.LumberLogsTo, "a mobile");
            s.ClientSends(Targeted(AssistFeatures.LumberLogsCursor, 0, 0, 0, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("mobile|" + Mule.ToString("X"), f.LumberLogsTo, "a cancelled cursor keeps the last");
            Check.True(f.LumberStatus.Contains("cancelled"), "status: " + f.LumberStatus);
        });
    }

    static void Cursors()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            Check.Null(f.LumberAxeTarget(), "LumberAxeTarget");
            Check.Null(f.LumberLogsTarget(), "LumberLogsTarget");
            s.Clock.Sleep(1);
            List<uint> ids = new();
            foreach (byte[] p in s.ClientGot)
            {
                if (p[0] == 0x6C)
                    ids.Add((uint)(p[2] << 24 | p[3] << 16 | p[4] << 8 | p[5]));
            }
            Check.True(ids.Contains(AssistFeatures.LumberAxeCursor) && ids.Contains(AssistFeatures.LumberLogsCursor),
                "both cursors reached the client");
            f.LumberLogsTo = "item|" + Bag.ToString("X");
            string? why = f.LumberStore(s.Link);
            Check.True(why is not null && why.Contains("not readable"), "an item with no place: " + why);
            Check.Equal("Logs to: the item " + Bag.ToString("X"), f.LumberLogsText(), "its text");
            f.LumberLogsTo = "";
            Check.Equal("Logs to: not set", f.LumberLogsText(), "unset");
        });
    }

    static int Sent(FakeShard s, byte op)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == op ? 1 : 0;
        return n;
    }

    static uint DropTarget(byte[] p) => (uint)(p[10] << 24 | p[11] << 16 | p[12] << 8 | p[13]);

    // The shard's drops: taken (the stack gone from the pack), or ignored.
    static void DropShard(FakeShard s, bool take)
    {
        s.Answer = p =>
        {
            if (p[0] == 0x08 && take)
            {
                uint item = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
                return ImmutableArray.Create<byte[]>(Delete(item));
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static List<byte[]> Drops(FakeShard s)
    {
        List<byte[]> l = new();
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x08)
                l.Add(p);
        }
        return l;
    }

    static void StoreGround()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberLogsTo = "ground|0|1001|1000|0";
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "drops");
            Check.Equal(0xFFFFFFFFu, DropTarget(d[0]), "onto the ground");
            Check.Equal(1001, d[0][5] << 8 | d[0][6], "x");
        });
    }

    static void StoreOntoPile()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Lying(Pile, LogArt, 100, 1001, 1000));
            s.Clock.Sleep(1);
            f.LumberLogsTo = "ground|0|1001|1000|0";
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "drops");
            Check.Equal(Pile, DropTarget(d[0]), "onto the pile");
            Check.Equal(Pile, DropTarget(d[1]), "the second onto the pile");
        });
    }

    static void StoreBag()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1000|0";
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "drops");
            Check.Equal(Bag, DropTarget(d[0]), "into the bag");
        });
    }

    static void StoreMule()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Moved(Mule, 1001, 1001));
            s.Clock.Sleep(1);
            f.LumberLogsTo = "mobile|" + Mule.ToString("X");
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "drops");
            Check.Equal(Mule, DropTarget(d[0]), "onto the mule");
            Check.Equal(0, Sent(s, 0x02), "no step");
        });
    }

    static void StoreRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberLogsTo = "ground|0|1001|1000|0";
            DropShard(s, false);
            string? why = f.LumberStore(s.Link);
            Check.True(why is not null && why.Contains("the server put it back where it lay")
                && why.Contains("in container " + Pack.ToString("X")), "LumberStore: " + why);
            Check.Equal(1, Drops(s).Count, "one drop, not repeated");
        });
    }

    static void StoreFeet()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberPilesClear();
            f.LumberLogsTo = "feet|0";
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "drops");
            Check.Equal(0xFFFFFFFFu, DropTarget(d[0]), "onto the ground");
            Check.Equal(1000, d[0][5] << 8 | d[0][6], "x beside you");
            Check.Equal(1001, d[0][7] << 8 | d[0][8], "the tile south of yours, not your own");
            Check.Equal("Logs to: at my feet", f.LumberLogsText(), "its text");
            List<int[]> piles = f.LumberPiles();
            Check.Equal(1, piles.Count, "one pile");
            Check.Equal(1001, piles[0][1], "kept at the tile it went on");
            Check.Equal(50, piles[0][2], "its logs");
            Check.Equal(50, f.LumberDropped(), "dropped");
            f.LumberPilesClear();
        });
    }

    // uo1998 put the drop south of the player back in the pack (as it does on the player's own tile): the next tile
    // beside, east, takes it.
    static void StoreFeetNextTile()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberPilesClear();
            f.LumberLogsTo = "feet|0";
            s.Answer = p =>
            {
                if (p[0] != 0x08)
                    return ImmutableArray<byte[]>.Empty;
                uint item = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
                return (p[7] << 8 | p[8]) == 1001
                    ? ImmutableArray.Create<byte[]>(Packets.Contained(item, LogArt, item == Logs ? 30 : 20, Pack))
                    : ImmutableArray.Create<byte[]>(Delete(item));
            };
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            List<byte[]> d = Drops(s);
            Check.Equal(3, d.Count, "south refused once, then east for both");
            Check.Equal(1001, d[1][5] << 8 | d[1][6], "east");
            Check.Equal(1001, f.LumberPiles()[0][0], "the pile kept east");
            f.LumberPilesClear();
        });
    }

    static void StoreFeetOntoPile()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Lying(Pile, LogArt, 100, 999, 999));
            s.Clock.Sleep(1);
            f.LumberPilesClear();
            f.LumberLogsTo = "feet|0";
            DropShard(s, true);
            Check.Null(f.LumberStore(s.Link), "LumberStore");
            Check.Equal(Pile, DropTarget(Drops(s)[0]), "onto the pile beside you");
            Check.Equal(999, f.LumberPiles()[0][0], "the pile's tile kept");
            f.LumberPilesClear();
        });
    }

    // A pile on the player's own tile, one 3 tiles away and one 2 away: the one 2 away takes both stacks.
    static void StoreFeetReach()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Lying(Pile, LogArt, 100, 1000, 1000));
            s.ServerSends(Lying(Pile + 1, LogArt, 100, 1003, 1000));
            s.ServerSends(Lying(Pile + 2, LogArt, 100, 1002, 998));
            s.Clock.Sleep(1);
            f.LumberPilesClear();
            f.LumberLogsTo = "feet|0";
            DropShard(s, true);
            try
            {
                Check.Null(f.LumberStore(s.Link), "LumberStore");
                List<byte[]> d = Drops(s);
                Check.Equal(2, d.Count, "drops");
                Check.Equal(Pile + 2, DropTarget(d[0]), "onto the pile 2 tiles away");
                Check.Equal(Pile + 2, DropTarget(d[1]), "the second there too");
                Check.Equal(1002 * 65536 + 998, f.LumberPiles()[0][0] * 65536 + f.LumberPiles()[0][1],
                    "the pile's tile kept");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
    }

    static void StoreFeetAtPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Lying(Pile, LogArt, 100, 1001, 1001));
            s.Clock.Sleep(1);
            f.LumberPilesClear();
            f.LumberLogsTo = "item|" + Pile.ToString("X") + "|1001|1001|0";
            f.LumberAtFeet = true;
            DropShard(s, true);
            try
            {
                Check.Null(f.LumberStore(s.Link), "LumberStore");
                Check.Equal(Pile, DropTarget(Drops(s)[0]), "onto the place's pile beside you");
                Check.Equal(0, f.LumberPiles().Count, "nothing to collect");
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
    }

    static void MuleAway()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberLogsTo = "mobile|" + Mule.ToString("X");
            string? why = f.LumberStore(s.Link);
            Check.True(why is not null && why.Contains("is not in view"), "LumberStore: " + why);
        });
    }

    static void NoPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            f.LumberLogsTo = "";
            string? why = f.LumberStore(s.Link);
            Check.True(why is not null && why.StartsWith("no place for the logs"), "LumberStore: " + why);
        });
    }

    static void NoAxe()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            f.LumberLogsTo = "ground|0|1001|1000|0";
            f.LumberAxe = 0;
            Check.Null(f.CircuitLumber(), "CircuitLumber");
            for (int i = 0; i < 400 && f.CircuitRunning; i++)
                s.Clock.Sleep(50);
            Check.False(f.CircuitRunning, "the circuit ended");
            bool told = false;
            foreach (byte[] p in s.ClientGot)
                told |= Encoding.ASCII.GetString(p).Contains("no axe");
            Check.True(told, "the player was told");
        });
    }

    const uint Ingots = 0x44AA0007;
    const uint Ingots2 = 0x44AA0008;

    static void MineStoreCursor()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            s.ServerSends(Moved(Mule, 1001, 1000));
            s.Clock.Sleep(1);
            f.LumberLogsTo = "ground|0|5|5|0";
            Check.Null(f.MineStoreToTarget(), "MineStoreToTarget");
            s.ClientSends(Targeted(AssistFeatures.MineStoreCursor, 0, Mule, 1001, 1000, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("mobile|" + Mule.ToString("X"), f.MineStoreTo, "the miner's place");
            Check.Equal("ground|0|5|5|0", f.LumberLogsTo, "the logs' place untouched");
            Check.Equal("Store to: the mobile " + Mule.ToString("X"), f.MineStoreToText(), "its text");
            s.ClientSends(Targeted(AssistFeatures.MineStoreCursor, 0, 0, 0, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.True(f.LumberStatus.StartsWith("Store to not set"), "a cancel names Store to: " + f.LumberStatus);
        });
    }

    static void MineStoreMule()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, true);
            s.ServerSends(Packets.Contained(Ingots, 0x1BF2, 300, Pack));
            s.ServerSends(Packets.Contained(Ingots2, 0x1BF2, 200, Pack));
            s.ServerSends(Moved(Mule, 1001, 1001));
            s.Clock.Sleep(1);
            f.MineStoreTo = "mobile|" + Mule.ToString("X");
            DropShard(s, true);
            Check.Null(f.MineStoreHere(s.Link, "target", out int put), "MineStoreHere");
            Check.Equal(500, put, "ingots put");
            List<byte[]> d = Drops(s);
            Check.Equal(2, d.Count, "two drops, the logs left in the pack");
            Check.Equal(Mule, DropTarget(d[0]), "onto the mule");
        });
    }

    static void MineStoreUnset()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            f.MineStoreTo = "";
            string? why = f.MineStoreHere(s.Link, "target", out int _);
            Check.True(why is not null && why.Contains("no place for the ingots") && why.Contains("Store to"),
                "MineStoreHere: " + why);
        });
    }

    static void MineStoreSetup()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            f.MineStoreTo = "ground|0|7|8|0";
            string r = f.MineSetup(new[] { "mine-setup", "storage", "target" });
            Check.True(r.StartsWith("OK storage: the targeted place, Store to: the ground at 7,8,0"),
                "storage target: " + r);
            Check.Equal("target", f.MineStorage, "MineStorage");
            Check.True(f.MineSetup(new[] { "mine-setup" }).Contains("storage target (Store to: the ground at 7,8,0)"),
                "shown");
            f.MineSetup(new[] { "mine-setup", "storage", "backpack" });
        });
    }

    static void WithMiner(Action<FakeShard, AssistFeatures, long> body)
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, false);
            body(s, f, s.Clock.UtcNow.Ticks);
        });
    }

    static long Sec(int n) => n * TimeSpan.TicksPerSecond;

    static void NoStrikeNoPrediction() => WithMiner((s, f, now) =>
        Check.Equal(0L, f.NextStrike(new AssistFeatures.MineCave()), "NextStrike"));

    static void ServerCycle()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastEmpty = now - Sec(40)
            };
            f.Strike(c, now, "test");
            Check.Equal(now - Sec(40), c.StrikeFrom, "StrikeFrom");
            Check.Equal(now - Sec(40) + AssistFeatures.ServerCycleTicks, f.NextStrike(c), "NextStrike");
        });
    }

    static void MeasuredCycle()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastEmpty = now - Sec(10),
                Cycle = Sec(900)
            };
            f.Strike(c, now, "test");
            Check.Equal(now - Sec(10) + Sec(900), f.NextStrike(c), "NextStrike");
        });
    }

    static void LongCycleIgnored()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastStrike = now,
                Cycle = 2537L * TimeSpan.TicksPerMinute
            };
            Check.Equal(now + AssistFeatures.ServerCycleTicks, f.NextStrike(c), "NextStrike");
        });
    }

    static void AbsenceNotMeasured()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastStrike = now - 42L * TimeSpan.TicksPerHour
            };
            f.Strike(c, now, "test");
            Check.Equal(0L, c.Cycle, "Cycle");
        });
    }

    static void RollsForward()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastStrike = now - Sec(5000),
                StrikeFrom = now - Sec(5030)
            };
            long n = f.NextStrike(c);
            Check.True(n >= now - AssistFeatures.ServerCycleTicks && n < now + AssistFeatures.ServerCycleTicks,
                "within a cycle of now");
            Check.Equal(0L, (n - (now - Sec(5030))) % AssistFeatures.ServerCycleTicks, "on the cycle from StrikeFrom");
        });
    }

    static void OldEmptyIgnored()
    {
        WithMiner((s, f, now) =>
        {
            AssistFeatures.MineCave c = new()
            {
                LastStrike = now - Sec(600),
                LastEmpty = now - Sec(700)
            };
            f.Strike(c, now, "test");
            Check.Equal(0L, c.StrikeFrom, "StrikeFrom");
            Check.Equal(now + Sec(600), f.NextStrike(c), "from the strike, by the 600 s gap measured as the cycle");
        });
    }

    const uint Shield = 0x44AA0006;
    const int ShieldArt = 0x1B76;
    const int TreeArt = 0x0CCA;

    // The trees the test puts on the map: x, y each, all TreeArt at z 0.
    static void Trees(AssistFeatures f, params int[][] at)
    {
        f.LumberStatics = (x, y) =>
        {
            List<int[]> l = new();
            foreach (int[] t in at)
            {
                if (t[0] == x && t[1] == y)
                    l.Add(new[] { TreeArt, 0 });
            }
            return l;
        };
    }

    static int Tx(byte[] p) => p[11] << 8 | p[12];

    static int Ty(byte[] p) => p[13] << 8 | p[14];

    // The circuit's shard: a lift taken, an equip worn, a drop into the pack put there and any other drop taken away
    // (with weightAfterDrop, a status of that weight after it), the axe's use answered by a cursor, a chop by line(x)
    // after 400 ms (null: no answer), and a chop aimed at y notTreeY answered "You can't use an axe on that.".
    static void CircuitShard(FakeShard s, Func<int, string?> line, int weightAfterDrop = 0, int notTreeY = -1)
    {
        s.Answer = p =>
        {
            uint item = p.Length >= 5 ? (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]) : 0;
            if (p[0] == 0x13)
                return ImmutableArray.Create<byte[]>(Packets.Wear(item, item == Axe ? AxeArt : ShieldArt, p[5],
                    Packets.Player));
            if (p[0] == 0x08)
            {
                if (DropTarget(p) == Pack)
                    return ImmutableArray.Create<byte[]>(Packets.Contained(item, item == Axe ? AxeArt : ShieldArt, 1,
                        Pack));
                if (weightAfterDrop > 0)
                {
                    byte[] st = Packets.Status(51);
                    st[64] = (byte)(weightAfterDrop >> 8);
                    st[65] = (byte)weightAfterDrop;
                    return ImmutableArray.Create(Delete(item), st);
                }
                return ImmutableArray.Create<byte[]>(Delete(item));
            }
            if (p[0] == 0x06)
                return ImmutableArray.Create<byte[]>(Packets.Cursor(0x00001234, 0x01));
            if (p[0] == 0x6C)
            {
                string? said = Ty(p) == notTreeY ? "You can't use an axe on that." : line(Tx(p));
                if (said is not null)
                    s.Also(400, () => s.ServerSends(Packets.Speech(0xFFFFFFFF, "", said, 0)));
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static List<byte[]> Chopped(FakeShard s)
    {
        List<byte[]> l = new();
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x6C)
                l.Add(p);
        }
        return l;
    }

    // The circuit started, run until done holds or it ends (stopped once done holds), and waited out.
    static void Run(FakeShard s, AssistFeatures f, Func<bool> done)
    {
        Check.Null(f.CircuitLumber(), "CircuitLumber");
        for (int i = 0; i < 1000 && f.CircuitRunning && !done(); i++)
            s.Clock.Sleep(200);
        f.CircuitHalt("test done");
        for (int i = 0; i < 2000 && f.CircuitRunning; i++)
            s.Clock.Sleep(50);
        Check.False(f.CircuitRunning, "the circuit ended");
        // A chop's answer still on its 400 ms delay is sent before the link closes.
        s.Clock.Sleep(600);
    }

    // The pack and its bag already opened (their contents seen), as after the player looked in them: the circuit's
    // start opens neither.
    static void Ready(FakeShard s, AssistFeatures f, bool logs)
    {
        s.ServerSends(Packets.Contents(Pack));
        s.ServerSends(Packets.Contents(Bag));
        Kwik(s, logs);
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
        f.LumberAxe = Axe;
        f.LumberLogsTo = "mobile|" + Mule.ToString("X");
        // The settings file is the suites' own and shared: no chunk found empty and no Chop from left by another case.
        f.LumberEmptyClear();
        f.LumberFrom = "";
    }

    static void Chops()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            s.ServerSends(Packets.Wear(Shield, ShieldArt, 1, Packets.Player));
            s.Clock.Sleep(1);
            Trees(f, new[] { 1001, 1000 });
            int n = 0;
            CircuitShard(s,
                x => n++ == 0 ? "You hack at the tree for a while, but fail to produce any useable wood."
                : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 3);
            Check.True(f.LumberStatus.Contains("1 failed"), "the failed chop counted: " + f.LumberStatus);
            int putAway = -1, equip = -1, first = -1;
            for (int i = 0; i < s.ServerGot.Length; i++)
            {
                byte[] p = s.ServerGot[i];
                if (p[0] == 0x08 && putAway < 0 && DropTarget(p) == Pack)
                    putAway = i;
                if (p[0] == 0x13 && equip < 0)
                    equip = i;
                if (p[0] == 0x6C && first < 0)
                    first = i;
            }
            Check.True(putAway >= 0 && putAway < equip && equip < first,
                "the shield away, then the axe on, then the chop: " + putAway + " " + equip + " " + first);
            byte[] eq = s.ServerGot[equip];
            Check.Equal(Axe, (uint)(eq[1] << 24 | eq[2] << 16 | eq[3] << 8 | eq[4]), "the axe equipped");
            byte[] c = Chopped(s)[0];
            Check.Equal(1001, Tx(c), "the tree's x");
            Check.Equal(TreeArt, c[17] << 8 | c[18], "the tree's graphic");
            Check.True(f.LumberStatus.Contains(" logs"), "status: " + f.LumberStatus);
        });
    }

    static void EmptyMovesOn()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 }, new[] { 999, 1000 });
            // Both 1 tile away: the scan meets 999 first.
            CircuitShard(s,
                x => x == 999 ? "There's not enough wood here to harvest." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 4);
            List<byte[]> c = Chopped(s);
            Check.Equal(999, Tx(c[0]), "the first chop");
            for (int i = 1; i < c.Count; i++)
                Check.Equal(1001, Tx(c[i]), "chop " + i + " in the other chunk");
            List<string[]> rows = f.LumberZoneRows();
            Check.Equal(2, rows.Count, "two chunks in the grid");
            Check.Equal("1000,1000", rows[0][0], "the chunk with wood first");
            Check.Equal("has wood", rows[0][7], "its back");
            Check.Equal("992,1000", rows[1][0], "the empty chunk last");
            Check.Equal("1", rows[1][4], "emptied once");
            Check.Equal("until Clear", rows[1][7], "empty until Clear empties");
            List<int[]> map = f.HarvestChunks();
            Check.True(map.Exists(m => m[0] == 1000 && m[1] == 1000 && m[2] == 1000),
                "the chunk with wood ready on the map");
            Check.True(map.Exists(m => m[0] == 992 && m[1] == 1000 && m[2] == 0), "the empty chunk at 0 on the map");
            string? hover = f.ChunkText(999, 1000);
            Check.True(hover is not null && hover.Contains("empty 1x") && hover.Contains("until Clear empties"),
                "hover: " + hover);
            // Kept in the settings file: another session's settings read the empty chunk.
            Check.True(new AssistSettings(AssistRelay.SettingsPath).Get(f.Server + "lumber.empty", "").Length > 0,
                "kept in the settings file");
            Check.Equal(1, f.LumberEmptyClear(), "one forgotten");
            Check.Equal(0, f.LumberEmpties().Count, "cleared");
            Check.Equal(0, f.LumberZoneRows().Count, "the chunk grid emptied with them");
            Check.Null(f.ChunkText(1100, 1100), "no chunk, no hover text");
            Check.Equal(Color.FromArgb(220, 30, 30).ToArgb(), MapView.ChunkShade(0).ToArgb(), "red at empty");
            Check.Equal(Color.FromArgb(Color.LightGreen.R, Color.LightGreen.G, Color.LightGreen.B).ToArgb(),
                MapView.ChunkShade(1000).ToArgb(), "light green at ready");
        });
    }

    static void NotTreeMovesOn()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            // Both 1 tile away: the scan meets 1001,1000 before 1001,1001.
            Trees(f, new[] { 1001, 1000 }, new[] { 1001, 1001 });
            CircuitShard(s, x => "You put some logs into your backpack.", notTreeY: 1000);
            Run(s, f, () => Chopped(s).Count >= 3);
            List<byte[]> c = Chopped(s);
            Check.Equal(1000, Ty(c[0]), "the first chop");
            for (int i = 1; i < c.Count; i++)
                Check.Equal(1001, Ty(c[i]), "chop " + i + " on the other tile");
        });
    }

    static void AllResting()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "There's not enough wood here to harvest.");
            Run(s, f, () => false);
            Check.Equal(1, Chopped(s).Count, "one chop");
            Check.True(f.CircuitStatus.Contains("1 of its 1 trees are in chunks found empty; a resume chops the "
                + "sentinel, and Clear empties"), "end: " + f.CircuitStatus);
            Check.Equal("Sentinel: the tree at 1001,1000", f.LumberSentinelText(),
                "the first chunk found empty is the sentinel");
            f.LumberEmptyClear();
            Check.Equal("Sentinel: none yet (the next chunk found empty)", f.LumberSentinelText(),
                "Clear empties forgets it");
        });
    }

    // The player at 1000,1000 (Chop from unset: where the circuit starts), the collection's place at 1000,1020: the
    // tree on the line 12 tiles away before the one 10 tiles off it; in a band, the nearer.
    static void LineFirst()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberLogsTo = "ground|0|1000|1020|0";
            f.LumberFrom = "1000,1000";
            // 990,1000 is scanned first, in a higher band than the line's.
            Trees(f, new[] { 990, 1000 }, new[] { 1010, 1000 }, new[] { 1000, 1012 }, new[] { 1000, 1018 });
            try
            {
                int[]? t = f.LumberTree(s.Link, 1000, 1000, out int trees, out int empty);
                Check.Equal(4, trees, "four trees in the oval");
                Check.True(t is not null && t[0] == 1000 && t[1] == 1012,
                    "the tree on the line first: " + (t is null ? "none" : t[0] + "," + t[1]));
                int[]? far = f.LumberTree(s.Link, 1000, 1019, out trees, out empty);
                Check.True(far is not null && far[1] == 1018, "the nearer of two on the line from 1000,1019");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberFrom = "";
            }
        });
    }

    // Chop from targeted at 1000,1040: the oval's foci are the place (1000,1020) and 1000,1040, and a tree beside the
    // player at 1001,1000 lies far off it.
    static void FromSpot()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberLogsTo = "ground|0|1000|1020|0";
            Trees(f, new[] { 1001, 1000 }, new[] { 1000, 1030 });
            Check.Null(f.LumberFromTarget(), "the cursor sent");
            s.ClientSends(Targeted(AssistFeatures.LumberFromCursor, 1, 0, 1000, 1040, 0, 0));
            s.Clock.Sleep(1);
            try
            {
                Check.Equal("Chop from: 1000,1040", f.LumberFromText(), "kept");
                int[]? t = f.LumberTree(s.Link, 1000, 1000, out int trees, out int empty);
                Check.True(t is not null && t[1] == 1030,
                    "the tree between the foci first: " + (t is null ? "none" : t[0] + "," + t[1]));
                s.ClientSends(Targeted(AssistFeatures.LumberFromCursor, 0, 0, 0, 0, 0, 0));
                s.Clock.Sleep(1);
                Check.Equal("Chop from: 1000,1040", f.LumberFromText(), "a cancel keeps it");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberFrom = "";
            }
            Check.Equal("Chop from: where the circuit starts", f.LumberFromText(), "unset");
        });
    }

    static void EmptyEarlier()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            // The chunk holding 1201,1301 (150,162 in chunks), found empty 2026-10-20 12:00 UTC.
            long zone = ((long)(1201 >> 3) << 32) | (uint)(1301 >> 3);
            long at = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).Ticks;
            f.Cfg.Set(f.Server + "lumber.empty", zone + ":" + at + ";junk;1:x");
            try
            {
                Check.Equal(1, f.LumberEmpties().Count, "one kept, the unreadable entries left out");
                List<string[]> rows = f.LumberZoneRows();
                Check.Equal(1, rows.Count, "its row");
                Check.Equal("1200,1296", rows[0][0], "the chunk");
                Check.Equal("until Clear", rows[0][7], "empty");
                string? hover = f.ChunkText(1201, 1301);
                Check.True(hover is not null && hover.StartsWith("wood 1200,1296: empty since ")
                    && hover.EndsWith(", until Clear empties"), "hover: " + hover);
                Check.True(f.HarvestChunks().Exists(m => m[0] == 1200 && m[1] == 1296 && m[2] == 0), "on the map at 0");
            }
            finally
            {
                f.LumberEmptyClear();
            }
        });
    }

    static void FromFallbacks()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 });
            f.LumberFrom = null!;
            Check.Equal("Chop from: where the circuit starts", f.LumberFromText(), "null unsets");
            int[]? t = f.LumberTree(s.Link, 1000, 1000, out int trees, out int empty);
            Check.True(t is not null && t[0] == 1001, "unset, before any circuit: from where you stand");
            f.LumberFrom = "a,b";
            t = f.LumberTree(s.Link, 1000, 1000, out trees, out empty);
            Check.True(t is not null && t[0] == 1001, "unreadable: from where you stand");
            f.LumberFrom = "";
            s.ClientSends(Targeted(0x11111111, 1, 0, 1300, 1300, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("Chop from: where the circuit starts", f.LumberFromText(), "another cursor's target leaves it");
        });
    }

    static void NoneLeft()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "You can't use an axe on that.");
            Run(s, f, () => false);
            Check.Equal(1, Chopped(s).Count, "one chop");
            Check.Equal("Circuit ended (no tree left to chop in reach).", f.CircuitStatus, "end");
        });
    }

    static long Zone(int x, int y) => ((long)(x >> 3) << 32) | (uint)(y >> 3);

    // The sentinel's chunk (999,1000) and another (1000,999) found empty in an earlier run; a virgin tree at 1001,1000.
    static void EarlierDay(AssistFeatures f, long sentinelAt, long otherAt)
    {
        f.Cfg.Set(f.Server + "lumber.empty",
            Zone(999, 1000) + ":" + sentinelAt + ";" + Zone(1000, 999) + ":" + otherAt);
        f.LumberSentinel = "999,1000,0," + TreeArt;
        Trees(f, new[] { 999, 1000 }, new[] { 1000, 999 }, new[] { 1001, 1000 });
    }

    static void SentinelFirst()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 999, 1000 }, new[] { 1001, 1000 });
            CircuitShard(s, x => "There's not enough wood here to harvest.");
            Run(s, f, () => f.LumberEmpties().Count >= 2);
            Check.Equal("Sentinel: the tree at 999,1000", f.LumberSentinelText(), "the first chunk found empty");
            Check.Equal(2, f.LumberEmpties().Count, "both empty");
            Check.True(f.Notes.Exists(n => n.Contains("its tree at 999,1000 is the sentinel")), "noted");
            f.LumberEmptyClear();
        });
    }

    static void SentinelStillEmpty()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            CircuitShard(s,
                x => x == 999 ? "There's not enough wood here to harvest." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 2);
            List<byte[]> c = Chopped(s);
            Check.Equal(999, Tx(c[0]), "the sentinel chopped first");
            Check.Equal(1001, Tx(c[1]), "then the virgin tree, not the other empty chunk's");
            Check.Equal(2, f.LumberEmpties().Count, "every empty stands");
            Check.Equal("Sentinel: the tree at 999,1000", f.LumberSentinelText(), "the sentinel kept");
            Check.True(f.Notes.Exists(n => n.Contains("is still empty: every chunk chopped since is taken as empty")),
                "noted");
            f.LumberEmptyClear();
        });
    }

    static void SentinelWood()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 2);
            Check.Equal(999, Tx(Chopped(s)[0]), "the sentinel chopped first");
            Check.Equal(0, f.LumberEmpties().Count, "a new day: every empty forgotten");
            Check.Equal("Sentinel: none yet (the next chunk found empty)", f.LumberSentinelText(),
                "the sentinel forgotten");
            Check.True(f.Notes.Exists(n =>
                n.Contains("has wood again: a new day, the 2 chunks found empty are chopped again")), "noted");
        });
    }

    static void SentinelFailedAgain()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            int n = 0;
            CircuitShard(s,
                x => x == 999 && n++ == 0 ? "You hack at the tree for a while, but fail to produce any useable wood."
                : x == 999 ? "There's not enough wood here to harvest." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 3);
            List<byte[]> c = Chopped(s);
            Check.True(Tx(c[0]) == 999 && Tx(c[1]) == 999, "the sentinel chopped twice");
            Check.Equal(1001, Tx(c[2]), "then the virgin tree");
            Check.Equal(2, f.LumberEmpties().Count, "every empty stands");
            f.LumberEmptyClear();
        });
    }

    static void SentinelNothing()
    {
        // Its chunk no longer in the record (cleared by hand): no probe, the sentinel kept (a new chop's first tree is
        // one whose chunk has not run out yet).
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            f.Cfg.Set(f.Server + "lumber.empty", Zone(1000, 999) + ":200");
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 1);
            Check.Equal(999, Tx(Chopped(s)[0]), "the nearest fresh tree, chopped as any");
            Check.Equal("Sentinel: the tree at 999,1000", f.LumberSentinelText(), "kept");
            f.LumberEmptyClear();
        });
        // Not a tree: forgotten, the empties kept.
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            CircuitShard(s, x => x == 999 ? "You can't use an axe on that." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 2);
            Check.Equal(1001, Tx(Chopped(s)[1]), "the virgin tree next");
            Check.Equal(2, f.LumberEmpties().Count, "every empty kept");
            Check.Equal("Sentinel: none yet (the next chunk found empty)", f.LumberSentinelText(), "forgotten");
            f.LumberEmptyClear();
        });
        // Silent: kept, the empties kept, chopping goes on.
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            CircuitShard(s, x => x == 999 ? null : "You put some logs into your backpack.");
            f.Timings.Set("chop-wait", 3000);
            try
            {
                Run(s, f, () => Chopped(s).Count >= 2);
            }
            finally
            {
                f.Timings.Set("chop-wait", 20000);
            }
            Check.Equal(1001, Tx(Chopped(s)[1]), "the virgin tree next");
            Check.Equal("Sentinel: the tree at 999,1000", f.LumberSentinelText(), "kept");
            Check.True(f.Notes.Exists(n => n.Contains("gave no answer that shows wood or none")), "noted");
            f.LumberEmptyClear();
        });
        // In the oval but farther than a chop's reach: never walked to, kept, chopping goes on where the player stands.
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.Cfg.Set(f.Server + "lumber.empty", Zone(1010, 1000) + ":100");
            f.LumberSentinel = "1010,1000,0," + TreeArt;
            Trees(f, new[] { 1001, 1000 }, new[] { 1010, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 1);
            Check.Equal(1001, Tx(Chopped(s)[0]), "chopping goes on");
            Check.Equal("Sentinel: the tree at 1010,1000", f.LumberSentinelText(), "kept");
            Check.True(f.Notes.Exists(n => n.Contains("the sentinel at 1010,1000 is 10 tiles away; not walked to")),
                "noted");
            f.LumberEmptyClear();
        });
        // Outside the oval (another grove's): not chopped, kept.
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.Cfg.Set(f.Server + "lumber.empty", Zone(1100, 1100) + ":100");
            f.LumberSentinel = "1100,1100,0," + TreeArt;
            Trees(f, new[] { 1001, 1000 }, new[] { 1100, 1100 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 1);
            Check.Equal(1001, Tx(Chopped(s)[0]), "the new grove's tree first");
            Check.Equal("Sentinel: the tree at 1100,1100", f.LumberSentinelText(), "kept");
            Check.True(f.Notes.Exists(n => n.Contains("the sentinel at 1100,1100 is outside this oval; not chopped")),
                "noted");
            f.LumberEmptyClear();
        });
    }

    // The sentinel's chunk (992..999) found empty, its tree at 998,1000, 2 tiles west; a virgin tree at 1001,1000.
    static void SentinelNotWalked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.Cfg.Set(f.Server + "lumber.empty", Zone(998, 1000) + ":100");
            f.LumberSentinel = "998,1000,0," + TreeArt;
            Trees(f, new[] { 998, 1000 }, new[] { 1001, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 1);
            Check.Equal(1001, Tx(Chopped(s)[0]), "the tree beside the player first");
            Check.True(f.Notes.Exists(n => n.Contains("the sentinel at 998,1000 is 2 tiles away; not walked to")),
                "noted");
            Check.False(f.Notes.Exists(n => n.Contains("Walk: ")), "no walk");
            Check.Equal("Sentinel: the tree at 998,1000", f.LumberSentinelText(), "kept");
            f.LumberEmptyClear();
        });
    }

    // The item the caller found before its walk (the axe in hand) is stale once a recall on the way put it in the pack.
    static void EquipAfterWalk()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            CircuitShard(s, x => null);
            AssistItem before = new()
            {
                Serial = Axe,
                Graphic = AxeArt,
                Layer = 1,
                Mobile = Packets.Player
            };
            Check.Null(f.LumberEquip(s.Link, before), "LumberEquip");
            int equips = 0;
            foreach (byte[] p in s.ServerGot)
                equips += p[0] == 0x13 ? 1 : 0;
            Check.Equal(1, equips, "the axe equipped");
            Check.True(s.Link.State.Items[Axe]!.Layer >= 0, "in hand");
            s.ServerSends(Delete(Axe));
            s.Clock.Sleep(1);
            Check.Equal("the axe is no longer in the pack or hand", f.LumberEquip(s.Link, before), "gone since");
        });
    }

    // A rune whose landing is 1 tile from a tree 120 tiles off: a walk outside the circuit recalls by it (24 s of walk,
    // the rune saving 23.8, over 15).
    static void NoRecall()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint Rune = 0x44AA0009;
            string data = Path.GetDirectoryName(AssistRelay.SettingsPath)!;
            string safe = f.Server.TrimEnd('.').Replace(':', '-').Replace(',', '-');
            File.WriteAllLines(Path.Combine(data, "rune-landings-" + safe + ".txt"),
                new[] { Rune.ToString("X") + "\t1119\t1000" });
            Ready(s, f, false);
            s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Pack));
            s.Clock.Sleep(1);
            int range = f.LumberRadius;
            f.LumberRadius = 150;
            Trees(f, new[] { 1120, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            try
            {
                Run(s, f, () => false);
            }
            finally
            {
                f.LumberRadius = range;
            }
            Check.True(f.Notes.Exists(n => n.Contains("Lumber: the tree at 1120,1000 was not reached")),
                "the walk was tried");
            Check.False(f.Notes.Exists(n => n.Contains("recalling by the rune")), "no recall");
            // The circuit over (its mode is cleared only at the next start), a walk of the player's own recalls again.
            f.WalkTo(1120, 1000, 1);
            Check.True(f.Notes.Exists(n => n.Contains("recalling by the rune")), "a walk after the circuit recalls");
        });
    }

    static void SentinelEdges()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberSentinel = "999,x,0,1";
            Check.Null(f.LumberSentinelTree(), "unreadable");
            f.LumberSentinel = null!;
            Check.Equal("Sentinel: none yet (the next chunk found empty)", f.LumberSentinelText(), "null is none");
            EarlierDay(f, 100, 200);
            f.LumberAxe = 0;
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => false);
            Check.Equal(0, Chopped(s).Count, "nothing chopped");
            Check.True(f.CircuitStatus.Contains("no axe"), "end: " + f.CircuitStatus);
            Check.Equal("Sentinel: the tree at 999,1000", f.LumberSentinelText(), "kept");
            f.LumberEmptyClear();
        });
    }

    static void SentinelAnswers()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            int n = 0;
            CircuitShard(s,
                x => x == 999 && n++ == 0 ? "You must wait to perform another action."
                : x == 999 ? "There's not enough wood here to harvest." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 3);
            List<byte[]> c = Chopped(s);
            Check.True(Tx(c[0]) == 999 && Tx(c[1]) == 999 && Tx(c[2]) == 1001,
                "waited, chopped again, then the virgin tree");
            f.LumberEmptyClear();
        });
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            EarlierDay(f, 100, 200);
            CircuitShard(s, x => "The axe must be equipped for any serious wood chopping.");
            Run(s, f, () => false);
            Check.Equal(1, Chopped(s).Count, "the sentinel once");
            Check.True(f.CircuitStatus.Contains("the axe is not in hand"), "end: " + f.CircuitStatus);
            f.LumberEmptyClear();
        });
    }

    // A chunk found empty is skipped and counted until cleared: 999,1000 and 1001,1000 lie in two chunks, both beside
    // you.
    static void EmptySkipped()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 999, 1000 }, new[] { 1001, 1000 });
            CircuitShard(s,
                x => x == 999 ? "There's not enough wood here to harvest." : "You put some logs into your backpack.");
            Run(s, f, () => Chopped(s).Count >= 2);
            int[]? t = f.LumberTree(s.Link, 1000, 1000, out int trees, out int empty);
            Check.Equal(1, empty, "one tree in a chunk found empty");
            Check.True(t is not null && t[0] == 1001, "the tree in the other chunk next");
            f.LumberEmptyClear();
            t = f.LumberTree(s.Link, 1000, 1000, out trees, out empty);
            Check.True(t is not null && t[0] == 999 && empty == 0, "cleared: the first tree again");
        });
    }

    static void SilentEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => null);
            f.Timings.Set("chop-wait", 3000);
            try
            {
                Run(s, f, () => false);
            }
            finally
            {
                f.Timings.Set("chop-wait", 20000);
            }
            Check.Equal(3, Chopped(s).Count, "chops");
            Check.True(f.CircuitStatus.Contains("3 chops in a row had no answer"), "end: " + f.CircuitStatus);
        });
    }

    static void NotEquippedEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "The axe must me equipped for any serious wood chopping.");
            Run(s, f, () => false);
            Check.Equal(1, Chopped(s).Count, "chops");
            Check.True(f.CircuitStatus.Contains("the axe is not in hand"), "end: " + f.CircuitStatus);
        });
    }

    static void FullStores()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, true);
            byte[] st = Packets.Status(51);
            st[64] = 360 >> 8;
            st[65] = 360 & 0xFF;
            s.ServerSends(st);
            s.ServerSends(Moved(Mule, 1000, 1001));
            s.Clock.Sleep(1);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.", weightAfterDrop: 110);
            Run(s, f, () => Chopped(s).Count >= 1);
            int drops = 0, firstChop = -1, lastDrop = -1;
            for (int i = 0; i < s.ServerGot.Length; i++)
            {
                byte[] p = s.ServerGot[i];
                if (p[0] == 0x08 && DropTarget(p) == Mule)
                {
                    drops++;
                    lastDrop = i;
                }
                if (p[0] == 0x6C && firstChop < 0)
                    firstChop = i;
            }
            Check.Equal(2, drops, "both stacks onto the mule");
            Check.True(lastDrop < firstChop, "the logs stored before the chop");
        });
    }

    static void ThreatRuns()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            byte[] lizard = Moved(0x0000CAFE, 1005, 1000);
            lizard[6] = 0x21;
            s.ServerSends(lizard);
            s.Clock.Sleep(1);
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            Run(s, f, () => false);
            Check.Equal(0, Chopped(s).Count, "no chop");
            Check.True(f.CircuitStatus.Contains("running from a threat"), "end: " + f.CircuitStatus);
        });
    }

    // A mobile 5 tiles east with the given body and notoriety byte; the circuit run over one tree beside the player;
    // the chops sent and the end. Server: uo1998 unless off is set.
    static int NotoRun(int body, int noto, bool off, string pets, out string end)
    {
        int chopped = 0;
        string status = "";
        RecallTests.WithFeatures((s, f) =>
        {
            string server = f.Server;
            if (!off)
                f.Server = "login.uo1998.com,5000";
            f.Cfg.Set("pets", pets);
            try
            {
                Ready(s, f, false);
                byte[] m = Moved(0x0000CAFE, 1005, 1000);
                m[5] = (byte)(body >> 8);
                m[6] = (byte)body;
                m[16] = (byte)noto;
                s.ServerSends(m);
                s.Clock.Sleep(1);
                Trees(f, new[] { 1001, 1000 });
                CircuitShard(s, x => "You can't use an axe on that.");
                Run(s, f, () => false);
                chopped = Chopped(s).Count;
                status = f.CircuitStatus;
            }
            finally
            {
                f.Cfg.Set("pets", "");
                f.Server = server;
            }
        });
        end = status;
        return chopped;
    }

    static void ThreatByNotoriety()
    {
        Check.Equal(0, NotoRun(0x18, 0x83, false, "", out string end), "no chop");
        Check.True(end.Contains("running from a threat"), "end: " + end);
    }

    static void ThreatNotoBounds()
    {
        Check.Equal(0, NotoRun(0x18, unchecked((byte)(sbyte)-120), false, "", out string at), "-120: no chop");
        Check.True(at.Contains("running from a threat"), "-120: " + at);
        Check.Equal(1, NotoRun(0x18, unchecked((byte)(sbyte)-119), false, "", out string above), "-119: chopped");
        Check.False(above.Contains("threat"), "-119: " + above);
        Check.Equal(1, NotoRun(0xC8, 1, false, "", out string horse), "a horse: chopped");
        Check.False(horse.Contains("threat"), "a horse: " + horse);
    }

    static void ThreatNotOwnPet()
    {
        Check.Equal(1, NotoRun(0xCA, 0x83, false, "CAFE,Snappy,0,0,0", out string end), "chopped beside the pet");
        Check.False(end.Contains("threat"), "end: " + end);
    }

    // The Session card's status: uo1998's byte read signed (Kwik's 0x04 is +4, level 0, not the 1.25 "criminal"); off
    // uo1998 the 1.25 value's name.
    static void NotorietyByte()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string server = f.Server;
            try
            {
                f.Server = "login.uo1998.com,5000";
                Check.Equal("+4 (level 0)", f.NotorietyByte(0x04), "Kwik");
                Check.Equal("-127 (level -5, negative: the criminal side)", f.NotorietyByte(0x81), "an orc");
                f.Server = "127.0.0.1:2593.";
                Check.Equal("criminal", f.NotorietyByte(0x04), "the 1.25 value");
            }
            finally
            {
                f.Server = server;
            }
        });
    }

    // TestMap's wall and shut door are 20 high and its bag has no height: at z 7 the bag (a floor's shape) draws before
    // the wall listed first, two walls keep their order, and a part at z 0 draws before both. A house's door is an item
    // on the floor's tile: it draws after the floor and after the wall of its own priority, and an item at z 0 before
    // them all.
    static void PartOrder()
    {
        new TestMap().Run(() =>
        {
            static string Drawn(List<int> l) => string.Join(" ",
                l.ConvertAll(v => (v & 0xFFFF).ToString("X") + "@" + (sbyte)(v >> 16)));
            List<int[]> parts = new() { new[] { TestMap.Wall, 7 }, new[] { TestMap.Bag, 7 }, new[] { TestMap.Wall, 7 },
                new[] { TestMap.Bag, 0 } };
            Check.Equal("82@0 82@7 80@7 80@7", Drawn(MapView.TileOrder(null, parts, null)), "the parts alone");
            List<int> statics = new() { TestMap.Wall };
            List<int[]> door = new() { new[] { TestMap.DoorShut, 1000, 1000, 7 } };
            Check.Equal("80@0 82@7 80@7 E8@7",
                Drawn(MapView.TileOrder(statics, new() { new[] { TestMap.Wall, 7 }, new[] { TestMap.Bag, 7 } }, door)),
                "the door after the floor and the wall on its tile");
            List<int[]> low = new() { new[] { TestMap.Bag, 1000, 1000, 0 } };
            Check.Equal("82@0 80@0 82@7 80@7",
                Drawn(MapView.TileOrder(statics, new() { new[] { TestMap.Wall, 7 }, new[] { TestMap.Bag, 7 } }, low)),
                "an item at z 0 before the static wall and the house");
            Check.Equal("80@0", Drawn(MapView.TileOrder(statics, new(), null)), "the statics alone");
            Check.Equal("80@0", Drawn(MapView.TileOrder(statics, new(), new())), "an empty item list");
        });
    }

    static void ThreatNotoOnlyUo1998()
    {
        Check.Equal(1, NotoRun(0x18, 0x83, true, "", out string end), "chopped");
        Check.False(end.Contains("threat"), "end: " + end);
    }

    // A circle round Chop from 1000,1000 (a pack animal is no collection place): a band is 4 tiles of distance (the
    // level is twice it, in bands of 8).
    static int[]? Picked(int px, int py, params int[][] trees)
    {
        int[]? got = null;
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberFrom = "1000,1000";
            Trees(f, trees);
            try
            {
                got = f.LumberTree(s.Link, px, py, out int n, out int e);
            }
            finally
            {
                f.LumberFrom = "";
            }
        });
        return got;
    }

    static void OrderLookAhead()
    {
        // At the top of one arm (1010,1000): 990,1000 in band 2 is 20 tiles across (20 + 16), 1012,1000 in band 3 is 2
        // beside (2 + 24); strict band order took the far one.
        int[]? t = Picked(1010, 1000, new[] { 990, 1000 }, new[] { 1012, 1000 });
        Check.True(t is not null && t[0] == 1012, "the next band beside: " + (t is null ? "none" : t[0] + "," + t[1]));
    }

    static void OrderBandStill()
    {
        // At 1006,1000: 1002,1000 in band 0 is 4 off (4 + 0), 1007,1000 in band 1 is 1 off (1 + 8).
        int[]? t = Picked(1006, 1000, new[] { 1002, 1000 }, new[] { 1007, 1000 });
        Check.True(t is not null && t[0] == 1002, "the lower band: " + (t is null ? "none" : t[0] + "," + t[1]));
    }

    static void NewChopFirstSentinel()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberSentinel = "1100,1100,0," + TreeArt;
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            string text;
            try
            {
                Check.Null(f.CircuitLumberNew(), "CircuitLumberNew");
                for (int i = 0; i < 1000 && f.CircuitRunning && Chopped(s).Count == 0; i++)
                    s.Clock.Sleep(200);
                s.Clock.Sleep(600);
                text = f.LumberSentinelText();
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(600);
            }
            finally
            {
                f.LumberSentinel = "";
                f.LumberLogsTo = "";
                f.Cfg.Set(f.Server + "lumber.old", "");
            }
            Check.Equal("Sentinel: the tree at 1001,1000", text, "the first tree chopped");
            Check.True(f.Notes.Exists(n => n.Contains("the first tree chopped is the sentinel")),
                "the new chop says so");
        });
    }

    // 999,1000 answers "You can't use an axe on that"; 1001,1000 fails.
    static void NewChopNotTreeFirst()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1001, 1000 }, new[] { 999, 1000 });
            CircuitShard(s, x => x == 999 ? "You can't use an axe on that." : "You fail to produce any useable wood.");
            string text;
            try
            {
                Check.Null(f.CircuitLumberNew(), "CircuitLumberNew");
                for (int i = 0; i < 1000 && f.CircuitRunning && Chopped(s).Count < 2; i++)
                    s.Clock.Sleep(200);
                s.Clock.Sleep(600);
                text = f.LumberSentinelText();
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(600);
            }
            finally
            {
                f.LumberSentinel = "";
                f.LumberLogsTo = "";
            }
            Check.Equal(999, Tx(Chopped(s)[0]), "the tile that is no tree chopped first");
            Check.Equal("Sentinel: the tree at 1001,1000", text,
                "the first real tree, a failed chop (chopped "
                + string.Join(" ", Chopped(s).ConvertAll(p => Tx(p).ToString())) + "; " + f.CircuitStatus + ")");
        });
    }

    static void ResumeLiveSentinel()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberSentinel = "1001,1000,0," + TreeArt;
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => "You put some logs into your backpack.");
            try
            {
                Run(s, f, () => Chopped(s).Count >= 1);
                Check.Equal("Sentinel: the tree at 1001,1000", f.LumberSentinelText(), "kept");
            }
            finally
            {
                f.LumberSentinel = "";
            }
            Check.False(f.Notes.Exists(n => n.Contains("has wood again")), "no new day");
            Check.False(f.Notes.Exists(n => n.Contains("chopping the sentinel")), "not probed");
        });
    }

    // 0x78: length, serial, body, x, y, z, direction, hue, flags, notoriety, then no worn item (a zero serial).
    static byte[] Drawn(uint serial, int body, int noto, int length = 23)
    {
        byte[] p = new byte[length];
        p[0] = 0x78;
        p[1] = (byte)(length >> 8);
        p[2] = (byte)length;
        p[3] = (byte)(serial >> 24);
        p[4] = (byte)(serial >> 16);
        p[5] = (byte)(serial >> 8);
        p[6] = (byte)serial;
        p[7] = (byte)(body >> 8);
        p[8] = (byte)body;
        p[9] = 1005 >> 8;
        p[10] = 1005 & 0xFF;
        p[11] = 1000 >> 8;
        p[12] = 1000 & 0xFF;
        if (length > 18)
            p[18] = (byte)noto;
        return p;
    }

    // uo1998's door 40001F3C shut and opened (relay log 2026-10-23 17:50:19 and 17:50:22), as sent; then an item with
    // an amount and an increment.
    static void StateDoorOpened()
    {
        AssistState st = new();
        st.Server(UoHexBytes("1A000E40001F3C06A505AC06BF06"));
        Check.Equal("6A5 1452,1727,6",
            st.Ground[0x40001F3C]!.Graphic.ToString("X") + " " + st.Ground[0x40001F3C]!.X + "," + st.Ground[0x40001F3C]!.Y
            + "," + st.Ground[0x40001F3C]!.Z, "shut");
        st.Server(UoHexBytes("1A000F40001F3C86A50105AB06C006"));
        Check.Equal("6A6 1451,1728,6",
            st.Ground[0x40001F3C]!.Graphic.ToString("X") + " " + st.Ground[0x40001F3C]!.X + "," + st.Ground[0x40001F3C]!.Y
            + "," + st.Ground[0x40001F3C]!.Z, "open: the art plus the byte, and the tile after it");
        st.Server(UoHexBytes("1A001180001F3D8010000502050600070A"));
        Check.Equal("12 1286,7,10",
            st.Ground[0x00001F3D]!.Graphic.ToString("X") + " " + st.Ground[0x00001F3D]!.X + "," + st.Ground[0x00001F3D]!.Y
            + "," + st.Ground[0x00001F3D]!.Z, "with an amount: the byte after the amount");
    }

    static byte[] UoHexBytes(string hex)
    {
        byte[] b = new byte[hex.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
        return b;
    }

    static void StateNotoriety()
    {
        AssistState st = new();
        st.Player = 0x00000001;
        st.Server(Drawn(0x0000CAFE, 0x18, 0x83));
        Check.True(st.Notorieties.TryGet(0x0000CAFE, out int liche) && (sbyte)liche == -125,
            "0x78: the liche at -125");
        byte[] moved = Moved(0x0000CAFE, 1006, 1000);
        moved[16] = 0x05;
        st.Server(moved);
        Check.Equal(5, st.Notorieties[0x0000CAFE], "0x77: the newest");
        st.Server(Drawn(0x0000BEEF, 0x18, 0x83, 18));
        Check.False(st.Notorieties.ContainsKey(0x0000BEEF), "an 0x78 cut before its notoriety");
        Check.True(st.Mobiles.ContainsKey(0x0000BEEF), "and still in view");
        st.Server(Drawn(0x00000001, 0x190, 0x83));
        byte[] me = Moved(0x00000001, 1000, 1000);
        me[16] = 0x83;
        st.Server(me);
        Check.False(st.Notorieties.ContainsKey(0x00000001), "never the player's");
    }

    static void StateNotorietyBound()
    {
        AssistState st = new();
        st.Player = 0x00000001;
        for (uint s = 0; s <= AssistState.MobileKeep + 1; s++)
            st.Server(Drawn(0x00010000 + s, 0x18, 0x83));
        Check.True(st.Notorieties.Count <= AssistState.MobileKeep + 1, "bounded: " + st.Notorieties.Count);
        Check.True(st.Notorieties.ContainsKey(0x00010000 + (uint)AssistState.MobileKeep + 1), "the newest kept");
    }

    static void NoTreeEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            Trees(f, new[] { 1012, 1000 });
            CircuitShard(s, x => null);
            f.LumberRadius = 10;
            try
            {
                Run(s, f, () => false);
            }
            finally
            {
                f.LumberRadius = AssistFeatures.LumberRadiusDefault;
            }
            Check.True(f.CircuitStatus.Contains("no tree within 10 tiles"),
                "the tree 12 away is past a range of 10: " + f.CircuitStatus);
        });
    }

    static void NoPlaceEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Ready(s, f, false);
            f.LumberLogsTo = "";
            Trees(f, new[] { 1001, 1000 });
            CircuitShard(s, x => null);
            Run(s, f, () => false);
            Check.Equal(0, Chopped(s).Count, "no chop");
            Check.True(f.CircuitStatus.Contains("no place for the logs"), "end: " + f.CircuitStatus);
        });
    }
}
