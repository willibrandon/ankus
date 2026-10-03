using System.Globalization;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Worker connection names and OIDs preserve database and role identity, privileges and process-ending failures.
    /// </summary>
    [TestMethod]
    public async Task BackgroundWorkerConnectionsPreserveRolesAndFailures()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishPackageConsumerAsync("WorkerConnections", "ankus_worker_connections", WorkerConnectionSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true,
            additionalConfiguration: ["max_worker_processes = 4", "max_parallel_workers = 0", "max_logical_replication_workers = 0", "log_error_verbosity = verbose"]);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        await ExecutePackageGucAsync(connection, """
            CREATE EXTENSION ankus_worker_connections;
            CREATE ROLE worker_reader LOGIN;
            CREATE ROLE worker_no_login NOLOGIN;
            CREATE ROLE worker_denied LOGIN;
            CREATE TABLE worker_visible (value integer);
            INSERT INTO worker_visible VALUES (42);
            GRANT SELECT ON worker_visible TO worker_reader;
            CREATE TABLE worker_secret (value integer);
            INSERT INTO worker_secret VALUES (13);
            """);
        string database = Assert.IsInstanceOfType<string>(await PackageGucScalarAsync(connection, "SELECT current_database()::text"));
        uint databaseOid = Assert.IsInstanceOfType<uint>(await PackageGucScalarAsync(connection, "SELECT oid FROM pg_database WHERE datname = current_database()"));
        uint bootstrapRole = Assert.IsInstanceOfType<uint>(await PackageGucScalarAsync(connection, "SELECT oid FROM pg_roles WHERE rolname = current_user"));
        uint readerRole = Assert.IsInstanceOfType<uint>(await PackageGucScalarAsync(connection, "SELECT oid FROM pg_roles WHERE rolname = 'worker_reader'"));
        uint noLoginRole = Assert.IsInstanceOfType<uint>(await PackageGucScalarAsync(connection, "SELECT oid FROM pg_roles WHERE rolname = 'worker_no_login'"));
        uint deniedRole = Assert.IsInstanceOfType<uint>(await PackageGucScalarAsync(connection, "SELECT oid FROM pg_roles WHERE rolname = 'worker_denied'"));
        string identifier = '"' + database.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
        await ExecutePackageGucAsync(connection, $"REVOKE CONNECT ON DATABASE {identifier} FROM PUBLIC; GRANT CONNECT ON DATABASE {identifier} TO worker_reader");

        foreach (int mode in new[] { 1, 2 })
        {
            string[] reader = await ObserveWorkerConnectionAsync(connection, mode, database, "worker_reader", databaseOid, readerRole, true);
            AssertWorkerConnection(reader, databaseOid, readerRole, superuser: false);
            string[] bootstrap = await ObserveWorkerConnectionAsync(connection, mode, database, null, databaseOid, 0, true);
            AssertWorkerConnection(bootstrap, databaseOid, bootstrapRole, superuser: true);
            string[] noDatabase = await ObserveWorkerConnectionAsync(connection, mode, null, "worker_reader", 0, readerRole, true);
            AssertWorkerConnection(noDatabase, 0, readerRole, superuser: false);
            string[] defaultIdentity = await ObserveWorkerConnectionAsync(connection, mode, null, null, 0, 0, true);
            AssertWorkerConnection(defaultIdentity, 0, bootstrapRole, superuser: true);
        }

        foreach ((int mode, string? selectedDatabase, string? role, uint selectedDatabaseOid, uint roleOid, bool access, string state, string message) in
            new (int, string?, string?, uint, uint, bool, string, string)[]
            {
                (1, "worker_missing_database", "worker_reader", 0, 0, true, "3D000", "database \"worker_missing_database\" does not exist"),
                (1, "", "worker_reader", 0, 0, true, "3D000", "database \"\" does not exist"),
                (2, null, null, uint.MaxValue, readerRole, true, "3D000", "database 4294967295 does not exist"),
                (1, database, "worker_missing_role", 0, 0, true, "28000", "role \"worker_missing_role\" does not exist"),
                (1, database, "", 0, 0, true, "28000", "role \"\" does not exist"),
                (2, null, null, databaseOid, uint.MaxValue, true, "28000", "role with OID 4294967295 does not exist"),
                (1, database, "worker_no_login", 0, 0, true, "28000", "role \"worker_no_login\" is not permitted to log in"),
                (2, null, null, databaseOid, noLoginRole, true, "28000", "role \"worker_no_login\" is not permitted to log in"),
                (1, database, "worker_denied", 0, 0, true, "42501", "permission denied for database"),
                (2, null, null, databaseOid, deniedRole, true, "42501", "permission denied for database"),
                (1, database, "worker_reader", 0, 0, false, "55000", "the worker must request database access and connect only once"),
            })
        {
            string[] failed = await ObserveWorkerConnectionAsync(connection, mode, selectedDatabase, role, selectedDatabaseOid, roleOid, access);
            int workerProcess = int.Parse(failed[0], CultureInfo.InvariantCulture);
            Assert.IsGreaterThan(0, workerProcess);
            Assert.AreNotEqual(connection.ProcessID, workerProcess);
            Assert.AreSequenceEqual(["0", "0", "0", "0", "0", "0", "0", "Stopped"], failed.Skip(1));
            string[] diagnostics = [.. cluster.ReadServerLog().Split('\n').Where(line =>
                line.Contains($"[{workerProcess}]", StringComparison.Ordinal) && line.Contains(message, StringComparison.Ordinal))];
            string diagnostic = Assert.ContainsSingle(diagnostics, $"Worker {workerProcess}, expected {state}: {message}\n{cluster.ReadServerLog()}");
            Assert.Contains((access ? "FATAL:  " : "ERROR:  ") + state + ":", diagnostic);
            Assert.AreEqual(42, await PackageGucScalarAsync(connection, "SELECT 42"));
            string[] recovered = await ObserveWorkerConnectionAsync(connection, 1, database, "worker_reader", 0, 0, true);
            AssertWorkerConnection(recovered, databaseOid, readerRole, superuser: false);
            Assert.AreNotEqual(failed[0], recovered[0]);
        }
    }

    /// <summary>
    /// Sends distinct null, empty, name and OID inputs to one independently observed worker.
    /// </summary>
    private async Task<string[]> ObserveWorkerConnectionAsync(NpgsqlConnection connection, int mode, string? database, string? role,
        uint databaseOid, uint roleOid, bool access)
    {
        await using var command = new NpgsqlCommand("SELECT worker_connect($1, $2, $3, $4, $5, $6)", connection);
        command.Parameters.AddWithValue(mode);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)database ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)role ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Oid, databaseOid);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Oid, roleOid);
        command.Parameters.AddWithValue(access);
        string result = Assert.IsInstanceOfType<string>(await command.ExecuteScalarAsync(context.CancellationToken));
        string[] fields = result.Split('|');
        Assert.HasCount(9, fields);
        Assert.IsGreaterThan(0, int.Parse(fields[0], CultureInfo.InvariantCulture));
        Assert.AreNotEqual(connection.ProcessID.ToString(CultureInfo.InvariantCulture), fields[0]);
        return fields;
    }

    /// <summary>
    /// Checks independent native identity, privilege enforcement, transaction recovery and normal worker exit.
    /// </summary>
    private static void AssertWorkerConnection(string[] fields, uint databaseOid, uint roleOid, bool superuser)
    {
        Assert.AreSequenceEqual(
            ["1", databaseOid.ToString(CultureInfo.InvariantCulture), roleOid.ToString(CultureInfo.InvariantCulture),
                superuser ? "1" : "0", databaseOid == 0 ? "0" : "42", !superuser && databaseOid != 0 ? "1" : "0", "47", "Stopped"],
            fields.Skip(1));
    }

    /// <summary>
    /// Reports connection identity and permission outcomes from each newly initialized PostgreSQL process.
    /// </summary>
    private const string WorkerConnectionSource = """
        using System.Globalization;
        using Ankus;
        using Ankus.Postgres;

        public static class Connections
        {
            private static readonly PgAtomic<int> Process = new("ankus_worker_connections.process");
            private static readonly PgAtomic<int> Connected = new("ankus_worker_connections.connected");
            private static readonly PgAtomic<uint> Database = new("ankus_worker_connections.database");
            private static readonly PgAtomic<uint> Role = new("ankus_worker_connections.role");
            private static readonly PgAtomic<int> Superuser = new("ankus_worker_connections.superuser");
            private static readonly PgAtomic<int> Value = new("ankus_worker_connections.value");
            private static readonly PgAtomic<int> Denied = new("ankus_worker_connections.denied");
            private static readonly PgAtomic<int> Recovered = new("ankus_worker_connections.recovered");

            [PgModuleLoad]
            public static void Load()
            {
                PgSharedMemory.Initialize(Process);
                PgSharedMemory.Initialize(Connected);
                PgSharedMemory.Initialize(Database);
                PgSharedMemory.Initialize(Role);
                PgSharedMemory.Initialize(Superuser);
                PgSharedMemory.Initialize(Value);
                PgSharedMemory.Initialize(Denied);
                PgSharedMemory.Initialize(Recovered);
            }

            [PgBackgroundWorker]
            public static void Connect(nuint mode)
            {
                unsafe
                {
                    string[] fields = PgBackgroundWorker.Extra.Split('|');
                    Process.Exchange(Environment.ProcessId);
                    if (mode == 1)
                    {
                        PgBackgroundWorker.Connect(fields[0] == "\u0001" ? null : fields[0], fields[1] == "\u0001" ? null : fields[1]);
                    }
                    else
                    {
                        PgBackgroundWorker.Connect(uint.Parse(fields[2], CultureInfo.InvariantCulture), uint.Parse(fields[3], CultureInfo.InvariantCulture));
                    }

                    Connected.Exchange(1);
                    Database.Exchange(NativeGlobals.MyDatabaseId);
                    Role.Exchange(NativeMethods.GetUserId());
                    Superuser.Exchange(PgBackgroundWorker.RunTransaction(NativeMethods.superuser) ? 1 : 0);
                    if (Database.Value == 0)
                    {
                        uint inside = PgBackgroundWorker.RunTransaction(NativeMethods.GetUserId);
                        Recovered.Exchange(inside == Role.Value ? 47 : -1);
                        return;
                    }

                    Value.Exchange(PgBackgroundWorker.RunTransaction(() => Spi.ExecuteScalar<int>("SELECT value FROM worker_visible")));
                    if (Superuser.Value == 0)
                    {
                        try
                        {
                            PgBackgroundWorker.RunTransaction(() => Spi.Execute("SELECT value FROM worker_secret"));
                        }
                        catch (PgException exception) when (exception.SqlState == "42501")
                        {
                            Denied.Exchange(1);
                        }
                    }

                    Recovered.Exchange(PgBackgroundWorker.RunTransaction(() => Spi.ExecuteScalar<int>("SELECT 47")));
                }
            }

            [PgFunction]
            public static string WorkerConnect(int mode, string? database, string? role, uint databaseOid, uint roleOid, bool access)
            {
                Process.Exchange(0);
                Connected.Exchange(0);
                Database.Exchange(0);
                Role.Exchange(0);
                Superuser.Exchange(0);
                Value.Exchange(0);
                Denied.Exchange(0);
                Recovered.Exchange(0);
                string extra = string.Create(CultureInfo.InvariantCulture, $"{database ?? "\u0001"}|{role ?? "\u0001"}|{databaseOid}|{roleOid}");
                if (!PgBackgroundWorker.TryStart(new("connection probe", "WorkerConnections", nameof(Connect))
                {
                    DatabaseAccess = access,
                    StartTime = PgBackgroundWorkerStartTime.RecoveryFinished,
                    Argument = checked((nuint)mode),
                    Extra = extra,
                    NotifyProcessId = Environment.ProcessId,
                }, out PgBackgroundWorkerHandle? worker))
                {
                    throw new InvalidOperationException("A connection worker slot was unavailable.");
                }

                using (worker)
                {
                    try
                    {
                        PgBackgroundWorkerStatus stopped = worker.WaitForShutdown();
                        return string.Create(CultureInfo.InvariantCulture,
                            $"{Process.Value}|{Connected.Value}|{Database.Value}|{Role.Value}|{Superuser.Value}|{Value.Value}|{Denied.Value}|{Recovered.Value}|{stopped}");
                    }
                    finally
                    {
                        worker.Terminate();
                    }
                }
            }
        }
        """;
}
