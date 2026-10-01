namespace Ankus.TestExtension;

public static partial class BorrowedArrayFunctions
{
    private static PgArrayView? s_savedArray;
    private static PgDatum? s_savedCell;
    private static IEnumerator<PgDatum>? s_savedIterator;

    /// <summary>
    /// Returns the original input view through the generated return transfer before callback cleanup.
    /// </summary>
    /// <param name="value">The nullable native array input.</param>
    /// <returns>The same array, including its shape and NULL cells.</returns>
    [PgFunction]
    public static PgArrayView? ArrayViewIdentity(PgArrayView? value) => value;

    /// <summary>
    /// Returns a borrowed raw element before the input view's native storage is released.
    /// </summary>
    /// <param name="value">A nonempty native input array.</param>
    /// <returns>The first cell, including a typed SQL NULL or a present zero word.</returns>
    [PgFunction]
    [return: PgSqlType("anyelement", Schema = "pg_catalog")]
    public static PgDatum ArrayViewFirstCell(PgArrayView value) => value[0];

    /// <summary>
    /// Captures views without disposing them so a later callback can observe automatic invalidation.
    /// </summary>
    /// <param name="value">The directly borrowed input.</param>
    /// <returns>The first cell's text before callback expiration.</returns>
    [PgFunction]
    public static string? ArrayViewSave(PgArrayView value)
    {
        s_savedArray = value;
        s_savedCell = value[0];
        s_savedIterator = value.GetEnumerator();
        _ = s_savedIterator.MoveNext();
        return s_savedCell.ToPostgresString();
    }

    /// <summary>
    /// Checks expired input aliases in a later managed callback on the same backend.
    /// </summary>
    /// <returns>The failures for the view, cell and iterator plus remaining native owner count.</returns>
    [PgFunction]
    public static string[] ArrayViewExpired()
        => [Failure(() => _ = s_savedArray!.Datum), Failure(() => s_savedCell!.DangerousGetBits()),
            Failure(() => s_savedIterator!.MoveNext()), Failure(() => _ = s_savedIterator!.Current),
            Spi.ExecuteScalar<long>("SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'")
                .ToString(System.Globalization.CultureInfo.InvariantCulture)];

    /// <summary>
    /// Leaves captured input aliases behind when managed execution fails.
    /// </summary>
    /// <param name="value">The input whose view and cursor must expire on the error path.</param>
    [PgFunction]
    public static void ArrayViewFail(PgArrayView value)
    {
        _ = ArrayViewSave(value);
        throw new InvalidOperationException("Borrowed array callback failed.");
    }

    /// <summary>
    /// Expires a nested borrowed input without invalidating the still-active enclosing callback.
    /// </summary>
    /// <param name="value">The enclosing native array.</param>
    /// <returns>The nested and outer values and the nested lease's exact rejection.</returns>
    [PgFunction]
    public static string?[] ArrayViewNested(PgArrayView value)
    {
        using IEnumerator<PgDatum> iterator = value.GetEnumerator();
        _ = iterator.MoveNext();
        string? before = iterator.Current.ToPostgresString();
        string nested = Spi.ExecuteScalar<string>("SELECT borrowed_arrays.array_view_save(ARRAY['nested'])");
        return [before, nested, Failure(() => s_savedCell!.DangerousGetBits()), value[0].ToPostgresString(),
            iterator.Current.ToPostgresString()];
    }

    /// <summary>
    /// Keeps the generated owned snapshot alive across lazy set callbacks and disposes it on early exit.
    /// </summary>
    /// <param name="value">The array captured by the set iterator.</param>
    /// <returns>Raw elements in order, preserving SQL NULLs.</returns>
    [PgFunction]
    public static IEnumerable<PgAnyElement?> ArrayViewRows(PgArrayView value)
    {
        using (value)
        {
            foreach (PgDatum cell in value)
            {
                yield return cell.IsNull ? null : new PgAnyElement(cell);
            }
        }
    }

    /// <summary>
    /// Retains an array snapshot across transition and final callbacks until the aggregate owner expires.
    /// </summary>
    [PgAggregate(Name = "array_view_first")]
    public sealed class FirstArray : IPgAggregate<PgAggregateState<PgArrayView>?, PgArrayView?>,
        IPgFinalizingAggregate<PgAggregateState<PgArrayView>?, ValueTuple, string?[]?>
    {
        /// <summary>
        /// Retains only the first present input and promptly releases later unused snapshots.
        /// </summary>
        /// <param name="context">The aggregate invocation and owner.</param>
        /// <param name="state">The previously retained snapshot.</param>
        /// <param name="value">The next input snapshot or SQL NULL.</param>
        /// <returns>The first present array state.</returns>
        public static PgAggregateState<PgArrayView>? Transition(PgAggregateContext context, PgAggregateState<PgArrayView>? state, PgArrayView? value)
        {
            if (state is null && value is not null)
            {
                return new(value);
            }

            value?.Dispose();
            return state;
        }

        /// <summary>
        /// Reads all retained native metadata and elements after transition callbacks have ended.
        /// </summary>
        /// <param name="context">The aggregate invocation and owner.</param>
        /// <param name="state">The first array or no present input.</param>
        /// <param name="arguments">The empty direct argument group.</param>
        /// <returns>The exact snapshot or SQL NULL.</returns>
        public static string?[]? Final(PgAggregateContext context, PgAggregateState<PgArrayView>? state, ValueTuple arguments)
            => state is null ? null : Snapshot(state.Value);
    }
}
