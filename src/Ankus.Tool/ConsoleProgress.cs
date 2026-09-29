namespace Ankus.Tool;

/// <summary>
/// Writes command progress synchronously so messages precede the command's final result.
/// </summary>
internal sealed class ConsoleProgress : IProgress<string>
{
    /// <inheritdoc />
    public void Report(string value) => Console.WriteLine(value);
}
