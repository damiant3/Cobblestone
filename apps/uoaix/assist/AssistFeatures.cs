// Stage 4 of assist-plan.md: canned loops, arm and disarm, drinking without hotkeys, reagent autopickup, resource
// counts, skills and stats advice, display options. Every action goes through GameLink, which enforces rules 1 to 4;
// one feature acts at a time (actionGate), so two never have actions in flight together (rule 3).
// No using directives: assist.ps1 compiles this with Assist.cs as one unit.

// key=value lines in %LOCALAPPDATA%\uoaix-assist\settings.txt: delays the server corrected, round counts, the arm set,
// the reagent list, advice corrections, display choices.
public sealed class AssistSettings {
    readonly string path; readonly System.Collections.Generic.Dictionary<string, string> kv = new System.Collections.Generic.Dictionary<string, string>();
    public AssistSettings(string path) {
        this.path = path;
        if (System.IO.File.Exists(path)) foreach (var line in System.IO.File.ReadAllLines(path)) { int eq = line.IndexOf('='); if (eq > 0) kv[line.Substring(0, eq)] = line.Substring(eq + 1); }
    }
    public string Get(string key, string def) { lock (kv) { string v; return kv.TryGetValue(key, out v) ? v : def; } }
    public int GetInt(string key, int def) { int v; return int.TryParse(Get(key, ""), out v) ? v : def; }
    public void Set(string key, string value) {
        lock (kv) {
            kv[key] = value;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var lines = new System.Collections.Generic.List<string>(); foreach (var e in kv) lines.Add(e.Key + "=" + e.Value);
            System.IO.File.WriteAllLines(path, lines);
        }
    }
}

// The fail-safe (Damian, 2026-10-15: "if errors appear to happen, stop doing the proxy behaviors. run clean unless
// everything seems nominal. we want this to detect subtle errors in configuration and not spam the server with bad
// requests"). It watches the relay log and the server's packets; a watch that trips disables every assist action
// (GameLink.Inject refuses, a potion double-click passes through) and the relay keeps relaying the player's own play.
// The disablement is kept in the settings until the player resumes; a watch can be turned off ("guard.off.<id>").
// Server refusals count only within InjectWindowMs of an assist injection, so the player's own refusals do not trip it.
public sealed class AssistGuard {
    public static AssistGuard Current;
    public const int InjectWindowMs = 3000, CloseWindowMs = 10000, MoveWindowMs = 1000;
    public sealed class Watch { public string Id, Text; public int Limit, Seconds; }
    public static readonly Watch[] Watches = {
        new Watch { Id = "desync", Text = "the relay lost packet framing", Limit = 1, Seconds = 60 },
        new Watch { Id = "error", Text = "a relay error", Limit = 1, Seconds = 60 },
        new Watch { Id = "unanswered", Text = "the server did not answer an assist action", Limit = 3, Seconds = 120 },
        new Watch { Id = "refusal", Text = "the server refused assist actions", Limit = 4, Seconds = 60 },
        new Watch { Id = "lift-reject", Text = "the server rejected assist lifts", Limit = 3, Seconds = 60 },
        new Watch { Id = "move-reject", Text = "the server rejected moves right after assist actions", Limit = 3, Seconds = 30 },
        new Watch { Id = "disconnect", Text = "the server dropped the connection right after an assist action", Limit = 1, Seconds = 60 },
    };
    // Refusal lines (RunUO and OUO system text, lower case).
    static readonly string[] RefusalText = { "you must wait", "too far away", "cannot reach", "can't reach", "out of range", "not in line of sight", "cannot pick that up", "can't pick that up", "that is locked", "you can't do that" };
    readonly AssistSettings cfg; readonly AssistLog log;
    readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Queue<System.DateTime>> hits = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Queue<System.DateTime>>();
    public readonly System.Collections.Generic.List<string> History = new System.Collections.Generic.List<string>();
    System.DateTime lastInject = System.DateTime.MinValue;
    public volatile string Disabled;
    public AssistGuard(AssistSettings cfg, AssistLog log) {
        this.cfg = cfg; this.log = log;
        string kept = cfg.Get("guard.disabled", ""); if (kept.Length > 0) { Disabled = kept + " (kept from an earlier session)"; History.Add(System.DateTime.Now.ToString("HH:mm:ss") + " still disabled: " + kept); }
        log.Lined += Observe;
    }
    public bool Watching(string id) { return cfg.Get("guard.off." + id, "0") != "1"; }
    public void SetWatching(string id, bool on) { cfg.Set("guard.off." + id, on ? "0" : "1"); }
    public void Resume() {
        lock (hits) hits.Clear();
        Disabled = null; cfg.Set("guard.disabled", "");
        lock (History) History.Add(System.DateTime.Now.ToString("HH:mm:ss") + " resumed by the player");
        log.Line(0, "--", "GUARD resumed by the player");
    }
    bool RecentInject(int ms) { return (System.DateTime.UtcNow - lastInject).TotalMilliseconds <= ms; }
    // A refused assist step is the walker's to route around (AssistFeatures.WalkTo), not a fault.
    System.DateTime lastStep = System.DateTime.MinValue;
    void Observe(string line) {
        if (line.Contains(" INJ C>S 02")) { lastInject = lastStep = System.DateTime.UtcNow; return; }
        if (line.Contains(" INJ C>S ")) { lastInject = System.DateTime.UtcNow; return; }
        if (line.Contains(" GUARD ")) return;
        if (line.Contains(" -- HALT ")) Hit("desync", line);
        else if (line.Contains(" -- ERROR ")) Hit("error", line);
        else if (line.Contains(" -- UNANSWERED ")) Hit("unanswered", line);
        // Only a connection the server ended (Damian, 2026-10-16: "legit logout is being interpreted as server angry": a
        // logout is the client's close, and the relay's own close of the server side then reads as an abort).
        else if (line.EndsWith(" -- ENDED BY SERVER") && RecentInject(CloseWindowMs)) Hit("disconnect", line);
    }
    // Server packets: 0x27 lift reject, 0x21 move reject, 0x1C system refusal text.
    // A step refused while stamina is 0 is the server's answer to the player's own running ("You are too fatigued to
    // move.", uo1998 16:11:49 with stamina 0 of 68, after an autopickup lift), not a fault of the assist: such a 0x21
    // is not counted. Stamina from 0xA3 (serial, max, current) and the 0x11 status (current at 50).
    volatile int stamina = -1;
    public void Server(byte[] p) {
        if (p[0] == 0xA3 && p.Length >= 9) stamina = (p[7] << 8) | p[8];
        else if (p[0] == 0x11 && p.Length >= 52 && p[42] >= 1) stamina = (p[50] << 8) | p[51];
        if (!RecentInject(InjectWindowMs)) return;
        // Out of range (1) and out of sight (2) say where the player stood, not that the assist is wrong: autopickup
        // riding past reagents tripped the watch with two of each (uo1998 2026-10-17 14:28:04). Every other reason counts.
        if (p[0] == 0x27) { if (p.Length < 2 || (p[1] != 1 && p[1] != 2)) Hit("lift-reject", "server 0x27 lift reject, reason " + (p.Length >= 2 ? p[1].ToString("X2") : "?")); }
        // A step into a mobile is refused too (uo1998 16:46:41, a mobile on the tile ahead, 1.4 s after a pickup): only a
        // refusal within MoveWindowMs of an assist action counts.
        else if (p[0] == 0x21) { if (stamina != 0 && RecentInject(MoveWindowMs) && (System.DateTime.UtcNow - lastStep).TotalMilliseconds > MoveWindowMs) Hit("move-reject", "server 0x21 move reject"); }
        else if (p[0] == 0x1C && p.Length > 44) {
            uint who = ((uint)p[3] << 24) | ((uint)p[4] << 16) | ((uint)p[5] << 8) | p[6]; if (who != 0xFFFFFFFF) return;
            string text = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').ToLowerInvariant();
            // A running loop paces itself on "you must wait" (AssistFeatures.Refused), so that line is the loop's to handle.
            if (AssistFeatures.Pacing && text.Contains("you must wait")) return;
            foreach (var r in RefusalText) if (text.Contains(r)) { Hit("refusal", "server said \"" + text + "\""); break; }
        }
    }
    void Hit(string id, string evidence) {
        Watch w = null; foreach (var x in Watches) if (x.Id == id) w = x;
        if (w == null) return;
        int n;
        lock (hits) {
            System.Collections.Generic.Queue<System.DateTime> q; if (!hits.TryGetValue(id, out q)) hits[id] = q = new System.Collections.Generic.Queue<System.DateTime>();
            q.Enqueue(System.DateTime.UtcNow); while (q.Count > 0 && (System.DateTime.UtcNow - q.Peek()).TotalSeconds > w.Seconds) q.Dequeue();
            n = q.Count;
        }
        string stamp = System.DateTime.Now.ToString("HH:mm:ss");
        lock (History) { History.Add(stamp + " " + id + " " + n + "/" + w.Limit + ": " + evidence); if (History.Count > 200) History.RemoveAt(0); }
        if (n < w.Limit || Disabled != null) return;
        if (!Watching(id)) { lock (History) History.Add(stamp + " " + id + " would disable the assist, but that watch is off"); return; }
        Disabled = w.Text + " (" + n + " in " + w.Seconds + " s): " + evidence;
        cfg.Set("guard.disabled", Disabled);
        log.Line(0, "--", "GUARD disabled the assist: " + Disabled);
    }
}

public sealed class LoopKind {
    // Probe: the delay is the 5 s assumption, so a loop that keeps working tries shorter delays too.
    public string Name; public int Skill = -1; public int Target; public int Pairs; public int DelayMs; public bool Probe; public string Source;
    // Smith: 1 builds until the pack runs out of ingots, 2 until an exceptional item; the hammer's menu picks are recorded.
    public int Smith;
    // Magery: 1 trains Magery, each cast at the circle the skill can gain on (AssistFeatures.RunMagery).
    public int Magery;
    // Smelt: 1 smelts one ore at a time from a pile on the ground (AssistFeatures.RunSmelt).
    public int Smelt;
    // Tailor: 1 sews from a cloth pile, then from a bolt of the same stack (AssistFeatures.RunTailor).
    public int Tailor;
}

public sealed class AdviceRow {
    public string Key, Text, Source; public bool Strength; public int Skill = -1; public int At; public int[] Graphics = new int[0];
}

public sealed class AssistFeatures {
    // Skill ids are the client's, 0-based (Hiding 21, as RunUO's SkillInfo.Table[21]).
    // Target: 0 none, 1 an item or creature the first iteration picks. Pairs: tool uses recorded, each with its target.
    public static readonly LoopKind[] Loops = {
        new LoopKind { Name = "Mining (1 tool)", Pairs = 1, DelayMs = 2500, Source = "Damian, 2026-10-15: \"for mining 1 its 2500ms\"; \"the delay for mining is 2500 ... in uoassist exactly. never fails at that ... for shovel or pickaxe\"" },
        new LoopKind { Name = "Mining (2 tools)", Pairs = 2, DelayMs = 1250, Source = "Damian, 2026-10-15: \"for 2 its 1250 per tool use\"" },
        new LoopKind { Name = "Blacksmithing (until out of ingots)", Skill = 7, Smith = 1, Source = "Damian, 2026-10-15: \"get smithy in there for loops. both build until exhausted of ingots and build until exceptional\"" },
        new LoopKind { Name = "Blacksmithing (until exceptional)", Skill = 7, Smith = 2, Source = "Damian, 2026-10-15: \"get smithy in there for loops. both build until exhausted of ingots and build until exceptional\"" },
        new LoopKind { Name = "Smelting (one ore at a time)", Skill = MiningSkill, Smelt = 1, DelayMs = 2500, Source = "Damian, 2026-10-16: \"given a target on the ground, take 1 of it and put it in backpack, then smelt that one item. first iteration is manual\", and of 1000 ms \"the wait between steps is a little too short\": uo1998 refused a smelt 1.9 s after the last; 2500 ms is the mining tool's" },
        new LoopKind { Name = "Tailoring (cloth, then bolts)", Skill = 34, Tailor = 1, DelayMs = 2500, Source = "Damian, 2026-10-16: \"use that pile of cloth until there isn't enough left over, then start a new bolt from the same pile of bolts if possible\"; no delay was given: uo1998 answered every demo sewing at 2 to 8 s apart (relay log 04:19:39 to 04:22:09), and 2500 ms is the mining tool's" },
        new LoopKind { Name = "Magery (training, circles 1 to 8)", Skill = 25, Magery = 1, Source = "Damian, 2026-10-15: \"add magery loops too\", \"for each spell level\", \"a loop for training the skill, and it needs to go all the way to circle 8\"" },
        new LoopKind { Name = "Hiding", Skill = 21, DelayMs = 12500, Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new LoopKind { Name = "Spirit Speak", Skill = 32, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new LoopKind { Name = "Detect Hidden", Skill = 14, Target = 1, DelayMs = 12500, Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new LoopKind { Name = "Arms Lore", Skill = 4, Target = 1, DelayMs = 12500, Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new LoopKind { Name = "Anatomy", Skill = 1, Target = 1, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new LoopKind { Name = "Animal Lore", Skill = 2, Target = 1, DelayMs = 12500, Source = "Damian, 2026-10-15: \"a timeout default of 12500 ... for lore, hiding, detect\"" },
        new LoopKind { Name = "Item Identification", Skill = 3, Target = 1, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new LoopKind { Name = "Evaluating Intelligence", Skill = 16, Target = 1, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new LoopKind { Name = "Forensic Evaluation", Skill = 19, Target = 1, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
        new LoopKind { Name = "Taste Identification", Skill = 36, Target = 1, DelayMs = 5000, Probe = true, Source = "Damian, 2026-10-15: \"start with a 5s assumption and program in a bisect\"" },
    };
    // The bisect's bounds: a probe never goes under MinDelayMs (twice RunUO's 500 ms ActionDelay), it stops once the
    // working and refused delays are within SettleMs, and a loop the server refuses past MaxDelayMs stops.
    public const int MinDelayMs = 1000, SettleMs = 250, MaxDelayMs = 60000, ProbeAfterRounds = 3;
    public static volatile bool Pacing;

    // Action timings (Damian, 2026-10-16: "make all the delays configurable so we don't have to redo this if its too slow",
    // "then add active monitoring and tuning of these shared configurations"): each kept per login server as ms.<key> and
    // shown in the window's Timings panel with what was seen. A measured timing waits for something the server sends;
    // each arrival is a sample of how long it took. With auto-tune on (ms.auto, per server) a sample past the timing
    // raises it at once, and from TuneAfter samples it follows the longest of the last TuneKeep, times 1.5 plus 100 ms,
    // never under half its default: an action timeout cut too short reads as a server refusal.
    // Name is the panel's short label and Text its tooltip; Group is the panel's subgroup.
    public sealed class Timing { public string Key, Group, Name, Text; public int Default; public bool Measured; }
    static Timing T(string key, string group, string name, int ms, bool measured, string text) { return new Timing { Key = key, Group = group, Name = name, Default = ms, Measured = measured, Text = text }; }
    public static readonly Timing[] Timings = {
        T("answer", "Server", "Answer", 3000, true, "Longest wait for the server to answer an action."),
        T("cursor", "Server", "Cursor", 3000, true, "Longest wait for a target cursor after a use."),
        T("lift-settle", "Server", "Lift settle", 600, true, "A lift counts as accepted after this long with no refusal."),
        T("pending-dig", "Mining and smelting", "Dig result", 6000, true, "A dig with no result this long counts as answered."),
        T("drop-settle", "Mining and smelting", "Ore drop", 700, false, "After ore is put down, before checking it stayed down."),
        T("smelt", "Mining and smelting", "Smelt result", 4000, true, "Longest wait for a smelt's result."),
        T("smelt-settle", "Mining and smelting", "Ingot count", 300, false, "After the smelted ore is gone, before the ingots are counted."),
        T("tailor", "Tailoring", "Sew result", 4000, true, "Longest wait for a sewing's result after the last menu pick."),
        T("craft", "Smithing", "Build max", 120000, false, "Longest a smith build may take."),
        T("craft-quiet", "Smithing", "Hammer quiet", 6000, true, "A smith build with no hammer sound this long has ended."),
        T("quality-wait", "Smithing", "Quality line", 600, false, "After a build, before its quality line is read."),
        T("label-wait", "Smithing", "Item label", 1500, true, "Longest wait for a built item's name label."),
        T("busy", "Harvest and stations", "Busy", 1250, false, "How long a double-click or a lift keeps you busy: between harvest actions, before ore is put down, and between the smelting loop's double-click and lift. Auto-tune adds 250 ms when the server refuses a smelting lift as busy."),
        T("pluck", "Harvest and stations", "Pluck", 1500, true, "Longest wait for what a pluck gives."),
        T("spin", "Harvest and stations", "Spin", 6500, false, "A spinning wheel is busy this long after it takes a bale."),
        T("work-wait", "Harvest and stations", "Station", 12000, true, "Longest wait for a mill, wheel or loom to take or give."),
        T("mana-wait", "Magery", "Mana wait", 300000, false, "Longest the magery loop waits for mana."),
        T("pricer-open", "Other", "Bag open", 700, false, "After the pricer opens a bag, before the next."),
        T("loadout-cursor-gap", "Other", "Cursor gap", 300, false, "Between the reagent loadout's two target cursors."),
        T("look", "Other", "Name click", 1500, false, "Between single clicks that name people nearby."),
    };
    public const int TuneKeep = 20, TuneAfter = 5, TimingMaxMs = 600000, TuneSlackMs = 100;
    public static Timing TimingOf(string key) { foreach (var t in Timings) if (t.Key == key) return t; return null; }
    public int Ms(string key) { var t = TimingOf(key); return Cfg.GetInt(Server + "ms." + key, t.Default); }
    public void SetMs(string key, int ms) { Cfg.Set(Server + "ms." + key, System.Math.Max(0, System.Math.Min(TimingMaxMs, ms)).ToString()); }
    public bool AutoTune { get { return Cfg.Get(Server + "ms.auto", "on") == "on"; } set { Cfg.Set(Server + "ms.auto", value ? "on" : "off"); } }
    readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> seenMs = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
    void Seen(string key, int ms) {
        var t = TimingOf(key); if (t == null || ms < 0) return;
        int n, longest = 0;
        lock (seenMs) {
            System.Collections.Generic.List<int> l; if (!seenMs.TryGetValue(key, out l)) seenMs[key] = l = new System.Collections.Generic.List<int>();
            l.Add(ms); if (l.Count > TuneKeep) l.RemoveAt(0);
            n = l.Count; foreach (var v in l) longest = System.Math.Max(longest, v);
        }
        if (!AutoTune || !t.Measured) return;
        int now = Ms(key), want = now;
        if (ms > now) want = (ms * 3 / 2 + TuneSlackMs + 49) / 50 * 50;
        else if (n >= TuneAfter) want = System.Math.Max(t.Default / 2, (longest * 3 / 2 + TuneSlackMs + 49) / 50 * 50);
        // Lowered only by more than the slack, so one sample's jitter does not rewrite the setting.
        if (want > now || want < now - TuneSlackMs) { SetMs(key, want); Note("Timing " + key + ": " + now + " ms to " + want + " ms (" + (ms > now ? "the server took " + ms + " ms" : "longest of the last " + n + ": " + longest + " ms") + ")"); }
    }
    // For the Timings panel: what was seen this session.
    public string SeenText(string key) {
        lock (seenMs) {
            System.Collections.Generic.List<int> l; if (!seenMs.TryGetValue(key, out l) || l.Count == 0) return TimingOf(key).Measured ? "not seen yet" : "set by hand";
            int longest = 0; foreach (var v in l) longest = System.Math.Max(longest, v);
            return "seen " + l.Count + ", longest " + longest + " ms, last " + l[l.Count - 1] + " ms";
        }
    }

    // A loop stops on these journal lines: the UOAIX shard's (GameHarvestState.codex:69-70), RunUO's worn tool
    // (HarvestSystem cliloc 1044038), and uo1998's empty seam, worded apart from the shard's (relay log 2026-10-15; Damian:
    // "need to stop the mining macro if the seam runs out").
    const string WornOut = "You have worn out your tool!";
    public static readonly string[] StopLines = {
        "There is no metal to mine here.", "There is no metal here to mine.", "You cannot mine there.", "You are too far away to mine that!", WornOut,
        "More reagents are needed", "You do not have enough reagents",
    };
    // uo1998's full pack. The loop puts the pack's largest ore stack down at the player's feet and goes on (Damian,
    // 2026-10-15: "full pack should dump the biggest load of ore out of your pack on to the ground at your feet, like the
    // server will do, but instead of the server, we do with the ore you have, not the overflow").
    public static readonly string[] FullPackLines = { "put it at your feet", "That container cannot hold more weight." };
    static readonly int[] OreGraphics = { 0x19B7, 0x19B8, 0x19B9, 0x19BA };
    // The bag full (Damian, 2026-10-16: "i said stop when the bag is FULL", "and the next dig is going to fucking OVERFLOW",
    // "INSTEAD move the WHOLE BIG PILE IN MY BAG", "NOT 1 FUCKING PIECE OF ORE"): the server's own status weight (0x11)
    // plus one dig's ore passes PackLimit ("its like 450, not 400"; the status read 451 when uo1998 put a dig at the feet,
    // relay log 2026-10-16 19:42:34 to 19:42:46, each dig adding 20), or the server says it put ore at the feet. Then the
    // biggest ore pile in the pack, whole, goes on the tile beside the player: every hand drop uo1998 kept went beside the
    // player, and both drops on the player's own tile came back into the pack (19:42:47, 20:04:30). The dump waits
    // DumpAfterDigMs after the last dig's target, so the server has handed over that dig's ore.
    public const int PackLimit = 450, DigGain = 20;
    int DropSettleMs { get { return Ms("drop-settle"); } }
    int PendingDigMs { get { return Ms("pending-dig"); } }
    // Digs whose result has not come back (Damian, 2026-10-16: "sometimes when a bag is full, there is a pending shovel
    // pull. moving the ore too soon will cause the single to drop in the bag ... keep track of the pending response from
    // the other shovel, and if there is a pending response, just do nothing instead"): a mining target adds one, a dig
    // result line takes the oldest off, and one older than PendingDigMs is taken as answered.
    // One line a dig ("You destroyed the shovel." follows a dig's own line, relay log 2026-10-16 20:21:21, so it is not one).
    static readonly string[] DigResults = { "You dig some ore", "You loosen some rocks", "There is no metal", "You cannot mine", "is too far away to mine" };
    readonly System.Collections.Generic.Queue<System.DateTime> pendingDigs = new System.Collections.Generic.Queue<System.DateTime>();
    int PendingDigs() { lock (pendingDigs) { while (pendingDigs.Count > 0 && (System.DateTime.UtcNow - pendingDigs.Peek()).TotalMilliseconds > PendingDigMs) pendingDigs.Dequeue(); return pendingDigs.Count; } }
    void DigResult(byte[] p) {
        if (p[0] != 0x1C || p.Length <= 44) return;
        string said = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44);
        foreach (var r in DigResults) if (said.IndexOf(r, System.StringComparison.OrdinalIgnoreCase) >= 0) {
            System.DateTime at = System.DateTime.MinValue; lock (pendingDigs) if (pendingDigs.Count > 0) at = pendingDigs.Dequeue();
            if (at != System.DateTime.MinValue) Seen("pending-dig", (int)(System.DateTime.UtcNow - at).TotalMilliseconds);
            return;
        }
    }
    const string NoOre = "the pack is full and holds no ore to put down";
    static System.Collections.Generic.HashSet<uint> PackTree(GameLink k) {
        var inside = new System.Collections.Generic.HashSet<uint>(); if (k.State.Backpack != 0) inside.Add(k.State.Backpack);
        for (int pass = 0; pass < 8; pass++) foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container)) inside.Add(kv.Key);
        return inside;
    }
    public int PackLimitFor { get { return Cfg.GetInt(Server + "pack.limit", PackLimit); } }
    bool PackNearFull(GameLink k) { int w; lock (k.State.Gate) w = k.State.Weight; return w > 0 && w + DigGain > PackLimitFor; }
    string DumpOre(GameLink k) {
        AssistItem best = null; int x, y, z;
        lock (k.State.Gate) {
            x = k.State.X; y = k.State.Y; z = k.State.Z; var inside = PackTree(k);
            foreach (var kv in k.State.Items) {
                var it = kv.Value;
                if (it.Layer < 0 && inside.Contains(it.Container) && System.Array.IndexOf(OreGraphics, it.Graphic) >= 0 && (best == null || it.Amount > best.Amount)) best = it;
            }
        }
        if (best == null) return NoOre;
        int amount = System.Math.Max(1, best.Amount), dx = x, dy = y + 1;
        // A pile of the same ore (any ore art, the same hue) within one tile takes the load (Damian, 2026-10-16: "if there is
        // an identical type of ore in 1 tile grid around the character, just move it to that existing pile instead"): the
        // drop names the pile, which stacks it; a pile that sends the load back leaves the tile beside the player.
        AssistItem pile = null;
        lock (k.State.Gate) foreach (var g in k.State.Ground.Values)
            if (System.Array.IndexOf(OreGraphics, g.Graphic) >= 0 && g.Hue == best.Hue && System.Math.Max(System.Math.Abs(g.X - x), System.Math.Abs(g.Y - y)) <= 1 && (pile == null || g.Amount > pile.Amount)) pile = g;
        for (int attempt = 0; attempt < 3; attempt++) {
            Answered(k, AnswerMs);
            var since = lastUse > harvestActAt ? lastUse : harvestActAt;
            int wait = BusyMs - (int)(System.DateTime.UtcNow - since).TotalMilliseconds;
            if (wait > 0) System.Threading.Thread.Sleep(wait);
            string why = Lifted(k, best.Serial, amount); harvestActAt = System.DateTime.UtcNow;
            if (why != null) return "putting ore down: " + why;
            var onto = pile;
            why = onto != null ? k.Drop(best.Serial, onto.Serial) : k.DropGround(best.Serial, dx, dy, z); if (why != null) return Stopped("putting ore down", best.Serial, why);
            Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
            bool back; lock (k.State.Gate) { AssistItem now; back = k.State.Items.TryGetValue(best.Serial, out now) && PackTree(k).Contains(now.Container); }
            lastUse = System.DateTime.UtcNow;
            if (back) { if (onto != null) pile = null; else { dx = x - 1; dy = y; } continue; }
            leave.Add(best.Serial);  // autopickup leaves the stack where it was put
            if (onto != null) leave.Add(onto.Serial);
            int weight; lock (k.State.Gate) weight = k.State.Weight;
            Note("Loop: the bag is full; put the whole pile of " + amount + " " + UoTiles.Name(best.Graphic) + (onto != null ? " on the ore pile " + onto.Serial.ToString("X") + " at " + onto.X + "," + onto.Y : " beside you (" + dx + "," + dy + "," + z + ")") + "; weight now " + weight);
            return null;
        }
        return "putting ore down: the server sent the pile back into the pack twice";
    }
    // The loop's delay is too short for this server: RunUO cliloc 500118 (a skill too soon) and 500119 (an action too
    // soon), and the UOAIX shard's harvest swing (GameHarvest.codex:42, 2400 ms a mining swing per player).
    public static readonly string[] WaitLines = { "You must wait", "Wait before gathering again", "You have not yet recovered", "You are already casting" };

    // Pre-AOS strength requirements, RunUO Scripts/Items (OldStrReq, OldStrengthReq); shields have none before AOS.
    public static readonly AdviceRow[] Advice = {
        Row("str10", 10, "a katana, kryss, scimitar or cutlass", "Weapons/Swords/Katana.cs, Kryss.cs, Scimitar.cs, Cutlass.cs", 0x13FF, 0x1401, 0x13B6, 0x1441),
        Row("str15", 15, "a leather tunic", "Armor/Leather/LeatherChest.cs", 0x13CC),
        Row("str20", 20, "a chain tunic, chain coif or ringmail tunic, a mace, maul or bow", "Armor/Chain/ChainChest.cs, Armor/Helmets/ChainCoif.cs, Armor/Ring/RingmailChest.cs, Weapons/Maces/Mace.cs, Maul.cs, Weapons/Ranged/Bow.cs", 0x13BF, 0x13BB, 0x13EC, 0xF5C, 0x143B, 0x13B2),
        Row("str25", 25, "a broadsword or longsword", "Weapons/Swords/Broadsword.cs, Longsword.cs", 0xF5E, 0xF61),
        Row("str30", 30, "a war mace, spear, quarter staff or crossbow", "Weapons/Maces/WarMace.cs, SpearsAndForks/Spear.cs, Staves/QuarterStaff.cs, Ranged/Crossbow.cs", 0x1407, 0xF62, 0xE89, 0xF50),
        Row("str35", 35, "studded leather, a war axe, executioner's axe, two handed axe or hammer pick", "Armor/Studded/StuddedChest.cs, Weapons/Axes/WarAxe.cs, ExecutionersAxe.cs, TwoHandedAxe.cs, Maces/HammerPick.cs", 0x13DB, 0x13B0, 0xF45, 0x1443, 0x143D),
        Row("str40", 40, "bone armor, a plate helm or plate arms, a battle axe, large battle axe, war hammer, bardiche, viking sword or heavy crossbow", "Armor/Bone/BoneChest.cs, Armor/Helmets/PlateHelm.cs, Armor/Plate/PlateArms.cs, Weapons/Axes/BattleAxe.cs, LargeBattleAxe.cs, Maces/WarHammer.cs, PoleArms/Bardiche.cs, Swords/VikingSword.cs, Ranged/HeavyCrossbow.cs", 0x144F, 0x1412, 0x1410, 0xF47, 0x13FB, 0x1439, 0xF4D, 0x13B9, 0x13FD),
        Row("str45", 45, "a double axe or halberd", "Weapons/Axes/DoubleAxe.cs, PoleArms/Halberd.cs", 0xF4B, 0x143E),
        Row("str60", 60, "a platemail breastplate", "Armor/Plate/PlateChest.cs", 0x1415),
        new AdviceRow { Key = "mining65", Skill = 45, At = 650, Text = "you can now mine colored ore, on a server that has it (65.0 Mining)", Source = "RunUO Scripts/Engines/Harvest/Mining.cs:87" },
    };
    static AdviceRow Row(string key, int at, string what, string files, params int[] graphics) {
        return new AdviceRow { Key = key, Strength = true, At = at, Text = "you can now wear or wield " + what + " (strength " + at + ")", Source = "RunUO Scripts/Items/" + files, Graphics = graphics };
    }

    // RunUO Server/Mobile.cs:4243 ActionDelay 500 ms between item actions; a lift the server accepts sends nothing, so
    // one is taken as accepted when no 0x27 refusal has come in LiftSettleMs.
    int LiftSettleMs { get { return Ms("lift-settle"); } }
    int AnswerMs { get { return Ms("answer"); } }
    int CursorMs { get { return Ms("cursor"); } }
    // RunUO Server/Mobile.cs:4272: a lift reaches 2 tiles.
    public const int LiftReach = 2;
    // RunUO Scripts/Items/Skill Items/Magical/Potions: heal 0xF0C, cure 0xF07, refresh 0xF0B, agility 0xF08, strength
    // 0xF09, night sight 0xF06; every one needs a free hand (BasePotion.cs:75, 102). Poison and explosion are not drunk here.
    public static readonly int[] Drinkable = { 0xF0C, 0xF07, 0xF0B, 0xF08, 0xF09, 0xF06 };
    // RunUO Scripts/Items/Shields: bronze 0x1B72 to metal 0x1B7B.
    public static bool IsShield(int g) { return g >= 0x1B72 && g <= 0x1B7B; }
    // RunUO Scripts/Items/Resources/Reagents: black pearl, bloodmoss, garlic, ginseng, mandrake root, nightshade,
    // sulfurous ash, spiders' silk.
    public const string DefaultReagents = "F7A,F7B,F84,F85,F86,F88,F8C,F8D";
    public static readonly string[][] CountGroups = {
        new[] { "ore", "19B7,19B8,19B9,19BA", "harvest" }, new[] { "ingots", "1BF2", "create" }, new[] { "logs", "1BDD", "harvest" }, new[] { "boards", "1BD7", "create" },
        // Damian, 2026-10-18: "add empty bottles and blank scrolls and empty scrollbooks". A spellbook's art is the same empty
        // or full, so every spellbook counts.
        new[] { "blank scrolls", "EF3", "create" }, new[] { "spellbooks", "EFA,E3B", "create" },
        new[] { "bandages", "E21", "combat" }, new[] { "arrows", "F3F", "combat" }, new[] { "bolts", "1BFB", "combat" },
        // The gems, by their 1.25 tile data names (TILEDATA.MUL 0F0F..0F30).
        new[] { "amber", "F25", "gems" }, new[] { "amethyst", "F16,F17,F22,F2E", "gems" }, new[] { "citrine", "F15,F23,F24,F2C", "gems" },
        new[] { "diamond", "F26,F27,F28,F29,F30", "gems" }, new[] { "emerald", "F10,F2F", "gems" }, new[] { "ruby", "F13,F14,F1A,F1C,F1D,F2A,F2B", "gems" },
        new[] { "sapphire", "F11,F12,F19,F1F", "gems" }, new[] { "star sapphire", "F0F,F1B,F21", "gems" }, new[] { "tourmaline", "F18,F1E,F20,F2D", "gems" },
        // The potions by the 1998 colour for each (yellow heal, orange cure, red refresh, blue agility, white strength, black
        // night sight, green poison, purple explosion), and the empty bottles alchemy fills.
        new[] { "heal", "F0C", "potions" }, new[] { "cure", "F07", "potions" }, new[] { "refresh", "F0B", "potions" },
        new[] { "agility", "F08", "potions" }, new[] { "strength", "F09", "potions" }, new[] { "night sight", "F06", "potions" },
        new[] { "poison", "F0A", "potions" }, new[] { "explosion", "F0D", "potions" }, new[] { "empty bottles", "F0E", "potions" },
    };
    // The resource panel's subgroups, in panel order (Damian, 2026-10-15: "group resources into subgroups: harvest,
    // create, reagents, and combat"; 2026-10-18: "under resources, add gems as subgroup", "add potions as subgroup").
    public static readonly string[] ResourceGroups = { "harvest", "create", "reagents", "gems", "potions", "combat" };
    // The subgroup an added item lands in: 1.25 art of ore, logs, fish, hides, wool, cotton and feathers is harvest;
    // bandages, arrows and bolts are combat; a reagent is reagents; a gem is gems; a potion or an empty bottle is potions;
    // anything else is create.
    public static string GroupOf(int g) {
        if (ReagentName(g) != null) return "reagents";
        if (g >= 0x0F0F && g <= 0x0F30) return "gems";
        if (g >= 0x0F06 && g <= 0x0F0E) return "potions";
        if ((g >= 0x19B7 && g <= 0x19BA) || (g >= 0x1BDD && g <= 0x1BE2) || (g >= 0x09CC && g <= 0x09CF) || g == 0x1078 || g == 0x1079 || g == 0x0DF8 || g == 0x0DF9 || g == 0x1BD1) return "harvest";
        if (g == 0x0E21 || g == 0x0F3F || g == 0x1BFB || (g >= 0x0F06 && g <= 0x0F0E)) return "combat";
        return "create";
    }
    // What the UO window's title bar can show; hits, mana and stamina are on until the player turns them off.
    public static readonly string[] DisplayNames = { "hits", "mana", "stamina", "strength", "dexterity", "intelligence", "weight", "gold", "armor" };
    static readonly string[] TitleDefaults = { "hits", "mana", "stamina" };

    readonly AssistLog log; public readonly AssistSettings Cfg;
    readonly object actionGate = new object();
    GameLink link; volatile bool stop; System.Threading.Thread loopThread;
    public volatile string Status = "idle", Sequence = "", LoopWords = "";
    public readonly System.Collections.Generic.List<string> Notes = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.Dictionary<string, int> seen = new System.Collections.Generic.Dictionary<string, int>();
    volatile byte[] lastRefusal; long refusals;
    uint lastEquip; System.DateTime lastEquipAt;
    readonly System.Collections.Generic.HashSet<uint> leave = new System.Collections.Generic.HashSet<uint>();

    public AssistFeatures(AssistLog log, AssistSettings cfg) {
        this.log = log; Cfg = cfg;
        var pick = new System.Threading.Thread(PickupLoop); pick.IsBackground = true; pick.Start();
        var harvest = new System.Threading.Thread(HarvestLoop); harvest.IsBackground = true; harvest.Start();
    }

    public GameLink Link { get { return link; } }
    public void Fault(string what) { Note("FAULT " + what.Replace("\r", " ").Replace("\n", " | ")); }
    void Note(string text) {
        lock (Notes) { Notes.Add(System.DateTime.Now.ToString("HH:mm:ss") + " " + text); if (Notes.Count > 100) Notes.RemoveAt(0); }
        log.Line(link != null ? link.Id : 0, "--", "ASSIST " + text);
    }

    // A client-only journal line on every skill change, as UOAssist wrote one (Damian, 2026-10-15: "its generated chat in
    // the client on skill bumps"), green up and red down ("uoassist shows drops green. i'd like to see them red").
    // Hues in the 1.25 HUES.MUL: 0x0044 is green (mid colour rgb 16,168,0), 0x0026 red (rgb 168,16,0).
    public const int RiseHue = 0x0044, DropHue = 0x0026;
    readonly System.Collections.Generic.Dictionary<int, int> lastSkills = new System.Collections.Generic.Dictionary<int, int>();
    void SkillNotices(GameLink k) {
        var said = new System.Collections.Generic.List<System.Tuple<string, int>>();
        lock (k.State.Gate) lock (lastSkills) {
            foreach (var kv in k.State.Skills) {
                int was; bool known = lastSkills.TryGetValue(kv.Key, out was); lastSkills[kv.Key] = kv.Value;
                if (!known || was == kv.Value) continue;
                int i = kv.Key - 1; string name = i >= 0 && i < AssistWindow.SkillNames.Length ? AssistWindow.SkillNames[i] : "skill " + kv.Key;
                int d = kv.Value - was;
                said.Add(System.Tuple.Create("Your skill in " + name + " has " + (d > 0 ? "increased" : "decreased") + " by " + (System.Math.Abs(d) / 10.0).ToString("0.0") + ". It is now " + (kv.Value / 10.0).ToString("0.0") + ".", d > 0 ? RiseHue : DropHue));
            }
        }
        foreach (var s in said) k.Tell(s.Item1, s.Item2);
    }

    // At login (0x55) ask for the status and the skills, as the client does when its status bar and skill gump open
    // (Damian, 2026-10-15: "we need to invoke status to get that info", "we should do that automatically on login"):
    // uo1998 sends 0x11 and the 0x3A list only when asked. One request at a time, each after the last one's answer.
    void LoginRequests(GameLink k) {
        System.Threading.Thread.Sleep(1000);
        lock (actionGate) {
            uint me; lock (k.State.Gate) me = k.State.Player;
            if (me == 0 || k.Closed) return;
            foreach (int type in new[] { 4, 5 }) {
                Answered(k, AnswerMs);
                string why = k.Status(type, me);
                if (why != null) { Note("Login " + (type == 4 ? "status" : "skills") + " request not sent: " + why); return; }
                Answered(k, AnswerMs);
            }
        }
    }

    // A client journal line when the load first crosses 50, 75 and 95% of the 1998 carry limit, strength * 4 + 30 (Damian,
    // 2026-10-15: "add a warning, for 50%, 75%, 95% to the client like with skill gains"); a mark is said again only
    // after the load has dropped below it. Hues in the 1.25 HUES.MUL: 0x0035 yellow (rgb 168,168,0), 0x002B orange
    // (168,64,0), 0x0026 red (168,16,0).
    static readonly int[] LoadMarks = { 50, 75, 95 }, LoadHues = { 0x0035, 0x002B, 0x0026 };
    // A mark is said again only after the load stayed below it for RearmMs: uo1998 sends each dig's status twice, the
    // first without the ore stack (84 stones, then 224, at 16:29:59), and that dip re-said every mark.
    const int RearmMs = 5000;
    int loadSaid;  // the highest mark said since the load was last below it
    System.DateTime belowSince = System.DateTime.MaxValue;
    void WeightNotices(GameLink k) {
        int str, weight; lock (k.State.Gate) { str = k.State.Str; weight = k.State.Weight; }
        if (str <= 0) return;
        int max = str * 4 + 30, pct = weight * 100 / max, reached = 0;
        for (int i = 0; i < LoadMarks.Length; i++) if (pct >= LoadMarks[i]) reached = i + 1;
        if (reached >= loadSaid) {
            belowSince = System.DateTime.MaxValue;
            if (reached > loadSaid) k.Tell("Your load is " + weight + " of " + max + " stones (" + pct + "%)" + (reached == LoadMarks.Length ? ": past " + max + " every step costs a hundred times the stamina." : "."), LoadHues[reached - 1]);
            loadSaid = reached;
        } else if (belowSince == System.DateTime.MaxValue) belowSince = System.DateTime.UtcNow;
        else if ((System.DateTime.UtcNow - belowSince).TotalMilliseconds >= RearmMs) { loadSaid = reached; belowSince = System.DateTime.MaxValue; }
    }

    // Stamina warnings (Damian, 2026-10-15: "add a stamina warning for both you and mount"): the player's when it falls
    // below 25% and 10% of the maximum, said again after StaminaRearmMs back above; the mount's stamina stays on the
    // server, which says "Your horse is very fatigued." when it crosses 10% (OUO mobile.c CMobile_HandleStaminaDrain), and
    // that line is repeated in red. Hues: 0x002B orange, 0x0026 red (HUES.MUL, as the load warnings).
    static readonly int[] StaminaMarks = { 25, 10 }, StaminaHues = { 0x002B, 0x0026 };
    const int StaminaRearmMs = 5000;
    int staminaSaid; System.DateTime staminaAbove = System.DateTime.MaxValue;
    void StaminaNotices(GameLink k) {
        int stam, max; lock (k.State.Gate) { stam = k.State.Stam; max = k.State.MaxStam; }
        if (max <= 0) return;
        int pct = stam * 100 / max, reached = 0;
        for (int i = 0; i < StaminaMarks.Length; i++) if (pct < StaminaMarks[i]) reached = i + 1;
        if (reached >= staminaSaid) {
            staminaAbove = System.DateTime.MaxValue;
            if (reached > staminaSaid) k.Tell("Your stamina is " + stam + " of " + max + " (" + pct + "%)" + (reached == StaminaMarks.Length ? ": rest before you run." : "."), StaminaHues[reached - 1]);
            staminaSaid = reached;
        } else if (staminaAbove == System.DateTime.MaxValue) staminaAbove = System.DateTime.UtcNow;
        else if ((System.DateTime.UtcNow - staminaAbove).TotalMilliseconds >= StaminaRearmMs) { staminaSaid = reached; staminaAbove = System.DateTime.MaxValue; }
    }

    // Mount recovery (Damian, 2026-10-15: "i need to know when the mount is recovered"). The server never sends the
    // mount's stamina, so the assist keeps an estimate by the OUO rules: each 250 ms tick takes 18 off a mobile's
    // stamina-loss counter and each 200 taken off is a point regained, a ridden mount included (npc.c
    // CNPCHash_RegenMoveTick runs regen before its parent test); a step ridden adds the step cost / 10 to the mount's
    // counter, 100 walking and 400 running (player.c HandleMovement, mobile.c HandleStaminaDrain). "Your horse is very
    // fatigued." puts the mount at 10% of its maximum and "Your horse is too fatigued to move." at 0. The maximum is
    // 75, the top of a horse's dexterity (ServUO Horse.cs 56 to 75), and the counter starts a full 200 owed: the notice
    // can be late, never early.
    const int MountMaxStam = 75, MountTickMs = 250, MountRegen = 18, MountUnit = 200, MountHue = 0x0044;
    readonly object mountGate = new object(); long mountOwed = -1; System.DateTime mountAt, mountTold; System.Threading.Timer mountTimer;
    void MountAnchor(GameLink k, int stam) {
        long eta; bool quiet;
        lock (mountGate) {
            // "Too fatigued to move" comes once per refused step: said once, and the estimate restarted at each.
            quiet = mountOwed >= 0 && (System.DateTime.UtcNow - mountTold).TotalMilliseconds < 10000;
            mountOwed = (long)(MountMaxStam - stam) * MountUnit + MountUnit; mountAt = System.DateTime.UtcNow; eta = mountOwed / MountRegen * MountTickMs / 1000;
            if (!quiet) mountTold = mountAt;
        }
        if (!quiet) k.Tell(stam == 0 ? "Your mount is out of stamina: at rest it recovers in about " + (eta + 59) / 60 + " min."
            : "Your mount is at 10% stamina or less: at rest it recovers in about " + (eta + 59) / 60 + " min.", StaminaHues[1]);
    }
    void MountSettle() {
        bool done = false;
        lock (mountGate) {
            if (mountOwed < 0) return;
            long ticks = (long)((System.DateTime.UtcNow - mountAt).TotalMilliseconds / MountTickMs);
            mountOwed -= ticks * MountRegen; mountAt = mountAt.AddMilliseconds(ticks * MountTickMs);
            if (mountOwed <= 0) { mountOwed = -1; done = true; }
        }
        var k = link; if (done && k != null) k.Tell("Your mount has recovered its stamina (estimated).", MountHue);
    }
    void MountStep(GameLink k, byte[] p) {
        bool riding = false;
        lock (k.State.Gate) foreach (var kv in k.State.Items) if (kv.Value.Layer == 0x19 && kv.Value.Mobile == k.State.Player) { riding = true; break; }
        if (!riding) return;
        MountSettle();
        lock (mountGate) if (mountOwed >= 0) mountOwed += (p[1] & 0x80) != 0 ? 40 : 10;
    }

    // Pets and the mount (Damian, 2026-10-15: "add pet and mount happiness"). uo1998 tells a pet's mood only when it is fed:
    // food dropped on the pet, then the pet says "Your pet looks happier." (relay log 02:57:37, the pet wind 055A47EB), so
    // a pet is learnt from that line and its happiness shown as its feedings; the mount is the pet whose double-click put
    // an item on the player's mount layer (0x19) at once. Kept in the settings ("pets": serial, name, last fed ticks,
    // feedings, 1 for the mount; | between pets).
    // History (Damian, 2026-10-15: "keep history for each pet of its feedings, kills, commands, etc"): every event a line in
    // pets-<server>.txt beside the settings (time, serial, name, event), the newest PetEventsKeep in memory for the window.
    // A command is the player's own speech that starts with the pet's name or "all"; a kill is a death (0xAF) of a mobile
    // the pet was last seen fighting (0x2F) within PetFightMs, the 1.25 packets, not yet seen on uo1998's wire.
    public sealed class Pet { public uint Serial; public string Name = ""; public System.DateTime LastFed; public int Fed, Kills, Commands; public bool Mount; }
    public const int PetEventsKeep = 200, PetFightMs = 30000;
    readonly System.Collections.Generic.List<string> petEvents = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.Dictionary<uint, uint> foughtBy = new System.Collections.Generic.Dictionary<uint, uint>();
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> foughtAt = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    uint mountItem;
    string PetsPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "pets-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    void PetEvent(Pet p, string what) {
        string line = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + p.Serial.ToString("X") + "\t" + (p.Name.Length > 0 ? p.Name : "-") + "\t" + what;
        lock (petEvents) { petEvents.Add(line); if (petEvents.Count > PetEventsKeep) petEvents.RemoveAt(0); }
        try { System.IO.File.AppendAllText(PetsPath, line + "\r\n"); } catch { }
    }
    // The newest events first, for the window.
    public string[] PetHistory() {
        lock (petEvents) {
            if (petEvents.Count == 0) try { if (System.IO.File.Exists(PetsPath)) { var all = System.IO.File.ReadAllLines(PetsPath); for (int i = System.Math.Max(0, all.Length - PetEventsKeep); i < all.Length; i++) petEvents.Add(all[i]); } } catch { }
            var a = petEvents.ToArray(); System.Array.Reverse(a); return System.Array.ConvertAll(a, s => s.Replace("\t", "  "));
        }
    }
    readonly System.Collections.Generic.List<Pet> pets = new System.Collections.Generic.List<Pet>();
    uint usedMobile; System.DateTime usedMobileAt; bool petsLoaded;
    public const int MountUseMs = 3000;
    Pet PetOf(uint s) {
        if (!petsLoaded) {
            petsLoaded = true;
            foreach (var e in Cfg.Get("pets", "").Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)) {
                var t = e.Split(','); if (t.Length < 5) continue;
                try { pets.Add(new Pet { Serial = System.Convert.ToUInt32(t[0], 16), Name = t[1], LastFed = new System.DateTime(long.Parse(t[2])), Fed = int.Parse(t[3]), Mount = t[4] == "1", Kills = t.Length > 5 ? int.Parse(t[5]) : 0, Commands = t.Length > 6 ? int.Parse(t[6]) : 0 }); } catch { }
            }
        }
        foreach (var p in pets) if (p.Serial == s) return p;
        return null;
    }
    void SavePets() { Cfg.Set("pets", string.Join("|", pets.ConvertAll(p => p.Serial.ToString("X") + "," + p.Name.Replace(",", " ").Replace("|", " ") + "," + p.LastFed.Ticks + "," + p.Fed + "," + (p.Mount ? "1" : "0") + "," + p.Kills + "," + p.Commands))); }
    // The player's own speech (0x03: type, hue, font, then text): a command to every pet named first, or to all.
    void PetCommand(byte[] p) {
        if (p.Length <= 8) return;
        string text = System.Text.Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0').Trim(); if (text.Length == 0) return;
        string lower = text.ToLowerInvariant();
        lock (pets) {
            PetOf(0); bool any = false;
            foreach (var pet in pets) {
                bool all = lower.StartsWith("all "), named = pet.Name.Length > 0 && lower.StartsWith(pet.Name.ToLowerInvariant() + " ");
                if (!all && !named) continue;
                pet.Commands++; any = true; PetEvent(pet, "command: " + text);
            }
            if (any) SavePets();
        }
    }
    void PetPacket(GameLink k, byte[] p) {
        if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) != 0xFFFFFFFF && System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).IndexOf("pet looks happier", System.StringComparison.OrdinalIgnoreCase) >= 0) {
            uint s = U32(p, 3); string name = System.Text.Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
            lock (pets) { var pet = PetOf(s); if (pet == null) { pet = new Pet { Serial = s }; pets.Add(pet); } if (name.Length > 0) pet.Name = name; pet.LastFed = System.DateTime.Now; pet.Fed++; SavePets(); PetEvent(pet, "fed: looks happier"); }
            // Fed while dismounted, the mount is taken as rested: a stack of 9 or more food gives ServUO's 15 a piece less 50,
            // past a horse's 75 (BaseCreature.CheckFeed), and the feeding drops a stack of 10.
            bool riding; lock (k.State.Gate) { riding = false; foreach (var it in k.State.Items.Values) if (it.Layer == MountLayer && it.Mobile == k.State.Player) riding = true; }
            if (!riding) lock (mountGate) mountOwed = -1;
        }
        // 0x2F fight: a byte, attacker, defender. 0xAF death: the dying mobile, its corpse.
        if (p[0] == 0x2F && p.Length >= 10) lock (foughtBy) { if (foughtBy.Count > 2000) { foughtBy.Clear(); foughtAt.Clear(); } uint a = U32(p, 2), d = U32(p, 6); foughtBy[d] = a; foughtAt[d] = System.DateTime.UtcNow; foughtBy[a] = d; foughtAt[a] = System.DateTime.UtcNow; }
        if (p[0] == 0xAF && p.Length >= 5) {
            uint dead = U32(p, 1); uint by = 0; System.DateTime at;
            lock (foughtBy) if (foughtBy.TryGetValue(dead, out by) && (!foughtAt.TryGetValue(dead, out at) || (System.DateTime.UtcNow - at).TotalMilliseconds > PetFightMs)) by = 0;
            lock (pets) {
                var victim = PetOf(dead); if (victim != null) PetEvent(victim, "died");
                var killer = by != 0 ? PetOf(by) : null;
                if (killer != null) { killer.Kills++; SavePets(); PetEvent(killer, "kill: " + MobileName(k, dead)); KillAt(k, dead, MobileName(k, dead), killer.Name, p.Length >= 9 ? U32(p, 5) : 0); }
            }
        }
        // The mount item leaving the player: dismounted.
        if (p[0] == 0x1D && p.Length >= 5 && mountItem != 0 && U32(p, 1) == mountItem) {
            mountItem = 0; lock (pets) { PetOf(0); foreach (var pet in pets) if (pet.Mount) PetEvent(pet, "dismounted"); }
        }
        // 0x2E: item, graphic, a byte, layer (8), mobile (9): the mount layer on the player just after a double-click on a mobile.
        if (p[0] == 0x2E && p.Length >= 13 && p[8] == 0x19 && usedMobile != 0 && (System.DateTime.UtcNow - usedMobileAt).TotalMilliseconds < MountUseMs) {
            uint me; lock (k.State.Gate) me = k.State.Player;
            if (U32(p, 9) == me) {
                string name; lock (k.State.Gate) k.State.Names.TryGetValue(usedMobile, out name);
                lock (pets) { var pet = PetOf(usedMobile); if (pet == null) { pet = new Pet { Serial = usedMobile, Name = name ?? "" }; pets.Add(pet); } foreach (var o in pets) o.Mount = o == pet; SavePets(); PetEvent(pet, "mounted"); }
                usedMobile = 0; mountItem = U32(p, 1);
            }
        }
    }
    // One line a pet for the window: its name, the mount, its feedings, and the mount's estimated stamina.
    public string PetLines() {
        var lines = new System.Collections.Generic.List<string>();
        lock (pets) {
            PetOf(0);
            foreach (var p in pets) {
                string fed = (p.Fed == 0 ? "not fed while watched" : "fed " + (int)(System.DateTime.Now - p.LastFed).TotalMinutes + " min ago (" + p.LastFed.ToString("HH:mm") + "), " + p.Fed + (p.Fed == 1 ? " feeding" : " feedings")) + ", " + p.Kills + " kills, " + p.Commands + " commands";
                lines.Add((p.Name.Length > 0 ? p.Name : p.Serial.ToString("X")) + (p.Mount ? " (your mount)" : "") + ": " + fed + (p.Mount ? "; " + MountStamina() : ""));
            }
        }
        return lines.Count == 0 ? "No pet known yet: feed a pet and it is learnt from \"Your pet looks happier.\"" : string.Join("\n", lines);
    }
    string MountStamina() {
        MountSettle(); long owed; lock (mountGate) owed = mountOwed;
        if (owed < 0) return "stamina: no fatigue since the last full estimate";
        long stam = System.Math.Max(0, MountMaxStam - (owed + MountUnit - 1) / MountUnit);
        return "stamina about " + stam + " of " + MountMaxStam + " (estimated)";
    }

    public void Attach(GameLink k) {
        // A trip running on the connection being replaced ends with it.
        if (trip != null) { string path; string why = EndTrip(out path); if (why != null) Note("Trip: " + why); }
        link = k; stop = true; loadSaid = 0; staminaSaid = 0;
        // A new connection knows no container's contents: the pack keeps its serial across a login, and the old list
        // made a pack never opened since look counted (Resources read low, 2026-10-18).
        lock (contents) { contents.Clear(); contentAmounts.Clear(); }
        k.ServerPacket += p => PetPacket(k, p);
        k.ClientPacket += p => { if (p[0] == 0x03) PetCommand(p); };
        k.ClientPacket += p => LootClient(k, p);
        k.ServerPacket += p => TripPacket(k, p);
        k.ServerPacket += DigResult;
        lock (pendingDigs) pendingDigs.Clear();
        k.ClientPacket += p => { if (p[0] == 0x06 && p.Length >= 5) { uint s = U32(p, 1) & 0x7FFFFFFF; bool mobile; lock (k.State.Gate) mobile = k.State.Bodies.ContainsKey(s) && s != k.State.Player; if (mobile) { usedMobile = s; usedMobileAt = System.DateTime.UtcNow; } } };
        lock (mountGate) mountOwed = -1;
        if (mountTimer == null) mountTimer = new System.Threading.Timer(_ => MountSettle(), null, 1000, 1000);
        k.ClientPacket += p => { if (p[0] == 0x02 && p.Length >= 3) MountStep(k, p); };
        lock (seen) seen.Clear();
        lock (lastSkills) lastSkills.Clear();
        k.ServerPacket += p => {
            if (p[0] == 0x27) { RefusalSeen(); lastRefusal = p; System.Threading.Interlocked.Increment(ref refusals); }
            if (p[0] == 0x11 || p[0] == 0x3A) CheckAdvice(k);
            if (p[0] == 0x3A) SkillNotices(k);
            if (p[0] == 0x11) WeightNotices(k);
            if (p[0] == 0x11 || p[0] == 0xA3) StaminaNotices(k);
            if (p[0] == 0x2C && p.Length >= 2 && p[1] == 0) PlayerDied(k);
            if (p[0] == 0x1C && p.Length > 44) {
                string said = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44);
                if (said.IndexOf("horse is very fatigued", System.StringComparison.OrdinalIgnoreCase) >= 0) MountAnchor(k, MountMaxStam / 10);
                else if (said.IndexOf("horse is too fatigued to move", System.StringComparison.OrdinalIgnoreCase) >= 0 || said.IndexOf("mount is too fatigued to move", System.StringComparison.OrdinalIgnoreCase) >= 0) MountAnchor(k, 0);
            }
            if (p[0] == 0x55) { loginAt = System.DateTime.UtcNow; System.Threading.ThreadPool.QueueUserWorkItem(_ => LoginRequests(k)); System.Threading.ThreadPool.QueueUserWorkItem(_ => SessionTrip()); }
            if (p[0] == 0xAF && p.Length >= 5) Died(k, U32(p, 1), p.Length >= 9 ? U32(p, 5) : 0);
            if (p[0] == 0x27) liftSerial = 0;
            People(k, p);
            if (p[0] == 0x1C) CheckStrengthRefusal(k);
        };
        k.ClientPacket += p => { if (p[0] == 0x3B || p[0] == 0x9F) Traded(k, p); };
        // A spell's target (0x6C: type, cursor id, flags, then the clicked serial) is a fight as well: 6 of those 13 deaths
        // were a cast (0x12) then a target on the creature.
        k.ClientPacket += p => { if (p[0] == 0x05 && p.Length >= 5) Fought(U32(p, 1)); else if (p[0] == 0x6C && p.Length >= 11 && p[1] == 0 && U32(p, 7) != 0) Fought(U32(p, 7)); };
        // A fight the server starts counts too (Damian, 2026-10-16: "the map is slow to update the kills i make"): uo1998
        // sets the combatant (0xAA) and reports the player swinging (0x2F, attacker then defender) with no 0x05 from the
        // client, so 13 deaths in a row (relay log 2026-10-16 16:27 to 16:37) made no kill mark.
        k.ServerPacket += p => {
            if (p[0] == 0xAA && p.Length >= 5 && U32(p, 1) != 0) Fought(U32(p, 1));
            else if (p[0] == 0x2F && p.Length >= 10) { uint me; lock (k.State.Gate) me = k.State.Player; if (U32(p, 2) == me) Fought(U32(p, 6)); }
        };
        if (PeopleReportPath.Length == 0) {
            string safe = Server.TrimEnd('.').Replace(':', '-').Replace(',', '-');
            PeopleReportPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "reports", "people-" + safe + "-" + System.DateTime.Now.ToString("yyyyMMdd-HHmm") + ".md");
            Atlas = new AssistAtlas(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "atlas-" + safe + ".txt"));
            reportTimer = new System.Threading.Timer(_ => { WritePeopleReport(); Atlas.Save(); }, null, ReportMs, ReportMs);
            var look = new System.Threading.Thread(LookLoop); look.IsBackground = true; look.Start();
        }
        // The player's own skill use (0x12 type 0x24, "<skill> 0"): 1998 skills share one timer, so any skill counts.
        k.ClientPacket += p => { if (p[0] == 0x12 && p.Length > 4 && p[3] == 0x24) ownSkillAt = System.DateTime.UtcNow; };
        if (Smith == null) Smith = new SmithBook(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "smithing-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"));
        if (Tailor == null) Tailor = new SmithBook(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "tailoring-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"));
        foreach (var c in Crafts) if (c.Book == null) c.Book = new SmithBook(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "craft-" + c.Name.ToLowerInvariant() + "-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"));
        k.ServerPacket += p => SmithPacket(k, p);
        k.ClientPacket += p => { if (p[0] == 0x06 && p.Length >= 5) { var it = Item(k, U32(p, 1) & 0x7FFFFFFF); craftTool = it == null ? null : CraftOf(it.Graphic); craftToolAt = System.DateTime.UtcNow; } };
        // The last pick decides: a pick with no metal cost (a tailoring menu, uo1998 04:19:45) is no smith build, so its
        // "You create" is not recorded as one.
        k.ClientPacket += p => {
            if (p[0] != 0x7D || p.Length < 9) return;
            var t = MenuItem(lastMenuSeen, (p[7] << 8) | p[8]); var c = CraftNow();
            if (c != null && CraftCost(t) > 0) { lastCraft = c; lastCraftItem = SmithBook.Display(t); lastCraftAt = System.DateTime.UtcNow; } else lastCraft = null;
            if (c == null && MetalCost(t) > 0) { lastBuild = SmithBook.Display(t); lastBuildAt = System.DateTime.UtcNow; } else lastBuild = null;
            if (ClothCost(t) > 0) { lastSewn = SmithBook.Display(t); lastSewnAt = System.DateTime.UtcNow; } else lastSewn = null;
        };
        k.ClientPacket += p => { if (p[0] == 0x13 && p.Length >= 5) { lastEquip = ((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8) | p[4]; lastEquipAt = System.DateTime.UtcNow; } };
        lock (markers) markers.Clear();
        k.HoldClient = p => HoldLoginSelfUse(k, p) || HoldRecord(k, p) || HoldMarker(k, p) || HoldPotion(k, p) || HoldPricer(k, p) || HoldLoadout(k, p);
        k.HoldServer = p => HoldDigChatter(p) || HoldSound(p) || HoldShop(p) || HoldPrompt(p) || HoldLabel(p) || (p[0] == 0x27 && k.AssistLift);
        k.ServerPacket += p => CircuitWatch(k, p);
        // The assist speaks in the player's own hue and font.
        k.ClientPacket += p => { if (p[0] == 0x03 && p.Length >= 8) { k.SpeechHue = (p[4] << 8) | p[5]; k.SpeechFont = (p[6] << 8) | p[7]; } };
        var guard = AssistGuard.Current; if (guard != null) k.ServerPacket += guard.Server;
    }

    // uo1998's system lines on every dig (relay log 2026-10-16 01:08:47 to 01:08:58, sender FFFFFFFF), kept off the client's
    // screen while a mining loop runs (Damian, 2026-10-16); the loop still reads them from the journal.
    static readonly string[] DigChatter = { "You loosen some rocks", "You dig some", "Where do you wish to dig?" };
    // A label (0x1C type 6) answering the assist's own single click (LabelsOf) stays off the client's screen until
    // shortly after the clicks (Damian, 2026-10-17: "a flood of \"unmarked recall rune\" to show up in my chat").
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> looking = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    bool HoldLabel(byte[] p) {
        if (p[0] != 0x1C || p.Length <= 44 || p[9] != 6) return false;
        uint s = U32(p, 3); System.DateTime until;
        lock (looking) { if (!looking.TryGetValue(s, out until)) return false; if (System.DateTime.UtcNow <= until) return true; looking.Remove(s); return false; }
    }
    bool HoldDigChatter(byte[] p) {
        var run = currentRun;
        if (p[0] != 0x1C || p.Length <= 44 || run == null || !run.Name.StartsWith("Mining") || U32(p, 3) != 0xFFFFFFFF) return false;
        string text = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44);
        foreach (var c in DigChatter) if (text.StartsWith(c)) return true;
        return false;
    }

    // The buy list (0x74) runs in the reverse of its stock's order (0x3C): Jannelle's 43 entries end "Shuttle of thread @3"
    // for the stock's first item, 0x0FA0 x19, and begin "Sewing kit @3" for its last, 0x0F9D (uo1998 2026-10-17 00:17:52);
    // paired straight, a thread buy was written as "Sewing kit". The stock index of list entry i; -1 when the two differ in
    // length.
    static int StockIndex(int i, int listCount, int stockCount) { return listCount == stockCount ? stockCount - 1 - i : -1; }

    // ---------- walking (assist-plan.md stage 6) ----------
    // Toward a tile until within reach, a step every StepMs at walking pace: the direction straight at it first, then the
    // ones beside it. A refused step marks its tile blocked; a door within a tile of a refused step is opened (Damian,
    // 2026-10-17: "you have to learn to open those") and the step tried again. A tile another character stands on is
    // stepped around: a step through one is blocked without full stamina and costs stamina with it. The client is shown
    // the player after every step it did not take itself.
    // A walk runs (direction | 0x80, as the client sends a run) at RunMs a step, the on-foot run pace (Damian, 2026-10-17:
    // "walking very slowly" at 594 ms a step). The route is followed, not planned again every step: a refusal, a
    // character on the next tile, or ReplanSteps steps plan it anew.
    // Mounted, a run is RunMountMs a step (ServUO's mounted run pace; Damian: "you have a horse, you can run too"); a
    // mount is the item on layer MountLayer.
    // ReplanSteps: a route is planned again after this many steps as well as on a refusal or a character on the next tile.
    public const int StepMs = 400, RunMs = 200, RunMountMs = 100, WalkMountMs = 200, MountLayer = 0x19, RunFlag = 0x80, ReplanSteps = 64, StepAnswerMs = 1500, WalkStepsMax = 400, WalkRefusalsMax = 40;
    // FatigueRestMs: a mount regains a point every 200 / 18 ticks of 250 ms, about 2.8 s (the estimate below), so 10 s
    // buys a few steps of running.
    // A refusal for fatigue rests FatigueRestMs, doubled for each one more in the same walk, at most FatigueRestMaxMs.
    public const int StraightCost = 100, DiagonalCost = 101, StepsAhead = 2, StepSettleMs = 300, FatigueRestMs = 10000, FatigueRestMaxMs = 60000, WarnRestMs = 5000;
    // Pace (Damian, 2026-10-17: "when the horse is in a known rested state, run it if it helps. when it is in a known tired
    // state, rest it a little to catch its breath, but before its fully recouped, you can start walking again to the next
    // natural resting job. and if walking is still a problem, rest a bit longer"): "auto" runs a mount the estimate holds
    // rested (MountRested) and walks one it holds tired, deciding again every step, so the walk turns to a run once the
    // mount has recovered; "walk" and "run" fix it (the control port's `pace auto|walk|run`). On foot "auto" runs.
    public volatile string Pace = "auto";
    bool Mounted(GameLink k) { lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Layer == MountLayer && it.Mobile == k.State.Player) return true; return false; }
    bool MountRested() { MountSettle(); lock (mountGate) return mountOwed < 0; }
    bool RunNow(GameLink k) { var p = Pace; return p == "run" || (p == "auto" && (!Mounted(k) || MountRested())); }
    int StepPace(GameLink k) { lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Layer == MountLayer && it.Mobile == k.State.Player) return RunMountMs; return RunMs; }
    static readonly int[] DX = { 0, 1, 1, 1, 0, -1, -1, -1 }, DY = { -1, -1, 0, 1, 1, 1, 0, -1 };
    static int DirTo(int dx, int dy) { int sx = System.Math.Sign(dx), sy = System.Math.Sign(dy); for (int d = 0; d < 8; d++) if (DX[d] == sx && DY[d] == sy) return d; return 0; }
    // Any mobile in view on the tile, animals as well as people (two dogs in a row stopped a walk, 2026-10-17): the
    // server's own tiles (AssistState.Mobiles), not the atlas, which keeps people.
    bool Occupied(GameLink k, int x, int y) {
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) if (kv.Key != k.State.Player && kv.Value[0] == x && kv.Value[1] == y) return true;
        return false;
    }
    // One step and its answer: 1 moved or turned, 0 refused, -1 no answer. The client is told to take it (0x97) and walks
    // it as its own; a client that takes no step for it leaves the walk to steps the assist sends itself (clientWalks off).
    // The 1.25 client takes no step for 0x97 (uo1998 2026-10-17 07:44:23, 9707 then nothing), so the assist sends them.
    bool clientWalks = false;
    int StepOnce(GameLink k, int dir) {
        if (clientWalks) {
            int ca = k.ClientStepAcks, cr = k.ClientStepRefusals;
            if (k.MoveClient(dir) != null) return -1;
            var by = System.DateTime.UtcNow.AddMilliseconds(StepAnswerMs);
            while (System.DateTime.UtcNow < by && !k.Closed) { if (k.ClientStepRefusals != cr) return 0; if (k.ClientStepAcks != ca) return 1; System.Threading.Thread.Sleep(10); }
            clientWalks = false; Note("Walk: the client took no step when told to; the assist sends the steps itself");
        }
        int acks = k.StepAcks, refusals = k.StepRefusals;
        if (k.Step(dir) != null) return -1;
        var until = System.DateTime.UtcNow.AddMilliseconds(StepAnswerMs);
        while (System.DateTime.UtcNow < until && !k.Closed) { if (k.StepRefusals != refusals) return 0; if (k.StepAcks != acks) return 1; System.Threading.Thread.Sleep(10); }
        return -1;
    }
    // A tile is blocked at the player's height by a wall or impassable static or ground item that overlaps a person's
    // height above the floor, however low (1.25 TILEDATA flags 0x10 wall, 0x40 impassable; a door, 0x20000000, is opened
    // instead). Players' piles block this way (Damian, 2026-10-17: "look for blocking items too ... like big ore piles,
    // boxes and stuff other players can put down"): the big iron ore pile 0x19B9 (1 high), crates, wooden boxes and
    // chests are impassable in the tile data; a bag, a small ore pile or a shirt is not.
    const uint TileWall = 0x10, TileImpassable = 0x40, TileDoor = 0x20000000;
    public const int PersonHeight = 16, PlanMargin = 24, TurnCost = 100, DoorClear = 2, DoorTries = 4, DoorBackoffMs = 500, DoorOpenMs = 1000;
    // A door or a gate, opened by use (Damian, 2026-10-17, of a Moonglow gate: "opens like a door").
    static bool IsDoor(int graphic) { string n = UoTiles.Name(graphic).ToLowerInvariant(); return n.Contains("door") || n.Contains("gate"); }
    static bool Blocks(int graphic, int z, int pz) {
        var l = UoTiles.Items(); if (graphic < 0 || graphic >= l.Count) return false; var t = l[graphic];
        if (IsDoor(graphic)) return false;
        if ((t.Flags & (TileWall | TileImpassable)) == 0 || (t.Flags & TileDoor) != 0) return false;
        int top = z + System.Math.Max(1, t.Height);
        return z < pz + PersonHeight && top > pz;
    }
    // The height a walker from fromZ stands at on the tile, or int.MinValue when the tile is closed to it. The map's
    // floor (UoMap.StandZ) decides the height; walls and impassable statics and ground items are tested at that height.
    // An unreadable map falls back to the walker's own height and passable land.
    int Open(GameLink k, int x, int y, int fromZ, System.Collections.Generic.Dictionary<long, int> seen, bool shove = false) {
        long key = State(x, y, fromZ); int z;
        if (seen.TryGetValue(key, out z)) return z;
        z = UoMap.StandZ(x, y, fromZ);
        if (z == int.MinValue && UoMap.LandZ(x, y) == int.MinValue && !UoMap.LandBlocks(x, y)) z = fromZ;
        bool open = z != int.MinValue;
        if (open) foreach (var s in UoMap.StaticsAt(x, y)) if (Blocks(s[0], s[1], z)) { open = false; break; }
        if (open) lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (g.X == x && g.Y == y && Blocks(g.Graphic, g.Z, z)) { open = false; break; }
        if (open && !shove && Occupied(k, x, y)) open = false;
        if (!open) z = int.MinValue;
        seen[key] = z; return z;
    }
    // The shortest walk (8 directions, a diagonal only past two open sides) from a tile to within reach of another, over
    // the box around both widened by PlanMargin; the directions in order, or null when there is none.
    // Whether the line from a tile at height z to another crosses nothing a person cannot see through at eye height (z +
    // PersonHeight - 1): a wall or impassable static or ground item that spans it, or a door (Damian, 2026-10-17: "you
    // could be 1 tile away but not able to talk"). A counter below the eyes does not block. The tiles strictly between
    // the two, by Bresenham.
    static bool SightBlocks(int graphic, int z, int pz) {
        var l = UoTiles.Items(); if (graphic < 0 || graphic >= l.Count) return false; var t = l[graphic];
        if ((t.Flags & (TileWall | TileImpassable)) == 0) return false;
        int eye = pz + PersonHeight - 1; return z <= eye && z + System.Math.Max(1, t.Height) > eye;
    }
    bool Sight(GameLink k, int x0, int y0, int z, int x1, int y1) {
        if (x0 == x1 && y0 == y1) return true;
        int dx = System.Math.Abs(x1 - x0), dy = -System.Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy, x = x0, y = y0;
        while (true) {
            int e2 = 2 * err; if (e2 >= dy) { err += dy; x += sx; } if (e2 <= dx) { err += dx; y += sy; }
            if (x == x1 && y == y1) return true;
            foreach (var s in UoMap.StaticsAt(x, y)) if (SightBlocks(s[0], s[1], z)) return false;
            lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (g.X == x && g.Y == y && (SightBlocks(g.Graphic, g.Z, z) || UoTiles.Name(g.Graphic).ToLowerInvariant().Contains("door"))) return false;
        }
    }
    // tz: the floor the walk ends on, within FloorApart of it; AnyFloor for any.
    public const int AnyFloor = int.MinValue;
    static bool OnFloor(int z, int tz) { return tz == AnyFloor || System.Math.Abs(z - tz) < FloorApart; }
    // A walk state: a tile and the height stood at there, so one tile holds a state per floor (the Magincia mage shop's
    // floors at z 20, 40 and 60 over the same tiles).
    static long State(int x, int y, int z) { return ((((long)x << 16) | (uint)(y & 0xFFFF)) << 8) | (byte)(z + 128); }
    static int StateX(long s) { return (int)(s >> 24); }
    static int StateY(long s) { return (int)((s >> 8) & 0xFFFF); }
    static int StateZ(long s) { return (int)(s & 0xFF) - 128; }
    // The cheapest walks from the starts over the box x0, y0, w, h: every state reached, its cost and the step that reached
    // it (from: the previous state times 8 plus the direction, -1 at a start); stops at the first state goal accepts and
    // returns it, else -1 with the whole box walked. Cheapest first, a straight step StraightCost and a diagonal
    // DiagonalCost: every step takes the same time, so the route has the fewest steps, and of those the fewest diagonals,
    // a straight line where one will do (Damian, 2026-10-17: "the pathfinding is choosing diagonals here when straight
    // line would be preferred").
    // back: the walks INTO the starts, each step taken only where its reverse is open too, as a step down off a roof is
    // open and the climb back is not.
    long Walks(GameLink k, System.Collections.Generic.IEnumerable<long> starts, int x0, int y0, int w, int h, System.Collections.Generic.HashSet<long> refused, bool shove, System.Func<int, int, int, bool> goal, System.Collections.Generic.Dictionary<long, int> cost, System.Collections.Generic.Dictionary<long, long> from, bool back = false) {
        var seen = new System.Collections.Generic.Dictionary<long, int>();
        // The standing height on a tile reached from a tile at fromZ, int.MinValue when closed.
        System.Func<int, int, int, int> open = (x, y, fromZ) => x >= x0 && y >= y0 && x < x0 + w && y < y0 + h && !refused.Contains(((long)x << 16) | (uint)y) ? Open(k, x, y, fromZ, seen, shove) : int.MinValue;
        // Each search node is a state and the heading it was reached by (h = (state << 4) | direction, 8 at a start or
        // off a teleporter), and a change of heading costs TurnCost: a turn is a move of its own and tires the mount, and
        // the shortest walk hugging Vesper's river bank turned at nearly every step (Damian, 2026-10-18: "use backtracking
        // for minimizing turns, bearing in mind ... a diagonal move actually moves more real distance than the 4
        // cardinals"). cost and from hold, per plain state, its first (cheapest) arrival; the goal's route is written
        // into them along the node chain it was reached by.
        var hcost = new System.Collections.Generic.Dictionary<long, int>(); var hfrom = new System.Collections.Generic.Dictionary<long, long>();
        var queue = new System.Collections.Generic.PriorityQueue<long, int>(); var done = new System.Collections.Generic.HashSet<long>();
        foreach (var s in starts) { long hs = (s << 4) | 8; hcost[hs] = 0; hfrom[hs] = -1; queue.Enqueue(hs, 0); }
        // The known teleporters as one-step links (from: -2 minus the state the link leaves), reversed for back.
        var tele = Teleporters(k); var into = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<long>>();
        foreach (var kv in tele) foreach (var e in kv.Value) {
            long a = State((int)(kv.Key >> 16), (int)(kv.Key & 0xFFFF), e[0]), b = State(e[1], e[2], e[3]);
            long src = back ? b : a, dst = back ? a : b; System.Collections.Generic.List<long> l;
            if (!into.TryGetValue(src, out l)) into[src] = l = new System.Collections.Generic.List<long>(); l.Add(dst);
        }
        while (queue.Count > 0) {
            long hc = queue.Dequeue(); if (!done.Add(hc)) continue;
            long c = hc >> 4; int head = (int)(hc & 15);
            int cx = StateX(c), cy = StateY(c), cz = StateZ(c);
            if (!cost.ContainsKey(c)) {
                long hp = hfrom[hc]; cost[c] = hcost[hc];
                from[c] = hp == -1 ? -1 : hp <= -2 ? -2 - ((-2 - hp) >> 4) : (((hp >> 4) >> 4) << 3) | (hp & 15 & 7);
            }
            if (goal != null && goal(cx, cy, cz)) {
                // The route the goal was reached by, written over each state's first arrival.
                for (long n = hc; hfrom[n] != -1; ) {
                    long p = hfrom[n], ps = p <= -2 ? (-2 - p) : (p >> 4); long s = n >> 4;
                    from[s] = p <= -2 ? -2 - (ps >> 4) : ((ps >> 4) << 3) | (p & 7); cost[s] = hcost[n]; n = ps;
                }
                return c;
            }
            System.Collections.Generic.List<long> links;
            if (into.TryGetValue(c, out links)) foreach (var n in links) {
                int nx = StateX(n), ny = StateY(n), was; if (nx < x0 || ny < y0 || nx >= x0 + w || ny >= y0 + h) continue;
                long hn = (n << 4) | 8; int nc = hcost[hc] + StraightCost; if (done.Contains(hn) || (hcost.TryGetValue(hn, out was) && nc >= was)) continue;
                hcost[hn] = nc; hfrom[hn] = -2 - hc; queue.Enqueue(hn, nc);
            }
            for (int d = 0; d < 8; d++) {
                int nx = cx + DX[d], ny = cy + DY[d];
                int nz = open(nx, ny, cz); if (nz == int.MinValue || (back && open(cx, cy, nz) != cz)) continue;
                bool diag = DX[d] != 0 && DY[d] != 0;
                if (diag && (open(cx + DX[d], cy, cz) == int.MinValue || open(cx, cy + DY[d], cz) == int.MinValue)) continue;
                long hn = (State(nx, ny, nz) << 4) | (long)d; int nc = hcost[hc] + (diag ? DiagonalCost : StraightCost) + (head != 8 && head != d ? TurnCost : 0), was;
                if (done.Contains(hn) || (hcost.TryGetValue(hn, out was) && nc >= was)) continue;
                hcost[hn] = nc; hfrom[hn] = (hc << 4) | (long)d; queue.Enqueue(hn, nc);
            }
        }
        return -1;
    }
    // The directions from a start to the state Walks reached. A teleporter on the way ends them on its tile: the server
    // moves the player from there, and the walk plans again from the landing.
    static System.Collections.Generic.List<int> Route(System.Collections.Generic.Dictionary<long, long> from, long end) {
        var dirs = new System.Collections.Generic.List<int>();
        for (long c = end; from[c] != -1; ) { long f = from[c]; if (f <= -2) { dirs.Clear(); c = -2 - f; continue; } dirs.Add((int)(f & 7)); c = f >> 3; }
        dirs.Reverse(); return dirs;
    }
    // The server's hidden teleporters (Jhelom's islands are joined by them, Damian 2026-10-17; the walk to Kimmie on the
    // south island had "no way"): learned from AssistState.Jumps, one way each, tile stepped onto to landing, kept in
    // teleporters-<server>.txt ("x,y,z>x,y,z").
    readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int[]>> teleporters = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int[]>>(); bool teleportersLoaded;
    string TeleportersPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "teleporters-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    static long TileKey(int x, int y) { return ((long)x << 16) | (uint)y; }
    void AddTeleporter(int fx, int fy, int fz, int tx, int ty, int tz) {
        System.Collections.Generic.List<int[]> l; long key = TileKey(fx, fy);
        if (!teleporters.TryGetValue(key, out l)) teleporters[key] = l = new System.Collections.Generic.List<int[]>();
        if (!l.Exists(e => OnFloor(e[0], fz) && e[1] == tx && e[2] == ty)) l.Add(new[] { fz, tx, ty, tz });
    }
    // The teleporters known, the new jumps the session saw added first (and written to the file).
    System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int[]>> Teleporters(GameLink k) {
        lock (teleporters) {
            if (!teleportersLoaded) { teleportersLoaded = true; try { if (System.IO.File.Exists(TeleportersPath)) foreach (var line in System.IO.File.ReadAllLines(TeleportersPath)) { var ends = line.Split('>'); if (ends.Length != 2) continue; var a = ends[0].Split(','); var b = ends[1].Split(','); int fx, fy, fz, tx, ty, tz; if (a.Length == 3 && b.Length == 3 && int.TryParse(a[0], out fx) && int.TryParse(a[1], out fy) && int.TryParse(a[2], out fz) && int.TryParse(b[0], out tx) && int.TryParse(b[1], out ty) && int.TryParse(b[2], out tz)) AddTeleporter(fx, fy, fz, tx, ty, tz); } } catch { } }
            var fresh = new System.Collections.Generic.List<int[]>(); if (k != null) lock (k.State.Gate) { fresh.AddRange(k.State.Jumps); k.State.Jumps.Clear(); }
            foreach (var j in fresh) {
                AddTeleporter(j[0], j[1], j[2], j[3], j[4], j[5]);
                try { System.IO.File.AppendAllText(TeleportersPath, j[0] + "," + j[1] + "," + j[2] + ">" + j[3] + "," + j[4] + "," + j[5] + System.Environment.NewLine); } catch { }
                Note("Walk: learned a teleporter, " + j[0] + "," + j[1] + " z " + j[2] + " to " + j[3] + "," + j[4] + " z " + j[5]);
            }
            return new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int[]>>(teleporters);
        }
    }
    // A box with no way is widened, PlanMargin doubled up to PlanWiden times: Vesper's canal crossing from the east
    // provisioner (2979,640) to the bank (2896,677) runs north to y 606, ten tiles past the first box (Damian's walk,
    // 2026-10-18).
    public const int PlanWiden = 2;
    System.Collections.Generic.List<int> PlanPath(GameLink k, int sx, int sy, int pz, int tx, int ty, int reach, System.Collections.Generic.HashSet<long> refused, bool sight = false, bool shove = false, int tz = AnyFloor) {
        for (int widen = 0, m = PlanMargin; widen <= PlanWiden; widen++, m *= 2) {
            int x0 = System.Math.Min(sx, tx) - m, y0 = System.Math.Min(sy, ty) - m, w = System.Math.Abs(sx - tx) + 2 * m + 1, h = System.Math.Abs(sy - ty) + 2 * m + 1;
            var cost = new System.Collections.Generic.Dictionary<long, int>(); var from = new System.Collections.Generic.Dictionary<long, long>();
            long goal = Walks(k, new[] { State(sx, sy, pz) }, x0, y0, w, h, refused, shove, (cx, cy, cz) => Tiles(cx, cy, tx, ty) <= reach && OnFloor(cz, tz) && (!sight || Sight(k, cx, cy, cz, tx, ty)), cost, from);
            if (goal >= 0) return Route(from, goal);
        }
        return null;
    }
    // Teleport (spell 22, circle 3) where no walk reaches (Damian, 2026-10-17: "this is the whole point of the teleport
    // spell ... to access otherwise unwalkable places"; the Magincia mage shop's upper floors have no stairs). The 1998
    // rules (teleportbase.m): within 11 tiles ("That location is too far away"), seen ("Target cannot be seen."), a
    // standing spot from the targeted z to z + 8, no one there, no house or boat part within 5. TeleportMana is UO's
    // circle 3 cost (Recall's 11 for circle 4 was measured); TeleportCost weighs a cast as 30 steps against walking.
    public const int TeleporterWaitMs = 2000;
    public const int TeleportSpell = 22, TeleportMana = 9, TeleportRange = 11, TeleportTries = 3, TeleportHops = 3, TeleportCost = 30 * StraightCost, TeleportMoveMs = 4000;
    static readonly int[] TeleportReagents = { 0xF7B, 0xF86 };  // bloodmoss, mandrake root
    static readonly string[] TeleportRefusals = { "too far away", "cannot be seen", "Cannot teleport to that spot", "Someone is standing there", "can't teleport there", "too encumbered" };
    // Every cast the server refused, cast tile to landing ("x,y,z>x,y,z"), kept in teleport-refused-<server>.txt: a
    // refused pair is never cast again (L-FUTILITY); another pair is new circumstances.
    readonly System.Collections.Generic.HashSet<string> teleportRefused = new System.Collections.Generic.HashSet<string>(); bool teleportRefusedLoaded;
    string TeleportRefusedPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "teleport-refused-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    static string Pair(long r, long l) { return StateX(r) + "," + StateY(r) + "," + StateZ(r) + ">" + StateX(l) + "," + StateY(l) + "," + StateZ(l); }
    bool TeleportRefused(string pair) { lock (teleportRefused) { if (!teleportRefusedLoaded) { teleportRefusedLoaded = true; try { if (System.IO.File.Exists(TeleportRefusedPath)) foreach (var l in System.IO.File.ReadAllLines(TeleportRefusedPath)) teleportRefused.Add(l.Trim()); } catch { } } return teleportRefused.Contains(pair); } }
    void RefuseTeleport(string pair) { TeleportRefused(pair); lock (teleportRefused) { if (!teleportRefused.Add(pair)) return; try { System.IO.File.AppendAllText(TeleportRefusedPath, pair + System.Environment.NewLine); } catch { } } }
    // The teleport-only spots near the player, for the map (Actions, Movement; Damian, 2026-10-17: "flag unwalkable
    // teleport spots, those spots like the ones on the roof there of the magincia mage shop", "unwalkable from where you
    // stand, at least by some reasonable heuristic"): every standing spot within TeleportRange, on land or on a surface,
    // that a Teleport can aim at and no walk from the player reaches inside the plan box (PlanMargin past the range).
    // Worked out off the window's thread when the player has moved, and kept until the next move.
    System.Collections.Generic.List<int[]> teleportOnly = new System.Collections.Generic.List<int[]>(); long teleportOnlyAt = -1; volatile bool teleportOnlyBusy;
    public System.Collections.Generic.List<int[]> TeleportOnlySpots() {
        var k = link; if (k == null || k.Closed) return new System.Collections.Generic.List<int[]>();
        int px, py, pz; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; }
        long at = State(px, py, pz);
        if (at != teleportOnlyAt && !teleportOnlyBusy) {
            teleportOnlyBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { var l = TeleportOnlyAt(k, px, py, pz); teleportOnly = l; teleportOnlyAt = at; } catch { } finally { teleportOnlyBusy = false; } });
        }
        return teleportOnly;
    }
    System.Collections.Generic.List<int[]> TeleportOnlyAt(GameLink k, int px, int py, int pz) {
        int m = PlanMargin + TeleportRange, x0 = px - m, y0 = py - m, w = 2 * m + 1;
        var cost = new System.Collections.Generic.Dictionary<long, int>(); var from = new System.Collections.Generic.Dictionary<long, long>();
        Walks(k, new[] { State(px, py, pz) }, x0, y0, w, w, new System.Collections.Generic.HashSet<long>(), false, null, cost, from);
        var spots = new System.Collections.Generic.List<int[]>(); var seen = new System.Collections.Generic.Dictionary<long, int>(); var items = UoTiles.Items();
        // The heights the walks reached on each tile: a spot is walked to when any of them is on its floor, as a dip in the
        // land is reached a few z off its corner average (Damian, 2026-10-17: "a weird little divet in the map ... i can
        // walk there").
        var reached = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
        foreach (var s in cost.Keys) { long t = TileKey(StateX(s), StateY(s)); System.Collections.Generic.List<int> l; if (!reached.TryGetValue(t, out l)) reached[t] = l = new System.Collections.Generic.List<int>(); l.Add(StateZ(s)); }
        for (int x = px - TeleportRange; x <= px + TeleportRange; x++) for (int y = py - TeleportRange; y <= py + TeleportRange; y++) {
            // Each floor on the tile: the land, and the top of every surface; a floor counts where a walker stands on it.
            var floors = new System.Collections.Generic.HashSet<int>(); if (!UoMap.LandBlocks(x, y)) floors.Add(UoMap.LandStandZ(x, y));
            foreach (var s in UoMap.StaticsAt(x, y)) { if (s[0] < 0 || s[0] >= items.Count) continue; var t = items[s[0]]; if ((t.Flags & 0x200) != 0 && (t.Flags & 0x40) == 0) floors.Add(s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height)); }
            foreach (int f in floors) {
                int z = Open(k, x, y, f, seen); if (z != f || TeleportAim(x, y, z) == null) continue;
                // Walked to when this tile or one beside it was reached on this floor: a hollow entered only diagonally past
                // two blocked sides, which the planner does not step, is walked to on uo1998 (2659,459 at z 0 beside 2660,460 at
                // 15, Damian, 2026-10-17: "another here").
                bool walked = false;
                for (int dx = -1; dx <= 1 && !walked; dx++) for (int dy = -1; dy <= 1 && !walked; dy++) { System.Collections.Generic.List<int> got; walked = reached.TryGetValue(TileKey(x + dx, y + dy), out got) && got.Exists(r => OnFloor(r, z)); }
                if (walked) continue;
                spots.Add(new[] { x, y, z });
            }
        }
        return spots;
    }
    // Where a cast at a landing state aims: a surface static whose top is the landing height (its graphic and z, as the
    // client targets the roof tile, 0x0569 at z 60), else the land there (graphic 0); null when neither stands there.
    static int[] TeleportAim(int x, int y, int z) {
        var items = UoTiles.Items();
        foreach (var s in UoMap.StaticsAt(x, y)) { if (s[0] < 0 || s[0] >= items.Count) continue; var t = items[s[0]]; if ((t.Flags & 0x200) == 0 || (t.Flags & 0x40) != 0) continue; if (s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height) == z) return new[] { s[0], s[1] }; }
        return UoMap.LandStandZ(x, y) == z && !UoMap.LandBlocks(x, y) ? new[] { 0, z } : null;
    }
    // One cast where no walk reaches the target: the cast state among those walked to from the start, the landing among
    // those the target is walked to from, at most TeleportRange apart, the cheapest walk to the cast plus the walk on
    // from the landing, no pair refused before; on one floor the landing must be in sight. Null when none.
    sealed class Hop { public long Cast, Land; public int Graphic, AimZ; public System.Collections.Generic.List<int> Walk; }
    Hop TeleportHop(GameLink k, int sx, int sy, int sz, int tx, int ty, int tz, int reach, bool sight, System.Collections.Generic.HashSet<long> refused) {
        if (tz == AnyFloor) return null;
        int m = PlanMargin + TeleportRange, x0 = System.Math.Min(sx, tx) - m, y0 = System.Math.Min(sy, ty) - m, w = System.Math.Abs(sx - tx) + 2 * m + 1, h = System.Math.Abs(sy - ty) + 2 * m + 1;
        var costR = new System.Collections.Generic.Dictionary<long, int>(); var fromR = new System.Collections.Generic.Dictionary<long, long>();
        Walks(k, new[] { State(sx, sy, sz) }, x0, y0, w, h, refused, false, null, costR, fromR);
        // The target's floor: every tile within reach where a walker on that floor stands in sight of it.
        var goals = new System.Collections.Generic.List<long>(); var seen = new System.Collections.Generic.Dictionary<long, int>();
        for (int x = tx - reach; x <= tx + reach; x++) for (int y = ty - reach; y <= ty + reach; y++) {
            int z = Open(k, x, y, tz, seen); if (z == int.MinValue || !OnFloor(z, tz) || (sight && !Sight(k, x, y, z, tx, ty))) continue; goals.Add(State(x, y, z));
        }
        if (goals.Count == 0) return null;
        var costC = new System.Collections.Generic.Dictionary<long, int>(); var fromC = new System.Collections.Generic.Dictionary<long, long>();
        Walks(k, goals, x0, y0, w, h, refused, false, null, costC, fromC, true);
        var byTile = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<long>>();
        foreach (var r in costR.Keys) { long t = ((long)StateX(r) << 16) | (uint)StateY(r); System.Collections.Generic.List<long> l; if (!byTile.TryGetValue(t, out l)) byTile[t] = l = new System.Collections.Generic.List<long>(); l.Add(r); }
        Hop best = null; int bestCost = int.MaxValue;
        // Landings cheapest first, so the scan stops once no landing can beat the best pair found.
        var lands = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<long, int>>(costC); lands.Sort((a, b) => a.Value.CompareTo(b.Value));
        foreach (var kv in lands) {
            long land = kv.Key; int lx = StateX(land), ly = StateY(land), lz = StateZ(land);
            if (kv.Value + TeleportCost >= bestCost) break;
            if (costR.ContainsKey(land) || Occupied(k, lx, ly)) continue;
            int[] aim = TeleportAim(lx, ly, lz); if (aim == null) continue;
            for (int x = lx - TeleportRange; x <= lx + TeleportRange; x++) for (int y = ly - TeleportRange; y <= ly + TeleportRange; y++) {
                System.Collections.Generic.List<long> casts; if (!byTile.TryGetValue(((long)x << 16) | (uint)y, out casts)) continue;
                foreach (var r in casts) {
                    int c = costR[r] + kv.Value + TeleportCost; if (c >= bestCost) continue;
                    if (OnFloor(StateZ(r), lz) && !Sight(k, x, y, StateZ(r), lx, ly)) continue;
                    if (TeleportRefused(Pair(r, land))) continue;
                    best = new Hop { Cast = r, Land = land, Graphic = aim[0], AimZ = aim[1] }; bestCost = c;
                }
            }
        }
        if (best != null) best.Walk = Route(fromR, best.Cast);
        return best;
    }
    // Casts Teleport at the hop's landing: the reagents and mana first, again only after a fizzle; null once the server
    // has put the player on the landing, else the server's refusal (the pair is then never cast again).
    string Teleport(GameLink k, Hop hop) {
        int lx = StateX(hop.Land), ly = StateY(hop.Land); string pair = Pair(hop.Cast, hop.Land);
        for (int attempt = 1; attempt <= TeleportTries; attempt++) {
            if (circuitStop) return "stopped";
            OpenPackBags(k);
            foreach (int r in TeleportReagents) if (PackCount(k, g => g == r) == 0) return "no " + UoTiles.Name(r) + " in the pack for Teleport";
            var manaBy = System.DateTime.UtcNow.AddMilliseconds(ManaWaitMs);
            while (true) { int mana; lock (k.State.Gate) mana = k.State.Mana; if (mana >= TeleportMana) break; if (System.DateTime.UtcNow > manaBy || k.Closed) return "mana stayed under " + TeleportMana; System.Threading.Thread.Sleep(500); }
            long mark;
            lock (actionGate) {
                Answered(k, AnswerMs); mark = k.State.JournalSeq;
                string why = k.Cast(TeleportSpell, true); if (why != null) return why;
                var until = System.DateTime.UtcNow.AddMilliseconds(CastCursorMs);
                while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed && !k.State.JournalSince(mark).Exists(j => System.Array.Exists(CastEnds, e => j.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0))) System.Threading.Thread.Sleep(20);
                if (k.MacroCursor) { why = k.Target(true, 0, lx, ly, hop.AimZ, hop.Graphic); if (why != null) return "aiming the Teleport: " + why; }
            }
            var moveBy = System.DateTime.UtcNow.AddMilliseconds(TeleportMoveMs);
            while (System.DateTime.UtcNow < moveBy && !k.Closed) {
                int x, y, z; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; z = k.State.Z; }
                if (Tiles(x, y, lx, ly) <= 1 && OnFloor(z, StateZ(hop.Land))) { Note("Walk: teleported to " + x + "," + y + " z " + z); return null; }
                var heard0 = k.State.JournalSince(mark);
                if (heard0.Exists(j => j.IndexOf("fizzle", System.StringComparison.OrdinalIgnoreCase) >= 0) || heard0.Exists(j => System.Array.Exists(TeleportRefusals, e => j.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0))) break;
                System.Threading.Thread.Sleep(100);
            }
            var heard = k.State.JournalSince(mark); string refusal = heard.Find(j => System.Array.Exists(TeleportRefusals, e => j.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (refusal != null) { RefuseTeleport(pair); return "the server refused the Teleport " + pair + ": \"" + refusal + "\""; }
            // L-FUTILITY: cast again only after a fizzle or "You must wait"; any other answer, or none, ends it.
            if (!heard.Exists(j => j.IndexOf("fizzle", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("You must wait", System.StringComparison.OrdinalIgnoreCase) >= 0)) { RefuseTeleport(pair); string said = null; foreach (var j in heard) said = j; return "the Teleport " + pair + " did not move you" + (said != null ? " (\"" + said + "\")" : ""); }
            System.Threading.Thread.Sleep(BusyMs);
        }
        return "the Teleport fizzled " + TeleportTries + " times";
    }
    // To where someone at x, y hears the player speak: within VendorReach (a vendor hears its name from 3 tiles) and in
    // sight, so not on the far side of a wall (Damian, 2026-10-17: "walkto should probably be walktospeakingrange").
    // A ServUO vendor hears by range alone (BaseVendor.cs:2497), so a tile in sight is preferred, not required: with none
    // reachable (Jannelle at 3761,1267 behind her shop's walls, 2026-10-17 09:07:04) any tile within reach does.
    // In sight is required: Slade, 3 tiles off behind his shop's closed door, never answered "Slade buy" (2026-10-17).
    public string WalkToSpeakingRange(int x, int y, int z = AnyFloor) { return WalkTo(x, y, VendorReach, true, z); }
    // sight: arrived only where the target is in sight (Sight), for a vendor or a loom; a banker hears through walls.
    // teleport: a Teleport where no walk reaches (TeleportHop); off for the walk to a cast tile.
    public string WalkTo(int tx, int ty, int reach, bool sight = false, int tz = AnyFloor, bool teleport = true) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        var blocked = new System.Collections.Generic.HashSet<long>(); var opened = new System.Collections.Generic.HashSet<uint>(); int[] clearOf = null;
        int steps = 0, refusals = 0, hops = 0;
        System.Collections.Generic.List<int> path = null; int followed = 0, pace = StepPace(k);
        // Steps go out as the client sends its own run: StepsAhead in flight before their answers, one every pace (the
        // client's mounted run, sniffed 2026-10-17: 25 steps in 2.8 s, a median of 94 ms apart, 2 outstanding at most;
        // waiting for each answer ran at 140 ms). The route is planned from where the steps in flight will put the
        // player; a refusal names the first unanswered step, and the rest are let settle before the walk goes on.
        var flight = new System.Collections.Generic.Queue<int[]>(); int acksSeen = k.StepAcks, refusalsSeen = k.StepRefusals;
        // The player's own step ends the walk (Damian, 2026-10-17: "you were controlling movement and the guy was coming
        // on and i couldn't get us outta harms way").
        int playerAcks = k.ClientStepAcks, playerRefusals = k.ClientStepRefusals;
        // Run or walk is decided again before every step (RunNow); runPace is the mount's or the foot's run.
        int runPace = pace, tiredRests = 0;
        int x = 0, y = 0, z = 0, f = 0, run = 0; var nextAt = System.DateTime.UtcNow; long journalMark = k.State.JournalSeq, warnMark = k.State.JournalSeq;
        try {
            while (!circuitStop && !k.Closed) {
                // The player's own step stops the circuit as well as the walk: a trip that read it as a failed walk recalled
                // by a pack rune after Damian took over (2026-10-17 23:13 and 01:12).
                if (!clientWalks && (k.ClientStepAcks != playerAcks || k.ClientStepRefusals != playerRefusals)) { CircuitHalt(TookOver); return TookOver; }
                for (int a = k.StepAcks; acksSeen < a; acksSeen++) if (flight.Count > 0) flight.Dequeue();
                if (k.StepRefusals != refusalsSeen) {
                    var bad = flight.Count > 0 ? flight.Peek() : null; flight.Clear();
                    System.Threading.Thread.Sleep(StepSettleMs); refusalsSeen = k.StepRefusals; acksSeen = k.StepAcks; path = null;
                    // A tired mount or player is refused every step, not a wall (ServUO WeightOverloading: "Your mount is
                    // too fatigued to move." at 0 stamina): rest and go on, the tile not marked blocked.
                    bool tired = k.State.JournalSince(journalMark).Exists(j => j.IndexOf("too fatigued", System.StringComparison.OrdinalIgnoreCase) >= 0);
                    journalMark = k.State.JournalSeq;
                    // Then walked until the estimate says rested (Damian, 2026-10-17: "when the horse is exhausted, rest a
                    // bit, then walk"); refused again for fatigue, the rest is longer ("if walking is still a problem, rest
                    // a bit longer").
                    if (tired) { int rest = System.Math.Min(FatigueRestMaxMs, FatigueRestMs << System.Math.Min(4, tiredRests++)); CircuitStatus = "Resting: too fatigued to move"; Note("Walk: too fatigued to move; resting " + rest / 1000 + " s, then walking"); System.Threading.Thread.Sleep(rest); continue; }
                    if (bad != null && bad[3] == 0) {
                        int bx = bad[0] + DX[bad[2]], by = bad[1] + DY[bad[2]];
                        if (++refusals > WalkRefusalsMax) return "refused " + refusals + " steps on the way";
                        // A door beside the refused step: opened, and the step tried again. The door on the refused tile
                        // first, then the nearest the player: Vesper's double door at 2923-2924,671 had its far leaf used
                        // ("I can't reach that.") and the near one 0.8 s later ("You must wait to perform another
                        // action."), and neither opened (2026-10-18 12:45:20). A use answered "You must wait" is used
                        // again once after the busy time, the answer asking for it.
                        AssistItem door = null;
                        lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (!opened.Contains(g.Serial) && Tiles(g.X, g.Y, bx, by) <= 1 && IsDoor(g.Graphic) && (door == null || Tiles(g.X, g.Y, bx, by) < Tiles(door.X, door.Y, bx, by) || (Tiles(g.X, g.Y, bx, by) == Tiles(door.X, door.Y, bx, by) && Tiles(g.X, g.Y, k.State.X, k.State.Y) < Tiles(door.X, door.Y, k.State.X, k.State.Y)))) door = g;
                        if (door != null) {
                            // Opened, and seen open: the door's art or tile changes when it swings. Not open yet (a use the
                            // server answered "You must wait", or a delay), it is used again after DoorBackoffMs, doubling,
                            // DoorTries in all (Damian, 2026-10-18: "pathfinding through a closed doorway should open it,
                            // check its open, if not open repeat with backoff delay iteration").
                            opened.Add(door.Serial); bool swung = false; int g0 = door.Graphic, x0d = door.X, y0d = door.Y;
                            for (int dt = 0; dt < DoorTries && !swung && !circuitStop; dt++) {
                                if (dt > 0) System.Threading.Thread.Sleep(DoorBackoffMs << (dt - 1));
                                BusyWait(clickedAt);
                                lock (actionGate) { Answered(k, AnswerMs); k.Use(door.Serial, false); clickedAt = System.DateTime.UtcNow; }
                                var dby = System.DateTime.UtcNow.AddMilliseconds(DoorOpenMs);
                                while (!swung && System.DateTime.UtcNow < dby) { AssistItem now; lock (k.State.Gate) k.State.Ground.TryGetValue(door.Serial, out now); swung = now != null && (now.Graphic != g0 || now.X != x0d || now.Y != y0d); if (!swung) System.Threading.Thread.Sleep(50); }
                            }
                            Note("Walk: " + (swung ? "opened" : "could not open") + " the " + UoTiles.Name(door.Graphic) + " at " + door.X + "," + door.Y);
                            if (!swung) blocked.Add(((long)bx << 16) | (uint)by); else System.Threading.Thread.Sleep(StepMs);
                        } else blocked.Add(((long)bx << 16) | (uint)by);
                    }
                }
                // The 10% warning stops the running at once (Damian, 2026-10-17: "when we get the 10% horse exhaustion
                // warning you stop the running immediately, rest for 5 seconds, then start walking until the horse is
                // rested ... usually due to a work step where you just sit there").
                if (run != 0 && k.State.JournalSince(warnMark).Exists(j => j.IndexOf("very fatigued", System.StringComparison.OrdinalIgnoreCase) >= 0)) {
                    warnMark = k.State.JournalSeq;
                    var settle = System.DateTime.UtcNow.AddMilliseconds(StepAnswerMs);
                    while (k.StepAcks - acksSeen < flight.Count && k.StepRefusals == refusalsSeen && System.DateTime.UtcNow < settle) System.Threading.Thread.Sleep(10);
                    for (int a = k.StepAcks; acksSeen < a; acksSeen++) if (flight.Count > 0) flight.Dequeue();
                    CircuitStatus = "Resting: the horse is very fatigued"; Note("Walk: the horse is very fatigued; resting " + WarnRestMs / 1000 + " s, then walking until it is rested");
                    System.Threading.Thread.Sleep(WarnRestMs); path = null; continue;
                }
                if (flight.Count == 0) lock (k.State.Gate) { x = k.State.X; y = k.State.Y; z = k.State.Z; f = k.State.Facing; }
                bool there = Tiles(x, y, tx, ty) <= reach && OnFloor(z, tz) && (!sight || Sight(k, x, y, z, tx, ty));
                if (there && flight.Count == 0) return null;
                if (flight.Count > 0) {
                    var oldest = flight.Peek(); if ((System.DateTime.UtcNow - new System.DateTime(((long)oldest[4] << 32) | (uint)oldest[5], System.DateTimeKind.Utc)).TotalMilliseconds > StepAnswerMs) return "the server did not answer a step";
                }
                if (there || flight.Count >= StepsAhead || System.DateTime.UtcNow < nextAt) { System.Threading.Thread.Sleep(5); continue; }
                if (steps >= WalkStepsMax) return "still " + Tiles(x, y, tx, ty) + " tiles away after " + steps + " steps";
                // Through a doorway the walk keeps to its route until DoorClear tiles past the door, then plans afresh
                // (Damian, 2026-10-18: "building egress through a doorway clears the door by 2 tiles before finding path
                // to target").
                if (clearOf == null) lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (IsDoor(g.Graphic) && Tiles(g.X, g.Y, x, y) <= 1) { clearOf = new[] { g.X, g.Y }; break; }
                bool nearDoor = clearOf != null && Tiles(x, y, clearOf[0], clearOf[1]) < DoorClear;
                if (clearOf != null && !nearDoor) { clearOf = null; path = null; }
                if (path != null && path.Count > 0 && ((followed >= ReplanSteps && !nearDoor) || Occupied(k, x + DX[path[0]], y + DY[path[0]]))) path = null;
                if (path == null || path.Count == 0) {
                    if (flight.Count > 0) { System.Threading.Thread.Sleep(5); continue; }
                    path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, false, tz); followed = 0;
                    // An empty route short of the target: the player stands on a teleporter the server has yet to act on.
                    if (path != null && path.Count == 0) {
                        var moveBy = System.DateTime.UtcNow.AddMilliseconds(TeleporterWaitMs); int mx = x, my = y;
                        while (System.DateTime.UtcNow < moveBy && !k.Closed) { lock (k.State.Gate) { mx = k.State.X; my = k.State.Y; } if (mx != x || my != y) break; System.Threading.Thread.Sleep(50); }
                        if (mx == x && my == y) return "the teleporter at " + x + "," + y + " did not move you";
                        path = null; continue;
                    }
                    // No way round the people in it: at full stamina a step through one is taken, as a player shoves
                    // through a pinch point (Damian, 2026-10-17: "it can push through if it has full stamina ... sometimes
                    // just walking through is best"; ServUO refuses the shove below full stamina).
                    if (path == null || path.Count == 0) {
                        int stam, maxStam; lock (k.State.Gate) { stam = k.State.Stam; maxStam = k.State.MaxStam; }
                        if (maxStam > 0 && stam >= maxStam) { path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, true, tz); if (path != null && path.Count > 0) Note("Walk: no way round the people here; pushing through at full stamina"); }
                    }
                    // No walk: one Teleport from a tile walked to, onto a tile the target is walked to from (TeleportHop),
                    // at most TeleportHops a walk. A refused cast ends the walk (L-FUTILITY): the next pair was the same
                    // ask from the tile beside it, refused "Target cannot be seen." three times running (Vesper,
                    // 2026-10-18 12:45:24-30).
                    if ((path == null || path.Count == 0) && teleport && hops < TeleportHops) {
                        if (flight.Count > 0) { System.Threading.Thread.Sleep(5); continue; }
                        var hop = TeleportHop(k, x, y, z, tx, ty, tz, reach, sight, blocked);
                        if (hop != null) {
                            hops++; Note("Walk: no walk reaches " + tx + "," + ty + " z " + tz + "; teleporting " + Pair(hop.Cast, hop.Land));
                            string cw = hop.Walk.Count == 0 ? null : WalkTo(StateX(hop.Cast), StateY(hop.Cast), 0, false, StateZ(hop.Cast), false);
                            string tw = cw ?? Teleport(k, hop);
                            if (tw != null) Note("Walk: " + (cw != null ? "walking to the cast tile: " : "") + tw);
                            if (cw == null && tw != null) return "no way from " + x + "," + y + " z " + z + " to " + tx + "," + ty + ": " + tw;
                            acksSeen = k.StepAcks; refusalsSeen = k.StepRefusals; path = null; continue;
                        }
                    }
                    if (path == null || path.Count == 0) return "no way from " + x + "," + y + " z " + z + " to within " + reach + " of " + tx + "," + ty + (tz != AnyFloor ? " z " + tz : "");
                }
                int pick = path[0]; bool turn = f != pick; long sentAt = System.DateTime.UtcNow.Ticks;
                bool running = RunNow(k); run = running ? RunFlag : 0; pace = running ? runPace : runPace == RunMountMs ? WalkMountMs : StepMs;
                string why; lock (actionGate) why = k.Step(pick | run);
                if (why != null) return "stepping: " + why;
                // The assist's own steps tire the mount as the player's do; the estimate sees only the client's otherwise.
                MountStep(k, new byte[] { 0x02, (byte)(pick | run), 0 });
                flight.Enqueue(new[] { x, y, pick, turn ? 1 : 0, (int)(sentAt >> 32), (int)sentAt });
                nextAt = System.DateTime.UtcNow.AddMilliseconds(pace);
                // A step in a new direction only turns the player; a move puts it on the next tile at that tile's height.
                if (turn) f = pick;
                else {
                    x += DX[pick]; y += DY[pick]; int nz = UoMap.StandZ(x, y, z); if (nz != int.MinValue) z = nz;
                    path.RemoveAt(0); followed++; steps++; if ((steps & 7) == 0) NoteLooms(k);
                }
            }
            return circuitStop ? "stopped" : "the connection closed";
        } finally { if (!clientWalks) k.ShowPlayer(true); }
    }

    // Away from every orc and lizardman in view, running, chosen again on every step (Damian, 2026-10-17: "pick a
    // direction away or at least right angle, and run", "zig zag your way out", "paying attention, every second"): of the
    // open tiles straight away, 45 and 90 degrees off it (all eight when those are closed), the one farthest from the nearest threat, until none is within
    // FleeClear, FleeSteps are run, or the player steps. Orc 0x07 0x11 0x8A, lizardman 0x21 0x23 0x24.
    public static readonly int[] ThreatBodies = { 0x07, 0x11, 0x8A, 0x21, 0x23, 0x24 };
    public const int FleeClear = 14, FleeSteps = 150;
    // Every orc and lizardman in view: x, y, distance.
    System.Collections.Generic.List<int[]> Threats(GameLink k) {
        var all = new System.Collections.Generic.List<int[]>();
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) {
            int b; if (!k.State.Bodies.TryGetValue(kv.Key, out b) || System.Array.IndexOf(ThreatBodies, b) < 0) continue;
            all.Add(new[] { kv.Value[0], kv.Value[1], Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) });
        }
        return all;
    }
    static int Nearest(System.Collections.Generic.List<int[]> all) { int d = int.MaxValue; foreach (var t in all) d = System.Math.Min(d, t[2]); return d; }
    public string Flee() {
        var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        int playerAcks = k.ClientStepAcks, playerRefusals = k.ClientStepRefusals, pace = StepPace(k);
        var refused = new System.Collections.Generic.HashSet<long>();
        try {
            for (int step = 0; step < FleeSteps; step++) {
                if (circuitStop) return "stopped";
                if (k.Closed) return "the connection closed";
                if (k.ClientStepAcks != playerAcks || k.ClientStepRefusals != playerRefusals) { CircuitHalt(TookOver); return TookOver; }
                var all = Threats(k); if (all.Count == 0 || Nearest(all) > FleeClear) { Note("Flee: clear after " + step + " steps"); return null; }
                int px, py, pz, f; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; f = k.State.Facing; }
                // Away from all of them, the nearer weighing more (Damian: "because you just might run into another").
                double vx = 0, vy = 0; foreach (var t in all) { double dd = System.Math.Max(1, t[2]); vx += (px - t[0]) / (dd * dd); vy += (py - t[1]) / (dd * dd); }
                int away = DirTo((int)System.Math.Round(vx * 1000), (int)System.Math.Round(vy * 1000));
                var seen = new System.Collections.Generic.Dictionary<long, int>(); int best = -1, bestScore = int.MinValue;
                // Boxed in, past them (Damian, 2026-10-17: "boxed in requires run past them"): 135 and 180 degrees only
                // when no tile from away to 90 degrees off is open.
                foreach (int turn in new[] { 0, 1, 7, 2, 6, -1, 3, 5, 4 }) {
                    if (turn < 0) { if (best >= 0) break; continue; }
                    int d = (away + turn) & 7, nx = px + DX[d], ny = py + DY[d];
                    if (refused.Contains(((long)nx << 16) | (uint)ny) || Open(k, nx, ny, pz, seen) == int.MinValue) continue;
                    if (DX[d] != 0 && DY[d] != 0 && (Open(k, px + DX[d], py, pz, seen) == int.MinValue || Open(k, px, py + DY[d], pz, seen) == int.MinValue)) continue;
                    int m = int.MaxValue; foreach (var t in all) m = System.Math.Min(m, Tiles(nx, ny, t[0], t[1]));
                    // Farther from the nearest threat first; of equals, no turn (a turn is a step that does not move).
                    int score = m * 4 - (d != f ? 1 : 0); if (score > bestScore) { bestScore = score; best = d; }
                }
                // Never a stand in the corner (Damian, 2026-10-17: "sitting in the corner waiting to die is no solution"):
                // the refusals forgotten, the tiles read again.
                if (best < 0) { refused.Clear(); System.Threading.Thread.Sleep(200); continue; }
                int r; lock (actionGate) r = StepOnce(k, best | RunFlag);
                if (r == 0) { refused.Add(((long)(px + DX[best]) << 16) | (uint)(py + DY[best])); System.Threading.Thread.Sleep(StepSettleMs); continue; }
                if (r < 0) return "the server did not answer a step";
                System.Threading.Thread.Sleep(pace);
            }
            var left = Threats(k); return left.Count == 0 || Nearest(left) > FleeClear ? null : "still within " + Nearest(left) + " tiles after " + FleeSteps + " steps";
        } finally { if (!clientWalks) k.ShowPlayer(true); }
    }

    // ---------- the tailoring circuit (assist-plan.md stage 6, step 4) ----------
    // No round zero (Damian, 2026-10-17: "no round 0 here"): the sewing kit is the pack's, the menus are picked by their
    // text, the cloth is the pack's largest pile and then its bolts.
    // What the circuit sells and makes are settings, changed from the control port with no code change (Damian, 2026-10-17:
    // "lets make this price and even the item a configurable so no code change", "take a list of items and min prices",
    // "sell as low as 23"). circuit.sell: items separated by ";", each "name|arts in hex, comma-separated|lowest price",
    // the name as vendors list it; circuit.item.menu: the sewing menus' picks, separated by "|".
    public const string SellDefault = "fancy shirt|1EFD,1EFE|23";
    public sealed class SellItem { public string Name; public int[] Arts; public int Floor; public override string ToString() { return Name + "|" + string.Join(",", System.Array.ConvertAll(Arts, a => a.ToString("X4"))) + "|" + Floor; } }
    string sellRead; System.Collections.Generic.List<SellItem> sellList = new System.Collections.Generic.List<SellItem>();
    public System.Collections.Generic.List<SellItem> SellList() {
        string t = Cfg.Get("circuit.sell", SellDefault);
        lock (sellList) {
            if (t != sellRead) {
                var l = new System.Collections.Generic.List<SellItem>();
                foreach (var e in t.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) {
                    var f = e.Split('|'); int floor; if (f.Length != 3 || !int.TryParse(f[2].Trim(), out floor)) continue;
                    var arts = new System.Collections.Generic.List<int>(); foreach (var s in f[1].Split(',')) { int v; if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v)) arts.Add(v); }
                    if (arts.Count > 0) l.Add(new SellItem { Name = f[0].Trim(), Arts = arts.ToArray(), Floor = floor });
                }
                sellList = l; sellRead = t;
            }
            return new System.Collections.Generic.List<SellItem>(sellList);
        }
    }
    // Adds the item, or replaces the one of the same name; a price under 0 removes it.
    public string SetSellItem(string name, string arts, int floor) {
        var l = SellList(); l.RemoveAll(i => string.Equals(i.Name, name, System.StringComparison.OrdinalIgnoreCase));
        if (floor >= 0) { var probe = new SellItem { Name = name, Floor = floor, Arts = new int[0] }; var a = new System.Collections.Generic.List<int>(); foreach (var s in arts.Split(',')) { int v; if (!int.TryParse(s.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v)) return "not an art in hex: " + s; a.Add(v); } probe.Arts = a.ToArray(); l.Add(probe); }
        Cfg.Set("circuit.sell", string.Join(";", l.ConvertAll(i => i.ToString()))); return null;
    }
    public string ProductMenu { get { return Cfg.Get("circuit.item.menu", "shirts|Fancy shirt"); } set { Cfg.Set("circuit.item.menu", value); } }
    // The buy circuit's shopping list (Damian, 2026-10-17: "a shopping list of mage supply stuff (reagents, runes,
    // scrolls)"; "we should seek to acquire 250 of them @5gp each"): circuit.buy.list, items separated by ";", each
    // "name|arts in hex, comma-separated|count|highest price".
    public const string BuyDefault = "black pearl|0F7A|250|5";
    public sealed class BuyItem { public string Name; public int[] Arts; public int Count, MaxPrice; public override string ToString() { return Name + "|" + string.Join(",", System.Array.ConvertAll(Arts, a => a.ToString("X4"))) + "|" + Count + "|" + MaxPrice; } }
    public System.Collections.Generic.List<BuyItem> BuyList() {
        var l = new System.Collections.Generic.List<BuyItem>();
        foreach (var e in Cfg.Get("circuit.buy.list", BuyDefault).Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) {
            var f = e.Split('|'); int count, max; if (f.Length != 4 || !int.TryParse(f[2].Trim(), out count) || !int.TryParse(f[3].Trim(), out max)) continue;
            var arts = new System.Collections.Generic.List<int>(); foreach (var s in f[1].Split(',')) { int v; if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v)) arts.Add(v); }
            if (arts.Count > 0) l.Add(new BuyItem { Name = f[0].Trim(), Arts = arts.ToArray(), Count = count, MaxPrice = max });
        }
        return l;
    }
    // Adds the item, or replaces the one of the same name; a count of 0 or less removes it.
    public string SetBuyItem(string name, string arts, int count, int maxPrice) {
        var l = BuyList(); l.RemoveAll(i => string.Equals(i.Name, name, System.StringComparison.OrdinalIgnoreCase));
        if (count > 0) { var a = new System.Collections.Generic.List<int>(); foreach (var s in arts.Split(',')) { int v; if (!int.TryParse(s.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v)) return "not an art in hex: " + s; a.Add(v); } l.Add(new BuyItem { Name = name, Arts = a.ToArray(), Count = count, MaxPrice = System.Math.Max(1, maxPrice) }); }
        Cfg.Set("circuit.buy.list", string.Join(";", l.ConvertAll(i => i.ToString()))); return null;
    }
    // The buy circuit's purse: the gold it carries from each bank, topped up from and banked onto the box's own pile.
    public int BuyPurse { get { return Cfg.GetInt("circuit.buy.purse", 2000); } set { Cfg.Set("circuit.buy.purse", System.Math.Max(0, value).ToString()); } }
    // What the shopping list offers by name: the reagents, a blank scroll, a recall rune.
    public static readonly string[][] BuyPresets = {
        new[] { "black pearl", "0F7A" }, new[] { "bloodmoss", "0F7B" }, new[] { "garlic", "0F84" }, new[] { "ginseng", "0F85" },
        new[] { "mandrake root", "0F86" }, new[] { "nightshade", "0F88" }, new[] { "sulfurous ash", "0F8C" }, new[] { "spiders' silk", "0F8D" },
        new[] { "blank scroll", "0EF3" }, new[] { "recall rune", "1F14" },
    };
    // The lowest price for the art, or -1 when nothing on the list is that art.
    int FloorOf(int g) { foreach (var i in SellList()) if (System.Array.IndexOf(i.Arts, g) >= 0) return i.Floor; return -1; }
    bool IsShirtArt(int g) { return FloorOf(g) >= 0; }
    string FloorText() { return string.Join(", ", SellList().ConvertAll(i => i.Name + " " + i.Floor)); }
    // A vendor whose stock lists anything on the sell list by its whole name ("Skirt: 33 gp x 10"): a shop that deals in
    // it, and so buys it. A part of a name is not the item: an armorer's "leather skirt" took Moonglow's armory for a
    // skirt buyer (2026-10-17).
    bool DealsIn(AtlasNpc n) { var l = SellList(); return n.Items.Exists(s => { int c = s.IndexOf(':'); string item = (c >= 0 ? s.Substring(0, c) : s).Trim(); return l.Exists(i => string.Equals(item, i.Name, System.StringComparison.OrdinalIgnoreCase)); }); }
    System.Collections.Generic.List<AtlasNpc> CityBuyers(string city) { var l = new System.Collections.Generic.List<AtlasNpc>(); if (Atlas == null) return l; foreach (var n in Atlas.Shops()) if (UoRegions.City(n.X, n.Y) == city && DealsIn(n) && TailorVendor(n)) l.Add(n); return l; }
    public const int SewWeightRoom = 10, PackItemsMax = 125, KitArt = 0x0F9D, ThreadWant = 100, CircuitReach = 150, VendorReach = 2, RestockPollMs = 5000;
    static bool IsThreadArt(int g) { return g == 0x0FA0 || g == 0x0FA1; }
    LoopKind TailorKind { get { foreach (var l in Loops) if (l.Tailor > 0) return l; return Loops[0]; } }
    // Sews fancy shirts until the pack holds no cloth or bolts. Null, or why it stopped.
    public string SewAll(out int made, out int failed) {
        made = 0; failed = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        // TailorResult waits on the loops' stop, which Attach leaves set: a sewing outside a loop (the control port, the
        // circuit) clears it, or every shirt after the first reads as stopped.
        ManualStart(); if (loopThread == null || !loopThread.IsAlive) stop = false;
        OpenPackBags(k);
        var spent = new System.Collections.Generic.HashSet<uint>(); var l = TailorKind;
        while (!circuitStop && !k.Closed) {
            // A shirt does not stack, and the pack holds PackItemsMax items counting every bag's contents (ServUO
            // Container.CheckHold: TotalItems + 1 > 125, "That container cannot hold more items."; worn items do not
            // count), so the sewing stops one short of it. The bags are opened first: an unopened bag's items are
            // unknown here, 113 counted against the server's 124 at 08:43:05.
            int held = 0; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container)) held++; }
            if (held >= PackItemsMax - 1) return "the pack holds " + held + " items, " + PackItemsMax + " at most";
            // Past the 1998 carry limit (strength * 4 + 30) every step drains ten times the stamina, so the sewing stops
            // SewWeightRoom short of it: 105 unsold shirts left Kwik at 460 of 394 stones, too fatigued to walk to a bank
            // (Moonglow, 2026-10-18 04:31).
            int str, weight; lock (k.State.Gate) { str = k.State.Str; weight = k.State.Weight; }
            if (str > 0 && weight + SewWeightRoom > str * 4 + 30) return "the pack weighs " + weight + " of " + (str * 4 + 30) + " stones";
            AssistItem kit = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic == KitArt) { kit = it; break; } }
            if (kit == null) return "no sewing kit in the pack";
            AssistItem mat = null;
            lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && IsCloth(it.Graphic) && !spent.Contains(it.Serial) && (mat == null || it.Amount > mat.Amount)) mat = it; }
            bool fromCloth = mat != null;
            if (mat == null) foreach (var b in PackBolts(k)) if (mat == null || b.Amount > mat.Amount) mat = b;
            if (mat == null) return null;
            var due = lastUse.AddMilliseconds(Delay(l));
            while (System.DateTime.UtcNow < due && !circuitStop) System.Threading.Thread.Sleep(50);
            long mark; string w;
            lock (actionGate) {
                var menu = ProductMenu.Split('|');
                Answered(k, AnswerMs); mark = k.State.JournalSeq; lastSewn = menu[menu.Length - 1]; lastSewnAt = System.DateTime.UtcNow;
                w = Sew(k, kit.Serial, mat.Serial, menu.Length, (i, m) => MenuPick(m, menu[i]));
            }
            string res = w == null ? TailorResult(k, l, mark, TailorMs) : w == "no cursor" || w == "no menu" ? (SmeltWait(k, l, mark) ?? w) : null;
            // The server's own limit stops the sewing: the pack's count above misses bags never opened (the server refused
            // at 113 counted, 2026-10-17 08:43:05).
            foreach (var j in k.State.JournalSince(mark)) if (j.IndexOf("cannot hold more items", System.StringComparison.OrdinalIgnoreCase) >= 0) return "the pack is full: \"" + j + "\"";
            if (w != null && res == null) return w;
            if (res == "again") continue;
            if (res == "made" || res == "exceptional") { made++; continue; }
            if (res == "failed") { failed++; continue; }
            if (res == "stopped") return "stopped";
            string said = TailorLast(k, mark);
            if (fromCloth) { if (said != null) DropScraps(k, mat.Serial); spent.Add(mat.Serial); continue; }
            return "the bolts gave " + res;
        }
        return circuitStop ? "stopped" : "the connection closed";
    }

    // Staff, attack and death end the circuit (assist-plan.md stage 6): staff is a name begun "GM ", a title of game
    // master, GM, counselor or councilor, seer or admin, or the GM robe (0x204F) worn; players are ignored.
    static readonly string[] StaffWords = { "game master", "counselor", "councilor", "seer", "admin" };
    public const int GmRobeArt = 0x204F;
    static bool IsStaffName(string name) {
        string n = name.Trim(); if (n.StartsWith("GM ", System.StringComparison.Ordinal) || n == "GM") return true;
        int at = n.LastIndexOf(" the ", System.StringComparison.OrdinalIgnoreCase); string title = at >= 0 ? n.Substring(at + 5).ToLowerInvariant() : "";
        if (title == "gm" || title.StartsWith("gm ")) return true;
        foreach (var w in StaffWords) if (title.Contains(w)) return true;
        return false;
    }
    volatile string circuitAlarm;
    void CircuitWatch(GameLink k, byte[] p) {
        if (circuitThread == null || !circuitThread.IsAlive) return;
        uint me; lock (k.State.Gate) me = k.State.Player;
        if (p[0] == 0x1C && p.Length > 44) {
            uint who = U32(p, 3); if (who == 0xFFFFFFFF || who == me) return;
            string name = System.Text.Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
            if (IsStaffName(name)) Alarm(k, "staff spoke: " + name + ": \"" + System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0') + "\"");
        } else if (p[0] == 0x78 && p.Length >= 19) {
            // An incoming mobile: serial, body, x, y, z, direction, hue, flags, notoriety, then worn items (serial, graphic
            // with 0x8000 when a hue follows, layer, hue) until a zero serial.
            uint who = U32(p, 3); int at = 19;
            while (at + 4 <= p.Length && U32(p, at) != 0) {
                if (at + 7 > p.Length) break; int g = (p[at + 4] << 8) | p[at + 5]; at += 7; if ((g & 0x8000) != 0) at += 2;
                if ((g & 0x7FFF) == GmRobeArt && who != me) { Alarm(k, "someone in a GM robe is near (" + who.ToString("X") + ")"); break; }
            }
        } else if (p[0] == 0x2F && p.Length >= 10 && U32(p, 6) == me) Alarm(k, "you are attacked by " + MobileName(k, U32(p, 2)));
        else if (p[0] == 0x2C) Alarm(k, "you died");
    }
    void Alarm(GameLink k, string why) {
        if (circuitAlarm != null) return;
        circuitAlarm = why; circuitStop = true; workStop = true;
        Note("Circuit: stopped, " + why); k.Tell("Circuit stopped: " + why, 0x0026);
    }

    // The circuit at the town the player is in: buy out each thread vendor whose restock is due, weave at the nearest
    // loom, sew fancy shirts from all the cloth, sell what the sell list holds at its lowest prices or more, then wait for
    // the next restock.
    System.Threading.Thread circuitThread;
    volatile bool sellOnly;
    public string CircuitSell() { sellOnly = true; tourMode = false; buyMode = false; surveyMode = false; refreshMode = false; string w = StartCircuit(); if (w != null) sellOnly = false; return w; }
    public string CircuitGo() { sellOnly = false; tourMode = false; buyMode = false; surveyMode = false; refreshMode = false; return StartCircuit(); }
    // The tour (RunTour): every tailor shop once, one shirt sold to each tailor that buys and all its thread bought.
    volatile bool tourMode;
    public string CircuitTour() { sellOnly = false; tourMode = true; buyMode = false; surveyMode = false; refreshMode = false; string w = StartCircuit(); if (w != null) tourMode = false; return w; }
    // The mining circuit (RunMine): shovels tinkered, ore dug through the cave the circuit starts in, smelted at the forge
    // set up, the ingots stored where the player keeps them.
    volatile bool mineMode;
    public string CircuitMine() { sellOnly = false; tourMode = false; buyMode = false; surveyMode = false; refreshMode = false; mineMode = true; string w = StartCircuit(); if (w != null) mineMode = false; return w; }
    // The buy circuit (RunBuy): the shopping list bought at the picked shops and stored in the bank.
    volatile bool buyMode;
    public string CircuitBuy() { sellOnly = false; tourMode = false; buyMode = true; surveyMode = false; refreshMode = false; string w = StartCircuit(); if (w != null) buyMode = false; return w; }
    // The survey (RunSurvey): every shop in a town visited and each keeper's stock read, nothing bought or sold.
    volatile bool surveyMode;
    public string CircuitSurvey() { sellOnly = false; tourMode = false; buyMode = false; surveyMode = true; refreshMode = false; string w = StartCircuit(); if (w != null) surveyMode = false; return w; }
    // The refresh (RunRefresh): food for the mount and the player, both fed full, then the bank.
    volatile bool refreshMode;
    public string CircuitRefresh() { sellOnly = false; tourMode = false; buyMode = false; surveyMode = false; refreshMode = true; string w = StartCircuit(); if (w != null) refreshMode = false; return w; }
    // The smithing circuit (RunSmithing): the ladder smithed at the anvil it starts at, sold, until 100.0.
    volatile bool smithMode;
    public string CircuitSmith() { sellOnly = false; tourMode = false; buyMode = false; surveyMode = false; refreshMode = false; mineMode = false; smithMode = true; string w = StartCircuit(); if (w != null) smithMode = false; return w; }
    // The town surveyed: circuit.survey.town, empty for the town the player is in.
    public string SurveyTown { get { return Cfg.Get("circuit.survey.town", ""); } set { Cfg.Set("circuit.survey.town", value ?? ""); } }
    public string SurveyTarget() { string t = SurveyTown; if (t.Length > 0) return t; var k = link; return k == null ? null : CityOf(k); }
    // The town's shops to survey: the ones ticked (circuit.shops.survey) among the town's, else every shop in it.
    public System.Collections.Generic.List<UoRegions.Shop> SurveyShops() {
        var all = UoRegions.AllShopsIn(SurveyTarget()); var keys = CircuitShopKeys("survey");
        var picked = all.FindAll(s => keys.Contains(s.Key)); return picked.Count > 0 ? picked : all;
    }
    string StartCircuit() {
        var k = link; if (k == null || k.Closed) return "no game connection";
        if (circuitThread != null && circuitThread.IsAlive) return "the circuit is running";
        if (loopThread != null && loopThread.IsAlive) return "a loop is running";
        ManualStart(); circuitAlarm = null; CircuitStart = System.DateTime.Now; ResetCooldowns();
        circuitThread = new System.Threading.Thread(() => RunCircuit(k)); circuitThread.IsBackground = true; circuitThread.Start();
        return null;
    }
    // A start clears every timed avoidance, saved ones too (Damian, 2026-10-18: "we need the start to refresh the cooldown
    // timers"; sell-only passed Ella, standing beside Kwik and willing to buy, for an avoidance set an hour before).
    void ResetCooldowns() {
        lock (underFloor) { underFloor.Clear(); underFloorLoaded = true; try { System.IO.File.WriteAllLines(UnderFloorPath, new string[0]); } catch { } }
        lock (skipped) skipped.Clear();
        lock (bankSkip) bankSkip.Clear();
        lock (shopAvoid) { shopAvoid.Clear(); shopAvoidLoaded = true; try { System.IO.File.WriteAllLines(ShopAvoidPath, new string[0]); } catch { } }
        Note("Circuit: start; every vendor, bank and shop avoidance cleared");
    }
    // A step clears the stop only when no circuit runs, a step run from the control port: a step inside the circuit that
    // cleared it made a stop asked mid-trip do nothing (2026-10-17 10:42:49, the circuit went on to Vesper and Jhelom).
    void ManualStart() { var t = circuitThread; if (t == null || !t.IsAlive) circuitStop = false; }
    public bool CircuitRunning { get { var t = circuitThread; return t != null && t.IsAlive; } }
    public void CircuitHalt(string why) { if (circuitThread != null && circuitThread.IsAlive) { circuitStop = true; workStop = true; Note("Circuit: stopping, " + why); } }
    AtlasNpc VendorNow(uint serial) { if (Atlas == null) return null; foreach (var n in Atlas.Visible()) if (n.Serial == serial) return n; return null; }
    // A named vendor is walked to before it is asked: it hears its name from 3 tiles, and one behind a door is reached by
    // opening the door and going into the room (Damian, 2026-10-17: "if they can't be seen, you need to open the door and
    // go in the room"; Michelle was 20 tiles from the east tailor's door). The nearest of that first name the atlas knows
    // within CircuitReach, as last seen; null when there, unknown, or "vendor".
    // A vendor out of view is where it was last seen, perhaps a day ago, so the walk is taken again from where it ends,
    // to where the vendor is seen now (Jannelle, 2026-10-17: walked to 3772,1248 from 50 tiles off, seen at 3765,1262).
    public const int VendorWalks = 3;
    public const string TookOver = "the player took over";
    // A shop asked and, unanswered, asked again after the walk is taken anew by where the vendor is seen now: a vendor
    // the atlas still had 2 tiles off did not hear "Jannelle buy" (2026-10-17 09:17), then answered the sale.
    // Every circuit's asks come through here (VendorBuy, VendorSell). A vendor going home is asked again once it is home,
    // as often as it answers "Let us go back to my shop", GoingHomeAsks asks in all (Damian, 2026-10-17: "if they repeat
    // the let us go line, wait again and observe the walking"); a positive answer records where it stands as its home.
    public const int GoingHomeAsks = 3;
    byte[] AskNear(GameLink k, string who, string word, int op) {
        bool askedAgain = false; int[] fix = null; NotHome = null;
        // A vendor where it has sent the player home before is not asked there: it is watched until it is home, and
        // passed over when it stays away (Cybil on the Magincia mage shop roof).
        var named = who == "vendor" ? null : VendorNamed(k, who);
        if (named != null && VendorAtHome(k, named.Serial) == false) {
            string where; VendorArea(k, named.Serial, out where); Note("Circuit: " + who + " is away from its shop (" + where + "); waiting for it to be home");
            if (!WaitVendorHome(k, who)) { VendorArea(k, named.Serial, out where); NotHome = who + " stayed away from its shop (" + where + ")"; return null; }
            if (NearVendor(k, who) != null) return null;
        }
        for (int attempt = 0; attempt < GoingHomeAsks; attempt++) {
            // Asked again only when the answer says the vendor is going home, and then only at home, or says "hand it to me"
            // (AskAgain, once), after SellGapMs: every other answer ("I do not understand." from a minter or a guard) is
            // final, and one nobody knows is kicked up. Silence is final only when nothing changed (Damian, 2026-10-17:
            // "L-FUTILE means if nothing else changes: meaning take a step, re-evaluate and re-measure, then try again").
            if (attempt > 0) {
                var kind = KindOf(LastReply); string changed;
                if (kind == Reply.Unknown) KickUp(who + " " + word, LastReply);
                if (kind == Reply.AskAgain && !askedAgain) { askedAgain = true; System.Threading.Thread.Sleep(SellGapMs); }
                else if (kind == Reply.None && SayRefused == null && who != "vendor" && (changed = SilenceChanged(fix, VendorFix(k, who), OtherWindow)) != null) {
                    Note("Circuit: " + who + " did not answer \"" + word + "\": " + changed + "; closing in to ask again");
                    string nw = NearVendor(k, who); if (nw != null) { Note("Circuit: " + nw); return null; }
                }
                else if (kind != Reply.GoingHome || who == "vendor") return null;
                else {
                    // The region it answered in before, walked to while it goes there (Damian: "next time you visit, you
                    // could anticipate a walking if they are far from known home"); then the vendor watched until it is
                    // home, and the player follows it there (Damian: "kwik needed to follow the vendor to the shop and retry").
                    NoteVendorAway(k, who);
                    var v = VendorNamed(k, who); var home = v == null ? null : VendorHomeRegion(k, v);
                    if (home != null) { string hw = WalkToSpeakingRange(home[0], home[1], home[2]); if (hw != null) Note("Circuit: walking to " + who + "'s shop: " + hw); }
                    if (!WaitVendorHome(k, who, true)) { string where; VendorArea(k, v.Serial, out where); NotHome = who + " stayed away from its shop (" + where + ")"; return null; }
                    if (NearVendor(k, who) != null) return null;
                }
                // The answer asked for this one more ask (assist-plan.md rule 1).
                k.Heard(who + " " + word);
            }
            byte[] r; lock (actionGate) { Answered(k, AnswerMs); fix = VendorFix(k, who); r = AskShop(k, who, word, op); }
            if (r != null) { NoteVendorHome(k, who); return r; }
        }
        if (KindOf(LastReply) == Reply.Unknown) KickUp(who + " " + word, LastReply);
        return null;
    }
    // Where the vendor asked stood as the server last placed it, and whether it could hear from there (in view, within
    // VendorHear, in sight): { x, y, hears }, { 0, 0, 0 } out of view, null when no vendor of that name is in view.
    int[] VendorFix(GameLink k, string who) {
        var v = VendorNamed(k, who); if (v == null) return null;
        int px, py, pz; int[] at; bool seen; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; seen = k.State.Mobiles.TryGetValue(v.Serial, out at); if (seen) at = new[] { at[0], at[1], at[2] }; }
        if (!seen) return new[] { 0, 0, 0 };
        return new[] { at[0], at[1], Hears(k, px, py, pz, at[0], at[1], at[2]) ? 1 : 0 };
    }
    // A vendor hears only on the player's floor: Edmund, 1 and 2 tiles off on the floor below (z 20, the player at 40),
    // answered none of three "Edmund buy" (Magincia, 2026-10-17 02:55). FloorApart: floors stand 20 apart; a step, dais or
    // slope stays under 16.
    public const int FloorApart = 16;
    bool Hears(GameLink k, int px, int py, int pz, int vx, int vy, int vz) {
        return Tiles(vx, vy, px, py) <= VendorHear && OnFloor(pz, vz) && Sight(k, px, py, pz, vx, vy);
    }
    // What changed between the ask and its silence, or null when nothing did: another vendor's window came in its place
    // (the race), the vendor was out of hearing when the line went out, or it has moved since.
    static string SilenceChanged(int[] asked, int[] now, bool raced) {
        if (raced) return "another vendor's window came instead";
        if (asked == null || now == null) return null;
        if (asked[2] == 0) return "it was out of hearing when asked";
        if (now[0] != asked[0] || now[1] != asked[1]) return now[2] == 0 && now[0] == 0 ? "it went out of view" : "it moved from " + asked[0] + "," + asked[1] + " to " + now[0] + "," + now[1];
        return null;
    }
    // The vendor is located before it is asked, not after it fails to answer (Damian, 2026-10-17: "be sure to locate the
    // npc and test its distance before asking to buy or sell, not after a failure"): its tile is the server's (the
    // mobiles in view, AssistState.Mobiles), the atlas only says where to walk until it comes into view, and the ask goes
    // out only with the vendor in view and within VendorHear (ServUO BaseVendor: 3 tiles, by range alone).
    public const int VendorHear = 3, VendorFindReach = 400;
    string NearVendor(GameLink k, string who) {
        if (Atlas == null || who == "vendor") return null;
        string why = who + " is not known here";
        for (int leg = 0; leg <= VendorWalks; leg++) {
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            AtlasNpc best = null; int bx = 0, by = 0, bz = 0; bool bestSeen = false; var known = Atlas.Visible(); known.AddRange(Atlas.Shops());
            foreach (var n in known) {
                if (!string.Equals(n.Name.Split(' ')[0], who, System.StringComparison.OrdinalIgnoreCase)) continue;
                int x = n.X, y = n.Y, z = 0; bool seen; lock (k.State.Gate) { int[] at; seen = k.State.Mobiles.TryGetValue(n.Serial, out at); if (seen) { x = at[0]; y = at[1]; z = at[2]; } }
                // A named vendor is looked for across the city: Jhelom's Kimmie is 224 tiles from the recall landing, and a
                // reach of CircuitReach made her "not known here".
                if (Tiles(x, y, px, py) > VendorFindReach) continue;
                // One in view first, then the nearest.
                if (best == null || (seen && !bestSeen) || (seen == bestSeen && Tiles(x, y, px, py) < Tiles(bx, by, px, py))) { best = n; bx = x; by = y; bz = z; bestSeen = seen; }
            }
            if (best == null) return why;
            int pz; lock (k.State.Gate) pz = k.State.Z;
            int floor = bestSeen ? bz : AnyFloor; var rivals = Interceptors(k, best.Serial, floor);
            if (bestSeen && Hears(k, px, py, pz, bx, by, bz)) {
                int[] rival = rivals.Find(r => Tiles(r[0], r[1], px, py) <= Tiles(bx, by, px, py));
                if (rival == null) return null;
                why = who + " is " + Tiles(bx, by, px, py) + " tiles away and another vendor " + Tiles(rival[0], rival[1], px, py);
                if (leg == VendorWalks) break;
                var spots = NearerSpots(px, py, bx, by, rivals); if (spots.Count == 0) return why + ", and no tile within " + VendorHear + " of " + who + " is nearer " + who;
                string sw = null; foreach (var s in spots.GetRange(0, System.Math.Min(3, spots.Count))) { sw = WalkTo(s[0], s[1], 0, false, floor); if (sw == null) break; }
                if (sw != null) return "stepping nearer " + who + " than the other vendor: " + sw;
                continue;
            }
            why = bestSeen ? who + " is " + Tiles(bx, by, px, py) + " tiles away" : who + " is not in view at " + bx + "," + by;
            if (leg == VendorWalks) break;
            string w = WalkToSpeakingRange(bx, by, floor); if (w != null) return "walking to " + who + (bestSeen ? " (z " + bz + ")" : "") + ": " + w;
        }
        return why;
    }
    // A named ask is answered by another vendor nearer the player: Edmund answered "Cybil buy" three times while Cybil
    // walked off (Magincia, 2026-10-17 02:37). The player stands nearer the vendor named than any other vendor in view
    // (Damian, 2026-10-17: "ensuring the distance to target is shorter than distance to interceptor"). VendorRivalReach is
    // ServUO VendorAI's pre-AOS hearing, 4 tiles, around the furthest tile the player can ask from.
    public const int VendorRivalReach = 4;
    // floor: the vendor's; a vendor on another floor does not hear the ask (Hears).
    System.Collections.Generic.List<int[]> Interceptors(GameLink k, uint target, int floor) {
        var list = new System.Collections.Generic.List<int[]>(); if (Atlas == null) return list;
        var shops = new System.Collections.Generic.HashSet<uint>(); foreach (var n in Atlas.Shops()) shops.Add(n.Serial);
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) if (kv.Key != target && shops.Contains(kv.Key) && OnFloor(kv.Value[2], floor) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= VendorHear + VendorRivalReach) list.Add(new[] { kv.Value[0], kv.Value[1] });
        return list;
    }
    // The tiles within VendorHear of the vendor at (vx, vy) nearer it than every rival: the widest margin first, then the
    // nearest the player.
    static System.Collections.Generic.List<int[]> NearerSpots(int px, int py, int vx, int vy, System.Collections.Generic.List<int[]> rivals) {
        var spots = new System.Collections.Generic.List<int[]>();
        for (int dx = -VendorHear; dx <= VendorHear; dx++) for (int dy = -VendorHear; dy <= VendorHear; dy++) {
            int x = vx + dx, y = vy + dy, to = Tiles(x, y, vx, vy); if (to == 0) continue;
            int margin = int.MaxValue; foreach (var r in rivals) margin = System.Math.Min(margin, Tiles(x, y, r[0], r[1]) - to);
            if (margin >= 1) spots.Add(new[] { x, y, margin, Tiles(x, y, px, py) });
        }
        spots.Sort((a, b) => a[2] != b[2] ? b[2].CompareTo(a[2]) : a[3].CompareTo(b[3]));
        return spots;
    }
    // Every loom seen, kept for the walk back to one from out of view.
    readonly System.Collections.Generic.List<int[]> loomSpots = new System.Collections.Generic.List<int[]>();
    AssistItem NearLoom(GameLink k, int px, int py) {
        NoteLooms(k); AssistItem loom = null;
        lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (System.Array.IndexOf(Looms, g.Graphic) >= 0 && Tiles(g.X, g.Y, px, py) <= CircuitReach && (loom == null || Tiles(g.X, g.Y, px, py) < Tiles(loom.X, loom.Y, px, py))) loom = g;
        return loom;
    }
    // Every loom in view, noted on each walk step, so the nearest loom known is the nearest one passed, not only the one
    // last used.
    void NoteLooms(GameLink k) {
        var seen = new System.Collections.Generic.List<int[]>(); var signs = new System.Collections.Generic.List<int[]>();
        lock (k.State.Gate) foreach (var g in k.State.Ground.Values) { if (System.Array.IndexOf(Looms, g.Graphic) >= 0) seen.Add(new[] { g.X, g.Y }); else if (UoTiles.Name(g.Graphic) == "Bank") signs.Add(new[] { g.X, g.Y }); }
        lock (loomSpots) foreach (var s in seen) if (!loomSpots.Exists(t => t[0] == s[0] && t[1] == s[1])) loomSpots.Add(s);
        if (signs.Count > 0) { LoadBankSigns(); bool added = false; lock (bankSigns) foreach (var s in signs) if (!bankSigns.Exists(t => t[0] == s[0] && t[1] == s[1])) { bankSigns.Add(s); added = true; } if (added) SaveBankSigns(); }
    }
    // Every town has a bank (Damian, 2026-10-17: "there are many banks, and every town has one"); the 1998 region file
    // misses some (Nujel'm's, by its sign at 3768,1313), so each "Bank" sign seen is kept in banks-<server>.txt (x, tab, y)
    // and banked at in a city whose banks the region file misses.
    readonly System.Collections.Generic.List<int[]> bankSigns = new System.Collections.Generic.List<int[]>(); bool bankSignsLoaded;
    string BankSignsPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "banks-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    void LoadBankSigns() {
        lock (bankSigns) {
            if (bankSignsLoaded) return; bankSignsLoaded = true;
            try { if (System.IO.File.Exists(BankSignsPath)) foreach (var line in System.IO.File.ReadAllLines(BankSignsPath)) { var t = line.Split('\t'); if (t.Length == 2) bankSigns.Add(new[] { int.Parse(t[0]), int.Parse(t[1]) }); } } catch { }
        }
    }
    void SaveBankSigns() { lock (bankSigns) { var l = new System.Collections.Generic.List<string>(); foreach (var s in bankSigns) l.Add(s[0] + "\t" + s[1]); try { System.IO.File.WriteAllLines(BankSignsPath, l); } catch { } } }
    // A sign stands outside its bank's wall (Vesper's at 2896,673 is outside The Mint), so a city's banks are its BANK_
    // regions, and its signs only when the region file has none for it (Nujel'm).
    // A bank that answered "bank" before goes first, then the nearest (Nujel'm's sign at 3768,1313 went unheard twice
    // and the one at 3762,1316 answered, 2026-10-17); the answered ones are kept in banks-heard-<server>.txt (x, tab, y).
    System.Collections.Generic.List<int[]> CityBanks(string city, int x, int y) {
        var l = UoRegions.BanksIn(city, x, y); LoadBankSigns();
        if (l.Count == 0) lock (bankSigns) foreach (var s in bankSigns) if (UoRegions.City(s[0], s[1]) == city) l.Add(s);
        l.Sort((a, b) => { int ha = WasHeard(a) ? 0 : 1, hb = WasHeard(b) ? 0 : 1; return ha != hb ? ha.CompareTo(hb) : Tiles(a[0], a[1], x, y).CompareTo(Tiles(b[0], b[1], x, y)); });
        return l;
    }
    readonly System.Collections.Generic.HashSet<string> bankHeard = new System.Collections.Generic.HashSet<string>(); bool bankHeardLoaded;
    string BankHeardPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "banks-heard-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    bool WasHeard(int[] bank) {
        lock (bankHeard) {
            if (!bankHeardLoaded) { bankHeardLoaded = true; try { if (System.IO.File.Exists(BankHeardPath)) foreach (var line in System.IO.File.ReadAllLines(BankHeardPath)) { var t = line.Split('\t'); if (t.Length == 2) bankHeard.Add(t[0] + "," + t[1]); } } catch { } }
            return bankHeard.Contains(bank[0] + "," + bank[1]);
        }
    }
    void BankHeard(int[] bank) {
        if (WasHeard(bank)) return;
        lock (bankHeard) { bankHeard.Add(bank[0] + "," + bank[1]); var l = new System.Collections.Generic.List<string>(); foreach (var s in bankHeard) l.Add(s.Replace(',', '\t')); try { System.IO.File.WriteAllLines(BankHeardPath, l); } catch { } }
    }

    // ---------- the circuit's trips (tailoring-circuit.md) ----------
    // Travel is by Recall from the bank box's runes (BankRecall). A city is the region file's (UoRegions.City); a tailor
    // that offered under the floor is passed over for UnderFloorMinutes, as a city's price falls with what it has bought
    // ("sales have pushed it down") and comes back: Tate offered 16 at 15:35 and paid 24 at 16:21, Ella 14 at 15:31 and 24
    // at 16:27 (2026-10-17, Damian selling by hand at 4 times the circuit's rate).
    public const int UnderFloorMinutes = 45, TripReagents = 4, ReagentRefill = 30;
    // Before a trip: TripReagents of each recall reagent, else ReagentRefill from the bank of the city here;
    // null, or why the trip cannot go (2026-10-17: out of mandrake root, the trip tried 8 cities in a row).
    string TripReady(GameLink k) {
        OpenPackBags(k);
        var shortOf = new System.Collections.Generic.List<int>(); foreach (int r in RecallReagents) if (PackCount(k, g => g == r) < TripReagents) shortOf.Add(r);
        if (shortOf.Count == 0) return null;
        string city = CityOf(k);
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        var cb = city == null ? new System.Collections.Generic.List<int[]>() : CityBanks(city, px, py); var bank = cb.Count > 0 ? cb[0] : null; if (bank == null) return "short of " + UoTiles.Name(shortOf[0]) + " and no bank here";
        CircuitStatus = "Fetching reagents from the bank in " + city;
        string w = ToBanker(k, bank); if (w != null) return "walking to the bank for reagents: " + w;
        string taken; w = BankTake(new System.Collections.Generic.List<int>(RecallReagents), ReagentRefill, out taken); Note("Circuit: reagents from the bank: " + (w ?? taken));
        // What the bank lacked, from the city's mage shop (the bank ran out of mandrake root, 2026-10-17).
        // The nearest seller the atlas knows has it, by name (Britain's are in The Sorcerer's Delight; the nearest mage shop,
        // Ethereal Goods, has none known); a mage shop asked as "vendor buy" only when the atlas knows no seller here.
        foreach (int r in RecallReagents) {
            if (PackCount(k, g => g == r) >= TripReagents) continue;
            lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            int qx = px, qy = py, want = r; string label = UoTiles.Name(r); string mw;
            var sellers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => UoRegions.City(n.X, n.Y) == city && n.Items.Exists(s => s.StartsWith(label + ":", System.StringComparison.OrdinalIgnoreCase)));
            sellers.Sort((a, b) => Tiles(a.X, a.Y, qx, qy).CompareTo(Tiles(b.X, b.Y, qx, qy)));
            if (sellers.Count > 0) { CircuitStatus = "Buying " + label + " from " + sellers[0].Name; int got, left; mw = VendorBuy(g => g == want, ReagentRefill, out got, out left, sellers[0].Name.Split(' ')[0]); }
            // No seller known by name: none asked (Damian, 2026-10-17: "use the vendors name proper"; "vendor buy" asked
            // Mark in Moonglow and anyone else in earshot).
            else mw = "no " + label + " seller in " + city + " known by name";
            if (mw != null) Note("Circuit: buying " + label + ": " + mw);
            if (Silent(mw)) return "buying " + label + ": " + mw;
        }
        foreach (int r in RecallReagents) if (PackCount(k, g => g == r) < 1) return "no " + UoTiles.Name(r) + " in the pack, the bank or the mage shop";
        return null;
    }
    // Every travel failure ends the trip but a city with no rune in the bank: the next city's recall goes from the same
    // bank and fails the same way, and the trip said "bank" every 1.5 s, city after city, short of Vesper's banker
    // (Damian, 2026-10-17: "you must stop all spamming").
    static bool TripStops(string why) { return why != null && !why.StartsWith("no rune for "); }
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> underFloor = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    string CityOf(GameLink k) { int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; } return UoRegions.City(x, y); }
    // Travel from the bank box (Damian, 2026-10-17: "now we don't need the public library and dont have to 'go back to
    // brit' ever. all the destinations we need are right there in any bank"): to the nearest bank of the city here (or
    // "bank" where the player stands when the region file knows none), the box and its bags opened, each rune labelled
    // where it lies (cached by serial in bank-runes-<server>.txt), and the recall cast at the rune for the city. Runes
    // never leave the bank (Damian: "never take runes out of the bank").
    readonly System.Collections.Generic.Dictionary<uint, string> bankRunes = new System.Collections.Generic.Dictionary<uint, string>();
    bool bankRunesLoaded, lastRecallShop;
    string BankRunesPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "bank-runes-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    static string PlaceKey(string s) { return s == null ? "" : s.Replace("'", "").Replace(" ", "").ToLowerInvariant(); }
    void LoadBankRunes() { lock (bankRunes) if (!bankRunesLoaded) { bankRunesLoaded = true; try { if (System.IO.File.Exists(BankRunesPath)) foreach (var line in System.IO.File.ReadAllLines(BankRunesPath)) { var t = line.Split('\t'); if (t.Length == 2) bankRunes[System.Convert.ToUInt32(t[0], 16)] = t[1]; } } catch { } } }
    // Whether the bank box holds a rune for the city, by the labels read so far; true while none has been read.
    bool BankHasRune(string city) { LoadBankRunes(); lock (bankRunes) { if (bankRunes.Count == 0) return true; foreach (var v in bankRunes.Values) if (PlaceKey(v) == PlaceKey(city)) return true; return false; } }
    public string BankRecall(GameLink k, string city) { return BankRecall(k, city, true); }
    string BankRecall(GameLink k, string city, bool shopRune) {
        if (Banned(city)) return "never going to " + city;
        LoadBankRunes();
        string here = CityOf(k); int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        var banks = here == null ? new System.Collections.Generic.List<int[]>() : CityBanks(here, x, y);
        // The city's banks, one that answered before first (CityBanks); a bank the walk cannot reach is passed over for
        // FullMinutes and the next walked to, nothing having been said; "bank" said and unanswered ends the trip
        // (L-FUTILITY). With none left the walk-to-bank fallback in Travel takes a pack rune.
        int[] bank = null; string why = null;
        foreach (var b in banks) {
            if (circuitStop) return "stopped";
            if (BankSkipped(b)) continue;
            // ToBanker says "bank" only at the bank, its edge first (Damian, 2026-10-17: "kwik called for the bank NOWHERE
            // NEAR THE BANK"), and returns with the box open.
            CircuitStatus = "Walking to the bank in " + here + " for the " + city + " rune"; string w = ToBanker(k, b); if (w != null) { SkipBank(b); why = "the bank: " + w; continue; }
            bank = b; break;
        }
        if (banks.Count > 0 && bank == null) return why ?? "walking to the bank: every bank in " + here + " failed within " + FullMinutes + " minutes";
        byte[] open;
        lock (actionGate) { Answered(k, AnswerMs); open = SayBank(k); }
        if (open == null) { if (bank != null) SkipBank(bank); return NoBanker(); }
        if (bank != null) BankHeard(bank);
        uint box = U32(open, 1);
        var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
        while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
        var tree = new System.Collections.Generic.HashSet<uint> { box };
        for (int depth = 0; depth < 2; depth++) {
            var bags = new System.Collections.Generic.List<uint>();
            lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)) bags.Add(it.Serial);
            foreach (var b in bags) { bool known; lock (contents) known = contents.ContainsKey(b); if (!known) { BusyWait(clickedAt); Peek(b); } tree.Add(b); }
        }
        var runes = new System.Collections.Generic.List<AssistItem>();
        lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17) runes.Add(it);
        var unknown = runes.FindAll(r => { lock (bankRunes) return !bankRunes.ContainsKey(r.Serial); });
        if (unknown.Count > 0) {
            // Every answered label is kept, an unmarked rune's too: kept only for marked ones, every unmarked rune was
            // clicked again at every bank, a flood of "unmarked recall rune" in Damian's chat (2026-10-17).
            foreach (var line in LabelsOf(k, unknown)) {
                int sp = line.IndexOf(' '), at = line.LastIndexOf(" at "); if (sp <= 0 || at <= sp) continue;
                string label = line.Substring(sp + 1, at - sp - 1); if (label == "no label") continue;
                int rf = label.IndexOf("recall rune for "); lock (bankRunes) bankRunes[System.Convert.ToUInt32(line.Substring(0, sp), 16)] = rf >= 0 ? label.Substring(rf + 16) : label;
            }
            lock (bankRunes) { var lines = new System.Collections.Generic.List<string>(); foreach (var kv in bankRunes) lines.Add(kv.Key.ToString("X") + "\t" + kv.Value); try { System.IO.File.WriteAllLines(BankRunesPath, lines); } catch { } }
        }
        // A shop rune first (Damian, 2026-10-17: "the circuit didn't use the moonglow tailor rune"): the bank's bag of runes
        // marked at the tailor shops, each labelled for its shop ("Stitchin' Time in Magincia", "Moonglow tailor"), lands at
        // the shop; the city's own rune, which lands at the city's recall point, when the city has none.
        uint rune = 0, cityRune = 0;
        foreach (var r in runes) {
            string label; lock (bankRunes) bankRunes.TryGetValue(r.Serial, out label); string key = PlaceKey(label);
            if (key == PlaceKey(city)) { if (cityRune == 0) cityRune = r.Serial; }
            else if (shopRune && rune == 0 && key.Length > 0 && key.Contains(PlaceKey(city))) rune = r.Serial;
        }
        if (rune == 0) rune = cityRune;
        lastRecallShop = rune != cityRune;
        if (rune == 0) return "no rune for " + city + " in the bank";
        CircuitStatus = "Recalling to " + city + " from the bank"; return Recall(rune);
    }
    // Places a trip never goes (Damian, 2026-10-17: "put a rule in, do not go to wind"), by destination or rune label.
    static readonly string[] NeverGo = { "Wind" };
    static bool Banned(string place) { if (place == null) return false; foreach (var b in NeverGo) if (place.IndexOf(b, System.StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
    // A recall that moved the player is not one that arrived: the trip goes on only in the city it named.
    string Travel(GameLink k, string city) {
        if (Banned(city)) return "never going to " + city;
        if (CityOf(k) == city) return null;
        // The recall goes from the bank, so the gold over the purse is banked there first: one walk, not two (Damian banked
        // whatever he carried at each recall's bank, 970 in Britain, 2026-10-17).
        string from = CityOf(k);
        if (from != null && PackCount(k, g => g == GoldArt) > PurseKeep) { string bw = BankHere(k, from); if (bw != null) Note("Circuit: banking before the recall: " + bw); }
        string w = BankRecall(k, city);
        // A shop rune's landing taken by someone ("Something is blocking the location."; Skara Brae's tailor, 2026-10-17):
        // the city's own rune once, a different landing, from the box still open.
        if (w == "the landing is blocked" && lastRecallShop) { Note("Circuit: the shop rune for " + city + " is blocked; by the city's rune"); w = BankRecall(k, city, false); }
        // A bank the walk cannot reach (Jhelom's south island has none; Damian, 2026-10-17: "also you had runes you could
        // have recalled"): by the pack's rune for the city when it carries one, else by another pack rune to a city with
        // a bank, and the bank's rune from there.
        if (w != null && !circuitStop && (w.StartsWith("walking to the bank") || w.StartsWith("the walk to the bank"))) {
            Note("Circuit: " + w + "; travelling by a pack rune");
            uint direct = 0, other = 0; string otherCity = null, here = CityOf(k);
            foreach (var line in PackRuneLabels(k)) {
                int sp = line.IndexOf(' '), at = line.IndexOf("recall rune for "), end = line.LastIndexOf(" at "); if (sp <= 0 || at < 0 || end <= at) continue;
                uint s = System.Convert.ToUInt32(line.Substring(0, sp), 16); string place = line.Substring(at + 16, end - at - 16);
                if (PlaceKey(place) == PlaceKey(city)) direct = s; else if (other == 0 && PlaceKey(place) != PlaceKey(here) && !Banned(place)) { other = s; otherCity = place; }
            }
            if (direct == 0 && other == 0) return w + "; no pack rune to travel by";
            string rw = Recall(direct != 0 ? direct : other); if (rw != null) return "recalling by the pack rune for " + (direct != 0 ? city : otherCity) + ": " + rw;
            w = CityOf(k) == city ? null : BankRecall(k, city);
        }
        if (w != null) return w;
        return CityOf(k) == city ? null : "the rune for " + city + " landed in " + (CityOf(k) ?? "no city");
    }
    // The shops of a city: tailors (fancy shirts) or thread vendors, from the atlas.
    System.Collections.Generic.List<AtlasNpc> CityShops(string city, string sells) {
        var l = new System.Collections.Generic.List<AtlasNpc>(); if (Atlas == null) return l;
        foreach (var n in Atlas.Shops()) if (UoRegions.City(n.X, n.Y) == city && TailorVendor(n) && n.Items.Exists(s => s.IndexOf(sells, System.StringComparison.OrdinalIgnoreCase) >= 0)) l.Add(n);
        return l;
    }
    // A thread vendor by its stock list, or any tailor not yet seen to have none: a tailor's buy list may never have been
    // read (Damian, 2026-10-17: "we go find another vendor that is not recently checked. magincia is great for that";
    // the atlas knew no Magincia thread vendor).
    // Every named shopkeeper standing in one of the city's tailor shops is one too, whatever is known of it, unless its
    // stock list is read and holds no thread (Damian, 2026-10-17: "its always a tailor that is thread selling, and they
    // all do"; started in Jhelom's north Needle, the circuit walked for Kimmie on the south island and asked nobody there).
    System.Collections.Generic.List<AtlasNpc> ThreadCandidates(string city) {
        var l = CityShops(city, "thread");
        foreach (var n in CityBuyers(city)) if (!l.Exists(x => x.Serial == n.Serial)) l.Add(n);
        if (Atlas != null) {
            var shops = AllShops("tailor").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
            foreach (var n in Atlas.Shops()) {
                if (n.Name.Length == 0 || l.Exists(x => x.Serial == n.Serial) || !shops.Exists(s => s.Holds(n.X, n.Y, 2))) continue;
                if (n.Items.Count > 0 && !n.Items.Exists(s => s.IndexOf("thread", System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                l.Add(n);
            }
        }
        return l;
    }
    // The candidates nearest the player first.
    System.Collections.Generic.List<AtlasNpc> NearestFirst(GameLink k, System.Collections.Generic.List<AtlasNpc> l) {
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        l.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py))); return l;
    }
    bool ThreadDue(AtlasNpc n) { if (Skipped(n.Serial)) return false; var rec = CircuitVendors().Find(c => c.Serial == n.Serial); return rec == null || rec.Stock != 0 || System.DateTime.Now >= rec.BoughtOutAt.AddMinutes(RestockMinutes); }
    // A trip's preference by city: the quiet ones first (Damian, 2026-10-17: "magincia is great for solo, nujelm and jhelom
    // too"). Britain is one city among the rest, its shops used and no centre (Damian, 2026-10-17: "yes it should visit
    // brit, but it should also not consider it the primary central location"; he sold at both its tailors at 24).
    static readonly string[] QuietCities = { "Magincia", "Nujel'm", "Jhelom" };
    static int CityWeight(string city) { return System.Array.IndexOf(QuietCities, city) >= 0 ? 300 : 100; }
    // The under-floor offers, kept in underfloor-<server>.txt (serial, tab, ticks) so a relay restart does not ask every
    // tailor again (2026-10-17: after a restart the circuit walked Britain end to end re-asking Slade, Tracy and Michelle).
    bool underFloorLoaded;
    string UnderFloorPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "underfloor-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    // underFloor holds when a tailor may be asked again: UnderFloorMinutes after an offer under the floor, FullMinutes after it
    // took no more (Damian, 2026-10-17: "do not fucking return to a vendor you have seen and bought out and sold out for 15
    // mintues. remember the history of these runs").
    public const int FullMinutes = 15;
    void UnderFloorOffered(uint serial) { AvoidTailor(serial, System.TimeSpan.FromMinutes(UnderFloorMinutes)); }
    void AvoidTailor(uint serial, System.TimeSpan span) {
        lock (underFloor) {
            var until = System.DateTime.Now + span; System.DateTime had; if (underFloor.TryGetValue(serial, out had) && had > until) until = had;
            underFloor[serial] = until;
            var lines = new System.Collections.Generic.List<string>(); foreach (var kv in underFloor) lines.Add(kv.Key.ToString("X") + "\t" + kv.Value.Ticks);
            try { System.IO.File.WriteAllLines(UnderFloorPath, lines); } catch { }
        }
    }
    bool PaysFloor(AtlasNpc n) {
        lock (underFloor) if (!underFloorLoaded) {
            underFloorLoaded = true;
            try { if (System.IO.File.Exists(UnderFloorPath)) foreach (var line in System.IO.File.ReadAllLines(UnderFloorPath)) { var t = line.Split('\t'); if (t.Length == 2) underFloor[System.Convert.ToUInt32(t[0], 16)] = new System.DateTime(long.Parse(t[1])); } } catch { }
        } System.DateTime until; lock (underFloor) if (underFloor.TryGetValue(n.Serial, out until) && System.DateTime.Now < until) return false; return true; }
    // Hours since the circuit last sold to this tailor, from the trades history, at most RestCapHours (never: the cap).
    public const int RestCapHours = 12;
    double RestHours(AtlasNpc n) {
        if (Atlas == null) return RestCapHours; var last = System.DateTime.MinValue;
        foreach (var t in Atlas.Trades(n.Serial)) if (t.Kind == "sell" && t.At > last) last = t.At;
        return last == System.DateTime.MinValue ? RestCapHours : System.Math.Min(RestCapHours, (System.DateTime.Now - last).TotalHours);
    }
    // The cities a trip can go to, best first by score.
    System.Collections.Generic.List<string> TripCities(GameLink k, System.Func<string, int> score) {
        var cities = new System.Collections.Generic.List<string>(); string here = CityOf(k);
        if (Atlas != null) foreach (var n in Atlas.Shops()) { string c = UoRegions.City(n.X, n.Y); if (c != null && c != here && !Banned(c) && !cities.Contains(c) && score(c) > 0) cities.Add(c); }
        cities.Sort((a, b) => score(b).CompareTo(score(a)));
        return cities;
    }
    // Null, or why the buying stopped: a vendor that gave no proper answer ends it (assist-plan.md rule 1); one that could
    // not be reached or sold none is passed over for FullMinutes.
    // Nearest first, chosen again from where each ask left the player (Vesper, 2026-10-17: the first candidate in the
    // atlas's order was Tate across town, and the circuit walked past Morena's shop where the recall landed).
    string BuyCityThread(GameLink k, string city, out int got) {
        got = 0; NameUnnamed(VendorHear * 4);
        var left0 = ThreadCandidates(city).FindAll(ThreadDue);
        while (left0.Count > 0) {
            var v = NearestFirst(k, left0)[0]; left0.RemoveAt(0);
            if (circuitStop || PackCount(k, IsThreadArt) >= ThreadWant) break;
            if (!ThreadDue(v)) continue;
            string name = v.Name.Split(' ')[0]; CircuitStatus = "Buying thread from " + name + " in " + city; int g, left;
            string w = VendorBuy(IsThreadArt, ThreadWant * 10, out g, out left, name);
            if (w == null) { got += g; continue; }
            Note("Circuit: " + name + ": " + w);
            if (Silent(w)) return "buying thread from " + name + ": " + w;
            SkipVendor(v.Serial);
        }
        return null;
    }
    // Vendors not walked to or asked again for FullMinutes: the walk to them failed, or the buy failed with an answer.
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> skipped = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    void SkipVendor(uint serial) { lock (skipped) skipped[serial] = System.DateTime.Now.AddMinutes(FullMinutes); }
    bool Skipped(uint serial) { System.DateTime until; lock (skipped) return skipped.TryGetValue(serial, out until) && System.DateTime.Now < until; }
    public const int BankWalkReach = 3, BankerFind = 10, BankerNear = 3, BankSpotTries = 3, BankOpenFreshMs = 10000;
    // The bank approached as a player does (Damian, 2026-10-17: "first goal is usually to find a spot in range of the banker
    // but not go inside. to stay outside so you can't be boxed in, plus its faster. a human usually runs up right the edge
    // of the building where their travel lands and they say bank. if bank opens great. if not, run toward the bank door the
    // shortest route hugging the wall, unless there is players there then ride around them. keep spacing of at least 1
    // between you and other players when you land and say bank again. look to find the banker if the second blind attempt
    // fails. then you consider going through the doors"). "bank" is said once from each new place, in order: the nearest
    // free tile outside the building, the free outside tile nearest its door, a free outside tile within BankHear of the
    // banker, and beside the banker inside. Null once the box has opened (SayBank keeps its answer), else why every place
    // failed. say false: the walk to the building's edge only, nothing said (the survey's end).
    string ToBanker(GameLink k, int[] bank, bool say = true) {
        var rects = UoRegions.BankRects(bank[0], bank[1]); if (rects.Count == 0) rects.Add(new[] { bank[0], bank[1], 1, 1 });
        var said = new System.Collections.Generic.List<int[]>(); string why = null;
        // 1. The edge nearest where the travel landed.
        string w = ToBankSpot(k, rects, null, 0); if (w != null) return "walking to the bank's edge: " + w;
        if (!say) return null;
        if (BankFrom(k, said, "at the bank's edge")) return null;
        // 2. Along the wall toward its door, BankRetryMin to BankRetryMax tiles on from the first try (Damian, 2026-10-17:
        // "when the first attempt doesn't land, move about 5-8 tiles before you try again").
        int[] door = BankDoor(k, rects), first = said[said.Count - 1];
        w = ToBankSpot(k, rects, door, int.MaxValue, first, BankRetryMin, BankRetryMax);
        if (w != null) why = "walking along the bank toward its door: " + w; else if (BankFrom(k, said, door != null ? "toward the bank's door" : "further along the bank")) return null;
        // 3. Outside, within the banker's hearing, BankRetryMin on from every try; 4. beside the banker, through the doors.
        int[] banker = BankerAt(k, rects, bank);
        if (banker == null) return why ?? "no banker answered \"bank\" from two places outside the bank, and no banker is in view";
        w = ToBankSpot(k, rects, banker, BankHear, said[said.Count - 1], BankRetryMin, int.MaxValue, said);
        if (w == null && BankFrom(k, said, "outside, within the banker's hearing")) return null;
        w = WalkTo(banker[0], banker[1], BankerNear); if (w != null) return "walking to the banker at " + banker[0] + "," + banker[1] + ": " + w;
        if (BankFrom(k, said, "beside the banker")) return null;
        return "no banker answered \"bank\" at the bank at " + bank[0] + "," + bank[1] + " (" + said.Count + " places)";
    }
    static bool InRects(System.Collections.Generic.List<int[]> rects, int x, int y) { foreach (var r in rects) if (x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]) return true; return false; }
    // To a free tile just outside the building (beside a tile of it, standable, no one within a tile): within reach of
    // near when given, nearest near first, else nearest the player; from minFrom to maxFrom tiles from `from` and at least
    // minFrom from every place in `apart`; BankSpotTries tried. Null once there.
    public const int BankRetryMin = 5, BankRetryMax = 8;
    string ToBankSpot(GameLink k, System.Collections.Generic.List<int[]> rects, int[] near, int reach, int[] from = null, int minFrom = 0, int maxFrom = int.MaxValue, System.Collections.Generic.List<int[]> apart = null) {
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        var spots = new System.Collections.Generic.List<int[]>(); var seen = new System.Collections.Generic.HashSet<long>();
        foreach (var r in rects) for (int x = r[0] - 1; x <= r[0] + r[2]; x++) for (int y = r[1] - 1; y <= r[1] + r[3]; y++) {
            if (InRects(rects, x, y) || !seen.Add(TileKey(x, y))) continue;
            if (near != null && Tiles(x, y, near[0], near[1]) > reach) continue;
            if (from != null && (Tiles(x, y, from[0], from[1]) < minFrom || Tiles(x, y, from[0], from[1]) > maxFrom)) continue;
            if (apart != null && apart.Exists(a => Tiles(x, y, a[0], a[1]) < minFrom)) continue;
            if (UoMap.StandZ(x, y, UoMap.LandStandZ(x, y)) == int.MinValue || Crowded(k, x, y)) continue;
            spots.Add(new[] { x, y });
        }
        if (spots.Count == 0) return "no free tile outside the bank" + (near != null && reach < int.MaxValue ? " within " + reach + " of " + near[0] + "," + near[1] : "") + (from != null ? " " + minFrom + (maxFrom < int.MaxValue ? " to " + maxFrom : " or more") + " tiles from " + from[0] + "," + from[1] : "");
        // Already on one: nothing to walk.
        if (spots.Exists(s => s[0] == px && s[1] == py)) return null;
        spots.Sort((a, b) => near != null && Tiles(a[0], a[1], near[0], near[1]) != Tiles(b[0], b[1], near[0], near[1]) ? Tiles(a[0], a[1], near[0], near[1]).CompareTo(Tiles(b[0], b[1], near[0], near[1])) : Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        string w = null; foreach (var s in spots.GetRange(0, System.Math.Min(BankSpotTries, spots.Count))) { w = WalkTo(s[0], s[1], 0); if (w == null) return null; }
        return w;
    }
    // Anyone other than the player on the tile or beside it (Damian: "keep spacing of at least 1 between you and other players").
    bool Crowded(GameLink k, int x, int y) { lock (k.State.Gate) foreach (var kv in k.State.Mobiles) if (kv.Key != k.State.Player && Tiles(kv.Value[0], kv.Value[1], x, y) <= 1) return true; return false; }
    // The bank's door nearest the player: a door or gate item in view on the building's edge.
    int[] BankDoor(GameLink k, System.Collections.Generic.List<int[]> rects) {
        int px, py, best = int.MaxValue; int[] door = null; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; foreach (var g in k.State.Ground.Values) { if (!IsDoor(g.Graphic)) continue; bool edge = false; for (int dx = -1; dx <= 1 && !edge; dx++) for (int dy = -1; dy <= 1 && !edge; dy++) edge = InRects(rects, g.X + dx, g.Y + dy); if (edge && Tiles(g.X, g.Y, px, py) < best) { best = Tiles(g.X, g.Y, px, py); door = new[] { g.X, g.Y }; } } }
        return door;
    }
    // The banker: the person in view inside the building nearest its centre (uo1998's bankers carry no title).
    int[] BankerAt(GameLink k, System.Collections.Generic.List<int[]> rects, int[] bank) {
        int[] at = null; int best = int.MaxValue;
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) {
            int body; if (kv.Key == k.State.Player || !k.State.Bodies.TryGetValue(kv.Key, out body) || body < 0x190 || body > 0x191 || !InRects(rects, kv.Value[0], kv.Value[1])) continue;
            int d = Tiles(kv.Value[0], kv.Value[1], bank[0], bank[1]); if (d < best) { best = d; at = new[] { kv.Value[0], kv.Value[1] }; }
        }
        return at;
    }
    // "bank" from where the player stands, unless said from beside here already; true when the box opened. Each is a new
    // place, so the line may go again although the last went unanswered (k.Heard lifts GameLink.Say's repeat guard).
    bool BankFrom(GameLink k, System.Collections.Generic.List<int[]> said, string where) {
        int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        if (said.Exists(s => Tiles(s[0], s[1], x, y) <= 1)) return false;
        if (said.Count > 0) k.Heard("bank");
        said.Add(new[] { x, y });
        byte[] open; lock (actionGate) { Answered(k, AnswerMs); open = SayBank(k); }
        if (open == null) Note("Bank: no answer to \"bank\" " + where + " (" + x + "," + y + ")");
        return open != null;
    }
    // "bank", or the box it opened from here in the last BankOpenFreshMs: the approach opens the box, and the deposit, the
    // take or the put that follows uses it without saying "bank" again.
    byte[] lastBankOpen; System.DateTime lastBankOpenAt; int lastBankX, lastBankY;
    bool BankFresh(GameLink k) { int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; } return lastBankOpen != null && (System.DateTime.UtcNow - lastBankOpenAt).TotalMilliseconds < BankOpenFreshMs && Tiles(x, y, lastBankX, lastBankY) <= 1; }
    byte[] SayBank(GameLink k) {
        if (BankFresh(k)) return lastBankOpen;
        var open = SayFor(k, "bank", 0x24, AnswerMs);
        if (open != null) { lastBankOpen = open; lastBankOpenAt = System.DateTime.UtcNow; lock (k.State.Gate) { lastBankX = k.State.X; lastBankY = k.State.Y; } }
        return open;
    }
    // The toolbag (Damian, 2026-10-18: "smithing, and other circuits, should check the bag in the upper right of the bank
    // box, it is my toolbag where i stash tools i dont always need"): the container in the bank box drawn nearest the box's
    // upper right (the largest x less y of its place in the box), kept by serial once found (<server>toolbag).
    uint Toolbag(GameLink k, uint box) {
        uint saved; AssistItem it;
        if (uint.TryParse(Cfg.Get(Server + "toolbag", ""), System.Globalization.NumberStyles.HexNumber, null, out saved)) lock (k.State.Gate) if (k.State.Items.TryGetValue(saved, out it) && it.Container == box) return saved;
        AssistItem best = null;
        lock (k.State.Gate) foreach (var i in k.State.Items.Values) if (i.Container == box && IsContainer(i.Graphic) && (best == null || i.X - i.Y > best.X - best.Y)) best = i;
        if (best == null) return 0;
        Cfg.Set(Server + "toolbag", best.Serial.ToString("X")); Note("Toolbag: " + best.Serial.ToString("X") + " at " + best.X + "," + best.Y + " in the bank box");
        return best.Serial;
    }
    // Up to want of a tool from the toolbag into the pack, at the bank of the city here. Null once the pack holds want,
    // else why not.
    string ToolsFromBank(GameLink k, System.Func<int, bool> tool, int want, string what) {
        if (PackCount(k, tool) >= want) return null;
        string city = CityOf(k); if (city == null) return "no " + what + " in the pack, and no bank here for the toolbag";
        int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        string why = "no bank known in " + city;
        foreach (var b in CityBanks(city, x, y)) { if (BankSkipped(b)) continue; CircuitStatus = "To the toolbag for " + what; why = ToBanker(k, b); if (why == null) break; SkipBank(b); }
        if (why != null) return "the bank for the toolbag: " + why;
        byte[] open; lock (actionGate) { Answered(k, AnswerMs); open = SayBank(k); }
        if (open == null) return NoBanker();
        uint box = U32(open, 1), pack; lock (k.State.Gate) pack = k.State.Backpack;
        var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
        while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
        uint bag = Toolbag(k, box);
        if (bag != 0) { bool known; lock (contents) known = contents.ContainsKey(bag); if (!known) { BusyWait(clickedAt); Peek(bag); } }
        var take = new System.Collections.Generic.List<uint>(); int need = want - PackCount(k, tool);
        lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (bag != 0 && it.Container == bag && tool(it.Graphic) && take.Count < need) take.Add(it.Serial);
        // Then the rest of the box, its bags two deep (Damian, 2026-10-18: "get tinker tools from bank").
        if (take.Count < need) {
            var tree = new System.Collections.Generic.HashSet<uint> { box };
            for (int depth = 0; depth < 2; depth++) {
                var bags = new System.Collections.Generic.List<uint>();
                lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)) bags.Add(it.Serial);
                foreach (var b in bags) { bool known; lock (contents) known = contents.ContainsKey(b); if (!known) { BusyWait(clickedAt); Peek(b); } tree.Add(b); }
            }
            lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && tool(it.Graphic) && !take.Contains(it.Serial) && take.Count < need) take.Add(it.Serial);
        }
        if (take.Count == 0) return "no " + what + " in the bank box";
        string w = MoveInto(take, pack); if (w != null) return what + " from the toolbag: " + w;
        Note("Tools: " + take.Count + " " + what + " from the toolbag"); return PackCount(k, tool) >= want ? null : "the toolbag held " + take.Count + " " + what + " of " + want;
    }
    // Tinker's tools bought in the town's tinker shops (region kind TINKER), nearest first, each walked into and its keepers
    // named and asked by name (Damian, 2026-10-18: "or tinker with tinker shop search like with smith vendor"). The arts
    // are every 1.25 tile whose name says tinker. Null once the pack holds one, else why not.
    string BuyTinkerTools(GameLink k) {
        string city = CityOf(k); if (city == null) return "no tinker's tools, and no town to buy them in";
        var arts = new System.Collections.Generic.List<int>(); foreach (var t in UoTiles.Items()) if (IsTinkerTool(t.Graphic)) arts.Add(t.Graphic);
        if (arts.Count == 0) return "no tinker's tools art in the tile data";
        var list = new System.Collections.Generic.List<BuyItem> { new BuyItem { Name = "tinker's tools", Arts = arts.ToArray(), Count = 1, MaxPrice = int.MaxValue } };
        var left = new System.Collections.Generic.Dictionary<string, int> { { "tinker's tools", 1 } }; int bought = 0;
        var shops = UoRegions.ShopsIn(city, new[] { "TINKER" }); string end = shops.Count == 0 ? "no tinker shop in " + city : null;
        while (shops.Count > 0 && left["tinker's tools"] > 0 && !circuitStop) {
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            var s = shops[0]; shops.RemoveAt(0);
            end = BuyInShop(k, s, list, left, ref bought); if (end != null) break;
        }
        return PackCount(k, IsTinkerTool) > 0 ? null : "no tinker's tools bought in " + city + (end != null ? ": " + end : "");
    }
    // KitsWant sewing kits from the nearest tailor in reach that sells them (a kit wears out; the circuit stalled with
    // bolts and no kit, 2026-10-17 10:08:36).
    public const int KitsWant = 2;
    string BuyKits(GameLink k, int px, int py) {
        if (Atlas == null) return "no atlas";
        var sellers = Atlas.Shops().FindAll(n => Tiles(n.X, n.Y, px, py) <= CircuitReach && TailorVendor(n) && n.Items.Exists(s => s.IndexOf("sewing kit", System.StringComparison.OrdinalIgnoreCase) >= 0));
        sellers.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        string w = "no tailor in reach sells sewing kits";
        foreach (var v in sellers) {
            if (circuitStop) break; string name = v.Name.Split(' ')[0]; CircuitStatus = "Buying sewing kits from " + name; int got, left;
            w = VendorBuy(g => g == KitArt, KitsWant, out got, out left, name);
            if (w == null && got > 0) return null; if (w != null) Note("Circuit: " + name + ": " + w);
            // A tailor with no kit is an answer, and the next is asked ("in case for some reason that particular vendor
            // doesn't have one"); no proper answer stops the asking.
            if (Silent(w)) return name + ": " + w;
        }
        return w;
    }
    // A bank whose walk or "bank" failed, not tried again for FullMinutes (L-FUTILITY), by its tile.
    readonly System.Collections.Generic.Dictionary<string, System.DateTime> bankSkip = new System.Collections.Generic.Dictionary<string, System.DateTime>();
    // A lost connection is no fault of the bank: the client dropping mid-walk (2026-10-17 01:37:58) marked every Vesper
    // bank failed and the next circuit ended on them.
    void SkipBank(int[] bank) { var k = link; if (k == null || k.Closed) return; lock (bankSkip) bankSkip[bank[0] + "," + bank[1]] = System.DateTime.Now.AddMinutes(FullMinutes); }
    bool BankSkipped(int[] bank) { System.DateTime until; lock (bankSkip) return bankSkip.TryGetValue(bank[0] + "," + bank[1], out until) && System.DateTime.Now < until; }
    string BankHere(GameLink k, string city, int keep = PurseKeep) {
        int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        var banks = CityBanks(city, x, y); if (banks.Count == 0) return "no bank known in " + city;
        string w = "every bank in " + city + " failed within " + FullMinutes + " minutes";
        foreach (var bank in banks) {
            if (circuitStop) return "stopped";
            if (BankSkipped(bank)) continue;
            CircuitStatus = "Walking to the bank at " + bank[0] + "," + bank[1] + " in " + city; w = ToBanker(k, bank);
            if (w != null) { Note("Circuit: walking to the bank at " + bank[0] + "," + bank[1] + ": " + w); SkipBank(bank); continue; }
            int put; w = BankDeposit(keep, out put);
            if (w != null) SkipBank(bank); else BankHeard(bank);
            return w == null ? null : "the bank at " + bank[0] + "," + bank[1] + ": " + w;
        }
        return w;
    }
    static readonly string[] TailorShopKinds = { "CLOTHIER", "WEAVER" }, SellFirst = { "Magincia", "Moonglow" };
    public static readonly string[] MageShopKinds = { "MAGE", "ALCHEMIST", "SCRIBE" };
    // Each circuit's shops, picked by the shop's name and place (Damian, 2026-10-17: "for each, let me pick the list of
    // shops to visit in case we need to adjust on the fly", "not vendor names, but shops"): circuit.shops.<circuit>, shop
    // keys (UoRegions.Shop.Key) separated by ";", in visiting order. Unset or empty for the tailor circuit is every tailor
    // shop; the buy circuit's default is Vesper's mage shop by the bank and Britain's big one in the north (Damian: "the
    // best mage shops are the ones in vesper by the bank and the big one in brit up north").
    public static string[] ShopKindsOf(string circuit) { return circuit == "buy" ? MageShopKinds : circuit == "refresh" ? FoodShopKinds : TailorShopKinds; }
    // The refresh's food shops, in Damian's order (2026-10-17: "use provisioner for apples or pears or a farmers market, or
    // a stable if necessary").
    public static readonly string[] FoodShopKinds = { "PROVISIONER", "FARM", "MARKET", "STABLES" };
    static readonly string[] BuyShopDefault = { "The Circles of Magic in Vesper", "The Sorcerer's Delight in Britain" };
    public static System.Collections.Generic.List<UoRegions.Shop> AllShops(string circuit) {
        var kinds = ShopKindsOf(circuit); var l = new System.Collections.Generic.List<UoRegions.Shop>();
        foreach (var c in UoRegions.CitiesWith(kinds)) if (!Banned(c)) l.AddRange(UoRegions.ShopsIn(c, kinds));
        return l;
    }
    public System.Collections.Generic.List<string> CircuitShopKeys(string circuit) {
        string t = Cfg.Get("circuit.shops." + circuit, null);
        var l = new System.Collections.Generic.List<string>();
        if (t == null && circuit == "buy") { foreach (var name in BuyShopDefault) { var s = AllShops("buy").Find(x => x.Text == name); if (s != null) l.Add(s.Key); } return l; }
        if (t != null) foreach (var e in t.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) l.Add(e.Trim());
        return l;
    }
    public void SetCircuitShopKeys(string circuit, System.Collections.Generic.List<string> keys) { Cfg.Set("circuit.shops." + circuit, string.Join(";", keys)); }
    // The circuit's shops in visiting order; for the tailor circuit with none picked, every tailor shop.
    public System.Collections.Generic.List<UoRegions.Shop> CircuitShops(string circuit) {
        var all = AllShops(circuit); var keys = CircuitShopKeys(circuit);
        if (keys.Count == 0) return circuit == "buy" ? new System.Collections.Generic.List<UoRegions.Shop>() : all;
        var l = new System.Collections.Generic.List<UoRegions.Shop>(); foreach (var key in keys) { var s = all.Find(x => x.Key == key); if (s != null) l.Add(s); }
        return l;
    }
    System.Collections.Generic.List<UoRegions.Shop> TailorShopsIn(string city) { var keys = CircuitShopKeys("tailor"); return UoRegions.ShopsIn(city, TailorShopKinds).FindAll(s => keys.Count == 0 || keys.Contains(s.Key)); }
    System.Collections.Generic.List<string> TailorCities() { return UoRegions.CitiesWith(TailorShopKinds).FindAll(c => TailorShopsIn(c).Count > 0); }
    // A vendor the tailor circuit may deal with: any, with no shops picked; else one inside a picked shop.
    bool TailorVendor(AtlasNpc n) { if (CircuitShopKeys("tailor").Count == 0) return true; foreach (var s in CircuitShops("tailor")) if (s.Holds(n.X, n.Y, 2)) return true; return false; }
    // A shop's avoidance, by its description, kept in shops-<server>.txt (description, tab, ticks it may be asked again).
    readonly System.Collections.Generic.Dictionary<string, System.DateTime> shopAvoid = new System.Collections.Generic.Dictionary<string, System.DateTime>(); bool shopAvoidLoaded;
    string ShopAvoidPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "shops-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    bool ShopOpen(UoRegions.Shop s) {
        lock (shopAvoid) {
            if (!shopAvoidLoaded) { shopAvoidLoaded = true; try { if (System.IO.File.Exists(ShopAvoidPath)) foreach (var line in System.IO.File.ReadAllLines(ShopAvoidPath)) { var t = line.Split('\t'); if (t.Length == 2) shopAvoid[t[0]] = new System.DateTime(long.Parse(t[1])); } } catch { } }
            System.DateTime until; return !shopAvoid.TryGetValue(s.Key, out until) || System.DateTime.Now >= until;
        }
    }
    void AvoidShop(UoRegions.Shop s, System.TimeSpan span) {
        ShopOpen(s);
        lock (shopAvoid) {
            var until = System.DateTime.Now + span; System.DateTime had; if (shopAvoid.TryGetValue(s.Key, out had) && had > until) until = had; shopAvoid[s.Key] = until;
            var lines = new System.Collections.Generic.List<string>(); foreach (var kv in shopAvoid) lines.Add(kv.Key + "\t" + kv.Value.Ticks);
            try { System.IO.File.WriteAllLines(ShopAvoidPath, lines); } catch { }
        }
    }
    // A city's sell score: its best open shop's hours since a sale there (a shop never sold to, the cap), by its weight.
    int ShopScore(string city) {
        double rest = 0; var tailors = CityBuyers(city);
        foreach (var s in TailorShopsIn(city)) {
            if (!ShopOpen(s)) continue; var known = tailors.FindAll(n => s.Holds(n.X, n.Y, 2));
            if (known.Count == 0) { rest = System.Math.Max(rest, RestCapHours); continue; }
            foreach (var n in known) if (PaysFloor(n)) rest = System.Math.Max(rest, RestHours(n));
        }
        return rest <= 0 ? 0 : (int)((1 + rest * 10) * CityWeight(city));
    }
    string SellTrip(GameLink k, out int sold, out int gold, out int thread) {
        sold = 0; gold = 0; thread = 0;
        // Best first: the tailor longest since a sale (Damian, 2026-10-17: "other vendors we haven't seen in hours that likely
        // have available buy power"), by the city's weight, thread there a plus.
        // By the region file's tailor shops, not only the tailors the atlas has met: a shop never entered counts as rested
        // RestCapHours (Damian, 2026-10-17: "there are still half a dozen other tailor shops"; Trinsic's, Vesper's second and
        // Jhelom's second were never visited).
        var cities = new System.Collections.Generic.List<string>(); string here = CityOf(k);
        // Only cities the bank box has a rune for (travel is from the bank's runes): Delucia and Papua have tailor shops
        // and no rune.
        foreach (var c in TailorCities())
            if (c != here && !Banned(c) && BankHasRune(c) && CityWeight(c) > 0 && ShopScore(c) > 0) cities.Add(c);
        // Magincia first, then Moonglow (Damian, 2026-10-17: "next time, try magincia first"; Brian in Moonglow took 90 at
        // 27), then by score.
        cities.Sort((a, b) => { int fa = System.Array.IndexOf(SellFirst, a), fb = System.Array.IndexOf(SellFirst, b); if (fa < 0) fa = SellFirst.Length; if (fb < 0) fb = SellFirst.Length; return fa != fb ? fa.CompareTo(fb) : ShopScore(b).CompareTo(ShopScore(a)); });
        if (cities.Count == 0) return NoSellCity + " that has not offered under " + FloorText();
        string ready = TripReady(k); if (ready != null) return ready;
        foreach (var city in cities) {
            if (circuitStop || PackCount(k, IsShirtArt) < SellBatch) break;
            string w = Travel(k, city); if (TripStops(w)) return w; if (w != null) { Note("Circuit: " + w); continue; }
            int x0, y0; lock (k.State.Gate) { x0 = k.State.X; y0 = k.State.Y; }
            var shops = TailorShopsIn(city); shops.Sort((a, b) => Tiles(a.CX, a.CY, x0, y0).CompareTo(Tiles(b.CX, b.CY, x0, y0)));
            string silent = null;
            foreach (var shop in shops) {
                if (circuitStop || PackCount(k, IsShirtArt) == 0 || !ShopOpen(shop)) continue;
                // A tailor the atlas knows in the shop is asked by name; a shop with none known is walked into, the people
                // in view are named by a click, and its shopkeeper (a worn shop pack) is asked by name. Never "vendor
                // sell": every vendor in earshot answers it (Damian, 2026-10-17: "use the vendors name proper").
                var known = CityBuyers(city).FindAll(n => shop.Holds(n.X, n.Y, 2));
                if (known.Count > 0 && !known.Exists(PaysFloor)) continue;
                // Every tailor in the shop that pays the floor, each by name (Damian sold to every vendor in Morena's shop
                // and in Tate's, 2026-10-17; asking the first alone left the rest of the shop's buying unused).
                var tailors = known.FindAll(PaysFloor);
                if (tailors.Count == 0) {
                    CircuitStatus = "Walking into " + shop.Text; w = WalkTo(shop.CX, shop.CY, 2);
                    if (w != null) { Note("Circuit: walking into " + shop.Text + ": " + w); AvoidShop(shop, System.TimeSpan.FromMinutes(FullMinutes)); continue; }
                    MobileLabels(VendorHear * 4);
                    var keeper = Atlas == null ? null : Atlas.Shops().Find(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && (System.DateTime.Now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
                    if (keeper == null) { Note("Circuit: no shopkeeper in " + shop.Text + " is known by name; not asked"); AvoidShop(shop, System.TimeSpan.FromMinutes(FullMinutes)); continue; }
                    tailors.Add(keeper);
                }
                int shopUnder = 0;
                foreach (var t in tailors) {
                    if (circuitStop || PackCount(k, IsShirtArt) == 0) break;
                    string name = t.Name.Split(' ')[0];
                    CircuitStatus = "Selling shirts to " + name + " in " + shop.Text; int n2, g2, under;
                    w = VendorSell(IsShirtArt, FloorOf, out n2, out g2, out under, name);
                    if (w != null) Note("Circuit: " + shop.Text + ", " + name + ": " + w); sold += n2; gold += g2;
                    if (Silent(w)) { silent = "selling in " + shop.Text + ": " + w; break; }
                    shopUnder = System.Math.Max(shopUnder, under);
                    if (under > 0) UnderFloorOffered(t.Serial); else if (PackCount(k, IsShirtArt) > 0) AvoidTailor(t.Serial, System.TimeSpan.FromMinutes(FullMinutes));
                }
                if (silent != null) break;
                if (shopUnder > 0) AvoidShop(shop, System.TimeSpan.FromMinutes(UnderFloorMinutes)); else if (PackCount(k, IsShirtArt) > 0) AvoidShop(shop, System.TimeSpan.FromMinutes(FullMinutes));
            }
            if (silent != null) return silent;
            // Thread wherever shirts were offered, sold or not, sell-only too (Damian, 2026-10-17: "every single time you go
            // to sell shirts, you should also look to buy threads afterwards"; "flying to magincia and not even looking"),
            // bought while still in the shop, before the walk to the bank ("it is mindblowingly stupid to run back to the
            // bank to deposit gold, only to run back to tailorshop to buy 10 thread when you started in the tailor shop").
            if (!circuitStop && CityOf(k) == city) { int got; string bw = BuyCityThread(k, city, out got); thread += got; if (bw != null) return bw; }
            // Then the gold banked before the city is left (Damian, 2026-10-17, of 1863 gold carried home unbanked from
            // Moonglow: "that was critical at that moment"): every bank of the city.
            // Only above BankOver, and a bank that fails is passed over, the gold carried: banking after every sale ended 3
            // of 7 circuits on 2026-10-17, while Damian banked once at 2042 gold and kept selling.
            if (sold > 0 && PackCount(k, g => g == GoldArt) > BankOver) {
                w = BankHere(k, city);
                if (w != null) Note("Circuit: banking in " + city + ": " + w + "; the gold carried");
            }
            // On to the next city while shirts are left: in sell-only, until none are; else once some sold here.
            if (sellOnly ? PackCount(k, IsShirtArt) == 0 : sold > 0) break;
        }
        if (!circuitStop && !sellOnly && PackCount(k, IsThreadArt) == 0) { int got; string tw = ThreadTrip(k, out got); thread += got; if (tw != null && tw != NoThreadDue) return "thread trip: " + tw; }
        // No trip home for the loom: the circuit weaves at the nearest loom to where the thread was bought (Damian,
        // 2026-10-17: "you didn't need to go to brit and didn't need to use this loom. there were closer one in brit and
        // closer where you bought the last threads").
        return null;
    }
    string ThreadTrip(GameLink k, out int thread) {
        thread = 0;
        var cities = TripCities(k, c => ThreadCandidates(c).FindAll(ThreadDue).Count * CityWeight(c));
        if (cities.Count == 0) return NoThreadDue;
        string ready = TripReady(k); if (ready != null) return ready;
        foreach (var city in cities) {
            if (circuitStop) break;
            string w = Travel(k, city); if (TripStops(w)) return w; if (w != null) { Note("Circuit: " + w); continue; }
            int got; w = BuyCityThread(k, city, out got); thread += got; if (w != null) return w;
            if (got > 0) break;
        }
        return null;
    }
    public const string NoThreadDue = "no city has thread due", NoSellCity = "no other city has a tailor shop";
    // The tour (Damian, 2026-10-17: "we have to visit every tailor shop before making that call, all recently ... go to
    // every tailor shop, buy all the thread they have and sell 1 shirt to each (that will buy them)"): every city with a
    // tailor shop and a rune in the bank box, in the region file's order; in each, every shop walked into, its people named
    // by a click, and each shopkeeper in it asked once to buy one shirt at any price, then for all its thread. The gold over
    // twice the purse is banked before the next recall. Each shop's answer is a line of the report, kept in
    // reports\tour-<server>-<time>.txt; a keeper that gives no proper answer ends the tour (assist-plan.md rule 1).
    public volatile string TourReport = "";
    // TourBankOver: a tour banks only above this, keeping TourGold, so its deposit and its top-up never undo each other.
    public const int TourGoldLow = 500, TourGold = 1000, TourBankOver = 2000;
    // The tour is a cycle (Damian, 2026-10-17: "the point is to keep following the cycle", "not starting from the top every
    // time"): the cities reached in the current lap are kept in tour-lap-<server>.txt, one a line, and a tour goes on with
    // the cities not yet reached; a lap with every city reached starts the next lap.
    string TourLapPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "tour-lap-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    string RunTour(GameLink k, ref int bought, ref int sold, ref int gold) {
        var report = new System.Collections.Generic.List<string>(); var asked = new System.Collections.Generic.HashSet<uint>();
        var cycle = new System.Collections.Generic.List<string>();
        foreach (var c in TailorCities()) if (!Banned(c) && BankHasRune(c)) cycle.Add(c);
        var lap = new System.Collections.Generic.List<string>();
        try { if (System.IO.File.Exists(TourLapPath)) lap.AddRange(System.IO.File.ReadAllLines(TourLapPath)); } catch { }
        var cities = cycle.FindAll(c => !lap.Contains(c));
        if (cities.Count == 0) { lap.Clear(); cities = cycle; Note("Tour: every city reached; a new lap"); }
        else if (lap.Count > 0) Note("Tour: this lap reached " + string.Join(", ", lap) + "; on to " + string.Join(", ", cities));
        string end = null;
        try {
            foreach (var city in cities) {
                if (circuitStop) { end = "stopped"; break; }
                if (CityOf(k) != city) {
                    string ready = TripReady(k); if (ready != null) { end = ready; break; }
                    // Gold for every vendor's thread, from the bank box (Damian, 2026-10-17: "there is plenty of gold in the
                    // bank if you need it").
                    if (PackCount(k, g => g == GoldArt) < TourGoldLow) {
                        int gx, gy; lock (k.State.Gate) { gx = k.State.X; gy = k.State.Y; } var cb = CityOf(k) == null ? new System.Collections.Generic.List<int[]>() : CityBanks(CityOf(k), gx, gy);
                        if (cb.Count > 0) { string gw = ToBanker(k, cb[0]); string taken; if (gw == null) gw = BankTake(new System.Collections.Generic.List<int> { GoldArt }, TourGold, out taken); if (gw != null) { end = "gold from the bank: " + gw; break; } }
                    }
                    CircuitStatus = "Tour: travelling to " + city; string tw = Travel(k, city);
                    if (TripStops(tw)) { end = tw; break; }
                    if (tw != null) { report.Add(city + ": " + tw); lap.Add(city); try { System.IO.File.WriteAllLines(TourLapPath, lap); } catch { } continue; }
                }
                lap.Add(city); try { System.IO.File.WriteAllLines(TourLapPath, lap); } catch { }
                int x0, y0; lock (k.State.Gate) { x0 = k.State.X; y0 = k.State.Y; }
                var shops = TailorShopsIn(city); shops.Sort((a, b) => Tiles(a.CX, a.CY, x0, y0).CompareTo(Tiles(b.CX, b.CY, x0, y0)));
                foreach (var shop in shops) {
                    if (circuitStop) break;
                    CircuitStatus = "Tour: " + shop.Text;
                    int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                    string w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
                    if (w != null) { report.Add(shop.Text + ": not reached: " + w); continue; }
                    MobileLabels(VendorHear * 4);
                    var keepers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && (System.DateTime.Now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
                    if (keepers.Count == 0) { report.Add(shop.Text + ": no shopkeeper known by name"); continue; }
                    foreach (var v in keepers) {
                        if (circuitStop || !asked.Add(v.Serial)) continue;
                        string name = v.Name.Split(' ')[0]; int n2 = 0, g2 = 0, under, got = 0, left; string sw = "no shirt left";
                        if (PackCount(k, IsShirtArt) > 0) { CircuitStatus = "Tour: one shirt to " + name; sw = VendorSell(IsShirtArt, 0, out n2, out g2, out under, name, 1); sold += n2; gold += g2; }
                        if (Silent(sw)) { report.Add(shop.Text + ", " + name + ": " + sw); end = "asking " + name + ": " + sw; break; }
                        CircuitStatus = "Tour: thread from " + name; string bw = VendorBuy(IsThreadArt, 10000, out got, out left, name); bought += got;
                        report.Add(shop.Text + ", " + name + ": " + (n2 > 0 ? "bought a shirt for " + g2 : "shirt: " + (sw ?? "not sold")) + "; thread: " + (bw ?? "sold us " + got));
                        Note("Tour: " + report[report.Count - 1]);
                        if (Silent(bw)) { end = "asking " + name + ": " + bw; break; }
                    }
                    if (end != null) break;
                }
                if (end != null) break;
                if (PackCount(k, g => g == GoldArt) > TourBankOver) { string bw = BankHere(k, city, TourGold); if (bw != null) { end = "banking in " + city + ": " + bw; break; } }
            }
        } finally {
            TourReport = string.Join("\r\n", report);
            try { string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "reports"); System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "tour-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + "-" + System.DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt"), TourReport); } catch { }
            tourMode = false;
        }
        return end ?? (circuitStop ? "stopped" : "tour done: " + report.Count + " shops and keepers");
    }
    // The buy circuit (Damian, 2026-10-17: "a new circuit called "Buy reagents" which takes a shopping list of mage supply
    // stuff (reagents, runes, scrolls) and goes to stores buying them. like a simpler version of the big tailor circuit,
    // this one just buys and stores in the pack in the bank"): the picked shops in their order, lap after lap while a lap
    // buys anything; in each shop every keeper named by a click is asked by name for each item still wanted that its stock
    // list (when read) names, at most the item's highest price; at the bank of every city left, and where the circuit
    // ends, the goods go into the bank (BuyBank). A keeper that gives no proper answer ends it (assist-plan.md rule 1).
    string RunBuy(GameLink k) {
        var list = BuyList(); var left = new System.Collections.Generic.Dictionary<string, int>(); foreach (var i in list) left[i.Name] = i.Count;
        var shops = CircuitShops("buy");
        if (list.Count == 0) { buyMode = false; return "the shopping list is empty"; }
        if (shops.Count == 0) { buyMode = false; return "no shop is picked for the buy circuit"; }
        string end = null; int bought = 0;
        try {
            // The purse first, from the bank of the city here.
            string here0 = CityOf(k); if (here0 != null) { string bw = BuyBank(k, here0); if (bw != null) return "the bank in " + here0 + ": " + bw; }
            for (int lap = 0; end == null && !circuitStop; lap++) {
                int lapStart = bought;
                foreach (var shop in shops) {
                    if (circuitStop) { end = "stopped"; break; }
                    if (!list.Exists(i => left[i.Name] > 0)) break;
                    string city = UoRegions.City(shop.CX, shop.CY);
                    if (city != null && CityOf(k) != city) {
                        string ready = TripReady(k); if (ready != null) { end = ready; break; }
                        string here = CityOf(k);
                        if (here != null) { string bw = BuyBank(k, here); if (bw != null) { end = "the bank in " + here + ": " + bw; break; } }
                        CircuitStatus = "Buy: travelling to " + city; string tw = BankRecall(k, city, false);
                        if (tw != null) { end = "travelling to " + city + ": " + tw; break; }
                        if (CityOf(k) != city) { end = "the rune for " + city + " landed in " + (CityOf(k) ?? "no city"); break; }
                    }
                    end = BuyInShop(k, shop, list, left, ref bought);
                    if (end != null) break;
                }
                if (end == null && !list.Exists(i => left[i.Name] > 0)) end = "every item bought";
                else if (end == null && bought == lapStart) end = "a lap of every picked shop bought nothing more";
            }
            // The goods into the bank where the circuit ends.
            string last = CityOf(k); if (last != null && !circuitStop) { string bw = BuyBank(k, last, true); if (bw != null) end += "; the bank in " + last + ": " + bw; }
        } finally {
            buyMode = false;
            var still = new System.Collections.Generic.List<string>(); foreach (var i in list) if (left[i.Name] > 0) still.Add(left[i.Name] + " " + i.Name);
            Note("Buy: bought " + bought + (still.Count > 0 ? "; still wanted " + string.Join(", ", still) : ""));
        }
        return end ?? (circuitStop ? "stopped" : "done");
    }
    // One shop of a buy: walked into, its people named by a click, and each keeper asked by name for each item still wanted
    // that its stock list (when read) names, at most the item's highest price. Null, or why the buying ends (a keeper that
    // gives no proper answer, assist-plan.md rule 1).
    string BuyInShop(GameLink k, UoRegions.Shop shop, System.Collections.Generic.List<BuyItem> list, System.Collections.Generic.Dictionary<string, int> left, ref int bought) {
        CircuitStatus = "Buy: " + shop.Text;
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        string w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
        if (w != null) { Note("Buy: " + shop.Text + ": not reached: " + w); return null; }
        MobileLabels(VendorHear * 4);
        var keepers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && (System.DateTime.Now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
        if (keepers.Count == 0) { Note("Buy: no shopkeeper in " + shop.Text + " is known by name; not asked"); return null; }
        foreach (var v in keepers) {
            string name = v.Name.Split(' ')[0];
            foreach (var item in list) {
                int want = left[item.Name]; if (want <= 0 || circuitStop) continue;
                // A keeper whose stock list was read and names none of it is not asked.
                string label = UoTiles.Name(item.Arts[0]);
                if (v.Items.Count > 0 && !v.Items.Exists(s => s.StartsWith(item.Name, System.StringComparison.OrdinalIgnoreCase) || s.StartsWith(label, System.StringComparison.OrdinalIgnoreCase))) continue;
                var arts = item.Arts; int got, stock;
                CircuitStatus = "Buy: " + item.Name + " from " + name;
                string bw = VendorBuy(g => System.Array.IndexOf(arts, g) >= 0, want, out got, out stock, name, item.MaxPrice);
                left[item.Name] = want - got; bought += got;
                Note("Buy: " + shop.Text + ", " + name + ", " + item.Name + ": " + (bw ?? "bought " + got + ", " + left[item.Name] + " still wanted"));
                if (Silent(bw)) return "asking " + name + ": " + bw;
            }
        }
        return null;
    }
    // At the city's bank: the bought goods into the bank (into the container in the box already holding the most of them,
    // else the box itself), ReagentRefill of each recall reagent kept in the pack for travel, and the purse brought to
    // BuyPurse: the gold over it onto the box's piles (BankDeposit), the gold short of it from the box's own piles, never a
    // bag's (Damian, 2026-10-17: "keep storing and pulling from the now smaller top level pile of gold"). Null, or why not.
    // The survey (Damian, 2026-10-17: "visit all the vendors in a town and just get their inventory. if not current town,
    // recall there, and end at a bank. no transactions, just data collection"): the town's shops nearest first, each walked
    // into, its people named by a click, and each keeper asked "buy" once by name, which sends its stock and prices to the
    // atlas; nothing is bought. Then the walk to the town's nearest bank, where nothing is said. A keeper that gives no
    // proper answer ends it (assist-plan.md rule 1). Each shop's line goes into reports\survey-<server>-<time>.txt.
    string RunSurvey(GameLink k) {
        var report = new System.Collections.Generic.List<string>(); string end = null; int read = 0;
        string town = SurveyTarget();
        try {
            if (town == null) return "no town to survey: stand in one or pick one";
            if (CityOf(k) != town) {
                string ready = TripReady(k); if (ready != null) return ready;
                CircuitStatus = "Survey: travelling to " + town; string tw = BankRecall(k, town, false);
                if (tw != null) return "travelling to " + town + ": " + tw;
                if (CityOf(k) != town) return "the rune for " + town + " landed in " + (CityOf(k) ?? "no city");
            }
            var left = SurveyShops(); var asked = new System.Collections.Generic.HashSet<uint>();
            while (left.Count > 0 && !circuitStop) {
                int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                int qx = px, qy = py; left.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
                var shop = left[0]; left.RemoveAt(0);
                CircuitStatus = "Survey: " + shop.Text;
                string w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
                if (w != null) { report.Add(shop.Text + ": not reached: " + w); continue; }
                MobileLabels(VendorHear * 4);
                var keepers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && (System.DateTime.Now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
                if (keepers.Count == 0) { report.Add(shop.Text + ": no shopkeeper known by name"); continue; }
                foreach (var v in keepers) {
                    if (circuitStop || !asked.Add(v.Serial)) continue;
                    string name = v.Name.Split(' ')[0]; int got, stock;
                    CircuitStatus = "Survey: " + name + "'s stock in " + shop.Text;
                    // Wanting none of anything: the buy window is read and nothing is picked.
                    string bw = VendorBuy(g => false, 0, out got, out stock, name);
                    if (Silent(bw)) { report.Add(shop.Text + ", " + name + ": " + bw); end = "asking " + name + ": " + bw; break; }
                    if (bw != null && bw.StartsWith("away: ")) { report.Add(shop.Text + ", " + name + ": " + bw); Note("Survey: " + report[report.Count - 1]); continue; }
                    var now = Atlas.Shops().Find(n => n.Serial == v.Serial); int items = now == null ? 0 : now.Items.Count; if (items > 0) read++;
                    report.Add(shop.Text + ", " + name + ": " + items + " items listed" + (items > 0 ? " (" + string.Join(", ", now.Items.GetRange(0, System.Math.Min(4, items))) + (items > 4 ? ", ..." : "") + ")" : ""));
                    Note("Survey: " + report[report.Count - 1]);
                }
                if (end != null) break;
            }
            // End at a bank: walked to, nothing said.
            if (end == null && !circuitStop) {
                int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
                var banks = CityBanks(town, x, y); string bw = banks.Count == 0 ? "no bank known in " + town : null;
                foreach (var b in banks) { if (BankSkipped(b)) continue; CircuitStatus = "Survey: walking to the bank"; bw = ToBanker(k, b, false); if (bw == null) break; SkipBank(b); }
                if (bw != null) report.Add("the bank: " + bw);
            }
        } finally {
            surveyMode = false;
            TourReport = string.Join("\r\n", report);
            try { string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "reports"); System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "survey-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + "-" + System.DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt"), TourReport); } catch { }
            Note("Survey: " + (town ?? "no town") + ", " + read + " stock lists read");
        }
        return end ?? (circuitStop ? "stopped" : "survey of " + town + " done: " + read + " stock lists read");
    }
    // ---------- the refresh circuit ----------
    // (Damian, 2026-10-17: "new circuit: refresh loadout and feed horse and self, dump gold in bank when done. use
    // provisioner for apples or pears or a farmers market, or a stable if necessary. never bails of hay or wheat, for some
    // reason they don't work. eat for self as well. better food is usually better, but not required. get full
    // basically"): food from the city's food shops, the player eats until the server says full, the mount is handed its
    // fruit, and at the bank the gold over the purse goes in and the reagent loadout is refilled from its storage bag.
    // Satiety: each 1998 food's (rundir/scripts.wombat/<item>.m); the server refuses food past 24 ("You are simply too
    // full to eat any more!", eat.m), so FullSatiety from empty fills the player. No hay or wheat is on any list.
    static readonly System.Collections.Generic.Dictionary<int, int> Satiety = new System.Collections.Generic.Dictionary<int, int> {
        { 0x09BB, 25 }, { 0x09BC, 25 }, { 0x097E, 16 }, { 0x09B7, 12 }, { 0x09B8, 12 }, { 0x09C0, 12 }, { 0x09C1, 12 }, { 0x097D, 12 },
        { 0x09E9, 10 }, { 0x09C9, 10 }, { 0x09D3, 10 }, { 0x09F2, 9 }, { 0x0970, 9 }, { 0x0971, 9 }, { 0x0972, 9 }, { 0x0973, 9 },
        { 0x0C6A, 8 }, { 0x0C6B, 8 }, { 0x0C6C, 8 }, { 0x09B6, 6 }, { 0x1040, 6 }, { 0x1041, 6 }, { 0x1721, 6 }, { 0x1722, 6 }, { 0x0993, 6 },
        { 0x0C5C, 5 }, { 0x0C5D, 5 }, { 0x0C74, 5 }, { 0x0C75, 5 }, { 0x103B, 5 }, { 0x0C79, 4 }, { 0x0C7A, 4 }, { 0x097B, 4 }, { 0x103C, 4 },
        { 0x0C7B, 3 }, { 0x0C7C, 3 }, { 0x0C70, 3 }, { 0x0C71, 3 }, { 0x160A, 3 }, { 0x1608, 3 }, { 0x0976, 3 }, { 0x0977, 3 }, { 0x097C, 3 },
        { 0x098C, 3 }, { 0x0C72, 2 }, { 0x0C73, 2 }, { 0x09D0, 2 }, { 0x09EA, 2 }, { 0x09EB, 2 }, { 0x171F, 2 }, { 0x1720, 2 }, { 0x1723, 2 },
        { 0x0994, 1 }, { 0x09D1, 1 }, { 0x09D2, 1 },
    };
    // The mount's: apples and pears, HorseFeed handed over in one stack (the feeding Damian does by hand).
    static readonly int[] HorseFood = { 0x09D0, 0x0994 };
    public const int HorseFeed = 10, FullSatiety = 25, EatMax = 40, EatAnswerMs = 2500, MountWaitMs = 3000;
    static readonly string[] EatLines = { "too full", "stuffed", "You eat the food", "After eating the food", "quite full after", "can't eat that" };
    public volatile string RefreshStatus = "";
    bool Riding(GameLink k) { lock (k.State.Gate) { foreach (var it in k.State.Items.Values) if (it.Layer == MountLayer && it.Mobile == k.State.Player) return true; } return false; }
    uint MountSerial() { lock (pets) { foreach (var p in pets) if (p.Mount) return p.Serial; } return 0; }
    // The satiety the pack's food holds for the player, the mount's fruit left out while a mount is fed.
    int SelfFood(GameLink k, bool mount) { int n = 0; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) { int s; if (inside.Contains(it.Container) && Satiety.TryGetValue(it.Graphic, out s) && !(mount && System.Array.IndexOf(HorseFood, it.Graphic) >= 0)) n += s * System.Math.Max(1, it.Amount); } } return n; }
    // The stocked food of the most satiety, by the art in the vendor's stock boxes (the buy window just read): the stock
    // list's names are the server's ("Loaf of bread", "Cooked ribs") and not the client's ("bread loa", "cut of ribs").
    // 0 when it stocks none.
    int BestFood(GameLink k, uint vendor) {
        var boxes = new System.Collections.Generic.HashSet<uint>(); lock (wornBy) foreach (var kv in wornBy) if (kv.Value == vendor) boxes.Add(kv.Key);
        int best = 0; lock (k.State.Gate) foreach (var it in k.State.Items.Values) { int s; if (boxes.Contains(it.Container) && Satiety.TryGetValue(it.Graphic, out s) && (best == 0 || s > Satiety[best])) best = it.Graphic; }
        return best;
    }
    // ---------- the mining circuit (Damian, 2026-10-18: "tinker shovels, dig and smelt, store the ingots. the target will
    // be any cave, but the circuit needs to be configured for a player's prefered storage"; "the configure needs to see the
    // forge") ----------
    // Set up per server with mine-setup: the forge in view, one dig made by hand (its target relative to the player, land
    // or static), the storage (the vendor's in-progress ingot pile, priced by the player for its final 500; the bank box;
    // or a floor tile) and, for storage or a forge out of walking reach, the bank runes there and back. The cave is where
    // the circuit starts ("Where I start it").
    public const int MinePileMax = 500, MineHop = 4, MineRadius = 24, MineSpotsBadMax = 6, ShovelIngots = 4, MineCursorMs = 3000;
    static bool IsForge(int graphic) { return UoTiles.Name(graphic).ToLowerInvariant().Contains("forge"); }
    // 1EBC "tinker's tools"; the shop and guild signs (0BA7, 0BA8, 0C01, 0C02) and 201D also say tinker.
    static bool IsTinkerTool(int graphic) { string n = UoTiles.Name(graphic).ToLowerInvariant(); return n.Contains("tinker") && n.Contains("tool"); }
    string MineCfg(string key, string def = "") { return Cfg.Get(Server + "mine." + key, def); }
    int MineCfgInt(string key, int def) { return Cfg.GetInt(Server + "mine." + key, def); }
    public string MineSetup(string[] a) {
        var k = link; if (k == null || k.Closed) return "ERROR no game connection";
        string what = a.Length > 1 ? a[1] : "";
        int px, py, pz; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; }
        if (what == "forge") {
            // The forge must be in view: an item, else a static within 12 tiles, the nearest.
            int[] best = null; int bd = int.MaxValue;
            lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (IsForge(g.Graphic) && Tiles(g.X, g.Y, px, py) < bd) { bd = Tiles(g.X, g.Y, px, py); best = new[] { 0, (int)g.Serial, g.X, g.Y, g.Z, g.Graphic }; }
            for (int x = px - 12; x <= px + 12; x++) for (int y = py - 12; y <= py + 12; y++) if (Tiles(x, y, px, py) < bd) foreach (var s in UoMap.StaticsAt(x, y)) if (IsForge(s[0])) { bd = Tiles(x, y, px, py); best = new[] { 1, 0, x, y, s[1], s[0] }; }
            if (best == null) return "REFUSED no forge in view within 12 tiles: stand where you can see it";
            Cfg.Set(Server + "mine.forge", string.Join("|", best));
            return "OK forge " + UoTiles.Name(best[5]) + " " + best[5].ToString("X4") + " at " + best[2] + "," + best[3] + "," + best[4] + (best[0] == 0 ? " (item " + ((uint)best[1]).ToString("X") + ")" : " (static)");
        }
        if (what == "dig") {
            // One dig made by hand: the digging tool double-clicked, then its target.
            var use = Watch(k, 0x06, 120000); if (use == null) return "REFUSED no tool was double-clicked in 2 minutes";
            var tool = Item(k, U32(use, 1)); if (tool == null || !DigTool(tool.Graphic)) return "REFUSED that was not a shovel or a pickaxe";
            var t = Watch(k, 0x6C, 30000); if (t == null) return "REFUSED no target was picked";
            var s = TargetStep(t); if (!s.Ground) return "REFUSED the dig targeted an item, not the ground";
            bool land = s.Graphic == 0 || s.Graphic == UoMap.LandIdAt(s.X, s.Y);
            Cfg.Set(Server + "mine.dig", (s.X - px) + "|" + (s.Y - py) + "|" + (s.Z - pz) + "|" + (land ? 1 : 0) + "|" + s.Graphic);
            return "OK dig " + (s.X - px) + "," + (s.Y - py) + " from the player, " + (land ? "land" : "static " + s.Graphic.ToString("X4"));
        }
        if (what == "storage" && a.Length > 2) {
            if (a[2] == "bank") { Cfg.Set(Server + "mine.store", "bank"); return "OK storage: the bank box"; }
            if (a[2] == "floor") { Cfg.Set(Server + "mine.store", "floor|" + px + "|" + py + "|" + pz); return "OK storage: the floor at " + px + "," + py + "," + pz; }
            if (a[2] == "vendor") {
                // The in-progress ingot pile in an open vendor's pack, nearest the player.
                AssistItem pile = null; uint vendor = 0; int bd = int.MaxValue;
                lock (k.State.Gate) lock (wornBy) foreach (var it in k.State.Items.Values) {
                    uint v; if (it.Graphic != IngotArt || !wornBy.TryGetValue(it.Container, out v)) continue;
                    int[] at; if (!k.State.Mobiles.TryGetValue(v, out at)) continue;
                    if (Tiles(at[0], at[1], px, py) < bd) { bd = Tiles(at[0], at[1], px, py); pile = it; vendor = v; }
                }
                if (pile == null) return "REFUSED no ingot pile in an open vendor's pack: open the vendor first";
                Cfg.Set(Server + "mine.store", "vendor|" + pile.Serial.ToString("X") + "|" + vendor.ToString("X"));
                return "OK storage: the ingot pile " + pile.Serial.ToString("X") + " (" + System.Math.Max(1, pile.Amount) + ") on the vendor " + vendor.ToString("X");
            }
        }
        if ((what == "store-rune" || what == "cave-rune") && a.Length > 2) { string label = string.Join(" ", a, 2, a.Length - 2); Cfg.Set(Server + "mine." + what, label == "none" ? "" : label); return "OK " + what + " " + label; }
        if (what == "shovels" && a.Length > 2) { Cfg.Set(Server + "mine.shovels", a[2]); return "OK shovels " + a[2]; }
        if (what == "store-at" && a.Length > 2) { Cfg.Set(Server + "mine.store-at", a[2]); return "OK store-at " + a[2]; }
        if (what == "tinker" && a.Length > 2) { Cfg.Set(Server + "mine.tinker", string.Join(" ", a, 2, a.Length - 2)); return "OK tinker " + MineCfg("tinker"); }
        return "forge " + MineCfg("forge", "none") + " ; dig " + MineCfg("dig", "none") + " ; storage " + MineCfg("store", "none") + " ; store-rune " + MineCfg("store-rune", "none") + " ; cave-rune " + MineCfg("cave-rune", "none") + " ; shovels " + MineCfgInt("shovels", 2) + " ; store-at " + MineCfgInt("store-at", 500) + " ; tinker " + MineCfg("tinker", "Tools|Shovel");
    }
    // Mounted, the miner leaves the mount at the back of the cave, where the circuit starts, told to stop, and mounts it
    // again when the circuit ends (Damian, 2026-10-18: "if the player is mounted, the character should dismount at the back
    // of the cave and tell the mount to stop. when its time to finalize the circuit, then mount the horse again").
    string RunMine(GameLink k) {
        uint horse = 0;
        if (Riding(k)) { string dw = Dismount(k, out horse); if (dw != null) { mineMode = false; return "dismounting: " + dw; } }
        try { return RunMineOn(k); }
        finally {
            // A circuit stopped (by Stop, the player's own step, or an alarm) walks the player nowhere: the mount is left.
            if (horse != 0 && !k.Closed) {
                if (circuitStop) { int[] at; lock (k.State.Gate) k.State.Mobiles.TryGetValue(horse, out at); Note("Mine: the circuit was stopped; the mount waits" + (at != null ? " at " + at[0] + "," + at[1] : " where it was left")); }
                else { string mw = Remount(k, horse); Note("Mine: " + (mw ?? "mounted again")); }
            }
        }
    }
    // Off the mount, its serial out, told to stop by its name ("all stop" when it has none). Null, or why not.
    string Dismount(GameLink k, out uint horse) {
        horse = MountSerial(); uint me; lock (k.State.Gate) me = k.State.Player;
        var before = new System.Collections.Generic.HashSet<uint>(); lock (k.State.Gate) foreach (var key in k.State.Mobiles.Keys) before.Add(key);
        BusyWait(clickedAt);
        lock (actionGate) { Answered(k, AnswerMs); string why = k.Use(me, false); clickedAt = System.DateTime.UtcNow; if (why != null) return why; }
        var by = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); while (Riding(k) && System.DateTime.UtcNow < by) System.Threading.Thread.Sleep(50);
        if (Riding(k)) return "the dismount did not happen";
        var seenBy = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); bool inView = false; uint h = horse;
        while (!inView && System.DateTime.UtcNow < seenBy) {
            lock (k.State.Gate) {
                if (h != 0 && k.State.Mobiles.ContainsKey(h)) inView = true;
                else foreach (var kv in k.State.Mobiles) if (!before.Contains(kv.Key) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= 2) { h = kv.Key; inView = true; break; }
            }
            if (!inView) System.Threading.Thread.Sleep(50);
        }
        horse = h; if (!inView) return "the mount is not in view after the dismount";
        string name = ""; lock (pets) { var p = PetOf(h); if (p != null) name = p.Name; }
        k.Say((name.Length > 0 ? name : "all") + " stop");
        Note("Mine: dismounted; " + (name.Length > 0 ? name : "the mount") + " told to stop");
        return null;
    }
    // Back to the mount where it stands and onto it. Null, or why not.
    string Remount(GameLink k, uint horse) {
        int[] at; lock (k.State.Gate) { if (!k.State.Mobiles.TryGetValue(horse, out at)) at = null; else at = new[] { at[0], at[1] }; }
        if (at == null) return "the mount " + horse.ToString("X") + " is not in view; it waits where it was left";
        CircuitStatus = "Mine: back to the mount"; string w = WalkTo(at[0], at[1], 1); if (w != null) return "walking to the mount: " + w;
        BusyWait(clickedAt);
        lock (actionGate) { Answered(k, AnswerMs); w = k.Use(horse, false); clickedAt = System.DateTime.UtcNow; if (w != null) return "mounting: " + w; }
        var by = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); while (!Riding(k) && System.DateTime.UtcNow < by) System.Threading.Thread.Sleep(50);
        return Riding(k) ? null : "mounting did not happen; the mount waits beside you";
    }
    string RunMineOn(GameLink k) {
        try {
            var forge = MineCfg("forge").Split('|'); var dig = MineCfg("dig").Split('|'); string store = MineCfg("store");
            if (forge.Length < 6) return "no forge set up (mine-setup forge, with the forge in view)";
            if (dig.Length < 5) return "no dig set up (mine-setup dig, then dig once by hand)";
            if (store.Length == 0) return "no storage set up (mine-setup storage vendor|bank|floor)";
            int sx, sy; lock (k.State.Gate) { sx = k.State.X; sy = k.State.Y; }
            var tried = new System.Collections.Generic.List<int[]> { new[] { sx, sy } };
            int ores = 0, ingotsStored = 0, bad = 0;
            var digKind = Find("Mining (1 tool)");
            while (!circuitStop && !k.Closed) {
                // 1. Shovels.
                if (PackCount(k, DigTool) < MineCfgInt("shovels", 2)) { string tw = TinkerShovels(k); if (tw != null) Note("Mine: tinkering: " + tw); }
                if (PackCount(k, DigTool) == 0) return "no shovel or pickaxe left";
                // 2. A full pack: smelt at the forge, store when enough, back to the cave.
                if (PackNearFull(k)) {
                    int x0, y0; lock (k.State.Gate) { x0 = k.State.X; y0 = k.State.Y; }
                    string sw = MineSmelt(k, forge); if (sw != null) return "smelting: " + sw;
                    if (SmeltIngots(k) >= MineCfgInt("store-at", 500)) { int put; string stw = MineStore(k, store, out put); ingotsStored += put; if (stw != null) return "storing: " + stw; }
                    string bw = MineBack(k, sx, sy, x0, y0); if (bw != null) return "back to the cave: " + bw;
                    continue;
                }
                // 3. One dig.
                var due = lastUse.AddMilliseconds(Delay(digKind));
                while (System.DateTime.UtcNow < due && !circuitStop) System.Threading.Thread.Sleep(50);
                string res = MineDig(k, dig);
                CircuitStatus = "Mine: " + res;
                if (res == "ore") { ores++; bad = 0; continue; }
                if (res == "again" || res == "loosened" || res == "worn") continue;
                // The spot answered no ore, or could not be mined: never dug again; the next spot is walked to.
                if (res != "empty" && ++bad > MineSpotsBadMax) return MineSpotsBadMax + " spots in a row could not be mined; the last: " + res;
                string mw = MineNextSpot(k, sx, sy, tried); if (mw != null) return mw;
            }
            return circuitStop ? "stopped" : "the connection closed";
        } finally { mineMode = false; }
    }
    // One dig at the set-up offset from where the player stands: "ore", "loosened", "empty" (no metal), "worn" (the tool
    // broke), "again" (the server asked to wait), or why the spot could not be mined.
    string MineDig(GameLink k, string[] dig) {
        int dx = int.Parse(dig[0]), dy = int.Parse(dig[1]), dz = int.Parse(dig[2]), graphic = int.Parse(dig[4]); bool land = dig[3] == "1";
        int px, py, pz; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; }
        int tx = px + dx, ty = py + dy, tz = pz + dz, tg = graphic;
        if (land) { tz = UoMap.LandZ(tx, ty); if (graphic != 0) tg = UoMap.LandIdAt(tx, ty); }
        else { var st = UoMap.StaticsAt(tx, ty); var same = st.Find(s => s[0] == graphic) ?? (st.Count > 0 ? st[0] : null); if (same == null) return "no static to dig at " + tx + "," + ty; tg = same[0]; tz = same[1]; }
        AssistItem tool = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if ((inside.Contains(it.Container) || (it.Layer >= 0 && it.Mobile == k.State.Player)) && DigTool(it.Graphic)) { tool = it; break; } }
        if (tool == null) return "worn";
        long mark;
        lock (actionGate) {
            Answered(k, AnswerMs); mark = k.State.JournalSeq;
            string w = k.Use(tool.Serial, true); if (w != null) return "using the tool: " + w;
            lastUse = System.DateTime.UtcNow;
            var until = System.DateTime.UtcNow.AddMilliseconds(MineCursorMs); while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed) System.Threading.Thread.Sleep(20);
            if (!k.MacroCursor) { k.Settle(); foreach (var j in k.State.JournalSince(mark)) foreach (var wl in WaitLines) if (j.StartsWith(wl, System.StringComparison.OrdinalIgnoreCase)) return "again"; return "the tool opened no target cursor"; }
            w = k.Target(true, 0, tx, ty, tz, tg); if (w != null) return "aiming: " + w;
            lastUse = System.DateTime.UtcNow;
        }
        var by = System.DateTime.UtcNow.AddMilliseconds(PendingDigMs);
        while (System.DateTime.UtcNow < by && !circuitStop && !k.Closed) {
            foreach (var j in k.State.JournalSince(mark)) {
                if (j.IndexOf("You dig some", System.StringComparison.OrdinalIgnoreCase) >= 0) return "ore";
                if (j.IndexOf("loosen some rocks", System.StringComparison.OrdinalIgnoreCase) >= 0) return "loosened";
                if (j.IndexOf("no metal", System.StringComparison.OrdinalIgnoreCase) >= 0) return "empty";
                if (j.IndexOf("worn out", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("destroyed the", System.StringComparison.OrdinalIgnoreCase) >= 0) return "worn";
                foreach (var wl in WaitLines) if (j.StartsWith(wl, System.StringComparison.OrdinalIgnoreCase)) return "again";
                if (j.IndexOf("cannot mine", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("too far", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("seen", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("can't mine", System.StringComparison.OrdinalIgnoreCase) >= 0) return "\"" + j + "\"";
            }
            System.Threading.Thread.Sleep(50);
        }
        return "no answer to the dig at " + tx + "," + ty;
    }
    // The next spot: a standable tile at least MineHop from every spot dug, within MineRadius of the start, nearest the
    // player; walked to. A spot the walk cannot reach is marked tried and the next taken.
    string MineNextSpot(GameLink k, int sx, int sy, System.Collections.Generic.List<int[]> tried) {
        for (int attempt = 0; attempt < MineSpotsBadMax && !circuitStop; attempt++) {
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; tried.Add(new[] { px, py }); }
            int[] best = null; int bd = int.MaxValue;
            for (int x = sx - MineRadius; x <= sx + MineRadius; x++) for (int y = sy - MineRadius; y <= sy + MineRadius; y++) {
                if (tried.Exists(t => Tiles(t[0], t[1], x, y) < MineHop)) continue;
                if (UoMap.StandZ(x, y, UoMap.LandStandZ(x, y)) == int.MinValue || Crowded(k, x, y)) continue;
                int d = Tiles(x, y, px, py); if (d < bd) { bd = d; best = new[] { x, y }; }
            }
            if (best == null) return "every spot within " + MineRadius + " tiles of the start has been dug";
            CircuitStatus = "Mine: walking to " + best[0] + "," + best[1];
            string w = WalkTo(best[0], best[1], 0); if (w == null) return null;
            tried.Add(best); Note("Mine: walking to " + best[0] + "," + best[1] + ": " + w);
        }
        return "no spot reached in " + MineSpotsBadMax + " tries";
    }
    // Every ore pile in the pack smelted at the forge, the player within 2 tiles of it.
    string MineSmelt(GameLink k, string[] forge) {
        int fx = int.Parse(forge[2]), fy = int.Parse(forge[3]), fz = int.Parse(forge[4]), fg = int.Parse(forge[5]); bool stat = forge[0] == "1"; uint fs = (uint)int.Parse(forge[1]);
        CircuitStatus = "Mine: walking to the forge"; string w = WalkTo(fx, fy, 2); if (w != null) return "walking to the forge: " + w;
        var smelt = Find("Smelting (one ore at a time)");
        for (int round = 0; round < 200 && !circuitStop && !k.Closed; round++) {
            AssistItem ore = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && IsOre(it.Graphic)) { ore = it; break; } }
            if (ore == null) return null;
            var due = lastUse.AddMilliseconds(Delay(smelt)); while (System.DateTime.UtcNow < due && !circuitStop) System.Threading.Thread.Sleep(50);
            BusyWait(clickedAt); long mark; int ingots = SmeltIngots(k);
            lock (actionGate) {
                Answered(k, AnswerMs); mark = k.State.JournalSeq; CircuitStatus = "Mine: smelting";
                w = k.Use(ore.Serial, true); if (w != null) return "using the ore: " + w;
                clickedAt = lastUse = System.DateTime.UtcNow;
                var until = System.DateTime.UtcNow.AddMilliseconds(MineCursorMs); while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed) System.Threading.Thread.Sleep(20);
                if (!k.MacroCursor) { k.Settle(); string said = null; foreach (var j in k.State.JournalSince(mark)) said = j; if (said != null && System.Array.Exists(WaitLines, wl => said.StartsWith(wl, System.StringComparison.OrdinalIgnoreCase))) continue; return "the ore opened no forge cursor" + (said != null ? "; the server said \"" + said + "\"" : ""); }
                w = k.Target(stat, stat ? 0 : fs, fx, fy, fz, fg); if (w != null) return "aiming at the forge: " + w;
            }
            string res = SmeltResult(k, smelt, mark, ore.Serial, ingots);
            if (res != "smelted" && res != "failed" && res != "again") return res;
        }
        return null;
    }
    // Shovels tinkered from the pack's ingots with tinker's tools until the pack holds the number set up.
    string TinkerShovels(GameLink k) { return TinkerTool(k, MineCfg("tinker", "Tools|Shovel").Split('|'), DigTool, MineCfgInt("shovels", 2), ShovelIngots, "a shovel"); }
    // Tools of a kind tinkered from the pack's ingots with tinker's tools (the menus picked by their text) until the pack
    // holds want; a failed tinker is tried again, any other answer ends it.
    // The toolbag in the bank box is tried first for the tool itself, then for the tinker's tools.
    string TinkerTool(GameLink k, string[] picks, System.Func<int, bool> made, int want, int ingotsMin, string what) {
        bool town = CityOf(k) != null;
        if (town && PackCount(k, made) < want) { string bw = ToolsFromBank(k, made, want, what.Substring(what.IndexOf(' ') + 1)); if (bw == null) return null; Note("Tools: " + bw); }
        if (town && PackCount(k, IsTinkerTool) == 0) { string bw = ToolsFromBank(k, IsTinkerTool, 1, "tinker's tools"); if (bw != null) { Note("Tools: " + bw); bw = BuyTinkerTools(k); if (bw != null) Note("Tools: " + bw); } }
        for (int n = 0; n < 10 && !circuitStop && PackCount(k, made) < want; n++) {
            AssistItem tools = null, ingots = null;
            lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container)) { if (IsTinkerTool(it.Graphic)) tools = it; if (it.Graphic == IngotArt && (ingots == null || it.Amount > ingots.Amount)) ingots = it; } }
            if (tools == null) return "no tinker's tools in the pack";
            if (ingots == null || System.Math.Max(1, ingots.Amount) < ingotsMin) return "fewer than " + ingotsMin + " ingots in the pack";
            int before = PackCount(k, made); BusyWait(clickedAt); long mark;
            lock (actionGate) { Answered(k, AnswerMs); mark = k.State.JournalSeq; CircuitStatus = "Tinkering " + what; string w = Sew(k, tools.Serial, ingots.Serial, picks.Length, (i, m) => MenuPick(m, picks[i])); clickedAt = System.DateTime.UtcNow; if (w != null) return w; }
            var by = System.DateTime.UtcNow.AddMilliseconds(TailorMs); while (PackCount(k, made) == before && System.DateTime.UtcNow < by && !circuitStop) System.Threading.Thread.Sleep(100);
            if (PackCount(k, made) == before) { string said = null; foreach (var j in k.State.JournalSince(mark)) said = j; if (said == null || said.IndexOf("fail", System.StringComparison.OrdinalIgnoreCase) < 0) return "no " + what.Substring(what.IndexOf(' ') + 1) + " made" + (said != null ? ": \"" + said + "\"" : ""); }
        }
        return null;
    }
    // The ingots over the shovels' reserve put where the player keeps them; put: how many.
    string MineStore(GameLink k, string store, out int put) {
        put = 0; var s = store.Split('|'); string rune = MineCfg("store-rune");
        if (rune.Length > 0) { CircuitStatus = "Mine: travelling to " + rune; string tw = BankRecall(k, rune, false); if (tw != null) return "travelling to " + rune + ": " + tw; }
        int reserve = ShovelIngots * MineCfgInt("shovels", 2);
        System.Func<AssistItem> stack = () => { AssistItem b = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic == IngotArt && (b == null || it.Amount > b.Amount)) b = it; } return b; };
        if (s[0] == "bank") {
            string city = CityOf(k); if (city == null) return "not in a city with a bank";
            int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
            string why = "no bank known in " + city; foreach (var b in CityBanks(city, x, y)) { if (BankSkipped(b)) continue; why = ToBanker(k, b); if (why == null) break; SkipBank(b); }
            if (why != null) return "the bank: " + why;
            int before = SmeltIngots(k); string p; string w = BankPut(new System.Collections.Generic.List<int> { IngotArt }, out p); put = before - SmeltIngots(k); return w;
        }
        if (s[0] == "floor") {
            int fx = int.Parse(s[1]), fy = int.Parse(s[2]), fz = int.Parse(s[3]);
            CircuitStatus = "Mine: walking to the storage"; string w = WalkTo(fx, fy, 1); if (w != null) return "walking to the storage: " + w;
            var st = stack(); if (st == null) return null; int amount = System.Math.Max(1, st.Amount) - reserve; if (amount <= 0) return null;
            AssistItem pile = null; lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if (g.Graphic == IngotArt && g.X == fx && g.Y == fy) pile = g;
            w = BusyLift(k, st.Serial, amount); if (w != null) return "lifting the ingots: " + w;
            w = pile != null ? k.Drop(st.Serial, pile.Serial) : k.DropGround(st.Serial, fx, fy, fz); if (w != null) return Stopped("Storing ingots", st.Serial, w);
            Answered(k, AnswerMs); put = amount; Note("Mine: stored " + amount + " ingots at " + fx + "," + fy); return null;
        }
        if (s[0] == "vendor") {
            uint pileSerial = System.Convert.ToUInt32(s[1], 16), vendor = System.Convert.ToUInt32(s[2], 16);
            int[] at; lock (k.State.Gate) { if (!k.State.Mobiles.TryGetValue(vendor, out at)) at = null; else at = new[] { at[0], at[1] }; }
            if (at == null) return "the vendor " + vendor.ToString("X") + " is not in view";
            CircuitStatus = "Mine: walking to the vendor"; string w = WalkTo(at[0], at[1], 1); if (w != null) return "walking to the vendor: " + w;
            if (Item(k, pileSerial) == null) { BusyWait(clickedAt); lock (actionGate) { Answered(k, AnswerMs); k.Use(vendor, false); clickedAt = System.DateTime.UtcNow; } System.Threading.Thread.Sleep(1500); }
            var pile = Item(k, pileSerial); if (pile == null) return "the vendor's ingot pile " + pileSerial.ToString("X") + " is not there";
            int room = MinePileMax - System.Math.Max(1, pile.Amount); if (room <= 0) return "the vendor's pile holds " + MinePileMax + ": start a new in-progress pile and set it up";
            var st = stack(); if (st == null) return null; int amount = System.Math.Min(room, System.Math.Max(1, st.Amount) - reserve); if (amount <= 0) return null;
            int had = pile.Amount;
            w = BusyLift(k, st.Serial, amount); if (w != null) return "lifting the ingots: " + w;
            w = k.Drop(st.Serial, pileSerial); if (w != null) return Stopped("Storing ingots", st.Serial, w);
            Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
            var now = Item(k, pileSerial); if (now == null || now.Amount <= had) return "the ingots did not join the vendor's pile";
            put = now.Amount - had; Note("Mine: " + put + " ingots onto the vendor's pile, now " + now.Amount); return null;
        }
        return "unknown storage " + s[0];
    }
    // Back to the spot the trip left: by the cave's bank rune when one is set up, then walked.
    string MineBack(GameLink k, int sx, int sy, int x0, int y0) {
        string rune = MineCfg("cave-rune");
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        if (rune.Length > 0 && Tiles(px, py, x0, y0) > CircuitReach) { CircuitStatus = "Mine: travelling to " + rune; string tw = BankRecall(k, rune, false); if (tw != null) return "travelling to " + rune + ": " + tw; }
        CircuitStatus = "Mine: walking back to the cave"; return WalkTo(x0, y0, 0);
    }
    // ---------- the smithing circuit (Damian, 2026-10-18: smith the ladder's item for the skill, sell to the smiths in
    // reach, bank gold over 300, tinker a sledge hammer when none, until Blacksmithing reads 100.0; "no skill lock
    // assumption") ----------
    // It works where it starts: the 1998 hammer needs an anvil and a forge within 3 tiles (blacksmith.m Q4ZM). The
    // hammer's menus are walked by their text (SmithPick): "Build Armor", then "Build Ring Armor", "Build Chain Armor" or
    // "Build Plate Armor" ("Build Helmets" under it), then "Build <item>, N metal." A category holding one entry the player
    // can build is flattened into its parent, and an item needing more metal or skill than the player has is left off
    // (blacksmith.m Q4DY and Q4A1). Ingots come from smith.ingots: bank (the city's bank box) or buy (a smith in reach).
    public const int SmithSkill = 7, SmithDone = 1000, SmithIngotTake = 100, SmithSellAt = 5, SmithAgainMax = 3;
    public string SmithIngots { get { return Cfg.Get(Server + "smith.ingots", "bank"); } set { Cfg.Set(Server + "smith.ingots", value == "buy" ? "buy" : "bank"); } }
    // The ingots taken from the bank or bought at a time (Damian, 2026-10-18: "make the ingot count loadout a config in
    // the ui"), SmithIngotTake unless set.
    public int SmithIngotLoad { get { return System.Math.Max(1, Cfg.GetInt(Server + "smith.ingots.load", SmithIngotTake)); } set { Cfg.Set(Server + "smith.ingots.load", System.Math.Max(1, value).ToString()); } }
    public string SmithTinker { get { return Cfg.Get(Server + "smith.tinker", "Tools|Sledge"); } set { Cfg.Set(Server + "smith.tinker", value); } }
    static bool SmithTool(int g) { string n = UoTiles.Name(g).ToLowerInvariant(); return n.Contains("smith") || n.Contains("sledge") || n == "tongs"; }
    // A menu entry or ladder step as one name: "Build Platemail gorget, 10 metal." and "plate gorget" are both
    // "plate gorget"; the menu's plate tunic is "Platemail".
    static string SmithNorm(string s) {
        s = s.Trim().ToLowerInvariant(); if (s.StartsWith("build ")) s = s.Substring(6);
        int cut = s.IndexOfAny(new[] { ',', ':' }); if (cut >= 0) s = s.Substring(0, cut);
        s = s.Replace("  ", " ").Trim(); if (s == "platemail") return "plate tunic";
        return s.StartsWith("platemail ") ? "plate " + s.Substring(10) : s;
    }
    // The hammer's menu, blacksmith.m Q4Y8: each category's path and the arts built under it; an item's name is its 1.25
    // tile data name ("katana%s%" is "katana").
    static readonly object[][] SmithMenu = {
        new object[] { new[] { "build armor", "build ring armor" }, new[] { 0x13EB, 0x13EF, 0x13F0, 0x13EC } },
        new object[] { new[] { "build armor", "build chain armor" }, new[] { 0x13BB, 0x13BE, 0x13BF } },
        new object[] { new[] { "build armor", "build plate armor", "build helmets" }, new[] { 0x140A, 0x140C, 0x140E, 0x1408, 0x1412 } },
        new object[] { new[] { "build armor", "build plate armor" }, new[] { 0x1413, 0x1414, 0x1410, 0x1411, 0x1415, 0x1C04 } },
        new object[] { new[] { "build shields" }, new[] { 0x1B73, 0x1B72, 0x1B7B, 0x1B78, 0x1B74, 0x1B76 } },
        new object[] { new[] { "build weapons", "build blades" }, new[] { 0x0F51, 0x1441, 0x13FF, 0x1401, 0x13B6, 0x0F5E, 0x0F61, 0x13B9 } },
        new object[] { new[] { "build weapons", "build axes" }, new[] { 0x0F47, 0x0F49, 0x0F45, 0x1443, 0x0F4B, 0x13FB, 0x13B0 } },
        new object[] { new[] { "build weapons", "build pole arms" }, new[] { 0x1403, 0x0F62, 0x1405, 0x0F4D, 0x143F } },
        new object[] { new[] { "build weapons", "build bludgeoning weapons" }, new[] { 0x0F5C, 0x143B, 0x1407, 0x1439, 0x143D } },
    };
    // A tile data name read as a player writes it: "rub%ies/y%" is "ruby", "katana%s%" is "katana".
    static string TileWord(string n) {
        int a;
        while ((a = n.IndexOf('%')) >= 0) { int b = n.IndexOf('%', a + 1); if (b < 0) break; string inner = n.Substring(a + 1, b - a - 1); int sl = inner.IndexOf('/'); n = n.Substring(0, a) + (sl >= 0 ? inner.Substring(sl + 1) : "") + n.Substring(b + 1); }
        return n;
    }
    static System.Collections.Generic.Dictionary<string, string[]> smithPaths;
    // Every smithable item by name (SmithNorm of its tile name), with its path; null before the tile data is read.
    public static System.Collections.Generic.Dictionary<string, string[]> SmithItems() {
        if (smithPaths != null) return smithPaths;
        if (UoTiles.Items().Count == 0) return null;
        var d = new System.Collections.Generic.Dictionary<string, string[]>();
        foreach (var e in SmithMenu) foreach (int art in (int[])e[1]) { string n = SmithNorm(TileWord(UoTiles.Name(art))); if (n.Length > 0 && !d.ContainsKey(n)) d[n] = (string[])e[0]; }
        return smithPaths = d;
    }
    // The categories above an item, outermost first; null for an item the circuit has no path for.
    static System.Collections.Generic.List<string> SmithChain(string n) {
        var items = SmithItems(); string[] path; if (items != null && items.TryGetValue(n, out path)) return new System.Collections.Generic.List<string>(path);
        var c = new System.Collections.Generic.List<string>();
        if (n.Contains("shield") || n.Contains("buckler")) { c.Add("build shields"); return c; }
        c.Add("build armor");
        if (n.StartsWith("ringmail")) c.Add("build ring armor");
        else if (n.StartsWith("chainmail")) c.Add("build chain armor");
        else if (n.StartsWith("plate") || n.Contains("helm") || n.Contains("bascinet")) { c.Add("build plate armor"); if (n.Contains("helm") || n.Contains("bascinet")) c.Add("build helmets"); }
        else return null;
        return c;
    }
    readonly System.Collections.Generic.Dictionary<string, int> smithCost = new System.Collections.Generic.Dictionary<string, int>();
    int SmithCostOf(string item) {
        string n = SmithNorm(item); int c; if (smithCost.TryGetValue(n, out c)) return c;
        foreach (var r in SmithDifficulty) if (SmithNorm(r[0]) == n) return int.Parse(r[1]);
        return 25;
    }
    // One build's menus under actionGate: the hammer used, each menu answered with the item when it lists it, else with the
    // deepest of the item's categories it lists. Null once the item is picked (art: its model), "again" on a wait line, or
    // why not; a menu offering neither is cancelled.
    string SmithPick(GameLink k, uint hammer, string item, long mark, out int art) {
        art = 0; string want = SmithNorm(item); var chain = SmithChain(want); if (chain == null) return "no menu path is known for " + item;
        byte[] menu = null; System.Action<byte[]> seen = p => { if (p[0] == 0x7C) menu = p; };
        k.ClaimMenu = true; k.ServerPacket += seen;
        try {
            craftTool = null; k.Settle(); string w = k.Use(hammer, false); if (w != null) return w;
            for (int depth = 0; depth < SmithPicksMax; depth++) {
                var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs);
                while (menu == null && System.DateTime.UtcNow < by && !k.Closed) System.Threading.Thread.Sleep(20);
                var m = menu; menu = null;
                if (m == null) {
                    string said = null; foreach (var j in k.State.JournalSince(mark)) said = j;
                    if (said != null && System.Array.Exists(WaitLines, wl => said.StartsWith(wl, System.StringComparison.OrdinalIgnoreCase))) return "again";
                    return said != null ? "\"" + said + "\"" : "the hammer opened no menu";
                }
                string text = null; int deepest = -1;
                for (int i = 1; ; i++) {
                    string t = MenuItem(m, i); if (t == null) break;
                    // An item names its metal; a category ("Build Plate Armor") names none.
                    if (SmithNorm(t) == want && MetalCost(t) > 0) { text = t; deepest = int.MaxValue; break; }
                    int c = chain.IndexOf(t.Trim().ToLowerInvariant()); if (c > deepest) { deepest = c; text = t; }
                }
                if (text == null) {
                    var cancel = new byte[13]; cancel[0] = 0x7D; System.Array.Copy(m, 3, cancel, 1, 6); k.Settle(); k.Inject(cancel, "menu cancel", false);
                    return "the hammer's menu offers neither " + item + " nor its category (too few ingots or too little skill)";
                }
                var r = MenuPick(m, text); k.Settle(); w = k.Inject(r, "menu pick " + ((r[7] << 8) | r[8]), false); if (w != null) return w;
                if (deepest == int.MaxValue) { art = (r[9] << 8) | r[10]; int cost = MetalCost(text); if (cost > 0) smithCost[want] = cost; lastBuild = SmithBook.Display(text); lastBuildAt = System.DateTime.UtcNow; return null; }
            }
            return "no item after " + SmithPicksMax + " menus";
        } finally { k.ServerPacket -= seen; k.ClaimMenu = false; }
    }
    // The 1998 smithy (blacksmith.m Q4ZM): an anvil is 0FAF or 0FB0, a forge 0FB1 or 197A..19A9, each within 3 tiles of
    // the smith.
    static bool IsAnvil(int g) { return g == 0x0FAF || g == 0x0FB0; }
    static bool IsSmithForge(int g) { return g == 0x0FB1 || (g >= 0x197A && g <= 0x19A9); }
    public const int SmithyReach = 3, SmithyScan = 60;
    // Every anvil and forge known near (x, y): those the atlas recorded as world items (Damian, 2026-10-18: "integrate with
    // the atlas which should record where anvils are"), and the map's statics within SmithyScan tiles.
    System.Collections.Generic.List<int[]> SmithyParts(int x, int y, bool anvils) {
        var l = new System.Collections.Generic.List<int[]>();
        if (Atlas != null) foreach (var p in Atlas.Places()) if (p.Kind == (anvils ? "Anvil" : "Forge") && Tiles(p.X, p.Y, x, y) <= SmithyScan) l.Add(new[] { p.X, p.Y });
        var k = link; if (k != null) lock (k.State.Gate) foreach (var g in k.State.Ground.Values) if ((anvils ? IsAnvil(g.Graphic) : IsSmithForge(g.Graphic)) && !l.Exists(t => t[0] == g.X && t[1] == g.Y)) l.Add(new[] { g.X, g.Y });
        for (int tx = x - SmithyScan; tx <= x + SmithyScan; tx++) for (int ty = y - SmithyScan; ty <= y + SmithyScan; ty++)
            foreach (var s in UoMap.StaticsAt(tx, ty)) if (anvils ? IsAnvil(s[0]) : IsSmithForge(s[0])) { l.Add(new[] { tx, ty }); break; }
        return l;
    }
    // The spot to smith from: the player's tile when an anvil and a forge are both within SmithyReach of it; else the tile
    // between the nearest anvil with a forge within 2 * SmithyReach of it and that forge, walked to. Null, or why not.
    // Minoc's anvils and forges are server items, none in the map's statics (2026-10-18: 16377 statics around its bank,
    // no anvil or forge among them), and the client is sent an item only in view. So the town's smith shops and forges
    // (the region file's BLACKSMITH, ARMORER and WEAPONRY shops and its miners' guilds; Damian, 2026-10-18: "make it check
    // forges and smith shops to inform the atlas") are walked into, nearest first, each one the atlas holds no anvil or
    // forge for, the items in view there going into the atlas; then the search runs again.
    static readonly string[] SmithyShopKinds = { "BLACKSMITH", "ARMORER", "WEAPONRY", "MINERSGUILD" };
    readonly System.Collections.Generic.HashSet<string> smithyLooked = new System.Collections.Generic.HashSet<string>();
    string ToSmithy(GameLink k, out int sx, out int sy) {
        string w = ToSmithyOnce(k, out sx, out sy); if (w == null) return w;
        string city = CityOf(k); if (city == null) return w + "; not in a town";
        var shops = UoRegions.ShopsIn(city, SmithyShopKinds); var known = Atlas == null ? new System.Collections.Generic.List<AtlasPlace>() : Atlas.Places().FindAll(p => p.Kind == "Anvil" || p.Kind == "Forge");
        shops.RemoveAll(s => smithyLooked.Contains(s.Key) || known.Exists(p => s.Holds(p.X, p.Y, 1)));
        int looked = 0;
        while (shops.Count > 0 && !circuitStop) {
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            var s = shops[0]; shops.RemoveAt(0); smithyLooked.Add(s.Key); looked++;
            CircuitStatus = "Smithing: looking for an anvil in " + s.Text; string ww = WalkTo(s.CX, s.CY, 2);
            if (ww != null) { Note("Smithing: " + s.Text + ": not reached: " + ww); continue; }
            System.Threading.Thread.Sleep(AnswerMs);
            int anvils = SmithyParts(s.CX, s.CY, true).FindAll(a => s.Holds(a[0], a[1], 1)).Count, forges = SmithyParts(s.CX, s.CY, false).FindAll(f => s.Holds(f[0], f[1], 1)).Count;
            Note("Smithing: " + s.Text + ": " + anvils + " anvils, " + forges + " forges");
        }
        if (looked == 0) return w + "; every smith shop in " + city + " was looked in";
        return ToSmithyOnce(k, out sx, out sy);
    }
    string ToSmithyOnce(GameLink k, out int sx, out int sy) {
        lock (k.State.Gate) { sx = k.State.X; sy = k.State.Y; }
        int px = sx, py = sy; var anvils = SmithyParts(px, py, true); var forges = SmithyParts(px, py, false);
        if (anvils.Exists(a => Tiles(a[0], a[1], px, py) <= SmithyReach) && forges.Exists(f => Tiles(f[0], f[1], px, py) <= SmithyReach)) return null;
        var pairs = new System.Collections.Generic.List<int[]>();
        foreach (var a in anvils) { int[] near = null; foreach (var f in forges) if (Tiles(a[0], a[1], f[0], f[1]) <= 2 * SmithyReach && (near == null || Tiles(a[0], a[1], f[0], f[1]) < Tiles(a[0], a[1], near[0], near[1]))) near = f; if (near != null) pairs.Add(new[] { (a[0] + near[0]) / 2, (a[1] + near[1]) / 2 }); }
        if (pairs.Count == 0) return "no anvil with a forge beside it known within " + SmithyScan + " tiles (" + anvils.Count + " anvils, " + forges.Count + " forges)";
        pairs.Sort((a, b) => Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        string w = null;
        foreach (var s in pairs) { CircuitStatus = "Smithing: walking to the anvil at " + s[0] + "," + s[1]; w = WalkTo(s[0], s[1], 1); if (w == null) { lock (k.State.Gate) { sx = k.State.X; sy = k.State.Y; } return null; } Note("Smithing: the anvil at " + s[0] + "," + s[1] + ": " + w); }
        return "no anvil reached: " + w;
    }
    // What the circuit smiths for (Damian, 2026-10-18: "make it an option to build for other reasons like pure profit or to
    // make suits and gear for people and sales on merchants. like a ring suit, a five pack of kats or spears"): skill, the
    // ladder until 100.0; profit, the item of the most gold per ingot the smithing book knows, sold to NPCs until the
    // ingots run out; order, the order list, SmithPickOf times each item built and the best kept by NPC price.
    public string SmithGoal { get { return Cfg.Get(Server + "smith.goal", "skill"); } set { Cfg.Set(Server + "smith.goal", value == "profit" || value == "order" ? value : "skill"); } }
    public string SmithOrder { get { return Cfg.Get(Server + "smith.order", ""); } set { Cfg.Set(Server + "smith.order", value ?? ""); } }
    public int SmithPickOf { get { return System.Math.Max(1, Cfg.GetInt(Server + "smith.pick", 2)); } set { Cfg.Set(Server + "smith.pick", System.Math.Max(1, value).ToString()); } }
    public static readonly string[][] SmithOrderPresets = {
        new[] { "Ring suit", "ringmail tunic@1;ringmail leggings@1;ringmail sleeves@1;ringmail gloves@1" },
        new[] { "Chain suit", "chainmail coif@1;chainmail tunic@1;chainmail leggings@1;ringmail sleeves@1;ringmail gloves@1" },
        new[] { "Plate suit", "plate helm@1;plate gorget@1;plate arms@1;plate gloves@1;plate legs@1;plate tunic@1" },
        new[] { "Five katanas", "katana@5" }, new[] { "Five spears", "spear@5" },
    };
    // The order as name and count, "item@count;..."; an item the hammer's menu does not hold is left out and said.
    System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> SmithOrderLines(out string bad) {
        var l = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(); var no = new System.Collections.Generic.List<string>();
        foreach (var e in SmithOrder.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) {
            int at = e.LastIndexOf('@'); int n = 1; string name = SmithNorm(at > 0 ? e.Substring(0, at) : e);
            if (at > 0 && !int.TryParse(e.Substring(at + 1).Trim(), out n)) n = 1;
            if (SmithChain(name) == null) no.Add(name); else if (n > 0) l.Add(new System.Collections.Generic.KeyValuePair<string, int>(name, n));
        }
        bad = no.Count > 0 ? "not on the hammer's menu: " + string.Join(", ", no) : null; return l;
    }
    // The smithable item of the most gold per ingot near this skill, from the smithing book; null when none has an NPC price.
    string SmithProfitItem(int skill) {
        if (Smith == null) return null;
        foreach (var r in Smith.Rows(skill)) if (r.PerIngot > 0 && SmithChain(SmithNorm(r.Item)) != null) return SmithNorm(r.Item);
        return null;
    }
    string RunSmithing(GameLink k) {
        string city = CityOf(k); var made = new System.Collections.Generic.HashSet<int>();
        int built = 0, failed = 0, sold = 0, goldIn = 0, again = 0, from = SkillOf(k, SmithSkill);
        string end = null; int sx, sy;
        string goal = SmithGoal, badOrder = null; var order = goal == "order" ? SmithOrderLines(out badOrder) : null; int line = 0, pick = SmithPickOf;
        var lineBuilt = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<uint>>();
        var seen = new System.Collections.Generic.HashSet<uint>(); lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container)) seen.Add(it.Serial); }
        try {
            if (badOrder != null) Note("Smithing: " + badOrder);
            if (order != null && order.Count == 0) return "the order holds nothing the hammer can build";
            end = ToSmithy(k, out sx, out sy); if (end != null) return end;
            while (end == null && !circuitStop && !k.Closed) {
                int skill = SkillOf(k, SmithSkill); string item;
                if (goal == "order") {
                    while (line < order.Count && lineBuilt.ContainsKey(order[line].Key) && lineBuilt[order[line].Key].Count >= order[line].Value * pick) line++;
                    if (line == order.Count) { end = "the order is built"; break; }
                    item = order[line].Key;
                } else if (goal == "profit") {
                    item = SmithProfitItem(skill); if (item == null) { end = "no item you can smith has an NPC price yet (Maker tab, Smithing value)"; break; }
                } else {
                    if (skill >= SmithDone) { end = "Blacksmithing reads " + (skill / 10.0).ToString("0.0"); break; }
                    item = SmithNow(skill); if (item == null) { end = "the ladder has no item for " + (skill / 10.0).ToString("0.0"); break; }
                }
                int cost = SmithCostOf(item);
                // Sold at 5 made, or before a trip for ingots; the gold over the purse banked above BankOver. An order's
                // pieces are kept until it is built.
                if (goal != "order" && made.Count > 0 && (PackCount(k, g => made.Contains(g)) >= SmithSellAt || PackIngots(k) < cost)) { end = SmithSell(k, made, sx, sy, ref sold, ref goldIn); if (end != null) break; }
                if (city != null && PackCount(k, g => g == GoldArt) > BankOver) { CircuitStatus = "Smithing: banking the gold"; string bw = BankHere(k, city); if (bw != null) Note("Smithing: " + bw); string ww = WalkTo(sx, sy, 0); if (ww != null) { end = "back to the anvil: " + ww; break; } }
                if (PackIngots(k) < cost) { end = SmithIngotsIn(k, city, cost, sx, sy); if (end != null) break; }
                if (PackCount(k, SmithTool) == 0) {
                    string tw = TinkerTool(k, SmithTinker.Split('|'), SmithTool, 1, 1, "a sledge hammer"); if (tw != null) { end = "no smith's hammer, and tinkering one: " + tw; break; }
                    string ww = WalkTo(sx, sy, 0); if (ww != null) { end = "back to the anvil: " + ww; break; }
                }
                AssistItem hammer = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if ((inside.Contains(it.Container) || (it.Layer >= 0 && it.Mobile == k.State.Player)) && SmithTool(it.Graphic)) { hammer = it; break; } }
                if (hammer == null) { end = "no smith's hammer in the pack"; break; }
                CircuitStatus = "Smithing: " + item + " at " + (skill / 10.0).ToString("0.0") + " (" + PackIngots(k) + " ingots)";
                BusyWait(clickedAt); long mark; string w; int art;
                lock (actionGate) { Answered(k, AnswerMs); mark = k.State.JournalSeq; w = SmithPick(k, hammer.Serial, item, mark, out art); clickedAt = System.DateTime.UtcNow; }
                if (w == "again") { if (++again > SmithAgainMax) { end = "the server asked to wait " + again + " times in a row"; break; } System.Threading.Thread.Sleep(BusyMs); continue; }
                again = 0;
                if (w != null) { end = item + ": " + w; break; }
                if (art != 0) made.Add(art);
                string got = CraftResult(k, mark, true);
                if (got == "failed") failed++; else if (got == "made" || got == "exceptional") built++;
                else { end = item + ": " + got; break; }
                // An order's piece by its serial: the new item of the art picked in the pack.
                if (goal == "order" && got != "failed") {
                    uint piece = 0;
                    for (int look = 0; look < 5 && piece == 0; look++) {
                        lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic == art && !seen.Contains(it.Serial)) { piece = it.Serial; break; } }
                        if (piece == 0) System.Threading.Thread.Sleep(200);
                    }
                    if (piece == 0) { end = item + ": made, but no new " + item + " is in the pack"; break; }
                    seen.Add(piece); if (!lineBuilt.ContainsKey(item)) lineBuilt[item] = new System.Collections.Generic.List<uint>(); lineBuilt[item].Add(piece);
                }
            }
            if (end == null) end = circuitStop ? "stopped" : "the connection closed";
            // Ended on its own: what was made sold, then the gold over the purse and the ingots left into the bank (Damian,
            // 2026-10-18: "this circuit ended without selling the last legs made and banking the gold and excess iron").
            if (!circuitStop && !k.Closed) {
                // A smith that gave no proper answer is not asked again (L-FUTILITY).
                string sw = made.Count == 0 || end.StartsWith("asking ") ? null : goal == "order" ? OrderPick(k, order, lineBuilt, made, sx, sy, ref sold, ref goldIn) : SmithSell(k, made, sx, sy, ref sold, ref goldIn);
                if (sw != null) end += "; " + (goal == "order" ? "" : "selling: ") + sw;
                if (city != null && !circuitStop) {
                    CircuitStatus = "Smithing: to the bank"; string bw = BankHere(k, city);
                    if (bw == null && PackIngots(k) > 0) { string put; bw = BankPut(new System.Collections.Generic.List<int> { IngotArt }, out put); }
                    end += bw == null ? "; the gold and ingots banked" : "; the bank: " + bw;
                }
            }
            return end;
        } finally {
            smithMode = false;
            SmithStatus = built + " made, " + failed + " failed, " + sold + " sold for " + goldIn + " gold; Blacksmithing " + (from / 10.0).ToString("0.0") + " to " + (SkillOf(k, SmithSkill) / 10.0).ToString("0.0") + (end != null ? "; " + end : "");
            Note("Smithing: " + SmithStatus);
        }
    }
    public volatile string SmithStatus = "";
    // An order's pick (Damian, 2026-10-18: "when making for sale to people (not npc) we pick only those items that have the
    // best resale value at npc for a check of the hidden quality we can't see"): the nearest smith in reach is asked "sell"
    // and its list is read and answered with nothing; each order line keeps its count of pieces at the highest prices, and
    // the rest are sold to that smith. The kept pieces stay in the pack. What was kept and sold comes back.
    string OrderPick(GameLink k, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> order, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<uint>> built, System.Collections.Generic.HashSet<int> made, int sx, int sy, ref int sold, ref int goldIn) {
        var smiths = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && Tiles(n.X, n.Y, sx, sy) <= CircuitReach && n.Items.Exists(s => SmithChain(SmithNorm(s)) != null));
        smiths.Sort((a, b) => Tiles(a.X, a.Y, sx, sy).CompareTo(Tiles(b.X, b.Y, sx, sy)));
        if (smiths.Count == 0) return "no smith in reach to read prices from; every piece kept";
        string name = smiths[0].Name.Split(' ')[0];
        string near = NearVendor(k, name); if (near != null) return "reading prices at " + name + ": " + near + "; every piece kept";
        byte[] list; lock (actionGate) { Answered(k, AnswerMs); CircuitStatus = "Smithing: reading " + name + "'s prices"; list = AskNear(k, name, "sell", 0x9E); }
        if (list == null || list.Length < 9) return "asking " + name + ": " + NoAnswer(name, "sell") + "; every piece kept";
        var price = new System.Collections.Generic.Dictionary<uint, int>(); int n = (list[7] << 8) | list[8], at = 9;
        for (int i = 0; i < n && at + 14 <= list.Length; i++) { uint s = U32(list, at); int len = (list[at + 12] << 8) | list[at + 13]; price[s] = (list[at + 10] << 8) | list[at + 11]; at += 14 + len; }
        var keep = new System.Collections.Generic.HashSet<uint>(); var mine = new System.Collections.Generic.HashSet<uint>(); var parts = new System.Collections.Generic.List<string>();
        foreach (var o in order) {
            System.Collections.Generic.List<uint> pieces; if (!built.TryGetValue(o.Key, out pieces) || pieces.Count == 0) { parts.Add(o.Key + ": none built"); continue; }
            System.Func<uint, int> p = s => { int v; return price.TryGetValue(s, out v) ? v : -1; };
            var sorted = new System.Collections.Generic.List<uint>(pieces); sorted.Sort((a, b) => p(b).CompareTo(p(a)));
            var kept = sorted.GetRange(0, System.Math.Min(o.Value, sorted.Count)); foreach (var s in kept) keep.Add(s); foreach (var s in pieces) mine.Add(s);
            parts.Add(o.Key + ": kept " + kept.Count + " of " + o.Value + " (" + name + " pays " + string.Join(", ", kept.ConvertAll(s => p(s) < 0 ? "?" : p(s).ToString())) + "), " + (pieces.Count - kept.Count) + " to sell");
        }
        Note("Smithing: order pick: " + string.Join("; ", parts));
        if (mine.Count > keep.Count) {
            int s2, gd, under; string w = VendorSell(g => made.Contains(g), g => 1, out s2, out gd, out under, name, int.MaxValue, s => mine.Contains(s) && !keep.Contains(s));
            sold += s2; goldIn += gd; if (w != null) parts.Add("selling the rest to " + name + ": " + w);
        }
        return string.Join("; ", parts);
    }
    // The made items sold to each smith in reach of the anvil whose stock list names armour, by name, 5 a sale at any
    // price; then back to the anvil. Null, or why the circuit ends.
    string SmithSell(GameLink k, System.Collections.Generic.HashSet<int> made, int sx, int sy, ref int sold, ref int goldIn) {
        var smiths = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && Tiles(n.X, n.Y, sx, sy) <= CircuitReach && n.Items.Exists(s => SmithChain(SmithNorm(s)) != null));
        smiths.Sort((a, b) => Tiles(a.X, a.Y, sx, sy).CompareTo(Tiles(b.X, b.Y, sx, sy)));
        if (smiths.Count == 0) { Note("Smithing: no smith in reach is known to deal in armour; nothing sold"); return null; }
        foreach (var v in smiths) {
            if (circuitStop || PackCount(k, g => made.Contains(g)) == 0) break;
            string name = v.Name.Split(' ')[0]; int s, gd, under;
            CircuitStatus = "Smithing: selling to " + name;
            string w = VendorSell(g => made.Contains(g), 1, out s, out gd, out under, name);
            sold += s; goldIn += gd; Note("Smithing: " + name + ": " + (w ?? "sold " + s + " for " + gd));
            if (Silent(w)) return "asking " + name + ": " + w;
        }
        string back = WalkTo(sx, sy, 0); return back == null ? null : "back to the anvil: " + back;
    }
    // Ingots for the next build: from the city's bank box (the gold over the purse banked on the same visit), or bought
    // from a smith in reach whose stock lists iron ingots; then back to the anvil. Null, or why the circuit ends.
    string SmithIngotsIn(GameLink k, string city, int need, int sx, int sy) {
        if (SmithIngots == "buy") {
            var sellers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && Tiles(n.X, n.Y, sx, sy) <= CircuitReach && n.Items.Exists(s => s.StartsWith("Iron ingot", System.StringComparison.OrdinalIgnoreCase)));
            sellers.Sort((a, b) => Tiles(a.X, a.Y, sx, sy).CompareTo(Tiles(b.X, b.Y, sx, sy)));
            foreach (var v in sellers) {
                if (circuitStop || PackIngots(k) >= need) break;
                string name = v.Name.Split(' ')[0]; int got, left; CircuitStatus = "Smithing: buying ingots from " + name;
                string w = VendorBuy(g => g == IngotArt, SmithIngotLoad, out got, out left, name); Note("Smithing: ingots from " + name + ": " + (w ?? "bought " + got));
                if (Silent(w)) return "asking " + name + ": " + w;
            }
        } else {
            if (city == null) return "not in a city with a bank, and ingots come from the bank";
            CircuitStatus = "Smithing: ingots from the bank"; string bw = BankHere(k, city); if (bw != null) return "the bank: " + bw;
            string taken; bw = BankTake(new System.Collections.Generic.List<int> { IngotArt }, SmithIngotLoad, out taken); if (bw != null) return "ingots from the bank: " + bw;
        }
        string back = WalkTo(sx, sy, 0); if (back != null) return "back to the anvil: " + back;
        int have = PackIngots(k); return have >= need ? null : "out of ingots: " + have + " in the pack, " + need + " wanted (" + SmithIngots + ")";
    }
    string RunRefresh(GameLink k) {
        string city = CityOf(k); if (city == null) return "not in a city";
        var said = new System.Collections.Generic.List<string>(); bool mount = Riding(k) || MountSerial() != 0;
        string fw = BuyFood(k, city, mount); if (fw != null) { if (Silent(fw) || fw == "stopped") return fw; said.Add("food: " + fw); }
        if (!circuitStop) { CircuitStatus = "Refresh: eating"; said.Add(EatFull(k, mount)); }
        if (mount && !circuitStop) { CircuitStatus = "Refresh: feeding the mount"; said.Add(FeedMount(k) ?? "the mount fed"); }
        if (!circuitStop) { CircuitStatus = "Refresh: at the bank"; string bw = BankHere(k, city); said.Add(bw != null ? "bank: " + bw : RefreshLoadout(k, city)); }
        RefreshStatus = string.Join("; ", said); Note("Refresh: " + RefreshStatus);
        return circuitStop ? "stopped" : "refresh done: " + RefreshStatus;
    }
    // Food from the city's food shops (the ticked ones when any are), provisioners first, then farms and markets, then
    // stables, each kind nearest first: HorseFeed apples or pears for the mount, and for the player the food of the most
    // satiety a keeper stocks, enough for FullSatiety. Null once both are in the pack, else what is short.
    string BuyFood(GameLink k, string city, bool mount) {
        System.Func<int> horseWant = () => mount ? System.Math.Max(0, HorseFeed - PackCount(k, g => System.Array.IndexOf(HorseFood, g) >= 0)) : 0;
        if (horseWant() == 0 && SelfFood(k, mount) >= FullSatiety) return null;
        var shops = CircuitShops("refresh").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
        if (shops.Count == 0) return "no food shop in " + city;
        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        System.Func<UoRegions.Shop, int> rank = s => { string kind = UoRegions.ShopKind(s.CX, s.CY); int r = kind == null ? -1 : System.Array.IndexOf(FoodShopKinds, kind.ToUpperInvariant()); return r < 0 ? FoodShopKinds.Length : r; };
        shops.Sort((a, b) => rank(a) != rank(b) ? rank(a).CompareTo(rank(b)) : Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
        foreach (var shop in shops) {
            if (circuitStop) return "stopped";
            if (horseWant() == 0 && SelfFood(k, mount) >= FullSatiety) break;
            int qx, qy; lock (k.State.Gate) { qx = k.State.X; qy = k.State.Y; }
            CircuitStatus = "Refresh: walking into " + shop.Text; string w = shop.Holds(qx, qy, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
            if (w != null) { Note("Refresh: walking into " + shop.Text + ": " + w); continue; }
            NameUnnamed(VendorHear * 4);
            var keepers = Atlas == null ? new System.Collections.Generic.List<AtlasNpc>() : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2) && (System.DateTime.Now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
            foreach (var v in keepers) {
                if (circuitStop) return "stopped";
                int hw = horseWant(); bool selfShort = SelfFood(k, mount) < FullSatiety; if (hw == 0 && !selfShort) break;
                string name = v.Name.Split(' ')[0]; int got, left;
                // The first ask reads the stock list, and buys the mount's fruit from it.
                CircuitStatus = "Refresh: food from " + name + " in " + shop.Text;
                string bw = VendorBuy(g => hw > 0 && System.Array.IndexOf(HorseFood, g) >= 0, hw, out got, out left, name);
                if (Silent(bw)) return "asking " + name + ": " + bw;
                if (bw != null && bw.StartsWith("away: ")) { Note("Refresh: " + name + ": " + bw); continue; }
                if (!selfShort) continue;
                int best = BestFood(k, v.Serial); if (best == 0) continue;
                int want = (FullSatiety - SelfFood(k, mount) + Satiety[best] - 1) / Satiety[best];
                bw = VendorBuy(g => g == best, want, out got, out left, name);
                if (Silent(bw)) return "asking " + name + ": " + bw;
                if (bw != null) Note("Refresh: " + name + ": " + bw);
            }
        }
        return horseWant() > 0 ? "no apples or pears found in " + city + "'s food shops" : SelfFood(k, mount) < FullSatiety ? "not enough food for full found in " + city : null;
    }
    // Eats the pack's food of the most satiety, one at a time, until the server says full ("too full" or "stuffed");
    // an eat with no answer ends it (L-FUTILITY). What happened, for the report.
    string EatFull(GameLink k, bool mount) {
        int eaten = 0;
        for (int i = 0; i < EatMax && !circuitStop && !k.Closed; i++) {
            AssistItem food = null; int fs = 0;
            lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) { int s; if (!inside.Contains(it.Container) || !Satiety.TryGetValue(it.Graphic, out s)) continue; if (mount && System.Array.IndexOf(HorseFood, it.Graphic) >= 0 && PackCount(k, g => g == it.Graphic) <= HorseFeed) continue; if (s > fs) { fs = s; food = it; } } }
            if (food == null) return eaten == 0 ? "no food in the pack to eat" : "ate " + eaten + ", then the food ran out before full";
            long mark;
            lock (actionGate) { Answered(k, AnswerMs); mark = k.State.JournalSeq; string why = k.Use(food.Serial, false); if (why != null) return "eating: " + why; }
            string line = null; var by = System.DateTime.UtcNow.AddMilliseconds(EatAnswerMs);
            while (line == null && System.DateTime.UtcNow < by) { foreach (var j in k.State.JournalSince(mark)) if (System.Array.Exists(EatLines, e => j.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0)) line = j; if (line == null) System.Threading.Thread.Sleep(50); }
            if (line == null) return "no answer to eating the " + UoTiles.Name(food.Graphic) + (eaten > 0 ? " after " + eaten + " eaten" : "");
            if (line.IndexOf("can't eat", System.StringComparison.OrdinalIgnoreCase) >= 0) return "the server said \"" + line + "\"";
            if (line.IndexOf("too full", System.StringComparison.OrdinalIgnoreCase) >= 0) return "full (" + eaten + " eaten)";
            eaten++;
            if (line.IndexOf("stuffed", System.StringComparison.OrdinalIgnoreCase) >= 0) return "full (" + eaten + " eaten)";
            System.Threading.Thread.Sleep(BusyMs);
        }
        return "ate " + eaten;
    }
    // The mount handed HorseFeed apples or pears: dismounted first when ridden, and mounted again after "Your pet looks
    // happier." Null when fed, else why not.
    string FeedMount(GameLink k) {
        uint me; lock (k.State.Gate) me = k.State.Player;
        bool riding = Riding(k); uint horse = MountSerial();
        AssistItem fruit = null; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && System.Array.IndexOf(HorseFood, it.Graphic) >= 0 && (fruit == null || it.Amount > fruit.Amount)) fruit = it; }
        if (fruit == null) return "no apples or pears for the mount";
        var before = new System.Collections.Generic.HashSet<uint>(); lock (k.State.Gate) foreach (var key in k.State.Mobiles.Keys) before.Add(key);
        if (riding) {
            // The last bite leaves the player busy: a dismount 187 ms after it was answered "You must wait to perform
            // another action." and dropped (uo1998 2026-10-18 08:25:51).
            System.Threading.Thread.Sleep(BusyMs);
            lock (actionGate) { Answered(k, AnswerMs); string why = k.Use(me, false); if (why != null) return "dismounting: " + why; }
            var by = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); while (Riding(k) && System.DateTime.UtcNow < by) System.Threading.Thread.Sleep(50);
            if (Riding(k)) return "the dismount did not happen";
        }
        // The mount in view: the one known, else the mobile that came into view beside the player at the dismount.
        var seenBy = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); bool inView = false;
        while (!inView && System.DateTime.UtcNow < seenBy) {
            lock (k.State.Gate) {
                if (horse != 0 && k.State.Mobiles.ContainsKey(horse)) inView = true;
                else foreach (var kv in k.State.Mobiles) if (!before.Contains(kv.Key) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= 2) { horse = kv.Key; inView = true; break; }
            }
            if (!inView) System.Threading.Thread.Sleep(50);
        }
        if (!inView) return "the mount is not in view";
        long mark = k.State.JournalSeq; string w;
        lock (actionGate) { Answered(k, AnswerMs); int take = System.Math.Min(HorseFeed, System.Math.Max(1, fruit.Amount)); w = Lifted(k, fruit.Serial, take); if (w == null) w = k.Drop(fruit.Serial, horse); }
        bool happy = false;
        if (w == null) { var by = System.DateTime.UtcNow.AddMilliseconds(EatAnswerMs); while (!happy && System.DateTime.UtcNow < by) { happy = k.State.JournalSince(mark).Exists(j => j.IndexOf("looks happier", System.StringComparison.OrdinalIgnoreCase) >= 0); if (!happy) System.Threading.Thread.Sleep(50); } }
        if (riding) {
            lock (actionGate) { Answered(k, AnswerMs); k.Use(horse, false); }
            var by = System.DateTime.UtcNow.AddMilliseconds(MountWaitMs); while (!Riding(k) && System.DateTime.UtcNow < by) System.Threading.Thread.Sleep(50);
            if (!Riding(k)) Note("Refresh: mounting again did not happen; the mount waits beside you");
        }
        return w != null ? "feeding the mount: " + w : happy ? null : "the mount took no food (no \"looks happier\")";
    }
    // The loadout bag refilled from the storage bag in the open bank box: the two bags of the last loadout picked by hand.
    // With no storage bag set, RefreshFromBank.
    string RefreshLoadout(GameLink k, string city) {
        uint bag, storage; bool hasBag = uint.TryParse(Cfg.Get("loadout.bag", ""), System.Globalization.NumberStyles.HexNumber, null, out bag);
        if (!uint.TryParse(Cfg.Get("loadout.storage", ""), System.Globalization.NumberStyles.HexNumber, null, out storage)) return RefreshFromBank(k, city, hasBag ? bag : 0);
        if (!hasBag) return "loadout: pick the loadout bag and the storage bag once by hand (Pricer tab, Reagent loadout)";
        bool there; lock (k.State.Gate) there = k.State.Items.ContainsKey(bag) && k.State.Items.ContainsKey(storage);
        if (!there) return "loadout: the loadout bag " + bag.ToString("X") + " or the storage bag " + storage.ToString("X") + " is not in the pack or the open bank box";
        CircuitStatus = "Refresh: refilling the loadout"; LoadoutRun(k, bag, storage); return "loadout: " + LoadoutStatus;
    }
    public string LoadoutNoStorage() { Cfg.Set("loadout.storage", ""); return "Refresh refills the loadout from every bag in the bank box, and buys what the bank lacks."; }
    // A reagent's highest price on a shortfall buy: the shopping list's when it names the reagent, else twice RunUO's
    // SBMage price (5 for pearl and bloodmoss, 3 for the rest), as a depleted stock charges more (4 against a listed 3,
    // Jannelle's thread, 2026-10-17).
    int LoadoutPrice(int g) { foreach (var i in BuyList()) if (System.Array.IndexOf(i.Arts, g) >= 0) return i.MaxPrice; return g == 0xF7A || g == 0xF7B ? 10 : 6; }
    // With no storage bag set (Damian, 2026-10-18): the loadout's reagents taken from every bag in the open bank box; what
    // the bank lacks bought in the city's mage shops (the buy circuit's picked ones here, else every one here), exactly the
    // shortfall, with gold from the box's own piles; then the change banked and the reagents into the loadout bag.
    string RefreshFromBank(GameLink k, string city, uint bag) {
        if (bag != 0) { bool inPack; lock (k.State.Gate) inPack = PackTree(k).Contains(bag); if (!inPack) bag = 0; }
        var arts = new System.Collections.Generic.List<int>(); foreach (int g in LoadoutReagents()) if (LoadoutWant(g) > 0) arts.Add(g);
        CircuitStatus = "Refresh: the loadout from the bank"; string taken;
        string w = BankTake(arts, LoadoutWant, out taken); if (w != null) return "loadout from the bank: " + w;
        var said = new System.Collections.Generic.List<string> { "from the bank " + taken };
        var list = new System.Collections.Generic.List<BuyItem>(); var left = new System.Collections.Generic.Dictionary<string, int>(); int cost = 0;
        foreach (int g in arts) { int s = LoadoutWant(g) - PackCount(k, x => x == g); if (s <= 0) continue; var i = new BuyItem { Name = ReagentName(g), Arts = new[] { g }, Count = s, MaxPrice = LoadoutPrice(g) }; list.Add(i); left[i.Name] = s; cost += s * i.MaxPrice; }
        if (list.Count > 0 && !circuitStop) {
            w = BankTake(new System.Collections.Generic.List<int> { GoldArt }, PurseKeep + cost, out taken);
            if (w != null) return string.Join("; ", said) + "; gold from the bank: " + w;
            var shops = CircuitShops("buy").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
            if (shops.Count == 0) shops = AllShops("buy").FindAll(s => UoRegions.City(s.CX, s.CY) == city);
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            int bought = 0; string end = shops.Count == 0 ? "no mage shop in " + city : null;
            foreach (var shop in shops) { if (circuitStop || !list.Exists(i => left[i.Name] > 0)) break; end = BuyInShop(k, shop, list, left, ref bought); if (end != null) break; }
            var still = new System.Collections.Generic.List<string>(); foreach (var i in list) if (left[i.Name] > 0) still.Add(left[i.Name] + " " + i.Name);
            said.Add("bought " + bought + (still.Count > 0 ? ", still short " + string.Join(", ", still) : "") + (end != null ? " (" + end + ")" : ""));
            if (!circuitStop) { CircuitStatus = "Refresh: banking the change"; string bw = BankHere(k, city); if (bw != null) said.Add("the change: " + bw); }
        }
        if (bag != 0 && !circuitStop) { int moved; w = PutInto(arts, bag, out moved); if (w != null) said.Add("into the loadout bag: " + w); }
        return "loadout: " + string.Join("; ", said);
    }
    // final: where the circuit ends, everything bought goes into the bank, none kept back for travel, and the purse drops
    // to PurseKeep (Damian, 2026-10-17: "the ending should have included putting the pearl in the bank bag, and dropping
    // back to 300gp rest purse").
    string BuyBank(GameLink k, string city, bool final = false) {
        int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        var banks = CityBanks(city, x, y); int[] bank = null; string why = null;
        foreach (var b in banks) {
            if (circuitStop) return "stopped";
            if (BankSkipped(b)) continue;
            // ToBanker returns null only with the box open; a distance check from the region's centre after it threw an
            // open box away and walked to Britain's other bank, the gold and goods left in the pack (2026-10-18 12:48).
            CircuitStatus = "Buy: walking to the bank in " + city; string w = ToBanker(k, b); if (w != null) { SkipBank(b); why = "walking to the bank: " + w; continue; }
            bank = b; break;
        }
        if (banks.Count > 0 && bank == null) return why ?? "every bank in " + city + " failed within " + FullMinutes + " minutes";
        byte[] open; lock (actionGate) { Answered(k, AnswerMs); open = SayBank(k); }
        if (open == null) { if (bank != null) SkipBank(bank); return NoBanker(); }
        if (bank != null) BankHeard(bank);
        uint box = U32(open, 1);
        var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
        while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
        var tree = new System.Collections.Generic.HashSet<uint> { box };
        for (int depth = 0; depth < 2; depth++) {
            var bags = new System.Collections.Generic.List<uint>();
            lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)) bags.Add(it.Serial);
            foreach (var bg in bags) { bool known; lock (contents) known = contents.ContainsKey(bg); if (!known) { BusyWait(clickedAt); Peek(bg); } tree.Add(bg); }
        }
        var arts = new System.Collections.Generic.HashSet<int>(); foreach (var i in BuyList()) foreach (int a in i.Arts) arts.Add(a);
        // The store: the container in the box holding the most of the list's goods.
        uint store = box; int most = 0;
        lock (k.State.Gate) {
            var units = new System.Collections.Generic.Dictionary<uint, int>();
            foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && it.Container != box && arts.Contains(it.Graphic)) { int u; units.TryGetValue(it.Container, out u); units[it.Container] = u + System.Math.Max(1, it.Amount); }
            foreach (var kv in units) if (kv.Value > most) { most = kv.Value; store = kv.Key; }
        }
        var tiles = UoTiles.Items();
        lock (actionGate) {
            foreach (int art in arts) {
                int keep = !final && System.Array.IndexOf(RecallReagents, art) >= 0 ? ReagentRefill : 0;
                bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
                for (int round = 0; round < 20 && !circuitStop; round++) {
                    int have = PackCount(k, g => g == art); if (have <= keep) break;
                    AssistItem stack = null, onto = null;
                    lock (k.State.Gate) {
                        var inside = PackTree(k);
                        foreach (var it in k.State.Items.Values) {
                            if (it.Graphic != art) continue;
                            if (inside.Contains(it.Container) && (stack == null || it.Amount > stack.Amount)) stack = it;
                            else if (it.Container == store && stacks && (onto == null || it.Amount > onto.Amount)) onto = it;
                        }
                    }
                    if (stack == null) break;
                    int amount = System.Math.Min(System.Math.Max(1, stack.Amount), have - keep);
                    string w = BusyLift(k, stack.Serial, amount); if (w != null) return "lifting " + UoTiles.Name(art) + ": " + w;
                    w = k.Drop(stack.Serial, onto != null ? onto.Serial : store); if (w != null) return Stopped("Storing " + UoTiles.Name(art), stack.Serial, w);
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    if (PackCount(k, g => g == art) >= have) return UoTiles.Name(art) + " did not leave the pack";
                }
            }
        }
        int stored = 0; lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == store && arts.Contains(it.Graphic)) stored += System.Math.Max(1, it.Amount);
        Note("Buy: the bank in " + city + " holds " + stored + " of the list's goods in " + store.ToString("X"));
        int purse = PackCount(k, g => g == GoldArt), target = final ? PurseKeep : BuyPurse;
        if (purse > target) { int put; string w = BankDeposit(target, out put); if (w != null) return "depositing: " + w; }
        else if (!final && purse < BuyPurse) { string taken; string w = BankTake(new System.Collections.Generic.List<int> { GoldArt }, BuyPurse, out taken); if (w != null) return "gold from the bank: " + w; }
        return null;
    }
    // Each circuit's last end, by its name, for the window's totals card (Damian, 2026-10-18: "add all circuit's summary to
    // totals card").
    public readonly System.Collections.Generic.Dictionary<string, string> CircuitEnds = new System.Collections.Generic.Dictionary<string, string>();
    void RunCircuit(GameLink k) {
        string runName = smithMode ? "Smithing" : mineMode ? "Cave mining" : tourMode ? "Tour" : buyMode ? "Buy reagents" : surveyMode ? "Survey town" : refreshMode ? "Refresh" : sellOnly ? "Sell only" : "Tailoring";
        string end = "stopped"; int bought = 0, shirts = 0, sold = 0, goldIn = 0;
        try {
            // After a login the pack's contents are unknown until it is opened, and the circuit counted no shirts.
            uint pack; lock (k.State.Gate) pack = k.State.Backpack; bool known; lock (contents) known = pack != 0 && contents.ContainsKey(pack);
            if (pack != 0 && !known) { Peek(pack); BusyWait(clickedAt); }
            OpenPackBags(k);
            if (smithMode) { end = RunSmithing(k); return; }
            if (mineMode) { end = RunMine(k); return; }
            if (tourMode) { end = RunTour(k, ref bought, ref sold, ref goldIn); return; }
            if (buyMode) { end = RunBuy(k); return; }
            if (surveyMode) { end = RunSurvey(k); return; }
            if (refreshMode) { end = RunRefresh(k); return; }
            while (!circuitStop && !k.Closed) {
                bool did = false; int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                // The town's thread vendors: those bought from before and those whose buy list named thread, near here.
                var vendors = new System.Collections.Generic.List<AtlasNpc>(); var seen = new System.Collections.Generic.HashSet<uint>();
                foreach (var v in CircuitVendors()) { var n = VendorNow(v.Serial); if (n != null && Tiles(n.X, n.Y, px, py) <= CircuitReach && seen.Add(n.Serial)) vendors.Add(n); }
                if (Atlas != null) foreach (var n in Atlas.Shops()) if (Tiles(n.X, n.Y, px, py) <= CircuitReach && TailorVendor(n) && n.Items.Exists(s => s.IndexOf("thread", System.StringComparison.OrdinalIgnoreCase) >= 0) && seen.Add(n.Serial)) vendors.Add(n);
                vendors.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
                // The order: the work where the player stands first, sell the shirts, buy thread, weave and sew; the gold is
                // banked when the bank is the next place anyway (a recall goes from it, Travel) or before a wait (Damian,
                // 2026-10-17: "am I in the place i will come back to after the very next step, and maybe i could just kill 2
                // birds with one stone, and one less round trip"; banking first walked from the tailor to the bank and back).
                // 1b. Sell the shirts, nearest tailor to where the player stands first, on to the next when one will not pay
                // the floor or is full.
                lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                if (!circuitStop && PackCount(k, IsShirtArt) > 0) {
                    // Only a vendor that deals in something on the sell list: the circuit's thread record also holds a vendor
                    // asked for thread after a failed sale (Moonglow's armorer, 2026-10-17), which then drew every pass back.
                    var sellers = vendors.FindAll(DealsIn);
                    if (Atlas != null) foreach (var n in Atlas.Shops()) if (Tiles(n.X, n.Y, px, py) <= CircuitReach && DealsIn(n) && TailorVendor(n) && !sellers.Exists(x => x.Serial == n.Serial)) sellers.Add(n);
                    sellers.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
                    foreach (var v in sellers) {
                        if (circuitStop || PackCount(k, IsShirtArt) == 0) break;
                        // A tailor that offered under the floor is not walked to again for UnderFloorMinutes (it walked
                        // Britain end to end asking Slade, then Tracy, each having just offered 24).
                        if (!PaysFloor(v) || Skipped(v.Serial)) continue;
                        string name = v.Name.Split(' ')[0]; var cur = VendorNow(v.Serial) ?? v;
                        // A tailor the walk could not reach is not walked to again every pass.
                        CircuitStatus = "Walking to " + name + " to sell"; string w = WalkToSpeakingRange(cur.X, cur.Y); if (w != null) { Note("Circuit: walking to " + name + ": " + w); SkipVendor(v.Serial); continue; }
                        CircuitStatus = "Selling shirts to " + name; int n2, g2, under; w = VendorSell(IsShirtArt, FloorOf, out n2, out g2, out under, name);
                        sold += n2; goldIn += g2; if (n2 > 0) did = true;
                        if (w != null) Note("Circuit: " + name + ": " + w);
                        if (Silent(w)) { end = "selling to " + name + ": " + w; return; }
                        if (under > 0) UnderFloorOffered(v.Serial); else if (PackCount(k, IsShirtArt) > 0) AvoidTailor(v.Serial, System.TimeSpan.FromMinutes(FullMinutes));
                        // Thread from the tailor just asked, sale or not (Damian, 2026-10-17: "every single time you go to
                        // sell shirts, you should also look to buy threads afterwards"; every tailor sells thread).
                        if (!circuitStop && ThreadDue(v) && PackCount(k, IsThreadArt) < ThreadWant) {
                            int got, left; string tw = VendorBuy(IsThreadArt, ThreadWant * 10, out got, out left, name);
                            if (tw == null) { bought += got; did = true; } else { Note("Circuit: " + name + ": " + tw); if (Silent(tw)) { end = "buying thread from " + name + ": " + tw; return; } SkipVendor(v.Serial); }
                        }
                    }
                }
                // 1c. Shirts no tailor here takes at the floor: sold in another city, thread bought there or in the next,
                // the gold banked where it was made (Damian, 2026-10-17: "find a place to sell them. if you leave brit to
                // do that, check for thread where you sell em. then dump gold. if you didn't find thread yet, then go
                // find more thead").
                if (!circuitStop && PackCount(k, IsShirtArt) >= SellBatch) {
                    int n2, g2, t2; string w = SellTrip(k, out n2, out g2, out t2); sold += n2; goldIn += g2; bought += t2;
                    if (w != null) Note("Circuit: trip: " + w); if (n2 + t2 > 0) did = true;
                    if (sellOnly && n2 == 0) { end = "no tailor reached would buy at " + FloorText() + (w != null ? ": " + w : ""); return; }
                    // A trip that failed is not taken again next pass: the circuit ends and says why. No city to go to is
                    // only a reading of the record, and the circuit goes on making shirts.
                    if (w != null && !w.StartsWith(NoSellCity)) { end = "sell trip: " + w; return; }
                }
                // Sell-only (circuit sell): the shirts sold and the gold banked, and nothing else (Damian, 2026-10-17: "not
                // buy anything, and not do anything that is not critical path to selling the shirts in its backpack").
                if (sellOnly) {
                    if (PackCount(k, IsShirtArt) == 0) {
                        string city = CityOf(k); if (city != null && PackCount(k, g => g == GoldArt) > PurseKeep) { string bw = BankHere(k, city); if (bw != null) Note("Circuit: banking in " + city + ": " + bw); }
                        end = "every shirt sold"; return;
                    }
                    if (PackCount(k, IsShirtArt) < SellBatch && !did) { end = PackCount(k, IsShirtArt) + " shirts left, under a batch"; return; }
                    continue;
                }
                // 2. Thread here, from the nearest vendor to where the player stands whose restock is due, up to ThreadWant;
                // none while shirts wait to be sold (Damian, 2026-10-17: "it went and bought more thread first thing. it did
                // not even try to sell the shirts").
                if (PackCount(k, IsShirtArt) < SellBatch) {
                    var due = new System.Collections.Generic.List<AtlasNpc>(); string here = CityOf(k);
                    if (here != null) { NameUnnamed(VendorHear * 4); foreach (var v in ThreadCandidates(here)) if (ThreadDue(v)) due.Add(v); }
                    while (!circuitStop && due.Count > 0 && PackCount(k, IsThreadArt) < ThreadWant) {
                        lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                        int qx = px, qy = py; due.Sort((a, b) => Tiles(a.X, a.Y, qx, qy).CompareTo(Tiles(b.X, b.Y, qx, qy)));
                        var v = due[0]; due.RemoveAt(0); string name = v.Name.Split(' ')[0];
                        CircuitStatus = "Buying thread from " + name; int got, left; string w = VendorBuy(IsThreadArt, ThreadWant * 10, out got, out left, name);
                        if (w == null) { bought += got; did = true; continue; }
                        Note("Circuit: " + name + ": " + w);
                        if (Silent(w)) { end = "buying thread from " + name + ": " + w; return; }
                        SkipVendor(v.Serial);
                    }
                }
                lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                // 3. Weave at the nearest loom.
                if (!circuitStop && PackCount(k, IsThreadArt) > 0) {
                    AssistItem loom = NearLoom(k, px, py);
                    // The nearest loom known: one in view, or one seen before and closer, walked to and looked for again.
                    int[] nearer = null; lock (loomSpots) foreach (var s in loomSpots) if (Tiles(s[0], s[1], px, py) <= CircuitReach && (loom == null || Tiles(s[0], s[1], px, py) < Tiles(loom.X, loom.Y, px, py)) && (nearer == null || Tiles(s[0], s[1], px, py) < Tiles(nearer[0], nearer[1], px, py))) nearer = s;
                    if (nearer != null && loom != null && Tiles(nearer[0], nearer[1], px, py) < Tiles(loom.X, loom.Y, px, py)) loom = null;
                    if (loom == null) {
                        int[] spot = nearer; if (spot == null) lock (loomSpots) foreach (var s in loomSpots) if (Tiles(s[0], s[1], px, py) <= CircuitReach && (spot == null || Tiles(s[0], s[1], px, py) < Tiles(spot[0], spot[1], px, py))) spot = s;
                        // None seen since the relay started: the thread vendors keep their tailor shops' looms.
                        if (spot == null) foreach (var v in CircuitVendors()) if (Tiles(v.X, v.Y, px, py) <= CircuitReach && (spot == null || Tiles(v.X, v.Y, px, py) < Tiles(spot[0], spot[1], px, py))) spot = new[] { v.X, v.Y };
                        if (spot != null) { CircuitStatus = "Walking to the loom at " + spot[0] + "," + spot[1]; string lw = WalkTo(spot[0], spot[1], 2); if (lw != null) Note("Circuit: walking to the loom: " + lw); lock (k.State.Gate) { px = k.State.X; py = k.State.Y; } loom = NearLoom(k, px, py); }
                    }
                    if (loom == null) Note("Circuit: no loom within " + CircuitReach + " tiles");
                    else {
                        CircuitStatus = "Walking to the loom"; string w = WalkTo(loom.X, loom.Y, 1, true);
                        if (w != null) Note("Circuit: walking to the loom: " + w);
                        else {
                            int threadBefore = PackCount(k, IsThreadArt);
                            CircuitStatus = "Weaving " + threadBefore + " thread"; WorkToggle();
                            while (workThread != null && workThread.IsAlive && !circuitStop) System.Threading.Thread.Sleep(500);
                            // A loom that took no thread is not used again next pass.
                            if (!circuitStop && PackCount(k, IsThreadArt) >= threadBefore) { end = "weaving: " + WorkEnd; return; }
                            did = true;
                        }
                    }
                }
                // 4. Sew all the cloth.
                if (!circuitStop && (PackCloth(k).Count > 0 || PackBolts(k).Count > 0)) {
                    if (PackCount(k, g => g == KitArt) == 0) {
                        // The toolbag in the bank box first, then a tailor.
                        string kw = ToolsFromBank(k, g => g == KitArt, KitsWant, "sewing kits");
                        if (kw != null) { Note("Circuit: " + kw); kw = BuyKits(k, px, py); }
                        // No kit is no sewing, and the next pass would ask the same tailors again.
                        if (kw != null && PackCount(k, g => g == KitArt) == 0) { end = "sewing kits: " + kw; return; }
                    }
                    CircuitStatus = "Sewing fancy shirts"; int made, failed; string w = SewAll(out made, out failed);
                    shirts += made; did |= made + failed > 0; if (w != null && w != "stopped") Note("Circuit: sewing: " + w);
                }
                // 5. Nothing to do here: thread from another city's vendor (Damian, 2026-10-17: "no waiting, find another
                // vendor"), once a pass, and the wait for a restock only when no city has thread due.
                if (!did && !circuitStop) {
                    int t2; string tw = ThreadTrip(k, out t2); bought += t2;
                    if (t2 > 0) continue;
                    if (tw != null && tw != NoThreadDue) { end = "thread trip: " + tw; return; }
                }
                if (!did && !circuitStop) {
                    var next = System.DateTime.MaxValue; foreach (var v in CircuitVendors()) { var due = v.BoughtOutAt.AddMinutes(RestockMinutes); if (v.Stock == 0 && due > System.DateTime.Now && due < next) next = due; }
                    if (next == System.DateTime.MaxValue) { end = "nothing to buy, weave, sew or sell here"; return; }
                    // Before the wait, the gold over BankOver banked (nothing else is due here until the restock).
                    if (PackCount(k, g => g == GoldArt) > BankOver) {
                        string city = CityOf(k); string bw = city != null ? BankHere(k, city) : "not in a city";
                        // A failed bank is passed over for FullMinutes (BankSkipped), never asked again inside that, and the
                        // gold carried; never a Britain fallback.
                        if (bw != null) Note("Circuit: banking in " + (city ?? "no city") + ": " + bw + "; the gold carried");
                    }
                    CircuitStatus = "Waiting for a thread restock at " + next.ToString("HH:mm");
                    while (System.DateTime.Now < next && !circuitStop && !k.Closed) System.Threading.Thread.Sleep(RestockPollMs);
                }
            }
            end = circuitAlarm ?? (k.Closed ? "the connection closed" : "stopped");
        } catch (System.Exception e) { end = "error: " + e.Message; }
        finally {
            CircuitStatus = "Circuit ended (" + end + "): bought " + bought + " thread, sewed " + shirts + " shirts, sold " + sold + " for " + goldIn + " gold.";
            Note("Circuit: " + CircuitStatus);
            lock (CircuitEnds) CircuitEnds[runName] = System.DateTime.Now.ToString("HH:mm") + " " + CircuitStatus;
        }
    }

    // ---------- the bank (assist-plan.md stage 6) ----------
    // Damian, 2026-10-17: "walk to the bank wall, say bank, and deposit the money carefully not to overflow the pile of
    // gold there, but to use it if possible since there are limits to items and weight as well". A banker hears "bank"
    // through the bank's walls, so the walk ends BankHear tiles from the nearest banker. The pack keeps PurseKeep gold for
    // thread; the rest goes onto the bank's gold piles: the fullest pile that takes it all under BankStackMax, else the
    // pile with the most room (filled to BankStackMax), and a new pile only when every pile is full.
    // BankOver: a bank the circuit would walk to only for the deposit (after a sale, before a wait) is walked to only above
    // this; at a recall's bank the gold over PurseKeep goes in.
    public const int BankStackMax = 60000, PurseKeep = 300, BankHear = 8, BankOver = 2000;
    public string BankDeposit(int keep, out int put) {
        put = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        // Near enough a banker to be heard, unless the box is open from here already (ToBanker).
        if (Atlas != null && !BankFresh(k)) {
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            AtlasNpc banker = null; foreach (var n in Atlas.Visible()) if (n.Type == "Banker" && (banker == null || Tiles(n.X, n.Y, px, py) < Tiles(banker.X, banker.Y, px, py))) banker = n;
            // uo1998's bankers carry no "the banker" title (Britain's: Blythe, Francis, Elwood, Aida), so an unknown banker
            // is asked from here: the answer, or none, settles it.
            if (banker != null && Tiles(banker.X, banker.Y, px, py) > BankHear) { string w = WalkTo(banker.X, banker.Y, BankHear); if (w != null) return "walking to the bank: " + w; }
        }
        lock (actionGate) {
            Answered(k, AnswerMs);
            var open = SayBank(k);
            if (open == null) return NoBanker();
            uint box = U32(open, 1);
            var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
            while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
            for (int round = 0; round < 20; round++) {
                int purse = PackCount(k, g => g == GoldArt); lock (k.State.Gate) purse = System.Math.Max(purse, k.State.Gold);
                if (purse <= keep) break;
                AssistItem stack = null;
                lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic == GoldArt && (stack == null || it.Amount > stack.Amount)) stack = it; }
                if (stack == null) return "the pack's gold is not known: open the pack once";
                int amount = System.Math.Min(System.Math.Max(1, stack.Amount), purse - keep);
                AssistItem fits = null, roomiest = null;
                lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == box && it.Graphic == GoldArt) {
                    int a = System.Math.Max(1, it.Amount);
                    if (a + amount <= BankStackMax && (fits == null || a > fits.Amount)) fits = it;
                    if (a < BankStackMax && (roomiest == null || a < roomiest.Amount)) roomiest = it;
                }
                AssistItem onto = fits ?? roomiest; if (onto != null && fits == null) amount = System.Math.Min(amount, BankStackMax - System.Math.Max(1, onto.Amount));
                string why = BusyLift(k, stack.Serial, amount); if (why != null) return "lifting gold: " + why;
                why = onto != null ? k.Drop(stack.Serial, onto.Serial) : k.Drop(stack.Serial, box);
                if (why != null) return Stopped("Depositing gold", stack.Serial, why);
                Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                int after = PackCount(k, g => g == GoldArt); lock (k.State.Gate) after = System.Math.Min(after, k.State.Gold);
                if (after >= purse) return "the gold did not leave the pack";
                put += purse - after;
                Note("Bank: put " + (purse - after) + " gold " + (onto != null ? "on the pile " + onto.Serial.ToString("X") : "in a new pile") + "; " + after + " in the pack");
            }
            return null;
        }
    }

    // Where an item of art goes in the pack so it costs no item of the pack's limit: onto the pack's biggest stack of it
    // (a drop on a stack joins it), else into the bag already holding any of arts (Damian, 2026-10-17: "you have bag
    // inside your backpack with reagents in them"), else the pack itself. except: a stack not to drop onto.
    uint StackOnto(GameLink k, int art, System.Collections.Generic.ICollection<int> arts, uint except) {
        // An item that does not stack (no 0x800 in the 1.25 tile data, a recall rune) is refused when dropped on another.
        var tiles = UoTiles.Items(); bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
        lock (k.State.Gate) {
            var inside = PackTree(k); AssistItem best = null; uint bag = 0;
            foreach (var it in k.State.Items.Values) {
                if (!inside.Contains(it.Container) || it.Serial == except) continue;
                if (stacks && it.Graphic == art && (best == null || it.Amount > best.Amount)) best = it;
                if (bag == 0 && it.Container != k.State.Backpack && arts.Contains(it.Graphic)) bag = it.Container;
            }
            return best != null ? best.Serial : bag != 0 ? bag : k.State.Backpack;
        }
    }
    // Every stack of each art in the pack and its bags joined onto the biggest one (Damian, 2026-10-17: "merge those
    // loose reagents with the bag"). Null, or why not; how many stacks were joined comes back.
    public string PackMerge(System.Collections.Generic.List<int> arts, out int joined) {
        joined = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        lock (actionGate) {
            foreach (int art in arts) {
                for (int round = 0; round < 20 && !circuitStop; round++) {
                    AssistItem small = null; int stacks = 0;
                    lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic == art) { stacks++; if (small == null || it.Amount < small.Amount) small = it; } }
                    if (stacks < 2) break;
                    uint onto = StackOnto(k, art, arts, small.Serial);
                    string why = BusyLift(k, small.Serial, System.Math.Max(1, small.Amount)); if (why != null) return "lifting " + UoTiles.Name(art) + ": " + why;
                    why = k.Drop(small.Serial, onto); if (why != null) return Stopped("Joining " + UoTiles.Name(art), small.Serial, why);
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    if (Item(k, small.Serial) != null && Item(k, small.Serial).Container != onto) return UoTiles.Name(art) + " did not join its stack";
                    joined++;
                }
            }
            return null;
        }
    }
    // Up to want of each art from the bank box into the pack, for what a trip needs (Damian, 2026-10-17: "there is extra
    // reagents in your bank box"; the design's refill at any bank). The box opens to "bank" as for a deposit; a stack is
    // lifted only as far as the pack still wants. Null, or why not; taken, art by art, comes back.
    public string BankTake(System.Collections.Generic.List<int> arts, int want, out string taken) { return BankTake(arts, a => want, out taken); }
    public string BankTake(System.Collections.Generic.List<int> arts, System.Func<int, int> wantOf, out string taken) {
        taken = ""; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        lock (actionGate) {
            Answered(k, AnswerMs);
            var open = SayBank(k);
            if (open == null) return NoBanker();
            uint box = U32(open, 1), pack; lock (k.State.Gate) pack = k.State.Backpack;
            if (pack == 0) return "the pack is not known: open the pack once";
            var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
            while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
            // The box's containers are opened for their contents, a wooden box of reagents in the bank box (Damian,
            // 2026-10-17: "there is a wooden box in there with the reagents"), two levels down.
            var tree = new System.Collections.Generic.HashSet<uint> { box };
            for (int depth = 0; depth < 2; depth++) {
                var bags = new System.Collections.Generic.List<uint>();
                lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)) bags.Add(it.Serial);
                // Each open waits out the last action ("You must wait to perform another action." twice at 08:42:57).
                foreach (var b in bags) { BusyWait(clickedAt); string w = Peek(b); if (w != null) Note("Bank: opening " + b.ToString("X") + ": " + w); tree.Add(b); }
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (int art in arts) {
                int got = 0, want = wantOf(art);
                for (int round = 0; round < 10 && !circuitStop; round++) {
                    int need = want - PackCount(k, g => g == art); if (need <= 0) break;
                    AssistItem stack = null;
                    // Gold only from the box's own piles, never a bag's (Damian, 2026-10-17: "i have stashed 30k gold in the
                    // bank into a sub-bag so nothing happens accidentally to it. keep storing and pulling from the now smaller
                    // top level pile of gold").
                    lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && it.Graphic == art && (art != GoldArt || it.Container == box) && (stack == null || it.Amount > stack.Amount)) stack = it;
                    if (stack == null) break;
                    int amount = System.Math.Min(System.Math.Max(1, stack.Amount), need), before = PackCount(k, g => g == art);
                    long mark = k.State.JournalSeq;
                    string why = BusyLift(k, stack.Serial, amount); if (why != null) return "lifting " + UoTiles.Name(art) + ": " + why;
                    why = k.Drop(stack.Serial, StackOnto(k, art, arts, stack.Serial)); if (why != null) return Stopped("Taking " + UoTiles.Name(art) + " from the bank", stack.Serial, why);
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    // Counted only when it reached the pack; a refused drop ("That container cannot hold more items.", the
                    // pack at 113 items, ten times over at 08:43:05) stops the take rather than trying it again.
                    int moved = PackCount(k, g => g == art) - before;
                    if (moved <= 0) {
                        string said = null; foreach (var j in k.State.JournalSince(mark)) said = j;
                        parts.Add(UoTiles.Name(art) + " " + got + " (pack " + before + ")"); taken = string.Join(", ", parts);
                        return UoTiles.Name(art) + " did not reach the pack" + (said != null ? ": \"" + said + "\"" : "");
                    }
                    got += moved;
                }
                parts.Add(UoTiles.Name(art) + " " + got + " (pack " + PackCount(k, g => g == art) + ")");
            }
            taken = string.Join(", ", parts);
            Note("Bank: took " + taken);
            return null;
        }
    }
    // Every stack of each art in the pack and its bags into one container (Damian, 2026-10-17: "put these all in the
    // subpack in the upper left corner of the main pack"): onto its stack of the art when it has one and the art stacks,
    // else into the container. Null, or why not; how many stacks moved comes back.
    public string PutInto(System.Collections.Generic.List<int> arts, uint into, out int moved) {
        moved = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart(); var tiles = UoTiles.Items();
        lock (actionGate) {
            foreach (int art in arts) {
                bool stacks = art >= 0 && art < tiles.Count && (tiles[art].Flags & 0x800) != 0;
                for (int round = 0; round < 20 && !circuitStop; round++) {
                    AssistItem stack = null, onto = null;
                    lock (k.State.Gate) {
                        var inside = PackTree(k);
                        foreach (var it in k.State.Items.Values) {
                            if (it.Graphic != art) continue;
                            if (it.Container == into) { if (stacks && (onto == null || it.Amount > onto.Amount)) onto = it; }
                            else if (inside.Contains(it.Container) && (stack == null || it.Amount > stack.Amount)) stack = it;
                        }
                    }
                    if (stack == null) break;
                    string why = BusyLift(k, stack.Serial, System.Math.Max(1, stack.Amount)); if (why != null) return "lifting " + UoTiles.Name(art) + ": " + why;
                    why = k.Drop(stack.Serial, onto != null ? onto.Serial : into); if (why != null) return Stopped("Moving " + UoTiles.Name(art), stack.Serial, why);
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    var now = Item(k, stack.Serial);
                    if (now != null && now.Container != into && now.Container != (onto != null ? onto.Serial : into)) return UoTiles.Name(art) + " did not reach " + into.ToString("X");
                    moved++;
                }
            }
            return null;
        }
    }

    // Exactly these items into one container, by serial (Damian, 2026-10-17: "put those runes back in the bank": the
    // pack's Skara Brae rune is the same art and stays). Null, or why not.
    public string MoveInto(System.Collections.Generic.List<uint> serials, uint into) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        lock (actionGate) {
            foreach (var s in serials) {
                var it = Item(k, s); if (it == null) return s.ToString("X") + " is not known";
                if (it.Container == into) continue;
                string why = BusyLift(k, s, System.Math.Max(1, it.Amount)); if (why != null) return "lifting " + s.ToString("X") + ": " + why;
                why = k.Drop(s, into); if (why != null) return Stopped("Moving " + UoTiles.Name(it.Graphic), s, why);
                Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                var now = Item(k, s); if (now == null || now.Container != into) return s.ToString("X") + " did not reach " + into.ToString("X");
            }
            return null;
        }
    }

    // Each art in one container brought to exactly want (Damian, 2026-10-17: "we want to make it 50 of each", the
    // vendor's in-progress reagent bag): the excess lifted off the container's biggest stack onto the source's
    // stack, a shortfall lifted from the source's own stacks only, never from a bag inside it; the source is source,
    // else the pack. A move that changes nothing stops the fill (L-FUTILITY). Null, or why not; each art's count
    // after, and any shortfall, come back.
    public string Fill(uint into, int want, System.Collections.Generic.List<int> arts, uint source, out string filled) {
        filled = ""; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        lock (actionGate) {
            uint pack = source; if (pack == 0) lock (k.State.Gate) pack = k.State.Backpack;
            if (pack == 0) return "the pack is not known: open the pack once";
            var parts = new System.Collections.Generic.List<string>();
            System.Func<uint, int, int> count = (box, art) => { int n = 0; lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == box && it.Graphic == art) n += System.Math.Max(1, it.Amount); return n; };
            System.Func<uint, int, AssistItem> biggest = (box, art) => { AssistItem b = null; lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == box && it.Graphic == art && (b == null || it.Amount > b.Amount)) b = it; return b; };
            foreach (int art in arts) {
                for (int round = 0; round < 10 && !circuitStop; round++) {
                    int have = count(into, art); if (have == want) break;
                    AssistItem from, onto; int amount;
                    if (have > want) { from = biggest(into, art); onto = biggest(pack, art); amount = have - want; }
                    else { from = biggest(pack, art); if (from == null) break; onto = biggest(into, art); amount = System.Math.Min(System.Math.Max(1, from.Amount), want - have); }
                    string why = BusyLift(k, from.Serial, amount);
                    if (why != null) { parts.Add(UoTiles.Name(art) + " " + have); filled = string.Join(", ", parts); return "lifting " + UoTiles.Name(art) + ": " + why; }
                    why = k.Drop(from.Serial, onto != null ? onto.Serial : have > want ? pack : into);
                    if (why != null) { filled = string.Join(", ", parts); return Stopped("Filling " + UoTiles.Name(art), from.Serial, why); }
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    if (count(into, art) == have) { parts.Add(UoTiles.Name(art) + " " + have); filled = string.Join(", ", parts); return UoTiles.Name(art) + " did not move; the item " + from.Serial.ToString("X") + " is " + (Item(k, from.Serial) == null ? "held by the server" : "in container " + Item(k, from.Serial).Container.ToString("X")); }
                }
                int after = count(into, art);
                parts.Add(UoTiles.Name(art) + " " + after + (after < want ? " (short " + (want - after) + ")" : ""));
            }
            filled = string.Join(", ", parts);
            Note("Fill: " + filled);
            return null;
        }
    }

    // Every stack of each art in the pack and its bags onto the bank's biggest stack of it, else into the bank's bag
    // already holding any of arts, else the box (Damian, 2026-10-17: "merge in these reagents with the stuff there").
    // Null, or why not; put, art by art, and the bank's count of each after, come back.
    public string BankPut(System.Collections.Generic.List<int> arts, out string put) {
        put = ""; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        lock (actionGate) {
            Answered(k, AnswerMs);
            var open = SayBank(k);
            if (open == null) return NoBanker();
            uint box = U32(open, 1);
            var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); System.Collections.Generic.List<uint> had = null;
            while (System.DateTime.UtcNow < by) { lock (contents) contents.TryGetValue(box, out had); if (had != null) break; System.Threading.Thread.Sleep(50); }
            var tree = new System.Collections.Generic.HashSet<uint> { box };
            for (int depth = 0; depth < 2; depth++) {
                var bags = new System.Collections.Generic.List<uint>();
                lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)) bags.Add(it.Serial);
                foreach (var b in bags) { BusyWait(clickedAt); string w = Peek(b); if (w != null) Note("Bank: opening " + b.ToString("X") + ": " + w); tree.Add(b); }
            }
            System.Func<int, int> banked = art => { int n = 0; lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (tree.Contains(it.Container) && it.Graphic == art) n += System.Math.Max(1, it.Amount); return n; };
            var parts = new System.Collections.Generic.List<string>();
            foreach (int art in arts) {
                int moved = 0;
                for (int round = 0; round < 20 && !circuitStop; round++) {
                    AssistItem stack = null, onto = null; uint bag = 0;
                    lock (k.State.Gate) {
                        var inside = PackTree(k);
                        foreach (var it in k.State.Items.Values) {
                            if (inside.Contains(it.Container) && it.Graphic == art && (stack == null || it.Amount > stack.Amount)) stack = it;
                            // A stack in a bag before one loose in the box (Damian: "i keep my regs in bank in a subcontiner").
                            if (tree.Contains(it.Container) && it.Graphic == art && (onto == null || (onto.Container == box && it.Container != box) || ((onto.Container == box) == (it.Container == box) && it.Amount > onto.Amount))) onto = it;
                            if (bag == 0 && tree.Contains(it.Container) && it.Container != box && arts.Contains(it.Graphic)) bag = it.Container;
                        }
                    }
                    if (stack == null) break;
                    int before = PackCount(k, g => g == art), amount = System.Math.Max(1, stack.Amount);
                    string why = BusyLift(k, stack.Serial, amount); if (why != null) return "lifting " + UoTiles.Name(art) + ": " + why;
                    why = k.Drop(stack.Serial, onto != null ? onto.Serial : bag != 0 ? bag : box); if (why != null) return Stopped("Banking " + UoTiles.Name(art), stack.Serial, why);
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                    int left = PackCount(k, g => g == art);
                    if (left >= before) { parts.Add(UoTiles.Name(art) + " " + moved); put = string.Join(", ", parts); return UoTiles.Name(art) + " did not leave the pack"; }
                    moved += before - left;
                }
                parts.Add(UoTiles.Name(art) + " " + moved + " (bank " + banked(art) + ")");
            }
            put = string.Join(", ", parts);
            Note("Bank: put " + put);
            return null;
        }
    }

    // ---------- the tailoring circuit's record, for the window's Circuit tab ----------
    // Per thread vendor: its thread stock as last bought from, when it was bought out, and each restock's size, kept in
    // circuit-<server>.txt beside the settings (tab-separated: serial, name, x, y, stock, stock ticks, bought-out ticks,
    // restocks as stock@ticks;...), so the restock timers outlive a relay restart. A vendor restocks about every
    // RestockMinutes (Damian, 2026-10-17: "respawns happen about every 15 mintues").
    public const int RestockMinutes = 15;
    public sealed class CircuitVendor { public uint Serial; public string Name = ""; public int X, Y, Stock = -1; public System.DateTime StockAt, BoughtOutAt; public System.Collections.Generic.List<int[]> Restocks = new System.Collections.Generic.List<int[]>(); public System.Collections.Generic.List<long> RestockTicks = new System.Collections.Generic.List<long>(); }
    readonly System.Collections.Generic.Dictionary<uint, CircuitVendor> circuitVendors = new System.Collections.Generic.Dictionary<uint, CircuitVendor>();
    bool circuitLoaded;
    public volatile string CircuitStatus = "No circuit running: steps are run one at a time from the control port.";
    public System.DateTime CircuitStart = System.DateTime.MinValue;
    string CircuitPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "circuit-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    void LoadCircuit() {
        if (circuitLoaded) return; circuitLoaded = true;
        try {
            if (!System.IO.File.Exists(CircuitPath)) return;
            foreach (var line in System.IO.File.ReadAllLines(CircuitPath)) {
                var t = line.Split('\t'); if (t.Length < 8) continue;
                var v = new CircuitVendor { Serial = System.Convert.ToUInt32(t[0], 16), Name = t[1], X = int.Parse(t[2]), Y = int.Parse(t[3]), Stock = int.Parse(t[4]), StockAt = new System.DateTime(long.Parse(t[5])), BoughtOutAt = new System.DateTime(long.Parse(t[6])) };
                foreach (var r in t[7].Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) { var p = r.Split('@'); if (p.Length == 2) { v.Restocks.Add(new[] { int.Parse(p[0]) }); v.RestockTicks.Add(long.Parse(p[1])); } }
                circuitVendors[v.Serial] = v;
            }
        } catch { }
    }
    void SaveCircuit() {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var v in circuitVendors.Values) {
            var rs = new System.Text.StringBuilder(); for (int i = 0; i < v.Restocks.Count; i++) rs.Append(v.Restocks[i][0] + "@" + v.RestockTicks[i] + ";");
            lines.Add(v.Serial.ToString("X") + "\t" + v.Name.Replace('\t', ' ') + "\t" + v.X + "\t" + v.Y + "\t" + v.Stock + "\t" + v.StockAt.Ticks + "\t" + v.BoughtOutAt.Ticks + "\t" + rs);
        }
        try { System.IO.File.WriteAllLines(CircuitPath, lines); } catch { }
    }
    // A thread buy at a vendor: the stock it showed (before buying) and what it has left. A vendor bought out before that
    // shows stock again has restocked, and that stock is kept as one of its restocks.
    void CircuitStock(uint vendor, string name, int shown, int left) {
        lock (circuitVendors) {
            LoadCircuit();
            CircuitVendor v; if (!circuitVendors.TryGetValue(vendor, out v)) circuitVendors[vendor] = v = new CircuitVendor { Serial = vendor };
            if (name.Length > 0) v.Name = name;
            if (Atlas != null) foreach (var n in Atlas.Visible()) if (n.Serial == vendor) { v.X = n.X; v.Y = n.Y; if (v.Name.Length == 0) v.Name = n.Name; }
            var now = System.DateTime.Now;
            if (v.BoughtOutAt > v.StockAt && shown > 0) { v.Restocks.Add(new[] { shown }); v.RestockTicks.Add(now.Ticks); if (v.Restocks.Count > 50) { v.Restocks.RemoveAt(0); v.RestockTicks.RemoveAt(0); } }
            v.Stock = left; v.StockAt = now; if (left == 0) v.BoughtOutAt = now;
            SaveCircuit();
        }
    }
    public System.Collections.Generic.List<CircuitVendor> CircuitVendors() {
        lock (circuitVendors) { LoadCircuit(); return new System.Collections.Generic.List<CircuitVendor>(circuitVendors.Values); }
    }
    // What the pack holds for the circuit: thread, bolts, cloth, shirts, gold and the recall reagents.
    public string CircuitPack() {
        var k = link; if (k == null || k.Closed) return "no game connection";
        int gold = PackCount(k, g => g == GoldArt); lock (k.State.Gate) gold = System.Math.Max(gold, k.State.Gold);
        var r = new System.Collections.Generic.List<string>(); foreach (int g in RecallReagents) r.Add(UoTiles.Name(g) + " " + PackCount(k, x => x == g));
        return "Gold " + gold + ", thread " + PackCount(k, g => g == 0x0FA0 || g == 0x0FA1) + ", bolts " + PackCount(k, IsBolt) + ", cloth piles " + PackCloth(k).Count
            + ", for sale " + PackCount(k, IsShirtArt) + "; " + string.Join(", ", r);
    }

    // ---------- the tailoring circuit's steps (assist-plan.md stage 6, step 1 and 2) ----------
    // "vendor buy" and "vendor sell" open the shop windows (0x24 gump 0x30 with the stock 0x3C and the buy list 0x74; the
    // sell list 0x9E); while the assist asks (ShopClaimMs from its line) those windows go to the assist and not to the
    // client's screen, and the assist answers them as the client would.
    public const int ShopClaimMs = 4000, GoldArt = 0x0EED, BuyGump = 0x30;
    System.DateTime shopClaimUntil = System.DateTime.MinValue;
    bool HoldShop(byte[] p) {
        // A bag the assist opened to read (Peek) shows no window on the client (Damian, 2026-10-17: "lets just inject the
        // right messages instead of user emulation of clicks unless absolutely necessary").
        if (p[0] == 0x24 && p.Length >= 7) { uint s = U32(p, 1); lock (peeking) if (peeking.Remove(s)) return true; }
        if (System.DateTime.UtcNow > shopClaimUntil) return false;
        return p[0] == 0x74 || p[0] == 0x9E || (p[0] == 0x24 && p.Length >= 7 && ((p[5] << 8) | p[6]) == BuyGump);
    }
    // Opens a container for its contents (0x3C) without a window on the client. Null, or why not.
    readonly System.Collections.Generic.HashSet<uint> peeking = new System.Collections.Generic.HashSet<uint>();
    public string Peek(uint bag) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        lock (actionGate) {
            Answered(k, AnswerMs);
            var got = new System.Threading.ManualResetEventSlim(false);
            System.Action<byte[]> h = p => { if (p[0] == 0x3C && p.Length >= 5 && ((p[3] << 8) | p[4]) > 0 && U32(p, 5 + 13) == bag) got.Set(); else if (p[0] == 0x24 && p.Length >= 7 && U32(p, 1) == bag) got.Set(); };
            lock (peeking) peeking.Add(bag);
            k.ServerPacket += h;
            // The open is a double-click: it leaves the player busy, and the next action waits it out (a loom's thread
            // used 1.2 s after a peek was refused, "You must wait to perform another action.", 2026-10-17 00:34:15).
            bool opened;
            try { string w = k.Use(bag, false); if (w != null) return w; harvestActAt = clickedAt = System.DateTime.UtcNow; opened = got.Wait(AnswerMs); System.Threading.Thread.Sleep(200); }
            finally { k.ServerPacket -= h; lock (peeking) peeking.Remove(bag); }
            return opened ? null : "the bag " + bag.ToString("X") + " sent no contents in " + AnswerMs + " ms";
        }
    }
    // A marked rune renamed (Damian, 2026-10-17: "you can 'use' a rune to rename it"): its double-click asks for a
    // description by a text prompt (0x9A: serial, prompt id, type, text), kept off the client, and answered as the client
    // answers one (0x9A: the serial and prompt id echoed, type 1 for ok, the text and its NUL). Null, or why not.
    System.DateTime promptClaimUntil = System.DateTime.MinValue;
    bool HoldPrompt(byte[] p) { return p[0] == 0x9A && System.DateTime.UtcNow <= promptClaimUntil; }
    public string Rename(uint rune, string text) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        lock (actionGate) {
            Answered(k, AnswerMs); BusyWait(clickedAt);
            byte[] prompt = null; var got = new System.Threading.ManualResetEventSlim(false);
            System.Action<byte[]> h = p => { if (p[0] == 0x9A && p.Length >= 15 && U32(p, 3) == rune) { prompt = p; got.Set(); } };
            k.ServerPacket += h; promptClaimUntil = System.DateTime.UtcNow.AddMilliseconds(AnswerMs);
            try { string w = k.Use(rune, false); if (w != null) return w; clickedAt = System.DateTime.UtcNow; got.Wait(AnswerMs); }
            finally { k.ServerPacket -= h; promptClaimUntil = System.DateTime.MinValue; }
            if (prompt == null) return "the rune " + rune.ToString("X") + " asked for no description in " + AnswerMs + " ms";
            k.Settle();
            var t = System.Text.Encoding.ASCII.GetBytes(text); var r = new byte[16 + t.Length];
            r[0] = 0x9A; r[1] = (byte)(r.Length >> 8); r[2] = (byte)r.Length; System.Array.Copy(prompt, 3, r, 3, 8); r[14] = 1; t.CopyTo(r, 15);
            string why = k.Inject(r, "rename " + rune.ToString("X"), false); if (why != null) return why;
            // The bank's label cache reads a rune once by serial, so a renamed rune is relabelled there too.
            LoadBankRunes(); lock (bankRunes) { if (bankRunes.ContainsKey(rune)) { bankRunes[rune] = text; var lines = new System.Collections.Generic.List<string>(); foreach (var kv in bankRunes) lines.Add(kv.Key.ToString("X") + "\t" + kv.Value); try { System.IO.File.WriteAllLines(BankRunesPath, lines); } catch { } } }
            return null;
        }
    }
    // Every bag in the pack whose contents are not known opened once, so the pack's item count is the server's; a book is
    // not a bag (the spellbook's scrolls came in as items), and the next action waits out the last open ("You must wait
    // to perform another action." at 09:10:22, a kit used 375 ms after it).
    void OpenPackBags(GameLink k) {
        var bags = new System.Collections.Generic.List<uint>();
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && IsContainer(it.Graphic) && !UoTiles.Name(it.Graphic).ToLowerInvariant().Contains("book")) bags.Add(it.Serial); }
        bool opened = false;
        foreach (var b in bags) { bool known; lock (contents) known = contents.ContainsKey(b); if (known) continue; BusyWait(clickedAt); string w = Peek(b); opened = true; if (w != null) Note("Pack: opening " + b.ToString("X") + ": " + w); }
        if (opened) BusyWait(clickedAt);
    }
    // Whether the pack's counts are incomplete: the pack, or a bag in it, not opened on this connection (Damian,
    // 2026-10-18: "there should be a 'count' button there in the header if the data is unintialized or stale").
    public bool PackStale() {
        var k = link; if (k == null || k.Closed) return false;
        uint pack; lock (k.State.Gate) pack = k.State.Backpack; if (pack == 0) return true;
        lock (contents) if (!contents.ContainsKey(pack)) return true;
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && IsContainer(it.Graphic) && !UoTiles.Name(it.Graphic).ToLowerInvariant().Contains("book")) lock (contents) if (!contents.ContainsKey(it.Serial)) return true; }
        return false;
    }
    // The pack, then every bag in it not yet known, opened once each. Null, or why not.
    public string CountPack() {
        var k = link; if (k == null || k.Closed) return "no game connection";
        uint pack; lock (k.State.Gate) pack = k.State.Backpack; if (pack == 0) return "the pack is not known: double-click yourself once";
        bool known; lock (contents) known = contents.ContainsKey(pack);
        if (!known) { BusyWait(clickedAt); string w = Peek(pack); if (w != null) return "opening the pack: " + w; }
        OpenPackBags(k); return null;
    }
    int PackCount(GameLink k, System.Func<int, bool> graphic) {
        // An item that does not stack is one, whatever amount the server's 0x3C gives it (uo1998 sent 176 for one fancy shirt).
        int n = 0; lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && graphic(it.Graphic)) n += UoTiles.Stacks(it.Graphic) ? System.Math.Max(1, it.Amount) : 1; }
        return n;
    }
    // A vendor trades only inside its shop, a small range; engaged elsewhere it teleports home, or says something and walks
    // home slowly (Damian, 2026-10-17: "you have to wait till they get there. stay out of their way until they land
    // home"). Asked by name and unanswered, the asking waits until that vendor has stood still VendorStillMs (at most
    // VendorHomeMs), then asks again, ShopAsks times in all. The circuit never walks, so it keeps out of the way.
    // Asked once where it stands (Damian, 2026-10-17: "it shouldn't repeated ask to buy over and over again ... or any
    // command"); AskNear's one more ask goes out only at the vendor's home.
    // Home is told by timing, as nothing announces it (Damian, 2026-10-17: "about 1 second ticks on walks, then a 5 second
    // stop is usually an indication they are home"): the vendor's tile from the server's mobile updates, home once it has
    // not moved VendorStillMs; a jump of more than VendorJump tiles is a teleport home ("sometimes they teleport when you
    // ask, and immediate land on home"), home after VendorLandMs still.
    // Where the vendor's region is known (VendorAtHome), home is that region and nothing else: the wait ends when the
    // vendor stands in it. False when the wait ran out with the vendor away.
    public const int ShopAsks = 1, VendorStillMs = 5000, VendorHomeMs = 60000, VendorJump = 3, VendorLandMs = 1000;
    // sentHome: the vendor has just answered "Let us go back to my shop", which overrules the shop region: it is home only
    // once it has moved and then stood still, and one that never moves in VendorHomeMs stayed away (Allison in The
    // Sorcerer's Delight, inside her region, was asked three times in 5 s and ended the buy circuit, 2026-10-18 12:46:45).
    bool WaitVendorHome(GameLink k, string who, bool sentHome = false) {
        var v = VendorNamed(k, who); if (v == null) { System.Threading.Thread.Sleep(VendorStillMs); return true; }
        var until = System.DateTime.UtcNow.AddMilliseconds(VendorHomeMs); int lx = int.MinValue, ly = 0, need = VendorStillMs, moves = 0; var still = System.DateTime.UtcNow;
        while (System.DateTime.UtcNow < until && !k.Closed && !circuitStop) {
            var home = VendorAtHome(k, v.Serial); if (home == true && !sentHome) return true;
            int[] at; bool seen; lock (k.State.Gate) seen = k.State.Mobiles.TryGetValue(v.Serial, out at);
            if (seen && (at[0] != lx || at[1] != ly)) {
                need = lx != int.MinValue && Tiles(at[0], at[1], lx, ly) > VendorJump ? VendorLandMs : VendorStillMs;
                if (lx != int.MinValue) moves++;
                lx = at[0]; ly = at[1]; still = System.DateTime.UtcNow;
            }
            if ((sentHome ? moves > 0 : home == null) && (System.DateTime.UtcNow - still).TotalMilliseconds >= need) return true;
            System.Threading.Thread.Sleep(100);
        }
        return !sentHome && VendorAtHome(k, v.Serial) != false;
    }
    // The shopkeeper by first name, the one in view nearest the player.
    AtlasNpc VendorNamed(GameLink k, string who) {
        if (Atlas == null || who == "vendor") return null; int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
        AtlasNpc best = null; foreach (var n in Atlas.Visible()) if (string.Equals(n.Name.Split(' ')[0], who, System.StringComparison.OrdinalIgnoreCase) && (best == null || Tiles(n.X, n.Y, px, py) < Tiles(best.X, best.Y, px, py))) best = n;
        return best;
    }
    // Each vendor's home as the server judges it (human.m: the smallest region holding the vendor, z included, named as
    // its myHomeShop): Cybil's spot on the Magincia mage shop roof, z 60, lies above MAGEGUILD_MAGINCIA's z 20..59, and she
    // answered every ask there "Let us go back to my shop" (2026-10-17 02:56). Home is the region key (UoRegions.AreaKey)
    // it answered in, "*" once it answered in two (a vendor with no region trades anywhere); Away the keys it sent the
    // player home from. Kept in vendor-homes-<server>.txt (serial, home, away keys by commas).
    sealed class VendorHome { public string Home = ""; public readonly System.Collections.Generic.HashSet<string> Away = new System.Collections.Generic.HashSet<string>(); }
    readonly System.Collections.Generic.Dictionary<uint, VendorHome> vendorHomes = new System.Collections.Generic.Dictionary<uint, VendorHome>(); bool vendorHomesLoaded;
    string VendorHomesPath { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "vendor-homes-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"); } }
    void LoadVendorHomes() {
        lock (vendorHomes) if (!vendorHomesLoaded) {
            vendorHomesLoaded = true;
            try { if (System.IO.File.Exists(VendorHomesPath)) foreach (var line in System.IO.File.ReadAllLines(VendorHomesPath)) { var t = line.Split('\t'); if (t.Length < 2 || t.Length > 3) continue; var h = new VendorHome { Home = t[1] }; if (t.Length > 2) foreach (var a in t[2].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)) h.Away.Add(a); vendorHomes[System.Convert.ToUInt32(t[0], 16)] = h; } } catch { }
        }
    }
    void SaveVendorHomes() {
        var lines = new System.Collections.Generic.List<string>(); foreach (var kv in vendorHomes) lines.Add(kv.Key.ToString("X") + "\t" + kv.Value.Home + "\t" + string.Join(",", kv.Value.Away));
        try { System.IO.File.WriteAllLines(VendorHomesPath, lines); } catch { }
    }
    // The region key where the vendor stands now, with where for a report; null when out of view or no region holds it.
    string VendorArea(GameLink k, uint serial, out string where) {
        int[] at; where = "out of view"; lock (k.State.Gate) if (!k.State.Mobiles.TryGetValue(serial, out at)) return null;
        string key = UoRegions.AreaKey(at[0], at[1], at[2]); where = (key ?? "no region") + " at " + at[0] + "," + at[1] + " z " + at[2]; return key;
    }
    // True at home, false where it sent the player home from or outside a known home, null when nothing is known.
    bool? VendorAtHome(GameLink k, uint serial) {
        string where, key = VendorArea(k, serial, out where); if (key == null) return null;
        LoadVendorHomes(); VendorHome h; lock (vendorHomes) { if (!vendorHomes.TryGetValue(serial, out h)) return null; if (h.Home == "*") return true; if (h.Home.Length > 0) return key == h.Home; return h.Away.Contains(key) ? (bool?)false : null; }
    }
    void NoteVendorHome(GameLink k, string who) {
        var v = VendorNamed(k, who); if (v == null) return; string where, key = VendorArea(k, v.Serial, out where); if (key == null) return;
        LoadVendorHomes();
        lock (vendorHomes) {
            VendorHome h; if (!vendorHomes.TryGetValue(v.Serial, out h)) vendorHomes[v.Serial] = h = new VendorHome();
            string home = h.Home.Length == 0 || h.Home == key ? key : "*";
            if (home == h.Home && !h.Away.Contains(key)) return;
            h.Home = home; h.Away.Remove(key); SaveVendorHomes();
        }
    }
    void NoteVendorAway(GameLink k, string who) {
        var v = VendorNamed(k, who); if (v == null) return; string where, key = VendorArea(k, v.Serial, out where); if (key == null) return;
        LoadVendorHomes();
        lock (vendorHomes) { VendorHome h; if (!vendorHomes.TryGetValue(v.Serial, out h)) vendorHomes[v.Serial] = h = new VendorHome(); if (h.Home == key || h.Home == "*" || !h.Away.Add(key)) return; SaveVendorHomes(); }
        Note("Circuit: " + who + " is away from its shop in " + where);
    }
    // The vendor's home region nearest it: centre and z range, or null when its home is not known.
    int[] VendorHomeRegion(GameLink k, AtlasNpc v) { LoadVendorHomes(); VendorHome h; lock (vendorHomes) if (!vendorHomes.TryGetValue(v.Serial, out h) || h.Home.Length == 0 || h.Home == "*") return null; return UoRegions.AreaNearest(h.Home, v.X, v.Y); }
    // A shop ask goes out SellGapMs after the last shop window: "Slade buy" said the instant Bertram's window came was
    // answered by Bertram and never by Slade, which ended the survey (2026-10-17 02:12:40).
    System.DateTime lastShopWindow = System.DateTime.MinValue;
    byte[] AskShop(GameLink k, string who, string what, int opcode) {
        for (int ask = 1; ask <= ShopAsks; ask++) {
            var wait = lastShopWindow.AddMilliseconds(SellGapMs) - System.DateTime.UtcNow; if (wait > System.TimeSpan.Zero) System.Threading.Thread.Sleep(wait);
            var got = SayFor(k, who + " " + what, opcode, AnswerMs, who);
            if (got != null) lastShopWindow = System.DateTime.UtcNow;
            if (got == null && LastReply != null) return null;
            if (got != null) return got;
            if (ask < ShopAsks) { Note("Circuit: " + who + " did not answer \"" + what + "\"; waiting for them to be home"); WaitVendorHome(k, who); }
        }
        return null;
    }
    // A spoken line answered by a window (opcode) or by speech, and both are watched (Damian, 2026-10-17: "a command that
    // might open a window or return a string in chat needs to be not one or the other checked, but both"): the window
    // wins; speech from the one asked (from, a name; null for anyone) with no window within ReplyGraceMs is the answer,
    // kept in LastReply ("I don't understand", "You have nothing I would be interested in.", "You can only sell 5 items
    // at a time!"). A greeting before the window ("Greetings.  Have a look around.") waits out the grace for it.
    public const int ReplyGraceMs = 700;
    // SayRefused: why the line was not said (GameLink.Say refused it), null when it went out; the answer is then null with
    // no LastReply, and the caller reports the refusal, not a silent NPC.
    public volatile string LastReply, SayRefused;
    // OtherWindow: the asked window came during the wait, but from another vendor than the one asked.
    public volatile bool OtherWindow;
    // NotHome: why AskNear passed the vendor over unasked, away from its shop (VendorAtHome); null when it asked.
    public volatile string NotHome;
    // A spoken answer can take longer than an action's: Simon's came 1.55 s after the ask, past an auto-tuned 1500 ms
    // answer wait, and was read as silence (Trinsic, 2026-10-17). The wait is SpeechWaitMs at least; a window ends it at once.
    public const int SpeechWaitMs = 4000;
    byte[] SayFor(GameLink k, string line, int opcode, int ms, string from = null) {
        ms = System.Math.Max(ms, SpeechWaitMs);
        LastReply = null; SayRefused = null; OtherWindow = false;
        byte[] got = null; string reply = null; var replyAt = System.DateTime.MaxValue; var done = new System.Threading.ManualResetEventSlim(false);
        uint me; lock (k.State.Gate) me = k.State.Player;
        // A window answers only from the vendor asked, where its name is known: Judd's buy list arrived as the answer to "Tate
        // buy" (Vesper, 2026-10-17), and Tate's thread was recorded as none. The sell list (0x9E) names its vendor; the buy
        // list (0x74) names the stock box, worn by its vendor.
        System.Func<byte[], bool> fromAsked = p => {
            if (from == null || from == "vendor" || p.Length < 7) return true;
            uint v = U32(p, 3); if (p[0] == 0x74) { uint wv; lock (wornBy) if (!wornBy.TryGetValue(v, out wv)) return true; v = wv; }
            string n; lock (k.State.Gate) if (!k.State.Names.TryGetValue(v, out n)) return true;
            return string.Equals(n.Split(' ')[0], from, System.StringComparison.OrdinalIgnoreCase);
        };
        System.Action<byte[]> h = p => {
            if (p[0] == opcode && got == null) { if (!fromAsked(p)) { OtherWindow = true; return; } got = p; done.Set(); return; }
            // 0x1C: serial, body, type (9), hue, font, name (14, 30), text (44); a label (6) is not an answer.
            if (p[0] == 0x1C && p.Length > 44 && reply == null) {
                uint s = U32(p, 3); if (s == me || s == 0xFFFFFFFF || p[9] == 6) return;
                string name = System.Text.Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
                if (from != null && from != "vendor" && !string.Equals(name, from, System.StringComparison.OrdinalIgnoreCase)) return;
                reply = name + ": " + System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0'); replyAt = System.DateTime.UtcNow;
            }
        };
        k.ServerPacket += h; shopClaimUntil = System.DateTime.UtcNow.AddMilliseconds(ShopClaimMs);
        try {
            string refused = k.Say(line); if (refused != null) { SayRefused = refused; return null; }
            var by = System.DateTime.UtcNow.AddMilliseconds(ms);
            while (System.DateTime.UtcNow < by && !k.Closed) {
                if (done.Wait(50)) break;
                // Anyone's speech (from null, the bank's "bank") is kept but never ends the wait: chatter nearby is not
                // an answer.
                if (reply != null && from != null && (System.DateTime.UtcNow - replyAt).TotalMilliseconds >= ReplyGraceMs) break;
            }
        } finally { k.ServerPacket -= h; }
        LastReply = got == null ? reply : null;
        if (got != null) k.Heard(line);
        return got;
    }
    // A line said from the control port, answered when it opens a window (0x24) within AnswerMs. Null, or why not said.
    public string SayHeard(string text) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        lock (actionGate) { SayFor(k, text, 0x24, AnswerMs); return SayRefused; }
    }
    // Why "bank" opened no box: the line refused before it went out, or no banker answered it.
    string NoBanker() { var r = SayRefused; return r != null ? "\"bank\" not said: " + r : "no banker answered \"bank\""; }
    // What an NPC's spoken answer means, from the lines uo1998 gave on 2026-10-17; a line matching none is Unknown, and
    // goes up to whoever drives the assist (Damian, 2026-10-17: "these unknown text responses that the bot gets need to
    // be kicked up to you monitoring so you can interpret any new text we haven't seen before").
    public enum Reply { None, Vendor, NotTrader, NoInterest, GoingHome, AskAgain, SellLimit, Unknown }
    // The specific kinds first: "I do not have my wares here with me" also holds a greeting's "my wares". The wording can
    // change any day (Damian, 2026-10-17: "tomorrow the devs could change the reponse slightly so we need agent to pay
    // attention here"), and a changed line falls to Unknown and is kicked up rather than guessed at.
    static readonly string[][] ReplyLines = {
        // "... thou must give it to me and I'll decide if I need it in my stock" (Simon, Trinsic, 2026-10-17, asked away from
        // his post after buying 99): the vendor not at its shop spot (Damian: "so that vendor walked back to its shop
        // space"); asked once more there.
        new[] { "GoingHome", "do not have my wares here", "go back to my shop", "thou must give it to me" },
        new[] { "NotTrader", "do not understand", "fail to follow", "do not quite follow", "meaning escapes", "fail to grasp" },
        // "... hand it over and I shall determine its value." (Jannelle, Nujel'm, after buying 5) and "Hand it to me and I
        // shall decide its worth." (Sophie, Moonglow): misleading, not a refusal (Damian, 2026-10-17: "the hand it to me line
        // is misleading"; he asked again and sold); one more ask after SellGapMs.
        // The rest of the 1998 shopkeeper conversation's answers to "sell" for a player of good name (rundir/convo/
        // shopkeep.frg, high, medium and low sophistication), one picked at random: Michelle's "I may be interested in
        // what thou have-let me see it." after buying 10 (2026-10-17 03:30) ended the circuit as unknown.
        new[] { "AskAgain", "hand it over and i shall determine", "hand it to me and i shall decide", "let me see it", "give it to me and i shall value",
            "hand them to me for appraisal", "let me see what thou art offering", "give it here", "hand it over and i'll see", "give 'em to me for a once-over",
            "give me what thou'rt wantin' to sell" },
        // "I am a tailor. I could purchase those things that I supply." (Nana, Magincia, 2026-10-17, asked 0.7 s after a sale
        // of 5, see SellGapMs): no window this time; the circuit moves on from her rather than stopping.
        new[] { "NoInterest", "nothing i would be interested in", "purchase those things that i supply" },
        new[] { "SellLimit", "only sell 5 items" },
        new[] { "Vendor", "have a look around", "thou seekest", "sell to thee", "buy from me", "my wares", "purchase supplies", "my shop sells", "greetings" },
    };
    public static Reply KindOf(string reply) {
        if (reply == null) return Reply.None; string t = reply.Substring(reply.IndexOf(": ") + 2).ToLowerInvariant();
        foreach (var row in ReplyLines) for (int i = 1; i < row.Length; i++) if (t.Contains(row[i])) return (Reply)System.Enum.Parse(typeof(Reply), row[0]);
        return Reply.Unknown;
    }
    public readonly System.Collections.Generic.List<string> UnknownReplies = new System.Collections.Generic.List<string>();
    void KickUp(string asked, string reply) {
        string row = System.DateTime.Now.ToString("HH:mm:ss") + "\t" + asked + "\t" + reply;
        lock (UnknownReplies) UnknownReplies.Add(row);
        Note("UNKNOWN REPLY to \"" + asked + "\": " + reply);
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "unknown-replies.txt"), row + System.Environment.NewLine); } catch { }
    }
    // An ask that got no proper answer (not said, nobody answered, not a trader, an unknown line, or a greeting with no
    // window): the code that asked stops there (assist-plan.md rule 1) rather than asking the next vendor. "Nothing I would
    // be interested in" and an offer under the floor are answers, and the circuit moves on from them.
    static bool Silent(string why) {
        if (why == null) return false;
        foreach (var s in new[] { "no vendor answered", "not said", "UNKNOWN REPLY", "NotTrader", "GoingHome", "Vendor " }) if (why.StartsWith(s)) return true;
        return false;
    }
    // Why an ask failed, in the words given: the reply and its kind, or that nobody answered.
    string NoAnswer(string who, string word) {
        if (NotHome != null) return "away: " + NotHome;
        var r = LastReply; if (r == null) return SayRefused != null ? "not said: " + SayRefused : "no vendor answered \"" + who + " " + word + "\"";
        var kind = KindOf(r); return (kind == Reply.Unknown ? "UNKNOWN REPLY " : kind + " ") + who + " answered \"" + word + "\" with \"" + r.Substring(r.IndexOf(": ") + 2) + "\"";
    }
    // Buys up to want of the stock whose art is one of graphics, as far as the pack's gold goes: every such stack bought
    // out ("each time if they have sold out of thread, the next respawn gets more"). Null, or why not; bought and the
    // stock left come back.
    // who: "vendor" asks every vendor in earshot; a vendor's name ("Morena buy") asks that one (Damian, 2026-10-17: "you
    // can use the name of the vendor").
    // maxPrice: a stock priced above it is not bought (Damian, 2026-10-17: "250 of them @5gp each").
    public string VendorBuy(System.Func<int, bool> graphic, int want, out int bought, out int left, string who = "vendor", int maxPrice = int.MaxValue) {
        bought = 0; left = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        string near = NearVendor(k, who); if (near != null) return near;
        var list = AskNear(k, who, "buy", 0x74);
        lock (actionGate) {
            if (list == null || list.Length < 8) return NoAnswer(who, "buy");
            uint box = U32(list, 3);
            System.Collections.Generic.List<uint> order; System.Collections.Generic.List<int> stock;
            lock (contents) { contents.TryGetValue(box, out order); contentAmounts.TryGetValue(box, out stock); }
            if (order == null) return "the vendor's stock was not sent";
            var prices = new System.Collections.Generic.List<int>(); int at = 8;
            var listed = new System.Collections.Generic.List<int>();
            for (int i = 0; i < list[7] && at + 5 <= list.Length; i++) { listed.Add((int)U32(list, at)); int len = list[at + 4]; at += 5 + len; }
            // Into stock order (StockIndex): prices[i] is the price of order[i].
            for (int i = 0; i < listed.Count; i++) prices.Add(0);
            for (int i = 0; i < listed.Count; i++) { int si = StockIndex(i, listed.Count, order.Count); if (si >= 0) prices[si] = listed[i]; }
            // The vendor wearing the stock, as the atlas finds it; else the nearest shopkeeper in sight.
            uint vendor = 0; lock (wornBy) wornBy.TryGetValue(box, out vendor);
            if (vendor == 0) { AssistItem boxItem = Item(k, box); if (boxItem != null) vendor = boxItem.Mobile != 0 ? boxItem.Mobile : boxItem.Container; }
            if (vendor == 0 && Atlas != null) {
                int px, py, best = int.MaxValue; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                foreach (var n in Atlas.Visible()) if (n.Type == "Shopkeeper") { int d = Tiles(n.X, n.Y, px, py); if (d < best) { best = d; vendor = n.Serial; } }
            }
            if (vendor == 0) return "the vendor wearing the stock " + box.ToString("X") + " is not known";
            // The status's gold (0x11) counts the pack before its contents are known (after login, unopened).
            int gold = PackCount(k, g => g == GoldArt); lock (k.State.Gate) gold = System.Math.Max(gold, k.State.Gold);
            var picks = new System.Collections.Generic.List<uint[]>(); int cost = 0, shown = 0, overPrice = 0;
            for (int i = 0; i < order.Count && i < prices.Count; i++) { var it = Item(k, order[i]); if (it != null && graphic(it.Graphic) && stock[i] > 0) shown += stock[i]; }
            for (int i = 0; i < order.Count && i < prices.Count && bought < want; i++) {
                var it = Item(k, order[i]); if (it == null || !graphic(it.Graphic)) continue;
                int have = System.Math.Max(1, stock[i]), price = System.Math.Max(1, prices[i]);
                if (price > maxPrice) { left += have; overPrice = System.Math.Max(overPrice, price); continue; }
                int take = System.Math.Min(have, System.Math.Min(want - bought, (gold - cost) / price));
                left += have - System.Math.Max(0, take);
                if (take <= 0) continue;
                picks.Add(new uint[] { order[i], (uint)take }); bought += take; cost += take * price;
            }
            // Only a thread buy goes in the circuit's record: the circuit takes every vendor there for a thread seller and
            // a shirt buyer, and offered shirts to the mages and the stable a reagent and a horse buy had recorded
            // (Damian, 2026-10-17: "the bot is trying to sell to mage").
            bool threadBuy = graphic(0x0FA0) || graphic(0x0FA1);
            if (picks.Count == 0) { k.Settle(); if (threadBuy) CircuitStock(vendor, who == "vendor" ? "" : who, shown, shown); return overPrice > 0 ? "the vendor asks " + overPrice + ", over " + maxPrice : left > 0 ? "the pack's " + gold + " gold buys none of it" : "the vendor has none"; }
            long mark = k.State.JournalSeq;
            string why = k.Buy(vendor, picks); if (why != null) return why;
            // The vendor's own total ("The total of thy purchase is 36 gold."): Jannelle's last 9 thread cost 4 each against
            // the listed 3 (2026-10-17 00:19:39), so the record takes what was charged.
            var totalBy = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); int charged = 0;
            while (charged == 0 && System.DateTime.UtcNow < totalBy && !k.Closed) {
                foreach (var j in k.State.JournalSince(mark)) { int t = j.IndexOf("total of thy purchase is ", System.StringComparison.OrdinalIgnoreCase); if (t >= 0) { var w = j.Substring(t + 25).Split(' '); int n; if (w.Length > 0 && int.TryParse(w[0], out n)) charged = n; } }
                if (charged == 0) System.Threading.Thread.Sleep(50);
            }
            if (charged > 0 && cost > 0 && charged != cost) { for (int i = 0; i < prices.Count; i++) prices[i] = prices[i] * charged / cost; cost = charged; }
            var sent = new byte[8 + 7 * picks.Count]; sent[0] = 0x3B; sent[3] = (byte)(vendor >> 24); sent[4] = (byte)(vendor >> 16); sent[5] = (byte)(vendor >> 8); sent[6] = (byte)vendor; sent[7] = 2;
            for (int i = 0; i < picks.Count; i++) { uint s = picks[i][0]; sent[8 + i * 7] = 0x1A; sent[9 + i * 7] = (byte)(s >> 24); sent[10 + i * 7] = (byte)(s >> 16); sent[11 + i * 7] = (byte)(s >> 8); sent[12 + i * 7] = (byte)s; sent[13 + i * 7] = (byte)(picks[i][1] >> 8); sent[14 + i * 7] = (byte)picks[i][1]; }
            lock (offers) { offers.Clear(); for (int i = 0; i < order.Count && i < prices.Count; i++) { var it = Item(k, order[i]); offers[order[i]] = new object[] { it != null ? UoTiles.Name(it.Graphic) : "item", prices[i], vendor }; } }
            Traded(k, sent);
            System.Threading.Thread.Sleep(DropSettleMs);
            if (threadBuy) CircuitStock(vendor, who == "vendor" ? "" : who, shown, left);
            Note("Circuit: bought " + bought + " for " + cost + " gold from " + (who == "vendor" ? vendor.ToString("X") : who) + "; " + left + " left in stock");
            return null;
        }
    }
    // Sells every item of these arts the vendor offers at least floor for. Null, or why not; sold, the gold, and the best
    // price offered under the floor come back.
    // most: the items sold at most (the tour sells one to each tailor to read its price).
    public string VendorSell(System.Func<int, bool> graphic, int floor, out int sold, out int gold, out int under, string who = "vendor", int most = int.MaxValue) {
        return VendorSell(graphic, g => floor, out sold, out gold, out under, who, most);
    }
    // floorOf: the lowest price for each art (the sell list's, FloorOf).
    // serialOk: only these pieces (an order's spares, OrderPick).
    public string VendorSell(System.Func<int, bool> graphic, System.Func<int, int> floorOf, out int sold, out int gold, out int under, string who = "vendor", int most = int.MaxValue, System.Func<uint, bool> serialOk = null) {
        sold = 0; gold = 0; under = 0; var k = link; if (k == null || k.Closed) return "no game connection";
        ManualStart();
        string near = NearVendor(k, who); if (near != null) return near;
        // A sale takes at most SellBatch items (Damian, 2026-10-17: "you can only sell 5 items at a time"; his own sales
        // went 5 at a time, trades 2026-10-16), so the vendor is asked again after each until it offers none at the floor.
        uint vendor = 0; string end = null;
        while (!circuitStop && !k.Closed && sold < most) {
            lock (actionGate) {
                Answered(k, AnswerMs);
                var list = AskNear(k, who, "sell", 0x9E);
                // A later batch's ask that got no proper answer is reported, the shirts already sold with it.
                if (list == null || list.Length < 9) { string na = NoAnswer(who, "sell"); end = sold > 0 && !Silent(na) ? null : na; break; }
                vendor = U32(list, 3); int n = (list[7] << 8) | list[8], at = 9, batchGold = 0, batchItems = 0;
                var picks = new System.Collections.Generic.List<uint[]>();
                for (int i = 0; i < n && at + 14 <= list.Length; i++) {
                    uint s = U32(list, at); int g = (list[at + 4] << 8) | list[at + 5], amount = (list[at + 8] << 8) | list[at + 9], price = (list[at + 10] << 8) | list[at + 11], len = (list[at + 12] << 8) | list[at + 13];
                    at += 14 + len;
                    if (!graphic(g) || (serialOk != null && !serialOk(s))) continue;
                    if (price < floorOf(g)) { under = System.Math.Max(under, price); continue; }
                    // The 5 counts a stack's amount, not its stacks (uo1998 2026-10-17: a stack of 129 Spider's Silk
                    // answered "You can only sell 5 items at a time!"): a stack goes 5 at a time.
                    int room = System.Math.Min(SellBatch, most - sold) - batchItems; if (room <= 0) continue;
                    int a = System.Math.Min(System.Math.Max(1, amount), room); picks.Add(new uint[] { s, (uint)a, (uint)System.Math.Max(1, amount) }); batchGold += a * price; batchItems += a;
                }
                if (picks.Count == 0) { if (sold == 0) end = under > 0 ? "the vendor pays " + under + ", under the lowest price" : "the vendor wants none of them"; break; }
                string why = k.Sell(vendor, picks); if (why != null) { end = why; break; }
                var sent = new byte[9 + 6 * picks.Count]; sent[0] = 0x9F; sent[3] = (byte)(vendor >> 24); sent[4] = (byte)(vendor >> 16); sent[5] = (byte)(vendor >> 8); sent[6] = (byte)vendor;
                for (int i = 0; i < picks.Count; i++) { uint s = picks[i][0]; sent[9 + i * 6] = (byte)(s >> 24); sent[10 + i * 6] = (byte)(s >> 16); sent[11 + i * 6] = (byte)(s >> 8); sent[12 + i * 6] = (byte)s; sent[13 + i * 6] = (byte)(picks[i][1] >> 8); sent[14 + i * 6] = (byte)picks[i][1]; }
                Traded(k, sent);
                System.Threading.Thread.Sleep(DropSettleMs);
                // Counted only when the server took them: an item still in the pack was not sold.
                // A stack sold in part stays in the pack with less: the drop in its amount is what sold.
                int took = 0, offeredCount = 0; foreach (var pk in picks) { offeredCount += (int)pk[1]; var left = Item(k, pk[0]); took += left == null ? (int)pk[1] : System.Math.Max(0, (int)pk[2] - System.Math.Max(1, left.Amount)); }
                sold += took; gold += batchGold * took / System.Math.Max(1, offeredCount);
                if (took == 0) { end = sold > 0 ? null : "the vendor took none of the " + picks.Count + " offered"; break; }
                // The next batch's ask waits SellGapMs: asked 0.7 s after a sale, Nana answered "I am a tailor. I could
                // purchase those things that I supply." with no window, and bought at a person's pace (Magincia, 2026-10-17).
                if (sold < most) System.Threading.Thread.Sleep(SellGapMs);
            }
        }
        if (sold > 0) Note("Circuit: sold " + sold + " for " + gold + " gold to " + who + (under > 0 ? "; it offered " + under + " for others" : ""));
        return end;
    }
    public const int SellBatch = 5, SellGapMs = 2500;
    // The runes on the ground within reach, each single-clicked (0x09) for its label (0x1C from the rune: "a recall rune
    // for <place>"), nearest first: a rune library (Damian, 2026-10-17: "there is a rune library in the moat next to brit
    // bank. the names so far have been reliable"). Serial, label and tile each; a rune that answered nothing in
    // LabelWaitMs reads "no label".
    public const int LabelGapMs = 150;
    public System.Collections.Generic.List<string> RuneLabels(int reach) {
        var k = link; var list = new System.Collections.Generic.List<string>(); if (k == null || k.Closed) return list;
        var runes = new System.Collections.Generic.List<AssistItem>(); int px, py;
        lock (k.State.Gate) { px = k.State.X; py = k.State.Y; foreach (var g in k.State.Ground.Values) if (Tiles(g.X, g.Y, px, py) <= reach && UoTiles.Name(g.Graphic).ToLowerInvariant().Contains("rune")) runes.Add(g); }
        runes.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        return LabelsOf(k, runes);
    }
    // The pack's runes and their labels, the same way: the home rune and the spare one (Damian, 2026-10-17: "i put a
    // second rune in the backpack bag to skara brea in case the brit one is blocked by a character standing in the way").
    public System.Collections.Generic.List<string> PackRuneLabels(GameLink k) {
        OpenPackBags(k); var runes = new System.Collections.Generic.List<AssistItem>();
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17) runes.Add(it); }
        return LabelsOf(k, runes);
    }
    // Every item in one container labelled where it lies, a rune in the bank box read without moving it (Damian,
    // 2026-10-17: "learn how to get their names while they are still in the bank box").
    public System.Collections.Generic.List<string> ContainerLabels(uint box) {
        var k = link; var items = new System.Collections.Generic.List<AssistItem>(); if (k == null || k.Closed) return new System.Collections.Generic.List<string>();
        lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == box) items.Add(it);
        return LabelsOf(k, items);
    }
    // Every mobile in view within reach single-clicked for its name (Damian, 2026-10-17: the vendors of Serpent's Hold
    // showed "?"); the label answers name them in AssistState.Names as well.
    public System.Collections.Generic.List<string> MobileLabels(int reach) {
        var k = link; var list = new System.Collections.Generic.List<AssistItem>(); if (k == null || k.Closed) return new System.Collections.Generic.List<string>();
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) if (Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= reach) list.Add(new AssistItem { Serial = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
        return LabelsOf(k, list);
    }
    // Only the mobiles in reach with no name known yet, so a circuit pass clicks nobody twice.
    public void NameUnnamed(int reach) {
        var k = link; var list = new System.Collections.Generic.List<AssistItem>(); if (k == null || k.Closed) return;
        lock (k.State.Gate) foreach (var kv in k.State.Mobiles) if (!k.State.Names.ContainsKey(kv.Key) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= reach) list.Add(new AssistItem { Serial = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
        if (list.Count > 0) LabelsOf(k, list);
    }
    System.Collections.Generic.List<string> LabelsOf(GameLink k, System.Collections.Generic.List<AssistItem> runes) {
        var list = new System.Collections.Generic.List<string>();
        var labels = new System.Collections.Generic.Dictionary<uint, string>(); var wanted = new System.Collections.Generic.HashSet<uint>(); foreach (var r in runes) wanted.Add(r.Serial);
        System.Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44) { uint s = U32(p, 3); if (wanted.Contains(s)) lock (labels) labels[s] = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0'); } };
        k.ServerPacket += h;
        var hideUntil = System.DateTime.UtcNow.AddMilliseconds(LabelWaitMs + LabelGapMs * (runes.Count + 1));
        lock (looking) foreach (var r in runes) looking[r.Serial] = hideUntil;
        try {
            foreach (var r in runes) { lock (actionGate) k.Look(r.Serial); System.Threading.Thread.Sleep(LabelGapMs); }
            var by = System.DateTime.UtcNow.AddMilliseconds(LabelWaitMs);
            while (System.DateTime.UtcNow < by) { lock (labels) if (labels.Count == runes.Count) break; System.Threading.Thread.Sleep(50); }
        } finally { k.ServerPacket -= h; }
        foreach (var r in runes) { string t; lock (labels) labels.TryGetValue(r.Serial, out t); list.Add(r.Serial.ToString("X") + " " + (t ?? "no label") + " at " + r.X + "," + r.Y + "," + r.Z); }
        return list;
    }
    // Recall (spell 32, circle 4) at a rune in the pack or in reach: the reagents and mana first, retried on a fizzle
    // ("it sometimes fizzles but it works. be persistent"), done when the player has moved.
    public const int RecallSpell = 32, RecallMana = 11, RecallTries = 5, CastCursorMs = 6000, RecallMoveMs = 5000, RecallMoved = 10;
    static readonly string[] CastEnds = { "fizzle", "already casting", "insufficient", "not enough", "reagents", "You must wait", "cannot cast" };
    public string Recall(uint rune) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        for (int attempt = 1; attempt <= RecallTries; attempt++) {
            // A stop ends the tries (the circuit cast three more after "circuit stop", 2026-10-17).
            if (circuitStop && circuitThread != null && circuitThread.IsAlive) return "stopped";
            // The rune before the cast: a cast at a rune out of sight spent mana and left its cursor open (2026-10-17).
            bool seen; lock (k.State.Gate) seen = k.State.Items.ContainsKey(rune) || k.State.Ground.ContainsKey(rune);
            if (!seen) return "the rune " + rune.ToString("X") + " is not in sight";
            // The pack's bags read first: after a login a bag's reagents are unknown until it is opened.
            OpenPackBags(k);
            foreach (int r in RecallReagents) if (PackCount(k, g => g == r) == 0) return "no " + UoTiles.Name(r) + " in the pack";
            var manaBy = System.DateTime.UtcNow.AddMilliseconds(ManaWaitMs);
            while (true) { int mana; lock (k.State.Gate) mana = k.State.Mana; if (mana >= RecallMana) break; if (System.DateTime.UtcNow > manaBy || k.Closed) return "mana stayed under " + RecallMana; System.Threading.Thread.Sleep(500); }
            int x0, y0; lock (k.State.Gate) { x0 = k.State.X; y0 = k.State.Y; }
            long mark;
            lock (actionGate) {
                Answered(k, AnswerMs); mark = k.State.JournalSeq;
                string why = k.Cast(RecallSpell, true); if (why != null) return why;
                // The player's own spell words ("Kal Ort Por") come before the cursor; only a line that ends the cast ends
                // the wait. Anyone else's speech does not: Jannelle's "Excuse me, but what art thou saying?" came 121 ms
                // before the cursor (2026-10-17 08:54:25), the cursor went unanswered and every retry was "You are
                // already casting a spell."
                var until = System.DateTime.UtcNow.AddMilliseconds(CastCursorMs);
                while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed && !k.State.JournalSince(mark).Exists(j => System.Array.Exists(CastEnds, e => j.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0))) System.Threading.Thread.Sleep(20);
                if (k.MacroCursor) {
                    AssistItem it; lock (k.State.Gate) { if (!k.State.Items.TryGetValue(rune, out it)) k.State.Ground.TryGetValue(rune, out it); }
                    if (it == null) { k.Settle(); return "the rune " + rune.ToString("X") + " is not in sight"; }
                    why = k.Target(false, rune, it.X, it.Y, it.Z, it.Graphic); if (why != null) return "aiming at the rune: " + why;
                }
            }
            var moveBy = System.DateTime.UtcNow.AddMilliseconds(RecallMoveMs);
            while (System.DateTime.UtcNow < moveBy && !k.Closed) {
                int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
                // A recall moves the player far; a step of a tile is not one ("the rune for Jhelom landed in Britain" at
                // 1414,1699 from 1414,1700).
                if (Tiles(x, y, x0, y0) > RecallMoved) { Note("Circuit: recalled to " + x + "," + y + (attempt > 1 ? " (try " + attempt + ")" : "")); return null; }
                if (k.State.JournalSince(mark).Exists(j => j.IndexOf("fizzle", System.StringComparison.OrdinalIgnoreCase) >= 0)) break;
                System.Threading.Thread.Sleep(100);
            }
            string said = null; foreach (var j in k.State.JournalSince(mark)) said = j;
            Note("Circuit: recall try " + attempt + " did not move you" + (said != null ? " (\"" + said + "\")" : ""));
            var heard = k.State.JournalSince(mark); System.Func<string, bool> has = s => heard.Exists(j => j.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0);
            // A blocked landing is the server's answer, not bad luck: tried once (a recall to Skara Brae from its own
            // landing tile answered this twice, 2026-10-17).
            if (has("blocking the location")) return "the landing is blocked";
            if (has("reagents") || has("insufficient")) return "the server says the cast is short: \"" + heard.Find(j => j.IndexOf("reagents", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("insufficient", System.StringComparison.OrdinalIgnoreCase) >= 0) + "\"";
            // L-FUTILITY: cast again only when the server's answer asks for it, a fizzle or "You must wait"; any other
            // answer, or none, ends it.
            if (!has("fizzle") && !has("You must wait")) return "the recall did not move you" + (said != null ? " (\"" + said + "\")" : "");
            System.Threading.Thread.Sleep(BusyMs);
        }
        return "the recall failed " + RecallTries + " times";
    }

    // ---------- sounds ----------
    // The sound filter (Damian, 2026-10-16: "add a tab for supressing sounds like uoassist, include pack sounds"): a server
    // sound (0x54: flags, sound id, volume, x, y, z) in a checked group is kept from the client. Ids are the 1.25 SOUNDIDX
    // entries, named by the file names SOUND.MUL holds; 0x48 leather1 and 0x57 usecloth, the pack's, were the two sounds
    // uo1998 sent most (relay logs 2026-10-16, about 480 each).
    public sealed class SoundGroup { public string Name; public int[] Ids; }
    static int[] Ids(params int[] pairs) {
        var l = new System.Collections.Generic.List<int>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) for (int id = pairs[i]; id <= pairs[i + 1]; id++) l.Add(id);
        return l.ToArray();
    }
    static SoundGroup S(string name, params int[] pairs) { return new SoundGroup { Name = name, Ids = Ids(pairs) }; }
    public static readonly SoundGroup[] SoundGroups = {
        S("Pack and bags", 0x48, 0x48, 0x57, 0x59),
        S("Coins and gems", 0x32, 0x37),
        S("Chests and drawers", 0x2C, 0x2F),
        S("Doors and gates", 0xEA, 0xF4),
        S("Bard music", 0x38, 0x39, 0x3D, 0x3D, 0x43, 0x46, 0x4C, 0x4D, 0x52, 0x53),
        S("Anvil and hammer", 0x2A, 0x2B, 0x42, 0x42),
        S("Crafting tools", 0x23D, 0x23E, 0x241, 0x242, 0x248, 0x249),
        S("Spirit Speak", 0x24A, 0x24A),
        S("Spells", 0x1DF, 0x217),
        S("Footsteps", 0x11F, 0x134, 0x24B, 0x24C),
        S("Swings and hits", 0x135, 0x14A, 0x232, 0x23C),
        S("Grunts and death cries", 0x14B, 0x15D),
        S("Dogs and wolves", 0x85, 0x89, 0xE5, 0xE9),
        S("Cats", 0x69, 0x6D),
        S("Horses and llamas", 0xA8, 0xAC, 0xB7, 0xB9),
        S("Sheep, goats and pigs", 0xD6, 0xDA, 0x99, 0x9D, 0xC4, 0xC8),
        S("Cows and bulls", 0x64, 0x68, 0x78, 0x7C),
        S("Chickens and birds", 0x6E, 0x72, 0x17, 0x1F, 0x7D, 0x81, 0x8F, 0x98, 0xAD, 0xB1, 0xBF, 0xC3, 0xD1, 0xD5, 0x270, 0x279),
        S("Dragons", 0x16A, 0x16E),
        S("Weather, water and wind", 0x00, 0x16, 0x20, 0x29),
    };
    public bool SoundOff(string group) { return Cfg.Get("sound.off." + group, "0") == "1"; }
    public void SetSoundOff(string group, bool off) { Cfg.Set("sound.off." + group, off ? "1" : "0"); muted = null; }
    volatile System.Collections.Generic.HashSet<int> muted;
    readonly System.Collections.Generic.Dictionary<int, int> heard = new System.Collections.Generic.Dictionary<int, int>();
    bool HoldSound(byte[] p) {
        if (p[0] != 0x54 || p.Length < 4) return false;
        int id = (p[2] << 8) | p[3];
        lock (heard) { int n; heard.TryGetValue(id, out n); heard[id] = n + 1; }
        var m = muted;
        if (m == null) { m = new System.Collections.Generic.HashSet<int>(); foreach (var g in SoundGroups) if (SoundOff(g.Name)) foreach (var s in g.Ids) m.Add(s); muted = m; }
        return m.Contains(id);
    }
    // For the Sounds tab: how often this session the server played a group's sounds.
    public int Heard(SoundGroup g) { int n = 0; lock (heard) foreach (var id in g.Ids) { int c; if (heard.TryGetValue(id, out c)) n += c; } return n; }

    // ---------- waiting, pacing ----------
    bool Answered(GameLink k, int ms) {
        var until = System.DateTime.UtcNow.AddMilliseconds(ms); bool waited = k.InFlight != null; var from = k.InFlightAt;
        while (k.InFlight != null && System.DateTime.UtcNow < until) { if (k.Closed) return false; System.Threading.Thread.Sleep(20); }
        if (k.InFlight != null) { k.Settle(); return false; }
        if (waited) Seen("answer", (int)(System.DateTime.UtcNow - from).TotalMilliseconds);
        return true;
    }
    // A lift: refused when a 0x27 comes back, accepted when none has in LiftSettleMs, or in settleMs from a caller that
    // tunes its own settle (PickupLoop), whose late refusals are its own and do not raise lift-settle.
    string Lifted(GameLink k, uint serial, int amount, int settleMs = -1) {
        long before = System.Threading.Interlocked.Read(ref refusals);
        string why = k.Lift(serial, amount); if (why != null) return why;
        var at = System.DateTime.UtcNow; liftJudgedAt = System.DateTime.MinValue; liftSentAt = at;
        System.Threading.Thread.Sleep(settleMs < 0 ? LiftSettleMs : settleMs);
        k.Settle();
        if (System.Threading.Interlocked.Read(ref refusals) != before) { Seen("lift-settle", (int)(lastRefusalAt - at).TotalMilliseconds); return "the server refused the lift (reason " + lastRefusal[1] + ")"; }
        if (settleMs < 0) { liftAt = at; liftJudgedAt = System.DateTime.UtcNow; }
        return null;
    }
    // A refusal that comes after the lift was judged accepted, within LateRefusalMs: lift-settle was too short.
    public const int LateRefusalMs = 5000;
    // liftSentAt: the last lift sent, answered or refused; the server's busy time runs from it (a lift refused out of
    // range at 06:37:26.357 and the next lift 624 ms later refused busy).
    System.DateTime liftAt, liftJudgedAt = System.DateTime.MinValue, lastRefusalAt, liftSentAt = System.DateTime.MinValue;
    void RefusalSeen() {
        lastRefusalAt = System.DateTime.UtcNow;
        if (liftJudgedAt != System.DateTime.MinValue && (lastRefusalAt - liftJudgedAt).TotalMilliseconds < LateRefusalMs) { liftJudgedAt = System.DateTime.MinValue; Seen("lift-settle", (int)(lastRefusalAt - liftAt).TotalMilliseconds); }
    }
    static AssistItem Item(GameLink k, uint s) { lock (k.State.Gate) { AssistItem it; return k.State.Items.TryGetValue(s, out it) ? it : null; } }
    static AssistItem Hand(GameLink k, int layer) { lock (k.State.Gate) { foreach (var kv in k.State.Items) if (kv.Value.Layer == layer && kv.Value.Mobile == k.State.Player) return kv.Value; } return null; }

    // ---------- canned loops ----------
    public string Menu() {
        var sb = new System.Text.StringBuilder();
        foreach (var l in Loops) sb.Append("Loop " + l.Name + " (" + Delay(l) + " ms; " + (l.Tailor > 0 ? "round zero: double-click the sewing kit, target the bolts or the cloth and pick from the menus" : l.Smelt > 0 ? "round zero: lift one ore from a pile on the ground, drop it in a bag in your pack, double-click it and pick the forge" : l.Pairs > 0 ? "round zero: double-click each tool and pick its target" : l.Target == 1 ? "round zero: pick the target" : "nothing to pick") + ") | ");
        return sb.ToString().TrimEnd(' ', '|');
    }
    // Per login server: pace.<loop> the delay in use, pace.lo.<loop> the longest the server refused, pace.hi.<loop> the
    // shortest that ran ProbeAfterRounds rounds clean (0: none yet).
    public int Delay(LoopKind l) { return Cfg.GetInt(Server + "pace." + l.Name, l.DelayMs); }
    int Bound(string which, LoopKind l) { return Cfg.GetInt(Server + "pace." + which + "." + l.Name, 0); }
    void SetPace(LoopKind l, int delay, int lo, int hi) {
        Cfg.Set(Server + "pace.lo." + l.Name, lo.ToString()); Cfg.Set(Server + "pace.hi." + l.Name, hi.ToString());
        Cfg.Set(Server + "pace." + l.Name, delay.ToString());
    }
    static int Mid(int lo, int hi) { return ((lo + hi) / 2 + 49) / 50 * 50; }
    // A wait line at the delay in use: that delay is refused; the next is halfway to the shortest that worked, or double.
    // Null to repeat the step at the new delay, or why the loop stops.
    // Auto-detect (Damian, 2026-10-15: "put the actual delays in there that can be shown and overridden, with a button to
    // turn on or off auto-detect by bisect feature"): off, a wait line repeats the step at the same delay, and
    // MaxRefusedInARow refusals in a row stop the loop.
    public bool AutoPace { get { return Cfg.Get(Server + "pace.auto", "on") == "on"; } set { Cfg.Set(Server + "pace.auto", value ? "on" : "off"); } }
    public const int MaxRefusedInARow = 3;
    int refusedInARow;
    // A delay the player set: kept for this server, and the bisect's bounds are forgotten.
    public void SetDelay(LoopKind l, int ms) { SetPace(l, System.Math.Max(MinDelayMs / 2, System.Math.Min(MaxDelayMs, ms)), 0, 0); }
    string Refused(LoopKind l) {
        if (!AutoPace) {
            if (++refusedInARow >= MaxRefusedInARow) return "the server refused the loop " + refusedInARow + " times in a row at " + Delay(l) + " ms (auto-detect is off)";
            Note("Loop " + l.Name + ": refused at " + Delay(l) + " ms; auto-detect is off, repeating at the same delay");
            return null;
        }
        int d = Delay(l), lo = System.Math.Max(Bound("lo", l), d), hi = Bound("hi", l);
        if (hi <= lo) hi = 0;
        int next = hi > 0 ? Mid(lo, hi) : (d * 2 + 49) / 50 * 50;
        if (next > MaxDelayMs) return "the server refused the loop at " + d + " ms; " + MaxDelayMs + " ms is the most the loop waits";
        SetPace(l, next, lo, hi);
        Note("Loop " + l.Name + ": refused at " + d + " ms; trying " + next + " ms (refused " + lo + ", worked " + (hi > 0 ? hi + " ms" : "none yet") + ") on " + Server.TrimEnd('.'));
        return null;
    }
    // ProbeAfterRounds clean rounds at the delay in use: it works; try halfway to the longest refused, when one is known
    // and the two are more than SettleMs apart. With none refused yet there is one point, and no halfway (Damian,
    // 2026-10-16: "bisect makes sense only when you have 2 points ... if the start is 5seconds, maybe try 4 and if that
    // works 3, if that fails 3.5"): the next try is a fifth shorter, in GuessStepMs steps, never under MinDelayMs.
    public const int GuessStepMs = 500;
    static int Guess(int d) { int step = System.Math.Max(GuessStepMs, (d / 5 + GuessStepMs / 2) / GuessStepMs * GuessStepMs); return System.Math.Max(MinDelayMs, d - step); }
    void Worked(LoopKind l) {
        if (!AutoPace) return;
        int d = Delay(l), lo = Bound("lo", l), hi = Bound("hi", l);
        if (hi == 0 || d < hi) hi = d;
        int next = d;
        if (lo > 0 && hi - lo > SettleMs) next = Mid(lo, hi);
        else if (lo == 0 && Guess(hi) < hi) next = Guess(hi);
        SetPace(l, next, lo, hi);
        if (next != d) Note("Loop " + l.Name + ": " + d + " ms worked; trying " + next + " ms on " + Server.TrimEnd('.'));
    }
    public static LoopKind Find(string name) { foreach (var l in Loops) if (string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase) || string.Equals("Loop " + l.Name, name, System.StringComparison.OrdinalIgnoreCase)) return l; return null; }

    public string Start(LoopKind l, int rounds, int untilSkill, string untilLine) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        if (loopThread != null && loopThread.IsAlive) return "a loop is running: " + Status;
        if (rounds <= 0) rounds = Cfg.GetInt("rounds." + l.Name, 10);
        Cfg.Set("rounds." + l.Name, rounds.ToString());
        stop = false;
        loopThread = new System.Threading.Thread(() => RunLoop(k, l, rounds, untilSkill, untilLine)); loopThread.IsBackground = true; loopThread.Start();
        return null;
    }
    public void Stop(string why) { circuitStop = true; if (loopThread != null && loopThread.IsAlive) { stop = true; Note("Loop stopping: " + why); } }
    // The circuit's own stop: the loop key and "stop" set it; each circuit command clears it as it starts (stop stays set
    // while no loop runs, so the circuit cannot use it).
    public volatile bool circuitStop;

    // Home: the bag a tool step's tool was in at round zero, where a replacement is looked for.
    sealed class Step { public bool Use, Skill, Target, Ground; public uint Serial, Home; public int X, Y, Z, Graphic, Id; public string Words; }
    // A broken tool (Damian, 2026-10-16: "should detect when you break the shovel or pickaxe, and then find one in the same
    // bag that isn't the other one and use it"): a tool step whose tool is gone takes another digging tool from its home
    // bag, never one another step uses. Digging tools by their 1.25 TILEDATA name.
    static bool DigTool(int graphic) { string n = UoTiles.Name(graphic).ToLowerInvariant(); return n == "shovel" || n == "pickaxe"; }
    // A tool that opened no cursor while still in its bag is defective (Damian, 2026-10-16: "does it still exist in the
    // bag, if so, note it as defective and not use it and pick another and try again. only after that should we stop the
    // loop"); it is never picked again this session.
    readonly System.Collections.Generic.HashSet<uint> defectiveTools = new System.Collections.Generic.HashSet<uint>();
    string ReplaceTool(GameLink k, System.Collections.Generic.List<Step> steps, Step s, bool defective = false) {
        if (s.Home == 0) return null;
        bool gone = Item(k, s.Serial) == null;
        if (!gone && !defective) return null;
        if (defective && !gone) defectiveTools.Add(s.Serial);
        var taken = new System.Collections.Generic.HashSet<uint>(); foreach (var o in steps) if (o.Use) taken.Add(o.Serial);
        AssistItem pick = null;
        lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == s.Home && !taken.Contains(it.Serial) && !defectiveTools.Contains(it.Serial) && DigTool(it.Graphic) && (pick == null || it.Serial < pick.Serial)) pick = it;
        string what = gone ? "broke" : "opened no target cursor and is set aside as defective";
        if (pick == null) return "the tool " + s.Serial.ToString("X") + " " + what + ", and its bag " + s.Home.ToString("X") + " holds no other usable shovel or pickaxe";
        Note("Loop: the tool " + s.Serial.ToString("X") + " " + what + "; using the " + UoTiles.Name(pick.Graphic) + " " + pick.Serial.ToString("X") + " from the same bag");
        s.Serial = pick.Serial; s.Words = "use item " + pick.Graphic.ToString("X4") + " " + pick.Serial.ToString("X");
        return null;
    }

    void RunLoop(GameLink k, LoopKind l, int rounds, int untilSkill, string untilLine) {
        if (l.Smith > 0) { RunSmith(k, l, rounds, untilSkill); return; }
        if (l.Magery > 0) { RunMagery(k, l, rounds, untilSkill); return; }
        if (l.Smelt > 0) { RunSmelt(k, l, rounds, untilSkill); return; }
        if (l.Tailor > 0) { RunTailor(k, l, rounds, untilSkill); return; }
        string why = "done: " + rounds + " rounds";
        try {
            var steps = new System.Collections.Generic.List<Step>();
            // A skill loop uses the skill at once (Damian, 2026-10-15: "for skill loops, go ahead and invoke the skill. only
            // need to gather user input when there is a variable"), unless the player's own last skill use is within the
            // delay ("be sure to wait the skill-appropriate default timeout before starting"): that use is the last use.
            lastUse = lastOk = l.Pairs > 0 ? System.DateTime.UtcNow : (ownSkillAt > lastUse ? ownSkillAt : lastUse);
            Status = "Loop " + l.Name + ": round zero";
            if (l.Pairs > 0) {
                for (int i = 1; i <= l.Pairs; i++) {
                    Status = "Loop " + l.Name + ": round zero: double-click tool " + i;
                    var use = Watch(k, 0x06, 120000); if (use == null) { why = "stopped: no tool was double-clicked"; return; }
                    uint s = U32(use, 1); var it = Item(k, s); lastUse = lastOk = System.DateTime.UtcNow;
                    steps.Add(new Step { Use = true, Serial = s, Home = it != null && DigTool(it.Graphic) ? it.Container : 0, Words = "use " + (it != null ? "item " + it.Graphic.ToString("X4") : "") + " " + s.ToString("X") });
                    Status = "Loop " + l.Name + ": round zero: pick the target for tool " + i;
                    var t = Watch(k, 0x6C, 120000); if (t == null) { why = "stopped: no target was picked"; return; }
                    steps.Add(TargetStep(t)); lastUse = lastOk = System.DateTime.UtcNow;
                    if (i == 1) lock (pendingDigs) pendingDigs.Clear();
                    if (l.Name.StartsWith("Mining")) lock (pendingDigs) pendingDigs.Enqueue(System.DateTime.UtcNow);
                }
            } else {
                var first = new Step { Skill = true, Id = l.Skill, Words = "use " + l.Name };
                steps.Add(first);
                if (l.Target == 1) {
                    Status = "Loop " + l.Name + ": round zero: waiting " + Delay(l) + " ms before the first use";
                    var due = lastUse.AddMilliseconds(Delay(l));
                    while (System.DateTime.UtcNow < due) { if (stop || k.Closed) { why = stop ? "stopped" : "stopped: the connection closed"; return; } System.Threading.Thread.Sleep(50); }
                    Status = "Loop " + l.Name + ": round zero: pick the target";
                    string w; lock (actionGate) { w = k.Skill(l.Skill, false); }
                    if (w != null) { why = "stopped: " + w; return; }
                    var t = Watch(k, 0x6C, 120000); if (t == null) { why = "stopped: no target was picked"; return; }
                    steps.Add(TargetStep(t));
                    lastUse = System.DateTime.UtcNow;
                }
            }
            var words = new System.Text.StringBuilder();
            foreach (var s in steps) words.Append(s.Words + (s.Use || s.Skill ? ", wait for the server's answer and " + Delay(l) + " ms from the last use" : ", wait for the server's answer") + "; ");
            LoopWords = "Loop " + l.Name + ": " + words.ToString().TrimEnd(' ', ';') + "; " + rounds + " times" + (untilSkill > 0 ? ", or until the skill reaches " + (untilSkill / 10.0).ToString("0.0") : "") + (untilLine != null ? ", or until \"" + untilLine + "\"" : "") + ", or F2";
            Note(LoopWords);
            Pacing = true; int clean = 0; refusedInARow = 0;
            int gainSkill = l.Skill >= 0 ? l.Skill : l.Pairs > 0 ? MiningSkill : -1;
            currentRun = new LoopRun { Name = l.Name, Skill = gainSkill, Start = System.DateTime.UtcNow, From = SkillOf(k, gainSkill) };
            for (int round = 1; round <= rounds; round++) {
                refusedThisRound = false;
                for (int i = 0; i < steps.Count; i++) {
                    if (stop) { why = "stopped"; return; }
                    if (k.Closed) { why = "stopped: the connection closed"; return; }
                    if (k.Halted != null) { why = "stopped: " + k.Halted; return; }
                    Status = "Loop " + l.Name + ": round " + round + " of " + rounds + ": " + steps[i].Words;
                    string stopped = Do(k, l, steps, i);
                    if (stopped == "again") { i--; continue; }
                    if (stopped != null) { why = "stopped: " + stopped; return; }
                    if (untilLine != null) foreach (var j in k.State.JournalSince(markJournal)) if (j.IndexOf(untilLine, System.StringComparison.OrdinalIgnoreCase) >= 0) { why = "done: \"" + j + "\""; return; }
                    if (untilSkill > 0 && l.Skill >= 0) { int v; lock (k.State.Gate) k.State.Skills.TryGetValue(l.Skill + 1, out v); if (v >= untilSkill) { why = "done: the skill reached " + (v / 10.0).ToString("0.0"); return; } }
                }
                if (currentRun != null) currentRun.Rounds = round;
                if (!refusedThisRound) refusedInARow = 0;
                clean = refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds) { Worked(l); clean = 0; }
            }
        } catch (System.Exception e) { why = "stopped: " + e.Message; }
        finally {
            Pacing = false; Status = "Loop " + l.Name + " " + why; Note(Status);
            var run = currentRun; currentRun = null;
            if (run != null) { run.End = System.DateTime.UtcNow; run.To = SkillOf(k, run.Skill); lock (runs) runs.Add(run); }
        }
    }

    System.DateTime lastUse = System.DateTime.MinValue, ownSkillAt = System.DateTime.MinValue; long markJournal;

    // One step. Null when it went as it should, "again" to repeat it after a corrected delay, or why the loop stops.
    string Do(GameLink k, LoopKind l, System.Collections.Generic.List<Step> steps, int i) {
        var s = steps[i]; bool targetNext = i + 1 < steps.Count && steps[i + 1].Target;
        if (s.Use) { string broke = ReplaceTool(k, steps, s); if (broke != null) return broke; }
        if (s.Use && l.Name.StartsWith("Mining") && PackNearFull(k)) {
            // A dig still out lands its ore in the pack first: nothing is sent until every pending dig has answered, then
            // the whole pile (that dig's ore with it) goes down, and the next use follows on the loop's delay.
            while (PendingDigs() > 0) { if (stop || k.Closed) return stop ? "stopped" : "the connection closed"; System.Threading.Thread.Sleep(50); }
            if (PackNearFull(k)) lock (actionGate) { string full = DumpOre(k); if (full != null && full != NoOre) return full; }
        }
        if (s.Use || s.Skill) {
            // Rule 3: the later of the server's answer to the last action and the step's delay after the last use. The
            // wait holds no lock, so drinking, arming and pickup act in it.
            var due = lastUse.AddMilliseconds(Delay(l));
            while (System.DateTime.UtcNow < due) { if (stop || k.Closed) return stop ? "stopped" : "the connection closed"; System.Threading.Thread.Sleep(50); }
        }
        lock (actionGate) {
            if (s.Use || s.Skill) {
                Answered(k, AnswerMs);
                // Checked again right before the use: the tool can break during the wait (relay log 2026-10-16
                // 20:21:21.117 "You destroyed the shovel." and its 0x1D, then a use of it 67 ms later).
                if (s.Use) { string broke = ReplaceTool(k, steps, s); if (broke != null) return broke; }
                markJournal = k.State.JournalSeq;
                string why = s.Use ? k.Use(s.Serial, targetNext) : k.Skill(s.Id, targetNext);
                if (why != null) return why;
                lastUse = System.DateTime.UtcNow;
                if (targetNext) {
                    var until = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
                    while (!k.MacroCursor && System.DateTime.UtcNow < until) {
                        if (k.Closed) return "the connection closed";
                        string said = Journal(k, l); if (said != null) return said;
                        System.Threading.Thread.Sleep(20);
                    }
                    if (k.MacroCursor) Seen("cursor", (int)(System.DateTime.UtcNow - lastUse).TotalMilliseconds);
                    if (!k.MacroCursor) {
                        k.Settle();
                        string said = Journal(k, l); if (said != null) return said;
                        // A digging tool that opened no cursor: replaced (gone) or set aside (still in the bag), then the
                        // step is tried again; the loop stops only when no other tool is left.
                        if (s.Use && s.Home != 0) { string none = ReplaceTool(k, steps, s, true); if (none != null) return none; return "again"; }
                        return "the server opened no target cursor";
                    }
                } else {
                    Answered(k, AnswerMs);
                    string said = Journal(k, l); if (said == null) lastOk = lastUse;
                    return said;
                }
                lastOk = lastUse;
                return null;
            }
            string t = k.Target(s.Ground, s.Serial, s.X, s.Y, s.Z, s.Graphic);
            if (t != null) return t;
            if (l.Name.StartsWith("Mining")) lock (pendingDigs) pendingDigs.Enqueue(System.DateTime.UtcNow);
            // The server's action timer runs from the target (Damian, 2026-10-15: "1250 per tool use (after first and
            // second target)"), so the next use's delay counts from here.
            lastUse = System.DateTime.UtcNow;
            // With two or more tools the digs overlap: a target's answer is its own dig's result, about 2.6 s later on
            // uo1998, so waiting for it serializes the tools. Round zero starts both digs and each wait was answered by the
            // other tool's result (1.5 s a use); after a pause (the ore pile put down) the loop ran one dig at a time, at
            // half speed, for the rest of the run (relay log 2026-10-16 20:14:30, Damian: "it restarts at half speed or
            // only 1 of the two tools"). Such a loop paces on its delay alone.
            if (l.Pairs > 1) k.Settle(); else Answered(k, AnswerMs);
            System.Threading.Thread.Sleep(100);
            string heard = Journal(k, l); if (heard == null) lastOk = lastUse;
            return heard;
        }
    }

    // The journal since the step: a stop line ends the loop; a wait line moves this loop's delay for this server by the
    // bisect (Refused), keeps it, and repeats the step that long after the last use the server accepted.
    string Journal(GameLink k, LoopKind l) {
        foreach (var j in k.State.JournalSince(markJournal)) {
            foreach (var stopLine in StopLines) if (j.IndexOf(stopLine, System.StringComparison.OrdinalIgnoreCase) >= 0) {
                // A worn-out tool in a tool loop is replaced before its next use (ReplaceTool), not a stop.
                if (l.Pairs > 0 && stopLine == WornOut) continue;
                return "\"" + j + "\"";
            }
            foreach (var full in FullPackLines) if (j.IndexOf(full, System.StringComparison.OrdinalIgnoreCase) >= 0) {
                string why = DumpOre(k); if (why != null) return why;
                markJournal = k.State.JournalSeq; return null;
            }
            foreach (var waitLine in WaitLines) if (j.StartsWith(waitLine, System.StringComparison.OrdinalIgnoreCase)) {
                string why = Refused(l); if (why != null) return why;
                // The retry waits the new delay from the refused attempt: the server's timer restarted no earlier than it.
                markJournal = k.State.JournalSeq; lastUse = System.DateTime.UtcNow; refusedThisRound = true;
                return "again";
            }
        }
        return null;
    }
    volatile bool refusedThisRound;
    System.DateTime lastOk = System.DateTime.MinValue;
    // Settings the server corrects are kept per login server.
    public string Server = "";

    static uint U32(byte[] p, int at) { return ((uint)p[at] << 24) | ((uint)p[at + 1] << 16) | ((uint)p[at + 2] << 8) | p[at + 3]; }
    static Step TargetStep(byte[] t) {
        var s = new Step { Target = true, Ground = t[1] == 1, Serial = U32(t, 7), X = (t[11] << 8) | t[12], Y = (t[13] << 8) | t[14], Z = (sbyte)t[16], Graphic = (t[17] << 8) | t[18] };
        s.Words = s.Ground ? "target the tile " + s.X + "," + s.Y + "," + s.Z : "target " + s.Serial.ToString("X");
        return s;
    }
    // The next client packet with this opcode, or null after ms.
    byte[] Watch(GameLink k, int opcode, int ms) {
        byte[] got = null; var done = new System.Threading.ManualResetEventSlim(false);
        System.Action<byte[]> h = p => { if (p[0] == opcode && got == null) { got = p; done.Set(); } };
        k.ClientPacket += h;
        try {
            var until = System.DateTime.UtcNow.AddMilliseconds(ms);
            while (got == null && !stop && !k.Closed && System.DateTime.UtcNow < until) done.Wait(100);
        } finally { k.ClientPacket -= h; }
        return got;
    }

    // ---------- smithing ----------
    // uo1998 22:21:36 to 22:23:37: a double-click on the smith's hammer opens an item menu (0x7C: dialog serial, menu id,
    // title, then per item a model, a hue and a text); each pick is 0x7D (dialog serial, menu id, 1-based index, model,
    // hue), three deep for platemail legs ("Build Armor", "Build Plate Armor", "Build Platemail legs, 20 metal"). The
    // item comes 3 to 10 s after the last pick (Damian: "there is a variable return length of time"), with "You create
    // the item ..." and a quality line ("Due to your exceptional skill ..." for exceptional). Round zero is the player's
    // own build; every later one replays its picks into menus kept off the client's screen.
    // A build has no fixed length (Damian, 2026-10-15: "sometimes the response is 20 seconds of hammer banging then just a
    // message that says you lost some metal. sometimes it responds immediatly"): the server plays the hammer (sound 0x2A,
    // uo1998 23:06:17 to 23:06:41, about one a second) until the result line, so the wait lasts while it bangs.
    public const int HammerSound = 0x2A, IngotArt = 0x1BF2, SmithPicksMax = 6;
    int CraftMs { get { return Ms("craft"); } }
    int CraftQuietMs { get { return Ms("craft-quiet"); } }
    static readonly string[] CraftFailLines = { "You failed", "you fail", "destroyed", "lost some" };
    System.DateTime lastBang = System.DateTime.MinValue;
    static readonly string[] CraftStopLines = { "enough metal", "enough ingot", "don't have enough", "do not have enough", "worn out", "broke", "must be near", "too far", "anvil", "forge" };
    // The text of item index (1-based) in an item menu, or null.
    static string MenuItem(byte[] m, int index) {
        if (m == null || m.Length < 11) return null;
        int at = 10 + m[9]; if (at >= m.Length) return null; int n = m[at]; at++;
        for (int i = 1; i <= n && at + 5 <= m.Length; i++) { int len = m[at + 4]; if (at + 5 + len > m.Length) return null; if (i == index) return System.Text.Encoding.ASCII.GetString(m, at + 5, len); at += 5 + len; }
        return null;
    }
    static int MetalCost(string text) {
        if (text == null) return 0; var parts = text.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i++) { int v; if (int.TryParse(parts[i], out v) && (parts[i + 1].StartsWith("metal") || parts[i + 1].StartsWith("ingot"))) return v; }
        return 0;
    }
    // The next server packet with this opcode, or null after ms.
    byte[] WatchServer(GameLink k, int opcode, int ms) {
        byte[] got = null; var done = new System.Threading.ManualResetEventSlim(false);
        System.Action<byte[]> h = p => { if (p[0] == opcode && got == null) { got = p; done.Set(); } };
        k.ServerPacket += h;
        try { var until = System.DateTime.UtcNow.AddMilliseconds(ms); while (got == null && !stop && !k.Closed && System.DateTime.UtcNow < until) done.Wait(100); }
        finally { k.ServerPacket -= h; }
        return got;
    }
    int PackIngots(GameLink k) {
        int n = 0;
        lock (k.State.Gate) {
            var inside = new System.Collections.Generic.HashSet<uint> { k.State.Backpack };
            for (int pass = 0; pass < 8; pass++) foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container)) inside.Add(kv.Key);
            foreach (var kv in k.State.Items) if (kv.Value.Graphic == IngotArt && inside.Contains(kv.Value.Container)) n += kv.Value.Amount;
        }
        return n;
    }
    // Waits for the build's result: "exceptional", "made", "failed", or the line that stops the loop (quoted).
    // circuit: a circuit's build, ended by the circuit's stop, not the loops' (a login sets the loops' stop, and the
    // smithing circuit's first build read "stopped" at once, 2026-10-18 17:36:35).
    string CraftResult(GameLink k, long mark, bool circuit = false) {
        var start = System.DateTime.UtcNow; var cap = start.AddMilliseconds(CraftMs);
        System.Func<bool> halted = () => circuit ? circuitStop : stop;
        while (!halted() && !k.Closed) {
            var now = System.DateTime.UtcNow; var heard = lastBang > start ? lastBang : start;
            if (now > cap || now > heard.AddMilliseconds(CraftQuietMs)) break;
            var lines = k.State.JournalSince(mark);
            foreach (var j in lines) {
                foreach (var s in CraftStopLines) if (j.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return "\"" + j + "\"";
                foreach (var s in CraftFailLines) if (j.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return "failed";
                if (j.StartsWith("You create", System.StringComparison.OrdinalIgnoreCase)) {
                    System.Threading.Thread.Sleep(400);
                    foreach (var q in k.State.JournalSince(mark)) if (q.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0) return "exceptional";
                    return "made";
                }
            }
            System.Threading.Thread.Sleep(100);
        }
        return halted() ? "stopped" : k.Closed ? "the connection closed" : "no result: the hammer went quiet " + CraftQuietMs / 1000 + " s with no line";
    }
    // ---------- magery training ----------
    // Each cast is at the circle the skill gains on (pre-AOS RunUO MagerySpell.GetCastSkills: circle n gains between
    // 14.29 (n - 1) - 20 and 14.29 (n - 1) + 20 skill, the chance rising across it): the highest circle the skill has passed
    // the 40% point of, 14.29 (n - 1) - 4: circle 2 from 10.3, 3 from 24.6, 4 from 38.9, 5 from 53.2, 6 from 67.5, 7 from
    // 81.8 and 8 from 96.0 ((tenths + 40) * 7 / 1000 + 1; codex_run fact 951040c8). Each circle
    // casts a spell safe on oneself, and a target cursor it opens is aimed at the player. Mana is pre-AOS's per circle.
    // Each circle keeps its own delay, found by the loops' bisect on the server's "not yet recovered".
    sealed class TrainSpell { public int Id; public string Name; public bool Area; }
    static readonly TrainSpell[] TrainSpells = {
        new TrainSpell { Id = 6, Name = "Night Sight" }, new TrainSpell { Id = 9, Name = "Agility" }, new TrainSpell { Id = 17, Name = "Bless" },
        new TrainSpell { Id = 29, Name = "Greater Heal" }, new TrainSpell { Id = 36, Name = "Magic Reflection" }, new TrainSpell { Id = 44, Name = "Invisibility" },
        new TrainSpell { Id = 53, Name = "Mana Vampire" }, new TrainSpell { Id = 59, Name = "Earthquake", Area = true },
    };
    static readonly int[] CircleMana = { 4, 6, 9, 11, 14, 20, 40, 50 };
    public static readonly LoopKind[] CirclePace = {
        new LoopKind { Name = "Magery circle 1", DelayMs = 1500, Probe = true }, new LoopKind { Name = "Magery circle 2", DelayMs = 2000, Probe = true },
        new LoopKind { Name = "Magery circle 3", DelayMs = 2500, Probe = true }, new LoopKind { Name = "Magery circle 4", DelayMs = 3000, Probe = true },
        new LoopKind { Name = "Magery circle 5", DelayMs = 3500, Probe = true }, new LoopKind { Name = "Magery circle 6", DelayMs = 4000, Probe = true },
        new LoopKind { Name = "Magery circle 7", DelayMs = 4500, Probe = true }, new LoopKind { Name = "Magery circle 8", DelayMs = 5000, Probe = true },
    };
    public const int MagerySkill = 25;
    int ManaWaitMs { get { return Ms("mana-wait"); } }
    public static int CircleFor(int tenths) { return System.Math.Max(1, System.Math.Min(8, (System.Math.Max(0, tenths) + 40) * 7 / 1000 + 1)); }
    void RunMagery(GameLink k, LoopKind l, int rounds, int untilSkill) {
        string why = "done: " + rounds + " casts"; int clean = 0; refusedInARow = 0;
        Pacing = true; currentRun = new LoopRun { Name = l.Name, Skill = MagerySkill, Start = System.DateTime.UtcNow, From = SkillOf(k, MagerySkill) };
        LoopWords = "Loop " + l.Name + ": each cast at the circle Magery gains on, aimed at you; waits for mana; " + rounds + " casts" + (untilSkill > 0 ? ", or until Magery reaches " + (untilSkill / 10.0).ToString("0.0") : "") + ", or F2";
        Note(LoopWords);
        try {
            for (int round = 1; round <= rounds; round++) {
                if (stop) { why = "stopped"; return; }
                if (k.Closed) { why = "stopped: the connection closed"; return; }
                if (k.Halted != null) { why = "stopped: " + k.Halted; return; }
                int skill = SkillOf(k, MagerySkill), c = CircleFor(skill); var sp = TrainSpells[c - 1]; var pace = CirclePace[c - 1];
                if (sp.Area && k.State.Guarded == true) { why = "stopped: Magery reached circle 8, and Earthquake hurts everyone near you; the loop does not cast it inside a guarded zone"; return; }
                Status = "Loop " + l.Name + ": cast " + round + " of " + rounds + ": circle " + c + ", " + sp.Name + " (Magery " + (skill / 10.0).ToString("0.0") + ")";
                // Mana first: pre-AOS cost per circle.
                var manaBy = System.DateTime.UtcNow.AddMilliseconds(ManaWaitMs);
                while (true) {
                    int mana; lock (k.State.Gate) mana = k.State.Mana;
                    if (mana >= CircleMana[c - 1]) break;
                    if (stop || k.Closed) { why = stop ? "stopped" : "stopped: the connection closed"; return; }
                    if (System.DateTime.UtcNow > manaBy) { why = "stopped: mana stayed under " + CircleMana[c - 1] + " for " + ManaWaitMs / 60000 + " min"; return; }
                    Status = "Loop " + l.Name + ": waiting for mana " + mana + " of " + CircleMana[c - 1] + " for " + sp.Name;
                    System.Threading.Thread.Sleep(500);
                }
                var due = lastUse.AddMilliseconds(Delay(pace));
                while (System.DateTime.UtcNow < due) { if (stop || k.Closed) { why = stop ? "stopped" : "stopped: the connection closed"; return; } System.Threading.Thread.Sleep(50); }
                string heard;
                lock (actionGate) {
                    Answered(k, AnswerMs);
                    markJournal = k.State.JournalSeq;
                    string w = k.Cast(sp.Id, true); if (w != null) { why = "stopped: " + w; return; }
                    lastUse = System.DateTime.UtcNow;
                    var until = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
                    while (!k.MacroCursor && System.DateTime.UtcNow < until && k.State.JournalSince(markJournal).Count == 0) System.Threading.Thread.Sleep(20);
                    if (k.MacroCursor) {
                        uint me; int x, y, z, body; lock (k.State.Gate) { me = k.State.Player; x = k.State.X; y = k.State.Y; z = k.State.Z; k.State.Bodies.TryGetValue(me, out body); }
                        w = k.Target(false, me, x, y, z, body); if (w != null) { why = "stopped: aiming " + sp.Name + " at you: " + w; return; }
                    }
                    Answered(k, AnswerMs); System.Threading.Thread.Sleep(300);
                    heard = null;
                    foreach (var j in k.State.JournalSince(markJournal)) if (j.IndexOf("insufficient mana", System.StringComparison.OrdinalIgnoreCase) >= 0) heard = "mana";
                    if (heard == null) heard = Journal(k, pace);
                }
                if (heard == "mana" || heard == "again") { round--; continue; }
                if (heard != null) { why = "stopped: " + heard; return; }
                if (currentRun != null) currentRun.Rounds = round;
                if (!refusedThisRound) refusedInARow = 0;
                clean = refusedThisRound ? 0 : clean + 1; refusedThisRound = false;
                if (clean >= ProbeAfterRounds) { Worked(pace); clean = 0; }
                if (untilSkill > 0 && SkillOf(k, MagerySkill) >= untilSkill) { why = "done: Magery reached " + (SkillOf(k, MagerySkill) / 10.0).ToString("0.0"); return; }
            }
        } catch (System.Exception e) { why = "stopped: " + e.Message; }
        finally {
            Pacing = false; Status = "Loop " + l.Name + " " + why; Note(Status);
            var run = currentRun; currentRun = null;
            if (run != null) { run.End = System.DateTime.UtcNow; run.To = SkillOf(k, MagerySkill); lock (runs) runs.Add(run); }
        }
    }

    void RunSmith(GameLink k, LoopKind l, int rounds, int untilSkill) {
        string why = "done: " + rounds + " rounds"; int made = 0, exceptional = 0, failed = 0;
        try {
            Status = "Loop " + l.Name + ": round zero: double-click your smith's hammer and build the item once";
            var use = Watch(k, 0x06, 120000); if (use == null) { why = "stopped: no tool was double-clicked"; return; }
            uint hammer = U32(use, 1);
            byte[] lastMenu = null; System.Action<byte[]> menus = p => { if (p[0] == 0x7C) lastMenu = p; }; k.ServerPacket += menus;
            var picks = new System.Collections.Generic.List<byte[]>(); string item = null; int cost = 0; long mark0 = k.State.JournalSeq;
            try {
                while (picks.Count < SmithPicksMax) {
                    Status = "Loop " + l.Name + ": round zero: pick from the menu";
                    var pick = Watch(k, 0x7D, 120000); if (pick == null) { why = "stopped: no menu pick"; return; }
                    int index = (pick[7] << 8) | pick[8]; if (index == 0) { why = "stopped: the menu was cancelled"; return; }
                    picks.Add(pick); item = MenuItem(lastMenu, index); cost = MetalCost(item);
                    if (cost > 0) break;
                }
            } finally { k.ServerPacket -= menus; }
            if (cost == 0) { why = "stopped: no pick named a metal cost (last: " + (item ?? "none") + ")"; return; }
            item = item.StartsWith("Build ") ? item.Substring(6) : item; lastBuild = item; lastBuildAt = System.DateTime.UtcNow;
            Status = "Loop " + l.Name + ": round zero: building " + item;
            string first = CraftResult(k, mark0);
            if (first.StartsWith("\"")) { why = "stopped: " + first; return; }
            if (first == "exceptional") exceptional++; else if (first == "made") made++; else if (first == "failed") failed++;
            LoopWords = "Loop " + l.Name + ": double-click the hammer, pick " + picks.Count + " menus to " + item + " (" + cost + " ingots each), wait for the result; " + (l.Smith == 1 ? "until the pack holds fewer than " + cost + " ingots" : "until one is exceptional") + ", or " + rounds + " rounds, or F2";
            Note(LoopWords);
            if (l.Smith == 2 && first == "exceptional") { why = "done: round zero was exceptional"; return; }
            currentRun = new LoopRun { Name = l.Name, Skill = l.Skill, Start = System.DateTime.UtcNow, From = SkillOf(k, l.Skill) };
            for (int round = 1; round <= rounds; round++) {
                if (stop) { why = "stopped"; return; }
                if (k.Closed) { why = "stopped: the connection closed"; return; }
                int ingots = PackIngots(k); if (ingots < cost) { why = "done: " + ingots + " ingots left, " + item + " takes " + cost; return; }
                Status = "Loop " + l.Name + ": round " + round + " of " + rounds + ": " + item + " (" + ingots + " ingots)";
                long mark; string w = null;
                lock (actionGate) {
                    Answered(k, AnswerMs); mark = k.State.JournalSeq;
                    k.ClaimMenu = true;
                    try {
                        System.Action<byte[]> seen = p => { if (p[0] == 0x7C) lastMenu = p; };
                        lastMenu = null; k.ServerPacket += seen;
                        try {
                            k.Settle(); w = k.Use(hammer, false);
                            for (int i = 0; w == null && i < picks.Count; i++) {
                                var until = System.DateTime.UtcNow.AddMilliseconds(AnswerMs);
                                while (lastMenu == null && System.DateTime.UtcNow < until && !k.Closed) System.Threading.Thread.Sleep(20);
                                var m = lastMenu; lastMenu = null;
                                if (m == null) { string said = null; foreach (var j in k.State.JournalSince(mark)) said = j; w = said != null ? "\"" + said + "\"" : "the hammer opened no menu"; break; }
                                var r = (byte[])picks[i].Clone(); System.Array.Copy(m, 3, r, 1, 6);  // this menu's dialog serial and id
                                k.Settle(); w = k.Inject(r, "menu pick " + ((r[7] << 8) | r[8]), false);
                            }
                        } finally { k.ServerPacket -= seen; }
                    } finally { k.ClaimMenu = false; }
                    k.Settle();
                }
                if (w != null) { why = "stopped: " + w; return; }
                string got = CraftResult(k, mark);
                if (got.StartsWith("\"") || got.StartsWith("no result") || got == "stopped" || got.StartsWith("the connection")) { why = (got == "stopped" ? "" : "stopped: ") + got; if (got == "stopped") why = "stopped"; return; }
                if (got == "exceptional") exceptional++; else if (got == "made") made++; else failed++;
                currentRun.Rounds = round;
                if (l.Smith == 2 && got == "exceptional") { why = "done: an exceptional " + item + " in round " + round; return; }
                if (untilSkill > 0) { int v; lock (k.State.Gate) k.State.Skills.TryGetValue(l.Skill + 1, out v); if (v >= untilSkill) { why = "done: the skill reached " + (v / 10.0).ToString("0.0"); return; } }
            }
        } catch (System.Exception e) { why = "stopped: " + e.Message; }
        finally {
            k.ClaimMenu = false;
            Status = "Loop " + l.Name + " " + why + " (" + made + " made, " + exceptional + " exceptional, " + failed + " failed)"; Note(Status);
            var run = currentRun; currentRun = null;
            if (run != null) { run.End = System.DateTime.UtcNow; run.To = SkillOf(k, run.Skill); lock (runs) runs.Add(run); }
        }
    }

    // ---------- smelting ----------
    // One ore at a time (Damian, 2026-10-16: "given a target on the ground, take 1 of it and put it in backpack, then smelt
    // that one item. first iteration is manual"). Round zero is the player's: a lift from an ore pile on the ground, the
    // drop on a bag in the pack, a double-click on that ore and the forge picked. uo1998 keeps the pile's serial on a part
    // lift and gives the dropped piece a new one (relay log 2026-10-16 02:23:09 to 02:23:21), so the single is the ore that
    // appears in the bag after the drop; a pile gone under its serial is found again by ore art and hue within a tile.
    // A piece that lands as more than one is never used: its double-click would smelt the whole stack. A single comes as
    // amount 0 (0x25 at 02:36:54, relay log 2026-10-16), so only an amount above one is a stack.
    // uo1998 smelts without a line (02:36:58: the single deleted, an ingot stack of 2 into the pack), so the result is the
    // single gone, with the pack's ingots up (smelted) or not (failed). The UOAIX shard says "You smelt the ore ..."
    // (2026-10-15 15:28:26); RunUO cliloc 501990 "You burn away the impurities ..." is a failure. The single still there
    // after SmeltMs stops the loop with the last line heard.
    int SmeltMs { get { return Ms("smelt"); } }
    int SmeltSettleMs { get { return Ms("smelt-settle"); } }
    static readonly string[] SmeltLines = { "You smelt" }, SmeltFailLines = { "burn away", "You fail" };
    static readonly string[] SmeltPrompts = { "Select the forge", "Select another pile" };
    // Ingots in the pack, a stack sent as amount 0 counted as one.
    int SmeltIngots(GameLink k) {
        int n = 0;
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var kv in k.State.Items) if (kv.Value.Graphic == IngotArt && inside.Contains(kv.Value.Container)) n += System.Math.Max(1, kv.Value.Amount); }
        return n;
    }
    static bool IsOre(int graphic) { return System.Array.IndexOf(OreGraphics, graphic) >= 0; }
    static int Tiles(int x, int y, int x2, int y2) { return System.Math.Max(System.Math.Abs(x - x2), System.Math.Abs(y - y2)); }
    // A wait line since mark: "again" to repeat after the bisect's new delay, or why the loop stops; null when none.
    string SmeltWait(GameLink k, LoopKind l, long mark) {
        foreach (var j in k.State.JournalSince(mark)) foreach (var w in WaitLines) if (j.StartsWith(w, System.StringComparison.OrdinalIgnoreCase)) {
            string why = Refused(l); if (why != null) return why;
            lastUse = System.DateTime.UtcNow; refusedThisRound = true; return "again";
        }
        return null;
    }
    string SmeltResult(GameLink k, LoopKind l, long mark, uint ore, int ingots) {
        var start = System.DateTime.UtcNow; var until = start.AddMilliseconds(SmeltMs); string last = null;
        while (System.DateTime.UtcNow < until && !stop && !k.Closed) {
            foreach (var j in k.State.JournalSince(mark)) {
                foreach (var s in SmeltLines) if (j.StartsWith(s, System.StringComparison.OrdinalIgnoreCase)) { Seen("smelt", (int)(System.DateTime.UtcNow - start).TotalMilliseconds); return "smelted"; }
                foreach (var s in SmeltFailLines) if (j.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) { Seen("smelt", (int)(System.DateTime.UtcNow - start).TotalMilliseconds); return "failed"; }
                bool prompt = false; foreach (var s in SmeltPrompts) if (j.StartsWith(s, System.StringComparison.OrdinalIgnoreCase)) prompt = true;
                if (!prompt) last = j;
            }
            string wait = SmeltWait(k, l, mark); if (wait != null) return wait;
            if (Item(k, ore) == null) {
                Seen("smelt", (int)(System.DateTime.UtcNow - start).TotalMilliseconds);
                System.Threading.Thread.Sleep(SmeltSettleMs);
                if (Item(k, ore) == null) return SmeltIngots(k) > ingots ? "smelted" : "failed";
            }
            System.Threading.Thread.Sleep(50);
        }
        return stop ? "stopped" : k.Closed ? "the connection closed" : "no smelting result in " + SmeltMs / 1000 + " s: the ore " + ore.ToString("X") + " is still there" + (last != null ? "; the server said \"" + last + "\"" : "");
    }
    // A double-click or a lift leaves the player busy (BusyMs): uo1998 refused a lift 0.78 s after the loop's double-click
    // ("You must wait to perform another action." and 0x27 reason FF, relay log 2026-10-16 03:01:41.703 to 03:01:42.682).
    // The lift waits BusyMs after the last double-click and the double-click BusyMs after the lift; a lift still refused as
    // busy is tried again after BusyMs, and with auto-tune on busy grows by BusyStepMs.
    public const int BusyStepMs = 250, BusyRetries = 2;
    System.DateTime clickedAt = System.DateTime.MinValue, liftedAt = System.DateTime.MinValue;
    void BusyWait(System.DateTime since) { int wait = BusyMs - (int)(System.DateTime.UtcNow - since).TotalMilliseconds; if (wait > 0) System.Threading.Thread.Sleep(wait); }
    string BusyLift(GameLink k, uint serial, int amount = 1) {
        for (int attempt = 0; ; attempt++) {
            BusyWait(clickedAt);
            string why = Lifted(k, serial, amount);
            if (why == null) { liftedAt = System.DateTime.UtcNow; return null; }
            var r = lastRefusal; if (r == null || r[1] != 0xFF || attempt >= BusyRetries) return why;
            if (AutoTune) { int b = BusyMs; SetMs("busy", b + BusyStepMs); Note("Timing busy: " + b + " ms to " + (b + BusyStepMs) + " ms (the server refused a lift as busy)"); }
            clickedAt = System.DateTime.UtcNow;
        }
    }
    // Whole pile: after round zero the loop runs once for each ore left in the pile (the server sends a pile of one as
    // amount 0), in place of the rounds the player set.
    public bool WholePile { get { return Cfg.Get("smelt.whole", "off") == "on"; } set { Cfg.Set("smelt.whole", value ? "on" : "off"); } }
    int PileLeft(GameLink k, AssistItem pile) {
        lock (k.State.Gate) {
            AssistItem g; if (k.State.Ground.TryGetValue(pile.Serial, out g)) return System.Math.Max(1, g.Amount);
            AssistItem best = null;
            foreach (var c in k.State.Ground.Values) if (IsOre(c.Graphic) && c.Hue == pile.Hue && Tiles(c.X, c.Y, pile.X, pile.Y) <= 1 && (best == null || c.Amount > best.Amount)) best = c;
            return best == null ? 0 : System.Math.Max(1, best.Amount);
        }
    }
    // A failed smelt can leave the ore in the bag ("You burn away the impurities but are left with less useable metal.",
    // then the ore sent back into the bag, relay log 2026-10-16 03:05:02): that ore is smelted again, a retry not counted
    // as a round, up to MaxSmeltRetries times in a row.
    public const int MaxSmeltRetries = 10;
    bool StillInBag(GameLink k, uint ore, uint bag) {
        System.Threading.Thread.Sleep(SmeltSettleMs);
        var it = Item(k, ore); return it != null && it.Container == bag && it.Amount <= 1;
    }
    void RunSmelt(GameLink k, LoopKind l, int rounds, int untilSkill) {
        string why = "done: " + rounds + " rounds"; int smelted = 0, failed = 0;
        try {
            // The pile as the lift found it: the server takes it off the ground before it sends the rest back.
            AssistItem pile = null;
            System.Action<byte[]> lifting = p => {
                if (p[0] != 0x07 || p.Length < 7) return;
                lock (k.State.Gate) { AssistItem g; if (k.State.Ground.TryGetValue(U32(p, 1), out g)) pile = new AssistItem { Serial = g.Serial, Graphic = g.Graphic, Hue = g.Hue, X = g.X, Y = g.Y, Z = g.Z, Amount = g.Amount }; }
            };
            byte[] lift; k.ClientPacket += lifting;
            try { Status = "Loop " + l.Name + ": round zero: lift one ore from a pile on the ground"; lift = Watch(k, 0x07, 120000); } finally { k.ClientPacket -= lifting; }
            if (lift == null) { why = "stopped: nothing was lifted"; return; }
            if (pile == null || !IsOre(pile.Graphic)) { why = "stopped: the lift was not from an ore pile on the ground"; return; }
            Status = "Loop " + l.Name + ": round zero: drop the ore on a bag in your pack";
            var drop = Watch(k, 0x08, 120000); if (drop == null) { why = "stopped: the ore was not dropped"; return; }
            uint bag = U32(drop, 10); var onto = Item(k, bag); bool inPack; lock (k.State.Gate) inPack = PackTree(k).Contains(bag);
            if (!inPack) { why = "stopped: the ore was dropped outside the pack; drop it on your backpack or a bag in it"; return; }
            if (onto != null && IsOre(onto.Graphic)) { why = "stopped: the ore was dropped onto ore; drop it on the bag itself"; return; }
            Status = "Loop " + l.Name + ": round zero: double-click the ore";
            var used = Watch(k, 0x06, 120000); if (used == null) { why = "stopped: the ore was not double-clicked"; return; }
            clickedAt = System.DateTime.UtcNow;
            long mark0 = k.State.JournalSeq; int ingots0 = SmeltIngots(k);
            Status = "Loop " + l.Name + ": round zero: pick the forge";
            var t = Watch(k, 0x6C, 120000); if (t == null) { why = "stopped: no forge was picked"; return; }
            var forge = TargetStep(t); if (!forge.Ground && forge.Serial == 0) { why = "stopped: the cursor was cancelled"; return; }
            lastUse = System.DateTime.UtcNow; refusedInARow = 0;
            string first = SmeltResult(k, l, mark0, U32(used, 1), ingots0);
            if (first == "smelted") smelted++; else if (first == "failed") failed++; else if (first != "again") { why = "stopped: round zero: " + first; return; }
            leave.Add(pile.Serial);
            if (WholePile) {
                rounds = PileLeft(k, pile);
                if (rounds == 0) { why = "done: round zero took the last ore of the pile"; return; }
            }
            LoopWords = "Loop " + l.Name + ": lift one ore from the pile at " + pile.X + "," + pile.Y + ", drop it in " + bag.ToString("X") + ", double-click it, " + forge.Words + ", wait for the result and " + Delay(l) + " ms from the last smelt; until the pile is gone, or " + rounds + " rounds" + (untilSkill > 0 ? ", or until Mining reaches " + (untilSkill / 10.0).ToString("0.0") : "") + ", or F2";
            Note(LoopWords);
            Pacing = true; int clean = 0;
            currentRun = new LoopRun { Name = l.Name, Skill = MiningSkill, Start = System.DateTime.UtcNow, From = SkillOf(k, MiningSkill) };
            uint single = first == "failed" && StillInBag(k, U32(used, 1), bag) ? U32(used, 1) : 0; int retries = 0;
            for (int round = 1; round <= rounds; round++) {
                if (stop) { why = "stopped"; return; }
                if (k.Closed) { why = "stopped: the connection closed"; return; }
                if (k.Halted != null) { why = "stopped: " + k.Halted; return; }
                refusedThisRound = false;
                Status = "Loop " + l.Name + ": round " + round + " of " + rounds + " (" + smelted + " smelted, " + failed + " failed)";
                long mark = 0; int ingots = 0; string w = null;
                lock (actionGate) {
                    Answered(k, AnswerMs);
                    // A single left by a refused smelt is used again.
                    var have = single != 0 ? Item(k, single) : null;
                    if (have == null || have.Container != bag || have.Amount > 1) {
                        single = 0; AssistItem src = null; int px, py;
                        lock (k.State.Gate) {
                            px = k.State.X; py = k.State.Y; AssistItem g;
                            if (k.State.Ground.TryGetValue(pile.Serial, out g)) src = g;
                            else foreach (var c in k.State.Ground.Values) if (IsOre(c.Graphic) && c.Hue == pile.Hue && Tiles(c.X, c.Y, pile.X, pile.Y) <= 1 && (src == null || c.Amount > src.Amount)) src = c;
                        }
                        if (src == null) w = "done: the ore pile is used up";
                        else if (Tiles(src.X, src.Y, px, py) > LiftReach) w = "stopped: the ore pile is " + Tiles(src.X, src.Y, px, py) + " tiles away; a lift reaches " + LiftReach;
                        else {
                            pile.Serial = src.Serial; leave.Add(src.Serial);
                            var before = new System.Collections.Generic.HashSet<uint>();
                            lock (k.State.Gate) foreach (var kv in k.State.Items) if (kv.Value.Container == bag) before.Add(kv.Key);
                            w = BusyLift(k, src.Serial);
                            if (w != null) w = "stopped: lifting one ore: " + w;
                            else if ((w = k.Drop(src.Serial, bag)) != null) w = "stopped: " + Stopped("Smelting", src.Serial, w);
                            else {
                                Answered(k, AnswerMs);
                                var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs); AssistItem got = null;
                                while (got == null && System.DateTime.UtcNow < by && !k.Closed) {
                                    lock (k.State.Gate) foreach (var kv in k.State.Items) if (kv.Value.Container == bag && !before.Contains(kv.Key) && IsOre(kv.Value.Graphic)) { got = kv.Value; break; }
                                    if (got == null) System.Threading.Thread.Sleep(50);
                                }
                                if (got == null) w = "stopped: the lifted ore did not appear in " + bag.ToString("X") + " as an item of its own (stacked onto ore there?)";
                                else if (got.Amount > 1) w ="stopped: the ore landed as a stack of " + got.Amount + " (" + got.Serial.ToString("X") + "); the loop does not smelt a stack";
                                else single = got.Serial;
                            }
                        }
                    }
                }
                if (w != null) { why = w; return; }
                // The server refuses a double-click 1.9 s after the last smelt's target (relay log 2026-10-16 02:42:40.555 to
                // 02:42:42.444, "You must wait to perform another action."); the lift and the drop at 1.1 s were accepted. So
                // the delay runs from the target to the next double-click, and the single is fetched inside it.
                var due = lastUse.AddMilliseconds(Delay(l));
                while (System.DateTime.UtcNow < due) { if (stop || k.Closed) { why = stop ? "stopped" : "stopped: the connection closed"; return; } System.Threading.Thread.Sleep(50); }
                lock (actionGate) {
                    Answered(k, AnswerMs);
                    if (Item(k, single) == null) w = "stopped: the ore " + single.ToString("X") + " left the bag before its smelt";
                    if (w == null) {
                        BusyWait(liftedAt);
                        mark = k.State.JournalSeq; ingots = SmeltIngots(k);
                        w = k.Use(single, true);
                        if (w != null) w = "stopped: " + w;
                        else {
                            lastUse = clickedAt = System.DateTime.UtcNow;
                            var until = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
                            while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed) System.Threading.Thread.Sleep(20);
                            if (k.MacroCursor) Seen("cursor", (int)(System.DateTime.UtcNow - lastUse).TotalMilliseconds);
                            if (!k.MacroCursor) {
                                k.Settle(); w = SmeltWait(k, l, mark);
                                if (w == null) { string said = null; foreach (var j in k.State.JournalSince(mark)) said = j; w = "stopped: the ore opened no forge cursor" + (said != null ? "; the server said \"" + said + "\"" : ""); }
                                else if (w != "again") w = "stopped: " + w;
                            } else {
                                w = k.Target(forge.Ground, forge.Serial, forge.X, forge.Y, forge.Z, forge.Graphic);
                                if (w != null) w = "stopped: aiming at the forge: " + w; else lastUse = System.DateTime.UtcNow;
                            }
                        }
                    }
                }
                if (w == "again") { round--; continue; }
                if (w != null) { why = w; return; }
                string res = SmeltResult(k, l, mark, single, ingots);
                if (res == "again") { round--; continue; }
                if (res == "smelted") smelted++; else if (res == "failed") failed++; else { why = "stopped: " + res; return; }
                if (res == "failed" && StillInBag(k, single, bag)) {
                    if (++retries > MaxSmeltRetries) { why = "stopped: the ore " + single.ToString("X") + " failed " + retries + " times in a row"; return; }
                    Note("Loop " + l.Name + ": the smelt failed and the ore " + single.ToString("X") + " is still in the bag; smelting it again");
                    round--; continue;
                }
                retries = 0; single = 0;
                currentRun.Rounds = round;
                if (!refusedThisRound) refusedInARow = 0;
                clean = refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds) { Worked(l); clean = 0; }
                if (untilSkill > 0 && SkillOf(k, MiningSkill) >= untilSkill) { why = "done: Mining reached " + (SkillOf(k, MiningSkill) / 10.0).ToString("0.0"); return; }
            }
        } catch (System.Exception e) { why = "stopped: " + e.Message; }
        finally {
            Pacing = false; Status = "Loop " + l.Name + " " + why + " (" + smelted + " smelted, " + failed + " failed)"; Note(Status);
            var run = currentRun; currentRun = null;
            if (run != null) { run.End = System.DateTime.UtcNow; run.To = SkillOf(k, MiningSkill); lock (runs) runs.Add(run); }
        }
    }

    // ---------- tailoring ----------
    // Damian, 2026-10-16: "start with tareting a bolt or pile of bolts, use one of that during the demo, it will produce a
    // pile of cloth and the garment if sucessful. use that pile of cloth until there isn't enough left over, then start a
    // new bolt from the same pile of bolts if possible". uo1998, relay log 2026-10-16 04:19:39 to 04:22:09: a double-click on
    // the sewing kit says "What cloth shall I use this sewing kit on?" and opens a cursor; a bolt (0x0F95, a stack of 2)
    // targeted is cut and its left-over cloth (0x1766) put in the pack ("You place the left-over cloth pieces into your
    // backpack"), stacked onto the pile already there (3 to 7, same serial); then item menus whose dialog serial is the kit
    // ("Choose a category.", "What kind of shirt?"). "You create the fancy shirt and put it in your backpack." took one cloth;
    // "Tailoring failed. Some of the cloth is ruined." took none. Round zero is the player's: the kit, the bolts or the cloth,
    // the picks. Each later round sews from the cloth pile the last bolt fed (the pack's cloth item that appeared or grew;
    // cloth of another kind may not stack), and cuts a bolt from the same stack when that pile is gone or answers with no
    // menu or no result: how uo1998 says "not enough cloth" was not seen.
    static readonly string[] TailorFailLines = { "failed", "ruined" };
    static readonly string[] TailorPrompts = { "What cloth", "You place the left-over" };
    static bool IsCloth(int g) { string n = UoTiles.Name(g).ToLowerInvariant(); return n.Contains("cloth") && !n.Contains("bolt"); }
    // The bolts of cloth' arts, not the name: "bolt" also names the Energy Bolt scroll (0x1F56), sewn at once the spellbook
    // was opened (2026-10-17 09:10:26, "Can't use a sewing kit on that.").
    static bool IsBolt(int g) { return g >= 0x0F95 && g <= 0x0F9C; }
    int TailorMs { get { return Ms("tailor"); } }
    System.Collections.Generic.Dictionary<uint, int> PackCloth(GameLink k) {
        var d = new System.Collections.Generic.Dictionary<uint, int>();
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container) && IsCloth(kv.Value.Graphic)) d[kv.Key] = System.Math.Max(1, kv.Value.Amount); }
        return d;
    }
    // The cloth a bolt fed: the pack's cloth item that appeared or grew since before; 0 when none did.
    uint FedCloth(GameLink k, System.Collections.Generic.Dictionary<uint, int> before) {
        foreach (var kv in PackCloth(k)) { int was; if (!before.TryGetValue(kv.Key, out was) || kv.Value > was) return kv.Key; }
        return 0;
    }
    string TailorLast(GameLink k, long mark) {
        string last = null;
        foreach (var j in k.State.JournalSince(mark)) { bool prompt = false; foreach (var s in TailorPrompts) if (j.StartsWith(s, System.StringComparison.OrdinalIgnoreCase)) prompt = true; if (!prompt) last = j; }
        return last;
    }
    // "made", "exceptional", "failed", "again" (a wait line), or why there was no result.
    string TailorResult(GameLink k, LoopKind l, long mark, int ms) {
        var start = System.DateTime.UtcNow; var until = start.AddMilliseconds(ms);
        while (System.DateTime.UtcNow < until && !stop && !k.Closed) {
            foreach (var j in k.State.JournalSince(mark)) {
                if (j.StartsWith("You create", System.StringComparison.OrdinalIgnoreCase)) {
                    Seen("tailor", (int)(System.DateTime.UtcNow - start).TotalMilliseconds);
                    System.Threading.Thread.Sleep(QualityWaitMs);
                    foreach (var q in k.State.JournalSince(mark)) if (q.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0) return "exceptional";
                    return "made";
                }
                foreach (var f in TailorFailLines) if (j.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) { Seen("tailor", (int)(System.DateTime.UtcNow - start).TotalMilliseconds); return "failed"; }
            }
            string wait = SmeltWait(k, l, mark); if (wait != null) return wait;
            System.Threading.Thread.Sleep(50);
        }
        string said = TailorLast(k, mark);
        return stop ? "stopped" : k.Closed ? "the connection closed" : "no result in " + ms / 1000 + " s" + (said != null ? "; the server said \"" + said + "\"" : "");
    }
    // One sewing under actionGate: the kit used, its cursor aimed at the material, the recorded picks replayed into menus
    // kept off the client's screen. Null when the last pick went out; "no cursor" or "no menu" when the server answered
    // without one; otherwise why the loop stops.
    string Sew(GameLink k, uint kit, uint material, System.Collections.Generic.List<byte[]> picks) {
        return Sew(k, kit, material, picks.Count, (i, m) => { var r = (byte[])picks[i].Clone(); System.Array.Copy(m, 3, r, 1, 6); return r; });
    }
    // A menu's pick by its text (the entry whose text begins so): 0x7D, the menu's dialog serial and id, the entry's
    // 1-based index, model and hue (uo1998 04:19:44, 7D 45ADB8BD 0011 0001 1517 0000); null when no entry matches. An
    // entry is model, hue, a length byte and the text.
    static byte[] MenuPick(byte[] m, string text) {
        if (m == null || m.Length < 11) return null;
        int at = 10 + m[9]; if (at >= m.Length) return null; int n = m[at]; at++;
        for (int i = 1; i <= n && at + 5 <= m.Length; i++) {
            int len = m[at + 4]; if (at + 5 + len > m.Length) return null;
            if (System.Text.Encoding.ASCII.GetString(m, at + 5, len).StartsWith(text, System.StringComparison.OrdinalIgnoreCase)) {
                var r = new byte[13]; r[0] = 0x7D; System.Array.Copy(m, 3, r, 1, 6); r[7] = (byte)(i >> 8); r[8] = (byte)i; r[9] = m[at]; r[10] = m[at + 1]; r[11] = m[at + 2]; r[12] = m[at + 3];
                return r;
            }
            at += 5 + len;
        }
        return null;
    }
    string Sew(GameLink k, uint kit, uint material, int menus, System.Func<int, byte[], byte[]> pick) {
        var mat = Item(k, material); if (mat == null) return "the cloth " + material.ToString("X") + " is gone";
        byte[] menu = null; System.Action<byte[]> seen = p => { if (p[0] == 0x7C) menu = p; };
        k.ClaimMenu = true; k.ServerPacket += seen;
        try {
            string w = k.Use(kit, true); if (w != null) return w;
            lastUse = System.DateTime.UtcNow;
            var until = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
            while (!k.MacroCursor && System.DateTime.UtcNow < until && !k.Closed) System.Threading.Thread.Sleep(20);
            if (!k.MacroCursor) { k.Settle(); return "no cursor"; }
            Seen("cursor", (int)(System.DateTime.UtcNow - lastUse).TotalMilliseconds);
            w = k.Target(false, material, mat.X, mat.Y, 0, mat.Graphic); if (w != null) return "aiming at the cloth: " + w;
            for (int i = 0; i < menus; i++) {
                var by = System.DateTime.UtcNow.AddMilliseconds(AnswerMs);
                while (menu == null && System.DateTime.UtcNow < by && !k.Closed) System.Threading.Thread.Sleep(20);
                var m = menu; menu = null;
                if (m == null) return "no menu";
                var r = pick(i, m); if (r == null) return "the menu has no entry for this item";
                k.Settle(); w = k.Inject(r, "menu pick " + ((r[7] << 8) | r[8]), false); if (w != null) return w;
            }
            return null;
        } finally { k.ServerPacket -= seen; k.ClaimMenu = false; }
    }
    // Scraps (Damian, 2026-10-16: "just move the scrap pile the floor somewere", "just look for a tile that is available
    // like in mining"): cloth the server would not sew is lifted whole and put on a tile beside the player, as DumpOre puts
    // ore down: the four tiles around (south, west, east, north), those with nothing on them first, the next tried when
    // the pile comes back into the pack. Autopickup leaves it there. A lift of fewer than the pile leaves the rest in the
    // pack, which is lifted again the same way.
    // The tile of the last drop that left the pack is tried first while it is within ScrapReach (Damian, 2026-10-17: "add
    // to the remove scraps code a rememberance of where the last successful drop happened, and try there again if
    // possible"); the pack has an item limit as well as a weight limit, so scraps kept in it cost both.
    static readonly int[,] ScrapTiles = { { 0, 1 }, { -1, 0 }, { 1, 0 }, { 0, -1 } };
    public const int ScrapReach = 2;
    int scrapsDropped; int[] lastScrapTile;
    void DropScraps(GameLink k, uint cloth) {
        string end = null; int x = 0, y = 0, z = 0, px, py;
        var tiles = new System.Collections.Generic.List<int[]>();
        lock (k.State.Gate) {
            px = k.State.X; py = k.State.Y;
            var last = lastScrapTile; if (last != null && Tiles(last[0], last[1], px, py) <= ScrapReach) tiles.Add(new[] { last[0], last[1] });
            var taken = new System.Collections.Generic.HashSet<long>(); foreach (var g in k.State.Ground.Values) taken.Add(((long)g.X << 16) | (uint)g.Y);
            for (int pass = 0; pass < 2; pass++) for (int i = 0; i < ScrapTiles.GetLength(0); i++) {
                int tx = px + ScrapTiles[i, 0], ty = py + ScrapTiles[i, 1];
                if (taken.Contains(((long)tx << 16) | (uint)ty) == (pass == 1) && !tiles.Exists(t => t[0] == tx && t[1] == ty)) tiles.Add(new[] { tx, ty });
            }
        }
        for (int attempt = 0; attempt < tiles.Count && Item(k, cloth) != null && !stop && !k.Closed; attempt++) {
            lock (actionGate) {
                Answered(k, AnswerMs);
                var c = Item(k, cloth); if (c == null) break;
                lock (k.State.Gate) z = k.State.Z;
                int dx = tiles[attempt][0], dy = tiles[attempt][1];
                string w = BusyLift(k, cloth, System.Math.Max(1, c.Amount)); if (w != null) { end = "lifting the scrap cloth: " + w; break; }
                w = k.DropGround(cloth, dx, dy, z); if (w != null) { end = Stopped("Putting scrap cloth down", cloth, w); break; }
                Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs);
                leave.Add(cloth);
                x = dx; y = dy;
                if (Item(k, cloth) == null) lastScrapTile = new[] { dx, dy };
            }
        }
        if (end == null && Item(k, cloth) != null) end = "it came back into the pack from every tile around you";
        if (end == null) scrapsDropped++;
        Note("Loop: the scrap cloth " + cloth.ToString("X") + (end == null ? " is on the ground at " + x + "," + y : " stayed in the pack: " + end));
    }
    // All cloth: the loop runs until the cloth and the bolts are used up, in place of the rounds the player set.
    public bool AllCloth { get { return Cfg.Get("tailor.all", "off") == "on"; } set { Cfg.Set("tailor.all", value ? "on" : "off"); } }
    void RunTailor(GameLink k, LoopKind l, int rounds, int untilSkill) {
        bool all = AllCloth; if (all) rounds = int.MaxValue;
        string count = all ? "until all the cloth is used" : rounds + " rounds";
        string why = "done: " + count; int made = 0, exceptional = 0, failed = 0, cut = 0; scrapsDropped = 0;
        try {
            Status = "Loop " + l.Name + ": round zero: double-click your sewing kit";
            var use = Watch(k, 0x06, 120000); if (use == null) { why = "stopped: no sewing kit was double-clicked"; return; }
            uint kit = U32(use, 1) & 0x7FFFFFFF;
            var before = PackCloth(k);
            Status = "Loop " + l.Name + ": round zero: target the bolts or the cloth";
            var t = Watch(k, 0x6C, 120000); if (t == null) { why = "stopped: nothing was targeted"; return; }
            long mark0 = k.State.JournalSeq;
            uint first = U32(t, 7); var firstItem = first != 0 ? Item(k, first) : null;
            if (firstItem == null) { why = "stopped: target the bolts or the cloth in your pack"; return; }
            bool fromBolt = IsBolt(firstItem.Graphic);
            if (!fromBolt && !IsCloth(firstItem.Graphic)) { why = "stopped: the target, a " + UoTiles.Name(firstItem.Graphic) + ", is neither bolts nor cloth"; return; }
            // The picks, until the result.
            var picks = new System.Collections.Generic.List<byte[]>(); string got = null;
            System.Action<byte[]> picked = p => { if (p[0] == 0x7D) lock (picks) picks.Add(p); };
            k.ClientPacket += picked;
            try {
                Status = "Loop " + l.Name + ": round zero: pick from the menus";
                got = TailorResult(k, l, mark0, 120000);
            } finally { k.ClientPacket -= picked; }
            if (got != "made" && got != "exceptional" && got != "failed") { why = "stopped: round zero: " + got; return; }
            if (picks.Count == 0) { why = "stopped: round zero made no menu pick"; return; }
            foreach (var p in picks) if (((p[7] << 8) | p[8]) == 0) { why = "stopped: a menu was cancelled in round zero"; return; }
            if (got == "failed") failed++; else if (got == "exceptional") exceptional++; else made++;
            // The item round zero's last pick named; the loop's own picks never reach the client, so each round renews it
            // for the tailoring book.
            string sewnItem = lastSewn;
            System.Threading.Thread.Sleep(SmeltSettleMs);
            uint bolt = fromBolt ? first : 0, cloth = fromBolt ? FedCloth(k, before) : first;
            if (fromBolt) cut++;
            LoopWords = "Loop " + l.Name + ": double-click the sewing kit, target " + (cloth != 0 ? "the cloth " + cloth.ToString("X") : "a bolt") + (bolt != 0 ? ", a bolt from " + bolt.ToString("X") + " when the cloth runs out" : "") + ", pick " + picks.Count + " menus; " + count + (untilSkill > 0 ? ", or until Tailoring reaches " + (untilSkill / 10.0).ToString("0.0") : "") + ", or F2";
            Note(LoopWords);
            Pacing = true; refusedInARow = 0; int clean = 0;
            currentRun = new LoopRun { Name = l.Name, Skill = l.Skill, Start = System.DateTime.UtcNow, From = SkillOf(k, l.Skill) };
            for (int round = 1; round <= rounds; round++) {
                if (stop) { why = "stopped"; return; }
                if (k.Closed) { why = "stopped: the connection closed"; return; }
                if (k.Halted != null) { why = "stopped: " + k.Halted; return; }
                refusedThisRound = false;
                // The kit is sent again now and then (deleted and re-sent, 04:21:24.977 to 04:21:25.858): gone past
                // StackResendMs, it is used up.
                var kitBy = System.DateTime.UtcNow.AddMilliseconds(StackResendMs);
                while (Item(k, kit) == null && System.DateTime.UtcNow < kitBy) System.Threading.Thread.Sleep(20);
                if (Item(k, kit) == null) { why = "stopped: the sewing kit " + kit.ToString("X") + " is gone (worn out?)"; return; }
                bool fromCloth = cloth != 0 && Item(k, cloth) != null;
                uint mat = fromCloth ? cloth : bolt;
                if (mat == 0 || Item(k, mat) == null) { why = "done: " + (bolt != 0 ? "the bolts are used up" : "the cloth is used up"); return; }
                Status = "Loop " + l.Name + ": round " + round + (all ? "" : " of " + rounds) + " from the " + (fromCloth ? "cloth" : "bolts") + " (" + made + " made, " + exceptional + " exceptional, " + failed + " failed)";
                var due = lastUse.AddMilliseconds(Delay(l));
                while (System.DateTime.UtcNow < due) { if (stop || k.Closed) { why = stop ? "stopped" : "stopped: the connection closed"; return; } System.Threading.Thread.Sleep(50); }
                var pre = PackCloth(k); long mark; string w;
                lock (actionGate) { Answered(k, AnswerMs); mark = k.State.JournalSeq; if (sewnItem != null) { lastSewn = sewnItem; lastSewnAt = System.DateTime.UtcNow; } w = Sew(k, kit, mat, picks); }
                string res = w == null ? TailorResult(k, l, mark, TailorMs) : w == "no cursor" || w == "no menu" ? (SmeltWait(k, l, mark) ?? w) : null;
                if (w != null && res == null) { why = "stopped: " + w; return; }
                if (res == "again") { round--; continue; }
                if (res != "made" && res != "exceptional" && res != "failed") {
                    string said = res == "stopped" ? null : TailorLast(k, mark);
                    // Cloth the server answered without sewing is scraps; a pile that only went quiet (lag) stays.
                    if (fromCloth && said != null) DropScraps(k, cloth);
                    if (res != "stopped" && fromCloth && bolt != 0 && Item(k, bolt) != null) {
                        Note("Loop " + l.Name + ": the cloth " + cloth.ToString("X") + " gave " + (res == "no menu" || res == "no cursor" ? res : "no result") + (said != null ? " (\"" + said + "\")" : "") + "; cutting a bolt");
                        cloth = 0; round--; continue;
                    }
                    if (res == "stopped") { why = "stopped"; return; }
                    if (fromCloth && said != null && (bolt == 0 || Item(k, bolt) == null)) { why = "done: all the cloth is used (the server said \"" + said + "\")"; return; }
                    string last = said;
                    why = "stopped: the " + (fromCloth ? "cloth" : "bolt") + " gave " + res + (last != null && res.IndexOf(last) < 0 ? "; the server said \"" + last + "\"" : ""); return;
                }
                if (res == "failed") failed++; else if (res == "exceptional") exceptional++; else made++;
                if (!fromCloth) { cut++; System.Threading.Thread.Sleep(SmeltSettleMs); uint fed = FedCloth(k, pre); if (fed != 0) cloth = fed; }
                currentRun.Rounds = round;
                if (!refusedThisRound) refusedInARow = 0;
                clean = refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds) { Worked(l); clean = 0; }
                if (untilSkill > 0 && SkillOf(k, l.Skill) >= untilSkill) { why = "done: Tailoring reached " + (SkillOf(k, l.Skill) / 10.0).ToString("0.0"); return; }
            }
        } catch (System.Exception e) { why = "stopped: " + e.Message; }
        finally {
            k.ClaimMenu = false; Pacing = false;
            Status = "Loop " + l.Name + " " + why + " (" + made + " made, " + exceptional + " exceptional, " + failed + " failed, " + cut + " bolts cut, " + scrapsDropped + " scrap piles put down)"; Note(Status);
            var run = currentRun; currentRun = null;
            if (run != null) { run.End = System.DateTime.UtcNow; run.To = SkillOf(k, run.Skill); lock (runs) runs.Add(run); }
        }
    }

    // ---------- smithing value: what each build made and what NPCs pay for it ----------
    public SmithBook Smith, Tailor;
    // The other crafts' value (Damian, 2026-10-18: "expand it to cover all other skills too"): each craft's book is fed as
    // smithing's is, from the menus of the tool double-clicked last (by its name in the 1.25 tile data, the first craft
    // whose tool word it holds): an entry's cost where it shows one ("takes N <material>", or ", N <material>."), the
    // build picked and its result line, what NPCs offer and pay. Skill ids are the 1998 ones (inscribe.m 0x17,
    // alchemy.m 0x00, fletcher.m 0x08).
    public sealed class CraftKind { public string Name, Unit; public int Skill; public string[] Tools; public SmithBook Book; }
    public readonly CraftKind[] Crafts = {
        new CraftKind { Name = "Carpentry", Skill = 11, Unit = "board", Tools = new[] { "saw", "froe", "inshave", "scorp", "plane", "draw knife" } },
        new CraftKind { Name = "Tinkering", Skill = 37, Unit = "ingot", Tools = new[] { "tinker" } },
        new CraftKind { Name = "Bowcraft", Skill = 8, Unit = "log", Tools = new[] { "dagger", "knife" } },
        new CraftKind { Name = "Alchemy", Skill = 0, Unit = "reagent", Tools = new[] { "mortar" } },
        new CraftKind { Name = "Cartography", Skill = 12, Unit = "blank map", Tools = new[] { "mapmaker" } },
        new CraftKind { Name = "Inscription", Skill = 23, Unit = "scroll", Tools = new[] { "scribe" } },
        new CraftKind { Name = "Cooking", Skill = 13, Unit = "ingredient", Tools = new[] { "rolling pin", "skillet", "sifter" } },
    };
    volatile CraftKind craftTool, lastCraft; System.DateTime craftToolAt, lastCraftAt; volatile string lastCraftItem;
    CraftKind CraftOf(int graphic) { string n = UoTiles.Name(graphic).ToLowerInvariant(); foreach (var c in Crafts) foreach (var t in c.Tools) if (n.Contains(t)) return c; return null; }
    CraftKind CraftNow() { var c = craftTool; return c != null && (System.DateTime.UtcNow - craftToolAt).TotalSeconds < 60 ? c : null; }
    static int CraftCost(string text) {
        if (text == null) return 0; int v;
        int at = text.IndexOf("takes ", System.StringComparison.OrdinalIgnoreCase);
        if (at >= 0) { var p = text.Substring(at + 6).Split(' '); return p.Length >= 2 && int.TryParse(p[0], out v) ? v : 0; }
        int comma = text.LastIndexOf(','); if (comma < 0) return 0;
        var q = text.Substring(comma + 1).Trim().Split(' '); return q.Length >= 2 && int.TryParse(q[0], out v) ? v : 0;
    }
    // A result line: "exceptional", "average" or "failed"; null when the line is not a result.
    static string CraftLine(string text) {
        foreach (var f in new[] { "failed", "fail to", "ruined", "toss the failed", "You burn", "destroyed", "lost some" }) if (text.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) return "failed";
        foreach (var s in new[] { "You create", "You make", "You put the", "You pour the completed", "You carefully" })
            if (text.StartsWith(s, System.StringComparison.OrdinalIgnoreCase)) return text.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "exceptional" : "average";
        return null;
    }
    // Tailoring's cost: "Fancy shirt - takes 8 cloth." (uo1998 tailor menus, relay log 2026-10-16 04:19:44).
    public const int TailoringSkill = 34;
    static int ClothCost(string text) {
        if (text == null) return 0; int at = text.IndexOf("takes ", System.StringComparison.OrdinalIgnoreCase); if (at < 0) return 0;
        var parts = text.Substring(at + 6).Split(' '); int v;
        return parts.Length >= 2 && parts[1].StartsWith("cloth", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[0], out v) ? v : 0;
    }
    volatile string lastSewn; System.DateTime lastSewnAt;
    volatile byte[] lastMenuSeen; volatile string lastBuild; System.DateTime lastBuildAt;
    readonly System.Collections.Generic.List<object[]> recentAdds = new System.Collections.Generic.List<object[]>();  // serial, graphic, at
    object[] pendingCraft; volatile string pendingQuality;
    public const int SmithySkill = 7, CraftMatchMs = 2000;
    int QualityWaitMs { get { return Ms("quality-wait"); } }
    int LabelWaitMs { get { return Ms("label-wait"); } }
    void SmithPacket(GameLink k, byte[] p) {
        if (Smith == null) return;
        switch (p[0]) {
            case 0x1D: if (p.Length >= 5) SaleDeleted(U32(p, 1)); break;
            case 0x54: if (p.Length >= 4 && ((p[2] << 8) | p[3]) == HammerSound) {
                    // The gap between two bangs of one build is what craft-quiet waits out.
                    var now = System.DateTime.UtcNow; int gap = (int)(now - lastBang).TotalMilliseconds;
                    if (gap < CraftQuietMs * 3) Seen("craft-quiet", gap);
                    lastBang = now;
                }
                break;
            case 0x7C:
                lastMenuSeen = p;
                { var craft = CraftNow();
                  for (int i = 1; i < 64; i++) {
                      var t = MenuItem(p, i); if (t == null) break;
                      if (craft != null) { int cc = CraftCost(t); if (cc > 0) craft.Book.Cost(t, cc); continue; }
                      int c = MetalCost(t); if (c > 0) Smith.Cost(t, c); c = ClothCost(t); if (c > 0 && Tailor != null) Tailor.Cost(t, c);
                  } }
                break;
            // An item into a container: serial, graphic, a byte, amount, x, y, container, hue (uo1998 22:21:53, 2545679DFE1411...).
            case 0x25:
                if (p.Length >= 20) lock (recentAdds) { recentAdds.Add(new object[] { U32(p, 1), (p[5] << 8) | p[6], System.DateTime.UtcNow }); if (recentAdds.Count > 12) recentAdds.RemoveAt(0); }
                break;
            case 0x1C:
                if (p.Length <= 44 || U32(p, 3) != 0xFFFFFFFF) break;
                string text = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim();
                if (lastCraft != null && (System.DateTime.UtcNow - lastCraftAt).TotalSeconds < 60) {
                    var c = lastCraft; string q = CraftLine(text);
                    if (q != null) { lastCraft = null; c.Book.Crafted(0, lastCraftItem, q, SkillOf(k, c.Skill)); }
                    break;
                }
                if (Tailor != null && lastSewn != null && (System.DateTime.UtcNow - lastSewnAt).TotalSeconds < 60) {
                    string sewn = lastSewn;
                    if (text.StartsWith("You create", System.StringComparison.OrdinalIgnoreCase)) {
                        // The garment, not the cloth pile sent again beside it (04:20:25.950: the pile 45ADDD06 then the shirt).
                        uint garment = 0; var now = System.DateTime.UtcNow;
                        lock (recentAdds) for (int i = recentAdds.Count - 1; i >= 0; i--) { var a = recentAdds[i]; if (!IsCloth((int)a[1]) && !IsBolt((int)a[1]) && (now - (System.DateTime)a[2]).TotalMilliseconds < CraftMatchMs) { garment = (uint)a[0]; break; } }
                        long mark = k.State.JournalSeq - 1; int skill = SkillOf(k, TailoringSkill);
                        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                            System.Threading.Thread.Sleep(QualityWaitMs);
                            string q = "average"; foreach (var j in k.State.JournalSince(mark)) if (j.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0) q = "exceptional";
                            Tailor.Crafted(garment, sewn, q, skill);
                        });
                    } else if (text.IndexOf("Tailoring failed", System.StringComparison.OrdinalIgnoreCase) >= 0) Tailor.Crafted(0, sewn, "failed", SkillOf(k, TailoringSkill));
                    break;
                }
                if (text.StartsWith("You create", System.StringComparison.OrdinalIgnoreCase)) {
                    object[] made = null; var now = System.DateTime.UtcNow;
                    lock (recentAdds) for (int i = recentAdds.Count - 1; i >= 0; i--) { var a = recentAdds[i]; if ((int)a[1] != IngotArt && (now - (System.DateTime)a[2]).TotalMilliseconds < CraftMatchMs) { made = a; break; } }
                    if (made == null || lastBuild == null || (now - lastBuildAt).TotalSeconds >= 60) break;
                    string item = lastBuild;
                    pendingCraft = new object[] { made[0], item, (int)made[1] }; pendingQuality = null;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ => CraftDone(k));
                } else if (pendingCraft != null && (text.IndexOf("quality", System.StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0)) pendingQuality = text;
                else if (lastBuild != null && (System.DateTime.UtcNow - lastBuildAt).TotalSeconds < 60) foreach (var f in CraftFailLines) if (text.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) { Smith.Crafted(0, lastBuild, "failed", SkillOf(k, SmithySkill)); break; }
                break;
            // The sell list (OUO MakePacket_SHOP_SELL): what an NPC offers for each of the player's items, by its quality.
            case 0x9E:
                if (p.Length < 9) break;
                { int n = (p[7] << 8) | p[8], at = 9;
                  // Tailored goods offered against carried, per item, for the vendor's demand.
                  var offered = new System.Collections.Generic.Dictionary<string, int>(); var graphicOf = new System.Collections.Generic.Dictionary<string, int>();
                  for (int i = 0; i < n && at + 14 <= p.Length; i++) {
                      uint s = U32(p, at); int graphic = (p[at + 4] << 8) | p[at + 5], price = (p[at + 10] << 8) | p[at + 11], len = (p[at + 12] << 8) | p[at + 13]; if (at + 14 + len > p.Length) break;
                      string name = System.Text.Encoding.ASCII.GetString(p, at + 14, len).TrimEnd('\0'); string q = Smith.QualityOf(s);
                      if (q != null || Smith.CostOf(name) > 0) Smith.Offer(name, q ?? "unknown", price);
                      foreach (var c in Crafts) if (c.Book != null && c.Book.CostOf(name) > 0) c.Book.Offer(name, "unknown", price);
                      if (Tailor != null) {
                          string tq = Tailor.QualityOf(s);
                          if (tq != null || Tailor.CostOf(name) > 0) { Tailor.Offer(name, tq ?? "unknown", price); int c; offered.TryGetValue(name, out c); offered[name] = c + 1; graphicOf[name] = graphic; }
                      }
                      at += 14 + len;
                  }
                  if (offered.Count > 0 && Atlas != null) {
                      uint vendor = U32(p, 3); string vname = vendor.ToString("X"); foreach (var v in Atlas.Visible()) if (v.Serial == vendor) vname = v.Name;
                      foreach (var kv in offered) {
                          int carried = 0, g = graphicOf[kv.Key];
                          lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (it.Graphic == g && inside.Contains(it.Container)) carried++; }
                          Tailor.Want(vname, kv.Key, kv.Value, System.Math.Max(carried, kv.Value));
                      }
                  } }
                break;
        }
    }
    // After the quality line had its chance: record the build, read the item's own label, and tell the player in the game's
    // text (Damian, 2026-10-15: "if we know we got max quality we should say that. if we know more details at all than just
    // exceptional, make that an output in the chat stream").
    void CraftDone(GameLink k) {
        System.Threading.Thread.Sleep(QualityWaitMs);
        var c = pendingCraft; pendingCraft = null; if (c == null) return;
        uint serial = (uint)c[0]; string item = (string)c[1], line = pendingQuality;
        string q = line == null ? "average" : line.IndexOf("exceptional", System.StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("higher than average", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "exceptional"
            : line.IndexOf("below average", System.StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("barely", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "below average" : "average";
        int skill = SkillOf(k, SmithySkill);
        Smith.Crafted(serial, item, q, skill);
        string label = null; var got = new System.Threading.ManualResetEventSlim(false);
        System.Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) == serial && p[9] == 6) { label = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').Trim(); got.Set(); } };
        k.ServerPacket += h;
        var asked = System.DateTime.UtcNow;
        try { for (int i = 0; i < 10 && k.Look(serial) != null; i++) System.Threading.Thread.Sleep(150); if (got.Wait(LabelWaitMs)) Seen("label-wait", (int)(System.DateTime.UtcNow - asked).TotalMilliseconds); } finally { k.ServerPacket -= h; }
        k.Tell("Made " + item + ": " + q + (q == "exceptional" ? ", the highest quality this server reports" : "") + (label != null && label.Length > 0 ? "; it reads \"" + label + "\"" : "") + " (smithing " + (skill / 10.0).ToString("0.0") + ")", q == "exceptional" ? 0x59 : 0x3B2);
    }

    // ---------- arm and disarm ----------
    // Three toggles (Damian, 2026-10-16: "there should be two buttons, disarm weapon, and disarm both", then "for
    // completeness do all 3 options"): "both" hands, the "weapon" (any held item that is not a shield) or the "shield".
    // Each takes off what it covers and remembers it, and with nothing of its kind in hand puts the remembered set back.
    // Every lift waits out the server's busy time (HarvestGap): uo1998 refused a second-hand lift 829 ms after the first
    // with reason 0xFF (relay log 2026-10-16 16:21:02).
    public static readonly string[] ArmKinds = { "both", "weapon", "shield" };
    public string ArmToggle() { return ArmToggle("both"); }
    public string ArmToggle(string which) {
        var k = link; if (k == null || k.Closed) return "no game connection";
        if (System.Array.IndexOf(ArmKinds, which) < 0) return "arm takes both, weapon or shield";
        lock (actionGate) {
            uint pack; uint me; lock (k.State.Gate) { pack = k.State.Backpack; me = k.State.Player; }
            if (pack == 0) return "the backpack is not known yet";
            string key = Server + "arm.set." + (which == "both" ? "" : which + ".") + me.ToString("X");
            var held = new System.Collections.Generic.List<AssistItem>();
            foreach (int layer in new[] { 1, 2 }) { var h = Hand(k, layer); if (h != null && (which == "both" || (which == "shield") == IsShield(h.Graphic))) held.Add(h); }
            if (held.Count > 0) {
                var set = new System.Text.StringBuilder();
                foreach (var h in held) set.Append(h.Serial.ToString("X") + ":" + h.Layer + ",");
                Cfg.Set(key, set.ToString().TrimEnd(','));
                foreach (var h in held) {
                    HarvestGap();
                    Sequence = "Disarm: lift " + h.Graphic.ToString("X4") + " from layer " + h.Layer;
                    string why = Lifted(k, h.Serial, 1); if (why != null) return Stopped("disarm", h.Serial, why);
                    Sequence = "Disarm: put " + h.Graphic.ToString("X4") + " in the backpack";
                    why = k.Drop(h.Serial, pack); if (why != null) return Stopped("disarm", h.Serial, why);
                    Answered(k, AnswerMs);
                }
                Sequence = "Disarmed"; Note(Sequence); return null;
            }
            string remembered = Cfg.Get(key, "");
            if (remembered.Length == 0) return which == "both" ? "no weapon set is remembered: disarm once to remember it" : "no " + which + " is remembered: disarm it once to remember it";
            foreach (var part in remembered.Split(',')) {
                var f = part.Split(':'); uint s = System.Convert.ToUInt32(f[0], 16); int layer = int.Parse(f[1]);
                if (Item(k, s) == null) return "the remembered item " + s.ToString("X") + " is not in the pack";
                HarvestGap();
                Sequence = "Arm: lift " + s.ToString("X");
                string why = Lifted(k, s, 1); if (why != null) return Stopped("arm", s, why);
                Sequence = "Arm: equip " + s.ToString("X") + " on layer " + layer;
                why = k.Equip(s, layer, me); if (why != null) return Stopped("arm", s, why);
                Answered(k, AnswerMs);
            }
            Sequence = "Armed"; Note(Sequence); return null;
        }
    }
    string Stopped(string what, uint serial, string why) {
        var it = link != null ? Item(link, serial) : null;
        string where = it == null ? "lifted, held by the server" : it.Layer >= 0 ? "in your hand (layer " + it.Layer + ")" : "in container " + it.Container.ToString("X");
        Sequence = what + " stopped: " + why + "; the item " + serial.ToString("X") + " is " + where;
        Note(Sequence); return Sequence;
    }

    // ---------- drinking without hotkeys ----------
    // A double-click on a drinkable potion with no hand free (RunUO BasePotion.HasFreeHand: a weapon on the two-handed
    // layer fills both hands) is held: the weapon goes to the pack, the held double-click goes out, the weapon comes back.
    // ---------- 8x8 resource bank corners ----------
    // Damian, 2026-10-15: "it would be nice to show the 8x8 corners for resource harvesting in the client somehow, like we
    // did we runes for zones in uoaix server". A recall rune (the shard's zone marker art, GmOverlay.codex gmo-art) on the
    // first tile of each 8x8 bank around the player, sent to the client only: serials 0x7FF00000 | bankX << 10 | bankY,
    // above every server item. 16 banks (4 by 4) around the player's own, all within the client's 18-tile view.
    public const int MarkerArt = 0x1F14, MarkerHue = 0x0035; const uint MarkerBase = 0x7FF00000;
    readonly System.Collections.Generic.Dictionary<uint, int[]> markers = new System.Collections.Generic.Dictionary<uint, int[]>();
    public bool ShowBanks { get { return Cfg.Get("banks", "off") == "on"; } set { Cfg.Set("banks", value ? "on" : "off"); } }
    static bool IsMarker(uint serial) { return (serial & 0xFFF00000) == MarkerBase; }
    // In a cave the corner tile's land is usually the mountain above, drawn above the cave roof where the client hides it
    // (Damian, 2026-10-15: "the 8x8 runes don't show in side caves, where they are useful"): a corner whose land is more
    // than CaveSpan from the player's height takes the player's height, and resend redraws every rune when that changes.
    const int CaveSpan = 12;
    public void UpdateBanks(System.Func<int, int, int> landZ, bool resend = false) {
        var k = link; if (k == null || k.Closed) return;
        var want = new System.Collections.Generic.Dictionary<uint, int[]>(); int px, py, pz;
        lock (k.State.Gate) { px = k.State.X; py = k.State.Y; pz = k.State.Z; }
        if (ShowBanks && (px | py) != 0) {
            int bx = px >> 3, by = py >> 3;
            for (int i = -1; i <= 2; i++) for (int j = -1; j <= 2; j++) {
                int cx = (bx + i) * 8, cy = (by + j) * 8; if (cx < 0 || cy < 0 || bx + i >= 1024 || by + j >= 1024) continue;
                want[MarkerBase | (uint)((bx + i) << 10) | (uint)(by + j)] = new[] { cx, cy };
            }
        }
        System.Collections.Generic.List<uint> gone = new System.Collections.Generic.List<uint>(); System.Collections.Generic.List<uint> fresh = new System.Collections.Generic.List<uint>();
        lock (markers) {
            foreach (var s in markers.Keys) if (!want.ContainsKey(s)) gone.Add(s);
            foreach (var s in want.Keys) if (resend || !markers.ContainsKey(s)) fresh.Add(s);
            foreach (var s in gone) markers.Remove(s);
            foreach (var s in fresh) markers[s] = want[s];
        }
        foreach (var s in gone) { var p = new byte[5]; p[0] = 0x1D; Put32(p, 1, s); k.ToClient(p, "bank marker off"); }
        foreach (var s in fresh) {
            int x = want[s][0], y = want[s][1]; int z = landZ(x, y); if (z == int.MinValue || System.Math.Abs(z - pz) > CaveSpan) z = pz;
            // 0x1A with a hue (y | 0x8000): serial, graphic, x, y, z, hue.
            var p = new byte[16]; p[0] = 0x1A; p[1] = 0; p[2] = 16; Put32(p, 3, s); Put16(p, 7, MarkerArt); Put16(p, 9, x); Put16(p, 11, y | 0x8000); p[13] = (byte)(sbyte)z; Put16(p, 14, MarkerHue);
            k.ToClient(p, "bank marker " + x + "," + y);
        }
    }
    // The 1.25 client double-clicks its own player right after login (0x06 with the plain serial: uo1998 17:50:57 and
    // 17:57:40, 0602BE4F79), which dismounts a mounted player (Damian, 2026-10-15: "when i log back in, my character
    // dismounts", "unless im hidden"). Within LoginSelfUseMs of 0x55 that click goes out as the paperdoll request the
    // client sends for its own paperdoll (serial | 0x80000000, 0682BE4F79), which does not dismount.
    const int LoginSelfUseMs = 3000;
    System.DateTime loginAt = System.DateTime.MinValue;
    bool HoldLoginSelfUse(GameLink k, byte[] p) {
        if (p[0] != 0x06 || p.Length != 5 || (System.DateTime.UtcNow - loginAt).TotalMilliseconds > LoginSelfUseMs) return false;
        uint me; lock (k.State.Gate) me = k.State.Player;
        if (me == 0 || U32(p, 1) != me) return false;
        loginAt = System.DateTime.MinValue;
        var q = new byte[5]; q[0] = 0x06; Put32(q, 1, me | 0x80000000);
        string why = k.Release(q, "login paperdoll instead of the self double-click", false);
        Note("Login: the client's double-click on you went out as a paperdoll request" + (why != null ? " (not sent: " + why + ")" : ""));
        return true;
    }
    // ---------- the smithing ladder (Damian, 2026-10-18: "allow both a user config and your best guess. the server might
    // have different code") ----------
    // One item per skill band, never alternated ("there was never a reason to bounce around different items"). The
    // player's ladder is smith.ladder ("item@skill;..."), defaulting to his own steps: ringmail gloves, plate gorget, plate
    // gloves from 65, platemail arms from 95.0, platemail legs from 99.5 (the gorget's start is a guess). The best guess
    // is the 1998 server's: a check of range 50 (blacksmith.m:433) succeeds at (skill - difficulty) * 2 + 500 thousandths
    // and gains in proportion to chance * (1000 - chance) (OUO skill.c:746-909), here per ingot. The difficulties are
    // derived from itemres.mul and the script's rescale (blacksmith.m:121-232), not quoted. smith.ladder.mode: mine|guess.
    public const string DefaultLadder = "ringmail gloves@0;plate gorget@50;plate gloves@65;platemail arms@95;platemail legs@99.5";
    static readonly string[][] SmithDifficulty = {
        new[] { "ringmail gloves", "10", "426" }, new[] { "ringmail tunic", "18", "557" }, new[] { "chainmail tunic", "20", "688" },
        new[] { "plate gorget", "10", "754" }, new[] { "plate gloves", "12", "786" }, new[] { "plate helm", "15", "836" },
        new[] { "platemail arms", "18", "885" }, new[] { "platemail legs", "20", "918" }, new[] { "platemail tunic", "25", "1000" },
    };
    public string SmithLadder { get { return Cfg.Get(Server + "smith.ladder", DefaultLadder); } set { Cfg.Set(Server + "smith.ladder", value); } }
    public string SmithLadderMode { get { return Cfg.Get(Server + "smith.ladder.mode", "mine"); } set { Cfg.Set(Server + "smith.ladder.mode", value == "guess" ? "guess" : "mine"); } }
    // The item to smith at skill (tenths): the last ladder step at or under it, or the best guess's.
    public string SmithNow(int skill) {
        if (SmithLadderMode == "guess") {
            string best = null; double bestG = -1;
            foreach (var r in SmithDifficulty) {
                int ingots = int.Parse(r[1]), diff = int.Parse(r[2]); double c = System.Math.Max(0, System.Math.Min(1000, (skill - diff) * 2 + 500));
                double g = c * (1000 - c) / ingots; if (diff <= skill + 250 && g > bestG) { bestG = g; best = r[0]; }
            }
            return best;
        }
        string pick = null;
        foreach (var step in SmithLadder.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) {
            int at = step.LastIndexOf('@'); double from; if (at <= 0 || !double.TryParse(step.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out from)) continue;
            if (skill >= (int)System.Math.Round(from * 10)) pick = step.Substring(0, at).Trim();
        }
        return pick;
    }
    // ---------- record me (Damian, 2026-10-18: "i click it, it sends a message to the proxy/log and not to the server.
    // then i can either direct you to it, or if you are live monitoring I can trigger you from in client to start
    // watching ... then I click to stop it") ----------
    // A recording is the relay log between "Record: start" and "Record: stop" lines, toggled by the window's action (F8),
    // the control port's record, or "[rec" and "[stop" said in the client; while it runs, a line said beginning "[" is a
    // "Record note:" in the log. None of them reaches the server.
    public volatile bool Recording; System.DateTime recordAt;
    public string RecordToggle(string by) {
        var k = link; Recording = !Recording;
        string line = Recording ? "Record: start (" + by + ")" : "Record: stop (" + by + ", " + (int)(System.DateTime.UtcNow - recordAt).TotalSeconds + " s)";
        if (Recording) { recordAt = System.DateTime.UtcNow; string p = log.Record(true); line += "; copied to " + System.IO.Path.GetFileName(p); }
        Note(line); if (!Recording) log.Record(false);
        if (k != null && !k.Closed) k.Tell(Recording ? "Recording: say [ and a note; [stop or F8 ends it" : "Recording stopped");
        return line;
    }
    bool HoldRecord(GameLink k, byte[] p) {
        if (p.Length < 9 || p[0] != 0x03) return false;
        int end = System.Array.IndexOf(p, (byte)0, 8); string text = System.Text.Encoding.ASCII.GetString(p, 8, (end < 0 ? p.Length : end) - 8).Trim();
        if (text.StartsWith("[rec", System.StringComparison.OrdinalIgnoreCase)) { if (!Recording) RecordToggle("the client"); return true; }
        if (text.StartsWith("[stop", System.StringComparison.OrdinalIgnoreCase)) { if (Recording) RecordToggle("the client"); return true; }
        if (Recording && text.StartsWith("[")) { Note("Record note: " + text.Substring(1).Trim()); return true; }
        return false;
    }
    // A click, double-click or lift of a marker never reaches the server; a lift is refused to the client (0x27).
    bool HoldMarker(GameLink k, byte[] p) {
        if (p.Length < 5 || (p[0] != 0x06 && p[0] != 0x09 && p[0] != 0x07)) return false;
        uint s = U32(p, 1); if (!IsMarker(s)) return false;
        if (p[0] == 0x07) k.ToClient(new byte[] { 0x27, 0 }, "marker lift refused");
        // A single click names the bank (Damian, 2026-10-15: "make them click name for the map location and which corner
        // its in"): the rune stands on the bank's north-west tile, its first.
        if (p[0] == 0x09) {
            int bx = (int)((s >> 10) & 0x3FF), by = (int)(s & 0x3FF), x = bx * 8, y = by * 8;
            Label(k, s, "8x8 bank " + bx + "," + by + ": north-west corner " + x + "," + y + " of tiles " + x + "-" + (x + 7) + ", " + y + "-" + (y + 7));
        }
        return true;
    }
    // A label over an item (0x1C type 6, the item's serial and graphic), for the client only.
    void Label(GameLink k, uint serial, string text) {
        var t = System.Text.Encoding.ASCII.GetBytes(text.Length > 200 ? text.Substring(0, 200) : text);
        var p = new byte[45 + t.Length]; p[0] = 0x1C; Put16(p, 1, p.Length); Put32(p, 3, serial); Put16(p, 7, MarkerArt);
        p[9] = 6; Put16(p, 10, MarkerHue); Put16(p, 12, 3); t.CopyTo(p, 44);
        k.ToClient(p, "marker label");
    }
    static void Put32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
    static void Put16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }

    // The bag pricer (Damian, 2026-10-15: "you target a bag, then it counts everything in it, and gives them all a default
    // price to sell at on player vendor"): a target cursor of the assist's own, shown to the client only; the client's
    // answer is held from the server. The picked bag and every container inside it is opened (double-clicked) once, so
    // the server sends their contents; PricerSeq moves when the count is ready.
    public const uint PricerCursor = 0x50524943;  // "PRIC": no server cursor id this client has seen is near it
    public const int PricerBagsMax = 24;
    int PricerOpenMs { get { return Ms("pricer-open"); } }
    public volatile uint PricerBag; public long PricerSeq; public volatile string PricerStatus = "Target a bag to price its contents.";
    public string PricerTarget() {
        var k = link; if (k == null || k.Closed) return "not connected";
        var p = new byte[19]; p[0] = 0x6C; p[1] = 0; Put32(p, 2, PricerCursor);
        string why = k.ToClient(p, "pricer cursor"); PricerStatus = why == null ? "Pick the bag to price." : "No cursor: " + why; return why;
    }
    bool HoldPricer(GameLink k, byte[] p) {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != PricerCursor) return false;
        uint s = U32(p, 7);
        if (s == 0 || p[1] != 0) { PricerStatus = "Pricing cancelled: pick a bag, not the ground."; return true; }
        PricerBag = s; PricerStatus = "Opening the bag...";
        var t = new System.Threading.Thread(() => PricerOpen(k, s)); t.IsBackground = true; t.Start();
        return true;
    }
    public static bool IsContainer(int graphic) { var l = UoTiles.Items(); return graphic >= 0 && graphic < l.Count && (l[graphic].Flags & 0x00200000) != 0; }
    void PricerOpen(GameLink k, uint bag) {
        var opened = new System.Collections.Generic.HashSet<uint>(); var queue = new System.Collections.Generic.Queue<uint>(); queue.Enqueue(bag);
        while (queue.Count > 0 && opened.Count < PricerBagsMax && !k.Closed) {
            uint b = queue.Dequeue(); if (!opened.Add(b)) continue;
            string why = "busy";
            for (int tries = 0; tries < 10 && why != null; tries++) { lock (actionGate) why = k.Use(b, false); if (why != null) System.Threading.Thread.Sleep(300); }
            if (why != null) { PricerStatus = "Could not open " + b.ToString("X") + ": " + why; break; }
            System.Threading.Thread.Sleep(PricerOpenMs);
            lock (k.State.Gate) foreach (var kv in k.State.Items) if (kv.Value.Container == b && IsContainer(kv.Value.Graphic) && !opened.Contains(kv.Key)) queue.Enqueue(kv.Key);
            PricerStatus = "Opened " + opened.Count + " bag" + (opened.Count == 1 ? "" : "s") + "...";
            System.Threading.Interlocked.Increment(ref PricerSeq);
        }
        PricerStatus = "Counted " + bag.ToString("X") + " (" + opened.Count + " bag" + (opened.Count == 1 ? "" : "s") + " opened).";
        System.Threading.Interlocked.Increment(ref PricerSeq);
    }
    // ---------- reagent loadout ----------
    // Damian, 2026-10-16: "its the reagent bag loadout tool. it takes two targets, first one is loadout bag, second is
    // storage. take all reagents out of loadout and merge with the storage bag. then pull back a configurable amount of
    // each. start with 15 of each, plus 5 extra of recall regs". A reagent is dropped on the storage stack of its kind,
    // which merges it, or into the bag when storage holds none; each pull back is a part lift off the storage stacks.
    public const uint LoadoutCursor = 0x52454147;  // "REAG", apart from PricerCursor
    public const int LoadoutBase = 15, LoadoutRecallExtra = 5;
    int LoadoutCursorGapMs { get { return Ms("loadout-cursor-gap"); } }
    static readonly int[] RecallReagents = { 0xF7A, 0xF7B, 0xF86 };  // black pearl, bloodmoss, mandrake root
    public static int[] LoadoutReagents() { var l = new System.Collections.Generic.List<int>(ReagentNames.Keys); l.Sort(); return l.ToArray(); }
    public int LoadoutWant(int g) { return Cfg.GetInt("loadout." + g.ToString("X"), LoadoutBase + (System.Array.IndexOf(RecallReagents, g) >= 0 ? LoadoutRecallExtra : 0)); }
    public void SetLoadoutWant(int g, int n) { Cfg.Set("loadout." + g.ToString("X"), System.Math.Max(0, n).ToString()); }
    public volatile string LoadoutStatus = "Target the loadout bag, then the storage bag.";
    volatile int loadoutStep; volatile uint loadoutBag;
    public string LoadoutTarget() {
        var k = link; if (k == null || k.Closed) return "not connected";
        loadoutStep = 1; return LoadoutCursorOut(k, "Pick the loadout bag.");
    }
    string LoadoutCursorOut(GameLink k, string ask) {
        var p = new byte[19]; p[0] = 0x6C; p[1] = 0; Put32(p, 2, LoadoutCursor);
        string why = k.ToClient(p, "loadout cursor"); LoadoutStatus = why == null ? ask : "No cursor: " + why; if (why != null) loadoutStep = 0; return why;
    }
    bool HoldLoadout(GameLink k, byte[] p) {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != LoadoutCursor) return false;
        uint s = U32(p, 7);
        if (s == 0 || p[1] != 0) { loadoutStep = 0; LoadoutStatus = "Loadout cancelled: pick a bag, not the ground."; return true; }
        if (loadoutStep == 1) {
            loadoutBag = s; loadoutStep = 2;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { System.Threading.Thread.Sleep(LoadoutCursorGapMs); LoadoutCursorOut(k, "Now pick the storage bag."); });
            return true;
        }
        loadoutStep = 0; uint bag = loadoutBag;
        if (s == bag) { LoadoutStatus = "Loadout cancelled: the storage bag is the loadout bag."; return true; }
        // Kept for the refresh circuit, which refills the loadout at the bank with no cursor.
        Cfg.Set("loadout.bag", bag.ToString("X")); Cfg.Set("loadout.storage", s.ToString("X"));
        var t = new System.Threading.Thread(() => LoadoutRun(k, bag, s)); t.IsBackground = true; t.Start();
        return true;
    }
    System.Collections.Generic.List<AssistItem> Inside(GameLink k, uint bag, int graphic) {
        var l = new System.Collections.Generic.List<AssistItem>();
        lock (k.State.Gate) foreach (var it in k.State.Items.Values) if (it.Container == bag && (graphic < 0 ? ReagentName(it.Graphic) != null : it.Graphic == graphic)) l.Add(it);
        return l;
    }
    void LoadoutRun(GameLink k, uint bag, uint storage) {
        lock (actionGate) {
            foreach (var b in new[] { bag, storage }) {
                HarvestGap(); string why = k.Use(b, false); if (why != null) { LoadoutStatus = "Could not open " + b.ToString("X") + ": " + why; return; }
                System.Threading.Thread.Sleep(PricerOpenMs);
            }
            int stored = 0;
            foreach (var it in Inside(k, bag, -1)) {
                LoadoutStatus = "Storing " + ReagentName(it.Graphic) + "...";
                var stack = Inside(k, storage, it.Graphic);
                HarvestGap(); string why = Lifted(k, it.Serial, it.Amount); if (why != null) { LoadoutStatus = Stopped("loadout", it.Serial, why); return; }
                why = k.Drop(it.Serial, stack.Count > 0 ? stack[0].Serial : storage); if (why != null) { LoadoutStatus = Stopped("loadout", it.Serial, why); return; }
                Answered(k, AnswerMs); stored += it.Amount;
            }
            System.Threading.Thread.Sleep(PricerOpenMs);
            var got = new System.Collections.Generic.List<string>(); var short_ = new System.Collections.Generic.List<string>();
            foreach (int g in LoadoutReagents()) {
                int want = LoadoutWant(g), have = 0; if (want <= 0) continue;
                foreach (var st in Inside(k, storage, g)) {
                    if (have >= want) break;
                    int take = System.Math.Min(want - have, st.Amount);
                    LoadoutStatus = "Taking " + take + " " + ReagentName(g) + "...";
                    HarvestGap(); string why = Lifted(k, st.Serial, take); if (why != null) { LoadoutStatus = Stopped("loadout", st.Serial, why); return; }
                    why = k.Drop(st.Serial, bag); if (why != null) { LoadoutStatus = Stopped("loadout", st.Serial, why); return; }
                    Answered(k, AnswerMs); have += take;
                }
                got.Add(have + " " + ReagentName(g)); if (have < want) short_.Add(ReagentName(g) + " (" + have + " of " + want + ")");
            }
            LoadoutStatus = "Stored " + stored + "; loaded " + string.Join(", ", got) + (short_.Count > 0 ? "; short of " + string.Join(", ", short_) : "") + ".";
            Note("Loadout: " + LoadoutStatus);
        }
    }
    // Every item inside the bag, at any depth, less the containers themselves.
    public System.Collections.Generic.List<AssistItem> PricerItems() {
        var list = new System.Collections.Generic.List<AssistItem>(); var k = link; uint bag = PricerBag; if (k == null || bag == 0) return list;
        lock (k.State.Gate) {
            var inside = new System.Collections.Generic.HashSet<uint> { bag };
            for (int pass = 0; pass < 8; pass++) foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container)) inside.Add(kv.Key);
            foreach (var kv in k.State.Items) if (kv.Key != bag && inside.Contains(kv.Value.Container) && !IsContainer(kv.Value.Graphic)) list.Add(kv.Value);
        }
        return list;
    }
    bool HoldPotion(GameLink k, byte[] p) {
        if (p[0] != 0x06 || p.Length != 5) return false;
        if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null) return false;
        var it = Item(k, U32(p, 1)); if (it == null || System.Array.IndexOf(Drinkable, it.Graphic) < 0) return false;
        var one = Hand(k, 1); var two = Hand(k, 2);
        bool twoWeapon = two != null && !IsShield(two.Graphic);
        if (!(twoWeapon || (one != null && two != null))) return false;
        var weapon = twoWeapon ? two : one;
        var t = new System.Threading.Thread(() => Drink(k, p, weapon)); t.IsBackground = true; t.Start();
        return true;
    }
    void Drink(GameLink k, byte[] held, AssistItem weapon) {
        lock (actionGate) {
            uint pack, me; lock (k.State.Gate) { pack = k.State.Backpack; me = k.State.Player; }
            Answered(k, AnswerMs);
            Sequence = "Drink: lift " + weapon.Graphic.ToString("X4") + " from your hand";
            string why = Lifted(k, weapon.Serial, 1); if (why != null) { Stopped("drink", weapon.Serial, why); return; }
            Sequence = "Drink: put " + weapon.Graphic.ToString("X4") + " in the backpack";
            why = k.Drop(weapon.Serial, pack); if (why != null) { Stopped("drink", weapon.Serial, why); return; }
            Answered(k, AnswerMs);
            Sequence = "Drink: drink";
            why = k.Release(held, "drink " + U32(held, 1).ToString("X")); if (why != null) { Stopped("drink", weapon.Serial, why); return; }
            Answered(k, AnswerMs);
            System.Threading.Thread.Sleep(LiftSettleMs);
            Sequence = "Drink: lift " + weapon.Graphic.ToString("X4") + " back";
            why = Lifted(k, weapon.Serial, 1); if (why != null) { Stopped("drink", weapon.Serial, why); return; }
            Sequence = "Drink: equip " + weapon.Graphic.ToString("X4") + " on layer " + weapon.Layer;
            why = k.Equip(weapon.Serial, weapon.Layer, me); if (why != null) { Stopped("drink", weapon.Serial, why); return; }
            Answered(k, AnswerMs);
            Sequence = "Drank, rearmed"; Note(Sequence);
        }
    }

    // ---------- reagent autopickup ----------
    public bool Pickup { get { return Cfg.Get("pickup", "off") == "on"; } set { Cfg.Set("pickup", value ? "on" : "off"); } }
    public int Reach { get { return System.Math.Max(1, Cfg.GetInt(Server + "pickup.reach", LiftReach)); } }
    public int[] Reagents() {
        var l = new System.Collections.Generic.List<int>();
        foreach (var h in Cfg.Get("reagents", DefaultReagents).Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)) l.Add(System.Convert.ToInt32(h, 16));
        return l.ToArray();
    }
    public void SetReagents(int[] g) { var parts = new string[g.Length]; for (int i = 0; i < g.Length; i++) parts[i] = g[i].ToString("X"); Cfg.Set("reagents", string.Join(",", parts)); }

    bool overweightNoted;
    // A step answered within PickupWalkingMs is walking: a mounted run steps every 100 to 200 ms, a walk every 400.
    public const int PickupWalkingMs = 600;
    static bool Moving(GameLink k) { lock (k.State.Gate) return (System.DateTime.UtcNow - k.State.StepAt).TotalMilliseconds < PickupWalkingMs; }
    // Items the server refused as out of range, and the player's distance from each then (PickupLoop's thread only).
    readonly System.Collections.Generic.Dictionary<uint, int> outOfRange = new System.Collections.Generic.Dictionary<uint, int>(), busyTries = new System.Collections.Generic.Dictionary<uint, int>();
    // The adaptive pace (Damian, 2026-10-17: "tries to fire all its messages with slightly less delay till it notices a
    // problem, then backs off"): the gap from the last pickup's drop to the next lift (from busy) and the lift's settle
    // (from lift-settle) each come down PickupStepMs after every clean pickup, never under its floor. A busy refusal
    // raises the gap, a refusal after the lift was judged accepted raises the settle (and the gap too when it is busy), by
    // PickupBackoffMs, and the refused value plus a step becomes that one's floor; PickupProbeAfter clean pickups at a
    // floor lower it a step, so a server that has grown quicker is found again. Nothing comes down for PickupHoldMs after
    // any pickup refusal, so the probing costs at most one of the guard's 3 lift refusals in 60 s (it tripped at 06:32:42
    // with the gap probing on). Kept per server (pickup.gap, pickup.settle and their .floor).
    public const int PickupStepMs = 25, PickupBackoffMs = 150, PickupProbeAfter = 100, PickupSaveEvery = 20, PickupSettleLeastMs = 50, PickupHoldMs = 60000;
    System.DateTime pickupRefusedAt = System.DateTime.MinValue;
    sealed class PickupPace { public string Key; public int Ms, Floor, Least, AtFloor; }
    PickupPace pickupGap, pickupSettle;
    public const int PickupTickMs = 25;
    int pickupClean;
    PickupPace PaceOf(string key, int start, int least) {
        return new PickupPace { Key = Server + "pickup." + key, Least = least, Ms = System.Math.Max(least, Cfg.GetInt(Server + "pickup." + key, start)), Floor = System.Math.Max(least, Cfg.GetInt(Server + "pickup." + key + ".floor", least)) };
    }
    void PaceSave(PickupPace p) { Cfg.Set(p.Key, p.Ms.ToString()); Cfg.Set(p.Key + ".floor", p.Floor.ToString()); }
    void PaceDown(PickupPace p) {
        if (p.Ms - PickupStepMs >= p.Floor) { p.Ms -= PickupStepMs; p.AtFloor = 0; return; }
        if (++p.AtFloor >= PickupProbeAfter && p.Floor - PickupStepMs >= p.Least) { p.Floor -= PickupStepMs; p.AtFloor = 0; }
    }
    void PaceBack(PickupPace p, string why) {
        int was = p.Ms; p.Floor = was + PickupStepMs; p.Ms = was + PickupBackoffMs; p.AtFloor = 0; PaceSave(p);
        Note("Pickup: " + why + " at " + (p == pickupGap ? "gap" : "settle") + " " + was + " ms; now " + p.Ms + " ms, floor " + p.Floor + " ms");
    }
    public string PickupPaceText { get { var g = pickupGap; var s = pickupSettle; return g == null ? "pace not started" : "gap " + g.Ms + " ms (floor " + g.Floor + "), settle " + s.Ms + " ms (floor " + s.Floor + ")"; } }
    void PickupLoop() {
        while (true) {
            // The ground is looked at every PickupTickMs (Damian, 2026-10-17: "down to 25ms chunks").
            System.Threading.Thread.Sleep(PickupTickMs);
            var k = link; if (k == null || k.Closed || k.Halted != null || !Pickup || k.State.Dead) continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null) continue;
            if (pickupGap == null) { pickupGap = PaceOf("lift-gap", BusyMs, 0); pickupSettle = PaceOf("settle", LiftSettleMs, PickupSettleLeastMs); }
            // A lift leaves the player busy, refused or not: "You must wait to perform another action." (uo1998 2026-10-17
            // 14:29:51, 06:37:26). The pace's gap from the last lift sent, or the full BusyMs after any other action since (a
            // harvest's pluck), is waited out BEFORE the item is chosen, so the first one met on a walk goes at once and the
            // ones after keep the gap, each chosen where the player is then (Damian, 2026-10-17: "when walking the first one
            // encountered needs to fire sooner, and the ones after need the delay to prevent the failsafe").
            bool paced = harvestActAt <= liftSentAt;
            if ((paced ? pickupGap.Ms - (int)(System.DateTime.UtcNow - liftSentAt).TotalMilliseconds : BusyMs - (int)(System.DateTime.UtcNow - harvestActAt).TotalMilliseconds) > 0) continue;
            int reach = Reach; var reagents = Reagents(); AssistItem best = null; int bestD = int.MaxValue; uint pack; int fromX, fromY;
            // Moving, only what is beside the player: a lift at the full reach went out between steps and the server, a step
            // on, refused it out of range (Damian, 2026-10-17: "its going off too soon"; 05:17:08 and 05:23:38).
            if (Moving(k)) reach = System.Math.Min(reach, 1);
            lock (k.State.Gate) {
                pack = k.State.Backpack; fromX = k.State.X; fromY = k.State.Y;
                foreach (var kv in k.State.Ground) {
                    var g = kv.Value; if (leave.Contains(g.Serial) || System.Array.IndexOf(reagents, g.Graphic) < 0) continue;
                    int d = System.Math.Max(System.Math.Abs(g.X - k.State.X), System.Math.Abs(g.Y - k.State.Y)), was;
                    if (outOfRange.TryGetValue(g.Serial, out was) && d >= was) continue;
                    if (d <= reach && d < bestD) { best = g; bestD = d; }
                }
            }
            if (best == null || pack == 0) continue;
            // The 1998 carry limit is strength * 4 + 30 (OUO mobile.c CMobile_GetMaxWeight); past it every step costs ten
            // times the load in stamina instead of a tenth (CMobile_HandleStaminaDrain), so pickup stops short of it.
            // OUO item.c:4165-4169 weighs a weightless stack at (amount + 9) / 10 or (amount + 49) / 50; the larger is used.
            int str, weight; lock (k.State.Gate) { str = k.State.Str; weight = k.State.Weight; }
            if (str > 0 && weight + System.Math.Max(1, (best.Amount + 9) / 10) > str * 4 + 30) {
                if (!overweightNoted) { Note("Pickup: paused, the pack is at " + weight + " of " + (str * 4 + 30) + " stones"); overweightNoted = true; }
                continue;
            }
            overweightNoted = false;
            if (!System.Threading.Monitor.TryEnter(actionGate)) continue;
            try {
                Answered(k, AnswerMs);
                string why = Lifted(k, best.Serial, best.Amount, pickupSettle.Ms);
                if (why != null) {
                    pickupRefusedAt = System.DateTime.UtcNow;
                    // Busy (0xFF) asks for a repeat: lifted again after the busy time, BusyRetries times at most.
                    if (lastRefusal != null && lastRefusal[1] == 0xFF) {
                        if (paced) PaceBack(pickupGap, "busy");
                        int tries; if (busyTries.Count > 2000) busyTries.Clear(); busyTries.TryGetValue(best.Serial, out tries); busyTries[best.Serial] = ++tries;
                        if (tries <= BusyRetries) { Note("Pickup: busy; " + best.Serial.ToString("X") + " is lifted again after the busy time"); continue; }
                    }
                    // Out of range or out of sight is an answer about where the player stood: the item is lifted again only from nearer
                    // than that (a pearl refused mid-walk at 14:14:17 was left alone with Kwik standing on it). Any other
                    // refusal leaves it for good.
                    if (lastRefusal != null && (lastRefusal[1] == 1 || lastRefusal[1] == 2)) { int nowD; lock (k.State.Gate) nowD = System.Math.Max(System.Math.Abs(best.X - k.State.X), System.Math.Abs(best.Y - k.State.Y)); if (outOfRange.Count > 2000) outOfRange.Clear(); outOfRange[best.Serial] = System.Math.Max(nowD, bestD); }
                    else leave.Add(best.Serial);
                    // LRReason.OutOfRange is 1 (RunUO Server/Mobile.cs): this server's reach is shorter than that, but only
                    // when the player stood still; a player walking away was out of range by the step (uo1998 2026-10-17
                    // 14:10:08, refused at 1 tile mid-walk, reach cut to 0 and every ginseng beside him left). Never
                    // under 1 tile.
                    bool still; lock (k.State.Gate) still = k.State.X == fromX && k.State.Y == fromY;
                    if (lastRefusal != null && lastRefusal[1] == 1 && still && bestD > 1) { Cfg.Set(Server + "pickup.reach", (bestD - 1).ToString()); Note("Pickup: the server refused at " + bestD + " tiles; reach is now " + (bestD - 1)); }
                    else Note("Pickup: " + why + "; left " + best.Serial.ToString("X") + " alone");
                    continue;
                }
                long judged = System.Threading.Interlocked.Read(ref refusals);
                why = k.Drop(best.Serial, pack); if (why != null) { Stopped("pickup", best.Serial, why); leave.Add(best.Serial); continue; }
                Answered(k, AnswerMs);
                // A refusal after the settle: the lift was judged accepted too soon, and a busy one came of too short a gap
                // as well. Busy asks for a repeat; any other reason leaves the item.
                if (System.Threading.Interlocked.Read(ref refusals) != judged) {
                    pickupRefusedAt = System.DateTime.UtcNow;
                    var r = lastRefusal; PaceBack(pickupSettle, "a refusal after the settle (reason " + (r != null ? r[1].ToString("X") : "?") + ")");
                    if (r != null && r[1] == 0xFF) { if (paced) PaceBack(pickupGap, "busy, seen after the settle"); }
                    else leave.Add(best.Serial);
                    continue;
                }
                if ((System.DateTime.UtcNow - pickupRefusedAt).TotalMilliseconds > PickupHoldMs) { PaceDown(pickupGap); PaceDown(pickupSettle); }
                if (++pickupClean % PickupSaveEvery == 0) { PaceSave(pickupGap); PaceSave(pickupSettle); }
                Note("Pickup: " + best.Amount + " of " + best.Graphic.ToString("X4") + " into the backpack");
                lock (picked) { int n; picked.TryGetValue(best.Graphic, out n); picked[best.Graphic] = n + best.Amount; }
            } finally { System.Threading.Monitor.Exit(actionGate); }
        }
    }

    // ---------- harvest ----------
    // Damian, 2026-10-15: "a food harvest item pickup. it will like when running past carrots, it needs to pluck then
    // pickup", "and sheep for wool", "wheat and cotton need to harvest, as weell cabbage which requires no digging, just
    // pluck it". A crop is plucked by a double-click on the plant, and its product is lifted into the pack: the carrots
    // plant turns into the carrot itself (uo1998 21:59:17: 44B6C930, 0x0C76, became 0x0C78), wheat and cotton drop a new
    // item on the plant's tile (uo1998 01:40:30: wheat 0x0C58 dropped a sheaf 0x1EBD and became the stalk 0x0DAE; 01:40:42:
    // cotton 0x0C54 dropped a bale 0x0DF9 and was removed). A head of cabbage lies loose and is only lifted: a double-click
    // eats it (01:38:53). The plants are the 1.25 TILEDATA's, a fixed list: a learned one took iron ore and ribs. A sheep
    // (body 0xCF; 0xDF once shorn) is shorn with a blade from the pack targeted on it, and its wool (0x0DF8) is picked up;
    // the shearing is 1998 practice, not yet seen on uo1998's wire.
    public bool Harvest { get { return Cfg.Get("harvest", "off") == "on"; } set { Cfg.Set("harvest", value ? "on" : "off"); } }
    static readonly int[] CropPlants = { 0x0C76, 0x0C4F, 0x0C50, 0x0C51, 0x0C52, 0x0C53, 0x0C54, 0x0C55, 0x0C56, 0x0C57, 0x0C58, 0x0C59, 0x0C5A, 0x0C5B };
    static readonly int[] LooseCrops = { 0x0C7B, 0x0C7C };
    public const int SheepBody = 0xCF, WoolArt = 0x0DF8, ShornMs = 600000;
    int PluckMs { get { return Ms("pluck"); } }
    // A double-click or a lift leaves the player busy for 4 ticks of 250 ms (OUO packet_handler.c DispatchDoubleClick
    // and REQ_GETOBJ set actionState 4; player.c RegenTick counts it down). A lift while busy is refused with reason 0xFF
    // and a double-click is dropped unanswered (uo1998 01:29:26: a carrot lifted 250 ms after its pluck, "You must wait
    // to perform another action."), so each harvest action waits BusyMs, one tick of margin, after the last.
    int BusyMs { get { return Ms("busy"); } }
    System.DateTime harvestActAt;
    void HarvestGap() {
        int wait = BusyMs - (int)(System.DateTime.UtcNow - (liftSentAt > harvestActAt ? liftSentAt : harvestActAt)).TotalMilliseconds;
        if (wait > 0) System.Threading.Thread.Sleep(wait);
        harvestActAt = System.DateTime.UtcNow;
    }
    // Dagger, butcher knife, cleaver, skinning knife (1.25 TILEDATA).
    static readonly int[] Blades = { 0x0F51, 0x0F52, 0x13F6, 0x13F7, 0x0EC2, 0x0EC3, 0x0EC4, 0x0EC5 };
    public int[] Crops() { var l = new System.Collections.Generic.List<int>(CropPlants); l.AddRange(LooseCrops); return l.ToArray(); }
    // What a pluck gave: a new item on the plant's tile (wheat, cotton), else the plant itself turned into the food
    // (carrots), else null.
    AssistItem Plucked(GameLink k, AssistItem plant, System.Collections.Generic.HashSet<uint> before) {
        var start = System.DateTime.UtcNow; var until = start.AddMilliseconds(PluckMs); AssistItem turned = null; System.DateTime turnedAt = System.DateTime.MaxValue;
        while (System.DateTime.UtcNow < until) {
            AssistItem fresh = null;
            lock (k.State.Gate) {
                foreach (var kv in k.State.Ground) if (kv.Value.X == plant.X && kv.Value.Y == plant.Y && !before.Contains(kv.Key)) { fresh = kv.Value; break; }
                AssistItem now; if (fresh == null && turned == null && k.State.Ground.TryGetValue(plant.Serial, out now) && now.Graphic != plant.Graphic) { turned = now; turnedAt = System.DateTime.UtcNow; }
            }
            if (fresh != null) { Seen("pluck", (int)(System.DateTime.UtcNow - start).TotalMilliseconds); return fresh; }
            // A new item can follow the plant's own change on the wire: wait a little for one before taking the plant.
            if (turned != null && (System.DateTime.UtcNow - turnedAt).TotalMilliseconds >= 300) { Seen("pluck", (int)(turnedAt - start).TotalMilliseconds); return turned; }
            System.Threading.Thread.Sleep(50);
        }
        return turned;
    }
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> shorn = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    void HarvestLoop() {
        while (true) {
            System.Threading.Thread.Sleep(300);
            var k = link; if (k == null || k.Closed || k.Halted != null || !Harvest || k.State.Dead) continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null) continue;
            int reach = Reach; AssistItem plant = null, wool = null; uint pack, blade = 0; int px, py, plantD = int.MaxValue, str, weight;
            lock (k.State.Gate) {
                pack = k.State.Backpack; px = k.State.X; py = k.State.Y; str = k.State.Str; weight = k.State.Weight;
                foreach (var kv in k.State.Ground) {
                    var g = kv.Value; if (leave.Contains(g.Serial)) continue;
                    int d = System.Math.Max(System.Math.Abs(g.X - px), System.Math.Abs(g.Y - py)); if (d > reach) continue;
                    if (g.Graphic == WoolArt || System.Array.IndexOf(LooseCrops, g.Graphic) >= 0) wool = g;
                    else if (System.Array.IndexOf(CropPlants, g.Graphic) >= 0 && d < plantD) { plant = g; plantD = d; }
                }
                foreach (var kv in k.State.Items) if (System.Array.IndexOf(Blades, kv.Value.Graphic) >= 0 && (kv.Value.Container == pack || kv.Value.Mobile == k.State.Player)) { blade = kv.Key; break; }
            }
            if (pack == 0 || (str > 0 && weight + 1 > str * 4 + 30)) continue;
            AtlasNpc sheep = null;
            if (plant == null && wool == null && blade != 0 && Atlas != null) {
                var now = System.DateTime.Now;
                foreach (var n in Atlas.Visible()) {
                    if (n.Body != SheepBody || (now - n.Last).TotalMilliseconds > 3000 || System.Math.Max(System.Math.Abs(n.X - px), System.Math.Abs(n.Y - py)) > reach) continue;
                    System.DateTime at; lock (shorn) { if (shorn.TryGetValue(n.Serial, out at) && (System.DateTime.UtcNow - at).TotalMilliseconds < ShornMs) continue; }
                    sheep = n; break;
                }
            }
            if (plant == null && wool == null && sheep == null) continue;
            if (!System.Threading.Monitor.TryEnter(actionGate)) continue;
            try {
                Answered(k, AnswerMs);
                if (wool != null) { Gather(k, wool, pack, UoTiles.Name(wool.Graphic)); continue; }
                if (plant != null) {
                    var before = new System.Collections.Generic.HashSet<uint>();
                    lock (k.State.Gate) foreach (var kv in k.State.Ground) if (kv.Value.X == plant.X && kv.Value.Y == plant.Y) before.Add(kv.Key);
                    HarvestGap(); string why = k.Use(plant.Serial, false);
                    if (why != null) { leave.Add(plant.Serial); continue; }
                    var got = Plucked(k, plant, before);
                    k.Settle(); leave.Add(plant.Serial);
                    if (got == null) { Note("Harvest: " + UoTiles.Name(plant.Graphic) + " " + plant.Serial.ToString("X") + " gave nothing; left alone"); continue; }
                    Gather(k, got, pack, UoTiles.Name(got.Graphic));
                    continue;
                }
                lock (shorn) { if (shorn.Count > 2000) shorn.Clear(); shorn[sheep.Serial] = System.DateTime.UtcNow; }
                HarvestGap(); string w = k.Use(blade, true); if (w != null) { Note("Harvest: the blade " + blade.ToString("X") + " did not open: " + w); continue; }
                var cursorBy = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
                while (!k.MacroCursor && System.DateTime.UtcNow < cursorBy) System.Threading.Thread.Sleep(20);
                if (!k.MacroCursor) { Note("Harvest: the blade opened no target cursor; sheep " + sheep.Serial.ToString("X") + " left"); continue; }
                w = k.Target(false, sheep.Serial, sheep.X, sheep.Y, 0, SheepBody);
                Note(w == null ? "Harvest: shearing sheep " + sheep.Serial.ToString("X") : "Harvest: shearing failed: " + w);
                Answered(k, AnswerMs);
            } finally { System.Threading.Monitor.Exit(actionGate); }
        }
    }
    // ---------- mill, spin and weave ----------
    // Damian, 2026-10-15: "we need a miller feature", "and a tailor for the cott". The 1998 rule (UOX3
    // js/jse_objectassociations.scp, skill/cooking/wheat.js and flourmill.js; ServUO Cooking.cs WheatSheaf): a material in
    // the pack is double-clicked and its cursor targeted on a station: wheat sheaves into a flour mill, cotton or wool on a
    // spinning wheel, thread or yarn on a loom. uo1998 places these stations as items (a spinning wheel 0x1019 at 648,2176,
    // a loom 652,2177, three mills at Yew 550-554,820); how each answers on uo1998 is noted below. The art is the 1.25
    // TILEDATA's.
    static readonly int[] Mills = { 0x1920, 0x1921, 0x1922, 0x1923, 0x1925, 0x1926, 0x1927, 0x192C, 0x192D, 0x192E, 0x192F, 0x1931, 0x1932, 0x1933 };
    static readonly int[] Wheels = { 0x1015, 0x1016, 0x1017, 0x1019, 0x101A, 0x101B, 0x101C, 0x101D, 0x101E, 0x10A4, 0x10A5, 0x10A6 };
    // A loom that weaves is an addon of two of these (ServUO LoomEastAddon 0x1060 and 0x105F, LoomSouthAddon 0x1061 and
    // 0x1062); 0x1063-0x1066, the upright loom, is decoration, and thread used on it got no answer (Moonglow, 2026-10-17).
    static readonly int[] Looms = { 0x105F, 0x1060, 0x1061, 0x1062 };
    static readonly int[] Sheaves = { 0x1EBD }, Fibres = { 0x0DF9, 0x0DF8, 0x101F }, Strands = { 0x0FA0, 0x0FA1, 0x0E1D, 0x0E1E, 0x0E1F };
    static readonly int[] Products = { 0x1039, 0x1045, 0x0FA0, 0x0FA1, 0x0E1D, 0x0E1E, 0x0E1F, 0x0F95, 0x0F96, 0x0F97, 0x0F98, 0x0F99, 0x0F9A, 0x0F9B, 0x0F9C, 0x175D, 0x175E, 0x175F, 0x1760, 0x1761, 0x1762, 0x1763, 0x1764, 0x1765, 0x1767 };
    // uo1998's loom takes one spool a use and builds the bolt across uses ("The bolt of cloth has just been started.",
    // "... needs quite a bit more.", "... needs a little more.", "... is almost finished.", then "You create some cloth";
    // relay log 2026-10-15 02:09-02:10), not UOX3's five at once: every spool in the pack is used.
    // uo1998's mill (Yew, relay log 2026-10-15 02:17-02:19): a sheaf targeted on the hopper goes into it and the hopper
    // shows loaded (0x192E empty, 0x192F loaded); a double-click on the hopper grinds, "You need more wheat to make a sack
    // of flour." under MillSheaves, else the hopper shows 0x1932 and about 5 s later is empty again with a sack of flour
    // (0x1045) dropped on the ground beside it, which is picked up. The east mill's art (0x1922, 0x1923, 0x1926) is taken
    // to run the same way. Every mill in reach works at once (Damian, 2026-10-15: "there are three at a time here").
    static readonly int[] HopperEmpty = { 0x1922, 0x192E }, HopperLoaded = { 0x1923, 0x192F }, HopperGrinding = { 0x1926, 0x1927, 0x1932, 0x1933 };
    static readonly int[] FlourSacks = { 0x1039, 0x1045 };
    public const int MillSheaves = 4;  // a hopper with 1 sheaf before plus 3 ground; 1 before plus 2 needed more (02:18)
    // Sheaves this run put in each hopper; -1 for a hopper loaded before the run, whose count is unknown.
    readonly System.Collections.Generic.Dictionary<uint, int> hopperLoad = new System.Collections.Generic.Dictionary<uint, int>();
    readonly System.Collections.Generic.HashSet<uint> oldSacks = new System.Collections.Generic.HashSet<uint>(), ground = new System.Collections.Generic.HashSet<uint>(), everGround = new System.Collections.Generic.HashSet<uint>();
    bool millsTouched;
    // UOX3 InRange 3 for a mill, a wheel and a loom; Yew's three mills stand 2 tiles apart.
    public const int StationReach = 3, LoomTakes = 1, CollectWaitMs = 3000, WorkMisses = 3, StackResendMs = 500;
    // Bolts off the loom (Damian, 2026-10-16: "after using the loom, to stack the bolts. inline is best"): the loom's bolt
    // comes as an item of its own ("You create some cloth and put it in your backpack.", a new 0x0F95 of amount 1, uo1998
    // 04:38:43.051), so right after the turn it goes on the pack's largest bolt stack of the same hue: any bolt art first,
    // then its own art when the server leaves it apart.
    System.Collections.Generic.List<AssistItem> PackBolts(GameLink k) {
        var l = new System.Collections.Generic.List<AssistItem>();
        lock (k.State.Gate) { var inside = PackTree(k); foreach (var it in k.State.Items.Values) if (inside.Contains(it.Container) && IsBolt(it.Graphic)) l.Add(it); }
        return l;
    }
    void StackBolt(GameLink k, AssistItem bolt) {
        foreach (bool sameArt in new[] { false, true }) {
            AssistItem stack = null;
            foreach (var b in PackBolts(k)) if (b.Serial != bolt.Serial && b.Hue == bolt.Hue && (!sameArt || b.Graphic == bolt.Graphic) && (stack == null || b.Amount > stack.Amount)) stack = b;
            if (stack == null) return;
            lock (actionGate) {
                HarvestGap();
                string why = Lifted(k, bolt.Serial, System.Math.Max(1, bolt.Amount)); harvestActAt = System.DateTime.UtcNow;
                if (why != null) { Note("Work: lifting the new bolt " + bolt.Serial.ToString("X") + ": " + why); return; }
                why = k.Drop(bolt.Serial, stack.Serial); if (why != null) { Stopped("Stacking the bolt", bolt.Serial, why); return; }
                Answered(k, AnswerMs); System.Threading.Thread.Sleep(DropSettleMs); harvestActAt = System.DateTime.UtcNow;
            }
            if (Item(k, bolt.Serial) == null) return;
            if (stack.Graphic == bolt.Graphic) { Note("Work: the bolt " + bolt.Serial.ToString("X") + " did not stack onto " + stack.Serial.ToString("X")); return; }
        }
    }
    int WorkWaitMs { get { return Ms("work-wait"); } }
    public volatile string WorkState = "", WorkEnd = "";
    volatile bool workStop; System.Threading.Thread workThread;
    static readonly string[] WorkKinds = { "wheel", "loom" };
    // One mill action: pick up a new sack of flour, grind a full hopper, or load a sheaf. 1 acted, 2 nothing to do now but
    // a mill is grinding, 0 no mill work.
    int MillStep(GameLink k, uint pack, int px, int py, ref string end) {
        AssistItem sack = null; var hoppers = new System.Collections.Generic.List<AssistItem>(); var sacks = new System.Collections.Generic.List<AssistItem>();
        lock (k.State.Gate) foreach (var kv in k.State.Ground) {
            var g = kv.Value; int d = System.Math.Max(System.Math.Abs(g.X - px), System.Math.Abs(g.Y - py)); if (d > StationReach) continue;
            if (System.Array.IndexOf(FlourSacks, g.Graphic) >= 0 && !oldSacks.Contains(g.Serial) && !leave.Contains(g.Serial)) sacks.Add(g);
            if (System.Array.IndexOf(HopperEmpty, g.Graphic) >= 0 || System.Array.IndexOf(HopperLoaded, g.Graphic) >= 0 || System.Array.IndexOf(HopperGrinding, g.Graphic) >= 0) hoppers.Add(g);
        }
        // Only a sack beside a mill this run ground: another player's flour is left alone.
        foreach (var s in sacks) foreach (var h in hoppers) if (everGround.Contains(h.Serial) && System.Math.Abs(s.X - h.X) <= 1 && System.Math.Abs(s.Y - h.Y) <= 1) sack = s;
        if (hoppers.Count == 0 && sack == null) return 0;
        if (sack != null) { WorkState = "picking up a sack of flour"; Gather(k, sack, pack, "sack of flour"); return 1; }
        bool grinding = false;
        foreach (var h in hoppers) {
            int n; bool known = hopperLoad.TryGetValue(h.Serial, out n);
            if (System.Array.IndexOf(HopperGrinding, h.Graphic) >= 0) { if (ground.Contains(h.Serial)) grinding = true; hopperLoad[h.Serial] = 0; continue; }
            ground.Remove(h.Serial);
            if (System.Array.IndexOf(HopperEmpty, h.Graphic) >= 0) { hopperLoad[h.Serial] = 0; continue; }
            if (!known) hopperLoad[h.Serial] = -1;
        }
        // Grind a hopper at MillSheaves, or one of unknown contents after each sheaf this run added (marked -2).
        foreach (var h in hoppers) {
            int n = hopperLoad[h.Serial];
            if (System.Array.IndexOf(HopperLoaded, h.Graphic) < 0 || !(n >= MillSheaves || n == -2)) continue;
            WorkState = "grinding the mill at " + h.X + "," + h.Y;
            long seq; lock (k.State.Gate) seq = k.State.JournalSeq;
            lock (actionGate) { HarvestGap(); string why = k.Use(h.Serial, true); if (why != null) { end = "grinding failed: " + why; return -1; } }
            var by = System.DateTime.UtcNow.AddMilliseconds(1500); bool more = false, started = false;
            while (System.DateTime.UtcNow < by && !more && !started) {
                foreach (var line in k.State.JournalSince(seq)) if (line.IndexOf("need more wheat", System.StringComparison.OrdinalIgnoreCase) >= 0) more = true;
                AssistItem now; lock (k.State.Gate) k.State.Ground.TryGetValue(h.Serial, out now);
                if (now != null && System.Array.IndexOf(HopperGrinding, now.Graphic) >= 0) started = true;
                System.Threading.Thread.Sleep(50);
            }
            k.Settle(); millsTouched = true;
            if (started) { hopperLoad[h.Serial] = 0; ground.Add(h.Serial); everGround.Add(h.Serial); Note("Work: the mill at " + h.X + "," + h.Y + " is grinding"); return 1; }
            hopperLoad[h.Serial] = n >= MillSheaves ? MillSheaves - 1 : -1;  // needs more: one more sheaf, then try again
            return 1;
        }
        // Load a sheaf: a hopper this run has started first, then an empty one, then one of unknown contents.
        AssistItem sheaf; int have = InPack(k, pack, Sheaves, out sheaf);
        if (sheaf == null) return grinding ? 2 : 0;
        AssistItem target = null; int rank = 9;
        foreach (var h in hoppers) {
            if (System.Array.IndexOf(HopperGrinding, h.Graphic) >= 0) continue;
            int n = hopperLoad[h.Serial], r = n > 0 ? 0 : n == 0 ? 1 : 2;
            if (r < rank) { rank = r; target = h; }
        }
        if (target == null) return grinding ? 2 : 0;
        WorkState = "wheat into the mill at " + target.X + "," + target.Y + ": " + have + " sheaves left";
        lock (actionGate) {
            HarvestGap(); string why = k.Use(sheaf.Serial, true);
            if (why != null) { end = "the wheat sheaf did not open: " + why; return -1; }
            var cursorBy = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
            while (!k.MacroCursor && System.DateTime.UtcNow < cursorBy) System.Threading.Thread.Sleep(20);
            if (!k.MacroCursor) { k.Settle(); end = "the wheat sheaf opened no target cursor"; return -1; }
            why = k.Target(false, target.Serial, target.X, target.Y, target.Z, target.Graphic);
            if (why != null) { end = "targeting the mill failed: " + why; return -1; }
            harvestActAt = System.DateTime.UtcNow;
        }
        var until = System.DateTime.UtcNow.AddMilliseconds(3000); AssistItem none; bool took = false;
        while (System.DateTime.UtcNow < until && !(took = InPack(k, pack, Sheaves, out none) < have)) System.Threading.Thread.Sleep(100);
        k.Settle(); millsTouched = true;
        if (!took) { end = "the mill at " + target.X + "," + target.Y + " took no wheat (see the journal)"; return -1; }
        int was = hopperLoad[target.Serial]; hopperLoad[target.Serial] = was < 0 ? -2 : was + 1;
        return 1;
    }
    string lastKind = ""; System.DateTime lastWorkAt;
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> wheelBusy = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    int SpinMs { get { return Ms("spin"); } }  // uo1998 spun a bale in 5.1 s and 5.4 s (relay log 02:06:35, 02:06:50), with margin
    public void WorkToggle() {
        if (workThread != null && workThread.IsAlive) { workStop = true; WorkState = "stopping..."; return; }
        workStop = false; workThread = new System.Threading.Thread(Work); workThread.IsBackground = true; workThread.Start();
    }
    // The pack's own items of these graphics: the total and the largest stack.
    static int InPack(GameLink k, uint pack, int[] graphics, out AssistItem biggest) {
        int n = 0; biggest = null;
        lock (k.State.Gate) foreach (var kv in k.State.Items) {
            var it = kv.Value; if (it.Container != pack || System.Array.IndexOf(graphics, it.Graphic) < 0) continue;
            // uo1998 sends a single item's amount as 0 (relay log 02:06:35: the bale 4570393A, 0x25 amount 0000).
            n += System.Math.Max(1, it.Amount); if (biggest == null || it.Amount > biggest.Amount) biggest = it;
        }
        return n;
    }
    void Work() {
        int done = 0, misses = 0; string end = "nothing to work"; var k = link; lastKind = ""; wheelBusy.Clear();
        hopperLoad.Clear(); ground.Clear(); everGround.Clear(); oldSacks.Clear(); millsTouched = false;
        if (k != null) lock (k.State.Gate) foreach (var kv in k.State.Ground) if (System.Array.IndexOf(FlourSacks, kv.Value.Graphic) >= 0) oldSacks.Add(kv.Key);
        try {
            while (!workStop) {
                k = link; if (k == null || k.Closed) { end = "no connection"; break; }
                if (k.State.Dead) { end = "you are dead"; break; }
                uint pack; int px, py; lock (k.State.Gate) { pack = k.State.Backpack; px = k.State.X; py = k.State.Y; }
                // The nearest station of each kind in reach. With more than one kind holding material in the pack, the kinds
                // take turns (Damian, 2026-10-15: "make it spin and weave at the same time if both are there", "like after you
                // start"): a wheel takes its bale at the target, so the loom weaves while the wheel turns.
                int mill = MillStep(k, pack, px, py, ref end);
                if (mill < 0) break;
                if (mill == 1) { done++; misses = 0; continue; }
                bool grinding = mill == 2;
                // Every spinning wheel in reach, but only the nearest loom (Damian, 2026-10-16: "for spinning, use all
                // available spinning wheels, but only 1 loom").
                var near = new System.Collections.Generic.Dictionary<string, AssistItem>(); var nearD = new System.Collections.Generic.Dictionary<string, int>();
                var wheels = new System.Collections.Generic.List<AssistItem>();
                lock (k.State.Gate) foreach (var kv in k.State.Ground) {
                    var g = kv.Value; int d = System.Math.Max(System.Math.Abs(g.X - px), System.Math.Abs(g.Y - py)); if (d > StationReach) continue;
                    if (System.Array.IndexOf(Wheels, g.Graphic) >= 0) { wheels.Add(g); continue; }
                    int was; if (System.Array.IndexOf(Looms, g.Graphic) >= 0 && (!nearD.TryGetValue("loom", out was) || d < was)) { near["loom"] = g; nearD["loom"] = d; }
                }
                if (near.Count == 0 && wheels.Count == 0 && !grinding) { end = millsTouched ? "no wheat left in the backpack and no mill grinding" : "no flour mill, spinning wheel or loom within " + StationReach + " tiles"; break; }
                AssistItem station = null, use = null; string kind = null; int[] takes = null; int have = 0;
                int from = System.Array.IndexOf(WorkKinds, lastKind) + 1;
                // A wheel spins one bale at a time (uo1998 02:06:35 to 02:06:40: the bale taken at the target, the spool 5 s
                // later), so a wheel that took a bale is passed over for SpinMs; another free wheel takes the next bale.
                AssistItem none; int madeNow = InPack(k, pack, Products, out none);
                var nowUtc = System.DateTime.UtcNow; bool spinning = false; AssistItem freeWheel = null;
                foreach (var w in wheels) { System.DateTime busy; if (wheelBusy.TryGetValue(w.Serial, out busy) && busy > nowUtc) spinning = true; else if (freeWheel == null) freeWheel = w; }
                if (freeWheel != null) near["wheel"] = freeWheel;
                for (int i = 0; i < WorkKinds.Length && use == null; i++) {
                    string kd = WorkKinds[(from + i) % WorkKinds.Length]; if (!near.ContainsKey(kd)) continue;
                    if (kd == "loom") {
                        // A loom counts one material across every stack in the pack (UOX3 ResourceCount): the first with LoomTakes.
                        foreach (int sg in Strands) { AssistItem big; int n = InPack(k, pack, new[] { sg }, out big); if (n >= LoomTakes) { takes = new[] { sg }; use = big; have = n; break; } }
                    } else { takes = Fibres; have = InPack(k, pack, takes, out use); }
                    if (use != null) { kind = kd; station = near[kd]; }
                }
                if (use == null) {
                    // The wheel is still spinning: its spool is the next material.
                    if (spinning || grinding) { System.Threading.Thread.Sleep(200); continue; }
                    end = "no wheat, cotton, wool, thread or yarn left in the backpack for the stations here"; break;
                }
                lastKind = kind; lastWorkAt = System.DateTime.UtcNow;
                int made = madeNow; long workMark = k.State.JournalSeq;
                var boltsBefore = kind == "loom" ? PackBolts(k) : null;
                WorkState = UoTiles.Name(use.Graphic) + " on the " + UoTiles.Name(station.Graphic) + ": " + have + " left, " + done + " done";
                lock (actionGate) {
                    HarvestGap(); string why = k.Use(use.Serial, true);
                    if (why != null) { end = "the " + UoTiles.Name(use.Graphic) + " did not open: " + why; break; }
                    var cursorBy = System.DateTime.UtcNow.AddMilliseconds(CursorMs);
                    while (!k.MacroCursor && System.DateTime.UtcNow < cursorBy) System.Threading.Thread.Sleep(20);
                    if (!k.MacroCursor) { k.Settle(); end = "the " + UoTiles.Name(use.Graphic) + " opened no target cursor"; break; }
                    why = k.Target(false, station.Serial, station.X, station.Y, station.Z, station.Graphic);
                    if (why != null) { end = "targeting the " + UoTiles.Name(station.Graphic) + " failed: " + why; break; }
                    harvestActAt = System.DateTime.UtcNow;
                }
                // The material leaves the pack or a product arrives; a loaded mill is emptied when its hopper shows ready.
                var workFrom = System.DateTime.UtcNow; var until = workFrom.AddMilliseconds(WorkWaitMs); bool took = false, gave = false;
                while (System.DateTime.UtcNow < until && !workStop) {
                    took = InPack(k, pack, takes, out none) < have; gave = InPack(k, pack, Products, out none) > made;
                    // A spool from another wheel is not this wheel's answer: a wheel answers by taking its bale.
                    if (took || gave && kind != "wheel") { Seen("work-wait", (int)(System.DateTime.UtcNow - workFrom).TotalMilliseconds); break; }
                    System.Threading.Thread.Sleep(100);
                }
                k.Settle();
                // A stack the station took one from is deleted and sent again one fewer (uo1998 04:23:53.311 to .439, a
                // vendor stack of 10 "shuttle of thread", art 0x0FA0, on the loom); counted in between, the pack read empty
                // and weaving stopped after one. The next turn waits up to StackResendMs for the stack to come back.
                if (took) { var back = System.DateTime.UtcNow.AddMilliseconds(StackResendMs); while (Item(k, use.Serial) == null && System.DateTime.UtcNow < back) System.Threading.Thread.Sleep(20); }
                if (boltsBefore != null) foreach (var b in PackBolts(k)) if (!boltsBefore.Exists(o => o.Serial == b.Serial)) { StackBolt(k, b); break; }
                // A station can be in use for a moment (UOX3: "That is already in use." while a wheel turns), or the server
                // asks for a wait: tried again. Silence, or any other answer, stops the work (assist-plan.md rule 1).
                if (!took && (!gave || kind == "wheel")) {
                    if (kind == "wheel") wheelBusy[station.Serial] = System.DateTime.UtcNow.AddMilliseconds(SpinMs);
                    var said = k.State.JournalSince(workMark);
                    bool again = said.Exists(j => j.IndexOf("in use", System.StringComparison.OrdinalIgnoreCase) >= 0 || j.IndexOf("You must wait", System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!again) { end = "the " + UoTiles.Name(station.Graphic) + " took nothing" + (said.Count > 0 ? "; the server said \"" + said[said.Count - 1] + "\"" : " and the server said nothing"); break; }
                    if (++misses >= WorkMisses) { end = "the " + UoTiles.Name(station.Graphic) + " was busy " + WorkMisses + " times in a row"; break; }
                    System.Threading.Thread.Sleep(3000); continue;
                }
                if (kind == "wheel") wheelBusy[station.Serial] = System.DateTime.UtcNow.AddMilliseconds(SpinMs);
                misses = 0; done++; Note("Work: " + UoTiles.Name(use.Graphic) + " on the " + UoTiles.Name(station.Graphic) + (gave ? "" : ", nothing new in the pack yet"));
            }
            if (workStop) end = "stopped";
        } finally {
            WorkState = ""; WorkEnd = end;
            Note("Work: " + done + " done; " + end);
            if (k != null && !k.Closed) k.Tell("Mill, spin and weave: " + done + " done; " + end + ".", done > 0 ? 0x0044 : 0x002B);
        }
    }

    // Lift a ground item into the pack, as the pickup does.
    void Gather(GameLink k, AssistItem it, uint pack, string what) {
        HarvestGap(); string why = Lifted(k, it.Serial, it.Amount); if (why != null) { leave.Add(it.Serial); Note("Harvest: " + what + " not lifted: " + why); return; }
        why = k.Drop(it.Serial, pack); if (why != null) { Stopped("harvest", it.Serial, why); leave.Add(it.Serial); return; }
        Answered(k, AnswerMs);
        lock (picked) { int n; picked.TryGetValue(it.Graphic, out n); picked[it.Graphic] = n + it.Amount; }
        Note("Harvest: " + it.Amount + " " + what + " into the backpack");
    }

    // ---------- session summary ----------
    // Damian, 2026-10-15: "add a session summary report to the session details, showing aggregates of things like kills by
    // creature, total skills gained lost, attrs, reagents picked up, loops run, and their effectiveness at skill gain".
    // A kill is the death (0xAF) of a mobile the player attacked (0x05) within KillWindowMs.
    public sealed class LoopRun { public string Name; public int Skill; public System.DateTime Start, End; public int Rounds, From, To; }
    public const int KillWindowMs = 120000, MiningSkill = 45;
    readonly System.Collections.Generic.List<LoopRun> runs = new System.Collections.Generic.List<LoopRun>();
    volatile LoopRun currentRun;
    readonly System.Collections.Generic.Dictionary<string, int> kills = new System.Collections.Generic.Dictionary<string, int>();
    readonly System.Collections.Generic.Dictionary<int, int> picked = new System.Collections.Generic.Dictionary<int, int>();
    readonly System.Collections.Generic.Dictionary<uint, System.DateTime> attacked = new System.Collections.Generic.Dictionary<uint, System.DateTime>();
    void Fought(uint s) { lock (attacked) { if (attacked.Count > 1000) attacked.Clear(); attacked[s] = System.DateTime.UtcNow; } }
    static int SkillOf(GameLink k, int id) { if (id < 0) return 0; int v; lock (k.State.Gate) k.State.Skills.TryGetValue(id + 1, out v); return v; }
    static string SkillName(int id) { return id >= 0 && id < AssistWindow.SkillNames.Length ? AssistWindow.SkillNames[id] : "skill " + id; }
    static string Tenths(int v) { return (v >= 0 ? "+" : "-") + (System.Math.Abs(v) / 10.0).ToString("0.0"); }
    string MobileName(GameLink k, uint s) {
        lock (k.State.Gate) { string n; if (k.State.Names.TryGetValue(s, out n)) return n; int b; if (k.State.Bodies.TryGetValue(s, out b)) return "body 0x" + b.ToString("X3"); }
        return "mobile " + s.ToString("X");
    }
    void Died(GameLink k, uint s, uint corpse) {
        string victim = MobileName(k, s);
        lock (killMarks) if (corpse != 0) { if (corpseOf.Count > 2000) corpseOf.Clear(); corpseOf[corpse] = victim; }
        System.DateTime at; lock (attacked) { if (!attacked.TryGetValue(s, out at) || (System.DateTime.UtcNow - at).TotalMilliseconds > KillWindowMs) return; attacked.Remove(s); }
        lock (kills) { int n; kills.TryGetValue(victim, out n); kills[victim] = n + 1; }
        KillAt(k, s, victim, "you", corpse);
    }
    // The session's kills on the map (Damian, 2026-10-15: "on the map mark all the sessions kills"): where the victim was
    // last seen, by you or by a pet, with the loot taken from its corpse.
    public sealed class KillMark { public int X, Y; public string Name = "", By = "", Loot = ""; public System.DateTime At; public uint Corpse; }
    readonly System.Collections.Generic.List<KillMark> killMarks = new System.Collections.Generic.List<KillMark>();
    readonly System.Collections.Generic.Dictionary<uint, string> corpseOf = new System.Collections.Generic.Dictionary<uint, string>();
    void KillAt(GameLink k, uint s, string victim, string by, uint corpse) {
        int x, y; Creature c; bool seen; lock (creatures) seen = creatures.TryGetValue(s, out c);
        if (seen) { x = c.X; y = c.Y; } else lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        lock (killMarks) { killMarks.Add(new KillMark { X = x, Y = y, Name = victim, By = by, At = System.DateTime.Now, Corpse = corpse }); if (killMarks.Count > 1000) killMarks.RemoveAt(0); }
    }
    public KillMark[] KillMarks() { lock (killMarks) return killMarks.ToArray(); }

    // ---------- trips ----------
    // Damian, 2026-10-15: "'begin trip' and 'end trip' and that trip will log all the npcs you see, the resources and other
    // loose items found ... a report i could view to see how many frigging monsters i just had to dodge". Between the two:
    // every mobile drawn (0x77, 0x78: serial, body, place, notoriety), every world item drawn (0x1A), the steps walked
    // (0x22), and the session's kills, loot and deaths since the start; End writes reports/trip-<server>-<start>.md.
    // Close: the player's steps this hostile spent within TripRecorder.ChaseRange; Area: the region it was first seen in.
    public sealed class TripMob { public uint Serial; public int Body, X, Y, Noto, X0, Y0, Close; public bool Person; public System.DateTime First, Last; public string Area = ""; }
    public sealed class TripItem { public uint Serial; public int Graphic, Amount, X, Y; }
    public sealed class Trip {
        public System.DateTime Start; public int X0, Y0, Steps, Kills0, Deaths0; public bool Auto, Session;
        public readonly TripRecorder Rec = new TripRecorder();
        public readonly System.Collections.Generic.Dictionary<uint, TripMob> Mobs = new System.Collections.Generic.Dictionary<uint, TripMob>();
        public readonly System.Collections.Generic.Dictionary<uint, TripItem> Items = new System.Collections.Generic.Dictionary<uint, TripItem>();
        public readonly System.Collections.Generic.Dictionary<string, int> Loot0 = new System.Collections.Generic.Dictionary<string, int>();
    }
    public const int TripKeep = 20000;
    volatile Trip trip;
    public bool TripOn { get { return trip != null; } }
    public string TripState() { var t = trip; if (t == null) return ""; lock (t) return "Trip since " + t.Start.ToString("HH:mm") + ": " + t.Mobs.Count + " mobiles, " + t.Items.Count + " items, " + t.Steps + " steps"; }
    public string BeginTrip() { return BeginTrip(false); }
    // Every session is a trip (Damian, 2026-10-16: "always start a new session with a new trip"): the login (0x55) ends a
    // trip still running and begins one that runs until the connection goes or End trip; one with no step is dropped.
    void SessionTrip() {
        if (trip != null) { string path; string why = EndTrip(out path); if (why != null) Note("Trip: " + why); }
        BeginTrip(false, true);
    }
    string BeginTrip(bool auto, bool session = false) {
        var k = link; if (k == null) return "no game connection";
        var t = new Trip { Start = System.DateTime.Now, Auto = auto, Session = session };
        lock (k.State.Gate) { t.X0 = k.State.X; t.Y0 = k.State.Y; t.Rec.Begin(k.State.Skills); }
        lock (killMarks) t.Kills0 = killMarks.Count; lock (corpses) t.Deaths0 = corpses.Count;
        lock (lootTotals) foreach (var kv in lootTotals) t.Loot0[kv.Key] = kv.Value;
        trip = t; Note((auto ? "Trip begun on leaving guarded land at " : session ? "Trip begun with the session at " : "Trip begun at ") + t.X0 + "," + t.Y0); return null;
    }
    // Automatic trips (Damian, 2026-10-16: "a drop down of trips taken"): with the server's region file loaded, a trip
    // begins at the first step out of guarded land and ends after AutoEndSteps steps back inside it; one shorter than
    // AutoMinSteps is dropped. A trip begun by the button runs until the button ends it.
    public const int AutoEndSteps = 20, AutoMinSteps = 50;
    public bool AutoTrips { get { return Cfg.Get("trips.auto", "on") == "on"; } set { Cfg.Set("trips.auto", value ? "on" : "off"); } }
    public int TripsWritten;
    void TripPacket(GameLink k, byte[] p) {
        var t = trip;
        if (t == null) {
            if (p[0] != 0x22 || p.Length < 3 || p[2] == 0 || !AutoTrips || !UoRegions.Loaded) return;
            int ax, ay, az; lock (k.State.Gate) { ax = k.State.X; ay = k.State.Y; az = k.State.Z; }
            bool guarded; TripLore.AreaAt(ax, ay, az, out guarded); if (!guarded) BeginTrip(true);
            return;
        }
        uint me; lock (k.State.Gate) me = k.State.Player;
        uint s = 0; int body = 0, x = 0, y = 0, noto = 0;
        if (p[0] == 0x77 && p.Length >= 17) { s = U32(p, 1); body = (p[5] << 8) | p[6]; x = (p[7] << 8) | p[8]; y = (p[9] << 8) | p[10]; noto = p[16]; }
        else if (p[0] == 0x78 && p.Length >= 19) { s = U32(p, 3) & 0x7FFFFFFF; body = (p[7] << 8) | p[8]; x = ((p[9] << 8) | p[10]) & 0x7FFF; y = (p[11] << 8) | p[12]; noto = p[18]; }
        else if (p[0] == 0x22 && p.Length >= 3 && p[2] != 0) { TripStep(k, t); return; }
        else if (p[0] == 0x1C && p.Length > 44) {
            if (System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).IndexOf(TripLore.ResistLine, System.StringComparison.OrdinalIgnoreCase) >= 0) lock (t) t.Rec.Get(t.Rec.Current, t.X0, t.Y0).Resist++;
            return;
        }
        else if (p[0] == 0x1A && p.Length >= 9) {
            uint item = U32(p, 3) & 0x7FFFFFFF; AssistItem g; lock (k.State.Gate) k.State.Ground.TryGetValue(item, out g);
            if (g == null) return;
            lock (t) if (!t.Items.ContainsKey(item) && t.Items.Count < TripKeep) {
                t.Items[item] = new TripItem { Serial = item, Graphic = g.Graphic, Amount = System.Math.Max(1, g.Amount), X = g.X, Y = g.Y };
                if (ReagentName(g.Graphic) != null) t.Rec.Get(t.Rec.Current, g.X, g.Y).ReagentPiles++;
            }
            return;
        } else return;
        if (s == me || s == 0) return;
        lock (t) {
            TripMob m;
            if (!t.Mobs.TryGetValue(s, out m)) { if (t.Mobs.Count >= TripKeep) return; t.Mobs[s] = m = new TripMob { Serial = s, First = System.DateTime.Now, X0 = x, Y0 = y, Area = t.Rec.Current }; }
            m.Body = body; m.X = x; m.Y = y; m.Noto = noto & 0x7F; m.Person = body >= 0x190 && body <= 0x193; m.Last = System.DateTime.UtcNow;
        }
    }
    void TripStep(GameLink k, Trip t) {
        int x, y, z, hits, maxHits, stam, maxStam, gold; var skills = new System.Collections.Generic.Dictionary<int, int>();
        lock (k.State.Gate) { x = k.State.X; y = k.State.Y; z = k.State.Z; hits = k.State.Hits; maxHits = k.State.MaxHits; stam = k.State.Stam; maxStam = k.State.MaxStam; gold = k.State.Gold; foreach (var kv in k.State.Skills) skills[kv.Key] = kv.Value; }
        bool guarded; int run;
        lock (t) { t.Steps++; t.Rec.Step(x, y, z, hits, maxHits, stam, maxStam, gold, skills, t.Mobs.Values, out guarded); run = t.Rec.GuardedRun; }
        if (t.Auto && run >= AutoEndSteps && trip == t) { string path; string why = EndTrip(out path); if (why != null) Note("Trip: " + why); }
    }
    static readonly string[] NotoNames = { "unknown", "innocent", "friend", "attackable", "criminal", "enemy", "murderer", "invulnerable" };
    // Ends the trip and writes its report; the report's path, or why none.
    public string EndTrip(out string path) {
        path = null; var t = trip; var k = link; if (t == null) return "no trip is running"; trip = null;
        if (t.Auto) { int steps; lock (t) steps = t.Rec.Steps; if (steps < AutoMinSteps) return "dropped a " + steps + "-step trip outside the guards"; }
        if (t.Session) { int steps; lock (t) steps = t.Rec.Steps; if (steps == 0) return "dropped a session trip with no step"; }
        var sb = new System.Text.StringBuilder(); var now = System.DateTime.Now; int x1 = 0, y1 = 0; if (k != null) lock (k.State.Gate) { x1 = k.State.X; y1 = k.State.Y; }
        sb.AppendLine("# Trip " + t.Start.ToString("yyyy-MM-dd HH:mm") + " to " + now.ToString("HH:mm") + " (" + (int)(now - t.Start).TotalMinutes + " min) on " + Server.TrimEnd('.'));
        sb.AppendLine(); sb.AppendLine("From " + t.X0 + "," + t.Y0 + " to " + x1 + "," + y1 + "; " + t.Steps + " steps.");
        lock (t) {
            var creatures = new System.Collections.Generic.Dictionary<string, int[]>(); var people = new System.Collections.Generic.List<string>(); var hostile = 0;
            foreach (var m in t.Mobs.Values) {
                string name = k != null ? MobileName(k, m.Serial) : m.Serial.ToString("X");
                if (m.Person) { people.Add(name + " (" + (m.Noto < NotoNames.Length ? NotoNames[m.Noto] : "notoriety " + m.Noto) + ") first at " + m.X0 + "," + m.Y0); continue; }
                int[] c; if (!creatures.TryGetValue(name, out c)) creatures[name] = c = new int[2]; c[0]++; if (m.Noto == 3 || m.Noto >= 4 && m.Noto <= 6) { c[1]++; hostile++; }
            }
            int all = 0; foreach (var c in creatures.Values) all += c[0];
            sb.AppendLine(); sb.AppendLine("## Creatures: " + all + " (" + hostile + " attackable or worse)"); sb.AppendLine();
            sb.AppendLine("| creature | seen | attackable or worse |"); sb.AppendLine("|---|---|---|");
            var cl = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int[]>>(creatures); cl.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            foreach (var kv in cl) sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " |");
            sb.AppendLine(); sb.AppendLine("## People and NPCs: " + people.Count); sb.AppendLine(); people.Sort(); foreach (var p in people) sb.AppendLine("- " + p);
            var items = new System.Collections.Generic.Dictionary<string, int[]>();
            foreach (var it in t.Items.Values) { string n = UoTiles.Name(it.Graphic); int[] c; if (!items.TryGetValue(n, out c)) items[n] = c = new int[2]; c[0]++; c[1] += it.Amount; }
            sb.AppendLine(); sb.AppendLine("## Loose items: " + t.Items.Count); sb.AppendLine(); sb.AppendLine("| item | piles | total |"); sb.AppendLine("|---|---|---|");
            var il = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int[]>>(items); il.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            foreach (var kv in il) sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " |");
        }
        sb.AppendLine(); sb.AppendLine("## Kills, loot and deaths"); sb.AppendLine();
        lock (killMarks) for (int i = t.Kills0; i < killMarks.Count; i++) { var m = killMarks[i]; sb.AppendLine("- killed " + m.Name + (m.By != "you" ? " (by " + m.By + ")" : "") + " at " + m.X + "," + m.Y + " " + m.At.ToString("HH:mm") + (m.Loot.Length > 0 ? ": " + m.Loot : "")); }
        lock (lootTotals) foreach (var kv in lootTotals) { int was; t.Loot0.TryGetValue(kv.Key, out was); if (kv.Value > was) sb.AppendLine("- looted " + (kv.Value - was) + " " + kv.Key); }
        lock (corpses) for (int i = t.Deaths0; i < corpses.Count; i++) sb.AppendLine("- died at " + corpses[i].X + "," + corpses[i].Y + " " + corpses[i].At.ToString("HH:mm"));
        try {
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "reports"); System.IO.Directory.CreateDirectory(dir);
            path = System.IO.Path.Combine(dir, "trip-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + "-" + t.Start.ToString("yyyyMMdd-HHmm") + ".md");
            System.IO.File.WriteAllText(path, sb.ToString());
            TripRegions(k, t);
            lock (t) t.Rec.Write(System.IO.Path.ChangeExtension(path, ".trip"), t.Start, now, Server.TrimEnd('.'), t.Auto ? 1 : t.Session ? 2 : 0, t.X0, t.Y0, x1, y1);
            System.Threading.Interlocked.Increment(ref TripsWritten); Note("Trip ended: " + path); return null;
        } catch (System.Exception e) { path = null; return "the report was not written: " + e.Message; }
    }
    // The trip's creatures, kills and deaths into the regions they happened in.
    void TripRegions(GameLink k, Trip t) {
        var names = new System.Collections.Generic.Dictionary<uint, string>();
        lock (t) foreach (var m in t.Mobs.Values) if (!m.Person) names[m.Serial] = k != null ? MobileName(k, m.Serial) : m.Serial.ToString("X");
        var kills = new System.Collections.Generic.List<KillMark>(); lock (killMarks) for (int i = t.Kills0; i < killMarks.Count; i++) kills.Add(killMarks[i]);
        var deaths = new System.Collections.Generic.List<Corpse>(); lock (corpses) for (int i = t.Deaths0; i < corpses.Count; i++) deaths.Add(corpses[i]);
        int z = 0; if (k != null) lock (k.State.Gate) z = k.State.Z;
        lock (t) {
            foreach (var m in t.Mobs.Values) {
                if (m.Person) continue; bool g;
                var a = t.Rec.Get(m.Area.Length > 0 ? m.Area : TripLore.AreaAt(m.X0, m.Y0, z, out g), m.X0, m.Y0);
                int[] c; string n = names[m.Serial]; if (!a.Creatures.TryGetValue(n, out c)) a.Creatures[n] = c = new int[3];
                c[0]++; if (TripRecorder.Hostile(m.Noto)) c[1]++; if (m.Close >= TripRecorder.ChaseSteps) c[2]++;
            }
            bool guarded;
            foreach (var m in kills) t.Rec.Get(TripLore.AreaAt(m.X, m.Y, z, out guarded), m.X, m.Y).Kills++;
            foreach (var d in deaths) t.Rec.Get(TripLore.AreaAt(d.X, d.Y, z, out guarded), d.X, d.Y).Deaths++;
        }
    }
    // A corpse's loot into the trip region its kill was in, or the region the player stands in.
    void TripLoot(uint corpse, int graphic, string what, int amount) {
        var t = trip; if (t == null) return;
        int x = -1, y = -1; lock (killMarks) foreach (var m in killMarks) if (m.Corpse == corpse) { x = m.X; y = m.Y; }
        lock (t) {
            bool g; var a = x < 0 ? t.Rec.Get(t.Rec.Current, t.X0, t.Y0) : t.Rec.Get(TripLore.AreaAt(x, y, 0, out g), x, y);
            int n; a.Loot.TryGetValue(what, out n); a.Loot[what] = n + amount;
            if (ReagentName(graphic) != null) a.ReagentsLooted += amount;
            if (TripLore.Wand(graphic) || AssistWindow.IsMagic(what)) a.MagicLoot++;
        }
    }
    // Loot off corpses (Damian, 2026-10-15: "track the loot off the corpses"): the player's own lift (0x07) of an item
    // inside a corpse, then its drop (0x08) into any container but that corpse; a refused lift (0x27) cancels. A corpse is
    // one the death packet named (0xAF) or an item of corpse art (0x2006). Totals for the session, and a line each in
    // loot-<server>.txt.
    public const int CorpseArt = 0x2006;
    readonly System.Collections.Generic.Dictionary<string, int> lootTotals = new System.Collections.Generic.Dictionary<string, int>();
    uint liftSerial, liftCorpse; int liftGraphic, liftAmount;
    void LootClient(GameLink k, byte[] p) {
        if (p[0] == 0x07 && p.Length >= 7) {
            uint s = U32(p, 1); liftSerial = 0; AssistItem it; uint box = 0; int g = 0; bool corpse = false;
            lock (k.State.Gate) {
                if (!k.State.Items.TryGetValue(s, out it)) return;
                box = it.Container; g = it.Graphic;
                AssistItem bx; corpse = k.State.Ground.TryGetValue(box, out bx) && bx.Graphic == CorpseArt;
            }
            lock (killMarks) corpse |= corpseOf.ContainsKey(box);
            if (!corpse) return;
            liftSerial = s; liftCorpse = box; liftGraphic = g; liftAmount = System.Math.Max(1, (p[5] << 8) | p[6]);
        }
        if (p[0] == 0x08 && p.Length >= 14 && liftSerial != 0 && U32(p, 1) == liftSerial) {
            uint into = U32(p, 10); uint s = liftSerial; liftSerial = 0;
            if (into == liftCorpse || into == 0xFFFFFFFF) return;
            string what = UoTiles.Name(liftGraphic), from;
            lock (killMarks) { if (!corpseOf.TryGetValue(liftCorpse, out from)) from = "a corpse"; foreach (var m in killMarks) if (m.Corpse == liftCorpse) m.Loot += (m.Loot.Length > 0 ? ", " : "") + liftAmount + " " + what; }
            lock (lootTotals) { int n; lootTotals.TryGetValue(what, out n); lootTotals[what] = n + liftAmount; }
            TripLoot(liftCorpse, liftGraphic, what, liftAmount);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "loot-" + Server.TrimEnd('.').Replace(':', '-').Replace(',', '-') + ".txt"), System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + from + "\t" + liftAmount + "\t" + what + "\r\n"); } catch { }
        }
    }
    public string SessionSummary() {
        var k = link; if (k == null) return "";
        var lines = new System.Collections.Generic.List<string>();
        lock (kills) if (kills.Count > 0) { var parts = new System.Collections.Generic.List<string>(); int all = 0; foreach (var kv in kills) { parts.Add(kv.Key + " " + kv.Value); all += kv.Value; } lines.Add("Kills " + all + ": " + string.Join(", ", parts)); }
        lock (lootTotals) if (lootTotals.Count > 0) { var parts = new System.Collections.Generic.List<string>(); foreach (var kv in lootTotals) parts.Add(kv.Value + " " + kv.Key); lines.Add("Loot: " + string.Join(", ", parts)); }
        lock (k.State.Gate) {
            var up = new System.Collections.Generic.List<string>(); int net = 0;
            foreach (var kv in k.State.Skills) { int s0; if (!k.State.SkillStart.TryGetValue(kv.Key, out s0) || kv.Value == s0) continue; up.Add(Tenths(kv.Value - s0) + " " + SkillName(kv.Key - 1)); net += kv.Value - s0; }
            lines.Add("Skills: " + (up.Count == 0 ? "no change" : string.Join(", ", up) + " (net " + Tenths(net) + ")"));
            if (k.State.StrStart >= 0) lines.Add("Attributes: str " + k.State.Str + " (" + (k.State.Str - k.State.StrStart).ToString("+0;-0;0") + "), dex " + k.State.Dex + " (" + (k.State.Dex - k.State.DexStart).ToString("+0;-0;0") + "), int " + k.State.Int + " (" + (k.State.Int - k.State.IntStart).ToString("+0;-0;0") + ")");
        }
        lock (picked) if (picked.Count > 0) { var parts = new System.Collections.Generic.List<string>(); int all = 0; foreach (var kv in picked) { parts.Add((ReagentName(kv.Key) ?? kv.Key.ToString("X4")) + " " + kv.Value); all += kv.Value; } lines.Add("Picked up " + all + ": " + string.Join(", ", parts)); }
        var each = new System.Collections.Generic.Dictionary<string, int[]>();  // runs, rounds, gain, seconds
        var all2 = new System.Collections.Generic.List<LoopRun>(); lock (runs) all2.AddRange(runs); var cur = currentRun; if (cur != null) all2.Add(cur);
        foreach (var r in all2) {
            int[] a; if (!each.TryGetValue(r.Name, out a)) each[r.Name] = a = new int[5];
            a[0]++; a[1] += r.Rounds; a[2] += (r == cur ? SkillOf(k, r.Skill) : r.To) - r.From; a[3] += (int)(((r == cur ? System.DateTime.UtcNow : r.End) - r.Start).TotalSeconds); a[4] = r.Skill;
        }
        foreach (var kv in each) {
            var a = kv.Value; string gain = a[4] < 0 ? "" : ", " + Tenths(a[2]) + " " + SkillName(a[4]) + (a[3] > 0 ? " (" + (a[2] / 10.0 * 3600.0 / a[3]).ToString("0.0") + "/hour" + (a[1] > 0 ? ", " + (a[2] / 10.0 * 100.0 / a[1]).ToString("0.0") + "/100 rounds" : "") + ")" : "");
            lines.Add("Loop " + kv.Key + ": " + a[0] + " run" + (a[0] == 1 ? "" : "s") + ", " + a[1] + " rounds, " + (a[3] / 60) + " min" + gain);
        }
        int seen, beasts; lock (people) seen = people.Count; lock (creatures) beasts = creatures.Count;
        if (seen + beasts > 0) lines.Add("Seen " + seen + " people, " + beasts + " creatures: report " + PeopleReportPath);
        return string.Join("\n", lines);
    }

    // ---------- people seen ----------
    // Damian, 2026-10-15: "keep a report of all players seen, any activities they were performing, where they were,
    // noteriety and criminal details". Every human-bodied mobile (0x190 to 0x193, ghosts included) but the player; the
    // 1.25 wire does not tell players from human NPCs. Activity from 0x2F (fight), 0x6E (animation), 0x1C (speech) and
    // 0xAF (death); notoriety from 0x77/0x78, every change kept. Written as Markdown every ReportMs.
    public sealed class Person { public uint Serial; public string Name = ""; public int Body, Noto, X, Y, Z; public bool NotoKnown; public System.DateTime First, Last; public readonly System.Collections.Generic.List<string> Acts = new System.Collections.Generic.List<string>(); public string LastAct; public int Repeats; }
    public const int PeopleKeep = 2000, ActsKeep = 100, ReportMs = 30000;
    static readonly string[] Notorieties = { "none", "innocent", "friend", "attackable", "criminal", "enemy", "murderer", "invulnerable" };
    readonly System.Collections.Generic.Dictionary<uint, Person> people = new System.Collections.Generic.Dictionary<uint, Person>();
    public string PeopleReportPath = "";
    bool peopleChanged; System.Threading.Timer reportTimer;
    // Human action numbers (the 1.25 client's body animations): swings 9-13, casting 16-17, shooting 18-19, bow 32, salute 33, eat 34.
    static string Action(int a) {
        if (a >= 9 && a <= 13) return "swinging (fighting or harvesting)";
        if (a == 16 || a == 17) return "casting"; if (a == 18 || a == 19) return "shooting";
        if (a == 32) return "bowing"; if (a == 33) return "saluting"; if (a == 34) return "eating"; return null;
    }
    // Creatures (every mobile that is not human-bodied) for the report's second part (Damian, 2026-10-15: "also for the
    // report creatures seen, and where"): one row per serial, first and last place and time, and whether it died.
    public sealed class Creature { public uint Serial; public int Body, X0, Y0, X, Y; public System.DateTime First, Last; public bool Died; }
    public const int CreaturesKeep = 5000, PlacesShown = 12;
    readonly System.Collections.Generic.Dictionary<uint, Creature> creatures = new System.Collections.Generic.Dictionary<uint, Creature>();
    void SeenCreature(uint s, int body, int x, int y) {
        lock (creatures) {
            Creature c; var now = System.DateTime.Now;
            if (!creatures.TryGetValue(s, out c)) { if (creatures.Count >= CreaturesKeep) return; creatures[s] = c = new Creature { Serial = s, X0 = x, Y0 = y, First = now }; }
            c.Body = body; c.X = x; c.Y = y; c.Last = now; peopleChanged = true;
        }
    }
    public AssistAtlas Atlas;
    // 1.25 art (the client's TILEDATA.MUL): 0x0ED4 the gravestone a 1998 guildstone is drawn with, 0x0BCF-0x0BD2 the
    // wooden and brass signs a house carries. Each such item is single-clicked once for its label.
    public const int GuildstoneArt = 0x0ED4, HouseSignArt0 = 0x0BCF, HouseSignArt1 = 0x0BD2;
    static readonly string[] HouseWords = { "decay", "condition", "repair", "worn", "ruin", "collaps", "refresh", "owner", "house" };
    readonly System.Collections.Generic.Dictionary<uint, int[]> lookItems = new System.Collections.Generic.Dictionary<uint, int[]>();  // x, y, graphic
    readonly System.Collections.Generic.Dictionary<uint, uint> wornBy = new System.Collections.Generic.Dictionary<uint, uint>();  // item, mobile
    readonly System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<uint>> contents = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<uint>>();
    readonly System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<int>> contentAmounts = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<int>>();  // box, amounts in contents order
    readonly System.Collections.Generic.Dictionary<uint, object[]> offers = new System.Collections.Generic.Dictionary<uint, object[]>();  // name, price, vendor
    System.DateTime offersAt;
    // A buy (client 0x3B: vendor, flag, then layer, serial, amount per item) or a sell (client 0x9F: vendor, count, then
    // serial, amount per item), priced from the offer the server made, recorded per item.
    void Traded(GameLink k, byte[] p) {
        if (Atlas == null || p.Length < 8) return;
        bool buy = p[0] == 0x3B; uint vendor = U32(p, 3); int at = buy ? 8 : 9, step = buy ? 7 : 6;
        int vx = 0, vy = 0; string vname = ""; foreach (var n in Atlas.Visible()) if (n.Serial == vendor) { vx = n.X; vy = n.Y; vname = n.Name; }
        for (; at + step <= p.Length; at += step) {
            uint s = buy ? U32(p, at + 1) : U32(p, at); int amount = buy ? (p[at + 5] << 8) | p[at + 6] : (p[at + 4] << 8) | p[at + 5];
            object[] o; lock (offers) offers.TryGetValue(s, out o);
            var t = new AtlasTrade { At = System.DateTime.Now, Kind = buy ? "buy" : "sell", Vendor = vname, VendorSerial = vendor, X = vx, Y = vy, Item = o != null ? (string)o[0] : "item " + s.ToString("X"), Amount = amount, Price = o != null ? (int)o[1] : 0 };
            // A sale counts when the server deletes the item (uo1998 23:07:32: Clement answered a six-item sell with speech
            // and took nothing; the five-item sell at 23:07:36 deleted each one).
            if (buy) Atlas.Trade(t); else lock (pendingSales) pendingSales[s] = t;
        }
    }
    public const int SaleConfirmMs = 5000;
    readonly System.Collections.Generic.Dictionary<uint, AtlasTrade> pendingSales = new System.Collections.Generic.Dictionary<uint, AtlasTrade>();
    void SaleDeleted(uint s) {
        AtlasTrade t; lock (pendingSales) { if (!pendingSales.TryGetValue(s, out t)) return; pendingSales.Remove(s); }
        if ((System.DateTime.Now - t.At).TotalMilliseconds > SaleConfirmMs || Atlas == null) return;
        Atlas.Trade(t);
        if (Smith != null && !t.Item.StartsWith("item ")) { string q = Smith.QualityOf(s); if (q != null || Smith.CostOf(t.Item) > 0) Smith.Sold(t.Vendor, t.Item, q ?? "unknown", t.Amount, t.Price); }
        if (Tailor != null && !t.Item.StartsWith("item ")) { string q = Tailor.QualityOf(s); if (q != null || Tailor.CostOf(t.Item) > 0) Tailor.Sold(t.Vendor, t.Item, q ?? "unknown", t.Amount, t.Price); }
        if (!t.Item.StartsWith("item ")) foreach (var c in Crafts) if (c.Book != null && c.Book.CostOf(t.Item) > 0) c.Book.Sold(t.Vendor, t.Item, "unknown", t.Amount, t.Price);
    }
    // Names for the atlas: a single click (0x09) on each new unnamed human within LookReach, at most one per LookMs, as a
    // player clicking would; the server answers with the name label. Off with the "look" setting.
    public const int LookReach = 14;
    int LookMs { get { return Ms("look"); } }
    public bool AutoLook { get { return Cfg.Get("look", "on") == "on"; } set { Cfg.Set("look", value ? "on" : "off"); } }
    readonly System.Collections.Generic.HashSet<uint> looked = new System.Collections.Generic.HashSet<uint>();
    readonly System.Collections.Generic.Dictionary<uint, int[]> lookCandidates = new System.Collections.Generic.Dictionary<uint, int[]>();
    void LookLoop() {
        while (true) {
            System.Threading.Thread.Sleep(LookMs);
            var k = link; if (k == null || k.Closed || k.Halted != null || !AutoLook || Atlas == null) continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null) continue;
            int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
            uint pick = 0;
            lock (lookCandidates) {
                foreach (var kv in lookCandidates) if (!looked.Contains(kv.Key) && System.Math.Max(System.Math.Abs(kv.Value[0] - px), System.Math.Abs(kv.Value[1] - py)) <= LookReach && Atlas.NeedsName(kv.Key)) { pick = kv.Key; break; }
                if (pick != 0) { looked.Add(pick); lookCandidates.Remove(pick); if (looked.Count > 10000) looked.Clear(); }
            }
            if (pick == 0) lock (lookItems) foreach (var kv in lookItems) if (!looked.Contains(kv.Key) && System.Math.Max(System.Math.Abs(kv.Value[0] - px), System.Math.Abs(kv.Value[1] - py)) <= LookReach) { pick = kv.Key; looked.Add(pick); break; }
            // On a trip, every creature in reach is clicked once for its name.
            if (pick == 0 && trip != null) lock (trip) foreach (var m in trip.Mobs.Values) {
                string n; lock (k.State.Gate) k.State.Names.TryGetValue(m.Serial, out n);
                if (m.Person || !string.IsNullOrEmpty(n) || looked.Contains(m.Serial) || System.Math.Max(System.Math.Abs(m.X - px), System.Math.Abs(m.Y - py)) > LookReach) continue;
                pick = m.Serial; looked.Add(pick); break;
            }
            if (pick == 0) continue;
            if (!System.Threading.Monitor.TryEnter(actionGate)) continue;
            try { k.Look(pick); } finally { System.Threading.Monitor.Exit(actionGate); }
        }
    }
    void SeenPerson(GameLink k, uint s, int body, int x, int y, int z, int noto) {
        uint me; string known; lock (k.State.Gate) { me = k.State.Player; if (!k.State.Names.TryGetValue(s, out known)) known = ""; }
        if (s == me) return;
        if (Atlas != null) {
            Atlas.Seen(s, body, x, y, known, Criminal(noto));
            if (Atlas.NeedsName(s)) lock (lookCandidates) { if (lookCandidates.Count > 2000) lookCandidates.Clear(); lookCandidates[s] = new[] { x, y }; }
        }
        if (body < 0x190 || body > 0x193) { SeenCreature(s, body, x, y); return; }
        lock (people) {
            Person p; var now = System.DateTime.Now;
            if (!people.TryGetValue(s, out p)) { if (people.Count >= PeopleKeep) return; people[s] = p = new Person { Serial = s, First = now }; }
            p.Body = body; p.X = x; p.Y = y; p.Z = z; p.Last = now;
            if (!p.NotoKnown) { p.Noto = noto; p.NotoKnown = true; }
            else if (noto != p.Noto) { Act(p, "notoriety " + Notoriety(p.Noto) + " to " + Notoriety(noto)); p.Noto = noto; }
            peopleChanged = true;
        }
    }
    // uo1998 sends the 1998 server's raw notoriety, a signed karma-like value -127 to 127 (OUO mobile.c
    // CMobile_SetNotoriety clamps it there; NotoValueToLevel groups it by 24): Kwik 0x01, an orc 0x81 (-127). The UOAIX
    // shard sends the 1.25 highlight value 1 to 7.
    bool RawNotoriety { get { return Server.IndexOf("uo1998", System.StringComparison.OrdinalIgnoreCase) >= 0; } }
    string Notoriety(int n) {
        if (RawNotoriety) return n.ToString("+0;-0;0") + " (level " + (n < 0 ? "-" : "") + (System.Math.Abs(n) / 24) + (n < 0 ? ", negative: the criminal side" : "") + ")";
        return n >= 0 && n < Notorieties.Length ? Notorieties[n] : "notoriety " + n;
    }
    bool Criminal(int n) { return RawNotoriety ? n < 0 : n == 4 || n == 6; }
    // Called with people locked. The same activity again within a minute counts as a repeat, not a new line.
    void Act(Person p, string what) {
        var now = System.DateTime.Now;
        if (what == p.LastAct && p.Acts.Count > 0 && (now - p.Last).TotalSeconds < 60) { p.Repeats++; p.Acts[p.Acts.Count - 1] = p.Acts[p.Acts.Count - 1].Split(new[] { " (x" }, System.StringSplitOptions.None)[0] + " (x" + (p.Repeats + 1) + ")"; }
        else { p.Acts.Add(now.ToString("HH:mm:ss") + " at " + p.X + "," + p.Y + ": " + what); p.LastAct = what; p.Repeats = 0; if (p.Acts.Count > ActsKeep) p.Acts.RemoveAt(0); }
        p.Last = now; peopleChanged = true;
    }
    void ActOn(uint s, string what) { lock (people) { Person p; if (people.TryGetValue(s, out p)) Act(p, what); } }
    void People(GameLink k, byte[] p) {
        switch (p[0]) {
            case 0x78:
                if (p.Length >= 19) {
                    uint m = U32(p, 3) & 0x7FFFFFFF;
                    SeenPerson(k, m, (p[7] << 8) | p[8], (p[9] << 8) | p[10], (p[11] << 8) | p[12], (sbyte)p[13], (sbyte)p[18]);
                    // Worn items: serial, graphic (hue follows when 0x8000), layer, to a zero serial; a vendor's stock pack
                    // (layer 0x1A) leads a buy list back to its vendor (uo1998: Marlene 00280EC0 wears 40280EC1).
                    // uo1998's click label is the bare name ("Marlene", no title), so a worn shop pack (layers 0x1A-0x1C,
                    // which no player wears) is what types a vendor there.
                    bool shop = false;
                    lock (wornBy) {
                        if (wornBy.Count > 20000) wornBy.Clear();
                        for (int at = 19; at + 4 <= p.Length; ) { uint s = U32(p, at); if (s == 0 || at + 7 > p.Length) break; int g = (p[at + 4] << 8) | p[at + 5]; if (p[at + 6] >= 0x1A && p[at + 6] <= 0x1C) shop = true; wornBy[s & 0x7FFFFFFF] = m; at += 7 + ((g & 0x8000) != 0 ? 2 : 0); }
                    }
                    if (shop && Atlas != null) Atlas.ShopPack(m);
                }
                break;
            case 0x77: if (p.Length >= 17) SeenPerson(k, U32(p, 1), (p[5] << 8) | p[6], (p[7] << 8) | p[8], (p[9] << 8) | p[10], (sbyte)p[11], (sbyte)p[16]); break;
            case 0x2F: if (p.Length >= 10) { uint a = U32(p, 2), d = U32(p, 6); ActOn(a, "fighting " + MobileName(k, d)); ActOn(d, "fought by " + MobileName(k, a)); } break;
            case 0x6E: if (p.Length >= 7) { string w = Action((p[5] << 8) | p[6]); if (w != null) ActOn(U32(p, 1), w); } break;
            case 0xAF: if (p.Length >= 5) { ActOn(U32(p, 1), "died"); lock (creatures) { Creature c; if (creatures.TryGetValue(U32(p, 1), out c)) { c.Died = true; peopleChanged = true; } } } break;
            case 0x11: if (Atlas != null && p.Length >= 37) Atlas.Named(U32(p, 3), System.Text.Encoding.ASCII.GetString(p, 7, 30).TrimEnd('\0').Trim()); break;
            // A world item: serial (amount follows when its top bit is set), graphic (stack byte follows when 0x8000), x, y.
            case 0x1A:
                if (Atlas != null && p.Length >= 13) {
                    uint s = U32(p, 3); int at = 7; if ((s & 0x80000000) != 0) at += 2; s &= 0x7FFFFFFF;
                    int g = (p[at] << 8) | p[at + 1]; at += 2; if ((g & 0x8000) != 0) at++; g &= 0x7FFF;
                    if (at + 4 > p.Length) break; int x = ((p[at] << 8) | p[at + 1]) & 0x7FFF, y = ((p[at + 2] << 8) | p[at + 3]) & 0x3FFF;
                    if ((g & 0x4000) != 0) Atlas.Place(s, "House", g, x, y);
                    else if (IsAnvil(g) || IsSmithForge(g)) Atlas.Place(s, IsAnvil(g) ? "Anvil" : "Forge", g, x, y);
                    else if ((g == GuildstoneArt || (g >= HouseSignArt0 && g <= HouseSignArt1)) && !Atlas.HasPlace(s)) lock (lookItems) { if (lookItems.Count > 500) lookItems.Clear(); lookItems[s] = new[] { x, y, g }; }
                }
                break;
            // A container's contents in order (serial, graphic, a byte, amount, x, y, container, hue: 19 bytes each); the
            // buy list names the same contents in the same order (OUO MakePacket_SHOP_DATA walks container->contents).
            case 0x3C:
                if (p.Length >= 5) {
                    int n = (p[3] << 8) | p[4]; var order = new System.Collections.Generic.List<uint>(); var amounts = new System.Collections.Generic.List<int>(); uint box = 0;
                    for (int i = 0; i < n && 5 + i * 19 + 19 <= p.Length; i++) { int at = 5 + i * 19; order.Add(U32(p, at)); amounts.Add((p[at + 7] << 8) | p[at + 8]); box = U32(p, at + 13); }
                    if (box != 0) lock (contents) { if (contents.Count > 200) { contents.Clear(); contentAmounts.Clear(); } contents[box] = order; contentAmounts[box] = amounts; }
                }
                break;
            // The sell list: vendor, count, then serial, graphic, hue, amount, price (u16 each after the serial), name length
            // (u16) and name (OUO MakePacket_SHOP_SELL).
            case 0x9E:
                if (p.Length >= 9) {
                    uint vendor = U32(p, 3); int n = (p[7] << 8) | p[8], at = 9;
                    lock (offers) {
                        offers.Clear();
                        for (int i = 0; i < n && at + 14 <= p.Length; i++) {
                            uint s = U32(p, at); int price = (p[at + 10] << 8) | p[at + 11], len = (p[at + 12] << 8) | p[at + 13];
                            if (at + 14 + len > p.Length) break;
                            offers[s] = new object[] { System.Text.Encoding.ASCII.GetString(p, at + 14, len).TrimEnd('\0'), price, vendor }; at += 14 + len;
                        }
                    }
                }
                break;
            // The buy list (OUO packet_manager.c MakePacket_SHOP_DATA): stock container, count, then price (u32), name
            // length (u8, with its NUL) and name per item. The container is worn by the vendor (layer 0x1A).
            case 0x74:
                if (Atlas != null && p.Length >= 8 && p[7] > 0) {
                    uint box = U32(p, 3); int count = p[7], at = 8; var items = new System.Collections.Generic.List<string>();
                    // The quantity is the stock container's amount for the item (0x3C, sent just before), whose order the
                    // list reverses (StockIndex).
                    System.Collections.Generic.List<int> amounts; lock (contents) contentAmounts.TryGetValue(box, out amounts);
                    for (int i = 0; i < count && at + 5 <= p.Length; i++) {
                        uint price = U32(p, at); int len = p[at + 4]; if (at + 5 + len > p.Length) break;
                        int si = StockIndex(i, count, amounts != null ? amounts.Count : 0);
                        items.Add(System.Text.Encoding.ASCII.GetString(p, at + 5, len).TrimEnd('\0') + ": " + price + " gp" + (amounts != null && si >= 0 && si < amounts.Count ? " x " + amounts[si] : "")); at += 5 + len;
                    }
                    uint vendor = 0; lock (wornBy) wornBy.TryGetValue(box, out vendor);
                    if (vendor == 0) lock (k.State.Gate) { AssistItem it; if (k.State.Items.TryGetValue(box, out it)) vendor = it.Mobile != 0 ? it.Mobile : it.Container; }
                    if (vendor != 0) Atlas.Stock(vendor, items);
                    // Prices by serial for the buy that may follow.
                    System.Collections.Generic.List<uint> order; lock (contents) contents.TryGetValue(box, out order);
                    lock (offers) {
                        var now = System.DateTime.Now; if ((now - offersAt).TotalMilliseconds >= AssistAtlas.StockJoinMs) offers.Clear(); offersAt = now; at = 8;
                        for (int i = 0; i < count && at + 5 <= p.Length; i++) {
                            uint price = U32(p, at); int len = p[at + 4]; if (at + 5 + len > p.Length) break;
                            int si = StockIndex(i, count, order != null ? order.Count : 0);
                            if (order != null && si >= 0 && si < order.Count) offers[order[si]] = new object[] { System.Text.Encoding.ASCII.GetString(p, at + 5, len).TrimEnd('\0'), (int)price, vendor };
                            at += 5 + len;
                        }
                    }
                }
                break;
            case 0x1C:
                // A label (type 6, the answer to a single click) carries the name as its text; speech names its speaker.
                if (Atlas != null && p.Length > 44 && U32(p, 3) != 0xFFFFFFFF) {
                    uint s = U32(p, 3); string label = (p[9] == 6 ? System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44) : System.Text.Encoding.ASCII.GetString(p, 14, 30)).TrimEnd('\0').Trim();
                    // Speech names its speaker without the title ("Marlene" for "Marlene the armourer"): it never replaces a name.
                    if (p[9] == 6) Atlas.Named(s, label); else Atlas.NamedIfNone(s, label);
                    int[] item; lock (lookItems) { if (lookItems.TryGetValue(s, out item)) lookItems.Remove(s); }
                    if (item != null && p[9] == 6) {
                        if (item[2] == GuildstoneArt && label.IndexOf("guild", System.StringComparison.OrdinalIgnoreCase) >= 0) { Atlas.Place(s, "Guildstone", item[2], item[0], item[1]); Atlas.PlaceName(s, label); }
                        else if (item[2] != GuildstoneArt) { uint house = Atlas.HouseNear(item[0], item[1], 16); if (house != 0) Atlas.PlaceName(house, label); }
                    }
                }
                // A system line about a house's state while the player stands by one is kept as that house's details.
                if (Atlas != null && p.Length > 44 && U32(p, 3) == 0xFFFFFFFF) {
                    string text = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                    bool about = false; foreach (var w in HouseWords) if (text.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0) about = true;
                    if (about) {
                        int px, py; lock (k.State.Gate) { px = k.State.X; py = k.State.Y; }
                        uint house = Atlas.HouseNear(px, py, 16); if (house != 0) Atlas.PlaceDetail(house, text);
                    }
                }
                if (p.Length > 44 && (p[9] == 0 || p[9] == 2 || p[9] == 8 || p[9] == 9)) {
                    string text = System.Text.Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                    ActOn(U32(p, 3), (p[9] == 2 ? "emoted " : p[9] == 8 ? "whispered " : p[9] == 9 ? "yelled " : "said ") + "\"" + text + "\"");
                }
                break;
        }
    }
    // The player's corpses (Damian, 2026-10-15: "and if you die, a marker for your corpse in the map"): the server's 0x2C
    // with flag 0 (OUO packet_manager.c MakePacket_DEATH, the death screen) marks the player's tile at that moment.
    public sealed class Corpse { public int X, Y; public System.DateTime At; }
    public const int CorpsesKeep = 5;
    readonly System.Collections.Generic.List<Corpse> corpses = new System.Collections.Generic.List<Corpse>();
    void PlayerDied(GameLink k) {
        int x, y; lock (k.State.Gate) { x = k.State.X; y = k.State.Y; }
        lock (corpses) { corpses.Add(new Corpse { X = x, Y = y, At = System.DateTime.Now }); if (corpses.Count > CorpsesKeep) corpses.RemoveAt(0); }
        k.Tell("Your corpse lies at " + x + "," + y + " (marked on the assist's map).", 0x0026);
        Note("Died at " + x + "," + y);
        // Nothing automatic runs while dead (Damian, 2026-10-15: "be sure none of these auto actions or loops run while
        // dead"): the running loop and the mill, spin and weave run stop; GameLink refuses the assist's own packets.
        stop = true; workStop = true;
        PlotHealers(k, x, y);
    }
    // At death, the way to a healer (Damian, 2026-10-15: "mark the corpse and then plot a course for the nearest known
    // healer and last seen wandering healer"): the atlas's nearest healer who is not a wanderer and its most recently seen
    // wandering healer, told in the journal and drawn on the map from the player while the player is dead. Straight
    // lines; the atlas holds no paths.
    public sealed class Waypoint { public int X, Y; public string Label; }
    readonly System.Collections.Generic.List<Waypoint> course = new System.Collections.Generic.List<Waypoint>();
    public volatile bool ShowMapNow;
    static readonly string[] Compass = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
    public static string Heading(int dx, int dy) {
        if (dx == 0 && dy == 0) return "here";
        double a = System.Math.Atan2(dx, -dy) * 180 / System.Math.PI; if (a < 0) a += 360;
        return Compass[(int)((a + 22.5) / 45) % 8];
    }
    void PlotHealers(GameLink k, int x, int y) {
        AtlasNpc near = null, wander = null; int nd = int.MaxValue;
        if (Atlas != null) foreach (var n in Atlas.Visible()) {
            if (n.Type != "Healer") continue;
            if (n.Sub.Contains("wandering")) { if (wander == null || n.Last > wander.Last) wander = n; continue; }
            int d = System.Math.Max(System.Math.Abs(n.X - x), System.Math.Abs(n.Y - y)); if (d < nd) { nd = d; near = n; }
        }
        lock (course) {
            course.Clear();
            if (near != null) course.Add(new Waypoint { X = near.X, Y = near.Y, Label = "healer " + near.Name });
            if (wander != null) course.Add(new Waypoint { X = wander.X, Y = wander.Y, Label = "wandering healer " + wander.Name + " (seen " + wander.Last.ToString("HH:mm") + ")" });
        }
        if (near == null && wander == null) { k.Tell("No healer is known yet: the assist learns healers as you see them.", 0x002B); return; }
        foreach (var n in new[] { near, wander }) {
            if (n == null) continue;
            int d = System.Math.Max(System.Math.Abs(n.X - x), System.Math.Abs(n.Y - y));
            k.Tell((n == wander ? "Last seen wandering healer " : "Nearest healer ") + n.Name + ": " + d + " tiles " + Heading(n.X - x, n.Y - y) + ", at " + n.X + "," + n.Y + (n == wander ? " (seen " + n.Last.ToString("HH:mm") + ")" : "") + ".", 0x0044);
        }
        ShowMapNow = true;
    }
    // The course while the player is dead; nothing once alive again.
    public Waypoint[] Course() {
        var k = link; if (k == null || !k.State.Dead) { lock (course) course.Clear(); return new Waypoint[0]; }
        lock (course) return course.ToArray();
    }
    public Corpse[] Corpses() { lock (corpses) return corpses.ToArray(); }
    public void ClearCorpses() { lock (corpses) corpses.Clear(); }
    // For the map: x, y and 1 when on the criminal side, of every person seen in the last PeopleOnMapMs.
    public const int PeopleOnMapMs = 60000;
    public int[][] PeoplePositions() {
        var list = new System.Collections.Generic.List<int[]>(); var now = System.DateTime.Now;
        lock (people) foreach (var p in people.Values) if ((now - p.Last).TotalMilliseconds < PeopleOnMapMs) list.Add(new[] { p.X, p.Y, Criminal(p.Noto) ? 1 : 0 });
        return list.ToArray();
    }
    void WritePeopleReport() {
        var k = link; if (k == null || !peopleChanged || PeopleReportPath.Length == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# People seen, " + Server.TrimEnd('.') + ", session from " + System.DateTime.Now.ToString("yyyy-MM-dd"));
        sb.AppendLine(); sb.AppendLine("Human-bodied mobiles the server showed this client (players and human NPCs alike; the 1.25 wire does not tell them apart).");
        lock (people) {
            peopleChanged = false;
            var list = new System.Collections.Generic.List<Person>(people.Values); list.Sort((a, b) => b.Last.CompareTo(a.Last));
            foreach (var p in list) {
                string name; lock (k.State.Gate) { if (!k.State.Names.TryGetValue(p.Serial, out name)) name = ""; }
                if (name.Length > 0) p.Name = name;
                sb.AppendLine(); sb.AppendLine("## " + (p.Name.Length > 0 ? p.Name : "unnamed") + " (" + p.Serial.ToString("X") + (p.Body >= 0x192 ? ", ghost" : "") + ")");
                sb.AppendLine("- seen " + p.First.ToString("HH:mm:ss") + " to " + p.Last.ToString("HH:mm:ss") + ", last at " + p.X + "," + p.Y + "," + p.Z);
                sb.AppendLine("- notoriety: " + Notoriety(p.Noto) + (Criminal(p.Noto) ? " (CRIMINAL)" : ""));
                foreach (var a in p.Acts) sb.AppendLine("- " + a);
            }
        }
        // Creatures by kind, most seen first; each one's place where last seen (and first, when it moved), newest first.
        var kinds = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Creature>>();
        lock (creatures) foreach (var c in creatures.Values) {
            string name = MobileName(k, c.Serial);
            System.Collections.Generic.List<Creature> l; if (!kinds.TryGetValue(name, out l)) kinds[name] = l = new System.Collections.Generic.List<Creature>(); l.Add(c);
        }
        if (kinds.Count > 0) {
            sb.AppendLine(); sb.AppendLine("# Creatures seen");
            var order = new System.Collections.Generic.List<string>(kinds.Keys); order.Sort((a, b) => kinds[b].Count.CompareTo(kinds[a].Count));
            foreach (var name in order) {
                var l = kinds[name]; l.Sort((a, b) => b.Last.CompareTo(a.Last)); int died = 0; foreach (var c in l) if (c.Died) died++;
                sb.AppendLine(); sb.AppendLine("## " + name + ": " + l.Count + " seen" + (died > 0 ? ", " + died + " died" : ""));
                for (int i = 0; i < l.Count && i < PlacesShown; i++) {
                    var c = l[i];
                    sb.AppendLine("- " + c.Last.ToString("HH:mm") + " at " + c.X + "," + c.Y + (c.X != c.X0 || c.Y != c.Y0 ? " (first at " + c.X0 + "," + c.Y0 + " " + c.First.ToString("HH:mm") + ")" : "") + (c.Died ? ", died" : ""));
                }
                if (l.Count > PlacesShown) sb.AppendLine("- and " + (l.Count - PlacesShown) + " more");
            }
        }
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PeopleReportPath)); System.IO.File.WriteAllText(PeopleReportPath, sb.ToString()); } catch (System.Exception e) { Note("People report not written: " + e.Message); }
    }

    // ---------- resource counts ----------
    public string Counts() {
        var k = link; if (k == null) return "no game connection";
        var sums = new System.Collections.Generic.Dictionary<string, int>(); var reagents = Reagents();
        lock (k.State.Gate) {
            var inside = new System.Collections.Generic.HashSet<uint>(); if (k.State.Backpack != 0) inside.Add(k.State.Backpack);
            for (int pass = 0; pass < 8; pass++) foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container)) inside.Add(kv.Key);
            foreach (var kv in k.State.Items) {
                var it = kv.Value; if (it.Layer >= 0 || !inside.Contains(it.Container)) continue;
                foreach (var g in CountGroups) foreach (var h in g[1].Split(',')) if (System.Convert.ToInt32(h, 16) == it.Graphic) { int n; sums.TryGetValue(g[0], out n); sums[g[0]] = n + it.Amount; }
                if (System.Array.IndexOf(reagents, it.Graphic) >= 0) { int n; sums.TryGetValue("reagents", out n); sums["reagents"] = n + it.Amount; }
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var g in CountGroups) { int n; sums.TryGetValue(g[0], out n); sb.Append(g[0] + " " + n + " "); }
        int r; sums.TryGetValue("reagents", out r); sb.Append("reagents " + r);
        return sb.ToString();
    }

    // RunUO Scripts/Items/Resources/Reagents, by graphic.
    static readonly System.Collections.Generic.Dictionary<int, string> ReagentNames = new System.Collections.Generic.Dictionary<int, string> {
        { 0xF7A, "black pearl" }, { 0xF7B, "bloodmoss" }, { 0xF84, "garlic" }, { 0xF85, "ginseng" },
        { 0xF86, "mandrake root" }, { 0xF88, "nightshade" }, { 0xF8C, "sulfurous ash" }, { 0xF8D, "spiders' silk" },
    };
    public static string ReagentName(int g) { string n; return ReagentNames.TryGetValue(g, out n) ? n : null; }
    public sealed class CountRow { public string Name, Group; public int Graphic, Count; public int[] Graphics; }
    // The counted list (Damian, 2026-10-15: "add the same for the resources, a picker and a set list"): settings key
    // "resources", entries name=hex[,hex][@group] joined by |, "-" for an empty list; unset, the count groups and the
    // reagents. An entry with no @group takes GroupOf its first graphic.
    public System.Collections.Generic.List<CountRow> ResourceList() {
        var list = new System.Collections.Generic.List<CountRow>(); string s = Cfg.Get("resources", "");
        if (s.Length == 0) {
            foreach (var g in CountGroups) list.Add(new CountRow { Name = g[0], Group = g[2], Graphics = System.Array.ConvertAll(g[1].Split(','), h => System.Convert.ToInt32(h, 16)) });
            foreach (var r in Reagents()) list.Add(new CountRow { Name = ReagentName(r) ?? r.ToString("X4"), Group = "reagents", Graphics = new[] { r } });
        } else if (s != "-") {
            foreach (var part in s.Split('|')) {
                int eq = part.IndexOf('='); if (eq <= 0) continue;
                string value = part.Substring(eq + 1), group = null; int at = value.IndexOf('@');
                if (at >= 0) { group = value.Substring(at + 1); value = value.Substring(0, at); if (System.Array.IndexOf(ResourceGroups, group) < 0) group = null; }
                try { list.Add(new CountRow { Name = part.Substring(0, eq), Group = group, Graphics = System.Array.ConvertAll(value.Split(','), h => System.Convert.ToInt32(h, 16)) }); } catch (System.FormatException) { }
            }
        }
        // Ore shows the solid pile 0x19B8 (Damian, 2026-10-17: "the art with the more solid pile would be better").
        foreach (var r in list) { r.Graphic = System.Array.IndexOf(r.Graphics, 0x19B8) >= 0 ? 0x19B8 : r.Graphics[0]; if (r.Group == null) r.Group = GroupOf(r.Graphics[0]); }
        return list;
    }
    public void SetResources(System.Collections.Generic.List<CountRow> list) {
        if (list.Count == 0) { Cfg.Set("resources", "-"); return; }
        Cfg.Set("resources", string.Join("|", list.ConvertAll(r => r.Name.Replace("|", " ").Replace("=", " ").Replace("@", " ") + "=" + string.Join(",", System.Array.ConvertAll(r.Graphics, g => g.ToString("X"))) + "@" + (r.Group ?? GroupOf(r.Graphics[0])))));
    }
    // One row per counted entry, summed over the backpack and its containers.
    public System.Collections.Generic.List<CountRow> CountRows() {
        var rows = ResourceList();
        var k = link; if (k == null) return rows;
        lock (k.State.Gate) {
            var inside = new System.Collections.Generic.HashSet<uint>(); if (k.State.Backpack != 0) inside.Add(k.State.Backpack);
            for (int pass = 0; pass < 8; pass++) foreach (var kv in k.State.Items) if (inside.Contains(kv.Value.Container)) inside.Add(kv.Key);
            foreach (var kv in k.State.Items) {
                var it = kv.Value; if (it.Layer >= 0 || !inside.Contains(it.Container)) continue;
                foreach (var r in rows) if (System.Array.IndexOf(r.Graphics, it.Graphic) >= 0) r.Count += it.Amount;
            }
        }
        return rows;
    }
    // Graphics the player can see now: carried (in the backpack or worn) first, then on the ground nearby.
    public System.Collections.Generic.HashSet<int> SeenGraphics(bool carried) {
        var seen = new System.Collections.Generic.HashSet<int>(); var k = link; if (k == null) return seen;
        lock (k.State.Gate) { if (carried) foreach (var kv in k.State.Items) seen.Add(kv.Value.Graphic); else foreach (var kv in k.State.Ground) seen.Add(kv.Value.Graphic); }
        return seen;
    }

    // ---------- skills and stats advice ----------
    int Value(GameLink k, AdviceRow a) {
        lock (k.State.Gate) { if (a.Strength) return k.State.Str; int v; k.State.Skills.TryGetValue(a.Skill + 1, out v); return v; }
    }
    int Threshold(AdviceRow a) { return Cfg.GetInt(Server + "advice." + a.Key, a.At); }
    bool Known(GameLink k, AdviceRow a) { lock (k.State.Gate) return a.Strength ? k.State.Str > 0 : k.State.Skills.ContainsKey(a.Skill + 1); }
    void CheckAdvice(GameLink k) {
        foreach (var a in Advice) {
            if (!Known(k, a)) continue;
            int v = Value(k, a); int before;
            lock (seen) { if (!seen.TryGetValue(a.Key, out before)) { seen[a.Key] = v; continue; } seen[a.Key] = v; }
            int at = Threshold(a);
            if (before < at && v >= at) Note("Advice: " + a.Text + " [" + a.Source + "]");
        }
    }
    // RunUO cliloc 500213 after an equip: the threshold for that item's row is wrong for this server.
    void CheckStrengthRefusal(GameLink k) {
        var j = k.State.JournalSince(k.State.JournalSeq - 1);
        if (j.Count == 0 || j[0].IndexOf("not strong enough to equip", System.StringComparison.OrdinalIgnoreCase) < 0) return;
        if ((System.DateTime.UtcNow - lastEquipAt).TotalSeconds > 5) return;
        var it = Item(k, lastEquip); if (it == null) return;
        int str; lock (k.State.Gate) str = k.State.Str;
        foreach (var a in Advice) if (a.Strength && System.Array.IndexOf(a.Graphics, it.Graphic) >= 0 && Threshold(a) <= str) {
            Cfg.Set(Server + "advice." + a.Key, (str + 1).ToString());
            Note("Advice corrected: this server refused " + it.Graphic.ToString("X4") + " at strength " + str + "; " + a.Key + " is now " + (str + 1));
        }
    }
    public string AdviceNow() {
        var k = link; if (k == null) return "no game connection";
        var sb = new System.Text.StringBuilder();
        foreach (var a in Advice) { int v = Value(k, a), at = Threshold(a); sb.Append((v >= at ? "yes " : "not yet ") + a.Key + " (" + (a.Strength ? v.ToString() : (v / 10.0).ToString("0.0")) + "/" + (a.Strength ? at.ToString() : (at / 10.0).ToString("0.0")) + ") | "); }
        return sb.ToString().TrimEnd(' ', '|');
    }

    // ---------- display options ----------
    public bool Shown(string name) { return Cfg.Get("title." + name, System.Array.IndexOf(TitleDefaults, name) >= 0 ? "on" : "off") == "on"; }
    public void Show(string name, bool on) { Cfg.Set("title." + name, on ? "on" : "off"); }
}
