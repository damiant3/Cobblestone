#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

// The fail-safe (Damian, 2026-10-15: "if errors appear to happen, stop doing the proxy behaviors. run clean unless
// everything seems nominal. we want this to detect subtle errors in configuration and not spam the server with bad
// requests"). It watches the relay log and the server's packets; a watch that trips disables every assist action
// (GameLink.Inject refuses, a potion double-click passes through) and the relay keeps relaying the player's own play.
// The disablement is kept in the settings until the player resumes; a watch can be turned off ("guard.off.<id>").
// Server refusals count only within InjectWindowMs of an assist injection, so the player's own refusals do not trip it.
public sealed class AssistGuard
{
    static AssistGuard? s_current;
    public static AssistGuard? Current { get => s_current; set => s_current = value; }
    static readonly IClock s_clock = SystemClock.Instance;
    public const int InjectWindowMs = 3000, CloseWindowMs = 10000, MoveWindowMs = 1000;
    public sealed class Watch
    {
        public string Id = null!, Text = null!; public int Limit, Seconds;
    }
    static readonly ImmutableArray<Watch> s_watches = [
        new() { Id = "desync", Text = "the relay lost packet framing", Limit = 1, Seconds = 60 },
        new() { Id = "error", Text = "a relay error", Limit = 1, Seconds = 60 },
        new() { Id = "unanswered", Text = "the server did not answer an assist action", Limit = 3, Seconds = 120 },
        new() { Id = "refusal", Text = "the server refused assist actions", Limit = 4, Seconds = 60 },
        new() { Id = "lift-reject", Text = "the server rejected assist lifts", Limit = 3, Seconds = 60 },
        new() { Id = "move-reject", Text = "the server rejected moves right after assist actions", Limit = 3,
        Seconds = 30 },
        new() { Id = "disconnect", Text = "the server dropped the connection right after an assist action", Limit = 1,
        Seconds = 60 },
    ];
    public static ImmutableArray<Watch> Watches => s_watches;
    // Refusal lines (RunUO and OUO system text, lower case).
    static readonly ImmutableArray<string> s_refusalText = [ "you must wait", "too far away", "cannot reach",
        "can't reach",
        "out of range", "not in line of sight", "cannot pick that up", "can't pick that up", "that is locked",
        "you can't do that" ];
    readonly AssistSettings m_cfg; readonly AssistLog m_log;
    Map<string, Queue<DateTime>> m_hits = Map<string, Queue<DateTime>>.Empty; readonly Lock m_hitsGate = new();
    public readonly List<string> History = new();
    DateTime m_lastInject = DateTime.MinValue;
    public volatile string? Disabled = null;
    public AssistGuard(AssistSettings cfg, AssistLog log)
    {
        m_cfg = cfg;
        m_log = log;
        string kept = cfg.Get("guard.disabled", "");
        if (kept.Length > 0)
        {
            Disabled = kept + " (kept from an earlier session)";
            History.Add(s_clock.UtcNow.ToLocalTime().ToString("HH:mm:ss") + " still disabled: " + kept);
        }
        log.Lined += Observe;
    }
    public bool Watching(string id)
    {
        return m_cfg.Get("guard.off." + id, "0") != "1";
    }
    public void SetWatching(string id, bool on)
    {
        m_cfg.Set("guard.off." + id, on ? "0" : "1");
    }
    public void Resume()
    {
        lock (m_hitsGate)
            m_hits = m_hits.Clear();
        Disabled = null;
        m_cfg.Set("guard.disabled", "");
        lock (History)
            History.Add(s_clock.UtcNow.ToLocalTime().ToString("HH:mm:ss") + " resumed by the player");
        m_log.Line(0, "--", "GUARD resumed by the player");
    }
    bool RecentInject(int ms)
    {
        return (s_clock.UtcNow - m_lastInject).TotalMilliseconds <= ms;
    }
    // A refused assist step is the walker's to route around (AssistFeatures.WalkTo), not a fault.
    DateTime m_lastStep = DateTime.MinValue;
    void Observe(string line)
    {
        if (line.Contains(" INJ C>S 02"))
        {
            m_lastInject = m_lastStep = s_clock.UtcNow;
            return;
        }
        if (line.Contains(" INJ C>S "))
        {
            m_lastInject = s_clock.UtcNow;
            return;
        }
        if (line.Contains(" GUARD "))
            return;
        if (line.Contains(" -- HALT "))
            Hit("desync", line);
        else if (line.Contains(" -- ERROR "))
            Hit("error", line);
        else if (line.Contains(" -- UNANSWERED "))
            Hit("unanswered", line);
        // Only a connection the server ended (Damian, 2026-10-16: "legit logout is being interpreted as server angry":
        // a logout is the client's close, and the relay's own close of the server side then reads as an abort).
        else if (line.EndsWith(" -- ENDED BY SERVER") && RecentInject(CloseWindowMs))
            Hit("disconnect", line);
    }
    // Server packets: 0x27 lift reject, 0x21 move reject, 0x1C system refusal text.
    // A step refused while stamina is 0 is the server's answer to the player's own running ("You are too fatigued to
    // move.", uo1998 16:11:49 with stamina 0 of 68, after an autopickup lift), not a fault of the assist: such a 0x21
    // is not counted. Stamina from 0xA3 (serial, max, current) and the 0x11 status (current at 50).
    volatile int m_stamina = -1;
    public void Server(byte[] p)
    {
        if (p[0] == 0xA3 && p.Length >= 9)
            m_stamina = (p[7] << 8) | p[8];
        else if (p[0] == 0x11 && p.Length >= 52 && p[42] >= 1)
            m_stamina = (p[50] << 8) | p[51];
        if (!RecentInject(InjectWindowMs))
            return;
        // Out of range (1) and out of sight (2) say where the player stood, not that the assist is wrong: autopickup
        // riding past reagents tripped the watch with two of each (uo1998 2026-10-17 14:28:04). Every other reason
        // counts.
        if (p[0] == 0x27)
        {
            if (p.Length < 2 || (p[1] != 1 && p[1] != 2))
                Hit("lift-reject", "server 0x27 lift reject, reason " + (p.Length >= 2 ? p[1].ToString("X2") : "?"));
        }
        // A step into a mobile is refused too (uo1998 16:46:41, a mobile on the tile ahead, 1.4 s after a pickup): only
        // a refusal within MoveWindowMs of an assist action counts.
        else if (p[0] == 0x21)
        {
            if (m_stamina != 0 && RecentInject(MoveWindowMs)
                && (s_clock.UtcNow - m_lastStep).TotalMilliseconds > MoveWindowMs)
                Hit("move-reject", "server 0x21 move reject");
        }
        else if (p[0] == 0x1C && p.Length > 44)
        {
            uint who = ((uint)p[3] << 24) | ((uint)p[4] << 16) | ((uint)p[5] << 8) | p[6];
            if (who != 0xFFFFFFFF)
                return;
            string text = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0').ToLowerInvariant();
            // A running loop paces itself on "you must wait" (AssistFeatures.Refused), so that line is the loop's to
            // handle.
            if (AssistFeatures.Pacing && text.Contains("you must wait"))
                return;
            foreach (string r in s_refusalText)
                if (text.Contains(r))
                {
                    Hit("refusal", "server said \"" + text + "\"");
                    break;
                }
        }
    }
    void Hit(string id, string evidence)
    {
        Watch? w = null;
        foreach (Watch x in s_watches)
            if (x.Id == id)
                w = x;
        if (w == null)
            return;
        int n;
        lock (m_hitsGate)
        {
            Queue<DateTime>? q;
            if (!m_hits.TryGet(id, out q))
                m_hits = m_hits.Set(id, q = new Queue<DateTime>());
            q.Enqueue(s_clock.UtcNow);
            while (q.Count > 0 && (s_clock.UtcNow - q.Peek()).TotalSeconds > w.Seconds)
                q.Dequeue();
            n = q.Count;
        }
        string stamp = s_clock.UtcNow.ToLocalTime().ToString("HH:mm:ss");
        lock (History)
        {
            History.Add(stamp + " " + id + " " + n + "/" + w.Limit + ": " + evidence);
            if (History.Count > 200)
                History.RemoveAt(0);
        }
        if (n < w.Limit || Disabled != null)
            return;
        if (!Watching(id))
        {
            lock (History)
                History.Add(stamp + " " + id + " would disable the assist, but that watch is off");
            return;
        }
        Disabled = w.Text + " (" + n + " in " + w.Seconds + " s): " + evidence;
        m_cfg.Set("guard.disabled", Disabled);
        m_log.Line(0, "--", "GUARD disabled the assist: " + Disabled);
    }
}
