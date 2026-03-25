using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_greeting.main());

public static class Codex_greeting
{
    public static string greeting(string name)
    {
        return string.Concat("Hello, ", string.Concat(name, "!"));
    }

    public static string main()
    {
        return greeting("World");
    }

}
