using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Binds native node operations to the measured declaration companion selected for the extension.
/// </summary>
internal static class NativeNodeBridge
{
    /// <summary>
    /// Emits the exact measured identity visible to this extension's C# compilation.
    /// </summary>
    /// <param name="compilation">The extension and its resolved native declaration references.</param>
    /// <returns>A native constant, empty when the extension has no unique generated binding contract.</returns>
    internal static string Binding(Compilation compilation)
    {
        INamedTypeSymbol? binding = compilation.GetTypeByMetadataName("Ankus.Postgres.NativeBinding");
        string? identity = binding?.GetMembers("Identity").OfType<IFieldSymbol>().SingleOrDefault()?.ConstantValue as string;
        if (identity is null || identity.Length != 64 || !identity.All(static value => value is >= '0' and <= '9' or >= 'A' and <= 'F'))
        {
            identity = "";
        }

        return "static const char ankus_node_binding_identity[] = \"" + identity + "\";";
    }

    /// <summary>
    /// Validates a caller's binding identity and server major beneath the existing native memory error guard.
    /// </summary>
    internal const string Source = """
        static void
        ankus_memory_native_binding(AnkusMemoryRequest *request)
        {
            if (sizeof(ankus_node_binding_identity) == 1)
            {
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("this extension has no unique generated PostgreSQL binding contract")));
            }

            if (request->value != PG_VERSION_NUM / 10000 ||
                request->length != sizeof(ankus_node_binding_identity) - 1 || request->data == 0 ||
                memcmp((const void *) request->data, ankus_node_binding_identity, sizeof(ankus_node_binding_identity) - 1) != 0)
            {
                ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED),
                    errmsg("the node representation does not match the active extension's PostgreSQL binding ABI")));
            }
        }
        """;
}
