using Codex.Core;

namespace Codex.Syntax;

public sealed partial class Parser
{
    public ExpressionNode ParseExpression()
    {
        return ParseBinary(0);
    }

    ExpressionNode ParseBinary(int minPrecedence)
    {
        ExpressionNode left = ParseUnary();

        while (true)
        {
            SkipNewlines();
            (int prec, Associativity assoc) = GetPrecedence(Current.Kind);
            if (prec < 0 || prec < minPrecedence)
            {
                break;
            }

            Token op = Current;
            Advance();
            SkipNewlines();

            int nextMin = assoc == Associativity.Right ? prec : prec + 1;
            ExpressionNode right = ParseBinary(nextMin);

            left = new BinaryExpressionNode(left, op, right, left.Span.Through(right.Span));
        }

        return left;
    }

    ExpressionNode ParseUnary()
    {
        if (Current.Kind == TokenKind.Minus)
        {
            Token op = Current;
            Advance();
            ExpressionNode operand = ParseUnary();
            return new UnaryExpressionNode(op, operand, op.Span.Through(operand.Span));
        }

        return ParseApplication();
    }

    ExpressionNode ParseApplication()
    {
        ExpressionNode func = ParseAtom();

        bool isCompound = func is MatchExpressionNode
            or IfExpressionNode
            or LetExpressionNode
            or DoExpressionNode;
        if (isCompound)
        {
            return func;
        }

        while (true)
        {
            if (IsApplicationStart())
            {
                ExpressionNode arg = ParseAtom();
                func = new ApplicationExpressionNode(func, arg, func.Span.Through(arg.Span));
            }
            else if (Current.Kind == TokenKind.Dot)
            {
                Advance();
                if (!IsIdentifierLike(Current.Kind))
                {
                    Expect(TokenKind.Identifier);
                    break;
                }
                Token field = Current;
                Advance();

                func = new FieldAccessExpressionNode(func, field, func.Span.Through(field.Span));
            }
            else
            {
                break;
            }
        }

        return func;
    }

    bool IsApplicationStart()
    {
        return Current.Kind is TokenKind.Identifier
            or TokenKind.TypeIdentifier
            or TokenKind.IntegerLiteral
            or TokenKind.NumberLiteral
            or TokenKind.TextLiteral
            or TokenKind.CharLiteral
            or TokenKind.TrueKeyword
            or TokenKind.FalseKeyword
            or TokenKind.LeftParen
            or TokenKind.LeftBracket
            or TokenKind.DoKeyword
            or TokenKind.ActKeyword
            or TokenKind.EndKeyword
            or TokenKind.QedKeyword
            or TokenKind.WithKeyword;
    }

    ExpressionNode ParseAtom()
    {
        switch (Current.Kind)
        {
            case TokenKind.IntegerLiteral:
            case TokenKind.NumberLiteral:
            case TokenKind.TextLiteral:
            case TokenKind.CharLiteral:
            case TokenKind.TrueKeyword:
            case TokenKind.FalseKeyword:
            {
                Token token = Current;
                Advance();
                return new LiteralExpressionNode(token);
            }

            case TokenKind.InterpolatedStart:
                return ParseInterpolatedString();

            case TokenKind.Identifier:
            case TokenKind.TypeIdentifier:
            case TokenKind.EndKeyword:
            case TokenKind.QedKeyword:
            {
                Token token = Current;
                Advance();

                if (token.Kind == TokenKind.TypeIdentifier && Current.Kind == TokenKind.LeftBrace)
                {
                    return ParseRecordExpression(token);
                }

                ExpressionNode node = new NameExpressionNode(token);
                while (Current.Kind == TokenKind.Dot)
                {
                    Advance();
                    if (!IsIdentifierLike(Current.Kind))
                    {
                        Expect(TokenKind.Identifier);
                        break;
                    }
                    Token field = Current;
                    Advance();
                    node = new FieldAccessExpressionNode(node, field, node.Span.Through(field.Span));
                }
                return node;
            }

            case TokenKind.LeftParen:
            {
                Token start = Current;
                Advance();
                SkipNewlines();
                ExpressionNode inner = ParseExpression();
                SkipNewlines();
                Expect(TokenKind.RightParen);
                ExpressionNode node = new ParenthesizedExpressionNode(inner, start.Span.Through(Previous.Span));
                while (Current.Kind == TokenKind.Dot)
                {
                    Advance();
                    if (!IsIdentifierLike(Current.Kind))
                    {
                        Expect(TokenKind.Identifier);
                        break;
                    }
                    Token field = Current;
                    Advance();
                    node = new FieldAccessExpressionNode(node, field, node.Span.Through(field.Span));
                }
                return node;
            }

            case TokenKind.LeftBracket:
                return ParseListExpression();

            case TokenKind.IfKeyword:
                return ParseIfExpression();

            case TokenKind.LetKeyword:
                return ParseLetExpression();

            case TokenKind.WhenKeyword:
                return ParseMatchExpression();

            case TokenKind.DoKeyword:
                return ParseDoExpression();

            case TokenKind.ActKeyword:
                return ParseActExpression();

            case TokenKind.WithKeyword:
                return ParseHandleExpression();

            case TokenKind.Backslash:
                return ParseLambdaExpression();

            default:
            {
                m_diagnostics.Error(CdxCodes.ExpectedExpression, $"Expected an expression, found {Current.Kind}", Current.Span);
                Token err = Current;
                Advance();
                return new ErrorExpressionNode(err);
            }
        }
    }

