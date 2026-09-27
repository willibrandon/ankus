namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies fixed circular queues against explicit examples and an independent ordered model.
/// </summary>
[TestClass]
public sealed class PgFixedDequeTests
{
    /// <summary>
    /// Both ends wrap, preserve logical order and release slots while keeping exterior sentinels intact.
    /// </summary>
    [TestMethod]
    public void FixedDequePreservesWrappedOrder()
    {
        int[] buffer = [91, 0, 0, 0, 0, 93];
        int count = 0;
        int head = 0;
        var queue = new PgFixedDeque<int>(buffer.AsSpan(1, 4), ref count, ref head);
        Assert.AreEqual(4, queue.Capacity);
        Assert.IsTrue(queue.IsEmpty);
        Assert.IsFalse(queue.IsFull);
        queue.PushBack(20);
        queue.PushBack(30);
        queue.PushFront(10);
        Assert.AreEqual(3, head);
        Assert.IsTrue(queue.TryPushFront(5));
        Assert.IsTrue(queue.IsFull);
        Assert.IsFalse(queue.TryPushFront(99));
        Assert.IsFalse(queue.TryPushBack(99));
        Assert.AreSequenceEqual([5, 10, 20, 30], queue.ToArray());
        var enumerated = new List<int>();
        foreach (int value in queue)
        {
            enumerated.Add(value);
        }

        Assert.AreSequenceEqual([5, 10, 20, 30], enumerated);
        PgFixedDequeEnumerator<int> iterator = queue.GetEnumerator();
        for (int index = 0; index < enumerated.Count; index++)
        {
            Assert.IsTrue(iterator.MoveNext());
            Assert.AreEqual(enumerated[index], iterator.Current);
        }

        Assert.IsFalse(iterator.MoveNext());
        Assert.IsFalse(iterator.MoveNext());
        queue.GetSpans(out Span<int> first, out Span<int> second);
        Assert.AreSequenceEqual([5, 10], first.ToArray());
        Assert.AreSequenceEqual([20, 30], second.ToArray());
        first[0] = 7;
        queue[2] = 23;
        Assert.AreEqual(23, second[0]);
        Assert.AreEqual(7, queue[0]);
        Assert.AreEqual(7, queue.PopFront());
        Assert.AreEqual(30, queue.PopBack());
        queue.PushBack(40);
        queue.PushBack(50);
        Assert.AreSequenceEqual([10, 23, 40, 50], queue.ToArray());
        int[] output = [81, 0, 0, 0, 0, 83];
        queue.CopyTo(output.AsSpan(1));
        Assert.AreSequenceEqual([81, 10, 23, 40, 50, 83], output);
        Assert.AreSequenceEqual([10, 23, 40, 50], queue.Drain());
        Assert.AreEqual(0, count);
        Assert.AreEqual(0, head);
        Assert.AreSequenceEqual([91, 0, 0, 0, 0, 93], buffer);
        Assert.IsFalse(queue.TryPopFront(out int missingFront));
        Assert.IsFalse(queue.TryPopBack(out int missingBack));
        Assert.AreEqual(0, missingFront);
        Assert.AreEqual(0, missingBack);
        queue.PushFront(73);
        Assert.AreEqual(73, queue.PopBack());
        Assert.AreEqual(0, head);
        queue.PushFront(79);
        Assert.AreEqual(79, queue.PopFront());
        Assert.AreEqual(0, head);
    }

    /// <summary>
    /// Mixed operations agree with a separate list model across many full, empty and wrapped states.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    public void FixedDequeMatchesOrderedModel(int capacity)
    {
        int[] buffer = new int[capacity];
        int count = 0;
        int head = 0;
        var queue = new PgFixedDeque<int>(buffer, ref count, ref head);
        var expected = new List<int>();
        var random = new Random(73);
        for (int step = 0; step < 1000; step++)
        {
            int operation = random.Next(4);
            bool allowed = operation < 2 ? expected.Count < capacity : expected.Count > 0;
            switch (operation)
            {
                case 0:
                    Assert.AreEqual(allowed, queue.TryPushFront(step));
                    if (allowed)
                    {
                        expected.Insert(0, step);
                    }

                    break;
                case 1:
                    Assert.AreEqual(allowed, queue.TryPushBack(step));
                    if (allowed)
                    {
                        expected.Add(step);
                    }

                    break;
                case 2:
                    Assert.AreEqual(allowed, queue.TryPopFront(out int front));
                    Assert.AreEqual(allowed ? expected[0] : 0, front);
                    if (allowed)
                    {
                        expected.RemoveAt(0);
                    }

                    break;
                default:
                    Assert.AreEqual(allowed, queue.TryPopBack(out int back));
                    Assert.AreEqual(allowed ? expected[^1] : 0, back);
                    if (allowed)
                    {
                        expected.RemoveAt(expected.Count - 1);
                    }

                    break;
            }

            Assert.AreEqual(expected.Count, queue.Count);
            Assert.AreSequenceEqual(expected, queue.ToArray(), $"Operation {step} at capacity {capacity}.");
        }
    }

