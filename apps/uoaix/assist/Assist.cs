using System; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading;
using System.Collections.Generic; using System.Collections.Concurrent;

// One file per relay session, rotated at 64 MB, the last 4 kept (assist-plan.md rule 8).
public sealed class AssistLog {
    const long Limit = 64L << 20; const int Keep = 4;
    // The stem names the relay's port: two relays (the shard's and uo1998's) started in the same second wrote one file.
    readonly object gate = new object(); readonly string dir, stem, pattern; StreamWriter w; long written; int part;
    public string Path { get; private set; }
    public AssistLog(string dir, int port) {
        this.dir = dir; Directory.CreateDirectory(dir);
        pattern = "assist-" + port + "-*.log"; stem = "assist-" + port + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Open();
    }
    void Open() {
        if (w != null) w.Dispose();
        Path = System.IO.Path.Combine(dir, stem + (part == 0 ? "" : "-" + part) + ".log"); part++;
        w = new StreamWriter(Path, false, new UTF8Encoding(false)); written = 0;
        var all = new DirectoryInfo(dir).GetFiles(pattern); Array.Sort(all, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int i = Keep; i < all.Length; i++) if (all[i].FullName != Path) try { all[i].Delete(); } catch { }
    }
    // The last TailKeep lines, for the window's Log tab; Lined fires for each line after it is written (the fail-safe
    // watches it). The file is flushed every line: a size of 0 in a directory listing is Windows reporting a file another
    // process holds open, not unwritten lines.
    public const int TailKeep = 400;
    readonly List<string> tail = new List<string>(); long tailSeq;
    public event Action<string> Lined;
    public void Line(int id, string dir, string text) {
        string s = DateTime.UtcNow.ToString("HH:mm:ss.fff") + " c" + id + " " + dir + " " + text;
        lock (gate) {
            if (written + s.Length + 2 > Limit) Open();
            w.WriteLine(s); w.Flush(); written += s.Length + 2;
            tail.Add(s.Length > 300 ? s.Substring(0, 300) : s); tailSeq++; if (tail.Count > TailKeep) tail.RemoveRange(0, tail.Count - TailKeep);
        }
        var h = Lined; if (h != null) try { h(s); } catch { }
    }
    public List<string> TailSince(ref long seq) {
        lock (gate) { long first = tailSeq - tail.Count; int from = (int)Math.Max(0, seq - first); seq = tailSeq; return tail.GetRange(from, tail.Count - from); }
    }
}

public sealed class AssistRelay {
    sealed class Ticket { public string Host; public int Port; public DateTime At; }
    readonly int listen; readonly string loginHost; readonly int loginPort; readonly string gameHost; readonly int gamePort; readonly int[] lengths; readonly UoHuffman huff;
    readonly AssistLog log; readonly ConcurrentDictionary<uint, Ticket> tickets = new ConcurrentDictionary<uint, Ticket>(); int next;
    // gameHost null follows each 0x8C. A shard copy on a forwarded port names its guest-side port in 0x8C (2593, the
    // live shard's), so its game server is given here instead.
    public AssistRelay(int listen, string loginHost, int loginPort, string gameHost, int gamePort, int[] lengths, int[] book, string logDir) {
        this.listen = listen; this.loginHost = loginHost; this.loginPort = loginPort; this.gameHost = string.IsNullOrEmpty(gameHost) ? null : gameHost; this.gamePort = gamePort; this.lengths = lengths;
        huff = new UoHuffman(book); log = new AssistLog(logDir, listen);
        Features = new AssistFeatures(log, new AssistSettings(settingsPath)); Features.Server = loginHost + ":" + loginPort + ".";
        AssistGuard.Current = new AssistGuard(Features.Cfg, log);
    }
    public AssistLog Log { get { return log; } }
    public readonly AssistFeatures Features;
    public static string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uoaix-assist", "settings.txt");
    public string LogPath { get { return log.Path; } }
    public int ListenPort { get { return listen; } }
    // The client's end of its newest connection: the window finds the client's process (and its window) by it.
    public volatile int LastClientPort;

    readonly Dictionary<int, GameLink> links = new Dictionary<int, GameLink>();
    public const int StateSeconds = 30;

