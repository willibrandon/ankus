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

/// <summary>
/// Matches CXSourceLocation; its opaque data belongs to the live translation unit.
/// </summary>
/// <param name="First">The first opaque native slot.</param>
/// <param name="Second">The second opaque native slot.</param>
/// <param name="Data">The encoded location.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangLocation(nint First, nint Second, uint Data);

/// <summary>
/// Matches CXSourceRange; its opaque data belongs to the live translation unit.
/// </summary>
/// <param name="First">The first opaque native slot.</param>
/// <param name="Second">The second opaque native slot.</param>
/// <param name="Begin">The encoded start.</param>
/// <param name="End">The encoded end.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangRange(nint First, nint Second, uint Begin, uint End);

/// <summary>
/// Matches CXToken; its opaque data belongs to the tokenizer's allocation.
/// </summary>
/// <param name="First">The first encoded value.</param>
/// <param name="Second">The second encoded value.</param>
/// <param name="Third">The third encoded value.</param>
/// <param name="Fourth">The fourth encoded value.</param>
/// <param name="Data">The opaque native slot.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeClangToken(uint First, uint Second, uint Third, uint Fourth, nint Data);
