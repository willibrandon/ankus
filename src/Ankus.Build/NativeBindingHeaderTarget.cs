using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Records header and compiler target identity in the same translation unit as collected native types.
/// </summary>
internal static class NativeBindingHeaderTarget
{
    /// <summary>
    /// Emits constants which the frontend evaluates without linking or running a target executable.
    /// </summary>
    internal static string GenerateSource(string headers, int major)
    {
        var source = new StringBuilder(headers);
        source.AppendLine();
        source.AppendLine("#include <limits.h>");
        source.AppendLine("#include <stdint.h>");
        NativeBindingTarget.Write(source);
        source.AppendLine(CultureInfo.InvariantCulture, $"#if PG_VERSION_NUM / 10000 != {major}");
        source.AppendLine("#error Selected native headers have the wrong PostgreSQL major");
        source.AppendLine("#endif");
        source.AppendLine("#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_LITTLE_ENDIAN__");
        source.AppendLine("#define ANKUS_HEADER_LITTLE_ENDIAN 1");
        source.AppendLine("#elif defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__");
        source.AppendLine("#define ANKUS_HEADER_LITTLE_ENDIAN 0");
        source.AppendLine("#elif defined(_WIN32)");
        source.AppendLine("#define ANKUS_HEADER_LITTLE_ENDIAN 1");
        source.AppendLine("#else");
        source.AppendLine("#error Unknown native byte order");
        source.AppendLine("#endif");
        source.AppendLine("enum { ankus_header_pg_version = PG_VERSION_NUM, ankus_header_pointer_size = sizeof(void*),");
        source.AppendLine("    ankus_header_little_endian = ANKUS_HEADER_LITTLE_ENDIAN, ankus_header_clang_major = __clang_major__ };");
        source.AppendLine("const char ankus_header_runtime_identifier[] = ANKUS_NATIVE_OS \"-\" ANKUS_NATIVE_ARCH;");
        return source.ToString();
    }

    /// <summary>
    /// Requires consistent version, compiler, pointer and byte-order facts from the selected translation unit.
    /// </summary>
    internal static NativeHeaderTarget Read(JsonElement root, int major)
    {
        try
        {
            return ReadCore(root, major);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("Malformed native header target observation.", exception);
        }
    }

    private static NativeHeaderTarget ReadCore(JsonElement root, int major)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        string? runtime = null;
        foreach (JsonElement node in root.GetProperty("inner").EnumerateArray())
        {
            string? kind = node.GetProperty("kind").GetString();
            if (kind == "EnumDecl" && node.TryGetProperty("inner", out JsonElement members))
            {
                foreach (JsonElement member in members.EnumerateArray())
                {
                    if (!member.TryGetProperty("name", out JsonElement nameProperty)) { continue; }

                    string name = nameProperty.GetString()!;
                    if (name is not ("ankus_header_pg_version" or "ankus_header_pointer_size" or "ankus_header_little_endian" or "ankus_header_clang_major")) { continue; }

                    string value = Constant(member, "ConstantExpr");
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || !numbers.TryAdd(name, number))
                    {
                        throw new FormatException("Invalid or duplicate native header target constant.");
                    }
                }
            }
            else if (kind == "VarDecl" && node.GetProperty("name").GetString() == "ankus_header_runtime_identifier")
            {
                string value = Constant(node, "StringLiteral");
                if (runtime is not null || value.Length < 2 || value[0] != '"' || value[^1] != '"')
                {
                    throw new FormatException("Invalid or duplicate native header target identifier.");
                }

                runtime = value[1..^1];
            }
        }

        if (runtime is null || !numbers.TryGetValue("ankus_header_pg_version", out int version) || version / 10000 != major ||
            !numbers.TryGetValue("ankus_header_pointer_size", out int size) || !numbers.TryGetValue("ankus_header_little_endian", out int endian) ||
            endian is not (0 or 1) || !numbers.TryGetValue("ankus_header_clang_major", out int compiler) || compiler <= 0 ||
            !NativeBindingTarget.IsValid(runtime, size, endian == 1))
        {
            throw new FormatException("Missing or inconsistent native header target identity.");
        }

        return new(version, runtime, size, endian == 1, compiler);
    }

    private static string Constant(JsonElement node, string kind)
    {
        while (node.GetProperty("kind").GetString() != kind)
        {
            if (!node.TryGetProperty("inner", out JsonElement inner) || inner.ValueKind != JsonValueKind.Array || inner.GetArrayLength() != 1)
            {
                throw new FormatException("Missing native compiler constant expression.");
            }

            node = inner[0];
        }

        return node.GetProperty("value").GetString() ?? throw new FormatException("Missing native compiler constant value.");
    }
}

/// <summary>
/// Identifies the headers and frontend target used to collect native type facts.
/// </summary>
/// <param name="PostgresVersion">The selected header's exact PG_VERSION_NUM.</param>
/// <param name="RuntimeIdentifier">The compiler's operating-system and processor target.</param>
/// <param name="PointerSize">The target pointer width in bytes.</param>
/// <param name="IsLittleEndian">Whether the target uses little-endian byte order.</param>
/// <param name="ClangMajor">The frontend's reported major version.</param>
internal sealed record NativeHeaderTarget(int PostgresVersion, string RuntimeIdentifier, int PointerSize, bool IsLittleEndian, int ClangMajor);
