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
    /// Gets whether this type uses a variable-length native buffer.
    /// </summary>
    internal bool IsBuffer => Reference || Reader is "uuid" or "json" or "jsonb";

    /// <summary>
    /// Gets the native built-in OID macro for buffered datum conversion.
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

        string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
}