    /// <summary>
    /// Full, empty, index and copy failures do not modify any storage.
    /// </summary>
    [TestMethod]
    public void FixedDequeRejectsInvalidStateAndBounds()
    {
        int[] buffer = [11, 13];
        int count = 2;
        int head = 1;
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedDeque<int>(buffer, ref count, ref head).PushFront(99));
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedDeque<int>(buffer, ref count, ref head).PushBack(99));
        Assert.AreEqual("index", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PgFixedDeque<int>(buffer, ref count, ref head)[-1]).ParamName);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PgFixedDeque<int>(buffer, ref count, ref head)[2] = 99);
        int[] shortDestination = [73];
        Assert.AreEqual("destination", Assert.ThrowsExactly<ArgumentException>(() =>
            new PgFixedDeque<int>(buffer, ref count, ref head).CopyTo(shortDestination)).ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => new PgFixedDeque<int>(buffer, ref count, ref head).CopyTo(buffer));
        Assert.AreSequenceEqual([73], shortDestination);
        Assert.AreSequenceEqual([11, 13], buffer);
        Assert.AreEqual(2, count);
        Assert.AreEqual(1, head);
        count = 1;
        Assert.AreEqual("head", Assert.ThrowsExactly<ArgumentException>(() => _ = new PgFixedDeque<int>(buffer, ref count, ref count)).ParamName);
        count = -1;
        Assert.AreEqual("count", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedDeque<int>(buffer, ref count, ref head)).ParamName);
        count = 3;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedDeque<int>(buffer, ref count, ref head));
        count = 0;
        head = -1;
        Assert.AreEqual("head", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedDeque<int>(buffer, ref count, ref head)).ParamName);
        head = 2;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new PgFixedDeque<int>(buffer, ref count, ref head));
        head = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => ClearWithInvalidHead(buffer, ref count, ref head));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgFixedDeque<int>).PushBack(99));
        head = 0;
        var empty = new PgFixedDeque<int>([], ref count, ref head);
        Assert.IsTrue(empty.IsFull);
        Assert.IsTrue(empty.IsEmpty);
        Assert.IsFalse(empty.TryPushBack(7));
        Assert.IsFalse(empty.TryPushFront(7));
        Assert.IsEmpty(empty.Drain());
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedDeque<int>([], ref count, ref head).PopFront());
        Assert.ThrowsExactly<InvalidOperationException>(() => new PgFixedDeque<int>([], ref count, ref head).PopBack());
        Assert.AreSequenceEqual([11, 13], buffer);
        Assert.AreEqual(0, count);
        Assert.AreEqual(0, head);
    }

    /// <summary>
    /// Invalidates metadata after constructing a view and attempts a mutation through that view.
    /// </summary>
    private static void ClearWithInvalidHead(int[] buffer, ref int count, ref int head)
    {
        var queue = new PgFixedDeque<int>(buffer, ref count, ref head);
        head = buffer.Length;
        queue.Clear();
    }

    /// <summary>
    /// Enumerators reject Current outside their valid range, including zero-capacity and default instances.
    /// </summary>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public void FixedDequeEnumeratorsRejectInvalidPositions(int count, bool finish)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => ReadInvalidCurrent(count, finish));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(PgFixedDequeEnumerator<int>).Current);
    }

    /// <summary>
    /// Reads a borrowed enumerator before starting it or after reaching the end.
    /// </summary>
    private static int ReadInvalidCurrent(int count, bool finish)
    {
        Span<int> buffer = stackalloc int[count];
        int head = 0;
        var queue = new PgFixedDeque<int>(buffer, ref count, ref head);
        PgFixedDequeEnumerator<int> iterator = queue.GetEnumerator();
        if (finish)
        {
            while (iterator.MoveNext())
            {
                _ = iterator.Current;
            }
        }

        return iterator.Current;
    }
}
