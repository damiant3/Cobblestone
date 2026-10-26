#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- session summary ----------
    // Damian, 2026-10-15: "add a session summary report to the session details, showing aggregates of things like kills
    // by creature, total skills gained lost, attrs, reagents picked up, loops run, and their effectiveness at skill
    // gain". A kill is the death (0xAF) of a mobile the player attacked (0x05) within KillWindowMs.
    public sealed class LoopRun
    {
        public string Name = null!; public int Skill; public DateTime Start, End; public int Rounds, From, To;
    }
    public const int KillWindowMs = 120000, MiningSkill = 45;
    readonly List<LoopRun> m_runs = new();
    volatile LoopRun? m_currentRun = null;
    ValueMap<string, int> m_kills = ValueMap<string, int>.Empty; readonly Lock m_killsGate = new();
    ValueMap<int, int> m_picked = ValueMap<int, int>.Empty; readonly Lock m_pickedGate = new();
    ValueMap<uint, DateTime> m_attacked = ValueMap<uint, DateTime>.Empty; readonly Lock m_attackedGate = new();
    void Fought(uint s)
    {
        lock (m_attackedGate)
        {
            if (m_attacked.Count > 1000)
                m_attacked = m_attacked.Clear();
            m_attacked = m_attacked.Set(s, Clock.UtcNow);
        }
    }
    static int SkillOf(GameLink k, int id)
    {
        if (id < 0)
            return 0;
        return k.State.Skills.Get(id + 1, 0);
    }
    static string SkillName(int id)
    {
        return id >= 0 && id < AssistWindow.SkillNames.Length ? AssistWindow.SkillNames[id] : "skill " + id;
    }
    static string Tenths(int v)
    {
        return (v >= 0 ? "+" : "-") + (Math.Abs(v) / 10.0).ToString("0.0");
    }
    string MobileName(GameLink k, uint s)
    {
        lock (k.State.Gate)
        {
            string? n;
            if (k.State.Names.TryGet(s, out n))
                return n;
            int b;
            if (k.State.Bodies.TryGet(s, out b))
                return "body 0x" + b.ToString("X3");
        }
        return "mobile " + s.ToString("X");
    }
    void Died(GameLink k, uint s, uint corpse)
    {
        string victim = MobileName(k, s);
        lock (m_killMarks)
            if (corpse != 0)
            {
                if (m_corpseOf.Count > 2000)
                    m_corpseOf = m_corpseOf.Clear();
                m_corpseOf = m_corpseOf.Set(corpse, victim);
            }
        DateTime at;
        lock (m_attackedGate)
        {
            if (!m_attacked.TryGet(s, out at) || (Clock.UtcNow - at).TotalMilliseconds > KillWindowMs)
                return;
            m_attacked = m_attacked.Remove(s);
        }
        lock (m_killsGate)
            m_kills = m_kills.Set(victim, m_kills.Get(victim, 0) + 1);
        KillAt(k, s, victim, "you", corpse);
    }
    // The session's kills on the map (Damian, 2026-10-15: "on the map mark all the sessions kills"): where the victim
    // was last seen, by you or by a pet, with the loot taken from its corpse.
    public sealed class KillMark
    {
        public int X, Y; public string Name = "", By = "", Loot = ""; public DateTime At; public uint Corpse;
    }
    readonly List<KillMark> m_killMarks = new();
    Map<uint, string> m_corpseOf = Map<uint, string>.Empty;
    void KillAt(GameLink k, uint s, string victim, string by, uint corpse)
    {
        int x, y;
        Creature? c;
        bool seen;
        lock (m_creaturesGate)
            seen = m_creatures.TryGet(s, out c);
        if (seen)
        {
            x = c!.X;
            y = c.Y;
        }
        else
        {
            lock (k.State.Gate)
            {
                x = k.State.X;
                y = k.State.Y;
            }
        }

        lock (m_killMarks)
        {
            m_killMarks.Add(new KillMark { X = x, Y = y, Name = victim, By = by, At = Clock.UtcNow.ToLocalTime(),
                Corpse = corpse });
            if (m_killMarks.Count > 1000)
                m_killMarks.RemoveAt(0);
        }
    }
    public KillMark[] KillMarks()
    {
        lock (m_killMarks)
            return m_killMarks.ToArray();
    }

    // ---------- trips ----------
    // Damian, 2026-10-15: "'begin trip' and 'end trip' and that trip will log all the npcs you see, the resources and
    // other loose items found ... a report i could view to see how many frigging monsters i just had to dodge". Between
    // the two: every mobile drawn (0x77, 0x78: serial, body, place, notoriety), every world item drawn (0x1A), the
    // steps walked (0x22), and the session's kills, loot and deaths since the start; End writes
    // reports/trip-<server>-<start>.md. Close: the player's steps this hostile spent within TripRecorder.ChaseRange;
    // Area: the region it was first seen in.
    public sealed class TripMob
    {
        public uint Serial;
        public int Body, X, Y, Noto, X0, Y0, Close;
        public bool Person; public DateTime First, Last; public string Area = "";
    }
    public sealed class TripItem
    {
        public uint Serial; public int Graphic, Amount, X, Y;
    }
    public sealed class Trip
    {
        public DateTime Start; public int X0, Y0, Steps, Kills0, Deaths0; public bool Auto, Session;
        public readonly TripRecorder Rec = new();
        public Map<uint, TripMob> Mobs = Map<uint, TripMob>.Empty;
        public Map<uint, TripItem> Items = Map<uint, TripItem>.Empty;
        public ValueMap<string, int> Loot0 = ValueMap<string, int>.Empty;
    }
    public const int TripKeep = 20000;
    volatile Trip? m_trip = null;
    public bool TripOn => m_trip != null;
    public string TripState()
    {
        Trip? t = m_trip;
        if (t == null)
            return "";
        lock (t)
            return "Trip since " + t.Start.ToString("HH:mm") + ": " + t.Mobs.Count + " mobiles, " + t.Items.Count
                + " items, " + t.Steps + " steps";
    }
    public string? BeginTrip()
    {
        return BeginTrip(false);
    }
    // Every session is a trip (Damian, 2026-10-16: "always start a new session with a new trip"): the login (0x55) ends
    // a trip still running and begins one that runs until the connection goes or End trip; one with no step is dropped.
    void SessionTrip()
    {
        if (m_trip != null)
        {
            string? path;
            string? why = EndTrip(out path);
            if (why != null)
                Note("Trip: " + why);
        }
        BeginTrip(false, true);
    }
    string? BeginTrip(bool auto, bool session = false)
    {
        GameLink k = m_link;
        if (k == null)
            return "no game connection";
        Trip t = new()
        {
            Start = Clock.UtcNow.ToLocalTime(),
            Auto = auto,
            Session = session
        };
        lock (k.State.Gate)
        {
            t.X0 = k.State.X;
            t.Y0 = k.State.Y;
            t.Rec.Begin(k.State.Skills);
        }
        lock (m_killMarks)
            t.Kills0 = m_killMarks.Count;
        lock (m_corpses)
            t.Deaths0 = m_corpses.Count;
        t.Loot0 = m_lootTotals;
        m_trip = t;
        Note((auto ? "Trip begun on leaving guarded land at "
            : session ? "Trip begun with the session at " : "Trip begun at ") + t.X0 + "," + t.Y0);
        return null;
    }
    // Automatic trips (Damian, 2026-10-16: "a drop down of trips taken"): with the server's region file loaded, a trip
    // begins at the first step out of guarded land and ends after AutoEndSteps steps back inside it; one shorter than
    // AutoMinSteps is dropped. A trip begun by the button runs until the button ends it.
    public const int AutoEndSteps = 20, AutoMinSteps = 50;
    public bool AutoTrips
    {
        get => Cfg.Get("trips.auto", "on") == "on";
        set => Cfg.Set("trips.auto", value ? "on" : "off");
    }
    public int TripsWritten;
    void TripPacket(GameLink k, byte[] p)
    {
        Trip? t = m_trip;
        if (t == null)
        {
            if (p[0] != 0x22 || p.Length < 3 || p[2] == 0 || !AutoTrips || !UoRegions.Loaded)
                return;
            int ax, ay, az;
            lock (k.State.Gate)
            {
                ax = k.State.X;
                ay = k.State.Y;
                az = k.State.Z;
            }
            bool guarded;
            TripLore.AreaAt(ax, ay, az, out guarded);
            if (!guarded)
                BeginTrip(true);
            return;
        }
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        uint s = 0;
        int body = 0, x = 0, y = 0, noto = 0;
        if (p[0] == 0x77 && p.Length >= 17)
        {
            s = U32(p, 1);
            body = (p[5] << 8) | p[6];
            x = (p[7] << 8) | p[8];
            y = (p[9] << 8) | p[10];
            noto = p[16];
        }
        else if (p[0] == 0x78 && p.Length >= 19)
        {
            s = U32(p, 3) & 0x7FFFFFFF;
            body = (p[7] << 8) | p[8];
            x = ((p[9] << 8) | p[10]) & 0x7FFF;
            y = (p[11] << 8) | p[12];
            noto = p[18];
        }
        else if (p[0] == 0x22 && p.Length >= 3 && p[2] != 0)
        {
            TripStep(k, t);
            return;
        }
        else if (p[0] == 0x1C && p.Length > 44)
        {
            if (Encoding.ASCII.GetString(p, 44, p.Length - 44).IndexOf(TripLore.ResistLine,
                StringComparison.OrdinalIgnoreCase) >= 0)
                lock (t)
                    t.Rec.Get(t.Rec.Current, t.X0, t.Y0).Resist++;
            return;
        }
        else if (p[0] == 0x1A && p.Length >= 9)
        {
            uint item = U32(p, 3) & 0x7FFFFFFF;
            AssistItem? g;
            k.State.Ground.TryGet(item, out g);
            if (g == null)
                return;
            lock (t)
            {
                if (!t.Items.ContainsKey(item) && t.Items.Count < TripKeep)
                {
                    t.Items = t.Items.Set(item, new TripItem { Serial = item, Graphic = g.Graphic,
                        Amount = Math.Max(1, g.Amount), X = g.X, Y = g.Y });
                    if (ReagentName(g.Graphic) != null)
                        t.Rec.Get(t.Rec.Current, g.X, g.Y).ReagentPiles++;
                }
            }

            return;
        }
        else
        {
            return;
        }

        if (s == me || s == 0)
            return;
        lock (t)
        {
            TripMob? m;
            if (!t.Mobs.TryGet(s, out m))
            {
                if (t.Mobs.Count >= TripKeep)
                    return;
                t.Mobs = t.Mobs.Set(s, m = new TripMob { Serial = s, First = Clock.UtcNow.ToLocalTime(), X0 = x,
                    Y0 = y, Area = t.Rec.Current });
            }
            m.Body = body;
            m.X = x;
            m.Y = y;
            m.Noto = noto & 0x7F;
            m.Person = body >= 0x190 && body <= 0x193;
            m.Last = Clock.UtcNow;
        }
    }
    void TripStep(GameLink k, Trip t)
    {
        int x, y, z, hits, maxHits, stam, maxStam, gold;
        ValueMap<int, int> skills;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
            hits = k.State.Hits;
            maxHits = k.State.MaxHits;
            stam = k.State.Stam;
            maxStam = k.State.MaxStam;
            gold = k.State.Gold;
            skills = k.State.Skills;
        }
        bool guarded;
        int run;
        lock (t)
        {
            t.Steps++;
            t.Rec.Step(Clock.UtcNow, x, y, z, hits, maxHits, stam, maxStam, gold, skills, t.Mobs.Values, out guarded);
            run = t.Rec.GuardedRun;
        }
        if (t.Auto && run >= AutoEndSteps && m_trip == t)
        {
            string? path;
            string? why = EndTrip(out path);
            if (why != null)
                Note("Trip: " + why);
        }
    }
    static readonly ImmutableArray<string> s_notoNames = [ "unknown", "innocent", "friend", "attackable", "criminal",
        "enemy",
        "murderer", "invulnerable" ];
    // Ends the trip and writes its report; the report's path, or why none.
    public string? EndTrip(out string? path)
    {
        path = null;
        Trip? t = m_trip;
        GameLink k = m_link;
        if (t == null)
            return "no trip is running";
        m_trip = null;
        if (t.Auto)
        {
            int steps;
            lock (t)
                steps = t.Rec.Steps;
            if (steps < AutoMinSteps)
                return "dropped a " + steps + "-step trip outside the guards";
        }
        if (t.Session)
        {
            int steps;
            lock (t)
                steps = t.Rec.Steps;
            if (steps == 0)
                return "dropped a session trip with no step";
        }
        StringBuilder sb = new();
        DateTime now = Clock.UtcNow.ToLocalTime();
        int x1 = 0, y1 = 0;
        if (k != null)
            lock (k.State.Gate)
            {
                x1 = k.State.X;
                y1 = k.State.Y;
            }
        sb.AppendLine("# Trip " + t.Start.ToString("yyyy-MM-dd HH:mm") + " to " + now.ToString("HH:mm") + " ("
            + (int)(now - t.Start).TotalMinutes + " min) on " + Server.TrimEnd('.'));
        sb.AppendLine();
        sb.AppendLine("From " + t.X0 + "," + t.Y0 + " to " + x1 + "," + y1 + "; " + t.Steps + " steps.");
        lock (t)
        {
            Map<string, int[]> creatures = Map<string, int[]>.Empty;
            List<string> people = new();
            int hostile = 0;
            foreach (TripMob m in t.Mobs.Values)
            {
                string name = k != null ? MobileName(k, m.Serial) : m.Serial.ToString("X");
                if (m.Person)
                {
                    people.Add(name + " ("
                        + (m.Noto < s_notoNames.Length ? s_notoNames[m.Noto] : "notoriety " + m.Noto) + ") first at "
                        + m.X0 + "," + m.Y0);
                    continue;
                }
                int[]? c;
                if (!creatures.TryGet(name, out c))
                    creatures = creatures.Set(name, c = new int[2]);
                c[0]++;
                if (m.Noto == 3 || m.Noto >= 4 && m.Noto <= 6)
                {
                    c[1]++;
                    hostile++;
                }
            }
            int all = 0;
            foreach (int[] c in creatures.Values)
                all += c[0];
            sb.AppendLine();
            sb.AppendLine("## Creatures: " + all + " (" + hostile + " attackable or worse)");
            sb.AppendLine();
            sb.AppendLine("| creature | seen | attackable or worse |");
            sb.AppendLine("|---|---|---|");
            List<KeyValuePair<string, int[]>> cl = creatures.Ordered(StringComparer.Ordinal);
            cl.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            foreach (KeyValuePair<string, int[]> kv in cl)
                sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " |");
            sb.AppendLine();
            sb.AppendLine("## People and NPCs: " + people.Count);
            sb.AppendLine();
            people.Sort();
            foreach (string p in people)
                sb.AppendLine("- " + p);
            Map<string, int[]> items = Map<string, int[]>.Empty;
            foreach (TripItem it in t.Items.Values)
            {
                string n = UoTiles.Name(it.Graphic);
                int[]? c;
                if (!items.TryGet(n, out c))
                    items = items.Set(n, c = new int[2]);
                c[0]++;
                c[1] += it.Amount;
            }
            sb.AppendLine();
            sb.AppendLine("## Loose items: " + t.Items.Count);
            sb.AppendLine();
            sb.AppendLine("| item | piles | total |");
            sb.AppendLine("|---|---|---|");
            List<KeyValuePair<string, int[]>> il = items.Ordered(StringComparer.Ordinal);
            il.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            foreach (KeyValuePair<string, int[]> kv in il)
                sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## Kills, loot and deaths");
        sb.AppendLine();
        lock (m_killMarks)
        {
            for (int i = t.Kills0; i < m_killMarks.Count; i++)
            {
                KillMark m = m_killMarks[i];
                sb.AppendLine("- killed " + m.Name + (m.By != "you" ? " (by " + m.By + ")" : "") + " at " + m.X + ","
                    + m.Y + " " + m.At.ToString("HH:mm") + (m.Loot.Length > 0 ? ": " + m.Loot : ""));
            }
        }

        foreach (KeyValuePair<string, int> kv in m_lootTotals.Ordered(StringComparer.Ordinal))
        {
            int was = t.Loot0.Get(kv.Key, 0);
            if (kv.Value > was)
                sb.AppendLine("- looted " + (kv.Value - was) + " " + kv.Key);
        }
        lock (m_corpses)
            for (int i = t.Deaths0; i < m_corpses.Count; i++)
                sb.AppendLine("- died at " + m_corpses[i].X + "," + m_corpses[i].Y + " "
                    + m_corpses[i].At.ToString("HH:mm"));
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "trip-" + ServerSafe + "-" + t.Start.ToString("yyyyMMdd-HHmm") + ".md");
            File.WriteAllText(path, sb.ToString());
            TripRegions(k!, t);
            lock (t)
                t.Rec.Write(Path.ChangeExtension(path, ".trip"), t.Start, now, Server.TrimEnd('.'),
                    t.Auto ? 1 : t.Session ? 2 : 0, t.X0, t.Y0, x1, y1);
            Interlocked.Increment(ref TripsWritten);
            Note("Trip ended: " + path);
            return null;
        }
        catch (Exception e) { path = null; return "the report was not written: " + e.Message; }
    }
    // The trip's creatures, kills and deaths into the regions they happened in.
    void TripRegions(GameLink k, Trip t)
    {
        Map<uint, string> names = Map<uint, string>.Empty;
        lock (t)
            foreach (TripMob m in t.Mobs.Values)
                if (!m.Person)
                    names = names.Set(m.Serial, k != null ? MobileName(k, m.Serial) : m.Serial.ToString("X"));
        List<KillMark> kills = new();
        lock (m_killMarks)
            for (int i = t.Kills0; i < m_killMarks.Count; i++)
                kills.Add(m_killMarks[i]);
        List<Corpse> deaths = new();
        lock (m_corpses)
            for (int i = t.Deaths0; i < m_corpses.Count; i++)
                deaths.Add(m_corpses[i]);
        int z = 0;
        if (k != null)
            lock (k.State.Gate)
                z = k.State.Z;
        lock (t)
        {
            foreach (TripMob m in t.Mobs.Values)
            {
                if (m.Person)
                    continue;
                bool g;
                TripArea a = t.Rec.Get(m.Area.Length > 0 ? m.Area : TripLore.AreaAt(m.X0, m.Y0, z, out g), m.X0, m.Y0);
                int[]? c;
                string n = names.Get(m.Serial, m.Serial.ToString("X"));
                if (!a.Creatures.TryGet(n, out c))
                    a.Creatures = a.Creatures.Set(n, c = new int[3]);
                c[0]++;
                if (TripRecorder.Hostile(m.Noto))
                    c[1]++;
                if (m.Close >= TripRecorder.ChaseSteps)
                    c[2]++;
            }
            bool guarded;
            foreach (KillMark m in kills)
                t.Rec.Get(TripLore.AreaAt(m.X, m.Y, z, out guarded), m.X, m.Y).Kills++;
            foreach (Corpse d in deaths)
                t.Rec.Get(TripLore.AreaAt(d.X, d.Y, z, out guarded), d.X, d.Y).Deaths++;
        }
    }
    // A corpse's loot into the trip region its kill was in, or the region the player stands in.
    void TripLoot(uint corpse, int graphic, string what, int amount)
    {
        Trip? t = m_trip;
        if (t == null)
            return;
        int x = -1, y = -1;
        lock (m_killMarks)
            foreach (KillMark m in m_killMarks)
                if (m.Corpse == corpse)
                {
                    x = m.X;
                    y = m.Y;
                }
        lock (t)
        {
            bool g;
            TripArea a = x < 0 ? t.Rec.Get(t.Rec.Current, t.X0, t.Y0)
                : t.Rec.Get(TripLore.AreaAt(x, y, 0, out g), x, y);
            a.Loot = a.Loot.Set(what, a.Loot.Get(what, 0) + amount);
            if (ReagentName(graphic) != null)
                a.ReagentsLooted += amount;
            if (TripLore.Wand(graphic) || AssistWindow.IsMagic(what))
                a.MagicLoot++;
        }
    }
    // Loot off corpses (Damian, 2026-10-15: "track the loot off the corpses"): the player's own lift (0x07) of an item
    // inside a corpse, then its drop (0x08) into any container but that corpse; a refused lift (0x27) cancels. A corpse
    // is one the death packet named (0xAF) or an item of corpse art (0x2006). Totals for the session, and a line each
    // in loot-<server>.txt.
    public const int CorpseArt = 0x2006;
    ValueMap<string, int> m_lootTotals = ValueMap<string, int>.Empty; readonly Lock m_lootTotalsGate = new();
    uint m_liftSerial, m_liftCorpse; int m_liftGraphic, m_liftAmount;
    void LootClient(GameLink k, byte[] p)
    {
        if (p[0] == 0x07 && p.Length >= 7)
        {
            uint s = U32(p, 1);
            m_liftSerial = 0;
            AssistItem? it;
            uint box = 0;
            int g = 0;
            bool corpse = false;
            lock (k.State.Gate)
            {
                if (!k.State.Items.TryGet(s, out it))
                    return;
                box = it.Container;
                g = it.Graphic;
                AssistItem? bx;
                corpse = k.State.Ground.TryGet(box, out bx) && bx.Graphic == CorpseArt;
            }
            lock (m_killMarks)
                corpse |= m_corpseOf.ContainsKey(box);
            if (!corpse)
                return;
            m_liftSerial = s;
            m_liftCorpse = box;
            m_liftGraphic = g;
            m_liftAmount = Math.Max(1, (p[5] << 8) | p[6]);
        }
        if (p[0] == 0x08 && p.Length >= 14 && m_liftSerial != 0 && U32(p, 1) == m_liftSerial)
        {
            uint into = U32(p, 10);
            uint s = m_liftSerial;
            m_liftSerial = 0;
            if (into == m_liftCorpse || into == 0xFFFFFFFF)
                return;
            string? what = UoTiles.Name(m_liftGraphic), from;
            lock (m_killMarks)
            {
                if (!m_corpseOf.TryGet(m_liftCorpse, out from))
                    from = "a corpse";
                foreach (KillMark m in m_killMarks)
                    if (m.Corpse == m_liftCorpse)
                        m.Loot += (m.Loot.Length > 0 ? ", " : "") + m_liftAmount + " " + what;
            }
            lock (m_lootTotalsGate)
                m_lootTotals = m_lootTotals.Set(what, m_lootTotals.Get(what, 0) + m_liftAmount);
            TripLoot(m_liftCorpse, m_liftGraphic, what, m_liftAmount);
            try
            {
                File.AppendAllText(ServerFile("loot"),
                    Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\t" + Clean(from) + "\t"
                    + m_liftAmount + "\t" + Clean(what) + "\r\n");
            }
            catch { }
        }
    }
    public string SessionSummary()
    {
        GameLink k = m_link;
        if (k == null)
            return "";
        List<string> lines = new();
        ValueMap<string, int> kills = m_kills;
        if (kills.Count > 0)
        {
            List<string> parts = new();
            int all = 0;
            foreach (KeyValuePair<string, int> kv in kills.Ordered(StringComparer.Ordinal))
            {
                parts.Add(kv.Key + " " + kv.Value);
                all += kv.Value;
            }
            lines.Add("Kills " + all + ": " + string.Join(", ", parts));
        }

        ValueMap<string, int> loot = m_lootTotals;
        if (loot.Count > 0)
        {
            List<string> parts = new();
            foreach (KeyValuePair<string, int> kv in loot.Ordered(StringComparer.Ordinal))
                parts.Add(kv.Value + " " + kv.Key);
            lines.Add("Loot: " + string.Join(", ", parts));
        }

        lock (k.State.Gate)
        {
            List<string> up = new();
            int net = 0;
            foreach (KeyValuePair<int, int> kv in k.State.Skills)
            {
                int s0;
                if (!k.State.SkillStart.TryGet(kv.Key, out s0) || kv.Value == s0)
                    continue;
                up.Add(Tenths(kv.Value - s0) + " " + SkillName(kv.Key - 1));
                net += kv.Value - s0;
            }
            lines.Add("Skills: "
                + (up.Count == 0 ? "no change" : string.Join(", ", up) + " (net " + Tenths(net) + ")"));
            if (k.State.StrStart >= 0)
                lines.Add("Attributes: str " + k.State.Str + " ("
                    + (k.State.Str - k.State.StrStart).ToString("+0;-0;0") + "), dex " + k.State.Dex + " ("
                    + (k.State.Dex - k.State.DexStart).ToString("+0;-0;0") + "), int " + k.State.Int + " ("
                    + (k.State.Int - k.State.IntStart).ToString("+0;-0;0") + ")");
        }
        ValueMap<int, int> picked = m_picked;
        if (picked.Count > 0)
        {
            List<string> parts = new();
            int all = 0;
            foreach (KeyValuePair<int, int> kv in picked.Ordered(Comparer<int>.Default))
            {
                parts.Add((ReagentName(kv.Key) ?? kv.Key.ToString("X4")) + " " + kv.Value);
                all += kv.Value;
            }
            lines.Add("Picked up " + all + ": " + string.Join(", ", parts));
        }

        Map<string, int[]> each = Map<string, int[]>.Empty;  // runs, rounds, gain, seconds
        List<LoopRun> all2 = new();
        lock (m_runs)
            all2.AddRange(m_runs);
        LoopRun? cur = m_currentRun;
        if (cur != null)
            all2.Add(cur);
        foreach (LoopRun r in all2)
        {
            int[]? a;
            if (!each.TryGet(r.Name, out a))
                each = each.Set(r.Name, a = new int[5]);
            a[0]++;
            a[1] += r.Rounds;
            a[2] += (r == cur ? SkillOf(k, r.Skill) : r.To) - r.From;
            a[3] += (int)(((r == cur ? Clock.UtcNow : r.End) - r.Start).TotalSeconds);
            a[4] = r.Skill;
        }
        foreach (KeyValuePair<string, int[]> kv in each.Ordered(StringComparer.Ordinal))
        {
            int[] a = kv.Value;
            string gain = a[4] < 0 ? ""
                : ", " + Tenths(a[2]) + " " + SkillName(a[4])
                + (a[3] > 0
                ? " (" + (a[2] / 10.0 * 3600.0 / a[3]).ToString("0.0") + "/hour"
                + (a[1] > 0 ? ", " + (a[2] / 10.0 * 100.0 / a[1]).ToString("0.0") + "/100 rounds" : "") + ")" : "");
            lines.Add("Loop " + kv.Key + ": " + a[0] + " run" + (a[0] == 1 ? "" : "s") + ", " + a[1] + " rounds, "
                + (a[3] / 60) + " min" + gain);
        }
        int seen, beasts;
        lock (m_peopleGate)
            seen = m_people.Count;
        lock (m_creaturesGate)
            beasts = m_creatures.Count;
        if (seen + beasts > 0)
            lines.Add("Seen " + seen + " people, " + beasts + " creatures: report " + PeopleReportPath);
        return string.Join("\n", lines);
    }
}
