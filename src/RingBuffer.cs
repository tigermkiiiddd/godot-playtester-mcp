using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Fixed-capacity ring buffer. O(1) push, O(1) indexed access.
/// Replaces List.RemoveAt(0) which is O(n).
/// </summary>
public class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _head;
    private int _count;

    public RingBuffer(int capacity) => _items = new T[capacity];
    public int Count => _count;
    public int Capacity => _items.Length;

    public void Push(T item)
    {
        _items[_head] = item;
        _head = (_head + 1) % _items.Length;
        if (_count < _items.Length) _count++;
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head - _count + index + _items.Length) % _items.Length];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public T[] ToArray()
    {
        var result = new T[_count];
        for (int i = 0; i < _count; i++) result[i] = this[i];
        return result;
    }
}