    ExpressionNode ParseLambdaExpression()
    {
        SourceSpan start = Current.Span;
        Advance(); // consume backslash

        // Collect parameters: identifiers before ->
        List<Token> parameters = [];
        while (IsIdentifierLike(Current.Kind) && !IsAtEnd)
        {
            parameters.Add(Current);
            Advance();
        }

        Expect(TokenKind.Arrow);
        SkipNewlines();
        ExpressionNode body = ParseExpression();

        return new LambdaExpressionNode(parameters, body, start.Through(body.Span));
    }

    ExpressionNode ParseRecordExpression(Token typeName)
    {
        SourceSpan start = typeName.Span;
        Expect(TokenKind.LeftBrace);
        SkipNewlines();

        List<RecordFieldNode> fields = [];
        while (Current.Kind != TokenKind.RightBrace && !IsAtEnd)
        {
            if (!IsIdentifierLike(Current.Kind))
            {
                m_diagnostics.Error(CdxCodes.ExpectedFieldName,
                    $"Expected field name, found {Current.Kind}", Current.Span);
                int before = m_position;
                Synchronize();
                if (m_position == before)
                {
                    Advance();
                }
                if (Current.Kind == TokenKind.RightBrace)
                {
                    break;
                }

                SkipNewlines();
                continue;
            }

            Token fieldName = Current;
            Advance();
            Token eqToken = Expect(TokenKind.Equals);
            if (eqToken.Kind != TokenKind.Equals)
            {
                int before = m_position;
                Synchronize();
                if (m_position == before)
                {
                    Advance();
                }
                if (Current.Kind == TokenKind.RightBrace)
                {
                    break;
                }

                SkipNewlines();
                continue;
            }
            ExpressionNode fieldValue = ParseExpression();
            fields.Add(new RecordFieldNode(fieldName, fieldValue,
                fieldName.Span.Through(fieldValue.Span)));

            SkipNewlines();
            if (Current.Kind == TokenKind.Comma)
            {
                Advance();
                SkipNewlines();
            }
        }

        Expect(TokenKind.RightBrace);
        return new RecordExpressionNode(typeName, fields, start.Through(Previous.Span));
    }

    ExpressionNode ParseListExpression()
    {
        Token start = Current;
        Advance();
        SkipNewlines();

        List<ExpressionNode> elements = [];
        while (Current.Kind != TokenKind.RightBracket && !IsAtEnd)
        {
            if (Current.Kind is TokenKind.Newline or TokenKind.Dedent)
            {
                SkipNewlines();
                if (Current.Kind == TokenKind.RightBracket)
                {
                    break;
                }
            }

            elements.Add(ParseExpression());
            SkipNewlines();
            if (Current.Kind == TokenKind.Comma)
            {
                Advance();
                SkipNewlines();
            }
        }

        if (Current.Kind != TokenKind.RightBracket)
        {
            m_diagnostics.Error(CdxCodes.UnterminatedListLiteral,
                "Unterminated list literal — expected ']'", start.Span);
            return new ListExpressionNode(elements, start.Span.Through(Previous.Span));
        }

        Expect(TokenKind.RightBracket);
        return new ListExpressionNode(elements, start.Span.Through(Previous.Span));
    }

