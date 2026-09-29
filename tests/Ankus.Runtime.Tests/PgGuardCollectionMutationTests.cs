using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Ankus.Runtime.Tests;

public sealed unsafe partial class PgSharedMemoryTests
{
    /// <summary>
    /// Borrowed collection views update original storage and metadata through both exclusive guard kinds.
    /// </summary>
    /// <param name="spin">Whether to use a spinlock rather than a lightweight lock.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GuardMutationsPreserveBoundedViews(bool spin)
    {
        using var spins = new NativeSpinLockTestFixture();
        using var fixture = new SharedFixture();
        Func<NativeMemoryRequest, NativeMemoryResult> shared = fixture.Memory.Handler!;
        fixture.Memory.Handler = request => request._operation == NativeMemoryOperation.SpinLock ? spins.Respond(request) : shared(request);
        var storage = new PgLwLock<MutableQueue>("queue");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = new byte[sizeof(MutableQueue)];
        using PgLwLockExclusiveGuard<MutableQueue>? lightweight = spin ? null : storage.Exclusive();
        using PgSpinLockGuard<MutableQueue>? spinlock = spin ? new PgSpinLock<MutableQueue>(default).Lock() : null;
        int[] append(ref MutableQueue value)
        {
            PgFixedDeque<int> queue = value.Items();
            queue.PushBack(17);
            queue.PushBack(19);
            queue.PushFront(13);
            return queue.ToArray();
        }

        Assert.AreSequenceEqual([13, 17, 19], spin ? spinlock!.Mutate(append) : lightweight!.Mutate(append));
        MutableQueue copy = spin ? spinlock!.Value : lightweight!.Value;
        Assert.AreSequenceEqual([13, 17, 19], copy.Items().ToArray());
        copy.Items().Clear();
        var expected = new FormatException("queue mutation failure");
        int failing(ref MutableQueue value)
        {
            PgFixedDeque<int> queue = value.Items();
            Assert.AreEqual(13, queue.PopFront());
            queue.PushBack(23);
            throw expected;
        }

        FormatException error = Assert.ThrowsExactly<FormatException>(() =>
            spin ? spinlock!.Mutate(failing) : lightweight!.Mutate(failing));
        Assert.AreSame(expected, error);
        int[] drain(ref MutableQueue value) => value.Items().Drain();
        Assert.AreSequenceEqual([17, 19, 23], spin ? spinlock!.Mutate(drain) : lightweight!.Mutate(drain));
        MutableQueue empty = spin ? spinlock!.Value : lightweight!.Value;
        Assert.AreEqual(0, empty.Items().Count);
        Assert.IsEmpty(empty.Items().ToArray());
    }

    /// <summary>
    /// Owns a small buffer whose first element is exposed through the compiler's inline-array conversion.
    /// </summary>
    [InlineArray(4)]
    private struct QueueItems
    {
        private int _element;
    }

    /// <summary>
    /// Owns the complete metadata and element region borrowed by a queue view.
    /// </summary>
    private struct MutableQueue
    {
        private QueueItems _items;
        private int _count;
        private int _head;

        /// <summary>
        /// Borrows this owner's original fields without copying the collection.
        /// </summary>
        [UnscopedRef]
        internal PgFixedDeque<int> Items() => new(_items, ref _count, ref _head);
    }
}
