using System.Text;

namespace Ankus.TestExtension;

/// <summary>
/// Declares one identity function per supported managed type so tests can read the installed catalog signatures, as
/// pgrx's <c>sql_translatable_signature</c> matrix does.
/// </summary>
[PgSchema("signatures")]
public static class SignatureFunctions
{
    /// <summary>
    /// Returns a boolean.
    /// </summary>
    [PgFunction]
    public static bool SignatureBool(bool value) => value;

    /// <summary>
    /// Returns a signed byte.
    /// </summary>
    [PgFunction]
    public static sbyte SignatureSbyte(sbyte value) => value;

    /// <summary>
    /// Returns a 16-bit integer.
    /// </summary>
    [PgFunction]
    public static short SignatureShort(short value) => value;

    /// <summary>
    /// Returns a 32-bit integer.
    /// </summary>
    [PgFunction]
    public static int SignatureInt(int value) => value;

    /// <summary>
    /// Returns a 64-bit integer.
    /// </summary>
    [PgFunction]
    public static long SignatureLong(long value) => value;

    /// <summary>
    /// Returns an object identifier.
    /// </summary>
    [PgFunction]
    public static uint SignatureUint(uint value) => value;

    /// <summary>
    /// Returns a single-precision number.
    /// </summary>
    [PgFunction]
    public static float SignatureFloat(float value) => value;

    /// <summary>
    /// Returns a double-precision number.
    /// </summary>
    [PgFunction]
    public static double SignatureDouble(double value) => value;

    /// <summary>
    /// Returns text.
    /// </summary>
    [PgFunction]
    public static string SignatureString(string value) => value;

    /// <summary>
    /// Returns one character.
    /// </summary>
    [PgFunction]
    public static Rune SignatureRune(Rune value) => value;

    /// <summary>
    /// Returns bytes.
    /// </summary>
    [PgFunction]
    public static byte[] SignatureBytes(byte[] value) => value;

    /// <summary>
    /// Returns an integer vector.
    /// </summary>
    [PgFunction]
    public static int[] SignatureIntVector(int[] value) => value;

    /// <summary>
    /// Returns a nullable integer.
    /// </summary>
    [PgFunction]
    public static int? SignatureNullableInt(int? value) => value;

    /// <summary>
    /// Returns a date.
    /// </summary>
    [PgFunction]
    public static PgDate SignatureDate(PgDate value) => value;

    /// <summary>
    /// Returns a time.
    /// </summary>
    [PgFunction]
    public static PgTime SignatureTime(PgTime value) => value;

    /// <summary>
    /// Returns a time with an offset.
    /// </summary>
    [PgFunction]
    public static PgTimeTz SignatureTimeTz(PgTimeTz value) => value;

    /// <summary>
    /// Returns a timestamp.
    /// </summary>
    [PgFunction]
    public static PgTimestamp SignatureTimestamp(PgTimestamp value) => value;

    /// <summary>
    /// Returns a timestamp with time zone.
    /// </summary>
    [PgFunction]
    public static PgTimestampTz SignatureTimestampTz(PgTimestampTz value) => value;

    /// <summary>
    /// Returns an interval.
    /// </summary>
    [PgFunction]
    public static PgInterval SignatureInterval(PgInterval value) => value;

    /// <summary>
    /// Returns unconstrained numeric.
    /// </summary>
    [PgFunction]
    public static PgNumeric SignatureNumeric(PgNumeric value) => value;

    /// <summary>
    /// Returns numeric(10, 2), whose modifier PostgreSQL does not keep in function signatures.
    /// </summary>
    [PgFunction]
    [return: PgNumericPrecision(10, 2)]
    public static PgNumeric SignatureNumeric10And2([PgNumericPrecision(10, 2)] PgNumeric value) => value;

    /// <summary>
    /// Returns a numeric vector.
    /// </summary>
    [PgFunction]
    public static decimal?[] SignatureNumericVector(decimal?[] value) => value;

    /// <summary>
    /// Returns JSON.
    /// </summary>
    [PgFunction]
    public static PgJson SignatureJson(PgJson value) => value;

    /// <summary>
    /// Returns JSONB.
    /// </summary>
    [PgFunction]
    public static PgJsonb SignatureJsonb(PgJsonb value) => value;

    /// <summary>
    /// Returns a UUID.
    /// </summary>
    [PgFunction]
    public static Guid SignatureUuid(Guid value) => value;

    /// <summary>
    /// Returns a network address.
    /// </summary>
    [PgFunction]
    public static PgInet SignatureInet(PgInet value) => value;

    /// <summary>
    /// Returns a point.
    /// </summary>
    [PgFunction]
    public static PgPoint SignaturePoint(PgPoint value) => value;

    /// <summary>
    /// Returns backend-owned state.
    /// </summary>
    [PgFunction]
    public static PgInternal SignatureInternal(PgInternal value) => value;

    /// <summary>
    /// Returns a 32-bit integer range.
    /// </summary>
    [PgFunction]
    public static PgRange<int> SignatureRangeInt(PgRange<int> value) => value;

    /// <summary>
    /// Returns a 64-bit integer range.
    /// </summary>
    [PgFunction]
    public static PgRange<long> SignatureRangeLong(PgRange<long> value) => value;

    /// <summary>
    /// Returns a numeric range.
    /// </summary>
    [PgFunction]
    public static PgRange<PgNumeric> SignatureRangeNumeric(PgRange<PgNumeric> value) => value;

    /// <summary>
    /// Returns a date range.
    /// </summary>
    [PgFunction]
    public static PgRange<PgDate> SignatureRangeDate(PgRange<PgDate> value) => value;

    /// <summary>
    /// Returns a timestamp range.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestamp> SignatureRangeTimestamp(PgRange<PgTimestamp> value) => value;

    /// <summary>
    /// Returns a timestamp-with-time-zone range.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestampTz> SignatureRangeTimestampTz(PgRange<PgTimestampTz> value) => value;

    /// <summary>
    /// Returns a generated enum.
    /// </summary>
    [PgFunction(Requires = ["enum.mood"])]
    public static EnumMood SignatureEnum(EnumMood value) => value;

    /// <summary>
    /// Returns a generated custom type.
    /// </summary>
    [PgFunction(Requires = ["custom.number"])]
    public static CustomTypeFunctions.Number SignatureCustom(CustomTypeFunctions.Number value) => value;

    /// <summary>
    /// Returns one integer row.
    /// </summary>
    [PgFunction]
    public static IEnumerable<int> SignatureSetOf()
    {
        yield return 1;
    }

    /// <summary>
    /// Returns one integer column in a table.
    /// </summary>
    [PgFunction]
    [return: PgColumnNames("id")]
    public static IEnumerable<int> SignatureTable()
    {
        yield return 1;
    }
}
