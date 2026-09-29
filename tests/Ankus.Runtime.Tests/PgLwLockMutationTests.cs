using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

public sealed unsafe partial class PgSharedMemoryTests
{
    /// <summary>
    /// Exclusive mutations expose original ordinary and atomic fields while child locks require a separate reader.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsPreserveOriginalStorage()
    {
        using var spins = new NativeSpinLockTestFixture();
        using var fixture = new SharedFixture();
        Func<NativeMemoryRequest, NativeMemoryResult> shared = fixture.Memory.Handler!;
        fixture.Memory.Handler = request => request._operation == NativeMemoryOperation.SpinLock ? spins.Respond(request) : shared(request);
        var storage = new PgLwLock<MutableLockValue>("mutation");
        PgSharedMemory.Initialize(storage);
        MutableLockValue original = new(long.MinValue);
        fixture.Bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<MutableLockValue>(in original)).ToArray();
        using PgLwLockExclusiveGuard<MutableLockValue> guard = storage.Exclusive();
        MutableLockValue copy = guard.Value;
        Assert.AreEqual((long.MaxValue, 11L), guard.Mutate((ref value) =>
        {
            value._marker = long.MaxValue;
            value._bits = ulong.MaxValue;
            Assert.AreEqual(3L, value._atomic.Exchange(11));
            Assert.AreEqual(long.MaxValue, guard.Value._marker);
            GuardMutationAssertions.ChildAccessIsRejected(in value._child);
            return (value._marker, value._atomic.Value);
        }));
        Assert.AreEqual(long.MinValue, copy._marker);
        Assert.AreEqual(3L, copy._atomic.Value);
        Assert.AreEqual(ulong.MaxValue, guard.Value._bits);
        Assert.AreEqual(13L, guard.Read(static (in value) =>
        {
            using PgSpinLockGuard<long> child = value._child.Lock();
            Assert.AreEqual(7L, child.Value);
            return child.Mutate(static (ref stored) => stored = 13);
        }));
        NativeMemoryRequest admission = Assert.ContainsSingle(fixture.Memory.Requests.Where(static request => request._flags == 7));
        Assert.AreEqual(71, admission._context);
        Assert.AreEqual(20, admission._other);
        Assert.AreEqual((nuint)sizeof(MutableLockValue), admission._length);
        Assert.AreEqual(long.MaxValue, MemoryMarshal.Read<MutableLockValue>(fixture.Bytes)._marker);
        NativeBorrowScope.CheckBackendAccess();
    }

    /// <summary>
    /// Mutations and readonly callbacks exclude each other's original references while copied reads remain available.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsProtectParentOwnership()
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<long>("aliases");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(17L);
        using PgLwLockExclusiveGuard<long> guard = storage.Exclusive();
        PgLwLockExclusiveGuard<long> alias = guard;
        Assert.AreEqual(19L, guard.Mutate((ref value) =>
        {
            value = 19;
            Assert.AreEqual(19L, alias.Value);
            Assert.ThrowsExactly<InvalidOperationException>(alias.Dispose);
            Assert.ThrowsExactly<InvalidOperationException>(() => alias.Value = 99);
            Assert.ThrowsExactly<InvalidOperationException>(() => alias.Read(static (in nested) => nested));
            Assert.ThrowsExactly<InvalidOperationException>(() => alias.Mutate(static (ref nested) => ++nested));
            return value;
        }));
        Assert.AreEqual(19L, guard.Read((in value) =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => alias.Mutate(static (ref nested) => ++nested));
            return value;
        }));
        Assert.AreSequenceEqual([0, 2, 7, 6], fixture.Memory.Requests.Select(static request => request._flags));
        Assert.AreEqual(23L, alias.Mutate(static (ref value) => value = 23));
        alias.Value = 29;
        Assert.AreEqual(29L, guard.Value);
        alias.Dispose();
        Assert.HasCount(1, fixture.Memory.Requests.Where(static request => request._flags == 5));
    }

    /// <summary>
    /// Mutations persist when callbacks throw, and subsequent reads and mutations keep the same parent acquisition.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsUnwindAndRecover()
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<long>("exception");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(31L);
        using PgLwLockExclusiveGuard<long> guard = storage.Exclusive();
        var expected = new FormatException("original mutation failure");
        FormatException error = Assert.ThrowsExactly<FormatException>(() => guard.Mutate<int>((ref value) =>
        {
            value = 37;
            throw expected;
        }));
        Assert.AreSame(expected, error);
        Assert.AreEqual(37L, guard.Value);
        Assert.AreEqual(37L, guard.Read(static (in value) => value));
        NativeBorrowScope.CheckBackendAccess();
        Assert.IsEmpty(fixture.Memory.Requests.Where(static request => request._flags == 5));
        Assert.AreEqual(41L, guard.Mutate(static (ref value) => value = 41));
        guard.Dispose();
        Assert.HasCount(1, fixture.Memory.Requests.Where(static request => request._flags == 5));
    }

    /// <summary>
    /// A failed inner admission preserves an outer mutable reference and its backend restriction.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsComposeAlreadyHeldGuards()
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<long>("outer");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(43L);
        using PgLwLockExclusiveGuard<long> first = storage.Exclusive();
        var lease = new NativeSharedMemoryLease(NativeMemoryContext.BorrowScope, 73);
        NativeMemoryContext.BorrowScope.Register(lease);
        lease.Acquired(27);
        using var second = new PgLwLockExclusiveGuard<long>(lease);
        long[] other = GC.AllocateArray<long>(1, pinned: true);
        other[0] = 47;
        bool valid = true;
        Func<NativeMemoryRequest, NativeMemoryResult> respond = fixture.Memory.Handler!;
        fixture.Memory.Handler = request =>
        {
            if (request._context == 73 && request._flags is 6 or 7)
            {
                Assert.AreEqual(27, request._other);
                if (!valid)
                {
                    throw new PgException("55000", "exclusive admission rejected");
                }

                return new NativeMemoryResult { _data = (nint)Unsafe.AsPointer(ref other[0]), _length = sizeof(long) };
            }

            return respond(request);
        };
        Assert.AreEqual((53L, 59L), first.Mutate((ref value) =>
        {
            value = 53;
            Assert.AreEqual(59L, second.Mutate(static (ref nested) => nested = 59));
            valid = false;
            bool invoked = false;
            PgException error = Assert.ThrowsExactly<PgException>(() => second.Mutate((ref nested) =>
            {
                invoked = true;
                return ++nested;
            }));
            Assert.IsFalse(invoked);
            Assert.AreEqual("55000", error.SqlState);
            Assert.AreEqual("exclusive admission rejected", error.Message);
            Assert.ThrowsExactly<InvalidOperationException>(NativeBorrowScope.CheckBackendAccess);
            valid = true;
            return (value, second.Read(static (in nested) => nested));
        }));
        Assert.AreEqual(59L, other[0]);
        NativeBorrowScope.CheckBackendAccess();
        Assert.AreEqual(53L, first.Value);
        GC.KeepAlive(other);
    }

    /// <summary>
    /// Invalid native replies reject the callback without retaining an admission or losing later write access.
    /// </summary>
    /// <param name="scenario">The missing, misaligned, undersized, oversized or failed response.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void LwLockMutationsValidateAdmission(int scenario)
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<long>("admission");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(61L);
        using PgLwLockExclusiveGuard<long> guard = storage.Exclusive();
        Func<NativeMemoryRequest, NativeMemoryResult> respond = fixture.Memory.Handler!;
        fixture.Memory.Handler = request =>
        {
            NativeMemoryResult result = respond(request);
            if (request._flags == 7)
            {
                switch (scenario)
                {
                    case 0:
                        result._data = 0;
                        break;
                    case 1:
                        result._data++;
                        break;
                    case 2:
                        result._length--;
                        break;
                    case 3:
                        result._length++;
                        break;
                    default:
                        throw new PgException("55000", "not exclusive");
                }
            }

            return result;
        };
        bool invoked = false;
        long callback(ref long value)
        {
            invoked = true;
            return value = 67;
        }

        if (scenario == 4)
        {
            PgException error = Assert.ThrowsExactly<PgException>(() => guard.Mutate(callback));
            Assert.AreEqual("55000", error.SqlState);
            Assert.AreEqual("not exclusive", error.Message);
        }
        else
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => guard.Mutate(callback));
        }

        Assert.IsFalse(invoked);
        NativeBorrowScope.CheckBackendAccess();
        fixture.Memory.Handler = respond;
        Assert.AreEqual(61L, guard.Value);
        Assert.AreEqual(67L, guard.Mutate(callback));
        Assert.IsTrue(invoked);
        Assert.AreEqual(67L, BitConverter.ToInt64(fixture.Bytes));
    }

    /// <summary>
    /// Invalid owners and null callbacks do not publish mutable addresses or invoke user code.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsValidateOwnership()
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<long>("ownership");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(71L);
        using PgLwLockExclusiveGuard<long> guard = storage.Exclusive();
        Assert.AreEqual("mutator", Assert.ThrowsExactly<ArgumentNullException>(() => guard.Mutate<long>(null!)).ParamName);
        bool invoked = false;
        long mutator(ref long value)
        {
            invoked = true;
            return ++value;
        }

        foreach (nint provider in new nint[] { 0, 29 })
        {
            using MemoryContextTestFixture.Scope foreign = MemoryContextTestFixture.Enter(provider);
            Assert.ThrowsExactly<InvalidOperationException>(() => guard.Mutate(mutator));
        }

        GuardMutationAssertions.ForeignThreadIsRejected(() => guard.Mutate(mutator));
        guard.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => guard.Mutate(mutator));
        Assert.IsFalse(invoked);
        Assert.AreSequenceEqual([0, 2, 5], fixture.Memory.Requests.Select(static request => request._flags));
    }

    /// <summary>
    /// Backend entry is blocked only during mutation, and owned terminal diagnostics preserve written values.
    /// </summary>
    [TestMethod]
    public void LwLockMutationsPreserveBackendBoundaries()
    {
        using var fixture = new SharedFixture();
        var storage = new PgLwLock<int>("boundaries");
        PgSharedMemory.Initialize(storage);
        fixture.Bytes = BitConverter.GetBytes(73);
        using PgLwLockExclusiveGuard<int> guard = storage.Exclusive();
        nint previous = NativeLog.Enter(1);
        try
        {
            PgException error = Assert.ThrowsExactly<PgException>(() => guard.Mutate<int>(static (ref value) =>
            {
                value = 79;
                Assert.ThrowsExactly<InvalidOperationException>(() => Spi.Execute("SELECT 1/0"));
                Assert.ThrowsExactly<InvalidOperationException>(() => PgMemoryContext.Current);
                Assert.ThrowsExactly<InvalidOperationException>(() => NativeGuc.ReadInt32("example.count"));
                Assert.ThrowsExactly<InvalidOperationException>(() => PgLog.Write(PgLogLevel.Notice, "blocked"));
                PgLog.Write(PgLogLevel.Error, new PgDiagnostic("lightweight mutation error")
                {
                    SqlState = "P7844",
                    Detail = "owned detail",
                    Hint = "after unwind",
                });
                return 0;
            }));
            Assert.AreEqual("P7844", error.SqlState);
            Assert.AreEqual("lightweight mutation error", error.Message);
            Assert.AreEqual("owned detail", error.Detail);
            Assert.AreEqual("after unwind", error.Hint);
        }
        finally
        {
            NativeLog.Exit(previous);
        }

        Assert.AreSequenceEqual([0, 2, 7], fixture.Memory.Requests.Select(static request => request._flags));
        Assert.AreEqual(79, guard.Value);
        NativeBorrowScope.CheckBackendAccess();
        NativeMemoryRequest request = new() { _operation = NativeMemoryOperation.Current };
        fixture.Memory.Handler = fixture.Memory.Respond;
        NativeMemoryContext.Invoke(ref request, out NativeMemoryResult result);
        Assert.AreEqual(101, result._context);
    }
}
