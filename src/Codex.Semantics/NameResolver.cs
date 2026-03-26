using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public sealed record ResolvedModule(
    Module Module,
    Set<string> TopLevelNames,
    Set<string> TypeNames,
    Set<string> ConstructorNames,
    Set<string> ExportedNames)
{
    public IReadOnlyList<ResolvedModule> ImportedModules { get; init; } = [];
}

public sealed class NameResolver(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    IModuleLoader? m_loader;

    static readonly Set<string> s_builtins = Set<string>.Of(
        "show", "negate", "True", "False", "Nothing",
        "print-line", "read-line",
        "open-file", "read-all", "close-file", "read-file",
        "write-file", "file-exists", "list-files",
        "char-at", "char-to-text", "text-length", "substring",
        "is-letter", "is-digit", "is-whitespace",
        "text-to-integer", "integer-to-text", "text-replace",
        "text-split", "text-contains", "text-starts-with",
        "char-code", "char-code-at", "code-to-char",
        "list-length", "list-at", "list-insert-at", "list-snoc",
        "text-compare",
        "get-args", "get-env", "current-dir",
        "run-process",
        "map",
        "get-state", "set-state", "run-state",
        "now",
        "random-integer",
        "fork", "await", "par", "race"
    );

    public NameResolver(DiagnosticBag diagnostics, IModuleLoader? loader)
        : this(diagnostics)
    {
        m_loader = loader;
    }

    public ResolvedModule Resolve(Module module)
    {
        Set<string> topLevel = Set<string>.s_empty;
        foreach (Definition def in module.Definitions)
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

        foreach (TypeDef td in module.TypeDefinitions)
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
        foreach (EffectDef eff in module.EffectDefs)
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

        // Compute exported names: if no export declarations, export everything
        Set<string> exportedNames = ComputeExportedNames(
            module, topLevel, typeNames.Union(effectNames), ctorNames);

        List<ResolvedModule> importedModules = [];
        foreach (ImportDecl imp in module.Imports)
        {
            ResolvedModule? imported = m_loader?.Load(imp.ModuleName.Value);
            if (imported is null)
            {
                m_diagnostics.Error("CDX3010",
                    $"Cannot resolve import '{imp.ModuleName.Value}'",
                    imp.Span);
                continue;
            }
            importedModules.Add(imported);
            // Only import names that the other module exports
            topLevel = topLevel.Union(imported.ExportedNames.Intersect(imported.TopLevelNames));
            typeNames = typeNames.Union(imported.ExportedNames.Intersect(imported.TypeNames));
            ctorNames = ctorNames.Union(imported.ExportedNames.Intersect(imported.ConstructorNames));
        }

        Set<string> allKnownNames = topLevel
            .Union(s_builtins)
            .Union(ctorNames);

        foreach (Definition def in module.Definitions)
        {
            Set<string> scope = allKnownNames;
            foreach (Parameter p in def.Parameters)
                scope = scope.Add(p.Name.Value);
            ResolveExpr(def.Body, scope);
        }

        return new ResolvedModule(module, topLevel, typeNames, ctorNames, exportedNames)
            { ImportedModules = importedModules };
    }

    Set<string> ComputeExportedNames(
        Module module, Set<string> topLevel, Set<string> typeNames, Set<string> ctorNames)
    {
        if (module.Exports.Count == 0)
        {
            // No export declarations = export everything
            return topLevel.Union(typeNames).Union(ctorNames);
        }

        Set<string> exported = Set<string>.s_empty;
        Set<string> allDefined = topLevel.Union(typeNames).Union(ctorNames);
        foreach (ExportDecl exp in module.Exports)
        {
            foreach (Name n in exp.Names)
            {
                if (!allDefined.Contains(n.Value))
                {
                    m_diagnostics.Error("CDX3020",
                        $"Exported name '{n.Value}' is not defined in this module",
                        exp.Span);
                }
                exported = exported.Add(n.Value);
            }
        }
        return exported;
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
