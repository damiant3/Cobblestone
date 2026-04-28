// Stack-bumped launcher for Codex.Codex.dll. The selfhost compiler
// uses heavily-curried lambdas that overflow .NET's 1MB default thread
// stack on full self-source. Run on a 32MB-stack thread.
//
// Modes (argv):
//   CSHARP <input-path> <output-path> <chapter-name>
//     Compile a .codex source file to C# text. Reads input as Unicode,
//     converts to CCE, runs Codex_Codex_Codex.compile_text, writes
//     emitted C# back as Unicode.
//   BINARY <input-path> <output-path> <chapter-name> [exit-mode] [wd-mode]
//     Compile a .codex source file to a bare-metal x86-64 ELF.
//     exit-mode: repl|qemu-exit (default qemu-exit).
//     wd-mode: progress|pet (default progress).
using System;
using System.IO;
using System.Threading;

namespace CodexHost;

public static class Program
{
    public static int Main(string[] args)
    {
        int rc = 0;
        Exception err = null;
        Thread t = new(() =>
        {
            try { rc = Run(args); }
            catch (Exception e) { err = e; rc = 1; }
        }, 32 * 1024 * 1024);
        t.Start();
        t.Join();
        if (err != null) Console.Error.WriteLine(err);
        return rc;
    }

    static int Run(string[] args)
    {
        if (args.Length < 1) { Usage(); return 1; }
        string mode = args[0];
        if (mode == "CSHARP")
        {
            if (args.Length < 4) { Usage(); return 1; }
            string source = CodexHost.CceTable.Encode(File.ReadAllText(args[1]));
            CompileTextResult r = Codex_Codex_Codex.compile_text(source, CodexHost.CceTable.Encode(args[3]), new CtCSharp());
            if (bag_has_errors_local(r.bag))
            {
                PrintErrors(r.bag);
                return 1;
            }
            File.WriteAllText(args[2], CodexHost.CceTable.Decode(r.text));
            return 0;
        }
        if (mode == "BINARY")
        {
            if (args.Length < 4) { Usage(); return 1; }
            string source = CodexHost.CceTable.Encode(File.ReadAllText(args[1]));
            X86_64ExitMode exitMode = (args.Length >= 5 ? args[4] : "qemu-exit") switch
            {
                "repl" => new ExitRepl(),
                "qemu-exit" => new ExitQemuExit(),
                _ => throw new ArgumentException("exit-mode must be repl|qemu-exit"),
            };
            X86_64WatchdogMode wdMode = (args.Length >= 6 ? args[5] : "progress") switch
            {
                "progress" => new WatchdogProgress(),
                "pet" => new WatchdogPet(),
                _ => throw new ArgumentException("wd-mode must be progress|pet"),
            };
            EmitChapterResult r = Codex_Codex_Codex.compile_to_binary_with_options(source, CodexHost.CceTable.Encode(args[3]), exitMode, wdMode);
            if (bag_has_errors_local(r.bag))
            {
                PrintErrors(r.bag);
                return 1;
            }
            byte[] bytes = new byte[r.bytes.Count];
            for (int i = 0; i < r.bytes.Count; i++) bytes[i] = (byte)r.bytes[i];
            File.WriteAllBytes(args[2], bytes);
            return 0;
        }
        Usage();
        return 1;
    }

    static bool bag_has_errors_local(DiagnosticBag bag) => Codex_Codex_Codex.bag_has_errors(bag);

    static void PrintErrors(DiagnosticBag bag)
    {
        var diags = Codex_Codex_Codex.bag_diagnostics(bag);
        foreach (var d in diags)
            Console.Error.WriteLine($"{d.span.start.line}:{d.span.start.column}: {CodexHost.CceTable.Decode(d.message)}");
    }

    static void Usage()
    {
        Console.Error.WriteLine("Usage: CodexHost CSHARP <input> <output> <chapter-name>");
        Console.Error.WriteLine("   or: CodexHost BINARY <input> <output> <chapter-name> [exit-mode] [wd-mode]");
    }
}
