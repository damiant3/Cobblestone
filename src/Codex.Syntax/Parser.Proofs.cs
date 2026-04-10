using Codex.Core;

namespace Codex.Syntax;

public sealed partial class Parser
{
    ClaimNode? TryParseClaim()
    {
        if (Current.Kind != TokenKind.ClaimKeyword)
            return null;

        Token claimKw = Current;
        Advance();

        Token name = Expect(TokenKind.Identifier);

        List<Token> parameters = [];
        while (Current.Kind == TokenKind.LeftParen)
        {
            Advance();
            parameters.Add(Expect(TokenKind.Identifier));
            Expect(TokenKind.RightParen);
        }

        Expect(TokenKind.Colon);
        TypeNode left = ParseType();
        Expect(TokenKind.TripleEquals);
        TypeNode right = ParseType();

        return new ClaimNode(name, parameters, left, right,
            claimKw.Span.Through(right.Span));
    }

    ProofNode? TryParseProof()
    {
        if (Current.Kind != TokenKind.ProofKeyword)
            return null;

        Token proofKw = Current;
        Advance();

        Token name = Expect(TokenKind.Identifier);

        List<Token> parameters = [];
        while (Current.Kind == TokenKind.LeftParen)
        {
            Advance();
            parameters.Add(Expect(TokenKind.Identifier));
            Expect(TokenKind.RightParen);
        }

        Expect(TokenKind.Equals);
        SkipNewlines();

        ProofExprNode body = ParseProofExpr();

        return new ProofNode(name, parameters, body,
            proofKw.Span.Through(body.Span));
    }

    ProofExprNode ParseProofExpr()
    {
        if (Current.Kind == TokenKind.TypeIdentifier && Current.Text == "Refl")
        {
            Token t = Current;
            Advance();
            return new ReflNode(t.Span);
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "assume")
        {
            Token t = Current;
            Advance();
            return new AssumeNode(t.Span);
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "sym")
        {
            Token t = Current;
            Advance();
            ProofExprNode inner = ParseProofExpr();
            return new SymNode(inner, t.Span.Through(inner.Span));
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "trans")
        {
            Token t = Current;
            Advance();
            ProofExprNode left = ParseProofSimpleAtom();
            ProofExprNode right = ParseProofSimpleAtom();
            return new TransNode(left, right, t.Span.Through(right.Span));
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "cong")
        {
            Token t = Current;
            Advance();
            Token funcName = Current;
            Advance();
            ProofExprNode inner = ParseProofExpr();
            return new CongNode(funcName, inner, t.Span.Through(inner.Span));
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "induction")
        {
            Token t = Current;
            Advance();
            Token variable = Current;
            Advance();
            SkipNewlines();

            List<ProofCaseNode> cases = [];
            while (Current.Kind == TokenKind.IfKeyword)
            {
                Advance();
                SkipNewlines();
                PatternNode pattern = ParsePattern();
                Expect(TokenKind.Arrow);
                SkipNewlines();
                ProofExprNode caseBody = ParseProofExpr();
                SourceSpan caseSpan = pattern.Span.Through(caseBody.Span);
                cases.Add(new ProofCaseNode(pattern, caseBody, caseSpan));
                SkipNewlines();
            }

            SourceSpan endSpan = cases.Count > 0 ? cases[^1].Span : variable.Span;
            return new InductionNode(variable, cases, t.Span.Through(endSpan));
        }

        return ParseProofAtom();
    }

    ProofExprNode ParseProofAtom()
    {
        if (Current.Kind == TokenKind.TypeIdentifier && Current.Text == "Refl")
        {
            Token t = Current;
            Advance();
            return new ReflNode(t.Span);
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "assume")
        {
            Token t = Current;
            Advance();
            return new AssumeNode(t.Span);
        }

        if (Current.Kind == TokenKind.LeftParen)
        {
            Advance();
            ProofExprNode inner = ParseProofExpr();
            Expect(TokenKind.RightParen);
            return inner;
        }

        if (Current.Kind is TokenKind.Identifier or TokenKind.TypeIdentifier)
        {
            Token name = Current;
            Advance();

            List<ExpressionNode> args = [];
            while (Current.Kind is TokenKind.Identifier or TokenKind.TypeIdentifier
                   or TokenKind.IntegerLiteral or TokenKind.LeftParen)
            {
                if (Current.Kind == TokenKind.LeftParen)
                {
                    Advance();
                    args.Add(ParseExpression());
                    Token rp = Expect(TokenKind.RightParen);
                    if (rp.Kind != TokenKind.RightParen)
                        break;
                }
                else
                {
                    Token argTok = Current;
                    Advance();
                    args.Add(new NameExpressionNode(argTok));
                }
            }

            if (args.Count > 0)
                return new ProofApplyNode(name, args, name.Span.Through(args[^1].Span));

            return new ProofNameNode(name, name.Span);
        }

        m_diagnostics.Error("CDX1020", $"Expected a proof expression, found {Current.Kind}", Current.Span);
        Token err = Current;
        Advance();
        return new ReflNode(err.Span);
    }

    ProofExprNode ParseProofSimpleAtom()
    {
        if (Current.Kind == TokenKind.TypeIdentifier && Current.Text == "Refl")
        {
            Token t = Current;
            Advance();
            return new ReflNode(t.Span);
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == "assume")
        {
            Token t = Current;
            Advance();
            return new AssumeNode(t.Span);
        }

        if (Current.Kind == TokenKind.LeftParen)
        {
            Advance();
            ProofExprNode inner = ParseProofExpr();
            Expect(TokenKind.RightParen);
            return inner;
        }

        if (Current.Kind is TokenKind.Identifier or TokenKind.TypeIdentifier)
        {
            Token name = Current;
            Advance();
            return new ProofNameNode(name, name.Span);
        }

        m_diagnostics.Error("CDX1020", $"Expected a proof expression, found {Current.Kind}", Current.Span);
        Token err = Current;
        Advance();
        return new ReflNode(err.Span);
    }
}
