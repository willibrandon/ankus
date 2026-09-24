using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises first-row pairs and triples through standalone, scoped, and prepared SPI calls.
/// </summary>
public static class SpiScalarFunctions
{
    /// <summary>
    /// Reads two nullable values and uses them after their native owners have been released.
    /// </summary>
    /// <param name="api">Zero for standalone, one for session, two for retained plan, or three for session-owned plan.</param>
    /// <param name="sql">The SQL commands.</param>
    /// <param name="number">The integer parameter.</param>
    /// <param name="text">The text parameter.</param>
    /// <param name="bytes">The bytea parameter.</param>
    /// <returns>The first two values with visible NULL markers.</returns>
    [PgFunction]
    public static string SpiScalarPair(int api, string sql, int? number, string? text, byte[]? bytes)
    {
        (int? first, string? second) = ReadPair<int?, string?>(api, sql, Parameters(number, text, bytes));
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return (first?.ToString(CultureInfo.InvariantCulture) ?? "<null>") + "|" + (second ?? "<null>");
    }

    /// <summary>
    /// Reads three nullable values and uses them after their native owners have been released.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="sql">The SQL commands.</param>
    /// <param name="number">The integer parameter.</param>
    /// <param name="text">The text parameter.</param>
    /// <param name="bytes">The bytea parameter.</param>
    /// <returns>The first three values with visible NULL markers and hexadecimal binary data.</returns>
    [PgFunction]
    public static string SpiScalarTriple(int api, string sql, int? number, string? text, byte[]? bytes)
    {
        (int? first, string? second, byte[]? third) = ReadTriple<int?, string?, byte[]?>(api, sql, Parameters(number, text, bytes));
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return (first?.ToString(CultureInfo.InvariantCulture) ?? "<null>") + "|" + (second ?? "<null>") + "|" +
            (third is null ? "<null>" : Convert.ToHexString(third));
    }

    /// <summary>
    /// Catches native and managed result errors, then verifies another first-row read can complete.
    /// </summary>
    /// <param name="api">The SPI owner selection.</param>
    /// <param name="columns">Whether to request two or three required integers.</param>
    /// <param name="sql">The failing commands.</param>
    /// <returns>The exception type or SQLSTATE, its message, and a follow-up result.</returns>
    [PgFunction]
    public static string SpiScalarRecover(int api, int columns, string sql)
    {
        string error;
        try
        {
            if (columns == 2)
            {
                ReadPair<int, int>(api, sql, Parameters(null, null, null));
            }
            else
            {
                ReadTriple<int, int, int>(api, sql, Parameters(null, null, null));
            }

            return "unexpected success";
        }
        catch (PgException exception)
        {
            error = exception.SqlState + ":" + exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            error = nameof(InvalidOperationException) + ":" + exception.Message;
        }
        catch (InvalidCastException exception)
        {
            error = nameof(InvalidCastException) + ":" + exception.Message;
        }

        (int first, int second, int third) = ReadTriple<int, int, int>(api, "SELECT 40, 1, 1", Parameters(null, null, null));
        return error + "|" + (first + second + third).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes a pair through the selected ownership path.
    /// </summary>
    /// <typeparam name="TFirst">The first managed type.</typeparam>
    /// <typeparam name="TSecond">The second managed type.</typeparam>
    /// <param name="api">The owner selection.</param>
    /// <param name="sql">The commands.</param>
    /// <param name="parameters">Bound values.</param>
    /// <returns>The copied pair.</returns>
    private static (TFirst, TSecond) ReadPair<TFirst, TSecond>(int api, string sql, SpiParameter[] parameters)
        => api switch
        {
            0 => Spi.ExecuteScalars<TFirst, TSecond>(sql, parameters),
            1 => Spi.Connect(session => session.ExecuteScalars<TFirst, TSecond>(sql, parameters)),
            2 => PreparedPair<TFirst, TSecond>(Spi.Prepare(sql, typeof(int), typeof(string), typeof(byte[])), parameters),
            3 => Spi.Connect(session => PreparedPair<TFirst, TSecond>(
                session.Prepare(sql, typeof(int), typeof(string), typeof(byte[])), parameters)),
            _ => throw new ArgumentOutOfRangeException(nameof(api)),
        };

    /// <summary>
    /// Executes a triple through the selected ownership path.
    /// </summary>
    /// <typeparam name="TFirst">The first managed type.</typeparam>
    /// <typeparam name="TSecond">The second managed type.</typeparam>
    /// <typeparam name="TThird">The third managed type.</typeparam>
    /// <param name="api">The owner selection.</param>
    /// <param name="sql">The commands.</param>
    /// <param name="parameters">Bound values.</param>
    /// <returns>The copied triple.</returns>
    private static (TFirst, TSecond, TThird) ReadTriple<TFirst, TSecond, TThird>(int api, string sql, SpiParameter[] parameters)
        => api switch
        {
            0 => Spi.ExecuteScalars<TFirst, TSecond, TThird>(sql, parameters),
            1 => Spi.Connect(session => session.ExecuteScalars<TFirst, TSecond, TThird>(sql, parameters)),
            2 => PreparedTriple<TFirst, TSecond, TThird>(Spi.Prepare(sql, typeof(int), typeof(string), typeof(byte[])), parameters),
            3 => Spi.Connect(session => PreparedTriple<TFirst, TSecond, TThird>(
                session.Prepare(sql, typeof(int), typeof(string), typeof(byte[])), parameters)),
            _ => throw new ArgumentOutOfRangeException(nameof(api)),
        };

    /// <summary>
    /// Reads a pair and disposes its prepared statement before returning the values.
    /// </summary>
    /// <typeparam name="TFirst">The first managed type.</typeparam>
    /// <typeparam name="TSecond">The second managed type.</typeparam>
    /// <param name="statement">The owned statement.</param>
    /// <param name="parameters">Bound values.</param>
    /// <returns>The copied pair.</returns>
    private static (TFirst, TSecond) PreparedPair<TFirst, TSecond>(SpiPreparedStatement statement, SpiParameter[] parameters)
    {
        using (statement)
        {
            return statement.ExecuteScalars<TFirst, TSecond>(parameters);
        }
    }

    /// <summary>
    /// Reads a triple and disposes its prepared statement before returning the values.
    /// </summary>
    /// <typeparam name="TFirst">The first managed type.</typeparam>
    /// <typeparam name="TSecond">The second managed type.</typeparam>
    /// <typeparam name="TThird">The third managed type.</typeparam>
    /// <param name="statement">The owned statement.</param>
    /// <param name="parameters">Bound values.</param>
    /// <returns>The copied triple.</returns>
    private static (TFirst, TSecond, TThird) PreparedTriple<TFirst, TSecond, TThird>(
        SpiPreparedStatement statement, SpiParameter[] parameters)
    {
        using (statement)
        {
            return statement.ExecuteScalars<TFirst, TSecond, TThird>(parameters);
        }
    }

    /// <summary>
    /// Creates consistently typed nullable parameters for each ownership path.
    /// </summary>
    /// <param name="number">The integer parameter.</param>
    /// <param name="text">The text parameter.</param>
    /// <param name="bytes">The bytea parameter.</param>
    /// <returns>The bound parameters.</returns>
    private static SpiParameter[] Parameters(int? number, string? text, byte[]? bytes)
        => [SpiParameter.Create(number), SpiParameter.Create(text), SpiParameter.Create(bytes)];
}
