namespace Ankus.Tool;

internal sealed partial class UpgradeProject
{
    /// <summary>
    /// Finds source-declared package identities without assigning conditional properties a guessed effective value.
    /// </summary>
    /// <param name="expression">The item include or update expression.</param>
    /// <returns>Distinct possible identities; version edits must remain guarded by the actual item identity.</returns>
    internal string[] GetIdentities(string expression)
        => [.. ExpandIdentity(expression, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .SelectMany(static value => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private IEnumerable<string> ExpandIdentity(string expression, HashSet<string> active)
    {
        System.Text.RegularExpressions.Match match = PropertyUse().Match(expression);
        if (!match.Success)
        {
            if (expression.Contains("$(", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Cannot resolve the package identity expression '{expression}' from source declarations.");
            }

            return [expression];
        }

        string name = match.Groups[1].Value;
        if (!active.Add(name))
        {
            throw new InvalidOperationException($"The package identity property '{name}' has a circular reference.");
        }

        string[] definitions = [.. GetProperties(name).Select(static definition => definition.Element.Value)];
        if (definitions.Length == 0)
        {
            string? inherited = Environment.GetEnvironmentVariable(name);
            definitions = inherited is not null ? [inherited]
                : throw new InvalidOperationException($"The package identity property '{name}' has no source declaration or environment value.");
        }

        string[] expanded;
        try
        {
            expanded = [.. definitions.SelectMany(value => ExpandIdentity(value, active)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        finally
        {
            active.Remove(name);
        }

        return expanded.SelectMany(value => ExpandIdentity(expression[..match.Index] + value + expression[(match.Index + match.Length)..], active));
    }
}
