using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_prose_greeting.main());

public static class Codex_prose_greeting
{
    public static string greet(string name)
    {
        return string.Concat("Hello, ", string.Concat(name, "!"));
    }

    public static string main()
    {
        return greet("World");
    }

}
