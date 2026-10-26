#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Uoaix.Assist;

// One file per relay session, rotated at 64 MB, the last 4 kept (assist-plan.md rule 8).
public sealed class AssistLog : IRelayLog
{
    const long Limit = 64L << 20; const int Keep = 4;
    // The stem names the relay's port: two relays (the shard's and uo1998's) started in the same second wrote one file.
    readonly Lock m_gate = new();
    readonly string m_dir, m_stem, m_pattern; StreamWriter m_w = null!; long m_written; int m_part;
    public string Path
    {
        get; private set;
    } = null!;
    // A recording's lines are copied into record-<port>-<time>.log, which rotation never deletes: the keep-last-4 rule
    // deleted Damian's smithing recording two hours after he made it (2026-10-18). Bounded at Limit like a log.
    readonly int m_port; StreamWriter? m_rec = null; long m_recWritten;
    public string? Record(bool on)
    {
        lock (m_gate)
        {
            if (m_rec != null)
            {
                m_rec.Dispose();
                m_rec = null;
            }
            if (!on)
                return null;
            string p = System.IO.Path.Combine(m_dir,
                "record-" + m_port + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            m_rec = new StreamWriter(p, false, new UTF8Encoding(false));
            m_recWritten = 0;
            return p;
        }
    }
    // A client crash (client-crashes.md): the last RecentKeep lines, whole, written to crash-<port>-<time>.log, which
    // rotation never deletes. The tail below is cut to 300 characters a line for the window.
    public const int RecentKeep = 1000;
    readonly Queue<string> m_recent = new();
    public string Crash(string why)
    {
        string p = System.IO.Path.Combine(m_dir,
            "crash-" + m_port + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        try
        {
            lock (m_gate)
                File.WriteAllText(p,
                    "CRASH " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (local; the lines below are UTC): "
                    + why + "\r\n" + string.Join("\r\n", m_recent) + "\r\n");
            return p;
        }
        catch { return null!; }
    }
    public AssistLog(string dir, int port)
    {
        m_dir = dir;
        m_port = port;
        Directory.CreateDirectory(dir);
        m_pattern = "assist-" + port + "-*.log";
        m_stem = "assist-" + port + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Open();
    }
    void Open()
    {
        if (m_w != null)
            m_w.Dispose();
        Path = System.IO.Path.Combine(m_dir, m_stem + (m_part == 0 ? "" : "-" + m_part) + ".log");
        m_part++;
        m_w = new StreamWriter(Path, false, new UTF8Encoding(false));
        m_written = 0;
        FileInfo[] all = new DirectoryInfo(m_dir).GetFiles(m_pattern);
        Array.Sort(all, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int i = Keep; i < all.Length; i++)
            if (all[i].FullName != Path)
                try
                {
                    all[i].Delete();
                }
                catch { }
    }
    // The last TailKeep lines, for the window's Log tab; Lined fires for each line after it is written (the fail-safe
    // watches it). The file is flushed every line: a size of 0 in a directory listing is Windows reporting a file
    // another process holds open, not unwritten lines.
    public const int TailKeep = 400;
    readonly List<string> m_tail = new(); long m_tailSeq;
    public event Action<string> Lined = null!;
    public void Line(int id, string dir, string text)
    {
        string s = DateTime.UtcNow.ToString("HH:mm:ss.fff") + " c" + id + " " + dir + " " + text;
        lock (m_gate)
        {
            if (m_written + s.Length + 2 > Limit)
                Open();
            m_w.WriteLine(s);
            m_w.Flush();
            m_written += s.Length + 2;
            if (m_rec != null && m_recWritten + s.Length + 2 <= Limit)
            {
                m_rec.WriteLine(s);
                m_rec.Flush();
                m_recWritten += s.Length + 2;
            }
            m_recent.Enqueue(s);
            if (m_recent.Count > RecentKeep)
                m_recent.Dequeue();
            m_tail.Add(s.Length > 300 ? s.Substring(0, 300) : s);
            m_tailSeq++;
            if (m_tail.Count > TailKeep)
                m_tail.RemoveRange(0, m_tail.Count - TailKeep);
        }
        Action<string> h = Lined;
        if (h != null)
            try
            {
                h(s);
            }
            catch { }
    }
    public List<string> TailSince(ref long seq)
    {
        lock (m_gate)
        {
            long first = m_tailSeq - m_tail.Count;
            int from = (int)Math.Max(0, seq - first);
            seq = m_tailSeq;
            return m_tail.GetRange(from, m_tail.Count - from);
        }
    }
}

public sealed class AssistRelay
{
    sealed class Ticket
    {
        public string Host = null!; public int Port; public DateTime At;
    }
    readonly int m_listen;
    readonly string m_loginHost;
    readonly int m_loginPort;
    readonly string? m_gameHost = null; readonly int m_gamePort; readonly int[] m_lengths; readonly UoHuffman m_huff;
    readonly AssistLog m_log; Map<uint, Ticket> m_tickets = Map<uint, Ticket>.Empty; int m_next;
    readonly Lock m_ticketsGate = new();
    // gameHost null follows each 0x8C. A shard copy on a forwarded port names its guest-side port in 0x8C (2593, the
    // live shard's), so its game server is given here instead.
    public AssistRelay(int listen, string loginHost, int loginPort, string gameHost, int gamePort, int[] lengths,
        int[] book, string logDir)
    {
        m_listen = listen;
        m_loginHost = loginHost;
        m_loginPort = loginPort;
        m_gameHost = string.IsNullOrEmpty(gameHost) ? null : gameHost;
        m_gamePort = gamePort;
        m_lengths = lengths;
        m_huff = new UoHuffman(book);
        m_log = new AssistLog(logDir, listen);
        Features = new AssistFeatures(m_log, new AssistSettings(s_settingsPath));
        Features.Server = loginHost + ":" + loginPort + ".";
        AssistGuard.Current = new AssistGuard(Features.Cfg, m_log);
    }
    public AssistLog Log => m_log;
    public readonly AssistFeatures Features;
    static string s_settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uoaix-assist", "settings.txt");
    public static string SettingsPath { get => s_settingsPath; set => s_settingsPath = value; }
    // The sources' folder, set by assist.ps1: the data shipped beside them (teleporters-public.txt).
    static string s_sourceDir = "";
    public static string SourceDir { get => s_sourceDir; set => s_sourceDir = value; }
    public string LogPath => m_log.Path;
    public int ListenPort => m_listen;
    // The client's end of its newest connection: the window finds the client's process (and its window) by it.
    public volatile int LastClientPort;

    Map<int, GameLink> m_links = Map<int, GameLink>.Empty; readonly Lock m_linksGate = new();
    public const int StateSeconds = 30;

    // The newest game connection still open, or null.
    public GameLink? Link()
    {
        lock (m_linksGate)
        {
            GameLink? best = null;
            foreach (KeyValuePair<int, GameLink> kv in m_links)
                if (!kv.Value.Closed && (best == null || kv.Key > best.Id))
                    best = kv.Value;
            return best;
        }
    }

    public void Run()
    {
        TcpListener listener = new(IPAddress.Loopback, m_listen);
        listener.Start();
        m_log.Line(0, "--",
            "LISTEN 127.0.0.1:" + m_listen + " login " + m_loginHost + ":" + m_loginPort + " control 127.0.0.1:"
            + (m_listen + 1));
        Thread census = new(() =>
        {
            while (true)
            {
                Thread.Sleep(StateSeconds * 1000);
                lock (m_linksGate)
                    foreach (KeyValuePair<int, GameLink> kv in m_links)
                        m_log.Line(kv.Key, "--", "STATE " + kv.Value.State.Summary());
            }
        });
        census.IsBackground = true;
        census.Start();
        Thread control = new(Control);
        control.IsBackground = true;
        control.Start();
        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            int id = Interlocked.Increment(ref m_next);
            Thread t = new(() => Serve(id, client));
            t.IsBackground = true;
            t.Start();
        }
    }

    static bool ReadExact(NetworkStream s, byte[] b, int n)
    {
        for (int at = 0; at < n;)
        {
            int r = s.Read(b, at, n - at);
            if (r <= 0)
                return false;
            at += r;
        }
        return true;
    }

    // The first 4 client bytes are the keystream seed; on a game connection they are the key the server's
    // 0x8C named, and the remembered ticket for that key says where the game server is.
    void Serve(int id, TcpClient client)
    {
        TcpClient? server = null;
        try
        {
            client.NoDelay = true;
            NetworkStream cs = client.GetStream();
            byte[] seed = new byte[4];
            LastClientPort = (((IPEndPoint?)client.Client.RemoteEndPoint)!).Port;
            if (!ReadExact(cs, seed, 4))
            {
                m_log.Line(id, "--", "CLOSE before seed");
                return;
            }
            uint key = ((uint)seed[0] << 24) | ((uint)seed[1] << 16) | ((uint)seed[2] << 8) | seed[3];
            Ticket? t;
            bool game;
            lock (m_ticketsGate)
            {
                game = m_tickets.TryGet(key, out t);
                m_tickets = m_tickets.Remove(key);
            }
            string host = game ? t!.Host : m_loginHost;
            int port = game ? t!.Port : m_loginPort;
            m_log.Line(id, "--",
                "OPEN " + (game ? "game" : "login") + " seed " + key.ToString("X8") + " -> " + host + ":" + port);
            server = new TcpClient(host, port);
            server.NoDelay = true;
            NetworkStream ss = server.GetStream();
            ss.Write(seed, 0, 4);
            if (game)
            {
                RunGame(id, cs, ss, key);
                return;
            }
            Thread up = new(() => PumpUp(id, cs, ss, key));
            up.IsBackground = true;
            up.Start();
            PumpLoginDown(id, ss, cs);
            up.Join(2000);
        }
        catch (Exception e) { m_log.Line(id, "--", "ERROR " + e.Message); }
        finally { try { client.Close(); } catch { } try { if (server
            != null) server.Close(); } catch { } m_log.Line(id, "--", "CLOSE"); }
    }

    // Client bytes go upstream unchanged; the log shows them decrypted, the password masked (rule 7:
    // 0x80 bytes 31-60, 0x91 bytes 35-64 of the connection's first packet).
    void PumpUp(int id, NetworkStream from, NetworkStream to, uint seed)
    {
        byte[] buf = new byte[8192];
        UoKeystream ks = new(seed);
        long pos = 0;
        int op = -1;
        try
        {
            while (true)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0)
                    break;
                to.Write(buf, 0, n);
                StringBuilder sb = new(n * 2);
                for (int i = 0; i < n; i++)
                {
                    byte p = ks.Apply(buf[i]);
                    if (op < 0)
                        op = p;
                    long at = pos++;
                    if ((op == 0x80 && at >= 31 && at <= 60) || (op == 0x91 && at >= 35 && at <= 64))
                        sb.Append("**");
                    else
                        sb.Append(p.ToString("X2"));
                }
                m_log.Line(id, "C>S", sb.ToString());
            }
        }
        catch (Exception e) { m_log.Line(id, "--", "C>S END " + e.Message); }
        try
        {
            to.Close();
        }
        catch { }
    }

    // Login replies are plain. Each whole packet goes on to the client; a 0x8C is rewritten to name this
    // relay, and the real game server is remembered under its key.
    void PumpLoginDown(int id, NetworkStream from, NetworkStream to)
    {
        byte[] buf = new byte[8192];
        UoFramer framer = new(m_lengths);
        bool raw = false;
        try
        {
            while (true)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0)
                    break;
                if (raw)
                {
                    to.Write(buf, 0, n);
                    m_log.Line(id, "S>C", "raw " + UoHex.Of(buf, 0, n));
                    continue;
                }
                framer.Feed(buf, 0, n, p =>
                {
                    if (p[0] == 0x8C)
                        Redirect(id, p);
                    m_log.Line(id, "S>C", UoHex.Of(p));
                    to.Write(p, 0, p.Length);
                });
                if (framer.Desync != null)
                {
                    raw = true;
                    byte[] rest = framer.TakeBuffered();
                    to.Write(rest, 0, rest.Length);
                    m_log.Line(id, "S>C",
                        "DESYNC login " + framer.Desync + "; no 0x8C after this is redirected: raw " + UoHex.Of(rest));
                }
            }
        }
        catch (Exception e) { m_log.Line(id, "--", "S>C END " + e.Message); }
        try
        {
            to.Close();
        }
        catch { }
    }

    void Redirect(int id, byte[] p)
    {
        string host = p[1] + "." + p[2] + "." + p[3] + "." + p[4];
        int port = (p[5] << 8) | p[6];
        uint key = ((uint)p[7] << 24) | ((uint)p[8] << 16) | ((uint)p[9] << 8) | p[10];
        DateTime now = DateTime.UtcNow;
        string to = m_gameHost ?? host;
        int toPort = m_gameHost != null ? m_gamePort : port;
        lock (m_ticketsGate)
        {
            foreach (KeyValuePair<uint, Ticket> kv in m_tickets)
                if ((now - kv.Value.At).TotalMinutes > 10)
                    m_tickets = m_tickets.Remove(kv.Key);
            m_tickets = m_tickets.Set(key, new Ticket { Host = to, Port = toPort, At = now });
        }
        p[1] = 127;
        p[2] = 0;
        p[3] = 0;
        p[4] = 1;
        p[5] = (byte)(m_listen >> 8);
        p[6] = (byte)m_listen;
        m_log.Line(id, "--",
            "REDIRECT key " + key.ToString("X8") + " " + host + ":" + port
            + (m_gameHost != null ? " (game server given: " + to + ":" + toPort + ")" : "") + " -> 127.0.0.1:"
            + m_listen);
    }

    void RunGame(int id, NetworkStream cs, NetworkStream ss, uint key)
    {
        GameLink link = new(id, cs, ss, key, m_lengths, m_huff, m_log, SystemClock.Instance,
            () => (AssistGuard.Current != null ? AssistGuard.Current.Disabled : null)!);
        lock (m_linksGate)
            m_links = m_links.Set(id, link);
        Features.Attach(link);
        try
        {
            link.Run();
        }
        finally { lock (m_linksGate) m_links = m_links.Remove(id); m_log.Line(id, "--", "STATE " + link.State.Summary()); }
    }

    // Loopback control port (listen + 1), one command a line, one answer line each: the headless way to drive what the
    // window drives. state | journal [n] | items | use <serial> | target <x> <y> <z> [graphic] | target-object <serial>
    // | tell <text>
    void Control()
    {
        TcpListener l = new(IPAddress.Loopback, m_listen + 1);
        l.Start();
        while (true)
        {
            TcpClient c = l.AcceptTcpClient();
            Thread t = new(() =>
            {
                try
                {
                    using (c)
                    {
                        NetworkStream s = c.GetStream();
                        StreamReader r = new(s, Encoding.ASCII);
                        StreamWriter w = new(s, new UTF8Encoding(false))
                        {
                            AutoFlush = true
                        };
                        string? line;
                        while ((line = r.ReadLine()) != null)
                        {
                            if (Http(line))
                            {
                                m_log.Line(0, "--",
                                    "CONTROL refused an HTTP request: "
                                    + (line.Length > 80 ? line.Substring(0, 80) : line));
                                break;
                            }
                            string answer;
                            try
                            {
                                answer = Command(line.Trim());
                            }
                            catch (Exception e) { answer = "ERROR " + e.Message; }
                            w.WriteLine(answer);
                        }
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // Any web page can POST text/plain to a loopback port with no preflight, and each line of its body would run as a
    // command; the request line and the headers come first, so the connection ends there.
    static bool Http(string line)
    {
        foreach (string m in new[] { "GET ", "POST ", "HEAD ", "OPTIONS ", "PUT ", "DELETE ", "CONNECT " })
            if (line.StartsWith(m, StringComparison.Ordinal))
                return true;
        return line.Contains(" HTTP/") || line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase);
    }

    public string Command(string line)
    {
        string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0)
            return "ERROR empty";
        if (a[0] == "window")
            return AssistWindow.Current == null ? "no window"
                : a.Length > 2 && a[1] == "snapshot"
                ? AssistWindow.Current.Snapshot(line.Substring(line.IndexOf("snapshot") + 9))
                : AssistWindow.Current.Report();
        GameLink? k = Link();
        if (k == null)
            return "ERROR no game connection";
        Func<string, string> done = why => why == null ? "OK" : "REFUSED " + why;
        Func<string, uint> hex = v => Convert.ToUInt32(v.StartsWith("0x") ? v.Substring(2) : v, 16);
        switch (a[0])
        {
            case "state":
                return k.State.Summary() + (k.Halted != null ? " HALTED " + k.Halted : "")
                    + (k.InFlight != null ? " in-flight " + k.InFlight : "") + (k.MacroCursor ? " assist-cursor" : "");
            case "journal":
                {
                    int n = a.Length > 1 ? int.Parse(a[1]) : 5;
                    lock (k.State.Gate)
                    {
                        List<string> j = k.State.Journal;
                        return string.Join(" | ", j.GetRange(Math.Max(0, j.Count - n), Math.Min(n, j.Count)));
                    }
                }
            case "items":
                lock (k.State.Gate)
                {
                    StringBuilder sb = new();
                    foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                        sb.Append(kv.Key.ToString("X") + ":" + kv.Value.Graphic.ToString("X4") + "x" + kv.Value.Amount
                            + (kv.Value.Layer >= 0 ? "@L" + kv.Value.Layer : "@" + kv.Value.Container.ToString("X"))
                            + " ");
                    return sb.ToString().TrimEnd();
                }
            case "use":
                return done(k.Use(hex(a[1]))!);
            case "target":
                return done(k.Target(true, 0, int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]),
                    a.Length > 4 ? (int)hex(a[4]) : 0)!);
            case "target-object":
                {
                    uint s = hex(a[1]);
                    AssistItem? it;
                    lock (k.State.Gate)
                    {
                        if (!k.State.Items.TryGet(s, out it))
                            k.State.Ground.TryGet(s, out it);
                    }
                    return done(k.Target(false, s, it != null ? it.X : 0, it != null ? it.Y : 0, it != null ? it.Z : 0,
                        it != null ? it.Graphic : 0)!);
                }
            case "tell":
                return done(k.Tell(line.Substring(line.IndexOf(' ') + 1))!);
            // The tailoring circuit's steps (assist-plan.md stage 6): say <text> | vendor-buy <hex art,...> <most>
            // [name] | vendor-sell <hex art,...> <floor gp> [name] | recall <rune serial>. A name asks that vendor, not
            // every one. A line said here counts as answered when it opens a window (a "bank" that opened the box), so
            // the circuit's own "bank" after it is not refused as an unanswered repeat (2026-10-17, Vesper and
            // Nujel'm).
            case "say":
                return done(Features.SayHeard(line.Substring(line.IndexOf(' ') + 1))!);
            case "vendor-buy":
            case "vendor-sell":
                {
                    if (a.Length < 3)
                        return "ERROR " + a[0] + " <hex art,...> <" + (a[0] == "vendor-buy" ? "most" : "floor gp")
                            + ">";
                    Set<int> arts = Set<int>.Empty;
                    foreach (string s in a[1].Split(','))
                        arts = arts.Add((int)hex(s));
                    int n = int.Parse(a[2]);
                    string who = a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "vendor";
                    if (a[0] == "vendor-buy")
                    {
                        int bought, left;
                        string? why = Features.VendorBuy(g => arts.Contains(g), n, out bought, out left, who);
                        return why == null ? "OK bought " + bought + ", " + left + " left in stock" : "REFUSED " + why;
                    }
                    {
                        int sold, gold, under;
                        string? why = Features.VendorSell(g => arts.Contains(g), n, out sold, out gold, out under, who);
                        return why == null
                            ? "OK sold " + sold + " for " + gold
                            + (under > 0 ? ", offered " + under + " for others" : "") : "REFUSED " + why;
                    }
                }
            case "recall":
                return done(Features.Recall(hex(a[1]))!);
            // runes [reach]: the runes on the ground within reach (default 12) and their labels, nearest first.
            case "runes":
                return string.Join(" | ", Features.RuneLabels(a.Length > 1 ? int.Parse(a[1]) : 12));
            case "pack-runes":
                return string.Join(" | ", Features.PackRuneLabels(k));
            // mobiles [reach]: the mobiles in view (AssistState.Mobiles) within reach (default 18), nearest first.
            case "mobiles":
                {
                    int reach = a.Length > 1 ? int.Parse(a[1]) : 18;
                    List<string> rows = new();
                    lock (k.State.Gate)
                    {
                        int px = k.State.X, py = k.State.Y;
                        foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                        {
                            int d = Math.Max(Math.Abs(kv.Value[0] - px), Math.Abs(kv.Value[1] - py));
                            if (d <= reach)
                            {
                                string? n;
                                int b;
                                k.State.Names.TryGet(kv.Key, out n);
                                k.State.Bodies.TryGet(kv.Key, out b);
                                rows.Add(d.ToString("D2") + " " + kv.Key.ToString("X") + " " + (n ?? "?") + " body "
                                    + b.ToString("X") + " at " + kv.Value[0] + "," + kv.Value[1]);
                            }
                        }
                    }
                    rows.Sort();
                    return string.Join(" | ", rows);
                }
            case "peek":
                return done(Features.Peek(hex(a[1]))!);
            // houses: the registered houses. house-register <vendor> [name]: the house where the player stands
            // (unnamed: its sign's name), its vendor and the one ring on it, its devices scanned. house-mode
            // <public|private> <name>. house-forget <name>.
            case "houses":
                {
                    List<AssistFeatures.HouseRec> hs = Features.Houses();
                    if (hs.Count == 0)
                        return "none";
                    List<string> rows = new();
                    foreach (AssistFeatures.HouseRec h in hs)
                        rows.Add(h.Name + ": " + (h.Private ? "private" : "public") + ", door " + h.DoorX + ","
                            + h.DoorY + ", vendor " + h.Vendor.ToString("X") + ", ring " + h.Ring.ToString("X") + ", "
                            + (h.Devices.Length > 0 ? h.Devices : "storage"));
                    return string.Join(" | ", rows);
                }
            case "house-register":
                {
                    if (a.Length < 2)
                        return "ERROR house-register <vendor> [name]";
                    string? w = Features.HouseRegister(a.Length > 2
                        ? line.Substring(line.IndexOf(a[1]) + a[1].Length + 1).Trim() : "", hex(a[1]));
                    return w == null ? "OK " + Features.HouseStatus : "REFUSED " + w;
                }
            case "house-mode":
                {
                    if (a.Length < 3 || a[1] != "public" && a[1] != "private")
                        return "ERROR house-mode <public|private> <name>";
                    string? w = Features.HouseMode(line.Substring(line.IndexOf(a[1]) + a[1].Length + 1).Trim(),
                        a[1] == "private");
                    return w == null ? "OK " + Features.HouseStatus : "REFUSED " + w;
                }
            case "house-forget":
                {
                    if (a.Length < 2)
                        return "ERROR house-forget <name>";
                    string? w = Features.HouseForget(line.Substring(line.IndexOf(' ') + 1).Trim());
                    return w == null ? "OK " + Features.HouseStatus : "REFUSED " + w;
                }
            // rename <rune> <text>: the rune's description, answered to the prompt its double-click opens.
            case "rename":
                if (a.Length < 3)
                    return "ERROR rename <rune> <text>";
                return done(Features.Rename(hex(a[1]), line.Substring(line.IndexOf(a[1]) + a[1].Length + 1).Trim())!);
            // ground [reach]: the ground items within reach (default 12), nearest first, named by the client's tile
            // data.
            case "ground":
                {
                    int reach = a.Length > 1 ? int.Parse(a[1]) : 12;
                    List<AssistItem> list = new();
                    int px, py;
                    lock (k.State.Gate)
                    {
                        px = k.State.X;
                        py = k.State.Y;
                        foreach (AssistItem g in k.State.Ground.Values)
                            if (Math.Max(Math.Abs(g.X - px), Math.Abs(g.Y - py)) <= reach)
                                list.Add(g);
                    }
                    list.Sort((x, y) =>
                        Math.Max(Math.Abs(x.X - px),
                        Math.Abs(x.Y - py)).CompareTo(Math.Max(Math.Abs(y.X - px), Math.Abs(y.Y - py))));
                    return string.Join(" | ",
                        list.ConvertAll(g =>
                        g.Serial.ToString("X") + " " + UoTiles.Name(g.Graphic) + " " + g.Graphic.ToString("X4")
                        + (g.Amount > 1 ? " x" + g.Amount : "") + " at " + g.X + "," + g.Y + "," + g.Z));
                }
            case "work":
                {
                    string? w = Features.WorkUserToggle();
                    return w == null ? "OK " + Features.WorkState : "REFUSED " + w;
                }
            // sew: fancy shirts from all the pack's cloth | circuit go|stop|status.
            case "sew":
                {
                    int made, failed;
                    string? why = Features.SewAll(out made, out failed);
                    return (why == null ? "OK" : "STOPPED " + why) + ": " + made + " made, " + failed + " failed";
                }
            // bank-deposit [keep]: the pack's gold over keep (default 300) onto the bank's piles.
            case "bank-deposit":
                {
                    int put;
                    string? why = Features.BankDeposit(a.Length > 1 ? int.Parse(a[1]) : Features.PurseKeep, out put);
                    return (why == null ? "OK" : "STOPPED " + why) + ": put " + put;
                }
            // bank-take <hex art,...> <count>: from the bank box into the pack until the pack holds count of each.
            case "bank-take":
                {
                    if (a.Length < 3)
                        return "ERROR bank-take <hex art,...> <count>";
                    List<int> arts = new();
                    foreach (string s in a[1].Split(','))
                        arts.Add((int)hex(s));
                    string taken;
                    string? why = Features.BankTake(arts, int.Parse(a[2]), out taken);
                    return (why == null ? "OK" : "STOPPED " + why) + ": " + taken;
                }
            // contents <container>: its items, serial art xamount at x,y in the container. put <hex art,...>
            // <container>: every pack stack of each art into that container.
            case "contents":
                {
                    uint box = hex(a[1]);
                    List<string> rows = new();
                    lock (k.State.Gate)
                        foreach (AssistItem it in k.State.Items.Values)
                            if (it.Container == box)
                                rows.Add(it.Serial.ToString("X") + " " + UoTiles.Name(it.Graphic) + " "
                                    + it.Graphic.ToString("X4") + " x" + it.Amount + " at " + it.X + "," + it.Y);
                    return string.Join(" | ", rows);
                }
            // labels <container>: each item in it single-clicked where it lies, and its label.
            case "labels":
                return string.Join(" | ", Features.ContainerLabels(hex(a[1])));
            // replies: the NPC answers no known kind matched, newest last, for the one driving to judge.
            case "replies":
                lock (Features.UnknownReplies)
                    return Features.UnknownReplies.Count == 0 ? "none" : string.Join(" | ", Features.UnknownReplies);
            // travel <city>: from the nearest bank, by the bank's rune for the city.
            case "travel":
                return done(Features.BankRecall(k, line.Substring(line.IndexOf(' ') + 1).Trim())!);
            // names [reach]: each mobile in view within reach (default 12) single-clicked, and its name.
            case "names":
                return string.Join(" | ", Features.MobileLabels(a.Length > 1 ? int.Parse(a[1]) : 12));
            // get <serial> [amount] [container]: one item off the ground or out of any container into the container (the
            // pack unset), amount of it (all unset, into the pack only what the carry limit takes).
            case "get":
                {
                    if (a.Length < 2)
                        return "ERROR get <serial> [amount] [container]";
                    int amount = a.Length > 2 ? int.Parse(a[2]) : 0;
                    uint into = a.Length > 3 ? hex(a[3]) : 0;
                    string? why = Features.GetItem(hex(a[1]), amount, into, out int moved);
                    return why == null ? "OK got " + moved : "STOPPED " + why;
                }
            // move <serial,...> <container>: exactly these items into the container, each lifted whole and dropped in
            // it.
            case "move":
                {
                    if (a.Length < 3)
                        return "ERROR move <serial,...> <container>";
                    List<uint> serials = new();
                    foreach (string s in a[1].Split(','))
                        serials.Add(hex(s));
                    string? why = Features.MoveInto(serials, hex(a[2]));
                    return why == null ? "OK moved " + serials.Count : "STOPPED " + why;
                }
            // fill <container> <count> [hex art,...] [from <source>]: each art (default the reagents) in the container
            // brought to count, from and to the source's loose stacks (default the pack).
            // mine-setup [forge | mode 1|2 | toolbag | storage backpack|stash|vendor | bank-rune <label> | shovels <n>
            // | store-at <n> | tinker <pick|pick> | ore-to | feet on|off | collect-at <n> | cave mark <name> [radius] |
            // cave pick <name> | cave rune <name> | <label> | cave forget <name> | cave show [name] | caves]: the
            // mining circuit's setup (ore-to a target cursor for where the ore piles at feet are collected); nothing:
            // the setup as it stands.
            case "mine-setup":
                return Features.MineSetup(a);
            // hop-to-me: your tile kept and the object cursor sent (Actions, Bunny hop); again while it runs, stopped.
            case "hop-to-me":
                {
                    string? w = Features.HopToMe();
                    return w == null ? "OK " + Features.HopStatus : "ERROR " + w;
                }
            // lumber-collect: the piles at feet collected now (Collect now).
            case "lumber-collect":
                {
                    string? w = Features.CollectNow();
                    return w == null ? "OK collecting" : "ERROR " + w;
                }
            // mine-collect: the ore piles at feet collected now (the mining rows' Collect now).
            case "mine-collect":
                {
                    string? w = Features.MineCollectNow();
                    return w == null ? "OK collecting" : "ERROR " + w;
                }
            // lumber-setup axe|logs|from|from here|clear-empty|feet on|off: a target cursor for the axe, for where the
            // logs go or for where the chopping starts from; Chop from unset; the chunks found empty forgotten; At my
            // feet; no word: the setup as it stands, with the piles at feet and the collection.
            case "lumber-setup":
                {
                    if (a.Length > 2 && a[1] == "feet" && (a[2] == "on" || a[2] == "off"))
                        Features.LumberAtFeet = a[2] == "on";
                    else if (a.Length > 1 && a[1] == "feet")
                        return "ERROR lumber-setup feet on|off";
                    if (a.Length > 2 && a[1] == "from" && a[2] == "here")
                        Features.LumberFrom = "";
                    if (a.Length > 1 && a[1] == "clear-empty")
                        Features.LumberEmptyClear();
                    string? w = a.Length > 1 && a[1] == "axe" ? Features.LumberAxeTarget()
                        : a.Length > 1 && a[1] == "logs" ? Features.LumberLogsTarget()
                        : a.Length == 2 && a[1] == "from" ? Features.LumberFromTarget() : null;
                    if (a.Length > 1 && w != null)
                        return "ERROR " + w;
                    return "OK axe " + (Features.LumberAxe != 0 ? Features.LumberAxe.ToString("X") : "none") + " ; "
                        + Features.LumberLogsText() + " ; " + Features.LumberFromText() + " ; "
                        + Features.LumberEmpties().Count + " chunks found empty ; " + Features.LumberSentinelText()
                        + " ; at feet " + Features.LumberDropped() + " logs in " + Features.LumberPiles().Count
                        + " piles (" + Features.LumberWild() + " wild), the collection at " + Features.LumberCollectAt
                        + (Features.CollectStatus.Length > 0 ? " ; collection " + Features.CollectStatus : "")
                        + (Features.LumberStatus.Length > 0 ? " ; " + Features.LumberStatus : "");
                }
            // feathers-setup blade|place|feet on|off: a target cursor for the blade or for where the feathers go; At my
            // feet; no word: the setup as it stands.
            case "feathers-setup":
                {
                    if (a.Length > 2 && a[1] == "feet" && (a[2] == "on" || a[2] == "off"))
                        Features.FeatherAtFeet = a[2] == "on";
                    else if (a.Length > 1 && a[1] == "feet")
                        return "ERROR feathers-setup feet on|off";
                    string? w = a.Length > 1 && a[1] == "blade" ? Features.FeatherBladeTarget()
                        : a.Length > 1 && a[1] == "place" ? Features.FeathersToTarget() : null;
                    if (a.Length > 1 && w != null)
                        return "ERROR " + w;
                    return "OK blade " + (Features.FeatherBlade != 0 ? Features.FeatherBlade.ToString("X") : "none")
                        + " ; " + Features.FeathersText() + " ; range " + Features.FeatherRadius + " ; at feet "
                        + Features.FeatherDropped() + " feathers in " + Features.FeatherPileCount()
                        + " piles, the collection at " + Features.FeatherCollectAt
                        + (Features.FeatherStatus.Length > 0 ? " ; " + Features.FeatherStatus : "");
                }
            // smith-ingots [bank | buy | mine]: where the smithing circuit's ingots come from; nothing: the setting.
            case "smith-ingots":
                if (a.Length > 1)
                    Features.SmithIngots = a[1];
                return "ingots from " + Features.SmithIngots;
            // advert [title <words> | text <words, | a new line> | author <words> | per <n> | price <gp> | places
            // <p;p...> | here]: the advertiser's setup; here adds where the player stands; nothing: the setup.
            case "advert":
                {
                    string rest = a.Length > 2 ? line.Substring(line.IndexOf(a[1], 6) + a[1].Length).Trim() : "";
                    if (a.Length > 1)
                    {
                        switch (a[1])
                        {
                            case "title":
                                Features.AdTitle = rest;
                                break;
                            case "text":
                                Features.AdText = rest;
                                break;
                            case "author":
                                Features.AdAuthor = rest;
                                break;
                            case "per":
                                {
                                    int n;
                                    if (int.TryParse(rest, out n))
                                        Features.AdPer = n;
                                    break;
                                }
                            case "price":
                                {
                                    int n;
                                    if (int.TryParse(rest, out n))
                                        Features.AdPrice = n;
                                    break;
                                }
                            case "places":
                                Features.SetAdPlaces(new List<string>(rest.Split(new[] { ';' },
                                    StringSplitOptions.RemoveEmptyEntries)));
                                break;
                            case "here":
                                return "OK " + Features.AdAddHere();
                            default:
                                return "ERROR advert title|text|author|per|price|places|here";
                        }
                    }

                    return "title " + Features.AdTitle + " ; author "
                        + (Features.AdAuthor.Length > 0 ? Features.AdAuthor : "the player") + " ; per "
                        + Features.AdPer + " ; price " + Features.AdPrice + " ; places "
                        + string.Join(";", Features.AdPlaces()) + " ; text " + Features.AdText;
                }
            // spellbook-setup [count <n> | chance <percent> | inscribe on|off | price <gp> | scroll-price <gp>]: the
            // spellbook maker's setup; nothing: the setup.
            case "spellbook-setup":
                {
                    int n;
                    bool num = a.Length > 2 && int.TryParse(a[2], out n);
                    if (a.Length > 2)
                    {
                        switch (a[1])
                        {
                            case "count":
                                if (num)
                                    Features.SpellbookCount = int.Parse(a[2]);
                                break;
                            case "chance":
                                if (num)
                                    Features.SpellbookChance = int.Parse(a[2]);
                                break;
                            case "inscribe":
                                Features.SpellbookInscribe = a[2] == "on";
                                break;
                            case "price":
                                if (num)
                                    Features.SpellbookPrice = int.Parse(a[2]);
                                break;
                            case "scroll-price":
                                if (num)
                                    Features.ScrollPrice = int.Parse(a[2]);
                                break;
                            default:
                                return "ERROR spellbook-setup count|chance|inscribe|price|scroll-price <value>";
                        }
                    }

                    return "count " + Features.SpellbookCount + " ; inscribe "
                        + (Features.SpellbookInscribe ? "on at " + Features.SpellbookChance + "%" : "off")
                        + " ; price " + Features.SpellbookPrice + " ; scroll-price " + Features.ScrollPrice;
                }
            // mark-setup [class <bank|shop kind> | per <n> | price <gp>]: the rune marker's setup; nothing: the setup.
            case "mark-setup":
                {
                    if (a.Length > 2)
                    {
                        int n;
                        if (a[1] == "class")
                            Features.MarkClass = a[2];
                        else if (a[1] == "per" && int.TryParse(a[2], out n))
                            Features.MarkPer = n;
                        else if (a[1] == "price" && int.TryParse(a[2], out n))
                            Features.MarkPrice = n;
                        else
                            return "ERROR mark-setup class|per|price <value>";
                    }
                    return "class " + Features.MarkClass + " ; per " + Features.MarkPer + " ; price "
                        + Features.MarkPrice + " ; classes " + string.Join(",", AssistFeatures.MarkClasses());
                }
            // ingots [count] [highest gp]: the ingot-buying circuit's count and highest price; nothing: the setting.
            case "ingots":
                {
                    int n;
                    if (a.Length > 1 && int.TryParse(a[1], out n))
                        Features.IngotBuyCount = n;
                    if (a.Length > 2 && int.TryParse(a[2], out n))
                        Features.IngotBuyPrice = n;
                    return "OK ingots " + Features.IngotBuyCount + " at " + Features.IngotBuyPrice + " gp at most";
                }
            // smith-ladder [mine | guess | item@skill;item@skill...]: the smithing gain ladder, which one is used, or
            // the player's own steps; nothing: both, and what each picks now.
            case "smith-ladder":
                {
                    string rest = line.Length > 13 ? line.Substring(13).Trim() : "";
                    if (rest == "mine" || rest == "guess")
                        Features.SmithLadderMode = rest;
                    else if (rest.Length > 0)
                        Features.SmithLadder = rest;
                    int sk;
                    lock (k.State.Gate)
                        k.State.Skills.TryGet(AssistFeatures.SmithySkill + 1, out sk);
                    return "mode " + Features.SmithLadderMode + " ; ladder " + Features.SmithLadder + " ; at "
                        + (sk / 10.0).ToString("0.0") + ": " + (Features.SmithNow(sk) ?? "none");
                }
            // record: a recording started or stopped (Features.RecordToggle); record status: whether one runs.
            case "housegrade":
                return Features.HouseGrade();
            case "record":
                return a.Length > 1 && a[1] == "status" ? (Features.Recording ? "recording" : "not recording")
                    : Features.RecordToggle("the control port");
            case "fill":
                {
                    if (a.Length < 3)
                        return "ERROR fill <container> <count> [hex art,...] [from <source>]";
                    int at = Array.IndexOf(a, "from");
                    if (at >= 0 && at + 1 >= a.Length)
                        return "ERROR from <source>";
                    uint source = at >= 0 ? hex(a[at + 1]) : 0;
                    int end = at >= 0 ? at : a.Length;
                    List<int> arts = new();
                    if (end > 3)
                        foreach (string s in a[3].Split(','))
                            arts.Add((int)hex(s));
                    else
                        arts.AddRange(Features.Reagents());
                    string filled;
                    string? why = Features.Fill(hex(a[1]), int.Parse(a[2]), arts, source, out filled);
                    return (why == null ? "OK" : "STOPPED " + why) + ": " + filled;
                }
            case "put":
                {
                    if (a.Length < 3)
                        return "ERROR put <hex art,...> <container>";
                    List<int> arts = new();
                    foreach (string s in a[1].Split(','))
                        arts.Add((int)hex(s));
                    int moved;
                    string? why = Features.PutInto(arts, hex(a[2]), out moved);
                    return (why == null ? "OK" : "STOPPED " + why) + ": moved " + moved;
                }
            // bank-put <hex art,...>: every pack stack of each art onto the bank's biggest stack of it.
            case "bank-put":
                {
                    if (a.Length < 2)
                        return "ERROR bank-put <hex art,...>";
                    List<int> arts = new();
                    foreach (string s in a[1].Split(','))
                        arts.Add((int)hex(s));
                    string put;
                    string? why = Features.BankPut(arts, out put);
                    return (why == null ? "OK" : "STOPPED " + why) + ": " + put;
                }
            // pack-merge <hex art,...>: each art's stacks in the pack and its bags joined onto the biggest.
            case "pack-merge":
                {
                    if (a.Length < 2)
                        return "ERROR pack-merge <hex art,...>";
                    List<int> arts = new();
                    foreach (string s in a[1].Split(','))
                        arts.Add((int)hex(s));
                    int joined;
                    string? why = Features.PackMerge(arts, out joined);
                    return (why == null ? "OK" : "STOPPED " + why) + ": joined " + joined;
                }
            // sell-list: what the circuit sells and its lowest prices | sell-item <arts hex,...> <lowest gp> <name
            // words>: added or replaced by name, a price under 0 removes it | make-menu <pick|pick...>: the sewing
            // menus' picks.
            case "sell-list":
                return string.Join(" ; ", Features.SellList().ConvertAll(i => i.ToString())) + " || make "
                    + Features.ProductMenu;
            // sell-order [name, name, ... | none]: the homespun bag's tailors in order, by first name (none: the shop
            // order) | sell-rest [restocks]: a tailor sold down rests that many restocks under a sell order.
            case "sell-order":
                if (a.Length > 1)
                    Features.SellOrder = a[1] == "none" ? ImmutableArray<string>.Empty
                        : ImmutableArray.Create(line.Substring(line.IndexOf("sell-order") + 10)
                            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                return Features.SellOrder.Length == 0 ? "sell order: none (the shop order)"
                    : "sell order: " + string.Join(", ", Features.SellOrder) + "; rest " + Features.SellRest
                        + " restocks";
            case "sell-rest":
                if (a.Length > 1)
                    Features.SellRest = int.Parse(a[1]);
                return "sell rest: " + Features.SellRest + " restocks";
            case "sell-item":
                {
                    if (a.Length < 4)
                        return "ERROR sell-item <arts hex,...> <lowest gp> <name words>";
                    string? why = Features.SetSellItem(string.Join(" ", a, 3, a.Length - 3), a[1], int.Parse(a[2]));
                    return why == null ? "OK " + string.Join(" ; ", Features.SellList().ConvertAll(i => i.ToString()))
                        : "ERROR " + why;
                }
            case "make-menu":
                if (a.Length > 1)
                    Features.ProductMenu = line.Substring(line.IndexOf(' ') + 1).Trim();
                return "OK make " + Features.ProductMenu;
            // buy-list: the buy circuit's shopping list and purse | buy-item <arts hex,...> <count> <highest gp> <name
            // words>: added or replaced by name, a count of 0 removes it | buy-purse <gold>.
            case "buy-list":
                return string.Join(" ; ", Features.BuyList().ConvertAll(i => i.ToString())) + " || purse "
                    + Features.BuyPurse;
            case "buy-item":
                {
                    if (a.Length < 5)
                        return "ERROR buy-item <arts hex,...> <count> <highest gp> <name words>";
                    string? why = Features.SetBuyItem(string.Join(" ", a, 4, a.Length - 4), a[1], int.Parse(a[2]),
                        int.Parse(a[3]));
                    return why == null ? "OK " + string.Join(" ; ", Features.BuyList().ConvertAll(i => i.ToString()))
                        : "ERROR " + why;
                }
            case "buy-purse":
                if (a.Length > 1)
                    Features.BuyPurse = int.Parse(a[1]);
                return "OK purse " + Features.BuyPurse;
            // survey-town [town words | here]: the town the survey visits; here (or nothing) is the town the player is
            // in.
            case "survey-town":
                if (a.Length > 1)
                    Features.SurveyTown = a[1] == "here" ? "" : string.Join(" ", a, 1, a.Length - 1);
                return "OK survey " + (Features.SurveyTown!.Length > 0 ? Features.SurveyTown : "here");
            // circuit-shops tailor|buy [all | key;key;...]: a circuit's picked shops by key ("text @x,y"), in visiting
            // order.
            case "circuit-shops":
                {
                    if (a.Length < 2 || (a[1] != "tailor" && a[1] != "buy" && a[1] != "survey" && a[1] != "ingots"))
                        return "ERROR circuit-shops tailor|buy|survey|ingots [all | key;key;...]";
                    if (a[1] == "survey")
                    {
                        if (a.Length > 2)
                        {
                            string rest = line.Substring(line.IndexOf("survey") + 6).Trim();
                            Features.SetCircuitShopKeys("survey",
                                rest == "all" ? new List<string>()
                                : new List<string>(rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)));
                        }
                        return string.Join(" ; ", Features.SurveyShops().ConvertAll(s => s.Key));
                    }
                    if (a.Length > 2)
                    {
                        string rest = line.Substring(line.IndexOf(a[1]) + a[1].Length).Trim();
                        Features.SetCircuitShopKeys(a[1],
                            rest == "all"
                            ? (a[1] == "buy" ? AssistFeatures.AllShops("buy").ConvertAll(s => s.Key)
                            : new List<string>())
                            : new List<string>(rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)));
                    }
                    return string.Join(" ; ", Features.CircuitShops(a[1]).ConvertAll(s => s.Key));
                }
            // broken: the tasks on targets that failed three times | broken clear <key | all>: tried again.
            case "broken":
                return a.Length > 2 && a[1] == "clear"
                    ? Features.BrokenClear(line.Substring(line.IndexOf("clear") + 5).Trim()) : Features.BrokenList();
            // thread-off <name|serial> | thread-on <name|serial>: that tailor taken out of the thread round or put
            // back.
            case "thread-off":
                return a.Length > 1 ? Features.ThreadOffSet(a[1], true) : "ERROR thread-off <name|serial>";
            case "thread-on":
                return a.Length > 1 ? Features.ThreadOffSet(a[1], false) : "ERROR thread-on <name|serial>";
            case "circuit":
                if (a.Length > 1 && a[1] == "go")
                    return done(Features.CircuitGo()!);
                // circuit sell: the shirts sold and the gold banked, then stop; nothing bought but the recall reagents.
                if (a.Length > 1 && a[1] == "sell")
                    return done(Features.CircuitSell()!);
                // circuit tour: every tailor shop once, one shirt to each tailor that buys, all its thread; circuit
                // report.
                if (a.Length > 1 && a[1] == "tour")
                    return done(Features.CircuitTour()!);
                // circuit keep: every thread vendor of the approved shops kept bought out until stopped (Keep thread).
                if (a.Length > 1 && a[1] == "keep")
                    return done(Features.CircuitKeep()!);
                // circuit buy: the shopping list (buy-list) bought at the buy circuit's shops and stored in the bank.
                if (a.Length > 1 && a[1] == "buy")
                    return done(Features.CircuitBuy()!);
                // circuit survey [town words]: every shop of the town (survey-town, else here) visited, each keeper's
                // stock read, nothing bought; circuit report.
                if (a.Length > 1 && a[1] == "survey")
                {
                    if (a.Length > 2)
                        Features.SurveyTown = string.Join(" ", a, 2, a.Length - 2);
                    return done(Features.CircuitSurvey()!);
                }
                // circuit refresh: food bought, eaten until full, the mount fed, the gold banked and the loadout
                // refilled.
                if (a.Length > 1 && a[1] == "refresh")
                    return done(Features.CircuitRefresh()!);
                // circuit mine: shovels tinkered, ore dug through the cave here, smelted, the ingots stored
                // (mine-setup).
                if (a.Length > 1 && a[1] == "mine")
                    return done(Features.CircuitMine()!);
                // circuit lumber: the nearest trees chopped until stopped, the logs put where lumber-setup logs
                // targets.
                if (a.Length > 1 && a[1] == "lumber")
                    return done(Features.CircuitLumber()!);
                // circuit feathers: every bird in range hunted, carved, the feathers and meat put where feathers-setup
                // place targets.
                if (a.Length > 1 && a[1] == "feathers")
                    return done(Features.CircuitFeathers()!);
                // circuit reagents: the reagent piles seen within reagents.range walked to, nearest first, for the
                // pickup.
                if (a.Length > 1 && a[1] == "reagents")
                    return done(Features.CircuitReagents()!);
                // circuit smith: the smithing ladder at the anvil here, sold to the smiths in reach, until 100.0
                // (smith-ingots bank|buy).
                if (a.Length > 1 && a[1] == "smith")
                    return done(Features.CircuitSmith()!);
                // circuit ingots: iron ingots (ingots) bought at the picked smith shops, else the town's, and banked.
                if (a.Length > 1 && a[1] == "ingots")
                    return done(Features.CircuitBuyIngots()!);
                // circuit advert: blank books bought, the ad written in them and put down at each place (advert).
                if (a.Length > 1 && a[1] == "advert")
                    return done(Features.CircuitAdvert()!);
                // circuit spellbook: spellbooks and the scrolls they lack bought, the rest inscribed while the chance
                // holds, the books filled and stocked in the bank (spellbook-setup).
                if (a.Length > 1 && a[1] == "spellbook")
                    return done(Features.CircuitSpellbooks()!);
                // circuit mark: a rune marked at spread spots of every place of the class (mark-setup), in every town.
                if (a.Length > 1 && a[1] == "mark")
                    return done(Features.CircuitMark()!);
                if (a.Length > 1 && a[1] == "report")
                    return Features.TourReport.Replace("\r\n", " | ");
                if (a.Length > 1 && a[1] == "stop")
                {
                    Features.CircuitHalt("stop asked");
                    return "OK";
                }
                return Features.CircuitStatus;
            // walk <x> <y> [reach] | walk-to <name> [reach]: walking until within reach (default 1) of a tile or a
            // mobile.
            case "walk":
                return done(Features.WalkTo(int.Parse(a[1]), int.Parse(a[2]), a.Length > 3 ? int.Parse(a[3]) : 0)!);
            // walk-show 77|20|0: how the client is shown the player's assist steps (GameLink.ShowMode).
            case "flee":
                return done(Features.Flee()!);
            // pace auto|walk|run: auto runs a rested mount and walks a tired one (AssistFeatures.Pace).
            case "pace":
                if (a.Length > 1 && (a[1] == "auto" || a[1] == "walk" || a[1] == "run"))
                    Features.Pace = a[1];
                return "OK pace " + Features.Pace;
            case "walk-show":
                k.ShowMode = Convert.ToInt32(a[1], 16);
                return "OK show " + k.ShowMode.ToString("X");
            case "walk-to":
                {
                    if (Features.Atlas == null)
                        return "ERROR no atlas";
                    AtlasNpc? who = null;
                    foreach (AtlasNpc n in Features.Atlas.Visible())
                        if (string.Equals(n.Name, a[1], StringComparison.OrdinalIgnoreCase))
                            who = n;
                    if (who == null)
                        return "ERROR no one named " + a[1] + " in sight";
                    return done(Features.WalkTo(who.X, who.Y, a.Length > 2 ? int.Parse(a[2]) : 1)!);
                }
            case "action":
                return AssistWindow.Current == null ? "ERROR no window"
                    : done(AssistWindow.Current.RunAction(a.Length > 1 ? a[1] : "")!);
            case "loops":
                return Features.Menu();
            case "loop":
                {
                    // loop <name words> [rounds N] [until-skill V] [until-line text]
                    List<string> name = new();
                    int rounds = 0, until = 0;
                    string? untilLine = null;
                    int i = 1;
                    for (; i < a.Length && a[i] != "rounds" && a[i] != "until-skill" && a[i] != "until-line"; i++)
                        name.Add(a[i]);
                    for (; i < a.Length; i++)
                    {
                        if (a[i] == "rounds")
                        {
                            rounds = int.Parse(a[++i]);
                        }
                        else if (a[i] == "until-skill")
                        {
                            until = (int)(double.Parse(a[++i], CultureInfo.InvariantCulture) * 10);
                        }
                        else if (a[i] == "until-line")
                        {
                            untilLine = string.Join(" ", a, i + 1, a.Length - i - 1);
                            break;
                        }
                    }
                    LoopKind? kind = AssistFeatures.Find(string.Join(" ", name));
                    if (kind == null)
                        return "ERROR no loop named " + string.Join(" ", name);
                    return done(Features.Start(kind, rounds, until, untilLine!)!);
                }
            case "stop":
                Features.Stop("stop asked");
                return "OK";
            // timing [key [ms]]: every timing, one, or one set as the Timings panel sets it (TimingCommand).
            case "timing":
                return TimingCommand.Run(Features.Timings, a);
            // suggest [on|off|do|skip]: the circuit's next step waits for an answer (AssistFeatures.Suggest).
            case "suggest":
                if (a.Length > 1 && (a[1] == "on" || a[1] == "off"))
                    Features.Suggest = a[1] == "on";
                else if (a.Length > 1 && (a[1] == "do" || a[1] == "skip"))
                    return done(Features.SuggestAnswer(a[1] == "do")!);
                return "OK suggest " + (Features.Suggest ? "on" : "off")
                    + (Features.Suggested.Length > 0 ? "; waiting: " + Features.Suggested : "");
            case "macro":
                return Features.Status + " || " + Features.LoopWords + " || " + Features.Sequence;
            case "arm":
                return done(Features.ArmToggle(a.Length > 1 ? a[1] : "both")!);
            case "pickup":
                if (a.Length > 1)
                    Features.Pickup = a[1] == "on";
                return "pickup " + (Features.Pickup ? "on" : "off") + " reach " + Features.Reach + ", "
                    + Features.PickupPaceText;
            case "reagents":
                {
                    List<int> g = new(Features.Reagents());
                    if (a.Length > 2 && a[1] == "add")
                        g.Add((int)hex(a[2]));
                    else if (a.Length > 2 && a[1] == "remove")
                        g.Remove((int)hex(a[2]));
                    Features.SetReagents(g.ToArray());
                    List<string> s = new();
                    foreach (int x in g)
                        s.Add(x.ToString("X"));
                    return string.Join(",", s);
                }
            case "counts":
                return Features.Counts();
            case "advice":
                return Features.AdviceNow();
            case "notes":
                lock (Features.Notes)
                    return string.Join(" | ",
                        Features.Notes.GetRange(Math.Max(0, Features.Notes.Count - 5),
                        Math.Min(5, Features.Notes.Count)));
            case "display":
                if (a.Length > 2)
                    Features.Show(a[1], a[2] == "on");
                {
                    List<string> s = new();
                    foreach (string n in AssistFeatures.DisplayNames)
                        s.Add(n + (Features.Shown(n) ? " on" : " off"));
                    return string.Join(", ", s);
                }
        }
        return "ERROR unknown command " + a[0];
    }
}
