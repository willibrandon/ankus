namespace Ankus.TestExtension;

/// <summary>
/// Exercises UUID, JSON, and JSONB through generated boundaries and each SPI ownership path.
/// </summary>
public static class ExtendedDatumFunctions
{
    /// <summary>
    /// Returns a nullable UUID through the selected SPI path.
    /// </summary>
    /// <param name="value">The UUID or SQL NULL.</param>
    /// <param name="mode">Zero returns directly; one queries; two prepares; three uses a session; four fetches a cursor.</param>
    /// <returns>The independently owned value.</returns>
    [PgFunction]
    public static Guid? ExchangeUuid(Guid? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns nullable JSON through the selected SPI path.
    /// </summary>
    /// <param name="value">The JSON value or SQL NULL.</param>
    /// <param name="mode">The conversion path.</param>
    /// <returns>The JSON value.</returns>
    [PgFunction]
    public static PgJson? ExchangeJson(PgJson? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Returns nullable JSONB through the selected SPI path.
    /// </summary>
    /// <param name="value">The JSONB value or SQL NULL.</param>
    /// <param name="mode">The conversion path.</param>
    /// <returns>The JSONB value.</returns>
    [PgFunction]
    public static PgJsonb? ExchangeJsonb(PgJsonb? value, int mode) => Exchange(value, mode);

    /// <summary>
    /// Formats PostgreSQL input using Guid's independent textual representation.
    /// </summary>
    /// <param name="value">The UUID.</param>
    /// <returns>The conventional dashed UUID string.</returns>
    [PgFunction]
    public static string UuidText(Guid value) => value.ToString("D");

    /// <summary>
    /// Creates a Guid independently of the PostgreSQL UUID reader.
    /// </summary>
    /// <param name="value">The UUID text.</param>
    /// <returns>The parsed UUID.</returns>
    [PgFunction]
    public static Guid UuidFromText(string value) => Guid.Parse(value);

    /// <summary>
    /// Creates JSON from managed text.
    /// </summary>
    /// <param name="text">The JSON text.</param>
    /// <returns>A JSON datum.</returns>
    [PgFunction]
    public static PgJson JsonFromText(string text) => new(text);

    /// <summary>
    /// Creates JSONB from managed text, allowing PostgreSQL to enforce jsonb's numeric and Unicode restrictions.
    /// </summary>
    /// <param name="text">The JSON text.</param>
    /// <returns>A JSONB datum.</returns>
    [PgFunction]
    public static PgJsonb JsonbFromText(string text) => new(text);

    /// <summary>
    /// Returns the default JSON value, distinct from SQL NULL.
    /// </summary>
    /// <returns>JSON null.</returns>
    [PgFunction]
    public static PgJson JsonDefault() => default;

    /// <summary>
    /// Returns the default JSONB value, distinct from SQL NULL.
    /// </summary>
    /// <returns>JSONB null.</returns>
    [PgFunction]
    public static PgJsonb JsonbDefault() => default;

    /// <summary>
    /// Parses PostgreSQL JSONB with source-generated metadata and embeds typed JSON fields when serializing.
    /// </summary>
    /// <param name="input">The input envelope.</param>
    /// <returns>The envelope with its UUID replaced and its nested JSON values preserved.</returns>
    [PgFunction]
    public static PgJsonb JsonEnvelopeTransform(PgJsonb input)
    {
        JsonEnvelope envelope = input.Deserialize(ExtensionJsonContext.Default.JsonEnvelope)
            ?? throw new InvalidOperationException("An envelope is required.");
        return PgJsonb.Serialize(envelope with { Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff") },
            ExtensionJsonContext.Default.JsonEnvelope);
    }

    /// <summary>
    /// Deserializes and serializes an envelope through the JSON metadata overloads inside Native AOT.
    /// </summary>
    /// <param name="input">The input envelope.</param>
    /// <returns>The serialized envelope.</returns>
    [PgFunction]
    public static PgJson JsonEnvelopeRoundtrip(PgJson input)
        => PgJson.Serialize(input.Deserialize(ExtensionJsonContext.Default.JsonEnvelope), ExtensionJsonContext.Default.JsonEnvelope);

    /// <summary>
    /// Converts a JSONB parameter that fails in PostgreSQL and verifies a prior write in the session survives.
    /// </summary>
    /// <param name="text">Syntactically valid JSON rejected by jsonb.</param>
    /// <returns>The SQLSTATE and recovered row count.</returns>
    [PgFunction]
    public static string JsonbParameterRecovery(string text)
        => Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE json_recovery (value int); INSERT INTO json_recovery VALUES (42)");
            try
            {
                session.Execute("INSERT INTO json_recovery SELECT 99 WHERE $1 IS NOT NULL", SpiParameter.Create(new PgJsonb(text)));
                return "unexpected success";
            }
            catch (PgException error)
            {
                return error.SqlState + ":" + session.ExecuteScalar<long>("SELECT count(*) FROM json_recovery");
            }
        });

    /// <summary>
    /// Materializes a caller-supplied domain query, then verifies copied UUID and JSON cells after another SPI call.
    /// </summary>
    /// <param name="sql">The query yielding one UUID, one JSON, and one JSONB column.</param>
    /// <returns>The owned cell values.</returns>
    [PgFunction]
    public static string ExtendedDomainValues(string sql)
    {
        SpiResult rows = Spi.Query(sql);
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return rows[0].Get<Guid>(0) + "|" + rows[0].Get<PgJson>(1).Text + "|" + rows[0].Get<PgJsonb>(2).Text;
    }

    private static T Exchange<T>(T value, int mode)
    {
        const string sql = "SELECT $1";
        switch (mode)
        {
            case 0:
                return value;
            case 1:
                return Spi.ExecuteScalar<T>(sql, SpiParameter.Create(value));
            case 2:
                using (SpiPreparedStatement plan = Spi.Prepare(sql, typeof(T)))
                {
                    return plan.ExecuteScalar<T>(SpiParameter.Create(value));
                }

            case 3:
                return Spi.Connect(session => session.ExecuteScalar<T>(sql, SpiParameter.Create(value)));
            case 4:
                using (SpiCursor cursor = Spi.OpenCursor(sql, SpiParameter.Create(value)))
                {
                    SpiResult rows = cursor.Fetch(1);
                    cursor.Fetch(1);
                    return rows[0].Get<T>(0);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
