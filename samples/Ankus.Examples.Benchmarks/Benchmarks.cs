namespace Ankus.Examples.Benchmarks;

/// <summary>
/// Measures managed value operations and SPI writes inside PostgreSQL.
/// </summary>
public static class Benchmarks
{
    /// <summary>
    /// Measures PostgreSQL numeric addition through the managed value API.
    /// </summary>
    /// <param name="bencher">The PostgreSQL benchmark timing boundary.</param>
    [PgBenchmark]
    public static void AddNumeric(PgBencher bencher)
    {
        PgNumeric left = PgNumeric.Parse("123.45");
        PgNumeric right = PgNumeric.Parse("67.89");

        bencher.Iterate(() => PgBenchmark.BlackBox(left + right));
    }

    /// <summary>
    /// Measures a parameterized insert with fresh values and one subtransaction per batch.
    /// </summary>
    /// <param name="bencher">The PostgreSQL benchmark timing boundary.</param>
    [PgBenchmark(Setup = nameof(PrepareRows), Transaction = PgBenchmarkTransactionMode.SubtransactionPerBatch)]
    public static void InsertRows(PgBencher bencher)
        => bencher.IterateBatched(static () => 42,
            static value => Spi.Execute("INSERT INTO benchmark_rows(value) VALUES ($1)", SpiParameter.Create(value)),
            PgBenchmarkBatchSize.SmallInput);

    /// <summary>
    /// Creates the table used by the insert benchmark inside its rollback-only run.
    /// </summary>
    internal static void PrepareRows()
        => Spi.Execute("CREATE TABLE benchmark_rows(value integer NOT NULL)");
}
