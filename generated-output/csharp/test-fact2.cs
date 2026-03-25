using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_test_fact2.main());

public static class Codex_test_fact2
{
    public static long fact(long n)
    {
        return ((n == 0L) ? 1L : (n * fact((n - 1L))));
    }

    public static long main()
    {
        return fact(2L);
    }

}
