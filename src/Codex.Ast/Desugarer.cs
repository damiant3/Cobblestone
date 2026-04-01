using Codex.Core;
using Codex.Syntax;

namespace Codex.Ast;

public sealed class Desugarer(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    string? m_currentModuleName;

    public Module Desugar(DocumentNode document, string moduleName)
    {
        m_currentModuleName = moduleName;
        QualifiedName name = QualifiedName.Simple(moduleName);
        List<Definition> definitions = document.Definitions.Select(DesugarDefinition).ToList();
        List<TypeDef> typeDefinitions = document.TypeDefinitions.Select(DesugarTypeDefinition).ToList();
        List<ClaimDef> claims = document.Claims.Select(DesugarClaim).ToList();
        List<ProofDef> proofs = document.Proofs.Select(DesugarProof).ToList();
        List<ImportDecl> imports = document.Imports
            .Select(i => new ImportDecl(new Name(i.Name.Text), i.Span)
                { SelectedNames = i.SelectedNames.Select(n => new Name(n.Text)).ToList() })
            .ToList();
        List<ExportDecl> exports = document.Exports
            .Select(e => new ExportDecl(
                e.Names.Select(n => new Name(n.Text)).ToList(), e.Span)).ToList();
        List<EffectDef> effectDefs = document.EffectDefinitions
            .Select(DesugarEffectDef).ToList();
        return new Module(name, definitions, typeDefinitions, claims, proofs, document.Span)
            { Imports = imports, Exports = exports, EffectDefs = effectDefs };
    }

    EffectDef DesugarEffectDef(EffectDefinitionNode node)
    {
        Name effectName = new(node.Name.Text);
        List<EffectOperationDef> ops = node.Operations
            .Select(o => new EffectOperationDef(
                new Name(o.Name.Text), DesugarType(o.Type), o.Span)).ToList();
        return new EffectDef(effectName, ops, node.Span);
    }

    Definition DesugarDefinition(DefinitionNode node)
    {
        Name name = new Name(node.Name.Text);
        List<Parameter> parameters = node.Parameters.Select(p =>
            new Parameter(new Name(p.Text), null, p.Span)).ToList();
        TypeExpr? declaredType = node.TypeAnnotation is not null
            ? DesugarType(node.TypeAnnotation.Type)
            : null;
        Expr body = DesugarExpr(node.Body);

        return new Definition(name, parameters, declaredType, body, node.Span)
            { SourceModule = m_currentModuleName };
    }

    Expr DesugarExpr(ExpressionNode node) => node switch
    {
        LiteralExpressionNode lit => new LiteralExpr(
            lit.Literal.LiteralValue ?? lit.Literal.Text,
            lit.Literal.Kind switch
            {
                TokenKind.IntegerLiteral => LiteralKind.Integer,
                TokenKind.NumberLiteral => LiteralKind.Number,
                TokenKind.TextLiteral => LiteralKind.Text,
                TokenKind.CharLiteral => LiteralKind.Char,
                TokenKind.TrueKeyword or TokenKind.FalseKeyword => LiteralKind.Boolean,
                _ => LiteralKind.Text
            },
            lit.Span),

        NameExpressionNode name => new NameExpr(new Name(name.Name.Text), name.Span),

        ApplicationExpressionNode app => new ApplyExpr(
            DesugarExpr(app.Function),
            DesugarExpr(app.Argument),
            app.Span),

        BinaryExpressionNode bin => new BinaryExpr(
            DesugarExpr(bin.Left),
            DesugarBinaryOp(bin.Operator.Kind),
            DesugarExpr(bin.Right),
            bin.Span),

        UnaryExpressionNode un => new UnaryExpr(
            UnaryOp.Negate,
            DesugarExpr(un.Operand),
            un.Span),

        IfExpressionNode iff => new IfExpr(
            DesugarExpr(iff.Condition),
            DesugarExpr(iff.Then),
            DesugarExpr(iff.Else),
            iff.Span),

        LetExpressionNode let => new LetExpr(
            let.Bindings.Select(b =>
                new Ast.LetBinding(new Name(b.Name.Text), DesugarExpr(b.Value))).ToList(),
            DesugarExpr(let.Body),
            let.Span),

        LambdaExpressionNode lam => new LambdaExpr(
            lam.Parameters.Select(p =>
                new Parameter(new Name(p.Text), null, p.Span)).ToList(),
            DesugarExpr(lam.Body),
            lam.Span),

        MatchExpressionNode m => new MatchExpr(
            DesugarExpr(m.Scrutinee),
            m.Branches.Select(b => new MatchBranch(
                DesugarPattern(b.Pattern),
                DesugarExpr(b.Body),
                b.Span)).ToList(),
            m.Span),

        ListExpressionNode list => new ListExpr(
            list.Elements.Select(DesugarExpr).ToList(),
            list.Span),

        RecordExpressionNode rec => new RecordExpr(
            rec.TypeName is not null ? new Name(rec.TypeName.Text) : null,
            rec.Fields.Select(f => new RecordFieldExpr(
                new Name(f.Name.Text),
                DesugarExpr(f.Value),
                f.Span)).ToList(),
            rec.Span),

        FieldAccessExpressionNode fa => new FieldAccessExpr(
            DesugarExpr(fa.Record),
            new Name(fa.FieldName.Text),
            fa.Span),

        ParenthesizedExpressionNode paren => DesugarExpr(paren.Inner),

        DoExpressionNode doExpr => new DoExpr(
            doExpr.Statements.Select(DesugarDoStatement).ToList(),
            doExpr.Span),

        HandleExpressionNode handle => new HandleExpr(
            DesugarExpr(handle.Computation),
            new Name(handle.EffectName.Text),
            handle.Clauses.Select(DesugarHandleClause).ToList(),
            handle.Span),

        InterpolatedStringNode interp => DesugarInterpolatedString(interp),

        ErrorExpressionNode err => new ErrorExpr("parse error", err.Span),

        _ => new ErrorExpr($"unknown expression node: {node.Kind}", node.Span)
    };

    DoStatement DesugarDoStatement(DoStatementNode node) => node switch
    {
        DoBindStatementNode bind => new DoBindStatement(
            new Name(bind.Name.Text), DesugarExpr(bind.Value), bind.Span),
        DoExprStatementNode expr => new DoExprStatement(DesugarExpr(expr.Expression), expr.Span),
        _ => new DoExprStatement(new ErrorExpr("unknown do statement", node.Span), node.Span)
    };

    HandleClause DesugarHandleClause(HandleClauseNode node) =>
        new(new Name(node.OperationName.Text),
            node.Parameters.Select(p => new Name(p.Text)).ToList(),
            new Name(node.ResumeName.Text),
            DesugarExpr(node.Body),
            node.Span);

    Expr DesugarInterpolatedString(InterpolatedStringNode node)
    {
        if (node.Parts.Count == 0)
        {
            return new LiteralExpr("", LiteralKind.Text, node.Span);
        }

        if (node.Parts.Count == 1 && node.Parts[0] is LiteralExpressionNode singleLit
            && singleLit.Literal.Kind == TokenKind.TextLiteral)
        {
            return new LiteralExpr(
                singleLit.Literal.LiteralValue ?? singleLit.Literal.Text,
                LiteralKind.Text, node.Span);
        }

        Expr result = DesugarInterpolationPart(node.Parts[0], node.Span);
        for (int i = 1; i < node.Parts.Count; i++)
        {
            Expr right = DesugarInterpolationPart(node.Parts[i], node.Span);
            result = new BinaryExpr(result, BinaryOp.Append, right, node.Span);
        }
        return result;
    }

    Expr DesugarInterpolationPart(ExpressionNode part, SourceSpan fallbackSpan)
    {
        if (part is LiteralExpressionNode lit && lit.Literal.Kind == TokenKind.TextLiteral)
        {
            return new LiteralExpr(
                lit.Literal.LiteralValue ?? lit.Literal.Text,
                LiteralKind.Text, lit.Span);
        }
        Expr inner = DesugarExpr(part);
        return new ApplyExpr(
            new NameExpr(new Name("show"), fallbackSpan),
            inner,
            fallbackSpan);
    }

    Pattern DesugarPattern(PatternNode node) => node switch
    {
        VariablePatternNode v => new VarPattern(new Name(v.Name.Text), v.Span),
        LiteralPatternNode l => new LiteralPattern(
            l.Literal.LiteralValue ?? l.Literal.Text,
            l.Literal.Kind switch
            {
                TokenKind.IntegerLiteral => LiteralKind.Integer,
                TokenKind.TextLiteral => LiteralKind.Text,
                TokenKind.TrueKeyword or TokenKind.FalseKeyword => LiteralKind.Boolean,
                _ => LiteralKind.Text
            },
            l.Span),
        ConstructorPatternNode c => new CtorPattern(
            new Name(c.Constructor.Text),
            c.SubPatterns.Select(DesugarPattern).ToList(),
            c.Span),
        WildcardPatternNode w => new WildcardPattern(w.Span),
        _ => new WildcardPattern(node.Span)
    };

    TypeExpr DesugarType(TypeNode node) => node switch
    {
        NamedTypeNode n => new NamedTypeExpr(new Name(n.Name.Text), n.Span),
        FunctionTypeNode f => new FunctionTypeExpr(
            DesugarType(f.Parameter),
            DesugarType(f.Return),
            f.Span),
        ApplicationTypeNode a => new AppliedTypeExpr(
            DesugarType(a.Constructor),
            a.Arguments.Select(DesugarType).ToList(),
            a.Span),
        ParenthesizedTypeNode p => DesugarType(p.Inner),
        EffectfulTypeNode e => new EffectfulTypeExpr(
            e.Effects.Select(DesugarType).ToList(),
            DesugarType(e.Return),
            e.Span),
        LinearTypeNode l => new LinearTypeExpr(
            DesugarType(l.Inner),
            l.Span),
        DependentTypeNode d => new DependentTypeExpr(
            new Name(d.ParamName.Text),
            DesugarType(d.ParamType),
            DesugarType(d.Body),
            d.Span),
        IntegerTypeNode i => new IntegerLiteralTypeExpr(
            Convert.ToInt64(i.Literal.LiteralValue ?? 0),
            i.Span),
        BinaryTypeNode b => new BinaryTypeExpr(
            DesugarType(b.Left),
            DesugarBinaryOp(b.Operator.Kind),
            DesugarType(b.Right),
            b.Span),
        ProofConstraintNode p => new ProofConstraintExpr(
            DesugarType(p.Left),
            DesugarBinaryOp(p.Operator.Kind),
            DesugarType(p.Right),
            p.Span),
        _ => new NamedTypeExpr(new Name("?"), node.Span)
    };

    TypeDef DesugarTypeDefinition(TypeDefinitionNode node)
    {
        Name typeName = new Name(node.Name.Text);
        List<Name> typeParams = node.TypeParameters.Select(t => new Name(t.Text)).ToList();

        TypeDef td = node.Body switch
        {
            RecordTypeBody rec => new RecordTypeDef(
                typeName,
                typeParams,
                rec.Fields.Select(f => new RecordFieldDef(
                    new Name(f.Name.Text),
                    DesugarType(f.Type),
                    f.Span)).ToList(),
                node.Span),

            VariantTypeBody variant => new VariantTypeDef(
                typeName,
                typeParams,
                variant.Constructors.Select(c => new VariantCtorDef(
                    new Name(c.Name.Text),
                    c.Fields.Select(f => new VariantFieldDef(
                        f.FieldName is not null ? new Name(f.FieldName.Text) : null,
                        DesugarType(f.Type),
                        f.Span)).ToList(),
                    c.Span)).ToList(),
                node.Span),

            ErrorTypeBody => new RecordTypeDef(
                typeName,
                typeParams,
                [],
                node.Span),

            _ => throw new InvalidOperationException($"Unknown type definition body: {node.Body.GetType().Name}")
        };
        return td with { SourceModule = m_currentModuleName };
    }

    static BinaryOp DesugarBinaryOp(TokenKind kind) => kind switch
    {
        TokenKind.Plus => BinaryOp.Add,
        TokenKind.Minus => BinaryOp.Sub,
        TokenKind.Star => BinaryOp.Mul,
        TokenKind.Slash => BinaryOp.Div,
        TokenKind.Caret => BinaryOp.Pow,
        TokenKind.DoubleEquals => BinaryOp.Eq,
        TokenKind.NotEquals => BinaryOp.NotEq,
        TokenKind.LessThan => BinaryOp.Lt,
        TokenKind.GreaterThan => BinaryOp.Gt,
        TokenKind.LessOrEqual => BinaryOp.LtEq,
        TokenKind.GreaterOrEqual => BinaryOp.GtEq,
        TokenKind.TripleEquals => BinaryOp.DefEq,
        TokenKind.PlusPlus => BinaryOp.Append,
        TokenKind.ColonColon => BinaryOp.Cons,
        TokenKind.Ampersand => BinaryOp.And,
        TokenKind.Pipe => BinaryOp.Or,
        _ => BinaryOp.Add
    };

    ClaimDef DesugarClaim(ClaimNode node)
    {
        Name name = new(node.Name.Text);
        List<Parameter> parameters = node.Parameters.Select(p =>
            new Parameter(new Name(p.Text), null, p.Span)).ToList();
        TypeExpr left = DesugarType(node.Left);
        TypeExpr right = DesugarType(node.Right);
        return new ClaimDef(name, parameters, left, right, node.Span);
    }

    ProofDef DesugarProof(ProofNode node)
    {
        Name name = new(node.Name.Text);
        List<Parameter> parameters = node.Parameters.Select(p =>
            new Parameter(new Name(p.Text), null, p.Span)).ToList();
        ProofExpr body = DesugarProofExpr(node.Body);
        return new ProofDef(name, parameters, body, node.Span);
    }

    ProofExpr DesugarProofExpr(ProofExprNode node) => node switch
    {
        ReflNode r => new ReflProofExpr(r.Span),
        AssumeNode a => new AssumeProofExpr(a.Span),
        SymNode s => new SymProofExpr(DesugarProofExpr(s.Inner), s.Span),
        TransNode t => new TransProofExpr(
            DesugarProofExpr(t.Left), DesugarProofExpr(t.Right), t.Span),
        CongNode c => new CongProofExpr(
            new Name(c.FunctionName.Text), DesugarProofExpr(c.Inner), c.Span),
        InductionNode ind => new InductionProofExpr(
            new Name(ind.Variable.Text),
            ind.Cases.Select(c => new ProofCase(
                DesugarPattern(c.Pattern),
                DesugarProofExpr(c.Body),
                c.Span)).ToList(),
            ind.Span),
        ProofNameNode n => new NameProofExpr(new Name(n.Name.Text), n.Span),
        ProofApplyNode a => new ApplyProofExpr(
            new Name(a.LemmaName.Text),
            a.Arguments.Select(DesugarExpr).ToList(),
            a.Span),
        _ => new ReflProofExpr(node.Span)
    };
}
