using System.Collections;
using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises set-returning values, table metadata, lazy enumeration and backend cleanup.
/// </summary>
[PgSchema("set_values")]
public static class SetFunctions
{
    private static int s_factory;
    private static int s_sequenceConstruction;
    private static int s_getEnumerator;
    private static int s_enumeratorConstruction;
    private static int s_moveNext;
    private static int s_current;
    private static int s_dispose;
    private static int s_finally;
    private static int s_cleanupSpi;
    private static int s_cleanupDenied;
    private static int s_live;
    private static int s_duplicateDispose;
    private static int s_iteratorFinally;
    private static int s_floodRows;
    private static long s_maxRowBytes;

    /// <summary>
    /// Resets backend-local lifecycle counters between independent calls.
    /// </summary>
    [PgFunction]
    public static void SetReset()
    {
        s_factory = 0;
        s_sequenceConstruction = 0;
        s_getEnumerator = 0;
        s_enumeratorConstruction = 0;
        s_moveNext = 0;
        s_current = 0;
        s_dispose = 0;
        s_finally = 0;
        s_cleanupSpi = 0;
        s_cleanupDenied = 0;
        s_live = 0;
        s_duplicateDispose = 0;
        s_iteratorFinally = 0;
        s_floodRows = 0;
        s_maxRowBytes = 0;
    }

    /// <summary>
    /// Reports factory, sequence construction, GetEnumerator, enumerator construction, MoveNext, Current,
    /// Dispose, finally, cleanup SPI, denied SPI, live enumerators, duplicate disposal and iterator finally counts.
    /// </summary>
    [PgFunction]
    public static int[] SetStatus() =>
    [
        s_factory, s_sequenceConstruction, s_getEnumerator, s_enumeratorConstruction, s_moveNext, s_current,
        s_dispose, s_finally, s_cleanupSpi, s_cleanupDenied, s_live, s_duplicateDispose, s_iteratorFinally,
    ];

    /// <summary>
    /// Returns a counted sequence using PostgreSQL's available execution mode.
    /// </summary>
    [PgFunction(Rows = 17, Cost = 2.5)]
    public static IEnumerable<int> SetProbe(int count, int failure, bool cleanupSql) => CreateProbe(count, failure, cleanupSql);

    /// <summary>
    /// Requires lazy per-call enumeration, including ProjectSet early termination.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<int> SetProbeStreaming(int count, int failure, bool cleanupSql) => CreateProbe(count, failure, cleanupSql);

    /// <summary>
    /// Requires complete materialization before handing rows back to the executor.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<int> SetProbeMaterialized(int count, int failure, bool cleanupSql) => CreateProbe(count, failure, cleanupSql);

    /// <summary>
    /// Produces an ordinary compiler-generated iterator with observable finally execution.
    /// </summary>
    [PgFunction]
    public static IEnumerable<int> SetSeries(int start, int count)
    {
        try
        {
            for (int offset = 0; offset < count; offset++)
            {
                yield return checked(start + offset);
            }
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Exposes immutable, parallel-safe set metadata without lifecycle counter side effects.
    /// </summary>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, Rows = 23.5)]
    public static IEnumerable<int> SetPureSeries(int start, int count) => Enumerable.Range(start, count);

    /// <summary>
    /// Distinguishes absent sequences from nullable values within present sequences.
    /// </summary>
    [PgFunction]
    public static IEnumerable<int?>? SetNullable(bool absent) => absent ? null : [null, 7, null, 19];

    /// <summary>
    /// Supplies one named TABLE column without wrapping scalar values in ValueTuple.
    /// </summary>
    [PgFunction]
    [return: PgColumnNames("custom_value")]
    public static IEnumerable<int?> SetOneColumn() => [7, null, 19];

    /// <summary>
    /// Overrides tuple element names in the SQL table contract.
    /// </summary>
    [PgFunction]
    [return: PgColumnNames("custom_id", "custom_text")]
    public static IEnumerable<(int OriginalId, string? OriginalText)> SetNamed() => [(1, "café"), (2, null)];

    /// <summary>
    /// Returns more than seven columns, including nullable and owned datum types.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(int FirstNumber, string? SecondText, long ThirdNumber, Guid Identifier,
        EnumMood? Mood, PgArray<int?>? Numbers, PgNumeric ExactValue, bool Flag, string NinthText)> SetWide()
    {
        yield return (1, "café", 5000000000L, Guid.Parse("c7c3e551-bd58-4dc6-b1cd-065b72302136"),
            EnumMood.Cafe, new PgArray<int?>([4, null, 6, 7], [2, 2], [-1, 5]), PgNumeric.Parse("1234.500"), true, "ninth");
        yield return (2, null, -5000000000L, Guid.Empty, null, null, PgNumeric.Zero, false, "last");
    }

    /// <summary>
    /// Retains owned arguments while intervening SPI calls replace the source memory contexts.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(int RowIndex, string? Payload, EnumMood? Mood, PgArray<EnumMood?>? Values)> SetRetained(
        string? inputPayload, EnumMood? inputMood, PgArray<EnumMood?>? inputValues, int count)
    {
        for (int index = 0; index < count; index++)
        {
            Spi.Execute("SELECT repeat('replacement', 20000)");
            yield return (index, inputPayload, inputMood, inputValues);
        }
    }

