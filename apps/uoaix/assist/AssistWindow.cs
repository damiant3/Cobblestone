using System; using System.Drawing; using System.Windows.Forms; using System.Runtime.InteropServices;
using System.Collections.Generic; using System.Threading;

// Stage 5 of assist-plan.md: one window docked to the right edge of the client the relay serves, owned by it (so it
// sits directly above the client and minimizes with it), every feature an always-visible panel with its own hotkey on
// its own control. Hotkeys are registered only while the client is the foreground window.
public static class AssistNative {
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int cls, uint reserved);
    // MIB_TCPROW_OWNER_PID rows of 24 bytes: state, local address, local port, remote address, remote port, pid; ports in
    // network order in the low 16 bits (TCP_TABLE_OWNER_PID_ALL = 5, AF_INET = 2).
    public static int TcpPid(int localPort, int remotePort) {
        int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return 0;
            int n = Marshal.ReadInt32(buf);
            for (int i = 0; i < n; i++) {
                IntPtr row = buf + 4 + i * 24;
                if (Port(Marshal.ReadInt32(row, 8)) == localPort && Port(Marshal.ReadInt32(row, 16)) == remotePort) return Marshal.ReadInt32(row, 20);
            }
        } finally { Marshal.FreeHGlobal(buf); }
        return 0;
    }
    static int Port(int v) { return ((v & 0xFF) << 8) | ((v >> 8) & 0xFF); }

    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lp);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point p);
    // Opens a file with its default program (a trip report).
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr hwnd, string verb, string file, string args, string dir, int show);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, int pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    // The visible frame (DWMWA_EXTENDED_FRAME_BOUNDS): GetWindowRect also counts the invisible resize border, about 7
    // pixels a side on Windows 10 and 11, which left a gap between the client and the docked window.
    public static RECT Visible(IntPtr hwnd) {
        RECT r; if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0) return r;
        GetWindowRect(hwnd, out r); return r;
    }
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    // The client's title bar: WM_SETTEXT (0x000C) is marshalled across processes for a top-level window.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendText(IntPtr hwnd, int msg, IntPtr w, string l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder s, int n);
    public static string Title(IntPtr hwnd) { var sb = new System.Text.StringBuilder(512); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); }
    public static void SetTitle(IntPtr hwnd, string text) { SendText(hwnd, 0x000C, IntPtr.Zero, text); }
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);
    // The folder of a process's executable (PROCESS_QUERY_LIMITED_INFORMATION), or null.
    public static string ExeFolder(int pid) {
        IntPtr h = OpenProcess(0x1000, false, pid); if (h == IntPtr.Zero) return null;
        try { var sb = new System.Text.StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? System.IO.Path.GetDirectoryName(sb.ToString()) : null; }
        finally { CloseHandle(h); }
    }

    // The process's largest visible top-level window not owned by a visible window.
    public static IntPtr MainWindow(int pid) {
        IntPtr best = IntPtr.Zero; long area = 0;
        EnumWindows((h, lp) => {
            int p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h) && (GetWindow(h, 4) == IntPtr.Zero || !IsWindowVisible(GetWindow(h, 4)))) {
                RECT r; GetWindowRect(h, out r); long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (a > area) { area = a; best = h; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }
}

public sealed class AssistWindow : Form {
    // The 1.25.32 client's skill order (SKILLS.IDX), 0-based as the skill packets number them from 1.
    public static readonly string[] SkillNames = { "Alchemy", "Anatomy", "Animal Lore", "Item Identification", "Arms Lore", "Parrying",
        "Begging", "Blacksmithy", "Bowcraft/Fletching", "Peacemaking", "Camping", "Carpentry", "Cartography", "Cooking",
        "Detecting Hidden", "Enticement", "Evaluating Intelligence", "Healing", "Fishing", "Forensic Evaluation", "Herding",
        "Hiding", "Provocation", "Inscription", "Lockpicking", "Magery", "Resisting Spells", "Tactics", "Snooping",
        "Musicianship", "Poisoning", "Archery", "Spirit Speak", "Stealing", "Tailoring", "Animal Taming",
        "Taste Identification", "Tinkering", "Tracking", "Veterinary", "Swordsmanship", "Mace Fighting", "Fencing",
        "Wrestling", "Lumberjacking", "Mining" };
    public const int Width0 = 380, MinWidth = 300;

    readonly AssistRelay relay; readonly AssistFeatures f;
    IntPtr client = IntPtr.Zero; int clientPid; IntPtr locHook, fgHook; AssistNative.WinEventProc locProc, fgProc;
    bool keysOn; string capturing;
    readonly Dictionary<string, Button> keyButtons = new Dictionary<string, Button>();
    readonly Dictionary<string, Control> actionControls = new Dictionary<string, Control>();
    readonly Dictionary<string, string> actionText = new Dictionary<string, string>();
    Label status, session, loopState, loopWords, hands, drink, reach, workState, petsCard; ListBox petHistory; Button tripButton; ComboBox loops; NumericUpDown rounds, untilSkill;
    CheckBox pickup; ListBox picks; SortList skills; ListBox notes, chat; TabControl talk; long chatSeen; ListBox logList; Label guardState, bannerText; CheckedListBox watches; Panel banner; FlowLayoutPanel flow, autoFlow; TableLayoutPanel res;
    Button resCount; string resShape = ""; List<Label> resLabels = new List<Label>(); List<PictureBox> resPics = new List<PictureBox>(); List<int> resAt = new List<int>();
    const int ResIcon = 28, ResRow = 32;
    // Resources collapsed: black pearl, mandrake root, bloodmoss.
    static readonly int[] ResSummaryArts = { 0xF7A, 0xF86, 0xF7B };
    readonly List<PictureBox> resSumPics = new List<PictureBox>(); readonly List<Label> resSumCounts = new List<Label>();
    // The reagents' short names, three to a row (Damian, 2026-10-17: "use short names like pearl, moss, mandrake, ash silk").
    static readonly Dictionary<int, string> ShortReagent = new Dictionary<int, string> { { 0xF7A, "pearl" }, { 0xF7B, "moss" }, { 0xF86, "mandrake" }, { 0xF8C, "ash" }, { 0xF8D, "silk" }, { 0xF84, "garlic" }, { 0xF85, "ginseng" }, { 0xF88, "nightshade" } };
    string statLine = "", sessionLine = "";
    // Running lights for the loop and the circuit (Damian, 2026-10-17: "add some green and bold \"running\" indicators, red
    // for stopped, yellow for any unusual skips or unexplained failures"): yellow while an event of the last AttentionMs
    // carries one of AttentionWords; the section's header takes the light's colour, so a collapsed section shows it too.
    Panel loopGroup, circJobGroup; Label loopLight, circLight, goldLine; PictureBox goldPic; FlowLayoutPanel goldRow;
    const int GoldPileArt = 0xEEF, AttentionMs = 300000;
    static readonly string[] AttentionWords = { "FAULT", "UNKNOWN REPLY", "skip", "fail", "refused", "did not", "no way", "not answer", "error" };
    Label Light() { return new Label { AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 0, 0, 0), AutoEllipsis = true }; }
    // The newest event of the last AttentionMs, from the circuit (Circuit, Walk, Survey, Buy) or not, that names a skip or a
    // failure; null when none does.
    string Attention(bool circuit) {
        var now = DateTime.Now.TimeOfDay;
        lock (f.Notes) for (int i = f.Notes.Count - 1; i >= 0; i--) {
            string n = f.Notes[i]; TimeSpan at; if (n.Length < 9 || !TimeSpan.TryParse(n.Substring(0, 8), out at)) continue;
            var age = now - at; if (age < TimeSpan.Zero) age += TimeSpan.FromDays(1); if (age.TotalMilliseconds > AttentionMs) break;
            string text = n.Substring(9); bool mine = text.StartsWith("Circuit:") || text.StartsWith("Walk:") || text.StartsWith("Survey:") || text.StartsWith("Buy:");
            if (mine != circuit && !text.StartsWith("UNKNOWN REPLY") && !text.StartsWith("FAULT")) continue;
            foreach (var w in AttentionWords) if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return text;
        }
        return null;
    }
    void SetLight(Label light, Panel group, bool running, string attention) {
        string text = (running ? "RUNNING" : "STOPPED") + (attention != null ? ":  " + attention : "");
        Color back = attention != null ? Color.FromArgb(240, 200, 40) : running ? Color.FromArgb(40, 150, 60) : Color.FromArgb(190, 40, 40), fore = attention != null ? Color.Black : Color.White;
        if (light.Text != text) light.Text = text; if (light.BackColor != back) light.BackColor = back; if (light.ForeColor != fore) light.ForeColor = fore;
        Button head; Color hc = attention != null ? Color.DarkGoldenrod : running ? Color.Green : Color.DarkRed;
        if (group != null && headOf.TryGetValue(group, out head) && head.ForeColor != hc) head.ForeColor = hc;
    }
    bool LoopRunning() { string s = f.Status; return s.StartsWith("Loop ") && !s.Contains(" done") && !s.Contains(" stopped"); }
    TableLayoutPanel stat; readonly Dictionary<string, Label> statValues = new Dictionary<string, Label>();
    static readonly string[][] StatGroups = {
        new[] { "vitals", "hits", "mana", "stamina" }, new[] { "attributes", "strength", "dexterity", "intelligence" }, new[] { "backpack", "weight", "gold" }, new[] { "combat", "armor", "dps", "vs" },
    };
    // The client's own title ends where TitleMark starts; what follows is the assist's.
    const string TitleMark = "  |  ";
    static readonly string[] ActionGroups = { "Loops", "Timings", "Arm and disarm", "Item pickup", "Mill, spin and weave", "General" };
    readonly Dictionary<Panel, string> pageOf = new Dictionary<Panel, string>();
    string page; Button infoTab, skillsTab, actionsTab, mapTab, shopsTab, pricerTab, makerTab; Label underflow; bool fitting;
    MapView mapView; Label mapInfo; readonly List<Point> trail = new List<Point>();
    const int TrailKeep = 2000;
    NumericUpDown delayBox; CheckBox autoPace; bool settingDelay; Label summary;
    // The session summary under the session details, one line each, grown to its lines (the window does not scroll).
    void Summary() {
        string text = f.SessionSummary(); if (summary.Text == text) return;
        summary.Text = text;
        if (summary.Height != Grown(summary)) Fit();
    }
    // The delay box follows the loop's delay as the bisect moves it, except while the player is editing it.
    void ShowDelay() {
        if (delayBox == null || delayBox.Focused) return;
        int d = Math.Max((int)delayBox.Minimum, Math.Min((int)delayBox.Maximum, f.Delay(Kind())));
        if ((int)delayBox.Value != d) { settingDelay = true; delayBox.Value = d; settingDelay = false; }
    }
    // The Timings panel (Damian, 2026-10-16: "shorter names, tool tips for the full explanation ... 2 colums ... up down
    // nudge should be 100ms ... exando subgroups by functional category"): each action timing on this server, editable,
    // in a collapsible subgroup of two columns; the tooltip carries the explanation and what was seen this session. The
    // magery circles' delays are keyed "circle:<loop name>".
    readonly List<Tuple<string, NumericUpDown, Label, string>> timingRows = new List<Tuple<string, NumericUpDown, Label, string>>();
    readonly Dictionary<string, string> timingTips = new Dictionary<string, string>();
    readonly ToolTip tipper = new ToolTip { AutoPopDelay = 20000 };
    bool settingTiming;
    const int TimingRowH = 24, TimingHeadH = 20, TimingStep = 100;
    int TimingValue(string key) { return key.StartsWith("circle:") ? f.Delay(Array.Find(AssistFeatures.CirclePace, c => c.Name == key.Substring(7))) : f.Ms(key); }
    void TimingGroup(Panel g, string cat, List<string[]> rows) {
        string closedKey = "collapsed.timings." + cat; bool closed = f.Cfg.Get(closedKey, "1") == "1";
        int lines = (rows.Count + 1) / 2;
        var sub = Add(g, new Panel { Height = TimingHeadH + (closed ? 0 : lines * TimingRowH), Margin = new Padding(0) });
        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = lines, Dock = DockStyle.Fill, Visible = !closed, Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < lines; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, TimingRowH));
        var head = new Button { Text = (closed ? "[+] " : "[-] ") + cat, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, Height = TimingHeadH, Dock = DockStyle.Top };
        head.FlatAppearance.BorderSize = 0;
        head.Click += (s, e) => { grid.Visible = !grid.Visible; head.Text = (grid.Visible ? "[-] " : "[+] ") + cat; f.Cfg.Set(closedKey, grid.Visible ? "0" : "1"); sub.Height = TimingHeadH + (grid.Visible ? lines * TimingRowH : 0); Fit(); };
        for (int i = 0; i < rows.Count; i++) {
            string key = rows[i][0];
            var cell = new FlowLayoutPanel { WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var box = new NumericUpDown { Minimum = 0, Maximum = AssistFeatures.TimingMaxMs, Increment = TimingStep, Width = 66, Margin = new Padding(0, 1, 3, 0) };
            box.Value = Math.Min(box.Maximum, TimingValue(key));
            box.ValueChanged += (s, e) => {
                if (settingTiming) return;
                if (key.StartsWith("circle:")) f.SetDelay(Array.Find(AssistFeatures.CirclePace, c => c.Name == key.Substring(7)), (int)box.Value); else f.SetMs(key, (int)box.Value);
            };
            var label = new Label { Text = rows[i][1], AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            cell.Controls.Add(box); cell.Controls.Add(label);
            grid.Controls.Add(cell, i % 2, i / 2);
            timingRows.Add(Tuple.Create(key, box, label, rows[i][2]));
        }
        sub.Controls.Add(grid); sub.Controls.Add(head);
    }
    // The Sounds tab (General): each group's checkbox, its tooltip the sound files in it and how often they played.
    const int GeneralTabsH = 92, SoundRowH = 20;
    readonly List<Tuple<AssistFeatures.SoundGroup, CheckBox>> soundBoxes = new List<Tuple<AssistFeatures.SoundGroup, CheckBox>>();
    readonly Dictionary<CheckBox, string> soundTips = new Dictionary<CheckBox, string>();
    void ShowSounds() {
        foreach (var t in soundBoxes) {
            var names = new List<string>(); foreach (var id in t.Item1.Ids) { string n = UoSounds.Name(id); if (n != null && !names.Contains(n)) names.Add(n); }
            string tip = "Keep these sounds from the client: " + string.Join(", ", names) + ".\nPlayed " + f.Heard(t.Item1) + " times this session.";
            string was; if (!soundTips.TryGetValue(t.Item2, out was) || was != tip) { soundTips[t.Item2] = tip; tipper.SetToolTip(t.Item2, tip); }
        }
    }
    void ShowTimings() {
        foreach (var r in timingRows) {
            if (!r.Item2.Focused) { int v = Math.Max((int)r.Item2.Minimum, Math.Min((int)r.Item2.Maximum, TimingValue(r.Item1))); if ((int)r.Item2.Value != v) { settingTiming = true; r.Item2.Value = v; settingTiming = false; } }
            var t = r.Item1.StartsWith("circle:") ? null : AssistFeatures.TimingOf(r.Item1);
            string tip = t == null ? r.Item4 : r.Item4 + "\n" + f.SeenText(r.Item1) + ". Default " + t.Default + " ms.";
            string was; if (!timingTips.TryGetValue(r.Item1, out was) || was != tip) { timingTips[r.Item1] = tip; tipper.SetToolTip(r.Item2, tip); tipper.SetToolTip(r.Item3, tip); }
        }
    }
    void ShowPage(string p) {
        page = p; f.Cfg.Set("window.page", p);
        foreach (var kv in pageOf) kv.Key.Visible = kv.Value == p;
        if (p == "map.trip") FillTrips(); else if (p == "map.areas") FillAreas();
        Fit();
    }
    // The Map tab's sub tabs: the live map, one trip's report, and the region analysis over every trip.
    void SubTabs(Panel g, string here) {
        var row = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        foreach (var t in new[] { new[] { "Live map", "map" }, new[] { "Trip report", "map.trip" }, new[] { "Area analysis", "map.areas" } }) {
            string to = t[1]; bool on = to == here;
            var b = new Button { Text = t[0], Width = 110, Height = 26, FlatStyle = FlatStyle.Flat, Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular), BackColor = on ? SystemColors.ControlLightLight : SystemColors.Control };
            b.FlatAppearance.BorderColor = on ? SystemColors.Highlight : SystemColors.ControlDark; b.Click += (s, e) => ShowPage(to); row.Controls.Add(b);
        }
    }
    Label loadoutStatus;
    ComboBox tripPick, areaPeriod; TextBox tripText, areaDetail; SortList areaList; Label areaBest; int tripsSeen = -1;
    static readonly KeyValuePair<string, int>[] AreaPeriods = { new KeyValuePair<string, int>("the last 7 days", 7), new KeyValuePair<string, int>("the last 30 days", 30), new KeyValuePair<string, int>("the last 90 days", 90), new KeyValuePair<string, int>("all time", 0) };
    sealed class TripItemView { public TripRecord Trip; public override string ToString() { return Trip.Label(); } }
    void FillTrips() {
        if (tripPick == null) return; tripsSeen = f.TripsWritten;
        string was = tripPick.SelectedItem is TripItemView ? ((TripItemView)tripPick.SelectedItem).Trip.Report : null;
        tripPick.BeginUpdate(); tripPick.Items.Clear(); int keep = 0;
        foreach (var r in TripBook.Load()) { if (r.Report == was) keep = tripPick.Items.Count; tripPick.Items.Add(new TripItemView { Trip = r }); }
        tripPick.EndUpdate();
        if (tripPick.Items.Count > 0) tripPick.SelectedIndex = keep; else tripText.Text = "No trips yet. Every session is a trip from the login, and with no session trip running one records itself from the first step out of guarded land; Begin trip on the live map starts one by hand.";
    }
    void ShowTrip() { var v = tripPick.SelectedItem as TripItemView; if (v != null) tripText.Text = string.Join("\r\n", TripBook.Report(v.Trip)); }
    void FillAreas() {
        if (areaList == null) return; tripsSeen = f.TripsWritten;
        var trips = TripBook.Load(); var grades = TripBook.Grades(trips, AreaPeriods[Math.Max(0, areaPeriod.SelectedIndex)].Value);
        areaBest.Text = grades.Count == 0 ? "No trip with region data in this period." : string.Join("\n", TripBook.BestFor(grades));
        areaList.BeginUpdate(); areaList.Items.Clear();
        foreach (var g in grades) {
            var cells = new[] { g.Area, TripBook.Age(g.Newest), g.Trips.ToString(), TripBook.Level(g), TripBook.Profit(g), TripBook.TrainingGrade(g), TripBook.Spawn(g), TripBook.CloseCalls(g), TripBook.Harvest(g), TripBook.Reagents(g), TripBook.Resist(g), TripBook.Magic(g), TripBook.Taming(g), TripBook.Herding(g), TripBook.FeathersHides(g), TripBook.Foes(g) };
            var detail = new List<string> { g.Area + ": " + g.Trips + " trip" + (g.Trips == 1 ? "" : "s") + ", " + g.Minutes + " min, newest " + TripBook.Age(g.Newest) };
            for (int i = 3; i < cells.Length; i++) if (cells[i].Length > 0) detail.Add(areaList.Columns[i].Text + ": " + cells[i]);
            areaList.Items.Add(new ListViewItem(cells) { Tag = string.Join("\r\n", detail) });
        }
        areaList.EndUpdate(); areaDetail.Text = ""; Fit();
    }
    bool OnPage(Control g) { string p; return pageOf.TryGetValue((Panel)g, out p) && p == page; }
    readonly Dictionary<string, CheckBox> display = new Dictionary<string, CheckBox>();
    static readonly string[] Actions = { "loop", "arm", "pickup", "work", "arm.weapon", "arm.shield", "record" };
    static readonly Keys[] DefaultKeys = { Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8 };

    public static void Start(AssistRelay relay) {
        // A fault in the window is logged and the window goes on: unhandled, it took the relay and the player's session
        // down with no word in the log (2026-10-15 20:41:36).
        var t = new Thread(() => {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => relay.Features.Fault("window: " + e.Exception);
            Application.EnableVisualStyles(); Application.Run(new AssistWindow(relay));
        });
        t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start();
    }

    // The assist never takes focus from the client.
    protected override bool ShowWithoutActivation { get { return true; } }

    AssistWindow(AssistRelay relay) {
        this.relay = relay; f = relay.Features;
        Text = "UOAIX Assist"; FormBorderStyle = FormBorderStyle.SizableToolWindow; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(-32000, -32000, Width0, 600); KeyPreview = true;
        Font = new Font("Segoe UI", 8.5f);
        // The border (Damian, 2026-10-15: "when a loop is running, put a green border around the whole window ... 2 pixel
        // wide ... painted background gray to start so it doesn't relayout ... if it stops for failsafe, make it red"):
        // the form's 2 px padding shows the form's colour; the panels inside keep the control gray.
        // 3 px, and yellow while round zero configures the loop (Damian, 2026-10-15: "make the colored border 3 pixels,
        // not 2. and add yellow to indicate \"configuring\" first step").
        Padding = new Padding(3); BackColor = SystemColors.Control;
        flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, BackColor = SystemColors.Control };
        Controls.Add(flow);
        // The fail-safe banner: shown while the assist is disabled, with the reason and a resume.
        banner = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(200, 40, 40), Visible = false };
        bannerText = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Padding = new Padding(4) };
        var resume = new Button { Dock = DockStyle.Right, Width = 80, Text = "Resume", BackColor = Color.White };
        resume.Click += (s, e) => { if (AssistGuard.Current != null) AssistGuard.Current.Resume(); };
        banner.Controls.Add(bannerText); banner.Controls.Add(resume);
        // Two pages (Damian, 2026-10-15: "if we are in an under-flow condition ... we need a way to indicate that that
        // doesn't scroll. like maybe the whole main panel needs a mode/tab for putting the info in one place and the
        // actions in the other"): Info and Actions tabs, and a bar at the bottom while a page runs past the window.
        page = f.Cfg.Get("window.page", "info"); if (page != "skills" && page != "actions" && page != "map" && page != "map.trip" && page != "map.areas" && page != "shops" && page != "pricer" && page != "circuit" && page != "maker") page = "info";
        // The tabs size to their text and wrap to a second row (Damian, 2026-10-18: "the buttons at the top of the ui should be
        // padding reduced for the text inside, to fit all the buttons on screen. the reset is hidden here").
        var pageTabs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = SystemColors.Control, Padding = new Padding(3, 2, 0, 2) };
        Func<string, Button> tab = t => new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 26), Padding = new Padding(4, 0, 4, 0), Margin = new Padding(2), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
        // Skills on a tab of its own (Damian, 2026-10-18: "we should make skills its own tab entirely").
        infoTab = tab("Info"); skillsTab = tab("Skills"); skillsTab.Click += (s, e) => ShowPage("skills"); actionsTab = tab("Actions"); mapTab = tab("Map"); shopsTab = tab("Shops"); pricerTab = tab("Bags");
        infoTab.Click += (s, e) => ShowPage("info"); actionsTab.Click += (s, e) => ShowPage("actions"); mapTab.Click += (s, e) => ShowPage("map"); shopsTab.Click += (s, e) => { ShowPage("shops"); shopShape = ""; FillShops(); };
        pricerTab.Click += (s, e) => { ShowPage("pricer"); FillPricer(); };
        // "Circuits" (Damian, 2026-10-18: "change tab name Circuit to Circuits"); Maker holds the crafts' value tables
        // (Damian, 2026-10-18: "move smithing and tailoring values to a new top level tab called Maker").
        circuitTab = tab("Circuits"); makerTab = tab("Maker"); makerTab.Click += (s, e) => { ShowPage("maker"); smithShape = ""; tailorShape = ""; craftShape = ""; FillSmith(); FillTailor(); FillCraft(); };
        circuitTab.Click += (s, e) => { ShowPage("circuit"); circShape = ""; FillCircuit(); };
        // The order (Damian, 2026-10-18): "make map the second in the ordering of tabs, then Skills, Maker, Actions, Circuits,
        // Shops, Bags".
        foreach (var b in new[] { infoTab, mapTab, skillsTab, makerTab, actionsTab, circuitTab, shopsTab, pricerTab }) pageTabs.Controls.Add(b);
        var reset = tab("Reset layout"); reset.Margin = new Padding(8, 2, 2, 2);
        new ToolTip().SetToolTip(reset, "Size and place the client again, dock this window, open every section, and reset the map.");
        reset.Click += (s, e) => ResetLayout(); pageTabs.Controls.Add(reset);
        underflow = new Label { Dock = DockStyle.Bottom, Height = 18, Visible = false, BackColor = Color.FromArgb(240, 190, 60), TextAlign = ContentAlignment.MiddleCenter,
            Text = "More below the window's edge (this window does not scroll): collapse a section or switch tabs" };
        Controls.Add(pageTabs); Controls.Add(underflow); Controls.Add(banner); flow.BringToFront(); flow.SizeChanged += (s, e) => Fit();

        // Status (Damian, 2026-10-15: "two columns name: values grouped by vitals, attrs, and other"): every value, always.
        var sg = Group("Status");
        // The name and title large (Damian, 2026-10-18: "make the name and title in status like 18pt font").
        status = Add(sg, new Label { AutoSize = false, Height = 36, Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoEllipsis = true });
        // Laid out as Resources is (Damian, 2026-10-15: "layout for status like resources, denormalized"): the group's name
        // in the first column, level with its first values, and each value one "name  value" cell, StatColumns to a row
        // (Damian, 2026-10-18: "make the vitals and attrs and others 3 wide columns, there is room").
        const int StatColumns = 3;
        stat = Add(sg, new TableLayoutPanel { ColumnCount = 1 + StatColumns, Height = 4 });
        stat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); for (int c = 0; c < StatColumns; c++) stat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / StatColumns));
        int srow = 0;
        foreach (var grp in StatGroups) {
            stat.Controls.Add(new Label { AutoSize = true, Text = grp[0], Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 1, 0, 0) }, 0, srow);
            for (int i = 1; i < grp.Length; i++) {
                var v = new Label { AutoSize = true, Margin = new Padding(0, 1, 0, 0) }; stat.Controls.Add(v, 1 + (i - 1) % StatColumns, srow + (i - 1) / StatColumns); statValues[grp[i]] = v;
                if (grp[i] == "dps" || grp[i] == "vs") { string field = grp[i]; v.Cursor = Cursors.Hand; v.Click += (s, e) => PickCombat(field); tipper.SetToolTip(v, field == "dps" ? "Click to pick the defender." : "Click to pick the caster and the spell."); }
            }
            int lines = (grp.Length - 1 + StatColumns - 1) / StatColumns; for (int r = 0; r < lines; r++) stat.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            srow += lines;
        }
        stat.RowCount = srow; stat.Height = srow * 18 + 4;
        Summarize(sg, () => statLine);
        // Session (Damian, 2026-10-15: "add session status, time since login, where you at overview, track and show the exact
        // tile info, any zone you are in, guarded/not guarded, home, not home, show criminal state").
        var sessionGroup = Group("Session");
        session = Add(sessionGroup, new Label { AutoSize = false, Height = 80 });
        summary = Add(sessionGroup, new Label { AutoSize = false, Height = 4 });
        growers.Add(session); growers.Add(summary);
        Summarize(sessionGroup, () => sessionLine);
        // Pets and the mount (Damian, 2026-10-15: "add pet and mount happiness").
        var petGroup = Group("Pets and mount");
        petsCard = Add(petGroup, new Label { AutoSize = false, Height = 4 }); growers.Add(petsCard);
        Summarize(petGroup, () => FirstLine(petsCard.Text));
        petHistory = Add(petGroup, new ListBox { Height = 90, HorizontalScrollbar = true });

        var g = Group("Loops");
        Summarize(g, () => f.Status);
        loops = Add(g, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }); Hold(loops);
        foreach (var l in AssistFeatures.Loops) loops.Items.Add(l.Name);
        loops.SelectedIndex = Math.Max(0, Array.FindIndex(AssistFeatures.Loops, l => l.Name == f.Cfg.Get("window.loop", "Mining (2 tools)")));
        loops.SelectedIndexChanged += (s, e) => { var k = Kind(); f.Cfg.Set("window.loop", k.Name); rounds.Value = f.Cfg.GetInt("rounds." + k.Name, 10); };
        var row = Add(g, new FlowLayoutPanel { Height = 28, WrapContents = false });
        row.Controls.Add(new Label { Text = "times", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
        rounds = new NumericUpDown { Minimum = 1, Maximum = 9999, Width = 60, Value = f.Cfg.GetInt("rounds." + Kind().Name, 10) }; row.Controls.Add(rounds);
        row.Controls.Add(new Label { Text = "or until skill", AutoSize = true, Margin = new Padding(8, 6, 2, 0) });
        untilSkill = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 0.1m, Width = 60 }; row.Controls.Add(untilSkill);
        // Smelting only (Damian, 2026-10-16: "add option \"whole pile\" and take the loop count from the pile size after the
        // first example run").
        var whole = new CheckBox { Text = "whole pile", AutoSize = true, Checked = f.WholePile, Visible = Kind().Smelt > 0, Margin = new Padding(8, 5, 0, 0) };
        tipper.SetToolTip(whole, "Smelt the whole pile: after round zero the loop runs once for each ore left in the pile, in place of the times box.");
        whole.CheckedChanged += (s, e) => f.WholePile = whole.Checked; row.Controls.Add(whole);
        loops.SelectedIndexChanged += (s, e) => whole.Visible = Kind().Smelt > 0;
        // Tailoring only (Damian, 2026-10-16: "the tailor loop should have a \"or all cloth is used\"").
        var allCloth = new CheckBox { Text = "all cloth", AutoSize = true, Checked = f.AllCloth, Visible = Kind().Tailor > 0, Margin = new Padding(8, 5, 0, 0) };
        tipper.SetToolTip(allCloth, "Sew until the cloth and the bolts are used up, in place of the times box.");
        allCloth.CheckedChanged += (s, e) => f.AllCloth = allCloth.Checked; row.Controls.Add(allCloth);
        loops.SelectedIndexChanged += (s, e) => allCloth.Visible = Kind().Tailor > 0;
        // The loop's delay on this server, shown and overridden here; auto-detect turns the bisect on and off.
        var drow2 = Add(g, new FlowLayoutPanel { Height = 28, WrapContents = false });
        drow2.Controls.Add(new Label { Text = "delay ms", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
        // A smithing loop has no delay (0): the box shows its floor rather than refusing the value.
        delayBox = new NumericUpDown { Minimum = AssistFeatures.MinDelayMs / 2, Maximum = AssistFeatures.MaxDelayMs, Increment = 50, Width = 70 };
        delayBox.Value = Math.Max(delayBox.Minimum, Math.Min(delayBox.Maximum, f.Delay(Kind()))); drow2.Controls.Add(delayBox);
        delayBox.ValueChanged += (s, e) => { if (!settingDelay && (int)delayBox.Value != f.Delay(Kind())) f.SetDelay(Kind(), (int)delayBox.Value); };
        autoPace = new CheckBox { Text = "auto-detect (bisect on \"you must wait\")", AutoSize = true, Checked = f.AutoPace, Margin = new Padding(8, 5, 0, 0) };
        autoPace.CheckedChanged += (s, e) => f.AutoPace = autoPace.Checked; drow2.Controls.Add(autoPace);
        AddAction("loop", g, "Start or stop the loop", () => ToggleLoop());
        loopGroup = g; loopLight = Add(g, Light());
        loopState = Add(g, new Label { AutoSize = false, Height = 16, Font = new Font(Font, FontStyle.Bold) });
        loopWords = Add(g, new Label { AutoSize = false, Height = 30, Font = new Font(Font, FontStyle.Bold) });

        g = Group("Timings");
        var tune = Add(g, new CheckBox { Text = "auto-tune measured timings", AutoSize = false, Height = 20, Margin = new Padding(3, 0, 3, 0), Checked = f.AutoTune });
        tipper.SetToolTip(tune, "A measured timing records how long the server took each time. Auto-tune raises it at once when the server is later, and otherwise follows 1.5 x the longest of the last " + AssistFeatures.TuneKeep + " plus " + AssistFeatures.TuneSlackMs + " ms, never under half its default.");
        tune.CheckedChanged += (s, e) => f.AutoTune = tune.Checked;
        var cats = new List<string>(); foreach (var t in AssistFeatures.Timings) if (!cats.Contains(t.Group)) cats.Add(t.Group);
        foreach (var cat in cats) {
            var rows = new List<string[]>();
            foreach (var t in AssistFeatures.Timings) if (t.Group == cat) rows.Add(new[] { t.Key, t.Name, t.Text });
            if (cat == "Magery") foreach (var c in AssistFeatures.CirclePace) rows.Add(new[] { "circle:" + c.Name, "Circle " + c.Name.Substring(c.Name.Length - 1), c.Name + ": the wait between casts. Auto-detect in the Loops panel moves it on \"not yet recovered\"." });
            TimingGroup(g, cat, rows);
        }

        g = Group("Arm and disarm");
        AddAction("arm", g, "Disarm both / arm", () => ThreadPool.QueueUserWorkItem(_ => { string why = f.ArmToggle("both"); if (why != null) Note(why); }));
        AddAction("arm.weapon", g, "Disarm weapon / arm", () => ThreadPool.QueueUserWorkItem(_ => { string why = f.ArmToggle("weapon"); if (why != null) Note(why); }));
        AddAction("arm.shield", g, "Disarm shield / arm", () => ThreadPool.QueueUserWorkItem(_ => { string why = f.ArmToggle("shield"); if (why != null) Note(why); }));
        hands = Add(g, new Label { AutoSize = false, Height = 18 });
        Summarize(g, () => hands.Text);

        // Item pickup (Damian, 2026-10-15: "for reagent pickup make it item pickup, and add a button to add, that reads in
        // the item data and lets you pick from a list"): the picked item types, kept in the "reagents" setting.
        g = Group("Item pickup");
        pickup = new CheckBox { Text = "Pick up these items near you", AutoSize = true, Checked = f.Pickup };
        pickup.CheckedChanged += (s, e) => f.Pickup = pickup.Checked;
        AddAction("pickup", g, null, () => { pickup.Checked = !pickup.Checked; }, pickup);
        var harvest = Add(g, new CheckBox { Text = "Harvest: pluck crops and shear sheep near you, then pick up the food and wool", AutoSize = true, Checked = f.Harvest });
        harvest.CheckedChanged += (s, e) => f.Harvest = harvest.Checked;
        Summarize(g, () => "pickup " + (pickup.Checked ? "on" : "off") + " (" + picks.Items.Count + " items), harvest " + (harvest.Checked ? "on" : "off"));
        reach = Add(g, new Label { AutoSize = false, Height = 16 });
        picks = Add(g, new ListBox { Height = 64 });
        var prow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        var padd = new Button { Text = "Add...", AutoSize = true }; var pdel = new Button { Text = "Remove", AutoSize = true };
        padd.Click += (s, e) => { int gr = ItemPicker.Pick(this, "Add an item to pick up", f.SeenGraphics(true), f.SeenGraphics(false)); if (gr < 0) return; var l = new List<int>(f.Reagents()); if (!l.Contains(gr)) { l.Add(gr); f.SetReagents(l.ToArray()); } FillPicks(); };
        pdel.Click += (s, e) => { if (picks.SelectedIndex < 0) return; var l = new List<int>(f.Reagents()); l.RemoveAt(picks.SelectedIndex); f.SetReagents(l.ToArray()); FillPicks(); };
        prow.Controls.Add(padd); prow.Controls.Add(pdel);
        FillPicks();

        // Mill, spin and weave (Damian, 2026-10-15: "we need a miller feature", "and a tailor for the cott").
        g = Group("Mill, spin and weave");
        AddAction("work", g, "Work the pack's wheat, cotton, wool, thread or yarn at the station beside you", () => f.WorkToggle());
        AddAction("record", g, "Record me: start / stop (in the client: [rec, [stop, and [ notes)", () => Note(f.RecordToggle("the window")));
        workState = Add(g, new Label { AutoSize = false, Height = 16 });
        Summarize(g, () => workState.Text);

        g = Group("Resources");
        resCount = new Button { Text = "Count", AutoSize = true, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Visible = false, BackColor = Color.FromArgb(240, 190, 60), Font = new Font(Font, FontStyle.Regular) };
        new ToolTip().SetToolTip(resCount, "The pack or a bag in it has not been opened since the login: open each one once and count.");
        resCount.Click += (s, e) => { resCount.Enabled = false; ThreadPool.QueueUserWorkItem(_ => { string w = f.CountPack(); BeginInvoke(new Action(() => { resCount.Enabled = true; if (w != null) Note(w); })); }); };
        headOf[g].Controls.Add(resCount);
        // Six item columns after the heading: two to a row span three each, the reagents three to a row span two.
        res = Add(g, new TableLayoutPanel { ColumnCount = 7, Height = 28 });
        res.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); for (int c = 0; c < 6; c++) res.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        var rrow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        var radd = new Button { Text = "Add...", AutoSize = true };
        radd.Click += (s, e) => { int gr = ItemPicker.Pick(this, "Add an item to count", f.SeenGraphics(true), f.SeenGraphics(false)); if (gr < 0) return; var l = f.ResourceList(); if (!l.Exists(r => Array.IndexOf(r.Graphics, gr) >= 0)) { l.Add(new AssistFeatures.CountRow { Name = UoTiles.Name(gr), Group = AssistFeatures.GroupOf(gr), Graphics = new[] { gr } }); f.SetResources(l); } resShape = ""; };
        rrow.Controls.Add(radd); rrow.Controls.Add(new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), Text = "Right-click an item to remove it or move it to another group." });
        // Collapsed: the recall reagents, icon and count (Damian, 2026-10-17: "show pearl mandrake moss with icon: count").
        // In the header row, between the title and the Count button (Damian, 2026-10-18: "put the summary about recall regs in
        // same row as the expando between that and the count button, not on new line").
        var rsum = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        foreach (int art in ResSummaryArts) {
            var pic = new PictureBox { Width = 18, Height = 18, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 2, 0) };
            var tally = new Label { AutoSize = true, Margin = new Padding(0, 4, 10, 0), Font = new Font(Font, FontStyle.Bold) };
            rsum.Controls.Add(pic); rsum.Controls.Add(tally); resSumPics.Add(pic); resSumCounts.Add(tally);
        }
        var resInner = (FlowLayoutPanel)g.Tag; rsum.Visible = !resInner.Visible; resInner.VisibleChanged += (s, e) => rsum.Visible = !resInner.Visible;
        headOf[g].Controls.Add(rsum);

        g = Group("Skills");
        skills = Add(g, new SortList { View = View.Details, Height = 220, FullRowSelect = true });
        skills.Columns.Add("Skill", 150); skills.Columns.Add("Shown", 55); skills.Columns.Add("Base", 55); skills.Columns.Add("Change", 60);
        skills.SortColumn = 2; skills.Descending = true;  // Damian, 2026-10-18: "skills list default sorting should be descinding by base"
        Add(g, new Label { AutoSize = false, Height = 14, Text = "Click a column to sort up, right-click to sort down." });

        // Chat: player speech only (Damian, 2026-10-15: "filters out everything but human player speech, so status messages don't scroll out of sight comms"). Events: advice and assist events.
        var chatGroup = Group("Chat and events");
        talk = Add(chatGroup, new TabControl { Height = 110 });
        Summarize(chatGroup, () => notes.Items.Count > 0 ? (string)notes.Items[notes.Items.Count - 1] : "");
        var chatPage = new TabPage("Chat"); var eventPage = new TabPage("Events"); talk.TabPages.Add(chatPage); talk.TabPages.Add(eventPage);
        chat = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true }; chatPage.Controls.Add(chat);
        var hide = new ContextMenuStrip();
        hide.Items.Add("Hide this speaker", null, (s, e) => HideSpeaker());
        hide.Items.Add("Show every speaker again", null, (s, e) => { f.Cfg.Set("chat.hidden", ""); chatSeen = 0; chat.Items.Clear(); });
        chat.ContextMenuStrip = hide;
        chat.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right) { int i = chat.IndexFromPoint(e.Location); if (i >= 0) chat.SelectedIndex = i; } };
        notes = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true }; eventPage.Controls.Add(notes);
        // Log: the relay log's tail, the fail-safe's state and its watches (untick one to stop it disabling the assist).
        var logPage = new TabPage("Log"); talk.TabPages.Add(logPage);
        logList = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, Font = new Font("Consolas", 8f) };
        guardState = new Label { Dock = DockStyle.Top, Height = 32 };
        watches = new CheckedListBox { Dock = DockStyle.Bottom, Height = 74, CheckOnClick = true };
        foreach (var w in AssistGuard.Watches) watches.Items.Add(w.Id + ": " + w.Text + " (" + w.Limit + " in " + w.Seconds + " s)", AssistGuard.Current == null || AssistGuard.Current.Watching(w.Id));
        watches.ItemCheck += (s, e) => { if (AssistGuard.Current != null) AssistGuard.Current.SetWatching(AssistGuard.Watches[e.Index].Id, e.NewValue == CheckState.Checked); };
        logPage.Controls.Add(logList); logPage.Controls.Add(guardState); logPage.Controls.Add(watches); logList.BringToFront();

        // Automatic: what the assist does with no input. Display: what the UO window's title bar shows (Damian, 2026-10-15:
        // "the display option, can it add things to uo window titlebar? having display configs affect status in
        // uoaixassist is unnecessary with the collapser").
        g = Group("General");
        var tabs = Add(g, new TabControl { Height = GeneralTabsH });
        var auto = new TabPage("Automatic"); var disp = new TabPage("Display"); var snd = new TabPage("Sounds"); tabs.TabPages.Add(auto); tabs.TabPages.Add(disp); tabs.TabPages.Add(snd);
        // Sounds kept from the client, by group, in two columns; the tab grows to hold them while it is the one shown.
        // The first line says what a check does (Damian, 2026-10-18: "tell me in the tabpage what the config is turning on or
        // off... sounds or supressions").
        int soundLines = (AssistFeatures.SoundGroups.Length + 1) / 2 + 1;
        var sgrid = new TableLayoutPanel { ColumnCount = 2, RowCount = soundLines, Dock = DockStyle.Fill };
        sgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); sgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < soundLines; i++) sgrid.RowStyles.Add(new RowStyle(SizeType.Absolute, SoundRowH));
        var sayCheck = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 0, 0), Text = "Checked = muted: the client never hears that sound. Unchecked = it plays." };
        sgrid.Controls.Add(sayCheck, 0, 0); sgrid.SetColumnSpan(sayCheck, 2);
        for (int i = 0; i < AssistFeatures.SoundGroups.Length; i++) {
            var group = AssistFeatures.SoundGroups[i];
            var c = new CheckBox { Text = "Mute " + group.Name, AutoSize = true, Checked = f.SoundOff(group.Name), Margin = new Padding(3, 1, 0, 0) };
            c.CheckedChanged += (s, e) => f.SetSoundOff(group.Name, c.Checked);
            sgrid.Controls.Add(c, i % 2, 1 + i / 2); soundBoxes.Add(Tuple.Create(group, c));
        }
        snd.Controls.Add(sgrid);
        tabs.SelectedIndexChanged += (s, e) => { tabs.Height = tabs.SelectedTab == snd ? 30 + soundLines * SoundRowH : GeneralTabsH; Fit(); };
        autoFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false }; auto.Controls.Add(autoFlow);
        autoFlow.Controls.Add(new Label { AutoSize = false, Height = 46, Text = "Potion drinking: double-click a potion with no hand free and the weapon goes to the pack, you drink, and it comes back. Nothing to set up." });
        drink = new Label { AutoSize = false, Height = 18 }; autoFlow.Controls.Add(drink);
        var drow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true }; disp.Controls.Add(drow);
        drow.Controls.Add(new Label { AutoSize = true, Text = "In the UO window's title bar:", Margin = new Padding(3, 4, 3, 0) });
        foreach (var n in AssistFeatures.DisplayNames) {
            var c = new CheckBox { Text = n, AutoSize = true, Checked = f.Shown(n) }; string name = n;
            c.CheckedChanged += (s, e) => f.Show(name, c.Checked); display[n] = c; drow.Controls.Add(c);
        }
        var banks = new CheckBox { Text = "8x8 bank corners (runes in your client only)", AutoSize = true, Checked = f.ShowBanks };
        banks.CheckedChanged += (s, e) => { f.ShowBanks = banks.Checked; ThreadPool.QueueUserWorkItem(_ => f.UpdateBanks(UoMap.LandZ)); };
        drow.Controls.Add(banks);

        // Map (Damian, 2026-10-15: "a third main tab which is map, and read the map file and put a player tracker there like
        // uo automap"): the client's own map files drawn in its radar colours, following the player, with the trail walked
        // and the people seen.
        g = Group("Map");
        SubTabs(g, "map");
        var mrow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        var zin = new Button { Text = "+", Width = 30 }; var zout = new Button { Text = "-", Width = 30 }; var clear = new Button { Text = "Clear trail and corpses", AutoSize = true };
        mapInfo = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
        // Teleport-only spots (Damian, 2026-10-17: "flag unwalkable teleport spots", then "just move the control to the map tab").
        var tpOnly = new CheckBox { Text = "Teleport-only spots", AutoSize = true, Margin = new Padding(8, 6, 0, 0), Checked = f.Cfg.Get("map.teleportonly", "0") == "1" };
        new ToolTip().SetToolTip(tpOnly, "Magenta marks: places within Teleport range (11 tiles) you can stand on but cannot walk to from here, like the roof of Magincia's mage shop.");
        tpOnly.CheckedChanged += (s, e) => { f.Cfg.Set("map.teleportonly", tpOnly.Checked ? "1" : "0"); if (mapView != null) mapView.Invalidate(); };
        mrow.Controls.Add(zin); mrow.Controls.Add(zout); mrow.Controls.Add(clear); mrow.Controls.Add(tpOnly); mrow.Controls.Add(mapInfo);
        mapView = Add(g, new MapView { Height = MapMin, Zoom = Array.IndexOf(MapView.ZoomSteps, f.Cfg.GetInt("map.zoom", 2)) >= 0 ? f.Cfg.GetInt("map.zoom", 2) : 2 });
        zin.Click += (s, e) => MapZoom(1); zout.Click += (s, e) => MapZoom(-1);
        mapView.MouseWheel += (s, e) => MapZoom(e.Delta > 0 ? 1 : -1);
        clear.Click += (s, e) => { lock (trail) trail.Clear(); f.ClearCorpses(); mapView.Invalidate(); };
        mapView.Corpses = () => f.Corpses();
        mapView.Hover = (x, y) => { mapInfo.Text = x < 0 ? "" : x + "," + y + "  " + Sextant(x, y); };
        mapView.Trail = () => { lock (trail) return trail.ToArray(); };
        mapView.Course = () => f.Course(); mapView.Kills = () => f.KillMarks();
        mapView.Pins = () => LoadPins().ToArray();
        mapView.RightClick = (x, y, at) => PinMenu(x, y, at);
        // Before the hover readout (Damian, 2026-10-15: "reverse the layout order of the pin my spot and the text readout").
        // Back on the player after a drag (Damian, 2026-10-15: "add a button above the map to center on me").
        var centre = new Button { Text = "Center on me", AutoSize = true }; mrow.Controls.Add(centre); mrow.Controls.SetChildIndex(centre, 0);
        centre.Click += (s, e) => { mapView.OffX = mapView.OffY = 0; mapView.Invalidate(); };
        tripButton = new Button { Text = "Begin trip", AutoSize = true }; mrow.Controls.Add(tripButton); mrow.Controls.SetChildIndex(tripButton, 1);
        tripButton.Click += (s, e) => {
            if (!f.TripOn) { string why = f.BeginTrip(); if (why != null) Note(why); return; }
            string path; string w = f.EndTrip(out path);
            if (w != null) Note(w); else { Note("Trip report: " + path); AssistNative.ShellExecute(IntPtr.Zero, "open", path, null, null, 1); }
        };
        var pinMe = new Button { Text = "Pin my spot", AutoSize = true }; mrow.Controls.Add(pinMe); mrow.Controls.SetChildIndex(pinMe, mrow.Controls.GetChildIndex(mapInfo));
        pinMe.Click += (s, e) => EditPin(null, mapView.CenterX, mapView.CenterY);
        mapView.Caption = () => { int x = mapView.CenterX, y = mapView.CenterY; return (x | y) == 0 ? "" : "You: " + x + "," + y + "  " + Sextant(x, y); };
        // Filters: a checkbox per type, and Subtypes... for type:subtype; hidden ones kept in "map.hide" (| separated).
        var frow = Add(g, new FlowLayoutPanel { Height = 66, WrapContents = true });
        foreach (var t in AssistAtlas.Types) {
            string type = t; var c = new CheckBox { Text = t, AutoSize = true, Checked = !Hidden(t), ForeColor = t == "Person" || t == "Ghost" || t == "NPC" ? Color.DimGray : t == "Banker" ? Color.DarkGoldenrod : TypeColor(t, false), Margin = new Padding(2, 0, 2, 0) };
            c.CheckedChanged += (s, e) => { SetHidden(type, !c.Checked); mapView.Invalidate(); }; frow.Controls.Add(c);
        }
        var regionsBox = new CheckBox { Text = "Regions", AutoSize = true, Checked = f.Cfg.Get("map.regions", "on") == "on", Margin = new Padding(2, 0, 2, 0) };
        regionsBox.CheckedChanged += (s, e) => { f.Cfg.Set("map.regions", regionsBox.Checked ? "on" : "off"); mapView.Invalidate(); }; frow.Controls.Add(regionsBox);
        var subs = new Button { Text = "Subtypes...", AutoSize = true, Margin = new Padding(2, 0, 2, 0) }; subs.Click += (s, e) => SubtypeFilter(); frow.Controls.Add(subs);
        var lookBox = new CheckBox { Text = "Name people by a click", AutoSize = true, Checked = f.AutoLook, Margin = new Padding(2, 0, 2, 0) };
        lookBox.CheckedChanged += (s, e) => f.AutoLook = lookBox.Checked; frow.Controls.Add(lookBox);
        mapView.ShowRegions = () => regionsBox.Checked;
        mapView.Marks = MapMarks;
        mapDetail = Add(g, new ListBox { Height = 110, HorizontalScrollbar = true });
        StockDraw(mapDetail); mapView.Picked = s => ShowNpc(mapDetail, null, s);
        mapView.Opened = s => { if (f.Atlas != null && f.Atlas.Shops().Exists(n => n.Serial == s)) { ShowPage("shops"); PickShop(s); } };

        // Trips (Damian, 2026-10-16: "i should be able to select a past trip, and see a sub tab of the map tab that gives me
        // the report of that specific trip, and a overall meta analysis sub tab that shows time-bounded regions of analysis").
        g = Group("Trip report");
        SubTabs(g, "map.trip");
        var trow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        tripPick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 }; Hold(tripPick); trow.Controls.Add(tripPick);
        var openReport = new Button { Text = "Open full report", AutoSize = true }; trow.Controls.Add(openReport);
        var autoTrips = new CheckBox { Text = "Record trips outside the guards", AutoSize = true, Checked = f.AutoTrips, Margin = new Padding(8, 6, 0, 0) };
        autoTrips.CheckedChanged += (s, e) => f.AutoTrips = autoTrips.Checked; trow.Controls.Add(autoTrips);
        tripText = Add(g, new TextBox { Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Height = TripMin, BackColor = SystemColors.Window });
        tripPick.SelectedIndexChanged += (s, e) => ShowTrip();
        openReport.Click += (s, e) => { var r = tripPick.SelectedItem as TripItemView; if (r != null && r.Trip.Report != null && System.IO.File.Exists(r.Trip.Report)) AssistNative.ShellExecute(IntPtr.Zero, "open", r.Trip.Report, null, null, 1); };
        g = Group("Area analysis");
        SubTabs(g, "map.areas");
        var arow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        arow.Controls.Add(new Label { Text = "Trips from:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        areaPeriod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 }; Hold(areaPeriod);
        foreach (var p in AreaPeriods) areaPeriod.Items.Add(p.Key);
        areaPeriod.SelectedIndex = Math.Max(0, Math.Min(AreaPeriods.Length - 1, f.Cfg.GetInt("areas.period", 1)));
        areaPeriod.SelectedIndexChanged += (s, e) => { f.Cfg.Set("areas.period", areaPeriod.SelectedIndex.ToString()); FillAreas(); }; arow.Controls.Add(areaPeriod);
        arow.Controls.Add(new Label { Text = "newer trips weigh more (half weight every " + TripBook.HalfLifeDays + " days)", AutoSize = true, Margin = new Padding(8, 7, 0, 0) });
        areaBest = Add(g, new Label { Height = 40, Font = new Font(Font, FontStyle.Bold) }); growers.Add(areaBest);
        areaList = Add(g, new SortList { View = View.Details, Height = TripMin, FullRowSelect = true, GridLines = true, AutoSort = true });
        foreach (var c in new[] { "Region:220", "Newest:70", "Trips:45", "Combat:75", "Profit:190", "Training:230", "Spawn:65", "Close calls:220", "Harvest:150", "Reagents:170", "Resist training:200", "Magic drops:240", "Taming:220", "Herding:200", "Feathers and hides:300", "Hostiles:240" }) { var a = c.Split(':'); areaList.Columns.Add(a[0], int.Parse(a[1])); }
        areaDetail = Add(g, new TextBox { Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Height = AreaDetailMin, BackColor = SystemColors.Window });
        areaList.SelectedIndexChanged += (s, e) => { if (areaList.SelectedItems.Count > 0) areaDetail.Text = (string)areaList.SelectedItems[0].Tag; };

        // Shopkeepers and merchants (Damian, 2026-10-15: "add a shopkeeps and merchants as a main tab"): every vendor the
        // atlas holds, with its trade, place, distance and buy list; the search keeps vendors whose list holds the text.
        g = Group("Shopkeepers and merchants");
        var findRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        findRow.Controls.Add(new Label { Text = "Find an item:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        shopFind = new TextBox { Width = 200 }; shopFind.TextChanged += (s, e) => { shopShape = ""; FillShops(); }; findRow.Controls.Add(shopFind);
        var filterRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        shopCity = Filter(filterRow, "City:", "shops.city"); shopKind = Filter(filterRow, "Type:", "shops.kind");
        shops = Add(g, new SortList { View = View.Details, Height = 200, FullRowSelect = true, AutoSort = true });
        shops.Columns.Add("Name", 150); shops.Columns.Add("Trade", 95); shops.Columns.Add("Where", 260); shops.Columns.Add("Tiles", 50); shops.Columns.Add("Items", 45); shops.Columns.Add("Seen", 70); shops.Columns.Add("Off the norm", 200);
        shopInfo = Add(g, new ListBox { Height = 80, HorizontalScrollbar = true }); StockDraw(shopInfo);
        shopItems = Add(g, new SortList { View = View.Details, Height = 160, FullRowSelect = true, GridLines = true, AutoSort = true });
        shopItems.Columns.Add("Item", 220); shopItems.Columns.Add("Price (gp)", 75, HorizontalAlignment.Right); shopItems.Columns.Add("Qty", 50, HorizontalAlignment.Right); shopItems.Columns.Add("Off the norm", 330);
        shops.SelectedIndexChanged += (s, e) => { if (!selecting && shops.SelectedItems.Count > 0) ShowNpc(shopInfo, shopItems, (uint)shops.SelectedItems[0].Tag); };

        // Smithing value (Damian, 2026-10-15: "calculate the best ingot efficient item to make and sell the npcs based on
        // smithy skill. you should add this to shops feature"): per item its ingots, builds and their quality at about your
        // skill, what NPCs pay by quality, what you sold, and the expected gold an ingot turns into; best first.
        g = Group("Smithing value");
        smithBest = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });
        smithRows = Add(g, new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true, AutoSort = true });
        foreach (var c in new[] { "Item:150", "Ingots:50", "Builds:50", "Exceptional:75", "Failed:50", "NPC pays:65", "Exc. pays:65", "Sold:90", "Gold per ingot:90", "Based on:170" }) { var a = c.Split(':'); smithRows.Columns.Add(a[0], int.Parse(a[1])); }

        // Tailoring value: the smithing value's table in cloth, with each vendor's demand from its latest sell list.
        g = Group("Tailoring value");
        tailorBest = Add(g, new Label { Height = 52, Font = new Font(Font, FontStyle.Bold) });
        tailorRows = Add(g, new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true, AutoSort = true });
        foreach (var c in new[] { "Item:120", "Cloth:45", "Builds:50", "Exceptional:75", "Failed:50", "NPC pays:65", "Exc. pays:65", "Sold:90", "Gold per cloth:90", "Vendors want:200", "Based on:170" }) { var a = c.Split(':'); tailorRows.Columns.Add(a[0], int.Parse(a[1])); }

        // Every other craft's value table, the craft picked (Damian, 2026-10-18: "expand it to cover all other skills too").
        g = Group("Other crafts value");
        var craftRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        craftRow.Controls.Add(new Label { Text = "Craft:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        craftPick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 }; foreach (var c in f.Crafts) craftPick.Items.Add(c.Name);
        craftPick.SelectedIndex = Math.Max(0, Array.FindIndex(f.Crafts, c => c.Name == f.Cfg.Get("maker.craft", "Carpentry")));
        craftPick.SelectedIndexChanged += (s, e) => { f.Cfg.Set("maker.craft", f.Crafts[craftPick.SelectedIndex].Name); craftShape = ""; FillCraft(); }; craftRow.Controls.Add(craftPick);
        craftBest = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });
        craftRows = Add(g, new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true, AutoSort = true });
        foreach (var c in new[] { "Item:150", "Takes:50", "Builds:50", "Exceptional:75", "Failed:50", "NPC pays:65", "Sold:90", "Gold per unit:90", "Based on:170" }) { var a = c.Split(':'); craftRows.Columns.Add(a[0], int.Parse(a[1])); }

        // The bag pricer (Damian, 2026-10-15: "a default price to sell at on player vendor, based on known market conditions
        // and a reasonable markup over city prices when in bulk ... let each price be configurable", "mine are all gm made,
        // so there is my pricing for the better than store quality").
        // The reagent loadout (Damian, 2026-10-16: "like the pricer, its the reagent bag loadout tool"): two targets, the
        // loadout bag then the storage bag; the amounts to pull back are kept per reagent.
        g = Group("Reagent loadout");
        var lrow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        var lpick = new Button { Text = "Target loadout, then storage", AutoSize = true, Height = 26 }; lpick.Click += (s, e) => f.LoadoutTarget(); lrow.Controls.Add(lpick);
        loadoutStatus = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0) }; lrow.Controls.Add(loadoutStatus);
        var wants = Add(g, new FlowLayoutPanel { Height = 60, WrapContents = true });
        foreach (int rg in AssistFeatures.LoadoutReagents()) {
            int graphic = rg;
            wants.Controls.Add(new Label { Text = AssistFeatures.ReagentName(graphic) + ":", AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
            var n = new NumericUpDown { Minimum = 0, Maximum = 999, Width = 50, Value = Math.Min(999, f.LoadoutWant(graphic)) };
            n.ValueChanged += (s, e) => f.SetLoadoutWant(graphic, (int)n.Value); wants.Controls.Add(n);
        }

        g = Group("Bag pricer");
        var pickRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        var pick = new Button { Text = "Target a bag", Width = 120, Height = 26 }; pick.Click += (s, e) => f.PricerTarget(); pickRow.Controls.Add(pick);
        pricerStatus = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0) }; pickRow.Controls.Add(pricerStatus);
        var markRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        bulkPct = Percent(markRow, "Bulk markup over city %:", "pricer.bulk", BulkPct);
        craftPct = Percent(markRow, "GM-made markup %:", "pricer.craft", CraftPct);
        gmMade = new CheckBox { Text = "My armour, weapons and clothing are GM-made", AutoSize = true, Checked = f.Cfg.Get("pricer.gm", "on") == "on", Margin = new Padding(10, 6, 0, 0) };
        gmMade.CheckedChanged += (s, e) => { f.Cfg.Set("pricer.gm", gmMade.Checked ? "on" : "off"); FillPricer(); }; markRow.Controls.Add(gmMade);
        pricer = Add(g, new DataGridView { Height = 300, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window });
        foreach (var c in new[] { "Item", "Qty", "City price", "Markup", "Unit price (edit)", "Total" }) pricer.Columns.Add(c, c);
        foreach (DataGridViewColumn c in pricer.Columns) { c.ReadOnly = c.Index != PriceCol; c.SortMode = DataGridViewColumnSortMode.Automatic; }
        pricer.Columns[PriceCol].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 220);
        pricer.CellEndEdit += (s, e) => PriceEdited(e.RowIndex);
        pricerTotal = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });

        // The tailoring circuit (Damian, 2026-10-17: "make a new tab in uoaix assist for this that shows the details of the
        // job, vendors state, sales totals, thread buys etc."): the job and the pack, each thread vendor's stock and restock,
        // and the thread bought and shirts sold this session and in all.
        // The circuit's setup (Damian, 2026-10-17: "the circuit tab in the assist UI should give me select the circuit,
        // configure its properties, and launch in all its modes, with helpful tips"; "for each, let me pick the list of
        // shops to visit in case we need to adjust on the fly"): the circuit, its lists, its shops by name, its modes.
        g = Group("Circuit setup");
        var circRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        circRow.Controls.Add(new Label { Text = "Circuit:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        circPick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 }; circPick.Items.AddRange(new object[] { "Tailoring", "Buy reagents", "Survey town", "Refresh", "Cave mining", "Smithing" });
        string picked0 = f.Cfg.Get("circuit.selected", "tailor"); circPick.SelectedIndex = picked0 == "buy" ? 1 : picked0 == "survey" ? 2 : picked0 == "refresh" ? 3 : picked0 == "mine" ? 4 : picked0 == "smith" ? 5 : 0; circRow.Controls.Add(circPick);
        tipper.SetToolTip(circPick, "Tailoring buys thread, weaves, sews and sells shirts. Buy reagents buys the shopping list at the picked shops and stores it in the bank. Survey town reads every vendor's stock in a town and buys nothing. Refresh buys food, eats until full, feeds the mount, banks the gold and refills the reagent loadout.");
        System.Action<string, string, Func<string>> launch = (text, tip, run) => {
            var b = new Button { Text = text, AutoSize = true, Height = 26 }; b.Click += (s, e) => { string w = run(); circSetupNote.Text = w == null ? text + ": started." : text + ": " + w; };
            tipper.SetToolTip(b, tip); circRow.Controls.Add(b); circLaunch[text] = b;
        };
        // One Go for the circuit picked, and a circuit's own extra buttons only while it is picked (Damian, 2026-10-18: "pick a
        // circuit from the drop down by profession, then if that profession has sub options like tailor, they show up,
        // other wise, go").
        launch("Go", "Start the circuit picked. Tailoring: sell the shirts, buy thread, weave, sew, and sell trips. Buy reagents: the shopping list at the picked shops, into the bank. Survey town: every ticked shop's stock, nothing bought. Refresh: food, the mount fed, the gold banked, the loadout refilled. Cave mining: the cave you stand in, smelted at the forge set up below, the ingots stored. Smithing: at the anvil you stand by, the ladder's item for your skill, sold to the smiths in reach, until 100.0.",
            () => { switch (CircuitPicked) { case "buy": return f.CircuitBuy(); case "survey": return f.CircuitSurvey(); case "refresh": return f.CircuitRefresh(); case "mine": return f.CircuitMine(); case "smith": return f.CircuitSmith(); default: return f.CircuitGo(); } });
        launch("Sell only", "Sell the shirts in the pack at the picked tailor shops and bank the gold; nothing else is bought.", f.CircuitSell);
        launch("Tour", "Visit every picked tailor shop once: one shirt to each tailor that buys, and all its thread. The report is in the job notes.", f.CircuitTour);
        var stop = new Button { Text = "Stop", AutoSize = true, Height = 26 }; stop.Click += (s, e) => { f.CircuitHalt("stop asked"); circSetupNote.Text = "Stop asked."; }; circRow.Controls.Add(stop);
        tipper.SetToolTip(stop, "Stops the running circuit after its current step. Taking a step yourself also stops it.");
        // The mining circuit's setup in the window (Damian, 2026-10-18: "where is the cave miner circuit?"), each step run off
        // the window's thread (Dig once waits for the player's own dig) and its answer shown in the note.
        mineRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        mineRow.Controls.Add(new Label { Text = "Mining setup:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        System.Action<string, string, string[]> mineStep = (text, tip, args) => {
            var b = new Button { Text = text, AutoSize = true, Height = 26 }; tipper.SetToolTip(b, tip); mineRow.Controls.Add(b);
            b.Click += (s, e) => { circSetupNote.Text = text + "..."; ThreadPool.QueueUserWorkItem(_ => { string w = f.MineSetup(args); BeginInvoke(new Action(() => circSetupNote.Text = text + ": " + w)); }); };
        };
        mineStep("Forge", "Records the forge you can see (within 12 tiles); stand where it is in view.", new[] { "mine-setup", "forge" });
        mineStep("Dig once", "Then dig once by hand: the tool and where you aimed are what the circuit repeats.", new[] { "mine-setup", "dig" });
        mineStep("Vendor", "Ingots onto your in-progress ingot pile on the vendor: open the vendor first.", new[] { "mine-setup", "storage", "vendor" });
        mineStep("Bank", "Ingots into the bank box.", new[] { "mine-setup", "storage", "bank" });
        mineStep("Floor here", "Ingots dropped on the tile you stand on (a house floor).", new[] { "mine-setup", "storage", "floor" });
        mineStep("Show", "The mining setup as it stands.", new[] { "mine-setup" });
        // Refresh's loadout bag picked from the Circuit tab (Damian, 2026-10-18: "for refresh setup add a button to do the
        // selection of loadout bag").
        refreshRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        refreshRow.Controls.Add(new Label { Text = "Refresh setup:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        var loadPick = new Button { Text = "Pick loadout bag, then storage", AutoSize = true, Height = 26 };
        loadPick.Click += (s, e) => { f.LoadoutTarget(); circSetupNote.Text = "Target the loadout bag in your pack, then the bag it refills from."; };
        tipper.SetToolTip(loadPick, "Two target cursors: first the bag in your pack that holds the reagent loadout, then the bag it refills from (in the bank box)."); refreshRow.Controls.Add(loadPick);
        var noStore = new Button { Text = "No storage bag", AutoSize = true, Height = 26 };
        noStore.Click += (s, e) => circSetupNote.Text = f.LoadoutNoStorage();
        tipper.SetToolTip(noStore, "Refresh refills the loadout from every bag in the bank box, and buys what the bank lacks in the town's mage shops (exactly the shortfall), then banks the change."); refreshRow.Controls.Add(noStore);
        // The smithing circuit's setup: where its ingots come from, and the tinker menus for a sledge hammer.
        smithRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        smithRow.Controls.Add(new Label { Text = "Smithing: ingots from", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        var ingotFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 }; ingotFrom.Items.AddRange(new object[] { "the bank", "a smith" });
        ingotFrom.SelectedIndex = f.SmithIngots == "buy" ? 1 : 0; ingotFrom.SelectedIndexChanged += (s, e) => f.SmithIngots = ingotFrom.SelectedIndex == 1 ? "buy" : "bank";
        tipper.SetToolTip(ingotFrom, "The bank: the ingots at a time set beside this from the city's bank box, the gold banked on the same visit. A smith: bought from a smith in reach whose stock lists iron ingots."); smithRow.Controls.Add(ingotFrom);
        smithRow.Controls.Add(new Label { Text = "at a time:", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        var ingotLoad = new NumericUpDown { Minimum = 1, Maximum = 9999, Width = 60, Value = Math.Min(9999, f.SmithIngotLoad) };
        ingotLoad.ValueChanged += (s, e) => f.SmithIngotLoad = (int)ingotLoad.Value;
        tipper.SetToolTip(ingotLoad, "Ingots the circuit carries from each bank visit or purchase. The pack's weight limit is the ceiling."); smithRow.Controls.Add(ingotLoad);
        smithRow.Controls.Add(new Label { Text = "Sledge tinker menus:", AutoSize = true, Margin = new Padding(8, 7, 4, 0) });
        var tinkerBox = new TextBox { Width = 110, Text = f.SmithTinker }; tinkerBox.Leave += (s, e) => { if (tinkerBox.Text.Trim().Length > 0) f.SmithTinker = tinkerBox.Text.Trim(); };
        tipper.SetToolTip(tinkerBox, "The tinker's menu entries to a sledge hammer, each the start of an entry's text, separated by |."); smithRow.Controls.Add(tinkerBox);
        // What the smithing circuit builds for: skill, profit, or an order kept for people at its best NPC prices.
        smithRow2 = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        smithRow2.Controls.Add(new Label { Text = "Build for:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        var goal = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 }; goal.Items.AddRange(new object[] { "skill", "profit", "order" });
        goal.SelectedItem = f.SmithGoal; goal.SelectedIndexChanged += (s, e) => f.SmithGoal = (string)goal.SelectedItem;
        tipper.SetToolTip(goal, "Skill: your ladder until Blacksmithing reads 100.0. Profit: the item of the most gold per ingot the Maker tab knows, sold to the smiths. Order: the order list, built extra and the best kept for people by what the smith would pay."); smithRow2.Controls.Add(goal);
        var orderBox = new TextBox { Width = 210, Text = f.SmithOrder }; orderBox.Leave += (s, e) => f.SmithOrder = orderBox.Text.Trim();
        tipper.SetToolTip(orderBox, "The order: item@count separated by ; (katana@5;spear@5). Names as the game names them."); smithRow2.Controls.Add(orderBox);
        var orderPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 }; orderPreset.Items.Add("Presets..."); foreach (var op in AssistFeatures.SmithOrderPresets) orderPreset.Items.Add(op[0]); orderPreset.SelectedIndex = 0;
        orderPreset.SelectedIndexChanged += (s, e) => { if (orderPreset.SelectedIndex <= 0) return; orderBox.Text = AssistFeatures.SmithOrderPresets[orderPreset.SelectedIndex - 1][1]; f.SmithOrder = orderBox.Text; orderPreset.SelectedIndex = 0; };
        smithRow2.Controls.Add(orderPreset);
        smithRow2.Controls.Add(new Label { Text = "build x", AutoSize = true, Margin = new Padding(4, 7, 2, 0) });
        var pickN = new NumericUpDown { Minimum = 1, Maximum = 10, Width = 40, Value = Math.Min(10, f.SmithPickOf) }; pickN.ValueChanged += (s, e) => f.SmithPickOf = (int)pickN.Value;
        tipper.SetToolTip(pickN, "Each piece of the order is built this many times; the smith's offers pick the best, the rest are sold to it."); smithRow2.Controls.Add(pickN);
        circSetupNote = Add(g, new Label { Height = 18, AutoSize = false });
        // Tailoring: what is sold and its lowest prices, and what is sewn.
        sellGrid = Add(g, new DataGridView { Height = 110, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window });
        foreach (var c in new[] { "Item to sell", "Arts (hex)", "Lowest price" }) sellGrid.Columns.Add(c, c);
        sellGrid.CellEndEdit += (s, e) => SaveSellGrid(); sellGrid.UserDeletedRow += (s, e) => SaveSellGrid();
        tipper.SetToolTip(sellGrid, "What the circuit sells and the lowest price for each. Arts are the item's graphics in hex, comma separated. Delete a row to drop it.");
        var menuRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        menuRow.Controls.Add(new Label { Text = "Sewing menu picks:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        var menuBox = new TextBox { Width = 200, Text = f.ProductMenu }; menuBox.Leave += (s, e) => f.ProductMenu = menuBox.Text.Trim(); menuRow.Controls.Add(menuBox);
        tipper.SetToolTip(menuBox, "The sewing menus' picks by their text, separated by |: shirts|Fancy shirt.");
        circTailorOnly = new Control[] { sellGrid, menuRow };
        // Buy reagents: the shopping list, each with a count and the highest price, and the purse.
        var addRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        var preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 }; foreach (var p in AssistFeatures.BuyPresets) preset.Items.Add(p[0]); preset.SelectedIndex = 0; addRow.Controls.Add(preset);
        var count = new NumericUpDown { Minimum = 1, Maximum = 9999, Value = 100, Width = 60 }; addRow.Controls.Add(count);
        addRow.Controls.Add(new Label { Text = "at most", AutoSize = true, Margin = new Padding(2, 7, 2, 0) });
        var price = new NumericUpDown { Minimum = 1, Maximum = 9999, Value = 5, Width = 55 }; addRow.Controls.Add(price);
        addRow.Controls.Add(new Label { Text = "gp", AutoSize = true, Margin = new Padding(2, 7, 6, 0) });
        var add = new Button { Text = "Add", AutoSize = true, Height = 26 }; add.Click += (s, e) => { var p = AssistFeatures.BuyPresets[preset.SelectedIndex]; f.SetBuyItem(p[0], p[1], (int)count.Value, (int)price.Value); FillCircuitSetup(); }; addRow.Controls.Add(add);
        addRow.Controls.Add(new Label { Text = "Purse:", AutoSize = true, Margin = new Padding(12, 7, 2, 0) });
        var purse = new NumericUpDown { Minimum = 0, Maximum = 60000, Increment = 500, Value = Math.Min(60000, f.BuyPurse), Width = 70 }; purse.ValueChanged += (s, e) => f.BuyPurse = (int)purse.Value; addRow.Controls.Add(purse);
        tipper.SetToolTip(add, "Adds the item to the shopping list, or changes its count and price. For anything not in the list, type a row into the grid below.");
        tipper.SetToolTip(price, "The highest price paid for one; a shop asking more is passed over for that item.");
        tipper.SetToolTip(purse, "The gold carried from each bank: topped up from, and banked onto, the bank box's own gold pile, never a bag in it.");
        buyGrid = Add(g, new DataGridView { Height = 110, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window });
        foreach (var c in new[] { "Item to buy", "Arts (hex)", "Count", "Highest price" }) buyGrid.Columns.Add(c, c);
        buyGrid.CellEndEdit += (s, e) => SaveBuyGrid(); buyGrid.UserDeletedRow += (s, e) => SaveBuyGrid();
        tipper.SetToolTip(buyGrid, "The shopping list: how many of each to buy this run and the highest price. The name is matched against the shops' stock lists. Delete a row to drop it.");
        circBuyOnly = new Control[] { addRow, buyGrid };
        // Survey town: the town to visit.
        var townRow = Add(g, new FlowLayoutPanel { Height = 32, WrapContents = false });
        townRow.Controls.Add(new Label { Text = "Town:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        surveyTown = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 }; surveyTown.Items.Add(HereTown); townRow.Controls.Add(surveyTown);
        surveyTown.SelectedIndexChanged += (s, e) => { if (fillingShops) return; f.SurveyTown = surveyTown.SelectedIndex <= 0 ? "" : surveyTown.SelectedItem.ToString(); FillCircuitSetup(); };
        tipper.SetToolTip(surveyTown, "The town to survey. Where I stand surveys the town you are in; any other town is reached by its rune in the bank.");
        circSurveyOnly = new Control[] { townRow };
        // The shops, by name, in visiting order.
        var shopsLabel = Add(g, new Label { Text = "Shops to visit (ticked), top to bottom:", Height = 18, AutoSize = false });
        shopList = Add(g, new CheckedListBox { Height = 170, CheckOnClick = true, IntegralHeight = false });
        shopList.ItemCheck += (s, e) => BeginInvoke(new System.Action(SaveShops));
        tipper.SetToolTip(shopList, "The circuit visits only the ticked shops; the buy circuit visits them in this order. Untick a shop to keep the circuit away from it.");
        var shopRow = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        circShopsUi = new Control[] { shopsLabel, shopList, shopRow };
        foreach (var bt in new[] { "Up", "Down", "All", "None" }) {
            string which = bt; var b = new Button { Text = bt, AutoSize = true, Height = 26 }; shopRow.Controls.Add(b);
            b.Click += (s, e) => {
                int i = shopList.SelectedIndex;
                if (which == "All" || which == "None") { for (int j = 0; j < shopList.Items.Count; j++) shopList.SetItemChecked(j, which == "All"); }
                else if (i >= 0) { int to = which == "Up" ? i - 1 : i + 1; if (to >= 0 && to < shopList.Items.Count) { var it = shopList.Items[i]; bool on = shopList.GetItemChecked(i); shopList.Items.RemoveAt(i); shopList.Items.Insert(to, it); shopList.SetItemChecked(to, on); shopList.SelectedIndex = to; } }
                SaveShops();
            };
            tipper.SetToolTip(b, which == "Up" || which == "Down" ? "Moves the selected shop earlier or later in the visiting order." : which == "All" ? "Ticks every shop." : "Unticks every shop.");
        }
        circPick.SelectedIndexChanged += (s, e) => { f.Cfg.Set("circuit.selected", CircuitPicked); CircuitButtons(); FillCircuitSetup(); FillCircuit(); Fit(); };
        CircuitButtons();
        FillCircuitSetup();

        g = Group("Circuit job");
        circJobGroup = g; circLight = Add(g, Light());
        circJob = Add(g, new Label { AutoSize = false, Height = 4 }); growers.Add(circJob);
        Summarize(g, () => FirstLine(circJob.Text));
        g = Group("Circuit vendors");
        circVendors = Add(g, new SortList { View = View.Details, Height = 180, FullRowSelect = true, GridLines = true, AutoSort = true });
        CircCols(CircuitPicked == "buy" ? BuyCols : TailorCols);
        Summarize(g, () => circVendors.Items.Count + " vendors");
        g = Group("Circuit totals");
        // The gold made, by the big gold pile (Damian, 2026-10-17: "use that big gold pile graphic in the displays of gold made").
        goldRow = Add(g, new FlowLayoutPanel { Height = 36, WrapContents = false });
        goldPic = new PictureBox { Width = 32, Height = 32, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 6, 2) }; goldRow.Controls.Add(goldPic);
        goldLine = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold) }; goldRow.Controls.Add(goldLine);
        circTotals = Add(g, new Label { AutoSize = false, Height = 4, Font = new Font(Font, FontStyle.Bold) }); growers.Add(circTotals);
        circAll = Add(g, new Label { AutoSize = false, Height = 4 }); growers.Add(circAll);
        Summarize(g, () => FirstLine(circTotals.Text));

        KeyDown += OnKeyCapture;
        Resize += (s, e) => Fit();
        var timer = new System.Windows.Forms.Timer { Interval = 500 }; timer.Tick += (s, e) => Tick(); timer.Start();
        Fit();
    }

    readonly List<System.Action> expanders = new List<System.Action>();
    // Back to the working layout (Damian, 2026-10-15: "put a reset default screen orientation button somehwere in case i
    // dork with it i can easily get back to working right"): the client sized and placed again with this window docked,
    // every section and resource group open, the map at its default zoom on the player, and the Info page.
    void ResetLayout() {
        foreach (var a in expanders) a();
        foreach (var g in AssistFeatures.ResourceGroups) f.Cfg.Set("collapsed.resources." + g, "0");
        resShape = "";
        mapView.Zoom = 2; mapView.OffX = mapView.OffY = 0; f.Cfg.Set("map.zoom", "2"); mapView.Invalidate();
        if (client != IntPtr.Zero) { RightSize(); Follow(); }
        ShowPage("info");
    }
    // A section: a header that collapses and expands it (remembered in the settings), over its controls.
    Panel Group(string title) {
        bool closed = f.Cfg.Get("collapsed." + title, "0") == "1";
        var box = new Panel { AutoSize = false, Margin = new Padding(3, 1, 3, 1) };
        pageOf[box] = title == "Map" ? "map" : title == "Trip report" ? "map.trip" : title == "Area analysis" ? "map.areas" : title == "Shopkeepers and merchants" ? "shops" : title == "Smithing value" || title == "Tailoring value" || title == "Other crafts value" ? "maker" : title == "Bag pricer" || title == "Reagent loadout" ? "pricer" : title.StartsWith("Circuit ") ? "circuit" : title == "Skills" ? "skills" : Array.IndexOf(ActionGroups, title) >= 0 ? "actions" : "info"; box.Visible = pageOf[box] == page;
        var head = new Button { Text = (closed ? "[+] " : "[-] ") + title, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, Height = 22, Dock = DockStyle.Top, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true };
        head.FlatAppearance.BorderSize = 0;
        var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = !closed, Location = new Point(6, 22) };
        titleOf[box] = title; headOf[box] = head;
        head.Click += (s, e) => { inner.Visible = !inner.Visible; Control sc; if (summaryBox.TryGetValue(box, out sc)) sc.Visible = !inner.Visible; f.Cfg.Set("collapsed." + title, inner.Visible ? "0" : "1"); Heads(); Fit(); };
        expanders.Add(() => { inner.Visible = true; Control sc; if (summaryBox.TryGetValue(box, out sc)) sc.Visible = false; f.Cfg.Set("collapsed." + title, "0"); Heads(); });
        box.Controls.Add(inner); box.Controls.Add(head); box.Tag = inner; flow.Controls.Add(box); return box;
    }
    // A collapsed section shows a one-line summary on its header (Damian, 2026-10-17: "for status and session expandos,
    // when contracted, show a 1 line summary. in fact, do that for all where it makes sense"), or a row of its own under
    // it (Resources' reagent icons).
    readonly Dictionary<Panel, string> titleOf = new Dictionary<Panel, string>(); readonly Dictionary<Panel, Button> headOf = new Dictionary<Panel, Button>();
    readonly Dictionary<Panel, Func<string>> summaryOf = new Dictionary<Panel, Func<string>>(); readonly Dictionary<Panel, Control> summaryBox = new Dictionary<Panel, Control>();
    void Summarize(Panel g, Func<string> line) { summaryOf[g] = line; }
    void SummaryRow(Panel g, Control row) { row.Location = new Point(6, 22); row.Visible = !((FlowLayoutPanel)g.Tag).Visible; g.Controls.Add(row); summaryBox[g] = row; }
    void Heads() {
        foreach (var kv in headOf) {
            bool open = ((FlowLayoutPanel)kv.Key.Tag).Visible; Func<string> line; string s = null;
            if (!open && summaryOf.TryGetValue(kv.Key, out line)) { try { s = line(); } catch { s = null; } }
            string text = (open ? "[-] " : "[+] ") + titleOf[kv.Key] + (string.IsNullOrEmpty(s) ? "" : ":  " + s.Replace("\n", "  ").Replace("&", "&&"));
            if (kv.Value.Text != text) kv.Value.Text = text;
        }
    }
    static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; int n = s.IndexOf('\n'); return n < 0 ? s : s.Substring(0, n); }
    T Add<T>(Panel g, T c) where T : Control { ((FlowLayoutPanel)g.Tag).Controls.Add(c); return c; }
    // An action's control carries its hotkey; the key button beside it shows the key, and a click then a key press changes it.
    void AddAction(string name, Panel g, string text, System.Action run, Control own = null) {
        var row = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        Control c = own ?? new Button { AutoSize = true };
        if (own == null) ((Button)c).Click += (s, e) => run();
        row.Controls.Add(c); actionControls[name] = c; actionText[name] = text ?? c.Text;
        var key = new Button { Width = 110, Text = "key", FlatStyle = FlatStyle.Flat };
        new ToolTip().SetToolTip(key, "Click, then press the key you want for this.");
        key.Click += (s, e) => { capturing = name; key.Text = "press a key..."; };
        row.Controls.Add(key); keyButtons[name] = key;
        runs[name] = run;
        LabelKeys();
    }
    readonly Dictionary<string, System.Action> runs = new Dictionary<string, System.Action>();
    // The control port's "action <name>": the window's action of that hotkey name, on the window's thread.
    public string RunAction(string name) {
        System.Action run; if (!runs.TryGetValue(name, out run)) return "no action named " + name + " (" + string.Join(", ", runs.Keys) + ")";
        BeginInvoke(run); return null;
    }
    Keys Key(string name) { int i = Array.IndexOf(Actions, name); return (Keys)f.Cfg.GetInt("key." + name, (int)DefaultKeys[i]); }
    void LabelKeys() { foreach (var kv in actionControls) { kv.Value.Text = actionText[kv.Key]; keyButtons[kv.Key].Text = "Key: " + Key(kv.Key); } }

    void OnKeyCapture(object s, KeyEventArgs e) {
        if (capturing == null) return;
        f.Cfg.Set("key." + capturing, ((int)e.KeyCode).ToString()); capturing = null; e.Handled = true;
        LabelKeys(); if (keysOn) { HotKeys(false); HotKeys(true); }
    }

    // No window scroll bar (Damian, 2026-10-15: "all of the panels of the uoaix assist need to fit the screen with no full
    // window scroll bars in any resolution", "if it doesn't fit, just let it underflow the screen, but make it fit a properly
    // sized screen"): the skills list and the events list take what height is left, down to a floor; past that the
    // bottom runs off the window.
    const int SkillsMin = 80, NotesMin = 40, MapMin = 200, ShopsMin = 120, PricerMin = 160, TripMin = 160, AreaDetailMin = 120;
    void Fit() {
        if (flow == null || skills == null || talk == null || fitting || dropped > 0) return;
        fitting = true;
        try {
            foreach (var b in new[] { infoTab, skillsTab, actionsTab, mapTab, shopsTab, pricerTab, makerTab, circuitTab }) {
                bool on = b == (page == "info" ? infoTab : page == "skills" ? skillsTab : page == "maker" ? makerTab : page == "actions" ? actionsTab : page.StartsWith("map") ? mapTab : page == "pricer" ? pricerTab : page == "circuit" ? circuitTab : shopsTab);
                b.Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular);
                b.BackColor = on ? SystemColors.ControlLightLight : SystemColors.Control;
                b.FlatAppearance.BorderColor = on ? SystemColors.Highlight : SystemColors.ControlDark;
                b.FlatAppearance.BorderSize = on ? 2 : 1;
            }
            int w = flow.ClientSize.Width - 8;
            skills.Height = SkillsMin; talk.Height = NotesMin; if (mapView != null) mapView.Height = MapMin; if (shops != null) shops.Height = ShopsMin; if (pricer != null) pricer.Height = PricerMin;
            if (tripText != null) tripText.Height = TripMin; if (areaList != null) { areaList.Height = TripMin; areaDetail.Height = AreaDetailMin; }
            Measure(w);
            int spare = flow.ClientSize.Height - Used() - 4;
            if (page == "map" && mapView != null && spare > 0) { mapView.Height += spare; Measure(w); }
            if (page == "pricer" && pricer != null && spare > 0) { pricer.Height += spare; Measure(w); }
            if (page == "map.trip" && tripText != null && spare > 0) { tripText.Height += spare; Measure(w); }
            if (page == "map.areas" && areaList != null && spare > 0) { areaList.Height += spare / 2; areaDetail.Height += spare - spare / 2; Measure(w); }
            if (page == "shops" && shops != null && shopItems != null && spare > 0) { shops.Height += spare / 2; shopItems.Height = 160 + spare - spare / 2; Measure(w); }
            bool info = page == "info";
            bool skillsOpen = page == "skills" && f.Cfg.Get("collapsed.Skills", "0") != "1", notesOpen = info && f.Cfg.Get("collapsed.Chat and events", "0") != "1";
            if (spare > 0 && (skillsOpen || notesOpen)) {
                if (skillsOpen && notesOpen) { skills.Height += spare * 3 / 5; talk.Height += spare - spare * 3 / 5; }
                else if (skillsOpen) skills.Height += spare; else talk.Height += spare;
                Measure(w);
            }
            if (autoFlow != null) foreach (Control c in autoFlow.Controls) c.Width = Math.Max(100, autoFlow.ClientSize.Width - 8);
            bool under = Used() > flow.ClientSize.Height;
            if (underflow.Visible != under) underflow.Visible = under;
        } finally { fitting = false; }
    }
    int Used() { int used = 0; foreach (Control g in flow.Controls) if (OnPage(g)) used += g.Height + g.Margin.Vertical; return used; }
    readonly HashSet<Control> growers = new HashSet<Control>();
    static int Grown(Label l) { return string.IsNullOrEmpty(l.Text) ? 4 : l.GetPreferredSize(new Size(Math.Max(100, l.Width), 0)).Height + 4; }
    void Measure(int w) {
        foreach (Control g in flow.Controls) {
            g.Width = w; var inner = (FlowLayoutPanel)g.Tag; int h = 0;
            foreach (Control c in inner.Controls) {
                c.Width = w - 20;
                // A wrapping text card takes the height of its text at the width just given (Damian, 2026-10-15: "session
                // card under flows the alloted space, can't read it all").
                if (growers.Contains(c)) c.Height = Grown((Label)c);
                if (c.Visible) h += c.Height + c.Margin.Vertical;
            }
            inner.Width = w - 8; inner.Height = h + 4;
            Control sc; bool row = summaryBox.TryGetValue((Panel)g, out sc) && sc.Visible; if (row) sc.Width = w - 20;
            g.Height = 22 + (inner.Visible ? inner.Height : row ? sc.Height + 2 : 0);
        }
    }

    LoopKind Kind() { return AssistFeatures.Loops[Math.Max(0, loops.SelectedIndex)]; }
    void ToggleLoop() {
        if (f.Status.StartsWith("Loop ") && !f.Status.Contains(" done") && !f.Status.Contains(" stopped")) { f.Stop("the loop key"); return; }
        string why = f.Start(Kind(), (int)rounds.Value, (int)(untilSkill.Value * 10), null);
        if (why != null) Note(why);
    }
    void Note(string text) { notes.Items.Add(DateTime.Now.ToString("HH:mm:ss") + " " + text); notes.TopIndex = notes.Items.Count - 1; }

    // ---------- docking ----------
    public string DockToClient() {
        var k = relay.Link(); int port = relay.LastClientPort;
        if (client == IntPtr.Zero && port != 0) {
            int pid = AssistNative.TcpPid(port, relay.ListenPort);
            if (pid != 0) {
                var h = AssistNative.MainWindow(pid);
                if (h != IntPtr.Zero) { client = h; clientPid = pid; RightSize(); Hook(); }
            }
        }
        if (client == IntPtr.Zero) { int pid = port == 0 ? 0 : AssistNative.TcpPid(port, relay.ListenPort); return "not docked: client port " + port + " pid " + pid + " window " + (pid == 0 ? "-" : AssistNative.MainWindow(pid).ToString()); }
        var r = AssistNative.Visible(client); AssistNative.RECT area; AssistNative.GetClientRect(client, out area);
        return "docked to pid " + clientPid + " client " + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom + " area " + area.Right + "x" + area.Bottom + " window " + Left + "," + Top + "," + Right + "," + Bottom + " visible " + AssistNative.IsWindowVisible(Handle) + " keys " + keysOn;
    }
    // The 1.25.32 client stretches its 640x480 frame over its client area, so any other size draws torn text (Damian,
    // 2026-10-15: "the graphics renders tearing of fonts and stuff if its off by a pixel"). At dock the client area is set
    // to the largest whole multiple of 640x480 that leaves this window MinWidth beside it, the visible frame at the top
    // left of the work area of the screen the client is on.
    void RightSize() {
        if (AssistNative.IsIconic(client)) return;
        if (AssistNative.IsZoomed(client)) AssistNative.ShowWindow(client, 9);  // SW_RESTORE: a maximized window keeps its size
        AssistNative.RECT outer, area; AssistNative.GetWindowRect(client, out outer); AssistNative.GetClientRect(client, out area);
        var seen = AssistNative.Visible(client);
        int frameW = outer.Right - outer.Left - area.Right, frameH = outer.Bottom - outer.Top - area.Bottom;
        int padL = seen.Left - outer.Left, padT = seen.Top - outer.Top, padR = outer.Right - seen.Right, padB = outer.Bottom - seen.Bottom;
        var work = Screen.FromRectangle(new Rectangle(seen.Left, seen.Top, seen.Right - seen.Left, seen.Bottom - seen.Top)).WorkingArea;
        int k = Math.Max(1, Math.Min((work.Width - MinWidth - (frameW - padL - padR)) / 640, (work.Height - (frameH - padT - padB)) / 480));
        AssistNative.SetWindowPos(client, IntPtr.Zero, work.Left - padL, work.Top - padT, 640 * k + frameW, 480 * k + frameH, 0x0004 | 0x0010);  // SWP_NOZORDER | SWP_NOACTIVATE
    }
    void Hook() {
        // No owner link: a cross-process owner attaches the two input queues, and the client's hidden cursor then
        // hides the Windows cursor over this window too (Damian, 2026-10-15). Z-order follows the foreground instead.
        locProc = (hk, ev, h, obj, child, th, time) => { if (h == client && obj == 0) Follow(); };
        locHook = AssistNative.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, locProc, clientPid, 0, 0);  // EVENT_OBJECT_LOCATIONCHANGE
        fgProc = (hk, ev, h, obj, child, th, time) => { HotKeys(h == client); Stack(h); };
        fgHook = AssistNative.SetWinEventHook(3, 3, IntPtr.Zero, fgProc, 0, 0, 0);                       // EVENT_SYSTEM_FOREGROUND
        Follow(); var fg = AssistNative.GetForegroundWindow(); HotKeys(fg == client); Stack(fg);
    }
    // The title-bar bars: over the client's title bar (its visible top down to its client area), left of the caption
    // buttons; owned by this window, so they stay above the client with it.
    TitleBars bars;
    void PlaceBars(AssistNative.RECT r) {
        var pt = new Point(0, 0); AssistNative.ClientToScreen(client, ref pt);
        int h = pt.Y - r.Top;
        if (bars == null) { bars = new TitleBars(); bars.Show(this); }
        if (h < 12 || r.Right - r.Left < TitleBars.BarsWidth + TitleBars.CaptionButtons + 100) { bars.Hide(); return; }
        bars.Bounds = new Rectangle(r.Right - TitleBars.CaptionButtons - TitleBars.BarsWidth, r.Top + 2, TitleBars.BarsWidth, h - 4);
        if (!bars.Visible) bars.Show();
    }
    void Follow() {
        if (client == IntPtr.Zero) return;
        if (AssistNative.IsIconic(client)) { if (Visible) Hide(); if (bars != null) bars.Hide(); return; }
        var r = AssistNative.Visible(client);
        PlaceBars(r);
        // This window's own invisible border, measured once it has a handle: place its visible frame on the client's.
        int padL = 0, padT = 0, padR = 0, padB = 0;
        if (IsHandleCreated) {
            AssistNative.RECT outer; AssistNative.GetWindowRect(Handle, out outer); var seen = AssistNative.Visible(Handle);
            padL = seen.Left - outer.Left; padT = seen.Top - outer.Top; padR = outer.Right - seen.Right; padB = outer.Bottom - seen.Bottom;
        }
        var screen = Screen.FromRectangle(new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)).WorkingArea;
        int w = Math.Max(MinWidth, Math.Min(Width0 * 2, screen.Right - r.Right));
        Bounds = new Rectangle(r.Right - padL, r.Top - padT, w + padL + padR, r.Bottom - r.Top + padT + padB);
        if (!Visible) Show();
    }
    // Directly above the client in z-order, always (Damian, 2026-10-15: "the uoaix assist window is not at the same z level
    // as the client window ... the uoclient will be on top of agentgrid, but then uoaixassist is behind agentgrid"): a
    // window that comes between them is stepped over, and clicking this window brings the client up right beneath it.
    // Run on every foreground change and every Tick, because a raise of the client sends no foreground event to us.
    void Stack(IntPtr fg) {
        if (client == IntPtr.Zero || dropped > 0) return;
        const uint keep = 0x0001 | 0x0002 | 0x0010;  // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
        if (fg == Handle) { if (AboveClient() != Handle) AssistNative.SetWindowPos(client, Handle, 0, 0, 0, 0, keep); return; }
        var above = AboveClient();
        if (above == Handle) return;
        if (above != IntPtr.Zero) { AssistNative.SetWindowPos(Handle, above, 0, 0, 0, 0, keep); return; }  // after `above`: between it and the client
        // The client heads the non-topmost band. Windows ignores HWND_TOP from a background process when the foreground
        // window would be passed, and the title-bar meter went under the client (Damian, 2026-10-16: "when i am in the
        // client, the overlay hits/mana/stamina meter disappears"); a topmost round trip lands at the head of the band.
        AssistNative.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, keep);  // HWND_TOPMOST
        AssistNative.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, keep);  // HWND_NOTTOPMOST
    }
    // The nearest visible non-topmost window above the client (GW_HWNDPREV), or IntPtr.Zero when none is.
    IntPtr AboveClient() {
        var h = AssistNative.GetWindow(client, 3);
        while (h != IntPtr.Zero && h != Handle && (!AssistNative.IsWindowVisible(h) || (AssistNative.GetWindowLong(h, -20) & 0x8) != 0)) h = AssistNative.GetWindow(h, 3);  // GWL_EXSTYLE, WS_EX_TOPMOST
        return h;
    }
    void HotKeys(bool on) {
        if (on == keysOn) return;
        for (int i = 0; i < Actions.Length; i++) {
            if (on) AssistNative.RegisterHotKey(Handle, 0x4100 + i, 0, (uint)Key(Actions[i])); else AssistNative.UnregisterHotKey(Handle, 0x4100 + i);
        }
        keysOn = on;
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x0312) { int i = m.WParam.ToInt32() - 0x4100; if (i >= 0 && i < Actions.Length) runs[Actions[i]](); return; }
        base.WndProc(ref m);
    }

    // ---------- refresh ----------
    // Status as Damian asked (2026-10-18: "for attributes, show buff and debuff, base versus current. add weight capacity
    // ... show weight of gold and color it to indicate if you are carrying too much gold (over 300). armor should color
    // code if your armor is in need of repair"). The server sends base plus buff only (OUO packet_manager.c:1206-1208), so
    // the base is learned: a change of 1 is a gain or loss and moves it, a larger jump is a buff or debuff and does not.
    // The carry limit is base strength * 4 + 30 (OUO mobile.c:3447-3450); gold weighs (amount + 49) / 50 (item.c:4165-4169).
    // The client is never sent armor durability (OUO weapon.c:901-906 wears the rating down), so the rating is held against
    // the best seen with the same worn items: under it, amber; at RepairRed of it or less, red.
    public const int GoldCarry = 300; const double RepairRed = 0.8;
    readonly Dictionary<string, int> bestArmor = new Dictionary<string, int>();
    int BaseStat(AssistState st, string name, int cur) {
        string key = "base." + st.Name + "." + name; int b = f.Cfg.GetInt(key, int.MinValue);
        if (b == int.MinValue || Math.Abs(cur - b) == 1) { if (b != cur && cur > 0) f.Cfg.Set(key, cur.ToString()); return cur; }
        return b;
    }
    string StatText(AssistState st, string name, out Color color) {
        color = SystemColors.ControlText;
        switch (name) {
            case "strength": case "dexterity": case "intelligence": {
                int cur = name == "strength" ? st.Str : name == "dexterity" ? st.Dex : st.Int, b = BaseStat(st, name, cur), d = cur - b;
                if (d > 0) color = Color.FromArgb(0, 130, 0); else if (d < 0) color = Color.FromArgb(190, 30, 30);
                return cur + (d != 0 ? "  (base " + b + ", " + (d > 0 ? "+" : "") + d + ")" : "");
            }
            case "weight": { int bs = BaseStat(st, "strength", st.Str), cap = bs * 4 + 30; if (st.Weight > cap) color = Color.FromArgb(190, 30, 30); return st.Weight + " / " + cap; }
            case "gold": { if (st.Gold > GoldCarry) color = Color.FromArgb(200, 120, 0); return st.Gold + "  (" + (st.Gold + 49) / 50 + " st)"; }
            case "armor": {
                var worn = new List<uint>(); foreach (var kv in st.Items) if (kv.Value.Layer >= 0 && kv.Value.Mobile == st.Player) worn.Add(kv.Key); worn.Sort();
                string key = string.Join(",", worn); int best; if (!bestArmor.TryGetValue(key, out best) || st.Armor > best) bestArmor[key] = best = st.Armor;
                if (st.Armor < best) color = st.Armor <= best * RepairRed ? Color.FromArgb(190, 30, 30) : Color.FromArgb(200, 120, 0);
                return st.Armor + (st.Armor < best ? "  (best " + best + ", repair)" : "");
            }
            case "dps": return Dps(st);
            case "vs": return SpellTaken(st);
        }
        return StatValue(st, name);
    }
    static int SkillOf(AssistState st, int id) { int v; return st.Skills.TryGetValue(id + 1, out v) ? v : 0; }
    // The defender and the caster the combat row is figured against, picked by a click on the value (Damian, 2026-10-18:
    // "allow me to click that field and pick any magic caster and spell. also for dps, allow setting the relevant
    // defender details. assume troll for default"), from the server's templates (UoBestiary).
    static string[] Creature(string name) { foreach (var r in UoBestiary.Creatures) { var p = r.Split('|'); if (string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase)) return p; } return null; }
    static string[] SpellRow(string name) { foreach (var r in UoBestiary.Spells) { var p = r.Split('|'); if (string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase)) return p; } return null; }
    static string Abbrev(string spell) { var w = spell.Split(' '); return w.Length > 1 ? (w[0].Substring(0, 1) + w[1].Substring(0, 1)).ToUpperInvariant() : spell.Equals("Flamestrike", StringComparison.OrdinalIgnoreCase) ? "FS" : spell.Length > 2 ? spell.Substring(0, 2).ToUpperInvariant() : spell; }
    string Defender { get { return f.Cfg.Get("status.defender", "troll"); } }
    string Caster { get { return f.Cfg.Get("status.caster", "lich"); } }
    string Spell { get { return f.Cfg.Get("status.spell", "Flamestrike"); } }
    void PickCombat(string field) {
        if (field == "dps") { var c = PickName("Defender for the DPS", Array.ConvertAll(UoBestiary.Creatures, r => r.Split('|')[0]), Defender); if (c != null) f.Cfg.Set("status.defender", c); return; }
        var casters = Array.FindAll(UoBestiary.Creatures, r => { int m; return int.TryParse(r.Split('|')[6], out m) && m > 0; });
        var who = PickName("Caster", Array.ConvertAll(casters, r => r.Split('|')[0]), Caster); if (who == null) return;
        var sp = PickName("Spell", Array.ConvertAll(UoBestiary.Spells, r => r.Split('|')[0]), Spell); if (sp == null) return;
        f.Cfg.Set("status.caster", who); f.Cfg.Set("status.spell", sp);
    }
    // A list to pick one name from, filtered as you type; null when cancelled.
    string PickName(string title, string[] names, string current) {
        using (var dlg = new Form { Text = title, Width = 320, Height = 420, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow }) {
            var filter = new TextBox { Dock = DockStyle.Top }; var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var ok = new Button { Dock = DockStyle.Bottom, Text = "Pick", Height = 28, DialogResult = DialogResult.OK };
            Action fill = () => { list.BeginUpdate(); list.Items.Clear(); foreach (var n in names) if (n.IndexOf(filter.Text, StringComparison.OrdinalIgnoreCase) >= 0) list.Items.Add(n); list.EndUpdate(); int at = list.Items.IndexOf(current); if (at >= 0) list.SelectedIndex = at; };
            filter.TextChanged += (s, e) => fill(); list.DoubleClick += (s, e) => { if (list.SelectedItem != null) dlg.DialogResult = DialogResult.OK; };
            dlg.Controls.Add(list); dlg.Controls.Add(filter); dlg.Controls.Add(ok); dlg.AcceptButton = ok; fill();
            return dlg.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null ? (string)list.SelectedItem : null;
        }
    }
    // A weapon's melee damage per second on the defender, from the 1998 server (OUO combat.c): the hit chance is
    // (skill + 500) * 50 / (defender's wrestling + 500) percent, at least 2 (1923-1942); a hit is (tactics + base strength
    // * 2 + 500) * the weapon's dice / 1000 (326-346), less the armor's absorption, on average between AR / 4 and
    // (AR + 1) / 2 (1259-1268), halved, at least 1 (1504-1507); a swing takes 2 * (40000 / ((base dex + 100) * speed)) + 1
    // ticks of 250 ms, 20 when the product is under 2000 (1644-1720).
    const int TacticsSkill = 27, ResistSkill = 26, SwordsSkill = 40, MaceSkill = 41, FencingSkill = 42;
    string Dps(AssistState st) {
        string[] w = null; string named = null;
        foreach (var it in st.Items.Values) if (it.Mobile == st.Player && (it.Layer == 1 || it.Layer == 2)) {
            var tiles = UoTiles.Items(); if (it.Graphic < 0 || it.Graphic >= tiles.Count) continue;
            int id = tiles[it.Graphic].Misc & 0xFF; foreach (var r in UoWeapons.Rows) if (r.StartsWith(id + "|")) { var p = r.Split('|'); if (p[4] != "Shield") { w = p; named = p[1]; } }
        }
        if (w == null) return "no weapon";
        // "NdM" or "NdM+K", averaged.
        var plus = w[2].Split('+'); var nd = plus[0].Split('d'); int n, m, add = 0;
        if (nd.Length != 2 || !int.TryParse(nd[0], out n) || !int.TryParse(nd[1], out m) || (plus.Length > 1 && !int.TryParse(plus[1], out add))) return named;
        double dice = n * (m + 1) / 2.0 + add;
        int str = BaseStat(st, "strength", st.Str), dex = BaseStat(st, "dexterity", st.Dex), speed = int.Parse(w[3]);
        var foe = Creature(Defender); int ar = foe != null ? int.Parse(foe[2]) : 0, wrestle = foe != null ? int.Parse(foe[3]) : 0;
        int skill = SkillOf(st, w[4] == "Bashing" ? MaceSkill : w[4] == "Piercing" ? FencingSkill : SwordsSkill);
        double chance = System.Math.Max(2, (skill + 500) * 50 / (wrestle + 500)) / 100.0;
        double raw = (SkillOf(st, TacticsSkill) + str * 2 + 500) * dice / 1000, absorb = (ar / 4 + (ar + 1) / 2) / 2.0;
        double hit = System.Math.Max(1, (raw - absorb) / 2);
        int prod = (dex + 100) * speed, b = prod < 2000 ? 20 : 40000 / prod; double swing = (2 * b + 1) * 0.25;
        return (hit * chance / swing).ToString("0.0") + " vs " + (foe != null ? foe[0] : Defender + "?") + "  (" + (chance * 100).ToString("0") + "% hit)";
    }
    // The picked caster's picked spell on the player, outside guards, from the 1998 scripts: num * 1d(die) + bonus by circle
    // (spelskil.m:1679-1688) scaled by (50 + Magery) / 100, Magery as the 0-100 level (1693-1706); resisted with Resisting
    // Spells against a difficulty of damage * 25, at most 1000, range 50 (1155-1179; 40 in guarded regions), the chance
    // (skill - difficulty) * 100 / range + 500 in thousandths (OUO skill.c:745, 891); a resist halves it, rounded up.
    string SpellTaken(AssistState st) {
        var who = Creature(Caster); var sp = SpellRow(Spell); if (who == null || sp == null) return "pick a caster";
        int magery = int.Parse(who[6]) / 10, num = int.Parse(sp[2]), die = int.Parse(sp[3]), bonus = int.Parse(sp[4]), resist = SkillOf(st, ResistSkill);
        double taken = 0, chance = 0;
        for (int roll = 1; roll <= die; roll++) {
            int dmg = (num * roll + bonus) * (50 + magery) / 100, diff = System.Math.Min(1000, dmg * 25);
            double p = System.Math.Max(0, System.Math.Min(1000, (resist - diff) * 100 / 50 + 500)) / 1000.0;
            taken += (dmg * (1 - p) + (dmg + 1) / 2 * p) / die; chance += p / die;
        }
        return who[0] + " " + Abbrev(sp[0]) + " " + taken.ToString("0") + " hp, " + (chance * 100).ToString("0") + "% resist";
    }
    static string StatValue(AssistState st, string name) {
        switch (name) {
            case "hits": return st.Hits + " / " + st.MaxHits;
            case "mana": return st.Mana + " / " + st.MaxMana;
            case "stamina": return st.Stam + " / " + st.MaxStam;
            case "strength": return st.Str.ToString();
            case "dexterity": return st.Dex.ToString();
            case "intelligence": return st.Int.ToString();
            case "weight": return st.Weight.ToString();
            case "gold": return st.Gold.ToString();
            case "armor": return st.Armor.ToString();
        }
        return "";
    }
    // The client's title, then the display choices after TitleMark; null state puts the client's own title back.
    // Set only when it differs, so the client's title is written once per change.
    void Title(AssistState st) {
        string now = AssistNative.Title(client); int cut = now.IndexOf(TitleMark);
        string want = cut >= 0 ? now.Substring(0, cut) : now;
        if (st != null) {
            var parts = new List<string>();
            lock (st.Gate) foreach (var n in AssistFeatures.DisplayNames) if (f.Shown(n)) parts.Add(n + " " + StatValue(st, n).Replace(" / ", "/"));
            if (parts.Count > 0) want += TitleMark + string.Join("  ", parts);
        }
        if (now != want) AssistNative.SetTitle(client, want);
    }
    void Border() {
        bool halted = AssistGuard.Current != null && AssistGuard.Current.Disabled != null;
        bool looping = f.Status.StartsWith("Loop ") && !f.Status.Contains(" done") && !f.Status.Contains(" stopped");
        bool configuring = looping && f.Status.Contains(": round zero");
        // Black once a loop completed (Damian, 2026-10-15: "when it completes successfully, turn it black"), until the next.
        bool done = f.Status.StartsWith("Loop ") && f.Status.Contains(" done");
        var want = halted ? Color.FromArgb(200, 40, 40) : configuring ? Color.FromArgb(235, 200, 40) : looping ? Color.FromArgb(40, 170, 60) : done ? Color.Black : SystemColors.Control;
        if (BackColor != want) BackColor = want;
    }
    ListBox mapDetail, shopInfo; ListView shopItems; SortList shops; ComboBox shopCity, shopKind; TextBox shopFind; string shopShape = "";
    bool Hidden(string kind) { return Array.IndexOf(f.Cfg.Get("map.hide", "").Split('|'), kind) >= 0; }
    void SetHidden(string kind, bool hide) {
        var l = new List<string>(f.Cfg.Get("map.hide", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)); l.Remove(kind); if (hide) l.Add(kind);
        f.Cfg.Set("map.hide", string.Join("|", l));
    }
    static Color TypeColor(string type, bool criminal) {
        if (criminal) return Color.Red;
        switch (type) {
            case "Banker": return Color.Gold; case "Shopkeeper": return Color.LimeGreen; case "Healer": return Color.DeepSkyBlue;
            case "Guard": return Color.RoyalBlue; case "NPC": return Color.Silver; case "Person": return Color.White;
            case "Ghost": return Color.LightGray; case "Player vendor": return Color.Aquamarine;
            case "House": return Color.SandyBrown; case "Guildstone": return Color.MediumOrchid; case "Anvil": return Color.SlateGray; case "Forge": return Color.OrangeRed; default: return Color.Orange;
        }
    }
    // Pins, kept in the "pins" setting: x, y, kind, label each, | between pins.
    List<MapView.Pin> LoadPins() {
        var list = new List<MapView.Pin>();
        foreach (var e in f.Cfg.Get("pins", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) {
            var t = e.Split(new[] { ',' }, 4); int x, y; if (t.Length < 4 || !int.TryParse(t[0], out x) || !int.TryParse(t[1], out y)) continue;
            list.Add(new MapView.Pin { X = x, Y = y, Kind = t[2], Label = t[3] });
        }
        return list;
    }
    void SavePins(List<MapView.Pin> pins) { f.Cfg.Set("pins", string.Join("|", pins.ConvertAll(p => p.X + "," + p.Y + "," + p.Kind + "," + p.Label.Replace("|", "/")))); mapView.Invalidate(); }
    // Right-click on the map: add a pin there, or edit or remove the pin under the pointer.
    void PinMenu(int x, int y, Point at) {
        var pins = LoadPins(); MapView.Pin near = null;
        foreach (var p in pins) { var px = mapView.PixelOf(p.X, p.Y); if (Math.Abs(px.X - at.X) <= 8 && at.Y >= px.Y - 16 && at.Y <= px.Y + 2) near = p; }
        var menu = new ContextMenuStrip();
        if (near != null) {
            var hit = near;
            menu.Items.Add("Edit pin \"" + hit.Label + "\"...", null, (s, e) => EditPin(hit, hit.X, hit.Y));
            menu.Items.Add("Remove pin \"" + hit.Label + "\"", null, (s, e) => { var l = LoadPins(); l.RemoveAll(p => p.X == hit.X && p.Y == hit.Y && p.Label == hit.Label); SavePins(l); });
        } else menu.Items.Add("Add a pin at " + x + "," + y + "...", null, (s, e) => EditPin(null, x, y));
        menu.Show(mapView, at);
    }
    void EditPin(MapView.Pin was, int x, int y) {
        using (var dlg = new Form { Text = was == null ? "Add a pin at " + x + "," + y : "Edit pin", Width = 320, Height = 170, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedToolWindow }) {
            var label = new TextBox { Left = 10, Top = 10, Width = 285, Text = was != null ? was.Label : "" };
            var kind = new ComboBox { Left = 10, Top = 42, Width = 285, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var k in MapView.PinKinds) kind.Items.Add(k); kind.SelectedItem = was != null ? was.Kind : "My house"; if (kind.SelectedIndex < 0) kind.SelectedIndex = 0;
            var ok = new Button { Text = "OK", Left = 134, Top = 82, Width = 75, DialogResult = DialogResult.OK }; var cancel = new Button { Text = "Cancel", Left = 220, Top = 82, Width = 75, DialogResult = DialogResult.Cancel };
            dlg.Controls.AddRange(new Control[] { label, kind, ok, cancel }); dlg.AcceptButton = ok; dlg.CancelButton = cancel;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string text = label.Text.Trim(); if (text.Length == 0) text = (string)kind.SelectedItem;
            var pins = LoadPins(); if (was != null) pins.RemoveAll(p => p.X == was.X && p.Y == was.Y && p.Label == was.Label);
            pins.Add(new MapView.Pin { X = x, Y = y, Kind = (string)kind.SelectedItem, Label = text }); SavePins(pins);
        }
    }
    MapView.Mark[] MapMarks() {
        if (f.Atlas == null) return new MapView.Mark[0];
        var hide = new HashSet<string>(f.Cfg.Get("map.hide", "").Split('|')); var list = new List<MapView.Mark>();
        foreach (var n in f.Atlas.Visible()) if (!hide.Contains(n.Type) && !hide.Contains(n.Type + ":" + n.Sub)) list.Add(new MapView.Mark { X = n.X, Y = n.Y, Serial = n.Serial, Color = TypeColor(n.Type, n.Criminal), Scale = MapView.TierScale(n.Name), Body = n.Body >= 0x190 && n.Body <= 0x193 ? 0 : n.Body });
        foreach (var p in f.Atlas.Places()) if (!hide.Contains(p.Kind) && !hide.Contains(p.Kind + ":" + AssistAtlas.PlaceSub(p))) list.Add(new MapView.Mark { X = p.X, Y = p.Y, Serial = p.Serial, Color = TypeColor(p.Kind, false) });
        if (f.Cfg.Get("map.teleportonly", "0") == "1") foreach (var s in f.TeleportOnlySpots()) list.Add(new MapView.Mark { X = s[0], Y = s[1], Color = Color.Magenta });
        return list.ToArray();
    }
    // Every type:subtype the atlas has seen, ticked when shown.
    void SubtypeFilter() {
        if (f.Atlas == null) return;
        using (var dlg = new Form { Text = "Map subtypes", Width = 360, Height = 520, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow }) {
            var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            foreach (var kind in f.Atlas.Kinds()) list.Items.Add(kind, !Hidden(kind));
            list.ItemCheck += (s, e) => { SetHidden((string)list.Items[e.Index], e.NewValue != CheckState.Checked); mapView.Invalidate(); };
            dlg.Controls.Add(list); dlg.ShowDialog(this);
        }
    }
    // A buy list (Damian, 2026-10-15: "sort the list by name and then by any quality/price descending. if magic items are
    // in the list, put them at the top and make them blue and bold", "include quantity in the list, make it a grid"). Rows
    // are "name: price gp x quantity", the quantity absent when no contents list came first; a 1998 item's quality shows
    // only in its price (Helmet at 169 and 183). Magic is read from the 1998 OSI name words, unseen on uo1998's wire.
    static readonly string[] MagicWords = { "magic", " of ruin", " of might", " of force", " of power", " of vanquishing", " of defense",
        " of guarding", " of hardening", " of fortification", " of invulnerability", "accurate", "surpassingly", "eminently", "exceedingly",
        "supremely", "durable", "substantial", "massive", "fortified", "indestructible", " charges" };
    public static bool IsMagic(string s) { foreach (var w in MagicWords) if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
    static string StockName(string i) { int c = i.LastIndexOf(": "); return string.Join(" ", (c < 0 ? i : i.Substring(0, c)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); }
    static string[] StockTail(string i) { int c = i.LastIndexOf(": "); return c < 0 ? new string[0] : i.Substring(c + 2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); }
    static int StockPrice(string i) { var t = StockTail(i); int p; return t.Length > 0 && int.TryParse(t[0], out p) ? p : 0; }
    static string StockQty(string i) { var t = StockTail(i); return t.Length > 3 && t[2] == "x" ? t[3] : ""; }
    static List<string> SortedStock(List<string> items) {
        var l = new List<string>(items);
        l.Sort((a, b) => { int m = IsMagic(b).CompareTo(IsMagic(a)); if (m != 0) return m; int n = string.Compare(StockName(a), StockName(b), StringComparison.OrdinalIgnoreCase); return n != 0 ? n : StockPrice(b).CompareTo(StockPrice(a)); });
        return l;
    }
    // Off the norm (Damian, 2026-10-15: "highlight any deviation from the norm. if they have cheap ingots or expensive ones,
    // or if they have large quantities available, or something unique unseen at others of the same type"): each row against
    // the same item at every OTHER recorded vendor. Cheap or dear is CheapPct below or DearPct above their median price,
    // large is LargeTimes their median quantity, unique is no other vendor of the same trade selling it.
    public const int CheapPct = 15, DearPct = 15, LargeTimes = 2;
    public const int OffCheap = 1, OffDear = 2, OffLarge = 4, OffUnique = 8;
    sealed class Offer { public uint Vendor; public string Kind; public int Price, Qty; }
    // The NPC's own title when the server sent one (the UOAIX shard), else the shop region's trade (uo1998 sends bare names).
    static string Kind(AtlasNpc n) { return n.Sub.Length > 0 && n.Sub != "vendor" && n.Sub != "untitled" && n.Sub != "unnamed" ? n.Sub : UoRegions.ShopKind(n.X, n.Y) ?? n.Sub; }
    static Dictionary<string, List<Offer>> Market(List<AtlasNpc> vendors) {
        var m = new Dictionary<string, List<Offer>>();
        foreach (var v in vendors) {
            string kind = Kind(v);
            foreach (var i in v.Items) {
                string key = StockName(i).ToLowerInvariant(); List<Offer> l; if (!m.TryGetValue(key, out l)) m[key] = l = new List<Offer>();
                int q; l.Add(new Offer { Vendor = v.Serial, Kind = kind, Price = StockPrice(i), Qty = int.TryParse(StockQty(i), out q) ? q : -1 });
            }
        }
        return m;
    }
    static int Median(List<int> l) { l.Sort(); return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
    // The flags and the note for one row of vendor v; kindSellers is how many OTHER vendors of v's trade have a list.
    static int OffNorm(AtlasNpc v, string kind, string row, Dictionary<string, List<Offer>> market, int kindSellers, out string note) {
        note = ""; int flags = 0; List<Offer> all; if (!market.TryGetValue(StockName(row).ToLowerInvariant(), out all)) return 0;
        var prices = new List<int>(); var qtys = new List<int>(); var shops = new HashSet<uint>(); bool sameKind = false;
        foreach (var o in all) if (o.Vendor != v.Serial) { prices.Add(o.Price); if (o.Qty >= 0) qtys.Add(o.Qty); shops.Add(o.Vendor); if (o.Kind == kind) sameKind = true; }
        var notes = new List<string>();
        int price = StockPrice(row), qty; bool hasQty = int.TryParse(StockQty(row), out qty);
        if (prices.Count > 0) {
            int med = Median(prices);
            if (price * 100 <= med * (100 - CheapPct)) { flags |= OffCheap; notes.Add("cheap: " + price + " vs " + med + " typical at " + shops.Count + " other shops"); }
            else if (price * 100 >= med * (100 + DearPct)) { flags |= OffDear; notes.Add("expensive: " + price + " vs " + med + " typical at " + shops.Count + " other shops"); }
        }
        if (hasQty && qtys.Count > 0) { int med = Median(qtys); if (med > 0 && qty >= med * LargeTimes) { flags |= OffLarge; notes.Add("large stock: " + qty + " vs " + med + " typical"); } }
        if (!sameKind && kindSellers > 0) { flags |= OffUnique; notes.Add("unique: no other " + kind + " of " + kindSellers + " sells it"); }
        note = string.Join("; ", notes); return flags;
    }
    static int KindSellers(AtlasNpc v, string kind, List<AtlasNpc> vendors) { int c = 0; foreach (var o in vendors) if (o.Serial != v.Serial && o.Items.Count > 0 && Kind(o) == kind) c++; return c; }
    static string OffSummary(int cheap, int dear, int large, int unique) {
        var l = new List<string>(); if (cheap > 0) l.Add(cheap + " cheap"); if (dear > 0) l.Add(dear + " expensive"); if (large > 0) l.Add(large + " large stock"); if (unique > 0) l.Add(unique + " unique");
        return string.Join(", ", l);
    }
    static Color OffColor(int flags) {
        if ((flags & OffCheap) != 0) return Color.FromArgb(200, 240, 200); if ((flags & OffDear) != 0) return Color.FromArgb(250, 210, 210);
        if ((flags & OffUnique) != 0) return Color.FromArgb(255, 235, 175); if ((flags & OffLarge) != 0) return Color.FromArgb(215, 228, 250); return Color.Empty;
    }
    // The detail lists draw a magic item's row bold and blue.
    static void StockDraw(ListBox b) {
        b.DrawMode = DrawMode.OwnerDrawFixed; var bold = new Font(b.Font, FontStyle.Bold);
        b.DrawItem += (s, e) => {
            e.DrawBackground(); if (e.Index < 0) return;
            string t = b.Items[e.Index].ToString(); bool magic = t.StartsWith("   ") && IsMagic(t);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(e.Graphics, t, magic ? bold : e.Font, e.Bounds, sel ? SystemColors.HighlightText : magic ? Color.Blue : e.ForeColor, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };
    }
    // With a grid, the buy list goes to the grid (item, price, quantity) and the box keeps the rest.
    void ShowNpc(ListBox box, ListView grid, uint serial) {
        box.Items.Clear(); if (grid != null) grid.Items.Clear(); if (f.Atlas == null || serial == 0) return;
        try { ShowNpcRows(box, grid, serial); }
        finally { int w = 0; var bold = new Font(box.Font, FontStyle.Bold); foreach (var o in box.Items) w = Math.Max(w, TextRenderer.MeasureText(o.ToString(), bold).Width); box.HorizontalExtent = w + 8; bold.Dispose(); }
    }
    // Fills the grid; returns the off-the-norm summary for the details list.
    string FillStock(ListView grid, AtlasNpc n) {
        var vendors = f.Atlas.Shops(); var market = Market(vendors); string kind = Kind(n); int sellers = KindSellers(n, kind, vendors);
        int cheap = 0, dear = 0, large = 0, unique = 0;
        grid.BeginUpdate(); var bold = new Font(grid.Font, FontStyle.Bold);
        foreach (var i in SortedStock(n.Items)) {
            string note; int off = OffNorm(n, kind, i, market, sellers, out note);
            if ((off & OffCheap) != 0) cheap++; if ((off & OffDear) != 0) dear++; if ((off & OffLarge) != 0) large++; if ((off & OffUnique) != 0) unique++;
            var row = new ListViewItem(new[] { StockName(i), StockPrice(i).ToString(), StockQty(i), note }) { UseItemStyleForSubItems = true };
            if (IsMagic(i)) { row.ForeColor = Color.Blue; row.Font = bold; }
            if (off != 0) row.BackColor = OffColor(off);
            grid.Items.Add(row);
        }
        grid.EndUpdate();
        string sum = OffSummary(cheap, dear, large, unique);
        return "Off the norm against " + (vendors.Count - 1) + " other recorded vendors (" + sellers + " " + kind + "): " + (sum.Length > 0 ? sum : "nothing");
    }
    void ShowNpcRows(ListBox box, ListView grid, uint serial) {
        AtlasNpc n = null; foreach (var x in f.Atlas.Visible()) if (x.Serial == serial) n = x;
        if (n == null) foreach (var x in f.Atlas.Shops()) if (x.Serial == serial) n = x;
        if (n == null) {
            foreach (var p in f.Atlas.Places()) if (p.Serial == serial) {
                box.Items.Add((p.Name.Length > 0 ? p.Name : "unnamed") + "  (" + p.Kind + (p.Kind == "House" ? ", multi 0x" + (p.Graphic & 0x3FFF).ToString("X") : "") + ", " + p.Serial.ToString("X") + ")");
                box.Items.Add("at " + p.X + "," + p.Y + "  " + Sextant(p.X, p.Y) + "  seen " + p.Last.ToString("MM-dd HH:mm"));
                box.Items.Add(p.Details.Length > 0 ? "Last word on it: " + p.Details : (p.Kind == "House" ? "No decay or condition line heard here yet; click its sign or stand by it." : ""));
            }
            return;
        }
        box.Items.Add((n.Name.Length > 0 ? n.Name : "unnamed") + "  (" + n.Type + ": " + n.Sub + ", " + n.Serial.ToString("X") + ")");
        string where = UoRegions.Where(n.X, n.Y);
        box.Items.Add((where != null ? where + ", " : "") + "at " + n.X + "," + n.Y + "  " + Sextant(n.X, n.Y) + "  seen " + n.Last.ToString("MM-dd HH:mm"));
        if (n.Items.Count > 0 && grid != null) { box.Items.Add("Sells " + n.Items.Count + " items (buy list of " + n.ItemsAt.ToString("MM-dd HH:mm") + "), below"); box.Items.Add(FillStock(grid, n)); }
        else if (n.Items.Count > 0) { box.Items.Add("Sells (buy list of " + n.ItemsAt.ToString("MM-dd HH:mm") + "):"); foreach (var i in SortedStock(n.Items)) box.Items.Add("   " + i); }
        else if (n.Type == "Shopkeeper") box.Items.Add("No buy list yet: say \"vendor buy\" near it to record one.");
        var trades = f.Atlas.Trades(serial);
        if (trades.Count > 0) {
            int spent = 0, earned = 0; foreach (var t in trades) { if (t.Kind == "buy") spent += t.Amount * t.Price; else earned += t.Amount * t.Price; }
            box.Items.Add("Trades: bought " + spent + " gp, sold " + earned + " gp");
            foreach (var t in trades) box.Items.Add("   " + t.At.ToString("MM-dd HH:mm") + " " + t.Kind + " " + t.Amount + " " + t.Item + " at " + t.Price + " gp");
        }
    }
    // A filter's entries are "All" and the set; the pick is kept in the settings and survives a refill.
    bool choosing;
    void Choices(ComboBox box, SortedSet<string> set, string key) {
        var want = new List<string> { "All" }; want.AddRange(set);
        string pick = f.Cfg.Get(key, "All");
        bool same = box.Items.Count == want.Count; for (int i = 0; same && i < want.Count; i++) same = (string)box.Items[i] == want[i];
        choosing = true;
        try {
            if (!same) { box.BeginUpdate(); box.Items.Clear(); foreach (var w in want) box.Items.Add(w); box.EndUpdate(); }
            int at = want.IndexOf(pick); if (box.SelectedIndex != (at < 0 ? 0 : at)) box.SelectedIndex = at < 0 ? 0 : at;
        } finally { choosing = false; }
    }
    ComboBox Filter(FlowLayoutPanel row, string label, string key) {
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(10, 7, 4, 0) });
        var box = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList }; Hold(box);
        box.SelectedIndexChanged += (s, e) => { if (choosing || box.SelectedItem == null) return; f.Cfg.Set(key, (string)box.SelectedItem); shopShape = ""; FillShops(); };
        row.Controls.Add(box); return box;
    }
    static string City(AtlasNpc n) { return UoRegions.City(n.X, n.Y) ?? "(no city)"; }

    // Best first by gold per cloth among the items a vendor still wants (Damian, 2026-10-16: "i sometimes sell the vendor
    // down and i can't get all the sales i need there, so need more than smithy"): the best item, then the next best for
    // when its vendors are full.
    Label tailorBest; SortList tailorRows; string tailorShape = "";
    void FillTailor() {
        if (f.Tailor == null || tailorRows == null) return;
        int skill = 0; var k = f.Link; if (k != null) lock (k.State.Gate) k.State.Skills.TryGetValue(AssistFeatures.TailoringSkill + 1, out skill);
        var rows = f.Tailor.Rows(skill);
        string shape = skill + "|" + string.Join(",", rows.ConvertAll(r => r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.PriceExceptional + r.Sold + r.Demand));
        if (shape == tailorShape) return; tailorShape = shape;
        tailorRows.BeginUpdate(); tailorRows.Items.Clear();
        foreach (var r in rows) {
            tailorRows.Items.Add(new ListViewItem(new[] { r.Item, r.Cost.ToString(), r.Builds.ToString(), r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "", r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "", r.PriceExceptional > 0 ? r.PriceExceptional.ToString() : "", r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "", r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price",
                r.Demand.Length > 0 ? r.Demand : "no sell list in " + SmithBook.DemandHours + " h", r.Basis }));
        }
        tailorRows.EndUpdate();
        var priced = rows.FindAll(r => r.PerIngot > 0);
        var wanted = priced.FindAll(r => r.Wanted > 0);
        string at = "At tailoring " + (skill / 10.0).ToString("0.0") + ": ";
        if (rows.Count == 0) tailorBest.Text = "No tailoring menus seen yet: open the sewing kit's menus once to learn the cloth costs.";
        else if (priced.Count == 0) tailorBest.Text = "No NPC price known yet: show a tailor your tailored items (\"vendor sell\") to learn what they pay.";
        else {
            string Line(SmithRow r) { return r.Item + ", " + r.PerIngot.ToString("0.00") + " gold per cloth (" + r.Cost + " cloth" + (r.Demand.Length > 0 ? "; wanted: " + r.Demand : "") + ")"; }
            var best = wanted.Count > 0 ? wanted[0] : priced[0];
            var next = (wanted.Count > 0 ? wanted : priced).Find(r => r != best);
            tailorBest.Text = at + "make " + Line(best) + "."
                + (wanted.Count == 0 ? " No vendor's sell list in " + SmithBook.DemandHours + " h shows demand: open one with the goods in your pack." : "")
                + (next != null ? "\nWhen those vendors are full: " + Line(next) + "." : "")
                + (priced[0] != best ? "\n" + priced[0].Item + " pays more per cloth (" + priced[0].PerIngot.ToString("0.00") + ") but no vendor seen wants more." : "");
        }
    }

    Button circuitTab; Label circJob, circTotals; SortList circVendors; string circShape = ""; readonly DateTime windowStart = DateTime.Now;
    ComboBox circPick, surveyTown; Label circSetupNote; DataGridView sellGrid, buyGrid; CheckedListBox shopList; Control[] circTailorOnly, circBuyOnly, circSurveyOnly, circShopsUi = new Control[0];
    readonly Dictionary<string, Button> circLaunch = new Dictionary<string, Button>(); bool fillingShops;
    const string HereTown = "(where I stand)";
    string CircuitPicked { get { return circPick == null ? "tailor" : circPick.SelectedIndex == 1 ? "buy" : circPick.SelectedIndex == 2 ? "survey" : circPick.SelectedIndex == 3 ? "refresh" : circPick.SelectedIndex == 4 ? "mine" : circPick.SelectedIndex == 5 ? "smith" : "tailor"; } }
    FlowLayoutPanel mineRow, smithRow, smithRow2;
    void CircuitButtons() {
        string c = CircuitPicked; Button b;
        if (circLaunch.TryGetValue("Sell only", out b)) b.Visible = c == "tailor"; if (circLaunch.TryGetValue("Tour", out b)) b.Visible = c == "tailor";
        if (mineRow != null) mineRow.Visible = c == "mine";
        if (refreshRow != null) refreshRow.Visible = c == "refresh";
        if (smithRow != null) smithRow.Visible = c == "smith";
        if (smithRow2 != null) smithRow2.Visible = c == "smith";
    }
    FlowLayoutPanel refreshRow;
    // The shops the picker lists for the circuit: the survey's are the town's, every trade; the mining circuit picks none.
    List<UoRegions.Shop> PickerShops(string circuit) { return circuit == "mine" || circuit == "smith" ? new List<UoRegions.Shop>() : circuit == "survey" ? UoRegions.AllShopsIn(f.SurveyTarget()) : AssistFeatures.AllShops(circuit); }
    sealed class ShopEntry { public UoRegions.Shop Shop; public override string ToString() { return Shop.Text + "  (" + Shop.CX + "," + Shop.CY + ")"; } }
    // The setup shown for the picked circuit: its lists, its shops (the picked ones first, in order), its launch modes.
    void FillCircuitSetup() {
        if (circPick == null) return;
        string circuit0 = CircuitPicked; bool buy = circuit0 == "buy", survey = circuit0 == "survey", refresh = circuit0 == "refresh", mine = circuit0 == "mine" || circuit0 == "smith";
        foreach (var c in circTailorOnly) c.Visible = !buy && !survey && !refresh && !mine; foreach (var c in circBuyOnly) c.Visible = buy; foreach (var c in circSurveyOnly) c.Visible = survey;
        foreach (var c in circShopsUi) c.Visible = !mine;
        CircuitButtons();
        // The town list fills once the region file is read (after login).
        if (surveyTown.Items.Count <= 1) { fillingShops = true; try { foreach (var c in UoRegions.Cities()) surveyTown.Items.Add(c); } finally { fillingShops = false; } }
        fillingShops = true; try { int ti = f.SurveyTown.Length == 0 ? 0 : surveyTown.Items.IndexOf(f.SurveyTown); surveyTown.SelectedIndex = ti < 0 ? 0 : ti; } finally { fillingShops = false; }
        sellGrid.Rows.Clear(); foreach (var i in f.SellList()) sellGrid.Rows.Add(i.Name, string.Join(",", Array.ConvertAll(i.Arts, a => a.ToString("X4"))), i.Floor.ToString());
        buyGrid.Rows.Clear(); foreach (var i in f.BuyList()) buyGrid.Rows.Add(i.Name, string.Join(",", Array.ConvertAll(i.Arts, a => a.ToString("X4"))), i.Count.ToString(), i.MaxPrice.ToString());
        fillingShops = true;
        try {
            shopList.Items.Clear(); string circuit = CircuitPicked; var all = PickerShops(circuit); var keys = f.CircuitShopKeys(circuit);
            // The survey's ticks are the town's ticked shops; none of them ticked is every shop of the town.
            if (circuit == "survey" && !all.Exists(s => keys.Contains(s.Key))) keys = new List<string>();
            all.Sort((a, b) => { int c = string.Compare(UoRegions.City(a.CX, a.CY), UoRegions.City(b.CX, b.CY), StringComparison.Ordinal); return c != 0 ? c : string.Compare(a.Text, b.Text, StringComparison.Ordinal); });
            foreach (var key in keys) { var s = all.Find(x => x.Key == key); if (s != null) shopList.Items.Add(new ShopEntry { Shop = s }, true); }
            foreach (var s in all) if (!keys.Contains(s.Key)) shopList.Items.Add(new ShopEntry { Shop = s }, circuit != "buy" && keys.Count == 0);
        } finally { fillingShops = false; }
        circSetupNote.Text = circuit0 == "smith" ? "Smithing: stand by an anvil and a forge with a smith's hammer, then Go. Ladder: " + f.SmithLadderMode + "." : mine ? "Cave mining: set up the forge, one dig and the storage below, then Go in the cave." : refresh ? "Refresh: " + shopList.Items.Count + " food shops; the loadout " + (f.Cfg.Get("loadout.storage", "").Length > 0 ? "from its storage bag" : "from every bag in the bank box, the shortfall bought") + "." : buy ? "Buy reagents: " + f.BuyList().Count + " items on the list, purse " + f.BuyPurse + "." : survey ? "Survey: " + (f.SurveyTarget() ?? "no town") + ", " + shopList.Items.Count + " shops." : "Tailoring: sells " + f.SellList().Count + " items.";
    }
    void SaveShops() {
        if (fillingShops || shopList == null) return;
        var keys = new List<string>(); for (int i = 0; i < shopList.Items.Count; i++) if (shopList.GetItemChecked(i)) keys.Add(((ShopEntry)shopList.Items[i]).Shop.Key);
        // Every tailor shop ticked is the default of every tailor shop, so a shop added to the region file is not left out.
        if (CircuitPicked != "buy" && keys.Count == shopList.Items.Count) keys.Clear();
        f.SetCircuitShopKeys(CircuitPicked, keys);
    }
    static readonly string[] TailorCols = { "Vendor:90", "Town:80", "Thread stock:95", "Bought out:75", "Restock:80", "Restocks seen:110", "Thread bought:95", "Shirts sold:90", "Shirt price:70", "Wants shirts:90" };
    static readonly string[] BuyCols = { "Vendor:90", "Town:80", "Item:110", "Bought:70", "Gold:70", "Last price:70", "Last bought:120" };
    string circCols = "";
    void CircCols(string[] spec) {
        string key = string.Join("|", spec); if (key == circCols) return; circCols = key; circShape = "";
        circVendors.BeginUpdate(); circVendors.Items.Clear(); circVendors.Columns.Clear(); foreach (var c in spec) { var a = c.Split(':'); circVendors.Columns.Add(a[0], int.Parse(a[1])); } circVendors.EndUpdate();
    }
    bool IsBuyItem(string item) { string s = item.ToLowerInvariant(); return f.BuyList().Exists(i => s.Contains(i.Name.ToLowerInvariant()) || s.Contains(UoTiles.Name(i.Arts[0]).ToLowerInvariant())); }
    // The buy circuit's cards: each vendor and item on the shopping list bought from, and the totals by item, this session
    // and on record.
    void FillBuyCards(List<AtlasTrade> trades) {
        CircCols(BuyCols);
        var sums = new Dictionary<string, int[]>(); var last = new Dictionary<string, AtlasTrade>(); var order = new List<string>();
        foreach (var t in trades) {
            if (t.Kind != "buy" || !IsBuyItem(t.Item)) continue;
            string key = t.VendorSerial.ToString("X") + "|" + t.Item.ToLowerInvariant(); int[] s;
            if (!sums.TryGetValue(key, out s)) { sums[key] = s = new int[2]; order.Add(key); }
            s[0] += t.Amount; s[1] += t.Amount * t.Price; last[key] = t;
        }
        var rows = new List<string[]>();
        foreach (var key in order) { var t = last[key]; var s = sums[key]; rows.Add(new[] { t.Vendor, UoRegions.City(t.X, t.Y) ?? "", t.Item, s[0].ToString(), s[1].ToString(), t.Price.ToString(), Ago(t.At) }); }
        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != circShape) { circShape = shape; circVendors.BeginUpdate(); circVendors.Items.Clear(); foreach (var r in rows) circVendors.Items.Add(new ListViewItem(r)); circVendors.EndUpdate(); }
        string Sum(Predicate<AtlasTrade> when) {
            var by = new Dictionary<string, int[]>(); var names = new List<string>();
            foreach (var t in trades) { if (t.Kind != "buy" || !IsBuyItem(t.Item) || !when(t)) continue; string n = t.Item.ToLowerInvariant(); int[] s; if (!by.TryGetValue(n, out s)) { by[n] = s = new int[2]; names.Add(n); } s[0] += t.Amount; s[1] += t.Amount * t.Price; }
            if (names.Count == 0) return "nothing on the shopping list bought";
            return string.Join("; ", names.ConvertAll(n => n + " " + by[n][0] + " for " + by[n][1] + " gold"));
        }
        var left = f.BuyList().ConvertAll(i => i.Count + " " + i.Name);
        var hours = (DateTime.Now - windowStart).TotalHours;
        circTotals.Text = "This session (" + (hours < 1 ? (int)(hours * 60) + " min" : hours.ToString("0.0") + " h") + "): " + Sum(t => t.At >= windowStart) + "\nOn record: " + Sum(t => true) + "\nOn the shopping list: " + (left.Count > 0 ? string.Join(", ", left) : "nothing");
    }
    static readonly string[] SurveyCols = { "Vendor:90", "Shop:170", "Items listed:85", "Stock:260", "Last seen:120" };
    // The survey's cards: every keeper the atlas knows in the town, its shop and its stock list, and how many are read.
    void FillSurveyCards() {
        CircCols(SurveyCols);
        string town = f.SurveyTarget(); var rows = new List<string[]>(); int listed = 0;
        if (f.Atlas != null && town != null) foreach (var n in f.Atlas.Shops()) {
            if (UoRegions.City(n.X, n.Y) != town) continue; if (n.Items.Count > 0) listed++;
            rows.Add(new[] { n.Name, UoRegions.Where(n.X, n.Y) ?? "", n.Items.Count.ToString(), string.Join(", ", n.Items.GetRange(0, Math.Min(6, n.Items.Count))) + (n.Items.Count > 6 ? ", ..." : ""), Ago(n.Last) });
        }
        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != circShape) { circShape = shape; circVendors.BeginUpdate(); circVendors.Items.Clear(); foreach (var r in rows) circVendors.Items.Add(new ListViewItem(r)); circVendors.EndUpdate(); }
        circTotals.Text = (town ?? "No town") + ": " + rows.Count + " shopkeepers known, " + listed + " with a stock list read." + (f.TourReport.Length > 0 ? "\nLast report: " + f.TourReport.Replace("\r\n", " | ") : "");
    }
    static string Cell(DataGridViewRow r, int i) { var v = r.Cells[i].Value; return v == null ? "" : v.ToString().Trim(); }
    void SaveSellGrid() {
        var l = new List<string>();
        foreach (DataGridViewRow r in sellGrid.Rows) { if (r.IsNewRow) continue; int floor; if (Cell(r, 0).Length > 0 && Cell(r, 1).Length > 0 && int.TryParse(Cell(r, 2), out floor)) l.Add(Cell(r, 0) + "|" + Cell(r, 1) + "|" + floor); }
        f.Cfg.Set("circuit.sell", string.Join(";", l));
    }
    void SaveBuyGrid() {
        var l = new List<string>();
        foreach (DataGridViewRow r in buyGrid.Rows) { if (r.IsNewRow) continue; int n, gp; if (Cell(r, 0).Length > 0 && Cell(r, 1).Length > 0 && int.TryParse(Cell(r, 2), out n) && int.TryParse(Cell(r, 3), out gp) && n > 0) l.Add(Cell(r, 0) + "|" + Cell(r, 1) + "|" + n + "|" + gp); }
        f.Cfg.Set("circuit.buy.list", string.Join(";", l));
    }
    static bool IsThread(string item) { string s = item.ToLowerInvariant(); return s.Contains("thread") || s.Contains("shuttle") || s.Contains("spool"); }
    bool IsShirt(string item) { string s = item.ToLowerInvariant(); return f.SellList().Exists(i => s.Contains(i.Name.ToLowerInvariant())); }
    static string Ago(DateTime t) { var m = (int)(DateTime.Now - t).TotalMinutes; return t.ToString("HH:mm") + (m < 120 ? " (" + m + " min ago)" : ""); }
    Label circAll;
    // "Circuit ended (why): bought 0 thread, sewed 5 shirts, ..." without the parts whose count is 0, and without the colon
    // when every part is.
    static string NoZeros(string s) {
        int at = s.LastIndexOf("): "); if (at < 0) return s;
        var keep = new List<string>();
        foreach (var part in s.Substring(at + 3).TrimEnd('.').Split(new[] { ", " }, StringSplitOptions.None)) {
            bool zero = false; foreach (var w in part.Split(' ')) { int n; if (int.TryParse(w, out n)) { zero = n == 0; break; } }
            if (!zero) keep.Add(part);
        }
        return s.Substring(0, at + 1) + (keep.Count > 0 ? ": " + string.Join(", ", keep) + "." : "");
    }
    void FillCircuit() {
        if (circVendors == null) return;
        // Cave mining deals with no vendors (Damian, 2026-10-18: "when cave mining is picked, there is no need to show circuit
        // vendors for tailoring").
        var vbox = circVendors.Parent != null ? circVendors.Parent.Parent : null;
        // Refresh deals with food shops only (Damian: "same with like refresh: no need to show the vendors").
        if (vbox != null && page == "circuit") { bool show = CircuitPicked != "mine" && CircuitPicked != "refresh" && CircuitPicked != "smith"; if (vbox.Visible != show) { vbox.Visible = show; Fit(); } }
        // Every circuit's last end, under the totals.
        var ends = new List<string>(); lock (f.CircuitEnds) foreach (var kv in f.CircuitEnds) ends.Add(kv.Key + ": " + kv.Value);
        string all = ends.Count == 0 ? "All circuits: none ended this session." : "All circuits, last end:\n" + string.Join("\n", ends);
        if (circAll.Text != all) circAll.Text = all;
        if (CircuitPicked == "mine") return;
        var vendors = f.CircuitVendors(); var trades = f.Atlas != null ? f.Atlas.Trades(0) : new List<AtlasTrade>();
        var notes = new List<string>(); lock (f.Notes) foreach (var n in f.Notes) if (n.Contains("Circuit:") || n.Contains("Buy:") || n.Contains("Smithing:") || n.Contains("Tools:") || n.Contains("Toolbag:") || n.Contains("Refresh:") || n.Contains("Mine:")) notes.Add(n);
        // No inventory line and no zero counts (Damian, 2026-10-18: "in circuit job no need to show summary for inventory or
        // zeros").
        circJob.Text = NoZeros(f.CircuitStatus) + (notes.Count > 0 ? "\n" + string.Join("\n", notes.GetRange(Math.Max(0, notes.Count - 6), Math.Min(6, notes.Count))) : "");
        // The vendors and totals of the circuit picked (Damian, 2026-10-17: "the circuit totals card on the circuit tab is
        // tailor specific").
        bool tailor = CircuitPicked != "buy" && CircuitPicked != "survey";
        if (goldRow.Visible != tailor) { goldRow.Visible = tailor; Fit(); }
        if (CircuitPicked == "buy") { FillBuyCards(trades); return; }
        if (CircuitPicked == "survey") { FillSurveyCards(); return; }
        if (CircuitPicked == "smith") { string st = f.SmithStatus.Length > 0 ? "Last smithing: " + f.SmithStatus : "No smithing run yet."; if (circTotals.Text != st) circTotals.Text = st; return; }
        if (CircuitPicked == "refresh") { string rt = f.RefreshStatus.Length > 0 ? "Last refresh: " + f.RefreshStatus : "No refresh run yet."; if (circTotals.Text != rt) circTotals.Text = rt; return; }
        CircCols(TailorCols);
        {
            int spent = 0, made = 0; foreach (var t in trades) if (t.At >= windowStart) { if (t.Kind == "buy" && IsThread(t.Item)) spent += t.Amount * t.Price; else if (t.Kind == "sell" && IsShirt(t.Item)) made += t.Amount * t.Price; }
            if (goldPic.Image == null && UoArt.Folder != null) goldPic.Image = UoArt.Icon(GoldPileArt);
            string g = (made - spent) + " gold net this session (" + made + " made, " + spent + " on thread)"; if (goldLine.Text != g) goldLine.Text = g;
        }
        // Every vendor the circuit bought thread from, and every vendor that bought shirts.
        var serials = new List<uint>(); foreach (var v in vendors) serials.Add(v.Serial);
        foreach (var t in trades) if ((t.Kind == "buy" && IsThread(t.Item) || t.Kind == "sell" && IsShirt(t.Item)) && !serials.Contains(t.VendorSerial)) serials.Add(t.VendorSerial);
        var rows = new List<string[]>();
        foreach (uint s in serials) {
            var v = vendors.Find(x => x.Serial == s); var mine = trades.FindAll(t => t.VendorSerial == s);
            string name = v != null && v.Name.Length > 0 ? v.Name : mine.Count > 0 ? mine[mine.Count - 1].Vendor : s.ToString("X");
            int x = v != null ? v.X : mine.Count > 0 ? mine[mine.Count - 1].X : 0, y = v != null ? v.Y : mine.Count > 0 ? mine[mine.Count - 1].Y : 0;
            int tb = 0, tg = 0, ss = 0, sg = 0, lastPrice = 0;
            foreach (var t in mine) { if (t.Kind == "buy" && IsThread(t.Item)) { tb += t.Amount; tg += t.Amount * t.Price; } else if (t.Kind == "sell" && IsShirt(t.Item)) { ss += t.Amount; sg += t.Amount * t.Price; lastPrice = t.Price; } }
            string restock = "", restocks = "", stockText = "", boughtOut = "";
            if (v != null) {
                if (v.Stock >= 0) stockText = v.Stock + " at " + v.StockAt.ToString("HH:mm");
                if (v.BoughtOutAt > DateTime.MinValue.AddDays(1)) {
                    boughtOut = v.BoughtOutAt.ToString("HH:mm");
                    var due = v.BoughtOutAt.AddMinutes(AssistFeatures.RestockMinutes); int left = (int)Math.Ceiling((due - DateTime.Now).TotalMinutes);
                    restock = v.Stock > 0 && v.StockAt > v.BoughtOutAt ? "has stock" : left > 0 ? "in " + left + " min" : "due";
                }
                var r = new List<string>(); foreach (var a in v.Restocks) r.Add(a[0].ToString()); restocks = string.Join(", ", r);
            }
            var sl = f.SellList(); string wants = f.Tailor != null && sl.Count > 0 ? f.Tailor.WantAt(name, sl[0].Name) : "";
            rows.Add(new[] { name, (UoRegions.City(x, y) ?? "") , stockText, boughtOut, restock, restocks, tb > 0 ? tb + " for " + tg : "", ss > 0 ? ss + " for " + sg : "", lastPrice > 0 ? lastPrice.ToString() : "", wants });
        }
        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != circShape) {
            circShape = shape;
            circVendors.BeginUpdate(); circVendors.Items.Clear(); foreach (var r in rows) circVendors.Items.Add(new ListViewItem(r)); circVendors.EndUpdate();
        }
        // Totals: this session (since the window opened) and every trade on record.
        string Sum(Predicate<AtlasTrade> when) {
            int tb = 0, tg = 0, ss = 0, sg = 0;
            foreach (var t in trades) if (when(t)) { if (t.Kind == "buy" && IsThread(t.Item)) { tb += t.Amount; tg += t.Amount * t.Price; } else if (t.Kind == "sell" && IsShirt(t.Item)) { ss += t.Amount; sg += t.Amount * t.Price; } }
            return "thread bought " + tb + " for " + tg + " gold; sold " + ss + " for " + sg + " gold; net " + (sg - tg) + " gold";
        }
        var hours = (DateTime.Now - windowStart).TotalHours;
        circTotals.Text = "This session (" + (hours < 1 ? (int)(hours * 60) + " min" : hours.ToString("0.0") + " h") + "): " + Sum(t => t.At >= windowStart) + "\nOn record: " + Sum(t => true);
    }

    ComboBox craftPick; Label craftBest; SortList craftRows; string craftShape = "";
    void FillCraft() {
        if (craftRows == null || craftPick.SelectedIndex < 0) return;
        var c = f.Crafts[craftPick.SelectedIndex]; if (c.Book == null) { craftBest.Text = "Log in to read the " + c.Name + " book."; return; }
        int skill = 0; var k = f.Link; if (k != null) lock (k.State.Gate) k.State.Skills.TryGetValue(c.Skill + 1, out skill);
        var rows = c.Book.Rows(skill);
        string shape = c.Name + skill + "|" + string.Join(",", rows.ConvertAll(r => r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.Sold));
        if (shape == craftShape) return; craftShape = shape;
        craftRows.BeginUpdate(); craftRows.Items.Clear();
        foreach (var r in rows)
            craftRows.Items.Add(new ListViewItem(new[] { r.Item, r.Cost + " " + c.Unit + (r.Cost == 1 ? "" : "s"), r.Builds.ToString(), r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "", r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "", r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "", r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price", r.Basis }));
        craftRows.EndUpdate();
        var best = rows.Find(r => r.PerIngot > 0);
        craftBest.Text = rows.Count == 0 ? "No " + c.Name.ToLowerInvariant() + " menu with a cost seen yet: open the tool's menus once (" + string.Join(", ", c.Tools) + ")."
            : best == null ? "No NPC price known yet: show a vendor your " + c.Name.ToLowerInvariant() + " goods (\"<name> sell\") to learn what it pays."
            : "Best at " + c.Name.ToLowerInvariant() + " " + (skill / 10.0).ToString("0.0") + ": " + best.Item + ", " + best.PerIngot.ToString("0.00") + " gold per " + c.Unit + " (" + best.Basis + ").";
    }
    Label smithBest; SortList smithRows; string smithShape = "";
    void FillSmith() {
        if (f.Smith == null || smithRows == null) return;
        int skill = 0; var k = f.Link; if (k != null) lock (k.State.Gate) k.State.Skills.TryGetValue(AssistFeatures.SmithySkill + 1, out skill);
        var rows = f.Smith.Rows(skill);
        string shape = skill + "|" + string.Join(",", rows.ConvertAll(r => r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.PriceExceptional + r.Sold));
        if (shape == smithShape) return; smithShape = shape;
        smithRows.BeginUpdate(); smithRows.Items.Clear();
        foreach (var r in rows) {
            int ok = r.Builds - r.Failed;
            var it = new ListViewItem(new[] { r.Item, r.Cost.ToString(), r.Builds.ToString(), r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "", r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "", r.PriceExceptional > 0 ? r.PriceExceptional.ToString() : "", r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "", r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price", r.Basis });
            smithRows.Items.Add(it);
        }
        smithRows.EndUpdate();
        var best = rows.Find(r => r.PerIngot > 0);
        smithBest.Text = rows.Count == 0 ? "No smithing menus seen yet: open the hammer's menus once to learn the ingot costs."
            : best == null ? "No NPC price known yet: show a blacksmith your smithed items (\"vendor sell\") to learn what they pay."
            : "Best at smithing " + (skill / 10.0).ToString("0.0") + ": " + best.Item + ", " + best.PerIngot.ToString("0.00") + " gold per ingot (" + best.Cost + " ingots, " + best.Basis + ").";
        smithBest.Text += "\nFor skill gain (" + (f.SmithLadderMode == "guess" ? "the server formula's best guess" : "your ladder") + "): " + (f.SmithNow(skill) ?? "none") + ".";
    }

    // ---- the bag pricer. A unit price is the city price (the median of every recorded vendor's buy-list price for the
    // item) plus the bulk markup, or the GM-made markup for wearables (TILEDATA flag 0x400000: armour, weapons, clothing)
    // when GM-made is ticked; a price the player types is kept per item in the settings and wins. Empty clears it.
    public const int BulkPct = 25, CraftPct = 50, PriceCol = 4;
    DataGridView pricer; Label pricerStatus, pricerTotal; NumericUpDown bulkPct, craftPct; CheckBox gmMade; long pricerSeen; bool pricerFilling;
    NumericUpDown Percent(FlowLayoutPanel row, string label, string key, int dflt) {
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 7, 4, 0) });
        var box = new NumericUpDown { Minimum = 0, Maximum = 1000, Width = 60, Value = Math.Max(0, Math.Min(1000, f.Cfg.GetInt(key, dflt))) };
        box.ValueChanged += (s, e) => { f.Cfg.Set(key, ((int)box.Value).ToString()); FillPricer(); };
        row.Controls.Add(box); return box;
    }
    static string PriceKey(string name) {
        string s = string.Join(" ", name.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("a ")) s = s.Substring(2); else if (s.StartsWith("an ")) s = s.Substring(3);
        if (s.EndsWith("s") && s.Length > 3) s = s.Substring(0, s.Length - 1);
        return s;
    }
    sealed class PriceRow { public string Name, Key; public int Qty, City, Vendors, Unit; public bool Wearable, Mine; public string Markup; }
    List<PriceRow> PriceRows() {
        var rows = new Dictionary<string, PriceRow>(); var tiles = UoTiles.Items();
        foreach (var it in f.PricerItems()) {
            string name = UoTiles.Name(it.Graphic), key = PriceKey(name); PriceRow r;
            if (!rows.TryGetValue(key, out r)) rows[key] = r = new PriceRow { Name = name, Key = key, Wearable = it.Graphic < tiles.Count && (tiles[it.Graphic].Flags & 0x00400000) != 0 };
            r.Qty += Math.Max(1, it.Amount);
        }
        var market = new Dictionary<string, List<int>>();
        if (f.Atlas != null) foreach (var v in f.Atlas.Shops()) foreach (var i in v.Items) { string key = PriceKey(StockName(i)); List<int> l; if (!market.TryGetValue(key, out l)) market[key] = l = new List<int>(); l.Add(StockPrice(i)); }
        int bulk = (int)bulkPct.Value, craft = (int)craftPct.Value; bool gm = gmMade.Checked;
        foreach (var r in rows.Values) {
            List<int> l; if (market.TryGetValue(r.Key, out l)) { r.Vendors = l.Count; r.City = Median(new List<int>(l)); }
            int m = r.Wearable && gm ? craft : bulk; r.Markup = (r.Wearable && gm ? "GM-made " : "bulk ") + m + "%";
            r.Unit = r.City > 0 ? (int)Math.Round(r.City * (100 + m) / 100.0) : 0;
            int mine; if (int.TryParse(f.Cfg.Get("pricer.price." + r.Key, ""), out mine)) { r.Unit = mine; r.Mine = true; r.Markup = "your price"; }
        }
        var list = new List<PriceRow>(rows.Values); list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
    void FillPricer() {
        if (pricer == null || pricerFilling) return;
        pricerFilling = true;
        try {
            var rows = PriceRows(); pricer.Rows.Clear(); int total = 0, count = 0, unpriced = 0;
            foreach (var r in rows) {
                int i = pricer.Rows.Add(r.Name, r.Qty, r.City > 0 ? r.City + " (" + r.Vendors + " vendor" + (r.Vendors == 1 ? "" : "s") + ")" : "none recorded", r.Markup, r.Unit > 0 ? r.Unit.ToString() : "", r.Unit * r.Qty);
                pricer.Rows[i].Tag = r.Key; if (r.Unit == 0) { unpriced++; pricer.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(250, 215, 215); }
                total += r.Unit * r.Qty; count += r.Qty;
            }
            pricerTotal.Text = rows.Count == 0 ? "" : "Price the bag at " + total + " gp: " + count + " items in " + rows.Count + " kinds" + (unpriced > 0 ? "; " + unpriced + " kinds have no city price, type one in" : "") + ".";
        } finally { pricerFilling = false; }
    }
    void PriceEdited(int row) {
        if (pricerFilling || row < 0 || row >= pricer.Rows.Count) return;
        string key = (string)pricer.Rows[row].Tag, text = Convert.ToString(pricer.Rows[row].Cells[PriceCol].Value ?? "").Trim(); int v;
        f.Cfg.Set("pricer.price." + key, int.TryParse(text, out v) && v > 0 ? v.ToString() : "");
        BeginInvoke(new Action(FillPricer));
    }
    long stockSeen; bool selecting;
    ListViewItem ShopRow(uint serial) { foreach (ListViewItem it in shops.Items) if ((uint)it.Tag == serial) return it; return null; }
    // Selects a vendor's row, clearing the filters and the search when they hide it, and shows its details.
    void PickShop(uint serial) {
        if (serial == 0 || shops == null) return;
        shopShape = ""; FillShops(); var row = ShopRow(serial);
        if (row == null) { f.Cfg.Set("shops.city", "All"); f.Cfg.Set("shops.kind", "All"); shopFind.Text = ""; shopShape = ""; FillShops(); row = ShopRow(serial); }
        if (row == null) return;
        shops.SelectedItems.Clear(); row.Selected = true; row.EnsureVisible(); ShowNpc(shopInfo, shopItems, serial);
    }
    void FillShops() {
        if (f.Atlas == null || shops == null || !shops.Visible && page != "shops") return;
        string find = shopFind.Text.Trim(); int px = mapView.CenterX, py = mapView.CenterY;
        var vendors = f.Atlas.Shops(); var list = vendors;
        // City and type filters (Damian, 2026-10-15: "add a combobox to filter by city or all, then filter by npc type when
        // known, or by shop type"); each offers what the recorded vendors hold.
        var cities = new SortedSet<string>(); var kinds = new SortedSet<string>();
        foreach (var n in vendors) { cities.Add(City(n)); kinds.Add(Kind(n)); }
        Choices(shopCity, cities, "shops.city"); Choices(shopKind, kinds, "shops.kind");
        string city = shopCity.SelectedIndex > 0 ? (string)shopCity.SelectedItem : null, kindPick = shopKind.SelectedIndex > 0 ? (string)shopKind.SelectedItem : null;
        if (city != null) list = list.FindAll(n => City(n) == city);
        if (kindPick != null) list = list.FindAll(n => Kind(n) == kindPick);
        if (find.Length > 0) list = list.FindAll(n => n.Items.Exists(i => i.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0) || n.Sub.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0);
        string shape = string.Join(",", list.ConvertAll(n => n.Serial + ":" + n.X + ":" + n.Y + ":" + n.Items.Count + ":" + n.Name)) + "|" + px / 8 + "," + py / 8 + "|" + vendors.Count;
        if (shape == shopShape) return; shopShape = shape;
        uint kept = shops.SelectedItems.Count > 0 ? (uint)shops.SelectedItems[0].Tag : 0;
        shops.BeginUpdate(); shops.Items.Clear();
        var market = Market(vendors);
        foreach (var n in list) {
            int d = Math.Max(Math.Abs(n.X - px), Math.Abs(n.Y - py));
            string where = UoRegions.Where(n.X, n.Y), kind = Kind(n); int sellers = KindSellers(n, kind, vendors), cheap = 0, dear = 0, large = 0, unique = 0;
            foreach (var i in n.Items) { string note; int off = OffNorm(n, kind, i, market, sellers, out note); if ((off & OffCheap) != 0) cheap++; if ((off & OffDear) != 0) dear++; if ((off & OffLarge) != 0) large++; if ((off & OffUnique) != 0) unique++; }
            var it = new ListViewItem(new[] { n.Name.Length > 0 ? n.Name : "unnamed", kind, (where != null ? where + " " : "") + n.X + "," + n.Y, d.ToString(), n.Items.Count.ToString(), n.Last.ToString("MM-dd HH:mm"), OffSummary(cheap, dear, large, unique) }) { Tag = n.Serial };
            shops.Items.Add(it);
        }
        // The refill keeps the selected vendor selected (walking 8 tiles refills the list).
        if (kept != 0) { var row = ShopRow(kept); if (row != null) { selecting = true; try { row.Selected = true; } finally { selecting = false; } } }
        shops.EndUpdate();
    }
    void MapZoom(int d) { int at = Array.IndexOf(MapView.ZoomSteps, mapView.Zoom); if (at < 0) at = 1; mapView.Zoom = MapView.ZoomSteps[Math.Max(0, Math.Min(MapView.ZoomSteps.Length - 1, at + d))]; f.Cfg.Set("map.zoom", mapView.Zoom.ToString()); mapView.Invalidate(); }
    void MapTick(AssistState st) {
        int x, y; lock (st.Gate) { x = st.X; y = st.Y; }
        if ((x | y) == 0) return;
        lock (trail) { if (trail.Count == 0 || trail[trail.Count - 1].X != x || trail[trail.Count - 1].Y != y) { trail.Add(new Point(x, y)); if (trail.Count > TrailKeep) trail.RemoveAt(0); } }
        if (mapView.CenterX != x || mapView.CenterY != y) { mapView.CenterX = x; mapView.CenterY = y; if (page == "map") mapView.Invalidate(); }
        else if (page == "map") mapView.Invalidate();
    }
    // An open dropdown closes on a relayout or a z-order change, so neither runs while one is open (Damian, 2026-10-15:
    // "something is causing refresh of the UI so i can't make dropdown selections").
    int dropped;
    void Hold(ComboBox box) { box.DropDown += (s, e) => dropped++; box.DropDownClosed += (s, e) => dropped = Math.Max(0, dropped - 1); }
    void Tick() {
        if (dropped > 0) return;
        Border(); ShowDelay(); ShowTimings(); ShowSounds();
        if (tripsSeen != f.TripsWritten) { if (page == "map.trip") FillTrips(); else if (page == "map.areas") FillAreas(); }
        if (client != IntPtr.Zero && Visible) Stack(AssistNative.GetForegroundWindow());
        // A client that closed takes the dock with it; the next client to connect is docked anew.
        if (client != IntPtr.Zero && !AssistNative.IsWindow(client)) { AssistNative.UnhookWinEvent(locHook); AssistNative.UnhookWinEvent(fgHook); HotKeys(false); client = IntPtr.Zero; relay.LastClientPort = 0; Hide(); if (bars != null) bars.Hide(); }
        if (client == IntPtr.Zero) DockToClient();
        var k = relay.Link();
        if (k == null) { status.Text = client == IntPtr.Zero ? "Waiting for the client to connect through the relay." : "Logged out."; if (client != IntPtr.Zero) Title(null); return; }
        var st = k.State;
        lock (st.Gate) {
            foreach (var kv in statValues) { Color c; string v = kv.Key + "  " + StatText(st, kv.Key, out c); if (kv.Value.Text != v) kv.Value.Text = v; if (kv.Value.ForeColor != c) kv.Value.ForeColor = c; }
            statLine = string.Join("   ", Array.ConvertAll(new[] { "hits", "mana", "stamina", "gold", "weight" }, n => n + " " + StatValue(st, n)));
            if (bars != null) bars.Set(st.Hits, st.MaxHits, st.Mana, st.MaxMana, st.Stam, st.MaxStam);
        }
        status.Text = (st.Title.Length > 0 ? st.Title : st.Name) + (k.Halted != null ? "  HALTED: " + k.Halted : "");
        Title(st);
        Session(k);
        Summary();
        if (UoArt.Folder == null && clientPid != 0) UoArt.Folder = AssistNative.ExeFolder(clientPid);
        MapTick(st);
        if (page == "shops") FillShops();
        if (page == "maker") { FillSmith(); FillTailor(); FillCraft(); }
        if (page == "circuit") FillCircuit();
        // A text card whose text needs another height lays the window out again (Damian, 2026-10-18: "the circuit job card
        // underflows": its text changed between layouts and kept the old height).
        foreach (var c in growers) if (c.Visible && c is Label && c.Height != Grown((Label)c)) { Fit(); break; }
        if (pricerStatus != null && pricerStatus.Text != f.PricerStatus) pricerStatus.Text = f.PricerStatus;
        if (loadoutStatus != null && loadoutStatus.Text != f.LoadoutStatus) loadoutStatus.Text = f.LoadoutStatus;
        { long seq = System.Threading.Interlocked.Read(ref f.PricerSeq); if (seq != pricerSeen && pricer != null && !pricer.IsCurrentCellInEditMode) { pricerSeen = seq; FillPricer(); } }
        // A new buy list selects its vendor while the Shops tab is open (Damian, 2026-10-15); the game keeps the keyboard.
        if (f.Atlas != null) { long seq = System.Threading.Interlocked.Read(ref f.Atlas.StockedSeq); if (seq != stockSeen) { stockSeen = seq; if (page == "shops") PickShop(f.Atlas.Stocked); } }
        Chat(st);
        loopState.Text = f.Status; loopWords.Text = f.LoopWords;
        SetLight(loopLight, loopGroup, LoopRunning(), Attention(false)); SetLight(circLight, circJobGroup, f.CircuitRunning, Attention(true));
        var one = HandText(k, 1); var two = HandText(k, 2); hands.Text = "hands: " + one + ", " + two;
        workState.Text = f.WorkState;
        string tripText = f.TripOn ? "End trip (" + f.TripState().Substring(f.TripState().IndexOf(':') + 2) + ")" : "Begin trip"; if (tripButton.Text != tripText) tripButton.Text = tripText;
        string petText = f.PetLines(); if (petsCard.Text != petText) { petsCard.Text = petText; if (petsCard.Height != Grown(petsCard)) Fit(); }
        var events = f.PetHistory();
        if (events.Length != petHistory.Items.Count || (events.Length > 0 && (string)petHistory.Items[0] != events[0])) { petHistory.BeginUpdate(); petHistory.Items.Clear(); petHistory.Items.AddRange(events); petHistory.EndUpdate(); }
        if (f.ShowMapNow) { f.ShowMapNow = false; ShowPage("map"); }
        drink.Text = f.Sequence.StartsWith("Drink") || f.Sequence.StartsWith("Drank") ? f.Sequence : "";
        reach.Text = "reach " + f.Reach + " tiles; crops: " + string.Join(", ", Array.ConvertAll(f.Crops(), c => UoTiles.Name(c)));
        Resources();
        Skills(st);
        Heads();
        lock (f.Notes) { while (notes.Items.Count < f.Notes.Count) notes.Items.Add(f.Notes[notes.Items.Count]); }
        Guard(k);
        int bank = (st.X >> 3) * 1024 + (st.Y >> 3);
        if (bank != lastBank) { lastBank = bank; lastBankZ = st.Z; ThreadPool.QueueUserWorkItem(_ => f.UpdateBanks(UoMap.LandZ)); }
        else if (Math.Abs(st.Z - lastBankZ) > 10) { lastBankZ = st.Z; ThreadPool.QueueUserWorkItem(_ => f.UpdateBanks(UoMap.LandZ, true)); }
    }
    int lastBank = -1, lastBankZ;
    static string HandText(GameLink k, int layer) {
        lock (k.State.Gate) foreach (var kv in k.State.Items) if (kv.Value.Layer == layer && kv.Value.Mobile == k.State.Player) return kv.Value.Graphic.ToString("X4");
        return "empty";
    }
    // The 1998 server's GetSkillValue blend (OUO skill.c 0x004D5FBB, the UOAIX copy in Skill1998.codex s98-value):
    // shown = base * (1000 - stat) / 1000 + stat, stat = (str * ws + dex * wd + int * wi) * (100 - skillStat) / 1000, in
    // tenths, weights per skill from Skill1998Data.codex. OUO sends a 1.25 client the blended value only; the UOAIX shard
    // sends the base, so there shown and base are the same number.
    static readonly int[] WStr = { 0, 15, 0, 0, 75, 75, 0, 100, 50, 0, 40, 80, 0, 0, 0, 0, 0, 30, 50, 0, 65, 0, 0, 0, 0, 0, 25, 75, 0, 0, 0, 55, 0, 0, 15, 70, 20, 50, 0, 40, 75, 100, 50, 100, 100, 100 };
    static readonly int[] WDex = { 50, 15, 0, 0, 15, 25, 0, 0, 50, 50, 30, 20, 50, 40, 40, 50, 0, 30, 50, 20, 25, 80, 90, 20, 100, 0, 25, 25, 100, 80, 20, 45, 0, 100, 65, 10, 0, 20, 50, 20, 25, 0, 50, 0, 0, 0 };
    static readonly int[] WInt = { 50, 70, 100, 100, 10, 0, 0, 0, 0, 50, 30, 0, 50, 60, 60, 50, 100, 40, 0, 80, 10, 20, 10, 80, 0, 100, 50, 0, 0, 20, 80, 0, 100, 0, 20, 20, 80, 30, 50, 40, 0, 0, 0, 0, 0, 0 };
    static readonly int[] WSkill = { 90, 100, 100, 100, 100, 90, 100, 90, 80, 100, 50, 75, 85, 50, 100, 95, 100, 80, 100, 100, 75, 100, 95, 90, 75, 85, 100, 90, 75, 100, 80, 90, 100, 90, 75, 80, 100, 90, 75, 80, 90, 90, 90, 90, 80, 80 };
    bool SentBlended { get { return !f.Server.StartsWith("127.0.0.1:2593"); } }
    static int BaseOf(int id, int shown, int str, int dex, int wit) {
        if (id < 0 || id >= WStr.Length) return shown;
        int stat = (str * WStr[id] + dex * WDex[id] + wit * WInt[id]) * (100 - WSkill[id]) / 1000;
        if (stat >= 1000) return shown;
        return Math.Max(0, (int)Math.Round((shown - stat) * 1000.0 / (1000 - stat)));
    }
    void Skills(AssistState st) {
        var rows = new List<int[]>(); var names = new Dictionary<int, string>();
        lock (st.Gate) foreach (var kv in st.Skills) {
            int id = kv.Key - 1; int start; st.SkillStart.TryGetValue(kv.Key, out start);
            int baseValue = SentBlended ? BaseOf(id, kv.Value, st.Str, st.Dex, st.Int) : kv.Value;
            rows.Add(new[] { kv.Key, kv.Value, baseValue, kv.Value - start });
            names[kv.Key] = id >= 0 && id < SkillNames.Length ? SkillNames[id] : "skill " + kv.Key;
        }
        int col = skills.SortColumn; int dir = skills.Descending ? -1 : 1;
        rows.Sort((a, b) => {
            int d = col == 0 ? string.CompareOrdinal(names[a[0]], names[b[0]]) : a[col].CompareTo(b[col]);
            return d != 0 ? d * dir : string.CompareOrdinal(names[a[0]], names[b[0]]);
        });
        var all = new List<string[]>();
        foreach (var r in rows) all.Add(new[] { names[r[0]], (r[1] / 10.0).ToString("0.0"), (r[2] / 10.0).ToString("0.0"), (r[3] / 10.0).ToString("+0.0;-0.0;0") });
        // An EndUpdate repaints the whole grid, so a tick with no changed cell leaves it alone (Damian, 2026-10-16: "the
        // skills grid visibly refreshes about every half second and its distracting").
        bool same = skills.Items.Count == all.Count;
        for (int i = 0; same && i < all.Count; i++) for (int c = 0; c < 4; c++) if (skills.Items[i].SubItems[c].Text != all[i][c]) { same = false; break; }
        if (same) return;
        skills.BeginUpdate();
        while (skills.Items.Count > all.Count) skills.Items.RemoveAt(skills.Items.Count - 1);
        for (int i = 0; i < all.Count; i++) {
            if (i >= skills.Items.Count) skills.Items.Add(new ListViewItem(all[i]));
            else for (int c = 0; c < 4; c++) if (skills.Items[i].SubItems[c].Text != all[i][c]) skills.Items[i].SubItems[c].Text = all[i][c];
        }
        skills.EndUpdate();
    }
    // Player speech lines since the last tick, less the speakers hidden by name (a human NPC passes the speech test).
    void Chat(AssistState st) {
        var hidden = new HashSet<string>(f.Cfg.Get("chat.hidden", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        List<string[]> lines;
        // A reconnect starts a new state whose count restarts below the one seen (2026-10-15 21:45: 145 window faults).
        lock (st.Gate) { if (chatSeen > st.ChatSeq) chatSeen = 0; long first = st.ChatSeq - st.Chat.Count; int from = (int)Math.Min(st.Chat.Count, Math.Max(0, chatSeen - first)); lines = st.Chat.GetRange(from, st.Chat.Count - from); chatSeen = st.ChatSeq; }
        if (lines.Count == 0) return;
        foreach (var l in lines) if (!hidden.Contains(l[1])) chat.Items.Add(l[0] + " " + l[1] + ": " + l[2]);
        while (chat.Items.Count > 500) chat.Items.RemoveAt(0);
        chat.TopIndex = Math.Max(0, chat.Items.Count - 1);
    }
    void HideSpeaker() {
        if (chat.SelectedItem == null) return;
        string line = (string)chat.SelectedItem; int sp = line.IndexOf(' '), colon = line.IndexOf(": ");
        if (sp < 0 || colon <= sp) return;
        string name = line.Substring(sp + 1, colon - sp - 1);
        f.Cfg.Set("chat.hidden", f.Cfg.Get("chat.hidden", "") + "|" + name);
        for (int i = chat.Items.Count - 1; i >= 0; i--) { string x = (string)chat.Items[i]; if (x.Length > sp && x.IndexOf(" " + name + ": ") >= 0) chat.Items.RemoveAt(i); }
    }
    static readonly string[] NotorietyNames = { "not seen yet", "innocent (blue)", "friend (green)", "attackable (grey)", "criminal (grey)", "enemy (orange)", "murderer (red)", "invulnerable (yellow)" };
    static readonly string[] Facings = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    string lastTileKey = ""; string lastTile = "";
    // Sextant reading, the classic center at Lord British's throne (1323, 1624) on the 5120 by 4096 overworld.
    static string Sextant(int x, int y) {
        double lon = (x - 1323) * 360.0 / 5120, lat = (y - 1624) * 360.0 / 4096;
        Func<double, string> dm = v => { v = Math.Abs(v); int d = (int)v; int m = (int)Math.Round((v - d) * 60); if (m == 60) { d++; m = 0; } return d + "o" + m.ToString("00") + "'"; };
        return dm(lat) + (lat < 0 ? "N" : "S") + " " + dm(lon) + (lon < 0 ? "W" : "E");
    }
    void Session(GameLink k) {
        if (UoArt.Folder == null && clientPid != 0) UoArt.Folder = AssistNative.ExeFolder(clientPid);
        bool uo1998 = !f.Server.StartsWith("127.0.0.1:2593");
        // The Circuit tab's shops and towns come from the region file, read here after login: filled again once it is.
        bool hadRegions = UoRegions.Loaded;
        if (uo1998) UoRegions.Load(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AssistRelay.settingsPath), "regions-1998.txt"));
        if (!hadRegions && UoRegions.Loaded) FillCircuitSetup();
        var st = k.State; int x, y, z, face, noto; DateTime at; bool? guardLine; var multis = new List<int[]>();
        lock (st.Gate) {
            x = st.X; y = st.Y; z = st.Z; face = st.Facing; noto = st.Notoriety; at = st.LoginAt; guardLine = st.Guarded;
            foreach (var kv in st.Ground) if (kv.Value.Graphic >= 0x4000) multis.Add(new[] { kv.Value.Graphic - 0x4000, kv.Value.X, kv.Value.Y });
        }
        var lines = new List<string>();
        var up = at == default(DateTime) ? TimeSpan.Zero : DateTime.Now - at;
        lines.Add("Logged in " + (at == default(DateTime) ? "-" : ((int)up.TotalHours).ToString() + up.ToString(@"\:mm\:ss")) + " on " + f.Server.TrimEnd('.'));
        lines.Add("At " + x + ", " + y + ", " + z + " facing " + Facings[face & 7] + "   sextant " + Sextant(x, y));
        string key = x + "," + y; if (key != lastTileKey) { lastTileKey = key; lastTile = UoMap.Tile(x, y) ?? "map files not readable"; }
        lines.Add("Tile: " + lastTile);
        bool mapGuarded = false; string zone = uo1998 ? UoRegions.Zone(x, y, z, out mapGuarded) : null;
        string guard = guardLine.HasValue ? (guardLine.Value ? "guarded" : "not guarded") : (zone != null ? (mapGuarded ? "guarded (by the region map)" : "not guarded (by the region map)") : "guarded: no word from the server yet");
        lines.Add("Zone: " + (zone ?? (uo1998 ? "no region file (regions-1998.txt)" : "not sent by this server")) + "   " + guard);
        bool inHouse = false;
        foreach (var m in multis) { var b = UoMap.MultiBox(m[0]); if (b != null && x >= m[1] + b[0] && x <= m[1] + b[2] && y >= m[2] + b[1] && y <= m[2] + b[3]) { inHouse = true; break; } }
        lines.Add((inHouse ? "Inside a house" : "Not inside a house") + "   Status: " + (noto >= 0 && noto < NotorietyNames.Length ? NotorietyNames[noto] : "notoriety " + noto));
        string text = string.Join("\n", lines);
        sessionLine = (zone ?? "") + ", " + (guardLine.HasValue ? (guardLine.Value ? "guarded" : "not guarded") : (mapGuarded ? "guarded" : "not guarded")) + ", at " + x + "," + y + "," + z + ", " + (at == default(DateTime) ? "-" : ((int)up.TotalHours).ToString() + up.ToString(@"\:mm\:ss")) + " online";
        if (session.Text != text) {
            session.Text = text;
            if (session.Height != Grown(session)) Fit();
        }
    }
    // The fail-safe's banner, Log tab and the one-time notice in the client's journal (a client-only line, never sent to
    // the server) when it disables the assist.
    long logSeen; int historySeen; string lastDisabled;
    void Guard(GameLink k) {
        var g = AssistGuard.Current; string off = g == null ? null : g.Disabled;
        banner.Visible = off != null;
        if (off != null) { string t = "Assist disabled by the fail-safe: " + off + ". Your own play still passes through."; if (bannerText.Text != t) bannerText.Text = t; }
        guardState.Text = off != null ? "DISABLED: " + off : "Fail-safe watching; nothing has tripped. Untick a watch below to stop it disabling the assist.";
        if (off != null && off != lastDisabled && k != null) { k.Tell("Assist disabled by the fail-safe: " + off); Note("Fail-safe: " + off); }
        if (off == null && lastDisabled != null) Note("Fail-safe: assist resumed");
        lastDisabled = off;
        var lines = relay.Log.TailSince(ref logSeen);
        if (g != null) lock (g.History) { while (historySeen < g.History.Count) lines.Add(">> " + g.History[historySeen++]); }
        if (lines.Count > 0) {
            bool atEnd = logList.TopIndex >= logList.Items.Count - logList.ClientSize.Height / Math.Max(1, logList.ItemHeight) - 1;
            logList.BeginUpdate(); foreach (var l in lines) logList.Items.Add(l); while (logList.Items.Count > AssistLog.TailKeep) logList.Items.RemoveAt(0); logList.EndUpdate();
            if (atEnd) logList.TopIndex = Math.Max(0, logList.Items.Count - 1);
        }
    }
    void FillPicks() {
        picks.Items.Clear();
        foreach (var gr in f.Reagents()) picks.Items.Add((AssistFeatures.ReagentName(gr) ?? (UoArt.Folder != null ? UoTiles.Name(gr) : gr.ToString("X4"))) + "  (" + gr.ToString("X4") + ")");
    }
    // Counted items under their subgroup headings (harvest, create, reagents, combat), two columns each, each with its
    // art from the client's own ART.MUL.
    void Resources() {
        if (UoArt.Folder == null && clientPid != 0) UoArt.Folder = AssistNative.ExeFolder(clientPid);
        bool stale = f.PackStale(); if (resCount.Visible != stale) resCount.Visible = stale;
        if (!res.Visible) {
            if (resSumPics.Count == 0 || !resSumPics[0].Parent.Visible) return;
            var counts = f.CountRows();
            for (int i = 0; i < ResSummaryArts.Length; i++) {
                int art = ResSummaryArts[i], n = 0; foreach (var r in counts) if (Array.IndexOf(r.Graphics, art) >= 0) n += r.Count;
                if (resSumPics[i].Image == null) resSumPics[i].Image = UoArt.Icon(art);
                string text = ShortReagent[art] + ": " + n; if (resSumCounts[i].Text != text) resSumCounts[i].Text = text;
            }
            return;
        }
        var rows = f.CountRows(); string shape = string.Join(",", rows.ConvertAll(r => r.Name + r.Graphic + r.Group)) + string.Join("", Array.ConvertAll(AssistFeatures.ResourceGroups, g => f.Cfg.Get("collapsed.resources." + g, "0")));
        if (shape != resShape) {
            resShape = shape; res.SuspendLayout(); res.Controls.Clear(); res.RowStyles.Clear();
            resLabels.Clear(); resPics.Clear(); resAt.Clear();
            int row = 0, height = 4;
            foreach (var group in AssistFeatures.ResourceGroups) {
                var inGroup = new List<int>(); for (int i = 0; i < rows.Count; i++) if (rows[i].Group == group) inGroup.Add(i);
                if (inGroup.Count == 0) continue;
                // Each subgroup collapses on its heading (Damian, 2026-10-15: "make the subgroups collapsable in resources").
                bool shut = f.Cfg.Get("collapsed.resources." + group, "0") == "1"; string key = group;
                // The heading sits in the first column, level with its first items (Damian, 2026-10-15: "make the group
                // names and the items start on the same row to perserve vertical screen space").
                var head = new Label { AutoSize = true, Text = (shut ? "[+] " : "[-] ") + group, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, shut ? 3 : 8, 0, 0), Cursor = Cursors.Hand };
                head.Click += (s, e) => { f.Cfg.Set("collapsed.resources." + key, f.Cfg.Get("collapsed.resources." + key, "0") == "1" ? "0" : "1"); resShape = ""; };
                res.Controls.Add(head, 0, row);
                if (shut) { res.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); row++; height += 20; continue; }
                int per = group == "reagents" || group == "gems" || group == "potions" ? 3 : 2, span = 6 / per;
                for (int n = 0; n < inGroup.Count; n++) {
                    int at = inGroup[n];
                    var cell = new FlowLayoutPanel { WrapContents = false, Height = ResRow, Margin = new Padding(0), Dock = DockStyle.Fill };
                    var pic = new PictureBox { Width = ResIcon, Height = ResIcon, SizeMode = PictureBoxSizeMode.Zoom, Image = UoArt.Icon(rows[at].Graphic), Margin = new Padding(0, 2, 4, 2) };
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Remove " + rows[at].Name, null, (s, e) => { var l = f.ResourceList(); if (at < l.Count) { l.RemoveAt(at); f.SetResources(l); } resShape = ""; });
                    foreach (var other in AssistFeatures.ResourceGroups) {
                        if (other == group) continue; string to = other;
                        menu.Items.Add("Move to " + to, null, (s, e) => { var l = f.ResourceList(); if (at < l.Count) { l[at].Group = to; f.SetResources(l); } resShape = ""; });
                    }
                    var label = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0), ContextMenuStrip = menu };
                    cell.ContextMenuStrip = menu; pic.ContextMenuStrip = menu;
                    cell.Controls.Add(pic); cell.Controls.Add(label);
                    res.Controls.Add(cell, 1 + (n % per) * span, row + n / per); res.SetColumnSpan(cell, span);
                    resLabels.Add(label); resPics.Add(pic); resAt.Add(at);
                }
                int lines = (inGroup.Count + per - 1) / per;
                for (int r = 0; r < lines; r++) res.RowStyles.Add(new RowStyle(SizeType.Absolute, ResRow));
                row += lines; height += lines * ResRow;
            }
            res.RowCount = row; res.Height = height; res.ResumeLayout(); Fit();
        }
        for (int i = 0; i < resAt.Count; i++) {
            var r = rows[resAt[i]]; string sn; string text = (r.Group == "reagents" && ShortReagent.TryGetValue(r.Graphic, out sn) ? sn : r.Name) + "  " + r.Count;
            if (resLabels[i].Text != text) resLabels[i].Text = text;
            if (resPics[i].Image == null && UoArt.Folder != null) resPics[i].Image = UoArt.Icon(r.Graphic);
        }
    }

    // For the control port: where the window is, and whether it follows.
    public static AssistWindow Current;
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Current = this; }
    public string Report() { return (string)Invoke(new Func<string>(DockToClient)); }
    // The window drawn to a PNG at the docked size, off screen: a layout check that takes no focus.
    public string Snapshot(string path) {
        return (string)Invoke(new Func<string>(() => {
            if (client == IntPtr.Zero) Size = new Size(620, 1010);
            Fit(); Tick();
            using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
            return "saved " + path + " " + Width + "x" + Height;
        }));
    }
}

