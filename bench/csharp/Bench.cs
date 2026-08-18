// Nine benchmark functions, one per bench/c file, same signatures.
// Run: Bench <name>; the JIT listing of that one method is taken with
// DOTNET_JitDisasm=<Method> DOTNET_TieredCompilation=0 (FullOpts).
using System;
using System.Runtime.CompilerServices;

static class Bench
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Fib(int n) { if (n <= 1) return n; return Fib(n - 1) + Fib(n - 2); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Fact(int n) { if (n == 0) return 1; return (long)n * Fact(n - 1); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Gcd(int a, int b) { while (b != 0) { int t = b; b = a % b; a = t; } return a; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Sum(int n) { long s = 0; for (int i = 1; i <= n; i++) s += i; return s; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Ack(int m, int n) { if (m == 0) return n + 1; if (n == 0) return Ack(m - 1, 1); return Ack(m - 1, (int)Ack(m, n - 1)); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Tak(long x, long y, long z) { if (y >= x) return z; return Tak(Tak(x - 1, y, z), Tak(y - 1, z, x), Tak(z - 1, x, y)); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Collatz(long n) { long steps = 0; while (n != 1) { if (n % 2 == 0) n = n / 2; else n = 3 * n + 1; steps++; } return steps; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Compute(int n) { long acc = 0; for (int i = n; i > 0; i--) { long a = i + 1, b = i * 2, c = i - 3; long d = a * b, e = b + c, f = c * a; acc += d + e + f; } return acc; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Regright(int n) { if (n <= 0) return 1; return Regright(n - 1) * n - n; }

    static int Main(string[] args)
    {
        string name = args.Length > 0 ? args[0] : "fib";
        switch (name)
        {
            case "fib": Console.WriteLine(Fib(35)); break;
            case "fact": Console.WriteLine(Fact(20)); break;
            case "gcd": Console.WriteLine(Gcd(46368, 28657)); break;
            case "sum": Console.WriteLine(Sum(1000000)); break;
            case "ack": Console.WriteLine(Ack(3, 9)); break;
            case "tak": Console.WriteLine(Tak(24, 16, 8)); break;
            case "collatz": Console.WriteLine(Collatz(837799)); break;
            case "locals": Console.WriteLine(Compute(1000)); break;
            case "regright": Console.WriteLine(Regright(12)); break;
            default: Console.Error.WriteLine("unknown bench " + name); return 2;
        }
        return 0;
    }
}