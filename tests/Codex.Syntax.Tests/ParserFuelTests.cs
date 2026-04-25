using System.Text;
using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

public class ParserFuelTests
{
    static void Parse(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        parser.ParseDocument();
    }

    static DiagnosticBag ParseBag(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        parser.ParseDocument();
        return bag;
    }

    [Fact]
    public void Parser_moderate_paren_depth_succeeds()
    {
        // 64 levels of parens around an integer — well under the 256 budget.
        StringBuilder open = new();
        StringBuilder close = new();
        for (int i = 0; i < 64; i++) { open.Append('('); close.Append(')'); }
        string body = $"opening : Integer\nopening = {open}42{close}\n";

        DiagnosticBag bag = ParseBag(body);
        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Parser_deep_paren_bomb_trips_fuel()
    {
        // 500 levels of parens. Each re-enters ParseExpression + ParseBinary,
        // so depth grows ~2x per paren — trips well before 500 levels.
        StringBuilder open = new();
        StringBuilder close = new();
        for (int i = 0; i < 500; i++) { open.Append('('); close.Append(')'); }
        string body = $"opening : Integer\nopening = {open}42{close}\n";

        DiagnosticBag bag = ParseBag(body);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Parser_deep_nested_ctor_pattern_trips_fuel()
    {
        // Pattern `Some (Some (Some ... (otherwise)))` 300 deep.
        StringBuilder patternOpen = new();
        StringBuilder patternClose = new();
        for (int i = 0; i < 300; i++)
        {
            patternOpen.Append("Some (");
            patternClose.Append(")");
        }
        string body = $@"
Opt =
 | Some (Integer)
 | None

opening : Integer
opening = when None
 is {patternOpen}otherwise{patternClose} -> 0
 is otherwise -> 1
";

        DiagnosticBag bag = ParseBag(body);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Parser_deep_function_type_trips_fuel()
    {
        // Type `Integer -> Integer -> ... -> Integer` 500 deep. ParseType
        // recurses on arrow right-hand side.
        StringBuilder sig = new("Integer");
        for (int i = 0; i < 500; i++) { sig.Append(" -> Integer"); }
        string body = $"opening : {sig}\nopening = 42\n";

        DiagnosticBag bag = ParseBag(body);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResourceExhausted_detail_names_the_parser_op()
    {
        StringBuilder open = new();
        StringBuilder close = new();
        for (int i = 0; i < 500; i++) { open.Append('('); close.Append(')'); }
        string body = $"opening : Integer\nopening = {open}42{close}\n";

        DiagnosticBag bag = ParseBag(body);
        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("parser", fuelDiag.Message);
        Assert.Contains("budget 256", fuelDiag.Message);
    }
}
