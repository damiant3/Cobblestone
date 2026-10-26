// The server's side of walking for a FakeShard over a TestMap (docs/Designs/Active/Apps/AssistTesting.md, T3): a step
// in a new direction turns the player, a step onto grass moves it, and a step onto a wall, water or a shut door is
// refused where the player stands; a use of a door swings it. uo1998 answers so (0x22 the step taken, 0x21 refused with
// the player's place, a door's swing a new 0x1A).
#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.IO;

namespace Uoaix.Assist.Tests;

public sealed class StepShard
{
    sealed class Leaf
    {
        public int Graphic;
        public int X;
        public int Y;
        public bool Shut;
        public int Z;
        public bool Swings;
        public bool WaitFirst;
        public bool Vanishes;
        public int Uses;
        public bool ShutsOnStep;
    }

    static readonly ImmutableArray<int> s_dx = [ 0, 1, 1, 1, 0, -1, -1, -1 ];
    static readonly ImmutableArray<int> s_dy = [ -1, -1, 0, 1, 1, 1, 0, -1 ];

    readonly Lock m_gate = new();
    readonly FakeShard m_shard;
    readonly TestMap m_map;
    Map<uint, Leaf> m_doors = Map<uint, Leaf>.Empty;
    Set<long> m_refuseOnce = Set<long>.Empty;
    ValueMap<long, int> m_entered = ValueMap<long, int>.Empty;
    int m_x;
    int m_y;
    int m_facing = 2;
    int m_refusals;

    // The player logged in at x, y facing east, and the shard answering its steps and uses.
    public StepShard(FakeShard shard, TestMap map, int x, int y)
    {
        m_shard = shard;
        m_map = map;
        m_x = x;
        m_y = y;
        shard.ServerSends(Packets.LoginConfirm(x, y, 0));
        shard.Answer = Answer;
        shard.Clock.Sleep(1);
    }

    static long Key(int x, int y) => ((long)x << 16) | (uint)y;

    // The player put at x, y by something other than a step (a recall's landing).
    public void MoveTo(int x, int y)
    {
        lock (m_gate)
        {
            m_x = x;
            m_y = y;
        }
    }

    public int X
    {
        get
        {
            lock (m_gate)
                return m_x;
        }
    }

    public int Y
    {
        get
        {
            lock (m_gate)
                return m_y;
        }
    }

    public int Refusals
    {
        get
        {
            lock (m_gate)
                return m_refusals;
        }
    }

    // A door at x, y, z with its art; swings: a use moves it between shut and open (a set art one past or back, an art
    // in no set a tile north and back); else a use answers nothing. waitFirst: the first use answered "You must wait to
    // perform another action." and not swung; vanishes: a use deletes the door; shutsOnStep: the first step onto it
    // while open finds it shut by itself (as uo1998's doors swing back), the redraw sent and the step refused. A door
    // bars only the floor at z 0.
    public void Door(uint serial, int graphic, int x, int y, bool shut, bool swings = true, int z = 0,
        bool waitFirst = false, bool vanishes = false, bool shutsOnStep = false)
    {
        lock (m_gate)
            m_doors = m_doors.Set(serial,
                new Leaf { Graphic = graphic, X = x, Y = y, Z = z, Shut = shut, Swings = swings, WaitFirst = waitFirst,
                Vanishes = vanishes, ShutsOnStep = shutsOnStep });
        m_shard.ServerSends(Packets.Ground(serial, graphic, x, y, z));
        m_shard.Clock.Sleep(1);
    }

    // The virtual ms each step waits for its answer; 0 answers at once.
    public int AnswerMs
    {
        get; set;
    }

    // The uses of a door still standing, 0 for one that vanished.
    public int Uses(uint serial)
    {
        lock (m_gate)
            return m_doors.TryGet(serial, out Leaf? d) ? d.Uses : 0;
    }

    public bool Standing(uint serial)
    {
        lock (m_gate)
            return m_doors.ContainsKey(serial);
    }

    // The steps taken onto x, y.
    public int Entered(int x, int y)
    {
        lock (m_gate)
            return m_entered.Get(Key(x, y), 0);
    }

    // The next step onto x, y is refused once, whatever stands there.
    public void RefuseOnce(int x, int y)
    {
        lock (m_gate)
            m_refuseOnce = m_refuseOnce.Add(Key(x, y));
    }

