namespace Ankus.PreloadExtension;

/// <summary>
/// Checks ordinary native reset during actual nontransactional shared preload.
/// </summary>
internal static class PreloadCleanupProbe
{
    /// <summary>
    /// Runs one callback while its payload is live and retains only managed observations across backend startup.
    /// </summary>
    /// <returns>Transaction presence, callback count, payload, native result, pending registration and retained owner.</returns>
    internal static int[] Run()
    {
        using PgMemoryContext? transaction = PgMemoryContext.Get(PgMemoryContextKind.CurTransaction);
        using PgMemoryContext owner = PgMemoryContext.Create("preload callback control");
        using PgAllocation value = owner.Allocate(sizeof(int));
        value.Write(73);
        int calls = 0;
        int payload = 0;
        bool absent = false;
        using PgMemoryCallback callback = owner.RegisterResetCallback(() =>
        {
            calls++;
            payload = value.Read<int>();
            absent = MissingScanIsAbsent();
        });
        owner.Reset();
        return [transaction is null ? 0 : 1, calls, payload, absent ? 1 : 0,
            callback.IsPending ? 1 : 0, owner.IsAlive ? 1 : 0];
    }

    /// <summary>
    /// Executes a valid raw lookup in a callback with no transaction resources.
    /// </summary>
    /// <returns>Whether PostgreSQL permits the lookup and returns the independently chosen missing name.</returns>
    private static unsafe bool MissingScanIsAbsent()
    {
        fixed (byte* name = "ankus_unregistered_preload_scan\0"u8)
        {
            return Ankus.Postgres.NativeMethods.GetCustomScanMethods((sbyte*)name, true) == null;
        }
    }
}
