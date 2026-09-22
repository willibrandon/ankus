namespace Ankus.TestExtension;

/// <summary>
/// Exercises enums across generated functions and SPI without relying on the backend search path.
/// </summary>
[PgSchema("enum_values", Id = "enum.schema")]
public static class EnumFunctions
{
    /// <summary>
    /// A distinct type with matching labels in a fixed schema.
    /// </summary>
    [PgEnum(Name = "other_mood")]
    public enum OtherMood : byte
    {
        /// <summary>
        /// Retains a byte enum's identity separately from bytea.
        /// </summary>
        Low = 1,

        /// <summary>
        /// A second label.
        /// </summary>
        High = 255,
    }

    /// <summary>
    /// An enum used through SPI only, allowing catalog recreation without dropping functions.
    /// </summary>
    [PgEnum(Name = "ephemeral_mood")]
    public enum EphemeralMood
    {
        /// <summary>
        /// A single stable label over changing catalog identities.
        /// </summary>
        Value,
    }

    /// <summary>
    /// Exchanges nullable scalars through eight ownership paths.
    /// </summary>
    [PgFunction]
    public static EnumMood? EnumScalar(EnumMood? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shape-preserving nullable enum arrays.
    /// </summary>
    [PgFunction]
    public static PgArray<EnumMood?>? EnumArray(PgArray<EnumMood?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable enum vectors.
    /// </summary>
    [PgFunction]
    public static EnumMood?[]? EnumVector(EnumMood?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges required enum vectors without underlying-integer reinterpretation.
    /// </summary>
    [PgFunction]
    public static EnumMood[] EnumRequired(EnumMood[] value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges byte-backed enums in vectors without treating them as bytea.
    /// </summary>
    [PgFunction]
    public static OtherMood[] EnumBytes(OtherMood[] value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Selects a declared C# optional enum default.
    /// </summary>
    [PgFunction]
    public static EnumMood EnumDefault(EnumMood value = EnumMood.Medium) => value;

    /// <summary>
    /// Converts variadic enum arguments into an array result.
    /// </summary>
    [PgFunction]
    public static EnumMood?[] EnumVariadic(params EnumMood?[] values) => values;

    /// <summary>
    /// Exposes the managed numeric value independently of the label writer.
    /// </summary>
    [PgFunction]
    public static long EnumNumber(EnumMood value) => (long)value;

    /// <summary>
    /// Tests undefined managed enum values on the output boundary.
    /// </summary>
    [PgFunction]
    public static EnumMood EnumUndefined() => (EnumMood)12345;

    /// <summary>
    /// Returns an owned query value after releasing SPI buffers and running another query.
    /// </summary>
    [PgFunction]
    public static EnumMood? EnumQuery(string query)
    {
        EnumMood? result = Spi.Query(query)[0].Get<EnumMood?>(0);
        Spi.Execute("SELECT repeat('replacement', 20000)");
        return result;
    }

    /// <summary>
    /// Returns a shaped owned query result, including arrays of enum domains.
    /// </summary>
    [PgFunction]
    public static PgArray<EnumMood?>? EnumArrayQuery(string query)
    {
        PgArray<EnumMood?>? result = Spi.Query(query)[0].Get<PgArray<EnumMood?>?>(0);
        Spi.Execute("SELECT repeat('replacement', 20000)");
        return result;
    }

    /// <summary>
    /// Rejects a distinct enum result even when its label matches.
    /// </summary>
    [PgFunction]
    public static EnumMood EnumWrongType() => Spi.ExecuteScalar<EnumMood>("SELECT 'Low'::enum_values.other_mood");

    /// <summary>
    /// Resolves enum type identity in the extension installation schema.
    /// </summary>
    [PgFunction]
    public static uint EnumOid() => PgEnums.GetTypeOid<EnumMood>();

    /// <summary>
    /// Reads the dynamically recreated enum and its arrays through SPI.
    /// </summary>
    [PgFunction]
    public static uint EnumEphemeralOid()
    {
        _ = Spi.ExecuteScalar<EphemeralMood>("SELECT $1", SpiParameter.Create(EphemeralMood.Value));
        _ = Spi.ExecuteScalar<EphemeralMood[]>("SELECT $1", SpiParameter.Create(new[] { EphemeralMood.Value }));
        return PgEnums.GetTypeOid<EphemeralMood>();
    }

    /// <summary>
    /// Preserves writes and plans through managed and native enum failures in one backend scope.
    /// </summary>
    [PgFunction]
    public static string EnumRecovery() => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE enum_writes(value int); INSERT INTO enum_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM enum_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int failures = 0;
        int finalized = 0;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                session.Execute("SELECT 'unknown'::datatype.enum_mood");
            }
            catch (PgException error) when (error.SqlState == "22P02")
            {
                failures++;
            }
            finally
            {
                finalized++;
            }

            try
            {
                _ = session.ExecuteScalar<EnumMood>("SELECT $1", SpiParameter.Create((EnumMood)12345));
            }
            catch (ArgumentException)
            {
                failures++;
            }

            if (session.ExecuteScalar<EnumMood>("SELECT $1", SpiParameter.Create(EnumMood.Cafe)) != EnumMood.Cafe)
            {
                throw new InvalidOperationException("Enum conversion failed after recovery.");
            }
        }

        session.Execute("INSERT INTO enum_writes VALUES (2)");
        return $"{failures}:{finalized}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });
    /// <summary>
    /// Converts a detached label to managed state before PostgreSQL performs the output lookup.
    /// </summary>
    [PgFunction]
    public static EnumMood EnumFromLabel(string label) => PgEnums.Parse<EnumMood>(label);

    /// <summary>
    /// Resolves the enum datum OID rather than its underlying C# numeric value.
    /// </summary>
    [PgFunction]
    public static uint EnumValueOid(EnumMood value) => PgEnums.GetValueOid(value);

    /// <summary>
    /// Copies catalog fields, then releases and replaces all native query storage before returning them.
    /// </summary>
    [PgFunction]
    public static string EnumCatalog(uint oid)
    {
        PgEnumInfo info = PgEnums.Lookup(oid);
        Spi.Execute("SELECT repeat('replacement', 20000)");
        return System.FormattableString.Invariant($"{info.Label}:{info.TypeOid}:{info.ValueOid}:{info.SortOrder}");
    }

    /// <summary>
    /// Recovers inside one managed callback from missing labels and invalid catalog OIDs.
    /// </summary>
    [PgFunction]
    public static string EnumCatalogRecovery()
    {
        int failures = 0;
        for (int i = 0; i < 20; i++)
        {
            try { _ = PgEnums.GetValueOid(EnumMood.Low); }
            catch (PgException error) when (error.SqlState == "22P02") { failures++; }

            try { _ = PgEnums.Lookup(0); }
            catch (PgException error) when (error.SqlState == "22P03") { failures++; }
        }

        return $"{failures}:{PgEnums.Lookup(PgEnums.GetValueOid(EnumMood.Medium)).Label}";
    }

    /// <summary>
    /// Materializes multiple enum columns and rows, including a leading NULL, without mixing cached mappings.
    /// </summary>
    [PgFunction]
    public static bool EnumMultipleColumns()
    {
        SpiResult result = Spi.Query("""
            SELECT *, INTERVAL '-3 days', '[1,2)'::int4range FROM (VALUES
                (1, NULL::datatype.enum_mood, 'High'::enum_values.other_mood),
                (2, 'Medium'::datatype.enum_mood, 'Low'::enum_values.other_mood),
                (3, 'High'::datatype.enum_mood, NULL::enum_values.other_mood)) AS r(n,a,b) ORDER BY n
            """);
        Spi.Execute("SELECT repeat('replacement', 10000)");
        return result.Count == 3 && result[0].Get<PgInterval>(3).Days == -3 && result[0].Get<PgRange<int>>(4).Lower == 1 &&
            result[0].Get<EnumMood?>(1) is null && result[0].Get<OtherMood>(2) == OtherMood.High &&
            result[1].Get<EnumMood>(1) == EnumMood.Medium && result[1].Get<OtherMood>(2) == OtherMood.Low &&
            result[2].Get<EnumMood>(1) == EnumMood.High && result[2].Get<OtherMood?>(2) is null;
    }

    /// <summary>
    /// Restores the outer enum lookup context after successful and throwing recursive managed callbacks.
    /// </summary>
    [PgFunction]
    public static bool EnumRecursive(EnumMood value)
    {
        uint before = PgEnums.GetTypeOid<EnumMood>();
        EnumMood nested = Spi.ExecuteScalar<EnumMood>("SELECT enum_values.enum_scalar($1,1)", SpiParameter.Create(value));
        bool caught = false;
        try { Spi.Execute("SELECT enum_values.enum_undefined()"); }
        catch (PgException error) when (error.SqlState == "38000") { caught = true; }

        return caught && nested == value && PgEnums.GetTypeOid<EnumMood>() == before &&
            PgEnums.Lookup(PgEnums.GetValueOid(value)).Label == PgEnums.GetLabel(value);
    }
}