    // The newest game connection still open, or null.
    public GameLink Link() { lock (links) { GameLink best = null; foreach (var kv in links) if (!kv.Value.Closed && (best == null || kv.Key > best.Id)) best = kv.Value; return best; } }

    public void Run() {
        var listener = new TcpListener(IPAddress.Loopback, listen); listener.Start();
        log.Line(0, "--", "LISTEN 127.0.0.1:" + listen + " login " + loginHost + ":" + loginPort + " control 127.0.0.1:" + (listen + 1));
        var census = new Thread(() => {
            while (true) {
                Thread.Sleep(StateSeconds * 1000);
                lock (links) foreach (var kv in links) log.Line(kv.Key, "--", "STATE " + kv.Value.State.Summary());
            }
        }); census.IsBackground = true; census.Start();
        var control = new Thread(Control); control.IsBackground = true; control.Start();
        while (true) {
            var client = listener.AcceptTcpClient(); int id = Interlocked.Increment(ref next);
            var t = new Thread(() => Serve(id, client)); t.IsBackground = true; t.Start();
        }
    }

    static bool ReadExact(NetworkStream s, byte[] b, int n) {
        for (int at = 0; at < n; ) { int r = s.Read(b, at, n - at); if (r <= 0) return false; at += r; }
        return true;
    }

    // The first 4 client bytes are the keystream seed; on a game connection they are the key the server's
    // 0x8C named, and the remembered ticket for that key says where the game server is.
    void Serve(int id, TcpClient client) {
        TcpClient server = null;
        try {
            client.NoDelay = true; var cs = client.GetStream(); var seed = new byte[4];
            LastClientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;
            if (!ReadExact(cs, seed, 4)) { log.Line(id, "--", "CLOSE before seed"); return; }
            uint key = ((uint)seed[0] << 24) | ((uint)seed[1] << 16) | ((uint)seed[2] << 8) | seed[3];
            Ticket t; bool game = tickets.TryRemove(key, out t);
            string host = game ? t.Host : loginHost; int port = game ? t.Port : loginPort;
            log.Line(id, "--", "OPEN " + (game ? "game" : "login") + " seed " + key.ToString("X8") + " -> " + host + ":" + port);
            server = new TcpClient(host, port); server.NoDelay = true; var ss = server.GetStream();
            ss.Write(seed, 0, 4);
            if (game) { RunGame(id, cs, ss, key); return; }
            var up = new Thread(() => PumpUp(id, cs, ss, key)); up.IsBackground = true; up.Start();
            PumpLoginDown(id, ss, cs);
            up.Join(2000);
        } catch (Exception e) { log.Line(id, "--", "ERROR " + e.Message); }
        finally { try { client.Close(); } catch { } try { if (server != null) server.Close(); } catch { } log.Line(id, "--", "CLOSE"); }
    }

    // Client bytes go upstream unchanged; the log shows them decrypted, the password masked (rule 7:
    // 0x80 bytes 31-60, 0x91 bytes 35-64 of the connection's first packet).
    void PumpUp(int id, NetworkStream from, NetworkStream to, uint seed) {
        var buf = new byte[8192]; var ks = new UoKeystream(seed); long pos = 0; int op = -1;
        try {
            while (true) {
                int n = from.Read(buf, 0, buf.Length); if (n <= 0) break;
                to.Write(buf, 0, n);
                var sb = new StringBuilder(n * 2);
                for (int i = 0; i < n; i++) {
                    byte p = ks.Apply(buf[i]); if (op < 0) op = p; long at = pos++;
                    if ((op == 0x80 && at >= 31 && at <= 60) || (op == 0x91 && at >= 35 && at <= 64)) sb.Append("**"); else sb.Append(p.ToString("X2"));
                }
                log.Line(id, "C>S", sb.ToString());
            }
        } catch (Exception e) { log.Line(id, "--", "C>S END " + e.Message); }
        try { to.Close(); } catch { }
    }

