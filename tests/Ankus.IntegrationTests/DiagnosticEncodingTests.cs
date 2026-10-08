using System.Text;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Proves diagnostic capture and reports convert server text without raising in non-UTF-8 databases.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class DiagnosticEncodingTests(TestContext context)
{
    /// <summary>
    /// SQL_ASCII error text that is not UTF-8 reaches managed code escaped instead of terminating the backend.
    /// </summary>
    [TestMethod]
    public Task SqlAsciiCaptureEscapesInvalidUtf8()
        => InDatabaseAsync("SQL_ASCII", async (connection, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("SELECT datatype.diagnostic_cache($1)", connection);
            command.Parameters.AddWithValue("SELECT pg_catalog.convert_from('\\xe9'::bytea, 'SQL_ASCII')::integer");
            Assert.AreEqual(PostgresErrorCodes.InvalidTextRepresentation, await command.ExecuteScalarAsync(token));
            command.Parameters.Clear();
            command.CommandText = "SELECT datatype.diagnostic_read('message')";
            Assert.AreEqual("invalid input syntax for type integer: \"\\xe9\"", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT pg_catalog.pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
        });

    /// <summary>
    /// A commit callback reports exact LATIN1 text after transaction state ends, without a UTF-8 client conversion.
    /// </summary>
    [TestMethod]
    public Task Latin1CommitCallbackReportsOutsideTransaction()
        => InDatabaseAsync("LATIN1", async (connection, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand("SET client_encoding = LATIN1", connection);
            await command.ExecuteNonQueryAsync(token);
            // Backends log in the database encoding; Windows collects some messages through the UTF-16 Event Log.
            const string Message = "commit report café";
            byte[] latin1 = Encoding.Latin1.GetBytes(Message);
            byte[] utf8 = Encoding.UTF8.GetBytes(Message);
            int start = ReadLog().Length;
            command.CommandText = "BEGIN; SELECT datatype.transaction_callback_register_encoded_report(); COMMIT";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT pg_catalog.pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!Logged(ReadLog().AsSpan(start), latin1, utf8))
            {
                await Task.Delay(10, timeout.Token);
            }
        });

    /// <summary>
    /// Finds a message written in either the database encoding or UTF-8.
    /// </summary>
    private static bool Logged(ReadOnlySpan<byte> log, byte[] database, byte[] utf8)
        => log.IndexOf(database) >= 0 || log.IndexOf(utf8) >= 0;

    /// <summary>
    /// Reads raw server log bytes, which use the database encoding of each reporting backend.
    /// </summary>
    private static byte[] ReadLog()
    {
        // Windows refreshes the file from native and Event Log messages while reading.
        _ = PostgresFixture.Cluster.ReadServerLog();
        using var stream = new FileStream(PostgresFixture.Cluster.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// Runs a case in a fresh database with the selected encoding and the test extension installed.
    /// </summary>
    private async Task InDatabaseAsync(string encoding, Func<NpgsqlConnection, CancellationToken, Task> body)
    {
        CancellationToken token = context.CancellationToken;
        string database = "diagnostic_encoding_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            await using (var setup = new NpgsqlCommand("CREATE SCHEMA datatype; CREATE EXTENSION ankus_test WITH SCHEMA datatype", connection))
            {
                await setup.ExecuteNonQueryAsync(token);
            }

            await body(connection, token);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
