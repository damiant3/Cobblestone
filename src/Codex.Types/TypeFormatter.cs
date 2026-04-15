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

            case ConstructedType ct:
            {
                if (ct.Arguments.IsEmpty)
                    {
                        return ct.Constructor.Value;
                    }

                    List<string> argParts = [];
                foreach (CodexType arg in ct.Arguments)
                    {
                        argParts.Add(FormatInner(arg, ref varNames, ref nextVar, true));
                    }

                    return $"{ct.Constructor.Value} {string.Join(" ", argParts)}";
            }

            case EffectfulType eft:
            {
                List<string> effectParts = [];
                foreach (CodexType e in eft.Effects)
                    {
                        effectParts.Add(FormatInner(e, ref varNames, ref nextVar, false));
                    }

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

            default:
                return type.ToString() ?? "?";
        }
    }

    static string GetVarName(int id, ref Map<int, string> varNames, ref int nextVar)
    {
        string? existing = varNames[id];
        if (existing is not null)
        {
            return existing;
        }

        string name = nextVar < 26
            ? ((char)('a' + nextVar)).ToString()
            : $"t{nextVar}";
        nextVar++;
        varNames = varNames.Set(id, name);
        return name;
    }
}
