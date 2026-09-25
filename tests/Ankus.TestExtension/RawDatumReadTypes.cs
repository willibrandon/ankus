using Ankus;
using Ankus.TestExtension;

[assembly: PgSql("raw-read-types", """
    CREATE DOMAIN raw_read_domains.number AS integer;
    CREATE DOMAIN raw_read_domains.vector AS integer[];
    CREATE FUNCTION raw_read_domains.count_number(value integer) RETURNS boolean LANGUAGE plpgsql VOLATILE AS $f$
    BEGIN PERFORM nextval('raw_read_checks'); RETURN true; END $f$;
    CREATE FUNCTION raw_read_domains.count_vector(value integer[]) RETURNS boolean LANGUAGE plpgsql VOLATILE AS $f$
    BEGIN PERFORM nextval('raw_vector_checks'); RETURN true; END $f$;
    """, Requires = ["raw-read-schema"])]
[assembly: PgSqlTypeProvider("raw-read-types", typeof(RawReadNumber))]
[assembly: PgSqlTypeProvider("raw-read-types", "number", Schema = "raw_read_domains")]

namespace Ankus.TestExtension;

/// <summary>
/// Selects native integer decoding while preserving a dedicated domain's exact identity.
/// </summary>
/// <param name="Value">The independently known integer.</param>
/// <param name="ReturnNull">Whether a present writer deliberately returns typed SQL NULL.</param>
[PgDatumType("number", typeof(RawReadNumberConverter), Schema = "raw_read_domains")]
public readonly record struct RawReadNumber(int Value, bool ReturnNull = false);

/// <summary>
/// Exposes repeated domain evaluation through native reads without bypassing it through raw-bit interpretation.
/// </summary>
public sealed class RawReadNumberConverter : IPgDatumReader<RawReadNumber>, IPgDatumWriter<RawReadNumber>
{
    /// <summary>
    /// Counts only actually present mapped reads.
    /// </summary>
    public static int Reads { get; private set; }

    /// <inheritdoc />
    public RawReadNumber Read(PgDatum value)
    {
        Reads++;
        return new RawReadNumber(value.Read<int>());
    }

    /// <inheritdoc />
    public PgDatum Write(RawReadNumber value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(unchecked((nuint)value.Value), typeOid, destination, value.ReturnNull);
}
