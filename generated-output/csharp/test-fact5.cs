using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

Console.WriteLine(Codex_test_fact5.main());

public static class Codex_test_fact5
{
    public static long factorial(long n)
    {
        return ((n == 0L) ? 1L : (n * factorial((n - 1L))));
    }

    public static long main()
    {
        return factorial(5L);
    }

}
