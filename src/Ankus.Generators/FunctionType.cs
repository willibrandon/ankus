using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Describes the SQL type and native conversion contract for a generated method parameter or result.
/// </summary>
internal sealed class FunctionType
{
    private FunctionType(string managed, string sql, string reader, string writer, string field, bool nullable, bool reference)
    {
        Managed = managed;
        Sql = sql;
        Reader = reader;
        Writer = writer;
        Field = field;
        Nullable = nullable;
        Reference = reference;
    }

    /// <summary>
    /// Gets the non-nullable managed type spelling.
    /// </summary>
    internal string Managed { get; }

    /// <summary>
    /// Gets the SQL type spelling.
    /// </summary>
    internal string Sql { get; }

    /// <summary>
    /// Gets the PostgreSQL argument access macro, or the varlena conversion category.
    /// </summary>
    internal string Reader { get; }

    /// <summary>
    /// Gets the PostgreSQL datum constructor, or the varlena conversion category.
    /// </summary>
    internal string Writer { get; }

    /// <summary>
    /// Gets the managed transport property used for scalar conversion.
    /// </summary>
    internal string Field { get; }

    /// <summary>
    /// Gets whether SQL NULL is accepted as a managed argument.
    /// </summary>
    internal bool Nullable { get; }

    /// <summary>
    /// Gets whether the managed representation is a reference type.
    /// </summary>
    internal bool Reference { get; }

    /// <summary>
    /// Gets the scalar element contract for a vector or shape-preserving array.
    /// </summary>
    internal FunctionType? Element { get; private set; }

    /// <summary>
    /// Gets the nullable-aware element spelling used by generated generic adapters.
    /// </summary>
    internal string ElementManaged => Element!.Managed + (Element.Nullable ? "?" : string.Empty);

    /// <summary>
    /// Gets whether this array is represented by an ordinary managed vector.
    /// </summary>
    internal bool IsVector { get; private set; }

    /// <summary>
    /// Gets whether this type uses a variable-length native buffer.
    /// </summary>
    internal bool IsBuffer => Reference || Reader is "uuid" or "json" or "jsonb" or "numeric" or "inet" or "cidr";

    /// <summary>
    /// Gets whether the type uses the field-wise temporal transport.
    /// </summary>
    internal bool IsTemporal => Reader is "date" or "time" or "timetz" or "timestamp" or "timestamptz" or "interval";

    /// <summary>
    /// Gets the temporal reader/writer suffix on NativeValue.
    /// </summary>
    internal string TemporalName => Reader switch
    {
        "date" => "Date",
        "time" => "Time",
        "timetz" => "TimeTz",
        "timestamp" => "Timestamp",
        "timestamptz" => "TimestampTz",
        "interval" => "Interval",
        _ => string.Empty,
    };

    /// <summary>
    /// Gets the optional .NET conversion suffix for a built-in temporal type.
    /// </summary>
    internal string ClrTemporalName => Managed.StartsWith("global::System.", System.StringComparison.Ordinal)
        ? Managed.Substring("global::System.".Length) : string.Empty;

    /// <summary>
    /// Gets the native built-in OID macro for typed datum conversion.
    /// </summary>
    internal string BufferOid => Reader.ToUpperInvariant() + "OID";

    /// <summary>
    /// Resolves a Roslyn type, including nullable value and reference annotations, to a SQL conversion contract.
    /// </summary>
    /// <param name="type">The managed type symbol.</param>
    /// <returns>The conversion contract, or null when the type needs an additional converter.</returns>
    internal static FunctionType? Create(ITypeSymbol type)
    {
        bool nullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional)
        {
            type = optional.TypeArguments[0];
            nullable = true;
        }

        if (type is IArrayTypeSymbol { Rank: 1, IsSZArray: true, ElementType.SpecialType: SpecialType.System_Byte })
        {
            return new("byte[]", "bytea", "bytea", "bytea", string.Empty, nullable, reference: true);
        }

