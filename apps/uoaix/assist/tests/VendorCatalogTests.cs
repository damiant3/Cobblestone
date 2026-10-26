#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist.Tests;

// The player vendor catalogue (AssistFeatures.VendorCatalog) on the fake shard: Rex the vendor at 1001,1000, his pack
// holding iron ingots, a katana whose description is its name, a free dagger, a wall piece that answers its price and
// no name, an item that answers no click, a bag holding a key, and a bag that does not open.
public static class VendorCatalogTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("catalogue: the player's double-click on a player vendor opens every bag once, reads each item "
            + "once, writes its wares and stock", Reads),
        new TestCase("catalogue: past CatalogBagsMax bags opened, no more are opened, and every item of the opened "
        + "ones is read", BagCap),
        new TestCase("catalogue: a vendor 3 tiles off has its pack's top level read and no bag in it asked; at 2 "
        + "every bag is", BagReach),
        new TestCase("catalogue: a vendor with no title is known by its \"Take a look at my goods.\", typed Player "
        + "vendor and read; a pack opened without it is not", Untitled),
        new TestCase("catalogue: a mobile that is no player vendor, an answer past AnswerMs, or the vendor tab "
        + "working reads nothing", NotRead),
        new TestCase("catalogue: an empty pack is private utility with no stock", EmptyPack),
        new TestCase("catalogue: a stop during the clicks writes nothing", Stopped),
        new TestCase("vendors: the book keeps every item of the read with its bag, price and description, and reads "
        + "back from its file", Book),
        new TestCase("vendors: a wand of identification in the pack is equipped, used once on each magic item, its "
        + "names kept, and put back; a second read spends no charge", Identifies),
        new TestCase("vendors: out of charges stops the identify at the first item; with no identify item none is used",
        IdentifyStops),
        new TestCase("vendors: an item the server cannot see, no cursor, or no answer stops the identify; a label for "
        + "an answer is no magic name and the run goes on", IdentifyAnswers),
        new TestCase("vendors: the link refusing the hands' put-away, the wand's lift, its equip, use, target or "
        + "put-away, or the sword's lift or equip back stops the identify there", IdentifyRefused),
        new TestCase("vendors: a wand equip the server never shows stops the identify; with empty hands nothing is "
        + "put back but the wand", IdentifyHands));

    public static ImmutableArray<TestCase> Cases => s_cases;

    const uint Rex = 0x00033333, Bob = 0x00044444, VPack = 0x40000100, Bag = 0x40000101, Shut = 0x40000102;
    const uint Ingots = 0x40000110, Katana = 0x40000111, Key = 0x40000112, Mute = 0x40000113, Dagger = 0x40000114,
        Piece = 0x40000115;
    const uint ManyBags = 0x40000300;
    const uint Pack = 0x40000001;

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static void Login(FakeShard s, AssistFeatures f)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.Clock.Sleep(1);
        f.Atlas.ForgetWorld();
        File.Delete(f.ServerFile("vendors"));
        f.Vendors = new VendorBook(f.ServerFile("vendors"), s.Clock);
        f.Atlas.Seen(Rex, 0x190, 1001, 1000, "Rex the vendor", false);
        f.Atlas.Seen(Bob, 0x190, 1002, 1000, "Bob the banker", false);
    }

    static byte[] Said(uint serial, string text, byte type) => Packets.Speech(serial, "", text, type);

    // The shard's answers: Rex's pack with its contents, the bag's key, no answer for Shut or Mute's click, and each
    // other item's Cost line and name label. onLook runs at every click.
    // bags: Rex's pack holds that many bags instead, each opening empty.
    static Func<byte[], ImmutableArray<byte[]>> Shard(bool empty = false, Action? onLook = null, int bags = 0)
    {
        return p =>
        {
            if (p[0] == 0x06 && p.Length >= 5)
            {
                uint s = U32(p, 1) & 0x7FFFFFFF;
                if (s == Rex && empty)
                    return ImmutableArray.Create<byte[]>(Packets.Open(VPack));
                if (s == Rex && bags > 0)
                {
                    ImmutableArray<byte[]>.Builder b = ImmutableArray.CreateBuilder<byte[]>();
                    b.Add(Packets.Open(VPack));
                    for (uint i = 0; i < bags; i++)
                        b.Add(Packets.Contained(ManyBags + i, TestMap.Bag, 1, VPack));
                    return b.ToImmutable();
                }
                if (s >= ManyBags && s < ManyBags + bags)
                    return ImmutableArray.Create<byte[]>(Packets.Open(s));
                if (s == Rex)
                {
                    return ImmutableArray.Create(Packets.Open(VPack), Packets.Contained(Bag, TestMap.Bag, 1, VPack),
                        Packets.Contained(Shut, TestMap.Bag, 1, VPack), Packets.Contained(Ingots, 0x1BF2, 26, VPack),
                        Packets.Contained(Katana, 0x13FF, 1, VPack), Packets.Contained(Mute, 0x0A0F, 1, VPack),
                        Packets.Contained(Dagger, 0x0F52, 1, VPack), Packets.Contained(Piece, TestMap.Wall, 1, VPack));
                }

                if (s == Bob)
                    return ImmutableArray.Create<byte[]>(Packets.Open(0x40000200));
                if (s == Bag)
                    return ImmutableArray.Create(Packets.Open(Bag), Packets.Contained(Key, 0x100E, 1, Bag));
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] != 0x09 || p.Length < 5)
                return ImmutableArray<byte[]>.Empty;
            onLook?.Invoke();
            uint l = U32(p, 1);
            return l switch
            {
                Ingots => ImmutableArray.Create(Said(l, "iron for smithing.  Cost:1000", 0),
                    Said(l, "26 iron ingots", 6)),
                Katana => ImmutableArray.Create(Said(l, "a katana.  Cost:500", 0), Said(l, "a katana", 6)),
                Dagger => ImmutableArray.Create(Said(l, "Cost:FREE!", 0), Said(l, "a dagger", 6)),
                Piece => ImmutableArray.Create<byte[]>(Said(l, "Cost:5", 0)),
                Key => ImmutableArray.Create(Said(l, "Keys.  Cost:Not for sale.", 0), Said(l, "a key", 6)),
                Bag or Shut => ImmutableArray.Create(Said(l, "Cost:Not for sale.", 0), Said(l, "a bag", 6)),
                _ => ImmutableArray<byte[]>.Empty,
            };
        };
    }

    static AtlasNpc? Row(AssistFeatures f, uint serial) => f.Atlas.Shops().Find(n => n.Serial == serial);

    // Virtual time until the vendor's row holds wares and the read has ended, at most 60 s: a read left running past
    // its case holds the test map's files when the next case writes them.
    static void AwaitWares(FakeShard s, AssistFeatures f, uint vendor)
    {
        for (int i = 0; i < 240 && ((Row(f, vendor)?.Wares ?? "") == "" || f.VendorBusy); i++)
            s.Clock.Sleep(250);
    }

    static void AwaitIdle(FakeShard s, AssistFeatures f)
    {
        for (int i = 0; i < 240 && f.VendorBusy; i++)
            s.Clock.Sleep(250);
    }

    static int Count(FakeShard s, byte op, uint serial)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == op && p.Length >= 5 && (U32(p, 1) & 0x7FFFFFFF) == serial)
                n++;
        }
        return n;
    }

    static void Reads()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.Answer = Shard();
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            AtlasNpc? r = Row(f, Rex);
            Check.True(r is not null, "Rex's row");
            Check.Equal("weapons, ingots", r!.Wares,
                "the wares: two weapons, ingots and a wall piece for sale, the key and bags not");
            Check.Equal("26 iron ingots (iron for smithing): 1000 gp x 26|a katana: 500 gp x 1|a dagger: 0 gp x "
                + "1|wall: 5 gp x 1",
                string.Join("|", r.Items),
                "the stock: a description apart from the name in brackets, free at 0, no label the art's name");
            Check.Equal(1, Count(s, 0x06, Bag), "the bag opened once");
            Check.Equal(1, Count(s, 0x06, Shut), "the bag that did not open asked once");
            foreach (uint it in new[] { Ingots, Katana, Mute, Key, Bag, Shut, Dagger, Piece })
                Check.Equal(1, Count(s, 0x09, it), "clicked once: " + it.ToString("X"));
            Check.Equal("weapons, ingots", AssistWindow.Kind(r), "the Shops tab's kind");
            Check.True(f.VendorStatus.Contains(" catalogued: weapons, ingots, 4 for sale of 8 items")
                && !f.VendorStatus.StartsWith("Reading"), "the status says it ended: " + f.VendorStatus);
        }));
    }

    // Leighton as uo1998 shows him (2026-10-23 17:57:18): no title, his line, then his pack; Penny, an untitled person
    // whose pack opens with no line.
    static void Untitled()
    {
        const uint Leighton = 0x01DB554F, Penny = 0x01DB5550, PennyPack = 0x40000400;
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            f.Atlas.Seen(Leighton, 0x190, 1001, 1001, "Leighton", false);
            f.Atlas.Seen(Penny, 0x191, 1002, 1001, "Penny", false);
            Check.Equal("Person", f.Atlas.TypeOf(Leighton), "untitled, a person");
            Func<byte[], ImmutableArray<byte[]>> shard = Shard();
            s.Answer = p =>
            {
                if (p[0] == 0x06 && p.Length >= 5 && (U32(p, 1) & 0x7FFFFFFF) == Leighton)
                {
                    ImmutableArray<byte[]> rex = shard(Packets.Use(Rex));
                    return ImmutableArray.Create<byte[]>(Said(Leighton, AssistFeatures.VendedSays, 0)).AddRange(rex);
                }
                if (p[0] == 0x06 && p.Length >= 5 && (U32(p, 1) & 0x7FFFFFFF) == Penny)
                    return ImmutableArray.Create(Packets.Open(PennyPack),
                        Packets.Contained(0x40000401, 0x1BF2, 5, PennyPack));
                return shard(p);
            };
            s.ClientSends(Packets.Use(Penny));
            s.Clock.Sleep(10000);
            Check.Equal(0, Count(s, 0x09, 0x40000401), "no line: nothing read");
            Check.Equal("Person", f.Atlas.TypeOf(Penny), "and not typed");
            s.ClientSends(Packets.Use(Leighton));
            AwaitWares(s, f, Leighton);
            Check.Equal("Player vendor", f.Atlas.TypeOf(Leighton), "typed by his line");
            Check.Equal("weapons, ingots", Row(f, Leighton)?.Wares ?? "", "read");
        }));
    }

    // Rex 3 tiles east of the player, then 2 (Damian, 2026-10-24: "i can open a vendor primary pack at 1 farther range
    // than the subconainers in it").
    static void BagReach()
    {
        foreach (int off in new[] { 3, 2 })
        {
            new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
            {
                Login(s, f);
                f.Atlas.Seen(Rex, 0x190, 1000 + off, 1000, "Rex the vendor", false);
                s.Answer = Shard();
                s.ClientSends(Packets.Use(Rex));
                AwaitWares(s, f, Rex);
                Check.Equal(off > AssistFeatures.CatalogBagReach ? 0 : 1, Count(s, 0x06, Bag),
                    "the bag at " + off + " tiles");
                Check.Equal(off > AssistFeatures.CatalogBagReach ? 0 : 1, Count(s, 0x06, Shut),
                    "the other bag at " + off + " tiles");
                Check.Equal(1, Count(s, 0x09, Katana), "the pack's top level read at " + off + " tiles");
            }));
        }
    }

    static void BagCap()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            const int Bags = AssistFeatures.CatalogBagsMax + 5;
            s.Answer = Shard(bags: Bags);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            int opened = 0, clicked = 0;
            for (uint i = 0; i < Bags; i++)
            {
                opened += Count(s, 0x06, ManyBags + i);
                clicked += Count(s, 0x09, ManyBags + i);
            }
            Check.Equal(AssistFeatures.CatalogBagsMax - 1, opened, "the pack and CatalogBagsMax - 1 bags opened");
            Check.Equal(Bags, clicked, "every bag in the pack read");
            Check.Equal(VendorWares.Utility, Row(f, Rex)?.Wares ?? "", "none answered: nothing for sale");
        }));
    }

    static void NotRead()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.Answer = Shard();
            s.ClientSends(Packets.Use(Bob));
            s.Clock.Sleep(10000);
            Check.Equal(0, Count(s, 0x09, Ingots) + Count(s, 0x06, Bag), "a banker's box: nothing read");
            // Rex's pack opened past AnswerMs after the double-click.
            s.Answer = p => ImmutableArray<byte[]>.Empty;
            s.ClientSends(Packets.Use(Rex));
            s.Clock.Sleep(GameLink.AnswerMs + 500);
            s.ServerSends(Packets.Open(VPack));
            s.Clock.Sleep(10000);
            Check.Equal(0, Count(s, 0x09, Ingots) + Count(s, 0x06, Bag), "a late pack: nothing read");
            Check.Equal("", Row(f, Rex)?.Wares ?? "", "no wares");
            // The vendor tab working.
            s.Answer = Shard();
            f.VendorBusy = true;
            try
            {
                s.ClientSends(Packets.Use(Rex));
                s.Clock.Sleep(10000);
            }
            finally
            {
                f.VendorBusy = false;
            }
            Check.Equal(0, Count(s, 0x09, Ingots) + Count(s, 0x06, Bag), "the vendor tab working: nothing read");
            Check.Equal("", Row(f, Rex)?.Wares ?? "", "still no wares");
        }));
    }

    static void EmptyPack()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.Answer = Shard(empty: true);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(VendorWares.Utility, Row(f, Rex)?.Wares ?? "", "nothing for sale");
            Check.Equal(0, Row(f, Rex)!.Items.Count, "no stock");
        }));
    }

    static void Book()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.Answer = Shard();
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            VendorRecord? r = f.Vendors.Get(Rex);
            Check.True(r is not null, "Rex's record");
            Check.Equal(8, r!.Items.Length, "every item, for sale or not");
            Check.Equal("Rex the vendor", r.Name, "the name");
            VendorItem? key = VendorBook.Find(r, Key), katana = VendorBook.Find(r, Katana), mute = VendorBook.Find(r,
                Mute);
            Check.Equal(Bag, key?.Bag ?? 0, "the key in its bag");
            Check.Equal("Not for sale", key?.Price ?? "", "the key's price");
            Check.Equal("Keys", key?.Description ?? "", "the key's description");
            Check.Equal("500", katana?.Price ?? "", "the katana's price");
            Check.True(katana?.ForSale ?? false, "the katana for sale");
            Check.False(key?.ForSale ?? true, "the key not for sale");
            Check.Equal("no answer", mute?.Price ?? "", "an item that answered nothing");
            VendorBook again = new(f.ServerFile("vendors"), s.Clock);
            VendorRecord? back = again.Get(Rex);
            Check.Equal(8, back?.Items.Length ?? 0, "read back from the file");
            Check.Equal(Bag, back is null ? 0 : VendorBook.Find(back, Key)?.Bag ?? 0, "the bag read back");
        }));
    }

    const uint Wand = 0x40000020, Ring = 0x40000116, Bracelet = 0x40000117, Sword = 0x40000021;

    // Kwik with a broadsword in hand and the wand in his pack; Rex's pack holds a magic ring and a magic bracelet
    // besides. identify: the target's answer per item (null: the shard's "It is: a <item> of protection").
    // identify's "" answers nothing and "label" answers the item's own label (identify.m's doLookAt); cursor false: the
    // wand's use opens no cursor.
    static Func<byte[], ImmutableArray<byte[]>> MagicShard(AssistFeatures f, Func<uint, string?>? identify = null,
        string wandLabel = "a wand of identification with 5 charges", bool cursor = true)
    {
        Func<byte[], ImmutableArray<byte[]>> plain = Shard();
        return p =>
        {
            uint s = p.Length >= 5 ? U32(p, 1) & 0x7FFFFFFF : 0;
            if (p[0] == 0x06 && s == Rex)
            {
                return plain(p).AddRange(Packets.Contained(Ring, 0x108A, 1, VPack),
                    Packets.Contained(Bracelet, 0x1086, 1, VPack));
            }
            if (p[0] == 0x09 && s == Ring)
                return ImmutableArray.Create(Said(s, "a ring.  Cost:900", 0), Said(s, "a magic ring", 6));
            if (p[0] == 0x09 && s == Bracelet)
                return ImmutableArray.Create(Said(s, "Cost:1200", 0), Said(s, "a magic bracelet", 6));
            if (p[0] == 0x09 && s == Wand)
                return ImmutableArray.Create<byte[]>(Said(s, wandLabel, 6));
            if (p[0] == 0x13)
                return ImmutableArray.Create<byte[]>(Packets.Wear(s, s == Wand ? 0x0DF2 : 0x0F5E, p[5],
                    Packets.Player));
            if (p[0] == 0x08 && U32(p, 10) == Pack)
                return ImmutableArray.Create<byte[]>(Packets.Contained(s, s == Wand ? 0x0DF2 : 0x0F5E, 1, Pack));
            if (p[0] == 0x06 && s == Wand)
                return cursor ? ImmutableArray.Create<byte[]>(Packets.Cursor(0x00000777, 0))
                    : ImmutableArray<byte[]>.Empty;
            if (p[0] == 0x6C && p.Length >= 11)
            {
                uint t = U32(p, 7);
                string? says = identify?.Invoke(t);
                if (says == "")
                    return ImmutableArray<byte[]>.Empty;
                if (says == "label")
                    return ImmutableArray.Create<byte[]>(Said(t, t == Ring ? "a magic ring" : "a magic bracelet", 6));
                return ImmutableArray.Create<byte[]>(Said(0xFFFFFFFF,
                    says ?? "It is: " + (t == Ring ? "a ring of protection" : "a bracelet of protection"), 6));
            }
            return plain(p);
        };
    }

    static void MagicLogin(FakeShard s, AssistFeatures f, bool wand)
    {
        Login(s, f);
        s.ServerSends(Packets.Wear(Sword, 0x0F5E, 1, Packets.Player));
        if (wand)
            s.ServerSends(Packets.Contained(Wand, 0x0DF2, 1, Pack));
        s.Clock.Sleep(1);
    }

    static int Targeted(FakeShard s, uint serial)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x6C && p.Length >= 11 && U32(p, 7) == serial)
                n++;
        }
        return n;
    }

    static void Identifies()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            VendorRecord r = f.Vendors.Get(Rex)!;
            Check.Equal("a ring of protection", VendorBook.Find(r, Ring)?.Identified ?? "", "the ring identified");
            Check.Equal("a bracelet of protection", VendorBook.Find(r, Bracelet)?.Identified ?? "",
                "the bracelet identified");
            Check.Equal("", VendorBook.Find(r, Katana)?.Identified ?? "x", "a plain item not identified");
            Check.Equal(2, Count(s, 0x06, Wand), "the wand used twice");
            Check.Equal(1, Targeted(s, Ring), "the ring targeted once");
            Check.Equal(1, Targeted(s, Bracelet), "the bracelet targeted once");
            Check.Equal(1, Count(s, 0x09, Wand), "the wand's label read once");
            Check.Equal(Pack, f.Link?.State.Items[Wand]?.Container ?? 0, "the wand back in the pack");
            Check.Equal(1, f.Link?.State.Items[Sword]?.Layer ?? 0, "the sword back in hand");
            Check.True(f.VendorStatus.Contains("2 of 2 magic items identified"), "the status: " + f.VendorStatus);
            // A second read of the same pack: the names are kept and no charge is spent.
            s.ClientSends(Packets.Use(Rex));
            s.Clock.Sleep(1000);
            AwaitIdle(s, f);
            Check.Equal(2, Count(s, 0x06, Wand), "no more uses");
            Check.Equal("a ring of protection", VendorBook.Find(f.Vendors.Get(Rex)!, Ring)?.Identified ?? "",
                "the ring's name kept");
        }));
    }

    static void IdentifyStops()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, t => "This magic item is out of charges.");
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(1, Targeted(s, Ring) + Targeted(s, Bracelet), "one target, then the stop");
            Check.True(f.VendorStatus.Contains("0 of 2 magic items identified; stopped: the identify item is out of "
                + "charges"), "the status: " + f.VendorStatus);
            Check.Equal(Pack, f.Link?.State.Items[Wand]?.Container ?? 0, "the wand back in the pack");
        }));
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, false);
            s.Answer = MagicShard(f);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(0, Targeted(s, Ring) + Targeted(s, Bracelet), "nothing targeted");
            Check.True(f.VendorStatus.Contains("2 magic items not identified: no identify item in the pack"),
                "the status: " + f.VendorStatus);
        }));
        // A wand of another kind is read once and not used.
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, wandLabel: "a wand of lightning with 9 charges");
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(0, Count(s, 0x06, Wand), "the lightning wand never used");
            Check.Equal(1, Count(s, 0x09, Wand), "its label read once");
        }));
    }

    // MagicShard, the player dying (0x2C) with the answer to the first client packet when picks: GameLink then refuses
    // every action of the assist's own ("the player is dead").
    static Func<byte[], ImmutableArray<byte[]>> DiesOn(AssistFeatures f, Func<byte[], bool> when)
    {
        Func<byte[], ImmutableArray<byte[]>> magic = MagicShard(f);
        bool died = false;
        return p =>
        {
            ImmutableArray<byte[]> r = magic(p);
            if (died || !when(p))
                return r;
            died = true;
            return r.Add(new byte[] { 0x2C, 0x00 });
        };
    }

    static void IdentifyRefused()
    {
        (Func<byte[], bool> When, string Says)[] arms =
        {
            (p => p[0] == 0x07 && (U32(p, 1) & 0x7FFFFFFF) == Sword
                && LiftsOf(p) == 1, "0 of 2 magic items identified; stopped: putting the held items away: "),
            (p => p[0] == 0x08 && (U32(p, 1) & 0x7FFFFFFF) == Sword
            && U32(p, 10)
            == Pack, "0 of 2 magic items identified; stopped: lifting the identify item: the player is dead"),
            (p => p[0] == 0x07
            && (U32(p, 1) & 0x7FFFFFFF)
            == Wand, "0 of 2 magic items identified; stopped: equipping the identify item: the player is dead"),
            (p => p[0] == 0x13
            && (U32(p, 1) & 0x7FFFFFFF)
            == Wand, "0 of 2 magic items identified; stopped: the identify item's use: the player is dead"),
            (p => p[0] == 0x06
            && (U32(p, 1) & 0x7FFFFFFF)
            == Wand, "0 of 2 magic items identified; stopped: the identify's target: the player is dead"),
            (p => p[0] == 0x6C && p.Length >= 11
            && Targeted(p) == Bracelet, "2 of 2 magic items identified; stopped: "),
            (p => p[0] == 0x08 && (U32(p, 1) & 0x7FFFFFFF) == Wand
            && U32(p, 10) == Pack, "2 of 2 magic items identified; stopped: lifting " + Sword.ToString("X")
            + " back into your hand: the player is dead"),
            (p => p[0] == 0x07 && (U32(p, 1) & 0x7FFFFFFF) == Sword
            && LiftsOf(p) > 1, "2 of 2 magic items identified; stopped: the player is dead"),
        };
        foreach ((Func<byte[], bool> when, string says) in arms)
        {
            new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
            {
                MagicLogin(s, f, true);
                s_swordLifts = 0;
                s.Answer = DiesOn(f, when);
                s.ClientSends(Packets.Use(Rex));
                AwaitWares(s, f, Rex);
                Check.True(f.VendorStatus.Contains(says), "the status: " + f.VendorStatus + "; wanted: " + says);
            }));
        }
    }

    static void IdentifyHands()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            Func<byte[], ImmutableArray<byte[]>> magic = MagicShard(f);
            s.Answer = p => p[0] == 0x13 && (U32(p, 1) & 0x7FFFFFFF) == Wand ? ImmutableArray<byte[]>.Empty : magic(p);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.True(f.VendorStatus.Contains("0 of 2 magic items identified; stopped: the identify item did not go "
                + "into your hand"), "the status: " + f.VendorStatus);
        }));
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.ServerSends(Packets.Contained(Wand, 0x0DF2, 1, Pack));
            s.Clock.Sleep(1);
            s.Answer = MagicShard(f);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.True(f.VendorStatus.Contains("2 of 2 magic items identified") && !f.VendorStatus.Contains("stopped"),
                "the status: " + f.VendorStatus);
            Check.Equal(0, Count(s, 0x13, Sword), "nothing else equipped");
            Check.Equal(Pack, f.Link?.State.Items[Wand]?.Container ?? 0, "the wand back in the pack");
        }));
    }

    static uint Targeted(byte[] p) => U32(p, 7);

    // The sword's lifts so far: the first puts it away (FreeHands), the second brings it back.
    static int s_swordLifts;

    static int LiftsOf(byte[] p) => ++s_swordLifts;

    static void IdentifyAnswers()
    {
        // "You can't see that object well enough to identify it.": the first target, then the stop.
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, t => "You can't see that object well enough to identify it.");
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(1, Targeted(s, Ring) + Targeted(s, Bracelet), "one target");
            Check.True(f.VendorStatus.Contains("stopped: the server could not see a magic"),
                "the status: " + f.VendorStatus);
            Check.Equal(1, f.Link?.State.Items[Sword]?.Layer ?? 0, "the sword back in hand");
        }));
        // No cursor from the wand's use: nothing targeted.
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, cursor: false);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(0, Targeted(s, Ring) + Targeted(s, Bracelet), "nothing targeted");
            Check.Equal(1, Count(s, 0x06, Wand), "the wand used once");
            Check.True(f.VendorStatus.Contains("stopped: the identify item opened no target cursor"),
                "the status: " + f.VendorStatus);
        }));
        // The item's label for an answer: no magic name, and the next item is identified.
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, t => t == Ring ? "label" : null);
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            VendorRecord r = f.Vendors.Get(Rex)!;
            Check.Equal("no magic name (a magic ring)", VendorBook.Find(r, Ring)?.Identified ?? "", "the ring");
            Check.Equal("a bracelet of protection", VendorBook.Find(r, Bracelet)?.Identified ?? "", "the bracelet");
        }));
        // No answer at all: the first target, then the stop.
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            MagicLogin(s, f, true);
            s.Answer = MagicShard(f, t => "");
            s.ClientSends(Packets.Use(Rex));
            AwaitWares(s, f, Rex);
            Check.Equal(1, Targeted(s, Ring) + Targeted(s, Bracelet), "one target");
            Check.True(f.VendorStatus.Contains("stopped: no answer to the identify of a magic"),
                "the status: " + f.VendorStatus);
        }));
    }

    static void Stopped()
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Login(s, f);
            s.Answer = Shard(onLook: f.VendorStop);
            s.ClientSends(Packets.Use(Rex));
            s.Clock.Sleep(1000);
            AwaitIdle(s, f);
            Check.Equal("", Row(f, Rex)?.Wares ?? "", "no wares");
            int clicks = 0;
            foreach (uint it in new[] { Bag, Shut, Ingots, Katana, Mute, Key, Dagger, Piece })
                clicks += Count(s, 0x09, it);
            Check.Equal(1, clicks, "one click, then the stop");
            Check.True(f.VendorStatus.Contains("stopped; nothing written"),
                "the status says it stopped: " + f.VendorStatus);
        }));
    }
}