    // Login replies are plain. Each whole packet goes on to the client; a 0x8C is rewritten to name this
    // relay, and the real game server is remembered under its key.
    void PumpLoginDown(int id, NetworkStream from, NetworkStream to) {
        var buf = new byte[8192]; var framer = new UoFramer(lengths); bool raw = false;
        try {
            while (true) {
                int n = from.Read(buf, 0, buf.Length); if (n <= 0) break;
                if (raw) { to.Write(buf, 0, n); log.Line(id, "S>C", "raw " + UoHex.Of(buf, 0, n)); continue; }
                framer.Feed(buf, 0, n, p => {
                    if (p[0] == 0x8C) Redirect(id, p);
                    log.Line(id, "S>C", UoHex.Of(p));
                    to.Write(p, 0, p.Length);
                });
                if (framer.Desync != null) {
                    raw = true; var rest = framer.TakeBuffered(); to.Write(rest, 0, rest.Length);
                    log.Line(id, "S>C", "DESYNC login " + framer.Desync + "; no 0x8C after this is redirected: raw " + UoHex.Of(rest));
                }
            }
        } catch (Exception e) { log.Line(id, "--", "S>C END " + e.Message); }
        try { to.Close(); } catch { }
    }

    void Redirect(int id, byte[] p) {
        string host = p[1] + "." + p[2] + "." + p[3] + "." + p[4]; int port = (p[5] << 8) | p[6];
        uint key = ((uint)p[7] << 24) | ((uint)p[8] << 16) | ((uint)p[9] << 8) | p[10];
        var now = DateTime.UtcNow;
        foreach (var kv in tickets) if ((now - kv.Value.At).TotalMinutes > 10) { Ticket gone; tickets.TryRemove(kv.Key, out gone); }
        string to = gameHost ?? host; int toPort = gameHost != null ? gamePort : port;
        tickets[key] = new Ticket { Host = to, Port = toPort, At = now };
        p[1] = 127; p[2] = 0; p[3] = 0; p[4] = 1; p[5] = (byte)(listen >> 8); p[6] = (byte)listen;
        log.Line(id, "--", "REDIRECT key " + key.ToString("X8") + " " + host + ":" + port + (gameHost != null ? " (game server given: " + to + ":" + toPort + ")" : "") + " -> 127.0.0.1:" + listen);
    }

    void RunGame(int id, NetworkStream cs, NetworkStream ss, uint key) {
        var link = new GameLink(id, cs, ss, key, lengths, huff, log); lock (links) links[id] = link;
        Features.Attach(link);
        try { link.Run(); }
        finally { lock (links) links.Remove(id); log.Line(id, "--", "STATE " + link.State.Summary()); }
    }