// A list whose column header sorts it: a click sorts up, a right-click sorts down.
// With AutoSort the list sorts its own rows on the click (numbers as numbers) and keeps that order through refills
// (Damian, 2026-10-15: "add sorting by column left click asc right click desc"); without it the owner sorts.
public sealed class SortList : ListView {
    public int SortColumn = 3; public bool Descending = true; public bool AutoSort;
    public SortList() { DoubleBuffered = true; ColumnClick += (s, e) => { SortColumn = e.Column; Descending = false; Apply(); }; }
    void Apply() { if (!AutoSort) return; ListViewItemSorter = new ByColumn { Column = SortColumn, Dir = Descending ? -1 : 1 }; Sort(); }
    sealed class ByColumn : System.Collections.IComparer {
        public int Column, Dir;
        public int Compare(object a, object b) {
            string x = Cell((ListViewItem)a), y = Cell((ListViewItem)b); double dx, dy;
            int c = double.TryParse(x, out dx) && double.TryParse(y, out dy) ? dx.CompareTo(dy) : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            return c * Dir;
        }
        string Cell(ListViewItem i) { return Column < i.SubItems.Count ? i.SubItems[Column].Text : ""; }
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x004E) {  // WM_NOTIFY: NMHDR is hwndFrom, idFrom, code
            IntPtr from = Marshal.ReadIntPtr(m.LParam); int code = Marshal.ReadInt32(m.LParam, IntPtr.Size * 2);
            IntPtr header = AssistNative.SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);  // LVM_GETHEADER
            if (from == header && code == -5) {  // NM_RCLICK
                var p = PointToClient(Cursor.Position); int x = 0;
                for (int i = 0; i < Columns.Count; i++) { x += Columns[i].Width; if (p.X < x) { SortColumn = i; Descending = true; break; } }
                Apply(); return;
            }
        }
        base.WndProc(ref m);
    }
}

