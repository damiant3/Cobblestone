using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Codex.Core;

public sealed class Map<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : class
{
    readonly ImmutableDictionary<TKey, TValue> m_inner;

    Map(ImmutableDictionary<TKey, TValue> inner)
    {
        m_inner = inner;
    }

    public static readonly Map<TKey, TValue> s_empty = new(ImmutableDictionary<TKey, TValue>.Empty);

    public static Map<TKey, TValue> Of(params (TKey Key, TValue Value)[] items)
    {
        ImmutableDictionary<TKey, TValue>.Builder builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach ((TKey key, TValue value) in items)
        {
            builder[key] = value;
        }

        return new(builder.ToImmutable());
    }

    public static Map<TKey, TValue> From(ImmutableDictionary<TKey, TValue> dict) => new(dict);

    public TValue? this[TKey key] =>
        m_inner.TryGetValue(key, out TValue? value) ? value : null;

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value) =>
        m_inner.TryGetValue(key, out value);

    public TValue Get(TKey key, TValue fallback) =>
        m_inner.TryGetValue(key, out TValue? value) ? value : fallback;

    public Map<TKey, TValue> Set(TKey key, TValue value) =>
        new(m_inner.SetItem(key, value));

    public Map<TKey, TValue> Remove(TKey key) =>
        new(m_inner.Remove(key));

    public bool ContainsKey(TKey key) => m_inner.ContainsKey(key);

    public int Count => m_inner.Count;

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => m_inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ValueMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : struct
{
    readonly ImmutableDictionary<TKey, TValue> m_inner;

    ValueMap(ImmutableDictionary<TKey, TValue> inner)
    {
        m_inner = inner;
    }

    public static readonly ValueMap<TKey, TValue> s_empty = new(ImmutableDictionary<TKey, TValue>.Empty);

    public static ValueMap<TKey, TValue> Of(params (TKey Key, TValue Value)[] items)
    {
        ImmutableDictionary<TKey, TValue>.Builder builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach ((TKey key, TValue value) in items)
        {
            builder[key] = value;
        }

        return new(builder.ToImmutable());
    }

    public TValue? this[TKey key] =>
        m_inner.TryGetValue(key, out TValue value) ? value : null;

    public bool TryGet(TKey key, out TValue value) =>
        m_inner.TryGetValue(key, out value);

    public TValue Get(TKey key, TValue fallback) =>
        m_inner.TryGetValue(key, out TValue value) ? value : fallback;

    public ValueMap<TKey, TValue> Set(TKey key, TValue value) =>
        new(m_inner.SetItem(key, value));

    public ValueMap<TKey, TValue> Remove(TKey key) =>
        new(m_inner.Remove(key));

    public bool ContainsKey(TKey key) => m_inner.ContainsKey(key);

    public int Count => m_inner.Count;

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => m_inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
