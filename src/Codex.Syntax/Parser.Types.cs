namespace Codex.Syntax;

public sealed partial class Parser
{
    public TypeNode ParseType()
    {
        TypeNode left = ParseTypeAtom();

        if (Current.Kind == TokenKind.Arrow)
        {
            Advance();
            TypeNode right = ParseType();
            return new FunctionTypeNode(left, right, left.Span.Through(right.Span));
        }

        return left;
    }

    public TypeNode? TryParseType()
    {
        if (Current.Kind == TokenKind.EndOfFile)
            return null;
        return ParseType();
    }

    TypeNode ParseTypeAtom()
    {
        if (Current.Kind == TokenKind.LinearKeyword)
        {
            Token start = Current;
            Advance();
            TypeNode inner = ParseTypeAtom();
            return new LinearTypeNode(inner, start.Span.Through(inner.Span));
        }

        if (Current.Kind == TokenKind.LeftBrace)
        {
            Token start = Current;
            Advance();
            if ((Current.Kind == TokenKind.Identifier || Current.Kind == TokenKind.ProofKeyword)
                && Current.Text == "proof"
                && Peek(1)?.Kind == TokenKind.Colon)
            {
                Advance();
                Advance();
            }
            TypeNode left = ParseTypeAtom();
            Token op = Current;
            if (op.Kind is TokenKind.LessThan or TokenKind.GreaterThan
                or TokenKind.LessOrEqual or TokenKind.GreaterOrEqual)
            {
                Advance();
                TypeNode right = ParseTypeAtom();
                Expect(TokenKind.RightBrace);
                return new ProofConstraintNode(left, op, right, start.Span.Through(Previous.Span));
            }
            Expect(TokenKind.RightBrace);
            return new ProofConstraintNode(left, op, left, start.Span.Through(Previous.Span));
        }

        if (Current.Kind == TokenKind.LeftBracket)
        {
            Token start = Current;
            Advance();
            List<TypeNode> effects = [];
            while (Current.Kind != TokenKind.RightBracket && !IsAtEnd)
            {
                effects.Add(ParseTypeAtom());
                if (Current.Kind == TokenKind.Comma)
                {
                    Advance();
                }
            }
            Expect(TokenKind.RightBracket);
            TypeNode returnType = ParseType();
            return new EffectfulTypeNode(effects, returnType, start.Span.Through(returnType.Span));
        }

        if (Current.Kind == TokenKind.LeftParen)
        {
            Token start = Current;
            if (IsDependentTypeLookahead())
            {
                Advance();
                if (Current.Kind is not (TokenKind.Identifier or TokenKind.TypeIdentifier))
                {
                    TypeNode fallbackType = ParseType();
                    Expect(TokenKind.RightParen);
                    return new ParenthesizedTypeNode(fallbackType, start.Span.Through(Previous.Span));
                }
                Token paramName = Current;
                Advance();
                Expect(TokenKind.Colon);
                TypeNode paramType = ParseType();
                Expect(TokenKind.RightParen);
                Expect(TokenKind.Arrow);
                TypeNode body = ParseType();
                return new DependentTypeNode(paramName, paramType, body, start.Span.Through(body.Span));
            }
            Advance();
            TypeNode inner = ParseType();
            if (Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star)
            {
                Token op = Current;
                Advance();
                TypeNode right = ParseType();
                Expect(TokenKind.RightParen);
                return new BinaryTypeNode(inner, op, right, start.Span.Through(Previous.Span));
            }
            Expect(TokenKind.RightParen);
            return new ParenthesizedTypeNode(inner, start.Span.Through(Previous.Span));
        }

        if (Current.Kind == TokenKind.IntegerLiteral)
        {
            Token lit = Current;
            Advance();
            return new IntegerTypeNode(lit, lit.Span);
        }

        if (Current.Kind is TokenKind.TypeIdentifier or TokenKind.Identifier)
        {
            Token nameToken = Current;
            Advance();
            TypeNode baseType = new NamedTypeNode(nameToken);

            List<TypeNode> args = [];
            while (Current.Kind is TokenKind.TypeIdentifier or TokenKind.Identifier
                       or TokenKind.LeftParen or TokenKind.IntegerLiteral
                   && !IsAtEnd
                   && Current.Kind != TokenKind.Arrow
                   && Current.Kind != TokenKind.Newline
                   && Current.Kind != TokenKind.Equals
                   && Current.Kind != TokenKind.RightParen
                   && Current.Kind != TokenKind.Comma
                   && Current.Kind != TokenKind.TripleEquals)
            {
                args.Add(ParseTypeAtomSimple());
            }

            if (args.Count > 0)
            {
                return new ApplicationTypeNode(baseType, args, nameToken.Span.Through(args[^1].Span));
            }

            return baseType;
        }

        m_diagnostics.Error("CDX1010", $"Expected a type, found {Current.Kind}", Current.Span);
        Token errToken = Current;
        Advance();
        return new NamedTypeNode(errToken);
    }

    TypeNode ParseTypeAtomSimple()
    {
        if (Current.Kind == TokenKind.LeftParen)
        {
            Token start = Current;
            Advance();
            TypeNode inner = ParseType();
            if (Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star)
            {
                Token op = Current;
                Advance();
                TypeNode right = ParseType();
                Expect(TokenKind.RightParen);
                return new BinaryTypeNode(inner, op, right, start.Span.Through(Previous.Span));
            }
            Expect(TokenKind.RightParen);
            return new ParenthesizedTypeNode(inner, start.Span.Through(Previous.Span));
        }

        if (Current.Kind == TokenKind.IntegerLiteral)
        {
            Token lit = Current;
            Advance();
            return new IntegerTypeNode(lit, lit.Span);
        }

        if (Current.Kind is TokenKind.TypeIdentifier or TokenKind.Identifier)
        {
            Token nameToken = Current;
            Advance();
            return new NamedTypeNode(nameToken);
        }

        m_diagnostics.Error("CDX1010", $"Expected a type, found {Current.Kind}", Current.Span);
        Token errToken = Current;
        Advance();
        return new NamedTypeNode(errToken);
    }
}
