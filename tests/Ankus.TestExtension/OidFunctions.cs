namespace Ankus.TestExtension;

/// <summary>
/// Exercises typed OID classification and explicit datum conversion inside Native AOT PostgreSQL callbacks.
/// </summary>
[PgSchema("oid_catalog")]
public static class OidFunctions
{
    /// <summary>
    /// Reports every active-version member through the AOT-compiled enum and native-header version lookup.
    /// </summary>
    [PgFunction]
    public static IEnumerable<(uint value, string name)> Entries()
    {
        foreach (PgBuiltInOid value in PgBuiltInOids.GetValues())
        {
            yield return ((uint)value, PgBuiltInOids.GetNativeName(value)!);
        }
    }

    /// <summary>
    /// Classifies an input without querying the catalog for existence.
    /// </summary>
    [PgFunction]
    public static string Describe(uint? value)
    {
        if (!value.HasValue)
        {
            return "null";
        }

        PgOid oid = PgOid.FromValue(value.Value);
        return $"{oid.Kind}|{oid}|{(oid.BuiltIn is { } builtIn ? PgBuiltInOids.GetNativeName(builtIn) : "")}";
    }

    /// <summary>
    /// Returns constants from several independent object catalogs.
    /// </summary>
    [PgFunction]
    public static uint[] KnownValues() =>
    [
        (uint)PgBuiltInOid.BoolOid, (uint)PgBuiltInOid.Int4Oid, (uint)PgBuiltInOid.Int4ArrayOid,
        (uint)PgBuiltInOid.MoneyOid, (uint)PgBuiltInOid.PgNodeTreeOid, (uint)PgBuiltInOid.RelationRelationId,
        (uint)PgBuiltInOid.ProcedureRelationId, (uint)PgBuiltInOid.FunctionHashOid, (uint)PgBuiltInOid.BTreeAmOid,
        (uint)PgBuiltInOid.CCollationOid, (uint)PgBuiltInOid.TextBTreeOpsOid, (uint)PgBuiltInOid.IntegerBTreeFamOid,
    ];

    /// <summary>
    /// Returns a tagged oid datum, retaining the distinction between Invalid and Custom(0).
    /// </summary>
    [PgFunction]
    [return: PgSqlType("oid", Schema = "pg_catalog")]
    public static PgDatum AsDatum(uint value, bool custom)
        => (custom ? PgOid.Custom(value) : PgOid.FromValue(value)).ToDatum(PgMemoryContext.Current);

    /// <summary>
    /// Verifies full unsigned datum words are rejected without wrapping to built-in values.
    /// </summary>
    [PgFunction]
    public static string Oversized()
    {
        bool success = PgBuiltInOids.TryFromValue((1UL << 32) + 23, out PgBuiltInOid value, out PgOidLookupError error);
        return $"{success}|{(uint)value}|{error}";
    }

    /// <summary>
    /// Rejects invalid built-in construction through managed catch/finally, then executes SPI in the same backend.
    /// </summary>
    [PgFunction]
    public static string RejectBuiltIn(uint value)
    {
        string? parameter = null;
        bool finalized;
        try
        {
            _ = PgOid.FromBuiltIn((PgBuiltInOid)value);
        }
        catch (ArgumentOutOfRangeException error)
        {
            parameter = error.ParamName;
        }
        finally
        {
            finalized = true;
        }

        return $"{parameter}|{finalized}|{Spi.ExecuteScalar<int>("SELECT 42")}";
    }

    /// <summary>
    /// Passes created datums through SPI and observes invalidation after resetting the explicit owner.
    /// </summary>
    [PgFunction]
    public static string DatumLifetime()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("oid-datum-owner");
        PgDatum invalid = PgOid.Invalid.ToDatum(owner);
        PgDatum zero = PgOid.Custom(0).ToDatum(owner);
        PgDatum maximum = PgOid.Custom(uint.MaxValue).ToDatum(owner);
        uint? absent = Spi.ExecuteScalar<uint?>("SELECT $1::oid", SpiParameter.Create(invalid));
        uint present = Spi.ExecuteScalar<uint>("SELECT $1::oid", SpiParameter.Create(zero));
        uint high = Spi.ExecuteScalar<uint>("SELECT $1::oid", SpiParameter.Create(maximum));
        owner.Reset();
        bool expired = false;
        try
        {
            _ = maximum.DangerousGetBits();
        }
        catch (ObjectDisposedException)
        {
            expired = true;
        }

        return $"{absent.HasValue}|{present}|{high}|{invalid.TypeOid}|{zero.IsNull}|{expired}|{Spi.ExecuteScalar<int>("SELECT 42")}";
    }
}
