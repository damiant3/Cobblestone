using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed partial class TypeChecker(DiagnosticBag diagnostics)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly Unifier m_unifier = new(diagnostics);
    TypeEnvironment m_env = TypeEnvironment.WithBuiltins();
    Map<string, CodexType> m_typeDefMap = Map<string, CodexType>.s_empty;
    Map<string, CtorInfo> m_ctorMap = Map<string, CtorInfo>.s_empty;
    Map<string, CodexType> m_typeParamEnv = Map<string, CodexType>.s_empty;
    Map<string, CodexType> m_typeLevelEnv = Map<string, CodexType>.s_empty;
    Map<string, EffectRowVariable> m_effectRowVars = Map<string, EffectRowVariable>.s_empty;
    Set<string> m_currentEffects = Set<string>.s_empty;
    Map<string, string> m_operationToEffect = Map<string, string>.s_empty;
    bool m_builtinEffectsRegistered;

    void EnsureBuiltinEffects()
    {
        if (m_builtinEffectsRegistered)
            return;
        m_builtinEffectsRegistered = true;
        RegisterEffectDefinitions(BuiltinEffects.Load());
    }

    public Map<string, CodexType> CheckChapter(Chapter chapter)
    {
        EnsureBuiltinEffects();
        RegisterTypeDefinitions(chapter.TypeDefinitions);
        RegisterEffectDefinitions(chapter.EffectDefs);

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
            m_unifier.ContextSpan = null;
            if (m_diagnostics.Count > errorsBefore)
                m_diagnostics.Info(CdxCodes.InDefinition,
                    $"in definition '{def.Name.Value}'", def.Span);
        }

        Map<string, CodexType> result = Map<string, CodexType>.s_empty;
        foreach (KeyValuePair<string, CodexType> kv in topLevelTypes)
        {
            CodexType t = m_unifier.DeepResolve(kv.Value);
            while (t is ForAllType fa)
                t = fa.Body;
            result = result.Set(kv.Key, t);
        }

        return result;
    }

    void RegisterTypeDefinitions(IReadOnlyList<TypeDef> typeDefs)
    {
        foreach (TypeDef td in typeDefs)
            switch (td)
            {
                case RecordTypeDef rec:
                    m_typeDefMap = m_typeDefMap.Set(rec.Name.Value, new ConstructedType(rec.Name, []));
                    break;
                case VariantTypeDef variant:
                    m_typeDefMap = m_typeDefMap.Set(variant.Name.Value, new ConstructedType(variant.Name, []));
                    break;
            }

        foreach (TypeDef td in typeDefs)
            if (td is RecordTypeDef rec)
                RegisterRecord(rec);

        foreach (TypeDef td in typeDefs)
            if (td is VariantTypeDef variant)
                RegisterVariant(variant);
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
        RecordType recordType = new(rec.Name, paramIds.ToImmutable(), fields.ToImmutable());
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
        SumType sumType = new(variant.Name, paramIds.ToImmutable(), ctors.ToImmutable());
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

    public void CiteChapter(Chapter chapter)
    {
        EnsureBuiltinEffects();
        RegisterTypeDefinitions(chapter.TypeDefinitions);
        RegisterEffectDefinitions(chapter.EffectDefs);

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
