using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Lowers fixed native prototypes to storage-address call bodies for execution beneath a native error guard.
/// </summary>
internal static class NativeBindingCallSource
{
    /// <summary>
    /// Emits typed C calls whose compiler supplies the native scalar and aggregate calling conventions.
    /// </summary>
    /// <param name="records">The validated selected-header signatures and native storage graph.</param>
    /// <param name="headers">The selected target's original C headers.</param>
    /// <returns>Native call bodies; the caller must provide the PostgreSQL error guard and backend thread contract.</returns>
    internal static string Generate(NativeHeaderRecords records, string headers)
    {
        ArgumentNullException.ThrowIfNull(records);
        return Generate(records, headers, [.. records.Headers.Symbols.Keys]);
    }

    /// <summary>
    /// Emits selected fixed bodies while retaining and validating the complete shared declaration graph.
    /// </summary>
    /// <param name="records">The complete native signature and storage contract, including other functions and globals.</param>
    /// <param name="headers">The original target headers.</param>
    /// <param name="names">Unique function names whose fixed bodies should be emitted.</param>
    /// <returns>Typed native bodies that share the original graph's storage identities.</returns>
    internal static string Generate(NativeHeaderRecords records, string headers, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(names);
        NativeBindingSignatureValidation.Validate(records);
        var selected = new SortedDictionary<string, NativeHeaderSymbol>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!records.Headers.Symbols.TryGetValue(name, out NativeHeaderSymbol? symbol) || !selected.TryAdd(name, symbol))
            {
                throw new FormatException($"Unknown or duplicate native call selection '{name}'.");
            }
        }

        var source = new StringBuilder(NativeBindingHeaderParser.GenerateChecks(headers, selected));
        source.AppendLine("#include <stddef.h>");
        source.AppendLine("#include <stdint.h>");
        source.AppendLine("#include <string.h>");
        WriteTarget(source, records.Headers.Target);
        source.AppendLine("/* These bodies require a native error guard; they must never be called directly from managed code. */");
        source.AppendLine("typedef struct AnkusNativeCallArgument { const void *data; size_t size; } AnkusNativeCallArgument;");
        source.AppendLine("enum AnkusNativeCallStatus { ANKUS_CALL_OK, ANKUS_CALL_COUNT, ANKUS_CALL_ARGUMENTS, ANKUS_CALL_RESULT, ANKUS_CALL_STORAGE, ANKUS_CALL_ALIGNMENT };");
        foreach ((string name, NativeHeaderSymbol symbol) in selected)
        {
            NativeBindingCDeclaration.ValidateName(name);
            NativeBindingCDeclaration.ValidateName(symbol.NativeName);
            NativeRecordGraph graph = records.Graph;
            NativeRecordType root = graph.Types[graph.Types[graph.Roots[name]].Canonical];
            if (!symbol.IsFunction || Canonical(symbol.Type) is not NativeHeaderFunction function || root.Function is not NativeRecordFunction shape)
            {
                throw new FormatException($"Native call {name} requires a function declaration.");
            }

            if (!function.HasPrototype || !shape.HasPrototype || function.IsVariadic || shape.IsVariadic)
            {
                throw new FormatException($"Native call {name} requires a fixed prototype; variadic and unprototyped calls need explicit call-site types.");
            }

            int declaredIndex = graph.Roots[name];
            var visited = new HashSet<int>();
            NativeRecordType declared = graph.Types[declaredIndex];
            while (declared.Function is null && declared.Element is int element)
            {
                if (!visited.Add(declaredIndex)) { throw new FormatException($"Native call {name} has a cyclic function alias."); }

                declaredIndex = element;
                declared = graph.Types[element];
            }

            if (declared.Function is not NativeRecordFunction written || function.Parameters.Count != shape.Parameters.Count ||
                function.Parameters.Count != written.Parameters.Count)
            {
                throw new FormatException($"Native call {name} has inconsistent parameter shapes.");
            }

            string prefix = "ankus_native_call_" + name;
            var aliases = new List<string>();
            for (int index = 0; index < function.Parameters.Count; index++)
            {
                string alias = prefix + "_argument_" + index.ToString(CultureInfo.InvariantCulture);
                NativeRecordType storage = graph.Types[written.Parameters[index]];
                if (graph.Types[storage.Canonical].Kind is "array" or "function") { storage = graph.Types[shape.Parameters[index]]; }

                WriteType(source, function.Parameters[index], storage, alias);
                aliases.Add(alias);
            }

            bool hasResult = Canonical(function.Result) is not NativeHeaderScalar { Name: "void" };
            NativeRecordType result = graph.Types[written.Result];
            NativeRecordType canonicalResult = graph.Types[result.Canonical];
            if (hasResult != (canonicalResult.Kind != "scalar" || canonicalResult.Name != "void"))
            {
                throw new FormatException($"Native call {name} has inconsistent result shapes.");
            }

            string resultAlias = prefix + "_result";
            if (hasResult) { WriteType(source, function.Result, result, resultAlias); }

            source.AppendLine("#if !defined(_WIN32)\n__attribute__((visibility(\"hidden\")))\n#endif");
            source.AppendLine(CultureInfo.InvariantCulture,
                $"int {prefix}(const AnkusNativeCallArgument *arguments, size_t count, void *result, size_t result_size)");
            source.AppendLine("{");
            source.AppendLine(CultureInfo.InvariantCulture, $"    if (count != {aliases.Count}) return ANKUS_CALL_COUNT;");
            source.AppendLine(aliases.Count == 0 ? "    (void) arguments;" : "    if (arguments == NULL) return ANKUS_CALL_ARGUMENTS;");
            source.AppendLine(hasResult
                ? $"    if (result == NULL || result_size != sizeof({resultAlias})) return ANKUS_CALL_RESULT;"
                : "    if (result != NULL || result_size != 0) return ANKUS_CALL_RESULT;");
            for (int index = 0; index < aliases.Count; index++)
            {
                string alias = aliases[index];
                source.AppendLine(CultureInfo.InvariantCulture,
                    $"    if (arguments[{index}].data == NULL || arguments[{index}].size != sizeof({alias})) return ANKUS_CALL_STORAGE;");
                source.AppendLine(CultureInfo.InvariantCulture,
                    $"    if ((uintptr_t) arguments[{index}].data % _Alignof({alias}) != 0) return ANKUS_CALL_ALIGNMENT;");
            }

            // Each typedef already retains the native qualifiers; an extra const duplicates qualified arguments on MSVC.
            string arguments = string.Join(", ", aliases.Select(static (alias, index) =>
                string.Create(CultureInfo.InvariantCulture, $"*({alias} *) arguments[{index}].data")));
            string call = "(" + symbol.NativeName + ")(" + arguments + ")";
            source.AppendLine(hasResult ? $"    {resultAlias} value = {call};" : $"    {call};");
            if (hasResult) { source.AppendLine("    memcpy(result, &value, sizeof(value));"); }

            source.AppendLine("    return ANKUS_CALL_OK;");
            source.AppendLine("}");
        }

        return source.ToString().ReplaceLineEndings("\n");
    }

    private static void WriteType(StringBuilder source, NativeHeaderType type, NativeRecordType storage, string alias)
    {
        if (storage.Size is not long size || size < 0 || storage.Alignment is not long alignment || alignment <= 0 ||
            storage.Kind is "array" or "function" || Canonical(type) is NativeHeaderArray or NativeHeaderFunction)
        {
            throw new FormatException($"Native call value {alias} requires a complete object representation.");
        }

        source.Append("typedef ").Append(type.Declare(alias)).AppendLine(";");
        source.AppendLine(CultureInfo.InvariantCulture,
            $"_Static_assert(sizeof({alias}) == {size} && _Alignof({alias}) == {alignment}, \"Native call storage changed: {alias}\");");
    }

    private static void WriteTarget(StringBuilder source, NativeHeaderTarget target)
    {
        int separator = target.RuntimeIdentifier.LastIndexOf('-');
        string operatingSystem = target.RuntimeIdentifier[..separator] switch
        {
            "win" => "defined(_WIN32)",
            "osx" => "defined(__APPLE__) && defined(__MACH__)",
            "linux" => "defined(__linux__) && defined(__GLIBC__)",
            "linux-musl" => "defined(__linux__) && !defined(__GLIBC__) && !defined(__ANDROID__)",
            _ => throw new FormatException("Unsupported native call operating system."),
        };
        string architecture = target.RuntimeIdentifier[(separator + 1)..] switch
        {
            "x64" => "defined(_M_X64) || defined(__x86_64__)",
            "x86" => "defined(_M_IX86) || defined(__i386__)",
            "arm64" => "defined(_M_ARM64) || defined(__aarch64__)",
            "arm" => "defined(_M_ARM) || defined(__arm__)",
            _ => throw new FormatException("Unsupported native call processor."),
        };
        source.AppendLine("#include <limits.h>");
        NativeBindingNumericModel.WriteChecks(source, target.Numeric);
        source.AppendLine(CultureInfo.InvariantCulture, $"#if PG_VERSION_NUM != {target.PostgresVersion} || !({operatingSystem}) || !({architecture})");
        source.AppendLine("#error Native call target changed");
        source.AppendLine("#endif");
        source.AppendLine(CultureInfo.InvariantCulture,
            $"_Static_assert(CHAR_BIT == 8 && sizeof(void *) == {target.PointerSize}, \"Native call primitive model changed\");");
        source.AppendLine(target.IsLittleEndian
            ? "#if !defined(_WIN32) && (!defined(__BYTE_ORDER__) || __BYTE_ORDER__ != __ORDER_LITTLE_ENDIAN__)"
            : "#if !defined(__BYTE_ORDER__) || __BYTE_ORDER__ != __ORDER_BIG_ENDIAN__");
        source.AppendLine("#error Native call byte order changed");
        source.AppendLine("#endif");
    }

    private static NativeHeaderType Canonical(NativeHeaderType type)
    {
        while (true)
        {
            switch (type)
            {
                case NativeHeaderAlias alias: type = alias.Underlying; break;
                case NativeHeaderQualified qualified: type = qualified.Underlying; break;
                case NativeHeaderAdjusted adjusted: type = adjusted.Adjusted; break;
                default: return type;
            }
        }
    }
}
