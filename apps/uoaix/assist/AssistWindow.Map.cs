#nullable enable
using System.Collections.Immutable;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildMap()
    {
        // Map (Damian, 2026-10-15: "a third main tab which is map, and read the map file and put a player tracker there
        // like uo automap"): the client's own map files drawn in its radar colours, following the player, with the
        // trail walked and the people seen.
        Panel g = Group("Map");
        FlowLayoutPanel mapTop = SubTabs(g, "map");
        Button zin = new()
        {
            Text = "+",
            Width = 30
        };
        Button zout = new()
        {
            Text = "-",
            Width = 30
        };
        Button clear = Btn("Clear trail, corpses and drops");
        m_mapInfo = Words("");
        // Teleport-only spots (Damian, 2026-10-17: "flag unwalkable teleport spots", then "just move the control to the
        // map tab").
        CheckBox tpOnly = new()
        {
            Text = "Teleport-only spots",
            AutoSize = true,
            Checked = m_f.Cfg.Get("map.teleportonly", "0") == "1"
        };
        m_tipper.SetToolTip(tpOnly,
            "Magenta marks: places within Teleport range (11 tiles) you can stand on but cannot walk to from here, "
            + "like the roof of Magincia's mage shop. Hover one: flashing gold stars mark where you can walk to and "
            + "cast a Teleport at it from.");
        tpOnly.CheckedChanged += (s, e) => { m_f.Cfg.Set("map.teleportonly", tpOnly.Checked ? "1" : "0"); if (m_mapView
            != null) m_mapView.Invalidate(); };
        // The search (Damian, 2026-10-18: "in map add search box for name and type of shop or feature, that shows
        // shortest path, and a walk button there, and a run button too"): the places matching every word, nearest
        // first; the one picked gets its shortest walk drawn, and Walk or Run takes the player there.
        TextBox findBox = new()
        {
            Width = 120
        };
        ComboBox found = Pick(220);
        Button walkGo = Btn("Walk");
        Button runGo = Btn("Run");
        Button walkStop = Btn("Stop");
        Label routeNote = Words("");
        // Back on the player after a drag (Damian, 2026-10-15: "add a button above the map to center on me").
        Button centre = Btn("Center on me", () => { m_mapView.OffX = m_mapView.OffY = 0; m_mapView.Invalidate(); });
        m_tripButton = Btn("Begin trip", () =>
        {
            if (!m_f.TripOn)
            {
                string? why = m_f.BeginTrip();
                if (why != null)
                    Note(why);
                return;
            }
            string? path;
            string? w = m_f.EndTrip(out path);
            if (w != null)
            {
                Note(w);
            }
            else
            {
                Note("Trip report: " + path);
                AssistNative.ShellExecute(IntPtr.Zero, "open", path!, null!, null!, 1);
            }
        });
        // Before the hover readout (Damian, 2026-10-15: "reverse the layout order of the pin my spot and the text
        // readout").
        Button pinMe = Btn("Pin my spot", () => EditPin(null!, m_mapView.CenterX, m_mapView.CenterY));
        FieldGrid mg = Grid(g);
        // Folds give the map their height (Damian, 2026-10-21: "would love it if this map page had more expando use so
        // i can get more height in the view"); each kept as map.fold.<name>.
        mg.Fold("Tools", m_f.Cfg.Get("map.fold.tools", "0") == "1",
            shut => m_f.Cfg.Set("map.fold.tools", shut ? "1" : "0"));
        mg.Line("View", Strip(centre, zin, zout, clear, tpOnly));
        mg.Line("Mark", Strip(m_tripButton, pinMe, m_mapInfo));
        mg.Line("Find", Strip(findBox, found, walkGo, runGo, walkStop, routeNote));
        m_tipper.SetToolTip(findBox,
            "A shop's name or kind (tailor, smith, mage, provisioner), a bank, a feature the atlas keeps (anvil, "
            + "forge, loom) or a shopkeeper's name; every word must match. Enter searches.");
        m_tipper.SetToolTip(found, "The places found, nearest first; picking one draws its shortest walk on the map.");
        // The house pretest (AssistHousePlacement.md stage 3): with a deed's house picked, a left click on the map is
        // the house's front row, as the deed's cursor takes it.
        ComboBox housePick = Pick(220, "(no house)");
        foreach (string n in UoMap.HouseNames)
            housePick.Items.Add(n);
        housePick.SelectedIndex = 0;
        Label houseNote = Words("");
        // On the sub-tabs' row, after Area analysis (Damian, 2026-10-21: "move house to top row, after area analysis
        // save a row").
        housePick.Margin = new Padding(12, 4, 3, 3);
        houseNote.Margin = new Padding(3, 8, 3, 3);
        mapTop.Controls.Add(housePick);
        mapTop.Controls.Add(houseNote);
        m_tipper.SetToolTip(housePick,
            "A deed's house: a left click on the map is its front row, as the deed's cursor takes it in the game. "
            + "Green tiles fit, red tiles fail, the reasons are listed below the map. [house <type> said in the "
            + "client tests the game's own cursor.");
        m_tipper.SetToolTip(walkGo, "Walks to the place picked. A step of your own stops it.");
        m_tipper.SetToolTip(runGo, "Runs to the place picked. A step of your own stops it.");
        Action find = () => { found.Items.Clear(); foreach (AssistFeatures.MapPlace p in m_f.MapSearch(findBox.Text)) found.Items.Add(p); routeNote.Text = found.Items.Count
            == 0
            ? "nothing found" : found.Items.Count + " found"; if (found.Items.Count
            > 0) found.SelectedIndex = 0; };
        findBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; find(); } };
        found.SelectedIndexChanged += (s, e) =>
        {
            if (found.SelectedItem is not AssistFeatures.MapPlace p)
                return;
            routeNote.Text = "planning...";
            ThreadPool.QueueUserWorkItem(_ => { string? w = m_f.ShowRoute(p); BeginInvoke(new Action(() => { routeNote.Text = w
                ?? ""; m_mapView.Invalidate(); })); });
        };
        walkGo.Click += (s, e) => { string? w = m_f.MapGo(false); routeNote.Text = w ?? "walking"; };
        runGo.Click += (s, e) => { string? w = m_f.MapGo(true); routeNote.Text = w ?? "running"; };
        walkStop.Click += (s, e) => { m_f.MapStop(); routeNote.Text = "stopped"; };
        m_mapRouteNote = routeNote;
        m_mapView = Add(g,
            new MapView { Height = MapMin,
            Zoom = MapView.ZoomSteps.IndexOf(m_f.Cfg.GetInt("map.zoom", 2)) >= 0 ? m_f.Cfg.GetInt("map.zoom", 2)
            : 2 });
        // The house's reasons only while a house is picked.
        FieldGrid hg = Grid(g);
        ListBox houseWhy = hg.Line(null, new ListBox { Height = 60, HorizontalScrollbar = true }, null, true);
        hg.Show(houseWhy, false);
        List<int[]>? houseTiles = null;
        m_mapView.HouseTiles = () => houseTiles!;
        housePick.SelectedIndexChanged += (s, e) => { houseTiles = null; houseWhy.Items.Clear(); houseNote.Text = ""; hg.Show(houseWhy,
            housePick.SelectedIndex > 0); m_mapView.Invalidate(); };
        m_mapView.HouseClick = (x, y) =>
        {
            int type = housePick.SelectedIndex - 1;
            if (type < 0)
                return false;
            int z = UoMap.LandZ(x, y);
            if (z == int.MinValue)
            {
                houseNote.Text = "the map is not readable";
                return true;
            }
            houseNote.Text = "checking...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int cy;
                UoMap.HouseFit fit = m_f.HouseTest(UoMap.HouseMulti(UoMap.HouseTypes[type]), x, y, z, out cy);
                BeginInvoke(new Action(() =>
                {
                    houseTiles = fit.Tiles;
                    houseNote.Text = UoMap.HouseNames[type] + " at " + x + "," + cy + "," + z + ": "
                        + AssistFeatures.HouseVerdict(fit);
                    houseWhy.Items.Clear();
                    foreach (string w in fit.Why)
                        houseWhy.Items.Add(w);
                    m_mapView.Invalidate();
                }));
            });
            return true;
        };
        zin.Click += (s, e) => MapZoom(1);
        zout.Click += (s, e) => MapZoom(-1);
        m_mapView.MouseWheel += (s, e) => MapZoom(e.Delta > 0 ? 1 : -1);
        clear.Click += (s, e) => { lock (m_trail) m_trail.Clear(); m_f.ClearCorpses(); m_mapView.Invalidate(); };
        m_mapView.Corpses = () => m_f.Corpses();
        m_mapView.Hover = (x, y) =>
        {
            m_mapInfo.Text = x < 0 ? "" : x + "," + y + "  " + Sextant(x, y);
            // The pink spot under the cursor (a tile either way below IsoZoom, where a tile is under 4 px), and where
            // it can be cast from, worked out off the window's thread.
            int[]? spot = null;
            int near = m_mapView.Zoom >= MapView.IsoZoom ? 0 : 1, nd = int.MaxValue;
            if (x >= 0 && m_f.Cfg.Get("map.teleportonly", "0") == "1")
                foreach (int[] s in m_f.TeleportOnlySpots())
                {
                    int d = Math.Max(Math.Abs(s[0] - x), Math.Abs(s[1] - y));
                    if (d <= near && d < nd)
                    {
                        nd = d;
                        spot = s;
                    }
                }
            string key = spot == null ? "" : spot[0] + "," + spot[1] + "," + spot[2];
            if (key == m_tpHoverKey)
            {
                if (spot != null)
                    m_mapInfo.Text += "  teleport from " + m_tpFrom.Length + " spots";
                return;
            }
            m_tpHoverKey = key;
            m_tpFrom = new int[0][];
            m_mapView.Invalidate();
            if (spot == null)
                return;
            ThreadPool.QueueUserWorkItem(_ => { int[][] l = m_f.TeleportFrom(spot[0],
                spot[1]).ToArray(); BeginInvoke(new Action(() => { if (m_tpHoverKey
                != key) return; m_tpFrom = l; m_mapInfo.Text = spot[0] + "," + spot[1] + "  teleport from " + l.Length
                + " spots"; m_mapView.Invalidate(); })); });
        };
        m_mapView.Stars = () => m_tpFrom;
        m_mapView.OwnHouses = OwnHouses;
        m_mapView.Trail = () => { lock (m_trail) return m_trail.ToArray(); };
        m_mapView.Course = () => m_f.Course();
        m_mapView.Kills = () => m_f.KillMarks();
        m_mapView.Route = () => m_f.RouteShown();
        m_mapView.RouteLabel = () => { AssistFeatures.MapPlace? t = m_f.RouteTarget; return (t == null ? null
            : t.Name)!; };
        m_mapView.Pins = () => LoadPins().ToArray();
        m_mapView.RightClick = (x, y, at) => PinMenu(x, y, at);
        m_mapView.Caption = () => { int x = m_mapView.CenterX, y = m_mapView.CenterY; return (x | y) == 0 ? ""
            : "You: " + x + "," + y + "  " + Sextant(x, y); };
        // Filters: a checkbox per type, and Subtypes... for type:subtype; hidden ones kept in "map.hide" (| separated).
        FieldGrid fg = Grid(g);
        fg.SlotGap = 4;
        fg.Fold("Show types", m_f.Cfg.Get("map.fold.types", "0") == "1",
            shut => m_f.Cfg.Set("map.fold.types", shut ? "1" : "0"));
        foreach (string t in AssistAtlas.Types)
        {
            string type = t;
            CheckBox c = fg.Cell(new CheckBox { Text = t, AutoSize = true, Checked = !Hidden(t),
                ForeColor = t == "Person" || t == "Ghost" || t == "NPC" ? Color.DimGray
                : t == "Banker" ? Color.DarkGoldenrod : TypeColor(t, false) });
            c.CheckedChanged += (s, e) => { SetHidden(type, !c.Checked); m_mapView.Invalidate(); };
        }
        CheckBox regionsBox = fg.Cell(new CheckBox { Text = "Regions", AutoSize = true,
            Checked = m_f.Cfg.Get("map.regions", "on") == "on" });
        regionsBox.CheckedChanged += (s, e) => { m_f.Cfg.Set("map.regions",
            regionsBox.Checked ? "on" : "off"); m_mapView.Invalidate(); };
        fg.Cell(Btn("Subtypes...", SubtypeFilter));
        m_mapView.ShowRegions = () => regionsBox.Checked;
        // The harvest chunks, resource piles, drops and the logger's places are on the Circuits tab's map (Damian,
        // 2026-10-24: "then we can remove those features from the "map" tab which can be for other purposes").
        m_mapView.Decor = m_f.DecorIn;
        m_mapView.DecorSeq = () => m_f.DecorSeq;
        m_mapView.Marks = MapMarks;
        FieldGrid dg = Grid(g);
        dg.Fold("Details", m_f.Cfg.Get("map.fold.details", "0") == "1",
            shut => m_f.Cfg.Set("map.fold.details", shut ? "1" : "0"));
        m_mapDetail = dg.Line(null, new ListBox { Height = 110, HorizontalScrollbar = true }, null, true);
        StockDraw(m_mapDetail);
        m_mapView.Picked = s => ShowNpc(m_mapDetail, null!, s);
        m_mapView.Opened = s =>
        {
            if (m_f.Vendors != null && m_f.Vendors.Get(s) != null)
            {
                ShowPage("vendors");
                m_vendorShown = s;
                m_vendorsShape = "";
                m_vendorsList.SelectedItems.Clear();
                FillVendors();
            }
            else if (m_f.Atlas != null && m_f.Atlas.Shops().Exists(n => n.Serial == s && n.Type != "Player vendor"))
            {
                ShowPage("shops");
                PickShop(s);
            }
        };

        // Trips (Damian, 2026-10-16: "i should be able to select a past trip, and see a sub tab of the map tab that
        // gives me the report of that specific trip, and a overall meta analysis sub tab that shows time-bounded
        // regions of analysis").
        g = Group("Trip report");
        SubTabs(g, "map.trip");
        FieldGrid tripGrid = Grid(g);
        m_tripPick = Pick(380);
        Button openReport = Btn("Open full report");
        tripGrid.Line("Trip", Strip(m_tripPick, openReport));
        CheckBox autoTrips = tripGrid.Wide(new CheckBox { Text = "Record trips outside the guards", AutoSize = true,
            Checked = m_f.AutoTrips });
        autoTrips.CheckedChanged += (s, e) => m_f.AutoTrips = autoTrips.Checked;
        m_tripText = Add(g,
            new TextBox { Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            Height = TripMin, BackColor = SystemColors.Window });
        m_tripPick.SelectedIndexChanged += (s, e) => ShowTrip();
        openReport.Click += (s, e) => {
                if (m_tripPick.SelectedItem is TripItemView r && r.Trip.Report != null
            && File.Exists(r.Trip.Report))
                    AssistNative.ShellExecute(IntPtr.Zero, "open", r.Trip.Report, null!, null!,
            1);
            };
        g = Group("Area analysis");
        SubTabs(g, "map.areas");
        m_areaPeriod = Pick(130);
        foreach (KeyValuePair<string, int> p in s_areaPeriods)
            m_areaPeriod.Items.Add(p.Key);
        m_areaPeriod.SelectedIndex = Math.Max(0, Math.Min(s_areaPeriods.Length - 1, m_f.Cfg.GetInt("areas.period", 1)));
        m_areaPeriod.SelectedIndexChanged += (s, e) => { m_f.Cfg.Set("areas.period",
            m_areaPeriod.SelectedIndex.ToString()); FillAreas(); };
        Grid(g).Line("Trips from",
            Strip(m_areaPeriod,
            Words("newer trips weigh more (half weight every " + TripBook.HalfLifeDays + " days)")));
        m_areaBest = Add(g, new Label { Height = 40, Font = new Font(Font, FontStyle.Bold) });
        m_growers = m_growers.Add(m_areaBest);
        m_areaList = Add(g,
            new SortList { View = View.Details, Height = TripMin, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Region:220", "Newest:70", "Trips:45", "Combat:75", "Profit:190", "Training:230",
            "Spawn:65", "Close calls:220", "Harvest:150", "Reagents:170", "Resist training:200", "Magic drops:240",
            "Taming:220", "Herding:200", "Feathers and hides:300", "Hostiles:240" })
        {
            string[] a = c.Split(':');
            m_areaList.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_areaDetail = Add(g,
            new TextBox { Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            Height = AreaDetailMin, BackColor = SystemColors.Window });
        m_areaList.SelectedIndexChanged += (s, e) => { if (m_areaList.SelectedItems.Count
            > 0) m_areaDetail.Text = ((string?)m_areaList.SelectedItems[0].Tag)!; };
    }

    // The Map tab's sub tabs: the live map, one trip's report, and the region analysis over every trip.
    FlowLayoutPanel SubTabs(Panel g, string here)
    {
        FlowLayoutPanel row = Add(g, new FlowLayoutPanel { Height = 30, WrapContents = false });
        foreach (string[] t in new[] { new[] { "Live map", "map" }, new[] { "Trip report", "map.trip" },
            new[] { "Area analysis", "map.areas" } })
        {
            string to = t[1];
            bool on = to == here;
            Button b = new()
            {
                Text = t[0],
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular),
                BackColor = on ? SystemColors.ControlLightLight : SystemColors.Control
            };
            b.FlatAppearance.BorderColor = on ? SystemColors.Highlight : SystemColors.ControlDark;
            b.Click += (s, e) => ShowPage(to);
            row.Controls.Add(b);
        }
        return row;
    }

    ComboBox m_tripPick = null!, m_areaPeriod = null!;
    TextBox m_tripText = null!, m_areaDetail = null!;
    SortList m_areaList = null!; Label m_areaBest = null!; int m_tripsSeen = -1;
    static readonly ImmutableArray<KeyValuePair<string, int>> s_areaPeriods = [ new("the last 7 days", 7),
        new("the last 30 days", 30), new("the last 90 days", 90), new("all time", 0) ];
    sealed class TripItemView
    {
        public TripRecord Trip = null!; public override string ToString()
        {
            return Trip.Label();
        }
    }
    void FillTrips()
    {
        if (m_tripPick == null)
            return;
        m_tripsSeen = m_f.TripsWritten;
        string? was = m_tripPick.SelectedItem is TripItemView ? ((TripItemView)m_tripPick.SelectedItem).Trip.Report
            : null;
        m_tripPick.BeginUpdate();
        m_tripPick.Items.Clear();
        int keep = 0;
        foreach (TripRecord r in TripBook.Load())
        {
            if (r.Report == was)
                keep = m_tripPick.Items.Count;
            m_tripPick.Items.Add(new TripItemView { Trip = r });
        }
        m_tripPick.EndUpdate();
        if (m_tripPick.Items.Count > 0)
            m_tripPick.SelectedIndex = keep;
        else
            m_tripText.Text = "No trips yet. Every session is a trip from the login, and with no session trip running "
                + "one records itself from the first step out of guarded land; Begin trip on the live map starts one "
                + "by hand.";
    }
    void ShowTrip()
    {
        if (m_tripPick.SelectedItem is TripItemView v)
            m_tripText.Text = string.Join("\r\n", TripBook.Report(v.Trip, DateTime.Now));
    }
    void FillAreas()
    {
        if (m_areaList == null)
            return;
        m_tripsSeen = m_f.TripsWritten;
        DateTime now = DateTime.Now;
        List<TripRecord> trips = TripBook.Load();
        List<TripBook.Grade> grades = TripBook.Grades(trips,
            s_areaPeriods[Math.Max(0, m_areaPeriod.SelectedIndex)].Value, now);
        m_areaBest.Text = grades.Count == 0 ? "No trip with region data in this period."
            : string.Join("\n", TripBook.BestFor(grades, now));
        m_areaList.BeginUpdate();
        m_areaList.Items.Clear();
        foreach (TripBook.Grade g in grades)
        {
            string[] cells = new[] { g.Area, TripBook.Age(g.Newest, now), g.Trips.ToString(), TripBook.Level(g),
                TripBook.Profit(g), TripBook.TrainingGrade(g), TripBook.Spawn(g), TripBook.CloseCalls(g),
                TripBook.Harvest(g), TripBook.Reagents(g), TripBook.Resist(g), TripBook.Magic(g), TripBook.Taming(g),
                TripBook.Herding(g), TripBook.FeathersHides(g), TripBook.Foes(g) };
            List<string> detail = new()
            { g.Area + ": " + g.Trips + " trip" + (g.Trips == 1 ? "" : "s") + ", " + g.Minutes + " min, newest "
                + TripBook.Age(g.Newest, now) };
            for (int i = 3; i < cells.Length; i++)
                if (cells[i].Length > 0)
                    detail.Add(m_areaList.Columns[i].Text + ": " + cells[i]);
            m_areaList.Items.Add(new ListViewItem(cells) { Tag = string.Join("\r\n", detail) });
        }
        m_areaList.EndUpdate();
        m_areaDetail.Text = "";
        Fit();
    }

    bool Hidden(string kind)
    {
        return Array.IndexOf(m_f.Cfg.Get("map.hide", "").Split('|'), kind) >= 0;
    }
    void SetHidden(string kind, bool hide)
    {
        List<string> l = new(m_f.Cfg.Get("map.hide", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        l.Remove(kind);
        if (hide)
            l.Add(kind);
        m_f.Cfg.Set("map.hide", string.Join("|", l));
    }
    static Color TypeColor(string type, bool criminal)
    {
        if (criminal)
            return Color.Red;
        switch (type)
        {
            case "Banker":
                return Color.Gold;
            case "Shopkeeper":
                return Color.LimeGreen;
            case "Healer":
                return Color.DeepSkyBlue;
            case "Guard":
                return Color.RoyalBlue;
            case "NPC":
                return Color.Silver;
            case "Person":
                return Color.White;
            case "Ghost":
                return Color.LightGray;
            case "Player vendor":
                return Color.Aquamarine;
            case "House":
                return Color.SandyBrown;
            case "Boat":
                return Color.CadetBlue;
            case "Guildstone":
                return Color.MediumOrchid;
            case "Anvil":
                return Color.SlateGray;
            case "Forge":
                return Color.OrangeRed;
            default:
                return Color.Orange;
        }
    }
    // Pins, kept in the "pins" setting: x, y, kind, label each, | between pins.
    List<MapView.Pin> LoadPins()
    {
        List<MapView.Pin> list = new();
        foreach (string e in m_f.Cfg.Get("pins", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] t = e.Split(new[] { ',' }, 4);
            int x, y;
            if (t.Length < 4 || !int.TryParse(t[0], out x) || !int.TryParse(t[1], out y))
                continue;
            list.Add(new MapView.Pin { X = x, Y = y, Kind = t[2], Label = t[3] });
        }
        return list;
    }
    void SavePins(List<MapView.Pin> pins)
    {
        m_f.Cfg.Set("pins",
            string.Join("|", pins.ConvertAll(p => p.X + "," + p.Y + "," + p.Kind + "," + p.Label.Replace("|", "/"))));
        m_mapView.Invalidate();
    }
    // Right-click on the map: add a pin there, or edit or remove the pin under the pointer.
    void PinMenu(int x, int y, Point at)
    {
        List<MapView.Pin> pins = LoadPins();
        MapView.Pin? near = null;
        foreach (MapView.Pin p in pins)
        {
            Point px = m_mapView.PixelOf(p.X, p.Y);
            if (Math.Abs(px.X - at.X) <= 8 && at.Y >= px.Y - 16 && at.Y <= px.Y + 2)
                near = p;
        }
        ContextMenuStrip menu = new();
        if (near != null)
        {
            MapView.Pin hit = near;
            menu.Items.Add("Edit pin \"" + hit.Label + "\"...", null, (s, e) => EditPin(hit, hit.X, hit.Y));
            menu.Items.Add("Remove pin \"" + hit.Label + "\"", null,
                (s, e) => { List<MapView.Pin> l = LoadPins(); l.RemoveAll(p => p.X == hit.X && p.Y == hit.Y
                && p.Label == hit.Label); SavePins(l); });
        }
        else
        {
            menu.Items.Add("Add a pin at " + x + "," + y + "...", null, (s, e) => EditPin(null!, x, y));
        }

        menu.Show(m_mapView, at);
    }
    void EditPin(MapView.Pin was, int x, int y)
    {
        using (Form dlg = new()
        {
            Text = was == null ? "Add a pin at " + x + "," + y : "Edit pin",
            Width = 320,
            Height = 170,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedToolWindow
        })
        {
            TextBox label = new()
            {
                Left = 10,
                Top = 10,
                Width = 285,
                Text = was != null ? was.Label : ""
            };
            ComboBox kind = new()
            {
                Left = 10,
                Top = 42,
                Width = 285,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (string k in MapView.PinKinds)
                kind.Items.Add(k);
            kind.SelectedItem = was != null ? was.Kind : "My house";
            if (kind.SelectedIndex < 0)
                kind.SelectedIndex = 0;
            Button ok = new()
            {
                Text = "OK",
                Left = 134,
                Top = 82,
                Width = 75,
                DialogResult = DialogResult.OK
            };
            Button cancel = new()
            {
                Text = "Cancel",
                Left = 220,
                Top = 82,
                Width = 75,
                DialogResult = DialogResult.Cancel
            };
            dlg.Controls.AddRange(new Control[] { label, kind, ok, cancel });
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            string text = label.Text.Trim();
            if (text.Length == 0)
                text = (string)kind.SelectedItem;
            List<MapView.Pin> pins = LoadPins();
            if (was != null)
                pins.RemoveAll(p => p.X == was.X && p.Y == was.Y && p.Label == was.Label);
            pins.Add(new MapView.Pin { X = x, Y = y, Kind = (string)kind.SelectedItem, Label = text });
            SavePins(pins);
        }
    }

    List<(int X, int Y, string Name)> OwnHouses()
    {
        return m_f.Houses().ConvertAll(h => (h.DoorX, h.DoorY, h.Name));
    }

    MapView.Mark[] MapMarks()
    {
        if (m_f.Atlas == null)
            return new MapView.Mark[0];
        Set<string> hide = Set<string>.Of(m_f.Cfg.Get("map.hide", "").Split('|'));
        List<MapView.Mark> list = new();
        // People live only (Damian, 2026-10-20: "i do expect the view there to be live, not old archived data"; six
        // shopkeepers last seen days before drew round his house): the kept NPCs' archived places stay for the walks.
        DateTime now = DateTime.Now;
        foreach (AtlasNpc n in m_f.Atlas.Visible())
            if ((now - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs && !hide.Contains(n.Type)
                && !hide.Contains(n.Type + ":" + n.Sub))
                list.Add(new MapView.Mark { X = n.X, Y = n.Y, Serial = n.Serial, Color = TypeColor(n.Type, n.Criminal),
                    Scale = MapView.TierScale(n.Name), Body = n.Body >= 0x190 && n.Body <= 0x193 ? 0 : n.Body });
        foreach (AtlasPlace p in m_f.Atlas.Places())
            if (!hide.Contains(p.Kind) && !hide.Contains(p.Kind + ":" + AssistAtlas.PlaceSub(p)))
                list.Add(new MapView.Mark { X = p.X, Y = p.Y, Serial = p.Serial, Color = TypeColor(p.Kind, false) });
        if (m_f.Cfg.Get("map.teleportonly", "0") == "1")
            foreach (int[] s in m_f.TeleportOnlySpots())
                list.Add(new MapView.Mark { X = s[0], Y = s[1], Color = Color.Magenta });
        return list.ToArray();
    }
    // Every type:subtype the atlas has seen, ticked when shown.
    void SubtypeFilter()
    {
        if (m_f.Atlas == null)
            return;
        using (Form dlg = new()
        {
            Text = "Map subtypes",
            Width = 360,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow
        })
        {
            CheckedListBox list = new()
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true
            };
            foreach (string kind in m_f.Atlas.Kinds())
                list.Items.Add(kind, !Hidden(kind));
            list.ItemCheck += (s, e) => { SetHidden((string)list.Items[e.Index],
                e.NewValue != CheckState.Checked); m_mapView.Invalidate(); };
            dlg.Controls.Add(list);
            dlg.ShowDialog(this);
        }
    }

    void MapZoom(int d)
    {
        int at = MapView.ZoomSteps.IndexOf(m_mapView.Zoom);
        if (at < 0)
            at = 1;
        m_mapView.Zoom = MapView.ZoomSteps[Math.Max(0, Math.Min(MapView.ZoomSteps.Length - 1, at + d))];
        m_f.Cfg.Set("map.zoom", m_mapView.Zoom.ToString());
        m_mapView.Invalidate();
    }
    void MapTick(AssistState st)
    {
        int x, y;
        lock (st.Gate)
        {
            x = st.X;
            y = st.Y;
        }
        if ((x | y) == 0)
            return;
        lock (m_trail)
        {
            if (m_trail.Count == 0 || m_trail[m_trail.Count - 1].X != x || m_trail[m_trail.Count - 1].Y != y)
            {
                m_trail.Add(new Point(x, y));
                if (m_trail.Count > TrailKeep)
                    m_trail.RemoveAt(0);
            }
        }
        long seq = m_f.Atlas != null ? Interlocked.Read(ref m_f.Atlas.ChangeSeq) : 0;
        if (m_mapView.CenterX != x || m_mapView.CenterY != y)
        {
            m_mapView.CenterX = x;
            m_mapView.CenterY = y;
            if (m_page == "map")
            {
                m_mapView.Invalidate();
                m_mapSeq = seq;
                m_mapIdle = 0;
            }
        }
        // Standing still, the map repaints when the atlas changed, else every MapIdleTicks for the marks it does not
        // count (corpses, kills, the course).
        else if (m_page == "map" && (seq != m_mapSeq || ++m_mapIdle >= MapIdleTicks))
        {
            m_mapView.Invalidate();
            m_mapSeq = seq;
            m_mapIdle = 0;
        }
        // The circuit map follows the player while the Circuits tab shows, and repaints each tick for its piles and
        // marks.
        if (m_circMap != null && m_page == "circuit" && m_circMap.Visible)
        {
            m_circMap.CenterX = x;
            m_circMap.CenterY = y;
            m_circMap.Invalidate();
        }
    }
}
