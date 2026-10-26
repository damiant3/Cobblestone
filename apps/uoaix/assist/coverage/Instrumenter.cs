// The coverage instrument (docs/Designs/Active/Apps/AssistTesting.md, instruments): a copy of the Assist's sources with
// a probe on every statement (line coverage), on every branch arm (if and else, each case and a missing default, each
// ?: and switch-expression arm, the ?? fallback, a foreach over nothing and over something, each expression body) and
// on every operand of && and || seen true and seen false (condition coverage). An arm it cannot probe is listed as
// unmeasured, never dropped.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Uoaix.Assist.Coverage;

public sealed record Probe(int Id, string File, int Line, string Kind);

public static class Instrumenter
{
    const string CovType = "global::Uoaix.Assist.Coverage.Cov";

    // Writes the instrumented copy of each source into outDir and probes.tsv beside them; the compile errors that stop
    // it, or an empty list.
    public static List<string> Run(string[] sources, string[] references, string outDir)
    {
        List<SyntaxTree> trees = new();
        CSharpParseOptions parse = new(LanguageVersion.Preview);
        foreach (string path in sources)
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), parse, path));
        List<MetadataReference> refs = new();
        foreach (string r in references)
            refs.Add(MetadataReference.CreateFromFile(r));
        CSharpCompilation compilation = CSharpCompilation.Create("assist-coverage", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        List<string> errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        if (errors.Count > 0)
            return errors;
        Directory.CreateDirectory(outDir);
        List<Probe> probes = new();
        foreach (SyntaxTree tree in trees)
        {
            Rewriter rewriter = new(compilation.GetSemanticModel(tree), Path.GetFileName(tree.FilePath), probes);
            SyntaxNode result = rewriter.Visit(tree.GetRoot());
            // A probe can weaken a nullable annotation (Pass<TValue> for TValue?); warnings change no code, and
            // Add-Type fails on any warning, so the copy carries none.
            File.WriteAllText(Path.Combine(outDir, Path.GetFileName(tree.FilePath)), "#pragma warning disable\r\n" + result.ToFullString());
        }
        File.WriteAllLines(Path.Combine(outDir, "probes.tsv"),
            probes.Select(p => p.Id + "\t" + p.File + "\t" + p.Line + "\t" + p.Kind));
        return new List<string>();
    }

    sealed class Rewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel m_model;
        readonly string m_file;
        readonly List<Probe> m_probes;

        public Rewriter(SemanticModel model, string file, List<Probe> probes)
        {
            m_model = model;
            m_file = file;
            m_probes = probes;
        }

        int New(SyntaxNode at, string kind)
        {
            int id = m_probes.Count;
            m_probes.Add(new Probe(id, m_file, at.GetLocation().GetLineSpan().StartLinePosition.Line + 1, kind));
            return id;
        }

        void Unmeasured(SyntaxNode at, string why)
        {
            New(at, "unmeasured " + why);
        }

        static StatementSyntax Hit(int id) => SyntaxFactory.ParseStatement(CovType + ".Hit(" + id + ");");

        static ExpressionSyntax Call(string method, string? typeArg, params ExpressionSyntax[] args)
        {
            string generic = typeArg is null ? "" : "<" + typeArg + ">";
            ArgumentListSyntax list = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args.Select(SyntaxFactory.Argument)));
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(CovType + "." + method + generic), list);
        }

        static SyntaxToken Keyword(SyntaxKind kind) => SyntaxFactory.Token(kind).WithTrailingTrivia(SyntaxFactory.Space);

        static SyntaxToken Semicolon => SyntaxFactory.Token(SyntaxKind.SemicolonToken);

        static ExpressionSyntax Id(int id) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(id));

        // A type a probe can name: not an error, not anonymous, not ref-like, not a pointer.
        static string? Nameable(ITypeSymbol? type)
        {
            if (type is null || type.TypeKind == TypeKind.Error || type.IsAnonymousType || type.IsRefLikeType || type.TypeKind == TypeKind.Pointer)
                return null;
            if (type is INamedTypeSymbol named && named.TypeArguments.Any(a => Nameable(a) is null))
                return null;
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // A pattern or out variable declared inside: wrapping it in a call ends its definite assignment.
        static bool Declares(SyntaxNode node) => node.DescendantNodesAndSelf().Any(n =>
            n is DeclarationPatternSyntax or DeclarationExpressionSyntax or SingleVariableDesignationSyntax);

        BlockSyntax Armed(StatementSyntax visited, StatementSyntax original, int arm)
        {
            if (visited is BlockSyntax block)
                return block.WithStatements(block.Statements.Insert(0, Hit(arm)));
            return SyntaxFactory.Block(Hit(arm), Hit(New(original, "line")), visited);
        }

        BlockSyntax Embedded(StatementSyntax visited, StatementSyntax original)
        {
            if (visited is BlockSyntax block)
                return block;
            return SyntaxFactory.Block(Hit(New(original, "line")), visited);
        }

        SyntaxList<StatementSyntax> Lined(SyntaxList<StatementSyntax> visited, SyntaxList<StatementSyntax> original)
        {
            List<StatementSyntax> list = new();
            for (int i = 0; i < visited.Count; i++)
            {
                list.Add(Hit(New(original[i], "line")));
                list.Add(visited[i]);
            }
            return SyntaxFactory.List(list);
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            BlockSyntax visited = (BlockSyntax)base.VisitBlock(node)!;
            return visited.WithStatements(Lined(visited.Statements, node.Statements));
        }

        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            IfStatementSyntax visited = (IfStatementSyntax)base.VisitIfStatement(node)!;
            visited = visited.WithStatement(Armed(visited.Statement, node.Statement, New(node, "arm then")));
            if (visited.Else is null)
                return visited.WithElse(SyntaxFactory.ElseClause(SyntaxFactory.Block(Hit(New(node, "arm else")))));
            return visited.WithElse(visited.Else.WithStatement(Armed(visited.Else.Statement, node.Else!.Statement, New(node.Else, "arm else"))));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            WhileStatementSyntax visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return visited.WithCondition(Condition(visited.Condition, node.Condition)).WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            DoStatementSyntax visited = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return visited.WithCondition(Condition(visited.Condition, node.Condition)).WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            ForStatementSyntax visited = (ForStatementSyntax)base.VisitForStatement(node)!;
            if (visited.Condition is not null)
                visited = visited.WithCondition(Condition(visited.Condition, node.Condition!));
            return visited.WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            ForEachStatementSyntax visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            visited = visited.WithStatement(Embedded(visited.Statement, node.Statement));
            ForEachStatementInfo info = m_model.GetForEachStatementInfo(node);
            string? element = Nameable(info.ElementType);
            ITypeSymbol? source = m_model.GetTypeInfo(node.Expression).Type;
            if (element is null || source is null || source.IsRefLikeType || node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) || node.Type is RefTypeSyntax)
            {
                Unmeasured(node, "foreach empty and non-empty");
                return visited;
            }
            bool generic = source is IArrayTypeSymbol
                || source.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                || source.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
            bool plain = source.SpecialType == SpecialType.System_Collections_IEnumerable
                || source.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
            if (!generic && !plain)
            {
                Unmeasured(node, "foreach over a GetEnumerator pattern");
                return visited;
            }
            ExpressionSyntax wrapped = Call("Each", generic ? element : null, Id(New(node, "arm foreach empty")), Id(New(node, "arm foreach some")), visited.Expression);
            return visited.WithExpression(wrapped);
        }

        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node)
        {
            UsingStatementSyntax visited = (UsingStatementSyntax)base.VisitUsingStatement(node)!;
            return visited.WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitLockStatement(LockStatementSyntax node)
        {
            LockStatementSyntax visited = (LockStatementSyntax)base.VisitLockStatement(node)!;
            return visited.WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node)
        {
            FixedStatementSyntax visited = (FixedStatementSyntax)base.VisitFixedStatement(node)!;
            return visited.WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
        {
            LabeledStatementSyntax visited = (LabeledStatementSyntax)base.VisitLabeledStatement(node)!;
            return visited.WithStatement(Embedded(visited.Statement, node.Statement));
        }

        public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node)
        {
            CatchClauseSyntax visited = (CatchClauseSyntax)base.VisitCatchClause(node)!;
            return visited.WithBlock(visited.Block.WithStatements(visited.Block.Statements.Insert(0, Hit(New(node, "arm catch")))));
        }

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
        {
            SwitchStatementSyntax visited = (SwitchStatementSyntax)base.VisitSwitchStatement(node)!;
            bool hasDefault = node.Sections.Any(s => s.Labels.Any(l => l is DefaultSwitchLabelSyntax
                || l is CasePatternSwitchLabelSyntax { Pattern: DiscardPatternSyntax or VarPatternSyntax, WhenClause: null }));
            if (hasDefault)
                return visited;
            SwitchSectionSyntax none = SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.DefaultSwitchLabel()),
                SyntaxFactory.List(new[] { Hit(New(node, "arm no case")), SyntaxFactory.BreakStatement() }));
            return visited.WithSections(visited.Sections.Add(none));
        }

        public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
        {
            SwitchSectionSyntax visited = (SwitchSectionSyntax)base.VisitSwitchSection(node)!;
            SyntaxList<StatementSyntax> lined = Lined(visited.Statements, node.Statements);
            return visited.WithStatements(lined.Insert(0, Hit(New(node, "arm case"))));
        }

        // An operand probed true and false when it is a plain bool that declares nothing.
        ExpressionSyntax Condition(ExpressionSyntax visited, ExpressionSyntax original)
        {
            ExpressionSyntax bare = original;
            while (bare is ParenthesizedExpressionSyntax p)
                bare = p.Expression;
            // A constant (while (true)) decides nothing, and the compiler's reachability depends on seeing it.
            if (bare.IsKind(SyntaxKind.LogicalAndExpression) || bare.IsKind(SyntaxKind.LogicalOrExpression) || m_model.GetConstantValue(original).HasValue)
                return visited;
            TypeInfo info = m_model.GetTypeInfo(original);
            if (info.Type?.SpecialType != SpecialType.System_Boolean)
            {
                Unmeasured(original, "condition");
                return visited;
            }
            int whenTrue = New(original, "cond true");
            int whenFalse = New(original, "cond false");
            if (!Declares(original))
                return Call("Cond", null, Id(whenTrue), Id(whenFalse), visited);
            ExpressionSyntax trueArm = SyntaxFactory.ParseExpression("(" + CovType + ".Mark(" + whenTrue + ") || true)");
            ExpressionSyntax falseArm = SyntaxFactory.ParseExpression("(" + CovType + ".Mark(" + whenFalse + ") && false)");
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(SyntaxFactory.ParenthesizedExpression(visited), trueArm, falseArm));
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            BinaryExpressionSyntax visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
            if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
            {
                IMethodSymbol? op = m_model.GetSymbolInfo(node).Symbol as IMethodSymbol;
                if (op is not null && op.MethodKind == MethodKind.UserDefinedOperator)
                {
                    Unmeasured(node, "user-defined && or ||");
                    return visited;
                }
                return visited.WithLeft(Condition(visited.Left, node.Left)).WithRight(Condition(visited.Right, node.Right));
            }
            if (node.IsKind(SyntaxKind.CoalesceExpression))
            {
                string? type = Nameable(m_model.GetTypeInfo(node).ConvertedType ?? m_model.GetTypeInfo(node).Type);
                if (type is null)
                {
                    Unmeasured(node, "?? fallback");
                    return visited;
                }
                return visited.WithRight(Arm(visited.Right, node.Right, type, "arm ?? fallback"));
            }
            return visited;
        }

        ExpressionSyntax Arm(ExpressionSyntax visited, ExpressionSyntax original, string type, string kind)
        {
            int id = New(original, kind);
            if (visited is ThrowExpressionSyntax t)
                return Call("Throw", type, Id(id), t.Expression);
            return Call("Pass", type, Id(id), visited);
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            ConditionalExpressionSyntax visited = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;
            TypeInfo info = m_model.GetTypeInfo(node);
            string? type = Nameable(info.ConvertedType ?? info.Type);
            if (type is null || node.WhenTrue is RefExpressionSyntax)
            {
                Unmeasured(node, "?: arms");
                return visited;
            }
            return visited.WithWhenTrue(Arm(visited.WhenTrue, node.WhenTrue, type, "arm ?: true"))
                .WithWhenFalse(Arm(visited.WhenFalse, node.WhenFalse, type, "arm ?: false"));
        }

        public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            SwitchExpressionSyntax visited = (SwitchExpressionSyntax)base.VisitSwitchExpression(node)!;
            TypeInfo info = m_model.GetTypeInfo(node);
            string? type = Nameable(info.ConvertedType ?? info.Type);
            if (type is null)
            {
                Unmeasured(node, "switch expression arms");
                return visited;
            }
            List<SwitchExpressionArmSyntax> arms = new();
            for (int i = 0; i < visited.Arms.Count; i++)
                arms.Add(visited.Arms[i].WithExpression(Arm(visited.Arms[i].Expression, node.Arms[i].Expression, type, "arm switch")));
            return visited.WithArms(SyntaxFactory.SeparatedList(arms, visited.Arms.GetSeparators()));
        }

        // An expression body: the void ones become a block, the others pass their value through a probe.
        SyntaxNode Body(SyntaxNode visited, SyntaxNode original, ExpressionSyntax body, IMethodSymbol? method, Func<BlockSyntax, SyntaxNode> toBlock, Func<ExpressionSyntax, SyntaxNode> toArrow)
        {
            if (method is null || method.ReturnsByRef || method.ReturnsByRefReadonly)
            {
                Unmeasured(original, "expression body");
                return visited;
            }
            int id = New(original, "line expression body");
            // A body with no value to return: void, or async over a plain Task or ValueTask.
            bool valueless = method.ReturnsVoid || method.IsAsync && method.ReturnType is INamedTypeSymbol { Arity: 0 };
            if (body is ThrowExpressionSyntax thrown && (valueless || method.IsAsync))
                return toBlock(SyntaxFactory.Block(Hit(id), SyntaxFactory.ThrowStatement(Keyword(SyntaxKind.ThrowKeyword), thrown.Expression, Semicolon)));
            if (valueless)
                return toBlock(SyntaxFactory.Block(Hit(id), SyntaxFactory.ExpressionStatement(body)));
            if (method.IsAsync)
                return toBlock(SyntaxFactory.Block(Hit(id), SyntaxFactory.ReturnStatement(Keyword(SyntaxKind.ReturnKeyword), body, Semicolon)));
            string? type = Nameable(method.ReturnType);
            if (type is null)
            {
                Unmeasured(original, "expression body type");
                return visited;
            }
            ExpressionSyntax passed = body is ThrowExpressionSyntax th ? Call("Throw", type, Id(id), th.Expression) : Call("Pass", type, Id(id), body);
            return toArrow(passed);
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            ParenthesizedLambdaExpressionSyntax visited = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            if (IsExpressionTree(node))
            {
                Unmeasured(node, "expression tree lambda");
                return visited;
            }
            IMethodSymbol? method = m_model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            return Body(visited, node, visited.ExpressionBody, method,
                b => visited.WithExpressionBody(null).WithBlock(b),
                e => visited.WithExpressionBody(e));
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            SimpleLambdaExpressionSyntax visited = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            if (IsExpressionTree(node))
            {
                Unmeasured(node, "expression tree lambda");
                return visited;
            }
            IMethodSymbol? method = m_model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            return Body(visited, node, visited.ExpressionBody, method,
                b => visited.WithExpressionBody(null).WithBlock(b),
                e => visited.WithExpressionBody(e));
        }

        bool IsExpressionTree(LambdaExpressionSyntax node)
        {
            ITypeSymbol? target = m_model.GetTypeInfo(node).ConvertedType;
            return target is not null && target.Name == "Expression" && target.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions";
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            MethodDeclarationSyntax visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IMethodSymbol? method = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, method,
                b => visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(b),
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            LocalFunctionStatementSyntax visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IMethodSymbol? method = m_model.GetDeclaredSymbol(node) as IMethodSymbol;
            return Body(visited, node, visited.ExpressionBody.Expression, method,
                b => visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(b),
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            ConstructorDeclarationSyntax visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IMethodSymbol? method = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, method,
                b => visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(b),
                e => visited);
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            AccessorDeclarationSyntax visited = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IMethodSymbol? method = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, method,
                b => visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(b),
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            PropertyDeclarationSyntax visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IPropertySymbol? property = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, property?.GetMethod,
                b => visited,
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            IndexerDeclarationSyntax visited = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IPropertySymbol? indexer = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, indexer?.GetMethod,
                b => visited,
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            OperatorDeclarationSyntax visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node)!;
            if (visited.ExpressionBody is null)
                return visited;
            IMethodSymbol? method = m_model.GetDeclaredSymbol(node);
            return Body(visited, node, visited.ExpressionBody.Expression, method,
                b => visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(b),
                e => visited.WithExpressionBody(visited.ExpressionBody.WithExpression(e)));
        }
    }
}
