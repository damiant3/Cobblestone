// The assertions of the Assist's suites (docs/Designs/Active/Apps/AssistTesting.md): each throws CheckFailed with what
// was expected and what came.
#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Uoaix.Assist.Tests;

public sealed class CheckFailed : Exception
{
    public CheckFailed(string message)
        : base(message)
    {
    }
}

public sealed record TestCase(string Name, Action Body);

public static class Check
{
    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!Equals(expected, actual))
            throw new CheckFailed(what + ": expected " + Show(expected) + ", got " + Show(actual));
    }

    public static void True(bool condition, string what)
    {
        if (!condition)
            throw new CheckFailed(what + ": expected true");
    }

    public static void False(bool condition, string what)
    {
        if (condition)
            throw new CheckFailed(what + ": expected false");
    }

    public static void Null(object? value, string what)
    {
        if (value is not null)
            throw new CheckFailed(what + ": expected null, got " + Show(value));
    }

    public static void Bytes(byte[] expected, byte[] actual, string what)
    {
        if (expected.Length != actual.Length)
            throw new CheckFailed(what + ": expected " + expected.Length + " bytes " + UoHex.Of(expected) + ", got "
                + actual.Length + " bytes " + UoHex.Of(actual));
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
                throw new CheckFailed(what + ": byte " + i + " differs: expected " + UoHex.Of(expected) + ", got "
                    + UoHex.Of(actual));
        }
    }

    // body throws an exception of type TException; anything else, or nothing, fails.
    public static TException Throws<TException>(Action body, string what)
        where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException e)
        {
            return e;
        }
        catch (Exception e)
        {
            throw new CheckFailed(what + ": expected " + typeof(TException).Name + ", got " + e.GetType().Name + " "
                + e.Message);
        }
        throw new CheckFailed(what + ": expected " + typeof(TException).Name + ", nothing was thrown");
    }

    static string Show(object? value)
    {
        return value is null ? "null" : value is string s ? "\"" + s + "\"" : value.ToString() ?? "";
    }
}

public static class TestRunner
{
    // A case still running after HangMs of wall time is a deadlock: it fails, and its thread is abandoned. The only
    // wall time any test reads. ASSIST_HANG_MS sets it for an instrumented run (cover-assist.ps1), whose code runs 5 to
    // 8 times slower: a circuit case taking 1.1 to 2.4 s plain ran past 10 s there (2026-10-22).
    static readonly int s_hangMs = int.TryParse(Environment.GetEnvironmentVariable("ASSIST_HANG_MS"), out int ms)
        && ms > 0 ? ms : 10000;
    public static int HangMs => s_hangMs;

    // Runs every case, prints PASS or FAIL for each and the counts; the number of failures.
    public static int Run(ImmutableArray<TestCase> cases, Action<string> print)
    {
        int failed = 0;
        foreach (TestCase c in cases)
        {
            string? why = RunOne(c);
            if (why is null)
            {
                print("PASS " + c.Name);
            }
            else
            {
                failed++;
                print("FAIL " + c.Name + ": " + why);
            }
        }
        print("cases " + cases.Length + ", passed " + (cases.Length - failed) + ", failed " + failed);
        return failed;
    }

    static string? RunOne(TestCase c)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                c.Body();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(s_hangMs))
            return "hung past " + s_hangMs + " ms";
        if (failure is null)
            return null;
        Exception shown = failure is InvalidOperationException && failure.InnerException is not null
            ? failure.InnerException : failure;
        return shown.GetType().Name + ": " + shown.Message;
    }
}
