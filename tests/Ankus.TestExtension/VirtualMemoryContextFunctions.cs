using System.Collections;
using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Observes injected context ownership through scalar calls, iterators, and executor cleanup.
/// </summary>
[PgSchema("virtual_memory")]
public static unsafe class VirtualMemoryContextFunctions
{
    private static readonly Dictionary<int, ProbeState> s_states = [];
    private static PgMemoryContext? s_innerContext;
    private static PgContextValue<int>? s_innerValue;
    private static PgMemoryContext? s_scalarContext;
    private static PgContextValue<int>? s_scalarValue;
    private static PgNativeReference<int>? s_scalarRaw;
    private static PgMemoryContext? s_controlContext;
    private static PgContextValue<int>? s_controlValue;

    /// <summary>
    /// Places virtual contexts around distinct SQL inputs and independently consumes one borrowed wrapper.
    /// </summary>
    /// <param name="first">The first injected wrapper.</param>
    /// <param name="left">The integer SQL input.</param>
    /// <param name="middle">The nullable-annotated injected wrapper.</param>
    /// <param name="text">The nullable text SQL input.</param>
    /// <param name="right">The bigint SQL input.</param>
    /// <param name="last">The final injected wrapper.</param>
    /// <returns>The exact inputs and independent native identity and wrapper-lifetime observations.</returns>
    [PgFunction]
    public static string VmScalar(PgMemoryContext first, [PgParameter(Name = "input_left")] int left, PgMemoryContext? middle, string? text, long right, PgMemoryContext last)
    {
        ArgumentNullException.ThrowIfNull(middle);
        PgContextValue<int> value = first.CreateContextValue(left);
        bool same = first.Id == middle.Id && middle.Id == last.Id;
        bool current = first.Id == PgMemoryContext.Current.Id;
        bool owner = value.Context.Id == first.Id;
        first.Dispose();
        bool independent = !first.IsAlive && middle.IsAlive && last.IsAlive && value.Value == left && PgMemoryContext.Current.Id == last.Id;
        return $"{left}|{text ?? "null"}|{right}|{same}|{current}|{owner}|{independent}";
    }

    /// <summary>
    /// Distinguishes a real SQL default from an optional virtual null default.
    /// </summary>
    /// <param name="value">The real SQL value and default.</param>
    /// <param name="context">The optional injected dependency, always supplied by SQL dispatch.</param>
    /// <returns>The value and actual injected current identity.</returns>
    [PgFunction]
    public static string VmDefaults(int value = 17, PgMemoryContext? context = null)
        => $"{value}|{context is not null}|{context?.Id == PgMemoryContext.Current.Id}";

    /// <summary>
    /// Receives SQL NULL despite its non-nullable virtual dependency.
    /// </summary>
    /// <param name="value">The nullable real SQL argument.</param>
    /// <param name="context">The injected current context.</param>
    /// <returns>The input and injected identity.</returns>
    [PgFunction(NullInput = PgNullInput.CalledOnNull)]
    public static string VmNullable(int? value, PgMemoryContext context)
        => $"{value?.ToString(CultureInfo.InvariantCulture) ?? "null"}|{context.Id == PgMemoryContext.Current.Id}";

    /// <summary>
    /// Supplies multiple context dependencies without declaring any SQL arguments.
    /// </summary>
    /// <param name="first">The required context dependency.</param>
    /// <param name="second">The optional-annotated context dependency.</param>
    /// <returns>Whether both non-null contexts identify the native current context.</returns>
    [PgFunction]
    public static bool VmOnly(PgMemoryContext first, PgMemoryContext? second = null)
        => second is not null && first.Id == second.Id && second.Id == PgMemoryContext.Current.Id;

    /// <summary>
    /// Retains variadic SQL ordinals and NULL elements after a virtual argument.
    /// </summary>
    /// <param name="context">The injected context.</param>
    /// <param name="factor">The fixed real SQL input.</param>
    /// <param name="values">The variadic nullable integers.</param>
    /// <returns>The fixed and variadic values with their native owner witness.</returns>
    [PgFunction]
    public static string VmVariadic(PgMemoryContext context, int factor, params int?[] values)
        => $"{factor}|{string.Join(',', values.Select(static value => value?.ToString(CultureInfo.InvariantCulture) ?? "null"))}|" +
            $"{context.CreateContextValue(factor).Context.Id == context.Id}";

