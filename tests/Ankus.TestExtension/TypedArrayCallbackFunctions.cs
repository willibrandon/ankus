namespace Ankus.TestExtension;

public static partial class BorrowedArrayFunctions
{
    private static PgArrayView<PgTextView?>? s_generatedArray;
    private static PgTextView? s_generatedCell;
    private static IEnumerator<PgTextView?>? s_generatedCursor;

    /// <summary>
    /// Transfers the checked original integer array before the input callback expires.
    /// </summary>
    [PgFunction]
    public static PgArrayView<int?>? TypedArrayIdentity(PgArrayView<int?>? value) => value;

    /// <summary>
    /// Transfers native text arrays without copying or converting their individual cells.
    /// </summary>
    [PgFunction]
    public static PgArrayView<PgTextView?>? TypedTextArrayIdentity(PgArrayView<PgTextView?>? value) => value;

    /// <summary>
    /// Transfers native bytea arrays including nullable and empty binary cells.
    /// </summary>
    [PgFunction]
    public static PgArrayView<PgByteaView?>? TypedByteaArrayIdentity(PgArrayView<PgByteaView?>? value) => value;

    /// <summary>
    /// Reads through a one-way converter while returning the original unchanged native storage.
    /// </summary>
    [PgFunction]
    public static PgArrayView<ReadMappedInt?>? TypedReadOnlyArrayIdentity(PgArrayView<ReadMappedInt?>? value)
    {
        if (value is { Count: > 0 } && value[0]?.Value != 107)
        {
            throw new InvalidOperationException("The read-only element converter was not applied.");
        }

        return value;
    }

    /// <summary>
    /// Returns a statically named composite array through its declared SQL contract.
    /// </summary>
    [PgFunction(Requires = ["composite-types"])]
    [return: PgCompositeType("dog", Schema = "tuple_values")]
    public static PgArrayView<PgHeapTuple?>? TypedCompositeArrayIdentity(
        [PgCompositeType("dog", Schema = "tuple_values")] PgArrayView<PgHeapTuple?>? value) => value;

    /// <summary>
    /// Returns arrays of generated enum and native-layout types through closed element registrations.
    /// </summary>
    [PgFunction]
    public static PgArrayView<EnumMood?>? TypedEnumArrayIdentity(PgArrayView<EnumMood?>? value) => value;

    /// <summary>
    /// Returns a generated custom type array without reconstructing any native element.
    /// </summary>
    [PgFunction]
    public static PgArrayView<CustomTypeFunctions.Number?>? TypedCustomArrayIdentity(PgArrayView<CustomTypeFunctions.Number?>? value) => value;

    /// <summary>
    /// Requires a completely present integer array before managed execution begins.
    /// </summary>
    [PgFunction]
    public static int TypedArrayRequired(PgArrayView<int> value) => value.Count;

    /// <summary>
    /// Returns a separately selected native array to exercise exact declared-result identity checking.
    /// </summary>
    [PgFunction]
    public static PgArrayView<int?> TypedArrayReturnQuery(string sql) => Spi.ExecuteScalar<PgArrayView<int?>>(sql);

    /// <summary>
    /// Captures a typed array and borrowed text cells under a generated scalar callback lease.
    /// </summary>
    [PgFunction]
    public static string? TypedArrayCallbackSave(PgArrayView<PgTextView?> value, bool fail)
    {
        s_generatedArray = value;
        s_generatedCursor = value.GetEnumerator();
        _ = s_generatedCursor.MoveNext();
        s_generatedCell = s_generatedCursor.Current;
        if (fail)
        {
            throw new InvalidOperationException("Typed array callback failed.");
        }

        return s_generatedCell?.ToString();
    }

    /// <summary>
    /// Observes each escaped alias in a later callback without reviving its original source.
    /// </summary>
    [PgFunction]
    public static string[] TypedArrayCallbackExpired()
        => [Failure(() => _ = s_generatedArray!.Datum), Failure(() => s_generatedCell!.ToString()),
            Failure(() => s_generatedCursor!.MoveNext()), Failure(() => _ = s_generatedCursor!.Current)];

