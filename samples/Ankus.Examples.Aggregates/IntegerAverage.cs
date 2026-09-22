using System.Buffers.Binary;

namespace Ankus.Examples.Aggregates;

/// <summary>
/// Computes an integer average with an owned state, parallel aggregation, and inverse window transitions.
/// </summary>
[PgAggregate(Name = "integer_average", ParallelSafety = PgParallelSafety.Safe)]
public static class IntegerAverage
{
    /// <summary>
    /// Adds one nonnull integer, creating the state when needed.
    /// </summary>
    /// <param name="state">The current state, or null before the first transition.</param>
    /// <param name="value">The input integer, or null to leave the sum and count unchanged.</param>
    /// <returns>The owned state, including an empty state when the input is null.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgAggregateState<AverageState> Transition(PgAggregateState<AverageState>? state, int? value)
    {
        state ??= new(new AverageState());
        if (value is { } number)
        {
            state.Value.Sum = checked(state.Value.Sum + number);
            state.Value.Count = checked(state.Value.Count + 1);
        }

        return state;
    }

    /// <summary>
    /// Returns the average, or SQL NULL for an empty or all-null input.
    /// </summary>
    /// <param name="state">The current state, which remains valid for later window transitions.</param>
    /// <returns>The average as a double, or null when no integer was accumulated.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static double? Final(PgAggregateState<AverageState>? state)
        => state is null || state.Value.Count == 0 ? null : (double)state.Value.Sum / state.Value.Count;

    /// <summary>
    /// Merges partial values into the destination aggregate's state.
    /// </summary>
    /// <param name="state">The destination state, or null before its first partial value.</param>
    /// <param name="other">A partial state which can belong to a temporary deserialization context.</param>
    /// <returns>The destination state; borrowed partial state ownership is never returned.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgAggregateState<AverageState> Combine(PgAggregateState<AverageState>? state, PgAggregateState<AverageState>? other)
    {
        state ??= new(new AverageState());
        if (other is not null)
        {
            state.Value.Sum = checked(state.Value.Sum + other.Value.Sum);
            state.Value.Count = checked(state.Value.Count + other.Value.Count);
        }

        return state;
    }

    /// <summary>
    /// Encodes a versioned, process-independent partial state for a parallel worker.
    /// </summary>
    /// <param name="state">The present partial state.</param>
    /// <returns>A format version followed by the little-endian sum and count.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static byte[] Serialize(PgAggregateState<AverageState> state)
    {
        byte[] bytes = new byte[17];
        bytes[0] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1), state.Value.Sum);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(9), state.Value.Count);
        return bytes;
    }

    /// <summary>
    /// Creates a temporary partial state from a worker's versioned bytes.
    /// </summary>
    /// <param name="bytes">The serialized partial state.</param>
    /// <returns>A new state whose values can be copied into a combine destination.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgAggregateState<AverageState> Deserialize(byte[] bytes)
    {
        if (bytes.Length != 17 || bytes[0] != 1)
        {
            throw new PgException("22000", "Invalid integer_average state format.");
        }

        long count = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(9));
        if (count < 0)
        {
            throw new PgException("22000", "Invalid integer_average state count.");
        }

        return new(new AverageState { Sum = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(1)), Count = count });
    }

    /// <summary>
    /// Adds an input to a moving frame using the same exact state representation.
    /// </summary>
    /// <param name="state">The current moving state.</param>
    /// <param name="value">The newly included integer.</param>
    /// <returns>The present moving state.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgAggregateState<AverageState> MovingTransition(PgAggregateState<AverageState>? state, int? value)
        => Transition(state, value);

    /// <summary>
    /// Removes the integer leaving the moving frame.
    /// </summary>
    /// <param name="state">The current moving state.</param>
    /// <param name="value">The departing integer, or null for an unchanged state.</param>
    /// <returns>The remaining moving state.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static PgAggregateState<AverageState> MovingInverse(PgAggregateState<AverageState>? state, int? value)
    {
        if (state is null)
        {
            throw new InvalidOperationException("An inverse transition requires an existing state.");
        }

        if (value is { } number)
        {
            state.Value.Sum = checked(state.Value.Sum - number);
            state.Value.Count = checked(state.Value.Count - 1);
        }

        return state;
    }

    /// <summary>
    /// Reads a moving frame without consuming or modifying its state.
    /// </summary>
    /// <param name="state">The current moving state.</param>
    /// <returns>The frame average, or null for an empty or all-null frame.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static double? MovingFinal(PgAggregateState<AverageState>? state) => Final(state);
}