    /// <summary>
    /// Exposes an operator with virtual dependencies surrounding both SQL operands.
    /// </summary>
    /// <param name="first">The first context dependency.</param>
    /// <param name="left">The integer operand.</param>
    /// <param name="middle">The middle context dependency.</param>
    /// <param name="right">The bigint operand.</param>
    /// <param name="last">The final context dependency.</param>
    /// <returns>The exact operand-order witness.</returns>
    [PgOperator("#@#")]
    public static long VmOperator(PgMemoryContext first, int left, PgMemoryContext middle, long right, PgMemoryContext last)
    {
        RequireContexts(first, middle, last);
        return first.CreateContextValue(checked(left * 1000L - right)).Value;
    }

    /// <summary>
    /// Isolates the virtual-parameter cast from PostgreSQL's built-in coercions.
    /// </summary>
    [PgEnum(Name = "vm_token")]
    public enum VmToken
    {
        /// <summary>
        /// Contributes a nonzero value to the cast metadata witness.
        /// </summary>
        Value = 7,
    }

    /// <summary>
    /// Keeps source, typmod, and explicit-cast slots correct around injected contexts.
    /// </summary>
    /// <param name="first">The first virtual dependency.</param>
    /// <param name="value">The actual cast source.</param>
    /// <param name="middle">The middle virtual dependency.</param>
    /// <param name="typmod">PostgreSQL's target type modifier.</param>
    /// <param name="isExplicit">Whether the cast was explicitly requested.</param>
    /// <param name="last">The final virtual dependency.</param>
    /// <returns>The exact source and metadata witness.</returns>
    [PgCast]
    public static PgNumeric VmCast(PgMemoryContext first, VmToken value, PgMemoryContext middle, int typmod, bool isExplicit, PgMemoryContext last)
    {
        RequireContexts(first, middle, last);
        return PgNumeric.FromInteger(first.CreateContextValue(checked((long)typmod * 10 + (isExplicit ? 1 : 0) + (int)value)).Value);
    }

    /// <summary>
    /// Saves an inner scalar context before returning or reporting a managed or guarded native error.
    /// </summary>
    /// <param name="failure">Success, managed PgException, or native division-by-zero.</param>
    /// <param name="context">The inner callback's injected context.</param>
    /// <returns>The independent current identity witness.</returns>
    [PgFunction]
    public static bool VmInner(int failure, PgMemoryContext context)
    {
        s_innerContext = context;
        s_innerValue = context.CreateContextValue(73);
        if (failure == 1)
        {
            throw Failure(1);
        }

        if (failure == 2)
        {
            Spi.ExecuteScalar<int>("SELECT 1 / 0");
        }

        return context.Id == PgMemoryContext.Current.Id && s_innerValue.Context.Id == context.Id;
    }

    /// <summary>
    /// Preserves the injected snapshot and outer current context through nested SPI and managed scope failures.
    /// </summary>
    /// <param name="failure">Success, managed inner error, native inner error, or a local managed scope error.</param>
    /// <param name="context">The outer injected snapshot.</param>
    /// <returns>The exact error, nested identity separation, restoration, retained bytes, and inner expiry.</returns>
    [PgFunction]
    public static string VmNested(int failure, PgMemoryContext context)
    {
        PgContextValue<int> outer = context.CreateContextValue(17);
        using PgMemoryContext selected = PgMemoryContext.Create("virtual context nested", context);
        bool innerDifferent = false;
        bool innerRestored = false;
        string outcome = "none";
        try
        {
            selected.Run(() =>
            {
                try
                {
                    outcome = Spi.ExecuteScalar<bool>($"SELECT virtual_memory.vm_inner({(failure == 3 ? 0 : failure)})").ToString();
                }
                catch (PgException error)
                {
                    outcome = $"{error.SqlState}:{error.Message}:{error.Detail}:{error.Hint}";
                }

                innerDifferent = s_innerContext?.Id != selected.Id && selected.Id != context.Id;
                innerRestored = PgMemoryContext.Current.Id == selected.Id;
                if (failure == 3)
                {
                    throw new InvalidOperationException("local scope failure");
                }
            });
        }
        catch (InvalidOperationException error) when (failure == 3)
        {
            outcome = error.Message;
        }

        return $"{outcome}|{innerDifferent}|{innerRestored}|{PgMemoryContext.Current.Id == context.Id}|{outer.Value}|" +
            $"{s_innerContext?.IsAlive}|{ReadOrStale(() => s_innerValue!.Value)}|{Spi.ExecuteScalar<int>("SELECT 42")}";
    }

