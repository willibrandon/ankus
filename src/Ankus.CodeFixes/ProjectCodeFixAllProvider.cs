using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Ankus.CodeFixes;

/// <summary>
/// Composes a project's editor corrections before validating the compiler once for that project.
/// </summary>
/// <param name="title">The editor action title.</param>
/// <param name="apply">Applies and verifies all selected diagnostics in one project.</param>
internal sealed class ProjectCodeFixAllProvider(string title,
    Func<Project, ImmutableDictionary<DocumentId, ImmutableArray<Diagnostic>>, CancellationToken, Task<Solution>> apply) : FixAllProvider
{
    /// <summary>
    /// Defers diagnostic gathering and editing until the user requests the action or its preview.
    /// </summary>
    /// <param name="fixAllContext">The requested document, project or solution scope.</param>
    /// <returns>The lazy complete correction.</returns>
    public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
    {
        fixAllContext.CancellationToken.ThrowIfCancellationRequested();
        CodeAction? action = fixAllContext.Scope is FixAllScope.Document or FixAllScope.Project or FixAllScope.Solution
            ? CodeAction.Create(title, token => ApplyAsync(fixAllContext, token), fixAllContext.CodeActionEquivalenceKey)
            : null;
        return Task.FromResult(action);
    }

    /// <summary>
    /// Collects the selected source diagnostics and applies each project's complete correction once.
    /// </summary>
    /// <param name="context">The original diagnostic and scope selection.</param>
    /// <param name="cancellationToken">Cancels collection, editing and validation.</param>
    /// <returns>The corrected solution with unrelated projects and documents preserved.</returns>
    private async Task<Solution> ApplyAsync(FixAllContext context, CancellationToken cancellationToken)
    {
        FixAllContext selected = context.WithCancellationToken(cancellationToken);
        Project[] projects = context.Scope == FixAllScope.Solution
            ? [.. context.Solution.Projects.Where(project => project.Language == context.Project.Language)]
            : [context.Project];
        Solution solution = context.Solution;
        foreach (Project original in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableDictionary<DocumentId, ImmutableArray<Diagnostic>>.Builder diagnostics = ImmutableDictionary.CreateBuilder<DocumentId, ImmutableArray<Diagnostic>>();
            IEnumerable<Document> documents = context.Scope == FixAllScope.Document
                ? [context.Document!]
                : original.Documents;
            foreach (Document document in documents)
            {
                ImmutableArray<Diagnostic> values = await selected.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
                if (!values.IsEmpty)
                {
                    diagnostics.Add(document.Id, values);
                }
            }

            if (diagnostics.Count != 0)
            {
                solution = await apply(solution.GetProject(original.Id)!, diagnostics.ToImmutable(), cancellationToken).ConfigureAwait(false);
            }
        }

        return solution;
    }
}
