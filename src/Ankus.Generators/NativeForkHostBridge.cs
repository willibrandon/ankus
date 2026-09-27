namespace Ankus.Generators;

/// <summary>
/// Shares runtime host entry between configuration hooks and statically declared native callbacks.
/// </summary>
internal static class NativeForkHostBridge
{
    /// <summary>
    /// Resumes a dormant postmaster runtime only while a managed callback is executing.
    /// </summary>
    internal const string Source = """
        #ifndef WIN32
        extern int RhEnterForkHost(void);
        extern int RhExitForkHost(void);

        static void
        ankus_fork_host_enter(void)
        {
            int status = RhEnterForkHost();
            if (status != 1)
            {
                ereport(FATAL, (errcode(ERRCODE_INTERNAL_ERROR),
                    errmsg("Ankus runtime host entry failed: %d", status)));
            }
        }

        static void
        ankus_fork_host_exit(void)
        {
            int status = RhExitForkHost();
            if (status != 1)
            {
                ereport(FATAL, (errcode(ERRCODE_INTERNAL_ERROR),
                    errmsg("Ankus runtime host exit failed: %d", status)));
            }
        }
        #else
        static void
        ankus_fork_host_enter(void)
        {
        }

        static void
        ankus_fork_host_exit(void)
        {
        }
        #endif
        """;
}
