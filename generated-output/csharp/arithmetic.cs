using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_arithmetic.main());

public static class Codex_arithmetic
{
    public static long max(long x, long y)
    {
        return ((x > y) ? x : y);
    }

    public static long abs(long x)
    {
        return ((x < 0L) ? (-x) : x);
    }

    public static long clamp(long lo, long hi, long x)
    {
        return ((Func<long, long>)((clamped) => clamped))(((x < lo) ? lo : ((x > hi) ? hi : x)));
    }

    public static long main()
    {
        return clamp(0L, 100L, abs(max((-42L), 37L)));
    }

}
