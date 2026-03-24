using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

Console.WriteLine(Codex_mini_bootstrap.main());

public abstract record Color;

public sealed record Red : Color;
public sealed record Green : Color;
public sealed record Blue(long Field0) : Color;

public sealed record Point(long x, long y);

public static class Codex_mini_bootstrap
{
    public static string show_color(Color c)
    {
        return ((Func<Color, string>)((_scrutinee0_) => (_scrutinee0_ is Red _mRed0_ ? "red" : (_scrutinee0_ is Green _mGreen0_ ? "green" : (_scrutinee0_ is Blue _mBlue0_ ? ((Func<long, string>)((n) => "blue"))((long)_mBlue0_.Field0) : throw new InvalidOperationException("Non-exhaustive match"))))))(c);
    }

    public static long get_x(Point p)
    {
        return p.x;
    }

    public static Point add_points(Point a, Point b)
    {
        return new Point((a.x + b.x), (a.y + b.y));
    }

    public static List<T3> map_list<T2, T3>(Func<T2, T3> f, List<T2> xs)
    {
        return map_list_loop(f, xs, 0L, ((long)xs.Count), new List<T3>());
    }

    public static List<T5> map_list_loop<T4, T5>(Func<T4, T5> f, List<T4> xs, long i, long len, List<T5> acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return acc;
            }
            else
            {
                var _tco_0 = f;
                var _tco_1 = xs;
                var _tco_2 = (i + 1L);
                var _tco_3 = len;
                var _tco_4 = Enumerable.Concat(acc, new List<T5>() { f(xs[(int)i]) }).ToList();
                f = _tco_0;
                xs = _tco_1;
                i = _tco_2;
                len = _tco_3;
                acc = _tco_4;
                continue;
            }
        }
    }

    public static List<long> use_map(List<Point> pts)
    {
        return map_list(new Func<Point, long>(get_x), pts);
    }

    public static List<long> main()
    {
        return use_map(new List<Point>() { new Point(1L, 2L) });
    }

}
