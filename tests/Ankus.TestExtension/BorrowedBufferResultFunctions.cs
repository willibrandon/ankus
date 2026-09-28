namespace Ankus.TestExtension;

public static partial class BorrowedBufferFunctions
{
    /// <summary>
    /// Leaves the original varchar or domain identity intact for the generated return guard.
    /// </summary>
    /// <param name="value">The original text-compatible datum.</param>
    /// <returns>A text view with its original SQL type.</returns>
    [PgFunction]
    public static PgTextView RawTextReturn(PgAnyElement value) => value.Datum.Read<PgTextView>();

    /// <summary>
    /// Leaves a bytea domain's identity intact for the generated return guard.
    /// </summary>
    /// <param name="value">The original binary datum.</param>
    /// <returns>A bytea view with its original SQL type.</returns>
    [PgFunction]
    public static PgByteaView RawByteaReturn(PgAnyElement value) => value.Datum.Read<PgByteaView>();

    /// <summary>
    /// Keeps both buffer representations across lazy table callbacks and releases them on iterator disposal.
    /// </summary>
    /// <param name="text">The retained text snapshot.</param>
    /// <param name="binary">The retained binary snapshot.</param>
    /// <param name="fail">Whether to throw before the second row.</param>
    /// <returns>Two exact rows or a deliberate iterator error.</returns>
    [PgFunction]
    public static IEnumerable<(PgTextView? Label, PgByteaView? Payload)> BufferTable(PgTextView? text, PgByteaView? binary, bool fail)
    {
        using (text)
        using (binary)
        {
            yield return (text, binary);
            _ = Spi.ExecuteScalar<int>("SELECT 42");
            if (fail)
            {
                throw new InvalidOperationException("Borrowed buffer iterator failed.");
            }

            yield return (text, binary);
        }
    }

    /// <summary>
    /// Returns results from session and prepared-plan conversions after their temporary owners have closed.
    /// </summary>
    /// <returns>Exact text, binary and integer observations from both result paths.</returns>
    [PgFunction]
    public static string[] BufferSpiPairs()
    {
        (PgTextView text, PgByteaView binary) = Spi.Connect(static session
            => session.ExecuteScalars<PgTextView, PgByteaView>("SELECT 'session café'::text,'\\x00ff'::bytea"));
        using (text)
        using (binary)
        {
            (PgTextView preparedText, PgByteaView preparedBytes, int answer) = Spi.Connect(session =>
            {
                using SpiPreparedStatement plan = session.Prepare("SELECT $1,$2,42", typeof(PgTextView), typeof(PgByteaView));
                return plan.ExecuteScalars<PgTextView, PgByteaView, int>(SpiParameter.Create(text), SpiParameter.Create(binary));
            });
            using (preparedText)
            using (preparedBytes)
            {
                return [text.ToString(), Convert.ToHexString(binary.DangerousGetSpan()), preparedText.ToString(),
                    Convert.ToHexString(preparedBytes.DangerousGetSpan()), answer.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            }
        }
    }
}
