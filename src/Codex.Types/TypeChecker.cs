using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed partial class TypeChecker(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly Unifier m_unifier = new(diagnostics);
    TypeEnvironment m_env = new();
    Map<string, CodexType> m_typeDefMap = Map<string, CodexType>.s_empty;
    Map<string, CtorInfo> m_ctorMap = Map<string, CtorInfo>.s_empty;
    Map<string, CodexType> m_typeParamEnv = Map<string, CodexType>.s_empty;
    Map<string, CodexType> m_typeLevelEnv = Map<string, CodexType>.s_empty;
    Map<string, EffectRowVariable> m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
    Set<string> m_currentEffects = Set<string>.s_empty;
    Map<string, string> m_operationToEffect = Map<string, string>.s_empty;
    bool m_builtinEffectsRegistered;

    // Elaborated-AST table: every Expr encountered during inference has its
    // resolved type recorded here. Reference-equality keyed so that two
    // structurally-identical subtrees (e.g. two LiteralExpr(0, Integer) with
    // the same span) don't collide. After CheckChapter completes, every
    // entry has been DeepResolve'd so no substitution placeholders remain
    // in the exposed types.
    readonly Dictionary<Ast.Expr, CodexType> m_exprTypes = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyDictionary<Ast.Expr, CodexType> ExprTypes => m_exprTypes;

    const int MaxInferenceDepth = 256;
    int m_inferDepth;

    bool InferenceFuelExhausted(SourceSpan span)
    {
        if (m_inferDepth < MaxInferenceDepth)
            return false;
        m_diagnostics.Error(CdxCodes.ResourceExhausted,
            $"compiler resource exhausted in type-checker.InferExpr (budget {MaxInferenceDepth})",
            span);
        return true;
    }

    void EnsureBuiltinEffects()
    {
        if (m_builtinEffectsRegistered)
            return;
        m_builtinEffectsRegistered = true;
        RegisterEffectDefinitions(BuiltinEffects.Load());
    }

    void BindBuiltinChapterCitations(Chapter chapter)
    {
        foreach (CitesDecl cite in chapter.Citations)
        {
            if (cite.Quire.Value != "Codex") continue;
            BuiltinChapter? bc = BuiltinChapters.LookupByName(cite.ChapterName.Value);
            if (bc is null) continue; // NameResolver already diagnosed
            foreach ((string name, CodexType type) in bc.TypedBindings)
                m_env = m_env.Bind(name, type);
        }
    }

    public Map<string, CodexType> CheckChapter(Chapter chapter)
    {
        RegisterChapterMetadata(chapter, bindCitations: true);

        Map<string, CodexType> topLevelTypes = Map<string, CodexType>.s_empty;
        foreach (Definition def in chapter.Definitions)
        {
            Map<string, CodexType> savedTypeParams = m_typeParamEnv;
            m_typeParamEnv = Map<string, CodexType>.s_empty;
            m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
            CodexType declaredType = def.DeclaredType is not null
                ? ResolveTypeExpr(def.DeclaredType)
                : m_unifier.FreshVar();
            m_typeParamEnv = savedTypeParams;
            m_effectRowVars = Map<string, EffectRowVariable>.s_empty;

            CodexType envType = def.DeclaredType is not null
                ? Generalize(declaredType)
                : declaredType;
            topLevelTypes = topLevelTypes.Set(def.Name.Value, declaredType);
            m_env = m_env.Bind(def.Name, envType);
        }

        foreach (Definition def in chapter.Definitions)
        {
            CodexType expectedType = topLevelTypes[def.Name.Value]!;
            CodexType envType = m_env.Lookup(def.Name)!;
            CodexType checkType = envType is ForAllType
                ? Instantiate(envType)
                : expectedType;
            int errorsBefore = m_diagnostics.Count;
            m_unifier.ContextSpan = def.Span;
            CodexType bodyType = InferDefinition(def, checkType);
            m_unifier.Unify(checkType, bodyType, def.Span);
            // Tie the instantiation vars (fresh per-body) back to the rigid
            // signature vars so ExprTypes entries resolve to identifiers the
            // emitter recognizes as in-scope. Without this, polymorphic call
            // sites in the body retain instantiation-var IDs (T479 rather
            // than T26), which emit outside the enclosing generic context
            // and fail downstream CS0246 (M3 in REF-DRY-AUDIT.md).
            if (envType is ForAllType)
                m_unifier.Unify(checkType, expectedType, def.Span);
            m_unifier.ContextSpan = null;
            if (m_diagnostics.Count > errorsBefore)
            {
                m_diagnostics.Info(CdxCodes.InDefinition,
                    $"in definition '{def.Name.Value}'", def.Span);
            }
        }

        Map<string, CodexType> result = Map<string, CodexType>.s_empty;
        foreach (KeyValuePair<string, CodexType> kv in topLevelTypes)
        {
            CodexType t = m_unifier.DeepResolve(kv.Value);
            while (t is ForAllType fa)
                t = fa.Body;
            result = result.Set(kv.Key, t);
        }

        // Elaborated-AST finalization: DeepResolve every per-expression type
        // recorded during inference so the exposed table has concrete types
        // (no TypeVariable placeholders unless the type is genuinely
        // polymorphic, which ForAll-stripping at the top level already
        // handles for public-API entries).
        foreach (Ast.Expr key in m_exprTypes.Keys.ToList())
            m_exprTypes[key] = m_unifier.DeepResolve(m_exprTypes[key]);

        return result;
    }

    void RegisterTypeDefinitions(IReadOnlyList<TypeDef> typeDefs)
    {
        foreach (TypeDef td in typeDefs)
        {
            switch (td)
            {
                case RecordTypeDef rec:
                    m_typeDefMap = m_typeDefMap.Set(rec.Name.Value, new ConstructedType(rec.Name, []));
                    break;
                case VariantTypeDef variant:
                    m_typeDefMap = m_typeDefMap.Set(variant.Name.Value, new ConstructedType(variant.Name, []));
                    break;
            }
        }

        foreach (TypeDef td in typeDefs)
        {
            if (td is RecordTypeDef rec)
                RegisterRecord(rec);
        }

        foreach (TypeDef td in typeDefs)
        {
            if (td is VariantTypeDef variant)
                RegisterVariant(variant);
        }
    }

    void RegisterRecord(RecordTypeDef rec)
    {
        Map<string, CodexType> typeParamEnv = Map<string, CodexType>.s_empty;
        ImmutableArray<int>.Builder paramIds = ImmutableArray.CreateBuilder<int>();
        foreach (Name tp in rec.TypeParameters)
        {
            TypeVariable tv = m_unifier.FreshVar();
            typeParamEnv = typeParamEnv.Set(tp.Value, tv);
            paramIds.Add(tv.Id);
        }

        Map<string, CodexType> savedTypeParams = m_typeParamEnv;
        m_typeParamEnv = typeParamEnv;

        ImmutableArray<RecordFieldType>.Builder fields =
            ImmutableArray.CreateBuilder<RecordFieldType>();
        foreach (RecordFieldDef f in rec.Fields)
            fields.Add(new(f.FieldName, ResolveTypeExpr(f.Type)));
        ImmutableArray<int> paramIdsImm = paramIds.ToImmutable();
        RecordType recordType = new(rec.Name, paramIdsImm, fields.ToImmutable())
        {
            TypeArguments = [.. paramIdsImm.Select(id => (CodexType)new TypeVariable(id))]
        };
        m_typeDefMap = m_typeDefMap.Set(rec.Name.Value, recordType);

        m_typeParamEnv = savedTypeParams;
    }

    void RegisterVariant(VariantTypeDef variant)
    {
        Map<string, CodexType> typeParamEnv = Map<string, CodexType>.s_empty;
        ImmutableArray<int>.Builder paramIds = ImmutableArray.CreateBuilder<int>();
        foreach (Name tp in variant.TypeParameters)
        {
            TypeVariable tv = m_unifier.FreshVar();
            typeParamEnv = typeParamEnv.Set(tp.Value, tv);
            paramIds.Add(tv.Id);
        }

        Map<string, CodexType> savedTypeParams = m_typeParamEnv;
        m_typeParamEnv = typeParamEnv;

        ImmutableArray<SumConstructorType>.Builder ctors =
            ImmutableArray.CreateBuilder<SumConstructorType>();
        foreach (VariantCtorDef c in variant.Constructors)
        {
            ImmutableArray<CodexType>.Builder ctorFields =
                ImmutableArray.CreateBuilder<CodexType>();
            foreach (VariantFieldDef f in c.Fields)
                ctorFields.Add(ResolveTypeExpr(f.Type));
            ctors.Add(new(c.Name, ctorFields.ToImmutable()));
        }
        ImmutableArray<int> paramIdsImm = paramIds.ToImmutable();
        SumType sumType = new(variant.Name, paramIdsImm, ctors.ToImmutable())
        {
            TypeArguments = [.. paramIdsImm.Select(id => (CodexType)new TypeVariable(id))]
        };
        m_typeDefMap = m_typeDefMap.Set(variant.Name.Value, sumType);

        foreach (SumConstructorType ctor in sumType.Constructors)
        {
            CodexType ctorType = sumType;
            for (int i = ctor.Fields.Length - 1; i >= 0; i--)
                ctorType = new FunctionType(ctor.Fields[i], ctorType);
            for (int i = paramIds.Count - 1; i >= 0; i--)
                ctorType = new ForAllType(paramIds[i], ctorType);
            m_ctorMap = m_ctorMap.Set(ctor.Name.Value, new(ctorType, sumType));
            m_env = m_env.Bind(ctor.Name, ctorType);
        }

        m_typeParamEnv = savedTypeParams;
    }

    public Map<string, CodexType> TypeDefMap => m_typeDefMap;

    public Map<string, CtorInfo> ConstructorMap => m_ctorMap;

    void RegisterEffectDefinitions(IReadOnlyList<EffectDef> effectDefs)
    {
        foreach (EffectDef eff in effectDefs)
        {
            foreach (EffectOperationDef op in eff.Operations)
            {
                Map<string, CodexType> savedTypeParams = m_typeParamEnv;
                m_typeParamEnv = Map<string, CodexType>.s_empty;
                m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
                CodexType opType = ResolveTypeExpr(op.Type);
                CodexType generalizedType = Generalize(opType);
                m_typeParamEnv = savedTypeParams;
                m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
                m_env = m_env.Bind(op.Name, generalizedType);
                m_operationToEffect = m_operationToEffect.Set(op.Name.Value, eff.EffectName.Value);
            }
        }
    }

    public Map<string, string> OperationToEffect => m_operationToEffect;

    // Shared preamble for CheckChapter and CiteChapter. CheckChapter threads
    // the main chapter's `cites Codex chapter X` references onto m_env
    // (bindCitations: true) so the body-checking loop resolves builtin names;
    // CiteChapter is called for imported chapters, whose own citations do not
    // propagate to the enclosing chapter's env — only their type defs and
    // effect defs do.
    void RegisterChapterMetadata(Chapter chapter, bool bindCitations)
    {
        EnsureBuiltinEffects();
        if (bindCitations)
            BindBuiltinChapterCitations(chapter);
        RegisterTypeDefinitions(chapter.TypeDefinitions);
        RegisterEffectDefinitions(chapter.EffectDefs);
    }

    public void CiteChapter(Chapter chapter)
    {
        RegisterChapterMetadata(chapter, bindCitations: false);

        foreach (Definition def in chapter.Definitions)
        {

            Map<string, CodexType> savedTypeParams = m_typeParamEnv;
            m_typeParamEnv = Map<string, CodexType>.s_empty;
            m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
            CodexType declaredType = def.DeclaredType is not null
                ? ResolveTypeExpr(def.DeclaredType)
                : m_unifier.FreshVar();
            m_typeParamEnv = savedTypeParams;
            m_effectRowVars = Map<string, EffectRowVariable>.s_empty;

            CodexType envType = def.DeclaredType is not null
                ? Generalize(declaredType)
                : declaredType;
            m_env = m_env.Bind(def.Name, envType);
        }
    }
}

public sealed record CtorInfo(CodexType ConstructorType, CodexType OwnerType);
