using System;
using System.Collections.Generic;
using System.Linq;

public sealed record Name(string value);

public static class Codex_Name
{
    public static Name make_name(string s)
    {
        return new Name(s);
    }

    public static string name_value(Name n)
    {
        return n.value;
    }

}
