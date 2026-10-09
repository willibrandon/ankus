using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Reads installed catalog signatures for one identity function per supported managed type, as pgrx's
/// <c>signature_matrix_functions_are_installed</c> does.
/// </summary>
/// <param name="context">The per-test context.</param>
[TestClass]
public sealed class SignatureMatrixTests(TestContext context)
{
    private static readonly string[] s_expected =
    [
        "signature_bool(value boolean) boolean",
        "signature_bytes(value bytea) bytea",
        "signature_custom(value custom_values.number) custom_values.number",
        "signature_date(value date) date",
        "signature_double(value double precision) double precision",
        "signature_enum(value datatype.enum_mood) datatype.enum_mood",
        "signature_float(value real) real",
        "signature_inet(value inet) inet",
        "signature_int(value integer) integer",
        "signature_int_vector(value integer[]) integer[]",
        "signature_internal(value internal) internal",
        "signature_interval(value interval) interval",
        "signature_json(value json) json",
        "signature_jsonb(value jsonb) jsonb",
        "signature_long(value bigint) bigint",
        "signature_nullable_int(value integer) integer",
        "signature_numeric(value numeric) numeric",
        "signature_numeric10_and2(value numeric) numeric",
        "signature_numeric_vector(value numeric[]) numeric[]",
        "signature_point(value point) point",
        "signature_range_date(value daterange) daterange",
        "signature_range_int(value int4range) int4range",
        "signature_range_long(value int8range) int8range",
        "signature_range_numeric(value numrange) numrange",
        "signature_range_timestamp(value tsrange) tsrange",
        "signature_range_timestamp_tz(value tstzrange) tstzrange",
        "signature_rune(value character varying) character varying",
        "signature_sbyte(value \"char\") \"char\"",
        "signature_set_of() SETOF integer",
        "signature_short(value smallint) smallint",
        "signature_string(value text) text",
        "signature_table() TABLE(id integer)",
        "signature_time(value time without time zone) time without time zone",
        "signature_time_tz(value time with time zone) time with time zone",
        "signature_timestamp(value timestamp without time zone) timestamp without time zone",
        "signature_timestamp_tz(value timestamp with time zone) timestamp with time zone",
        "signature_uint(value oid) oid",
        "signature_uuid(value uuid) uuid",
    ];

    /// <summary>
    /// Every identity function installs with its expected argument and result types, and the set and table returns
    /// produce their row.
    /// </summary>
    [TestMethod]
    public Task InstalledSignaturesMatchTheTypeMatrix()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InstalledSignaturesMatchTheTypeMatrix),
            async (connection, transaction, token) =>
            {
                await using var command = new NpgsqlCommand("""
                    SET LOCAL search_path TO pg_catalog;
                    SELECT array_agg(proname || '(' || pg_get_function_identity_arguments(oid) || ') ' || pg_get_function_result(oid)
                                     ORDER BY proname)
                      FROM pg_proc WHERE pronamespace = 'signatures'::regnamespace
                    """, connection, transaction);
                string[] actual = Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token));
                Assert.AreSequenceEqual(s_expected, actual, string.Join(Environment.NewLine, actual));
                command.CommandText = "SELECT (SELECT count(*) FROM signatures.signature_set_of()) + (SELECT count(*) FROM signatures.signature_table())";
                Assert.AreEqual(2L, await command.ExecuteScalarAsync(token));
            }, context.CancellationToken);
}
