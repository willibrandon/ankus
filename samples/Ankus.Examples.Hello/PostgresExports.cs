using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Defines the C ABI entry points PostgreSQL resolves from the Native AOT shared
/// library. These wrappers are generated from attributes in the full framework.
/// </summary>
internal static unsafe class PostgresExports
{
    private static readonly nint s_magic = Allocate(PgMagic.Create());
    private static readonly nint s_addFunctionInfo = Allocate(PgFinfoRecord.Create());

    [UnmanagedCallersOnly(EntryPoint = "Pg_magic_func")]
    private static nint GetModuleMagic() => s_magic;

    [UnmanagedCallersOnly(EntryPoint = "pg_finfo_add_wrapper")]
    private static nint GetAddFunctionInfo() => s_addFunctionInfo;

    [UnmanagedCallersOnly(EntryPoint = "add_wrapper")]
    private static ulong Add(nint functionCallInfo)
    {
        NullableDatum* arguments = (NullableDatum*)((byte*)functionCallInfo + sizeof(FunctionCallInfo));
        int left = (int)arguments[0].Value;
        int right = (int)arguments[1].Value;
        int result = unchecked(left + right);
        return unchecked((ulong)(long)result);
    }

    private static nint Allocate<T>(T value)
        where T : unmanaged
    {
        T* address = (T*)NativeMemory.Alloc((nuint)sizeof(T));
        *address = value;
        return (nint)address;
    }
}
