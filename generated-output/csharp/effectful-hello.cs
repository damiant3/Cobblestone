using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_effectful_hello.main();

public static class Codex_effectful_hello
{
    public static object main()
    {
        ((Func<object>)(() => {
                Console.WriteLine("What is your name?");
                var name = (Console.ReadLine() ?? "");
                Console.WriteLine(string.Concat("Hello, ", string.Concat(name, "!")));
                return null;
            }))();
        return null;
    }

}
