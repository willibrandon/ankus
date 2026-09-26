using System.Runtime.InteropServices;

namespace Ankus.Build;

/// <summary>
/// Owns a parsed translation unit and its index while retaining the selected compiler library.
/// </summary>
/// <param name="value">The successfully parsed translation unit.</param>
/// <param name="index">The index which owns the unit.</param>
/// <param name="library">The library with one transferred dangerous reference.</param>
internal sealed unsafe class NativeClangUnit(nint value, nint index, NativeClang library) : SafeHandle(value, ownsHandle: true)
{
    /// <inheritdoc />
    public override bool IsInvalid => handle == 0;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        ((delegate* unmanaged[Cdecl]<nint, void>)library.Export("clang_disposeTranslationUnit"))(handle);
        ((delegate* unmanaged[Cdecl]<nint, void>)library.Export("clang_disposeIndex"))(index);
        library.DangerousRelease();
        return true;
    }
}
