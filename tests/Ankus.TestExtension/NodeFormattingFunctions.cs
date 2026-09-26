using Ankus.Postgres;

namespace Ankus.TestExtension;

public static unsafe partial class NodeFunctions
{
    /// <summary>
    /// Allocates a real zeroed RangeTblRef, formats its root view and retains text after native reclamation.
    /// </summary>
    /// <param name="transfer">Whether individual ownership transfers to the native context.</param>
    /// <returns>The zeroed payload, exact output, original ownership and expired view.</returns>
    [PgFunction]
    public static string NodeAllocateAndFormat(bool transfer)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("allocated native node");
        using PgNativeBox<RangeTblRef> box = PgNodes.DangerousAllocate<RangeTblRef>((uint)NodeTag.T_RangeTblRef, owner);
        bool zeroed = new ReadOnlySpan<byte>((byte*)box.DangerousGetPointer() + sizeof(uint), sizeof(RangeTblRef) - sizeof(uint))
            .IndexOfAnyExcept((byte)0) == -1;
        RangeTblRef changed = box.Value;
        changed.rtindex = 9;
        box.Value = changed;
        PgNodeReference<Node> root = PgNodes.Borrow(box.Borrow()).TryCast<Node>()!;
        bool contextMatches = root.LifetimeContext.Id == owner.Id;
        if (transfer)
        {
            _ = box.ReleaseToContext();
        }

        string text = root.DangerousToNativeString();
        owner.Dispose();
        bool stale;
        try
        {
            _ = root.DangerousToNativeString();
            stale = false;
        }
        catch (ObjectDisposedException)
        {
            stale = true;
        }

