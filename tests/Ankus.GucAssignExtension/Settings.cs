namespace Ankus.GucAssignExtension;

/// <summary>
/// Exercises an assign-only library without managed initialization or SQL exports.
/// </summary>
public static partial class Settings
{
    private static int s_previous = 7;

    /// <summary>
    /// Gets a setting with an assignment callback and no other hooks.
    /// </summary>
    [PgGucInt("ankus_guc_assign.count", 7, "Assignment example", Assign = nameof(Assign))]
    public static partial int Count { get; }

    /// <summary>
    /// Checks the native old value before recording the newly accepted value.
    /// </summary>
    /// <param name="accepted">The accepted integer.</param>
    /// <param name="extra">The optional copied hook data.</param>
    internal static void Assign(int accepted, PgGucExtra? extra)
    {
        if (Count != s_previous)
        {
            throw new InvalidOperationException("Assign-only native old value was incorrect.");
        }

        if (accepted == 666)
        {
            throw new InvalidOperationException("Assign-only callback entered.");
        }

        s_previous = accepted;
    }
}
