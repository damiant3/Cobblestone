// A list whose column header sorts it: a click sorts up, a right-click sorts down.
// With AutoSort the list sorts its own rows on the click (numbers as numbers) and keeps that order through refills
// (Damian, 2026-10-15: "add sorting by column left click asc right click desc"); without it the owner sorts.
#nullable enable
using System;
using System.Collections;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Uoaix.Assist;

public sealed class SortList : ListView
{
    // WM_NOTIFY, whose NMHDR is hwndFrom, idFrom, code; LVM_GETHEADER; NM_RCLICK.
    const int WmNotify = 0x004E;
    const int LvmGetHeader = 0x101F;
    const int NmRClick = -5;

    public int SortColumn = 3;
    public bool Descending = true;
    public bool AutoSort;

    public SortList()
    {
        DoubleBuffered = true;
        ColumnClick += (s, e) =>
        {
            SortColumn = e.Column;
            Descending = false;
            Apply();
        };
    }

    void Apply()
    {
        if (!AutoSort)
            return;
        ListViewItemSorter = new ByColumn(SortColumn, Descending ? -1 : 1);
        Sort();
    }

    sealed class ByColumn : IComparer
    {
        readonly int m_column;
        readonly int m_dir;

        public ByColumn(int column, int dir)
        {
            m_column = column;
            m_dir = dir;
        }

        public int Compare(object? a, object? b)
        {
            string x = a is ListViewItem ia ? Cell(ia) : "";
            string y = b is ListViewItem ib ? Cell(ib) : "";
            int c = double.TryParse(x, out double dx) && double.TryParse(y, out double dy)
                ? dx.CompareTo(dy)
                : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            return c * m_dir;
        }

        string Cell(ListViewItem i)
        {
            return m_column < i.SubItems.Count ? i.SubItems[m_column].Text : "";
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNotify)
        {
            IntPtr from = Marshal.ReadIntPtr(m.LParam);
            int code = Marshal.ReadInt32(m.LParam, IntPtr.Size * 2);
            IntPtr header = AssistNative.SendMessage(Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
            if (from == header && code == NmRClick)
            {
                Point p = PointToClient(Cursor.Position);
                int x = 0;
                for (int i = 0; i < Columns.Count; i++)
                {
                    x += Columns[i].Width;
                    if (p.X < x)
                    {
                        SortColumn = i;
                        Descending = true;
                        break;
                    }
                }
                Apply();
                return;
            }
        }
        base.WndProc(ref m);
    }
}
