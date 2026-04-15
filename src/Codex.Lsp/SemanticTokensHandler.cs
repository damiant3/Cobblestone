using Codex.Syntax;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal sealed class SemanticTokensHandler(DocumentStore store) : SemanticTokensHandlerBase
{
    readonly DocumentStore m_store = store;

    internal static readonly SemanticTokensLegend s_legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(
            SemanticTokenType.Keyword,      // 0
            SemanticTokenType.Function,     // 1
            SemanticTokenType.Variable,     // 2
            SemanticTokenType.Type,         // 3
            SemanticTokenType.Number,       // 4
            SemanticTokenType.String,       // 5
            SemanticTokenType.Operator,     // 6
            SemanticTokenType.Comment       // 7
        ),
        TokenModifiers = new Container<SemanticTokenModifier>(
            SemanticTokenModifier.Declaration,
            SemanticTokenModifier.Definition
        ),
    };

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false,
            Legend = s_legend,
        };
    }

    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        AnalysisResult? result = m_store.GetResult(uri);
        if (result is null)
            return Task.CompletedTask;

        foreach (Token token in result.Tokens)
        {
            if (token.Kind is TokenKind.Newline or TokenKind.Indent
                or TokenKind.Dedent or TokenKind.EndOfFile)
                continue;

            int tokenType = ClassifyToken(token);
            if (tokenType < 0)
                continue;

            int line = Math.Max(0, token.Span.Start.Line - 1);
            int col = Math.Max(0, token.Span.Start.Column - 1);
            int length = token.Span.Length;
            if (length <= 0)
                continue;

            builder.Push(line, col, length, tokenType, 0);
        }

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams request,
        CancellationToken ct)
    {
        return Task.FromResult(new SemanticTokensDocument(s_legend));
    }

    static int ClassifyToken(Token token)
    {
        return token.Kind switch
        {
            TokenKind.LetKeyword or TokenKind.InKeyword
                or TokenKind.IfKeyword or TokenKind.IsKeyword or TokenKind.OtherwiseKeyword
                or TokenKind.ThenKeyword or TokenKind.ElseKeyword
                or TokenKind.WhenKeyword or TokenKind.DoKeyword
                or TokenKind.LinearKeyword or TokenKind.RecordKeyword
                or TokenKind.WhereKeyword or TokenKind.SuchThatKeyword
                or TokenKind.CitesKeyword
                or TokenKind.ClaimKeyword or TokenKind.ProofKeyword
                or TokenKind.ForAllKeyword or TokenKind.ThereExistsKeyword
                or TokenKind.TrueKeyword or TokenKind.FalseKeyword
                => 0, // keyword

            TokenKind.TypeIdentifier => 3, // type

            TokenKind.Identifier => 2, // variable (functions resolved at this level too)

            TokenKind.IntegerLiteral or TokenKind.NumberLiteral
                => 4, // number

            TokenKind.TextLiteral => 5, // string

            TokenKind.Equals or TokenKind.Colon or TokenKind.Arrow or TokenKind.LeftArrow
                or TokenKind.Pipe or TokenKind.Ampersand
                or TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                or TokenKind.Caret or TokenKind.PlusPlus or TokenKind.ColonColon
                or TokenKind.DoubleEquals or TokenKind.NotEquals
                or TokenKind.LessThan or TokenKind.GreaterThan
                or TokenKind.LessOrEqual or TokenKind.GreaterOrEqual
                or TokenKind.TripleEquals or TokenKind.DashGreater
                or TokenKind.Turnstile or TokenKind.LinearProduct
                or TokenKind.ForAllSymbol or TokenKind.ExistsSymbol
                => 6, // operator

            _ => -1,
        };
    }
}
