namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies collation-sensitive equality and order through actual ICU and C PostgreSQL comparison routines.
/// </summary>
public sealed partial class AggregateTests
{
    /// <summary>
    /// Case-insensitive ICU equality differs from C ordering while both context and key retain the selected collation.
    /// </summary>
    /// <param name="caseInsensitive">Whether to use the nondeterministic ICU collation instead of C.</param>
    /// <param name="descending">Whether to reverse order and put NULL first.</param>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public Task NativeComparisonHonorsNonDefaultIcuCollation(bool caseInsensitive, bool descending)
        => Run(nameof(NativeComparisonHonorsNonDefaultIcuCollation), async (connection, transaction, token) =>
        {
            await Execute(connection, transaction, """
                CREATE COLLATION aggregate_values.case_insensitive (provider=icu,locale='und-u-ks-level2',deterministic=false)
                """, token);
            Assert.IsTrue(await Scalar<bool>(connection, transaction, """
                SELECT collprovider='i' AND NOT collisdeterministic FROM pg_collation
                WHERE oid='aggregate_values.case_insensitive'::regcollation
                """, token));
            string collation = caseInsensitive ? "aggregate_values.case_insensitive" : "\"C\"";
            string order = descending ? "DESC NULLS FIRST" : "ASC NULLS LAST";
            long oid = await Scalar<long>(connection, transaction, $"SELECT '{collation}'::regcollation::oid::bigint", token);
            long less = caseInsensitive ? 0 : 1;
            long equal = caseInsensitive ? 2 : 1;
            long greater = 2;
            long[] expected = descending ? [greater, equal, less, oid, oid] : [less, equal, greater, oid, oid];
            Assert.AreSequenceEqual(expected, await Scalar<long[]>(connection, transaction, $"""
                SELECT aggregate_values.collation_compare('a' COLLATE {collation})
                    WITHIN GROUP(ORDER BY value COLLATE {collation} {order})
                FROM (VALUES('A'),('a'),('b'),(NULL)) AS input(value)
                """, token));
            Assert.AreEqual(equal, await Scalar<long>(connection, transaction, $"""
                SELECT count(*) FILTER(WHERE value COLLATE {collation} = 'a' COLLATE {collation})
                FROM (VALUES('A'),('a'),('b'),(NULL)) AS input(value)
                """, token));
            Assert.AreEqual(caseInsensitive ? 1L : 2L, await Scalar<long>(connection, transaction, $"""
                SELECT aggregate_values.hypothetical_rank('a' COLLATE {collation},1)
                    WITHIN GROUP(ORDER BY value COLLATE {collation},number)
                FROM (VALUES('A',1),('b',1)) AS input(value,number)
                """, token));
        });
}
