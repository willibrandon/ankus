using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies bounded lists over caller-owned storage, including aliases and rejected mutations.
/// </summary>
[TestClass]
public sealed class PgFixedListTests
{
    /// <summary>
    /// Append, insertion, ordered and unordered removal, popping and draining preserve exact values.
    /// </summary>
    [TestMethod]
    public void FixedListPreservesCapacityAndOrder()
    {
        int[] buffer = [91, 0, 0, 0, 0, 0, 93];
        int count = 0;
        var list = new PgFixedList<int>(buffer.AsSpan(1, 5), ref count);
        Assert.AreEqual(5, list.Capacity);
        Assert.IsTrue(list.IsEmpty);
        Assert.IsFalse(list.IsFull);
        Assert.IsFalse(list.TryPop(out int missing));
        Assert.AreEqual(0, missing);
        list.Add(10);
        list.Insert(0, 5);
        list.Insert(2, 20);
        list.Insert(2, 15);
        Assert.IsTrue(list.TryAdd(25));
        Assert.IsTrue(list.IsFull);
        Assert.AreEqual(5, count);
        Assert.IsFalse(list.TryAdd(99));
        Assert.AreSequenceEqual([5, 10, 15, 20, 25], list.ToArray());
        var enumerated = new List<int>();
        foreach (int value in list)
        {
            enumerated.Add(value);
        }

        Assert.AreSequenceEqual([5, 10, 15, 20, 25], enumerated);
        list[1] = 11;
        Assert.AreEqual(11, list[1]);
        Assert.AreEqual(15, list.RemoveAt(2));
        Assert.AreSequenceEqual([5, 11, 20, 25], list.ToArray());
        Assert.AreEqual(11, list.SwapRemoveAt(1));
        Assert.AreSequenceEqual([5, 25, 20], list.ToArray());
        Assert.AreEqual(20, list.Pop());
        Assert.AreSequenceEqual([5, 25], list.Drain());
        Assert.IsTrue(list.IsEmpty);
        Assert.AreEqual(0, count);
        Assert.AreSequenceEqual([91, 0, 0, 0, 0, 0, 93], buffer);
        list.Insert(0, 73);
        Assert.AreEqual(73, list.SwapRemoveAt(0));
        Assert.IsEmpty(list.Drain());
    }

    /// <summary>
    /// Appending overlapping ranges uses original source values and rejects insufficient capacity atomically.
    /// </summary>
    [TestMethod]
    public void FixedListCopiesOverlappingRanges()
    {
        int[] buffer = [1, 2, 3, 4, 5, 6];
        int count = 2;
        var list = new PgFixedList<int>(buffer, ref count);
        Assert.IsTrue(list.TryAddRange(buffer.AsSpan(1, 4)));
        Assert.AreSequenceEqual([1, 2, 2, 3, 4, 5], list.ToArray());
        Assert.IsTrue(list.TryAddRange([]));
        Assert.IsFalse(list.TryAddRange(buffer.AsSpan(0, 1)));
        Assert.AreEqual(6, count);
        Assert.AreSequenceEqual([1, 2, 2, 3, 4, 5], buffer);
        list.Clear();
        list.AddRange([7, 8, 9]);
        list.AddRange(list.AsSpan());
        Assert.AreSequenceEqual([7, 8, 9, 7, 8, 9], list.ToArray());
    }

    /// <summary>
    /// Views alias storage while copies of an owning inline-array aggregate remain independent.
    /// </summary>
    [TestMethod]
    public void FixedListViewsRespectStorageIdentity()
    {
        ListState state = default;
        PgFixedList<Point> first = state.AsList();
        PgFixedList<Point> alias = first;
        first.Add(new Point(11, 73));
        alias.Add(new Point(-7, 91));
        Assert.AreEqual(2, first.Count);
        Assert.AreEqual(new Point(-7, 91), first[1]);
        ListState copy = state;
        copy.AsList()[0] = new Point(99, 101);
        Assert.AreEqual(new Point(11, 73), state.AsList()[0]);
        Assert.AreEqual(new Point(99, 101), copy.AsList()[0]);
        alias.AsSpan()[0] = new Point(17, 19);
        Assert.AreEqual(new Point(17, 19), first[0]);
        Point[] owned = first.ToArray();
        alias.Clear();
        Assert.IsTrue(first.IsEmpty);
        Assert.AreSequenceEqual([new Point(17, 19), new Point(-7, 91)], owned);
    }

