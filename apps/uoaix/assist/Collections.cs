using System;
// Map and Set from the C# reference compiler (old/src/Codex.Core/Map.cs, Set.cs): the Assist's only keyed collections
// (Damian, 2026-10-21: "I forbid use of Dictionary go find Map from the old project and use Set, its sibling, for
// HashSet"). Immutable: a change returns a new map or set, so a reader never sees one half-changed.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Uoaix.Assist;

public sealed class Map<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : class
{
    readonly ImmutableDictionary<TKey, TValue> m_inner;

    Map(ImmutableDictionary<TKey, TValue> inner)
    {
        m_inner = inner;
    }

    static readonly Map<TKey, TValue> s_empty = new(ImmutableDictionary<TKey, TValue>.Empty);
    public static Map<TKey, TValue> Empty => s_empty;

    public static Map<TKey, TValue> Of(params (TKey Key, TValue Value)[] items)
    {
        ImmutableDictionary<TKey, TValue>.Builder builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach ((TKey key, TValue value) in items)
            builder[key] = value;
        return new(builder.ToImmutable());
    }

    public static Map<TKey, TValue> From(ImmutableDictionary<TKey, TValue> dict) => new(dict);

    public static Map<TKey, TValue> Keyed(IEqualityComparer<TKey> keys) =>
        new(ImmutableDictionary.Create<TKey, TValue>(keys));

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

    public Map<TKey, TValue> Clear() => new(m_inner.Clear());

    public bool ContainsKey(TKey key) => m_inner.ContainsKey(key);

    public int Count => m_inner.Count;

    public IEnumerable<TKey> Keys => m_inner.Keys;

    public IEnumerable<TValue> Values => m_inner.Values;

    // The entries by key: a map's own order is its hash's, and a string's hash changes with every process.
    public List<KeyValuePair<TKey, TValue>> Ordered(IComparer<TKey> keys)
    {
        List<KeyValuePair<TKey, TValue>> l = new(m_inner);
        l.Sort((a, b) => keys.Compare(a.Key, b.Key));
        return l;
    }

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

    static readonly ValueMap<TKey, TValue> s_empty = new(ImmutableDictionary<TKey, TValue>.Empty);
    public static ValueMap<TKey, TValue> Empty => s_empty;

    public static ValueMap<TKey, TValue> Of(params (TKey Key, TValue Value)[] items)
    {
        ImmutableDictionary<TKey, TValue>.Builder builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach ((TKey key, TValue value) in items)
            builder[key] = value;
        return new(builder.ToImmutable());
    }

    public static ValueMap<TKey, TValue> Keyed(IEqualityComparer<TKey> keys) =>
        new(ImmutableDictionary.Create<TKey, TValue>(keys));

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

    public ValueMap<TKey, TValue> Clear() => new(m_inner.Clear());

    public bool ContainsKey(TKey key) => m_inner.ContainsKey(key);

    public int Count => m_inner.Count;

    public IEnumerable<TKey> Keys => m_inner.Keys;

    public IEnumerable<TValue> Values => m_inner.Values;

    public List<KeyValuePair<TKey, TValue>> Ordered(IComparer<TKey> keys)
    {
        List<KeyValuePair<TKey, TValue>> l = new(m_inner);
        l.Sort((a, b) => keys.Compare(a.Key, b.Key));
        return l;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => m_inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class Set<T> : IEnumerable<T>
    where T : notnull
{
    readonly ImmutableHashSet<T> m_inner;

    Set(ImmutableHashSet<T> inner)
    {
        m_inner = inner;
    }

    static readonly Set<T> s_empty = new([]);
    public static Set<T> Empty => s_empty;

    public static Set<T> Of(params T[] items) => new(ImmutableHashSet.Create(items));

    public static Set<T> From(ImmutableHashSet<T> set) => new(set);

    public static Set<T> Keyed(IEqualityComparer<T> items) => new(ImmutableHashSet.Create(items));

    public bool Contains(T item) => m_inner.Contains(item);

    public Set<T> Add(T item) => new(m_inner.Add(item));

    public Set<T> Remove(T item) => new(m_inner.Remove(item));

    public Set<T> Clear() => new(m_inner.Clear());

    public Set<T> Union(Set<T> other) => new(m_inner.Union(other.m_inner));

    public Set<T> Intersect(Set<T> other) => new(m_inner.Intersect(other.m_inner));

    public Set<T> Except(Set<T> other) => new(m_inner.Except(other.m_inner));

    public bool IsSupersetOf(Set<T> other) => m_inner.IsSupersetOf(other.m_inner);

    public List<T> Ordered(IComparer<T> items)
    {
        List<T> l = new(m_inner);
        l.Sort(items);
        return l;
    }

    public int Count => m_inner.Count;

    public IEnumerator<T> GetEnumerator() => m_inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Array's and List's lookups for a fixed list (ImmutableArray), and two for any list, written out: the Assist's
// references hold no System.Linq.
public static class FixedLists
{
    public static int FindIndex<T>(this ImmutableArray<T> items, Predicate<T> match)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (match(items[i]))
                return i;
        }

        return -1;
    }

    public static bool Exists<T>(this ImmutableArray<T> items, Predicate<T> match) => items.FindIndex(match) >= 0;

    public static T? Find<T>(this ImmutableArray<T> items, Predicate<T> match)
    {
        int at = items.FindIndex(match);
        return at >= 0 ? items[at] : default;
    }

    public static bool TrueForAll<T>(this ImmutableArray<T> items, Predicate<T> match) =>
        items.FindIndex(x => !match(x)) < 0;

    public static TOut[] ConvertAll<T, TOut>(this ImmutableArray<T> items, Func<T, TOut> convert)
    {
        TOut[] o = new TOut[items.Length];
        for (int i = 0; i < items.Length; i++)
            o[i] = convert(items[i]);
        return o;
    }

    public static T[] FindAll<T>(this ImmutableArray<T> items, Predicate<T> match)
    {
        List<T> o = new();
        foreach (T x in items)
        {
            if (match(x))
                o.Add(x);
        }

        return o.ToArray();
    }

    public static T[] ToArray<T>(this ImmutableArray<T> items)
    {
        T[] o = new T[items.Length];
        items.CopyTo(o);
        return o;
    }

    public static bool Contains<T>(this IReadOnlyList<T> items, T item)
    {
        EqualityComparer<T> eq = EqualityComparer<T>.Default;
        for (int i = 0; i < items.Count; i++)
        {
            if (eq.Equals(items[i], item))
                return true;
        }

        return false;
    }

    public static int Count<T>(this IReadOnlyList<T> items, Predicate<T> match)
    {
        int n = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (match(items[i]))
                n++;
        }

        return n;
    }
}
