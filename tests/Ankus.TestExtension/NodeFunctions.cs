using Ankus.Postgres;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises generated native node casts and their original PostgreSQL storage lifetimes.
/// </summary>
public static unsafe class NodeFunctions
{
    /// <summary>
    /// Repeats pgrx's RangeTblRef roundtrip over owned or explicitly bounded raw native storage.
    /// </summary>
    /// <param name="raw">Whether the root is an explicitly bounded raw Node view.</param>
    /// <returns>The original and mutated index, shared address, preserved tag and owner identity.</returns>
    [PgFunction]
    public static string NodeRangeTableRoundtrip(bool raw)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("native node roundtrip");
        using PgNativeBox<RangeTblRef> value = owner.CreateBox(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        PgNodeReference<Node> root = raw
            ? PgNodes.Borrow(owner.DangerousBorrow<Node>(value.DangerousGetPointer(), (nuint)sizeof(RangeTblRef))!)
            : PgNodes.Borrow(value.Borrow()).TryCast<Node>()!;
        PgNodeReference<RangeTblRef> roundtrip = root.TryCast<RangeTblRef>()
            ?? throw new InvalidOperationException("A RangeTblRef rejected its own tag.");
        int original = roundtrip.Value.rtindex;
        RangeTblRef changed = roundtrip.Value;
        changed.rtindex = 73;
        roundtrip.Value = changed;
        bool shared = roundtrip.DangerousGetPointer() == value.DangerousGetPointer() && root.DangerousGetPointer() == value.DangerousGetPointer();
        bool tag = root.Tag == (uint)NodeTag.T_RangeTblRef && roundtrip.IsA((uint)NodeTag.T_RangeTblRef) && !root.IsA((uint)NodeTag.T_Var);
        return $"{original}|{value.Value.rtindex}|{shared}|{tag}|{roundtrip.LifetimeContext.Id == owner.Id}";
    }

