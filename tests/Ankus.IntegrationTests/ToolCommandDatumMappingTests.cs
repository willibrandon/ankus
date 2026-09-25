using Ankus.PgConfig;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// Owned scalar mappings resolve current extension identities through relocation and reinstall independently of external mappings.
    /// </summary>
    [TestMethod]
    public async Task DatumMappingPackageRelocatesAndReinstallsWithCurrentTypeIdentity()
    {
        CancellationToken token = context.CancellationToken;
        string directory = CreateDirectory();
        string project = Path.Combine(directory, "DatumMappings.csproj");
        File.Copy(s_project, project);
        await File.WriteAllTextAsync(Path.Combine(directory, "Mappings.cs"), DatumMappingPackageSource, token);
        string output = Path.Combine(directory, "published");
        await PublishSqlControlsAsync(project, output, token);
        PublishedExtension manifest = PublishedExtension.Read(output);
        string sql = await File.ReadAllTextAsync(Path.Combine(output, "extension", manifest.Sql), token);
        Assert.Contains("relocatable = true", await File.ReadAllTextAsync(Path.Combine(output, "extension", manifest.Control), token));
        int provider = sql.IndexOf("CREATE DOMAIN package_key", StringComparison.Ordinal);
        int consumer = sql.IndexOf("CREATE FUNCTION \"package_echo\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, provider);
        Assert.IsGreaterThan(provider, consumer);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token);
        await using NpgsqlConnection connection = await cluster.OpenConnectionAsync(token);
        int process = connection.ProcessID;
        await ExecuteSqlPackageAsync(connection, """
            CREATE SCHEMA mapping_first; CREATE SCHEMA mapping_second; CREATE SCHEMA mapping_shadow;
            CREATE DOMAIN mapping_shadow.package_key AS bigint CHECK (VALUE < 0);
            CREATE DOMAIN mapping_shadow.int4 AS text CHECK (VALUE LIKE 'shadow%');
            CREATE FUNCTION mapping_shadow.package_echo(mapping_shadow.package_key) RETURNS mapping_shadow.package_key
                LANGUAGE SQL IMMUTABLE AS 'SELECT $1';
            SET search_path=mapping_shadow,pg_catalog;
            """);
        uint[] shadow = await DatumMappingShadowIdentities(connection);
        await ExecuteSqlPackageAsync(connection, "CREATE EXTENSION ankus_tool_probe WITH SCHEMA mapping_first");
        Dictionary<string, uint> original = await DatumMappingPackageMembers(connection, "mapping_first");
        uint originalType = original["type:package_key"];
        await AssertDatumMappingPackage(connection, "mapping_first", originalType);
        Assert.AreEqual(originalType, await SqlPackageScalarAsync<uint>(connection, "SELECT mapping_first.package_remember(42)"));
        Assert.AreEqual(1042, await SqlPackageScalarAsync<int>(connection, "SELECT mapping_first.package_replay()"));

        await ExecuteSqlPackageAsync(connection, "ALTER EXTENSION ankus_tool_probe SET SCHEMA mapping_second");
        Dictionary<string, uint> moved = await DatumMappingPackageMembers(connection, "mapping_second");
        Assert.AreSequenceEqual(original.Keys, moved.Keys);
        foreach ((string name, uint oid) in original)
        {
            Assert.AreEqual(oid, moved[name], name);
        }

        await AssertDeclarationSchemaEmpty(connection, "mapping_first");
        await AssertDatumMappingPackage(connection, "mapping_second", originalType);
        Assert.AreEqual(1042, await SqlPackageScalarAsync<int>(connection, "SELECT mapping_second.package_replay()"));
        await ExecuteSqlPackageAsync(connection, "DROP EXTENSION ankus_tool_probe");
        await AssertDeclarationSchemaEmpty(connection, "mapping_second");
        Assert.IsFalse(await SqlPackageScalarAsync<bool>(connection,
            "SELECT EXISTS(SELECT FROM pg_extension WHERE extname='ankus_tool_probe')"));
        Assert.AreSequenceEqual(shadow, await DatumMappingShadowIdentities(connection));
        Assert.AreEqual("-7|shadow value", await SqlPackageScalarAsync<string>(connection, """
            SELECT mapping_shadow.package_echo('-7')::text||'|'||'shadow value'::mapping_shadow.int4::text
            """));

        await ExecuteSqlPackageAsync(connection, "CREATE EXTENSION ankus_tool_probe WITH SCHEMA mapping_first");
        Dictionary<string, uint> reinstalled = await DatumMappingPackageMembers(connection, "mapping_first");
        Assert.AreSequenceEqual(original.Keys, reinstalled.Keys);
        foreach ((string name, uint oid) in original)
        {
            Assert.AreNotEqual(oid, reinstalled[name], name);
        }

        PostgresException stale = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ExecuteSqlPackageAsync(connection, "SELECT mapping_first.package_replay()"));
        Assert.AreEqual("38000", stale.SqlState);
        Assert.AreEqual("The mapped PostgreSQL parameter type has changed since the parameter was created.", stale.MessageText);
        uint newType = reinstalled["type:package_key"];
        await AssertDatumMappingPackage(connection, "mapping_first", newType);
        Assert.AreEqual(newType, await SqlPackageScalarAsync<uint>(connection, "SELECT mapping_first.package_remember(7)"));
        Assert.AreEqual(1007, await SqlPackageScalarAsync<int>(connection, "SELECT mapping_first.package_replay()"));
        Assert.AreEqual(process, await SqlPackageScalarAsync<int>(connection, "SELECT pg_backend_pid()"));
        await ExecuteSqlPackageAsync(connection, "DROP EXTENSION ankus_tool_probe");
        await AssertDeclarationSchemaEmpty(connection, "mapping_first");
        await AssertDeclarationSchemaEmpty(connection, "mapping_second");
        Assert.AreSequenceEqual(shadow, await DatumMappingShadowIdentities(connection));
        Assert.AreEqual(23U, await SqlPackageScalarAsync<uint>(connection, "SELECT 'pg_catalog.int4'::regtype::oid"));
    }

    /// <summary>
    /// Executes independently expected converter results and domain failures after each catalog lifecycle phase.
    /// </summary>
    private async Task AssertDatumMappingPackage(NpgsqlConnection connection, string schema, uint typeOid)
    {
        int process = connection.ProcessID;
        Assert.AreEqual("1|43|1042|true|8", await SqlPackageScalarAsync<string>(connection, $"""
            SELECT concat_ws('|',{schema}.package_echo(0)::integer,{schema}.package_echo(42)::integer,
                {schema}.package_read(42),({schema}.package_echo(NULL::{schema}.package_key) IS NULL)::text,
                {schema}.package_external_echo(7))
            """));
        Assert.AreEqual(FormattableString.Invariant($"{typeOid}|1042|True|True|23|2042"),
            await SqlPackageScalarAsync<string>(connection, $"SELECT {schema}.package_probe(42)"));
        Assert.AreEqual("1042|1043|1043|2042|True|True",
            await SqlPackageScalarAsync<string>(connection, $"SELECT {schema}.package_typed()"));
        Assert.IsTrue(await SqlPackageScalarAsync<bool>(connection, $"""
            SELECT (SELECT proargtypes[0]='{schema}.package_key'::regtype
                    AND prorettype='{schema}.package_key'::regtype AND NOT proisstrict
                    FROM pg_proc WHERE oid='{schema}.package_echo({schema}.package_key)'::regprocedure)
                AND (SELECT proargtypes[0]=23 AND prorettype=23 AND proisstrict
                    FROM pg_proc WHERE oid='{schema}.package_external_echo(integer)'::regprocedure)
                AND (SELECT typtype='d' AND typbasetype=23 AND typlen=4 AND typbyval
                    FROM pg_type WHERE oid='{schema}.package_key'::regtype)
            """));
        PostgresException rejected = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ExecuteSqlPackageAsync(connection, $"SELECT {schema}.package_probe(-1)"));
        Assert.AreEqual("23514", rejected.SqlState);
        Assert.AreEqual($"value for domain {schema}.package_key violates check constraint \"package_key_check\"", rejected.MessageText);
        Assert.AreEqual(FormattableString.Invariant($"{typeOid}|1000|True|True|23|2000"),
            await SqlPackageScalarAsync<string>(connection, $"SELECT {schema}.package_probe(0)"));
        Assert.AreEqual(process, await SqlPackageScalarAsync<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Captures the domain, its array and all seven generated callbacks with their exact extension ownership.
    /// </summary>
    private async Task<Dictionary<string, uint>> DatumMappingPackageMembers(NpgsqlConnection connection, string schema)
    {
        string[] expected = ["array:package_key", "function:package_echo", "function:package_external_echo",
            "function:package_probe", "function:package_read", "function:package_remember", "function:package_replay",
            "function:package_typed", "type:package_key"];
        await using var command = new NpgsqlCommand($"""
            WITH objects(label,classid,objid,ownerid) AS (
                SELECT 'type:'||typname,'pg_type'::regclass,oid,oid FROM pg_type
                    WHERE typnamespace='{schema}'::regnamespace AND typname='package_key'
                UNION ALL SELECT 'array:'||t.typname,'pg_type'::regclass,a.oid,t.oid FROM pg_type t JOIN pg_type a
                    ON a.oid=t.typarray AND a.typelem=t.oid AND a.typnamespace=t.typnamespace
                    WHERE t.typnamespace='{schema}'::regnamespace AND t.typname='package_key'
                UNION ALL SELECT 'function:'||proname,'pg_proc'::regclass,oid,oid FROM pg_proc
                    WHERE pronamespace='{schema}'::regnamespace)
            SELECT label,objid,EXISTS(SELECT FROM pg_depend d JOIN pg_extension e ON e.oid=d.refobjid
                WHERE d.classid=objects.classid AND d.objid=objects.ownerid AND d.refclassid='pg_extension'::regclass
                    AND d.deptype='e' AND e.extname='ankus_tool_probe') FROM objects ORDER BY label COLLATE "C"
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        var objects = new Dictionary<string, uint>(StringComparer.Ordinal);
        while (await reader.ReadAsync(context.CancellationToken))
        {
            string label = reader.GetString(0);
            Assert.IsTrue(reader.GetBoolean(2), label);
            objects.Add(label, reader.GetFieldValue<uint>(1));
        }

        Assert.AreSequenceEqual(expected, objects.Keys);
        return objects;
    }

    /// <summary>
    /// Retains unrelated same-name domain and function identities as a schema-resolution and drop witness.
    /// </summary>
    private Task<uint[]> DatumMappingShadowIdentities(NpgsqlConnection connection)
        => SqlPackageScalarAsync<uint[]>(connection, """
            SELECT ARRAY['mapping_shadow.package_key'::regtype::oid,'mapping_shadow.package_key[]'::regtype::oid,
                'mapping_shadow.int4'::regtype::oid,'mapping_shadow.int4[]'::regtype::oid,
                'mapping_shadow.package_echo(mapping_shadow.package_key)'::regprocedure::oid]
            """);

    /// <summary>
    /// Supplies a relocatable owned domain plus a fixed external wrapper with distinguishable reader results.
    /// </summary>
    private const string DatumMappingPackageSource = """
        using Ankus;
        [assembly: PgSql("mapping-type", "CREATE DOMAIN package_key AS integer CHECK (VALUE >= 0);", Relocatable = true)]
        [assembly: PgSqlTypeProvider("mapping-type", typeof(OwnedKey))]
        [PgDatumType("package_key", typeof(OwnedKeyConverter))]
        public readonly record struct OwnedKey(int Number);
        public sealed class OwnedKeyConverter : IPgDatumReader<OwnedKey>, IPgDatumWriter<OwnedKey>
        {
            public OwnedKey Read(PgDatum value) => new(value.Read<int>() + 1000);
            public PgDatum Write(OwnedKey value, uint typeOid, PgMemoryContext destination)
                => PgDatum.DangerousCreate(unchecked((nuint)(nint)(value.Number - 1000)), typeOid, destination);
        }
        [PgDatumType("int4", typeof(ExternalKeyConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
        public readonly record struct ExternalKey(int Number);
        public sealed class ExternalKeyConverter : IPgDatumReader<ExternalKey>, IPgDatumWriter<ExternalKey>
        {
            public ExternalKey Read(PgDatum value) => new(value.Read<int>() + 2000);
            public PgDatum Write(ExternalKey value, uint typeOid, PgMemoryContext destination)
                => PgDatum.DangerousCreate(unchecked((nuint)(nint)(value.Number - 2000)), typeOid, destination);
        }
        public static class Functions
        {
            private static SpiParameter s_remembered;
            [PgFunction(Name = "package_echo")]
            public static OwnedKey? Echo(OwnedKey? value) => value is { } present ? new OwnedKey(present.Number + 1) : null;
            [PgFunction(Name = "package_read")]
            public static int ReadKey(OwnedKey value) => value.Number;
            [PgFunction(Name = "package_external_echo")]
            public static ExternalKey ExternalEcho(ExternalKey value) => new(value.Number + 1);
            [PgFunction(Name = "package_probe")]
            public static string Probe(int value)
            {
                using SpiRawResult result = Spi.QueryRaw("SELECT $1,$2,$3",
                    SpiParameter.Create(new OwnedKey(value + 1000)), SpiParameter.Create<OwnedKey?>(null),
                    SpiParameter.Create(new ExternalKey(value + 2000)));
                PgDatum own = result[0][0];
                PgDatum absent = result[0][1];
                PgDatum external = result[0][2];
                return FormattableString.Invariant($"{own.TypeOid}|{own.Read<OwnedKey>().Number}|{absent.IsNull}|{absent.Read<OwnedKey?>() is null}|{external.TypeOid}|{external.Read<ExternalKey>().Number}");
            }
            [PgFunction(Name = "package_remember")]
            public static uint Remember(int value)
            {
                s_remembered = SpiParameter.Create(new OwnedKey(value + 1000));
                return s_remembered.TypeOid;
            }
            [PgFunction(Name = "package_replay")]
            public static int Replay()
                => Spi.ExecuteScalar<OwnedKey>("SELECT $1", s_remembered).Number;
            [PgFunction(Name = "package_typed")]
            public static string Typed(PgFunctionContext call)
            {
                string schema = Spi.ExecuteScalar<string>(
                    "SELECT n.nspname::text FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE p.oid=$1",
                    SpiParameter.Create(call.FunctionOid));
                string type = Spi.QuoteQualifiedIdentifier(schema, "package_key");
                string function = Spi.QuoteQualifiedIdentifier(schema, "package_echo");
                uint oid = Spi.ExecuteScalar<uint>("SELECT $1::regprocedure::oid", SpiParameter.Create($"{function}({type})"));
                OwnedKey queried = Spi.ExecuteScalar<OwnedKey>($"SELECT 42::{type}");
                OwnedKey named = PgFunctions.Call<OwnedKey>(function, PgFunctionArgument.Create(new OwnedKey(1042)));
                OwnedKey identified = PgFunctions.Call<OwnedKey>(oid, PgFunctionArgument.Create(new OwnedKey(1042)));
                ExternalKey external = PgFunctions.Call<ExternalKey>("pg_catalog.abs", PgFunctionArgument.Create(-42));
                bool queryNull = Spi.ExecuteScalar<OwnedKey?>($"SELECT NULL::{type}") is null;
                bool callNull = PgFunctions.Call<OwnedKey?>(function, PgFunctionArgument.Create<OwnedKey?>(null)) is null;
                return FormattableString.Invariant($"{queried.Number}|{named.Number}|{identified.Number}|{external.Number}|{queryNull}|{callNull}");
            }
        }
        """;
}
