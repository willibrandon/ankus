using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies mapped scalar result transport, temporary ownership and error preservation without a backend.
/// </summary>
[TestClass]
public sealed unsafe class PgMappedResultTests
{
    /// <summary>
    /// Registers isolated closed readers without invoking their factories or PostgreSQL.
    /// </summary>
    static PgMappedResultTests()
    {
        PgDatumRegistry.RegisterValue<Number>("result", "fixed", PgTypeOrigin.External, typeof(NumberReader),
            static () => new NumberReader(), true, false);
        PgDatumRegistry.RegisterValue<Alias>("result", "fixed", PgTypeOrigin.External, typeof(AliasReader),
            static () => new AliasReader(), true, false);
        PgDatumRegistry.RegisterValue<Absent>("result", "fixed", PgTypeOrigin.External, typeof(UnusedReader<Absent>),
            static () => throw new InvalidOperationException("An absent result must not construct a reader."), true, false);
        PgDatumRegistry.RegisterReference<AbsentReference>("result", "fixed", PgTypeOrigin.External, typeof(UnusedReader<AbsentReference>),
            static () => throw new InvalidOperationException("An absent reference must not construct a reader."), true, false);
        PgDatumRegistry.RegisterValue<WriteOnly>("result", "fixed", PgTypeOrigin.External, typeof(UnusedWriter),
            static () => throw new InvalidOperationException("A write-only result must not construct a converter."), false, true);
        PgDatumRegistry.RegisterValue<FactoryFailure>("result", "fixed", PgTypeOrigin.External, typeof(UnusedReader<FactoryFailure>),
            static () =>
            {
                Script.Current.FactoryCalls++;
                throw Script.Current.Primary!;
            }, true, false);
    }

    /// <summary>
    /// Every SPI owner uses raw reader-only results and retains declared CLR identity for a shared SQL OID.
    /// </summary>
    /// <param name="api">Static, session, retained plan, or session plan.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ReaderOnlyResultsWorkAcrossSpiOwners(int api)
    {
        using var script = new Script { Cells = [new(9001, 7), new(9001, 9), new(9001, 11)] };
        (Number First, Alias Second, Number Third) values = api switch
        {
            0 => Spi.ExecuteScalars<Number, Alias, Number>("SELECT values"),
            1 => Spi.Connect(static session => session.ExecuteScalars<Number, Alias, Number>("SELECT values")),
            2 => ReadPlan(null),
            _ => Spi.Connect(static session => ReadPlan(session)),
        };
        Assert.AreEqual(new Number(107), values.First);
        Assert.AreEqual(new Alias(209), values.Second);
        Assert.AreEqual(new Number(111), values.Third);
        Assert.AreEqual(3, script.Reads);
        Assert.AreEqual(1, script.Executions);
        Assert.AreEqual(1, script.ResultReleases);
        Assert.AreEqual(1, script.Deletes);
        Assert.AreEqual(SpiResultMode.Triple, script.LastMode);
        Assert.AreEqual(0, script.LastLimit);
        Assert.IsNotNull(script.Captured);
        Assert.ThrowsExactly<ObjectDisposedException>(() => script.Captured.DangerousGetBits());
    }

    /// <summary>
    /// A later writer-only position rejects before SQL, catalog lookup or result-owner allocation.
    /// </summary>
    [TestMethod]
    public void ReadCapabilityPreflightsEverySelectedPosition()
    {
        using var script = new Script();
        Assert.ThrowsExactly<NotSupportedException>(() => Spi.ExecuteScalar<WriteOnly?>("SELECT NULL WHERE false"));
        Assert.ThrowsExactly<NotSupportedException>(() => Spi.ExecuteScalars<WriteOnly, Number>("SELECT values"));
        Assert.ThrowsExactly<NotSupportedException>(() => Spi.ExecuteScalars<PgAnyElement, WriteOnly>("SELECT values"));
        Assert.ThrowsExactly<NotSupportedException>(() => Spi.ExecuteScalars<PgAnyElement, Number, WriteOnly>("SELECT values"));
        Assert.ThrowsExactly<NotSupportedException>(() => PgFunctions.Call<WriteOnly>("fixed.result"));
        Assert.ThrowsExactly<NotSupportedException>(() => PgFunctions.Call<WriteOnly?>(77));
        Assert.ThrowsExactly<NotSupportedException>(() => Spi.ExecuteScalars<Number, Number[]>("SELECT values"));
        Assert.ThrowsExactly<NotSupportedException>(() => PgFunctions.Call<Number[]>("fixed.result"));
        Assert.ThrowsExactly<NotSupportedException>(() => PgFunctions.DangerousCall<Number>(1, 0));
        Assert.AreEqual(0, script.Executions);
        Assert.AreEqual(0, script.Lookups);
        Assert.AreEqual(0, script.Creates);
        Assert.IsEmpty(script.Memory.Requests);
    }

