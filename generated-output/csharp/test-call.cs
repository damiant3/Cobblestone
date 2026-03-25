using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_test_call.main());

public static class Codex_test_call
{
    public static long add1(long x)
    {
        return (x + 1L);
    }

    public static long main()
    {
        return add1(9L);
    }

}
