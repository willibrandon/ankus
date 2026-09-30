using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Supplies a real PostgreSQL row estimate through a managed planner callback.
/// </summary>
[PgSchema("ankus_planner")]
public static class PlannerSupportFunctions
{
    [ThreadStatic]
    private static bool s_failNextEstimate;

    /// <summary>
    /// Returns three values while its support function overrides the default row estimate.
    /// </summary>
    /// <returns>The exact generated values.</returns>
    [PgFunction(Name = "numbers", Rows = 1000)]
    [PgSupportFunction(typeof(PlannerSupportFunctions), nameof(ZEstimate))]
    public static IEnumerable<int> AValues() => [1, 2, 3];

    /// <summary>
    /// Arms one error so the next row-estimation callback exercises native error recovery.
    /// </summary>
    [PgFunction]
    public static void ArmFailure() => s_failNextEstimate = true;

    /// <summary>
    /// Updates a PostgreSQL-owned row request and declines every unsupported request with a present zero pointer.
    /// </summary>
    /// <param name="request">The planner-owned internal request, valid only during this call.</param>
    /// <returns>The accepted request or a present zero pointer; never SQL NULL.</returns>
    [PgFunction(Name = "row_estimate")]
    public static unsafe PgInternal ZEstimate(PgInternal request)
    {
        PgMemoryContext owner = PgMemoryContext.Current;
        void* address = (void*)request.Datum.DangerousGetBits();
        PgNodeReference<Node> node = PgNodes.Borrow(owner.DangerousBorrow<Node>(address)!);
        if (node.Tag != (uint)NodeTag.T_SupportRequestRows)
        {
            return new PgInternal(PgDatum.DangerousCreate(0, 2281, owner));
        }

        if (s_failNextEstimate)
        {
            s_failNextEstimate = false;
            throw new InvalidOperationException("managed planner estimate failed");
        }

        PgNodeReference<SupportRequestRows> rows = PgNodes.Borrow(owner.DangerousBorrow<SupportRequestRows>(address)!);
        SupportRequestRows changed = rows.Value;
        changed.rows = 37;
        rows.Value = changed;
        return request;
    }
}
