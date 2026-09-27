using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Invokes measured function pointers only inside native bodies beneath the PostgreSQL error guard.
/// </summary>
internal static class NativeBindingIndirectSource
{
    /// <summary>
    /// Emits selected indirect bodies after the common native frame declarations and original selected headers.
    /// </summary>
    /// <param name="records">The complete header and native storage contract.</param>
    /// <param name="signatures">Canonical fixed signatures selected for invocation.</param>
    /// <returns>Checked native bodies whose first argument contains the target function pointer.</returns>
    internal static string Bodies(NativeHeaderRecords records, IReadOnlyList<int> signatures)
    {
        IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Select(records, signatures);
        var values = new List<(int Type, string Name)>();
        foreach (NativeBindingIndirectCall call in calls)
        {
            string prefix = BodyName(call.FunctionType);
            values.Add((call.PointerType, prefix + "_target"));
            for (int index = 0; index < call.Parameters.Count; index++)
            {
                values.Add((call.Parameters[index], prefix + "_argument_" + Number(index)));
            }

            if (call.Result is int result)
            {
                values.Add((result, prefix + "_result"));
            }
        }

        var source = new StringBuilder(NativeBindingRecordChecks.DeclareValues(records, values));
        foreach (NativeBindingIndirectCall call in calls)
        {
            string prefix = BodyName(call.FunctionType);
            string[] aliases = [prefix + "_target", .. call.Parameters.Select((_, index) => prefix + "_argument_" + Number(index))];
            string result = prefix + "_result";
            source.AppendLine("#if !defined(_WIN32)\n__attribute__((visibility(\"hidden\")))\n#endif");
            source.Append("int ").Append(prefix).AppendLine("(const AnkusNativeCallArgument *arguments, size_t count, void *result, size_t result_size)");
            source.AppendLine("{");
            source.AppendLine(CultureInfo.InvariantCulture, $"    if (count != {Number(aliases.Length)}) return ANKUS_CALL_COUNT;");
            source.AppendLine("    if (arguments == NULL) return ANKUS_CALL_ARGUMENTS;");
            source.AppendLine(call.Result is null ? "    if (result != NULL || result_size != 0) return ANKUS_CALL_RESULT;"
                : $"    if (result == NULL || result_size != sizeof({result})) return ANKUS_CALL_RESULT;");
            for (int index = 0; index < aliases.Length; index++)
            {
                string position = Number(index);
                source.AppendLine(CultureInfo.InvariantCulture, $"    if (arguments[{position}].data == NULL || arguments[{position}].size != sizeof({aliases[index]})) return ANKUS_CALL_STORAGE;");
                source.AppendLine(CultureInfo.InvariantCulture, $"    if ((uintptr_t)arguments[{position}].data % _Alignof({aliases[index]}) != 0) return ANKUS_CALL_ALIGNMENT;");
            }

            source.AppendLine(CultureInfo.InvariantCulture, $"    {aliases[0]} target = *({aliases[0]} *)arguments[0].data;");
            source.AppendLine("    if (target == NULL) return ANKUS_CALL_TARGET;");
            string arguments = string.Join(", ", aliases.Skip(1).Select((alias, index) => $"*({alias} *)arguments[{Number(index + 1)}].data"));
            source.AppendLine(call.Result is null ? $"    target({arguments});" : $"    {result} value = target({arguments});\n    memcpy(result, &value, sizeof(value));");
            source.AppendLine("    return ANKUS_CALL_OK;\n}");
        }

        return source.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Names a native body by its canonical signature within the complete measured binding.
    /// </summary>
    internal static string BodyName(int signature) => "ankus_native_indirect_" + Number(signature);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