        return $"{zeroed}|{contextMatches}|{text}|{stale}";
    }

    /// <summary>
    /// Formats an interior allocation view or explicitly bounded raw root without changing its original extent.
    /// </summary>
    /// <param name="raw">Whether the root borrows a raw address with an explicit complete extent.</param>
    /// <returns>The exact native RangeTblRef output.</returns>
    [PgFunction]
    public static string NodeFormatInterior(bool raw)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("interior native node");
        using PgAllocation storage = owner.AllocateZeroed(64);
        storage.Write(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 27 }, 8);
        PgNativeReference<Node> reference = raw
            ? owner.DangerousBorrow<Node>((byte*)storage.DangerousGetPointer() + 8, (nuint)sizeof(RangeTblRef))!
            : storage.Borrow<Node>(8);
        return PgNodes.Borrow(reference).DangerousToNativeString();
    }

    /// <summary>
    /// Supplies a valid tag prefix whose promised extent is one byte short of the complete concrete node.
    /// </summary>
    /// <param name="raw">Whether the insufficient extent comes from raw borrowing or a checked allocation.</param>
    /// <returns>Only returns if native concrete bounds validation incorrectly accepts incomplete storage.</returns>
    [PgFunction]
    public static string NodeFormatIncomplete(bool raw)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("incomplete native node");
        using PgAllocation storage = owner.AllocateZeroed((nuint)sizeof(RangeTblRef));
        storage.Write(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        PgNodeReference<Node> root;
        if (raw)
        {
            root = PgNodes.Borrow(owner.DangerousBorrow<Node>(storage.DangerousGetPointer(), (nuint)sizeof(RangeTblRef) - 1)!);
        }
        else
        {
            root = PgNodes.Borrow(storage.Borrow<Node>());
            storage.Reallocate((nuint)sizeof(RangeTblRef) - 1);
        }

        return root.DangerousToNativeString();
    }

    /// <summary>
    /// Formats an unknown tag using PostgreSQL's own warning and textual fallback semantics.
    /// </summary>
    /// <returns>The server's exact fallback text.</returns>
    [PgFunction]
    public static string NodeFormatUnknown()
    {
        using PgNativeBox<Node> box = PgNodes.DangerousAllocate<Node>(uint.MaxValue);
        return PgNodes.Borrow(box.Borrow()).DangerousToNativeString();
    }

    /// <summary>
    /// Formats a nested native expression or deliberately creates a valid-address cycle to exercise native error recovery.
    /// </summary>
    /// <param name="cycle">Whether the CollateExpr refers to itself.</param>
    /// <returns>The exact native nested expression output.</returns>
    [PgFunction]
    public static string NodeFormatNested(bool cycle)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("nested native node");
        using PgNativeBox<RangeTblRef> child = owner.CreateBox(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        using PgNativeBox<CollateExpr> parent = PgNodes.DangerousAllocate<CollateExpr>((uint)NodeTag.T_CollateExpr, owner);
        CollateExpr value = parent.Value;
        value.arg = cycle ? (nint)parent.DangerousGetPointer() : (nint)child.DangerousGetPointer();
        value.collOid = 123;
        value.location = -1;
        parent.Value = value;
        return PgNodes.Borrow(parent.Borrow()).TryCast<Node>()!.DangerousToNativeString();
    }

    /// <summary>
    /// Builds an Alias using explicitly server-encoded, terminated bytes and formats its native text.
    /// </summary>
    /// <param name="serverBytes">The alias name already encoded for the selected server database, or null for a null native name.</param>
    /// <returns>The native Alias text converted back into owned Unicode.</returns>
    [PgFunction]
    public static string NodeFormatAlias(byte[]? serverBytes)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("encoded native node");
        using PgAllocation? name = serverBytes is null ? null : owner.CopyFrom([.. serverBytes, (byte)0]);
        using PgNativeBox<Alias> alias = PgNodes.DangerousAllocate<Alias>((uint)NodeTag.T_Alias, owner);
        Alias value = alias.Value;
        value.aliasname = name is null ? 0 : (nint)name.DangerousGetPointer();
        alias.Value = value;
        return PgNodes.Borrow(alias.Borrow()).DangerousToNativeString();
    }

    /// <summary>
    /// Formats a native integer-list tag through its independently measured List representation.
    /// </summary>
    /// <returns>PostgreSQL's integer-list output.</returns>
    [PgFunction]
    public static string NodeFormatIntegerList()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("formatted native list");
        using PgList<int> list = PgList.Create<int>([11, -42, 0], owner);
        return PgNodes.Borrow(owner.DangerousBorrow<Node>(list.DangerousGetPointer(), (nuint)sizeof(Ankus.Postgres.List))!)
            .DangerousToNativeString();
    }

    /// <summary>
    /// Supplies an aligned Node prefix whose actual Alias representation needs a stricter alignment.
    /// </summary>
    /// <returns>Only returns if native concrete alignment validation incorrectly accepts the root.</returns>
    [PgFunction]
    public static string NodeFormatMisaligned()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("misaligned native node");
        using PgAllocation storage = owner.AllocateZeroed((nuint)sizeof(Alias) + 8, alignment: 8);
        storage.Write(new Alias { type = NodeTag.T_Alias }, 4);
        return PgNodes.Borrow(storage.Borrow<Node>(4)).DangerousToNativeString();
    }

    /// <summary>
    /// Keeps raw formatting bound to its original reset generation while the external bytes remain live.
    /// </summary>
    /// <param name="delete">Whether the lifetime anchor is deleted rather than reset.</param>
    /// <returns>Initial and fresh text plus invalidation of the retained raw reference.</returns>
    [PgFunction]
    public static string NodeFormatRawLifetime(bool delete)
    {
        using PgMemoryContext external = PgMemoryContext.Create("format external bytes");
        using PgMemoryContext anchor = PgMemoryContext.Create("format raw anchor");
        using PgNativeBox<RangeTblRef> box = external.CreateBox(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        PgNodeReference<RangeTblRef> old = PgNodes.Borrow(anchor.DangerousBorrow<RangeTblRef>(box.DangerousGetPointer())!);
        string initial = old.DangerousToNativeString();
        if (delete) { anchor.Dispose(); }
        else { anchor.Reset(); }

        string fresh = delete ? "deleted" : PgNodes.Borrow(anchor.DangerousBorrow<RangeTblRef>(box.DangerousGetPointer())!).DangerousToNativeString();
        bool stale;
        try
        {
            _ = old.DangerousToNativeString();
            stale = false;
        }
        catch (ObjectDisposedException)
        {
            stale = true;
        }

        return $"{initial}|{fresh}|{stale}|{box.Value.rtindex}";
    }
}
