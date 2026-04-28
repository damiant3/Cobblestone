// Stack-bumped entry point for Codex.Cli-Codex.exe. Selfhost's curried
// lambdas overflow .NET's default 1MB stack when compiling large source
// (700KB+). We launch Codex_CliCodex.opening() on a 32MB-stack thread so
// the build subcommand can compile selfhost itself without stack-overflow.
using System;
using System.Threading;

public static class Bridge
{
    public static int Main(string[] args)
    {
        int rc = 0;
        Exception err = null;
        Thread t = new(() =>
        {
            try { Codex_CliCodex.opening(); }
            catch (Exception e) { err = e; rc = 1; }
        }, 32 * 1024 * 1024);
        t.Start();
        t.Join();
        if (err != null) Console.Error.WriteLine(err);
        return rc;
    }
}
