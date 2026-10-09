using System.Text;

namespace Ankus.Examples.PglzInspect;

/// <summary>
/// Ports pgrx's 24 <c>pglz_inspect</c> backend tests, which call both the SQL functions and the PGLZ wrapper.
/// </summary>
/// <remarks>
/// Test publications install these functions; ordinary publications omit them. Each case runs in its own rolled-back
/// transaction, so the tables it creates disappear afterwards. The wrapper tests must run inside PostgreSQL because
/// <see cref="Pglz"/> calls the server's own compressor.
/// </remarks>
public static partial class PglzInspectTests
{
    /// <summary>
    /// 1024 repeated bytes compress well.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void PglzSizeCompressesRepetitiveInput()
    {
        (int raw, int compressed, double ratio) = Spi.ExecuteScalars<int, int, double>(
            "SELECT raw_bytes, compressed_bytes, ratio FROM pglz_size(repeat('a', 1024)::bytea)");
        Require(raw == 1024, $"expected 1024 raw bytes, got {raw}");
        Require(compressed < raw, "expected compression to shrink input");
        Require(ratio < 0.5, $"expected ratio < 0.5, got {ratio}");
    }

    /// <summary>
    /// Eight bytes are below PGLZ's minimum input size.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void PglzSizeRejectsSmallInput()
        => Require(Spi.ExecuteScalar<bool?>("SELECT accepted FROM pglz_size('\\x0102030405060708'::bytea)") == false,
            "expected an eight-byte input to be rejected");

    /// <summary>
    /// Random printable text does not compress well, whether or not PGLZ accepts it.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void PglzSizeHandlesRandomInput()
    {
        double ratio = Spi.ExecuteScalar<double>("""
            SELECT ratio FROM pglz_size(
                convert_to(
                    (SELECT string_agg(chr((random()*94+32)::int), '') FROM generate_series(1, 4096)),
                    'SQL_ASCII'))
            """);
        Require(ratio > 0.7, $"random text should not compress well, got {ratio}");
    }

    /// <summary>
    /// A column of repeated phrases is fully sampled, accepted and compressed well.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void AnalyzeColumnOnRepetitiveData()
    {
        _ = Spi.Execute("CREATE TABLE _t_analyze (v text)");
        _ = Spi.Execute("INSERT INTO _t_analyze SELECT repeat('hello world ', 200) FROM generate_series(1, 50)");
        (int sampled, double averageRatio, double pctAccepted) = Spi.ExecuteScalars<int, double, double>(
            "SELECT sampled_rows, avg_ratio, pct_accepted FROM pglz_analyze_column('_t_analyze'::regclass, 'v', 50)");
        Require(sampled == 50, $"expected 50 sampled rows, got {sampled}");
        Require(averageRatio < 0.3, $"expected good compression, got {averageRatio}");
        Require(pctAccepted >= 0.99, $"expected all accepted, got {pctAccepted}");
    }

    /// <summary>
    /// A compressible column is recommended.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void RecommendSaysYesForCompressibleColumn()
    {
        _ = Spi.Execute("CREATE TABLE _t_rec_yes (v text)");
        _ = Spi.Execute("INSERT INTO _t_rec_yes SELECT repeat('compress_me_', 200) FROM generate_series(1, 50)");
        string message = Spi.ExecuteScalar<string>("SELECT pglz_recommend('_t_rec_yes'::regclass, 'v', 50)");
        Require(message.StartsWith("RECOMMEND", StringComparison.Ordinal), $"got: {message}");
    }

    /// <summary>
    /// A column of hexadecimal digests is skipped or marginal.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void RecommendSaysSkipForRandomColumn()
    {
        _ = Spi.Execute("CREATE TABLE _t_rec_no (v text)");
        _ = Spi.Execute("INSERT INTO _t_rec_no SELECT md5(i::text) || md5((i+1)::text) FROM generate_series(1, 50) i");
        string message = Spi.ExecuteScalar<string>("SELECT pglz_recommend('_t_rec_no'::regclass, 'v', 50)");
        Require(message.StartsWith("SKIP", StringComparison.Ordinal) || message.StartsWith("MARGINAL", StringComparison.Ordinal),
            $"got: {message}");
    }

    /// <summary>
    /// Every sampled row is counted in exactly one bucket.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void HistogramBucketsSumToSampleSize()
    {
        _ = Spi.Execute("CREATE TABLE _t_hist (v text)");
        _ = Spi.Execute("""
            INSERT INTO _t_hist
            SELECT CASE WHEN i % 2 = 0 THEN repeat('aaaaaaaa', 50) ELSE md5(i::text) || md5((i+1)::text) END
            FROM generate_series(1, 40) i
            """);
        long total = Spi.ExecuteScalar<long>("SELECT SUM(row_count)::bigint FROM pglz_ratio_histogram('_t_hist'::regclass, 'v', 40)");
        Require(total == 40, $"expected 40 counted rows, got {total}");
    }

