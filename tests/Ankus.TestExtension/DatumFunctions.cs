namespace Ankus.TestExtension;

/// <summary>
/// Provides managed functions executed by PostgreSQL for datum conversion and native-boundary integration tests.
/// </summary>
public static class DatumFunctions
{
    /// <summary>
    /// Returns a Boolean unchanged.
    /// </summary>
    /// <param name="value">The SQL Boolean.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static bool EchoBoolean(bool value) => value;

    /// <summary>
    /// Returns PostgreSQL's signed internal char unchanged.
    /// </summary>
    /// <param name="value">The SQL internal char.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static sbyte EchoChar(sbyte value) => value;

    /// <summary>
    /// Returns a smallint unchanged.
    /// </summary>
    /// <param name="value">The SQL smallint.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static short EchoSmallInt(short value) => value;

    /// <summary>
    /// Returns an integer unchanged, sharing its SQL name with the bigint overload.
    /// </summary>
    /// <param name="value">The SQL integer.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static int Echo(int value) => value;

    /// <summary>
    /// Returns a bigint unchanged, sharing its SQL name with the integer overload.
    /// </summary>
    /// <param name="value">The SQL bigint.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static long Echo(long value) => value;

    /// <summary>
    /// Returns an unsigned PostgreSQL OID unchanged.
    /// </summary>
    /// <param name="value">The SQL OID.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static uint EchoOid(uint value) => value;

    /// <summary>
    /// Returns a real unchanged.
    /// </summary>
    /// <param name="value">The SQL real.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static float EchoReal(float value) => value;

    /// <summary>
    /// Returns a double precision value unchanged.
    /// </summary>
    /// <param name="value">The SQL double precision value.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static double EchoDouble(double value) => value;

    /// <summary>
    /// Returns a nullable integer unchanged.
    /// </summary>
    /// <param name="value">The SQL integer or NULL.</param>
    /// <returns>The supplied value.</returns>
    [PgFunction]
    public static int? EchoOptional(int? value) => value;

    /// <summary>
    /// Evaluates a mixed nullable and required parameter contract in managed code.
    /// </summary>
    /// <param name="value">An optional integer.</param>
    /// <param name="fallback">The required fallback.</param>
    /// <returns>The supplied value or fallback.</returns>
    [PgFunction]
    public static int Coalesce(int? value, int fallback) => value ?? fallback;

    /// <summary>
    /// Returns copied text unchanged.
    /// </summary>
    /// <param name="value">The SQL text.</param>
    /// <returns>The supplied text.</returns>
    [PgFunction]
    public static string EchoText(string value) => value;

    /// <summary>
    /// Returns nullable text unchanged.
    /// </summary>
    /// <param name="value">The SQL text or NULL.</param>
    /// <returns>The supplied text.</returns>
    [PgFunction]
    public static string? EchoOptionalText(string? value) => value;

    /// <summary>
    /// Distinguishes SQL NULL from an empty string by invoking managed code.
    /// </summary>
    /// <param name="value">The SQL text or NULL.</param>
    /// <returns>A managed substitute for NULL.</returns>
    [PgFunction]
    public static string TextOrDefault(string? value) => value ?? "default";

    /// <summary>
    /// Combines nullable and required text arguments to exercise multiple native buffer slots.
    /// </summary>
    /// <param name="left">The optional prefix.</param>
    /// <param name="right">The required suffix.</param>
    /// <returns>The concatenated text.</returns>
    [PgFunction]
    public static string Concatenate(string? left, string right) => (left ?? "<null>") + right;

    /// <summary>
    /// Returns copied binary bytes unchanged.
    /// </summary>
    /// <param name="value">The SQL bytea value.</param>
    /// <returns>The supplied bytes.</returns>
    [PgFunction]
    public static byte[] EchoBytes(byte[] value) => value;

    /// <summary>
    /// Returns nullable binary bytes unchanged.
    /// </summary>
    /// <param name="value">The SQL bytea value or NULL.</param>
    /// <returns>The supplied bytes.</returns>
    [PgFunction]
    public static byte[]? EchoOptionalBytes(byte[]? value) => value;

    /// <summary>
    /// Returns text that cannot be represented in a LATIN1 database, exercising native error cleanup.
    /// </summary>
    /// <returns>A supplementary Unicode character.</returns>
    [PgFunction]
    public static string UnicodeText() => "🐘";

    /// <summary>
    /// Returns an invalid text value to exercise managed output validation.
    /// </summary>
    /// <returns>A string containing a zero character.</returns>
    [PgFunction]
    public static string InvalidText() => "before\0after";

    /// <summary>
    /// Returns an unpaired UTF-16 surrogate to exercise strict UTF-8 encoding.
    /// </summary>
    /// <returns>An invalid Unicode string.</returns>
    [PgFunction]
    public static string InvalidSurrogate() => "\uD800";

    /// <summary>
    /// Executes a method with no SQL result payload.
    /// </summary>
    [PgFunction]
    public static void Nothing()
    {
    }
}