    /// <summary>
    /// Present NULL validates exact OID, while absent rows and utility results invent no datum or reader call.
    /// </summary>
    [TestMethod]
    public void NullAndEmptyResultsPreserveIdentityAndAbsenceRules()
    {
        using var script = new Script { Cells = [new(9001, 0, true)] };
        Assert.IsNull(Spi.ExecuteScalar<Absent?>("SELECT NULL"));
        Assert.IsNull(Spi.ExecuteScalar<AbsentReference>("SELECT NULL"));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.ExecuteScalar<Absent>("SELECT NULL"));
        Assert.AreEqual(3, script.Lookups);
        script.Cells = [new(23, 0, true)];
        Assert.ThrowsExactly<InvalidCastException>(() => Spi.ExecuteScalar<Absent?>("SELECT wrong NULL"));
        int lookups = script.Lookups;
        script.Rows = 0;
        Assert.IsNull(Spi.ExecuteScalar<Absent?>("SELECT wrong WHERE false"));
        (Absent? missingValue, AbsentReference? missingReference) = Spi.ExecuteScalars<Absent?, AbsentReference>("SELECT nothing");
        Assert.IsNull(missingValue);
        Assert.IsNull(missingReference);
        script.Cells = [];
        Assert.IsNull(Spi.ExecuteScalar<AbsentReference>("CREATE TABLE unused"));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.ExecuteScalar<Absent>("SELECT nothing"));
        Assert.AreEqual(lookups, script.Lookups);
        script.Rows = 1;
        Assert.IsNull(Spi.ExecuteScalar<AbsentReference>("SELECT FROM source"));
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.ExecuteScalars<Absent?, AbsentReference>("SELECT FROM source"));
        script.Cells = [new(9001, 0, true)];
        Assert.ThrowsExactly<InvalidOperationException>(() => Spi.ExecuteScalars<Absent?, AbsentReference>("SELECT one NULL"));
        Assert.AreEqual(0, script.Reads);
        Assert.AreEqual(script.Creates, script.Deletes);
        Assert.AreEqual(script.Executions, script.ResultReleases);
    }

    /// <summary>
    /// Mapped catalog calls send exact mode, refresh catalog OIDs, preserve defaults and delete escaped input storage.
    /// </summary>
    /// <param name="byOid">Whether to select the function by OID.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CatalogMappedResultsUseExactModeAndTemporaryOwners(bool byOid)
    {
        using var script = new Script();
        PgFunctionArgument[] arguments = [PgFunctionArgument.Create(7), PgFunctionArgument.Default<int>()];
        Number first = byOid ? PgFunctions.Call<Number>(77, arguments) : PgFunctions.Call<Number>("fixed.result", arguments);
        Assert.AreEqual(new Number(142), first);
        Assert.AreEqual(1, script.CallMode);
        Assert.AreEqual(9001U, script.ExpectedOid);
        Assert.AreEqual(byOid ? 77U : 0U, script.FunctionOid);
        Assert.AreSequenceEqual<byte>([0, 1], script.Defaults);
        Assert.AreEqual(101, script.ParentContext);
        Assert.AreNotEqual(101, script.ResultContext);
        Assert.IsNotNull(script.Captured);
        Assert.ThrowsExactly<ObjectDisposedException>(() => script.Captured.DangerousGetBits());
        script.Oid = 9002;
        script.ResultOid = 9002;
        Assert.AreEqual(new Number(142), PgFunctions.Call<Number>("fixed.result"));
        Assert.AreEqual(9002U, script.ExpectedOid);
        Assert.AreEqual(4, script.Lookups);
        Assert.AreEqual(2, script.Deletes);
        Assert.AreEqual(2, script.ResultReleases);
    }

    /// <summary>
    /// Raw, polymorphic, void and built-in catalog calls retain mode zero independently of result ownership.
    /// </summary>
    [TestMethod]
    public void ExistingCatalogResultsRetainCompatibilityMode()
    {
        using var script = new Script();
        Assert.AreEqual(42, PgFunctions.Call<int>("fixed.result"));
        Assert.AreEqual(0, script.CallMode);
        Assert.AreEqual(23U, script.ExpectedOid);
        Assert.AreEqual(0, script.ResultContext);
        PgFunctions.Call("fixed.result");
        Assert.AreEqual(0, script.CallMode);
        Assert.AreEqual(2278U, script.ExpectedOid);
        using PgMemoryContext owner = PgMemoryContext.Create("explicit");
        PgDatum raw = PgFunctions.CallRaw("fixed.result", owner);
        Assert.AreEqual((nuint)42, raw.DangerousGetBits());
        Assert.AreEqual(0, script.CallMode);
        Assert.AreEqual(0U, script.ExpectedOid);
        Assert.AreEqual(owner.Id, script.ResultContext);
        PgAnyElement value = PgFunctions.Call<PgAnyElement>("fixed.result");
        Assert.AreEqual((nuint)42, value.Datum.DangerousGetBits());
        Assert.AreEqual(0, script.CallMode);
        Assert.AreEqual(2283U, script.ExpectedOid);
        Assert.AreEqual(101, script.ResultContext);
        Assert.AreEqual(0, script.Reads);
    }

    /// <summary>
    /// Catalog NULL skips reader construction after identity validation, including wrong-OID rejection.
    /// </summary>
    [TestMethod]
    public void CatalogNullResultsKeepCheckedIdentityWithoutReaderCalls()
    {
        using var script = new Script { IsNull = true };
        Assert.IsNull(PgFunctions.Call<Absent?>("fixed.result"));
        Assert.IsNull(PgFunctions.Call<AbsentReference>(77));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgFunctions.Call<Absent>("fixed.result"));
        script.ResultOid = 23;
        Assert.ThrowsExactly<InvalidCastException>(() => PgFunctions.Call<Absent?>("fixed.result"));
        Assert.AreEqual(0, script.Reads);
        Assert.AreEqual(4, script.Deletes);
        Assert.AreEqual(4, script.ResultReleases);
    }

    /// <summary>
    /// Mixed reads detach mapped values and separately preserve a polymorphic value beyond temporary deletion.
    /// </summary>
    [TestMethod]
    public void MixedResultsCopyPolymorphicStorageBeforeTemporaryCleanup()
    {
        using var script = new Script { Cells = [new(9001, 7), new(23, 9), new(23, 11)] };
        (Number mapped, int ordinary, PgAnyElement polymorphic) = Spi.ExecuteScalars<Number, int, PgAnyElement>("SELECT mixed");
        Assert.AreEqual(new Number(107), mapped);
        Assert.AreEqual(9, ordinary);
        Assert.AreEqual((nuint)11, polymorphic.Datum.DangerousGetBits());
        Assert.AreEqual(101, script.CopyDestination);
        Assert.AreEqual(1, script.Copies);
        Assert.AreEqual(1, script.Deletes);
        Assert.IsNotNull(script.Captured);
        Assert.ThrowsExactly<ObjectDisposedException>(() => script.Captured.DangerousGetBits());
    }

    /// <summary>
    /// Reader-only, cleanup-only and combined failures preserve the correct primary and release native transport.
    /// </summary>
    /// <param name="catalog">Whether the temporary owner belongs to a catalog call rather than SPI.</param>
    /// <param name="readerFailure">Whether the reader fails.</param>
    /// <param name="cleanupFailure">Whether deleting the owner fails.</param>
    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public void ConversionAndCleanupFailuresPreserveTheirOrdering(bool catalog, bool readerFailure, bool cleanupFailure)
    {
        var primary = new InvalidOperationException("reader failed");
        using var script = new Script { Primary = readerFailure ? primary : null, FailDelete = cleanupFailure };
        Action read = catalog ? () => PgFunctions.Call<Number>("fixed.result") : () => Spi.ExecuteScalar<Number>("SELECT value");
        if (readerFailure && cleanupFailure)
        {
            AggregateException failure = Assert.ThrowsExactly<AggregateException>(read);
            Assert.HasCount(2, failure.InnerExceptions);
            Assert.AreSame(primary, failure.InnerExceptions[0]);
            PgException cleanup = Assert.IsInstanceOfType<PgException>(failure.InnerExceptions[1]);
            Assert.AreEqual("55006", cleanup.SqlState);
            Assert.AreEqual("result cleanup failed", cleanup.Message);
            Assert.AreEqual("cleanup detail", cleanup.Detail);
        }
        else if (readerFailure)
        {
            Assert.AreSame(primary, Assert.ThrowsExactly<InvalidOperationException>(read));
        }
        else
        {
            PgException cleanup = Assert.ThrowsExactly<PgException>(read);
            Assert.AreEqual("55006", cleanup.SqlState);
            Assert.AreEqual("result cleanup failed", cleanup.Message);
            Assert.AreEqual("cleanup detail", cleanup.Detail);
        }

        if (readerFailure)
        {
            Assert.Contains(nameof(ThrowReader), primary.StackTrace!);
        }

        Assert.AreEqual(1, script.Deletes);
        Assert.AreEqual(1, script.ResultReleases);
        Assert.IsNotNull(script.Captured);
        if (cleanupFailure)
        {
            Assert.AreEqual((nuint)42, script.Captured.DangerousGetBits());
        }
        else
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => script.Captured.DangerousGetBits());
        }
    }

    /// <summary>
    /// A raw SPI acquisition error survives owner cleanup, with both diagnostics retained if deletion also fails.
    /// </summary>
    /// <param name="cleanupFailure">Whether deleting the newly acquired owner also fails.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RawAcquisitionErrorsPreserveNativeDiagnosticsAndCleanup(bool cleanupFailure)
    {
        using var script = new Script { FailExecution = true, FailDelete = cleanupFailure };
        PgException primary;
        if (cleanupFailure)
        {
            AggregateException failure = Assert.ThrowsExactly<AggregateException>(() => Spi.ExecuteScalar<Number>("SELECT failure"));
            Assert.HasCount(2, failure.InnerExceptions);
            primary = Assert.IsInstanceOfType<PgException>(failure.InnerExceptions[0]);
            PgException cleanup = Assert.IsInstanceOfType<PgException>(failure.InnerExceptions[1]);
            Assert.AreEqual("55006", cleanup.SqlState);
            Assert.AreEqual("result cleanup failed", cleanup.Message);
            Assert.AreEqual("cleanup detail", cleanup.Detail);
        }
        else
        {
            primary = Assert.ThrowsExactly<PgException>(() => Spi.ExecuteScalar<Number>("SELECT failure"));
        }

        Assert.AreEqual("P8501", primary.SqlState);
        Assert.AreEqual("native acquisition failed", primary.Message);
        Assert.AreEqual("owned detail", primary.Detail);
        Assert.AreEqual("owned hint", primary.Hint);
        Assert.AreEqual(1, script.Deletes);
        Assert.AreEqual(1, script.ResultReleases);
        Assert.AreEqual(0, script.Reads);
    }

    /// <summary>
    /// A shared lazy factory failure retains its identity while each catalog call releases its own result owner.
    /// </summary>
    [TestMethod]
    public void CatalogFactoryFailureIsCachedWhileEveryOwnerIsReleased()
    {
        var primary = new PgException("P8502", "factory failed", detail: "reader detail", hint: "reader hint");
        using var script = new Script { Primary = primary };
        Assert.AreSame(primary, Assert.ThrowsExactly<PgException>(() => PgFunctions.Call<FactoryFailure>("fixed.result")));
        Assert.AreSame(primary, Assert.ThrowsExactly<PgException>(() => PgFunctions.Call<FactoryFailure>(77)));
        Assert.AreEqual(1, script.FactoryCalls);
        Assert.AreEqual(2, script.Deletes);
        Assert.AreEqual(2, script.ResultReleases);
    }

    /// <summary>
    /// The shared cleanup helper preserves both original exception objects and propagates cleanup alone unchanged.
    /// </summary>
    [TestMethod]
    public void CleanupHelperRetainsOriginalExceptionInstances()
    {
        var primary = new InvalidOperationException("primary");
        var cleanup = new ArgumentException("cleanup");
        var owner = new ThrowingOwner(cleanup);
        AggregateException combined = Assert.ThrowsExactly<AggregateException>(() => PgResultCleanup.Dispose(owner, primary));
        Assert.AreSame(primary, combined.InnerExceptions[0]);
        Assert.AreSame(cleanup, combined.InnerExceptions[1]);
        Assert.AreSame(cleanup, Assert.ThrowsExactly<ArgumentException>(() => PgResultCleanup.Dispose(owner, null)));
        Assert.AreEqual(2, owner.Disposals);
    }

    /// <summary>
    /// Reads through a retained or session-owned prepared plan, ending its owner before returning values.
    /// </summary>
    private static (Number, Alias, Number) ReadPlan(SpiSession? session)
    {
        using SpiPreparedStatement plan = session is null ? Spi.Prepare("SELECT values") : session.Prepare("SELECT values");
        return plan.ExecuteScalars<Number, Alias, Number>();
    }

    /// <summary>
    /// Gives a primary reader failure a stable stack origin independent of cleanup and rethrow sites.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowReader(Exception exception) => throw exception;

    /// <summary>
    /// Supplies a detached value selected by its requested managed identity.
    /// </summary>
    private sealed class NumberReader : IPgDatumReader<Number>
    {
        /// <inheritdoc />
        public Number Read(PgDatum value)
        {
            Script script = Script.Current;
            script.Reads++;
            script.Captured = value;
            if (script.Primary is { } failure)
            {
                ThrowReader(failure);
            }

            return new Number(checked((int)value.DangerousGetBits()) + 100);
        }
    }

    /// <summary>
    /// Shares the SQL identity but returns an independently distinguishable managed representation.
    /// </summary>
    private sealed class AliasReader : IPgDatumReader<Alias>
    {
        /// <inheritdoc />
        public Alias Read(PgDatum value)
        {
            Script.Current.Reads++;
            return new Alias(checked((int)value.DangerousGetBits()) + 200);
        }
    }

    /// <summary>
    /// Defines the exact reader contract for a factory that must never return an instance.
    /// </summary>
    /// <typeparam name="T">The requested absent or failing managed result.</typeparam>
    private sealed class UnusedReader<T> : IPgDatumReader<T>
    {
        /// <inheritdoc />
        public T Read(PgDatum value) => throw new InvalidOperationException("The unused reader must not execute.");
    }

    /// <summary>
    /// Defines the write-only direction independently of the unsupported result read.
    /// </summary>
    private sealed class UnusedWriter : IPgDatumWriter<WriteOnly>
    {
        /// <inheritdoc />
        public PgDatum Write(WriteOnly value, uint typeOid, PgMemoryContext destination)
            => throw new InvalidOperationException("The unused writer must not execute.");
    }

    /// <summary>
    /// Throws a specific cleanup exception without crossing native error transport.
    /// </summary>
    private sealed class ThrowingOwner(Exception failure) : IDisposable
    {
        /// <summary>
        /// Gets the number of cleanup attempts.
        /// </summary>
        internal int Disposals { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            Disposals++;
            throw failure;
        }
    }

    /// <summary>
    /// Scripts native envelopes and memory lifetimes without emulating PostgreSQL execution.
    /// </summary>
    private sealed class Script : IDisposable
    {
        [ThreadStatic]
        private static Script? s_current;
        private readonly Script? _previous = s_current;
        private readonly MemoryContextTestFixture.Scope _memoryScope;
        private readonly nint _previousBackend;
        private readonly HashSet<nint> _deleted = [];

        /// <summary>
        /// Installs synchronous thread-local backend and memory responders.
        /// </summary>
        internal Script()
        {
            s_current = this;
            Memory = new MemoryContextTestFixture();
            Memory.Handler = RespondMemory;
            _memoryScope = MemoryContextTestFixture.Enter();
            _previousBackend = NativeBackend.Enter((nint)(delegate* unmanaged[Cdecl]<NativeSpiRequest*, NativeSpiResult*, NativeCallError*, int>)&Invoke);
        }

        /// <summary>
        /// Gets the current thread's scripted operation state.
        /// </summary>
        internal static Script Current => s_current!;

        /// <summary>
        /// Gets the native memory responder and recorded requests.
        /// </summary>
        internal MemoryContextTestFixture Memory { get; }

        /// <summary>
        /// Gets or sets independently supplied result cells.
        /// </summary>
        internal Cell[] Cells { get; set; } = [new(9001, 42)];

        /// <summary>
        /// Gets or sets the returned row count.
        /// </summary>
        internal int Rows { get; set; } = 1;

        /// <summary>
        /// Gets or sets the current resolved mapping identity.
        /// </summary>
        internal uint Oid { get; set; } = 9001;

        /// <summary>
        /// Gets or sets the actual catalog result identity.
        /// </summary>
        internal uint ResultOid { get; set; } = 9001;

        /// <summary>
        /// Gets or sets whether a catalog result is SQL NULL.
        /// </summary>
        internal bool IsNull { get; set; }

        /// <summary>
        /// Gets or sets whether native owner deletion fails.
        /// </summary>
        internal bool FailDelete { get; set; }

        /// <summary>
        /// Gets or sets whether native result acquisition fails.
        /// </summary>
        internal bool FailExecution { get; set; }

        /// <summary>
        /// Gets or sets the original managed reader or factory failure.
        /// </summary>
        internal Exception? Primary { get; set; }

        /// <summary>
        /// Gets or sets the reader input retained for lifetime assertions.
        /// </summary>
        internal PgDatum? Captured { get; set; }

        /// <summary>
        /// Gets or sets the number of executed reader calls.
        /// </summary>
        internal int Reads { get; set; }

        /// <summary>
        /// Gets or sets the number of failing factory attempts.
        /// </summary>
        internal int FactoryCalls { get; set; }

        /// <summary>
        /// Gets the number of live mapping identity resolutions.
        /// </summary>
        internal int Lookups { get; private set; }

        /// <summary>
        /// Gets the number of result owner allocations.
        /// </summary>
        internal int Creates { get; private set; }

        /// <summary>
        /// Gets the number of attempted owner deletions.
        /// </summary>
        internal int Deletes { get; private set; }

        /// <summary>
        /// Gets the number of native execution requests.
        /// </summary>
        internal int Executions { get; private set; }

        /// <summary>
        /// Gets the number of released native transport envelopes.
        /// </summary>
        internal int ResultReleases { get; private set; }

        /// <summary>
        /// Gets the number of native datum copies.
        /// </summary>
        internal int Copies { get; private set; }

        /// <summary>
        /// Gets the last catalog result validation mode.
        /// </summary>
        internal int CallMode { get; private set; }

        /// <summary>
        /// Gets the requested catalog result identity.
        /// </summary>
        internal uint ExpectedOid { get; private set; }

        /// <summary>
        /// Gets the explicit catalog function identity.
        /// </summary>
        internal uint FunctionOid { get; private set; }

        /// <summary>
        /// Gets the parent of the most recently created owner.
        /// </summary>
        internal nint ParentContext { get; private set; }

        /// <summary>
        /// Gets the last catalog result destination.
        /// </summary>
        internal nint ResultContext { get; private set; }

        /// <summary>
        /// Gets the last polymorphic copy destination.
        /// </summary>
        internal nint CopyDestination { get; private set; }

        /// <summary>
        /// Gets the captured argument default markers.
        /// </summary>
        internal byte[] Defaults { get; private set; } = [];

        /// <summary>
        /// Gets the selected SPI result width.
        /// </summary>
        internal SpiResultMode LastMode { get; private set; }

        /// <summary>
        /// Gets the requested SQL execution row limit.
        /// </summary>
        internal int LastLimit { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            NativeBackend.Exit(_previousBackend);
            _memoryScope.Dispose();
            Memory.Dispose();
            s_current = _previous;
        }

        /// <summary>
        /// Records deletion success separately from attempts so failed cleanup leaves its owner live.
        /// </summary>
        private NativeMemoryResult RespondMemory(NativeMemoryRequest request)
        {
            if (request._operation == NativeMemoryOperation.Callback)
            {
                return new NativeMemoryResult { _context = 101 };
            }

            if (request._operation == NativeMemoryOperation.Create)
            {
                ParentContext = request._context;
                return new NativeMemoryResult { _context = 201 + ++Creates };
            }

            if (request._operation == NativeMemoryOperation.Delete)
            {
                Deletes++;
                if (FailDelete)
                {
                    throw new PgException("55006", "result cleanup failed", detail: "cleanup detail");
                }

                _deleted.Add(request._context);
            }

            if (_deleted.Contains(request._context) && request._operation is NativeMemoryOperation.CaptureGeneration or NativeMemoryOperation.Name)
            {
                throw new PgException("55000", "deleted result owner");
            }

            return Memory.Respond(request);
        }

        /// <summary>
        /// Returns literal rows or scalar envelopes and records produced request modes.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int Invoke(NativeSpiRequest* request, NativeSpiResult* result, NativeCallError* error)
        {
            try
            {
                Current.Respond(request, result);
                return 0;
            }
            catch (Exception exception)
            {
                NativeError.Write(exception, error);
                return 1;
            }
        }

        /// <summary>
        /// Responds only to the existing transport operations needed by these result contracts.
        /// </summary>
        private void Respond(NativeSpiRequest* request, NativeSpiResult* result)
        {
            if (request->_operation == SpiOperation.DatumType)
            {
                Lookups++;
                result->_text.Integral = Oid;
                return;
            }

            if (request->_operation == SpiOperation.OpenSession)
            {
                request->_sessionId = 51;
                return;
            }

            if (request->_operation == SpiOperation.Prepare)
            {
                request->_plan = 71;
                return;
            }

            if (request->_operation is SpiOperation.CloseSession or SpiOperation.FreePlan)
            {
                return;
            }

            if (request->_operation == SpiOperation.Datum)
            {
                NativeDatumReference reference = MemoryMarshal.Read<NativeDatumReference>(request->_parameters[0]._value.ReadBytes());
                result->_text.Integral = (long)reference._bits;
                result->_rowsAffected = request->_parameters[0]._typeOid;
                if (request->_scalarOperation == 2)
                {
                    Copies++;
                    CopyDestination = request->_resultContext;
                }

                return;
            }

            Executions++;
            result->_release = &Release;
            if (FailExecution)
            {
                throw new PgException("P8501", "native acquisition failed", detail: "owned detail", hint: "owned hint");
            }

            if (request->_operation == SpiOperation.FunctionCall)
            {
                CallMode = request->_scalarOperation;
                ExpectedOid = request->_scalarResultOid;
                FunctionOid = request->_functionOid;
                ResultContext = request->_resultContext;
                Defaults = new ReadOnlySpan<byte>(request->_argumentDefaults, request->_parameterCount).ToArray();
                result->_resultTypeOid = ResultOid;
                result->_rowsAffected = 23;
                result->_text = new NativeValue { Integral = 42, IsNull = IsNull ? (byte)1 : (byte)0 };
                return;
            }

            LastMode = request->_resultMode;
            LastLimit = request->_limit;
            int columns = Math.Min(Cells.Length, (int)request->_resultMode - 1);
            result->_columnCount = columns;
            result->_rowCount = Rows;
            result->_rowsAffected = Rows;
            result->_columns = (NativeSpiColumn*)NativeMemory.AllocZeroed((nuint)Math.Max(1, columns), (nuint)sizeof(NativeSpiColumn));
            result->_values = (NativeValue*)NativeMemory.AllocZeroed((nuint)Math.Max(1, columns * Rows), (nuint)sizeof(NativeValue));
            for (int column = 0; column < columns; column++)
            {
                Cell cell = Cells[column];
                result->_columns[column] = new NativeSpiColumn
                {
                    _typeOid = cell.Oid,
                    _baseTypeOid = cell.Oid,
                    _name = NativeValue.FromString("value"),
                };
                if (Rows != 0)
                {
                    result->_values[column] = new NativeValue { Integral = cell.Bits, IsNull = cell.IsNull ? (byte)1 : (byte)0 };
                }
            }
        }

        /// <summary>
        /// Releases transport buffers independently of the PostgreSQL result owner's deletion.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Release(NativeSpiResult* result)
        {
            Current.ResultReleases++;
            for (int column = 0; column < result->_columnCount; column++)
            {
                result->_columns[column]._name.Release();
            }

            NativeMemory.Free(result->_columns);
            NativeMemory.Free(result->_values);
            result->_text.Release();
        }
    }

    /// <summary>
    /// Describes an independently supplied native result cell.
    /// </summary>
    private readonly record struct Cell(uint Oid, long Bits, bool IsNull = false);

    /// <summary>
    /// Represents the primary reader-only scalar.
    /// </summary>
    private readonly record struct Number(int Value);

    /// <summary>
    /// Represents a different CLR reader sharing the same SQL identity.
    /// </summary>
    private readonly record struct Alias(int Value);

    /// <summary>
    /// Requires NULL and empty results to bypass converter creation.
    /// </summary>
    private readonly record struct Absent;

    /// <summary>
    /// Preserves reference-type absence rules.
    /// </summary>
    private sealed record AbsentReference;

    /// <summary>
    /// Isolates unsupported read capability.
    /// </summary>
    private readonly record struct WriteOnly;

    /// <summary>
    /// Isolates the cached failing factory.
    /// </summary>
    private readonly record struct FactoryFailure;
}
