using Codex.Core;

namespace Codex.Types;

public static class TypeFormatter
{
    public static string Format(CodexType type)
    {
        Map<int, string> varNames = Map<int, string>.s_empty;
        int nextVar = 0;
        return FormatInner(type, ref varNames, ref nextVar, false);
    }

    static string FormatInner(
        CodexType type, ref Map<int, string> varNames, ref int nextVar, bool parenthesize)
    {
        switch (type)
        {
            case IntegerType: return "Integer";
            case NumberType: return "Number";
            case TextType: return "Text";
            case BooleanType: return "Boolean";
            case CharType: return "Char";
            case NothingType: return "Nothing";
            case VoidType: return "Void";
            case ErrorType: return "?";

            case TypeVariable tv:
                return GetVarName(tv.Id, ref varNames, ref nextVar);

            case FunctionType ft:
                string param = FormatInner(ft.Parameter, ref varNames, ref nextVar, true);
                string ret = FormatInner(ft.Return, ref varNames, ref nextVar, false);
                string fnResult = $"{param} \u2192 {ret}";
                return parenthesize ? $"({fnResult})" : fnResult;

            case ForAllType fa:
                string faVar = GetVarName(fa.VariableId, ref varNames, ref nextVar);
                string faBody = FormatInner(fa.Body, ref varNames, ref nextVar, false);
                return $"\u2200{faVar}. {faBody}";

            case ListType lt:
                string elem = FormatInner(lt.Element, ref varNames, ref nextVar, false);
                return $"List {elem}";

            case LinkedListType llt:
                string llElem = FormatInner(llt.Element, ref varNames, ref nextVar, false);
                return $"LinkedList {llElem}";

            case ConstructedType ct:
            {
                if (ct.Arguments.IsEmpty)
                    return ct.Constructor.Value;

                List<string> argParts = [];
                foreach (CodexType arg in ct.Arguments)
                    argParts.Add(FormatInner(arg, ref varNames, ref nextVar, true));

                return $"{ct.Constructor.Value} {string.Join(" ", argParts)}";
            }

            case RecordType rt:
            {
                List<string> fieldParts = [];
                foreach (RecordFieldType f in rt.Fields)
                {
                    string fieldType = FormatInner(f.Type, ref varNames, ref nextVar, false);
                    fieldParts.Add($"{f.FieldName.Value} : {fieldType}");
                }
                return $"{rt.TypeName.Value} {{ {string.Join(", ", fieldParts)} }}";
            }

            case SumType st:
            {
                List<string> ctorParts = [];
                foreach (SumConstructorType ctor in st.Constructors)
                {
                    if (ctor.Fields.IsEmpty)
                        {
                            ctorParts.Add(ctor.Name.Value);
                        }
                        else
                    {
                        List<string> fieldStrs = [];
                        foreach (CodexType fieldTy in ctor.Fields)
                            fieldStrs.Add(FormatInner(fieldTy, ref varNames, ref nextVar, true));
                        ctorParts.Add($"{ctor.Name.Value} {string.Join(" ", fieldStrs)}");
                    }
                }
                return $"{st.TypeName.Value} = {string.Join(" | ", ctorParts)}";
            }

            case EffectfulType eft:
            {
                List<string> effectParts = [];
                foreach (CodexType e in eft.Effects)
                    effectParts.Add(FormatInner(e, ref varNames, ref nextVar, false));

                string eftRet = FormatInner(eft.Return, ref varNames, ref nextVar, false);
                string row = eft.RowVariable is not null
                    ? $", {GetVarName(eft.RowVariable.Id, ref varNames, ref nextVar)}"
                    : "";
                return $"[{string.Join(", ", effectParts)}{row}] {eftRet}";
            }

            case EffectType et:
                return et.EffectName.Value;

            case EffectRowVariable erv:
                return GetVarName(erv.Id, ref varNames, ref nextVar);

            case LinearType lin:
                return $"linear {FormatInner(lin.Inner, ref varNames, ref nextVar, false)}";

            case DependentFunctionType dep:
                string depParam = FormatInner(dep.ParamType, ref varNames, ref nextVar, false);
                string depBody = FormatInner(dep.Body, ref varNames, ref nextVar, false);
                return $"({dep.ParamName} : {depParam}) \u2192 {depBody}";

            case TypeLevelValue tlv:
                return tlv.Value.ToString();

            case TypeLevelVar tlvar:
                return tlvar.Name;

            case TypeLevelBinary bin:
            {
                string opStr = bin.Op switch
                {
                    TypeLevelOp.Add => "+",
                    TypeLevelOp.Sub => "-",
                    TypeLevelOp.Mul => "*",
                    _ => "?"
                };
                string binLeft = FormatInner(bin.Left, ref varNames, ref nextVar, false);
                string binRight = FormatInner(bin.Right, ref varNames, ref nextVar, false);
                return $"({binLeft} {opStr} {binRight})";
            }

            case ProofType proof:
                return $"{{proof : {FormatInner(proof.Claim, ref varNames, ref nextVar, false)}}}";

            case LessThanClaim lt:
            {
                string ltLeft = FormatInner(lt.Left, ref varNames, ref nextVar, false);
                string ltRight = FormatInner(lt.Right, ref varNames, ref nextVar, false);
                return $"{ltLeft} < {ltRight}";
            }

            case EqualityType eq:
            {
                string eqLeft = FormatInner(eq.Left, ref varNames, ref nextVar, false);
                string eqRight = FormatInner(eq.Right, ref varNames, ref nextVar, false);
                return $"{eqLeft} \u2261 {eqRight}";
            }

            case ReflProof:
                return "Refl";

            case CongProof cong:
                return $"Cong {cong.FunctionName} {FormatInner(cong.InnerProof, ref varNames, ref nextVar, false)}";

            case SymProof sym:
                return $"Sym {FormatInner(sym.InnerProof, ref varNames, ref nextVar, false)}";

            case TransProof trans:
            {
                string tLeft = FormatInner(trans.Left, ref varNames, ref nextVar, false);
                string tRight = FormatInner(trans.Right, ref varNames, ref nextVar, false);
                return $"Trans {tLeft} {tRight}";
            }

            case InductionProof ind:
            {
                string indBase = FormatInner(ind.BaseCase, ref varNames, ref nextVar, false);
                string indStep = FormatInner(ind.InductiveStep, ref varNames, ref nextVar, false);
                return $"Induction {ind.Variable} (base: {indBase}) (step: {indStep})";
            }

            default:
                return "?";
        }
    }

    static string GetVarName(int id, ref Map<int, string> varNames, ref int nextVar)
    {
        string? existing = varNames[id];
        if (existing is not null) return existing;

        string name = nextVar < 26
            ? ((char)('a' + nextVar)).ToString()
            : $"t{nextVar}";
        nextVar++;
        varNames = varNames.Set(id, name);
        return name;
    }
}
