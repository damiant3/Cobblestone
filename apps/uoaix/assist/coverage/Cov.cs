// The probes the coverage instrument puts into a copy of the Assist's sources (cover-assist.ps1). Compiled only into
// that copy, never into the relay.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist.Coverage;

public static class Cov
{
    static int[] s_hits = new int[0];

    public static void Size(int probes)
    {
        s_hits = new int[probes];
    }

    public static int Hits(int id) => Volatile.Read(ref s_hits[id]);

    public static void Hit(int id)
    {
        Interlocked.Increment(ref s_hits[id]);
    }

    // A value passed through, its arm counted.
    public static T Pass<T>(int id, T value)
    {
        Hit(id);
        return value;
    }

    // A condition passed through, counted at whenTrue or whenFalse.
    public static bool Cond(int whenTrue, int whenFalse, bool value)
    {
        Hit(value ? whenTrue : whenFalse);
        return value;
    }

    // Counted, and true: the arms of a condition that declares a variable, (c ? (Mark(t) || true) : (Mark(f) && false)),
    // where the literals keep the compiler's definite assignment of what c declares.
    public static bool Mark(int id)
    {
        Hit(id);
        return true;
    }

    // A throw expression's arm counted, then thrown.
    public static T Throw<T>(int id, Exception e)
    {
        Hit(id);
        throw e;
    }

    // A foreach's source, counted at empty or at someItems once the loop has asked for its first element.
    public static IEnumerable<T> Each<T>(int empty, int someItems, IEnumerable<T> source)
    {
        bool any = false;
        foreach (T item in source)
        {
            if (!any)
            {
                any = true;
                Hit(someItems);
            }
            yield return item;
        }
        if (!any)
            Hit(empty);
    }

    public static System.Collections.IEnumerable Each(int empty, int someItems, System.Collections.IEnumerable source)
    {
        bool any = false;
        foreach (object? item in source)
        {
            if (!any)
            {
                any = true;
                Hit(someItems);
            }
            yield return item;
        }
        if (!any)
            Hit(empty);
    }
}
