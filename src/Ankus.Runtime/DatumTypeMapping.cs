namespace Ankus;

/// <summary>
/// Retains a closed scalar mapping's metadata without resolving catalog identities or constructing user code.
/// </summary>
internal abstract class DatumTypeMapping(string name, string? schema, PgTypeOrigin origin, Type converterType,
    bool canRead, bool canWrite)
{
    /// <summary>
    /// Checks whether another generated registration describes this exact conversion contract.
    /// </summary>
    internal bool Matches(string candidateName, string? candidateSchema, PgTypeOrigin candidateOrigin,
        Type candidateConverterType, bool candidateCanRead, bool candidateCanWrite)
        => string.Equals(name, candidateName, StringComparison.Ordinal) &&
            string.Equals(schema, candidateSchema, StringComparison.Ordinal) && origin == candidateOrigin &&
            converterType == candidateConverterType && canRead == candidateCanRead && canWrite == candidateCanWrite;

    /// <summary>
    /// Resolves the concrete type in the current backend on every operation.
    /// </summary>
    internal uint GetOid() => NativeBackend.ResolveDatumType(name, schema);

    /// <summary>
    /// Rejects reading before invoking a factory or accessing native storage.
    /// </summary>
    internal void RequireRead()
    {
        if (!canRead)
        {
            throw new NotSupportedException("The mapped PostgreSQL type has no datum reader.");
        }
    }

    /// <summary>
    /// Rejects writing before invoking a factory or allocating native storage.
    /// </summary>
    internal void RequireWrite()
    {
        if (!canWrite)
        {
            throw new NotSupportedException("The mapped PostgreSQL type has no datum writer.");
        }
    }

    /// <summary>
    /// Validates a datum before bypassing the converter for SQL NULL or reading a present value.
    /// </summary>
    internal object? Read(PgDatum value)
    {
        RequireRead();
        value.Lifetime.Validate();
        uint oid = GetOid();
        ValidateIdentity(value, oid);
        return value.IsNull ? null : ReadPresent(value);
    }

    /// <summary>
    /// Writes the declared managed contract while rejecting stale captured parameter identities.
    /// </summary>
    internal NativeValue Write(object? value, uint capturedOid = 0)
    {
        RequireWrite();
        uint oid = GetOid();
        if (capturedOid != 0 && capturedOid != oid)
        {
            throw new InvalidOperationException("The mapped PostgreSQL parameter type has changed since the parameter was created.");
        }

        if (value is null)
        {
            return new NativeValue { IsNull = 1 };
        }

        PgDatum datum = WritePresent(value, oid, PgMemoryContext.Current);
        datum.Lifetime.Validate();
        ValidateIdentity(datum, oid);
        return NativeValue.FromPolymorphic(datum);
    }

    /// <summary>
    /// Converts a present input to an independent managed value.
    /// </summary>
    protected abstract object ReadPresent(PgDatum value);

    /// <summary>
    /// Converts a present managed value to a non-null native handle.
    /// </summary>
    protected abstract PgDatum WritePresent(object value, uint oid, PgMemoryContext destination);

    /// <summary>
    /// Checks nominal SQL identity independently of storage representation and NULL.
    /// </summary>
    private static void ValidateIdentity(PgDatum value, uint expected)
    {
        if (value.TypeOid != expected)
        {
            throw new InvalidCastException($"PostgreSQL datum type OID {value.TypeOid} does not match mapped type OID {expected}.");
        }
    }
}
