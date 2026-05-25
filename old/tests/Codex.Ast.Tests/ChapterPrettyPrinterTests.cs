using Codex.Ast;
using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Ast.Tests;

// Round-trip tests for ChapterPrettyPrinter (wishlist section G/1). The
// pretty-printer emits post-desugar Chapter back into Codex source. The
// round-trip invariant: parse → desugar → print → parse → desugar must
// reach a fixed point. Divergence indicates a shape the printer drops or
// a surface form the parser won't accept back.
//
// We compare by re-printing both chapters rather than by AST equality —
// AST records include Spans that differ between parse runs, and the
// printer is itself the canonical normalizer.
public class ChapterPrettyPrinterTests
{
    static Chapter ParseAndDesugar(string source, string chapterName = "Test")
    {
        SourceText src = new($"{chapterName}.codex", source);
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Assert.False(bag.HasErrors,
            $"parse errors in input: {string.Join("; ", bag.ToImmutable())}");
        Desugarer des = new(bag);
        Chapter chapter = des.Desugar(doc, chapterName);
        Assert.False(bag.HasErrors,
            $"desugar errors in input: {string.Join("; ", bag.ToImmutable())}");
        return chapter;
    }

    static void AssertRoundTripFixedPoint(string source, string chapterName = "Test")
    {
        Chapter round1 = ParseAndDesugar(source, chapterName);
        string printed1 = ChapterPrettyPrinter.Print(round1);

        Chapter round2 = ParseAndDesugar(printed1, chapterName);
        string printed2 = ChapterPrettyPrinter.Print(round2);

        Assert.Equal(printed1, printed2);
    }

    [Fact]
    public void Simple_definition_roundtrips()
    {
        AssertRoundTripFixedPoint("x = 42");
    }

    [Fact]
    public void Binary_expression_roundtrips()
    {
        AssertRoundTripFixedPoint("x = 1 + 2 * 3");
    }

    [Fact]
    public void Typed_definition_roundtrips()
    {
        AssertRoundTripFixedPoint("x : Integer\nx = 42");
    }

    [Fact]
    public void Function_with_params_roundtrips()
    {
        AssertRoundTripFixedPoint("add (a) (b) = a + b");
    }

    [Fact]
    public void If_expression_roundtrips()
    {
        AssertRoundTripFixedPoint("f (x) = if x > 0 then 1 else 0");
    }

    [Fact]
    public void Let_expression_roundtrips()
    {
        AssertRoundTripFixedPoint("f = let x = 1 in x + 2");
    }

    [Fact]
    public void Lambda_expression_roundtrips()
    {
        AssertRoundTripFixedPoint("f = \\x -> x + 1");
    }

    [Fact]
    public void List_expression_roundtrips()
    {
        AssertRoundTripFixedPoint("xs = [1, 2, 3]");
    }

