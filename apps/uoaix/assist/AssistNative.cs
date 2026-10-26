#nullable enable
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace Uoaix.Assist;

public static class AssistNative
{
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af,
        int cls, uint reserved);
    // MIB_TCPROW_OWNER_PID rows of 24 bytes: state, local address, local port, remote address, remote port, pid; ports
    // in network order in the low 16 bits (TCP_TABLE_OWNER_PID_ALL = 5, AF_INET = 2).
    public static int TcpPid(int localPort, int remotePort)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0)
                return 0;
            int n = Marshal.ReadInt32(buf);
            for (int i = 0; i < n; i++)
            {
                IntPtr row = buf + 4 + i * 24;
                if (Port(Marshal.ReadInt32(row, 8)) == localPort && Port(Marshal.ReadInt32(row, 16)) == remotePort)
                    return Marshal.ReadInt32(row, 20);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return 0;
    }
    static int Port(int v)
    {
        return ((v & 0xFF) << 8) | ((v >> 8) & 0xFF);
    }

    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lp);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point p);
    // Opens a file with its default program (a trip report).
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr hwnd,
        string verb, string file, string args, string dir, int show);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx,
        int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
        WinEventProc proc, int pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    // The visible frame (DWMWA_EXTENDED_FRAME_BOUNDS): GetWindowRect also counts the invisible resize border, about 7
    // pixels a side on Windows 10 and 11, which left a gap between the client and the docked window.
    public static RECT Visible(IntPtr hwnd)
    {
        RECT r;
        if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0)
            return r;
        GetWindowRect(hwnd, out r);
        return r;
    }
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    // The client's title bar: WM_SETTEXT (0x000C) is marshalled across processes for a top-level window.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendText(IntPtr hwnd,
        int msg, IntPtr w, string l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder s,
        int n);
    public static string Title(IntPtr hwnd)
    {
        StringBuilder sb = new(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
    public static void SetTitle(IntPtr hwnd, string text)
    {
        SendText(hwnd, 0x000C, IntPtr.Zero, text);
    }
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h,
        int flags, StringBuilder name, ref int size);
    // The folder of a process's executable (PROCESS_QUERY_LIMITED_INFORMATION), or null.
    public static string? ExeFolder(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero)
            return null;
        try
        {
            StringBuilder sb = new(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? Path.GetDirectoryName(sb.ToString()) : null;
        }
        finally { CloseHandle(h); }
    }

    // The process's largest visible top-level window not owned by a visible window.
    public static IntPtr MainWindow(int pid)
    {
        IntPtr best = IntPtr.Zero;
        long area = 0;
        EnumWindows((h, lp) =>
        {
            int p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h) && (GetWindow(h, 4) == IntPtr.Zero || !IsWindowVisible(GetWindow(h, 4))))
            {
                RECT r;
                GetWindowRect(h, out r);
                long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (a > area)
                {
                    area = a;
                    best = h;
                }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }
}
