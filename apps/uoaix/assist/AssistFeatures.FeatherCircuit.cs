#nullable enable
using System;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the feather circuit (docs/Designs/Done/Apps/AssistHarvest.md stage 3) ----------
    // Damian, 2026-10-23: "kill a chicken or other non-tame bird, use a blade, dump the food and feathers at a
    // collection point with all the same harvest setup targeting, range controls etc."; "you can target any other birds
    // too, they are all the same afaik for the harvest". The 1998 server (stage 2's measurement,
    // record-2650-20261023-153050.log): an attack (0x05) is answered by the combatant (0xAA) and needs no war mode (OUO
    // HandlePacket_ATTACK goes straight to CombatInitiate); the death (0xAF) names the corpse, sent as 0x2006 on the
    // bird's tile; the blade used opens a target cursor, and the corpse targeted puts the meat and the feathers
    // straight into the pack, each with its line (rundir bladed.m targetobj), the corpse left empty. A carve takes the
    // corpse's whole bank: a chicken gave 25 feathers and one cut of raw ribs.
    readonly FeatherGather m_featherGather;
    volatile bool m_featherMode;
    // The feathers of the richest bird bank (templatestable.dat: 25 a chicken, 36 an eagle, 50 the most), for the
    // pack's room.
    public const int FeatherBankMax = 50, FeatherRadiusDefault = 18, FeatherMinHitsDefault = 50;
    public const uint FeatherBladeCursor = 0x46424C44, FeathersToCursor = 0x46504C43;  // "FBLD", "FPLC"
    // A carve's answer (bladed.m targetobj): a part taken (meat, hide, fur, feathers, wool), a part put at the player's
    // feet for want of room, nothing left on the corpse, not a corpse, alive, too far, out of sight, wait, or none.
    public enum CarveAnswer
    {
        None, Meat, Hide, Fur, Feathers, Wool, NoRoom, Nothing, NotCorpse, Alive, TooFar, Unseen, Wait
    }
    public static CarveAnswer CarveSaid(string line)
    {
        if (line.IndexOf("You carve away some meat", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Meat;
        if (line.IndexOf("get the hide", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Hide;
        if (line.IndexOf("get the fur", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Fur;
        if (line.IndexOf("You pluck the bird and get the feathers", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Feathers;
        if (line.IndexOf("get the wool", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Wool;
        if (line.IndexOf("You don't have anywhere to carry the", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.NoRoom;
        if (line.IndexOf("You see nothing useful to carve", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Nothing;
        if (line.IndexOf("Use this on corpses to carve", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.NotCorpse;
        if (line.IndexOf("But that's not dead", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Alive;
        if (line.IndexOf("That is too far away", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.TooFar;
        if (line.IndexOf("You can't see that", StringComparison.OrdinalIgnoreCase) >= 0)
            return CarveAnswer.Unseen;
        foreach (string wl in s_waitLines)
            if (line.IndexOf(wl, StringComparison.OrdinalIgnoreCase) >= 0)
                return CarveAnswer.Wait;
        return CarveAnswer.None;
    }
    // The part a "no room" line names: "meat", "hides", "furs", "feathers" or "wool".
    public static string NoRoomPart(string line)
    {
        const string at = "anywhere to carry the ";
        int i = line.IndexOf(at, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return "";
        string part = line.Substring(i + at.Length);
        int dot = part.IndexOf('.');
        return dot < 0 ? part : part.Substring(0, dot);
    }
    public string? CircuitFeathers()
    {
        return StartCircuit(() => m_featherMode = true);
    }
    // New hunt, as New chop: the piles at feet forgotten and the collection point the player's tile; the blade, At my
    // feet, Range and Collect at kept.
    public string? CircuitFeathersNew()
    {
        return StartCircuit(() =>
        {
            m_featherMode = true;
            GameLink k = m_link;
            int[]? tile = null;
            if (k != null)
                lock (k.State.Gate)
                    tile = new[] { k.State.X, k.State.Y, k.State.Z };
            int piles, goods;
            m_featherGather.NewStart(tile, out piles, out goods);
            Note("Feathers: new hunt"
                + (piles > 0 ? "; " + goods + " feathers in " + piles + " piles at feet forgotten" : "")
                + (tile != null ? "; the collection point is " + tile[0] + "," + tile[1] : ""));
        });
    }
    public volatile string FeatherStatus = "";
    public uint FeatherBlade
    {
        get => m_featherGather.Tool;
        set => m_featherGather.Tool = value;
    }
    public string FeathersTo
    {
        get => m_featherGather.PlaceSpec;
        set => m_featherGather.PlaceSpec = value;
    }
    public bool FeatherAtFeet
    {
        get => m_featherGather.AtFeet;
        set => m_featherGather.AtFeet = value;
    }
    public int FeatherCollectAt
    {
        get => m_featherGather.CollectAt;
        set => m_featherGather.CollectAt = value;
    }
    public int FeatherDropped()
    {
        return m_featherGather.Dropped();
    }
    public int FeatherPileCount()
    {
        return m_featherGather.Piles().Length;
    }
    public void FeatherPilesClear()
    {
        m_featherGather.ClearPiles();
    }
    // How far a bird is looked for from where the player stands, read at every pick.
    public int FeatherRadius
    {
        get => Math.Max(1,
            Math.Min(AssistState.GroundReach, Cfg.GetInt(Server + "feathers.range", FeatherRadiusDefault)));
        set => Cfg.Set(Server + "feathers.range", Math.Max(1, Math.Min(AssistState.GroundReach, value)).ToString());
    }
    // The player's hits, in percent of their most, under which a fight ends the circuit (AssistHarvest.md: "an eagle
    // that fights back ends the attack when hits fall under a setting").
    public int FeatherMinHits
    {
        get => Math.Max(0, Math.Min(99, Cfg.GetInt(Server + "feathers.minhits", FeatherMinHitsDefault)));
        set => Cfg.Set(Server + "feathers.minhits", Math.Max(0, Math.Min(99, value)).ToString());
    }
    public string FeathersText()
    {
        return PlaceText(FeathersTo, "Feathers to")
            + (FeatherAtFeet && !FeathersTo.StartsWith("feet") ? ", at my feet first" : "");
    }
    public string? FeatherBladeTarget()
    {
        return FeatherCursor(FeatherBladeCursor,
            "Target the blade in your pack or your hand: a knife, a dagger, a sword or an axe.");
    }
    public string? FeathersToTarget()
    {
        return FeatherCursor(FeathersToCursor,
            "Target where the feathers and the meat go: a tile, a bag, a pile or the floor.");
    }
    string? FeatherCursor(uint id, string ask)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)(id == FeatherBladeCursor ? 0 : 1);
        Put32(p, 2, id);
        string? why = k.ToClient(p, "feathers cursor");
        FeatherStatus = why == null ? ask : "No cursor: " + why;
        return why;
    }
    bool HoldFeathers(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 19)
            return false;
        uint id = U32(p, 2);
        if (id != FeatherBladeCursor && id != FeathersToCursor)
            return false;
        uint s = U32(p, 7);
        int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
        if (id == FeatherBladeCursor)
        {
            AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
            if (it == null || !FeatherGather.IsBlade(it.Graphic))
            {
                FeatherStatus = "Blade not set: target a knife, a dagger, a sword or an axe in your pack or hand.";
                return true;
            }
            FeatherBlade = s;
            FeatherStatus = "Blade: " + UoTiles.Name(it.Graphic) + " (" + s.ToString("X") + ").";
            return true;
        }
        if (s == 0 && p[1] == 0)
        {
            FeatherStatus = "Feathers to not set: the cursor was cancelled.";
            return true;
        }
        bool mobile;
        lock (k.State.Gate)
            mobile = s != 0 && k.State.Mobiles.ContainsKey(s);
        if (mobile)
        {
            FeatherStatus = "Feathers to not set: a pack animal moves; target a tile, a bag or a pile.";
            return true;
        }
        FeathersTo = p[1] != 0 || s == 0 ? "ground|0|" + x + "|" + y + "|" + z
            : "item|" + s.ToString("X") + "|" + x + "|" + y + "|" + z;
        FeatherStatus = PlaceText(FeathersTo, "Feathers to") + ".";
        return true;
    }
    // The birds left this run: unreached, an attack with no answer, a fight that did not end in a death, a carve
    // refused.
    Set<uint> m_featherBad = Set<uint>.Empty;
    // The nearest bird in view within FeatherRadius: never the player's pet, a threat, an invulnerable one, one
    // labelled "(tame)", or one left this run. A renamed bird with no "(tame)" is a training tame released back to the
    // wild, and hunted (Damian, 2026-10-23: "the release makes 2 exist where 1 used to"; "eventually you get a buildup
    // of excess fauna"). x, y, serial; null when none.
    uint[]? BirdNow(GameLink k, int px, int py)
    {
        List<uint> mine = PetSerials();
        uint best = 0;
        int bx = 0, by = 0, bd = int.MaxValue, radius = FeatherRadius;
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
            {
                int body;
                if (!k.State.Bodies.TryGet(kv.Key, out body) || !FeatherGather.IsBird(body))
                    continue;
                if (mine.Contains(kv.Key) || Threat(k, kv.Key, mine) || k.State.Invulnerable.Contains(kv.Key)
                    || k.State.Tame.Contains(kv.Key) || m_featherBad.Contains(kv.Key))
                    continue;
                int d = Tiles(kv.Value[0], kv.Value[1], px, py);
                if (d > radius || d >= bd)
                    continue;
                best = kv.Key;
                bx = kv.Value[0];
                by = kv.Value[1];
                bd = d;
            }
        }

        return best == 0 ? null : new[] { (uint)bx, (uint)by, best };
    }
    // The pack takes one more bird: the richest bank's feathers and a cut of ribs.
    bool FeatherRoom(GameLink k)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        int feather = items.Count > 0x1BD1 ? items[0x1BD1].Weight : 0, meat = items.Count > FeatherGather.MeatArt
            ? Math.Max(1, items[FeatherGather.MeatArt].Weight) : 1;
        int w;
        lock (k.State.Gate)
            w = k.State.Weight;
        return w <= 0 || w + meat + feather * FeatherBankMax <= MineLimit(k);
    }
    // One bird attacked until its death: the corpse's serial, or 0 and why not. The attack is sent once (L-FUTILITY):
    // the server swings on its own until the bird or the fight ends; a bird a tile or more off is walked after, once a
    // second.
    uint FeatherKill(GameLink k, uint bird, out string? why)
    {
        why = null;
        uint corpse = 0;
        bool engaged = false, dead = false, gone = false;
        object gate = new();
        Action<byte[]> watch = p =>
        {
            if (p.Length < 5 || U32(p, 1) != bird)
                return;
            lock (gate)
            {
                if (p[0] == 0xAA)
                {
                    engaged = true;
                }
                else if (p[0] == 0xAF && p.Length >= 9)
                {
                    dead = true;
                    corpse = U32(p, 5);
                }
                else if (p[0] == 0x1D && !dead)
                {
                    gone = true;
                }
            }
        };
        k.ServerPacket += watch;
        try
        {
            using (Act(k))
            {
                string? w = k.Attack(bird);
                if (w != null)
                {
                    why = "the attack: " + w;
                    return 0;
                }
            }
            DateTime start = Clock.UtcNow;
            DateTime until = start.AddMilliseconds(Ms("kill-wait"));
            DateTime chased = start;
            while (true)
            {
                bool e, d, g;
                lock (gate)
                {
                    e = engaged;
                    d = dead;
                    g = gone;
                }
                if (d)
                    return corpse;
                if (circuitStop)
                {
                    why = "stopped";
                    return 0;
                }
                if (k.Closed)
                {
                    why = "the connection closed";
                    return 0;
                }
                if (g)
                {
                    why = "it left view";
                    return 0;
                }
                if (!e && (Clock.UtcNow - start).TotalMilliseconds > AnswerMs)
                {
                    why = "the attack had no answer";
                    return 0;
                }
                if (Clock.UtcNow > until)
                {
                    why = "not dead after " + Ms("kill-wait") / 1000 + " s";
                    return 0;
                }
                int hits, max;
                lock (k.State.Gate)
                {
                    hits = k.State.Hits;
                    max = k.State.MaxHits;
                }
                if (max > 0 && hits * 100 < max * FeatherMinHits)
                {
                    why = "your hits fell to " + hits + " of " + max;
                    return 0;
                }
                int[]? at;
                int px, py;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                    if (!k.State.Mobiles.TryGet(bird, out at))
                        at = null;
                }
                if (at != null && Tiles(px, py, at[0], at[1]) > 1 && (Clock.UtcNow - chased).TotalMilliseconds >= 1000)
                {
                    chased = Clock.UtcNow;
                    WalkTo(at[0], at[1], 1);
                    continue;
                }
                Clock.Sleep(100);
            }
        }
        finally { k.ServerPacket -= watch; }
    }
    // The corpse carved with the blade: every answer bladed.m gave, the last first-class one returned (feathers,
    // nothing, a refusal), the meat when only that came. Null why, or why there was no answer.
    CarveAnswer FeatherCarve(GameLink k, uint blade, uint corpse, out string? why, List<string> noRoom)
    {
        why = null;
        Wait.Until(Clock, () => ItemOrGround(k, corpse) != null, AnswerMs, "the corpse in view", stepMs: 50);
        AssistItem? c = ItemOrGround(k, corpse);
        if (c == null || c.Container != 0)
        {
            why = "the corpse " + corpse.ToString("X") + " is not on the ground";
            return CarveAnswer.None;
        }
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        // bladed.m carves from within 3 tiles; a step short of that leaves the corpse in reach if the bird fell moving.
        if (Tiles(px, py, c.X, c.Y) > 2)
        {
            string? ww = WalkTo(c.X, c.Y, 1);
            if (ww != null)
            {
                why = "walking to the corpse: " + ww;
                return CarveAnswer.None;
            }
        }
        for (int tries = 0; tries < 2; tries++)
        {
            Heard mark;
            BusyWait(m_lastUse);
            using (Act(k))
            {
                mark = new(k.State);
                if (k.Use(blade, true) != null)
                {
                    why = "the blade did not open";
                    return CarveAnswer.None;
                }
                m_lastUse = Clock.UtcNow;
                AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CursorMs));
                if (!k.MacroCursor)
                {
                    k.Settle();
                    why = "the blade opened no target cursor";
                    foreach (string j in mark.Answers())
                        if (CarveSaid(j) == CarveAnswer.Wait)
                        {
                            why = null;
                            break;
                        }
                    if (why != null)
                        return CarveAnswer.None;
                    continue;
                }
                if (k.Target(false, corpse, c.X, c.Y, c.Z, CorpseArt) != null)
                {
                    why = "the carve's target did not go out";
                    return CarveAnswer.None;
                }
                k.Settle();
            }
            CarveAnswer got = CarveAnswer.None;
            DateTime until = Clock.UtcNow.AddMilliseconds(Ms("carve-wait"));
            while (Clock.UtcNow < until && !k.Closed && !circuitStop)
            {
                noRoom.Clear();
                foreach (string j in mark.Answers())
                {
                    CarveAnswer a = CarveSaid(j);
                    if (a != CarveAnswer.None)
                        got = a;
                    if (a == CarveAnswer.NoRoom)
                        noRoom.Add(NoRoomPart(j));
                }
                if (got != CarveAnswer.None && got != CarveAnswer.Meat && got != CarveAnswer.Hide
                    && got != CarveAnswer.Fur)
                    break;
                Clock.Sleep(100);
            }
            if (got == CarveAnswer.Wait)
                continue;
            if (got == CarveAnswer.None)
                why = "the carve had no answer in " + Ms("carve-wait") / 1000 + " s";
            return got;
        }
        why = "\"You must wait\" twice";
        return CarveAnswer.Wait;
    }
    // Every cut of ribs in the pack put back on the corpse (Damian, 2026-10-23: "have it dump those ribs back on the
    // corpse"): uo1998 makes each cut its own item, never stacked, so a pack carrying them fills by count. Null, or why
    // not.
    string? RibsToCorpse(GameLink k, uint corpse)
    {
        if (!PackHolds(k, FeatherGather.IsMeat))
            return null;
        AssistItem? c = ItemOrGround(k, corpse);
        if (c == null || c.Container != 0)
            return "the corpse " + corpse.ToString("X") + " is gone";
        int put;
        return PutStacks(k, "item|" + corpse.ToString("X") + "|" + c.X + "|" + c.Y + "|" + c.Z, FeatherGather.IsMeat,
            "ribs", "the corpse", out put);
    }
    // Every stack of feathers onto the place, or at feet (GatherStore); the meat with them to the place, or, at feet,
    // carried to the place by the collection (Damian, 2026-10-23: "dump the food and feathers at a collection point"),
    // put down at feet only when the pack has no room for it. Null, or why not.
    string? FeatherStore(GameLink k)
    {
        string? w = GatherStore(k, m_featherGather);
        if (w != null)
            return w;
        int put;
        if (!m_featherGather.AtFeet)
            return PackHolds(k, FeatherGather.IsMeat)
                ? PutStacks(k, m_featherGather.PlaceSpec, FeatherGather.IsMeat, "meat", m_featherGather.PlaceButton,
                out put) : null;
        if (m_featherGather.CollectDue)
            return FeatherCollect(k);
        if (!FeatherRoom(k) && PackHolds(k, FeatherGather.IsMeat))
        {
            w = PutStacks(k, "feet|0", FeatherGather.IsMeat, "meat", m_featherGather.PlaceButton, out put);
            if (w == null)
                Note("Feathers: no room for the meat; " + put + " cuts left at feet");
            return w;
        }
        return null;
    }
    // The piles at feet collected to the place (GatherCollect), and the meat carried there put down with them.
    string? FeatherCollect(GameLink k)
    {
        CircuitStatus = "Feathers: collecting " + m_featherGather.Dropped() + " feathers in "
            + m_featherGather.Piles().Length + " piles";
        string? w = GatherCollect(k, m_featherGather);
        if (w != null)
            return "the collection: " + w;
        int put;
        return PackHolds(k, FeatherGather.IsMeat)
            ? PutStacks(k, m_featherGather.PlaceSpec, FeatherGather.IsMeat, "meat", m_featherGather.PlaceButton,
            out put) : null;
    }
    string RunFeathers(GameLink k)
    {
        if (FeathersTo.Length == 0)
            return "no place for the feathers: target one with Feathers to";
        if (FeatherAtFeet)
        {
            int cx, cy, cz;
            uint co;
            string? pw = CollectPlace(k, m_featherGather, out cx, out cy, out cz, out co);
            if (pw != null)
                return pw;
        }
        int kills = 0, feathers = 0;
        m_featherGather.Stored = 0;
        m_featherBad = m_featherBad.Clear();
        Note("Feathers: hunting around here, " + FeathersText());
        if (m_featherGather.CollectDue)
        {
            string? cr = FeatherCollect(k);
            if (cr != null)
                return cr;
        }
        while (!circuitStop && !k.Closed)
        {
            FeatherStatus = kills + " birds, " + feathers + " feathers, " + m_featherGather.Stored + " stored";
            AssistItem? blade = GatherToolNow(k, m_featherGather);
            if (blade == null)
            {
                k.Tell("Feathers stopped: no blade. Target one with the Blade button.", 0x0026);
                return "no blade in the pack or hand: target one with the Blade button";
            }
            List<int[]> th = Threats(k);
            if (th.Count > 0 && Nearest(th) <= FleeClear)
            {
                CircuitStatus = "Feathers: away from a threat";
                Note("Feathers: a threat within " + Nearest(th) + " tiles; running");
                string? fw = Flee();
                if (fw != null)
                    return "running from a threat: " + fw;
                continue;
            }
            if (!FeatherRoom(k))
            {
                string? sw = FeatherStore(k);
                if (sw != null)
                    return "storing the feathers: " + sw;
                if (!FeatherRoom(k))
                    return "the pack has no room after storing the feathers";
                continue;
            }
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            uint[]? bird = BirdNow(k, px, py);
            if (bird == null)
            {
                string end = "no bird within " + FeatherRadius + " tiles";
                if (FeatherAtFeet && (PackHolds(k, FeatherGather.IsFeather) || m_featherGather.Piles().Length > 0))
                {
                    if (PackHolds(k, FeatherGather.IsFeather))
                    {
                        string? sw = GatherStore(k, m_featherGather);
                        if (sw != null)
                            return end + "; storing the feathers: " + sw;
                    }
                    Note("Feathers: " + end + "; collecting the piles first");
                    string? cw = FeatherCollect(k);
                    if (cw != null)
                        return end + "; " + cw;
                }
                return end;
            }
            int bx = (int)bird[0], by = (int)bird[1];
            uint serial = bird[2];
            string what = MobileName(k, serial) + " (" + serial.ToString("X") + ")";
            if (Tiles(px, py, bx, by) > 1)
            {
                CircuitStatus = "Feathers: to " + what + " at " + bx + "," + by;
                string? w = WalkTo(bx, by, 1);
                if (w != null)
                {
                    m_featherBad = m_featherBad.Add(serial);
                    Note("Feathers: " + what + " at " + bx + "," + by + " was not reached: " + w + "; left");
                    continue;
                }
            }
            // Its label read once before the fight: "(tame)" is someone's pet (Damian, 2026-10-23: "there is typically
            // a (Tame) in the name for a currently owned pet").
            bool labelled;
            lock (k.State.Gate)
                labelled = k.State.Labelled.Contains(serial);
            if (!labelled)
            {
                using (Act(k))
                    k.Look(serial);
                Wait.Until(Clock, () => { lock (k.State.Gate) return k.State.Labelled.Contains(serial); }, LabelWaitMs,
                    "the bird's label", stepMs: 50);
            }
            bool tame;
            lock (k.State.Gate)
                tame = k.State.Tame.Contains(serial);
            if (tame)
            {
                m_featherBad = m_featherBad.Add(serial);
                Note("Feathers: " + what + " is tame; left");
                continue;
            }
            string? ew = GatherEquip(k, m_featherGather, blade);
            if (ew != null)
                return "the blade: " + ew;
            CircuitStatus = "Feathers: attacking " + what;
            string? kw;
            uint corpse = FeatherKill(k, serial, out kw);
            if (corpse == 0)
            {
                if (kw!.StartsWith("your hits"))
                {
                    k.Tell("Feathers stopped: " + kw + ".", 0x0026);
                    return "the fight with " + what + ": " + kw;
                }
                if (kw == "stopped" || kw == "the connection closed")
                    return kw;
                m_featherBad = m_featherBad.Add(serial);
                Note("Feathers: " + what + ": " + kw + "; left");
                continue;
            }
            kills++;
            int before = PackCount(k, FeatherGather.IsFeather), meatBefore = PackCount(k, FeatherGather.IsMeat);
            CircuitStatus = "Feathers: carving " + what;
            string? cwhy;
            List<string> noRoom = new();
            CarveAnswer a = FeatherCarve(k, blade.Serial, corpse, out cwhy, noRoom);
            Wait.Until(Clock, () => PackCount(k, FeatherGather.IsFeather) > before, Ms("drop-settle"),
                "the feathers in the pack", stepMs: 50);
            int got = Math.Max(0, PackCount(k, FeatherGather.IsFeather) - before);
            feathers += got;
            if (noRoom.Count > 0)
            {
                // bladed.m asks canHold of the part it made after giveItem, and a part merged into the pack's stack is
                // deleted by the merge, so the line comes with the part in the pack (2026-10-23 02:05:52: 25 feathers
                // onto the pack's 25, "You don't have anywhere to carry the feathers.", nothing at feet).
                bool fell = false;
                foreach (string part in noRoom)
                    fell |= part == "feathers" ? got == 0
                        : part == "meat" ? PackCount(k, FeatherGather.IsMeat) <= meatBefore : true;
                if (fell)
                    return "the carve of " + what + ": the server had no room in the pack for a part, put at your feet";
            }
            string? rw = RibsToCorpse(k, corpse);
            if (rw != null)
                Note("Feathers: the ribs stay in the pack: " + rw);
            if (noRoom.Count > 0)
            {
                Note("Feathers: " + what + " carved: " + got + " feathers (the server's no room for the "
                    + string.Join(", ", noRoom) + " was a merge into the pack's stack)");
                continue;
            }
            if (a == CarveAnswer.Feathers || a == CarveAnswer.Nothing)
            {
                Note("Feathers: " + what + " carved: " + got + " feathers"
                    + (a == CarveAnswer.Nothing ? " (nothing useful left on it)" : ""));
                continue;
            }
            Note("Feathers: the carve of " + what + ": " + (cwhy ?? a.ToString())
                + (got > 0 ? "; " + got + " feathers" : ""));
        }
        return circuitStop ? "stopped" : "the connection closed";
    }
}
