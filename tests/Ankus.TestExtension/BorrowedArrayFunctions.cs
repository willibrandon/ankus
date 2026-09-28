using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Observes borrowed native array identity, iteration and lifetime through real backend calls.
/// </summary>
[PgSchema("borrowed_arrays")]
public static partial class BorrowedArrayFunctions
{
    /// <summary>
    /// Reads exact copied metadata and native cell values without managed element mappings.
    /// </summary>
    /// <param name="value">The raw array, including domains over arrays.</param>
    /// <returns>Type, shape, NULL metadata and each native formatted cell.</returns>
    [PgFunction]
    public static string?[] ArrayViewSnapshot([PgSqlType("any", Schema = "pg_catalog")] PgDatum value)
    {
        using var view = new PgArrayView(value);
        return Snapshot(view);
    }

    /// <summary>
    /// Compares flat array and cell addresses with independently deconstructed native storage.
    /// </summary>
    /// <param name="value">The source array snapshot.</param>
    /// <returns>Whether every original word, address and NULL flag is retained.</returns>
    [PgFunction]
    public static bool ArrayViewStorage([PgSqlType("any", Schema = "pg_catalog")] PgDatum value)
    {
        using var view = new PgArrayView(value);
        long?[] original = Spi.ExecuteScalar<long?[]>("SELECT tests.array_bits($1)",
            [SpiParameter.Create(unchecked((long)value.DangerousGetBits()))]);
        long?[] cells = [unchecked((long)view.Datum.DangerousGetBits()),
            .. view.Select(static cell => cell.IsNull ? (long?)null : unchecked((long)cell.DangerousGetBits()))];
        long?[] indexed = [unchecked((long)view.Datum.DangerousGetBits()),
            .. Enumerable.Range(0, view.Count).Select(index => view[index].IsNull ? (long?)null : unchecked((long)view[index].DangerousGetBits()))];
        return original.SequenceEqual(cells) && original.SequenceEqual(indexed);
    }

    /// <summary>
    /// Interleaves cursors, releases one early and retains its escaped by-reference element.
    /// </summary>
    /// <returns>Exact cursor states and values after independent advancement and disposal.</returns>
    [PgFunction]
    public static string?[] ArrayViewIterators()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT ARRAY['first',NULL,'third','fourth']::text[]");
        using var view = new PgArrayView(result[0][0]);
        using IEnumerator<PgDatum> first = view.GetEnumerator();
        using IEnumerator<PgDatum> second = view.GetEnumerator();
        var observed = new List<string?> { Failure(() => _ = first.Current) };
        observed.Add(first.MoveNext().ToString());
        PgDatum escaped = first.Current;
        observed.Add(escaped.ToPostgresString());
        observed.Add(second.MoveNext().ToString());
        observed.Add(second.Current.ToPostgresString());
        observed.Add(first.MoveNext().ToString());
        observed.Add(first.Current.IsNull.ToString());
        first.Dispose();
        observed.Add(Failure(() => first.MoveNext()));
        observed.Add(escaped.ToPostgresString());
        while (second.MoveNext())
        {
            observed.Add(second.Current.ToPostgresString());
        }