// Item art from the 1.25 client's ARTIDX.MUL and ART.MUL: an item's entry is 0x4000 + its graphic, 12 bytes (offset,
// length, extra). A static image is a 4-byte header, width and height (u16), one u16 row offset per row counted in u16
// units from the end of that table, then per row (x skip, run length, run of 16-bit 555 colours) pairs ending in 0, 0.
// The 1.25 sound names: SOUNDIDX.MUL holds 12 bytes a sound (offset, length, extra), and each SOUND.MUL entry opens with
// its file name, NUL-padded, in the first 20 bytes. Null for an id with no sound.
public static class UoSounds {
    static string[] names;
    public static string Name(int id) {
        if (UoArt.Folder == null) return null;
        if (names == null) {
            var l = new string[0];
            try {
                var idx = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "SOUNDIDX.MUL")); l = new string[idx.Length / 12];
                using (var fs = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "SOUND.MUL"))) {
                    var b = new byte[20];
                    for (int i = 0; i < l.Length; i++) {
                        int off = BitConverter.ToInt32(idx, i * 12), len = BitConverter.ToInt32(idx, i * 12 + 4);
                        if (off < 0 || len <= 20) continue;
                        fs.Position = off; if (fs.Read(b, 0, 20) < 20) continue;
                        int n = Array.IndexOf(b, (byte)0); string s = System.Text.Encoding.ASCII.GetString(b, 0, n < 0 ? 20 : n);
                        l[i] = s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s;
                    }
                }
            } catch { }
            names = l;
        }
        return id >= 0 && id < names.Length ? names[id] : null;
    }
}

