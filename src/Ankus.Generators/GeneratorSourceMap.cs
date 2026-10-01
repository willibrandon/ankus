using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Freezes only the source attribution needed by detached graph composition.
/// </summary>
/// <param name="Entries">Distinct source coordinates in stable tree and span order.</param>
internal sealed record GeneratorSourceMap(EquatableArray<GeneratorSourceMap.Entry> Entries)
{
    /// <summary>
    /// Reads relevant physical and mapped positions without retaining compiler objects.
    /// </summary>
    /// <param name="locations">Declaration and diagnostic coordinates used by composition.</param>
    /// <param name="compilation">The current compilation containing those coordinates.</param>
    /// <param name="cancellationToken">The current analysis cancellation token.</param>
    /// <returns>Comparable attribution values for the detached composition step.</returns>
    internal static GeneratorSourceMap Create(IEnumerable<GeneratorLocation?> locations, Compilation compilation, CancellationToken cancellationToken)
    {
        SyntaxTree[] trees = [.. compilation.SyntaxTrees];
        var entries = new List<Entry>();
        foreach (GeneratorLocation location in locations.Where(static location => location.HasValue).Select(static location => location!.Value)
            .Distinct().OrderBy(static location => location.TreeIndex).ThenBy(static location => location.Span.Start).ThenBy(static location => location.Span.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Location source = Location.Create(trees[location.TreeIndex], location.Span);
            entries.Add(new(location, source.GetLineSpan(), source.GetMappedLineSpan()));
        }

        return new(new EquatableArray<Entry>(entries));
    }

    /// <summary>
    /// Contains portable line metadata and a tree ordinal that distinguishes duplicate file paths.
    /// </summary>
    /// <param name="Coordinates">The exact source coordinates.</param>
    /// <param name="Physical">The physical file and line span used by SQL provenance.</param>
    /// <param name="Mapped">The compiler's mapped file and line span.</param>
    internal sealed record Entry(GeneratorLocation Coordinates, FileLinePositionSpan Physical, FileLinePositionSpan Mapped);

}