    ExpressionNode ParseIfExpression()
    {
        Token start = Current;
        Advance();
        SkipNewlines();
        ExpressionNode condition = ParseExpression();
        SkipNewlines();

        if (Current.Kind != TokenKind.ThenKeyword)
        {
            m_diagnostics.Error(CdxCodes.ExpectedThenKeyword,
                $"Expected 'then' after condition, found {Current.Kind}", Current.Span);
            Synchronize();
            if (Current.Kind == TokenKind.ThenKeyword)
            {
                Advance();
            }
            else
            {
                return new ErrorExpressionNode(start);
            }
        }
        else
        {
            Advance();
        }

        SkipNewlines();
        ExpressionNode thenExpr = ParseExpression();
        SkipNewlines();

        if (Current.Kind != TokenKind.ElseKeyword)
        {
            m_diagnostics.Error(CdxCodes.ExpectedElseKeyword,
                $"Expected 'else' after 'then' branch, found {Current.Kind}", Current.Span);
            Synchronize();
            if (Current.Kind == TokenKind.ElseKeyword)
            {
                Advance();
            }
            else
            {
                return new IfExpressionNode(condition, thenExpr,
                    new ErrorExpressionNode(Current), start.Span.Through(thenExpr.Span));
            }
        }
        else
        {
            Advance();
        }

        SkipNewlines();
        ExpressionNode elseExpr = ParseExpression();
        return new IfExpressionNode(condition, thenExpr, elseExpr,
            start.Span.Through(elseExpr.Span));
    }

    ExpressionNode ParseLetExpression()
    {
        Token start = Current;
        Advance();
        SkipNewlines();

        List<Syntax.LetBinding> bindings = [];
        while (IsIdentifierLike(Current.Kind)
            || (IsReservedKeyword(Current.Kind) && Peek(1)?.Kind == TokenKind.Equals))
        {
            if (IsReservedKeyword(Current.Kind) && !IsIdentifierLike(Current.Kind))
            {
                ReportReservedKeywordAsIdentifier("a let-binding name");
            }
            Token name = Current;
            Advance();
            Expect(TokenKind.Equals);
            ExpressionNode value = ParseExpression();
            bindings.Add(new Syntax.LetBinding(name, value));
            SkipNewlines();

            if (Current.Kind == TokenKind.Comma)
            {
                Advance();
                SkipNewlines();
            }
        }

        if (Current.Kind != TokenKind.InKeyword)
        {
            m_diagnostics.Error(CdxCodes.ExpectedInKeyword,
                $"Expected 'in' after let bindings, found {Current.Kind}", Current.Span);
            Synchronize();
            if (Current.Kind == TokenKind.InKeyword)
            {
                Advance();
            }
            else
            {
                ExpressionNode errBody = new ErrorExpressionNode(Current);
                return new LetExpressionNode(bindings, errBody,
                    start.Span.Through(errBody.Span));
            }
        }
        else
        {
            Advance();
        }

        SkipNewlines();
        ExpressionNode body = ParseExpression();
        return new LetExpressionNode(bindings, body, start.Span.Through(body.Span));
    }