public static class UoArt {
    public static string Folder;
    static byte[] index; static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
    // An item's art cut to its drawn pixels for an icon (Damian, 2026-10-17: "make all the resource icons more zoomed in,
    // each is about half the right size"): a black pearl is a 5 x 5 dot in a 44 x 20 tile. The cut is padded to at least
    // IconMin square, so a zoomed icon is at most about twice its art.
    public const int IconMin = 14;
    static readonly Dictionary<int, Bitmap> icons = new Dictionary<int, Bitmap>();
    public static Bitmap Icon(int graphic) {
        Bitmap c; if (icons.TryGetValue(graphic, out c)) return c;
        var b = Item(graphic); if (b == null) return null;
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) if (b.GetPixel(x, y).A > 0) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
        if (x1 < 0) { icons[graphic] = b; return b; }
        int w = x1 - x0 + 1, h = y1 - y0 + 1, side = Math.Max(IconMin, Math.Max(w, h));
        c = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(c)) g.DrawImage(b, new Rectangle((side - w) / 2, (side - h) / 2, w, h), new Rectangle(x0, y0, w, h), GraphicsUnit.Pixel);
        icons[graphic] = c; return c;
    }
    public static Bitmap Item(int graphic) {
        if (Folder == null) return null;
        Bitmap b; if (cache.TryGetValue(graphic, out b)) return b;
        b = null;
        try {
            if (index == null) index = System.IO.File.ReadAllBytes(System.IO.Path.Combine(Folder, "ARTIDX.MUL"));
            int i = 0x4000 + graphic;
            if ((i + 1) * 12 <= index.Length) {
                int off = BitConverter.ToInt32(index, i * 12), len = BitConverter.ToInt32(index, i * 12 + 4);
                if (off >= 0 && len > 8) {
                    var data = new byte[len];
                    using (var fs = System.IO.File.OpenRead(System.IO.Path.Combine(Folder, "ART.MUL"))) { fs.Position = off; int got = 0; while (got < len) { int n = fs.Read(data, got, len - got); if (n <= 0) break; got += n; } }
                    b = Decode(data);
                }
            }
        } catch { b = null; }
        cache[graphic] = b; return b;
    }
    // A land tile's art: ART.MUL entries below 0x4000 are raw 44 x 44 diamonds, rows 2, 4 ... 44 ... 4, 2 pixels wide,
    // centred, 5-5-5 u16 each.
    static readonly Dictionary<int, Bitmap> lands = new Dictionary<int, Bitmap>();
    public static Bitmap Land(int id) {
        if (Folder == null || id < 0 || id >= 0x4000) return null;
        Bitmap b; if (lands.TryGetValue(id, out b)) return b;
        b = null;
        try {
            if (index == null) index = System.IO.File.ReadAllBytes(System.IO.Path.Combine(Folder, "ARTIDX.MUL"));
            int off = BitConverter.ToInt32(index, id * 12), len = BitConverter.ToInt32(index, id * 12 + 4);
            if (off >= 0 && len >= 2 * 44 * 22) {
                var d = new byte[len];
                using (var fs = System.IO.File.OpenRead(System.IO.Path.Combine(Folder, "ART.MUL"))) { fs.Position = off; int got = 0; while (got < len) { int n = fs.Read(d, got, len - got); if (n <= 0) break; got += n; } }
                var px = new int[44 * 44]; int p = 0;
                for (int y = 0; y < 44; y++) {
                    int w = y < 22 ? 2 + y * 2 : 44 - (y - 22) * 2, x0 = 22 - w / 2;
                    for (int x = 0; x < w && p + 2 <= d.Length; x++, p += 2) { int c = BitConverter.ToUInt16(d, p); px[y * 44 + x0 + x] = unchecked((int)0xFF000000) | (((c >> 10) & 31) * 255 / 31) << 16 | (((c >> 5) & 31) * 255 / 31) << 8 | ((c & 31) * 255 / 31); }
                }
                b = new Bitmap(44, 44, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = b.LockBits(new Rectangle(0, 0, 44, 44), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, bd.Scan0, px.Length); b.UnlockBits(bd);
            }
        } catch { b = null; }
        lands[id] = b; return b;
    }
    // Art scaled by percent, cached (land ids as is, statics + 0x10000), so drawing is a copy.
    static readonly Dictionary<long, Bitmap> scaledArt = new Dictionary<long, Bitmap>();
    public static Bitmap Scaled(int key, int pct) {
        long k = (long)key << 16 | (uint)pct; Bitmap s;
        if (scaledArt.TryGetValue(k, out s)) return s;
        if (scaledArt.Count > 6000) { foreach (var v in scaledArt.Values) if (v != null) v.Dispose(); scaledArt.Clear(); }
        var src = key >= 0x10000 ? Item(key - 0x10000) : Land(key);
        if (src != null) {
            // A land diamond is scaled 2 px larger, so neighbours overlap their soft (partly transparent) edges.
            int grow = key < 0x10000 ? 2 : 0, w = Math.Max(1, src.Width * pct / 100) + grow, h = Math.Max(1, src.Height * pct / 100) + grow;
            s = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(s)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; g.DrawImage(src, new Rectangle(0, 0, w, h)); }
        }
        scaledArt[k] = s; return s;
    }
    static Bitmap Decode(byte[] d) {
        int w = BitConverter.ToUInt16(d, 4), h = BitConverter.ToUInt16(d, 6);
        if (w <= 0 || h <= 0 || w > 512 || h > 512 || 8 + h * 2 > d.Length) return null;
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb); var px = new int[w * h];
        int start = 8 + h * 2;
        for (int y = 0; y < h; y++) {
            int p = start + BitConverter.ToUInt16(d, 8 + y * 2) * 2, x = 0;
            while (p + 4 <= d.Length) {
                int skip = BitConverter.ToUInt16(d, p), run = BitConverter.ToUInt16(d, p + 2); p += 4;
                if (skip + run == 0) break;
                x += skip;
                for (int k = 0; k < run && p + 2 <= d.Length; k++, x++, p += 2) {
                    int c = BitConverter.ToUInt16(d, p);
                    if (x >= 0 && x < w) px[y * w + x] = unchecked((int)0xFF000000) | (((c >> 10) & 31) * 255 / 31) << 16 | (((c >> 5) & 31) * 255 / 31) << 8 | ((c & 31) * 255 / 31);
                }
            }
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, bd.Scan0, px.Length); bmp.UnlockBits(bd);
        return bmp;
    }
}

