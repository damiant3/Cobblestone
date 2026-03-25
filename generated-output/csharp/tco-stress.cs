using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_tco_stress.main());

public static class Codex_tco_stress
{
    public static long sum_to(long n, long acc)
    {
        while (true)
        {
            if ((n == 0L))
            {
                return acc;
            }
            else
            {
                var _tco_0 = (n - 1L);
                var _tco_1 = (acc + n);
                n = _tco_0;
                acc = _tco_1;
                continue;
            }
        }
    }

    public static long main()
    {
        return sum_to(1000000L, 0L);
    }

}