    /// <summary>
    /// Repeats pgrx's node inheritance and unrelated-node rejection using a real PostgreSQL allocation.
    /// </summary>
    /// <returns>The shared addresses, preserved tags and rejected Var cast.</returns>
    [PgFunction]
    public static string NodeInheritance()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("native node inheritance");
        using PgNativeBox<AlternativeSubPlan> value = owner.CreateBox(new AlternativeSubPlan
        {
            xpr = new Expr { type = NodeTag.T_AlternativeSubPlan },
            subplans = 0,
        });
        PgNodeReference<AlternativeSubPlan> leaf = PgNodes.Borrow(value.Borrow());
        PgNodeReference<Expr> parent = leaf.TryCast<Expr>()!;
        PgNodeReference<Node> root = parent.TryCast<Node>()!;
        PgNodeReference<AlternativeSubPlan> roundtrip = root.TryCast<Expr>()!.TryCast<AlternativeSubPlan>()!;
        bool sameAddress = leaf.DangerousGetPointer() == parent.DangerousGetPointer() &&
            leaf.DangerousGetPointer() == root.DangerousGetPointer() && leaf.DangerousGetPointer() == roundtrip.DangerousGetPointer();
        bool tags = leaf.Tag == (uint)NodeTag.T_AlternativeSubPlan && parent.Tag == leaf.Tag && root.Tag == leaf.Tag && roundtrip.Tag == leaf.Tag;
        return $"{sameAddress}|{tags}|{root.TryCast<Var>() is null}|{roundtrip.Value.subplans == 0}";
    }

    /// <summary>
    /// Native OpExpr aliases retain their distinct tags while sharing their declared representation.
    /// </summary>
    /// <param name="nullIf">Whether to use NullIfExpr rather than DistinctExpr.</param>
    /// <returns>Exact tag identity, the shared operation OID and rejection of an unrelated Var.</returns>
    [PgFunction]
    public static string NodeAlias(bool nullIf)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("native node alias");
        NodeTag tag = nullIf ? NodeTag.T_NullIfExpr : NodeTag.T_DistinctExpr;
        using PgNativeBox<OpExpr> value = owner.CreateBox(new OpExpr { xpr = new Expr { type = tag }, opno = 711 });
        PgNodeReference<OpExpr> leaf = PgNodes.Borrow(value.Borrow());
        PgNodeReference<Node> root = leaf.TryCast<Node>()!;
        PgNodeReference<OpExpr> alias = root.TryCast<OpExpr>()!;
        return $"{alias.Tag == (uint)tag}|{alias.Value.opno}|{alias.DangerousGetPointer() == value.DangerousGetPointer()}|{root.TryCast<Var>() is null}";
    }

    /// <summary>
    /// Attempts a compatible-sized representation with a deliberately different measured ABI identity.
    /// </summary>
    /// <returns>The value only if native ABI validation incorrectly accepts the incompatible contract.</returns>
    [PgFunction]
    public static uint NodeIncompatibleBinding()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("native node incompatible binding");
        ForeignNode value = new() { _tag = (uint)NodeTag.T_RangeTblRef, _index = 9 };
        return PgNodes.Borrow(owner.DangerousBorrow<ForeignNode>(&value)!).Tag;
    }

    /// <summary>
    /// Retains original cast bounds through native growth, shrink, regrowth and explicit release.
    /// </summary>
    /// <returns>The preserved values, complete-target rejection and release invalidation.</returns>
    [PgFunction]
    public static string NodeAllocationBounds()
    {
        using PgMemoryContext owner = PgMemoryContext.Create("native node bounds");
        using PgAllocation allocation = owner.Allocate<RangeTblRef>();
        allocation.Write(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        PgNodeReference<RangeTblRef> leaf = PgNodes.Borrow(allocation.Borrow<RangeTblRef>());
        PgNodeReference<Node> root = leaf.TryCast<Node>()!;
        allocation.Reallocate((nuint)sizeof(RangeTblRef) * 8, zeroNewMemory: true);
        int grown = root.TryCast<RangeTblRef>()!.Value.rtindex;
        bool address = root.DangerousGetPointer() == allocation.DangerousGetPointer();
        allocation.Reallocate((nuint)sizeof(Node));
        bool targetRejected = false;
        try
        {
            _ = root.TryCast<RangeTblRef>();
        }
        catch (InvalidCastException)
        {
            targetRejected = true;
        }

        bool sourceRejected = false;
        try
        {
            _ = leaf.TryCast<Node>();
        }
        catch (ArgumentOutOfRangeException)
        {
            sourceRejected = true;
        }

        allocation.Reallocate((nuint)sizeof(RangeTblRef), zeroNewMemory: true);
        int regrown = root.TryCast<RangeTblRef>()!.Value.rtindex;
        allocation.Dispose();
        return $"{grown}|{address}|{targetRejected}|{sourceRejected}|{regrown}|{NodeIsStale(() => root.Tag)}|{NodeIsStale(() => leaf.Tag)}";
    }

    /// <summary>
    /// Raw casts retain their initial reset generation independently of a still-live external byte owner.
    /// </summary>
    /// <param name="delete">Whether the anchor is deleted rather than reset.</param>
    /// <returns>Stale original and cast views, the untouched external value and the fresh borrow result.</returns>
    [PgFunction]
    public static string NodeRawAnchor(bool delete)
    {
        using PgMemoryContext external = PgMemoryContext.Create("native node external storage");
        using PgMemoryContext anchor = PgMemoryContext.Create("native node raw anchor");
        using PgNativeBox<RangeTblRef> value = external.CreateBox(new RangeTblRef { type = NodeTag.T_RangeTblRef, rtindex = 9 });
        PgNodeReference<RangeTblRef> leaf = PgNodes.Borrow(anchor.DangerousBorrow<RangeTblRef>(value.DangerousGetPointer())!);
        PgNodeReference<Node> root = leaf.TryCast<Node>()!;
        if (delete)
        {
            anchor.Dispose();
        }
        else
        {
            anchor.ResetOnly();
        }

        bool staleCast = false;
        try
        {
            _ = root.TryCast<RangeTblRef>();
        }
        catch (ObjectDisposedException)
        {
            staleCast = true;
        }

        int fresh = delete ? -1 : PgNodes.Borrow(anchor.DangerousBorrow<RangeTblRef>(value.DangerousGetPointer())!).Value.rtindex;
        return $"{NodeIsStale(() => leaf.Tag)}|{NodeIsStale(() => root.Tag)}|{staleCast}|{value.Value.rtindex}|{fresh}|{NodeIsStale(() => root.Tag)}";
    }

    private static bool NodeIsStale(Func<uint> read)
    {
        try
        {
            _ = read();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static int BindingMajor<T>() where T : unmanaged, IPgNativeType => T.PostgresMajor;

    /// <summary>
    /// Supplies independent native bytes with an incompatible declaration identity to exercise the real native rejection path.
    /// </summary>
    private struct ForeignNode : IPgNativeNode
    {
        /// <summary>
        /// Carries a real native tag without promising a matching ABI.
        /// </summary>
        internal uint _tag;

        /// <summary>
        /// Carries initialized bytes matching the concrete node's size.
        /// </summary>
        internal int _index;

        static int IPgNativeType.PostgresMajor => BindingMajor<RangeTblRef>();
        static string IPgNativeType.AbiIdentity => "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        static string IPgNativeType.RuntimeIdentifier => NativeBinding.RuntimeIdentifier;
        static int IPgNativeType.NativeSize => sizeof(ForeignNode);
        static int IPgNativeType.NativeAlignment => sizeof(uint);
        static bool IPgNativeNode.AcceptsTag(uint tag) => tag == (uint)NodeTag.T_RangeTblRef;
    }
}