        observed.Add(second.MoveNext().ToString());
        observed.Add(Failure(() => _ = second.Current));
        observed.Add(Failure(second.Reset));
        observed.Add(string.Join(',', view.Select(static cell => cell.ToPostgresString() ?? "NULL")));
        return [.. observed];
    }

    /// <summary>
    /// Expires a view or its source and checks escaped datums, cursors and an independent copy.
    /// </summary>
    /// <param name="mode">Zero disposes the view, one resets its source, two deletes it, and three resets only the source.</param>
    /// <returns>Exact rejection types, preserved metadata and copied native contents.</returns>
    [PgFunction]
    public static string?[] ArrayViewOwners(int mode)
    {
        using PgMemoryContext source = PgMemoryContext.Create("borrowed array source");
        using PgMemoryContext destination = PgMemoryContext.Create("borrowed array independent copy");
        using SpiRawResult result = Spi.QueryRaw("SELECT '[4:5]={owned,NULL}'::text[]");
        PgDatum original = result[0][0].CopyTo(source);
        using var view = new PgArrayView(original);
        PgDatum cell = view[0];
        PgDatum array = view.Datum;
        PgDatum copy = cell.CopyTo(destination);
        using IEnumerator<PgDatum> iterator = view.GetEnumerator();
        _ = iterator.MoveNext();
        switch (mode)
        {
            case 0:
                view.Dispose();
                break;
            case 1:
                source.Reset();
                break;
            case 2:
                source.Dispose();
                break;
            case 3:
                source.ResetOnly();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return [Failure(() => _ = view[0]), Failure(() => _ = view.Datum), Failure(() => view.GetEnumerator()),
            Failure(() => iterator.MoveNext()), Failure(() => _ = iterator.Current),
            Failure(() => cell.DangerousGetBits()), Failure(() => array.ToPostgresString()),
            copy.ToPostgresString(), view.Count.ToString(CultureInfo.InvariantCulture), string.Join(',', view.LowerBounds.ToArray()),
            view.HasNulls.ToString()];
    }

    /// <summary>
    /// Rejects adjacent flat and multidimensional bounds without damaging the remaining reads.
    /// </summary>
    /// <returns>Exact argument errors and unchanged valid values.</returns>
    [PgFunction]
    public static string?[] ArrayViewBounds()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT '[-2:-1][4:6]={{0,NULL,7},{-9,11,15}}'::integer[]");
        using var view = new PgArrayView(result[0][0]);
        return [Failure(() => _ = view[-1]), Failure(() => _ = view[6]),
            Failure(() => view.GetValue(-2)), Failure(() => view.GetValue(-3, 4)), Failure(() => view.GetValue(0, 4)),
            Failure(() => view.GetValue(-2, 3)), Failure(() => view.GetValue(-2, 7)),
            view.GetValue(-2, 4).ToPostgresString(), view.GetValue(-2, 5).ToPostgresString(),
            view.GetValue(-1, 6).ToPostgresString(), view[3].ToPostgresString()];
    }

    /// <summary>
    /// Rejects invalid constructors and checks that normal operations still work in the same backend.
    /// </summary>
    /// <returns>The exact errors from null, SQL NULL, scalar and stale source inputs.</returns>
    [PgFunction]
    public static string[] ArrayViewConstructionErrors()
    {
        using SpiRawResult result = Spi.QueryRaw("SELECT NULL::integer[], 42, ARRAY[7]");
        PgDatum stale = result[0][2];
        string[] errors = [Failure(() => new PgArrayView(null!).Dispose()), Failure(() => new PgArrayView(result[0][0]).Dispose()),
            Failure(() => new PgArrayView(result[0][1]).Dispose())];
        result.Dispose();
        return [.. errors, Failure(() => new PgArrayView(stale).Dispose()), Spi.ExecuteScalar<int>("SELECT 42").ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>
    /// Copies borrowed metadata and cell formatting into independent managed strings.
    /// </summary>
    private static string?[] Snapshot(PgArrayView view)
        => [view.TypeOid.ToString(CultureInfo.InvariantCulture), view.ElementTypeOid.ToString(CultureInfo.InvariantCulture),
            view.Rank.ToString(CultureInfo.InvariantCulture), view.Count.ToString(CultureInfo.InvariantCulture), view.HasNulls.ToString(),
            string.Join(',', view.Lengths.ToArray()), string.Join(',', view.LowerBounds.ToArray()),
            .. view.Select(static cell => cell.ToPostgresString())];

    /// <summary>
    /// Exposes exact expected failure types and parameters to assertions outside the extension.
    /// </summary>
    private static string Failure(Action action)
    {
        try
        {
            action();
            return "accepted";
        }
        catch (ArgumentException error)
        {
            return error.GetType().Name + ":" + error.ParamName;
        }
        catch (PgException error)
        {
            return error.SqlState + ":" + error.Message;
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or NotSupportedException)
        {
            return error.GetType().Name;
        }
    }
}