    // The caller holds m_gate.
    bool Closed(int x, int y)
    {
        if (m_refuseOnce.Contains(Key(x, y)))
        {
            m_refuseOnce = m_refuseOnce.Remove(Key(x, y));
            return true;
        }
        if (m_map.IsWall(x, y) || m_map.IsWater(x, y))
            return true;
        foreach (KeyValuePair<uint, Leaf> d in m_doors)
        {
            if (d.Value.X == x && d.Value.Y == y && d.Value.Z == 0 && d.Value.Shut)
                return true;
        }
        return false;
    }

    // Its replies to one client packet, empty for one not its own: chained with another shard's where both run.
    public ImmutableArray<byte[]> Answer(byte[] p)
    {
        lock (m_gate)
        {
            if (p[0] == 0x02)
            {
                ImmutableArray<byte[]> step = StepAnswer(p[1] & 7, p[2]);
                if (AnswerMs == 0)
                    return step;
                // A late answer to a link the case has closed goes nowhere, as the real server's would: thrown on this
                // actor it ended the whole run after every case had passed (cover-assist, 2026-10-24).
                m_shard.Also(AnswerMs, () =>
                {
                    try
                    {
                        foreach (byte[] reply in step)
                            m_shard.ServerSends(reply);
                    }
                    catch (IOException)
                    {
                    }
                });
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] == 0x06)
                return UseAnswer((uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]));
            return ImmutableArray<byte[]>.Empty;
        }
    }

    // The caller holds m_gate.
    ImmutableArray<byte[]> StepAnswer(int dir, int seq)
    {
        if (dir != m_facing)
        {
            m_facing = dir;
            return ImmutableArray.Create<byte[]>(Packets.StepAck(seq));
        }
        int nx = m_x + s_dx[dir];
        int ny = m_y + s_dy[dir];
        foreach (KeyValuePair<uint, Leaf> d in m_doors)
        {
            if (d.Value.ShutsOnStep && !d.Value.Shut && d.Value.Z == 0 && Covers(d.Value, nx, ny))
            {
                d.Value.ShutsOnStep = false;
                Swing(d.Value);
                m_refusals++;
                return ImmutableArray.Create(Packets.Ground(d.Key, d.Value.Graphic, d.Value.X, d.Value.Y, 0),
                    Packets.StepRefused(seq, m_x, m_y, m_facing));
            }
        }
        if (Closed(nx, ny))
        {
            m_refusals++;
            return ImmutableArray.Create<byte[]>(Packets.StepRefused(seq, m_x, m_y, m_facing));
        }
        m_x = nx;
        m_y = ny;
        m_entered = m_entered.Set(Key(nx, ny), m_entered.Get(Key(nx, ny), 0) + 1);
        return ImmutableArray.Create<byte[]>(Packets.StepAck(seq));
    }

    // The caller holds m_gate.
    ImmutableArray<byte[]> UseAnswer(uint serial)
    {
        if (!m_doors.TryGet(serial, out Leaf? d))
            return ImmutableArray<byte[]>.Empty;
        d.Uses++;
        if (d.Vanishes)
        {
            m_doors = m_doors.Remove(serial);
            return ImmutableArray.Create<byte[]>(Packets.Delete(serial));
        }
        if (d.WaitFirst && d.Uses == 1)
            return ImmutableArray.Create<byte[]>(Packets.Speech(0xFFFFFFFF, "",
                "You must wait to perform another action.", 0));
        if (!d.Swings)
            return ImmutableArray<byte[]>.Empty;
        // A set art steps by its own parity, so a door whose art misreads its state swings as the art says.
        Swing(d);
        return ImmutableArray.Create<byte[]>(Packets.Ground(serial, d.Graphic, d.X, d.Y, 0));
    }

    // The doorway a leaf closes: its tile, and for an art in no set (which swings a tile north) its shut tile too.
    static bool Covers(Leaf d, int x,
        int y) => d.X == x && (d.Y == y || AssistFeatures.DoorShut(d.Graphic) is null && d.Y + 1 == y);

    // The caller holds m_gate.
    static void Swing(Leaf d)
    {
        bool? artShut = AssistFeatures.DoorShut(d.Graphic);
        if (artShut is not null)
            d.Graphic += artShut.Value ? 1 : -1;
        else
            d.Y += d.Shut ? -1 : 1;
        d.Shut = !d.Shut;
    }
}
