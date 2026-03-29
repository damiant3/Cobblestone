using System;
using System.IO;
using System.Threading;

const int StackSize = 128 * 1024 * 1024; // 128 MB stack

int exitCode = 0;
Thread thread = new Thread(() => { exitCode = Run(args); }, StackSize);
thread.Start();
thread.Join();
return exitCode;

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        Console.WriteLine("codex — The Codex compiler (self-hosted)");
        Console.WriteLine();
        Console.WriteLine("Usage: codex build <file.codex>  [-o <output.cs> ]");
        Console.WriteLine("       codex version");
        Console.WriteLine();
        Console.WriteLine("Compiles a .codex source file to C#.");
        return 0;
    }

    string command = args[0];

    if (command == "version")
    {
        Console.WriteLine("codex 0.1.0 (self-hosted, Stage 2)");
        return 0;
    }

    if (command == "build")
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: expected a .codex source file.");
            Console.Error.WriteLine("Usage: codex build <file.codex> [-o <output.cs>]");
            return 1;
        }

        string inputPath = args[1];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: file not found: {inputPath}");
            return 1;
        }

        string moduleName = Path.GetFileNameWithoutExtension(inputPath);
        string? outputPath = null;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
        }

        outputPath ??= Path.ChangeExtension(inputPath, ".cs");

        string source = File.ReadAllText(inputPath);

        try
        {
            string result = Codex_Codex_Codex.compile(source, moduleName);
            File.WriteAllText(outputPath, result);
            Console.WriteLine($"✓ Compiled {inputPath} → {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: compilation failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'codex' with no arguments for help.");
    return 1;
}
