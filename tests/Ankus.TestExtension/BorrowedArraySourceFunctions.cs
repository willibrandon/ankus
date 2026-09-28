using System.Globalization;

namespace Ankus.TestExtension;

public static partial class BorrowedArrayFunctions
{
    /// <summary>
    /// Resets only original array storage while nested views and cursor bookkeeping remain allocated.
    /// </summary>
    /// <returns>Independent native context counts, exact alias failures and surviving copied values.</returns>
    [PgFunction]
    public static string?[] ArrayViewSourceReset()
    {
        using PgMemoryContext source = PgMemoryContext.Create("borrowed nested source");
        using PgMemoryContext destination = PgMemoryContext.Create("borrowed nested copy");
        using SpiRawResult result = Spi.QueryRaw("SELECT '[4:5]={owned,NULL}'::text[]");
        PgDatum original = result[0][0].CopyTo(source);
        using var first = new PgArrayView(original);
        using var nested = new PgArrayView(first.Datum);
        using IEnumerator<PgDatum> iterator = nested.GetEnumerator();
        _ = iterator.MoveNext();
        PgDatum cell = iterator.Current;
        PgDatum array = nested.Datum;
        PgDatum copy = array.CopyTo(destination);
        PgDatum copiedCell = cell.CopyTo(destination);
        bool sameStorage = original.DangerousGetBits() == array.DangerousGetBits();
        long before = OwnerCount();
        source.ResetOnly();
        long after = OwnerCount();
        return [sameStorage.ToString(), source.IsAlive.ToString(), before.ToString(CultureInfo.InvariantCulture),
            after.ToString(CultureInfo.InvariantCulture), Failure(() => original.DangerousGetBits()),
            Failure(() => _ = first.Datum), Failure(() => _ = nested[0]), Failure(() => _ = nested.Datum),
            Failure(() => nested.GetEnumerator()), Failure(() => iterator.MoveNext()), Failure(() => _ = iterator.Current),
            Failure(() => cell.ToPostgresString()), Failure(() => array.Read<PgArrayView>()),
            Failure(() => array.CopyTo(destination)), copiedCell.ToPostgresString(), copy.ToPostgresString(),
            nested.Count.ToString(CultureInfo.InvariantCulture), string.Join(',', nested.LowerBounds.ToArray()),
            Spi.ExecuteScalar<int>("SELECT 42").ToString(CultureInfo.InvariantCulture)];
    }
}
