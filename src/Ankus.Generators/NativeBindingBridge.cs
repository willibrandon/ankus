using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Binds generated native values and calls to the extension's complete measured declaration companion.
/// </summary>
internal static class NativeBindingBridge
{
    /// <summary>
    /// Emits the unique companion identity visible to this extension's C# compilation.
    /// </summary>
    /// <param name="compilation">The extension and its resolved native declaration references.</param>
    /// <returns>A native constant, empty when no unique valid binding contract is present.</returns>
    internal static string Binding(Compilation compilation)
    {
        INamedTypeSymbol? binding = compilation.GetTypeByMetadataName("Ankus.Postgres.NativeBinding");
        string? identity = binding?.GetMembers("Identity").OfType<IFieldSymbol>().SingleOrDefault()?.ConstantValue as string;
        if (identity is null || identity.Length != 64 || !identity.All(static value => value is >= '0' and <= '9' or >= 'A' and <= 'F'))
        {
            identity = "";
        }

        return "static const char ankus_binding_identity[] = \"" + identity + "\";\n";
    }

    /// <summary>
    /// Validates the complete native declaration identity and server major beneath the shared native error guard.
    /// </summary>
    internal const string Source = """
        static void
        ankus_memory_native_binding(AnkusMemoryRequest *request)
        {
            if (sizeof(ankus_binding_identity) == 1)
            {
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("this extension has no unique generated PostgreSQL binding contract")));
            }

            if (request->value != PG_VERSION_NUM / 10000 ||
                request->length != sizeof(ankus_binding_identity) - 1 || request->data == 0 ||
                memcmp((const void *) request->data, ankus_binding_identity, sizeof(ankus_binding_identity) - 1) != 0)
            {
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("the generated binding does not match the active extension's PostgreSQL ABI")));
            }
        }

        """;
}
