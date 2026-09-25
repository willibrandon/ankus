using System.Globalization;
using System.Runtime.InteropServices;
using Ankus;

[assembly: PgSql("declaration.manual-enum", "CREATE TYPE declaration_sql.manual_mood AS ENUM('Blue','Red');", Requires = ["declaration.manual-enum-anchor"])]
[assembly: PgSql("declaration.hidden-aggregate", """
    CREATE AGGREGATE declaration_sql.visible_total(integer)
        (SFUNC=declaration_sql.hidden_step,STYPE=integer,INITCOND='0');
    """, Requires = ["declaration.hidden-parent"])]
[assembly: PgSql("declaration.supplied-helper", """
    CREATE FUNCTION declaration_sql.supplied_step(integer,integer) RETURNS integer
        LANGUAGE SQL IMMUTABLE STRICT AS 'SELECT $1+$2+100';
    """, Requires = ["declaration.supplied-helper-anchor"], Before = ["declaration.supplied-parent"])]

namespace Ankus.TestExtension;

/// <summary>
/// Makes each declaration's SQL ownership boundary observable through retained native contracts.
/// </summary>
[PgSchema("declaration_sql")]
public static class DeclarationSqlFunctions
{
    private static int s_textFinally;
    private static int s_aggregateFinally;

    /// <summary>
    /// Uses consumer-named text and binary I/O with generated JSON and CBOR.
    /// </summary>
    /// <param name="Number">The exact stored number.</param>
    [PgType(Name = "json_value", BinaryProtocol = true, Sql = """
        CREATE TYPE declaration_sql.json_value;
        CREATE FUNCTION declaration_sql.json_input(cstring) RETURNS declaration_sql.json_value AS '@MODULE_PATHNAME@','@INPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.json_output(declaration_sql.json_value) RETURNS cstring AS '@MODULE_PATHNAME@','@OUTPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.json_receive(internal) RETURNS declaration_sql.json_value AS '@MODULE_PATHNAME@','@RECEIVE_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.json_send(declaration_sql.json_value) RETURNS bytea AS '@MODULE_PATHNAME@','@SEND_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE TYPE declaration_sql.json_value(INTERNALLENGTH=variable,INPUT=declaration_sql.json_input,OUTPUT=declaration_sql.json_output,RECEIVE=declaration_sql.json_receive,SEND=declaration_sql.json_send,ALIGNMENT=int4,STORAGE=extended);
        """)]
    public readonly record struct JsonValue(int Number);

    /// <summary>
    /// Keeps a custom text surface independent of generated CBOR storage.
    /// </summary>
    /// <param name="Number">The exact stored number.</param>
    [PgType(Name = "text_value", TextCodec = typeof(NumberText), BinaryProtocol = true, Sql = """
        CREATE TYPE declaration_sql.text_value;
        CREATE FUNCTION declaration_sql.text_input(cstring) RETURNS declaration_sql.text_value AS '@MODULE_PATHNAME@','@INPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.text_output(declaration_sql.text_value) RETURNS cstring AS '@MODULE_PATHNAME@','@OUTPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.text_receive(internal) RETURNS declaration_sql.text_value AS '@MODULE_PATHNAME@','@RECEIVE_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.text_send(declaration_sql.text_value) RETURNS bytea AS '@MODULE_PATHNAME@','@SEND_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE TYPE declaration_sql.text_value(INTERNALLENGTH=variable,INPUT=declaration_sql.text_input,OUTPUT=declaration_sql.text_output,RECEIVE=declaration_sql.text_receive,SEND=declaration_sql.text_send,ALIGNMENT=int4,STORAGE=extended);
        """)]
    public readonly record struct TextValue(int Number);

