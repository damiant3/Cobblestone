using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Types;

public sealed class TypeEnvironment
{
    readonly Map<string, CodexType> m_bindings;

    public TypeEnvironment() : this(Map<string, CodexType>.s_empty)
    {
    }

    TypeEnvironment(Map<string, CodexType> bindings)
    {
        m_bindings = bindings;
    }

    public TypeEnvironment Bind(string name, CodexType type) => new(m_bindings.Set(name, type));

    public TypeEnvironment Bind(Name name, CodexType type) => Bind(name.Value, type);

    public CodexType? Lookup(string name) => m_bindings[name];

    public CodexType? Lookup(Name name) => Lookup(name.Value);

    public bool Contains(string name) => m_bindings.ContainsKey(name);

    public static TypeEnvironment WithBuiltins()
    {
        BuiltinChapters.BindTypedInto(new TypeEnvironment(), out TypeEnvironment env);
        return env;
    }
}
