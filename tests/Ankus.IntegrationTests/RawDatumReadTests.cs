using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Distinguishes reads of stored domain data from assignment checks in the actual PostgreSQL backend.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class RawDatumReadTests(TestContext context)
{
    private const string NumberCheck = "ALTER DOMAIN raw_read_domains.number ADD CONSTRAINT changed CHECK(VALUE<0) NOT VALID";
    private const string VectorCheck = "ALTER DOMAIN raw_read_domains.vector ADD CONSTRAINT changed CHECK(VALUE IS NULL) NOT VALID";
    private const string JoinedNull = "SELECT r.value FROM (VALUES(1)) l(id) LEFT JOIN raw_read_values r ON false";

    /// <summary>
    /// Every raw access opcode and nested mapped read avoids additional CHECK effects on existing storage.
    /// </summary>
    /// <param name="operation">Built-in, mapped, format, copy, raw array, mapped array, or raw array-cell reads.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task StoredDomainAccessDoesNotRepeatCheckEffects(int operation)
    {
        await using NpgsqlConnection connection = await Open();
        await Setup(connection);
        try
        {
            await Execute(connection, """
                ALTER DOMAIN raw_read_domains.number ADD CONSTRAINT counted CHECK(raw_read_domains.count_number(VALUE)) NOT VALID;
                ALTER DOMAIN raw_read_domains.vector ADD CONSTRAINT counted CHECK(raw_read_domains.count_vector(VALUE)) NOT VALID;
                """);
            (string sql, string expected) = await Present(connection, operation);
            Assert.AreEqual(expected, await Probe(connection, sql, operation));
            await AssertNoChecks(connection);
            Assert.AreEqual(operation == 1 ? 1 : operation == 5 ? 2 : 0,
                await Scalar<int>(connection, "SELECT raw_read_domains.reads()"));

            string nullSql = operation < 4 ? "SELECT value FROM raw_read_values WHERE id=2"
                : operation == 4 ? "SELECT value FROM raw_read_vectors WHERE id=2"
                : "SELECT NULL::raw_read_domains.number[]";
            uint type = await TypeOid(connection, operation < 4 ? "number" : operation == 4 ? "vector" : "number[]");
            string result = operation == 3 ? $"{type}|True|0" : "NULL";
            Assert.AreEqual($"{type}|True|{result}|alive", await Probe(connection, nullSql, operation));
            await AssertNoChecks(connection);
            if (operation >= 5)
            {
                uint number = await TypeOid(connection, "number");
                string cells = operation == 5 ? $"{number}|1|1|NULL" : "NULL";
                Assert.AreEqual($"{type}|False|{cells}|alive", await Probe(connection,
                    "SELECT array_agg(value) FROM raw_read_values WHERE id=2", operation));
                await AssertNoChecks(connection);
            }

            Assert.AreSequenceEqual(Enumerable.Range(1, 4096), await Scalar<int[]>(connection,
                "SELECT raw_read_domains.copy_vector(NULL)"));
            Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM raw_read_vector_source"));
            await AssertNoChecks(connection);
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await Clean(connection);
        }
    }

    /// <summary>
    /// A value captured before a rejecting NOT VALID constraint remains readable without becoming an assignment.
    /// </summary>
    /// <param name="operation">The independently selected raw or mapped access.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task StoredDomainValuesRemainReadableAfterNotValidConstraint(int operation)
    {
        await using NpgsqlConnection connection = await Open();
        await Setup(connection);
        try
        {
            (string sql, string expected) = await Present(connection, operation);
            Assert.AreEqual(expected, await Probe(connection, sql, operation, operation == 4 ? VectorCheck : NumberCheck));
            Assert.AreEqual(42, await Scalar<int>(connection, "SELECT value::integer FROM raw_read_values WHERE id=1"));
            Assert.AreEqual("[0:2]={7,NULL,11}", await Scalar<string>(connection,
                "SELECT value::integer[]::text FROM raw_read_vectors WHERE id=1"));
            await Clean(connection);
            Assert.AreSequenceEqual(Enumerable.Range(1, 4096), await Scalar<int[]>(connection,
                $"SELECT raw_read_domains.copy_vector({Literal(VectorCheck)})"));
            Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM raw_read_vector_source"));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await Clean(connection);
        }
    }

    /// <summary>
    /// A genuine outer-join NULL keeps its NOT NULL domain identity without undergoing a new coercion.
    /// </summary>
    /// <param name="operation">Built-in, mapped, output text, or native copy access.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task OuterJoinDomainNullIsReadableWithoutReassignment(int operation)
    {
        await using NpgsqlConnection connection = await Open();
        await Setup(connection);
        try
        {
            await Execute(connection, "DELETE FROM raw_read_values WHERE value IS NULL; ALTER DOMAIN raw_read_domains.number SET NOT NULL");
            uint type = await TypeOid(connection, "number");
            string result = operation == 3 ? $"{type}|True|0" : "NULL";
            Assert.AreEqual($"{type}|True|{result}|alive", await Probe(connection, JoinedNull, operation));
            Assert.AreEqual(0, await Scalar<int>(connection, "SELECT raw_read_domains.reads()"));
            Assert.IsTrue(await Scalar<bool>(connection, $"SELECT value IS NULL AND pg_typeof(value)::oid={type}::oid FROM ({JoinedNull}) input"));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await Clean(connection);
        }
    }

    /// <summary>
    /// Raw, mapped and array assignments still enforce current CHECK and NOT NULL constraints before target execution.
    /// </summary>
    [TestMethod]
    public async Task RawAndMappedAssignmentsStillValidateCurrentDomains()
    {
        await using NpgsqlConnection connection = await Open();
        await Setup(connection);
        try
        {
            await Execute(connection, NumberCheck);
            const string positive = "SELECT value FROM raw_read_values WHERE id=1";
            const string negative = "SELECT value FROM raw_read_values WHERE id=3";
            const string check = "value for domain raw_read_domains.number violates check constraint \"changed\"";
            for (int route = 0; route < 3; route++)
            {
                Assert.AreEqual($"23514|{check}|source alive", await Bind(connection, positive, route, 42, 0));
                Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM raw_read_target"));
            }

            foreach (string sql in new[]
            {
                $"SELECT raw_read_domains.raw_output({Literal(positive)})",
                "SELECT raw_read_domains.mapped_output(42,0)",
                "SELECT raw_read_domains.array_output(42,0)",
            })
            {
                await AssertFailure(connection, sql, "23514", check);
            }

            await Execute(connection, "ALTER DOMAIN raw_read_domains.number ADD CONSTRAINT counted CHECK(raw_read_domains.count_number(VALUE)) NOT VALID");
            for (int route = 0; route < 3; route++)
            {
                await Execute(connection, "ALTER SEQUENCE raw_read_checks RESTART WITH 1; ALTER SEQUENCE raw_read_target RESTART WITH 1");
                Assert.AreEqual("assigned", await Bind(connection, negative, route, -7, 0));
                Assert.IsTrue(await Scalar<bool>(connection, "SELECT is_called FROM raw_read_checks"));
                Assert.AreEqual(1L, await Scalar<long>(connection, "SELECT last_value FROM raw_read_target"));
            }

            Assert.AreEqual(-7, await Scalar<int>(connection, "SELECT raw_read_domains.mapped_output(-7,0)::integer"));
            Assert.AreSequenceEqual<int>([-7, -11], await Scalar<int[]>(connection,
                "SELECT raw_read_domains.array_output(-11,0)::integer[]"));
            await Execute(connection, """
                ALTER DOMAIN raw_read_domains.number DROP CONSTRAINT changed;
                DELETE FROM raw_read_values WHERE value IS NULL;
                ALTER DOMAIN raw_read_domains.number SET NOT NULL;
                ALTER SEQUENCE raw_read_target RESTART WITH 1;
                """);
            const string notNull = "domain raw_read_domains.number does not allow null values";
            Assert.AreEqual($"23502|{notNull}|source alive", await Bind(connection, JoinedNull, 0, 0, 0));
            for (int mode = 1; mode <= 2; mode++)
            {
                for (int route = 1; route <= 2; route++)
                {
                    Assert.AreEqual($"23502|{notNull}|source alive", await Bind(connection, JoinedNull, route, 0, mode));
                }

                await AssertFailure(connection, $"SELECT raw_read_domains.mapped_output(0,{mode})", "23502", notNull);
                await AssertFailure(connection, $"SELECT raw_read_domains.array_output(0,{mode})", "23502", notNull);
            }

            await AssertFailure(connection, $"SELECT raw_read_domains.raw_output({Literal(JoinedNull)})", "23502", notNull);
            Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM raw_read_target"));
            Assert.AreEqual(42, await Scalar<int>(connection, "SELECT raw_read_domains.mapped_output(42,0)::integer"));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
        }
        finally
        {
            await Clean(connection);
        }
    }

    /// <summary>
    /// Bypassing domain coercion does not bypass catalog existence for a typed NULL with a deleted type.
    /// </summary>
    /// <param name="operation">Read, format, or copy through publicly reachable typed-NULL opcodes.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task DroppedTypedNullStillRequiresALiveCatalogType(int operation)
    {
        await using NpgsqlConnection connection = await Open();
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Scalar<string>(connection,
            $"SELECT raw_read_domains.dropped_null({operation})"));
        uint oid = await Scalar<uint>(connection, "SELECT raw_read_domains.dropped_oid()");
        Assert.AreEqual("42704", error.SqlState);
        Assert.AreEqual($"Parameter type OID {oid} does not exist", error.MessageText);
        Assert.AreEqual(73, await Scalar<int>(connection, "SELECT 73"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Creates source storage before test constraints and avoids persistent domain-container columns.
    /// </summary>
    private Task Setup(NpgsqlConnection connection) => Execute(connection, """
        CREATE TEMP SEQUENCE raw_read_checks;
        CREATE TEMP SEQUENCE raw_vector_checks;
        CREATE TEMP SEQUENCE raw_read_target;
        CREATE TEMP TABLE raw_read_values(id integer, value raw_read_domains.number);
        INSERT INTO raw_read_values VALUES(1,42),(2,NULL),(3,-7);
        CREATE TEMP TABLE raw_read_vectors(id integer, value raw_read_domains.vector);
        INSERT INTO raw_read_vectors VALUES(1,'[0:2]={7,NULL,11}'),(2,NULL);
        CREATE TEMP TABLE raw_read_vector_source(value raw_read_domains.vector);
        ALTER TABLE raw_read_vector_source ALTER COLUMN value SET STORAGE EXTERNAL;
        INSERT INTO raw_read_vector_source SELECT ARRAY(SELECT generate_series(1,4096))::raw_read_domains.vector;
        """);

    /// <summary>
    /// Removes only this class's dedicated domain changes, including after the expected initial regression failure.
    /// </summary>
    private Task Clean(NpgsqlConnection connection) => Execute(connection, """
        ALTER DOMAIN raw_read_domains.number DROP CONSTRAINT IF EXISTS changed;
        ALTER DOMAIN raw_read_domains.number DROP CONSTRAINT IF EXISTS counted;
        ALTER DOMAIN raw_read_domains.number DROP NOT NULL;
        ALTER DOMAIN raw_read_domains.vector DROP CONSTRAINT IF EXISTS changed;
        ALTER DOMAIN raw_read_domains.vector DROP CONSTRAINT IF EXISTS counted;
        """);

    /// <summary>
    /// Supplies independent expected scalar/shape/cell values for each selected operation.
    /// </summary>
    private async Task<(string Sql, string Expected)> Present(NpgsqlConnection connection, int operation)
    {
        uint number = await TypeOid(connection, "number");
        if (operation < 4)
        {
            return ("SELECT value FROM raw_read_values WHERE id=1", $"{number}|False|{(operation == 3 ? $"{number}|False|42" : "42")}|alive");
        }

        if (operation == 4)
        {
            uint vector = await TypeOid(connection, "vector");
            return ("SELECT value FROM raw_read_vectors WHERE id=1", $"{vector}|False|23|1|0|7,NULL,11|alive");
        }

        uint array = await TypeOid(connection, "number[]");
        return ("SELECT array_agg(value ORDER BY id) FROM raw_read_values",
            $"{array}|False|{(operation == 5 ? $"{number}|1|1|42,NULL,-7" : "42,NULL,-7")}|alive");
    }

    /// <summary>
    /// Resolves the actual dedicated fixture identity independently of generated mapping code.
    /// </summary>
    private Task<uint> TypeOid(NpgsqlConnection connection, string name)
        => Scalar<uint>(connection, $"SELECT {Literal("raw_read_domains." + name)}::regtype::oid");

    /// <summary>
    /// Requires no nontransactional CHECK side effects, even if a native subtransaction rolled back.
    /// </summary>
    private async Task AssertNoChecks(NpgsqlConnection connection)
    {
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM raw_read_checks"));
        Assert.IsFalse(await Scalar<bool>(connection, "SELECT is_called FROM raw_vector_checks"));
    }

    /// <summary>
    /// Runs one selected operation without passing the stored datum through a new SQL parameter coercion.
    /// </summary>
    private Task<string> Probe(NpgsqlConnection connection, string sql, int operation, string? mutation = null)
        => Scalar<string>(connection, $"SELECT raw_read_domains.probe({Literal(sql)},{operation},{(mutation is null ? "NULL" : Literal(mutation))})");

    /// <summary>
    /// Captures assignment errors inside the managed callback while retaining its source datum.
    /// </summary>
    private Task<string> Bind(NpgsqlConnection connection, string sql, int route, int value, int nullMode)
        => Scalar<string>(connection, $"SELECT raw_read_domains.bind({Literal(sql)},{route},{value},{nullMode})");

    /// <summary>
    /// Checks an actual generated output error and its complete native message.
    /// </summary>
    private async Task AssertFailure(NpgsqlConnection connection, string sql, string state, string message)
    {
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, sql));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
    }

    /// <summary>
    /// Opens a fresh backend while serializing only this class's dedicated domain DDL.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Quotes controlled SQL as a literal without executing it during fixture argument evaluation.
    /// </summary>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Reads the requested type directly, preserving nullable array element contracts.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
        return await reader.GetFieldValueAsync<T>(0, context.CancellationToken);
    }

    /// <summary>
    /// Completes every setup, cleanup and expected assignment statement result.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
