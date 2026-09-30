namespace Ankus.Build;

/// <summary>
/// Separates backend header implementations from standalone probe implementations.
/// </summary>
internal static class NativeProbeHeaders
{
    /// <summary>
    /// Places Windows header definitions in sections which standalone probes never execute.
    /// </summary>
    /// <param name="headers">The original declarations, definitions and compiler assertions.</param>
    /// <returns>Unchanged headers with conditional section boundaries for standalone compilation.</returns>
    internal static string Wrap(string headers) => """
        #if defined(_WIN32) && defined(ANKUS_STANDALONE_PROBE)
        #include <stdio.h>
        #include <stdlib.h>
        #include <string.h>
        #pragma code_seg(push, "ankus_header_code")
        #pragma data_seg(push, "ankus_header_data")
        #pragma const_seg(push, "ankus_header_const")
        #pragma bss_seg(push, "ankus_header_bss")
        #endif

        """ + headers + "\n" + """
        #if defined(_WIN32) && defined(ANKUS_STANDALONE_PROBE)
        #pragma bss_seg(pop)
        #pragma const_seg(pop)
        #pragma data_seg(pop)
        #pragma code_seg(pop)
        #endif

        """;
}
