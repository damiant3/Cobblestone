using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_fibonacci.main());

public static class Codex_fibonacci
{
    public static long fib(long n)
    {
        return ((n == 0L) ? 0L : ((n == 1L) ? 1L : (fib((n - 1L)) + fib((n - 2L)))));
    }

    public static long main()
    {
        return fib(20L);
    }

}
