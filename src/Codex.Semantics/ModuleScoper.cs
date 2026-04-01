using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public sealed class ModuleScoper(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;

    public Module Scope(IReadOnlyList<Module> perFileModules, string combinedName)
    {
        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<ImportDecl> allImports = [];
        List<ExportDecl> allExports = [];
        List<EffectDef> allEffectDefs = [];

        // Phase A: collect all names per module and detect collisions
        Dictionary<string, List<string>> nameToModules = [];
        foreach (Module mod in perFileModules)
        {
            string modName = mod.Name.Parts[^1].Value;
            foreach (Definition def in mod.Definitions)
            {
                string defName = def.Name.Value;
                if (!nameToModules.TryGetValue(defName, out List<string>? modules))
                {
                    modules = [];
                    nameToModules[defName] = modules;
                }
                modules.Add(modName);
            }
        }

        // Identify colliding names (appear in 2+ modules)
        HashSet<string> collidingNames = [];
        foreach (var kvp in nameToModules)
        {
            if (kvp.Value.Count > 1)
                collidingNames.Add(kvp.Key);
        }

        // Build selective import maps per module
        Dictionary<string, Dictionary<string, string>> moduleImportAliases = [];
        foreach (Module mod in perFileModules)
        {
            string modName = mod.Name.Parts[^1].Value;
            Dictionary<string, string> aliases = [];
            foreach (ImportDecl imp in mod.Imports)
            {
                if (imp.SelectedNames.Count > 0)
                {
                    string importedModSlug = ToSlug(imp.ModuleName.Value);
                    foreach (Name selected in imp.SelectedNames)
                    {
                        string mangledName = $"{importedModSlug}_{selected.Value}";
                        aliases[selected.Value] = mangledName;
                    }
                }
            }
            moduleImportAliases[modName] = aliases;
        }

        // Process each module: mangle colliding names, rewrite expressions
        foreach (Module mod in perFileModules)
        {
            string modName = mod.Name.Parts[^1].Value;
            string modSlug = ToSlug(modName);
            var importAliases = moduleImportAliases.GetValueOrDefault(modName)
                ?? new Dictionary<string, string>();

            // Build the rename map for this module
            Dictionary<string, string> renameMap = [];
            foreach (string colliding in collidingNames)
            {
                if (importAliases.TryGetValue(colliding, out string? aliased))
                {
                    // Selective import: use the imported module's mangled name
                    renameMap[colliding] = aliased;
                }
                else if (nameToModules[colliding].Contains(modName))
                {
                    // Own definition: mangle with own module slug
                    renameMap[colliding] = $"{modSlug}_{colliding}";
                }
                else
                {
                    // Not defined here and not imported — leave for NameResolver to catch
                }
            }

            foreach (Definition def in mod.Definitions)
            {
                string defName = def.Name.Value;
                string mangledName = collidingNames.Contains(defName)
                    ? $"{modSlug}_{defName}"
                    : defName;

                Definition renamed = def with
                {
                    Name = new Name(mangledName),
                    Body = RenameExpr(def.Body, renameMap)
                };
                allDefinitions.Add(renamed);
            }

            foreach (TypeDef td in mod.TypeDefinitions)
                allTypeDefinitions.Add(td);

            allClaims.AddRange(mod.Claims);
            allProofs.AddRange(mod.Proofs);
            allEffectDefs.AddRange(mod.EffectDefs);

            // Keep non-selective imports (external module imports) for NameResolver
            foreach (ImportDecl imp in mod.Imports)
            {
                if (imp.SelectedNames.Count == 0)
                    allImports.Add(imp);
            }
            allExports.AddRange(mod.Exports);
        }

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<combined>");

        return new Module(
            QualifiedName.Simple(combinedName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Imports = allImports,
            Exports = allExports,
            EffectDefs = allEffectDefs
        };
    }

    static string ToSlug(string moduleName)
    {
        // Convert "CodexEmitter" or "Codex Emitter" to "codex-emitter"
        var sb = new System.Text.StringBuilder(moduleName.Length + 4);
        for (int i = 0; i < moduleName.Length; i++)
        {
            char c = moduleName[i];
            if (c == ' ' || c == '_')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }
            else if (char.IsUpper(c))
            {
                // Insert hyphen before uppercase if preceded by lowercase
                if (i > 0 && char.IsLower(moduleName[i - 1]) && sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c) || c == '-')
            {
                sb.Append(c);
            }
        }
        // Trim trailing hyphens
        while (sb.Length > 0 && sb[^1] == '-')
            sb.Length--;
        return sb.ToString();
    }

    Expr RenameExpr(Expr expr, Dictionary<string, string> renameMap)
    {
        if (renameMap.Count == 0) return expr;

        return expr switch
        {
            NameExpr name => renameMap.TryGetValue(name.Name.Value, out string? mangled)
                ? name with { Name = new Name(mangled) }
                : name,

            LiteralExpr => expr,
            ErrorExpr => expr,

            BinaryExpr bin => bin with
            {
                Left = RenameExpr(bin.Left, renameMap),
                Right = RenameExpr(bin.Right, renameMap)
            },

            UnaryExpr un => un with
            {
                Operand = RenameExpr(un.Operand, renameMap)
            },

            ApplyExpr app => app with
            {
                Function = RenameExpr(app.Function, renameMap),
                Argument = RenameExpr(app.Argument, renameMap)
            },

            IfExpr iff => iff with
            {
                Condition = RenameExpr(iff.Condition, renameMap),
                Then = RenameExpr(iff.Then, renameMap),
                Else = RenameExpr(iff.Else, renameMap)
            },

            LetExpr let => let with
            {
                Bindings = let.Bindings.Select(b =>
                    new LetBinding(b.Name, RenameExpr(b.Value, renameMap))).ToList(),
                Body = RenameExpr(let.Body, renameMap)
            },

            LambdaExpr lam => lam with
            {
                Body = RenameExpr(lam.Body, renameMap)
            },

            MatchExpr match => match with
            {
                Scrutinee = RenameExpr(match.Scrutinee, renameMap),
                Branches = match.Branches.Select(b =>
                    new MatchBranch(
                        RenamePattern(b.Pattern, renameMap),
                        RenameExpr(b.Body, renameMap),
                        b.Span)).ToList()
            },

            ListExpr list => list with
            {
                Elements = list.Elements.Select(e => RenameExpr(e, renameMap)).ToList()
            },

            RecordExpr rec => rec with
            {
                Fields = rec.Fields.Select(f =>
                    new RecordFieldExpr(f.FieldName, RenameExpr(f.Value, renameMap), f.Span)).ToList()
            },

            FieldAccessExpr fa => fa with
            {
                Record = RenameExpr(fa.Record, renameMap)
            },

            DoExpr doExpr => doExpr with
            {
                Statements = doExpr.Statements.Select(s => RenameDoStatement(s, renameMap)).ToList()
            },

            HandleExpr handle => handle with
            {
                Computation = RenameExpr(handle.Computation, renameMap),
                Clauses = handle.Clauses.Select(c =>
                    new HandleClause(
                        c.OperationName,
                        c.Parameters,
                        c.ResumeName,
                        RenameExpr(c.Body, renameMap),
                        c.Span)).ToList()
            },

            _ => expr
        };
    }

    Pattern RenamePattern(Pattern pattern, Dictionary<string, string> renameMap)
    {
        return pattern switch
        {
            CtorPattern ctor => renameMap.TryGetValue(ctor.Constructor.Value, out string? mangled)
                ? ctor with
                {
                    Constructor = new Name(mangled),
                    SubPatterns = ctor.SubPatterns.Select(p => RenamePattern(p, renameMap)).ToList()
                }
                : ctor with
                {
                    SubPatterns = ctor.SubPatterns.Select(p => RenamePattern(p, renameMap)).ToList()
                },

            _ => pattern
        };
    }

    DoStatement RenameDoStatement(DoStatement stmt, Dictionary<string, string> renameMap)
    {
        return stmt switch
        {
            DoBindStatement bind => bind with { Value = RenameExpr(bind.Value, renameMap) },
            DoExprStatement exprStmt => exprStmt with { Expression = RenameExpr(exprStmt.Expression, renameMap) },
            _ => stmt
        };
    }
}