    /// <summary>
    /// Retains a scalar callback's native storage for a later executor-lifetime check.
    /// </summary>
    /// <param name="context">The scalar's injected context.</param>
    /// <returns>The actual current and allocation-owner identity.</returns>
    [PgFunction]
    public static bool VmSaveScalar(PgMemoryContext context)
    {
        s_scalarContext = context;
        s_scalarValue = context.CreateContextValue(91);
        s_scalarRaw = context.DangerousBorrow<int>(s_scalarValue.DangerousGetPointer());
        return context.Id == PgMemoryContext.Current.Id && s_scalarValue.Context.Id == context.Id;
    }

    /// <summary>
    /// Reports saved scalar handle expiry from a separate SQL callback.
    /// </summary>
    /// <returns>The scalar owner, tracked value, and raw view lifetimes.</returns>
    [PgFunction]
    public static string VmScalarState()
        => $"{s_scalarContext?.IsAlive}|{ReadOrStale(() => s_scalarValue!.Value)}|{ReadOrStale(() => s_scalarRaw!.Value)}";

    /// <summary>
    /// Creates a streaming iterator with native storage retained from its factory.
    /// </summary>
    /// <param name="key">The independent iterator identity and value seed.</param>
    /// <param name="context">The injected multi-call context.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="failure">The optional failing lifecycle stage.</param>
    /// <returns>The retained iterator.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<int> VmStreaming(int key, PgMemoryContext context, int count, int failure)
        => CreateProbe(context, key, count, failure, static value => value);

    /// <summary>
    /// Materializes rows while preserving factory-owned native data independently of row scratch.
    /// </summary>
    /// <param name="context">The injected multi-call context.</param>
    /// <param name="key">The independent iterator identity and value seed.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="failure">The optional failing lifecycle stage.</param>
    /// <returns>The retained iterator.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<int> VmMaterialized(PgMemoryContext context, int key, int count, int failure)
        => CreateProbe(context, key, count, failure, static value => value);

    /// <summary>
    /// Uses an injected parameter from a deferred compiler-generated iterator body.
    /// </summary>
    /// <param name="key">The iterator's value seed.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="context">The snapshot captured before the iterator body executes.</param>
    /// <returns>The rows backed by that captured snapshot.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<int> VmDeferred(int key, int count, PgMemoryContext context)
    {
        ProbeState state = CreateState(context, key, count, 0);
        try
        {
            while (Advance(state))
            {
                yield return state.Value.Value;
            }
        }
        finally
        {
            Cleanup(state);
        }
    }

    /// <summary>
    /// Suspends a streaming enum iterator before PostgreSQL performs native label lookup.
    /// </summary>
    /// <param name="key">The independent iterator identity.</param>
    /// <param name="context">The injected owner.</param>
    /// <returns>Labels whose native lookup can fail after yielding.</returns>
    [PgFunction(SetMode = PgSetMode.ValuePerCall)]
    public static IEnumerable<EnumMood> VmEnumStreaming(int key, PgMemoryContext context)
        => CreateProbe(context, key, 3, 0, static _ => EnumMood.Low);

    /// <summary>
    /// Exercises the same native conversion error while materializing rows.
    /// </summary>
    /// <param name="context">The injected owner.</param>
    /// <param name="key">The independent iterator identity.</param>
    /// <returns>Labels whose native lookup can fail after yielding.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<EnumMood> VmEnumMaterialized(PgMemoryContext context, int key)
        => CreateProbe(context, key, 3, 0, static _ => EnumMood.Low);

