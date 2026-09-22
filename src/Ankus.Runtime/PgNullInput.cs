namespace Ankus;

/// <summary>
/// Controls whether PostgreSQL invokes a generated function when arguments are SQL NULL.
/// </summary>
public enum PgNullInput
{
    /// <summary>
    /// Declares STRICT when all parameters are required; mixed signatures still reject NULL required arguments before dispatch.
    /// </summary>
    Inferred,

    /// <summary>
    /// Returns SQL NULL without calling managed code whenever any argument is NULL.
    /// </summary>
    Strict,

    /// <summary>
    /// Invokes managed code with NULL arguments. Every parameter must have a nullable declaration.
    /// </summary>
    CalledOnNull,
}
