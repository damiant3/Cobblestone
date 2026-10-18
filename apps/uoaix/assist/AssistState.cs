// What the server has told this client, kept from whole packets in both directions (assist-plan.md stage 2).
// Field offsets are the 1.25.32 layouts the UOAIX shard writes (GamePackets.codex gp-status, gp-speech;
// GameSession.codex gs-skills) and the soak bots read (proofs/soak-bots.ps1 Handle).
public sealed class AssistItem {
    public uint Serial, Container, Mobile; public int Graphic, Amount = 1, X, Y, Z, Hue, Layer = -1;
}

public sealed class AssistState {
    public readonly object Gate = new object();
    public uint Player, Backpack; public int X, Y, Z, Facing;
    public string Name = "", Title = ""; public int Hits, MaxHits, Str, Dex, Int, Stam, MaxStam, Mana, MaxMana, Gold, Armor, Weight;
    public readonly System.Collections.Generic.Dictionary<int, int> Skills = new System.Collections.Generic.Dictionary<int, int>();
    public readonly System.Collections.Generic.Dictionary<int, int> SkillStart = new System.Collections.Generic.Dictionary<int, int>();
    // Carried, contained and worn items; ground items apart, dropped beyond GroundReach of the player.
    public readonly System.Collections.Generic.Dictionary<uint, AssistItem> Items = new System.Collections.Generic.Dictionary<uint, AssistItem>();
    public readonly System.Collections.Generic.Dictionary<uint, AssistItem> Ground = new System.Collections.Generic.Dictionary<uint, AssistItem>();
    public const int GroundReach = 24, JournalKeep = 200;
    public readonly System.Collections.Generic.List<string> Journal = new System.Collections.Generic.List<string>();
    public bool CursorOpen; public uint Cursor; public int CursorType;
    public int SkillEntry;  // 4 (id, value) or 6 (id, value, base) bytes a skill in the 0x3A full list
    public string Fault;    // the first packet this state could not read
    readonly int[] walking = new int[256];

    static int U16(byte[] p, int at) { return (p[at] << 8) | p[at + 1]; }
    static uint U32(byte[] p, int at) { return ((uint)p[at] << 24) | ((uint)p[at + 1] << 16) | ((uint)p[at + 2] << 8) | p[at + 3]; }
    static string Ascii(byte[] p, int at, int max) {
        var sb = new System.Text.StringBuilder();
        for (int i = at; i < at + max && i < p.Length && p[i] != 0; i++) sb.Append(p[i] >= 32 && p[i] < 127 ? (char)p[i] : '?');
        return sb.ToString();
    }
    public long JournalSeq;  // lines ever said; the journal holds the last JournalKeep of them
    public readonly System.Collections.Generic.List<string[]> Chat = new System.Collections.Generic.List<string[]>();  // time, speaker, text
    public long ChatSeq;
    // Session: when the game login confirmed; the player's notoriety (0x77/0x78, 1 innocent to 7 invulnerable, 0 not
    // seen); guarded from the server's own guard-zone lines (OUO player.c), null until the first one.
    public System.DateTime LoginAt; public int Notoriety; public bool? Guarded;
    // Dead from the death packet (0x2C) until the player's body is drawn human again (a ghost is 0x192 or 0x193).
    public bool Dead;
    void PlayerBody(int body) { Dead = body == 0x192 || body == 0x193; }
    // For the session summary: attributes at login, and what the server named or drew each mobile as (bounded: cleared
    // past MobileKeep, as mobiles come and go).
    public int StrStart = -1, DexStart, IntStart;
    public const int MobileKeep = 4096;
    public readonly System.Collections.Generic.Dictionary<uint, string> Names = new System.Collections.Generic.Dictionary<uint, string>();
    public readonly System.Collections.Generic.Dictionary<uint, int> Bodies = new System.Collections.Generic.Dictionary<uint, int>();
    // The mobiles in view and their tiles, from the server's own packets: 0x78 in view, 0x77 moved, 0x1D out of view.
    public readonly System.Collections.Generic.Dictionary<uint, int[]> Mobiles = new System.Collections.Generic.Dictionary<uint, int[]>();
    // Teleporter jumps seen (from x, y, z, to x, y, z), taken by AssistFeatures.LearnJumps.
    public readonly System.Collections.Generic.List<int[]> Jumps = new System.Collections.Generic.List<int[]>();
    public System.DateTime StepAt, CursorAt;
    public const int JumpTiles = 2, JumpStepMs = 1500, JumpCursorMs = 10000;
    void Named(uint s, string name) { if (name.Length == 0) return; if (Names.Count > MobileKeep) Names.Clear(); Names[s] = name; }
    void Body(uint s, int body) { if (Bodies.Count > MobileKeep) Bodies.Clear(); Bodies[s] = body; }
    void Say(string line) { Journal.Add(line); JournalSeq++; if (Journal.Count > JournalKeep) Journal.RemoveRange(0, Journal.Count - JournalKeep); }
    public System.Collections.Generic.List<string> JournalSince(long seq) {
        lock (Gate) { long first = JournalSeq - Journal.Count; int from = seq > JournalSeq ? 0 : (int)System.Math.Min(Journal.Count, System.Math.Max(0, seq - first)); return Journal.GetRange(from, Journal.Count - from); }
    }

