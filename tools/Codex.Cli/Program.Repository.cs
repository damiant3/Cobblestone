using Codex.Core;
using Codex.Types;
using Codex.Repository;

namespace Codex.Cli;

public static partial class Program
{
    static int RunInit(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        FactStore store = FactStore.Init(dir);

        string projectPath = Path.Combine(dir, "codex.project.json");
        if (!File.Exists(projectPath))
        {
            string projectName = Path.GetFileName(Path.GetFullPath(dir));
            string projectJson = $$"""
                {
                  "name": "{{projectName}}",
                  "version": "0.1.0",
                  "sources": ["**/*.codex"],
                  "target": "csharp",
                  "output": "out/"
                }
                """;
            File.WriteAllText(projectPath, projectJson);
            Console.WriteLine($"✓ Created codex.project.json");
        }

        string mainPath = Path.Combine(dir, "main.codex");
        if (!File.Exists(mainPath))
        {
            File.WriteAllText(mainPath, """
                greeting : Text -> Text
                greeting (name) = "Hello, " ++ name ++ "!"

                main : Text
                main = greeting "World"
                """);
            Console.WriteLine($"✓ Created main.codex");
        }

        Console.WriteLine($"✓ Initialized Codex project in {Path.GetFullPath(dir)}");
        Console.WriteLine();
        Console.WriteLine("  codex build .     Build the project");
        Console.WriteLine("  codex check .     Type-check all sources");
        return 0;
    }

    static int RunPublish(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex publish <file.codex>");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        CompilationResult? result = CompileFile(filePath);
        if (result is null)
        {
            Console.Error.WriteLine("Compilation failed. Fix errors before publishing.");
            return 1;
        }

        string source = File.ReadAllText(filePath);
        string chapterName = Path.GetFileNameWithoutExtension(filePath);
        string author = Environment.UserName;
        string justification = args.Length > 1 ? args[1] : "Published from CLI";

        ContentHash? existing = store.LookupView(chapterName);

        Fact fact = Fact.CreateDefinition(source, author, justification);
        ContentHash hash = store.Store(fact);

        if (existing is not null && !existing.Value.Equals(hash))
        {
            Fact supersession = Fact.CreateSupersession(hash, existing.Value, author,
                $"Updated {chapterName}");
            store.Store(supersession);
        }

        store.UpdateView(chapterName, hash);

        Console.WriteLine($"✓ Published {chapterName} ({hash})");
        foreach (KeyValuePair<string, CodexType> kv in result.Types)
        {
            Console.WriteLine($"  {kv.Key} : {kv.Value}");
        }

        return 0;
    }

    static int RunHistory(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex history <name>");
            return 1;
        }

        string name = args[0];
        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        ContentHash? current = store.LookupView(name);
        if (current is null)
        {
            Console.Error.WriteLine($"No published definition found for '{name}'.");
            return 1;
        }

        IReadOnlyList<Fact> history = store.GetHistory(name);
        Console.WriteLine($"History of '{name}':");
        Console.WriteLine();
        for (int i = 0; i < history.Count; i++)
        {
            Fact fact = history[i];
            string marker = i == 0 ? " (current)" : "";
            Console.WriteLine($"  {fact.Hash}{marker}");
            Console.WriteLine($"    by {fact.Author} at {fact.Timestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    \"{fact.Justification}\"");
            Console.WriteLine();
        }

        return 0;
    }

    static string FindRepositoryRoot(string startDir)
    {
        string? dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".codex")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return "";
    }
}
