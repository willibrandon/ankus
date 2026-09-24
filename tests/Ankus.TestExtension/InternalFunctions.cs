namespace Ankus.TestExtension;

/// <summary>
/// Exercises native internal pointers and managed state through ordinary functions and aggregate callbacks.
/// </summary>
[PgSchema("internal_values")]
public static class InternalFunctions
{
    private static int s_created;
    private static int s_disposed;
    private static int s_invalidated;
    private static int s_combines;
    private static int s_deserializes;

    /// <summary>
    /// Clears observations before an independent backend test.
    /// </summary>
    [PgFunction]
    public static void InternalReset() => (s_created, s_disposed, s_invalidated, s_combines, s_deserializes) = (0, 0, 0, 0, 0);

    /// <summary>
    /// Returns allocation, disposal, invalidation, combine, and deserialization counts.
    /// </summary>
    [PgFunction]
    public static int[] InternalCounts() => [s_created, s_disposed, s_invalidated, s_combines, s_deserializes];

    /// <summary>
    /// Reads and updates a caller-owned native Int64 without confusing SQL NULL with a zero pointer.
    /// </summary>
    [PgFunction]
    public static long InternalNativeRead(PgInternal? state)
    {
        if (state is null)
        {
            return -1;
        }

        PgNativeReference<long>? pointer = state.DangerousBorrow<long>();
        if (pointer is null)
        {
            return 0;
        }

        pointer.Value++;
        return pointer.Value;
    }

    /// <summary>
    /// Exposes a deliberately incompatible managed read for guarded error testing.
    /// </summary>
    [PgFunction]
    public static long InternalWrongRead(PgInternal? state) => state?.Get<long>() ?? -1;

    /// <summary>
    /// Implements an ordinary internal-returning function used as a manually declared SQL transition.
    /// </summary>
    [PgFunction]
    public static PgInternal InternalStep(PgFunctionContext context, PgInternal? state, int? value)
    {
        state ??= CreateCounter(context.StateMemoryContext);
        if (value is { } present)
        {
            state.Get<Counter>().Total += present;
        }

        return state;
    }

    /// <summary>
    /// Reads managed state passed by PostgreSQL to an ordinary generated function.
    /// </summary>
    [PgFunction]
    public static long? InternalFinal(PgInternal? state) => state?.Get<Counter>().Total;

    /// <summary>
    /// Retains separate managed states across set advances, repeated values, and SQL NULL.
    /// </summary>
    [PgFunction]
    public static IEnumerable<PgInternal?> InternalStates(PgInternal? state)
    {
        PgInternal first = state ?? CreateCounter();
        first.Get<Counter>().Total = 40;
        yield return first;
        PgInternal second = CreateCounter();
        second.Get<Counter>().Total = 42;
        yield return second;
        yield return first;
        yield return null;
    }

    /// <summary>
    /// Carries internal state in a named TABLE column with independent row metadata.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(PgInternal? State, int Position)> InternalRows(PgInternal? seed)
    {
        int position = 0;
        foreach (PgInternal? value in InternalStates(seed))
        {
            yield return (value, ++position);
        }
    }

    /// <summary>
    /// Exercises explicit reset and the original owner recovered by an alias from another context.
    /// </summary>
    [PgFunction]
    public static bool InternalResetOwner()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("internal state owner");
        PgInternal state = CreateCounter(owner);
        PgInternal alias = Spi.ExecuteScalar<PgInternal>("SELECT $1", SpiParameter.Create(state));
        if (!ReferenceEquals(state.Get<Counter>(), alias.Get<Counter>()))
        {
            throw new InvalidOperationException("Internal aliases did not recover managed state.");
        }

        owner.Reset();
        try
        {
            _ = alias.Get<Counter>();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Rejects a consumed payload after its disposal interrupts native context cleanup.
    /// </summary>
    [PgFunction]
    public static string InternalThrowingCleanup()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("throwing internal cleanup");
        PgInternal state = CreateCounter(owner);
        state.Get<Counter>().Failure = -5;
        SpiParameter parameter = SpiParameter.Create(state);
        string sqlState;
        try
        {
            owner.Reset();
            return "cleanup did not throw";
        }
        catch (PgException error)
        {
            sqlState = error.SqlState;
        }

        bool contextAlive = owner.IsAlive;
        try
        {
            _ = state.DangerousGetBits();
            return "released identity remained accessible";
        }
        catch (ObjectDisposedException)
        {
            // The payload expires before its context finishes resetting.
        }

        try
        {
            _ = Spi.ExecuteScalar<PgInternal>("SELECT $1", parameter);
            return "released parameter remained accessible";
        }
        catch (ObjectDisposedException)
        {
            // A parameter created before cleanup still validates at execution.
        }

        owner.Reset();
        return $"{sqlState}|{contextAlive}|{Spi.ExecuteScalar<int>("SELECT 42")}";
    }

