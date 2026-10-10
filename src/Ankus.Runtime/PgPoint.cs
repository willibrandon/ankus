namespace Ankus;

/// <summary>
/// A PostgreSQL point with double-precision coordinates. Equality uses .NET value semantics, not geometric tolerance.
/// </summary>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public readonly partial record struct PgPoint(double X, double Y)
{
    /// <summary>
    /// Parses a point using PostgreSQL on the active backend.
    /// </summary>
    /// <param name="text">The point text.</param>
    /// <returns>The parsed point.</returns>
    public static PgPoint Parse(string text) => PgGeometry.Parse<PgPoint>(text);

    /// <summary>
    /// Tries PostgreSQL parsing without swallowing backend-access or operational errors.
    /// </summary>
    /// <param name="text">The point text.</param>
    /// <param name="value">The result, or default.</param>
    /// <returns>Whether parsing succeeded.</returns>
    /// <remarks>
    /// Invalid input returns false only after actual rollback. Transaction callbacks and parallel operations
    /// before PostgreSQL 17 forbid independent rollback; native errors there remain pending and throw PgException.
    /// </remarks>
    public static bool TryParse(string? text, out PgPoint value) => PgGeometry.TryParse(text, out value);

    /// <summary>
    /// Formats round-trippable coordinates without backend access.
    /// </summary>
    /// <returns>The point text.</returns>
    public override string ToString() => "(" + PgGeometry.Format(X) + "," + PgGeometry.Format(Y) + ")";
}
