#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildMaker()
    {
        // Smithing value (Damian, 2026-10-15: "calculate the best ingot efficient item to make and sell the npcs based
        // on smithy skill. you should add this to shops feature"): per item its ingots, builds and their quality at
        // about your skill, what NPCs pay by quality, what you sold, and the expected gold an ingot turns into; best
        // first.
        Panel g = Group("Smithing value");
        m_smithBest = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });
        m_smithRows = Add(g,
            new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Item:150", "Ingots:50", "Builds:50", "Exceptional:75", "Failed:50",
            "NPC pays:65", "Exc. pays:65", "Sold:90", "Gold per ingot:90", "Based on:170" })
        {
            string[] a = c.Split(':');
            m_smithRows.Columns.Add(a[0], int.Parse(a[1]));
        }

        // Tailoring value: the smithing value's table in cloth, with each vendor's demand from its latest sell list.
        g = Group("Tailoring value");
        m_tailorBest = Add(g, new Label { Height = 52, Font = new Font(Font, FontStyle.Bold) });
        m_tailorRows = Add(g,
            new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Item:120", "Cloth:45", "Builds:50", "Exceptional:75", "Failed:50", "NPC pays:65",
            "Exc. pays:65", "Sold:90", "Gold per cloth:90", "Vendors want:200", "Based on:170" })
        {
            string[] a = c.Split(':');
            m_tailorRows.Columns.Add(a[0], int.Parse(a[1]));
        }

        // Every other craft's value table, the craft picked (Damian, 2026-10-18: "expand it to cover all other skills
        // too").
        g = Group("Other crafts value");
        m_craftPick = Grid(g).Line("Craft", Pick(120));
        foreach (AssistFeatures.CraftKind c in m_f.Crafts)
            m_craftPick.Items.Add(c.Name);
        m_craftPick.SelectedIndex = Math.Max(0,
            Array.FindIndex(m_f.Crafts, c => c.Name == m_f.Cfg.Get("maker.craft", "Carpentry")));
        m_craftPick.SelectedIndexChanged += (s, e) => { m_f.Cfg.Set("maker.craft",
            m_f.Crafts[m_craftPick.SelectedIndex].Name); m_craftShape = ""; FillCraft(); };
        m_craftBest = Add(g, new Label { Height = 36, Font = new Font(Font, FontStyle.Bold) });
        m_craftRows = Add(g,
            new SortList { View = View.Details, Height = 140, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        foreach (string c in new[] { "Item:150", "Takes:50", "Builds:50", "Exceptional:75", "Failed:50", "NPC pays:65",
            "Sold:90", "Gold per unit:90", "Based on:170" })
        {
            string[] a = c.Split(':');
            m_craftRows.Columns.Add(a[0], int.Parse(a[1]));
        }
    }


    // Best first by gold per cloth among the items a vendor still wants (Damian, 2026-10-16: "i sometimes sell the
    // vendor down and i can't get all the sales i need there, so need more than smithy"): the best item, then the next
    // best for when its vendors are full.
    Label m_tailorBest = null!; SortList m_tailorRows = null!; string m_tailorShape = "";
    void FillTailor()
    {
        if (m_f.Tailor == null || m_tailorRows == null)
            return;
        int skill = 0;
        GameLink k = m_f.Link;
        if (k != null)
            lock (k.State.Gate)
                k.State.Skills.TryGet(AssistFeatures.TailoringSkill + 1, out skill);
        List<SmithRow> rows = m_f.Tailor.Rows(skill);
        string shape = skill + "|"
            + string.Join(",",
            rows.ConvertAll(r =>
            r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.PriceExceptional + r.Sold + r.Demand));
        if (shape == m_tailorShape)
            return;
        m_tailorShape = shape;
        m_tailorRows.BeginUpdate();
        m_tailorRows.Items.Clear();
        foreach (SmithRow r in rows)
        {
            m_tailorRows.Items.Add(new ListViewItem(new[] { r.Item, r.Cost.ToString(), r.Builds.ToString(),
                r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "",
                r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "",
                r.PriceExceptional > 0 ? r.PriceExceptional.ToString() : "",
                r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "",
                r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price",
                r.Demand.Length > 0 ? r.Demand : "no sell list in " + SmithBook.DemandHours + " h", r.Basis }));
        }
        m_tailorRows.EndUpdate();
        List<SmithRow> priced = rows.FindAll(r => r.PerIngot > 0);
        List<SmithRow> wanted = priced.FindAll(r => r.Wanted > 0);
        string at = "At tailoring " + (skill / 10.0).ToString("0.0") + ": ";
        if (rows.Count == 0)
        {
            m_tailorBest.Text = "No tailoring menus seen yet: open the sewing kit's menus once to learn the cloth "
                + "costs.";
        }
        else if (priced.Count == 0)
        {
            m_tailorBest.Text = "No NPC price known yet: show a tailor your tailored items (\"vendor sell\") to learn "
                + "what they pay.";
        }
        else
        {
            string Line(SmithRow r)
            {
                return r.Item + ", " + r.PerIngot.ToString("0.00") + " gold per cloth (" + r.Cost + " cloth"
                    + (r.Demand.Length > 0 ? "; wanted: " + r.Demand : "") + ")";
            }
            SmithRow best = wanted.Count > 0 ? wanted[0] : priced[0];
            SmithRow? next = (wanted.Count > 0 ? wanted : priced).Find(r => r != best);
            m_tailorBest.Text = at + "make " + Line(best) + "."
                + (wanted.Count == 0
                ? " No vendor's sell list in " + SmithBook.DemandHours
                + " h shows demand: open one with the goods in your pack." : "")
                + (next != null ? "\nWhen those vendors are full: " + Line(next) + "." : "")
                + (priced[0] != best
                ? "\n" + priced[0].Item + " pays more per cloth (" + priced[0].PerIngot.ToString("0.00")
                + ") but no vendor seen wants more." : "");
        }
    }


    ComboBox m_craftPick = null!; Label m_craftBest = null!; SortList m_craftRows = null!; string m_craftShape = "";
    void FillCraft()
    {
        if (m_craftRows == null || m_craftPick.SelectedIndex < 0)
            return;
        AssistFeatures.CraftKind c = m_f.Crafts[m_craftPick.SelectedIndex];
        if (c.Book == null)
        {
            m_craftBest.Text = "Log in to read the " + c.Name + " book.";
            return;
        }
        int skill = 0;
        GameLink k = m_f.Link;
        if (k != null)
            lock (k.State.Gate)
                k.State.Skills.TryGet(c.Skill + 1, out skill);
        List<SmithRow> rows = c.Book.Rows(skill);
        string shape = c.Name + skill + "|"
            + string.Join(",",
            rows.ConvertAll(r => r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.Sold));
        if (shape == m_craftShape)
            return;
        m_craftShape = shape;
        m_craftRows.BeginUpdate();
        m_craftRows.Items.Clear();
        foreach (SmithRow r in rows)
        {
            m_craftRows.Items.Add(new ListViewItem(new[] { r.Item, r.Cost + " " + c.Unit + (r.Cost == 1 ? "" : "s"),
                r.Builds.ToString(), r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "",
                r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "", r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "",
                r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price", r.Basis }));
        }

        m_craftRows.EndUpdate();
        SmithRow? best = rows.Find(r => r.PerIngot > 0);
        m_craftBest.Text = rows.Count == 0
            ? "No " + c.Name.ToLowerInvariant() + " menu with a cost seen yet: open the tool's menus once ("
            + string.Join(", ", c.Tools) + ")."
            : best == null
            ? "No NPC price known yet: show a vendor your " + c.Name.ToLowerInvariant()
            + " goods (\"<name> sell\") to learn what it pays."
            : "Best at " + c.Name.ToLowerInvariant() + " " + (skill / 10.0).ToString("0.0") + ": " + best.Item + ", "
            + best.PerIngot.ToString("0.00") + " gold per " + c.Unit + " (" + best.Basis + ").";
    }
    Label m_smithBest = null!; SortList m_smithRows = null!; string m_smithShape = "";
    void FillSmith()
    {
        if (m_f.Smith == null || m_smithRows == null)
            return;
        int skill = 0;
        GameLink k = m_f.Link;
        if (k != null)
            lock (k.State.Gate)
                k.State.Skills.TryGet(AssistFeatures.SmithySkill + 1, out skill);
        List<SmithRow> rows = m_f.Smith.Rows(skill);
        string shape = skill + "|"
            + string.Join(",",
            rows.ConvertAll(r => r.Item + r.Builds + r.Exceptional + r.Failed + r.PriceNormal + r.PriceExceptional
            + r.Sold));
        if (shape == m_smithShape)
            return;
        m_smithShape = shape;
        m_smithRows.BeginUpdate();
        m_smithRows.Items.Clear();
        foreach (SmithRow r in rows)
        {
            int ok = r.Builds - r.Failed;
            ListViewItem it = new(new[] { r.Item, r.Cost.ToString(), r.Builds.ToString(),
                r.Builds > 0 ? (100 * r.Exceptional / r.Builds) + "%" : "",
                r.Builds > 0 ? (100 * r.Failed / r.Builds) + "%" : "",
                r.PriceNormal > 0 ? r.PriceNormal.ToString() : "",
                r.PriceExceptional > 0 ? r.PriceExceptional.ToString() : "",
                r.Sold > 0 ? r.Sold + " for " + r.SoldGold : "",
                r.PerIngot > 0 ? r.PerIngot.ToString("0.00") : "no NPC price", r.Basis });
            m_smithRows.Items.Add(it);
        }
        m_smithRows.EndUpdate();
        SmithRow? best = rows.Find(r => r.PerIngot > 0);
        m_smithBest.Text = rows.Count == 0
            ? "No smithing menus seen yet: open the hammer's menus once to learn the ingot costs."
            : best == null
            ? "No NPC price known yet: show a blacksmith your smithed items (\"vendor sell\") to learn what they pay."
            : "Best at smithing " + (skill / 10.0).ToString("0.0") + ": " + best.Item + ", "
            + best.PerIngot.ToString("0.00") + " gold per ingot (" + best.Cost + " ingots, " + best.Basis + ").";
        m_smithBest.Text += "\nFor skill gain ("
            + (m_f.SmithLadderMode == "guess" ? "the server formula's best guess" : "your ladder") + "): "
            + (m_f.SmithNow(skill) ?? "none") + ".";
    }
}