    /// <summary>
    /// Invalid indices and full-buffer exceptions leave both metadata and surrounding storage unchanged.
    /// </summary>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(2)]
    [DataRow(int.MaxValue)]
    public void FixedListRejectsInvalidIndices(int index)
    {
        int[] buffer = [91, 11, 13, 93];
        int count = 2;
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => new PgFixedList<int>(buffer.AsSpan(1, 2), ref count)[index]);
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => new PgFixedList<int>(buffer.AsSpan(1, 2), ref count)[index] = 99);
        Assert.AreEqual("index", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PgFixedList<int>(buffer.AsSpan(1, 2), ref count).RemoveAt(index)).ParamName);
        Assert.AreEqual("index", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PgFixedList<int>(buffer.AsSpan(1, 2), ref count).SwapRemoveAt(index)).ParamName);
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedList<int>(buffer.AsSpan(1, 2), ref count).Add(99));
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedList<int>(buffer.AsSpan(1, 2), ref count).Insert(1, 99));
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedList<int>(buffer.AsSpan(1, 2), ref count).AddRange([99]));
        Assert.AreEqual(2, count);
        Assert.AreSequenceEqual([91, 11, 13, 93], buffer);
    }

    /// <summary>
    /// Invalid metadata, default views and zero-capacity lists have explicit bounded behavior.
    /// </summary>
    [TestMethod]
    public void FixedListRejectsInvalidStateAndBounds()
    {
        int[] buffer = [11, 13];
        int count = -1;
        Assert.AreEqual("count", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedList<int>(buffer, ref count)).ParamName);
        count = 3;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedList<int>(buffer, ref count));
        count = 1;
        Assert.ThrowsExactly<InvalidOperationException>(() => ClearWithInvalidCount(buffer, ref count));
        Assert.AreSequenceEqual([11, 13], buffer);
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgFixedList<int>).Add(99));
        count = 0;
        var empty = new PgFixedList<int>([], ref count);
        Assert.IsTrue(empty.IsEmpty);
        Assert.IsTrue(empty.IsFull);
        Assert.IsFalse(empty.TryAdd(7));
        Assert.IsTrue(empty.TryAddRange([]));
        Assert.IsFalse(empty.TryPop(out int missing));
        Assert.AreEqual(0, missing);
        Assert.IsEmpty(empty.Drain());
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedList<int>([], ref count).Pop());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgFixedList<int>(buffer, ref count).Insert(-1, 17));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgFixedList<int>(buffer, ref count).Insert(1, 17));
        Assert.AreEqual(0, count);
        Assert.AreSequenceEqual([11, 13], buffer);
    }

    /// <summary>
    /// Invalidates metadata after constructing a view and attempts a mutation through that view.
    /// </summary>
    private static void ClearWithInvalidCount(int[] buffer, ref int count)
    {
        var list = new PgFixedList<int>(buffer, ref count);
        count = -1;
        list.Clear();
    }

    /// <summary>
    /// Supplies a custom unmanaged value with more than one field.
    /// </summary>
    private readonly record struct Point(int X, int Y);

    /// <summary>
    /// Owns three values without managed references.
    /// </summary>
    [InlineArray(3)]
    private struct Values
    {
        private Point _element;
    }

    /// <summary>
    /// Demonstrates the reusable ordinary C# storage pattern for a shared aggregate.
    /// </summary>
    private struct ListState
    {
        private Values _values;
        private int _count;

        /// <summary>
        /// Returns a view borrowing this instance's fields.
        /// </summary>
        [UnscopedRef]
        internal PgFixedList<Point> AsList() => new(_values, ref _count);
    }
}
