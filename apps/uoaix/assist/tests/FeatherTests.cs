#nullable enable
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text;

namespace Uoaix.Assist.Tests;

// The feather circuit (AssistFeatures, the feather circuit; AssistHarvest.md stage 3) against a fake shard that answers
// as uo1998 did for Damian's chicken (record-2650-20261023-153050.log): the attack by the combatant, the death by the
// corpse, the blade's use by a cursor, and the carve by the meat and the feathers put in the pack with their lines.
public static class FeatherTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Blade = 0x44AF0001;
    const uint Chicken = 0x02804E4B;
    const uint Corpse = 0x471A90C6;
    const uint Meat = 0x471A95EC;
    const uint Plume = 0x471A95ED;
    const uint Rib = 0x471A95EE;
    const uint Harpy = 0x02804E50;
    const uint Named = 0x02804E51;
    const int BladeArt = 0x0F47;
    const int ChickenBody = 0xD0;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("feathers: every carve answer of bladed.m is read, an unrelated line is none", Answers),
        new TestCase("feathers: the blade, bird and feather lists hold their ends and nothing past them", Lists),
        new TestCase("feathers: a chicken attacked dies and is carved: 25 feathers in the pack, the ribs back on the "
        + "corpse, then the circuit ends", Chicken1),
        new TestCase("feathers: every cut of ribs in the pack goes back on the corpse; a corpse gone leaves them in "
        + "the pack", RibsBack),
        new TestCase("feathers: an attack with no answer leaves the bird, never attacked again (L-FUTILITY)",
        SilentAttack),
        new TestCase("feathers: with no blade the circuit stops and tells the player", NoBlade),
        new TestCase("feathers: a harpy is never hunted; a renamed bird not labelled (tame), a released one, is",
        NeverHunted),
        new TestCase("feathers: a bird is looked at once before its fight; labelled (tame) it is never attacked",
        TameLabel),
        new TestCase("feathers: hits falling under the setting during a fight end the circuit", LowHits),
        new TestCase("feathers: no place for the feathers refuses to start", NoPlace),
        new TestCase("feathers: the setup's settings, their bounds, both cursors and New hunt", Setup),
        new TestCase("feathers: a bird no walk reaches is left and never attacked", Unreached),
        new TestCase("feathers: a bird that leaves view mid-fight is left", LeftView),
        new TestCase("feathers: a bird not dead by the kill wait is left; one that moves off is walked after",
        NeverDies),
        new TestCase("feathers: a blade's use with no cursor is no carve; \"You must wait\" asks for one more use",
        CarveNoCursor),
        new TestCase("feathers: a carve answered \"You must wait\" is carved once more, twice is given up", CarveWait),
        new TestCase("feathers: \"nothing useful\" and no answer are noted and the hunt goes on; no room ends it "
        + "unless the part merged into the pack", CarveOther),
        new TestCase("feathers: a full pack puts the feathers and the meat on the place, then the hunt goes on",
        StorePlace),
        new TestCase("feathers: at my feet, the last store and the collection run before the end, the meat put at the "
        + "place", AtFeetEnd),
        new TestCase("feathers: the nearest bird first; one past Range is never hunted", Nearest),
        new TestCase("feathers: a corpse that never comes, or one too far to walk to, is no carve", CorpseEdges),
        new TestCase("feathers: the link refusing the equip ends the circuit; refusing the attack or the blade's use "
        + "leaves the bird", Refused),
        new TestCase("feathers: a threat in view is run from before any bird", ThreatRuns),
        new TestCase("feathers: a stop during a fight ends the circuit as stopped", StopMidFight),
        new TestCase("feathers: at my feet, a collection due at the start runs first; a pack animal as the place "
        + "refuses to start", FeetStarts),
        new TestCase("feathers: at my feet with no room, the meat goes down at feet; still no room ends the circuit; "
        + "a store that cannot walk ends it", StoreEdges));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Put32(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    // 0x77: a mobile of body at x, y, notoriety 3 (a neutral animal).
    static byte[] Creature(uint serial, int body, int x, int y)
    {
        byte[] p = new byte[17];
        p[0] = 0x77;
        Put32(p, 1, serial);
        p[5] = (byte)(body >> 8);
        p[6] = (byte)body;
        p[7] = (byte)(x >> 8);
        p[8] = (byte)x;
        p[9] = (byte)(y >> 8);
        p[10] = (byte)y;
        p[16] = 3;
        return p;
    }

    static byte[] Combatant(uint serial)
    {
        byte[] p = new byte[5];
        p[0] = 0xAA;
        Put32(p, 1, serial);
        return p;
    }

    // 0xAF: the dying mobile, its corpse, 13 bytes as uo1998 sent it.
    static byte[] Death(uint serial, uint corpse)
    {
        byte[] p = new byte[13];
        p[0] = 0xAF;
        Put32(p, 1, serial);
        Put32(p, 5, corpse);
        return p;
    }

    // 0xA1: the player's hits, of max.
    static byte[] Hits(int hits, int max)
    {
        byte[] p = new byte[9];
        p[0] = 0xA1;
        Put32(p, 1, Packets.Player);
        p[5] = (byte)(max >> 8);
        p[6] = (byte)max;
        p[7] = (byte)(hits >> 8);
        p[8] = (byte)hits;
        return p;
    }

    static byte[] Line(string text) => Packets.Speech(0xFFFFFFFF, "", text, 6);

    static uint Serial(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    // Kwik at 1000,1000 with the blade in the pack, its contents seen, and his status.
    static void Kwik(FakeShard s, AssistFeatures f, bool blade)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contents(Pack));
        if (blade)
            s.ServerSends(Packets.Contained(Blade, BladeArt, 1, Pack));
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
        f.FeatherBlade = blade ? Blade : 0;
        f.FeathersTo = "ground|0|1005|1000|0";
        f.FeatherAtFeet = false;
        f.FeatherPilesClear();
    }

    static readonly ImmutableArray<byte[]> s_parts = ImmutableArray.Create(
        Packets.Contained(Rib, 0x09F1, 1, Pack), Line("You carve away some meat."),
        Packets.Contained(Plume, 0x1BD1, 25, Pack), Line("You pluck the bird and get the feathers."));

    // 0x1A with an amount: serial, graphic, amount, x, y, z 0.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint s = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)s,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    // uo1998 for one chicken: an equip worn; the attack answered by the combatant and, 300 ms later, the death, the
    // corpse on the chicken's tile and the chicken gone (attack false: no answer); the blade's n-th use by use(n), else
    // a cursor; the carve's n-th target by carve(n), else the ribs and 25 feathers into the pack, each with its line.
    // Items: a lift taken; a drop into the pack put there, on the ground put on its tile (art, amount: the item's in
    // the pack), onto a pile merged into it, each followed by a status of weightAfter when set; ground holds serial to
    // x, y, amount, art.
    static void ChickenShard(FakeShard s, bool attack = true, Func<int, ImmutableArray<byte[]>>? use = null,
        Func<int, ImmutableArray<byte[]>>? carve = null, int weightAfter = 0, Ground? ground = null)
    {
        int uses = 0;
        int carves = 0;
        Ground lying = ground ?? new();
        s.Answer = p =>
        {
            uint item = p.Length >= 5 ? Serial(p, 1) : 0;
            if (p[0] == 0x13)
                return ImmutableArray.Create<byte[]>(Packets.Wear(item, BladeArt, p[5], Packets.Player));
            if (p[0] == 0x05 && attack && item == Chicken)
            {
                s.Also(300, () =>
                {
                    s.ServerSends(Death(Chicken, Corpse));
                    s.ServerSends(Packets.Ground(Corpse, AssistFeatures.CorpseArt, 1001, 1000, 0));
                    s.ServerSends(Packets.Delete(Chicken));
                });
                return ImmutableArray.Create<byte[]>(Combatant(Chicken));
            }
            if (p[0] == 0x06 && item == Blade)
                return use is not null ? use(uses++)
                    : ImmutableArray.Create(Line("What do you want to use battle axe on?"),
                    Packets.Cursor(0x0003EDFB, 0));
            if (p[0] == 0x6C && Serial(p, 7) == Corpse)
                return carve is not null ? carve(carves++) : s_parts;
            if (p[0] == 0x08)
            {
                uint onto = Serial(p, 10);
                AssistItem? held = s.Link.State.Items[item];
                int art = held?.Graphic ?? (lying.Piles.TryGet(item, out int[]? was) ? was[3] : 0x1BD1);
                int amount = held?.Amount ?? (lying.Piles.TryGet(item, out int[]? had) ? had[2] : 1);
                ImmutableArray<byte[]>.Builder replies = ImmutableArray.CreateBuilder<byte[]>();
                if (onto == Pack || onto == Corpse)
                {
                    lying.Piles = lying.Piles.Remove(item);
                    replies.Add(Packets.Contained(item, art, amount, onto));
                }
                else if (lying.Piles.TryGet(onto, out int[]? under))
                {
                    lying.Piles = lying.Piles.Remove(item);
                    under[2] += amount;
                    replies.Add(Packets.Delete(item));
                    replies.Add(Lying(onto, under[3], under[2], under[0], under[1]));
                }
                else
                {
                    int x = p[5] << 8 | p[6];
                    int y = p[7] << 8 | p[8];
                    lying.Piles = lying.Piles.Set(item, new[] { x, y, amount, art });
                    replies.Add(Lying(item, art, amount, x, y));
                }
                if (weightAfter > 0)
                {
                    byte[] st = Packets.Status(51);
                    st[64] = (byte)(weightAfter >> 8);
                    st[65] = (byte)weightAfter;
                    replies.Add(st);
                }
                return replies.ToImmutable();
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static byte[] Weight(int stones)
    {
        byte[] st = Packets.Status(51);
        st[64] = (byte)(stones >> 8);
        st[65] = (byte)stones;
        return st;
    }

    // A target cursor's answer from the client: kind (0 object, 1 ground), the cursor, serial, x, y, z, graphic.
    static byte[] Targeted(uint cursor, int kind, uint serial, int x, int y, int z, int graphic)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)kind;
        Put32(p, 2, cursor);
        Put32(p, 7, serial);
        p[11] = (byte)(x >> 8);
        p[12] = (byte)x;
        p[13] = (byte)(y >> 8);
        p[14] = (byte)y;
        p[16] = (byte)(sbyte)z;
        p[17] = (byte)(graphic >> 8);
        p[18] = (byte)graphic;
        return p;
    }

    // The chicken beside Kwik, the shard of ChickenShard, the circuit run.
    static void Hunt(FakeShard s, AssistFeatures f, Func<int, ImmutableArray<byte[]>>? use = null,
        Func<int, ImmutableArray<byte[]>>? carve = null)
    {
        s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
        s.Clock.Sleep(1);
        ChickenShard(s, use: use, carve: carve);
        Run(s, f);
    }

    static int Sent(FakeShard s, int op, uint serial)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == op && p.Length >= 5 && Serial(p, op == 0x6C ? 7 : 1) == serial)
                n++;
        }
        return n;
    }

    // The circuit started and waited out: it ends on its own (no bird left), or is halted after 200 s of virtual time.
    static void Run(FakeShard s, AssistFeatures f)
    {
        Check.Null(f.CircuitFeathers(), "CircuitFeathers");
        for (int i = 0; i < 1000 && f.CircuitRunning; i++)
            s.Clock.Sleep(200);
        f.CircuitHalt("test done");
        for (int i = 0; i < 2000 && f.CircuitRunning; i++)
            s.Clock.Sleep(50);
        Check.False(f.CircuitRunning, "the circuit ended");
    }

    static void Answers()
    {
        Check.Equal(AssistFeatures.CarveAnswer.Meat, AssistFeatures.CarveSaid("You carve away some meat."), "meat");
        Check.Equal(AssistFeatures.CarveAnswer.Hide, AssistFeatures.CarveSaid("You skin the corpse and get the hide."),
            "hide");
        Check.Equal(AssistFeatures.CarveAnswer.Fur, AssistFeatures.CarveSaid("You skin the corpse and get the fur."),
            "fur");
        Check.Equal(AssistFeatures.CarveAnswer.Feathers,
            AssistFeatures.CarveSaid("You pluck the bird and get the feathers."), "feathers");
        Check.Equal(AssistFeatures.CarveAnswer.Wool,
            AssistFeatures.CarveSaid("You shear the corpse and get the wool."), "wool");
        Check.Equal(AssistFeatures.CarveAnswer.NoRoom,
            AssistFeatures.CarveSaid("You don't have anywhere to carry the feathers."), "no room");
        Check.Equal("feathers", AssistFeatures.NoRoomPart("You don't have anywhere to carry the feathers."),
            "the part");
        Check.Equal("meat", AssistFeatures.NoRoomPart("You don't have anywhere to carry the meat"),
            "the part with no full stop");
        Check.Equal("", AssistFeatures.NoRoomPart("You carve away some meat."), "no part");
        Check.Equal(AssistFeatures.CarveAnswer.Nothing,
            AssistFeatures.CarveSaid("a chicken: You see nothing useful to carve from the corpse."),
            "nothing, barked by the corpse");
        Check.Equal(AssistFeatures.CarveAnswer.NotCorpse,
            AssistFeatures.CarveSaid("Use this on corpses to carve away meat and hide."), "not a corpse");
        Check.Equal(AssistFeatures.CarveAnswer.Alive, AssistFeatures.CarveSaid("a chicken: But that's not dead!"),
            "alive");
        Check.Equal(AssistFeatures.CarveAnswer.TooFar, AssistFeatures.CarveSaid("That is too far away."), "too far");
        Check.Equal(AssistFeatures.CarveAnswer.Unseen, AssistFeatures.CarveSaid("You can't see that."), "unseen");
        Check.Equal(AssistFeatures.CarveAnswer.Wait,
            AssistFeatures.CarveSaid("You must wait to perform another action."), "wait");
        Check.Equal(AssistFeatures.CarveAnswer.None,
            AssistFeatures.CarveSaid("What do you want to use battle axe on?"), "the use's ask is no answer");
    }

    static void Lists()
    {
        foreach (int g in new[] { 0x09A5, 0x09F7, 0x0EC2, 0x0EC5, 0x0F43, 0x0F47, 0x0F4E, 0x0F51, 0x0F52, 0x0F5E,
            0x0F63, 0x13AF, 0x13B0, 0x13B5, 0x13BA, 0x13F6, 0x13F7, 0x13FA, 0x13FB, 0x13FE, 0x1403, 0x143E, 0x1443 })
            Check.True(FeatherGather.IsBlade(g), "blade " + g.ToString("X4"));
        foreach (int g in new[] { 0x09A4, 0x09A7, 0x09F5, 0x09F8, 0x0EC1, 0x0EC6, 0x0F42, 0x0F4F, 0x0F50, 0x0F53,
            0x0F5D, 0x0F64, 0x13AE, 0x13B1, 0x13B4, 0x13BB, 0x13F5, 0x13F8, 0x13F9, 0x13FC, 0x13FD, 0x1404, 0x143D,
            0x1444 })
            Check.False(FeatherGather.IsBlade(g), "no blade " + g.ToString("X4"));
        foreach (int b in new[] { 0x05, 0x06, 0xD0 })
            Check.True(FeatherGather.IsBird(b), "bird " + b.ToString("X2"));
        foreach (int b in new[] { 0x04, 0x07, 0x1E, 0xCF, 0xD1 })
            Check.False(FeatherGather.IsBird(b), "no bird " + b.ToString("X2"));
        Check.True(FeatherGather.IsFeather(0x1BD1) && FeatherGather.IsFeather(0x1BD2), "feathers");
        Check.False(FeatherGather.IsFeather(0x1BD0) || FeatherGather.IsFeather(0x1BD3), "past the feathers");
        Check.True(FeatherGather.IsMeat(0x09F1) && !FeatherGather.IsMeat(0x09F2), "the ribs alone");
    }

    static void Chicken1()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Run(s, f);
            Check.Equal(1, Sent(s, 0x05, Chicken), "one attack");
            Check.Equal(1, Sent(s, 0x6C, Corpse), "one carve of the corpse");
            Check.Equal(25, s.Link.State.Items.TryGet(Plume, out AssistItem? plume) ? plume.Amount : 0,
                "25 feathers in the pack");
            Check.True(s.Link.State.Items.TryGet(Rib, out AssistItem? rib) && rib.Container == Corpse,
                "the ribs back on the corpse");
            Check.True(f.Notes.Exists(n => n.Contains("carved: 25 feathers")), "noted");
            Check.Equal("Circuit ended (no bird within " + f.FeatherRadius + " tiles).", f.CircuitStatus, "end");
            Check.True(f.FeatherStatus.StartsWith("1 birds, 25 feathers"), "status: " + f.FeatherStatus);
        });
    }

    // uo1998 gives every cut of ribs its own serial (6 in the pack by 02:07 on 2026-10-23): two cuts carried from
    // earlier birds go back on the corpse with the new one.
    static void RibsBack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Meat, 0x09F1, 1, Pack));
            s.ServerSends(Packets.Contained(Meat + 0x10, 0x09F1, 1, Pack));
            s.Clock.Sleep(1);
            Hunt(s, f);
            foreach (uint r in new[] { Meat, Meat + 0x10, Rib })
                Check.True(s.Link.State.Items.TryGet(r, out AssistItem? it) && it.Container == Corpse,
                    "cut " + r.ToString("X") + " on the corpse");
            Check.Equal(25, s.Link.State.Items[Plume]!.Amount, "the feathers kept");
        });
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f, carve: n => s_parts.Add(Packets.Delete(Corpse)));
            Check.True(s.Link.State.Items.TryGet(Rib, out AssistItem? it) && it.Container == Pack,
                "the ribs kept in the pack");
            Check.True(f.Notes.Exists(n =>
                n.Contains("the ribs stay in the pack: the corpse " + Corpse.ToString("X") + " is gone")), "noted");
        });
    }

    static void SilentAttack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s, attack: false);
            Run(s, f);
            Check.Equal(1, Sent(s, 0x05, Chicken), "attacked once");
            Check.True(f.Notes.Exists(n => n.Contains("the attack had no answer; left")), "noted");
            Check.Equal("Circuit ended (no bird within " + f.FeatherRadius + " tiles).", f.CircuitStatus, "end");
        });
    }

    static void NoBlade()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, false);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Run(s, f);
            Check.Equal(0, Sent(s, 0x05, Chicken), "no attack");
            bool told = false;
            foreach (byte[] p in s.ClientGot)
                told |= Encoding.ASCII.GetString(p).Contains("no blade");
            Check.True(told, "the player was told");
        });
    }

    // A harpy (body 30, a threat at -125 would make the circuit run) at notoriety 3, and a chicken a player renamed and
    // released ("Clucky", no "(tame)"): its attack goes unanswered here, so it is attacked once and left.
    static void NeverHunted()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Harpy, 0x1E, 1002, 1000));
            s.ServerSends(Creature(Named, ChickenBody, 999, 1000));
            s.ServerSends(Packets.Speech(Named, "Clucky", "Clucky", 6));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Run(s, f);
            Check.Equal(0, Sent(s, 0x05, Harpy), "the harpy never attacked");
            Check.Equal(1, Sent(s, 0x05, Named), "the released bird hunted");
            Check.Equal("Circuit ended (no bird within " + f.FeatherRadius + " tiles).", f.CircuitStatus, "end");
        });
    }

    // The chicken's single-click label answered as label; the looks and the attacks in the order the server got them.
    static void Labelled(string label, out int looks, out int attacks, out bool lookFirst)
    {
        int l = 0;
        int a = 0;
        bool first = false;
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
            s.Answer = p => p[0] == 0x09 && Serial(p, 1) == Chicken
                ? ImmutableArray.Create<byte[]>(Packets.Speech(Chicken, "", label, 6)) : rest(p);
            Run(s, f);
            int seen = 0;
            foreach (byte[] p in s.ServerGot)
            {
                if (p[0] == 0x09 && Serial(p, 1) == Chicken)
                {
                    l++;
                    if (seen == 0)
                        first = true;
                }
                if (p[0] == 0x05 && Serial(p, 1) == Chicken)
                {
                    a++;
                    seen++;
                }
            }
        });
        looks = l;
        attacks = a;
        lookFirst = first;
    }

    static void TameLabel()
    {
        Labelled("a chicken (tame)", out int looks, out int attacks, out bool _);
        Check.Equal(1, looks, "the tame one looked at once");
        Check.Equal(0, attacks, "the tame one never attacked");
        Labelled("a chicken", out looks, out attacks, out bool lookFirst);
        Check.Equal(1, looks, "the wild one looked at once");
        Check.Equal(1, attacks, "the wild one attacked");
        Check.True(lookFirst, "the look before the attack");
    }

    // The chicken fights on and the player's hits fall to 30 of 85, under 50%.
    static void LowHits()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            s.Answer = p =>
            {
                if (p[0] == 0x13)
                    return ImmutableArray.Create<byte[]>(Packets.Wear(Serial(p, 1), BladeArt, p[5], Packets.Player));
                if (p[0] == 0x05)
                {
                    s.Also(500, () => s.ServerSends(Hits(30, 85)));
                    return ImmutableArray.Create<byte[]>(Combatant(Chicken));
                }
                return ImmutableArray<byte[]>.Empty;
            };
            Run(s, f);
            Check.True(f.CircuitStatus.Contains("your hits fell to 30 of 85"), "end: " + f.CircuitStatus);
        });
    }

    static void Setup()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(0x44AF0009, 0x0F39, 1, Pack));
            s.ServerSends(Creature(0x0000BEEF, 0xC8, 1002, 1002));
            s.Clock.Sleep(1);
            FeatherGather g = new(f.Cfg, () => f.Server);
            Check.Equal("feathers", g.Key, "prefix");
            Check.Equal("Feathers to", g.PlaceButton, "button");
            Check.Equal(500, g.CollectAtDefault, "Collect at");
            Check.Equal(0x1BD1, g.WeightArt, "weight art");
            Check.Equal("Feathers", g.Title, "title");
            Check.Equal("feathers", g.Goods, "goods");
            Check.Equal("blade", g.ToolName, "tool");
            Check.True(g.Gathered(0x1BD2) && g.IsTool(0x0F51) && !g.IsTool(0x0F39), "gathered and tool");
            int range = f.FeatherRadius;
            int hits = f.FeatherMinHits;
            int at = f.FeatherCollectAt;
            try
            {
                f.FeatherRadius = 999;
                Check.Equal(AssistState.GroundReach, f.FeatherRadius, "range capped at the view");
                f.FeatherRadius = 0;
                Check.Equal(1, f.FeatherRadius, "range at least 1");
                f.FeatherMinHits = 150;
                Check.Equal(99, f.FeatherMinHits, "min hits capped");
                f.FeatherMinHits = -5;
                Check.Equal(0, f.FeatherMinHits, "min hits at least 0");
                f.FeatherCollectAt = 40;
                Check.Equal(40, f.FeatherCollectAt, "Collect at");
            }
            finally
            {
                f.FeatherRadius = range;
                f.FeatherMinHits = hits;
                f.FeatherCollectAt = at;
            }
            f.FeatherAtFeet = true;
            Check.Equal("Feathers to: the ground at 1005,1000,0, at my feet first", f.FeathersText(),
                "the text at feet");
            f.FeatherAtFeet = false;
            Check.Null(f.FeatherBladeTarget(), "the blade cursor");
            s.ClientSends(Targeted(AssistFeatures.FeatherBladeCursor, 0, 0x44AF0009, 0, 0, 0, 0x0F39));
            s.Clock.Sleep(1);
            Check.True(f.FeatherStatus.StartsWith("Blade not set"), "a shovel is no blade: " + f.FeatherStatus);
            s.ClientSends(Targeted(AssistFeatures.FeatherBladeCursor, 0, Blade, 0, 0, 0, BladeArt));
            s.Clock.Sleep(1);
            Check.Equal(Blade, f.FeatherBlade, "the blade kept");
            Check.Null(f.FeathersToTarget(), "the place cursor");
            s.ClientSends(Targeted(AssistFeatures.FeathersToCursor, 0, 0, 0, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.True(f.FeatherStatus.StartsWith("Feathers to not set: the cursor was cancelled"),
                "cancel: " + f.FeatherStatus);
            s.ClientSends(Targeted(AssistFeatures.FeathersToCursor, 0, 0x0000BEEF, 1002, 1002, 0, 0));
            s.Clock.Sleep(1);
            Check.True(f.FeatherStatus.Contains("a pack animal moves"), "a mobile refused: " + f.FeatherStatus);
            s.ClientSends(Targeted(AssistFeatures.FeathersToCursor, 1, 0, 1010, 1011, 2, 0));
            s.Clock.Sleep(1);
            Check.Equal("ground|0|1010|1011|2", f.FeathersTo, "a tile");
            s.ClientSends(Targeted(AssistFeatures.FeathersToCursor, 0, 0x44AF0009, 1012, 1013, 0, 0x0F39));
            s.Clock.Sleep(1);
            Check.Equal("item|44AF0009|1012|1013|0", f.FeathersTo, "an item");
            f.FeatherAtFeet = true;
            f.FeatherPilesClear();
            Check.Null(f.CircuitFeathersNew(), "New hunt");
            for (int i = 0; i < 400 && f.CircuitRunning; i++)
                s.Clock.Sleep(50);
            f.CircuitHalt("test done");
            for (int i = 0; i < 400 && f.CircuitRunning; i++)
                s.Clock.Sleep(50);
            Check.Equal("ground|0|1000|1000|0", f.FeathersTo, "New hunt's collection point is your tile");
            Check.True(f.Notes.Exists(n => n.Contains("Feathers: new hunt")
                && n.Contains("the collection point is 1000,1000")), "noted");
            Check.Equal(0, f.FeatherDropped() + f.FeatherPileCount(), "no piles");
            f.FeatherAtFeet = false;
        });
    }

    static void Unreached()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1003, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Run(s, f);
            Check.Equal(0, Sent(s, 0x05, Chicken), "never attacked");
            Check.True(f.Notes.Exists(n => n.Contains("at 1003,1000 was not reached")), "noted");
        });
    }

    static void LeftView()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s, attack: false);
            Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
            s.Answer = p =>
            {
                if (p[0] != 0x05)
                    return rest(p);
                s.Also(300, () => s.ServerSends(Packets.Delete(Chicken)));
                return ImmutableArray.Create<byte[]>(Combatant(Chicken));
            };
            Run(s, f);
            Check.True(f.Notes.Exists(n => n.Contains("it left view; left")), "noted");
        });
    }

    // The chicken steps 3 tiles off during the fight and never dies; the kill wait set to 3 s.
    static void NeverDies()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s, attack: false);
            Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
            s.Answer = p =>
            {
                if (p[0] != 0x05)
                    return rest(p);
                s.Also(200, () => s.ServerSends(Creature(Chicken, ChickenBody, 1003, 1000)));
                return ImmutableArray.Create<byte[]>(Combatant(Chicken));
            };
            f.Timings.Set("kill-wait", 3000);
            try
            {
                Run(s, f);
            }
            finally
            {
                f.Timings.Set("kill-wait", 30000);
            }
            Check.Equal(1, Sent(s, 0x05, Chicken), "attacked once");
            Check.True(f.Notes.Exists(n => n.Contains("not dead after 3 s; left")), "noted");
        });
    }

    static void CarveNoCursor()
    {
        // No cursor at all: no carve.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f, use: n => ImmutableArray<byte[]>.Empty);
            Check.Equal(0, Sent(s, 0x6C, Corpse), "no carve");
            Check.True(f.Notes.Exists(n => n.Contains("the blade opened no target cursor")), "noted");
        });
        // "You must wait" at the first use, a cursor at the second: carved.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f,
                use: n => n == 0 ? ImmutableArray.Create<byte[]>(Line("You must wait to perform another action."))
                : ImmutableArray.Create<byte[]>(Packets.Cursor(0x0003EDFB, 0)));
            Check.Equal(1, Sent(s, 0x6C, Corpse), "one carve");
            Check.True(f.Notes.Exists(n => n.Contains("carved: 25 feathers")), "carved");
        });
    }

    static void CarveWait()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f,
                carve: n => n == 0 ? ImmutableArray.Create<byte[]>(Line("You must wait to perform another action."))
                : s_parts);
            Check.Equal(2, Sent(s, 0x6C, Corpse), "carved twice");
            Check.True(f.Notes.Exists(n => n.Contains("carved: 25 feathers")), "carved");
        });
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f, carve: n => ImmutableArray.Create<byte[]>(Line("You must wait to perform another action.")));
            Check.Equal(2, Sent(s, 0x6C, Corpse), "carved twice, no more");
            Check.True(f.Notes.Exists(n => n.Contains("\"You must wait\" twice")), "noted");
        });
    }

    static void CarveOther()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f,
                carve: n =>
                ImmutableArray.Create<byte[]>(Packets.Speech(Corpse, "",
                "You see nothing useful to carve from the corpse.", 0)));
            Check.True(f.Notes.Exists(n => n.Contains("carved: 0 feathers (nothing useful left on it)")),
                "nothing noted");
        });
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f, carve: n => ImmutableArray<byte[]>.Empty);
            Check.True(f.Notes.Exists(n => n.Contains("the carve had no answer in 3 s")), "silence noted");
        });
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f,
                carve: n => ImmutableArray.Create<byte[]>(Line("You don't have anywhere to carry the feathers.")));
            Check.True(f.CircuitStatus.Contains("the server had no room in the pack"), "end: " + f.CircuitStatus);
        });
        // uo1998 at 02:05:52 on 2026-10-23: 25 feathers merged onto the pack's 25 and the line said no room anyway; the
        // hunt goes on.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Plume, 0x1BD1, 25, Pack));
            s.Clock.Sleep(1);
            Hunt(s, f,
                carve: n =>
                ImmutableArray.Create(Packets.Contained(Meat, 0x09F1, 1, Pack), Line("You carve away some meat."),
                Packets.Contained(Plume, 0x1BD1, 50, Pack), Line("You don't have anywhere to carry the feathers.")));
            Check.Equal(50, s.Link.State.Items[Plume]!.Amount, "50 feathers in the pack");
            Check.True(f.Notes.Exists(n =>
                n.Contains("carved: 25 feathers (the server's no room for the feathers was a merge")), "noted");
            Check.Equal("Circuit ended (no bird within " + f.FeatherRadius + " tiles).", f.CircuitStatus, "end");
        });
        // The meat said no room and never reached the pack: put at feet, the circuit ends.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Hunt(s, f, carve: n => ImmutableArray.Create(Line("You don't have anywhere to carry the meat."),
                Packets.Contained(Plume, 0x1BD1, 25, Pack), Line("You pluck the bird and get the feathers.")));
            Check.True(f.CircuitStatus.Contains("the server had no room in the pack"), "end: " + f.CircuitStatus);
        });
    }

    // 100 feathers and the ribs in a pack at 600 stones, past Kwik's limit; the place beside him; each drop leaves 110.
    static void StorePlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Plume, 0x1BD1, 100, Pack));
            s.ServerSends(Packets.Contained(Meat, 0x09F1, 1, Pack));
            s.ServerSends(Weight(600));
            s.Clock.Sleep(1);
            f.FeathersTo = "ground|0|1001|1000|0";
            Ground ground = new();
            ChickenShard(s, weightAfter: 110, ground: ground);
            Run(s, f);
            Check.True(ground.Piles.TryGet(Plume, out int[]? p) && p[0] == 1001 && p[2] == 100,
                "the feathers on the place");
            Check.True(ground.Piles.TryGet(Meat, out int[]? m) && m[0] == 1001, "the meat on the place");
            Check.True(f.Notes.Exists(n => n.Contains("Feathers: 100 feathers put down")), "noted");
        });
    }

    // At my feet with 100 feathers and the ribs in the pack and no bird: the feathers put beside Kwik, the pile
    // collected to the place 3 tiles east, and the meat put there after it.
    static void AtFeetEnd()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Plume, 0x1BD1, 100, Pack));
            s.ServerSends(Packets.Contained(Meat, 0x09F1, 1, Pack));
            s.Clock.Sleep(1);
            f.FeathersTo = "ground|0|1003|1000|0";
            f.FeatherAtFeet = true;
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                List<int[]> l = new() { new[] { fx, fy, 0 } };
                int x = fx;
                int y = fy;
                while (x != tx || y != ty)
                {
                    x += Math.Sign(tx - x);
                    y += Math.Sign(ty - y);
                    l.Add(new[] { x, y, 0 });
                }
                return l.ToArray();
            };
            f.CollectWalk = (x, y) =>
            {
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                return null!;
            };
            Ground ground = new();
            ChickenShard(s, ground: ground);
            try
            {
                Run(s, f);
            }
            finally
            {
                f.FeatherAtFeet = false;
                f.CollectRoute = null!;
                f.CollectWalk = null!;
            }
            Check.True(f.Notes.Exists(n => n.Contains("no bird within") && n.Contains("collecting the piles first")),
                "collected before the end");
            Check.True(ground.Piles.TryGet(Plume, out int[]? p) && p[0] == 1003 && p[1] == 1000,
                "the feathers at the place: " + (p is null ? "none" : p[0] + "," + p[1]));
            Check.True(ground.Piles.TryGet(Meat, out int[]? m) && m[0] == 1003, "the meat at the place");
            Check.Equal(0, f.FeatherPileCount(), "no pile left in the record");
        });
    }

    // A chicken beside Kwik and another 30 tiles off, past the Range of 18: the near one hunted, the far one never.
    static void Nearest()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint Far = 0x02804E52;
            Kwik(s, f, true);
            s.ServerSends(Creature(Far, ChickenBody, 1030, 1000));
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Run(s, f);
            Check.Equal(1, Sent(s, 0x05, Chicken), "the near one hunted");
            Check.Equal(0, Sent(s, 0x05, Far), "the far one never");
            // No client map in the suites, so a walk to it would fail before any attack: the walk itself is never
            // tried.
            Check.False(f.Notes.Exists(n => n.Contains("at 1030,1000 was not reached")), "never walked to");
        });
    }

    // The death with no corpse sent; then a corpse sent 4 tiles off, which no walk reaches (no client map in the
    // suites).
    static void CorpseEdges()
    {
        foreach (int corpseX in new[] { -1, 1004 })
        {
            RecallTests.WithFeatures((s, f) =>
            {
                Kwik(s, f, true);
                s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
                s.Clock.Sleep(1);
                ChickenShard(s);
                Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
                s.Answer = p =>
                {
                    if (p[0] != 0x05)
                        return rest(p);
                    s.Also(300, () =>
                    {
                        s.ServerSends(Death(Chicken, Corpse));
                        if (corpseX > 0)
                            s.ServerSends(Packets.Ground(Corpse, AssistFeatures.CorpseArt, corpseX, 1000, 0));
                        s.ServerSends(Packets.Delete(Chicken));
                    });
                    return ImmutableArray.Create<byte[]>(Combatant(Chicken));
                };
                Run(s, f);
                Check.Equal(0, Sent(s, 0x6C, Corpse), "no carve");
                Check.True(f.Notes.Exists(n =>
                    n.Contains(corpseX > 0 ? "walking to the corpse" : "is not on the ground")), "noted");
            });
        }
    }

    static void Refused()
    {
        // The equip refused: the blade in the pack, the link disabled before the circuit.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            s.Disabled = "test";
            try
            {
                Run(s, f);
            }
            finally
            {
                s.Disabled = null;
            }
            Check.True(f.CircuitStatus.Contains("the blade: "), "end: " + f.CircuitStatus);
        });
        // The attack refused: the blade already in hand, so nothing goes out before it.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Wear(Blade, BladeArt, 1, Packets.Player));
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            s.Disabled = "test";
            try
            {
                Run(s, f);
            }
            finally
            {
                s.Disabled = null;
            }
            Check.True(f.Notes.Exists(n => n.Contains("the attack: ")), "the attack's refusal noted");
        });
        // The blade's use refused: the link disabled at the death.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s);
            Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
            s.Answer = p =>
            {
                if (p[0] == 0xAF || p[0] != 0x05)
                    return rest(p);
                s.Also(250, () => s.Disabled = "test");
                return rest(p);
            };
            try
            {
                Run(s, f);
            }
            finally
            {
                s.Disabled = null;
            }
            Check.True(f.Notes.Exists(n => n.Contains("the blade did not open")), "the use's refusal noted");
        });
    }

    // A liche (notoriety -125) 5 tiles off: run from before any bird; no client map, so the flee ends the circuit.
    static void ThreatRuns()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string server = f.Server;
            f.Server = "login.uo1998.com,5000";
            try
            {
                Kwik(s, f, true);
                byte[] liche = Creature(0x02804E60, 0x18, 1005, 1000);
                liche[16] = unchecked((byte)(sbyte)-125);
                s.ServerSends(liche);
                s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
                s.Clock.Sleep(1);
                ChickenShard(s);
                Run(s, f);
            }
            finally
            {
                f.Server = server;
            }
            Check.Equal(0, Sent(s, 0x05, Chicken), "no bird attacked");
            Check.True(f.CircuitStatus.Contains("running from a threat"), "end: " + f.CircuitStatus);
        });
    }

    // The chicken fights on, and the circuit is stopped a second into the fight.
    static void StopMidFight()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Creature(Chicken, ChickenBody, 1001, 1000));
            s.Clock.Sleep(1);
            ChickenShard(s, attack: false);
            Func<byte[], ImmutableArray<byte[]>> rest = s.Answer!;
            s.Answer = p =>
            {
                if (p[0] != 0x05)
                    return rest(p);
                s.Also(1000, () => f.CircuitHalt("test stop"));
                return ImmutableArray.Create<byte[]>(Combatant(Chicken));
            };
            Run(s, f);
            Check.Equal("Circuit ended (stopped).", f.CircuitStatus, "end");
        });
    }

    static void FeetStarts()
    {
        // 25 feathers lying at 1001,1000 in the record, Collect at 1: collected to 1003,1000 before any bird.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            Ground ground = new();
            ground.Piles = ground.Piles.Set(Plume, new[] { 1001, 1000, 25, 0x1BD1 });
            s.ServerSends(Lying(Plume, 0x1BD1, 25, 1001, 1000));
            s.Clock.Sleep(1);
            f.FeathersTo = "ground|0|1003|1000|0";
            f.FeatherAtFeet = true;
            int at = f.FeatherCollectAt;
            f.FeatherCollectAt = 1;
            f.Cfg.Set(f.Server + "feathers.piles", "1001,1000,25");
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                List<int[]> l = new() { new[] { fx, fy, 0 } };
                int x = fx;
                while (x != tx)
                {
                    x += Math.Sign(tx - x);
                    l.Add(new[] { x, fy, 0 });
                }
                return l.ToArray();
            };
            f.CollectWalk = (x, y) =>
            {
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                return null!;
            };
            ChickenShard(s, ground: ground);
            try
            {
                Run(s, f);
            }
            finally
            {
                f.FeatherAtFeet = false;
                f.FeatherCollectAt = at;
                f.CollectRoute = null!;
                f.CollectWalk = null!;
                f.FeatherPilesClear();
            }
            Check.True(ground.Piles[Plume]![0] == 1003, "the feathers at the place: " + ground.Piles[Plume]![0]);
        });
        // A pack animal as the place: At my feet refuses to start.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            f.FeathersTo = "mobile|BEEF";
            f.FeatherAtFeet = true;
            try
            {
                Run(s, f);
            }
            finally
            {
                f.FeatherAtFeet = false;
            }
            Check.True(f.CircuitStatus.Contains("a pack animal moves"), "end: " + f.CircuitStatus);
        });
    }

    static void StoreEdges()
    {
        // At my feet, 600 stones and nothing lightens the pack: the feathers and the meat go down at feet, and the
        // circuit ends with no room.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Plume, 0x1BD1, 100, Pack));
            s.ServerSends(Packets.Contained(Meat, 0x09F1, 1, Pack));
            s.ServerSends(Weight(600));
            s.Clock.Sleep(1);
            f.FeathersTo = "ground|0|1003|1000|0";
            f.FeatherAtFeet = true;
            Ground ground = new();
            ChickenShard(s, ground: ground);
            try
            {
                Run(s, f);
            }
            finally
            {
                f.FeatherAtFeet = false;
                f.FeatherPilesClear();
            }
            Check.True(ground.Piles.ContainsKey(Meat), "the meat at feet");
            Check.True(f.Notes.Exists(n => n.Contains("no room for the meat")), "noted");
            Check.Equal("Circuit ended (the pack has no room after storing the feathers).", f.CircuitStatus, "end");
        });
        // The place 10 tiles off, which no walk reaches: the store ends the circuit.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            s.ServerSends(Packets.Contained(Plume, 0x1BD1, 100, Pack));
            s.ServerSends(Weight(600));
            s.Clock.Sleep(1);
            f.FeathersTo = "ground|0|1010|1000|0";
            ChickenShard(s);
            Run(s, f);
            Check.True(f.CircuitStatus.Contains("storing the feathers: walking to the place"),
                "end: " + f.CircuitStatus);
        });
    }

    static void NoPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f, true);
            f.FeathersTo = "";
            Run(s, f);
            Check.Equal("Circuit ended (no place for the feathers: target one with Feathers to).", f.CircuitStatus,
                "end");
        });
    }
}
