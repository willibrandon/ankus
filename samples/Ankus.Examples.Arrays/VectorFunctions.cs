using Ankus.Examples.Arrays;

[assembly: PgSql("vectors_data", """
    DO $$ BEGIN RAISE WARNING 'creating sample data in the table ''vectors.data''.'; END $$;
    CREATE TABLE vectors.data AS
        SELECT vectors.random_vector(768) AS v FROM generate_series(1, 1000);
    """)]
[assembly: PgRequires(typeof(VectorFunctions), nameof(VectorFunctions.RandomVector), DeclarationId = "vectors_data")]

namespace Ankus.Examples.Arrays;

/// <summary>
/// Demonstrates borrowed iteration, copied vectors and contiguous native array spans.
/// </summary>
[PgSchema("vectors")]
public static class VectorFunctions
{
    /// <summary>
    /// Sums borrowed non-NULL real cells using one native cursor.
    /// </summary>
    /// <param name="input">The borrowed native real array.</param>
    /// <returns>The single-precision sequential sum.</returns>
    [PgFunction]
    public static float SumVectorArray(PgArrayView<float> input)
    {
        float sum = -0.0f;
        foreach (float value in input)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Sums detached real values in row-major order, including arrays with nonstandard bounds.
    /// </summary>
    /// <param name="input">The detached array whose cells must be present.</param>
    /// <returns>The single-precision sequential sum.</returns>
    [PgFunction]
    public static float SumVectorVec(PgArray<float> input)
    {
        float sum = -0.0f;
        foreach (float value in input)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Sums a contiguous borrowed real payload before making another backend call.
    /// </summary>
    /// <param name="input">The native real array without SQL NULL cells.</param>
    /// <returns>The single-precision sequential sum.</returns>
    [PgFunction]
    public static float SumVectorSlice(PgArrayView<float> input)
    {
        using var view = new PgArrayView(input.Datum);
        ReadOnlySpan<float> values = view.DangerousGetSpan<float>();
        float sum = -0.0f;
        foreach (float value in values)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Uses pgrx's sixteen independent accumulators followed by its scalar remainder and reduction order.
    /// </summary>
    /// <param name="input">The native real array without SQL NULL cells.</param>
    /// <returns>The single-precision sixteen-lane sum; no measured speedup is promised.</returns>
    [PgFunction(Name = "sum_vector_simd")]
    public static float SumVectorLanes(PgArrayView<float> input)
    {
        const int LaneCount = 16;
        using var view = new PgArrayView(input.Datum);
        ReadOnlySpan<float> values = view.DangerousGetSpan<float>();
        Span<float> lanes = stackalloc float[LaneCount];
        lanes.Clear();
        int chunks = values.Length / LaneCount;
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            int offset = chunk * LaneCount;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                lanes[lane] += values[offset + lane];
            }
        }

        float remainder = -0.0f;
        foreach (float value in values[(chunks * LaneCount)..])
        {
            remainder += value;
        }

        float reduced = 0;
        foreach (float value in lanes)
        {
            reduced += value;
        }

        return reduced + remainder;
    }

    /// <summary>
    /// Generates real cells in the half-open interval from zero to one.
    /// </summary>
    /// <param name="length">The requested length; zero or negative values produce an empty array like Rust's range.</param>
    /// <returns>New managed random cells copied to native PostgreSQL array storage.</returns>
    [PgFunction]
    public static float[] RandomVector(int length)
    {
        if (length <= 0)
        {
            return [];
        }

        float[] values = new float[length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = Random.Shared.NextSingle();
        }

        return values;
    }
}
