using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_factorial.main());

public static class Codex_factorial
{
    public static long abs(long x)
    {
        return ((x < 0L) ? (-x) : x);
    }

    public static long max(long x, long y)
    {
        return ((x > y) ? x : y);
    }

    public static long factorial(long n)
    {
        return ((n == 0L) ? 1L : (n * factorial((n - 1L))));
    }

    public static long main()
    {
        return factorial(10L);
    }

}
