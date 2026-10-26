#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace Uoaix.Assist.Tests;

// The atlas's names (AssistAtlas.Named): uo1998's labels and status name a banker bare ("Danyelle"), its paperdoll
// (0x88) says "Danyelle the banker" (Damian, 2026-10-22, Magincia, 793BE at 3736,2156).
public static class AtlasTests
{
    const uint Danyelle = 0x000793BE;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("atlas: a bare name is an untitled person, the paperdoll's title makes a banker",
            TitleMakesBanker),
        new TestCase("atlas: a bare name after the title keeps the title", BareKeepsTitle),
        new TestCase("atlas: another name replaces the titled one", OtherNameReplaces),
        new TestCase("atlas: the paperdoll packet of a person in view names the banker", PaperdollNames),
        new TestCase("atlas: the player's own paperdoll makes no atlas row", OwnPaperdoll),
        new TestCase("atlas: every buy list is a survey, newest first; a list within StockJoinMs joins its survey, "
        + "one at StockJoinMs starts another; a catalogue is one; another vendor's apart", Surveys),
        new TestCase("atlas: a vendor's trades are every row of the record for its serial, past the newest 5000 "
        + "Trades keeps; the header and a bad row skipped", VendorTrades),
        new TestCase("atlas: survey edges: an unknown vendor or an empty list records none, a list just after a "
        + "restart starts its own, a bad line is skipped, an empty one has no rows, a locked file reads as none",
        SurveyEdges),
        new TestCase("atlas: trade edges: no record is none; a row bad in any one field, or empty, is skipped; a "
        + "locked record reads as none", TradeEdges));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static string FreshPath() => Path.Combine(Path.GetTempPath(),
        "assist-tests-atlas-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".txt");

    static AssistAtlas Fresh() => new(FreshPath(),
        new VirtualClock(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)));

    const uint Brian = 0x00011111;
    const uint Nana = 0x00022222;

    static string Rows(AtlasSurvey s) => s.At.ToString("HH:mm:ss") + " " + string.Join("+", s.Items);

    static void Surveys()
    {
        DateTime start = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        VirtualClock c = new(start);
        AssistAtlas a = new(FreshPath(), c);
        string T(int ms) => start.AddMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss");
        void Later(int ms) => c.Run(() => c.Sleep(ms));
        a.Seen(Brian, 0x190, 4400, 1100, "Brian the tailor", false);
        a.Seen(Nana, 0x191, 1400, 1600, "Nana the tailor", false);
        Check.Equal(0, a.Surveys(Brian).Count, "none before a buy list");
        a.Stock(Brian, new List<string> { "thread@3@20" });
        Later(AssistAtlas.StockJoinMs - 1);
        a.Stock(Brian, new List<string> { "kit@22@2" });
        a.Stock(Nana, new List<string> { "cloth@4@50" });
        Later(60000);
        a.Stock(Brian, new List<string> { "thread@3@9" });
        Later(AssistAtlas.StockJoinMs);
        a.Stock(Brian, new List<string> { "thread@3@30" });
        Later(1000);
        a.Catalogued(Brian, new List<string> { "shirt@40@1" }, "a hodgepodge");
        List<string> l = a.Surveys(Brian).ConvertAll(Rows);
        int j = AssistAtlas.StockJoinMs - 1, s2 = j + 60000, s3 = s2 + AssistAtlas.StockJoinMs, s4 = s3 + 1000;
        Check.Equal(T(s4) + " shirt@40@1|" + T(s3) + " thread@3@30|" + T(s2) + " thread@3@9|" + T(0)
            + " thread@3@20+kit@22@2", string.Join("|", l), "Brian's surveys, newest first");
        Check.Equal(T(j) + " cloth@4@50", string.Join("|", a.Surveys(Nana).ConvertAll(Rows)), "Nana's apart");
    }

    static string Sibling(string atlasPath, string stem,
        string ext) => Path.Combine(Path.GetDirectoryName(atlasPath)!,
        Path.GetFileName(atlasPath).Replace("atlas-", stem + "-").Replace(".txt", ext));

    static void SurveyEdges()
    {
        DateTime start = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        string path = FreshPath();
        string stocks = Sibling(path, "stocks", ".txt");
        // The atlas file a restart reads: Brian's buy list stamped now, so the next list falls inside StockJoinMs.
        File.WriteAllLines(path,
            new[] { Brian.ToString("X") + "\tShopkeeper\ttailor\tBrian the tailor\t400\t4400\t1100\t"
            + start.ToLocalTime().Ticks + "\tthread@3@20\t" + start.ToLocalTime().Ticks + "\t" });
        VirtualClock c = new(start);
        AssistAtlas a = new(path, c);
        a.Stock(0x00099999, new List<string> { "x@1@1" });
        a.Catalogued(0x00099999, new List<string> { "x@1@1" }, "");
        a.Stock(Brian, new List<string>());
        Check.False(File.Exists(stocks), "an unknown vendor and an empty list record nothing");
        a.Stock(Brian, new List<string> { "kit@22@2" });
        List<AtlasSurvey> after = a.Surveys(Brian);
        Check.True(after.Count == 1 && after[0].At == start.ToLocalTime()
            && string.Join("+", after[0].Items) == "thread@3@20+kit@22@2",
            "joined on screen, its own survey from the restart");
        File.AppendAllLines(stocks,
            new[] { Brian.ToString("X") + "\tbad", Brian.ToString("X") + "\tnot-ticks\tx@1@1",
            Brian.ToString("X") + "\t" + start.AddHours(1).Ticks + "\t" });
        List<AtlasSurvey> l = a.Surveys(Brian);
        Check.True(l.Count == 2 && l[0].Items.Count == 0 && l[1].Items.Count == 2,
            "the bad lines skipped, the empty one with no rows: " + l.Count);
        using (new FileStream(stocks, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check.Equal(0, a.Surveys(Brian).Count, "a locked file reads as none");
            a.Stock(Brian, new List<string> { "kit@22@3" });
        }
        Check.Equal(2, a.Surveys(Brian).Count, "the write while locked is lost, nothing thrown");
    }

    static void TradeEdges()
    {
        string path = FreshPath();
        AssistAtlas a = new(path, new VirtualClock(new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)));
        Check.Equal(0, a.VendorTrades(Brian).Count, "no record");
        string trades = Sibling(path, "trades", ".csv");
        string s = Brian.ToString("X");
        File.WriteAllLines(trades, new[]
        {
            "time,kind,vendor,vendor serial,x,y,item,amount,unit price,total",
            "",
            "never,sell,\"Brian\"," + s + ",1,1,\"shirt\",5,27,135",
            "2026-10-24 08:00:00,sell,\"Brian\",zz,1,1,\"shirt\",5,27,135",
            "2026-10-24 08:00:00,sell,\"Brian\"," + s + ",x,1,\"shirt\",5,27,135",
            "2026-10-24 08:00:00,sell,\"Brian\"," + s + ",1,y,\"shirt\",5,27,135",
            "2026-10-24 08:00:00,sell,\"Brian\"," + s + ",1,1,\"shirt\",n,27,135",
            "2026-10-24 08:00:00,sell,\"Brian\"," + s + ",1,1,\"shirt\",5,p,135",
            "2026-10-24 08:00:00,sell,\"Brian, the tailor\"," + s + ",1,1,\"shirt\",5,27,135",
        });
        List<AtlasTrade> l = a.VendorTrades(Brian);
        Check.True(l.Count == 1 && l[0].Vendor == "Brian, the tailor",
            "only the good row, its quoted comma kept: " + l.Count);
        using (new FileStream(trades, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check.Equal(0, a.VendorTrades(Brian).Count, "a locked record reads as none");
    }

    static void VendorTrades()
    {
        string path = FreshPath();
        AssistAtlas a = new(path, new VirtualClock(new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)));
        string trades = Path.Combine(Path.GetDirectoryName(path)!,
            Path.GetFileName(path).Replace("atlas-", "trades-").Replace(".txt", ".csv"));
        List<string> lines = new()
        { "time,kind,vendor,vendor serial,x,y,item,amount,unit price,total" };
        DateTime t0 = new(2026, 10, 1, 8, 0, 0);
        for (int i = 0; i < 5001; i++)
            lines.Add(t0.AddMinutes(i).ToString("yyyy-MM-dd HH:mm:ss") + ",sell,\"Brian the tailor\","
                + Brian.ToString("X") + ",4400,1100,\"fancy shirt " + i + "\",5,27,135");
        lines.Add(t0.ToString("yyyy-MM-dd HH:mm:ss") + ",buy,\"Nana the tailor\"," + Nana.ToString("X")
            + ",1400,1600,\"thread\",20,3,60");
        lines.Add("not,a,row");
        File.WriteAllLines(trades, lines);
        List<AtlasTrade> brian = a.VendorTrades(Brian);
        Check.Equal(5001, brian.Count, "every one of Brian's");
        Check.Equal("fancy shirt 0", brian[0].Item, "the oldest first, past the 5000");
        Check.True(brian[5000].Kind == "sell" && brian[5000].Amount == 5 && brian[5000].Price == 27,
            "the newest read whole");
        Check.Equal(4998, a.Trades(Brian).Count,
            "Trades keeps the record's newest 5000 lines, Nana's and the bad row among them");
        List<AtlasTrade> nana = a.VendorTrades(Nana);
        Check.True(nana.Count == 1 && nana[0].Kind == "buy" && nana[0].Amount == 20, "Nana's one");
        Check.Equal(0, a.VendorTrades(0x00033333).Count, "none for a vendor with no trades");
    }

    static AtlasNpc? Find(AssistAtlas a, uint serial)
    {
        foreach (AtlasNpc n in a.Visible())
        {
            if (n.Serial == serial)
                return n;
        }
        return null;
    }

    static void TitleMakesBanker()
    {
        AssistAtlas a = Fresh();
        a.Seen(Danyelle, 0x191, 3736, 2156, "Danyelle", false);
        Check.Equal("Person", Find(a, Danyelle)!.Type, "bare");
        a.Named(Danyelle, "Danyelle the banker");
        AtlasNpc n = Find(a, Danyelle)!;
        Check.Equal("Banker", n.Type, "titled");
        Check.Equal("banker", n.Sub, "sub");
        Check.True(n.Kept, "kept");
    }

    static void BareKeepsTitle()
    {
        AssistAtlas a = Fresh();
        a.Seen(Danyelle, 0x191, 3736, 2156, "Danyelle", false);
        a.Named(Danyelle, "Danyelle the banker");
        a.Named(Danyelle, "Danyelle");
        a.Seen(Danyelle, 0x191, 3736, 2157, "Danyelle", false);
        Check.Equal("Danyelle the banker", Find(a, Danyelle)!.Name, "Name");
    }

    static void OtherNameReplaces()
    {
        AssistAtlas a = Fresh();
        a.Seen(Danyelle, 0x191, 3736, 2156, "Danyelle", false);
        a.Named(Danyelle, "Danyelle the banker");
        a.Named(Danyelle, "Danya");
        Check.Equal("Danya", Find(a, Danyelle)!.Name, "Name");
    }

    // 0x77: serial, body, x, y, z, direction, hue, flags, notoriety, 17 bytes.
    static byte[] Moved(uint serial, int body, int x, int y)
    {
        byte[] p = new byte[17];
        p[0] = 0x77;
        p[1] = (byte)(serial >> 24);
        p[2] = (byte)(serial >> 16);
        p[3] = (byte)(serial >> 8);
        p[4] = (byte)serial;
        p[5] = (byte)(body >> 8);
        p[6] = (byte)body;
        p[7] = (byte)(x >> 8);
        p[8] = (byte)x;
        p[9] = (byte)(y >> 8);
        p[10] = (byte)y;
        p[16] = 1;
        return p;
    }

    // 0x88: serial, 60 bytes of text, flags, 66 bytes.
    static byte[] Paperdoll(uint serial, string text)
    {
        byte[] p = new byte[66];
        p[0] = 0x88;
        p[1] = (byte)(serial >> 24);
        p[2] = (byte)(serial >> 16);
        p[3] = (byte)(serial >> 8);
        p[4] = (byte)serial;
        Encoding.ASCII.GetBytes(text).CopyTo(p, 5);
        return p;
    }

    static void PaperdollNames()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(3733, 2156, 0));
            s.ServerSends(Moved(Danyelle, 0x191, 3736, 2156));
            s.ServerSends(Paperdoll(Danyelle, "Danyelle the banker"));
            s.Clock.Sleep(1);
            AtlasNpc? n = Find(f.Atlas, Danyelle);
            Check.True(n is not null, "in the atlas");
            Check.Equal("Banker", n!.Type, "Type");
            Check.Equal("Danyelle the banker", n.Name, "Name");
        });
    }

    static void OwnPaperdoll()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(3733, 2156, 0));
            s.ServerSends(Paperdoll(Packets.Player, "Kwik the tailor"));
            s.Clock.Sleep(1);
            Check.True(Find(f.Atlas, Packets.Player) is null, "no row for the player");
        });
    }
}
