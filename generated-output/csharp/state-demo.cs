using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_state_demo.main());

public static class Codex_state_demo
{
    public static long main()
    {
        return ((Func<long>)(() => {
                long __state = 0L;
                var x = __state;
                __state = (x + 10L);
                var y = __state;
                __state = (y * 2L);
                return __state;
            }))();
    }

}
