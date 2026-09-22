using System.Globalization;
using System.Numerics;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises numeric constraints and generic conversions in the published native library.
/// </summary>
public static class NumericContractFunctions
{
    private static int s_called;
    private static int s_finalized;

    /// <summary>
    /// Reports the numeric actually delivered to managed code after input coercion.
    /// </summary>
    /// <param name="value">The constrained input or SQL NULL.</param>
    /// <returns>The coerced text, or a marker proving nullable inputs enter the method.</returns>
    [PgFunction]
    public static string ConstrainedNumericInput([PgNumericPrecision(5, 2)] PgNumeric? value)
    {
        s_called++;
        return value?.Text ?? "managed null";
    }

    /// <summary>
    /// Returns a numeric whose return constraint applies after the method's finally block.
    /// </summary>
    /// <param name="value">The unconstrained input.</param>
    /// <returns>The constrained result or SQL NULL.</returns>
    [PgFunction]
    [return: PgNumericPrecision(5, 2)]
    public static PgNumeric? ConstrainedNumericOutput(PgNumeric? value)
    {
        s_called++;
        try
        {
            return value;
        }
        finally
        {
            s_finalized++;
        }
    }

    /// <summary>
    /// Checks numeric coercion precedes exact decimal narrowing.
    /// </summary>
    /// <param name="value">The constrained decimal.</param>
    /// <returns>The exact narrowed decimal text, including scale.</returns>
    [PgFunction]
    public static string ConstrainedDecimalInput([PgNumericPrecision(5, 2)] decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "managed null";

    /// <summary>
    /// Coerces the result of a decimal method after its return.
    /// </summary>
    /// <param name="value">The original decimal.</param>
    /// <returns>The constrained numeric.</returns>
    [PgFunction]
    [return: PgNumericPrecision(5, 2)]
    public static decimal? ConstrainedDecimalOutput(decimal? value) => value;

    /// <summary>
    /// Uses the default zero scale on an input parameter.
    /// </summary>
    /// <param name="value">The numeric input.</param>
    /// <returns>The input rounded to an integer.</returns>
    [PgFunction]
    public static string ConstrainedWholeInput([PgNumericPrecision(5)] PgNumeric value) => value.Text;

    /// <summary>
    /// Applies a negative scale to a returned numeric on PostgreSQL 15 or later.
    /// </summary>
    /// <param name="value">The original numeric.</param>
    /// <returns>The rounded numeric.</returns>
    [PgFunction]
    [return: PgNumericPrecision(2, -3)]
    public static PgNumeric ConstrainedNegativeScale(PgNumeric value) => value;

    /// <summary>
    /// Applies a scale larger than precision on PostgreSQL 15 or later.
    /// </summary>
    /// <param name="value">The original numeric.</param>
    /// <returns>The rounded numeric.</returns>
    [PgFunction]
    [return: PgNumericPrecision(3, 5)]
    public static PgNumeric ConstrainedFractionalScale(PgNumeric value) => value;

    /// <summary>
    /// Applies independent constraints to two parameters and one return.
    /// </summary>
    /// <param name="left">The first constrained operand.</param>
    /// <param name="right">The second constrained operand.</param>
    /// <returns>The product with the return constraint.</returns>
    [PgFunction]
    [return: PgNumericPrecision(6, 3)]
    public static PgNumeric ConstrainedProduct([PgNumericPrecision(5, 2)] PgNumeric left, [PgNumericPrecision(4, 1)] PgNumeric right)
        => left * right;

    /// <summary>
    /// Exercises native numeric-to-primitive casts, converting their results to invariant text.
    /// </summary>
    /// <param name="value">The numeric input.</param>
    /// <param name="kind">The target primitive.</param>
    /// <returns>The native-cast value.</returns>
    [PgFunction]
    public static string NumericPrimitiveCast(PgNumeric value, string kind) => kind switch
    {
        "int2" => value.ToInt16().ToString(CultureInfo.InvariantCulture),
        "int4" => value.ToInt32().ToString(CultureInfo.InvariantCulture),
        "int8" => value.ToInt64().ToString(CultureInfo.InvariantCulture),
        "float4" => BitConverter.SingleToInt32Bits((float)value).ToString(CultureInfo.InvariantCulture),
        "float8" => BitConverter.DoubleToInt64Bits((double)value).ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("Unknown primitive.", nameof(kind)),
    };

    /// <summary>
    /// Exercises the float4 conversion independently of float8 widening.
    /// </summary>
    /// <param name="value">The single-precision input.</param>
    /// <returns>The native numeric.</returns>
    [PgFunction]
    public static PgNumeric NumericFromSingle(float value) => (PgNumeric)value;

    /// <summary>
    /// Checks closed generic integer conversions in Native AOT.
    /// </summary>
    /// <param name="value">The integer numeric.</param>
    /// <param name="kind">The target integer.</param>
    /// <returns>The numeric after checked generic narrowing and conversion back.</returns>
    [PgFunction]
    public static PgNumeric NumericGenericInteger(PgNumeric value, string kind) => kind switch
    {
        "sbyte" => PgNumeric.FromInteger(value.ToInteger<sbyte>()),
        "byte" => PgNumeric.FromInteger(value.ToInteger<byte>()),
        "short" => PgNumeric.FromInteger(value.ToInteger<short>()),
        "ushort" => PgNumeric.FromInteger(value.ToInteger<ushort>()),
        "int" => PgNumeric.FromInteger(value.ToInteger<int>()),
        "uint" => PgNumeric.FromInteger(value.ToInteger<uint>()),
        "long" => PgNumeric.FromInteger(value.ToInteger<long>()),
        "ulong" => PgNumeric.FromInteger(value.ToInteger<ulong>()),
        "nint" => PgNumeric.FromInteger(value.ToInteger<nint>()),
        "nuint" => PgNumeric.FromInteger(value.ToInteger<nuint>()),
        "Int128" => PgNumeric.FromInteger(value.ToInteger<Int128>()),
        "UInt128" => PgNumeric.FromInteger(value.ToInteger<UInt128>()),
        "BigInteger" => PgNumeric.FromInteger(value.ToInteger<BigInteger>()),
        _ => throw new ArgumentException("Unknown integer.", nameof(kind)),
    };

    /// <summary>
    /// Checks generic arithmetic interfaces, mixed primitive operations, and one-pass summation.
    /// </summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>The calculated numeric.</returns>
    [PgFunction]
    public static PgNumeric NumericGenericMath(PgNumeric left, PgNumeric right)
        => PgNumeric.Sum([GenericProduct(left, right), 1.2300m, (left + 2) * 3 - 4, 5 - right]);

    private static T GenericProduct<T>(T left, T right)
        where T : IMultiplyOperators<T, T, T>, IMultiplicativeIdentity<T, T>, IUnaryPlusOperators<T, T>
        => (+left * right) * T.MultiplicativeIdentity;

    /// <summary>
    /// Checks failures unwind, preserve prior writes/plans, and release native operation contexts.
    /// </summary>
    /// <param name="kind">The failing conversion route.</param>
    /// <returns>Error, invocation/finally counts, context growth, and surviving database state.</returns>
    [PgFunction]
    public static string NumericContractRecovery(string kind)
        => Spi.Connect(session =>
        {
            string sql = kind switch
            {
                "input" => "SELECT datatype.constrained_numeric_input(999.995)",
                "output" => "SELECT datatype.constrained_numeric_output(999.995)",
                "integer" => "SELECT datatype.numeric_primitive_cast(32767.5, 'int2')",
                "single" => "SELECT datatype.numeric_primitive_cast(1e-100, 'float4')",
                "generic" => "SELECT datatype.numeric_generic_integer(-1, 'ulong')",
                _ => throw new ArgumentException("Unknown failure route.", nameof(kind)),
            };
            session.Execute("CREATE TEMP TABLE numeric_contract_writes(value integer)");
            session.Execute("INSERT INTO numeric_contract_writes VALUES (1)");
            using SpiPreparedStatement plan = session.Prepare("SELECT $1 + 2", typeof(int));
            const string contexts = """
                SELECT count(*) FROM pg_backend_memory_contexts
                WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')
                """;
            long before = session.ExecuteScalar<long>(contexts);
            int calledBefore = s_called;
            int finalizedBefore = s_finalized;
            string state = "no error";
            int finalized = 0;
            for (int index = 0; index < 50; index++)
            {
                try { session.Execute(sql); }
                catch (PgException error) { state = error.SqlState; }
                finally { finalized++; }
            }

            session.Execute("INSERT INTO numeric_contract_writes VALUES (2)");
            return $"{state}:{s_called - calledBefore}:{s_finalized - finalizedBefore}:{finalized}:" +
                (session.ExecuteScalar<long>(contexts) - before) + ":" +
                plan.ExecuteScalar<int>(SpiParameter.Create(40)) + ":" + session.ExecuteScalar<long>("SELECT sum(value) FROM numeric_contract_writes");
        });
}
