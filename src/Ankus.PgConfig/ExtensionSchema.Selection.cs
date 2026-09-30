using System.Text;

namespace Ankus.PgConfig;

public sealed partial class ExtensionSchema
{
    /// <summary>
    /// Selects declarations by exact SQL name, managed name, signature or explicit dependency identifier.
    /// </summary>
    /// <param name="names">One or more names; ambiguous names require a qualified name or signature.</param>
    /// <param name="alterExtension">Whether to wrap creation and extension attachment in one transaction.</param>
    /// <returns>The selected SQL, ordered dependency closure and attachment diagnostics.</returns>
    /// <exception cref="InvalidOperationException">The library predates embedded dependency graphs.</exception>
    /// <exception cref="ArgumentException">A selection is empty, unknown or ambiguous.</exception>
    /// <remarks>
    /// The extension must already exist when attaching objects. The concrete library must be installed in PostgreSQL's library directory.
    /// Custom SQL without declared created objects remains in the script and produces a warning when attachment is requested.
    /// </remarks>
    public ExtensionSchemaSelection Select(IEnumerable<string> names, bool alterExtension = true)
    {
        ArgumentNullException.ThrowIfNull(names);
        ExtensionSchemaGraph graph = Graph ?? throw new InvalidOperationException(
            "This library does not contain a dependency graph. Rebuild it with a current Ankus SDK to select schema items.");
        Dictionary<string, ExtensionSchemaItem> items = graph.Items.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        ILookup<string, ExtensionSchemaItem> families = graph.Items.Where(static item => item.Owner.Length != 0)
            .ToLookup(static item => item.Owner, StringComparer.Ordinal);
        ILookup<string, ExtensionSchemaItem> matches = graph.Items.SelectMany(static item => item.Names.Select(name => (Name: name, Item: item)))
            .ToLookup(static entry => entry.Name, static entry => entry.Item, StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<ExtensionSchemaItem>();
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ExtensionSchemaItem[] candidates = [.. matches[name].Select(item => item.Owner.Length == 0 ? item : items[item.Owner])
                .DistinctBy(static item => item.Id)];
            if (candidates.Length != 1)
            {
                throw new ArgumentException(candidates.Length == 0 ? $"Unknown schema item '{name}'." :
                    $"Ambiguous schema item '{name}': {string.Join(", ", candidates.Select(static item => item.Id))}. Use a qualified name or signature.", nameof(names));
            }

            Add(candidates[0]);
        }

        if (pending.Count == 0)
        {
            throw new ArgumentException("Select at least one schema item.", nameof(names));
        }

        while (pending.TryDequeue(out ExtensionSchemaItem? item))
        {
            foreach (string dependency in item.Dependencies)
            {
                Add(items[dependency]);
            }

            if (item.Owner.Length != 0)
            {
                Add(items[item.Owner]);
            }

            foreach (ExtensionSchemaItem member in families[item.Id])
            {
                Add(member);
            }
        }

        ExtensionSchemaItem[] ordered = [.. graph.Items.Where(item => selected.Contains(item.Id))];
        var script = new StringBuilder(alterExtension ? "BEGIN;\n\n" : string.Empty);
        var warnings = new List<string>();
        var attached = new HashSet<string>(StringComparer.Ordinal);
        string extension = "\"" + Name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string library = "'$libdir/" + Artifacts.Library.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal) + "'";
        string prefix = DefaultSchema is null ? string.Empty : "\"" + DefaultSchema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\".";
        foreach (ExtensionSchemaItem item in ordered)
        {
            string sql = item.SqlTemplate.Replace("\0", prefix, StringComparison.Ordinal).Replace("'MODULE_PATHNAME'", library, StringComparison.Ordinal);
            script.Append(sql);
            if (sql.Length != 0 && sql[^1] != '\n')
            {
                script.Append('\n');
            }

            bool emits = item.Sql.Length != 0 || item.Owner.Length != 0 && items[item.Owner].Sql.Length != 0;
            if (!alterExtension || !emits)
            {
                continue;
            }

            foreach (string template in item.AttachmentTemplates)
            {
                string attachment = template.Replace("\0", prefix, StringComparison.Ordinal);
                if (attached.Add(attachment))
                {
                    script.Append("ALTER EXTENSION ").Append(extension).Append(" ADD ").Append(attachment).Append(";\n");
                }
            }

            if (item.Kind == "sql" && item.Attachments.Count == 0)
            {
                warnings.Add($"Custom SQL '{(item.Names.Count == 0 ? item.Id : item.Names[0])}' has no declared created objects; attach them to the extension manually.");
            }
        }

        if (alterExtension)
        {
            script.Append("\nCOMMIT;\n");
        }

        return new ExtensionSchemaSelection(script.ToString(), ordered, [.. warnings]);

        void Add(ExtensionSchemaItem item)
        {
            if (selected.Add(item.Id))
            {
                pending.Enqueue(item);
            }
        }
    }
}
