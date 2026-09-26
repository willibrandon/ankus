using System.Globalization;
using System.Text;
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

        string? layouts = binding?.GetMembers("NodeLayouts").OfType<IFieldSymbol>().SingleOrDefault()?.ConstantValue as string;
        return "#include \"nodes/nodes.h\"\nstatic const char ankus_node_binding_identity[] = \"" + identity + "\";\n" + Layouts(layouts);
    }

    private static string Layouts(string? encoded)
    {
        var cases = new StringBuilder();
        var tags = new HashSet<uint>();
        bool valid = !string.IsNullOrEmpty(encoded);
        foreach (string record in (encoded ?? "").Split(';'))
        {
            string[] fields = record.Split(':');
            if (fields.Length != 3 || !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint tag) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size < 4 ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int alignment) || alignment <= 0 ||
                (alignment & (alignment - 1)) != 0 || size % alignment != 0 || !tags.Add(tag))
            {
                valid = false;
                break;
            }

            cases.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        case {0}U: *size = {1}; *alignment = {2}; return;", tag, size, alignment));
        }

        return "static void ankus_node_layout(uint32 tag, Size *size, Size *alignment)\n{\n" +
            (valid ? "    switch (tag)\n    {\n" + cases +
                "        default: *size = sizeof(Node); *alignment = sizeof(NodeTag); return;\n    }\n" :
                "    ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg(\"this extension has no measured node layout contract\")));\n") + "}\n";
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

        static void
        ankus_node_free_text(void *text)
        {
            free(text);
        }

        static void
        ankus_memory_format_node(AnkusMemoryRequest *request)
        {
            void *address;
            Size available;
            AnkusMemoryContext *owner;
            if (request->pointer == 0)
            {
                AnkusMemoryAllocation *allocation = ankus_memory_allocation_by_id((uint64) request->context);
                if (allocation == NULL)
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE), errmsg("the node allocation is stale")));
                }

                uint64 offset = (uint64) request->value;
                if (offset > allocation->size || request->length > allocation->size - offset)
                {
                    ereport(ERROR, (errcode(ERRCODE_DATA_EXCEPTION), errmsg("the node range is outside its allocation")));
                }

                address = (char *) allocation->pointer + offset;
                available = (Size) request->length;
                owner = ankus_memory_context_by_id(allocation->context_id);
            }
            else
            {
                owner = ankus_memory_context_from_request(request);
                if (owner->generation == 0 || owner->generation != (uintptr_t) request->other)
                {
                    ereport(ERROR, (errcode(ERRCODE_OBJECT_NOT_IN_PREREQUISITE_STATE), errmsg("the node lifetime anchor is stale")));
                }

                address = (void *) request->pointer;
                available = (Size) request->length;
            }

            if (available < sizeof(NodeTag) || request->data == 0)
            {
                ereport(ERROR, (errcode(ERRCODE_DATA_EXCEPTION), errmsg("the node header or output buffer is incomplete")));
            }

            uint32 tag;
            Size size;
            Size alignment;
            memcpy(&tag, address, sizeof(tag));
            ankus_node_layout(tag, &size, &alignment);
            if (available < size || (uintptr_t) address % alignment != 0)
            {
                ereport(ERROR, (errcode(ERRCODE_DATA_EXCEPTION), errmsg("the node storage does not satisfy its concrete tag layout")));
            }

            AnkusValue *output = (AnkusValue *) request->data;
            MemoryContext caller = CurrentMemoryContext;
            MemoryContext temporary = AllocSetContextCreate(caller, "Ankus node formatting", ALLOCSET_SMALL_SIZES);
            AnkusMemoryProtection scope = {0};
            ankus_memory_protect(&scope, owner->context, false);
            PG_TRY();
            {
                MemoryContextSwitchTo(temporary);
                char *native = nodeToString(address);
                char *utf8 = pg_server_to_any(native, strlen(native), PG_UTF8);
                Size length = strlen(utf8);
                pg_verify_mbstr(PG_UTF8, utf8, length, false);
                if (length > INT_MAX)
                {
                    ereport(ERROR, (errcode(ERRCODE_PROGRAM_LIMIT_EXCEEDED), errmsg("the node representation exceeds the managed string limit")));
                }

                output->data = malloc(length + 1);
                if (output->data == NULL)
                {
                    ereport(ERROR, (errcode(ERRCODE_OUT_OF_MEMORY), errmsg("unable to copy the node representation")));
                }

                output->release = ankus_node_free_text;
                memcpy(output->data, utf8, length + 1);
                output->length = (int) length;
            }
            PG_FINALLY();
            {
                MemoryContextSwitchTo(caller);
                ankus_memory_protection = scope.previous;
                MemoryContextDelete(temporary);
            }
            PG_END_TRY();
        }
        """;
}
