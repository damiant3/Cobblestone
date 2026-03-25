using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_sdiff.main();

public static class Codex_sdiff
{
    public static string snap_path(string file)
    {
        return string.Concat(file, ".snap");
    }

    public static long count_lines(string content)
    {
        return ((((long)content.Length) == 0L) ? 0L : ((long)new List<string>(content.Split("\n")).Count));
    }

    public static object do_snap(string file)
    {
        (File.Exists(file) ? ((Func<string, object>)((content) => ((Func<long, object>)((lines) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Snapshot saved: ", string.Concat(file, string.Concat(" (", string.Concat((lines).ToString(), " lines)")))))))(File.WriteAllText(snap_path(file), content))))(count_lines(content))))(File.ReadAllText(file)) : Console.WriteLine(string.Concat("  File not found: ", file)));
        return null;
    }

    public static object do_restore(string file)
    {
        ((Func<string, object>)((snap) => (File.Exists(snap) ? ((Func<string, object>)((content) => ((Func<long, object>)((lines) => ((Func<object, object>)((_) => Console.WriteLine(string.Concat("  Restored: ", string.Concat(file, string.Concat(" (", string.Concat((lines).ToString(), " lines)")))))))(File.WriteAllText(file, content))))(count_lines(content))))(File.ReadAllText(snap)) : Console.WriteLine(string.Concat("  No snapshot found for ", string.Concat(file, ". Run 'snap' first."))))))(snap_path(file));
        return null;
    }

    public static string diff_report(string file, long old_lines, long new_lines, long delta)
    {
        return string.Concat("-- Diff: ", string.Concat(file, string.Concat(" --\n", string.Concat("  Before: ", string.Concat((old_lines).ToString(), string.Concat(" lines   After: ", string.Concat((new_lines).ToString(), string.Concat(" lines   Delta: ", string.Concat((delta).ToString(), string.Concat("\n", string.Concat("  File has been modified. Use peek to inspect.\n", "-- End diff --")))))))))));
    }

    public static string unchanged_report(string file, long lines)
    {
        return string.Concat("-- Diff: ", string.Concat(file, string.Concat(" --\n", string.Concat("  Before: ", string.Concat((lines).ToString(), string.Concat(" lines   After: ", string.Concat((lines).ToString(), string.Concat(" lines\n", string.Concat("  No changes.\n", "-- End diff --")))))))));
    }

    public static object do_diff(string file)
    {
        ((Func<string, object>)((snap) => (File.Exists(snap) ? (File.Exists(file) ? ((Func<string, object>)((old_content) => ((Func<string, object>)((new_content) => ((Func<long, object>)((old_lines) => ((Func<long, object>)((new_lines) => ((Func<long, object>)((delta) => ((old_content == new_content) ? Console.WriteLine(unchanged_report(file, old_lines)) : Console.WriteLine(diff_report(file, old_lines, new_lines, delta)))))((new_lines - old_lines))))(count_lines(new_content))))(count_lines(old_content))))(File.ReadAllText(file))))(File.ReadAllText(snap)) : Console.WriteLine(string.Concat("  File not found: ", file))) : Console.WriteLine(string.Concat("  No snapshot found for ", string.Concat(file, ". Run 'snap' first."))))))(snap_path(file));
        return null;
    }

    public static object main()
    {
        ((Func<object>)(() => {
                ((Func<List<string>, object>)((args) => ((Func<long, object>)((argc) => ((argc < 2L) ? Console.WriteLine("Usage: codex-sdiff <snap|diff|restore> <file>") : ((Func<string, object>)((action) => ((action == "snap") ? ((argc < 3L) ? Console.WriteLine("Usage: codex-sdiff snap <file>") : do_snap(args[(int)2L])) : ((action == "diff") ? ((argc < 3L) ? Console.WriteLine("Usage: codex-sdiff diff <file>") : do_diff(args[(int)2L])) : ((action == "restore") ? ((argc < 3L) ? Console.WriteLine("Usage: codex-sdiff restore <file>") : do_restore(args[(int)2L])) : ((action == "clean") ? Console.WriteLine("  clean: manually delete .snap files (needs delete-file builtin)") : Console.WriteLine(string.Concat("Unknown action: ", string.Concat(action, ". Use snap, diff, or restore.")))))))))(args[(int)1L]))))(((long)args.Count))))(new List<string>(Environment.GetCommandLineArgs()));
                return null;
            }))();
        return null;
    }

}
