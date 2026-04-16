using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed partial class TypeChecker
{
    CodexType InferDefinition(Definition def, CodexType expectedType)
    {
        TypeEnvironment savedEnv = m_env;
        Set<string> savedEffects = m_currentEffects;

        m_currentEffects = ExtractEffects(expectedType);

        CodexType currentExpected = expectedType;
        foreach (Parameter param in def.Parameters)
        {
            while (currentExpected is FunctionType skipFt && skipFt.Parameter is ProofType)
            {
                currentExpected = skipFt.Return;
            }

            CodexType paramType;
            if (currentExpected is FunctionType ft)
            {
                paramType = ft.Parameter;
                currentExpected = ft.Return;
            }
            else if (currentExpected is DependentFunctionType dep)
            {
                paramType = dep.ParamType;
                currentExpected = dep.Body;
            }
            else
            {
                paramType = m_unifier.FreshVar();
                CodexType returnType = m_unifier.FreshVar();
                m_unifier.Unify(currentExpected,
                    new FunctionType(paramType, returnType), def.Span);
                currentExpected = returnType;
            }

            m_env = m_env.Bind(param.Name, paramType);
        }

        while (currentExpected is FunctionType skipFt2 && skipFt2.Parameter is ProofType)
        {
            currentExpected = skipFt2.Return;
        }

        CodexType bodyType = InferExpr(def.Body);
        m_unifier.Unify(currentExpected, bodyType, def.Body.Span);

        m_env = savedEnv;
        m_currentEffects = savedEffects;
        return expectedType;
    }

    CodexType InferExpr(Expr expr)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                return lit.Kind switch
                {
                    LiteralKind.Integer => IntegerType.s_instance,
                    LiteralKind.Number => NumberType.s_instance,
                    LiteralKind.Text => TextType.s_instance,
                    LiteralKind.Boolean => BooleanType.s_instance,
                    _ => ErrorType.s_instance
                };

            case NameExpr name:
                CodexType? looked = m_env.Lookup(name.Name);
                if (looked is null)
                {
                    m_diagnostics.Error(CdxCodes.UnknownName,
                        $"Unknown name '{name.Name.Value}' during type checking",
                        name.Span);
                    return ErrorType.s_instance;
                }
                return Instantiate(looked);

            case BinaryExpr bin:
                return InferBinary(bin);

            case UnaryExpr un:
                CodexType operandType = InferExpr(un.Operand);
                m_unifier.Unify(operandType, IntegerType.s_instance, un.Span);
                return IntegerType.s_instance;

            case ApplyExpr app:
                return InferApplication(app);

            case IfExpr iff:
                CodexType condType = InferExpr(iff.Condition);
                m_unifier.Unify(condType, BooleanType.s_instance, iff.Condition.Span);
                CodexType thenType = InferExpr(iff.Then);
                CodexType elseType = InferExpr(iff.Else);
                m_unifier.Unify(thenType, elseType, iff.Span);
                return thenType;

            case LetExpr let:
                return InferLet(let);

            case LambdaExpr lam:
                return InferLambda(lam);

            case MatchExpr match:
                return InferMatch(match);

            case ListExpr list:
                return InferList(list);

            case RecordExpr rec:
                return InferRecord(rec);

            case FieldAccessExpr fa:
                return InferFieldAccess(fa);

            case ActExpr actExpr:
                return InferDoExpr(actExpr);

            case HandleExpr handleExpr:
                return InferHandleExpr(handleExpr);

            case ErrorExpr:
                return ErrorType.s_instance;

            default:
                return ErrorType.s_instance;
        }
    }

    CodexType InferBinary(BinaryExpr bin)
    {
        CodexType leftType = InferExpr(bin.Left);
        CodexType rightType = InferExpr(bin.Right);

        return bin.Op switch
        {
            BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Pow =>
                InferArithmetic(leftType, rightType, bin),

            BinaryOp.Eq or BinaryOp.NotEq or BinaryOp.Lt or BinaryOp.Gt
                or BinaryOp.LtEq or BinaryOp.GtEq or BinaryOp.DefEq =>
                InferComparison(leftType, rightType, bin),

            BinaryOp.And or BinaryOp.Or =>
                InferLogical(leftType, rightType, bin),

            BinaryOp.Append =>
                InferAppend(leftType, rightType, bin),

            BinaryOp.Cons =>
                InferCons(leftType, rightType, bin),

            _ => ErrorType.s_instance
        };
    }

    CodexType InferArithmetic(CodexType left, CodexType right, BinaryExpr bin)
    {
        m_unifier.Unify(left, right, bin.Span);
        CodexType resolved = m_unifier.Resolve(left);
        if (resolved is not (IntegerType or NumberType or TypeVariable or ErrorType))
        {
            m_diagnostics.Error(CdxCodes.ArithmeticRequiresNumeric,
                $"Arithmetic operator requires Integer or Number, but found {resolved}",
                bin.Span);
        }

        return left;
    }

    BooleanType InferComparison(CodexType left, CodexType right, BinaryExpr bin)
    {
        m_unifier.Unify(left, right, bin.Span);
        return BooleanType.s_instance;
    }

    BooleanType InferLogical(CodexType left, CodexType right, BinaryExpr bin)
    {
        m_unifier.Unify(left, BooleanType.s_instance, bin.Left.Span);
        m_unifier.Unify(right, BooleanType.s_instance, bin.Right.Span);
        return BooleanType.s_instance;
    }

    CodexType InferAppend(CodexType left, CodexType right, BinaryExpr bin)
    {
        CodexType resolved = m_unifier.Resolve(left);
        if (resolved is TextType)
        {
            m_unifier.Unify(right, TextType.s_instance, bin.Right.Span);
            return TextType.s_instance;
        }
        m_unifier.Unify(left, right, bin.Span);
        return left;
    }

    ListType InferCons(CodexType left, CodexType right, BinaryExpr bin)
    {
        ListType listType = new(left);
        m_unifier.Unify(right, listType, bin.Span);
        return listType;
    }

    CodexType InferApplication(ApplyExpr app)
    {
        CodexType funcType = InferExpr(app.Function);
        CodexType resolvedFunc = m_unifier.Resolve(funcType);

        if (resolvedFunc is DependentFunctionType dep)
        {
            CodexType argType = InferExpr(app.Argument);
            m_unifier.Unify(argType, dep.ParamType, app.Span);
            CodexType? argValue = TryExtractTypeLevelValue(app.Argument);
            CodexType body = argValue is not null
                ? SubstituteTypeLevelVar(dep.Body, dep.ParamName, argValue)
                : dep.Body;
            body = TryDischargeProofParams(body, app.Span);
            CheckEffectAllowed(body, app.Span);
            return body;
        }

        // If the function's parameter type is effectful, temporarily allow those effects
        // while inferring the argument. This is how effect handlers (run-state) work:
        // the handler's parameter declares the effects it handles, and the argument
        // is allowed to use them.
        Set<string> savedEffects = m_currentEffects;
        bool isEffectHandler = false;
        CodexType resolvedForParam = m_unifier.Resolve(resolvedFunc);
        if (resolvedForParam is FunctionType paramFt
            && paramFt.Parameter is EffectfulType paramEft)
        {
            isEffectHandler = true;
            foreach (EffectType e in paramEft.Effects)
            {
                m_currentEffects = m_currentEffects.Add(e.EffectName.Value);
            }

            if (paramEft.RowVariable is not null)
            {
                m_currentEffects = m_currentEffects.Add("*");
            }
        }

        CodexType argType2 = InferExpr(app.Argument);

        m_currentEffects = savedEffects;

        CodexType returnType = m_unifier.FreshVar();

        if (!m_unifier.Unify(funcType, new FunctionType(argType2, returnType), app.Span))
        {
            return ErrorType.s_instance;
        }

        CodexType resolved = m_unifier.Resolve(returnType);
        resolved = TryDischargeProofParams(resolved, app.Span);

        // Effect handlers eliminate effects — don't re-check the return type
        if (!isEffectHandler)
        {
            CheckEffectAllowed(resolved, app.Span);
        }

        // If the handler returned an EffectfulType with no concrete effects, unwrap it
        if (isEffectHandler && resolved is EffectfulType handledEft && handledEft.Effects.IsEmpty)
        {
            ImmutableArray<EffectType> rowEffects = m_unifier.ResolveEffectRow(handledEft.RowVariable);
            if (rowEffects.IsEmpty)
            {
                resolved = handledEft.Return;
            }
        }

        return resolved;
    }

    CodexType InferLet(LetExpr let)
    {
        TypeEnvironment savedEnv = m_env;

        foreach (LetBinding binding in let.Bindings)
        {
            CodexType valueType = InferExpr(binding.Value);
            // let X = effectful-call silently corrupts on bare metal because
            // downstream uses read X as if it were the unwrapped value type
            // when it's actually the effect wrapper. Force the user to
            // sequence with act-bind (X <- expr) instead.
            if (valueType is EffectfulType eft && !eft.Effects.IsEmpty)
            {
                m_diagnostics.Error(CdxCodes.LetBindsEffectfulValue,
                    $"let-binding '{binding.Name.Value}' to a value of effectful type '{valueType}' is not allowed outside an act-bind. Use '{binding.Name.Value} <- ...' inside an act block.",
                    binding.Value.Span);
                // Unwrap so downstream type-checking doesn't cascade with the wrapper.
                valueType = eft.Return;
            }
            m_env = m_env.Bind(binding.Name, valueType);
        }

        CodexType bodyType = InferExpr(let.Body);
        m_env = savedEnv;
        return bodyType;
    }

    CodexType InferLambda(LambdaExpr lam)
    {
        TypeEnvironment savedEnv = m_env;

        List<CodexType> paramTypes = [];
        foreach (Parameter p in lam.Parameters)
        {
            CodexType paramType = m_unifier.FreshVar();
            paramTypes.Add(paramType);
            m_env = m_env.Bind(p.Name, paramType);
        }

        CodexType bodyType = InferExpr(lam.Body);
        m_env = savedEnv;

        CodexType result = bodyType;
        for (int i = paramTypes.Count - 1; i >= 0; i--)
        {
            result = new FunctionType(paramTypes[i], result);
        }

        return result;
    }

    CodexType InferMatch(MatchExpr match)
    {
        CodexType scrutineeType = InferExpr(match.Scrutinee);
        CodexType resultType = m_unifier.FreshVar();

        foreach (MatchBranch branch in match.Branches)
        {
            TypeEnvironment savedEnv = m_env;
            CheckPattern(branch.Pattern, scrutineeType);
            CodexType branchType = InferExpr(branch.Body);
            m_unifier.Unify(resultType, branchType, branch.Span);
            m_env = savedEnv;
        }

        CheckExhaustiveness(match, scrutineeType);

        return resultType;
    }

    void CheckExhaustiveness(MatchExpr match, CodexType scrutineeType)
    {
        CodexType resolved = m_unifier.Resolve(scrutineeType);
        if (resolved is not SumType sumType)
        {
            return;
        }

        bool hasCatchAll = match.Branches.Any(b =>
            b.Pattern is VarPattern or WildcardPattern);
        if (hasCatchAll)
        {
            return;
        }

        Set<string> covered = Set<string>.s_empty;
        foreach (MatchBranch branch in match.Branches)
        {
            if (branch.Pattern is CtorPattern cp)
            {
                covered = covered.Add(cp.Constructor.Value);
            }
        }

        List<string> missing = [];
        foreach (SumConstructorType ctor in sumType.Constructors)
        {
            if (!covered.Contains(ctor.Name.Value))
            {
                missing.Add(ctor.Name.Value);
            }
        }

        if (missing.Count > 0)
        {
            string missingStr = string.Join(", ", missing);
            m_diagnostics.Warning(CdxCodes.NonExhaustiveMatch,
                $"Non-exhaustive match: missing constructor(s) {missingStr}",
                match.Span);
        }
    }

    void CheckPattern(Pattern pattern, CodexType expectedType)
    {
        switch (pattern)
        {
            case VarPattern v:
                m_env = m_env.Bind(v.Name, expectedType);
                break;

            case LiteralPattern lit:
                CodexType litType = lit.Kind switch
                {
                    LiteralKind.Integer => IntegerType.s_instance,
                    LiteralKind.Text => TextType.s_instance,
                    LiteralKind.Boolean => BooleanType.s_instance,
                    _ => ErrorType.s_instance
                };
                m_unifier.Unify(expectedType, litType, lit.Span);
                break;

            case CtorPattern ctor:
                CtorInfo? info = m_ctorMap[ctor.Constructor.Value];
                if (info is not null)
                {
                    CodexType ctorType = Instantiate(info.ConstructorType);
                    CodexType resultType = ctorType;
                    List<CodexType> fieldTypes = [];
                    while (resultType is FunctionType ft)
                    {
                        fieldTypes.Add(ft.Parameter);
                        resultType = ft.Return;
                    }
                    m_unifier.Unify(expectedType, resultType, ctor.Span);

                    int count = Math.Min(ctor.SubPatterns.Count, fieldTypes.Count);
                    for (int i = 0; i < count; i++)
                    {
                        CheckPattern(ctor.SubPatterns[i], fieldTypes[i]);
                    }
                }
                else
                {
                    foreach (Pattern sub in ctor.SubPatterns)
                    {
                        CheckPattern(sub, m_unifier.FreshVar());
                    }
                }
                break;

            case WildcardPattern:
                break;
        }
    }

    CodexType InferRecord(RecordExpr rec)
    {
        if (rec.TypeName is null)
        {
            return m_unifier.FreshVar();
        }

        CodexType? typeDef = m_typeDefMap[rec.TypeName.Value.Value];
        if (typeDef is not RecordType recordType)
        {
            return m_unifier.FreshVar();
        }

        // Each construction site needs its own fresh type vars, otherwise a
        // call like `P { f = p.s, s = p.f } : P b a` from a `P a b` value
        // unifies the type def's shared params and collapses a with b.
        ImmutableArray<CodexType> freshArgs = [.. recordType.TypeParamIds
            .Select(_ => (CodexType)m_unifier.FreshVar())];
        RecordType instantiated =
            (RecordType)InstantiateParametricType(recordType, freshArgs);

        foreach (RecordFieldExpr field in rec.Fields)
        {
            RecordFieldType? expectedField = instantiated.Fields
                .FirstOrDefault(f => f.FieldName.Value == field.FieldName.Value);
            CodexType fieldType = InferExpr(field.Value);
            if (expectedField is not null)
            {
                m_unifier.Unify(fieldType, expectedField.Type, field.Span);
            }
        }
        return instantiated;
    }

    CodexType InferFieldAccess(FieldAccessExpr fa)
    {
        CodexType recordType = InferExpr(fa.Record);
        CodexType resolved = m_unifier.Resolve(recordType);
        if (resolved is not RecordType rt)
        {
            return m_unifier.FreshVar();
        }

        RecordFieldType? field = rt.Fields
            .FirstOrDefault(f => f.FieldName.Value == fa.FieldName.Value);
        if (field is not null)
        {
            return field.Type;
        }

        m_diagnostics.Error(CdxCodes.RecordFieldNotFound,
            $"Record type '{rt.TypeName.Value}' has no field '{fa.FieldName.Value}'",
            fa.Span);
        return ErrorType.s_instance;
    }

    CodexType InferDoExpr(ActExpr actExpr)
    {
        TypeEnvironment savedEnv = m_env;
        Set<string> collectedEffects = Set<string>.s_empty;
        CodexType lastType = NothingType.s_instance;

        foreach (ActStatement stmt in actExpr.Statements)
        {
            switch (stmt)
            {
                case ActBindStatement bind:
                {
                    CodexType valueType = InferExpr(bind.Value);
                    CodexType unwrapped =
                        UnwrapEffectful(valueType, ref collectedEffects);
                    m_env = m_env.Bind(bind.Name, unwrapped);
                    lastType = NothingType.s_instance;
                    break;
                }
                case ActExprStatement exprStmt:
                {
                    CodexType stmtType = InferExpr(exprStmt.Expression);
                    UnwrapEffectful(stmtType, ref collectedEffects);
                    lastType = stmtType;
                    break;
                }
            }
        }

        m_env = savedEnv;

        if (collectedEffects.Count > 0)
        {
            ImmutableArray<EffectType> effects = [.. collectedEffects.Select(
                e => new EffectType(new Name(e)))];
            CodexType returnType =
                lastType is EffectfulType eft ? eft.Return : lastType;
            return new EffectfulType(effects, returnType);
        }

        return lastType;
    }

    static CodexType UnwrapEffectful(CodexType type, ref Set<string> effects)
    {
        if (type is not EffectfulType eft)
        {
            return type;
        }

        foreach (EffectType effect in eft.Effects)
        {
            effects = effects.Add(effect.EffectName.Value);
        }

        return eft.Return;
    }

    ListType InferList(ListExpr list)
    {
        if (list.Elements.Count == 0)
        {
            return new ListType(m_unifier.FreshVar());
        }

        CodexType elementType = InferExpr(list.Elements[0]);
        for (int i = 1; i < list.Elements.Count; i++)
        {
            CodexType itemType = InferExpr(list.Elements[i]);
            m_unifier.Unify(elementType, itemType, list.Elements[i].Span);
        }

        return new ListType(elementType);
    }

    CodexType InferHandleExpr(HandleExpr handleExpr)
    {
        Set<string> savedEffects = m_currentEffects;
        m_currentEffects = m_currentEffects.Add(handleExpr.EffectName.Value);
        CodexType compType = InferExpr(handleExpr.Computation);
        m_currentEffects = savedEffects;

        CodexType resultType;
        if (compType is EffectfulType eft)
        {
            ImmutableArray<EffectType> remaining = [.. eft.Effects.Where(
                e => e.EffectName.Value != handleExpr.EffectName.Value)];
            resultType = remaining.IsEmpty ? eft.Return : new EffectfulType(remaining, eft.Return);
        }
        else
        {
            resultType = compType;
        }

        CodexType handlerResultType = m_unifier.FreshVar();

        foreach (HandleClause clause in handleExpr.Clauses)
        {
            TypeEnvironment savedEnv = m_env;

            CodexType? opType = m_env.Lookup(clause.OperationName);
            if (opType is null)
            {
                m_diagnostics.Error(CdxCodes.InfiniteType,
                    $"Unknown effect operation '{clause.OperationName.Value}'",
                    clause.Span);
                m_env = savedEnv;
                continue;
            }

            opType = Instantiate(opType);

            CodexType paramType = opType;
            foreach (Name p in clause.Parameters)
            {
                if (paramType is FunctionType ft)
                {
                    m_env = m_env.Bind(p, ft.Parameter);
                    paramType = ft.Return;
                }
                else
                {
                    m_env = m_env.Bind(p, m_unifier.FreshVar());
                }
            }

            CodexType opReturnType = paramType is EffectfulType opEft ? opEft.Return : paramType;
            CodexType resumeType = new FunctionType(opReturnType, handlerResultType);
            m_env = m_env.Bind(clause.ResumeName, resumeType);

            CodexType bodyType = InferExpr(clause.Body);
            m_unifier.Unify(handlerResultType, bodyType, clause.Span);

            m_env = savedEnv;
        }

        m_unifier.Unify(handlerResultType, resultType, handleExpr.Span);
        return m_unifier.Resolve(handlerResultType);
    }
}