    ExpressionNode ParseMatchExpression()
    {
        Token start = Current;
        Advance();
        ExpressionNode scrutinee = ParseExpression();
        SkipNewlines();

        List<MatchBranchNode> branches = [];
        if (Current.Kind == TokenKind.IfKeyword)
        {
            m_diagnostics.Error(CdxCodes.ExpectedMatchBranch,
                "Use 'is' to introduce a when-arm; 'if' is reserved for boolean conditionals",
                Current.Span);
        }
        int branchColumn = Current.Kind == TokenKind.IsKeyword ? Current.Span.Start.Column : -1;
        int branchLine = Current.Kind == TokenKind.IsKeyword ? Current.Span.Start.Line : -1;
        while (Current.Kind == TokenKind.IsKeyword
            && (Current.Span.Start.Line == branchLine || Current.Span.Start.Column == branchColumn))
        {
            Advance();
            PatternNode pattern = ParsePattern();

            if (Current.Kind != TokenKind.Arrow)
            {
                m_diagnostics.Error(CdxCodes.ExpectedArrowAfterPattern,
                    $"Expected '->' after pattern, found {Current.Kind}", Current.Span);
                Synchronize();
                if (Current.Kind == TokenKind.Arrow)
                {
                    Advance();
                }
                else
                {
                    SkipNewlines();
                    continue;
                }
            }
            else
            {
                Advance();
            }

            SkipNewlines();
            ExpressionNode body = ParseExpression();
            branches.Add(new MatchBranchNode(pattern, body,
                pattern.Span.Through(body.Span)));
            SkipNewlines();
        }

        if (branches.Count == 0)
        {
            m_diagnostics.Error(CdxCodes.ExpectedMatchBranch,
                "Match expression requires at least one branch", start.Span);
        }

        SourceSpan endSpan = branches.Count > 0 ? branches[^1].Span : scrutinee.Span;
        return new MatchExpressionNode(scrutinee, branches,
            start.Span.Through(endSpan));
    }

    ExpressionNode ParseDoExpression()
    {
        Token start = Current;
        Advance();
        SkipNewlines();

        List<DoStatementNode> statements = [];
        while (!IsAtEnd
            && Current.Kind is not (TokenKind.EndOfFile or TokenKind.Dedent
                                    or TokenKind.ElseKeyword or TokenKind.InKeyword)
            && !(Current.Kind == TokenKind.Identifier && Peek(1)?.Kind == TokenKind.Colon))
        {
            if (IsIdentifierLike(Current.Kind) && Peek(1)?.Kind == TokenKind.LeftArrow)
            {
                Token name = Current;
                Advance();
                Advance();
                ExpressionNode value = ParseExpression();
                statements.Add(new DoBindStatementNode(name, value, name.Span.Through(value.Span)));
            }
            else
            {
                ExpressionNode expr = ParseExpression();
                statements.Add(new DoExprStatementNode(expr, expr.Span));
            }
            SkipNewlines();
        }

        if (statements.Count == 0)
        {
            m_diagnostics.Error(CdxCodes.EmptyDoBlock, "do expression requires at least one statement", start.Span);
        }

        SourceSpan endSpan = statements.Count > 0 ? statements[^1].Span : start.Span;
        return new DoExpressionNode(statements, start.Span.Through(endSpan));
    }

    ExpressionNode ParseActExpression()
    {
        Token start = Current;
        Advance();
        SkipNewlines();

        List<DoStatementNode> statements = [];
        while (!IsAtEnd && Current.Kind != TokenKind.EndKeyword && Current.Kind != TokenKind.EndOfFile)
        {
            if (IsIdentifierLike(Current.Kind) && Peek(1)?.Kind == TokenKind.LeftArrow)
            {
                Token name = Current;
                Advance();
                Advance();
                ExpressionNode value = ParseExpression();
                statements.Add(new DoBindStatementNode(name, value, name.Span.Through(value.Span)));
            }
            else
            {
                ExpressionNode expr = ParseExpression();
                statements.Add(new DoExprStatementNode(expr, expr.Span));
            }
            SkipNewlines();
        }

        Token endTok = Expect(TokenKind.EndKeyword);

        if (statements.Count == 0)
        {
            m_diagnostics.Error(CdxCodes.EmptyDoBlock, "act expression requires at least one statement", start.Span);
        }

        return new DoExpressionNode(statements, start.Span.Through(endTok.Span));
    }

    ExpressionNode ParseInterpolatedString()
    {
        Token start = Current;
        Advance(); // skip InterpolatedStart

        List<ExpressionNode> parts = [];
        while (Current.Kind != TokenKind.InterpolatedEnd && !IsAtEnd)
        {
            if (Current.Kind == TokenKind.TextFragment)
            {
                Token frag = Current;
                Advance();
                parts.Add(new LiteralExpressionNode(frag with { Kind = TokenKind.TextLiteral }));
            }
            else if (Current.Kind == TokenKind.InterpolatedExprStart)
            {
                Advance(); // skip {
                ExpressionNode expr = ParseExpression();
                parts.Add(expr);
                if (Current.Kind == TokenKind.InterpolatedExprEnd)
                {
                    Advance(); // skip }
                }
            }
            else
            {
                break;
            }
        }

        if (Current.Kind == TokenKind.InterpolatedEnd)
        {
            Advance();
        }

        SourceSpan span = start.Span.Through(Previous.Span);
        return new InterpolatedStringNode(parts, span);
    }

