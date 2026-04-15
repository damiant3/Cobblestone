using System.Text;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    static string GenericSuffix(IRDefinition def)
    {
        HashSet<int> ids = [];
        CollectTypeVarIds(def.Type, ids);
        if (ids.Count == 0)
        {
            return "";
        }

        return "<" + string.Join(", ", ids.Order().Select(id => $"T{id}")) + ">";
    }

    static string GenericSuffixFromType(CodexType type)
    {
        HashSet<int> ids = [];
        CollectTypeVarIds(type, ids);
        if (ids.Count == 0)
        {
            return "";
        }

        return "<" + string.Join(", ", ids.Order().Select(id => $"T{id}")) + ">";
    }
}
