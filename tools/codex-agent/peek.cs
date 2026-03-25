using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_peek.main();

public static class Codex_peek
{
    public static long parse_int_or(string s, long default_val)
    {
        return ((((long)s.Length) == 0L) ? default_val : long.Parse(s));
    }

    public static string format_line(long num, string line)
    {
        return string.Concat((num).ToString(), string.Concat("  ", line));
    }

    public static string format_lines_loop(List<string> lines, long current, long end_line)
    {
        return ((current > end_line) ? "" : ((current > ((long)lines.Count)) ? "" : ((Func<string, string>)((line) => ((Func<string, string>)((formatted) => ((Func<string, string>)((rest) => ((((long)rest.Length) == 0L) ? formatted : string.Concat(formatted, string.Concat("\n", rest)))))(format_lines_loop(lines, (current + 1L), end_line))))(format_line(current, line))))(lines[(int)(current - 1L)])));
    }

    public static string peek_output(string file, string content, long start, long end_line)
    {
        return ((Func<List<string>, string>)((lines) => ((Func<long, string>)((total) => ((Func<long, string>)((actual_end) => ((Func<string, string>)((header) => string.Concat(header, string.Concat("\n", format_lines_loop(lines, start, actual_end)))))(string.Concat("--- ", string.Concat(file, string.Concat(" lines ", string.Concat((start).ToString(), string.Concat("..", string.Concat((actual_end).ToString(), string.Concat(" of ", string.Concat((total).ToString(), " ---")))))))))))(((end_line == 0L) ? total : end_line))))(((long)lines.Count))))(new List<string>(content.Split("\n")));
    }

    public static object main()
    {
        ((Func<object>)(() => {
                ((Func<List<string>, object>)((args) => ((Func<long, object>)((argc) => ((argc < 2L) ? Console.WriteLine("Usage: codex-peek <file> [start] [end]") : ((Func<string, object>)((file) => ((Func<long, object>)((raw_start) => ((Func<long, object>)((start) => ((Func<long, object>)((end_line) => (File.Exists(file) ? Console.WriteLine(peek_output(file, File.ReadAllText(file), start, end_line)) : Console.WriteLine(string.Concat("File not found: ", file)))))(((argc > 3L) ? parse_int_or(args[(int)3L], 0L) : 0L))))(((raw_start < 1L) ? 1L : raw_start))))(((argc > 2L) ? parse_int_or(args[(int)2L], 1L) : 1L))))(args[(int)1L]))))(((long)args.Count))))(new List<string>(Environment.GetCommandLineArgs()));
                return null;
            }))();
        return null;
    }

}