        ITypeSymbol? elementType = type switch
        {
            IArrayTypeSymbol { Rank: 1, IsSZArray: true } array => array.ElementType,
            INamedTypeSymbol { Name: "PgArray", Arity: 1 } array when array.ContainingNamespace.ToDisplayString() == "Ankus"
                => array.TypeArguments[0],
            _ => null,
        };
        if (elementType is not null)
        {
            FunctionType? element = Create(elementType);
            if (element is null || element.Element is not null || element.Managed == "void")
            {
                return null;
            }

            bool vector = type is IArrayTypeSymbol;
            string elementName = element.Managed + (element.Nullable ? "?" : string.Empty);
            string managed = vector ? elementName + "[]" : "global::Ankus.PgArray<" + elementName + ">";
            return new(managed, element.Sql + "[]", "array", "array", string.Empty, nullable, reference: true)
            {
                Element = element,
                IsVector = vector,
            };
        }

        string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (name is "global::Ankus.PgInet" or "global::System.Net.IPAddress" or "global::Ankus.PgCidr" or "global::System.Net.IPNetwork")
        {
            string sql = name is "global::Ankus.PgInet" or "global::System.Net.IPAddress" ? "inet" : "cidr";
            return new(name, sql, sql, sql, string.Empty, nullable, reference: name == "global::System.Net.IPAddress");
        }

        if (name == "global::Ankus.PgNumeric" || type.SpecialType == SpecialType.System_Decimal)
        {
            return new(type.SpecialType == SpecialType.System_Decimal ? "decimal" : name,
                "numeric", "numeric", "numeric", string.Empty, nullable, reference: false);
        }

        string? temporal = name switch
        {
            "global::Ankus.PgDate" or "global::System.DateOnly" => "date",
            "global::Ankus.PgTime" or "global::System.TimeOnly" => "time",
            "global::Ankus.PgTimeTz" => "timetz",
            "global::Ankus.PgTimestamp" or "global::System.DateTime" => "timestamp",
            "global::Ankus.PgTimestampTz" or "global::System.DateTimeOffset" => "timestamptz",
            "global::Ankus.PgInterval" or "global::System.TimeSpan" => "interval",
            _ => null,
        };
        if (temporal is not null)
        {
            return new(name, temporal, temporal, temporal, string.Empty, nullable, reference: false);
        }

        if (name is "global::System.Guid" or "global::Ankus.PgJson" or "global::Ankus.PgJsonb")
        {
            string sql = name == "global::System.Guid" ? "uuid" : name == "global::Ankus.PgJson" ? "json" : "jsonb";
            return new(name, sql, sql, sql, string.Empty, nullable, reference: false);
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => new("bool", "boolean", "BOOL", "Bool", "Integral", nullable, false),
            SpecialType.System_SByte => new("sbyte", "\"char\"", "CHAR", "Char", "Integral", nullable, false),
            SpecialType.System_Int16 => new("short", "smallint", "INT16", "Int16", "Integral", nullable, false),
            SpecialType.System_Int32 => new("int", "integer", "INT32", "Int32", "Integral", nullable, false),
            SpecialType.System_Int64 => new("long", "bigint", "INT64", "Int64", "Integral", nullable, false),
            SpecialType.System_UInt32 => new("uint", "oid", "OID", "ObjectId", "Integral", nullable, false),
            SpecialType.System_Single => new("float", "real", "FLOAT4", "Float4", "Integral", nullable, false),
            SpecialType.System_Double => new("double", "double precision", "FLOAT8", "Float8", "Integral", nullable, false),
            SpecialType.System_String => new("string", "text", "text", "text", string.Empty, nullable, true),
            SpecialType.System_Void => new("void", "void", string.Empty, string.Empty, string.Empty, false, false),
            _ => null,
        };
    }

    /// <summary>
    /// Gets the built-in scalar OID macro, including the scalar types passed by value.
    /// </summary>
    internal string ScalarOid => Reader switch
    {
        "INT16" => "INT2OID",
        "INT32" => "INT4OID",
        "INT64" => "INT8OID",
        "OID" => "OIDOID",
        _ => BufferOid,
    };
}
