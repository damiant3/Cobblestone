using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_fstat.main();

public static class Codex_fstat
{
    public static string pad_right(string s, long width)
    {
        while (true)
        {
            if ((((long)s.Length) >= width))
            {
                return s;
            }
            else
            {
                var _tco_0 = string.Concat(s, " ");
                var _tco_1 = width;
                s = _tco_0;
                width = _tco_1;
                continue;
            }
        }
    }

    public static string pad_left(string s, long width)
    {
        while (true)
        {
            if ((((long)s.Length) >= width))
            {
                return s;
            }
            else
            {
                var _tco_0 = string.Concat(" ", s);
                var _tco_1 = width;
                s = _tco_0;
                width = _tco_1;
                continue;
            }
        }
    }

    public static long count_lines(string content)
    {
        return ((((long)content.Length) == 0L) ? 0L : ((long)new List<string>(content.Split("\n")).Count));
    }

    public static string size_hint(long lines)
    {
        return ((lines > 300L) ? " !! LARGE" : ((lines > 100L) ? " ~  medium" : ""));
    }

    public static string stat_line(string file)
    {
        return ((Func<string, string>)((content) => ((Func<long, string>)((lines) => ((Func<long, string>)((chars) => string.Concat(pad_right(file, 55L), string.Concat(pad_left((lines).ToString(), 7L), string.Concat(pad_left((chars).ToString(), 9L), size_hint(lines))))))(((long)content.Length))))(count_lines(content))))(File.ReadAllText(file));
    }

    public static string stat_loop(List<string> args, long idx, long total_lines, long total_chars)
    {
        return ((idx >= ((long)args.Count)) ? ((idx > 2L) ? string.Concat("\n", string.Concat(pad_right(string.Concat("TOTAL (", string.Concat(((idx - 1L)).ToString(), " files)")), 55L), string.Concat(pad_left((total_lines).ToString(), 7L), pad_left((total_chars).ToString(), 9L)))) : "") : ((Func<string, string>)((file) => (File.Exists(file) ? ((Func<string, string>)((content) => ((Func<long, string>)((lines) => ((Func<long, string>)((chars) => ((Func<string, string>)((row) => ((Func<string, string>)((rest) => string.Concat(row, string.Concat("\n", rest))))(stat_loop(args, (idx + 1L), (total_lines + lines), (total_chars + chars)))))(string.Concat(pad_right(file, 55L), string.Concat(pad_left((lines).ToString(), 7L), string.Concat(pad_left((chars).ToString(), 9L), size_hint(lines)))))))(((long)content.Length))))(count_lines(content))))(File.ReadAllText(file)) : ((Func<string, string>)((rest) => string.Concat("  (not found: ", string.Concat(file, string.Concat(")\n", rest)))))(stat_loop(args, (idx + 1L), total_lines, total_chars)))))(args[(int)idx]));
    }

    public static object main()
    {
        ((Func<object>)(() => {
                ((Func<List<string>, object>)((args) => ((Func<long, object>)((argc) => ((argc < 2L) ? Console.WriteLine("Usage: codex-fstat <file> [file2] ...") : ((Func<string, object>)((header) => ((Func<string, object>)((sep) => Console.WriteLine(string.Concat(header, string.Concat("\n", string.Concat(sep, string.Concat("\n", stat_loop(args, 1L, 0L, 0L))))))))(string.Concat(pad_right("───────────────────────────────────────────────────────", 55L), string.Concat(" -------", " --------")))))(string.Concat(pad_right("File", 55L), string.Concat(pad_left("Lines", 7L), pad_left("Chars", 9L)))))))(((long)args.Count))))(new List<string>(Environment.GetCommandLineArgs()));
                return null;
            }))();
        return null;
    }

}
