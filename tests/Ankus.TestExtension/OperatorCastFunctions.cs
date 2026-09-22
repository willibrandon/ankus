namespace Ankus.TestExtension;

/// <summary>
/// Exercises attributed operators and casts through PostgreSQL's expression resolver and planner.
/// </summary>
[PgSchema("operator_values", Id = "operators.schema")]
public static class OperatorCastFunctions
{
    /// <summary>
    /// Isolates explicit casts from PostgreSQL's built-in coercion paths.
    /// </summary>
    [PgEnum(Name = "explicit_token")]
    public enum ExplicitToken
    {
        /// <summary>
        /// A deliberately nonordinal value.
        /// </summary>
        First = 7,

        /// <summary>
        /// A second nonordinal value.
        /// </summary>
        Second = 19,

        /// <summary>
        /// A present source value whose cast returns SQL NULL.
        /// </summary>
        Empty = -2,

        /// <summary>
        /// A label whose conversion reports an error.
        /// </summary>
        Invalid = -1,
    }

    /// <summary>
    /// Isolates assignment casts from explicit and implicit cast declarations.
    /// </summary>
    [PgEnum(Name = "assignment_token")]
    public enum AssignmentToken
    {
        /// <summary>
        /// The only assignment input.
        /// </summary>
        Value = 23,
    }

    /// <summary>
    /// Isolates implicit casts from assignment and explicit cast declarations.
    /// </summary>
    [PgEnum(Name = "implicit_token")]
    public enum ImplicitToken
    {
        /// <summary>
        /// The only implicit input.
        /// </summary>
        Value = 37,
    }

    /// <summary>
    /// Identifies an input whose numeric cast reports target typmod and coercion context.
    /// </summary>
    [PgEnum(Name = "metadata_token")]
    public enum MetadataToken
    {
        /// <summary>
        /// The metadata probe input.
        /// </summary>
        Value,
    }

    /// <summary>
    /// Exposes argument order and distinct SQL integer widths.
    /// </summary>
    [PgOperator("#-", Commutator = "-#", Id = "operators.mixed")]
    [PgFunction(Name = "mixed_difference", Id = "operators.mixed.function", Volatility = PgVolatility.Immutable,
        ParallelSafety = PgParallelSafety.Safe, Cost = 2.5)]
    public static long OperatorDifference(int left, long right) => checked(left * 1000L - right);

    /// <summary>
    /// Fills the commutator shell using reversed SQL argument types.
    /// </summary>
    [PgOperator("-#", Commutator = "#-", Requires = ["operators.mixed"])]
    public static long OperatorCommutedDifference(long left, int right) => OperatorDifference(right, left);

    /// <summary>
    /// Exposes a right-only prefix operator.
    /// </summary>
    [PgOperator("~#")]
    public static int OperatorPrefix(int value) => checked(-value - 10);

    /// <summary>
    /// Distinguishes nullable dispatch from PostgreSQL STRICT short circuiting.
    /// </summary>
    [PgOperator("?#")]
    public static int? OperatorNullable(int? left, int? right)
        => left is null && right is null ? null : checked((left ?? 100) + (right ?? 200));

    /// <summary>
    /// Preserves shaped enum arrays through direct and nested SPI operator calls.
    /// </summary>
    [PgOperator("||#")]
    public static PgArray<ExplicitToken?>? OperatorArray(PgArray<ExplicitToken?>? value, int mode)
        => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Promises integer equality semantics suitable for explicitly supplied hash and B-tree families.
    /// </summary>
    [PgOperator("@=", Commutator = "@=", Negator = "@<>", RestrictionEstimator = "pg_catalog.eqsel",
        JoinEstimator = "pg_catalog.eqjoinsel", Hashes = true, Merges = true, Id = "operators.equal")]
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static bool OperatorEqual(int left, int right) => left == right;

