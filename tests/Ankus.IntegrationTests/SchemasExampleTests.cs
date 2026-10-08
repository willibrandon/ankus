using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes pgrx's schemas example through its independently published extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class SchemasExampleTests(TestContext context)
{
    /// <summary>
    /// Mirrors each pgrx test: the default, extension-owned, pg_catalog and public objects resolve as declared.
    /// </summary>
    [TestMethod]
    public Task SchemasSampleResolvesObjectsInEachSchema()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SchemasSampleResolvesObjectsInEachSchema), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, "CREATE EXTENSION ankus_schemas", token);
            Assert.AreEqual("Hello from the schema where you installed this extension",
                await Scalar<string>(connection, transaction, "SELECT hello_default_schema()", token));
            Assert.AreEqual("""{"Value":"test"}""",
                await Scalar<string>(connection, transaction, """SELECT '{"Value":"test"}'::MyType::text""", token));
            Assert.AreEqual("test",
                await Scalar<string>(connection, transaction, """SELECT '{"Value":"test"}'::MyType::text::json->>'Value'""", token));
            Assert.AreEqual("Hello from some_schema",
                await Scalar<string>(connection, transaction, "SELECT some_schema.hello_some_schema()", token));
            Assert.AreEqual("""{"Value":"test"}""",
                await Scalar<string>(connection, transaction, """SELECT '{"Value":"test"}'::MyPgCatalogType::text""", token));
            Assert.AreEqual("Hello from the public schema", await Scalar<string>(connection, transaction, "SELECT hello_public()", token));

            await transaction.SaveAsync("unqualified_some_schema_type", token);
            PostgresException missing = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, """SELECT '{"Value":"test"}'::MySomeSchemaType""", token));
            Assert.AreEqual("42704", missing.SqlState);
            Assert.AreEqual("type \"mysomeschematype\" does not exist", missing.MessageText);
            await transaction.RollbackAsync("unqualified_some_schema_type", token);

            await Execute(connection, transaction, "SET LOCAL search_path TO some_schema,public", token);
            Assert.AreEqual("""{"Value":"test"}""",
                await Scalar<string>(connection, transaction, """SELECT '{"Value":"test"}'::MySomeSchemaType::text""", token));
            Assert.AreEqual("Hello from some_schema", await Scalar<string>(connection, transaction, "SELECT hello_some_schema()", token));

            await Execute(connection, transaction, "SET LOCAL search_path = ''", token);
            Assert.AreEqual("""{"Value":"pg_catalog"}""",
                await Scalar<string>(connection, transaction, """SELECT '{"Value":"pg_catalog"}'::MyPgCatalogType::text""", token));
            Assert.AreEqual("""{"Value":"test"}""",
                await Scalar<string>(connection, transaction, "SELECT (public.return_vec_of_customtype())[1]::text", token));
            Assert.AreEqual("public.mytype[]",
                await Scalar<string>(connection, transaction, "SELECT pg_typeof(public.return_vec_of_customtype())::text", token));
            Assert.AreEqual(1, await Scalar<int>(connection, transaction,
                "SELECT cardinality(public.return_vec_of_customtype())", token));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Separates the installation schema from fixed schemas and preserves ownership through removal and reinstallation.
    /// </summary>
    [TestMethod]
    public Task SchemasSampleSeparatesInstallationAndFixedSchemas()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SchemasSampleSeparatesInstallationAndFixedSchemas), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE SCHEMA schemas_target;
                CREATE SCHEMA schemas_other;
                CREATE EXTENSION ankus_schemas WITH SCHEMA schemas_target;
                """, token);
            Assert.AreSequenceEqual<string>([
                "pg_catalog.mypgcatalogtype", "public.hello_public", "schemas_target.hello_default_schema",
                "schemas_target.mytype", "schemas_target.return_vec_of_customtype", "some_schema.hello_some_schema",
                "some_schema.mysomeschematype",
            ], await Scalar<string[]>(connection, transaction, """
                SELECT array_agg(name ORDER BY name COLLATE "C") FROM (
                    SELECT n.nspname || '.' || p.proname AS name
                    FROM pg_depend d JOIN pg_proc p ON d.classid = 'pg_proc'::regclass AND p.oid = d.objid
                        JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE d.refclassid = 'pg_extension'::regclass AND d.deptype = 'e'
                        AND d.refobjid = (SELECT oid FROM pg_extension WHERE extname = 'ankus_schemas')
                        AND p.prorettype <> 'cstring'::regtype AND NOT p.proargtypes::oid[] && ARRAY['cstring'::regtype::oid]
                    UNION ALL
                    SELECT n.nspname || '.' || t.typname
                    FROM pg_depend d JOIN pg_type t ON d.classid = 'pg_type'::regclass AND t.oid = d.objid
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                    WHERE d.refclassid = 'pg_extension'::regclass AND d.deptype = 'e' AND t.typcategory <> 'A'
                        AND d.refobjid = (SELECT oid FROM pg_extension WHERE extname = 'ankus_schemas')) AS members
                """, token));
            Assert.AreSequenceEqual<string>(["search_path=schemas_target"], await Scalar<string[]>(connection, transaction,
                "SELECT proconfig FROM pg_proc WHERE oid = 'schemas_target.return_vec_of_customtype()'::regprocedure", token));
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT NOT e.extrelocatable AND e.extnamespace = 'schemas_target'::regnamespace
                    AND EXISTS(SELECT FROM pg_depend d WHERE d.classid = 'pg_namespace'::regclass
                        AND d.objid = 'some_schema'::regnamespace AND d.refobjid = e.oid AND d.deptype = 'e')
                    AND NOT EXISTS(SELECT FROM pg_depend d WHERE d.classid = 'pg_namespace'::regclass
                        AND d.objid IN ('public'::regnamespace, 'pg_catalog'::regnamespace, 'schemas_target'::regnamespace)
                        AND d.refobjid = e.oid)
                    AND (SELECT superuser FROM pg_available_extension_versions WHERE name = 'ankus_schemas')
                FROM pg_extension e WHERE e.extname = 'ankus_schemas'
                """, token));

            await transaction.SaveAsync("installation_schema", token);
            PostgresException missing = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "SELECT hello_default_schema()", token));
            Assert.AreEqual("42883", missing.SqlState);
            Assert.AreEqual("function hello_default_schema() does not exist", missing.MessageText);
            await transaction.RollbackAsync("installation_schema", token);
            Assert.AreEqual("Hello from the schema where you installed this extension",
                await Scalar<string>(connection, transaction, "SELECT schemas_target.hello_default_schema()", token));
            Assert.AreEqual("Hello from the public schema", await Scalar<string>(connection, transaction, "SELECT hello_public()", token));
            Assert.AreEqual("schemas_target.mytype[]",
                await Scalar<string>(connection, transaction, "SELECT pg_typeof(schemas_target.return_vec_of_customtype())::text", token));

            await transaction.SaveAsync("relocation", token);
            PostgresException relocation = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "ALTER EXTENSION ankus_schemas SET SCHEMA schemas_other", token));
            Assert.AreEqual("0A000", relocation.SqlState);
            Assert.AreEqual("extension \"ankus_schemas\" does not support SET SCHEMA", relocation.MessageText);
            await transaction.RollbackAsync("relocation", token);

            await Execute(connection, transaction, "DROP EXTENSION ankus_schemas", token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT to_regnamespace('some_schema') IS NULL AND to_regtype('pg_catalog.mypgcatalogtype') IS NULL
                    AND to_regprocedure('public.hello_public()') IS NULL AND to_regtype('schemas_target.mytype') IS NULL
                    AND to_regnamespace('schemas_target') IS NOT NULL AND to_regnamespace('public') IS NOT NULL
                """, token));
            await Execute(connection, transaction, "CREATE EXTENSION ankus_schemas WITH SCHEMA schemas_other", token);
            Assert.AreEqual("""{"Value":"again"}""", await Scalar<string>(connection, transaction,
                """SELECT '{"Value":"again"}'::schemas_other.mytype::text""", token));
            Assert.AreEqual("Hello from some_schema",
                await Scalar<string>(connection, transaction, "SELECT some_schema.hello_some_schema()", token));
            Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, transaction, "SELECT pg_backend_pid()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Requires a superuser because the extension creates a type in pg_catalog.
    /// </summary>
    [TestMethod]
    public Task SchemasSampleRequiresSuperuser()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(SchemasSampleRequiresSuperuser), async (connection, transaction, token) =>
        {
            string role = "schemas_user_" + Guid.NewGuid().ToString("N");
            await Execute(connection, transaction, $"CREATE ROLE {role}", token);
            await transaction.SaveAsync("unprivileged", token);
            await Execute(connection, transaction, $"SET LOCAL ROLE {role}", token);
            PostgresException denied = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
                Execute(connection, transaction, "CREATE EXTENSION ankus_schemas", token));
            Assert.AreEqual("42501", denied.SqlState);
            Assert.AreEqual("permission denied to create extension \"ankus_schemas\"", denied.MessageText);
            Assert.AreEqual("Must be superuser to create this extension.", denied.Hint);
            await transaction.RollbackAsync("unprivileged", token);
            await Execute(connection, transaction, "CREATE EXTENSION ankus_schemas", token);
            Assert.AreEqual("Hello from the public schema", await Scalar<string>(connection, transaction, "SELECT hello_public()", token));
        }, context.CancellationToken);

    /// <summary>
    /// Returns one typed observation.
    /// </summary>
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Assert.IsInstanceOfType<T>(await command.ExecuteScalarAsync(token));
    }

    /// <summary>
    /// Completes DDL or an error-producing operation before the next assertion.
    /// </summary>
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }
}
