using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies native name encoding independently of the UTF-8 transport and managed UTF-16 representation.
/// </summary>
/// <param name="context">The current test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class NamespaceLookupEncodingTests(TestContext context)
{
    /// <summary>
    /// Names at the server's byte boundary retain identity, while unrepresentable managed names raise and recover.
    /// </summary>
    [TestMethod]
    public async Task Latin1NamesPreserveIdentityAndRejectLoss()
    {
        CancellationToken token = context.CancellationToken;
        string database = "lookup_latin1_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = await PostgresFixture.Cluster.OpenConnectionAsync(token);
        await using (var create = new NpgsqlCommand(
            $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C'", administrator))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(administrator.ConnectionString) { Database = database, Pooling = false };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            int backend = connection.ProcessID;
            string schema = new('é', 63);
            await using var command = new NpgsqlCommand($"""
                CREATE EXTENSION ankus_test;
                CREATE SCHEMA "{schema}";
                CREATE DOMAIN "{schema}"."Café" AS integer;
                CREATE OPERATOR "{schema}".#+ (FUNCTION=pg_catalog.int4pl, LEFTARG=integer, RIGHTARG=integer);
                SELECT concat_ws('|',
                  catalog_lookup.type_oid('"{schema}"."Café"')='"{schema}"."Café"'::regtype::oid,
                  catalog_lookup.operator_oid(ARRAY['{schema}','#+'],23,23) =
                    (SELECT oid FROM pg_operator WHERE oprnamespace='"{schema}"'::regnamespace AND oprname='#+'))
                """, connection);
            Assert.AreEqual("t|t", await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT catalog_lookup.unrepresentable_lookup(false) || '/' || catalog_lookup.unrepresentable_lookup(true)";
            Assert.AreEqual("22P05|True|42/22P05|True|42", await command.ExecuteScalarAsync(token));
            command.CommandText = $"SELECT catalog_lookup.type_oid('\"{schema}\".\"Café\"')='\"{schema}\".\"Café\"'::regtype::oid";
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            Assert.AreEqual(backend, connection.ProcessID);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", administrator);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
