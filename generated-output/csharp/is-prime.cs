using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_is_prime.main());

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
            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)0;
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
        return _fromUni.TryGetValue((int)u, out int c) ? c : 0;
    }
    public static long CceToUni(long b) {
        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;
    }
}

public static class Codex_is_prime
{
    public static bool is_prime(long n)
    {
        return ((n < 2L) ? false : check_divisor(n, 2L));
    }

    public static bool check_divisor(long n, long d)
    {
        while (true)
        {
            if (((d * d) > n))
            {
                return true;
            }
            else
            {
                if ((((n / d) * d) == n))
                {
                    return false;
                }
                else
                {
                    var _tco_0 = n;
                    var _tco_1 = (d + 1L);
                    n = _tco_0;
                    d = _tco_1;
                    continue;
                }
            }
        }
    }

    public static string main()
    {
        return ((Func<bool, string>)((a) => ((Func<bool, string>)((b) => ((Func<bool, string>)((c) => ((Func<bool, string>)((d) => string.Concat(_Cce.FromUnicode(Convert.ToString(a)), "\u0002", _Cce.FromUnicode(Convert.ToString(b)), "\u0002", _Cce.FromUnicode(Convert.ToString(c)), "\u0002", _Cce.FromUnicode(Convert.ToString(d)))))(is_prime(97L))))(is_prime(49L))))(is_prime(17L))))(is_prime(2L));
    }

}