    /// <summary>
    /// Nested callback expiration leaves the enclosing array and already converted cell alive.
    /// </summary>
    [PgFunction]
    public static string?[] TypedArrayNested(PgArrayView<PgTextView?> value)
    {
        using IEnumerator<PgTextView?> cursor = value.GetEnumerator();
        _ = cursor.MoveNext();
        using PgTextView? cell = cursor.Current;
        string? before = cell?.ToString();
        string nested = Spi.ExecuteScalar<string>("SELECT borrowed_arrays.typed_array_callback_save(ARRAY['nested'],false)");
        return [before, nested, Failure(() => s_generatedCell!.ToString()), cell?.ToString(),
            ReferenceEquals(cell, cursor.Current) ? "same cell" : "different cell"];
    }

    /// <summary>
    /// Retains an independently copied typed array across multiple lazy set callbacks.
    /// </summary>
    [PgFunction]
    public static IEnumerable<PgArrayView<int?>?> TypedArrayRows(PgArrayView<int?>? value, bool fail)
    {
        using (value)
        {
            yield return value;
            if (fail)
            {
                throw new InvalidOperationException("Typed array iterator failed.");
            }

            yield return null;
            yield return value;
        }
    }

    /// <summary>
    /// Preserves an input snapshot and concrete array identity for each TABLE result column.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(PgArrayView<ReadMappedInt?>? Cell, int Position)> TypedArrayTable(PgArrayView<ReadMappedInt?>? value)
    {
        using (value)
        {
            yield return (value, value?[0]?.Value ?? -1);
            yield return (value, value?[2]?.Value ?? -1);
        }
    }

    /// <summary>
    /// Keeps the first present typed array alive until the aggregate returns its exact native value.
    /// </summary>
    [PgAggregate(Name = "typed_array_first")]
    public sealed class FirstTypedArray : IPgAggregate<PgAggregateState<PgArrayView<int?>>?, PgArrayView<int?>?>,
        IPgFinalizingAggregate<PgAggregateState<PgArrayView<int?>>?, ValueTuple, PgArrayView<int?>?>
    {
        /// <summary>
        /// Retains the first snapshot and releases later inputs without traversing their cells.
        /// </summary>
        public static PgAggregateState<PgArrayView<int?>>? Transition(PgAggregateContext context,
            PgAggregateState<PgArrayView<int?>>? state, PgArrayView<int?>? value)
        {
            if (state is null && value is not null)
            {
                return new(value);
            }

            value?.Dispose();
            return state;
        }

        /// <summary>
        /// Returns the stored native array after the callbacks that created it have ended.
        /// </summary>
        public static PgArrayView<int?>? Final(PgAggregateContext context, PgAggregateState<PgArrayView<int?>>? state, ValueTuple arguments) => state?.Value;
    }

    /// <summary>
    /// Uses a concrete SQL array as aggregate state, independently of managed internal state ownership.
    /// </summary>
    [PgAggregate(Name = "typed_array_sql_first")]
    public sealed class FirstTypedSqlArray : IPgAggregate<PgArrayView<int?>?, PgArrayView<int?>?>,
        IPgFinalizingAggregate<PgArrayView<int?>?, ValueTuple, PgArrayView<int?>?>
    {
        /// <summary>
        /// Transfers the first present array state and releases an unused copied input.
        /// </summary>
        public static PgArrayView<int?>? Transition(PgAggregateContext context, PgArrayView<int?>? state, PgArrayView<int?>? value)
        {
            if (state is null)
            {
                return value;
            }

            value?.Dispose();
            return state;
        }

        /// <summary>
        /// Returns the final concrete SQL array state.
        /// </summary>
        public static PgArrayView<int?>? Final(PgAggregateContext context, PgArrayView<int?>? state, ValueTuple arguments) => state;
    }
}
