using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Xunit;

namespace Codex.Ast.Tests;

public class DesugarerTests
{
    private static Chapter ParseAndDesugar(string source, string chapterName = "Test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        return desugarer.Desugar(doc, chapterName);
    }

    [Fact]
    public void Desugar_simple_definition()
    {
        Chapter chapter = ParseAndDesugar("x = 42");
        Assert.Single(chapter.Definitions);
        Assert.Equal("x", chapter.Definitions[0].Name.Value);
        Assert.IsType<LiteralExpr>(chapter.Definitions[0].Body);
    }

    [Fact]
    public void Desugar_preserves_module_name()
    {
        Chapter chapter = ParseAndDesugar("x = 1", "MyModule");
        Assert.Equal("MyModule", chapter.Name.Leaf.Value);
    }

    [Fact]
    public void Desugar_binary_expression()
    {
        Chapter chapter = ParseAndDesugar("x = 1 + 2");
        Assert.IsType<BinaryExpr>(chapter.Definitions[0].Body);
        BinaryExpr bin = (BinaryExpr)chapter.Definitions[0].Body;
        Assert.Equal(BinaryOp.Add, bin.Op);
    }

    [Fact]
    public void Desugar_if_expression()
    {
        Chapter chapter = ParseAndDesugar("x = if True then 1 else 0");
        Assert.IsType<IfExpr>(chapter.Definitions[0].Body);
    }

    [Fact]
    public void Desugar_let_expression()
    {
        Chapter chapter = ParseAndDesugar("x = let a = 1 in a + 2");
        Assert.IsType<LetExpr>(chapter.Definitions[0].Body);
        LetExpr letExpr = (LetExpr)chapter.Definitions[0].Body;
        Assert.Single(letExpr.Bindings);
        Assert.Equal("a", letExpr.Bindings[0].Name.Value);
    }

    [Fact]
    public void Desugar_with_type_annotation()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        Chapter chapter = ParseAndDesugar(source);
        Assert.Single(chapter.Definitions);
        Assert.NotNull(chapter.Definitions[0].DeclaredType);
        Assert.IsType<FunctionTypeExpr>(chapter.Definitions[0].DeclaredType);
    }

    [Fact]
    public void Desugar_list_expression()
    {
        Chapter chapter = ParseAndDesugar("xs = [1, 2, 3]");
        Assert.IsType<ListExpr>(chapter.Definitions[0].Body);
        ListExpr list = (ListExpr)chapter.Definitions[0].Body;
        Assert.Equal(3, list.Elements.Count);
    }

    [Fact]
    public void Desugar_function_application()
    {
        Chapter chapter = ParseAndDesugar("x = f 1");
        Assert.IsType<ApplyExpr>(chapter.Definitions[0].Body);
    }

    [Fact]
    public void Desugar_multiple_definitions()
    {
        Chapter chapter = ParseAndDesugar("a = 1\nb = 2\nc = 3");
        Assert.Equal(3, chapter.Definitions.Count);
    }

    [Fact]
    public void Desugar_match_expression()
    {
        string source = "x = when y is True -> 1 is False -> 0";
        Chapter chapter = ParseAndDesugar(source);
        Assert.IsType<MatchExpr>(chapter.Definitions[0].Body);
        MatchExpr match = (MatchExpr)chapter.Definitions[0].Body;
        Assert.Equal(2, match.Branches.Count);
    }

    [Fact]
    public void Desugar_record_expression()
    {
        Chapter chapter = ParseAndDesugar("p = Point { x = 1, y = 2 }");
        Assert.IsType<RecordExpr>(chapter.Definitions[0].Body);
    }

    [Fact]
    public void Desugar_interpolated_string_to_append_chain()
    {
        Chapter chapter = ParseAndDesugar("x = \"hello #{name}!\"");
        Expr body = chapter.Definitions[0].Body;
        Assert.IsType<BinaryExpr>(body);
        BinaryExpr outer = (BinaryExpr)body;
        Assert.Equal(BinaryOp.Append, outer.Op);

        Assert.IsType<BinaryExpr>(outer.Left);
        BinaryExpr inner = (BinaryExpr)outer.Left;
        Assert.Equal(BinaryOp.Append, inner.Op);

        Assert.IsType<LiteralExpr>(inner.Left);
        LiteralExpr hello = (LiteralExpr)inner.Left;
        Assert.Equal("hello ", hello.Value);

        Assert.IsType<ApplyExpr>(inner.Right);
        ApplyExpr showCall = (ApplyExpr)inner.Right;
        Assert.IsType<NameExpr>(showCall.Function);
        Assert.Equal("show", ((NameExpr)showCall.Function).Name.Value);
        Assert.IsType<NameExpr>(showCall.Argument);
        Assert.Equal("name", ((NameExpr)showCall.Argument).Name.Value);

        Assert.IsType<LiteralExpr>(outer.Right);
        LiteralExpr excl = (LiteralExpr)outer.Right;
        Assert.Equal("!", excl.Value);
    }

    [Fact]
    public void Desugar_interpolated_string_single_text_to_literal()
    {
        Chapter chapter = ParseAndDesugar("x = \"just text\"");
        Expr body = chapter.Definitions[0].Body;
        Assert.IsType<LiteralExpr>(body);
        LiteralExpr lit = (LiteralExpr)body;
        Assert.Equal("just text", lit.Value);
    }

    [Fact]
    public void Desugar_interpolated_string_single_expr()
    {
        Chapter chapter = ParseAndDesugar("x = \"#{name}\"");
        Expr body = chapter.Definitions[0].Body;
        Assert.IsType<ApplyExpr>(body);
        ApplyExpr showCall = (ApplyExpr)body;
        Assert.IsType<NameExpr>(showCall.Function);
        Assert.Equal("show", ((NameExpr)showCall.Function).Name.Value);
        Assert.IsType<NameExpr>(showCall.Argument);
        Assert.Equal("name", ((NameExpr)showCall.Argument).Name.Value);
    }

    [Fact]
    public void Desugar_interpolated_string_empty()
    {
        Chapter chapter = ParseAndDesugar("x = \"\"");
        Expr body = chapter.Definitions[0].Body;
        Assert.IsType<LiteralExpr>(body);
        LiteralExpr lit = (LiteralExpr)body;
        Assert.Equal("", lit.Value);
    }

    [Fact]
    public void Desugar_interpolated_integer_expr_wraps_in_show()
    {
        Chapter chapter = ParseAndDesugar("x = \"value: #{42}\"");
        Expr body = chapter.Definitions[0].Body;
        Assert.IsType<BinaryExpr>(body);
        BinaryExpr bin = (BinaryExpr)body;
        Assert.Equal(BinaryOp.Append, bin.Op);

        Assert.IsType<LiteralExpr>(bin.Left);
        Assert.Equal("value: ", ((LiteralExpr)bin.Left).Value);

        Assert.IsType<ApplyExpr>(bin.Right);
        ApplyExpr showCall = (ApplyExpr)bin.Right;
        Assert.IsType<NameExpr>(showCall.Function);
        Assert.Equal("show", ((NameExpr)showCall.Function).Name.Value);
        Assert.IsType<LiteralExpr>(showCall.Argument);
    }
}
