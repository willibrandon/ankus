using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Validates and topologically orders installation SQL with stable output and explicit cycle diagnostics.
/// </summary>
internal sealed class SqlGraph
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS005", "Invalid installation SQL dependency", "{0}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private readonly List<SqlEntity> _entities = [];
    private readonly SourceProductionContext _context;
    private bool _invalid;

    /// <summary>
    /// Creates a graph whose validation errors are reported to the current generator run.
    /// </summary>
    /// <param name="context">The diagnostic sink and cancellation token.</param>
    internal SqlGraph(SourceProductionContext context) => _context = context;

    /// <summary>
    /// Registers an installation node.
    /// </summary>
    /// <param name="entity">The declaration node.</param>
    internal void Add(SqlEntity entity) => _entities.Add(entity);

    /// <summary>
    /// Reads explicit identifiers and dependencies shared by function, schema and SQL attributes.
    /// </summary>
    /// <param name="entity">The destination node.</param>
    /// <param name="attribute">The semantic attribute.</param>
    /// <param name="name">An explicit SQL name, or null to read the optional Id property.</param>
    internal void Configure(SqlEntity entity, AttributeData attribute, string? name = null)
    {
        name ??= AttributeValues.Get<string?>(attribute, "Id", null);
        if (name is not null)
        {
            if (ValidName(name))
            {
                entity.Names.Add(name);
            }
            else
            {
                Error(entity.Location, "Dependency identifiers must be nonempty text without zero characters or invalid Unicode.");
            }
        }

        ReadNames("Requires", entity.Requires);
        ReadNames("Before", entity.Before);

        void ReadNames(string property, HashSet<string> destination)
        {
            foreach (string? value in AttributeValues.Strings(attribute, property))
            {
                if (!ValidName(value))
                {
                    Error(entity.Location, $"'{entity.DisplayName}' has an invalid {property} identifier.");
                }
                else
                {
                    destination.Add(value!);
                }
            }
        }
    }

    /// <summary>
    /// Reports a declaration error and prevents emission of a partial installation script.
    /// </summary>
    /// <param name="location">The offending declaration.</param>
    /// <param name="message">The diagnostic detail.</param>
    internal void Error(Location? location, string message)
    {
        _invalid = true;
        _context.ReportDiagnostic(Diagnostic.Create(s_invalid, location, message));
    }

    /// <summary>
    /// Resolves references and returns deterministic dependency-ordered SQL, or null after an error.
    /// </summary>
    /// <returns>The complete script or null when the graph is invalid.</returns>
    internal string? Emit()
    {
        var names = new Dictionary<string, SqlEntity>(StringComparer.Ordinal);
        foreach (SqlEntity entity in _entities)
        {
            foreach (string name in entity.Names.OrderBy(static value => value, StringComparer.Ordinal))
            {
                if (names.ContainsKey(name))
                {
                    Error(entity.Location, $"Dependency identifier '{name}' is declared more than once.");
                }
                else
                {
                    names.Add(name, entity);
                }
            }
        }

        foreach (SqlEntity entity in _entities)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            Resolve(entity.Requires, before: false);
            Resolve(entity.Before, before: true);

            void Resolve(HashSet<string> references, bool before)
            {
                foreach (string name in references.OrderBy(static value => value, StringComparer.Ordinal))
                {
                    if (!names.TryGetValue(name, out SqlEntity? dependency))
                    {
                        Error(entity.Location, $"'{entity.DisplayName}' refers to missing dependency '{name}'.");
                    }
                    else if (before)
                    {
                        dependency.Dependencies.Add(entity);
                    }
                    else
                    {
                        entity.Dependencies.Add(dependency);
                    }
                }
            }
        }

        AddBoundary(1);
        AddBoundary(2);
        if (_invalid)
        {
            return null;
        }

        var remaining = new Dictionary<SqlEntity, int>();
        var dependents = new Dictionary<SqlEntity, List<SqlEntity>>();
        var ready = new SortedSet<SqlEntity>(Comparer<SqlEntity>.Create(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Key, right.Key)));
        foreach (SqlEntity entity in _entities)
        {
            remaining.Add(entity, entity.Dependencies.Count);
            dependents.Add(entity, []);
            if (entity.Dependencies.Count == 0)
            {
                ready.Add(entity);
            }
        }

        foreach (SqlEntity entity in _entities)
        {
            foreach (SqlEntity dependency in entity.Dependencies)
            {
                dependents[dependency].Add(entity);
            }
        }

        var result = new StringBuilder();
        int emitted = 0;
        while (ready.Count != 0)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            SqlEntity entity = ready.Min!;
            ready.Remove(entity);
            result.Append(entity.Sql);
            if (entity.Sql.Length != 0 && entity.Sql[entity.Sql.Length - 1] != '\n')
            {
                result.Append('\n');
            }

            emitted++;
            foreach (SqlEntity dependent in dependents[entity])
            {
                if (--remaining[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (emitted != _entities.Count)
        {
            SqlEntity[] blocked = [.. _entities.Where(entity => remaining[entity] != 0).OrderBy(static entity => entity.Key, StringComparer.Ordinal)];
            Error(blocked[0].Location, "Installation dependency cycle blocks: " + string.Join(", ", blocked.Select(static entity => entity.DisplayName)) + ".");
            return null;
        }

        return result.ToString();

        void AddBoundary(int order)
        {
            SqlEntity[] boundaries = [.. _entities.Where(entity => entity.Order == order)];
            if (boundaries.Length > 1)
            {
                Error(boundaries[1].Location, order == 1 ? "Only one bootstrap SQL block is allowed." : "Only one final SQL block is allowed.");
            }

            foreach (SqlEntity boundary in boundaries)
            {
                foreach (SqlEntity entity in _entities)
                {
                    if (entity == boundary)
                    {
                        continue;
                    }

                    if (order == 1)
                    {
                        entity.Dependencies.Add(boundary);
                    }
                    else
                    {
                        boundary.Dependencies.Add(entity);
                    }
                }
            }
        }
    }

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && SqlText.IsText(name!);
}
