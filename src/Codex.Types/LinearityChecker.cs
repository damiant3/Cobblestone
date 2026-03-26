using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

public sealed class LinearityChecker(DiagnosticBag diagnostics, Map<string, CodexType> typeMap)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly Map<string, CodexType> m_typeMap = typeMap;
    ValueMap<string, int> m_usageCounts = ValueMap<string, int>.s_empty;
    Map<string, CodexType> m_linearBindings = Map<string, CodexType>.s_empty;
    bool m_lambdaIsLinear; // set by CheckLambdaExpr when a lambda captures linear vars

    public void CheckModule(Module module)
    {
        foreach (Definition def in module.Definitions)
        {
            CheckDefinition(def);
        }
    }

    void CheckDefinition(Definition def)
    {
        ValueMap<string, int> savedCounts = m_usageCounts;
        Map<string, CodexType> savedLinear = m_linearBindings;
        m_usageCounts = ValueMap<string, int>.s_empty;
        m_linearBindings = Map<string, CodexType>.s_empty;

        CodexType defType = m_typeMap[def.Name.Value] ?? ErrorType.s_instance;
        CodexType currentType = defType;
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

            if (paramType is LinearType)
            {
                m_linearBindings = m_linearBindings.Set(param.Name.Value, paramType);
                m_usageCounts = m_usageCounts.Set(param.Name.Value, 0);
            }
        }

        CheckExpr(def.Body);
        ReportUnusedLinearBindings(def.Span);

        m_usageCounts = savedCounts;
        m_linearBindings = savedLinear;
    }

    void CheckExpr(Expr expr)
    {
        switch (expr)
        {
            case NameExpr name:
                RecordUsage(name.Name.Value, name.Span);
                break;

            case LiteralExpr:
                break;

            case BinaryExpr bin:
                CheckExpr(bin.Left);
                CheckExpr(bin.Right);
                break;

            case UnaryExpr un:
                CheckExpr(un.Operand);
                break;

            case ApplyExpr app:
                // Step 3: Direct lambda application — (\x -> body) arg — is safe.
                // The closure is consumed immediately, so linear captures don't escape.
                // Check the body directly without the closure-capture check.
                if (app.Function is LambdaExpr directLam)
                {
                    CheckExpr(app.Argument);
                    CheckExpr(directLam.Body);
                }
                else
                {
                    CheckExpr(app.Function);
                    CheckExpr(app.Argument);
                }
                break;

            case IfExpr iff:
                CheckExpr(iff.Condition);
                ValueMap<string, int> savedThen = m_usageCounts;
                CheckExpr(iff.Then);
                ValueMap<string, int> afterThen = m_usageCounts;
                m_usageCounts = savedThen;
                CheckExpr(iff.Else);
                ValueMap<string, int> afterElse = m_usageCounts;
                MergeBranchCounts(afterThen, afterElse, iff.Span);
                break;

            case LetExpr let:
                CheckLetExpr(let);
                break;

            case LambdaExpr lam:
                CheckLambdaExpr(lam);
                break;

            case MatchExpr match:
                CheckExpr(match.Scrutinee);
                CheckMatchBranches(match);
                break;

            case ListExpr list:
                foreach (Expr element in list.Elements)
                    CheckExpr(element);
                break;

            case RecordExpr rec:
                foreach (RecordFieldExpr field in rec.Fields)
                    CheckExpr(field.Value);
                break;

            case FieldAccessExpr fa:
                CheckExpr(fa.Record);
                break;

            case DoExpr doExpr:
                CheckDoExpr(doExpr);
                break;

            case ErrorExpr:
                break;
        }
    }

    void CheckLetExpr(LetExpr let)
    {
        Map<string, CodexType> savedLinear = m_linearBindings;

        foreach (LetBinding binding in let.Bindings)
        {
            if (IsLinearForward(binding.Value))
            {
                string sourceName = ((NameExpr)binding.Value).Name.Value;
                CodexType? sourceType = m_linearBindings[sourceName];
                if (sourceType is not null)
                {
                    RecordUsage(sourceName, binding.Value.Span);
                    m_linearBindings = m_linearBindings.Set(
                        binding.Name.Value, sourceType);
                    m_usageCounts = m_usageCounts.Set(binding.Name.Value, 0);
                    continue;
                }
            }

            m_lambdaIsLinear = false;
            CheckExpr(binding.Value);

            // Step 2: if the binding value is a lambda that captures linear variables,
            // the binding itself becomes linear (must be consumed exactly once).
            if (m_lambdaIsLinear)
            {
                m_linearBindings = m_linearBindings.Set(
                    binding.Name.Value, new LinearType(ErrorType.s_instance));
                m_usageCounts = m_usageCounts.Set(binding.Name.Value, 0);
                m_lambdaIsLinear = false;
            }
        }

        CheckExpr(let.Body);

        ReportUnusedLetLinearBindings(let.Span, savedLinear);
        m_linearBindings = savedLinear;
    }

    bool IsLinearForward(Expr value)
    {
        return value is NameExpr name
            && m_linearBindings[name.Name.Value] is not null;
    }

    void ReportUnusedLetLinearBindings(SourceSpan span, Map<string, CodexType> outerLinear)
    {
        foreach (KeyValuePair<string, CodexType> kv in m_linearBindings)
        {
            if (outerLinear[kv.Key] is not null)
                continue;

            int count = m_usageCounts[kv.Key] ?? 0;
            if (count == 0)
            {
                m_diagnostics.Error("CDX2040",
                    $"Linear variable '{kv.Key}' is never used " +
                    "(linear resources must be consumed)",
                    span);
            }
        }
    }

    void CheckLambdaExpr(LambdaExpr lam)
    {
        Map<string, CodexType> savedLinear = m_linearBindings;
        ValueMap<string, int> savedCounts = m_usageCounts;

        foreach (Parameter param in lam.Parameters)
        {
            if (param.TypeAnnotation is LinearTypeExpr)
            {
                LinearType linType = new(ErrorType.s_instance);
                m_linearBindings = m_linearBindings.Set(
                    param.Name.Value, linType);
                m_usageCounts = m_usageCounts.Set(param.Name.Value, 0);
            }
        }

        CheckExpr(lam.Body);

        // Check lambda's own linear parameters
        foreach (Parameter param in lam.Parameters)
        {
            if (m_linearBindings[param.Name.Value] is not null
                && !savedLinear.ContainsKey(param.Name.Value))
            {
                int count = m_usageCounts[param.Name.Value] ?? 0;
                if (count == 0)
                {
                    m_diagnostics.Error("CDX2040",
                        $"Linear variable '{param.Name.Value}' is never used " +
                        "(linear resources must be consumed)",
                        lam.Span);
                }
            }
        }

        // Detect closure capture of outer linear variables (CDX2043)
        bool capturesLinear = false;
        List<string>? capturedNames = null;
        foreach (KeyValuePair<string, CodexType> kv in savedLinear)
        {
            int beforeCount = savedCounts[kv.Key] ?? 0;
            int afterCount = m_usageCounts[kv.Key] ?? 0;
            if (afterCount > beforeCount)
            {
                capturesLinear = true;
                (capturedNames ??= new()).Add(kv.Key);
                m_diagnostics.Error("CDX2043",
                    $"Linear variable '{kv.Key}' is captured by a closure. " +
                    "Bind the closure with 'let' and use it exactly once, " +
                    "or apply the lambda directly.",
                    lam.Span);
            }
        }

        m_linearBindings = savedLinear;
        m_usageCounts = savedCounts;

        // The capture transfers the linear obligation from the outer variable
        // to the closure. Record a usage of each captured variable so the outer
        // scope doesn't report CDX2040 (unused).
        if (capturedNames is not null)
        {
            foreach (string name in capturedNames)
            {
                int current = m_usageCounts[name] ?? 0;
                m_usageCounts = m_usageCounts.Set(name, current + 1);
            }
        }

        // Step 2: A lambda that captures linear variables is itself linear.
        // Mark it so that let-bindings track it via CDX2040/CDX2041.
        if (capturesLinear)
            m_lambdaIsLinear = true;
    }

    void CheckDoExpr(DoExpr doExpr)
    {
        Map<string, CodexType> savedLinear = m_linearBindings;

        foreach (DoStatement stmt in doExpr.Statements)
        {
            switch (stmt)
            {
                case DoBindStatement bind:
                    CheckExpr(bind.Value);
                    break;
                case DoExprStatement exprStmt:
                    CheckExpr(exprStmt.Expression);
                    break;
            }
        }

        ReportUnusedLetLinearBindings(doExpr.Span, savedLinear);
        m_linearBindings = savedLinear;
    }

    void CheckMatchBranches(MatchExpr match)
    {
        if (match.Branches.Count == 0) return;

        ValueMap<string, int> savedCounts = m_usageCounts;
        ValueMap<string, int>? mergedCounts = null;

        foreach (MatchBranch branch in match.Branches)
        {
            m_usageCounts = savedCounts;
            CheckExpr(branch.Body);
            if (mergedCounts is null)
            {
                mergedCounts = m_usageCounts;
            }
            else
            {
                MergeBranchCounts(mergedCounts, m_usageCounts, match.Span);
                mergedCounts = m_usageCounts;
            }
        }

        if (mergedCounts is not null)
            m_usageCounts = mergedCounts;
    }

    void MergeBranchCounts(
        ValueMap<string, int> branch1,
        ValueMap<string, int> branch2,
        SourceSpan span)
    {
        foreach (KeyValuePair<string, CodexType> kv in m_linearBindings)
        {
            string name = kv.Key;
            int count1 = branch1[name] ?? 0;
            int count2 = branch2[name] ?? 0;
            if (count1 != count2)
            {
                m_diagnostics.Error("CDX2042",
                    $"Linear variable '{name}' is used inconsistently across branches " +
                    $"({count1} time(s) in one branch, {count2} in another)",
                    span);
            }
        }
    }

    void RecordUsage(string name, SourceSpan span)
    {
        if (m_linearBindings[name] is null) return;

        int current = m_usageCounts[name] ?? 0;
        m_usageCounts = m_usageCounts.Set(name, current + 1);

        if (current + 1 > 1)
        {
            m_diagnostics.Error("CDX2041",
                $"Linear variable '{name}' is used more than once",
                span);
        }
    }

    void ReportUnusedLinearBindings(SourceSpan span)
    {
        foreach (KeyValuePair<string, CodexType> kv in m_linearBindings)
        {
            int count = m_usageCounts[kv.Key] ?? 0;
            if (count == 0)
            {
                m_diagnostics.Error("CDX2040",
                    $"Linear variable '{kv.Key}' is never used " +
                    "(linear resources must be consumed)",
                    span);
            }
        }
    }
}
