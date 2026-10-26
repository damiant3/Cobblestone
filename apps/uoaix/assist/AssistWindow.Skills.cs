#nullable enable
using System.Collections.Immutable;
using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildSkills()
    {
        Panel g = Group("Skills");
        m_skills = Add(g, new SortList { View = View.Details, Height = 220, FullRowSelect = true });
        m_skills.Columns.Add("Skill", 150);
        m_skills.Columns.Add("Shown", 55);
        m_skills.Columns.Add("Base", 55);
        m_skills.Columns.Add("Change", 60);
        m_skills.SortColumn = 2;
        m_skills.Descending = true;  // Damian, 2026-10-18: "skills list default sorting should be descinding by base"
        Add(g, new Label { AutoSize = false, Height = 14,
            Text = "Click a column to sort up, right-click to sort down." });
    }

    // The 1998 server's GetSkillValue blend (OUO skill.c 0x004D5FBB, the UOAIX copy in Skill1998.codex s98-value):
    // shown = base * (1000 - stat) / 1000 + stat, stat = (str * ws + dex * wd + int * wi) * (100 - skillStat) / 1000,
    // in tenths, weights per skill from Skill1998Data.codex. OUO sends a 1.25 client the blended value only; the UOAIX
    // shard sends the base, so there shown and base are the same number.
    static readonly ImmutableArray<int> s_wStr = [ 0, 15, 0, 0, 75, 75, 0, 100, 50, 0, 40, 80, 0, 0, 0, 0, 0, 30, 50,
        0, 65, 0, 0, 0,
        0, 0, 25, 75, 0, 0, 0, 55, 0, 0, 15, 70, 20, 50, 0, 40, 75, 100, 50, 100, 100, 100 ];
    static readonly ImmutableArray<int> s_wDex = [ 50, 15, 0, 0, 15, 25, 0, 0, 50, 50, 30, 20, 50, 40, 40, 50, 0, 30,
        50, 20, 25, 80,
        90, 20, 100, 0, 25, 25, 100, 80, 20, 45, 0, 100, 65, 10, 0, 20, 50, 20, 25, 0, 50, 0, 0, 0 ];
    static readonly ImmutableArray<int> s_wInt = [ 50, 70, 100, 100, 10, 0, 0, 0, 0, 50, 30, 0, 50, 60, 60, 50, 100,
        40, 0, 80, 10,
        20, 10, 80, 0, 100, 50, 0, 0, 20, 80, 0, 100, 0, 20, 20, 80, 30, 50, 40, 0, 0, 0, 0, 0, 0 ];
    static readonly ImmutableArray<int> s_wSkill = [ 90, 100, 100, 100, 100, 90, 100, 90, 80, 100, 50, 75, 85, 50, 100,
        95, 100, 80,
        100, 100, 75, 100, 95, 90, 75, 85, 100, 90, 75, 100, 80, 90, 100, 90, 75, 80, 100, 90, 75, 80, 90, 90, 90, 90,
        80, 80 ];
    bool SentBlended => !m_f.Server.StartsWith("127.0.0.1:2593");
    static int BaseOf(int id, int shown, int str, int dex, int wit)
    {
        if (id < 0 || id >= s_wStr.Length)
            return shown;
        int stat = (str * s_wStr[id] + dex * s_wDex[id] + wit * s_wInt[id]) * (100 - s_wSkill[id]) / 1000;
        if (stat >= 1000)
            return shown;
        return Math.Max(0, (int)Math.Round((shown - stat) * 1000.0 / (1000 - stat)));
    }
    void Skills(AssistState st)
    {
        List<int[]> rows = new();
        Map<int, string> names = Map<int, string>.Empty;
        // Skills 47 to 49 the server sends at 0 are named by neither the client (SKILLS.IDX holds 46) nor the server's
        // skills.txt (46): hidden while 0.
        lock (st.Gate)
        {
            foreach (KeyValuePair<int, int> kv in st.Skills)
            {
                int id = kv.Key - 1;
                int start;
                st.SkillStart.TryGet(kv.Key, out start);
                if (id >= s_skillNames.Length && kv.Value == 0)
                    continue;
                int baseValue = SentBlended ? BaseOf(id, kv.Value, st.Str, st.Dex, st.Int) : kv.Value;
                rows.Add(new[] { kv.Key, kv.Value, baseValue, kv.Value - start });
                names = names.Set(kv.Key, id >= 0 && id < s_skillNames.Length ? s_skillNames[id] : "skill " + kv.Key);
            }
        }

        int col = m_skills.SortColumn;
        int dir = m_skills.Descending ? -1 : 1;
        rows.Sort((a, b) =>
        {
            int d = col == 0 ? string.CompareOrdinal(names[a[0]], names[b[0]]) : a[col].CompareTo(b[col]);
            return d != 0 ? d * dir : string.CompareOrdinal(names[a[0]], names[b[0]]);
        });
        List<string[]> all = new();
        foreach (int[] r in rows)
            all.Add(new[] { names[r[0]]!, (r[1] / 10.0).ToString("0.0"), (r[2] / 10.0).ToString("0.0"),
                (r[3] / 10.0).ToString("+0.0;-0.0;0") });
        // Each number column's total in its header (Damian, 2026-10-22: "those two columns for shown and base should
        // have a secondary header column with sum at the top, and change too").
        int shownSum = 0, baseSum = 0, changeSum = 0;
        foreach (int[] r in rows)
        {
            shownSum += r[1];
            baseSum += r[2];
            changeSum += r[3];
        }
        string[] heads = { "Shown " + (shownSum / 10.0).ToString("0.0"), "Base " + (baseSum / 10.0).ToString("0.0"),
            "Change " + (changeSum / 10.0).ToString("+0.0;-0.0;0") };
        for (int c = 0; c < 3; c++)
            if (m_skills.Columns[c + 1].Text != heads[c])
                m_skills.Columns[c + 1].Text = heads[c];
        // An EndUpdate repaints the whole grid, so a tick with no changed cell leaves it alone (Damian, 2026-10-16:
        // "the skills grid visibly refreshes about every half second and its distracting").
        bool same = m_skills.Items.Count == all.Count;
        for (int i = 0; same && i < all.Count; i++)
            for (int c = 0; c < 4; c++)
                if (m_skills.Items[i].SubItems[c].Text != all[i][c])
                {
                    same = false;
                    break;
                }
        if (same)
            return;
        m_skills.BeginUpdate();
        while (m_skills.Items.Count > all.Count)
            m_skills.Items.RemoveAt(m_skills.Items.Count - 1);
        for (int i = 0; i < all.Count; i++)
        {
            if (i >= m_skills.Items.Count)
                m_skills.Items.Add(new ListViewItem(all[i]));
            else
                for (int c = 0; c < 4; c++)
                    if (m_skills.Items[i].SubItems[c].Text != all[i][c])
                        m_skills.Items[i].SubItems[c].Text = all[i][c];
        }
        m_skills.EndUpdate();
    }
}
