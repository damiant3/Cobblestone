#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Uoaix.Assist.Tests;

public static class PipeTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("pipe: bytes come out in the order they went in", InOrder),
        new TestCase("pipe: a read takes at most its count", ReadAtMostCount),
        new TestCase("pipe: a read of 0 bytes returns 0 at once", ReadZero),
        new TestCase("pipe: a write of 0 bytes adds nothing", WriteZero),
        new TestCase("pipe: a range ending at the buffer's end is accepted", RangeAtEnd),
        new TestCase("pipe: a range 1 past the buffer's end is refused", RangePastEnd),
        new TestCase("pipe: a negative offset is refused", NegativeOffset),
        new TestCase("pipe: a negative count is refused", NegativeCount),
        new TestCase("pipe: a null buffer is refused", NullBuffer),
        new TestCase("pipe: bytes written before the close are still read", ReadAfterClose),
        new TestCase("pipe: a closed empty pipe reads 0", ClosedEmpty),
        new TestCase("pipe: a write after the close is refused", WriteAfterClose),
        new TestCase("pipe: a blocked read wakes on a write", BlockedReadWakes),
        new TestCase("pipe: a blocked read wakes on the close with 0", BlockedReadCloses),
        new TestCase("pipe: quiet only while empty and its reader waits", Quiet),
        new TestCase("pipe: the clock waits for the reader to take the bytes", ClockWaitsForReader),
        new TestCase("pipe: the stream reads the pipe and writes the sink", StreamEnds),
        new TestCase("pipe: a failed pipe hands over its bytes, then throws, and is quiet", Fails));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static byte[] ReadExactly(BytePipe pipe, int n)
    {
        byte[] got = new byte[n];
        int have = 0;
        while (have < n)
        {
            int r = pipe.Read(got, have, n - have);
            if (r == 0)
                throw new CheckFailed("the pipe closed after " + have + " of " + n + " bytes");
            have += r;
        }
        return got;
    }

    static void InOrder()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 1, 2, 3 }, 0, 3);
        pipe.Write(new byte[] { 4 }, 0, 1);
        Check.Bytes(new byte[] { 1, 2, 3, 4 }, ReadExactly(pipe, 4), "bytes");
    }

    static void ReadAtMostCount()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 9, 8, 7 }, 0, 3);
        byte[] buffer = new byte[2];
        Check.Equal(2, pipe.Read(buffer, 0, 2), "the first read");
        Check.Equal(1, pipe.Buffered, "left in the pipe");
    }

    static void ReadZero()
    {
        BytePipe pipe = new(null);
        Check.Equal(0, pipe.Read(new byte[1], 0, 0), "a read of 0");
    }

    static void WriteZero()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 5 }, 0, 0);
        Check.Equal(0, pipe.Buffered, "bytes in the pipe");
    }

    static void RangeAtEnd()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 1, 2, 3 }, 1, 2);
        Check.Bytes(new byte[] { 2, 3 }, ReadExactly(pipe, 2), "bytes");
    }

    static void RangePastEnd()
    {
        BytePipe pipe = new(null);
        Check.Throws<ArgumentOutOfRangeException>(() => pipe.Write(new byte[3], 1, 3), "a write 1 past the end");
        Check.Throws<ArgumentOutOfRangeException>(() => pipe.Read(new byte[3], 1, 3), "a read 1 past the end");
    }

    static void NegativeOffset()
    {
        BytePipe pipe = new(null);
        Check.Throws<ArgumentOutOfRangeException>(() => pipe.Write(new byte[3], -1, 1), "a write at -1");
    }

    static void NegativeCount()
    {
        BytePipe pipe = new(null);
        Check.Throws<ArgumentOutOfRangeException>(() => pipe.Read(new byte[3], 0, -1), "a read of -1");
    }

    static void NullBuffer()
    {
        BytePipe pipe = new(null);
        Check.Throws<ArgumentNullException>(() => pipe.Write(null!, 0, 0), "a null write");
    }

    static void ReadAfterClose()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 7, 6 }, 0, 2);
        pipe.Close();
        Check.Bytes(new byte[] { 7, 6 }, ReadExactly(pipe, 2), "bytes");
        Check.Equal(0, pipe.Read(new byte[1], 0, 1), "the read after them");
    }

    static void ClosedEmpty()
    {
        BytePipe pipe = new(null);
        pipe.Close();
        Check.Equal(0, pipe.Read(new byte[1], 0, 1), "a read");
    }

    static void WriteAfterClose()
    {
        BytePipe pipe = new(null);
        pipe.Close();
        Check.Throws<IOException>(() => pipe.Write(new byte[1], 0, 1), "a write");
    }

    static void WaitUntilQuiet(BytePipe pipe)
    {
        while (!pipe.IsQuiet)
            Thread.Yield();
    }

    static void BlockedReadWakes()
    {
        BytePipe pipe = new(null);
        int got = -1;
        Thread reader = new(() => got = pipe.Read(new byte[4], 0, 4));
        reader.IsBackground = true;
        reader.Start();
        WaitUntilQuiet(pipe);
        pipe.Write(new byte[] { 1, 2 }, 0, 2);
        reader.Join();
        Check.Equal(2, got, "bytes read");
    }

    static void BlockedReadCloses()
    {
        BytePipe pipe = new(null);
        int got = -1;
        Thread reader = new(() => got = pipe.Read(new byte[4], 0, 4));
        reader.IsBackground = true;
        reader.Start();
        WaitUntilQuiet(pipe);
        pipe.Close();
        reader.Join();
        Check.Equal(0, got, "bytes read");
    }

    static void Quiet()
    {
        BytePipe pipe = new(null);
        Check.False(pipe.IsQuiet, "quiet with no reader waiting");
        ManualResetEventSlim handled = new(false);
        ManualResetEventSlim release = new(false);
        Thread reader = new(() =>
        {
            byte[] one = new byte[1];
            pipe.Read(one, 0, 1);
            handled.Set();
            release.Wait();
            pipe.Read(one, 0, 1);
        });
        reader.IsBackground = true;
        reader.Start();
        WaitUntilQuiet(pipe);
        Check.True(pipe.IsQuiet, "quiet while empty and the reader waits");
        pipe.Write(new byte[] { 1 }, 0, 1);
        handled.Wait();
        Check.False(pipe.IsQuiet, "quiet while the reader handles what it took");
        release.Set();
        WaitUntilQuiet(pipe);
        pipe.Write(new byte[] { 2, 3 }, 0, 2);
        reader.Join();
        Check.False(pipe.IsQuiet, "quiet with a byte nobody took");
        pipe.Close();
        Check.False(pipe.IsQuiet, "quiet when closed with a byte left");
        pipe.Read(new byte[1], 0, 1);
        Check.True(pipe.IsQuiet, "quiet when closed and empty");
    }

    static void ClockWaitsForReader()
    {
        DateTime start = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        VirtualClock clock = new(start);
        BytePipe pipe = new(clock);
        DateTime handledAt = DateTime.MinValue;
        Thread reader = new(() =>
        {
            byte[] one = new byte[1];
            while (pipe.Read(one, 0, 1) > 0)
                handledAt = clock.UtcNow;
        });
        reader.IsBackground = true;
        reader.Start();
        WaitUntilQuiet(pipe);
        clock.Run(() =>
        {
            pipe.Write(new byte[] { 1 }, 0, 1);
            clock.Sleep(100);
        });
        Check.Equal(start, handledAt, "the time the reader handled the byte");
        Check.Equal(start.AddMilliseconds(100), clock.UtcNow, "UtcNow after the sleep");
        pipe.Close();
        reader.Join();
    }

    static void Fails()
    {
        BytePipe pipe = new(null);
        pipe.Write(new byte[] { 3 }, 0, 1);
        pipe.Fail("forcibly closed");
        Check.False(pipe.IsQuiet, "quiet with a byte left");
        Check.Equal(1, pipe.Read(new byte[1], 0, 1), "the byte written before the failure");
        Check.True(pipe.IsQuiet, "quiet once failed and empty");
        IOException e = Check.Throws<IOException>(() => pipe.Read(new byte[1], 0, 1), "the read after");
        Check.Equal("forcibly closed", e.Message, "message");
    }

    static void StreamEnds()
    {
        BytePipe from = new(null);
        MemoryStream sink = new();
        bool closed = false;
        PipeStream stream = new(from, (b, o, n) => sink.Write(b, o, n), () => closed = true);
        from.Write(new byte[] { 4, 5 }, 0, 2);
        byte[] got = new byte[2];
        Check.Equal(2, stream.Read(got, 0, 2), "read");
        stream.Write(new byte[] { 6, 7, 8 }, 1, 2);
        Check.Bytes(new byte[] { 7, 8 }, sink.ToArray(), "written");
        Check.True(stream.CanRead, "CanRead");
        Check.True(stream.CanWrite, "CanWrite");
        Check.False(stream.CanSeek, "CanSeek");
        Check.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin), "Seek");
        Check.Throws<NotSupportedException>(() => _ = stream.Length, "Length");
        Check.Throws<NotSupportedException>(() => stream.SetLength(1), "SetLength");
        Check.Throws<NotSupportedException>(() => _ = stream.Position, "Position get");
        Check.Throws<NotSupportedException>(() => stream.Position = 1, "Position set");
        stream.Flush();
        stream.Close();
        Check.True(closed, "the close reached the sink's side");
        Check.Equal(0, stream.Read(got, 0, 1), "a read after the close");
    }
}
