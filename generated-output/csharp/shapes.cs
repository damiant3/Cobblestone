using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_shapes.main());

public abstract record Shape;

public sealed record Circle(decimal Field0) : Shape;
public sealed record Rectangle(decimal Field0, decimal Field1) : Shape;

public static class Codex_shapes
{
    public static decimal main()
    {
        return ((Func<Shape, decimal>)((_scrutinee0_) => (_scrutinee0_ is Circle _mCircle0_ ? ((Func<decimal, decimal>)((r) => ((3.14m * r) * r)))((decimal)_mCircle0_.Field0) : (_scrutinee0_ is Rectangle _mRectangle0_ ? ((Func<decimal, decimal>)((h) => ((Func<decimal, decimal>)((w) => (w * h)))((decimal)_mRectangle0_.Field0)))((decimal)_mRectangle0_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))(new Circle(5.0m));
    }

}
