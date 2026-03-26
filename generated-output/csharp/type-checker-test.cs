using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_type_checker_test.main());

public static class Codex_type_checker_test
{
    public static long add_one(long x)
    {
        return (x + 1L);
    }

    public static long @double(long x)
    {
        return (x * 2L);
    }

    public static string greet(string name)
    {
        return string.Concat("Hello, ", name, "!");
    }

    public static bool is_positive(long x)
    {
        return (x > 0L);
    }

    public static long apply_twice(Func<long, long> f, long x)
    {
        return f(f(x));
    }

    public static long main()
    {
        return apply_twice(new Func<long, long>(add_one), 40L);
    }

}
