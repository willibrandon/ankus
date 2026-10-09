using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises SPI boundary cases from pgrx's SPI tests: cursor argument counts and parse errors, utility-statement
/// metadata, empty results, mixed identifier quoting, nested domains and required values from empty results.
/// </summary>
[PgSchema("spi_edges")]
public static class SpiEdgeFunctions
{
    /// <summary>
    /// Runs one boundary case and describes its outcome, then proves the backend still runs SPI.
    /// </summary>
    /// <param name="scenario">The case to run.</param>
    /// <returns>The result or the exception type, SQLSTATE and message, followed by a follow-up query's value.</returns>
    [PgFunction]
    public static string SpiEdge(int scenario)
    {
        string outcome;
        try
        {
            outcome = scenario switch
            {
                0 => OpenPrepared([]),
                1 => OpenPrepared([SpiParameter.Create(1), SpiParameter.Create(2)]),
                2 => OpenText("THIS IS NOT SQL"),
                3 => Columns(Spi.Query("SET TIME ZONE 'UTC'")),
                4 => Spi.Select("SELECT 1 WHERE false")[0].Get<int>(0).ToString(CultureInfo.InvariantCulture),
                5 => Spi.QuoteQualifiedIdentifier("unquoted", "actually-quoted") + " " +
                    Spi.QuoteQualifiedIdentifier("actually-quoted", "unquoted"),
                6 => NestedDomain(),
                7 => Spi.ExecuteScalar<int>("SELECT 1 LIMIT 0").ToString(CultureInfo.InvariantCulture),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }
        catch (Exception error) when (error is not ArgumentOutOfRangeException { ParamName: nameof(scenario) })
        {
            outcome = $"{error.GetType().Name}:{(error as PgException)?.SqlState}:{error.Message}";
        }

        return outcome + "|" + Spi.ExecuteScalar<int>("SELECT 6 * 7").ToString(CultureInfo.InvariantCulture);
    }

    private static string OpenPrepared(SpiParameter[] parameters)
    {
        using SpiPreparedStatement statement = Spi.Prepare("SELECT $1", typeof(int));
        using SpiCursor cursor = statement.OpenCursor(parameters);
        return "opened";
    }

    private static string OpenText(string sql)
    {
        using SpiCursor cursor = Spi.OpenCursor(sql);
        return "opened";
    }

    private static string Columns(SpiResult result) => $"columns={result.Columns.Count};rows={result.Count}";

    private static string NestedDomain()
    {
        SpiResult result = Spi.Select("SELECT 'hello'::pg_temp.inner_domain::pg_temp.outer_domain");
        uint expected = Spi.ExecuteScalar<uint>("SELECT 'pg_temp.outer_domain'::regtype::oid");
        return $"{result[0].Get<string>(0)};{result.Columns[0].TypeOid == expected}";
    }
}
