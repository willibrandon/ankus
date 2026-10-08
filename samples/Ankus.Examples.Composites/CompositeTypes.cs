using Ankus;

// Composite types must exist before functions use them. Bootstrap SQL runs before every generated declaration.
[assembly: PgSql("create_composites", """
    CREATE TYPE Dog AS (
        name TEXT,
        scritches INT
    );

    CREATE TYPE Cat AS (
        name TEXT,
        boops INT
    );
    """, Order = PgSqlOrder.Bootstrap, Relocatable = true)]

// An ordinary block runs where its dependents require it. make_friendship requires this block by name.
[assembly: PgSql("create_cat_and_dog_friendship", """
    CREATE TYPE CatAndDogFriendship AS (
        cat Cat,
        dog Dog
    );
    """, Relocatable = true)]

namespace Ankus.Examples.Composites;

/// <summary>
/// Names the composite types created by the extension's custom SQL.
/// </summary>
/// <remarks>
/// PostgreSQL folds the unquoted names in <c>CREATE TYPE</c> to lowercase. Binding attributes use exact catalog names.
/// </remarks>
public static class CompositeTypes
{
    /// <summary>
    /// The <c>dog (name text, scritches integer)</c> composite.
    /// </summary>
    public const string Dog = "dog";

    /// <summary>
    /// The <c>cat (name text, boops integer)</c> composite.
    /// </summary>
    public const string Cat = "cat";

    /// <summary>
    /// The <c>catanddogfriendship (cat cat, dog dog)</c> composite.
    /// </summary>
    public const string CatAndDogFriendship = "catanddogfriendship";
}