    /// <summary>
    /// Produces moderate-sized text rows while sampling scratch allocation and retaining a small owner payload.
    /// </summary>
    /// <param name="key">The independent iterator identity.</param>
    /// <param name="context">The injected owner.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="width">The exact text width.</param>
    /// <returns>Exact row identifiers and independent text payloads.</returns>
    [PgFunction(SetMode = PgSetMode.Materialize)]
    public static IEnumerable<(int RowId, string Payload)> VmBounded(int key, PgMemoryContext context, int count, int width)
        => CreateProbe(context, key, count, 0, value => (value, value.ToString(CultureInfo.InvariantCulture).PadRight(width, 'x')));

    /// <summary>
    /// Reports the exact lifecycle observations and current checked lifetimes of one iterator.
    /// </summary>
    /// <param name="key">The saved iterator identity.</param>
    /// <returns>The factory, row, cleanup, and lifetime observations.</returns>
    [PgFunction]
    public static string VmState(int key)
    {
        ProbeState state = s_states[key];
        return $"{state.OwnerName == "SRF multi-call context"},{state.FactoryCurrent},{state.ActualOwner}|" +
            $"{state.Enumerators},{state.EnumeratorCurrent}|{state.Moves},{state.Rows},{state.ExpiredScratch}|{state.Disposals}|{state.Cleanup}|" +
            $"{state.Owner.IsAlive},{ReadOrStale(() => state.Value.Value)},{ReadOrStale(() => state.Raw.Value)}";
    }

    /// <summary>
    /// Compares native owners captured by two simultaneously suspended iterators.
    /// </summary>
    /// <param name="left">The first iterator identity.</param>
    /// <param name="right">The second iterator identity.</param>
    /// <returns>Whether the native owners are distinct and alive.</returns>
    [PgFunction]
    public static bool VmDistinct(int left, int right)
        => s_states[left].Owner.Id != s_states[right].Owner.Id && s_states[left].Owner.IsAlive && s_states[right].Owner.IsAlive;

    /// <summary>
    /// Reads the maximum materialized scratch size observed before row conversion.
    /// </summary>
    /// <param name="key">The iterator identity.</param>
    /// <returns>The maximum observed native scratch byte count.</returns>
    [PgFunction]
    public static long VmMaximumScratch(int key) => checked((long)s_states[key].MaximumScratch);

