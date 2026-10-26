#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the house kept locked (Damian, 2026-10-20: "there is a keyring on the vendor that can be used to lock
    // and unlock the door. it should be kept on the vendor in the top left corner when not in immediate use ... the
    // lock/unlock has to be a smooth move executed when no other players are around who might kill us ... the key
    // should never leave the doorstep or the inside of the house") ----------
    // The 1998 house door (rundir housedoor.m): locked, it opens from inside to anyone ("That is locked, but is usable
    // from the inside."), from outside only to one carrying its key, and stays locked ("You quickly unlock, use, and
    // relock the door."); else "That is locked.". A keyring used and aimed at the door toggles the lock ("You
    // lock"/"You unlock" and the door's name, keyring.m). The door therefore stays locked: the key is taken off the
    // vendor at the doorstep, the door passed and shut behind, and the key put back from the doorstep before any other
    // step outside.
    public bool HouseLocked
    {
        get => Cfg.Get(Server + "house.locked", "on") == "on";
        set => Cfg.Set(Server + "house.locked", value ? "on" : "off");
    }
    public const int HomeClearWaitMs = 180000, HomeViewTiles = 18, KeyX = 44, KeyY = 65;
    static readonly ImmutableArray<string> s_homeSafeTypes = [ "Shopkeeper", "Banker", "Healer", "Guard", "NPC",
        "Player vendor" ];
    // Players the door does not wait for, by name, ";" between (Damian, 2026-10-20, of his neighbour Driz Havvik: "he
    // is blue, keeps to himself ... i think he is safe to work around").
    public string HomeIgnoreText
    {
        get => Cfg.Get(Server + "house.ignore", "");
        set => Cfg.Set(Server + "house.ignore", value ?? "");
    }
    Set<string> HomeIgnored
    {
        get
        {
            Set<string> s = Set<string>.Empty;
            foreach (string n in HomeIgnoreText.Split(';'))
                if (n.Trim().Length > 0)
                    s = s.Add(n.Trim().ToLowerInvariant());
            return s;
        }
    }
    static bool IsKey(int g)
    {
        return (g >= 0x100E && g <= 0x1013) || (g >= 0x1769 && g <= 0x176B);
    }
    // Vendor and Ring: the house's own vendor and the key ring on it (Ring 0: any key the vendor holds, the one-house
    // settings); Name empty for the one-house settings.
    sealed class Home
    {
        public int X0, Y0, X1, Y1, Floor, DoorX, DoorY, DoorArt, OutX, OutY;
        public uint Vendor, Ring;
        public string Name = "";
        public int StepX => DoorX + OutX;
        public int StepY => DoorY + OutY; public int InX => DoorX - OutX; public int InY => DoorY - OutY;
    }
    // The house holding the mining forge (or the house vendor, or where either was last seen, house.at, for a circuit
    // that arrives with neither set nor in view): its footprint, its outer door (the door part nearest the footprint's
    // edge) and the way out through it; null when none is known.
    Home? HomeNow(GameLink k)
    {
        int hx = 0, hy = 0;
        string[] fg = MineCfg("forge").Split('|');
        if (fg.Length >= 6)
        {
            hx = int.Parse(fg[2]);
            hy = int.Parse(fg[3]);
        }
        else
        {
            uint v = HouseVendorSerial;
            int[]? at;
            lock (k.State.Gate)
                if (v != 0 && k.State.Mobiles.TryGet(v, out at))
                {
                    hx = at[0];
                    hy = at[1];
                }
        }
        string seenAt = Cfg.Get(Server + "house.at", "");
        if (hx == 0 && hy == 0)
        {
            string[] s = seenAt.Split(',');
            if (s.Length == 2)
            {
                int.TryParse(s[0], out hx);
                int.TryParse(s[1], out hy);
            }
        }
        else if (seenAt != hx + "," + hy && UoMap.HouseAround(hx, hy) != null)
        {
            Cfg.Set(Server + "house.at", hx + "," + hy);
        }

        Home? h = HomeAt(hx, hy);
        if (h != null)
            h.Vendor = HouseVendorSerial;
        return h;
    }
    // The house holding the tile, its footprint, outer door and way out; null when no house the server sent holds it.
    static Home? HomeAt(int hx, int hy)
    {
        int[]? box = UoMap.HouseAround(hx, hy);
        if (box == null)
            return null;
        int[]? door = null;
        int bd = int.MaxValue;
        foreach (int[] d in UoMap.HouseDoors(hx, hy))
        {
            int e = Math.Min(Math.Min(d[0] - box[0], box[2] - d[0]), Math.Min(d[1] - box[1], box[3] - d[1]));
            if (e < bd)
            {
                bd = e;
                door = d;
            }
        }
        if (door == null)
            return null;
        Home h = new()
        {
            X0 = box[0],
            Y0 = box[1],
            X1 = box[2],
            Y1 = box[3],
            Floor = door[2],
            DoorX = door[0],
            DoorY = door[1],
            DoorArt = door[3]
        };
        int[] best = new[] { door[1] - box[1], box[3] - door[1], door[0] - box[0], box[2] - door[0] };
        int m = Math.Min(Math.Min(best[0], best[1]), Math.Min(best[2], best[3]));
        if (m == best[1])
            h.OutY = 1;
        else if (m == best[0])
            h.OutY = -1;
        else if (m == best[3])
            h.OutX = 1;
        else
            h.OutX = -1;
        return h;
    }
    // Inside: within the footprint, not past the door on the way out (the door's own tile is inside), and under the
    // house's own floor at the door's height. The footprint takes in the eaves, roof over grass outside the walls:
    // 0x6C's x 2483, where the house floor's first stash was put (2026-10-20 20:43:51).
    static bool InHome(Home h, int x, int y)
    {
        if (x < h.X0 || x > h.X1 || y < h.Y0 || y > h.Y1 || (x - h.DoorX) * h.OutX + (y - h.DoorY) * h.OutY > 0)
            return false;
        List<UoTiles.Tile> items = UoTiles.Items();
        foreach (int[] p in UoMap.MultiAt(x, y))
            if (p[1] == h.Floor && p[0] >= 0 && p[0] < items.Count && (items[p[0]].Flags & 0x200) != 0
                && (items[p[0]].Flags & TileImpassable) == 0)
                return true;
        return false;
    }
    AssistItem? HomeDoor(GameLink k, Home h)
    {
        AssistItem? d = null;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (IsDoor(g.Graphic) && Tiles(g.X, g.Y, h.DoorX, h.DoorY) <= 1
                    && (d == null || Tiles(g.X, g.Y, h.DoorX, h.DoorY) < Tiles(d.X, d.Y, h.DoorX, h.DoorY)))
                    d = g;
        return d;
    }
    static bool Shut(Home h, AssistItem d)
    {
        return d != null && d.X == h.DoorX && d.Y == h.DoorY && d.Graphic == h.DoorArt;
    }
    uint KeyInPack(GameLink k)
    {
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && IsKey(it.Graphic))
                    return it.Serial;
        }
        return 0;
    }
    // Whoever in view could kill us: a human body other than the house vendor that the atlas does not know as a
    // shopkeeper, banker, healer, guard or NPC, and every threat creature; their names, empty when none.
    List<string> HomeWatchers(GameLink k, Home home)
    {
        List<string> l = new();
        Map<uint, string> types = Map<uint, string>.Empty;
        if (Atlas != null)
            foreach (AtlasNpc n in Atlas.Visible())
                types = types.Set(n.Serial, n.Type);
        List<uint> mine = PetSerials();
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
            {
                if (kv.Key == k.State.Player || OwnVendor(kv.Key)
                    || Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) > HomeViewTiles)
                    continue;
                int b;
                if (!k.State.Bodies.TryGet(kv.Key, out b))
                    continue;
                string? t;
                types.TryGet(kv.Key, out t);
                bool threat = Threat(k, kv.Key, mine), person = (b == 0x190 || b == 0x191)
                    && s_homeSafeTypes.IndexOf(t ?? "") < 0;
                string? name;
                k.State.Names.TryGet(kv.Key, out name);
                if (person && name != null && HomeIgnored.Contains(name.Trim().ToLowerInvariant()))
                    continue;
                // A player inside another house's walls is no threat to the door (Damian, 2026-10-20: "the security
                // problem of a guy in the house next to me is basically zero ... the security problem of a guy 2 tiles
                // away hidden with a poison dagger in their hand is much greater"); the sweep is for that one.
                if (person && !threat)
                {
                    int[]? hb = UoMap.HouseAround(kv.Value[0], kv.Value[1]);
                    if (hb != null && (home == null || hb[0] != home.X0 || hb[1] != home.Y0))
                        continue;
                }
                if (threat || person)
                    l.Add((name ?? kv.Key.ToString("X")) + " at " + kv.Value[0] + "," + kv.Value[1]);
            }
        }

        return l;
    }
    // Waits at the door until nobody who could kill us is in view, HomeClearWaitMs at most. Null, or why not.
    string? HomeClear(GameLink k, Home h)
    {
        DateTime until = Clock.UtcNow.AddMilliseconds(HomeClearWaitMs);
        bool said = false;
        while (!circuitStop && !k.Closed)
        {
            List<string> w = HomeWatchers(k, h);
            if (w.Count == 0)
                return null;
            if (Clock.UtcNow > until)
                return "people near the house door for " + HomeClearWaitMs / 1000 + " s: " + string.Join(", ", w);
            if (!said)
            {
                Note("House: waiting at the door until nobody is near: " + string.Join(", ", w));
                CircuitStatus = "House: waiting for the door to be clear";
                said = true;
            }
            Clock.Sleep(1000);
        }
        return "stopped";
    }
    // The door used once and its swing awaited; the journal lines since. Null, or why not.
    string? HomeUseDoor(GameLink k, Home h, out List<string> said)
    {
        said = new List<string>();
        AssistItem? d = HomeDoor(k, h);
        if (d == null)
            return "no door in view at " + h.DoorX + "," + h.DoorY;
        bool was = Shut(h, d);
        Heard mark;
        BusyWait(clickedAt);
        using (Act(k))
        {
            mark = new(k.State);
            string? w = k.Use(d.Serial, false);
            clickedAt = Clock.UtcNow;
            if (w != null)
                return "using the door: " + w;
        }
        Wait.Until(Clock, () => Shut(h, HomeDoor(k, h)!) != was, DoorOpenMs, "the door", stepMs: 50);
        said = mark.Answers();
        if (Shut(h, HomeDoor(k, h)!) == was)
            return "the door did not " + (was ? "open" : "shut")
                + (said.Count > 0 ? ": \"" + said[said.Count - 1] + "\"" : "");
        return null;
    }
    // Whether the door is locked, by its own lines since mark: with the key in the pack a locked door opens and shuts
    // for the player and says so ("You quickly unlock, use, and relock the door.", "That is locked, but is usable from
    // the inside."), barked over the player as the player's own line (serial the player's, hue 0x5A, 2026-10-24
    // 13:19:25), so Answers, which drops the player's lines, never held them. Either within AnswerMs, and the key is
    // not turned (Damian, 2026-10-24: "if we take the key in hand to open and close the door on the way in and out, we
    // do not need to unlock and relock the door"; every entry and exit of 13:04 to 13:19 turned it twice).
    bool DoorLocked(Heard mark)
    {
        bool got = false;
        Wait.Until(Clock,
            () => got = mark.Lines().Exists(j => j.Contains("You quickly unlock")
            || j.Contains("usable from the inside")), AnswerMs, "the door's lock line", stepMs: 50);
        return got;
    }
    // The keyring used and aimed at the door; want "lock" or "unlock" as its answer. Null, or why not.
    string? HomeKeyTurn(GameLink k, Home h, uint key, string want)
    {
        AssistItem? d = HomeDoor(k, h);
        if (d == null)
            return "no door in view";
        BusyWait(clickedAt);
        using (Act(k))
        {
            Heard mark = new(k.State);
            string? w = k.Use(key, true);
            clickedAt = Clock.UtcNow;
            if (w != null)
                return "using the key: " + w;
            AwaitCursor(k, Clock.UtcNow.AddMilliseconds(AnswerMs));
            if (!k.MacroCursor)
            {
                k.Settle();
                return "the key opened no target cursor";
            }
            w = k.Target(false, d.Serial, d.X, d.Y, d.Z, d.Graphic);
            if (w != null)
                return "aiming the key: " + w;
            string? got = null;
            Wait.Until(Clock,
                () => { foreach (string j in mark.Answers()) if (j.Contains("You lock ") || j.Contains("You unlock ")
                || j.Contains("You do not have a key")) got = j; return got != null; }, AnswerMs, "the key's answer",
                stepMs: 50);
            if (got == null)
                return "the key's turn had no answer";
            if (!got.Contains("You " + want + " "))
                return "the key answered \"" + got + "\"";
            return null;
        }
    }
    // The door locked: a turn that answers "You unlock" has shown the door was locked, and that answer asks for the one
    // turn more that locks it again (2026-10-20 20:28:05, a door read as unlocked). Null, or why not.
    string? HomeLock(GameLink k, Home h, uint key)
    {
        string? w = HomeKeyTurn(k, h, key, "lock");
        if (w == null)
            return null;
        if (w.IndexOf("You unlock ", StringComparison.Ordinal) < 0)
            return w;
        Note("House: the key unlocked the door, so it was locked; turning it back");
        return HomeKeyTurn(k, h, key, "lock");
    }
    // The door's own answers speak over the player, and the journal names the speaker ("Kwik: That is locked, ..."):
    // matched anywhere in the line.
    static bool Heard(List<string> said, string line)
    {
        return said.Exists(j => j.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    // Where the key is put back on the vendor and taken off it (Damian, 2026-10-21: "leave house securely by walking
    // out the front door on to the doorstep, going 1 tile east to the guard zone, then putting the key back in the
    // vendor pouch"; the way in, "from the same spot on the porch in guarded zone"): the doorstep when it is guarded,
    // else the first open tile beside it outside the house that is (east, south, west, north, then the corners); the
    // doorstep when none is.
    static readonly ImmutableArray<int> s_keySpotDirs = [ 2, 4, 6, 0, 1, 3, 5, 7 ];
    int[] HomeKeySpot(GameLink k, Home h)
    {
        int[] step = new[] { h.StepX, h.StepY };
        if (!UoRegions.Loaded)
            return step;
        bool g;
        int sz = UoMap.LandStandZ(h.StepX, h.StepY);
        UoRegions.Zone(h.StepX, h.StepY, sz == int.MinValue ? h.Floor : sz, out g);
        if (g)
            return step;
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        foreach (int i in s_keySpotDirs)
        {
            int x = h.StepX + s_dX[i], y = h.StepY + s_dY[i], lz = UoMap.LandStandZ(x, y);
            if (InHome(h, x, y) || lz == int.MinValue || Open(k, x, y, lz, ref seen) == int.MinValue)
                continue;
            UoRegions.Zone(x, y, lz, out g);
            if (g)
                return new[] { x, y };
        }
        return step;
    }
    bool AtKeySpot(GameLink k, Home h, int x, int y)
    {
        int[] s = HomeKeySpot(k, h);
        return x == s[0] && y == s[1];
    }
    // The key back on the vendor from the key spot, walked to first. Null, or why not.
    string? HomeKeyBack(GameLink k, Home h)
    {
        int[] s = HomeKeySpot(k, h);
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (px != s[0] || py != s[1])
        {
            CircuitStatus = "House: to the key spot";
            string? w = WalkToIn(s[0], s[1], 0, false, AnyFloor, false);
            if (w != null)
                return "to the key spot " + s[0] + "," + s[1] + ": " + w + "; the key stays in the pack";
        }
        return HomeKeyReturn(k, h);
    }
    // The tile in front of the door checked before the door is opened or unlocked (Damian, 2026-10-21: "ensure all
    // security sweeps and all enterences to locked doors on our houses check the tile immediatly in front of the door
    // before opening or unlocking"; "that one spot is critical"): standing on the doorstep is the check, as nobody
    // hidden can share it; else the step onto it, and a refused step onto an open doorstep with nobody seen there is
    // someone hidden on it, searched once, and the door is left alone. Null, or why not.
    string? HomeDoorstep(GameLink k, Home h)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (px == h.StepX && py == h.StepY)
            return null;
        DateTime since = Clock.UtcNow;
        long key = ((long)h.StepX << 16) | (uint)h.StepY;
        CircuitStatus = "House: onto the doorstep";
        string? w = WalkToIn(h.StepX, h.StepY, 0, false, AnyFloor, false);
        DateTime at;
        bool refused;
        refused = m_walls.TryGet(key, out at) && at >= since;
        bool seen = false;
        lock (k.State.Gate)
            foreach (int[] m in k.State.Mobiles.Values)
                if (m[0] == h.StepX && m[1] == h.StepY)
                    seen = true;
        if (refused && !seen)
        {
            lock (m_wallsGate)
                m_walls = m_walls.Remove(key);
            int z = UoMap.LandStandZ(h.StepX, h.StepY);
            return "something hidden blocks the doorstep " + h.StepX + "," + h.StepY + "; "
                + HomeSearch(k, h.StepX, h.StepY, z == int.MinValue ? h.Floor : z);
        }
        if (w != null)
            return "onto the doorstep: " + w;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        return px == h.StepX && py == h.StepY ? null : "not on the doorstep (at " + px + "," + py + ")";
    }
    // The key taken at the key spot, and back to the doorstep unless back is false. Null, or why not.
    string? HomeKeyFetch(GameLink k, Home h, bool back = true)
    {
        int[] s = HomeKeySpot(k, h);
        string? w;
        if (s[0] != h.StepX || s[1] != h.StepY)
        {
            CircuitStatus = "House: to the key spot";
            w = WalkToIn(s[0], s[1], 0, false, AnyFloor, false);
            if (w != null)
                return "to the key spot " + s[0] + "," + s[1] + ": " + w;
        }
        w = HomeKeyTake(k, h);
        if (w != null)
            return w;
        if (back && (s[0] != h.StepX || s[1] != h.StepY))
        {
            w = HomeDoorstep(k, h);
            if (w != null)
                return "back to the doorstep with the key: " + w;
        }
        return null;
    }
    // The key off the vendor's pack into ours, at the key spot. Null, or why not.
    // A registered house's own ring only, and never with another key in the pack (AssistHouses.md: "a keyring with all
    // the houses keys is a recipe for disaster"; never two rings carried).
    string? HomeKeyTake(GameLink k, Home h)
    {
        uint held = KeyInPack(k);
        if (held != 0 && held != h.Ring)
            return "another key " + held.ToString("X") + " is in the pack; one ring at a time";
        string? w;
        uint pack = VendorOpen(k, out w, h.Vendor);
        if (pack == 0)
            return "opening the vendor for the key: " + w;
        AssistItem? key = null;
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == pack && IsKey(it.Graphic)
                    && (h.Ring != 0 ? it.Serial == h.Ring : key == null || it.Graphic >= 0x1769))
                    key = it;
        if (key == null)
            return h.Ring != 0 ? "the ring " + h.Ring.ToString("X") + " is not on the vendor" : "no key on the vendor";
        uint backpack;
        lock (k.State.Gate)
            backpack = k.State.Backpack;
        using (Act(k))
            w = MoveStack(k, key.Serial, 1, "the house key", "Taking the house key",
                () => k.Drop(key.Serial, backpack)!);
        if (w != null)
            return w;
        return KeyInPack(k) == key.Serial ? null : "the key did not come into the pack";
    }
    // The key back on the vendor at its top left, not for sale, checked by its Cost line; taken straight back into the
    // pack when the vendor prices it. Null, or why not.
    string? HomeKeyReturn(GameLink k, Home h)
    {
        uint key = KeyInPack(k);
        if (key == 0)
            return null;
        if (h.Ring != 0 && key != h.Ring)
            return "the key " + key.ToString("X") + " in the pack is not this house's ring " + h.Ring.ToString("X");
        string? w;
        uint pack = VendorOpen(k, out w, h.Vendor);
        if (pack == 0)
            return "opening the vendor for the key: " + w;
        w = VendorGiveOne(k, pack,
            new VendorEntry { Serial = key, Amount = 1, NotForSale = true, Description = "House keys",
            What = "the house key", X = KeyX, Y = KeyY }, 0);
        if (w == null)
        {
            Note("House: the key is back on the vendor");
            return null;
        }
        AssistItem? it = Item(k, key);
        bool onVendor;
        lock (k.State.Gate)
            onVendor = it != null && it.Container == pack;
        if (onVendor)
        {
            uint backpack;
            lock (k.State.Gate)
                backpack = k.State.Backpack;
            string? tw;
            using (Act(k))
                tw = MoveStack(k, key, 1, "the house key", "Taking the house key back", () => k.Drop(key, backpack)!);
            return "putting the key back: " + w
                + (tw == null ? "; taken back into the pack" : "; AND IT STAYS ON THE VENDOR: " + tw);
        }
        return "putting the key back: " + w;
    }
    // The door sweep before going in (Damian, 2026-10-20: "a little circuit which is pure secops security scan. first
    // you run to all the spots within (n) of the door to be sure nothing is there"; "if you get the warning about
    // blocking, you can use detect or reveal"): every open tile outside the house within HomeSweepTiles of the doorstep
    // walked onto, in rows. A step the server refuses onto a tile the map holds open with nobody seen on it is someone
    // hidden there (no relay log holds a line for it; the refusal is the sign): that tile is searched once by Detecting
    // Hidden, and the door is left alone. From inside nothing outside can be walked, so the way out has only the check
    // by sight.
    public const int HomeSweepTiles = 2, DetectHiddenSkill = 14, RevealSpell = 48;
    public bool HomeSweepOn
    {
        get => Cfg.Get(Server + "house.sweep", "on") == "on";
        set => Cfg.Set(Server + "house.sweep", value ? "on" : "off");
    }
    // The first return of a circuit is always swept; later ones by the setting (Damian, 2026-10-21: "1 sweep on the
    // first return is good, but after that respect the setting").
    volatile bool m_sweptThisCircuit;
    string? HomeSweep(GameLink k, Home h)
    {
        bool first = CircuitRunning && !m_sweptThisCircuit;
        if (!HomeSweepOn && !first)
            return null;
        if (CircuitRunning)
            m_sweptThisCircuit = true;
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        List<int[]> tiles = new();
        for (int dy = -HomeSweepTiles; dy <= HomeSweepTiles; dy++)
        {
            List<int[]> row = new();
            for (int dx = -HomeSweepTiles; dx <= HomeSweepTiles; dx++)
            {
                int x = h.StepX + dx, y = h.StepY + dy, lz = UoMap.LandStandZ(x, y);
                if (InHome(h, x, y) || lz == int.MinValue || Open(k, x, y, lz, ref seen) == int.MinValue)
                    continue;
                row.Add(new[] { x, y });
            }
            if ((dy & 1) != 0)
                row.Reverse();
            tiles.AddRange(row);
        }
        // The doorstep first (HomeDoorstep): the one tile in front of the door.
        int at = tiles.FindIndex(t => t[0] == h.StepX && t[1] == h.StepY);
        if (at > 0)
        {
            int[] d = tiles[at];
            tiles.RemoveAt(at);
            tiles.Insert(0, d);
        }
        DateTime since = Clock.UtcNow;
        CircuitStatus = "House: sweeping " + tiles.Count + " tiles by the door";
        foreach (int[] t in tiles)
        {
            if (circuitStop || k.Closed)
                return "stopped";
            WalkToIn(t[0], t[1], 0, false, AnyFloor, false);
            int[]? hid = null;
            foreach (KeyValuePair<long, DateTime> kv in m_walls)
            {
                if (kv.Value >= since)
                {
                    int wx = (int)(kv.Key >> 16), wy = (int)(kv.Key & 0xFFFF), lz = UoMap.LandStandZ(wx, wy);
                    bool someone = false;
                    lock (k.State.Gate)
                        foreach (int[] m in k.State.Mobiles.Values)
                            if (m[0] == wx && m[1] == wy)
                                someone = true;
                    ValueMap<long, int> fresh = ValueMap<long, int>.Empty;
                    if (!someone && lz != int.MinValue && Open(k, wx, wy, lz, ref fresh, true) != int.MinValue)
                    {
                        hid = new[] { wx, wy, lz };
                        break;
                    }
                }
            }

            if (hid != null)
            {
                lock (m_wallsGate)
                    m_walls = m_walls.Remove(((long)hid[0] << 16) | (uint)hid[1]);
                return "something hidden blocks " + hid[0] + "," + hid[1] + " by the door; "
                    + HomeSearch(k, hid[0], hid[1], hid[2]);
            }
        }
        Note("House: the door's " + tiles.Count + " tiles swept, nothing hidden");
        return null;
    }
    // One search of a tile by Detecting Hidden; what the server said. Not Reveal: the 1998 spell undoes only the
    // Invisibility spell (revealbase.m, "reminvis"), never the Hiding skill. Detecting Hidden finds the hidden within 2
    // tiles of the spot aimed at, only on a passed skill check, and a failed check answers as a clear search does ("You
    // can see nothing hidden there.", detcthid.m), so that answer is no all-clear.
    string HomeSearch(GameLink k, int x, int y, int z)
    {
        if (SkillOf(k, DetectHiddenSkill) <= 0)
            return "no Detecting Hidden skill to search with";
        bool detect = true;
        BusyWait(clickedAt);
        using (Act(k))
        {
            Heard mark = new(k.State);
            string? w = detect ? k.Skill(DetectHiddenSkill, true) : k.Cast(RevealSpell, true);
            clickedAt = Clock.UtcNow;
            if (w != null)
                return (detect ? "Detecting Hidden" : "Reveal") + ": " + w;
            AwaitCursor(k, Clock.UtcNow.AddMilliseconds(detect ? AnswerMs : GameLink.CastClaimMs));
            if (!k.MacroCursor)
            {
                k.Settle();
                return (detect ? "Detecting Hidden" : "Reveal") + " opened no cursor";
            }
            w = k.Target(true, 0, x, y, z, 0);
            if (w != null)
                return "aiming the search: " + w;
            Clock.Sleep(AnswerMs);
            List<string> said = mark.Answers();
            string r = (detect ? "Detecting Hidden" : "Reveal") + " at " + x + "," + y
                + (said.Count > 0 ? ": \"" + said[said.Count - 1] + "\"" : ": no answer");
            Note("House: " + r);
            return r;
        }
    }
    // The ring of searches before the key, off unless set (Damian, 2026-10-20: "an optional extra precaution"; "if we
    // were in war mode with a guild, sure"): Detecting Hidden aimed at the doorstep and at the eight spots
    // HomeRingTiles out, each search covering 2 tiles round its spot (15 x 15 about the door); a spot inside the house
    // is skipped. Any answer but "You can see nothing hidden there.", or a new person in view after it, leaves the key
    // on the vendor. A "You must wait" answer asks for the same search again, after HomeRingWaitMs, HomeRingTries in
    // all.
    public const int HomeRingTiles = 5, HomeRingWaitMs = 2000, HomeRingTries = 5;
    public bool HomeRingOn
    {
        get => Cfg.Get(Server + "house.ring", "off") == "on";
        set => Cfg.Set(Server + "house.ring", value ? "on" : "off");
    }
    string? HomeRing(GameLink k, Home h)
    {
        if (!HomeRingOn)
            return null;
        if (SkillOf(k, DetectHiddenSkill) <= 0)
            return "the ring of searches is set, and there is no Detecting Hidden skill";
        int done = 0;
        for (int i = -1; i < 8 && !circuitStop; i++)
        {
            int x = h.StepX + (i < 0 ? 0 : s_dX[i] * HomeRingTiles), y = h.StepY
                + (i < 0 ? 0 : s_dY[i] * HomeRingTiles), z = UoMap.LandStandZ(x, y);
            if (InHome(h, x, y) || z == int.MinValue)
                continue;
            int seenBefore = HomeWatchers(k, h).Count;
            string? r = null;
            for (int t = 0; t < HomeRingTries && !circuitStop; t++)
            {
                CircuitStatus = "House: searching " + x + "," + y;
                r = HomeSearch(k, x, y, z);
                if (!s_waitLines.Exists(wl => r.IndexOf(wl, StringComparison.OrdinalIgnoreCase) >= 0))
                    break;
                Clock.Sleep(HomeRingWaitMs);
            }
            if (r == null || r.IndexOf("You can see nothing hidden there", StringComparison.OrdinalIgnoreCase) < 0)
                return "the search at " + x + "," + y + " did not come back clear: " + r;
            if (HomeWatchers(k, h).Count > seenBefore)
                return "someone came into view after the search at " + x + "," + y + ": "
                    + string.Join(", ", HomeWatchers(k, h));
            done++;
        }
        Note("House: " + done + " searches about the door, nothing hidden found");
        return null;
    }
    // In through the door from outside: the doorstep, nobody near, the sweep, the key, the door shut first, opened,
    // passed, shut behind, and locked from inside when it was not. Null, or why not.
    // The Moonglow Astronomical Society's way in (Damian, 2026-10-21: "recall to the safe spot, take 2 steps to the
    // tile in front of the door, take one step back to the guard zone, open the vendor pack, grab key, open door, run
    // in, close"): the doorstep walked onto first (HomeDoorstep), the key taken at the key spot, the door opened from
    // there when it is in reach, and the run in across the doorstep; a run in that fails shuts the door again.
    string? HomeEnter(GameLink k, Home h)
    {
        string? w = HomeDoorstep(k, h);
        if (w != null)
            return "to the doorstep: " + w;
        w = HomeClear(k, h);
        if (w != null)
            return w;
        w = HomeSweep(k, h);
        if (w != null)
            return w;
        w = HomeDoorstep(k, h);
        if (w != null)
            return "back to the doorstep: " + w;
        w = HomeRing(k, h);
        if (w != null)
            return w;
        w = HomeClear(k, h);
        if (w != null)
            return w;
        if (KeyInPack(k) == 0 || KeyInPack(k) != h.Ring && h.Ring != 0)
        {
            w = HomeKeyFetch(k, h, false);
            if (w != null)
                return "taking the key: " + w;
        }
        List<string> said;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (!(AtKeySpot(k, h, px, py) && Tiles(px, py, h.DoorX, h.DoorY) <= 1))
        {
            w = HomeDoorstep(k, h);
            if (w != null)
                return w;
        }
        Heard doorMark = new(k.State);
        if (!Shut(h, HomeDoor(k, h)!))
        {
            w = HomeUseDoor(k, h, out said);
            if (w != null)
                return "shutting the door first: " + w;
        }
        w = HomeUseDoor(k, h, out said);
        if (w != null)
            return "opening the door: " + w;
        CircuitStatus = "House: in";
        w = WalkToIn(h.InX, h.InY, 0, false, AnyFloor, false);
        if (w != null)
        {
            List<string> again;
            string? sw = HomeUseDoor(k, h, out again);
            return "running in: " + w + (sw == null ? "; the door shut again" : "; AND THE DOOR DID NOT SHUT: " + sw);
        }
        w = HomeUseDoor(k, h, out said);
        if (w != null)
            return "shutting the door behind: " + w;
        if (!DoorLocked(doorMark))
        {
            w = HomeLock(k, h, KeyInPack(k));
            if (w != null)
                return "locking the door from inside: " + w;
        }
        Note("House: in" + (h.Name.Length > 0 ? " at " + h.Name : "")
            + ", the door shut and locked, the key in the pack");
        return null;
    }
    // Out through the door: nobody near, the door opened from inside, the doorstep, the door shut with the key (and
    // locked when it was not), and the key back on the vendor from the doorstep. Null, or why not; on a failure at the
    // doorstep the walk goes nowhere, so the key never leaves it.
    string? HomeLeave(GameLink k, Home h)
    {
        CircuitStatus = "House: to the door";
        string? w = WalkToIn(h.InX, h.InY, 0, false, AnyFloor, false);
        if (w != null)
            return "to the door: " + w;
        w = HomeClear(k, h);
        if (w != null)
            return w;
        List<string> said;
        Heard doorMark = new(k.State);
        if (Shut(h, HomeDoor(k, h)!))
        {
            w = HomeUseDoor(k, h, out said);
            if (w != null)
                return "opening the door: " + w;
        }
        // The doorstep cannot be walked from inside before the door opens: a step out refused there shuts the door
        // again.
        w = HomeDoorstep(k, h);
        if (w != null)
        {
            List<string> again;
            string? sw = HomeUseDoor(k, h, out again);
            return "stepping out: " + w + (sw == null ? "; the door shut again" : "; AND THE DOOR DID NOT SHUT: " + sw);
        }
        // On the doorstep the key goes back on the vendor whatever the door did (Damian, 2026-10-20: "the door is still
        // unlocked and the key was left in backpack and not on vendor"); a door left open or unlocked is said, and ends
        // the walk.
        string? fail = null;
        if (KeyInPack(k) == 0)
        {
            w = HomeKeyFetch(k, h);
            if (w != null)
                fail = "taking the key to shut the door: " + w;
        }
        if (fail == null)
        {
            w = HomeUseDoor(k, h, out said);
            if (w != null)
            {
                fail = "shutting the door: " + w;
            }
            else if (!DoorLocked(doorMark))
            {
                w = HomeLock(k, h, KeyInPack(k));
                if (w != null)
                    fail = "locking the door: " + w + " (THE DOOR MAY BE UNLOCKED)";
            }
        }
        w = HomeKeyBack(k, h);
        if (fail != null)
            return fail + (w == null ? "; the key is back on the vendor" : "; and the key: " + w);
        if (w != null)
            return w;
        Note("House: out" + (h.Name.Length > 0 ? " of " + h.Name : "")
            + ", the door shut and locked, the key on the vendor");
        return null;
    }
    // A walk that crosses a locked house's door goes through HomeEnter or HomeLeave (out of one house and into another,
    // both); a walk near the key spot with the key in the pack puts it back first, and refuses when it cannot.
    string? HomeCross(GameLink k, int tx, int ty)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        Home? hf = LockedHomeAt(k, px, py);
        Home? ht = LockedHomeAt(k, tx, ty);
        bool fromIn = hf != null && InHome(hf, px, py), toIn = ht != null && InHome(ht, tx, ty);
        bool same = fromIn && toIn && hf!.DoorX == ht!.DoorX && hf.DoorY == ht.DoorY;
        if (fromIn && !same)
        {
            string? w = HomeLeave(k, hf!);
            if (w != null || !toIn)
                return w;
            return HomeEnter(k, ht!);
        }
        if (!fromIn && toIn)
            return HomeEnter(k, ht!);
        uint key = KeyInPack(k);
        if (fromIn || key == 0)
            return null;
        Home? h = HomeOfKey(k, key);
        if (h == null)
            return null;
        if (tx == h.StepX && ty == h.StepY || AtKeySpot(k, h, tx, ty))
            return null;
        // Within HomeKeyBackTiles of its key spot the key goes back on the vendor first (Damian, 2026-10-24: "its
        // supposed to put the key back on the vendor"; "it probably should check that the home for the key is only a
        // few tiles away and put it back"): a chop started beside the house refused every tree while the key stayed in
        // the pack. Farther off the key is noted once and the walk goes on; it goes into the bank box before the next
        // recall.
        int[] spot = HomeKeySpot(k, h);
        if (Tiles(px, py, spot[0], spot[1]) <= HomeKeyBackTiles)
            return HomeKeyBack(k, h);
        if (m_keyAwayNoted != key)
        {
            m_keyAwayNoted = key;
            Note("House: the house key " + key.ToString("X") + " is in the pack " + Tiles(px, py, h.DoorX, h.DoorY)
                + " tiles from the house; it goes into the bank box before the next recall");
        }
        return null;
    }
    uint m_keyAwayNoted;
    public const int HomeKeyBackTiles = 5;
    // Before a recall, any circuit's (Damian, 2026-10-20: "integrate the key and security protocol with the tailoring
    // and other circuits as appropriate"; 2026-10-22: "the circuit can never travel with the house key and is an
    // emergency situation if discovered and the key should be put on the vendor to the house not for sale immediatly.
    // if that is delayed by movement, or refused or not possible, then it should be put in the bank. the circuit can
    // continue at that point."): inside the house the way out is HomeLeave, which puts it back on the vendor from the
    // doorstep; on the doorstep or the key spot it goes back on the vendor; anywhere else, or when the vendor did not
    // take it, it goes into the bank box (HomeKeyBank) and the recall goes on.
    string? HomeKeyBeforeTravel(GameLink k)
    {
        if (m_keyToBank)
            return null;
        uint key = KeyInPack(k);
        if (key == 0)
            return null;
        if (!HousesKnown() && !(HouseLocked && HouseVendorSerial != 0))
            return null;
        Home? h = HomeOfKey(k, key);
        string? w;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (h != null && InHome(h, px, py))
            w = HomeLeave(k, h);
        else if (h != null && (px == h.StepX && py == h.StepY || AtKeySpot(k, h, px, py)))
            w = HomeKeyBack(k, h);
        else
            w = h == null ? "no house of the key in view" : "away from its house (" + px + "," + py + ")";
        if (!KeyHeld(k, key))
            return w;
        Note("House: EMERGENCY: the house key " + key.ToString("X")
            + " is in the pack before a recall, not put on the vendor (" + (w ?? "it stayed in the pack")
            + "); into the bank box");
        string? bw = HomeKeyBank(k, key);
        return bw == null ? null
            : "the key " + key.ToString("X") + " is in the pack: not on the vendor (" + (w ?? "it stayed in the pack")
            + "), and not into the bank: " + bw;
    }
    bool KeyHeld(GameLink k, uint key)
    {
        lock (k.State.Gate)
        {
            AssistItem? it = Item(k, key);
            return it != null && PackTree(k).Contains(it.Container);
        }
    }
    // The key's one recall is the one to a bank (RecallNearBank), past BankWalkTiles from the town's bank.
    volatile bool m_keyToBank;
    // The house key into the bank box: a recall to a bank first when the town's is a long walk, the bank walked to, the
    // box opened and the key dropped in it. Null, or why not.
    string? HomeKeyBank(GameLink k, uint key)
    {
        m_keyToBank = true;
        try
        {
            RecallNearBank(k);
        }
        finally { m_keyToBank = false; }
        string? city = WalkTown(k);
        if (city == null)
            return "not in a town, and no rune in the pack lands by a bank";
        string? w = ToCityBank(k, city, "the house key");
        if (w != null)
            return w;
        using (Act(k))
        {
            byte[]? open = SayBank(k);
            if (open == null)
                return NoBanker();
            uint box = BankBox(open);
            w = MoveStack(k, key, 1, "the house key", "Banking the house key", () => k.Drop(key, box)!);
            if (w != null)
                return w;
        }
        if (KeyHeld(k, key))
            return "the key did not leave the pack";
        Note("House: the house key " + key.ToString("X") + " is in the bank box in " + city);
        return null;
    }
    // ---------- many houses (docs/Designs/Active/Apps/AssistHouses.md) ----------
    // A registered house (Damian, 2026-10-21: "the player has to register their house as a step, the code should scan
    // the house for the features that make it stand out in a role like forge or loom"; "every house should be setup
    // with vendor and key on its own ring stored on the vendor"). DoorX, DoorY: its outer door, the tile that finds it
    // again; Private: locked, the key routine at its door; public houses stay unlocked and their door is walked as any
    // door. Devices: what the scan found inside, "kind x,y" each, ";" between.
    public sealed class HouseRec
    {
        public string Name = "", Devices = "";
        public int DoorX, DoorY; public uint Vendor, Ring; public bool Private = true;
    }
    readonly List<HouseRec> m_houses = new();
    bool m_housesLoaded; public long HouseSeq; public volatile string HouseStatus = "";
    string HousesPath => ServerFile("houses");
    // One line a house in houses-<server>.txt, tab separated: H name door-x door-y vendor ring private|public devices.
    void LoadHouses()
    {
        lock (m_houses)
        {
            if (m_housesLoaded)
                return;
            m_housesLoaded = true;
            try
            {
                if (!File.Exists(HousesPath))
                    return;
                foreach (string line in File.ReadAllLines(HousesPath))
                {
                    string[] t = line.Split('\t');
                    if (t[0] != "H" || t.Length < 8)
                        continue;
                    m_houses.Add(new HouseRec { Name = t[1], DoorX = int.Parse(t[2]), DoorY = int.Parse(t[3]),
                        Vendor = uint.Parse(t[4], NumberStyles.HexNumber),
                        Ring = uint.Parse(t[5], NumberStyles.HexNumber), Private = t[6] != "public", Devices = t[7] });
                }
            }
            catch (Exception e) { Note("Houses: the house list did not load: " + e.Message); }
        }
    }
    void SaveHouses()
    {
        lock (m_houses)
        {
            List<string> lines = new();
            foreach (HouseRec h in m_houses)
                lines.Add(string.Join("\t", "H", Clean(h.Name), h.DoorX.ToString(), h.DoorY.ToString(),
                    h.Vendor.ToString("X"), h.Ring.ToString("X"), h.Private ? "private" : "public", Clean(h.Devices)));
            try
            {
                File.WriteAllLines(HousesPath, lines);
            }
            catch (Exception e) { Note("Houses: the house list did not save: " + e.Message); }
        }
        Interlocked.Increment(ref HouseSeq);
    }
    public List<HouseRec> Houses()
    {
        LoadHouses();
        lock (m_houses)
            return new List<HouseRec>(m_houses);
    }
    bool HousesKnown()
    {
        LoadHouses();
        lock (m_houses)
            return m_houses.Count > 0;
    }
    // Every vendor of ours (the registered houses' and the Vendor tab's) never steps away and holds no door.
    bool OwnVendor(uint s)
    {
        if (s == 0)
            return false;
        if (s == HouseVendorSerial)
            return true;
        LoadHouses();
        lock (m_houses)
            return m_houses.Exists(h => h.Vendor == s);
    }
    // A registered house as a Home, while the server has sent its multi; null when not in view.
    static Home? HomeOf(HouseRec r)
    {
        Home? h = HomeAt(r.DoorX, r.DoorY);
        if (h == null || h.DoorX != r.DoorX || h.DoorY != r.DoorY)
            return null;
        h.Vendor = r.Vendor;
        h.Ring = r.Ring;
        h.Name = r.Name;
        return h;
    }
    // The locked house holding the tile: a private registered house; with none registered, the one-house settings'
    // house while "Keep the house locked" is ticked.
    Home? LockedHomeAt(GameLink k, int x, int y)
    {
        if (!HousesKnown())
        {
            if (!HouseLocked || HouseVendorSerial == 0)
                return null;
            Home? l = HomeNow(k);
            return l != null && x >= l.X0 && x <= l.X1 && y >= l.Y0 && y <= l.Y1 ? l : null;
        }
        int[]? box = UoMap.HouseAround(x, y);
        if (box == null)
            return null;
        foreach (HouseRec r in Houses())
            if (r.Private && r.DoorX >= box[0] && r.DoorX <= box[2] && r.DoorY >= box[1] && r.DoorY <= box[3])
                return HomeOf(r);
        return null;
    }
    // The locked house whose outer door stands within a tile of x, y; null when none.
    Home? LockedHomeByDoor(GameLink k, int x, int y)
    {
        if (!HousesKnown())
        {
            if (!HouseLocked || HouseVendorSerial == 0)
                return null;
            Home? l = HomeNow(k);
            return l != null && Tiles(x, y, l.DoorX, l.DoorY) <= 1 ? l : null;
        }
        foreach (HouseRec r in Houses())
        {
            if (r.Private && Tiles(x, y, r.DoorX, r.DoorY) <= 1)
            {
                Home? h = HomeOf(r);
                if (h != null)
                    return h;
            }
        }

        return null;
    }
    // A door no walk uses: a locked house's, or a private registered house's within a tile by the record alone, its
    // multi resolved or not (2026-10-23 22:50:54: a collection's walk used the Moonglow house's door 4686014B, the
    // record's own door, and LockedHomeByDoor answered none).
    bool LockedHouseDoor(GameLink k, int x, int y)
    {
        if (LockedHomeByDoor(k, x, y) != null)
            return true;
        foreach (HouseRec r in Houses())
            if (r.Private && Tiles(x, y, r.DoorX, r.DoorY) <= 1)
            {
                Note("Walk: " + r.Name + " at " + r.DoorX + "," + r.DoorY
                    + " did not resolve from its multi; its door refused by the house record");
                return true;
            }
        return false;
    }
    // The house a key in the pack belongs to: the registered house whose ring it is; with none registered, the
    // one-house settings' house; null when unknown.
    Home? HomeOfKey(GameLink k, uint key)
    {
        if (!HousesKnown())
            return HouseLocked && HouseVendorSerial != 0 ? HomeNow(k) : null;
        foreach (HouseRec r in Houses())
            if (r.Ring == key)
                return HomeOf(r);
        return null;
    }
    // Registration (AssistHouses.md, "What has to change" 1): the house holding where the player stands (inside, or on
    // a tile beside it), its vendor in reach, exactly one key on that vendor (its ring), and its devices scanned. A
    // name or door already registered is replaced. Null, or why not.
    public string? HouseRegister(string name, uint vendor)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        Home? h = null;
        for (int d = 0; d <= 1 && h == null; d++)
            for (int i = -1; i < 8 && h == null; i++)
            {
                if ((d == 0) != (i < 0))
                    continue;
                h = HomeAt(px + (i < 0 ? 0 : s_dX[i]), py + (i < 0 ? 0 : s_dY[i]));
            }
        if (h == null)
            return "no house where you stand: stand inside it or beside it";
        name = Clean(name);
        if (name.Length == 0)
        {
            string? sw;
            name = Clean(SignName(k, h, out sw)!);
            if (name.Length == 0)
                return "no name given, and the sign: " + sw;
        }
        bool vendorSeen;
        lock (k.State.Gate)
            vendorSeen = k.State.Mobiles.ContainsKey(vendor);
        if (!vendorSeen)
            return "the vendor " + vendor.ToString("X") + " is not in view";
        LoadHouses();
        lock (m_houses)
            foreach (HouseRec o in m_houses)
                if (o.Name != name && !(o.DoorX == h.DoorX && o.DoorY == h.DoorY) && o.Vendor == vendor)
                    return "the vendor " + vendor.ToString("X") + " already serves " + o.Name + "; one vendor a house";
        string? w;
        uint pack = VendorOpen(k, out w, vendor);
        if (pack == 0)
            return "opening the vendor: " + w;
        List<AssistItem> keys = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == pack && IsKey(it.Graphic))
                    keys.Add(it);
        if (keys.Count == 0)
            return "no key ring on the vendor: put this house's ring on its vendor first";
        if (keys.Count > 1)
            return "the vendor holds " + keys.Count + " keys; one ring a house, on its own vendor";
        uint ring = keys[0].Serial;
        lock (m_houses)
            foreach (HouseRec o in m_houses)
                if (o.Name != name && !(o.DoorX == h.DoorX && o.DoorY == h.DoorY) && o.Ring == ring)
                    return "the ring " + ring.ToString("X") + " already opens " + o.Name;
        string devices = HouseScan(k, h);
        lock (m_houses)
        {
            HouseRec? was = m_houses.Find(o => o.Name == name || o.DoorX == h.DoorX && o.DoorY == h.DoorY);
            HouseRec r = new()
            {
                Name = name,
                DoorX = h.DoorX,
                DoorY = h.DoorY,
                Vendor = vendor,
                Ring = ring,
                Private = was == null || was.Private,
                Devices = devices
            };
            m_houses.RemoveAll(o => o.Name == name || o.DoorX == h.DoorX && o.DoorY == h.DoorY);
            m_houses.Add(r);
        }
        SaveHouses();
        HouseStatus = name + ": door " + h.DoorX + "," + h.DoorY + ", vendor " + vendor.ToString("X") + ", ring "
            + ring.ToString("X") + ", " + (devices.Length > 0 ? devices : "no devices (storage)") + ".";
        Note("Houses: registered " + HouseStatus);
        return null;
    }
    // The house's name off its sign (Damian, 2026-10-21: "it should pull the name from the sign"): the sign is a part
    // the server places as its own item by the footprint's edge, and one single click on it answers its name as the
    // first line (0x1C, "Moonglow Astronomical Society", then "This house is like new.", relay log 2026-10-21
    // 05:59:59).
    string? SignName(GameLink k, Home h, out string? why)
    {
        why = null;
        AssistItem? sign = null;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.X >= h.X0 - 1 && g.X <= h.X1 + 1 && g.Y >= h.Y0 - 1 && g.Y <= h.Y1 + 1
                    && g.Graphic >= HouseSignArt0 && g.Graphic <= HouseSignArt1)
                    sign = g;
        if (sign == null)
        {
            why = "no house sign in view";
            return "";
        }
        string? got = null;
        ManualResetEventSlim said = new(false);
        uint ss = sign.Serial;
        Action<byte[]> hnd = p => { if (p[0] == 0x1C && p.Length > 44 && U32(p, 3) == ss
            && got == null) { got = Encoding.ASCII.GetString(p, 44,
            p.Length - 44).TrimEnd('\0').Trim(); said.Set(); } };
        k.ServerPacket += hnd;
        try
        {
            string? w;
            using (Act(k))
            {
                BusyWait(clickedAt);
                w = k.Look(ss);
            }
            if (w != null)
            {
                why = "clicking the sign: " + w;
                return "";
            }
            if (!Clock.Wait(said, AnswerMs))
            {
                why = "the sign " + ss.ToString("X") + " said no name in " + AnswerMs + " ms";
                return "";
            }
        }
        finally { k.ServerPacket -= hnd; }
        return got;
    }
    // The role devices inside the footprint (AssistHouses.md: "forge or loom"; the anvil and the spinning wheel beside
    // them for smithing and spinning): the items on the house floor and the multi's own parts. "kind x,y" each, ";"
    // between.
    string HouseScan(GameLink k, Home h)
    {
        List<string> found = new();
        Set<string> at = Set<string>.Empty;
        Action<int, int, int> add = (g, x, y) =>
        {
            string? kind = IsForge(g) ? "forge"
                : IsAnvil(g) ? "anvil"
                : s_looms.IndexOf(g) >= 0 ? "loom" : s_wheels.IndexOf(g) >= 0 ? "spinning wheel" : null;
            if (kind != null && !at.Contains(kind + " " + x + "," + y))
            {
                at = at.Add(kind + " " + x + "," + y);
                found.Add(kind + " " + x + "," + y);
            }
        };
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.X >= h.X0 && g.X <= h.X1 && g.Y >= h.Y0 && g.Y <= h.Y1)
                    add(g.Graphic, g.X, g.Y);
        for (int x = h.X0; x <= h.X1; x++)
            for (int y = h.Y0; y <= h.Y1; y++)
                foreach (int[] p in UoMap.MultiAt(x, y))
                    add(p[0], x, y);
        return string.Join("; ", found);
    }
    public string? HouseMode(string name, bool priv)
    {
        LoadHouses();
        HouseRec? r;
        lock (m_houses)
            r = m_houses.Find(o => o.Name == name);
        if (r == null)
            return "no house named " + name;
        r.Private = priv;
        SaveHouses();
        HouseStatus = name + " is "
            + (priv ? "private: locked, the key routine at its door"
            : "public: the door unlocked, nothing of value stored") + ".";
        return null;
    }
    // The Vendor tab's house: the registered house whose vendor is the vendor in use (house.vendor); empty when none.
    public string VendorHouseName()
    {
        uint v = HouseVendorSerial;
        foreach (HouseRec h in Houses())
            if (h.Vendor == v && v != 0)
                return h.Name;
        return "";
    }
    public string? VendorHousePick(string name)
    {
        HouseRec? r = Houses().Find(o => o.Name == name);
        if (r == null)
            return "no house named " + name;
        Cfg.Set(Server + "house.vendor", r.Vendor.ToString("X"));
        HouseVendorStatus = "House vendor " + r.Vendor.ToString("X") + ", " + name + "'s.";
        return null;
    }
    public string? HouseForget(string name)
    {
        LoadHouses();
        int n;
        lock (m_houses)
            n = m_houses.RemoveAll(o => o.Name == name);
        if (n == 0)
            return "no house named " + name;
        SaveHouses();
        HouseStatus = name + " forgotten.";
        return null;
    }
    // The vendor for a registration, by its target cursor; the name is kept until the target comes back.
    const uint HouseRegisterCursor = 0x48524547;  // "HREG"
    volatile string m_houseRegisterName = "";
    public string? HouseRegisterTarget(string name)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        m_houseRegisterName = name ?? "";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, HouseRegisterCursor);
        string? why = k.ToClient(p, "house register cursor");
        HouseStatus = why == null ? "Target this house's vendor." : "No cursor: " + why;
        return why;
    }
    bool HoldHouseRegister(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != HouseRegisterCursor)
            return false;
        uint s = U32(p, 7);
        bool mobile;
        lock (k.State.Gate)
            mobile = s != 0 && p[1] == 0 && k.State.Mobiles.ContainsKey(s);
        if (!mobile)
        {
            HouseStatus = "Not registered: target the vendor itself.";
            return true;
        }
        string name = m_houseRegisterName;
        HouseStatus = "Registering " + (name.Length > 0 ? name : "the house by its sign") + "...";
        Spawn("the house registration",
            () => { string? w = HouseRegister(name, s); if (w
            != null) { HouseStatus = "Not registered: " + w; Note("Houses: " + HouseStatus); } });
        return true;
    }
    // When the vendor or the ingot stash will take no more (Damian, 2026-10-20: "a fallback storage for the vendor and
    // bank box ... the house floor behind the east wall edge hidden as best we can from other players"): the pack's
    // ingots put on one tile inside the house against its east wall, out of sight from the doorstep where one is, the
    // farthest from the door; onto the pile already there. put: how many. The stash and the tile to stand on beside it,
    // both house floor (at the door's height): the stash's east neighbour is not walkable, and the standing tile is its
    // west neighbour, so the drop is made from inside onto a tile the walk never has to reach from outside (2026-10-20
    // 20:43:51: a stash in the wall's own column, 2483,68, was reached from the grass at 2484,69, through the door and
    // round the house).
    int[]? HomeStashTile(GameLink k, Home h)
    {
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        int[]? best = null;
        int bestScore = int.MinValue;
        Func<int, int, bool> floor = (x, y) => InHome(h, x, y) && !(x == h.DoorX && y == h.DoorY)
            && Math.Abs(Open(k, x, y, h.Floor, ref seen) - h.Floor) <= 1;
        for (int x = h.X0; x <= h.X1; x++)
        {
            for (int y = h.Y0; y <= h.Y1; y++)
            {
                if (!floor(x, y) || floor(x + 1, y) || !floor(x - 1, y))
                    continue;
                int score = (Sight(k, h.StepX, h.StepY, h.Floor, x, y) ? 0 : 1000) + Tiles(x, y, h.DoorX, h.DoorY);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new[] { x, y, x - 1, y };
                }
            }
        }

        return best;
    }
    // The stash picked by a target cursor inside the house (Damian, 2026-10-20: "set the place to put the ingots via a
    // target cursor inside the house. target either existing pile or container. if existing pile > 60k, new pile in
    // same container if possible"): an ingot pile is stacked onto up to BankStackMax, past that a new pile beside it
    // (in its container, else on its tile); a container takes them onto its largest pile with room, else as a new pile.
    // A floor tile targeted is kept as "x,y,z" and the ingots piled on it (Damian, 2026-10-20: "the click and target is
    // the right interface, and it should include targeting floor or ingot or bag").
    const uint StashCursor = 0x53545348;  // "STSH"
    public volatile string StashStatus = "";
    uint StashSerial
    {
        get
        {
            uint v;
            string s = Cfg.Get(Server + "house.stash", "");
            return s.IndexOf(',') < 0 && uint.TryParse(s, NumberStyles.HexNumber, null, out v) ? v : 0;
        }
    }
    int[]? StashTile
    {
        get
        {
            string[] a = Cfg.Get(Server + "house.stash", "").Split(',');
            int x, y, z;
            return a.Length == 3 && int.TryParse(a[0], out x) && int.TryParse(a[1], out y) && int.TryParse(a[2], out z)
                ? new[] { x, y, z } : null;
        }
    }
    // A stash in the bank box (house.stash.where "bank") is reached by the bank rune; uo1998 opens the one bank box at
    // any bank (BankTake and BankPut use the nearest).
    bool StashInBank => StashSerial != 0 && Cfg.Get(Server + "house.stash.where", "") == "bank";
    public bool StashSet => StashSerial != 0 || StashTile != null;
    public string StashText()
    {
        uint s = StashSerial;
        int[]? t = StashTile;
        return t != null ? "the house floor at " + t[0] + "," + t[1]
            : s != 0 ? s.ToString("X") + (StashInBank ? " in the bank box" : " in the house")
            : "the house floor by the east wall";
    }
    public string? StashTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 1;
        Put32(p, 2, StashCursor);
        string? why = k.ToClient(p, "stash cursor");
        StashStatus = why == null
            ? ("Target the floor, an ingot pile or a bag inside the house, or an ingot pile or a container in your "
            + "open bank box.") : "No cursor: " + why;
        return why;
    }
    public void StashClear()
    {
        Cfg.Set(Server + "house.stash", "");
        Cfg.Set(Server + "house.stash.where", "");
        StashStatus = "Stash cleared: the house floor by the east wall.";
    }
    bool HoldStash(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 19 || U32(p, 2) != StashCursor)
            return false;
        uint s = U32(p, 7);
        Home? h = HomeNow(k);
        if (p[1] == 1 && s == 0)
        {
            int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
            if (h == null || !InHome(h, x, y))
            {
                StashStatus = "Stash not set: " + x + "," + y + " is not inside the house.";
                return true;
            }
            Cfg.Set(Server + "house.stash", x + "," + y + "," + z);
            Cfg.Set(Server + "house.stash.where", "");
            StashStatus = "Stash: the house floor at " + x + "," + y + ".";
            Note("Mine: " + StashStatus);
            return true;
        }
        AssistItem? it = s == 0 ? null : StashItem(k, s);
        if (it == null || (it.Graphic != IngotArt && !IsContainer(it.Graphic)))
        {
            StashStatus = "Stash not set: target the floor, an ingot pile or a bag.";
            return true;
        }
        bool bank;
        List<string> boxes = StashChain(k, it, out bank);
        int[]? at = bank ? null : StashGround(k, it);
        if (!bank && (h == null || at == null || !InHome(h, at[0], at[1])))
        {
            StashStatus = "Stash not set: it must be inside the house or in your bank box.";
            return true;
        }
        Cfg.Set(Server + "house.stash", s.ToString("X"));
        Cfg.Set(Server + "house.stash.boxes", string.Join(";", boxes));
        Cfg.Set(Server + "house.stash.where", bank ? "bank" : "");
        StashStatus = "Stash: " + UoTiles.Name(it.Graphic) + " " + s.ToString("X")
            + (bank ? " in the bank box" : " at " + at![0] + "," + at[1])
            + (boxes.Count > 0 ? " inside " + string.Join(" > ", boxes) : "") + ".";
        Note("Mine: " + StashStatus);
        return true;
    }
    // The containers holding an item, outermost first, up to one on the ground or the bank box (not listed); bank:
    // whether the bank box holds it, or it is the bank box.
    List<string> StashChain(GameLink k, AssistItem it, out bool bank)
    {
        List<string> boxes = new();
        bank = false;
        lock (k.State.Gate)
        {
            if (it.Layer == BankLayer && it.Mobile == k.State.Player)
            {
                bank = true;
                return boxes;
            }
            uint c = it.Container;
            for (int i = 0; i < 16 && c != 0; i++)
            {
                AssistItem? b;
                bool known = k.State.Items.TryGet(c, out b);
                if (known && b!.Layer == BankLayer && b.Mobile == k.State.Player)
                {
                    bank = true;
                    break;
                }
                boxes.Insert(0, c.ToString("X"));
                if (k.State.Ground.ContainsKey(c) || !known)
                    break;
                c = b!.Container;
            }
        }
        return boxes;
    }
    // A stash inside a container is unknown until the container is opened, as after a relay restart (2026-10-21
    // 00:00:28, "the ingot stash 467AF27C is not in view", the pile in container 44F157AD): the containers recorded at
    // its target, outermost first (house.stash.boxes), walked to and opened.
    string? StashOpen(GameLink k, Home h)
    {
        List<uint> chain = new();
        foreach (string t in Cfg.Get(Server + "house.stash.boxes", "").Split(';'))
        {
            uint v;
            if (uint.TryParse(t, NumberStyles.HexNumber, null, out v))
                chain.Add(v);
        }
        if (chain.Count == 0)
            return "no container is recorded for it: target the stash again";
        AssistItem? outer = StashItem(k, chain[0]);
        if (outer == null)
            return "its container " + chain[0].ToString("X") + " is not in view";
        int[]? at = StashGround(k, outer);
        if (at == null || !InHome(h, at[0], at[1]))
            return "its container " + chain[0].ToString("X") + " is not inside the house";
        CircuitStatus = "Mine: to the ingot stash's container";
        string? w = WalkTo(at[0], at[1], 1, false, h.Floor);
        if (w != null)
            return "to its container: " + w;
        foreach (uint b in chain)
        {
            BusyWait(clickedAt);
            string? pw = Peek(b);
            if (pw != null)
                return "opening " + b.ToString("X") + ": " + pw;
        }
        return null;
    }
    // A stash in the bank box unknown until its containers are opened, the bank box open where the player stands.
    string? StashOpenInBank()
    {
        foreach (string t in Cfg.Get(Server + "house.stash.boxes", "").Split(';'))
        {
            uint b;
            if (!uint.TryParse(t, NumberStyles.HexNumber, null, out b))
                continue;
            BusyWait(clickedAt);
            string? pw = Peek(b);
            if (pw != null)
                return "opening " + b.ToString("X") + " in the bank box: " + pw;
        }
        return null;
    }
    // The house stash's largest ingot pile: the targeted pile, else the largest in the targeted container, else the
    // largest on the targeted floor tile; at: where the stash lies, null when it is unset or not in view.
    AssistItem? StashPile(GameLink k, out int[]? at)
    {
        at = StashTile;
        AssistItem? best = null;
        uint s = StashSerial;
        if (s != 0)
        {
            AssistItem? it = StashItem(k, s);
            at = it == null ? null : StashGround(k, it);
            if (it == null)
                return null;
            if (it.Graphic == IngotArt)
                return it;
            lock (k.State.Gate)
                foreach (AssistItem x in k.State.Items.Values)
                    if (x.Container == s && x.Graphic == IngotArt && (best == null || x.Amount > best.Amount))
                        best = x;
        }
        else if (at != null)
        {
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (g.Graphic == IngotArt && g.X == at[0] && g.Y == at[1]
                        && (best == null || g.Amount > best.Amount))
                        best = g;
        }

        return best;
    }
    // The stash by serial in a container or on the ground (Item reads containers only).
    static AssistItem? StashItem(GameLink k, uint s)
    {
        lock (k.State.Gate)
        {
            AssistItem? it;
            return k.State.Items.TryGet(s, out it) || k.State.Ground.TryGet(s, out it) ? it : null;
        }
    }
    // Where an item lies on the ground, itself or its outermost container: x, y, z; null when not on the ground.
    int[]? StashGround(GameLink k, AssistItem? it)
    {
        lock (k.State.Gate)
        {
            for (int i = 0; i < 16 && it != null; i++)
            {
                AssistItem? g;
                if (k.State.Ground.TryGet(it.Serial, out g))
                    return new[] { g.X, g.Y, g.Z };
                uint c = it.Container;
                if (c == 0)
                    break;
                if (k.State.Ground.TryGet(c, out g))
                    return new[] { g.X, g.Y, g.Z };
                if (!k.State.Items.TryGet(c, out it))
                    break;
            }
        }
        return null;
    }
    // In the house (h), walked to; in the bank box (h null), with the box open where the player stands.
    string? MineStoreStash(GameLink k, Home h, uint stash, out int put)
    {
        put = 0;
        AssistItem? it = StashItem(k, stash);
        if (it == null)
        {
            string? ow = h != null ? StashOpen(k, h) : StashOpenInBank();
            if (ow != null)
                return "the ingot stash " + stash.ToString("X") + " is not in view: " + ow;
            it = StashItem(k, stash);
        }
        if (it == null)
            return "the ingot stash " + stash.ToString("X") + " is not in view, its containers opened";
        int[]? at = null;
        string? w;
        if (h != null)
        {
            at = StashGround(k, it);
            if (at == null || !InHome(h, at[0], at[1]))
                return "the ingot stash " + stash.ToString("X") + " is not inside the house";
            CircuitStatus = "Mine: to the ingot stash";
            w = WalkTo(at[0], at[1], 1, false, h.Floor);
            if (w != null)
                return "to the ingot stash: " + w;
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            if (!InHome(h, px, py))
                return "the walk to the ingot stash ended outside the house, at " + px + "," + py;
        }
        for (int round = 0; round < 32 && !circuitStop && !k.Closed; round++)
        {
            AssistItem? st = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem x in k.State.Items.Values)
                    if (inside.Contains(x.Container) && x.Graphic == IngotArt && (st == null || x.Amount > st.Amount))
                        st = x;
            }
            int n = Math.Min(Math.Max(1, st == null ? 0 : st.Amount), PackCount(k, g => g == IngotArt));
            if (st == null || n <= 0)
                break;
            // Onto: the targeted pile while it has room, else the targeted container's largest pile with room; a new
            // pile in the container (the targeted one, or the pile's), else on the pile's tile.
            AssistItem? cur = StashItem(k, stash);
            if (cur == null)
                return "the ingot stash is gone from view";
            uint box = IsContainer(cur.Graphic) ? cur.Serial : cur.Container;
            AssistItem? onto = null;
            if (cur.Graphic == IngotArt && Math.Max(1, cur.Amount) + n <= BankStackMax)
                onto = cur;
            else if (box != 0)
                lock (k.State.Gate)
                    foreach (AssistItem x in k.State.Items.Values)
                        if (x.Container == box && x.Graphic == IngotArt && Math.Max(1, x.Amount) + n <= BankStackMax
                            && (onto == null || x.Amount > onto.Amount))
                            onto = x;
            bool ground;
            lock (k.State.Gate)
                ground = box == 0 || k.State.Ground.ContainsKey(cur.Serial) && cur.Graphic == IngotArt;
            if (ground && at == null)
                return "the ingot stash " + stash.ToString("X") + " lies on no tile the circuit walked to";
            int before = PackCount(k, g => g == IngotArt);
            uint target = onto != null ? onto.Serial : box;
            using (Act(k))
                w = MoveStack(k, st.Serial, n, "the ingots", "Putting ingots in the stash",
                    () => (onto != null ? k.Drop(st.Serial, onto.Serial)
                    : !ground && box != 0 ? k.Drop(st.Serial, box) : k.DropGround(st.Serial, at![0], at[1], at[2]))!);
            if (w != null)
                return w;
            if (PackCount(k, g => g == IngotArt) >= before)
                return StashRefused;
            put += n;
        }
        Note("Mine: " + put + " ingots put in the stash " + stash.ToString("X"));
        return null;
    }
    string? MineStoreHomeFloor(GameLink k, out int put)
    {
        put = 0;
        Home? h = HomeNow(k);
        if (h == null)
            return "no house known to keep ingots in";
        if (PackCount(k, g => g == IngotArt) <= 0)
            return null;
        // A stash that refused once is full for the rest of the run, as the vendor is (its container's weight limit,
        // "That container cannot hold more weight.", 2026-10-21 02:20:23): the house floor by the east wall takes the
        // rest.
        if (StashSerial != 0 && !StashInBank && !m_mineStashFull)
        {
            string? sw = MineStoreStash(k, h, StashSerial, out put);
            if (sw != StashRefused)
                return sw;
            m_mineStashFull = true;
            Note("Mine: the ingot stash took no more; the house floor by the east wall for the rest of this run");
        }
        int[]? picked = StashTile;
        if (picked != null && !InHome(h, picked[0], picked[1]))
            return "the stash tile " + picked[0] + "," + picked[1] + " is not inside the house";
        int[]? t = picked ?? HomeStashTile(k, h);
        if (t == null)
            return "no house floor against the east wall with floor beside it";
        int dropZ = picked != null ? picked[2] : h.Floor;
        CircuitStatus = "Mine: to the house floor";
        string? w = picked != null ? WalkTo(t[0], t[1], 1, false, h.Floor) : WalkTo(t[2], t[3], 0, false, h.Floor);
        if (w != null)
            return "to the house floor: " + w;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (!InHome(h, px, py))
            return "the walk to the house floor ended outside the house, at " + px + "," + py;
        for (int round = 0; round < 32 && !circuitStop && !k.Closed; round++)
        {
            AssistItem? st = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && it.Graphic == IngotArt
                        && (st == null || it.Amount > st.Amount))
                        st = it;
            }
            int n = Math.Min(Math.Max(1, st == null ? 0 : st.Amount), PackCount(k, g => g == IngotArt));
            if (st == null || n <= 0)
                break;
            AssistItem? pile = null;
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (g.Graphic == IngotArt && g.X == t[0] && g.Y == t[1])
                        pile = g;
            int before = PackCount(k, g => g == IngotArt);
            using (Act(k))
                w = MoveStack(k, st.Serial, n, "the ingots", "Keeping ingots on the house floor",
                    () => (pile != null ? k.Drop(st.Serial, pile.Serial)
                    : k.DropGround(st.Serial, t[0], t[1], dropZ))!);
            if (w != null)
                return w;
            // A drop that came back to the pack is not made again (L-FUTILITY: eight in a row at 20:43:51, then the
            // guard).
            if (PackCount(k, g => g == IngotArt) >= before)
                return "the house floor at " + t[0] + "," + t[1] + " did not take the ingots; they stay in the pack";
            put += n;
        }
        Note("Mine: " + put + " ingots kept on the house floor at " + t[0] + "," + t[1]);
        return null;
    }
    // The player's house vendor, set by its target (Bags, "House vendor"); 0 unset.
    uint HouseVendorSerial
    {
        get
        {
            uint v;
            return uint.TryParse(Cfg.Get(Server + "house.vendor", ""), NumberStyles.HexNumber, null, out v) ? v : 0;
        }
    }
    static readonly ImmutableArray<string> s_bankBagSlots = [ "bankstore", "cityrunes", "shoprunes" ];
    public static ImmutableArray<string> BankBagSlots => s_bankBagSlots;
    static readonly ImmutableArray<uint> s_bankBagCursors = [ 0x4253544F, 0x52435459,
        0x52534850 ];  // "BSTO" "RCTY" "RSHP"
    static string BankBagKey(string slot)
    {
        return slot == "bankstore" ? "bank.store" : slot == "cityrunes" ? "runes.city" : "runes.shop";
    }
    public List<uint> BankBags(string slot)
    {
        List<uint> l = new();
        foreach (string s in Cfg.Get(Server + BankBagKey(slot), "").Split(new[] { ';', ',' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            uint v;
            if (uint.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v))
                l.Add(v);
        }
        return l;
    }
    public volatile string BankBagStatus = "";
    public string? BankBagTarget(string slot)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        int i = s_bankBagSlots.IndexOf(slot);
        if (i < 0)
            return "no such bag";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, s_bankBagCursors[i]);
        string? why = k.ToClient(p, "bank bag cursor");
        BankBagStatus = why == null ? "Target the bag in your open bank box." : "No cursor: " + why;
        return why;
    }
    bool HoldBankBag(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11)
            return false;
        int i = s_bankBagCursors.IndexOf(U32(p, 2));
        if (i < 0)
            return false;
        string slot = s_bankBagSlots[i];
        uint s = U32(p, 7);
        AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
        bool inPack = false;
        if (it != null)
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(s);
        // The rune bags travel in the pack (the circuits carry the shop runes bag, CarryShopRunes), so one there is set
        // as one in the bank box is (Damian, 2026-10-24: "shop runes in bags wont set": 43DD0F1E in a backpack in the
        // pack); the bank store is the bank's alone.
        bool packOk = slot != "bankstore";
        if (it == null || !IsContainer(it.Graphic) || it.Graphic == SpellbookArt || inPack && !packOk)
        {
            BankBagStatus = packOk ? "Not set: pick a bag in the open bank box or your pack."
                : "Not set: pick a bag in the open bank box, not one in the pack.";
            return true;
        }
        List<uint> l = BankBags(slot);
        if (slot == "shoprunes")
        {
            if (l.Contains(s))
                l.Remove(s);
            else
                l.Add(s);
        }
        else
        {
            l.Clear();
            l.Add(s);
        }
        Cfg.Set(Server + BankBagKey(slot), string.Join(";", l.ConvertAll(v => v.ToString("X"))));
        BankBagStatus = slot + ": " + (l.Count == 0 ? "none" : string.Join(", ", l.ConvertAll(v => v.ToString("X"))));
        Note("Bank bags: " + BankBagStatus);
        return true;
    }
    // The pack's rune for a place, by where it lands (RuneChoice; AssistIdentity.md stage 1): one landing in the place,
    // else one never yet recalled by whose label names it (the label equal before the label containing it); 0 for none.
    // The rune targeted for the place (pinned) is taken first, then the serial last found for the label, each while it
    // is in the pack and its landing is in the place, or unknown.
    ValueMap<string, uint> m_packRunes = ValueMap<string, uint>.Empty; readonly Lock m_packRunesGate = new();
    public uint PackRuneFor(GameLink k, string label, Func<int, int, bool> place, uint pinned = 0)
    {
        string want = PlaceKey(label);
        LoadRuneLandings();
        if (pinned != 0)
        {
            bool inPack;
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(pinned);
            int[]? land;
            if (!m_runeLandings.TryGet(pinned, out land))
                land = null;
            if (inPack && (land == null || place(land[0], land[1])))
                return pinned;
        }
        uint s = 0;
        if (want.Length > 0)
            m_packRunes.TryGet(want, out s);
        if (s != 0)
        {
            bool inPack;
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(s);
            int[]? land;
            if (!m_runeLandings.TryGet(s, out land))
                land = null;
            if (inPack && !Reserved(s) && (land == null || place(land[0], land[1])))
                return s;
        }
        ImmutableArray<RuneSeen>.Builder seen = ImmutableArray.CreateBuilder<RuneSeen>();
        foreach (HeldRune r in PackRunes(k))
        {
            string key = PlaceKey(r.Place!);
            seen.Add(new RuneSeen(r.Serial, r.Landing,
                want.Length == 0 || key.Length == 0 ? 0 : key == want ? 2 : key.Contains(want) ? 1 : 0));
        }
        s = RuneChoice.Pick(seen.ToImmutable(), place);
        if (s != 0 && want.Length > 0)
            lock (m_packRunesGate)
                m_packRunes = m_packRunes.Set(want, s);
        return s;
    }
    // A tile in a shop, for a shop rune's landing: the smallest described region there is not a whole area (NONE_), a
    // bank, housing or the plains, where the Magincia, Nujel'm and Moonglow runes land (NONE_MAGINCIA "Isle of
    // Magincia", NONE_NUJELM, NONE_VERITY) and a Britain rune (BANK_BRITAIN_1) (regions-1998.txt, the 2026-10-22 rune
    // landings).
    static readonly ImmutableArray<string> s_notShops = [ "none", "bank", "housing", "plains" ];
    static bool InShop(int x, int y)
    {
        string? kind = UoRegions.ShopKind(x, y);
        return kind != null && s_notShops.IndexOf(kind) < 0;
    }
    // A town's rune: landing in its region.
    uint PackTownRune(GameLink k, string town)
    {
        return PackRuneFor(k, town, (x, y) => UoRegions.City(x, y) == town);
    }
    // The mining bank rune (mine.bank-rune): landing within BankRuneTiles of a bank of the town it lands in.
    uint PackBankRune(GameLink k)
    {
        return PackRuneFor(k, MineBankRune,
            (x, y) => { string? c = UoRegions.City(x, y); return c != null
            && CityBanks(c, x, y).Exists(b => Tiles(b[0], b[1], x, y) <= BankRuneTiles); }, MineBankRuneSerial);
    }
    // How far outside a cave's radius its rune may land: the rune back can be marked outside the cave (CaveBack).
    public const int CaveRuneSlack = 16;
    // Back to the cave by its rune: the pack's, else the bank's (the box here open); then walked into the cave, as the
    // rune can be marked outside it (Damian, 2026-10-18: "the rune back may not be in the cave exactly, so will require
    // a walk to continue"). Null, or why not.
    // Within the farthest a cave's rune may land (its radius and CaveRuneSlack), the cave is walked to, not recalled to
    // (Damian, 2026-10-25: "the cave was closer than the recall spot"; Kwik at 2476,81, 38 tiles from Home2's mark,
    // recalled to 2456,81 and walked on from there).
    static bool CaveWalkable(MineCave c, int px, int py)
    {
        return Tiles(px, py, c.X, c.Y) <= c.Radius + CaveRuneSlack;
    }
    string? CaveBack(GameLink k, MineCave c)
    {
        if (c == null)
            return "no rune back to the cave";
        int sx, sy;
        lock (k.State.Gate)
        {
            sx = k.State.X;
            sy = k.State.Y;
        }
        if (CaveWalkable(c, sx, sy))
        {
            CircuitStatus = "Mine: walking into the cave " + c.Name;
            string? ww = WalkTo(c.X, c.Y, MineReach);
            if (ww == null)
                return null;
            if (c.Rune.Length == 0)
                return "walking into the cave from " + sx + "," + sy + ": " + ww;
            Note("Mine: walking into the cave " + c.Name + " from " + sx + "," + sy + ": " + ww + "; recalling");
        }
        if (c.Rune.Length == 0)
            return "no rune back to the cave";
        string place = RunePlace(c.Rune);
        uint r = PackRuneFor(k, place, (x, y) => Tiles(x, y, c.X, c.Y) <= c.Radius + CaveRuneSlack, c.RuneSerial);
        CircuitStatus = "Mine: recalling to the cave " + c.Name;
        string? w;
        if (r != 0)
        {
            w = Recall(r);
            if (w != null)
                return "recalling by the pack rune " + place + ": " + w;
        }
        else if (CityOf(k) == null)
        {
            return "no rune for \"" + place + "\" in the pack, and no bank here to recall from";
        }
        else
        {
            w = BankRecall(k, place, false);
            if (w != null)
                return w;
        }
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (Tiles(px, py, c.X, c.Y) <= MineReach)
            return null;
        CircuitStatus = "Mine: walking into the cave " + c.Name;
        w = WalkTo(c.X, c.Y, MineReach);
        return w == null ? null : "walking into the cave from where the rune landed (" + px + "," + py + "): " + w;
    }
    // This run's ingots: smelted from its ore, and put in the bank or on the floor.
    int m_mineIngotsMade, m_mineIngotsStored;
    // Ore piles too small to smelt, never smelted again.
    Set<uint> m_smeltSkip = Set<uint>.Empty;
    // The cave's rune by a target cursor on the rune (in the pack or the open bank box), its label read and kept.
    public const uint CaveRuneCursor = 0x43415645;  // "CAVE"
    public string? CaveRuneTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (PickedCave!.Length == 0)
            return "pick or mark a cave first";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, CaveRuneCursor);
        string? why = k.ToClient(p, "cave rune cursor");
        ToolbagStatus = why == null ? "Target the rune back to the cave " + PickedCave + "." : "No cursor: " + why;
        return why;
    }
    bool HoldCaveRune(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != CaveRuneCursor)
            return false;
        uint s = U32(p, 7);
        AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
        string? cave = PickedCave;
        if (it == null || !IsRuneArt(it.Graphic))
        {
            ToolbagStatus = "Cave rune not set: pick a rune.";
            return true;
        }
        Spawn("the cave rune's label", () =>
        {
            List<string> l = LabelsOf(k, new List<AssistItem> { it });
            string line = l.Count > 0 ? l[0] : "";
            int sp = line.IndexOf(' '), at = line.LastIndexOf(" at ");
            string label = sp > 0 && at > sp ? line.Substring(sp + 1, at - sp - 1).Trim() : "";
            if (label.Length == 0 || label == "no label")
            {
                ToolbagStatus = "Cave rune not set: the rune " + s.ToString("X") + " answered no label.";
                return;
            }
            ToolbagStatus = SetCaveRune(cave!, label, s).Replace("OK ", "");
        }, true);
        return true;
    }
    // Back to the spot the trip left: walked when near, else by the cave's rune.
    string? MineBack(GameLink k, MineCave c, int x0, int y0)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (Tiles(px, py, x0, y0) > CircuitReach)
        {
            string? cw = CaveBack(k, c);
            if (cw != null)
                return "back to the cave: " + cw;
        }
        if (m_mineInside && !InCave(x0, y0))
            return null;
        CircuitStatus = "Mine: walking back to the dig";
        string? w = WalkTo(x0, y0, 0);
        return w == null ? null : "back to the dig: " + w;
    }
    // Every ore pile in the pack smelted at the forge, the player within 2 tiles of it.
    string? MineSmelt(GameLink k, string[] forge)
    {
        int fx = int.Parse(forge[2]), fy = int.Parse(forge[3]), fz = int.Parse(forge[4]), fg = int.Parse(forge[5]);
        bool stat = forge[0] == "1";
        uint fs = (uint)int.Parse(forge[1]);
        // On the forge's own floor, in sight of it: a house's forge stands on a floor above the ground, and a tile
        // within 2 on the grass beneath it was arrived at (2026-10-18 18:59, 2479,73 z 0 under the forge at 2480,71 z
        // 7).
        CircuitStatus = "Mine: walking to the forge";
        string? w = WalkTo(fx, fy, 2, true, fz);
        if (w != null)
            return "walking to the forge: " + w;
        LoopKind? smelt = Find("Smelting (one ore at a time)");
        for (int round = 0; round < 200 && !circuitStop && !k.Closed; round++)
        {
            AssistItem? ore = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && IsOre(it.Graphic) && !m_smeltSkip.Contains(it.Serial))
                    {
                        ore = it;
                        break;
                    }
            }
            if (ore == null)
                return null;
            Wait.Till(Clock, m_lastUse.AddMilliseconds(Delay(smelt!)), () => circuitStop);
            BusyWait(clickedAt);
            Heard mark;
            int ingots = SmeltIngots(k);
            using (Act(k))
            {
                mark = new(k.State);
                CircuitStatus = "Mine: smelting";
                w = k.Use(ore.Serial, true);
                if (w != null)
                    return "using the ore: " + w;
                clickedAt = m_lastUse = Clock.UtcNow;
                DateTime until = Clock.UtcNow.AddMilliseconds(MineCursorMs);
                AwaitCursor(k, until);
                if (!k.MacroCursor)
                {
                    k.Settle();
                    string? said = mark.Last();
                    if (said != null
                        && s_waitLines.Exists(wl => said.StartsWith(wl, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    return "the ore opened no forge cursor"
                        + (said != null ? "; the server said \"" + said + "\"" : "");
                }
                w = k.Target(stat, stat ? 0 : fs, fx, fy, fz, fg);
                if (w != null)
                    return "aiming at the forge: " + w;
            }
            string res = SmeltResult(k, smelt!, mark, ore.Serial, ingots);
            m_mineIngotsMade += Math.Max(0, SmeltIngots(k) - ingots);
            // A pile too small to smelt is dropped at the feet, as scrap cloth is (Damian, 2026-10-18: "we got a
            // useless ore, and we should just drop it like scrap cloth"; "There is not enough metal-bearing ore in this
            // pile to make an ingot.", 19:56:27).
            if (res != null && res.IndexOf("not enough metal-bearing ore", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // The scraps' drop: the tiles around, each checked for the pile leaving the pack. A drop on the
                // player's own tile came back to the pack each time and the pile was smelted again (19:59:36 to
                // 19:59:51); a pile still in the pack is never smelted again (L-FUTILITY).
                m_smeltSkip = m_smeltSkip.Add(ore.Serial);
                DropScraps(k, ore.Serial);
                AssistItem? left = Item(k, ore.Serial);
                bool kept;
                lock (k.State.Gate)
                    kept = left != null && PackTree(k).Contains(left.Container);
                Note("Mine: " + ore.Serial.ToString("X") + ", too little ore to smelt, "
                    + (kept ? "would not drop; it stays in the pack" : "dropped"));
                continue;
            }
            if (res != "smelted" && res != "failed" && res != "again")
                return res;
        }
        return null;
    }
    // Tools of a profession tinkered from the pack's ingots with tinker's tools (the menus picked by their text) until
    // the pack holds want; a failed tinker is tried again, any other answer ends it. The tinker's tools come from their
    // toolbox when it is within reach, else are bought in town. A failed tinker is the answer asking to try again,
    // TinkerTries times at most (Damian, 2026-10-18: "hit a bad run of luck and the bot gave up ... need a little more
    // try again on a difficult challenge").
    public const int TinkerTries = 30;
    string? TinkerTool(GameLink k, string[] picks, Profession prof, int want, int ingotsMin, string what)
    {
        Func<int, bool> made = prof.IsTool;
        if (PackCount(k, IsTinkerTool) == 0)
        {
            string? bw = ToolsFromBox(k, s_tinkerTools, 1);
            if (bw != null && CityOf(k) != null)
            {
                Note("Tools: " + bw);
                bw = BuyTinkerTools(k);
                if (bw != null)
                    Note("Tools: " + bw);
            }
        }
        for (int n = 0; n < TinkerTries && !circuitStop && PackCount(k, made) < want; n++)
        {
            AssistItem? tools = null, ingots = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container))
                    {
                        if (IsTinkerTool(it.Graphic))
                            tools = it;
                        if (it.Graphic == IngotArt && (ingots == null || it.Amount > ingots.Amount))
                            ingots = it;
                    }
            }
            if (tools == null)
                return "no tinker's tools in the pack";
            if (ingots == null || Math.Max(1, ingots.Amount) < ingotsMin)
                return "fewer than " + ingotsMin + " ingots in the pack";
            int before = PackCount(k, made);
            BusyWait(clickedAt);
            Heard mark;
            // uo1998 checks the skill before any menu: "Tinkering failed." comes in the menu's place (2026-10-18
            // 20:56:34), a failure the answer itself asks to try again.
            bool failedEarly = false;
            using (Act(k))
            {
                // A menu that lists the item itself where the pick names its category is answered with the item
                // (uo1998's tinker menu is flat, its tools by picture).
                bool leaf = false;
                Func<int, byte[], byte[]> pickOf = (i, m) => { byte[]? r = MenuPick(m, picks[i]); if (r == null
                    && i < picks.Length
                    - 1) { r = MenuPick(m, picks[picks.Length - 1]); leaf = r != null; } return r!; };
                mark = new(k.State);
                CircuitStatus = "Tinkering " + what;
                string? w = Sew(k, tools.Serial, ingots.Serial, picks.Length, pickOf, () => leaf);
                clickedAt = Clock.UtcNow;
                if (w != null)
                {
                    failedEarly = mark.Has("fail");
                    if (!failedEarly)
                        return w;
                }
            }
            if (failedEarly)
            {
                Note("Tools: tinkering " + what + " failed; again");
                continue;
            }
            Wait.Until(Clock, () => PackCount(k, made) != before, TailorMs, "the sewing", stopped: () => circuitStop,
                stepMs: 100);
            if (PackCount(k, made) == before)
            {
                string? said = mark.Last();
                if (said == null || said.IndexOf("fail", StringComparison.OrdinalIgnoreCase) < 0)
                    return "no " + what.Substring(what.IndexOf(' ') + 1) + " made"
                        + (said != null ? ": \"" + said + "\"" : "");
            }
        }
        ToolsToBag(k, prof);
        return null;
    }
}
