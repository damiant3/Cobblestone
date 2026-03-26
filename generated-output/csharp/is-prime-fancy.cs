using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_is_prime_fancy.main());

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
                    return Enumerable.Concat(new List<string>() { Convert.ToString(x) }, collect_primes(xs, (i + 1L), len)).ToList();
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
        return ((i == len) ? "" : ((i == (len - 1L)) ? xs[(int)i] : string.Concat(xs[(int)i], " ", join_with_spaces(xs, (i + 1L), len))));
    }

    public static string main()
    {
        return ((Func<List<long>, string>)((candidates) => ((Func<List<string>, string>)((primes) => string.Concat("Primes up to 50: ", join_with_spaces(primes, 0L, ((long)primes.Count)))))(collect_primes(candidates, 0L, ((long)candidates.Count)))))(range(2L, 50L));
    }

}
