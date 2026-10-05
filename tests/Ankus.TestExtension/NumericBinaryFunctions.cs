namespace Ankus.TestExtension;

/// <summary>
/// Exercises opaque numeric ownership, portable formatting and reuse after native cleanup and rollback.
/// </summary>
public static class NumericBinaryFunctions
{
    /// <summary>
    /// Formats a numeric copied from a native datum without calling numeric_out.
    /// </summary>
    /// <param name="value">The native numeric.</param>
    /// <returns>Canonical output rendered from the portable numeric_send digits.</returns>
    [PgFunction]
    public static string NumericBinaryFormat(PgNumeric value) => value.Text;

    /// <summary>
    /// Retains a numeric after SPI disposal and an unrelated arithmetic rollback, then reuses its opaque bytes.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>The observed error, detached formatting and subsequent arithmetic output.</returns>
    [PgFunction]
    public static string NumericBinaryReuse(PgNumeric left, PgNumeric right)
    {
        PgNumeric captured = Spi.Connect(session => session.ExecuteScalar<PgNumeric>(
            "SELECT $1::numeric + $2::numeric", SpiParameter.Create(left), SpiParameter.Create(right)));
        string failure = "no error";
        try
        {
            _ = PgTransaction.RunInSubtransaction(() => PgNumeric.One / PgNumeric.Zero);
        }
        catch (PgException error) when (error.SqlState == "22012")
        {
            failure = error.SqlState;
        }

        _ = Spi.ExecuteScalar<string>("SELECT repeat('overwrite freed operation memory', 10000)");
        return $"{failure}:{captured.Text}:{((captured + left) * right).Text}";
    }
}
