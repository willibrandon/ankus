using System.Diagnostics.CodeAnalysis;

namespace Ankus.TestExtension;

/// <summary>
/// Proves that safe implementation differences retain the interface's PostgreSQL NULL contract.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class TypedAggregateNullabilityFunctions
{
    /// <summary>
    /// Accepts SQL NULL state and inputs while returning a present managed value.
    /// </summary>
    [PgAggregate(Name = "nullable_contract", Requires = ["aggregate-support"])]
    public sealed class NullableContract : IPgAggregate<string?, string?>
    {
        /// <summary>
        /// Records each nullable input while tolerating the first NULL state.
        /// </summary>
        public static string Transition(PgAggregateContext context, string? state, string? arguments)
            => (state ?? string.Empty) + (arguments ?? "<NULL>");
    }

    /// <summary>
    /// Keeps SQL inputs required even though its implementation has broader annotations.
    /// </summary>
    [PgAggregate(Name = "required_contract", InitialCondition = "", Requires = ["aggregate-support"])]
    public sealed class RequiredContract : IPgAggregate<string, string>
    {
        /// <summary>
        /// Rejects a NULL invocation so the backend test proves that STRICT skips NULL rows.
        /// </summary>
        public static string Transition(PgAggregateContext context, string? state, string? arguments)
            => (state ?? throw new InvalidOperationException("Required state must be present.")) +
                (arguments ?? throw new InvalidOperationException("Required input must be present."));
    }

    /// <summary>
    /// Accepts SQL NULL through the standard C# input precondition attribute.
    /// </summary>
    [PgAggregate(Name = "annotated_contract", InitialCondition = "0", Requires = ["aggregate-support"])]
    public sealed class AnnotatedContract : IPgAggregate<long, string?>
    {
        /// <summary>
        /// Adds a distinct sentinel for a real SQL NULL input.
        /// </summary>
        public static long Transition(PgAggregateContext context, long state, [AllowNull] string arguments)
            => checked(state + (arguments?.Length ?? 100));
    }
}
