using System.Globalization;

namespace Ankus.TestExtension;

public static partial class BorrowedArrayFunctions
{
    /// <summary>
    /// Reads a caller-proven physical array Datum and releases every flattening and cursor context repeatedly.
    /// </summary>
    /// <param name="bits">The original Datum whose native caller retains its storage during this callback.</param>
    /// <param name="type">The independently resolved original array OID.</param>
    /// <param name="kind">The storage form observed by native macros before managed execution.</param>
    /// <returns>Physical storage observations, copied contents and independent native cleanup counts.</returns>
    [PgFunction]
    public static string?[] ArrayViewNative(long bits, uint type, string kind)
    {
        PgDatum source;
        unsafe
        {
            source = PgDatum.DangerousCreate(unchecked((nuint)bits), type, PgMemoryContext.Current);
        }

        long before = OwnerCount();
        string?[]? snapshot = null;
        string owner = "borrowed";
        bool copied = false;
        for (int iteration = 0; iteration < 40; iteration++)
        {
            using var view = new PgArrayView(source);
            copied = view.Datum.DangerousGetBits() != source.DangerousGetBits();
            if (iteration == 0)
            {
                snapshot = Snapshot(view);
                if (copied)
                {
                    owner = Spi.ExecuteScalar<string>("SELECT tests.array_owner($1)",
                        [SpiParameter.Create(unchecked((long)view.Datum.DangerousGetBits()))]);
                }
            }

            using IEnumerator<PgDatum> cursor = view.GetEnumerator();
            _ = cursor.MoveNext();
            if (OwnerCount() != before + 2)
            {
                throw new InvalidOperationException("The view and cursor do not have exactly two private contexts.");
            }
        }

        return [kind, copied.ToString(), owner, before.ToString(CultureInfo.InvariantCulture),
            OwnerCount().ToString(CultureInfo.InvariantCulture), .. snapshot!];
    }

    /// <summary>
    /// Compares a generated borrowed argument with the original array address independently supplied by native code.
    /// </summary>
    /// <param name="value">The generated direct array input.</param>
    /// <param name="address">The original flat native array address.</param>
    /// <returns>Whether the generated input and all flat cells preserve original storage.</returns>
    [PgFunction]
    public static bool ArrayViewOriginal(PgArrayView value, long address)
    {
        if (value.Datum.DangerousGetBits() != unchecked((nuint)address))
        {
            return false;
        }

        long?[] original = Spi.ExecuteScalar<long?[]>("SELECT tests.array_bits($1)", [SpiParameter.Create(address)]);
        long?[] cells = [address, .. value.Select(static cell => cell.IsNull ? (long?)null : unchecked((long)cell.DangerousGetBits()))];
        return original.SequenceEqual(cells);
    }

    /// <summary>
    /// Exercises typed SPI/function results, exact raw parameter binding and views over existing raw rows.
    /// </summary>
    /// <returns>The concatenated array's native metadata and the raw row's zero, NULL and final cells.</returns>
    [PgFunction]
    public static string?[] ArrayViewSpi()
    {
        using PgArrayView source = Spi.ExecuteScalar<PgArrayView>("SELECT '[-1:1]={first,NULL,last}'::text[]");
        using PgArrayView returned = PgFunctions.Call<PgArrayView>("pg_catalog.array_cat",
            PgFunctionArgument.Create(source), PgFunctionArgument.Create<string[]>(["tail"]));
        using PgArrayView echoed = Spi.ExecuteScalar<PgArrayView>("SELECT $1", [SpiParameter.Create(returned)]);
        using SpiRawResult raw = Spi.QueryRaw("SELECT ARRAY[0,NULL,7], NULL::integer[]");
        using PgArrayView cells = raw[0][0].Read<PgArrayView>();
        if (raw[0][1].Read<PgArrayView?>() is not null)
        {
            throw new InvalidOperationException("A SQL NULL array did not remain a null view.");
        }

        return [.. Snapshot(echoed), .. cells.Select(static cell => cell.ToPostgresString())];
    }

    /// <summary>
    /// Verifies an empty native cursor and all immediately adjacent empty-array boundaries.
    /// </summary>
    /// <returns>Exact empty cursor states, boundary errors and retained metadata.</returns>
    [PgFunction]
    public static string[] ArrayViewEmpty()
    {
        using PgArrayView view = Spi.ExecuteScalar<PgArrayView>("SELECT ARRAY[]::integer[]");
        using IEnumerator<PgDatum> cursor = view.GetEnumerator();
        return [Failure(() => _ = cursor.Current), cursor.MoveNext().ToString(), cursor.MoveNext().ToString(),
            Failure(() => _ = cursor.Current), Failure(() => _ = view[-1]), Failure(() => _ = view[0]),
            Failure(() => view.GetValue()), Failure(() => view.GetValue(1)), view.Rank.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>
    /// Counts only native array view and iterator contexts through the independent PostgreSQL inventory.
    /// </summary>
    private static long OwnerCount()
        => Spi.ExecuteScalar<long>("SELECT count(*) FROM ankus_test_memory.contexts WHERE ident LIKE 'Ankus borrowed array%'");
}
