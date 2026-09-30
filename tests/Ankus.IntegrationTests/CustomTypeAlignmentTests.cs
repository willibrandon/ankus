using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Checks real PostgreSQL storage and array behavior for both supported datum alignments.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class CustomTypeAlignmentTests(TestContext context)
{
    /// <summary>
    /// The declared alignment reaches the catalog and preserves heap, TOAST and array values.
    /// </summary>
    /// <param name="type">The generated SQL type and function suffix.</param>
    /// <param name="alignment">The expected PostgreSQL catalog alignment code.</param>
    [TestMethod]
    [DataRow("ordinary", "i")]
    [DataRow("wide", "d")]
    public async Task CustomTypeAlignmentPreservesNativeStorage(string type, string alignment)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.AreEqual(alignment, await Scalar<string>(connection,
            $"SELECT typalign::text FROM pg_type WHERE oid = 'aligned_values.{type}'::regtype"));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            CREATE TEMP TABLE aligned_storage (prefix_code smallint, padding text, value aligned_values.{{type}}, suffix_code integer);
            ALTER TABLE aligned_storage ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO aligned_storage VALUES (7, 'x', '{"Text":"","Number":-9223372036854775808}', 11),
                (13, 'xyz', jsonb_build_object('Text', repeat('large 😀',12000), 'Number', 9223372036854775807)::text::aligned_values.{{type}}, 19);
            SELECT count(*) = 2 AND bool_and(
                aligned_values.echo_{{type}}(value)::text::jsonb = CASE prefix_code
                    WHEN 7 THEN '{"Text":"","Number":-9223372036854775808}'::jsonb
                    ELSE jsonb_build_object('Text', repeat('large 😀',12000), 'Number', 9223372036854775807) END
                AND suffix_code = CASE prefix_code WHEN 7 THEN 11 ELSE 19 END)
            FROM aligned_storage;
            """));
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT pg_relation_size(reltoastrelid) > 0 FROM pg_class
            WHERE oid = 'pg_temp.aligned_storage'::regclass
            """));
        Assert.IsTrue(await Scalar<bool>(connection, $$"""
            CREATE TEMP TABLE aligned_array (value aligned_values.{{type}}[]);
            INSERT INTO aligned_array VALUES (array_fill(NULL::aligned_values.{{type}}, ARRAY[3], ARRAY[-1]));
            UPDATE aligned_array SET value[-1] = '{"Text":"é 😀","Number":17}', value[1] = '{"Text":"","Number":-19}';
            WITH output AS (SELECT aligned_values.echo_{{type}}_array(value) AS value FROM aligned_array)
            SELECT array_dims(value) = '[-1:1]'
                AND value[-1]::text::jsonb = '{"Text":"é 😀","Number":17}'::jsonb
                AND value[0] IS NULL AND value[1]::text::jsonb = '{"Text":"","Number":-19}'::jsonb
                AND cardinality(aligned_values.echo_{{type}}_array('{}')) = 0
                AND aligned_values.echo_{{type}}_array(NULL) IS NULL FROM output
            """));
    }

    /// <summary>
    /// Datum alignment does not insert padding into serialized or binary-protocol payloads.
    /// </summary>
    [TestMethod]
    public async Task CustomTypeAlignmentKeepsCodecBytes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        Assert.IsTrue(await Scalar<bool>(connection, """
            SELECT aligned_values.ordinary_send('{"Text":"abc","Number":17}') =
                aligned_values.wide_send('{"Text":"abc","Number":17}')
            """));
    }

    /// <summary>
    /// Executes a scalar and retains the exact server result type.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(context.CancellationToken));
    }
}