    /// <summary>
    /// Allocates a counter and retains its own wrapper for cleanup-order checks.
    /// </summary>
    private static PgInternal CreateCounter(PgMemoryContext? owner = null)
    {
        var counter = new Counter();
        PgInternal result = PgInternal.Create(counter, owner);
        counter.State = result;
        return result;
    }

    /// <summary>
    /// Uses general internal state in serial, moving, and real parallel aggregation.
    /// </summary>
    [PgAggregate(Name = "state_total", ParallelSafety = PgParallelSafety.Safe)]
    public static class Total
    {
        /// <summary>
        /// Creates aggregate-owned managed state and records input or a controlled failure mode.
        /// </summary>
        public static PgInternal Transition(PgInternal? state, int? value)
        {
            state ??= CreateCounter();
            Counter counter = state.Get<Counter>();
            if (value is < 0)
            {
                counter.Failure = value.Value;
            }
            else
            {
                counter.Total += value ?? 0;
            }

            if (counter.Failure == -1)
            {
                throw new PgException("P7921", "internal transition failed");
            }

            return state;
        }

        /// <summary>
        /// Reads the result without consuming managed state.
        /// </summary>
        public static long? Final(PgInternal? state) => state?.Get<Counter>().Total;

        /// <summary>
        /// Creates moving state under PostgreSQL's window owner.
        /// </summary>
        public static PgInternal MovingTransition(PgInternal? state, int? value) => Transition(state, value);

        /// <summary>
        /// Removes a departing value while retaining the same owned state.
        /// </summary>
        public static PgInternal MovingInverse(PgInternal? state, int? value)
        {
            state!.Get<Counter>().Total -= value ?? 0;
            return state;
        }

        /// <summary>
        /// Reads the bounded frame without changing the state.
        /// </summary>
        public static long? MovingFinal(PgInternal? state) => Final(state);

        /// <summary>
        /// Copies temporary worker state into the destination aggregate owner.
        /// </summary>
        public static PgInternal? Combine(PgInternal? state, PgInternal? other)
        {
            s_combines++;
            if (other is null)
            {
                return state;
            }

            Counter partial = other.Get<Counter>();
            if (partial.Failure == -4)
            {
                return other;
            }

            state ??= CreateCounter();
            state.Get<Counter>().Total += partial.Total;
            return state;
        }

        /// <summary>
        /// Serializes values without transporting managed or native pointer identities.
        /// </summary>
        public static byte[] Serialize(PgInternal state)
        {
            Counter counter = state.Get<Counter>();
            if (counter.Failure == -2)
            {
                throw new PgException("P7922", "internal serialization failed");
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(counter.Total);
            writer.Write(counter.Failure);
            return stream.ToArray();
        }

        /// <summary>
        /// Recreates state under the native deserializer's temporary owner.
        /// </summary>
        public static PgInternal Deserialize(byte[] bytes)
        {
            s_deserializes++;
            PgInternal state = CreateCounter();
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            Counter counter = state.Get<Counter>();
            counter.Total = reader.ReadInt64();
            counter.Failure = reader.ReadInt32();
            if (counter.Failure == -3)
            {
                throw new PgException("P7923", "internal deserialization failed");
            }

            return state;
        }
    }

    /// <summary>
    /// Holds one managed state and proves invalidation precedes user disposal.
    /// </summary>
    private sealed class Counter : IDisposable
    {
        /// <summary>
        /// Records a new payload.
        /// </summary>
        internal Counter() => s_created++;

        /// <summary>
        /// Gets or sets this payload's internal wrapper.
        /// </summary>
        internal PgInternal? State { get; set; }

        /// <summary>
        /// Gets or sets the accumulated total.
        /// </summary>
        internal long Total { get; set; }

        /// <summary>
        /// Gets or sets the controlled callback failure mode.
        /// </summary>
        internal int Failure { get; set; }

        /// <inheritdoc />
        public void Dispose()
        {
            s_disposed++;
            try
            {
                _ = State!.Get<Counter>();
            }
            catch (ObjectDisposedException)
            {
                s_invalidated++;
                if (Failure == -5)
                {
                    throw new PgException("P7924", "internal cleanup failed");
                }

                return;
            }

            throw new InvalidOperationException("Internal state remained accessible during disposal.");
        }
    }
}
