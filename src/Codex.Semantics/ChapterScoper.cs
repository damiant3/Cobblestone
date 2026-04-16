using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public sealed class ChapterScoper(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;

    public Chapter Scope(IReadOnlyList<Chapter> perFileChapters, string combinedName)
    {
        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<CitesDecl> allCitations = [];
        List<EffectDef> allEffectDefs = [];

        // Phase A: collect all names per module and detect collisions
        Dictionary<string, List<string>> nameToModules = [];
        foreach (Chapter mod in perFileChapters)
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
        foreach (KeyValuePair<string, List<string>> kvp in nameToModules)
        {
            if (kvp.Value.Count > 1)
            {
                collidingNames.Add(kvp.Key);
            }
        }

        // A chapter's identity is (quire, chapter-title). Same chapter title
        // in different quires produces a different slug because the quire
        // participates. "--" is the separator: self-host's tokenizer splits it
        // into two Minus tokens but collect-title-tokens (option-2 alnum-join)
        // re-serializes adjacent punctuation without a space, so the round
        // trip survives byte-identically.
        static string SlugFor(Chapter c)
        {
            string name = c.Name.Parts[^1].Value;
            return c.Quire is null ? ToSlug(name) : ToSlug(c.Quire) + "--" + ToSlug(name);
        }
        static string SlugForCite(CitesDecl cite) =>
            ToSlug(cite.Quire.Value) + "--" + ToSlug(cite.ChapterName.Value);

        // Build selective cite maps per chapter (keyed by per-file chapter slug).
        Dictionary<string, Dictionary<string, string>> chapterCiteAliases = [];
        foreach (Chapter mod in perFileChapters)
        {
            string modKey = SlugFor(mod);
            Dictionary<string, string> aliases = [];
            foreach (CitesDecl cite in mod.Citations)
            {
                if (cite.SelectedNames.Count > 0)
                {
                    string importedModSlug = SlugForCite(cite);
                    foreach (Name selected in cite.SelectedNames)
                    {
                        string mangledName = $"{importedModSlug}_{selected.Value}";
                        aliases[selected.Value] = mangledName;
                    }
                }
            }
            chapterCiteAliases[modKey] = aliases;
        }

        // Process each module: mangle colliding names, rewrite expressions
        foreach (Chapter mod in perFileChapters)
        {
            string modName = mod.Name.Parts[^1].Value;
            string modSlug = SlugFor(mod);
            Dictionary<string, string> citeAliases = chapterCiteAliases.GetValueOrDefault(modSlug)
                ?? new Dictionary<string, string>();

            // Build the rename map for this module
            Dictionary<string, string> renameMap = [];
            foreach (string colliding in collidingNames)
            {
                if (citeAliases.TryGetValue(colliding, out string? aliased))
                {
                    // Selective cite: use the cited chapter's mangled name
                    renameMap[colliding] = aliased;
                }
                else if (nameToModules[colliding].Contains(modName))
                {
                    // Own definition: mangle with own module slug
                    renameMap[colliding] = $"{modSlug}_{colliding}";
                }
                else
                {
                    // Not defined here and not cited — leave for NameResolver to catch
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
            {
                allTypeDefinitions.Add(td);
            }

            allClaims.AddRange(mod.Claims);
            allProofs.AddRange(mod.Proofs);
            allEffectDefs.AddRange(mod.EffectDefs);

            // Keep non-selective citations for NameResolver
            foreach (CitesDecl cite in mod.Citations)
            {
                if (cite.SelectedNames.Count == 0)
                {
                    allCitations.Add(cite);
                }
            }
        }

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<combined>");

        // Merge prose maps from all per-file chapters
        Dictionary<string, ChapterProse> mergedProse = [];
        foreach (Chapter mod in perFileChapters)
        {
            foreach (KeyValuePair<string, ChapterProse> kvp in mod.ProseByFile)
            {
                mergedProse[kvp.Key] = kvp.Value;
            }
        }

        return new Chapter(
            QualifiedName.Simple(combinedName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Citations = allCitations,
            EffectDefs = allEffectDefs,
            ProseByFile = mergedProse
        };
    }

    static string ToSlug(string chapterName)
    {
        // Convert "CodexEmitter" or "Codex Emitter" to "codex-emitter"
        System.Text.StringBuilder sb = new System.Text.StringBuilder(chapterName.Length + 4);
        for (int i = 0; i < chapterName.Length; i++)
        {
            char c = chapterName[i];
            if (c == ' ' || c == '_')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }
            else if (char.IsUpper(c))
            {
                // Insert hyphen before uppercase if preceded by lowercase
                if (i > 0 && char.IsLower(chapterName[i - 1]) && sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c) || c == '-')
            {
                sb.Append(c);
            }
        }
        // Trim trailing hyphens
        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    Expr RenameExpr(Expr expr, Dictionary<string, string> renameMap)
    {
        if (renameMap.Count == 0)
        {
            return expr;
        }

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

            LetExpr let => RenameLetExpr(let, renameMap),

            LambdaExpr lam => RenameLambdaExpr(lam, renameMap),

            MatchExpr match => match with
            {
                Scrutinee = RenameExpr(match.Scrutinee, renameMap),
                Branches = match.Branches.Select(b => RenameMatchBranch(b, renameMap)).ToList()
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

            ActExpr actExpr => RenameDoExpr(actExpr, renameMap),

            HandleExpr handle => RenameHandleExpr(handle, renameMap),

            _ => expr
        };
    }

    // Remove locally-bound names from the rename map so shadows aren't touched
    static Dictionary<string, string> WithoutKeys(
        Dictionary<string, string> map, IEnumerable<string> keys)
    {
        Dictionary<string, string>? reduced = null;
        foreach (string key in keys)
        {
            if (map.ContainsKey(key))
            {
                reduced ??= new Dictionary<string, string>(map);
                reduced.Remove(key);
            }
        }
        return reduced ?? map;
    }

    Expr RenameLetExpr(LetExpr let, Dictionary<string, string> renameMap)
    {
        // Each binding's value is renamed with the map BEFORE that binding's name
        // is in scope. After all bindings, the body uses a map with bound names removed.
        List<LetBinding> bindings = [];
        Dictionary<string, string> bodyMap = renameMap;
        foreach (LetBinding b in let.Bindings)
        {
            bindings.Add(new LetBinding(b.Name, RenameExpr(b.Value, bodyMap)));
            if (renameMap.ContainsKey(b.Name.Value))
            {
                bodyMap = WithoutKeys(bodyMap, [b.Name.Value]);
            }
        }
        return let with
        {
            Bindings = bindings,
            Body = RenameExpr(let.Body, bodyMap)
        };
    }

    Expr RenameLambdaExpr(LambdaExpr lam, Dictionary<string, string> renameMap)
    {
        Dictionary<string, string> bodyMap = WithoutKeys(renameMap,
            lam.Parameters.Select(p => p.Name.Value));
        return lam with { Body = RenameExpr(lam.Body, bodyMap) };
    }

    MatchBranch RenameMatchBranch(MatchBranch branch, Dictionary<string, string> renameMap)
    {
        // Collect all variable names bound by the pattern
        List<string> patternVars = [];
        CollectPatternVars(branch.Pattern, patternVars);
        Dictionary<string, string> bodyMap = WithoutKeys(renameMap, patternVars);
        return new MatchBranch(
            RenamePattern(branch.Pattern, renameMap),
            RenameExpr(branch.Body, bodyMap),
            branch.Span);
    }

    static void CollectPatternVars(Pattern pattern, List<string> vars)
    {
        switch (pattern)
        {
            case VarPattern v:
                vars.Add(v.Name.Value);
                break;
            case CtorPattern ctor:
                foreach (Pattern sub in ctor.SubPatterns)
                {
                    CollectPatternVars(sub, vars);
                }

                break;
        }
    }

    Expr RenameDoExpr(ActExpr actExpr, Dictionary<string, string> renameMap)
    {
        // Each bind statement introduces a name that shadows in subsequent statements
        List<ActStatement> stmts = [];
        Dictionary<string, string> currentMap = renameMap;
        foreach (ActStatement stmt in actExpr.Statements)
        {
            stmts.Add(RenameActStatement(stmt, currentMap));
            if (stmt is ActBindStatement bind && renameMap.ContainsKey(bind.Name.Value))
            {
                currentMap = WithoutKeys(currentMap, [bind.Name.Value]);
            }
        }
        return actExpr with { Statements = stmts };
    }

    Expr RenameHandleExpr(HandleExpr handle, Dictionary<string, string> renameMap)
    {
        return handle with
        {
            Computation = RenameExpr(handle.Computation, renameMap),
            Clauses = handle.Clauses.Select(c =>
            {
                Dictionary<string, string> clauseMap = WithoutKeys(renameMap,
                    c.Parameters.Select(p => p.Value).Append(c.ResumeName.Value));
                return new HandleClause(
                    c.OperationName,
                    c.Parameters,
                    c.ResumeName,
                    RenameExpr(c.Body, clauseMap),
                    c.Span);
            }).ToList()
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

    ActStatement RenameActStatement(ActStatement stmt, Dictionary<string, string> renameMap)
    {
        return stmt switch
        {
            ActBindStatement bind => bind with { Value = RenameExpr(bind.Value, renameMap) },
            ActExprStatement exprStmt => exprStmt with { Expression = RenameExpr(exprStmt.Expression, renameMap) },
            _ => stmt
        };
    }
}
