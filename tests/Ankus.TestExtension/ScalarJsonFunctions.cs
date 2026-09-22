using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises statically generated JSON contracts for full-range scalar values inside Native AOT.
/// </summary>
public static class ScalarJsonFunctions
{
    /// <summary>
    /// Reads and writes a scalar property using its statically registered converter.
    /// </summary>
    /// <param name="type">The scalar type.</param>
    /// <param name="json">The object containing a Value property.</param>
    /// <returns>The serialized object.</returns>
    [PgFunction]
    public static PgJson ScalarJson(string type, PgJson json) => type switch
    {
        "date" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgDate),
        "time" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgTime),
        "timetz" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgTimeTz),
        "timestamp" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgTimestamp),
        "timestamptz" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgTimestampTz),
        "interval" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgInterval),
        "numeric" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgNumeric),
        "inet" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgInet),
        "cidr" => Exchange(json, ScalarJsonContext.Default.ScalarEnvelopePgCidr),
        "nullable" => Exchange(json, ScalarJsonContext.Default.NullableScalars),
        _ => throw new ArgumentException("Unknown type.", nameof(type)),
    };

    /// <summary>
    /// Recovers from JSON validation errors while preserving prior writes and an active prepared plan.
    /// </summary>
    /// <param name="type">The scalar type.</param>
    /// <param name="json">The input object.</param>
    /// <returns>The exception path, native SQLSTATE, finally count, retained contexts and surviving writes.</returns>
    [PgFunction]
    public static string ScalarJsonRecovery(string type, PgJson json) => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE scalar_json_writes(value int)");
        session.Execute("INSERT INTO scalar_json_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM scalar_json_writes");
        const string countSql = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(countSql);
        string failure = "no error";
        int finalized = 0;
        for (int index = 0; index < 50; index++)
        {
            try
            {
                _ = ScalarJson(type, json);
            }
            catch (JsonException error)
            {
                failure = error.Path + ":" + (error.InnerException is PgException native ? native.SqlState : "JSON");
            }
            finally
            {
                finalized++;
            }
        }

        session.Execute("INSERT INTO scalar_json_writes VALUES (2)");
        return $"{failure}:{finalized}:" + (session.ExecuteScalar<long>(countSql) - before) + ":" + plan.ExecuteScalar<long>();
    });

    private static PgJson Exchange<T>(PgJson json, JsonTypeInfo<T> metadata)
        => PgJson.Serialize(json.Deserialize(metadata) ?? throw new JsonException("Expected an object."), metadata);
}

/// <summary>
/// Wraps a scalar to exercise source-generated JSON conversion inside a PostgreSQL callback.
/// </summary>
/// <typeparam name="T">The scalar under test.</typeparam>
/// <param name="Value">The wrapped value.</param>
internal sealed record ScalarEnvelope<T>(T Value);

/// <summary>
/// Exercises independent nullable temporal and numeric properties in one source-generated JSON contract.
/// </summary>
/// <param name="Date">The nullable date.</param>
/// <param name="Time">The nullable wall-clock time.</param>
/// <param name="TimeTz">The nullable fixed-offset time.</param>
/// <param name="Timestamp">The nullable wall-clock timestamp.</param>
/// <param name="TimestampTz">The nullable UTC timestamp.</param>
/// <param name="Interval">The nullable calendar interval.</param>
/// <param name="Numeric">The nullable full-range numeric value.</param>
internal sealed record NullableScalars(PgDate? Date, PgTime? Time, PgTimeTz? TimeTz, PgTimestamp? Timestamp,
    PgTimestampTz? TimestampTz, PgInterval? Interval, PgNumeric? Numeric);

/// <summary>
/// Supplies statically generated metadata for scalar JSON tests running under Native AOT.
/// </summary>
[JsonSerializable(typeof(ScalarEnvelope<PgDate>))]
[JsonSerializable(typeof(ScalarEnvelope<PgInet>))]
[JsonSerializable(typeof(ScalarEnvelope<PgCidr>))]
[JsonSerializable(typeof(ScalarEnvelope<PgTime>))]
[JsonSerializable(typeof(ScalarEnvelope<PgTimeTz>))]
[JsonSerializable(typeof(ScalarEnvelope<PgTimestamp>))]
[JsonSerializable(typeof(ScalarEnvelope<PgTimestampTz>))]
[JsonSerializable(typeof(ScalarEnvelope<PgInterval>))]
[JsonSerializable(typeof(ScalarEnvelope<PgNumeric>))]
[JsonSerializable(typeof(NullableScalars))]
internal sealed partial class ScalarJsonContext : JsonSerializerContext;
