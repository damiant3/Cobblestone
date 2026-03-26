namespace Codex.Cli;

public static partial class Program  // this file is locked.  use a partial.
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        string command = args[0];
        return command switch
        {
            "check" => RunCheck(args.Skip(1).ToArray()),
            "parse" => RunParse(args.Skip(1).ToArray()),
            "build" => RunBuild(args.Skip(1).ToArray()),
            "run" => RunRun(args.Skip(1).ToArray()),
            "repl" => RunRepl(args.Skip(1).ToArray()),
            "read" => RunRead(args.Skip(1).ToArray()),
            "init" => RunInit(args.Skip(1).ToArray()),
            "publish" => RunPublish(args.Skip(1).ToArray()),
            "history" => RunHistory(args.Skip(1).ToArray()),
            "propose" => RunPropose(args.Skip(1).ToArray()),
            "verdict" => RunVerdict(args.Skip(1).ToArray()),
            "proposals" => RunProposals(args.Skip(1).ToArray()),
            "vouch" => RunVouch(args.Skip(1).ToArray()),
            "sync" => RunSync(args.Skip(1).ToArray()),
            "add" => RunAdd(args.Skip(1).ToArray()),
            "remove" => RunRemove(args.Skip(1).ToArray()),
            "pack" => RunPack(args.Skip(1).ToArray()),
            "packages" => RunListPackages(args.Skip(1).ToArray()),
            "bootstrap" => RunBootstrap(args.Skip(1).ToArray()),
            "encode" => RunEncode(args.Skip(1).ToArray()),
            "version" => RunVersion(),
            "--help" or "-h" => RunHelp(),
            _ => UnknownCommand(command)
        };
    }

    static int RunVersion()
    {
        Console.WriteLine("Codex 0.1.0-bootstrap");
        Console.WriteLine("The beginning.");
        return 0;
    }

    static int RunHelp()
    {
        PrintUsage();
        return 0;
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    static void PrintUsage()
    {
        Console.WriteLine("Codex — A language for the rest of human time");
        Console.WriteLine();
        Console.WriteLine("Usage: codex <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  parse <file>      Lex, parse, and display the structure of a Codex file");
        Console.WriteLine("  check <file>      Parse and type-check a Codex file");
        Console.WriteLine("  build <file|dir>  Compile a Codex file or project");
        Console.WriteLine("  bootstrap [dir]   Full self-hosting verification (stage 0→1→2→3, fixed point)");
        Console.WriteLine("                    --target <t>        Target backend (cs|js|rust|py|cpp|go|java|ada|fortran|cobol|babbage|il)");
        Console.WriteLine("                    --targets <t1,t2>   Emit to multiple backends in parallel");
        Console.WriteLine("                    --incremental, -i   Skip unchanged files (uses .codex-build/manifest.json)");
        Console.WriteLine("  run <file>        Compile and execute a Codex file");
        Console.WriteLine("  repl              Interactive evaluation loop");
        Console.WriteLine("  read <file>       Display a prose-mode document as formatted text");
        Console.WriteLine("  init [dir]        Initialize a Codex repository in the given directory");
        Console.WriteLine("  publish <file>    Publish a .codex file to the local repository");
        Console.WriteLine("  history <name>    Show the history of a published definition");
        Console.WriteLine("  propose <file>    Propose a new definition or change");
        Console.WriteLine("  verdict <hash> <decision>  Post a verdict on a proposal");
        Console.WriteLine("  proposals         List all proposals");
        Console.WriteLine("  vouch <hash> <degree>  Vouch for a fact (trust)");
        Console.WriteLine("  sync <path>       Sync facts with another repository");
        Console.WriteLine();
        Console.WriteLine("Package Management:");
        Console.WriteLine("  add <pkg>         Add a package dependency (--version <v>, --path <dir>)");
        Console.WriteLine("  remove <pkg>      Remove a package dependency");
        Console.WriteLine("  pack [dir]        Pack a project into the local package cache");
        Console.WriteLine("  packages          List project dependencies (--cache for cached packages)");
        Console.WriteLine();
        Console.WriteLine("Encoding:");
        Console.WriteLine("  encode [file]     Convert between Unicode and CCE (--from, --to, --output)");
        Console.WriteLine();
        Console.WriteLine("Other:");
        Console.WriteLine("  version           Display the Codex version");
        Console.WriteLine("  --help, -h        Display this help message");
    }
}
