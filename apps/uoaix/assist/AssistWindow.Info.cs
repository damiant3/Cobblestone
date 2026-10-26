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
    void BuildStatus()
    {
        // Status (Damian, 2026-10-15: "two columns name: values grouped by vitals, attrs, and other"): every value,
        // always.
        Panel sg = Group("Status");
        // The name and title large (Damian, 2026-10-18: "make the name and title in status like 18pt font").
        m_status = Add(sg,
            new Label { AutoSize = false, Height = 36, Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            AutoEllipsis = true });
        // Laid out as Resources is (Damian, 2026-10-15: "layout for status like resources, denormalized"): the group's
        // name in the first column, level with its first values, and each value one "name  value" cell, StatColumns to
        // a row (Damian, 2026-10-18: "make the vitals and attrs and others 3 wide columns, there is room").
        const int StatColumns = 3;
        m_stat = Add(sg, new TableLayoutPanel { ColumnCount = 1 + StatColumns, Height = 4 });
        m_stat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        for (int c = 0; c < StatColumns; c++)
            m_stat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / StatColumns));
        int srow = 0;
        foreach (string[] grp in s_statGroups)
        {
            m_stat.Controls.Add(new Label { AutoSize = true, Text = grp[0], Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 1, 0, 0) }, 0, srow);
            for (int i = 1; i < grp.Length; i++)
            {
                Label v = new()
                {
                    AutoSize = true,
                    Margin = new Padding(0, 1, 0, 0)
                };
                m_stat.Controls.Add(v, 1 + (i - 1) % StatColumns, srow + (i - 1) / StatColumns);
                m_statValues = m_statValues.Set(grp[i], v);
                if (grp[i] == "dps" || grp[i] == "vs")
                {
                    string field = grp[i];
                    v.Cursor = Cursors.Hand;
                    v.Click += (s, e) => PickCombat(field);
                    m_tipper.SetToolTip(v,
                        field == "dps" ? "Click to pick the defender." : "Click to pick the caster and the spell.");
                }
            }
            int lines = (grp.Length - 1 + StatColumns - 1) / StatColumns;
            for (int r = 0; r < lines; r++)
                m_stat.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            srow += lines;
        }
        m_stat.RowCount = srow;
        m_stat.Height = srow * 18 + 4;
        Summarize(sg, () => m_statLine);
        // Session (Damian, 2026-10-15: "add session status, time since login, where you at overview, track and show the
        // exact tile info, any zone you are in, guarded/not guarded, home, not home, show criminal state").
        Panel sessionGroup = Group("Session");
        m_session = Add(sessionGroup, new Label { AutoSize = false, Height = 80 });
        m_summary = Add(sessionGroup, new Label { AutoSize = false, Height = 4 });
        m_growers = m_growers.Add(m_session).Add(m_summary);
        Summarize(sessionGroup, () => m_sessionLine);
        // Pets and the mount (Damian, 2026-10-15: "add pet and mount happiness").
        m_petGroup = Group("Pets and mount");
        m_petsCard = Add(m_petGroup, new Label { AutoSize = false, Height = 4 });
        m_growers = m_growers.Add(m_petsCard);
        Summarize(m_petGroup, () => FirstLine(m_petsCard.Text));
        m_petHistory = Add(m_petGroup, new ListBox { Height = 90, HorizontalScrollbar = true });
    }

    void BuildResources()
    {
        Panel g = Group("Resources");
        m_resCount = new Button { Text = "Count", AutoSize = true, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat,
            Visible = false, BackColor = Color.FromArgb(240, 190, 60), Font = new Font(Font, FontStyle.Regular) };
        m_tipper.SetToolTip(m_resCount,
            "The pack or a bag in it has not been opened since the login: open each one once and count.");
        m_resCount.Click += (s, e) => { m_resCount.Enabled = false; ThreadPool.QueueUserWorkItem(_ => { string? w = m_f.CountPack(); BeginInvoke(new Action(() => { m_resCount.Enabled = true; if (w
            != null) Note(w); })); }); };
        m_headOf[g]!.Controls.Add(m_resCount);
        // Six item columns after the heading: two to a row span three each, the reagents three to a row span two.
        m_res = Add(g, new TableLayoutPanel { ColumnCount = 7, Height = 28 });
        m_res.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        for (int c = 0; c < 6; c++)
            m_res.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        Button radd = Btn("Add...");
        radd.Click += (s, e) => { int gr = ItemPicker.Pick(this, "Add an item to count", m_f.SeenGraphics(true),
            m_f.SeenGraphics(false)); if (gr
            < 0) return; List<AssistFeatures.CountRow> l = m_f.ResourceList(); if (!l.Exists(r =>
            Array.IndexOf(r.Graphics, gr)
            >= 0)) { l.Add(new AssistFeatures.CountRow { Name = UoTiles.Name(gr), Group = AssistFeatures.GroupOf(gr),
            Graphics = new[] { gr } }); m_f.SetResources(l); } m_resShape = ""; };
        Grid(g).Line(null, Strip(radd, Words("Right-click an item to remove it or move it to another group.")));
        // Collapsed: the recall reagents, icon and count (Damian, 2026-10-17: "show pearl mandrake moss with icon:
        // count"). In the header row, between the title and the Count button (Damian, 2026-10-18: "put the summary
        // about recall regs in same row as the expando between that and the count button, not on new line").
        FlowLayoutPanel rsum = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        foreach (int art in s_resSummaryArts)
        {
            PictureBox pic = new()
            {
                Width = 18,
                Height = 18,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, 2, 0)
            };
            Label tally = new()
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 10, 0),
                Font = new Font(Font, FontStyle.Bold)
            };
            rsum.Controls.Add(pic);
            rsum.Controls.Add(tally);
            m_resSumPics.Add(pic);
            m_resSumCounts.Add(tally);
        }
        FlowLayoutPanel? resInner = ((FlowLayoutPanel?)g.Tag)!;
        rsum.Visible = !resInner!.Visible;
        resInner.VisibleChanged += (s, e) => rsum.Visible = !resInner.Visible;
        m_headOf[g]!.Controls.Add(rsum);
    }

    void BuildChat()
    {
        // Chat: player speech only (Damian, 2026-10-15: "filters out everything but human player speech, so status
        // messages don't scroll out of sight comms"). Events: advice and assist events.
        Panel chatGroup = Group("Chat and events");
        m_talk = Add(chatGroup, new TabControl { Height = 110 });
        Summarize(chatGroup, () => m_notes.Items.Count > 0 ? (string)m_notes.Items[m_notes.Items.Count - 1] : "");
        TabPage chatPage = new("Chat");
        TabPage eventPage = new("Events");
        m_talk.TabPages.Add(chatPage);
        m_talk.TabPages.Add(eventPage);
        m_chat = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        chatPage.Controls.Add(m_chat);
        ContextMenuStrip hide = new();
        hide.Items.Add("Hide this speaker", null, (s, e) => HideSpeaker());
        hide.Items.Add("Show every speaker again", null,
            (s, e) => { m_f.Cfg.Set("chat.hidden", ""); m_chatSeen = 0; m_chat.Items.Clear(); });
        m_chat.ContextMenuStrip = hide;
        m_chat.MouseDown += (s, e) => { if (e.Button
            == MouseButtons.Right) { int i = m_chat.IndexFromPoint(e.Location); if (i
            >= 0) m_chat.SelectedIndex = i; } };
        m_notes = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        eventPage.Controls.Add(m_notes);
        // Log: the relay log's tail, the fail-safe's state and its watches (untick one to stop it disabling the
        // assist).
        TabPage logPage = new("Log");
        m_talk.TabPages.Add(logPage);
        m_logList = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, Font = new Font("Consolas", 8f) };
        m_guardState = new Label { Dock = DockStyle.Top, Height = 32 };
        m_watches = new CheckedListBox { Dock = DockStyle.Bottom, Height = 74, CheckOnClick = true };
        foreach (AssistGuard.Watch w in AssistGuard.Watches)
            m_watches.Items.Add(w.Id + ": " + w.Text + " (" + w.Limit + " in " + w.Seconds + " s)",
                AssistGuard.Current == null || AssistGuard.Current.Watching(w.Id));
        m_watches.ItemCheck += (s, e) => { if (AssistGuard.Current
            != null) AssistGuard.Current.SetWatching(AssistGuard.Watches[e.Index].Id,
            e.NewValue == CheckState.Checked); };
        logPage.Controls.Add(m_logList);
        logPage.Controls.Add(m_guardState);
        logPage.Controls.Add(m_watches);
        m_logList.BringToFront();
    }

    // Resources collapsed: black pearl, mandrake root, bloodmoss.
    static readonly ImmutableArray<int> s_resSummaryArts = [ 0xF7A, 0xF86, 0xF7B ];
    readonly List<PictureBox> m_resSumPics = new(); readonly List<Label> m_resSumCounts = new();
    // The reagents' short names, three to a row (Damian, 2026-10-17: "use short names like pearl, moss, mandrake, ash
    // silk").
    static readonly Map<int, string> s_shortReagent = Map<int, string>.Of((0xF7A, "pearl"), (0xF7B, "moss"),
        (0xF86, "mandrake"), (0xF8C, "ash"), (0xF8D, "silk"), (0xF84, "garlic"), (0xF85, "ginseng"),
        (0xF88, "nightshade"));
    string m_statLine = "", m_sessionLine = "";

    TableLayoutPanel m_stat = null!; Map<string, Label> m_statValues = Map<string, Label>.Empty;
    static readonly string[][] s_statGroups = {
        new[] { "attributes", "strength", "dexterity", "intelligence" }, new[] { "backpack", "weight", "gold" },
            new[] { "combat", "armor", "dps", "vs" },
    };

    // The session summary under the session details, one line each, grown to its lines (the window does not scroll).
    void Summary()
    {
        string text = m_f.SessionSummary();
        if (m_summary.Text == text)
            return;
        m_summary.Text = text;
        if (m_summary.Height != Grown(m_summary))
            Fit();
    }

    void PetsAtLogin(AssistState st)
    {
        if (m_workGroup != null && st.LoginAt != DateTime.MinValue && st.LoginAt != m_workCheckedFor
            && !m_f.PackStale())
        {
            m_workCheckedFor = st.LoginAt;
            if (!m_f.WorkMaterialsHeld())
                Collapse(m_workGroup);
        }
        if (m_petGroup == null || st.LoginAt == DateTime.MinValue || st.LoginAt == m_petsCheckedFor
            || (DateTime.Now - st.LoginAt).TotalMilliseconds < PetsSettleMs)
            return;
        m_petsCheckedFor = st.LoginAt;
        if (!m_f.PetsAround())
            Collapse(m_petGroup);
    }

    // ---------- refresh ----------
    // Status as Damian asked (2026-10-18: "for attributes, show buff and debuff, base versus current. add weight
    // capacity ... show weight of gold and color it to indicate if you are carrying too much gold (over 300). armor
    // should color code if your armor is in need of repair"). The server sends base plus buff only (OUO
    // packet_manager.c:1206-1208), so the base is learned: a change of 1 is a gain or loss and moves it, a larger jump
    // is a buff or debuff and does not. The carry limit is base strength * 4 + 30 (OUO mobile.c:3447-3450); gold weighs
    // (amount + 49) / 50 (item.c:4165-4169). The client is never sent armor durability (OUO weapon.c:901-906 wears the
    // rating down), so the rating is held against the best seen with the same worn items: under it, amber; at RepairRed
    // of it or less, red.
    public const int GoldCarry = 300; const double RepairRed = 0.8;
    ValueMap<string, int> m_bestArmor = ValueMap<string, int>.Empty;
    // Worn items that change attributes (Damian, 2026-10-18: "when the assist first saw Kwik, he was wearing a wizards
    // hat, which had mutated his base attributes ... solve the general case of a new char"): once the worn items and
    // the attributes have stood StatSettleMs after exactly one item went on or off, the attributes' change is that
    // item's modifier (statmod.<serial>=str,dex,int). The base is learned from the current value less the worn items'
    // modifiers; an item worn when the base was first learned (base.<name>.worn) that comes off has its modifier taken
    // out of the base. Kwik's hat: str -5, dex -5, int +5.
    const int StatSettleMs = 3000;
    static readonly ImmutableArray<string> s_statNames = [ "strength", "dexterity", "intelligence" ];
    Set<uint> m_settledWorn = null!, m_seenWorn = null!;
    int[] m_settledStats = null!, m_seenStats = null!; DateTime m_seenAt;
    static bool SameStats(int[] a, int[] b)
    {
        return a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
    }
    static Set<uint> Worn(AssistState st)
    {
        Set<uint> w = Set<uint>.Empty;
        foreach (KeyValuePair<uint, AssistItem> kv in st.Items)
            if (kv.Value.Layer >= 0 && kv.Value.Mobile == st.Player)
                w = w.Add(kv.Key);
        return w;
    }
    static bool SameItems(Set<uint> a, Set<uint> b)
    {
        return a.Count == b.Count && a.IsSupersetOf(b);
    }
    int[]? StatMod(uint item)
    {
        string[] t = m_f.Cfg.Get("statmod." + item.ToString("X"), "").Split(',');
        int a, b, c;
        return t.Length == 3 && int.TryParse(t[0], out a) && int.TryParse(t[1], out b) && int.TryParse(t[2], out c)
            ? new[] { a, b, c } : null;
    }
    void LearnStatMods(AssistState st)
    {
        if (st.Player == 0 || st.Str <= 0)
            return;
        Set<uint> worn = Worn(st);
        int[] now = new[] { st.Str, st.Dex, st.Int };
        if (m_seenWorn == null || !SameItems(worn, m_seenWorn) || !SameStats(now, m_seenStats))
        {
            m_seenWorn = worn;
            m_seenStats = now;
            m_seenAt = DateTime.UtcNow;
            if (m_settledWorn == null)
            {
                m_settledWorn = worn;
                m_settledStats = now;
            }
            return;
        }
        if ((DateTime.UtcNow - m_seenAt).TotalMilliseconds < StatSettleMs
            || (SameItems(worn, m_settledWorn) && SameStats(now, m_settledStats)))
            return;
        List<uint> on = new();
        foreach (uint s in worn)
            if (!m_settledWorn.Contains(s))
                on.Add(s);
        List<uint> off = new();
        foreach (uint s in m_settledWorn)
            if (!worn.Contains(s))
                off.Add(s);
        if (on.Count + off.Count == 1 && !SameStats(now, m_settledStats))
        {
            uint item = on.Count == 1 ? on[0] : off[0];
            int sign = on.Count == 1 ? 1 : -1;
            int[] mod = new int[3];
            for (int i = 0; i < 3; i++)
                mod[i] = sign * (now[i] - m_settledStats[i]);
            m_f.Cfg.Set("statmod." + item.ToString("X"), string.Join(",", mod));
            List<string> atLearn = new(m_f.Cfg.Get("base." + st.Name + ".worn", "").Split(new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries));
            if (off.Count == 1 && atLearn.Remove(item.ToString("X")))
            {
                for (int i = 0; i < 3; i++)
                {
                    string key = "base." + st.Name + "." + s_statNames[i];
                    int b = m_f.Cfg.GetInt(key, int.MinValue);
                    if (b != int.MinValue)
                        m_f.Cfg.Set(key, (b - mod[i]).ToString());
                }
                m_f.Cfg.Set("base." + st.Name + ".worn", string.Join(",", atLearn));
            }
        }
        m_settledWorn = worn;
        m_settledStats = now;
    }
    int BaseStat(AssistState st, string name, int cur)
    {
        LearnStatMods(st);
        int at = s_statNames.IndexOf(name);
        int e = cur;
        if (at >= 0)
        {
            foreach (uint s in Worn(st))
            {
                int[]? m = StatMod(s);
                if (m != null)
                    e -= m[at];
            }
        }

        string key = "base." + st.Name + "." + name;
        int b = m_f.Cfg.GetInt(key, int.MinValue);
        if (b == int.MinValue)
        {
            if (e > 0)
            {
                m_f.Cfg.Set(key, e.ToString());
                List<string> w = new();
                foreach (uint s in Worn(st))
                    if (StatMod(s) == null)
                        w.Add(s.ToString("X"));
                m_f.Cfg.Set("base." + st.Name + ".worn", string.Join(",", w));
            }
            return e;
        }
        if (Math.Abs(e - b) == 1)
        {
            m_f.Cfg.Set(key, e.ToString());
            return e;
        }
        return b;
    }
    string? StatText(AssistState st, string name, out Color color)
    {
        color = SystemColors.ControlText;
        switch (name)
        {
            case "strength":
            case "dexterity":
            case "intelligence":
                {
                    int cur = name == "strength" ? st.Str
                        : name == "dexterity" ? st.Dex : st.Int, b = BaseStat(st, name, cur), d = cur - b;
                    if (d > 0)
                        color = Color.FromArgb(0, 130, 0);
                    else if (d < 0)
                        color = Color.FromArgb(190, 30, 30);
                    // Each attribute with the vital it sets, the 1998 maximum being the attribute (Damian, 2026-10-18:
                    // "vitals and attributes seem like they need to be merged ... base hits is just the same as
                    // strength"): the vital's maximum is shown only where it is not the attribute.
                    string vital = name == "strength" ? "hits" : name == "dexterity" ? "stamina" : "mana";
                    int now = name == "strength" ? st.Hits
                        : name == "dexterity" ? st.Stam : st.Mana, max = name == "strength" ? st.MaxHits
                        : name == "dexterity" ? st.MaxStam : st.MaxMana;
                    return cur + (d != 0 ? "  (base " + b + ", " + (d > 0 ? "+" : "") + d + ")" : "") + "   " + vital
                        + " " + now + (max != cur ? " / " + max : "");
                }
            case "weight":
                {
                    int bs = BaseStat(st, "strength", st.Str), cap = bs * 4 + 30;
                    if (st.Weight * 100 > cap * AssistFeatures.LoadOverPct)
                        color = Color.FromArgb(190, 30, 30);
                    else if (st.Weight * 100 >= cap * 95)
                        color = Color.FromArgb(220, 120, 0);
                    return st.Weight + " / " + cap;
                }
            case "gold":
                {
                    if (st.Gold > GoldCarry)
                        color = Color.FromArgb(200, 120, 0);
                    return st.Gold + "  (" + (st.Gold + 49) / 50 + " st)";
                }
            case "armor":
                {
                    List<uint> worn = new();
                    foreach (KeyValuePair<uint, AssistItem> kv in st.Items)
                        if (kv.Value.Layer >= 0 && kv.Value.Mobile == st.Player)
                            worn.Add(kv.Key);
                    worn.Sort();
                    string key = string.Join(",", worn);
                    int best;
                    if (!m_bestArmor.TryGet(key, out best) || st.Armor > best)
                        m_bestArmor = m_bestArmor.Set(key, best = st.Armor);
                    if (st.Armor < best)
                        color = st.Armor <= best * RepairRed ? Color.FromArgb(190, 30, 30)
                            : Color.FromArgb(200, 120, 0);
                    return st.Armor + (st.Armor < best ? "  (best " + best + ", repair)" : "");
                }
            case "dps":
                return Dps(st);
            case "vs":
                return SpellTaken(st);
        }
        return StatValue(st, name);
    }
    static int SkillOf(AssistState st, int id)
    {
        int v;
        return st.Skills.TryGet(id + 1, out v) ? v : 0;
    }
    // The defender and the caster the combat row is figured against, picked by a click on the value (Damian,
    // 2026-10-18: "allow me to click that field and pick any magic caster and spell. also for dps, allow setting the
    // relevant defender details. assume troll for default"), from the server's templates (UoBestiary).
    static string[]? Creature(string name)
    {
        foreach (string r in UoBestiary.Creatures)
        {
            string[] p = r.Split('|');
            if (string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
    static string[]? SpellRow(string name)
    {
        foreach (string r in UoBestiary.Spells)
        {
            string[] p = r.Split('|');
            if (string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
    static string Abbrev(string spell)
    {
        string[] w = spell.Split(' ');
        return w.Length > 1 ? (w[0].Substring(0, 1) + w[1].Substring(0, 1)).ToUpperInvariant()
            : spell.Equals("Flamestrike", StringComparison.OrdinalIgnoreCase) ? "FS"
            : spell.Length > 2 ? spell.Substring(0, 2).ToUpperInvariant() : spell;
    }
    string Defender => m_f.Cfg.Get("status.defender", "troll");
    string Caster => m_f.Cfg.Get("status.caster", "lich");
    string Spell => m_f.Cfg.Get("status.spell", "Flamestrike");
    void PickCombat(string field)
    {
        if (field == "dps")
        {
            string? c = PickName("Defender for the DPS", UoBestiary.Creatures.ConvertAll(r => r.Split('|')[0]),
                Defender);
            if (c != null)
                m_f.Cfg.Set("status.defender", c);
            return;
        }
        string[] casters = UoBestiary.Creatures.FindAll(r => { int m; return int.TryParse(r.Split('|')[6], out m)
            && m > 0; });
        string? who = PickName("Caster", Array.ConvertAll(casters, r => r.Split('|')[0]), Caster);
        if (who == null)
            return;
        string? sp = PickName("Spell", UoBestiary.Spells.ConvertAll(r => r.Split('|')[0]), Spell);
        if (sp == null)
            return;
        m_f.Cfg.Set("status.caster", who);
        m_f.Cfg.Set("status.spell", sp);
    }
    // A list to pick one name from, filtered as you type; null when cancelled.
    string? PickName(string title, string[] names, string current)
    {
        using (Form dlg = new()
        {
            Text = title,
            Width = 320,
            Height = 420,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow
        })
        {
            TextBox filter = new()
            {
                Dock = DockStyle.Top
            };
            ListBox list = new()
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
            Button ok = new()
            {
                Dock = DockStyle.Bottom,
                Text = "Pick",
                Height = 28,
                DialogResult = DialogResult.OK
            };
            Action fill = () => { list.BeginUpdate(); list.Items.Clear(); foreach (string n in names) if (n.IndexOf(filter.Text,
                StringComparison.OrdinalIgnoreCase)
                >= 0) list.Items.Add(n); list.EndUpdate(); int at = list.Items.IndexOf(current); if (at
                >= 0) list.SelectedIndex = at; };
            filter.TextChanged += (s, e) => fill();
            list.DoubleClick += (s, e) => { if (list.SelectedItem != null) dlg.DialogResult = DialogResult.OK; };
            dlg.Controls.Add(list);
            dlg.Controls.Add(filter);
            dlg.Controls.Add(ok);
            dlg.AcceptButton = ok;
            fill();
            return dlg.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null ? (string)list.SelectedItem
                : null;
        }
    }
    // A weapon's melee damage per second on the defender, from the 1998 server (OUO combat.c): the hit chance is
    // (skill + 500) * 50 / (defender's wrestling + 500) percent, at least 2 (1923-1942); a hit is (tactics + base
    // strength
    // * 2 + 500) * the weapon's dice / 1000 (326-346), less the armor's absorption, on average between AR / 4 and
    // (AR + 1) / 2 (1259-1268), halved, at least 1 (1504-1507); a swing takes 2 * (40000 / ((base dex + 100) * speed))
    // + 1 ticks of 250 ms, 20 when the product is under 2000 (1644-1720).
    const int TacticsSkill = 27, ResistSkill = 26, SwordsSkill = 40, MaceSkill = 41, FencingSkill = 42;
    string? Dps(AssistState st)
    {
        string[]? w = null;
        string? named = null;
        foreach (AssistItem it in st.Items.Values)
        {
            if (it.Mobile == st.Player && (it.Layer == 1 || it.Layer == 2))
            {
                List<UoTiles.Tile> tiles = UoTiles.Items();
                if (it.Graphic < 0 || it.Graphic >= tiles.Count)
                    continue;
                int id = tiles[it.Graphic].Misc & 0xFF;
                foreach (string r in UoWeapons.Rows)
                {
                    if (r.StartsWith(id + "|"))
                    {
                        string[] p = r.Split('|');
                        if (p[4] != "Shield")
                        {
                            w = p;
                            named = p[1];
                        }
                    }
                }
            }
        }

        if (w == null)
            return "no weapon";
        // "NdM" or "NdM+K", averaged.
        string[] plus = w[2].Split('+');
        string[] nd = plus[0].Split('d');
        int n, m, add = 0;
        if (nd.Length != 2 || !int.TryParse(nd[0], out n) || !int.TryParse(nd[1], out m)
            || (plus.Length > 1 && !int.TryParse(plus[1], out add)))
            return named;
        double dice = n * (m + 1) / 2.0 + add;
        int str = BaseStat(st, "strength", st.Str), dex = BaseStat(st, "dexterity", st.Dex), speed = int.Parse(w[3]);
        string[]? foe = Creature(Defender);
        int ar = foe != null ? int.Parse(foe[2]) : 0, wrestle = foe != null ? int.Parse(foe[3]) : 0;
        int skill = SkillOf(st, w[4] == "Bashing" ? MaceSkill : w[4] == "Piercing" ? FencingSkill : SwordsSkill);
        double chance = Math.Max(2, (skill + 500) * 50 / (wrestle + 500)) / 100.0;
        double raw = (SkillOf(st, TacticsSkill) + str * 2 + 500) * dice / 1000, absorb = (ar / 4 + (ar + 1) / 2) / 2.0;
        double hit = Math.Max(1, (raw - absorb) / 2);
        int prod = (dex + 100) * speed, b = prod < 2000 ? 20 : 40000 / prod;
        double swing = (2 * b + 1) * 0.25;
        return (hit * chance / swing).ToString("0.0") + " vs " + (foe != null ? foe[0] : Defender + "?") + "  ("
            + (chance * 100).ToString("0") + "% hit)";
    }
    // The picked caster's picked spell on the player, outside guards, from the 1998 scripts: num * 1d(die) + bonus by
    // circle (spelskil.m:1679-1688) scaled by (50 + Magery) / 100, Magery as the 0-100 level (1693-1706); resisted with
    // Resisting Spells against a difficulty of damage * 25, at most 1000, range 50 (1155-1179; 40 in guarded regions),
    // the chance (skill - difficulty) * 100 / range + 500 in thousandths (OUO skill.c:745, 891); a resist halves it,
    // rounded up.
    string SpellTaken(AssistState st)
    {
        string[]? who = Creature(Caster);
        string[]? sp = SpellRow(Spell);
        if (who == null || sp == null)
            return "pick a caster";
        int magery = int.Parse(who[6])
            / 10, num = int.Parse(sp[2]), die = int.Parse(sp[3]), bonus = int.Parse(sp[4]), resist = SkillOf(st,
            ResistSkill);
        double taken = 0, chance = 0;
        for (int roll = 1; roll <= die; roll++)
        {
            int dmg = (num * roll + bonus) * (50 + magery) / 100, diff = Math.Min(1000, dmg * 25);
            double p = Math.Max(0, Math.Min(1000, (resist - diff) * 100 / 50 + 500)) / 1000.0;
            taken += (dmg * (1 - p) + (dmg + 1) / 2 * p) / die;
            chance += p / die;
        }
        return who[0] + " " + Abbrev(sp[0]) + " " + taken.ToString("0") + " hp, " + (chance * 100).ToString("0")
            + "% resist";
    }
    static string StatValue(AssistState st, string name)
    {
        switch (name)
        {
            case "hits":
                return st.Hits + " / " + st.MaxHits;
            case "mana":
                return st.Mana + " / " + st.MaxMana;
            case "stamina":
                return st.Stam + " / " + st.MaxStam;
            case "strength":
                return st.Str.ToString();
            case "dexterity":
                return st.Dex.ToString();
            case "intelligence":
                return st.Int.ToString();
            case "weight":
                return st.Weight.ToString();
            case "gold":
                return st.Gold.ToString();
            case "armor":
                return st.Armor.ToString();
        }
        return "";
    }

    // Player speech lines since the last tick, less the speakers hidden by name (a human NPC passes the speech test).
    void Chat(AssistState st)
    {
        Set<string> hidden = Set<string>.Of(m_f.Cfg.Get("chat.hidden", "").Split(new[] { '|' },
            StringSplitOptions.RemoveEmptyEntries));
        List<string[]> lines;
        // A reconnect starts a new state whose count restarts below the one seen (2026-10-15 21:45: 145 window faults).
        lock (st.Gate)
        {
            if (m_chatSeen > st.ChatSeq)
                m_chatSeen = 0;
            long first = st.ChatSeq - st.Chat.Count;
            int from = (int)Math.Min(st.Chat.Count, Math.Max(0, m_chatSeen - first));
            lines = st.Chat.GetRange(from, st.Chat.Count - from);
            m_chatSeen = st.ChatSeq;
        }
        if (lines.Count == 0)
            return;
        foreach (string[] l in lines)
            if (!hidden.Contains(l[1]))
                m_chat.Items.Add(l[0] + " " + l[1] + ": " + l[2]);
        while (m_chat.Items.Count > 500)
            m_chat.Items.RemoveAt(0);
        m_chat.TopIndex = Math.Max(0, m_chat.Items.Count - 1);
    }
    void HideSpeaker()
    {
        if (m_chat.SelectedItem == null)
            return;
        string line = (string)m_chat.SelectedItem;
        int sp = line.IndexOf(' '), colon = line.IndexOf(": ");
        if (sp < 0 || colon <= sp)
            return;
        string name = line.Substring(sp + 1, colon - sp - 1);
        m_f.Cfg.Set("chat.hidden", m_f.Cfg.Get("chat.hidden", "") + "|" + name);
        for (int i = m_chat.Items.Count - 1; i >= 0; i--)
        {
            string x = (string)m_chat.Items[i];
            if (x.Length > sp && x.IndexOf(" " + name + ": ") >= 0)
                m_chat.Items.RemoveAt(i);
        }
    }
    static readonly ImmutableArray<string> s_notorietyNames = [ "not seen yet", "innocent (blue)", "friend (green)",
        "attackable (grey)", "criminal (grey)", "enemy (orange)", "murderer (red)", "invulnerable (yellow)" ];
    static readonly ImmutableArray<string> s_facings = [ "N", "NE", "E", "SE", "S", "SW", "W", "NW" ];
    string m_lastTileKey = ""; string m_lastTile = "";
    // Sextant reading, the classic center at Lord British's throne (1323, 1624) on the 5120 by 4096 overworld.
    static string Sextant(int x, int y)
    {
        double lon = (x - 1323) * 360.0 / 5120, lat = (y - 1624) * 360.0 / 4096;
        Func<double, string> dm = v => { v = Math.Abs(v); int d = (int)v; int m = (int)Math.Round((v - d) * 60); if (m
            == 60) { d++; m = 0; } return d + "o" + m.ToString("00") + "'"; };
        return dm(lat) + (lat < 0 ? "N" : "S") + " " + dm(lon) + (lon < 0 ? "W" : "E");
    }
    void Session(GameLink k)
    {
        if (UoArt.Folder == null && m_clientPid != 0)
            UoArt.Folder = AssistNative.ExeFolder(m_clientPid);
        bool uo1998 = !m_f.Server.StartsWith("127.0.0.1:2593");
        // The Circuit tab's shops and towns come from the region file, read here after login: filled again once it is.
        bool hadRegions = UoRegions.Loaded;
        if (uo1998)
            UoRegions.Load(Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "regions-1998.txt"));
        if (!hadRegions && UoRegions.Loaded)
            FillCircuitSetup();
        AssistState st = k.State;
        int x, y, z, face, noto;
        DateTime at;
        bool? guardLine;
        List<int[]> multis = new();
        lock (st.Gate)
        {
            x = st.X;
            y = st.Y;
            z = st.Z;
            face = st.Facing;
            noto = st.Notoriety;
            at = st.LoginAt;
            guardLine = st.Guarded;
            foreach (KeyValuePair<uint, AssistItem> kv in st.Ground)
                if (kv.Value.Graphic >= 0x4000)
                    multis.Add(new[] { kv.Value.Graphic - 0x4000, kv.Value.X, kv.Value.Y });
        }
        List<string> lines = new();
        TimeSpan up = at == default(DateTime) ? TimeSpan.Zero : DateTime.Now - at;
        lines.Add("Logged in "
            + (at == default(DateTime) ? "-" : ((int)up.TotalHours).ToString() + up.ToString(@"\:mm\:ss")) + " on "
            + m_f.Server.TrimEnd('.'));
        lines.Add("At " + x + ", " + y + ", " + z + " facing " + s_facings[face & 7] + "   sextant " + Sextant(x, y));
        string key = x + "," + y;
        if (key != m_lastTileKey)
        {
            m_lastTileKey = key;
            m_lastTile = UoMap.Tile(x, y) ?? "map files not readable";
        }
        lines.Add("Tile: " + m_lastTile);
        bool mapGuarded = false;
        string? zone = uo1998 ? UoRegions.Zone(x, y, z, out mapGuarded) : null;
        string guard = guardLine.HasValue ? (guardLine.Value ? "guarded" : "not guarded")
            : (zone != null ? (mapGuarded ? "guarded (by the region map)" : "not guarded (by the region map)")
            : "guarded: no word from the server yet");
        lines.Add("Zone: " + (zone ?? (uo1998 ? "no region file (regions-1998.txt)" : "not sent by this server"))
            + "   " + guard);
        bool inHouse = false;
        foreach (int[] m in multis)
        {
            int[]? b = UoMap.MultiBox(m[0]);
            if (b != null && x >= m[1] + b[0] && x <= m[1] + b[2] && y >= m[2] + b[1] && y <= m[2] + b[3])
            {
                inHouse = true;
                break;
            }
        }
        lines.Add((inHouse ? "Inside a house" : "Not inside a house") + "   Status: "
            + (noto == 0 ? s_notorietyNames[0] : m_f.NotorietyByte(noto)));
        string text = string.Join("\n", lines);
        m_sessionLine = (zone ?? "") + ", "
            + (guardLine.HasValue ? (guardLine.Value ? "guarded" : "not guarded")
            : (mapGuarded ? "guarded" : "not guarded")) + ", at " + x + "," + y + "," + z + ", "
            + (at == default(DateTime) ? "-" : ((int)up.TotalHours).ToString() + up.ToString(@"\:mm\:ss")) + " online";
        if (m_session.Text != text)
        {
            m_session.Text = text;
            if (m_session.Height != Grown(m_session))
                Fit();
        }
    }
    // The fail-safe's banner, Log tab and the one-time notice in the client's journal (a client-only line, never sent
    // to the server) when it disables the assist.
    long m_logSeen; int m_historySeen; string? m_lastDisabled = null;
    void Guard(GameLink k)
    {
        AssistGuard? g = AssistGuard.Current;
        string? off = g == null ? null : g.Disabled;
        m_banner.Visible = off != null;
        if (off != null)
        {
            string t = "Assist disabled by the fail-safe: " + off + ". Your own play still passes through.";
            if (m_bannerText.Text != t)
                m_bannerText.Text = t;
        }
        m_guardState.Text = off != null ? "DISABLED: " + off
            : "Fail-safe watching; nothing has tripped. Untick a watch below to stop it disabling the assist.";
        if (off != null && off != m_lastDisabled && k != null)
        {
            k.Tell("Assist disabled by the fail-safe: " + off);
            Note("Fail-safe: " + off);
        }
        if (off == null && m_lastDisabled != null)
            Note("Fail-safe: assist resumed");
        m_lastDisabled = off;
        List<string> lines = m_relay.Log.TailSince(ref m_logSeen);
        if (g != null)
            lock (g.History)
            {
                while (m_historySeen < g.History.Count)
                    lines.Add(">> " + g.History[m_historySeen++]);
            }
        if (lines.Count > 0)
        {
            bool atEnd = m_logList.TopIndex
                >= m_logList.Items.Count - m_logList.ClientSize.Height / Math.Max(1, m_logList.ItemHeight) - 1;
            m_logList.BeginUpdate();
            foreach (string l in lines)
                m_logList.Items.Add(l);
            while (m_logList.Items.Count > AssistLog.TailKeep)
                m_logList.Items.RemoveAt(0);
            m_logList.EndUpdate();
            if (atEnd)
                m_logList.TopIndex = Math.Max(0, m_logList.Items.Count - 1);
        }
    }

    // Counted items under their subgroup headings (harvest, create, reagents, combat), two columns each, each with its
    // art from the client's own ART.MUL.
    void Resources()
    {
        if (UoArt.Folder == null && m_clientPid != 0)
            UoArt.Folder = AssistNative.ExeFolder(m_clientPid);
        bool stale = m_f.PackStale();
        if (Readable(m_resCount) && m_resCount.Visible != stale)
            m_resCount.Visible = stale;
        if (!m_res.Visible)
        {
            if (m_resSumPics.Count == 0 || !m_resSumPics[0].Parent!.Visible)
                return;
            List<AssistFeatures.CountRow> counts = m_f.CountRows();
            for (int i = 0; i < s_resSummaryArts.Length; i++)
            {
                int art = s_resSummaryArts[i], n = 0;
                foreach (AssistFeatures.CountRow r in counts)
                    if (Array.IndexOf(r.Graphics, art) >= 0)
                        n += r.Count;
                if (m_resSumPics[i].Image == null)
                    m_resSumPics[i].Image = UoArt.Icon(art);
                string text = s_shortReagent[art] + ": " + n;
                if (m_resSumCounts[i].Text != text)
                    m_resSumCounts[i].Text = text;
            }
            return;
        }
        List<AssistFeatures.CountRow> rows = m_f.CountRows();
        string shape = string.Join(",", rows.ConvertAll(r => r.Name + r.Graphic + r.Group))
            + string.Join("",
            AssistFeatures.ResourceGroups.ConvertAll(g => m_f.Cfg.Get("collapsed.resources." + g, "0")));
        if (shape != m_resShape)
        {
            m_resShape = shape;
            m_res.SuspendLayout();
            m_res.Controls.Clear();
            m_res.RowStyles.Clear();
            m_resLabels.Clear();
            m_resPics.Clear();
            m_resAt.Clear();
            int row = 0, height = 4;
            foreach (string group in AssistFeatures.ResourceGroups)
            {
                List<int> inGroup = new();
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Group == group)
                        inGroup.Add(i);
                if (inGroup.Count == 0)
                    continue;
                // Each subgroup collapses on its heading (Damian, 2026-10-15: "make the subgroups collapsable in
                // resources").
                bool shut = m_f.Cfg.Get("collapsed.resources." + group, "0") == "1";
                string key = group;
                // The heading sits in the first column, level with its first items (Damian, 2026-10-15: "make the group
                // names and the items start on the same row to perserve vertical screen space").
                Label head = new()
                {
                    AutoSize = true,
                    Text = (shut ? "[+] " : "[-] ") + group,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = new Padding(0, shut ? 3 : 8, 0, 0),
                    Cursor = Cursors.Hand
                };
                head.Click += (s, e) => { m_f.Cfg.Set("collapsed.resources." + key,
                    m_f.Cfg.Get("collapsed.resources." + key, "0") == "1" ? "0" : "1"); m_resShape = ""; };
                m_res.Controls.Add(head, 0, row);
                if (shut)
                {
                    m_res.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
                    row++;
                    height += 20;
                    continue;
                }
                int per = group == "reagents" || group == "gems" || group == "potions" ? 3 : 2, span = 6 / per;
                for (int n = 0; n < inGroup.Count; n++)
                {
                    int at = inGroup[n];
                    FlowLayoutPanel cell = new()
                    {
                        WrapContents = false,
                        Height = ResRow,
                        Margin = new Padding(0),
                        Dock = DockStyle.Fill
                    };
                    PictureBox pic = new()
                    {
                        Width = ResIcon,
                        Height = ResIcon,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = UoArt.Icon(rows[at].Graphic),
                        Margin = new Padding(0, 2, 4, 2)
                    };
                    ContextMenuStrip menu = new();
                    menu.Items.Add("Remove " + rows[at].Name, null,
                        (s, e) => { List<AssistFeatures.CountRow> l = m_f.ResourceList(); if (at
                        < l.Count) { l.RemoveAt(at); m_f.SetResources(l); } m_resShape = ""; });
                    foreach (string other in AssistFeatures.ResourceGroups)
                    {
                        if (other == group)
                            continue;
                        string to = other;
                        menu.Items.Add("Move to " + to, null,
                            (s, e) => { List<AssistFeatures.CountRow> l = m_f.ResourceList(); if (at
                            < l.Count) { l[at].Group = to; m_f.SetResources(l); } m_resShape = ""; });
                    }
                    Label label = new()
                    {
                        AutoSize = true,
                        Margin = new Padding(0, 8, 0, 0),
                        ContextMenuStrip = menu
                    };
                    cell.ContextMenuStrip = menu;
                    pic.ContextMenuStrip = menu;
                    cell.Controls.Add(pic);
                    cell.Controls.Add(label);
                    m_res.Controls.Add(cell, 1 + (n % per) * span, row + n / per);
                    m_res.SetColumnSpan(cell, span);
                    m_resLabels.Add(label);
                    m_resPics.Add(pic);
                    m_resAt.Add(at);
                }
                int lines = (inGroup.Count + per - 1) / per;
                for (int r = 0; r < lines; r++)
                    m_res.RowStyles.Add(new RowStyle(SizeType.Absolute, ResRow));
                row += lines;
                height += lines * ResRow;
            }
            m_res.RowCount = row;
            m_res.Height = height;
            m_res.ResumeLayout();
            Fit();
        }
        for (int i = 0; i < m_resAt.Count; i++)
        {
            AssistFeatures.CountRow r = rows[m_resAt[i]];
            string? sn;
            string text = (r.Group == "reagents" && s_shortReagent.TryGet(r.Graphic, out sn) ? sn : r.Name) + "  "
                + r.Count;
            if (m_resLabels[i].Text != text)
                m_resLabels[i].Text = text;
            if (m_resPics[i].Image == null && UoArt.Folder != null)
                m_resPics[i].Image = UoArt.Icon(r.Graphic);
        }
    }
}