    public void Client(byte[] p) {
        lock (Gate) {
            switch (p[0]) {
                case 0x02: walking[p[2]] = p[1] + 1; break;
                case 0x6C: CursorOpen = false; break;
            }
        }
    }

    public void Server(byte[] p) {
        lock (Gate) {
            try { Read(p); } catch (System.Exception e) { if (Fault == null) Fault = "opcode " + p[0].ToString("X2") + " length " + p.Length + ": " + e.Message; }
        }
    }

    void Read(byte[] p) {
        switch (p[0]) {
            case 0x77: if (p.Length >= 7) { Body(U32(p, 1), U16(p, 5)); if (U32(p, 1) == Player) PlayerBody(U16(p, 5)); } if (p.Length >= 17 && U32(p, 1) == Player) Notoriety = p[16]; if (p.Length >= 12 && U32(p, 1) != Player) Mobiles[U32(p, 1)] = new[] { U16(p, 7), U16(p, 9), (sbyte)p[11] }; break;
            case 0x1B: Mobiles.Clear(); LoginAt = System.DateTime.Now; Player = U32(p, 1); PlayerBody(U16(p, 9)); X = U16(p, 11); Y = U16(p, 13); Z = (sbyte)p[16]; Facing = p[17] & 7; Prune(); break;
            case 0x2C: Dead = true; break;
            case 0x20: if (U32(p, 1) == Player) {
                    int nx = U16(p, 11), ny = U16(p, 13), nz = (sbyte)p[18];
                    // A redraw far off just after a step, with no spell cursor (Recall, Teleport) in JumpCursorMs, is a
                    // teleporter the server keeps from the client: the tile stepped onto sends the player to the landing.
                    var now = System.DateTime.UtcNow;
                    if (System.Math.Max(System.Math.Abs(nx - X), System.Math.Abs(ny - Y)) > JumpTiles && (now - StepAt).TotalMilliseconds < JumpStepMs && (now - CursorAt).TotalMilliseconds > JumpCursorMs) {
                        Jumps.Add(new[] { X, Y, Z, nx, ny, nz }); if (Jumps.Count > 64) Jumps.RemoveAt(0);
                    }
                    PlayerBody(U16(p, 5)); X = nx; Y = ny; Facing = p[17] & 7; Z = nz; Prune();
                } break;
            case 0x21: X = U16(p, 2); Y = U16(p, 4); Facing = p[6] & 7; Z = (sbyte)p[7]; System.Array.Clear(walking, 0, 256); Prune(); break;
            case 0x22: {
                int w = walking[p[1]]; walking[p[1]] = 0;
                if (w == 0) break;
                int dir = (w - 1) & 7;
                // A step in a new direction only turns the player.
                if (dir != Facing) { Facing = dir; break; }
                X += new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[dir]; Y += new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[dir];
                int z = UoMap.StandZ(X, Y, Z); if (z != int.MinValue) Z = z;
                StepAt = System.DateTime.UtcNow; Prune(); break;
            }
            // Hits, mana and stamina updates between full statuses: serial, maximum, current (uo1998 sends these, and the
            // full 0x11 only when the client asks with 0x34).
            case 0xA1: if (p.Length >= 9 && U32(p, 1) == Player) { MaxHits = U16(p, 5); Hits = U16(p, 7); } break;
            case 0xA2: if (p.Length >= 9 && U32(p, 1) == Player) { MaxMana = U16(p, 5); Mana = U16(p, 7); } break;
            case 0xA3: if (p.Length >= 9 && U32(p, 1) == Player) { MaxStam = U16(p, 5); Stam = U16(p, 7); } break;
            // The paperdoll's line, name and title ("Astroglide, Master Ranger"): serial, 60 bytes of text.
            case 0x88: if (p.Length >= 65 && U32(p, 1) == Player) Title = Ascii(p, 5, 60); break;
            case 0x11:
                if (p.Length >= 37) Named(U32(p, 3), Ascii(p, 7, 30));
                if (U32(p, 3) != Player || p.Length < 43) break;
                Name = Ascii(p, 7, 30); Hits = U16(p, 37); MaxHits = U16(p, 39);
                if (p[42] >= 1 && p.Length >= 66) {
                    Str = U16(p, 44); Dex = U16(p, 46); Int = U16(p, 48); Stam = U16(p, 50); MaxStam = U16(p, 52);
                    if (StrStart < 0) { StrStart = Str; DexStart = Dex; IntStart = Int; }
                    Mana = U16(p, 54); MaxMana = U16(p, 56); Gold = (int)U32(p, 58); Armor = U16(p, 62); Weight = U16(p, 64);
                }
                break;
            case 0x3A: Skills3A(p); break;
            case 0x1C: {
                uint who = U32(p, 3); string name = Ascii(p, 14, 30); string text = Ascii(p, 44, p.Length - 44);
                if (who != 0xFFFFFFFF && name.Length > 0) Named(who, name);
                Say(who == 0xFFFFFFFF || name.Length == 0 ? text : name + ": " + text);
                if (text.Contains("under the protection of the town guards")) Guarded = true;
                else if (text.Contains("left the protection of the town guards")) Guarded = false;
                // 0x1C: serial, body (7), type (9). Player speech: a named mobile with a human or human-ghost body
                // (0x190 to 0x193) speaking normally (0), emoting (2), whispering (8) or yelling (9); labels (6) and
                // system lines are not. A human NPC passes this test too; the window hides speakers by name.
                int body = U16(p, 7), type = p[9];
                if (who != 0xFFFFFFFF && (who & 0x40000000) == 0 && name.Length > 0 && name != "System" && body >= 0x190 && body <= 0x193
                    && (type == 0 || type == 2 || type == 8 || type == 9)) {
                    Chat.Add(new[] { System.DateTime.Now.ToString("HH:mm"), name, text }); ChatSeq++;
                    if (Chat.Count > JournalKeep) Chat.RemoveRange(0, Chat.Count - JournalKeep);
                }
                break;
            }
            case 0x6C: CursorOpen = true; CursorType = p[1]; Cursor = U32(p, 2); CursorAt = System.DateTime.UtcNow; break;
            case 0x1D: { uint s = U32(p, 1); Items.Remove(s); Ground.Remove(s); Mobiles.Remove(s); if (s == Backpack) Backpack = 0; break; }
            case 0x25: Put(U32(p, 1), U16(p, 5), U16(p, 8), U16(p, 10), U16(p, 12), U32(p, 14), U16(p, 18)); break;
            case 0x3C: {
                int count = U16(p, 3);
                for (int i = 0; i < count; i++) {
                    int at = 5 + i * 19; if (at + 19 > p.Length) break;
                    if (i == 0) { uint box = U32(p, at + 13); var gone = new System.Collections.Generic.List<uint>(); foreach (var kv in Items) if (kv.Value.Container == box) gone.Add(kv.Key); foreach (var s in gone) Items.Remove(s); }
                    Put(U32(p, at), U16(p, at + 4), U16(p, at + 7), U16(p, at + 9), U16(p, at + 11), U32(p, at + 13), U16(p, at + 17));
                }
                break;
            }
            case 0x2E: Wear(U32(p, 1), U16(p, 5), p[8], U32(p, 9), U16(p, 13)); break;
            case 0x78: {
                uint m = U32(p, 3) & 0x7FFFFFFF; if (p.Length >= 9) Body(m, U16(p, 7));
                if (m != Player && p.Length >= 14) Mobiles[m] = new[] { U16(p, 9), U16(p, 11), (sbyte)p[13] };
                if (m != Player) break;
                if (p.Length >= 9) PlayerBody(U16(p, 7));
                if ((U32(p, 3) & 0x80000000) == 0 && p.Length > 18) Notoriety = p[18];
                for (int at = 19; at + 7 <= p.Length; ) {
                    uint item = U32(p, at); if (item == 0) break;
                    int g = U16(p, at + 4); int hue = 0; if ((g & 0x8000) != 0 && at + 9 <= p.Length) hue = U16(p, at + 7);
                    Wear(item, g & 0x7FFF, p[at + 6], m, hue); at += 7 + ((g & 0x8000) != 0 ? 2 : 0);
                }
                break;
            }
            case 0x1A: {
                uint s = U32(p, 3); int g = U16(p, 7); int at = 9, amount = 1;
                if ((s & 0x80000000) != 0) { amount = U16(p, 9); at = 11; }
                if ((g & 0x8000) != 0) at++;
                int xs = U16(p, at), ys = U16(p, at + 2); int x = xs & 0x7FFF, y = ys & 0x3FFF; at += 4; if ((xs & 0x8000) != 0) at++;
                // After z: a hue (u16) when y's 0x8000 is set, then flags when its 0x4000 is.
                int hue = (ys & 0x8000) != 0 && at + 3 <= p.Length ? U16(p, at + 1) : 0;
                s &= 0x7FFFFFFF;
                Items.Remove(s);
                Ground[s] = new AssistItem { Serial = s, Graphic = g & 0x7FFF, Amount = amount, X = x, Y = y, Z = at < p.Length ? (sbyte)p[at] : 0, Hue = hue };
                break;
            }
        }
    }

