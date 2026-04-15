using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

public class LexerTests
{
    private static IReadOnlyList<Token> Tokenize(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        return lexer.TokenizeAll();
    }

    private static IReadOnlyList<Token> NonTrivialTokens(string source)
    {
        return Tokenize(source)
            .Where(t => t.Kind is not (TokenKind.Newline or TokenKind.Indent
                or TokenKind.Dedent or TokenKind.EndOfFile))
            .ToList();
    }

    [Fact]
    public void Empty_source_produces_eof()
    {
        IReadOnlyList<Token> tokens = Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
    }

    [Fact]
    public void Integer_literal()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("42");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.IntegerLiteral, tokens[0].Kind);
        Assert.Equal(42L, tokens[0].LiteralValue);
    }

    [Fact]
    public void Number_literal_with_decimal()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("3.14");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.NumberLiteral, tokens[0].Kind);
        Assert.Equal(3.14d, tokens[0].LiteralValue);
    }

    [Fact]
    public void Text_literal()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"hello\"");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.TextLiteral, tokens[0].Kind);
        Assert.Equal("hello", tokens[0].LiteralValue);
    }

    [Fact]
    public void Text_literal_with_escapes()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"hello\\nworld\"");
        Assert.Single(tokens);
        Assert.Equal("hello\nworld", tokens[0].LiteralValue);
    }

    [Fact]
    public void Hyphenated_identifier()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("compute-monthly-payment");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("compute-monthly-payment", tokens[0].Text);
    }

    [Fact]
    public void Type_identifier()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("Account");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.TypeIdentifier, tokens[0].Kind);
    }

    [Fact]
    public void Keywords_are_recognized()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("let in if then else when where do");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.LetKeyword, TokenKind.InKeyword,
            TokenKind.IfKeyword, TokenKind.ThenKeyword, TokenKind.ElseKeyword,
            TokenKind.WhenKeyword, TokenKind.WhereKeyword, TokenKind.DoKeyword
        }, kinds);
    }

    [Fact]
    public void Operators()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("+ - * / = == ++ :: -> : |");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.Plus, TokenKind.Minus, TokenKind.Star, TokenKind.Slash,
            TokenKind.Equals, TokenKind.DoubleEquals,
            TokenKind.PlusPlus, TokenKind.ColonColon,
            TokenKind.Arrow, TokenKind.Colon, TokenKind.Pipe
        }, kinds);
    }

    [Fact]
    public void Unicode_operators()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("→ ← ∀ ∃ ≡ ≠ ≤ ≥ ⊢ ⊗");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.Arrow, TokenKind.LeftArrow,
            TokenKind.ForAllSymbol, TokenKind.ExistsSymbol,
            TokenKind.TripleEquals, TokenKind.NotEquals,
            TokenKind.LessOrEqual, TokenKind.GreaterOrEqual,
            TokenKind.Turnstile, TokenKind.LinearProduct
        }, kinds);
    }

    [Fact]
    public void Boolean_literals()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("True False");
        Assert.Equal(TokenKind.TrueKeyword, tokens[0].Kind);
        Assert.Equal(true, tokens[0].LiteralValue);
        Assert.Equal(TokenKind.FalseKeyword, tokens[1].Kind);
        Assert.Equal(false, tokens[1].LiteralValue);
    }

    [Fact]
    public void Delimiters()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("( ) [ ] { } , .");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.LeftParen, TokenKind.RightParen,
            TokenKind.LeftBracket, TokenKind.RightBracket,
            TokenKind.LeftBrace, TokenKind.RightBrace,
            TokenKind.Comma, TokenKind.Dot
        }, kinds);
    }

    [Fact]
    public void Indentation_produces_indent_dedent()
    {
        IReadOnlyList<Token> tokens = Tokenize("a\n  b\nc");
        List<TokenKind> kinds = tokens.Select(t => t.Kind).ToList();
        Assert.Contains(TokenKind.Indent, kinds);
        Assert.Contains(TokenKind.Dedent, kinds);
    }

    [Fact]
    public void Complete_definition_tokens()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        IReadOnlyList<Token> tokens = NonTrivialTokens(source);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("square", tokens[0].Text);
        Assert.Equal(TokenKind.Colon, tokens[1].Kind);
        Assert.Equal(TokenKind.TypeIdentifier, tokens[2].Kind);
        Assert.Equal("Integer", tokens[2].Text);
    }

    [Fact]
    public void Underscored_number()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("1_000_000");
        Assert.Single(tokens);
        Assert.Equal(1000000L, tokens[0].LiteralValue);
    }

    [Fact]
    public void Plain_string_without_braces_is_TextLiteral()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"hello world\"");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.TextLiteral, tokens[0].Kind);
        Assert.Equal("hello world", tokens[0].LiteralValue);
    }

    [Fact]
    public void Interpolated_string_produces_token_sequence()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"hello #{name}!\"");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.InterpolatedStart,
            TokenKind.TextFragment,
            TokenKind.InterpolatedExprStart,
            TokenKind.Identifier,
            TokenKind.InterpolatedExprEnd,
            TokenKind.TextFragment,
            TokenKind.InterpolatedEnd
        }, kinds);
        Assert.Equal("hello ", tokens[1].LiteralValue);
        Assert.Equal("name", tokens[3].Text);
        Assert.Equal("!", tokens[5].LiteralValue);
    }

    [Fact]
    public void Interpolated_string_with_expression()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"count is #{integer-to-text n}\"");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.InterpolatedStart,
            TokenKind.TextFragment,
            TokenKind.InterpolatedExprStart,
            TokenKind.Identifier,
            TokenKind.Identifier,
            TokenKind.InterpolatedExprEnd,
            TokenKind.InterpolatedEnd
        }, kinds);
    }

    [Fact]
    public void Interpolated_string_only_expression()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"#{x}\"");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.InterpolatedStart,
            TokenKind.InterpolatedExprStart,
            TokenKind.Identifier,
            TokenKind.InterpolatedExprEnd,
            TokenKind.InterpolatedEnd
        }, kinds);
    }

    [Fact]
    public void Escaped_brace_produces_plain_text_literal()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"hello \\#{world}\"");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.TextLiteral, tokens[0].Kind);
        Assert.Equal("hello #{world}", tokens[0].LiteralValue);
    }

    [Fact]
    public void Multiple_interpolation_holes()
    {
        IReadOnlyList<Token> tokens = NonTrivialTokens("\"#{a} and #{b}\"");
        TokenKind[] kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(new[]
        {
            TokenKind.InterpolatedStart,
            TokenKind.InterpolatedExprStart,
            TokenKind.Identifier,
            TokenKind.InterpolatedExprEnd,
            TokenKind.TextFragment,
            TokenKind.InterpolatedExprStart,
            TokenKind.Identifier,
            TokenKind.InterpolatedExprEnd,
            TokenKind.InterpolatedEnd
        }, kinds);
        Assert.Equal(" and ", tokens[4].LiteralValue);
    }

    // --- Tier 0 escape diagnostics (CDX0005 / CDX0006) ---

    private static (IReadOnlyList<Token> tokens, DiagnosticBag bag) TokenizeWithDiag(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        return (lexer.TokenizeAll(), bag);
    }

    [Fact]
    public void Tab_escape_in_text_literal_emits_CDX0005()
    {
        (IReadOnlyList<Token> tokens, DiagnosticBag bag) = TokenizeWithDiag("\"hello\\tworld\"");
        Assert.True(bag.HasErrors);
        Diagnostic diag = bag.ToImmutable().First(d => d.Code == CdxCodes.InvalidTabEscape);
        Assert.Contains("\\t", diag.Message);
        // Recovery: \t normalizes to two spaces
        Token lit = tokens.First(t => t.Kind == TokenKind.TextLiteral);
        Assert.Equal("hello  world", lit.LiteralValue);
    }

    [Fact]
    public void CR_escape_in_text_literal_emits_CDX0006()
    {
        (IReadOnlyList<Token> tokens, DiagnosticBag bag) = TokenizeWithDiag("\"hello\\rworld\"");
        Assert.True(bag.HasErrors);
        Diagnostic diag = bag.ToImmutable().First(d => d.Code == CdxCodes.InvalidCarriageReturnEscape);
        Assert.Contains("\\r", diag.Message);
        // Recovery: \r stripped
        Token lit = tokens.First(t => t.Kind == TokenKind.TextLiteral);
        Assert.Equal("helloworld", lit.LiteralValue);
    }

    [Fact]
    public void Tab_escape_in_char_literal_emits_CDX0005()
    {
        (IReadOnlyList<Token> tokens, DiagnosticBag bag) = TokenizeWithDiag("'\\t'");
        Assert.True(bag.HasErrors);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.InvalidTabEscape);
        // Recovery: maps to space (32)
        Token lit = tokens.First(t => t.Kind == TokenKind.CharLiteral);
        Assert.Equal((long)' ', lit.LiteralValue);
    }

    [Fact]
    public void CR_escape_in_char_literal_emits_CDX0006()
    {
        (IReadOnlyList<Token> tokens, DiagnosticBag bag) = TokenizeWithDiag("'\\r'");
        Assert.True(bag.HasErrors);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.InvalidCarriageReturnEscape);
        // Recovery: maps to newline (10)
        Token lit = tokens.First(t => t.Kind == TokenKind.CharLiteral);
        Assert.Equal((long)'\n', lit.LiteralValue);
    }

    [Fact]
    public void Valid_escapes_still_work()
    {
        (IReadOnlyList<Token> tokens, DiagnosticBag bag) = TokenizeWithDiag("\"hello\\nworld\\\\end\"");
        Assert.False(bag.HasErrors);
        Token lit = tokens.First(t => t.Kind == TokenKind.TextLiteral);
        Assert.Equal("hello\nworld\\end", lit.LiteralValue);
    }
}
