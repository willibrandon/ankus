using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Identifies the actual native compiler target independently of the managed build host.
/// </summary>
internal static class NativeBindingTarget
{
    /// <summary>
    /// Requires the compiling headers and primitive model to match the independently measured target.
    /// </summary>
    /// <param name="source">The translation unit after its selected PostgreSQL headers.</param>
    /// <param name="target">The target measured from the original compiler.</param>
    /// <param name="contract">The diagnostic name for the native contract being checked.</param>
    internal static void WriteChecks(StringBuilder source, NativeHeaderTarget target, string contract)
    {
        int separator = target.RuntimeIdentifier.LastIndexOf('-');
        string operatingSystem = target.RuntimeIdentifier[..separator] switch
        {
            "win" => "defined(_WIN32)",
            "osx" => "defined(__APPLE__) && defined(__MACH__)",
            "linux" => "defined(__linux__) && defined(__GLIBC__)",
            "linux-musl" => "defined(__linux__) && !defined(__GLIBC__) && !defined(__ANDROID__)",
            _ => throw new FormatException("Unsupported native operating system."),
        };
        string architecture = target.RuntimeIdentifier[(separator + 1)..] switch
        {
            "x64" => "defined(_M_X64) || defined(__x86_64__)",
            "x86" => "defined(_M_IX86) || defined(__i386__)",
            "arm64" => "defined(_M_ARM64) || defined(__aarch64__)",
            "arm" => "defined(_M_ARM) || defined(__arm__)",
            _ => throw new FormatException("Unsupported native processor."),
        };
        source.AppendLine("#include <limits.h>");
        NativeBindingNumericModel.WriteChecks(source, target.Numeric);
        source.AppendLine(CultureInfo.InvariantCulture, $"#if PG_VERSION_NUM != {target.PostgresVersion} || !({operatingSystem}) || !({architecture})");
        source.Append("#error ").Append(contract).AppendLine(" target changed");
        source.AppendLine("#endif");
        source.AppendLine(CultureInfo.InvariantCulture,
            $"_Static_assert(CHAR_BIT == 8 && sizeof(void *) == {target.PointerSize}, \"{contract} primitive model changed\");");
        source.AppendLine(target.IsLittleEndian
            ? "#if !defined(_WIN32) && (!defined(__BYTE_ORDER__) || __BYTE_ORDER__ != __ORDER_LITTLE_ENDIAN__)"
            : "#if !defined(__BYTE_ORDER__) || __BYTE_ORDER__ != __ORDER_BIG_ENDIAN__");
        source.Append("#error ").Append(contract).AppendLine(" byte order changed");
        source.AppendLine("#endif");
    }

    /// <summary>
    /// Emits target identification from compiler and C library predefined macros.
    /// </summary>
    /// <param name="source">The probe source after including its system headers.</param>
    internal static void Write(StringBuilder source)
    {
        source.AppendLine("#if CHAR_BIT != 8");
        source.AppendLine("#error Ankus bindings require eight-bit native bytes");
        source.AppendLine("#endif");
        source.AppendLine("#if defined(_WIN32)");
        source.AppendLine("#define ANKUS_NATIVE_OS \"win\"");
        source.AppendLine("#elif defined(__APPLE__) && defined(__MACH__)");
        source.AppendLine("#define ANKUS_NATIVE_OS \"osx\"");
        source.AppendLine("#elif defined(__linux__) && defined(__GLIBC__)");
        source.AppendLine("#define ANKUS_NATIVE_OS \"linux\"");
        source.AppendLine("#elif defined(__linux__) && !defined(__ANDROID__)");
        source.AppendLine("#define ANKUS_NATIVE_OS \"linux-musl\"");
        source.AppendLine("#else");
        source.AppendLine("#error Unsupported native operating system for Ankus bindings");
        source.AppendLine("#endif");
        source.AppendLine("#if defined(_M_ARM64) || defined(__aarch64__)");
        source.AppendLine("#define ANKUS_NATIVE_ARCH \"arm64\"");
        source.AppendLine("#elif defined(_M_X64) || defined(__x86_64__)");
        source.AppendLine("#define ANKUS_NATIVE_ARCH \"x64\"");
        source.AppendLine("#elif defined(_M_IX86) || defined(__i386__)");
        source.AppendLine("#define ANKUS_NATIVE_ARCH \"x86\"");
        source.AppendLine("#elif defined(_M_ARM) || defined(__arm__)");
        source.AppendLine("#define ANKUS_NATIVE_ARCH \"arm\"");
        source.AppendLine("#else");
        source.AppendLine("#error Unsupported native processor for Ankus bindings");
        source.AppendLine("#endif");
    }

    /// <summary>
    /// Rejects unknown targets and primitive models that contradict their measured architecture.
    /// </summary>
    /// <param name="runtimeIdentifier">The target identifier reported by the native probe.</param>
    /// <param name="pointerSize">The measured pointer width.</param>
    /// <param name="littleEndian">The measured byte order.</param>
    /// <returns>Whether the target and its primitive model are supported and consistent.</returns>
    internal static bool IsValid(string runtimeIdentifier, int pointerSize, bool littleEndian)
    {
        int separator = runtimeIdentifier.LastIndexOf('-');
        if (separator < 0 || runtimeIdentifier[..separator] is not ("win" or "osx" or "linux" or "linux-musl"))
        {
            return false;
        }

        return runtimeIdentifier[(separator + 1)..] switch
        {
            "x64" => pointerSize == 8 && littleEndian,
            "x86" => pointerSize == 4 && littleEndian,
            "arm64" => pointerSize == 8,
            "arm" => pointerSize == 4,
            _ => false,
        };
    }
}
