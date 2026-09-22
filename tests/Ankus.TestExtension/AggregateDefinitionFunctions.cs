namespace Ankus.TestExtension;

/// <summary>
/// Exposes catalog options and strict native seeding of binary-compatible state types.
/// </summary>
public static class AggregateDefinitionFunctions
{
    private static int s_seedCalls;

    /// <summary>
    /// Clears the actual transition count for strict state seeding probes.
    /// </summary>
    [PgFunction]
    public static void AggregateSeedReset() => s_seedCalls = 0;

    /// <summary>
    /// Gets the number of callbacks that PostgreSQL actually invoked after seeding.
    /// </summary>
    /// <returns>The transition count.</returns>
    [PgFunction]
    public static int AggregateSeedCalls() => s_seedCalls;

    /// <summary>
    /// Preserves an integer's full bit pattern when PostgreSQL seeds an OID state directly.
    /// </summary>
    [PgAggregate(Name = "state_seed_oid")]
    public static class SeedOid
    {
        /// <summary>
        /// Returns the native seed and counts every subsequent transition.
        /// </summary>
        public static uint Transition(uint state, int value)
        {
            s_seedCalls++;
            return state;
        }
    }

    /// <summary>
    /// Uses PostgreSQL's implicit binary-compatible CIDR-to-INET seed conversion.
    /// </summary>
    [PgAggregate(Name = "state_seed_inet")]
    public static class SeedInet
    {
        /// <summary>
        /// Returns the owned seed after the second network reaches managed code.
        /// </summary>
        public static PgInet Transition(PgInet state, PgCidr value)
        {
            s_seedCalls++;
            return state;
        }
    }

    /// <summary>
    /// Preserves explicit size, initial, final-extra, modification, and parallel options in PostgreSQL's catalog.
    /// </summary>
    [PgAggregate(Name = "state_catalog_options", InitialCondition = "0", MovingInitialCondition = "0",
        StateSize = int.MaxValue, MovingStateSize = 4096, FinalExtra = true, MovingFinalExtra = true,
        FinalModify = PgAggregateFinalModify.Shareable, MovingFinalModify = PgAggregateFinalModify.ReadOnly,
        ParallelSafety = PgParallelSafety.Restricted)]
    public static class CatalogOptions
    {
        /// <summary>
        /// Adds integer inputs to a required bigint state with distinct support-function options.
        /// </summary>
        [PgFunction(Volatility = PgVolatility.Immutable, ParallelSafety = PgParallelSafety.Safe, Cost = 42)]
        public static long Transition(long state, int value) => checked(state + value);

        /// <summary>
        /// Maintains the same result through an independently configured moving state.
        /// </summary>
        public static long MovingTransition(long state, int value) => checked(state + value);

        /// <summary>
        /// Removes a departing value from the moving state.
        /// </summary>
        public static long MovingInverse(long state, int value) => checked(state - value);
    }

    /// <summary>
    /// Retains PostgreSQL-supported final flags even when no final or moving callback exists.
    /// </summary>
    [PgAggregate(Name = "state_catalog_flags", InitialCondition = "0", FinalExtra = true,
        MovingFinalExtra = true, FinalModify = PgAggregateFinalModify.ReadWrite,
        MovingFinalModify = PgAggregateFinalModify.Shareable)]
    public static class CatalogFlags
    {
        /// <summary>
        /// Adds required values without a separate final function.
        /// </summary>
        public static int Transition(int state, int value) => checked(state + value);
    }
}
