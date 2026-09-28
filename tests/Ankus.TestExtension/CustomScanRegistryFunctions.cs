using System.Runtime.InteropServices;
using System.Text;
using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises the native custom-scan registry with backend-owned copies of a real provider's method table.
/// </summary>
public static unsafe partial class CustomScanRegistryFunctions
{
    /// <summary>
    /// Supplies a callable registry witness which advertises no planning hook or executable scans.
    /// </summary>
    [PgNativeCallback(nameof(RejectPlan))]
    private static partial CustomScanMethods_CreateCustomScanStateCallback UnavailableFactory { get; }

    /// <summary>
    /// Registers a distinct backend-owned table and reclaims attempted storage when native registration rejects it.
    /// </summary>
    /// <param name="name">The exact UTF8 registry name, including native byte-length boundary cases.</param>
    /// <param name="copyTrace">Whether to copy the loaded trace table or reserve a name before that module loads.</param>
    /// <returns>The native address retained by PostgreSQL.</returns>
    [PgFunction]
    public static long CustomScanRegistryRegister(string name, bool copyTrace = true)
    {
        CustomScanMethods original = copyTrace
            ? *(CustomScanMethods*)CustomScanRegistryLookup("Ankus Trace", false)
            : new CustomScanMethods { CreateCustomScanState = UnavailableFactory };
        PgMemoryContext owner = PgMemoryContext.Create("Ankus registry probe", PgMemoryContext.Get(PgMemoryContextKind.Top)!);
        try
        {
            return owner.Run(() =>
            {
                var methods = (CustomScanMethods*)NativeMethods.palloc((nuint)sizeof(CustomScanMethods));
                *methods = original;
                byte[] text = Encoding.UTF8.GetBytes(name + "\0");
                fixed (byte* bytes = text)
                {
                    methods->CustomName = NativeMethods.pstrdup((nint)bytes);
                }

                NativeMethods.RegisterCustomScanMethods((nint)methods);
                return (long)methods;
            });
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the native registry without substituting a managed map or altering PostgreSQL's missing-name behavior.
    /// </summary>
    /// <param name="name">The exact case-sensitive native name.</param>
    /// <param name="optional">Whether PostgreSQL may return a null pointer for a missing entry.</param>
    /// <returns>The registered method address or zero for an optional missing entry.</returns>
    [PgFunction]
    public static long CustomScanRegistryLookup(string name, bool optional)
    {
        byte[] text = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* bytes = text)
        {
            return NativeMethods.GetCustomScanMethods((nint)bytes, optional);
        }
    }

    /// <summary>
    /// Reads retained name bytes after the registering statement, transaction and managed buffers have ended.
    /// </summary>
    /// <param name="name">The registered native name.</param>
    /// <returns>The exact UTF8 name stored in the retained native table.</returns>
    [PgFunction]
    public static string CustomScanRegistryName(string name)
    {
        var methods = (CustomScanMethods*)CustomScanRegistryLookup(name, false);
        return Marshal.PtrToStringUTF8(methods->CustomName)!;
    }

    /// <summary>
    /// Rejects execution explicitly because the reserved-name witness does not create any paths or plans.
    /// </summary>
    private static nint RejectPlan(nint plan)
    {
        _ = plan;
        throw new PgException("0A000", "registry witness does not supply plans");
    }
}
