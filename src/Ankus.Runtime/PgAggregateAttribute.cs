namespace Ankus;

/// <summary>
/// Declares a PostgreSQL aggregate from the typed static callback methods in a class or struct.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PgAggregateAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the SQL aggregate name, defaulting to the container name in snake_case.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a fixed existing SQL schema, overriding the enclosing PgSchema declaration.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the case-sensitive installation dependency identifier.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets declarations that must precede this aggregate in installation SQL.
    /// </summary>
    public string[] Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets whether installation SQL is emitted for the aggregate's CREATE AGGREGATE declaration.
    /// The default is true. False retains all support callbacks, their independent SQL policies and the dependency identifier.
    /// Cannot be false when Sql contains a replacement, including an empty string.
    /// </summary>
    public bool GenerateSql { get; set; } = true;

    /// <summary>
    /// Gets or sets literal installation SQL replacing this aggregate's CREATE AGGREGATE declaration.
    /// Null preserves generated SQL; empty text emits no statements for this declaration.
    /// </summary>
    /// <remarks>
    /// @MODULE_PATHNAME@ becomes MODULE_PATHNAME. Support functions retain their own PgFunction SQL controls.
    /// Replacement SQL must use helpers with compatible argument, state, result and NULL contracts.
    /// </remarks>
    public string? Sql { get; set; }

    /// <summary>
    /// Gets or sets whether the literal Sql replacement permits moving the extension to another schema.
    /// The default is false. This option applies only to non-null Sql; fixed schemas and other
    /// non-relocatable declarations can still prevent relocation.
    /// </summary>
    public bool SqlRelocatable { get; set; }

    /// <summary>
    /// Gets or sets declarations that must follow this aggregate in installation SQL.
    /// </summary>
    public string[] Before { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this is a normal, ordered-set, or hypothetical-set aggregate.
    /// </summary>
    public PgAggregateKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the aggregate's parallel execution contract, defaulting to Unsafe.
    /// </summary>
    public PgParallelSafety ParallelSafety { get; set; }

    /// <summary>
    /// Gets or sets the initial state as PostgreSQL input text. Null means SQL NULL.
    /// </summary>
    public string? InitialCondition { get; set; }

    /// <summary>
    /// Gets or sets the moving state initial value as PostgreSQL input text. Null means SQL NULL.
    /// </summary>
    public string? MovingInitialCondition { get; set; }

    /// <summary>
    /// Gets or sets the optionally schema-qualified sort operator used by PostgreSQL's aggregate optimization.
    /// </summary>
    public string? SortOperator { get; set; }

    /// <summary>
    /// Gets or sets the estimated transition state size in bytes, or zero to use PostgreSQL's default.
    /// </summary>
    public int StateSize { get; set; }

    /// <summary>
    /// Gets or sets the estimated moving transition state size in bytes, or zero for the default.
    /// </summary>
    public int MovingStateSize { get; set; }

    /// <summary>
    /// Gets or sets whether Final receives extra SQL NULL arguments describing the aggregate input types.
    /// </summary>
    public bool FinalExtra { get; set; }

    /// <summary>
    /// Gets or sets whether MovingFinal receives extra SQL NULL arguments describing the aggregate input types.
    /// </summary>
    public bool MovingFinalExtra { get; set; }

    /// <summary>
    /// Gets or sets the final function's state modification contract, or the PostgreSQL aggregate-kind default.
    /// </summary>
    public PgAggregateFinalModify FinalModify { get; set; }

    /// <summary>
    /// Gets or sets the moving final function's state modification contract.
    /// </summary>
    public PgAggregateFinalModify MovingFinalModify { get; set; }

    /// <summary>
    /// Gets or sets the required transition callback name.
    /// </summary>
    public string Transition { get; set; } = nameof(Transition);

    /// <summary>
    /// Gets or sets the optional final callback name.
    /// </summary>
    public string Final { get; set; } = nameof(Final);

    /// <summary>
    /// Gets or sets the optional combine callback name.
    /// </summary>
    public string Combine { get; set; } = nameof(Combine);

    /// <summary>
    /// Gets or sets the optional state serialization callback name.
    /// </summary>
    public string Serialize { get; set; } = nameof(Serialize);

    /// <summary>
    /// Gets or sets the optional state deserialization callback name.
    /// </summary>
    public string Deserialize { get; set; } = nameof(Deserialize);

    /// <summary>
    /// Gets or sets the optional moving transition callback name.
    /// </summary>
    public string MovingTransition { get; set; } = nameof(MovingTransition);

    /// <summary>
    /// Gets or sets the optional moving inverse callback name.
    /// </summary>
    public string MovingInverse { get; set; } = nameof(MovingInverse);

    /// <summary>
    /// Gets or sets the optional moving final callback name.
    /// </summary>
    public string MovingFinal { get; set; } = nameof(MovingFinal);
}
