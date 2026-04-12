using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public sealed record ResolvedChapter(
    Chapter Chapter,
    Set<string> TopLevelNames,
    Set<string> TypeNames,
    Set<string> ConstructorNames)
{
    public IReadOnlyList<ResolvedChapter> CitedChapters { get; init; } = [];
}

public sealed class NameResolver(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    IChapterLoader? m_loader;

    static readonly Set<string> s_builtins = Set<string>.Of(
        "show", "negate", "True", "False", "Nothing",
        "print-line", "read-line",
        "open-file", "read-all", "close-file", "read-file",
        "write-file", "write-binary", "file-exists", "list-files",
        "char-at", "char-to-text", "text-length", "substring",
        "is-letter", "is-digit", "is-whitespace",
        "text-to-integer", "text-to-double-bits", "integer-to-text", "text-replace",
        "text-split", "text-contains", "text-starts-with",
        "char-code", "char-code-at", "code-to-char",
        "list-length", "list-at", "list-insert-at", "list-snoc", "list-contains",
        "text-compare", "text-concat-list",
        "get-args", "get-env", "current-dir",
        "run-process",
        "map",
        "get-state", "set-state", "run-state",
        "now",
        "random-integer",
        "fork", "await", "par", "race",
        "record-set",
        "linked-list-empty", "linked-list-push", "linked-list-to-list",
        "heap-save", "heap-restore", "heap-advance",
        "list-with-capacity",
        "buf-write-byte", "buf-write-bytes", "buf-read-bytes",
        "bit-and", "bit-or", "bit-xor", "bit-shl", "bit-shr", "bit-not",
        "int-mod", "abs", "min", "max"
    );

    public NameResolver(DiagnosticBag diagnostics, IChapterLoader? loader)
        : this(diagnostics)
    {
        m_loader = loader;
    }

    public ResolvedChapter Resolve(Chapter chapter)
    {
        Set<string> topLevel = Set<string>.s_empty;
        foreach (Definition def in chapter.Definitions)
        {
            if (topLevel.Contains(def.Name.Value))
            {
                m_diagnostics.Error("CDX3001",
                    $"Duplicate definition: '{def.Name.Value}' is already defined",
                    def.Span);
            }
            topLevel = topLevel.Add(def.Name.Value);
        }

        Set<string> typeNames = Set<string>.s_empty;
        Set<string> ctorNames = Set<string>.s_empty;

        foreach (TypeDef td in chapter.TypeDefinitions)
        {
            if (typeNames.Contains(td.Name.Value))
            {
                m_diagnostics.Error("CDX3001",
                    $"Duplicate type definition: '{td.Name.Value}' is already defined",
                    td.Span);
            }
            typeNames = typeNames.Add(td.Name.Value);

            if (td is VariantTypeDef variant)
                foreach (VariantCtorDef ctor in variant.Constructors)
                {
                    if (ctorNames.Contains(ctor.Name.Value))
                    {
                        m_diagnostics.Error("CDX3001",
                            $"Duplicate constructor: '{ctor.Name.Value}' is already defined",
                            ctor.Span);
                    }
                    ctorNames = ctorNames.Add(ctor.Name.Value);
                }
        }

        // Register effect operation names
        Set<string> effectNames = Set<string>.s_empty;
        foreach (EffectDef eff in chapter.EffectDefs)
        {
            effectNames = effectNames.Add(eff.EffectName.Value);
            foreach (EffectOperationDef op in eff.Operations)
            {
                if (topLevel.Contains(op.Name.Value) || ctorNames.Contains(op.Name.Value))
                {
                    m_diagnostics.Error("CDX3001",
                        $"Effect operation '{op.Name.Value}' conflicts with existing name",
                        op.Span);
                }
                topLevel = topLevel.Add(op.Name.Value);
            }
        }

        List<ResolvedChapter> citedChapters = [];
        foreach (CitesDecl cite in chapter.Citations)
        {
            ResolvedChapter? cited = m_loader?.Load(cite.ChapterName.Value);
            if (cited is null)
            {
                m_diagnostics.Error("CDX3010",
                    $"Cannot resolve citation '{cite.ChapterName.Value}'",
                    cite.Span);
                continue;
            }
            citedChapters.Add(cited);
            topLevel = topLevel.Union(cited.TopLevelNames);
            typeNames = typeNames.Union(cited.TypeNames);
            ctorNames = ctorNames.Union(cited.ConstructorNames);
        }

        Set<string> allKnownNames = topLevel
            .Union(s_builtins)
            .Union(ctorNames);

        foreach (Definition def in chapter.Definitions)
        {
            Set<string> scope = allKnownNames;
            foreach (Parameter p in def.Parameters)
                scope = scope.Add(p.Name.Value);
            ResolveExpr(def.Body, scope);
        }

        return new ResolvedChapter(chapter, topLevel, typeNames, ctorNames)
            { CitedChapters = citedChapters };
    }

    void ResolveExpr(Expr expr, Set<string> scope)
    {
        switch (expr)
        {
            case NameExpr name:
                if (!scope.Contains(name.Name.Value) && !IsTypeName(name.Name))
                {
                    string? suggestion = StringDistance.FindClosest(name.Name.Value, scope);
                    string message = suggestion is not null
                        ? $"Undefined name: '{name.Name.Value}'. Did you mean '{suggestion}'?"
                        : $"Undefined name: '{name.Name.Value}'";
                    m_diagnostics.Error("CDX3002", message, name.Span);
                }
                break;

            case LiteralExpr:
                break;

            case BinaryExpr bin:
                ResolveExpr(bin.Left, scope);
                ResolveExpr(bin.Right, scope);
                break;

            case UnaryExpr un:
                ResolveExpr(un.Operand, scope);
                break;

            case ApplyExpr app:
                ResolveExpr(app.Function, scope);
                ResolveExpr(app.Argument, scope);
                break;

            case IfExpr iff:
                ResolveExpr(iff.Condition, scope);
                ResolveExpr(iff.Then, scope);
                ResolveExpr(iff.Else, scope);
                break;

            case LetExpr let:
                Set<string> letScope = scope;
                foreach (LetBinding binding in let.Bindings)
                {
                    ResolveExpr(binding.Value, letScope);
                    letScope = letScope.Add(binding.Name.Value);
                }
                ResolveExpr(let.Body, letScope);
                break;

            case LambdaExpr lam:
                Set<string> lamScope = scope;
                foreach (Parameter p in lam.Parameters)
                {
                    lamScope = lamScope.Add(p.Name.Value);
                }
                ResolveExpr(lam.Body, lamScope);
                break;

            case MatchExpr match:
                ResolveExpr(match.Scrutinee, scope);
                foreach (MatchBranch branch in match.Branches)
                {
                    Set<string> branchScope = scope;
                    CollectPatternBindings(branch.Pattern, ref branchScope);
                    ResolveExpr(branch.Body, branchScope);
                }
                break;

            case ListExpr list:
                foreach (Expr element in list.Elements)
                    ResolveExpr(element, scope);
                break;

            case RecordExpr rec:
                foreach (RecordFieldExpr field in rec.Fields)
                    ResolveExpr(field.Value, scope);
                break;

            case FieldAccessExpr fa:
                ResolveExpr(fa.Record, scope);
                break;

            case DoExpr doExpr:
            {
                Set<string> doScope = scope;
                foreach (DoStatement stmt in doExpr.Statements)
                {
                    switch (stmt)
                    {
                        case DoBindStatement bind:
                            ResolveExpr(bind.Value, doScope);
                            doScope = doScope.Add(bind.Name.Value);
                            break;
                        case DoExprStatement exprStmt:
                            ResolveExpr(exprStmt.Expression, doScope);
                            break;
                    }
                }
                break;
            }

            case HandleExpr handleExpr:
            {
                ResolveExpr(handleExpr.Computation, scope);
                foreach (HandleClause clause in handleExpr.Clauses)
                {
                    Set<string> clauseScope = scope;
                    foreach (Name p in clause.Parameters)
                        clauseScope = clauseScope.Add(p.Value);
                    clauseScope = clauseScope.Add(clause.ResumeName.Value);
                    ResolveExpr(clause.Body, clauseScope);
                }
                break;
            }

            case ErrorExpr:
                break;
        }
    }

    static void CollectPatternBindings(Pattern pattern, ref Set<string> scope)
    {
        switch (pattern)
        {
            case VarPattern v:
                scope = scope.Add(v.Name.Value);
                break;
            case CtorPattern ctor:
                foreach (Pattern sub in ctor.SubPatterns)
                    CollectPatternBindings(sub, ref scope);
                break;
            case WildcardPattern:
            case LiteralPattern:
                break;
        }
    }

    static bool IsTypeName(Name name) => name.IsTypeName;
}