    /// <summary>
    /// An empty table reports NO DATA and zero sampled rows.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void EmptyTableReturnsNoData()
    {
        _ = Spi.Execute("CREATE TABLE _t_empty (v text)");
        string message = Spi.ExecuteScalar<string>("SELECT pglz_recommend('_t_empty'::regclass, 'v', 10)");
        Require(message.StartsWith("NO DATA", StringComparison.Ordinal), $"got: {message}");
        int sampled = Spi.ExecuteScalar<int>("SELECT sampled_rows FROM pglz_analyze_column('_t_empty'::regclass, 'v', 10)");
        Require(sampled == 0, $"expected no sampled rows, got {sampled}");
    }

    /// <summary>
    /// A table outside the search path is sampled through its qualified name.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void AnalyzeColumnHandlesNonPublicSchema()
    {
        _ = Spi.Execute("CREATE SCHEMA _sch_test");
        _ = Spi.Execute("CREATE TABLE _sch_test.t (v text)");
        _ = Spi.Execute("INSERT INTO _sch_test.t SELECT repeat('x', 100) FROM generate_series(1, 5)");
        int sampled = Spi.ExecuteScalar<int>("SELECT sampled_rows FROM pglz_analyze_column('_sch_test.t'::regclass, 'v', 5)");
        Require(sampled == 5, $"expected 5 sampled rows, got {sampled}");
    }

    /// <summary>
    /// The allocating wrapper compresses and restores 1024 bytes exactly.
    /// </summary>
    [PgTest]
    public static void WrapperRoundtripDefault()
    {
        byte[] source = Repeat("abcd", 256);
        byte[] compressed = Pglz.Compress(source, PglzStrategy.Default) ?? throw new InvalidOperationException("input should be accepted");
        Require(compressed.Length < source.Length, "expected compression to shrink input");
        Require(Pglz.Decompress(compressed, source.Length, checkComplete: true).AsSpan().SequenceEqual(source), "round trip changed the bytes");
    }

    /// <summary>
    /// Empty input is below the minimum size, and decompressing zero bytes produces an empty array.
    /// </summary>
    [PgTest]
    public static void WrapperEmptyInputIsRejected()
    {
        Require(Pglz.Compress([], PglzStrategy.Default) is null, "expected empty input to be rejected");
        Require(Pglz.Decompress([], 0, checkComplete: true).Length == 0, "expected an empty result");
    }

    /// <summary>
    /// A negative raw size, which pgrx's unsigned size cannot express as anything but an oversized length, is rejected.
    /// </summary>
    [PgTest]
    public static void WrapperRejectsRawSizeOutsideInt32() => Expect<ArgumentOutOfRangeException>(() => Pglz.Decompress([], -1, checkComplete: true));

    /// <summary>
    /// The span wrapper compresses and restores 1200 bytes through caller-owned buffers.
    /// </summary>
    [PgTest]
    public static void IntoRoundtripDefault()
    {
        byte[] source = Repeat("hello world ", 100);
        byte[] compressed = new byte[Pglz.MaxOutput(source.Length)];
        Require(Pglz.TryCompress(source, compressed, PglzStrategy.Default, out int written), "input should be accepted");
        Require(written < source.Length, "expected compression to shrink input");
        byte[] restored = new byte[source.Length];
        int length = Pglz.Decompress(compressed.AsSpan(0, written), restored, source.Length, checkComplete: true);
        Require(length == source.Length && restored.AsSpan().SequenceEqual(source), "round trip changed the bytes");
    }

    /// <summary>
    /// Twelve high-entropy bytes are below the minimum input size.
    /// </summary>
    [PgTest]
    public static void IntoCompressRejectsRandomShortInput()
    {
        byte[] source = [0x91, 0xa2, 0xb3, 0xc4, 0xd5, 0xe6, 0xf7, 0x08, 0x19, 0x2a, 0x3b, 0x4c];
        byte[] buffer = new byte[Pglz.MaxOutput(source.Length)];
        Require(!Pglz.TryCompress(source, buffer, PglzStrategy.Default, out _), "expected PGLZ to reject random short input");
    }

    /// <summary>
    /// Garbage input fails decompression even without the completeness check.
    /// </summary>
    [PgTest]
    public static void IntoDecompressRejectsCorruptedInput()
    {
        byte[] garbage = [.. Enumerable.Repeat((byte)0xff, 64)];
        Expect<InvalidDataException>(() => Pglz.Decompress(garbage, new byte[256], 256, checkComplete: false));
    }

    /// <summary>
    /// The always strategy attempts 64 repeated bytes, and its output round-trips.
    /// </summary>
    [PgTest]
    public static void IntoStrategyAlwaysSucceedsOnInputDefaultMightReject()
    {
        byte[] source = [.. Enumerable.Repeat((byte)'a', 64)];
        byte[] compressed = new byte[Pglz.MaxOutput(source.Length)];
        Require(Pglz.TryCompress(source, compressed, PglzStrategy.Always, out int written),
            "the always strategy should accept highly compressible input");
        byte[] restored = new byte[source.Length];
        int length = Pglz.Decompress(compressed.AsSpan(0, written), restored, source.Length, checkComplete: true);
        Require(restored.AsSpan(0, length).SequenceEqual(source), "round trip changed the bytes");
    }

