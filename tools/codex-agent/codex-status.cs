using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// codex-status: Structured workstream status tracking for the Codex agent toolkit.
// Data: one key-value text file per entry in .codex-agent/status/<id>.txt
// Commands: list, get <id>, update <id> [--key val ...], create <id> --title <t>

class Program
{
    static string StatusDir => ".codex-agent/status";

    static int Main(string[] args)
    {
        if (args.Length == 0) { List(); return 0; }
        return args[0] switch
        {
            "list" => List(),
            "get" when args.Length >= 2 => Get(args[1]),
            "update" when args.Length >= 2 => Update(args),
            "create" when args.Length >= 2 => Create(args),
            "dashboard" => Dashboard(),
            "help" => Help(),
            _ => Help()
        };
    }

    static Dictionary<string, string> ReadEntry(string path)
    {
        var dict = new Dictionary<string, string>();
        if (!File.Exists(path)) return dict;
        foreach (var line in File.ReadAllLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq > 0) dict[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return dict;
    }

    static void WriteEntry(string path, Dictionary<string, string> fields)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var lines = new List<string>();
        string[] order = ["id", "title", "status", "updated", "agent", "summary", "commit", "detail"];
        foreach (var key in order)
            if (fields.TryGetValue(key, out var val)) lines.Add($"{key}={val}");
        foreach (var kv in fields)
            if (!order.Contains(kv.Key)) lines.Add($"{kv.Key}={kv.Value}");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    static List<Dictionary<string, string>> LoadAll()
    {
        var result = new List<Dictionary<string, string>>();
        if (!Directory.Exists(StatusDir)) return result;
        foreach (var file in Directory.GetFiles(StatusDir, "*.txt").OrderBy(f => f))
            result.Add(ReadEntry(file));
        return result;
    }

    static string Icon(string s) => s switch
    {
        "active" or "investigating" => "[>>]",
        "blocked" => "[!!]",
        "fixed" or "done" or "complete" => "[ok]",
        "wontfix" or "deferred" => "[--]",
        _ => "[  ]"
    };

    static int List()
    {
        var entries = LoadAll();
        if (entries.Count == 0)
        {
            Console.WriteLine("No status entries. Use 'codex-status create <id> --title <t>' to add one.");
            return 0;
        }
        Console.WriteLine("=== Workstream Status ===\n");
        Console.WriteLine($"  {"Icon",-6}{"Status",-14}{"ID",-28}{"Updated",-12}{"Summary"}");
        Console.WriteLine($"  {"----",-6}{"------",-14}{"--",-28}{"-------",-12}{"-------"}");
        foreach (var e in entries)
        {
            var id = e.GetValueOrDefault("id", "?");
            var st = e.GetValueOrDefault("status", "?");
            var updated = e.GetValueOrDefault("updated", "");
            var summary = e.GetValueOrDefault("summary", "");
            if (summary.Length > 44) summary = summary[..41] + "...";
            Console.WriteLine($"  {Icon(st),-6}{st,-14}{id,-28}{updated,-12}{summary}");
        }
        Console.WriteLine($"\n  {entries.Count} workstream(s).");
        return 0;
    }

    static int Get(string id)
    {
        var path = Path.Combine(StatusDir, id + ".txt");
        if (!File.Exists(path))
        {
            Console.WriteLine($"No status entry '{id}'. Use 'codex-status list' to see all.");
            return 1;
        }
        var fields = ReadEntry(path);
        Console.WriteLine($"=== {fields.GetValueOrDefault("title", id)} ===\n");
        foreach (var kv in fields)
            Console.WriteLine($"  {kv.Key,-10} {kv.Value}");
        return 0;
    }

    static int Update(string[] args)
    {
        var id = args[1];
        var path = Path.Combine(StatusDir, id + ".txt");
        var fields = ReadEntry(path);
        if (fields.Count == 0) fields["id"] = id;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--"))
            {
                fields[args[i][2..]] = args[i + 1];
                i++;
            }
        }
        fields["updated"] = DateTime.Now.ToString("yyyy-MM-dd");
        WriteEntry(path, fields);
        Console.WriteLine($"Updated {id}: {fields.GetValueOrDefault("status", "?")} -- {fields.GetValueOrDefault("summary", "")}");
        return 0;
    }

    static int Create(string[] args)
    {
        var id = args[1];
        var path = Path.Combine(StatusDir, id + ".txt");
        if (File.Exists(path))
        {
            Console.WriteLine($"Status entry '{id}' already exists. Use 'codex-status update' to modify.");
            return 1;
        }
        var fields = new Dictionary<string, string> { ["id"] = id, ["status"] = "active" };
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--"))
            {
                fields[args[i][2..]] = args[i + 1];
                i++;
            }
        }
        fields["updated"] = DateTime.Now.ToString("yyyy-MM-dd");
        WriteEntry(path, fields);
        Console.WriteLine($"Created {id}: {fields.GetValueOrDefault("title", "(no title)")}");
        return 0;
    }

    static int Dashboard()
    {
        // Compact format for embedding in orient output
        var entries = LoadAll();
        if (entries.Count == 0) { Console.WriteLine("  (no status entries)"); return 0; }
        foreach (var e in entries)
        {
            var id = e.GetValueOrDefault("id", "?");
            var st = e.GetValueOrDefault("status", "?");
            var summary = e.GetValueOrDefault("summary", "");
            if (summary.Length > 52) summary = summary[..49] + "...";
            Console.WriteLine($"  {Icon(st)} {st,-12} {id,-26} {summary}");
        }
        return 0;
    }

    static int Help()
    {
        Console.WriteLine("""
        codex-status — Workstream status tracking

        Usage:
          codex-status                     Show all workstreams
          codex-status list                Show all workstreams (same as no args)
          codex-status get <id>            Show detail for one workstream
          codex-status update <id> [opts]  Update a workstream (upsert)
          codex-status create <id> [opts]  Create a new workstream
          codex-status dashboard           Compact format for orient embedding

        Options for update/create:
          --status <s>     active|blocked|fixed|done|investigating|deferred
          --title <t>      Human-readable title
          --summary <text> One-line summary
          --agent <name>   Who last touched this
          --commit <hash>  Related commit
          --detail <text>  Longer description

        Data: .codex-agent/status/<id>.txt (one file per workstream, merge-safe)
        """);
        return 0;
    }
}
