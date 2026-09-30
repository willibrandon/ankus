using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Selected native declarations execute in PostgreSQL, attach to an existing extension and roll back together on failure.
    /// </summary>
    /// <param name="fixedSchema">Whether the control file overrides the caller's search path.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SchemaSelectionPreservesCatalogOwnershipAndRecovery(bool fixedSchema)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = await CreateControlProjectAsync(directory, token, "ankus_selection_probe");
        await File.WriteAllTextAsync(Path.Combine(directory, "Functions.cs"), SchemaSelectionSource, token);
        await File.WriteAllTextAsync(Path.Combine(directory, "author settings.control"), fixedSchema ? "schema = 'Case Schema'" : "comment = 'selection'", token);
        string output = CreateDirectory();
        PublishedExtension publication = await PublishControlProbeAsync(project, output, token);
        string library = Path.Combine(output, publication.Library);
        ExtensionSchema schema = ExtensionSchema.Read(library);
        Assert.AreEqual(fixedSchema ? "Case Schema" : null, schema.DefaultSchema);
        Assert.IsNotNull(schema.Graph);
        Assert.AreEqual(schema.Sql, schema.Graph.Sql);

        string sqlPath = Path.Combine(directory, "selected.sql");
        string graphPath = Path.Combine(directory, "selected.dot");
        string[] names = ["read_value", "make_value", "Choice.ordering", "Choice.hashing", "Total", "CountRows", "CountArguments", "OrderedTotal", "manual.raw_function(integer)"];
        ProcessResult selected = await InvokeAsync(["schema", "--from", library, "--output", sqlPath, "--dot", graphPath, .. names], token);
        Assert.AreEqual(0, selected.ExitCode, selected.StandardError);
        Assert.IsEmpty(selected.StandardOutput);
        Assert.IsEmpty(selected.StandardError);
        string sql = await File.ReadAllTextAsync(sqlPath, token);
        Assert.DoesNotContain("unrelated", sql);
        Assert.DoesNotContain("MODULE_PATHNAME", sql);
        Assert.Contains("$libdir/" + publication.Library, sql);
        Assert.StartsWith("BEGIN;\n", sql);
        Assert.EndsWith("COMMIT;\n", sql);
        string graph = await File.ReadAllTextAsync(graphPath, token);
        Assert.AreEqual(schema.Graph.ToGraphviz(), graph);
        Assert.Contains(" -> ", graph);
        Assert.Contains("unrelated", graph);
        Assert.AreEqual(sql, schema.Select(names).Sql);

        // Keep the extension itself installed while replaying only its selected objects.
        await File.WriteAllTextAsync(Path.Combine(output, "extension", publication.Sql), "-- Empty initial extension for selection.\n", token);
        await using PostgresTestInstallation owner = await PostgresTestInstallation.StageAsync(s_installation, Path.Combine(directory, "server"), token);
        owner.InstallExtensionFiles(output);
        File.Copy(library, Path.Combine(owner.Installation.LibraryDirectory, publication.Library));
        await using PostgresTestCluster cluster = await PostgresTestCluster.StartAsync(new PostgresTestClusterOptions
        {
            Installation = owner.Installation,
            DataDirectoryBase = Path.Combine(s_root, "pgdata"),
            LogDirectory = Path.Combine(IntegrationEnvironment.RepositoryRoot, "artifacts", "test-logs"),
        }, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int backend = connection.ProcessID;
        await ExecuteSqlPackageAsync(connection, "CREATE EXTENSION ankus_selection_probe; SET search_path TO public");
        string prefix = fixedSchema ? "\"Case Schema\"." : "public.";
        string failedSql = sql.Replace("\nCOMMIT;", "\nSELECT 1 / 0;\nCOMMIT;", StringComparison.Ordinal);
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecuteSqlPackageAsync(connection, failedSql));
        Assert.AreEqual("22012", error.SqlState, error.MessageText);
        await ExecuteSqlPackageAsync(connection, "ROLLBACK");
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_proc WHERE proname = 'read_value'"));
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_type WHERE typname IN ('value', 'choice')"));
        Assert.AreEqual(1L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_extension WHERE extname = 'ankus_selection_probe'"));

        await ExecuteSqlPackageAsync(connection, sql);
        Assert.AreEqual(42, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}read_value({prefix}make_value(42))"));
        Assert.AreEqual(6, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}total(value) FROM (VALUES(1),(2),(3)) input(value)"));
        Assert.AreEqual(3, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}count_rows(*) FROM (VALUES(1),(2),(3)) input(value)"));
        Assert.AreEqual(6, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}count_arguments(value, value) FROM (VALUES(1),(2),(3)) input(value)"));
        Assert.AreEqual(16, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}ordered_total(10) WITHIN GROUP (ORDER BY value) FROM (VALUES(1),(2),(3)) input(value)"));
        Assert.AreEqual(42, await SqlPackageScalarAsync<int>(connection, "SELECT manual.raw_function(41)"));
        Assert.IsTrue(await SqlPackageScalarAsync<bool>(connection, $"SELECT 'A'::{prefix}choice OPERATOR({prefix}=) 'A'::{prefix}choice"));
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_proc WHERE proname='unrelated'"));
        Assert.AreEqual(9L, await SqlPackageScalarAsync<long>(connection, $$"""
            SELECT count(*) FROM pg_depend dependency
            JOIN pg_extension extension ON extension.oid = dependency.refobjid AND dependency.refclassid = 'pg_extension'::regclass
            WHERE extension.extname = 'ankus_selection_probe' AND dependency.deptype = 'e' AND (
              (dependency.classid = 'pg_proc'::regclass AND dependency.objid IN (
                '{{prefix}}read_value({{prefix}}value)'::regprocedure, '{{prefix}}make_value(integer)'::regprocedure,
                '{{prefix}}total(integer)'::regprocedure, 'manual.raw_function(integer)'::regprocedure)) OR
              (dependency.classid = 'pg_type'::regclass AND dependency.objid IN ('{{prefix}}value'::regtype, '{{prefix}}choice'::regtype)) OR
              (dependency.classid = 'pg_opclass'::regclass AND dependency.objid IN (SELECT oid FROM pg_opclass WHERE opcname IN ('choice_btree_ops','choice_hash_ops'))) OR
              (dependency.classid = 'pg_cast'::regclass AND dependency.objid IN (SELECT oid FROM pg_cast WHERE castsource = '{{prefix}}value'::regtype AND casttarget='integer'::regtype))
            )
            """));
        Assert.AreEqual(backend, connection.ProcessID);
        await ExecuteSqlPackageAsync(connection, "DROP EXTENSION ankus_selection_probe");
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_proc WHERE proname IN ('read_value','make_value','total','count_rows','count_arguments','ordered_total','raw_function')"));
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_type WHERE typname IN ('value','choice')"));
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, "SELECT count(*) FROM pg_namespace WHERE nspname = 'manual'"));

        ProcessResult detached = await InvokeAsync(["schema", "--from", library, "--no-alter-extension", .. names], token);
        Assert.AreEqual(0, detached.ExitCode, detached.StandardError);
        Assert.IsEmpty(detached.StandardError);
        Assert.DoesNotContain("ALTER EXTENSION", detached.StandardOutput);
        Assert.DoesNotContain("BEGIN;", detached.StandardOutput);
        await ExecuteSqlPackageAsync(connection, detached.StandardOutput);
        Assert.AreEqual(42, await SqlPackageScalarAsync<int>(connection, $"SELECT {prefix}read_value({prefix}make_value(42))"));
        Assert.AreEqual(0L, await SqlPackageScalarAsync<long>(connection, $$"""
            SELECT count(*) FROM pg_depend WHERE classid='pg_proc'::regclass
              AND objid='{{prefix}}read_value({{prefix}}value)'::regprocedure AND deptype='e'
            """));
        Assert.AreEqual(backend, connection.ProcessID);
    }

    /// <summary>
    /// Invalid names and colliding destinations preserve both previous schema outputs and the native library.
    /// </summary>
    /// <param name="failure">The invalid output or selection partition.</param>
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("same outputs")]
    [DataRow("graph is library")]
    [DataRow("graph directory")]
    [DataRow("no selection")]
    public async Task InvalidSchemaSelectionPreservesOutputs(string failure)
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        PublishedExtension publication = PublishedExtension.Read(s_published);
        string library = Path.Combine(directory, publication.Library);
        File.Copy(Path.Combine(s_published, publication.Library), library);
        byte[] original = await File.ReadAllBytesAsync(library, token);
        string sql = Path.Combine(directory, "selected.sql");
        string dot = Path.Combine(directory, "selected.dot");
        await File.WriteAllTextAsync(sql, "old SQL", token);
        await File.WriteAllTextAsync(dot, "old graph", token);
        string graphOutput = failure switch
        {
            "same outputs" => sql,
            "graph is library" => library,
            "graph directory" => directory,
            _ => dot,
        };
        string[] selection = failure == "no selection" ? ["--no-alter-extension"] : [failure == "unknown" ? "missing" : "add"];
        ProcessResult result = await InvokeAsync(["schema", "--from", library, "--output", sql, "--dot", graphOutput, .. selection], token);
        Assert.AreEqual(1, result.ExitCode);
        Assert.IsEmpty(result.StandardOutput);
        Assert.Contains(failure switch
        {
            "unknown" => "Unknown schema item",
            "same outputs" or "graph is library" => "Graphviz output must differ",
            "no selection" => "requires at least one schema item",
            _ => directory,
        }, result.StandardError);
        Assert.AreEqual("old SQL", await File.ReadAllTextAsync(sql, token));
        Assert.AreEqual("old graph", await File.ReadAllTextAsync(dot, token));
        Assert.AreSequenceEqual(original, await File.ReadAllBytesAsync(library, token));
        Assert.AreSequenceEqual(new[] { library, sql, dot }.Order(StringComparer.Ordinal), Directory.GetFiles(directory).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Exercises typed base types, enums, derived index support, casts, aggregates and custom function declarations.
    /// </summary>
    private const string SchemaSelectionSource = """
        using Ankus;
        [assembly: PgSql("manual-function", "CREATE FUNCTION manual.raw_function(value integer) RETURNS integer LANGUAGE sql AS $$SELECT value + 1$$;", Requires = new[] { "manual-schema" })]
        [assembly: PgSqlFunctionProvider("manual-function", "manual.raw_function(integer)")]
        [PgSchema("manual", Id = "manual-schema")]
        public static class Manual;
        [PgType]
        public readonly record struct Value(int Number);
        [PgEnum, PgEquality, PgOrdering, PgHashing]
        public enum Choice { A, B }
        public static class Functions
        {
            [PgFunction, PgCast]
            public static int ReadValue(Value value) => value.Number;
            [PgFunction]
            public static Value MakeValue(int value) => new(value);
            [PgFunction]
            public static int Unrelated() => 99;
        }
        [PgAggregate(InitialCondition = "0")]
        public static class Total
        {
            public static int Transition(int state, int value) => checked(state + value);
        }
        [PgAggregate(InitialCondition = "0")]
        public static class CountRows
        {
            public static int Transition(int state) => checked(state + 1);
        }
        [PgAggregate(InitialCondition = "0")]
        public static class CountArguments
        {
            public static int Transition(int state, params int[] values) => checked(state + values.Length);
        }
        [PgAggregate(Kind = PgAggregateKind.OrderedSet, InitialCondition = "0")]
        public static class OrderedTotal
        {
            public static int Transition(int state, int value) => checked(state + value);
            public static int Final(int state, int direct) => checked(state + direct);
        }
        """;
}
