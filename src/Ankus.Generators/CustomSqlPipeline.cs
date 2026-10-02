using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Separates assembly SQL constants, tracked file selection and validated text from current graph composition.
/// </summary>
internal static class CustomSqlPipeline
{
    /// <summary>
    /// Registers value-comparable inputs and per-block content resolution without retaining compiler objects.
    /// </summary>
    /// <param name="context">The generator registration context.</param>
    /// <param name="projectDirectory">The independently evaluated project directory.</param>
    /// <returns>The resolved blocks with current graph options and diagnostic coordinates.</returns>
    internal static IncrementalValueProvider<EquatableArray<Output>> Register(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<string> projectDirectory)
    {
        IncrementalValuesProvider<Analysis> analysis = context.CompilationProvider
            .Select(static (compilation, token) => Read(compilation, token))
            .SelectMany(static (values, _) => values).WithTrackingName("CustomSqlAnalysis");
        IncrementalValuesProvider<Input> inputs = analysis.Select(static (value, _) => value.Input)
            .WithTrackingName("CustomSqlInput");
        IncrementalValueProvider<ImmutableArray<string>> paths = context.AdditionalTextsProvider.Select(static (file, _) => file.Path).Collect();
        IncrementalValueProvider<EquatableArray<string>> selectedPaths = inputs.Collect()
            .Combine(paths).Combine(projectDirectory)
            .Select(static (value, _) => new EquatableArray<string>(value.Left.Left.Where(static input => input.File && input.ValidArguments)
                .Select(input => CustomSql.SelectFilePath(input.Content, value.Left.Right, value.Right).Path)
                .Where(static path => path is not null).Select(static path => path!)
                .Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal)))
            .WithTrackingName("CustomSqlSelectedPaths");
        IncrementalValueProvider<EquatableArray<FileInput>> fileInputs = context.AdditionalTextsProvider.Combine(selectedPaths)
            .Where(static value => value.Right.Contains(value.Left.Path, StringComparer.Ordinal))
            .Select(static (value, _) => value.Left)
            .Select(static (file, token) => new FileInput(file.Path, file.GetText(token)?.ToString()))
            .Collect().Select(static (values, _) => new EquatableArray<FileInput>(values));
        IncrementalValuesProvider<Selection> selection = inputs.Combine(fileInputs).Combine(projectDirectory).Combine(paths)
            .Select(static (value, _) => CustomSql.Select(value.Left.Left.Left, value.Left.Left.Right, value.Left.Right, value.Right))
            .WithTrackingName("CustomSqlSelection");
        IncrementalValuesProvider<Resolution> resolution = selection.Select(static (value, _) => CustomSql.Resolve(value))
            .WithTrackingName("CustomSqlResolution");
        return analysis.Collect().Combine(resolution.Collect()).Select(static (value, _) =>
            new EquatableArray<Output>(value.Left.Select((item, index) => new Output(item, value.Right[index]))));
    }

    /// <summary>
    /// Reads authored assembly constants and detaches diagnostic coordinates from the current compilation.
    /// </summary>
    private static EquatableArray<Analysis> Read(Compilation compilation, CancellationToken cancellationToken)
        => new(compilation.Assembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() is
            "Ankus.PgSqlAttribute" or "Ankus.PgSqlFileAttribute").Select(attribute => new Analysis(
                new(attribute.ConstructorArguments.Length == 2, attribute.ConstructorArguments.ElementAtOrDefault(0).Value as string,
                    attribute.ConstructorArguments.ElementAtOrDefault(1).Value as string, attribute.AttributeClass?.Name == "PgSqlFileAttribute"),
                AttributeValues.Get(attribute, "Order", 0), AttributeValues.Get(attribute, "Relocatable", false),
                SqlDeclarationOptions.Read(attribute)!,
                GeneratorLocation.Create(attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(), compilation))));

    /// <summary>
    /// Contains only values needed to select and validate one SQL block's exact text.
    /// </summary>
    /// <param name="ValidArguments">Whether the constructor supplies the required two arguments.</param>
    /// <param name="Name">The authored dependency name, retaining invalid values.</param>
    /// <param name="Content">The authored inline SQL or additional-file path.</param>
    /// <param name="File">Whether content is a file path.</param>
    internal sealed record Input(bool ValidArguments, string? Name, string? Content, bool File);

    /// <summary>
    /// Retains graph-only settings and current diagnostic coordinates separately from content resolution.
    /// </summary>
    /// <param name="Input">The detached text resolution input.</param>
    /// <param name="Order">The authored installation ordering value.</param>
    /// <param name="Relocatable">Whether the block permits schema relocation.</param>
    /// <param name="Options">The detached dependency options.</param>
    /// <param name="Location">The current authored attribute coordinates.</param>
    internal sealed record Analysis(Input Input, int Order, bool Relocatable, SqlDeclarationOptions Options, GeneratorLocation? Location);

    /// <summary>
    /// Owns compiler-tracked file content with ordered value equality.
    /// </summary>
    /// <param name="Path">The exact additional input path.</param>
    /// <param name="Text">The tracked contents, or null for an unreadable input.</param>
    internal sealed record FileInput(string Path, string? Text);

    /// <summary>
    /// Retains a unique tracked path without reading its file contents.
    /// </summary>
    /// <param name="Path">The original selected compiler path, or null when selection fails.</param>
    /// <param name="Error">The existing path-selection diagnostic.</param>
    internal sealed record FileSelection(string? Path, string? Error);

    /// <summary>
    /// Contains only the selected SQL and diagnostics that affect text validation.
    /// </summary>
    /// <param name="Name">The authored block name.</param>
    /// <param name="Sql">The exact selected SQL text.</param>
    /// <param name="Error">An input selection error, or null on success.</param>
    internal sealed record Selection(string? Name, string? Sql, string? Error);

    /// <summary>
    /// Owns a validated block's text or error independently of graph and compiler state.
    /// </summary>
    /// <param name="Name">The authored block name.</param>
    /// <param name="Sql">The validated exact SQL text, or null on failure.</param>
    /// <param name="Error">The validation error, or null on success.</param>
    internal sealed record Resolution(string? Name, string? Sql, string? Error);

    /// <summary>
    /// Supplies cached text and current metadata for one installation graph node.
    /// </summary>
    /// <param name="Analysis">The current detached declaration metadata.</param>
    /// <param name="Resolution">The independently cached text resolution.</param>
    internal sealed record Output(Analysis Analysis, Resolution Resolution);
}
