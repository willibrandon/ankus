namespace Ankus.Generators;

/// <summary>
/// Retains derived catalog declarations independently of conversion, interface calls and authored graph policy.
/// </summary>
/// <param name="Name">The unquoted scalar catalog name.</param>
/// <param name="Schema">The explicit catalog schema or extension-default selection.</param>
/// <param name="Symbol">The assembly-specific native helper identity.</param>
/// <param name="Type">The scalar SQL spelling and current-provider qualification.</param>
/// <param name="Equality">Whether equality operators and helpers are declared.</param>
/// <param name="Ordering">Whether ordering operators and the btree class are declared.</param>
/// <param name="Hashing">Whether hashing support and its class are declared.</param>
internal sealed record DerivedSqlModel(string Name, string? Schema, string Symbol, SqlTypeTemplate Type,
    bool Equality, bool Ordering, bool Hashing)
{
    /// <summary>
    /// Projects only values used in PostgreSQL DDL, excluding managed implementation and SQL replacement options.
    /// </summary>
    /// <param name="model">The complete detached derived semantics.</param>
    /// <returns>The catalog contract, or null when its scalar identity is unavailable.</returns>
    internal static DerivedSqlModel? Create(DerivedOperatorModel model)
    {
        FunctionType? value = model.Value;
        string? name = value?.CustomType?.Name ?? value?.Enumeration?.Name ?? value?.DatumType?.Name;
        if (name is null || value is null)
        {
            return null;
        }

        return new(name, value.CustomType?.Schema ?? value.Enumeration?.Schema ?? value.DatumType?.Schema,
            model.Symbol, SqlTypeTemplate.Create(value), model.Equality is not null, model.Ordering is not null, model.Hashing is not null);
    }
}
