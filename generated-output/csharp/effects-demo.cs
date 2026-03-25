using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_effects_demo.main();

public static class Codex_effects_demo
{
    public static object greet(string name)
    {
        Console.WriteLine(string.Concat("Hello, ", string.Concat(name, "!")));
        return null;
    }

    public static object main()
    {
        ((Func<object>)(() => {
                greet("Alice");
                greet("Bob");
                Console.WriteLine("Done!");
                return null;
            }))();
        return null;
    }

}
