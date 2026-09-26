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
        IReadOnlyList<NativeBindingCall> calls = NativeBindingCallModel.Select(records, names);
        Dictionary<string, NativeHeaderSymbol> selected = calls.ToDictionary(static call => call.Name, static call => call.Symbol, StringComparer.Ordinal);
        var source = new StringBuilder(NativeBindingHeaderParser.GenerateChecks(headers, selected));
        source.AppendLine("#include <stddef.h>");
        source.AppendLine("#include <stdint.h>");
        source.AppendLine("#include <string.h>");
        NativeBindingTarget.WriteChecks(source, records.Headers.Target, "Native call");
        source.AppendLine("/* These bodies require a native error guard; they must never be called directly from managed code. */");
        source.AppendLine("typedef struct AnkusNativeCallArgument { const void *data; size_t size; } AnkusNativeCallArgument;");
        source.AppendLine("enum AnkusNativeCallStatus { ANKUS_CALL_OK, ANKUS_CALL_COUNT, ANKUS_CALL_ARGUMENTS, ANKUS_CALL_RESULT, ANKUS_CALL_STORAGE, ANKUS_CALL_ALIGNMENT };");
        foreach (NativeBindingCall contract in calls)
        {
            string name = contract.Name;
            string prefix = "ankus_native_call_" + name;
            var aliases = new List<string>();
            for (int index = 0; index < contract.Parameters.Count; index++)
            {
                string alias = prefix + "_argument_" + index.ToString(CultureInfo.InvariantCulture);
                WriteType(source, records.Graph, contract.Parameters[index], alias);
                aliases.Add(alias);
            }

            bool hasResult = contract.Result is not null;
            string resultAlias = prefix + "_result";
            if (contract.Result is NativeBindingCallValue result) { WriteType(source, records.Graph, result, resultAlias); }

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
            string call = "(" + NativeBindingCompilerShims.Reference(contract.Symbol) + ")(" + arguments + ")";
            source.AppendLine(hasResult ? $"    {resultAlias} value = {call};" : $"    {call};");
            if (hasResult) { source.AppendLine("    memcpy(result, &value, sizeof(value));"); }

            source.AppendLine("    return ANKUS_CALL_OK;");
            source.AppendLine("}");
        }

        return source.ToString().ReplaceLineEndings("\n");
    }

    private static void WriteType(StringBuilder source, NativeRecordGraph graph, NativeBindingCallValue value, string alias)
    {
        NativeRecordType storage = graph.Types[value.StorageType];
        source.Append("typedef ").Append(value.Type.Declare(alias)).AppendLine(";");
        source.AppendLine(CultureInfo.InvariantCulture,
            $"_Static_assert(sizeof({alias}) == {storage.Size!.Value} && _Alignof({alias}) == {storage.Alignment!.Value}, \"Native call storage changed: {alias}\");");
    }
}
