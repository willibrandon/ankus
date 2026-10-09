namespace Ankus.CascadeExtension;

/// <summary>
/// Calls into the extension that the control file requires.
/// </summary>
public static class Functions
{
    /// <summary>
    /// Reads the required extension's value through SPI.
    /// </summary>
    /// <returns>The dependency's value plus one.</returns>
    [PgFunction]
    public static int CascadeValue() => Spi.ExecuteScalar<int>("SELECT ankus_cascade_dependency()") + 1;
}