// A creature's picture for the map (Damian, 2026-10-16: "make the dots into the graphics from the game, scaled to be
// useful"): the first frame of its first action, facing down-right, from the 1.25 client's ANIM.IDX and ANIM.MUL.
// ANIM.IDX holds 12-byte entries (offset, length, extra) per body, action and direction: 110 a body below 200 (22
// actions), 65 a body from 200 to 399 (13), 175 above (35), 5 directions an action. An entry is a palette of 256 5-5-5
// colours, a frame count and per frame an offset from the palette's end; a frame is centre x and y (s16), width and
// height (u16), then runs until 0x7FFF7FFF: a u32 holding x (10 bits from bit 22), y (10 bits from bit 12), both biased
// by 0x200, and a length (12 bits), followed by that many palette indexes (UltimaSDK Frame).
public static class UoAnim {
    public const int Direction = 2;
    static System.IO.FileStream mul; static byte[] index; static string folder;
    static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
    static int Entry(int body) {
        int at = body < 200 ? body * 110 : body < 400 ? 22000 + (body - 200) * 65 : 35000 + (body - 400) * 175;
        return at + Direction;
    }
    public static Bitmap Body(int body) {
        if (UoArt.Folder == null || body <= 0) return null;
        lock (cache) {
            Bitmap b; if (cache.TryGetValue(body, out b)) return b;
            b = null;
            try {
                if (folder != UoArt.Folder) { if (mul != null) mul.Dispose(); mul = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "ANIM.MUL")); index = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "ANIM.IDX")); folder = UoArt.Folder; }
                int e = Entry(body);
                if ((e + 1) * 12 <= index.Length) {
                    int off = BitConverter.ToInt32(index, e * 12), len = BitConverter.ToInt32(index, e * 12 + 4);
                    if (off >= 0 && len > 520 && len < 1 << 22 && off + (long)len <= mul.Length) {
                        var d = new byte[len]; mul.Position = off; int got = 0; while (got < len) { int n = mul.Read(d, got, len - got); if (n <= 0) break; got += n; }
                        b = Frame(d);
                    }
                }
            } catch { b = null; }
            cache[body] = b; return b;
        }
    }
    // The picture scaled to the area of a `side`-pixel square (a long flat rat keeps its length), cached per body and size
    // so a repaint is a plain copy.
    static readonly Dictionary<long, Bitmap> sized = new Dictionary<long, Bitmap>();
    public const int SizedKeep = 2000;
    public static Bitmap Sized(int body, int side) {
        var src = Body(body); if (src == null) return null;
        lock (sized) {
            long key = (long)body << 16 | (uint)side; Bitmap b; if (sized.TryGetValue(key, out b)) return b;
            if (sized.Count > SizedKeep) { foreach (var x in sized.Values) x.Dispose(); sized.Clear(); }
            float k = (float)(side / Math.Sqrt((double)src.Width * src.Height));
            b = new Bitmap(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, b.Width, b.Height); }
            sized[key] = b; return b;
        }
    }
    static Bitmap Frame(byte[] d) {
        var pal = new int[256];
        for (int i = 0; i < 256; i++) { int c = BitConverter.ToUInt16(d, i * 2); pal[i] = UoRadar.ToArgb((ushort)c); }
        int count = BitConverter.ToInt32(d, 512); if (count <= 0) return null;
        int at = 512 + BitConverter.ToInt32(d, 516);
        if (at + 8 > d.Length) return null;
        int cx = BitConverter.ToInt16(d, at), cy = BitConverter.ToInt16(d, at + 2), w = BitConverter.ToUInt16(d, at + 4), h = BitConverter.ToUInt16(d, at + 6);
        if (w <= 0 || h <= 0 || w > 512 || h > 512) return null;
        var px = new int[w * h]; int xBase = cx - 0x200, yBase = cy + h - 0x200; at += 8;
        while (at + 4 <= d.Length) {
            uint head = BitConverter.ToUInt32(d, at); at += 4;
            if (head == 0x7FFF7FFF) break;
            head ^= (0x200u << 22) | (0x200u << 12);
            int x = xBase + (int)((head >> 22) & 0x3FF), y = yBase + (int)((head >> 12) & 0x3FF), run = (int)(head & 0xFFF);
            for (int i = 0; i < run && at < d.Length; i++, at++) { int xx = x + i; if (y >= 0 && y < h && xx >= 0 && xx < w) px[y * w + xx] = pal[d[at]]; }
        }
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, bd.Scan0, px.Length); bmp.UnlockBits(bd); return bmp;
    }
}

