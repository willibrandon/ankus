namespace Ankus.TestExtension;

/// <summary>
/// Observes allocation and reclamation without copying the complete native payload into managed storage.
/// </summary>
public static class AllocationLifecycleFunctions
{
    private const string ContextName = "allocation lifecycle probe";

    /// <summary>
    /// Retains allocation policy, checked views, and endpoint values through growth, shrinkage, and release.
    /// </summary>
    /// <param name="initialSize">The first requested byte length.</param>
    /// <param name="grownSize">The larger requested byte length.</param>
    /// <param name="tryOperations">Whether allocation and both resizes use no-OOM operations.</param>
    /// <param name="requestedAlignment">The requested native alignment.</param>
    /// <param name="zeroed">Whether to clear the initial payload and the growth tail.</param>
    /// <returns>Independent stage observations, including native and catalog accounting before context deletion.</returns>
    [PgFunction]
    public static IEnumerable<(string Stage, long Length, int Options, int Alignment, bool Owner,
        long NativeBytes, long CatalogBytes, string Values, string View, string Zeroes, bool Aligned)> MemoryAllocationLifecycle(
        long initialSize, long grownSize, bool tryOperations, int requestedAlignment, bool zeroed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSize, 129);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(grownSize, initialSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(grownSize - initialSize, 1024 * 1024);
        nuint initialLength = checked((nuint)initialSize);
        nuint grownLength = checked((nuint)grownSize);
        nuint nativeAlignment = checked((nuint)requestedAlignment);
        PgAllocationOptions options = PgAllocationOptions.Huge | (zeroed ? PgAllocationOptions.Zeroed : PgAllocationOptions.None);
        var observations = new List<(string Stage, long Length, int Options, int Alignment, bool Owner,
            long NativeBytes, long CatalogBytes, string Values, string View, string Zeroes, bool Aligned)>();
        using PgMemoryContext owner = PgMemoryContext.Create(ContextName);
        observations.Add(("baseline", 0, 0, 0, true, NativeBytes(owner), CatalogBytes(), "", "", "", false));
        using PgAllocation allocation = tryOperations
            ? owner.TryAllocate(initialLength, options, nativeAlignment) ?? throw new InvalidOperationException("The admitted allocation returned null.")
            : owner.Allocate(initialLength, options, nativeAlignment);
        string initialZeroes = zeroed
            ? $"{allocation.Read<byte>()},{allocation.Read<byte>(127)},{allocation.Read<byte>(initialLength - 1)}"
            : "unchecked";
        allocation.Write<byte>(17);
        allocation.Write<byte>(73, 127);
        allocation.Write<byte>(231, initialLength - 1);
        PgNativeReference<byte> first = allocation.Borrow<byte>();
        PgNativeReference<byte> last = allocation.Borrow<byte>(initialLength - 1);
        observations.Add(("allocated", checked((long)allocation.Length), (int)allocation.Options, checked((int)allocation.Alignment),
            allocation.Context.Id == owner.Id, NativeBytes(owner), CatalogBytes(),
            $"{allocation.Read<byte>()},{allocation.Read<byte>(127)},{allocation.Read<byte>(initialLength - 1)}",
            $"{first.Value},{last.Value}", initialZeroes, IsAligned(allocation)));

        Resize(allocation, grownLength, tryOperations, zeroed);
        string grownZeroes = zeroed ? ReadZeroes(allocation, initialLength) : "unchecked";
        allocation.Write<byte>(99, initialLength);
        allocation.Write<byte>(142, grownLength - 1);
        observations.Add(("grown", checked((long)allocation.Length), (int)allocation.Options, checked((int)allocation.Alignment),
            allocation.Context.Id == owner.Id, NativeBytes(owner), CatalogBytes(),
            $"{allocation.Read<byte>()},{allocation.Read<byte>(127)},{allocation.Read<byte>(initialLength - 1)}," +
            $"{allocation.Read<byte>(initialLength)},{allocation.Read<byte>(grownLength - 1)}",
            $"{first.Value},{last.Value}", grownZeroes, IsAligned(allocation)));

        Resize(allocation, 128, tryOperations, zeroed);
        observations.Add(("shrunk", checked((long)allocation.Length), (int)allocation.Options, checked((int)allocation.Alignment),
            allocation.Context.Id == owner.Id, NativeBytes(owner), CatalogBytes(),
            $"{allocation.Read<byte>()},{allocation.Read<byte>(127)}", $"{first.Value},{Observe(last)}", "", IsAligned(allocation)));

        allocation.Dispose();
        string controlValue;
        using (PgAllocation control = owner.Allocate<int>())
        {
            control.Write(42);
            controlValue = control.Read<int>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        observations.Add(("freed", 0, 0, 0, owner.IsAlive, NativeBytes(owner), CatalogBytes(),
            controlValue, $"{Observe(first)},{Observe(last)}", "", false));
        owner.Dispose();
        observations.Add(("deleted", 0, 0, 0, owner.IsAlive, owner.IsAlive ? -1 : 0, CatalogBytes(),
            "", $"{Observe(first)},{Observe(last)}", "", false));
        return observations;
    }

    private static long NativeBytes(PgMemoryContext owner) => checked((long)owner.GetAllocatedBytes());

    private static unsafe bool IsAligned(PgAllocation allocation)
        => allocation.Alignment == 0 || (nuint)allocation.DangerousGetPointer() % allocation.Alignment == 0;

    private static long CatalogBytes()
        => Spi.ExecuteScalar<long>("SELECT coalesce(sum(total_bytes), 0)::bigint FROM pg_backend_memory_contexts WHERE ident = '" + ContextName + "'");

    private static void Resize(PgAllocation allocation, nuint length, bool tryOperation, bool zeroed)
    {
        if (tryOperation)
        {
            if (!allocation.TryReallocate(length, zeroed))
            {
                throw new InvalidOperationException("The admitted resize returned false.");
            }
        }
        else
        {
            allocation.Reallocate(length, zeroed);
        }
    }

    private static string ReadZeroes(PgAllocation allocation, nuint start)
    {
        Span<byte> buffer = stackalloc byte[4096];
        for (nuint offset = start; offset < allocation.Length; offset += (nuint)buffer.Length)
        {
            int count = checked((int)nuint.Min((nuint)buffer.Length, allocation.Length - offset));
            Span<byte> slice = buffer[..count];
            allocation.Read(slice, offset);
            if (slice.IndexOfAnyExcept((byte)0) >= 0)
            {
                return "nonzero";
            }
        }

        return "all-zero";
    }

    private static string Observe(PgNativeReference<byte> view)
    {
        try
        {
            return view.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "range";
        }
        catch (ObjectDisposedException)
        {
            return "stale";
        }
    }
}
