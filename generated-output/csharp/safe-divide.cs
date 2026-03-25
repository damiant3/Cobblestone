using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_safe_divide.main());

public abstract record Result;

public sealed record Success(T2 Field0) : Result;
public sealed record Failure(string Field0) : Result;

public static class Codex_safe_divide
{
    public static Result safe_divide(long x, long y)
    {
        return ((y == 0L) ? new Failure("division by zero") : new Success((x / y)));
    }

    public static string describe(Result result)
    {
        return ((Func<Result, string>)((_scrutinee0_) => (_scrutinee0_ is Success _mSuccess0_ ? ((Func<long, string>)((n) => string.Concat("got ", Convert.ToString(n))))((long)_mSuccess0_.Field0) : (_scrutinee0_ is Failure _mFailure0_ ? ((Func<string, string>)((msg) => string.Concat("error: ", msg)))((string)_mFailure0_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(result);
    }

    public static string main()
    {
        return describe(safe_divide(42L, 7L));
    }

}