// Item names, flags and weights from the 1.25 client's TILEDATA.MUL (the pre-High Seas layout): 512 land blocks of
// a 4-byte header and 32 entries of 26 bytes, then item blocks of a 4-byte header and 32 entries of 37 bytes (flags u32,
// weight, quality, u16, u8, quantity, animation u16, u8, hue, u16, height, name of 20 ASCII bytes).
public static class UoTiles {
    // Misc: the record's int16 at offset 6, a weapon's entry in the server's weapon table (OUO load.c:303, item.c:5906).
    public sealed class Tile { public int Graphic, Weight, Height, Misc; public uint Flags; public string Name; }
    static List<Tile> items;
    public static List<Tile> Items() {
        if (items != null) return items;
        var list = new List<Tile>();
        try {
            var d = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "TILEDATA.MUL"));
            const int land = 512 * (4 + 32 * 26);
            for (int g = 0; ; g++) {
                int at = land + (g / 32) * (4 + 32 * 37) + 4 + (g % 32) * 37;
                if (at + 37 > d.Length) break;
                int n = 0; while (n < 20 && d[at + 17 + n] != 0) n++;
                // A name carries its plural as %s or %es/% markers ("iron ingot%s", "Black Pearl%s%"): the singular drops them.
                string raw = System.Text.Encoding.ASCII.GetString(d, at + 17, n); var sb = new System.Text.StringBuilder(); bool marked = false; foreach (char ch in raw) { if (ch == '%') { marked = !marked; continue; } if (!marked) sb.Append(ch); } string name = sb.ToString().Trim();
                list.Add(new Tile { Graphic = g, Flags = BitConverter.ToUInt32(d, at), Weight = d[at + 4], Height = d[at + 16], Misc = BitConverter.ToInt16(d, at + 6), Name = name });
            }
        } catch { }
        if (list.Count > 0) items = list;
        return list;
    }
    public static string Name(int graphic) { var l = Items(); return graphic >= 0 && graphic < l.Count && l[graphic].Name.Length > 0 ? l[graphic].Name : graphic.ToString("X4"); }
    // Never lifted: weight 255, or a background, wall, impassable, surface, bridge, window, internal, foliage, roof or door tile.
    const uint Fixed = 0x1 | 0x10 | 0x40 | 0x200 | 0x400 | 0x1000 | 0x10000 | 0x20000 | 0x10000000 | 0x20000000;
    public static bool Liftable(Tile t) { return t.Weight < 255 && (t.Flags & Fixed) == 0 && t.Name.Length > 0; }
    // The tile data's Generic flag (0x800): the art stacks. An art the tile data does not hold is taken to stack.
    public static bool Stacks(int graphic) { var l = Items(); return graphic < 0 || graphic >= l.Count || (l[graphic].Flags & 0x800) != 0; }
}