    /// <summary>
    /// Probes reserved-owner protection between cursor fetches and exact child-only reset semantics.
    /// </summary>
    /// <param name="key">The suspended iterator identity.</param>
    /// <param name="operation">Owner reset, owner reset-only, parent reset, parent reset-children, parent reset-only, or owner reset-children.</param>
    /// <returns>The native rejection or allowed child reset, unchanged owner bytes, and exact child lifetime.</returns>
    [PgFunction]
    public static string VmGuard(int key, int operation)
    {
        ProbeState state = s_states[key];
        PgMemoryContext parent = state.Owner.Parent ?? throw new InvalidOperationException("The set owner has no parent.");
        using PgMemoryContext child = PgMemoryContext.Create("virtual context reserved child", state.Owner);
        PgContextValue<int> childValue = child.CreateContextValue(51);
        int before = state.Value.Value;
        string result = "allowed";
        try
        {
            switch (operation)
            {
                case 0: state.Owner.Reset(); break;
                case 1: state.Owner.ResetOnly(); break;
                case 2: parent.Reset(); break;
                case 3: parent.ResetChildren(); break;
                case 4: parent.ResetOnly(); break;
                case 5: state.Owner.ResetChildren(); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        catch (PgException error)
        {
            result = error.SqlState + ":" + error.Message;
        }

        return $"{result}|{before},{state.Value.Value},{state.Raw.Value}|{state.Owner.IsAlive}|{child.IsAlive},{ReadOrStale(() => childValue.Value)}";
    }

    /// <summary>
    /// Keeps a control allocation under the top transaction while a nested savepoint is rolled back.
    /// </summary>
    /// <param name="create">Whether to create a new control value.</param>
    /// <returns>The live control value or stale marker.</returns>
    [PgFunction]
    public static string VmControl(bool create)
    {
        if (create)
        {
            s_controlContext?.Dispose();
            PgMemoryContext parent = PgMemoryContext.Get(PgMemoryContextKind.TopTransaction) ?? throw new InvalidOperationException("No top transaction.");
            s_controlContext = PgMemoryContext.Create("virtual context transaction control", parent);
            s_controlValue = s_controlContext.CreateContextValue(41);
        }

        return ReadOrStale(() => s_controlValue!.Value);
    }

    private static void RequireContexts(PgMemoryContext first, PgMemoryContext middle, PgMemoryContext last)
    {
        if (first.Id != middle.Id || middle.Id != last.Id || last.Id != PgMemoryContext.Current.Id)
        {
            throw new InvalidOperationException("Virtual operator or cast context identity changed.");
        }
    }

    private static PgException Failure(int stage)
        => new("P760" + stage.ToString(CultureInfo.InvariantCulture), "virtual context failure " + stage.ToString(CultureInfo.InvariantCulture),
            "virtual detail café", "virtual hint");

    private static string ReadOrStale(Func<int> read)
    {
        try
        {
            return read().ToString(CultureInfo.InvariantCulture);
        }
        catch (ObjectDisposedException)
        {
            return "stale";
        }
    }

    private static ProbeState CreateState(PgMemoryContext owner, int key, int count, int failure)
    {
        PgContextValue<int> value = owner.CreateContextValue(checked(key * 100));
        PgNativeReference<int> raw = owner.DangerousBorrow<int>(value.DangerousGetPointer()) ?? throw new InvalidOperationException("Missing raw owner payload.");
        var state = new ProbeState(owner, value, raw, count, failure)
        {
            FactoryCurrent = PgMemoryContext.Current.Id == owner.Id,
            ActualOwner = value.Context.Id == owner.Id,
            OwnerName = owner.Name,
        };
        s_states[key] = state;
        _ = PgMemoryContext.Create("virtual context iterator marker", owner);
        return state;
    }

    private static ProbeEnumerable<T> CreateProbe<T>(PgMemoryContext owner, int key, int count, int failure, Func<int, T> project)
    {
        ProbeState state = CreateState(owner, key, count, failure);
        if (failure == 1)
        {
            throw Failure(1);
        }

        return new ProbeEnumerable<T>(state, project);
    }

    private static bool Advance(ProbeState state)
    {
        state.Moves++;
        if (state.Scratch is not null)
        {
            string previous = ReadOrStale(() => state.Scratch.Value);
            if (previous != "stale")
            {
                throw new InvalidOperationException("A prior row's scratch allocation remained live: " + previous);
            }

            state.ExpiredScratch++;
        }

        if (state.Failure is 3 or 5 && state.Rows == 1)
        {
            throw Failure(3);
        }

        if (state.Rows == state.Count)
        {
            return false;
        }

        PgMemoryContext current = PgMemoryContext.Current;
        if (current.Id == state.Owner.Id)
        {
            throw new InvalidOperationException("Row scratch unexpectedly uses the retained set owner.");
        }

        state.Scratch = current.CreateContextValue(37);
        nuint bytes = current.GetAllocatedBytes();
        if (bytes > state.MaximumScratch)
        {
            state.MaximumScratch = bytes;
        }

        state.Rows++;
        state.Value.Value++;
        if (state.Raw.Value != state.Value.Value || state.Value.Context.Id != state.Owner.Id)
        {
            throw new InvalidOperationException("The retained iterator payload lost its owner or alias.");
        }

        return true;
    }

    private static void Cleanup(ProbeState state)
    {
        state.Disposals++;
        string sql;
        string payload = $"{state.Owner.IsAlive},{state.Value.Value},{state.Raw.Value}";
        try
        {
            sql = Spi.ExecuteScalar<int>("SELECT 42").ToString(CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            sql = "denied";
        }

        state.Cleanup = payload + "," + sql;
        if (state.Failure is 4 or 5)
        {
            throw Failure(4);
        }
    }

    /// <summary>
    /// Holds copied observations and checked handles across native iterator callbacks.
    /// </summary>
    /// <param name="owner">The injected native owner.</param>
    /// <param name="value">The direct owner payload.</param>
    /// <param name="raw">The reset-sensitive raw alias.</param>
    /// <param name="count">The requested row count.</param>
    /// <param name="failure">The requested failing lifecycle stage.</param>
    private sealed class ProbeState(PgMemoryContext owner, PgContextValue<int> value, PgNativeReference<int> raw, int count, int failure)
    {
        /// <summary>
        /// Gets the injected native owner.
        /// </summary>
        internal PgMemoryContext Owner { get; } = owner;

        /// <summary>
        /// Gets the checked direct owner payload.
        /// </summary>
        internal PgContextValue<int> Value { get; } = value;

        /// <summary>
        /// Gets the independent raw generation alias.
        /// </summary>
        internal PgNativeReference<int> Raw { get; } = raw;

        /// <summary>
        /// Gets the requested row count.
        /// </summary>
        internal int Count { get; } = count;

        /// <summary>
        /// Gets the failing lifecycle stage.
        /// </summary>
        internal int Failure { get; } = failure;

        /// <summary>
        /// Gets or sets the owner name captured while live.
        /// </summary>
        internal string OwnerName { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets whether factory current matched injection.
        /// </summary>
        internal bool FactoryCurrent { get; init; }

        /// <summary>
        /// Gets or sets whether native allocation ownership matched injection.
        /// </summary>
        internal bool ActualOwner { get; init; }

        /// <summary>
        /// Gets or sets the number of enumerator acquisitions.
        /// </summary>
        internal int Enumerators { get; set; }

        /// <summary>
        /// Gets or sets whether GetEnumerator ran in the injected owner.
        /// </summary>
        internal bool EnumeratorCurrent { get; set; }

        /// <summary>
        /// Gets or sets the number of MoveNext calls.
        /// </summary>
        internal int Moves { get; set; }

        /// <summary>
        /// Gets or sets the number of yielded rows.
        /// </summary>
        internal int Rows { get; set; }

        /// <summary>
        /// Gets or sets the number of expired prior scratch allocations.
        /// </summary>
        internal int ExpiredScratch { get; set; }

        /// <summary>
        /// Gets or sets the exact disposal count.
        /// </summary>
        internal int Disposals { get; set; }

        /// <summary>
        /// Gets or sets the copied cleanup observations.
        /// </summary>
        internal string Cleanup { get; set; } = "none";

        /// <summary>
        /// Gets or sets the previous row's checked scratch allocation.
        /// </summary>
        internal PgContextValue<int>? Scratch { get; set; }

        /// <summary>
        /// Gets or sets the maximum observed scratch allocation.
        /// </summary>
        internal nuint MaximumScratch { get; set; }
    }

    /// <summary>
    /// Separates factory invocation from enumerator acquisition.
    /// </summary>
    /// <typeparam name="T">The SQL row representation.</typeparam>
    /// <param name="state">The retained native ownership and observations.</param>
    /// <param name="project">The row projection.</param>
    private sealed class ProbeEnumerable<T>(ProbeState state, Func<int, T> project) : IEnumerable<T>
    {
        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            state.Enumerators++;
            state.EnumeratorCurrent = PgMemoryContext.Current.Id == state.Owner.Id;
            if (state.Failure == 2)
            {
                throw Failure(2);
            }

            return new ProbeEnumerator<T>(state, project);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Exposes exact native ownership at row and cleanup boundaries.
    /// </summary>
    /// <typeparam name="T">The SQL row representation.</typeparam>
    /// <param name="state">The retained native ownership and observations.</param>
    /// <param name="project">The row projection.</param>
    private sealed class ProbeEnumerator<T>(ProbeState state, Func<int, T> project) : IEnumerator<T>
    {
        /// <inheritdoc />
        public T Current => project(state.Value.Value);

        /// <inheritdoc />
        object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => Advance(state);

        /// <inheritdoc />
        public void Reset() => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose() => Cleanup(state);
    }
}
