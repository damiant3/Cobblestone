#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the circuit brain (docs/Designs/Active/Apps/AssistCircuitBrain.md) ----------
    // Each step of the tailoring loop asks Decide before its body runs. The expert brain runs every legal step, which
    // is the loop's own order. Every decision point is one row of decisions-<server>.csv; a step's outcome is the next
    // row's state and the time between the two rows. The state columns are the planner's input nodes (Damian,
    // 2026-10-19: "gold in the bank ... sell shirts ... bolts and sew kit ... thread and loom ... (small) gold and
    // mobility. the vendors restocking behavior, timing, weight of thread and gold, etc ... all input nodes").
    int m_circuitPass; bool m_decisionLogFailed; readonly Lock m_decisionGate = new();
    const string DecisionHeader = "time,circuit,pass,step,legal,run,by,other,city,x,y,shirts,thread,cloth,bolts,gold,kits,weight,limit,stam,max_stam,mount_owed,pearl,mandrake,moss,survey,best_offer,due_here,due_cities,restock_min,loom_tiles,bank_open_min,bank_gold,skips,note";
    // The per-move reward's two inputs the pack does not show: the gold in the bank box (the box the player wears on
    // layer 0x1D, its piles known once it has been opened; -1 before), and the vendors passed over so far (SkipVendor
    // and AvoidTailor: a refusal, no answer, a walk that failed, a tailor full).
    int m_vendorSkips;
    int BankGold(GameLink k)
    {
        lock (k.State.Gate)
        {
            uint box = 0;
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Layer == 0x1D && it.Mobile == k.State.Player)
                    box = it.Serial;
            if (box == 0)
                return -1;
            int gold = 0;
            bool seen = false;
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == box)
                {
                    seen = true;
                    if (it.Graphic == GoldArt)
                        gold += Math.Max(1, it.Amount);
                }
            return seen ? gold : -1;
        }
    }
    string DecisionPath => Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!,
        "decisions-" + ServerSafe + ".csv");
    // Suggest (Damian, 2026-10-19: "lets add a suggest feature. what would the circuit do next. it will give me a way
    // to evaluate the decisions step by step before they go off"): a step the circuit would run waits, named with the
    // pack it was decided on, for Do it (runs) or Skip (refused, the next legal step offered); a stop ends the wait.
    // Each answer is a decision row for the brain's training (Damian: "lets make that feed the neural network"):
    // "approve" or "reject".
    public bool Suggest
    {
        get => Cfg.Get("brain.suggest", "0") == "1"; set
        {
            Cfg.Set("brain.suggest", value ? "1" : "0");
            if (!value)
                m_suggestAnswer = 2;
        }
    }
    public volatile string Suggested = ""; volatile int m_suggestAnswer;
    public string? SuggestAnswer(bool doIt)
    {
        if (Suggested.Length == 0)
            return "no step is waiting";
        m_suggestAnswer = doIt ? 1 : 2;
        return null;
    }
    bool Decide(GameLink k, string step, bool legal)
    {
        bool run = legal;
        string by = "expert";
        if (run && Suggest)
        {
            m_suggestAnswer = 0;
            string pack = CircuitPack(), was = CircuitStatus;
            Suggested = step + ": " + pack;
            CircuitStatus = "Suggest: next " + step + " (" + pack + "); Do it or Skip";
            Note("Suggest: next " + step + " (" + pack + ")");
            while (m_suggestAnswer == 0 && Suggest && !circuitStop && !k.Closed)
                Clock.Sleep(100);
            run = m_suggestAnswer == 1 && !circuitStop && !k.Closed;
            Suggested = "";
            CircuitStatus = was;
            by = run ? "approve" : "reject";
            Note("Suggest: " + step + (run ? " done" : " skipped"));
        }
        DecisionRow(k, step, legal, run, by);
        if (run)
            m_lastRun = step;
        return run;
    }
    // Training (Damian, 2026-10-19: "a toggle in the ui that says turns on training. i run the circuit, and when
    // something wonkey happens of I don't like the decision, thats when i break the circuit and comment about the
    // decision and what was wrong or what could be more optimal"): a circuit he stops (his own step, Stop, circuit
    // stop; never an alarm) logs the step it was running as rejected (by "reject"), and Record starts, so his [ notes
    // are Comment rows and his own moves are the better choice; Go ends the critique and the recording it started.
    public bool Training
    {
        get => Cfg.Get("brain.training", "0") == "1";
        set => Cfg.Set("brain.training", value ? "1" : "0");
    }
    string? m_lastRun = null; bool m_critique;
    void Critique(GameLink k)
    {
        if (!Training || !circuitStop || m_circuitAlarm != null || k.Closed)
            return;
        DecisionRow(k, m_lastRun ?? "none", true, false, "reject");
        if (!Recording)
        {
            RecordToggle("training");
            m_critique = true;
        }
        k.Tell("Training: you stopped " + (m_lastRun ?? "the circuit")
            + ". Say [ and what was wrong or better; your next moves are recorded as the better choice. Go resumes.",
            0x0044);
    }
    // state: the state columns taken earlier (a hand move's, StateColumns at the decision), else taken now. note: a
    // Comment row's words.
    bool m_decisionHeaderChecked;
    void DecisionRow(GameLink k, string step, bool legal, bool run, string by, string? state = null, string note = "")
    {
        try
        {
            string circuit = by == "damian" ? "by hand" : CircuitName.Replace(',', ' ');
            int pass = by == "damian" ? 0 : m_circuitPass;
            string line = string.Join(",", Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), circuit, pass,
                step, legal ? 1 : 0, run ? 1 : 0, by, "") + "," + (state ?? StateColumns(k))
                + "," + (note ?? "").Replace(',', ';').Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
            lock (m_decisionGate)
            {
                string path = DecisionPath;
                // A log begun under other columns is kept beside, under its first row's date, and a new one starts.
                if (!m_decisionHeaderChecked && File.Exists(path))
                {
                    string? first;
                    using (StreamReader r = new(path))
                        first = r.ReadLine();
                    if (first != DecisionHeader)
                        File.Move(path,
                            path.Replace(".csv",
                            "-" + File.GetCreationTime(path).ToString("yyyyMMdd-HHmmss") + ".csv"));
                }
                m_decisionHeaderChecked = true;
                bool fresh = !File.Exists(path);
                File.AppendAllText(path, (fresh ? DecisionHeader + "\r\n" : "") + line + "\r\n");
            }
        }
        catch (Exception e) { if (!m_decisionLogFailed) { m_decisionLogFailed = true; Note("Brain: the decision log "
            + "failed, and no more rows are written this session: " + e.Message); } }
    }
    // The input nodes, DecisionHeader's columns from city on.
    string StateColumns(GameLink k)
    {
        {
            int x, y, weight, str, stam, maxStam;
            lock (k.State.Gate)
            {
                x = k.State.X;
                y = k.State.Y;
                weight = k.State.Weight;
                str = k.State.Str;
                stam = k.State.Stam;
                maxStam = k.State.MaxStam;
            }
            string city = UoRegions.City(x, y) ?? "";
            int cloth = 0;
            foreach (int amount in PackCloth(k).Values)
                cloth += amount;
            int bolts = 0;
            foreach (AssistItem it in PackBolts(k))
                bolts += Math.Max(1, it.Amount);
            long owed;
            lock (m_mountGate)
                owed = m_mountOwed;
            // The thread vendors due, by city, and the minutes to the next restock of one bought out.
            DateTime now = Clock.UtcNow.ToLocalTime();
            ValueMap<string, int> due = ValueMap<string, int>.Empty;
            double restock = -1;
            Set<uint> threadOff = ThreadOff();
            foreach (CircuitVendor v in CircuitVendors())
            {
                if (threadOff.Contains(v.Serial))
                    continue;
                string vc = UoRegions.City(v.X, v.Y) ?? "";
                DateTime back = ThreadDueAt(v.Serial);
                if (!Skipped(v.Serial) && now >= back)
                    due = due.Set(vc, due.Get(vc, 0) + 1);
                else if (v.Stock == 0 && back > now && (restock < 0 || (back - now).TotalMinutes < restock))
                {
                    restock = (back - now).TotalMinutes;
                }
            }
            int dueHere = due.Get(city, 0);
            List<string> dueCities = new();
            foreach (KeyValuePair<string, int> kv in due.Ordered(Comparer<string>.Default))
                dueCities.Add(kv.Key.Replace(',', ' ') + ":" + kv.Value);
            AssistItem? loom = NearLoom(k, x, y);
            object[] cols = new object[] {
                city.Replace(',', ' '), x, y,
                PackCount(k, IsShirtArt), PackCount(k, IsThreadArt), cloth, bolts, PackCount(k, g => g == GoldArt),
                PackCount(k, g => g == KitArt),
                weight, str > 0 ? str * 4 + 30 : 0, stam, maxStam, owed, PackCount(k, g => g == 0xF7A),
                PackCount(k, g => g == 0xF86), PackCount(k, g => g == 0xF7B),
                Surveyed ? 1 : 0, BestFreshOffer(), dueHere, string.Join(";", dueCities),
                restock < 0 ? -1 : (int)Math.Ceiling(restock),
                loom == null ? -1 : Tiles(loom.X, loom.Y, x, y),
                m_lastBankOpen == null ? -1 : (int)(Clock.UtcNow - m_lastBankOpenAt).TotalMinutes,
                BankGold(k), m_vendorSkips };
            return string.Join(",", Array.ConvertAll(cols, c => Convert.ToString(c, CultureInfo.InvariantCulture)));
        }
    }
    // Damian's own moves while Record is on and no circuit runs (AssistCircuitBrain.md, "Damian's play"), from what his
    // client sends: a sell reply holding shirts, a buy reply holding thread, a double-click on a loom or a sewing kit,
    // and "bank" said. A sale or a thread buy in another city than his last labelled move is the trip's. Each row
    // carries the state HandSettleMs after his previous move, when he chose this one; a move repeating the last within
    // HandRepeatMs is the same move (a sewing session's many menu picks, a sale in batches of 5).
    const int HandSettleMs = 5000, HandRepeatMs = 120000;
    string? m_handState = null, m_handCity = null, m_handStep = null; DateTime m_handAt; Timer m_handTimer = null!;
    void HandMove(GameLink k, byte[] p)
    {
        if (!Recording || CircuitRunning)
            return;
        string? step = null;
        if (p[0] == 0x9F && p.Length >= 15)
        {
            for (int at = 9; at + 6 <= p.Length; at += 6)
            {
                AssistItem? it = Item(k, U32(p, at));
                if (it != null && IsShirtArt(it.Graphic))
                    step = "Sell";
            }
        }
        else if (p[0] == 0x3B && p.Length >= 15)
        {
            for (int at = 8; at + 7 <= p.Length; at += 7)
            {
                uint s = U32(p, at + 1);
                AssistItem? it = Item(k, s);
                object[]? o;
                m_offers.TryGet(s, out o);
                if (it != null && IsThreadArt(it.Graphic)
                    || o != null && ((string)o[0]).IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0)
                    step = "BuyThread";
            }
        }
        else if (p[0] == 0x06 && p.Length >= 5)
        {
            AssistItem? it = Item(k, U32(p, 1) & 0x7FFFFFFF);
            if (it != null)
                step = s_looms.IndexOf(it.Graphic) >= 0 ? "Weave" : it.Graphic == KitArt ? "Sew" : null;
        }
        else if (p[0] == 0x03 && p.Length > 8)
        {
            string said = Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0').ToLowerInvariant();
            // A [ note is a comment, never a move, whatever words it holds.
            if (!said.TrimStart().StartsWith("[") && Array.IndexOf(said.Split(' ', '.', ',', '!', '?'), "bank") >= 0)
                step = "Bank";
        }
        if (step == null)
            return;
        string city = CityOf(k) ?? "";
        bool moved = m_handCity != null && city != m_handCity;
        if (step == "Sell")
            step = moved ? "SellTrip" : "SellHere";
        else if (step == "BuyThread")
            step = moved ? "ThreadTrip" : "BuyThreadHere";
        DateTime now = Clock.UtcNow;
        if (step == m_handStep && (now - m_handAt).TotalMilliseconds < HandRepeatMs)
        {
            m_handAt = now;
            return;
        }
        DecisionRow(k, step, true, true, "damian", m_handState);
        m_handStep = step;
        m_handAt = now;
        m_handCity = city;
        m_handState = null;
        Timer t = new(_ => { try { m_handState = StateColumns(k); } catch { } }, null, HandSettleMs, Timeout.Infinite);
        Timer was = Interlocked.Exchange(ref m_handTimer, t);
        if (was != null)
            was.Dispose();
    }
    // Each circuit's last end, by its name, for the window's totals card (Damian, 2026-10-18: "add all circuit's
    // summary to totals card").
    public Map<string, string> CircuitEnds = Map<string, string>.Empty; readonly Lock m_circuitEndsGate = new();
}