// Picks one item type: carried ones first, then ones on the ground nearby, then stackables, then by weight; a filter box
// narrows by name. Double-click or Add returns the graphic.
public sealed class ItemPicker : Form {
    const int PickIcon = 32;
    int picked = -1; readonly ListView list; readonly TextBox filter; readonly List<string[]> rows = new List<string[]>();
    public static int Pick(IWin32Window owner, string title, HashSet<int> carried, HashSet<int> near) {
        using (var p = new ItemPicker(title, carried, near)) { p.ShowDialog(owner); return p.picked; }
    }
    ItemPicker(string title, HashSet<int> carried, HashSet<int> near) {
        Text = title; Size = new Size(480, 640); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 8.5f);
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        filter = new TextBox { Dock = DockStyle.Top };
        list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        list.Columns.Add("Item", 250); list.Columns.Add("Where", 70); list.Columns.Add("Weight", 50); list.Columns.Add("Graphic", 55);
        // Each row shows the item's art (Damian, 2026-10-18: "add graphical icons of each that can be shown, since there are
        // lots of books for example... which one?"), drawn as the row is painted: an icon costs a scan of its art, too slow
        // for every row of the list at once. The image list only sets the row height.
        list.SmallImageList = new ImageList { ImageSize = new Size(PickIcon, PickIcon) }; list.OwnerDraw = true;
        list.DrawColumnHeader += (s, e) => e.DrawDefault = true;
        list.DrawSubItem += (s, e) => {
            bool on = e.Item.Selected; var back = on ? SystemColors.Highlight : SystemColors.Window; var fore = on ? SystemColors.HighlightText : SystemColors.WindowText;
            using (var br = new SolidBrush(back)) e.Graphics.FillRectangle(br, e.Bounds);
            var r = e.Bounds;
            if (e.ColumnIndex == 0) {
                var art = UoArt.Icon(Convert.ToInt32(e.Item.SubItems[3].Text, 16));
                if (art != null) { float k = Math.Min((float)(PickIcon - 2) / art.Width, (float)(PickIcon - 2) / art.Height); int w = (int)(art.Width * k), h = (int)(art.Height * k); e.Graphics.DrawImage(art, r.X + 2 + (PickIcon - 2 - w) / 2, r.Y + (r.Height - h) / 2, w, h); }
                r = new Rectangle(r.X + PickIcon + 6, r.Y, r.Width - PickIcon - 6, r.Height);
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, r, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Add" }; var cancel = new Button { Text = "Cancel" };
        ok.Click += (s, e) => Choose(); cancel.Click += (s, e) => Close(); list.DoubleClick += (s, e) => Choose();
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        Controls.Add(list); Controls.Add(filter); Controls.Add(new Label { Dock = DockStyle.Top, Height = 18, Text = "Type to filter by name." }); Controls.Add(buttons);
        var tiles = new List<UoTiles.Tile>();
        foreach (var t in UoTiles.Items()) if (UoTiles.Liftable(t) || carried.Contains(t.Graphic) || near.Contains(t.Graphic)) tiles.Add(t);
        Func<UoTiles.Tile, int> rank = t => (carried.Contains(t.Graphic) ? 0 : near.Contains(t.Graphic) ? 1 : (t.Flags & 0x800) != 0 ? 2 : 3);
        tiles.Sort((a, b) => { int d = rank(a).CompareTo(rank(b)); if (d != 0) return d; d = a.Weight.CompareTo(b.Weight); return d != 0 ? d : string.CompareOrdinal(a.Name, b.Name); });
        foreach (var t in tiles) rows.Add(new[] { t.Name.Length > 0 ? t.Name : t.Graphic.ToString("X4"), carried.Contains(t.Graphic) ? "carried" : near.Contains(t.Graphic) ? "nearby" : (t.Flags & 0x800) != 0 ? "stacks" : "", t.Weight.ToString(), t.Graphic.ToString("X4") });
        filter.TextChanged += (s, e) => Fill(); Fill();
        AcceptButton = ok; CancelButton = cancel;
    }
    void Fill() {
        string q = filter.Text.Trim(); list.BeginUpdate(); list.Items.Clear(); int shown = 0;
        foreach (var r in rows) {
            if (q.Length > 0 && r[0].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && r[3].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            list.Items.Add(new ListViewItem(r)); if (++shown >= 2000) break;
        }
        list.EndUpdate();
    }
    void Choose() { if (list.SelectedItems.Count == 0) return; picked = Convert.ToInt32(list.SelectedItems[0].SubItems[3].Text, 16); DialogResult = DialogResult.OK; Close(); }
}

// What is at a map tile, from the client's own MAP0, STAIDX0, STATICS0, TILEDATA, MULTI files (6144 by 4096 map,
// 8 by 8 blocks numbered x-block * 512 + y-block). MAP0: per block a 4-byte header and 64 cells of (land id u16, z s8),
// row by row. STAIDX0: per block (offset, length, extra); STATICS0: 7-byte entries (graphic u16, x u8, y u8, z s8, hue).
// TILEDATA land entries: 512 blocks of a 4-byte header and 32 of (flags u32, texture u16, name 20). MULTI.IDX per multi
// (offset, length, extra); MULTI.MUL 12-byte components (item u16, x s16, y s16, z s16, flags u32) around the center.
// The client's radar colours (RADARCOL.MUL: 0x8000 u16 colours, 5-5-5, land ids first, static ids from 0x4000) over
// MAP0.MUL and the statics: one colour per tile, the highest static's where a static stands, else the land's.
// Blocks of 8x8 tiles are cached, 128 bytes of colours each; the cache is dropped past BlockKeep blocks (5 MiB).
public static class UoRadar {
    const int BlocksTall = 512, BlockKeep = 20000;
    static ushort[] colors; static string folder; static System.IO.FileStream map, sidx, stat; static int blocksWide;
    // Per cell of a block, x fastest: the radar colour of the top surface, its height, the land id under it, and whether
    // a static covers the land.
    public sealed class Cells {
        public readonly ushort[] Color = new ushort[64], Land = new ushort[64], Static = new ushort[64]; public readonly sbyte[] Z = new sbyte[64], LandZ = new sbyte[64]; public readonly bool[] Covered = new bool[64];
        // Every static per cell, lowest first: graphic | (z & 0xFF) << 16; null where a cell has none.
        public readonly List<int>[] ByCell = new List<int>[64];
    }
    static readonly Dictionary<int, Cells> blocks = new Dictionary<int, Cells>();
    static bool Open() {
        if (UoArt.Folder == null) return false;
        if (folder == UoArt.Folder && colors != null) return true;
        try {
            var c = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "RADARCOL.MUL"));
            colors = new ushort[c.Length / 2]; Buffer.BlockCopy(c, 0, colors, 0, colors.Length * 2);
            map = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "MAP0.MUL"));
            sidx = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "STAIDX0.MUL"));
            stat = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "STATICS0.MUL"));
            blocksWide = (int)(map.Length / 196 / BlocksTall); folder = UoArt.Folder; blocks.Clear(); return true;
        } catch { colors = null; return false; }
    }
    static byte[] Read(System.IO.FileStream fs, long at, int len) {
        if (at < 0 || len <= 0 || at + len > fs.Length) return null;
        var b = new byte[len]; fs.Position = at; int got = 0; while (got < len) { int n = fs.Read(b, got, len - got); if (n <= 0) return null; got += n; } return b;
    }
    // The cells of block (bx, by); null off the map or with no files.
    public static Cells Block(int bx, int by) {
        lock (blocks) {
            if (!Open() || bx < 0 || by < 0 || bx >= blocksWide || by >= BlocksTall) return null;
            int key = bx * BlocksTall + by; Cells got; if (blocks.TryGetValue(key, out got)) return got;
            if (blocks.Count > BlockKeep) blocks.Clear();
            var cells = Read(map, (long)key * 196 + 4, 192); if (cells == null) return null;
            var c = new Cells();
            for (int i = 0; i < 64; i++) { int id = BitConverter.ToUInt16(cells, i * 3); c.Land[i] = (ushort)id; c.Color[i] = id < colors.Length ? colors[id] : (ushort)0; c.Z[i] = c.LandZ[i] = (sbyte)cells[i * 3 + 2]; }
            var idx = Read(sidx, (long)key * 12, 12);
            if (idx != null) {
                int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
                var st = off >= 0 && len > 0 && len < 1 << 20 ? Read(stat, off, len) : null;
                if (st != null) for (int i = 0; i + 7 <= st.Length; i += 7) {
                    int id = BitConverter.ToUInt16(st, i), cell = st[i + 3] * 8 + st[i + 2], z = (sbyte)st[i + 4];
                    if (cell < 64) {
                        var at = c.ByCell[cell] ?? (c.ByCell[cell] = new List<int>());
                        int v = (id & 0xFFFF) | (z & 0xFF) << 16, pri = Priority(id, z), j = at.Count; while (j > 0 && Priority(at[j - 1] & 0xFFFF, (sbyte)(at[j - 1] >> 16)) > pri) j--; at.Insert(j, v);
                    }
                    if (cell < 64 && z >= c.Z[cell] && 0x4000 + id < colors.Length) { c.Z[cell] = (sbyte)z; c.Color[cell] = colors[0x4000 + id]; c.Covered[cell] = true; c.Static[cell] = (ushort)id; }
                }
            }
            blocks[key] = c; return c;
        }
    }
    public static int ToArgb(ushort c) { int r = (c >> 10) & 31, g = (c >> 5) & 31, b = c & 31; return unchecked((int)0xFF000000) | (r * 255 / 31) << 16 | (g * 255 / 31) << 8 | (b * 255 / 31); }
    // A static's drawing order on its tile, as the client's (ClassicUO GameObject PriorityZ): its z, one less for a
    // background piece (TILEDATA flag 0x1), one more for one with height, so a tree's leaves draw over its trunk.
    static int Priority(int id, int z) {
        var items = UoTiles.Items(); if (id < 0 || id >= items.Count) return z;
        var t = items[id]; return z - ((t.Flags & 0x1) != 0 ? 1 : 0) + (t.Height > 0 ? 1 : 0);
    }
    public static int LandZAt(int x, int y, int fallback) { if (x < 0 || y < 0) return fallback; var b = Block(x >> 3, y >> 3); return b == null ? fallback : b.LandZ[(y & 7) * 8 + (x & 7)]; }
    // The top surface's height at (x, y), or fallback off the map.
    public static int TopZ(int x, int y, int fallback) { if (x < 0 || y < 0) return fallback; var b = Block(x >> 3, y >> 3); return b == null ? fallback : b.Z[(y & 7) * 8 + (x & 7)]; }
    // Hill shading, light from the northwest: a surface rising toward the southeast faces the light.
    public static int Shade(int argb, int z, int zNorthWest) { return Scale(argb, Math.Max(55, Math.Min(145, 100 + (z - zNorthWest) * 5))); }
    // The colour at f percent brightness.
    public static int Scale(int argb, int f) {
        int r = Math.Min(255, ((argb >> 16) & 255) * f / 100), g = Math.Min(255, ((argb >> 8) & 255) * f / 100), b = Math.Min(255, (argb & 255) * f / 100);
        return unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
    }
}

// Foliage among the statics, by the 1.25 TILEDATA name (Damian, 2026-10-15: "what about the trees and foliage"): 2 a
// canopy (trees, leaves, palms), 1 a small plant (vines, flowers, ferns, cacti, mushrooms), 3 water, 4 grass (drawn
// as the land texture beneath it), 0 anything else, and 0 for what is built of plants (a palm frond roof, a log wall,
// a potted tree).
public static class UoFoliage {
    static byte[] kind;
    static bool Any(string n, params string[] parts) { foreach (var p in parts) if (n.Contains(p)) return true; return false; }
    public static int Kind(int id) {
        if (kind == null) {
            var items = UoTiles.Items(); if (items.Count == 0) return 0;
            var k = new byte[items.Count];
            foreach (var t in items) {
                string n = t.Name.ToLowerInvariant(); if (n.Length == 0 || t.Graphic >= k.Length) continue;
                if (n == "water") { k[t.Graphic] = 3; continue; }
                if (n == "grass" || n == "grasses") { k[t.Graphic] = 4; continue; }
                if (Any(n, "roof", "wall", "table", "post", "pot", "grass", "wooden", "dried", "sunken")) continue;
                if (n.EndsWith("tree") || n.EndsWith("leaves") || Any(n, "palm", "fronds")) k[t.Graphic] = 2;
                else if (Any(n, "vine", "flower", "fern", "cactus", "mushroom", "plant", "lilypad", "stump", "reed", "weed", "bush", "shrub", "seaweed")) k[t.Graphic] = 1;
            }
            kind = k;
        }
        return id >= 0 && id < kind.Length ? kind[id] : 0;
    }
}

// Land textures (TEXIDX.MUL per texture: offset, length, extra; TEXMAPS.MUL 5-5-5 u16 pixels, 64x64 at 0x2000 bytes, 128x128
// at 0x8000), each land id's texture from TILEDATA's land entries (flags u32, texture u16, name 20), scaled by area
// average to the map's tile size and cached per (texture, size).
public static class UoTex {
    static ushort[] landTexture; static bool[] landWater; static byte[] index; static string folder;
    // Land named "water" in TILEDATA (the ocean's land tiles carry no texture).
    public static bool IsWater(int landId) { lock (scaled) return Open() && landId >= 0 && landId < landWater.Length && landWater[landId]; }
    static readonly Dictionary<long, int[]> scaled = new Dictionary<long, int[]>();
    const int ScaledKeep = 4000;
    static bool Open() {
        if (UoArt.Folder == null) return false;
        if (folder == UoArt.Folder && index != null) return true;
        try {
            var td = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "TILEDATA.MUL"));
            landTexture = new ushort[0x4000]; landWater = new bool[0x4000];
            for (int id = 0; id < 0x4000; id++) {
                int at = (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26 + 4; if (at + 22 > td.Length) continue;
                landTexture[id] = BitConverter.ToUInt16(td, at);
                int n = 0; while (n < 20 && td[at + 2 + n] != 0) n++;
                landWater[id] = System.Text.Encoding.ASCII.GetString(td, at + 2, n).Trim().ToLowerInvariant() == "water";
            }
            index = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UoArt.Folder, "TEXIDX.MUL")); folder = UoArt.Folder; scaled.Clear(); return true;
        } catch { index = null; return false; }
    }
    // The land id's texture as size x size ARGB pixels, or null when it has none.
    public static int[] Land(int landId, int size) {
        lock (scaled) {
            if (!Open() || landId < 0 || landId >= landTexture.Length) return null;
            int tex = landTexture[landId]; long key = (long)tex << 16 | (uint)size; int[] got;
            if (scaled.TryGetValue(key, out got)) return got;
            if (scaled.Count > ScaledKeep) scaled.Clear();
            int[] px = null;
            if (tex > 0 && (tex + 1) * 12 <= index.Length) {
                int off = BitConverter.ToInt32(index, tex * 12), len = BitConverter.ToInt32(index, tex * 12 + 4);
                int s = len == 0x8000 ? 128 : len == 0x2000 ? 64 : 0;
                if (off >= 0 && s > 0) {
                    var d = new byte[len];
                    using (var fs = System.IO.File.OpenRead(System.IO.Path.Combine(UoArt.Folder, "TEXMAPS.MUL"))) { fs.Position = off; int n = 0; while (n < len) { int r = fs.Read(d, n, len - n); if (r <= 0) break; n += r; } }
                    px = new int[size * size];
                    for (int j = 0; j < size; j++) for (int i = 0; i < size; i++) {
                        int u0 = i * s / size, u1 = Math.Max(u0 + 1, (i + 1) * s / size), v0 = j * s / size, v1 = Math.Max(v0 + 1, (j + 1) * s / size), r = 0, g = 0, b = 0, n = 0;
                        for (int v = v0; v < v1; v++) for (int u = u0; u < u1; u++) { int c = BitConverter.ToUInt16(d, (v * s + u) * 2); r += (c >> 10) & 31; g += (c >> 5) & 31; b += c & 31; n++; }
                        px[j * size + i] = unchecked((int)0xFF000000) | (r * 255 / 31 / n) << 16 | (g * 255 / 31 / n) << 8 | (b * 255 / 31 / n);
                    }
                }
            }
            scaled[key] = px; return px;
        }
    }
}

// The map tab's view: the tiles around (CenterX, CenterY) at Zoom pixels a tile, the trail, the people and the player.
// Hits, mana and stamina as three stacked bars over the right of the client's title bar (Damian, 2026-10-15: "uo assist
// puts a graphic 3 bars stacked in the title bar of the client for hits, mana, stamina ... can we do similar"). UOAssist
// paints inside the client; this is a window of the assist's own laid over the title bar, which never takes focus and
// lets every click through to the client beneath.
public sealed class TitleBars : Form {
    public const int BarsWidth = 120, CaptionButtons = 150;
    public int Hits, MaxHits, Mana, MaxMana, Stam, MaxStam;
    public TitleBars() {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 24); DoubleBuffered = true; Bounds = new Rectangle(-32000, -32000, BarsWidth, 20);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    // WS_EX_LAYERED | WS_EX_TRANSPARENT (clicks pass through) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE.
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000; return p; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Opacity = 0.97; }
    public void Set(int hits, int maxHits, int mana, int maxMana, int stam, int maxStam) {
        if (hits == Hits && maxHits == MaxHits && mana == Mana && maxMana == MaxMana && stam == Stam && maxStam == MaxStam) return;
        Hits = hits; MaxHits = maxHits; Mana = mana; MaxMana = maxMana; Stam = stam; MaxStam = maxStam; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; int pad = 3, gap = 1, h = Math.Max(3, (Height - pad * 2 - gap * 2) / 3), w = Width - pad * 2;
        var rows = new[] { new[] { Hits, MaxHits }, new[] { Mana, MaxMana }, new[] { Stam, MaxStam } };
        var colors = new[] { Color.FromArgb(220, 40, 40), Color.FromArgb(50, 110, 240), Color.FromArgb(230, 200, 40) };
        for (int i = 0; i < 3; i++) {
            int y = pad + i * (h + gap), fill = rows[i][1] > 0 ? Math.Max(0, Math.Min(w, w * rows[i][0] / rows[i][1])) : 0;
            using (var back = new SolidBrush(Color.FromArgb(60, 60, 60))) g.FillRectangle(back, pad, y, w, h);
            using (var b = new SolidBrush(colors[i])) g.FillRectangle(b, pad, y, fill, h);
        }
    }
}