    void Put(uint s, int graphic, int amount, int x, int y, uint box, int hue) {
        Ground.Remove(s);
        Items[s] = new AssistItem { Serial = s, Graphic = graphic, Amount = amount, X = x, Y = y, Container = box, Hue = hue };
    }
    void Wear(uint s, int graphic, int layer, uint mobile, int hue) {
        if (mobile != Player) return;
        Ground.Remove(s);
        Items[s] = new AssistItem { Serial = s, Graphic = graphic, Layer = layer, Mobile = mobile, Hue = hue };
        if (layer == 0x15) Backpack = s;
    }
    // Ground items and mobiles beyond GroundReach: a recall or a teleport sends no 0x1D for what was left behind, and a
    // stale mobile on a tile reads as Occupied to the walker.
    void Prune() {
        var far = new System.Collections.Generic.List<uint>();
        foreach (var kv in Ground) if (System.Math.Max(System.Math.Abs(kv.Value.X - X), System.Math.Abs(kv.Value.Y - Y)) > GroundReach) far.Add(kv.Key);
        foreach (var s in far) Ground.Remove(s);
        far.Clear();
        foreach (var kv in Mobiles) if (System.Math.Max(System.Math.Abs(kv.Value[0] - X), System.Math.Abs(kv.Value[1] - Y)) > GroundReach) far.Add(kv.Key);
        foreach (var s in far) Mobiles.Remove(s);
    }

