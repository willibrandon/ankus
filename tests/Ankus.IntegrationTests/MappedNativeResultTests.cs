using System.Globalization;
using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Executes requested mapped readers through valid native addresses without asserting catalog declaration proof.
/// </summary>
/// <param name="context">The test cancellation context.</param>
[TestClass]
[DoNotParallelize]
public sealed class MappedNativeResultTests(TestContext context)
{
    /// <summary>
    /// Exact selected readers preserve zero, extrema, nullable absence and unnamed CLR enum values.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeScalarsSelectRequestedReadersAndPreserveNull()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await Address(connection, "tests.native_nullable_sum(integer,integer)");
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT {Number(address, "SELECT NULL::integer,2", 0)} IS NULL"));
        Assert.AreEqual("0|0|0|0|0|0", await Scalar<string>(connection, "SELECT mapped_results.counts()"));
        foreach (int value in new[] { 0, 42, int.MinValue, int.MaxValue })
        {
            Assert.AreEqual(value, await Scalar<int>(connection,
                $"SELECT {Number(address, $"SELECT {Literal(value.ToString(CultureInfo.InvariantCulture))}::integer,0", 0)}"));
            Assert.AreEqual("23", await Scalar<string>(connection, "SELECT mapped_native.captures(0)"));
        }

        Assert.AreEqual(142, await Scalar<int>(connection, $"SELECT {Number(address, "SELECT 40,2", 1)}"));
        Assert.AreEqual(-17, await Scalar<int>(connection, $"SELECT {Number(address, "SELECT -17,0", 2)}"));
        Assert.AreEqual(0, await Scalar<int>(connection, $"SELECT {Number(address, "SELECT 0,0", 2)}"));
        Assert.AreEqual("1|4|0|0|0|0", await Scalar<string>(connection, "SELECT mapped_results.counts()"));
        await Failure(connection, $"SELECT {Number(address, "SELECT NULL::integer,2", 3)}", "38000",
            "SQL NULL cannot be read as a non-nullable managed value.");
        Assert.AreEqual(73, await Scalar<int>(connection, $"SELECT {Number(address, "SELECT 70,3", 0)}"));
        await SameBackend(connection);
    }

    /// <summary>
    /// SQL NULL, empty and all-NULL results bypass the shared lazy factory until a present value needs conversion.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeNullAndEmptyResultsBypassLazyFactories()
    {
        await using NpgsqlConnection connection = await Open();
        long sum = await Address(connection, "tests.native_nullable_sum(integer,integer)");
        long concatenate = await ArrayAddress(connection);
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT {Number(sum, "SELECT NULL::integer,0", 4)} IS NULL"));
        foreach (bool shaped in new[] { false, true })
        {
            Assert.IsTrue(await Scalar<bool>(connection,
                $"SELECT mapped_native.factory_array({concatenate},{Literal("SELECT NULL::integer[],NULL::integer[]")},{shaped}) IS NULL"));
            Assert.AreEqual(0, await Scalar<int>(connection,
                $"SELECT mapped_native.factory_array({concatenate},{Literal("SELECT ARRAY[]::integer[],ARRAY[]::integer[]")},{shaped})"));
            Assert.AreEqual(2, await Scalar<int>(connection,
                $"SELECT mapped_native.factory_array({concatenate},{Literal("SELECT ARRAY[NULL]::integer[],ARRAY[NULL]::integer[]")},{shaped})"));
        }

        Assert.AreEqual("0|0|0|0|0|0", await Scalar<string>(connection, "SELECT mapped_results.counts()"));
        PostgresException first = await Failure(connection, $"SELECT {Number(sum, "SELECT 0,0", 4)}", "P8512", "typed factory failed");
        Assert.AreEqual("lazy result", first.Detail);
        Assert.AreEqual("use another mapping", first.Hint);
        await Failure(connection, $"SELECT mapped_native.factory_array({concatenate},{Literal("SELECT ARRAY[0],NULL::integer[]")},false)",
            "P8512", "typed factory failed");
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT {Number(sum, "SELECT NULL::integer,0", 4)} IS NULL"));
        Assert.AreEqual("0|0|0|0|0|1", await Scalar<string>(connection, "SELECT mapped_results.counts()"));
        await Failure(connection, $"SELECT {Number(sum, "SELECT 0,0", 5)}", "38000", "ordinary typed factory failed");
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT {Number(sum, "SELECT NULL::integer,0", 5)} IS NULL"));
        await SameBackend(connection);
    }

    /// <summary>
    /// By-reference result text retains complete contents after input and result owner deletion and allocator activity.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeTextResultsOutliveSourceAndOperationOwners()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await Address(connection, "pg_catalog.textcat(text,text)");
        string expected = string.Concat(Enumerable.Repeat("héllo", 10000)) + " 🐘 tail";
        Assert.AreEqual(expected, await Scalar<string>(connection,
            $"SELECT mapped_native.text({address},{Literal("SELECT repeat('héllo',10000),' 🐘 tail'::text")})"));
        Assert.AreEqual("25", await Scalar<string>(connection, "SELECT mapped_native.captures(1)"));
        Assert.AreEqual("0|0|1|1|0|0", await Scalar<string>(connection, "SELECT mapped_results.counts()"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Reader diagnostics and rejected null results release temporary native text without poisoning the backend.
    /// </summary>
    /// <param name="value">The existing text reader's failure selector.</param>
    /// <param name="state">The expected SQLSTATE.</param>
    /// <param name="message">The complete expected message.</param>
    [TestMethod]
    [DataRow("reader-error", "P8511", "typed reader failed")]
    [DataRow("ordinary-error", "38000", "ordinary typed reader failed")]
    [DataRow("null-result", "38000", "A datum reader returned null for a present PostgreSQL value.")]
    public async Task MappedNativeTextReaderErrorsReleaseOwnersAndRecover(string value, string state, string message)
    {
        await using NpgsqlConnection connection = await Open();
        long address = await Address(connection, "pg_catalog.textcat(text,text)");
        PostgresException error = await Failure(connection,
            $"SELECT mapped_native.text({address},{Literal($"SELECT {Literal(value)},''::text")})", state, message);
        if (state == "P8511")
        {
            Assert.AreEqual("detached result", error.Detail);
            Assert.AreEqual("choose another result", error.Hint);
        }

        Assert.AreEqual("25", await Scalar<string>(connection, "SELECT mapped_native.captures(1)"));
        Assert.AreEqual("recovered", await Scalar<string>(connection,
            $"SELECT mapped_native.text({address},{Literal("SELECT 're','covered'::text")})"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Vectors select value, read-only alias and enum readers independently while retaining SQL NULL cells.
    /// </summary>
    /// <param name="kind">Value, read-only alias, or CLR enum reader.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task MappedNativeVectorsSelectExactElementReaders(int kind)
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        int shift = kind == 1 ? 100 : 0;
        Assert.AreSequenceEqual<int?>([shift, null, 42 + shift, -17 + shift], await Scalar<int?[]>(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT ARRAY[0,NULL],ARRAY[42,-17]")},{kind})"));
        if (kind == 0)
        {
            Assert.AreEqual("1|3|0|3", await Scalar<string>(connection, "SELECT mapped_native.array_counts()"));
            Assert.AreEqual("23,23,23", await Scalar<string>(connection, "SELECT mapped_native.captures(2)"));
        }

        Assert.IsEmpty(await Scalar<int?[]>(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT ARRAY[]::integer[],NULL::integer[]")},{kind})"));
        Assert.AreSequenceEqual<int?>([null, null], await Scalar<int?[]>(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT ARRAY[NULL]::integer[],ARRAY[NULL]::integer[]")},{kind})"));
        Assert.IsTrue(await Scalar<bool>(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT NULL::integer[],NULL::integer[]")},{kind}) IS NULL"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Shaped results preserve nominal elements, lower bounds and row-major cells; vectors reject lossy shapes before readers.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeArrayShapesRemainExact()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        const string shaped = "SELECT '[-2:-1][4:5]={{7,NULL},{0,-9}}'::integer[],NULL::integer[]";
        Assert.AreEqual("23|2|4|2,2|-2,4|7,NULL,0,-9", await Scalar<string>(connection,
            $"SELECT mapped_native.shape({address},{Literal(shaped)})"));
        Assert.AreEqual("23,23,23", await Scalar<string>(connection, "SELECT mapped_native.captures(2)"));
        Assert.AreEqual("23|0|0|||", await Scalar<string>(connection,
            $"SELECT mapped_native.shape({address},{Literal("SELECT ARRAY[]::integer[],ARRAY[]::integer[]")})"));
        Assert.AreEqual("23|1|2|2|1|NULL,NULL", await Scalar<string>(connection,
            $"SELECT mapped_native.shape({address},{Literal("SELECT ARRAY[NULL]::integer[],ARRAY[NULL]::integer[]")})"));
        Assert.AreEqual("NULL", await Scalar<string>(connection,
            $"SELECT mapped_native.shape({address},{Literal("SELECT NULL::integer[],NULL::integer[]")})"));
        foreach (string sql in new[] { shaped, "SELECT '[0:1]={7,11}'::integer[],NULL::integer[]" })
        {
            await Failure(connection, $"SELECT mapped_native.vector({address},{Literal(sql)},0)", "38000",
                "Use PgArray<T> to preserve dimensions and lower bounds, or ToArray() to explicitly flatten them.");
            Assert.AreEqual("1|0|0|0", await Scalar<string>(connection, "SELECT mapped_native.array_counts()"));
        }

        await SameBackend(connection);
    }

    /// <summary>
    /// Large text elements copied from an argument-returning native branch survive every source and extraction owner.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeReferenceArraysOutliveTheirOriginalStorage()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        await Execute(connection, """
            CREATE TEMP TABLE native_text_storage(value text[]);
            ALTER TABLE native_text_storage ALTER COLUMN value SET STORAGE EXTERNAL;
            INSERT INTO native_text_storage VALUES(ARRAY[repeat('héllo',10000),NULL,'','NULL','a,b{c}\d']);
            """);
        Assert.IsTrue(await Scalar<bool>(connection,
            "SELECT pg_relation_size(reltoastrelid)>0 FROM pg_class WHERE oid='native_text_storage'::regclass"));
        Assert.AreSequenceEqual<string?>([string.Concat(Enumerable.Repeat("héllo", 10000)), null, string.Empty, "NULL", "a,b{c}\\d"],
            await Scalar<string?[]>(connection,
                $"SELECT mapped_native.text_array({address},{Literal("SELECT NULL::text[],value FROM native_text_storage")})"));
        Assert.AreEqual("25,25,25,25", await Scalar<string>(connection, "SELECT mapped_native.captures(3)"));
        Assert.IsTrue(await Scalar<bool>(connection,
            $"SELECT mapped_native.text_array({address},{Literal("SELECT NULL::text[],NULL::text[]")}) IS NULL"));
        await SameBackend(connection);
    }

    /// <summary>
    /// A later failing array element releases earlier and failing readers' temporary handles and preserves diagnostics.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeArrayReaderErrorsReleaseEveryCapturedElement()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        PostgresException number = await Failure(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT ARRAY[7,NULL],ARRAY[-777,11]")},0)",
            "P8521", "mapped array reader failed");
        Assert.AreEqual("later element", number.Detail);
        Assert.AreEqual("replace the sentinel", number.Hint);
        Assert.AreEqual("1|2|0|2", await Scalar<string>(connection, "SELECT mapped_native.array_counts()"));
        Assert.AreEqual("23,23", await Scalar<string>(connection, "SELECT mapped_native.captures(2)"));
        PostgresException text = await Failure(connection,
            $"SELECT mapped_native.text_array({address},{Literal("SELECT ARRAY['kept',NULL],ARRAY['array-reader-error','unreached']")})",
            "P8523", "mapped text array reader failed");
        Assert.AreEqual("detached text", text.Detail);
        Assert.AreEqual("use another element", text.Hint);
        Assert.AreEqual("25,25", await Scalar<string>(connection, "SELECT mapped_native.captures(3)"));
        Assert.AreSequenceEqual<int?>([7, null, 11], await Scalar<int?[]>(connection,
            $"SELECT mapped_native.vector({address},{Literal("SELECT ARRAY[7,NULL],ARRAY[11]")},0)"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Explicit collation reaches a real native consumer, while native errors recover through the same pointer path.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeCallsForwardCollationAndRecoverFromNativeErrors()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await Address(connection, "pg_catalog.bttextcmp(text,text)");
        uint collation = await Scalar<uint>(connection, "SELECT 'pg_catalog.\"C\"'::regcollation::oid");
        Assert.IsLessThan(0, await Scalar<int>(connection, $"SELECT mapped_native.compare({address},{collation}::oid,{Literal("SELECT 'A'::text,'a'::text")})"));
        Assert.AreEqual(0, await Scalar<int>(connection, $"SELECT mapped_native.compare({address},{collation}::oid,{Literal("SELECT 'same'::text,'same'::text")})"));
        Assert.IsGreaterThan(0, await Scalar<int>(connection, $"SELECT mapped_native.compare({address},{collation}::oid,{Literal("SELECT 'a'::text,'A'::text")})"));
        PostgresException absent = await Failure(connection,
            $"SELECT mapped_native.compare({address},0::oid,{Literal("SELECT 'A'::text,'a'::text")})", "42P22",
            "could not determine which collation to use for string comparison");
        Assert.AreEqual("Use the COLLATE clause to set the collation explicitly.", absent.Hint);
        long division = await Address(connection, "pg_catalog.int4div(integer,integer)");
        await Failure(connection, $"SELECT {Number(division, "SELECT 7,0", 0)}", "22012", "division by zero");
        Assert.AreEqual(42, await Scalar<int>(connection, $"SELECT {Number(division, "SELECT 84,2", 0)}"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Writer-only array results reject NULL, empty and present values without inventing a reader.
    /// </summary>
    /// <param name="shaped">Whether to request PgArray rather than a CLR vector.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MappedNativeArraysRequireReadersBeforeConversion(bool shaped)
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        foreach (string sql in new[]
        {
            "SELECT NULL::integer[],NULL::integer[]",
            "SELECT ARRAY[]::integer[],ARRAY[]::integer[]",
            "SELECT ARRAY[NULL]::integer[],ARRAY[7]::integer[]",
        })
        {
            await Failure(connection, $"SELECT mapped_native.denied_array({address},{Literal(sql)},{shaped})", "38000",
                "The mapped PostgreSQL type has no datum reader.");
        }

        Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT mapped_native.array_counts()"));
        await SameBackend(connection);
    }

    /// <summary>
    /// Caught reader and cached factory failures retain completed transactional writes while preflight and stale inputs perform none.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeManagedFailuresRetainCompletedCatalogWrites()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await Address(connection, "pg_catalog.lo_create(oid)");
        uint owner = await Scalar<uint>(connection, "SELECT oid FROM pg_roles WHERE rolname=current_user");
        Dictionary<uint, string> original = await Metadata(connection);
        var owned = new List<uint>();
        try
        {
            uint sentinel = await Scalar<uint>(connection, "SELECT lo_create(0)");
            owned.Add(sentinel);
            await Execute(connection, $"SELECT lo_put({sentinel}::oid,0,decode('007fff00','hex'))");
            Dictionary<uint, string> baseline = await Metadata(connection);
            var reserved = new List<uint>();
            for (int index = 0; index < 6; index++)
            {
                uint oid = await Scalar<uint>(connection, "SELECT lo_create(0)");
                owned.Add(oid);
                reserved.Add(oid);
                Assert.AreEqual(1, await Scalar<int>(connection, $"SELECT lo_unlink({oid}::oid)"));
                Assert.IsFalse(baseline.ContainsKey(oid));
            }

            var created = new List<uint>();
            Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT mapped_native.counts()"));
            Assert.AreEqual("NotSupportedException|The mapped PostgreSQL type has no datum reader.",
                await Create(connection, address, reserved[0], 4));
            Assert.AreEqual("73", await Scalar<string>(connection, $"SELECT mapped_native.stale({address},{reserved[0]}::oid)"));
            await AssertMetadata(connection, baseline, created, owner);
            Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT mapped_native.counts()"));
            await Execute(connection, $"""
                CREATE DOMAIN pg_temp.native_argument AS oid;
                CREATE TEMP TABLE native_oid_source(value pg_temp.native_argument);
                INSERT INTO native_oid_source VALUES({reserved[0]}::oid);
                ALTER DOMAIN pg_temp.native_argument ADD CONSTRAINT excluded CHECK(VALUE<>{reserved[0]}::oid) NOT VALID;
                """);
            string domain = await Scalar<string>(connection, "SELECT 'pg_temp.native_argument'::regtype::text");
            await Failure(connection, $"SELECT mapped_native.create_from_sql({address},{Literal("SELECT value FROM native_oid_source")})",
                "23514", $"value for domain {domain} violates check constraint \"excluded\"");
            await AssertMetadata(connection, baseline, created, owner);
            Assert.AreEqual("0|0|0|0", await Scalar<string>(connection, "SELECT mapped_native.counts()"));

            Assert.AreEqual(reserved[0].ToString(CultureInfo.InvariantCulture), await Create(connection, address, reserved[0], 0));
            created.Add(reserved[0]);
            await AssertMetadata(connection, baseline, created, owner);
            Assert.AreEqual("26", await Scalar<string>(connection, "SELECT mapped_native.captures(4)"));
            string[] failures =
            [
                "P8531|native result reader failed|completed object creation|read another object",
                "FormatException|ordinary native result reader failed",
                "P8532|native result factory failed|completed object creation|use another reader",
                "P8532|native result factory failed|completed object creation|use another reader",
            ];
            for (int index = 0; index < failures.Length; index++)
            {
                Assert.AreEqual(failures[index], await Create(connection, address, reserved[index + 1], Math.Min(index + 1, 3)));
                created.Add(reserved[index + 1]);
                await AssertMetadata(connection, baseline, created, owner);
                Assert.AreSequenceEqual<byte>([0, 127, 255, 0], await Scalar<byte[]>(connection, $"SELECT lo_get({sentinel}::oid)"));
                Assert.AreEqual("26", await Scalar<string>(connection, "SELECT mapped_native.captures(4)"));
            }

            Assert.AreEqual("1|3|1|0", await Scalar<string>(connection, "SELECT mapped_native.counts()"));
            Assert.AreEqual(reserved[5].ToString(CultureInfo.InvariantCulture), await Create(connection, address, reserved[5], 0));
            created.Add(reserved[5]);
            await AssertMetadata(connection, baseline, created, owner);
            Assert.AreEqual("1|4|1|0", await Scalar<string>(connection, "SELECT mapped_native.counts()"));
            await SameBackend(connection);
        }
        finally
        {
            foreach (uint oid in owned)
            {
                await Execute(connection, $"SELECT lo_unlink(oid) FROM pg_largeobject_metadata WHERE oid={oid}::oid AND lomowner={owner}::oid");
            }
        }

        Dictionary<uint, string> final = await Metadata(connection);
        Assert.AreSequenceEqual(original.OrderBy(static pair => pair.Key), final.OrderBy(static pair => pair.Key));
    }

    /// <summary>
    /// Fresh calls resolve replacement domain-array identities without treating the native pointer as a catalog declaration.
    /// </summary>
    [TestMethod]
    public async Task MappedNativeArrayResultsRefreshCurrentExternalIdentities()
    {
        await using NpgsqlConnection connection = await Open();
        long address = await ArrayAddress(connection);
        await Execute(connection, "CREATE SCHEMA mapped_native_live; CREATE DOMAIN mapped_native_live.value AS integer");
        try
        {
            uint oldElement = await Scalar<uint>(connection, "SELECT 'mapped_native_live.value'::regtype::oid");
            uint oldArray = await Scalar<uint>(connection, "SELECT 'mapped_native_live.value[]'::regtype::oid");
            const string query = "SELECT ARRAY[7,NULL,11]::mapped_native_live.value[],NULL::mapped_native_live.value[]";
            Assert.AreEqual($"{oldElement}|1|3|3|1|7,NULL,11", await Scalar<string>(connection,
                $"SELECT mapped_native.live({address},{Literal(query)})"));
            Assert.AreEqual(oldElement, await Scalar<uint>(connection, "SELECT mapped_native.live_oid()"));
            await Execute(connection, "DROP DOMAIN mapped_native_live.value");
            await Failure(connection, $"SELECT mapped_native.live({address},{Literal("SELECT NULL::integer[],NULL::integer[]")})", "42704",
                "PostgreSQL concrete defined type \"value\" does not exist in the declared schema");
            await Execute(connection, "CREATE DOMAIN mapped_native_live.value AS integer CHECK(VALUE>0)");
            uint element = await Scalar<uint>(connection, "SELECT 'mapped_native_live.value'::regtype::oid");
            uint array = await Scalar<uint>(connection, "SELECT 'mapped_native_live.value[]'::regtype::oid");
            Assert.AreNotEqual(oldElement, element);
            Assert.AreNotEqual(oldArray, array);
            Assert.AreEqual($"{element}|1|3|3|1|7,NULL,11", await Scalar<string>(connection,
                $"SELECT mapped_native.live({address},{Literal(query)})"));
            Assert.AreEqual(element, await Scalar<uint>(connection, "SELECT mapped_native.live_oid()"));
            Assert.AreEqual($"{element}|0|0|||", await Scalar<string>(connection,
                $"SELECT mapped_native.live({address},{Literal("SELECT ARRAY[]::mapped_native_live.value[],NULL::mapped_native_live.value[]")})"));
            Assert.AreEqual("NULL", await Scalar<string>(connection,
                $"SELECT mapped_native.live({address},{Literal("SELECT NULL::mapped_native_live.value[],NULL::mapped_native_live.value[]")})"));
            await SameBackend(connection);
        }
        finally
        {
            await Execute(connection, "DROP SCHEMA mapped_native_live CASCADE");
        }
    }

    /// <summary>
    /// Compares complete preexisting metadata and every explicitly requested created identity and empty payload.
    /// </summary>
    private async Task AssertMetadata(NpgsqlConnection connection, Dictionary<uint, string> baseline, List<uint> created, uint owner)
    {
        Dictionary<uint, string> actual = await Metadata(connection);
        Assert.AreSequenceEqual(baseline.Keys.Concat(created).Order(), actual.Keys.Order());
        foreach (KeyValuePair<uint, string> row in baseline)
        {
            Assert.AreEqual(row.Value, actual[row.Key]);
        }

        foreach (uint oid in created)
        {
            Assert.AreEqual(owner, await Scalar<uint>(connection, $"SELECT lomowner FROM pg_largeobject_metadata WHERE oid={oid}::oid"));
            Assert.IsEmpty(await Scalar<byte[]>(connection, $"SELECT lo_get({oid}::oid)"));
        }
    }

    /// <summary>
    /// Snapshots the complete large-object metadata row set without deleting globally discovered objects.
    /// </summary>
    private async Task<Dictionary<uint, string>> Metadata(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT oid,lomowner::text || '|' || coalesce(lomacl::text,'NULL') FROM pg_largeobject_metadata ORDER BY oid", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        var values = new Dictionary<uint, string>();
        while (await reader.ReadAsync(context.CancellationToken))
        {
            values.Add(reader.GetFieldValue<uint>(0), reader.GetString(1));
        }

        return values;
    }

    /// <summary>
    /// Formats the exact mapped-number fixture call without a second native evaluation.
    /// </summary>
    private static string Number(long address, string sql, int kind)
        => string.Create(CultureInfo.InvariantCulture, $"mapped_native.number({address},{Literal(sql)},{kind})");

    /// <summary>
    /// Calls a verified metadata-independent native large-object constructor with its genuine OID argument.
    /// </summary>
    private Task<string> Create(NpgsqlConnection connection, long address, uint requested, int mode)
        => Scalar<string>(connection, $"SELECT mapped_native.create_object({address},{requested}::oid,{mode})");

    /// <summary>
    /// Resolves an existing native address through the shared fixture's fmgr lookup.
    /// </summary>
    private Task<long> Address(NpgsqlConnection connection, string signature)
        => Scalar<long>(connection, $"SELECT tests.function_address({Literal(signature)}::regprocedure)");

    /// <summary>
    /// Finds the unique polymorphic array_cat identity across anyarray and anycompatiblearray PostgreSQL versions.
    /// </summary>
    private async Task<long> ArrayAddress(NpgsqlConnection connection)
    {
        long[] addresses = await Scalar<long[]>(connection, """
            SELECT ARRAY(SELECT tests.function_address(p.oid::regprocedure)
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='pg_catalog' AND p.proname='array_cat' AND p.pronargs=2
              AND p.proargtypes[0]=p.proargtypes[1] AND p.proargtypes[0]=p.prorettype)
            """);
        Assert.HasCount(1, addresses);
        return addresses[0];
    }

    /// <summary>
    /// Requires exact native diagnostics rather than accepting connection loss or arbitrary exceptions.
    /// </summary>
    private async Task<PostgresException> Failure(NpgsqlConnection connection, string sql, string state, string message)
    {
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Execute(connection, sql));
        Assert.AreEqual(state, error.SqlState);
        Assert.AreEqual(message, error.MessageText);
        return error;
    }

    /// <summary>
    /// Confirms the original backend is still responsive after native and managed conversion failures.
    /// </summary>
    private async Task SameBackend(NpgsqlConnection connection)
    {
        Assert.AreEqual(73, await Scalar<int>(connection, "SELECT 73"));
        Assert.AreEqual(connection.ProcessID, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Opens an independent backend for static converter and object-lifetime observations.
    /// </summary>
    private Task<NpgsqlConnection> Open() => PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);

    /// <summary>
    /// Quotes fixture-controlled text without evaluating it as part of call argument setup.
    /// </summary>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Decodes the requested nullable-array representation directly through Npgsql.
    /// </summary>
    private async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(context.CancellationToken);
        Assert.IsTrue(await reader.ReadAsync(context.CancellationToken));
        return await reader.GetFieldValueAsync<T>(0, context.CancellationToken);
    }

    /// <summary>
    /// Drains setup, cleanup and expected-error statements.
    /// </summary>
    private async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
