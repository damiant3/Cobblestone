using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed partial class TypeChecker
{
    static CodexType InstantiateParametricType(
        CodexType typeDef, ImmutableArray<CodexType> args)
    {
        ImmutableArray<int> paramIds = typeDef switch
        {
            SumType s => s.TypeParamIds,
            RecordType r => r.TypeParamIds,
            _ => []
        };

        CodexType result = typeDef;
        int count = Math.Min(paramIds.Length, args.Length);
        for (int i = 0; i < count; i++)
            result = SubstituteVar(result, paramIds[i], args[i]);
        // Record the concrete type arguments on the result so emitters can
        // render `Name<arg1, arg2>` for record fields and function signatures.
        // Substitution baked args into constructor/field types but lost the
        // applied-to information.
        return result switch
        {
            SumType s when args.Length > 0 => s with { TypeArguments = args },
            RecordType r when args.Length > 0 => r with { TypeArguments = args },
            _ => result
        };
    }

    CodexType Instantiate(CodexType type)
    {
        while (type is ForAllType forAll)
        {
            if (ContainsEffectRowVar(forAll.Body, forAll.VariableId))
            {
                EffectRowVariable fresh = m_unifier.FreshEffectVar();
                type = SubstituteEffectRowVar(
                    forAll.Body, forAll.VariableId, fresh);
            }
            else
            {
                TypeVariable fresh = m_unifier.FreshVar();
                type = SubstituteVar(forAll.Body, forAll.VariableId, fresh);
            }
        }
        return type;
    }

    static bool ContainsEffectRowVar(CodexType type, int varId)
        => new EffectRowVarContainsFolder(varId).Fold(type);

    sealed class EffectRowVarContainsFolder(int varId) : CodexTypeFolder<bool>
    {
        protected override bool Zero => false;
        protected override bool Combine(bool a, bool b) => a || b;

        public override bool Fold(CodexType type) => type switch
        {
            EffectRowVariable erv => erv.Id == varId,
            EffectfulType eft => (eft.RowVariable is not null && eft.RowVariable.Id == varId)
                || Fold(eft.Return),
            FunctionType or ListType or LinkedListType or ForAllType or DependentFunctionType
                => base.Fold(type),
            _ => false
        };
    }

    static CodexType Generalize(CodexType type)
    {
        HashSet<int> freeVars = [];
        CollectFreeTypeVars(type, freeVars);
        CodexType result = type;
        foreach (int varId in freeVars)
            result = new ForAllType(varId, result);
        return result;
    }

    static void CollectFreeTypeVars(CodexType type, HashSet<int> vars)
        => new FreeVarsWalker(vars).Visit(type);

    sealed class FreeVarsWalker(HashSet<int> vars) : CodexTypeWalker
    {
        public override void Visit(CodexType type)
        {
            switch (type)
            {
                case TypeVariable tv:
                    vars.Add(tv.Id);
                    break;
                case ForAllType fa:
                    Visit(fa.Body);
                    vars.Remove(fa.VariableId);
                    break;
                case EffectfulType eft:
                    foreach (EffectType e in eft.Effects) Visit(e);
                    Visit(eft.Return);
                    break;
                case SumType or RecordType or LinearType or TypeLevelBinary
                    or ProofType or LessThanClaim:
                    break;
                default:
                    base.Visit(type);
                    break;
            }
        }
    }

    static Set<string> ExtractEffects(CodexType type)
    {
        EffectfulType? eft = CodexTypeHelpers.ExtractEffectfulType(type);
        if (eft is null)
            return Set<string>.s_empty;

        Set<string> result = Set<string>.s_empty;
        foreach (EffectType e in eft.Effects)
            result = result.Add(e.EffectName.Value);

        if (eft.RowVariable is not null)
            result = result.Add("*");

        return result;
    }

    void CheckEffectAllowed(CodexType type, SourceSpan span)
    {
        if (type is not EffectfulType eft)
            return;

        if (m_currentEffects.Contains("*"))
            return;

        ImmutableArray<EffectType> resolved = m_unifier.ResolveEffectRow(eft.RowVariable);
        foreach (EffectType effect in eft.Effects.AddRange(resolved))
        {
            if (!m_currentEffects.Contains(effect.EffectName.Value))
            {
                m_diagnostics.Error(CdxCodes.EffectNotDeclared,
                    $"Effect '{effect.EffectName.Value}' is not allowed in this context. "
                    + "Declare it in the function's type signature.",
                    span);
            }
        }
    }

    CodexType TryDischargeProofParams(CodexType type, SourceSpan span)
    {
        while (type is FunctionType ft && ft.Parameter is ProofType proof)
        {
            if (TryDischargeProof(proof.Claim))
            {
                type = ft.Return;
            }
            else
            {
                m_diagnostics.Error(CdxCodes.LinearUnused,
                    $"Cannot discharge proof obligation: {proof.Claim}",
                    span);
                type = ft.Return;
            }
        }
        return type;
    }

    static bool TryDischargeProof(CodexType claim)
    {
        if (claim is not LessThanClaim lt)
            return false;

        CodexType left = CodexTypeHelpers.NormalizeTypeLevelExpr(lt.Left);
        CodexType right = CodexTypeHelpers.NormalizeTypeLevelExpr(lt.Right);
        if (left is TypeLevelValue lv && right is TypeLevelValue rv)
            return lv.Value < rv.Value;

        return false;
    }


    static CodexType SubstituteVar(CodexType type, int varId, CodexType replacement)
        => new VarSubstituter(varId, replacement).Rewrite(type);

    sealed class VarSubstituter(int varId, CodexType replacement) : CodexTypeRewriter
    {
        public override CodexType Rewrite(CodexType type) => type switch
        {
            TypeVariable tv when tv.Id == varId => replacement,
            ForAllType fa when fa.VariableId == varId => fa,
            SumType s when s.TypeParamIds.IsEmpty => s,
            RecordType r when r.TypeParamIds.IsEmpty => r,
            TypeLevelBinary => CodexTypeHelpers.NormalizeTypeLevelExpr(base.Rewrite(type)),
            _ => base.Rewrite(type)
        };
    }

    CodexType? TryExtractTypeLevelValue(Expr expr)
    {
        if (expr is LiteralExpr lit && lit.Kind == LiteralKind.Integer)
            return new TypeLevelValue(Convert.ToInt64(lit.Value));
        if (expr is ListExpr list)
            return new TypeLevelValue(list.Elements.Count);
        return null;
    }

    static CodexType SubstituteTypeLevelVar(
        CodexType type, string varName, CodexType replacement)
        => new TypeLevelVarSubstituter(varName, replacement).Rewrite(type);

    sealed class TypeLevelVarSubstituter(string varName, CodexType replacement) : CodexTypeRewriter
    {
        public override CodexType Rewrite(CodexType type) => type switch
        {
            TypeLevelVar tv when tv.Name == varName => replacement,
            DependentFunctionType dep when dep.ParamName == varName => dep,
            SumType s => s,
            RecordType r => r,
            ForAllType fa => fa,
            LinearType lin => lin,
            TypeLevelBinary => CodexTypeHelpers.NormalizeTypeLevelExpr(base.Rewrite(type)),
            _ => base.Rewrite(type)
        };
    }

    static CodexType SubstituteEffectRowVar(
        CodexType type, int varId, EffectRowVariable replacement)
        => new EffectRowVarSubstituter(varId, replacement).Rewrite(type);

    sealed class EffectRowVarSubstituter(int varId, EffectRowVariable replacement) : CodexTypeRewriter
    {
        public override CodexType Rewrite(CodexType type) => type switch
        {
            EffectRowVariable erv when erv.Id == varId => replacement,
            EffectfulType eft => eft with
            {
                RowVariable = eft.RowVariable is not null && eft.RowVariable.Id == varId
                    ? replacement
                    : eft.RowVariable,
                Return = Rewrite(eft.Return)
            },
            ForAllType fa when fa.VariableId == varId => fa,
            SumType s => s,
            RecordType r => r,
            LinearType lin => lin,
            TypeLevelBinary bin => bin,
            ProofType proof => proof,
            LessThanClaim lt => lt,
            _ => base.Rewrite(type)
        };
    }
}
