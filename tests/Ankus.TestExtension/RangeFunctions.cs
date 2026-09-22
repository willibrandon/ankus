namespace Ankus.TestExtension;

/// <summary>
/// Exercises range construction, canonicalization, subtype aliases and all native ownership paths.
/// </summary>
public static class RangeFunctions
{
    /// <summary>
    /// Exchanges integer ranges through each SPI lifetime path.
    /// </summary>
    [PgFunction]
    public static PgRange<int>? RangeInt(PgRange<int>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges bigint ranges through each SPI lifetime path.
    /// </summary>
    [PgFunction]
    public static PgRange<long>? RangeLong(PgRange<long>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range numeric bounds, retaining scale and special values.
    /// </summary>
    [PgFunction]
    public static PgRange<PgNumeric>? RangeNumeric(PgRange<PgNumeric>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range dates and explicit infinity bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<PgDate>? RangeDate(PgRange<PgDate>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range timestamps and explicit infinity bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestamp>? RangeTimestamp(PgRange<PgTimestamp>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range UTC timestamps and explicit infinity bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestampTz>? RangeTimestampTz(PgRange<PgTimestampTz>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges checked decimal range bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<decimal>? RangeDecimal(PgRange<decimal>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges checked DateOnly range bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<DateOnly>? RangeDateOnly(PgRange<DateOnly>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges checked unspecified DateTime range bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<DateTime>? RangeDateTime(PgRange<DateTime>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges checked UTC DateTimeOffset range bounds.
    /// </summary>
    [PgFunction]
    public static PgRange<DateTimeOffset>? RangeDateTimeOffset(PgRange<DateTimeOffset>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped integer-range arrays with NULL, empty and unbounded elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgRange<int>?>? RangeInts(PgArray<PgRange<int>?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges bigint range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<long>?[]? RangeLongs(PgRange<long>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges numeric range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<PgNumeric>?[]? RangeNumerics(PgRange<PgNumeric>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range date vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<PgDate>?[]? RangeDates(PgRange<PgDate>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range timestamp vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestamp>?[]? RangeTimestamps(PgRange<PgTimestamp>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-range timestamp-with-time-zone vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<PgTimestampTz>?[]? RangeTimestampTzs(PgRange<PgTimestampTz>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges decimal range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<decimal>?[]? RangeDecimals(PgRange<decimal>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges DateOnly range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<DateOnly>?[]? RangeDateOnlys(PgRange<DateOnly>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges DateTime range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<DateTime>?[]? RangeDateTimes(PgRange<DateTime>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges DateTimeOffset range vectors.
    /// </summary>
    [PgFunction]
    public static PgRange<DateTimeOffset>?[]? RangeDateTimeOffsets(PgRange<DateTimeOffset>?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Constructs ranges without first passing through PostgreSQL input or canonicalization.
    /// </summary>
    [PgFunction]
    public static PgRange<int> RangeConstruct(int? lower, int? upper, bool lowerInclusive, bool upperInclusive, bool empty)
        => empty ? new() : new(lower, upper, lowerInclusive, upperInclusive);

    /// <summary>
    /// Exposes each integer-range state independently of the native writer.
    /// </summary>
    [PgFunction]
    public static string RangeInspect(PgRange<int>? value) => value is null ? "null" :
        $"{value.IsEmpty}:{value.IsUnbounded}:{value.Lower}:{value.Upper}:{value.LowerInclusive}:{value.UpperInclusive}";

    /// <summary>
    /// Parses and formats six range families using guarded backend operations.
    /// </summary>
    [PgFunction]
    public static string RangeParse(string type, string text) => type switch
    {
        "int4range" => PgRange.Parse<int>(text).ToPostgresString(),
        "int8range" => PgRange.Parse<long>(text).ToPostgresString(),
        "numrange" => PgRange.Parse<PgNumeric>(text).ToPostgresString(),
        "daterange" => PgRange.Parse<PgDate>(text).ToPostgresString(),
        "tsrange" => PgRange.Parse<PgTimestamp>(text).ToPostgresString(),
        "tstzrange" => PgRange.Parse<PgTimestampTz>(text).ToPostgresString(),
        _ => throw new ArgumentException("Unknown range type.", nameof(type)),
    };

    /// <summary>
    /// Runs range operators over a requested subtype and returns PostgreSQL output for comparison with SQL.
    /// </summary>
    [PgFunction]
    public static string RangeOperate(string type, string left, string right, int operation) => type switch
    {
        "int4range" => Operate<int>(left, right, operation),
        "int8range" => Operate<long>(left, right, operation),
        "numrange" => Operate<PgNumeric>(left, right, operation),
        "daterange" => Operate<PgDate>(left, right, operation),
        "tsrange" => Operate<PgTimestamp>(left, right, operation),
        "tstzrange" => Operate<PgTimestampTz>(left, right, operation),
        _ => throw new ArgumentException("Unknown range type.", nameof(type)),
    };

    /// <summary>
    /// Evaluates predicates independently against an element and another range.
    /// </summary>
    [PgFunction]
    public static bool[] RangePredicates(PgRange<int> left, PgRange<int> right, int element)
        => [left.Contains(element), left.Contains(right), left.Overlaps(right), left.IsAdjacentTo(right)];

    /// <summary>
    /// Tests the complete predicate dispatch for every scalar subtype, including element containment.
    /// </summary>
    [PgFunction]
    public static bool[] RangeSubtypePredicates(string type, string left, string right) => type switch
    {
        "int4range" => Predicates<int>(left, right), "int8range" => Predicates<long>(left, right),
        "numrange" => Predicates<PgNumeric>(left, right), "daterange" => Predicates<PgDate>(left, right),
        "tsrange" => Predicates<PgTimestamp>(left, right), "tstzrange" => Predicates<PgTimestampTz>(left, right),
        _ => throw new ArgumentException("Unknown range type.", nameof(type)),
    };

    /// <summary>
    /// Canonicalizes explicitly constructed bounds inside managed code before inspecting their values.
    /// </summary>
    [PgFunction]
    public static string RangeCanonicalize(int lower, int upper, bool lowerInclusive, bool upperInclusive)
        => RangeInspect(new PgRange<int>(lower, upper, lowerInclusive, upperInclusive).Canonicalize());

    /// <summary>
    /// Exercises checked managed input failures and verifies timezone offset normalization.
    /// </summary>
    [PgFunction]
    public static PgRange<DateTimeOffset> RangeOffsetConstruct()
        => new(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2024, 1, 3, 3, 4, 5, TimeSpan.FromHours(-7)));

    /// <summary>
    /// Reads an arbitrary domain or packed range query through SPI and retains it across further calls.
    /// </summary>
    [PgFunction]
    public static PgRange<PgNumeric>? RangeNumericQuery(string query)
    {
        PgRange<PgNumeric>? value = Spi.Query(query)[0].Get<PgRange<PgNumeric>?>(0);
        Spi.Execute("SELECT repeat('replacement', 10000)");
        return value;
    }

    /// <summary>
    /// Recovers from canonicalization, disjoint union and binary bound errors while retaining writes and plans.
    /// </summary>
    [PgFunction]
    public static string RangeRecovery() => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE range_writes(value int); INSERT INTO range_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM range_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int failures = 0;
        int finalized = 0;
        for (int i = 0; i < 40; i++)
        {
            if (!PgRange.TryParse<int>("[5,2)", out _) && !PgRange.TryParse<PgDate>("[bad,date)", out _))
            {
                failures++;
            }

            try
            {
                _ = PgRange.Create(1, 2).Union(PgRange.Create(4, 5));
            }
            catch (PgException error) when (error.SqlState == "22000")
            {
                failures++;
            }
            finally
            {
                finalized++;
            }

            try
            {
                _ = session.ExecuteScalar<PgRange<int>>("SELECT $1", SpiParameter.Create(new PgRange<int>(int.MaxValue, null, false)));
            }
            catch (PgException error) when (error.SqlState == "22003")
            {
                failures++;
            }
        }

        session.Execute("INSERT INTO range_writes VALUES (2)");
        return $"{failures}:{finalized}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });

    private static string Operate<T>(string left, string right, int operation) where T : struct
    {
        PgRange<T> a = PgRange.Parse<T>(left);
        PgRange<T> b = PgRange.Parse<T>(right);
        PgRange<T> result = operation switch
        {
            0 => a.Union(b), 1 => a.Intersect(b), 2 => a.Except(b), 3 => a.Merge(b), _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return result.ToPostgresString();
    }

    private static bool[] Predicates<T>(string left, string right) where T : struct
    {
        PgRange<T> a = PgRange.Parse<T>(left);
        PgRange<T> b = PgRange.Parse<T>(right);
        return [a.Contains(a.Lower!.Value), a.Contains(b), a.Overlaps(b), a.IsAdjacentTo(b)];
    }
}
