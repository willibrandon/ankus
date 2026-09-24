using System.ComponentModel;
using System.Collections.Concurrent;

namespace Ankus;

/// <summary>
/// Receives closed, generated custom-type conversions without runtime reflection or code generation.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PgTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, CustomTypeMapping> s_scalars = new();
    private static readonly ConcurrentDictionary<Type, CustomTypeMapping> s_arrays = new();

    /// <summary>
    /// Registers a value type and its nullable and array representations.
    /// </summary>
    /// <typeparam name="T">The generated managed value type.</typeparam>
    /// <param name="name">The SQL type name.</param>
    /// <param name="schema">The fixed schema, or null for the installation schema.</param>
    /// <param name="createCodec">The generated factory, first invoked inside the managed error boundary.</param>
    public static void RegisterValue<T>(string name, string? schema, Func<PgTypeCodec<T>> createCodec) where T : struct
        => Register<T, T?>(name, schema, createCodec);

    /// <summary>
    /// Registers a reference type and its array representations.
    /// </summary>
    /// <typeparam name="T">The generated managed reference type.</typeparam>
    /// <param name="name">The SQL type name.</param>
    /// <param name="schema">The fixed schema, or null for the installation schema.</param>
    /// <param name="createCodec">The generated factory, first invoked inside the managed error boundary.</param>
    public static void RegisterReference<T>(string name, string? schema, Func<PgTypeCodec<T>> createCodec) where T : class
        => Register<T, T?>(name, schema, createCodec);

    /// <summary>
    /// Converts a text input to the stored representation through the generated codec.
    /// </summary>
    /// <typeparam name="T">The generated managed type.</typeparam>
    /// <param name="input">The UTF-8 text transport.</param>
    /// <returns>The owned storage transport.</returns>
    public static NativeValue Input<T>(NativeValue input)
    {
        CustomTypeMapping mapping = Require(typeof(T));
        return mapping.Write(mapping.Parse(input.ReadString()));
    }

    /// <summary>
    /// Formats a stored value through the generated codec.
    /// </summary>
    /// <typeparam name="T">The generated managed type.</typeparam>
    /// <param name="input">The borrowed storage transport.</param>
    /// <returns>The owned UTF-8 text transport.</returns>
    public static NativeValue Output<T>(NativeValue input)
    {
        CustomTypeMapping mapping = Require(typeof(T));
        return NativeValue.FromString(mapping.Format(mapping.Read(input)));
    }

    /// <summary>
    /// Validates and writes a complete binary protocol payload through the generated codec.
    /// </summary>
    /// <typeparam name="T">The generated managed type.</typeparam>
    /// <param name="input">The borrowed storage or binary input transport.</param>
    /// <returns>The owned binary payload.</returns>
    public static NativeValue Binary<T>(NativeValue input)
    {
        CustomTypeMapping mapping = Require(typeof(T));
        return mapping.Write(mapping.Read(input));
    }

    /// <summary>
    /// Registers the complete set of statically instantiated conversions.
    /// </summary>
    private static void Register<T, TOptional>(string name, string? schema, Func<PgTypeCodec<T>> createCodec)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(createCodec);
        if (schema is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(schema);
        }

        var mapping = new CustomTypeMapping<T, TOptional>(name, schema, createCodec);
        if (!s_scalars.TryAdd(typeof(T), mapping))
        {
            throw new InvalidOperationException($"Type '{typeof(T)}' already has a generated PostgreSQL mapping.");
        }

        s_scalars[typeof(TOptional)] = mapping;
        s_arrays[typeof(T[])] = mapping;
        s_arrays[typeof(TOptional[])] = mapping;
        s_arrays[typeof(PgArray<T>)] = mapping;
        s_arrays[typeof(PgArray<TOptional>)] = mapping;
    }

    /// <summary>
    /// Gets a generated scalar mapping, including its nullable form.
    /// </summary>
    internal static CustomTypeMapping? Find(Type type) => s_scalars.GetValueOrDefault(type);

    /// <summary>
    /// Gets a generated vector or shaped-array mapping.
    /// </summary>
    internal static CustomTypeMapping? FindArray(Type type) => s_arrays.GetValueOrDefault(type);

    /// <summary>
    /// Requires an already generated mapping without invoking a backend operation.
    /// </summary>
    internal static CustomTypeMapping Require(Type type) => Find(type) ??
        throw new NotSupportedException($"Type '{type}' has no generated PgType mapping.");

    /// <summary>
    /// Matches a current backend OID without caching identities across DDL or backend calls.
    /// </summary>
    internal static CustomTypeMapping FindOid(uint oid)
    {
        foreach (CustomTypeMapping mapping in s_scalars.Values.Distinct())
        {
            if (mapping.GetOid(missingOk: true) == oid)
            {
                return mapping;
            }
        }

        throw new NotSupportedException($"PostgreSQL type OID {oid} has no generated PgType mapping.");
    }
}
