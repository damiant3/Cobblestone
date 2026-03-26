using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_is_prime.main());

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
        return ((Func<bool, string>)((a) => ((Func<bool, string>)((b) => ((Func<bool, string>)((c) => ((Func<bool, string>)((d) => string.Concat(Convert.ToString(a), " ", Convert.ToString(b), " ", Convert.ToString(c), " ", Convert.ToString(d))))(is_prime(97L))))(is_prime(49L))))(is_prime(17L))))(is_prime(2L));
    }

}
