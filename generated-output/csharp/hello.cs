using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_hello.main());

public static class Codex_hello
{
    public static long square(long x)
    {
        return (x * x);
    }

    public static long @double(long x)
    {
        return (x + x);
    }

    public static long main()
    {
        return square(5L);
    }

}
