using System.Text;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes the complete pgrx strings example through an independently published Native AOT library.
/// </summary>
/// <param name="context">The current backend test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class StringsExampleTests(TestContext context)
{
    /// <summary>
    /// Returns the exact static literal and appended x through the native text boundary.
    /// </summary>
    /// <param name="input">The original text.</param>
    /// <param name="extra">The appended text.</param>
    /// <param name="expected">The independently specified result.</param>
    [TestMethod]
    [DataRow("", "", "x")]
    [DataRow("hi", " there", "hi therex")]
    [DataRow("", "😀", "😀x")]
    [DataRow("中文", " € 😀", "中文 € 😀x")]
    public Task StringsSamplePreservesStaticAndAppend(string input, string extra, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSamplePreservesStaticAndAppend), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("SELECT strings_example.return_static(), strings_example.append($1,$2)", connection, transaction);
            command.Parameters.AddWithValue(input);
            command.Parameters.AddWithValue(extra);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("This is a static string xxx", reader.GetString(0));
            Assert.AreEqual(expected, reader.GetString(1));
            Assert.AreEqual("text", reader.GetDataTypeName(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Matches Rust's actual whole-string Unicode results rather than PostgreSQL's collation-dependent lower function.
    /// </summary>
    /// <param name="input">The independent Rust oracle input.</param>
    /// <param name="expected">Its actual Rust lowercase output.</param>
    [TestMethod]
    [DataRow("", "")]
    [DataRow("ASCII", "ascii")]
    [DataRow("İ", "i\u0307")]
    [DataRow("ΟΣ", "ος")]
    [DataRow("ΟΣΑ", "οσα")]
    [DataRow("AΣ'\u0301", "aς'\u0301")]
    [DataRow("AΣ'.A", "aσ'.a")]
    [DataRow("ǅΣ", "ǆς")]
    [DataRow("\U00010d50Σ", "\U00010d70ς")]
    [DataRow("\U00010400Σ", "\U00010428ς")]
    [DataRow("Σ", "σ")]
    [DataRow("中文 😀", "中文 😀")]
    public Task StringsSampleLowercaseMatchesRust(string input, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleLowercaseMatchesRust), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("SELECT strings_example.to_lowercase($1), convert_to(strings_example.to_lowercase($1),'UTF8')", connection, transaction);
            command.Parameters.AddWithValue(input);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(expected, reader.GetString(0));
            Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(expected), reader.GetFieldValue<byte[]>(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Native borrowed text uses UTF-8 byte offsets for every scalar width and valid empty boundaries.
    /// </summary>
    /// <param name="start">The inclusive UTF-8 byte offset.</param>
    /// <param name="end">The exclusive UTF-8 byte offset.</param>
    /// <param name="expected">The exact result.</param>
    [TestMethod]
    [DataRow(0, 1, "A")]
    [DataRow(1, 3, "é")]
    [DataRow(3, 6, "中")]
    [DataRow(6, 10, "😀")]
    [DataRow(10, 11, "Z")]
    [DataRow(0, 11, "Aé中😀Z")]
    [DataRow(0, 0, "")]
    [DataRow(3, 3, "")]
    [DataRow(11, 11, "")]
    public Task StringsSampleUsesUtf8ByteOffsets(int start, int end, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleUsesUtf8ByteOffsets), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("SELECT strings_example.substring($1,$2,$3), convert_to(strings_example.substring($1,$2,$3),'UTF8')", connection, transaction);
            command.Parameters.AddWithValue("Aé中😀Z");
            command.Parameters.AddWithValue(start);
            command.Parameters.AddWithValue(end);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(expected, reader.GetString(0));
            Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(expected), reader.GetFieldValue<byte[]>(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Invalid ranges fail without replacing bytes, losing the backend or damaging a later borrowed text call.
    /// </summary>
    /// <param name="start">The inclusive byte offset.</param>
    /// <param name="end">The exclusive byte offset.</param>
    [TestMethod]
    [DataRow(-1, 0)]
    [DataRow(0, -1)]
    [DataRow(3, 1)]
    [DataRow(12, 12)]
    [DataRow(0, 12)]
    [DataRow(2, 3)]
    [DataRow(4, 6)]
    [DataRow(5, 6)]
    [DataRow(7, 10)]
    [DataRow(8, 10)]
    [DataRow(9, 10)]
    [DataRow(1, 2)]
    [DataRow(3, 4)]
    [DataRow(3, 5)]
    [DataRow(6, 7)]
    [DataRow(6, 8)]
    [DataRow(6, 9)]
    [DataRow(2, 2)]
    [DataRow(4, 4)]
    [DataRow(7, 7)]
    public Task StringsSampleSliceFailuresRecoverSameBackend(int start, int end)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleSliceFailuresRecoverSameBackend), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            int backend = connection.ProcessID;
            await transaction.SaveAsync("strings_failure", token);
            await using var command = new NpgsqlCommand("SELECT strings_example.substring($1,$2,$3)", connection, transaction);
            command.Parameters.AddWithValue("Aé中😀Z");
            command.Parameters.AddWithValue(start);
            command.Parameters.AddWithValue(end);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("38000", error.SqlState);
            Assert.IsNotEmpty(error.MessageText);
            await transaction.RollbackAsync("strings_failure", token);
            command.Parameters.Clear();
            command.CommandText = "SELECT pg_backend_pid(), strings_example.substring('Aé中😀Z',6,10)";
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual(backend, reader.GetInt32(0));
            Assert.AreEqual("😀", reader.GetString(1));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Array, set and named-table forms retain the exact Rust terminator pieces and order.
    /// </summary>
    /// <param name="input">The complete scalar string.</param>
    /// <param name="pattern">The exact separator.</param>
    /// <param name="expected">The independently emitted Rust pieces.</param>
    [TestMethod]
    [DataRow("", "", new string[] { "" })]
    [DataRow("", ",", new string[] { })]
    [DataRow("abc", "", new string[] { "", "a", "b", "c" })]
    [DataRow("中😀", "", new string[] { "", "中", "😀" })]
    [DataRow("a,,", ",", new string[] { "a", "" })]
    [DataRow(",a,,b,", ",", new string[] { "", "a", "", "b" })]
    [DataRow("ababa", "aba", new string[] { "", "ba" })]
    [DataRow("a\r\nb\r\n", "\r\n", new string[] { "a", "b" })]
    [DataRow("中😀中", "中", new string[] { "", "😀" })]
    public Task StringsSampleSplitFormsPreserveRustResults(string input, string pattern, string[] expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleSplitFormsPreserveRustResults), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("SELECT strings_example.split($1,$2)", connection, transaction);
            command.Parameters.AddWithValue(input);
            command.Parameters.AddWithValue(pattern);
            Assert.AreSequenceEqual(expected, Assert.IsInstanceOfType<string[]>(await command.ExecuteScalarAsync(token)));

            command.CommandText = "SELECT piece FROM strings_example.split_set($1,$2) AS piece";
            var actual = new List<string>();
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    Assert.IsFalse(reader.IsDBNull(0));
                    actual.Add(reader.GetString(0));
                }
            }

            Assert.AreSequenceEqual(expected, actual);
            command.CommandText = "SELECT i,s,pg_typeof(i)::text,pg_typeof(s)::text FROM strings_example.split_table($1,$2)";
            await using NpgsqlDataReader table = await command.ExecuteReaderAsync(token);
            Assert.AreEqual("i", table.GetName(0));
            Assert.AreEqual("s", table.GetName(1));
            int index = 0;
            while (await table.ReadAsync(token))
            {
                Assert.IsLessThan(expected.Length, index);
                Assert.AreEqual(index, table.GetInt32(0));
                Assert.AreEqual(expected[index], table.GetString(1));
                Assert.AreEqual("integer", table.GetString(2));
                Assert.AreEqual("text", table.GetString(3));
                index++;
            }

            Assert.AreEqual(expected.Length, index);
        }, context.CancellationToken);

    /// <summary>
    /// SQL NULL stays distinct from empty scalar text, empty arrays and an empty-pattern set's leading empty row.
    /// </summary>
    [TestMethod]
    public Task StringsSamplePreservesStrictNullAndEmpty()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSamplePreservesStrictNullAndEmpty), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            await using var command = new NpgsqlCommand("""
                SELECT strings_example.to_lowercase(NULL::text), strings_example.substring(NULL::text,0,0),
                    strings_example.append(NULL::text,''), strings_example.append('',NULL::text),
                    strings_example.split(NULL::text,','), strings_example.split('',NULL::text),
                    strings_example.split('',','), strings_example.split('',''),
                    (SELECT count(*) FROM strings_example.split_set(NULL::text,',')),
                    (SELECT count(*) FROM strings_example.split_table(NULL::text,',')),
                    (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                        WHERE n.nspname='strings_example' AND p.proisstrict)
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            for (int index = 0; index < 6; index++)
            {
                Assert.IsTrue(reader.IsDBNull(index));
            }

            Assert.IsEmpty(reader.GetFieldValue<string[]>(6));
            Assert.AreSequenceEqual<string>([""], reader.GetFieldValue<string[]>(7));
            Assert.AreEqual(0L, reader.GetInt64(8));
            Assert.AreEqual(0L, reader.GetInt64(9));
            Assert.AreEqual(7L, reader.GetInt64(10));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// A substring result remains owned after externally stored source text is changed and later calls reuse the callback frame.
    /// </summary>
    [TestMethod]
    public Task StringsSampleOwnsToastedSubstringResult()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleOwnsToastedSubstringResult), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            string original = string.Concat(Enumerable.Repeat("Aé中😀Z", 8192));
            await using var setup = new NpgsqlCommand("""
                CREATE TABLE strings_source (value text);
                ALTER TABLE strings_source ALTER COLUMN value SET STORAGE EXTERNAL
                """, connection, transaction);
            await setup.ExecuteNonQueryAsync(token);
            await using var insert = new NpgsqlCommand("INSERT INTO strings_source VALUES ($1)", connection, transaction);
            insert.Parameters.AddWithValue(original);
            await insert.ExecuteNonQueryAsync(token);
            await using var materialize = new NpgsqlCommand("""
                CREATE TABLE strings_result AS SELECT strings_example.substring(value,6,10) AS value FROM strings_source;
                UPDATE strings_source SET value='changed'
                """, connection, transaction);
            await materialize.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand("""
                SELECT r.value, s.value, strings_example.append(s.value,''), strings_example.return_static()
                FROM strings_result r CROSS JOIN strings_source s
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("😀", reader.GetString(0));
            Assert.AreEqual("changed", reader.GetString(1));
            Assert.AreEqual("changedx", reader.GetString(2));
            Assert.AreEqual("This is a static string xxx", reader.GetString(3));
            Assert.IsFalse(await reader.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Early set/table completion and portal disposal preserve later iteration and the same backend.
    /// </summary>
    [TestMethod]
    public Task StringsSampleSetEarlyDisposalRecovers()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(StringsSampleSetEarlyDisposalRecovers), async (connection, transaction, token) =>
        {
            await InstallAsync(connection, transaction, token);
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("SELECT i,s FROM strings_example.split_table(',a,,b,',',') LIMIT 2", connection, transaction);
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual(0, reader.GetInt32(0));
                Assert.AreEqual(string.Empty, reader.GetString(1));
            }

            command.CommandText = "SELECT piece FROM strings_example.split_set('a,b,c',',') AS piece LIMIT 1";
            Assert.AreEqual("a", Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(token)));
            command.CommandText = "SELECT pg_backend_pid(), array_agg(s ORDER BY i) FROM strings_example.split_table(',a,,b,',',')";
            await using NpgsqlDataReader complete = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await complete.ReadAsync(token));
            Assert.AreEqual(backend, complete.GetInt32(0));
            Assert.AreSequenceEqual<string>(["", "a", "", "b"], complete.GetFieldValue<string[]>(1));
            Assert.IsFalse(await complete.ReadAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Installs the sample in a transaction-isolated schema after ordinary SDK publication.
    /// </summary>
    /// <param name="connection">The isolated native backend.</param>
    /// <param name="transaction">The test's rollback scope.</param>
    /// <param name="token">Cancels installation.</param>
    /// <returns>The installation completion.</returns>
    private static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("CREATE SCHEMA strings_example; CREATE EXTENSION ankus_strings WITH SCHEMA strings_example", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
