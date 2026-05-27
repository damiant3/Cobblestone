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

    // Fuel budget for recursive descent over the AST. Matches the unifier's
    // 256. Exceeding it trips CDX9001 instead of hanging or overflowing the
    // .NET stack — either adversarial input depth or a bug in the walker.
    const int MaxRecursionDepth = 256;
    int m_depth;

    public static Set<string> Builtins => s_builtins;

    // Always-in-scope names: reserved (True/False/Nothing) + built-in effect op names
    // that are auto-registered at type-check time via BuiltinEffects.Load. Typed
    // builtins are NOT here — they must be brought in via `cites Codex chapter X`.
    // True / False / Nothing are lexer keywords now; they are never NameExpr values
    // and never need to be resolved as identifiers. They stay out of this list.
    static readonly Set<string> s_builtins = Set<string>.Of(
        "print-line", "read-line",
        "open-file", "read-all", "close-file", "read-file",
        "write-file", "write-binary",
        "get-args", "get-env", "current-dir", "run-process", "run-process-full", "process-exit",
        "now",
        "random-integer",
        "get-state", "set-state",
        "fetch", "post", "resolve-dns",
        "draw-text", "draw-rect", "clear", "set-pixel",
        "capture", "capture-raw",
        "listen", "is-quiet",
        "locate", "altitude",
        "accelerometer", "gyroscope", "barometer", "light-level",
        "authenticate", "current-user"
    );

    public NameResolver(DiagnosticBag diagnostics, IChapterLoader? loader)
        : this(diagnostics)
    {
        m_loader = loader;
    }

    static Codex.Types.BuiltinChapter? FindBuiltinChapter(string name)
    {
        foreach (Codex.Types.BuiltinChapter c in Codex.Types.BuiltinChapters.All)
            if (c.Name == name) return c;
        return null;
    }

    public ResolvedChapter Resolve(Chapter chapter)
    {
        Set<string> topLevel = Set<string>.s_empty;
        foreach (Definition def in chapter.Definitions)
        {
            if (topLevel.Contains(def.Name.Value))
            {
                m_diagnostics.Error(CdxCodes.DuplicateDefinition,
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
                m_diagnostics.Error(CdxCodes.DuplicateDefinition,
                    $"Duplicate type definition: '{td.Name.Value}' is already defined",
                    td.Span);
            }
            typeNames = typeNames.Add(td.Name.Value);

            if (td is VariantTypeDef variant)
            {
                foreach (VariantCtorDef ctor in variant.Constructors)
                {
                    if (ctorNames.Contains(ctor.Name.Value))
                    {
                        m_diagnostics.Error(CdxCodes.DuplicateDefinition,
                            $"Duplicate constructor: '{ctor.Name.Value}' is already defined",
                            ctor.Span);
                    }
                    ctorNames = ctorNames.Add(ctor.Name.Value);
                }
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
                    m_diagnostics.Error(CdxCodes.DuplicateDefinition,
                        $"Effect operation '{op.Name.Value}' conflicts with existing name",
                        op.Span);
                }
                topLevel = topLevel.Add(op.Name.Value);
            }
        }

        List<ResolvedChapter> citedChapters = [];
        foreach (CitesDecl cite in chapter.Citations)
        {
            // Codex quire = synthetic builtin chapters (no .codex file on disk).
            // Names come from BuiltinChapters; effect-op names are auto-registered
            // elsewhere (BuiltinEffects.Load) so they are not added here.
            if (cite.Quire.Value == "Codex")
            {
                Codex.Types.BuiltinChapter? bc = FindBuiltinChapter(cite.ChapterName.Value);
                if (bc is null)
                {
                    m_diagnostics.Error(CdxCodes.UnresolvedCitation,
                        $"Unknown builtin chapter 'Codex chapter {cite.ChapterName.Value}'",
                        cite.Span);
                    continue;
                }
                foreach ((string name, _) in bc.TypedBindings)
                    topLevel = topLevel.Add(name);

                // If the builtin chapter declares inline record/variant types
                // via TypeSource, parse them (lazily cached in BuiltinTypes)
                // and thread them through citedChapters so
                // TypeChecker.CiteChapter picks them up in the usual way.
                Chapter? typeChapter = Codex.Types.BuiltinTypes.ChapterFor(cite.ChapterName.Value);
                if (typeChapter is not null)
                {
                    Set<string> citedTypeNames = Set<string>.s_empty;
                    Set<string> citedCtorNames = Set<string>.s_empty;
                    foreach (TypeDef td in typeChapter.TypeDefinitions)
                    {
                        citedTypeNames = citedTypeNames.Add(td.Name.Value);
                        if (td is VariantTypeDef variant)
                        {
                            foreach (VariantCtorDef ctor in variant.Constructors)
                                citedCtorNames = citedCtorNames.Add(ctor.Name.Value);
                        }
                    }
                    typeNames = typeNames.Union(citedTypeNames);
                    ctorNames = ctorNames.Union(citedCtorNames);
                    citedChapters.Add(new ResolvedChapter(typeChapter,
                        Set<string>.s_empty, citedTypeNames, citedCtorNames));
                }
                continue;
            }

            ResolvedChapter? cited = m_loader?.Load(cite.Quire.Value, cite.ChapterName.Value);
            if (cited is null)
            {
                m_diagnostics.Error(CdxCodes.UnresolvedCitation,
                    $"Cannot resolve citation '{cite.Quire.Value} chapter {cite.ChapterName.Value}'",
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
        if (m_depth >= MaxRecursionDepth)
        {
            m_diagnostics.Error(CdxCodes.ResourceExhausted,
                $"compiler resource exhausted in name-resolver.ResolveExpr (depth {MaxRecursionDepth})",
                expr.Span);
            return;
        }
        m_depth++;
        try
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
                    m_diagnostics.Error(CdxCodes.UndefinedName, message, name.Span);
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
                    lamScope = lamScope.Add(p.Name.Value);
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

            case ActExpr actExpr:
            {
                Set<string> doScope = scope;
                foreach (ActStatement stmt in actExpr.Statements)
                {
                    switch (stmt)
                    {
                        case ActBindStatement bind:
                            ResolveExpr(bind.Value, doScope);
                            doScope = doScope.Add(bind.Name.Value);
                            break;
                        case ActExprStatement exprStmt:
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
        finally
        {
            m_depth--;
        }
    }

    void CollectPatternBindings(Pattern pattern, ref Set<string> scope)
    {
        if (m_depth >= MaxRecursionDepth)
        {
            m_diagnostics.Error(CdxCodes.ResourceExhausted,
                $"compiler resource exhausted in name-resolver.CollectPatternBindings (depth {MaxRecursionDepth})",
                pattern.Span);
            return;
        }
        m_depth++;
        try
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
        finally
        {
            m_depth--;
        }
    }

    static bool IsTypeName(Name name) => name.IsTypeName;
}
