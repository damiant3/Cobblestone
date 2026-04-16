using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed partial class TypeChecker
{
    CodexType ResolveTypeExpr(TypeExpr typeExpr)
    {
        return typeExpr switch
        {
            NamedTypeExpr named => ResolveNamedType(named.Name),
            FunctionTypeExpr func => new FunctionType(
                ResolveTypeExpr(func.Parameter),
                ResolveTypeExpr(func.Return)),
            AppliedTypeExpr app => ResolveAppliedType(app),
            EffectfulTypeExpr eff => ResolveEffectfulType(eff),
            LinearTypeExpr lin => new LinearType(ResolveTypeExpr(lin.Inner)),
            DependentTypeExpr dep => ResolveDependentType(dep),
            IntegerLiteralTypeExpr intLit => new TypeLevelValue(intLit.Value),
            BinaryTypeExpr bin => ResolveTypeLevelBinary(bin),
            ProofConstraintExpr proof => ResolveProofConstraint(proof),
            _ => ErrorType.s_instance
        };
    }

    CodexType ResolveNamedType(Name name)
    {
        CodexType? fromTypeLevelEnv = m_typeLevelEnv[name.Value];
        if (fromTypeLevelEnv is not null)
        {
            return fromTypeLevelEnv;
        }

        CodexType? fromTypeParam = m_typeParamEnv[name.Value];
        if (fromTypeParam is not null)
        {
            return fromTypeParam;
        }

        CodexType? fromPrimitive = name.Value switch
        {
            "Integer" => IntegerType.s_instance,
            "Number" => NumberType.s_instance,
            "Text" => TextType.s_instance,
            "Boolean" => BooleanType.s_instance,
            "Char" => CharType.s_instance,
            "Nothing" => NothingType.s_instance,
            "Void" => VoidType.s_instance,
            _ => null
        };
        if (fromPrimitive is not null)
        {
            return fromPrimitive;
        }

        CodexType? userDef = m_typeDefMap[name.Value];
        if (userDef is not null)
        {
            return userDef;
        }

        if (name.IsValueName)
        {
            TypeVariable tv = m_unifier.FreshVar();
            m_typeParamEnv = m_typeParamEnv.Set(name.Value, tv);
            return tv;
        }

        return new ConstructedType(name, []);
    }

    CodexType ResolveAppliedType(AppliedTypeExpr app)
    {
        if (app.Constructor is NamedTypeExpr namedCtor && namedCtor.Name.Value == "List"
            && app.Arguments.Count == 1)
        {
            return new ListType(ResolveTypeExpr(app.Arguments[0]));
        }

        if (app.Constructor is NamedTypeExpr namedCtor2 && namedCtor2.Name.Value == "LinkedList"
            && app.Arguments.Count == 1)
        {
            return new LinkedListType(ResolveTypeExpr(app.Arguments[0]));
        }

        if (app.Constructor is NamedTypeExpr named)
        {
            CodexType? userDef = m_typeDefMap[named.Name.Value];
            if (userDef is not null)
            {
                ImmutableArray<CodexType> args = [.. app.Arguments.Select(ResolveTypeExpr)];

                // userDef is still a registration placeholder (forward reference
                // across record→variant or variant→record). We can't check arity
                // or instantiate yet — preserve the args in a ConstructedType so
                // emitters and downstream unification see the applied-to info.
                if (userDef is ConstructedType placeholderCt && placeholderCt.Arguments.IsEmpty)
                {
                    return new ConstructedType(named.Name, args);
                }

                int expectedArity = userDef switch
                {
                    SumType s => s.TypeParamIds.Length,
                    RecordType r => r.TypeParamIds.Length,
                    _ => -1
                };
                if (expectedArity >= 0 && args.Length != expectedArity)
                {
                    m_diagnostics.Error(CdxCodes.TypeArgArityMismatch,
                        $"Type '{named.Name.Value}' expects {expectedArity} type argument(s), "
                        + $"but received {args.Length}",
                        app.Span);
                }
                return InstantiateParametricType(userDef, args);
            }
        }

        Name ctorName = app.Constructor is NamedTypeExpr n
            ? n.Name
            : new("?");

        ImmutableArray<CodexType> fallbackArgs = [.. app.Arguments.Select(ResolveTypeExpr)];
        return new ConstructedType(ctorName, fallbackArgs);
    }

    EffectfulType ResolveEffectfulType(EffectfulTypeExpr eff)
    {
        ImmutableArray<EffectType>.Builder effects = ImmutableArray.CreateBuilder<EffectType>();
        EffectRowVariable? rowVar = null;
        foreach (TypeExpr e in eff.Effects)
        {
            if (e is NamedTypeExpr named)
            {
                if (named.Name.IsValueName)
                {
                    EffectRowVariable? existing = m_effectRowVars[named.Name.Value];
                    rowVar = existing ?? m_unifier.FreshEffectVar();
                    m_effectRowVars = m_effectRowVars.Set(named.Name.Value, rowVar);
                }
                else
                {
                    effects.Add(new EffectType(named.Name));
                }
            }
            else
            {
                m_diagnostics.Error(CdxCodes.EffectLabelMustBeName, "Effect label must be a name", e.Span);
            }
        }
        CodexType returnType = ResolveTypeExpr(eff.Return);
        return new EffectfulType(effects.ToImmutable(), returnType, rowVar);
    }

    CodexType ResolveDependentType(DependentTypeExpr dep)
    {
        CodexType paramType = ResolveTypeExpr(dep.ParamType);
        Map<string, CodexType> savedTypeLevelEnv = m_typeLevelEnv;
        m_typeLevelEnv = m_typeLevelEnv.Set(
            dep.ParamName.Value, new TypeLevelVar(dep.ParamName.Value));
        CodexType body = ResolveTypeExpr(dep.Body);
        m_typeLevelEnv = savedTypeLevelEnv;
        return new DependentFunctionType(dep.ParamName.Value, paramType, body);
    }

    CodexType ResolveTypeLevelBinary(BinaryTypeExpr bin)
    {
        CodexType left = ResolveTypeExpr(bin.Left);
        CodexType right = ResolveTypeExpr(bin.Right);
        TypeLevelOp op = bin.Op switch
        {
            BinaryOp.Add => TypeLevelOp.Add,
            BinaryOp.Sub => TypeLevelOp.Sub,
            BinaryOp.Mul => TypeLevelOp.Mul,
            _ => TypeLevelOp.Add
        };
        return NormalizeTypeLevelExpr(new TypeLevelBinary(op, left, right));
    }

    CodexType ResolveProofConstraint(ProofConstraintExpr proof)
    {
        CodexType left = ResolveTypeExpr(proof.Left);
        CodexType right = ResolveTypeExpr(proof.Right);
        return new ProofType(new LessThanClaim(left, right));
    }
}
