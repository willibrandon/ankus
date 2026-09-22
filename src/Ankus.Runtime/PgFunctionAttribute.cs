namespace Ankus;

/// <summary>
/// Exposes a static .NET method as a PostgreSQL function through generated native entry points and SQL.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the SQL function name. The default is the method name converted to snake_case.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a unique, case-sensitive identifier used by installation SQL dependencies.
    /// It does not change the function's SQL name or signature.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets identifiers of SQL blocks or generated declarations that must precede this function.
    /// </summary>
    public string[] Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets an existing SQL schema, overriding the nearest PgSchema declaration.
    /// Declare PgSchema separately when the extension should create the schema.
    /// A fixed schema makes the extension non-relocatable.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the planner's volatility contract. The default is Volatile.
    /// </summary>
    public PgVolatility Volatility { get; set; }

    /// <summary>
    /// Gets or sets whether PostgreSQL may execute this function in parallel queries. The default is Unsafe.
    /// </summary>
    public PgParallelSafety ParallelSafety { get; set; }

    /// <summary>
    /// Gets or sets SQL NULL dispatch behavior. The default infers strictness from parameter nullability.
    /// </summary>
    public PgNullInput NullInput { get; set; }

    /// <summary>
    /// Gets or sets whether the function executes with its owner's privileges instead of the caller's.
    /// </summary>
    public bool SecurityDefiner { get; set; }

    /// <summary>
    /// Gets or sets the claim that the function reveals no argument information except through its result.
    /// PostgreSQL requires superuser privileges to install a leakproof function.
    /// </summary>
    public bool Leakproof { get; set; }

    /// <summary>
    /// Gets or sets whether installation uses CREATE OR REPLACE FUNCTION.
    /// </summary>
    public bool CreateOrReplace { get; set; }

    /// <summary>
    /// Gets or sets the positive finite planner cost in cpu_operator_cost units. The C-language default is one.
    /// </summary>
    public double Cost { get; set; } = 1;

    /// <summary>
    /// Gets or sets the positive finite estimated row count for a set-returning function. The default is 1000.
    /// This option is only valid for IEnumerable returns.
    /// </summary>
    public double Rows { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how a set-returning function produces rows. Auto prefers one row per call.
    /// This option is only valid for IEnumerable returns.
    /// </summary>
    public PgSetMode SetMode { get; set; }

    /// <summary>
    /// Gets or sets an ordered schema search path scoped to this function. Null preserves the caller's search path.
    /// Each entry is a schema identifier, including the special $user and pg_temp entries.
    /// </summary>
    public string[]? SearchPath { get; set; }

    /// <summary>
    /// Gets or sets the name of an existing planner support function, optionally qualified with one schema.
    /// PostgreSQL resolves and validates its internal-to-internal signature during installation.
    /// </summary>
    public string? SupportFunction { get; set; }
}
