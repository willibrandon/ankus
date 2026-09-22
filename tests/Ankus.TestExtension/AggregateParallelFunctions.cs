using System.Text;

namespace Ankus.TestExtension;

/// <summary>
/// Makes real parallel aggregation, byte serialization and temporary-state ownership observable.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class AggregateParallelFunctions
{
    private static string s_mode = "normal";
    private static int s_created;
    private static int s_disposed;
    private static int s_live;
    private static int s_duplicate;

    /// <summary>
    /// Selects the combine ownership probe and resets leader-local payload lifecycle counts.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static void ParallelReset(string mode)
    {
        s_mode = mode;
        s_created = 0;
        s_disposed = 0;
        s_live = 0;
        s_duplicate = 0;
    }

    /// <summary>
    /// Reports created, disposed, live, and duplicate disposal counts in this backend.
    /// </summary>
    [PgFunction(Requires = ["aggregate-support"])]
    public static int[] ParallelStatus() => [s_created, s_disposed, s_live, s_duplicate];

    /// <summary>
    /// Uses process-independent serialization and copies borrowed partial state into the final aggregate's owner.
    /// </summary>
    [PgAggregate(Name = "parallel_sum", ParallelSafety = PgParallelSafety.Safe, StateSize = 256, Requires = ["aggregate-support"])]
    public static class ParallelSum
    {
        /// <summary>
        /// Retains the actual backend PID in each partial state so worker execution is independently observable.
        /// </summary>
        public static PgAggregateState<ParallelState> Transition(PgAggregateState<ParallelState>? state, int? value)
        {
            state ??= new PgAggregateState<ParallelState>(new ParallelState());
            ParallelState data = state.Value;
            if (value.HasValue)
            {
                data.Sum += value.Value;
                data.Count++;
                if (value.Value < 0)
                {
                    data.Failure = Math.Max(data.Failure, -value.Value);
                }
            }

            return state;
        }

        /// <summary>
        /// Preserves the right state and copies it when a new owner must be established.
        /// </summary>
        public static PgAggregateState<ParallelState>? Combine(PgAggregateState<ParallelState>? state, PgAggregateState<ParallelState>? other)
        {
            if (other is null)
            {
                return state;
            }

            ParallelState right = other.Value;
            if (right.Failure == 3)
            {
                throw new PgException("P7813", "aggregate combine failed");
            }

            if (s_mode == "borrow" && state is null)
            {
                return other;
            }

            state ??= new PgAggregateState<ParallelState>(new ParallelState());
            ParallelState left = state.Value;
            left.Sum += right.Sum;
            left.Count += right.Count;
            left.Combines += right.Combines + 1;
            left.Serialized += right.Serialized;
            left.Deserialized += right.Deserialized;
            left.Failure = Math.Max(left.Failure, right.Failure);
            left.Processes.UnionWith(right.Processes);
            return state;
        }

        /// <summary>
        /// Serializes values and process identities, never managed handles or native addresses.
        /// </summary>
        public static byte[]? Serialize(PgAggregateState<ParallelState> state)
        {
            ParallelState value = state.Value;
            if (value.Failure == 1)
            {
                throw new PgException("P7811", "aggregate serialization failed");
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(0x41474b31);
            writer.Write(value.Sum);
            writer.Write(value.Count);
            writer.Write(value.Combines);
            writer.Write(value.Serialized + 1);
            writer.Write(value.Deserialized);
            writer.Write(value.Failure);
            writer.Write(value.Processes.Count);
            foreach (int process in value.Processes.Order())
            {
                writer.Write(process);
            }

            writer.Flush();
            byte[] bytes = stream.ToArray();
            return value.Failure switch
            {
                4 => [],
                5 => bytes[..3],
                6 => new byte[bytes.Length],
                7 => null,
                _ => bytes
            };
        }

        /// <summary>
        /// Creates temporary owned state from bytea while PostgreSQL's internal dummy stays outside managed conversion.
        /// </summary>
        public static PgAggregateState<ParallelState> Deserialize(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadInt32() != 0x41474b31)
            {
                throw new PgException("22P03", "invalid aggregate state format");
            }

            long sum = reader.ReadInt64();
            long count = reader.ReadInt64();
            long combines = reader.ReadInt64();
            long serialized = reader.ReadInt64();
            long deserialized = reader.ReadInt64();
            int failure = reader.ReadInt32();
            if (failure == 2)
            {
                throw new PgException("P7812", "aggregate deserialization failed");
            }

            int processes = reader.ReadInt32();
            var identities = new HashSet<int>();
            for (int index = 0; index < processes; index++)
            {
                identities.Add(reader.ReadInt32());
            }

            if (stream.Position != stream.Length)
            {
                throw new PgException("22P03", "aggregate state has trailing bytes");
            }

            var value = new ParallelState
            {
                Sum = sum,
                Count = count,
                Combines = combines,
                Serialized = serialized,
                Deserialized = deserialized + 1,
                Failure = failure
            };
            value.Processes.Clear();
            value.Processes.UnionWith(identities);
            return new PgAggregateState<ParallelState>(value);
        }

        /// <summary>
        /// Returns exact sum/count and callback evidence followed by the source backend PIDs.
        /// </summary>
        public static long[] Final(PgAggregateState<ParallelState>? state)
        {
            if (state is null)
            {
                return [0, 0, 0, 0, 0];
            }

            ParallelState value = state.Value;
            return [value.Sum, value.Count, value.Combines, value.Serialized, value.Deserialized, .. value.Processes.Order().Select(static process => (long)process)];
        }
    }

    /// <summary>
    /// Carries an ordinary bigint-array state through partial aggregation with an observable nonidentity seed.
    /// </summary>
    [PgAggregate(Name = "parallel_seeded_array", InitialCondition = "{10,1}", ParallelSafety = PgParallelSafety.Safe,
        Requires = ["aggregate-support"])]
    public static class ParallelSeededArray
    {
        /// <summary>
        /// Adds input values while retaining the number of initial states represented by the state.
        /// </summary>
        public static long[] Transition(long[] state, int? value) => [state[0] + (value ?? 0), state[1]];

        /// <summary>
        /// Combines two ordinary SQL array datums, each including its independently parsed initial condition.
        /// </summary>
        public static long[] Combine(long[] state, long[] other) => [state[0] + other[0], state[1] + other[1]];
    }

    /// <summary>
    /// A payload copied across worker processes and invalidated when its individual native owner resets.
    /// </summary>
    public sealed class ParallelState : IDisposable
    {
        private bool _disposed;
        private readonly int _identity = Created();

        /// <summary>
        /// Gets or sets the exact signed sum.
        /// </summary>
        public long Sum { get; set; }

        /// <summary>
        /// Gets or sets the number of nonnull values.
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Gets or sets the number of combine callbacks represented by this payload.
        /// </summary>
        public long Combines { get; set; }

        /// <summary>
        /// Gets or sets the number of serialized partial states represented by this payload.
        /// </summary>
        public long Serialized { get; set; }

        /// <summary>
        /// Gets or sets the number of deserialized partial states represented by this payload.
        /// </summary>
        public long Deserialized { get; set; }

        /// <summary>
        /// Gets or sets a controlled failure selected by a negative input sentinel.
        /// </summary>
        public int Failure { get; set; }

        /// <summary>
        /// Gets the actual backends that processed the payload's input rows.
        /// </summary>
        public HashSet<int> Processes { get; } = [Environment.ProcessId];

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                s_duplicate++;
                return;
            }

            _disposed = true;
            s_disposed++;
            s_live--;
            GC.KeepAlive(_identity);
        }
    }

    /// <summary>
    /// Registers allocation before a payload is adopted by its native owner.
    /// </summary>
    private static int Created()
    {
        s_live++;
        return ++s_created;
    }
}
