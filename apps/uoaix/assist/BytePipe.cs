// An in-memory byte stream, one way (docs/Designs/Active/Apps/AssistTesting.md, instruments): the fake shard's side of
// a game connection. Quiet while it holds no bytes and its reader is waiting, so the virtual clock moves time only
// once the reader has taken and handled everything written.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed class BytePipe : IQuiet
{
    readonly Lock m_gate = new();
    readonly Queue<byte> m_bytes = new();
    readonly ManualResetEventSlim m_ready = new(false);
    readonly VirtualClock? m_clock;
    bool m_closed;
    bool m_waiting;
    string? m_failure;

    public BytePipe(VirtualClock? clock)
    {
        m_clock = clock;
        m_clock?.Watch(this);
    }

    public bool IsQuiet
    {
        get
        {
            lock (m_gate)
                return m_bytes.Count == 0 && (m_waiting || m_closed || m_failure is not null);
        }
    }

    public int Buffered
    {
        get
        {
            lock (m_gate)
                return m_bytes.Count;
        }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        CheckRange(buffer, offset, count);
        lock (m_gate)
        {
            if (m_closed)
                throw new IOException("the pipe is closed");
            for (int i = 0; i < count; i++)
                m_bytes.Enqueue(buffer[offset + i]);
            m_ready.Set();
        }
    }

    // Blocks until a byte is there or the pipe is closed; 0 means closed and empty.
    public int Read(byte[] buffer, int offset, int count)
    {
        CheckRange(buffer, offset, count);
        if (count == 0)
            return 0;
        while (true)
        {
            lock (m_gate)
            {
                if (m_bytes.Count > 0)
                {
                    int n = Math.Min(count, m_bytes.Count);
                    for (int i = 0; i < n; i++)
                        buffer[offset + i] = m_bytes.Dequeue();
                    m_waiting = false;
                    return n;
                }
                if (m_failure is not null)
                    throw new IOException(m_failure);
                if (m_closed)
                    return 0;
                m_waiting = true;
                m_ready.Reset();
            }
            m_clock?.Settle();
            m_ready.Wait();
        }
    }

    public void Close()
    {
        lock (m_gate)
        {
            m_closed = true;
            m_ready.Set();
        }
        m_clock?.Settle();
    }

    // The reader's next read, once the bytes written are taken, throws an IOException with message: a socket the far
    // side reset ("An existing connection was forcibly closed by the remote host").
    public void Fail(string message)
    {
        lock (m_gate)
        {
            m_failure = message;
            m_ready.Set();
        }
    }

    static void CheckRange(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(count),
                "offset " + offset + " and count " + count + " lie outside " + buffer.Length + " bytes");
    }
}

// A game connection's end as GameLink sees it: reads come from one pipe, writes go to a sink.
public sealed class PipeStream : Stream
{
    readonly BytePipe m_from;
    readonly Action<byte[], int, int> m_to;
    readonly Action m_closed;

    public PipeStream(BytePipe from, Action<byte[], int, int> to, Action closed)
    {
        m_from = from;
        m_to = to;
        m_closed = closed;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return m_from.Read(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        m_to(buffer, offset, count);
    }

    // The pipe closes before the callback runs, so a callback that throws still ends the reader.
    public override void Close()
    {
        m_from.Close();
        m_closed();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
}
