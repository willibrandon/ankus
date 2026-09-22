namespace Ankus.TestExtension;

/// <summary>
/// Exercises native SQL quoting, JSON EXPLAIN plans, and edits to owned SPI result rows.
/// </summary>
public static class SpiHelperFunctions
{
    /// <summary>
    /// Quotes one identifier through PostgreSQL.
    /// </summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The SQL fragment.</returns>
    [PgFunction]
    public static string SqlQuoteIdentifier(string value) => Spi.QuoteIdentifier(value);

    /// <summary>
    /// Quotes two identifier components through PostgreSQL.
    /// </summary>
    /// <param name="qualifier">The optional qualifier.</param>
    /// <param name="identifier">The required identifier.</param>
    /// <returns>The qualified SQL fragment.</returns>
    [PgFunction]
    public static string SqlQuoteQualified(string? qualifier, string identifier) => Spi.QuoteQualifiedIdentifier(qualifier, identifier);

    /// <summary>
    /// Quotes a text literal through PostgreSQL.
    /// </summary>
    /// <param name="value">The literal.</param>
    /// <returns>The SQL literal.</returns>
    [PgFunction]
    public static string SqlQuoteLiteral(string value) => Spi.QuoteLiteral(value);

    /// <summary>
    /// Calls quoting from inside a session without disturbing its active connection.
    /// </summary>
    /// <param name="identifier">The untrusted identifier spelling.</param>
    /// <param name="value">The untrusted literal.</param>
    /// <returns>The dynamically selected literal.</returns>
    [PgFunction]
    public static string SqlQuotedQuery(string identifier, string value)
        => Spi.Connect(session =>
        {
            string name = Spi.QuoteIdentifier(identifier);
            string sql = "SELECT " + name + " FROM (SELECT " + Spi.QuoteLiteral(value) + " AS " + name + ") AS quoted";
            return session.ExecuteScalar<string>(sql);
        });

    /// <summary>
    /// Exercises validation before native quotation can see a truncated or malformed string.
    /// </summary>
    /// <param name="mode">The invalid argument case.</param>
    /// <returns>The unreachable SQL result.</returns>
    [PgFunction]
    public static string SqlQuoteInvalid(int mode) => mode switch
    {
        0 => Spi.QuoteIdentifier("bad\0name"),
        1 => Spi.QuoteLiteral("bad\0literal"),
        2 => Spi.QuoteQualifiedIdentifier("bad\0schema", "table"),
        3 => Spi.QuoteQualifiedIdentifier("schema", "bad\0table"),
        4 => Spi.QuoteIdentifier("\uD800"),
        5 => Spi.QuoteIdentifier(null!),
        6 => Spi.QuoteLiteral(null!),
        7 => Spi.QuoteQualifiedIdentifier("schema", null!),
        8 => Spi.QuoteLiteral("🐘"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// Verifies quotation APIs retain backend-thread affinity.
    /// </summary>
    /// <returns>The worker rejection diagnostic.</returns>
    [PgFunction]
    public static string SqlQuoteWorker()
        => Task.Run(() =>
        {
            try
            {
                return Spi.QuoteIdentifier("value");
            }
            catch (InvalidOperationException error)
            {
                return error.Message;
            }
        }).GetAwaiter().GetResult();

    /// <summary>
    /// Explains SQL with an optional typed parameter, using a standalone or scoped connection.
    /// </summary>
    /// <param name="query">The statement to explain.</param>
    /// <param name="value">The parameter value.</param>
    /// <param name="bind">Whether to bind the parameter.</param>
    /// <param name="session">Whether to use a scoped session.</param>
    /// <returns>The independently owned JSON plan.</returns>
    [PgFunction]
    public static PgJson SqlExplain(string query, int? value, bool bind, bool session)
    {
        SpiParameter[] parameters = bind ? [SpiParameter.Create(value)] : [];
        PgJson plan = session ? Spi.Connect(client => client.Explain(query, parameters)) : Spi.Explain(query, parameters);
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        return plan;
    }

    /// <summary>
    /// Edits rows after the session closes and demonstrates independent row values, cell types, and original metadata.
    /// </summary>
    /// <returns>The edited values and unchanged database value.</returns>
    [PgFunction]
    public static string SqlEditRows()
    {
        SpiResult result = Spi.Connect(session =>
        {
            session.Execute("CREATE TEMP TABLE row_values (value int); INSERT INTO row_values VALUES (1), (2)");
            return session.Query("SELECT value FROM row_values ORDER BY value");
        });
        result[0].Set("value", new PgJsonb("[42]"));
        result[1].Set<long?>(0, null);
        return result[0].Get<PgJsonb>(0).Text + "|" + result[0].GetTypeOid(0) + "|" +
            (result[1][0] is null) + "|" + result[1].GetTypeOid(0) + "|" + result.Columns[0].TypeOid + "|" +
            Spi.ExecuteScalar<long>("SELECT sum(value) FROM row_values");
    }

    /// <summary>
    /// Verifies replacing a domain value updates only its local cell type, preserving declared domain metadata elsewhere.
    /// </summary>
    /// <param name="query">The domain query.</param>
    /// <returns>Whether the original domain identity is preserved around the edited cell.</returns>
    [PgFunction]
    public static bool SqlEditDomain(string query)
    {
        SpiResult rows = Spi.Query(query);
        uint original = rows.Columns[0].TypeOid;
        bool before = rows[0].GetTypeOid(0) == original && rows[1].GetTypeOid(0) == original && original != 23;
        rows[0].Set(0, "new value");
        return before && rows[0].Get<string>(0) == "new value" && rows[0].GetTypeOid(0) == 25 &&
            rows[1].Get<int>(0) == 2 && rows[1].GetTypeOid(0) == original && rows.Columns[0].TypeOid == original;
    }

    /// <summary>
    /// Measures temporary subtransaction-context growth after repeated native quotation or session parameter conversion.
    /// </summary>
    /// <param name="quote">Whether to exercise quotation.</param>
    /// <returns>The count of extra transaction contexts left behind.</returns>
    [PgFunction]
    public static long SqlHelperContextGrowth(bool quote)
        => Spi.Connect(session =>
        {
            const string countSql = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name = 'CurTransactionContext'";
            string value = new('x', 10000);
            session.ExecuteScalar<string>("SELECT $1::text", SpiParameter.Create(value));
            Spi.QuoteLiteral(value);
            long before = session.ExecuteScalar<long>(countSql);
            for (int index = 0; index < 100; index++)
            {
                string result = quote ? Spi.QuoteLiteral(value)
                    : session.ExecuteScalar<string>("SELECT $1::text", SpiParameter.Create(value));
                if (result.Length != value.Length + (quote ? 2 : 0))
                {
                    throw new InvalidOperationException("Temporary text changed during native conversion.");
                }
            }

            return session.ExecuteScalar<long>(countSql) - before;
        });
}