    // Type 0 is the full list ending in id 0; 0xFF one skill. The entry is (id, value) on the UOAIX shard and can carry a
    // base value after; the size is the one whose ids run 1, 2, 3 ... to the zero id.
    void Skills3A(byte[] p) {
        if (p.Length < 4) return;
        // The single update numbers its skill from 0, the full list from 1 (RunUO SkillUpdate; uo1998 sent 3A0008FF001901F9,
        // Magery 50.5, which read 1-based landed on Lockpicking).
        if (p[3] == 0xFF) { if (p.Length >= 8) Skill(U16(p, 4) + 1, U16(p, 6)); return; }
        if (p[3] != 0) return;
        foreach (int size in new[] { 4, 6 }) {
            bool ok = true; int at = 4, id = 1;
            for (; at + 2 <= p.Length; at += size, id++) { int got = U16(p, at); if (got == 0) break; if (got != id || at + size > p.Length) { ok = false; break; } }
            if (!ok || at + 2 > p.Length || U16(p, at) != 0) continue;
            SkillEntry = size;
            for (at = 4; U16(p, at) != 0; at += size) Skill(U16(p, at), U16(p, at + 2));
            return;
        }
        throw new System.Exception("skill list fits neither 4- nor 6-byte entries");
    }
    void Skill(int id, int value) { Skills[id] = value; if (!SkillStart.ContainsKey(id)) SkillStart[id] = value; }

    public int Count(uint box) { int n = 0; foreach (var kv in Items) if (kv.Value.Container == box) n++; return n; }

    public string Summary() {
        lock (Gate) {
            int learned = 0, gained = 0;
            foreach (var kv in Skills) { if (kv.Value > 0) learned++; if (kv.Value != SkillStart[kv.Key]) gained++; }
            int worn = 0; foreach (var kv in Items) if (kv.Value.Layer >= 0) worn++;
            return "player " + Player.ToString("X") + " '" + Name + "' at " + X + "," + Y + "," + Z + " hits " + Hits + "/" + MaxHits
                + " str " + Str + " dex " + Dex + " int " + Int + " gold " + Gold + " weight " + Weight
                + " skills " + Skills.Count + " (" + learned + " above 0, " + gained + " moved, entry " + SkillEntry + ")"
                + " worn " + worn + " pack " + (Backpack == 0 ? "?" : Backpack.ToString("X") + " holding " + Count(Backpack))
                + " items " + Items.Count + " ground " + Ground.Count + " journal " + Journal.Count
                + (CursorOpen ? " cursor " + Cursor.ToString("X") + " type " + CursorType : " no cursor")
                + (Fault != null ? " FAULT " + Fault : "");
        }
    }
}
