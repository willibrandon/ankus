namespace Ankus.TestExtension;

/// <summary>
/// Executes native namespace and type lookup contracts through the published extension.
/// </summary>
[PgSchema("catalog_lookup")]
public static class CatalogLookupFunctions
{
    /// <summary>
    /// Resolves exact qualified operator components.
    /// </summary>
    [PgFunction]
    public static uint OperatorOid(string[] components, uint left, uint right)
        => Build(components).GetOperatorOid(left, right);

    /// <summary>
    /// Resolves the full native type-name grammar.
    /// </summary>
    [PgFunction]
    public static uint TypeOid(string name) => PgTypes.GetOid(name);

    /// <summary>
    /// Reuses one builder on both sides of a catalog or search-path change.
    /// </summary>
    [PgFunction]
    public static uint[] OperatorChanges(string[] components, uint left, uint right, string change)
    {
        PgQualifiedNameBuilder name = Build(components);
        uint before = name.GetOperatorOid(left, right);
        Spi.Execute(change);
        return [before, name.GetOperatorOid(left, right)];
    }

    /// <summary>
    /// Looks up one type name on both sides of a catalog change.
    /// </summary>
    [PgFunction]
    public static uint[] TypeChanges(string name, string change)
    {
        uint before = PgTypes.GetOid(name);
        Spi.Execute(change);
        return [before, PgTypes.GetOid(name)];
    }

    /// <summary>
    /// Proves native lookup errors unwind through managed catch and finally before same-session SQL resumes.
    /// </summary>
    [PgFunction]
    public static string CatchLookup(bool type, string[] names)
    {
        string state = "none";
        bool finalized;
        try
        {
            _ = type ? PgTypes.GetOid(names[0]) : Build(names).GetOperatorOid(23, 23);
        }
        catch (PgException exception)
        {
            state = exception.SqlState;
        }
        finally
        {
            finalized = true;
        }

        return $"{state}|{finalized}|{Spi.ExecuteScalar<int>("SELECT 42")}";
    }

    /// <summary>
    /// Executes finite CLR-name lookup paths under Native AOT without generated SQL type mappings.
    /// </summary>
    [PgFunction]
    public static uint ManagedNameOid(int mode) => mode switch
    {
        0 => PgTypes.GetOidByManagedName<LookupName>(),
        1 => PgTypes.GetOidByManagedName<LookupName[]>(),
        2 => PgTypes.GetOidByManagedName<int>(),
        _ => PgTypes.GetOidByManagedName<List<int>>(),
    };

    /// <summary>
    /// Supplies managed Unicode that a LATIN1 server must reject inside the native guard.
    /// </summary>
    [PgFunction]
    public static string UnrepresentableLookup(bool type)
        => CatchLookup(type, type ? ["\"😀\""] : ["😀", "+"]);

    /// <summary>
    /// Measures bounded native ownership after repeated success, missing and large native error paths.
    /// </summary>
    [PgFunction]
    public static string LookupStorage()
    {
        PgMemoryContext transaction = PgMemoryContext.Get(PgMemoryContextKind.TopTransaction)!;
        var known = new PgQualifiedNameBuilder { "pg_catalog", "+" };
        var missing = new PgQualifiedNameBuilder { "ankus_missing_namespace", "+" };
        var failure = new PgQualifiedNameBuilder { new string('x', 32768), "pg_catalog", "+" };
        uint expected = Spi.ExecuteScalar<uint>("SELECT 'pg_catalog.+(integer,integer)'::regoperator::oid");
        for (int index = 0; index < 16; index++)
        {
            Probe();
        }

        nuint baseline = transaction.GetAllocatedBytes();
        for (int index = 0; index < 128; index++)
        {
            Probe();
        }

        nuint after = transaction.GetAllocatedBytes();
        long retained = Spi.ExecuteScalar<long>("SELECT count(*) FROM pg_backend_memory_contexts WHERE name='Ankus SPI operation'");
        return $"128|{retained}|{(after <= baseline + 65536 ? "bounded" : "grew")}";

        void Probe()
        {
            if (known.GetOperatorOid(23, 23) != expected || missing.GetOperatorOid(23, 23) != 0 || PgTypes.GetOid("integer[]") != 1007)
            {
                throw new InvalidOperationException("Lookup changed identity during repeated access.");
            }

            try
            {
                _ = failure.GetOperatorOid(23, 23);
                throw new InvalidOperationException("A foreign database unexpectedly resolved.");
            }
            catch (PgException exception) when (exception.SqlState == "0A000")
            {
                if (!exception.Message.Contains(new string('x', 32768), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The owned native diagnostic was truncated.");
                }
            }
        }
    }

    private static PgQualifiedNameBuilder Build(string[] components)
    {
        var name = new PgQualifiedNameBuilder();
        foreach (string component in components)
        {
            name.Add(component);
        }

        return name;
    }

    /// <summary>
    /// Supplies a nested name whose namespace and declaring type must not reach the native parser.
    /// </summary>
    private sealed class LookupName;
}