    PatternNode ParsePattern()
    {
        switch (Current.Kind)
        {
            case TokenKind.OtherwiseKeyword:
            {
                Token token = Current;
                Advance();
                return new WildcardPatternNode(token);
            }

            case TokenKind.Underscore:
            {
                m_diagnostics.Error(CdxCodes.ExpectedPattern,
                    "Use 'otherwise' for the wildcard pattern; '_' is no longer a pattern",
                    Current.Span);
                Token token = Current;
                Advance();
                return new WildcardPatternNode(token);
            }

            case TokenKind.IntegerLiteral:
            case TokenKind.TextLiteral:
            case TokenKind.CharLiteral:
            case TokenKind.TrueKeyword:
            case TokenKind.FalseKeyword:
            {
                Token token = Current;
                Advance();
                return new LiteralPatternNode(token);
            }

            case TokenKind.TypeIdentifier:
            {
                Token ctor = Current;
                Advance();
                List<PatternNode> subPatterns = [];

                while (Current.Kind == TokenKind.LeftParen)
                {
                    Advance();
                    subPatterns.Add(ParsePattern());
                    Expect(TokenKind.RightParen);
                }

                if (subPatterns.Count > 0)
                {
                    return new ConstructorPatternNode(ctor, subPatterns, ctor.Span.Through(Previous.Span));
                }

                return new ConstructorPatternNode(ctor, subPatterns, ctor.Span);
            }

            case TokenKind.Identifier:
            {
                Token token = Current;
                if (token.Text == "_")
                {
                    m_diagnostics.Error(CdxCodes.ExpectedPattern,
                        "Use 'otherwise' for the wildcard pattern; '_' is no longer a pattern",
                        Current.Span);
                    Advance();
                    return new WildcardPatternNode(token);
                }
                Advance();
                return new VariablePatternNode(token);
            }

            default:
            {
                m_diagnostics.Error(CdxCodes.ExpectedPattern, $"Expected a pattern, found {Current.Kind}", Current.Span);
                Token err = Current;
                Advance();
                return new WildcardPatternNode(err);
            }
        }
    }

    HandleExpressionNode ParseHandleExpression()
    {
        Token withKw = Expect(TokenKind.WithKeyword);
        Token effectName = Expect(TokenKind.TypeIdentifier);
        ExpressionNode computation = ParseExpression();
        SkipNewlines();

        List<HandleClauseNode> clauses = [];
        while (Current.Kind == TokenKind.Identifier)
        {
            Token opName = Current;
            Advance();

            List<Token> parameters = [];
            while (Current.Kind == TokenKind.LeftParen)
            {
                Advance();
                Token param = Expect(TokenKind.Identifier);
                parameters.Add(param);
                Expect(TokenKind.RightParen);
            }

            Token resumeName;
            if (parameters.Count > 0)
            {
                resumeName = parameters[^1];
                parameters.RemoveAt(parameters.Count - 1);
            }
            else
            {
                m_diagnostics.Error(CdxCodes.HandleClauseMissingResume,
                    "Handle clause must have at least a resume parameter", opName.Span);
                resumeName = opName;
            }

            Expect(TokenKind.Equals);
            SkipNewlines();
            ExpressionNode body = ParseExpression();
            SkipNewlines();

            clauses.Add(new HandleClauseNode(opName, parameters, resumeName, body,
                opName.Span.Through(body.Span)));
        }

        if (clauses.Count == 0)
        {
            m_diagnostics.Error(CdxCodes.HandleMissingClauses,
                "Expected at least one handler clause", effectName.Span);
        }

        SourceSpan span = clauses.Count > 0
            ? withKw.Span.Through(clauses[^1].Span)
            : withKw.Span.Through(effectName.Span);
        return new HandleExpressionNode(computation, effectName, clauses, span);
    }
}
