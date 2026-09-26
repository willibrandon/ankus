namespace Ankus.Generators;

/// <summary>
/// Executes generated native call bodies inside the shared callback error boundary.
/// </summary>
internal static class NativeRawCallBridge
{
    /// <summary>
    /// Gets the native storage frame and dispatch operation, with no managed callback between the call and its guard.
    /// </summary>
    internal const string Source = """
        typedef struct AnkusNativeCallArgument
        {
            const void *data;
            size_t size;
        } AnkusNativeCallArgument;

        typedef struct AnkusNativeCallFrame
        {
            const AnkusNativeCallArgument *arguments;
            size_t count;
            void *result;
            size_t result_size;
        } AnkusNativeCallFrame;

        typedef int (*AnkusNativeCallBody)(const AnkusNativeCallArgument *, size_t, void *, size_t);

        static void
        ankus_memory_native_call(AnkusMemoryRequest *request, AnkusMemoryResult *result)
        {
            if (request->pointer == 0 || request->data == 0)
            {
                ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE),
                    errmsg("the generated native call body or frame is missing")));
            }

            const AnkusNativeCallFrame *frame = (const AnkusNativeCallFrame *) request->data;
            AnkusNativeCallBody body = (AnkusNativeCallBody) request->pointer;
            result->value = body(frame->arguments, frame->count, frame->result, frame->result_size);
        }

        """;
}
