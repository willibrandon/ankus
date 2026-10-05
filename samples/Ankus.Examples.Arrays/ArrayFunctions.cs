namespace Ankus.Examples.Arrays;

/// <summary>
/// Ports pgrx's array examples with explicit nullable elements and checked native borrowing.
/// </summary>
public static partial class ArrayFunctions
{
    /// <summary>
    /// Sums squared differences across the shorter input, rejecting SQL NULL anywhere in either array.
    /// </summary>
    /// <param name="left">The first borrowed real array.</param>
    /// <param name="right">The second borrowed real array.</param>
    /// <returns>The single-precision squared Euclidean distance.</returns>
    [PgFunction(Name = "sq_euclid")]
    public static float SquaredEuclideanDistance(PgArrayView<float> left, PgArrayView<float> right)
    {
        using IEnumerator<float> first = left.GetEnumerator();
        using IEnumerator<float> second = right.GetEnumerator();
        float sum = -0.0f;
        while (first.MoveNext() && second.MoveNext())
        {
            float difference = first.Current - second.Current;
            sum += difference * difference;
        }

        return sum;
    }

    /// <summary>
    /// Sums the selected zero-based distance cells and reports the same INFO observations as pgrx.
    /// </summary>
    /// <param name="compressed">The borrowed array of zero-based distance indices.</param>
    /// <param name="distances">The borrowed distances, whose unselected NULL cells may remain unused.</param>
    /// <returns>The double-precision sum of selected distances.</returns>
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static double ApproxDistance(PgArrayView<long> compressed, PgArrayView<double?> distances)
    {
        double sum = -0.0;
        foreach (long index in compressed)
        {
            if (index < 0 || index >= distances.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(compressed), index, "A distance index is outside the array.");
            }

            double distance = distances[(int)index] ?? throw new InvalidOperationException("A selected distance is SQL NULL.");
            PgLog.Info(FormattableString.Invariant($"cc={index}, d={distance}"));
            sum += distance;
        }

        return sum;
    }

    /// <summary>
    /// Supplies an empty SQL integer array for the dependent default expression.
    /// </summary>
    /// <returns>A present empty array.</returns>
    [PgFunction]
    public static int[] DefaultArray() => [];

    /// <summary>
    /// Adds borrowed integer cells with SQL NULL contributing minus one.
    /// </summary>
    /// <param name="input">The borrowed array, defaulted by PostgreSQL to DefaultArray.</param>
    /// <returns>The signed 64-bit sum.</returns>
    [PgFunction]
    [PgRequires(typeof(ArrayFunctions), nameof(DefaultArray))]
    public static long SumArray([PgParameter(Default = "arrays.default_array()")] PgArrayView<int?> input)
    {
        long sum = 0;
        foreach (int? value in input)
        {
            sum += value ?? -1;
        }

        return sum;
    }

    /// <summary>
    /// Copies row-major input into a mutable list, appends six and treats SQL NULL as zero.
    /// </summary>
    /// <param name="input">The detached array whose original cells and dimensions remain unchanged.</param>
    /// <returns>The sum of copied cells plus six.</returns>
    [PgFunction]
    public static long SumVec(PgArray<int?> input)
    {
        List<int?> values = [.. input];
        values.Add(6);
        long sum = 0;
        foreach (int? value in values)
        {
            sum += value ?? 0;
        }

        return sum;
    }

    /// <summary>
    /// Returns pgrx's exact nullable name list.
    /// </summary>
    /// <returns>Brandy, Sally, SQL NULL and Anchovy in that order.</returns>
    [PgFunction]
    public static string?[] StaticNames() => ["Brandy", "Sally", null, "Anchovy"];

    /// <summary>
    /// Returns pgrx's three exact name arrays as independently produced SQL rows.
    /// </summary>
    /// <returns>The ordered nullable name arrays.</returns>
    [PgFunction]
    public static IEnumerable<string?[]> StaticNamesSet()
    {
        yield return StaticNames();
        yield return ["Eric", "David"];
        yield return ["ZomboDB", "PostgreSQL", "Elasticsearch"];
    }

    /// <summary>
    /// Returns five ordinary integer cells.
    /// </summary>
    /// <returns>The integers one through five.</returns>
    [PgFunction(Name = "i32_array_no_nulls")]
    public static int[] Integers() => [1, 2, 3, 4, 5];

    /// <summary>
    /// Returns the same integers with SQL NULL in the two upstream positions.
    /// </summary>
    /// <returns>One, NULL, two, three, NULL, four and five.</returns>
    [PgFunction(Name = "i32_array_with_nulls")]
    public static int?[] NullableIntegers() => [1, null, 2, 3, null, 4, 5];

    /// <summary>
    /// Copies every present row-major integer while preserving its relative order.
    /// </summary>
    /// <param name="input">The detached array with independently nullable cells.</param>
    /// <returns>A one-dimensional array containing only present cells.</returns>
    [PgFunction]
    public static int[] StripNulls(PgArray<int?> input) => [.. input.OfType<int>()];

    /// <summary>
    /// Returns the empty generated custom type as a singleton SQL array.
    /// </summary>
    /// <returns>A present array containing one empty custom-type value.</returns>
    [PgFunction(Name = "return_vec_of_customtype", SearchPath = [PgSearchPath.ExtensionSchema])]
    public static SomeStruct[] CustomTypeArray() => [new()];

    /// <summary>
    /// Reads the custom array through SPI using the extension's fixed installation schema.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void CustomTypeArrayRoundTrip()
    {
        SomeStruct[] values = Spi.ExecuteScalar<SomeStruct[]>("SELECT arrays.return_vec_of_customtype()");
        bool exact = Spi.ExecuteScalar<bool>("""
            SELECT pg_typeof(value) = 'arrays.some_struct[]'::regtype
                AND cardinality(value) = 1 AND value[1]::text = '{}'
            FROM (SELECT arrays.return_vec_of_customtype() AS value) AS input
            """);
        if (values.Length != 1 || !exact)
        {
            throw new InvalidOperationException("The custom array did not preserve its singleton type and empty payload.");
        }
    }
}