    /// <summary>
    /// Converts prefixed numbers and records finally execution on both success and failure.
    /// </summary>
    public sealed class NumberText : PgTypeTextCodec<TextValue>
    {
        /// <inheritdoc />
        public override TextValue Parse(string text)
        {
            try
            {
                return text.StartsWith("N:", StringComparison.Ordinal) &&
                    int.TryParse(text.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    ? new(number) : throw new PgException("P8301", "replacement text input failed", "expected N:number", "supply a prefixed integer");
            }
            finally
            {
                s_textFinally++;
            }
        }

        /// <inheritdoc />
        public override string Format(TextValue value) => value.Number == -2
            ? throw new PgException("P8302", "replacement text output failed")
            : "N:" + value.Number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Retains packed varlena storage and a custom NULL-input policy under replacement SQL.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [PgType(Name = "native_value", NativeLayout = true, TextCodec = typeof(NativeText), BinaryProtocol = true,
        NullInputErrorMessage = "replacement native input needs text", Sql = """
        CREATE TYPE declaration_sql.native_value;
        CREATE FUNCTION declaration_sql.native_input(cstring) RETURNS declaration_sql.native_value AS '@MODULE_PATHNAME@','@INPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE CALLED ON NULL INPUT;
        CREATE FUNCTION declaration_sql.native_output(declaration_sql.native_value) RETURNS cstring AS '@MODULE_PATHNAME@','@OUTPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.native_receive(internal) RETURNS declaration_sql.native_value AS '@MODULE_PATHNAME@','@RECEIVE_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.native_send(declaration_sql.native_value) RETURNS bytea AS '@MODULE_PATHNAME@','@SEND_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE TYPE declaration_sql.native_value(INTERNALLENGTH=variable,INPUT=declaration_sql.native_input,OUTPUT=declaration_sql.native_output,RECEIVE=declaration_sql.native_receive,SEND=declaration_sql.native_send,ALIGNMENT=int4,STORAGE=extended);
        """)]
    public struct NativeValue
    {
        /// <summary>
        /// The leading unaligned byte.
        /// </summary>
        public byte Tag;

        /// <summary>
        /// The complete fixed-width integer.
        /// </summary>
        public int Number;
    }

    /// <summary>
    /// Supplies native-layout text independently of its binary bytes.
    /// </summary>
    public sealed class NativeText : PgTypeTextCodec<NativeValue>
    {
        /// <inheritdoc />
        public override NativeValue Parse(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? new() { Tag = 0xA5, Number = number } : throw new PgException("22P02", "replacement native input failed");

        /// <inheritdoc />
        public override string Format(NativeValue value) => value.Number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Keeps default JSON text without exposing binary I/O exports or registrations.
    /// </summary>
    /// <param name="Number">The exact number.</param>
    [PgType(Name = "plain_value", Sql = """
        CREATE TYPE declaration_sql.plain_value;
        CREATE FUNCTION declaration_sql.plain_input(cstring) RETURNS declaration_sql.plain_value AS '@MODULE_PATHNAME@','@INPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE FUNCTION declaration_sql.plain_output(declaration_sql.plain_value) RETURNS cstring AS '@MODULE_PATHNAME@','@OUTPUT_FUNCTION_NAME@' LANGUAGE c IMMUTABLE STRICT;
        CREATE TYPE declaration_sql.plain_value(INTERNALLENGTH=variable,INPUT=declaration_sql.plain_input,OUTPUT=declaration_sql.plain_output,ALIGNMENT=int4,STORAGE=extended);
        """)]
    public readonly record struct PlainValue(int Number);

    /// <summary>
    /// Exchanges generated JSON values through copied SPI transport.
    /// </summary>
    [PgFunction]
    public static JsonValue? JsonEcho(JsonValue? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exchanges shaped generated JSON arrays through copied SPI transport.
    /// </summary>
    [PgFunction]
    public static PgArray<JsonValue?>? JsonArray(PgArray<JsonValue?>? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exchanges custom text values without parsing their storage as text.
    /// </summary>
    [PgFunction]
    public static TextValue? TextEcho(TextValue? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exchanges custom text arrays while preserving shape and NULL cells.
    /// </summary>
    [PgFunction]
    public static PgArray<TextValue?>? TextArray(PgArray<TextValue?>? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exchanges packed values without changing their native bits.
    /// </summary>
    [PgFunction]
    public static NativeValue? NativeEcho(NativeValue? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exchanges packed arrays with exact dimension and NULL information.
    /// </summary>
    [PgFunction]
    public static PgArray<NativeValue?>? NativeArray(PgArray<NativeValue?>? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Returns an already-typed NULL without calling the input routine.
    /// </summary>
    [PgFunction]
    public static NativeValue? NativeNull() => null;

    /// <summary>
    /// Exposes every packed field independently of the text formatter.
    /// </summary>
    [PgFunction]
    public static string NativeDescribe(NativeValue value) => $"{value.Tag:X2}:{value.Number}";

    /// <summary>
    /// Executes the text-only registration through a typed native callback.
    /// </summary>
    [PgFunction]
    public static PlainValue? PlainEcho(PlainValue? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Returns completed custom parser and aggregate callback finally counts.
    /// </summary>
    [PgFunction]
    public static int[] CleanupCounts() => [s_textFinally, s_aggregateFinally];

    /// <summary>
    /// Uses literal label order while preserving the declared exact managed label mapping.
    /// </summary>
    [PgEnum(Name = "mood", Sql = "CREATE TYPE declaration_sql.mood AS ENUM('bêta','','alpha');")]
    public enum Mood
    {
        /// <summary>
        /// The label that literal SQL places last.
        /// </summary>
        [PgEnumLabel("alpha")]
        Alpha = 8,

        /// <summary>
        /// The multibyte label that literal SQL places first.
        /// </summary>
        [PgEnumLabel("bêta")]
        Beta = -2,

        /// <summary>
        /// A present empty label.
        /// </summary>
        [PgEnumLabel("")]
        Empty = 0,
    }

    /// <summary>
    /// Relies on explicit compatible SQL after its disabled declaration anchor.
    /// </summary>
    [PgEnum(Name = "manual_mood", GenerateSql = false, Id = "declaration.manual-enum-anchor")]
    public enum ManualMood
    {
        /// <summary>
        /// The label placed last by supplied SQL.
        /// </summary>
        Red = 31,

        /// <summary>
        /// The label placed first by supplied SQL.
        /// </summary>
        Blue = -7,
    }

    /// <summary>
    /// Exchanges literal enum labels through the exact registered SQL type.
    /// </summary>
    [PgFunction]
    public static Mood? MoodEcho(Mood? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Exposes the exact nonordinal managed enum value.
    /// </summary>
    [PgFunction]
    public static int? MoodNumber(Mood? value) => value is null ? null : (int)value.Value;

    /// <summary>
    /// Exchanges nonstandard enum array bounds and NULL cells.
    /// </summary>
    [PgFunction]
    public static PgArray<Mood?>? MoodArray(PgArray<Mood?>? value) => ArrayFunctions.Exchange(value, 1);

    /// <summary>
    /// Executes a typed consumer only after the manual enum has been supplied.
    /// </summary>
    [PgFunction(Requires = ["declaration.manual-enum"])]
    public static int? ManualNumber(ManualMood? value) => value is null ? null : (int)value.Value;

    /// <summary>
    /// Replaces only the aggregate parent while its helper independently replaces its function registration.
    /// </summary>
    [PgAggregate(Name = "replaced_total", Sql = """
        CREATE AGGREGATE declaration_sql.literal_total(integer)
            (SFUNC=declaration_sql.custom_step,STYPE=integer,INITCOND='10');
        """)]
    public static class ReplacedTotal
    {
        /// <summary>
        /// Adds nullable inputs and completes cleanup before reporting an owned error.
        /// </summary>
        [PgFunction(Name = "replaced_step", Sql = """
            CREATE FUNCTION declaration_sql.custom_step(integer,integer) RETURNS integer
                AS '@MODULE_PATHNAME@','@FUNCTION_NAME@' LANGUAGE c IMMUTABLE CALLED ON NULL INPUT;
            """)]
        public static int Transition(int state, int? value)
        {
            try
            {
                return value == -99 ? throw new PgException("P8304", "replacement aggregate failed") : checked(state + (value ?? 0));
            }
            finally
            {
                s_aggregateFinally++;
            }
        }
    }

    /// <summary>
    /// Suppresses its parent SQL but leaves native support usable by a separately declared aggregate.
    /// </summary>
    [PgAggregate(Name = "hidden_total", GenerateSql = false, Id = "declaration.hidden-parent", InitialCondition = "0")]
    public static class HiddenTotal
    {
        /// <summary>
        /// Adds through the retained ordinary helper registration.
        /// </summary>
        [PgFunction(Name = "hidden_step")]
        public static int Transition(int state, int value) => checked(state + value);
    }

    /// <summary>
    /// Replaces its aggregate while leaving a disabled helper to independently ordered SQL.
    /// </summary>
    [PgAggregate(Name = "supplied_parent", Id = "declaration.supplied-parent", Sql = """
        CREATE AGGREGATE declaration_sql.supplied_total(integer)
            (SFUNC=declaration_sql.supplied_step,STYPE=integer,INITCOND='0');
        """)]
    public static class SuppliedTotal
    {
        /// <summary>
        /// Must not execute because its own declaration is suppressed.
        /// </summary>
        [PgFunction(Name = "supplied_step", GenerateSql = false, Id = "declaration.supplied-helper-anchor")]
        public static int Transition(int state, int value) => throw new InvalidOperationException("disabled aggregate helper executed");
    }

    /// <summary>
    /// Retains equality, comparison and hashing while neither default family is installed.
    /// </summary>
    /// <param name="Number">The exact comparison value.</param>
    [PgType(Name = "unindexed")]
    [PgEquality]
    [PgOrdering(GenerateSql = false)]
    [PgHashing(GenerateSql = false)]
    public readonly record struct Unindexed(int Number) : IComparable<Unindexed>, IPgHashable
    {
        /// <inheritdoc />
        public int CompareTo(Unindexed other) => Number.CompareTo(other.Number);

        /// <inheritdoc />
        public int GetPostgresHashCode() => Number * 17;

        /// <summary>
        /// Compares values using the declared total order.
        /// </summary>
        public static bool operator <(Unindexed left, Unindexed right) => left.CompareTo(right) < 0;

        /// <summary>
        /// Compares values using the declared total order.
        /// </summary>
        public static bool operator >(Unindexed left, Unindexed right) => left.CompareTo(right) > 0;

        /// <summary>
        /// Includes equality in the declared ascending order.
        /// </summary>
        public static bool operator <=(Unindexed left, Unindexed right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// Includes equality in the declared descending order.
        /// </summary>
        public static bool operator >=(Unindexed left, Unindexed right) => left.CompareTo(right) >= 0;
    }

    /// <summary>
    /// Forces hashed helper names with a sixty-byte identifier while custom classes reference exact helper tokens.
    /// </summary>
    /// <param name="Number">The stored comparison value.</param>
    [PgType(Name = "éééééééééééééééééééééééééééééé")]
    [PgEquality]
    [PgOrdering(Sql = """
        CREATE OPERATOR FAMILY declaration_sql.literal_btree_ops USING btree;
        CREATE OPERATOR CLASS declaration_sql.literal_btree_ops FOR TYPE declaration_sql."éééééééééééééééééééééééééééééé" USING btree FAMILY declaration_sql.literal_btree_ops AS
            OPERATOR 1 declaration_sql.<, OPERATOR 2 declaration_sql.<=, OPERATOR 3 declaration_sql.=,
            OPERATOR 4 declaration_sql.>=, OPERATOR 5 declaration_sql.>,
            FUNCTION 1 @COMPARISON_FUNCTION_SQL@(declaration_sql."éééééééééééééééééééééééééééééé",declaration_sql."éééééééééééééééééééééééééééééé");
        """)]
    [PgHashing(Sql = """
        CREATE OPERATOR FAMILY declaration_sql.literal_hash_ops USING hash;
        CREATE OPERATOR CLASS declaration_sql.literal_hash_ops FOR TYPE declaration_sql."éééééééééééééééééééééééééééééé" USING hash FAMILY declaration_sql.literal_hash_ops AS
            OPERATOR 1 declaration_sql.=,
            FUNCTION 1 @HASH_FUNCTION_SQL@(declaration_sql."éééééééééééééééééééééééééééééé");
        """)]
    public readonly record struct Indexed(int Number) : IComparable<Indexed>, IPgHashable
    {
        /// <inheritdoc />
        public int CompareTo(Indexed other) => Number == -99 || other.Number == -99
            ? throw new PgException("P8305", "replacement comparison failed") : Number.CompareTo(other.Number);

        /// <inheritdoc />
        public int GetPostgresHashCode() => Number == -98 ? throw new PgException("P8306", "replacement hash failed") : 7;

        /// <summary>
        /// Compares values through the error-preserving total-order contract.
        /// </summary>
        public static bool operator <(Indexed left, Indexed right) => left.CompareTo(right) < 0;

        /// <summary>
        /// Compares values through the error-preserving total-order contract.
        /// </summary>
        public static bool operator >(Indexed left, Indexed right) => left.CompareTo(right) > 0;

        /// <summary>
        /// Includes equality in the declared ascending order.
        /// </summary>
        public static bool operator <=(Indexed left, Indexed right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// Includes equality in the declared descending order.
        /// </summary>
        public static bool operator >=(Indexed left, Indexed right) => left.CompareTo(right) >= 0;
    }
}
