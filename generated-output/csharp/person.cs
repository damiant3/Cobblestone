using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_person.main());

public sealed record Person(string name, long age);

public static class Codex_person
{
    public static string greet(Person p)
    {
        return string.Concat("Hello, ", p.name, "!");
    }

    public static string main()
    {
        return greet(new Person("Alice", 30L));
    }

}
