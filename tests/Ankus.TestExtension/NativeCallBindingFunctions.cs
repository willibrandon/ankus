using System.Globalization;
using System.Text;
using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Checks the active generated contract before a real native aggregate call can observe its frame.
/// </summary>
public static unsafe class NativeCallBindingFunctions
{
    /// <summary>
    /// Rejects incompatible declaration contracts before changing native result bytes, then repeats a valid call.
    /// </summary>
    /// <param name="body">The generated FullTransactionId body address.</param>
    /// <param name="mode">The identity or server-version mismatch partition.</param>
    /// <returns>The native error, untouched rejected result and exact recovered aggregate value.</returns>
    [PgFunction]
    public static string RawCallBinding(long body, int mode)
    {
        byte[] identity = Encoding.UTF8.GetBytes(NativeBinding.Identity);
        byte[] incompatible = [.. identity];
        int major = Spi.ExecuteScalar<int>("SELECT current_setting('server_version_num')::integer / 10000");
        int otherMajor = major;
        switch (mode)
        {
            case 0: incompatible[0] ^= 1; break;
            case 1: incompatible[^1] ^= 1; break;
            case 2: incompatible = []; break;
            case 3: incompatible = identity[..^1]; break;
            case 4: incompatible = [.. identity, 0]; break;
            case 5: otherMajor++; break;
            case 6: otherMajor = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ulong value = 0xfedcba9876543210;
        ulong result = 0;
        NativeRawCall.ValidateBinding(identity, major);
        NativeRawCall.Invoke((nint)body, [new((nint)(&value), sizeof(ulong))], (nint)(&result), sizeof(ulong));
        if (result != value) { throw new InvalidOperationException("The initial checked call lost its native value."); }

        result = 12345;
        PgException? failure = null;
        try
        {
            NativeRawCall.ValidateBinding(incompatible, otherMajor);
            NativeRawCall.Invoke((nint)body, [new((nint)(&value), sizeof(ulong))], (nint)(&result), sizeof(ulong));
        }
        catch (PgException error) { failure = error; }

        if (failure is null) { throw new InvalidOperationException("The incompatible call binding was accepted."); }

        ulong rejected = result;
        value = ulong.MaxValue;
        NativeRawCall.ValidateBinding(identity, major);
        NativeRawCall.Invoke((nint)body, [new((nint)(&value), sizeof(ulong))], (nint)(&result), sizeof(ulong));
        return $"{failure.SqlState}|{failure.Message}|{rejected}|{result.ToString("X16", CultureInfo.InvariantCulture)}";
    }
}
