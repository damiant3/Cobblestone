using Codex.Core;

namespace Codex.Syntax;

// Use ASCII digit checks instead of char.IsDigit to avoid matching
// Unicode digits (NKo, Devanagari, etc.) that long.Parse can't handle.
static file class CharHelpers
{
    public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}

public enum LexerMode
{
    Prose,
    Notation
}

public sealed class Lexer
{
    readonly DiagnosticBag m_diagnostics;
    readonly string m_text;
    readonly string m_fileName;

    int m_position;
    int m_line;
    int m_column;

    readonly Stack<int> m_indentStack = new();
    int m_currentLineIndent;
    bool m_atLineStart = true;

    readonly List<Token> m_pending = [];

    public Lexer(SourceText source, DiagnosticBag diagnostics)
    {
        m_diagnostics = diagnostics;
        m_text = source.Content;
        m_fileName = source.FileName;
        m_position = 0;
        m_line = 1;
        m_column = 1;
        m_indentStack.Push(0);
    }

    public DiagnosticBag Diagnostics => m_diagnostics;

    public IReadOnlyList<Token> TokenizeAll()
    {
        List<Token> tokens = [];
        while (true)
        {
            Token token = NextToken();
            tokens.Add(token);
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }
        }
        return tokens;
    }

    public Token NextToken()
    {
        if (m_pending.Count > 0)
        {
            Token t = m_pending[0];
            m_pending.RemoveAt(0);
            return t;
        }

        if (m_atLineStart)
        {
            SkipBlankLines();
            if (IsAtEnd)
            {
                return EmitDedentsToZero();
            }

            m_currentLineIndent = MeasureIndentation();
            EmitIndentationTokens();
            m_atLineStart = false;

            if (m_pending.Count > 0)
            {
                Token t = m_pending[0];
                m_pending.RemoveAt(0);
                return t;
            }
        }

        if (IsAtEnd)
        {
            return EmitDedentsToZero();
        }

        return ScanToken();
    }

    Token ScanToken()
    {
        SkipSpaces();

        if (IsAtEnd)
        {
            return EmitDedentsToZero();
        }

        char c = Current;

        if (c == '\n' || c == '\r')
        {
            SourcePosition start = MakePosition();
            ConsumeNewline();
            m_atLineStart = true;
            return new Token(TokenKind.Newline, "\\n", MakeSpan(start));
        }

        if (c == '"')
        {
            return ScanTextLiteral();
        }

        if (CharHelpers.IsAsciiDigit(c))
        {
            return ScanNumber();
        }

        if (char.IsLetter(c) || c == '_')
        {
            return ScanIdentifierOrKeyword();
        }

        return ScanOperator();
    }

    Token ScanTextLiteral()
    {
        SourcePosition start = MakePosition();
        int savedPos = m_position;
        int savedLine = m_line;
        int savedCol = m_column;

        // Peek ahead to determine if this string contains interpolation holes.
        bool hasInterpolation = false;
        Advance(); // skip opening "
        while (!IsAtEnd && Current != '"' && Current != '\n')
        {
            if (Current == '\\' && m_position + 1 < m_text.Length)
            {
                Advance();
                Advance();
            }
            else if (Current == '#' && m_position + 1 < m_text.Length && m_text[m_position + 1] == '{')
            {
                hasInterpolation = true;
                break;
            }
            else
            {
                Advance();
            }
        }

        // Restore position and scan for real.
        m_position = savedPos;
        m_line = savedLine;
        m_column = savedCol;

        if (hasInterpolation)
        {
            return ScanInterpolatedString();
        }

        return ScanPlainTextLiteral();
    }

    Token ScanPlainTextLiteral()
    {
        SourcePosition start = MakePosition();
        Advance();

        System.Text.StringBuilder sb = new();
        while (!IsAtEnd && Current != '"' && Current != '\n')
        {
            if (Current == '\\' && m_position + 1 < m_text.Length)
            {
                Advance();
                sb.Append(Current switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '#' => '#',
                    _ => Current
                });
                Advance();
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        if (IsAtEnd || Current == '\n')
        {
            m_diagnostics.Error("CDX0001", "Unterminated text literal", MakeSpan(start));
        }
        else
        {
            Advance();
        }

        SourceSpan span = MakeSpan(start);
        return new Token(TokenKind.TextLiteral, m_text[start.Offset..span.End.Offset], span)
        {
            LiteralValue = sb.ToString()
        };
    }

    Token ScanInterpolatedString()
    {
        SourcePosition start = MakePosition();
        Advance(); // skip opening "

        m_pending.Add(new Token(TokenKind.InterpolatedStart, "\"", MakeSpan(start)));

        while (!IsAtEnd && Current != '"' && Current != '\n')
        {
            if (Current == '#' && m_position + 1 < m_text.Length && m_text[m_position + 1] == '{')
            {
                SourcePosition braceStart = MakePosition();
                Advance(); // skip #
                Advance(); // skip {
                m_pending.Add(new Token(TokenKind.InterpolatedExprStart, "#{", MakeSpan(braceStart)));

                // Scan expression tokens until matching }.
                int depth = 1;
                while (!IsAtEnd && depth > 0)
                {
                    SkipSpaces();
                    if (IsAtEnd) break;

                    if (Current == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            SourcePosition closeStart = MakePosition();
                            Advance();
                            m_pending.Add(new Token(TokenKind.InterpolatedExprEnd, "}", MakeSpan(closeStart)));
                            break;
                        }
                    }

                    if (depth > 0 && !IsAtEnd)
                    {
                        if (Current == '{') depth++;
                        Token exprToken = ScanExpressionToken();
                        m_pending.Add(exprToken);
                    }
                }

                if (depth > 0)
                {
                    m_diagnostics.Error("CDX0004", "Unterminated interpolation expression", MakeSpan(start));
                }
            }
            else
            {
                ScanTextFragment();
            }
        }

        if (IsAtEnd || Current == '\n')
        {
            m_diagnostics.Error("CDX0001", "Unterminated text literal", MakeSpan(start));
        }
        else
        {
            SourcePosition endStart = MakePosition();
            Advance();
            m_pending.Add(new Token(TokenKind.InterpolatedEnd, "\"", MakeSpan(endStart)));
        }

        // Return the first pending token; the rest are queued.
        Token first = m_pending[0];
        m_pending.RemoveAt(0);
        return first;
    }

    void ScanTextFragment()
    {
        SourcePosition start = MakePosition();
        System.Text.StringBuilder sb = new();

        while (!IsAtEnd && Current != '"' && Current != '\n' && !(Current == '#' && m_position + 1 < m_text.Length && m_text[m_position + 1] == '{'))
        {
            if (Current == '\\' && m_position + 1 < m_text.Length)
            {
                Advance();
                sb.Append(Current switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '#' => '#',
                    _ => Current
                });
                Advance();
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        if (sb.Length > 0)
        {
            SourceSpan span = MakeSpan(start);
            m_pending.Add(new Token(TokenKind.TextFragment, m_text[start.Offset..span.End.Offset], span)
            {
                LiteralValue = sb.ToString()
            });
        }
    }

    Token ScanExpressionToken()
    {
        SkipSpaces();
        char c = Current;

        if (c == '"') return ScanPlainTextLiteral();
        if (CharHelpers.IsAsciiDigit(c)) return ScanNumber();
        if (char.IsLetter(c) || c == '_') return ScanIdentifierOrKeyword();
        return ScanOperator();
    }

    Token ScanNumber()
    {
        SourcePosition start = MakePosition();
        bool isFloat = false;

        while (!IsAtEnd && (CharHelpers.IsAsciiDigit(Current) || Current == '_'))
        {
            Advance();
        }

        if (!IsAtEnd && Current == '.' && m_position + 1 < m_text.Length && char.IsDigit(m_text[m_position + 1]))
        {
            isFloat = true;
            Advance();
            while (!IsAtEnd && (CharHelpers.IsAsciiDigit(Current) || Current == '_'))
            {
                Advance();
            }
        }

        SourceSpan span = MakeSpan(start);
        string text = m_text[start.Offset..span.End.Offset];
        string cleanText = text.Replace("_", "");

        if (isFloat)
        {
            return new Token(TokenKind.NumberLiteral, text, span)
            {
                LiteralValue = decimal.Parse(cleanText, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        else
        {
            return new Token(TokenKind.IntegerLiteral, text, span)
            {
                LiteralValue = long.Parse(cleanText, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
    }

    Token ScanIdentifierOrKeyword()
    {
        SourcePosition start = MakePosition();

        while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
        {
            Advance();
        }

        while (!IsAtEnd && Current == '-' && m_position + 1 < m_text.Length && char.IsLetter(m_text[m_position + 1]))
        {
            Advance();
            while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            {
                Advance();
            }
        }

        SourceSpan span = MakeSpan(start);
        string text = m_text[start.Offset..span.End.Offset];

        TokenKind kind = ClassifyWord(text);
        Token token = new(kind, text, span);

        if (kind == TokenKind.TrueKeyword)
        {
            return token with { LiteralValue = true };
        }
        if (kind == TokenKind.FalseKeyword)
        {
            return token with { LiteralValue = false };
        }

        return token;
    }

    static TokenKind ClassifyWord(string text) => text switch
    {
        "let" => TokenKind.LetKeyword,
        "in" => TokenKind.InKeyword,
        "if" => TokenKind.IfKeyword,
        "then" => TokenKind.ThenKeyword,
        "else" => TokenKind.ElseKeyword,
        "when" => TokenKind.WhenKeyword,
        "where" => TokenKind.WhereKeyword,
        "do" => TokenKind.DoKeyword,
        "record" => TokenKind.RecordKeyword,
        "import" => TokenKind.ImportKeyword,
        "export" => TokenKind.ExportKeyword,
        "claim" => TokenKind.ClaimKeyword,
        "proof" => TokenKind.ProofKeyword,
        "forall" => TokenKind.ForAllKeyword,
        "exists" => TokenKind.ThereExistsKeyword,
        "linear" => TokenKind.LinearKeyword,
        "effect" => TokenKind.EffectKeyword,
        "with" => TokenKind.WithKeyword,
        "True" => TokenKind.TrueKeyword,
        "False" => TokenKind.FalseKeyword,
        _ => char.IsUpper(text[0]) ? TokenKind.TypeIdentifier : TokenKind.Identifier
    };

    Token ScanOperator()
    {
        SourcePosition start = MakePosition();
        char c = Current;
        Advance();

        switch (c)
        {
            case '(' : return new Token(TokenKind.LeftParen, "(", MakeSpan(start));
            case ')' : return new Token(TokenKind.RightParen, ")", MakeSpan(start));
            case '[' : return new Token(TokenKind.LeftBracket, "[", MakeSpan(start));
            case ']' : return new Token(TokenKind.RightBracket, "]", MakeSpan(start));
            case '{' : return new Token(TokenKind.LeftBrace, "{", MakeSpan(start));
            case '}' : return new Token(TokenKind.RightBrace, "}", MakeSpan(start));
            case ',' : return new Token(TokenKind.Comma, ",", MakeSpan(start));
            case '.' : return new Token(TokenKind.Dot, ".", MakeSpan(start));
            case '^' : return new Token(TokenKind.Caret, "^", MakeSpan(start));
            case '_' : return new Token(TokenKind.Underscore, "_", MakeSpan(start));

            case '+':
                if (!IsAtEnd && Current == '+') { Advance(); return new Token(TokenKind.PlusPlus, "++", MakeSpan(start)); }
                return new Token(TokenKind.Plus, "+", MakeSpan(start));

            case '-':
                if (!IsAtEnd && Current == '>') { Advance(); return new Token(TokenKind.Arrow, "->", MakeSpan(start)); }
                return new Token(TokenKind.Minus, "-", MakeSpan(start));

            case '*' : return new Token(TokenKind.Star, "*", MakeSpan(start));

            case '/' :
                if (!IsAtEnd && Current == '=') { Advance(); return new Token(TokenKind.NotEquals, "/=", MakeSpan(start)); }
                return new Token(TokenKind.Slash, "/", MakeSpan(start));

            case '=' :
                if (!IsAtEnd && Current == '=')
                {
                    Advance();
                    if (!IsAtEnd && Current == '=') { Advance(); return new Token(TokenKind.TripleEquals, "===", MakeSpan(start)); }
                    return new Token(TokenKind.DoubleEquals, "==", MakeSpan(start));
                }
                return new Token(TokenKind.Equals, "=", MakeSpan(start));

            case ':':
                if (!IsAtEnd && Current == ':') { Advance(); return new Token(TokenKind.ColonColon, "::", MakeSpan(start)); }
                return new Token(TokenKind.Colon, ":", MakeSpan(start));

            case '|':
                if (!IsAtEnd && Current == '-') { Advance(); return new Token(TokenKind.Turnstile, "|-", MakeSpan(start)); }
                return new Token(TokenKind.Pipe, "|", MakeSpan(start));

            case '&' : return new Token(TokenKind.Ampersand, "&", MakeSpan(start));
            case '\\' : return new Token(TokenKind.Backslash, "\\", MakeSpan(start));

            case '<':
                if (!IsAtEnd && Current == '=') { Advance(); return new Token(TokenKind.LessOrEqual, "<=", MakeSpan(start)); }
                if (!IsAtEnd && Current == '-') { Advance(); return new Token(TokenKind.LeftArrow, "<-", MakeSpan(start)); }
                return new Token(TokenKind.LessThan, "<", MakeSpan(start));

            case '>':
                if (!IsAtEnd && Current == '=') { Advance(); return new Token(TokenKind.GreaterOrEqual, ">=", MakeSpan(start)); }
                return new Token(TokenKind.GreaterThan, ">", MakeSpan(start));

            case '→' : return new Token(TokenKind.Arrow, "→", MakeSpan(start));
            case '←' : return new Token(TokenKind.LeftArrow, "←", MakeSpan(start));
            case '∀' : return new Token(TokenKind.ForAllSymbol, "∀", MakeSpan(start));
            case '∃' : return new Token(TokenKind.ExistsSymbol, "∃", MakeSpan(start));
            case '≡' : return new Token(TokenKind.TripleEquals, "≡", MakeSpan(start));
            case '≠' : return new Token(TokenKind.NotEquals, "≠", MakeSpan(start));
            case '≤' : return new Token(TokenKind.LessOrEqual, "≤", MakeSpan(start));
            case '≥' : return new Token(TokenKind.GreaterOrEqual, "≥", MakeSpan(start));
            case '⊢' : return new Token(TokenKind.Turnstile, "⊢", MakeSpan(start));
            case '⊗' : return new Token(TokenKind.LinearProduct, "⊗", MakeSpan(start));

            default:
                m_diagnostics.Error("CDX0002", $"Unexpected character '{c}'", MakeSpan(start));
                return new Token(TokenKind.Error, c.ToString(), MakeSpan(start));
        }
    }

    void SkipBlankLines()
    {
        while (!IsAtEnd)
        {
            int saved = m_position;
            int savedLine = m_line;
            int savedCol = m_column;

            while (!IsAtEnd && Current == ' ')
            {
                Advance();
            }

            if (IsAtEnd || Current == '\n' || Current == '\r')
            {
                if (!IsAtEnd)
                {
                    ConsumeNewline();
                }
                continue;
            }

            m_position = saved;
            m_line = savedLine;
            m_column = savedCol;
            break;
        }
    }

    int MeasureIndentation()
    {
        int indent = 0;
        while (!IsAtEnd && Current == ' ')
        {
            indent++;
            Advance();
        }
        return indent;
    }

    void EmitIndentationTokens()
    {
        int indent = m_currentLineIndent;
        int currentIndent = m_indentStack.Peek();
        SourcePosition pos = MakePosition();
        SourceSpan span = SourceSpan.Single(pos.Offset, pos.Line, pos.Column, m_fileName);

        if (indent > currentIndent)
        {
            m_indentStack.Push(indent);
            m_pending.Add(new Token(TokenKind.Indent, "<indent>", span));
        }
        else
        {
            while (indent < m_indentStack.Peek())
            {
                m_indentStack.Pop();
                m_pending.Add(new Token(TokenKind.Dedent, "<dedent>", span));
            }

            if (indent != m_indentStack.Peek())
            {
                m_diagnostics.Warning("CDX0003", "Indentation does not match any outer level", span);
            }
        }
    }

    Token EmitDedentsToZero()
    {
        SourcePosition pos = MakePosition();
        SourceSpan span = SourceSpan.Single(pos.Offset, pos.Line, pos.Column, m_fileName);

        while (m_indentStack.Count > 1)
        {
            m_indentStack.Pop();
            m_pending.Add(new Token(TokenKind.Dedent, "<dedent>", span));
        }

        if (m_pending.Count > 0)
        {
            Token t = m_pending[0];
            m_pending.RemoveAt(0);
            m_pending.Add(new Token(TokenKind.EndOfFile, "", span));
            return t;
        }

        return new Token(TokenKind.EndOfFile, "", span);
    }

    bool IsAtEnd => m_position >= m_text.Length;

    char Current => m_text[m_position];

    void Advance()
    {
        if (m_position < m_text.Length)
        {
            m_column++;
            m_position++;
        }
    }

    void ConsumeNewline()
    {
        if (Current == '\r')
        {
            m_position++;
            if (m_position < m_text.Length && m_text[m_position] == '\n')
            {
                m_position++;
            }
        }
        else if (Current == '\n')
        {
            m_position++;
        }
        m_line++;
        m_column = 1;
    }

    void SkipSpaces()
    {
        while (!IsAtEnd && Current == ' ')
        {
            Advance();
        }
    }

    SourcePosition MakePosition() => new(m_position, m_line, m_column);

    SourceSpan MakeSpan(SourcePosition start) =>
        new(start, MakePosition(), m_fileName);
}