    /// <summary>
    /// Fills the forward negator shell and keeps reciprocal catalog links.
    /// </summary>
    [PgOperator("@<>", Commutator = "@<>", Negator = "@=", Requires = ["operators.equal"])]
    [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe)]
    public static bool OperatorNotEqual(int left, int right) => left != right;

    /// <summary>
    /// Exercises a managed failure followed by a successful operator invocation.
    /// </summary>
    [PgOperator("/#")]
    public static int OperatorDivide(int left, int right) => left / right;

    /// <summary>
    /// Preserves an explicit cast's nullable source and destination contract.
    /// </summary>
    [PgCast(Id = "operators.explicit.cast")]
    [PgFunction(Name = "cast_explicit", Id = "operators.explicit.function", Volatility = PgVolatility.Immutable,
        ParallelSafety = PgParallelSafety.Safe)]
    public static int? CastExplicit(ExplicitToken? value) => value switch
    {
        null => 101,
        ExplicitToken.Empty => null,
        ExplicitToken.Invalid => throw new PgException("22003", "The token has no integer value."),
        _ => (int?)value,
    };

    /// <summary>
    /// Allows table assignment without making the cast an expression-resolution candidate.
    /// </summary>
    [PgCast(PgCastContext.Assignment)]
    public static int CastAssignment(AssignmentToken value) => (int)value;

    /// <summary>
    /// Allows PostgreSQL to resolve an integer function from an enum argument.
    /// </summary>
    [PgCast(PgCastContext.Implicit)]
    public static int CastImplicit(ImplicitToken value) => (int)value;

    /// <summary>
    /// Proves that PostgreSQL supplies the absent target modifier to two-argument cast functions.
    /// </summary>
    [PgCast]
    public static long CastTwoArguments(AssignmentToken value, int typmod) => (long)value * 100 + typmod;

    /// <summary>
    /// Reports PostgreSQL's packed numeric typmod and explicit flag without imposing an unrelated value policy.
    /// </summary>
    [PgCast(PgCastContext.Assignment)]
    public static PgNumeric CastMetadata(MetadataToken value, int typmod, bool isExplicit)
        => PgNumeric.FromInteger(checked((long)typmod * 10 + (isExplicit ? 1 : 0) + (int)value));

    /// <summary>
    /// Converts enum array values while retaining SQL NULL elements, dimensions and lower bounds.
    /// </summary>
    [PgCast]
    public static PgArray<long?>? CastArray(PgArray<ExplicitToken?>? value)
    {
        if (value is null)
        {
            return null;
        }

        long?[] values = [.. value.Select(static item => item is null ? (long?)null : checked((long)item + 100))];
        return new PgArray<long?>(values, value.Lengths, value.LowerBounds);
    }

    /// <summary>
    /// Exercises native subtransaction recovery while operators and casts reenter the extension.
    /// </summary>
    [PgFunction]
    public static string OperatorCastRecovery() => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE operator_cast_writes(value int); INSERT INTO operator_cast_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM operator_cast_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int failures = 0;
        int finalized = 0;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                session.Execute("""
                    WITH written AS (INSERT INTO operator_cast_writes VALUES (99) RETURNING value)
                    SELECT value OPERATOR(operator_values./#) 0 FROM written
                    """);
            }
            catch (PgException error) when (error.SqlState == "38000")
            {
                failures++;
            }
            finally
            {
                finalized++;
            }

            try
            {
                session.Execute("""
                    WITH written AS (INSERT INTO operator_cast_writes VALUES (98) RETURNING value)
                    SELECT (CASE WHEN value = 98 THEN 'Invalid'::operator_values.explicit_token
                        ELSE 'First'::operator_values.explicit_token END)::int FROM written
                    """);
            }
            catch (PgException error) when (error.SqlState == "22003")
            {
                failures++;
            }

            int result = session.ExecuteScalar<int>("SELECT ('First'::operator_values.explicit_token::int) OPERATOR(operator_values./#) 1");
            if (result != 7)
            {
                throw new InvalidOperationException("The operator or cast failed after backend recovery.");
            }
        }

        session.Execute("INSERT INTO operator_cast_writes VALUES (2)");
        return $"{failures}:{finalized}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });
}
