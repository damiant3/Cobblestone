#nullable enable
using System.Collections.Immutable;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using System.Drawing.Imaging;
using System.IO;

namespace Uoaix.Assist;

// Stage 5 of assist-plan.md: one window docked to the right edge of the client the relay serves, owned by it (so it
// sits directly above the client and minimizes with it), every feature an always-visible panel with its own hotkey on
// its own control. Hotkeys are registered only while the client is the foreground window.
public sealed partial class AssistWindow : Form
{
    // The 1.25.32 client's skill order (SKILLS.IDX), 0-based as the skill packets number them from 1.
    static readonly ImmutableArray<string> s_skillNames = [ "Alchemy", "Anatomy", "Animal Lore", "Item Identification",
        "Arms Lore", "Parrying",
        "Begging", "Blacksmithy", "Bowcraft/Fletching", "Peacemaking", "Camping", "Carpentry", "Cartography", "Cooking",
        "Detecting Hidden", "Enticement", "Evaluating Intelligence", "Healing", "Fishing", "Forensic Evaluation",
        "Herding",
        "Hiding", "Provocation", "Inscription", "Lockpicking", "Magery", "Resisting Spells", "Tactics", "Snooping",
        "Musicianship", "Poisoning", "Archery", "Spirit Speak", "Stealing", "Tailoring", "Animal Taming",
        "Taste Identification", "Tinkering", "Tracking", "Veterinary", "Swordsmanship", "Mace Fighting", "Fencing",
        "Wrestling", "Lumberjacking", "Mining" ];
    public static ImmutableArray<string> SkillNames => s_skillNames;
    public const int Width0 = 380, MinWidth = 300;

