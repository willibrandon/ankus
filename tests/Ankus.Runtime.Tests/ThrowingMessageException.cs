namespace Ankus.Runtime.Tests;

/// <summary>
/// Exercises a secondary failure while the native boundary obtains a managed exception message.
/// </summary>
internal sealed class ThrowingMessageException : Exception
{
    /// <summary>
    /// Throws to simulate an exception subtype with a broken message accessor.
    /// </summary>
    public override string Message => throw new InvalidOperationException("Message accessor failed.");
}
