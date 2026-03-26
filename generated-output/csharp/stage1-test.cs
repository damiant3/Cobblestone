using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_stage1_test.main();

public static class Codex_stage1_test
{
    public static long square(long x)
    {
        return (x * x);
    }

    public static string greet(string name)
    {
        return string.Concat("Hello, ", name, "!");
    }

    public static object main()
    {
        ((Func<object>)(() => {
                Console.WriteLine(greet("Codex"));
                Console.WriteLine(Convert.ToString(square(7L)));
                return null;
            }))();
        return null;
    }

}
