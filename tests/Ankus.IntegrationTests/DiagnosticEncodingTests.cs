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
            // Backends write their native log in the database encoding.
            const string Message = "commit report café";
            byte[] latin1 = Encoding.Latin1.GetBytes(Message);
            int start = ReadLog().Length;
            int serverStart = PostgresFixture.Cluster.ReadServerLog().Length;
            command.CommandText = "BEGIN; SELECT datatype.transaction_callback_register_encoded_report(); COMMIT";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT pg_catalog.pg_backend_pid()";
            Assert.AreEqual(backend, await command.ExecuteScalarAsync(token));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                // Read the merged log first: a report absent from the later native read came from the event log.
                string server = PostgresFixture.Cluster.ReadServerLog()[serverStart..];
                byte[] native = ReadLog()[start..];
                if (native.AsSpan().IndexOf("commit report caf"u8) >= 0)
                {
                    Assert.IsGreaterThanOrEqualTo(0, native.AsSpan().IndexOf(latin1),
                        "Native stderr must carry the report in the database encoding.");
                    break;
                }

                if (server.Contains("commit report caf", StringComparison.Ordinal))
                {
                    // PostgreSQL 13 and 14 send a Windows service's reports to the event log, converting them from
                    // the database encoding to UTF-16; an unconverted UTF-8 report would read "cafÃ©".
                    Assert.Contains(Message, server);
                    break;
                }

                await Task.Delay(10, timeout.Token);
            }
        });

    /// <summary>
    /// Reads raw server log bytes, which use the database encoding of each reporting backend.
    /// </summary>
    /// <remarks>
    /// On Windows, LogFilePath is a decoded UTF-8 snapshot; pg_ctl writes the raw stream beside it, as
    /// PostgresServerLog.NativeFilePath describes.
    /// </remarks>
    private static byte[] ReadLog()
    {
        string path = PostgresFixture.Cluster.LogFilePath + (OperatingSystem.IsWindows() ? ".stderr.log" : string.Empty);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
