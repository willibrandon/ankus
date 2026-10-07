using System.Runtime.CompilerServices;

namespace Ankus;

/// <summary>
/// Supplies benchmark helpers whose behavior remains stable under Native AOT optimization.
/// </summary>
public static class PgBenchmark
{
    /// <summary>
    /// Prevents the compiler from proving that a benchmark input or result is unused.
    /// </summary>
    /// <typeparam name="T">The input and result type, including ref structs.</typeparam>
    /// <param name="value">The value to hide from optimization.</param>
    /// <returns>The same value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static T BlackBox<T>(T value)
        where T : allows ref struct
        => value;
}
