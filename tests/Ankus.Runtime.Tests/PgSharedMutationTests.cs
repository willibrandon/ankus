namespace Ankus.Runtime.Tests;

public sealed unsafe partial class PgSharedTests
{
    /// <summary>
    /// An enclosing shared admission cannot allow a child spinlock while its parent value is mutable.
    /// </summary>
    [TestMethod]
    public void SpinMutationsComposeWithSharedStorage()
    {
        using var spins = new NativeSpinLockTestFixture();
        using var fixture = new SharedFixture();
        Func<NativeMemoryRequest, NativeMemoryResult> shared = fixture.Memory.Handler!;
        fixture.Memory.Handler = request => request._operation == NativeMemoryOperation.SpinLock ? spins.Respond(request) : shared(request);
        PgShared<PgSpinLockValue<MutableLockValue>> storage = fixture.Start(new PgSpinLockValue<MutableLockValue>(new MutableLockValue(67)));
        PgSpinLockGuard<MutableLockValue> expired = storage.Read((in cell) =>
        {
            PgSpinLockGuard<MutableLockValue> parent = cell.Lock();
            Assert.AreEqual(71L, parent.Mutate(static (ref value) =>
            {
                GuardMutationAssertions.ChildAccessIsRejected(in value._child);
                return value._marker = 71;
            }));
            Assert.AreEqual(73L, parent.Read(static (in value) =>
            {
                using PgSpinLockGuard<long> child = value._child.Lock();
                Assert.AreEqual(7L, child.Value);
                return child.Mutate(static (ref stored) => stored = 73);
            }));
            Assert.AreEqual(1, fixture.Access->_readers);
            return parent;
        });
        Assert.ThrowsExactly<ObjectDisposedException>(() => expired.Mutate(static (ref value) => value._marker = 99));
        Assert.AreEqual(0, fixture.Access->_readers);
        Assert.IsEmpty(spins.Held);
        Assert.HasCount(2, spins.Released);
        Assert.AreEqual((71L, 73L), storage.Read(static (in cell) =>
        {
            using PgSpinLockGuard<MutableLockValue> parent = cell.Lock();
            return parent.Read(static (in value) =>
            {
                using PgSpinLockGuard<long> child = value._child.Lock();
                return (value._marker, child.Value);
            });
        }));
        fixture.AssertGuards(sizeof(PgSpinLockValue<MutableLockValue>));
    }
}