    /// <summary>
    /// A trailing byte fails the completeness check and is ignored without it.
    /// </summary>
    [PgTest]
    public static void IntoCheckCompleteFlagBehaviour()
    {
        byte[] source = Repeat("abcd", 12);
        byte[] compressed = new byte[Pglz.MaxOutput(source.Length)];
        Require(Pglz.TryCompress(source, compressed, PglzStrategy.Default, out int written), "should compress");
        byte[] padded = [.. compressed.AsSpan(0, written), 0];
        byte[] restored = new byte[source.Length];
        Expect<InvalidDataException>(() => Pglz.Decompress(padded, restored, source.Length, checkComplete: true));
        int length = Pglz.Decompress(padded, restored, source.Length, checkComplete: false);
        Require(restored.AsSpan(0, length).SequenceEqual(source), "the payload should still decode");
    }

    /// <summary>
    /// A destination larger than the raw size is accepted and only its prefix is written.
    /// </summary>
    [PgTest]
    public static void IntoDecompressWithOversizedDestBuffer()
    {
        byte[] source = Repeat("xyzxyzxyzxyzxyzxyzxyzxyzxyzxyz", 20);
        byte[] compressed = new byte[Pglz.MaxOutput(source.Length)];
        Require(Pglz.TryCompress(source, compressed, PglzStrategy.Default, out int written), "should compress");
        byte[] scratch = new byte[4096];
        int length = Pglz.Decompress(compressed.AsSpan(0, written), scratch, source.Length, checkComplete: true);
        Require(length == source.Length && scratch.AsSpan(0, length).SequenceEqual(source), "oversized destination changed the result");
    }

    /// <summary>
    /// A bytea column is measured by its stored length, without a text round trip.
    /// </summary>
    [PgTest(SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void AnalyzeColumnByteaColumn()
    {
        _ = Spi.Execute("CREATE TABLE _t_bytea (b bytea)");
        _ = Spi.Execute("INSERT INTO _t_bytea SELECT decode(repeat('0011', 250), 'hex') FROM generate_series(1, 20)");
        (int sampled, double averageRaw) = Spi.ExecuteScalars<int, double>(
            "SELECT sampled_rows, avg_raw_bytes FROM pglz_analyze_column('_t_bytea'::regclass, 'b', 20)");
        Require(sampled == 20, $"expected 20 sampled rows, got {sampled}");
        Require(Math.Abs(averageRaw - 500.0) < 1.0, $"avg_raw should be the native bytea length (500), got {averageRaw}");
    }

    /// <summary>
    /// A misspelled strategy raises a PostgreSQL error.
    /// </summary>
    [PgTest(ExpectedError = "unknown strategy \"alwyas\": expected 'default' or 'always'", SearchPath = [PgSearchPath.ExtensionSchema])]
    public static void AnalyzeColumnUnknownStrategyErrors()
    {
        _ = Spi.Execute("CREATE TABLE _t_strat (v text)");
        _ = Spi.Execute("INSERT INTO _t_strat VALUES ('x')");
        _ = Spi.ExecuteScalar<int>("SELECT sampled_rows FROM pglz_analyze_column('_t_strat'::regclass, 'v', 1, 'alwyas')");
    }

    /// <summary>
    /// The maximum output adds four bytes and saturates at the largest span length.
    /// </summary>
    [PgTest]
    public static void MaxOutputAddsFour()
    {
        Require(Pglz.MaxOutput(0) == 4, "max_output(0)");
        Require(Pglz.MaxOutput(1024) == 1028, "max_output(1024)");
        Require(Pglz.MaxOutput(int.MaxValue - 4) == int.MaxValue, "max_output(int.MaxValue - 4)");
        Require(Pglz.MaxOutput(int.MaxValue) == int.MaxValue, "max_output saturates");
    }

    /// <summary>
    /// A destination one byte short of the maximum output is rejected before PGLZ runs.
    /// </summary>
    [PgTest]
    public static void CompressIntoRejectsUndersizedBuffer()
        => Expect<ArgumentException>(() => Pglz.TryCompress(new byte[1024], new byte[1027], PglzStrategy.Default, out _));

    /// <summary>
    /// A destination smaller than the raw size is rejected before PGLZ runs.
    /// </summary>
    [PgTest]
    public static void DecompressIntoRejectsUndersizedBuffer()
        => Expect<ArgumentException>(() => Pglz.Decompress([], new byte[4], 16, checkComplete: true));

    /// <summary>
    /// A raw size outside the native signed range is rejected before PGLZ runs.
    /// </summary>
    [PgTest]
    public static void InputTooLargeIsRejected()
        => Expect<ArgumentOutOfRangeException>(() => Pglz.Decompress(new byte[4], new byte[4], int.MinValue, checkComplete: true));

    /// <summary>
    /// Repeats ASCII text as bytes.
    /// </summary>
    private static byte[] Repeat(string text, int count) => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(text, count)));

    /// <summary>
    /// Fails the backend test with a message when a condition does not hold.
    /// </summary>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Requires an exception of exactly <typeparamref name="TException"/>.
    /// </summary>
    private static void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (Exception error) when (error.GetType() == typeof(TException))
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
