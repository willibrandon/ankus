namespace Ankus.TestExtension;

/// <summary>
/// Selects an OID reader for native large-object creation without requiring a writer.
/// </summary>
/// <param name="Value">The exact returned object identity.</param>
[PgDatumType("oid", typeof(NativeOidConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct NativeOid(uint Value);

/// <summary>
/// Observes completed native writes before deliberately failing managed conversion.
/// </summary>
public sealed class NativeOidConverter : IPgDatumReader<NativeOid>
{
    /// <summary>
    /// Gets or sets the reader failure selected by this backend's fixture.
    /// </summary>
    public static int Mode { get; set; }

    /// <summary>
    /// Gets the number of lazy constructions.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Gets the number of present reads.
    /// </summary>
    public static int Reads { get; private set; }

    /// <summary>
    /// Gets a checked input retained only to prove result-owner cleanup.
    /// </summary>
    public static PgDatum? Captured { get; private set; }

    /// <summary>
    /// Records construction independently of native invocation.
    /// </summary>
    public NativeOidConverter() => Constructions++;

    /// <inheritdoc />
    public NativeOid Read(PgDatum value)
    {
        Reads++;
        Captured = value;
        uint oid = value.Read<uint>();
        return Mode switch
        {
            1 => throw new PgException("P8531", "native result reader failed", detail: "completed object creation", hint: "read another object"),
            2 => throw new FormatException("ordinary native result reader failed"),
            _ => new NativeOid(oid),
        };
    }
}

/// <summary>
/// Selects a failing lazy factory independently of the successful OID reader.
/// </summary>
/// <param name="Value">The unreachable object identity.</param>
[PgDatumType("oid", typeof(NativeOidFactoryConverter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct NativeOidFactory(uint Value);

/// <summary>
/// Proves factory failures are cached without preventing subsequent native calls.
/// </summary>
public sealed class NativeOidFactoryConverter : IPgDatumReader<NativeOidFactory>
{
    /// <summary>
    /// Gets the number of failed constructions.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Fails after a native result has already been captured.
    /// </summary>
    public NativeOidFactoryConverter()
    {
        Constructions++;
        throw new PgException("P8532", "native result factory failed", detail: "completed object creation", hint: "use another reader");
    }

    /// <inheritdoc />
    public NativeOidFactory Read(PgDatum value) => throw new InvalidOperationException("The failed factory cannot supply a reader.");
}

/// <summary>
/// Selects a correctly represented OID whose missing read capability must prevent native writes.
/// </summary>
/// <param name="Value">The OID supplied to the writer.</param>
[PgDatumType("oid", typeof(NativeOidWriter), Schema = "pg_catalog", Origin = PgTypeOrigin.External)]
public readonly record struct NativeWriteOid(uint Value);

/// <summary>
/// Counts constructors to prove a denied result never instantiates a writer.
/// </summary>
public sealed class NativeOidWriter : IPgDatumWriter<NativeWriteOid>
{
    /// <summary>
    /// Gets the construction count.
    /// </summary>
    public static int Constructions { get; private set; }

    /// <summary>
    /// Records an unwanted eager construction if preflight fails.
    /// </summary>
    public NativeOidWriter() => Constructions++;

    /// <inheritdoc />
    public PgDatum Write(NativeWriteOid value, uint typeOid, PgMemoryContext destination)
        => PgDatum.DangerousCreate(value.Value, typeOid, destination);
}

/// <summary>
/// Resolves a dedicated external domain afresh for native scalar and array result contracts.
/// </summary>
/// <param name="Value">The copied integer.</param>
[PgDatumType("value", typeof(NativeLiveConverter), Schema = "mapped_native_live", Origin = PgTypeOrigin.External)]
public readonly record struct NativeLive(int Value);

/// <summary>
/// Records current identities without caching them in the converter.
/// </summary>
public sealed class NativeLiveConverter : IPgDatumReader<NativeLive>
{
    /// <summary>
    /// Gets the last present input's nominal identity.
    /// </summary>
    public static uint LastOid { get; private set; }

    /// <inheritdoc />
    public NativeLive Read(PgDatum value)
    {
        LastOid = value.TypeOid;
        return new NativeLive(value.Read<int>());
    }
}
