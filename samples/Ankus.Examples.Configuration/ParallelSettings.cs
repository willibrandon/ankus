using System.Globalization;

namespace Ankus.Examples.Configuration;

/// <summary>
/// Reads native preloaded configuration after managed state initializes independently in each worker.
/// </summary>
public static partial class Settings
{
    private static readonly int s_parallelInitializationProcess = Environment.ProcessId;

    /// <summary>
    /// Gets a named mode whose alias retains its full unsigned managed value.
    /// </summary>
    [PgGucEnum("ankus_configuration.mode", ConfigurationMode.Normal, "Execution mode")]
    public static partial ConfigurationMode Mode { get; }

    /// <summary>
    /// Reads all five configuration types and the inherited postmaster setting in a parallel-safe callback.
    /// </summary>
    /// <param name="discriminator">An input identifying the row group being processed.</param>
    /// <returns>The input, process identities, typed values, and startup-only value.</returns>
    [PgFunction(ParallelSafety = PgParallelSafety.Safe, Volatility = PgVolatility.Stable)]
    public static string?[] ConfigurationParallelValues(int discriminator) =>
    [
        discriminator.ToString(CultureInfo.InvariantCulture),
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        s_parallelInitializationProcess.ToString(CultureInfo.InvariantCulture),
        Enabled.ToString(),
        User.ToString(CultureInfo.InvariantCulture),
        BitConverter.DoubleToInt64Bits(RealSeconds).ToString(CultureInfo.InvariantCulture),
        Text,
        ((ulong)Mode).ToString(CultureInfo.InvariantCulture),
        Startup.ToString(CultureInfo.InvariantCulture),
    ];
}

/// <summary>
/// Provides configuration labels independently of their managed numeric values.
/// </summary>
public enum ConfigurationMode : ulong
{
    /// <summary>
    /// Selects the ordinary operating mode.
    /// </summary>
    [PgGucLabel("normal")]
    Normal,

    /// <summary>
    /// Selects the burst operating mode.
    /// </summary>
    [PgGucLabel("burst")]
    Burst = ulong.MaxValue,

    /// <summary>
    /// Supplies an alias for the same burst operating mode.
    /// </summary>
    [PgGucLabel("turbo")]
    Turbo = Burst,
}
