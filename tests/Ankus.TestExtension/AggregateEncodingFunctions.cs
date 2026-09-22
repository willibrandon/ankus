namespace Ankus.TestExtension;

/// <summary>
/// Produces owned encoded text from internal state, including a deliberate server-encoding failure at final output.
/// </summary>
[PgSchema("aggregate_values", Create = false)]
public static class AggregateEncodingFunctions
{
    /// <summary>
    /// Retains generated Unicode strings across transitions and returns them from the final callback.
    /// </summary>
    [PgAggregate(Name = "generated_text", Requires = ["aggregate-support"])]
    public static class GeneratedText
    {
        /// <summary>
        /// Creates representable or deliberately unrepresentable managed text without client-side encoding failure.
        /// </summary>
        public static PgAggregateState<List<string>> Transition(PgAggregateState<List<string>>? state, int value)
        {
            state ??= new PgAggregateState<List<string>>([]);
            state.Value.Add(value == 1 ? "café" : "🐘");
            GC.Collect();
            Spi.Execute("SELECT repeat('churn',1000)");
            return state;
        }

        /// <summary>
        /// Preserves the generated text after managed collection and native SPI memory churn.
        /// </summary>
        public static string Final(PgAggregateState<List<string>>? state) => state is null ? "" : string.Concat(state.Value);
    }
}
