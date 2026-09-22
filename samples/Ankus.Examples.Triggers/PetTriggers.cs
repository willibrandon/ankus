using Ankus;

[assembly: PgSql("pet-table", "CREATE TABLE pets (name text NOT NULL, visits integer NOT NULL DEFAULT 0);", Relocatable = true)]
[assembly: PgSql("pet-trigger", """
    CREATE TRIGGER normalize_pet BEFORE INSERT OR UPDATE ON pets
    FOR EACH ROW EXECUTE FUNCTION normalize_pet_name('name');
    """, Requires = ["pet-table", "normalize-pet-name"], Relocatable = true)]

namespace Ankus.Examples.Triggers;

/// <summary>
/// Demonstrates trigger arguments, owned NEW row edits, and skipping a row with a null return.
/// </summary>
public static class PetTriggers
{
    /// <summary>
    /// Trims the named text column and skips rows whose names are null, empty, or whitespace.
    /// </summary>
    /// <param name="context">The current INSERT or UPDATE trigger context.</param>
    /// <returns>The normalized NEW row, or null to skip the operation for that row.</returns>
    [PgTrigger]
    [PgFunction(Id = "normalize-pet-name")]
    public static PgHeapTuple? NormalizePetName(PgTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Timing != PgTriggerTiming.Before || context.Level != PgTriggerLevel.Row || context.New is null)
        {
            throw new InvalidOperationException("Name normalization requires a BEFORE INSERT or UPDATE row trigger.");
        }

        string column = context.Arguments[0];
        string? name = context.New.Get<string?>(column)?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        context.New.Set(column, name);
        return context.New;
    }
}
