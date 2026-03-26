using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_is_prime_fancy.main());

static class _Cce {
    static readonly int[] _toUni = {
        0, 10, 32,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
        118, 107, 106, 120, 113, 122,
        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
        86, 75, 74, 88, 81, 90,
        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
        43, 61, 42, 60, 62,
        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,
        244, 246, 250, 249, 251, 252, 241, 231, 237,
        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,
        1074, 1083, 1082, 1084, 1076, 1087, 1091
    };
    static readonly Dictionary<int, int> _fromUni = new();
    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }
    public static string FromUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int u = s[i];
            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)68;
        }
        return new string(cs);
    }
    public static string ToUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int b = s[i];
            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\uFFFD';
        }
        return new string(cs);
    }
    public static long UniToCce(long u) {
        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;
    }
    public static long CceToUni(long b) {
        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;
    }
}

public static class Codex_is_prime_fancy
{
    public static List<long> range(long lo, long hi)
    {
        return ((lo > hi) ? new List<long>() : Enumerable.Concat(new List<long>() { lo }, range((lo + 1L), hi)).ToList());
    }

    public static bool divides(long d, long n)
    {
        return (((n / d) * d) == n);
    }

    public static bool has_no_divisors_in(List<long> divisors, long n, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return true;
            }
            else
            {
                var d = divisors[(int)i];
                if (divides(d, n))
                {
                    return false;
                }
                else
                {
                    var _tco_0 = divisors;
                    var _tco_1 = n;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    divisors = _tco_0;
                    n = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static List<long> small_divisors(List<long> xs, long limit, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new List<long>();
            }
            else
            {
                var x = xs[(int)i];
                if (((x * x) <= limit))
                {
                    return Enumerable.Concat(new List<long>() { x }, small_divisors(xs, limit, (i + 1L), len)).ToList();
                }
                else
                {
                    var _tco_0 = xs;
                    var _tco_1 = limit;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    xs = _tco_0;
                    limit = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static bool is_prime(long n)
    {
        return ((n < 2L) ? false : ((Func<List<long>, bool>)((candidates) => ((Func<List<long>, bool>)((small) => has_no_divisors_in(small, n, 0L, ((long)small.Count))))(small_divisors(candidates, n, 0L, ((long)candidates.Count)))))(range(2L, n)));
    }

    public static List<string> collect_primes(List<long> xs, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new List<string>();
            }
            else
            {
                var x = xs[(int)i];
                if (is_prime(x))
                {
                    return Enumerable.Concat(new List<string>() { _Cce.FromUnicode(Convert.ToString(x)) }, collect_primes(xs, (i + 1L), len)).ToList();
                }
                else
                {
                    var _tco_0 = xs;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    xs = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    continue;
                }
            }
        }
    }

    public static string join_with_spaces(List<string> xs, long i, long len)
    {
        return ((i == len) ? "" : ((i == (len - 1L)) ? xs[(int)i] : string.Concat(xs[(int)i], "\u0002", join_with_spaces(xs, (i + 1L), len))));
    }

    public static string main()
    {
        return ((Func<List<long>, string>)((candidates) => ((Func<List<string>, string>)((primes) => string.Concat("9\u0015\u0011\u001A\u000D\u0013\u0002\u0019\u001F\u0002\u000E\u0010\u0002\u0008\u0003E\u0002", join_with_spaces(primes, 0L, ((long)primes.Count)))))(collect_primes(candidates, 0L, ((long)candidates.Count)))))(range(2L, 50L));
    }

}
