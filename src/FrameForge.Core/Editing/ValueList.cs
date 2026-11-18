using System.Collections;

namespace FrameForge.Core.Editing;

/// <summary>Shared empty <see cref="ValueList{T}"/> instances.</summary>
public static class ValueList
{
    public static ValueList<T> Empty<T>() => Holder<T>.Instance;

    private static class Holder<T>
    {
        public static readonly ValueList<T> Instance = new([]);
    }
}

/// <summary>
/// An immutable list compared by its items, so records that hold one (<see cref="EditRecipe"/>) keep value equality:
/// two recipes with the same local adjustments are equal, which undo, save and preset logic rely on.
/// </summary>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.AsSpan().SequenceEqual(other._items, EqualityComparer<T>.Default));

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>A copy with <paramref name="item"/> added at the end.</summary>
    public ValueList<T> Add(T item) => new(_items.Append(item));
}
