using Ankus.TestExtension;

[assembly: PgSql("typed-input", """
    CREATE TABLE ankus_typed.input(value integer);
    INSERT INTO ankus_typed.input VALUES (42);
    """)]
[assembly: PgRequires(typeof(TypedDependencyFunctions), DeclarationId = "typed-input")]
[assembly: PgBefore(typeof(TypedDependencyFunctions), nameof(TypedDependencyFunctions.ReadValue), DeclarationId = "typed-input")]
[assembly: PgSql("typed-captured", "CREATE TABLE ankus_typed.captured AS SELECT ankus_typed.read_value() AS value;")]
[assembly: PgRequires(typeof(TypedDependencyFunctions), nameof(TypedDependencyFunctions.ReadValue), DeclarationId = "typed-captured")]

namespace Ankus.TestExtension;

/// <summary>
/// Reads installation data whose prerequisites are expressed exclusively through managed SQL references.
/// </summary>
[PgSchema("ankus_typed")]
public static class TypedDependencyFunctions
{
    /// <summary>
    /// Reads data during extension installation after the typed Before edge supplied its table.
    /// </summary>
    /// <returns>The value from the custom SQL input table.</returns>
    [PgFunction]
    public static int ReadValue() => Spi.ExecuteScalar<int>("SELECT value FROM ankus_typed.input");
}
