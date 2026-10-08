namespace Ankus.Examples.Composites;

/// <summary>
/// Creates, reads and changes named composites without depending on the installation schema.
/// </summary>
public static class CompositeFunctions
{
    /// <summary>
    /// Creates a dog, like <c>RETURN ROW(name, scritches)::Dog</c>.
    /// </summary>
    /// <param name="name">The dog's name.</param>
    /// <param name="scritches">The dog's scritch count.</param>
    /// <param name="call">The call, whose result type identifies dog in the extension's current schema.</param>
    /// <returns>A new dog.</returns>
    [PgFunction]
    [return: PgCompositeType(CompositeTypes.Dog)]
    public static PgHeapTuple CreateDog(string name, int scritches, PgFunctionContext call)
    {
        PgHeapTuple dog = PgTupleDescriptor.Load(call.ResultTypeOid).CreateTuple();
        dog.Set("name", name);
        dog.Set("scritches", scritches);
        return dog;
    }

    /// <summary>
    /// Reads a dog's scritch count and writes the same count back, exactly as pgrx's example does.
    /// </summary>
    /// <param name="dog">The dog, including a nullable count.</param>
    /// <returns>A copy with the same name and count. Use the <c>+</c> operator to add scritches.</returns>
    [PgFunction]
    [return: PgCompositeType(CompositeTypes.Dog)]
    public static PgHeapTuple ScritchDog([PgCompositeType(CompositeTypes.Dog)] PgHeapTuple dog)
    {
        PgHeapTuple scritched = dog.Clone();
        scritched.Set("scritches", dog.Get<int?>("scritches"));
        return scritched;
    }

    /// <summary>
    /// Makes a cat and a dog friends, like <c>RETURN ROW(cat, dog)::CatAndDogFriendship</c>.
    /// </summary>
    /// <param name="dog">The dog.</param>
    /// <param name="cat">The cat.</param>
    /// <param name="call">The call, whose result type identifies the friendship type in the current schema.</param>
    /// <returns>A friendship containing both nested composites.</returns>
    [PgFunction(Requires = ["create_cat_and_dog_friendship"])]
    [return: PgCompositeType(CompositeTypes.CatAndDogFriendship)]
    public static PgHeapTuple MakeFriendship([PgCompositeType(CompositeTypes.Dog)] PgHeapTuple dog,
        [PgCompositeType(CompositeTypes.Cat)] PgHeapTuple cat, PgFunctionContext call)
    {
        PgHeapTuple friendship = PgTupleDescriptor.Load(call.ResultTypeOid).CreateTuple();
        friendship.Set("dog", dog);
        friendship.Set("cat", cat);
        return friendship;
    }

    /// <summary>
    /// Adds scritches to a dog, treating a NULL count as zero, like <c>RETURN ROW(dog.name, dog.scritches + number)::Dog</c>.
    /// </summary>
    /// <param name="left">The dog.</param>
    /// <param name="right">The scritches to add.</param>
    /// <returns>A copy with the checked sum.</returns>
    [PgOperator("+")]
    [return: PgCompositeType(CompositeTypes.Dog)]
    public static PgHeapTuple AddScritchesToDog([PgCompositeType(CompositeTypes.Dog)] PgHeapTuple left, int right)
    {
        PgHeapTuple dog = left.Clone();
        dog.Set("scritches", checked(left.Get<int?>("scritches").GetValueOrDefault() + right));
        return dog;
    }

    /// <summary>
    /// Streams copies with one more scritch each through the composite SETOF protocol.
    /// </summary>
    /// <param name="dog">The initial dog.</param>
    /// <param name="count">The number of scritches.</param>
    /// <returns>Successively scritched copies.</returns>
    [PgFunction]
    [return: PgCompositeType(CompositeTypes.Dog)]
    public static IEnumerable<PgHeapTuple> ScritchRepeatedly([PgCompositeType(CompositeTypes.Dog)] PgHeapTuple dog, int count)
    {
        for (int index = 0; index < count; index++)
        {
            dog = AddScritchesToDog(dog, 1);
            yield return dog;
        }
    }

    /// <summary>
    /// Exchanges a shaped array, preserving NULL elements and empty-array type identity.
    /// </summary>
    /// <param name="dogs">The array to return.</param>
    /// <returns>The same owned array.</returns>
    [PgFunction]
    [return: PgCompositeType(CompositeTypes.Dog)]
    public static PgArray<PgHeapTuple?>? EchoDogs([PgCompositeType(CompositeTypes.Dog)] PgArray<PgHeapTuple?>? dogs) => dogs;

    /// <summary>
    /// Creates an anonymous record whose names and types come from explicit typed fields.
    /// </summary>
    /// <param name="name">The name field.</param>
    /// <param name="scritches">The nullable integer field.</param>
    /// <returns>A registered PostgreSQL record.</returns>
    [PgFunction]
    public static PgHeapTuple MakeRecord(string? name, int? scritches)
        => PgHeapTuple.Create(("name", SpiParameter.Create(name)), ("scritches", SpiParameter.Create(scritches)));
}
