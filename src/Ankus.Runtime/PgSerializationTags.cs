using System.Formats.Cbor;

namespace Ankus;

/// <summary>
/// Names the registered CBOR semantic tags that generated storage uses for framework values.
/// </summary>
internal static class PgSerializationTags
{
    /// <summary>
    /// Gets the IANA tag for a binary UUID in network byte order.
    /// </summary>
    internal static CborTag Uuid => (CborTag)37;

    /// <summary>
    /// Gets the RFC 8943 tag for an RFC 3339 full-date string.
    /// </summary>
    internal static CborTag FullDate => (CborTag)1004;
}