public sealed class MapView : Control {
    public int CenterX { get; set; }
    public int CenterY { get; set; }
    public int Zoom { get; set; } = 2;
    public Func<Point[]> Trail; public Action<int, int> Hover; public Func<AssistFeatures.Corpse[]> Corpses;
    public sealed class Mark { public int X, Y, Body; public Color Color; public uint Serial; public int Scale = 100; }
    // A creature's symbol in percent of the normal size by its TripLore level (Damian, 2026-10-16: "the size of the symbols
    // should be roughly equiv to the value/difficulty of the critter. a big red circle for a rat, versus the same size for
    // a liche is bad"): harmless 50, newbie 75, medium 125, hard 175, endgame 225; a name the table lacks stays 100.
    static readonly int[] TierScales = { 50, 75, 125, 175, 225 };
    public static int TierScale(string name) { var l = TripLore.Of(name); return l == null ? 100 : TierScales[Math.Max(0, Math.Min(4, l.Tier))]; }
    public Func<Mark[]> Marks; public Func<bool> ShowRegions; public Action<uint> Picked, Opened; public uint Selected; public Func<string> Caption; public Func<AssistFeatures.Waypoint[]> Course; public Func<AssistFeatures.KillMark[]> Kills;
    public sealed class Pin { public int X, Y; public string Kind = "Other", Label = ""; }
    public static readonly string[] PinKinds = { "My house", "Friend's house", "Enemy's house", "Other" };
    public static Color PinColor(string kind) { return kind == "My house" ? Color.Gold : kind == "Friend's house" ? Color.LimeGreen : kind == "Enemy's house" ? Color.Red : Color.White; }
    public Func<Pin[]> Pins; public Action<int, int, Point> RightClick;
    public Point TileAt(Point p) { return ToTile(p.X, p.Y); }
    public Point PixelOf(int x, int y) { return ToPixel(x, y); }
    Mark[] shown = new Mark[0];
    // Dragging pans the view off the player by (OffX, OffY) tiles (Damian, 2026-10-15: "in the map, let drag move around");
    // a double-click on empty map comes back to the player.
    public int OffX, OffY; int ViewX { get { return CenterX + OffX; } } int ViewY { get { return CenterY + OffY; } }
    Point dragFrom; int dragOffX, dragOffY; bool dragging, dragged;
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { dragging = true; dragged = false; dragFrom = e.Location; dragOffX = OffX; dragOffY = OffY; } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) dragging = false; }
    Mark Nearest(Point p) {
        Mark best = null; int bestD = 100;
        foreach (var m in shown) { var at = ToPixel(m.X, m.Y); int d = (at.X - p.X) * (at.X - p.X) + (at.Y - p.Y) * (at.Y - p.Y); if (d <= bestD) { bestD = d; best = m; } }
        return best;
    }
    // A click picks the nearest mark within 10 pixels; the end of a drag is not a click.
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e); if (dragged) return;
        if (e.Button == MouseButtons.Right) { var t = ToTile(e.X, e.Y); if (RightClick != null) RightClick(t.X, t.Y, e.Location); return; }
        var best = Nearest(e.Location);
        Selected = best != null ? best.Serial : 0; if (Picked != null) Picked(Selected); Invalidate();
    }
    // A double-click on a mark opens it (a vendor's Shops entry, Damian, 2026-10-15); on empty map it re-centres.
    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e); var best = Nearest(e.Location);
        if (best != null && best.Serial != 0) { if (Opened != null) Opened(best.Serial); }
        else { OffX = OffY = 0; Invalidate(); }
    }
    Bitmap tiles; int drawnX = -1, drawnY = -1, drawnZoom, drawnW, drawnH;
    public MapView() { DoubleBuffered = true; BackColor = Color.Black; SetStyle(ControlStyles.Selectable, true); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (dragging && (e.Button & MouseButtons.Left) != 0) {
            int dx = e.X - dragFrom.X, dy = e.Y - dragFrom.Y;
            if (dragged || Math.Abs(dx) > 3 || Math.Abs(dy) > 3) {
                dragged = true;
                if (Iso) { double a = dx / Half, b = dy / Half; OffX = dragOffX - (int)Math.Round((a + b) / 2); OffY = dragOffY - (int)Math.Round((b - a) / 2); }
                else { OffX = dragOffX - dx / Zoom; OffY = dragOffY - dy / Zoom; }
                Invalidate();
            }
        }
        if (Hover != null) { var t = ToTile(e.X, e.Y); Hover(t.X, t.Y); }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (Hover != null) Hover(-1, -1); }
    // Game view from IsoZoom (Damian, 2026-10-15: "same with buildings", "the cool thing was seeing the building with all
    // the correct tiling and such"): the client's own projection, a tile a 44 x 44 diamond (scaled), x to the lower right,
    // y to the lower left, 4 pixels up a z step, every land tile and static drawn with its art back to front.
    // From 4 px a tile (Damian, 2026-10-15: "theres still 3 levels of zoom where its like the symbols and the transition
    // doesn't look great"): 20 to 55 ms a view at 4, 6 and 8 (480 x 500, Britain and the Yew woods).
    public const int IsoZoom = 4;
    // Every zoom shares the game's orientation (Damian, 2026-10-15: "when you get zooomed in, it switches the orientation"):
    // below IsoZoom the radar colours are laid out on the same diamonds, Zoom pixels from a tile's centre to its corner.
    bool Iso { get { return true; } }
    int IsoPct { get { return Zoom * 100 / 22; } }
    double Half { get { return Zoom >= IsoZoom ? 22.0 * IsoPct / 100 : Zoom; } }
    void RenderRadarIso() {
        int W = Width, H = Height; double half = Half;
        if (tiles != null && drawnX == ViewX && drawnY == ViewY && drawnZoom == Zoom && drawnW == W && drawnH == H) return;
        if (tiles == null || tiles.Width != W || tiles.Height != H || tiles.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb) { if (tiles != null) tiles.Dispose(); tiles = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb); }
        var px = new int[W * H]; int black = unchecked((int)0xFF000000);
        for (int py = 0; py < H; py++) {
            double b = (py - H / 2.0) / half;
            for (int pxx = 0; pxx < W; pxx++) {
                double a = (pxx - W / 2.0) / half;
                int x = ViewX + (int)Math.Floor((a + b) / 2 + 0.5), y = ViewY + (int)Math.Floor((b - a) / 2 + 0.5);
                if (x < 0 || y < 0) { px[py * W + pxx] = black; continue; }
                var blk = UoRadar.Block(x >> 3, y >> 3); if (blk == null) { px[py * W + pxx] = black; continue; }
                int cell = (y & 7) * 8 + (x & 7), z = blk.Z[cell];
                px[py * W + pxx] = UoRadar.Shade(UoRadar.ToArgb(blk.Color[cell]), z, UoRadar.TopZ(x - 1, y - 1, z));
            }
        }
        var data = tiles.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length); tiles.UnlockBits(data);
        drawnX = ViewX; drawnY = ViewY; drawnZoom = Zoom; drawnW = W; drawnH = H; drawnScale = 1;
    }
    Point ToTile(int px, int py) {
        if (Iso) { double a = (px - Width / 2.0) / Half, b = (py - Height / 2.0) / Half; return new Point(ViewX + (int)Math.Round((a + b) / 2), ViewY + (int)Math.Round((b - a) / 2)); }
        int tw = Width / Zoom, th = Height / Zoom; return new Point(ViewX - tw / 2 + px / Zoom, ViewY - th / 2 + py / Zoom);
    }
    Point ToPixel(int x, int y) {
        if (Iso) { int dx = x - ViewX, dy = y - ViewY, lz = UoRadar.LandZAt(x, y, 0); return new Point((int)(Width / 2 + (dx - dy) * Half), (int)(Height / 2 + (dx + dy) * Half - lz * 4 * Half / 22)); }
        int tw = Width / Zoom, th = Height / Zoom; return new Point((x - (ViewX - tw / 2)) * Zoom + Zoom / 2, (y - (ViewY - th / 2)) * Zoom + Zoom / 2);
    }
    void RenderIso() {
        int W = Width, H = Height, pct = IsoPct; double half = Half, zk = 4 * half / 22;
        if (tiles != null && drawnX == ViewX && drawnY == ViewY && drawnZoom == Zoom && drawnW == W && drawnH == H) return;
        if (tiles == null || tiles.Width != W || tiles.Height != H) { if (tiles != null) tiles.Dispose(); tiles = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); }
        int R = (int)((W + H) / (2 * half)) + 8;
        using (var g = Graphics.FromImage(tiles)) {
            g.Clear(Color.Black); g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            // Back to front: by x + y, then x; on a tile its land, then its statics lowest first.
            for (int d = -2 * R; d <= 2 * R; d++) for (int dx = -R; dx <= R; dx++) {
                int dy = d - dx; if (dy < -R || dy > R) continue;
                double sx = W / 2.0 + (dx - dy) * half, sy = H / 2.0 + (dx + dy) * half;
                if (sx < -2 * half || sx > W + 2 * half || sy < -2 * half || sy > H + 130 * zk) continue;
                int x = ViewX + dx, y = ViewY + dy; if (x < 0 || y < 0) continue;
                var blk = UoRadar.Block(x >> 3, y >> 3); if (blk == null) continue;
                int cell = (y & 7) * 8 + (x & 7);
                var land = UoArt.Scaled(blk.Land[cell], pct);
                if (land != null) {
                    // Land is drawn flat at its own height, so a slope leaves a seam toward a lower neighbour: the
                    // diamond is drawn again down to the lowest of its southern neighbours to close it.
                    int lz = blk.LandZ[cell], low = Math.Min(UoRadar.LandZAt(x + 1, y, lz), Math.Min(UoRadar.LandZAt(x, y + 1, lz), UoRadar.LandZAt(x + 1, y + 1, lz)));
                    int lx = (int)(sx - land.Width / 2.0), top = (int)(sy - half - lz * zk), bottom = (int)(sy - half - low * zk), step = Math.Max(1, (int)(half / 2));
                    for (int fy = bottom; fy > top; fy -= step) g.DrawImageUnscaled(land, lx, fy);
                    g.DrawImageUnscaled(land, lx, top);
                }
                var here = blk.ByCell[cell]; if (here == null) continue;
                foreach (int v in here) {
                    var art = UoArt.Scaled(0x10000 + (v & 0xFFFF), pct); if (art == null) continue;
                    g.DrawImageUnscaled(art, (int)(sx - art.Width / 2.0), (int)(sy + half - art.Height - (sbyte)(v >> 16) * zk));
                }
            }
        }
        drawnX = ViewX; drawnY = ViewY; drawnZoom = Zoom; drawnW = W; drawnH = H; drawnScale = 1;
    }
    // Zoom steps, in pixels a tile; from TextureZoom up the land is drawn with its texture (Damian, 2026-10-15: "can you make
    // our maps look as good on the zoom in as https://uomaps.com/"). Every zoom is hill-shaded.
    public static readonly int[] ZoomSteps = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
    public const int TextureZoom = 4;
    int drawnScale = 1; readonly List<int[]> canopies = new List<int[]>();
    void Render() {
        if (Zoom >= IsoZoom) { RenderIso(); return; }
        if (Iso) { RenderRadarIso(); return; }
        int tw = Math.Max(1, Width / Zoom), th = Math.Max(1, Height / Zoom), x0 = ViewX - tw / 2, y0 = ViewY - th / 2;
        if (tiles != null && drawnX == ViewX && drawnY == ViewY && drawnZoom == Zoom && drawnW == tw && drawnH == th) return;
        bool tex = Zoom >= TextureZoom; int s = tex ? Zoom : 1, bw = tw * s, bh = th * s;
        if (tiles == null || tiles.Width != bw || tiles.Height != bh) { if (tiles != null) tiles.Dispose(); tiles = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format32bppArgb); }
        var px = new int[bw * bh]; int black = unchecked((int)0xFF000000);
        for (int ty = 0; ty < th; ty++) {
            int y = y0 + ty; UoRadar.Cells blk = null; int lastBx = int.MinValue;
            for (int tx = 0; tx < tw; tx++) {
                int x = x0 + tx, bx = x >> 3;
                if (x >= 0 && y >= 0 && bx != lastBx) { blk = UoRadar.Block(bx, y >> 3); lastBx = bx; }
                if (x < 0 || y < 0 || blk == null) { for (int j = 0; j < s; j++) for (int i = 0; i < s; i++) px[(ty * s + j) * bw + tx * s + i] = black; continue; }
                int cell = (y & 7) * 8 + (x & 7), z = blk.Z[cell], zNw = UoRadar.TopZ(x - 1, y - 1, z);
                int flat = UoRadar.ToArgb(blk.Color[cell]);
                if (!tex) { px[ty * bw + tx] = UoRadar.Shade(flat, z, zNw); continue; }
                // Foliage: the ground drawn beneath, and a canopy laid over it once the ground is down.
                int leafy = blk.Covered[cell] ? UoFoliage.Kind(blk.Static[cell]) : UoTex.IsWater(blk.Land[cell]) ? 3 : 0;
                if (leafy == 3) {
                    // Water: the client's water radar colours run near black, so a blue with a ripple.
                    for (int j = 0; j < s; j++) for (int i = 0; i < s; i++) {
                        int wx = x * s + i, wy = y * s + j, ripple = (int)(10 * Math.Sin(wx * 0.35 + wy * 0.18) + 6 * Math.Sin(wy * 0.5 - wx * 0.12));
                        px[(ty * s + j) * bw + tx * s + i] = UoRadar.Scale(unchecked((int)0xFF1E4F86), 100 + ripple);
                    }
                    continue;
                }
                if (leafy == 2) {
                    // A crown in leaf green when the radar colour is not green (a trunk's brown or grey), varied per tree.
                    int r = (flat >> 16) & 255, g = (flat >> 8) & 255, b = flat & 255;
                    int leaf = g > r + 8 && g > b + 8 ? flat : UoRadar.Scale(unchecked((int)0xFF2F5A26), 85 + (int)((uint)(x * 7919 + y * 104729) % 35));
                    canopies.Add(new[] { tx * s + s / 2, ty * s + s / 2, s * 3 / 4 + 1, leaf });
                }
                else if (leafy == 1 && s >= 12) canopies.Add(new[] { tx * s + s / 2, ty * s + s / 2, s / 4, flat });
                var t = blk.Covered[cell] && leafy == 0 ? null : UoTex.Land(blk.Land[cell], s);
                // A static's edge where the surface steps against its neighbour is drawn dark, so roofs, walls and
                // buildings keep their outline at close zoom.
                bool edgeW = false, edgeN = false, edgeE = false, edgeS = false;
                if (blk.Covered[cell] && leafy == 0 && s >= 8) {
                    edgeW = Math.Abs(UoRadar.TopZ(x - 1, y, z) - z) >= 3; edgeN = Math.Abs(UoRadar.TopZ(x, y - 1, z) - z) >= 3;
                    edgeE = Math.Abs(UoRadar.TopZ(x + 1, y, z) - z) >= 3; edgeS = Math.Abs(UoRadar.TopZ(x, y + 1, z) - z) >= 3;
                }
                for (int j = 0; j < s; j++) for (int i = 0; i < s; i++) {
                    int c = UoRadar.Shade(t != null ? t[j * s + i] : flat, z, zNw);
                    if ((edgeW && i == 0) || (edgeN && j == 0) || (edgeE && i == s - 1) || (edgeS && j == s - 1)) c = UoRadar.Shade(c, 0, 8);
                    px[(ty * s + j) * bw + tx * s + i] = c;
                }
            }
        }
        // Canopies north to south, so a southern tree overlaps the one behind it: lit from the northwest, darker at the rim,
        // mottled per pixel so a crown reads as leaves rather than a disc.
        foreach (var c in canopies) {
            int cx = c[0], cy = c[1], r = c[2], col = c[3];
            for (int y = Math.Max(0, cy - r); y <= Math.Min(bh - 1, cy + r); y++) for (int x = Math.Max(0, cx - r); x <= Math.Min(bw - 1, cx + r); x++) {
                int dx = x - cx, dy = y - cy, d2 = dx * dx + dy * dy; if (d2 > r * r) continue;
                int hx = dx + r * 2 / 5, hy = dy + r * 2 / 5, light = 125 - (int)(55 * Math.Sqrt(hx * hx + hy * hy) / (r * 1.4 + 1));
                uint h = (uint)((x0 * s + x) * 73856093) ^ (uint)((y0 * s + y) * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                light += (int)(h % 21) - 10; if (d2 > r * r * 3 / 4) light -= 18;
                px[y * bw + x] = UoRadar.Scale(col, light);
            }
        }
        canopies.Clear();
        var data = tiles.LockBits(new Rectangle(0, 0, bw, bh), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length); tiles.UnlockBits(data);
        drawnX = ViewX; drawnY = ViewY; drawnZoom = Zoom; drawnW = tw; drawnH = th; drawnScale = Zoom / s;
    }
    protected override void OnPaint(PaintEventArgs e) {
        if (Width < 4 || Height < 4) return;
        Render();
        var gfx = e.Graphics; gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        gfx.DrawImage(tiles, new Rectangle(0, 0, tiles.Width * drawnScale, tiles.Height * drawnScale));
        var trail = Trail != null ? Trail() : new Point[0];
        if (trail.Length > 1) { var pts = new Point[trail.Length]; for (int i = 0; i < trail.Length; i++) pts[i] = ToPixel(trail[i].X, trail[i].Y); using (var pen = new Pen(Color.FromArgb(200, 255, 220, 0), Math.Max(1, Math.Min(4, Zoom / 2)))) gfx.DrawLines(pen, pts); }
        if (ShowRegions != null && ShowRegions()) {
            // A region's rectangle, as a diamond in the game's orientation; the view's tiles span ToTile of its corners.
            var c0 = ToTile(0, 0); var c1 = ToTile(Width, 0); var c2 = ToTile(0, Height); var c3 = ToTile(Width, Height);
            int x0 = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X)), x1 = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
            int y0 = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y)), y1 = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));
            using (var pen = new Pen(Color.FromArgb(160, 120, 200, 255), 1)) using (var b = new SolidBrush(Color.FromArgb(220, 160, 220, 255)))
                foreach (var r in UoRegions.InView(x0, y0, x1, y1)) {
                    var pts = new[] { ToPixel(r.X, r.Y), ToPixel(r.X + r.W, r.Y), ToPixel(r.X + r.W, r.Y + r.H), ToPixel(r.X, r.Y + r.H) };
                    gfx.DrawPolygon(pen, pts);
                    if (r.W * Half > 70) gfx.DrawString(r.Label, Font, b, pts[0].X - 20, pts[0].Y + 4);
                }
        }
        shown = Marks != null ? Marks() : new Mark[0];
        foreach (var m in shown) {
            var at = ToPixel(m.X, m.Y); int r = Math.Max(2, Math.Min(18, Math.Max(2, Math.Min(8, Zoom)) * m.Scale / 100));
            // A creature (not a human body) is its game picture, its longer side PictureBase scaled by its level, standing
            // on a ring in its type's colour; people, vendors and places stay dots.
            var pic = m.Body > 0 ? UoAnim.Sized(m.Body, Math.Max(8, Math.Min(80, Math.Max(18, Math.Min(40, Zoom * 5)) * m.Scale / 100))) : null;
            if (pic != null) {
                int pw = pic.Width, ph = pic.Height;
                using (var ring = new Pen(m.Color, 2)) gfx.DrawEllipse(ring, at.X - pw / 3, at.Y - 2, pw * 2 / 3, 5);
                gfx.DrawImageUnscaled(pic, at.X - pw / 2, at.Y - ph + 2);
                if (m.Serial == Selected) using (var pen = new Pen(Color.White, 2)) gfx.DrawRectangle(pen, at.X - pw / 2 - 2, at.Y - ph, pw + 4, ph + 4);
                continue;
            }
            using (var b = new SolidBrush(m.Color)) gfx.FillEllipse(b, at.X - r, at.Y - r, r * 2, r * 2);
            if (m.Serial == Selected) using (var pen = new Pen(Color.White, 2)) gfx.DrawEllipse(pen, at.X - r - 3, at.Y - r - 3, r * 2 + 6, r * 2 + 6);
        }
        // Custom pins (Damian, 2026-10-15: "add custom pins to the map, marking your own house, plus the houses of friends
        // and enemies alike"): a coloured pin with its label, at every zoom.
        if (Pins != null) foreach (var p in Pins()) {
            var at = ToPixel(p.X, p.Y); var col = PinColor(p.Kind);
            using (var b = new SolidBrush(col)) using (var edge = new Pen(Color.Black, 1)) {
                gfx.FillEllipse(b, at.X - 5, at.Y - 14, 10, 10); gfx.DrawEllipse(edge, at.X - 5, at.Y - 14, 10, 10);
                gfx.FillPolygon(b, new[] { new Point(at.X - 3, at.Y - 6), new Point(at.X + 3, at.Y - 6), new Point(at.X, at.Y) });
                var size = gfx.MeasureString(p.Label, Font);
                using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) gfx.FillRectangle(bg, at.X + 7, at.Y - 16, size.Width + 2, size.Height);
                gfx.DrawString(p.Label, Font, b, at.X + 8, at.Y - 16);
            }
        }
        // The session's kills: a red cross, named from 8 px a tile.
        if (Kills != null) using (var pen = new Pen(Color.Red, 2)) using (var b = new SolidBrush(Color.FromArgb(255, 200, 200))) foreach (var k in Kills()) {
            var at = ToPixel(k.X, k.Y); int h = Math.Max(2, 4 * TierScale(k.Name) / 100);
            gfx.DrawLine(pen, at.X - h, at.Y - h, at.X + h, at.Y + h); gfx.DrawLine(pen, at.X - h, at.Y + h, at.X + h, at.Y - h);
            if (Zoom >= 8) gfx.DrawString(k.Name + (k.By != "you" ? " (" + k.By + ")" : "") + (k.Loot.Length > 0 ? ": " + k.Loot : ""), Font, b, at.X + 6, at.Y - 6);
        }
        if (Corpses != null) foreach (var c in Corpses()) {
            var at = ToPixel(c.X, c.Y); int r = Math.Max(6, Math.Min(16, Zoom * 2));
            using (var pen = new Pen(Color.Magenta, 3)) { gfx.DrawEllipse(pen, at.X - r, at.Y - r, r * 2, r * 2); gfx.DrawLine(pen, at.X - r / 2, at.Y - r / 2, at.X + r / 2, at.Y + r / 2); gfx.DrawLine(pen, at.X - r / 2, at.Y + r / 2, at.X + r / 2, at.Y - r / 2); }
            using (var b = new SolidBrush(Color.Magenta)) gfx.DrawString("corpse " + c.At.ToString("HH:mm"), Font, b, at.X + r + 2, at.Y - 7);
        }
        var me = ToPixel(CenterX, CenterY); int s = Math.Max(8, Math.Min(24, Zoom * 3));
        // The course to a healer while dead: a dashed line from the player to each, labelled with its distance.
        if (Course != null) foreach (var w in Course()) {
            var to = ToPixel(w.X, w.Y); int d = Math.Max(Math.Abs(w.X - CenterX), Math.Abs(w.Y - CenterY));
            using (var pen = new Pen(Color.LimeGreen, 3) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) gfx.DrawLine(pen, me, to);
            using (var b = new SolidBrush(Color.LimeGreen)) { gfx.FillEllipse(b, to.X - 6, to.Y - 6, 12, 12); gfx.DrawString(w.Label + ": " + d + " tiles " + AssistFeatures.Heading(w.X - CenterX, w.Y - CenterY), Font, b, to.X + 8, to.Y - 7); }
        }
        using (var pen = new Pen(Color.Cyan, 3)) { gfx.DrawLine(pen, me.X - s, me.Y, me.X + s, me.Y); gfx.DrawLine(pen, me.X, me.Y - s, me.X, me.Y + s); }
        // The player's own coordinates (Damian, 2026-10-15: "put chararcter current coords on the map view").
        string where = Caption != null ? Caption() : null;
        if (!string.IsNullOrEmpty(where)) {
            var size = gfx.MeasureString(where, Font);
            using (var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) gfx.FillRectangle(bg, 2, 2, size.Width + 8, size.Height + 4);
            gfx.DrawString(where, Font, Brushes.Cyan, 6, 4);
        }
    }
}

public static class UoMap {
    const int BlocksTall = 512;
    static string F(string n) { return System.IO.Path.Combine(UoArt.Folder, n); }
    static byte[] Read(string file, long at, int len) {
        using (var fs = System.IO.File.OpenRead(F(file))) {
            if (at < 0 || at + len > fs.Length) return null;
            var b = new byte[len]; fs.Position = at; int got = 0; while (got < len) { int n = fs.Read(b, got, len - got); if (n <= 0) return null; got += n; } return b;
        }
    }
    static string LandName(int id) {
        try { var b = Read("TILEDATA.MUL", (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26 + 6, 20); if (b == null) return id.ToString("X4"); int n = 0; while (n < 20 && b[n] != 0) n++; string s = System.Text.Encoding.ASCII.GetString(b, 0, n).Trim(); return s.Length > 0 ? s : id.ToString("X4"); }
        catch { return id.ToString("X4"); }
    }
    // "ground (z), static (z), ..." for the tile, or null when the files are not readable.
    public static string Tile(int x, int y) {
        if (UoArt.Folder == null || x < 0 || y < 0) return null;
        try {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3);
            var cell = Read("MAP0.MUL", block * 196 + 4 + ((y & 7) * 8 + (x & 7)) * 3, 3); if (cell == null) return null;
            var parts = new List<string> { "ground " + LandName(BitConverter.ToUInt16(cell, 0)) + " (z " + (sbyte)cell[2] + ")" };
            var idx = Read("STAIDX0.MUL", block * 12, 12);
            if (idx != null) {
                int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
                if (off >= 0 && len > 0 && len < 1 << 20) {
                    var st = Read("STATICS0.MUL", off, len);
                    if (st != null) for (int i = 0; i + 7 <= st.Length; i += 7)
                        if (st[i + 2] == (x & 7) && st[i + 3] == (y & 7)) parts.Add(UoTiles.Name(BitConverter.ToUInt16(st, i)) + " (z " + (sbyte)st[i + 4] + ")");
                }
            }
            return string.Join(", ", parts);
        } catch { return null; }
    }
    // A tile's statics, graphic and height each, from blocks read once and kept (at most 4096, then read again).
    static readonly Dictionary<long, byte[]> staticBlocks = new Dictionary<long, byte[]>();
    public static List<int[]> StaticsAt(int x, int y) {
        var l = new List<int[]>(); if (UoArt.Folder == null || x < 0 || y < 0) return l;
        long block = (long)(x >> 3) * BlocksTall + (y >> 3); byte[] st;
        lock (staticBlocks) {
            if (!staticBlocks.TryGetValue(block, out st)) {
                st = new byte[0];
                try {
                    var idx = Read("STAIDX0.MUL", block * 12, 12);
                    if (idx != null) { int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4); if (off >= 0 && len > 0 && len < 1 << 20) st = Read("STATICS0.MUL", off, len) ?? new byte[0]; }
                } catch { }
                if (staticBlocks.Count > 4096) staticBlocks.Clear();
                staticBlocks[block] = st;
            }
        }
        for (int i = 0; i + 7 <= st.Length; i += 7) if (st[i + 2] == (x & 7) && st[i + 3] == (y & 7)) l.Add(new[] { (int)BitConverter.ToUInt16(st, i), (sbyte)st[i + 4] });
        return l;
    }
    // Whether a tile's land is impassable (water, rock: TILEDATA land flags 0x40), from MAP0's land id; false when unread.
    static uint[] landFlags;
    public static bool LandBlocks(int x, int y) {
        if (UoArt.Folder == null || x < 0 || y < 0) return false;
        try {
            if (landFlags == null) { var td = System.IO.File.ReadAllBytes(F("TILEDATA.MUL")); var f = new uint[0x4000]; for (int id = 0; id < 0x4000; id++) { int at = (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26; if (at + 4 > td.Length) break; f[id] = BitConverter.ToUInt32(td, at); } landFlags = f; }
            long block = (long)(x >> 3) * BlocksTall + (y >> 3); byte[] b;
            lock (mapBlocks) {
                if (!mapBlocks.TryGetValue(block, out b)) { b = Read("MAP0.MUL", block * 196, 196); if (mapBlocks.Count > 4096) mapBlocks.Clear(); mapBlocks[block] = b; }
            }
            if (b == null) return false;
            int cell = 4 + ((y & 7) * 8 + (x & 7)) * 3;
            return (landFlags[BitConverter.ToUInt16(b, cell) & 0x3FFF] & 0x40) != 0;
        } catch { return false; }
    }
    // MAP0 blocks (a 4-byte header, then 64 cells of land id u16 and z s8), read once and kept (at most 4096).
    static readonly Dictionary<long, byte[]> mapBlocks = new Dictionary<long, byte[]>();
    // The land height at a tile, int.MinValue when the map is not readable.
    // From the kept MAP0 blocks: a file read per call made each route plan take up to 1.3 s (the standing height reads 4
    // corners for every tile of the plan's box), and the walk stalled that long every 8 steps (2026-10-17).
    public static int LandZ(int x, int y) {
        if (UoArt.Folder == null || x < 0 || y < 0) return int.MinValue;
        try {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3); byte[] b;
            lock (mapBlocks) { if (!mapBlocks.TryGetValue(block, out b)) { b = Read("MAP0.MUL", block * 196, 196); if (mapBlocks.Count > 4096) mapBlocks.Clear(); mapBlocks[block] = b; } }
            return b == null ? int.MinValue : (sbyte)b[4 + ((y & 7) * 8 + (x & 7)) * 3 + 2];
        } catch { return int.MinValue; }
    }
    // The land id at a tile (MAP0's cell), -1 when the map is not readable.
    public static int LandIdAt(int x, int y) {
        if (UoArt.Folder == null || x < 0 || y < 0) return -1;
        try {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3); byte[] b;
            lock (mapBlocks) { if (!mapBlocks.TryGetValue(block, out b)) { b = Read("MAP0.MUL", block * 196, 196); if (mapBlocks.Count > 4096) mapBlocks.Clear(); mapBlocks[block] = b; } }
            return b == null ? -1 : BitConverter.ToUInt16(b, 4 + ((y & 7) * 8 + (x & 7)) * 3) & 0x3FFF;
        } catch { return -1; }
    }
    // The height a walker at fromZ stands at on a tile: the highest floor (passable land, or a static with the 1.25
    // TILEDATA surface flag 0x200 that is not impassable 0x40, its top half height when a bridge 0x400, as stairs are)
    // no more than StepUp above fromZ with PersonHeight clear above it; int.MinValue when there is none or the map is
    // not readable. A step only updates x and y on the wire (0x22), so the walker's z comes from here.
    // Land is stood on at the average of its corners (RunUO's GetAverageZ: the mean of the flatter diagonal), and a slope
    // climbs up to LandStepUp a tile; uo1998's Britain rises 0 to 20 east of x 1488 with cells 10 apart.
    public const int StepUp = 5, LandStepUp = 10, PersonHeight = 16;
    public static int LandStandZ(int x, int y) {
        int top = LandZ(x, y), left = LandZ(x, y + 1), right = LandZ(x + 1, y), bottom = LandZ(x + 1, y + 1);
        if (top == int.MinValue || left == int.MinValue || right == int.MinValue || bottom == int.MinValue) return top;
        return System.Math.Abs(top - bottom) > System.Math.Abs(left - right) ? (int)System.Math.Floor((left + right) / 2.0) : (int)System.Math.Floor((top + bottom) / 2.0);
    }
    public static int StandZ(int x, int y, int fromZ) {
        int land = LandStandZ(x, y); if (land == int.MinValue) return int.MinValue;
        var st = StaticsAt(x, y); var items = UoTiles.Items();
        var floors = new List<int>(); if (!LandBlocks(x, y) && land <= fromZ + LandStepUp) floors.Add(land);
        foreach (var s in st) {
            if (s[0] < 0 || s[0] >= items.Count) continue; var t = items[s[0]];
            if ((t.Flags & 0x200) == 0 || (t.Flags & 0x40) != 0) continue;
            int f = s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height);
            if (f <= fromZ + StepUp) floors.Add(f);
        }
        int best = int.MinValue;
        foreach (int f in floors) {
            if (f <= best) continue;
            bool clear = true;
            foreach (var s in st) {
                if (s[0] < 0 || s[0] >= items.Count) continue; var t = items[s[0]];
                bool solid = (t.Flags & (0x10 | 0x40)) != 0, surface = (t.Flags & 0x200) != 0;
                if (!solid && !surface) continue;
                int bottom = s[1], top = s[1] + System.Math.Max(1, t.Height);
                if ((solid ? bottom >= f : bottom > f) && bottom < f + PersonHeight && top > f) { clear = false; break; }
            }
            if (clear) best = f;
        }
        return best;
    }
    static readonly Dictionary<int, int[]> multiBox = new Dictionary<int, int[]>();
    // A multi's footprint around its center: min x, min y, max x, max y.
    public static int[] MultiBox(int id) {
        int[] box; if (multiBox.TryGetValue(id, out box)) return box;
        box = null;
        try {
            var idx = Read("MULTI.IDX", (long)id * 12, 12);
            if (idx != null) {
                int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
                if (off >= 0 && len >= 12 && len < 1 << 20) {
                    var m = Read("MULTI.MUL", off, len);
                    if (m != null) { box = new[] { int.MaxValue, int.MaxValue, int.MinValue, int.MinValue };
                        for (int i = 0; i + 12 <= m.Length; i += 12) { int cx = BitConverter.ToInt16(m, i + 2), cy = BitConverter.ToInt16(m, i + 4); box[0] = Math.Min(box[0], cx); box[1] = Math.Min(box[1], cy); box[2] = Math.Max(box[2], cx); box[3] = Math.Max(box[3], cy); } }
                }
            }
        } catch { box = null; }
        multiBox[id] = box; return box;
    }
}

// The 1998 server's regions (OUO/UoDemo regions.txt: id, x, y, width, height, z low, z high, flags, NAME, numbers,
// description), read from regions-1998.txt beside the assist's settings when present. The smallest region holding the
// player names the zone; a CITY_ or JUSTICE_ region is guarded (OUO RegionManager isInCityRegion, inJusticeRegion).
public static class UoRegions {
    sealed class R { public int X, Y, W, H, Z0, Z1; public string Name, Text; }
    static List<R> regions; static string loadedFrom;
    public static void Load(string path) {
        if (loadedFrom == path) return; loadedFrom = path; regions = null;
        if (!System.IO.File.Exists(path)) return;
        var list = new List<R>();
        foreach (var line in System.IO.File.ReadAllLines(path)) {
            var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 9) continue; int[] n = new int[7]; bool ok = true;
            for (int i = 0; i < 7 && ok; i++) ok = int.TryParse(t[i + 1], out n[i]);
            if (!ok) continue;
            list.Add(new R { X = n[0], Y = n[1], W = n[2], H = n[3], Z0 = n[4], Z1 = n[5], Name = t[8], Text = t.Length > 14 ? string.Join(" ", t, 14, t.Length - 14) : "" });
        }
        regions = list;
    }
    public static bool Loaded { get { return regions != null; } }
    // The regions overlapping a box, less the whole-map ones, for the map's region layer.
    public sealed class Box { public int X, Y, W, H; public string Label; }
    public static List<Box> InView(int x0, int y0, int x1, int y1) {
        var list = new List<Box>(); if (regions == null) return list;
        foreach (var r in regions) {
            if (r.Name.StartsWith("BRITANNIA") || r.Name.StartsWith("OCEAN") || r.X >= x1 || r.Y >= y1 || r.X + r.W <= x0 || r.Y + r.H <= y0) continue;
            list.Add(new Box { X = r.X, Y = r.Y, W = r.W, H = r.H, Label = r.Text.Length > 0 ? r.Text : r.Name });
        }
        return list;
    }
    // "City, shop" for a tile (Damian, 2026-10-15: "for the where, put the name of the city and name of shop too"): the city
    // is the smallest CITY_ or JUSTICE_ region's text, the shop the smallest other described region's ("The Hammer and
    // Anvil in Britain", its " in <city>" dropped); null with no region file or neither found.
    static R CityAndShop(int x, int y, out R city) {
        R shop = null; city = null; long ca = long.MaxValue, sa = long.MaxValue;
        foreach (var r in regions) {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || r.Text.Length == 0) continue;
            long a = (long)r.W * r.H;
            if (r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE")) { if (a < ca) { ca = a; city = r; } }
            else if (!r.Name.StartsWith("BRITANNIA") && !r.Name.StartsWith("OCEAN") && a < sa) { sa = a; shop = r; }
        }
        return shop;
    }
    // The shop's trade from its region name (BLACKSMITH_BRITAIN_1 is "blacksmith"); null outside every shop region.
    public static string ShopKind(int x, int y) {
        if (regions == null) return null; R city; var shop = CityAndShop(x, y, out city);
        return shop == null ? null : shop.Name.Split('_')[0].ToLowerInvariant();
    }
    public static string City(int x, int y) { if (regions == null) return null; R city; CityAndShop(x, y, out city); return city == null ? null : city.Text; }
    // The centre of the city's bank nearest x, y (a BANK_ region inside it), or null: where a trip banks its gold. The
    // nearest, not the smallest: Britain's east bank region is the smaller, and a deposit by the First Bank walked east.
    // The shops of a kind in a city (regions whose name starts with one of the prefixes, joined by their description,
    // "Adventurer's Clothing in Trinsic" being a WEAVER_ and a CLOTHIER_ region): description, box, centre.
    public sealed class Shop { public string Text; public int X, Y, W, H; public int CX { get { return X + W / 2; } } public int CY { get { return Y + H / 2; } } public string Key { get { return Text + " @" + CX + "," + CY; } } public bool Holds(int x, int y, int pad) { return x >= X - pad && y >= Y - pad && x < X + W + pad && y < Y + H + pad; } }
    const int ShopJoin = 16;
    public static List<Shop> ShopsIn(string city, string[] prefixes) {
        var l = new List<Shop>(); if (regions == null || city == null) return l;
        foreach (var r in regions) {
            bool kind = false; foreach (var p in prefixes) if (r.Name.StartsWith(p)) kind = true;
            if (!kind || City(r.X + r.W / 2, r.Y + r.H / 2) != city) continue;
            // The same description far apart is two shops (Jhelom's and Vesper's Adventurer's Needle and Spinning Wheel).
            string text = r.Text.Length > 0 ? r.Text : r.Name; var s = l.Find(t => t.Text == text && r.X < t.X + t.W + ShopJoin && t.X < r.X + r.W + ShopJoin && r.Y < t.Y + t.H + ShopJoin && t.Y < r.Y + r.H + ShopJoin);
            if (s == null) l.Add(new Shop { Text = text, X = r.X, Y = r.Y, W = r.W, H = r.H });
            else { int x1 = Math.Max(s.X + s.W, r.X + r.W), y1 = Math.Max(s.Y + s.H, r.Y + r.H); s.X = Math.Min(s.X, r.X); s.Y = Math.Min(s.Y, r.Y); s.W = x1 - s.X; s.H = y1 - s.Y; }
        }
        return l;
    }
    // Every shop in the city, of any trade: the named regions inside it that are not the city, a bank, the open land or
    // the sea.
    public static List<Shop> AllShopsIn(string city) {
        var kinds = new List<string>(); if (regions == null || city == null) return new List<Shop>();
        foreach (var r in regions) {
            if (r.Text.Length == 0 || r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE") || r.Name.StartsWith("BRITANNIA") || r.Name.StartsWith("OCEAN") || r.Name.StartsWith("BANK")) continue;
            if (City(r.X + r.W / 2, r.Y + r.H / 2) != city) continue;
            string kind = r.Name.Split('_')[0] + "_"; if (!kinds.Contains(kind)) kinds.Add(kind);
        }
        return ShopsIn(city, kinds.ToArray());
    }
    // Every city the region file names.
    // Not a moongate, a named spot ("docks in Moonglow", lower-case), or Wind, where no trip goes.
    public static List<string> Cities() { var l = new List<string>(); if (regions == null) return l; foreach (var r in regions) if ((r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE")) && r.Text.Length > 0 && char.IsUpper(r.Text[0]) && !r.Text.Contains("Moongate") && r.Text != "Wind" && !l.Contains(r.Text)) l.Add(r.Text); l.Sort(StringComparer.Ordinal); return l; }
    // Every city holding a shop of the kind.
    public static List<string> CitiesWith(string[] prefixes) {
        var l = new List<string>(); if (regions == null) return l;
        foreach (var r in regions) { bool kind = false; foreach (var p in prefixes) if (r.Name.StartsWith(p)) kind = true; if (!kind) continue; string c = City(r.X + r.W / 2, r.Y + r.H / 2); if (c != null && !l.Contains(c)) l.Add(c); }
        return l;
    }
    public static int[] BankIn(string city, int x, int y) { var l = BanksIn(city, x, y); return l.Count > 0 ? l[0] : null; }
    // Every bank region's centre in the city, nearest x, y first: a bank that does not answer is followed by the next
    // (Damian, 2026-10-17: "when the banking at moonglow failed, it should have tried to find another bank").
    public static List<int[]> BanksIn(string city, int x, int y) {
        var l = new List<int[]>(); if (regions == null || city == null) return l;
        foreach (var r in regions) { if (!r.Name.StartsWith("BANK")) continue; int cx = r.X + r.W / 2, cy = r.Y + r.H / 2; if (City(cx, cy) == city) l.Add(new[] { cx, cy }); }
        l.Sort((a, b) => Math.Max(Math.Abs(a[0] - x), Math.Abs(a[1] - y)).CompareTo(Math.Max(Math.Abs(b[0] - x), Math.Abs(b[1] - y))));
        return l;
    }
    public static string Where(int x, int y) {
        if (regions == null) return null;
        R city; R shop = CityAndShop(x, y, out city);
        string s = shop == null ? null : city != null && shop.Text.EndsWith(" in " + city.Text) ? shop.Text.Substring(0, shop.Text.Length - 4 - city.Text.Length) : shop.Text;
        if (city != null && s != null) return city.Text + ", " + s;
        return city != null ? city.Text : s;
    }
    // The rectangles (x, y, w, h) of the bank whose region is centred at cx, cy: every region of its name and text, as
    // Britain's First Bank is two.
    public static List<int[]> BankRects(int cx, int cy) {
        var l = new List<int[]>(); if (regions == null) return l;
        R hit = regions.Find(r => r.Name.StartsWith("BANK") && r.X + r.W / 2 == cx && r.Y + r.H / 2 == cy); if (hit == null) return l;
        foreach (var r in regions) if (r.Name == hit.Name && r.Text == hit.Text) l.Add(new[] { r.X, r.Y, r.W, r.H });
        return l;
    }
    // The 1998 server's home test (human.m: getSmallestArea at the vendor's location, z included, its name's first word
    // against the vendor's myHomeShop): the smallest region holding x, y, z, as that first word ("MAGEGUILD"); null with
    // no region file or none holding it.
    public static string AreaKey(int x, int y, int z) {
        if (regions == null) return null; R best = null; long area = long.MaxValue;
        foreach (var r in regions) { if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || z < r.Z0 || z > r.Z1) continue; long a = (long)r.W * r.H; if (a < area) { area = a; best = r; } }
        return best == null ? null : best.Name.Split('_')[0];
    }
    // The nearest region whose name's first word is key: its centre and z range, or null.
    public static int[] AreaNearest(string key, int x, int y) {
        if (regions == null || string.IsNullOrEmpty(key)) return null; int[] best = null; int bd = int.MaxValue;
        foreach (var r in regions) { if (r.Name.Split('_')[0] != key) continue; int cx = r.X + r.W / 2, cy = r.Y + r.H / 2, d = Math.Max(Math.Abs(cx - x), Math.Abs(cy - y)); if (d < bd) { bd = d; best = new[] { cx, cy, r.Z0, r.Z1 }; } }
        return best;
    }
    // The zone text and whether the map calls it guarded; null with no region file.
    public static string Zone(int x, int y, int z, out bool guarded) {
        guarded = false; if (regions == null) return null;
        R best = null; long area = long.MaxValue;
        foreach (var r in regions) {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || z < r.Z0 || z > r.Z1) continue;
            if (r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE")) guarded = true;
            long a = (long)r.W * r.H; if (r.Name.StartsWith("BRITANNIA")) a = long.MaxValue - 1;
            if (a < area) { area = a; best = r; }
        }
        if (best == null) return "outside every region";
        return best.Text.Length > 0 ? best.Text + " (" + best.Name + ")" : best.Name;
    }
}