    // Loopback control port (listen + 1), one command a line, one answer line each: the headless way to drive what the
    // window drives. state | journal [n] | items | use <serial> | target <x> <y> <z> [graphic] | target-object <serial> | tell <text>
    void Control() {
        var l = new TcpListener(IPAddress.Loopback, listen + 1); l.Start();
        while (true) {
            var c = l.AcceptTcpClient();
            var t = new Thread(() => {
                try {
                    using (c) {
                        var s = c.GetStream(); var r = new StreamReader(s, Encoding.ASCII); var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true };
                        string line; while ((line = r.ReadLine()) != null) { string answer; try { answer = Command(line.Trim()); } catch (Exception e) { answer = "ERROR " + e.Message; } w.WriteLine(answer); }
                    }
                } catch { }
            }); t.IsBackground = true; t.Start();
        }
    }

    public string Command(string line) {
        var a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (a.Length == 0) return "ERROR empty";
        if (a[0] == "window") return AssistWindow.Current == null ? "no window" : a.Length > 2 && a[1] == "snapshot" ? AssistWindow.Current.Snapshot(line.Substring(line.IndexOf("snapshot") + 9)) : AssistWindow.Current.Report();
        var k = Link(); if (k == null) return "ERROR no game connection";
        Func<string, string> done = why => why == null ? "OK" : "REFUSED " + why;
        Func<string, uint> hex = v => Convert.ToUInt32(v.StartsWith("0x") ? v.Substring(2) : v, 16);
        switch (a[0]) {
            case "state": return k.State.Summary() + (k.Halted != null ? " HALTED " + k.Halted : "") + (k.InFlight != null ? " in-flight " + k.InFlight : "") + (k.MacroCursor ? " assist-cursor" : "");
            case "journal": { int n = a.Length > 1 ? int.Parse(a[1]) : 5; lock (k.State.Gate) { var j = k.State.Journal; return string.Join(" | ", j.GetRange(Math.Max(0, j.Count - n), Math.Min(n, j.Count))); } }
            case "items": lock (k.State.Gate) { var sb = new StringBuilder(); foreach (var kv in k.State.Items) sb.Append(kv.Key.ToString("X") + ":" + kv.Value.Graphic.ToString("X4") + "x" + kv.Value.Amount + (kv.Value.Layer >= 0 ? "@L" + kv.Value.Layer : "@" + kv.Value.Container.ToString("X")) + " "); return sb.ToString().TrimEnd(); }
            case "use": return done(k.Use(hex(a[1])));
            case "target": return done(k.Target(true, 0, int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]), a.Length > 4 ? (int)hex(a[4]) : 0));
            case "target-object": {
                uint s = hex(a[1]); AssistItem it; lock (k.State.Gate) { if (!k.State.Items.TryGetValue(s, out it)) k.State.Ground.TryGetValue(s, out it); }
                return done(k.Target(false, s, it != null ? it.X : 0, it != null ? it.Y : 0, it != null ? it.Z : 0, it != null ? it.Graphic : 0));
            }
            case "tell": return done(k.Tell(line.Substring(line.IndexOf(' ') + 1)));
            // The tailoring circuit's steps (assist-plan.md stage 6): say <text> | vendor-buy <hex art,...> <most> [name] |
            // vendor-sell <hex art,...> <floor gp> [name] | recall <rune serial>. A name asks that vendor, not every one.
            // A line said here counts as answered when it opens a window (a "bank" that opened the box), so the circuit's own
            // "bank" after it is not refused as an unanswered repeat (2026-10-17, Vesper and Nujel'm).
            case "say": return done(Features.SayHeard(line.Substring(line.IndexOf(' ') + 1)));
            case "vendor-buy": case "vendor-sell": {
                if (a.Length < 3) return "ERROR " + a[0] + " <hex art,...> <" + (a[0] == "vendor-buy" ? "most" : "floor gp") + ">";
                var arts = new HashSet<int>(); foreach (var s in a[1].Split(',')) arts.Add((int)hex(s));
                int n = int.Parse(a[2]); string who = a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "vendor";
                if (a[0] == "vendor-buy") { int bought, left; string why = Features.VendorBuy(g => arts.Contains(g), n, out bought, out left, who); return why == null ? "OK bought " + bought + ", " + left + " left in stock" : "REFUSED " + why; }
                { int sold, gold, under; string why = Features.VendorSell(g => arts.Contains(g), n, out sold, out gold, out under, who); return why == null ? "OK sold " + sold + " for " + gold + (under > 0 ? ", offered " + under + " for others" : "") : "REFUSED " + why; }
            }
            case "recall": return done(Features.Recall(hex(a[1])));
            // runes [reach]: the runes on the ground within reach (default 12) and their labels, nearest first.
            case "runes": return string.Join(" | ", Features.RuneLabels(a.Length > 1 ? int.Parse(a[1]) : 12));
            case "pack-runes": return string.Join(" | ", Features.PackRuneLabels(k));
            // mobiles [reach]: the mobiles in view (AssistState.Mobiles) within reach (default 18), nearest first.
            case "mobiles": {
                int reach = a.Length > 1 ? int.Parse(a[1]) : 18; var rows = new List<string>();
                lock (k.State.Gate) { int px = k.State.X, py = k.State.Y; foreach (var kv in k.State.Mobiles) { int d = Math.Max(Math.Abs(kv.Value[0] - px), Math.Abs(kv.Value[1] - py)); if (d <= reach) { string n; int b; k.State.Names.TryGetValue(kv.Key, out n); k.State.Bodies.TryGetValue(kv.Key, out b); rows.Add(d.ToString("D2") + " " + kv.Key.ToString("X") + " " + (n ?? "?") + " body " + b.ToString("X") + " at " + kv.Value[0] + "," + kv.Value[1]); } } }
                rows.Sort(); return string.Join(" | ", rows);
            }
            case "peek": return done(Features.Peek(hex(a[1])));
            // rename <rune> <text>: the rune's description, answered to the prompt its double-click opens.
            case "rename": if (a.Length < 3) return "ERROR rename <rune> <text>"; return done(Features.Rename(hex(a[1]), line.Substring(line.IndexOf(a[1]) + a[1].Length + 1).Trim()));
            // ground [reach]: the ground items within reach (default 12), nearest first, named by the client's tile data.
            case "ground": {
                int reach = a.Length > 1 ? int.Parse(a[1]) : 12; var list = new List<AssistItem>(); int px, py;
                lock (k.State.Gate) { px = k.State.X; py = k.State.Y; foreach (var g in k.State.Ground.Values) if (Math.Max(Math.Abs(g.X - px), Math.Abs(g.Y - py)) <= reach) list.Add(g); }
                list.Sort((x, y) => Math.Max(Math.Abs(x.X - px), Math.Abs(x.Y - py)).CompareTo(Math.Max(Math.Abs(y.X - px), Math.Abs(y.Y - py))));
                return string.Join(" | ", list.ConvertAll(g => g.Serial.ToString("X") + " " + UoTiles.Name(g.Graphic) + " " + g.Graphic.ToString("X4") + (g.Amount > 1 ? " x" + g.Amount : "") + " at " + g.X + "," + g.Y + "," + g.Z));
            }
            case "work": Features.WorkToggle(); return "OK " + Features.WorkState;
            // sew: fancy shirts from all the pack's cloth | circuit go|stop|status.
            case "sew": { int made, failed; string why = Features.SewAll(out made, out failed); return (why == null ? "OK" : "STOPPED " + why) + ": " + made + " made, " + failed + " failed"; }
            // bank-deposit [keep]: the pack's gold over keep (default 300) onto the bank's piles.
            case "bank-deposit": { int put; string why = Features.BankDeposit(a.Length > 1 ? int.Parse(a[1]) : AssistFeatures.PurseKeep, out put); return (why == null ? "OK" : "STOPPED " + why) + ": put " + put; }
            // bank-take <hex art,...> <count>: from the bank box into the pack until the pack holds count of each.
            case "bank-take": {
                if (a.Length < 3) return "ERROR bank-take <hex art,...> <count>";
                var arts = new List<int>(); foreach (var s in a[1].Split(',')) arts.Add((int)hex(s));
                string taken; string why = Features.BankTake(arts, int.Parse(a[2]), out taken); return (why == null ? "OK" : "STOPPED " + why) + ": " + taken;
            }
            // contents <container>: its items, serial art xamount at x,y in the container. put <hex art,...> <container>:
            // every pack stack of each art into that container.
            case "contents": {
                uint box = hex(a[1]); var rows = new List<string>();
                lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == box) rows.Add(it.Serial.ToString("X") + " " + UoTiles.Name(it.Graphic) + " " + it.Graphic.ToString("X4") + " x" + it.Amount + " at " + it.X + "," + it.Y);
                return string.Join(" | ", rows);
            }
            // labels <container>: each item in it single-clicked where it lies, and its label.
            case "labels": return string.Join(" | ", Features.ContainerLabels(hex(a[1])));
            // replies: the NPC answers no known kind matched, newest last, for the one driving to judge.
            case "replies": lock (Features.UnknownReplies) return Features.UnknownReplies.Count == 0 ? "none" : string.Join(" | ", Features.UnknownReplies);
            // travel <city>: from the nearest bank, by the bank's rune for the city.
            case "travel": return done(Features.BankRecall(k, line.Substring(line.IndexOf(' ') + 1).Trim()));
            // names [reach]: each mobile in view within reach (default 12) single-clicked, and its name.
            case "names": return string.Join(" | ", Features.MobileLabels(a.Length > 1 ? int.Parse(a[1]) : 12));
            // move <serial,...> <container>: exactly these items into the container, each lifted whole and dropped in it.
            case "move": {
                if (a.Length < 3) return "ERROR move <serial,...> <container>";
                var serials = new List<uint>(); foreach (var s in a[1].Split(',')) serials.Add(hex(s));
                string why = Features.MoveInto(serials, hex(a[2])); return why == null ? "OK moved " + serials.Count : "STOPPED " + why;
            }
            // fill <container> <count> [hex art,...] [from <source>]: each art (default the reagents) in the container brought
            // to count, from and to the source's loose stacks (default the pack).
            // mine-setup [forge | dig | storage vendor|bank|floor | store-rune <label> | cave-rune <label> | shovels <n> |
            // store-at <n> | tinker <pick|pick>]: the mining circuit's setup; nothing: the setup as it stands.
            case "mine-setup": return Features.MineSetup(a);
            // smith-ladder [mine | guess | item@skill;item@skill...]: the smithing gain ladder, which one is used, or the
            // player's own steps; nothing: both, and what each picks now.
            case "smith-ladder": {
                string rest = line.Length > 13 ? line.Substring(13).Trim() : "";
                if (rest == "mine" || rest == "guess") Features.SmithLadderMode = rest; else if (rest.Length > 0) Features.SmithLadder = rest;
                int sk; lock (k.State.Gate) k.State.Skills.TryGetValue(AssistFeatures.SmithySkill + 1, out sk);
                return "mode " + Features.SmithLadderMode + " ; ladder " + Features.SmithLadder + " ; at " + (sk / 10.0).ToString("0.0") + ": " + (Features.SmithNow(sk) ?? "none");
            }
            // record: a recording started or stopped (Features.RecordToggle); record status: whether one runs.
            case "record": return a.Length > 1 && a[1] == "status" ? (Features.Recording ? "recording" : "not recording") : Features.RecordToggle("the control port");
            case "fill": {
                if (a.Length < 3) return "ERROR fill <container> <count> [hex art,...] [from <source>]";
                int at = Array.IndexOf(a, "from"); if (at >= 0 && at + 1 >= a.Length) return "ERROR from <source>";
                uint source = at >= 0 ? hex(a[at + 1]) : 0; int end = at >= 0 ? at : a.Length;
                var arts = new List<int>(); if (end > 3) foreach (var s in a[3].Split(',')) arts.Add((int)hex(s)); else arts.AddRange(Features.Reagents());
                string filled; string why = Features.Fill(hex(a[1]), int.Parse(a[2]), arts, source, out filled); return (why == null ? "OK" : "STOPPED " + why) + ": " + filled;
            }
            case "put": {
                if (a.Length < 3) return "ERROR put <hex art,...> <container>";
                var arts = new List<int>(); foreach (var s in a[1].Split(',')) arts.Add((int)hex(s));
                int moved; string why = Features.PutInto(arts, hex(a[2]), out moved); return (why == null ? "OK" : "STOPPED " + why) + ": moved " + moved;
            }
            // bank-put <hex art,...>: every pack stack of each art onto the bank's biggest stack of it.
            case "bank-put": {
                if (a.Length < 2) return "ERROR bank-put <hex art,...>";
                var arts = new List<int>(); foreach (var s in a[1].Split(',')) arts.Add((int)hex(s));
                string put; string why = Features.BankPut(arts, out put); return (why == null ? "OK" : "STOPPED " + why) + ": " + put;
            }
            // pack-merge <hex art,...>: each art's stacks in the pack and its bags joined onto the biggest.
            case "pack-merge": {
                if (a.Length < 2) return "ERROR pack-merge <hex art,...>";
                var arts = new List<int>(); foreach (var s in a[1].Split(',')) arts.Add((int)hex(s));
                int joined; string why = Features.PackMerge(arts, out joined); return (why == null ? "OK" : "STOPPED " + why) + ": joined " + joined;
            }
            // sell-list: what the circuit sells and its lowest prices | sell-item <arts hex,...> <lowest gp> <name words>: added
            // or replaced by name, a price under 0 removes it | make-menu <pick|pick...>: the sewing menus' picks.
            case "sell-list": return string.Join(" ; ", Features.SellList().ConvertAll(i => i.ToString())) + " || make " + Features.ProductMenu;
            case "sell-item": {
                if (a.Length < 4) return "ERROR sell-item <arts hex,...> <lowest gp> <name words>";
                string why = Features.SetSellItem(string.Join(" ", a, 3, a.Length - 3), a[1], int.Parse(a[2]));
                return why == null ? "OK " + string.Join(" ; ", Features.SellList().ConvertAll(i => i.ToString())) : "ERROR " + why;
            }
            case "make-menu": if (a.Length > 1) Features.ProductMenu = line.Substring(line.IndexOf(' ') + 1).Trim(); return "OK make " + Features.ProductMenu;
            // buy-list: the buy circuit's shopping list and purse | buy-item <arts hex,...> <count> <highest gp> <name words>:
            // added or replaced by name, a count of 0 removes it | buy-purse <gold>.
            case "buy-list": return string.Join(" ; ", Features.BuyList().ConvertAll(i => i.ToString())) + " || purse " + Features.BuyPurse;
            case "buy-item": {
                if (a.Length < 5) return "ERROR buy-item <arts hex,...> <count> <highest gp> <name words>";
                string why = Features.SetBuyItem(string.Join(" ", a, 4, a.Length - 4), a[1], int.Parse(a[2]), int.Parse(a[3]));
                return why == null ? "OK " + string.Join(" ; ", Features.BuyList().ConvertAll(i => i.ToString())) : "ERROR " + why;
            }
            case "buy-purse": if (a.Length > 1) Features.BuyPurse = int.Parse(a[1]); return "OK purse " + Features.BuyPurse;
            // survey-town [town words | here]: the town the survey visits; here (or nothing) is the town the player is in.
            case "survey-town": if (a.Length > 1) Features.SurveyTown = a[1] == "here" ? "" : string.Join(" ", a, 1, a.Length - 1); return "OK survey " + (Features.SurveyTown.Length > 0 ? Features.SurveyTown : "here");
            // circuit-shops tailor|buy [all | key;key;...]: a circuit's picked shops by key ("text @x,y"), in visiting order.
            case "circuit-shops": {
                if (a.Length < 2 || (a[1] != "tailor" && a[1] != "buy" && a[1] != "survey")) return "ERROR circuit-shops tailor|buy|survey [all | key;key;...]";
                if (a[1] == "survey") { if (a.Length > 2) { string rest = line.Substring(line.IndexOf("survey") + 6).Trim(); Features.SetCircuitShopKeys("survey", rest == "all" ? new List<string>() : new List<string>(rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))); } return string.Join(" ; ", Features.SurveyShops().ConvertAll(s => s.Key)); }
                if (a.Length > 2) { string rest = line.Substring(line.IndexOf(a[1]) + a[1].Length).Trim(); Features.SetCircuitShopKeys(a[1], rest == "all" ? (a[1] == "buy" ? AssistFeatures.AllShops("buy").ConvertAll(s => s.Key) : new List<string>()) : new List<string>(rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))); }
                return string.Join(" ; ", Features.CircuitShops(a[1]).ConvertAll(s => s.Key));
            }
            case "circuit":
                if (a.Length > 1 && a[1] == "go") return done(Features.CircuitGo());
                // circuit sell: the shirts sold and the gold banked, then stop; nothing bought but the recall reagents.
                if (a.Length > 1 && a[1] == "sell") return done(Features.CircuitSell());
                // circuit tour: every tailor shop once, one shirt to each tailor that buys, all its thread; circuit report.
                if (a.Length > 1 && a[1] == "tour") return done(Features.CircuitTour());
                // circuit buy: the shopping list (buy-list) bought at the buy circuit's shops and stored in the bank.
                if (a.Length > 1 && a[1] == "buy") return done(Features.CircuitBuy());
                // circuit survey [town words]: every shop of the town (survey-town, else here) visited, each keeper's stock
                // read, nothing bought; circuit report.
                if (a.Length > 1 && a[1] == "survey") { if (a.Length > 2) Features.SurveyTown = string.Join(" ", a, 2, a.Length - 2); return done(Features.CircuitSurvey()); }
                // circuit refresh: food bought, eaten until full, the mount fed, the gold banked and the loadout refilled.
                if (a.Length > 1 && a[1] == "refresh") return done(Features.CircuitRefresh());
                // circuit mine: shovels tinkered, ore dug through the cave here, smelted, the ingots stored (mine-setup).
                if (a.Length > 1 && a[1] == "mine") return done(Features.CircuitMine());
                if (a.Length > 1 && a[1] == "report") return Features.TourReport.Replace("\r\n", " | ");
                if (a.Length > 1 && a[1] == "stop") { Features.CircuitHalt("stop asked"); return "OK"; }
                return Features.CircuitStatus;
            // walk <x> <y> [reach] | walk-to <name> [reach]: walking until within reach (default 1) of a tile or a mobile.
            case "walk": return done(Features.WalkTo(int.Parse(a[1]), int.Parse(a[2]), a.Length > 3 ? int.Parse(a[3]) : 0));
            // walk-show 77|20|0: how the client is shown the player's assist steps (GameLink.ShowMode).
            case "flee": return done(Features.Flee());
            // pace auto|walk|run: auto runs a rested mount and walks a tired one (AssistFeatures.Pace).
            case "pace": if (a.Length > 1 && (a[1] == "auto" || a[1] == "walk" || a[1] == "run")) Features.Pace = a[1]; return "OK pace " + Features.Pace;
            case "walk-show": k.ShowMode = Convert.ToInt32(a[1], 16); return "OK show " + k.ShowMode.ToString("X");
            case "walk-to": {
                if (Features.Atlas == null) return "ERROR no atlas";
                AtlasNpc who = null; foreach (var n in Features.Atlas.Visible()) if (string.Equals(n.Name, a[1], StringComparison.OrdinalIgnoreCase)) who = n;
                if (who == null) return "ERROR no one named " + a[1] + " in sight";
                return done(Features.WalkTo(who.X, who.Y, a.Length > 2 ? int.Parse(a[2]) : 1));
            }
            case "action": return AssistWindow.Current == null ? "ERROR no window" : done(AssistWindow.Current.RunAction(a.Length > 1 ? a[1] : ""));
            case "loops": return Features.Menu();
            case "loop": {
                // loop <name words> [rounds N] [until-skill V] [until-line text]
                var name = new List<string>(); int rounds = 0, until = 0; string untilLine = null; int i = 1;
                for (; i < a.Length && a[i] != "rounds" && a[i] != "until-skill" && a[i] != "until-line"; i++) name.Add(a[i]);
                for (; i < a.Length; i++) {
                    if (a[i] == "rounds") rounds = int.Parse(a[++i]);
                    else if (a[i] == "until-skill") until = (int)(double.Parse(a[++i], System.Globalization.CultureInfo.InvariantCulture) * 10);
                    else if (a[i] == "until-line") { untilLine = string.Join(" ", a, i + 1, a.Length - i - 1); break; }
                }
                var kind = AssistFeatures.Find(string.Join(" ", name)); if (kind == null) return "ERROR no loop named " + string.Join(" ", name);
                return done(Features.Start(kind, rounds, until, untilLine));
            }
            case "stop": Features.Stop("stop asked"); return "OK";
            case "macro": return Features.Status + " || " + Features.LoopWords + " || " + Features.Sequence;
            case "arm": return done(Features.ArmToggle(a.Length > 1 ? a[1] : "both"));
            case "pickup": if (a.Length > 1) Features.Pickup = a[1] == "on"; return "pickup " + (Features.Pickup ? "on" : "off") + " reach " + Features.Reach + ", " + Features.PickupPaceText;
            case "reagents": {
                var g = new List<int>(Features.Reagents());
                if (a.Length > 2 && a[1] == "add") g.Add((int)hex(a[2])); else if (a.Length > 2 && a[1] == "remove") g.Remove((int)hex(a[2]));
                Features.SetReagents(g.ToArray()); var s = new List<string>(); foreach (var x in g) s.Add(x.ToString("X")); return string.Join(",", s);
            }
            case "counts": return Features.Counts();
            case "advice": return Features.AdviceNow();
            case "notes": lock (Features.Notes) return string.Join(" | ", Features.Notes.GetRange(Math.Max(0, Features.Notes.Count - 5), Math.Min(5, Features.Notes.Count)));
            case "display":
                if (a.Length > 2) Features.Show(a[1], a[2] == "on");
                { var s = new List<string>(); foreach (var n in AssistFeatures.DisplayNames) s.Add(n + (Features.Shown(n) ? " on" : " off")); return string.Join(", ", s); }
        }
        return "ERROR unknown command " + a[0];
    }
}
