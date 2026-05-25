using System.Collections;
using System.Collections.Immutable;

namespace Codex.Core;

public sealed class Set<T> : IEnumerable<T>
    where T : notnull
{
    readonly ImmutableHashSet<T> m_inner;

    Set(ImmutableHashSet<T> inner)
    {
        m_inner = inner;
    }

    public static readonly Set<T> s_empty = new([]);

    public static Set<T> Of(params T[] items) => new(ImmutableHashSet.Create(items));

    public static Set<T> From(ImmutableHashSet<T> set) => new(set);

    public bool Contains(T item) => m_inner.Contains(item);

    public Set<T> Add(T item) => new(m_inner.Add(item));

    public Set<T> Remove(T item) => new(m_inner.Remove(item));

    public Set<T> Union(Set<T> other) => new(m_inner.Union(other.m_inner));

    public Set<T> Intersect(Set<T> other) => new(m_inner.Intersect(other.m_inner));

    public int Count => m_inner.Count;

    public IEnumerator<T> GetEnumerator() => m_inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
