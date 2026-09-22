using System.Net;

namespace Ankus.TestExtension;

/// <summary>
/// Probes network conversion, native input errors, exact .NET mapping and detached network operations.
/// </summary>
public static class NetworkFunctions
{
    /// <summary>
    /// Constructs inet from independent address bytes, rather than echoing native transport input.
    /// </summary>
    [PgFunction]
    public static PgInet NetworkConstruct(byte[] bytes, int prefix) => new(new IPAddress(bytes), prefix);

    /// <summary>
    /// Confirms generated SQL defaults match detached C# defaults for each network representation.
    /// </summary>
    [PgFunction]
    public static bool NetworkDefaults(PgInet address = default, PgCidr network = default, IPNetwork clr = default)
        => address == default && network == default && clr == default;

    /// <summary>
    /// Exercises exact IPAddress narrowing of a detached SPI result after native result release.
    /// </summary>
    [PgFunction]
    public static IPAddress NetworkSpiHost(string text) => Spi.ExecuteScalar<IPAddress>("SELECT $1::inet", SpiParameter.Create(text));

    /// <summary>
    /// Exchanges nullable inet through all scalar ownership paths.
    /// </summary>
    [PgFunction]
    public static PgInet? NetworkInet(PgInet? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable cidr through all scalar ownership paths.
    /// </summary>
    [PgFunction]
    public static PgCidr? NetworkCidr(PgCidr? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges full-width host addresses without losing a subnet prefix.
    /// </summary>
    [PgFunction]
    public static IPAddress? NetworkAddress(IPAddress? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges .NET networks with exact prefix conversion.
    /// </summary>
    [PgFunction]
    public static IPNetwork? NetworkClr(IPNetwork? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped inet arrays, retaining lower bounds and NULL elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgInet?>? NetworkInets(PgArray<PgInet?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped cidr arrays, retaining lower bounds and NULL elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgCidr?>? NetworkCidrs(PgArray<PgCidr?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exercises host-only inet array narrowing under Native AOT.
    /// </summary>
    [PgFunction]
    public static IPAddress?[]? NetworkAddresses(IPAddress?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exercises .NET network arrays under Native AOT.
    /// </summary>
    [PgFunction]
    public static IPNetwork?[]? NetworkClrs(IPNetwork?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Parses inet text through the guarded PostgreSQL input function.
    /// </summary>
    [PgFunction]
    public static PgInet NetworkParseInet(string text) => PgInet.Parse(text);

    /// <summary>
    /// Parses cidr text through the guarded PostgreSQL input function.
    /// </summary>
    [PgFunction]
    public static PgCidr NetworkParseCidr(string text) => PgCidr.Parse(text);

    /// <summary>
    /// Exercises subnet derivation without a backend operation.
    /// </summary>
    [PgFunction]
    public static PgInet NetworkOperation(PgInet value, string operation, int prefix) => operation switch
    {
        "network" => value.Network.Inet,
        "broadcast" => value.Broadcast,
        "netmask" => value.Netmask,
        "hostmask" => value.Hostmask,
        "prefix" => value.WithPrefixLength(prefix),
        "cidr-prefix" => value.Network.WithPrefixLength(prefix).Inet,
        _ => throw new ArgumentException("Unknown network operation.", nameof(operation)),
    };

    /// <summary>
    /// Returns comparison, containment and equality observations for independent SQL verification.
    /// </summary>
    [PgFunction]
    public static int[] NetworkCompare(PgInet left, PgInet right) =>
        [Math.Sign(left.CompareTo(right)), left.Contains(right) ? 1 : 0, left.Contains(right, false) ? 1 : 0,
         left == right ? 1 : 0, left < right ? 1 : 0, left <= right ? 1 : 0, left > right ? 1 : 0, left >= right ? 1 : 0];

    /// <summary>
    /// Recovers from repeated input failures while retaining successful writes, a prepared plan and memory balance.
    /// </summary>
    [PgFunction]
    public static string NetworkRecovery() => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE network_writes(value int); INSERT INTO network_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM network_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int invalid = 0;
        int finalized = 0;
        for (int i = 0; i < 50; i++)
        {
            try
            {
                if (!PgInet.TryParse("256.0.0.1", out _) && !PgCidr.TryParse("192.0.2.1/24", out _))
                {
                    invalid++;
                }

                _ = PgInet.Parse("::1/129");
            }
            catch (PgException error) when (error.SqlState == "22P02")
            {
                invalid++;
            }
            finally
            {
                finalized++;
            }
        }

        session.Execute("INSERT INTO network_writes VALUES (2)");
        return $"{invalid}:{finalized}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });
}