    readonly AssistRelay m_relay; readonly AssistFeatures m_f;
    IntPtr m_client = IntPtr.Zero;
    int m_clientPid; IntPtr m_locHook, m_fgHook; AssistNative.WinEventProc m_locProc = null!, m_fgProc = null!;
    bool m_keysOn; string? m_capturing = null;
    Map<string, Button> m_keyButtons = Map<string, Button>.Empty;
    Map<string, Control> m_actionControls = Map<string, Control>.Empty;
    Map<string, string> m_actionText = Map<string, string>.Empty;
    string m_lastRun = "";
    Label m_status = null!, m_session = null!, m_loopWords = null!, m_hands = null!, m_drink = null!, m_reach = null!,
        m_workState = null!, m_petsCard = null!;
    ListBox m_petHistory = null!;
    Button m_tripButton = null!; ComboBox m_loops = null!; NumericUpDown m_rounds = null!, m_untilSkill = null!;
    CheckBox m_pickup = null!;
    ListBox m_picks = null!;
    SortList m_skills = null!;
    ListBox m_notes = null!, m_chat = null!;
    TabControl m_talk = null!;
    long m_chatSeen, m_notesSeen;
    ListBox m_logList = null!;
    Label m_guardState = null!, m_bannerText;
    CheckedListBox m_watches = null!;
    Panel m_banner; FlowLayoutPanel m_flow, m_autoFlow = null!; TableLayoutPanel m_res = null!;
    Button m_resCount = null!;
    string m_resShape = "";
    List<Label> m_resLabels = new(); List<PictureBox> m_resPics = new(); List<int> m_resAt = new();
    const int ResIcon = 28, ResRow = 32;
    // Running lights for the loop and the circuit (Damian, 2026-10-17: "add some green and bold \"running\" indicators,
    // red for stopped, yellow for any unusual skips or unexplained failures"): yellow while an event of the last
    // AttentionMs carries one of AttentionWords; the section's header takes the light's colour, so a collapsed section
    // shows it too.
    Panel m_loopGroup = null!, m_circJobGroup = null!;
    Label m_loopLight = null!, m_circLight = null!, m_goldLine = null!;
    PictureBox m_goldPic = null!; FlowLayoutPanel m_goldRow = null!;
    const int GoldPileArt = 0xEEF, AttentionMs = 300000;
    static readonly ImmutableArray<string> s_attentionWords = [ "FAULT", "UNKNOWN REPLY", "skip", "fail", "refused",
        "did not",
        "no way", "not answer", "error" ];
    Label Light()
    {
        return new Label { AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 0, 0, 0), AutoEllipsis = true };
    }
    // The newest event of the last AttentionMs, from the circuit (Circuit, Walk, Survey, Buy) or not, that names a skip
    // or a failure; null when none does.
    string? Attention(bool circuit)
    {
        TimeSpan now = DateTime.Now.TimeOfDay;
        lock (m_f.Notes)
        {
            for (int i = m_f.Notes.Count - 1; i >= 0; i--)
            {
                string n = m_f.Notes[i];
                TimeSpan at;
                if (n.Length < 9 || !TimeSpan.TryParse(n.Substring(0, 8), out at))
                    continue;
                TimeSpan age = now - at;
                if (age < TimeSpan.Zero)
                    age += TimeSpan.FromDays(1);
                if (age.TotalMilliseconds > AttentionMs)
                    break;
                string text = n.Substring(9);
                bool mine = text.StartsWith("Circuit:") || text.StartsWith("Walk:") || text.StartsWith("Survey:")
                    || text.StartsWith("Buy:");
                // A loop's warnings are its current run's: the last run's end line (its tally names "N failed") closes
                // the scan.
                if (!circuit && text.StartsWith("Loop ") && (text.Contains(" done") || text.Contains(" stopped")))
                    break;
                if (mine != circuit && !text.StartsWith("UNKNOWN REPLY") && !text.StartsWith("FAULT"))
                    continue;
                foreach (string w in s_attentionWords)
                    if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                        return text;
            }
        }

        return null;
    }
    // A title wraps whole (Damian, 2026-10-18: "if you have to wordwrap a title, do the whole title, do not break at
    // the space between level and profession, e.g. Grandmaster Smith must wrap together"): from " the " on, every space
    // is a no-break space, so a line breaks only between the name and its title.
    static string WholeTitle(string s)
    {
        int at = s.IndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return s;
        return s.Substring(0, at + 1) + s.Substring(at + 1).Replace(' ', ' ');
    }
    // name: what runs, shown after RUNNING (Damian, 2026-10-18: "put the name of the circuit in the RUNNING with the
    // suboption if any (Sell only, tour)"). Green running, yellow running with a warning, red stopped (Damian,
    // 2026-10-19: "green for go, yellow for warn, red for stop and errors").
    // detail: the loop's count-up and step on its own bar (Damian, 2026-10-20: "for the loop count-up, put it in the
    // header, along with the status summary", "where it says in white on red/green STOPPED or RUNNING").
    // cancel: a stop asked of a running circuit that has not yet ended (Damian, 2026-10-22: "stop request should update
    // Circuit Job status from Running to Running: Cancel Requested").
    public static string LightText(bool running, string attention, string name, string detail, bool cancel)
    {
        return (running && cancel ? "RUNNING: Cancel Requested" + (string.IsNullOrEmpty(name) ? "" : " (" + name + ")")
            : running ? "RUNNING" + (string.IsNullOrEmpty(name) ? "" : " " + name) : "STOPPED")
            + (string.IsNullOrEmpty(detail) ? "" : "  " + detail) + (attention != null ? ":  " + attention : "");
    }
    void SetLight(Label light, Panel group, bool running, string attention, string? name = null, string? detail = null,
        bool cancel = false)
    {
        string text = LightText(running, attention, name!, detail!, cancel);
        bool warn = running && attention != null;
        Color back = !running ? Color.FromArgb(190, 40, 40)
            : warn ? Color.FromArgb(240, 200, 40) : Color.FromArgb(40, 150, 60), fore = warn ? Color.Black
            : Color.White;
        if (light.Text != text)
            light.Text = text;
        if (light.BackColor != back)
            light.BackColor = back;
        if (light.ForeColor != fore)
            light.ForeColor = fore;
        Button? head;
        Color hc = !running ? Color.DarkRed : warn ? Color.DarkGoldenrod : Color.Green;
        if (group != null && m_headOf.TryGet(group, out head) && head.ForeColor != hc)
            head.ForeColor = hc;
    }
    bool LoopRunning()
    {
        string s = m_f.Status;
        return s.StartsWith("Loop ") && !s.Contains(" done") && !s.Contains(" stopped");
    }
    // The client's own title ends where TitleMark starts; what follows is the assist's.
    const string TitleMark = "  |  ";
    static readonly ImmutableArray<string> s_actionGroups = [ "Loops", "Watch me sell", "All Timings and Delays",
        "Arm and disarm",
        "Item pickup", "Mill, spin and weave", "Record me", "Bunny hop", "General" ];
    Map<Panel, string> m_pageOf = Map<Panel, string>.Empty;
    string m_page;
    Button m_infoTab, m_skillsTab, m_actionsTab, m_mapTab, m_shopsTab, m_vendorsTab, m_pricerTab, m_housesTab,
        m_makerTab;
    Label m_underflow; bool m_fitting;
    MapView m_mapView = null!; Label m_mapInfo = null!; readonly List<Point> m_trail = new();
    const int TrailKeep = 2000;
    NumericUpDown m_delayBox = null!; CheckBox m_autoPace = null!; bool m_settingDelay; Label m_summary = null!;
    void ShowPage(string p)
    {
        m_page = p;
        m_f.Cfg.Set("window.page", p);
        foreach (KeyValuePair<Panel, string> kv in m_pageOf)
            kv.Key.Visible = kv.Value == p;
        if (p == "map.trip")
            FillTrips();
        else if (p == "map.areas")
            FillAreas();
        Fit();
    }
    bool OnPage(Control g)
    {
        string? p;
        return m_pageOf.TryGet((Panel)g, out p) && p == m_page;
    }
    static readonly ImmutableArray<string> s_actions = [ "loop", "arm", "pickup", "work", "arm.weapon", "arm.shield",
        "record",
        "loop.last", "hop", "hop.target" ];
    // Record has no key (Damian, 2026-10-21: "de-hotkey the record feature. its basically annoying"); a key given it by
    // its key button still works. Neither Bunny hop has one either.
    static readonly ImmutableArray<Keys> s_defaultKeys = [ Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7,
        Keys.None, Keys.F9,
        Keys.None, Keys.None ];

    public static void Start(AssistRelay relay)
    {
        // A fault in the window is logged and the window goes on: unhandled, it took the relay and the player's session
        // down with no word in the log (2026-10-15 20:41:36).
        Thread t = new(() =>
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => relay.Features.Fault("window: " + e.Exception);
            Application.EnableVisualStyles();
            Application.Run(new AssistWindow(relay));
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    // The assist never takes focus from the client.
    protected override bool ShowWithoutActivation => true;

    AssistWindow(AssistRelay relay)
    {
        m_relay = relay;
        m_f = relay.Features;
        Text = "UOAIX Assist";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, Width0, 600);
        KeyPreview = true;
        Font = new Font("Segoe UI", 8.5f);
        // The border (Damian, 2026-10-15: "when a loop is running, put a green border around the whole window ... 2
        // pixel wide ... painted background gray to start so it doesn't relayout ... if it stops for failsafe, make it
        // red"): the form's 2 px padding shows the form's colour; the panels inside keep the control gray.
        // 3 px, and yellow while round zero configures the loop (Damian, 2026-10-15: "make the colored border 3 pixels,
        // not 2. and add yellow to indicate \"configuring\" first step").
        Padding = new Padding(3);
        BackColor = SystemColors.Control;
        m_flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = false, BackColor = SystemColors.Control };
        Controls.Add(m_flow);
        // The fail-safe banner: shown while the assist is disabled, with the reason and a resume.
        m_banner = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(200, 40, 40),
            Visible = false };
        m_bannerText = new Label { Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Padding = new Padding(4) };
        Button resume = new()
        {
            Dock = DockStyle.Right,
            Width = 80,
            Text = "Resume",
            BackColor = Color.White
        };
        resume.Click += (s, e) => { if (AssistGuard.Current != null) AssistGuard.Current.Resume(); };
        m_banner.Controls.Add(m_bannerText);
        m_banner.Controls.Add(resume);
        // Two pages (Damian, 2026-10-15: "if we are in an under-flow condition ... we need a way to indicate that that
        // doesn't scroll. like maybe the whole main panel needs a mode/tab for putting the info in one place and the
        // actions in the other"): Info and Actions tabs, and a bar at the bottom while a page runs past the window.
        m_page = m_f.Cfg.Get("window.page", "info");
        if (m_page != "skills" && m_page != "actions" && m_page != "map" && m_page != "map.trip"
            && m_page != "map.areas" && m_page != "shops" && m_page != "vendors" && m_page != "pricer"
            && m_page != "houses" && m_page != "circuit" && m_page != "maker")
            m_page = "info";
        // The tabs size to their text and wrap to a second row (Damian, 2026-10-18: "the buttons at the top of the ui
        // should be padding reduced for the text inside, to fit all the buttons on screen. the reset is hidden here").
        FlowLayoutPanel pageTabs = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            BackColor = SystemColors.Control,
            Padding = new Padding(3, 2, 0, 2)
        };
        Func<string, Button> tab = t =>
            new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 26), Padding = new Padding(4, 0, 4, 0), Margin = new Padding(2),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
        // Skills on a tab of its own (Damian, 2026-10-18: "we should make skills its own tab entirely").
        m_infoTab = tab("Info");
        m_skillsTab = tab("Skills");
        m_skillsTab.Click += (s, e) => ShowPage("skills");
        m_actionsTab = tab("Actions");
        m_mapTab = tab("Map");
        m_shopsTab = tab("Shops");
        m_pricerTab = tab("Bags");
        m_infoTab.Click += (s, e) => ShowPage("info");
        m_actionsTab.Click += (s, e) => ShowPage("actions");
        m_mapTab.Click += (s, e) => ShowPage("map");
        m_shopsTab.Click += (s, e) => { ShowPage("shops"); m_shopShape = ""; FillShops(); };
        m_pricerTab.Click += (s, e) => { ShowPage("pricer"); FillPricer(); };
        // Other players' vendors apart from the shopkeepers (Damian, 2026-10-24: "we need another distinction there in
        // the UI for Vendors. Vendors are player controlled, not shopkeeps").
        m_vendorsTab = tab("Vendors");
        m_vendorsTab.Click += (s, e) => { ShowPage("vendors"); m_vendorsShape = ""; FillVendors(); };
        // Houses apart from the vendor (Damian, 2026-10-21: "vendor is linked to a house but they do different
        // things").
        m_housesTab = tab("Houses");
        m_housesTab.Click += (s, e) => { ShowPage("houses"); FillHouses(); FillVendor(); };
        // "Circuits" (Damian, 2026-10-18: "change tab name Circuit to Circuits"); Maker holds the crafts' value tables
        // (Damian, 2026-10-18: "move smithing and tailoring values to a new top level tab called Maker").
        m_circuitTab = tab("Circuits");
        m_makerTab = tab("Maker");
        m_makerTab.Click += (s, e) => { ShowPage("maker"); m_smithShape = ""; m_tailorShape = ""; m_craftShape = ""; FillSmith(); FillTailor(); FillCraft(); };
        m_circuitTab.Click += (s, e) => { ShowPage("circuit"); m_circShape = ""; m_circMemo = ""; FillCircuit(); };
        // The order (Damian, 2026-10-18): "make map the second in the ordering of tabs, then Skills, Maker, Actions,
        // Circuits, Shops, Bags".
        foreach (Button b in new[] { m_infoTab, m_mapTab, m_skillsTab, m_makerTab, m_actionsTab, m_circuitTab,
            m_shopsTab, m_vendorsTab, m_pricerTab, m_housesTab })
            pageTabs.Controls.Add(b);
        m_underflow = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            Visible = false,
            BackColor = Color.FromArgb(240, 190, 60),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "More below the window's edge (this window does not scroll): collapse a section or switch tabs"
        };
        Controls.Add(pageTabs);
        Controls.Add(m_underflow);
        Controls.Add(m_banner);
        m_flow.BringToFront();
        m_flow.SizeChanged += (s, e) => Fit();
        BuildStatus();
        BuildActions();
        BuildResources();
        BuildSkills();
        BuildChat();
        BuildGeneral();
        BuildMap();
        BuildShops();
        BuildVendors();
        BuildMaker();
        BuildBags();
        BuildHouses();
        BuildCircuits();

        // The gear menu at the tab row's right (Damian, 2026-10-24: "a menu to the tab selection row, right anchored
        // gear icon. click it for a menu of options, the first is Reset Layout. the other will be the general settings
        // for Circuits, and move the general card out of actions into a popup linked in that new menu").
        ContextMenuStrip gearMenu = new();
        gearMenu.Items.Add("Reset layout", null, (s, e) => ResetLayout());
        gearMenu.Items.Add("General settings...", null, (s, e) => ShowPopup(m_generalGroup!));
        gearMenu.Items.Add("Circuit general settings...", null, (s, e) => ShowPopup(m_circGeneralGroup!));
        m_gear = new Button { Text = "⚙", Font = new Font("Segoe UI Symbol", 11f), Size = new Size(30, 26),
            FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right, TabStop = false };
        m_tipper.SetToolTip(m_gear, "Reset layout, the general settings and the circuits' general settings.");
        m_gear.Click += (s, e) => gearMenu.Show(m_gear, new Point(m_gear.Width - gearMenu.Width, m_gear.Height));
        Controls.Add(m_gear);
        m_gear.Location = new Point(ClientSize.Width - m_gear.Width - 4, 4);
        m_gear.BringToFront();
        pageTabs.Padding = new Padding(pageTabs.Padding.Left, pageTabs.Padding.Top, m_gear.Width + 8,
            pageTabs.Padding.Bottom);
        Popup(m_generalGroup!);
        Popup(m_circGeneralGroup!);

        KeyDown += OnKeyCapture;
        Resize += (s, e) => Fit();
        System.Windows.Forms.Timer timer = new()
        {
            Interval = 500
        };
        timer.Tick += (s, e) => Tick();
        timer.Start();
        Fit();
    }
    Button m_gear; Panel m_generalGroup = null!, m_circGeneralGroup = null!;
    Map<Panel, Form> m_popups = Map<Panel, Form>.Empty;
    // A section taken off its page into a window of its own, shown by the gear menu; closing it hides it.
    void Popup(Panel g)
    {
        m_flow.Controls.Remove(g);
        m_pageOf = m_pageOf.Remove(g);
        g.Visible = true;
        Form form = new()
        {
            Text = m_titleOf[g]!,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(620, 560),
            AutoScroll = true,
            BackColor = SystemColors.Control
        };
        form.Controls.Add(g);
        g.Location = new Point(0, 0);
        form.FormClosing += (s, e) => { if (e.CloseReason
            == CloseReason.UserClosing) { e.Cancel = true; form.Hide(); } };
        form.Resize += (s, e) => MeasureOne(g, form.ClientSize.Width - 4);
        m_popups = m_popups.Set(g, form);
    }
    void ShowPopup(Panel g)
    {
        Form? form;
        if (g == null || !m_popups.TryGet(g, out form))
            return;
        if (!form.Visible)
        {
            Point at = m_gear.PointToScreen(new Point(m_gear.Width, m_gear.Height));
            form.Location = new Point(Math.Max(0, at.X - form.Width), at.Y);
            OnScreen(form);
            form.Show(this);
        }
        MeasureOne(g, form.ClientSize.Width - 4);
        form.Activate();
    }

    readonly List<Action> m_expanders = new();
    // Back to the working layout (Damian, 2026-10-15: "put a reset default screen orientation button somehwere in case
    // i dork with it i can easily get back to working right"): the client sized and placed again with this window
    // docked, every section and resource group open, the map at its default zoom on the player, and the Info page.
    void ResetLayout()
    {
        foreach (Action a in m_expanders)
            a();
        foreach (string g in AssistFeatures.ResourceGroups)
            m_f.Cfg.Set("collapsed.resources." + g, "0");
        m_resShape = "";
        m_mapView.Zoom = 2;
        m_mapView.OffX = m_mapView.OffY = 0;
        m_f.Cfg.Set("map.zoom", "2");
        m_mapView.Invalidate();
        if (m_client != IntPtr.Zero)
        {
            RightSize();
            Follow();
        }
        ShowPage("info");
    }
    // A section: a header that collapses and expands it (remembered in the settings), over its controls.
    Panel Group(string title)
    {
        bool closed = m_f.Cfg.Get("collapsed." + title, "0") == "1";
        Panel box = new()
        {
            AutoSize = false,
            Margin = new Padding(3, 1, 3, 1)
        };
        string page = title == "Map" ? "map"
            : title == "Trip report" ? "map.trip"
            : title == "Area analysis" ? "map.areas"
            : title == "Shopkeepers and merchants" ? "shops"
            : title == "Player vendors" ? "vendors"
            : title == "Smithing value" || title == "Tailoring value" || title == "Other crafts value" ? "maker"
            : title == "Bags" || title == "Bag Organizer" || title == "Reagent loadout" || title == "Bag pricer"
            ? "pricer"
            : title == "Houses" || title.StartsWith("House door") || title == "No house registered"
            || title == "House vendor" ? "houses"
            : title.StartsWith("Circuit ") ? "circuit"
            : title == "Skills" ? "skills" : s_actionGroups.IndexOf(title) >= 0 ? "actions" : "info";
        m_pageOf = m_pageOf.Set(box, page);
        box.Visible = page == m_page;
        Button head = new()
        {
            Text = (closed ? "[+] " : "[-] ") + title,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Height = 22,
            Dock = DockStyle.Top,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true
        };
        head.FlatAppearance.BorderSize = 0;
        FlowLayoutPanel inner = new()
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Visible = !closed,
            Location = new Point(6, 22)
        };
        m_titleOf = m_titleOf.Set(box, title);
        m_headOf = m_headOf.Set(box, head);
        head.Click += (s, e) => { inner.Visible = !inner.Visible; Control? sc; if (m_summaryBox.TryGet(box,
            out sc)) sc.Visible = !inner.Visible; m_f.Cfg.Set("collapsed." + title,
            inner.Visible ? "0" : "1"); Heads(); Fit(); };
        m_expanders.Add(() => { inner.Visible = true; Control? sc; if (m_summaryBox.TryGet(box,
            out sc)) sc.Visible = false; m_f.Cfg.Set("collapsed." + title, "0"); Heads(); });
        box.Controls.Add(inner);
        box.Controls.Add(head);
        box.Tag = inner;
        m_flow.Controls.Add(box);
        return box;
    }
    // A collapsed section shows a one-line summary on its header (Damian, 2026-10-17: "for status and session expandos,
    // when contracted, show a 1 line summary. in fact, do that for all where it makes sense"), or a row of its own
    // under it (Resources' reagent icons).
    Map<Panel, string> m_titleOf = Map<Panel, string>.Empty; Map<Panel, Button> m_headOf = Map<Panel, Button>.Empty;
    Map<Panel, Func<string>> m_summaryOf = Map<Panel, Func<string>>.Empty;
    Map<Panel, Control> m_summaryBox = Map<Panel, Control>.Empty;
    void Summarize(Panel g, Func<string> line)
    {
        m_summaryOf = m_summaryOf.Set(g, line);
    }
    // A section closed from code, as its header's click closes it.
    void Collapse(Panel box)
    {
        FlowLayoutPanel? inner = ((FlowLayoutPanel?)box.Tag)!;
        if (!inner!.Visible)
            return;
        inner.Visible = false;
        Control? sc;
        if (m_summaryBox.TryGet(box, out sc))
            sc.Visible = true;
        m_f.Cfg.Set("collapsed." + m_titleOf[box], "1");
        Heads();
        Fit();
    }
    // Pets and mount closed at each login with no mount ridden and no pet in view (Damian, 2026-10-18: "collapse pets
    // and mount at startup if the character is not mounted and has no followers"), PetsSettleMs after the login so the
    // mount and the pets near have been drawn.
    Panel m_petGroup = null!;
    DateTime m_petsCheckedFor = DateTime.MinValue; const int PetsSettleMs = 5000; bool m_pickingLoop;
    // Mill, spin and weave closed at each login when the pack holds none of its materials (Damian, 2026-10-18: "mill
    // spin and weave should collapse on startup if there is no materials relevant to the tasks").
    // Decided only once the pack has been read: unread it looks empty, and the section closed with thread in the pack
    // (Damian, 2026-10-20: "i started the session with thread, and the mill, spin and weave exando started closed").
    Panel m_workGroup = null!; DateTime m_workCheckedFor = DateTime.MinValue;
    void SummaryRow(Panel g, Control row)
    {
        row.Location = new Point(6, 22);
        row.Visible = !(((FlowLayoutPanel?)g.Tag)!).Visible;
        g.Controls.Add(row);
        m_summaryBox = m_summaryBox.Set(g, row);
    }
    void Heads()
    {
        foreach (KeyValuePair<Panel, Button> kv in m_headOf)
        {
            bool open = (((FlowLayoutPanel?)kv.Key.Tag)!).Visible;
            Func<string>? line;
            string? s = null;
            if (!open && m_summaryOf.TryGet(kv.Key, out line))
            {
                try
                {
                    s = line();
                }
                catch { s = null; }
            }
            string text = (open ? "[-] " : "[+] ") + m_titleOf[kv.Key]
                + (string.IsNullOrEmpty(s) ? "" : ":  " + s.Replace("\n", "  ").Replace("&", "&&"));
            if (kv.Value.Text != text)
                kv.Value.Text = text;
        }
    }
    static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        int n = s.IndexOf('\n');
        return n < 0 ? s : s.Substring(0, n);
    }
    T Add<T>(Panel g, T c) where T : Control
    {
        (((FlowLayoutPanel?)g.Tag)!).Controls.Add(c);
        return c;
    }
    // A section's grid of controls (FieldGrid), sized by Measure.
    FieldGrid Grid(Panel g)
    {
        return Add(g, new FieldGrid(m_tipper, Fit));
    }
    // Every number box is one width, so a grid's boxes line up; its value kept by set, clamped into min..max when
    // shown.
    const int NumW = 70;
    NumericUpDown Num(int min, int max, int value, Action<int> set, int step = 1)
    {
        NumericUpDown n = new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            Width = NumW,
            Value = Math.Max(min, Math.Min(max, value))
        };
        n.ValueChanged += (s, e) => set((int)n.Value);
        return n;
    }
    static Button Btn(string text, Action? click = null)
    {
        Button b = new()
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(40, 0),
            Padding = new Padding(4, 0, 4, 0)
        };
        if (click != null)
            b.Click += (s, e) => click();
        return b;
    }
    ComboBox Pick(int width, params object[] items)
    {
        ComboBox c = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width
        };
        c.Items.AddRange(items);
        Hold(c);
        return c;
    }
    // Controls side by side, one control of a grid's line: buttons, or a box with its buttons. Words in it sit on the
    // buttons' text line.
    static FlowLayoutPanel Strip(params Control[] cs)
    {
        FlowLayoutPanel p = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (Control c in cs)
        {
            c.Margin = new Padding(0, c is Label ? 6 : c is CheckBox ? 4 : 0, 4, 0);
            p.Controls.Add(c);
        }
        return p;
    }
    static Label Words(string text)
    {
        return new Label { Text = text, AutoSize = true };
    }
    // An action's control carries its hotkey; the key button beside it shows the key, and a click then a key press
    // changes it. The action and its key are one slot of the grid, so the keys of a section stand in one column.
    void AddAction(string name, FieldGrid grid, string text, Action run, Control? own = null)
    {
        Control c = own ?? new Button { AutoSize = true };
        if (own == null)
            ((Button)c).Click += (s, e) => run();
        m_actionControls = m_actionControls.Set(name, c);
        m_actionText = m_actionText.Set(name, text ?? c.Text);
        Button key = new()
        {
            Width = 90,
            Text = "key",
            FlatStyle = FlatStyle.Flat
        };
        m_tipper.SetToolTip(key, "Click, then press the key you want for this.");
        key.Click += (s, e) => { m_capturing = name; key.Text = "press a key..."; };
        grid.Pair(c, key);
        m_keyButtons = m_keyButtons.Set(name, key);
        m_runs = m_runs.Set(name, run);
        LabelKeys();
    }
    Map<string, Action> m_runs = Map<string, Action>.Empty;
    // The control port's "action <name>": the window's action of that hotkey name, on the window's thread.
    public string? RunAction(string name)
    {
        Action? run;
        if (!m_runs.TryGet(name, out run))
            return "no action named " + name + " (" + string.Join(", ", m_runs.Keys) + ")";
        BeginInvoke(run);
        return null;
    }
    Keys Key(string name)
    {
        int i = s_actions.IndexOf(name);
        return (Keys)m_f.Cfg.GetInt("key." + name, (int)s_defaultKeys[i]);
    }
    void LabelKeys()
    {
        foreach (KeyValuePair<string, Control> kv in m_actionControls)
        {
            kv.Value.Text = m_actionText[kv.Key]!;
            m_keyButtons[kv.Key]!.Text = "Key: " + Key(kv.Key);
        }
    }

    void OnKeyCapture(object? s, KeyEventArgs e)
    {
        if (m_capturing == null)
            return;
        m_f.Cfg.Set("key." + m_capturing, ((int)e.KeyCode).ToString());
        m_capturing = null;
        e.Handled = true;
        LabelKeys();
        if (m_keysOn)
        {
            HotKeys(false);
            HotKeys(true);
        }
    }

    // No window scroll bar (Damian, 2026-10-15: "all of the panels of the uoaix assist need to fit the screen with no
    // full window scroll bars in any resolution", "if it doesn't fit, just let it underflow the screen, but make it fit
    // a properly sized screen"): the skills list and the events list take what height is left, down to a floor; past
    // that the bottom runs off the window.
    const int SkillsMin = 80, NotesMin = 40, MapMin = 200, ShopsMin = 120, ShopItemsMin = 160, PricerMin = 160,
        TripMin = 160, AreaDetailMin = 120;
    void Fit()
    {
        if (m_flow == null || m_skills == null || m_talk == null || m_fitting || m_dropped > 0)
            return;
        m_fitting = true;
        try
        {
            foreach (Button b in new[] { m_infoTab, m_skillsTab, m_actionsTab, m_mapTab, m_shopsTab, m_vendorsTab,
                m_pricerTab, m_housesTab, m_makerTab, m_circuitTab })
            {
                bool on = b
                    == (m_page == "info" ? m_infoTab
                    : m_page == "skills" ? m_skillsTab
                    : m_page == "maker" ? m_makerTab
                    : m_page == "actions" ? m_actionsTab
                    : m_page.StartsWith("map") ? m_mapTab
                    : m_page == "vendors" ? m_vendorsTab
                    : m_page == "pricer" ? m_pricerTab
                    : m_page == "houses" ? m_housesTab : m_page == "circuit" ? m_circuitTab : m_shopsTab);
                b.Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular);
                b.BackColor = on ? SystemColors.ControlLightLight : SystemColors.Control;
                b.FlatAppearance.BorderColor = on ? SystemColors.Highlight : SystemColors.ControlDark;
                b.FlatAppearance.BorderSize = on ? 2 : 1;
            }
            int w = m_flow.ClientSize.Width - 8;
            m_skills.Height = SkillsMin;
            m_talk.Height = NotesMin;
            if (m_mapView != null)
                m_mapView.Height = MapMin;
            if (m_shops != null)
                m_shops.Height = ShopsMin;
            if (m_shopItems != null)
                m_shopItems.Height = ShopItemsMin;
            if (m_shopTrades != null)
                m_shopTrades.Height = ShopItemsMin;
            if (m_pricer != null)
                m_pricer.Height = PricerMin;
            if (m_vendorRows != null)
                m_vendorRows.Height = PricerMin;
            if (m_circMap != null)
                m_circMap.Height = CircMapMin;
            if (m_tripText != null)
                m_tripText.Height = TripMin;
            if (m_areaList != null)
            {
                m_areaList.Height = TripMin;
                m_areaDetail.Height = AreaDetailMin;
            }
            Measure(w);
            int spare = m_flow.ClientSize.Height - Used() - 4;
            if (m_page == "map" && m_mapView != null && spare > 0)
            {
                m_mapView.Height += spare;
                Measure(w);
            }
            if (m_page == "pricer" && m_pricer != null && spare > 0)
            {
                m_pricer.Height += spare;
                Measure(w);
            }
            if (m_page == "circuit" && m_circMap != null && m_circMap.Visible && spare > 0)
            {
                m_circMap.Height += spare;
                Measure(w);
            }
            if (m_page == "houses" && m_vendorRows != null && spare > 0)
            {
                m_vendorRows.Height += spare;
                Measure(w);
            }
            if (m_page == "map.trip" && m_tripText != null && spare > 0)
            {
                m_tripText.Height += spare;
                Measure(w);
            }
            if (m_page == "map.areas" && m_areaList != null && spare > 0)
            {
                m_areaList.Height += spare / 2;
                m_areaDetail.Height += spare - spare / 2;
                Measure(w);
            }
            if (m_page == "shops" && m_shops != null && m_shopItems != null && m_shopTrades != null && spare > 0)
            {
                m_shops.Height += spare / 3;
                m_shopItems.Height += spare / 3;
                m_shopTrades.Height += spare - 2 * (spare / 3);
                Measure(w);
            }
            bool info = m_page == "info";
            bool skillsOpen = m_page == "skills" && m_f.Cfg.Get("collapsed.Skills", "0") != "1", notesOpen = info
                && m_f.Cfg.Get("collapsed.Chat and events", "0") != "1";
            if (spare > 0 && (skillsOpen || notesOpen))
            {
                if (skillsOpen && notesOpen)
                {
                    m_skills.Height += spare * 3 / 5;
                    m_talk.Height += spare - spare * 3 / 5;
                }
                else if (skillsOpen)
                {
                    m_skills.Height += spare;
                }
                else
                {
                    m_talk.Height += spare;
                }

                Measure(w);
            }
            if (m_autoFlow != null)
                foreach (Control c in m_autoFlow.Controls)
                    c.Width = Math.Max(100, m_autoFlow.ClientSize.Width - 8);
            bool under = Used() > m_flow.ClientSize.Height;
            if (m_underflow.Visible != under)
                m_underflow.Visible = under;
        }
        finally { m_fitting = false; }
    }
    int Used()
    {
        int used = 0;
        foreach (Control g in m_flow.Controls)
            if (OnPage(g))
                used += g.Height + g.Margin.Vertical;
        return used;
    }
    Set<Control> m_growers = Set<Control>.Empty;
    static int Grown(Label l)
    {
        return string.IsNullOrEmpty(l.Text) ? 4 : l.GetPreferredSize(new Size(Math.Max(100, l.Width), 0)).Height + 4;
    }
    void Measure(int w)
    {
        foreach (Control g in m_flow.Controls)
            MeasureOne(g, w);
        foreach (KeyValuePair<Panel, Form> kv in m_popups)
            if (kv.Value.Visible)
                MeasureOne(kv.Key, kv.Value.ClientSize.Width - 4);
    }
    void MeasureOne(Control g, int w)
    {
        {
            g.Width = w;
            FlowLayoutPanel? inner = ((FlowLayoutPanel?)g.Tag)!;
            int h = 0;
            foreach (Control c in inner!.Controls)
            {
                c.Width = w - 20;
                // A wrapping text card takes the height of its text at the width just given (Damian, 2026-10-15:
                // "session card under flows the alloted space, can't read it all").
                if (m_growers.Contains(c))
                    c.Height = Grown((Label)c);
                if (c is FieldGrid)
                    c.Height = ((FieldGrid)c).Arrange(c.Width);
                if (c.Visible)
                    h += c.Height + c.Margin.Vertical;
            }
            inner.Width = w - 8;
            inner.Height = h + 4;
            Control? sc;
            bool row = m_summaryBox.TryGet((Panel)g, out sc) && sc.Visible;
            if (row)
                sc!.Width = w - 20;
            g.Height = 22 + (inner.Visible ? inner.Height : row ? sc!.Height + 2 : 0);
        }
    }
    void Note(string text)
    {
        m_notes.Items.Add(DateTime.Now.ToString("HH:mm:ss") + " " + text);
        m_notes.TopIndex = m_notes.Items.Count - 1;
    }

    // ---------- docking ----------
    public string DockToClient()
    {
        GameLink? k = m_relay.Link();
        int port = m_relay.LastClientPort;
        if (m_client == IntPtr.Zero && port != 0)
        {
            int pid = AssistNative.TcpPid(port, m_relay.ListenPort);
            if (pid != 0)
            {
                nint h = AssistNative.MainWindow(pid);
                if (h != IntPtr.Zero)
                {
                    m_client = h;
                    m_clientPid = pid;
                    RightSize();
                    Hook();
                }
            }
        }
        if (m_client == IntPtr.Zero)
        {
            int pid = port == 0 ? 0 : AssistNative.TcpPid(port, m_relay.ListenPort);
            return "not docked: client port " + port + " pid " + pid + " window "
                + (pid == 0 ? "-" : AssistNative.MainWindow(pid).ToString());
        }
        AssistNative.RECT r = AssistNative.Visible(m_client);
        AssistNative.RECT area;
        AssistNative.GetClientRect(m_client, out area);
        return "docked to pid " + m_clientPid + " client " + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom
            + " area " + area.Right + "x" + area.Bottom + " window " + Left + "," + Top + "," + Right + "," + Bottom
            + " visible " + AssistNative.IsWindowVisible(Handle) + " keys " + m_keysOn;
    }
    // The 1.25.32 client stretches its 640x480 frame over its client area, so any other size draws torn text (Damian,
    // 2026-10-15: "the graphics renders tearing of fonts and stuff if its off by a pixel"). At dock the client area is
    // set to the largest whole multiple of 640x480 that leaves this window MinWidth beside it, the visible frame at the
    // top left of the work area of the screen the client is on.
    void RightSize()
    {
        if (AssistNative.IsIconic(m_client))
            return;
        if (AssistNative.IsZoomed(m_client))
            AssistNative.ShowWindow(m_client, 9);  // SW_RESTORE: a maximized window keeps its size
        AssistNative.RECT outer, area;
        AssistNative.GetWindowRect(m_client, out outer);
        AssistNative.GetClientRect(m_client, out area);
        AssistNative.RECT seen = AssistNative.Visible(m_client);
        int frameW = outer.Right - outer.Left - area.Right, frameH = outer.Bottom - outer.Top - area.Bottom;
        int padL = seen.Left - outer.Left, padT = seen.Top - outer.Top, padR = outer.Right
            - seen.Right, padB = outer.Bottom - seen.Bottom;
        Rectangle work = Screen.FromRectangle(new Rectangle(seen.Left, seen.Top, seen.Right - seen.Left,
            seen.Bottom - seen.Top)).WorkingArea;
        int k = Math.Max(1,
            Math.Min((work.Width - MinWidth - (frameW - padL - padR)) / 640,
            (work.Height - (frameH - padT - padB)) / 480));
        AssistNative.SetWindowPos(m_client, IntPtr.Zero, work.Left - padL, work.Top - padT, 640 * k + frameW,
            480 * k + frameH, 0x0004 | 0x0010);  // SWP_NOZORDER | SWP_NOACTIVATE
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            if (m_locHook != IntPtr.Zero)
                AssistNative.UnhookWinEvent(m_locHook);
            if (m_fgHook != IntPtr.Zero)
                AssistNative.UnhookWinEvent(m_fgHook);
            HotKeys(false);
        }
        catch { }
        m_locHook = IntPtr.Zero;
        m_fgHook = IntPtr.Zero;
        m_client = IntPtr.Zero;
        base.OnFormClosed(e);
    }
    void Hook()
    {
        // No owner link: a cross-process owner attaches the two input queues, and the client's hidden cursor then
        // hides the Windows cursor over this window too (Damian, 2026-10-15). Z-order follows the foreground instead.
        // A closed window answers no hook: the foreground hook asked a disposed window for its handle and the exception
        // ended the relay (2026-10-18 21:04:52, ObjectDisposedException in Stack); OnFormClosed unhooks both.
        m_locProc = (hk, ev, h, obj, child, th,
            time) => { if (IsDisposed || Disposing) return; if (h == m_client && obj == 0) Follow(); };
        m_locHook = AssistNative.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, m_locProc, m_clientPid, 0,
            0);  // EVENT_OBJECT_LOCATIONCHANGE
        m_fgProc = (hk, ev, h, obj, child, th,
            time) => { if (IsDisposed || Disposing) return; HotKeys(h == m_client); Stack(h); };
        m_fgHook = AssistNative.SetWinEventHook(3, 3, IntPtr.Zero, m_fgProc, 0, 0,
            0);                       // EVENT_SYSTEM_FOREGROUND
        Follow();
        nint fg = AssistNative.GetForegroundWindow();
        HotKeys(fg == m_client);
        Stack(fg);
    }
    // The title-bar bars: over the client's title bar (its visible top down to its client area), left of the caption
    // buttons; owned by this window, so they stay above the client with it.
    TitleBars m_bars = null!;
    void PlaceBars(AssistNative.RECT r)
    {
        Point pt = new(0, 0);
        AssistNative.ClientToScreen(m_client, ref pt);
        int h = pt.Y - r.Top;
        if (m_bars == null)
        {
            m_bars = new TitleBars();
            m_bars.Show(this);
        }
        int bw = m_bars.FullWidth;
        if (h < 12 || r.Right - r.Left < bw + TitleBars.CaptionButtons + 100)
        {
            m_bars.Hide();
            return;
        }
        m_bars.Bounds = new Rectangle(r.Right - TitleBars.CaptionButtons - bw, r.Top + 2, bw, h - 4);
        if (!m_bars.Visible)
            m_bars.Show();
    }
    void Follow()
    {
        if (m_client == IntPtr.Zero)
            return;
        if (AssistNative.IsIconic(m_client))
        {
            if (Visible)
                Hide();
            if (m_bars != null)
                m_bars.Hide();
            return;
        }
        AssistNative.RECT r = AssistNative.Visible(m_client);
        PlaceBars(r);
        // This window's own invisible border, measured once it has a handle: place its visible frame on the client's.
        int padL = 0, padT = 0, padR = 0, padB = 0;
        if (IsHandleCreated)
        {
            AssistNative.RECT outer;
            AssistNative.GetWindowRect(Handle, out outer);
            AssistNative.RECT seen = AssistNative.Visible(Handle);
            padL = seen.Left - outer.Left;
            padT = seen.Top - outer.Top;
            padR = outer.Right - seen.Right;
            padB = outer.Bottom - seen.Bottom;
        }
        Rectangle screen = Screen.FromRectangle(new Rectangle(r.Left, r.Top, r.Right - r.Left,
            r.Bottom - r.Top)).WorkingArea;
        int w = Math.Max(MinWidth, Math.Min(Width0 * 2, screen.Right - r.Right));
        Bounds = new Rectangle(r.Right - padL, r.Top - padT, w + padL + padR, r.Bottom - r.Top + padT + padB);
        if (!Visible)
            Show();
    }
    // Directly above the client in z-order, always (Damian, 2026-10-15: "the uoaix assist window is not at the same z
    // level as the client window ... the uoclient will be on top of agentgrid, but then uoaixassist is behind
    // agentgrid"): a window that comes between them is stepped over, and clicking this window brings the client up
    // right beneath it. Run on every foreground change and every Tick, because a raise of the client sends no
    // foreground event to us.
    void Stack(IntPtr fg)
    {
        if (m_client == IntPtr.Zero || m_dropped > 0)
            return;
        const uint keep = 0x0001 | 0x0002 | 0x0010;  // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
        if (fg == Handle)
        {
            if (AboveClient() != Handle)
                AssistNative.SetWindowPos(m_client, Handle, 0, 0, 0, 0, keep);
            return;
        }
        nint above = AboveClient();
        if (above == Handle)
            return;
        if (above != IntPtr.Zero)
        {
            AssistNative.SetWindowPos(Handle, above, 0, 0, 0, 0, keep);
            return;
        }  // after `above`: between it and the client
        // The client heads the non-topmost band. Windows ignores HWND_TOP from a background process when the foreground
        // window would be passed, and the title-bar meter went under the client (Damian, 2026-10-16: "when i am in the
        // client, the overlay hits/mana/stamina meter disappears"); a topmost round trip lands at the head of the band.
        AssistNative.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, keep);  // HWND_TOPMOST
        AssistNative.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, keep);  // HWND_NOTTOPMOST
    }
    // The nearest visible non-topmost window above the client (GW_HWNDPREV), or IntPtr.Zero when none is.
    IntPtr AboveClient()
    {
        nint h = AssistNative.GetWindow(m_client, 3);
        while (h != IntPtr.Zero && h != Handle
            && (!AssistNative.IsWindowVisible(h) || (AssistNative.GetWindowLong(h, -20) & 0x8) != 0))
            h = AssistNative.GetWindow(h, 3);  // GWL_EXSTYLE, WS_EX_TOPMOST
        return h;
    }
    void HotKeys(bool on)
    {
        if (on == m_keysOn)
            return;
        for (int i = 0; i < s_actions.Length; i++)
        {
            if (on)
            {
                if (Key(s_actions[i]) != Keys.None)
                    AssistNative.RegisterHotKey(Handle, 0x4100 + i, 0, (uint)Key(s_actions[i]));
            }
            else
            {
                AssistNative.UnregisterHotKey(Handle, 0x4100 + i);
            }
        }
        m_keysOn = on;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            int i = m.WParam.ToInt32() - 0x4100;
            if (i >= 0 && i < s_actions.Length)
                m_runs[s_actions[i]]!();
            return;
        }
        base.WndProc(ref m);
    }

    // The client's title, then the display choices after TitleMark; null state puts the client's own title back.
    // Set only when it differs, so the client's title is written once per change.
    void Title(AssistState st)
    {
        string now = AssistNative.Title(m_client);
        int cut = now.IndexOf(TitleMark);
        string want = cut >= 0 ? now.Substring(0, cut) : now;
        if (st != null)
        {
            List<string> parts = new();
            lock (st.Gate)
                foreach (string n in AssistFeatures.DisplayNames)
                    if (m_f.Shown(n))
                        parts.Add(n + " " + StatValue(st, n).Replace(" / ", "/"));
            if (parts.Count > 0)
                want += TitleMark + string.Join("  ", parts);
        }
        if (now != want)
            AssistNative.SetTitle(m_client, want);
    }
    void Border()
    {
        bool halted = AssistGuard.Current != null && AssistGuard.Current.Disabled != null;
        bool looping = m_f.Status.StartsWith("Loop ") && !m_f.Status.Contains(" done")
            && !m_f.Status.Contains(" stopped");
        bool configuring = looping && m_f.Status.Contains(": round zero");
        // Black once a loop completed (Damian, 2026-10-15: "when it completes successfully, turn it black"), until the
        // next.
        bool done = m_f.Status.StartsWith("Loop ") && m_f.Status.Contains(" done");
        // Green while the reagent loadout runs, as a loop (Damian, 2026-10-22: "Reagent loadout status should turn the
        // screen green like a loop and go back to gray when done").
        Color want = halted ? Color.FromArgb(200, 40, 40)
            : configuring ? Color.FromArgb(235, 200, 40)
            : looping || m_f.LoadoutRunning || m_f.HopRunning ? Color.FromArgb(40, 170, 60)
            : done ? Color.Black : SystemColors.Control;
        if (BackColor != want)
            BackColor = want;
    }
    string m_tpHoverKey = "";
    int[][] m_tpFrom = new int[0][];
    Label m_suggestNote = null!, m_bagNote = null!, m_layoutNote = null!;
    readonly List<Tuple<string, Label>> m_bagRows = new(); DateTime m_bagsAt;
    ListBox m_mapDetail = null!, m_shopInfo = null!;
    ListView m_shopItems = null!;
    SortList m_shops = null!, m_shopTrades = null!;
    ComboBox m_shopCity = null!, m_shopKind = null!, m_shopSurvey = null!;
    TextBox m_shopFind = null!; string m_shopShape = "";
    Label m_mapRouteNote = null!;
    Button m_overflowRuneButton = null!, m_overflowPileButton = null!;
    CheckBox m_houseLockBox = null!; string m_toolbagShown = "";
    const int MapIdleTicks = 4; long m_mapSeq; int m_mapIdle; Label m_watchWords = null!; CheckBox m_watchBox = null!;
    // An open dropdown closes on a relayout or a z-order change, so neither runs while one is open (Damian, 2026-10-15:
    // "something is causing refresh of the UI so i can't make dropdown selections").
    int m_dropped;
    void Hold(ComboBox box)
    {
        box.DropDown += (s, e) => m_dropped++;
        box.DropDownClosed += (s, e) => m_dropped = Math.Max(0, m_dropped - 1);
    }
    void Tick()
    {
        if (m_dropped > 0)
            return;
        Border();
        ShowDelay();
        ShowTimings();
        ShowSounds();
        // The Bags tab every 2 s while shown: each bag's place and count; a set bag on the floor in reach opened once.
        if (m_page == "pricer" && m_bagRows.Count > 0 && (DateTime.UtcNow - m_bagsAt).TotalSeconds >= 2)
        {
            m_bagsAt = DateTime.UtcNow;
            m_f.RefreshFloorBags();
            foreach (Tuple<string, Label> r in m_bagRows)
            {
                List<uint> l = m_f.BagsOf(r.Item1);
                string t = l.Count == 0 ? "not set" : string.Join("; ", l.ConvertAll(s => m_f.BagWhere(s)));
                if (r.Item2.Text != t)
                    r.Item2.Text = t;
            }
            if (m_layoutNote != null && m_layoutNote.Text != m_f.LayoutStatus)
                m_layoutNote.Text = m_f.LayoutStatus;
            string bst = m_f.HouseVendorStatus.Length > 0 ? m_f.HouseVendorStatus : m_f.BankBagStatus;
            if (bst.Length > 0 && m_bagNote.Text.Length == 0)
                m_bagNote.Text = bst;
            if (m_f.ToolbagStatus != m_toolbagShown)
            {
                m_toolbagShown = m_f.ToolbagStatus;
                if (m_toolbagShown.Length > 0)
                    m_bagNote.Text = m_toolbagShown;
            }
        }
        if (m_housesSeen != m_f.HouseSeq)
            FillHouses();
        if (m_housesNote != null && m_f.HouseStatus.Length > 0 && m_housesNote.Text != m_f.HouseStatus)
            m_housesNote.Text = m_f.HouseStatus;
        if (m_suggestNote != null)
        {
            string sn = m_f.Suggested.Length > 0 ? "Next: " + m_f.Suggested
                : m_f.Suggest ? "waiting for the circuit's next step" : "";
            if (m_suggestNote.Text != sn)
                m_suggestNote.Text = sn;
        }
        if (m_watchWords != null)
        {
            string ww = m_f.WatchStatus.Length > 0 ? m_f.WatchStatus : "Sell one batch by hand to start it.";
            if (m_watchWords.Text != ww)
                m_watchWords.Text = ww;
        }
        if (m_tripsSeen != m_f.TripsWritten)
        {
            if (m_page == "map.trip")
                FillTrips();
            else if (m_page == "map.areas")
                FillAreas();
        }
        if (m_client != IntPtr.Zero && Visible)
            Stack(AssistNative.GetForegroundWindow());
        // A client that closed takes the dock with it; the next client to connect is docked anew.
        if (m_client != IntPtr.Zero && !AssistNative.IsWindow(m_client))
        {
            AssistNative.UnhookWinEvent(m_locHook);
            AssistNative.UnhookWinEvent(m_fgHook);
            HotKeys(false);
            m_client = IntPtr.Zero;
            m_relay.LastClientPort = 0;
            Hide();
            if (m_bars != null)
                m_bars.Hide();
        }
        if (m_client == IntPtr.Zero)
            DockToClient();
        GameLink? k = m_relay.Link();
        if (k == null)
        {
            m_status.Text = m_client == IntPtr.Zero ? "Waiting for the client to connect through the relay."
                : "Logged out.";
            if (m_client != IntPtr.Zero)
                Title(null!);
            return;
        }
        AssistState st = k.State;
        lock (st.Gate)
        {
            foreach (KeyValuePair<string, Label> kv in m_statValues)
            {
                Color c;
                string v = kv.Key + "  " + StatText(st, kv.Key, out c);
                if (kv.Value.Text != v)
                    kv.Value.Text = v;
                if (kv.Value.ForeColor != c)
                    kv.Value.ForeColor = c;
            }
            m_statLine = string.Join("   ",
                Array.ConvertAll(new[] { "hits", "mana", "stamina", "gold", "weight" },
                n => n + " " + StatValue(st, n)));
            if (m_bars != null)
            {
                m_bars.Set(st.Hits, st.MaxHits, st.Mana, st.MaxMana, st.Stam, st.MaxStam);
                if (m_bars.SetRegs(m_f.RecallRegCounts()!) && m_client != IntPtr.Zero)
                    PlaceBars(AssistNative.Visible(m_client));
            }
        }
        PetsAtLogin(st);
        m_f.ForgetGoneMultis();
        m_status.Text = WholeTitle(st.Title.Length > 0 ? st.Title : st.Name)
            + (k.Halted != null ? "  HALTED: " + k.Halted : "");
        // Tall enough for the lines the text wraps to, so a title wrapped whole onto its own line shows.
        if (m_status.Width > 0)
        {
            int need = Math.Max(36,
                TextRenderer.MeasureText(m_status.Text, m_status.Font, new Size(m_status.Width, 0),
                TextFormatFlags.WordBreak).Height + 4);
            if (m_status.Height != need)
            {
                m_status.Height = need;
                Fit();
            }
        }
        Title(st);
        Session(k);
        Summary();
        if (UoArt.Folder == null && m_clientPid != 0)
            UoArt.Folder = AssistNative.ExeFolder(m_clientPid);
        MapTick(st);
        if (m_page == "shops")
            FillShops();
        if (m_page == "vendors")
        {
            FillVendors();
            if (m_vendorsNote != null && m_f.VendorStatus.Length > 0 && m_vendorsNote.Text != m_f.VendorStatus)
                m_vendorsNote.Text = m_f.VendorStatus;
        }
        if (m_page == "maker")
        {
            FillSmith();
            FillTailor();
            FillCraft();
        }
        if (m_page == "circuit")
            FillCircuit();
        // A text card whose text needs another height lays the window out again (Damian, 2026-10-18: "the circuit job
        // card underflows": its text changed between layouts and kept the old height).
        foreach (Control c in m_growers)
            if (c.Visible && c is Label && c.Height != Grown((Label)c))
            {
                Fit();
                break;
            }
        if (m_pricerStatus != null && m_pricerStatus.Text != m_f.PricerStatus)
            m_pricerStatus.Text = m_f.PricerStatus;
        if (m_vendorNote != null && m_f.VendorStatus.Length > 0 && m_vendorNote.Text != m_f.VendorStatus)
            m_vendorNote.Text = m_f.VendorStatus;
        {
            long seq = Interlocked.Read(ref m_f.VendorSeq);
            if (seq != m_vendorSeen)
            {
                m_vendorSeen = seq;
                FillVendor();
            }
        }
        if (m_loadoutStatus != null && m_loadoutStatus.Text != m_f.LoadoutStatus)
            m_loadoutStatus.Text = m_f.LoadoutStatus;
        // The loadout's color coding: each reagent against what its bag is refilled to.
        foreach (Tuple<int, Control> row in m_loadoutRows)
        {
            if (row.Item2 == null)
                continue;
            int have = m_f.LoadoutHave(row.Item1), want = m_f.LoadoutWant(row.Item1);
            Color c = have < 0 ? Color.DimGray
                : have >= want ? Color.Green : have > 0 ? Color.DarkGoldenrod : Color.DarkRed;
            if (row.Item2.ForeColor != c)
                row.Item2.ForeColor = c;
        }
        {
            long seq = Interlocked.Read(ref m_f.PricerSeq);
            if (seq != m_pricerSeen && m_pricer != null && !m_pricer.IsCurrentCellInEditMode)
            {
                m_pricerSeen = seq;
                FillPricer();
            }
        }
        // A new buy list selects its vendor while the Shops tab is open (Damian, 2026-10-15); the game keeps the
        // keyboard.
        if (m_f.Atlas != null)
        {
            long seq = Interlocked.Read(ref m_f.Atlas.StockedSeq);
            if (seq != m_stockSeen)
            {
                m_stockSeen = seq;
                if (m_page == "shops")
                    PickShop(m_f.Atlas.Stocked);
            }
        }
        Chat(st);
        m_loopWords.Text = m_f.LoopWords;
        string loopStatus = m_f.Status.StartsWith("Loop ") ? m_f.Status.Substring(5)
            : m_f.Status == "idle" ? "" : m_f.Status;
        // Watch me sell is a loop too while it sells (Damian, 2026-10-19: "watchme sell should turn the loops stop bar
        // green and say running"); its checkbox is changed by nothing but itself.
        bool loop = LoopRunning(), watching = m_f.WatchSelling;
        if (m_watchBox != null && m_watchBox.Checked != m_f.WatchSell)
            m_watchBox.Checked = m_f.WatchSell;
        // Mill, spin and weave lights the Loops bar too, its own button kept (Damian, 2026-10-20: "i definitely want
        // the weave step to turn the loops running lights integrated so its green / red"); stopped, the bar shows
        // whichever of the two ran last.
        bool weaving = m_f.WorkRunning && !m_f.CircuitRunning;
        if (weaving)
            m_lastRun = "work";
        else if (loop)
            m_lastRun = "loop";
        // Bunny hop lights the bar too (Damian, 2026-10-23: "bunny hop needs to engage the green RUNNING state in the
        // actions tab"); stopped, the bar keeps its end line until the next run.
        bool hopping = m_f.HopRunning;
        if (hopping)
            m_lastRun = "hop";
        string? lightName = loop ? null
            : weaving ? "Mill, spin and weave" : watching ? "Watch me sell" : hopping ? "Bunny hop" : null;
        // Watch me sell shows what it is doing, and a wait turns the bar yellow (2026-10-20 11:44: green and silent for
        // 14 s while it waited on Simon "away" behind his counter).
        string lightDetail = loop ? loopStatus
            : weaving ? m_f.WorkState
            : watching ? m_f.WatchStatus
            : hopping ? m_f.HopStatus
            : m_lastRun == "work" ? "Mill, spin and weave: " + m_f.WorkEnd
            : m_lastRun == "hop" ? "Bunny hop: " + m_f.HopStatus : loopStatus;
        string? attention = Attention(false);
        if (!loop && !weaving && watching && m_f.WatchStatus.StartsWith("waiting"))
            attention = attention ?? "waiting";
        // The loop button names what it starts or stops (Damian, 2026-10-20: "Start the <one word named> loop, or Stop
        // the <one word named> loop ... Stop the weave loop if its running, or mill loop, or spin loop").
        bool lastGoing = loop && m_f.LastRun;
        string loopText = lastGoing ? "Start the " + LoopWord(Kind().Name) + " loop"
            : loop ? "Stop the " + LoopWord(m_f.Status.Length > 5 ? m_f.Status.Substring(5) : "") + " loop"
            : weaving ? "Stop the " + m_f.WorkVerb + " loop"
            : watching ? "Stop the selling loop" : "Start the " + LoopWord(Kind().Name) + " loop";
        Control loopButton = m_actionControls["loop"]!, lastButton = m_actionControls["loop.last"]!;
        if (m_actionText["loop"] != loopText)
        {
            m_actionText = m_actionText.Set("loop", loopText);
            loopButton.Text = loopText;
        }
        bool lastOn = loop ? lastGoing : !weaving && !watching && m_f.HasLastTargets(Kind());
        if (lastButton.Enabled != lastOn)
            lastButton.Enabled = lastOn;
        if (loopButton.Enabled == lastGoing)
            loopButton.Enabled = !lastGoing;
        string lastText = lastGoing
            ? "Stop the " + LoopWord(m_f.Status.Length > 5 ? m_f.Status.Substring(5) : "") + " loop (last targets)"
            : "Start the loop on its last targets";
        if (m_actionText["loop.last"] != lastText)
        {
            m_actionText = m_actionText.Set("loop.last", lastText);
            lastButton.Text = lastText;
        }
        SetLight(m_loopLight, m_loopGroup, loop || weaving || watching || hopping, attention!, lightName!, lightDetail);
        SetLight(m_circLight, m_circJobGroup, m_f.CircuitRunning, Attention(true)!, m_f.CircuitName, null!,
            m_f.circuitStop);
        string one = HandText(k, 1);
        string two = HandText(k, 2);
        m_hands.Text = "hands: " + one + ", " + two;
        m_workState.Text = m_f.WorkState;
        string tripText = m_f.TripOn ? "End trip (" + m_f.TripState().Substring(m_f.TripState().IndexOf(':') + 2) + ")"
            : "Begin trip";
        if (m_tripButton.Text != tripText)
            m_tripButton.Text = tripText;
        string petText = m_f.PetLines();
        if (m_petsCard.Text != petText)
        {
            m_petsCard.Text = petText;
            if (m_petsCard.Height != Grown(m_petsCard))
                Fit();
        }
        string[] events = m_f.PetHistory();
        if (events.Length != m_petHistory.Items.Count
            || (events.Length > 0 && (string)m_petHistory.Items[0] != events[0]))
        {
            m_petHistory.BeginUpdate();
            m_petHistory.Items.Clear();
            m_petHistory.Items.AddRange(events);
            m_petHistory.EndUpdate();
        }
        if (m_f.ShowMapNow)
        {
            m_f.ShowMapNow = false;
            ShowPage("map");
        }
        if (m_mapRouteNote != null && m_f.MapWalkStatus.Length > 0 && m_mapRouteNote.Text != m_f.MapWalkStatus)
            m_mapRouteNote.Text = m_f.MapWalkStatus;
        // The book pictures once the client's art is readable.
        if (UoArt.Folder != null)
        {
            foreach (Tuple<CheckBox, int> p in m_adBookPicks)
            {
                if (p.Item1.Image == null)
                {
                    Bitmap? img = UoArt.Icon(p.Item2);
                    if (img != null)
                        p.Item1.Image = img;
                }
            }
        }

        if (m_f.BankBagStatus.Length > 0 && m_circSetupNote != null)
        {
            m_circSetupNote.Text = "Bank bag " + m_f.BankBagStatus;
            m_f.BankBagStatus = "";
        }
        if (m_f.OverflowStatus.Length > 0 && m_circSetupNote != null)
        {
            m_circSetupNote.Text = m_f.OverflowStatus;
            m_f.OverflowStatus = "";
        }
        if (m_overflowRuneButton != null)
        {
            string ob = m_f.OverflowRune != 0 ? "Reset house rune" : "Set house rune";
            if (m_overflowRuneButton.Text != ob)
                m_overflowRuneButton.Text = ob;
        }
        if (m_overflowPileButton != null)
        {
            string pb = m_f.OverflowPileSet ? "Reset pile" : "Set pile";
            if (m_overflowPileButton.Text != pb)
                m_overflowPileButton.Text = pb;
        }
        m_drink.Text = m_f.Sequence.StartsWith("Drink") || m_f.Sequence.StartsWith("Drank") ? m_f.Sequence : "";
        m_reach.Text = "reach " + m_f.Reach + " tiles; crops: "
            + string.Join(", ", Array.ConvertAll(m_f.Crops(), c => UoTiles.Name(c)));
        Resources();
        Skills(st);
        Heads();
        lock (m_f.Notes)
        {
            int from = (int)Math.Max(0, m_f.Notes.Count - (m_f.NotesAdded - m_notesSeen));
            for (int i = from; i < m_f.Notes.Count; i++)
                m_notes.Items.Add(m_f.Notes[i]);
            m_notesSeen = m_f.NotesAdded;
        }
        while (m_notes.Items.Count > 500)
            m_notes.Items.RemoveAt(0);
        Guard(k);
        int bank = (st.X >> 3) * 1024 + (st.Y >> 3);
        if (bank != m_lastBank)
        {
            m_lastBank = bank;
            m_lastBankZ = st.Z;
            ThreadPool.QueueUserWorkItem(_ => m_f.UpdateBanks(UoMap.LandZ));
        }
        else if (Math.Abs(st.Z - m_lastBankZ) > 10)
        {
            m_lastBankZ = st.Z;
            ThreadPool.QueueUserWorkItem(_ => m_f.UpdateBanks(UoMap.LandZ, true));
        }
    }
    int m_lastBank = -1, m_lastBankZ;
    static string HandText(GameLink k, int layer)
    {
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Value.Layer == layer && kv.Value.Mobile == k.State.Player)
                    return kv.Value.Graphic.ToString("X4");
        return "empty";
    }

    // For the control port: where the window is, and whether it follows.
    static AssistWindow? s_current;
    public static AssistWindow? Current { get => s_current; set => s_current = value; }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        s_current = this;
    }
    public string Report()
    {
        return (string)Invoke(new Func<string>(DockToClient));
    }
    // The window drawn to a PNG at the docked size, off screen: a layout check that takes no focus.
    // A control line names only a file: the PNG goes into the reports folder beside the settings, never another path.
    static string? SnapshotPath(string asked)
    {
        string name = Path.GetFileName((asked ?? "").Trim());
        if (name.Length == 0 || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;
        string dir = Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }
    public string Snapshot(string asked)
    {
        string? path = SnapshotPath(asked);
        if (path == null)
            return "ERROR window snapshot <name>.png";
        return (string)Invoke(new Func<string>(() =>
        {
            if (m_client == IntPtr.Zero)
                Size = new Size(620, 1010);
            Fit();
            Tick();
            using (Bitmap bmp = new(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, ImageFormat.Png);
            }
            return "saved " + path + " " + Width + "x" + Height;
        }));
    }
}
