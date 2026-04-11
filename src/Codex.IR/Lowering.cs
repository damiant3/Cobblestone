using System.Collections.Immutable;
using Codex.Core;
using Codex.Ast;
using Codex.Types;

namespace Codex.IR;

public sealed class Lowering(
    Map<string, CodexType> typeMap,
    Map<string, CtorInfo> ctorMap,
    Map<string, CodexType> typeDefMap,
    DiagnosticBag diagnostics)
{
    readonly Map<string, CodexType> m_typeMap = typeMap;
    readonly Map<string, CtorInfo> m_ctorMap = ctorMap;
    readonly Map<string, CodexType> m_typeDefMap = typeDefMap;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    Map<string, CodexType> m_localEnv = Map<string, CodexType>.s_empty;
    CodexType m_currentStateType = ErrorType.s_instance;

    static readonly Map<string, CodexType> s_builtinTypes = BuildBuiltinTypes();

    public IRChapter Lower(Chapter chapter)
    {
        ImmutableArray<IRDefinition>.Builder allDefs = ImmutableArray.CreateBuilder<IRDefinition>();
        foreach (Definition def in chapter.Definitions)
            allDefs.Add(LowerDefinition(def));

        // Build sections: group typedefs and definitions by SourceChapter
        ImmutableArray<IRDefinition> loweredDefs = allDefs.ToImmutable();
        ImmutableArray<IRChapterSection>.Builder sections = ImmutableArray.CreateBuilder<IRChapterSection>();
        Dictionary<string, (List<(string, CodexType)> Types, List<IRDefinition> Defs)> groups = new();
        List<string> chapterOrder = [];
        HashSet<string> seen = [];

        foreach (TypeDef td in chapter.TypeDefinitions)
        {
            string mod = td.SourceChapter ?? "";
            if (seen.Add(mod)) { chapterOrder.Add(mod); groups[mod] = ([], []); }
            if (m_typeDefMap.ContainsKey(td.Name.Value))
                groups[mod].Types.Add((td.Name.Value, m_typeDefMap[td.Name.Value]!));
        }

        for (int i = 0; i < chapter.Definitions.Count; i++)
        {
            string mod = chapter.Definitions[i].SourceChapter ?? "";
            if (seen.Add(mod)) { chapterOrder.Add(mod); groups[mod] = ([], []); }
            groups[mod].Defs.Add(loweredDefs[i]);
        }

        foreach (string mod in chapterOrder)
        {
            var g = groups[mod];
            chapter.ProseByFile.TryGetValue(mod, out ChapterProse? prose);
            sections.Add(new IRChapterSection(
                mod,
                [.. g.Types],
                [.. g.Defs])
            {
                ChapterTitle = prose?.ChapterTitle,
                Prose = prose?.Prose,
                SectionTitles = prose?.SectionTitles is not null
                    ? [.. prose.SectionTitles] : default
            });
        }

        return new(chapter.Name, loweredDefs, m_typeDefMap)
        {
            Sections = sections.ToImmutable()
        };
    }

    IRDefinition LowerDefinition(Definition def)
    {
        CodexType fullType = m_typeMap[def.Name.Value] ?? ErrorType.s_instance;

        Map<string, CodexType> savedEnv = m_localEnv;

        ImmutableArray<IRParameter>.Builder parameters = ImmutableArray.CreateBuilder<IRParameter>();
        CodexType currentType = fullType;
        foreach (Parameter param in def.Parameters)
        {
            while (currentType is FunctionType skipFt && skipFt.Parameter is ProofType)
                currentType = skipFt.Return;

            CodexType paramType;
            if (currentType is FunctionType ft)
            {
                paramType = ft.Parameter;
                currentType = ft.Return;
            }
            else if (currentType is DependentFunctionType dep)
            {
                paramType = dep.ParamType;
                currentType = dep.Body;
            }
            else
            {
                paramType = ErrorType.s_instance;
            }
            parameters.Add(new(param.Name.Value, paramType));
            m_localEnv = m_localEnv.Set(param.Name.Value, paramType);
        }

        while (currentType is FunctionType skipFt2 && skipFt2.Parameter is ProofType)
            currentType = skipFt2.Return;

        IRExpr body = LowerExpr(def.Body, currentType);
        bool needsEscape = IRRegion.TypeNeedsHeapEscape(body.Type);
        body = new IRRegion(body, body.Type, needsEscape);
        m_localEnv = savedEnv;
        return new(def.Name.Value, parameters.ToImmutable(), fullType, body)
            { Section = def.Section };
    }

    IRExpr LowerExpr(Expr expr, CodexType expectedType)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                return LowerLiteral(lit);

            case NameExpr name:
                if (name.Name.Value == "get-state")
                {
                    CodexType stateType = m_currentStateType is not ErrorType
                        ? m_currentStateType
                        : expectedType;
                    return new IRGetState(stateType);
                }
                return new IRName(name.Name.Value, LookupName(name.Name.Value, expectedType));

            case BinaryExpr bin:
                return LowerBinary(bin, expectedType);

            case UnaryExpr un:
                return new IRNegate(LowerExpr(un.Operand, IntegerType.s_instance));

            case IfExpr iff:
            {
                IRExpr thenExpr = LowerExpr(iff.Then, expectedType);
                IRExpr elseExpr = LowerExpr(iff.Else, expectedType is ErrorType ? thenExpr.Type : expectedType);
                CodexType resultType = expectedType is ErrorType ? thenExpr.Type : expectedType;
                return new IRIf(
                    LowerExpr(iff.Condition, BooleanType.s_instance),
                    thenExpr,
                    elseExpr,
                    resultType);
            }

            case LetExpr let:
                return LowerLet(let, expectedType);

            case ApplyExpr app:
                return LowerApply(app, expectedType);

            case LambdaExpr lam:
                return LowerLambda(lam, expectedType);

            case MatchExpr match:
                return LowerMatch(match, expectedType);

            case ListExpr list:
                return LowerList(list, expectedType);

            case DoExpr doExpr:
                return LowerDoExpr(doExpr, expectedType);

            case HandleExpr handleExpr:
                return LowerHandleExpr(handleExpr, expectedType);

            case RecordExpr rec:
                return LowerRecord(rec, expectedType);

            case FieldAccessExpr fa:
                return LowerFieldAccess(fa, expectedType);

            case ErrorExpr err:
                return new IRError(err.Message, expectedType);

            default:
                return new IRError($"Unsupported expression: {expr.GetType().Name}", expectedType);
        }
    }

    static IRExpr LowerLiteral(LiteralExpr lit)
    {
        return lit.Kind switch
        {
            LiteralKind.Integer => new IRIntegerLit(Convert.ToInt64(lit.Value)),
            LiteralKind.Number => new IRNumberLit(Convert.ToDouble(lit.Value)),
            LiteralKind.Text => new IRTextLit((string)lit.Value),
            LiteralKind.Boolean => new IRBoolLit((bool)lit.Value),
            LiteralKind.Char => new IRCharLit(Convert.ToInt64(lit.Value)),
            _ => new IRError("Unknown literal kind", ErrorType.s_instance)
        };
    }

    IRExpr LowerBinary(BinaryExpr bin, CodexType expectedType)
    {
        IRExpr left = LowerExpr(bin.Left, expectedType);

        CodexType rightExpected = expectedType;
        if (bin.Op == BinaryOp.Append && left.Type is ListType)
            rightExpected = left.Type;
        else if (bin.Op == BinaryOp.Cons && left.Type is not ErrorType)
            rightExpected = new ListType(left.Type);

        IRExpr right = LowerExpr(bin.Right, rightExpected);

        IRBinaryOp op = bin.Op switch
        {
            BinaryOp.Add when IsNumeric(left.Type) => IsInteger(left.Type) ? IRBinaryOp.AddInt : IRBinaryOp.AddNum,
            BinaryOp.Sub when IsNumeric(left.Type) => IsInteger(left.Type) ? IRBinaryOp.SubInt : IRBinaryOp.SubNum,
            BinaryOp.Mul when IsNumeric(left.Type) => IsInteger(left.Type) ? IRBinaryOp.MulInt : IRBinaryOp.MulNum,
            BinaryOp.Div when IsNumeric(left.Type) => IsInteger(left.Type) ? IRBinaryOp.DivInt : IRBinaryOp.DivNum,
            BinaryOp.Pow => IRBinaryOp.PowInt,
            BinaryOp.Add => IRBinaryOp.AddInt,
            BinaryOp.Sub => IRBinaryOp.SubInt,
            BinaryOp.Mul => IRBinaryOp.MulInt,
            BinaryOp.Div => IRBinaryOp.DivInt,
            BinaryOp.Eq => IRBinaryOp.Eq,
            BinaryOp.NotEq => IRBinaryOp.NotEq,
            BinaryOp.Lt => IRBinaryOp.Lt,
            BinaryOp.Gt => IRBinaryOp.Gt,
            BinaryOp.LtEq => IRBinaryOp.LtEq,
            BinaryOp.GtEq => IRBinaryOp.GtEq,
            BinaryOp.DefEq => IRBinaryOp.Eq,
            BinaryOp.And => IRBinaryOp.And,
            BinaryOp.Or => IRBinaryOp.Or,
            BinaryOp.Append when IsText(left.Type) || IsText(expectedType) => IRBinaryOp.AppendText,
            BinaryOp.Append => IRBinaryOp.AppendList,
            BinaryOp.Cons => IRBinaryOp.ConsList,
            _ => IRBinaryOp.AddInt
        };

        CodexType resultType = bin.Op switch
        {
            BinaryOp.Eq or BinaryOp.NotEq or BinaryOp.Lt or BinaryOp.Gt
                or BinaryOp.LtEq or BinaryOp.GtEq or BinaryOp.DefEq => BooleanType.s_instance,
            BinaryOp.And or BinaryOp.Or => BooleanType.s_instance,
            BinaryOp.Append when IsText(left.Type) || IsText(expectedType) => TextType.s_instance,
            _ => left.Type
        };

        return new IRBinary(op, left, right, resultType);
    }

    IRExpr LowerLet(LetExpr let, CodexType expectedType)
    {
        Map<string, CodexType> savedEnv = m_localEnv;

        List<(string Name, IRExpr Value)> loweredBindings = [];
        foreach (LetBinding binding in let.Bindings)
        {
            IRExpr value = LowerExpr(binding.Value, ErrorType.s_instance);
            loweredBindings.Add((binding.Name.Value, value));
            m_localEnv = m_localEnv.Set(binding.Name.Value, value.Type);
        }

        IRExpr body = LowerExpr(let.Body, expectedType);

        for (int i = loweredBindings.Count - 1; i >= 0; i--)
        {
            (string name, IRExpr value) = loweredBindings[i];
            // Wrap each let binding's value in its own region.
            // Scalar-returning expressions reclaim intermediates immediately.
            // Heap-returning expressions skip reclamation (handled by EmitRegion).
            bool letNeedsEscape = IRRegion.TypeNeedsHeapEscape(value.Type);
            IRExpr regionValue = new IRRegion(value, value.Type, letNeedsEscape);
            body = new IRLet(name, value.Type, regionValue, body);
        }

        m_localEnv = savedEnv;
        return body;
    }

    IRExpr LowerApply(ApplyExpr app, CodexType expectedType)
    {
        // Detect set-state val → IRSetState(val, stateType)
        if (app.Function is NameExpr setName && setName.Name.Value == "set-state")
        {
            IRExpr val = LowerExpr(app.Argument, ErrorType.s_instance);
            return new IRSetState(val, val.Type);
        }

        // Detect run-state init comp → IRRunState(init, comp, stateType, resultType)
        if (app.Function is ApplyExpr innerApp
            && innerApp.Function is NameExpr runName
            && runName.Name.Value == "run-state")
        {
            IRExpr init = LowerExpr(innerApp.Argument, ErrorType.s_instance);
            CodexType stateType = init.Type;
            CodexType savedStateType = m_currentStateType;
            m_currentStateType = stateType;
            IRExpr comp = LowerExpr(app.Argument, ErrorType.s_instance);
            m_currentStateType = savedStateType;
            CodexType resultType = expectedType;
            if (resultType is ErrorType or EffectfulType)
            {
                if (comp.Type is FunctionType compFt && compFt.Return is EffectfulType eft)
                    resultType = eft.Return;
                else if (comp.Type is FunctionType compFt2)
                    resultType = compFt2.Return;
            }
            return new IRRunState(init, comp, stateType, resultType);
        }

        IRExpr func = LowerExpr(app.Function, ErrorType.s_instance);
        CodexType argType;
        CodexType returnType;
        if (func.Type is FunctionType ft)
        {
            argType = ft.Parameter;
            returnType = ft.Return;
        }
        else if (func.Type is DependentFunctionType dep)
        {
            argType = dep.ParamType;
            returnType = dep.Body;
        }
        else
        {
            argType = ErrorType.s_instance;
            returnType = expectedType;
        }
        IRExpr arg = LowerExpr(app.Argument, argType);

        returnType = SubstituteTypeVarsFromArg(argType, arg.Type, returnType);

        return new IRApply(func, arg, returnType);
    }

    static CodexType SubstituteTypeVarsFromArg(
        CodexType paramType, CodexType argType, CodexType target)
    {
        if (paramType is TypeVariable tv)
            return SubstituteTypeVar(target, tv.Id, argType);

        if (paramType is ListType lp && argType is ListType la)
            return SubstituteTypeVarsFromArg(lp.Element, la.Element, target);

        if (paramType is FunctionType fp && argType is FunctionType fa)
        {
            target = SubstituteTypeVarsFromArg(fp.Parameter, fa.Parameter, target);
            return SubstituteTypeVarsFromArg(fp.Return, fa.Return, target);
        }

        return target;
    }

    static CodexType SubstituteTypeVar(CodexType type, int varId, CodexType replacement)
    {
        return type switch
        {
            TypeVariable tv when tv.Id == varId => replacement,
            FunctionType ft => new FunctionType(
                SubstituteTypeVar(ft.Parameter, varId, replacement),
                SubstituteTypeVar(ft.Return, varId, replacement)),
            ListType lt => new ListType(SubstituteTypeVar(lt.Element, varId, replacement)),
            LinkedListType lt => new LinkedListType(SubstituteTypeVar(lt.Element, varId, replacement)),
            _ => type
        };
    }

    IRExpr LowerLambda(LambdaExpr lam, CodexType expectedType)
    {
        Map<string, CodexType> savedEnv = m_localEnv;

        ImmutableArray<IRParameter>.Builder parameters = ImmutableArray.CreateBuilder<IRParameter>();
        CodexType currentType = expectedType;
        foreach (Parameter p in lam.Parameters)
        {
            CodexType paramType;
            if (currentType is FunctionType ft)
            {
                paramType = ft.Parameter;
                currentType = ft.Return;
            }
            else if (currentType is DependentFunctionType dep)
            {
                paramType = dep.ParamType;
                currentType = dep.Body;
            }
            else
            {
                paramType = ErrorType.s_instance;
            }
            parameters.Add(new(p.Name.Value, paramType));
            m_localEnv = m_localEnv.Set(p.Name.Value, paramType);
        }

        IRExpr body = LowerExpr(lam.Body, currentType);
        m_localEnv = savedEnv;
        return new IRLambda(parameters.ToImmutable(), body, expectedType);
    }

    IRExpr LowerMatch(MatchExpr match, CodexType expectedType)
    {
        IRExpr scrutinee = LowerExpr(match.Scrutinee, ErrorType.s_instance);
        ImmutableArray<IRMatchBranch>.Builder branches = ImmutableArray.CreateBuilder<IRMatchBranch>();

        CodexType resolvedType = expectedType;
        foreach (MatchBranch branch in match.Branches)
        {
            Map<string, CodexType> savedEnv = m_localEnv;
            IRPattern pattern = LowerPattern(branch.Pattern, scrutinee.Type);
            IRExpr body = LowerExpr(branch.Body, resolvedType);
            branches.Add(new(pattern, body));
            if (resolvedType is ErrorType && body.Type is not ErrorType)
                resolvedType = body.Type;
            m_localEnv = savedEnv;
        }

        return new IRMatch(scrutinee, branches.ToImmutable(), resolvedType);
    }

    IRPattern LowerPattern(Pattern pattern, CodexType scrutineeType)
    {
        switch (pattern)
        {
            case VarPattern v:
                m_localEnv = m_localEnv.Set(v.Name.Value, scrutineeType);
                return new IRVarPattern(v.Name.Value, scrutineeType);
            case CtorPattern ctor:
                return LowerCtorPattern(ctor, scrutineeType);
            case LiteralPattern lit:
                return new IRLiteralPattern(lit.Value, scrutineeType);
            case WildcardPattern:
                return new IRWildcardPattern();
            default:
                return new IRWildcardPattern();
        }
    }

    IRPattern LowerCtorPattern(CtorPattern ctor, CodexType scrutineeType)
    {
        SumType? sumType = scrutineeType as SumType;
        if (sumType is null && scrutineeType is ConstructedType ct)
        {
            CodexType? resolved = m_typeDefMap[ct.Constructor.Value];
            sumType = resolved as SumType;
        }
        SumConstructorType? sumCtor = sumType?.Constructors
            .FirstOrDefault(c => c.Name.Value == ctor.Constructor.Value);

        if (sumCtor is null)
        {
            CtorInfo? ctorInfo = m_ctorMap[ctor.Constructor.Value];
            if (ctorInfo is not null)
            {
                CodexType ctorType = ctorInfo.ConstructorType;
                List<CodexType> fields = [];
                while (ctorType is FunctionType ft)
                {
                    fields.Add(ft.Parameter);
                    ctorType = ft.Return;
                }
                sumCtor = new SumConstructorType(ctor.Constructor,
                    [.. fields]);
            }
        }

        ImmutableArray<IRPattern>.Builder subPatterns = ImmutableArray.CreateBuilder<IRPattern>();
        for (int i = 0; i < ctor.SubPatterns.Count; i++)
        {
            CodexType fieldType = sumCtor is not null && i < sumCtor.Fields.Length
                ? sumCtor.Fields[i]
                : ErrorType.s_instance;
            subPatterns.Add(LowerPattern(ctor.SubPatterns[i], fieldType));
        }

        return new IRCtorPattern(ctor.Constructor.Value, subPatterns.ToImmutable(), scrutineeType);
    }

    IRExpr LowerList(ListExpr list, CodexType expectedType)
    {
        CodexType elementType;
        if (expectedType is ListType lt)
        {
            elementType = lt.Element;
        }
        else if (list.Elements.Count > 0)
        {
            // Lower the first element to discover its actual type.
            // InferElementType only handles literals; this covers
            // function calls and other expressions.
            IRExpr first = LowerExpr(list.Elements[0], ErrorType.s_instance);
            elementType = first.Type is ErrorType
                ? InferElementType(list) // last resort: literal heuristic
                : first.Type;
        }
        else
        {
            elementType = ErrorType.s_instance;
        }

        ImmutableArray<IRExpr>.Builder elements = ImmutableArray.CreateBuilder<IRExpr>();
        foreach (Expr elem in list.Elements)
        {
            elements.Add(LowerExpr(elem, elementType));
        }

        return new IRList(elements.ToImmutable(), elementType);
    }

    static CodexType InferElementType(ListExpr list)
    {
        if (list.Elements.Count == 0) return ErrorType.s_instance;
        return list.Elements[0] switch
        {
            LiteralExpr lit => lit.Kind switch
            {
                LiteralKind.Integer => IntegerType.s_instance,
                LiteralKind.Number => NumberType.s_instance,
                LiteralKind.Text => TextType.s_instance,
                LiteralKind.Boolean => BooleanType.s_instance,
                LiteralKind.Char => CharType.s_instance,
                _ => ErrorType.s_instance
            },
            _ => ErrorType.s_instance
        };
    }

    CodexType InferExprType(Expr expr)
    {
        return expr switch
        {
            RecordExpr rec when rec.TypeName is not null =>
                m_typeDefMap[rec.TypeName.Value.Value] ?? ErrorType.s_instance,
            NameExpr name => LookupName(name.Name.Value, ErrorType.s_instance),
            _ => ErrorType.s_instance
        };
    }

    CodexType LookupName(string name, CodexType fallback)
    {
        CodexType result = m_localEnv[name]
            ?? m_typeMap[name]
            ?? m_ctorMap[name]?.ConstructorType
            ?? s_builtinTypes[name]
            ?? fallback;
        while (result is ForAllType fa)
            result = fa.Body;
        return result;
    }

    IRExpr LowerDoExpr(DoExpr doExpr, CodexType expectedType)
    {
        Map<string, CodexType> savedEnv = m_localEnv;
        ImmutableArray<IRDoStatement>.Builder statements = ImmutableArray.CreateBuilder<IRDoStatement>();

        foreach (DoStatement stmt in doExpr.Statements)
        {
            switch (stmt)
            {
                case DoBindStatement bind:
                {
                    IRExpr value = LowerExpr(bind.Value, ErrorType.s_instance);
                    CodexType boundType = value.Type is EffectfulType eft ? eft.Return : value.Type;
                    // Wrap in IRRegion for two-space reclamation, same as let-bindings.
                    bool needsEscape = IRRegion.TypeNeedsHeapEscape(boundType);
                    IRExpr regionValue = new IRRegion(value, boundType, needsEscape);
                    statements.Add(new IRDoBind(bind.Name.Value, boundType, regionValue));
                    m_localEnv = m_localEnv.Set(bind.Name.Value, boundType);
                    break;
                }
                case DoExprStatement exprStmt:
                {
                    IRExpr value = LowerExpr(exprStmt.Expression, ErrorType.s_instance);
                    statements.Add(new IRDoExec(value));
                    break;
                }
            }
        }

        m_localEnv = savedEnv;
        return new IRDo(statements.ToImmutable(), expectedType);
    }

    IRExpr LowerHandleExpr(HandleExpr handleExpr, CodexType expectedType)
    {
        IRExpr computation = LowerExpr(handleExpr.Computation, expectedType);

        ImmutableArray<IRHandleClause>.Builder clauses = ImmutableArray.CreateBuilder<IRHandleClause>();
        foreach (HandleClause clause in handleExpr.Clauses)
        {
            ImmutableArray<string>.Builder paramNames = ImmutableArray.CreateBuilder<string>();
            ImmutableArray<CodexType>.Builder paramTypes = ImmutableArray.CreateBuilder<CodexType>();

            CodexType? opType = m_typeMap[clause.OperationName.Value];
            CodexType currentType = opType ?? ErrorType.s_instance;

            foreach (Name p in clause.Parameters)
            {
                CodexType pType = ErrorType.s_instance;
                if (currentType is FunctionType ft)
                {
                    pType = ft.Parameter;
                    currentType = ft.Return;
                }
                paramNames.Add(p.Value);
                paramTypes.Add(pType);
            }

            CodexType resumeParamType = currentType is EffectfulType eft ? eft.Return : currentType;

            Map<string, CodexType> savedEnv = m_localEnv;
            for (int i = 0; i < paramNames.Count; i++)
                m_localEnv = m_localEnv.Set(paramNames[i], paramTypes[i]);
            m_localEnv = m_localEnv.Set(clause.ResumeName.Value,
                new FunctionType(resumeParamType, expectedType));

            IRExpr body = LowerExpr(clause.Body, expectedType);
            m_localEnv = savedEnv;

            clauses.Add(new IRHandleClause(
                clause.OperationName.Value,
                paramNames.ToImmutable(),
                paramTypes.ToImmutable(),
                clause.ResumeName.Value,
                resumeParamType,
                body));
        }

        return new IRHandle(computation, handleExpr.EffectName.Value,
            clauses.ToImmutable(), expectedType);
    }

    IRExpr LowerRecord(RecordExpr rec, CodexType expectedType)
    {
        ImmutableArray<(string FieldName, IRExpr Value)>.Builder fields =
            ImmutableArray.CreateBuilder<(string, IRExpr)>();
        CodexType recType = expectedType;
        RecordType? rt = recType as RecordType;
        if (rt is null && rec.TypeName is not null)
        {
            string typeName = rec.TypeName.Value.Value;
            CodexType? looked = m_typeMap[typeName] ?? m_typeDefMap[typeName];
            rt = looked as RecordType;
            if (rt is not null)
                recType = rt;
        }

        foreach (RecordFieldExpr field in rec.Fields)
        {
            CodexType fieldType = ErrorType.s_instance;
            if (rt is not null)
            {
                RecordFieldType? rft = rt.Fields
                    .FirstOrDefault(f => f.FieldName.Value == field.FieldName.Value);
                if (rft is not null) fieldType = rft.Type;
            }
            fields.Add((field.FieldName.Value, LowerExpr(field.Value, fieldType)));
        }

        string emittedName = rec.TypeName is not null
            ? rec.TypeName.Value.Value
            : rt?.TypeName.Value ?? "Unknown";
        return new IRRecord(emittedName, fields.ToImmutable(), recType);
    }

    IRExpr LowerFieldAccess(FieldAccessExpr fa, CodexType expectedType)
    {
        IRExpr record = LowerExpr(fa.Record, ErrorType.s_instance);

        // Resolve ConstructedType (e.g., Token, Name) to its underlying RecordType
        RecordType? rt = record.Type as RecordType;
        if (rt is null && record.Type is ConstructedType ct)
        {
            CodexType? resolved = m_typeDefMap[ct.Constructor.Value];
            rt = resolved as RecordType;
        }

        CodexType fieldType = expectedType;
        if (rt is not null)
        {
            RecordFieldType? rft = rt.Fields
                .FirstOrDefault(f => f.FieldName.Value == fa.FieldName.Value);
            if (rft is not null) fieldType = rft.Type;

            // Ensure emitters see RecordType (not ConstructedType) so they can compute field indices
            if (record.Type is not RecordType)
                record = record with { Type = rt };
        }
        return new IRFieldAccess(record, fa.FieldName.Value, fieldType);
    }

    static Map<string, CodexType> BuildBuiltinTypes()
    {
        Map<string, CodexType> map = Map<string, CodexType>.s_empty;
        map = map.Set("show", new ForAllType(0,
            new FunctionType(new TypeVariable(0), TextType.s_instance)));
        map = map.Set("negate", new FunctionType(IntegerType.s_instance, IntegerType.s_instance));

        EffectfulType consoleText = new(
            [new EffectType(new Name("Console"))],
            TextType.s_instance);
        map = map.Set("read-line", consoleText);

        EffectfulType consoleNothing = new(
            [new EffectType(new Name("Console"))],
            NothingType.s_instance);
        map = map.Set("print-line", new FunctionType(TextType.s_instance, consoleNothing));

        LinearType fileHandle = new(new ConstructedType(new Name("FileHandle"), []));

        EffectfulType fsFileHandle = new(
            [new EffectType(new Name("FileSystem"))],
            fileHandle);
        map = map.Set("open-file", new FunctionType(TextType.s_instance, fsFileHandle));

        EffectfulType fsTextAndHandle = new(
            [new EffectType(new Name("FileSystem"))],
            new ConstructedType(new Name("Pair"), [TextType.s_instance, fileHandle]));
        map = map.Set("read-all", new FunctionType(fileHandle, fsTextAndHandle));

        EffectfulType fsNothing = new(
            [new EffectType(new Name("FileSystem"))],
            NothingType.s_instance);
        map = map.Set("close-file", new FunctionType(fileHandle, fsNothing));

        EffectfulType fsText = new(
            [new EffectType(new Name("FileSystem"))],
            TextType.s_instance);
        map = map.Set("read-file", new FunctionType(TextType.s_instance, fsText));

        map = map.Set("write-file", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, fsNothing)));
        map = map.Set("file-exists", new FunctionType(TextType.s_instance, BooleanType.s_instance));
        map = map.Set("list-files", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new ListType(TextType.s_instance))));
        map = map.Set("text-split", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new ListType(TextType.s_instance))));
        map = map.Set("text-contains", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, BooleanType.s_instance)));
        map = map.Set("text-starts-with", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, BooleanType.s_instance)));
        map = map.Set("text-compare", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, IntegerType.s_instance)));
        map = map.Set("text-concat-list", new FunctionType(
            new ListType(TextType.s_instance), TextType.s_instance));
        map = map.Set("list-insert-at", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)),
                new FunctionType(IntegerType.s_instance,
                    new FunctionType(new TypeVariable(0), new ListType(new TypeVariable(0)))))));
        map = map.Set("list-snoc", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)),
                new FunctionType(new TypeVariable(0), new ListType(new TypeVariable(0))))));
        map = map.Set("linked-list-empty", new FunctionType(IntegerType.s_instance,
            new LinkedListType(new ListType(IntegerType.s_instance))));
        map = map.Set("linked-list-push",
            new FunctionType(new LinkedListType(new ListType(IntegerType.s_instance)),
                new FunctionType(new ListType(IntegerType.s_instance),
                    new LinkedListType(new ListType(IntegerType.s_instance)))));
        map = map.Set("linked-list-to-list",
            new FunctionType(new LinkedListType(new ListType(IntegerType.s_instance)),
                new ListType(new ListType(IntegerType.s_instance))));
        map = map.Set("record-set", new ForAllType(0,
            new ForAllType(1,
                new FunctionType(new TypeVariable(0),
                    new FunctionType(TextType.s_instance,
                        new FunctionType(new TypeVariable(1), new TypeVariable(0)))))));
        map = map.Set("list-contains", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)),
                new FunctionType(new TypeVariable(0), BooleanType.s_instance))));
        map = map.Set("get-args", new ListType(TextType.s_instance));
        map = map.Set("get-env", new FunctionType(TextType.s_instance, TextType.s_instance));
        map = map.Set("current-dir", TextType.s_instance);
        map = map.Set("run-process", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, TextType.s_instance)));

        map = map.Set("char-at", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance, CharType.s_instance)));
        map = map.Set("char-to-text", new FunctionType(CharType.s_instance, TextType.s_instance));
        map = map.Set("text-length", new FunctionType(TextType.s_instance, IntegerType.s_instance));
        map = map.Set("substring", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance,
                new FunctionType(IntegerType.s_instance, TextType.s_instance))));
        map = map.Set("is-letter", new FunctionType(CharType.s_instance, BooleanType.s_instance));
        map = map.Set("is-digit", new FunctionType(CharType.s_instance, BooleanType.s_instance));
        map = map.Set("is-whitespace", new FunctionType(CharType.s_instance, BooleanType.s_instance));
        map = map.Set("text-to-integer", new FunctionType(TextType.s_instance, IntegerType.s_instance));
        map = map.Set("integer-to-text", new FunctionType(IntegerType.s_instance, TextType.s_instance));
        map = map.Set("text-replace", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new FunctionType(TextType.s_instance, TextType.s_instance))));
        map = map.Set("char-code", new FunctionType(CharType.s_instance, IntegerType.s_instance));
        map = map.Set("char-code-at", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance, IntegerType.s_instance)));
        map = map.Set("code-to-char", new FunctionType(IntegerType.s_instance, CharType.s_instance));

        // Arithmetic builtins
        FunctionType intIntInt = new(IntegerType.s_instance,
            new FunctionType(IntegerType.s_instance, IntegerType.s_instance));
        map = map.Set("int-mod", intIntInt);
        map = map.Set("abs", new FunctionType(IntegerType.s_instance, IntegerType.s_instance));
        map = map.Set("min", intIntInt);
        map = map.Set("max", intIntInt);

        // Bitwise builtins
        map = map.Set("bit-and", intIntInt);
        map = map.Set("bit-or", intIntInt);
        map = map.Set("bit-xor", intIntInt);
        map = map.Set("bit-shl", intIntInt);
        map = map.Set("bit-shr", intIntInt);
        map = map.Set("bit-not", new FunctionType(IntegerType.s_instance, IntegerType.s_instance));

        map = map.Set("list-length", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)), IntegerType.s_instance)));
        map = map.Set("list-at", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)),
                new FunctionType(IntegerType.s_instance, new TypeVariable(0)))));


        TypeVariable stateS = new(200);
        TypeVariable stateA = new(201);
        EffectRowVariable stateE = new(202);

        EffectfulType getStateType = new(
            [new EffectType(new Name("State"))], stateS);
        map = map.Set("get-state", new ForAllType(200, getStateType));

        EffectfulType setStateReturn = new(
            [new EffectType(new Name("State"))], NothingType.s_instance);
        map = map.Set("set-state", new ForAllType(200,
            new FunctionType(stateS, setStateReturn)));

        // run-state : s -> [State s, e] a -> [e] a
        EffectfulType runCompType = new(
            [new EffectType(new Name("State"))], stateA, stateE);
        EffectfulType runStateReturn = new([], stateA, stateE);
        map = map.Set("run-state", new ForAllType(200,
            new ForAllType(201,
                new ForAllType(202,
                    new FunctionType(stateS,
                        new FunctionType(runCompType, runStateReturn))))));

        return map;
    }

    static bool IsInteger(CodexType type) => type is IntegerType;

    static bool IsNumeric(CodexType type) => type is IntegerType or NumberType;

    static bool IsText(CodexType type) => type is TextType;
}
