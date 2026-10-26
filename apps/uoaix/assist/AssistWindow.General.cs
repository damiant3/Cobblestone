#nullable enable
using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildGeneral()
    {
        // Automatic: what the assist does with no input. Display: what the UO window's title bar shows (Damian,
        // 2026-10-15: "the display option, can it add things to uo window titlebar? having display configs affect
        // status in uoaixassist is unnecessary with the collapser").
        Panel g = Group("General");
        m_generalGroup = g;
        TabControl tabs = Add(g, new TabControl { Height = GeneralTabsH });
        TabPage auto = new("Automatic");
        TabPage disp = new("Display");
        TabPage snd = new("Sounds");
        tabs.TabPages.Add(disp);
        tabs.TabPages.Add(auto);
        tabs.TabPages.Add(snd);  // Display first, and so the default (Damian, 2026-10-18)
        // Sounds kept from the client, by group, in two columns; the tab grows to hold them while it is the one shown.
        // The first line says what a check does (Damian, 2026-10-18: "tell me in the tabpage what the config is turning
        // on or off... sounds or supressions").
        FieldGrid sgrid = new(m_tipper, Fit)
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(3)
        };
        snd.Controls.Add(sgrid);
        sgrid.Head("Checked = muted: the client never hears that sound. Unchecked = it plays.");
        foreach (AssistFeatures.SoundGroup group in AssistFeatures.SoundGroups)
        {
            string name = group.Name;
            CheckBox c = sgrid.Cell(new CheckBox { Text = "Mute " + name, AutoSize = true,
                Checked = m_f.SoundOff(name) });
            c.CheckedChanged += (s, e) => m_f.SetSoundOff(name, c.Checked);
            m_soundBoxes.Add(Tuple.Create(group, c));
        }
        m_autoFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false };
        auto.Controls.Add(m_autoFlow);
        m_autoFlow.Controls.Add(new Label { AutoSize = false, Height = 46,
            Text = "Potion drinking: double-click a potion with no hand free and the weapon goes to the pack, you "
            + "drink, and it comes back. Nothing to set up." });
        m_drink = new Label { AutoSize = false, Height = 18 };
        m_autoFlow.Controls.Add(m_drink);
        CheckBox cure = new()
        {
            Text = "Cure poison: drink a cure potion, else cast Cure",
            AutoSize = false,
            Height = 22,
            Checked = m_f.AutoCure
        };
        m_tipper.SetToolTip(cure,
            "While you are poisoned: a cure potion from the pack first (the weapon goes to the pack and back if no "
            + "hand is free), then Cure cast at you if the potion did not cure you or there is none. Cast again only "
            + "on a fizzle or \"You have failed to cure\"; anything else stops it, and Events says why.");
        cure.CheckedChanged += (s, e) => m_f.AutoCure = cure.Checked;
        m_autoFlow.Controls.Add(cure);
        CheckBox castHands = new()
        {
            Text = "Casting: disarm for the spell, re-arm when it is done",
            AutoSize = false,
            Height = 22,
            Checked = m_f.CastHands
        };
        m_tipper.SetToolTip(castHands,
            "Cast any spell (Recall, Heal, Cure, ...) with a weapon or shield in hand: they go to the pack, the spell "
            + "is cast, and when it is done (you aim it, it fizzles or is refused, or it asks for no target) they "
            + "come back to your hands. A spellbook in hand stays.");
        castHands.CheckedChanged += (s, e) => m_f.CastHands = castHands.Checked;
        m_autoFlow.Controls.Add(castHands);
        FieldGrid dgrid = new(m_tipper, Fit)
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(3)
        };
        disp.Controls.Add(dgrid);
        dgrid.Head("In the UO window's title bar:");
        foreach (string n in AssistFeatures.DisplayNames)
        {
            string name = n;
            CheckBox c = dgrid.Cell(new CheckBox { Text = n, AutoSize = true, Checked = m_f.Shown(n) });
            c.CheckedChanged += (s, e) => m_f.Show(name, c.Checked);
            m_display = m_display.Set(n, c);
        }
        CheckBox banks = dgrid.Wide(new CheckBox { Text = "8x8 bank corners (runes in your client only)",
            AutoSize = true, Checked = m_f.ShowBanks });
        banks.CheckedChanged += (s, e) => { m_f.ShowBanks = banks.Checked; ThreadPool.QueueUserWorkItem(_ =>
            m_f.UpdateBanks(UoMap.LandZ)); };
        // Each tab page as tall as its grid at the tab's width, the Automatic page its fixed height.
        // The page holds the grid below the tab strip (the first tab's rectangle, 24 px before the control has a
        // handle), inside the page's 3 px padding and the control's borders.
        Action sizeTabs = () =>
        {
            FieldGrid? grid = tabs.SelectedTab == snd ? sgrid : tabs.SelectedTab == disp ? dgrid : null;
            int strip = tabs.IsHandleCreated && tabs.TabCount > 0 ? tabs.GetTabRect(0).Bottom : 24;
            int rows = 0;
            if (tabs.SelectedTab == auto)
                foreach (Control c in m_autoFlow.Controls)
                    rows += c.Height + c.Margin.Vertical;
            int h = grid != null ? strip + grid.Arrange(tabs.Width - 14) + 16
                : tabs.SelectedTab == auto ? strip + rows + 16 : GeneralTabsH;
            if (tabs.Height != h)
                tabs.Height = h;
        };
        tabs.SelectedIndexChanged += (s, e) => { sizeTabs(); Fit(); };
        tabs.Resize += (s, e) => sizeTabs();
    }

    // The Sounds tab (General): each group's checkbox, its tooltip the sound files in it and how often they played.
    const int GeneralTabsH = 92;
    readonly List<Tuple<AssistFeatures.SoundGroup, CheckBox>> m_soundBoxes = new();
    Map<CheckBox, string> m_soundTips = Map<CheckBox, string>.Empty;
    void ShowSounds()
    {
        foreach (Tuple<AssistFeatures.SoundGroup, CheckBox> t in m_soundBoxes)
        {
            List<string> names = new();
            foreach (int id in t.Item1.Ids)
            {
                string? n = UoSounds.Name(id);
                if (n != null && !names.Contains(n))
                    names.Add(n);
            }
            string tip = "Keep these sounds from the client: " + string.Join(", ", names) + ".\nPlayed "
                + m_f.Heard(t.Item1) + " times this session.";
            string? was;
            if (!m_soundTips.TryGet(t.Item2, out was) || was != tip)
            {
                m_soundTips = m_soundTips.Set(t.Item2, tip);
                m_tipper.SetToolTip(t.Item2, tip);
            }
        }
    }

    Map<string, CheckBox> m_display = Map<string, CheckBox>.Empty;
}
