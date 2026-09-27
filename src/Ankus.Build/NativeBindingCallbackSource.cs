using System.Globalization;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Emits native callback entry points whose compiler owns the complete native argument and result ABI.
/// </summary>
internal static class NativeBindingCallbackSource
{
    /// <summary>
    /// Constructs selected native functions which dispatch through the extension's managed exception boundary.
    /// </summary>
    /// <param name="records">The complete selected-header graph and signature contract.</param>
    /// <param name="callbacks">Distinct managed targets and their canonical fixed prototypes.</param>
    /// <returns>Typed native entry points and one-time registration accessors; the dispatcher must supply the callback guard.</returns>
    internal static string Generate(NativeHeaderRecords records, IReadOnlyList<NativeBindingCallbackImport> callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        Dictionary<int, NativeBindingIndirectCall> signatures = NativeBindingIndirectModel.Select(records,
            [.. callbacks.Select(static callback => callback.Signature).Distinct()]).ToDictionary(static call => call.FunctionType);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<(int Type, string Name)>();
        NativeBindingCallbackImport[] ordered = [.. callbacks.OrderBy(static callback => callback.Signature)
            .ThenBy(static callback => callback.Identity, StringComparer.Ordinal)];
        foreach (NativeBindingCallbackImport callback in ordered)
        {
            if (!NativeBindingCallbackImports.ValidIdentity(callback.Identity) || !identities.Add(callback.Identity))
            {
                throw new FormatException("A native callback requires a unique deterministic managed target identity.");
            }

            NativeBindingIndirectCall call = signatures[callback.Signature];
            string prefix = Name(callback);
            values.Add((call.PointerType, prefix + "_type"));
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
        if (callbacks.Count == 0)
        {
            return "";
        }

        source.AppendLine("typedef int (*AnkusManagedNativeCallback)(const AnkusNativeCallArgument *, size_t, void *, size_t, void *);");
        source.AppendLine("extern void ankus_dispatch_native_callback(AnkusManagedNativeCallback, const AnkusNativeCallArgument *, size_t, void *, size_t);");
        foreach (NativeBindingCallbackImport callback in ordered)
        {
            NativeBindingIndirectCall call = signatures[callback.Signature];
            string prefix = Name(callback);
            string resultType = prefix + "_result";
            string parameters = call.Parameters.Count == 0 ? "void" : string.Join(", ", call.Parameters.Select((_, index) =>
                prefix + "_argument_" + Number(index) + " argument_" + Number(index)));
            source.AppendLine(CultureInfo.InvariantCulture, $"static AnkusManagedNativeCallback {prefix}_target;");
            source.Append("static ").Append(Convention(records.Graph.Types[call.FunctionType].Function!.CallingConvention, records.Headers.Target.RuntimeIdentifier))
                .Append(call.Result is null ? "void" : resultType).Append(' ').Append(prefix).Append("_entry(").Append(parameters).AppendLine(")");
            source.AppendLine("{");
            if (call.Parameters.Count != 0)
            {
                source.AppendLine("    AnkusNativeCallArgument arguments[] =");
                source.AppendLine("    {");
                for (int index = 0; index < call.Parameters.Count; index++)
                {
                    string name = "argument_" + Number(index);
                    source.AppendLine(CultureInfo.InvariantCulture, $"        {{ &{name}, sizeof({name}) }},");
                }

                source.AppendLine("    };");
            }

            if (call.Result is not null)
            {
                // The byte member remains writable even when the native result typedef carries top-level const.
                source.AppendLine(CultureInfo.InvariantCulture, $"    union {{ {resultType} value; unsigned char bytes[sizeof({resultType}) == 0 ? 1 : sizeof({resultType})]; }} result = {{ .bytes = {{0}} }};");
            }

            source.AppendLine($"    ankus_dispatch_native_callback({prefix}_target, {(call.Parameters.Count == 0 ? "NULL" : "arguments")}, {Number(call.Parameters.Count)}, " +
                (call.Result is null ? "NULL, 0);" : $"result.bytes, sizeof(result.value));"));
            if (call.Result is not null)
            {
                source.AppendLine("    return result.value;");
            }

            source.AppendLine("}\n");
            source.AppendLine("#if !defined(_WIN32)\n__attribute__((visibility(\"hidden\")))\n#endif");
            source.AppendLine(CultureInfo.InvariantCulture, $"{prefix}_type {prefix}(AnkusManagedNativeCallback target)\n{{");
            source.AppendLine(CultureInfo.InvariantCulture, $"    if (target == NULL || ({prefix}_target != NULL && {prefix}_target != target))\n    {{\n        return NULL;\n    }}\n");
            source.AppendLine(CultureInfo.InvariantCulture, $"    {prefix}_target = target;\n    return {prefix}_entry;\n}}\n");
        }

        return source.ToString().ReplaceLineEndings("\n");
    }

    private static string Name(NativeBindingCallbackImport callback)
        => NativeBindingCallbackImports.Prefix + Number(callback.Signature) + "_" + callback.Identity;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Convention(int convention, string runtimeIdentifier) => convention switch
    {
        1 => "",
        2 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "__stdcall " : "__attribute__((stdcall)) ",
        3 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "__fastcall " : "__attribute__((fastcall)) ",
        4 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "__thiscall " : "__attribute__((thiscall)) ",
        10 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "" : "__attribute__((ms_abi)) ",
        11 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "__attribute__((sysv_abi)) " : "",
        12 => runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? "__vectorcall " : "__attribute__((vectorcall)) ",
        _ => throw new FormatException("Unsupported native callback calling convention."),
    };
}