    /// <summary>
    /// Retains a prepared SPI statement between callbacks while enum OID lookup remains backend-scoped.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(int RowIndex, EnumMood Value)> SetSpi(int count)
    {
        using SpiPreparedStatement plan = Spi.Prepare("SELECT $1", typeof(EnumMood));
        try
        {
            for (int index = 0; index < count; index++)
            {
                EnumMood value = index % 2 == 0 ? EnumMood.Cafe : EnumMood.Medium;
                yield return (index, plan.ExecuteScalar<EnumMood>(SpiParameter.Create(value)));
            }
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Produces enough materialized text to exercise PostgreSQL's work_mem-limited tuplestore.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<(int RowId, string Payload)> SetMaterialized(int count, int width)
    {
        string payload = new('x', width);
        for (int index = 1; index <= count; index++)
        {
            yield return (index, payload);
        }
    }

    /// <summary>
    /// Exposes managed label validation and native enum output lookup errors after an iterator produces a value.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<EnumMood> SetEnumStreaming(bool invalidValue) => EnumProbe(invalidValue);

    /// <summary>
    /// Exposes the same enum output boundaries while accumulating a tuple store.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<EnumMood> SetEnumMaterialized(bool invalidValue) => EnumProbe(invalidValue);

    /// <summary>
    /// Resumes with owned values from the temporal, range, geometry, floating-point, binary, network and JSON families.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(PgDate? DateValue, PgTimestampTz? Instant, PgInterval? IntervalValue,
        PgRange<int>? RangeValue, PgPoint? PointValue, double? FloatingValue, byte[]? Bytes, PgInet? Address, PgJsonb? Json)> SetDatums(
        PgDate? inputDate, PgTimestampTz? inputInstant, PgInterval? inputInterval,
        PgRange<int>? inputRange, PgPoint? inputPoint, double? inputFloating, byte[]? inputBytes, PgInet? inputAddress, PgJsonb? inputJson)
    {
        for (int index = 0; index < 2; index++)
        {
            Spi.Execute("SELECT repeat('overwrite', 10000)");
            yield return (inputDate, inputInstant, inputInterval, inputRange, inputPoint, inputFloating, inputBytes, inputAddress, inputJson);
        }
    }

    /// <summary>
    /// Recovers from nested set failures inside one managed SPI scope without retaining row contexts or handles.
    /// </summary>
    [PgFunction]
    public static string SetRecovery() => Spi.Connect(session =>
    {
        SetReset();
        session.Execute("CREATE TEMP TABLE set_recovery_writes(value int); INSERT INTO set_recovery_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM set_recovery_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus set row', 'SRF multi-call context', 'Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int failures = 0;
        for (int index = 0; index < 20; index++)
        {
            try
            {
                session.Execute("""
                    WITH written AS (INSERT INTO set_recovery_writes VALUES (99) RETURNING value)
                    SELECT result FROM written CROSS JOIN LATERAL set_values.set_probe_streaming(value,5,false) result
                    """);
            }
            catch (PgException error) when (error.SqlState == "P7105")
            {
                failures++;
            }

            if (session.ExecuteScalar<int>("SELECT sum(value)::int FROM set_values.set_series(1,3) value") != 6)
            {
                throw new InvalidOperationException("A set failed after guarded SPI recovery.");
            }
        }

        session.Execute("INSERT INTO set_recovery_writes VALUES (2)");
        return $"{failures}:{s_dispose}:{s_live}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });

    /// <summary>
    /// Suspends with an owned kept SPI plan and optional cursor before native enum output lookup can fail.
    /// </summary>
    [PgFunction]
    public static IEnumerable<EnumMood> SetOwnedResources(bool openCursor)
    {
        using SpiPreparedStatement plan = Spi.Prepare("SELECT 1 /* Ankus set resource probe */");
        try
        {
            if (openCursor)
            {
                using SpiCursor cursor = plan.OpenCursor();
                yield return cursor.Fetch(1)[0].Get<int>(0) == 1 ? EnumMood.Low : EnumMood.Medium;
            }
            else
            {
                yield return plan.ExecuteScalar<int>() == 1 ? EnumMood.Low : EnumMood.Medium;
            }
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Takes ownership of a cursor created in a surviving parent transaction before native row conversion fails.
    /// </summary>
    [PgFunction]
    public static IEnumerable<EnumMood> SetExistingCursor(string name)
    {
        using SpiCursor cursor = Spi.FindCursor(name);
        try
        {
            yield return cursor.Fetch(1)[0].Get<int>(0) == 1 ? EnumMood.Low : EnumMood.Medium;
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Enumerates entirely in managed code so cancellation depends on native checks between materialized rows.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<int> SetManagedFlood()
    {
        try
        {
            for (int index = 0; index < int.MaxValue; index++)
            {
                s_floodRows++;
                yield return index;
            }
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Reports how many rows were advanced before the native executor canceled materialization.
    /// </summary>
    [PgFunction]
    public static int SetFloodRows() => s_floodRows;

    /// <summary>
    /// Applies a numeric return constraint independently to each nullable scalar set row.
    /// </summary>
    [PgFunction]
    [return: PgNumericPrecision(4, 2)]
    public static IEnumerable<PgNumeric?> SetPrecision(PgNumeric? first, PgNumeric? last)
    {
        try
        {
            yield return first;
            yield return null;
            yield return last;
        }
        finally
        {
            s_iteratorFinally++;
        }
    }

    /// <summary>
    /// Samples row-context allocation while materializing distinct large rows that exceed work_mem.
    /// </summary>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<(int RowId, string Payload)> SetMemoryBounded(int count, int width)
    {
        for (int index = 1; index <= count; index++)
        {
            long bytes = Spi.ExecuteScalar<long>("SELECT COALESCE(max(total_bytes),0)::bigint FROM pg_backend_memory_contexts WHERE name = 'Ankus set row'");
            s_maxRowBytes = Math.Max(s_maxRowBytes, bytes);
            string prefix = index.ToString(CultureInfo.InvariantCulture);
            yield return (index, prefix + new string('x', checked(width - prefix.Length)));
        }
    }

    /// <summary>
    /// Reports the maximum row-context allocation observed between materialized rows.
    /// </summary>
    [PgFunction]
    public static long SetMaximumRowBytes() => s_maxRowBytes;

    /// <summary>
    /// Holds a counted iterator across conversion errors instead of ending it before conversion begins.
    /// </summary>
    private static IEnumerable<EnumMood> EnumProbe(bool invalidValue)
    {
        foreach (int value in CreateProbe(3, 0, cleanupSql: true))
        {
            yield return invalidValue && value == 2 ? (EnumMood)12345 : EnumMood.Low;
        }
    }

    /// <summary>
    /// Fails before returning the enumerable when the factory stage is selected.
    /// </summary>
    private static ProbeEnumerable CreateProbe(int count, int failure, bool cleanupSql)
    {
        s_factory++;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (failure == 1)
        {
            throw new PgException("P7101", "set factory failure");
        }

        return new ProbeEnumerable(count, failure, cleanupSql);
    }

    /// <summary>
    /// Separates enumerable construction from enumerator acquisition and advancement.
    /// </summary>
    /// <param name="count">The number of produced values.</param>
    /// <param name="failure">The selected failing stage, or zero.</param>
    /// <param name="cleanupSql">Whether disposal attempts a guarded backend call.</param>
    private sealed class ProbeEnumerable(int count, int failure, bool cleanupSql) : IEnumerable<int>
    {
        private readonly int _count = Construct(count, failure);

        /// <inheritdoc />
        public IEnumerator<int> GetEnumerator()
        {
            s_getEnumerator++;
            if (failure == 3)
            {
                throw new PgException("P7103", "set GetEnumerator failure");
            }

            return new ProbeEnumerator(_count, failure, cleanupSql);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Counts enumerable construction before optionally failing its initializer.
        /// </summary>
        private static int Construct(int count, int failure)
        {
            s_sequenceConstruction++;
            return failure == 2 ? throw new PgException("P7102", "set enumerable construction failure") : count;
        }
    }

    /// <summary>
    /// Makes every iterator lifecycle stage observable without depending on compiler-generated state layout.
    /// </summary>
    /// <param name="count">The number of produced values.</param>
    /// <param name="failure">The selected failing stage, or zero.</param>
    /// <param name="cleanupSql">Whether disposal attempts a guarded backend call.</param>
    private sealed class ProbeEnumerator(int count, int failure, bool cleanupSql) : IEnumerator<int>
    {
        private readonly int _count = Construct(count, failure);
        private int _current;
        private bool _disposed;

        /// <inheritdoc />
        public int Current
        {
            get
            {
                s_current++;
                return failure == 6 ? throw new PgException("P7106", "set Current failure") : _current;
            }
        }

        /// <inheritdoc />
        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            s_moveNext++;
            if (failure is 5 or 8 && _current == 1)
            {
                throw new PgException("P7105", "set MoveNext failure");
            }

            if (_current == _count)
            {
                return false;
            }

            _current++;
            return true;
        }

        /// <inheritdoc />
        public void Reset() => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                s_duplicateDispose++;
                return;
            }

            _disposed = true;
            s_dispose++;
            try
            {
                if (cleanupSql)
                {
                    try
                    {
                        Spi.Execute("INSERT INTO set_cleanup VALUES (1)");
                        s_cleanupSpi++;
                    }
                    catch (InvalidOperationException)
                    {
                        s_cleanupDenied++;
                    }
                }

                if (failure is 7 or 8)
                {
                    throw new PgException("P7107", "set Dispose failure");
                }
            }
            finally
            {
                s_finally++;
                s_live--;
            }
        }

        /// <summary>
        /// Counts successful enumerator ownership only after construction succeeds.
        /// </summary>
        private static int Construct(int count, int failure)
        {
            s_enumeratorConstruction++;
            if (failure == 4)
            {
                throw new PgException("P7104", "set enumerator construction failure");
            }

            s_live++;
            return count;
        }
    }
}
