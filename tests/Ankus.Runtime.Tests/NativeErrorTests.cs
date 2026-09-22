using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies bounded native error-buffer writes, Unicode boundaries, and secondary-failure containment.
/// </summary>
[TestClass]
public sealed class NativeErrorTests
{
    /// <summary>
    /// Verifies exact UTF-8 truncation at scalar boundaries, termination, and untouched guard bytes.
    /// </summary>
    /// <param name="capacity">The writable capacity including the terminator.</param>
    /// <param name="expected">The expected complete UTF-8 prefix.</param>
    [TestMethod]
    [DataRow(1, "")]
    [DataRow(2, "A")]
    [DataRow(3, "A")]
    [DataRow(4, "Aé")]
    [DataRow(7, "Aé")]
    [DataRow(8, "Aé🐘")]
    [DataRow(9, "Aé🐘Z")]
    public unsafe void MessageIsBoundedAndNullTerminated(int capacity, string expected)
    {
        byte[] buffer = Enumerable.Repeat((byte)0xCC, capacity + 2).ToArray();
        fixed (byte* pointer = buffer)
        {
            NativeError.Write(new InvalidOperationException("Aé🐘Z"), pointer + 1, capacity);
        }

        int written = Encoding.UTF8.GetByteCount(expected);
        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes(expected).AsSpan(), buffer.AsSpan(1, written));
        Assert.AreEqual((byte)0, buffer[written + 1]);
        Assert.AreEqual((byte)0xCC, buffer[0]);
        Assert.AreEqual((byte)0xCC, buffer[^1]);
    }

    /// <summary>
    /// Verifies secondary exceptions cannot escape through an unmanaged callback.
    /// </summary>
    [TestMethod]
    public unsafe void BrokenExceptionMessageUsesFallback()
    {
        byte[] buffer = new byte[128];
        fixed (byte* pointer = buffer)
        {
            NativeError.Write(new ThrowingMessageException(), pointer, buffer.Length);
        }

        int terminator = Array.IndexOf(buffer, (byte)0);
        Assert.AreEqual("Managed extension function failed.", Encoding.UTF8.GetString(buffer, 0, terminator));
    }

    /// <summary>
    /// Verifies an empty destination has no writes even when a pointer is provided.
    /// </summary>
    [TestMethod]
    public unsafe void ZeroCapacityDoesNotWrite()
    {
        byte guard = 0xCC;
        NativeError.Write(new InvalidOperationException("failure"), &guard, 0);

        Assert.AreEqual((byte)0xCC, guard);
    }
}
