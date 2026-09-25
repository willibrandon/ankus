using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies the native namespace and regtype contracts through a published Native AOT extension.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
public sealed class NamespaceLookupTests(TestContext context)
{
    /// <summary>
    /// Exact binary and prefix signatures resolve the same independent catalog rows.
    /// </summary>
    /// <param name="name">The operator token.</param>
    /// <param name="left">The exact left type or zero.</param>
    /// <param name="right">The exact right type.</param>
    [TestMethod]
    [DataRow("+", 23U, 23U)]
    [DataRow("-", 0U, 23U)]
    [DataRow("+", 20U, 20U)]
    [DataRow("=", 25U, 25U)]
    public Task OperatorLookupMatchesCatalog(string name, uint left, uint right)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(OperatorLookupMatchesCatalog), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT oid FROM pg_operator WHERE oprnamespace='pg_catalog'::regnamespace
                  AND oprname=$1 AND oprleft=$2 AND oprright=$3
                """, connection, transaction);
            command.Parameters.AddWithValue(name);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Oid, Value = left });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Oid, Value = right });
            uint expected = (uint)(await command.ExecuteScalarAsync(token))!;
            Assert.AreNotEqual(0U, expected);
            command.CommandText = "SELECT catalog_lookup.operator_oid(ARRAY['pg_catalog',$1],$2,$3)";
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT catalog_lookup.operator_oid(ARRAY[$1],$2,$3)";
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT catalog_lookup.operator_oid(ARRAY[current_database(),'pg_catalog',$1],$2,$3)";
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Missing schemas, exact-type mismatches and raw operator spellings return InvalidOid without parser normalization.
    /// </summary>
    [TestMethod]
    public Task MissingOperatorsReturnZero()
        => CheckAsync("""
            SELECT concat_ws('|',
              catalog_lookup.operator_oid(ARRAY['no_such_lookup_schema','+'],23,23),
              catalog_lookup.operator_oid(ARRAY['pg_catalog','+'],25,25),
              catalog_lookup.operator_oid(ARRAY['!='],23,23),
              catalog_lookup.operator_oid(ARRAY[''],23,23),
              catalog_lookup.operator_oid(ARRAY['pg_catalog','+'],4294967295,23),
              catalog_lookup.operator_oid(ARRAY['pg_catalog','+'],23,0))
            """, "0|0|0|0|0|0");

    /// <summary>
    /// Aliases, arrays, typmods, pseudotypes and raw OIDs retain native regtype parsing semantics.
    /// </summary>
    /// <param name="name">The exact native input syntax.</param>
    /// <param name="oid">The independently known built-in or raw OID.</param>
    [TestMethod]
    [DataRow("int", 23U)]
    [DataRow("INTEGER[]", 1007U)]
    [DataRow(" numeric(12,3) ", 1700U)]
    [DataRow("timestamp(6) with time zone", 1184U)]
    [DataRow("character varying(32)[]", 1015U)]
    [DataRow("text[][]", 1009U)]
    [DataRow("\"char\"", 18U)]
    [DataRow("record", 2249U)]
    [DataRow("-", 0U)]
    [DataRow("0", 0U)]
    [DataRow("4294967295", uint.MaxValue)]
    [DataRow("4294967294", 4294967294U)]
    public Task TypeSyntaxMatchesRegtype(string name, uint oid)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(TypeSyntaxMatchesRegtype), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT catalog_lookup.type_oid($1)", connection, transaction);
            command.Parameters.AddWithValue(name);
            Assert.AreEqual(oid, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT $1::regtype::oid";
            Assert.AreEqual(oid, await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Qualified components preserve Unicode, dots, quotes and case rather than interpreting SQL spelling.
    /// </summary>
    [TestMethod]
    public Task ExactNamesRetainIdentity()
        => CheckAsync("""
            CREATE SCHEMA "Lookup.Café""字";
            CREATE DOMAIN "Lookup.Café""字"."Ty.Pe" AS integer;
            CREATE OPERATOR "Lookup.Café""字".#+ (FUNCTION=pg_catalog.int4pl, LEFTARG=integer, RIGHTARG=integer);
            SELECT concat_ws('|',
              catalog_lookup.operator_oid(ARRAY['Lookup.Café"字','#+'],23,23) =
                (SELECT oid FROM pg_operator WHERE oprnamespace='"Lookup.Café""字"'::regnamespace AND oprname='#+'),
              catalog_lookup.operator_oid(ARRAY['lookup.café"字','#+'],23,23),
              catalog_lookup.operator_oid(ARRAY['"Lookup.Café""字"','#+'],23,23),
              catalog_lookup.type_oid('"Lookup.Café""字"."Ty.Pe"') = '"Lookup.Café""字"."Ty.Pe"'::regtype::oid,
              catalog_lookup.type_oid('"Lookup.Café""字"."Ty.Pe"[]') = '"Lookup.Café""字"."Ty.Pe"[]'::regtype::oid)
            """, "t|0|0|t|t");

    /// <summary>
    /// Search-path changes are observed by one reused builder, and temporary operators are excluded from unqualified lookup.
    /// </summary>
    [TestMethod]
    public Task SearchPathControlsLookup()
        => CheckAsync("""
            CREATE SCHEMA lookup_first;
            CREATE SCHEMA lookup_second;
            CREATE OPERATOR lookup_first.#+ (FUNCTION=pg_catalog.int4pl, LEFTARG=integer, RIGHTARG=integer);
            CREATE OPERATOR lookup_second.#+ (FUNCTION=pg_catalog.int4mi, LEFTARG=integer, RIGHTARG=integer);
            CREATE OPERATOR lookup_first.+ (FUNCTION=pg_catalog.int4mi, LEFTARG=integer, RIGHTARG=integer);
            CREATE TEMP TABLE lookup_temp(value integer);
            CREATE OPERATOR pg_temp.#+ (FUNCTION=pg_catalog.int4mul, LEFTARG=integer, RIGHTARG=integer);
            SET LOCAL search_path=lookup_first,lookup_second;
            SELECT concat_ws('|',
              catalog_lookup.operator_changes(ARRAY['#+'],23,23,'SET LOCAL search_path=lookup_second,lookup_first') =
                ARRAY['lookup_first.#+(integer,integer)'::regoperator::oid,'lookup_second.#+(integer,integer)'::regoperator::oid],
              catalog_lookup.operator_oid(ARRAY['pg_temp','#+'],23,23) = 'pg_temp.#+(integer,integer)'::regoperator::oid);
            """, "t|t");

    /// <summary>
    /// Implicit pg_catalog priority and explicit search-path placement retain native operator selection rules.
    /// </summary>
    [TestMethod]
    public Task CatalogPriorityMatchesNativeRules()
        => CheckAsync("""
            CREATE SCHEMA lookup_catalog_priority;
            CREATE OPERATOR lookup_catalog_priority.+ (FUNCTION=pg_catalog.int4mi, LEFTARG=integer, RIGHTARG=integer);
            SET LOCAL search_path=lookup_catalog_priority;
            SELECT (catalog_lookup.operator_changes(ARRAY['+'],23,23,
              'SET LOCAL search_path=lookup_catalog_priority,pg_catalog') =
              ARRAY['pg_catalog.+(integer,integer)'::regoperator::oid,
                    'lookup_catalog_priority.+(integer,integer)'::regoperator::oid])::text
            """, "true");

    /// <summary>
    /// Explicit namespace access enforces USAGE, then allows retry after privileges change in the same transaction.
    /// </summary>
    [TestMethod]
    public Task NamespacePermissionsAreEnforced()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamespacePermissionsAreEnforced), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE ROLE ankus_lookup_reader;
                CREATE SCHEMA lookup_private;
                CREATE DOMAIN lookup_private.value AS integer;
                CREATE OPERATOR lookup_private.#+ (FUNCTION=pg_catalog.int4pl, LEFTARG=integer, RIGHTARG=integer);
                GRANT USAGE ON SCHEMA catalog_lookup TO ankus_lookup_reader;
                SET LOCAL ROLE ankus_lookup_reader;
                SELECT catalog_lookup.catch_lookup(false,ARRAY['lookup_private','#+']) || '/' ||
                  catalog_lookup.catch_lookup(true,ARRAY['lookup_private.value'])
                """, connection, transaction);
            Assert.AreEqual("42501|True|42/42501|True|42", await command.ExecuteScalarAsync(token));
            command.CommandText = """
                RESET ROLE;
                GRANT USAGE ON SCHEMA lookup_private TO ankus_lookup_reader;
                SET LOCAL ROLE ankus_lookup_reader;
                SELECT concat_ws('|',
                  catalog_lookup.operator_oid(ARRAY['lookup_private','#+'],23,23) =
                    (SELECT oid FROM pg_operator WHERE oprnamespace='lookup_private'::regnamespace AND oprname='#+'),
                  catalog_lookup.type_oid('lookup_private.value')='lookup_private.value'::regtype::oid)
                """;
            Assert.AreEqual("t|t", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Type names track search-path order and exact domain OIDs are never coerced to their underlying operator signature.
    /// </summary>
    [TestMethod]
    public Task TypeSearchPathAndExactSignaturesArePreserved()
        => CheckAsync("""
            CREATE SCHEMA lookup_type_first;
            CREATE SCHEMA lookup_type_second;
            CREATE DOMAIN lookup_type_first.value AS integer;
            CREATE DOMAIN lookup_type_second.value AS bigint;
            SET LOCAL search_path=lookup_type_first,lookup_type_second;
            SELECT concat_ws('|',
              catalog_lookup.type_changes('value','SET LOCAL search_path=lookup_type_second,lookup_type_first') =
                ARRAY['lookup_type_first.value'::regtype::oid,'lookup_type_second.value'::regtype::oid],
              catalog_lookup.operator_oid(ARRAY['pg_catalog','+'],'lookup_type_first.value'::regtype::oid,23))
            """, "t|0");

    /// <summary>
    /// Invalid name counts and foreign databases surface native diagnostics after managed finally and recover.
    /// </summary>
    /// <param name="names">SQL syntax for the independent components.</param>
    /// <param name="state">The native SQLSTATE.</param>
    [TestMethod]
    [DataRow("ARRAY[]::text[]", "42601")]
    [DataRow("ARRAY['a','b','c','d']", "42601")]
    [DataRow("ARRAY['other_ankus_database','pg_catalog','+']", "0A000")]
    public Task QualifiedNamesPreserveNativeErrors(string names, string state)
        => CheckAsync($"SELECT catalog_lookup.catch_lookup(false,{names})", state + "|True|42");

    /// <summary>
    /// Named-type parser failures retain native error classes and leave the same backend usable.
    /// </summary>
    /// <param name="name">The malformed or absent type syntax.</param>
    /// <param name="state">The independently expected SQLSTATE.</param>
    [TestMethod]
    [DataRow("no_such_ankus_lookup_type", "42704")]
    [DataRow("no_such_lookup_schema.value", "3F000")]
    [DataRow("integer[", "42601")]
    [DataRow("", "42601")]
    [DataRow("4294967296", "22003")]
    public Task LookupErrorsRecover(string name, string state)
        => CheckAsync($"SELECT catalog_lookup.catch_lookup(true,ARRAY['{name}'])", state + "|True|42");

    /// <summary>
    /// Finite CLR-name lookup uses short metadata names, native case folding and array syntax under Native AOT.
    /// </summary>
    [TestMethod]
    public Task ManagedNameLookupUsesShortName()
        => CheckAsync("""
            CREATE DOMAIN pg_temp.lookupname AS integer;
            CREATE DOMAIN pg_temp.int32 AS bigint;
            SELECT concat_ws('|',
              catalog_lookup.managed_name_oid(0)='pg_temp.lookupname'::regtype::oid,
              catalog_lookup.managed_name_oid(1)='pg_temp.lookupname[]'::regtype::oid,
              catalog_lookup.managed_name_oid(2)='pg_temp.int32'::regtype::oid,
              catalog_lookup.managed_name_oid(2)<>23)
            """, "t|t|t|t");

    /// <summary>
    /// Generic CLR arity is retained under Native AOT and reaches the native parser instead of an inferred SQL mapping.
    /// </summary>
    [TestMethod]
    public Task GenericManagedNameRetainsNativeParserError()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GenericManagedNameRetainsNativeParserError), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await transaction.SaveAsync("generic_name", token);
            await using var command = new NpgsqlCommand("SELECT catalog_lookup.managed_name_oid(3)", connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("42601", error.SqlState);
            await transaction.RollbackAsync("generic_name", token);
            command.CommandText = "SELECT catalog_lookup.type_oid('integer')";
            Assert.AreEqual(23U, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);

    /// <summary>
    /// Repeated names observe new catalog identities after transactional DROP and CREATE.
    /// </summary>
    [TestMethod]
    public Task ReusedLookupsObserveCatalogChanges()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(ReusedLookupsObserveCatalogChanges), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA lookup_changes;
                CREATE DOMAIN lookup_changes.changing AS integer;
                CREATE OPERATOR lookup_changes.#+ (FUNCTION=pg_catalog.int4pl, LEFTARG=integer, RIGHTARG=integer);
                SELECT 'lookup_changes.changing'::regtype::oid
                """, connection, transaction);
            uint typeBefore = (uint)(await command.ExecuteScalarAsync(token))!;
            command.CommandText = """
                SELECT catalog_lookup.type_changes('lookup_changes.changing',
                  'DROP DOMAIN lookup_changes.changing; CREATE DOMAIN lookup_changes.changing AS bigint')
                """;
            uint[] types = (uint[])(await command.ExecuteScalarAsync(token))!;
            Assert.HasCount(2, types);
            Assert.AreEqual(typeBefore, types[0]);
            Assert.AreNotEqual(types[0], types[1]);
            command.CommandText = "SELECT 'lookup_changes.changing'::regtype::oid";
            Assert.AreEqual(types[1], await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 'lookup_changes.#+(integer,integer)'::regoperator::oid";
            uint operatorBefore = (uint)(await command.ExecuteScalarAsync(token))!;
            command.CommandText = """
                SELECT catalog_lookup.operator_changes(ARRAY['lookup_changes','#+'],23,23,
                  'DROP OPERATOR lookup_changes.#+(integer,integer); CREATE OPERATOR lookup_changes.#+ (FUNCTION=pg_catalog.int4mi, LEFTARG=integer, RIGHTARG=integer)')
                """;
            uint[] operators = (uint[])(await command.ExecuteScalarAsync(token))!;
            Assert.HasCount(2, operators);
            Assert.AreEqual(operatorBefore, operators[0]);
            Assert.AreNotEqual(operators[0], operators[1]);
            command.CommandText = "SELECT 'lookup_changes.#+(integer,integer)'::regoperator::oid";
            Assert.AreEqual(operators[1], await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);

    /// <summary>
    /// Repeated live and absent lookups plus large ERROR diagnostics release native operation contexts and bounded transaction storage.
    /// </summary>
    [TestMethod]
    public Task RepeatedLookupsReleaseTemporaryStorage()
        => CheckAsync("SELECT catalog_lookup.lookup_storage()", "128|0|bounded");

    private Task CheckAsync(string sql, string expected)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(NamespaceLookupTests), async (connection, transaction, token) =>
        {
            int backend = connection.ProcessID;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.AreEqual(expected, await command.ExecuteScalarAsync(token));
            command.CommandText = "SELECT 42";
            Assert.AreEqual(42, await command.ExecuteScalarAsync(token));
            Assert.AreEqual(backend, connection.ProcessID);
        }, context.CancellationToken);
}
