namespace Ankus;

/// <summary>
/// Selects the guarded native datum operation for a checked borrowed buffer.
/// </summary>
internal enum NativeBufferKind
{
    /// <summary>
    /// Borrows packed or detoasted bytea bytes.
    /// </summary>
    Bytea = 9,

    /// <summary>
    /// Borrows or transcodes PostgreSQL text into strict UTF-8.
    /// </summary>
    Text = 10,

    /// <summary>
    /// Borrows a C string without decoding or transcoding its bytes.
    /// </summary>
    CString = 13,
}
