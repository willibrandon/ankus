using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
        var entries = new List<Entry>();
        foreach (GeneratorLocation location in locations.Where(static location => location.HasValue).Select(static location => location!.Value)
            .Distinct().OrderBy(static location => location.Path, StringComparer.Ordinal).ThenBy(static location => location.TreeOccurrence)
            .ThenBy(static location => location.MemberIndex).ThenBy(static location => location.Span.Start).ThenBy(static location => location.Span.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Location source = location.Resolve(compilation);
            entries.Add(new(location, AttributionLine(source.GetLineSpan()), AttributionLine(source.GetMappedLineSpan())));
        }

        return new(new EquatableArray<Entry>(entries));
    }

    /// <summary>
    /// Freezes line attribution without absolute columns that are irrelevant to generated SQL.
    /// Exact diagnostic spans are independently resolved against the current declaration.
    /// </summary>
    /// <param name="span">The current source attribution.</param>
    /// <returns>The same file and starting line, with no body-dependent column or ending position.</returns>
    private static FileLinePositionSpan AttributionLine(FileLinePositionSpan span)
    {
        var line = new LinePosition(span.StartLinePosition.Line, 0);
        return new(span.Path, line, line);
    }

    /// <summary>
    /// Contains portable line attribution and declaration coordinates that distinguish duplicate file paths.
    /// </summary>
    /// <param name="Coordinates">The declaration-relative source coordinates.</param>
    /// <param name="Physical">The physical file and starting line used by SQL provenance.</param>
    /// <param name="Mapped">The compiler's mapped file and starting line.</param>
    internal sealed record Entry(GeneratorLocation Coordinates, FileLinePositionSpan Physical, FileLinePositionSpan Mapped);

}
