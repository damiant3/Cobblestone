using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_string_ops.main());

public static class Codex_string_ops
{
    public static bool is_identifier_char(long ch)
    {
        return (char.IsLetter((char)ch) ? true : (char.IsDigit((char)ch) ? true : ((((char)ch).ToString() == "-") ? true : false)));
    }

    public static long count_letters(string s, long i, long acc)
    {
        while (true)
        {
            if ((i >= ((long)s.Length)))
            {
                return acc;
            }
            else
            {
                var ch = ((long)s[(int)i]);
                if (char.IsLetter((char)ch))
                {
                    var _tco_0 = s;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = (acc + 1L);
                    s = _tco_0;
                    i = _tco_1;
                    acc = _tco_2;
                    continue;
                }
                else
                {
                    var _tco_0 = s;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = acc;
                    s = _tco_0;
                    i = _tco_1;
                    acc = _tco_2;
                    continue;
                }
            }
        }
    }

    public static long main()
    {
        return count_letters("hello world 123", 0L, 0L);
    }

}
