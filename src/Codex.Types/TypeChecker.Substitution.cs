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
        return result;
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
    {
        return type switch
        {
            EffectRowVariable erv => erv.Id == varId,
            EffectfulType eft => (eft.RowVariable is not null && eft.RowVariable.Id == varId)
                || ContainsEffectRowVar(eft.Return, varId),
            FunctionType f => ContainsEffectRowVar(f.Parameter, varId)
                || ContainsEffectRowVar(f.Return, varId),
            ListType l => ContainsEffectRowVar(l.Element, varId),
            LinkedListType l => ContainsEffectRowVar(l.Element, varId),
            ForAllType fa => ContainsEffectRowVar(fa.Body, varId),
            DependentFunctionType dep => ContainsEffectRowVar(dep.ParamType, varId)
                || ContainsEffectRowVar(dep.Body, varId),
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
    {
        switch (type)
        {
            case TypeVariable tv:
                vars.Add(tv.Id);
                break;
            case FunctionType ft:
                CollectFreeTypeVars(ft.Parameter, vars);
                CollectFreeTypeVars(ft.Return, vars);
                break;
            case ListType lt:
                CollectFreeTypeVars(lt.Element, vars);
                break;
            case LinkedListType llt:
                CollectFreeTypeVars(llt.Element, vars);
                break;
            case ForAllType fa:
                CollectFreeTypeVars(fa.Body, vars);
                vars.Remove(fa.VariableId);
                break;
            case ConstructedType ct:
                foreach (CodexType arg in ct.Arguments)
                    CollectFreeTypeVars(arg, vars);
                break;
            case EffectfulType eft:
                foreach (EffectType e in eft.Effects)
                    CollectFreeTypeVars(e, vars);
                CollectFreeTypeVars(eft.Return, vars);
                break;
            case DependentFunctionType dep:
                CollectFreeTypeVars(dep.ParamType, vars);
                CollectFreeTypeVars(dep.Body, vars);
                break;
        }
    }

    static Set<string> ExtractEffects(CodexType type)
    {
        CodexType current = type;
        while (current is FunctionType ft)
            current = ft.Return;
        while (current is DependentFunctionType dep)
            current = dep.Body;

        if (current is not EffectfulType eft)
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
            if (!m_currentEffects.Contains(effect.EffectName.Value))
                m_diagnostics.Error(CdxCodes.EffectNotDeclared,
                    $"Effect '{effect.EffectName.Value}' is not allowed in this context. "
                    + "Declare it in the function's type signature.",
                    span);
    }

    CodexType TryDischargeProofParams(CodexType type, SourceSpan span)
    {
        while (type is FunctionType ft && ft.Parameter is ProofType proof)
        {
            if (TryDischargeProof(proof.Claim))
                type = ft.Return;
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
        CodexType left = NormalizeTypeLevelExpr(lt.Left);
        CodexType right = NormalizeTypeLevelExpr(lt.Right);
        if (left is TypeLevelValue lv && right is TypeLevelValue rv)
            return lv.Value < rv.Value;
        return false;
    }

    static CodexType NormalizeTypeLevelExpr(CodexType type)
    {
        if (type is not TypeLevelBinary bin)
            return type;

        CodexType left = NormalizeTypeLevelExpr(bin.Left);
        CodexType right = NormalizeTypeLevelExpr(bin.Right);

        if (left is TypeLevelValue lv && right is TypeLevelValue rv)
        {
            long result = bin.Op switch
            {
                TypeLevelOp.Add => lv.Value + rv.Value,
                TypeLevelOp.Sub => lv.Value - rv.Value,
                TypeLevelOp.Mul => lv.Value * rv.Value,
                _ => 0
            };
            return new TypeLevelValue(result);
        }

        return new TypeLevelBinary(bin.Op, left, right);
    }

    static CodexType SubstituteVar(CodexType type, int varId, CodexType replacement)
    {
        return type switch
        {
            TypeVariable tv when tv.Id == varId => replacement,
            FunctionType f => new FunctionType(
                SubstituteVar(f.Parameter, varId, replacement),
                SubstituteVar(f.Return, varId, replacement)),
            ListType l => new ListType(SubstituteVar(l.Element, varId, replacement)),
            LinkedListType l => new LinkedListType(SubstituteVar(l.Element, varId, replacement)),
            ConstructedType c => c with
            {
                Arguments = [.. c.Arguments.Select(
                    a => SubstituteVar(a, varId, replacement))]
            },
            ForAllType fa when fa.VariableId != varId =>
                fa with { Body = SubstituteVar(fa.Body, varId, replacement) },
            SumType s => s with
            {
                Constructors = [.. s.Constructors.Select(c => c with
                {
                    Fields = [.. c.Fields.Select(
                        f => SubstituteVar(f, varId, replacement))]
                })]
            },
            RecordType r => r with
            {
                Fields = [.. r.Fields.Select(f => f with
                {
                    Type = SubstituteVar(f.Type, varId, replacement)
                })]
            },
            EffectfulType eft => eft with
            {
                Return = SubstituteVar(eft.Return, varId, replacement)
            },
            LinearType lin => new LinearType(
                SubstituteVar(lin.Inner, varId, replacement)),
            DependentFunctionType dep => new DependentFunctionType(
                dep.ParamName,
                SubstituteVar(dep.ParamType, varId, replacement),
                SubstituteVar(dep.Body, varId, replacement)),
            TypeLevelBinary bin => NormalizeTypeLevelExpr(new TypeLevelBinary(
                bin.Op,
                SubstituteVar(bin.Left, varId, replacement),
                SubstituteVar(bin.Right, varId, replacement))),
            ProofType proof => new ProofType(
                SubstituteVar(proof.Claim, varId, replacement)),
            LessThanClaim lt => new LessThanClaim(
                SubstituteVar(lt.Left, varId, replacement),
                SubstituteVar(lt.Right, varId, replacement)),
            _ => type
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
    {
        return type switch
        {
            TypeLevelVar tv when tv.Name == varName => replacement,
            FunctionType f => new FunctionType(
                SubstituteTypeLevelVar(f.Parameter, varName, replacement),
                SubstituteTypeLevelVar(f.Return, varName, replacement)),
            DependentFunctionType dep when dep.ParamName != varName =>
                new DependentFunctionType(
                    dep.ParamName,
                    SubstituteTypeLevelVar(dep.ParamType, varName, replacement),
                    SubstituteTypeLevelVar(dep.Body, varName, replacement)),
            ConstructedType c => c with
            {
                Arguments = [.. c.Arguments.Select(
                    a => SubstituteTypeLevelVar(a, varName, replacement))]
            },
            ListType l => new ListType(
                SubstituteTypeLevelVar(l.Element, varName, replacement)),
            LinkedListType l => new LinkedListType(
                SubstituteTypeLevelVar(l.Element, varName, replacement)),
            TypeLevelBinary bin => NormalizeTypeLevelExpr(new TypeLevelBinary(
                bin.Op,
                SubstituteTypeLevelVar(bin.Left, varName, replacement),
                SubstituteTypeLevelVar(bin.Right, varName, replacement))),
            EffectfulType eft => eft with
            {
                Return = SubstituteTypeLevelVar(eft.Return, varName, replacement)
            },
            ProofType proof => new ProofType(
                SubstituteTypeLevelVar(proof.Claim, varName, replacement)),
            LessThanClaim lt => new LessThanClaim(
                SubstituteTypeLevelVar(lt.Left, varName, replacement),
                SubstituteTypeLevelVar(lt.Right, varName, replacement)),
            _ => type
        };
    }

    static CodexType SubstituteEffectRowVar(
        CodexType type, int varId, EffectRowVariable replacement)
    {
        return type switch
        {
            EffectRowVariable erv when erv.Id == varId => replacement,
            EffectfulType eft => eft with
            {
                RowVariable = eft.RowVariable is not null && eft.RowVariable.Id == varId
                    ? replacement
                    : eft.RowVariable,
                Return = SubstituteEffectRowVar(eft.Return, varId, replacement)
            },
            FunctionType f => new FunctionType(
                SubstituteEffectRowVar(f.Parameter, varId, replacement),
                SubstituteEffectRowVar(f.Return, varId, replacement)),
            ListType l => new ListType(
                SubstituteEffectRowVar(l.Element, varId, replacement)),
            LinkedListType l => new LinkedListType(
                SubstituteEffectRowVar(l.Element, varId, replacement)),
            ForAllType fa when fa.VariableId != varId =>
                fa with { Body = SubstituteEffectRowVar(fa.Body, varId, replacement) },
            DependentFunctionType dep => new DependentFunctionType(
                dep.ParamName,
                SubstituteEffectRowVar(dep.ParamType, varId, replacement),
                SubstituteEffectRowVar(dep.Body, varId, replacement)),
            ConstructedType c => c with
            {
                Arguments = [.. c.Arguments.Select(
                    a => SubstituteEffectRowVar(a, varId, replacement))]
            },
            _ => type
        };
    }
}
