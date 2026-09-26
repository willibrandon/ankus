using System.Runtime.InteropServices;

namespace Ankus.Build;

/// <summary>
/// Matches the stable clang-c CXCursor ABI; its opaque data belongs to the live translation unit.
/// </summary>
/// <param name="Kind">The native cursor kind.</param>
/// <param name="Flags">The native cursor auxiliary value.</param>
/// <param name="First">The first opaque native slot.</param>
/// <param name="Second">The second opaque native slot.</param>
/// <param name="Third">The third opaque native slot.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangCursor(int Kind, int Flags, nint First, nint Second, nint Third);

/// <summary>
/// Matches CXType without interpreting the compiler's opaque type pointers.
/// </summary>
/// <param name="Kind">The native type kind.</param>
/// <param name="First">The first opaque native slot.</param>
/// <param name="Second">The second opaque native slot.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangType(int Kind, nint First, nint Second);

/// <summary>
/// Matches CXString; the library's string disposer must release its native storage.
/// </summary>
/// <param name="Data">The opaque string data.</param>
/// <param name="Flags">The native string ownership flags.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangString(nint Data, uint Flags);
