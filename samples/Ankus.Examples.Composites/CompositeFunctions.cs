using Ankus;

[assembly: PgSql("dog-type", "CREATE TYPE dog AS (name text, age integer);", Relocatable = true)]

namespace Ankus.Examples.Composites;

/// <summary>
/// Uses owned PostgreSQL tuples without depending on an installation schema or retaining catalog pointers.
/// </summary>
public static class CompositeFunctions
{
    /// <summary>
    /// Copies a dog and increments its age while preserving its PostgreSQL type identity.
    /// </summary>
    /// <param name="dog">The named composite, including a nullable age.</param>
    /// <returns>A new tuple with the same name and updated age.</returns>
    [PgFunction(Requires = ["dog-type"])]
    [return: PgCompositeType("dog")]
    public static PgHeapTuple Birthday([PgCompositeType("dog")] PgHeapTuple dog)
    {
        PgHeapTuple copy = dog.Clone();
        copy.Set("age", checked(dog.Get<int?>("age") + 1));
        return copy;
    }

    /// <summary>
    /// Exchanges a shaped array, preserving NULL elements and empty-array type identity.
    /// </summary>
    /// <param name="dogs">The array to return.</param>
    /// <returns>The same owned array.</returns>
    [PgFunction(Requires = ["dog-type"])]
    [return: PgCompositeType("dog")]
    public static PgArray<PgHeapTuple?>? EchoDogs([PgCompositeType("dog")] PgArray<PgHeapTuple?>? dogs) => dogs;

    /// <summary>
    /// Streams copies with successive ages through the composite SETOF protocol.
    /// </summary>
    /// <param name="dog">The initial tuple.</param>
    /// <param name="count">The number of birthdays.</param>
    /// <returns>Successively older copies.</returns>
    [PgFunction(Requires = ["dog-type"])]
    [return: PgCompositeType("dog")]
    public static IEnumerable<PgHeapTuple> Birthdays([PgCompositeType("dog")] PgHeapTuple dog, int count)
    {
        for (int index = 0; index < count; index++)
        {
            dog = Birthday(dog);
            yield return dog;
        }
    }

    /// <summary>
    /// Creates an anonymous record whose names and types come from explicit typed fields.
    /// </summary>
    /// <param name="name">The name field.</param>
    /// <param name="age">The nullable integer field.</param>
    /// <returns>A registered PostgreSQL record.</returns>
    [PgFunction]
    public static PgHeapTuple MakeRecord(string? name, int? age)
        => PgHeapTuple.Create(("name", SpiParameter.Create(name)), ("age", SpiParameter.Create(age)));
}
