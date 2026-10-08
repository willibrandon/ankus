namespace Ankus;

/// <summary>
/// Describes one label of an enumerated configuration parameter defined at run time.
/// </summary>
/// <param name="Name">The label PostgreSQL accepts, compared with its case-insensitive ASCII rules.</param>
/// <param name="Value">The ordinal stored for the label; aliases share an ordinal, and the first label is displayed.</param>
/// <param name="Hidden">Whether the label is accepted but omitted from lists of available values.</param>
public readonly record struct PgGucEnumOption(string Name, int Value, bool Hidden = false);
