global using static CodexRuntime;
using System;
using System.Collections.Generic;
using System.Linq;

public static class CodexRuntime
{
    public static long list_length<T>(List<T> xs) => xs.Count;
    public static Func<long, T> list_at<T>(List<T> xs) => (long i) => xs[(int)i];
    public static void print_line(object x) => Console.WriteLine(x);
    public static string integer_to_text(long n) => n.ToString();
    public static long text_to_integer(string s) => long.Parse(s);
    public static long text_length(string s) => s.Length;
    public static Func<long, string> char_at(string s) => (long i) => s[(int)i].ToString();
    public static long char_code(string s) => (long)s[0];
    public static Func<long, Func<long, string>> substring(string s) =>
        (long start) => (long len) => s.Substring((int)start, (int)len);
    public static bool is_letter(string s) => s.Length > 0 && char.IsLetter(s[0]);
    public static bool is_digit(string s) => s.Length > 0 && char.IsDigit(s[0]);
    public static bool is_whitespace(string s) => s.Length > 0 && char.IsWhiteSpace(s[0]);
    public static Func<string, Func<string, string>> text_replace(string s) =>
        (string old) => (string @new) => s.Replace(old, @new);
    public static string code_to_char(long code) => ((char)code).ToString();
    public static string show<T>(T value) => Convert.ToString(value) ?? "";
}
