#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- arm and disarm ----------
    // Three toggles (Damian, 2026-10-16: "there should be two buttons, disarm weapon, and disarm both", then "for
    // completeness do all 3 options"): "both" hands, the "weapon" (any held item that is not a shield) or the "shield".
    // Each takes off what it covers and remembers it, and with nothing of its kind in hand puts the remembered set
    // back. Every lift waits out the server's busy time (HarvestGap): uo1998 refused a second-hand lift 829 ms after
    // the first with reason 0xFF (relay log 2026-10-16 16:21:02).
    static readonly ImmutableArray<string> s_armKinds = [ "both", "weapon", "shield" ];
    public static ImmutableArray<string> ArmKinds => s_armKinds;
    public string? ArmToggle()
    {
        return ArmToggle("both");
    }
    public string? ArmToggle(string which)
    {
        string? was = s_busyTask;
        s_busyTask = "arm";
        try
        {
            return ArmToggleRun(which);
        }
        finally { s_busyTask = was; }
    }
    string? ArmToggleRun(string which)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        if (s_armKinds.IndexOf(which) < 0)
            return "arm takes both, weapon or shield";
        using (Act(k))
        {
            uint pack;
            uint me;
            lock (k.State.Gate)
            {
                pack = k.State.Backpack;
                me = k.State.Player;
            }
            if (pack == 0)
                return "the backpack is not known yet";
            string key = Server + "arm.set." + (which == "both" ? "" : which + ".") + me.ToString("X");
            List<AssistItem> held = new();
            foreach (int layer in new[] { 1, 2 })
            {
                AssistItem? h = Hand(k, layer);
                if (h != null && (which == "both" || (which == "shield") == IsShield(h.Graphic)))
                    held.Add(h);
            }
            if (held.Count > 0)
            {
                StringBuilder set = new();
                foreach (AssistItem h in held)
                    set.Append(h.Serial.ToString("X") + ":" + h.Layer + ",");
                Cfg.Set(key, set.ToString().TrimEnd(','));
                foreach (AssistItem h in held)
                {
                    HarvestGap();
                    Sequence = "Disarm: lift " + h.Graphic.ToString("X4") + " from layer " + h.Layer;
                    Spot? from = Where(k, h.Serial);
                    string? why = Lifted(k, h.Serial, 1);
                    if (why != null)
                        return Stopped("disarm", h.Serial, why);
                    Sequence = "Disarm: put " + h.Graphic.ToString("X4") + " in the backpack";
                    why = k.Drop(h.Serial, pack);
                    if (why != null)
                        return Stopped("disarm", h.Serial, why);
                    Answered(k, AnswerMs);
                    why = Landed(k, h.Serial, from!, "disarm");
                    if (why != null)
                        return why;
                }
                Sequence = "Disarmed";
                Note(Sequence);
                return null;
            }
            string remembered = Cfg.Get(key, "");
            if (remembered.Length == 0)
                return which == "both" ? "no weapon set is remembered: disarm once to remember it"
                    : "no " + which + " is remembered: disarm it once to remember it";
            foreach (string part in remembered.Split(','))
            {
                string[] f = part.Split(':');
                uint s = Convert.ToUInt32(f[0], 16);
                int layer = int.Parse(f[1]);
                if (Item(k, s) == null)
                    return "the remembered item " + s.ToString("X") + " is not in the pack";
                HarvestGap();
                Sequence = "Arm: lift " + s.ToString("X");
                Spot? from = Where(k, s);
                string? why = Lifted(k, s, 1);
                if (why != null)
                    return Stopped("arm", s, why);
                Sequence = "Arm: equip " + s.ToString("X") + " on layer " + layer;
                why = k.Equip(s, layer, me);
                if (why != null)
                    return Stopped("arm", s, why);
                Answered(k, AnswerMs);
                why = Landed(k, s, from!, "arm");
                if (why != null)
                    return why;
            }
            Sequence = "Armed";
            Note(Sequence);
            return null;
        }
    }
    string Stopped(string what, uint serial, string why)
    {
        AssistItem? it = m_link != null ? Item(m_link, serial) : null;
        string where = it == null ? "lifted, held by the server"
            : it.Layer >= 0 ? "in your hand (layer " + it.Layer + ")" : "in container " + it.Container.ToString("X");
        Sequence = what + " stopped: " + why + "; the item " + serial.ToString("X") + " is " + where;
        Note(Sequence);
        return Sequence;
    }

    // ---------- drinking without hotkeys ----------
    // A double-click on a drinkable potion with no hand free (RunUO BasePotion.HasFreeHand: a weapon on the two-handed
    // layer fills both hands) is held: the weapon goes to the pack, the held double-click goes out, the weapon comes
    // back.
    // ---------- 8x8 resource bank corners ----------
    // Damian, 2026-10-15: "it would be nice to show the 8x8 corners for resource harvesting in the client somehow, like
    // we did we runes for zones in uoaix server". A recall rune (the shard's zone marker art, GmOverlay.codex gmo-art)
    // on the first tile of each 8x8 bank around the player, sent to the client only: serials 0x7FF00000 | bankX << 10 |
    // bankY, above every server item. 16 banks (4 by 4) around the player's own, all within the client's 18-tile view.
    // Gears (1.25 0x1053), a small round tinker part seldom seen on the ground, not a recall rune (Damian, 2026-10-18:
    // "something small on screen not a rune or other overloaded frequently used game item").
    public const int MarkerArt = 0x1053, MarkerHue = 0x0035; const uint MarkerBase = 0x7FF00000;
    Map<uint, int[]> m_markers = Map<uint, int[]>.Empty; readonly Lock m_markersGate = new();
    public bool ShowBanks
    {
        get => Cfg.Get("banks", "off") == "on";
        set => Cfg.Set("banks", value ? "on" : "off");
    }
    static bool IsMarker(uint serial)
    {
        return (serial & 0xFFF00000) == MarkerBase;
    }
    // In a cave the corner tile's land is usually the mountain above, drawn above the cave roof where the client hides
    // it (Damian, 2026-10-15: "the 8x8 runes don't show in side caves, where they are useful"): a corner whose land is
    // more than CaveSpan from the player's height takes the player's height, and resend redraws every rune when that
    // changes.
    const int CaveSpan = 12;
    public void UpdateBanks(Func<int, int, int> landZ, bool resend = false)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return;
        Map<uint, int[]> want = Map<uint, int[]>.Empty;
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        if (ShowBanks && (px | py) != 0)
        {
            int bx = px >> 3, by = py >> 3;
            for (int i = -1; i <= 2; i++)
            {
                for (int j = -1; j <= 2; j++)
                {
                    int cx = (bx + i) * 8, cy = (by + j) * 8;
                    if (cx < 0 || cy < 0 || bx + i >= 1024 || by + j >= 1024)
                        continue;
                    want = want.Set(MarkerBase | (uint)((bx + i) << 10) | (uint)(by + j), new[] { cx, cy });
                }
            }
        }
        List<uint> gone = new();
        List<uint> fresh = new();
        lock (m_markersGate)
        {
            foreach (uint s in m_markers.Keys)
                if (!want.ContainsKey(s))
                    gone.Add(s);
            foreach (uint s in want.Keys)
                if (resend || !m_markers.ContainsKey(s))
                    fresh.Add(s);
            foreach (uint s in gone)
                m_markers = m_markers.Remove(s);
            foreach (uint s in fresh)
                m_markers = m_markers.Set(s, want[s]!);
        }
        foreach (uint s in gone)
        {
            byte[] p = new byte[5];
            p[0] = 0x1D;
            Put32(p, 1, s);
            k.ToClient(p, "bank marker off");
        }
        foreach (uint s in fresh)
        {
            int x = want[s]![0], y = want[s]![1];
            int z = landZ(x, y);
            if (z == int.MinValue || Math.Abs(z - pz) > CaveSpan)
                z = pz;
            // 0x1A with a hue (y | 0x8000): serial, graphic, x, y, z, hue.
            byte[] p = new byte[16];
            p[0] = 0x1A;
            p[1] = 0;
            p[2] = 16;
            Put32(p, 3, s);
            Put16(p, 7, MarkerArt);
            Put16(p, 9, x);
            Put16(p, 11, y | 0x8000);
            p[13] = (byte)(sbyte)z;
            Put16(p, 14, MarkerHue);
            k.ToClient(p, "bank marker " + x + "," + y);
        }
    }
    // The 1.25 client double-clicks its own player right after login (0x06 with the plain serial: uo1998 17:50:57 and
    // 17:57:40, 0602BE4F79), which dismounts a mounted player (Damian, 2026-10-15: "when i log back in, my character
    // dismounts", "unless im hidden"). Within LoginSelfUseMs of 0x55 that click goes out as the paperdoll request the
    // client sends for its own paperdoll (serial | 0x80000000, 0682BE4F79), which does not dismount.
    const int LoginSelfUseMs = 3000;
    DateTime m_loginAt = DateTime.MinValue;
    bool HoldLoginSelfUse(GameLink k, byte[] p)
    {
        if (p[0] != 0x06 || p.Length != 5 || (Clock.UtcNow - m_loginAt).TotalMilliseconds > LoginSelfUseMs)
            return false;
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        if (me == 0 || U32(p, 1) != me)
            return false;
        m_loginAt = DateTime.MinValue;
        byte[] q = new byte[5];
        q[0] = 0x06;
        Put32(q, 1, me | 0x80000000);
        string? why = k.Release(q, "login paperdoll instead of the self double-click", false);
        Note("Login: the client's double-click on you went out as a paperdoll request"
            + (why != null ? " (not sent: " + why + ")" : ""));
        return true;
    }
    // ---------- the smithing ladder (Damian, 2026-10-18: "allow both a user config and your best guess. the server
    // might have different code") ----------
    // One item per skill band, never alternated ("there was never a reason to bounce around different items"). The
    // player's ladder is smith.ladder ("item@skill;..."), defaulting to his own steps: ringmail gloves, plate gorget,
    // plate gloves from 65, platemail arms from 95.0, platemail legs from 99.5 (the gorget's start is a guess). The
    // best guess is the 1998 server's: a check of range 50 (blacksmith.m:433) succeeds at (skill - difficulty) * 2 +
    // 500 thousandths and gains in proportion to chance * (1000 - chance) (OUO skill.c:746-909), here per ingot. The
    // difficulties are derived from itemres.mul and the script's rescale (blacksmith.m:121-232), not quoted.
    // smith.ladder.mode: mine|guess.
    public const string DefaultLadder = "ringmail gloves@0;plate gorget@50;plate gloves@65;platemail "
        + "arms@95;platemail legs@99.5";
    static readonly string[][] s_smithDifficulty = {
        new[] { "ringmail gloves", "10", "426" }, new[] { "ringmail tunic", "18", "557" },
            new[] { "chainmail tunic", "20", "688" },
        new[] { "plate gorget", "10", "754" }, new[] { "plate gloves", "12", "786" },
        new[] { "plate helm", "15", "836" },
        new[] { "platemail arms", "18", "885" }, new[] { "platemail legs", "20", "918" },
        new[] { "platemail tunic", "25", "1000" },
    };
    public string SmithLadder
    {
        get => Cfg.Get(Server + "smith.ladder", DefaultLadder);
        set => Cfg.Set(Server + "smith.ladder", value);
    }
    public string SmithLadderMode
    {
        get => Cfg.Get(Server + "smith.ladder.mode", "mine");
        set => Cfg.Set(Server + "smith.ladder.mode", value == "guess" ? "guess" : "mine");
    }
    // The item to smith at skill (tenths): the last ladder step at or under it, or the best guess's.
    public string? SmithNow(int skill)
    {
        if (SmithLadderMode == "guess")
        {
            string? best = null;
            double bestG = -1;
            foreach (string[] r in s_smithDifficulty)
            {
                int ingots = int.Parse(r[1]), diff = int.Parse(r[2]);
                double c = Math.Max(0, Math.Min(1000, (skill - diff) * 2 + 500));
                double g = c * (1000 - c) / ingots;
                if (diff <= skill + 250 && g > bestG)
                {
                    bestG = g;
                    best = r[0];
                }
            }
            return best;
        }
        string? pick = null;
        foreach (string step in SmithLadder.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int at = step.LastIndexOf('@');
            double from;
            if (at <= 0
                || !double.TryParse(step.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out from))
                continue;
            if (skill >= (int)Math.Round(from * 10))
                pick = step.Substring(0, at).Trim();
        }
        return pick;
    }
    // ---------- record me (Damian, 2026-10-18: "i click it, it sends a message to the proxy/log and not to the server.
    // then i can either direct you to it, or if you are live monitoring I can trigger you from in client to start
    // watching ... then I click to stop it") ----------
    // A recording is the relay log between "Record: start" and "Record: stop" lines, toggled by the window's action (no
    // key by default), the control port's record, or "[rec" and "[stop" said in the client; while it runs, a line said
    // beginning "[" is a "Record note:" in the log. None of them reaches the server.
    public volatile bool Recording; DateTime m_recordAt;
    public string RecordToggle(string by)
    {
        GameLink k = m_link;
        Recording = !Recording;
        string line = Recording ? "Record: start (" + by + ")"
            : "Record: stop (" + by + ", " + (int)(Clock.UtcNow - m_recordAt).TotalSeconds + " s)";
        if (Recording)
        {
            m_recordAt = Clock.UtcNow;
            m_handCity = m_handStep = m_handState = null;
            string? p = m_log.Record(true);
            line += "; copied to " + Path.GetFileName(p);
        }
        Note(line);
        if (!Recording)
            m_log.Record(false);
        if (k != null && !k.Closed)
            k.Tell(Recording ? "Recording: say [ and a note; [stop ends it" : "Recording stopped");
        return line;
    }
    bool HoldRecord(GameLink k, byte[] p)
    {
        if (p.Length < 9 || p[0] != 0x03)
            return false;
        int end = Array.IndexOf(p, (byte)0, 8);
        string text = Encoding.ASCII.GetString(p, 8, (end < 0 ? p.Length : end) - 8).Trim();
        if (text.StartsWith("[rec", StringComparison.OrdinalIgnoreCase))
        {
            if (!Recording)
                RecordToggle("the client");
            return true;
        }
        if (text.StartsWith("[stop", StringComparison.OrdinalIgnoreCase))
        {
            if (Recording)
                RecordToggle("the client");
            return true;
        }
        if (Recording && text.StartsWith("["))
        {
            Note("Record note: " + text.Substring(1).Trim());
            DecisionRow(k, "Comment", true, false, "damian", null!, text.Substring(1).Trim());
            return true;
        }
        return false;
    }
}
