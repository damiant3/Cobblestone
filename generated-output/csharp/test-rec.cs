using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

Console.WriteLine(Codex_test_rec.main());

public static class Codex_test_rec
{
    public static long countdown(long n)
    {
        while (true)
        {
            if ((n == 0L))
            {
                return 0L;
            }
            else
            {
                var _tco_0 = (n - 1L);
                n = _tco_0;
                continue;
            }
        }
    }

    public static long main()
    {
        return countdown(5L);
    }

}