    [Fact]
    public void Record_type_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "Point = record {\n  x : Integer,\n  y : Integer\n}\norigin = Point { x = 0, y = 0 }");
    }

    [Fact]
    public void Variant_type_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "Shape =\n  | Circle (Integer)\n  | Square (Integer)");
    }

    [Fact]
    public void Match_expression_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "area (s) =\n  when s\n    is Circle (r) -> r\n    is Square (w) -> w");
    }

    [Fact]
    public void Act_block_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "opening =\n  act\n    x <- readLine\n    print x\n  end");
    }

    [Fact]
    public void Field_access_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "Point = record {\n  x : Integer,\n  y : Integer\n}\nprojX (p) = p.x");
    }

    [Fact]
    public void Function_type_roundtrips()
    {
        AssertRoundTripFixedPoint("apply : (Integer -> Integer) -> Integer -> Integer\napply (f) (x) = f x");
    }

    [Fact]
    public void List_type_roundtrips()
    {
        AssertRoundTripFixedPoint("nil : List Integer\nnil = []");
    }

    [Fact]
    public void String_literal_roundtrips()
    {
        AssertRoundTripFixedPoint("greeting = \"hello\"");
    }

    [Fact]
    public void Prose_chapter_roundtrips()
    {
        AssertRoundTripFixedPoint(
            "Chapter: Test\n\n A simple chapter.\n\nSection: Basics\n\n  x = 42\n",
            "Test");
    }

    [Fact]
    public void Cons_associates_right()
    {
        AssertRoundTripFixedPoint("xs = 1 :: 2 :: 3 :: []");
    }

    [Fact]
    public void Nested_let_roundtrips()
    {
        AssertRoundTripFixedPoint("f = let x = 1 in let y = 2 in x + y");
    }

    [Fact]
    public void Negation_roundtrips()
    {
        AssertRoundTripFixedPoint("x = -5");
    }

    [Fact]
    public void Lambda_body_then_next_def_roundtrips()
    {
        AssertRoundTripFixedPoint("f = \\x -> let y = 1 in [2]\ng = 5");
    }

    [Fact]
    public void Indented_lambda_body_then_next_def()
    {
        AssertRoundTripFixedPoint("f = \\x -> let y = [1, 2, 3] in let z = 4 in [-z, y]\ng = 5");
    }

    // Exercise the printer against real self-host sources. These catch
    // surface-syntax corners the hand-written unit cases miss.
    [Theory]
    [InlineData("Codex.Codex/Core/Name.codex", "Name")]
    [InlineData("Codex.Codex/Core/Severity.codex", "Severity")]
    [InlineData("Codex.Codex/Core/Phase.codex", "Phase")]
    [InlineData("Codex.Codex/Core/Collections.codex", "Collections")]
    [InlineData("Codex.Codex/Core/SourceText.codex", "SourceText")]
    [InlineData("Codex.Codex/Core/Diagnostic.codex", "Diagnostic")]
    [InlineData("Codex.Codex/Ast/AstNodes.codex", "AstNodes")]
    [InlineData("Codex.Codex/IR/IRChapter.codex", "IRChapter")]
    public void SelfHost_file_roundtrips(string relativePath, string chapterName)
    {
        string repoRoot = FindRepoRoot();
        string fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string source = File.ReadAllText(fullPath);
        AssertRoundTripFixedPoint(source, chapterName);
    }

    static string FindRepoRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Codex.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Could not find Codex.sln");
    }

    // Property test (wishlist H): for randomly-generated ASTs, the fixed
    // point `print → parse → desugar → print` must converge. Seeds are
    // fixed so failures reproduce; a new seed locking a found regression
    // joins the InlineData list.
    [Theory]
    [InlineData(0x1111)]
    [InlineData(0x2222)]
    [InlineData(0x3333)]
    [InlineData(0x4444)]
    [InlineData(0x5555)]
    [InlineData(0xAAAA)]
    [InlineData(0xBBBB)]
    [InlineData(0xCAFE)]
    public void Random_AST_roundtrips(int seed)
    {
        Random rng = new(seed);
        string source = BuildRandomSource(rng, defCount: 8, maxDepth: 4);
        try
        {
            Chapter round1 = ParseAndDesugar(source, "PropTest");
            string printed1 = ChapterPrettyPrinter.Print(round1);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"proptest-seed-{seed}-printed.codex"),
                printed1);
            Chapter round2 = ParseAndDesugar(printed1, "PropTest");
            string printed2 = ChapterPrettyPrinter.Print(round2);
            Assert.Equal(printed1, printed2);
        }
        catch (Exception)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"proptest-seed-{seed}-source.codex"),
                source);
            throw;
        }
    }

    static string BuildRandomSource(Random rng, int defCount, int maxDepth)
    {
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < defCount; i++)
        {
            sb.Append($"f{i} = ");
            sb.AppendLine(BuildRandomExpr(rng, maxDepth));
        }
        return sb.ToString();
    }

    static string BuildRandomExpr(Random rng, int depth)
    {
        if (depth <= 0)
        {
            return BuildLeaf(rng);
        }
        int choice = rng.Next(10);
        return choice switch
        {
            0 or 1 => BuildLeaf(rng),
            2 => $"({BuildRandomExpr(rng, depth - 1)} {PickBinOp(rng)} {BuildRandomExpr(rng, depth - 1)})",
            3 => $"if {BuildRandomExpr(rng, depth - 1)} then {BuildRandomExpr(rng, depth - 1)} else {BuildRandomExpr(rng, depth - 1)}",
            4 => $"let {RandomVar(rng)} = {BuildRandomExpr(rng, depth - 1)} in {BuildRandomExpr(rng, depth - 1)}",
            5 => $"(\\{RandomVar(rng)} -> {BuildRandomExpr(rng, depth - 1)})",
            6 => $"[{BuildRandomExpr(rng, depth - 1)}, {BuildRandomExpr(rng, depth - 1)}]",
            7 => $"(-{BuildLeafForNegation(rng)})",
            _ => BuildLeaf(rng)
        };
    }

    static string BuildLeaf(Random rng) => rng.Next(5) switch
    {
        0 => rng.Next(1000).ToString(),
        1 => "True",
        2 => "False",
        3 => $"\"str{rng.Next(100)}\"",
        _ => RandomVar(rng)
    };

    static string BuildLeafForNegation(Random rng) => rng.Next(2) switch
    {
        0 => rng.Next(1, 1000).ToString(),
        _ => RandomVar(rng)
    };

    static string RandomVar(Random rng)
    {
        // Lowercase identifier prefixed `v` so the name can't accidentally
        // collide with a reserved keyword (in, if, let, end, act, is, ...).
        const string alpha = "abcdefghijklmnopqrstuvwxyz";
        int len = rng.Next(2, 5);
        char[] buf = new char[len];
        for (int i = 0; i < len; i++) buf[i] = alpha[rng.Next(alpha.Length)];
        return "v" + new string(buf);
    }

    static string PickBinOp(Random rng) => rng.Next(6) switch
    {
        0 => "+",
        1 => "-",
        2 => "*",
        3 => "<",
        4 => "==",
        _ => "&"
    };
}
