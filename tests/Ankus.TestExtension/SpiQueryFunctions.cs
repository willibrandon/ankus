namespace Ankus.TestExtension;

/// <summary>
/// Exercises typed SPI parameters, scalar reads, materialized rows, and result metadata from managed extension code.
/// </summary>
public static class SpiQueryFunctions
{
    /// <summary>
    /// Round-trips a nullable Boolean through a bound SPI parameter.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static bool? SpiBoolean(bool? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable internal char through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static sbyte? SpiChar(sbyte? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable smallint through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static short? SpiSmallInt(short? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable integer through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static int? SpiInt(int? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable bigint through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static long? SpiBigInt(long? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable OID through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static uint? SpiOid(uint? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable real through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static float? SpiReal(float? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips a nullable double precision value through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static double? SpiDouble(double? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips nullable text through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static string? SpiText(string? value) => RoundTrip(value);

    /// <summary>
    /// Round-trips nullable bytea through SPI.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <returns>The SPI result.</returns>
    [PgFunction]
    public static byte[]? SpiBytes(byte[]? value) => RoundTrip(value);

    /// <summary>
    /// Describes parameter types as reported by PostgreSQL for explicitly typed NULL parameters.
    /// </summary>
    /// <returns>The PostgreSQL type names.</returns>
    [PgFunction]
    public static string SpiParameterTypes()
        => Spi.ExecuteScalar<string>("""
            SELECT concat_ws(',', pg_typeof($1)::text, pg_typeof($2)::text, pg_typeof($3)::text, pg_typeof($4)::text,
                pg_typeof($5)::text, pg_typeof($6)::text, pg_typeof($7)::text, pg_typeof($8)::text,
                pg_typeof($9)::text, pg_typeof($10)::text)
            """, SpiParameter.Create<bool?>(null), SpiParameter.Create<sbyte?>(null), SpiParameter.Create<short?>(null),
            SpiParameter.Create<int?>(null), SpiParameter.Create<long?>(null), SpiParameter.Create<uint?>(null),
            SpiParameter.Create<float?>(null), SpiParameter.Create<double?>(null),
            SpiParameter.Create<string?>(null), SpiParameter.Create<byte[]?>(null));

    /// <summary>
    /// Returns a materialized query's metadata after another SPI call has completed.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <returns>Exact names and declared type OIDs.</returns>
    [PgFunction]
    public static string SpiMetadata(string sql)
    {
        SpiResult result = Spi.Query(sql);
        Spi.Execute("SELECT 1");
        return string.Join("|", result.Columns.Select(static column => column.Name + ":" + column.TypeOid));
    }

    /// <summary>
    /// Reads nullable text rows after the native tuple table and a subsequent query have been released.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <returns>Ordered cell values with a visible NULL marker.</returns>
    [PgFunction]
    public static string SpiTextRows(string sql)
    {
        SpiResult result = Spi.Query(sql);
        Spi.Query("SELECT repeat('overwrite', 10000)");
        return string.Join("|", result.Select(static row => row.Get<string?>(0) ?? "<null>"));
    }

    /// <summary>
    /// Reads the first column by its exact name, including when duplicate column names are present.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <param name="column">The exact column name.</param>
    /// <returns>The selected integer.</returns>
    [PgFunction]
    public static int SpiNamedValue(string sql, string column) => Spi.Query(sql)[0].Get<int>(column);

    /// <summary>
    /// Gets a materialized row count with explicit read-only mode and row limit.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <param name="readOnly">Whether to use read-only SPI execution.</param>
    /// <param name="limit">The row limit.</param>
    /// <returns>The number of materialized rows.</returns>
    [PgFunction]
    public static int SpiRowCount(string sql, bool readOnly, int limit) => Spi.Query(sql, readOnly, limit).Count;

    /// <summary>
    /// Reads a nullable integer scalar.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <returns>The first value, or NULL for an empty result or SQL NULL.</returns>
    [PgFunction]
    public static int? SpiScalar(string sql) => Spi.ExecuteScalar<int?>(sql);

    /// <summary>
    /// Reads a required integer scalar to exercise null and type checks.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <returns>The required integer.</returns>
    [PgFunction]
    public static int SpiRequiredScalar(string sql) => Spi.ExecuteScalar<int>(sql);

    /// <summary>
    /// Inserts bound text and binary parameters into a test-owned table.
    /// </summary>
    /// <param name="text">The text value.</param>
    /// <param name="bytes">The binary value.</param>
    /// <returns>The inserted row count.</returns>
    [PgFunction]
    public static long SpiInsert(string? text, byte[]? bytes)
        => Spi.Execute("INSERT INTO spi_parameters VALUES ($1, $2)", SpiParameter.Create(text), SpiParameter.Create(bytes));

    /// <summary>
    /// Recovers from a native result-conversion error and performs a second scalar query.
    /// </summary>
    /// <param name="sql">The command whose result cannot be converted.</param>
    /// <returns>The error SQLSTATE and follow-up value.</returns>
    [PgFunction]
    public static string SpiRecoverResultError(string sql)
    {
        try
        {
            Spi.Query(sql);
            return "no error";
        }
        catch (PgException exception)
        {
            return exception.SqlState + ":" + Spi.ExecuteScalar<int>("SELECT 42");
        }
    }

    private static T RoundTrip<T>(T value) => Spi.ExecuteScalar<T>("SELECT $1", SpiParameter.Create(value));
}
